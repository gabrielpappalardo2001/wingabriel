Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports wgcontrols
Imports WGDati
Imports WGDb

' Gestione del database dei canali televisivi. Contiene la struttura dei canali, il caricamento da sorgenti esterne, la verifica
' dello stato di funzionamento e la ricerca.
Public Class TvCanaliDb

#Region "Modelli"

    Public Class CandidatoStream
        Public Property Url As String = ""
        Public Property UserAgent As String = ""
        Public Property Referer As String = ""
        Public Property Fonte As String = ""
    End Class

    Public Class CanaleTv
        Public Property Id As String = ""
        Public Property TvgId As String = ""
        Public Property Numero As Integer = 0
        Public Property Nome As String = ""
        Public Property Gruppo As String = ""
        Public Property Verificato As Boolean = False
        Public Property Funzionante As Boolean = False
        Public Property Candidati As New List(Of CandidatoStream)
        Public Property IndiceAttivo As Integer = 0

        Public ReadOnly Property CandidatoCorrente As CandidatoStream
            Get
                Try
                    If Candidati Is Nothing OrElse Candidati.Count = 0 Then Return Nothing
                    Dim indice As Integer = IndiceAttivo
                    If indice < 0 OrElse indice >= Candidati.Count Then indice = 0
                    Return Candidati(indice)
                Catch
                    Return Nothing
                End Try
            End Get
        End Property

        Public ReadOnly Property Url As String
            Get
                Try
                    Dim c As CandidatoStream = CandidatoCorrente
                    If c Is Nothing Then Return ""
                    Return c.Url
                Catch
                    Return ""
                End Try
            End Get
        End Property

        Public ReadOnly Property UserAgent As String
            Get
                Try
                    Dim c As CandidatoStream = CandidatoCorrente
                    If c Is Nothing Then Return ""
                    Return c.UserAgent
                Catch
                    Return ""
                End Try
            End Get
        End Property

        Public ReadOnly Property Referer As String
            Get
                Try
                    Dim c As CandidatoStream = CandidatoCorrente
                    If c Is Nothing Then Return ""
                    Return c.Referer
                Catch
                    Return ""
                End Try
            End Get
        End Property

        Public ReadOnly Property NumeroCandidati As Integer
            Get
                Try
                    Return Candidati.Count
                Catch
                    Return 0
                End Try
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Nome
        End Function
    End Class

    Private Class ConfrontoCanale
        Implements IComparer(Of CanaleTv)

        Public Function Confronta(x As CanaleTv, y As CanaleTv) As Integer Implements IComparer(Of CanaleTv).Compare
            Try
                If x Is Nothing AndAlso y Is Nothing Then Return 0
                If x Is Nothing Then Return 1
                If y Is Nothing Then Return -1
                If x.Numero <> y.Numero Then Return x.Numero.CompareTo(y.Numero)
                Return String.Compare(x.Nome, y.Nome, StringComparison.CurrentCultureIgnoreCase)
            Catch
                Return 0
            End Try
        End Function
    End Class

#End Region

