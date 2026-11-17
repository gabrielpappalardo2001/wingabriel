Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports WGDati
Imports WGDb
Imports wgcontrols

Public Class RadioDb

#Region "Modelli"

    Public Class StazioneRadio
        Public Property Id As String = ""
        Public Property Nome As String = ""
        Public Property Url As String = ""
        Public Property UrlRisolto As String = ""
        Public Property Sito As String = ""
        Public Property Regione As String = ""
        Public Property RegioneGrezza As String = ""
        Public Property Tag As String = ""
        Public Property Lingua As String = ""
        Public Property Codec As String = ""
        Public Property Bitrate As Integer = 0
        Public Property Voti As Integer = 0
        Public Property Ascolti As Integer = 0
        Public Property Attiva As Boolean = True
        Public Property TagsElenco As New List(Of String)

        Public ReadOnly Property UrlRiproduzione As String
            Get
                Try
                    If Not String.IsNullOrWhiteSpace(UrlRisolto) Then Return UrlRisolto
                    Return Url
                Catch
                    Return Url
                End Try
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Nome
        End Function
    End Class

    Public Class RegioneRadio
        Public Property Nome As String = ""
        Public Property Stazioni As New List(Of StazioneRadio)
    End Class

    Public Class GenereRadio
        Public Property Nome As String = ""
        Public Property Numero As Integer = 0

        Public Overrides Function ToString() As String
            Return Nome
        End Function
    End Class

    Public Class PaeseRadio
        Public Property Nome As String = ""
        Public Property Codice As String = ""
        Public Property NumeroStazioni As Integer = 0

        Public Overrides Function ToString() As String
            Return Nome
        End Function
    End Class

    Private Class ConfrontoGenere
        Implements IComparer(Of GenereRadio)

        Public Function Confronta(x As GenereRadio, y As GenereRadio) As Integer Implements IComparer(Of GenereRadio).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                If x.Numero <> y.Numero Then Return y.Numero.CompareTo(x.Numero)
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Private Class ConfrontoStazione
        Implements IComparer(Of StazioneRadio)

        Public Function Confronta(x As StazioneRadio, y As StazioneRadio) As Integer Implements IComparer(Of StazioneRadio).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Private Class ConfrontoVoti
        Implements IComparer(Of StazioneRadio)

        Public Function Confronta(x As StazioneRadio, y As StazioneRadio) As Integer Implements IComparer(Of StazioneRadio).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                If x.Voti <> y.Voti Then Return y.Voti.CompareTo(x.Voti)
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Private Class ConfrontoRegione
        Implements IComparer(Of RegioneRadio)

        Public Function Confronta(x As RegioneRadio, y As RegioneRadio) As Integer Implements IComparer(Of RegioneRadio).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                Dim xAltre As Boolean = String.Equals(x.Nome, ALTRE_REGIONI, StringComparison.CurrentCultureIgnoreCase)
                Dim yAltre As Boolean = String.Equals(y.Nome, ALTRE_REGIONI, StringComparison.CurrentCultureIgnoreCase)
                If xAltre AndAlso Not yAltre Then Return 1
                If yAltre AndAlso Not xAltre Then Return -1
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Private Class ConfrontoPaese
        Implements IComparer(Of PaeseRadio)

        Public Function Confronta(x As PaeseRadio, y As PaeseRadio) As Integer Implements IComparer(Of PaeseRadio).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

    Private Class RichiestaStazioniPaese
        Public Codice As String = ""
        Public Forza As Boolean = False
        Public Db As RadioDb

        Public Function Esegui() As Boolean
            Try
                If Db Is Nothing Then Return False
                Return Db.CaricaStazioniPaese(Codice, Forza)
            Catch
                Return False
            End Try
        End Function
    End Class

#End Region

#Region "Campi"

    Private Shared ReadOnly _lock As New Object()
    Private Shared _istanza As RadioDb

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _cartellaCache As String
    Private ReadOnly _fileDatabase As String
    Private ReadOnly _lockDb As New Object()
    Private _connessione As SQLiteConnection

    Private _stazioni As New List(Of StazioneRadio)
    Private _caricato As Boolean = False
    Private _ultimoAggiornamento As DateTime = DateTime.MinValue
    Private _idDaSegnalare As String = ""

    Private ReadOnly _lockPaesi As New Object()

    Private _paesi As New List(Of PaeseRadio)
    Private _paesiCaricati As Boolean = False
    Private _ultimoAggiornamentoPaesi As DateTime = DateTime.MinValue

    Private _stazioniPerPaese As New Dictionary(Of String, List(Of StazioneRadio))(StringComparer.OrdinalIgnoreCase)
    Private _paesiCaricatiElenco As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
    Private _ultimoAggiornamentoStazioniPaese As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)

    Private _stazioniManuali As New List(Of StazioneRadio)

    Public Const ALTRE_REGIONI As String = "Altre e non indicate"
    Public Const CODICE_ITALIA As String = "IT"

    Private Const ORE_CACHE As Integer = 24
    Private Const LIMITE_STAZIONI As Integer = 2000
    Private Const TIMEOUT_RETE As Integer = 20000
    Private Const UA_APP As String = "WinGabriel/1.0"

    Private Shared ReadOnly Mirror As String() = New String() {
        "https://all.api.radio-browser.info",
        "https://de1.api.radio-browser.info",
        "https://de2.api.radio-browser.info",
        "https://at1.api.radio-browser.info",
        "https://nl1.api.radio-browser.info"
    }

    Private Shared ReadOnly MappaRegioni As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"abruzzo", "Abruzzo"},
        {"abruzzi", "Abruzzo"},
        {"pescara", "Abruzzo"},
        {"chieti", "Abruzzo"},
        {"teramo", "Abruzzo"},
        {"l'aquila", "Abruzzo"},
        {"aquila", "Abruzzo"},
        {"basilicata", "Basilicata"},
        {"potenza", "Basilicata"},
        {"matera", "Basilicata"},
        {"calabria", "Calabria"},
        {"catanzaro", "Calabria"},
        {"cosenza", "Calabria"},
        {"crotone", "Calabria"},
        {"reggio calabria", "Calabria"},
        {"vibo valentia", "Calabria"},
        {"campania", "Campania"},
        {"napoli", "Campania"},
        {"naples", "Campania"},
        {"salerno", "Campania"},
        {"caserta", "Campania"},
        {"avellino", "Campania"},
        {"benevento", "Campania"},
        {"emilia-romagna", "Emilia-Romagna"},
        {"emilia romagna", "Emilia-Romagna"},
        {"emilia", "Emilia-Romagna"},
        {"bologna", "Emilia-Romagna"},
        {"modena", "Emilia-Romagna"},
        {"parma", "Emilia-Romagna"},
        {"reggio emilia", "Emilia-Romagna"},
        {"ferrara", "Emilia-Romagna"},
        {"ravenna", "Emilia-Romagna"},
        {"rimini", "Emilia-Romagna"},
        {"forli", "Emilia-Romagna"},
        {"cesena", "Emilia-Romagna"},
        {"piacenza", "Emilia-Romagna"},
        {"friuli-venezia giulia", "Friuli-Venezia Giulia"},
        {"friuli venezia giulia", "Friuli-Venezia Giulia"},
        {"friuli", "Friuli-Venezia Giulia"},
        {"trieste", "Friuli-Venezia Giulia"},
        {"udine", "Friuli-Venezia Giulia"},
        {"pordenone", "Friuli-Venezia Giulia"},
        {"gorizia", "Friuli-Venezia Giulia"},
        {"lazio", "Lazio"},
        {"latium", "Lazio"},
        {"roma", "Lazio"},
        {"rome", "Lazio"},
        {"latina", "Lazio"},
        {"frosinone", "Lazio"},
        {"viterbo", "Lazio"},
        {"rieti", "Lazio"},
        {"liguria", "Liguria"},
        {"genova", "Liguria"},
        {"genoa", "Liguria"},
        {"savona", "Liguria"},
        {"imperia", "Liguria"},
        {"la spezia", "Liguria"},
        {"lombardia", "Lombardia"},
        {"lombardy", "Lombardia"},
        {"milano", "Lombardia"},
        {"milan", "Lombardia"},
        {"bergamo", "Lombardia"},
        {"brescia", "Lombardia"},
        {"como", "Lombardia"},
        {"cremona", "Lombardia"},
        {"lecco", "Lombardia"},
        {"lodi", "Lombardia"},
        {"mantova", "Lombardia"},
        {"monza", "Lombardia"},
        {"pavia", "Lombardia"},
        {"sondrio", "Lombardia"},
        {"varese", "Lombardia"},
        {"marche", "Marche"},
        {"ancona", "Marche"},
        {"pesaro", "Marche"},
        {"urbino", "Marche"},
        {"macerata", "Marche"},
        {"ascoli piceno", "Marche"},
        {"fermo", "Marche"},
        {"molise", "Molise"},
        {"campobasso", "Molise"},
        {"isernia", "Molise"},
        {"piemonte", "Piemonte"},
        {"piedmont", "Piemonte"},
        {"torino", "Piemonte"},
        {"turin", "Piemonte"},
        {"alessandria", "Piemonte"},
        {"asti", "Piemonte"},
        {"biella", "Piemonte"},
        {"cuneo", "Piemonte"},
        {"novara", "Piemonte"},
        {"vercelli", "Piemonte"},
        {"verbania", "Piemonte"},
        {"puglia", "Puglia"},
        {"apulia", "Puglia"},
        {"bari", "Puglia"},
        {"brindisi", "Puglia"},
        {"foggia", "Puglia"},
        {"lecce", "Puglia"},
        {"taranto", "Puglia"},
        {"barletta", "Puglia"},
        {"sardegna", "Sardegna"},
        {"sardinia", "Sardegna"},
        {"cagliari", "Sardegna"},
        {"sassari", "Sardegna"},
        {"nuoro", "Sardegna"},
        {"oristano", "Sardegna"},
        {"olbia", "Sardegna"},
        {"sicilia", "Sicilia"},
        {"sicily", "Sicilia"},
        {"palermo", "Sicilia"},
        {"catania", "Sicilia"},
        {"messina", "Sicilia"},
        {"agrigento", "Sicilia"},
        {"caltanissetta", "Sicilia"},
        {"enna", "Sicilia"},
        {"ragusa", "Sicilia"},
        {"siracusa", "Sicilia"},
        {"trapani", "Sicilia"},
        {"toscana", "Toscana"},
        {"tuscany", "Toscana"},
        {"firenze", "Toscana"},
        {"florence", "Toscana"},
        {"arezzo", "Toscana"},
        {"grosseto", "Toscana"},
        {"livorno", "Toscana"},
        {"lucca", "Toscana"},
        {"massa", "Toscana"},
        {"carrara", "Toscana"},
        {"pisa", "Toscana"},
        {"pistoia", "Toscana"},
        {"prato", "Toscana"},
        {"siena", "Toscana"},
        {"poggibonsi", "Toscana"},
        {"valdelsa", "Toscana"},
        {"trentino-alto adige", "Trentino-Alto Adige"},
        {"trentino alto adige", "Trentino-Alto Adige"},
        {"trentino", "Trentino-Alto Adige"},
        {"alto adige", "Trentino-Alto Adige"},
        {"south tyrol", "Trentino-Alto Adige"},
        {"sudtirol", "Trentino-Alto Adige"},
        {"bolzano", "Trentino-Alto Adige"},
        {"bozen", "Trentino-Alto Adige"},
        {"trento", "Trentino-Alto Adige"},
        {"umbria", "Umbria"},
        {"perugia", "Umbria"},
        {"terni", "Umbria"},
        {"valle d'aosta", "Valle d'Aosta"},
        {"valle daosta", "Valle d'Aosta"},
        {"aosta valley", "Valle d'Aosta"},
        {"aosta", "Valle d'Aosta"},
        {"veneto", "Veneto"},
        {"venezia", "Veneto"},
        {"venice", "Veneto"},
        {"verona", "Veneto"},
        {"vicenza", "Veneto"},
        {"padova", "Veneto"},
        {"treviso", "Veneto"},
        {"rovigo", "Veneto"},
        {"belluno", "Veneto"}
    }

