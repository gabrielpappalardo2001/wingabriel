Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text

' Elaborazione e conversione dei messaggi di posta elettronica e dei relativi contenuti multimediali.
Public Class WGImmaginePosta
    Public Property Cid As String = ""
    Public Property NomeFile As String = ""
    Public Property TipoMime As String = ""
    Public Property Dati As Byte() = Nothing
End Class

Public Class WGContenutoPosta
    Public Property Html As String = ""
    Public Property Testo As String = ""
    Public Property Immagini As List(Of WGImmaginePosta) = New List(Of WGImmaginePosta)()
End Class

Partial Public Class WGWord

    Private Function TestoPostaSicuro(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim sb As New StringBuilder()
            For Each c As Char In testo
                Select Case c
                    Case "&"c
                        sb.Append("&amp;")
                    Case "<"c
                        sb.Append("&lt;")
                    Case ">"c
                        sb.Append("&gt;")
                    Case """"c
                        sb.Append("&quot;")
                    Case ControlChars.Lf
                        sb.Append("<br />")
                    Case ControlChars.Cr
                    Case Else
                        sb.Append(c)
                End Select
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.TestoPostaSicuro: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ColorePostaValido(nome As String) As String
        Try
            Dim hex As String = ConvertNomeColoreAHex(nome)
            If String.IsNullOrWhiteSpace(hex) Then Return ""
            If hex.Equals("auto", StringComparison.OrdinalIgnoreCase) Then Return ""
            Return hex
        Catch ex As Exception
            logger.ScriviLog("WGWord.ColorePostaValido: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RegistraImmaginePosta(dati As Byte(), descrizione As String, raccolta As List(Of WGImmaginePosta)) As String
        Try
            If dati Is Nothing OrElse dati.Length = 0 OrElse raccolta Is Nothing Then Return ""
            Dim formato As FormatoImmagine = RilevaFormatoImmagine(dati)
            If formato = FormatoImmagine.Vettoriale Then Return ""

            Dim datiFinali As Byte() = dati
            Dim tipoMime As String = "image/jpeg"
            Dim estensione As String = "jpg"

            If formato = FormatoImmagine.Png Then
                tipoMime = "image/png"
                estensione = "png"
            ElseIf formato = FormatoImmagine.Jpeg Then
                tipoMime = "image/jpeg"
                estensione = "jpg"
            Else
                Dim convertita As Byte() = ConvertePngInJpeg(dati)
                If convertita Is Nothing OrElse convertita.Length = 0 Then Return ""
                datiFinali = convertita
                tipoMime = "image/jpeg"
                estensione = "jpg"
            End If

            Dim progressivo As Integer = raccolta.Count + 1
            Dim immagine As New WGImmaginePosta()
            immagine.Cid = "wgimg" & progressivo.ToString(CultureInfo.InvariantCulture) & "@wingabriel"
            immagine.NomeFile = "immagine" & progressivo.ToString(CultureInfo.InvariantCulture) & "." & estensione
            immagine.TipoMime = tipoMime
            immagine.Dati = datiFinali
            raccolta.Add(immagine)
            Return immagine.Cid
        Catch ex As Exception
            logger.ScriviLog("WGWord.RegistraImmaginePosta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RunInPostaHtml(r As DocRun, raccolta As List(Of WGImmaginePosta)) As String
        Try
            If r Is Nothing Then Return ""

            If r.IsImage Then
                Dim cid As String = RegistraImmaginePosta(r.ImageBytes, r.Text, raccolta)
                Dim alternativo As String = TestoPostaSicuro(If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text))
                If String.IsNullOrEmpty(cid) Then Return "[" & alternativo & "]"
                Dim dimensioni As New StringBuilder()
                dimensioni.Append("max-width:100%;height:auto;")
                If r.ImageWidthPt > 0 Then
                    dimensioni.Append("width:").Append(CInt(Math.Round(r.ImageWidthPt * 96.0 / 72.0)).ToString(CultureInfo.InvariantCulture)).Append("px;")
                End If
                Return "<img src=""cid:" & cid & """ alt=""" & alternativo & """ style=""" & dimensioni.ToString() & """ />"
            End If

            If String.IsNullOrEmpty(r.Text) Then Return ""

            Dim stile As New StringBuilder()
            If Not String.IsNullOrWhiteSpace(r.FontFamily) Then
                stile.Append("font-family:'").Append(r.FontFamily.Trim()).Append("',Arial,sans-serif;")
            End If
            If r.FontSize > 0 Then
                stile.Append("font-size:").Append(r.FontSize.ToString("0.##", CultureInfo.InvariantCulture)).Append("pt;")
            End If
            If r.Bold Then stile.Append("font-weight:bold;")
            If r.Italic Then stile.Append("font-style:italic;")

            Dim decorazioni As New List(Of String)()
            If r.Underline Then decorazioni.Add("underline")
            If r.Strikethrough Then decorazioni.Add("line-through")
            If decorazioni.Count > 0 Then
                stile.Append("text-decoration:").Append(String.Join(" ", decorazioni.ToArray())).Append(";")
            End If

            Dim coloreTesto As String = ColorePostaValido(r.ColorHex)
            If coloreTesto <> "" AndAlso coloreTesto <> "000000" Then
                stile.Append("color:#").Append(coloreTesto).Append(";")
            End If

            Dim coloreSfondo As String = ColorePostaValido(r.HighlightColorHex)
            If coloreSfondo <> "" AndAlso coloreSfondo <> "000000" Then
                stile.Append("background-color:#").Append(coloreSfondo).Append(";")
            End If

            If r.SmallCaps Then stile.Append("font-variant:small-caps;")
            If r.AllCaps Then stile.Append("text-transform:uppercase;")

            Dim contenuto As String = TestoPostaSicuro(r.Text)
            If r.IsSuperscript Then contenuto = "<sup>" & contenuto & "</sup>"
            If r.IsSubscript Then contenuto = "<sub>" & contenuto & "</sub>"

            If stile.Length > 0 Then
                contenuto = "<span style=""" & stile.ToString() & """>" & contenuto & "</span>"
            End If

            If r.IsHyperlink OrElse Not String.IsNullOrWhiteSpace(r.LinkUrl) Then
                If Not String.IsNullOrWhiteSpace(r.LinkUrl) AndAlso Not r.LinkUrl.StartsWith("#") Then
                    contenuto = "<a href=""" & TestoPostaSicuro(r.LinkUrl) & """>" & contenuto & "</a>"
                End If
            End If

            Return contenuto
        Catch ex As Exception
            logger.ScriviLog("WGWord.RunInPostaHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RunsInPostaHtml(runs As List(Of DocRun), raccolta As List(Of WGImmaginePosta)) As String
        Try
            If runs Is Nothing OrElse runs.Count = 0 Then Return ""
            Dim sb As New StringBuilder()
            For Each r As DocRun In runs
                sb.Append(RunInPostaHtml(r, raccolta))
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.RunsInPostaHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function StileParagrafoPosta(para As DocParagraph) As String
        Try
            If para Is Nothing Then Return ""
            Dim stile As New StringBuilder()
            stile.Append("margin:0;")
            If para.SpacingBefore > 0 Then
                stile.Append("margin-top:").Append(Math.Round(para.SpacingBefore, 1).ToString(CultureInfo.InvariantCulture)).Append("pt;")
            End If
            If para.SpacingAfter > 0 Then
                stile.Append("margin-bottom:").Append(Math.Round(para.SpacingAfter, 1).ToString(CultureInfo.InvariantCulture)).Append("pt;")
            End If
            If para.LineSpacing > 0 AndAlso para.LineSpacing <> 1.0F Then
                stile.Append("line-height:").Append(Math.Round(para.LineSpacing, 2).ToString(CultureInfo.InvariantCulture)).Append(";")
            End If
            If para.IndentLevel > 0 Then
                stile.Append("margin-left:").Append((para.IndentLevel * 30).ToString(CultureInfo.InvariantCulture)).Append("px;")
            End If
            Select Case If(para.Alignment, "left").ToLowerInvariant()
                Case "center"
                    stile.Append("text-align:center;")
                Case "right"
                    stile.Append("text-align:right;")
                Case "justify"
                    stile.Append("text-align:justify;")
            End Select
            Return stile.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.StileParagrafoPosta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function LivelloTitoloPosta(para As DocParagraph) As Integer
        Try
            If para Is Nothing Then Return 0
            If para.HeadingLevel >= 1 AndAlso para.HeadingLevel <= 6 Then Return para.HeadingLevel
            If Not String.IsNullOrEmpty(para.BookmarkId) AndAlso _stiliTitolo IsNot Nothing AndAlso _stiliTitolo.ContainsKey(para.BookmarkId) Then
                Dim livelloSalvato As Integer = _stiliTitolo(para.BookmarkId)
                If livelloSalvato >= 1 AndAlso livelloSalvato <= 6 Then Return livelloSalvato
            End If
            If para.Runs Is Nothing Then Return 0
            Dim primoRun As DocRun = para.Runs.FirstOrDefault(Function(r) r IsNot Nothing AndAlso Not r.IsImage AndAlso Not String.IsNullOrWhiteSpace(r.Text))
            If primoRun Is Nothing Then Return 0
            If Not primoRun.Bold Then Return 0
            If primoRun.FontSize >= 22 Then Return 1
            If primoRun.FontSize >= 18 Then Return 2
            If primoRun.FontSize >= 16 Then Return 3
            If primoRun.FontSize >= 15 Then Return 4
            If primoRun.FontSize >= 14 Then Return 5
            Return 0
        Catch ex As Exception
            logger.ScriviLog("WGWord.LivelloTitoloPosta: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub TabellaInPostaHtml(tabella As DocTable, sb As StringBuilder, raccolta As List(Of WGImmaginePosta))
        Try
            If tabella Is Nothing OrElse sb Is Nothing Then Return
            sb.Append("<table border=""1"" cellspacing=""0"" cellpadding=""6"" style=""border-collapse:collapse;border:1px solid #999999;margin:10px 0;"">")
            If tabella.Rows IsNot Nothing Then
                For Each riga As DocTableRow In tabella.Rows
                    If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                    sb.Append("<tr>")
                    For Each cella As DocTableCell In riga.Cells
                        If cella Is Nothing Then Continue For
                        Dim intestazione As Boolean = riga.IsHeader OrElse cella.IsHeader
                        Dim tagCella As String = If(intestazione, "th", "td")

                        Dim attributi As New StringBuilder()
                        If cella.ColSpan > 1 Then attributi.Append(" colspan=""").Append(cella.ColSpan.ToString(CultureInfo.InvariantCulture)).Append("""")
                        If cella.RowSpan > 1 Then attributi.Append(" rowspan=""").Append(cella.RowSpan.ToString(CultureInfo.InvariantCulture)).Append("""")

                        Dim stileCella As New StringBuilder()
                        stileCella.Append("border:1px solid #999999;padding:6px;vertical-align:top;")
                        If intestazione Then stileCella.Append("font-weight:bold;background-color:#f0f0f0;")
                        Dim sfondo As String = ColorePostaValido(cella.ShadingColorHex)
                        If sfondo <> "" AndAlso sfondo <> "000000" Then
                            stileCella.Append("background-color:#").Append(sfondo).Append(";")
                        End If

                        sb.Append("<").Append(tagCella).Append(attributi.ToString())
                        sb.Append(" style=""").Append(stileCella.ToString()).Append(""">")

                        If cella.Paragraphs IsNot Nothing AndAlso cella.Paragraphs.Count > 0 Then
                            For Each pCella As DocParagraph In cella.Paragraphs
                                If pCella Is Nothing Then Continue For
                                sb.Append("<p style=""").Append(StileParagrafoPosta(pCella)).Append(""">")
                                Dim contenutoCella As String = RunsInPostaHtml(pCella.Runs, raccolta)
                                If String.IsNullOrEmpty(contenutoCella) Then contenutoCella = "&nbsp;"
                                sb.Append(contenutoCella)
                                sb.Append("</p>")
                            Next
                        Else
                            sb.Append("&nbsp;")
                        End If

                        sb.Append("</").Append(tagCella).Append(">")
                    Next
                    sb.Append("</tr>")
                Next
            End If
            sb.Append("</table>")
        Catch ex As Exception
            logger.ScriviLog("WGWord.TabellaInPostaHtml: " & ex.Message)
        End Try
    End Sub

    Private Sub TabellaInPostaTesto(tabella As DocTable, sb As StringBuilder)
        Try
            If tabella Is Nothing OrElse sb Is Nothing OrElse tabella.Rows Is Nothing Then Return
            For Each riga As DocTableRow In tabella.Rows
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                Dim valori As New List(Of String)()
                For Each cella As DocTableCell In riga.Cells
                    If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then
                        valori.Add("")
                        Continue For
                    End If
                    Dim parti As New List(Of String)()
                    For Each pCella As DocParagraph In cella.Paragraphs
                        If pCella Is Nothing Then Continue For
                        Dim testoCella As String = pCella.GetText()
                        If Not String.IsNullOrWhiteSpace(testoCella) Then parti.Add(testoCella.Trim())
                    Next
                    valori.Add(String.Join(" ", parti.ToArray()))
                Next
                sb.AppendLine(String.Join(" | ", valori.ToArray()))
            Next
            sb.AppendLine()
        Catch ex As Exception
            logger.ScriviLog("WGWord.TabellaInPostaTesto: " & ex.Message)
        End Try
    End Sub

    Public Function GeneraContenutoPosta() As WGContenutoPosta
        Dim risultato As New WGContenutoPosta()
        Try
            Dim elementiLocali As New List(Of DocElement)()
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el As DocElement In _elements
                        If el IsNot Nothing Then elementiLocali.Add(el)
                    Next
                End If
            End SyncLock

            Dim sbHtml As New StringBuilder()
            Dim sbTesto As New StringBuilder()

            sbHtml.Append("<html><body style=""font-family:Arial,Helvetica,sans-serif;font-size:11pt;color:#000000;"">")

            Dim inLista As Boolean = False
            Dim tipoListaCorrente As String = ""

            For Each el As DocElement In elementiLocali
                If TypeOf el Is DocParagraph Then
                    Dim para As DocParagraph = DirectCast(el, DocParagraph)

                    If para.IsPageBreak Then
                        If inLista Then
                            sbHtml.Append("</").Append(tipoListaCorrente).Append(">")
                            inLista = False
                            tipoListaCorrente = ""
                        End If
                        sbHtml.Append("<hr />")
                        sbTesto.AppendLine()
                        Continue For
                    End If

                    Dim testoParagrafo As String = para.GetText()

                    If para.IsListItem Then
                        Dim tipoLista As String = If(Not String.IsNullOrEmpty(para.ListNumValue), "ol", "ul")
                        If Not inLista OrElse tipoListaCorrente <> tipoLista Then
                            If inLista Then sbHtml.Append("</").Append(tipoListaCorrente).Append(">")
                            sbHtml.Append("<").Append(tipoLista).Append(" style=""margin:6px 0 6px 24px;padding:0;"">")
                            inLista = True
                            tipoListaCorrente = tipoLista
                        End If
                        sbHtml.Append("<li>")
                        sbHtml.Append(RunsInPostaHtml(para.Runs, risultato.Immagini))
                        sbHtml.Append("</li>")
                        sbTesto.Append("- ").AppendLine(testoParagrafo)
                        Continue For
                    End If

                    If inLista Then
                        sbHtml.Append("</").Append(tipoListaCorrente).Append(">")
                        inLista = False
                        tipoListaCorrente = ""
                    End If

                    Dim livello As Integer = LivelloTitoloPosta(para)
                    Dim tag As String = If(livello >= 1 AndAlso livello <= 6, "h" & livello.ToString(CultureInfo.InvariantCulture), "p")

                    sbHtml.Append("<").Append(tag).Append(" style=""").Append(StileParagrafoPosta(para)).Append(""">")
                    Dim contenuto As String = RunsInPostaHtml(para.Runs, risultato.Immagini)
                    If String.IsNullOrEmpty(contenuto) Then contenuto = "&nbsp;"
                    sbHtml.Append(contenuto)
                    sbHtml.Append("</").Append(tag).Append(">")

                    sbTesto.AppendLine(testoParagrafo)

                ElseIf TypeOf el Is DocTable Then
                    If inLista Then
                        sbHtml.Append("</").Append(tipoListaCorrente).Append(">")
                        inLista = False
                        tipoListaCorrente = ""
                    End If
                    TabellaInPostaHtml(DirectCast(el, DocTable), sbHtml, risultato.Immagini)
                    TabellaInPostaTesto(DirectCast(el, DocTable), sbTesto)
                End If
            Next

            If inLista Then
                sbHtml.Append("</").Append(tipoListaCorrente).Append(">")
            End If

            sbHtml.Append("</body></html>")

            risultato.Html = sbHtml.ToString()
            risultato.Testo = sbTesto.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.GeneraContenutoPosta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Public Function GeneraHtmlDocumento() As String
        Try
            Dim contenuto As WGContenutoPosta = GeneraContenutoPosta()
            If contenuto Is Nothing Then Return ""
            Return contenuto.Html
        Catch ex As Exception
            logger.ScriviLog("WGWord.GeneraHtmlDocumento: " & ex.Message)
            Return ""
        End Try
    End Function

End Class
