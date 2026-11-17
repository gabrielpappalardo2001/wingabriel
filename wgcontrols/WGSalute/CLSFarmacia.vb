Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.SQLite
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports UglyToad.PdfPig
Imports UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor
Imports wgcontrols
Imports WGOcr
Imports WGOpenStrictMap

''' <summary>
''' Anagrafica farmaci AIFA senza duplicati, prezzi, carenze, fogli illustrativi e RCP,
''' elenco farmacie con calcolo delle aperture, arricchito con OpenStreetMap e Overture Maps
''' tramite CLSLuoghi e CLSOverture.
''' </summary>
Public Class CLSFarmacie

#Region "Tipi pubblici"

    Public Delegate Sub Avanzamento(testo As String)

    Public Class Farmaco
        Public Property Aic6 As String
        Public Property Nome As String
        Public Property PrincipioAttivo As String
        Public Property Azienda As String
        Public Property CodiceSis As String
        Public Property Atc As String
        Public Property AtcDescrizione As String
        Public Property Stato As String
        Public Property StatoLeggibile As String
        Public Property RegimeFornitura As String
        Public Property RegimeFornituraLeggibile As String
        Public Property NumeroConfezioni As Integer
        Public Property Carente As Boolean
        Public Property NoteCarenza As String
    End Class

    Public Class Confezione
        Public Property Aic9 As String
        Public Property Aic6 As String
        Public Property Descrizione As String
        Public Property DescrizioneLeggibile As String
        Public Property RegimeFornitura As String
        Public Property RegimeFornituraLeggibile As String
        Public Property Stato As String
    End Class

    Public Class Farmacia
        Public Property Codice As String
        Public Property Denominazione As String
        Public Property Indirizzo As String
        Public Property Cap As String
        Public Property Comune As String
        Public Property Provincia As String
        Public Property Regione As String
        Public Property Telefono As String
        Public Property Sito As String
        Public Property Email As String
        Public Property Marca As String
        Public Property IdGers As String
        Public Property PercorsoTattile As String
        Public Property SegnalatoreAcustico As String
        Public Property Braille As String
        Public Property Corrimano As String
        Public Property DescrizionePerNonVedenti As String
        Public Property AccessoSediaRotelle As String
        Public Property BagnoAccessibile As String
        Public Property CaniGuidaAmmessi As String
        Public Property Orari As String
        Public Property Latitudine As Double
        Public Property Longitudine As Double
        Public Property DistanzaKm As Double
        Public Property StatoApertura As Integer
    End Class

#End Region

#Region "Singleton e campi"

    Private Shared _istanza As CLSFarmacie
    Private Shared ReadOnly _lockIstanza As New Object()

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _cartellaDati As String
    Private ReadOnly _cartellaPdf As String
    Private ReadOnly _dbPath As String
    Private ReadOnly _connString As String
    Private ReadOnly _lockDb As New Object()
    Private ReadOnly _luoghi As CLSLuoghi = CLSLuoghi.GetInstance()
    Private _avanzamentoCorrenteLuoghi As Avanzamento

    Private _conn As SQLiteConnection

    Private Const CATEGORIA_FARMACIE As String = "Farmacie e parafarmacie"

    Private Const URL_ANAGRAFICA_PRIMARIA As String = "https://drive.aifa.gov.it/farmaci/confezioni_fornitura.csv"
    Private Const URL_ANAGRAFICA_SECONDARIA As String = "https://drive.aifa.gov.it/farmaci/confezioni.csv"
    Private Const URL_PRINCIPI As String = "https://drive.aifa.gov.it/farmaci/PA_confezioni.csv"
    Private Const URL_ATC As String = "https://drive.aifa.gov.it/farmaci/atc.csv"
    Private Const URL_CARENZE As String = "https://www.aifa.gov.it/documents/20142/847339/elenco_medicinali_carenti.csv"
    ' URL aggiornato: il vecchio path /imgs/C_17_dataset_8... restituisce 404.
    ' Il dataset ufficiale è pubblicato giornalmente come FRM_FARMA_5_YYYYMMDD.csv
    Private Const URL_FARMACIE_BASE As String = "https://www.dati.salute.gov.it/sites/default/files/opendata/"
    Private Const URL_FARMACIE_MINISTERO_LEGACY As String = "http://www.dati.salute.gov.it/imgs/C_17_dataset_8_download_itemDownload0_upFile.CSV"
    Private Const URL_NOMINATIM As String = "https://nominatim.openstreetmap.org/search"
    Private Const URL_BASE_STAMPATI As String = "https://api.aifa.gov.it/aifa-bdf-eif-be/1.0.0/"


    Private Shared ReadOnly _http As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(4)
    }

    Shared Sub New()
        Try
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (accessibilita; +https://wingabriel.it)")
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json, application/pdf, text/csv, */*")
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("it-IT,it;q=0.9")
        Catch
        End Try
    End Sub

    Private Sub New()
        Try
            _cartellaDati = ""
            Try
                _cartellaDati = WGDb.WGDatabase.CartellaDati("farmacie")
            Catch exConfigurata As Exception
                _cartellaDati = ""
            End Try
            If String.IsNullOrWhiteSpace(_cartellaDati) Then
                _cartellaDati = Path.Combine(Application.StartupPath, "dati", "farmacie")
            End If
            Directory.CreateDirectory(_cartellaDati)
            _cartellaPdf = Path.Combine(_cartellaDati, "pdf")
            Directory.CreateDirectory(_cartellaPdf)
            _dbPath = Path.Combine(_cartellaDati, "farmaci.db")
            _connString = "Data Source=" & _dbPath & ";Version=3;Journal Mode=WAL;Pooling=True;"
            InizializzaDatabase()
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.New: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function GetInstance() As CLSFarmacie
        Try
            SyncLock _lockIstanza
                If _istanza Is Nothing Then
                    _istanza = New CLSFarmacie()
                End If
                Return _istanza
            End SyncLock
        Catch
            Return _istanza
        End Try
    End Function

#End Region

#Region "Database"

    Private Function Connessione() As SQLiteConnection
        Try
            If _conn Is Nothing OrElse _conn.State <> ConnectionState.Open Then
                _conn = New SQLiteConnection(_connString)
                _conn.Open()
                ApplicaPragma(_conn)
            End If
            Return _conn
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Connessione: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ConnessioneNuova() As SQLiteConnection
        Try
            Dim c As New SQLiteConnection(_connString)
            c.Open()
            ApplicaPragma(c)
            Return c
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ConnessioneNuova: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub ApplicaPragma(conn As SQLiteConnection)
        Try
            If conn Is Nothing Then Return
            Using cmd As New SQLiteCommand(conn)
                cmd.CommandText = "PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;PRAGMA temp_store=MEMORY;PRAGMA cache_size=-40000;PRAGMA busy_timeout=8000;"
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ApplicaPragma: " & ex.ToString())
        End Try
    End Sub

    Private Sub InizializzaDatabase()
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return

                ' 1) Solo CREATE TABLE (senza indici che dipendono da colonne eventualmente assenti)
                Dim sql As New StringBuilder()
                sql.Append("CREATE TABLE IF NOT EXISTS Farmaci (")
                sql.Append("Aic6 TEXT PRIMARY KEY,")
                sql.Append("Nome TEXT COLLATE NOCASE,")
                sql.Append("Chiave TEXT COLLATE NOCASE,")
                sql.Append("PrincipioAttivo TEXT COLLATE NOCASE,")
                sql.Append("Azienda TEXT COLLATE NOCASE,")
                sql.Append("CodiceSis TEXT,")
                sql.Append("Atc TEXT COLLATE NOCASE,")
                sql.Append("AtcDescrizione TEXT,")
                sql.Append("Stato TEXT,")
                sql.Append("RegimeFornitura TEXT,")
                sql.Append("NumeroConfezioni INTEGER DEFAULT 0,")
                sql.Append("Carente INTEGER DEFAULT 0,")
                sql.Append("NoteCarenza TEXT,")
                sql.Append("LinkFi TEXT,")
                sql.Append("LinkRcp TEXT,")
                sql.Append("DataAggiornamento TEXT);")

                sql.Append("CREATE TABLE IF NOT EXISTS Confezioni (")
                sql.Append("Aic9 TEXT PRIMARY KEY,")
                sql.Append("Aic6 TEXT NOT NULL,")
                sql.Append("Descrizione TEXT,")
                sql.Append("RegimeFornitura TEXT,")
                sql.Append("Stato TEXT);")

                sql.Append("CREATE TABLE IF NOT EXISTS Stampati (")
                sql.Append("Aic6 TEXT NOT NULL,")
                sql.Append("Tipo TEXT NOT NULL,")
                sql.Append("CodiceSis TEXT,")
                sql.Append("PdfPath TEXT,")
                sql.Append("TestoEstratto TEXT,")
                sql.Append("DataDownload TEXT,")
                sql.Append("PRIMARY KEY (Aic6, Tipo));")

                sql.Append("CREATE TABLE IF NOT EXISTS Farmacie (")
                sql.Append("Codice TEXT PRIMARY KEY,")
                sql.Append("Denominazione TEXT COLLATE NOCASE,")
                sql.Append("Indirizzo TEXT,")
                sql.Append("Cap TEXT,")
                sql.Append("Comune TEXT COLLATE NOCASE,")
                sql.Append("Provincia TEXT COLLATE NOCASE,")
                sql.Append("Regione TEXT COLLATE NOCASE,")
                sql.Append("Telefono TEXT,")
                sql.Append("Latitudine REAL DEFAULT 0,")
                sql.Append("Longitudine REAL DEFAULT 0);")

                sql.Append("CREATE TABLE IF NOT EXISTS FarmacieOrari (")
                sql.Append("IdOsm TEXT PRIMARY KEY,")
                sql.Append("Zona TEXT COLLATE NOCASE,")
                sql.Append("Denominazione TEXT COLLATE NOCASE,")
                sql.Append("Indirizzo TEXT,")
                sql.Append("Comune TEXT COLLATE NOCASE,")
                sql.Append("Telefono TEXT,")
                sql.Append("Orari TEXT,")
                sql.Append("Sito TEXT,")
                sql.Append("Email TEXT,")
                sql.Append("Marca TEXT,")
                sql.Append("IdGers TEXT,")
                sql.Append("PercorsoTattile TEXT,")
                sql.Append("SegnalatoreAcustico TEXT,")
                sql.Append("Braille TEXT,")
                sql.Append("Corrimano TEXT,")
                sql.Append("DescrizionePerNonVedenti TEXT,")
                sql.Append("AccessoSediaRotelle TEXT,")
                sql.Append("BagnoAccessibile TEXT,")
                sql.Append("CaniGuidaAmmessi TEXT,")
                sql.Append("Latitudine REAL DEFAULT 0,")
                sql.Append("Longitudine REAL DEFAULT 0,")
                sql.Append("DistanzaKm REAL DEFAULT 0,")
                sql.Append("DataAggiornamento TEXT);")

                sql.Append("CREATE TABLE IF NOT EXISTS Meta (Chiave TEXT PRIMARY KEY, Valore TEXT);")

                Using cmd As New SQLiteCommand(sql.ToString(), conn)
                    cmd.ExecuteNonQuery()
                End Using

                ' 2) Migrazione: aggiunge colonne mancanti su database già esistenti
                MigraSchema(conn)

                ' 3) Indici (ora le colonne esistono di sicuro)
                Dim sqlIdx As New StringBuilder()
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmaci_nome ON Farmaci(Nome COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmaci_chiave ON Farmaci(Chiave COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmaci_principio ON Farmaci(PrincipioAttivo COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmaci_azienda ON Farmaci(Azienda COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmaci_atc ON Farmaci(Atc COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_conf_aic6 ON Confezioni(Aic6);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_farmacie_comune ON Farmacie(Comune COLLATE NOCASE);")
                sqlIdx.Append("CREATE INDEX IF NOT EXISTS idx_orari_zona ON FarmacieOrari(Zona COLLATE NOCASE);")
                Using cmdIdx As New SQLiteCommand(sqlIdx.ToString(), conn)
                    cmdIdx.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.InizializzaDatabase: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Aggiunge colonne mancanti su un database creato con una versione precedente dello schema.
    ''' </summary>
    Private Sub MigraSchema(conn As SQLiteConnection)
        Try
            If conn Is Nothing Then Return

            ' Colonne introdotte nelle versioni successive di Farmaci
            AssicuraColonna(conn, "Farmaci", "Chiave", "TEXT COLLATE NOCASE")
            AssicuraColonna(conn, "Farmaci", "PrincipioAttivo", "TEXT COLLATE NOCASE")
            AssicuraColonna(conn, "Farmaci", "Azienda", "TEXT COLLATE NOCASE")
            AssicuraColonna(conn, "Farmaci", "CodiceSis", "TEXT")
            AssicuraColonna(conn, "Farmaci", "Atc", "TEXT COLLATE NOCASE")
            AssicuraColonna(conn, "Farmaci", "AtcDescrizione", "TEXT")
            AssicuraColonna(conn, "Farmaci", "Stato", "TEXT")
            AssicuraColonna(conn, "Farmaci", "RegimeFornitura", "TEXT")
            AssicuraColonna(conn, "Farmaci", "NumeroConfezioni", "INTEGER DEFAULT 0")
            AssicuraColonna(conn, "Farmaci", "Carente", "INTEGER DEFAULT 0")
            AssicuraColonna(conn, "Farmaci", "NoteCarenza", "TEXT")
            AssicuraColonna(conn, "Farmaci", "LinkFi", "TEXT")
            AssicuraColonna(conn, "Farmaci", "LinkRcp", "TEXT")
            AssicuraColonna(conn, "Farmaci", "DataAggiornamento", "TEXT")

            ' Colonne eventuali su Confezioni
            AssicuraColonna(conn, "Confezioni", "Descrizione", "TEXT")
            AssicuraColonna(conn, "Confezioni", "RegimeFornitura", "TEXT")
            AssicuraColonna(conn, "Confezioni", "Stato", "TEXT")

            ' Colonne eventuali su Farmacie
            AssicuraColonna(conn, "Farmacie", "Telefono", "TEXT")
            AssicuraColonna(conn, "Farmacie", "Latitudine", "REAL DEFAULT 0")
            AssicuraColonna(conn, "Farmacie", "Longitudine", "REAL DEFAULT 0")

            ' Colonne eventuali su Stampati
            AssicuraColonna(conn, "Stampati", "CodiceSis", "TEXT")
            AssicuraColonna(conn, "Stampati", "PdfPath", "TEXT")
            AssicuraColonna(conn, "Stampati", "TestoEstratto", "TEXT")
            AssicuraColonna(conn, "Stampati", "DataDownload", "TEXT")

            ' Colonne introdotte con l'arricchimento tramite ClsLuoghi/CLSOverture
            AssicuraColonna(conn, "FarmacieOrari", "Sito", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "Email", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "Marca", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "IdGers", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "PercorsoTattile", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "SegnalatoreAcustico", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "Braille", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "Corrimano", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "DescrizionePerNonVedenti", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "AccessoSediaRotelle", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "BagnoAccessibile", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "CaniGuidaAmmessi", "TEXT")
            AssicuraColonna(conn, "FarmacieOrari", "DistanzaKm", "REAL DEFAULT 0")
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.MigraSchema: " & ex.ToString())
        End Try
    End Sub

    Private Sub AssicuraColonna(conn As SQLiteConnection, tabella As String, colonna As String, definizione As String)
        Try
            If conn Is Nothing OrElse String.IsNullOrWhiteSpace(tabella) OrElse String.IsNullOrWhiteSpace(colonna) Then Return
            If Not EsisteTabella(conn, tabella) Then Return
            If EsisteColonna(conn, tabella, colonna) Then Return

            Dim sql As String = "ALTER TABLE " & tabella & " ADD COLUMN " & colonna & " " & definizione & ";"
            Using cmd As New SQLiteCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        Catch
        End Try
    End Sub

    Private Function EsisteTabella(conn As SQLiteConnection, tabella As String) As Boolean
        Try
            Using cmd As New SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1", conn)
                cmd.Parameters.AddWithValue("@n", tabella)
                Return cmd.ExecuteScalar() IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Function EsisteColonna(conn As SQLiteConnection, tabella As String, colonna As String) As Boolean
        Try
            Using cmd As New SQLiteCommand("PRAGMA table_info(" & tabella & ")", conn)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        Dim nome As String = rdr("name").ToString()
                        If String.Equals(nome, colonna, StringComparison.OrdinalIgnoreCase) Then Return True
                    End While
                End Using
            End Using
        Catch
        End Try
        Return False
    End Function

    Private Sub ImpostaMeta(chiave As String, valore As String)
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Meta (Chiave, Valore) VALUES (@k,@v)", conn)
                    cmd.Parameters.AddWithValue("@k", chiave)
                    cmd.Parameters.AddWithValue("@v", If(valore, ""))
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ImpostaMeta: " & ex.ToString())
        End Try
    End Sub

    Private Function LeggiMeta(chiave As String) As String
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return ""
                Using cmd As New SQLiteCommand("SELECT Valore FROM Meta WHERE Chiave=@k", conn)
                    cmd.Parameters.AddWithValue("@k", chiave)
                    Dim o = cmd.ExecuteScalar()
                    If o IsNot Nothing AndAlso Not IsDBNull(o) Then Return o.ToString()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.LeggiMeta: " & ex.ToString())
        End Try
        Return ""
    End Function

#End Region

