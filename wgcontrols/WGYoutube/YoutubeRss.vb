' ============================================================================================
' YoutubeRss - gli ultimi video di un canale dal feed RSS di YouTube
' ============================================================================================
'
' Ogni canale ha un indirizzo pubblico (https://www.youtube.com/feeds/videos.xml?channel_id=UC...) con i suoi
' ultimi 15 video: titolo, data di pubblicazione esatta, visualizzazioni e se è uno Short. Risponde in meno di
' mezzo secondo, non vuole un account e non ha i limiti delle pagine di YouTube. Per controllare se ci sono
' nuovi video è molto più veloce di yt-dlp, che per ogni canale avvia un programma e legge una pagina intera.
' Non c'è la durata: per i nuovi video non serve. Se il feed non risponde chi chiama ripiega su yt-dlp.
'
'   Dim video As List(Of YoutubeSearchResult) = YoutubeRss.Leggi(idCanale)            ' Nothing se non risponde
'   Dim tutti = YoutubeRss.LeggiPiuCanali(elencoDiId)                                  ' tanti canali insieme
' ============================================================================================
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Xml.Linq
Imports wgcontrols

Friend NotInheritable Class YoutubeRss

    Private Shared ReadOnly logger As New Logger()
    Private Shared ReadOnly _client As HttpClient = CreaClient()
    Private Const ParallelismoMassimo As Integer = 8
    Private Const AttesaMassimaSecondi As Integer = 25

    Private Shared ReadOnly Atom As XNamespace = "http://www.w3.org/2005/Atom"
    Private Shared ReadOnly YouTube As XNamespace = "http://www.youtube.com/xml/schemas/2015"
    Private Shared ReadOnly Media As XNamespace = "http://search.yahoo.com/mrss/"

    Private Sub New()
    End Sub

    Private Shared Function CreaClient() As HttpClient
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Catch
        End Try
        Dim client As New HttpClient()
        client.Timeout = TimeSpan.FromSeconds(AttesaMassimaSecondi)
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel")
        Return client
    End Function

    ''' <summary>Vero se è l'identificativo di un canale (UC...), l'unico che ha il feed.</summary>
    Public Shared Function IdValido(idCanale As String) As Boolean
        Return Not String.IsNullOrEmpty(idCanale) AndAlso Regex.IsMatch(idCanale, "^UC[A-Za-z0-9_-]{10,}$")
    End Function

    ''' <summary>Gli ultimi video del canale (al massimo 15); Nothing se il feed non risponde o non è fatto come previsto.</summary>
    Public Shared Async Function LeggiAsync(idCanale As String) As Task(Of List(Of YoutubeSearchResult))
        Try
            If Not IdValido(idCanale) Then Return Nothing
            Dim xml As String = Await _client.GetStringAsync("https://www.youtube.com/feeds/videos.xml?channel_id=" & idCanale)
            Return Analizza(xml, idCanale)
        Catch ex As HttpRequestException
            ' Un 404 vuol dire canale chiuso o senza video: si lascia a yt-dlp il verdetto.
            logger.ScriviLog($"YoutubeRss.LeggiAsync {idCanale}: {ex.Message}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"YoutubeRss.LeggiAsync {idCanale}: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Shared Function Analizza(xml As String, idCanale As String) As List(Of YoutubeSearchResult)
        Dim documento As XDocument = XDocument.Parse(xml)
        Dim radice As XElement = documento.Root
        If radice Is Nothing Then Return Nothing
        Dim autore As String = radice.Element(Atom + "author")?.Element(Atom + "name")?.Value
        Dim risultato As New List(Of YoutubeSearchResult)()
        For Each voce As XElement In radice.Elements(Atom + "entry")
            Dim id As String = voce.Element(YouTube + "videoId")?.Value
            Dim titolo As String = voce.Element(Atom + "title")?.Value
            If String.IsNullOrWhiteSpace(id) OrElse String.IsNullOrWhiteSpace(titolo) Then Continue For
            Dim video As New YoutubeSearchResult()
            video.Id = id
            video.Title = titolo
            video.Url = "https://www.youtube.com/watch?v=" & id
            video.ChannelId = idCanale
            video.ChannelName = If(String.IsNullOrWhiteSpace(autore), voce.Element(Atom + "author")?.Element(Atom + "name")?.Value, autore)
            video.ItemType = YoutubeItemType.Video
            Dim pubblicato As DateTimeOffset
            If DateTimeOffset.TryParse(voce.Element(Atom + "published")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, pubblicato) Then
                video.UploadDate = pubblicato.LocalDateTime
            End If
            Dim visualizzazioni As Long
            Dim testoViste As String = voce.Element(Media + "group")?.Element(Media + "community")?.Element(Media + "statistics")?.Attribute("views")?.Value
            If Long.TryParse(testoViste, NumberStyles.Integer, CultureInfo.InvariantCulture, visualizzazioni) Then video.ViewCount = visualizzazioni
            Dim indirizzo As String = voce.Elements(Atom + "link").Select(Function(l) l.Attribute("href")?.Value).FirstOrDefault()
            If Not String.IsNullOrEmpty(indirizzo) Then video.IsShort = indirizzo.Contains("/shorts/")
            risultato.Add(video)
        Next
        Return risultato
    End Function

    ''' <summary>Come LeggiAsync, da qualsiasi thread (non quello dell'interfaccia: aspetta la risposta).</summary>
    Public Shared Function Leggi(idCanale As String) As List(Of YoutubeSearchResult)
        Try
            Return Task.Run(Function() LeggiAsync(idCanale)).GetAwaiter().GetResult()
        Catch ex As Exception
            logger.ScriviLog($"YoutubeRss.Leggi {idCanale}: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Legge i feed di tanti canali insieme (otto alla volta). Per ogni canale l'elenco, o niente se non ha risposto.
    ''' Da qualsiasi thread (non quello dell'interfaccia).
    ''' </summary>
    Public Shared Function LeggiPiuCanali(idCanali As IEnumerable(Of String)) As Dictionary(Of String, List(Of YoutubeSearchResult))
        Dim risultato As New Dictionary(Of String, List(Of YoutubeSearchResult))()
        Try
            Dim ids As List(Of String) = idCanali.Where(Function(i) IdValido(i)).Distinct().ToList()
            If ids.Count = 0 Then Return risultato
            Task.Run(Async Function()
                         Using turno As New SemaphoreSlim(ParallelismoMassimo)
                             Dim lavori As List(Of Task) = ids.Select(Async Function(id As String)
                                                                          Await turno.WaitAsync()
                                                                          Try
                                                                              Dim video As List(Of YoutubeSearchResult) = Await LeggiAsync(id)
                                                                              If video IsNot Nothing Then
                                                                                  SyncLock risultato
                                                                                      risultato(id) = video
                                                                                  End SyncLock
                                                                              End If
                                                                          Finally
                                                                              turno.Release()
                                                                          End Try
                                                                      End Function).Cast(Of Task)().ToList()
                             Await Task.WhenAll(lavori)
                         End Using
                     End Function).GetAwaiter().GetResult()
        Catch ex As Exception
            logger.ScriviLog($"YoutubeRss.LeggiPiuCanali: {ex.Message}")
        End Try
        Return risultato
    End Function

End Class
