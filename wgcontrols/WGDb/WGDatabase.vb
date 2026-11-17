Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports System.Xml

Friend NotInheritable Class LoggerWGDb

    Private ReadOnly _percorsoLog As String
    Private ReadOnly _lock As New Object()

    Public Sub New()
        Try
            Dim cartella As String = WGPercorsiApplicazione.CartellaLog()
            Try
                If Not Directory.Exists(cartella) Then
                    Directory.CreateDirectory(cartella)
                End If
            Catch
            End Try
            _percorsoLog = Path.Combine(cartella, "log.txt")
        Catch
            Try
                _percorsoLog = Path.Combine(Path.GetTempPath(), "log.txt")
            Catch
                _percorsoLog = ""
            End Try
        End Try
    End Sub

    Public Sub ScriviLog(messaggio As String)
        Try
            SyncLock _lock
                If String.IsNullOrWhiteSpace(_percorsoLog) Then Return
                Dim riga As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine
                Try
                    File.AppendAllText(_percorsoLog, riga, New UTF8Encoding(False))
                Catch
                    Try
                        Using scrittore As New StreamWriter(_percorsoLog, True, New UTF8Encoding(False))
                            scrittore.Write(riga)
                        End Using
                    Catch
                    End Try
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

End Class

' ============================================================================================
' WGDatabase - accesso unico ai dati di WinGabriel
' ============================================================================================
'
' A cosa serve
'   E' l'unico punto da cui passano le basi dati SQLite del programma. Ogni modulo che deve
'   salvare qualcosa crea una istanza di questa classe invece di aprirsi una connessione per
'   conto proprio: cosi' la cartella dei dati, il nome delle colonne chiave, le date dei record
'   e la gestione degli errori restano uguali dappertutto.
'
' Dove finiscono i file
'   La cartella base e' quella configurata in dati\config\config.xml alla voce
'   "WGDb.CartellaBaseDati"; se non e' impostata o non esiste piu' si torna automaticamente a
'   dati\ dentro la cartella del programma. Non costruire mai percorsi a mano: usare
'   CartellaDati(nomeSottocartella) oppure PercorsoFileDati(nomeSottocartella, nomeFile).
'
' Come si usa
'   Using db As New WGDatabase("agenda", "wg-agenda.sqlite")
'       db.CreaTabellaSeNonEsiste("appuntamenti", "Titolo TEXT, Inizio TEXT, Note TEXT")
'
'       Dim valori As New Dictionary(Of String, Object)
'       valori("Titolo") = "Visita"
'       valori("Inizio") = "2026-08-20 10:00:00"
'       Dim chiave As String = db.InserisciRecord("appuntamenti", valori)
'
'       valori("Note") = "portare le analisi"
'       db.AggiornaRecord("appuntamenti", chiave, valori)
'
'       Dim riga As DataRow = db.OttieniRecord("appuntamenti", chiave)
'       Dim tutti As DataTable = db.OttieniTuttiIRecord("appuntamenti", "Inizio ASC")
'
'       db.EliminaRecord("appuntamenti", chiave)
'   End Using
'
' Colonne automatiche
'   ChiaveWG          chiave primaria testuale generata dal programma (max 25 caratteri),
'                     univoca nella tabella; e' il valore restituito da InserisciRecord ed e'
'                     quello da passare ad AggiornaRecord, OttieniRecord ed EliminaRecord.
'   DataCreazioneWG   valorizzata all'inserimento e mai piu' toccata dagli aggiornamenti.
'   DataModificaWG    valorizzata all'inserimento e riscritta a ogni AggiornaRecord.
'   Il formato delle due date e' "yyyy-MM-dd HH:mm:ss" (costante FormatoDataOra), quindi si
'   possono ordinare e confrontare direttamente come testo.
'
' Tabelle gia' esistenti
'   Le tabelle create prima di questa versione non hanno le due colonne di data: vengono
'   aggiunte da sole alla prima operazione (CreaTabellaSeNonEsiste, InserisciRecord,
'   AggiornaRecord o AssicuraColonnaChiaveIstanza) e ai record gia' presenti viene assegnata la
'   data del momento della migrazione, perche' quella vera non e' piu' ricostruibile.
'
' Errori
'   Nessun metodo solleva eccezioni: in caso di problema scrivono su dati\logs\log.txt e
'   restituiscono un valore neutro (stringa vuota, False, zero, DataTable vuota o Nothing).
'   Chi chiama deve quindi verificare il valore restituito, non affidarsi a un Try/Catch.
'
' Attenzione
'   I nomi di tabella e di colonna finiscono direttamente nel testo SQL: devono venire dal
'   codice, mai da testo digitato dall'utente. I valori invece passano sempre da parametri.
' ============================================================================================
Public Class WGDatabase
    Implements IDisposable

    Public Const NomeColonnaChiavePredefinita As String = "ChiaveWG"
    Public Const NomeColonnaDataCreazione As String = "DataCreazioneWG"
    Public Const NomeColonnaDataModifica As String = "DataModificaWG"
    Public Const FormatoDataOra As String = "yyyy-MM-dd HH:mm:ss"
    Public Const LunghezzaMassimaChiave As Integer = 25

    Private Shared ReadOnly _logger As New LoggerWGDb()
    Private Shared ReadOnly _generatoreCasuale As New RNGCryptoServiceProvider()
    Private Shared ReadOnly _lockGeneratoreCasuale As New Object()
    Private Shared ReadOnly _epocaChiavi As New DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)

    Private ReadOnly _tabelleColonneDateVerificate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Private ReadOnly _connessioneIstanza As SQLiteConnection
    Private ReadOnly _percorsoFileIstanza As String
    Private ReadOnly _lockIstanza As New Object()
    Private _smaltito As Boolean = False
    Private _chiuso As Boolean = False

    Public ReadOnly Property Chiuso As Boolean
        Get
            Return _chiuso
        End Get
    End Property

    Public ReadOnly Property Valido As Boolean
        Get
            Try
                If _chiuso Then Return False
                If _connessioneIstanza Is Nothing Then Return False
                Return _connessioneIstanza.State = ConnectionState.Open
            Catch ex As Exception
                _logger.ScriviLog("WGDatabase.Valido: " & ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public Sub New(nomeSottocartella As String, nomeFile As String)
        Try
            _percorsoFileIstanza = PercorsoFileDati(nomeSottocartella, nomeFile)
            _connessioneIstanza = ApriConnessione(_percorsoFileIstanza)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.New(sottocartella,file): " & ex.ToString())
        End Try
    End Sub

    Public Sub New(percorsoFileCompleto As String)
        Try
            _percorsoFileIstanza = percorsoFileCompleto
            _connessioneIstanza = ApriConnessione(_percorsoFileIstanza)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.New(percorso): " & ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property Connessione As SQLiteConnection
        Get
            Return _connessioneIstanza
        End Get
    End Property

    Public ReadOnly Property PercorsoFile As String
        Get
            Return _percorsoFileIstanza
        End Get
    End Property

    Public Const IdConfigurazioneCartellaBaseDati As String = "WGDb.CartellaBaseDati"

    Private Shared Function PercorsoFileConfigWGConfig() As String
        Try
            ' la configurazione sta fuori dai dati: nei dati locali dell'utente se il programma è installato, accanto al programma se è portatile
            Dim propria As String = WGPercorsiApplicazione.PercorsoConfig()
            ' Come WGConfig: se WinGabriel non ha un suo config.xml ma il lettore di schermo ne ha già uno, vale quello (puntatore nel registro).
            Try
                If Not File.Exists(propria) Then
                    Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\WinGabriel")
                        If chiave IsNot Nothing Then
                            Dim condiviso As String = Convert.ToString(chiave.GetValue("PercorsoConfigurazione"))
                            If Not String.IsNullOrWhiteSpace(condiviso) AndAlso File.Exists(condiviso) Then Return condiviso
                        End If
                    End Using
                End If
            Catch
            End Try
            Return propria
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.PercorsoFileConfigWGConfig: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function OttieniCartellaBaseDatiConfigurata() As String
        Try
            Dim percorsoFileConfig As String = PercorsoFileConfigWGConfig()
            If String.IsNullOrWhiteSpace(percorsoFileConfig) OrElse Not File.Exists(percorsoFileConfig) Then Return ""

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.Load(percorsoFileConfig)

            If documento.DocumentElement Is Nothing Then Return ""

            Dim nodo As XmlNode = documento.SelectSingleNode("//Configurazione[@id='" & IdConfigurazioneCartellaBaseDati & "']")
            If nodo Is Nothing Then Return ""

            Dim valore As String = ""
            If nodo.HasChildNodes AndAlso nodo.FirstChild IsNot Nothing AndAlso nodo.FirstChild.Name = "Valore" Then
                Dim primoValore As XmlNode = nodo.SelectSingleNode("Valore[1]")
                If primoValore IsNot Nothing Then valore = primoValore.InnerText
            Else
                valore = nodo.InnerText
            End If

            If valore Is Nothing Then Return ""
            Return valore.Trim()
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.OttieniCartellaBaseDatiConfigurata: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function VerificaCartellaScrivibile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim percorsoNormalizzato As String = percorso.Trim()

            If Not Directory.Exists(percorsoNormalizzato) Then
                Directory.CreateDirectory(percorsoNormalizzato)
            End If

            Dim percorsoFileProva As String = Path.Combine(percorsoNormalizzato, ".wgdb_test_" & Guid.NewGuid().ToString("N") & ".tmp")
            File.WriteAllText(percorsoFileProva, "test", New UTF8Encoding(False))
            File.Delete(percorsoFileProva)

            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.VerificaCartellaScrivibile (" & percorso & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Delegate Sub AvanzamentoOperazioneDati(fatti As Integer, totale As Integer)

    Private Shared _avanzamentoRaccolta As AvanzamentoOperazioneDati = Nothing

    Private Shared Sub SegnalaAvanzamento(destinatario As AvanzamentoOperazioneDati, fatti As Integer, totale As Integer)
        Try
            If destinatario Is Nothing Then Return
            destinatario.Invoke(fatti, totale)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.SegnalaAvanzamento: " & ex.ToString())
        End Try
    End Sub

    Private Shared ReadOnly _lockRadiceDati As New Object()
    Private Shared _sceltaRadiceInCache As String = Nothing
    Private Shared _radiceDatiInCache As String = ""

    Public Const NomeCartellaContenitoreDati As String = "WinGabriel"
    Public Const NomeFileMarcatoreDati As String = "wingabriel.dati"

    Private Shared ReadOnly IntestazioneSqlite As Byte() = {83, 81, 76, 105, 116, 101, 32, 102, 111, 114, 109, 97, 116, 32, 51, 0}

    Public Shared ReadOnly SottocartelleDatiConosciute As String() = {
        "File", "Ftp", "InternetArchive", "Libri", "Navidrome", "agenda", "farmacie", "mail", "meteo", "treni",
        "note", "notizie", "player", "posta", "preferiti", "quotidiani", "radio",
        "riservati", "suoni", "tv", "word", "youtube", "luoghi", "menu",
        "config", "logs", "log", "cache", "browsercache", "backup", "backupwg"}

    Private Const ATTRIBUTO_RICHIAMA_ALL_APERTURA As Integer = &H40000
    Private Const ATTRIBUTO_RICHIAMA_ALL_ACCESSO As Integer = &H400000

    ' Un file va aperto solo se puo' ragionevolmente essere un nostro database. La cartella
    ' scelta dall'utente puo' essere OneDrive: li' dentro ci sono file di servizio tenuti
    ' sempre aperti dal client di sincronizzazione, e soprattutto file che stanno solo nel
    ' cloud, che il semplice fatto di aprirli farebbe scaricare. Si scartano prima di
    ' toccarli, guardando solo nome, attributi e dimensione.
    Private Shared Function FileDaNonAprire(percorso As String) As Boolean
        Try
            Dim nome As String = Path.GetFileName(percorso)
            If String.IsNullOrEmpty(nome) Then Return True
            If nome.StartsWith(".", StringComparison.Ordinal) Then Return True
            If nome.StartsWith("~", StringComparison.Ordinal) Then Return True

            Dim informazioni As New FileInfo(percorso)
            If informazioni.Length < IntestazioneSqlite.Length Then Return True

            Dim attributi As Integer = CInt(informazioni.Attributes)
            If (attributi And CInt(FileAttributes.Directory)) <> 0 Then Return True
            If (attributi And CInt(FileAttributes.Hidden)) <> 0 Then Return True
            If (attributi And CInt(FileAttributes.System)) <> 0 Then Return True
            If (attributi And CInt(FileAttributes.Offline)) <> 0 Then Return True
            If (attributi And CInt(FileAttributes.ReparsePoint)) <> 0 Then Return True
            If (attributi And ATTRIBUTO_RICHIAMA_ALL_APERTURA) <> 0 Then Return True
            If (attributi And ATTRIBUTO_RICHIAMA_ALL_ACCESSO) <> 0 Then Return True

            Return False
        Catch
            Return True
        End Try
    End Function

    Public Shared Function EFileSqlite(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return False
            If FileDaNonAprire(percorso) Then Return False

            Dim intestazione(IntestazioneSqlite.Length - 1) As Byte
            Dim letti As Integer = 0
            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                While letti < intestazione.Length
                    Dim porzione As Integer = flusso.Read(intestazione, letti, intestazione.Length - letti)
                    If porzione <= 0 Then Exit While
                    letti += porzione
                End While
            End Using
            If letti < IntestazioneSqlite.Length Then Return False
            For indice As Integer = 0 To IntestazioneSqlite.Length - 1
                If intestazione(indice) <> IntestazioneSqlite(indice) Then Return False
            Next
            Return True
        Catch exAccesso As IOException
            Return False
        Catch exPermessi As UnauthorizedAccessException
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EFileSqlite (" & If(percorso, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function PercorsoMarcatoreDati(cartella As String) As String
        Try
            If String.IsNullOrWhiteSpace(cartella) Then Return ""
            Return Path.Combine(cartella, NomeFileMarcatoreDati)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.PercorsoMarcatoreDati (" & If(cartella, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function ECartellaDatiWinGabriel(cartella As String) As Boolean
        Try
            Dim marcatore As String = PercorsoMarcatoreDati(cartella)
            If String.IsNullOrWhiteSpace(marcatore) Then Return False
            Return File.Exists(marcatore)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ECartellaDatiWinGabriel (" & If(cartella, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>Crea la cartella dei dati scelta (se manca) e la marca come cartella dei dati di WinGabriel, così viene usata com'è.</summary>
    Public Shared Sub PreparaCartellaDati(cartella As String)
        ScriviMarcatoreDati(cartella)
    End Sub

    Private Shared Sub ScriviMarcatoreDati(cartella As String)
        Try
            Dim marcatore As String = PercorsoMarcatoreDati(cartella)
            If String.IsNullOrWhiteSpace(marcatore) OrElse File.Exists(marcatore) Then Return
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            File.WriteAllText(marcatore, "Cartella dei dati di WinGabriel. Non spostare ne' rinominare questo file.", New UTF8Encoding(False))
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ScriviMarcatoreDati (" & If(cartella, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Shared Function ESottocartellaConosciuta(nome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return False
            For Each conosciuta As String In SottocartelleDatiConosciute
                If String.Equals(nome, conosciuta, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ESottocartellaConosciuta (" & If(nome, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function CartellaContieneDatabase(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not Directory.Exists(percorso) Then Return False
            Dim esaminati As Integer = 0
            For Each fileTrovato As String In Directory.EnumerateFiles(percorso, "*", SearchOption.AllDirectories)
                esaminati += 1
                If esaminati > 5000 Then Return False
                If EFileSqlite(fileTrovato) Then Return True
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CartellaContieneDatabase (" & If(percorso, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function CartellaVuota(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not Directory.Exists(percorso) Then Return False
            If Directory.GetFiles(percorso).Length > 0 Then Return False
            If Directory.GetDirectories(percorso).Length > 0 Then Return False
            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CartellaVuota (" & If(percorso, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub SpostaCompagniSqlite(origine As String, destinazione As String)
        Try
            For Each suffisso As String In New String() {"-wal", "-shm", "-journal"}
                Try
                    Dim compagnoOrigine As String = origine & suffisso
                    If Not File.Exists(compagnoOrigine) Then Continue For
                    Dim compagnoDestinazione As String = destinazione & suffisso
                    If File.Exists(compagnoDestinazione) Then File.Delete(compagnoDestinazione)
                    File.Move(compagnoOrigine, compagnoDestinazione)
                Catch exCompagno As Exception
                    _logger.ScriviLog("WGDatabase.SpostaCompagniSqlite (" & origine & suffisso & "): " & exCompagno.ToString())
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.SpostaCompagniSqlite (" & If(origine, "") & "): " & ex.ToString())
        End Try
    End Sub

    ' La cartella scelta dall'utente puo' essere una cartella qualsiasi, piena di roba sua
    ' (tipico caso: OneDrive). Qui dentro si sposta nella sottocartella dedicata soltanto
    ' cio' che WinGabriel riconosce come proprio: le sottocartelle dei moduli e i file che
    ' sono davvero database SQLite. Tutto il resto non viene mai toccato.
    Private Shared Sub RaccogliDatiSparsiNellaCartellaDedicata(cartellaScelta As String, cartellaDedicata As String)
        Try
            If String.IsNullOrWhiteSpace(cartellaScelta) OrElse String.IsNullOrWhiteSpace(cartellaDedicata) Then Return
            If Not Directory.Exists(cartellaScelta) Then Return
            If Not Directory.Exists(cartellaDedicata) Then Directory.CreateDirectory(cartellaDedicata)

            Dim avanzamento As AvanzamentoOperazioneDati = _avanzamentoRaccolta
            Dim cartelleOrigine() As String = Directory.GetDirectories(cartellaScelta)
            Dim fileOrigineRadice() As String = Directory.GetFiles(cartellaScelta)
            Dim totaleVoci As Integer = cartelleOrigine.Length + fileOrigineRadice.Length
            Dim vociFatte As Integer = 0
            SegnalaAvanzamento(avanzamento, 0, totaleVoci)

            For Each sottocartella As String In cartelleOrigine
                Try
                    vociFatte += 1
                    SegnalaAvanzamento(avanzamento, vociFatte, totaleVoci)
                    Dim nome As String = Path.GetFileName(sottocartella.TrimEnd(Path.DirectorySeparatorChar))
                    If String.Equals(nome, NomeCartellaContenitoreDati, StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not ESottocartellaConosciuta(nome) Then Continue For
                    If Not CartellaContieneDatabase(sottocartella) AndAlso Not CartellaVuota(sottocartella) Then Continue For

                    Dim destinazione As String = Path.Combine(cartellaDedicata, nome)
                    If Directory.Exists(destinazione) Then Continue For
                    Directory.Move(sottocartella, destinazione)
                Catch exSottocartella As Exception
                    _logger.ScriviLog("WGDatabase.RaccogliDatiSparsiNellaCartellaDedicata cartella (" & sottocartella & "): " & exSottocartella.ToString())
                End Try
            Next

            For Each fileOrigine As String In fileOrigineRadice
                Try
                    vociFatte += 1
                    SegnalaAvanzamento(avanzamento, vociFatte, totaleVoci)

                    Dim nomeFile As String = Path.GetFileName(fileOrigine)
                    If String.Equals(nomeFile, NomeFileMarcatoreDati, StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not EFileSqlite(fileOrigine) Then Continue For

                    Dim destinazione As String = Path.Combine(cartellaDedicata, nomeFile)
                    If File.Exists(destinazione) Then Continue For
                    File.Move(fileOrigine, destinazione)
                    SpostaCompagniSqlite(fileOrigine, destinazione)
                Catch exFile As Exception
                    _logger.ScriviLog("WGDatabase.RaccogliDatiSparsiNellaCartellaDedicata file (" & fileOrigine & "): " & exFile.ToString())
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.RaccogliDatiSparsiNellaCartellaDedicata (" & If(cartellaScelta, "") & "): " & ex.ToString())
        End Try
    End Sub

    ' Restituisce la cartella in cui i dati stanno davvero. Se l'utente ha scelto una cartella
    ' sua (OneDrive, Documenti, la radice di un disco) i dati non ci vengono sparsi dentro: si
    ' usa una sottocartella dedicata, cosi' backup, spostamenti e pulizie non toccano mai i suoi file.
    Public Shared Function MigrazioneCartellaDatiNecessaria() As Boolean
        Try
            Dim scelta As String = OttieniCartellaBaseDatiConfigurata()
            If String.IsNullOrWhiteSpace(scelta) OrElse Not Directory.Exists(scelta) Then Return False
            If ECartellaDatiWinGabriel(scelta) Then Return False

            Dim dedicata As String = Path.Combine(scelta, NomeCartellaContenitoreDati)
            If Directory.Exists(dedicata) AndAlso ECartellaDatiWinGabriel(dedicata) Then Return False

            For Each sottocartella As String In Directory.GetDirectories(scelta)
                Try
                    Dim nome As String = Path.GetFileName(sottocartella.TrimEnd(Path.DirectorySeparatorChar))
                    If String.Equals(nome, NomeCartellaContenitoreDati, StringComparison.OrdinalIgnoreCase) Then Continue For
                    If ESottocartellaConosciuta(nome) Then Return True
                Catch exSottocartella As Exception
                    _logger.ScriviLog("WGDatabase.MigrazioneCartellaDatiNecessaria cartella (" & sottocartella & "): " & exSottocartella.ToString())
                End Try
            Next

            For Each fileTrovato As String In Directory.GetFiles(scelta)
                Try
                    If EFileSqlite(fileTrovato) Then Return True
                Catch exFile As Exception
                    _logger.ScriviLog("WGDatabase.MigrazioneCartellaDatiNecessaria file (" & fileTrovato & "): " & exFile.ToString())
                End Try
            Next

            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.MigrazioneCartellaDatiNecessaria: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Function EseguiMigrazioneCartellaDati(avanzamento As AvanzamentoOperazioneDati) As String
        Try
            _avanzamentoRaccolta = avanzamento
            AzzeraCacheCartellaBaseDati()
            Return CartellaBaseDatiEffettiva()
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EseguiMigrazioneCartellaDati: " & ex.ToString())
            Return ""
        Finally
            _avanzamentoRaccolta = Nothing
        End Try
    End Function

    Public Shared Sub AzzeraCacheCartellaBaseDati()
        Try
            SyncLock _lockRadiceDati
                _sceltaRadiceInCache = Nothing
                _radiceDatiInCache = ""
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AzzeraCacheCartellaBaseDati: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function CartellaBaseDatiEffettiva() As String
        Try
            Dim scelta As String = ""
            Try
                scelta = OttieniCartellaBaseDatiConfigurata()
            Catch exScelta As Exception
                _logger.ScriviLog("WGDatabase.CartellaBaseDatiEffettiva lettura configurazione: " & exScelta.ToString())
                scelta = ""
            End Try

            SyncLock _lockRadiceDati
                If _sceltaRadiceInCache IsNot Nothing AndAlso
                   String.Equals(_sceltaRadiceInCache, If(scelta, ""), StringComparison.OrdinalIgnoreCase) AndAlso
                   Not String.IsNullOrWhiteSpace(_radiceDatiInCache) AndAlso
                   Directory.Exists(_radiceDatiInCache) Then
                    Return _radiceDatiInCache
                End If
            End SyncLock

            Dim risolta As String = RisolviCartellaBaseDatiEffettiva(scelta)

            SyncLock _lockRadiceDati
                _sceltaRadiceInCache = If(scelta, "")
                _radiceDatiInCache = If(risolta, "")
            End SyncLock

            Return risolta
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CartellaBaseDatiEffettiva: " & ex.ToString())
            Return CartellaBaseDatiPredefinita()
        End Try
    End Function

    Private Shared Function RisolviCartellaBaseDatiEffettiva(scelta As String) As String
        Try
            If String.IsNullOrWhiteSpace(scelta) OrElse Not Directory.Exists(scelta) Then
                Dim predefinita As String = CartellaBaseDatiPredefinita()
                If Not String.IsNullOrWhiteSpace(predefinita) Then
                    If Not Directory.Exists(predefinita) Then Directory.CreateDirectory(predefinita)
                    ScriviMarcatoreDati(predefinita)
                End If
                Return predefinita
            End If

            If ECartellaDatiWinGabriel(scelta) Then Return scelta

            Dim dedicata As String = Path.Combine(scelta, NomeCartellaContenitoreDati)

            ' La sottocartella dedicata esiste ed e' gia' marcata: la raccolta e' stata
            ' fatta e non va rifatta. Senza questa uscita si rileggeva il contenuto della
            ' cartella dell'utente a ogni risoluzione del percorso, con tutto quello che
            ' comporta quando quella cartella e' OneDrive.
            If Directory.Exists(dedicata) AndAlso ECartellaDatiWinGabriel(dedicata) Then Return dedicata

            If Not Directory.Exists(dedicata) Then Directory.CreateDirectory(dedicata)

            Try
                RaccogliDatiSparsiNellaCartellaDedicata(scelta, dedicata)
            Catch exRaccolta As Exception
                _logger.ScriviLog("WGDatabase.RisolviCartellaBaseDatiEffettiva raccolta: " & exRaccolta.ToString())
            End Try

            ScriviMarcatoreDati(dedicata)
            Return dedicata
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.RisolviCartellaBaseDatiEffettiva: " & ex.ToString())
            Return CartellaBaseDatiPredefinita()
        End Try
    End Function

    Public Shared Function CartellaBaseDatiPredefinita() As String
        Try
            Return Path.Combine(Application.StartupPath, "dati")
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CartellaBaseDatiPredefinita: " & ex.ToString())
            Try
                Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati")
            Catch exRipiego As Exception
                _logger.ScriviLog("WGDatabase.CartellaBaseDatiPredefinita ripiego: " & exRipiego.ToString())
                Return ""
            End Try
        End Try
    End Function

    Public Shared Function SpostaCartellaDatiVersoNuovaPosizione(vecchiaCartella As String, nuovaCartella As String, ByRef fileNonSpostati As List(Of String)) As Boolean
        Return SpostaCartellaDatiVersoNuovaPosizione(vecchiaCartella, nuovaCartella, fileNonSpostati, Nothing)
    End Function

    Public Shared Function SpostaCartellaDatiVersoNuovaPosizione(vecchiaCartella As String, nuovaCartella As String, ByRef fileNonSpostati As List(Of String), avanzamento As AvanzamentoOperazioneDati) As Boolean
        fileNonSpostati = New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(vecchiaCartella) OrElse String.IsNullOrWhiteSpace(nuovaCartella) Then Return False
            If Not Directory.Exists(vecchiaCartella) Then Return True

            If Not ECartellaDatiWinGabriel(vecchiaCartella) Then
                _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione: l'origine (" & vecchiaCartella & ") non e' una cartella dati di WinGabriel, spostamento annullato.")
                Return False
            End If
            Dim origineCompleta As String = Path.GetFullPath(vecchiaCartella).TrimEnd(Path.DirectorySeparatorChar)
            Dim destinazioneCompleta As String = Path.GetFullPath(nuovaCartella).TrimEnd(Path.DirectorySeparatorChar)

            If String.Equals(origineCompleta, destinazioneCompleta, StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If

            If destinazioneCompleta.StartsWith(origineCompleta & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then
                _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione: la destinazione (" & destinazioneCompleta & ") e' dentro l'origine (" & origineCompleta & "), spostamento annullato.")
                Return False
            End If

            If origineCompleta.StartsWith(destinazioneCompleta & Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) Then
                _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione: l'origine (" & origineCompleta & ") e' dentro la destinazione (" & destinazioneCompleta & "), spostamento annullato.")
                Return False
            End If

            If Not Directory.Exists(nuovaCartella) Then
                Directory.CreateDirectory(nuovaCartella)
            End If

            Dim tuttoRiuscito As Boolean = True

            For Each sottocartella As String In Directory.GetDirectories(vecchiaCartella, "*", SearchOption.AllDirectories)
                Try
                    Dim percorsoRelativo As String = sottocartella.Substring(vecchiaCartella.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    If EDaEscludereDalloSpostamentoCartellaDati(percorsoRelativo) Then Continue For

                    Dim destinazioneCartella As String = Path.Combine(nuovaCartella, percorsoRelativo)
                    If Not Directory.Exists(destinazioneCartella) Then
                        Directory.CreateDirectory(destinazioneCartella)
                    End If
                Catch exCartella As Exception
                    _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione creazione cartella (" & sottocartella & "): " & exCartella.ToString())
                    tuttoRiuscito = False
                End Try
            Next

            Dim elencoFile() As String = Directory.GetFiles(vecchiaCartella, "*", SearchOption.AllDirectories)
            Dim totaleFile As Integer = elencoFile.Length
            Dim fileFatti As Integer = 0
            SegnalaAvanzamento(avanzamento, 0, totaleFile)

            For Each fileOrigine As String In elencoFile
                Try
                    fileFatti += 1
                    SegnalaAvanzamento(avanzamento, fileFatti, totaleFile)

                    Dim percorsoRelativo As String = fileOrigine.Substring(vecchiaCartella.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    If EDaEscludereDalloSpostamentoCartellaDati(percorsoRelativo) Then Continue For

                    Dim fileDestinazione As String = Path.Combine(nuovaCartella, percorsoRelativo)
                    Dim cartellaDestinazione As String = Path.GetDirectoryName(fileDestinazione)
                    If Not String.IsNullOrWhiteSpace(cartellaDestinazione) AndAlso Not Directory.Exists(cartellaDestinazione) Then
                        Directory.CreateDirectory(cartellaDestinazione)
                    End If
                    File.Copy(fileOrigine, fileDestinazione, True)
                    File.Delete(fileOrigine)
                Catch exFile As Exception
                    _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione file (" & fileOrigine & "): " & exFile.ToString())
                    fileNonSpostati.Add(fileOrigine)
                    tuttoRiuscito = False
                End Try
            Next

            Try
                ScriviMarcatoreDati(nuovaCartella)
            Catch exMarcatore As Exception
                _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione marcatore (" & nuovaCartella & "): " & exMarcatore.ToString())
            End Try

            Try
                RimuoviCartelleVuoteRicorsivamente(vecchiaCartella)
            Catch exPulizia As Exception
                _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione pulizia (" & vecchiaCartella & "): " & exPulizia.ToString())
            End Try

            Return tuttoRiuscito
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.SpostaCartellaDatiVersoNuovaPosizione (" & vecchiaCartella & " -> " & nuovaCartella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Const NomeCartellaConfigurazioneEsclusaDallaCopia As String = "config"

    Public Shared ReadOnly CartelleSempreNelPercorsoApplicazione As String() = {"config", "logs", "log", "cache", "browsercache", "backup", "backupwg"}

    Private Shared Function EDaEscludereDalloSpostamentoCartellaDati(percorsoRelativo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoRelativo) Then Return False

            Dim primoSegmento As String = percorsoRelativo.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)(0)
            For Each esclusa As String In CartelleSempreNelPercorsoApplicazione
                If String.Equals(primoSegmento, esclusa, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EDaEscludereDalloSpostamentoCartellaDati (" & percorsoRelativo & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub RimuoviCartelleVuoteRicorsivamente(cartella As String)
        RimuoviCartelleVuoteRicorsivamente(cartella, True)
    End Sub

    Private Shared Sub RimuoviCartelleVuoteRicorsivamente(cartella As String, eLaRadice As Boolean)
        Try
            If Not Directory.Exists(cartella) Then Return
            For Each sottocartella As String In Directory.GetDirectories(cartella)
                RimuoviCartelleVuoteRicorsivamente(sottocartella, False)
            Next
            If eLaRadice Then Return
            If Directory.GetFiles(cartella).Length = 0 AndAlso Directory.GetDirectories(cartella).Length = 0 Then
                Directory.Delete(cartella, False)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.RimuoviCartelleVuoteRicorsivamente (" & cartella & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Function CartellaDati(nomeSottocartella As String) As String
        Try
            Dim baseConfigurata As String = ""
            Try
                baseConfigurata = OttieniCartellaBaseDatiConfigurata()
            Catch
                baseConfigurata = ""
            End Try

            If Not String.IsNullOrWhiteSpace(baseConfigurata) AndAlso Not Directory.Exists(baseConfigurata) Then
                Try
                    RipristinaConfigurazioneCartellaBaseDatiSuPredefinita()
                Catch exRipristino As Exception
                    _logger.ScriviLog("WGDatabase.CartellaDati - ripristino configurazione fallito: " & exRipristino.ToString())
                End Try
            End If

            Dim baseEffettiva As String = CartellaBaseDatiEffettiva()
            If String.IsNullOrWhiteSpace(baseEffettiva) Then baseEffettiva = CartellaBaseDatiPredefinita()

            Dim percorso As String
            If String.IsNullOrWhiteSpace(nomeSottocartella) Then
                percorso = baseEffettiva
            Else
                percorso = Path.Combine(baseEffettiva, nomeSottocartella)
            End If

            If Not Directory.Exists(percorso) Then
                Directory.CreateDirectory(percorso)
            End If
            Return percorso
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CartellaDati (" & nomeSottocartella & "): " & ex.ToString())
            Try
                Dim ripiego As String = Path.Combine(Application.StartupPath, "dati")
                If Not String.IsNullOrWhiteSpace(nomeSottocartella) Then ripiego = Path.Combine(ripiego, nomeSottocartella)
                If Not Directory.Exists(ripiego) Then Directory.CreateDirectory(ripiego)
                Return ripiego
            Catch exRipiego As Exception
                _logger.ScriviLog("WGDatabase.CartellaDati ripiego (" & nomeSottocartella & "): " & exRipiego.ToString())
                Return ""
            End Try
        End Try
    End Function

    Private Shared Sub RipristinaConfigurazioneCartellaBaseDatiSuPredefinita()
        Try
            Dim percorsoFileConfig As String = PercorsoFileConfigWGConfig()
            If String.IsNullOrWhiteSpace(percorsoFileConfig) OrElse Not File.Exists(percorsoFileConfig) Then Return

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.Load(percorsoFileConfig)

            If documento.DocumentElement Is Nothing Then Return

            Dim nodo As XmlNode = documento.SelectSingleNode("//Configurazione[@id='" & IdConfigurazioneCartellaBaseDati & "']")
            If nodo Is Nothing Then Return

            If nodo.HasChildNodes AndAlso nodo.FirstChild IsNot Nothing AndAlso nodo.FirstChild.Name = "Valore" Then
                Dim primoValore As XmlNode = nodo.SelectSingleNode("Valore[1]")
                If primoValore IsNot Nothing Then primoValore.InnerText = ""
            Else
                nodo.InnerText = ""
            End If

            documento.Save(percorsoFileConfig)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.RipristinaConfigurazioneCartellaBaseDatiSuPredefinita: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function PercorsoFileDati(nomeSottocartella As String, nomeFile As String) As String
        Try
            Return Path.Combine(CartellaDati(nomeSottocartella), nomeFile)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.PercorsoFileDati (" & nomeSottocartella & "," & nomeFile & "): " & ex.ToString())
            Try
                Dim ripiego As String = Path.Combine(Application.StartupPath, "dati")
                If Not String.IsNullOrWhiteSpace(nomeSottocartella) Then ripiego = Path.Combine(ripiego, nomeSottocartella)
                If Not Directory.Exists(ripiego) Then Directory.CreateDirectory(ripiego)
                Return Path.Combine(ripiego, nomeFile)
            Catch exRipiego As Exception
                _logger.ScriviLog("WGDatabase.PercorsoFileDati ripiego (" & nomeSottocartella & "," & nomeFile & "): " & exRipiego.ToString())
                Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, nomeFile)
            End Try
        End Try
    End Function

    Public Shared Function ApriConnessione(percorsoFile As String, Optional parametriAggiuntivi As String = "Version=3;Journal Mode=WAL;Synchronous=Normal;Pooling=True;Default Timeout=30;BusyTimeout=8000;") As SQLiteConnection
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) Then Return Nothing

            Dim cartella As String = Path.GetDirectoryName(percorsoFile)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            If Not File.Exists(percorsoFile) Then
                SQLiteConnection.CreateFile(percorsoFile)
            End If

            Dim stringaConnessione As String = "Data Source=" & QuotaOrigineDati(percorsoFile) & ";" & parametriAggiuntivi
            Dim connessione As New SQLiteConnection(stringaConnessione)
            connessione.Open()
            ' Chi passa gia' BusyTimeout nella stringa tiene il suo valore. Gli altri (posta,
            ' rubrica, note, dati riservati) senza questo pragma falliscono subito con
            ' "database is locked" appena due moduli scrivono insieme.
            If parametriAggiuntivi Is Nothing OrElse parametriAggiuntivi.IndexOf("busy", StringComparison.OrdinalIgnoreCase) < 0 Then
                EseguiPragma(connessione, "PRAGMA busy_timeout=8000;")
            End If
            Return connessione
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ApriConnessione (" & percorsoFile & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function QuotaOrigineDati(percorsoFile As String) As String
        Try
            Dim pulito As String = If(percorsoFile, "").Replace("""", """""")
            Return """" & pulito & """"
        Catch
            Return percorsoFile
        End Try
    End Function

    Private Shared Function ErroreBloccoDatabase(ex As SQLiteException) As Boolean
        Try
            If ex Is Nothing Then Return False
            Return ex.ResultCode = SQLiteErrorCode.Busy OrElse
                   ex.ResultCode = SQLiteErrorCode.Locked
        Catch
            Return False
        End Try
    End Function

    Private Shared Sub AttendiBloccoDatabase(tentativo As Integer)
        Try
            Dim pausa As Integer = 80 * Math.Max(1, tentativo)
            If pausa > 400 Then pausa = 400
            System.Threading.Thread.Sleep(pausa)
        Catch
        End Try
    End Sub

    Public Shared Sub EseguiPragma(connessione As SQLiteConnection, sqlPragma As String)
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(sqlPragma) Then Return
            Using comando As New SQLiteCommand(sqlPragma, connessione)
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EseguiPragma (" & sqlPragma & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Function ChiaveValida(chiave As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiave) Then Return False
            Return chiave.Length <= LunghezzaMassimaChiave
        Catch
            Return False
        End Try
    End Function

    Private Shared Function CodificaBase36(valore As ULong, lunghezza As Integer) As String
        Try
            Const alfabeto As String = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            If lunghezza <= 0 Then lunghezza = 1
            Dim cifre(lunghezza - 1) As Char
            Dim residuo As ULong = valore
            For indice As Integer = lunghezza - 1 To 0 Step -1
                cifre(indice) = alfabeto(CInt(residuo Mod 36UL))
                residuo \= 36UL
            Next
            Return New String(cifre)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CodificaBase36: " & ex.ToString())
            Return New String("0"c, Math.Max(lunghezza, 1))
        End Try
    End Function

    Public Shared Function GeneraChiave() As String
        Try
            Dim millisecondiTrascorsi As ULong = CULng(Math.Max(0, (DateTime.UtcNow - _epocaChiavi).TotalMilliseconds))
            Dim parteTempo As String = CodificaBase36(millisecondiTrascorsi, 10)

            Dim bytesCasuali(7) As Byte
            SyncLock _lockGeneratoreCasuale
                _generatoreCasuale.GetBytes(bytesCasuali)
            End SyncLock
            Dim valoreCasuale As ULong = BitConverter.ToUInt64(bytesCasuali, 0)
            Dim parteCasuale As String = CodificaBase36(valoreCasuale, 13)

            Dim chiave As String = parteTempo & parteCasuale
            If chiave.Length > LunghezzaMassimaChiave Then chiave = chiave.Substring(0, LunghezzaMassimaChiave)
            Return chiave
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.GeneraChiave: " & ex.ToString())
            Try
                Dim riserva As String = "K" & Guid.NewGuid().ToString("N").ToUpperInvariant()
                If riserva.Length > LunghezzaMassimaChiave Then riserva = riserva.Substring(0, LunghezzaMassimaChiave)
                Return riserva
            Catch
                Return "K" & DateTime.UtcNow.Ticks.ToString().PadLeft(20, "0"c).Substring(0, 20)
            End Try
        End Try
    End Function

    Public Shared Function GeneraChiaveUnivocaPerTabella(connessione As SQLiteConnection, nomeTabella As String, Optional nomeColonna As String = NomeColonnaChiavePredefinita) As String
        Try
            If String.IsNullOrWhiteSpace(nomeColonna) Then nomeColonna = NomeColonnaChiavePredefinita
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return GeneraChiave()

            For tentativo As Integer = 1 To 20
                Dim candidata As String = GeneraChiave()
                Using comando As New SQLiteCommand("SELECT 1 FROM " & nomeTabella & " WHERE " & nomeColonna & " = @chiave LIMIT 1;", connessione)
                    comando.Parameters.AddWithValue("@chiave", candidata)
                    Dim risultato As Object = comando.ExecuteScalar()
                    If risultato Is Nothing Then Return candidata
                End Using
            Next
            Return GeneraChiave()
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.GeneraChiaveUnivocaPerTabella (" & nomeTabella & "): " & ex.ToString())
            Return GeneraChiave()
        End Try
    End Function

    Public Shared Function AssicuraColonnaChiave(connessione As SQLiteConnection, nomeTabella As String, Optional nomeColonna As String = NomeColonnaChiavePredefinita) As Boolean
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return False
            If String.IsNullOrWhiteSpace(nomeColonna) Then nomeColonna = NomeColonnaChiavePredefinita

            Dim colonnaPresente As Boolean = False
            Using comandoInfo As New SQLiteCommand("PRAGMA table_info(" & nomeTabella & ");", connessione)
                Using lettore As SQLiteDataReader = comandoInfo.ExecuteReader()
                    While lettore.Read()
                        Dim nomeColonnaEsistente As String = Convert.ToString(lettore("name"))
                        If String.Equals(nomeColonnaEsistente, nomeColonna, StringComparison.OrdinalIgnoreCase) Then
                            colonnaPresente = True
                            Exit While
                        End If
                    End While
                End Using
            End Using

            If Not colonnaPresente Then
                Using comandoAlter As New SQLiteCommand("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " TEXT;", connessione)
                    comandoAlter.ExecuteNonQuery()
                End Using
            End If

            Dim righeDaAggiornare As New List(Of Long)
            Using comandoSelezione As New SQLiteCommand("SELECT rowid FROM " & nomeTabella & " WHERE " & nomeColonna & " IS NULL OR " & nomeColonna & " = '';", connessione)
                Using lettore As SQLiteDataReader = comandoSelezione.ExecuteReader()
                    While lettore.Read()
                        Try
                            righeDaAggiornare.Add(Convert.ToInt64(lettore(0)))
                        Catch exRigaLettura As Exception
                            _logger.ScriviLog("WGDatabase.AssicuraColonnaChiave lettura rowid (" & nomeTabella & "): " & exRigaLettura.Message)
                        End Try
                    End While
                End Using
            End Using

            For Each identificatoreRiga As Long In righeDaAggiornare
                Try
                    Dim nuovaChiave As String = GeneraChiaveUnivocaPerTabella(connessione, nomeTabella, nomeColonna)
                    Using comandoAggiorna As New SQLiteCommand("UPDATE " & nomeTabella & " SET " & nomeColonna & " = @chiave WHERE rowid = @rowid;", connessione)
                        comandoAggiorna.Parameters.AddWithValue("@chiave", nuovaChiave)
                        comandoAggiorna.Parameters.AddWithValue("@rowid", identificatoreRiga)
                        comandoAggiorna.ExecuteNonQuery()
                    End Using
                Catch exRiga As Exception
                    _logger.ScriviLog("WGDatabase.AssicuraColonnaChiave backfill riga " & identificatoreRiga.ToString() & " (" & nomeTabella & "): " & exRiga.ToString())
                End Try
            Next

            Try
                Dim nomeIndice As String = "ix_" & nomeTabella & "_" & nomeColonna
                Using comandoIndice As New SQLiteCommand("CREATE UNIQUE INDEX IF NOT EXISTS " & nomeIndice & " ON " & nomeTabella & "(" & nomeColonna & ");", connessione)
                    comandoIndice.ExecuteNonQuery()
                End Using
            Catch exIndice As Exception
                _logger.ScriviLog("WGDatabase.AssicuraColonnaChiave indice (" & nomeTabella & "): " & exIndice.ToString())
            End Try

            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AssicuraColonnaChiave (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AssicuraColonnaChiaveIstanza(nomeTabella As String, Optional nomeColonna As String = NomeColonnaChiavePredefinita)
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return
            SyncLock _lockIstanza
                AssicuraColonnaChiave(_connessioneIstanza, nomeTabella, nomeColonna)
                Try
                    AssicuraColonneDateIstanza(nomeTabella)
                Catch exDate As Exception
                    _logger.ScriviLog("WGDatabase.AssicuraColonnaChiaveIstanza colonne data (" & nomeTabella & "): " & exDate.ToString())
                End Try
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AssicuraColonnaChiaveIstanza (" & nomeTabella & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Function DataOraCorrente() As String
        Try
            Return DateTime.Now.ToString(FormatoDataOra, CultureInfo.InvariantCulture)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.DataOraCorrente: " & ex.ToString())
            Try
                Return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Catch
                Return ""
            End Try
        End Try
    End Function

    ' Legge una delle due date dal record restituito da OttieniRecord o da OttieniTuttiIRecord.
    ' Restituisce DateTime.MinValue se la colonna non c'e' o non e' interpretabile.
    Public Shared Function LeggiDataRecord(riga As DataRow, nomeColonna As String) As DateTime
        Try
            If riga Is Nothing OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return DateTime.MinValue
            If riga.Table Is Nothing OrElse Not riga.Table.Columns.Contains(nomeColonna) Then Return DateTime.MinValue
            If riga.IsNull(nomeColonna) Then Return DateTime.MinValue

            Dim testo As String = Convert.ToString(riga(nomeColonna))
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue

            Dim valore As DateTime = DateTime.MinValue
            If DateTime.TryParseExact(testo.Trim(), FormatoDataOra, CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore
            If DateTime.TryParse(testo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, valore) Then Return valore

            Return DateTime.MinValue
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.LeggiDataRecord (" & nomeColonna & "): " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Public Shared Function DataCreazioneRecord(riga As DataRow) As DateTime
        Try
            Return LeggiDataRecord(riga, NomeColonnaDataCreazione)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.DataCreazioneRecord: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Public Shared Function DataModificaRecord(riga As DataRow) As DateTime
        Try
            Return LeggiDataRecord(riga, NomeColonnaDataModifica)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.DataModificaRecord: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Public Shared Function ColonnaEsistente(connessione As SQLiteConnection, nomeTabella As String, nomeColonna As String) As Boolean
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            Using comandoInfo As New SQLiteCommand("PRAGMA table_info(" & nomeTabella & ");", connessione)
                Using lettore As SQLiteDataReader = comandoInfo.ExecuteReader()
                    While lettore.Read()
                        Dim nomeColonnaEsistente As String = Convert.ToString(lettore("name"))
                        If String.Equals(nomeColonnaEsistente, nomeColonna, StringComparison.OrdinalIgnoreCase) Then Return True
                    End While
                End Using
            End Using
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ColonnaEsistente (" & nomeTabella & "." & nomeColonna & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function TabellaEsistente(connessione As SQLiteConnection, nomeTabella As String) As Boolean
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return False
            Using comando As New SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type IN ('table','view') AND name = @nome LIMIT 1;", connessione)
                comando.Parameters.AddWithValue("@nome", nomeTabella)
                Return comando.ExecuteScalar() IsNot Nothing
            End Using
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.TabellaEsistente (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Aggiunge alla tabella le colonne della data di creazione e della data di modifica se non ci
    ' sono ancora, e assegna ai record gia' presenti la data del momento in cui vengono portati
    ' alla nuova struttura: la data reale di quei record non e' recuperabile, quindi si usa quella
    ' della migrazione come punto di partenza. Da quel momento in poi le date sono quelle vere.
    Public Shared Function AssicuraColonneDate(connessione As SQLiteConnection, nomeTabella As String) As Boolean
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return False
            If Not TabellaEsistente(connessione, nomeTabella) Then Return False

            Dim esito As Boolean = True
            Dim dataMigrazione As String = DataOraCorrente()

            For Each nomeColonna As String In New String() {NomeColonnaDataCreazione, NomeColonnaDataModifica}
                Try
                    If Not ColonnaEsistente(connessione, nomeTabella, nomeColonna) Then
                        Using comandoAlter As New SQLiteCommand("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " TEXT;", connessione)
                            comandoAlter.ExecuteNonQuery()
                        End Using
                    End If
                Catch exColonna As Exception
                    _logger.ScriviLog("WGDatabase.AssicuraColonneDate colonna " & nomeColonna & " (" & nomeTabella & "): " & exColonna.ToString())
                    esito = False
                End Try
            Next

            If esito Then
                Try
                    RetrodataRecordEsistenti(connessione, nomeTabella, dataMigrazione)
                Catch exRetrodata As Exception
                    _logger.ScriviLog("WGDatabase.AssicuraColonneDate retrodatazione (" & nomeTabella & "): " & exRetrodata.ToString())
                End Try
            End If

            Return esito
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AssicuraColonneDate (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub RetrodataRecordEsistenti(connessione As SQLiteConnection, nomeTabella As String, dataMigrazione As String)
        Try
            If connessione Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return
            If String.IsNullOrWhiteSpace(dataMigrazione) Then dataMigrazione = DataOraCorrente()
            If String.IsNullOrWhiteSpace(dataMigrazione) Then Return

            Try
                Using comandoCreazione As New SQLiteCommand("UPDATE " & nomeTabella & " SET " & NomeColonnaDataCreazione & " = @data WHERE " & NomeColonnaDataCreazione & " IS NULL OR TRIM(" & NomeColonnaDataCreazione & ") = '';", connessione)
                    comandoCreazione.Parameters.AddWithValue("@data", dataMigrazione)
                    comandoCreazione.ExecuteNonQuery()
                End Using
            Catch exCreazione As Exception
                _logger.ScriviLog("WGDatabase.RetrodataRecordEsistenti data creazione (" & nomeTabella & "): " & exCreazione.ToString())
            End Try

            Try
                Using comandoModifica As New SQLiteCommand("UPDATE " & nomeTabella & " SET " & NomeColonnaDataModifica & " = COALESCE(NULLIF(TRIM(" & NomeColonnaDataCreazione & "), ''), @data) WHERE " & NomeColonnaDataModifica & " IS NULL OR TRIM(" & NomeColonnaDataModifica & ") = '';", connessione)
                    comandoModifica.Parameters.AddWithValue("@data", dataMigrazione)
                    comandoModifica.ExecuteNonQuery()
                End Using
            Catch exModifica As Exception
                _logger.ScriviLog("WGDatabase.RetrodataRecordEsistenti data modifica (" & nomeTabella & "): " & exModifica.ToString())
            End Try
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.RetrodataRecordEsistenti (" & nomeTabella & "): " & ex.ToString())
        End Try
    End Sub

    Public Function AssicuraColonneDateIstanza(nomeTabella As String) As Boolean
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return False
            If _chiuso Then Return False

            SyncLock _lockIstanza
                If _tabelleColonneDateVerificate.Contains(nomeTabella) Then Return True

                Dim esito As Boolean = AssicuraColonneDate(_connessioneIstanza, nomeTabella)
                If esito Then
                    Try
                        _tabelleColonneDateVerificate.Add(nomeTabella)
                    Catch exCache As Exception
                        _logger.ScriviLog("WGDatabase.AssicuraColonneDateIstanza cache (" & nomeTabella & "): " & exCache.ToString())
                    End Try
                End If

                Return esito
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AssicuraColonneDateIstanza (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' Aggiunge una colonna personalizzata alla tabella, se non c'e' ancora. Va sempre preferita a
    ' un "ALTER TABLE ... ADD COLUMN" scritto a mano nel codice chiamante: controllando prima
    ' l'esistenza della colonna rende l'operazione idempotente, quindi sicura da richiamare a ogni
    ' avvio del programma (es. per migrazioni), senza rischiare l'errore SQLite "duplicate column
    ' name" quando la colonna e' gia' stata aggiunta in una sessione precedente.
    Public Function AssicuraColonna(nomeTabella As String, nomeColonna As String, Optional tipoColonna As String = "TEXT") As Boolean
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            If String.IsNullOrWhiteSpace(tipoColonna) Then tipoColonna = "TEXT"
            SyncLock _lockIstanza
                Try
                    If ColonnaEsistente(_connessioneIstanza, nomeTabella, nomeColonna) Then Return True
                Catch exVerifica As Exception
                    _logger.ScriviLog("WGDatabase.AssicuraColonna verifica (" & nomeTabella & "." & nomeColonna & "): " & exVerifica.ToString())
                    Return False
                End Try

                Try
                    Using comando As New SQLiteCommand("ALTER TABLE " & nomeTabella & " ADD COLUMN " & nomeColonna & " " & tipoColonna & ";", _connessioneIstanza)
                        comando.ExecuteNonQuery()
                    End Using
                Catch exAlter As SQLiteException
                    ' Possibile corsa fra due controlli concorrenti sulla stessa colonna (o una
                    ' colonna aggiunta nel frattempo con lo stesso nome): non e' un vero errore,
                    ' il risultato finale (colonna presente) e' comunque quello desiderato.
                    If EMessaggioColonnaDuplicata(exAlter) Then
                        _logger.ScriviLog("WGDatabase.AssicuraColonna colonna gia' presente, ignorato (" & nomeTabella & "." & nomeColonna & "): " & exAlter.Message)
                        Return True
                    End If
                    _logger.ScriviLog("WGDatabase.AssicuraColonna ALTER TABLE (" & nomeTabella & "." & nomeColonna & "): " & exAlter.ToString())
                    Return False
                End Try
                Return True
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AssicuraColonna (" & nomeTabella & "." & nomeColonna & "): " & ex.ToString())
            Return False
        End Try
    End Function

    ' True se l'eccezione SQLite segnala che la colonna esiste gia' (tipico di un ALTER TABLE ...
    ' ADD COLUMN ripetuto). Centralizzato qui perche' il messaggio di SQLite non e' localizzato e
    ' viene confrontato in piu' punti della classe.
    Private Shared Function EMessaggioColonnaDuplicata(ex As Exception) As Boolean
        Try
            If ex Is Nothing OrElse String.IsNullOrWhiteSpace(ex.Message) Then Return False
            Return ex.Message.IndexOf("duplicate column name", StringComparison.OrdinalIgnoreCase) >= 0
        Catch
            Return False
        End Try
    End Function

    ' Riconosce un "ALTER TABLE <tabella> ADD COLUMN <colonna> ..." per poterlo rendere
    ' automaticamente idempotente in EseguiNonQuery, anche quando il chiamante non passa
    ' attraverso AssicuraColonna. Serve a spegnere alla radice l'intera classe di errori
    ' "duplicate column name" che si presentano quando uno script di migrazione scritto a mano
    ' viene rieseguito a ogni avvio del programma.
    Private Shared ReadOnly _espressioneAlterAddColumn As New Regex(
        "^\s*ALTER\s+TABLE\s+(?<tabella>[A-Za-z_][A-Za-z0-9_]*)\s+ADD\s+COLUMN\s+(?<colonna>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    Private Function EAggiuntaColonnaGiaPresente(sql As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(sql) Then Return False
            Dim corrispondenza As Match = _espressioneAlterAddColumn.Match(sql)
            If Not corrispondenza.Success Then Return False
            Dim nomeTabella As String = corrispondenza.Groups("tabella").Value
            Dim nomeColonna As String = corrispondenza.Groups("colonna").Value
            Return ColonnaEsistente(_connessioneIstanza, nomeTabella, nomeColonna)
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EAggiuntaColonnaGiaPresente (" & If(sql, "") & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function ContieneChiave(valori As Dictionary(Of String, Object), nomeColonna As String) As Boolean
        Try
            If valori Is Nothing OrElse String.IsNullOrWhiteSpace(nomeColonna) Then Return False
            For Each coppia As KeyValuePair(Of String, Object) In valori
                If String.Equals(coppia.Key, nomeColonna, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.ContieneChiave (" & nomeColonna & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function CreaTabellaSeNonEsiste(nomeTabella As String, Optional definizioneColonne As String = "", Optional nomeColonnaChiave As String = NomeColonnaChiavePredefinita) As Boolean
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return False
            SyncLock _lockIstanza
                Dim sql As New StringBuilder()
                sql.Append("CREATE TABLE IF NOT EXISTS ").Append(nomeTabella).Append(" (").Append(nomeColonnaChiave).Append(" TEXT PRIMARY KEY")
                If Not String.IsNullOrWhiteSpace(definizioneColonne) Then
                    sql.Append(", ").Append(definizioneColonne)
                End If
                sql.Append(", ").Append(NomeColonnaDataCreazione).Append(" TEXT")
                sql.Append(", ").Append(NomeColonnaDataModifica).Append(" TEXT")
                sql.Append(");")
                Using comando As New SQLiteCommand(sql.ToString(), _connessioneIstanza)
                    comando.ExecuteNonQuery()
                End Using

                Try
                    AssicuraColonneDateIstanza(nomeTabella)
                Catch exDate As Exception
                    _logger.ScriviLog("WGDatabase.CreaTabellaSeNonEsiste colonne data (" & nomeTabella & "): " & exDate.ToString())
                End Try
            End SyncLock
            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.CreaTabellaSeNonEsiste (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AggiungiParametri(comando As SQLiteCommand, parametri As Dictionary(Of String, Object))
        Try
            If parametri Is Nothing Then Return
            For Each coppia As KeyValuePair(Of String, Object) In parametri
                comando.Parameters.AddWithValue(coppia.Key, If(coppia.Value, DBNull.Value))
            Next
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AggiungiParametri: " & ex.ToString())
        End Try
    End Sub

    Public Function EseguiNonQuery(sql As String, Optional parametri As Dictionary(Of String, Object) = Nothing) As Integer
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(sql) Then Return 0
            SyncLock _lockIstanza
                ' Rende innocuo un "ALTER TABLE ... ADD COLUMN" gia' eseguito in precedenza (tipico
                ' di migrazioni ripetute a ogni avvio): senza questo controllo SQLite risponde con
                ' "duplicate column name" e l'istruzione fallisce inutilmente, anche se il risultato
                ' desiderato (la colonna presente) e' gia' raggiunto.
                Try
                    If EAggiuntaColonnaGiaPresente(sql) Then
                        _logger.ScriviLog("WGDatabase.EseguiNonQuery: colonna gia' presente, istruzione ignorata (" & sql & ")")
                        Return 0
                    End If
                Catch exControllo As Exception
                    _logger.ScriviLog("WGDatabase.EseguiNonQuery controllo colonna (" & sql & "): " & exControllo.ToString())
                End Try

                For tentativo As Integer = 1 To 5
                    Try
                        Using comando As New SQLiteCommand(sql, _connessioneIstanza)
                            AggiungiParametri(comando, parametri)
                            Return comando.ExecuteNonQuery()
                        End Using
                    Catch exSql As SQLiteException
                        If EMessaggioColonnaDuplicata(exSql) Then
                            _logger.ScriviLog("WGDatabase.EseguiNonQuery: colonna gia' presente, ignorato (" & sql & "): " & exSql.Message)
                            Return 0
                        End If
                        If ErroreBloccoDatabase(exSql) AndAlso tentativo < 5 Then
                            _logger.ScriviLog("WGDatabase.EseguiNonQuery: database occupato, nuovo tentativo " & tentativo.ToString())
                            AttendiBloccoDatabase(tentativo)
                            Continue For
                        End If
                        _logger.ScriviLog("WGDatabase.EseguiNonQuery [" & exSql.ResultCode.ToString() & "] (" & sql & "): " & exSql.ToString())
                        Return 0
                    End Try
                Next
                Return 0
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EseguiNonQuery (" & If(sql, "") & "): " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function EseguiScalare(sql As String, Optional parametri As Dictionary(Of String, Object) = Nothing) As Object
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(sql) Then Return Nothing
            SyncLock _lockIstanza
                For tentativo As Integer = 1 To 5
                    Try
                        Using comando As New SQLiteCommand(sql, _connessioneIstanza)
                            AggiungiParametri(comando, parametri)
                            Return comando.ExecuteScalar()
                        End Using
                    Catch exSql As SQLiteException
                        If ErroreBloccoDatabase(exSql) AndAlso tentativo < 5 Then
                            AttendiBloccoDatabase(tentativo)
                            Continue For
                        End If
                        _logger.ScriviLog("WGDatabase.EseguiScalare [" & exSql.ResultCode.ToString() & "] (" & sql & "): " & exSql.ToString())
                        Return Nothing
                    End Try
                Next
                Return Nothing
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EseguiScalare (" & If(sql, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function EseguiQuery(sql As String, Optional parametri As Dictionary(Of String, Object) = Nothing) As DataTable
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(sql) Then Return New DataTable()
            SyncLock _lockIstanza
                For tentativo As Integer = 1 To 5
                    Try
                        Using comando As New SQLiteCommand(sql, _connessioneIstanza)
                            AggiungiParametri(comando, parametri)
                            Using adattatore As New SQLiteDataAdapter(comando)
                                Dim tabella As New DataTable()
                                adattatore.Fill(tabella)
                                Return tabella
                            End Using
                        End Using
                    Catch exSql As SQLiteException
                        If ErroreBloccoDatabase(exSql) AndAlso tentativo < 5 Then
                            AttendiBloccoDatabase(tentativo)
                            Continue For
                        End If
                        _logger.ScriviLog("WGDatabase.EseguiQuery [" & exSql.ResultCode.ToString() & "] (" & sql & "): " & exSql.ToString())
                        Return New DataTable()
                    End Try
                Next
                Return New DataTable()
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EseguiQuery (" & If(sql, "") & "): " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    Public Function InserisciRecord(nomeTabella As String, valori As Dictionary(Of String, Object), Optional nomeColonnaChiave As String = NomeColonnaChiavePredefinita) As String
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return ""
            If valori Is Nothing Then valori = New Dictionary(Of String, Object)()

            Dim valoriEffettivi As New Dictionary(Of String, Object)()
            Try
                For Each coppia As KeyValuePair(Of String, Object) In valori
                    valoriEffettivi(coppia.Key) = coppia.Value
                Next
            Catch exCopia As Exception
                _logger.ScriviLog("WGDatabase.InserisciRecord copia valori (" & nomeTabella & "): " & exCopia.ToString())
            End Try

            SyncLock _lockIstanza
                Dim colonneDatePronte As Boolean = False

                Try
                    colonneDatePronte = AssicuraColonneDateIstanza(nomeTabella)
                Catch exDate As Exception
                    _logger.ScriviLog("WGDatabase.InserisciRecord colonne data (" & nomeTabella & "): " & exDate.ToString())
                    colonneDatePronte = False
                End Try

                If colonneDatePronte Then
                    Try
                        Dim adesso As String = DataOraCorrente()
                        If Not ContieneChiave(valoriEffettivi, NomeColonnaDataCreazione) Then
                            valoriEffettivi(NomeColonnaDataCreazione) = adesso
                        End If
                        If Not ContieneChiave(valoriEffettivi, NomeColonnaDataModifica) Then
                            valoriEffettivi(NomeColonnaDataModifica) = adesso
                        End If
                    Catch exImposta As Exception
                        _logger.ScriviLog("WGDatabase.InserisciRecord impostazione date (" & nomeTabella & "): " & exImposta.ToString())
                    End Try
                End If

                For tentativo As Integer = 1 To 10
                    Dim chiave As String = GeneraChiaveUnivocaPerTabella(_connessioneIstanza, nomeTabella, nomeColonnaChiave)
                    Try
                        Dim nomiColonne As New List(Of String) From {nomeColonnaChiave}
                        Dim segnaposti As New List(Of String) From {"@" & nomeColonnaChiave}

                        Using comando As New SQLiteCommand(_connessioneIstanza)
                            comando.Parameters.AddWithValue("@" & nomeColonnaChiave, chiave)
                            For Each coppia As KeyValuePair(Of String, Object) In valoriEffettivi
                                If String.Equals(coppia.Key, nomeColonnaChiave, StringComparison.OrdinalIgnoreCase) Then Continue For
                                nomiColonne.Add(coppia.Key)
                                segnaposti.Add("@" & coppia.Key)
                                comando.Parameters.AddWithValue("@" & coppia.Key, If(coppia.Value, DBNull.Value))
                            Next
                            comando.CommandText = "INSERT INTO " & nomeTabella & " (" & String.Join(",", nomiColonne) & ") VALUES (" & String.Join(",", segnaposti) & ");"
                            comando.ExecuteNonQuery()
                        End Using

                        Return chiave
                    Catch exCollisione As SQLiteException
                        _logger.ScriviLog("WGDatabase.InserisciRecord collisione tentativo " & tentativo.ToString() & " (" & nomeTabella & "): " & exCollisione.Message)
                        If ErroreBloccoDatabase(exCollisione) Then AttendiBloccoDatabase(tentativo)
                    End Try
                Next
            End SyncLock

            Return ""
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.InserisciRecord (" & nomeTabella & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function AggiornaRecord(nomeTabella As String, chiave As String, valori As Dictionary(Of String, Object), Optional nomeColonnaChiave As String = NomeColonnaChiavePredefinita) As Boolean
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(chiave) Then Return False
            If valori Is Nothing OrElse valori.Count = 0 Then Return False

            Dim valoriEffettivi As New Dictionary(Of String, Object)()
            Try
                For Each coppia As KeyValuePair(Of String, Object) In valori
                    valoriEffettivi(coppia.Key) = coppia.Value
                Next
            Catch exCopia As Exception
                _logger.ScriviLog("WGDatabase.AggiornaRecord copia valori (" & nomeTabella & "): " & exCopia.ToString())
            End Try

            SyncLock _lockIstanza
                Dim colonneDatePronte As Boolean = False

                Try
                    colonneDatePronte = AssicuraColonneDateIstanza(nomeTabella)
                Catch exDate As Exception
                    _logger.ScriviLog("WGDatabase.AggiornaRecord colonne data (" & nomeTabella & "): " & exDate.ToString())
                    colonneDatePronte = False
                End Try

                If colonneDatePronte Then
                    Try
                        If Not ContieneChiave(valoriEffettivi, NomeColonnaDataModifica) Then
                            valoriEffettivi(NomeColonnaDataModifica) = DataOraCorrente()
                        End If
                    Catch exImposta As Exception
                        _logger.ScriviLog("WGDatabase.AggiornaRecord impostazione data modifica (" & nomeTabella & "): " & exImposta.ToString())
                    End Try
                End If

                Dim assegnazioni As New List(Of String)
                Using comando As New SQLiteCommand()
                    comando.Connection = _connessioneIstanza
                    For Each coppia As KeyValuePair(Of String, Object) In valoriEffettivi
                        If String.Equals(coppia.Key, nomeColonnaChiave, StringComparison.OrdinalIgnoreCase) Then Continue For
                        If String.Equals(coppia.Key, NomeColonnaDataCreazione, StringComparison.OrdinalIgnoreCase) Then Continue For
                        assegnazioni.Add(coppia.Key & " = @" & coppia.Key)
                        comando.Parameters.AddWithValue("@" & coppia.Key, If(coppia.Value, DBNull.Value))
                    Next
                    If assegnazioni.Count = 0 Then Return False
                    comando.Parameters.AddWithValue("@" & nomeColonnaChiave, chiave)
                    comando.CommandText = "UPDATE " & nomeTabella & " SET " & String.Join(",", assegnazioni) & " WHERE " & nomeColonnaChiave & " = @" & nomeColonnaChiave & ";"
                    Dim righe As Integer = comando.ExecuteNonQuery()
                    Return righe > 0
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.AggiornaRecord (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function EliminaRecord(nomeTabella As String, chiave As String, Optional nomeColonnaChiave As String = NomeColonnaChiavePredefinita) As Boolean
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(chiave) Then Return False
            SyncLock _lockIstanza
                Using comando As New SQLiteCommand("DELETE FROM " & nomeTabella & " WHERE " & nomeColonnaChiave & " = @chiave;", _connessioneIstanza)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    Dim righe As Integer = comando.ExecuteNonQuery()
                    Return righe > 0
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.EliminaRecord (" & nomeTabella & "): " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniRecord(nomeTabella As String, chiave As String, Optional nomeColonnaChiave As String = NomeColonnaChiavePredefinita) As DataRow
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) OrElse String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            SyncLock _lockIstanza
                Using comando As New SQLiteCommand("SELECT * FROM " & nomeTabella & " WHERE " & nomeColonnaChiave & " = @chiave LIMIT 1;", _connessioneIstanza)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    Using adattatore As New SQLiteDataAdapter(comando)
                        Dim tabella As New DataTable()
                        adattatore.Fill(tabella)
                        If tabella.Rows.Count = 0 Then Return Nothing
                        Return tabella.Rows(0)
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.OttieniRecord (" & nomeTabella & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniTuttiIRecord(nomeTabella As String, Optional clausolaOrdine As String = "") As DataTable
        Try
            If _chiuso OrElse _connessioneIstanza Is Nothing OrElse String.IsNullOrWhiteSpace(nomeTabella) Then Return New DataTable()
            SyncLock _lockIstanza
                Dim sql As String = "SELECT * FROM " & nomeTabella
                If Not String.IsNullOrWhiteSpace(clausolaOrdine) Then sql &= " ORDER BY " & clausolaOrdine
                sql &= ";"
                Using comando As New SQLiteCommand(sql, _connessioneIstanza)
                    Using adattatore As New SQLiteDataAdapter(comando)
                        Dim tabella As New DataTable()
                        adattatore.Fill(tabella)
                        Return tabella
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.OttieniTuttiIRecord (" & nomeTabella & "): " & ex.ToString())
            Return New DataTable()
        End Try
    End Function

    Public Sub Chiudi()
        Try
            SyncLock _lockIstanza
                If _chiuso Then Return
                _chiuso = True
                If _connessioneIstanza IsNot Nothing Then
                    Try
                        If _connessioneIstanza.State <> ConnectionState.Closed Then _connessioneIstanza.Close()
                    Catch exChiusura As Exception
                        _logger.ScriviLog("WGDatabase.Chiudi chiusura: " & exChiusura.Message)
                    End Try
                    Try
                        _connessioneIstanza.Dispose()
                    Catch exSmaltimento As Exception
                        _logger.ScriviLog("WGDatabase.Chiudi smaltimento: " & exSmaltimento.Message)
                    End Try
                End If
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.Chiudi: " & ex.ToString())
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _smaltito Then Return
            _smaltito = True
            Chiudi()
        Catch ex As Exception
            _logger.ScriviLog("WGDatabase.Dispose: " & ex.ToString())
        End Try
    End Sub

End Class