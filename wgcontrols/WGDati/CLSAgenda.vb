Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports wgcontrols
Imports WGDb

' Gestione dell'agenda, degli appuntamenti, delle categorie e della sincronizzazione dei calendari.
Partial Public Class CLSAgenda
    Private log As New Logger()
    Private Shared _instance As CLSAgenda
    Private Shared ReadOnly _lock As New Object()

    Private Const MaxInputLength As Integer = 500
    Private Const MaxNoteLength As Integer = 4000
    Private Const MaxBatchSize As Integer = 1000
    Private Const SqlInChunkSize As Integer = 500
    Private Const CommandTimeoutSeconds As Integer = 30

    Private Const MaxRetryTentativi As Integer = 5
    Private Const RetryDelayBaseMs As Integer = 50

    Private Const MaxTakePerQuery As Integer = 10000
    Private Const MaxGiorniRangeMappa As Integer = 730
    Private Const MaxRisultatiRicercaSlot As Integer = 500

    Private Const ScrittureTraCheckpoint As Integer = 500
    Private _contatoreScritture As Integer = 0
    Private ReadOnly _checkpointLock As New Object()

    Private ReadOnly connectionString As String
    Private ReadOnly _dbPath As String
    Private _ftsDisponibile As Boolean = False

    Public Enum Frequenza
        Nessuna = 0
        Giornaliera = 1
        Settimanale = 2
        Bisettimanale = 3
        Mensile = 4
        Annuale = 5
    End Enum

    Public Enum TipoOrdinamento
        DataCrescente
        DataDecrescente
        NomeCrescente
        NomeDecrescente
        OggettoCrescente
        OggettoDecrescente
    End Enum

    Public Structure AppuntamentoLista
        Public Property Id As String
        Public Property Oggetto As String
        Public Property Data As String
    End Structure

    Public Structure Appuntamento
        Public Property Id As String
        Public Property Oggetto As String
        Public Property Nome As String
        Public Property Data As String
        Public Property DataFine As String
        Public Property OraInizio As String
        Public Property OraFine As String
        Public Property Categoria As String
        Public Property Frequenza As Frequenza
        Public Property Stato As String
        Public Property Note As String
        Public Property Preavviso As Integer
    End Structure

    Public Structure ElementoSincronizzazioneAgenda
        Public Property Id As String
        Public Property UidServer As String
        Public Property UrlServer As String
        Public Property ChiaveAccount As String
        Public Property Oggetto As String
        Public Property Nome As String
        Public Property Note As String
        Public Property Luogo As String
        Public Property DataInizio As String
        Public Property DataFine As String
        Public Property OraInizio As String
        Public Property OraFine As String
        Public Property Frequenza As Frequenza
    End Structure

    Public Structure AppuntamentoDettaglio
        Public Property DataInizio As String
        Public Property DataFine As String
        Public Property OraInizio As String
        Public Property OraFine As String
        Public Property IdEscluso As String
    End Structure

    ' Impostazione di sincronizzazione per un singolo calendario CalDAV di un account (o, quando
    ' UrlCalendario e' vuoto, impostazione di ripiego per l'intero account): in quale Categoria
    ' locale devono fluire gli appuntamenti importati da quel calendario, e se quel calendario va
    ' incluso tra quelli da cui scaricare gli eventi (Selezionato). Vedi RisolviCategoriaSincronizzazione
    ' e OttieniCalendariSelezionati.
    Public Class ImpostazioneCalendarioSincronizzazione
        Public Property ChiaveAccount As String = ""
        Public Property UrlCalendario As String = ""
        Public Property NomeCalendario As String = ""
        Public Property Categoria As String = ""
        Public Property Selezionato As Boolean = True
    End Class

    Private Shared Function RisolviCartellaAgenda() As String
        Dim cartella As String = WGDatabase.CartellaDati("agenda")

        Try
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Return cartella
    End Function

    Private Sub New()
        Try
            If log Is Nothing Then
                log = New Logger()
            End If
            Dim dbDir As String = RisolviCartellaAgenda()
            If Not Directory.Exists(dbDir) Then
                Directory.CreateDirectory(dbDir)
            End If
            _dbPath = Path.Combine(dbDir, "agenda.db")

            connectionString =
                "Data Source=" & _dbPath & ";" &
                "Version=3;" &
                "Journal Mode=WAL;" &
                "Synchronous=Normal;" &
                "Cache Size=-65536;" &
                "Page Size=4096;" &
                "Pooling=True;" &
                "Max Pool Size=64;" &
                "BusyTimeout=5000;" &
                "Foreign Keys=True;"

            InizializzaDatabase()
            PreparaTabelleAgende()
            PreparaSincronizzazioneFile()
            MarcaSempreLocaliPerSincronizzazione()
        Catch ex As Exception
            If log IsNot Nothing Then
                log.ScriviLog("CLSAgenda.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
            End If
        End Try
    End Sub

    ' All'avvio dell'agenda, ogni elemento che NON viene dal calendario (CalDAV) - cioe' senza
    ' ChiaveAccountOrigine, quindi creato/modificato solo in locale - va sempre marcato per la
    ' sincronizzazione: se per qualsiasi motivo (bug, sincronizzazione interrotta, modifica diretta
    ' sul database) un elemento locale fosse rimasto con DaSincronizzare = 0, a ogni avvio lo
    ' rimettiamo in coda invece di rischiare che resti silenziosamente mai inviato al server.
    Private Sub MarcaSempreLocaliPerSincronizzazione()
        Try
            EseguiConRetry(Function()
                                Using conn As SQLiteConnection = ApriConnessione()
                                    Using cmd As New SQLiteCommand(
                                        "UPDATE Appuntamenti SET DaSincronizzare = 1 " &
                                        "WHERE (ChiaveAccountOrigine IS NULL OR ChiaveAccountOrigine = '') AND DaSincronizzare != 1", conn)
                                        cmd.CommandTimeout = CommandTimeoutSeconds
                                        cmd.ExecuteNonQuery()
                                    End Using
                                End Using
                                Return True
                            End Function, "MarcaSempreLocaliPerSincronizzazione")
            SegnalaScritturaPerCheckpoint()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.MarcaSempreLocaliPerSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function GetInstance() As CLSAgenda
        If _instance Is Nothing Then
            SyncLock _lock
                If _instance Is Nothing Then
                    _instance = New CLSAgenda()
                End If
            End SyncLock
        End If
        Return _instance
    End Function

    Public Shared Sub ReimpostaIstanza()
        Try
            SyncLock _lock
                _instance = Nothing
            End SyncLock
        Catch ex As Exception
            Try
                Dim logReimposta As New Logger()
                logReimposta.ScriviLog("CLSAgenda.ReimpostaIstanza - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Catch
            End Try
        End Try
    End Sub

    Private Function ApriConnessione() As SQLiteConnection
        Try
            Dim conn As SQLiteConnection = WGDatabase.ApriConnessione(_dbPath, "Version=3;Journal Mode=WAL;Synchronous=Normal;Cache Size=-65536;Page Size=4096;Pooling=True;Max Pool Size=64;BusyTimeout=5000;Foreign Keys=True;")
            If conn Is Nothing Then
                conn = New SQLiteConnection(connectionString)
                conn.Open()
            End If
            Using cmd As New SQLiteCommand(conn)
                cmd.CommandTimeout = CommandTimeoutSeconds
                cmd.CommandText = "PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -65536; PRAGMA foreign_keys = ON;"
                cmd.ExecuteNonQuery()
            End Using
            Return conn
        Catch ex As Exception
            log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub InizializzaDatabase()
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds

                    cmd.CommandText = "PRAGMA temp_store = MEMORY;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "PRAGMA mmap_size = 268435456;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "PRAGMA page_size = 4096;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "PRAGMA wal_autocheckpoint = 1000;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Categorie (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT UNIQUE NOT NULL);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Appuntamenti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Oggetto TEXT NOT NULL, Nome TEXT, DataInizio TEXT NOT NULL, DataFine TEXT, OraInizio TEXT, OraFine TEXT, Categoria TEXT, Frequenza INTEGER DEFAULT 0, Stato TEXT DEFAULT 'attivo', Note TEXT, Preavviso INTEGER DEFAULT 0);"
                    cmd.ExecuteNonQuery()

                    Try
                        WGDatabase.AssicuraColonnaChiave(conn, "Categorie")
                        WGDatabase.AssicuraColonnaChiave(conn, "Appuntamenti")
                    Catch exChiave As Exception
                        log.ScriviLog("CLSAgenda.InizializzaDatabase chiave - " & exChiave.ToString())
                    End Try

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS AppuntamentiEliminati (Id INTEGER PRIMARY KEY AUTOINCREMENT, UidServer TEXT, UrlServer TEXT, ChiaveAccount TEXT, DataEliminazione TEXT);"
                    cmd.ExecuteNonQuery()

                    ' Impostazioni scelte dall'utente su in quale Categoria devono fluire gli
                    ' appuntamenti sincronizzati: una riga con UrlCalendario = '' rappresenta la
                    ' categoria di ripiego per l'intero account, una riga con UrlCalendario valorizzato
                    ' rappresenta la scelta (categoria + inclusione nella sincronizzazione) per un
                    ' singolo calendario di quell'account. Vedi RisolviCategoriaSincronizzazione.
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS ImpostazioniSincronizzazioneCalendari (Id INTEGER PRIMARY KEY AUTOINCREMENT, ChiaveAccount TEXT NOT NULL, UrlCalendario TEXT NOT NULL DEFAULT '', NomeCalendario TEXT, Categoria TEXT, Selezionato INTEGER DEFAULT 1, UNIQUE(ChiaveAccount, UrlCalendario));"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_sync_cal_account ON ImpostazioniSincronizzazioneCalendari(ChiaveAccount);"
                    cmd.ExecuteNonQuery()

                    AssicuraColonneSincronizzazione(cmd)

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_datainizio ON Appuntamenti(DataInizio);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_categoria ON Appuntamenti(Categoria);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_stato ON Appuntamenti(Stato);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_frequenza ON Appuntamenti(Frequenza);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_ricerca ON Appuntamenti(DataInizio, OraInizio, Stato);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_ord_data ON Appuntamenti(DataInizio, OraInizio);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_ord_nome ON Appuntamenti(Nome);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_ord_ogg ON Appuntamenti(Oggetto);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_cat_data ON Appuntamenti(Categoria, DataInizio, OraInizio);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_stato_data ON Appuntamenti(Stato, DataInizio, OraFine);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_cat_stato_data ON Appuntamenti(Categoria, Stato, DataInizio, OraInizio);"
                    cmd.ExecuteNonQuery()

                    InizializzaRicercaTestuale(cmd)

                    cmd.CommandText = "PRAGMA optimize;"
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            AssicuraCategoriePredefinite()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.InizializzaDatabase - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AssicuraColonneSincronizzazione(cmd As SQLiteCommand)
        Try
            AggiungiColonnaSeManca(cmd, "Appuntamenti", "UidServer", "TEXT")
            AggiungiColonnaSeManca(cmd, "Appuntamenti", "UrlServer", "TEXT")
            AggiungiColonnaSeManca(cmd, "Appuntamenti", "ChiaveAccountOrigine", "TEXT")
            AggiungiColonnaSeManca(cmd, "Appuntamenti", "DaSincronizzare", "INTEGER DEFAULT 0")
            AggiungiColonnaSeManca(cmd, "Appuntamenti", "Luogo", "TEXT")

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_uidserver ON Appuntamenti(UidServer);"
            cmd.ExecuteNonQuery()
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_appuntamenti_dasincronizzare ON Appuntamenti(DaSincronizzare);"
            cmd.ExecuteNonQuery()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AssicuraColonneSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ColonnaPresente(cmd As SQLiteCommand, tabella As String, colonna As String) As Boolean
        Try
            Dim testoOriginale As String = cmd.CommandText
            cmd.CommandText = "PRAGMA table_info(" & tabella & ");"
            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                While reader.Read()
                    Dim nomeColonna As String = reader("name").ToString()
                    If String.Equals(nomeColonna, colonna, StringComparison.OrdinalIgnoreCase) Then
                        reader.Close()
                        cmd.CommandText = testoOriginale
                        Return True
                    End If
                End While
            End Using
            cmd.CommandText = testoOriginale
            Return False
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ColonnaPresente (" & tabella & "." & colonna & ") - " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub AggiungiColonnaSeManca(cmd As SQLiteCommand, tabella As String, colonna As String, definizione As String)
        Try
            If ColonnaPresente(cmd, tabella, colonna) Then Return
            cmd.CommandText = "ALTER TABLE " & tabella & " ADD COLUMN " & colonna & " " & definizione & ";"
            cmd.ExecuteNonQuery()
        Catch exColonna As Exception
            log.ScriviLog("CLSAgenda.AggiungiColonnaSeManca (" & tabella & "." & colonna & ") - " & exColonna.Message)
        End Try
    End Sub

    Private Sub AssicuraCategoriePredefinite()
        Try
            Dim categorieEsistenti As List(Of String) = OttieniElencoCategorie()
            If categorieEsistenti IsNot Nothing AndAlso categorieEsistenti.Count > 0 Then Return

            AggiungiCategoria("Lavoro")
            AggiungiCategoria("Personale")
            AggiungiCategoria("Urgente")
            AggiungiCategoria("Famiglia")
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AssicuraCategoriePredefinite - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InizializzaRicercaTestuale(cmd As SQLiteCommand)
        Try
            cmd.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS Appuntamenti_FTS USING fts5(Oggetto, Nome, Note, content='Appuntamenti', content_rowid='Id', tokenize='unicode61 remove_diacritics 2');"
            cmd.ExecuteNonQuery()

            cmd.CommandText = "CREATE TRIGGER IF NOT EXISTS trg_appuntamenti_ai AFTER INSERT ON Appuntamenti BEGIN " &
                               "INSERT INTO Appuntamenti_FTS(rowid, Oggetto, Nome, Note) VALUES (new.Id, new.Oggetto, new.Nome, new.Note); END;"
            cmd.ExecuteNonQuery()

            cmd.CommandText = "CREATE TRIGGER IF NOT EXISTS trg_appuntamenti_ad AFTER DELETE ON Appuntamenti BEGIN " &
                               "INSERT INTO Appuntamenti_FTS(Appuntamenti_FTS, rowid, Oggetto, Nome, Note) VALUES('delete', old.Id, old.Oggetto, old.Nome, old.Note); END;"
            cmd.ExecuteNonQuery()

            cmd.CommandText = "DROP TRIGGER IF EXISTS trg_appuntamenti_au;"
            cmd.ExecuteNonQuery()

            cmd.CommandText = "CREATE TRIGGER IF NOT EXISTS trg_appuntamenti_testo_au AFTER UPDATE OF Oggetto, Nome, Note ON Appuntamenti BEGIN " &
                               "INSERT INTO Appuntamenti_FTS(Appuntamenti_FTS, rowid, Oggetto, Nome, Note) VALUES('delete', old.Id, old.Oggetto, old.Nome, old.Note); " &
                               "INSERT INTO Appuntamenti_FTS(rowid, Oggetto, Nome, Note) VALUES (new.Id, new.Oggetto, new.Nome, new.Note); END;"
            cmd.ExecuteNonQuery()

            cmd.CommandText = "INSERT INTO Appuntamenti_FTS(rowid, Oggetto, Nome, Note) " &
                               "SELECT a.Id, a.Oggetto, a.Nome, a.Note FROM Appuntamenti a " &
                               "WHERE NOT EXISTS (SELECT 1 FROM Appuntamenti_FTS f WHERE f.rowid = a.Id);"
            cmd.ExecuteNonQuery()

            _ftsDisponibile = True
        Catch ex As Exception
            _ftsDisponibile = False
            log.ScriviLog("CLSAgenda.InizializzaRicercaTestuale - FTS5 non disponibile, uso fallback LIKE - " & ex.Message)
        End Try
    End Sub

    Private Function EseguiConRetry(Of T)(operazione As Func(Of T), nomeOperazione As String) As T
        Dim tentativo As Integer = 0
        Do
            Try
                Return operazione()
            Catch ex As SQLiteException
                Dim transitorio As Boolean =
                    ex.ResultCode = SQLiteErrorCode.Busy OrElse
                    ex.ResultCode = SQLiteErrorCode.Locked

                tentativo += 1
                If Not transitorio OrElse tentativo >= MaxRetryTentativi Then
                    log.ScriviLog($"CLSAgenda.EseguiConRetry [{nomeOperazione}] - tentativo {tentativo} fallito definitivamente - {ex.Message}")
                    Throw
                End If

                Dim attesaMs As Integer = RetryDelayBaseMs * (1 << (tentativo - 1))
                log.ScriviLog($"CLSAgenda.EseguiConRetry [{nomeOperazione}] - {ex.ResultCode} al tentativo {tentativo}, riprovo tra {attesaMs}ms")
                Thread.Sleep(attesaMs)
            End Try
        Loop
    End Function

    Private Sub SegnalaScritturaPerCheckpoint()
        Dim devoCheckpoint As Boolean = False
        SyncLock _checkpointLock
            _contatoreScritture += 1
            If _contatoreScritture >= ScrittureTraCheckpoint Then
                _contatoreScritture = 0
                devoCheckpoint = True
            End If
        End SyncLock

        If Not devoCheckpoint Then Return

        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("PRAGMA wal_checkpoint(PASSIVE);", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SegnalaScritturaPerCheckpoint - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ConvertiDataDB(data As String) As String
        Try
            If String.IsNullOrWhiteSpace(data) Then
                Return ""
            End If
            Dim d As DateTime
            If DateTime.TryParseExact(data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
                Return d.ToString("yyyy-MM-dd")
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ConvertiDataDB - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function ConvertiDataIT(data As String) As String
        Try
            If String.IsNullOrWhiteSpace(data) Then
                Return ""
            End If
            Dim d As DateTime
            If DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
                Return d.ToString("dd/MM/yyyy")
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ConvertiDataIT - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    ''' <summary>Il testo di un campo senza spazi ai lati e tagliato alla lunghezza massima consentita.</summary>
    Private Function SanitizzaInput(input As String) As String
        Try
            If String.IsNullOrWhiteSpace(input) Then
                Return ""
            End If
            Dim pulito As String = input.Trim()
            If pulito.Length > MaxInputLength Then
                Return pulito.Substring(0, MaxInputLength)
            End If
            Return pulito
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SanitizzaInput: " & ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>Le note senza spazi ai lati e tagliate alla lunghezza massima consentita.</summary>
    Private Function SanitizzaNote(input As String) As String
        Try
            If String.IsNullOrWhiteSpace(input) Then
                Return ""
            End If
            Dim pulito As String = input.Trim()
            If pulito.Length > MaxNoteLength Then
                Return pulito.Substring(0, MaxNoteLength)
            End If
            Return pulito
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SanitizzaNote: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function OttieniElencoCategorie() As List(Of String)
        Dim lista As New List(Of String)()
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT Nome FROM Categorie ORDER BY Nome COLLATE NOCASE ASC", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            If Not reader.IsDBNull(0) Then
                                lista.Add(reader.GetString(0))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniElencoCategorie - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Function AggiungiCategoria(nome As String) As Boolean
        Try
            nome = SanitizzaInput(nome)
            If String.IsNullOrWhiteSpace(nome) Then
                Return False
            End If
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("INSERT OR IGNORE INTO Categorie (Nome, ChiaveWG) VALUES (@Nome, @ChiaveWG)", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Nome", nome)
                                                                  cmd.Parameters.AddWithValue("@ChiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Categorie"))
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "AggiungiCategoria")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiungiCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function ModificaCategoria(vecchioNome As String, nuovoNome As String) As Boolean
        Try
            vecchioNome = SanitizzaInput(vecchioNome)
            nuovoNome = SanitizzaInput(nuovoNome)
            If String.IsNullOrWhiteSpace(vecchioNome) OrElse String.IsNullOrWhiteSpace(nuovoNome) Then
                Return False
            End If
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Dim trans As SQLiteTransaction = Nothing
                                                              Try
                                                                  trans = conn.BeginTransaction()
                                                                  Using cmd As New SQLiteCommand("UPDATE Categorie SET Nome = @NuovoNome WHERE Nome = @VecchioNome", conn, trans)
                                                                      cmd.CommandTimeout = CommandTimeoutSeconds
                                                                      cmd.Parameters.AddWithValue("@NuovoNome", nuovoNome)
                                                                      cmd.Parameters.AddWithValue("@VecchioNome", vecchioNome)
                                                                      cmd.ExecuteNonQuery()
                                                                  End Using
                                                                  Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Categoria = @NuovoNome WHERE Categoria = @VecchioNome", conn, trans)
                                                                      cmd.CommandTimeout = CommandTimeoutSeconds
                                                                      cmd.Parameters.AddWithValue("@NuovoNome", nuovoNome)
                                                                      cmd.Parameters.AddWithValue("@VecchioNome", vecchioNome)
                                                                      cmd.ExecuteNonQuery()
                                                                  End Using
                                                                  trans.Commit()
                                                                  Return True
                                                              Catch
                                                                  If trans IsNot Nothing Then
                                                                      Try
                                                                          trans.Rollback()
                                                                      Catch exRb As Exception
                                                                          log.ScriviLog("CLSAgenda.ModificaCategoria Rollback - " & exRb.Message & Environment.NewLine & exRb.StackTrace)
                                                                      End Try
                                                                  End If
                                                                  Throw
                                                              Finally
                                                                  If trans IsNot Nothing Then
                                                                      trans.Dispose()
                                                                  End If
                                                              End Try
                                                          End Using
                                                      End Function, "ModificaCategoria")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ModificaCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function EliminaCategoria(nome As String) As Boolean
        Try
            nome = SanitizzaInput(nome)
            If String.IsNullOrWhiteSpace(nome) Then
                Return False
            End If
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("DELETE FROM Categorie WHERE Nome = @Nome", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Nome", nome)
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "EliminaCategoria")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EliminaCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function ContaAvvisiNotifiche() As Tuple(Of Integer, Integer)
        Dim scaduti As Integer = 0
        Dim inScadenza As Integer = 0
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Appuntamenti WHERE Stato != 'passato' AND (DataInizio < @Oggi OR (DataInizio = @Oggi AND OraFine < @Ora))", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Oggi", oggiDb)
                    cmd.Parameters.AddWithValue("@Ora", oraDb)
                    scaduti = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Appuntamenti WHERE Stato != 'passato' AND DataInizio = @Oggi AND OraInizio >= @Ora", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Oggi", oggiDb)
                    cmd.Parameters.AddWithValue("@Ora", oraDb)
                    inScadenza = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ContaAvvisiNotifiche - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return New Tuple(Of Integer, Integer)(scaduti, inScadenza)
    End Function

    Public Function ContaAppuntamentiFuturi() As Integer
        Dim count As Integer = 0
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Appuntamenti WHERE DataInizio > @Oggi OR (DataInizio = @Oggi AND OraInizio >= @Ora)", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Oggi", oggiDb)
                    cmd.Parameters.AddWithValue("@Ora", oraDb)
                    count = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ContaAppuntamentiFuturi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return count
    End Function

    Public Function ContaAppuntamentiInScadenza() As Integer
        Dim count As Integer = 0
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Appuntamenti WHERE Stato != 'passato' AND DataInizio = @Oggi AND OraInizio >= @Ora", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Oggi", oggiDb)
                    cmd.Parameters.AddWithValue("@Ora", oraDb)
                    count = Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ContaAppuntamentiInScadenza - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return count
    End Function

    Public Function SegnaPassati(dataOdiernaStr As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(dataOdiernaStr) Then
                Return False
            End If
            Dim parsedDate As DateTime
            If Not DateTime.TryParseExact(dataOdiernaStr, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, parsedDate) Then
                Return False
            End If
            Dim dataDb As String = parsedDate.ToString("yyyy-MM-dd")
            Dim oraDb As String = parsedDate.ToString("HH:mm")
            EseguiConRetry(Function()
                               Using conn As SQLiteConnection = ApriConnessione()
                                   Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Stato = 'passato' WHERE Stato != 'passato' AND (DataInizio < @Data OR (DataInizio = @Data AND OraFine < @Ora))", conn)
                                       cmd.CommandTimeout = CommandTimeoutSeconds
                                       cmd.Parameters.AddWithValue("@Data", dataDb)
                                       cmd.Parameters.AddWithValue("@Ora", oraDb)
                                       cmd.ExecuteNonQuery()
                                   End Using
                               End Using
                               Return True
                           End Function, "SegnaPassati")
            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SegnaPassati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function SegnaPassatiMassivi(ids As List(Of String)) As Boolean
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then
                Return False
            End If
            If ids.Count > MaxBatchSize Then
                Return False
            End If
            Dim validIds As New List(Of Long)()
            For Each id As String In ids
                Dim parsed As Long
                If Long.TryParse(id, parsed) AndAlso parsed > 0 Then
                    validIds.Add(parsed)
                End If
            Next
            If validIds.Count = 0 Then
                Return False
            End If

            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Dim trans As SQLiteTransaction = Nothing
                                                              Try
                                                                  trans = conn.BeginTransaction()
                                                                  Dim chunkSize As Integer = SqlInChunkSize
                                                                  Dim i As Integer = 0
                                                                  While i < validIds.Count
                                                                      Dim chunk As List(Of Long) = validIds.GetRange(i, Math.Min(chunkSize, validIds.Count - i))

                                                                      Dim nomiParametri As New List(Of String)(chunk.Count)
                                                                      Using cmdChunk As New SQLiteCommand(conn)
                                                                          cmdChunk.Transaction = trans
                                                                          cmdChunk.CommandTimeout = CommandTimeoutSeconds
                                                                          For idxParam As Integer = 0 To chunk.Count - 1
                                                                              Dim nomeParam As String = "@Id" & idxParam.ToString()
                                                                              nomiParametri.Add(nomeParam)
                                                                              cmdChunk.Parameters.AddWithValue(nomeParam, chunk(idxParam))
                                                                          Next
                                                                          cmdChunk.CommandText = "UPDATE Appuntamenti SET Stato = 'passato' WHERE Id IN (" & String.Join(",", nomiParametri) & ")"
                                                                          cmdChunk.ExecuteNonQuery()
                                                                      End Using
                                                                      i += chunkSize
                                                                  End While
                                                                  trans.Commit()
                                                                  Return True
                                                              Catch
                                                                  If trans IsNot Nothing Then
                                                                      Try
                                                                          trans.Rollback()
                                                                      Catch exRb As Exception
                                                                          log.ScriviLog("CLSAgenda.SegnaPassatiMassivi Rollback - " & exRb.Message & Environment.NewLine & exRb.StackTrace)
                                                                      End Try
                                                                  End If
                                                                  Throw
                                                              Finally
                                                                  If trans IsNot Nothing Then
                                                                      trans.Dispose()
                                                                  End If
                                                              End Try
                                                          End Using
                                                      End Function, "SegnaPassatiMassivi")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SegnaPassatiMassivi - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function OttieniListaAppuntamentiDaQuery(query As String, parameters As Dictionary(Of String, Object), skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Dim lista As New List(Of AppuntamentoLista)()
        Try
            If skip < 0 Then skip = 0
            If take <= 0 OrElse take > MaxTakePerQuery Then take = MaxTakePerQuery

            Dim orderBy As String = ""
            Select Case ord
                Case TipoOrdinamento.DataCrescente
                    orderBy = " ORDER BY DataInizio ASC, OraInizio ASC "
                Case TipoOrdinamento.DataDecrescente
                    orderBy = " ORDER BY DataInizio DESC, OraInizio DESC "
                Case TipoOrdinamento.NomeCrescente
                    orderBy = " ORDER BY Nome COLLATE NOCASE ASC "
                Case TipoOrdinamento.NomeDecrescente
                    orderBy = " ORDER BY Nome COLLATE NOCASE DESC "
                Case TipoOrdinamento.OggettoCrescente
                    orderBy = " ORDER BY Oggetto COLLATE NOCASE ASC "
                Case TipoOrdinamento.OggettoDecrescente
                    orderBy = " ORDER BY Oggetto COLLATE NOCASE DESC "
                Case Else
                    orderBy = " ORDER BY DataInizio ASC, OraInizio ASC "
            End Select

            ' Le consultazioni mostrano soltanto gli appuntamenti dell'agenda scelta, e mai
            ' quelli delle agende protette ancora chiuse.
            Dim parametriEffettivi As Dictionary(Of String, Object) = parameters
            If parametriEffettivi Is Nothing Then parametriEffettivi = New Dictionary(Of String, Object)()
            Dim queryFiltrata As String = ApplicaFiltroAgenda(query, parametriEffettivi)
            Dim finalQuery As String = queryFiltrata & orderBy & " LIMIT @Take OFFSET @Skip"

            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(finalQuery, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    If parametriEffettivi IsNot Nothing Then
                        For Each kvp As KeyValuePair(Of String, Object) In parametriEffettivi
                            cmd.Parameters.AddWithValue(kvp.Key, kvp.Value)
                        Next
                    End If
                    cmd.Parameters.AddWithValue("@Take", take)
                    cmd.Parameters.AddWithValue("@Skip", skip)

                    Using reader As SQLiteDataReader = cmd.ExecuteReader(CommandBehavior.SequentialAccess)
                        Dim ordId As Integer = reader.GetOrdinal("Id")
                        Dim ordOggetto As Integer = reader.GetOrdinal("Oggetto")
                        Dim ordData As Integer = reader.GetOrdinal("DataInizio")
                        While reader.Read()
                            If token.IsCancellationRequested Then
                                Exit While
                            End If
                            Dim a As New AppuntamentoLista()
                            a.Id = If(reader.IsDBNull(ordId), "", reader.GetValue(ordId).ToString())
                            a.Oggetto = TestoLeggibile(If(reader.IsDBNull(ordOggetto), "", reader.GetString(ordOggetto)))
                            a.Data = ConvertiDataIT(If(reader.IsDBNull(ordData), "", reader.GetString(ordData)))
                            lista.Add(a)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniListaAppuntamentiDaQuery - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Function OttieniAppuntamentiInScadenzaPaginati(skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Stato != 'passato' AND DataInizio = @Oggi AND OraInizio >= @Ora"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Oggi", oggiDb)
            p.Add("@Ora", oraDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiInScadenzaPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiInScadenzaPaginati(skip As Integer, take As Integer) As List(Of AppuntamentoLista)
        Return OttieniAppuntamentiInScadenzaPaginati(skip, take, CancellationToken.None, TipoOrdinamento.DataCrescente)
    End Function

    Public Function OttieniAppuntamentiInScadenza() As List(Of AppuntamentoLista)
        Return OttieniAppuntamentiInScadenzaPaginati(0, 1000, CancellationToken.None, TipoOrdinamento.DataCrescente)
    End Function

    Public Function OttieniAppuntamentiScadutiPaginati(skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Stato != 'passato' AND (DataInizio < @Oggi OR (DataInizio = @Oggi AND OraFine < @Ora))"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Oggi", oggiDb)
            p.Add("@Ora", oraDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiScadutiPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiInScadenzaPerCategoriaPaginati(cat As String, skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            cat = SanitizzaInput(cat)
            If String.IsNullOrWhiteSpace(cat) Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Categoria = @Cat AND Stato != 'passato' AND DataInizio = @Oggi AND OraInizio >= @Ora"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Cat", cat)
            p.Add("@Oggi", oggiDb)
            p.Add("@Ora", oraDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiInScadenzaPerCategoriaPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiScadutiPerCategoriaPaginati(cat As String, skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            cat = SanitizzaInput(cat)
            If String.IsNullOrWhiteSpace(cat) Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Categoria = @Cat AND Stato != 'passato' AND (DataInizio < @Oggi OR (DataInizio = @Oggi AND OraFine < @Ora))"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Cat", cat)
            p.Add("@Oggi", oggiDb)
            p.Add("@Ora", oraDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiScadutiPerCategoriaPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function VerificaSovrapposizioni(dettaglio As AppuntamentoDettaglio) As Boolean
        Try
            If String.IsNullOrWhiteSpace(dettaglio.DataInizio) OrElse String.IsNullOrWhiteSpace(dettaglio.OraInizio) OrElse String.IsNullOrWhiteSpace(dettaglio.OraFine) Then
                Return False
            End If
            Dim dataDb As String = ConvertiDataDB(dettaglio.DataInizio)
            If String.IsNullOrWhiteSpace(dataDb) Then
                Return False
            End If
            Dim escludiId As Boolean = Not String.IsNullOrWhiteSpace(dettaglio.IdEscluso)
            Dim q As String = "SELECT COUNT(*) FROM Appuntamenti WHERE DataInizio = @Data AND ((OraInizio < @OraFine AND OraFine > @OraInizio))"
            If escludiId Then
                q &= " AND Id != @IdEscluso"
            End If
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand(q, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Data", dataDb)
                    cmd.Parameters.AddWithValue("@OraInizio", dettaglio.OraInizio)
                    cmd.Parameters.AddWithValue("@OraFine", dettaglio.OraFine)
                    If escludiId Then
                        Dim idVal As Long
                        If Long.TryParse(dettaglio.IdEscluso, idVal) Then
                            cmd.Parameters.AddWithValue("@IdEscluso", idVal)
                        Else
                            Return False
                        End If
                    End If
                    Dim count As Integer = Convert.ToInt32(cmd.ExecuteScalar())
                    Return count > 0
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.VerificaSovrapposizioni - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function AggiungiAppuntamento(oggetto As String, nome As String, oraIni As String, oraFin As String, note As String, preavviso As Integer, freq As Frequenza, data As String, cat As String, dataFine As String) As Boolean
        Try
            oggetto = SanitizzaInput(oggetto)
            If String.IsNullOrWhiteSpace(oggetto) OrElse String.IsNullOrWhiteSpace(data) Then
                Return False
            End If
            Dim dIni As String = ConvertiDataDB(data)
            If String.IsNullOrWhiteSpace(dIni) Then
                Return False
            End If
            Dim dFin As String = ConvertiDataDB(dataFine)
            If String.IsNullOrWhiteSpace(dFin) Then
                dFin = dIni
            End If
            nome = SanitizzaInput(nome)
            note = SanitizzaNote(note)
            cat = SanitizzaInput(cat)
            If preavviso < 0 Then preavviso = 0

            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Dim agendaScelta As String = AgendaPerNuoviAppuntamenti()
                                                              If Not AgendaPuoRicevereTesto(agendaScelta) Then Return False
                                                              Using cmd As New SQLiteCommand("INSERT INTO Appuntamenti (Oggetto, Nome, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Stato, Note, Preavviso, ChiaveWG, DaSincronizzare, ChiaveAgenda) VALUES (@Oggetto, @Nome, @DataInizio, @DataFine, @OraInizio, @OraFine, @Categoria, @Frequenza, 'attivo', @Note, @Preavviso, @ChiaveWG, 1, @ChiaveAgenda)", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@ChiaveAgenda", agendaScelta)
                                                                  cmd.Parameters.AddWithValue("@Oggetto", TestoDaSalvare(oggetto, agendaScelta))
                                                                  cmd.Parameters.AddWithValue("@ChiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Appuntamenti"))
                                                                  cmd.Parameters.AddWithValue("@Nome", If(String.IsNullOrWhiteSpace(nome), DBNull.Value, CObj(TestoDaSalvare(nome, agendaScelta))))
                                                                  cmd.Parameters.AddWithValue("@DataInizio", dIni)
                                                                  cmd.Parameters.AddWithValue("@DataFine", dFin)
                                                                  cmd.Parameters.AddWithValue("@OraInizio", If(String.IsNullOrWhiteSpace(oraIni), DBNull.Value, CObj(oraIni)))
                                                                  cmd.Parameters.AddWithValue("@OraFine", If(String.IsNullOrWhiteSpace(oraFin), DBNull.Value, CObj(oraFin)))
                                                                  cmd.Parameters.AddWithValue("@Categoria", If(String.IsNullOrWhiteSpace(cat), DBNull.Value, CObj(cat)))
                                                                  cmd.Parameters.AddWithValue("@Frequenza", CType(freq, Integer))
                                                                  cmd.Parameters.AddWithValue("@Note", If(String.IsNullOrWhiteSpace(note), DBNull.Value, CObj(TestoDaSalvare(note, agendaScelta))))
                                                                  cmd.Parameters.AddWithValue("@Preavviso", preavviso)
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "AggiungiAppuntamento")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiungiAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function ModificaAppuntamento(id As String, oggetto As String, nome As String, oraIni As String, oraFin As String, note As String, preavviso As Integer, freq As Frequenza, data As String, cat As String, dataFine As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then
                Return False
            End If
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then
                Return False
            End If
            oggetto = SanitizzaInput(oggetto)
            If String.IsNullOrWhiteSpace(oggetto) OrElse String.IsNullOrWhiteSpace(data) Then
                Return False
            End If
            Dim dIni As String = ConvertiDataDB(data)
            If String.IsNullOrWhiteSpace(dIni) Then
                Return False
            End If
            Dim dFin As String = ConvertiDataDB(dataFine)
            If String.IsNullOrWhiteSpace(dFin) Then
                dFin = dIni
            End If
            nome = SanitizzaInput(nome)
            note = SanitizzaNote(note)
            cat = SanitizzaInput(cat)
            If preavviso < 0 Then preavviso = 0

            Dim agendaAppuntamento As String = AgendaDellAppuntamento(id)
            If Not AgendaPuoRicevereTesto(agendaAppuntamento) Then Return False
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Oggetto = @Oggetto, Nome = @Nome, DataInizio = @DataInizio, DataFine = @DataFine, OraInizio = @OraInizio, OraFine = @OraFine, Categoria = @Categoria, Frequenza = @Frequenza, Note = @Note, Preavviso = @Preavviso, DaSincronizzare = 1 WHERE Id = @Id", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Oggetto", TestoDaSalvare(oggetto, agendaAppuntamento))
                                                                  cmd.Parameters.AddWithValue("@Nome", If(String.IsNullOrWhiteSpace(nome), DBNull.Value, CObj(TestoDaSalvare(nome, agendaAppuntamento))))
                                                                  cmd.Parameters.AddWithValue("@DataInizio", dIni)
                                                                  cmd.Parameters.AddWithValue("@DataFine", dFin)
                                                                  cmd.Parameters.AddWithValue("@OraInizio", If(String.IsNullOrWhiteSpace(oraIni), DBNull.Value, CObj(oraIni)))
                                                                  cmd.Parameters.AddWithValue("@OraFine", If(String.IsNullOrWhiteSpace(oraFin), DBNull.Value, CObj(oraFin)))
                                                                  cmd.Parameters.AddWithValue("@Categoria", If(String.IsNullOrWhiteSpace(cat), DBNull.Value, CObj(cat)))
                                                                  cmd.Parameters.AddWithValue("@Frequenza", CType(freq, Integer))
                                                                  cmd.Parameters.AddWithValue("@Note", If(String.IsNullOrWhiteSpace(note), DBNull.Value, CObj(TestoDaSalvare(note, agendaAppuntamento))))
                                                                  cmd.Parameters.AddWithValue("@Preavviso", preavviso)
                                                                  cmd.Parameters.AddWithValue("@Id", idVal)
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "ModificaAppuntamento")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ModificaAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function EliminaAppuntamento(id As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then
                Return False
            End If
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then
                Return False
            End If
            RegistraEliminazioneAgenda(idVal)

            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("DELETE FROM Appuntamenti WHERE Id = @Id", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Id", idVal)
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "EliminaAppuntamento")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EliminaAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub RegistraEliminazioneAgenda(idVal As Long)
        Try
            Dim uid As String = ""
            Dim url As String = ""
            Dim account As String = ""

            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT UidServer, UrlServer, ChiaveAccountOrigine FROM Appuntamenti WHERE Id = @Id", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Id", idVal)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            If Not reader.IsDBNull(0) Then uid = reader.GetValue(0).ToString()
                            If Not reader.IsDBNull(1) Then url = reader.GetValue(1).ToString()
                            If Not reader.IsDBNull(2) Then account = reader.GetValue(2).ToString()
                        End If
                    End Using
                End Using

                If String.IsNullOrWhiteSpace(account) Then Return
                If String.IsNullOrWhiteSpace(uid) AndAlso String.IsNullOrWhiteSpace(url) Then Return

                Using cmdIns As New SQLiteCommand("INSERT INTO AppuntamentiEliminati (UidServer, UrlServer, ChiaveAccount, DataEliminazione) VALUES (@Uid, @Url, @Account, @Data)", conn)
                    cmdIns.CommandTimeout = CommandTimeoutSeconds
                    cmdIns.Parameters.AddWithValue("@Uid", uid)
                    cmdIns.Parameters.AddWithValue("@Url", url)
                    cmdIns.Parameters.AddWithValue("@Account", account)
                    cmdIns.Parameters.AddWithValue("@Data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    cmdIns.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RegistraEliminazioneAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function EliminaAppuntamentiMassivi(ids As List(Of String)) As Integer
        Dim contatore As Integer = 0
        Try
            If ids Is Nothing OrElse ids.Count = 0 Then Return 0
            For Each id As String In ids
                Try
                    If String.IsNullOrWhiteSpace(id) Then Continue For
                    If EliminaAppuntamento(id) Then contatore += 1
                Catch exId As Exception
                    log.ScriviLog("CLSAgenda.EliminaAppuntamentiMassivi (" & If(id, "") & ") - " & exId.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EliminaAppuntamentiMassivi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return contatore
    End Function

    Public Function TrovaAppuntamentoPerUidServer(chiaveAccount As String, uidServer As String) As String
        Try
            If String.IsNullOrWhiteSpace(uidServer) Then Return ""
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT Id FROM Appuntamenti WHERE UidServer = @Uid AND ChiaveAccountOrigine = @Account LIMIT 1", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Uid", uidServer)
                    cmd.Parameters.AddWithValue("@Account", If(chiaveAccount, ""))
                    Dim valore As Object = cmd.ExecuteScalar()
                    If valore Is Nothing OrElse valore Is DBNull.Value Then Return ""
                    Return valore.ToString()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TrovaAppuntamentoPerUidServer - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    ' ============================================================================================
    ' SINCRONIZZAZIONE - IN QUALE CATEGORIA FAR FLUIRE GLI APPUNTAMENTI IMPORTATI
    ' Un account puo' avere piu' calendari CalDAV: per ciascuno di essi (e, in mancanza di una
    ' scelta specifica, per l'intero account) l'utente puo' scegliere in quale Categoria locale
    ' devono finire gli appuntamenti importati da quel calendario, e se quel calendario va incluso
    ' tra quelli letti dalla sincronizzazione. Se l'utente non sceglie nulla resta il comportamento
    ' di sempre: nessuna categoria (o "Nessuna", cosi' come viene mostrato dall'interfaccia).
    ' ============================================================================================

    ' Chiave usata come UrlCalendario per la riga che rappresenta l'impostazione di ripiego a
    ' livello di intero account (categoria usata quando il calendario di provenienza non ha una
    ' categoria propria assegnata, o quando l'evento non porta con se' nessun calendario di origine).
    Private Const UrlCalendarioImpostazioneAccount As String = ""

    ' Imposta la categoria di ripiego per TUTTI gli appuntamenti sincronizzati di un account,
    ' usata quando il calendario di provenienza non ha una categoria propria configurata.
    Public Function ImpostaCategoriaSincronizzazioneAccount(chiaveAccount As String, categoria As String) As Boolean
        Return SalvaImpostazioneCalendarioSincronizzazione(chiaveAccount, UrlCalendarioImpostazioneAccount, "", categoria, True)
    End Function

    Public Function OttieniCategoriaSincronizzazioneAccount(chiaveAccount As String) As String
        Dim imp As ImpostazioneCalendarioSincronizzazione = OttieniImpostazioneCalendarioSincronizzazione(chiaveAccount, UrlCalendarioImpostazioneAccount)
        If imp Is Nothing Then Return ""
        Return If(imp.Categoria, "")
    End Function

    ' Sceglie, per un singolo calendario di un account, se includerlo nella sincronizzazione e in
    ' quale categoria devono fluire i suoi appuntamenti (categoria vuota = nessuna categoria propria,
    ' si applichera' comunque la categoria di ripiego dell'account se configurata).
    Public Function ImpostaCalendarioSincronizzazione(chiaveAccount As String, urlCalendario As String, nomeCalendario As String, selezionato As Boolean, categoria As String) As Boolean
        If String.IsNullOrWhiteSpace(urlCalendario) Then Return False
        Return SalvaImpostazioneCalendarioSincronizzazione(chiaveAccount, urlCalendario, nomeCalendario, categoria, selezionato)
    End Function

    Public Function RimuoviConfigurazioneCalendario(chiaveAccount As String, urlCalendario As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) OrElse String.IsNullOrWhiteSpace(urlCalendario) Then Return False
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("DELETE FROM ImpostazioniSincronizzazioneCalendari WHERE ChiaveAccount = @Account AND UrlCalendario = @Url", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                                                                  cmd.Parameters.AddWithValue("@Url", urlCalendario)
                                                                  cmd.ExecuteNonQuery()
                                                              End Using
                                                          End Using
                                                          Return True
                                                      End Function, "RimuoviConfigurazioneCalendario")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RimuoviConfigurazioneCalendario - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function SalvaImpostazioneCalendarioSincronizzazione(chiaveAccount As String, urlCalendario As String, nomeCalendario As String, categoria As String, selezionato As Boolean) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return False
            categoria = SanitizzaInput(categoria)
            nomeCalendario = SanitizzaInput(nomeCalendario)
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand(
                                                                  "INSERT INTO ImpostazioniSincronizzazioneCalendari (ChiaveAccount, UrlCalendario, NomeCalendario, Categoria, Selezionato) VALUES (@Account, @Url, @Nome, @Categoria, @Selezionato) " &
                                                                  "ON CONFLICT(ChiaveAccount, UrlCalendario) DO UPDATE SET NomeCalendario = excluded.NomeCalendario, Categoria = excluded.Categoria, Selezionato = excluded.Selezionato", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                                                                  cmd.Parameters.AddWithValue("@Url", If(urlCalendario, ""))
                                                                  cmd.Parameters.AddWithValue("@Nome", If(String.IsNullOrWhiteSpace(nomeCalendario), DBNull.Value, CObj(nomeCalendario)))
                                                                  cmd.Parameters.AddWithValue("@Categoria", If(String.IsNullOrWhiteSpace(categoria), DBNull.Value, CObj(categoria)))
                                                                  cmd.Parameters.AddWithValue("@Selezionato", If(selezionato, 1, 0))
                                                                  cmd.ExecuteNonQuery()
                                                              End Using
                                                          End Using
                                                          Return True
                                                      End Function, "SalvaImpostazioneCalendarioSincronizzazione")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SalvaImpostazioneCalendarioSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function OttieniImpostazioneCalendarioSincronizzazione(chiaveAccount As String, urlCalendario As String) As ImpostazioneCalendarioSincronizzazione
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return Nothing
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT UrlCalendario, NomeCalendario, Categoria, Selezionato FROM ImpostazioniSincronizzazioneCalendari WHERE ChiaveAccount = @Account AND UrlCalendario = @Url LIMIT 1", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                    cmd.Parameters.AddWithValue("@Url", If(urlCalendario, ""))
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            Return New ImpostazioneCalendarioSincronizzazione() With {
                                .ChiaveAccount = chiaveAccount,
                                .UrlCalendario = reader("UrlCalendario").ToString(),
                                .NomeCalendario = If(reader.IsDBNull(reader.GetOrdinal("NomeCalendario")), "", reader("NomeCalendario").ToString()),
                                .Categoria = If(reader.IsDBNull(reader.GetOrdinal("Categoria")), "", reader("Categoria").ToString()),
                                .Selezionato = Convert.ToInt32(reader("Selezionato")) = 1
                            }
                        End If
                    End Using
                End Using
            End Using
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniImpostazioneCalendarioSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    ' Tutti i calendari configurati esplicitamente per l'account (esclusa la riga di ripiego a
    ' livello di account, che ha UrlCalendario vuoto): serve alla UI per mostrare quali calendari
    ' sono stati scelti, se sono selezionati per la sincronizzazione e con quale categoria.
    Public Function OttieniCalendariConfigurati(chiaveAccount As String) As List(Of ImpostazioneCalendarioSincronizzazione)
        Dim lista As New List(Of ImpostazioneCalendarioSincronizzazione)
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return lista
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT UrlCalendario, NomeCalendario, Categoria, Selezionato FROM ImpostazioniSincronizzazioneCalendari WHERE ChiaveAccount = @Account AND UrlCalendario <> '' ORDER BY NomeCalendario COLLATE NOCASE ASC", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            lista.Add(New ImpostazioneCalendarioSincronizzazione() With {
                                .ChiaveAccount = chiaveAccount,
                                .UrlCalendario = reader("UrlCalendario").ToString(),
                                .NomeCalendario = If(reader.IsDBNull(reader.GetOrdinal("NomeCalendario")), "", reader("NomeCalendario").ToString()),
                                .Categoria = If(reader.IsDBNull(reader.GetOrdinal("Categoria")), "", reader("Categoria").ToString()),
                                .Selezionato = Convert.ToInt32(reader("Selezionato")) = 1
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniCalendariConfigurati - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    ' Url dei calendari da cui scaricare gli eventi per questo account (solo quelli marcati come
    ' selezionati). Lista vuota = l'utente non ha configurato esplicitamente nessun calendario:
    ' CLSMail.OttieniEventiDalServer mantiene in quel caso il comportamento di sempre (un solo
    ' calendario predefinito per l'account).
    Public Function OttieniCalendariSelezionati(chiaveAccount As String) As List(Of String)
        Dim lista As New List(Of String)
        Try
            For Each c As ImpostazioneCalendarioSincronizzazione In OttieniCalendariConfigurati(chiaveAccount)
                If c.Selezionato Then lista.Add(c.UrlCalendario)
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniCalendariSelezionati - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    ' Determina la categoria in cui deve fluire un appuntamento importato dalla sincronizzazione:
    ' 1) categoria specifica del calendario di provenienza, se configurata;
    ' 2) altrimenti categoria di ripiego dell'intero account, se configurata;
    ' 3) altrimenti stringa vuota, cioe' nessuna categoria (come e' sempre stato quando non si
    '    sceglie nulla nelle impostazioni).
    Public Function RisolviCategoriaSincronizzazione(chiaveAccount As String, urlCalendario As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return ""
            If Not String.IsNullOrWhiteSpace(urlCalendario) Then
                Dim impCalendario As ImpostazioneCalendarioSincronizzazione = OttieniImpostazioneCalendarioSincronizzazione(chiaveAccount, urlCalendario)
                If impCalendario IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(impCalendario.Categoria) Then
                    Return impCalendario.Categoria
                End If
            End If
            Dim impAccount As ImpostazioneCalendarioSincronizzazione = OttieniImpostazioneCalendarioSincronizzazione(chiaveAccount, UrlCalendarioImpostazioneAccount)
            If impAccount IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(impAccount.Categoria) Then
                Return impAccount.Categoria
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RisolviCategoriaSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Function ImportaAppuntamentoDaSincronizzazione(chiaveAccount As String, uidServer As String, urlServer As String,
                                                          oggetto As String, note As String, luogo As String,
                                                          dataInizio As String, dataFine As String,
                                                          oraInizio As String, oraFine As String,
                                                          Optional urlCalendario As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(uidServer) Then Return False

            oggetto = SanitizzaInput(oggetto)
            If String.IsNullOrWhiteSpace(oggetto) Then oggetto = "Appuntamento"
            note = SanitizzaNote(note)
            luogo = SanitizzaInput(luogo)

            Dim dIni As String = ConvertiDataDB(dataInizio)
            If String.IsNullOrWhiteSpace(dIni) Then Return False
            Dim dFin As String = ConvertiDataDB(dataFine)
            If String.IsNullOrWhiteSpace(dFin) Then dFin = dIni

            ' Sia gli eventi sincronizzati con un vero orario di inizio/fine sia quelli "tutto il
            ' giorno" restano appuntamenti a tutti gli effetti: la presenza o meno di OraInizio/
            ' OraFine (gia' salvata sotto) e' sufficiente a distinguerli quando serve, quindi qui
            ' NON forziamo piu' nessuna categoria automatica del tipo "Appuntamento". La Categoria
            ' viene invece decisa solo dalle impostazioni di sincronizzazione scelte dall'utente
            ' (per calendario di provenienza, poi per intero account): se non e' stato configurato
            ' nulla, l'appuntamento resta senza categoria, come e' sempre stato.
            Dim categoriaTesto As String = RisolviCategoriaSincronizzazione(chiaveAccount, urlCalendario)
            Dim categoria As Object = DBNull.Value
            If Not String.IsNullOrWhiteSpace(categoriaTesto) Then
                AggiungiCategoria(categoriaTesto)
                categoria = categoriaTesto
            End If

            Dim idEsistente As String = TrovaAppuntamentoPerUidServer(chiaveAccount, uidServer)
            If String.IsNullOrWhiteSpace(idEsistente) Then idEsistente = TrovaDoppioneAppuntamentoDaServer(chiaveAccount, uidServer, oggetto, dIni, oraInizio)

            Dim chiaveAgenda As String = ""
            If String.IsNullOrWhiteSpace(idEsistente) Then
                chiaveAgenda = AgendaPerSincronizzazione(chiaveAccount, urlCalendario)
            Else
                chiaveAgenda = AgendaDellAppuntamento(idEsistente)
                If String.IsNullOrWhiteSpace(chiaveAgenda) Then chiaveAgenda = AgendaPerSincronizzazione(chiaveAccount, urlCalendario)
            End If
            If Not AgendaPuoRicevereTesto(chiaveAgenda) Then Return False
            oggetto = TestoDaSalvare(oggetto, chiaveAgenda)
            If Not String.IsNullOrWhiteSpace(note) Then note = TestoDaSalvare(note, chiaveAgenda)
            If Not String.IsNullOrWhiteSpace(luogo) Then luogo = TestoDaSalvare(luogo, chiaveAgenda)

            Using conn As SQLiteConnection = ApriConnessione()
                If String.IsNullOrWhiteSpace(idEsistente) Then
                    Using cmd As New SQLiteCommand("INSERT INTO Appuntamenti (Oggetto, Nome, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Stato, Note, Preavviso, ChiaveWG, UidServer, UrlServer, ChiaveAccountOrigine, DaSincronizzare, Luogo, ChiaveAgenda, IdSync) VALUES (@Oggetto, NULL, @DataInizio, @DataFine, @OraInizio, @OraFine, @Categoria, 0, 'attivo', @Note, 0, @ChiaveWG, @Uid, @Url, @Account, 0, @Luogo, @Agenda, @IdSync)", conn)
                        cmd.Parameters.AddWithValue("@IdSync", IdSyncDaUidServerAgenda(uidServer))
                        cmd.CommandTimeout = CommandTimeoutSeconds
                        cmd.Parameters.AddWithValue("@Oggetto", oggetto)
                        cmd.Parameters.AddWithValue("@DataInizio", dIni)
                        cmd.Parameters.AddWithValue("@DataFine", dFin)
                        cmd.Parameters.AddWithValue("@OraInizio", If(String.IsNullOrWhiteSpace(oraInizio), DBNull.Value, CObj(oraInizio)))
                        cmd.Parameters.AddWithValue("@OraFine", If(String.IsNullOrWhiteSpace(oraFine), DBNull.Value, CObj(oraFine)))
                        cmd.Parameters.AddWithValue("@Categoria", categoria)
                        cmd.Parameters.AddWithValue("@Note", If(String.IsNullOrWhiteSpace(note), DBNull.Value, CObj(note)))
                        cmd.Parameters.AddWithValue("@ChiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "Appuntamenti"))
                        cmd.Parameters.AddWithValue("@Uid", uidServer)
                        cmd.Parameters.AddWithValue("@Url", If(urlServer, ""))
                        cmd.Parameters.AddWithValue("@Account", If(chiaveAccount, ""))
                        cmd.Parameters.AddWithValue("@Luogo", If(String.IsNullOrWhiteSpace(luogo), DBNull.Value, CObj(luogo)))
                        cmd.Parameters.AddWithValue("@Agenda", If(chiaveAgenda, ""))
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Dim daSincronizzare As Integer = 0
                    Using cmdStato As New SQLiteCommand("SELECT DaSincronizzare FROM Appuntamenti WHERE Id = @Id", conn)
                        cmdStato.CommandTimeout = CommandTimeoutSeconds
                        cmdStato.Parameters.AddWithValue("@Id", idEsistente)
                        Dim valoreStato As Object = cmdStato.ExecuteScalar()
                        If valoreStato IsNot Nothing AndAlso valoreStato IsNot DBNull.Value Then
                            Integer.TryParse(valoreStato.ToString(), daSincronizzare)
                        End If
                    End Using
                    If daSincronizzare = 1 Then Return True

                    Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Oggetto = @Oggetto, DataInizio = @DataInizio, DataFine = @DataFine, OraInizio = @OraInizio, OraFine = @OraFine, Categoria = @Categoria, Note = @Note, Luogo = @Luogo, UrlServer = @Url, DaSincronizzare = 0 WHERE Id = @Id", conn)
                        cmd.CommandTimeout = CommandTimeoutSeconds
                        cmd.Parameters.AddWithValue("@Oggetto", oggetto)
                        cmd.Parameters.AddWithValue("@DataInizio", dIni)
                        cmd.Parameters.AddWithValue("@DataFine", dFin)
                        cmd.Parameters.AddWithValue("@OraInizio", If(String.IsNullOrWhiteSpace(oraInizio), DBNull.Value, CObj(oraInizio)))
                        cmd.Parameters.AddWithValue("@OraFine", If(String.IsNullOrWhiteSpace(oraFine), DBNull.Value, CObj(oraFine)))
                        cmd.Parameters.AddWithValue("@Categoria", categoria)
                        cmd.Parameters.AddWithValue("@Note", If(String.IsNullOrWhiteSpace(note), DBNull.Value, CObj(note)))
                        cmd.Parameters.AddWithValue("@Luogo", If(String.IsNullOrWhiteSpace(luogo), DBNull.Value, CObj(luogo)))
                        cmd.Parameters.AddWithValue("@Url", If(urlServer, ""))
                        cmd.Parameters.AddWithValue("@Id", idEsistente)
                        cmd.ExecuteNonQuery()
                    End Using
                End If
            End Using

            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ImportaAppuntamentoDaSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function OttieniAppuntamentiDaSincronizzare(Optional chiaveAccount As String = "") As List(Of ElementoSincronizzazioneAgenda)
        Dim risultato As New List(Of ElementoSincronizzazioneAgenda)
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Dim query As String = "SELECT Id, UidServer, UrlServer, ChiaveAccountOrigine, Oggetto, Nome, Note, Luogo, DataInizio, DataFine, OraInizio, OraFine, Frequenza, ChiaveAgenda FROM Appuntamenti WHERE DaSincronizzare = 1"
                If Not String.IsNullOrWhiteSpace(chiaveAccount) Then
                    query &= " AND (ChiaveAccountOrigine = @Account OR ChiaveAccountOrigine IS NULL OR ChiaveAccountOrigine = '')"
                End If
                query &= " ORDER BY DataInizio ASC LIMIT " & MaxBatchSize.ToString()

                Using cmd As New SQLiteCommand(query, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    If Not String.IsNullOrWhiteSpace(chiaveAccount) Then cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim agendaRiga As String = LeggiTestoReader(reader, "ChiaveAgenda")
                            If Not AgendaPuoInviareTesto(agendaRiga) Then Continue While
                            Dim elemento As New ElementoSincronizzazioneAgenda()
                            elemento.Id = reader("Id").ToString()
                            elemento.UidServer = LeggiTestoReader(reader, "UidServer")
                            elemento.UrlServer = LeggiTestoReader(reader, "UrlServer")
                            elemento.ChiaveAccount = LeggiTestoReader(reader, "ChiaveAccountOrigine")
                            elemento.Oggetto = TestoLeggibileAgenda(LeggiTestoReader(reader, "Oggetto"), agendaRiga)
                            elemento.Nome = TestoLeggibileAgenda(LeggiTestoReader(reader, "Nome"), agendaRiga)
                            elemento.Note = TestoLeggibileAgenda(LeggiTestoReader(reader, "Note"), agendaRiga)
                            elemento.Luogo = TestoLeggibileAgenda(LeggiTestoReader(reader, "Luogo"), agendaRiga)
                            elemento.DataInizio = ConvertiDataIT(LeggiTestoReader(reader, "DataInizio"))
                            elemento.DataFine = ConvertiDataIT(LeggiTestoReader(reader, "DataFine"))
                            elemento.OraInizio = LeggiTestoReader(reader, "OraInizio")
                            elemento.OraFine = LeggiTestoReader(reader, "OraFine")
                            Dim freqRaw As Integer = 0
                            If Not reader.IsDBNull(reader.GetOrdinal("Frequenza")) Then
                                Integer.TryParse(reader("Frequenza").ToString(), freqRaw)
                            End If
                            elemento.Frequenza = CType(freqRaw, Frequenza)
                            risultato.Add(elemento)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiDaSincronizzare - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Public Function OttieniEliminazioniAgendaDaSincronizzare(Optional chiaveAccount As String = "") As List(Of ElementoSincronizzazioneAgenda)
        Dim risultato As New List(Of ElementoSincronizzazioneAgenda)
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Dim query As String = "SELECT Id, UidServer, UrlServer, ChiaveAccount FROM AppuntamentiEliminati"
                If Not String.IsNullOrWhiteSpace(chiaveAccount) Then query &= " WHERE ChiaveAccount = @Account"
                query &= " ORDER BY Id ASC LIMIT " & MaxBatchSize.ToString()

                Using cmd As New SQLiteCommand(query, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    If Not String.IsNullOrWhiteSpace(chiaveAccount) Then cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim elemento As New ElementoSincronizzazioneAgenda()
                            elemento.Id = reader("Id").ToString()
                            elemento.UidServer = LeggiTestoReader(reader, "UidServer")
                            elemento.UrlServer = LeggiTestoReader(reader, "UrlServer")
                            elemento.ChiaveAccount = LeggiTestoReader(reader, "ChiaveAccount")
                            risultato.Add(elemento)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniEliminazioniAgendaDaSincronizzare - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Public Function RimuoviEliminazioneAgendaSincronizzata(id As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then Return False
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then Return False

            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("DELETE FROM AppuntamentiEliminati WHERE Id = @Id", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Id", idVal)
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RimuoviEliminazioneAgendaSincronizzata - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function SegnaAppuntamentoSincronizzato(id As String, uidServer As String, urlServer As String, chiaveAccount As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then Return False
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then Return False

            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET UidServer = @Uid, UrlServer = @Url, ChiaveAccountOrigine = @Account, DaSincronizzare = 0 WHERE Id = @Id", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Uid", If(uidServer, ""))
                    cmd.Parameters.AddWithValue("@Url", If(urlServer, ""))
                    cmd.Parameters.AddWithValue("@Account", If(chiaveAccount, ""))
                    cmd.Parameters.AddWithValue("@Id", idVal)
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SegnaAppuntamentoSincronizzato - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function ContaAppuntamentiDaSincronizzare(Optional chiaveAccount As String = "") As Integer
        Try
            Dim daInviare As Integer = OttieniAppuntamentiDaSincronizzare(chiaveAccount).Count
            Dim daEliminare As Integer = OttieniEliminazioniAgendaDaSincronizzare(chiaveAccount).Count
            Return daInviare + daEliminare
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ContaAppuntamentiDaSincronizzare - " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function LeggiTestoReader(reader As SQLiteDataReader, nomeColonna As String) As String
        Try
            Dim ordinale As Integer = reader.GetOrdinal(nomeColonna)
            If reader.IsDBNull(ordinale) Then Return ""
            Return reader.GetValue(ordinale).ToString()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.LeggiTestoReader (" & If(nomeColonna, "") & ") - " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function OttieniDettagliAppuntamento(id As String) As Appuntamento?
        Try
            If String.IsNullOrWhiteSpace(id) Then
                Return Nothing
            End If
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then
                Return Nothing
            End If
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT Id, Oggetto, Nome, DataInizio, DataFine, OraInizio, OraFine, Categoria, Frequenza, Stato, Note, Preavviso FROM Appuntamenti WHERE Id = @Id", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Id", idVal)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        If reader.Read() Then
                            Dim a As New Appuntamento()
                            a.Id = reader("Id").ToString()
                            a.Oggetto = TestoLeggibile(If(reader.IsDBNull(reader.GetOrdinal("Oggetto")), "", reader("Oggetto").ToString()))
                            a.Nome = TestoLeggibile(If(reader.IsDBNull(reader.GetOrdinal("Nome")), "", reader("Nome").ToString()))
                            a.Data = ConvertiDataIT(If(reader.IsDBNull(reader.GetOrdinal("DataInizio")), "", reader("DataInizio").ToString()))
                            a.DataFine = ConvertiDataIT(If(reader.IsDBNull(reader.GetOrdinal("DataFine")), "", reader("DataFine").ToString()))
                            a.OraInizio = If(reader.IsDBNull(reader.GetOrdinal("OraInizio")), "", reader("OraInizio").ToString())
                            a.OraFine = If(reader.IsDBNull(reader.GetOrdinal("OraFine")), "", reader("OraFine").ToString())
                            a.Categoria = If(reader.IsDBNull(reader.GetOrdinal("Categoria")), "", reader("Categoria").ToString())
                            Dim freqRaw As Integer = 0
                            If Not reader.IsDBNull(reader.GetOrdinal("Frequenza")) Then
                                Integer.TryParse(reader("Frequenza").ToString(), freqRaw)
                            End If
                            a.Frequenza = CType(freqRaw, Frequenza)
                            a.Stato = If(reader.IsDBNull(reader.GetOrdinal("Stato")), "", reader("Stato").ToString())
                            a.Note = TestoLeggibile(If(reader.IsDBNull(reader.GetOrdinal("Note")), "", reader("Note").ToString()))
                            Dim prev As Integer = 0
                            If Not reader.IsDBNull(reader.GetOrdinal("Preavviso")) Then
                                Integer.TryParse(reader("Preavviso").ToString(), prev)
                            End If
                            a.Preavviso = prev
                            Return a
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniDettagliAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return Nothing
    End Function

    Public Function ClonaAppuntamento(id As String, nuovaData As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(nuovaData) Then
                Return False
            End If
            Dim a As Appuntamento? = OttieniDettagliAppuntamento(id)
            If Not a.HasValue Then
                Return False
            End If
            Dim oggettoClone As String = SanitizzaInput(a.Value.Oggetto & " (Copia)")
            Return AggiungiAppuntamento(oggettoClone, a.Value.Nome, a.Value.OraInizio, a.Value.OraFine, a.Value.Note, a.Value.Preavviso, a.Value.Frequenza, nuovaData, a.Value.Categoria, nuovaData)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ClonaAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function SpostaAppuntamentoCategoria(id As String, cat As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(id) Then
                Return False
            End If
            Dim idVal As Long
            If Not Long.TryParse(id, idVal) OrElse idVal <= 0 Then
                Return False
            End If
            cat = SanitizzaInput(cat)
            If String.IsNullOrWhiteSpace(cat) Then
                Return False
            End If
            Dim risultato As Boolean = EseguiConRetry(Function()
                                                          Using conn As SQLiteConnection = ApriConnessione()
                                                              Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Categoria = @Cat WHERE Id = @Id", conn)
                                                                  cmd.CommandTimeout = CommandTimeoutSeconds
                                                                  cmd.Parameters.AddWithValue("@Cat", cat)
                                                                  cmd.Parameters.AddWithValue("@Id", idVal)
                                                                  Dim affected As Integer = cmd.ExecuteNonQuery()
                                                                  Return affected > 0
                                                              End Using
                                                          End Using
                                                      End Function, "SpostaAppuntamentoCategoria")
            If risultato Then SegnalaScritturaPerCheckpoint()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SpostaAppuntamentoCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function GetAppuntamentoStato(id As Integer) As String
        Try
            If id <= 0 Then
                Return ""
            End If
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT Stato FROM Appuntamenti WHERE Id = @Id", conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Id", id)
                    Dim res As Object = cmd.ExecuteScalar()
                    If res IsNot Nothing AndAlso res IsNot DBNull.Value Then
                        Return res.ToString()
                    End If
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.GetAppuntamentoStato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Public Sub EseguiAutoCompletamentoAppuntamenti(minuti As Integer)
        Try
            If minuti < 0 Then minuti = 0
            Dim maxData As DateTime = DateTime.Now.AddMinutes(-minuti)
            Dim dataDb As String = maxData.ToString("yyyy-MM-dd")
            Dim oraDb As String = maxData.ToString("HH:mm")
            EseguiConRetry(Function()
                               Using conn As SQLiteConnection = ApriConnessione()
                                   Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET Stato = 'passato' WHERE Stato != 'passato' AND (DataInizio < @Data OR (DataInizio = @Data AND OraFine < @Ora))", conn)
                                       cmd.CommandTimeout = CommandTimeoutSeconds
                                       cmd.Parameters.AddWithValue("@Data", dataDb)
                                       cmd.Parameters.AddWithValue("@Ora", oraDb)
                                       cmd.ExecuteNonQuery()
                                   End Using
                               End Using
                               Return True
                           End Function, "EseguiAutoCompletamentoAppuntamenti")
            SegnalaScritturaPerCheckpoint()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EseguiAutoCompletamentoAppuntamenti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function OttieniAppuntamentiPaginati(skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti"
            Return OttieniListaAppuntamentiDaQuery(q, New Dictionary(Of String, Object)(), skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiFuturiPaginati(skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim now As DateTime = DateTime.Now
            Dim oggiDb As String = now.ToString("yyyy-MM-dd")
            Dim oraDb As String = now.ToString("HH:mm")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE DataInizio > @Oggi OR (DataInizio = @Oggi AND OraInizio >= @Ora)"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Oggi", oggiDb)
            p.Add("@Ora", oraDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiFuturiPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniProgrammaGiornalieroPaginato(data As String, skip As Integer, take As Integer, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim dataDb As String = ConvertiDataDB(data)
            If String.IsNullOrWhiteSpace(dataDb) Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE DataInizio = @Data"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Data", dataDb)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, CancellationToken.None, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniProgrammaGiornalieroPaginato - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiPerMesePaginati(skip As Integer, take As Integer, mese As Integer, anno As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            If mese < 1 OrElse mese > 12 OrElse anno < 1 OrElse anno > 9999 Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim startMese As String = New DateTime(anno, mese, 1).ToString("yyyy-MM-dd")
            Dim endMese As String = New DateTime(anno, mese, DateTime.DaysInMonth(anno, mese)).ToString("yyyy-MM-dd")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE DataInizio >= @Start AND DataInizio <= @End"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Start", startMese)
            p.Add("@End", endMese)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiPerMesePaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function CercaAppuntamentiDataOParolaPaginati(testo As String, skip As Integer, take As Integer, data As String, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            testo = SanitizzaInput(testo)
            If String.IsNullOrWhiteSpace(testo) Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim dataDb As String = ConvertiDataDB(data)

            If _ftsDisponibile Then
                Dim ftsQuery As String = CostruisciQueryFts(testo)
                If Not String.IsNullOrWhiteSpace(ftsQuery) Then
                    Dim q As String = "SELECT a.Id, a.Oggetto, a.DataInizio FROM Appuntamenti a " &
                                       "JOIN Appuntamenti_FTS f ON f.rowid = a.Id WHERE Appuntamenti_FTS MATCH @Testo"
                    Dim p As New Dictionary(Of String, Object)()
                    p.Add("@Testo", ftsQuery)
                    If Not String.IsNullOrWhiteSpace(dataDb) Then
                        q &= " AND a.DataInizio = @Data"
                        p.Add("@Data", dataDb)
                    End If
                    Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
                End If
            End If

            Dim qLike As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE (Oggetto LIKE @Testo OR Nome LIKE @Testo OR Note LIKE @Testo)"
            Dim pLike As New Dictionary(Of String, Object)()
            pLike.Add("@Testo", "%" & testo & "%")
            If Not String.IsNullOrWhiteSpace(dataDb) Then
                qLike &= " AND DataInizio = @Data"
                pLike.Add("@Data", dataDb)
            End If
            Return OttieniListaAppuntamentiDaQuery(qLike, pLike, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CercaAppuntamentiDataOParolaPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function CercaAppuntamentiPaginati(testo As String, skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            testo = SanitizzaInput(testo)
            If String.IsNullOrWhiteSpace(testo) Then
                Return New List(Of AppuntamentoLista)()
            End If

            If _ftsDisponibile Then
                Dim ftsQuery As String = CostruisciQueryFts(testo)
                If Not String.IsNullOrWhiteSpace(ftsQuery) Then
                    Dim q As String = "SELECT a.Id, a.Oggetto, a.DataInizio FROM Appuntamenti a " &
                                       "JOIN Appuntamenti_FTS f ON f.rowid = a.Id WHERE Appuntamenti_FTS MATCH @Testo"
                    Dim p As New Dictionary(Of String, Object)()
                    p.Add("@Testo", ftsQuery)
                    Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
                End If
            End If

            Dim qLike As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Oggetto LIKE @Testo OR Nome LIKE @Testo OR Note LIKE @Testo"
            Dim pLike As New Dictionary(Of String, Object)()
            pLike.Add("@Testo", "%" & testo & "%")
            Return OttieniListaAppuntamentiDaQuery(qLike, pLike, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CercaAppuntamentiPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Private Function CostruisciQueryFts(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""

            Dim parole As String() = testo.Split(New Char() {" "c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
            Dim sb As New StringBuilder()
            Dim primaParola As Boolean = True

            For Each parolaRaw As String In parole
                Dim parola As String = New String(parolaRaw.Where(Function(c) Char.IsLetterOrDigit(c)).ToArray())
                If String.IsNullOrWhiteSpace(parola) Then Continue For

                If Not primaParola Then sb.Append(" AND ")
                sb.Append(""""c).Append(parola).Append(""""c).Append("*")
                primaParola = False
            Next

            Return sb.ToString()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CostruisciQueryFts - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Function OttieniAppuntamentiPerPeriodoPaginati(da As DateTime, a As DateTime, skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            If da > a Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim startP As String = da.ToString("yyyy-MM-dd")
            Dim endP As String = a.ToString("yyyy-MM-dd")
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE DataInizio >= @Start AND DataInizio <= @End"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Start", startP)
            p.Add("@End", endP)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiPerPeriodoPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiPerCategoriaPaginati(cat As String, skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            cat = SanitizzaInput(cat)
            If String.IsNullOrWhiteSpace(cat) Then
                Return New List(Of AppuntamentoLista)()
            End If
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Categoria = @Cat"
            Dim p As New Dictionary(Of String, Object)()
            p.Add("@Cat", cat)
            Return OttieniListaAppuntamentiDaQuery(q, p, skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiPerCategoriaPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Public Function OttieniAppuntamentiRicorrentiPaginati(skip As Integer, take As Integer, token As CancellationToken, ord As TipoOrdinamento) As List(Of AppuntamentoLista)
        Try
            Dim q As String = "SELECT Id, Oggetto, DataInizio FROM Appuntamenti WHERE Frequenza > 0"
            Return OttieniListaAppuntamentiDaQuery(q, New Dictionary(Of String, Object)(), skip, take, token, ord)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAppuntamentiRicorrentiPaginati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of AppuntamentoLista)()
        End Try
    End Function

    Private Function OttieniMappaAppuntamenti(dataInizio As DateTime, giorni As Integer) As Dictionary(Of String, List(Of Tuple(Of TimeSpan, TimeSpan)))
        Dim mappa As New Dictionary(Of String, List(Of Tuple(Of TimeSpan, TimeSpan)))()
        Try
            If giorni <= 0 Then giorni = 1
            If giorni > MaxGiorniRangeMappa Then giorni = MaxGiorniRangeMappa
            Dim startDateDb As String = dataInizio.ToString("yyyy-MM-dd")
            Dim endDateDb As String = dataInizio.AddDays(giorni).ToString("yyyy-MM-dd")

            Const maxRigheLette As Integer = 2_000_000
            Dim righeLette As Integer = 0

            Using conn As SQLiteConnection = ApriConnessione()
                Dim q As String = "SELECT DataInizio, OraInizio, OraFine FROM Appuntamenti WHERE DataInizio BETWEEN @Start AND @End AND OraInizio IS NOT NULL AND OraFine IS NOT NULL ORDER BY DataInizio ASC, OraInizio ASC"
                Using cmd As New SQLiteCommand(q, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Start", startDateDb)
                    cmd.Parameters.AddWithValue("@End", endDateDb)
                    Using reader As SQLiteDataReader = cmd.ExecuteReader(CommandBehavior.SequentialAccess)
                        While reader.Read()
                            righeLette += 1
                            If righeLette > maxRigheLette Then
                                Exit While
                            End If

                            Dim dDb As String = If(reader.IsDBNull(0), "", reader.GetString(0))
                            If String.IsNullOrWhiteSpace(dDb) Then
                                Continue While
                            End If
                            Dim tI As TimeSpan
                            Dim tF As TimeSpan
                            Dim oraIniStr As String = If(reader.IsDBNull(1), "", reader.GetString(1))
                            Dim oraFinStr As String = If(reader.IsDBNull(2), "", reader.GetString(2))
                            If TimeSpan.TryParse(oraIniStr, tI) AndAlso TimeSpan.TryParse(oraFinStr, tF) Then
                                If Not mappa.ContainsKey(dDb) Then
                                    mappa(dDb) = New List(Of Tuple(Of TimeSpan, TimeSpan))()
                                End If
                                mappa(dDb).Add(New Tuple(Of TimeSpan, TimeSpan)(tI, tF))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniMappaAppuntamenti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return mappa
    End Function

    Private Function CalcolaBlocchiLiberi(appuntamenti As List(Of Tuple(Of TimeSpan, TimeSpan)), durataMin As Integer, oraIniLav As DateTime, oraFineLav As DateTime, escludiLav As Boolean, orariEsclusi As List(Of Tuple(Of TimeSpan, TimeSpan))) As List(Of String)
        Dim liberi As New List(Of String)()
        Try
            If durataMin <= 0 Then
                Return liberi
            End If
            Dim startH As TimeSpan = oraIniLav.TimeOfDay
            Dim endH As TimeSpan = oraFineLav.TimeOfDay

            If escludiLav Then
                startH = TimeSpan.Zero
                endH = New TimeSpan(23, 59, 59)
            End If

            Dim apptList As New List(Of Tuple(Of TimeSpan, TimeSpan))()
            If appuntamenti IsNot Nothing Then
                apptList.AddRange(appuntamenti)
            End If

            If orariEsclusi IsNot Nothing Then
                apptList.AddRange(orariEsclusi)
            End If

            If escludiLav AndAlso oraIniLav.TimeOfDay < oraFineLav.TimeOfDay Then
                apptList.Add(New Tuple(Of TimeSpan, TimeSpan)(oraIniLav.TimeOfDay, oraFineLav.TimeOfDay))
            End If

            apptList.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))

            Dim mergedList As New List(Of Tuple(Of TimeSpan, TimeSpan))()
            If apptList.Count > 0 Then
                Dim currentStart As TimeSpan = apptList(0).Item1
                Dim currentEnd As TimeSpan = apptList(0).Item2
                For i As Integer = 1 To apptList.Count - 1
                    If apptList(i).Item1 <= currentEnd Then
                        If apptList(i).Item2 > currentEnd Then
                            currentEnd = apptList(i).Item2
                        End If
                    Else
                        mergedList.Add(New Tuple(Of TimeSpan, TimeSpan)(currentStart, currentEnd))
                        currentStart = apptList(i).Item1
                        currentEnd = apptList(i).Item2
                    End If
                Next
                mergedList.Add(New Tuple(Of TimeSpan, TimeSpan)(currentStart, currentEnd))
            End If

            Dim curTime As TimeSpan = startH
            Dim targetDur As TimeSpan = TimeSpan.FromMinutes(durataMin)
            Dim stepSpan As TimeSpan = TimeSpan.FromMinutes(10)

            For Each blocco In mergedList
                If blocco.Item1 > curTime Then
                    Dim freeEnd As TimeSpan = blocco.Item1
                    If freeEnd > endH Then
                        freeEnd = endH
                    End If
                    Dim scanTime As TimeSpan = curTime
                    While scanTime.Add(targetDur) <= freeEnd
                        Dim slotEnd As TimeSpan = scanTime.Add(targetDur)
                        liberi.Add(scanTime.Hours.ToString("00") & ":" & scanTime.Minutes.ToString("00") & " - " & slotEnd.Hours.ToString("00") & ":" & slotEnd.Minutes.ToString("00"))
                        scanTime = scanTime.Add(stepSpan)
                        If liberi.Count >= MaxRisultatiRicercaSlot Then
                            Return liberi
                        End If
                    End While
                End If
                If blocco.Item2 > curTime Then
                    curTime = blocco.Item2
                End If
            Next

            If curTime < endH Then
                Dim scanTime As TimeSpan = curTime
                While scanTime.Add(targetDur) <= endH
                    Dim slotEnd As TimeSpan = scanTime.Add(targetDur)
                    liberi.Add(scanTime.Hours.ToString("00") & ":" & scanTime.Minutes.ToString("00") & " - " & slotEnd.Hours.ToString("00") & ":" & slotEnd.Minutes.ToString("00"))
                    scanTime = scanTime.Add(stepSpan)
                    If liberi.Count >= MaxRisultatiRicercaSlot Then
                        Return liberi
                    End If
                End While
            End If

        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CalcolaBlocchiLiberi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return liberi
    End Function

    Public Function RicercaDateLibere(dataInizio As DateTime, giorni As Integer, durataMin As Integer, oraIniLav As DateTime, oraFineLav As DateTime, giorniCons As List(Of DayOfWeek), escludiLav As Boolean, orariEsclusi As List(Of Tuple(Of TimeSpan, TimeSpan)), maxResults As Integer) As List(Of String)
        Dim risultati As New List(Of String)()
        Try
            If giorni <= 0 Then giorni = 1
            If giorni > 730 Then giorni = 730
            If maxResults <= 0 OrElse maxResults > 1000 Then maxResults = 100
            Dim mappa As Dictionary(Of String, List(Of Tuple(Of TimeSpan, TimeSpan))) = OttieniMappaAppuntamenti(dataInizio, giorni)
            Dim dataCorrente As DateTime = dataInizio
            While dataCorrente <= dataInizio.AddDays(giorni) AndAlso risultati.Count < maxResults
                If giorniCons Is Nothing OrElse giorniCons.Contains(dataCorrente.DayOfWeek) Then
                    Dim appts As List(Of Tuple(Of TimeSpan, TimeSpan)) = Nothing
                    mappa.TryGetValue(dataCorrente.ToString("yyyy-MM-dd"), appts)
                    If appts Is Nothing Then appts = New List(Of Tuple(Of TimeSpan, TimeSpan))()
                    Dim liberi As List(Of String) = CalcolaBlocchiLiberi(appts, durataMin, oraIniLav, oraFineLav, escludiLav, orariEsclusi)
                    If liberi IsNot Nothing AndAlso liberi.Count > 0 Then
                        risultati.Add(dataCorrente.ToString("dd/MM/yyyy"))
                    End If
                End If
                dataCorrente = dataCorrente.AddDays(1)
            End While
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RicercaDateLibere - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultati
    End Function

    Public Function RicercaOrariLiberi(data As DateTime, durataMin As Integer, durataMax As Integer, oraIniLav As DateTime, oraFineLav As DateTime, escludiLav As Boolean, orariEsclusi As List(Of Tuple(Of TimeSpan, TimeSpan))) As List(Of String)
        Dim appts As New List(Of Tuple(Of TimeSpan, TimeSpan))()
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Dim q As String = "SELECT OraInizio, OraFine FROM Appuntamenti WHERE DataInizio = @Data AND OraInizio IS NOT NULL AND OraFine IS NOT NULL ORDER BY OraInizio ASC"
                Using cmd As New SQLiteCommand(q, conn)
                    cmd.CommandTimeout = CommandTimeoutSeconds
                    cmd.Parameters.AddWithValue("@Data", data.ToString("yyyy-MM-dd"))
                    Using reader As SQLiteDataReader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim tI As TimeSpan
                            Dim tF As TimeSpan
                            Dim s1 As String = If(reader.IsDBNull(0), "", reader.GetString(0))
                            Dim s2 As String = If(reader.IsDBNull(1), "", reader.GetString(1))
                            If TimeSpan.TryParse(s1, tI) AndAlso TimeSpan.TryParse(s2, tF) Then
                                appts.Add(New Tuple(Of TimeSpan, TimeSpan)(tI, tF))
                            End If
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RicercaOrariLiberi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return CalcolaBlocchiLiberi(appts, durataMin, oraIniLav, oraFineLav, escludiLav, orariEsclusi)
    End Function

    Public Function RicercaBloccoContinuoLibero(durataOre As Integer, dataPartenza As DateTime) As List(Of String)
        Dim risultati As New List(Of String)()
        Try
            If durataOre <= 0 Then
                Return risultati
            End If
            Dim maxGgRicerca As Integer = 365
            Dim durataMins As Integer = durataOre * 60
            Dim requiredDur As TimeSpan = TimeSpan.FromMinutes(durataMins)
            Dim stepSpan As TimeSpan = TimeSpan.FromMinutes(10)
            Dim mappa As Dictionary(Of String, List(Of Tuple(Of TimeSpan, TimeSpan))) = OttieniMappaAppuntamenti(dataPartenza, maxGgRicerca)
            Dim dataCorrente As DateTime = dataPartenza
            While dataCorrente <= dataPartenza.AddDays(maxGgRicerca) AndAlso risultati.Count < 50
                Dim appts As List(Of Tuple(Of TimeSpan, TimeSpan)) = Nothing
                mappa.TryGetValue(dataCorrente.ToString("yyyy-MM-dd"), appts)
                If appts Is Nothing Then appts = New List(Of Tuple(Of TimeSpan, TimeSpan))()
                appts.Sort(Function(x, y) x.Item1.CompareTo(y.Item1))
                Dim mergedList As New List(Of Tuple(Of TimeSpan, TimeSpan))()
                If appts.Count > 0 Then
                    Dim currentStart As TimeSpan = appts(0).Item1
                    Dim currentEnd As TimeSpan = appts(0).Item2
                    For i As Integer = 1 To appts.Count - 1
                        If appts(i).Item1 <= currentEnd Then
                            If appts(i).Item2 > currentEnd Then
                                currentEnd = appts(i).Item2
                            End If
                        Else
                            mergedList.Add(New Tuple(Of TimeSpan, TimeSpan)(currentStart, currentEnd))
                            currentStart = appts(i).Item1
                            currentEnd = appts(i).Item2
                        End If
                    Next
                    mergedList.Add(New Tuple(Of TimeSpan, TimeSpan)(currentStart, currentEnd))
                End If
                Dim curT As TimeSpan = TimeSpan.Zero
                Dim endDay As TimeSpan = New TimeSpan(23, 59, 59)
                For Each blocco In mergedList
                    If blocco.Item1 > curT Then
                        Dim scanTime As TimeSpan = curT
                        While scanTime.Add(requiredDur) <= blocco.Item1
                            Dim slotEnd As TimeSpan = scanTime.Add(requiredDur)
                            risultati.Add(dataCorrente.ToString("dd/MM/yyyy") & " " & scanTime.Hours.ToString("00") & ":" & scanTime.Minutes.ToString("00") & " - " & dataCorrente.ToString("dd/MM/yyyy") & " " & slotEnd.Hours.ToString("00") & ":" & slotEnd.Minutes.ToString("00"))
                            scanTime = scanTime.Add(stepSpan)
                            If risultati.Count >= 50 Then
                                Return risultati
                            End If
                        End While
                    End If
                    If blocco.Item2 > curT Then
                        curT = blocco.Item2
                    End If
                Next
                If curT < endDay Then
                    Dim scanTime As TimeSpan = curT
                    While scanTime.Add(requiredDur) <= endDay
                        Dim slotEnd As TimeSpan = scanTime.Add(requiredDur)
                        risultati.Add(dataCorrente.ToString("dd/MM/yyyy") & " " & scanTime.Hours.ToString("00") & ":" & scanTime.Minutes.ToString("00") & " - " & dataCorrente.ToString("dd/MM/yyyy") & " " & slotEnd.Hours.ToString("00") & ":" & slotEnd.Minutes.ToString("00"))
                        scanTime = scanTime.Add(stepSpan)
                        If risultati.Count >= 50 Then
                            Return risultati
                        End If
                    End While
                End If
                dataCorrente = dataCorrente.AddDays(1)
            End While
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RicercaBloccoContinuoLibero - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultati
    End Function

    Public Function RicercaSlotGiornalieriConsecutivi(gg As Integer, min As Integer, dataPartenza As DateTime) As List(Of String)
        Dim liberi As New List(Of String)()
        Try
            If gg <= 0 Then gg = 1
            If min <= 0 Then min = 1
            Dim maxLimit As Integer = 365
            Dim mappa As Dictionary(Of String, List(Of Tuple(Of TimeSpan, TimeSpan))) = OttieniMappaAppuntamenti(dataPartenza, maxLimit + gg)
            Dim dataCorrente As DateTime = dataPartenza
            Dim targetLimit As DateTime = dataPartenza.AddDays(maxLimit)
            While dataCorrente <= targetLimit AndAlso liberi.Count < 20
                Dim valida As Boolean = True
                For i As Integer = 0 To gg - 1
                    Dim rDay As DateTime = dataCorrente.AddDays(i)
                    Dim appts As List(Of Tuple(Of TimeSpan, TimeSpan)) = Nothing
                    mappa.TryGetValue(rDay.ToString("yyyy-MM-dd"), appts)
                    If appts Is Nothing Then appts = New List(Of Tuple(Of TimeSpan, TimeSpan))()
                    Dim blocchiLiberi As List(Of String) = CalcolaBlocchiLiberi(appts, min, New DateTime(1, 1, 1, 0, 0, 0), New DateTime(1, 1, 1, 23, 59, 59), False, New List(Of Tuple(Of TimeSpan, TimeSpan))())
                    If blocchiLiberi Is Nothing OrElse blocchiLiberi.Count = 0 Then
                        valida = False
                        Exit For
                    End If
                Next
                If valida Then
                    liberi.Add(dataCorrente.ToString("dd/MM/yyyy") & " - " & dataCorrente.AddDays(gg - 1).ToString("dd/MM/yyyy"))
                End If
                dataCorrente = dataCorrente.AddDays(1)
            End While
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RicercaSlotGiornalieriConsecutivi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return liberi
    End Function

End Class