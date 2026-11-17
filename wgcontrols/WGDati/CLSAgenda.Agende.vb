Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports WGDb

' Gestione delle agende multiple, dei collegamenti agli account e della protezione tramite cifratura ibrida.
Partial Public Class CLSAgenda

    ' Agende multiple. Un'agenda e' un contenitore di appuntamenti con un nome. Puo' essere
    ' collegata a un account e a un calendario del server, oppure restare soltanto nel computer.
    ' Le categorie continuano a valere dentro ogni agenda, come prima.
    Public Class DescrizioneAgenda
        Public Property Chiave As String = ""
        Public Property Nome As String = ""
        Public Property ChiaveAccount As String = ""
        Public Property UrlCalendario As String = ""
        Public Property Predefinita As Boolean = False
        Public Property Protetta As Boolean = False
    End Class

    Private Shared ReadOnly CampiRiservatiAgenda As String() = {"Oggetto", "Nome", "Note", "Luogo"}
    Private Const PrefissoCifraturaIbrida As String = "WGRSA1:"
    ' WGRSA2: chiave casuale cifrata con RSA e dati cifrati con AES-GCM della libreria nativa (WGRSA1 resta solo in lettura)
    Private Const PrefissoCifraturaIbridaGcm As String = "WGRSA2:"

    Private ReadOnly _agendeSbloccate As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _chiaviPrivateAgende As New Dictionary(Of String, RSA)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockAgende As New Object()
    Private _agendaCorrente As String = ""

    ' ============================================================================================
    ' STRUTTURA
    ' ============================================================================================

    Friend Sub PreparaTabelleAgende()
        Try
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.CommandText =
                                "CREATE TABLE IF NOT EXISTS Agende (" &
                                "Id INTEGER PRIMARY KEY AUTOINCREMENT, " &
                                "Chiave TEXT NOT NULL, " &
                                "Nome TEXT NOT NULL, " &
                                "ChiaveAccount TEXT DEFAULT '', " &
                                "UrlCalendario TEXT DEFAULT '', " &
                                "Predefinita INTEGER DEFAULT 0, " &
                                "Protetta INTEGER DEFAULT 0, " &
                                "SalePassword TEXT DEFAULT '', " &
                                "ImprontaPassword TEXT DEFAULT '', " &
                                "DataCreazione TEXT, " &
                                "DataModifica TEXT);"
                            cmd.ExecuteNonQuery()
                            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_agende_chiave ON Agende(Chiave);"
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    Return True
                End Function, "PreparaTabelleAgende")

            AggiungiColonnaAppuntamenti("ChiaveAgenda", "TEXT")
            AggiungiColonnaAgende("ChiavePubblica", "TEXT DEFAULT ''")
            AggiungiColonnaAgende("ChiavePrivataCifrata", "TEXT DEFAULT ''")
            AssegnaAgendaPredefinitaAgliAppuntamentiSenza()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.PreparaTabelleAgende - " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiColonnaAgende(nomeColonna As String, tipo As String)
        Try
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Dim presente As Boolean = False
                        Using cmd As New SQLiteCommand("PRAGMA table_info(Agende);", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                While reader.Read()
                                    If String.Equals(reader("name").ToString(), nomeColonna, StringComparison.OrdinalIgnoreCase) Then
                                        presente = True
                                        Exit While
                                    End If
                                End While
                            End Using
                        End Using
                        If Not presente Then
                            Using cmd As New SQLiteCommand("ALTER TABLE Agende ADD COLUMN " & nomeColonna & " " & tipo & ";", conn)
                                cmd.CommandTimeout = CommandTimeoutSeconds
                                cmd.ExecuteNonQuery()
                            End Using
                        End If
                    End Using
                    Return True
                End Function, "AggiungiColonnaAgende " & nomeColonna)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiungiColonnaAgende (" & nomeColonna & ") - " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiColonnaAppuntamenti(nomeColonna As String, tipo As String)
        Try
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Dim presente As Boolean = False
                        Using cmd As New SQLiteCommand("PRAGMA table_info(Appuntamenti);", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                While reader.Read()
                                    If String.Equals(reader("name").ToString(), nomeColonna, StringComparison.OrdinalIgnoreCase) Then
                                        presente = True
                                        Exit While
                                    End If
                                End While
                            End Using
                        End Using
                        If Not presente Then
                            Using cmd As New SQLiteCommand("ALTER TABLE Appuntamenti ADD COLUMN " & nomeColonna & " " & tipo & ";", conn)
                                cmd.CommandTimeout = CommandTimeoutSeconds
                                cmd.ExecuteNonQuery()
                            End Using
                        End If
                    End Using
                    Return True
                End Function, "AggiungiColonnaAppuntamenti " & nomeColonna)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiungiColonnaAppuntamenti (" & nomeColonna & ") - " & ex.Message)
        End Try
    End Sub

    ' Gli appuntamenti che esistevano prima delle agende multiple finiscono nell'agenda
    ' predefinita, altrimenti scomparirebbero da ogni elenco.
    Private Sub AssegnaAgendaPredefinitaAgliAppuntamentiSenza()
        Try
            Dim predefinita As String = OttieniOCreaAgendaPredefinita()
            If String.IsNullOrWhiteSpace(predefinita) Then Return
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET ChiaveAgenda = @Agenda WHERE ChiaveAgenda IS NULL OR ChiaveAgenda = '';", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Agenda", predefinita)
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    Return True
                End Function, "AssegnaAgendaPredefinita")
            SegnalaScritturaPerCheckpoint()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AssegnaAgendaPredefinitaAgliAppuntamentiSenza - " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' ELENCO E GESTIONE
    ' ============================================================================================

    Public Function OttieniAgende() As List(Of DescrizioneAgenda)
        Dim elenco As New List(Of DescrizioneAgenda)
        Try
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT Chiave, Nome, ChiaveAccount, UrlCalendario, Predefinita, ImprontaPassword FROM Agende ORDER BY Predefinita DESC, Nome COLLATE NOCASE ASC;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                While reader.Read()
                                    Dim voce As New DescrizioneAgenda()
                                    voce.Chiave = LeggiTesto(reader, "Chiave")
                                    voce.Nome = LeggiTesto(reader, "Nome")
                                    voce.ChiaveAccount = LeggiTesto(reader, "ChiaveAccount")
                                    voce.UrlCalendario = LeggiTesto(reader, "UrlCalendario")
                                    voce.Predefinita = LeggiTesto(reader, "Predefinita") = "1"
                                    voce.Protetta = LeggiTesto(reader, "ImprontaPassword").Length > 0
                                    elenco.Add(voce)
                                End While
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "OttieniAgende")
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniAgende - " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Function LeggiTesto(reader As SQLiteDataReader, nomeColonna As String) As String
        Try
            Dim indice As Integer = reader.GetOrdinal(nomeColonna)
            If reader.IsDBNull(indice) Then Return ""
            Return reader.GetValue(indice).ToString()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.LeggiTesto (" & nomeColonna & ") - " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function CreaAgenda(nome As String, Optional chiaveAccount As String = "", Optional urlCalendario As String = "", Optional predefinita As Boolean = False) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim chiave As String = Guid.NewGuid().ToString("N")
            Dim adesso As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("INSERT INTO Agende (Chiave, Nome, ChiaveAccount, UrlCalendario, Predefinita, Protetta, SalePassword, ImprontaPassword, DataCreazione, DataModifica) VALUES (@Chiave, @Nome, @Account, @Url, @Pred, 0, '', '', @Creazione, @Modifica);", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", chiave)
                            cmd.Parameters.AddWithValue("@Nome", nome.Trim())
                            cmd.Parameters.AddWithValue("@Account", If(chiaveAccount, "").Trim())
                            cmd.Parameters.AddWithValue("@Url", If(urlCalendario, "").Trim())
                            cmd.Parameters.AddWithValue("@Pred", If(predefinita, 1, 0))
                            cmd.Parameters.AddWithValue("@Creazione", adesso)
                            cmd.Parameters.AddWithValue("@Modifica", adesso)
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    Return True
                End Function, "CreaAgenda")

            If predefinita Then ImpostaAgendaPredefinita(chiave)
            SegnalaScritturaPerCheckpoint()
            Return chiave
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CreaAgenda - " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function OttieniOCreaAgendaPredefinita() As String
        Try
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If voce.Predefinita Then Return voce.Chiave
            Next
            Dim tutte As List(Of DescrizioneAgenda) = OttieniAgende()
            If tutte.Count > 0 Then
                ImpostaAgendaPredefinita(tutte(0).Chiave)
                Return tutte(0).Chiave
            End If
            Return CreaAgenda("Agenda personale", "", "", True)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.OttieniOCreaAgendaPredefinita - " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function ImpostaAgendaPredefinita(chiaveAgenda As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return False
            Return EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("UPDATE Agende SET Predefinita = 0;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.ExecuteNonQuery()
                        End Using
                        Using cmd As New SQLiteCommand("UPDATE Agende SET Predefinita = 1, DataModifica = @Modifica WHERE Chiave = @Chiave;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", chiaveAgenda.Trim())
                            cmd.Parameters.AddWithValue("@Modifica", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    Return True
                End Function, "ImpostaAgendaPredefinita")
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ImpostaAgendaPredefinita - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function RinominaAgenda(chiaveAgenda As String, nuovoNome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) OrElse String.IsNullOrWhiteSpace(nuovoNome) Then Return False
            Return AggiornaCampoAgenda(chiaveAgenda, "Nome", nuovoNome.Trim())
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RinominaAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function CollegaAgendaAdAccount(chiaveAgenda As String, chiaveAccount As String, urlCalendario As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return False
            If Not AggiornaCampoAgenda(chiaveAgenda, "ChiaveAccount", If(chiaveAccount, "").Trim()) Then Return False
            Return AggiornaCampoAgenda(chiaveAgenda, "UrlCalendario", If(urlCalendario, "").Trim())
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CollegaAgendaAdAccount - " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AggiornaCampoAgenda(chiaveAgenda As String, nomeCampo As String, valore As Object) As Boolean
        Try
            Return EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("UPDATE Agende SET " & nomeCampo & " = @Valore, DataModifica = @Modifica WHERE Chiave = @Chiave;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Valore", valore)
                            cmd.Parameters.AddWithValue("@Modifica", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                            cmd.Parameters.AddWithValue("@Chiave", chiaveAgenda.Trim())
                            Return cmd.ExecuteNonQuery() > 0
                        End Using
                    End Using
                End Function, "AggiornaCampoAgenda " & nomeCampo)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AggiornaCampoAgenda (" & nomeCampo & ") - " & ex.Message)
            Return False
        End Try
    End Function

    ' Elimina un'agenda spostandone gli appuntamenti in un'altra, per non perderli.
    Public Function EliminaAgenda(chiaveAgenda As String) As Boolean
        Try
            Dim agende As List(Of DescrizioneAgenda) = OttieniAgende()
            If agende.Count <= 1 Then
                log.ScriviLog("CLSAgenda.EliminaAgenda - rifiutata: è l'unica agenda")
                Return False
            End If

            Dim destinazione As String = ""
            For Each voce As DescrizioneAgenda In agende
                If Not String.Equals(voce.Chiave, chiaveAgenda, StringComparison.Ordinal) Then
                    destinazione = voce.Chiave
                    Exit For
                End If
            Next
            If String.IsNullOrWhiteSpace(destinazione) Then Return False

            Dim eraPredefinita As Boolean = False
            For Each voce As DescrizioneAgenda In agende
                If String.Equals(voce.Chiave, chiaveAgenda, StringComparison.Ordinal) Then eraPredefinita = voce.Predefinita
            Next

            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET ChiaveAgenda = @Nuova WHERE ChiaveAgenda = @Vecchia;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Nuova", destinazione)
                            cmd.Parameters.AddWithValue("@Vecchia", chiaveAgenda.Trim())
                            cmd.ExecuteNonQuery()
                        End Using
                        Using cmd As New SQLiteCommand("DELETE FROM Agende WHERE Chiave = @Chiave;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", chiaveAgenda.Trim())
                            cmd.ExecuteNonQuery()
                        End Using
                    End Using
                    Return True
                End Function, "EliminaAgenda")

            If eraPredefinita Then ImpostaAgendaPredefinita(destinazione)
            If String.Equals(_agendaCorrente, chiaveAgenda, StringComparison.Ordinal) Then _agendaCorrente = ""
            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.EliminaAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Rimuove tutte le agende di un account che non puo' (piu') averne una propria, ad esempio
    ''' perche' e' un account Google e la sincronizzazione del calendario non e' possibile per
    ''' quel provider: sposta gli appuntamenti eventualmente presenti nell'agenda locale
    ''' predefinita (quella senza account) ed elimina le agende ormai orfane dell'account. Se
    ''' l'account non ha nessuna agenda non fa nulla.
    ''' </summary>
    Public Function RimuoviAgendaAccount(chiaveAccount As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return True
            Dim tutte As List(Of DescrizioneAgenda) = OttieniAgende()
            If tutte Is Nothing Then Return True
            Dim delAccount As New List(Of DescrizioneAgenda)
            For Each voce As DescrizioneAgenda In tutte
                If String.Equals(voce.ChiaveAccount, chiaveAccount, StringComparison.Ordinal) Then delAccount.Add(voce)
            Next
            If delAccount.Count = 0 Then Return True

            Dim agendaLocale As String = OttieniOCreaAgendaPredefinita()
            For Each voce As DescrizioneAgenda In delAccount
                Try
                    EseguiConRetry(Of Boolean)(
                        Function()
                            Using conn As SQLiteConnection = ApriConnessione()
                                Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET ChiaveAgenda = @Nuova, ChiaveAccountOrigine = '' WHERE ChiaveAgenda = @Vecchia;", conn)
                                    cmd.CommandTimeout = CommandTimeoutSeconds
                                    cmd.Parameters.AddWithValue("@Nuova", agendaLocale)
                                    cmd.Parameters.AddWithValue("@Vecchia", voce.Chiave)
                                    cmd.ExecuteNonQuery()
                                End Using
                                Using cmd As New SQLiteCommand("DELETE FROM Agende WHERE Chiave = @Chiave;", conn)
                                    cmd.CommandTimeout = CommandTimeoutSeconds
                                    cmd.Parameters.AddWithValue("@Chiave", voce.Chiave)
                                    cmd.ExecuteNonQuery()
                                End Using
                            End Using
                            Return True
                        End Function, "RimuoviAgendaAccount")
                    If String.Equals(_agendaCorrente, voce.Chiave, StringComparison.Ordinal) Then _agendaCorrente = ""
                Catch exVoce As Exception
                    log.ScriviLog("CLSAgenda.RimuoviAgendaAccount voce - " & exVoce.ToString())
                End Try
            Next
            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.RimuoviAgendaAccount - " & ex.ToString())
            Return False
        End Try
    End Function

    ' ============================================================================================
    ' AGENDA CORRENTE
    ' ============================================================================================

    ' Con un'agenda selezionata tutte le consultazioni mostrano soltanto i suoi appuntamenti.
    ' Lasciandola vuota si vedono gli appuntamenti di tutte le agende leggibili.
    Public Property AgendaCorrente As String
        Get
            Return If(_agendaCorrente, "")
        End Get
        Set(value As String)
            _agendaCorrente = If(value, "").Trim()
        End Set
    End Property

    Public Function NomeAgenda(chiaveAgenda As String) As String
        Try
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If String.Equals(voce.Chiave, chiaveAgenda, StringComparison.Ordinal) Then Return voce.Nome
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.NomeAgenda - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Condizione da aggiungere alle consultazioni. Le agende protette e chiuse restano fuori
    ' anche quando non e' selezionata alcuna agenda.
    Friend Function CondizioneAgenda(parametri As Dictionary(Of String, Object)) As String
        Try
            If parametri Is Nothing Then Return ""

            If Not String.IsNullOrWhiteSpace(_agendaCorrente) Then
                parametri("@FiltroAgenda") = _agendaCorrente
                Return " Id IN (SELECT Id FROM Appuntamenti WHERE ChiaveAgenda = @FiltroAgenda) "
            End If

            Dim escluse As New List(Of String)
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If voce.Protetta AndAlso Not AgendaSbloccata(voce.Chiave) Then escluse.Add(voce.Chiave)
            Next
            If escluse.Count = 0 Then Return ""

            Dim segnaposti As New List(Of String)
            For indice As Integer = 0 To escluse.Count - 1
                Dim nome As String = "@AgendaEsclusa" & indice.ToString()
                parametri(nome) = escluse(indice)
                segnaposti.Add(nome)
            Next
            Return " Id NOT IN (SELECT Id FROM Appuntamenti WHERE ChiaveAgenda IN (" & String.Join(", ", segnaposti.ToArray()) & ")) "
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CondizioneAgenda - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Innesta la condizione nella query, badando a distinguere il caso in cui una clausola
    ' WHERE ci sia gia' da quello in cui manchi.
    Friend Function ApplicaFiltroAgenda(query As String, parametri As Dictionary(Of String, Object)) As String
        Try
            Dim condizione As String = CondizioneAgenda(parametri)
            If condizione.Length = 0 Then Return query
            Dim testo As String = If(query, "")
            If testo.ToUpperInvariant().Contains(" WHERE ") Then Return testo & " AND " & condizione
            Return testo & " WHERE " & condizione
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ApplicaFiltroAgenda - " & ex.Message)
            Return query
        End Try
    End Function

    ' ============================================================================================
    ' PROTEZIONE CON PASSWORD
    ' ============================================================================================

    Public Function AgendaProtetta(chiaveAgenda As String) As Boolean
        Try
            For Each voce As DescrizioneAgenda In OttieniAgende()
                If String.Equals(voce.Chiave, chiaveAgenda, StringComparison.Ordinal) Then Return voce.Protetta
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaProtetta - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function AgendaSbloccata(chiaveAgenda As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return True
            SyncLock _lockAgende
                Return _agendeSbloccate.ContainsKey(chiaveAgenda.Trim())
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaSbloccata - " & ex.Message)
            Return False
        End Try
    End Function

    Private Function DatiProtezioneAgenda(chiaveAgenda As String, ByRef sale As String, ByRef impronta As String) As Boolean
        sale = ""
        impronta = ""
        Try
            Dim trovato As Boolean = False
            Dim saleLocale As String = ""
            Dim improntaLocale As String = ""
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT SalePassword, ImprontaPassword FROM Agende WHERE Chiave = @Chiave LIMIT 1;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", If(chiaveAgenda, "").Trim())
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                If reader.Read() Then
                                    saleLocale = LeggiTesto(reader, "SalePassword")
                                    improntaLocale = LeggiTesto(reader, "ImprontaPassword")
                                    trovato = True
                                End If
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "DatiProtezioneAgenda")
            sale = saleLocale
            impronta = improntaLocale
            Return trovato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.DatiProtezioneAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function SbloccaAgenda(chiaveAgenda As String, password As String) As Boolean
        Try
            Dim sale As String = ""
            Dim impronta As String = ""
            If Not DatiProtezioneAgenda(chiaveAgenda, sale, impronta) Then Return False
            If impronta.Length = 0 Then Return True
            If Not WGCifratura.PasswordCorretta(password, sale, impronta) Then Return False

            Dim chiave As Byte() = WGCifratura.DerivaChiave(password, sale)
            If chiave Is Nothing Then Return False
            SyncLock _lockAgende
                _agendeSbloccate(chiaveAgenda.Trim()) = chiave
            End SyncLock

            CaricaOGeneraChiavePrivata(chiaveAgenda, chiave)
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.SbloccaAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    ' Dopo l'introduzione della cifratura a coppia di chiavi, un'agenda protetta creata prima
    ' potrebbe non avere ancora la sua coppia: viene generata al volo al primo sblocco. Da quel
    ' momento la sincronizzazione automatica potra' scrivere in quell'agenda anche da bloccata,
    ' perche' per cifrare basta la chiave pubblica, sempre disponibile una volta generata.
    Private Sub CaricaOGeneraChiavePrivata(chiaveAgenda As String, chiaveSimmetrica As Byte())
        Try
            Dim pubblicaBase64 As String = ""
            Dim privataCifrata As String = ""
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT ChiavePubblica, ChiavePrivataCifrata FROM Agende WHERE Chiave = @Chiave LIMIT 1;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", chiaveAgenda.Trim())
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                If reader.Read() Then
                                    pubblicaBase64 = LeggiTesto(reader, "ChiavePubblica")
                                    privataCifrata = LeggiTesto(reader, "ChiavePrivataCifrata")
                                End If
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "CaricaOGeneraChiavePrivata lettura")

            If String.IsNullOrWhiteSpace(pubblicaBase64) OrElse String.IsNullOrWhiteSpace(privataCifrata) Then
                GeneraEImpostaCoppiaChiavi(chiaveAgenda, chiaveSimmetrica)
                Return
            End If

            Dim privataBase64 As String = WGCifratura.Decifra(privataCifrata, chiaveSimmetrica)
            If String.IsNullOrWhiteSpace(privataBase64) Then Return

            Dim rsaMemoria As RSA = ImportaChiavePrivata(Convert.FromBase64String(privataBase64))
            SyncLock _lockAgende
                _chiaviPrivateAgende(chiaveAgenda.Trim()) = rsaMemoria
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CaricaOGeneraChiavePrivata - " & ex.ToString())
        End Try
    End Sub

    ' Genera una nuova coppia di chiavi RSA per l'agenda: la pubblica resta in chiaro nel database
    ' (cifrare con quella non richiede la password), la privata viene cifrata con la chiave
    ' simmetrica derivata dalla password e torna leggibile solo sbloccando l'agenda.
    Private Function GeneraEImpostaCoppiaChiavi(chiaveAgenda As String, chiaveSimmetrica As Byte()) As Boolean
        Try
            Using rsaNuova As RSA = RSA.Create(2048)
                Dim pubblicaBase64 As String = Convert.ToBase64String(EsportaChiavePubblica(rsaNuova))
                Dim privataBase64 As String = Convert.ToBase64String(EsportaChiavePrivata(rsaNuova))
                Dim privataCifrata As String = WGCifratura.Cifra(privataBase64, chiaveSimmetrica)

                AggiornaCampoAgenda(chiaveAgenda, "ChiavePubblica", pubblicaBase64)
                AggiornaCampoAgenda(chiaveAgenda, "ChiavePrivataCifrata", privataCifrata)

                Dim rsaMemoria As RSA = ImportaChiavePrivata(EsportaChiavePrivata(rsaNuova))
                SyncLock _lockAgende
                    _chiaviPrivateAgende(chiaveAgenda.Trim()) = rsaMemoria
                End SyncLock
            End Using
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.GeneraEImpostaCoppiaChiavi - " & ex.ToString())
            Return False
        End Try
    End Function

    ' Come per le rubriche, la password identifica l'agenda: provata su tutte, apre quella
    ' a cui appartiene.
    Public Function TrovaAgendaPerPassword(password As String) As String
        Try
            If String.IsNullOrEmpty(password) Then Return ""
            For Each voce As DescrizioneAgenda In OttieniAgende()
                Try
                    If Not voce.Protetta Then Continue For
                    If SbloccaAgenda(voce.Chiave, password) Then Return voce.Chiave
                Catch exVoce As Exception
                    log.ScriviLog("CLSAgenda.TrovaAgendaPerPassword voce - " & exVoce.Message)
                End Try
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TrovaAgendaPerPassword - " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub BloccaAgenda(chiaveAgenda As String)
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return
            SyncLock _lockAgende
                If _agendeSbloccate.ContainsKey(chiaveAgenda.Trim()) Then _agendeSbloccate.Remove(chiaveAgenda.Trim())
                Dim rsaPrivata As RSA = Nothing
                If _chiaviPrivateAgende.TryGetValue(chiaveAgenda.Trim(), rsaPrivata) Then
                    _chiaviPrivateAgende.Remove(chiaveAgenda.Trim())
                    rsaPrivata.Dispose()
                End If
            End SyncLock
            If String.Equals(_agendaCorrente, chiaveAgenda, StringComparison.Ordinal) Then _agendaCorrente = ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.BloccaAgenda - " & ex.Message)
        End Try
    End Sub

    Friend Function ChiaveCifraturaAgenda(chiaveAgenda As String) As Byte()
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return Nothing
            SyncLock _lockAgende
                Dim chiave As Byte() = Nothing
                If _agendeSbloccate.TryGetValue(chiaveAgenda.Trim(), chiave) Then Return chiave
            End SyncLock
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ChiaveCifraturaAgenda - " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' Chiave pubblica dell'agenda: non e' un segreto, leggerla dal database non richiede la
    ' password. Serve a cifrare contenuti (es. la sincronizzazione in entrata) anche ad agenda
    ' bloccata.
    Friend Function ChiavePubblicaAgenda(chiaveAgenda As String) As RSA
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return Nothing
            Dim pubblicaBase64 As String = ""
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT ChiavePubblica FROM Agende WHERE Chiave = @Chiave LIMIT 1;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Chiave", chiaveAgenda.Trim())
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                If reader.Read() Then pubblicaBase64 = LeggiTesto(reader, "ChiavePubblica")
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "ChiavePubblicaAgenda")
            If String.IsNullOrWhiteSpace(pubblicaBase64) Then Return Nothing
            Return ImportaChiavePubblica(Convert.FromBase64String(pubblicaBase64))
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ChiavePubblicaAgenda - " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Chiave privata dell'agenda: esiste in memoria solo se l'agenda e' stata sbloccata con la
    ' password in questa sessione. Serve a decifrare, quindi a mostrare i contenuti veri.
    Friend Function ChiavePrivataAgenda(chiaveAgenda As String) As RSA
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return Nothing
            SyncLock _lockAgende
                Dim rsa As RSA = Nothing
                If _chiaviPrivateAgende.TryGetValue(chiaveAgenda.Trim(), rsa) Then Return rsa
            End SyncLock
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ChiavePrivataAgenda - " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' ============================================================================================
    ' CIFRATURA IBRIDA (RSA + AES) PER LA SCRITTURA SENZA PASSWORD
    ' ============================================================================================
    ' Una chiave AES-256 e una chiave HMAC-SHA256 vengono generate a caso per ogni valore, usate
    ' per cifrare il testo (AES-CBC) e autenticarlo (cifra-poi-autentica); le due chiavi insieme
    ' vengono poi cifrate con la chiave pubblica RSA dell'agenda. Chi scrive (anche la
    ' sincronizzazione automatica, senza password) ha sempre la pubblica; chi legge ha bisogno
    ' della privata, quindi della password.

    ' .NET Framework non ha i metodi ExportRSAPrivateKey/ImportRSAPrivateKey/ExportRSAPublicKey/
    ' ImportRSAPublicKey (arrivati solo con .NET Core 3.0/.NET 5+): la coppia di chiavi viene
    ' quindi serializzata a mano a partire da RSAParameters, disponibile fin dalle prime versioni
    ' del Framework.
    Private Function SerializzaCampiChiave(ParamArray campi As Byte()()) As Byte()
        Using memoria As New MemoryStream()
            Using scrittore As New BinaryWriter(memoria)
                scrittore.Write(campi.Length)
                For Each campo As Byte() In campi
                    Dim lunghezza As Integer = If(campo Is Nothing, 0, campo.Length)
                    scrittore.Write(lunghezza)
                    If lunghezza > 0 Then scrittore.Write(campo)
                Next
            End Using
            Return memoria.ToArray()
        End Using
    End Function

    Private Function DeserializzaCampiChiave(dati As Byte()) As Byte()()
        Using memoria As New MemoryStream(dati)
            Using lettore As New BinaryReader(memoria)
                Dim conteggio As Integer = lettore.ReadInt32()
                Dim risultato(conteggio - 1)() As Byte
                For indice As Integer = 0 To conteggio - 1
                    Dim lunghezza As Integer = lettore.ReadInt32()
                    risultato(indice) = If(lunghezza > 0, lettore.ReadBytes(lunghezza), New Byte() {})
                Next
                Return risultato
            End Using
        End Using
    End Function

    Private Function EsportaChiavePubblica(rsa As RSA) As Byte()
        Dim parametri As RSAParameters = rsa.ExportParameters(False)
        Return SerializzaCampiChiave(parametri.Modulus, parametri.Exponent)
    End Function

    Private Function EsportaChiavePrivata(rsa As RSA) As Byte()
        Dim parametri As RSAParameters = rsa.ExportParameters(True)
        Return SerializzaCampiChiave(parametri.Modulus, parametri.Exponent, parametri.D,
            parametri.P, parametri.Q, parametri.DP, parametri.DQ, parametri.InverseQ)
    End Function

    Private Function ImportaChiavePubblica(dati As Byte()) As RSA
        Dim campi As Byte()() = DeserializzaCampiChiave(dati)
        Dim parametri As New RSAParameters()
        parametri.Modulus = campi(0)
        parametri.Exponent = campi(1)
        Dim rsa As RSA = RSA.Create()
        rsa.ImportParameters(parametri)
        Return rsa
    End Function

    Private Function ImportaChiavePrivata(dati As Byte()) As RSA
        Dim campi As Byte()() = DeserializzaCampiChiave(dati)
        Dim parametri As New RSAParameters()
        parametri.Modulus = campi(0)
        parametri.Exponent = campi(1)
        parametri.D = campi(2)
        parametri.P = campi(3)
        parametri.Q = campi(4)
        parametri.DP = campi(5)
        parametri.DQ = campi(6)
        parametri.InverseQ = campi(7)
        Dim rsa As RSA = RSA.Create()
        rsa.ImportParameters(parametri)
        Return rsa
    End Function

    ' Cifra con la libreria nativa (WGRSA2). Se non e utilizzabile o fallisce scrive il motivo nel log e usa il sistema precedente (WGRSA1).
    Private Function CifraIbrido(valore As String, chiavePubblica As RSA) As String
        Try
            If chiavePubblica Is Nothing Then Return valore
            If WGCifraturaNativa.Funzionante Then
                Dim nativo As String = CifraIbridoNativo(valore, chiavePubblica)
                If nativo IsNot Nothing Then Return nativo
            Else
                log.ScriviLog("CifraIbrido: libreria nativa non utilizzabile (" & WGCifraturaNativa.MotivoNonFunzionante & "), uso il sistema precedente.")
            End If
        Catch ex As Exception
            log.ScriviLog("CifraIbrido: " & (ex.GetType().Name & ": " & ex.Message))
        End Try
        Return CifraIbridoPrecedente(valore, chiavePubblica)
    End Function

    Private Function CifraIbridoNativo(valore As String, chiavePubblica As RSA) As String
        Try
            Dim chiaveDati As Byte() = WGCifraturaNativa.Casuali(32)
            If chiaveDati Is Nothing Then
                log.ScriviLog("CifraIbridoNativo: numeri casuali non riusciti (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(chiaveDati, Encoding.UTF8.GetBytes(valore))
            If cifrato Is Nothing Then
                log.ScriviLog("CifraIbridoNativo: cifratura nativa non riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            ' Prima di accettarla si rilegge: se il testo non torna uguale si usa il sistema precedente.
            Dim riletto As Byte() = WGCifraturaNativa.DecifraConChiave(chiaveDati, cifrato)
            If riletto Is Nothing OrElse Not String.Equals(Encoding.UTF8.GetString(riletto), valore, StringComparison.Ordinal) Then
                log.ScriviLog("CifraIbridoNativo: la verifica di rilettura non e riuscita (" & WGCifraturaNativa.DettaglioNativo() & "), uso il sistema precedente.")
                Return Nothing
            End If
            Dim chiaveCifrata As Byte() = chiavePubblica.Encrypt(chiaveDati, RSAEncryptionPadding.OaepSHA256)
            Array.Clear(chiaveDati, 0, chiaveDati.Length)
            Return PrefissoCifraturaIbridaGcm & Convert.ToBase64String(chiaveCifrata) & "." & Convert.ToBase64String(cifrato)
        Catch ex As Exception
            log.ScriviLog("CifraIbridoNativo: " & (ex.GetType().Name & ": " & ex.Message) & " Si usa il sistema precedente.")
            Return Nothing
        End Try
    End Function

    Private Function CifraIbridoPrecedente(valore As String, chiavePubblica As RSA) As String
        Try
            If chiavePubblica Is Nothing Then Return valore
            Dim datiCombinati(63) As Byte
            Using generatore As RandomNumberGenerator = RandomNumberGenerator.Create()
                generatore.GetBytes(datiCombinati)
            End Using
            Dim chiaveAes(31) As Byte
            Dim chiaveMac(31) As Byte
            Array.Copy(datiCombinati, 0, chiaveAes, 0, 32)
            Array.Copy(datiCombinati, 32, chiaveMac, 0, 32)

            Dim testoCifrato As Byte()
            Dim iv As Byte()
            Using aes As Aes = Aes.Create()
                aes.KeySize = 256
                aes.Key = chiaveAes
                aes.GenerateIV()
                iv = aes.IV
                Using cifratore As ICryptoTransform = aes.CreateEncryptor()
                    Dim testoChiaro As Byte() = Encoding.UTF8.GetBytes(valore)
                    testoCifrato = cifratore.TransformFinalBlock(testoChiaro, 0, testoChiaro.Length)
                End Using
            End Using

            Dim tag As Byte()
            Using hmac As New HMACSHA256(chiaveMac)
                Dim datiAutenticati(iv.Length + testoCifrato.Length - 1) As Byte
                Array.Copy(iv, 0, datiAutenticati, 0, iv.Length)
                Array.Copy(testoCifrato, 0, datiAutenticati, iv.Length, testoCifrato.Length)
                tag = hmac.ComputeHash(datiAutenticati)
            End Using

            Dim chiaviCifrate As Byte() = chiavePubblica.Encrypt(datiCombinati, RSAEncryptionPadding.OaepSHA256)

            Return PrefissoCifraturaIbrida &
                Convert.ToBase64String(chiaviCifrate) & "." &
                Convert.ToBase64String(iv) & "." &
                Convert.ToBase64String(testoCifrato) & "." &
                Convert.ToBase64String(tag)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CifraIbrido - " & ex.ToString())
            Return valore
        End Try
    End Function

    Private Function DecifraIbrido(valoreCifrato As String, chiavePrivata As RSA) As String
        Try
            If chiavePrivata Is Nothing Then Return ""
            If valoreCifrato.StartsWith(PrefissoCifraturaIbridaGcm, StringComparison.Ordinal) Then
                Dim partiGcm As String() = valoreCifrato.Substring(PrefissoCifraturaIbridaGcm.Length).Split("."c)
                If partiGcm.Length <> 2 Then Return ""
                Dim chiaveDati As Byte() = chiavePrivata.Decrypt(Convert.FromBase64String(partiGcm(0)), RSAEncryptionPadding.OaepSHA256)
                Dim chiaro As Byte() = WGCifraturaNativa.DecifraConChiave(chiaveDati, Convert.FromBase64String(partiGcm(1)))
                Array.Clear(chiaveDati, 0, chiaveDati.Length)
                If chiaro Is Nothing Then Return ""
                Return Encoding.UTF8.GetString(chiaro)
            End If
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CifraturaIbrida - " & (ex.GetType().Name & ": " & ex.Message))
            Return ""
        End Try
        Return DecifraIbridoPrecedente(valoreCifrato, chiavePrivata)
    End Function

    ' Formato WGRSA1 (AES-CBC con HMAC gestiti): solo per leggere i valori scritti prima della libreria nativa.
    Private Function DecifraIbridoPrecedente(valoreCifrato As String, chiavePrivata As RSA) As String
        Try
            If chiavePrivata Is Nothing Then Return ""
            Dim corpo As String = valoreCifrato.Substring(PrefissoCifraturaIbrida.Length)
            Dim parti As String() = corpo.Split("."c)
            If parti.Length <> 4 Then Return ""

            Dim chiaviCifrate As Byte() = Convert.FromBase64String(parti(0))
            Dim iv As Byte() = Convert.FromBase64String(parti(1))
            Dim testoCifrato As Byte() = Convert.FromBase64String(parti(2))
            Dim tag As Byte() = Convert.FromBase64String(parti(3))

            Dim datiCombinati As Byte() = chiavePrivata.Decrypt(chiaviCifrate, RSAEncryptionPadding.OaepSHA256)
            If datiCombinati.Length <> 64 Then Return ""
            Dim chiaveAes(31) As Byte
            Dim chiaveMac(31) As Byte
            Array.Copy(datiCombinati, 0, chiaveAes, 0, 32)
            Array.Copy(datiCombinati, 32, chiaveMac, 0, 32)

            Dim tagAtteso As Byte()
            Using hmac As New HMACSHA256(chiaveMac)
                Dim datiAutenticati(iv.Length + testoCifrato.Length - 1) As Byte
                Array.Copy(iv, 0, datiAutenticati, 0, iv.Length)
                Array.Copy(testoCifrato, 0, datiAutenticati, iv.Length, testoCifrato.Length)
                tagAtteso = hmac.ComputeHash(datiAutenticati)
            End Using
            If Not ConfrontaByteACiclo(tag, tagAtteso) Then Return ""

            Using aes As Aes = Aes.Create()
                aes.KeySize = 256
                aes.Key = chiaveAes
                aes.IV = iv
                Using decifratore As ICryptoTransform = aes.CreateDecryptor()
                    Dim testoChiaro As Byte() = decifratore.TransformFinalBlock(testoCifrato, 0, testoCifrato.Length)
                    Return Encoding.UTF8.GetString(testoChiaro)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.DecifraIbrido - " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EValoreIbrido(valore As String) As Boolean
        Try
            Return Not String.IsNullOrEmpty(valore) AndAlso (valore.StartsWith(PrefissoCifraturaIbrida, StringComparison.Ordinal) OrElse valore.StartsWith(PrefissoCifraturaIbridaGcm, StringComparison.Ordinal))
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function ConfrontaByteACiclo(a As Byte(), b As Byte()) As Boolean
        Try
            If a Is Nothing OrElse b Is Nothing Then Return False
            If a.Length <> b.Length Then Return False
            Dim differenza As Integer = 0
            For indice As Integer = 0 To a.Length - 1
                differenza = differenza Or (a(indice) Xor b(indice))
            Next
            Return differenza = 0
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Function ImpostaPasswordAgenda(chiaveAgenda As String, password As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveAgenda) OrElse String.IsNullOrEmpty(password) Then Return False
            If AgendaProtetta(chiaveAgenda) Then Return False

            Dim sale As String = WGCifratura.GeneraSale()
            Dim impronta As String = WGCifratura.CalcolaImpronta(password, sale)
            Dim chiave As Byte() = WGCifratura.DerivaChiave(password, sale)
            If String.IsNullOrWhiteSpace(sale) OrElse String.IsNullOrWhiteSpace(impronta) OrElse chiave Is Nothing Then Return False

            CambiaCifraturaAppuntamenti(chiaveAgenda, Nothing, chiave)

            AggiornaCampoAgenda(chiaveAgenda, "Protetta", 1)
            AggiornaCampoAgenda(chiaveAgenda, "SalePassword", sale)
            AggiornaCampoAgenda(chiaveAgenda, "ImprontaPassword", impronta)

            SyncLock _lockAgende
                _agendeSbloccate(chiaveAgenda.Trim()) = chiave
            End SyncLock
            GeneraEImpostaCoppiaChiavi(chiaveAgenda, chiave)
            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.ImpostaPasswordAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function TogliPasswordAgenda(chiaveAgenda As String) As Boolean
        Try
            If Not AgendaProtetta(chiaveAgenda) Then Return True
            Dim chiave As Byte() = ChiaveCifraturaAgenda(chiaveAgenda)
            If chiave Is Nothing Then Return False

            CambiaCifraturaAppuntamenti(chiaveAgenda, chiave, Nothing)

            AggiornaCampoAgenda(chiaveAgenda, "Protetta", 0)
            AggiornaCampoAgenda(chiaveAgenda, "SalePassword", "")
            AggiornaCampoAgenda(chiaveAgenda, "ImprontaPassword", "")
            AggiornaCampoAgenda(chiaveAgenda, "ChiavePubblica", "")
            AggiornaCampoAgenda(chiaveAgenda, "ChiavePrivataCifrata", "")
            BloccaAgenda(chiaveAgenda)
            SegnalaScritturaPerCheckpoint()
            Return True
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TogliPasswordAgenda - " & ex.Message)
            Return False
        End Try
    End Function

    ' Riscrive i campi personali degli appuntamenti passando dalla chiave vecchia alla nuova.
    ' Con chiaveVecchia a Nothing cifra, con chiaveNuova a Nothing riporta in chiaro.
    Private Sub CambiaCifraturaAppuntamenti(chiaveAgenda As String, chiaveVecchia As Byte(), chiaveNuova As Byte())
        Try
            Dim identificativi As New List(Of String)
            Dim valori As New Dictionary(Of String, Dictionary(Of String, String))

            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT Id, Oggetto, Nome, Note, Luogo FROM Appuntamenti WHERE ChiaveAgenda = @Agenda;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Agenda", If(chiaveAgenda, "").Trim())
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                While reader.Read()
                                    Dim identificativo As String = LeggiTesto(reader, "Id")
                                    Dim campi As New Dictionary(Of String, String)
                                    For Each campo As String In CampiRiservatiAgenda
                                        campi(campo) = LeggiTesto(reader, campo)
                                    Next
                                    identificativi.Add(identificativo)
                                    valori(identificativo) = campi
                                End While
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "CambiaCifraturaAppuntamenti lettura")

            For Each identificativo As String In identificativi
                Try
                    Dim campi As Dictionary(Of String, String) = valori(identificativo)
                    Dim nuovi As New Dictionary(Of String, String)
                    For Each campo As String In CampiRiservatiAgenda
                        Dim valore As String = campi(campo)
                        If valore.Length = 0 Then Continue For
                        If chiaveVecchia IsNot Nothing Then
                            If EValoreIbrido(valore) Then
                                Dim rsaPrivata As RSA = ChiavePrivataAgenda(chiaveAgenda)
                                valore = DecifraIbrido(valore, rsaPrivata)
                            Else
                                valore = WGCifratura.Decifra(valore, chiaveVecchia)
                            End If
                        End If
                        If chiaveNuova IsNot Nothing Then valore = WGCifratura.Cifra(valore, chiaveNuova)
                        nuovi(campo) = valore
                    Next
                    If nuovi.Count = 0 Then Continue For

                    Dim assegnazioni As New List(Of String)
                    For Each campo As String In nuovi.Keys
                        assegnazioni.Add(campo & " = @" & campo)
                    Next

                    EseguiConRetry(Of Boolean)(
                        Function()
                            Using conn As SQLiteConnection = ApriConnessione()
                                Using cmd As New SQLiteCommand("UPDATE Appuntamenti SET " & String.Join(", ", assegnazioni.ToArray()) & " WHERE Id = @Id;", conn)
                                    cmd.CommandTimeout = CommandTimeoutSeconds
                                    For Each campo As String In nuovi.Keys
                                        cmd.Parameters.AddWithValue("@" & campo, nuovi(campo))
                                    Next
                                    cmd.Parameters.AddWithValue("@Id", identificativo)
                                    cmd.ExecuteNonQuery()
                                End Using
                            End Using
                            Return True
                        End Function, "CambiaCifraturaAppuntamenti scrittura")
                Catch exVoce As Exception
                    log.ScriviLog("CLSAgenda.CambiaCifraturaAppuntamenti voce - " & exVoce.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.CambiaCifraturaAppuntamenti - " & ex.Message)
        End Try
    End Sub

    ' Le consultazioni non portano con se' l'agenda di ogni riga. Poiche' le agende chiuse
    ' sono gia' escluse dal filtro, un valore cifrato appartiene per forza a un'agenda aperta:
    ' si prova con le chiavi disponibili e si tiene la prima che funziona.
    Friend Function TestoLeggibile(valore As String) As String
        Try
            If EValoreIbrido(valore) Then
                Dim chiaviRSA As New List(Of RSA)
                SyncLock _lockAgende
                    chiaviRSA.AddRange(_chiaviPrivateAgende.Values)
                End SyncLock
                For Each chiavePrivata As RSA In chiaviRSA
                    Try
                        Dim risultatoRSA As String = DecifraIbrido(valore, chiavePrivata)
                        If risultatoRSA.Length > 0 Then Return risultatoRSA
                    Catch exChiave As Exception
                        log.ScriviLog("CLSAgenda.TestoLeggibile chiave ibrida - " & exChiave.Message)
                    End Try
                Next
                Return ""
            End If
            If Not WGCifratura.ECifrato(valore) Then Return valore
            Dim chiavi As New List(Of Byte())
            SyncLock _lockAgende
                chiavi.AddRange(_agendeSbloccate.Values)
            End SyncLock
            For Each chiave As Byte() In chiavi
                Try
                    Dim risultato As String = WGCifratura.Decifra(valore, chiave)
                    If risultato.Length > 0 Then Return risultato
                Catch exChiave As Exception
                    log.ScriviLog("CLSAgenda.TestoLeggibile chiave - " & exChiave.Message)
                End Try
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TestoLeggibile - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Agenda a cui appartiene un appuntamento gia' salvato.
    Friend Function AgendaDellAppuntamento(identificativo As String) As String
        Try
            Dim risultato As String = ""
            EseguiConRetry(Of Boolean)(
                Function()
                    Using conn As SQLiteConnection = ApriConnessione()
                        Using cmd As New SQLiteCommand("SELECT ChiaveAgenda FROM Appuntamenti WHERE Id = @Id LIMIT 1;", conn)
                            cmd.CommandTimeout = CommandTimeoutSeconds
                            cmd.Parameters.AddWithValue("@Id", If(identificativo, ""))
                            Using reader As SQLiteDataReader = cmd.ExecuteReader()
                                If reader.Read() Then risultato = LeggiTesto(reader, "ChiaveAgenda")
                            End Using
                        End Using
                    End Using
                    Return True
                End Function, "AgendaDellAppuntamento")
            Return risultato
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaDellAppuntamento - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Agenda in cui salvare un nuovo appuntamento: quella scelta, altrimenti la predefinita.
    Friend Function AgendaPerNuoviAppuntamenti() As String
        Try
            If Not String.IsNullOrWhiteSpace(_agendaCorrente) Then Return _agendaCorrente
            Return OttieniOCreaAgendaPredefinita()
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaPerNuoviAppuntamenti - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Cifra un valore se l'agenda di destinazione e' protetta: basta la chiave pubblica, che e'
    ' sempre disponibile una volta generata (vedi GeneraEImpostaCoppiaChiavi), quindi funziona
    ' anche quando l'agenda e' bloccata - la sincronizzazione automatica non ha mai bisogno della
    ' password per scrivere.
    Friend Function TestoDaSalvare(valore As String, chiaveAgenda As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return valore
            If Not AgendaProtetta(chiaveAgenda) Then Return valore
            Using pubblica As RSA = ChiavePubblicaAgenda(chiaveAgenda)
                If pubblica Is Nothing Then Return valore
                Return CifraIbrido(valore, pubblica)
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TestoDaSalvare - " & ex.Message)
            Return valore
        End Try
    End Function

    ' Riporta in chiaro un valore letto dal database, quando appartiene a un'agenda sbloccata:
    ' riconosce sia il vecchio formato simmetrico sia il nuovo formato a coppia di chiavi.
    Friend Function TestoLeggibileAgenda(valore As String, chiaveAgenda As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return valore
            If EValoreIbrido(valore) Then
                Dim rsaPrivata As RSA = ChiavePrivataAgenda(chiaveAgenda)
                If rsaPrivata Is Nothing Then Return ""
                Return DecifraIbrido(valore, rsaPrivata)
            End If
            If Not WGCifratura.ECifrato(valore) Then Return valore
            Dim chiave As Byte() = ChiaveCifraturaAgenda(chiaveAgenda)
            If chiave Is Nothing Then Return ""
            Return WGCifratura.Decifra(valore, chiave)
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.TestoLeggibileAgenda - " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Function AgendaPerSincronizzazione(chiaveAccount As String, urlCalendario As String) As String
        Try
            If String.IsNullOrWhiteSpace(chiaveAccount) Then Return OttieniOCreaAgendaPredefinita()
            Dim trovata As String = ""
            Using conn As SQLiteConnection = ApriConnessione()
                If Not String.IsNullOrWhiteSpace(urlCalendario) Then
                    Using cmd As New SQLiteCommand("SELECT Chiave FROM Agende WHERE ChiaveAccount = @Account AND UrlCalendario = @Url LIMIT 1", conn)
                        cmd.CommandTimeout = CommandTimeoutSeconds
                        cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                        cmd.Parameters.AddWithValue("@Url", urlCalendario)
                        Dim valore As Object = cmd.ExecuteScalar()
                        If valore IsNot Nothing AndAlso valore IsNot DBNull.Value Then trovata = valore.ToString()
                    End Using
                End If
                If String.IsNullOrWhiteSpace(trovata) Then
                    Using cmd As New SQLiteCommand("SELECT Chiave FROM Agende WHERE ChiaveAccount = @Account ORDER BY Predefinita DESC, Id ASC LIMIT 1", conn)
                        cmd.CommandTimeout = CommandTimeoutSeconds
                        cmd.Parameters.AddWithValue("@Account", chiaveAccount)
                        Dim valore As Object = cmd.ExecuteScalar()
                        If valore IsNot Nothing AndAlso valore IsNot DBNull.Value Then trovata = valore.ToString()
                    End Using
                End If
            End Using
            If String.IsNullOrWhiteSpace(trovata) Then Return OttieniOCreaAgendaPredefinita()
            Return trovata
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaPerSincronizzazione - " & ex.Message)
            Return OttieniOCreaAgendaPredefinita()
        End Try
    End Function

    ' True se l'agenda puo' ricevere testo cifrato in scrittura: basta la chiave pubblica, quindi
    ' vale anche da bloccata (a meno che l'agenda non abbia ancora generato la sua coppia di
    ' chiavi, cosa che succede automaticamente al primo sblocco dopo l'aggiornamento).
    Friend Function AgendaPuoRicevereTesto(chiaveAgenda As String) As Boolean
        Try
            If Not AgendaProtetta(chiaveAgenda) Then Return True
            Using pubblica As RSA = ChiavePubblicaAgenda(chiaveAgenda)
                Return pubblica IsNot Nothing
            End Using
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaPuoRicevereTesto - " & ex.Message)
            Return True
        End Try
    End Function

    ' True se l'agenda puo' fornire testo in chiaro (serve la chiave privata, quindi solo se
    ' sbloccata con la password in questa sessione). Usata prima di inviare i contenuti veri a un
    ' server esterno, dove non basta la sola chiave pubblica.
    Friend Function AgendaPuoInviareTesto(chiaveAgenda As String) As Boolean
        Try
            If Not AgendaProtetta(chiaveAgenda) Then Return True
            Return ChiavePrivataAgenda(chiaveAgenda) IsNot Nothing
        Catch ex As Exception
            log.ScriviLog("CLSAgenda.AgendaPuoInviareTesto - " & ex.Message)
            Return True
        End Try
    End Function

End Class