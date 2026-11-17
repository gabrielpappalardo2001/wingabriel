Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports HtmlAgilityPack

' Importazione e conversione di codice HTML in elementi del documento, con gestione di stili, tabelle e immagini incorporate.
Partial Public Class WGWord

    Private Class StileHtml
        Public Grassetto As Boolean = False
        Public Corsivo As Boolean = False
        Public Sottolineato As Boolean = False
        Public Barrato As Boolean = False
        Public Apice As Boolean = False
        Public Pedice As Boolean = False
        Public Evidenziato As String = "Nessuno"
        Public Colore As String = "Nero"
        Public Carattere As String = "Arial"
        Public Dimensione As Single = 11.0F
        Public Collegamento As String = ""

        Public Function Clona() As StileHtml
            Dim copia As New StileHtml()
            copia.Grassetto = Me.Grassetto
            copia.Corsivo = Me.Corsivo
            copia.Sottolineato = Me.Sottolineato
            copia.Barrato = Me.Barrato
            copia.Apice = Me.Apice
            copia.Pedice = Me.Pedice
            copia.Evidenziato = Me.Evidenziato
            copia.Colore = Me.Colore
            copia.Carattere = Me.Carattere
            copia.Dimensione = Me.Dimensione
            copia.Collegamento = Me.Collegamento
            Return copia
        End Function
    End Class

    Private Class LivelloElenco
        Public Numerato As Boolean = False
        Public Contatore As Integer = 0
    End Class

    Private Class ImportatoreHtml

        Private ReadOnly _registro As Logger
        Private ReadOnly _immagini As Dictionary(Of String, Byte())
        Private ReadOnly _elementi As New List(Of DocElement)()
        Private ReadOnly _elenchi As New List(Of LivelloElenco)()
        Private _paragrafoCorrente As DocParagraph = Nothing
        Private _allineamentoCorrente As String = "left"
        Private _livelloTitoloCorrente As Integer = 0
        Private _immaginiIgnorate As Integer = 0

        Private Shared ReadOnly TagDaScartare As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "script", "style", "head", "title", "meta", "link", "noscript", "iframe", "frame", "frameset",
            "object", "embed", "svg", "canvas", "input", "button", "select", "textarea", "option",
            "map", "area", "video", "audio", "source", "track", "param", "base", "applet"}

        Private Shared ReadOnly TagBlocco As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "blockquote",
            "address", "figure", "figcaption", "dd", "dt", "dl", "center", "fieldset", "legend", "caption",
            "h1", "h2", "h3", "h4", "h5", "h6", "pre"}

        Public Sub New(immaginiInline As Dictionary(Of String, Byte()))
            _registro = New Logger()
            If immaginiInline Is Nothing Then
                _immagini = New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
            Else
                _immagini = New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
                For Each coppia As KeyValuePair(Of String, Byte()) In immaginiInline
                    Dim chiave As String = PulisciIdentificativo(coppia.Key)
                    If String.IsNullOrWhiteSpace(chiave) Then Continue For
                    If Not _immagini.ContainsKey(chiave) Then _immagini.Add(chiave, coppia.Value)
                Next
            End If
        End Sub

        Public ReadOnly Property ImmaginiIgnorate As Integer
            Get
                Return _immaginiIgnorate
            End Get
        End Property

        Private Shared Function PulisciIdentificativo(valore As String) As String
            Try
                If String.IsNullOrWhiteSpace(valore) Then Return ""
                Dim testo As String = valore.Trim()
                If testo.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) Then testo = testo.Substring(4)
                testo = testo.Trim().TrimStart("<"c).TrimEnd(">"c)
                Return testo.Trim()
            Catch
                Return ""
            End Try
        End Function

        Public Function Converti(html As String) As List(Of DocElement)
            Try
                If String.IsNullOrWhiteSpace(html) Then
                    _elementi.Add(New DocParagraph())
                    Return _elementi
                End If

                Dim documento As New HtmlDocument()
                documento.OptionFixNestedTags = True
                documento.OptionAutoCloseOnEnd = True
                documento.OptionCheckSyntax = False
                documento.LoadHtml(html)

                Dim partenza As HtmlNode = NodoDiPartenza(documento)
                If partenza Is Nothing Then partenza = documento.DocumentNode

                VisitaFigli(partenza, New StileHtml(), False)
                ChiudiParagrafo()
                RimuoviParagrafiVuotiInEccesso()

                If _elementi.Count = 0 Then _elementi.Add(New DocParagraph())
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.Converti: " & ex.Message & Environment.NewLine & ex.StackTrace)
                If _elementi.Count = 0 Then _elementi.Add(New DocParagraph())
            End Try
            Return _elementi
        End Function

        Private Function NodoDiPartenza(documento As HtmlDocument) As HtmlNode
            Try
                If documento Is Nothing OrElse documento.DocumentNode Is Nothing Then Return Nothing
                Dim radice As HtmlNode = documento.DocumentNode
                Dim html As HtmlNode = radice.SelectSingleNode("html")
                If html Is Nothing Then html = radice.SelectSingleNode("//html")
                If html IsNot Nothing Then Return html
                Dim corpo As HtmlNode = radice.SelectSingleNode("//body")
                If corpo IsNot Nothing Then Return corpo
                Return radice
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.NodoDiPartenza: " & ex.Message)
                If documento IsNot Nothing Then Return documento.DocumentNode
                Return Nothing
            End Try
        End Function

        Private Sub RimuoviParagrafiVuotiInEccesso()
            Try
                Dim i As Integer = _elementi.Count - 1
                Dim vuotiConsecutivi As Integer = 0
                While i >= 0
                    Dim para As DocParagraph = TryCast(_elementi(i), DocParagraph)
                    Dim vuoto As Boolean = para IsNot Nothing AndAlso (para.Runs Is Nothing OrElse para.Runs.Count = 0) AndAlso Not para.IsPageBreak
                    If vuoto Then
                        vuotiConsecutivi += 1
                        If vuotiConsecutivi > 1 Then _elementi.RemoveAt(i)
                    Else
                        vuotiConsecutivi = 0
                    End If
                    i -= 1
                End While

                While _elementi.Count > 1
                    Dim ultimo As DocParagraph = TryCast(_elementi(_elementi.Count - 1), DocParagraph)
                    If ultimo Is Nothing Then Exit While
                    If ultimo.Runs IsNot Nothing AndAlso ultimo.Runs.Count > 0 Then Exit While
                    If ultimo.IsPageBreak Then Exit While
                    _elementi.RemoveAt(_elementi.Count - 1)
                End While
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.RimuoviParagrafiVuotiInEccesso: " & ex.Message)
            End Try
        End Sub

        Private Function ParagrafoCorrente() As DocParagraph
            Try
                If _paragrafoCorrente Is Nothing Then
                    _paragrafoCorrente = New DocParagraph()
                    _paragrafoCorrente.Alignment = _allineamentoCorrente
                    If _livelloTitoloCorrente >= 1 AndAlso _livelloTitoloCorrente <= 6 Then
                        _paragrafoCorrente.HeadingLevel = _livelloTitoloCorrente
                    End If
                    If _elenchi.Count > 0 Then
                        Dim livello As LivelloElenco = _elenchi(_elenchi.Count - 1)
                        _paragrafoCorrente.IsListItem = True
                        _paragrafoCorrente.IndentLevel = _elenchi.Count - 1
                        If livello.Numerato Then
                            _paragrafoCorrente.NumberingId = 2
                            _paragrafoCorrente.ListNumValue = livello.Contatore.ToString(CultureInfo.InvariantCulture)
                        Else
                            _paragrafoCorrente.NumberingId = 1
                            _paragrafoCorrente.ListBulletChar = "•"
                        End If
                        _paragrafoCorrente.NumberingLevel = _elenchi.Count - 1
                    End If
                    _elementi.Add(_paragrafoCorrente)
                End If
                Return _paragrafoCorrente
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ParagrafoCorrente: " & ex.Message)
                Return New DocParagraph()
            End Try
        End Function

        Private Sub ChiudiParagrafo()
            Try
                _paragrafoCorrente = Nothing
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ChiudiParagrafo: " & ex.Message)
            End Try
        End Sub

        Private Sub AggiungiParagrafoVuoto()
            Try
                ChiudiParagrafo()
                Dim para As DocParagraph = ParagrafoCorrente()
                ChiudiParagrafo()
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AggiungiParagrafoVuoto: " & ex.Message)
            End Try
        End Sub

        Private Sub AggiungiTesto(testo As String, stile As StileHtml)
            Try
                If String.IsNullOrEmpty(testo) Then Return
                Dim para As DocParagraph = ParagrafoCorrente()
                If para.Runs Is Nothing Then para.Runs = New List(Of DocRun)()

                Dim run As New DocRun()
                run.Text = testo
                run.Bold = stile.Grassetto
                run.Italic = stile.Corsivo
                run.Underline = stile.Sottolineato OrElse Not String.IsNullOrWhiteSpace(stile.Collegamento)
                run.Strikethrough = stile.Barrato
                run.IsSuperscript = stile.Apice
                run.IsSubscript = stile.Pedice
                Dim coloreTesto As String = stile.Colore
                Dim evidenziatore As String = stile.Evidenziato
                ' Pulsanti delle mail (es. "Accedi" su fondo scuro) arrivano con testo bianco.
                ' Se lo sfondo scuro non c'e', il bianco sparisce sulla pagina: si ripiega sul nero.
                If ColoreChiaro(coloreTesto) AndAlso Not ColoreScuro(evidenziatore) Then coloreTesto = "Nero"
                run.ColorHex = coloreTesto
                run.HighlightColorHex = evidenziatore
                run.FontFamily = stile.Carattere
                run.FontSize = stile.Dimensione
                If Not String.IsNullOrWhiteSpace(stile.Collegamento) Then
                    run.LinkUrl = stile.Collegamento
                    run.IsHyperlink = True
                End If

                Dim ultimo As DocRun = Nothing
                If para.Runs.Count > 0 Then ultimo = para.Runs(para.Runs.Count - 1)
                If ultimo IsNot Nothing AndAlso RunCompatibili(ultimo, run) Then
                    ultimo.Text = ultimo.Text & run.Text
                Else
                    para.Runs.Add(run)
                End If
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AggiungiTesto: " & ex.Message)
            End Try
        End Sub

        Private Function RunCompatibili(a As DocRun, b As DocRun) As Boolean
            Try
                If a Is Nothing OrElse b Is Nothing Then Return False
                If a.IsImage OrElse b.IsImage Then Return False
                If a.Bold <> b.Bold Then Return False
                If a.Italic <> b.Italic Then Return False
                If a.Underline <> b.Underline Then Return False
                If a.Strikethrough <> b.Strikethrough Then Return False
                If a.IsSuperscript <> b.IsSuperscript Then Return False
                If a.IsSubscript <> b.IsSubscript Then Return False
                If a.FontSize <> b.FontSize Then Return False
                If Not String.Equals(a.ColorHex, b.ColorHex, StringComparison.OrdinalIgnoreCase) Then Return False
                If Not String.Equals(a.HighlightColorHex, b.HighlightColorHex, StringComparison.OrdinalIgnoreCase) Then Return False
                If Not String.Equals(a.FontFamily, b.FontFamily, StringComparison.OrdinalIgnoreCase) Then Return False
                If Not String.Equals(a.LinkUrl, b.LinkUrl, StringComparison.OrdinalIgnoreCase) Then Return False
                Return True
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.RunCompatibili: " & ex.Message)
                Return False
            End Try
        End Function

        Private Sub AggiungiImmagine(dati As Byte(), descrizione As String)
            Try
                Dim para As DocParagraph = ParagrafoCorrente()
                If para.Runs Is Nothing Then para.Runs = New List(Of DocRun)()
                Dim run As New DocRun()
                run.IsImage = True
                run.ImageBytes = dati
                run.Text = If(String.IsNullOrWhiteSpace(descrizione), "immagine", descrizione)
                para.Runs.Add(run)
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AggiungiImmagine: " & ex.Message)
            End Try
        End Sub

        Private Sub VisitaFigli(nodo As HtmlNode, stile As StileHtml, dentroPre As Boolean)
            Try
                If nodo Is Nothing OrElse nodo.ChildNodes Is Nothing Then Return
                For Each figlio As HtmlNode In nodo.ChildNodes
                    VisitaNodo(figlio, stile, dentroPre)
                Next
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaFigli: " & ex.Message)
            End Try
        End Sub

        Private Sub VisitaNodo(nodo As HtmlNode, stile As StileHtml, dentroPre As Boolean)
            Try
                If nodo Is Nothing Then Return

                If nodo.NodeType = HtmlNodeType.Comment Then Return

                If nodo.NodeType = HtmlNodeType.Text Then
                    Dim grezzo As String = HtmlEntity.DeEntitize(nodo.InnerText)
                    If grezzo Is Nothing Then Return
                    If dentroPre Then
                        AggiungiTestoPreformattato(grezzo, stile)
                    Else
                        Dim normalizzato As String = Regex.Replace(grezzo.Replace(ChrW(160), " "c), "\s+", " ")
                        If String.IsNullOrEmpty(normalizzato) Then Return
                        If normalizzato = " " AndAlso (_paragrafoCorrente Is Nothing OrElse _paragrafoCorrente.Runs Is Nothing OrElse _paragrafoCorrente.Runs.Count = 0) Then Return
                        AggiungiTesto(normalizzato, stile)
                    End If
                    Return
                End If

                If nodo.NodeType <> HtmlNodeType.Element Then
                    VisitaFigli(nodo, stile, dentroPre)
                    Return
                End If

                Dim tag As String = If(nodo.Name, "").ToLowerInvariant()

                If TagDaScartare.Contains(tag) Then Return
                If NodoNascosto(nodo) Then Return

                Select Case tag
                    Case "br"
                        ChiudiParagrafo()
                        Return

                    Case "hr"
                        ChiudiParagrafo()
                        Dim separatore As New StileHtml()
                        AggiungiTesto("────────", separatore)
                        ChiudiParagrafo()
                        Return

                    Case "img"
                        VisitaImmagine(nodo)
                        Return

                    Case "table"
                        VisitaTabella(nodo, stile)
                        Return

                    Case "ul", "ol"
                        VisitaElenco(nodo, stile, tag = "ol")
                        Return

                    Case "li"
                        VisitaVoceElenco(nodo, stile)
                        Return

                    Case "b", "strong"
                        Dim s As StileHtml = stile.Clona()
                        s.Grassetto = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "i", "em", "cite", "var", "dfn"
                        Dim s As StileHtml = stile.Clona()
                        s.Corsivo = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "u", "ins"
                        Dim s As StileHtml = stile.Clona()
                        s.Sottolineato = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "s", "strike", "del"
                        Dim s As StileHtml = stile.Clona()
                        s.Barrato = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "sup"
                        Dim s As StileHtml = stile.Clona()
                        s.Apice = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "sub"
                        Dim s As StileHtml = stile.Clona()
                        s.Pedice = True
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "mark"
                        Dim s As StileHtml = stile.Clona()
                        s.Evidenziato = "FFFF00"
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "code", "kbd", "samp", "tt"
                        Dim s As StileHtml = stile.Clona()
                        s.Carattere = "Courier New"
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "a"
                        Dim s As StileHtml = stile.Clona()
                        Dim indirizzo As String = HtmlEntity.DeEntitize(nodo.GetAttributeValue("href", ""))
                        If Not String.IsNullOrWhiteSpace(indirizzo) AndAlso Not indirizzo.Trim().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then
                            s.Collegamento = indirizzo.Trim()
                        End If
                        ApplicaStileInline(nodo, s)
                        VisitaFigli(nodo, s, dentroPre)
                        Return

                    Case "font", "span", "label", "small", "big", "abbr", "time", "bdi", "bdo", "q", "wbr"
                        Dim s As StileHtml = stile.Clona()
                        ApplicaStileInline(nodo, s)
                        If tag = "small" Then s.Dimensione = Math.Max(6.0F, s.Dimensione - 2.0F)
                        If tag = "big" Then s.Dimensione = s.Dimensione + 2.0F
                        VisitaFigli(nodo, s, dentroPre)
                        Return
                End Select

                If TagBlocco.Contains(tag) Then
                    VisitaBlocco(nodo, stile, tag)
                    Return
                End If

                VisitaFigli(nodo, stile, dentroPre)
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaNodo: " & ex.Message)
            End Try
        End Sub

        Private Sub AggiungiTestoPreformattato(testo As String, stile As StileHtml)
            Try
                Dim righe() As String = testo.Replace(vbCrLf, vbLf).Split(ChrW(10))
                For i As Integer = 0 To righe.Length - 1
                    If i > 0 Then ChiudiParagrafo()
                    If righe(i).Length > 0 Then AggiungiTesto(righe(i), stile)
                Next
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AggiungiTestoPreformattato: " & ex.Message)
            End Try
        End Sub

        Private Sub VisitaBlocco(nodo As HtmlNode, stile As StileHtml, tag As String)
            Try
                ChiudiParagrafo()

                Dim allineamentoPrecedente As String = _allineamentoCorrente
                Dim titoloPrecedente As Integer = _livelloTitoloCorrente

                Dim allineamento As String = AllineamentoDiNodo(nodo)
                If Not String.IsNullOrEmpty(allineamento) Then _allineamentoCorrente = allineamento

                Dim s As StileHtml = stile.Clona()

                If tag.Length = 2 AndAlso tag.StartsWith("h") AndAlso Char.IsDigit(tag(1)) Then
                    Dim livello As Integer = CInt(tag.Substring(1))
                    If livello >= 1 AndAlso livello <= 6 Then
                        _livelloTitoloCorrente = livello
                        s.Grassetto = True
                        s.Dimensione = DimensioneTitolo(livello)
                    End If
                ElseIf tag = "blockquote" Then
                    s.Corsivo = True
                End If

                If tag = "pre" Then s.Carattere = "Courier New"

                ApplicaStileInline(nodo, s)

                VisitaFigli(nodo, s, tag = "pre")

                ChiudiParagrafo()
                _allineamentoCorrente = allineamentoPrecedente
                _livelloTitoloCorrente = titoloPrecedente
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaBlocco: " & ex.Message)
            End Try
        End Sub

        Private Shared Function DimensioneTitolo(livello As Integer) As Single
            Select Case livello
                Case 1
                    Return 22.0F
                Case 2
                    Return 18.0F
                Case 3
                    Return 16.0F
                Case 4
                    Return 15.0F
                Case 5
                    Return 14.0F
                Case Else
                    Return 12.0F
            End Select
        End Function

        Private Sub VisitaElenco(nodo As HtmlNode, stile As StileHtml, numerato As Boolean)
            Try
                ChiudiParagrafo()
                Dim livello As New LivelloElenco()
                livello.Numerato = numerato
                livello.Contatore = 0
                _elenchi.Add(livello)
                VisitaFigli(nodo, stile, False)
                ChiudiParagrafo()
                If _elenchi.Count > 0 Then _elenchi.RemoveAt(_elenchi.Count - 1)
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaElenco: " & ex.Message)
                If _elenchi.Count > 0 Then _elenchi.RemoveAt(_elenchi.Count - 1)
            End Try
        End Sub

        Private Sub VisitaVoceElenco(nodo As HtmlNode, stile As StileHtml)
            Try
                ChiudiParagrafo()
                If _elenchi.Count > 0 Then _elenchi(_elenchi.Count - 1).Contatore += 1
                Dim s As StileHtml = stile.Clona()
                ApplicaStileInline(nodo, s)
                VisitaFigli(nodo, s, False)
                ChiudiParagrafo()
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaVoceElenco: " & ex.Message)
            End Try
        End Sub

        Private Sub VisitaImmagine(nodo As HtmlNode)
            Try
                Dim larghezza As Integer = nodo.GetAttributeValue("width", 0)
                Dim altezza As Integer = nodo.GetAttributeValue("height", 0)
                If larghezza > 0 AndAlso larghezza <= 2 AndAlso altezza > 0 AndAlso altezza <= 2 Then Return

                Dim descrizione As String = HtmlEntity.DeEntitize(nodo.GetAttributeValue("alt", ""))
                Dim indirizzo As String = HtmlEntity.DeEntitize(nodo.GetAttributeValue("src", ""))
                If String.IsNullOrWhiteSpace(indirizzo) Then Return
                indirizzo = indirizzo.Trim()

                If indirizzo.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) Then
                    Dim chiave As String = PulisciIdentificativo(indirizzo)
                    Dim dati As Byte() = Nothing
                    If _immagini.TryGetValue(chiave, dati) AndAlso dati IsNot Nothing AndAlso dati.Length > 0 Then
                        AggiungiImmagine(dati, descrizione)
                    Else
                        _immaginiIgnorate += 1
                        AggiungiTesto("[immagine: " & If(String.IsNullOrWhiteSpace(descrizione), "non disponibile", descrizione) & "]", New StileHtml())
                    End If
                    Return
                End If

                If indirizzo.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                    Dim posizione As Integer = indirizzo.IndexOf("base64,", StringComparison.OrdinalIgnoreCase)
                    If posizione > 0 Then
                        Try
                            Dim codificato As String = indirizzo.Substring(posizione + 7).Trim()
                            Dim dati As Byte() = Convert.FromBase64String(codificato)
                            If dati IsNot Nothing AndAlso dati.Length > 0 Then
                                AggiungiImmagine(dati, descrizione)
                                Return
                            End If
                        Catch exBase As Exception
                            _registro.ScriviLog("ImportatoreHtml.VisitaImmagine base64: " & exBase.Message)
                        End Try
                    End If
                    _immaginiIgnorate += 1
                    Return
                End If

                ' Icone sociali e spaziatrici delle newsletter (20x20, alt vuoto): non sono contenuto.
                If String.IsNullOrWhiteSpace(descrizione) AndAlso larghezza > 0 AndAlso larghezza <= 48 AndAlso altezza > 0 AndAlso altezza <= 48 Then Return

                _immaginiIgnorate += 1
                AggiungiTesto("[immagine remota: " & If(String.IsNullOrWhiteSpace(descrizione), "senza descrizione", descrizione) & "]", New StileHtml())
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaImmagine: " & ex.Message)
            End Try
        End Sub

        Private Function RigheDiTabella(nodo As HtmlNode) As List(Of HtmlNode)
            Dim righe As New List(Of HtmlNode)()
            Try
                For Each figlio As HtmlNode In nodo.ChildNodes
                    If figlio.NodeType <> HtmlNodeType.Element Then Continue For
                    Dim nome As String = If(figlio.Name, "").ToLowerInvariant()
                    If nome = "tr" Then
                        righe.Add(figlio)
                    ElseIf nome = "thead" OrElse nome = "tbody" OrElse nome = "tfoot" Then
                        For Each nipote As HtmlNode In figlio.ChildNodes
                            If nipote.NodeType = HtmlNodeType.Element AndAlso String.Equals(nipote.Name, "tr", StringComparison.OrdinalIgnoreCase) Then
                                righe.Add(nipote)
                            End If
                        Next
                    End If
                Next
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.RigheDiTabella: " & ex.Message)
            End Try
            Return righe
        End Function

        Private Function CelleDiRiga(riga As HtmlNode) As List(Of HtmlNode)
            Dim celle As New List(Of HtmlNode)()
            Try
                For Each figlio As HtmlNode In riga.ChildNodes
                    If figlio.NodeType <> HtmlNodeType.Element Then Continue For
                    Dim nome As String = If(figlio.Name, "").ToLowerInvariant()
                    If nome = "td" OrElse nome = "th" Then celle.Add(figlio)
                Next
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.CelleDiRiga: " & ex.Message)
            End Try
            Return celle
        End Function

        Private Function TabellaDiDati(nodo As HtmlNode, righe As List(Of HtmlNode)) As Boolean
            Try
                Dim ruolo As String = nodo.GetAttributeValue("role", "").Trim().ToLowerInvariant()
                If ruolo = "presentation" OrElse ruolo = "none" Then Return False

                If nodo.SelectSingleNode(".//table") IsNot Nothing Then Return False
                If nodo.SelectSingleNode(".//th") IsNot Nothing Then Return True

                If righe Is Nothing OrElse righe.Count < 2 Then Return False

                Dim colonneMassime As Integer = 0
                For Each riga As HtmlNode In righe
                    Dim numero As Integer = CelleDiRiga(riga).Count
                    If numero > colonneMassime Then colonneMassime = numero
                Next
                If colonneMassime < 2 Then Return False

                Dim bordo As String = nodo.GetAttributeValue("border", "").Trim()
                If bordo <> "" AndAlso bordo <> "0" Then Return True

                Dim stile As String = nodo.GetAttributeValue("style", "").ToLowerInvariant()
                If stile.Contains("border") Then
                    If Not Regex.IsMatch(stile, "border\s*:\s*(0|none)") Then Return True
                End If

                Return False
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.TabellaDiDati: " & ex.Message)
                Return False
            End Try
        End Function

        Private Sub VisitaTabella(nodo As HtmlNode, stile As StileHtml)
            Try
                Dim righe As List(Of HtmlNode) = RigheDiTabella(nodo)

                If Not TabellaDiDati(nodo, righe) Then
                    ChiudiParagrafo()
                    For Each riga As HtmlNode In righe
                        For Each cella As HtmlNode In CelleDiRiga(riga)
                            ChiudiParagrafo()
                            VisitaFigli(cella, stile.Clona(), False)
                            ChiudiParagrafo()
                        Next
                    Next
                    ChiudiParagrafo()
                    Return
                End If

                ChiudiParagrafo()

                Dim tabella As New DocTable()
                For Each riga As HtmlNode In righe
                    Dim celle As List(Of HtmlNode) = CelleDiRiga(riga)
                    If celle.Count = 0 Then Continue For

                    Dim rigaDoc As New DocTableRow()
                    Dim rigaIntestazione As Boolean = True

                    For Each cella As HtmlNode In celle
                        Dim intestazione As Boolean = String.Equals(cella.Name, "th", StringComparison.OrdinalIgnoreCase)
                        If Not intestazione Then rigaIntestazione = False

                        Dim cellaDoc As New DocTableCell()
                        cellaDoc.IsHeader = intestazione
                        cellaDoc.ColSpan = Math.Max(1, cella.GetAttributeValue("colspan", 1))
                        cellaDoc.RowSpan = Math.Max(1, cella.GetAttributeValue("rowspan", 1))

                        Dim sfondo As String = ColoreDaStile(cella.GetAttributeValue("style", ""), "background-color")
                        If String.IsNullOrEmpty(sfondo) Then sfondo = ColoreEsadecimale(cella.GetAttributeValue("bgcolor", ""))
                        cellaDoc.ShadingColorHex = If(String.IsNullOrEmpty(sfondo), "Nessuno", sfondo)

                        Dim stileCella As StileHtml = stile.Clona()
                        If intestazione Then stileCella.Grassetto = True
                        ApplicaStileInline(cella, stileCella)

                        Dim interno As New ImportatoreHtml(_immagini)
                        Dim elementiCella As List(Of DocElement) = interno.ConvertiContenutoCella(cella, stileCella)
                        AggiungiElementiInCella(cellaDoc, elementiCella)
                        If cellaDoc.Paragraphs.Count = 0 Then cellaDoc.Paragraphs.Add(New DocParagraph())

                        rigaDoc.Cells.Add(cellaDoc)
                    Next

                    rigaDoc.IsHeader = rigaIntestazione AndAlso rigaDoc.Cells.Count > 0
                    tabella.Rows.Add(rigaDoc)
                Next

                If tabella.Rows.Count > 0 Then
                    _elementi.Add(tabella)
                    AggiungiParagrafoVuoto()
                End If
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.VisitaTabella: " & ex.Message)
            End Try
        End Sub

        Private Sub AggiungiElementiInCella(destinazione As DocTableCell, elementi As List(Of DocElement))
            Try
                If destinazione Is Nothing OrElse elementi Is Nothing Then Return
                If destinazione.Paragraphs Is Nothing Then destinazione.Paragraphs = New List(Of DocParagraph)()
                For Each el As DocElement In elementi
                    If el Is Nothing Then Continue For
                    Dim para As DocParagraph = TryCast(el, DocParagraph)
                    If para IsNot Nothing Then
                        destinazione.Paragraphs.Add(para)
                        Continue For
                    End If
                    Dim tabellaInterna As DocTable = TryCast(el, DocTable)
                    If tabellaInterna Is Nothing OrElse tabellaInterna.Rows Is Nothing Then Continue For
                    For Each rigaInterna As DocTableRow In tabellaInterna.Rows
                        If rigaInterna Is Nothing OrElse rigaInterna.Cells Is Nothing Then Continue For
                        For Each cellaInterna As DocTableCell In rigaInterna.Cells
                            If cellaInterna Is Nothing OrElse cellaInterna.Paragraphs Is Nothing Then Continue For
                            For Each pInterno As DocParagraph In cellaInterna.Paragraphs
                                If pInterno IsNot Nothing Then destinazione.Paragraphs.Add(pInterno)
                            Next
                        Next
                    Next
                Next
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AggiungiElementiInCella: " & ex.Message)
            End Try
        End Sub

        Private Function ConvertiContenutoCella(cella As HtmlNode, stile As StileHtml) As List(Of DocElement)
            Try
                VisitaFigli(cella, stile, False)
                ChiudiParagrafo()
                RimuoviParagrafiVuotiInEccesso()
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ConvertiContenutoCella: " & ex.Message)
            End Try
            Return _elementi
        End Function

        ' True se il nodo ha un elemento figlio oppure un testo non vuoto: in quel caso
        ' font-size:0 sul contenitore non deve nascondere il contenuto.
        Private Function ContenitoreConTestoOFigli(nodo As HtmlNode) As Boolean
            Try
                If nodo Is Nothing OrElse Not nodo.HasChildNodes Then Return False
                For Each figlio As HtmlNode In nodo.ChildNodes
                    If figlio Is Nothing Then Continue For
                    If figlio.NodeType = HtmlNodeType.Element Then Return True
                    If figlio.NodeType = HtmlNodeType.Text Then
                        Dim testo As String = HtmlEntity.DeEntitize(If(figlio.InnerText, ""))
                        If Not String.IsNullOrWhiteSpace(testo) Then Return True
                    End If
                Next
                Return False
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ContenitoreConTestoOFigli: " & ex.Message)
                Return True
            End Try
        End Function

        Private Function NormalizzaHex(colore As String) As String
            Try
                If String.IsNullOrWhiteSpace(colore) Then Return ""
                Dim testo As String = colore.Trim().TrimStart("#"c)
                If testo.Length = 3 AndAlso Regex.IsMatch(testo, "^[0-9a-fA-F]{3}$") Then
                    testo = New String(testo(0), 2) & New String(testo(1), 2) & New String(testo(2), 2)
                End If
                If testo.Length >= 6 Then testo = testo.Substring(0, 6)
                If Regex.IsMatch(testo, "^[0-9a-fA-F]{6}$") Then Return testo.ToUpperInvariant()
                Return ""
            Catch
                Return ""
            End Try
        End Function

        Private Function Luminosita(colore As String) As Integer
            Try
                Dim hex As String = NormalizzaHex(colore)
                If hex.Length <> 6 Then Return -1
                Dim r As Integer = Convert.ToInt32(hex.Substring(0, 2), 16)
                Dim g As Integer = Convert.ToInt32(hex.Substring(2, 2), 16)
                Dim b As Integer = Convert.ToInt32(hex.Substring(4, 2), 16)
                Return (r * 299 + g * 587 + b * 114) \ 1000
            Catch
                Return -1
            End Try
        End Function

        Private Function ColoreChiaro(colore As String) As Boolean
            Dim valore As Integer = Luminosita(colore)
            Return valore >= 186
        End Function

        Private Function ColoreScuro(colore As String) As Boolean
            Dim valore As Integer = Luminosita(colore)
            Return valore >= 0 AndAlso valore < 140
        End Function

        ' "background: #141413" (scorciatoia usata dai pulsanti delle mail), non "background-color"
        ' e non "background-image" / url().
        Private Function ColoreSfondoCompatto(dichiarazioni As String) As String
            Try
                If String.IsNullOrWhiteSpace(dichiarazioni) Then Return ""
                Dim corrispondenza As Match = Regex.Match(dichiarazioni, "(^|;|\s)background\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                If Not corrispondenza.Success Then Return ""
                Dim valore As String = corrispondenza.Groups(2).Value.Trim()
                If valore.Length = 0 Then Return ""
                Dim primaParte As String = valore.Split(" "c)(0).Trim()
                If primaParte.StartsWith("url(", StringComparison.OrdinalIgnoreCase) Then Return ""
                Return ColoreEsadecimale(primaParte)
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ColoreSfondoCompatto: " & ex.Message)
                Return ""
            End Try
        End Function

        Private Function NodoNascosto(nodo As HtmlNode) As Boolean
            Try
                If nodo.Attributes IsNot Nothing AndAlso nodo.Attributes("hidden") IsNot Nothing Then Return True
                If String.Equals(nodo.GetAttributeValue("aria-hidden", ""), "true", StringComparison.OrdinalIgnoreCase) Then Return True
                Dim stile As String = nodo.GetAttributeValue("style", "").ToLowerInvariant().Replace(" ", "")
                If stile.Contains("display:none") Then Return True
                If stile.Contains("visibility:hidden") Then Return True
                If stile.Contains("max-height:0") AndAlso stile.Contains("overflow:hidden") Then Return True
                ' font-size:0 e' il trucco MJML/Thunderbird per togliere lo spazio fra le colonne,
                ' non un nodo nascosto: il testo vero sta nei figli (vedi le mail di Anthropic).
                ' Si scarta solo lo spaziatore che non contiene ne' elementi ne' testo.
                If stile.Contains("font-size:0") AndAlso Not ContenitoreConTestoOFigli(nodo) Then Return True
                Return False
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.NodoNascosto: " & ex.Message)
                Return False
            End Try
        End Function

        Private Function AllineamentoDiNodo(nodo As HtmlNode) As String
            Try
                Dim allineamento As String = nodo.GetAttributeValue("align", "").Trim().ToLowerInvariant()
                If allineamento = "" Then
                    Dim stile As String = nodo.GetAttributeValue("style", "").ToLowerInvariant()
                    Dim corrispondenza As Match = Regex.Match(stile, "text-align\s*:\s*([a-z]+)")
                    If corrispondenza.Success Then allineamento = corrispondenza.Groups(1).Value
                End If
                Select Case allineamento
                    Case "center"
                        Return "center"
                    Case "right"
                        Return "right"
                    Case "justify"
                        Return "justify"
                    Case "left", "start"
                        Return "left"
                End Select
                Return ""
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.AllineamentoDiNodo: " & ex.Message)
                Return ""
            End Try
        End Function

        Private Sub ApplicaStileInline(nodo As HtmlNode, stile As StileHtml)
            Try
                Dim colore As String = ColoreEsadecimale(nodo.GetAttributeValue("color", ""))
                If Not String.IsNullOrEmpty(colore) Then stile.Colore = colore

                Dim carattere As String = nodo.GetAttributeValue("face", "").Trim()
                If Not String.IsNullOrWhiteSpace(carattere) Then stile.Carattere = PrimoCarattere(carattere)

                Dim dichiarazioni As String = nodo.GetAttributeValue("style", "")
                If String.IsNullOrWhiteSpace(dichiarazioni) Then Return
                Dim minuscolo As String = dichiarazioni.ToLowerInvariant()

                If Regex.IsMatch(minuscolo, "font-weight\s*:\s*(bold|[6-9]00)") Then stile.Grassetto = True
                If Regex.IsMatch(minuscolo, "font-weight\s*:\s*(normal|[1-4]00)") Then stile.Grassetto = False
                If Regex.IsMatch(minuscolo, "font-style\s*:\s*italic") Then stile.Corsivo = True
                If Regex.IsMatch(minuscolo, "font-style\s*:\s*normal") Then stile.Corsivo = False
                If Regex.IsMatch(minuscolo, "text-decoration[^;]*underline") Then stile.Sottolineato = True
                If Regex.IsMatch(minuscolo, "text-decoration[^;]*line-through") Then stile.Barrato = True

                Dim coloreStile As String = ColoreDaStile(dichiarazioni, "color")
                If Not String.IsNullOrEmpty(coloreStile) Then stile.Colore = coloreStile

                Dim sfondoStile As String = ColoreDaStile(dichiarazioni, "background-color")
                If String.IsNullOrEmpty(sfondoStile) Then sfondoStile = ColoreSfondoCompatto(dichiarazioni)
                If String.IsNullOrEmpty(sfondoStile) Then sfondoStile = ColoreEsadecimale(nodo.GetAttributeValue("bgcolor", ""))
                ' Uno sfondo chiaro non si riporta: sulla pagina bianca non aggiunge nulla e
                ' coprirebbe il testo. Uno scuro (pulsante) si tiene, cosi' il testo chiaro resta leggibile.
                If Not String.IsNullOrEmpty(sfondoStile) AndAlso Not ColoreChiaro(sfondoStile) Then stile.Evidenziato = sfondoStile

                Dim famiglia As Match = Regex.Match(dichiarazioni, "font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                If famiglia.Success Then
                    Dim nome As String = PrimoCarattere(famiglia.Groups(1).Value)
                    If Not String.IsNullOrWhiteSpace(nome) Then stile.Carattere = nome
                End If

                Dim dimensione As Match = Regex.Match(dichiarazioni, "font-size\s*:\s*([0-9]+(?:\.[0-9]+)?)\s*(pt|px|em|rem)?", RegexOptions.IgnoreCase)
                If dimensione.Success Then
                    Dim valore As Double = 0
                    If Double.TryParse(dimensione.Groups(1).Value, NumberStyles.Float, CultureInfo.InvariantCulture, valore) AndAlso valore > 0 Then
                        Dim unita As String = dimensione.Groups(2).Value.ToLowerInvariant()
                        Dim punti As Double = valore
                        If unita = "px" Then punti = valore * 72.0 / 96.0
                        If unita = "em" OrElse unita = "rem" Then punti = valore * 11.0
                        If punti >= 4 AndAlso punti <= 96 Then stile.Dimensione = CSng(Math.Round(punti, 1))
                    End If
                End If
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ApplicaStileInline: " & ex.Message)
            End Try
        End Sub

        Private Function PrimoCarattere(elenco As String) As String
            Try
                If String.IsNullOrWhiteSpace(elenco) Then Return ""
                Dim parti() As String = elenco.Split(","c)
                Dim nome As String = parti(0).Trim().Trim(""""c).Trim("'"c).Trim()
                If String.IsNullOrWhiteSpace(nome) Then Return ""
                If nome.Length > 40 Then Return ""
                Return nome
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.PrimoCarattere: " & ex.Message)
                Return ""
            End Try
        End Function

        Private Function ColoreDaStile(dichiarazioni As String, proprieta As String) As String
            Try
                If String.IsNullOrWhiteSpace(dichiarazioni) Then Return ""
                Dim schema As String = "(^|;|\s)" & Regex.Escape(proprieta) & "\s*:\s*([^;]+)"
                Dim corrispondenza As Match = Regex.Match(dichiarazioni, schema, RegexOptions.IgnoreCase)
                If Not corrispondenza.Success Then Return ""
                Return ColoreEsadecimale(corrispondenza.Groups(2).Value)
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ColoreDaStile: " & ex.Message)
                Return ""
            End Try
        End Function

        Private Function ColoreEsadecimale(valore As String) As String
            Try
                If String.IsNullOrWhiteSpace(valore) Then Return ""
                Dim testo As String = valore.Trim().ToLowerInvariant()
                If testo = "transparent" OrElse testo = "inherit" OrElse testo = "initial" OrElse testo = "none" Then Return ""

                If testo.StartsWith("#") Then
                    Dim esadecimale As String = testo.Substring(1)
                    If esadecimale.Length = 3 Then
                        esadecimale = New String(esadecimale(0), 2) & New String(esadecimale(1), 2) & New String(esadecimale(2), 2)
                    End If
                    If esadecimale.Length >= 6 Then esadecimale = esadecimale.Substring(0, 6)
                    If Regex.IsMatch(esadecimale, "^[0-9a-f]{6}$") Then Return esadecimale.ToUpperInvariant()
                    Return ""
                End If

                Dim rgb As Match = Regex.Match(testo, "rgba?\s*\(\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})")
                If rgb.Success Then
                    Dim r As Integer = CInt(rgb.Groups(1).Value)
                    Dim g As Integer = CInt(rgb.Groups(2).Value)
                    Dim b As Integer = CInt(rgb.Groups(3).Value)
                    Return r.ToString("X2") & g.ToString("X2") & b.ToString("X2")
                End If

                Select Case testo
                    Case "black"
                        Return "000000"
                    Case "white"
                        Return "FFFFFF"
                    Case "red"
                        Return "FF0000"
                    Case "green"
                        Return "008000"
                    Case "blue"
                        Return "0000FF"
                    Case "yellow"
                        Return "FFFF00"
                    Case "orange"
                        Return "FFA500"
                    Case "purple"
                        Return "800080"
                    Case "gray", "grey"
                        Return "808080"
                    Case "brown"
                        Return "A52A2A"
                    Case "pink"
                        Return "FFC0CB"
                    Case "silver"
                        Return "C0C0C0"
                    Case "navy"
                        Return "000080"
                    Case "teal"
                        Return "008080"
                    Case "olive"
                        Return "808000"
                    Case "maroon"
                        Return "800000"
                    Case "lime"
                        Return "00FF00"
                    Case "aqua", "cyan"
                        Return "00FFFF"
                    Case "fuchsia", "magenta"
                        Return "FF00FF"
                End Select

                Return ""
            Catch ex As Exception
                _registro.ScriviLog("ImportatoreHtml.ColoreEsadecimale: " & ex.Message)
                Return ""
            End Try
        End Function

    End Class

    Public Function ImportaHtml(html As String, Optional titolo As String = "", Optional immaginiInline As Dictionary(Of String, Byte()) = Nothing) As Boolean
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return False

            Dim importatore As New ImportatoreHtml(immaginiInline)
            Dim elementi As List(Of DocElement) = importatore.Converti(html)
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                elementi = New List(Of DocElement)()
                elementi.Add(New DocParagraph())
            End If

            SyncLock _elementsLock
                _elements = elementi
            End SyncLock

            _caretIndex = 0
            _caretCharIndex = 0
            _tblRow = 0
            _tblCol = 0
            _tblPara = 0
            _selectionActive = False
            _vScrollPos = 0
            _isModified = False
            If Not String.IsNullOrWhiteSpace(titolo) Then _docTitle = titolo

            Try
                _undoStack.Clear()
                _redoStack.Clear()
            Catch exPile As Exception
                logger.ScriviLog("WGWord.ImportaHtml (pile): " & exPile.Message)
            End Try

            Try
                TriggerLayout()
            Catch exLayout As Exception
                logger.ScriviLog("WGWord.ImportaHtml (layout): " & exLayout.Message)
            End Try

            Try
                UpdateCaretAndNotify(True)
            Catch exCursore As Exception
                logger.ScriviLog("WGWord.ImportaHtml (cursore): " & exCursore.Message)
            End Try

            Try
                Me.Invalidate()
            Catch exDisegno As Exception
                logger.ScriviLog("WGWord.ImportaHtml (disegno): " & exDisegno.Message)
            End Try

            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.ImportaHtml: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

End Class
