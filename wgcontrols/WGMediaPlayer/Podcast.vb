Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Xml
Imports System.Xml.Linq
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>Un errore che si può dire a voce all'utente così com'è.</summary>
Friend Class PodcastEccezione
    Inherits Exception

    Public Sub New(messaggio As String)
        MyBase.New(messaggio)
    End Sub

    Public Sub New(messaggio As String, interna As Exception)
        MyBase.New(messaggio, interna)
    End Sub
End Class

''' <summary>Un podcast: il suo indirizzo (il feed), il titolo e quello che serve a riconoscerlo in un elenco.</summary>
Friend Class PodcastInfo
    Public Property FeedUrl As String = ""
    Public Property Titolo As String = ""
    Public Property Autore As String = ""
    Public Property Descrizione As String = ""
    Public Property Sito As String = ""
    Public Property Immagine As String = ""
    Public Property Categoria As String = ""
    Public Property NumeroEpisodi As Integer = 0

    Public Function Etichetta(Optional seguito As Boolean = False, Optional preferito As Boolean = False) As String
        Dim s As String = Titolo
        If Autore <> "" AndAlso Not String.Equals(Autore, Titolo, StringComparison.OrdinalIgnoreCase) Then s &= ", " & Autore
        If Categoria <> "" Then s &= ", " & Categoria
        If seguito Then s &= ", seguito"
        If preferito Then s &= ", preferito"
        Return s
    End Function
End Class

''' <summary>Un episodio di un podcast.</summary>
Friend Class EpisodioPodcast
    Public Property Guid As String = ""
    Public Property Titolo As String = ""
    Public Property Descrizione As String = ""
    Public Property Url As String = ""
    Public Property Tipo As String = ""
    Public Property Data As DateTime? = Nothing
    Public Property DurataSecondi As Integer = 0
    Public Property Dimensione As Long = 0
    Public Property Numero As Integer = 0
    Public Property Stagione As Integer = 0

    Public Function DataParlata() As String
        If Not Data.HasValue Then Return ""
        Return Data.Value.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"))
    End Function

    Public Function Etichetta(Optional conPodcast As String = "", Optional stato As String = "") As String
        Dim s As String = Titolo
        If conPodcast <> "" Then s &= ", " & conPodcast
        Dim d As String = DataParlata()
        If d <> "" Then s &= ", " & d
        Dim t As String = PodcastRete.DurataParlata(DurataSecondi)
        If t <> "" Then s &= ", " & t
        If stato <> "" Then s &= ", " & stato
        Return s
    End Function
End Class

''' <summary>Un podcast con i suoi episodi, come sta nel feed.</summary>
Friend Class FeedPodcast
    Public Property Info As New PodcastInfo()
    Public Property Episodi As New List(Of EpisodioPodcast)()
End Class

''' <summary>Le scelte dei podcast, nel file di configurazione di WinGabriel.</summary>
Friend Module PodcastImpostazioni

    Friend Const CHIAVE_PAESE As String = "podcast_paese"
    Private ReadOnly _config As New WGConfig()

    Friend Function Paese() As String
        Try
            If _config.EsisteId(CHIAVE_PAESE) Then
                Dim v As String = _config.LeggiValore(CHIAVE_PAESE)
                If Not String.IsNullOrWhiteSpace(v) Then Return v.Trim().ToUpperInvariant()
            End If
        Catch ex As Exception
            MediaUtil.Log("PodcastImpostazioni.Paese: " & ex.ToString())
        End Try
        Return "IT"
    End Function

    Friend Sub ImpostaPaese(codice As String)
        Try
            _config.SalvaValore(CHIAVE_PAESE, codice)
        Catch ex As Exception
            MediaUtil.Log("PodcastImpostazioni.ImpostaPaese: " & ex.ToString())
        End Try
    End Sub

End Module

