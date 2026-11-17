Imports System.Collections.Generic
Imports System.Linq
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports HAP = HtmlAgilityPack

' Estrazione e pulizia del testo da pagine web e documenti per una lettura strutturata e priva di elementi superflui.
Public Class ArticoloEstratto
    Public Property Indirizzo As String = ""
    Public Property Titolo As String = ""
    Public Property Autore As String = ""
    Public Property Data As String = ""
    Public Property Sito As String = ""
    Public Property Descrizione As String = ""
    Public Property Lingua As String = ""
    Public Property Corpo As String = ""
    Public Property Testo As String = ""
    ''' <summary>Testo con struttura per la lettura in un campo: titoli su riga propria, paragrafi separati da una riga vuota, elenchi con "- ", tabelle una riga per riga con celle separate da " | ".</summary>
    Public Property TestoFormattato As String = ""
    Public Property Motore As String = ""
    Public Property Pagine As Integer = 1
    Public Property APagamento As Boolean = False

    Private Shared ReadOnly _log As New Logger()

    Public ReadOnly Property Valido As Boolean
        Get
            Try
                Return Not String.IsNullOrWhiteSpace(Testo) AndAlso Testo.Trim().Length >= WGLettoreArticoli.LunghezzaMinimaArticolo
            Catch ex As Exception
                _log.ScriviLog("ArticoloEstratto.Valido: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    ''' <summary>Il testo strutturato se disponibile, altrimenti quello semplice.</summary>
    Public Function TestoPerLettura() As String
        Return If(String.IsNullOrWhiteSpace(TestoFormattato), If(Testo, ""), TestoFormattato)
    End Function

    Public Function Firma() As String
        Dim parti As New List(Of String)()
        Try
            If Not String.IsNullOrWhiteSpace(Autore) Then parti.Add("Di " & Autore.Trim())
            Dim dataLeggibile As String = WGLettoreArticoli.DataLeggibile(Data)
            If dataLeggibile <> "" Then parti.Add(dataLeggibile)
            If Not String.IsNullOrWhiteSpace(Sito) Then parti.Add(Sito.Trim())
            If Pagine > 1 Then parti.Add(Pagine & " pagine unite")
        Catch ex As Exception
            _log.ScriviLog("ArticoloEstratto.Firma: " & ex.Message)
        End Try
        Return String.Join(", ", parti)
    End Function

    Public Function HtmlCompleto() As String
        Dim html As New StringBuilder()
        Try
            If Not String.IsNullOrWhiteSpace(Titolo) Then html.Append("<h1>").Append(WebUtility.HtmlEncode(Titolo.Trim())).Append("</h1>")
            Dim firmaArticolo As String = Firma()
            If firmaArticolo <> "" Then html.Append("<p>").Append(WebUtility.HtmlEncode(firmaArticolo)).Append("</p>")
            Dim descrizionePulita As String = If(Descrizione, "").Trim()
            If descrizionePulita.Length >= 30 AndAlso Not TestoInizia(descrizionePulita) Then html.Append("<p>").Append(WebUtility.HtmlEncode(descrizionePulita)).Append("</p>")
            If Not String.IsNullOrWhiteSpace(Corpo) Then
                html.Append(Corpo)
            ElseIf Not String.IsNullOrWhiteSpace(Testo) Then
                html.Append(WGLettoreArticoli.TestoInHtml(Testo))
            End If
        Catch ex As Exception
            _log.ScriviLog("ArticoloEstratto.HtmlCompleto: " & ex.Message)
        End Try
        Return html.ToString()
    End Function

    Private Function TestoInizia(frase As String) As Boolean
        Try
            Dim a As String = WGLettoreArticoli.Compatta(frase)
            Dim b As String = WGLettoreArticoli.Compatta(If(Testo, ""))
            If a = "" OrElse b = "" Then Return False
            Dim prefisso As String = a.Substring(0, Math.Min(a.Length, 80))
            Return b.IndexOf(prefisso, StringComparison.Ordinal) >= 0
        Catch ex As Exception
            _log.ScriviLog("ArticoloEstratto.TestoInizia: " & ex.Message)
            Return False
        End Try
    End Function