#Region "Campi"

    Public Event ProgressoVerifica(fatti As Integer, totale As Integer)

    Private Shared ReadOnly _lock As New Object()
    Private Shared _istanza As TvCanaliDb

    Private ReadOnly _logger As New Logger()

    Private _canali As New List(Of CanaleTv)
    Private _caricato As Boolean = False
    Private _ultimoAggiornamento As DateTime = DateTime.MinValue
    Private _verificaEseguita As Boolean = False
    Private _verificaInCorso As Boolean = False
    Private _fonteAttuale As String = ""

    Private ReadOnly _cartellaDati As String
    Private ReadOnly _fileDb As String
    Private ReadOnly _stringaConnessione As String

    Private Const URL_TUNDRAK As String = "https://raw.githubusercontent.com/Tundrak/IPTV-Italia/main/iptvitaplus.m3u"
    Private Const URL_CHILLIPTV As String = "https://raw.githubusercontent.com/chillFil/chillIPTV/main/iptvitaplus.m3u"
    Private Const URL_IPTVORG As String = "https://iptv-org.github.io/iptv/countries/it.m3u"
    Private Const URL_FREETV As String = "https://raw.githubusercontent.com/Free-TV/IPTV/master/playlists/playlist_italy.m3u8"
    Private Const TIMEOUT_RETE As Integer = 20000
    Private Const TIMEOUT_VERIFICA As Integer = 8000
    Private Const DIMENSIONE_BATCH As Integer = 6
    Private Const MASSIMO_CANDIDATI_PER_CANALE As Integer = 6

    Public Const UA_HBBTV As String = "HbbTV/1.6.1"
    Public Const UA_BROWSER As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    Public Const REF_MEDIASET As String = "https://mediasetinfinity.mediaset.it/"
    Public Const REF_SKY As String = "https://tv8.it/"
    Public Const REF_DISCOVERY As String = "https://www.nove.it/"

    Private Shared ReadOnly CanaliFamosi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"rete4.it", "Mediaset"}, {"canale5.it", "Mediaset"}, {"italia1.it", "Mediaset"}, {"italia2.it", "Mediaset"},
        {"la5.it", "Mediaset"}, {"iris.it", "Mediaset"}, {"cine34.it", "Mediaset"}, {"focus.it", "Mediaset"},
        {"topcrime.it", "Mediaset"}, {"boing.it", "Mediaset"}, {"cartoonito.it", "Mediaset"},
        {"mediasetextra.it", "Mediaset"}, {"tgcom24.it", "Mediaset"}, {"20mediaset.it", "Mediaset"},
        {"twentyseven.it", "Mediaset"},
        {"la7.it", "La7"}, {"la7d.it", "La7"},
        {"tv8.it", "Sky"}, {"cielotv.it", "Sky"}, {"skytg24.it", "Sky"},
        {"nove.it", "Discovery"}, {"realtime.it", "Discovery"}, {"warnertv.it", "Discovery"},
        {"giallotv.it", "Discovery"}, {"k2.it", "Discovery"}, {"frisbee.it", "Discovery"}, {"dmax.it", "Discovery"}
    }

    Private Shared ReadOnly IndirizziManuali As New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase) From {
        {"la7.it", New String() {
            "https://d15umi5iaezxgx.cloudfront.net/LA7/CLN/HLS-B/Live_1280x720_.m3u8",
            "https://d15umi5iaezxgx.cloudfront.net/LA7/CLN/HLS/Live.m3u8",
            "https://d15umi5iaezxgx.cloudfront.net/LA7/CLN/HLS-B/Live.m3u8"
        }},
        {"la7d.it", New String() {
            "https://d15umi5iaezxgx.cloudfront.net/LA7D/CLN/HLS-B/Live_1280x720_.m3u8",
            "https://d15umi5iaezxgx.cloudfront.net/LA7D/CLN/HLS-B/Live.m3u8"
        }}
    }

    Private Shared ReadOnly AliasTvgId As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"sky.tg24.it", "skytg24.it"},
        {"27twentyseven.it", "twentyseven.it"},
        {"cielo.it", "cielotv.it"},
        {"giallo.it", "giallotv.it"},
        {"warner.tv.it", "warnertv.it"}
    }

#End Region