#End Region

#Region "Singleton"

    Public Shared Function GetInstance() As RadioDb
        Try
            If _istanza Is Nothing Then
                SyncLock _lock
                    If _istanza Is Nothing Then
                        _istanza = New RadioDb()
                    End If
                End SyncLock
            End If
            Return _istanza
        Catch
            Return _istanza
        End Try
    End Function

    Private Sub New()
        Try
            _cartellaCache = RisolviCartellaRadio()
            If Not Directory.Exists(_cartellaCache) Then
                Directory.CreateDirectory(_cartellaCache)
            End If
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.New: " & ex.ToString())
            _cartellaCache = Path.GetTempPath()
        End Try

        Try
            _fileDatabase = Path.Combine(_cartellaCache, "radio.sqlite")
            InizializzaDatabase()
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.New database: " & ex.ToString())
        End Try

        Try
            ServicePointManager.Expect100Continue = False
            ServicePointManager.DefaultConnectionLimit = 24
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.New servicepoint: " & ex.ToString())
        End Try

        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType) Or CType(12288, SecurityProtocolType)
        Catch
            Try
                ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            Catch ex2 As Exception
                _logger.ScriviLog("RadioDb.New tls: " & ex2.ToString())
            End Try
        End Try

        CaricaStazioniManuali()
    End Sub

    Private Shared Function RisolviCartellaRadio() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("radio")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(Application.StartupPath, "dati", "radio")
        Catch
            Return Path.GetTempPath()
        End Try
    End Function

    Public Sub Chiudi()
        SyncLock _lockDb
            Try
                If _connessione IsNot Nothing Then
                    _connessione.Close()
                    _connessione.Dispose()
                    _connessione = Nothing
                End If
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.Chiudi: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

#End Region