''' <summary>
''' Ricerca, classifiche e feed dei podcast. La ricerca e le classifiche vengono dal catalogo pubblico di Apple, che non chiede
''' né registrazione né chiavi; gli episodi si leggono direttamente dal feed del podcast, che è quello che pubblica chi lo fa.
''' </summary>
Friend NotInheritable Class PodcastRete

    Private Shared ReadOnly _http As HttpClient = CreaClient()

    Private Sub New()
    End Sub

    Private Shared Function CreaClient() As HttpClient
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Catch
        End Try
        Dim gestore As New HttpClientHandler()
        gestore.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
        Dim c As New HttpClient(gestore)
        c.Timeout = TimeSpan.FromSeconds(60)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (podcast per non vedenti)")
        Return c
    End Function

    ' ---- i paesi e le categorie delle classifiche ---------------------------------------------------

    Friend Shared ReadOnly Paesi As KeyValuePair(Of String, String)() = {
        New KeyValuePair(Of String, String)("IT", "Italia"),
        New KeyValuePair(Of String, String)("CH", "Svizzera"),
        New KeyValuePair(Of String, String)("US", "Stati Uniti"),
        New KeyValuePair(Of String, String)("GB", "Regno Unito"),
        New KeyValuePair(Of String, String)("FR", "Francia"),
        New KeyValuePair(Of String, String)("DE", "Germania"),
        New KeyValuePair(Of String, String)("ES", "Spagna"),
        New KeyValuePair(Of String, String)("PT", "Portogallo"),
        New KeyValuePair(Of String, String)("AT", "Austria"),
        New KeyValuePair(Of String, String)("BE", "Belgio"),
        New KeyValuePair(Of String, String)("NL", "Paesi Bassi"),
        New KeyValuePair(Of String, String)("CA", "Canada"),
        New KeyValuePair(Of String, String)("AU", "Australia"),
        New KeyValuePair(Of String, String)("BR", "Brasile"),
        New KeyValuePair(Of String, String)("MX", "Messico"),
        New KeyValuePair(Of String, String)("AR", "Argentina")}

    Friend Shared ReadOnly Categorie As KeyValuePair(Of Integer, String)() = {
        New KeyValuePair(Of Integer, String)(1301, "Arte e cultura"),
        New KeyValuePair(Of Integer, String)(1321, "Economia"),
        New KeyValuePair(Of Integer, String)(1303, "Umorismo"),
        New KeyValuePair(Of Integer, String)(1304, "Istruzione"),
        New KeyValuePair(Of Integer, String)(1483, "Narrativa"),
        New KeyValuePair(Of Integer, String)(1511, "Governo"),
        New KeyValuePair(Of Integer, String)(1512, "Salute e benessere"),
        New KeyValuePair(Of Integer, String)(1487, "Storia"),
        New KeyValuePair(Of Integer, String)(1305, "Bambini e famiglia"),
        New KeyValuePair(Of Integer, String)(1502, "Tempo libero"),
        New KeyValuePair(Of Integer, String)(1310, "Musica"),
        New KeyValuePair(Of Integer, String)(1489, "Notizie"),
        New KeyValuePair(Of Integer, String)(1314, "Religione e spiritualità"),
        New KeyValuePair(Of Integer, String)(1533, "Scienza"),
        New KeyValuePair(Of Integer, String)(1324, "Società e cultura"),
        New KeyValuePair(Of Integer, String)(1545, "Sport"),
        New KeyValuePair(Of Integer, String)(1318, "Tecnologia"),
        New KeyValuePair(Of Integer, String)(1488, "True crime"),
        New KeyValuePair(Of Integer, String)(1309, "Film e TV")}

    Friend Shared Function NomePaese(codice As String) As String
        For Each p As KeyValuePair(Of String, String) In Paesi
            If String.Equals(p.Key, codice, StringComparison.OrdinalIgnoreCase) Then Return p.Value
        Next
        Return codice
    End Function

    ' ---- rete ---------------------------------------------------------------------------------------

    Private Shared Async Function LeggiByteAsync(indirizzo As String, annulla As CancellationToken) As Task(Of Byte())
        Try
            Using r As HttpResponseMessage = Await _http.GetAsync(indirizzo, annulla).ConfigureAwait(False)
                If Not r.IsSuccessStatusCode Then
                    Throw New PodcastEccezione("Il sito non ha risposto come previsto (" & CInt(r.StatusCode).ToString() & ").")
                End If
                Return Await r.Content.ReadAsByteArrayAsync().ConfigureAwait(False)
            End Using
        Catch ex As PodcastEccezione
            Throw
        Catch ex As OperationCanceledException
            If annulla.IsCancellationRequested Then Throw
            Throw New PodcastEccezione("Il sito non risponde. Riprova tra poco.", ex)
        Catch ex As HttpRequestException
            Throw New PodcastEccezione("Non riesco a collegarmi. Controlla la connessione a internet.", ex)
        End Try
    End Function

    Private Shared Function Testo(t As JToken, nome As String) As String
        If t Is Nothing Then Return ""
        Dim v As JToken = t(nome)
        If v Is Nothing OrElse v.Type = JTokenType.Null Then Return ""
        Return v.ToString()
    End Function

    Private Shared Function DaCatalogo(r As JToken) As PodcastInfo
        Dim feed As String = Testo(r, "feedUrl")
        If feed = "" Then Return Nothing
        Dim p As New PodcastInfo()
        p.FeedUrl = feed
        p.Titolo = Testo(r, "collectionName")
        If p.Titolo = "" Then p.Titolo = Testo(r, "trackName")
        p.Autore = Testo(r, "artistName")
        p.Immagine = Testo(r, "artworkUrl100")
        p.Categoria = Testo(r, "primaryGenreName")
        Dim n As Integer = 0
        Integer.TryParse(Testo(r, "trackCount"), n)
        p.NumeroEpisodi = n
        If p.Titolo = "" Then Return Nothing
        Return p
    End Function

    ''' <summary>I podcast che corrispondono al testo, nel paese scelto.</summary>
    Friend Shared Async Function CercaAsync(cercato As String, paese As String, annulla As CancellationToken) As Task(Of List(Of PodcastInfo))
        Dim indirizzo As String = "https://itunes.apple.com/search?media=podcast&entity=podcast&limit=200&country=" & Uri.EscapeDataString(paese) & "&term=" & Uri.EscapeDataString(cercato)
        Dim dati As Byte() = Await LeggiByteAsync(indirizzo, annulla).ConfigureAwait(False)
        Dim risultati As New List(Of PodcastInfo)()
        Try
            Dim json As JObject = JObject.Parse(Encoding.UTF8.GetString(dati))
            Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each r As JToken In CType(json("results"), JArray)
                Dim p As PodcastInfo = DaCatalogo(r)
                If p IsNot Nothing AndAlso visti.Add(p.FeedUrl) Then risultati.Add(p)
            Next
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            Throw New PodcastEccezione("La risposta della ricerca non si legge. Riprova tra poco.", ex)
        End Try
        Return risultati
    End Function

    ''' <summary>La classifica dei podcast più ascoltati nel paese, in generale (genere 0) o in una categoria.</summary>
    Friend Shared Async Function ClassificaAsync(paese As String, genere As Integer, annulla As CancellationToken) As Task(Of List(Of PodcastInfo))
        Dim indirizzo As String = "https://itunes.apple.com/" & paese.ToLowerInvariant() & "/rss/toppodcasts/limit=200" & If(genere > 0, "/genre=" & genere.ToString(), "") & "/json"
        Dim dati As Byte() = Await LeggiByteAsync(indirizzo, annulla).ConfigureAwait(False)
        Dim ids As New List(Of String)()
        Try
            Dim json As JObject = JObject.Parse(Encoding.UTF8.GetString(dati))
            Dim voci As JToken = json("feed")("entry")
            If voci IsNot Nothing Then
                For Each e As JToken In voci
                    Dim id As String = Testo(e("id")("attributes"), "im:id")
                    If id <> "" Then ids.Add(id)
                Next
            End If
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            Throw New PodcastEccezione("La classifica non si legge. Riprova tra poco.", ex)
        End Try
        Dim risultati As New List(Of PodcastInfo)()
        If ids.Count = 0 Then Return risultati

        ' la classifica non dà l'indirizzo del feed: si chiede al catalogo, tutti insieme
        Dim ricerca As String = "https://itunes.apple.com/lookup?entity=podcast&country=" & Uri.EscapeDataString(paese) & "&id=" & String.Join(",", ids)
        Dim dati2 As Byte() = Await LeggiByteAsync(ricerca, annulla).ConfigureAwait(False)
        Try
            Dim json2 As JObject = JObject.Parse(Encoding.UTF8.GetString(dati2))
            Dim perId As New Dictionary(Of String, PodcastInfo)()
            For Each r As JToken In CType(json2("results"), JArray)
                Dim p As PodcastInfo = DaCatalogo(r)
                Dim id As String = Testo(r, "collectionId")
                If p IsNot Nothing AndAlso id <> "" AndAlso Not perId.ContainsKey(id) Then perId.Add(id, p)
            Next
            For Each id As String In ids
                Dim p As PodcastInfo = Nothing
                If perId.TryGetValue(id, p) Then risultati.Add(p)
            Next
        Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
            Throw New PodcastEccezione("La classifica non si legge. Riprova tra poco.", ex)
        End Try
        Return risultati
    End Function

    ' ---- il feed --------------------------------------------------------------------------------------

    Private Shared ReadOnly NsItunes As XNamespace = "http://www.itunes.com/dtds/podcast-1.0.dtd"

    Private Shared Function Pulisci(html As String) As String
        If String.IsNullOrEmpty(html) Then Return ""
        Dim t As String = Regex.Replace(html, "(?i)<\s*(br|/p|/div|/li)\s*/?>", vbLf)
        t = Regex.Replace(t, "<[^>]+>", "")
        t = WebUtility.HtmlDecode(t)
        t = Regex.Replace(t, "[ \t]+", " ")
        t = Regex.Replace(t, "\s*\n\s*", vbLf & vbLf)
        Return t.Trim()
    End Function

    Private Shared Function Elemento(padre As XElement, nome As XName) As String
        Dim e As XElement = padre.Element(nome)
        If e Is Nothing Then Return ""
        Return e.Value.Trim()
    End Function

    Friend Shared Function DurataDaTesto(testo As String) As Integer
        If String.IsNullOrWhiteSpace(testo) Then Return 0
        Dim t As String = testo.Trim()
        Dim pezzi As String() = t.Split(":"c)
        Dim totale As Double = 0
        Try
            For Each p As String In pezzi
                Dim v As Double
                If Not Double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return 0
                totale = totale * 60 + v
            Next
        Catch
            Return 0
        End Try
        Return CInt(totale)
    End Function

    Private Shared Function DataDaTesto(testo As String) As DateTime?
        If String.IsNullOrWhiteSpace(testo) Then Return Nothing
        Dim d As DateTimeOffset
        If DateTimeOffset.TryParse(testo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, d) Then Return d.LocalDateTime
        ' fusi orari come "EST" o "PST", che .NET non conosce: si toglie e si legge la data
        Dim senzaFuso As String = Regex.Replace(testo.Trim(), "\s+[A-Za-z]{2,4}$", "")
        If DateTimeOffset.TryParse(senzaFuso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, d) Then Return d.LocalDateTime
        Return Nothing
    End Function

    Friend Shared Function DurataParlata(secondi As Integer) As String
        If secondi <= 0 Then Return ""
        If secondi < 60 Then Return secondi.ToString() & If(secondi = 1, " secondo", " secondi")
        Dim minuti As Integer = CInt(Math.Round(secondi / 60.0))
        If minuti < 60 Then Return minuti.ToString() & If(minuti = 1, " minuto", " minuti")
        Dim ore As Integer = minuti \ 60
        Dim resto As Integer = minuti Mod 60
        Dim s As String = ore.ToString() & If(ore = 1, " ora", " ore")
        If resto > 0 Then s &= " " & resto.ToString() & If(resto = 1, " minuto", " minuti")
        Return s
    End Function

    ''' <summary>Legge il feed di un podcast: le informazioni e gli episodi, dal più recente.</summary>
    Friend Shared Async Function LeggiFeedAsync(feedUrl As String, annulla As CancellationToken) As Task(Of FeedPodcast)
        Dim dati As Byte() = Await LeggiByteAsync(feedUrl, annulla).ConfigureAwait(False)
        Return Await Task.Run(Function() AnalizzaFeed(feedUrl, dati), annulla).ConfigureAwait(False)
    End Function

    Private Shared Function AnalizzaFeed(feedUrl As String, dati As Byte()) As FeedPodcast
        Dim documento As XDocument
        Try
            ' alcuni feed hanno spazi o righe vuote prima della dichiarazione xml
            Dim inizio As Integer = 0
            While inizio < dati.Length AndAlso (dati(inizio) = 32 OrElse dati(inizio) = 10 OrElse dati(inizio) = 13 OrElse dati(inizio) = 9)
                inizio += 1
            End While
            Using flusso As New MemoryStream(dati, inizio, dati.Length - inizio)
                Dim impostazioni As New XmlReaderSettings()
                impostazioni.DtdProcessing = DtdProcessing.Ignore
                impostazioni.XmlResolver = Nothing
                impostazioni.CheckCharacters = False
                Using lettore As XmlReader = XmlReader.Create(flusso, impostazioni)
                    documento = XDocument.Load(lettore)
                End Using
            End Using
        Catch ex As Exception
            Throw New PodcastEccezione("Il feed di questo podcast non si legge.", ex)
        End Try

        Dim canale As XElement = If(documento.Root IsNot Nothing, documento.Root.Element("channel"), Nothing)
        If canale Is Nothing Then Throw New PodcastEccezione("Il feed di questo podcast non si legge.")

        Dim feed As New FeedPodcast()
        feed.Info.FeedUrl = feedUrl
        feed.Info.Titolo = Elemento(canale, "title")
        feed.Info.Autore = Elemento(canale, NsItunes + "author")
        feed.Info.Sito = Elemento(canale, "link")
        Dim descrizione As String = Elemento(canale, "description")
        If descrizione = "" Then descrizione = Elemento(canale, NsItunes + "summary")
        feed.Info.Descrizione = Pulisci(descrizione)
        Dim categoria As XElement = canale.Element(NsItunes + "category")
        If categoria IsNot Nothing AndAlso categoria.Attribute("text") IsNot Nothing Then feed.Info.Categoria = categoria.Attribute("text").Value
        Dim immagine As XElement = canale.Element(NsItunes + "image")
        If immagine IsNot Nothing AndAlso immagine.Attribute("href") IsNot Nothing Then feed.Info.Immagine = immagine.Attribute("href").Value

        For Each voce As XElement In canale.Elements("item")
            Dim ep As New EpisodioPodcast()
            Dim enc As XElement = voce.Element("enclosure")
            If enc Is Nothing OrElse enc.Attribute("url") Is Nothing Then Continue For
            ep.Url = enc.Attribute("url").Value.Trim()
            If ep.Url = "" Then Continue For
            If enc.Attribute("type") IsNot Nothing Then ep.Tipo = enc.Attribute("type").Value
            Dim lunghezza As Long = 0
            If enc.Attribute("length") IsNot Nothing Then Long.TryParse(enc.Attribute("length").Value, lunghezza)
            ep.Dimensione = lunghezza
            ep.Titolo = Elemento(voce, "title")
            If ep.Titolo = "" Then ep.Titolo = Elemento(voce, NsItunes + "title")
            If ep.Titolo = "" Then ep.Titolo = "Senza titolo"
            ep.Guid = Elemento(voce, "guid")
            If ep.Guid = "" Then ep.Guid = ep.Url
            Dim d As String = Elemento(voce, "description")
            If d = "" Then d = Elemento(voce, NsItunes + "summary")
            ep.Descrizione = Pulisci(d)
            ep.Data = DataDaTesto(Elemento(voce, "pubDate"))
            ep.DurataSecondi = DurataDaTesto(Elemento(voce, NsItunes + "duration"))
            Integer.TryParse(Elemento(voce, NsItunes + "episode"), ep.Numero)
            Integer.TryParse(Elemento(voce, NsItunes + "season"), ep.Stagione)
            feed.Episodi.Add(ep)
        Next
        If feed.Episodi.Count = 0 Then Throw New PodcastEccezione("Questo podcast non ha episodi da ascoltare.")
        feed.Info.NumeroEpisodi = feed.Episodi.Count
        ' dal più recente; senza data si resta nell'ordine del feed
        Dim ordinati As List(Of EpisodioPodcast) = feed.Episodi.Select(Function(e, i) New With {.E = e, .I = i}).
            OrderByDescending(Function(x) If(x.E.Data.HasValue, x.E.Data.Value, DateTime.MinValue)).ThenBy(Function(x) x.I).Select(Function(x) x.E).ToList()
        feed.Episodi = ordinati
        Return feed
    End Function

    ' ---- scaricamento -----------------------------------------------------------------------------------

    Friend Shared Function EstensionePer(ep As EpisodioPodcast) As String
        Try
            Dim e As String = Path.GetExtension(New Uri(ep.Url).AbsolutePath).ToLowerInvariant()
            If e = ".mp3" OrElse e = ".m4a" OrElse e = ".aac" OrElse e = ".ogg" OrElse e = ".opus" OrElse e = ".mp4" OrElse e = ".wav" OrElse e = ".flac" Then Return e
        Catch
        End Try
        Dim t As String = If(ep.Tipo, "").ToLowerInvariant()
        If t.Contains("mp4") OrElse t.Contains("m4a") Then Return If(t.StartsWith("video"), ".mp4", ".m4a")
        If t.Contains("aac") Then Return ".aac"
        If t.Contains("ogg") Then Return ".ogg"
        If t.Contains("opus") Then Return ".opus"
        If t.Contains("wav") Then Return ".wav"
        If t.Contains("flac") Then Return ".flac"
        Return ".mp3"
    End Function

    ''' <summary>Scarica l'episodio nel file indicato. Il file compare solo a scaricamento finito.</summary>
    Friend Shared Async Function ScaricaAsync(indirizzo As String, destinazione As String, annulla As CancellationToken) As Task(Of Long)
        Dim temporaneo As String = destinazione & ".part"
        Try
            Dim cartella As String = Path.GetDirectoryName(destinazione)
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Using r As HttpResponseMessage = Await _http.GetAsync(indirizzo, HttpCompletionOption.ResponseHeadersRead, annulla).ConfigureAwait(False)
                If Not r.IsSuccessStatusCode Then
                    Throw New PodcastEccezione("Il sito non ha dato il file (" & CInt(r.StatusCode).ToString() & ").")
                End If
                Dim tipo As String = If(r.Content.Headers.ContentType IsNot Nothing, r.Content.Headers.ContentType.MediaType, "")
                If tipo.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 OrElse tipo.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Throw New PodcastEccezione("Il sito non ha dato il file audio di questo episodio.")
                End If
                Using sorgente As Stream = Await r.Content.ReadAsStreamAsync().ConfigureAwait(False)
                    Using uscitaFile As FileStream = File.Create(temporaneo)
                        Await sorgente.CopyToAsync(uscitaFile, 81920, annulla).ConfigureAwait(False)
                    End Using
                End Using
            End Using
            If File.Exists(destinazione) Then File.Delete(destinazione)
            File.Move(temporaneo, destinazione)
            Return New FileInfo(destinazione).Length
        Catch ex As PodcastEccezione
            PulisciTemporaneo(temporaneo)
            Throw
        Catch ex As OperationCanceledException
            PulisciTemporaneo(temporaneo)
            If annulla.IsCancellationRequested Then Throw
            Throw New PodcastEccezione("Il sito non risponde. Riprova tra poco.", ex)
        Catch ex As HttpRequestException
            PulisciTemporaneo(temporaneo)
            Throw New PodcastEccezione("Non riesco a collegarmi. Controlla la connessione a internet.", ex)
        Catch ex As IOException
            PulisciTemporaneo(temporaneo)
            Throw New PodcastEccezione("Non è stato possibile salvare il file sul computer.", ex)
        End Try
    End Function

    Private Shared Sub PulisciTemporaneo(percorso As String)
        Try
            If File.Exists(percorso) Then File.Delete(percorso)
        Catch
        End Try
    End Sub

End Class
