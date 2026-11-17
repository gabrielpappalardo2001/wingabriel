Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml.Linq
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports AngleDom = AngleSharp.Dom
Imports AngleHtml = AngleSharp.Html.Parser
Imports AngleHtmlDom = AngleSharp.Html.Dom

' Estrazione e pulizia del testo principale dagli articoli web e dalle pagine HTML per la lettura facilitata.
Public NotInheritable Class EstrattoreArticoli
    Private Shared ReadOnly logger As New Logger()
    Private Const LunghezzaMinima As Integer = 200
    Private Const LunghezzaTestoCompleto As Integer = 1500
    Private Const RapportoPreferenzaTrafilatura As Double = 0.6
    Private Const QuotaInclusione As Double = 0.7
    Private Const RigheControllate As Integer = 30
    Private Const PagineSuccessiveMassime As Integer = 4
    Private Const NodiMassimiJson As Integer = 20000

    Private Shared ReadOnly TipiArticolo As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "Article", "NewsArticle", "BlogPosting", "Report", "ReportageNewsArticle", "AnalysisNewsArticle", "OpinionNewsArticle",
        "BackgroundNewsArticle", "ReviewNewsArticle", "LiveBlogPosting", "TechArticle", "ScholarlyArticle", "WebPage"
    }

    Private Sub New()
    End Sub

    Public Shared Sub Registra()
        Try
            WGLettoreArticoli.Estrattore = AddressOf EstraiPerLettura
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.Registra: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function EstraiPerLettura(ByVal url As String, ByVal html As String) As ArticoloEstratto
        Try
            Dim usaTrafilatura As Boolean = InstallatoreTrafilatura.Installato()
            Dim articolo As ArticoloEstratto = EstraiDaHtml(url, html, usaTrafilatura)
            If articolo.Valido Then UnisciPagineSuccessive(articolo, url, html, usaTrafilatura)
            Return articolo
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.EstraiPerLettura per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New ArticoloEstratto() With {.Indirizzo = If(url, String.Empty)}
        End Try
    End Function

    Public Shared Function EstraiDaHtml(ByVal url As String, ByVal html As String, ByVal usaTrafilatura As Boolean, Optional ByVal conStruttura As Boolean = True) As ArticoloEstratto
        Dim articolo As New ArticoloEstratto() With {.Indirizzo = If(url, String.Empty)}
        Try
            If String.IsNullOrWhiteSpace(html) Then Return articolo

            Try
                Dim analizzatore As New AngleHtml.HtmlParser()
                Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument(html)
                    If documento IsNot Nothing Then LeggiMetadati(documento, url, articolo)
                End Using
            Catch exMetadati As Exception
                logger.ScriviLog($"Eccezione in EstrattoreArticoli.EstraiDaHtml metadati per {url}: {exMetadati.Message}")
            End Try

            Dim candidati As New List(Of CandidatoTesto)()
            AnalizzatorePaginaArticolo.Analizza(url, html, candidati)
            Dim migliore As CandidatoTesto = SelezionatoreCandidati.ScegliMigliore(candidati)
            Dim testoInterno As String = If(migliore IsNot Nothing AndAlso migliore.Testo IsNot Nothing, migliore.Testo, String.Empty)

            Dim risposta As JObject = Nothing
            Dim testoTrafilatura As String = String.Empty
            If usaTrafilatura Then
                risposta = EstrattoreTrafilatura.EstraiCompleto(url, html, conStruttura)
                If risposta IsNot Nothing Then
                    testoTrafilatura = SelezionatoreCandidati.RimuoviUrl(Valore(risposta, "testo"))
                    If articolo.Titolo = String.Empty Then articolo.Titolo = Valore(risposta, "titolo")
                    If articolo.Autore = String.Empty Then articolo.Autore = Valore(risposta, "autore")
                    If articolo.Data = String.Empty Then articolo.Data = Valore(risposta, "data")
                    If articolo.Sito = String.Empty Then articolo.Sito = Valore(risposta, "sito")
                    If articolo.Descrizione = String.Empty Then articolo.Descrizione = Valore(risposta, "descrizione")
                    If articolo.Lingua = String.Empty Then articolo.Lingua = Valore(risposta, "lingua")
                End If
            End If
            If articolo.Sito = String.Empty Then articolo.Sito = WGLettoreArticoli.HostDi(url)

            If ScegliTrafilatura(testoTrafilatura, testoInterno) Then
                articolo.Motore = "trafilatura"
                articolo.Testo = PulisciTesto(testoTrafilatura, articolo.Titolo)
                articolo.Corpo = CorpoDaXml(Valore(risposta, "xml"), articolo.Titolo)
                If articolo.Corpo = String.Empty Then articolo.Corpo = WGLettoreArticoli.TestoInHtml(articolo.Testo)
            ElseIf testoInterno.Length > 0 Then
                articolo.Motore = NomeMotore(migliore.Priorita)
                articolo.Testo = PulisciTesto(testoInterno, articolo.Titolo)
                articolo.Corpo = WGLettoreArticoli.TestoInHtml(articolo.Testo)
            End If

            articolo.APagamento = RilevatoreArticoliAPagamento.APagamento(html) AndAlso articolo.Testo.Trim().Length < LunghezzaTestoCompleto
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.EstraiDaHtml per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return articolo
    End Function

    Public Shared Function TestoPagineSuccessive(ByVal url As String, ByVal html As String, ByVal usaTrafilatura As Boolean, ByVal testoPrimaPagina As String) As String
        Try
            Dim articolo As New ArticoloEstratto() With {.Indirizzo = If(url, String.Empty), .Testo = If(testoPrimaPagina, String.Empty)}
            Dim lunghezzaIniziale As Integer = articolo.Testo.Length
            UnisciPagineSuccessive(articolo, url, html, usaTrafilatura)
            If articolo.Testo.Length <= lunghezzaIniziale Then Return String.Empty
            Return articolo.Testo.Substring(lunghezzaIniziale).Trim()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.TestoPagineSuccessive per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Sub UnisciPagineSuccessive(ByVal articolo As ArticoloEstratto, ByVal url As String, ByVal html As String, ByVal usaTrafilatura As Boolean)
        Try
            If articolo Is Nothing OrElse String.IsNullOrWhiteSpace(html) Then Return
            Dim visitati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            visitati.Add(SenzaFrammento(url))
            Dim righeViste As New HashSet(Of String)(StringComparer.Ordinal)
            For Each riga As String In Righe(articolo.Testo)
                Dim chiave As String = WGLettoreArticoli.Compatta(riga)
                If chiave <> String.Empty Then righeViste.Add(chiave)
            Next
            Dim indirizzoCorrente As String = url
            Dim htmlCorrente As String = html
            For pagina As Integer = 1 To PagineSuccessiveMassime
                Dim successivo As String = IndirizzoPaginaSuccessiva(htmlCorrente, indirizzoCorrente)
                If successivo = String.Empty OrElse Not visitati.Add(SenzaFrammento(successivo)) Then Exit For
                Dim scaricata As RisultatoDownloadPagina = ScaricatorePagineWeb.Scarica(successivo)
                If scaricata Is Nothing OrElse String.IsNullOrWhiteSpace(scaricata.Html) Then Exit For
                Dim seguito As ArticoloEstratto = EstraiDaHtml(scaricata.UrlFinale, scaricata.Html, usaTrafilatura, False)
                If Not seguito.Valido Then Exit For
                Dim nuoveRighe As New List(Of String)()
                For Each riga As String In Righe(seguito.Testo)
                    Dim chiave As String = WGLettoreArticoli.Compatta(riga)
                    If chiave = String.Empty OrElse Not righeViste.Add(chiave) Then Continue For
                    nuoveRighe.Add(riga)
                Next
                If nuoveRighe.Count < 2 Then Exit For
                Dim testoNuovo As String = String.Join(ControlChars.Lf, nuoveRighe)
                articolo.Testo = (articolo.Testo & ControlChars.Lf & testoNuovo).Trim()
                articolo.Corpo &= "<h2>" & WebUtility.HtmlEncode("Pagina " & (pagina + 1)) & "</h2>" & WGLettoreArticoli.TestoInHtml(testoNuovo)
                articolo.Pagine += 1
                indirizzoCorrente = scaricata.UrlFinale
                htmlCorrente = scaricata.Html
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.UnisciPagineSuccessive per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function IndirizzoPaginaSuccessiva(ByVal html As String, ByVal url As String) As String
        Try
            Dim base As Uri = Nothing
            If Not Uri.TryCreate(If(url, String.Empty), UriKind.Absolute, base) Then Return String.Empty
            Dim analizzatore As New AngleHtml.HtmlParser()
            Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument(html)
                If documento Is Nothing Then Return String.Empty
                Dim collegamento As AngleDom.IElement = documento.QuerySelector("link[rel~='next']")
                If collegamento Is Nothing Then collegamento = documento.QuerySelector("a[rel~='next']")
                If collegamento Is Nothing Then Return String.Empty
                Dim destinazione As String = If(collegamento.GetAttribute("href"), String.Empty).Trim()
                If destinazione = String.Empty Then Return String.Empty
                Dim assoluto As Uri = Nothing
                If Not Uri.TryCreate(base, destinazione, assoluto) Then Return String.Empty
                If assoluto.Scheme <> Uri.UriSchemeHttp AndAlso assoluto.Scheme <> Uri.UriSchemeHttps Then Return String.Empty
                If Not String.Equals(assoluto.Host, base.Host, StringComparison.OrdinalIgnoreCase) Then Return String.Empty
                If Not StessoArticolo(base, assoluto) Then Return String.Empty
                Return assoluto.AbsoluteUri
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.IndirizzoPaginaSuccessiva per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function StessoArticolo(ByVal primo As Uri, ByVal secondo As Uri) As Boolean
        Try
            Dim a As String = primo.AbsolutePath.TrimEnd("/"c)
            Dim b As String = secondo.AbsolutePath.TrimEnd("/"c)
            If a.Length < 8 Then Return False
            If String.Equals(a, b, StringComparison.OrdinalIgnoreCase) Then Return Not String.Equals(primo.Query, secondo.Query, StringComparison.Ordinal)
            Dim comune As Integer = 0
            While comune < a.Length AndAlso comune < b.Length AndAlso Char.ToLowerInvariant(a(comune)) = Char.ToLowerInvariant(b(comune))
                comune += 1
            End While
            Return comune >= Math.Max(8, CInt(a.Length * 0.6))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.StessoArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function SenzaFrammento(ByVal url As String) As String
        Try
            Dim valore As String = If(url, String.Empty)
            Dim posizione As Integer = valore.IndexOf("#"c)
            Return If(posizione >= 0, valore.Substring(0, posizione), valore)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.SenzaFrammento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(url, String.Empty)
        End Try
    End Function

    Private Shared Function ScegliTrafilatura(ByVal testoTrafilatura As String, ByVal testoInterno As String) As Boolean
        Try
            Dim lunghezzaT As Integer = If(testoTrafilatura, String.Empty).Trim().Length
            Dim lunghezzaI As Integer = If(testoInterno, String.Empty).Trim().Length
            If lunghezzaT < LunghezzaMinima Then Return False
            If lunghezzaI < LunghezzaMinima Then Return True
            If lunghezzaT >= lunghezzaI * RapportoPreferenzaTrafilatura Then Return True
            Return Not ContenutoIncluso(testoTrafilatura, testoInterno)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.ScegliTrafilatura: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Not String.IsNullOrWhiteSpace(testoTrafilatura)
        End Try
    End Function

    Private Shared Function ContenutoIncluso(ByVal parte As String, ByVal tutto As String) As Boolean
        Try
            Dim compattoTutto As String = WGLettoreArticoli.Compatta(tutto)
            If compattoTutto = String.Empty Then Return False
            Dim controllate As Integer = 0
            Dim trovate As Integer = 0
            For Each riga As String In Righe(parte)
                Dim chiave As String = WGLettoreArticoli.Compatta(riga)
                If chiave.Length < 20 Then Continue For
                controllate += 1
                If compattoTutto.Contains(chiave.Substring(0, Math.Min(chiave.Length, 120))) Then trovate += 1
                If controllate >= RigheControllate Then Exit For
            Next
            If controllate = 0 Then Return False
            Return trovate >= controllate * QuotaInclusione
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.ContenutoIncluso: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function NomeMotore(ByVal priorita As Integer) As String
        Try
            Select Case priorita
                Case PrioritaCandidato.LettoreMozilla, PrioritaCandidato.LettoreIncerto
                    Return "lettore"
                Case PrioritaCandidato.SelettoriNoti
                    Return "selettori"
                Case PrioritaCandidato.DatiStrutturati
                    Return "dati strutturati"
                Case PrioritaCandidato.DensitaParagrafi
                    Return "densità"
            End Select
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.NomeMotore: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return "interno"
    End Function

    Private Shared Function Righe(ByVal testo As String) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            For Each riga As String In If(testo, String.Empty).Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
                Dim pulita As String = riga.Trim()
                If pulita.Length > 0 Then risultato.Add(pulita)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.Righe: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Private Shared Function PulisciTesto(ByVal testo As String, ByVal titolo As String) As String
        Try
            Dim chiaveTitolo As String = WGLettoreArticoli.Compatta(titolo)
            Dim risultato As New List(Of String)()
            Dim precedente As String = String.Empty
            For Each riga As String In Righe(testo)
                Dim chiave As String = WGLettoreArticoli.Compatta(riga)
                If chiave = String.Empty Then Continue For
                If risultato.Count = 0 AndAlso chiaveTitolo <> String.Empty AndAlso String.Equals(chiave, chiaveTitolo, StringComparison.Ordinal) Then Continue For
                If String.Equals(chiave, precedente, StringComparison.Ordinal) Then Continue For
                If WGLettoreArticoli.RigaDiRumore(riga) Then Continue For
                risultato.Add(riga)
                precedente = chiave
            Next
            Return String.Join(ControlChars.Lf, risultato)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.PulisciTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(testo, String.Empty)
        End Try
    End Function

    Private Shared Function Valore(ByVal oggetto As JObject, ByVal nome As String) As String
        Try
            If oggetto Is Nothing Then Return String.Empty
            Dim nodo As JToken = oggetto(nome)
            If nodo Is Nothing OrElse nodo.Type = JTokenType.Null Then Return String.Empty
            If nodo.Type = JTokenType.String Then Return If(nodo.Value(Of String)(), String.Empty).Trim()
            Return nodo.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.Valore ({nome}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function CorpoDaXml(ByVal xml As String, ByVal titolo As String) As String
        Dim corpo As New StringBuilder()
        Try
            If String.IsNullOrWhiteSpace(xml) Then Return String.Empty
            Dim documento As XDocument = XDocument.Parse(xml, LoadOptions.None)
            Dim principale As XElement = documento.Descendants("main").FirstOrDefault()
            If principale Is Nothing Then Return String.Empty
            Dim chiaveTitolo As String = WGLettoreArticoli.Compatta(titolo)
            Dim primo As Boolean = True
            For Each elemento As XElement In principale.Elements()
                AggiungiElemento(elemento, corpo, chiaveTitolo, primo)
                primo = False
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.CorpoDaXml: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
        Return corpo.ToString()
    End Function

    Private Shared Sub AggiungiElemento(ByVal elemento As XElement, ByVal corpo As StringBuilder, ByVal chiaveTitolo As String, ByVal primo As Boolean)
        Try
            Select Case elemento.Name.LocalName
                Case "head"
                    Dim testo As String = TestoElemento(elemento)
                    If testo = String.Empty Then Return
                    If primo AndAlso chiaveTitolo <> String.Empty AndAlso String.Equals(WGLettoreArticoli.Compatta(testo), chiaveTitolo, StringComparison.Ordinal) Then Return
                    Dim livello As String = If(CStr(elemento.Attribute("rend")), "h2").ToLowerInvariant()
                    Dim tag As String = If(livello = "h1" OrElse livello = "h2", "h2", "h3")
                    corpo.Append("<").Append(tag).Append(">").Append(WebUtility.HtmlEncode(testo)).Append("</").Append(tag).Append(">")
                Case "p", "code"
                    For Each riga As String In RigheParagrafo(elemento)
                        If WGLettoreArticoli.RigaDiRumore(riga) Then Continue For
                        corpo.Append("<p>").Append(WebUtility.HtmlEncode(riga)).Append("</p>")
                    Next
                Case "list"
                    Dim ordinato As Boolean = String.Equals(CStr(elemento.Attribute("rend")), "ol", StringComparison.OrdinalIgnoreCase)
                    Dim voci As New List(Of String)()
                    For Each voce As XElement In elemento.Elements("item")
                        Dim testo As String = TestoElemento(voce)
                        If testo <> String.Empty AndAlso Not WGLettoreArticoli.RigaDiRumore(testo) Then voci.Add(testo)
                    Next
                    If voci.Count = 0 Then Return
                    corpo.Append(If(ordinato, "<ol>", "<ul>"))
                    For Each voce As String In voci
                        corpo.Append("<li>").Append(WebUtility.HtmlEncode(voce)).Append("</li>")
                    Next
                    corpo.Append(If(ordinato, "</ol>", "</ul>"))
                Case "quote"
                    Dim testo As String = TestoElemento(elemento)
                    If testo = String.Empty Then Return
                    corpo.Append("<blockquote><p>").Append(WebUtility.HtmlEncode(testo)).Append("</p></blockquote>")
                Case "table"
                    AggiungiTabella(elemento, corpo)
                Case Else
                    Dim testo As String = TestoElemento(elemento)
                    If testo <> String.Empty AndAlso Not WGLettoreArticoli.RigaDiRumore(testo) Then corpo.Append("<p>").Append(WebUtility.HtmlEncode(testo)).Append("</p>")
            End Select
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.AggiungiElemento: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AggiungiTabella(ByVal tabella As XElement, ByVal corpo As StringBuilder)
        Try
            Dim intestazioni As New List(Of String)()
            For Each riga As XElement In tabella.Elements("row")
                Dim celle As List(Of XElement) = riga.Elements("cell").ToList()
                If celle.Count = 0 Then Continue For
                Dim tutteIntestazioni As Boolean = celle.All(Function(c) String.Equals(CStr(c.Attribute("role")), "head", StringComparison.OrdinalIgnoreCase))
                Dim valori As List(Of String) = celle.Select(Function(c) TestoElemento(c)).ToList()
                If tutteIntestazioni AndAlso intestazioni.Count = 0 Then
                    intestazioni = valori
                    Continue For
                End If
                Dim parti As New List(Of String)()
                For i As Integer = 0 To valori.Count - 1
                    If valori(i) = String.Empty Then Continue For
                    If i < intestazioni.Count AndAlso intestazioni(i) <> String.Empty Then
                        parti.Add(intestazioni(i) & ": " & valori(i))
                    Else
                        parti.Add(valori(i))
                    End If
                Next
                If parti.Count > 0 Then corpo.Append("<p>").Append(WebUtility.HtmlEncode(String.Join("; ", parti))).Append("</p>")
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.AggiungiTabella: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RigheParagrafo(ByVal elemento As XElement) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim corrente As New StringBuilder()
            For Each nodo As XNode In elemento.DescendantNodes()
                Dim testo As XText = TryCast(nodo, XText)
                If testo IsNot Nothing Then
                    corrente.Append(testo.Value)
                    Continue For
                End If
                Dim figlio As XElement = TryCast(nodo, XElement)
                If figlio IsNot Nothing AndAlso figlio.Name.LocalName = "lb" Then
                    Dim riga As String = WGLettoreArticoli.Normalizza(corrente.ToString())
                    If riga <> String.Empty Then risultato.Add(riga)
                    corrente.Clear()
                End If
            Next
            Dim ultima As String = WGLettoreArticoli.Normalizza(corrente.ToString())
            If ultima <> String.Empty Then risultato.Add(ultima)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.RigheParagrafo: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Private Shared Function TestoElemento(ByVal elemento As XElement) As String
        Try
            If elemento Is Nothing Then Return String.Empty
            Return WGLettoreArticoli.Normalizza(String.Join(" ", elemento.DescendantNodes().OfType(Of XText)().Select(Function(t) t.Value)))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.TestoElemento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Sub LeggiMetadati(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal url As String, ByVal articolo As ArticoloEstratto)
        Try
            Dim datiArticolo As JObject = DatiStrutturatiArticolo(documento, url)
            articolo.Titolo = WGLettoreArticoli.PrimoNonVuoto(Meta(documento, "og:title"), Meta(documento, "twitter:title"), Proprieta(datiArticolo, "headline"), TestoSelettore(documento, "h1"), If(documento.Title, String.Empty))
            articolo.Autore = WGLettoreArticoli.PrimoNonVuoto(AutoreDaDati(datiArticolo), AutoreLeggibile(Meta(documento, "author")), AutoreLeggibile(Meta(documento, "article:author")), AutoreLeggibile(TestoSelettore(documento, "[rel~='author']")))
            articolo.Data = WGLettoreArticoli.PrimoNonVuoto(Meta(documento, "article:published_time"), Proprieta(datiArticolo, "datePublished"), Meta(documento, "date"), Meta(documento, "pubdate"), AttributoSelettore(documento, "time[datetime]", "datetime"))
            articolo.Sito = WGLettoreArticoli.PrimoNonVuoto(Meta(documento, "og:site_name"), EditoreDaDati(datiArticolo))
            articolo.Descrizione = WGLettoreArticoli.PrimoNonVuoto(Meta(documento, "og:description"), Meta(documento, "description"), Proprieta(datiArticolo, "description"))
            If documento.DocumentElement IsNot Nothing Then articolo.Lingua = If(documento.DocumentElement.GetAttribute("lang"), String.Empty).Trim()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.LeggiMetadati per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function Meta(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal nome As String) As String
        Try
            For Each elemento As AngleDom.IElement In documento.QuerySelectorAll("meta")
                Dim chiave As String = If(elemento.GetAttribute("property"), String.Empty)
                If chiave = String.Empty Then chiave = If(elemento.GetAttribute("name"), String.Empty)
                If chiave = String.Empty Then chiave = If(elemento.GetAttribute("itemprop"), String.Empty)
                If Not String.Equals(chiave.Trim(), nome, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim contenuto As String = WGLettoreArticoli.Normalizza(WebUtility.HtmlDecode(If(elemento.GetAttribute("content"), String.Empty)))
                If contenuto <> String.Empty Then Return contenuto
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.Meta ({nome}): {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return String.Empty
    End Function

    Private Shared Function TestoSelettore(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal selettore As String) As String
        Try
            Dim elemento As AngleDom.IElement = documento.QuerySelector(selettore)
            If elemento Is Nothing Then Return String.Empty
            Dim testo As String = WGLettoreArticoli.Normalizza(elemento.TextContent)
            If testo.Length > 300 Then Return String.Empty
            Return testo
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.TestoSelettore ({selettore}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function AttributoSelettore(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal selettore As String, ByVal attributo As String) As String
        Try
            Dim elemento As AngleDom.IElement = documento.QuerySelector(selettore)
            If elemento Is Nothing Then Return String.Empty
            Return If(elemento.GetAttribute(attributo), String.Empty).Trim()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.AttributoSelettore ({selettore}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function AutoreLeggibile(ByVal valore As String) As String
        Try
            Dim pulito As String = WGLettoreArticoli.Normalizza(valore)
            If pulito = String.Empty Then Return String.Empty
            If pulito.StartsWith("http", StringComparison.OrdinalIgnoreCase) OrElse pulito.Contains("/") Then Return String.Empty
            If pulito.Length > 120 Then Return String.Empty
            If pulito.StartsWith("di ", StringComparison.OrdinalIgnoreCase) Then pulito = pulito.Substring(3).Trim()
            If pulito.StartsWith("by ", StringComparison.OrdinalIgnoreCase) Then pulito = pulito.Substring(3).Trim()
            Return pulito
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.AutoreLeggibile: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function DatiStrutturatiArticolo(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal url As String) As JObject
        Try
            For Each script As AngleDom.IElement In documento.QuerySelectorAll("script[type*='ld+json']")
                Dim radice As JToken = InterpretaJson(script.TextContent, url)
                If radice Is Nothing Then Continue For
                Dim trovato As JObject = CercaArticolo(radice)
                If trovato IsNot Nothing Then Return trovato
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.DatiStrutturatiArticolo per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return Nothing
    End Function

    Private Shared Function InterpretaJson(ByVal contenuto As String, ByVal url As String) As JToken
        Try
            If String.IsNullOrWhiteSpace(contenuto) Then Return Nothing
            Dim pulito As String = contenuto.Trim()
            pulito = pulito.Replace("/*<![CDATA[*/", String.Empty).Replace("/*]]>*/", String.Empty)
            pulito = pulito.Replace("<![CDATA[", String.Empty).Replace("]]>", String.Empty)
            pulito = pulito.Replace("<!--", String.Empty).Replace("-->", String.Empty).Trim()
            If pulito.Length = 0 Then Return Nothing
            If Not pulito.StartsWith("{", StringComparison.Ordinal) AndAlso Not pulito.StartsWith("[", StringComparison.Ordinal) Then Return Nothing
            Return JToken.Parse(pulito)
        Catch ex As JsonException
            logger.ScriviLog($"Dati strutturati JSON-LD non validi in {url}: {ex.Message}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.InterpretaJson per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function CercaArticolo(ByVal radice As JToken) As JObject
        Try
            Dim pila As New Stack(Of JToken)()
            pila.Push(radice)
            Dim visitati As Integer = 0
            Dim paginaWeb As JObject = Nothing
            While pila.Count > 0 AndAlso visitati < NodiMassimiJson
                Dim token As JToken = pila.Pop()
                visitati += 1
                If token Is Nothing Then Continue While
                If token.Type = JTokenType.Object Then
                    Dim oggetto As JObject = CType(token, JObject)
                    Dim tipi As List(Of String) = TipiDi(oggetto)
                    If tipi.Any(Function(t) TipiArticolo.Contains(t) AndAlso Not String.Equals(t, "WebPage", StringComparison.OrdinalIgnoreCase)) Then Return oggetto
                    If paginaWeb Is Nothing AndAlso tipi.Any(Function(t) String.Equals(t, "WebPage", StringComparison.OrdinalIgnoreCase)) Then paginaWeb = oggetto
                    For Each proprieta As JProperty In oggetto.Properties()
                        If proprieta.Value IsNot Nothing AndAlso (proprieta.Value.Type = JTokenType.Object OrElse proprieta.Value.Type = JTokenType.Array) Then pila.Push(proprieta.Value)
                    Next
                ElseIf token.Type = JTokenType.Array Then
                    For Each figlio As JToken In token.Children()
                        If figlio IsNot Nothing Then pila.Push(figlio)
                    Next
                End If
            End While
            Return paginaWeb
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.CercaArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function TipiDi(ByVal oggetto As JObject) As List(Of String)
        Dim tipi As New List(Of String)()
        Try
            Dim nodo As JToken = oggetto("@type")
            If nodo Is Nothing Then Return tipi
            If nodo.Type = JTokenType.String Then
                tipi.Add(nodo.Value(Of String)())
            ElseIf nodo.Type = JTokenType.Array Then
                For Each valore As JToken In nodo.Children()
                    If valore.Type = JTokenType.String Then tipi.Add(valore.Value(Of String)())
                Next
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.TipiDi: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return tipi
    End Function

    Private Shared Function Proprieta(ByVal oggetto As JObject, ByVal nome As String) As String
        Try
            If oggetto Is Nothing Then Return String.Empty
            Dim nodo As JToken = oggetto(nome)
            If nodo Is Nothing Then Return String.Empty
            If nodo.Type = JTokenType.String Then Return WGLettoreArticoli.Normalizza(WebUtility.HtmlDecode(nodo.Value(Of String)()))
            If nodo.Type = JTokenType.Date Then Return nodo.Value(Of DateTime)().ToString("o")
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.Proprieta ({nome}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function AutoreDaDati(ByVal oggetto As JObject) As String
        Try
            If oggetto Is Nothing Then Return String.Empty
            Dim nodo As JToken = oggetto("author")
            If nodo Is Nothing Then Return String.Empty
            Dim nomi As New List(Of String)()
            Dim elementi As New List(Of JToken)()
            If nodo.Type = JTokenType.Array Then
                elementi.AddRange(nodo.Children())
            Else
                elementi.Add(nodo)
            End If
            For Each elemento As JToken In elementi
                Dim nome As String = String.Empty
                If elemento.Type = JTokenType.String Then
                    nome = elemento.Value(Of String)()
                ElseIf elemento.Type = JTokenType.Object Then
                    Dim nodoNome As JToken = elemento("name")
                    If nodoNome IsNot Nothing AndAlso nodoNome.Type = JTokenType.String Then nome = nodoNome.Value(Of String)()
                End If
                nome = AutoreLeggibile(WebUtility.HtmlDecode(If(nome, String.Empty)))
                If nome <> String.Empty AndAlso Not nomi.Contains(nome) Then nomi.Add(nome)
                If nomi.Count >= 4 Then Exit For
            Next
            Return String.Join(", ", nomi)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.AutoreDaDati: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function EditoreDaDati(ByVal oggetto As JObject) As String
        Try
            If oggetto Is Nothing Then Return String.Empty
            Dim nodo As JToken = oggetto("publisher")
            If nodo Is Nothing Then Return String.Empty
            If nodo.Type = JTokenType.String Then Return WGLettoreArticoli.Normalizza(nodo.Value(Of String)())
            If nodo.Type = JTokenType.Object Then
                Dim nodoNome As JToken = nodo("name")
                If nodoNome IsNot Nothing AndAlso nodoNome.Type = JTokenType.String Then Return WGLettoreArticoli.Normalizza(nodoNome.Value(Of String)())
            End If
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreArticoli.EditoreDaDati: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function
End Class
