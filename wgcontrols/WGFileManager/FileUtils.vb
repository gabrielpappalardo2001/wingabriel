Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports System.Xml
Imports wgcontrols
Imports System.IO
Imports System.Data.SQLite
Imports System.Collections.Generic
Imports WGDb

' Gestione degli elementi preferiti e recenti, con salvataggio su database, ordinamento e funzioni di backup dedicate.
Public Enum TabellaPreferitiRecenti
    PreferitiDir
    PreferitiFile
    Recenti
End Enum

Friend Class GestoreDB
    Private Shared Function RisolviFileDbArchivio() As String
        Try
            Dim percorso As String = WGDatabase.PercorsoFileDati("File", "archivio.db")
            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch
        End Try
        Return Path.Combine(Application.StartupPath, "Dati", "File", "archivio.db")
    End Function

    Private Shared dbPath As String = RisolviFileDbArchivio()
    Private Shared connectionString As String = $"Data Source={dbPath};Version=3;Journal Mode=WAL;Pooling=True;"
    Private Shared ReadOnly dbLock As New Object()
    Private Shared ReadOnly logDb As New Logger()

    Private Shared Function NomeTabella(tabella As TabellaPreferitiRecenti) As String
        Select Case tabella
            Case TabellaPreferitiRecenti.PreferitiDir
                Return "PreferitiDir"
            Case TabellaPreferitiRecenti.PreferitiFile
                Return "PreferitiFile"
            Case Else
                Return "Recenti"
        End Select
    End Function

    Public Shared Sub Inizializza()
        Try
            Dim cartella As String = Path.GetDirectoryName(dbPath)
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)

            If Not File.Exists(dbPath) Then SQLiteConnection.CreateFile(dbPath)

            Dim conn As SQLiteConnection = WGDatabase.ApriConnessione(dbPath, "Version=3;Journal Mode=WAL;Pooling=True;")
            If conn Is Nothing Then
                conn = New SQLiteConnection(connectionString)
                conn.Open()
            End If
            Using conn
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS PreferitiDir (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Percorso TEXT UNIQUE COLLATE NOCASE, DataInserimento INTEGER);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS PreferitiFile (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Percorso TEXT UNIQUE COLLATE NOCASE, DataInserimento INTEGER);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Recenti (Id INTEGER PRIMARY KEY AUTOINCREMENT, Nome TEXT, Percorso TEXT UNIQUE COLLATE NOCASE, DataInserimento INTEGER);"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS MigrazioniCompletate (File TEXT, Tabella TEXT, PRIMARY KEY (File, Tabella));"
                    cmd.ExecuteNonQuery()

                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_prefdir_percorso ON PreferitiDir(Percorso);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_preffile_percorso ON PreferitiFile(Percorso);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_recenti_percorso ON Recenti(Percorso);"
                    cmd.ExecuteNonQuery()
                    cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_recenti_data ON Recenti(DataInserimento DESC);"
                    cmd.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "PreferitiDir")
                    WGDatabase.AssicuraColonnaChiave(conn, "PreferitiFile")
                    WGDatabase.AssicuraColonnaChiave(conn, "Recenti")
                Catch exChiave As Exception
                    logDb.ScriviLog("GestoreDB.Inizializza chiave: " & exChiave.Message)
                End Try
            End Using
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Inizializza - Errore: " & ex.Message)
        End Try
    End Sub

    Private Shared Function GiaMigrato(nomeFile As String, nomeTab As String) As Boolean
        Try
            Using conn As New SQLiteConnection(connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT 1 FROM MigrazioniCompletate WHERE File = @f AND Tabella = @t LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@f", nomeFile)
                    cmd.Parameters.AddWithValue("@t", nomeTab)
                    Return cmd.ExecuteScalar() IsNot Nothing
                End Using
            End Using
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.GiaMigrato - Errore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub MigraDaXML(xmlPath As String, tabella As TabellaPreferitiRecenti)
        Try
            Dim nomeFileChiave As String = Path.GetFileName(xmlPath)
            Dim nomeTab As String = NomeTabella(tabella)

            If GiaMigrato(nomeFileChiave, nomeTab) Then Return
            If Not File.Exists(xmlPath) Then Return

            Dim lista As New List(Of ElementoLista)()
            Try
                Dim xmlDoc As New XmlDocument()
                xmlDoc.XmlResolver = Nothing
                xmlDoc.Load(xmlPath)

                Dim nodi As XmlNodeList = Nothing
                Select Case tabella
                    Case TabellaPreferitiRecenti.PreferitiDir
                        nodi = xmlDoc.SelectNodes("/Preferiti/Directory/Elemento")
                    Case TabellaPreferitiRecenti.PreferitiFile
                        nodi = xmlDoc.SelectNodes("/Preferiti/File/Elemento")
                    Case Else
                        nodi = xmlDoc.SelectNodes("/Recenti/Elemento")
                End Select

                If nodi IsNot Nothing Then
                    For Each nodo As XmlNode In nodi
                        Dim n = nodo.SelectSingleNode("Nome")
                        Dim p = nodo.SelectSingleNode("Percorso")
                        If n IsNot Nothing AndAlso p IsNot Nothing Then
                            lista.Add(New ElementoLista(n.InnerText, p.InnerText))
                        End If
                    Next
                End If
            Catch ex As Exception
                logDb.ScriviLog("GestoreDB.MigraDaXML - Errore lettura XML: " & ex.Message)
                Return
            End Try

            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    Using trans = conn.BeginTransaction()
                        Try
                            For Each item In lista
                                Using cmd As New SQLiteCommand($"INSERT OR IGNORE INTO {nomeTab} (Nome, Percorso, DataInserimento, ChiaveWG) VALUES (@n, @p, @d, @chiaveWG)", conn)
                                    cmd.Parameters.AddWithValue("@n", item.Testo)
                                    cmd.Parameters.AddWithValue("@p", item.Id)
                                    cmd.Parameters.AddWithValue("@d", DateTime.Now.Ticks)
                                    cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                                    cmd.ExecuteNonQuery()
                                End Using
                            Next
                            Using cmdMarca As New SQLiteCommand("INSERT OR IGNORE INTO MigrazioniCompletate (File, Tabella) VALUES (@f, @t)", conn)
                                cmdMarca.Parameters.AddWithValue("@f", nomeFileChiave)
                                cmdMarca.Parameters.AddWithValue("@t", nomeTab)
                                cmdMarca.ExecuteNonQuery()
                            End Using
                            trans.Commit()
                        Catch ex As Exception
                            trans.Rollback()
                            logDb.ScriviLog("GestoreDB.MigraDaXML - Errore transazione: " & ex.Message)
                        End Try
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.MigraDaXML - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Function LeggiElementi(tabella As TabellaPreferitiRecenti, ordinamento As String, Optional limite As Integer = -1) As List(Of ElementoLista)
        Dim lista As New List(Of ElementoLista)()
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            Dim query As String = $"SELECT Nome, Percorso FROM {nomeTab}"

            Select Case ordinamento
                Case "Nome"
                    query &= " ORDER BY Nome ASC"
                Case "DataMenoRecente"
                    query &= " ORDER BY DataInserimento ASC"
                Case Else
                    query &= " ORDER BY DataInserimento DESC"
            End Select

            If limite > 0 Then query &= $" LIMIT {limite}"

            Using conn As New SQLiteConnection(connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand(query, conn)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            lista.Add(New ElementoLista(reader("Nome").ToString(), reader("Percorso").ToString()))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.LeggiElementi - Errore: " & ex.Message)
        End Try
        Return lista
    End Function

    Public Shared Function Conta(tabella As TabellaPreferitiRecenti) As Integer
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            Using conn As New SQLiteConnection(connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand($"SELECT COUNT(*) FROM {nomeTab}", conn)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Conta - Errore: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Shared Sub Aggiungi(tabella As TabellaPreferitiRecenti, nome As String, percorso As String)
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    Using cmd As New SQLiteCommand($"INSERT OR IGNORE INTO {nomeTab} (Nome, Percorso, DataInserimento, ChiaveWG) VALUES (@n, @p, @d, @chiaveWG)", conn)
                        cmd.Parameters.AddWithValue("@n", nome)
                        cmd.Parameters.AddWithValue("@p", percorso)
                        cmd.Parameters.AddWithValue("@d", DateTime.Now.Ticks)
                        cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiaveUnivocaPerTabella(conn, nomeTab))
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Aggiungi - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub AggiornaRecente(nome As String, percorso As String, maxItems As Integer)
        Try
            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    Using trans = conn.BeginTransaction()
                        Try
                            Dim chiaveWGRecente As String = ""
                            Try
                                Using cmdLookup As New SQLiteCommand("SELECT ChiaveWG FROM Recenti WHERE Percorso = @p", conn, trans)
                                    cmdLookup.Parameters.AddWithValue("@p", percorso)
                                    Dim risultatoLookup As Object = cmdLookup.ExecuteScalar()
                                    If risultatoLookup IsNot Nothing AndAlso Not IsDBNull(risultatoLookup) Then
                                        chiaveWGRecente = risultatoLookup.ToString()
                                    End If
                                End Using
                            Catch exLookup As Exception
                                logDb.ScriviLog("GestoreDB.AggiornaRecente lookup: " & exLookup.Message)
                            End Try
                            If String.IsNullOrWhiteSpace(chiaveWGRecente) Then chiaveWGRecente = WGDatabase.GeneraChiave()

                            Using cmdDel As New SQLiteCommand("DELETE FROM Recenti WHERE Percorso = @p", conn)
                                cmdDel.Parameters.AddWithValue("@p", percorso)
                                cmdDel.ExecuteNonQuery()
                            End Using

                            Using cmdIns As New SQLiteCommand("INSERT INTO Recenti (Nome, Percorso, DataInserimento, ChiaveWG) VALUES (@n, @p, @d, @chiaveWG)", conn)
                                cmdIns.Parameters.AddWithValue("@n", nome)
                                cmdIns.Parameters.AddWithValue("@p", percorso)
                                cmdIns.Parameters.AddWithValue("@d", DateTime.Now.Ticks)
                                cmdIns.Parameters.AddWithValue("@chiaveWG", chiaveWGRecente)
                                cmdIns.ExecuteNonQuery()
                            End Using

                            Using cmdLimit As New SQLiteCommand($"DELETE FROM Recenti WHERE Id NOT IN (SELECT Id FROM Recenti ORDER BY DataInserimento DESC LIMIT {maxItems})", conn)
                                cmdLimit.ExecuteNonQuery()
                            End Using
                            trans.Commit()
                        Catch ex As Exception
                            trans.Rollback()
                            logDb.ScriviLog("GestoreDB.AggiornaRecente - Errore transazione: " & ex.Message)
                        End Try
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.AggiornaRecente - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Rimuovi(tabella As TabellaPreferitiRecenti, percorso As String)
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    Using cmd As New SQLiteCommand($"DELETE FROM {nomeTab} WHERE Percorso = @p", conn)
                        cmd.Parameters.AddWithValue("@p", percorso)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Rimuovi - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Svuota(tabella As TabellaPreferitiRecenti)
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    Using cmd As New SQLiteCommand($"DELETE FROM {nomeTab}", conn)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Svuota - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Function Esiste(tabella As TabellaPreferitiRecenti, percorso As String) As Boolean
        Try
            Dim nomeTab As String = NomeTabella(tabella)
            Using conn As New SQLiteConnection(connectionString)
                conn.Open()
                Using cmd As New SQLiteCommand($"SELECT 1 FROM {nomeTab} WHERE Percorso = @p LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@p", percorso)
                    Dim res = cmd.ExecuteScalar()
                    Return res IsNot Nothing
                End Using
            End Using
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.Esiste - Errore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub AggiornaPercorsoOvunque(vecchioPercorso As String, nuovoPercorso As String, nuovoNome As String)
        Try
            SyncLock dbLock
                Using conn As New SQLiteConnection(connectionString)
                    conn.Open()
                    For Each nomeTab As String In New String() {"PreferitiDir", "PreferitiFile", "Recenti"}
                        Try
                            Using cmd As New SQLiteCommand($"UPDATE {nomeTab} SET Percorso = @nuovo, Nome = @nome WHERE Percorso = @vecchio", conn)
                                cmd.Parameters.AddWithValue("@nuovo", nuovoPercorso)
                                cmd.Parameters.AddWithValue("@nome", nuovoNome)
                                cmd.Parameters.AddWithValue("@vecchio", vecchioPercorso)
                                cmd.ExecuteNonQuery()
                            End Using
                        Catch ex As Exception
                            logDb.ScriviLog($"GestoreDB.AggiornaPercorsoOvunque - Errore tabella {nomeTab}: " & ex.Message)
                        End Try
                    Next
                End Using
            End SyncLock
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.AggiornaPercorsoOvunque - Errore: " & ex.Message)
        End Try
    End Sub

    Public Shared Function RimuoviVociMancanti(tabella As TabellaPreferitiRecenti, verificaEsistenza As Func(Of String, Boolean)) As Integer
        Dim rimossi As Integer = 0
        Try
            Dim elementi As List(Of ElementoLista) = LeggiElementi(tabella, "Nome")
            For Each el In elementi
                Try
                    If Not verificaEsistenza(el.Id) Then
                        Rimuovi(tabella, el.Id)
                        rimossi += 1
                    End If
                Catch ex As Exception
                    logDb.ScriviLog("GestoreDB.RimuoviVociMancanti - Errore verifica elemento: " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.RimuoviVociMancanti - Errore: " & ex.Message)
        End Try
        Return rimossi
    End Function

    Public Shared Function BackupSicuro(destinazione As String) As Boolean
        Try
            SyncLock dbLock
                Using sorgente As New SQLiteConnection(connectionString)
                    sorgente.Open()
                    Using dest As New SQLiteConnection($"Data Source={destinazione};Version=3;")
                        dest.Open()
                        sorgente.BackupDatabase(dest, "main", "main", -1, Nothing, 0)
                    End Using
                End Using
            End SyncLock
            Return True
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.BackupSicuro - Errore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function RipristinaSicuro(sorgente As String) As Boolean
        Try
            If Not File.Exists(sorgente) Then Return False
            SyncLock dbLock
                SQLiteConnection.ClearAllPools()
                File.Copy(sorgente, dbPath, True)
            End SyncLock
            Return True
        Catch ex As Exception
            logDb.ScriviLog("GestoreDB.RipristinaSicuro - Errore: " & ex.Message)
            Return False
        End Try
    End Function
End Class

Public MustInherit Class ElementiPreferitiRecentiBase
    Protected log As New Logger()
    Protected lista As WGMenuLista
    Protected fileManager As MenuFile
    Protected config As New WGConfig()
    Protected ordinamentoCorrente As String = "DataRecente"
    Private _percorsoSingoloInSospeso As String = ""
    Private _sceltaCartellaBackup As WGFileDialog
    Private _campiNomeBackup As Campi
    Private _cartellaBackup As String = ""
    Private _sceltaFileRipristino As WGFileDialog

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function GetFileAttributes(ByVal lpFileName As String) As UInteger
    End Function
    Private Const FILE_ATTRIBUTE_DIRECTORY As UInteger = &H10
    Private Const INVALID_FILE_ATTRIBUTES As UInteger = &HFFFFFFFFUI

    Protected Shared Function RisorsaDiReteNonRaggiungibile(percorso As String) As Boolean
        Try
            Return WGRete.IsRemoto(percorso) AndAlso Not WGRete.Raggiungibile(percorso)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Protected Shared Function IsDirectory(percorso As String) As Boolean
        Try
            If WGRete.IsRemoto(percorso) Then Return WGRete.EsisteDirectory(percorso)
            Dim attributes As UInteger = GetFileAttributes(percorso)
            If attributes = INVALID_FILE_ATTRIBUTES Then
                Return Directory.Exists(percorso)
            End If
            Return (attributes And FILE_ATTRIBUTE_DIRECTORY) = FILE_ATTRIBUTE_DIRECTORY
        Catch
            Return Directory.Exists(percorso)
        End Try
    End Function

    Protected Shared Function IsFile(percorso As String) As Boolean
        Try
            If WGRete.IsRemoto(percorso) Then Return WGRete.EsisteFile(percorso)
            Dim attributes As UInteger = GetFileAttributes(percorso)
            If attributes = INVALID_FILE_ATTRIBUTES Then
                Return File.Exists(percorso)
            End If
            Return (attributes And FILE_ATTRIBUTE_DIRECTORY) <> FILE_ATTRIBUTE_DIRECTORY
        Catch
            Return File.Exists(percorso)
        End Try
    End Function

    Protected MustOverride ReadOnly Property Tabella As TabellaPreferitiRecenti
    Protected MustOverride ReadOnly Property TitoloLista As String
    Protected MustOverride ReadOnly Property NomeClasse As String
    Protected MustOverride ReadOnly Property NomeElementiPlurale As String
    Protected MustOverride ReadOnly Property ChiaveConfigOrdinamento As String
    Protected MustOverride ReadOnly Property ConsentiBackup As Boolean
    Protected Overridable ReadOnly Property PersistiOrdinamento As Boolean
        Get
            Return False
        End Get
    End Property

    Public Sub New(Optional fileManager As MenuFile = Nothing)
        Try
            Me.fileManager = fileManager
            GestoreDB.Inizializza()
            EseguiMigrazione()
            If PersistiOrdinamento Then
                If config.EsisteId(ChiaveConfigOrdinamento) Then
                    ordinamentoCorrente = config.LeggiValore(ChiaveConfigOrdinamento)
                Else
                    config.AggiungiValore(ChiaveConfigOrdinamento, "DataRecente")
                    ordinamentoCorrente = "DataRecente"
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".New - Errore: " & ex.Message)
        End Try
    End Sub

    Protected Overridable Sub EseguiMigrazione()
        Try
            Dim oldXml As String = Path.Combine(Application.StartupPath, "Dati", "File", "preferiti.xml")
            GestoreDB.MigraDaXML(oldXml, Tabella)
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".EseguiMigrazione - Errore: " & ex.Message)
        End Try
    End Sub

    Public Sub VisualizzaElementi()
        Try
            lista = New WGMenuLista()
            lista.impostaTitolo(TitoloLista)
            lista.multiselezioneConsentita = False
            lista.opzioniPulisci()
            lista.aggiungiOpzione("Elimina", AddressOf EliminaSelezionati)
            lista.aggiungiOpzione("Svuota", AddressOf SvuotaElementi)
            lista.aggiungiOpzione("Ordina", AddressOf MostraOpzioniOrdinamento)
            lista.aggiungiOpzione("Verifica e pulisci", AddressOf VerificaEPulisci)
            If ConsentiBackup Then
                lista.aggiungiOpzione("Backup", AddressOf EseguiBackup)
                lista.aggiungiOpzione("Ripristina backup", AddressOf EseguiRipristino)
            End If
            AddHandler lista.ElementoSelezionato, AddressOf ElementoSelezionatoInterno
            AddHandler lista.escPremuto, AddressOf ChiudiLista

            Dim conteggio As Integer = GestoreDB.Conta(Tabella)
            If conteggio = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio($"Non ci sono {NomeElementiPlurale} da visualizzare.")
                msg.ShowDialog()
                ChiudiLista()
                Return
            End If

            Dim elementi As List(Of ElementoLista) = GestoreDB.LeggiElementi(Tabella, ordinamentoCorrente)
            PopolaLista(elementi, conteggio)

            lista.avvia()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".VisualizzaElementi - Errore: " & ex.Message)
        End Try
    End Sub

    Protected Overridable Sub PopolaLista(elementi As List(Of ElementoLista), conteggio As Integer)
        Try
            lista.CaricaListaDinamica(elementi)
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".PopolaLista - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub ElementoSelezionatoInterno(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            EseguiAperturaElemento(percorso)
            lista.abbandona()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".ElementoSelezionatoInterno - Errore: " & ex.Message)
        End Try
    End Sub

    Protected MustOverride Sub EseguiAperturaElemento(percorso As String)

    Private Sub OrdinaPerNome()
        OrdinamentoSelezionato("Nome")
    End Sub

    Private Sub OrdinaPerDataRecente()
        OrdinamentoSelezionato("DataRecente")
    End Sub

    Private Sub OrdinaPerDataMenoRecente()
        OrdinamentoSelezionato("DataMenoRecente")
    End Sub

    Private Sub MostraOpzioniOrdinamento()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Per Nome", AddressOf OrdinaPerNome)
            menu.aggiungi("Per Data (più recente)", AddressOf OrdinaPerDataRecente)
            menu.aggiungi("Per Data (meno recente)", AddressOf OrdinaPerDataMenoRecente)
            menu.avvia("Menu seleziona ordinamento", True)
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".MostraOpzioniOrdinamento - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub OrdinamentoSelezionato(id As String)
        Try
            ordinamentoCorrente = id
            If PersistiOrdinamento Then
                config.ModificaValore(ChiaveConfigOrdinamento, id)
            End If
            VisualizzaElementi()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".OrdinamentoSelezionato - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub EliminaSelezionati()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati IsNot Nothing AndAlso elementiSelezionati.Any() Then
                Dim testoDomanda As String = If(elementiSelezionati.Count = 1, "l'elemento selezionato", "gli elementi selezionati")
                Dim domandaElimina As New WGDomanda()
                AddHandler domandaElimina.rispostadomanda, AddressOf DomandaEliminaSelezionatiRisposta
                domandaElimina.avvia($"Domanda: Vuoi eliminare {testoDomanda} dai {NomeElementiPlurale}?")
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio($"Seleziona uno o più {NomeElementiPlurale} da eliminare.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".EliminaSelezionati - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub DomandaEliminaSelezionatiRisposta(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                Dim percorsiDaRimuovere As List(Of String) = lista.ottieniElementi()
                For Each percorso In percorsiDaRimuovere
                    GestoreDB.Rimuovi(Tabella, percorso)
                Next
                VisualizzaElementi()
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".DomandaEliminaSelezionatiRisposta - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub SvuotaElementi()
        Try
            If GestoreDB.Conta(Tabella) > 0 Then
                Dim domandaSvuota As New WGDomanda()
                AddHandler domandaSvuota.rispostadomanda, AddressOf DomandaSvuotaRisposta
                domandaSvuota.avvia($"Domanda: Vuoi svuotare l'intera lista dei {NomeElementiPlurale}?")
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio($"La lista dei {NomeElementiPlurale} è già vuota.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".SvuotaElementi - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub DomandaSvuotaRisposta(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                GestoreDB.Svuota(Tabella)
                VisualizzaElementi()
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".DomandaSvuotaRisposta - Errore: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviElemento(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            _percorsoSingoloInSospeso = percorso
            Dim domandaRimuovi As New WGDomanda()
            AddHandler domandaRimuovi.rispostadomanda, AddressOf DomandaRimuoviElementoRisposta
            domandaRimuovi.avvia($"Vuoi rimuovere questo elemento dai {NomeElementiPlurale}?")
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".RimuoviElemento - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub DomandaRimuoviElementoRisposta(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                GestoreDB.Rimuovi(Tabella, _percorsoSingoloInSospeso)
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".DomandaRimuoviElementoRisposta - Errore: " & ex.Message)
        End Try
    End Sub

    Public Function IsElementoPresente(percorso As String) As Boolean
        Return GestoreDB.Esiste(Tabella, percorso)
    End Function

    Private Sub VerificaEPulisci()
        Try
            Dim rimossi As Integer = GestoreDB.RimuoviVociMancanti(Tabella, AddressOf VerificaEsistenzaElemento)
            Dim msg As New WGMessaggi()
            If rimossi = 0 Then
                msg.Messaggio("Tutti gli elementi sono ancora validi.")
            Else
                msg.Messaggio($"Rimossi {rimossi} elementi non più esistenti.")
            End If
            msg.ShowDialog()
            VisualizzaElementi()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".VerificaEPulisci - Errore: " & ex.Message)
        End Try
    End Sub

    Protected Overridable Function VerificaEsistenzaElemento(percorso As String) As Boolean
        If RisorsaDiReteNonRaggiungibile(percorso) Then Return True
        Return IsDirectory(percorso) OrElse IsFile(percorso)
    End Function

    Private Sub EseguiBackup()
        Try
            _sceltaCartellaBackup = New WGFileDialog()
            _sceltaCartellaBackup.MultiselezioneConsentita = False
            AddHandler _sceltaCartellaBackup.singolaselezione, AddressOf CartellaBackupScelta
            _sceltaCartellaBackup.Avvia()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".EseguiBackup - Errore: " & ex.Message)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore durante il backup.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub CartellaBackupScelta(cartella As String)
        Try
            If _sceltaCartellaBackup IsNot Nothing Then RemoveHandler _sceltaCartellaBackup.singolaselezione, AddressOf CartellaBackupScelta
            _sceltaCartellaBackup = Nothing
            If String.IsNullOrWhiteSpace(cartella) OrElse Not WGRete.EsisteDirectory(cartella) Then Return
            _cartellaBackup = cartella
            _campiNomeBackup = New Campi()
            _campiNomeBackup.Aggiungi("Nome del file di backup", "nome_backup", True, False, $"Backup_Archivio_{DateTime.Now:yyyyMMdd}.db")
            AddHandler _campiNomeBackup.Fine, AddressOf NomeBackupConfermato
            _campiNomeBackup.Avvia()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".CartellaBackupScelta - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub NomeBackupConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiNomeBackup IsNot Nothing Then
                RemoveHandler _campiNomeBackup.Fine, AddressOf NomeBackupConfermato
                nome = If(_campiNomeBackup.RecuperaValore("nome_backup"), "").Trim()
            End If
            _campiNomeBackup = Nothing
            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(_cartellaBackup) Then Return
            For Each carattere As Char In Path.GetInvalidFileNameChars()
                nome = nome.Replace(carattere, "_"c)
            Next
            If Not nome.EndsWith(".db", StringComparison.OrdinalIgnoreCase) Then nome &= ".db"
            Dim destinazione As String = Path.Combine(_cartellaBackup, nome)
            Dim msg As New WGMessaggi()
            If GestoreDB.BackupSicuro(destinazione) Then
                msg.Messaggio("Backup completato.")
            Else
                msg.Messaggio("Errore durante il backup.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".NomeBackupConfermato - Errore: " & ex.Message)
            Try
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Errore durante il backup.")
                msgErrore.ShowDialog()
            Catch exMessaggio As Exception
                log.ScriviLog(NomeClasse & ".NomeBackupConfermato - Errore messaggio: " & exMessaggio.Message)
            End Try
        End Try
    End Sub

    Private Sub EseguiRipristino()
        Try
            Dim domandaConferma As New WGDomanda()
            AddHandler domandaConferma.rispostadomanda, AddressOf DomandaRipristinoRisposta
            domandaConferma.avvia("Il ripristino sovrascriverà preferiti e recenti attuali con quelli del file di backup. Vuoi continuare?")
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".EseguiRipristino - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub DomandaRipristinoRisposta(risposta As DialogResult)
        Try
            If risposta <> DialogResult.Yes Then Return
            _sceltaFileRipristino = New WGFileDialog()
            _sceltaFileRipristino.MultiselezioneConsentita = False
            _sceltaFileRipristino.AggiungiEstensione(".db")
            AddHandler _sceltaFileRipristino.selezioneterminata, AddressOf FileRipristinoScelto
            _sceltaFileRipristino.Avvia()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".DomandaRipristinoRisposta - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub FileRipristinoScelto(elementi As List(Of String))
        Try
            If _sceltaFileRipristino IsNot Nothing Then RemoveHandler _sceltaFileRipristino.selezioneterminata, AddressOf FileRipristinoScelto
            _sceltaFileRipristino = Nothing
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
            Dim msg As New WGMessaggi()
            If GestoreDB.RipristinaSicuro(percorso) Then
                msg.Messaggio("Ripristino completato. Riapri la lista per vedere i dati aggiornati.")
                msg.ShowDialog()
                VisualizzaElementi()
            Else
                msg.Messaggio("Errore durante il ripristino.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".FileRipristinoScelto - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiLista()
        Try
            lista.abbandona()
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".ChiudiLista - Errore: " & ex.Message)
        End Try
    End Sub
End Class

Public Class Preferiti
    Inherits ElementiPreferitiRecentiBase

    Public Sub New(Optional fileManager As MenuFile = Nothing)
        MyBase.New(fileManager)
    End Sub

    Protected Overrides ReadOnly Property Tabella As TabellaPreferitiRecenti
        Get
            Return TabellaPreferitiRecenti.PreferitiDir
        End Get
    End Property

    Protected Overrides ReadOnly Property TitoloLista As String
        Get
            Return "Cartelle Preferite"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeClasse As String
        Get
            Return "Preferiti"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeElementiPlurale As String
        Get
            Return "cartelle preferite"
        End Get
    End Property

    Protected Overrides ReadOnly Property ChiaveConfigOrdinamento As String
        Get
            Return "ordinamento_preferiti_dir"
        End Get
    End Property

    Protected Overrides ReadOnly Property ConsentiBackup As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides Sub PopolaLista(elementi As List(Of ElementoLista), conteggio As Integer)
        Try
            If conteggio > 100 Then
                lista.CaricaListaDinamica(elementi)
            Else
                For Each el In elementi
                    lista.aggiungiElemento(el.Testo, el.Id)
                Next
            End If
        Catch ex As Exception
            log.ScriviLog(NomeClasse & ".PopolaLista - Errore: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub EseguiAperturaElemento(percorso As String)
        If fileManager IsNot Nothing Then
            fileManager.Avvia(percorso)
        Else
            Dim fm As New MenuFile()
            fm.Avvia(percorso)
        End If
    End Sub

    Protected Overrides Function VerificaEsistenzaElemento(percorso As String) As Boolean
        If RisorsaDiReteNonRaggiungibile(percorso) Then Return True
        Return IsDirectory(percorso)
    End Function

    Public Sub VisualizzaPreferiti()
        VisualizzaElementi()
    End Sub

    Public Sub AggiungiAPreferito(percorso As String)
        Try
            If Not IsDirectory(percorso) Then Return
            If Not GestoreDB.Esiste(Tabella, percorso) Then
                Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"))
                If String.IsNullOrEmpty(nome) Then nome = percorso
                GestoreDB.Aggiungi(Tabella, nome, percorso)
            End If
        Catch ex As Exception
            log.ScriviLog("Preferiti.AggiungiAPreferito - Errore: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviPreferito(percorso As String)
        RimuoviElemento(percorso)
    End Sub

    Public Function IsPreferito(percorso As String) As Boolean
        Return IsElementoPresente(percorso)
    End Function
End Class

Public Class FilePreferiti
    Inherits ElementiPreferitiRecentiBase

    Public Sub New(Optional fileManager As MenuFile = Nothing)
        MyBase.New(fileManager)
    End Sub

    Protected Overrides ReadOnly Property Tabella As TabellaPreferitiRecenti
        Get
            Return TabellaPreferitiRecenti.PreferitiFile
        End Get
    End Property

    Protected Overrides ReadOnly Property TitoloLista As String
        Get
            Return "File Preferiti"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeClasse As String
        Get
            Return "FilePreferiti"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeElementiPlurale As String
        Get
            Return "file preferiti"
        End Get
    End Property

    Protected Overrides ReadOnly Property ChiaveConfigOrdinamento As String
        Get
            Return "ordinamento_preferiti_file"
        End Get
    End Property

    Protected Overrides ReadOnly Property ConsentiBackup As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides Sub EseguiAperturaElemento(percorso As String)
        If fileManager IsNot Nothing Then
            fileManager.Apri(percorso)
        Else
            Dim fm As New MenuFile()
            fm.Apri(percorso)
        End If
    End Sub

    Protected Overrides Function VerificaEsistenzaElemento(percorso As String) As Boolean
        If RisorsaDiReteNonRaggiungibile(percorso) Then Return True
        Return IsFile(percorso)
    End Function

    Public Sub VisualizzaPreferiti()
        VisualizzaElementi()
    End Sub

    Public Sub AggiungiAPreferito(percorso As String)
        Try
            If Not IsFile(percorso) Then Return
            If Not GestoreDB.Esiste(Tabella, percorso) Then
                Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"))
                If String.IsNullOrEmpty(nome) Then nome = percorso
                GestoreDB.Aggiungi(Tabella, nome, percorso)
            End If
        Catch ex As Exception
            log.ScriviLog("FilePreferiti.AggiungiAPreferito - Errore: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviPreferito(percorso As String)
        RimuoviElemento(percorso)
    End Sub

    Public Function IsPreferito(percorso As String) As Boolean
        Return IsElementoPresente(percorso)
    End Function
End Class

Public Class Recenti
    Inherits ElementiPreferitiRecentiBase

    Private Const MAX_RECENTI As Integer = 50

    Public Sub New(Optional fileManager As MenuFile = Nothing)
        MyBase.New(fileManager)
    End Sub

    Protected Overrides ReadOnly Property Tabella As TabellaPreferitiRecenti
        Get
            Return TabellaPreferitiRecenti.Recenti
        End Get
    End Property

    Protected Overrides ReadOnly Property TitoloLista As String
        Get
            Return "Recenti"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeClasse As String
        Get
            Return "Recenti"
        End Get
    End Property

    Protected Overrides ReadOnly Property NomeElementiPlurale As String
        Get
            Return "elementi recenti"
        End Get
    End Property

    Protected Overrides ReadOnly Property ChiaveConfigOrdinamento As String
        Get
            Return "ordinamento_recenti"
        End Get
    End Property

    Protected Overrides ReadOnly Property ConsentiBackup As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides ReadOnly Property PersistiOrdinamento As Boolean
        Get
            Return True
        End Get
    End Property

    Protected Overrides Sub EseguiMigrazione()
        Try
            Dim oldXml As String = Path.Combine(Application.StartupPath, "Dati", "File", "recenti.xml")
            GestoreDB.MigraDaXML(oldXml, Tabella)
        Catch ex As Exception
            log.ScriviLog("Recenti.EseguiMigrazione - Errore: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub EseguiAperturaElemento(percorso As String)
        Dim fm As MenuFile = If(fileManager, New MenuFile())
        If WGRete.EsisteDirectory(percorso) Then
            fm.Avvia(percorso)
        Else
            fm.Apri(percorso)
        End If
    End Sub

    Public Sub VisualizzaRecenti()
        VisualizzaElementi()
    End Sub

    Public Sub AggiungiARecenti(percorso As String)
        Try
            If Not IsDirectory(percorso) AndAlso Not IsFile(percorso) Then Return
            Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"))
            If String.IsNullOrEmpty(nome) Then nome = percorso
            GestoreDB.AggiornaRecente(nome, percorso, MAX_RECENTI)
        Catch ex As Exception
            log.ScriviLog("Recenti.AggiungiARecenti - Errore: " & ex.Message)
        End Try
    End Sub

    Public Function IsRecente(percorso As String) As Boolean
        Return IsElementoPresente(percorso)
    End Function

    Public Sub RimuoviRecenti(percorso As String)
        RimuoviElemento(percorso)
    End Sub

    Private Sub ConfiguraPerNome()
        SettaConfigurazione("Nome")
    End Sub

    Private Sub ConfiguraPerDataRecente()
        SettaConfigurazione("DataRecente")
    End Sub

    Private Sub ConfiguraPerDataMenoRecente()
        SettaConfigurazione("DataMenoRecente")
    End Sub

    Public Sub configurazione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Ordina per Nome", AddressOf ConfiguraPerNome)
            menu.aggiungi("Ordina per Data (più recente)", AddressOf ConfiguraPerDataRecente)
            menu.aggiungi("Ordina per Data (meno recente)", AddressOf ConfiguraPerDataMenoRecente)
            menu.avvia("Configura Ordinamento Recenti", True)
        Catch ex As Exception
            log.ScriviLog("Recenti.configurazione - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub SettaConfigurazione(id As String)
        Try
            ordinamentoCorrente = id
            config.ModificaValore(ChiaveConfigOrdinamento, id)
        Catch ex As Exception
            log.ScriviLog("Recenti.SettaConfigurazione - Errore: " & ex.Message)
        End Try
    End Sub
End Class