End Class

''' <summary>Opzioni facoltative per WGLettoreArticoli.EstrazioneSemplice.</summary>
Public Class OpzioniEstrazione
    ''' <summary>True quando l'html e' gia' solo il contenuto (per esempio il testo restituito dalle API di Wikipedia): niente ricerca del contenitore migliore, niente filtro delle righe "di rumore", niente eliminazione dei duplicati.</summary>
    Public Property ContenutoIsolato As Boolean = False
    Public Property Titolo As String = ""
    Public Property Sito As String = ""
    Public Property Lingua As String = ""
    ''' <summary>Titoli di sezioni da saltare del tutto (con le loro sottosezioni), per esempio "Note" e "Bibliografia".</summary>
    Public Property SezioniDaEscludere As IEnumerable(Of String) = Nothing
End Class

Public NotInheritable Class WGLettoreArticoli
    Private Shared ReadOnly _log As New Logger()
    Public Const LunghezzaMinimaArticolo As Integer = 200
    Private Const LunghezzaMinimaParagrafo As Integer = 40
    Private Const LunghezzaMassimaCella As Integer = 300
    Private Const QuotaMassimaLink As Double = 0.5
    Private Const DimensioneMassimaHtml As Integer = 12 * 1024 * 1024
    Private Shared _estrattore As Func(Of String, String, ArticoloEstratto) = Nothing
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly RegexSpazi As New Regex("\s+", RegexOptions.Compiled)
    Private Shared ReadOnly RegexNonAlfanumerici As New Regex("[^\p{L}\p{N}]+", RegexOptions.Compiled)
    Private Shared ReadOnly RegexNoteNelTesto As New Regex("\s*\[\s*(?:\d{1,3}|nota\s*\d{1,3}|senza\s+fonte|citazione\s+necessaria|serve\s+(?:una\s+)?fonte|fonte\s+necessaria|chiarire)\s*\]", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
    Private Shared ReadOnly RegexSpazioPrimaPunteggiatura As New Regex("\s+([,.;:!?])", RegexOptions.Compiled)
    Private Shared ReadOnly RegexRiferimentoEsponente As New Regex("^\[[^\]]{1,30}\]$", RegexOptions.Compiled)

    Private Shared ReadOnly ElementiDaScartare As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "script", "style", "noscript", "nav", "header", "footer", "aside", "form", "iframe", "svg", "button", "select", "textarea", "template", "dialog", "menu"
    }

    Private Shared ReadOnly ElementiBlocco As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "h1", "h2", "h3", "h4", "h5", "h6", "p", "li", "blockquote", "pre", "figcaption", "caption", "td", "dd", "dt"
    }

    ' Elementi che, dentro una cella, la rendono "non da tabella di dati" (la riga viene allora letta come contenuto normale).
    Private Shared ReadOnly ElementiNonAmmessiInCella As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "p", "ul", "ol", "dl", "table", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6"
    }

    ' Contenitori il cui testo proprio si distingue da quello degli elenchi annidati.
    Private Shared ReadOnly ElementiStruttura As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "ul", "ol", "dl", "table", "div", "section"
    }

    ' Classi di rumore sicure ovunque (elementi tipici di MediaWiki).
    Private Shared ReadOnly ClassiSicure As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "navbox", "navbox-inner", "navbox-group", "vertical-navbox", "infobox", "infobox_v2", "infobox_v3", "sinottico",
        "mw-editsection", "mw-empty-elt", "hatnote", "ambox", "mbox-small", "sisterproject", "sistersitebox",
        "authority-control", "mw-authority-control", "printfooter", "mw-jump-link"
    }

    ' Classi, id e ruoli scartati solo quando il contenuto e' gia' isolato (per non toccare i siti di notizie).
    Private Shared ReadOnly ClassiContenutoIsolato As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "noprint", "metadata", "reference", "references", "reflist", "refbegin", "mw-references-wrap", "Inline-Template", "noviewer",
        "toc", "toccolours", "catlinks", "thumb", "thumbinner", "tright", "tleft", "gallery", "sidebar", "portalbox", "avviso",
        "rellink", "dablink", "interprogetto", "mw-cite-backlink"
    }

    Private Shared ReadOnly IdContenutoIsolato As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "toc", "catlinks", "printfooter", "siteSub", "contentSub", "jump-to-nav"
    }

    Private Shared ReadOnly RuoliContenutoIsolato As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "navigation", "note", "complementary", "banner", "contentinfo", "search"
    }

    Private Shared ReadOnly ParoleRumore As String() = {
        "cookie", "newsletter", "iscriviti", "abbonati", "subscribe", "sign up", "leggi anche", "potrebbe interessarti", "ti potrebbe interessare",
        "riproduzione riservata", "all rights reserved", "condividi", "share on", "commenti", "pubblicità", "advertisement"
    }

    Private NotInheritable Class BloccoEstratto
        Public Property Tipo As String = ""
        Public Property Testo As String = ""
        Public Property Celle As List(Of String) = Nothing
        Public Property Intestazione As Boolean = False
    End Class

    Private Sub New()
    End Sub

    Public Shared Property Estrattore As Func(Of String, String, ArticoloEstratto)
        Get
            SyncLock _lock
                Return _estrattore
            End SyncLock
        End Get
        Set(value As Func(Of String, String, ArticoloEstratto))
            SyncLock _lock
                _estrattore = value
            End SyncLock
        End Set
    End Property

    Public Shared Function Estrai(indirizzo As String, html As String) As ArticoloEstratto
        Try
            If String.IsNullOrWhiteSpace(html) Then Return New ArticoloEstratto() With {.Indirizzo = If(indirizzo, "")}
            If html.Length > DimensioneMassimaHtml Then html = html.Substring(0, DimensioneMassimaHtml)
            Dim registrato As Func(Of String, String, ArticoloEstratto) = Estrattore
            If registrato IsNot Nothing Then
                Try
                    Dim risultato As ArticoloEstratto = registrato(indirizzo, html)
                    If risultato IsNot Nothing AndAlso (risultato.Valido OrElse risultato.APagamento) Then Return risultato
                Catch exRegistrato As Exception
                    _log.ScriviLog("WGLettoreArticoli.Estrai estrattore di WinGabriel: " & exRegistrato.ToString())
                End Try
            End If
            Return EstrazioneSemplice(indirizzo, html)
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Estrai: " & ex.ToString())
            Return New ArticoloEstratto() With {.Indirizzo = If(indirizzo, "")}
        End Try
    End Function

    Public Shared Function EstrazioneSemplice(indirizzo As String, html As String) As ArticoloEstratto
        Return EstrazioneSemplice(indirizzo, html, Nothing)
    End Function

    Public Shared Function EstrazioneSemplice(indirizzo As String, html As String, opzioni As OpzioniEstrazione) As ArticoloEstratto
        Dim articolo As New ArticoloEstratto() With {.Indirizzo = If(indirizzo, ""), .Motore = "semplice"}
        Try
            If String.IsNullOrWhiteSpace(html) Then Return articolo
            If html.Length > DimensioneMassimaHtml Then html = html.Substring(0, DimensioneMassimaHtml)
            Dim isolato As Boolean = opzioni IsNot Nothing AndAlso opzioni.ContenutoIsolato
            Dim documento As New HAP.HtmlDocument()
            documento.LoadHtml(html)
            Dim radice As HAP.HtmlNode = documento.DocumentNode
            If radice Is Nothing Then Return articolo
            Dim titoloNoto As String = If(opzioni IsNot Nothing, opzioni.Titolo, "")
            Dim sitoNoto As String = If(opzioni IsNot Nothing, opzioni.Sito, "")
            Dim linguaNota As String = If(opzioni IsNot Nothing, opzioni.Lingua, "")
            articolo.Titolo = PrimoNonVuoto(titoloNoto, Meta(radice, "og:title"), Meta(radice, "twitter:title"), TestoPrimo(radice, "h1"), TestoPrimo(radice, "title"))
            articolo.Sito = PrimoNonVuoto(sitoNoto, Meta(radice, "og:site_name"), HostDi(indirizzo))
            articolo.Descrizione = PrimoNonVuoto(Meta(radice, "og:description"), Meta(radice, "description"))
            articolo.Autore = PrimoNonVuoto(Meta(radice, "author"), Meta(radice, "article:author"))
            articolo.Data = PrimoNonVuoto(Meta(radice, "article:published_time"), Meta(radice, "date"), AttributoPrimo(radice, "time", "datetime"))
            articolo.Lingua = PrimoNonVuoto(linguaNota, AttributoPrimo(radice, "html", "lang"))

            Dim daRimuovere As List(Of HAP.HtmlNode) = radice.Descendants().Where(Function(n) n.NodeType = HAP.HtmlNodeType.Comment OrElse (n.NodeType = HAP.HtmlNodeType.Element AndAlso (ElementiDaScartare.Contains(n.Name) OrElse Nascosto(n) OrElse RumoreDiStruttura(n, isolato)))).ToList()
            For Each nodo As HAP.HtmlNode In daRimuovere
                Try
                    nodo.Remove()
                Catch exRimozione As Exception
                    _log.ScriviLog("WGLettoreArticoli.EstrazioneSemplice rimozione: " & exRimozione.Message)
                End Try
            Next

            Dim contenitore As HAP.HtmlNode = If(isolato, radice, ContenitoreMigliore(radice))
            If contenitore Is Nothing Then Return articolo

            Dim escluse As HashSet(Of String) = Nothing
            If opzioni IsNot Nothing AndAlso opzioni.SezioniDaEscludere IsNot Nothing Then
                escluse = New HashSet(Of String)(opzioni.SezioniDaEscludere.Select(Function(s) Compatta(s)).Where(Function(s) s <> ""), StringComparer.Ordinal)
            End If
            Dim livelloEscluso As Integer = 0
            Dim titoloCompatto As String = Compatta(articolo.Titolo)

            Dim blocchi As New List(Of BloccoEstratto)()
            Dim visti As New HashSet(Of String)(StringComparer.Ordinal)
            Dim righeConsumate As New HashSet(Of HAP.HtmlNode)()

            For Each nodo As HAP.HtmlNode In contenitore.Descendants().Where(Function(n) n.NodeType = HAP.HtmlNodeType.Element AndAlso (ElementiBlocco.Contains(n.Name) OrElse String.Equals(n.Name, "tr", StringComparison.OrdinalIgnoreCase)))
                If DentroRigaConsumata(nodo, righeConsumate) Then Continue For
                Dim nome As String = nodo.Name.ToLowerInvariant()

                If nome = "tr" Then
                    Dim intestazione As Boolean = False
                    Dim celle As List(Of String) = CelleRigaDati(nodo, intestazione)
                    If celle Is Nothing Then Continue For
                    righeConsumate.Add(nodo)
                    If livelloEscluso > 0 Then Continue For
                    blocchi.Add(New BloccoEstratto() With {.Tipo = "tr", .Celle = celle, .Intestazione = intestazione, .Testo = String.Join(" | ", celle)})
                    Continue For
                End If

                Dim annidato As Boolean = nodo.Descendants().Any(Function(d) d.NodeType = HAP.HtmlNodeType.Element AndAlso ElementiBlocco.Contains(d.Name))
                Dim contenuto As String
                If annidato Then
                    If nome <> "li" AndAlso nome <> "dd" AndAlso nome <> "blockquote" Then Continue For
                    contenuto = Normalizza(WebUtility.HtmlDecode(TestoProprio(nodo)))
                Else
                    contenuto = Normalizza(WebUtility.HtmlDecode(nodo.InnerText))
                End If
                If Not isolato Then contenuto = RimuoviRiferimenti(contenuto)
                If contenuto = "" Then Continue For
                Dim chiave As String = Compatta(contenuto)
                If chiave = "" Then Continue For

                Dim livello As Integer = LivelloIntestazione(nome)
                If livello > 0 Then
                    If livelloEscluso > 0 AndAlso livello <= livelloEscluso Then livelloEscluso = 0
                    If livelloEscluso = 0 AndAlso escluse IsNot Nothing AndAlso escluse.Contains(chiave) Then
                        livelloEscluso = livello
                        Continue For
                    End If
                End If
                If livelloEscluso > 0 Then Continue For

                If Not isolato Then
                    If Not visti.Add(chiave) Then Continue For
                    If RigaDiRumore(contenuto) Then Continue For
                End If

                Select Case nome
                    Case "h1", "h2"
                        If chiave = titoloCompatto Then Continue For
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "h2", .Testo = contenuto})
                    Case "h3"
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "h3", .Testo = contenuto})
                    Case "h4", "h5", "h6"
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "h4", .Testo = contenuto})
                    Case "li"
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "li", .Testo = contenuto})
                    Case "blockquote"
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "blockquote", .Testo = contenuto})
                    Case Else
                        If contenuto.Length < 3 Then Continue For
                        blocchi.Add(New BloccoEstratto() With {.Tipo = "p", .Testo = contenuto})
                End Select
            Next

            Dim corpo As New StringBuilder()
            Dim testo As New StringBuilder()
            Dim formattato As New StringBuilder()
            Dim precedente As String = ""
            For Each blocco As BloccoEstratto In blocchi
                If precedente = "li" AndAlso blocco.Tipo <> "li" Then corpo.Append("</ul>")
                If precedente = "tr" AndAlso blocco.Tipo <> "tr" Then corpo.Append("</table>")
                If blocco.Tipo = "li" AndAlso precedente <> "li" Then corpo.Append("<ul>")
                If blocco.Tipo = "tr" AndAlso precedente <> "tr" Then corpo.Append("<table>")
                Dim codificato As String = WebUtility.HtmlEncode(blocco.Testo)
                Select Case blocco.Tipo
                    Case "h2", "h3", "h4", "p", "blockquote"
                        corpo.Append("<").Append(blocco.Tipo).Append(">").Append(codificato).Append("</").Append(blocco.Tipo).Append(">")
                    Case "li"
                        corpo.Append("<li>").Append(codificato).Append("</li>")
                    Case "tr"
                        Dim cella As String = If(blocco.Intestazione, "th", "td")
                        corpo.Append("<tr>")
                        For Each valoreCella As String In blocco.Celle
                            corpo.Append("<").Append(cella).Append(">").Append(WebUtility.HtmlEncode(valoreCella)).Append("</").Append(cella).Append(">")
                        Next
                        corpo.Append("</tr>")
                End Select

                If testo.Length > 0 Then testo.Append(vbCrLf)
                testo.Append(blocco.Testo)

                If formattato.Length > 0 Then
                    If blocco.Tipo = precedente AndAlso (blocco.Tipo = "li" OrElse blocco.Tipo = "tr") Then
                        formattato.Append(vbCrLf)
                    Else
                        formattato.Append(vbCrLf).Append(vbCrLf)
                    End If
                End If
                If blocco.Tipo = "li" Then formattato.Append("- ")
                formattato.Append(blocco.Testo)

                precedente = blocco.Tipo
            Next
            If precedente = "li" Then corpo.Append("</ul>")
            If precedente = "tr" Then corpo.Append("</table>")

            articolo.Corpo = corpo.ToString()
            articolo.Testo = testo.ToString()
            articolo.TestoFormattato = formattato.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.EstrazioneSemplice: " & ex.ToString())
        End Try
        Return articolo
    End Function

    ''' <summary>Pagina html completa e autonoma (con titolo, stile leggibile e tema chiaro/scuro) per aprire l'articolo in un browser.</summary>
    Public Shared Function DocumentoHtml(articolo As ArticoloEstratto, Optional lingua As String = "it") As String
        Dim html As New StringBuilder()
        Try
            If articolo Is Nothing Then Return ""
            Dim titolo As String = If(articolo.Titolo, "").Trim()
            html.Append("<!DOCTYPE html><html lang=""").Append(WebUtility.HtmlEncode(PrimoNonVuoto(articolo.Lingua, lingua, "it"))).Append("""><head><meta charset=""utf-8"">")
            html.Append("<meta name=""viewport"" content=""width=device-width, initial-scale=1"">")
            html.Append("<title>").Append(WebUtility.HtmlEncode(If(titolo = "", "Articolo", titolo))).Append("</title>")
            html.Append("<style>")
            html.Append("body{font-family:'Segoe UI',Arial,sans-serif;font-size:1.15rem;line-height:1.65;max-width:46rem;margin:0 auto;padding:1rem 1.25rem 3rem;background:#fff;color:#111}")
            html.Append("h1{font-size:1.9rem;line-height:1.25}h2{font-size:1.5rem;margin-top:2rem}h3{font-size:1.25rem}h4{font-size:1.1rem}")
            html.Append("blockquote{margin:1rem 0;padding-left:1rem;border-left:4px solid #888}")
            html.Append("table{border-collapse:collapse;margin:1rem 0;max-width:100%}td,th{border:1px solid #888;padding:.3rem .55rem;text-align:left;vertical-align:top}")
            html.Append("footer{margin-top:2.5rem;font-size:.95rem}a{color:#0645ad}")
            html.Append("@media (prefers-color-scheme:dark){body{background:#121212;color:#eee}a{color:#8ab4f8}}")
            html.Append("</style></head><body><main>")
            html.Append(articolo.HtmlCompleto())
            html.Append("</main>")
            Dim origine As String = If(articolo.Indirizzo, "").Trim()
            If origine.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse origine.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                html.Append("<footer><a href=""").Append(WebUtility.HtmlEncode(origine)).Append(""">Pagina originale</a></footer>")
            End If
            html.Append("</body></html>")
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.DocumentoHtml: " & ex.Message)
        End Try
        Return html.ToString()
    End Function

    Private Shared Function LivelloIntestazione(nome As String) As Integer
        If nome IsNot Nothing AndAlso nome.Length = 2 AndAlso nome(0) = "h"c AndAlso Char.IsDigit(nome(1)) Then Return AscW(nome(1)) - AscW("0"c)
        Return 0
    End Function

    Private Shared Function DentroRigaConsumata(nodo As HAP.HtmlNode, consumate As HashSet(Of HAP.HtmlNode)) As Boolean
        Try
            If consumate.Count = 0 Then Return False
            Dim corrente As HAP.HtmlNode = nodo.ParentNode
            While corrente IsNot Nothing
                If consumate.Contains(corrente) Then Return True
                corrente = corrente.ParentNode
            End While
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.DentroRigaConsumata: " & ex.Message)
        End Try
        Return False
    End Function

    ''' <summary>Celle di una riga di tabella di dati (almeno due, corte, senza paragrafi o elenchi dentro); Nothing se la riga va letta come contenuto normale.</summary>
    Private Shared Function CelleRigaDati(riga As HAP.HtmlNode, ByRef intestazione As Boolean) As List(Of String)
        Try
            Dim celle As New List(Of String)()
            Dim tutteIntestazioni As Boolean = True
            For Each cella As HAP.HtmlNode In riga.ChildNodes
                If cella.NodeType <> HAP.HtmlNodeType.Element Then Continue For
                Dim nome As String = cella.Name.ToLowerInvariant()
                If nome <> "td" AndAlso nome <> "th" Then Continue For
                If cella.Descendants().Any(Function(d) d.NodeType = HAP.HtmlNodeType.Element AndAlso ElementiNonAmmessiInCella.Contains(d.Name)) Then Return Nothing
                Dim valore As String = Normalizza(WebUtility.HtmlDecode(cella.InnerText))
                If valore.Length > LunghezzaMassimaCella Then Return Nothing
                If nome = "td" Then tutteIntestazioni = False
                celle.Add(valore)
            Next
            If celle.Count < 2 Then Return Nothing
            If celle.All(Function(c) c = "") Then Return Nothing
            intestazione = tutteIntestazioni
            Return celle
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.CelleRigaDati: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Testo di un elemento senza quello dei suoi elenchi o blocchi annidati.</summary>
    Private Shared Function TestoProprio(nodo As HAP.HtmlNode) As String
        Dim sb As New StringBuilder()
        Try
            For Each figlio As HAP.HtmlNode In nodo.ChildNodes
                If figlio.NodeType = HAP.HtmlNodeType.Text Then
                    sb.Append(figlio.InnerText)
                ElseIf figlio.NodeType = HAP.HtmlNodeType.Element Then
                    If ElementiBlocco.Contains(figlio.Name) OrElse ElementiStruttura.Contains(figlio.Name) Then Continue For
                    sb.Append(" ").Append(figlio.InnerText).Append(" ")
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.TestoProprio: " & ex.Message)
        End Try
        Return sb.ToString()
    End Function

    ''' <summary>Toglie i richiami di nota scritti come testo, per esempio [1] o [senza fonte].</summary>
    Public Shared Function RimuoviRiferimenti(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            If testo.IndexOf("["c) < 0 Then Return testo
            Dim risultato As String = RegexNoteNelTesto.Replace(testo, "")
            If risultato.Length <> testo.Length Then risultato = RegexSpazioPrimaPunteggiatura.Replace(risultato, "$1")
            Return risultato.Trim()
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.RimuoviRiferimenti: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Shared Function RumoreDiStruttura(nodo As HAP.HtmlNode, isolato As Boolean) As Boolean
        Try
            Dim classi As String = nodo.GetAttributeValue("class", "")
            If classi <> "" Then
                For Each classe As String In classi.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
                    If ClassiSicure.Contains(classe) Then Return True
                    If isolato AndAlso ClassiContenutoIsolato.Contains(classe) Then Return True
                Next
            End If
            If String.Equals(nodo.Name, "sup", StringComparison.OrdinalIgnoreCase) Then
                If RegexRiferimentoEsponente.IsMatch(Normalizza(WebUtility.HtmlDecode(nodo.InnerText))) Then Return True
            End If
            If isolato Then
                Dim id As String = nodo.GetAttributeValue("id", "")
                If id <> "" AndAlso IdContenutoIsolato.Contains(id) Then Return True
                Dim ruolo As String = nodo.GetAttributeValue("role", "")
                If ruolo <> "" AndAlso RuoliContenutoIsolato.Contains(ruolo) Then Return True
                Dim tipo As String = nodo.GetAttributeValue("typeof", "")
                If tipo.StartsWith("mw:File", StringComparison.OrdinalIgnoreCase) Then Return True
            End If
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.RumoreDiStruttura: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function ContenitoreMigliore(radice As HAP.HtmlNode) As HAP.HtmlNode
        Try
            Dim punteggi As New Dictionary(Of HAP.HtmlNode, Double)()
            For Each paragrafo As HAP.HtmlNode In radice.Descendants("p")
                Dim contenuto As String = Normalizza(WebUtility.HtmlDecode(paragrafo.InnerText))
                If contenuto.Length < LunghezzaMinimaParagrafo Then Continue For
                Dim link As Integer = paragrafo.Descendants("a").Sum(Function(a) Normalizza(WebUtility.HtmlDecode(a.InnerText)).Length)
                If link > contenuto.Length * QuotaMassimaLink Then Continue For
                Dim genitore As HAP.HtmlNode = paragrafo.ParentNode
                If genitore Is Nothing Then Continue For
                Aggiungi(punteggi, genitore, contenuto.Length)
                If genitore.ParentNode IsNot Nothing Then Aggiungi(punteggi, genitore.ParentNode, contenuto.Length * 0.5)
            Next
            If punteggi.Count = 0 Then
                Dim articolo As HAP.HtmlNode = radice.Descendants("article").FirstOrDefault()
                If articolo IsNot Nothing Then Return articolo
                Dim principale As HAP.HtmlNode = radice.Descendants("main").FirstOrDefault()
                If principale IsNot Nothing Then Return principale
                Dim corpoPagina As HAP.HtmlNode = radice.Descendants("body").FirstOrDefault()
                Return If(corpoPagina, radice)
            End If
            Return punteggi.OrderByDescending(Function(c) c.Value).First().Key
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.ContenitoreMigliore: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub Aggiungi(punteggi As Dictionary(Of HAP.HtmlNode, Double), nodo As HAP.HtmlNode, valore As Double)
        Try
            Dim attuale As Double = 0
            If punteggi.TryGetValue(nodo, attuale) Then
                punteggi(nodo) = attuale + valore
            Else
                punteggi(nodo) = valore
            End If
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Aggiungi: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Nascosto(nodo As HAP.HtmlNode) As Boolean
        Try
            If nodo.Attributes.Contains("hidden") Then Return True
            If String.Equals(nodo.GetAttributeValue("aria-hidden", ""), "true", StringComparison.OrdinalIgnoreCase) Then Return True
            Dim stile As String = nodo.GetAttributeValue("style", "").Replace(" ", "").ToLowerInvariant()
            Return stile.Contains("display:none") OrElse stile.Contains("visibility:hidden")
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Nascosto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function Meta(radice As HAP.HtmlNode, nome As String) As String
        Try
            For Each nodo As HAP.HtmlNode In radice.Descendants("meta")
                Dim chiave As String = nodo.GetAttributeValue("property", "")
                If chiave = "" Then chiave = nodo.GetAttributeValue("name", "")
                If Not String.Equals(chiave, nome, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim valore As String = Normalizza(WebUtility.HtmlDecode(nodo.GetAttributeValue("content", "")))
                If valore <> "" Then Return valore
            Next
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Meta: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function TestoPrimo(radice As HAP.HtmlNode, nome As String) As String
        Try
            Dim nodo As HAP.HtmlNode = radice.Descendants(nome).FirstOrDefault()
            If nodo Is Nothing Then Return ""
            Return Normalizza(WebUtility.HtmlDecode(nodo.InnerText))
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.TestoPrimo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function AttributoPrimo(radice As HAP.HtmlNode, nome As String, attributo As String) As String
        Try
            For Each nodo As HAP.HtmlNode In radice.Descendants(nome)
                Dim valore As String = nodo.GetAttributeValue(attributo, "").Trim()
                If valore <> "" Then Return valore
            Next
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.AttributoPrimo: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Shared Function PrimoNonVuoto(ParamArray valori As String()) As String
        Try
            For Each valore As String In valori
                If Not String.IsNullOrWhiteSpace(valore) Then Return valore.Trim()
            Next
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.PrimoNonVuoto: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Shared Function HostDi(indirizzo As String) As String
        Try
            Dim uri As Uri = Nothing
            If Uri.TryCreate(If(indirizzo, ""), UriKind.Absolute, uri) Then
                Dim host As String = uri.Host
                If host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then host = host.Substring(4)
                Return host
            End If
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.HostDi: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Shared Function Normalizza(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return RegexSpazi.Replace(testo.Replace(ChrW(160), " "c), " ").Trim()
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Normalizza: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Public Shared Function Compatta(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return RegexNonAlfanumerici.Replace(testo.ToLowerInvariant(), "")
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.Compatta: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function RigaDiRumore(riga As String) As Boolean
        Try
            Dim pulita As String = If(riga, "").Trim()
            If pulita.Length = 0 Then Return True
            If pulita.Length > 160 Then Return False
            Dim minuscola As String = pulita.ToLowerInvariant()
            For Each parola As String In ParoleRumore
                If minuscola.Contains(parola) Then Return True
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.RigaDiRumore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function TestoInHtml(testo As String) As String
        Dim html As New StringBuilder()
        Try
            For Each riga As String In If(testo, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
                Dim pulita As String = riga.Trim()
                If pulita = "" Then Continue For
                html.Append("<p>").Append(WebUtility.HtmlEncode(pulita)).Append("</p>")
            Next
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.TestoInHtml: " & ex.Message)
        End Try
        Return html.ToString()
    End Function

    Public Shared Function DataLeggibile(valore As String) As String
        Try
            Dim pulito As String = If(valore, "").Trim()
            If pulito = "" Then Return ""
            Dim data As DateTimeOffset
            If DateTimeOffset.TryParse(pulito, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal, data) Then
                Dim locale As DateTime = data.ToLocalTime().DateTime
                Dim italiano As New Globalization.CultureInfo("it-IT")
                If pulito.Length <= 10 Then Return locale.ToString("d MMMM yyyy", italiano)
                Return locale.ToString("d MMMM yyyy, 'ore' H:mm", italiano)
            End If
            Return pulito
        Catch ex As Exception
            _log.ScriviLog("WGLettoreArticoli.DataLeggibile: " & ex.Message)
            Return If(valore, "")
        End Try
    End Function
End Class