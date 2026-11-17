Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports WGDb
Imports wgcontrols

Public NotInheritable Class WGBloccoPubblicita
    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _lockCaricamento As New Object()
    Private Shared _domini As HashSet(Of String) = Nothing
    Private Shared _attivo As Boolean = True
    Private Shared _istanteLetturaAttivo As DateTime = DateTime.MinValue

    Public Const IndirizzoElencoPredefinito As String = "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=nohtml&showintro=0&mimetype=plaintext"

    Private Shared ReadOnly DominiPredefiniti As String() = {
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com", "adservice.google.it",
        "amazon-adsystem.com", "adnxs.com", "adsrvr.org", "rubiconproject.com", "pubmatic.com", "openx.net", "criteo.com",
        "criteo.net", "taboola.com", "taboolasyndication.com", "outbrain.com", "outbrainimg.com", "teads.tv", "smartadserver.com",
        "adform.net", "casalemedia.com", "indexww.com", "3lift.com", "sharethrough.com", "yieldmo.com", "media.net",
        "contextweb.com", "advertising.com", "moatads.com", "doubleverify.com", "adsafeprotected.com", "scorecardresearch.com",
        "quantserve.com", "zedo.com", "revcontent.com", "mgid.com", "propellerads.com", "popads.net", "popcash.net",
        "adsterra.com", "exoclick.com", "juicyads.com", "trafficjunky.net", "onclickads.net", "adcash.com", "hilltopads.net",
        "bidswitch.net", "sonobi.com", "gumgum.com", "lijit.com", "sovrn.com", "spotxchange.com", "spotx.tv",
        "springserve.com", "tremorhub.com", "yieldlab.net", "adkernel.com", "emxdgt.com", "onetag-sys.com", "seedtag.com",
        "richaudience.com", "improvedigital.com", "360yield.com", "adhese.com", "serving-sys.com", "flashtalking.com",
        "innovid.com", "eyeota.net", "bluekai.com", "krxd.net", "exelator.com", "rlcdn.com", "adsymptotic.com", "adroll.com",
        "zemanta.com", "ligatus.com", "plista.com", "adagio.io", "a-mo.net", "aniview.com", "vidoomy.com", "viralize.com",
        "mobfox.com", "inmobi.com", "adition.com", "smaato.net", "33across.com", "adtelligent.com", "betweendigital.com",
        "connatix.com", "primis.tech", "undertone.com", "adpone.com", "ad-score.com", "admixer.net", "adotmob.com",
        "bidtheatre.com", "cootlogix.com", "e-planning.net", "gammaplatform.com", "justpremium.com", "kargo.com",
        "lkqd.net", "mediavine.com", "nativo.com", "nexx360.io", "optad360.io", "outbrain.org", "quantcount.com",
        "rtbhouse.com", "servedby-buysellads.com", "stickyadstv.com", "teads.com", "tribalfusion.com", "yieldlove.com",
        "ads.pubmatic.com", "adservice.google.de", "adservice.google.fr", "adservice.google.es", "pagead2.googlesyndication.com"
    }

    Private Sub New()
    End Sub

    Private Shared Function PercorsoElenco() As String
        Try
            Return WGDatabase.PercorsoFileDati("browser", "pubblicita.txt")
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.PercorsoElenco: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub AssicuraCaricamento()
        Try
            If _domini IsNot Nothing Then Return
            SyncLock _lockCaricamento
                If _domini IsNot Nothing Then Return
                Dim insieme As New HashSet(Of String)(DominiPredefiniti, StringComparer.OrdinalIgnoreCase)
                Try
                    Dim percorso As String = PercorsoElenco()
                    If percorso <> "" AndAlso File.Exists(percorso) Then
                        For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
                            Dim dominio As String = NormalizzaDominio(riga)
                            If dominio <> "" Then insieme.Add(dominio)
                        Next
                    End If
                Catch exFile As Exception
                    _log.ScriviLog("WGBloccoPubblicita.AssicuraCaricamento file: " & exFile.Message)
                End Try
                _domini = insieme
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.AssicuraCaricamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Function NormalizzaDominio(riga As String) As String
        Try
            If String.IsNullOrWhiteSpace(riga) Then Return ""
            Dim testo As String = riga.Trim()
            If testo.StartsWith("#") OrElse testo.StartsWith("!") OrElse testo.StartsWith("[") Then Return ""
            Dim commento As Integer = testo.IndexOf("#"c)
            If commento > 0 Then testo = testo.Substring(0, commento).Trim()
            Dim parti As String() = testo.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            If parti.Length = 0 Then Return ""
            Dim dominio As String = parti(parti.Length - 1).Trim().ToLowerInvariant()
            If dominio.StartsWith("||") Then dominio = dominio.Substring(2)
            dominio = dominio.TrimEnd("^"c, "/"c).TrimStart("."c)
            If dominio = "" OrElse dominio = "localhost" OrElse dominio = "0.0.0.0" OrElse dominio.IndexOf("."c) < 0 Then Return ""
            For Each c As Char In dominio
                If Not (Char.IsLetterOrDigit(c) OrElse c = "."c OrElse c = "-"c OrElse c = "_"c) Then Return ""
            Next
            Return dominio
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.NormalizzaDominio: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared ReadOnly Property NumeroDomini As Integer
        Get
            Try
                AssicuraCaricamento()
                Dim insieme As HashSet(Of String) = _domini
                Return If(insieme Is Nothing, 0, insieme.Count)
            Catch ex As Exception
                _log.ScriviLog("WGBloccoPubblicita.NumeroDomini: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Public Shared Function Attivo() As Boolean
        Try
            If (DateTime.Now - _istanteLetturaAttivo).TotalSeconds > 5 Then
                _attivo = WGImpostazioniBrowser.BloccoPubblicita
                _istanteLetturaAttivo = DateTime.Now
            End If
            Return _attivo
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.Attivo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub AggiornaStato()
        Try
            _istanteLetturaAttivo = DateTime.MinValue
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.AggiornaStato: " & ex.Message)
        End Try
    End Sub

    Public Shared Function NormalizzaSito(testo As String) As String
        Try
            Dim t As String = If(testo, "").Trim().ToLowerInvariant()
            If t = "" Then Return ""
            Dim uri As Uri = Nothing
            If t.Contains("://") AndAlso Uri.TryCreate(t, UriKind.Absolute, uri) Then
                t = uri.Host
            Else
                Dim fine As Integer = t.IndexOfAny(New Char() {"/"c, "?"c, "#"c, ":"c})
                If fine >= 0 Then t = t.Substring(0, fine)
            End If
            t = t.Trim().Trim("."c)
            If t.StartsWith("www.", StringComparison.Ordinal) Then t = t.Substring(4)
            If t = "" OrElse t.IndexOf("."c) < 0 Then Return ""
            For Each c As Char In t
                If Not (Char.IsLetterOrDigit(c) OrElse c = "."c OrElse c = "-"c) Then Return ""
            Next
            Return t
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.NormalizzaSito: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function DominiDaControllare(host As String) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim corrente As String = If(host, "").Trim().ToLowerInvariant().Trim("."c)
            While corrente.IndexOf("."c) > 0
                risultato.Add(corrente)
                corrente = corrente.Substring(corrente.IndexOf("."c) + 1)
            End While
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.DominiDaControllare: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Function SitoEscluso(host As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(host) Then Return False
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio Is Nothing Then Return False
            For Each dominio As String In DominiDaControllare(host)
                If archivio.ValoreSito(dominio, "pubblicita") = "consenti" Then Return True
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.SitoEscluso: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaSitoEscluso(host As String, escluso As Boolean)
        Try
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio Is Nothing OrElse String.IsNullOrWhiteSpace(host) Then Return
            If escluso Then
                Dim sito As String = NormalizzaSito(host)
                If sito = "" Then sito = host.Trim().ToLowerInvariant()
                archivio.ImpostaValoreSito(sito, "pubblicita", "consenti")
            Else
                For Each dominio As String In DominiDaControllare(host)
                    If archivio.ValoreSito(dominio, "pubblicita") <> "" Then archivio.ImpostaValoreSito(dominio, "pubblicita", "")
                Next
            End If
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.ImpostaSitoEscluso: " & ex.Message)
        End Try
    End Sub

    Public Shared Function SitiEsclusi() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio Is Nothing Then Return risultato
            For Each voce As Tuple(Of String, String, String) In archivio.ImpostazioniDeiSiti()
                If voce.Item2 = "pubblicita" AndAlso voce.Item3 = "consenti" AndAlso Not risultato.Contains(voce.Item1) Then risultato.Add(voce.Item1)
            Next
            risultato.Sort(StringComparer.OrdinalIgnoreCase)
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.SitiEsclusi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Function DaBloccare(indirizzoRisorsa As String, hostPagina As String) As Boolean
        Try
            If Not Attivo() Then Return False
            If String.IsNullOrEmpty(indirizzoRisorsa) Then Return False
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(indirizzoRisorsa, UriKind.Absolute, uri) Then Return False
            If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then Return False
            Dim host As String = uri.Host.ToLowerInvariant()
            If host = "" Then Return False
            Dim pagina As String = If(hostPagina, "").ToLowerInvariant()
            If pagina <> "" Then
                If host = pagina OrElse host.EndsWith("." & pagina, StringComparison.Ordinal) OrElse pagina.EndsWith("." & host, StringComparison.Ordinal) Then Return False
                If SitoEscluso(pagina) Then Return False
            End If
            AssicuraCaricamento()
            Dim insieme As HashSet(Of String) = _domini
            If insieme Is Nothing Then Return False
            Dim corrente As String = host
            While corrente.IndexOf("."c) > 0
                If insieme.Contains(corrente) Then Return True
                Dim punto As Integer = corrente.IndexOf("."c)
                corrente = corrente.Substring(punto + 1)
            End While
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.DaBloccare: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Async Function AggiornaElencoAsync(Optional indirizzoElenco As String = IndirizzoElencoPredefinito) As Task(Of Integer)
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Dim contenuto As String
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(40)
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0")
                contenuto = Await client.GetStringAsync(indirizzoElenco).ConfigureAwait(False)
            End Using
            If String.IsNullOrWhiteSpace(contenuto) Then Return -1
            Dim nuovi As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each riga As String In contenuto.Split(New Char() {ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
                Dim dominio As String = NormalizzaDominio(riga)
                If dominio <> "" Then nuovi.Add(dominio)
            Next
            If nuovi.Count < 50 Then Return -1
            Dim percorso As String = PercorsoElenco()
            If percorso <> "" Then
                Dim temporaneo As String = percorso & ".tmp"
                File.WriteAllLines(temporaneo, nuovi.OrderBy(Function(d) d), New UTF8Encoding(False))
                If File.Exists(percorso) Then File.Delete(percorso)
                File.Move(temporaneo, percorso)
            End If
            Dim insieme As New HashSet(Of String)(DominiPredefiniti, StringComparer.OrdinalIgnoreCase)
            insieme.UnionWith(nuovi)
            _domini = insieme
            Return insieme.Count
        Catch ex As Exception
            _log.ScriviLog("WGBloccoPubblicita.AggiornaElencoAsync: " & ex.Message)
            Return -1
        End Try
    End Function
End Class