#Region "Singleton"

    Public Shared Function GetInstance() As TvCanaliDb
        Try
            If _istanza Is Nothing Then
                SyncLock _lock
                    If _istanza Is Nothing Then
                        _istanza = New TvCanaliDb()
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
            ServicePointManager.Expect100Continue = False
            ServicePointManager.DefaultConnectionLimit = 32
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.New servicepoint: " & ex.ToString())
        End Try

        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType) Or CType(12288, SecurityProtocolType)
        Catch
            Try
                ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            Catch ex2 As Exception
                _logger.ScriviLog("TvCanaliDb.New tls: " & ex2.ToString())
            End Try
        End Try

        Try
            _cartellaDati = RisolviCartellaTv()
            If Not Directory.Exists(_cartellaDati) Then
                Directory.CreateDirectory(_cartellaDati)
            End If
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.New cartella: " & ex.ToString())
            _cartellaDati = Path.GetTempPath()
        End Try

        Try
            _fileDb = Path.Combine(_cartellaDati, "canali_cache.db")
            _stringaConnessione = "Data Source=" & _fileDb & ";Version=3;"
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.New percorso db: " & ex.ToString())
        End Try

        InizializzaDatabase()
    End Sub

    Private Shared Function RisolviCartellaTv() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("tv")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Try
            Return Path.Combine(Application.StartupPath, "dati", "tv")
        Catch
            Return Path.GetTempPath()
        End Try
    End Function

    Private Sub InizializzaDatabase()
        Try
            If String.IsNullOrWhiteSpace(_fileDb) Then Return
            If Not File.Exists(_fileDb) Then
                SQLiteConnection.CreateFile(_fileDb)
            End If

            Using connessione As New SQLiteConnection(_stringaConnessione)
                connessione.Open()

                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Canali (Id TEXT PRIMARY KEY, TvgId TEXT, Numero INTEGER, Nome TEXT, Gruppo TEXT, IndiceAttivo INTEGER, CandidatiJson TEXT)", connessione)
                    comando.ExecuteNonQuery()
                End Using

                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS Meta (Chiave TEXT PRIMARY KEY, Valore TEXT)", connessione)
                    comando.ExecuteNonQuery()
                End Using

                MigraSchemaCanali(connessione)

                Try
                    WGDatabase.AssicuraColonnaChiave(connessione, "Canali")
                Catch exChiave As Exception
                    _logger.ScriviLog("TvCanaliDb.InizializzaDatabase AssicuraColonnaChiave: " & exChiave.ToString())
                End Try
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.InizializzaDatabase: " & ex.ToString())
        End Try
    End Sub

    Private Sub MigraSchemaCanali(connessione As SQLiteConnection)
        Try
            Dim colonneEsistenti As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

            Using comando As New SQLiteCommand("PRAGMA table_info(Canali)", connessione)
                Using lettore As SQLiteDataReader = comando.ExecuteReader()
                    While lettore.Read()
                        Dim nomeColonna As String = Convert.ToString(lettore("name"))
                        If Not String.IsNullOrWhiteSpace(nomeColonna) Then
                            colonneEsistenti(nomeColonna) = True
                        End If
                    End While
                End Using
            End Using

            Dim colonneRichieste As New Dictionary(Of String, String) From {
                {"TvgId", "TEXT"},
                {"Numero", "INTEGER"},
                {"Nome", "TEXT"},
                {"Gruppo", "TEXT"},
                {"IndiceAttivo", "INTEGER"},
                {"CandidatiJson", "TEXT"}
            }

            For Each coppia As KeyValuePair(Of String, String) In colonneRichieste
                Try
                    If Not colonneEsistenti.ContainsKey(coppia.Key) Then
                        Using comandoAlter As New SQLiteCommand("ALTER TABLE Canali ADD COLUMN " & coppia.Key & " " & coppia.Value, connessione)
                            comandoAlter.ExecuteNonQuery()
                        End Using
                    End If
                Catch exColonna As Exception
                    _logger.ScriviLog("TvCanaliDb.MigraSchemaCanali colonna " & coppia.Key & ": " & exColonna.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.MigraSchemaCanali: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Proprieta"

    Public ReadOnly Property IsCaricato As Boolean
        Get
            Try
                Return _caricato AndAlso _canali.Count > 0
            Catch
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property NumeroCanali As Integer
        Get
            Try
                Return _canali.Count
            Catch
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property NumeroFunzionanti As Integer
        Get
            Try
                Dim n As Integer = 0
                For i As Integer = 0 To _canali.Count - 1
                    If _canali(i).Verificato AndAlso _canali(i).Funzionante Then n = n + 1
                Next
                Return n
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

    Public ReadOnly Property FonteAttuale As String
        Get
            Return _fonteAttuale
        End Get
    End Property

    Public ReadOnly Property VerificaEseguita As Boolean
        Get
            Return _verificaEseguita
        End Get
    End Property

    Public ReadOnly Property VerificaInCorso As Boolean
        Get
            Return _verificaInCorso
        End Get
    End Property

#End Region

#Region "Caricamento"

    Public Function Carica(Optional ignorato As Boolean = False) As Boolean
        SyncLock _lock
            Try
                Dim datiPerCanale As New Dictionary(Of String, CanaleTv)(StringComparer.OrdinalIgnoreCase)
                Dim fontiRiuscite As New List(Of String)

                UniscoFonte(datiPerCanale, URL_TUNDRAK, "Tundrak/IPTV-Italia", fontiRiuscite)
                UniscoFonte(datiPerCanale, URL_CHILLIPTV, "chillFil/chillIPTV", fontiRiuscite)
                UniscoFonte(datiPerCanale, URL_IPTVORG, "iptv-org", fontiRiuscite)
                UniscoFonte(datiPerCanale, URL_FREETV, "Free-TV/IPTV", fontiRiuscite)
                AggiungiIndirizziManuali(datiPerCanale)

                If datiPerCanale.Count = 0 Then

                    Dim elencoCache As List(Of CanaleTv) = CaricaCache()
                    If elencoCache Is Nothing OrElse elencoCache.Count = 0 Then
                        Return False
                    End If

                    _canali = elencoCache
                    _caricato = True
                    _verificaEseguita = False
                    _fonteAttuale = "cache locale (" & LeggiMeta("fonte") & ")"
                    _ultimoAggiornamento = LeggiDataMeta()
                    Return True
                End If

                Dim elenco As New List(Of CanaleTv)
                For Each coppia As KeyValuePair(Of String, CanaleTv) In datiPerCanale
                    elenco.Add(coppia.Value)
                Next

                _canali = elenco
                _caricato = True
                _ultimoAggiornamento = DateTime.Now
                _verificaEseguita = False
                _fonteAttuale = String.Join(" + ", fontiRiuscite.ToArray())

                SalvaCache(_canali, _fonteAttuale, _ultimoAggiornamento)

                Return True
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.Carica: " & ex.ToString())
                Return False
            End Try
        End SyncLock
    End Function

    Public Function CaricaAsync(Optional ignorato As Boolean = False) As Task(Of Boolean)
        Try
            Return Task.Run(New Func(Of Boolean)(AddressOf CaricaSenzaParametri))
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.CaricaAsync: " & ex.ToString())
            Return Task.FromResult(False)
        End Try
    End Function

    Private Function CaricaSenzaParametri() As Boolean
        Try
            Return Carica()
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.CaricaSenzaParametri: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub UniscoFonte(datiPerCanale As Dictionary(Of String, CanaleTv), url As String, nomeFonte As String, fontiRiuscite As List(Of String))
        Try
            Dim contenuto As String = ScaricaTesto(url)
            If String.IsNullOrWhiteSpace(contenuto) Then
                Return
            End If

            Dim candidatiTrovati As Integer = FondiM3uInDizionario(contenuto, nomeFonte, datiPerCanale)
            If candidatiTrovati > 0 Then
                fontiRiuscite.Add(nomeFonte)
            End If
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.UniscoFonte (" & nomeFonte & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiIndirizziManuali(datiPerCanale As Dictionary(Of String, CanaleTv))
        Try
            For Each coppia As KeyValuePair(Of String, String()) In IndirizziManuali
                Try
                    Dim tvgId As String = coppia.Key
                    If Not datiPerCanale.ContainsKey(tvgId) Then Continue For

                    Dim canale As CanaleTv = datiPerCanale(tvgId)
                    For i As Integer = 0 To coppia.Value.Length - 1
                        AggiungiCandidatoSeNuovo(canale, coppia.Value(i), UA_BROWSER, "", "manuale")
                    Next
                Catch exCanale As Exception
                    _logger.ScriviLog("TvCanaliDb.AggiungiIndirizziManuali canale: " & exCanale.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.AggiungiIndirizziManuali: " & ex.ToString())
        End Try
    End Sub

    Private Function ScaricaTesto(indirizzo As String) As String
        Dim risposta As HttpWebResponse = Nothing
        Try
            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_RETE
            richiesta.ReadWriteTimeout = TIMEOUT_RETE
            richiesta.UserAgent = UA_BROWSER
            richiesta.Accept = "*/*"
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
            _logger.ScriviLog("TvCanaliDb.ScaricaTesto (" & indirizzo & "): " & exWeb.Message)
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.ScaricaTesto: " & ex.ToString())
            Return Nothing
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                _logger.ScriviLog("TvCanaliDb.ScaricaTesto chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Function FondiM3uInDizionario(contenuto As String, nomeFonte As String, datiPerCanale As Dictionary(Of String, CanaleTv)) As Integer
        Dim aggiunti As Integer = 0
        Try
            If String.IsNullOrWhiteSpace(contenuto) Then Return 0

            Dim separatori As String() = New String() {vbCrLf, vbLf, vbCr}
            Dim righe As String() = contenuto.Split(separatori, StringSplitOptions.None)

            Dim nomeCorrente As String = ""
            Dim tvgIdCorrente As String = ""
            Dim numeroCorrente As Integer = 0
            Dim userAgentCorrente As String = ""

            For r As Integer = 0 To righe.Length - 1
                Dim riga As String = righe(r).Trim()
                If riga.Length = 0 Then Continue For

                If riga.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase) Then
                    nomeCorrente = ""
                    tvgIdCorrente = ""
                    numeroCorrente = 0
                    userAgentCorrente = ""

                    Dim mId As Match = Regex.Match(riga, "tvg-id=""([^""]*)""", RegexOptions.IgnoreCase)
                    If mId.Success Then tvgIdCorrente = mId.Groups(1).Value
                    If Not String.IsNullOrWhiteSpace(tvgIdCorrente) AndAlso AliasTvgId.ContainsKey(tvgIdCorrente) Then
                        tvgIdCorrente = AliasTvgId(tvgIdCorrente)
                    End If

                    Dim mChno As Match = Regex.Match(riga, "tvg-chno=""([^""]*)""", RegexOptions.IgnoreCase)
                    If mChno.Success Then Integer.TryParse(mChno.Groups(1).Value, numeroCorrente)

                    Dim virgola As Integer = riga.LastIndexOf(","c)
                    If virgola >= 0 AndAlso virgola < riga.Length - 1 Then
                        nomeCorrente = riga.Substring(virgola + 1).Trim()
                    End If

                ElseIf riga.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase) Then
                    Dim low As String = riga.ToLowerInvariant()
                    If low.Contains("http-user-agent=") Then
                        Dim pos As Integer = riga.IndexOf("="c)
                        If pos > 0 Then userAgentCorrente = riga.Substring(pos + 1).Trim()
                    End If

                ElseIf Not riga.StartsWith("#") Then
                    Try
                        If Not String.IsNullOrWhiteSpace(tvgIdCorrente) AndAlso CanaliFamosi.ContainsKey(tvgIdCorrente) Then
                            Dim chiave As String = tvgIdCorrente.ToLowerInvariant()

                            If Not datiPerCanale.ContainsKey(chiave) Then
                                Dim nuovo As New CanaleTv()
                                nuovo.Id = "tv_" & chiave.Replace(".", "_").Replace("+", "plus")
                                nuovo.TvgId = chiave
                                nuovo.Gruppo = CanaliFamosi(chiave)
                                datiPerCanale(chiave) = nuovo
                            End If

                            Dim canale As CanaleTv = datiPerCanale(chiave)

                            If canale.Numero = 0 AndAlso numeroCorrente > 0 Then canale.Numero = numeroCorrente
                            If String.IsNullOrWhiteSpace(canale.Nome) Then
                                canale.Nome = nomeCorrente
                                If String.IsNullOrWhiteSpace(canale.Nome) Then canale.Nome = tvgIdCorrente
                            End If

                            Dim agenteUsato As String = UA_BROWSER
                            If Not String.IsNullOrWhiteSpace(userAgentCorrente) Then agenteUsato = userAgentCorrente

                            Dim refererUsato As String = ""
                            If String.Equals(canale.Gruppo, "Mediaset", StringComparison.OrdinalIgnoreCase) Then
                                refererUsato = REF_MEDIASET
                            ElseIf String.Equals(canale.Gruppo, "Sky", StringComparison.OrdinalIgnoreCase) Then
                                refererUsato = REF_SKY
                            ElseIf String.Equals(canale.Gruppo, "Discovery", StringComparison.OrdinalIgnoreCase) Then
                                refererUsato = REF_DISCOVERY
                            End If

                            If AggiungiCandidatoSeNuovo(canale, riga, agenteUsato, refererUsato, nomeFonte) Then
                                aggiunti = aggiunti + 1
                            End If
                        End If
                    Catch exRiga As Exception
                        _logger.ScriviLog("TvCanaliDb.FondiM3uInDizionario riga: " & exRiga.Message)
                    End Try

                    nomeCorrente = ""
                    tvgIdCorrente = ""
                    numeroCorrente = 0
                    userAgentCorrente = ""
                End If
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.FondiM3uInDizionario: " & ex.ToString())
        End Try
        Return aggiunti
    End Function

    Private Function AggiungiCandidatoSeNuovo(canale As CanaleTv, url As String, userAgent As String, referer As String, fonte As String) As Boolean
        Try
            If canale Is Nothing OrElse String.IsNullOrWhiteSpace(url) Then Return False
            If canale.Candidati.Count >= MASSIMO_CANDIDATI_PER_CANALE Then Return False

            Dim urlPulito As String = url.Trim()
            For i As Integer = 0 To canale.Candidati.Count - 1
                If String.Equals(canale.Candidati(i).Url, urlPulito, StringComparison.OrdinalIgnoreCase) Then Return False
            Next

            Dim c As New CandidatoStream()
            c.Url = urlPulito
            c.UserAgent = userAgent
            c.Referer = referer
            c.Fonte = fonte
            canale.Candidati.Add(c)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.AggiungiCandidatoSeNuovo: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

#Region "Cache locale"

    Private Sub SalvaCache(elenco As List(Of CanaleTv), fonte As String, dataAggiornamento As DateTime)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return
            If String.IsNullOrWhiteSpace(_stringaConnessione) Then Return

            Using connessione As New SQLiteConnection(_stringaConnessione)
                connessione.Open()

                Using transazione As SQLiteTransaction = connessione.BeginTransaction()
                    Try
                        Dim chiaviWGEsistenti As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                        Using comandoChiavi As New SQLiteCommand("SELECT Id, ChiaveWG FROM Canali", connessione, transazione)
                            Using lettoreChiavi As SQLiteDataReader = comandoChiavi.ExecuteReader()
                                While lettoreChiavi.Read()
                                    Try
                                        Dim idEsistente As String = Convert.ToString(lettoreChiavi("Id"))
                                        Dim chiaveWGEsistente As String = Convert.ToString(lettoreChiavi("ChiaveWG"))
                                        If Not String.IsNullOrWhiteSpace(idEsistente) AndAlso Not String.IsNullOrWhiteSpace(chiaveWGEsistente) Then
                                            chiaviWGEsistenti(idEsistente) = chiaveWGEsistente
                                        End If
                                    Catch exRigaChiave As Exception
                                        _logger.ScriviLog("TvCanaliDb.SalvaCache lettura chiavi: " & exRigaChiave.Message)
                                    End Try
                                End While
                            End Using
                        End Using

                        Using comandoSvuota As New SQLiteCommand("DELETE FROM Canali", connessione, transazione)
                            comandoSvuota.ExecuteNonQuery()
                        End Using

                        For i As Integer = 0 To elenco.Count - 1
                            Dim c As CanaleTv = elenco(i)
                            Dim candidatiJson As String = JsonConvert.SerializeObject(c.Candidati)

                            Dim chiaveWGRiga As String = ""
                            If Not chiaviWGEsistenti.TryGetValue(c.Id, chiaveWGRiga) OrElse String.IsNullOrWhiteSpace(chiaveWGRiga) Then
                                chiaveWGRiga = WGDatabase.GeneraChiave()
                            End If

                            Using comandoInserisci As New SQLiteCommand("INSERT INTO Canali (Id, TvgId, Numero, Nome, Gruppo, IndiceAttivo, CandidatiJson, ChiaveWG) VALUES (@id, @tvgid, @numero, @nome, @gruppo, @indice, @candidati, @chiavewg)", connessione, transazione)
                                comandoInserisci.Parameters.AddWithValue("@id", c.Id)
                                comandoInserisci.Parameters.AddWithValue("@tvgid", c.TvgId)
                                comandoInserisci.Parameters.AddWithValue("@numero", c.Numero)
                                comandoInserisci.Parameters.AddWithValue("@nome", c.Nome)
                                comandoInserisci.Parameters.AddWithValue("@gruppo", c.Gruppo)
                                comandoInserisci.Parameters.AddWithValue("@indice", c.IndiceAttivo)
                                comandoInserisci.Parameters.AddWithValue("@candidati", candidatiJson)
                                comandoInserisci.Parameters.AddWithValue("@chiavewg", chiaveWGRiga)
                                comandoInserisci.ExecuteNonQuery()
                            End Using
                        Next

                        Using comandoFonte As New SQLiteCommand("INSERT OR REPLACE INTO Meta (Chiave, Valore) VALUES ('fonte', @valore)", connessione, transazione)
                            comandoFonte.Parameters.AddWithValue("@valore", fonte)
                            comandoFonte.ExecuteNonQuery()
                        End Using

                        Using comandoData As New SQLiteCommand("INSERT OR REPLACE INTO Meta (Chiave, Valore) VALUES ('data_aggiornamento', @valore)", connessione, transazione)
                            comandoData.Parameters.AddWithValue("@valore", dataAggiornamento.ToString("o"))
                            comandoData.ExecuteNonQuery()
                        End Using

                        transazione.Commit()
                    Catch exInterno As Exception
                        _logger.ScriviLog("TvCanaliDb.SalvaCache transazione: " & exInterno.ToString())
                        Try
                            transazione.Rollback()
                        Catch exRollback As Exception
                            _logger.ScriviLog("TvCanaliDb.SalvaCache rollback: " & exRollback.Message)
                        End Try
                    End Try
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.SalvaCache: " & ex.ToString())
        End Try
    End Sub

    Private Function CaricaCache() As List(Of CanaleTv)
        Dim risultato As New List(Of CanaleTv)
        Try
            If String.IsNullOrWhiteSpace(_stringaConnessione) Then Return risultato

            Using connessione As New SQLiteConnection(_stringaConnessione)
                connessione.Open()

                Using comando As New SQLiteCommand("SELECT Id, TvgId, Numero, Nome, Gruppo, IndiceAttivo, CandidatiJson FROM Canali", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim c As New CanaleTv()
                                c.Id = Convert.ToString(lettore("Id"))
                                c.TvgId = Convert.ToString(lettore("TvgId"))
                                c.Numero = Convert.ToInt32(lettore("Numero"))
                                c.Nome = Convert.ToString(lettore("Nome"))
                                c.Gruppo = Convert.ToString(lettore("Gruppo"))
                                c.IndiceAttivo = Convert.ToInt32(lettore("IndiceAttivo"))

                                Dim json As String = Convert.ToString(lettore("CandidatiJson"))
                                If Not String.IsNullOrWhiteSpace(json) Then
                                    Dim candidati As List(Of CandidatoStream) = JsonConvert.DeserializeObject(Of List(Of CandidatoStream))(json)
                                    If candidati IsNot Nothing Then c.Candidati = candidati
                                End If

                                If c.Candidati.Count > 0 Then risultato.Add(c)
                            Catch exRiga As Exception
                                _logger.ScriviLog("TvCanaliDb.CaricaCache riga: " & exRiga.Message)
                            End Try
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.CaricaCache: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function LeggiMeta(chiave As String) As String
        Try
            If String.IsNullOrWhiteSpace(_stringaConnessione) Then Return ""

            Using connessione As New SQLiteConnection(_stringaConnessione)
                connessione.Open()

                Using comando As New SQLiteCommand("SELECT Valore FROM Meta WHERE Chiave = @chiave", connessione)
                    comando.Parameters.AddWithValue("@chiave", chiave)
                    Dim valore As Object = comando.ExecuteScalar()
                    If valore Is Nothing Then Return ""
                    Return Convert.ToString(valore)
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.LeggiMeta (" & chiave & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function LeggiDataMeta() As DateTime
        Try
            Dim testo As String = LeggiMeta("data_aggiornamento")
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Return DateTime.Parse(testo, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind)
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.LeggiDataMeta: " & ex.Message)
            Return DateTime.MinValue
        End Try
    End Function

#End Region

#Region "Url riproduzione"

    Public Async Function OttieniUrlRiproduzioneAsync(canale As CanaleTv) As Task(Of CandidatoStream)
        Try
            If canale Is Nothing Then Return Nothing
            Return canale.CandidatoCorrente
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.OttieniUrlRiproduzioneAsync: " & ex.ToString())
            Try
                If canale Is Nothing Then Return Nothing
                Return canale.CandidatoCorrente
            Catch
                Return Nothing
            End Try
        End Try
    End Function

#End Region

#Region "Verifica canali"

    Public Async Function VerificaTuttiAsync(Optional ignorato As Boolean = False) As Task(Of Integer)
        Try
            If _verificaInCorso Then Return NumeroFunzionanti
            _verificaInCorso = True

            Dim daFare As New List(Of CanaleTv)
            For i As Integer = 0 To _canali.Count - 1
                If _canali(i).Candidati.Count = 0 Then Continue For
                daFare.Add(_canali(i))
            Next

            Dim indice As Integer = 0

            While indice < daFare.Count
                Dim lotto As New List(Of Task)
                Dim fine As Integer = indice + DIMENSIONE_BATCH - 1
                If fine > daFare.Count - 1 Then fine = daFare.Count - 1

                For k As Integer = indice To fine
                    lotto.Add(VerificaCanaleAsync(daFare(k)))
                Next

                Try
                    Await Task.WhenAll(lotto.ToArray()).ConfigureAwait(False)
                Catch exLotto As Exception
                    _logger.ScriviLog("TvCanaliDb.VerificaTuttiAsync lotto: " & exLotto.Message)
                End Try

                indice = fine + 1

                Try
                    RaiseEvent ProgressoVerifica(indice, daFare.Count)
                Catch exEvento As Exception
                    _logger.ScriviLog("TvCanaliDb.VerificaTuttiAsync evento: " & exEvento.Message)
                End Try
            End While

            _verificaEseguita = True

            SalvaCache(_canali, _fonteAttuale, _ultimoAggiornamento)

            Return NumeroFunzionanti
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.VerificaTuttiAsync: " & ex.ToString())
            Return 0
        Finally
            _verificaInCorso = False
        End Try
    End Function

    Public Async Function VerificaCanaleAsync(canale As CanaleTv) As Task
        Try
            If canale Is Nothing OrElse canale.Candidati.Count = 0 Then Return

            canale.Verificato = True

            Dim indiceIniziale As Integer = canale.IndiceAttivo
            If indiceIniziale < 0 OrElse indiceIniziale >= canale.Candidati.Count Then indiceIniziale = 0

            Dim candidatoAttivo As CandidatoStream = canale.Candidati(indiceIniziale)
            If Await ProvaIndirizzoAsync(candidatoAttivo).ConfigureAwait(False) Then
                canale.IndiceAttivo = indiceIniziale
                canale.Funzionante = True
                Return
            End If

            For i As Integer = 0 To canale.Candidati.Count - 1
                If i = indiceIniziale Then Continue For
                Try
                    If Await ProvaIndirizzoAsync(canale.Candidati(i)).ConfigureAwait(False) Then
                        canale.IndiceAttivo = i
                        canale.Funzionante = True
                        Return
                    End If
                Catch exCandidato As Exception
                    _logger.ScriviLog("TvCanaliDb.VerificaCanaleAsync candidato: " & exCandidato.Message)
                End Try
            Next

            canale.Funzionante = False
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.VerificaCanaleAsync: " & ex.ToString())
        End Try
    End Function

    Private Async Function ProvaIndirizzoAsync(candidato As CandidatoStream) As Task(Of Boolean)
        Dim risposta As HttpWebResponse = Nothing
        Try
            If candidato Is Nothing OrElse String.IsNullOrWhiteSpace(candidato.Url) Then Return False

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(candidato.Url.Trim()), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.AllowAutoRedirect = True
            richiesta.MaximumAutomaticRedirections = 8
            richiesta.Timeout = TIMEOUT_VERIFICA
            richiesta.ReadWriteTimeout = TIMEOUT_VERIFICA
            richiesta.KeepAlive = False
            richiesta.Accept = "*/*"
            richiesta.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate

            If Not String.IsNullOrWhiteSpace(candidato.UserAgent) Then
                richiesta.UserAgent = candidato.UserAgent
            Else
                richiesta.UserAgent = UA_BROWSER
            End If

            If Not String.IsNullOrWhiteSpace(candidato.Referer) Then
                richiesta.Referer = candidato.Referer
            End If

            risposta = CType(Await richiesta.GetResponseAsync().ConfigureAwait(False), HttpWebResponse)

            If CInt(risposta.StatusCode) <> 200 Then Return False

            Dim testo As String = Nothing
            Using flusso As Stream = risposta.GetResponseStream()
                If flusso Is Nothing Then Return False
                Using lettore As New StreamReader(flusso, Encoding.UTF8)
                    Dim buffer(4095) As Char
                    Dim letti As Integer = lettore.Read(buffer, 0, buffer.Length)
                    If letti > 0 Then testo = New String(buffer, 0, letti)
                End Using
            End Using

            If String.IsNullOrWhiteSpace(testo) Then Return False

            Dim t As String = testo.TrimStart()
            If t.StartsWith("#EXTM3U") Then Return True
            If t.IndexOf("<MPD", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True

            Return False
        Catch exWeb As WebException
            _logger.ScriviLog("TvCanaliDb.ProvaIndirizzoAsync (" & candidato.Url & "): " & exWeb.Message)
            Return False
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.ProvaIndirizzoAsync: " & ex.ToString())
            Return False
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                _logger.ScriviLog("TvCanaliDb.ProvaIndirizzoAsync chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Public Sub AzzeraVerifica()
        Try
            For i As Integer = 0 To _canali.Count - 1
                _canali(i).Verificato = False
                _canali(i).Funzionante = False
            Next
            _verificaEseguita = False
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.AzzeraVerifica: " & ex.ToString())
        End Try
    End Sub

    Public Function OttieniCanaliNonFunzionanti() As List(Of CanaleTv)
        SyncLock _lock
            Dim risultato As New List(Of CanaleTv)
            Try
                For i As Integer = 0 To _canali.Count - 1
                    Dim c As CanaleTv = _canali(i)
                    If c.Verificato AndAlso Not c.Funzionante Then risultato.Add(c)
                Next
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.OttieniCanaliNonFunzionanti: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

#End Region

#Region "Interrogazioni"

    Private Function CanaleAmmesso(c As CanaleTv) As Boolean
        Try
            If c Is Nothing Then Return False
            If c.Candidati Is Nothing OrElse c.Candidati.Count = 0 Then Return False
            If Not _verificaEseguita Then Return True
            If Not c.Verificato Then Return True
            Return c.Funzionante
        Catch ex As Exception
            _logger.ScriviLog("TvCanaliDb.CanaleAmmesso: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniTuttiICanali() As List(Of CanaleTv)
        SyncLock _lock
            Dim risultato As New List(Of CanaleTv)
            Try
                For i As Integer = 0 To _canali.Count - 1
                    If Not CanaleAmmesso(_canali(i)) Then Continue For
                    risultato.Add(_canali(i))
                Next
                risultato.Sort(New ConfrontoCanale())
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.OttieniTuttiICanali: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

    Public Function TrovaPerId(id As String) As CanaleTv
        SyncLock _lock
            Try
                If String.IsNullOrWhiteSpace(id) Then Return Nothing
                For i As Integer = 0 To _canali.Count - 1
                    If String.Equals(_canali(i).Id, id, StringComparison.OrdinalIgnoreCase) Then Return _canali(i)
                Next
                Return Nothing
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.TrovaPerId: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function TrovaPerNumero(numero As Integer) As CanaleTv
        SyncLock _lock
            Try
                For i As Integer = 0 To _canali.Count - 1
                    If _canali(i).Numero = numero Then Return _canali(i)
                Next
                Return Nothing
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.TrovaPerNumero: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function Cerca(testo As String) As List(Of CanaleTv)
        SyncLock _lock
            Dim risultato As New List(Of CanaleTv)
            Try
                If String.IsNullOrWhiteSpace(testo) Then Return risultato
                Dim q As String = testo.Trim()

                For i As Integer = 0 To _canali.Count - 1
                    Dim c As CanaleTv = _canali(i)
                    If Not CanaleAmmesso(c) Then Continue For
                    If c.Nome.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 Then Continue For
                    risultato.Add(c)
                Next

                risultato.Sort(New ConfrontoCanale())
            Catch ex As Exception
                _logger.ScriviLog("TvCanaliDb.Cerca: " & ex.ToString())
            End Try
            Return risultato
        End SyncLock
    End Function

#End Region

End Class