#Region "Database"

    Private Sub InizializzaDatabase()
        Try
            Dim cartella As String = Path.GetDirectoryName(_fileDatabase)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            Dim nuovoDatabase As Boolean = Not File.Exists(_fileDatabase)

            _connessione = New SQLiteConnection("Data Source=" & _fileDatabase & ";Version=3;")
            _connessione.Open()

            EseguiNonQuery("PRAGMA journal_mode=WAL;")
            EseguiNonQuery("PRAGMA synchronous=NORMAL;")
            EseguiNonQuery("PRAGMA foreign_keys=ON;")

            EseguiNonQuery("CREATE TABLE IF NOT EXISTS stazioni (" &
                "ambito TEXT NOT NULL, " &
                "id TEXT NOT NULL, " &
                "nome TEXT NOT NULL, " &
                "url TEXT NOT NULL, " &
                "url_risolto TEXT, " &
                "sito TEXT, " &
                "regione TEXT, " &
                "regione_grezza TEXT, " &
                "tag TEXT, " &
                "lingua TEXT, " &
                "codec TEXT, " &
                "bitrate INTEGER, " &
                "voti INTEGER, " &
                "ascolti INTEGER, " &
                "attiva INTEGER, " &
                "chiave_url TEXT NOT NULL, " &
                "PRIMARY KEY (ambito, chiave_url));")

            EseguiNonQuery("CREATE INDEX IF NOT EXISTS idx_stazioni_ambito ON stazioni(ambito);")
            EseguiNonQuery("CREATE INDEX IF NOT EXISTS idx_stazioni_id ON stazioni(id);")

            EseguiNonQuery("CREATE TABLE IF NOT EXISTS paesi (" &
                "codice TEXT PRIMARY KEY, " &
                "nome TEXT NOT NULL, " &
                "numero_stazioni INTEGER);")

            EseguiNonQuery("CREATE TABLE IF NOT EXISTS manuali (" &
                "id TEXT PRIMARY KEY, " &
                "nome TEXT NOT NULL, " &
                "url TEXT NOT NULL, " &
                "sito TEXT, " &
                "paese TEXT, " &
                "attiva INTEGER, " &
                "chiave_url TEXT NOT NULL UNIQUE);")

            EseguiNonQuery("CREATE TABLE IF NOT EXISTS meta (" &
                "chiave TEXT PRIMARY KEY, " &
                "valore TEXT);")

            Try
                WGDatabase.AssicuraColonnaChiave(_connessione, "stazioni")
                WGDatabase.AssicuraColonnaChiave(_connessione, "paesi")
                WGDatabase.AssicuraColonnaChiave(_connessione, "manuali")
            Catch exChiave As Exception
                _logger.ScriviLog("RadioDb.InizializzaDatabase AssicuraColonnaChiave: " & exChiave.ToString())
            End Try

            If nuovoDatabase Then
            End If
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.InizializzaDatabase: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiNonQuery(sql As String)
        Try
            If _connessione Is Nothing Then Return
            Using comando As New SQLiteCommand(sql, _connessione)
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.EseguiNonQuery (" & sql & "): " & ex.ToString())
        End Try
    End Sub

    Private Function ConvertiStringaDb(lettore As SQLiteDataReader, indice As Integer) As String
        Try
            If lettore.IsDBNull(indice) Then Return ""
            Return lettore.GetString(indice)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ConvertiStringaDb: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ConvertiInteroDb(lettore As SQLiteDataReader, indice As Integer) As Integer
        Try
            If lettore.IsDBNull(indice) Then Return 0
            Return lettore.GetInt32(indice)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ConvertiInteroDb: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function OttieniMeta(chiave As String) As DateTime
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return DateTime.MinValue
                Using comando As New SQLiteCommand("SELECT valore FROM meta WHERE chiave = @chiave;", _connessione)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    Dim valore As Object = comando.ExecuteScalar()
                    If valore Is Nothing OrElse valore Is DBNull.Value Then Return DateTime.MinValue
                    Dim data As DateTime = DateTime.MinValue
                    If DateTime.TryParse(valore.ToString(), data) Then Return data
                    Return DateTime.MinValue
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniMeta (" & chiave & "): " & ex.Message)
                Return DateTime.MinValue
            End Try
        End SyncLock
    End Function

    Private Sub ImpostaMeta(chiave As String, valore As DateTime)
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return
                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO meta (chiave, valore) VALUES (@chiave, @valore);", _connessione)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    comando.Parameters.AddWithValue("@valore", valore.ToString("O"))
                    comando.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.ImpostaMeta (" & chiave & "): " & ex.Message)
            End Try
        End SyncLock
    End Sub

    Private Function CacheScaduta(chiaveMeta As String) As Boolean
        Try
            Dim data As DateTime = OttieniMeta(chiaveMeta)
            If data = DateTime.MinValue Then Return True
            Return (DateTime.Now - data).TotalHours > ORE_CACHE
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CacheScaduta: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function CaricaStazioniDb(ambito As String) As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return risultato
                Using comando As New SQLiteCommand("SELECT id, nome, url, url_risolto, sito, regione, regione_grezza, tag, lingua, codec, bitrate, voti, ascolti, attiva FROM stazioni WHERE ambito = @ambito ORDER BY nome COLLATE NOCASE;", _connessione)
                    comando.Parameters.AddWithValue("@ambito", ambito)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim s As New StazioneRadio()
                                s.Id = ConvertiStringaDb(lettore, 0)
                                s.Nome = ConvertiStringaDb(lettore, 1)
                                s.Url = ConvertiStringaDb(lettore, 2)
                                s.UrlRisolto = ConvertiStringaDb(lettore, 3)
                                s.Sito = ConvertiStringaDb(lettore, 4)
                                s.Regione = ConvertiStringaDb(lettore, 5)
                                s.RegioneGrezza = ConvertiStringaDb(lettore, 6)
                                s.Tag = ConvertiStringaDb(lettore, 7)
                                s.TagsElenco = CalcolaTagsElenco(s.Tag)
                                s.Lingua = ConvertiStringaDb(lettore, 8)
                                s.Codec = ConvertiStringaDb(lettore, 9)
                                s.Bitrate = ConvertiInteroDb(lettore, 10)
                                s.Voti = ConvertiInteroDb(lettore, 11)
                                s.Ascolti = ConvertiInteroDb(lettore, 12)
                                s.Attiva = ConvertiInteroDb(lettore, 13) <> 0
                                risultato.Add(s)
                            Catch exRiga As Exception
                                _logger.ScriviLog("RadioDb.CaricaStazioniDb riga: " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.CaricaStazioniDb (" & ambito & "): " & ex.ToString())
            End Try
        End SyncLock
        Return risultato
    End Function

    Private Sub SalvaStazioniDb(ambito As String, elenco As List(Of StazioneRadio))
        SyncLock _lockDb
            Dim transazione As SQLiteTransaction = Nothing
            Try
                If _connessione Is Nothing Then Return
                transazione = _connessione.BeginTransaction()

                Dim chiaviWGEsistenti As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                Using comandoChiavi As New SQLiteCommand("SELECT chiave_url, ChiaveWG FROM stazioni WHERE ambito = @ambito;", _connessione, transazione)
                    comandoChiavi.Parameters.AddWithValue("@ambito", ambito)
                    Using lettoreChiavi As SQLiteDataReader = comandoChiavi.ExecuteReader()
                        While lettoreChiavi.Read()
                            Try
                                Dim chiaveUrlEsistente As String = ConvertiStringaDb(lettoreChiavi, 0)
                                Dim chiaveWGEsistente As String = ConvertiStringaDb(lettoreChiavi, 1)
                                If Not String.IsNullOrWhiteSpace(chiaveUrlEsistente) AndAlso Not String.IsNullOrWhiteSpace(chiaveWGEsistente) Then
                                    chiaviWGEsistenti(chiaveUrlEsistente) = chiaveWGEsistente
                                End If
                            Catch exRigaChiave As Exception
                                _logger.ScriviLog("RadioDb.SalvaStazioniDb lettura chiavi: " & exRigaChiave.Message)
                            End Try
                        End While
                    End Using
                End Using

                Using comandoElimina As New SQLiteCommand("DELETE FROM stazioni WHERE ambito = @ambito;", _connessione, transazione)
                    comandoElimina.Parameters.AddWithValue("@ambito", ambito)
                    comandoElimina.ExecuteNonQuery()
                End Using

                Dim doppioniScartati As Integer = 0
                Dim chiaviInserite As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

                For i As Integer = 0 To elenco.Count - 1
                    Try
                        Dim s As StazioneRadio = elenco(i)
                        Dim chiave As String = NormalizzaChiaveUrl(s.UrlRiproduzione)
                        If String.IsNullOrWhiteSpace(chiave) Then chiave = NormalizzaChiaveNome(s.Nome) & "|" & s.Regione
                        If chiaviInserite.ContainsKey(chiave) Then
                            doppioniScartati += 1
                            Continue For
                        End If
                        chiaviInserite(chiave) = True

                        Dim chiaveWGRiga As String = ""
                        If Not chiaviWGEsistenti.TryGetValue(chiave, chiaveWGRiga) OrElse String.IsNullOrWhiteSpace(chiaveWGRiga) Then
                            chiaveWGRiga = WGDatabase.GeneraChiave()
                        End If

                        Using comando As New SQLiteCommand("INSERT OR REPLACE INTO stazioni (ambito, id, nome, url, url_risolto, sito, regione, regione_grezza, tag, lingua, codec, bitrate, voti, ascolti, attiva, chiave_url, ChiaveWG) VALUES (@ambito, @id, @nome, @url, @urlRisolto, @sito, @regione, @regioneGrezza, @tag, @lingua, @codec, @bitrate, @voti, @ascolti, @attiva, @chiave, @chiavewg);", _connessione, transazione)
                            comando.Parameters.AddWithValue("@ambito", ambito)
                            comando.Parameters.AddWithValue("@id", s.Id)
                            comando.Parameters.AddWithValue("@nome", s.Nome)
                            comando.Parameters.AddWithValue("@url", s.Url)
                            comando.Parameters.AddWithValue("@urlRisolto", s.UrlRisolto)
                            comando.Parameters.AddWithValue("@sito", s.Sito)
                            comando.Parameters.AddWithValue("@regione", s.Regione)
                            comando.Parameters.AddWithValue("@regioneGrezza", s.RegioneGrezza)
                            comando.Parameters.AddWithValue("@tag", s.Tag)
                            comando.Parameters.AddWithValue("@lingua", s.Lingua)
                            comando.Parameters.AddWithValue("@codec", s.Codec)
                            comando.Parameters.AddWithValue("@bitrate", s.Bitrate)
                            comando.Parameters.AddWithValue("@voti", s.Voti)
                            comando.Parameters.AddWithValue("@ascolti", s.Ascolti)
                            comando.Parameters.AddWithValue("@attiva", If(s.Attiva, 1, 0))
                            comando.Parameters.AddWithValue("@chiave", chiave)
                            comando.Parameters.AddWithValue("@chiavewg", chiaveWGRiga)
                            comando.ExecuteNonQuery()
                        End Using
                    Catch exRiga As Exception
                        _logger.ScriviLog("RadioDb.SalvaStazioniDb riga: " & exRiga.Message)
                    End Try
                Next

                transazione.Commit()

                If doppioniScartati > 0 Then
                End If
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.SalvaStazioniDb (" & ambito & "): " & ex.ToString())
                Try
                    If transazione IsNot Nothing Then transazione.Rollback()
                Catch exRollback As Exception
                    _logger.ScriviLog("RadioDb.SalvaStazioniDb rollback: " & exRollback.Message)
                End Try
            End Try
        End SyncLock
    End Sub

    Private Function CaricaPaesiDb() As List(Of PaeseRadio)
        Dim risultato As New List(Of PaeseRadio)
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return risultato
                Using comando As New SQLiteCommand("SELECT codice, nome, numero_stazioni FROM paesi ORDER BY nome COLLATE NOCASE;", _connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim p As New PaeseRadio()
                                p.Codice = ConvertiStringaDb(lettore, 0)
                                p.Nome = ConvertiStringaDb(lettore, 1)
                                p.NumeroStazioni = ConvertiInteroDb(lettore, 2)
                                risultato.Add(p)
                            Catch exRiga As Exception
                                _logger.ScriviLog("RadioDb.CaricaPaesiDb riga: " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.CaricaPaesiDb: " & ex.ToString())
            End Try
        End SyncLock
        Return risultato
    End Function

    Private Sub SalvaPaesiDb(elenco As List(Of PaeseRadio))
        SyncLock _lockDb
            Dim transazione As SQLiteTransaction = Nothing
            Try
                If _connessione Is Nothing Then Return
                transazione = _connessione.BeginTransaction()

                Dim chiaviWGEsistenti As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                Using comandoChiavi As New SQLiteCommand("SELECT codice, ChiaveWG FROM paesi;", _connessione, transazione)
                    Using lettoreChiavi As SQLiteDataReader = comandoChiavi.ExecuteReader()
                        While lettoreChiavi.Read()
                            Try
                                Dim codiceEsistente As String = ConvertiStringaDb(lettoreChiavi, 0)
                                Dim chiaveWGEsistente As String = ConvertiStringaDb(lettoreChiavi, 1)
                                If Not String.IsNullOrWhiteSpace(codiceEsistente) AndAlso Not String.IsNullOrWhiteSpace(chiaveWGEsistente) Then
                                    chiaviWGEsistenti(codiceEsistente) = chiaveWGEsistente
                                End If
                            Catch exRigaChiave As Exception
                                _logger.ScriviLog("RadioDb.SalvaPaesiDb lettura chiavi: " & exRigaChiave.Message)
                            End Try
                        End While
                    End Using
                End Using

                Using comandoElimina As New SQLiteCommand("DELETE FROM paesi;", _connessione, transazione)
                    comandoElimina.ExecuteNonQuery()
                End Using

                For i As Integer = 0 To elenco.Count - 1
                    Try
                        Dim p As PaeseRadio = elenco(i)
                        Dim chiaveWGRiga As String = ""
                        If Not chiaviWGEsistenti.TryGetValue(p.Codice, chiaveWGRiga) OrElse String.IsNullOrWhiteSpace(chiaveWGRiga) Then
                            chiaveWGRiga = WGDatabase.GeneraChiave()
                        End If
                        Using comando As New SQLiteCommand("INSERT OR REPLACE INTO paesi (codice, nome, numero_stazioni, ChiaveWG) VALUES (@codice, @nome, @numero, @chiavewg);", _connessione, transazione)
                            comando.Parameters.AddWithValue("@codice", p.Codice)
                            comando.Parameters.AddWithValue("@nome", p.Nome)
                            comando.Parameters.AddWithValue("@numero", p.NumeroStazioni)
                            comando.Parameters.AddWithValue("@chiavewg", chiaveWGRiga)
                            comando.ExecuteNonQuery()
                        End Using
                    Catch exRiga As Exception
                        _logger.ScriviLog("RadioDb.SalvaPaesiDb riga: " & exRiga.Message)
                    End Try
                Next

                transazione.Commit()
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.SalvaPaesiDb: " & ex.ToString())
                Try
                    If transazione IsNot Nothing Then transazione.Rollback()
                Catch exRollback As Exception
                    _logger.ScriviLog("RadioDb.SalvaPaesiDb rollback: " & exRollback.Message)
                End Try
            End Try
        End SyncLock
    End Sub

    Private Function CaricaManualiDb() As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return risultato
                Using comando As New SQLiteCommand("SELECT id, nome, url, sito, paese, attiva FROM manuali ORDER BY nome COLLATE NOCASE;", _connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim s As New StazioneRadio()
                                s.Id = ConvertiStringaDb(lettore, 0)
                                s.Nome = ConvertiStringaDb(lettore, 1)
                                s.Url = ConvertiStringaDb(lettore, 2)
                                s.UrlRisolto = s.Url
                                s.Sito = ConvertiStringaDb(lettore, 3)
                                s.Regione = ConvertiStringaDb(lettore, 4)
                                s.RegioneGrezza = s.Regione
                                s.Attiva = ConvertiInteroDb(lettore, 5) <> 0
                                risultato.Add(s)
                            Catch exRiga As Exception
                                _logger.ScriviLog("RadioDb.CaricaManualiDb riga: " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.CaricaManualiDb: " & ex.ToString())
            End Try
        End SyncLock
        Return risultato
    End Function

    Private Function InserisciManualeDb(s As StazioneRadio) As Boolean
        SyncLock _lockDb
            Try
                If _connessione Is Nothing OrElse s Is Nothing Then Return False

                Dim chiave As String = NormalizzaChiaveUrl(s.UrlRiproduzione)
                If String.IsNullOrWhiteSpace(chiave) Then chiave = NormalizzaChiaveNome(s.Nome) & "|" & s.Regione

                Using comando As New SQLiteCommand("INSERT OR IGNORE INTO manuali (id, nome, url, sito, paese, attiva, chiave_url, ChiaveWG) VALUES (@id, @nome, @url, @sito, @paese, @attiva, @chiave, @chiavewg);", _connessione)
                    comando.Parameters.AddWithValue("@id", s.Id)
                    comando.Parameters.AddWithValue("@nome", s.Nome)
                    comando.Parameters.AddWithValue("@url", s.Url)
                    comando.Parameters.AddWithValue("@sito", s.Sito)
                    comando.Parameters.AddWithValue("@paese", s.Regione)
                    comando.Parameters.AddWithValue("@attiva", If(s.Attiva, 1, 0))
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    comando.Parameters.AddWithValue("@chiavewg", WGDatabase.GeneraChiaveUnivocaPerTabella(_connessione, "manuali"))
                    Dim righe As Integer = comando.ExecuteNonQuery()
                    Return righe > 0
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.InserisciManualeDb: " & ex.ToString())
                Return False
            End Try
        End SyncLock
    End Function

    Private Sub AggiornaAttivaManualeDb(id As String, attiva As Boolean)
        SyncLock _lockDb
            Try
                If _connessione Is Nothing OrElse String.IsNullOrWhiteSpace(id) Then Return
                Using comando As New SQLiteCommand("UPDATE manuali SET attiva = @attiva WHERE id = @id;", _connessione)
                    comando.Parameters.AddWithValue("@attiva", If(attiva, 1, 0))
                    comando.Parameters.AddWithValue("@id", id)
                    comando.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.AggiornaAttivaManualeDb: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Private Function ContaManualiDb() As Integer
        SyncLock _lockDb
            Try
                If _connessione Is Nothing Then Return 0
                Using comando As New SQLiteCommand("SELECT COUNT(*) FROM manuali;", _connessione)
                    Dim valore As Object = comando.ExecuteScalar()
                    If valore Is Nothing OrElse valore Is DBNull.Value Then Return 0
                    Return Convert.ToInt32(valore)
                End Using
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.ContaManualiDb: " & ex.Message)
                Return 0
            End Try
        End SyncLock
    End Function

    Private Sub MigraStazioniManualiDaJsonSeNecessario()
        Try
            Dim percorsoJson As String = Path.Combine(_cartellaCache, "radio_manuali.json")
            If Not File.Exists(percorsoJson) Then Return
            If ContaManualiDb() > 0 Then Return

            Dim contenuto As String = File.ReadAllText(percorsoJson, Encoding.UTF8)
            If String.IsNullOrWhiteSpace(contenuto) Then Return

            Dim vettore As JArray = JArray.Parse(contenuto)
            If vettore Is Nothing Then Return

            Dim migrate As Integer = 0
            Dim doppioniScartati As Integer = 0

            For Each elemento As JToken In vettore
                Try
                    Dim oggetto As JObject = TryCast(elemento, JObject)
                    If oggetto Is Nothing Then Continue For

                    Dim s As New StazioneRadio()
                    s.Id = LeggiStringa(oggetto, "id")
                    s.Nome = LeggiStringa(oggetto, "nome")
                    s.Url = LeggiStringa(oggetto, "url")
                    s.UrlRisolto = s.Url
                    s.Sito = LeggiStringa(oggetto, "sito")
                    s.Regione = LeggiStringa(oggetto, "paese")
                    s.RegioneGrezza = s.Regione
                    s.Attiva = LeggiBooleano(oggetto, "attiva", True)

                    If String.IsNullOrWhiteSpace(s.Id) Then s.Id = "manuale_" & Guid.NewGuid().ToString("N")
                    If String.IsNullOrWhiteSpace(s.Nome) Then Continue For
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    If InserisciManualeDb(s) Then
                        migrate += 1
                    Else
                        doppioniScartati += 1
                    End If
                Catch exElemento As Exception
                    _logger.ScriviLog("RadioDb.MigraStazioniManualiDaJsonSeNecessario elemento: " & exElemento.Message)
                End Try
            Next

            Dim percorsoBackup As String = percorsoJson & ".bak"
            Try
                If File.Exists(percorsoBackup) Then File.Delete(percorsoBackup)
                File.Move(percorsoJson, percorsoBackup)
            Catch exSpostamento As Exception
                _logger.ScriviLog("RadioDb.MigraStazioniManualiDaJsonSeNecessario spostamento: " & exSpostamento.Message)
            End Try

        Catch ex As Exception
            _logger.ScriviLog("RadioDb.MigraStazioniManualiDaJsonSeNecessario: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Proprieta"

    Public ReadOnly Property IsCaricato As Boolean
        Get
            Try
                Return _caricato AndAlso _stazioni.Count > 0
            Catch
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property NumeroStazioni As Integer
        Get
            Try
                Return _stazioni.Count
            Catch
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property UltimoAggiornamento As DateTime
        Get
            Return _ultimoAggiornamento
        End Get
    End Property

    Public ReadOnly Property IsPaesiCaricato As Boolean
        Get
            Try
                Return _paesiCaricati AndAlso _paesi.Count > 0
            Catch
                Return False
            End Try
        End Get
    End Property

#End Region

#Region "Caricamento"

    Public Function Carica(Optional forzaAggiornamento As Boolean = False) As Boolean
        SyncLock _lock
            Try
                If _caricato AndAlso Not forzaAggiornamento AndAlso _stazioni.Count > 0 Then
                    Return True
                End If

                Dim elenco As List(Of StazioneRadio) = Nothing

                If Not forzaAggiornamento AndAlso Not CacheScaduta("agg_" & CODICE_ITALIA) Then
                    elenco = CaricaStazioniDb(CODICE_ITALIA)
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    Dim contenuto As String = ScaricaElenco()
                    If Not String.IsNullOrWhiteSpace(contenuto) Then
                        Dim scaricato As List(Of StazioneRadio) = ParsaJson(contenuto)
                        If scaricato IsNot Nothing AndAlso scaricato.Count > 0 Then
                            SalvaStazioniDb(CODICE_ITALIA, scaricato)
                            ImpostaMeta("agg_" & CODICE_ITALIA, DateTime.Now)
                            elenco = scaricato
                        End If
                    End If
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    elenco = CaricaStazioniDb(CODICE_ITALIA)
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    Return False
                End If

                _stazioni = elenco
                _caricato = True
                _ultimoAggiornamento = DateTime.Now

                Return True
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.Carica: " & ex.ToString())
                Return False
            End Try
        End SyncLock
    End Function

    Public Function CaricaAsync(Optional forzaAggiornamento As Boolean = False) As Task(Of Boolean)
        Try
            If forzaAggiornamento Then
                Return Task.Run(New Func(Of Boolean)(AddressOf CaricaForzato))
            End If
            Return Task.Run(New Func(Of Boolean)(AddressOf CaricaNormale))
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaAsync: " & ex.ToString())
            Return Task.FromResult(False)
        End Try
    End Function

    Private Function CaricaNormale() As Boolean
        Try
            Return Carica(False)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaNormale: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function CaricaForzato() As Boolean
        Try
            Return Carica(True)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaForzato: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function ScaricaElenco() As String
        Try
            Dim percorso As String = "/json/stations/search?countrycode=IT&hidebroken=true&order=name&limit=" & LIMITE_STAZIONI.ToString()

            For i As Integer = 0 To Mirror.Length - 1
                Try
                    Dim risposta As String = ScaricaTesto(Mirror(i) & percorso)
                    If String.IsNullOrWhiteSpace(risposta) Then Continue For
                    If Not risposta.TrimStart().StartsWith("[") Then Continue For

                    Return risposta
                Catch exMirror As Exception
                    _logger.ScriviLog("RadioDb.ScaricaElenco mirror " & Mirror(i) & ": " & exMirror.Message)
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ScaricaElenco: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ScaricaTesto(indirizzo As String) As String
        Dim risposta As HttpWebResponse = Nothing
        Try
            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_RETE
            richiesta.ReadWriteTimeout = TIMEOUT_RETE
            richiesta.UserAgent = UA_APP
            richiesta.Accept = "application/json"
            richiesta.KeepAlive = False
            richiesta.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate

            risposta = CType(richiesta.GetResponse(), HttpWebResponse)
            If CInt(risposta.StatusCode) <> 200 Then Return Nothing

            Using flusso As Stream = risposta.GetResponseStream()
                If flusso Is Nothing Then Return Nothing
                Using lettore As New StreamReader(flusso, Encoding.UTF8)
                    Return lettore.ReadToEnd()
                End Using
            End Using
        Catch exWeb As WebException
            _logger.ScriviLog("RadioDb.ScaricaTesto (" & indirizzo & "): " & exWeb.Message)
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ScaricaTesto: " & ex.ToString())
            Return Nothing
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                _logger.ScriviLog("RadioDb.ScaricaTesto chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Function ParsaJson(contenuto As String) As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        Try
            If String.IsNullOrWhiteSpace(contenuto) Then Return risultato

            Dim vettore As JArray = JArray.Parse(contenuto)
            If vettore Is Nothing Then Return risultato

            Dim visti As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim doppioniTrovati As Integer = 0

            For Each elemento As JToken In vettore
                Try
                    Dim oggetto As JObject = TryCast(elemento, JObject)
                    If oggetto Is Nothing Then Continue For

                    Dim s As New StazioneRadio()
                    s.Id = LeggiStringa(oggetto, "stationuuid")
                    s.Nome = PulisciTesto(LeggiStringa(oggetto, "name"))
                    s.Url = LeggiStringa(oggetto, "url")
                    s.UrlRisolto = LeggiStringa(oggetto, "url_resolved")
                    s.Sito = LeggiStringa(oggetto, "homepage")
                    s.Tag = LeggiStringa(oggetto, "tags")
                    s.TagsElenco = CalcolaTagsElenco(s.Tag)
                    s.Lingua = LeggiStringa(oggetto, "language")
                    s.Codec = LeggiStringa(oggetto, "codec")
                    s.RegioneGrezza = LeggiStringa(oggetto, "state")
                    s.Regione = NormalizzaRegione(s.RegioneGrezza)
                    s.Bitrate = LeggiIntero(oggetto, "bitrate")
                    s.Voti = LeggiIntero(oggetto, "votes")
                    s.Ascolti = LeggiIntero(oggetto, "clickcount")
                    s.Attiva = LeggiBooleano(oggetto, "lastcheckok", True)

                    If String.IsNullOrWhiteSpace(s.Nome) Then Continue For
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    Dim chiave As String = NormalizzaChiaveUrl(s.UrlRiproduzione)
                    If String.IsNullOrWhiteSpace(chiave) Then chiave = NormalizzaChiaveNome(s.Nome) & "|" & s.Regione

                    If visti.ContainsKey(chiave) Then
                        doppioniTrovati += 1
                        Dim indiceEsistente As Integer = visti(chiave)
                        Dim esistente As StazioneRadio = risultato(indiceEsistente)
                        If PunteggioStazione(s) > PunteggioStazione(esistente) Then
                            If String.IsNullOrWhiteSpace(s.Id) Then s.Id = esistente.Id
                            risultato(indiceEsistente) = s
                        End If
                        Continue For
                    End If

                    If String.IsNullOrWhiteSpace(s.Id) Then s.Id = "rb_" & risultato.Count.ToString()

                    risultato.Add(s)
                    visti(chiave) = risultato.Count - 1
                Catch exElemento As Exception
                    _logger.ScriviLog("RadioDb.ParsaJson elemento: " & exElemento.Message)
                End Try
            Next

            If doppioniTrovati > 0 Then
            End If
        Catch exJson As JsonException
            _logger.ScriviLog("RadioDb.ParsaJson json non valido: " & exJson.Message)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ParsaJson: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function PunteggioStazione(s As StazioneRadio) As Long
        Try
            If s Is Nothing Then Return Long.MinValue
            Dim punteggio As Long = CLng(s.Voti) + CLng(s.Ascolti)
            If s.Attiva Then punteggio += 1
            Return punteggio
        Catch
            Return 0
        End Try
    End Function

    Private Function NormalizzaChiaveUrl(url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return ""
            Dim u As String = url.Trim().ToLowerInvariant()

            If u.StartsWith("https://") Then
                u = u.Substring(8)
            ElseIf u.StartsWith("http://") Then
                u = u.Substring(7)
            End If

            If u.StartsWith("www.") Then u = u.Substring(4)

            While u.EndsWith("/")
                u = u.Substring(0, u.Length - 1)
            End While

            Return u
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.NormalizzaChiaveUrl: " & ex.Message)
            Return If(url, "").Trim().ToLowerInvariant()
        End Try
    End Function

    Private Function NormalizzaChiaveNome(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Return nome.Trim().ToLowerInvariant()
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.NormalizzaChiaveNome: " & ex.Message)
            Return If(nome, "").Trim().ToLowerInvariant()
        End Try
    End Function

    Private Function LeggiStringa(oggetto As JObject, nome As String) As String
        Try
            If oggetto Is Nothing Then Return ""
            Dim token As JToken = oggetto(nome)
            If token Is Nothing Then Return ""
            If token.Type = JTokenType.Null Then Return ""
            Dim valore As String = token.ToString()
            If valore Is Nothing Then Return ""
            Return valore.Trim()
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.LeggiStringa (" & nome & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function LeggiIntero(oggetto As JObject, nome As String) As Integer
        Try
            If oggetto Is Nothing Then Return 0
            Dim token As JToken = oggetto(nome)
            If token Is Nothing Then Return 0
            If token.Type = JTokenType.Null Then Return 0
            If token.Type = JTokenType.Integer Then Return CInt(token)

            Dim valore As Integer = 0
            If Integer.TryParse(token.ToString().Trim(), valore) Then Return valore
            Return 0
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.LeggiIntero (" & nome & "): " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function LeggiBooleano(oggetto As JObject, nome As String, predefinito As Boolean) As Boolean
        Try
            If oggetto Is Nothing Then Return predefinito
            Dim token As JToken = oggetto(nome)
            If token Is Nothing Then Return predefinito
            If token.Type = JTokenType.Null Then Return predefinito
            If token.Type = JTokenType.Boolean Then Return CBool(token)
            If token.Type = JTokenType.Integer Then Return CInt(token) <> 0

            Dim testo As String = token.ToString().Trim().ToLowerInvariant()
            If testo = "1" OrElse testo = "true" Then Return True
            If testo = "0" OrElse testo = "false" Then Return False
            Return predefinito
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.LeggiBooleano (" & nome & "): " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Function PulisciTesto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim s As String = testo.Trim()
            While s.Contains("  ")
                s = s.Replace("  ", " ")
            End While
            Return s
        Catch
            Return testo
        End Try
    End Function

    Private Function CalcolaTagsElenco(tagGrezzi As String) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(tagGrezzi) Then Return risultato

            Dim pezzi As String() = tagGrezzi.Split(","c)
            For i As Integer = 0 To pezzi.Length - 1
                Dim normalizzato As String = NormalizzaTag(pezzi(i))
                If String.IsNullOrWhiteSpace(normalizzato) Then Continue For
                If risultato.Contains(normalizzato) Then Continue For
                risultato.Add(normalizzato)
            Next
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CalcolaTagsElenco: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function NormalizzaTag(tag As String) As String
        Try
            If String.IsNullOrWhiteSpace(tag) Then Return ""
            Dim t As String = tag.Trim().ToLowerInvariant()
            If t.Length = 0 Then Return ""
            If t.Length = 1 Then Return Char.ToUpperInvariant(t(0))
            Return Char.ToUpperInvariant(t(0)) & t.Substring(1)
        Catch
            Return ""
        End Try
    End Function

    Private Function NormalizzaRegione(grezza As String) As String
        Try
            If String.IsNullOrWhiteSpace(grezza) Then Return ALTRE_REGIONI

            Dim testo As String = grezza.Trim().ToLowerInvariant()
            If MappaRegioni.ContainsKey(testo) Then Return MappaRegioni(testo)

            For Each coppia As KeyValuePair(Of String, String) In MappaRegioni
                If testo.Contains(coppia.Key) Then Return coppia.Value
            Next

            Return ALTRE_REGIONI
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.NormalizzaRegione: " & ex.Message)
            Return ALTRE_REGIONI
        End Try
    End Function

#End Region

#Region "Interrogazioni"

    Public Function OttieniTutte() As List(Of StazioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of StazioneRadio)
            Try
                For i As Integer = 0 To _stazioni.Count - 1
                    If String.IsNullOrWhiteSpace(_stazioni(i).UrlRiproduzione) Then Continue For
                    risultato.Add(_stazioni(i))
                Next

                For i As Integer = 0 To _stazioniManuali.Count - 1
                    Dim s As StazioneRadio = _stazioniManuali(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For
                    If String.Equals(s.Regione, CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(s.Regione, ALTRE_REGIONI, StringComparison.OrdinalIgnoreCase) OrElse String.IsNullOrWhiteSpace(s.Regione) Then
                        risultato.Add(s)
                    End If
                Next

                risultato.Sort(New ConfrontoStazione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniTutte: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniPiuAscoltate(quante As Integer) As List(Of StazioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of StazioneRadio)
            Try
                If quante <= 0 Then quante = 50

                Dim copia As New List(Of StazioneRadio)
                For i As Integer = 0 To _stazioni.Count - 1
                    If String.IsNullOrWhiteSpace(_stazioni(i).UrlRiproduzione) Then Continue For
                    copia.Add(_stazioni(i))
                Next

                copia.Sort(New ConfrontoVoti())

                For i As Integer = 0 To copia.Count - 1
                    If risultato.Count >= quante Then Exit For
                    risultato.Add(copia(i))
                Next
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniPiuAscoltate: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniRegioni() As List(Of RegioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of RegioneRadio)
            Try
                Dim mappa As New Dictionary(Of String, RegioneRadio)(StringComparer.CurrentCultureIgnoreCase)

                For i As Integer = 0 To _stazioni.Count - 1
                    Dim s As StazioneRadio = _stazioni(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    Dim nomeRegione As String = s.Regione
                    If String.IsNullOrWhiteSpace(nomeRegione) Then nomeRegione = ALTRE_REGIONI

                    If Not mappa.ContainsKey(nomeRegione) Then
                        Dim r As New RegioneRadio()
                        r.Nome = nomeRegione
                        mappa(nomeRegione) = r
                    End If

                    mappa(nomeRegione).Stazioni.Add(s)
                Next

                For Each coppia As KeyValuePair(Of String, RegioneRadio) In mappa
                    coppia.Value.Stazioni.Sort(New ConfrontoStazione())
                    risultato.Add(coppia.Value)
                Next

                risultato.Sort(New ConfrontoRegione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniRegioni: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniStazioniRegione(nomeRegione As String) As List(Of StazioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of StazioneRadio)
            Try
                If String.IsNullOrWhiteSpace(nomeRegione) Then Return risultato

                For i As Integer = 0 To _stazioni.Count - 1
                    Dim s As StazioneRadio = _stazioni(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For
                    If Not String.Equals(s.Regione, nomeRegione, StringComparison.CurrentCultureIgnoreCase) Then Continue For
                    risultato.Add(s)
                Next

                risultato.Sort(New ConfrontoStazione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniStazioniRegione: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniGeneri(Optional minimoStazioni As Integer = 3) As List(Of GenereRadio)
        SyncLock _lock
            Dim risultato As New List(Of GenereRadio)
            Try
                Dim mappa As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

                For i As Integer = 0 To _stazioni.Count - 1
                    Dim s As StazioneRadio = _stazioni(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    Dim contati As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
                    For j As Integer = 0 To s.TagsElenco.Count - 1
                        Dim tag As String = s.TagsElenco(j)
                        If String.IsNullOrWhiteSpace(tag) Then Continue For
                        If contati.ContainsKey(tag) Then Continue For
                        contati(tag) = True

                        If mappa.ContainsKey(tag) Then
                            mappa(tag) = mappa(tag) + 1
                        Else
                            mappa(tag) = 1
                        End If
                    Next
                Next

                For Each coppia As KeyValuePair(Of String, Integer) In mappa
                    If coppia.Value < minimoStazioni Then Continue For
                    Dim g As New GenereRadio()
                    g.Nome = coppia.Key
                    g.Numero = coppia.Value
                    risultato.Add(g)
                Next

                risultato.Sort(New ConfrontoGenere())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniGeneri: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniStazioniGenere(nomeGenere As String) As List(Of StazioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of StazioneRadio)
            Try
                If String.IsNullOrWhiteSpace(nomeGenere) Then Return risultato

                For i As Integer = 0 To _stazioni.Count - 1
                    Dim s As StazioneRadio = _stazioni(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    Dim trovato As Boolean = False
                    For j As Integer = 0 To s.TagsElenco.Count - 1
                        If String.Equals(s.TagsElenco(j), nomeGenere, StringComparison.CurrentCultureIgnoreCase) Then
                            trovato = True
                            Exit For
                        End If
                    Next

                    If trovato Then risultato.Add(s)
                Next

                risultato.Sort(New ConfrontoStazione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniStazioniGenere: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function TrovaPerId(id As String) As StazioneRadio
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return Nothing

                For i As Integer = 0 To _stazioni.Count - 1
                    If String.Equals(_stazioni(i).Id, id, StringComparison.OrdinalIgnoreCase) Then Return _stazioni(i)
                Next

                For i As Integer = 0 To _stazioniManuali.Count - 1
                    If String.Equals(_stazioniManuali(i).Id, id, StringComparison.OrdinalIgnoreCase) Then Return _stazioniManuali(i)
                Next

                For Each coppia As KeyValuePair(Of String, List(Of StazioneRadio)) In _stazioniPerPaese
                    For i As Integer = 0 To coppia.Value.Count - 1
                        If String.Equals(coppia.Value(i).Id, id, StringComparison.OrdinalIgnoreCase) Then Return coppia.Value(i)
                    Next
                Next

                Return Nothing
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.TrovaPerId: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function TrovaPerUrl(url As String) As StazioneRadio
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(url) Then Return Nothing
                Dim u As String = url.Trim()

                For i As Integer = 0 To _stazioni.Count - 1
                    If String.Equals(_stazioni(i).UrlRiproduzione, u, StringComparison.OrdinalIgnoreCase) Then Return _stazioni(i)
                Next

                For i As Integer = 0 To _stazioniManuali.Count - 1
                    If String.Equals(_stazioniManuali(i).UrlRiproduzione, u, StringComparison.OrdinalIgnoreCase) Then Return _stazioniManuali(i)
                Next

                For Each coppia As KeyValuePair(Of String, List(Of StazioneRadio)) In _stazioniPerPaese
                    For i As Integer = 0 To coppia.Value.Count - 1
                        If String.Equals(coppia.Value(i).UrlRiproduzione, u, StringComparison.OrdinalIgnoreCase) Then Return coppia.Value(i)
                    Next
                Next

                Return Nothing
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.TrovaPerUrl: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function Cerca(testo As String) As List(Of StazioneRadio)
        SyncLock _lock
            Dim risultato As New List(Of StazioneRadio)
            Try
                If String.IsNullOrWhiteSpace(testo) Then Return risultato
                Dim q As String = testo.Trim()

                For i As Integer = 0 To _stazioni.Count - 1
                    Dim s As StazioneRadio = _stazioni(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                    Dim trovato As Boolean = False
                    If s.Nome.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 Then trovato = True
                    If Not trovato AndAlso Not String.IsNullOrWhiteSpace(s.Tag) Then
                        If s.Tag.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 Then trovato = True
                    End If

                    If trovato Then risultato.Add(s)
                Next

                For i As Integer = 0 To _stazioniManuali.Count - 1
                    Dim s As StazioneRadio = _stazioniManuali(i)
                    If String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For
                    If Not (String.Equals(s.Regione, CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(s.Regione, ALTRE_REGIONI, StringComparison.OrdinalIgnoreCase) OrElse String.IsNullOrWhiteSpace(s.Regione)) Then Continue For
                    If s.Nome.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 Then risultato.Add(s)
                Next

                risultato.Sort(New ConfrontoStazione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.Cerca: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

#End Region

#Region "Paesi"

    Public Function CaricaPaesi(Optional forzaAggiornamento As Boolean = False) As Boolean
        SyncLock _lockPaesi
            Try
                If _paesiCaricati AndAlso Not forzaAggiornamento AndAlso _paesi.Count > 0 Then
                    Return True
                End If

                Dim elenco As List(Of PaeseRadio) = Nothing

                If Not forzaAggiornamento AndAlso Not CacheScaduta("agg_paesi") Then
                    elenco = CaricaPaesiDb()
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    Dim contenuto As String = ScaricaElencoPaesi()
                    If Not String.IsNullOrWhiteSpace(contenuto) Then
                        Dim scaricato As List(Of PaeseRadio) = ParsaJsonPaesi(contenuto)
                        If scaricato IsNot Nothing AndAlso scaricato.Count > 0 Then
                            SalvaPaesiDb(scaricato)
                            ImpostaMeta("agg_paesi", DateTime.Now)
                            elenco = scaricato
                        End If
                    End If
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    elenco = CaricaPaesiDb()
                End If

                If elenco Is Nothing OrElse elenco.Count = 0 Then
                    Return False
                End If

                _paesi = elenco
                _paesiCaricati = True
                _ultimoAggiornamentoPaesi = DateTime.Now

                Return True
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.CaricaPaesi: " & ex.ToString())
                Return False
            End Try
        End SyncLock
    End Function

    Public Function CaricaPaesiAsync(Optional forzaAggiornamento As Boolean = False) As Task(Of Boolean)
        Try
            If forzaAggiornamento Then
                Return Task.Run(New Func(Of Boolean)(AddressOf CaricaPaesiForzato))
            End If
            Return Task.Run(New Func(Of Boolean)(AddressOf CaricaPaesiNormale))
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaPaesiAsync: " & ex.ToString())
            Return Task.FromResult(False)
        End Try
    End Function

    Private Function CaricaPaesiNormale() As Boolean
        Try
            Return CaricaPaesi(False)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaPaesiNormale: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function CaricaPaesiForzato() As Boolean
        Try
            Return CaricaPaesi(True)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaPaesiForzato: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniPaesi(Optional escludiItalia As Boolean = False) As List(Of PaeseRadio)
        SyncLock _lockPaesi
            Dim risultato As New List(Of PaeseRadio)
            Try
                For i As Integer = 0 To _paesi.Count - 1
                    Dim p As PaeseRadio = _paesi(i)
                    If p.NumeroStazioni <= 0 Then Continue For
                    If escludiItalia AndAlso String.Equals(p.Codice, CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then Continue For
                    risultato.Add(p)
                Next
                risultato.Sort(New ConfrontoPaese())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniPaesi: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Private Function ScaricaElencoPaesi() As String
        Try
            Dim percorso As String = "/json/countries?order=name"

            For i As Integer = 0 To Mirror.Length - 1
                Try
                    Dim risposta As String = ScaricaTesto(Mirror(i) & percorso)
                    If String.IsNullOrWhiteSpace(risposta) Then Continue For
                    If Not risposta.TrimStart().StartsWith("[") Then Continue For

                    Return risposta
                Catch exMirror As Exception
                    _logger.ScriviLog("RadioDb.ScaricaElencoPaesi mirror " & Mirror(i) & ": " & exMirror.Message)
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ScaricaElencoPaesi: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ParsaJsonPaesi(contenuto As String) As List(Of PaeseRadio)
        Dim risultato As New List(Of PaeseRadio)
        Try
            If String.IsNullOrWhiteSpace(contenuto) Then Return risultato

            Dim vettore As JArray = JArray.Parse(contenuto)
            If vettore Is Nothing Then Return risultato

            For Each elemento As JToken In vettore
                Try
                    Dim oggetto As JObject = TryCast(elemento, JObject)
                    If oggetto Is Nothing Then Continue For

                    Dim p As New PaeseRadio()
                    p.Nome = PulisciTesto(LeggiStringa(oggetto, "name"))
                    p.Codice = LeggiStringa(oggetto, "iso_3166_1").Trim().ToUpperInvariant()
                    p.NumeroStazioni = LeggiIntero(oggetto, "stationcount")

                    If String.IsNullOrWhiteSpace(p.Nome) OrElse String.IsNullOrWhiteSpace(p.Codice) Then Continue For

                    risultato.Add(p)
                Catch exElemento As Exception
                    _logger.ScriviLog("RadioDb.ParsaJsonPaesi elemento: " & exElemento.Message)
                End Try
            Next
        Catch exJson As JsonException
            _logger.ScriviLog("RadioDb.ParsaJsonPaesi json non valido: " & exJson.Message)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ParsaJsonPaesi: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function CaricaStazioniPaese(codicePaese As String, Optional forzaAggiornamento As Boolean = False) As Boolean
        Try
            If String.IsNullOrWhiteSpace(codicePaese) Then Return False
            Dim codice As String = codicePaese.Trim().ToUpperInvariant()

            SyncLock _lockPaesi
                Dim giaCaricato As Boolean = _paesiCaricatiElenco.ContainsKey(codice) AndAlso _paesiCaricatiElenco(codice)
                If giaCaricato AndAlso Not forzaAggiornamento AndAlso _stazioniPerPaese.ContainsKey(codice) AndAlso _stazioniPerPaese(codice).Count > 0 Then
                    Return True
                End If
            End SyncLock

            Dim chiaveMeta As String = "agg_paese_" & codice
            Dim elenco As List(Of StazioneRadio) = Nothing

            If Not forzaAggiornamento AndAlso Not CacheScaduta(chiaveMeta) Then
                elenco = CaricaStazioniDb(codice)
            End If

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                Dim contenuto As String = ScaricaElencoPaese(codice)
                If Not String.IsNullOrWhiteSpace(contenuto) Then
                    Dim scaricato As List(Of StazioneRadio) = ParsaJson(contenuto)
                    If scaricato IsNot Nothing AndAlso scaricato.Count > 0 Then
                        SalvaStazioniDb(codice, scaricato)
                        ImpostaMeta(chiaveMeta, DateTime.Now)
                        elenco = scaricato
                    End If
                End If
            End If

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                elenco = CaricaStazioniDb(codice)
            End If

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                Return False
            End If

            SyncLock _lockPaesi
                _stazioniPerPaese(codice) = elenco
                _paesiCaricatiElenco(codice) = True
                _ultimoAggiornamentoStazioniPaese(codice) = DateTime.Now
            End SyncLock

            Return True
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaStazioniPaese: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function CaricaStazioniPaeseAsync(codicePaese As String, Optional forzaAggiornamento As Boolean = False) As Task(Of Boolean)
        Try
            Dim richiesta As New RichiestaStazioniPaese()
            richiesta.Codice = codicePaese
            richiesta.Forza = forzaAggiornamento
            richiesta.Db = Me
            Return Task.Run(New Func(Of Boolean)(AddressOf richiesta.Esegui))
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaStazioniPaeseAsync: " & ex.ToString())
            Return Task.FromResult(False)
        End Try
    End Function

    Public Function IsPaeseCaricato(codicePaese As String) As Boolean
        SyncLock _lockPaesi
            Try
                If String.IsNullOrWhiteSpace(codicePaese) Then Return False
                Dim codice As String = codicePaese.Trim().ToUpperInvariant()
                Return _paesiCaricatiElenco.ContainsKey(codice) AndAlso _paesiCaricatiElenco(codice) AndAlso _stazioniPerPaese.ContainsKey(codice) AndAlso _stazioniPerPaese(codice).Count > 0
            Catch
                Return False
            End Try
        End SyncLock
    End Function

    Private Function ScaricaElencoPaese(codicePaese As String) As String
        Try
            Dim percorso As String = "/json/stations/search?countrycode=" & Uri.EscapeDataString(codicePaese) & "&hidebroken=true&order=name&limit=" & LIMITE_STAZIONI.ToString()

            For i As Integer = 0 To Mirror.Length - 1
                Try
                    Dim risposta As String = ScaricaTesto(Mirror(i) & percorso)
                    If String.IsNullOrWhiteSpace(risposta) Then Continue For
                    If Not risposta.TrimStart().StartsWith("[") Then Continue For

                    Return risposta
                Catch exMirror As Exception
                    _logger.ScriviLog("RadioDb.ScaricaElencoPaese mirror " & Mirror(i) & ": " & exMirror.Message)
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.ScaricaElencoPaese: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniStazioniPaese(codicePaese As String) As List(Of StazioneRadio)
        SyncLock _lockPaesi
            Dim risultato As New List(Of StazioneRadio)
            Try
                If String.IsNullOrWhiteSpace(codicePaese) Then Return risultato
                Dim codice As String = codicePaese.Trim().ToUpperInvariant()

                If _stazioniPerPaese.ContainsKey(codice) Then
                    Dim lista As List(Of StazioneRadio) = _stazioniPerPaese(codice)
                    For i As Integer = 0 To lista.Count - 1
                        If String.IsNullOrWhiteSpace(lista(i).UrlRiproduzione) Then Continue For
                        risultato.Add(lista(i))
                    Next
                End If

                risultato.AddRange(FiltraStazioniManualiPerPaese(codice))
                risultato.Sort(New ConfrontoStazione())
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.OttieniStazioniPaese: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function OttieniGeneriPaese(codicePaese As String, Optional minimoStazioni As Integer = 2) As List(Of GenereRadio)
        Dim risultato As New List(Of GenereRadio)
        Try
            Dim stazioni As List(Of StazioneRadio) = OttieniStazioniPaese(codicePaese)
            Dim mappa As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

            For i As Integer = 0 To stazioni.Count - 1
                Dim contati As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
                For j As Integer = 0 To stazioni(i).TagsElenco.Count - 1
                    Dim tag As String = stazioni(i).TagsElenco(j)
                    If String.IsNullOrWhiteSpace(tag) Then Continue For
                    If contati.ContainsKey(tag) Then Continue For
                    contati(tag) = True

                    If mappa.ContainsKey(tag) Then
                        mappa(tag) = mappa(tag) + 1
                    Else
                        mappa(tag) = 1
                    End If
                Next
            Next

            For Each coppia As KeyValuePair(Of String, Integer) In mappa
                If coppia.Value < minimoStazioni Then Continue For
                Dim g As New GenereRadio()
                g.Nome = coppia.Key
                g.Numero = coppia.Value
                risultato.Add(g)
            Next

            risultato.Sort(New ConfrontoGenere())
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.OttieniGeneriPaese: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniStazioniGenerePaese(codicePaese As String, nomeGenere As String) As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        Try
            If String.IsNullOrWhiteSpace(nomeGenere) Then Return risultato

            Dim stazioni As List(Of StazioneRadio) = OttieniStazioniPaese(codicePaese)
            For i As Integer = 0 To stazioni.Count - 1
                Dim trovato As Boolean = False
                For j As Integer = 0 To stazioni(i).TagsElenco.Count - 1
                    If String.Equals(stazioni(i).TagsElenco(j), nomeGenere, StringComparison.CurrentCultureIgnoreCase) Then
                        trovato = True
                        Exit For
                    End If
                Next
                If trovato Then risultato.Add(stazioni(i))
            Next

            risultato.Sort(New ConfrontoStazione())
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.OttieniStazioniGenerePaese: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function CercaPaese(codicePaese As String, testo As String) As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return risultato
            Dim q As String = testo.Trim()

            Dim stazioni As List(Of StazioneRadio) = OttieniStazioniPaese(codicePaese)
            For i As Integer = 0 To stazioni.Count - 1
                Dim s As StazioneRadio = stazioni(i)
                Dim trovato As Boolean = False
                If s.Nome.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 Then trovato = True
                If Not trovato AndAlso Not String.IsNullOrWhiteSpace(s.Tag) Then
                    If s.Tag.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 Then trovato = True
                End If
                If trovato Then risultato.Add(s)
            Next

            risultato.Sort(New ConfrontoStazione())
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CercaPaese: " & ex.ToString())
        End Try
        Return risultato
    End Function

#End Region

#Region "Stazioni manuali"

    Public Function AggiungiStazioneManuale(nome As String, url As String, Optional sito As String = "", Optional codicePaese As String = "") As StazioneRadio
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(url) Then Return Nothing

                Dim esistente As StazioneRadio = TrovaPerUrl(url)
                If esistente IsNot Nothing Then Return esistente

                Dim s As New StazioneRadio()
                s.Id = "manuale_" & Guid.NewGuid().ToString("N")
                s.Nome = nome.Trim()
                s.Url = url.Trim()
                s.UrlRisolto = url.Trim()
                s.Sito = If(sito, "").Trim()
                s.Regione = If(String.IsNullOrWhiteSpace(codicePaese), ALTRE_REGIONI, codicePaese.Trim().ToUpperInvariant())
                s.RegioneGrezza = s.Regione
                s.Attiva = True

                If Not InserisciManualeDb(s) Then
                    Return TrovaPerUrl(url)
                End If

                _stazioniManuali.Add(s)

                Return s
            Catch ex As Exception
                _logger.ScriviLog("RadioDb.AggiungiStazioneManuale: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function VerificaCompatibilitaIndirizzo(url As String) As Boolean
        Dim risposta As HttpWebResponse = Nothing
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(url.Trim()), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_RETE
            richiesta.ReadWriteTimeout = TIMEOUT_RETE
            richiesta.UserAgent = UA_APP
            richiesta.KeepAlive = False
            richiesta.AllowAutoRedirect = True

            risposta = CType(richiesta.GetResponse(), HttpWebResponse)
            Dim codice As Integer = CInt(risposta.StatusCode)
            Dim tipo As String = If(risposta.ContentType, "").ToLowerInvariant()

            If codice <> 200 Then Return False
            If tipo.Contains("audio") OrElse tipo.Contains("mpegurl") OrElse tipo.Contains("ogg") OrElse tipo.Contains("aac") OrElse tipo.Contains("octet-stream") OrElse tipo.Contains("video") OrElse String.IsNullOrWhiteSpace(tipo) Then
                Return True
            End If

            Return False
        Catch exWeb As WebException
            _logger.ScriviLog("RadioDb.VerificaCompatibilitaIndirizzo (" & url & "): " & exWeb.Message)
            Return False
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.VerificaCompatibilitaIndirizzo: " & ex.ToString())
            Return False
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                _logger.ScriviLog("RadioDb.VerificaCompatibilitaIndirizzo chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Public Function InviaARadioBrowser(nome As String, url As String, Optional sito As String = "", Optional codicePaese As String = "") As Boolean
        Dim risposta As HttpWebResponse = Nothing
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(url) Then Return False

            Dim parametri As New StringBuilder()
            parametri.Append("name=").Append(Uri.EscapeDataString(nome))
            parametri.Append("&url=").Append(Uri.EscapeDataString(url))
            If Not String.IsNullOrWhiteSpace(sito) Then
                parametri.Append("&homepage=").Append(Uri.EscapeDataString(sito))
            End If
            If Not String.IsNullOrWhiteSpace(codicePaese) Then
                parametri.Append("&countrycode=").Append(Uri.EscapeDataString(codicePaese))
            End If

            Dim indirizzo As String = Mirror(0) & "/json/add?" & parametri.ToString()

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_RETE
            richiesta.ReadWriteTimeout = TIMEOUT_RETE
            richiesta.UserAgent = UA_APP
            richiesta.Accept = "application/json"
            richiesta.KeepAlive = False

            risposta = CType(richiesta.GetResponse(), HttpWebResponse)
            Dim ok As Boolean = CInt(risposta.StatusCode) = 200

            Return ok
        Catch exWeb As WebException
            _logger.ScriviLog("RadioDb.InviaARadioBrowser (" & nome & "): " & exWeb.Message)
            Return False
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.InviaARadioBrowser: " & ex.ToString())
            Return False
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                _logger.ScriviLog("RadioDb.InviaARadioBrowser chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Public Sub SegnalaErroreStazione(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim s As StazioneRadio = TrovaPerId(id)
            If s Is Nothing Then Return

            s.Attiva = False

            If id.StartsWith("manuale_", StringComparison.OrdinalIgnoreCase) Then
                AggiornaAttivaManualeDb(id, False)
            End If

        Catch ex As Exception
            _logger.ScriviLog("RadioDb.SegnalaErroreStazione: " & ex.ToString())
        End Try
    End Sub

    Private Function FiltraStazioniManualiPerPaese(codicePaese As String) As List(Of StazioneRadio)
        Dim risultato As New List(Of StazioneRadio)
        Try
            If String.IsNullOrWhiteSpace(codicePaese) Then Return risultato
            For i As Integer = 0 To _stazioniManuali.Count - 1
                If String.Equals(_stazioniManuali(i).Regione, codicePaese, StringComparison.OrdinalIgnoreCase) Then
                    risultato.Add(_stazioniManuali(i))
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.FiltraStazioniManualiPerPaese: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Sub CaricaStazioniManuali()
        Try
            MigraStazioniManualiDaJsonSeNecessario()

            _stazioniManuali = CaricaManualiDb()
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.CaricaStazioniManuali: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Statistiche"

    Public Sub SegnalaAscolto(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            If id.StartsWith("rb_", StringComparison.OrdinalIgnoreCase) Then Return
            _idDaSegnalare = id
            Task.Run(New Action(AddressOf InviaSegnalazione))
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.SegnalaAscolto: " & ex.ToString())
        End Try
    End Sub

    Private Sub InviaSegnalazione()
        Try
            Dim id As String = _idDaSegnalare
            If String.IsNullOrWhiteSpace(id) Then Return
            ScaricaTesto(Mirror(0) & "/json/url/" & id)
        Catch ex As Exception
            _logger.ScriviLog("RadioDb.InviaSegnalazione: " & ex.Message)
        End Try
    End Sub

#End Region

End Class