#Region "Utilita generali"

    Private Sub Segnala(avanzamento As Avanzamento, testo As String)
        Try
            If avanzamento Is Nothing Then Return
            avanzamento.Invoke(testo)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Segnala: " & ex.Message)
        End Try
    End Sub

    Private Sub InoltraAvanzamentoLuoghi(testo As String)
        Try
            Segnala(_avanzamentoCorrenteLuoghi, testo)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.InoltraAvanzamentoLuoghi: " & ex.Message)
        End Try
    End Sub

    Public Function EstraiAic6(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim solo As New StringBuilder()
            For Each c In valore
                If Char.IsDigit(c) Then solo.Append(c)
            Next
            Dim s As String = solo.ToString()
            If s.Length = 0 Then Return ""
            If s.Length >= 9 Then Return s.Substring(0, 6)
            If s.Length >= 6 Then Return s.Substring(0, 6)
            Return s.PadLeft(6, "0"c)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EstraiAic6: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiAic9(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim solo As New StringBuilder()
            For Each c In valore
                If Char.IsDigit(c) Then solo.Append(c)
            Next
            Dim s As String = solo.ToString()
            If s.Length = 0 Then Return ""
            If s.Length > 9 Then s = s.Substring(0, 9)
            Return s.PadLeft(9, "0"c)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EstraiAic9: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function SoloNumeri(s As String) As String
        Try
            If String.IsNullOrEmpty(s) Then Return ""
            Dim sb As New StringBuilder()
            For Each c In s
                If Char.IsDigit(c) Then sb.Append(c)
            Next
            Return sb.ToString()
        Catch
            Return ""
        End Try
    End Function

    Public Function Normalizza(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim decomposto As String = testo.Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder(decomposto.Length)
            For Each c In decomposto
                If CharUnicodeInfo.GetUnicodeCategory(c) <> UnicodeCategory.NonSpacingMark Then
                    sb.Append(c)
                End If
            Next
            Return sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Normalizza: " & ex.Message)
            Return If(testo, "").ToUpperInvariant()
        End Try
    End Function

#End Region

#Region "Testi leggibili e guida ai termini"

    Private Shared ReadOnly _regimeFornitura As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"RR", "vendibile solo dietro presentazione di ricetta medica"},
        {"RRL", "vendibile con ricetta medica limitativa, da rinnovare volta per volta"},
        {"RRN", "vendibile con ricetta medica da rinnovare volta per volta"},
        {"RNR", "vendibile senza obbligo di ricetta medica"},
        {"RNRL", "vendibile senza obbligo di ricetta ma con dispensazione limitata"},
        {"OTC", "farmaco da banco: si compra senza ricetta e puo' essere pubblicizzato"},
        {"SOP", "farmaco senza obbligo di prescrizione, ma non pubblicizzabile"},
        {"USPL", "uso riservato esclusivamente all'ambiente ospedaliero"},
        {"OSP", "uso ospedaliero"},
        {"OSP1", "uso ospedaliero: utilizzabile solo all'interno della struttura"},
        {"OSP2", "uso ospedaliero, ma prescrivibile anche da specialisti fuori dall'ospedale"},
        {"RL", "vendibile solo su prescrizione di centri specialistici autorizzati"},
        {"RMR", "vendibile su prescrizione di centri ospedalieri o specialisti"}
    }

    ''' <summary>
    ''' Aggiunge, tra parentesi, una spiegazione in italiano semplice quando riconosce
    ''' nel testo del regime di fornitura le formule tipiche AIFA (es. "non soggetto a
    ''' prescrizione ma non da banco"), che da sole restano oscure a chi non e' del settore.
    ''' </summary>
    Private Function ChiarificaRegimeFornitura(testo As String) As String
        Try
            Dim n As String = Normalizza(testo)
            If n.Contains("NON SOGGETTO A PRESCRIZIONE") OrElse n.Contains("SENZA OBBLIGO DI PRESCRIZIONE") OrElse n.Contains("NON SOGGETTA A PRESCRIZIONE") Then
                If n.Contains("BANCO") OrElse n.Contains("AUTOMEDICAZIONE") Then
                    Return "non serve la ricetta del medico, ma non e' nemmeno un farmaco da banco: te lo deve consigliare o darti il farmacista, dietro al banco"
                End If
                Return "non serve la ricetta del medico per comprarlo"
            End If
            If n.Contains("BANCO") OrElse n.Contains("AUTOMEDICAZIONE") Then
                Return "lo scegli liberamente in farmacia, anche senza il consiglio del farmacista, ed e' un farmaco che puo' essere pubblicizzato"
            End If
            If n.Contains("OSPEDAL") Then
                Return "viene usato o dispensato solo dalle strutture sanitarie, non si acquista in farmacia"
            End If
            If n.Contains("PRESCRIZIONE MEDICA") Then
                If n.Contains("LIMITATIVA") Then
                    Return "serve la ricetta del medico, con alcune limitazioni su chi puo' prescriverlo"
                End If
                Return "serve la ricetta del medico per comprarlo"
            End If
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ChiarificaRegimeFornitura: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Traduce un codice o una frase AIFA di regime di fornitura in un testo comprensibile.
    ''' Toglie eventuali codici numerici o alfanumerici iniziali (es. "016 - ") e, quando
    ''' riconosce le formule tipiche AIFA, aggiunge tra parentesi una spiegazione semplice.
    ''' </summary>
    Public Function TestoRegimeFornitura(codice As String) As String
        Try
            Dim c As String = If(codice, "").Trim()
            If c.Length = 0 Then Return "non indicato"

            Dim pulito As String = c.ToUpperInvariant()
            If _regimeFornitura.ContainsKey(pulito) Then Return _regimeFornitura(pulito)

            ' Toglie un eventuale codice iniziale tipo "016 ", "016 - ", "RR: "
            Dim senzaCodice As String = Regex.Replace(c, "^[A-Za-z0-9]{1,6}\s*[-:]?\s+", "")
            If senzaCodice.Length < 8 Then senzaCodice = c

            If senzaCodice.ToUpperInvariant() <> pulito AndAlso _regimeFornitura.ContainsKey(senzaCodice.ToUpperInvariant()) Then
                Return _regimeFornitura(senzaCodice.ToUpperInvariant())
            End If

            Dim testoBase As String = senzaCodice.Trim()
            If testoBase.Length = 0 Then Return c
            testoBase = Char.ToUpperInvariant(testoBase(0)) & testoBase.Substring(1)

            Dim chiarimento As String = ChiarificaRegimeFornitura(testoBase)
            If chiarimento.Length > 0 Then Return testoBase & " (" & chiarimento & ")"
            Return testoBase
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TestoRegimeFornitura: " & ex.Message)
            Return If(codice, "non indicato")
        End Try
    End Function

    ''' <summary>
    ''' Traduce lo stato amministrativo dell'AIC in una frase comprensibile.
    ''' </summary>
    Public Function TestoStatoAic(codice As String) As String
        Try
            Dim c As String = If(codice, "").Trim()
            If c.Length = 0 Then Return "non indicato"
            Dim pulito As String = Normalizza(c)
            If pulito.Contains("ATTIV") Then Return "AIC attiva: il farmaco e' regolarmente in commercio"
            If pulito.Contains("SOSPES") Then Return "AIC sospesa: il farmaco non e' momentaneamente in commercio"
            If pulito.Contains("REVOC") Then Return "AIC revocata: il farmaco non e' piu' autorizzato"
            If pulito.Contains("NON COMMERC") OrElse pulito.Contains("NONCOMMERC") Then Return "autorizzato ma non commercializzato in Italia"
            If pulito.Contains("RINUNCIA") Then Return "AIC decaduta per rinuncia dell'azienda titolare"
            Return c
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TestoStatoAic: " & ex.Message)
            Return If(codice, "non indicato")
        End Try
    End Function

    Private Shared ReadOnly _abbreviazioniConfezione As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"CPR", "compresse"}, {"CPS", "capsule"}, {"CPS.RIG", "capsule rigide"}, {"CPS.MOLL", "capsule molli"},
        {"RIV", "rivestite con film"}, {"GASTR", "gastroresistenti"}, {"EFF", "effervescenti"},
        {"MASTIC", "masticabili"}, {"OROD", "orodispersibili"}, {"BUST", "bustine"}, {"FLAC", "flacone"},
        {"FL", "flacone"}, {"GTT", "gocce"}, {"SCIR", "sciroppo"}, {"SOSP", "sospensione"}, {"SOL", "soluzione"},
        {"INIET", "iniettabile"}, {"POLV", "polvere"}, {"UNG", "unguento"}, {"CREMA", "crema"}, {"CR", "crema"},
        {"GEL", "gel"}, {"SUPP", "supposte"}, {"OVUL", "ovuli"}, {"COLL", "collirio"}, {"SPRAY", "spray"},
        {"AER", "aerosol"}, {"CER", "cerotto transdermico"}, {"SC", "uso sottocutaneo"}, {"IM", "uso intramuscolare"},
        {"EV", "uso endovenoso"}, {"OS", "uso orale"}, {"OFT", "uso oftalmico"}, {"RP", "a rilascio prolungato"},
        {"AST", "astuccio"}, {"SIR", "siringa preriempita"}, {"PRER", "preriempita"}, {"FIALE", "fiale"},
        {"F", "fiale"}, {"CONF", "confezione"}, {"TUBO", "tubo"}, {"USO", "per uso"}
    }

    ''' <summary>
    ''' Espande le sigle piu' comuni presenti nella descrizione AIFA di una confezione
    ''' (es. "CPR RIV" -> "compresse rivestite con film") per renderla leggibile.
    ''' </summary>
    Public Function TestoConfezioneLeggibile(descrizione As String) As String
        Try
            If String.IsNullOrWhiteSpace(descrizione) Then Return "non disponibile"

            Dim testo As String = descrizione.Trim()
            testo = Regex.Replace(testo, "(\d)([A-Za-z])", "$1 $2")
            testo = Regex.Replace(testo, "([A-Za-z])(\d)", "$1 $2")

            Dim pezzi() As String = testo.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            Dim risultato As New StringBuilder()
            For Each pezzo In pezzi
                Dim pulito As String = pezzo.Trim().Trim("."c)
                If _abbreviazioniConfezione.ContainsKey(pulito) Then
                    If risultato.Length > 0 Then risultato.Append(" ")
                    risultato.Append(_abbreviazioniConfezione(pulito))
                Else
                    If risultato.Length > 0 Then risultato.Append(" ")
                    risultato.Append(pezzo)
                End If
            Next

            Dim finale As String = risultato.ToString().Trim()
            If finale.Length = 0 Then Return descrizione.Trim()
            Return Char.ToUpperInvariant(finale(0)) & finale.Substring(1)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TestoConfezioneLeggibile: " & ex.Message)
            Return If(descrizione, "non disponibile")
        End Try
    End Function

    ''' <summary>
    ''' Guida di riferimento con i termini piu' difficili usati in questa sezione,
    ''' pensata per essere aperta dal menu Opzioni e letta con lo screen reader.
    ''' </summary>
    Public Function TestoGuida() As String
        Try
            Dim g As New StringBuilder()
            g.AppendLine("Guida ai termini dei farmaci")
            g.AppendLine()
            g.AppendLine("Codice AIC")
            g.AppendLine("E' il numero che identifica in modo univoco un farmaco presso l'AIFA, l'Agenzia Italiana del Farmaco. Il codice a 6 cifre identifica il farmaco nel suo complesso, il codice a 9 cifre identifica la singola confezione, cioe' un formato, dosaggio o quantita' specifici.")
            g.AppendLine()
            g.AppendLine("Bugiardino, o foglio illustrativo (FI)")
            g.AppendLine("E' il documento che si trova dentro la scatola del farmaco. Spiega a cosa serve il farmaco, come si usa, quali effetti indesiderati puo' avere e con quali altri medicinali puo' interferire. E' scritto per chi assume il farmaco.")
            g.AppendLine()
            g.AppendLine("Riassunto delle caratteristiche del prodotto (RCP)")
            g.AppendLine("E' il documento tecnico destinato ai medici e ai farmacisti. Contiene le stesse informazioni del bugiardino ma in modo piu' dettagliato e con linguaggio medico-scientifico.")
            g.AppendLine()
            g.AppendLine("Principio attivo")
            g.AppendLine("E' la sostanza che produce l'effetto terapeutico del farmaco. Un farmaco equivalente, spesso chiamato generico, contiene lo stesso principio attivo alla stessa dose di un farmaco di marca, ma costa meno perche' non ha piu' il brevetto.")
            g.AppendLine()
            g.AppendLine("Codice ATC")
            g.AppendLine("E' un codice internazionale che classifica il farmaco in base all'organo o apparato su cui agisce e al tipo di azione terapeutica. Farmaci con lo stesso codice ATC agiscono in modo simile.")
            g.AppendLine()
            g.AppendLine("Regime di fornitura")
            g.AppendLine("Indica come si puo' acquistare il farmaco. Puo' essere scritto con una sigla breve oppure con una frase estesa, a volte preceduta da un codice numerico come 016. Le formule piu' comuni sono: soggetto a prescrizione medica, cioe' serve la ricetta del medico; non soggetto a prescrizione medica, cioe' non serve la ricetta; farmaco da banco o di automedicazione, cioe' si sceglie liberamente senza il consiglio del farmacista e puo' essere pubblicizzato; uso ospedaliero, cioe' non si acquista in farmacia ma viene dispensato dalle strutture sanitarie. Esiste anche una via di mezzo: non soggetto a prescrizione ma non da banco, che vuol dire che non serve la ricetta del medico ma il farmaco non e' comunque liberamente accessibile come quelli da banco: deve essere consigliato o consegnato dal farmacista, dietro al banco.")
            g.AppendLine()
            g.AppendLine("Stato dell'AIC")
            g.AppendLine("Indica se il farmaco e' attualmente in commercio. Attiva vuol dire che il farmaco e' regolarmente in vendita. Sospesa vuol dire che momentaneamente non viene distribuito. Revocata vuol dire che l'autorizzazione non e' piu' valida e il farmaco non puo' piu' essere venduto.")
            g.AppendLine()
            g.AppendLine("Farmaco carente")
            g.AppendLine("Un farmaco viene segnalato come carente quando l'azienda produttrice comunica all'AIFA una difficolta' temporanea di approvvigionamento, quindi puo' non essere disponibile in farmacia per un certo periodo.")
            g.AppendLine()
            g.AppendLine("Abbreviazioni piu' comuni nella descrizione delle confezioni")
            g.AppendLine("CPR sta per compresse, CPS per capsule, RIV per rivestite con film, BUST per bustine, FLAC per flacone, GTT per gocce, SCIR per sciroppo, SOL per soluzione, POLV per polvere, SUPP per supposte, COLL per collirio. OS indica l'uso orale, EV l'uso endovenoso, IM l'uso intramuscolare, SC l'uso sottocutaneo. MG sta per milligrammi, MCG per microgrammi e ML per millilitri.")
            Return g.ToString().Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TestoGuida: " & ex.Message)
            Return "Guida non disponibile al momento."
        End Try
    End Function

#End Region

#Region "Rete"

    Private Function ScaricaBytes(url As String, Optional tentativi As Integer = 3, Optional comeNavigatore As Boolean = False) As Byte()
        Dim attesa As Integer = 1200
        For i As Integer = 1 To Math.Max(1, tentativi)
            Try
                Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                    If comeNavigatore Then
                        richiesta.Headers.UserAgent.Clear()
                        richiesta.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36")
                        richiesta.Headers.Accept.Clear()
                        richiesta.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
                    End If
                    Dim risposta = _http.SendAsync(richiesta, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult()
                    If risposta.IsSuccessStatusCode Then
                        Dim bytes = risposta.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                        If bytes IsNot Nothing AndAlso bytes.Length > 64 Then Return bytes
                    Else
                        If CInt(risposta.StatusCode) = 404 OrElse CInt(risposta.StatusCode) = 403 Then Return Nothing
                    End If
                End Using
            Catch
            End Try
            Try
                Thread.Sleep(attesa)
                attesa = attesa * 2
            Catch
            End Try
        Next
        Return Nothing
    End Function

    Private Function ScaricaFile(url As String, percorsoLocale As String, Optional comeNavigatore As Boolean = False) As Boolean
        Try
            Dim bytes = ScaricaBytes(url, 3, comeNavigatore)
            If bytes Is Nothing OrElse bytes.Length < 100 Then Return False
            File.WriteAllBytes(percorsoLocale, bytes)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Function ScaricaTesto(url As String, Optional tentativi As Integer = 3, Optional comeNavigatore As Boolean = False) As String
        Try
            Dim bytes = ScaricaBytes(url, tentativi, comeNavigatore)
            If bytes Is Nothing Then Return ""
            Return Encoding.UTF8.GetString(bytes)
        Catch
            Return ""
        End Try
    End Function

#End Region

#Region "Lettura CSV"

    Private Function SeparatoreCsv(primaRiga As String) As Char
        Try
            If String.IsNullOrEmpty(primaRiga) Then Return ";"c
            Dim nPv As Integer = 0, nVirg As Integer = 0, nTab As Integer = 0
            For Each ch In primaRiga
                If ch = ";"c Then nPv += 1
                If ch = ","c Then nVirg += 1
                If ch = ControlChars.Tab Then nTab += 1
            Next
            If nTab > nPv AndAlso nTab > nVirg Then Return ControlChars.Tab
            If nVirg > nPv Then Return ","c
            Return ";"c
        Catch
            Return ";"c
        End Try
    End Function

    ''' <summary>
    ''' AIFA non usa sempre la stessa codifica per i CSV: alcuni sono UTF-8, altri Windows-1252
    ''' Si prova UTF-8
    ''' in modo rigoroso: se non e' valido, si passa a Windows-1252 senza dover indovinare a priori.
    ''' </summary>
    Private Function RilevaEncodingFile(percorso As String) As Encoding
        Try
            Dim buffer(8191) As Byte
            Dim letti As Integer
            Using fs As New FileStream(percorso, FileMode.Open, FileAccess.Read)
                letti = fs.Read(buffer, 0, buffer.Length)
            End Using

            If letti >= 3 AndAlso buffer(0) = &HEF AndAlso buffer(1) = &HBB AndAlso buffer(2) = &HBF Then
                Return Encoding.UTF8
            End If

            Try
                Dim utf8Rigoroso As New UTF8Encoding(False, True)
                utf8Rigoroso.GetString(buffer, 0, letti)
                Return Encoding.UTF8
            Catch
                Return Encoding.GetEncoding(1252)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.RilevaEncodingFile: " & ex.Message)
            Return Encoding.UTF8
        End Try
    End Function

    Private Function DividiRigaCsv(riga As String, sep As Char) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If riga Is Nothing Then Return risultato
            Dim sb As New StringBuilder()
            Dim traVirgolette As Boolean = False
            Dim i As Integer = 0
            While i < riga.Length
                Dim c As Char = riga(i)
                If c = """"c Then
                    If traVirgolette AndAlso i + 1 < riga.Length AndAlso riga(i + 1) = """"c Then
                        sb.Append(""""c)
                        i += 1
                    Else
                        traVirgolette = Not traVirgolette
                    End If
                ElseIf c = sep AndAlso Not traVirgolette Then
                    risultato.Add(sb.ToString())
                    sb.Length = 0
                Else
                    sb.Append(c)
                End If
                i += 1
            End While
            risultato.Add(sb.ToString())
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.DividiRigaCsv: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function IndiceColonna(intestazione As List(Of String), ParamArray nomi() As String) As Integer
        Try
            If intestazione Is Nothing Then Return -1
            For Each nome In nomi
                Dim cercato As String = Normalizza(nome).Replace(" ", "").Replace("_", "")
                For i As Integer = 0 To intestazione.Count - 1
                    Dim h As String = Normalizza(intestazione(i).Trim().Trim(""""c)).Replace(" ", "").Replace("_", "")
                    If h = cercato Then Return i
                Next
            Next
            For Each nome In nomi
                Dim cercato As String = Normalizza(nome).Replace(" ", "").Replace("_", "")
                For i As Integer = 0 To intestazione.Count - 1
                    Dim h As String = Normalizza(intestazione(i).Trim().Trim(""""c)).Replace(" ", "").Replace("_", "")
                    If h.Length > 0 AndAlso cercato.Length > 0 AndAlso h.Contains(cercato) Then Return i
                Next
            Next
            Return -1
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.IndiceColonna: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function ValoreCampo(campi As List(Of String), indice As Integer) As String
        Try
            If campi Is Nothing OrElse indice < 0 OrElse indice >= campi.Count Then Return ""
            Dim v As String = campi(indice)
            If v Is Nothing Then Return ""
            v = v.Trim().Trim(""""c).Trim()
            If String.Equals(v, "N.D.", StringComparison.OrdinalIgnoreCase) Then Return ""
            If String.Equals(v, "ND", StringComparison.OrdinalIgnoreCase) Then Return ""
            Return v
        Catch
            Return ""
        End Try
    End Function

    Private Function TrovaIntestazione(lettore As StreamReader, ByRef sep As Char, ByRef intestazione As List(Of String), ParamArray colonneAttese() As String) As Boolean
        Try
            Dim righeProvate As Integer = 0
            While righeProvate < 25
                Dim riga As String = lettore.ReadLine()
                If riga Is Nothing Then Return False
                righeProvate += 1
                If String.IsNullOrWhiteSpace(riga) Then Continue While
                Dim s As Char = SeparatoreCsv(riga)
                Dim campi = DividiRigaCsv(riga, s)
                If campi.Count < 2 Then Continue While
                Dim trovate As Integer = 0
                For Each atteso In colonneAttese
                    If IndiceColonna(campi, atteso) >= 0 Then trovate += 1
                Next
                If trovate > 0 Then
                    sep = s
                    intestazione = campi
                    Return True
                End If
            End While
            Return False
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TrovaIntestazione: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

#Region "Aggiornamento anagrafica"

    Public Function GiorniDaUltimoAggiornamento() As Integer
        Try
            Dim ultimo As String = LeggiMeta("UltimoAggiornamento")
            If String.IsNullOrEmpty(ultimo) Then Return 9999
            Dim dt As DateTime
            If DateTime.TryParse(ultimo, dt) Then
                Return CInt(Math.Floor((DateTime.Now - dt).TotalDays))
            End If
            Return 9999
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.GiorniDaUltimoAggiornamento: " & ex.Message)
            Return 9999
        End Try
    End Function

    Public Function AggiornaAnagrafica(Optional forza As Boolean = False, Optional avanzamento As Avanzamento = Nothing) As Boolean
        Dim tempAnagrafica As String = ""
        Dim tempPrincipi As String = ""
        Dim tempAtc As String = ""
        Dim tempCarenze As String = ""
        Try
            If Not forza Then
                If NumeroFarmaci() > 0 AndAlso GiorniDaUltimoAggiornamento() < 7 Then
                    Return True
                End If
            End If

            Segnala(avanzamento, "Scarico l'anagrafica dei farmaci dall'AIFA")

            tempAnagrafica = Path.Combine(_cartellaDati, "anagrafica_tmp.csv")
            tempPrincipi = Path.Combine(_cartellaDati, "principi_tmp.csv")
            tempAtc = Path.Combine(_cartellaDati, "atc_tmp.csv")
            tempCarenze = Path.Combine(_cartellaDati, "carenze_tmp.csv")

            Dim scaricata As Boolean = ScaricaFile(URL_ANAGRAFICA_PRIMARIA, tempAnagrafica)
            If Not scaricata Then
                Segnala(avanzamento, "Primo indirizzo non disponibile, provo l'archivio completo")
                scaricata = ScaricaFile(URL_ANAGRAFICA_SECONDARIA, tempAnagrafica)
            End If
            If Not scaricata Then
                Return False
            End If

            Segnala(avanzamento, "Scarico i principi attivi")
            Dim principiOk As Boolean = ScaricaFile(URL_PRINCIPI, tempPrincipi)

            Segnala(avanzamento, "Scarico le categorie terapeutiche")
            Dim atcOk As Boolean = ScaricaFile(URL_ATC, tempAtc)

            Segnala(avanzamento, "Importo le confezioni nel database, questa operazione richiede qualche minuto")
            Dim confezioniImportate As Integer = ImportaConfezioni(tempAnagrafica, avanzamento)
            If confezioniImportate <= 0 Then
                Return False
            End If

            Dim mappaPrincipi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If principiOk Then
                Segnala(avanzamento, "Associo i principi attivi ai farmaci")
                mappaPrincipi = LeggiPrincipiAttivi(tempPrincipi)
            End If

            Dim mappaAtc As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If atcOk Then
                Segnala(avanzamento, "Associo le categorie terapeutiche")
                mappaAtc = LeggiDescrizioniAtc(tempAtc)
            End If

            Segnala(avanzamento, "Preparo l'elenco dei farmaci")
            Dim farmaciCreati As Integer = ConsolidaFarmaci(mappaPrincipi, mappaAtc, avanzamento)

            Segnala(avanzamento, "Scarico l'elenco dei farmaci temporaneamente carenti")
            If ScaricaFile(URL_CARENZE, tempCarenze, True) Then
                ImportaCarenze(tempCarenze, avanzamento)
            End If

            ImpostaMeta("UltimoAggiornamento", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            ImpostaMeta("NumeroFarmaci", farmaciCreati.ToString())
            Segnala(avanzamento, "Archivio aggiornato")
            Return farmaciCreati > 0
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AggiornaAnagrafica: " & ex.ToString())
            Return False
        Finally
            Try
                If tempAnagrafica.Length > 0 AndAlso File.Exists(tempAnagrafica) Then File.Delete(tempAnagrafica)
                If tempPrincipi.Length > 0 AndAlso File.Exists(tempPrincipi) Then File.Delete(tempPrincipi)
                If tempAtc.Length > 0 AndAlso File.Exists(tempAtc) Then File.Delete(tempAtc)
                If tempCarenze.Length > 0 AndAlso File.Exists(tempCarenze) Then File.Delete(tempCarenze)
            Catch
            End Try
        End Try
    End Function

    Private Function ImportaConfezioni(percorso As String, avanzamento As Avanzamento) As Integer
        Dim inserite As Integer = 0
        Try
            If Not File.Exists(percorso) Then Return 0

            Using conn = ConnessioneNuova()
                If conn Is Nothing Then Return 0
                Using trans = conn.BeginTransaction()
                    Using cmdPulisci As New SQLiteCommand("DELETE FROM Confezioni;", conn, trans)
                        cmdPulisci.ExecuteNonQuery()
                    End Using

                    Using cmd As New SQLiteCommand(
                        "INSERT OR REPLACE INTO Confezioni (Aic9, Aic6, Descrizione, RegimeFornitura, Stato) VALUES (@a9,@a6,@d,@r,@s)", conn, trans)

                        cmd.Parameters.Add("@a9", DbType.String)
                        cmd.Parameters.Add("@a6", DbType.String)
                        cmd.Parameters.Add("@d", DbType.String)
                        cmd.Parameters.Add("@r", DbType.String)
                        cmd.Parameters.Add("@s", DbType.String)

                        Using lettore As New StreamReader(percorso, RilevaEncodingFile(percorso))
                            Dim sep As Char = ";"c
                            Dim intestazione As List(Of String) = Nothing
                            If Not TrovaIntestazione(lettore, sep, intestazione, "codice_aic", "AIC", "CODICE AIC") Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim iAic As Integer = IndiceColonna(intestazione, "codice_aic", "AIC", "CODICE AIC", "CODICE_AIC")
                            Dim iNome As Integer = IndiceColonna(intestazione, "denominazione", "nome", "descrizione_farmaco", "farmaco")
                            Dim iConf As Integer = IndiceColonna(intestazione, "descrizione_confezione", "confezione", "des_conf")
                            Dim iAzienda As Integer = IndiceColonna(intestazione, "ragione_sociale", "azienda", "titolare", "ditta")
                            Dim iSis As Integer = IndiceColonna(intestazione, "codice_sis", "sis", "cod_sis", "codice_ditta")
                            Dim iAtc As Integer = IndiceColonna(intestazione, "codice_atc", "atc")
                            Dim iStato As Integer = IndiceColonna(intestazione, "stato_amministrativo", "stato")
                            Dim iRegime As Integer = IndiceColonna(intestazione, "regime_fornitura", "fornitura", "regime")
                            Dim iLinkFi As Integer = IndiceColonna(intestazione, "link_fi")
                            Dim iLinkRcp As Integer = IndiceColonna(intestazione, "link_rcp")

                            Dim scriviSis As Boolean = iSis >= 0
                            Dim scriviAtc As Boolean = iAtc >= 0
                            Dim mappaSis As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                            Dim mappaAtcFarmaco As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                            Dim mappaNome As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                            Dim mappaAzienda As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                            Dim mappaLinkFi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                            Dim mappaLinkRcp As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

                            If iAic < 0 Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim riga As String = lettore.ReadLine()
                            While riga IsNot Nothing
                                Try
                                    If Not String.IsNullOrWhiteSpace(riga) Then
                                        Dim campi = DividiRigaCsv(riga, sep)
                                        If campi.Count > iAic Then
                                            Dim aic9 As String = EstraiAic9(campi(iAic))
                                            If aic9.Length = 9 Then
                                                Dim aic6 As String = aic9.Substring(0, 6)
                                                Dim nome As String = ValoreCampo(campi, iNome)
                                                Dim conf As String = ValoreCampo(campi, iConf)
                                                Dim descrizione As String = conf
                                                If descrizione.Length = 0 Then descrizione = nome

                                                cmd.Parameters("@a9").Value = aic9
                                                cmd.Parameters("@a6").Value = aic6
                                                cmd.Parameters("@d").Value = descrizione
                                                cmd.Parameters("@r").Value = ValoreCampo(campi, iRegime)
                                                cmd.Parameters("@s").Value = ValoreCampo(campi, iStato)
                                                cmd.ExecuteNonQuery()
                                                inserite += 1

                                                If nome.Length > 0 AndAlso Not mappaNome.ContainsKey(aic6) Then mappaNome(aic6) = nome
                                                If iAzienda >= 0 Then
                                                    Dim az As String = ValoreCampo(campi, iAzienda)
                                                    If az.Length > 0 AndAlso Not mappaAzienda.ContainsKey(aic6) Then mappaAzienda(aic6) = az
                                                End If
                                                If scriviSis Then
                                                    Dim sis As String = SoloNumeri(ValoreCampo(campi, iSis))
                                                    If sis.Length > 0 AndAlso Not mappaSis.ContainsKey(aic6) Then mappaSis(aic6) = sis
                                                End If
                                                If scriviAtc Then
                                                    Dim atc As String = ValoreCampo(campi, iAtc)
                                                    If atc.Length > 0 AndAlso Not mappaAtcFarmaco.ContainsKey(aic6) Then mappaAtcFarmaco(aic6) = atc
                                                End If
                                                If iLinkFi >= 0 Then
                                                    Dim lf As String = ValoreCampo(campi, iLinkFi)
                                                    If lf.StartsWith("http", StringComparison.OrdinalIgnoreCase) AndAlso Not mappaLinkFi.ContainsKey(aic6) Then mappaLinkFi(aic6) = lf
                                                End If
                                                If iLinkRcp >= 0 Then
                                                    Dim lr As String = ValoreCampo(campi, iLinkRcp)
                                                    If lr.StartsWith("http", StringComparison.OrdinalIgnoreCase) AndAlso Not mappaLinkRcp.ContainsKey(aic6) Then mappaLinkRcp(aic6) = lr
                                                End If

                                                If inserite Mod 20000 = 0 Then
                                                    Segnala(avanzamento, "Importate " & inserite.ToString() & " confezioni")
                                                End If
                                            End If
                                        End If
                                    End If
                                Catch
                                End Try
                                riga = lettore.ReadLine()
                            End While

                            trans.Commit()
                            SalvaCacheAnagrafica(mappaNome, mappaAzienda, mappaSis, mappaAtcFarmaco)
                            _cacheLinkFi = mappaLinkFi
                            _cacheLinkRcp = mappaLinkRcp
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ImportaConfezioni: " & ex.ToString())
        End Try
        Return inserite
    End Function

    Private _cacheNome As Dictionary(Of String, String)
    Private _cacheAzienda As Dictionary(Of String, String)
    Private _cacheSis As Dictionary(Of String, String)
    Private _cacheAtc As Dictionary(Of String, String)
    Private _cacheLinkFi As Dictionary(Of String, String)
    Private _cacheLinkRcp As Dictionary(Of String, String)

    Private Sub SalvaCacheAnagrafica(nome As Dictionary(Of String, String), azienda As Dictionary(Of String, String), sis As Dictionary(Of String, String), atc As Dictionary(Of String, String))
        Try
            _cacheNome = nome
            _cacheAzienda = azienda
            _cacheSis = sis
            _cacheAtc = atc
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.SalvaCacheAnagrafica: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiPrincipiAttivi(percorso As String) As Dictionary(Of String, String)
        Dim mappa As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not File.Exists(percorso) Then Return mappa
            Using lettore As New StreamReader(percorso, RilevaEncodingFile(percorso))
                Dim sep As Char = ";"c
                Dim intestazione As List(Of String) = Nothing
                If Not TrovaIntestazione(lettore, sep, intestazione, "codice_aic", "AIC") Then Return mappa
                Dim iAic As Integer = IndiceColonna(intestazione, "codice_aic", "AIC")
                Dim iPa As Integer = IndiceColonna(intestazione, "principio_attivo", "principio", "descrizione_pa")
                If iAic < 0 OrElse iPa < 0 Then Return mappa

                Dim riga As String = lettore.ReadLine()
                While riga IsNot Nothing
                    Try
                        If Not String.IsNullOrWhiteSpace(riga) Then
                            Dim campi = DividiRigaCsv(riga, sep)
                            If campi.Count > iAic AndAlso campi.Count > iPa Then
                                Dim aic6 As String = EstraiAic6(campi(iAic))
                                Dim pa As String = ValoreCampo(campi, iPa)
                                If aic6.Length = 6 AndAlso pa.Length > 0 AndAlso Not mappa.ContainsKey(aic6) Then
                                    mappa(aic6) = pa
                                End If
                            End If
                        End If
                    Catch
                    End Try
                    riga = lettore.ReadLine()
                End While
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.LeggiPrincipiAttivi: " & ex.ToString())
        End Try
        Return mappa
    End Function

    Private Function LeggiDescrizioniAtc(percorso As String) As Dictionary(Of String, String)
        Dim mappa As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not File.Exists(percorso) Then Return mappa
            Using lettore As New StreamReader(percorso, RilevaEncodingFile(percorso))
                Dim riga As String = lettore.ReadLine()
                While riga IsNot Nothing
                    Try
                        If Not String.IsNullOrWhiteSpace(riga) Then
                            Dim campi = DividiRigaCsv(riga, ";"c)
                            If campi.Count >= 2 Then
                                Dim codice As String = campi(0).Trim().Trim(""""c).ToUpperInvariant()
                                Dim descrizione As String = campi(1).Trim().Trim(""""c)
                                If codice.Length > 0 AndAlso descrizione.Length > 0 AndAlso Not mappa.ContainsKey(codice) Then
                                    mappa(codice) = descrizione
                                End If
                            End If
                        End If
                    Catch
                    End Try
                    riga = lettore.ReadLine()
                End While
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.LeggiDescrizioniAtc: " & ex.ToString())
        End Try
        Return mappa
    End Function

    Private Function ConsolidaFarmaci(principi As Dictionary(Of String, String), descrizioniAtc As Dictionary(Of String, String), avanzamento As Avanzamento) As Integer
        Dim creati As Integer = 0
        Try
            Dim nomi As Dictionary(Of String, String) = _cacheNome
            Dim aziende As Dictionary(Of String, String) = _cacheAzienda
            Dim sisPerFarmaco As Dictionary(Of String, String) = _cacheSis
            Dim atcPerFarmaco As Dictionary(Of String, String) = _cacheAtc

            If nomi Is Nothing Then nomi = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If aziende Is Nothing Then aziende = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If sisPerFarmaco Is Nothing Then sisPerFarmaco = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If atcPerFarmaco Is Nothing Then atcPerFarmaco = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            Dim sisEsistenti As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Try
                SyncLock _lockDb
                    Dim connLettura = Connessione()
                    If connLettura IsNot Nothing Then
                        Using cmdSis As New SQLiteCommand("SELECT Aic6, CodiceSis FROM Farmaci WHERE CodiceSis IS NOT NULL AND LENGTH(CodiceSis)>0", connLettura)
                            Using rdr = cmdSis.ExecuteReader()
                                While rdr.Read()
                                    Dim k As String = rdr(0).ToString()
                                    If Not sisEsistenti.ContainsKey(k) Then sisEsistenti(k) = rdr(1).ToString()
                                End While
                            End Using
                        End Using
                    End If
                End SyncLock
            Catch exSis As Exception
                _logger.ScriviLog("CLSFarmacie.ConsolidaFarmaci lettura SIS: " & exSis.Message)
            End Try

            Dim aggregati As New Dictionary(Of String, Double())(StringComparer.OrdinalIgnoreCase)
            Dim regimi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim stati As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim descrizioni As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            Using conn = ConnessioneNuova()
                If conn Is Nothing Then Return 0
                Using cmd As New SQLiteCommand("SELECT Aic6, Descrizione, RegimeFornitura, Stato FROM Confezioni", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim aic6 As String = rdr(0).ToString()
                                If aic6.Length <> 6 Then Continue While
                                Dim descrizione As String = rdr(1).ToString()

                                If Not aggregati.ContainsKey(aic6) Then
                                    aggregati(aic6) = New Double() {0, 0, 0}
                                End If
                                Dim valori = aggregati(aic6)
                                valori(2) = valori(2) + 1

                                If Not descrizioni.ContainsKey(aic6) AndAlso descrizione.Length > 0 Then descrizioni(aic6) = descrizione
                                If Not regimi.ContainsKey(aic6) Then regimi(aic6) = rdr(2).ToString()
                                If Not stati.ContainsKey(aic6) Then stati(aic6) = rdr(3).ToString()
                            Catch
                            End Try
                        End While
                    End Using
                End Using

                Using trans = conn.BeginTransaction()
                    Using cmdPulisci As New SQLiteCommand("DELETE FROM Farmaci;", conn, trans)
                        cmdPulisci.ExecuteNonQuery()
                    End Using

                    Using cmd As New SQLiteCommand(
                        "INSERT OR IGNORE INTO Farmaci (Aic6, Nome, Chiave, PrincipioAttivo, Azienda, CodiceSis, Atc, AtcDescrizione, Stato, RegimeFornitura, NumeroConfezioni, Carente, NoteCarenza, LinkFi, LinkRcp, DataAggiornamento) " &
                        "VALUES (@a6,@n,@k,@pa,@az,@sis,@atc,@atcd,@st,@rf,@nc,0,'',@lfi,@lrcp,@dt)", conn, trans)

                        cmd.Parameters.Add("@a6", DbType.String)
                        cmd.Parameters.Add("@n", DbType.String)
                        cmd.Parameters.Add("@k", DbType.String)
                        cmd.Parameters.Add("@pa", DbType.String)
                        cmd.Parameters.Add("@az", DbType.String)
                        cmd.Parameters.Add("@sis", DbType.String)
                        cmd.Parameters.Add("@atc", DbType.String)
                        cmd.Parameters.Add("@atcd", DbType.String)
                        cmd.Parameters.Add("@st", DbType.String)
                        cmd.Parameters.Add("@rf", DbType.String)
                        cmd.Parameters.Add("@nc", DbType.Int32)
                        cmd.Parameters.Add("@lfi", DbType.String)
                        cmd.Parameters.Add("@lrcp", DbType.String)
                        cmd.Parameters.Add("@dt", DbType.String)

                        Dim dataOra As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

                        For Each coppia In aggregati
                            Try
                                Dim aic6 As String = coppia.Key
                                Dim valori = coppia.Value

                                Dim nome As String = ""
                                If nomi.ContainsKey(aic6) Then nome = nomi(aic6)
                                If nome.Length = 0 AndAlso descrizioni.ContainsKey(aic6) Then nome = descrizioni(aic6)
                                If nome.Length = 0 Then Continue For
                                nome = PulisciNomeFarmaco(nome)

                                Dim principio As String = ""
                                If principi.ContainsKey(aic6) Then principio = principi(aic6)

                                Dim azienda As String = ""
                                If aziende.ContainsKey(aic6) Then azienda = aziende(aic6)

                                Dim sis As String = ""
                                If sisPerFarmaco.ContainsKey(aic6) Then sis = sisPerFarmaco(aic6)
                                If sis.Length = 0 AndAlso sisEsistenti.ContainsKey(aic6) Then sis = sisEsistenti(aic6)

                                Dim atc As String = ""
                                If atcPerFarmaco.ContainsKey(aic6) Then atc = atcPerFarmaco(aic6)
                                Dim atcDescrizione As String = ""
                                If atc.Length > 0 AndAlso descrizioniAtc.ContainsKey(atc.ToUpperInvariant()) Then
                                    atcDescrizione = descrizioniAtc(atc.ToUpperInvariant())
                                End If

                                cmd.Parameters("@a6").Value = aic6
                                cmd.Parameters("@n").Value = nome
                                cmd.Parameters("@k").Value = Normalizza(nome & " " & principio & " " & azienda & " " & atc)
                                cmd.Parameters("@pa").Value = principio
                                cmd.Parameters("@az").Value = azienda
                                cmd.Parameters("@sis").Value = sis
                                cmd.Parameters("@atc").Value = atc
                                cmd.Parameters("@atcd").Value = atcDescrizione
                                cmd.Parameters("@st").Value = If(stati.ContainsKey(aic6), stati(aic6), "")
                                cmd.Parameters("@rf").Value = If(regimi.ContainsKey(aic6), regimi(aic6), "")
                                cmd.Parameters("@nc").Value = CInt(valori(2))
                                cmd.Parameters("@lfi").Value = If(_cacheLinkFi IsNot Nothing AndAlso _cacheLinkFi.ContainsKey(aic6), _cacheLinkFi(aic6), "")
                                cmd.Parameters("@lrcp").Value = If(_cacheLinkRcp IsNot Nothing AndAlso _cacheLinkRcp.ContainsKey(aic6), _cacheLinkRcp(aic6), "")
                                cmd.Parameters("@dt").Value = dataOra
                                cmd.ExecuteNonQuery()
                                creati += 1

                                If creati Mod 5000 = 0 Then
                                    Segnala(avanzamento, "Registrazione dei farmaci in corso")
                                End If
                            Catch
                            End Try
                        Next

                        trans.Commit()
                    End Using
                End Using
            End Using

            _cacheNome = Nothing
            _cacheAzienda = Nothing
            _cacheSis = Nothing
            _cacheAtc = Nothing
            _cacheLinkFi = Nothing
            _cacheLinkRcp = Nothing
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ConsolidaFarmaci: " & ex.ToString())
        End Try
        Return creati
    End Function

    Private Function PulisciNomeFarmaco(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim risultato As String = nome.Trim().Trim(""""c).Trim()
            risultato = Regex.Replace(risultato, "\s{2,}", " ")
            If risultato.Length > 120 Then risultato = risultato.Substring(0, 120).Trim()
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.PulisciNomeFarmaco: " & ex.Message)
            Return If(nome, "")
        End Try
    End Function

#End Region

#Region "Carenze"

    Private Function ImportaCarenze(percorso As String, avanzamento As Avanzamento) As Integer
        Dim segnati As Integer = 0
        Try
            If Not File.Exists(percorso) Then Return 0

            Using conn = ConnessioneNuova()
                If conn Is Nothing Then Return 0
                Using trans = conn.BeginTransaction()
                    Using cmdReset As New SQLiteCommand("UPDATE Farmaci SET Carente=0, NoteCarenza=''", conn, trans)
                        cmdReset.CommandTimeout = 300
                        cmdReset.ExecuteNonQuery()
                    End Using

                    Using cmd As New SQLiteCommand("UPDATE Farmaci SET Carente=1, NoteCarenza=@n WHERE Aic6=@a", conn, trans)
                        cmd.Parameters.Add("@n", DbType.String)
                        cmd.Parameters.Add("@a", DbType.String)

                        Using lettore As New StreamReader(percorso, RilevaEncodingFile(percorso))
                            Dim sep As Char = ";"c
                            Dim intestazione As List(Of String) = Nothing
                            If Not TrovaIntestazione(lettore, sep, intestazione, "codice aic", "codice_aic") Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim iAic As Integer = IndiceColonna(intestazione, "codice aic", "codice_aic")
                            Dim iMotivo As Integer = IndiceColonna(intestazione, "motivazioni", "motivazione")
                            Dim iFine As Integer = IndiceColonna(intestazione, "fine presunta", "fine_presunta")
                            If iAic < 0 Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim riga As String = lettore.ReadLine()
                            While riga IsNot Nothing
                                Try
                                    If Not String.IsNullOrWhiteSpace(riga) Then
                                        Dim campi = DividiRigaCsv(riga, sep)
                                        If campi.Count > iAic Then
                                            Dim aic6 As String = EstraiAic6(campi(iAic))
                                            If aic6.Length = 6 Then
                                                Dim nota As New StringBuilder()
                                                If iMotivo >= 0 Then nota.Append(ValoreCampo(campi, iMotivo))
                                                If iFine >= 0 Then
                                                    Dim fine As String = ValoreCampo(campi, iFine)
                                                    If fine.Length > 0 Then
                                                        If nota.Length > 0 Then nota.Append(", ")
                                                        nota.Append("fine presunta ").Append(fine)
                                                    End If
                                                End If
                                                cmd.Parameters("@n").Value = nota.ToString()
                                                cmd.Parameters("@a").Value = aic6
                                                segnati += cmd.ExecuteNonQuery()
                                            End If
                                        End If
                                    End If
                                Catch
                                End Try
                                riga = lettore.ReadLine()
                            End While
                        End Using
                    End Using
                    trans.Commit()
                End Using
            End Using
            Segnala(avanzamento, "Registrati " & segnati.ToString() & " farmaci carenti")
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ImportaCarenze: " & ex.ToString())
        End Try
        Return segnati
    End Function

#End Region

#Region "Ricerca farmaci"

    Public Function NumeroFarmaci() As Integer
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return 0
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Farmaci", conn)
                    Dim o = cmd.ExecuteScalar()
                    If o Is Nothing OrElse IsDBNull(o) Then Return 0
                    Return Convert.ToInt32(o)
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.NumeroFarmaci: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function Cerca(testo As String, Optional massimo As Integer = 400) As List(Of Farmaco)
        Dim risultato As New List(Of Farmaco)
        Dim gia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return risultato
            Dim termine As String = testo.Trim()
            Dim normalizzato As String = Normalizza(termine)
            Dim numerico As String = SoloNumeri(termine)

            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return risultato

                If numerico.Length >= 6 Then
                    Dim aic6 As String = numerico.Substring(0, 6)
                    Using cmd As New SQLiteCommand(SqlSelezioneFarmaco() & " WHERE Aic6=@a LIMIT 1", conn)
                        cmd.Parameters.AddWithValue("@a", aic6)
                        Using rdr = cmd.ExecuteReader()
                            While rdr.Read()
                                AggiungiRisultato(risultato, gia, LeggiFarmaco(rdr))
                            End While
                        End Using
                    End Using
                End If

                If risultato.Count < massimo Then
                    Using cmd As New SQLiteCommand(SqlSelezioneFarmaco() & " WHERE Nome LIKE @p ORDER BY LENGTH(Nome), Nome LIMIT @m", conn)
                        cmd.Parameters.AddWithValue("@p", termine & "%")
                        cmd.Parameters.AddWithValue("@m", massimo)
                        Using rdr = cmd.ExecuteReader()
                            While rdr.Read()
                                AggiungiRisultato(risultato, gia, LeggiFarmaco(rdr))
                            End While
                        End Using
                    End Using
                End If

                If risultato.Count < massimo Then
                    Using cmd As New SQLiteCommand(SqlSelezioneFarmaco() & " WHERE PrincipioAttivo LIKE @p ORDER BY Nome LIMIT @m", conn)
                        cmd.Parameters.AddWithValue("@p", termine & "%")
                        cmd.Parameters.AddWithValue("@m", massimo)
                        Using rdr = cmd.ExecuteReader()
                            While rdr.Read()
                                AggiungiRisultato(risultato, gia, LeggiFarmaco(rdr))
                            End While
                        End Using
                    End Using
                End If

                If risultato.Count < massimo Then
                    Using cmd As New SQLiteCommand(SqlSelezioneFarmaco() & " WHERE Chiave LIKE @c ORDER BY Nome LIMIT @m", conn)
                        cmd.Parameters.AddWithValue("@c", "%" & normalizzato & "%")
                        cmd.Parameters.AddWithValue("@m", massimo)
                        Using rdr = cmd.ExecuteReader()
                            While rdr.Read()
                                AggiungiRisultato(risultato, gia, LeggiFarmaco(rdr))
                            End While
                        End Using
                    End Using
                End If
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Cerca: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Sub AggiungiRisultato(elenco As List(Of Farmaco), gia As HashSet(Of String), f As Farmaco)
        Try
            If f Is Nothing OrElse String.IsNullOrWhiteSpace(f.Aic6) Then Return
            If gia.Contains(f.Aic6) Then Return
            gia.Add(f.Aic6)
            elenco.Add(f)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AggiungiRisultato: " & ex.Message)
        End Try
    End Sub

    Private Function SqlSelezioneFarmaco() As String
        Return "SELECT Aic6, Nome, PrincipioAttivo, Azienda, CodiceSis, Atc, AtcDescrizione, Stato, RegimeFornitura, NumeroConfezioni, Carente, NoteCarenza FROM Farmaci"
    End Function

    Private Function LeggiFarmaco(rdr As SQLiteDataReader) As Farmaco
        Try
            Dim f As New Farmaco()
            f.Aic6 = rdr(0).ToString()
            f.Nome = rdr(1).ToString()
            f.PrincipioAttivo = rdr(2).ToString()
            f.Azienda = rdr(3).ToString()
            f.CodiceSis = rdr(4).ToString()
            f.Atc = rdr(5).ToString()
            f.AtcDescrizione = rdr(6).ToString()
            f.Stato = rdr(7).ToString()
            f.StatoLeggibile = TestoStatoAic(f.Stato)
            f.RegimeFornitura = rdr(8).ToString()
            f.RegimeFornituraLeggibile = TestoRegimeFornitura(f.RegimeFornitura)
            If Not IsDBNull(rdr(9)) Then f.NumeroConfezioni = Convert.ToInt32(rdr(9))
            If Not IsDBNull(rdr(10)) Then f.Carente = (Convert.ToInt32(rdr(10)) = 1)
            f.NoteCarenza = rdr(11).ToString()
            Return f
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.LeggiFarmaco: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function TrovaPerAic6(aic6 As String) As Farmaco
        Try
            Dim codice As String = EstraiAic6(aic6)
            If codice.Length <> 6 Then Return Nothing
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return Nothing
                Using cmd As New SQLiteCommand(SqlSelezioneFarmaco() & " WHERE Aic6=@a LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    Using rdr = cmd.ExecuteReader()
                        If rdr.Read() Then Return LeggiFarmaco(rdr)
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.TrovaPerAic6: " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Public Function OttieniConfezioni(aic6 As String) As List(Of Confezione)
        Dim elenco As New List(Of Confezione)
        Try
            Dim codice As String = EstraiAic6(aic6)
            If codice.Length <> 6 Then Return elenco
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT Aic9, Aic6, Descrizione, RegimeFornitura, Stato FROM Confezioni WHERE Aic6=@a ORDER BY Descrizione", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim c As New Confezione()
                                c.Aic9 = rdr(0).ToString()
                                c.Aic6 = rdr(1).ToString()
                                c.Descrizione = rdr(2).ToString()
                                c.DescrizioneLeggibile = TestoConfezioneLeggibile(c.Descrizione)
                                c.RegimeFornitura = rdr(3).ToString()
                                c.RegimeFornituraLeggibile = TestoRegimeFornitura(c.RegimeFornitura)
                                c.Stato = rdr(4).ToString()
                                elenco.Add(c)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniConfezioni: " & ex.ToString())
        End Try
        Return elenco
    End Function

#End Region

#Region "Stampati foglio illustrativo e RCP"

    Public Function HaStampatoConTesto(aic6 As String, tipo As String) As Boolean
        Try
            Dim codice As String = EstraiAic6(aic6)
            Dim t As String = If(tipo, "FI").ToUpperInvariant()
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return False
                Using cmd As New SQLiteCommand("SELECT 1 FROM Stampati WHERE Aic6=@a AND Tipo=@t AND TestoEstratto IS NOT NULL AND LENGTH(TestoEstratto)>80 LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@t", t)
                    Return cmd.ExecuteScalar() IsNot Nothing
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.HaStampatoConTesto: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniTestoStampato(aic6 As String, tipo As String) As String
        Try
            Dim codice As String = EstraiAic6(aic6)
            Dim t As String = If(tipo, "FI").ToUpperInvariant()
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return ""
                Using cmd As New SQLiteCommand("SELECT TestoEstratto FROM Stampati WHERE Aic6=@a AND Tipo=@t LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@t", t)
                    Dim o = cmd.ExecuteScalar()
                    If o IsNot Nothing AndAlso Not IsDBNull(o) Then Return o.ToString()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniTestoStampato: " & ex.ToString())
        End Try
        Return ""
    End Function

    Public Class SezioneFoglioIllustrativo
        Public Property Numero As Integer
        Public Property Titolo As String
        Public Property Testo As String
    End Class

    Private Shared ReadOnly _titoliSezioniFi As String() = {
        "Che cos'e' e a cosa serve",
        "Cosa deve sapere prima di prenderlo",
        "Come si prende (posologia)",
        "Possibili effetti indesiderati",
        "Come conservarlo",
        "Contenuto della confezione e altre informazioni"
    }

    Private Shared ReadOnly _patternSezioneFi1 As New Regex("^\s*1\D{0,3}CHE\s*COS", RegexOptions.IgnoreCase)
    Private Shared ReadOnly _patternSezioneFi2 As New Regex("^\s*2\D{0,3}COSA\s*DEVE\s*SAPERE", RegexOptions.IgnoreCase)
    Private Shared ReadOnly _patternSezioneFi3 As New Regex("^\s*3\D{0,3}COME\s*(PRENDERE|USARE)", RegexOptions.IgnoreCase)
    Private Shared ReadOnly _patternSezioneFi4 As New Regex("^\s*4\D{0,3}POSSIBILI\s*EFFETTI", RegexOptions.IgnoreCase)
    Private Shared ReadOnly _patternSezioneFi5 As New Regex("^\s*5\D{0,3}COME\s*CONSERVARE", RegexOptions.IgnoreCase)
    Private Shared ReadOnly _patternSezioneFi6 As New Regex("^\s*6\D{0,3}CONTENUTO\s*DELLA\s*CONFEZIONE", RegexOptions.IgnoreCase)

    Private Function RilevaNumeroSezioneFi(riga As String) As Integer
        Try
            If _patternSezioneFi1.IsMatch(riga) Then Return 1
            If _patternSezioneFi2.IsMatch(riga) Then Return 2
            If _patternSezioneFi3.IsMatch(riga) Then Return 3
            If _patternSezioneFi4.IsMatch(riga) Then Return 4
            If _patternSezioneFi5.IsMatch(riga) Then Return 5
            If _patternSezioneFi6.IsMatch(riga) Then Return 6
            Return 0
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Suddivide il testo del foglio illustrativo nelle sei sezioni numerate previste
    ''' per legge dal template QRD europeo (D.Lgs 219/2006), sempre nello stesso ordine
    ''' in tutti i foglietti illustrativi italiani dal 2013 in poi.
    ''' </summary>
    Public Function EstraiSezioniFoglioIllustrativo(testoFoglio As String) As List(Of SezioneFoglioIllustrativo)
        Dim sezioni As New List(Of SezioneFoglioIllustrativo)
        Try
            If String.IsNullOrWhiteSpace(testoFoglio) Then Return sezioni

            Dim righe() As String = testoFoglio.Replace(vbCrLf, vbLf).Split(vbLf(0))
            Dim indiciInizio As New Dictionary(Of Integer, Integer)
            Dim ultimoTrovato As Integer = 0

            For i As Integer = 0 To righe.Length - 1
                Try
                    Dim riga As String = righe(i).Trim()
                    If riga.Length = 0 OrElse riga.Length > 120 Then Continue For

                    Dim numeroRilevato As Integer = RilevaNumeroSezioneFi(riga)
                    If numeroRilevato = ultimoTrovato + 1 Then
                        indiciInizio(numeroRilevato) = i
                        ultimoTrovato = numeroRilevato
                    End If
                Catch
                End Try
            Next

            If indiciInizio.Count = 0 Then Return sezioni

            Dim numeriOrdinati = indiciInizio.Keys.OrderBy(Function(n) n).ToList()
            For idx As Integer = 0 To numeriOrdinati.Count - 1
                Try
                    Dim numero As Integer = numeriOrdinati(idx)
                    Dim rigaInizio As Integer = indiciInizio(numero) + 1
                    Dim rigaFine As Integer = If(idx < numeriOrdinati.Count - 1, indiciInizio(numeriOrdinati(idx + 1)), righe.Length)

                    Dim corpo As New StringBuilder()
                    For r As Integer = rigaInizio To rigaFine - 1
                        Dim testoRiga As String = righe(r).Trim()
                        If testoRiga.Length > 0 Then
                            If corpo.Length > 0 Then corpo.AppendLine()
                            corpo.Append(testoRiga)
                        End If
                    Next

                    Dim s As New SezioneFoglioIllustrativo()
                    s.Numero = numero
                    s.Titolo = If(numero >= 1 AndAlso numero <= 6, _titoliSezioniFi(numero - 1), "Sezione " & numero.ToString())
                    s.Testo = corpo.ToString().Trim()
                    If s.Testo.Length > 0 Then sezioni.Add(s)
                Catch exSezione As Exception
                    _logger.ScriviLog("CLSFarmacie.EstraiSezioniFoglioIllustrativo sezione: " & exSezione.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EstraiSezioniFoglioIllustrativo: " & ex.ToString())
        End Try
        Return sezioni
    End Function

    Public Function OttieniPercorsoPdf(aic6 As String, tipo As String) As String
        Try
            Dim codice As String = EstraiAic6(aic6)
            Dim t As String = If(tipo, "FI").ToUpperInvariant()
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return ""
                Using cmd As New SQLiteCommand("SELECT PdfPath FROM Stampati WHERE Aic6=@a AND Tipo=@t LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@t", t)
                    Dim o = cmd.ExecuteScalar()
                    If o IsNot Nothing AndAlso Not IsDBNull(o) Then
                        Dim p As String = o.ToString()
                        If File.Exists(p) Then Return p
                    End If
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniPercorsoPdf: " & ex.ToString())
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' Cerca il foglio illustrativo; se non e' disponibile (non ancora digitalizzato da AIFA,
    ''' farmaco di uso ospedaliero, ecc.) prova come ripiego il riassunto delle caratteristiche
    ''' del prodotto (RCP), che contiene le stesse informazioni di base in forma piu' tecnica.
    ''' </summary>
    Public Function AssicuraFoglioIllustrativoOFallback(aic6 As String, ByRef tipoOttenuto As String, Optional avanzamento As Avanzamento = Nothing) As Boolean
        Try
            tipoOttenuto = "FI"
            If AssicuraStampato(aic6, "FI", avanzamento) Then Return True

            Segnala(avanzamento, "Foglio illustrativo non disponibile, provo il documento tecnico RCP")
            tipoOttenuto = "RCP"
            Return AssicuraStampato(aic6, "RCP", avanzamento)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AssicuraFoglioIllustrativoOFallback: " & ex.ToString())
            tipoOttenuto = "FI"
            Return False
        End Try
    End Function

    Public Function AssicuraStampato(aic6 As String, Optional tipo As String = "FI", Optional avanzamento As Avanzamento = Nothing) As Boolean
        Try
            Dim t As String = If(tipo, "FI").ToUpperInvariant()
            If t <> "FI" AndAlso t <> "RCP" Then
                Return False
            End If

            Dim codice As String = EstraiAic6(aic6)
            If codice.Length <> 6 Then Return False

            If HaStampatoConTesto(codice, t) Then Return True

            Segnala(avanzamento, "Cerco il documento sui sistemi AIFA")

            Dim farmaco = TrovaPerAic6(codice)
            Dim listaSis As New List(Of String)

            If farmaco IsNot Nothing Then
                Dim sisLocale As String = SoloNumeri(farmaco.CodiceSis)
                If sisLocale.Length > 0 Then listaSis.Add(sisLocale)
            End If

            Dim meta = CercaMetaApi(codice)
            If meta IsNot Nothing Then
                Dim sisRemoto As String = SoloNumeri(meta.Item1)
                If sisRemoto.Length > 0 AndAlso Not listaSis.Contains(sisRemoto) Then listaSis.Add(sisRemoto)
                If meta.Item2.Length = 6 AndAlso meta.Item2 <> codice Then codice = meta.Item2
            End If

            If listaSis.Count = 0 Then
                Return False
            End If

            Segnala(avanzamento, "Scarico il documento in formato PDF")

            Dim pdfBytes As Byte() = Nothing
            Dim sisUsato As String = ""
            For Each sis In listaSis
                pdfBytes = ScaricaStampatoApi(sis, codice, t)
                If pdfBytes IsNot Nothing Then
                    sisUsato = sis
                    Exit For
                End If
            Next

            If pdfBytes Is Nothing OrElse pdfBytes.Length < 500 Then
                Return False
            End If

            Dim nomeFile As String = codice & "_" & t & ".pdf"
            Dim percorsoPdf As String = Path.Combine(_cartellaPdf, nomeFile)
            File.WriteAllBytes(percorsoPdf, pdfBytes)

            Segnala(avanzamento, "Estraggo il testo dal documento")
            Dim testo As String = EstraiTestoPdf(percorsoPdf, avanzamento)

            SyncLock _lockDb
                Dim conn = Connessione()
                If conn IsNot Nothing Then
                    Using cmd As New SQLiteCommand(
                        "INSERT OR REPLACE INTO Stampati (Aic6, Tipo, CodiceSis, PdfPath, TestoEstratto, DataDownload) VALUES (@a,@t,@s,@p,@tx,@d)", conn)
                        cmd.Parameters.AddWithValue("@a", codice)
                        cmd.Parameters.AddWithValue("@t", t)
                        cmd.Parameters.AddWithValue("@s", sisUsato)
                        cmd.Parameters.AddWithValue("@p", percorsoPdf)
                        cmd.Parameters.AddWithValue("@tx", If(testo, ""))
                        cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                        cmd.ExecuteNonQuery()
                    End Using

                    If sisUsato.Length > 0 Then
                        Using cmd2 As New SQLiteCommand("UPDATE Farmaci SET CodiceSis=@s WHERE Aic6=@a AND (CodiceSis IS NULL OR LENGTH(CodiceSis)=0)", conn)
                            cmd2.Parameters.AddWithValue("@s", sisUsato)
                            cmd2.Parameters.AddWithValue("@a", codice)
                            cmd2.ExecuteNonQuery()
                        End Using
                    End If
                End If
            End SyncLock

            Return True
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AssicuraStampato: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function LinkStampatoSalvato(aic6 As String, tipo As String) As String
        Try
            Dim colonna As String = If(String.Equals(tipo, "RCP", StringComparison.OrdinalIgnoreCase), "LinkRcp", "LinkFi")
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return ""
                Using cmd As New SQLiteCommand("SELECT " & colonna & " FROM Farmaci WHERE Aic6=@a", conn)
                    cmd.Parameters.AddWithValue("@a", EstraiAic6(aic6))
                    Dim o = cmd.ExecuteScalar()
                    If o IsNot Nothing AndAlso Not IsDBNull(o) Then Return o.ToString()
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.LinkStampatoSalvato: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Function ScaricaStampatoApi(codiceSis As String, aic6 As String, tipo As String) As Byte()
        Try
            Dim indirizzi As New List(Of String)

            ' Prima scelta: il link esatto pubblicato da AIFA nell'anagrafica, salvato nel database.
            Try
                Dim linkSalvato As String = LinkStampatoSalvato(aic6, tipo)
                If linkSalvato.Length > 0 Then indirizzi.Add(linkSalvato)
            Catch
            End Try

            ' Ricostruzione: AIFA usa il codice farmaco senza zeri iniziali (farmaci/367, non farmaci/000367).
            Dim aicSenzaZeri As String = aic6.TrimStart("0"c)
            If aicSenzaZeri.Length = 0 Then aicSenzaZeri = aic6

            indirizzi.Add(URL_BASE_STAMPATI & "organizzazione/" & codiceSis & "/farmaci/" & aicSenzaZeri & "/stampati?ts=" & tipo)
            indirizzi.Add(URL_BASE_STAMPATI & "organizzazione/" & codiceSis & "/farmaci/" & aic6 & "/stampati?ts=" & tipo)
            indirizzi.Add(URL_BASE_STAMPATI & "organizzazione/" & codiceSis & "/farmaci/" & aicSenzaZeri & "/stampati?tipoStampato=" & tipo)

            For Each url In indirizzi
                Try
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                        Dim risposta = _http.SendAsync(richiesta).GetAwaiter().GetResult()
                        If risposta.IsSuccessStatusCode Then
                            Dim bytes = risposta.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                            If bytes IsNot Nothing AndAlso bytes.Length > 500 Then
                                If bytes(0) = &H25 AndAlso bytes(1) = &H50 AndAlso bytes(2) = &H44 AndAlso bytes(3) = &H46 Then
                                    Return bytes
                                End If
                            End If
                        End If
                    End Using
                Catch
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ScaricaStampatoApi: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function CercaMetaApi(aic6 As String) As Tuple(Of String, String)
        Try
            Dim aic8 As String = aic6.PadLeft(8, "0"c)
            Dim indirizzi As New List(Of String)
            indirizzi.Add(URL_BASE_STAMPATI & "formadosaggio/ricerca?query=" & aic8 & "&spellingCorrection=true&page=0")
            indirizzi.Add(URL_BASE_STAMPATI & "formadosaggio/ricerca?query=" & aic6 & "&spellingCorrection=true&page=0")
            indirizzi.Add(URL_BASE_STAMPATI & "farmaco/ricerca?query=" & aic6 & "&page=0")

            For Each url In indirizzi
                Try
                    Dim json As String = ScaricaTesto(url, 2)
                    If String.IsNullOrWhiteSpace(json) Then Continue For

                    Dim radice As JToken = JToken.Parse(json)
                    Dim sis As String = ""
                    Dim aicTrovato As String = ""
                    CercaChiaviJson(radice, sis, aicTrovato, 0)

                    sis = SoloNumeri(sis)
                    aicTrovato = EstraiAic6(aicTrovato)
                    If aicTrovato.Length <> 6 Then aicTrovato = aic6
                    If sis.Length > 0 Then Return Tuple.Create(sis, aicTrovato)
                Catch
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.CercaMetaApi: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub CercaChiaviJson(token As JToken, ByRef sis As String, ByRef aic As String, profondita As Integer)
        Try
            If token Is Nothing OrElse profondita > 8 Then Return
            If sis.Length > 0 AndAlso aic.Length > 0 Then Return

            If token.Type = JTokenType.Object Then
                For Each prop As JProperty In DirectCast(token, JObject).Properties()
                    Dim nome As String = prop.Name.ToLowerInvariant()
                    If prop.Value IsNot Nothing AndAlso (prop.Value.Type = JTokenType.String OrElse prop.Value.Type = JTokenType.Integer) Then
                        Dim valore As String = prop.Value.ToString().Trim()
                        If sis.Length = 0 AndAlso (nome = "codicesis" OrElse nome = "siscode" OrElse nome = "codice_sis" OrElse nome = "sis") Then
                            sis = valore
                        End If
                        If aic.Length = 0 AndAlso (nome = "aic6" OrElse nome = "aic" OrElse nome = "codiceaic" OrElse nome = "codice_aic") Then
                            aic = valore
                        End If
                    Else
                        CercaChiaviJson(prop.Value, sis, aic, profondita + 1)
                    End If
                Next
            ElseIf token.Type = JTokenType.Array Then
                Dim contatore As Integer = 0
                For Each elemento As JToken In token.Children()
                    CercaChiaviJson(elemento, sis, aic, profondita + 1)
                    contatore += 1
                    If contatore >= 5 Then Exit For
                    If sis.Length > 0 AndAlso aic.Length > 0 Then Exit For
                Next
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.CercaChiaviJson: " & ex.Message)
        End Try
    End Sub

    Public Class StampatoScaricato
        Public Property Aic6 As String
        Public Property Tipo As String
        Public Property NomeFarmaco As String
        Public Property PdfPath As String
        Public Property DataDownload As String
    End Class

    Public Function OttieniStampatiScaricati() As List(Of StampatoScaricato)
        Dim elenco As New List(Of StampatoScaricato)
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand(
                    "SELECT s.Aic6, s.Tipo, s.PdfPath, s.DataDownload, f.Nome FROM Stampati s LEFT JOIN Farmaci f ON f.Aic6 = s.Aic6 ORDER BY s.DataDownload DESC", conn)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim percorso As String = If(IsDBNull(rdr(2)), "", rdr(2).ToString())
                                If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Continue While

                                Dim s As New StampatoScaricato()
                                s.Aic6 = rdr(0).ToString()
                                s.Tipo = rdr(1).ToString()
                                s.PdfPath = percorso
                                s.DataDownload = If(IsDBNull(rdr(3)), "", rdr(3).ToString())
                                Dim nome As String = If(IsDBNull(rdr(4)), "", rdr(4).ToString())
                                s.NomeFarmaco = If(String.IsNullOrWhiteSpace(nome), "AIC " & s.Aic6, nome)
                                elenco.Add(s)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniStampatiScaricati: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Public Function EliminaStampato(aic6 As String, tipo As String) As Boolean
        Try
            Dim codice As String = EstraiAic6(aic6)
            Dim t As String = If(tipo, "FI").ToUpperInvariant()

            Dim percorsoPdf As String = OttieniPercorsoPdf(codice, t)

            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return False
                Using cmd As New SQLiteCommand("DELETE FROM Stampati WHERE Aic6=@a AND Tipo=@t", conn)
                    cmd.Parameters.AddWithValue("@a", codice)
                    cmd.Parameters.AddWithValue("@t", t)
                    cmd.ExecuteNonQuery()
                End Using
            End SyncLock

            Try
                If Not String.IsNullOrWhiteSpace(percorsoPdf) AndAlso File.Exists(percorsoPdf) Then
                    File.Delete(percorsoPdf)
                End If
            Catch exFile As Exception
                _logger.ScriviLog("CLSFarmacie.EliminaStampato file: " & exFile.Message)
            End Try

            Return True
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EliminaStampato: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub EliminaTuttiGliStampati()
        Try
            Dim elenco = OttieniStampatiScaricati()
            For Each s In elenco
                Try
                    EliminaStampato(s.Aic6, s.Tipo)
                Catch exSingolo As Exception
                    _logger.ScriviLog("CLSFarmacie.EliminaTuttiGliStampati elemento: " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EliminaTuttiGliStampati: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Estrazione testo PDF"

    Public Function EstraiTestoPdf(percorsoPdf As String, Optional avanzamento As Avanzamento = Nothing) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then Return ""

            Dim sb As New StringBuilder()
            Using documento = PdfDocument.Open(percorsoPdf)
                For indice As Integer = 1 To documento.NumberOfPages
                    Dim testoPagina As String = ""
                    Try
                        Dim pagina = documento.GetPage(indice)
                        Try
                            testoPagina = ContentOrderTextExtractor.GetText(pagina)
                        Catch
                            testoPagina = pagina.Text
                        End Try
                    Catch exPagina As Exception
                        _logger.ScriviLog("CLSFarmacie.EstraiTestoPdf pagina " & indice.ToString() & ": " & exPagina.Message)
                    End Try

                    If Not String.IsNullOrWhiteSpace(testoPagina) Then
                        testoPagina = PulisciTestoPdf(testoPagina)
                        If sb.Length > 0 Then sb.AppendLine()
                        sb.Append(testoPagina)
                    End If
                Next
            End Using

            Dim testoFinale As String = FormattaPerLettura(sb.ToString())

            If String.IsNullOrWhiteSpace(testoFinale) Then
                Segnala(avanzamento, "Il documento non contiene testo, provo il riconoscimento OCR")
                testoFinale = EstraiTestoPdfConOcr(percorsoPdf)
            End If

            Return testoFinale
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EstraiTestoPdf: " & ex.ToString())
            Try
                Segnala(avanzamento, "Provo comunque il riconoscimento OCR")
                Return EstraiTestoPdfConOcr(percorsoPdf)
            Catch exOcr As Exception
                _logger.ScriviLog("CLSFarmacie.EstraiTestoPdf ocr: " & exOcr.ToString())
                Return ""
            End Try
        End Try
    End Function

    Private Function EstraiTestoPdfConOcr(percorsoPdf As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then Return ""

            Dim ocr As New CLSOcr()
            Dim testoOcr As String = ocr.EstraiTesto(percorsoPdf)
            If String.IsNullOrWhiteSpace(testoOcr) Then Return ""

            Return FormattaPerLettura(testoOcr)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.EstraiTestoPdfConOcr: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function PulisciTestoPdf(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim normalizzato As String = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim sb As New StringBuilder(normalizzato.Length)
            For Each c In normalizzato
                Dim codice As Integer = AscW(c)
                If codice = 9 OrElse codice = 10 OrElse codice >= 32 Then
                    If codice = 160 Then
                        sb.Append(" "c)
                    Else
                        sb.Append(c)
                    End If
                End If
            Next
            Dim risultato As String = sb.ToString()
            risultato = Regex.Replace(risultato, "(\w)-\n(\w)", "$1$2")
            risultato = Regex.Replace(risultato, "[ \t]{2,}", " ")
            risultato = Regex.Replace(risultato, "\n{3,}", vbLf & vbLf)
            Return risultato.Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.PulisciTestoPdf: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Private Function FormattaPerLettura(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim righe = testo.Split(New String() {vbLf}, StringSplitOptions.None)
            Dim sb As New StringBuilder()
            For Each riga In righe
                Dim r As String = riga.Trim()
                If r.Length = 0 Then
                    sb.AppendLine()
                    Continue For
                End If
                If Regex.IsMatch(r, "^\d{1,2}\.\s+\S") OrElse Regex.IsMatch(r, "^\d{1,2}\.\d{1,2}\s+\S") Then
                    If sb.Length > 0 Then sb.AppendLine()
                End If
                sb.AppendLine(r)
            Next
            Dim risultato As String = sb.ToString()
            risultato = Regex.Replace(risultato, "(\r?\n){3,}", vbCrLf & vbCrLf)
            Return risultato.Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.FormattaPerLettura: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

#End Region

#Region "Farmacie anagrafica nazionale"

    Public Function NumeroFarmacie() As Integer
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return 0
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM Farmacie", conn)
                    Dim o = cmd.ExecuteScalar()
                    If o Is Nothing OrElse IsDBNull(o) Then Return 0
                    Return Convert.ToInt32(o)
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.NumeroFarmacie: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function AggiornaElencoFarmacie(Optional avanzamento As Avanzamento = Nothing) As Integer
        Dim inserite As Integer = 0
        Dim temporaneo As String = ""
        Try
            Segnala(avanzamento, "Scarico l'elenco nazionale delle farmacie")
            temporaneo = Path.Combine(_cartellaDati, "farmacie_tmp.csv")

            ' Il file ufficiale cambia nome ogni giorno: FRM_FARMA_5_YYYYMMDD.csv
            Dim candidati As New List(Of String)
            For i As Integer = 0 To 5
                Dim giorno As DateTime = DateTime.Now.Date.AddDays(-i)
                candidati.Add(URL_FARMACIE_BASE & "FRM_FARMA_5_" & giorno.ToString("yyyyMMdd") & ".csv")
            Next
            candidati.Add(URL_FARMACIE_MINISTERO_LEGACY)

            Dim scaricato As Boolean = False
            For Each url In candidati
                If ScaricaFile(url, temporaneo) Then
                    scaricato = True
                    ImpostaMeta("UrlFarmacie", url)
                    Exit For
                End If
            Next

            If Not scaricato Then
                Return 0
            End If

            Segnala(avanzamento, "Importo le farmacie nel database")

            Using conn = ConnessioneNuova()
                If conn Is Nothing Then Return 0
                Using trans = conn.BeginTransaction()
                    Using cmdPulisci As New SQLiteCommand("DELETE FROM Farmacie;", conn, trans)
                        cmdPulisci.ExecuteNonQuery()
                    End Using

                    Using cmd As New SQLiteCommand(
                        "INSERT OR REPLACE INTO Farmacie (Codice, Denominazione, Indirizzo, Cap, Comune, Provincia, Regione, Telefono, Latitudine, Longitudine) VALUES (@c,@d,@i,@cap,@com,@pr,@reg,@tel,@lat,@lon)", conn, trans)

                        cmd.Parameters.Add("@c", DbType.String)
                        cmd.Parameters.Add("@d", DbType.String)
                        cmd.Parameters.Add("@i", DbType.String)
                        cmd.Parameters.Add("@cap", DbType.String)
                        cmd.Parameters.Add("@com", DbType.String)
                        cmd.Parameters.Add("@pr", DbType.String)
                        cmd.Parameters.Add("@reg", DbType.String)
                        cmd.Parameters.Add("@tel", DbType.String)
                        cmd.Parameters.Add("@lat", DbType.Double)
                        cmd.Parameters.Add("@lon", DbType.Double)

                        Using lettore As New StreamReader(temporaneo, Encoding.UTF8, True)
                            Dim sep As Char = ";"c
                            Dim intestazione As List(Of String) = Nothing
                            If Not TrovaIntestazione(lettore, sep, intestazione, "descrizione_farmacia", "descrizione farmacia", "denominazione", "descrizionefarmacia", "comune", "cod_farmacia") Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim iCodice As Integer = IndiceColonna(intestazione, "cod_farmacia", "codice identificativo farmacia", "codiceidentificativo", "codice farmacia", "codice")
                            Dim iNome As Integer = IndiceColonna(intestazione, "descrizione_farmacia", "descrizione farmacia", "denominazione farmacia", "denominazione", "descrizionefarmacia")
                            Dim iIndirizzo As Integer = IndiceColonna(intestazione, "indirizzo", "descrizione indirizzo")
                            Dim iCap As Integer = IndiceColonna(intestazione, "cap")
                            Dim iComune As Integer = IndiceColonna(intestazione, "comune", "descrizione comune")
                            Dim iProvincia As Integer = IndiceColonna(intestazione, "provincia", "descrizione provincia", "sigla provincia", "cod_provincia")
                            Dim iRegione As Integer = IndiceColonna(intestazione, "regione", "descrizione regione")
                            Dim iTelefono As Integer = IndiceColonna(intestazione, "telefono")
                            Dim iLat As Integer = IndiceColonna(intestazione, "latitudine", "lat")
                            Dim iLon As Integer = IndiceColonna(intestazione, "longitudine", "lon", "lng")

                            If iNome < 0 OrElse iComune < 0 Then
                                trans.Rollback()
                                Return 0
                            End If

                            Dim progressivo As Integer = 0
                            Dim riga As String = lettore.ReadLine()
                            While riga IsNot Nothing
                                Try
                                    If Not String.IsNullOrWhiteSpace(riga) Then
                                        Dim campi = DividiRigaCsv(riga, sep)
                                        Dim nome As String = ValoreCampo(campi, iNome)
                                        If nome.Length > 0 Then
                                            progressivo += 1
                                            Dim codice As String = ValoreCampo(campi, iCodice)
                                            If codice.Length = 0 Then codice = "F" & progressivo.ToString()

                                            cmd.Parameters("@c").Value = codice
                                            cmd.Parameters("@d").Value = nome
                                            cmd.Parameters("@i").Value = ValoreCampo(campi, iIndirizzo)
                                            cmd.Parameters("@cap").Value = ValoreCampo(campi, iCap)
                                            cmd.Parameters("@com").Value = ValoreCampo(campi, iComune)
                                            cmd.Parameters("@pr").Value = ValoreCampo(campi, iProvincia)
                                            cmd.Parameters("@reg").Value = ValoreCampo(campi, iRegione)
                                            cmd.Parameters("@tel").Value = ValoreCampo(campi, iTelefono)
                                            cmd.Parameters("@lat").Value = ParseCoordinata(ValoreCampo(campi, iLat))
                                            cmd.Parameters("@lon").Value = ParseCoordinata(ValoreCampo(campi, iLon))
                                            cmd.ExecuteNonQuery()
                                            inserite += 1

                                            If inserite Mod 3000 = 0 Then
                                                Segnala(avanzamento, "Importate " & inserite.ToString() & " farmacie")
                                            End If
                                        End If
                                    End If
                                Catch
                                End Try
                                riga = lettore.ReadLine()
                            End While
                        End Using
                    End Using
                    trans.Commit()
                End Using
            End Using

            ImpostaMeta("UltimoAggiornamentoFarmacie", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            Segnala(avanzamento, "Elenco farmacie aggiornato")
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AggiornaElencoFarmacie: " & ex.ToString())
        Finally
            Try
                If temporaneo.Length > 0 AndAlso File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch
            End Try
        End Try
        Return inserite
    End Function

    Private Function ParseCoordinata(valore As String) As Double
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return 0
            Dim s As String = valore.Trim().Replace(",", ".")
            Dim risultato As Double = 0
            If Double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, risultato) Then
                If risultato < -180 OrElse risultato > 180 Then Return 0
                Return risultato
            End If
            Return 0
        Catch
            Return 0
        End Try
    End Function

    Public Function FarmacieDelComune(comune As String) As List(Of Farmacia)
        Dim elenco As New List(Of Farmacia)
        Try
            If String.IsNullOrWhiteSpace(comune) Then Return elenco
            Dim comuneNormalizzato As String = Normalizza(comune)

            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT Codice, Denominazione, Indirizzo, Cap, Comune, Provincia, Regione, Telefono, Latitudine, Longitudine FROM Farmacie WHERE Comune LIKE @c ORDER BY Denominazione", conn)
                    cmd.Parameters.AddWithValue("@c", comune.Trim() & "%")
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim comuneRiga As String = rdr(4).ToString()
                                If Normalizza(comuneRiga) <> comuneNormalizzato Then Continue While

                                Dim f As New Farmacia()
                                f.Codice = rdr(0).ToString()
                                f.Denominazione = rdr(1).ToString()
                                f.Indirizzo = rdr(2).ToString()
                                f.Cap = rdr(3).ToString()
                                f.Comune = comuneRiga
                                f.Provincia = rdr(5).ToString()
                                f.Regione = rdr(6).ToString()
                                f.Telefono = rdr(7).ToString()
                                If Not IsDBNull(rdr(8)) Then f.Latitudine = Convert.ToDouble(rdr(8), CultureInfo.InvariantCulture)
                                If Not IsDBNull(rdr(9)) Then f.Longitudine = Convert.ToDouble(rdr(9), CultureInfo.InvariantCulture)
                                f.StatoApertura = -1
                                elenco.Add(f)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.FarmacieDelComune: " & ex.ToString())
        End Try
        Return elenco
    End Function

#End Region

#Region "Farmacie aperte con orari"

    Public Function AggiornaFarmacieAperte(zona As String, Optional raggioMetri As Integer = 6000, Optional avanzamento As Avanzamento = Nothing) As Integer
        Try
            If String.IsNullOrWhiteSpace(zona) Then Return 0
            Dim chiaveZona As String = Normalizza(zona)

            Dim ultimo As String = LeggiMeta("OrariAggiornati_" & chiaveZona)
            If ultimo = DateTime.Now.ToString("yyyy-MM-dd") Then
                Return NumeroFarmacieOrari(chiaveZona)
            End If

            Segnala(avanzamento, "Cerco le farmacie di " & zona)
            _avanzamentoCorrenteLuoghi = avanzamento
            Dim attivita As List(Of CLSLuoghi.Attivita) = _luoghi.CercaAttivita(zona, CATEGORIA_FARMACIE, New CLSLuoghi.Avanzamento(AddressOf InoltraAvanzamentoLuoghi))
            If attivita Is Nothing OrElse attivita.Count = 0 Then
                Return NumeroFarmacieOrari(chiaveZona)
            End If

            Segnala(avanzamento, "Registro le farmacie trovate")
            Dim salvate As Integer = SalvaFarmacieDaAttivita(attivita, chiaveZona)
            If salvate > 0 Then
                ImpostaMeta("OrariAggiornati_" & chiaveZona, DateTime.Now.ToString("yyyy-MM-dd"))
            End If
            Segnala(avanzamento, "Orari aggiornati")
            Return salvate
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.AggiornaFarmacieAperte: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function NumeroFarmacieOrari(chiaveZona As String) As Integer
        Try
            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return 0
                Using cmd As New SQLiteCommand("SELECT COUNT(*) FROM FarmacieOrari WHERE Zona=@z", conn)
                    cmd.Parameters.AddWithValue("@z", chiaveZona)
                    Dim o = cmd.ExecuteScalar()
                    If o Is Nothing OrElse IsDBNull(o) Then Return 0
                    Return Convert.ToInt32(o)
                End Using
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.NumeroFarmacieOrari: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function Geolocalizza(zona As String, ByRef latitudine As Double, ByRef longitudine As Double) As Boolean
        Try
            latitudine = 0
            longitudine = 0

            Dim chiave As String = "Coordinate_" & Normalizza(zona)
            Dim memorizzate As String = LeggiMeta(chiave)
            If memorizzate.Length > 0 Then
                Dim parti = memorizzate.Split("|"c)
                If parti.Length >= 2 Then
                    If Double.TryParse(parti(0), NumberStyles.Any, CultureInfo.InvariantCulture, latitudine) AndAlso
                       Double.TryParse(parti(1), NumberStyles.Any, CultureInfo.InvariantCulture, longitudine) Then
                        If latitudine <> 0 AndAlso longitudine <> 0 Then Return True
                    End If
                End If
            End If

            Dim url As String = URL_NOMINATIM & "?format=json&limit=1&countrycodes=it&q=" & Uri.EscapeDataString(zona)
            Dim json As String = ScaricaTesto(url, 2)
            If String.IsNullOrWhiteSpace(json) Then Return False

            Dim radice As JToken = JToken.Parse(json)
            If radice.Type <> JTokenType.Array OrElse Not radice.HasValues Then Return False

            Dim primo As JToken = radice(0)
            Dim latTesto As String = If(primo("lat") Is Nothing, "", primo("lat").ToString())
            Dim lonTesto As String = If(primo("lon") Is Nothing, "", primo("lon").ToString())
            latTesto = latTesto.Replace(",", ".")
            lonTesto = lonTesto.Replace(",", ".")

            If Not Double.TryParse(latTesto, NumberStyles.Any, CultureInfo.InvariantCulture, latitudine) Then Return False
            If Not Double.TryParse(lonTesto, NumberStyles.Any, CultureInfo.InvariantCulture, longitudine) Then Return False

            ImpostaMeta(chiave, latitudine.ToString(CultureInfo.InvariantCulture) & "|" & longitudine.ToString(CultureInfo.InvariantCulture))
            Return True
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Geolocalizza: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function SalvaFarmacieDaAttivita(attivita As List(Of CLSLuoghi.Attivita), chiaveZona As String) As Integer
        Dim salvate As Integer = 0
        Try
            If attivita Is Nothing Then Return 0

            Using conn = ConnessioneNuova()
                If conn Is Nothing Then Return 0
                Using trans = conn.BeginTransaction()
                    Using cmdPulisci As New SQLiteCommand("DELETE FROM FarmacieOrari WHERE Zona=@z", conn, trans)
                        cmdPulisci.Parameters.AddWithValue("@z", chiaveZona)
                        cmdPulisci.ExecuteNonQuery()
                    End Using

                    Using cmd As New SQLiteCommand(
                        "INSERT OR REPLACE INTO FarmacieOrari (IdOsm, Zona, Denominazione, Indirizzo, Comune, Telefono, Orari, Sito, Email, Marca, IdGers, PercorsoTattile, SegnalatoreAcustico, Braille, Corrimano, DescrizionePerNonVedenti, AccessoSediaRotelle, BagnoAccessibile, CaniGuidaAmmessi, Latitudine, Longitudine, DistanzaKm, DataAggiornamento) VALUES (@id,@z,@d,@i,@c,@t,@o,@s,@e,@m,@g,@pt,@sa,@br,@co,@dv,@ar,@ba,@cg,@lat,@lon,@dk,@dt)", conn, trans)

                        cmd.Parameters.Add("@id", DbType.String)
                        cmd.Parameters.Add("@z", DbType.String)
                        cmd.Parameters.Add("@d", DbType.String)
                        cmd.Parameters.Add("@i", DbType.String)
                        cmd.Parameters.Add("@c", DbType.String)
                        cmd.Parameters.Add("@t", DbType.String)
                        cmd.Parameters.Add("@o", DbType.String)
                        cmd.Parameters.Add("@s", DbType.String)
                        cmd.Parameters.Add("@e", DbType.String)
                        cmd.Parameters.Add("@m", DbType.String)
                        cmd.Parameters.Add("@g", DbType.String)
                        cmd.Parameters.Add("@pt", DbType.String)
                        cmd.Parameters.Add("@sa", DbType.String)
                        cmd.Parameters.Add("@br", DbType.String)
                        cmd.Parameters.Add("@co", DbType.String)
                        cmd.Parameters.Add("@dv", DbType.String)
                        cmd.Parameters.Add("@ar", DbType.String)
                        cmd.Parameters.Add("@ba", DbType.String)
                        cmd.Parameters.Add("@cg", DbType.String)
                        cmd.Parameters.Add("@lat", DbType.Double)
                        cmd.Parameters.Add("@lon", DbType.Double)
                        cmd.Parameters.Add("@dk", DbType.Double)
                        cmd.Parameters.Add("@dt", DbType.String)

                        Dim dataOra As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        Dim identificatoriUsati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                        For Each a As CLSLuoghi.Attivita In attivita
                            Try
                                If a Is Nothing OrElse String.IsNullOrWhiteSpace(a.Nome) Then Continue For

                                Dim identificativo As String = ""
                                If Not String.IsNullOrWhiteSpace(a.IdOsm) Then
                                    identificativo = "OSM" & a.IdOsm
                                ElseIf Not String.IsNullOrWhiteSpace(a.IdGers) Then
                                    identificativo = "GERS" & a.IdGers
                                Else
                                    identificativo = "FARM" & Normalizza(a.Nome) & "|" & a.Latitudine.ToString(CultureInfo.InvariantCulture) & "|" & a.Longitudine.ToString(CultureInfo.InvariantCulture)
                                End If
                                If identificatoriUsati.Contains(identificativo) Then Continue For
                                identificatoriUsati.Add(identificativo)

                                cmd.Parameters("@id").Value = identificativo
                                cmd.Parameters("@z").Value = chiaveZona
                                cmd.Parameters("@d").Value = a.Nome
                                cmd.Parameters("@i").Value = If(a.Indirizzo, "")
                                cmd.Parameters("@c").Value = If(a.Comune, "")
                                cmd.Parameters("@t").Value = If(a.Telefono, "")
                                cmd.Parameters("@o").Value = If(a.Orari, "")
                                cmd.Parameters("@s").Value = If(a.Sito, "")
                                cmd.Parameters("@e").Value = If(a.Email, "")
                                cmd.Parameters("@m").Value = If(a.Marca, "")
                                cmd.Parameters("@g").Value = If(a.IdGers, "")
                                cmd.Parameters("@pt").Value = If(a.PercorsoTattile, "")
                                cmd.Parameters("@sa").Value = If(a.SegnalatoreAcustico, "")
                                cmd.Parameters("@br").Value = If(a.Braille, "")
                                cmd.Parameters("@co").Value = If(a.Corrimano, "")
                                cmd.Parameters("@dv").Value = If(a.DescrizionePerNonVedenti, "")
                                cmd.Parameters("@ar").Value = If(a.AccessoSediaRotelle, "")
                                cmd.Parameters("@ba").Value = If(a.BagnoAccessibile, "")
                                cmd.Parameters("@cg").Value = If(a.CaniGuidaAmmessi, "")
                                cmd.Parameters("@lat").Value = a.Latitudine
                                cmd.Parameters("@lon").Value = a.Longitudine
                                cmd.Parameters("@dk").Value = a.DistanzaKm
                                cmd.Parameters("@dt").Value = dataOra
                                cmd.ExecuteNonQuery()
                                salvate += 1
                            Catch exA As Exception
                                _logger.ScriviLog("CLSFarmacie.SalvaFarmacieDaAttivita elemento: " & exA.Message)
                            End Try
                        Next
                    End Using
                    trans.Commit()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.SalvaFarmacieDaAttivita: " & ex.ToString())
        End Try
        Return salvate
    End Function


    Public Function OttieniFarmacieAperte(zona As String, soloAperte As Boolean) As List(Of Farmacia)
        Dim elenco As New List(Of Farmacia)
        Try
            If String.IsNullOrWhiteSpace(zona) Then Return elenco
            Dim chiaveZona As String = Normalizza(zona)
            Dim adesso As DateTime = DateTime.Now

            SyncLock _lockDb
                Dim conn = Connessione()
                If conn Is Nothing Then Return elenco
                Using cmd As New SQLiteCommand("SELECT IdOsm, Denominazione, Indirizzo, Comune, Telefono, Orari, Sito, Email, Marca, IdGers, PercorsoTattile, SegnalatoreAcustico, Braille, Corrimano, DescrizionePerNonVedenti, AccessoSediaRotelle, BagnoAccessibile, CaniGuidaAmmessi, Latitudine, Longitudine, DistanzaKm FROM FarmacieOrari WHERE Zona=@z ORDER BY Denominazione", conn)
                    cmd.Parameters.AddWithValue("@z", chiaveZona)
                    Using rdr = cmd.ExecuteReader()
                        While rdr.Read()
                            Try
                                Dim f As New Farmacia()
                                f.Codice = rdr(0).ToString()
                                f.Denominazione = rdr(1).ToString()
                                f.Indirizzo = rdr(2).ToString()
                                f.Comune = rdr(3).ToString()
                                f.Telefono = rdr(4).ToString()
                                f.Orari = rdr(5).ToString()
                                f.Sito = rdr(6).ToString()
                                f.Email = rdr(7).ToString()
                                f.Marca = rdr(8).ToString()
                                f.IdGers = rdr(9).ToString()
                                f.PercorsoTattile = rdr(10).ToString()
                                f.SegnalatoreAcustico = rdr(11).ToString()
                                f.Braille = rdr(12).ToString()
                                f.Corrimano = rdr(13).ToString()
                                f.DescrizionePerNonVedenti = rdr(14).ToString()
                                f.AccessoSediaRotelle = rdr(15).ToString()
                                f.BagnoAccessibile = rdr(16).ToString()
                                f.CaniGuidaAmmessi = rdr(17).ToString()
                                If Not IsDBNull(rdr(18)) Then f.Latitudine = Convert.ToDouble(rdr(18), CultureInfo.InvariantCulture)
                                If Not IsDBNull(rdr(19)) Then f.Longitudine = Convert.ToDouble(rdr(19), CultureInfo.InvariantCulture)
                                If Not IsDBNull(rdr(20)) Then f.DistanzaKm = Convert.ToDouble(rdr(20), CultureInfo.InvariantCulture)
                                f.StatoApertura = ValutaApertura(f.Orari, adesso)
                                elenco.Add(f)
                            Catch
                            End Try
                        End While
                    End Using
                End Using
            End SyncLock

            elenco.Sort(AddressOf ConfrontaFarmaciePerDistanza)

            If soloAperte Then
                Dim filtrate As New List(Of Farmacia)
                For Each f In elenco
                    If f.StatoApertura = 1 Then filtrate.Add(f)
                Next
                Return filtrate
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniFarmacieAperte: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Private Shared ReadOnly _regexCaratteriNonUtiliFarmacia As New Regex("[^A-Z0-9 ]", RegexOptions.Compiled)
    Private Shared ReadOnly _regexSpaziMultipliFarmacia As New Regex("\s+", RegexOptions.Compiled)

    Private Const DIMENSIONE_CELLA_GRIGLIA_FARMACIE As Double = 0.01

    Private Function NomeFarmaciaConfrontabile(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim n As String = Normalizza(nome)
            n = n.Replace("FARMACIA COMUNALE", " ").Replace("FARMACIA", " ")
            n = n.Replace("DOTT.SSA", " ").Replace("DOTTORESSA", " ").Replace("DOTT.", " ").Replace("DOTTOR", " ")
            n = n.Replace("DR.SSA", " ").Replace("DR.", " ")
            n = _regexCaratteriNonUtiliFarmacia.Replace(n, " ")
            n = _regexSpaziMultipliFarmacia.Replace(n, " ").Trim()
            Return n
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.NomeFarmaciaConfrontabile: " & ex.Message)
            Return Normalizza(nome)
        End Try
    End Function

    Private Function SonoStessaFarmacia(a As Farmacia, b As Farmacia, Optional nomeAConfrontabile As String = Nothing, Optional nomeBConfrontabile As String = Nothing) As Boolean
        Try
            If a Is Nothing OrElse b Is Nothing Then Return False

            If a.Latitudine <> 0 AndAlso a.Longitudine <> 0 AndAlso b.Latitudine <> 0 AndAlso b.Longitudine <> 0 Then
                Dim distanza As Double = DistanzaChilometri(a.Latitudine, a.Longitudine, b.Latitudine, b.Longitudine)
                If distanza <= 0.1 Then Return True
            End If

            Dim nomeA As String = If(nomeAConfrontabile, NomeFarmaciaConfrontabile(a.Denominazione))
            Dim nomeB As String = If(nomeBConfrontabile, NomeFarmaciaConfrontabile(b.Denominazione))
            If nomeA.Length >= 3 AndAlso nomeB.Length >= 3 Then
                If nomeA = nomeB Then Return True
                If nomeA.Contains(nomeB) OrElse nomeB.Contains(nomeA) Then Return True
            End If

            Return False
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.SonoStessaFarmacia: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ChiaveCellaFarmacia(riga As Integer, colonna As Integer) As String
        Try
            Return riga.ToString(CultureInfo.InvariantCulture) & "_" & colonna.ToString(CultureInfo.InvariantCulture)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ChiaveCellaFarmacia: " & ex.Message)
            Return "0_0"
        End Try
    End Function

    Private Function RigaCellaFarmacia(valore As Double) As Integer
        Try
            Return CInt(Math.Floor(valore / DIMENSIONE_CELLA_GRIGLIA_FARMACIE))
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.RigaCellaFarmacia: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function IndiceGrigliaFarmacie(elenco As List(Of Farmacia)) As Dictionary(Of String, List(Of Integer))
        Dim indice As New Dictionary(Of String, List(Of Integer))
        Try
            If elenco Is Nothing Then Return indice
            For i = 0 To elenco.Count - 1
                Try
                    Dim f As Farmacia = elenco(i)
                    If f Is Nothing OrElse f.Latitudine = 0 OrElse f.Longitudine = 0 Then Continue For
                    Dim chiave As String = ChiaveCellaFarmacia(RigaCellaFarmacia(f.Latitudine), RigaCellaFarmacia(f.Longitudine))
                    Dim lista As List(Of Integer) = Nothing
                    If Not indice.TryGetValue(chiave, lista) Then
                        lista = New List(Of Integer)
                        indice(chiave) = lista
                    End If
                    lista.Add(i)
                Catch exI As Exception
                    _logger.ScriviLog("CLSFarmacie.IndiceGrigliaFarmacie elemento: " & exI.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.IndiceGrigliaFarmacie: " & ex.ToString())
        End Try
        Return indice
    End Function

    Private Function ScomponiTokenNomeFarmacia(nomeConfrontabile As String) As List(Of String)
        Dim token As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(nomeConfrontabile) Then Return token
            Dim parti() As String = nomeConfrontabile.Split(" "c)
            For Each p In parti
                Dim t As String = p.Trim()
                If t.Length >= 2 Then token.Add(t)
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ScomponiTokenNomeFarmacia: " & ex.Message)
        End Try
        Return token
    End Function

    Private Function IndiceNomiFarmacie(elenco As List(Of Farmacia), nomiComparabili As List(Of String)) As Dictionary(Of String, List(Of Integer))
        Dim indice As New Dictionary(Of String, List(Of Integer))
        Try
            If elenco Is Nothing OrElse nomiComparabili Is Nothing Then Return indice
            For i = 0 To elenco.Count - 1
                Try
                    Dim token As List(Of String) = ScomponiTokenNomeFarmacia(nomiComparabili(i))
                    For Each t In token
                        Dim lista As List(Of Integer) = Nothing
                        If Not indice.TryGetValue(t, lista) Then
                            lista = New List(Of Integer)
                            indice(t) = lista
                        End If
                        lista.Add(i)
                    Next
                Catch exI As Exception
                    _logger.ScriviLog("CLSFarmacie.IndiceNomiFarmacie elemento: " & exI.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.IndiceNomiFarmacie: " & ex.ToString())
        End Try
        Return indice
    End Function

    Private Function CandidatiVicini(f As Farmacia, nomeConfrontabile As String, indiceGriglia As Dictionary(Of String, List(Of Integer)), indiceNomi As Dictionary(Of String, List(Of Integer))) As List(Of Integer)
        Dim candidati As New List(Of Integer)
        Dim visti As New HashSet(Of Integer)
        Try
            If f IsNot Nothing AndAlso f.Latitudine <> 0 AndAlso f.Longitudine <> 0 Then
                Dim rigaCentro As Integer = RigaCellaFarmacia(f.Latitudine)
                Dim colonnaCentro As Integer = RigaCellaFarmacia(f.Longitudine)
                For dr = -1 To 1
                    For dc = -1 To 1
                        Dim chiave As String = ChiaveCellaFarmacia(rigaCentro + dr, colonnaCentro + dc)
                        Dim lista As List(Of Integer) = Nothing
                        If indiceGriglia.TryGetValue(chiave, lista) Then
                            For Each idx In lista
                                If visti.Add(idx) Then candidati.Add(idx)
                            Next
                        End If
                    Next
                Next
            End If

            Dim token As List(Of String) = ScomponiTokenNomeFarmacia(nomeConfrontabile)
            For Each t In token
                Dim listaNome As List(Of Integer) = Nothing
                If indiceNomi.TryGetValue(t, listaNome) Then
                    For Each idx In listaNome
                        If visti.Add(idx) Then candidati.Add(idx)
                    Next
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.CandidatiVicini: " & ex.Message)
        End Try
        Return candidati
    End Function

    Private Sub FondiFarmacie(principale As Farmacia, secondaria As Farmacia)
        Try
            If principale Is Nothing OrElse secondaria Is Nothing Then Return

            If String.IsNullOrWhiteSpace(principale.Orari) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Orari) Then
                principale.Orari = secondaria.Orari
                principale.StatoApertura = secondaria.StatoApertura
            End If
            If String.IsNullOrWhiteSpace(principale.Telefono) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Telefono) Then principale.Telefono = secondaria.Telefono
            If String.IsNullOrWhiteSpace(principale.Sito) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Sito) Then principale.Sito = secondaria.Sito
            If String.IsNullOrWhiteSpace(principale.Email) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Email) Then principale.Email = secondaria.Email
            If String.IsNullOrWhiteSpace(principale.Marca) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Marca) Then principale.Marca = secondaria.Marca
            If String.IsNullOrWhiteSpace(principale.Indirizzo) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Indirizzo) Then principale.Indirizzo = secondaria.Indirizzo
            If String.IsNullOrWhiteSpace(principale.IdGers) AndAlso Not String.IsNullOrWhiteSpace(secondaria.IdGers) Then principale.IdGers = secondaria.IdGers
            If String.IsNullOrWhiteSpace(principale.PercorsoTattile) AndAlso Not String.IsNullOrWhiteSpace(secondaria.PercorsoTattile) Then principale.PercorsoTattile = secondaria.PercorsoTattile
            If String.IsNullOrWhiteSpace(principale.SegnalatoreAcustico) AndAlso Not String.IsNullOrWhiteSpace(secondaria.SegnalatoreAcustico) Then principale.SegnalatoreAcustico = secondaria.SegnalatoreAcustico
            If String.IsNullOrWhiteSpace(principale.Braille) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Braille) Then principale.Braille = secondaria.Braille
            If String.IsNullOrWhiteSpace(principale.Corrimano) AndAlso Not String.IsNullOrWhiteSpace(secondaria.Corrimano) Then principale.Corrimano = secondaria.Corrimano
            If String.IsNullOrWhiteSpace(principale.DescrizionePerNonVedenti) AndAlso Not String.IsNullOrWhiteSpace(secondaria.DescrizionePerNonVedenti) Then principale.DescrizionePerNonVedenti = secondaria.DescrizionePerNonVedenti
            If String.IsNullOrWhiteSpace(principale.AccessoSediaRotelle) AndAlso Not String.IsNullOrWhiteSpace(secondaria.AccessoSediaRotelle) Then principale.AccessoSediaRotelle = secondaria.AccessoSediaRotelle
            If String.IsNullOrWhiteSpace(principale.BagnoAccessibile) AndAlso Not String.IsNullOrWhiteSpace(secondaria.BagnoAccessibile) Then principale.BagnoAccessibile = secondaria.BagnoAccessibile
            If String.IsNullOrWhiteSpace(principale.CaniGuidaAmmessi) AndAlso Not String.IsNullOrWhiteSpace(secondaria.CaniGuidaAmmessi) Then principale.CaniGuidaAmmessi = secondaria.CaniGuidaAmmessi
            If (principale.Latitudine = 0 OrElse principale.Longitudine = 0) AndAlso secondaria.Latitudine <> 0 Then
                principale.Latitudine = secondaria.Latitudine
                principale.Longitudine = secondaria.Longitudine
            End If
            If principale.DistanzaKm = 0 AndAlso secondaria.DistanzaKm > 0 Then principale.DistanzaKm = secondaria.DistanzaKm
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.FondiFarmacie: " & ex.Message)
        End Try
    End Sub

    Private Function RimuoviDoppioniFarmacie(elenco As List(Of Farmacia)) As List(Of Farmacia)
        Dim risultato As New List(Of Farmacia)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return risultato

            Dim nomiComparabili As New List(Of String)
            For Each f In elenco
                nomiComparabili.Add(NomeFarmaciaConfrontabile(f.Denominazione))
            Next

            Dim indiceGriglia As Dictionary(Of String, List(Of Integer)) = IndiceGrigliaFarmacie(elenco)
            Dim indiceNomi As Dictionary(Of String, List(Of Integer)) = IndiceNomiFarmacie(elenco, nomiComparabili)
            Dim scartato(elenco.Count - 1) As Boolean

            For i = 0 To elenco.Count - 1
                Try
                    If scartato(i) Then Continue For
                    Dim corrente As Farmacia = elenco(i)
                    Dim candidati As List(Of Integer) = CandidatiVicini(corrente, nomiComparabili(i), indiceGriglia, indiceNomi)

                    For Each j In candidati
                        Try
                            If j <= i OrElse scartato(j) Then Continue For
                            If SonoStessaFarmacia(corrente, elenco(j), nomiComparabili(i), nomiComparabili(j)) Then
                                FondiFarmacie(corrente, elenco(j))
                                scartato(j) = True
                            End If
                        Catch exJ As Exception
                            _logger.ScriviLog("CLSFarmacie.RimuoviDoppioniFarmacie confronto: " & exJ.Message)
                        End Try
                    Next
                Catch exI As Exception
                    _logger.ScriviLog("CLSFarmacie.RimuoviDoppioniFarmacie elemento: " & exI.Message)
                End Try
            Next

            For i = 0 To elenco.Count - 1
                If Not scartato(i) Then risultato.Add(elenco(i))
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.RimuoviDoppioniFarmacie: " & ex.ToString())
            Return elenco
        End Try
        Return risultato
    End Function

    Private Function ConfrontaFarmaciePerDistanza(a As Farmacia, b As Farmacia) As Integer
        Try
            If a Is Nothing OrElse b Is Nothing Then Return 0
            If a.DistanzaKm > 0 AndAlso b.DistanzaKm > 0 Then Return a.DistanzaKm.CompareTo(b.DistanzaKm)
            If a.DistanzaKm > 0 Then Return -1
            If b.DistanzaKm > 0 Then Return 1
            Return String.Compare(a.Denominazione, b.Denominazione, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ConfrontaFarmaciePerDistanza: " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' Fonte unica e completa per una comune: unisce l'anagrafica ministeriale (indirizzo, cap, provincia)
    ''' con gli orari e i telefoni di OpenStreetMap, escludendo con precisione le farmacie di comuni diversi
    ''' tramite il poligono del confine comunale quando disponibile.
    ''' </summary>
    Public Function OttieniFarmacieComune(comune As String, soloAperte As Boolean, Optional avanzamento As Avanzamento = Nothing) As List(Of Farmacia)
        Dim risultato As New List(Of Farmacia)
        Try
            If String.IsNullOrWhiteSpace(comune) Then Return risultato

            Dim ministeriali As List(Of Farmacia) = FarmacieDelComune(comune)

            AggiornaFarmacieAperte(comune, 6000, avanzamento)
            Dim osm As List(Of Farmacia) = OttieniFarmacieAperte(comune, False)

            Dim nomiComparabiliOsm As New List(Of String)
            For Each o In osm
                nomiComparabiliOsm.Add(NomeFarmaciaConfrontabile(o.Denominazione))
            Next

            Dim indiceGrigliaOsm As Dictionary(Of String, List(Of Integer)) = IndiceGrigliaFarmacie(osm)
            Dim indiceNomiOsm As Dictionary(Of String, List(Of Integer)) = IndiceNomiFarmacie(osm, nomiComparabiliOsm)
            Dim osmUsato(osm.Count - 1) As Boolean

            For Each m In ministeriali
                Try
                    Dim nomeM As String = NomeFarmaciaConfrontabile(m.Denominazione)
                    Dim candidati As List(Of Integer) = CandidatiVicini(m, nomeM, indiceGrigliaOsm, indiceNomiOsm)

                    Dim indiceCorrispondente As Integer = -1
                    For Each idx In candidati
                        Try
                            If osmUsato(idx) Then Continue For
                            If SonoStessaFarmacia(m, osm(idx), nomeM, nomiComparabiliOsm(idx)) Then
                                indiceCorrispondente = idx
                                Exit For
                            End If
                        Catch exCandidato As Exception
                            _logger.ScriviLog("CLSFarmacie.OttieniFarmacieComune candidato: " & exCandidato.Message)
                        End Try
                    Next

                    If indiceCorrispondente >= 0 Then
                        osmUsato(indiceCorrispondente) = True
                        FondiFarmacie(m, osm(indiceCorrispondente))
                    End If

                    risultato.Add(m)
                Catch exM As Exception
                    _logger.ScriviLog("CLSFarmacie.OttieniFarmacieComune ministeriale: " & exM.Message)
                End Try
            Next

            For i = 0 To osm.Count - 1
                Try
                    If osmUsato(i) Then Continue For
                    risultato.Add(osm(i))
                Catch exO As Exception
                    _logger.ScriviLog("CLSFarmacie.OttieniFarmacieComune osm: " & exO.Message)
                End Try
            Next

            risultato = RimuoviDoppioniFarmacie(risultato)

            Try
                Dim mancaDistanza As Boolean = False
                For Each f In risultato
                    If f.DistanzaKm = 0 Then
                        mancaDistanza = True
                        Exit For
                    End If
                Next

                If mancaDistanza Then
                    Dim latitudineZona As Double = 0
                    Dim longitudineZona As Double = 0
                    If Geolocalizza(comune, latitudineZona, longitudineZona) Then
                        For Each f In risultato
                            If f.DistanzaKm = 0 AndAlso f.Latitudine <> 0 AndAlso f.Longitudine <> 0 Then
                                f.DistanzaKm = DistanzaChilometri(latitudineZona, longitudineZona, f.Latitudine, f.Longitudine)
                            End If
                        Next
                    End If
                End If

                risultato.Sort(AddressOf ConfrontaFarmaciePerDistanza)
            Catch exOrdina As Exception
                _logger.ScriviLog("CLSFarmacie.OttieniFarmacieComune ordina: " & exOrdina.Message)
            End Try

            If soloAperte Then
                Dim filtrate As New List(Of Farmacia)
                For Each f In risultato
                    If f.StatoApertura = 1 Then filtrate.Add(f)
                Next
                Return filtrate
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OttieniFarmacieComune: " & ex.ToString())
        End Try
        Return risultato
    End Function


    Public Function InformazioniFarmacia(f As Farmacia) As List(Of String)
        Dim righe As New List(Of String)
        Try
            If f Is Nothing Then Return righe

            righe.Add("Nome: " & If(String.IsNullOrWhiteSpace(f.Denominazione), "non disponibile", f.Denominazione))

            Dim indirizzoCompleto As New StringBuilder()
            If Not String.IsNullOrWhiteSpace(f.Indirizzo) Then indirizzoCompleto.Append(f.Indirizzo)
            If Not String.IsNullOrWhiteSpace(f.Cap) Then
                If indirizzoCompleto.Length > 0 Then indirizzoCompleto.Append(", ")
                indirizzoCompleto.Append(f.Cap)
            End If
            If Not String.IsNullOrWhiteSpace(f.Comune) Then
                If indirizzoCompleto.Length > 0 Then indirizzoCompleto.Append(" ")
                indirizzoCompleto.Append(f.Comune)
            End If
            If Not String.IsNullOrWhiteSpace(f.Provincia) Then
                indirizzoCompleto.Append(" (").Append(f.Provincia).Append(")")
            End If
            If indirizzoCompleto.Length > 0 Then
                righe.Add("Indirizzo: " & indirizzoCompleto.ToString())
            Else
                righe.Add("Indirizzo: non disponibile")
            End If

            If Not String.IsNullOrWhiteSpace(f.Regione) Then
                righe.Add("Regione: " & f.Regione)
            End If

            If Not String.IsNullOrWhiteSpace(f.Telefono) Then
                righe.Add("Telefono: " & f.Telefono)
            Else
                righe.Add("Telefono: non disponibile")
            End If

            If Not String.IsNullOrWhiteSpace(f.Orari) Then
                righe.Add("Orario: " & OrariLeggibili(f.Orari))
            Else
                righe.Add("Orario: non indicato")
            End If

            If f.StatoApertura >= 0 Then
                righe.Add("Stato attuale: " & StatoAperturaLeggibile(f.StatoApertura))
            End If

            If f.DistanzaKm > 0 Then
                righe.Add("Distanza dalla zona cercata: circa " & f.DistanzaKm.ToString("N1", New CultureInfo("it-IT")) & " chilometri")
            End If

            If Not String.IsNullOrWhiteSpace(f.Marca) Then
                righe.Add("Insegna o catena: " & f.Marca)
            End If

            If Not String.IsNullOrWhiteSpace(f.Sito) Then
                righe.Add("Sito web: " & f.Sito)
            End If

            If Not String.IsNullOrWhiteSpace(f.Email) Then
                righe.Add("Email: " & f.Email)
            End If

            If Not String.IsNullOrWhiteSpace(f.DescrizionePerNonVedenti) Then
                righe.Add("Note per non vedenti: " & f.DescrizionePerNonVedenti)
            End If

            If Not String.IsNullOrWhiteSpace(f.PercorsoTattile) Then
                righe.Add("Percorso tattile: " & f.PercorsoTattile)
            End If

            If Not String.IsNullOrWhiteSpace(f.SegnalatoreAcustico) Then
                righe.Add("Segnalatore acustico: " & f.SegnalatoreAcustico)
            End If

            If Not String.IsNullOrWhiteSpace(f.Braille) Then
                righe.Add("Indicazioni in Braille: " & f.Braille)
            End If

            If Not String.IsNullOrWhiteSpace(f.Corrimano) Then
                righe.Add("Corrimano: " & f.Corrimano)
            End If

            If Not String.IsNullOrWhiteSpace(f.AccessoSediaRotelle) Then
                righe.Add("Accesso con sedia a rotelle: " & f.AccessoSediaRotelle)
            End If

            If Not String.IsNullOrWhiteSpace(f.BagnoAccessibile) Then
                righe.Add("Bagno accessibile: " & f.BagnoAccessibile)
            End If

            If Not String.IsNullOrWhiteSpace(f.CaniGuidaAmmessi) Then
                righe.Add("Cani guida ammessi: " & f.CaniGuidaAmmessi)
            End If

            Return righe
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.InformazioniFarmacia: " & ex.ToString())
            Return righe
        End Try
    End Function

    Public Function StatoAperturaLeggibile(stato As Integer) As String
        Try
            If stato = 1 Then Return "aperta adesso"
            If stato = 0 Then Return "chiusa adesso"
            Return "orario non disponibile"
        Catch
            Return "orario non disponibile"
        End Try
    End Function

    Private Shared ReadOnly NomiGiorniLeggibili As String() = {"lunedi", "martedi", "mercoledi", "giovedi", "venerdi", "sabato", "domenica"}

    Public Function OrariLeggibili(orari As String) As String
        Try
            If String.IsNullOrWhiteSpace(orari) Then Return "orario non disponibile"
            Dim testoOriginale As String = orari.Trim()
            If testoOriginale = "24/7" Then Return "aperta 24 ore su 24, tutti i giorni"

            Dim regole = testoOriginale.Split(";"c)
            Dim frasi As New List(Of String)

            For Each regolaGrezza In regole
                Try
                    Dim regola As String = regolaGrezza.Trim()
                    If regola.Length = 0 Then Continue For

                    Dim posizioneCommento As Integer = regola.IndexOf(""""c)
                    If posizioneCommento >= 0 Then regola = regola.Substring(0, posizioneCommento).Trim()
                    If regola.Length = 0 Then Continue For

                    Dim parteGiorni As String = ""
                    Dim parteOrari As String = ""
                    Dim indiceOra As Integer = TrovaInizioOrari(regola)
                    If indiceOra < 0 Then
                        parteGiorni = regola
                        parteOrari = ""
                    Else
                        parteGiorni = regola.Substring(0, indiceOra).Trim()
                        parteOrari = regola.Substring(indiceOra).Trim()
                    End If

                    Dim testoGiorni As String = FormattaGiorniLeggibili(parteGiorni)
                    Dim testoOrari As String = FormattaOrariLeggibili(parteOrari)

                    Dim frase As String
                    If testoGiorni.Length > 0 AndAlso testoOrari.Length > 0 Then
                        frase = testoGiorni & " " & testoOrari
                    ElseIf testoGiorni.Length > 0 Then
                        frase = testoGiorni
                    Else
                        frase = testoOrari
                    End If

                    If frase.Length > 0 Then frasi.Add(frase)
                Catch
                End Try
            Next

            If frasi.Count = 0 Then Return "orario non disponibile"
            Return String.Join(". ", frasi)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.OrariLeggibili: " & ex.Message)
            Return If(orari, "orario non disponibile")
        End Try
    End Function

    Private Function FormattaGiorniLeggibili(parteGiorni As String) As String
        Try
            Dim p As String = parteGiorni.Trim().ToLowerInvariant()
            If p.Length = 0 Then Return ""
            If p = "ph" Then Return "nei giorni festivi"
            If p.Contains("ph") Then p = p.Replace("ph", "").Trim(","c, " "c)
            If p.Length = 0 Then Return ""

            Dim blocchi = p.Split(","c)
            Dim partiTesto As New List(Of String)
            For Each blocco In blocchi
                Dim b As String = blocco.Trim()
                If b.Length = 0 Then Continue For
                If b.Contains("-") Then
                    Dim estremi = b.Split("-"c)
                    If estremi.Length = 2 Then
                        Dim nomeInizio As String = NomeGiornoLeggibile(estremi(0).Trim())
                        Dim nomeFine As String = NomeGiornoLeggibile(estremi(1).Trim())
                        If nomeInizio.Length > 0 AndAlso nomeFine.Length > 0 Then
                            partiTesto.Add("dal " & nomeInizio & " al " & nomeFine)
                        End If
                    End If
                Else
                    Dim nomeGiorno As String = NomeGiornoLeggibile(b)
                    If nomeGiorno.Length > 0 Then partiTesto.Add(nomeGiorno)
                End If
            Next

            If partiTesto.Count = 0 Then Return ""
            If partiTesto.Count = 1 Then Return partiTesto(0)
            Return String.Join(", ", partiTesto.Take(partiTesto.Count - 1)) & " e " & partiTesto(partiTesto.Count - 1)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.FormattaGiorniLeggibili: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeGiornoLeggibile(codice As String) As String
        Try
            Dim indice As Integer = CodiceGiorno(codice)
            If indice < 0 OrElse indice > 6 Then Return ""
            Return NomiGiorniLeggibili(indice)
        Catch
            Return ""
        End Try
    End Function

    Private Function FormattaOrariLeggibili(parteOrari As String) As String
        Try
            Dim p As String = parteOrari.Trim()
            If p.Length = 0 Then Return ""
            Dim pMinuscolo As String = p.ToLowerInvariant()
            If pMinuscolo = "off" OrElse pMinuscolo = "closed" Then Return "chiusa"

            Dim intervalli = p.Split(","c)
            Dim partiTesto As New List(Of String)
            For Each intervalloGrezzo In intervalli
                Dim intervallo As String = intervalloGrezzo.Trim()
                If intervallo.Length = 0 Then Continue For
                Dim parti = intervallo.Split("-"c)
                If parti.Length = 2 Then
                    partiTesto.Add("dalle " & parti(0).Trim() & " alle " & parti(1).Trim())
                Else
                    partiTesto.Add(intervallo)
                End If
            Next

            If partiTesto.Count = 0 Then Return ""
            Return String.Join(" e ", partiTesto)
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.FormattaOrariLeggibili: " & ex.Message)
            Return parteOrari
        End Try
    End Function

    Public Function ValutaApertura(orari As String, adesso As DateTime) As Integer
        Try
            If String.IsNullOrWhiteSpace(orari) Then Return -1
            Dim testo As String = orari.Trim().ToLowerInvariant()
            If testo.Contains("24/7") Then Return 1

            Dim risultato As Integer = -1
            Dim regole = testo.Split(";"c)
            For Each regolaGrezza In regole
                Try
                    Dim regola As String = regolaGrezza.Trim()
                    If regola.Length = 0 Then Continue For
                    Dim posizioneCommento As Integer = regola.IndexOf(""""c)
                    If posizioneCommento >= 0 Then regola = regola.Substring(0, posizioneCommento).Trim()
                    If regola.Length = 0 Then Continue For

                    Dim parteGiorni As String = ""
                    Dim parteOrari As String = ""
                    Dim indiceOra As Integer = TrovaInizioOrari(regola)
                    If indiceOra < 0 Then
                        parteGiorni = regola
                        parteOrari = ""
                    Else
                        parteGiorni = regola.Substring(0, indiceOra).Trim()
                        parteOrari = regola.Substring(indiceOra).Trim()
                    End If

                    If parteGiorni.Contains("ph") OrElse parteGiorni.Contains("su[") Then Continue For

                    Dim applicaOggi As Boolean = False
                    If parteGiorni.Length = 0 Then
                        applicaOggi = True
                    Else
                        applicaOggi = GiorniIncludonoOggi(parteGiorni, adesso)
                    End If
                    If Not applicaOggi Then Continue For

                    If parteOrari.Length = 0 Then
                        If regola.Contains("off") OrElse regola.Contains("closed") Then
                            risultato = 0
                        End If
                        Continue For
                    End If

                    If parteOrari.Contains("off") OrElse parteOrari.Contains("closed") Then
                        risultato = 0
                        Continue For
                    End If

                    Dim aperto As Boolean = False
                    Dim intervalli = parteOrari.Split(","c)
                    For Each intervallo In intervalli
                        If IntervalloAttivo(intervallo.Trim(), adesso) Then
                            aperto = True
                            Exit For
                        End If
                    Next
                    If aperto Then
                        risultato = 1
                    Else
                        risultato = 0
                    End If
                Catch
                End Try
            Next

            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.ValutaApertura: " & ex.ToString())
            Return -1
        End Try
    End Function

    Private Function TrovaInizioOrari(regola As String) As Integer
        Try
            For i As Integer = 0 To regola.Length - 1
                If Char.IsDigit(regola(i)) Then
                    If i + 2 < regola.Length AndAlso regola(i + 2) = ":"c Then Return i
                    If i + 1 < regola.Length AndAlso regola(i + 1) = ":"c Then Return i
                End If
            Next
            Return -1
        Catch
            Return -1
        End Try
    End Function

    Private Function GiorniIncludonoOggi(parteGiorni As String, adesso As DateTime) As Boolean
        Try
            Dim oggi As Integer = IndiceGiorno(adesso.DayOfWeek)
            Dim blocchi = parteGiorni.Split(","c)
            For Each blocco In blocchi
                Dim b As String = blocco.Trim()
                If b.Length = 0 Then Continue For
                If b.Contains("-") Then
                    Dim estremi = b.Split("-"c)
                    If estremi.Length = 2 Then
                        Dim da As Integer = CodiceGiorno(estremi(0).Trim())
                        Dim a As Integer = CodiceGiorno(estremi(1).Trim())
                        If da >= 0 AndAlso a >= 0 Then
                            If da <= a Then
                                If oggi >= da AndAlso oggi <= a Then Return True
                            Else
                                If oggi >= da OrElse oggi <= a Then Return True
                            End If
                        End If
                    End If
                Else
                    Dim singolo As Integer = CodiceGiorno(b)
                    If singolo >= 0 AndAlso singolo = oggi Then Return True
                End If
            Next
            Return False
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.GiorniIncludonoOggi: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function IndiceGiorno(giorno As DayOfWeek) As Integer
        Select Case giorno
            Case DayOfWeek.Monday
                Return 0
            Case DayOfWeek.Tuesday
                Return 1
            Case DayOfWeek.Wednesday
                Return 2
            Case DayOfWeek.Thursday
                Return 3
            Case DayOfWeek.Friday
                Return 4
            Case DayOfWeek.Saturday
                Return 5
            Case Else
                Return 6
        End Select
    End Function

    Private Function CodiceGiorno(testo As String) As Integer
        Try
            Dim t As String = testo.Trim().ToLowerInvariant()
            If t.Length < 2 Then Return -1
            t = t.Substring(0, 2)
            Select Case t
                Case "mo"
                    Return 0
                Case "tu"
                    Return 1
                Case "we"
                    Return 2
                Case "th"
                    Return 3
                Case "fr"
                    Return 4
                Case "sa"
                    Return 5
                Case "su"
                    Return 6
                Case Else
                    Return -1
            End Select
        Catch
            Return -1
        End Try
    End Function

    Private Function IntervalloAttivo(intervallo As String, adesso As DateTime) As Boolean
        Try
            If intervallo.Length = 0 Then Return False
            Dim parti = intervallo.Split("-"c)
            If parti.Length <> 2 Then Return False

            Dim inizio As Integer = MinutiDaTesto(parti(0).Trim())
            Dim fine As Integer = MinutiDaTesto(parti(1).Trim())
            If inizio < 0 OrElse fine < 0 Then Return False

            Dim adessoMinuti As Integer = adesso.Hour * 60 + adesso.Minute

            If fine = inizio Then Return True
            If fine > inizio Then
                Return adessoMinuti >= inizio AndAlso adessoMinuti < fine
            End If
            Return adessoMinuti >= inizio OrElse adessoMinuti < fine
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.IntervalloAttivo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function MinutiDaTesto(testo As String) As Integer
        Try
            Dim t As String = testo.Trim()
            If t.Length = 0 Then Return -1
            Dim parti = t.Split(":"c)
            If parti.Length < 2 Then Return -1
            Dim ore As Integer = 0
            Dim minuti As Integer = 0
            If Not Integer.TryParse(parti(0).Trim(), ore) Then Return -1
            If Not Integer.TryParse(parti(1).Trim(), minuti) Then Return -1
            If ore < 0 OrElse ore > 48 Then Return -1
            If minuti < 0 OrElse minuti > 59 Then Return -1
            Return (ore Mod 24) * 60 + minuti
        Catch
            Return -1
        End Try
    End Function

    Public Function DistanzaChilometri(lat1 As Double, lon1 As Double, lat2 As Double, lon2 As Double) As Double
        Try
            If lat1 = 0 OrElse lat2 = 0 Then Return 0
            Dim raggio As Double = 6371.0
            Dim dLat As Double = (lat2 - lat1) * Math.PI / 180.0
            Dim dLon As Double = (lon2 - lon1) * Math.PI / 180.0
            Dim a As Double = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                              Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                              Math.Sin(dLon / 2) * Math.Sin(dLon / 2)
            Dim c As Double = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a))
            Return raggio * c
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.DistanzaChilometri: " & ex.Message)
            Return 0
        End Try
    End Function

#End Region

#Region "Chiusura"

    Public Sub Chiudi()
        Try
            SyncLock _lockDb
                If _conn IsNot Nothing Then
                    Try
                        If _conn.State = ConnectionState.Open Then _conn.Close()
                    Catch
                    End Try
                    Try
                        _conn.Dispose()
                    Catch
                    End Try
                    _conn = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("CLSFarmacie.Chiudi: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class