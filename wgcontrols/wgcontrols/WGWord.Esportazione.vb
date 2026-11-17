Imports System.Windows.Forms
Imports System.Drawing
Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Diagnostics
Imports System.Text.RegularExpressions
Imports SymSpell
Imports System.Threading.Tasks
Imports UglyToad.PdfPig.Fonts.Standard14Fonts
Imports UglyToad.PdfPig.Writer
Imports UglyToad.PdfPig.Content
Imports System.IO.Compression
Imports System.IO.Compression.ZipFile
Imports System.Linq
Imports UglyToad.PdfPig

' Esportazione dei documenti in vari formati tra cui HTML, testo, RTF, EPUB, Markdown e PDF, oltre all'importazione da PDF.
Partial Public Class WGWord
    Public Sub SalvaCopia(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim t As New Thread(AddressOf SaveDocxInternal)
            t.IsBackground = True
            t.Start(percorso)
            MostraMessaggioStato("Copia salvata in " & percorso)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub EsportaHtml(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim sb As New StringBuilder()
            sb.AppendLine("<!DOCTYPE html>")
            sb.AppendLine("<html lang=""it""><head><meta charset=""utf-8""><title>" & Net.WebUtility.HtmlEncode(_docTitle) & "</title></head><body>")
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            Dim livelloTitolo As Integer = 0
                            If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso _stiliTitolo.ContainsKey(p.BookmarkId) Then
                                livelloTitolo = _stiliTitolo(p.BookmarkId)
                            End If
                            Dim tagApertura As String
                            Dim tagChiusura As String
                            If livelloTitolo >= 1 AndAlso livelloTitolo <= 6 Then
                                tagApertura = "<h" & livelloTitolo & ">"
                                tagChiusura = "</h" & livelloTitolo & ">"
                            ElseIf p.IsPageBreak Then
                                sb.AppendLine("<hr>")
                                Continue For
                            Else
                                tagApertura = "<p>"
                                tagChiusura = "</p>"
                            End If
                            sb.Append(tagApertura)
                            If p.Runs IsNot Nothing Then
                            For Each r As DocRun In p.Runs
                                If r Is Nothing Then Continue For
                                If r.IsImage Then
                                    If r.ImageBytes IsNot Nothing AndAlso r.ImageBytes.Length > 0 Then
                                        Dim fmt As FormatoImmagine = RilevaFormatoImmagine(r.ImageBytes)
                                        If fmt = FormatoImmagine.Vettoriale Then
                                            sb.Append("[" & Net.WebUtility.HtmlEncode(If(String.IsNullOrWhiteSpace(r.Text), "immagine vettoriale", r.Text)) & "]")
                                        Else
                                            Dim mimeType As String = If(fmt = FormatoImmagine.Png, "image/png", "image/jpeg")
                                            Dim datiImg As Byte() = r.ImageBytes
                                            If fmt <> FormatoImmagine.Jpeg AndAlso fmt <> FormatoImmagine.Png Then
                                                Dim conv As Byte() = ConvertePngInJpeg(r.ImageBytes)
                                                If conv IsNot Nothing AndAlso conv.Length > 0 Then datiImg = conv Else datiImg = Nothing
                                                mimeType = "image/jpeg"
                                            End If
                                            If datiImg IsNot Nothing AndAlso datiImg.Length > 0 Then
                                                Dim altHtml As String = Net.WebUtility.HtmlEncode(If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text))
                                                Dim b64 As String = Convert.ToBase64String(datiImg)
                                                sb.Append("<img src=""data:" & mimeType & ";base64," & b64 & """ alt=""" & altHtml & """ style=""max-width:100%;height:auto;"">")
                                            Else
                                                sb.Append("[" & Net.WebUtility.HtmlEncode(If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text)) & "]")
                                            End If
                                        End If
                                    Else
                                        sb.Append("[" & Net.WebUtility.HtmlEncode(If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text)) & "]")
                                    End If
                                Else
                                    Dim testoRun As String = Net.WebUtility.HtmlEncode(r.Text)
                                    If r.Bold Then testoRun = "<strong>" & testoRun & "</strong>"
                                    If r.Italic Then testoRun = "<em>" & testoRun & "</em>"
                                    If r.Underline Then testoRun = "<u>" & testoRun & "</u>"
                                    If r.Strikethrough Then testoRun = "<s>" & testoRun & "</s>"
                                    If Not String.IsNullOrEmpty(r.LinkUrl) Then testoRun = "<a href=""" & Net.WebUtility.HtmlEncode(r.LinkUrl) & """>" & testoRun & "</a>"
                                    sb.Append(testoRun)
                                End If
                            Next
                            End If
                            sb.AppendLine(tagChiusura)
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            sb.AppendLine("<table border=""1"">")
                            If tbl.Rows IsNot Nothing Then
                            For Each row In tbl.Rows
                                If row Is Nothing OrElse row.Cells Is Nothing Then Continue For
                                sb.Append("<tr>")
                                For Each cell In row.Cells
                                    If cell Is Nothing OrElse cell.Paragraphs Is Nothing Then Continue For
                                    sb.Append("<td>")
                                    For Each pCell In cell.Paragraphs
                                        If pCell Is Nothing OrElse pCell.Runs Is Nothing Then Continue For
                                        For Each rCell As DocRun In pCell.Runs
                                            If rCell Is Nothing Then Continue For
                                            If rCell.IsImage Then
                                                If rCell.ImageBytes IsNot Nothing AndAlso rCell.ImageBytes.Length > 0 Then
                                                    Dim fmtC As FormatoImmagine = RilevaFormatoImmagine(rCell.ImageBytes)
                                                    If fmtC <> FormatoImmagine.Vettoriale Then
                                                        Dim mimeTypeC As String = If(fmtC = FormatoImmagine.Png, "image/png", "image/jpeg")
                                                        Dim datiImgC As Byte() = rCell.ImageBytes
                                                        If fmtC <> FormatoImmagine.Jpeg AndAlso fmtC <> FormatoImmagine.Png Then
                                                            Dim convC As Byte() = ConvertePngInJpeg(rCell.ImageBytes)
                                                            If convC IsNot Nothing AndAlso convC.Length > 0 Then datiImgC = convC Else datiImgC = Nothing
                                                            mimeTypeC = "image/jpeg"
                                                        End If
                                                        If datiImgC IsNot Nothing AndAlso datiImgC.Length > 0 Then
                                                            Dim altC As String = Net.WebUtility.HtmlEncode(If(String.IsNullOrWhiteSpace(rCell.Text), "immagine", rCell.Text))
                                                            Dim b64C As String = Convert.ToBase64String(datiImgC)
                                                            sb.Append("<img src=""data:" & mimeTypeC & ";base64," & b64C & """ alt=""" & altC & """ style=""max-width:100%;height:auto;"">")
                                                        End If
                                                    End If
                                                End If
                                            Else
                                                sb.Append(Net.WebUtility.HtmlEncode(rCell.Text))
                                            End If
                                        Next
                                        sb.Append("<br>")
                                    Next
                                    sb.Append("</td>")
                                Next
                                sb.AppendLine("</tr>")
                            Next
                            End If
                            sb.AppendLine("</table>")
                        End If
                    Next
                End If
            End SyncLock
            sb.AppendLine("</body></html>")
            File.WriteAllText(percorso, sb.ToString(), Encoding.UTF8)
            MostraMessaggioStato("HTML esportato in " & percorso)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImportaTesto(percorso As String)
        Try
            If SolaLettura Then Return
            If Not File.Exists(percorso) Then
                MostraMessaggioStato("File non trovato")
                Return
            End If
            Dim righe() As String = File.ReadAllLines(percorso, Encoding.UTF8)
            SaveUndoState()
            SyncLock _elementsLock
                For Each riga In righe
                    Dim p As New DocParagraph()
                    Dim r As New DocRun()
                    r.Text = riga
                    p.Runs.Add(r)
                    _elements.Insert(_caretIndex + 1, p)
                    _caretIndex += 1
                Next
                _caretCharIndex = 0
            End SyncLock
            _isModified = True
            TriggerLayout()
            ScrollToCaret()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Importate " & righe.Length & " righe da " & Path.GetFileName(percorso))
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub UnisciDocumento(percorso As String)
        Try
            If SolaLettura Then Return
            If Not File.Exists(percorso) Then
                MostraMessaggioStato("File non trovato")
                Return
            End If
            Dim elementiCaricati = WGWordUtils.CaricaDocumento(percorso, Function() False)
            If elementiCaricati Is Nothing OrElse elementiCaricati.Count = 0 Then
                MostraMessaggioStato("Documento vuoto o non leggibile")
                Return
            End If
            SaveUndoState()
            SyncLock _elementsLock
                For Each el In elementiCaricati
                    _elements.Add(el)
                Next
            End SyncLock
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Documento " & Path.GetFileName(percorso) & " unito al corrente")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub SalvaSubito()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _isModified AndAlso _elements IsNot Nothing AndAlso _elements.Count > 0 Then
                Dim localElements As List(Of DocElement)
                SyncLock _elementsLock
                    localElements = New List(Of DocElement)(_elements)
                End SyncLock
                Dim serializer As New System.Xml.Serialization.XmlSerializer(GetType(List(Of DocElement)))
                Dim sw As New IO.StringWriter()
                serializer.Serialize(sw, localElements)
                If _sqlite IsNot Nothing Then _sqlite.Salva(_docTitle, sw.ToString())
                MostraMessaggioStato("Documento salvato")
            Else
                MostraMessaggioStato("Nessuna modifica da salvare")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AttivaModalitaConcentrazione()
        Try
            If _modalitaConcentrazione Then
                MostraMessaggioStato("Modalità concentrazione già attiva")
                Return
            End If
            _modalitaConcentrazione = True
            _correttoreOrigConcentrazione = config.LeggiValore("abilita_correttore")
            _ecoOrigConcentrazione = config.LeggiValore("echo_digitazione")
            config.AggiungiValore("abilita_correttore", "0")
            config.AggiungiValore("echo_digitazione", "0")
            MostraMessaggioStato("Modalità concentrazione attiva")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub DisattivaModalitaConcentrazione()
        Try
            If Not _modalitaConcentrazione Then
                MostraMessaggioStato("Modalità concentrazione non attiva")
                Return
            End If
            _modalitaConcentrazione = False
            If String.IsNullOrEmpty(_correttoreOrigConcentrazione) Then _correttoreOrigConcentrazione = "1"
            If String.IsNullOrEmpty(_ecoOrigConcentrazione) Then _ecoOrigConcentrazione = "3"
            config.AggiungiValore("abilita_correttore", _correttoreOrigConcentrazione)
            config.AggiungiValore("echo_digitazione", _ecoOrigConcentrazione)
            MostraMessaggioStato("Modalità concentrazione disattivata")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InfoDocumento()
        Try
            Dim parole As Integer = 0
            Dim caratteri As Integer = 0
            Dim paragrafi As Integer = 0
            Dim tabelle As Integer = 0
            Dim immagini As Integer = 0
            Dim link As Integer = 0
            Dim segnalibri As Integer = 0
            Dim titoli As Integer = 0
            Dim noteAPie As Integer = 0
            Dim noteFine As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            paragrafi += 1
                            Dim testo2 As String = p.GetText()
                            caratteri += testo2.Length
                            parole += testo2.Split(New Char() {" "c, vbCr, vbLf, vbTab}, StringSplitOptions.RemoveEmptyEntries).Length
                            If p.Runs IsNot Nothing Then
                            For Each r In p.Runs
                                If r Is Nothing Then Continue For
                                If r.IsImage Then immagini += 1
                                If Not String.IsNullOrEmpty(r.LinkUrl) Then link += 1
                            Next
                            End If
                            If Not String.IsNullOrEmpty(p.BookmarkId) Then
                                If p.BookmarkId.StartsWith("BKMK_") Then segnalibri += 1
                                If p.BookmarkId.StartsWith("NOTA_PIE_") Then noteAPie += 1
                                If p.BookmarkId.StartsWith("NOTA_FINE_") Then noteFine += 1
                                If _stiliTitolo.ContainsKey(p.BookmarkId) Then titoli += 1
                            End If
                        ElseIf TypeOf el Is DocTable Then
                            tabelle += 1
                        End If
                    Next
                End If
            End SyncLock
            Dim sb As New StringBuilder()
            sb.AppendLine("Titolo: " & _docTitle)
            sb.AppendLine("Modificato: " & If(_isModified, "sì", "no"))
            sb.AppendLine(parole & " parole, " & caratteri & " caratteri")
            sb.AppendLine(paragrafi & " paragrafi, " & tabelle & " tabelle")
            sb.AppendLine(immagini & " immagini, " & link & " link")
            sb.AppendLine(titoli & " titoli, " & segnalibri & " segnalibri")
            sb.AppendLine(noteAPie & " note a piè di pagina, " & noteFine & " note di chiusura")
            Dim testo As String = sb.ToString()
            lettura.LeggiTesto(testo)
            MostraMessaggioStato(testo.Replace(vbCrLf, ". "))
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub


    Public Sub ImportaPdf(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                MostraMessaggioStato("File PDF non trovato")
                Return
            End If

            SyncLock _elementsLock
                If _elements Is Nothing Then
                    MostraMessaggioStato("Documento non inizializzato, impossibile importare il PDF")
                    Return
                End If
                If _caretIndex < 0 Then _caretIndex = 0
                If _elements.Count = 0 Then _caretIndex = -1
            End SyncLock

            Dim blocchiPerPagina As New List(Of List(Of UglyToad.PdfPig.Content.Word))
            Dim allLetters As New List(Of UglyToad.PdfPig.Content.Letter)

            Try
                Using docPdf As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorso)
                    For Each pagina In docPdf.GetPages()
                        Try
                            Dim letters As List(Of UglyToad.PdfPig.Content.Letter) = Nothing
                            Try
                                letters = pagina.Letters.ToList()
                            Catch exLetters As Exception
                                logger.ScriviLog("ImportaPdf Letters: " & exLetters.Message)
                            End Try

                            If letters Is Nothing OrElse letters.Count = 0 Then Continue For
                            allLetters.AddRange(letters)

                            Dim words As List(Of UglyToad.PdfPig.Content.Word) = Nothing
                            Try
                                Dim rawWords = UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor.NearestNeighbourWordExtractor.Instance.GetWords(letters)
                                If rawWords IsNot Nothing Then
                                    words = New List(Of UglyToad.PdfPig.Content.Word)
                                    For Each rw In rawWords
                                        If rw IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(rw.Text) Then
                                            words.Add(rw)
                                        End If
                                    Next
                                End If
                            Catch exWords As Exception
                                logger.ScriviLog("ImportaPdf GetWords: " & exWords.Message)
                            End Try

                            If words Is Nothing OrElse words.Count = 0 Then Continue For

                            Dim larghezzaPagina As Double = pagina.Width
                            If larghezzaPagina <= 0 Then larghezzaPagina = 595.0
                            Dim ordinati = OrdinaBlocchiPerColonne(words, larghezzaPagina)
                            If ordinati IsNot Nothing AndAlso ordinati.Count > 0 Then
                                blocchiPerPagina.Add(ordinati)
                            End If
                        Catch exPagina As Exception
                            logger.ScriviLog("ImportaPdf pagina: " & exPagina.Message)
                        End Try
                    Next
                End Using
            Catch exApri As Exception
                logger.ScriviLog("ImportaPdf apertura: " & exApri.Message)
                MostraMessaggioStato("Impossibile aprire il PDF: file protetto, danneggiato o non leggibile")
                Return
            End Try

            If allLetters.Count = 0 Then
                MostraMessaggioStato("PDF vuoto o senza testo estraibile")
                Return
            End If

            Dim bodySize As Double = CalcolaBodySize(allLetters)
            Dim xMinDoc As Double = Double.MaxValue
            Dim xMaxDoc As Double = Double.MinValue
            For Each l In allLetters
                If l.GlyphRectangle.Left < xMinDoc Then xMinDoc = l.GlyphRectangle.Left
                If l.GlyphRectangle.Right > xMaxDoc Then xMaxDoc = l.GlyphRectangle.Right
            Next
            Dim larghezzaUtile As Double = Math.Max(1.0, xMaxDoc - xMinDoc)

            Dim daInserire As New List(Of DocParagraph)
            Dim livelliTitolo As New List(Of Integer)

            For Each paginaParole In blocchiPerPagina
                Try
                    Dim righe = RaggruppaParoleInRighe(paginaParole, bodySize)
                    For Each riga In righe
                        Dim livelloT As Integer = 0
                        Dim para = ConvertiRigaParoleInParagrafo(riga, bodySize, xMinDoc, xMaxDoc, larghezzaUtile, livelloT)
                        If para Is Nothing Then Continue For
                        daInserire.Add(para)
                        livelliTitolo.Add(livelloT)
                        Dim paraVuoto As New DocParagraph()
                        paraVuoto.Runs.Add(New DocRun() With {.Text = ""})
                        daInserire.Add(paraVuoto)
                        livelliTitolo.Add(0)
                    Next
                Catch exPagina As Exception
                    logger.ScriviLog("ImportaPdf elaborazione pagina: " & exPagina.Message)
                End Try
            Next

            If daInserire.Count = 0 Then
                MostraMessaggioStato("PDF senza testo estraibile")
                Return
            End If

            SyncLock _elementsLock
                Dim posizioneInserimento As Integer = If(_elements.Count = 0, 0, Math.Min(_caretIndex + 1, _elements.Count))
                _elements.InsertRange(posizioneInserimento, daInserire)
                For i As Integer = 0 To daInserire.Count - 1
                    Dim liv As Integer = livelliTitolo(i)
                    If liv >= 1 AndAlso liv <= 6 Then
                        Dim p As DocParagraph = daInserire(i)
                        If String.IsNullOrEmpty(p.BookmarkId) Then
                            p.BookmarkId = "STL_" & Guid.NewGuid().ToString("N").Substring(0, 8)
                        End If
                        _stiliTitolo(p.BookmarkId) = liv
                    End If
                Next
                _caretIndex = 0
                _caretCharIndex = 0
            End SyncLock
            _isModified = True
            TriggerLayout()
            ScrollToCaret()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Importati " & (daInserire.Count \ 2) & " paragrafi dal PDF " & Path.GetFileName(percorso))
        Catch ex As Exception
            logger.ScriviLog("ImportaPdf: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function OrdinaBlocchiPerColonne(words As List(Of UglyToad.PdfPig.Content.Word), larghezzaPagina As Double) As List(Of UglyToad.PdfPig.Content.Word)
        Try
            If words Is Nothing OrElse words.Count = 0 Then Return New List(Of UglyToad.PdfPig.Content.Word)

            Dim metaPagina As Double = larghezzaPagina / 2.0

            Dim xCentri As New List(Of Double)
            For Each w In words
                xCentri.Add((w.BoundingBox.Left + w.BoundingBox.Right) / 2.0)
            Next
            xCentri.Sort()

            Dim colonnaConParole As Integer = 0
            For Each w In words
                If (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0 > metaPagina Then
                    colonnaConParole += 1
                End If
            Next
            Dim haSecondaColonna As Boolean = colonnaConParole >= 3 AndAlso colonnaConParole <= words.Count - 3

            Dim separatore As Double = metaPagina
            If haSecondaColonna Then
                Dim gapMassimo As Double = 0
                Dim gapX As Double = metaPagina
                For i As Integer = 0 To xCentri.Count - 2
                    Dim gap As Double = xCentri(i + 1) - xCentri(i)
                    If gap > gapMassimo AndAlso xCentri(i) > larghezzaPagina * 0.2 AndAlso xCentri(i) < larghezzaPagina * 0.8 Then
                        gapMassimo = gap
                        gapX = (xCentri(i) + xCentri(i + 1)) / 2.0
                    End If
                Next
                If gapMassimo > larghezzaPagina * 0.05 Then
                    separatore = gapX
                Else
                    haSecondaColonna = False
                End If
            End If

            Dim colonnaS As New List(Of UglyToad.PdfPig.Content.Word)
            Dim colonnaD As New List(Of UglyToad.PdfPig.Content.Word)
            For Each w In words
                If (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0 <= separatore Then
                    colonnaS.Add(w)
                Else
                    colonnaD.Add(w)
                End If
            Next

            OrdinaParoleBottomDesc(colonnaS)
            OrdinaParoleBottomDesc(colonnaD)

            If Not haSecondaColonna Then
                Return colonnaS
            End If

            Dim risultato As New List(Of UglyToad.PdfPig.Content.Word)
            risultato.AddRange(colonnaS)
            risultato.AddRange(colonnaD)
            Return risultato
        Catch ex As Exception
            logger.ScriviLog("OrdinaBlocchiPerColonne: " & ex.Message)
            Return words
        End Try
    End Function

    Private Sub OrdinaParoleBottomDesc(lista As List(Of UglyToad.PdfPig.Content.Word))
        Try
            lista.Sort(AddressOf ConfrontaParoleBottomDesc)
        Catch ex As Exception
            logger.ScriviLog("OrdinaParoleBottomDesc: " & ex.Message)
        End Try
    End Sub

    Private Function ConfrontaParoleBottomDesc(a As UglyToad.PdfPig.Content.Word, b As UglyToad.PdfPig.Content.Word) As Integer
        Try
            Dim cmp As Integer = b.BoundingBox.Bottom.CompareTo(a.BoundingBox.Bottom)
            If cmp <> 0 Then Return cmp
            Return a.BoundingBox.Left.CompareTo(b.BoundingBox.Left)
        Catch ex As Exception
            logger.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function RaggruppaParoleInRighe(words As List(Of UglyToad.PdfPig.Content.Word), bodySize As Double) As List(Of List(Of UglyToad.PdfPig.Content.Word))
        Try
            Dim risultato As New List(Of List(Of UglyToad.PdfPig.Content.Word))
            If words Is Nothing OrElse words.Count = 0 Then Return risultato

            Dim sogliaY As Double = Math.Max(2.0, bodySize * 0.55)
            Dim rigaCorrente As New List(Of UglyToad.PdfPig.Content.Word)
            Dim yRiga As Double = Double.MaxValue

            For Each w In words
                If String.IsNullOrWhiteSpace(w.Text) Then Continue For
                Dim yW As Double = w.BoundingBox.Bottom
                If rigaCorrente.Count = 0 Then
                    yRiga = yW
                    rigaCorrente.Add(w)
                ElseIf Math.Abs(yW - yRiga) <= sogliaY Then
                    rigaCorrente.Add(w)
                Else
                    rigaCorrente.Sort(AddressOf ConfrontaParoleSinistraAsc)
                    risultato.Add(New List(Of UglyToad.PdfPig.Content.Word)(rigaCorrente))
                    rigaCorrente.Clear()
                    yRiga = yW
                    rigaCorrente.Add(w)
                End If
            Next
            If rigaCorrente.Count > 0 Then
                rigaCorrente.Sort(AddressOf ConfrontaParoleSinistraAsc)
                risultato.Add(New List(Of UglyToad.PdfPig.Content.Word)(rigaCorrente))
            End If
            Return risultato
        Catch ex As Exception
            logger.ScriviLog("RaggruppaParoleInRighe: " & ex.Message)
            Return New List(Of List(Of UglyToad.PdfPig.Content.Word))
        End Try
    End Function

    Private Function ConfrontaParoleSinistraAsc(a As UglyToad.PdfPig.Content.Word, b As UglyToad.PdfPig.Content.Word) As Integer
        Try
            Return a.BoundingBox.Left.CompareTo(b.BoundingBox.Left)
        Catch ex As Exception
            logger.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function CalcolaBodySize(letters As IEnumerable(Of UglyToad.PdfPig.Content.Letter)) As Double
        Try
            Dim conteggi As New Dictionary(Of Double, Integer)
            For Each l In letters
                If l.PointSize > 0 Then
                    Dim size As Double = Math.Round(l.PointSize, 1)
                    If conteggi.ContainsKey(size) Then
                        conteggi(size) += 1
                    Else
                        conteggi(size) = 1
                    End If
                End If
            Next
            If conteggi.Count = 0 Then Return 11.0
            Dim modaSize As Double = 11.0
            Dim modaConteggio As Integer = 0
            For Each kv In conteggi
                If kv.Value > modaConteggio Then
                    modaConteggio = kv.Value
                    modaSize = kv.Key
                End If
            Next
            Return modaSize
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 11.0
        End Try
    End Function

    Private Function ConvertiRigaParoleInParagrafo(riga As List(Of UglyToad.PdfPig.Content.Word), bodySize As Double, xMinDoc As Double, xMaxDoc As Double, larghezzaUtile As Double, ByRef livelloTitoloOut As Integer) As DocParagraph
        Try
            livelloTitoloOut = 0
            If riga Is Nothing OrElse riga.Count = 0 Then Return Nothing

            Dim para As New DocParagraph()
            Dim sommaSize As Double = 0
            Dim conteggioLettere As Integer = 0
            Dim ciSonoBold As Boolean = False

            For Each w In riga
                If w.Letters IsNot Nothing Then
                    For Each lett In w.Letters
                        If lett.PointSize > 0 Then
                            sommaSize += lett.PointSize
                            conteggioLettere += 1
                        End If
                        If RilevaBold(lett.FontName) Then ciSonoBold = True
                    Next
                End If
            Next

            Dim sizeMedia As Double = If(conteggioLettere > 0, sommaSize / conteggioLettere, bodySize)
            livelloTitoloOut = RilevaLivelloTitolo(sizeMedia, bodySize, ciSonoBold)

            Dim xMinRiga As Double = Double.MaxValue
            Dim xMaxRiga As Double = Double.MinValue
            For Each w In riga
                If w.BoundingBox.Left < xMinRiga Then xMinRiga = w.BoundingBox.Left
                If w.BoundingBox.Right > xMaxRiga Then xMaxRiga = w.BoundingBox.Right
            Next
            Dim larghezzaRiga As Double = xMaxRiga - xMinRiga
            Dim gapLeft As Double = xMinRiga - xMinDoc
            Dim gapRight As Double = xMaxDoc - xMaxRiga
            Dim tolleranza As Double = larghezzaUtile * 0.04

            If Math.Abs(gapLeft - gapRight) < tolleranza AndAlso gapLeft > tolleranza Then
                para.Alignment = "Center"
            ElseIf gapRight < tolleranza AndAlso gapLeft < tolleranza AndAlso larghezzaRiga > larghezzaUtile * 0.85 Then
                para.Alignment = "Justify"
            ElseIf gapRight < tolleranza AndAlso gapLeft > tolleranza Then
                para.Alignment = "Right"
            Else
                para.Alignment = "Left"
            End If

            Dim sogliaIndent As Double = bodySize * 1.5
            If sogliaIndent <= 0 Then sogliaIndent = 16
            If gapLeft >= sogliaIndent * 0.7 Then
                Dim livelloIndent As Integer = CInt(Math.Floor(gapLeft / sogliaIndent))
                If livelloIndent > 5 Then livelloIndent = 5
                If livelloIndent > 0 Then para.IndentLevel = livelloIndent
            End If

            Dim runCorrente As DocRun = Nothing
            Dim ultimoFont As String = Nothing
            Dim ultimoBold As Boolean = False
            Dim ultimoItalic As Boolean = False
            Dim ultimoColore As String = ""
            Dim ultimaSize As Single = CSng(bodySize)
            Dim ultimaWordEndX As Double = -1
            Dim spazioMedio As Double = bodySize * 0.25

            For Each parola In riga
                If parola.Letters Is Nothing OrElse parola.Letters.Count = 0 Then Continue For
                Dim testoP As String = parola.Text
                If String.IsNullOrEmpty(testoP) Then Continue For

                Dim primaLettera = parola.Letters(0)
                Dim fontName As String = If(primaLettera.FontName, "")
                Dim cleanFont As String = PuliziaFontName(fontName)
                Dim isBold As Boolean = RilevaBold(fontName)
                Dim isItalic As Boolean = RilevaItalic(fontName)
                Dim fontSize As Single = CSng(primaLettera.PointSize)
                If fontSize <= 0 Then fontSize = CSng(bodySize)
                Dim coloreP As String = ColoreLetteraInNome(primaLettera)

                Dim wordStartX As Double = parola.BoundingBox.Left
                Dim wordEndX As Double = parola.BoundingBox.Right
                Dim mettiSpazio As Boolean = True
                If ultimaWordEndX >= 0 Then
                    Dim gap As Double = wordStartX - ultimaWordEndX
                    If gap < spazioMedio * 0.3 Then mettiSpazio = False
                End If

                Dim cambioFormato As Boolean = (runCorrente Is Nothing) OrElse (cleanFont <> ultimoFont) OrElse (isBold <> ultimoBold) OrElse (isItalic <> ultimoItalic) OrElse (coloreP <> ultimoColore) OrElse (Math.Abs(fontSize - ultimaSize) > 0.5F)

                If cambioFormato Then
                    runCorrente = New DocRun()
                    Dim prefix As String = ""
                    If mettiSpazio AndAlso para.Runs.Count > 0 Then prefix = " "
                    runCorrente.Text = prefix & testoP
                    runCorrente.Bold = isBold
                    runCorrente.Italic = isItalic
                    runCorrente.FontSize = fontSize
                    If Not String.IsNullOrEmpty(cleanFont) Then runCorrente.FontFamily = cleanFont
                    If Not String.IsNullOrEmpty(coloreP) Then runCorrente.ColorHex = coloreP
                    para.Runs.Add(runCorrente)
                    ultimoFont = cleanFont
                    ultimoBold = isBold
                    ultimoItalic = isItalic
                    ultimoColore = coloreP
                    ultimaSize = fontSize
                Else
                    If mettiSpazio Then
                        runCorrente.Text &= " " & testoP
                    Else
                        runCorrente.Text &= testoP
                    End If
                End If

                ultimaWordEndX = wordEndX
            Next

            If para.Runs.Count = 0 Then Return Nothing
            Return para
        Catch ex As Exception
            logger.ScriviLog("ConvertiRigaParoleInParagrafo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function RilevaLivelloTitolo(sizeMediaBlocco As Double, bodySize As Double, ciSonoBoldNelBlocco As Boolean) As Integer
        Try
            If bodySize <= 0 Then Return 0
            Dim ratio As Double = sizeMediaBlocco / bodySize
            If ratio >= 1.7 Then Return 1
            If ratio >= 1.45 Then Return 2
            If ratio >= 1.25 Then Return 3
            If ratio >= 1.12 Then Return 4
            If ratio >= 1.04 AndAlso ciSonoBoldNelBlocco Then Return 5
            Return 0
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Private Function RilevaListItem(testo As String, ByRef testoSenzaMarker As String) As Boolean
        Try
            testoSenzaMarker = testo
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim regexLista As New System.Text.RegularExpressions.Regex("^([\u2022\u2023\u25E6\u25CB\u25CF\u2043\u2010-\u2015•·‣⁃‒–—\-\*]|\d{1,2}[\.\)]|[a-zA-Z][\.\)])\s+")
            Dim m = regexLista.Match(testo)
            If m.Success Then
                testoSenzaMarker = testo.Substring(m.Length)
                Return True
            End If
            Return False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Private Function ColoreLetteraInNome(letter As UglyToad.PdfPig.Content.Letter) As String
        Try
            If letter Is Nothing OrElse letter.Color Is Nothing Then Return ""
            Dim r As Double = 0
            Dim g As Double = 0
            Dim b As Double = 0
            Try
                Dim rgb = letter.Color.ToRGBValues()
                r = rgb.Item1
                g = rgb.Item2
                b = rgb.Item3
            Catch exRgb As Exception
                logger.ScriviLog("ColoreLetteraInNome ToRGBValues: " & exRgb.Message)
                Return ""
            End Try
            Dim ri As Integer = Math.Min(255, Math.Max(0, CInt(r * 255)))
            Dim gi As Integer = Math.Min(255, Math.Max(0, CInt(g * 255)))
            Dim bi As Integer = Math.Min(255, Math.Max(0, CInt(b * 255)))

            If ri < 30 AndAlso gi < 30 AndAlso bi < 30 Then Return "Nero"
            If ri > 220 AndAlso gi > 220 AndAlso bi > 220 Then Return "Bianco"
            If ri > 200 AndAlso gi < 70 AndAlso bi < 70 Then Return "Rosso"
            If ri < 70 AndAlso gi > 130 AndAlso bi < 70 Then Return "Verde"
            If ri < 70 AndAlso gi < 70 AndAlso bi > 130 Then Return "Blu"
            If ri > 220 AndAlso gi > 220 AndAlso bi < 70 Then Return "Giallo"
            If ri > 220 AndAlso gi > 130 AndAlso bi < 80 Then Return "Arancione"
            If ri > 100 AndAlso gi < 70 AndAlso bi > 100 Then Return "Viola"
            If Math.Abs(ri - gi) < 25 AndAlso Math.Abs(gi - bi) < 25 AndAlso ri > 60 AndAlso ri < 220 Then Return "Grigio"
            Return ri.ToString("X2") & gi.ToString("X2") & bi.ToString("X2")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function
    Private Function PuliziaFontName(fontName As String) As String
        Try
            If String.IsNullOrEmpty(fontName) Then Return ""
            Dim risultato As String = fontName
            Dim posPlus As Integer = risultato.IndexOf("+"c)
            If posPlus >= 0 Then risultato = risultato.Substring(posPlus + 1)
            Dim suffissi() As String = {"-BoldItalic", "-BoldOblique", "-Bold", "-Italic", "-Oblique", ",BoldItalic", ",Bold", ",Italic", "BoldItalicMT", "BoldMT", "ItalicMT", "PSMT", "MT"}
            For Each suffix In suffissi
                Dim idx As Integer = risultato.IndexOf(suffix, StringComparison.OrdinalIgnoreCase)
                If idx > 0 Then
                    risultato = risultato.Substring(0, idx)
                    Exit For
                End If
            Next
            Return risultato.Trim()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return fontName
        End Try
    End Function

    Private Function RilevaBold(fontName As String) As Boolean
        Try
            If String.IsNullOrEmpty(fontName) Then Return False
            Dim lower As String = fontName.ToLower()
            Return lower.Contains("bold") OrElse lower.Contains("black") OrElse lower.Contains("heavy") OrElse lower.Contains("semibold")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Private Function RilevaItalic(fontName As String) As Boolean
        Try
            If String.IsNullOrEmpty(fontName) Then Return False
            Dim lower As String = fontName.ToLower()
            Return lower.Contains("italic") OrElse lower.Contains("oblique")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return False
        End Try
    End Function



    Private _bitmapMisura As New System.Drawing.Bitmap(1, 1)
    Private _graficaMisura As System.Drawing.Graphics = Nothing

    Private Function OttieniGraficaMisura() As System.Drawing.Graphics
        Try
            If _graficaMisura Is Nothing Then
                _graficaMisura = System.Drawing.Graphics.FromImage(_bitmapMisura)
                _graficaMisura.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias
            End If
            Return _graficaMisura
        Catch ex As Exception
            logger.ScriviLog($"OttieniGraficaMisura: errore. Errore: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Function MisuraTestoPdf(testo As String, nomeFontGdi As String, dimensione As Single, bold As Boolean, italic As Boolean) As Double
        Try
            If String.IsNullOrEmpty(testo) Then Return 0

            Dim stile As System.Drawing.FontStyle = System.Drawing.FontStyle.Regular
            If bold AndAlso italic Then
                stile = System.Drawing.FontStyle.Bold Or System.Drawing.FontStyle.Italic
            ElseIf bold Then
                stile = System.Drawing.FontStyle.Bold
            ElseIf italic Then
                stile = System.Drawing.FontStyle.Italic
            End If

            Dim g As System.Drawing.Graphics = OttieniGraficaMisura()
            If g Is Nothing Then Return testo.Length * dimensione * 0.5

            Using fontGdi As New System.Drawing.Font(nomeFontGdi, dimensione, stile)
                Return g.MeasureString(testo, fontGdi, Integer.MaxValue, System.Drawing.StringFormat.GenericTypographic).Width
            End Using
        Catch ex As Exception
            logger.ScriviLog("ex: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return testo.Length * dimensione * 0.5
        End Try
    End Function

    Private Function SanitizzaTestoPdf(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return String.Empty
            Dim sb As New System.Text.StringBuilder(testo.Length)
            For Each c As Char In testo
                Select Case c
                    Case "à"c : sb.Append("a")
                    Case "è"c, "é"c : sb.Append("e")
                    Case "ì"c : sb.Append("i")
                    Case "ò"c : sb.Append("o")
                    Case "ù"c : sb.Append("u")
                    Case "À"c : sb.Append("A")
                    Case "È"c, "É"c : sb.Append("E")
                    Case "Ì"c : sb.Append("I")
                    Case "Ò"c : sb.Append("O")
                    Case "Ù"c : sb.Append("U")
                    Case Else
                        If c >= " "c AndAlso AscW(c) < 128 Then sb.Append(c)
                End Select
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog($"SanitizzaTestoPdf: errore. Errore: {ex.Message}")
            Return testo
        End Try
    End Function

    Private Function SelezionaFontPerRun(run As DocRun,
                                          fontStandard As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontBold As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontBoldItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          cache As Dictionary(Of String, UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont)) As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont
        Try
            If cache IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(run.FontFamily) Then
                Dim chiave As String = run.FontFamily.ToLower().Trim() & "_" & run.Bold.ToString() & "_" & run.Italic.ToString()
                If cache.ContainsKey(chiave) Then Return cache(chiave)
            End If
            If run.Bold AndAlso run.Italic Then Return fontBoldItalic
            If run.Bold Then Return fontBold
            If run.Italic Then Return fontItalic
            Return fontStandard
        Catch ex As Exception
            logger.ScriviLog($"SelezionaFontPerRun: errore. Errore: {ex.Message}")
            Return fontStandard
        End Try
    End Function

    Private Class SegmentoPdf
        Public Property Testo As String
        Public Property Font As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont
        Public Property FontSize As Single
        Public Property Bold As Boolean
        Public Property Italic As Boolean
        Public Property NomeFontGdi As String
    End Class

    Private Class ElementoRigaPdf
        Public Property Testo As String
        Public Property Font As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont
        Public Property FontSize As Single
        Public Property Bold As Boolean
        Public Property Italic As Boolean
        Public Property NomeFontGdi As String
        Public Property Larghezza As Double
    End Class

    Private Function DisegnaParagrafoPdf(builder As UglyToad.PdfPig.Writer.PdfDocumentBuilder,
                                          ByRef page As UglyToad.PdfPig.Writer.PdfPageBuilder,
                                          p As DocParagraph,
                                          startY As Double,
                                          margineSinistro As Double,
                                          margineDestro As Double,
                                          margineInferiore As Double,
                                          margineSuperiore As Double,
                                          larghezzaPagina As Double,
                                          altezzaPagina As Double,
                                          fontStandard As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontBold As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          fontBoldItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                          cacheFontAggiunta As Dictionary(Of String, UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont)) As Double
        Try
            Dim indent As Double = Math.Max(0, p.IndentLevel) * 20.0
            Dim xSinistro As Double = margineSinistro + indent
            Dim xDestro As Double = larghezzaPagina - margineDestro
            Dim larghezzaDisponibile As Double = xDestro - xSinistro
            If larghezzaDisponibile <= 10 Then larghezzaDisponibile = larghezzaPagina - margineSinistro - margineDestro

            Dim segmenti As New List(Of SegmentoPdf)()

            If p.Runs IsNot Nothing Then
                For Each run As DocRun In p.Runs
                    Try
                        If run.IsImage Then Continue For
                        If String.IsNullOrEmpty(run.Text) Then Continue For
                        Dim fontPdf = SelezionaFontPerRun(run, fontStandard, fontBold, fontItalic, fontBoldItalic, cacheFontAggiunta)
                        Dim dimensione As Single = Math.Max(6.0F, Math.Min(72.0F, run.FontSize))
                        Dim nomeFontGdi As String = If(Not String.IsNullOrWhiteSpace(run.FontFamily), run.FontFamily.Trim(), "Arial")
                        segmenti.Add(New SegmentoPdf With {
                            .Testo = run.Text,
                            .Font = fontPdf,
                            .FontSize = dimensione,
                            .Bold = run.Bold,
                            .Italic = run.Italic,
                            .NomeFontGdi = nomeFontGdi
                        })
                    Catch exRun As Exception
                        logger.ScriviLog($"DisegnaParagrafoPdf: errore preparazione run. Errore: {exRun.Message}")
                    End Try
                Next
            End If

            If segmenti.Count = 0 Then
                Dim altezzaVuota As Double = 14.0
                Return startY - altezzaVuota
            End If

            Dim righe As New List(Of List(Of ElementoRigaPdf))()
            Dim rigaCorrente As New List(Of ElementoRigaPdf)()
            Dim xCorrente As Double = 0

            For Each seg As SegmentoPdf In segmenti
                Try
                    Dim parole As String() = seg.Testo.Split(" "c)
                    For idx As Integer = 0 To parole.Length - 1
                        Dim parola As String = parole(idx) & If(idx < parole.Length - 1, " ", "")
                        If String.IsNullOrEmpty(parola) Then Continue For

                        Dim larghezzaParola As Double = MisuraTestoPdf(parola, seg.NomeFontGdi, seg.FontSize, seg.Bold, seg.Italic)

                        If xCorrente + larghezzaParola > larghezzaDisponibile AndAlso rigaCorrente.Count > 0 Then
                            righe.Add(rigaCorrente)
                            rigaCorrente = New List(Of ElementoRigaPdf)()
                            xCorrente = 0
                            Dim parolaTrimmed As String = parola.TrimStart()
                            If String.IsNullOrEmpty(parolaTrimmed) Then Continue For
                            parola = parolaTrimmed
                            larghezzaParola = MisuraTestoPdf(parola, seg.NomeFontGdi, seg.FontSize, seg.Bold, seg.Italic)
                        End If

                        If larghezzaParola > larghezzaDisponibile Then
                            Dim sb As New System.Text.StringBuilder()
                            For Each c As Char In parola
                                Dim lc As Double = MisuraTestoPdf(sb.ToString() & c, seg.NomeFontGdi, seg.FontSize, seg.Bold, seg.Italic)
                                If lc > larghezzaDisponibile AndAlso sb.Length > 0 Then
                                    Dim parte As String = sb.ToString()
                                    Dim lp As Double = MisuraTestoPdf(parte, seg.NomeFontGdi, seg.FontSize, seg.Bold, seg.Italic)
                                    rigaCorrente.Add(New ElementoRigaPdf With {.Testo = parte, .Font = seg.Font, .FontSize = seg.FontSize, .Bold = seg.Bold, .Italic = seg.Italic, .NomeFontGdi = seg.NomeFontGdi, .Larghezza = lp})
                                    righe.Add(rigaCorrente)
                                    rigaCorrente = New List(Of ElementoRigaPdf)()
                                    xCorrente = 0
                                    sb.Clear()
                                End If
                                sb.Append(c)
                            Next
                            If sb.Length > 0 Then
                                Dim restoTesto As String = sb.ToString()
                                Dim restoL As Double = MisuraTestoPdf(restoTesto, seg.NomeFontGdi, seg.FontSize, seg.Bold, seg.Italic)
                                rigaCorrente.Add(New ElementoRigaPdf With {.Testo = restoTesto, .Font = seg.Font, .FontSize = seg.FontSize, .Bold = seg.Bold, .Italic = seg.Italic, .NomeFontGdi = seg.NomeFontGdi, .Larghezza = restoL})
                                xCorrente += restoL
                            End If
                        Else
                            rigaCorrente.Add(New ElementoRigaPdf With {.Testo = parola, .Font = seg.Font, .FontSize = seg.FontSize, .Bold = seg.Bold, .Italic = seg.Italic, .NomeFontGdi = seg.NomeFontGdi, .Larghezza = larghezzaParola})
                            xCorrente += larghezzaParola
                        End If
                    Next
                Catch exSeg As Exception
                    logger.ScriviLog($"DisegnaParagrafoPdf: errore elaborazione segmento. Errore: {exSeg.Message}")
                End Try
            Next

            If rigaCorrente.Count > 0 Then righe.Add(rigaCorrente)

            Dim correnteY As Double = startY
            Dim lineSpacing As Single = Math.Max(0.8F, p.LineSpacing)
            Dim totaleRighe As Integer = righe.Count

            For r As Integer = 0 To totaleRighe - 1
                Dim riga As List(Of ElementoRigaPdf) = righe(r)
                If riga.Count = 0 Then Continue For

                Dim fontSizeMassimo As Single = riga.Max(Function(e) e.FontSize)
                Dim altezzaRiga As Double = fontSizeMassimo * lineSpacing * 1.25

                correnteY -= altezzaRiga

                If correnteY < margineInferiore Then
                    Try
                        page = AggiuntaPagina(builder, larghezzaPagina, altezzaPagina)
                        correnteY = altezzaPagina - margineSuperiore - altezzaRiga
                    Catch exPage As Exception
                        logger.ScriviLog($"DisegnaParagrafoPdf: errore aggiunta pagina. Errore: {exPage.Message}")
                        Return correnteY
                    End Try
                End If

                Dim larghezzaTotaleRiga As Double = riga.Sum(Function(e) e.Larghezza)
                Dim isUltimaRiga As Boolean = (r = totaleRighe - 1)

                Dim xBase As Double = xSinistro
                Dim spaziatura As Double = 0

                Select Case If(p.Alignment, "").ToLower()
                    Case "center", "centre"
                        xBase = xSinistro + (larghezzaDisponibile - larghezzaTotaleRiga) / 2.0
                    Case "right"
                        xBase = xDestro - larghezzaTotaleRiga
                    Case "justify"
                        If Not isUltimaRiga AndAlso riga.Count > 1 Then
                            Dim spazioExtra As Double = larghezzaDisponibile - larghezzaTotaleRiga
                            spaziatura = spazioExtra / (riga.Count - 1)
                        End If
                    Case Else
                        xBase = xSinistro
                End Select

                Dim xEl As Double = xBase
                For Each el As ElementoRigaPdf In riga
                    Try
                        Dim puntoTesto As New UglyToad.PdfPig.Core.PdfPoint(xEl, correnteY)
                        page.AddText(el.Testo, el.FontSize, puntoTesto, el.Font)
                    Catch exCarattere As Exception
                        logger.ScriviLog("exCarattere: " & exCarattere.Message & Environment.NewLine & exCarattere.StackTrace)
                        Try
                            Dim testoSafe As String = SanitizzaTestoPdf(el.Testo)
                            If Not String.IsNullOrEmpty(testoSafe) Then
                                page.AddText(testoSafe, el.FontSize, New UglyToad.PdfPig.Core.PdfPoint(xEl, correnteY), el.Font)
                            End If
                        Catch exSafe As Exception
                            logger.ScriviLog($"DisegnaParagrafoPdf: testo non disegnabile anche dopo sanitizzazione. Errore: {exSafe.Message}")
                        End Try
                    End Try
                    xEl += el.Larghezza + spaziatura
                Next
            Next

            Return correnteY

        Catch ex As Exception
            logger.ScriviLog($"DisegnaParagrafoPdf: errore (indent={p?.IndentLevel}, runs={p?.Runs?.Count}, startY={startY:F2}). Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return startY - 14
        End Try
    End Function


    Private Function DisegnaTabellaPdf(builder As UglyToad.PdfPig.Writer.PdfDocumentBuilder,
                                    ByRef page As UglyToad.PdfPig.Writer.PdfPageBuilder,
                                    tbl As DocTable,
                                    startY As Double,
                                    margineSinistro As Double,
                                    margineDestro As Double,
                                    margineInferiore As Double,
                                    margineSuperiore As Double,
                                    altezzaPagina As Double,
                                    larghezzaPagina As Double,
                                    fontStandard As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont,
                                    fontBold As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont) As Double
        Try
            Dim correnteY As Double = startY
            Dim larghezzaTabella As Double = larghezzaPagina - margineSinistro - margineDestro
            Dim altezzaCellaBase As Double = 15
            Dim paddingCella As Double = 3

            If tbl.Rows Is Nothing OrElse tbl.Rows.Count = 0 Then Return correnteY

            Dim numeroColonne As Integer = 0
            For Each row In tbl.Rows
                If row.Cells IsNot Nothing AndAlso row.Cells.Count > numeroColonne Then
                    numeroColonne = row.Cells.Count
                End If
            Next

            If numeroColonne = 0 Then Return correnteY
            Dim larghezzaColonna As Double = larghezzaTabella / numeroColonne

            Dim indiceRiga As Integer = 0

            For Each row In tbl.Rows
                Dim altezzaMaxRiga As Double = altezzaCellaBase

                If row.Cells IsNot Nothing Then
                    For Each cell In row.Cells
                        If cell.Paragraphs IsNot Nothing Then
                            Dim righeStimate As Integer = 0
                            For Each pCell In cell.Paragraphs
                                If pCell Is Nothing Then Continue For
                                Dim haImmagini As Boolean = pCell.Runs IsNot Nothing AndAlso pCell.Runs.Any(Function(rr) rr IsNot Nothing AndAlso rr.IsImage AndAlso rr.ImageBytes IsNot Nothing AndAlso rr.ImageBytes.Length > 0)
                                If haImmagini Then
                                    For Each rImg As DocRun In pCell.Runs
                                        If rImg IsNot Nothing AndAlso rImg.IsImage AndAlso rImg.ImageBytes IsNot Nothing AndAlso rImg.ImageBytes.Length > 0 Then
                                            Dim altImg As Double = CalcolaAltezzaImmagine(rImg, larghezzaColonna - paddingCella * 2, altezzaPagina - margineSuperiore - margineInferiore)
                                            If altImg > altezzaMaxRiga Then altezzaMaxRiga = altImg + paddingCella * 2
                                        End If
                                    Next
                                End If
                                Dim testoCella As String = pCell.GetText()
                                If Not String.IsNullOrEmpty(testoCella) Then
                                    Dim charsPerRiga As Integer = CInt(Math.Max(1, Math.Floor((larghezzaColonna - paddingCella * 2) / (11 * 0.52))))
                                    righeStimate += CInt(Math.Ceiling(testoCella.Length / charsPerRiga))
                                Else
                                    righeStimate += 1
                                End If
                            Next
                            Dim altezzaCellaStimata As Double = righeStimate * (altezzaCellaBase + 2)
                            If altezzaCellaStimata > altezzaMaxRiga Then altezzaMaxRiga = altezzaCellaStimata
                        End If
                    Next
                End If

                If correnteY - altezzaMaxRiga < margineInferiore Then
                    page = builder.AddPage(larghezzaPagina, altezzaPagina)
                    correnteY = altezzaPagina - margineSuperiore
                End If

                Dim correnteX As Double = margineSinistro

                If row.Cells IsNot Nothing Then
                    For Each cell In row.Cells
                        page.DrawRectangle(
                        New UglyToad.PdfPig.Core.PdfPoint(correnteX, correnteY - altezzaMaxRiga),
                        larghezzaColonna,
                        altezzaMaxRiga,
                        0.5)

                        Dim cellaY As Double = correnteY - paddingCella

                        If cell.Paragraphs IsNot Nothing Then
                            For Each pCell In cell.Paragraphs
                                If pCell Is Nothing Then Continue For
                                Dim haImmaginiCella As Boolean = pCell.Runs IsNot Nothing AndAlso pCell.Runs.Any(Function(rr) rr IsNot Nothing AndAlso rr.IsImage AndAlso rr.ImageBytes IsNot Nothing AndAlso rr.ImageBytes.Length > 0)
                                If haImmaginiCella Then
                                    For Each rImg As DocRun In pCell.Runs
                                        If rImg IsNot Nothing AndAlso rImg.IsImage AndAlso rImg.ImageBytes IsNot Nothing AndAlso rImg.ImageBytes.Length > 0 Then
                                            Dim areaLarghCella As Double = larghezzaColonna - paddingCella * 2
                                            Dim areaAltCella As Double = altezzaMaxRiga - paddingCella * 2
                                            Dim larghImgO As Double = rImg.ImageWidthPt
                                            Dim altImgO As Double = rImg.ImageHeightPt
                                            If larghImgO <= 0 OrElse altImgO <= 0 Then
                                                Try
                                                    Using msImg As New IO.MemoryStream(rImg.ImageBytes)
                                                        Using bmpImg As New System.Drawing.Bitmap(msImg)
                                                            larghImgO = bmpImg.Width
                                                            altImgO = bmpImg.Height
                                                        End Using
                                                    End Using
                                                Catch exImg As Exception
                                                    logger.ScriviLog($"DisegnaTabellaPdf: errore lettura dimensioni immagine cella. Errore: {exImg.Message}")
                                                End Try
                                            End If
                                            If larghImgO > 0 AndAlso altImgO > 0 Then
                                                Dim scalaImg As Double = 1.0
                                                If larghImgO > areaLarghCella Then scalaImg = areaLarghCella / larghImgO
                                                Dim larghImgS As Double = larghImgO * scalaImg
                                                Dim altImgS As Double = altImgO * scalaImg
                                                If altImgS > areaAltCella Then
                                                    Dim scalaH2 As Double = areaAltCella / altImgS
                                                    larghImgS *= scalaH2
                                                    altImgS *= scalaH2
                                                End If
                                                Dim xImg As Double = correnteX + paddingCella
                                                Dim yImgBottom As Double = cellaY - altImgS
                                                If yImgBottom < margineInferiore Then yImgBottom = margineInferiore
                                                Dim rettImg As New UglyToad.PdfPig.Core.PdfRectangle(xImg, yImgBottom, xImg + larghImgS, cellaY)
                                                Dim datiJpegCella As Byte() = Nothing
                                                Dim fmtCella As FormatoImmagine = RilevaFormatoImmagine(rImg.ImageBytes)
                                                If fmtCella = FormatoImmagine.Vettoriale Then
                                                ElseIf fmtCella = FormatoImmagine.Jpeg Then
                                                    datiJpegCella = rImg.ImageBytes
                                                Else
                                                    datiJpegCella = ConvertePngInJpeg(rImg.ImageBytes)
                                                End If
                                                If datiJpegCella IsNot Nothing AndAlso datiJpegCella.Length > 0 Then
                                                    page.AddJpeg(datiJpegCella, rettImg)
                                                    cellaY -= altImgS + paddingCella
                                                End If
                                            End If
                                        End If
                                    Next
                                End If
                                Dim testoCella As String = pCell.GetText()
                                If Not String.IsNullOrEmpty(testoCella) Then
                                    Dim fontCella As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont = If(indiceRiga = 0, fontBold, fontStandard)
                                    Dim charsPerRiga As Integer = CInt(Math.Max(1, Math.Floor((larghezzaColonna - paddingCella * 2) / (11 * 0.52))))
                                    Dim pos As Integer = 0
                                    Do While pos < testoCella.Length
                                        Dim rigaTesto As String = testoCella.Substring(pos, Math.Min(charsPerRiga, testoCella.Length - pos))
                                        page.AddText(rigaTesto, 11, New UglyToad.PdfPig.Core.PdfPoint(correnteX + paddingCella, cellaY), fontCella)
                                        cellaY -= (altezzaCellaBase + 2)
                                        pos += charsPerRiga
                                    Loop
                                Else
                                    cellaY -= altezzaCellaBase
                                End If
                            Next
                        End If

                        correnteX += larghezzaColonna
                    Next
                End If

                correnteY -= altezzaMaxRiga
                indiceRiga += 1
            Next

            correnteY -= 5
            Return correnteY

        Catch ex As Exception
            logger.ScriviLog($"DisegnaTabellaPdf: errore nella tabella (righe={tbl?.Rows?.Count}). Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return startY
        End Try
    End Function
    Public Sub EsportaTxt(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return

            Dim sb As New StringBuilder()

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            For Each r In tbl.Rows
                                Dim rowText As New List(Of String)
                                For Each c In r.Cells
                                    Dim cellText As New StringBuilder()
                                    For Each p In c.Paragraphs
                                        cellText.Append(p.GetText() & " ")
                                    Next
                                    rowText.Add(cellText.ToString().Trim())
                                Next
                                sb.AppendLine(String.Join(vbTab, rowText))
                            Next
                            sb.AppendLine()
                        End If
                    Next
                End If
            End SyncLock

            IO.File.WriteAllText(percorso, sb.ToString())
            MostraMessaggioStato("Documento esportato in formato testuale")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub
    Public Sub EsportaRtf(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return

            Dim sb As New StringBuilder()
            sb.AppendLine("{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1040{\fonttbl{\f0\fnil\fcharset0 Arial;}}")
            sb.AppendLine("{\colortbl ;\red0\green0\blue0;\red255\green255\blue255;\red255\green0\blue0;\red0\green255\blue0;\red0\green0\blue255;\red255\green255\blue0;}")

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            sb.Append("\pard")
                            If p.Alignment = "Center" Then sb.Append("\qc")
                            If p.Alignment = "Right" Then sb.Append("\qr")
                            If p.Alignment = "Justify" Then sb.Append("\qj")
                            If p.IndentLevel > 0 Then sb.Append("\li" & (p.IndentLevel * 720).ToString())
                            sb.Append(" ")

                            If p.Runs IsNot Nothing Then
                            For Each r As DocRun In p.Runs
                                If r Is Nothing Then Continue For
                                If r.IsImage Then Continue For
                                If r.Bold Then sb.Append("\b ")
                                If r.Italic Then sb.Append("\i ")
                                If r.Underline Then sb.Append("\ul ")
                                If r.Strikethrough Then sb.Append("\strike ")

                                Dim sizeFs As Integer = If(r.FontSize > 0, CInt(r.FontSize * 2), 22)
                                sb.Append("\fs" & sizeFs.ToString() & " ")

                                Dim textSafe As String = If(r.Text, "").Replace("\", "\\").Replace("{", "\{").Replace("}", "\}")
                                sb.Append(textSafe & " ")

                                If r.Strikethrough Then sb.Append("\strike0 ")
                                If r.Underline Then sb.Append("\ulnone ")
                                If r.Italic Then sb.Append("\i0 ")
                                If r.Bold Then sb.Append("\b0 ")
                            Next
                            End If
                            sb.AppendLine("\par")
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            If tbl.Rows Is Nothing Then Continue For
                            For Each row In tbl.Rows
                                If row Is Nothing OrElse row.Cells Is Nothing Then Continue For
                                sb.Append("\pard ")
                                For Each cell In row.Cells
                                    If cell Is Nothing OrElse cell.Paragraphs Is Nothing Then Continue For
                                    For Each p In cell.Paragraphs
                                        If p Is Nothing Then Continue For
                                        Dim cellTextSafe As String = p.GetText().Replace("\", "\\").Replace("{", "\{").Replace("}", "\}")
                                        sb.Append(cellTextSafe & " ")
                                    Next
                                    sb.Append("\tab ")
                                Next
                                sb.AppendLine("\par")
                            Next
                        End If
                    Next
                End If
            End SyncLock

            sb.AppendLine("}")
            IO.File.WriteAllText(percorso, sb.ToString())
            MostraMessaggioStato("Documento esportato in formato RTF")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub
    Public Sub EsportaEpub(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then
                Return
            End If

            Dim directoryDestinazione As String = Path.GetDirectoryName(percorso)
            If Not String.IsNullOrWhiteSpace(directoryDestinazione) AndAlso Not Directory.Exists(directoryDestinazione) Then
                Try
                    Directory.CreateDirectory(directoryDestinazione)
                Catch exDir As Exception
                    logger.ScriviLog($"EsportaEpub: impossibile creare la directory '{directoryDestinazione}'. Errore: {exDir.Message}{Environment.NewLine}{exDir.StackTrace}")
                    Return
                End Try
            End If

            _processoInCorso = New WGProcessoInCorso()
            _processoInCorso.Avvia("Esportazione EPUB in corso, attendere")

            Dim elementiLocali As New List(Of DocElement)
            SyncLock _elementsLock
                If _elements IsNot Nothing Then elementiLocali.AddRange(_elements)
            End SyncLock

            If elementiLocali.Count = 0 Then
                MostraMessaggioStato("Nessun contenuto da esportare.")
                _processoInCorso.Fine()
                Return
            End If

            Task.Run(Sub() EsportaEpubAsync(percorso, elementiLocali))

        Catch ex As Exception
            logger.ScriviLog($"EsportaEpub: errore generale. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            MostraMessaggioStato("Errore durante l'esportazione EPUB.")
            If _processoInCorso IsNot Nothing Then
                Try
                    _processoInCorso.Fine()
                Catch exFerma As Exception
                    logger.ScriviLog($"EsportaEpub: errore nel fermare il processo. Errore: {exFerma.Message}")
                End Try
            End If
        End Try
    End Sub

    Private Sub EsportaEpubAsync(percorso As String, elementiLocali As List(Of DocElement))
        Dim tempFile As String = ""
        Try
            tempFile = Path.GetTempFileName()
            File.Delete(tempFile)

            Dim titoloDocumento As String = EstraiTitoloDocumento(elementiLocali)
            Dim navPoints As New List(Of Tuple(Of String, String))

            Using archive As ZipArchive = ZipFile.Open(tempFile, ZipArchiveMode.Create)

                Using writer As New StreamWriter(archive.CreateEntry("mimetype").Open(), New System.Text.UTF8Encoding(False))
                    writer.Write("application/epub+zip")
                End Using

                Using writer As New StreamWriter(archive.CreateEntry("META-INF/container.xml").Open())
                    writer.Write("<?xml version=""1.0""?><container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container""><rootfiles><rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/></rootfiles></container>")
                End Using

                Dim sbHtml As New StringBuilder()
                sbHtml.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
                sbHtml.Append("<!DOCTYPE html PUBLIC ""-//W3C//DTD XHTML 1.1//EN"" ""http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd"">")
                sbHtml.Append("<html xmlns=""http://www.w3.org/1999/xhtml""><head>")
                sbHtml.AppendFormat("<title>{0}</title>", System.Security.SecurityElement.Escape(titoloDocumento))
                sbHtml.Append("<style>")
                sbHtml.Append("body { font-family: serif; font-size: 12pt; margin: 1em; }")
                sbHtml.Append("p { margin: 0; padding: 0; }")
                sbHtml.Append("ul { list-style-type: disc; margin-left: 1.5em; }")
                sbHtml.Append("ol { list-style-type: decimal; margin-left: 1.5em; }")
                sbHtml.Append("table { border-collapse: collapse; width: 100%; margin-bottom: 15px; }")
                sbHtml.Append("td, th { border: 1px solid black; padding: 5px; vertical-align: top; }")
                sbHtml.Append("th { font-weight: bold; background-color: #f0f0f0; }")
                sbHtml.Append("h1, h2, h3, h4, h5, h6 { margin-top: 1em; margin-bottom: 0.5em; }")
                sbHtml.Append("</style></head><body>")

                Dim inLista As Boolean = False
                Dim tipoListaCorrente As String = ""
                Dim navCounter As Integer = 1

                For Each el In elementiLocali
                    If TypeOf el Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(el, DocParagraph)

                        If p.IsPageBreak Then
                            If inLista Then
                                sbHtml.AppendFormat("</{0}>", tipoListaCorrente)
                                inLista = False
                                tipoListaCorrente = ""
                            End If
                            sbHtml.Append("<hr style=""page-break-after: always; visibility: hidden;""/>")
                            Continue For
                        End If

                        Dim tipoLista As String = If(Not String.IsNullOrEmpty(p.ListNumValue), "ol", "ul")

                        If p.IsListItem Then
                            If Not inLista OrElse tipoListaCorrente <> tipoLista Then
                                If inLista Then sbHtml.AppendFormat("</{0}>", tipoListaCorrente)
                                sbHtml.AppendFormat("<{0}>", tipoLista)
                                inLista = True
                                tipoListaCorrente = tipoLista
                            End If
                            sbHtml.Append("<li>")
                            sbHtml.Append(RenderRunsHtml(p.Runs))
                            sbHtml.Append("</li>")
                        Else
                            If inLista Then
                                sbHtml.AppendFormat("</{0}>", tipoListaCorrente)
                                inLista = False
                                tipoListaCorrente = ""
                            End If

                            Dim tagP As String = "p"
                            Dim isHeading As Boolean = False

                            Dim styleP As New StringBuilder()
                            If p.SpacingBefore > 0 Then styleP.AppendFormat("margin-top: {0}pt; ", Math.Round(p.SpacingBefore, 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                            If p.SpacingAfter > 0 Then styleP.AppendFormat("margin-bottom: {0}pt; ", Math.Round(p.SpacingAfter, 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                            If p.LineSpacing > 0 AndAlso p.LineSpacing <> 1.0F Then styleP.AppendFormat("line-height: {0}; ", Math.Round(p.LineSpacing, 2).ToString(System.Globalization.CultureInfo.InvariantCulture))
                            If p.IndentLevel > 0 Then styleP.AppendFormat("margin-left: {0}px; ", p.IndentLevel * 20)

                            Select Case If(p.Alignment, "").ToLower()
                                Case "center" : styleP.Append("text-align: center; ")
                                Case "right" : styleP.Append("text-align: right; ")
                                Case "justify" : styleP.Append("text-align: justify; ")
                            End Select

                            Dim primoRun As DocRun = p.Runs?.FirstOrDefault(Function(r) r IsNot Nothing AndAlso Not r.IsImage)
                            If primoRun IsNot Nothing AndAlso primoRun.Bold AndAlso primoRun.FontSize >= 14 Then
                                Dim livello As Integer = If(primoRun.FontSize >= 22, 1, If(primoRun.FontSize >= 18, 2, If(primoRun.FontSize >= 16, 3, If(primoRun.FontSize >= 15, 4, If(primoRun.FontSize >= 14, 5, 6)))))
                                tagP = $"h{livello}"
                                isHeading = True
                            End If

                            Dim anchorId As String = ""
                            If isHeading Then
                                anchorId = $"nav{navCounter}"
                                Dim testoHeading As String = p.GetText()
                                If Not String.IsNullOrWhiteSpace(testoHeading) Then
                                    navPoints.Add(New Tuple(Of String, String)(anchorId, testoHeading))
                                End If
                                navCounter += 1
                            End If

                            Dim attrStyle As String = If(styleP.Length > 0, $" style=""{styleP.ToString().Trim()}""", "")
                            Dim attrId As String = If(anchorId <> "", $" id=""{anchorId}""", "")

                            sbHtml.AppendFormat("<{0}{1}{2}>", tagP, attrId, attrStyle)
                            sbHtml.Append(RenderRunsHtml(p.Runs))
                            sbHtml.AppendFormat("</{0}>", tagP)
                        End If

                    ElseIf TypeOf el Is DocTable Then
                        If inLista Then
                            sbHtml.AppendFormat("</{0}>", tipoListaCorrente)
                            inLista = False
                            tipoListaCorrente = ""
                        End If

                        Dim tbl As DocTable = DirectCast(el, DocTable)
                        sbHtml.Append("<table>")
                        If tbl.Rows IsNot Nothing Then
                        For Each row In tbl.Rows
                            If row Is Nothing OrElse row.Cells Is Nothing Then Continue For
                            sbHtml.Append("<tr>")
                            For Each cell In row.Cells
                                If cell Is Nothing Then Continue For
                                Dim tagCella As String = If(cell.IsHeader OrElse row.IsHeader, "th", "td")
                                Dim attrCella As New StringBuilder()
                                If cell.ColSpan > 1 Then attrCella.AppendFormat(" colspan=""{0}""", cell.ColSpan)
                                If cell.RowSpan > 1 Then attrCella.AppendFormat(" rowspan=""{0}""", cell.RowSpan)

                                Dim shadingHex As String = ConvertNomeColoreAHex(cell.ShadingColorHex)
                                Dim styleCella As String = ""
                                If shadingHex <> "000000" AndAlso shadingHex <> "auto" Then
                                    styleCella = $" style=""background-color: #{shadingHex};"""
                                End If

                                sbHtml.AppendFormat("<{0}{1}{2}>", tagCella, attrCella.ToString(), styleCella)
                                If cell.Paragraphs IsNot Nothing Then
                                For Each pCell In cell.Paragraphs
                                    If pCell Is Nothing Then Continue For
                                    Dim styleP As New StringBuilder()
                                    Select Case If(pCell.Alignment, "").ToLower()
                                        Case "center" : styleP.Append("text-align: center; ")
                                        Case "right" : styleP.Append("text-align: right; ")
                                        Case "justify" : styleP.Append("text-align: justify; ")
                                    End Select
                                    Dim attrStyle As String = If(styleP.Length > 0, $" style=""{styleP.ToString().Trim()}""", "")
                                    sbHtml.AppendFormat("<p{0}>", attrStyle)
                                    sbHtml.Append(RenderRunsHtml(pCell.Runs))
                                    sbHtml.Append("</p>")
                                Next
                                End If
                                sbHtml.AppendFormat("</{0}>", tagCella)
                            Next
                            sbHtml.Append("</tr>")
                        Next
                        End If
                        sbHtml.Append("</table>")
                    End If
                Next

                If inLista Then
                    sbHtml.AppendFormat("</{0}>", tipoListaCorrente)
                End If

                sbHtml.Append("</body></html>")

                Using writer As New StreamWriter(archive.CreateEntry("OEBPS/content.html").Open())
                    writer.Write(sbHtml.ToString())
                End Using

                Using writer As New StreamWriter(archive.CreateEntry("OEBPS/content.opf").Open())
                    Dim sbOpf As New StringBuilder()
                    sbOpf.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    sbOpf.Append("<package xmlns=""http://www.idpf.org/2007/opf"" unique-identifier=""bookid"" version=""2.0"">")
                    sbOpf.Append("<metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:opf=""http://www.idpf.org/2007/opf"">")
                    sbOpf.AppendFormat("<dc:title>{0}</dc:title>", System.Security.SecurityElement.Escape(titoloDocumento))
                    sbOpf.Append("<dc:language>it</dc:language>")
                    sbOpf.AppendFormat("<dc:identifier id=""bookid"">urn:uuid:{0}</dc:identifier>", Guid.NewGuid().ToString())
                    sbOpf.AppendFormat("<dc:date>{0}</dc:date>", DateTime.UtcNow.ToString("yyyy-MM-dd"))
                    sbOpf.Append("</metadata>")
                    sbOpf.Append("<manifest>")
                    sbOpf.Append("<item id=""ncx"" href=""toc.ncx"" media-type=""application/x-dtbncx+xml""/>")
                    sbOpf.Append("<item id=""content"" href=""content.html"" media-type=""application/xhtml+xml""/>")
                    sbOpf.Append("</manifest>")
                    sbOpf.Append("<spine toc=""ncx""><itemref idref=""content""/></spine>")
                    sbOpf.Append("</package>")
                    writer.Write(sbOpf.ToString())
                End Using

                Using writer As New StreamWriter(archive.CreateEntry("OEBPS/toc.ncx").Open())
                    Dim sbNcx As New StringBuilder()
                    sbNcx.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
                    sbNcx.Append("<ncx xmlns=""http://www.daisy.org/z3986/2005/ncx/"" version=""2005-1"">")
                    sbNcx.Append("<head>")
                    sbNcx.AppendFormat("<meta name=""dtb:uid"" content=""bookid""/>")
                    sbNcx.AppendFormat("<meta name=""dtb:depth"" content=""{0}""/>", If(navPoints.Count > 0, 1, 0))
                    sbNcx.Append("</head>")
                    sbNcx.AppendFormat("<docTitle><text>{0}</text></docTitle>", System.Security.SecurityElement.Escape(titoloDocumento))
                    sbNcx.Append("<navMap>")
                    If navPoints.Count > 0 Then
                        For idx As Integer = 0 To navPoints.Count - 1
                            Dim np = navPoints(idx)
                            sbNcx.AppendFormat("<navPoint id=""{0}"" playOrder=""{1}"">", np.Item1, idx + 1)
                            sbNcx.AppendFormat("<navLabel><text>{0}</text></navLabel>", System.Security.SecurityElement.Escape(np.Item2))
                            sbNcx.AppendFormat("<content src=""content.html#{0}""/>", np.Item1)
                            sbNcx.Append("</navPoint>")
                        Next
                    Else
                        sbNcx.Append("<navPoint id=""navpoint-1"" playOrder=""1"">")
                        sbNcx.AppendFormat("<navLabel><text>{0}</text></navLabel>", System.Security.SecurityElement.Escape(titoloDocumento))
                        sbNcx.Append("<content src=""content.html""/>")
                        sbNcx.Append("</navPoint>")
                    End If
                    sbNcx.Append("</navMap></ncx>")
                    writer.Write(sbNcx.ToString())
                End Using
            End Using

            If File.Exists(percorso) Then File.Delete(percorso)
            File.Move(tempFile, percorso)

            MostraMessaggioStato("Esportazione EPUB completata")

        Catch ex As Exception
            logger.ScriviLog($"EsportaEpubAsync: errore generale. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            MostraMessaggioStato("Errore durante l'esportazione EPUB.")
            Try
                If Not String.IsNullOrEmpty(tempFile) AndAlso File.Exists(tempFile) Then File.Delete(tempFile)
            Catch exClean As Exception
                logger.ScriviLog($"EsportaEpubAsync: impossibile eliminare il file temporaneo. Errore: {exClean.Message}")
            End Try
        Finally
            If _processoInCorso IsNot Nothing Then
                Try
                    _processoInCorso.Fine()
                Catch exFerma As Exception
                    logger.ScriviLog($"EsportaEpubAsync: errore nel fermare il processo. Errore: {exFerma.Message}")
                End Try
            End If
        End Try
    End Sub

    Private Function RenderRunsHtml(runs As List(Of DocRun)) As String
        Try
            If runs Is Nothing OrElse runs.Count = 0 Then Return ""
            Dim sb As New StringBuilder()
            For Each r In runs
                If r Is Nothing Then Continue For
                If r.IsImage Then
                    If r.ImageBytes IsNot Nothing AndAlso r.ImageBytes.Length > 0 Then
                        Dim fmtR As FormatoImmagine = RilevaFormatoImmagine(r.ImageBytes)
                        If fmtR <> FormatoImmagine.Vettoriale Then
                            Dim mimeTypeR As String = If(fmtR = FormatoImmagine.Png, "image/png", "image/jpeg")
                            Dim datiR As Byte() = r.ImageBytes
                            If fmtR <> FormatoImmagine.Jpeg AndAlso fmtR <> FormatoImmagine.Png Then
                                Dim convR As Byte() = ConvertePngInJpeg(r.ImageBytes)
                                If convR IsNot Nothing AndAlso convR.Length > 0 Then datiR = convR Else datiR = Nothing
                                mimeTypeR = "image/jpeg"
                            End If
                            If datiR IsNot Nothing AndAlso datiR.Length > 0 Then
                                Dim altR As String = System.Security.SecurityElement.Escape(If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text))
                                Dim b64R As String = Convert.ToBase64String(datiR)
                                sb.Append("<img src=""data:" & mimeTypeR & ";base64," & b64R & """ alt=""" & altR & """ style=""max-width:100%;height:auto;""/>")
                            End If
                        End If
                    End If
                    Continue For
                End If
                If r.Text Is Nothing Then Continue For

                Dim textSafe As String = System.Security.SecurityElement.Escape(r.Text)
                If String.IsNullOrEmpty(textSafe) Then Continue For

                Dim styleR As New StringBuilder()
                If r.Bold Then styleR.Append("font-weight: bold; ")
                If r.Italic Then styleR.Append("font-style: italic; ")
                If r.IsSuperscript Then styleR.Append("vertical-align: super; font-size: smaller; ")
                If r.IsSubscript Then styleR.Append("vertical-align: sub; font-size: smaller; ")

                Dim dec As New StringBuilder()
                If r.Underline Then dec.Append("underline ")
                If r.Strikethrough Then dec.Append("line-through ")
                If dec.Length > 0 Then styleR.AppendFormat("text-decoration: {0}; ", dec.ToString().Trim())

                If r.FontSize > 0 Then
                    styleR.AppendFormat("font-size: {0}pt; ", r.FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                End If

                If Not String.IsNullOrWhiteSpace(r.FontFamily) AndAlso r.FontFamily.ToLower() <> "arial" Then
                    styleR.AppendFormat("font-family: '{0}'; ", r.FontFamily.Trim())
                End If

                Dim hexColore As String = ConvertNomeColoreAHex(r.ColorHex)
                If hexColore <> "000000" AndAlso hexColore <> "auto" Then
                    styleR.AppendFormat("color: #{0}; ", hexColore)
                End If

                Dim hexHighlight As String = ConvertNomeColoreAHex(r.HighlightColorHex)
                If hexHighlight <> "000000" AndAlso hexHighlight <> "auto" Then
                    styleR.AppendFormat("background-color: #{0}; ", hexHighlight)
                End If

                If r.IsHyperlink AndAlso Not String.IsNullOrWhiteSpace(r.LinkUrl) Then
                    Dim urlSafe As String = System.Security.SecurityElement.Escape(r.LinkUrl)
                    If styleR.Length > 0 Then
                        sb.AppendFormat("<a href=""{0}""><span style=""{1}"">{2}</span></a>", urlSafe, styleR.ToString().Trim(), textSafe)
                    Else
                        sb.AppendFormat("<a href=""{0}"">{1}</a>", urlSafe, textSafe)
                    End If
                ElseIf styleR.Length > 0 Then
                    sb.AppendFormat("<span style=""{0}"">{1}</span>", styleR.ToString().Trim(), textSafe)
                Else
                    sb.Append(textSafe)
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog($"RenderRunsHtml: errore. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Function EstraiTitoloDocumento(elementiLocali As List(Of DocElement)) As String
        Try
            For Each el In elementiLocali
                If TypeOf el Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(el, DocParagraph)
                    Dim primoRun As DocRun = p.Runs?.FirstOrDefault(Function(r) r IsNot Nothing AndAlso Not r.IsImage AndAlso Not String.IsNullOrWhiteSpace(r.Text))
                    If primoRun IsNot Nothing AndAlso primoRun.Bold AndAlso primoRun.FontSize >= 16 Then
                        Dim testo As String = p.GetText().Trim()
                        If Not String.IsNullOrWhiteSpace(testo) Then Return testo
                    End If
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog($"EstraiTitoloDocumento: errore. Errore: {ex.Message}")
        End Try
        Return "Documento Esportato"
    End Function

    Private Function ConvertNomeColoreAHex(nome As String) As String
        Try
            If String.IsNullOrEmpty(nome) Then Return "000000"
            Dim nomeLower As String = nome.Trim().ToLower()
            If nomeLower = "nessuno" OrElse nomeLower = "none" OrElse nomeLower = "auto" Then Return "auto"
            Select Case nomeLower
                Case "nero" : Return "000000"
                Case "bianco" : Return "FFFFFF"
                Case "rosso" : Return "FF0000"
                Case "verde" : Return "008000"
                Case "blu" : Return "0000FF"
                Case "giallo" : Return "FFFF00"
                Case "arancione" : Return "FFA500"
                Case "viola" : Return "800080"
                Case "grigio" : Return "808080"
                Case "marrone" : Return "A52A2A"
                Case "celeste" : Return "ADD8E6"
                Case "rosa" : Return "FFC0CB"
                Case Else
                    If nome.Length = 6 AndAlso Regex.IsMatch(nome, "^[0-9A-Fa-f]{6}$") Then Return nome.ToUpper()
                    Return "000000"
            End Select
        Catch ex As Exception
            logger.ScriviLog($"ConvertNomeColoreAHex: errore su '{nome}'. Errore: {ex.Message}")
            Return "000000"
        End Try
    End Function

    Public Sub EsportaMd(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim sb As New StringBuilder()
            Dim cartellaBase As String = Path.GetDirectoryName(percorso)
            Dim nomeBase As String = Path.GetFileNameWithoutExtension(percorso)
            Dim contImgMd As Integer = 0

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            Dim prefisso As String = ""

                            If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso _stiliTitolo.ContainsKey(p.BookmarkId) Then
                                prefisso = New String("#"c, _stiliTitolo(p.BookmarkId)) & " "
                            End If

                            sb.Append(prefisso)
                            If p.Runs IsNot Nothing Then
                            For Each r In p.Runs
                                If r Is Nothing Then Continue For
                                If r.IsImage Then
                                    If r.ImageBytes IsNot Nothing AndAlso r.ImageBytes.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(cartellaBase) Then
                                        Try
                                            Dim fmtMd As FormatoImmagine = RilevaFormatoImmagine(r.ImageBytes)
                                            If fmtMd = FormatoImmagine.Vettoriale Then
                                                Continue For
                                            End If
                                            Dim estMd As String = "jpg"
                                            Dim datiMd As Byte() = r.ImageBytes
                                            If fmtMd = FormatoImmagine.Png Then
                                                estMd = "png"
                                            ElseIf fmtMd <> FormatoImmagine.Jpeg Then
                                                Dim convMd As Byte() = ConvertePngInJpeg(r.ImageBytes)
                                                If convMd IsNot Nothing AndAlso convMd.Length > 0 Then
                                                    datiMd = convMd
                                                Else
                                                    Continue For
                                                End If
                                            End If
                                            Dim nomeImgMd As String = $"{nomeBase}_img_{contImgMd}.{estMd}"
                                            Dim percorsoImgMd As String = Path.Combine(cartellaBase, nomeImgMd)
                                            File.WriteAllBytes(percorsoImgMd, datiMd)
                                            Dim altMd As String = If(String.IsNullOrWhiteSpace(r.Text), "immagine", r.Text).Replace("[", "(").Replace("]", ")")
                                            sb.Append("![" & altMd & "](" & nomeImgMd & ")")
                                            contImgMd += 1
                                        Catch exImgMd As Exception
                                            logger.ScriviLog($"EsportaMd: errore salvataggio immagine {contImgMd}. Errore: {exImgMd.Message}")
                                        End Try
                                    End If
                                    Continue For
                                End If
                                Dim t As String = If(r.Text, "")
                                If r.Bold Then t = "**" & t & "**"
                                If r.Italic Then t = "_" & t & "_"
                                sb.Append(t)
                            Next
                            End If
                            sb.AppendLine()
                            sb.AppendLine()
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            If tbl.Rows IsNot Nothing Then
                            For Each row In tbl.Rows
                                If row Is Nothing OrElse row.Cells Is Nothing Then Continue For
                                sb.Append("| ")
                                For Each cell In row.Cells
                                    If cell Is Nothing OrElse cell.Paragraphs Is Nothing Then Continue For
                                    Dim cellText As String = ""
                                    For Each p In cell.Paragraphs
                                        If p IsNot Nothing Then cellText &= p.GetText() & " "
                                    Next
                                    sb.Append(cellText.Trim() & " | ")
                                Next
                                sb.AppendLine()
                            Next
                            End If
                            sb.AppendLine()
                        End If
                    Next
                End If
            End SyncLock

            IO.File.WriteAllText(percorso, sb.ToString())
            MostraMessaggioStato("Documento esportato in Markdown")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private _processoInCorso As WGProcessoInCorso

    Private MaxRetryPdf As Integer = 3
    Private _percentualeCorrentePdf As Integer = 0
    Private _cacheFontTrueType As New Dictionary(Of String, Byte())

    Private _pageSettings As New Dictionary(Of String, Integer) From {
    {"PageWidth", 11906},
    {"PageHeight", 16838},
    {"MarginTop", 1417},
    {"MarginBottom", 1417},
    {"MarginLeft", 1417},
    {"MarginRight", 1417},
    {"Orientation", 1}
}

    Public Function EsportaPdf(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then
                Return False
            End If

            Dim directoryDestinazione As String = Path.GetDirectoryName(percorso)
            If Not String.IsNullOrWhiteSpace(directoryDestinazione) AndAlso Not Directory.Exists(directoryDestinazione) Then
                Try
                    Directory.CreateDirectory(directoryDestinazione)
                Catch exDir As Exception
                    logger.ScriviLog($"EsportaPdf: impossibile creare la directory '{directoryDestinazione}'. Errore: {exDir.Message}{Environment.NewLine}{exDir.StackTrace}")
                    Return False
                End Try
            End If

            Dim elementiLocali As New List(Of DocElement)
            Try
                SyncLock _elementsLock
                    If _elements IsNot Nothing Then
                        elementiLocali.AddRange(_elements)
                    End If
                End SyncLock
            Catch exLock As Exception
                logger.ScriviLog($"EsportaPdf: errore accesso elementi. Errore: {exLock.Message}{Environment.NewLine}{exLock.StackTrace}")
                Return False
            End Try

            If elementiLocali.Count = 0 Then
                Return False
            End If

            _processoInCorso = New WGProcessoInCorso()

            Dim barraPdf As New Panel()
            Try
                barraPdf.Size = New Size(Math.Max(200, _processoInCorso.ClientSize.Width - 20), 22)
                barraPdf.Location = New Point(10, _processoInCorso.ClientSize.Height - barraPdf.Height - 10)
                barraPdf.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
                barraPdf.BackColor = Color.FromArgb(210, 210, 210)
                barraPdf.BorderStyle = BorderStyle.None
                _processoInCorso.Controls.Add(barraPdf)
            Catch exPanel As Exception
                logger.ScriviLog($"EsportaPdf: errore aggiunta barra al form. Errore: {exPanel.Message}")
            End Try

            _processoInCorso.Avvia("Esportazione PDF in corso, attendere")

            Dim percorsoTemp As String = percorso & ".tmp"
            Dim margineSinistro As Double = 70.85
            Dim margineDestro As Double = 70.85
            Dim margineSuperiore As Double = 70.85
            Dim margineInferiore As Double = 70.85
            Dim larghezzaPagina As Double = 595
            Dim altezzaPagina As Double = 842
            Dim minMargine As Double = 10
            Dim maxMargine As Double = 200

            If _pageSettings IsNot Nothing Then
                If _pageSettings.ContainsKey("MarginLeft") Then margineSinistro = Math.Max(minMargine, Math.Min(maxMargine, _pageSettings("MarginLeft") / 20.0))
                If _pageSettings.ContainsKey("MarginRight") Then margineDestro = Math.Max(minMargine, Math.Min(maxMargine, _pageSettings("MarginRight") / 20.0))
                If _pageSettings.ContainsKey("MarginTop") Then margineSuperiore = Math.Max(minMargine, Math.Min(maxMargine, _pageSettings("MarginTop") / 20.0))
                If _pageSettings.ContainsKey("MarginBottom") Then margineInferiore = Math.Max(minMargine, Math.Min(maxMargine, _pageSettings("MarginBottom") / 20.0))
                If _pageSettings.ContainsKey("PageWidth") AndAlso _pageSettings("PageWidth") > 0 Then larghezzaPagina = _pageSettings("PageWidth") / 20.0
                If _pageSettings.ContainsKey("PageHeight") AndAlso _pageSettings("PageHeight") > 0 Then altezzaPagina = _pageSettings("PageHeight") / 20.0
            End If

            If margineSinistro + margineDestro >= larghezzaPagina Then
                margineSinistro = 70.85
                margineDestro = 70.85
            End If

            If margineSuperiore + margineInferiore >= altezzaPagina Then
                margineSuperiore = 70.85
                margineInferiore = 70.85
            End If

            Dim isLandscape As Boolean = False
            If _pageSettings IsNot Nothing AndAlso _pageSettings.ContainsKey("Orientation") Then
                isLandscape = (_pageSettings("Orientation") = 2)
            End If

            If isLandscape Then
                Dim tmp As Double = larghezzaPagina
                larghezzaPagina = altezzaPagina
                altezzaPagina = tmp
            End If

            Dim parametriLayout As New ParametriLayoutPdf With {
                .MargineSinistro = margineSinistro,
                .MargineDestro = margineDestro,
                .MargineSuperiore = margineSuperiore,
                .MargineInferiore = margineInferiore,
                .LarghezzaPagina = larghezzaPagina,
                .AltezzaPagina = altezzaPagina
            }

            Task.Run(Sub() EsportaPdfInBackground(percorso, percorsoTemp, elementiLocali, parametriLayout, barraPdf))

            Return True

        Catch ex As Exception
            logger.ScriviLog($"EsportaPdf: errore generale. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Try
                If _processoInCorso IsNot Nothing Then _processoInCorso.Fine()
            Catch exFine As Exception
                logger.ScriviLog($"EsportaPdf: errore Fine processo. Errore: {exFine.Message}")
            End Try
            Return False
        End Try
    End Function
    Private Sub EsportaPdfInBackground(percorso As String, percorsoTemp As String, elementiLocali As List(Of DocElement), parametriLayout As ParametriLayoutPdf, controlloProgresso As Control)
        Dim timerLettura As System.Threading.Timer = Nothing
        Try
            Threading.Volatile.Write(_percentualeCorrentePdf, 0)

            timerLettura = New System.Threading.Timer(
                Sub(stato As Object)
                    Try
                        lettura.LeggiTesto($"{Threading.Volatile.Read(_percentualeCorrentePdf)} percento")
                    Catch exTimer As Exception
                        logger.ScriviLog($"EsportaPdfInBackground: errore lettura vocale. Errore: {exTimer.Message}")
                    End Try
                End Sub,
                Nothing,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30))

            Dim builder As New UglyToad.PdfPig.Writer.PdfDocumentBuilder()

            Dim fontStandard As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont = Nothing
            Dim fontBold As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont = Nothing
            Dim fontItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont = Nothing
            Dim fontBoldItalic As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont = Nothing
            Dim cacheFontAggiunta As New Dictionary(Of String, UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont)

            Dim cartellaFont As String = Environment.GetFolderPath(Environment.SpecialFolder.Fonts)
            fontStandard = CaricaFontPdf(builder, cartellaFont, {"arial.ttf", "calibri.ttf", "verdana.ttf", "tahoma.ttf"})
            fontBold = CaricaFontPdf(builder, cartellaFont, {"arialbd.ttf", "calibrib.ttf", "verdanab.ttf", "tahomabd.ttf"})
            fontItalic = CaricaFontPdf(builder, cartellaFont, {"ariali.ttf", "calibrii.ttf", "verdanai.ttf", "tahoma.ttf"})
            fontBoldItalic = CaricaFontPdf(builder, cartellaFont, {"arialbi.ttf", "calibriz.ttf", "verdanaz.ttf", "tahomabd.ttf"})

            Try
                If fontStandard Is Nothing Then fontStandard = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica)
                If fontBold Is Nothing Then fontBold = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.HelveticaBold)
                If fontItalic Is Nothing Then fontItalic = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.HelveticaOblique)
                If fontBoldItalic Is Nothing Then fontBoldItalic = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.HelveticaBoldOblique)
            Catch exFont As Exception
                logger.ScriviLog($"EsportaPdfInBackground: errore fallback font standard. Errore: {exFont.Message}{Environment.NewLine}{exFont.StackTrace}")
            End Try

            Dim paginaCorrente As UglyToad.PdfPig.Writer.PdfPageBuilder = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
            Dim correnteY As Double = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
            Dim numeroPagina As Integer = 1
            Dim totaleElementi As Integer = elementiLocali.Count
            Dim elementiSaltati As Integer = 0

            AggiornaBarraProgresso(controlloProgresso, 0, totaleElementi)

            For i As Integer = 0 To elementiLocali.Count - 1
                Dim elementoLocale As DocElement = elementiLocali(i)
                Dim riuscito As Boolean = False
                Dim tentativo As Integer = 0

                Do While Not riuscito AndAlso tentativo < MaxRetryPdf
                    tentativo += 1
                    Try
                        If TypeOf elementoLocale Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(elementoLocale, DocParagraph)

                            If p.IsPageBreak Then
                                paginaCorrente = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
                                correnteY = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
                                numeroPagina += 1
                                riuscito = True
                                Continue Do
                            End If

                            correnteY -= p.SpacingBefore

                            Dim tuttiImmagini As Boolean = p.Runs IsNot Nothing AndAlso p.Runs.Count > 0
                            If tuttiImmagini Then
                                For Each rCheck As DocRun In p.Runs
                                    If rCheck Is Nothing OrElse Not rCheck.IsImage Then
                                        tuttiImmagini = False
                                        Exit For
                                    End If
                                Next
                            End If
                            If tuttiImmagini Then
                                For Each runImmagine As DocRun In p.Runs
                                    Dim altezzaImg As Double = CalcolaAltezzaImmagine(runImmagine, parametriLayout.LarghezzaPagina - parametriLayout.MargineSinistro - parametriLayout.MargineDestro, parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore - parametriLayout.MargineInferiore)

                                    If correnteY - altezzaImg < parametriLayout.MargineInferiore Then
                                        paginaCorrente = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
                                        numeroPagina += 1
                                        correnteY = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
                                    End If

                                    correnteY = DisegnaRunImmaginePdf(paginaCorrente, runImmagine, correnteY,
                                        parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                        parametriLayout.MargineInferiore,
                                        parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina,
                                        p.Alignment)
                                Next
                            Else
                                Dim fontsParaRun As New Dictionary(Of String, UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont)
                                If p.Runs IsNot Nothing Then
                                    For Each run As DocRun In p.Runs
                                        If run IsNot Nothing AndAlso Not run.IsImage AndAlso Not String.IsNullOrWhiteSpace(run.FontFamily) Then
                                            Dim chiave As String = run.FontFamily.ToLower() & "_" & run.Bold.ToString() & "_" & run.Italic.ToString()
                                            If Not fontsParaRun.ContainsKey(chiave) AndAlso Not cacheFontAggiunta.ContainsKey(chiave) Then
                                                Dim fontRun = CaricaFontFamigliaPerRun(builder, cartellaFont, run.FontFamily, run.Bold, run.Italic)
                                                If fontRun IsNot Nothing Then
                                                    cacheFontAggiunta(chiave) = fontRun
                                                End If
                                            End If
                                        End If
                                    Next
                                End If

                                Dim nuovaY As Double = DisegnaParagrafoPdf(builder, paginaCorrente, p, correnteY,
                                    parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                    parametriLayout.MargineInferiore, parametriLayout.MargineSuperiore,
                                    parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina,
                                    fontStandard, fontBold, fontItalic, fontBoldItalic, cacheFontAggiunta)

                                If nuovaY < parametriLayout.MargineInferiore Then
                                    paginaCorrente = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
                                    numeroPagina += 1
                                    correnteY = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
                                    nuovaY = DisegnaParagrafoPdf(builder, paginaCorrente, p, correnteY,
                                        parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                        parametriLayout.MargineInferiore, parametriLayout.MargineSuperiore,
                                        parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina,
                                        fontStandard, fontBold, fontItalic, fontBoldItalic, cacheFontAggiunta)
                                End If

                                correnteY = nuovaY

                                If p.Runs IsNot Nothing Then
                                    For Each runImmagine As DocRun In p.Runs
                                        If Not runImmagine.IsImage Then Continue For
                                        If runImmagine.ImageBytes Is Nothing OrElse runImmagine.ImageBytes.Length = 0 Then Continue For
                                        Dim altezzaImg As Double = CalcolaAltezzaImmagine(runImmagine, parametriLayout.LarghezzaPagina - parametriLayout.MargineSinistro - parametriLayout.MargineDestro, parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore - parametriLayout.MargineInferiore)
                                        If correnteY - altezzaImg < parametriLayout.MargineInferiore Then
                                            paginaCorrente = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
                                            numeroPagina += 1
                                            correnteY = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
                                        End If
                                        correnteY = DisegnaRunImmaginePdf(paginaCorrente, runImmagine, correnteY,
                                            parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                            parametriLayout.MargineInferiore,
                                            parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina,
                                            p.Alignment)
                                    Next
                                End If
                            End If

                            correnteY -= p.SpacingAfter

                        ElseIf TypeOf elementoLocale Is DocTable Then
                            Dim tbl As DocTable = DirectCast(elementoLocale, DocTable)

                            Dim nuovaY As Double = DisegnaTabellaPdf(builder, paginaCorrente, tbl, correnteY,
                                parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                parametriLayout.MargineInferiore, parametriLayout.MargineSuperiore,
                                parametriLayout.AltezzaPagina, parametriLayout.LarghezzaPagina,
                                fontStandard, fontBold)

                            If nuovaY < parametriLayout.MargineInferiore Then
                                paginaCorrente = AggiuntaPagina(builder, parametriLayout.LarghezzaPagina, parametriLayout.AltezzaPagina)
                                numeroPagina += 1
                                correnteY = parametriLayout.AltezzaPagina - parametriLayout.MargineSuperiore
                                nuovaY = DisegnaTabellaPdf(builder, paginaCorrente, tbl, correnteY,
                                    parametriLayout.MargineSinistro, parametriLayout.MargineDestro,
                                    parametriLayout.MargineInferiore, parametriLayout.MargineSuperiore,
                                    parametriLayout.AltezzaPagina, parametriLayout.LarghezzaPagina,
                                    fontStandard, fontBold)
                            End If

                            correnteY = nuovaY
                        End If

                        riuscito = True
                        Threading.Volatile.Write(_percentualeCorrentePdf, CInt(((i + 1) / CDbl(totaleElementi)) * 100))
                        AggiornaBarraProgresso(controlloProgresso, i + 1, totaleElementi)

                    Catch exEl As Exception
                        logger.ScriviLog($"EsportaPdfInBackground: errore elemento {i + 1}/{totaleElementi}, tentativo {tentativo}/{MaxRetryPdf} (tipo: {elementoLocale.GetType().Name}). Errore: {exEl.Message}{Environment.NewLine}{exEl.StackTrace}")
                        If tentativo >= MaxRetryPdf Then
                            riuscito = True
                            elementiSaltati += 1
                        Else
                            Try
                                Thread.Sleep(200 * tentativo)
                            Catch exSleep As Exception
                                logger.ScriviLog($"EsportaPdfInBackground: errore sleep. Errore: {exSleep.Message}")
                            End Try
                        End If
                    End Try
                Loop
            Next

            Try
                Dim fileBytes As Byte() = builder.Build()

                If fileBytes Is Nothing OrElse fileBytes.Length = 0 Then
                    MostraMessaggioStato("Errore durante l'esportazione PDF: file generato vuoto.")
                    Return
                End If

                File.WriteAllBytes(percorsoTemp, fileBytes)
                If File.Exists(percorso) Then File.Delete(percorso)
                File.Move(percorsoTemp, percorso)

                Threading.Volatile.Write(_percentualeCorrentePdf, 100)
                AggiornaBarraProgresso(controlloProgresso, totaleElementi, totaleElementi)

                If elementiSaltati > 0 Then
                    MostraMessaggioStato($"Esportazione PDF completata, ma {elementiSaltati} elementi non sono stati inclusi. Controlla il registro errori.")
                Else
                    MostraMessaggioStato("Esportazione PDF completata")
                End If

            Catch exWrite As Exception
                logger.ScriviLog($"EsportaPdfInBackground: errore scrittura file '{percorso}'. Errore: {exWrite.Message}{Environment.NewLine}{exWrite.StackTrace}")
                MostraMessaggioStato("Errore durante la scrittura del file PDF.")
                Try
                    If File.Exists(percorsoTemp) Then File.Delete(percorsoTemp)
                Catch exClean As Exception
                    logger.ScriviLog($"EsportaPdfInBackground: errore eliminazione file temporaneo. Errore: {exClean.Message}")
                End Try
            End Try

        Catch ex As Exception
            logger.ScriviLog($"EsportaPdfInBackground: errore generale. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            MostraMessaggioStato("Errore durante l'esportazione PDF.")
        Finally
            Try
                If timerLettura IsNot Nothing Then
                    timerLettura.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite)
                    timerLettura.Dispose()
                End If
            Catch exTimer As Exception
                logger.ScriviLog($"EsportaPdfInBackground: errore dispose timer. Errore: {exTimer.Message}")
            End Try
            Try
                If _processoInCorso IsNot Nothing Then _processoInCorso.Fine()
            Catch exFine As Exception
                logger.ScriviLog($"EsportaPdfInBackground: errore Fine processo. Errore: {exFine.Message}")
            End Try
        End Try
    End Sub

    Private Function CalcolaAltezzaImmagine(run As DocRun, areaLarghezza As Double, areaAltezzaMax As Double) As Double
        Try
            If run Is Nothing OrElse run.ImageBytes Is Nothing OrElse run.ImageBytes.Length = 0 Then Return 0

            Dim larghezzaOriginale As Double = run.ImageWidthPt
            Dim altezzaOriginale As Double = run.ImageHeightPt

            If larghezzaOriginale <= 0 OrElse altezzaOriginale <= 0 Then
                Try
                    Using ms As New IO.MemoryStream(run.ImageBytes)
                        Using bmp As New System.Drawing.Bitmap(ms)
                            larghezzaOriginale = bmp.Width
                            altezzaOriginale = bmp.Height
                        End Using
                    End Using
                Catch exDim As Exception
                    logger.ScriviLog($"CalcolaAltezzaImmagine: errore lettura dimensioni. Errore: {exDim.Message}")
                    Return 0
                End Try
            End If

            If larghezzaOriginale <= 0 OrElse altezzaOriginale <= 0 Then Return 0

            Dim scala As Double = 1.0
            If larghezzaOriginale > areaLarghezza Then scala = areaLarghezza / larghezzaOriginale
            Dim altezzaScalata As Double = altezzaOriginale * scala
            If altezzaScalata > areaAltezzaMax Then altezzaScalata = areaAltezzaMax

            Return altezzaScalata
        Catch ex As Exception
            logger.ScriviLog($"CalcolaAltezzaImmagine: errore. Errore: {ex.Message}")
            Return 0
        End Try
    End Function

    Private Function DisegnaRunImmaginePdf(pagina As UglyToad.PdfPig.Writer.PdfPageBuilder,
                                            run As DocRun,
                                            startY As Double,
                                            margineSinistro As Double,
                                            margineDestro As Double,
                                            margineInferiore As Double,
                                            larghezzaPagina As Double,
                                            altezzaPagina As Double,
                                            Optional allineamento As String = "Left") As Double
        Try
            If run Is Nothing OrElse run.ImageBytes Is Nothing OrElse run.ImageBytes.Length = 0 Then
                Return startY
            End If

            Dim areaLarghezza As Double = larghezzaPagina - margineSinistro - margineDestro
            Dim areaAltezzaMax As Double = altezzaPagina - margineInferiore

            Dim larghezzaOriginale As Double = run.ImageWidthPt
            Dim altezzaOriginale As Double = run.ImageHeightPt

            If larghezzaOriginale <= 0 OrElse altezzaOriginale <= 0 Then
                Try
                    Using ms As New IO.MemoryStream(run.ImageBytes)
                        Using bmp As New System.Drawing.Bitmap(ms)
                            larghezzaOriginale = bmp.Width
                            altezzaOriginale = bmp.Height
                        End Using
                    End Using
                Catch exDim As Exception
                    logger.ScriviLog($"DisegnaRunImmaginePdf: impossibile leggere dimensioni. Errore: {exDim.Message}")
                    Return startY
                End Try
            End If

            If larghezzaOriginale <= 0 OrElse altezzaOriginale <= 0 Then Return startY

            Dim scala As Double = 1.0
            If larghezzaOriginale > areaLarghezza Then scala = areaLarghezza / larghezzaOriginale
            Dim larghezzaScalata As Double = larghezzaOriginale * scala
            Dim altezzaScalata As Double = altezzaOriginale * scala

            If altezzaScalata > areaAltezzaMax Then
                Dim scalaH As Double = areaAltezzaMax / altezzaScalata
                larghezzaScalata *= scalaH
                altezzaScalata *= scalaH
            End If

            Dim xImmagine As Double = margineSinistro
            Select Case If(allineamento, "").ToLower()
                Case "center", "centre"
                    xImmagine = margineSinistro + (areaLarghezza - larghezzaScalata) / 2.0
                Case "right"
                    xImmagine = larghezzaPagina - margineDestro - larghezzaScalata
                Case Else
                    xImmagine = margineSinistro
            End Select

            Dim bottomY As Double = startY - altezzaScalata
            Dim rettangolo As New UglyToad.PdfPig.Core.PdfRectangle(xImmagine, bottomY, xImmagine + larghezzaScalata, startY)

            Dim datiJpeg As Byte() = Nothing
            Dim formato As FormatoImmagine = RilevaFormatoImmagine(run.ImageBytes)

            Select Case formato
                Case FormatoImmagine.Jpeg
                    datiJpeg = run.ImageBytes
                Case FormatoImmagine.Vettoriale
                    Return startY
                Case Else
                    datiJpeg = ConvertePngInJpeg(run.ImageBytes)
            End Select

            If datiJpeg IsNot Nothing AndAlso datiJpeg.Length > 0 Then
                pagina.AddJpeg(datiJpeg, rettangolo)
            Else
                Return startY
            End If

            Return bottomY - 6

        Catch ex As Exception
            logger.ScriviLog($"DisegnaRunImmaginePdf: errore. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return startY
        End Try
    End Function

    Private Enum FormatoImmagine
        Sconosciuto
        Jpeg
        Png
        Gif
        Bmp
        Tiff
        Vettoriale
    End Enum

    Private Function RilevaFormatoImmagine(dati As Byte()) As FormatoImmagine
        Try
            If dati Is Nothing OrElse dati.Length < 4 Then Return FormatoImmagine.Sconosciuto
            If dati(0) = &HFF AndAlso dati(1) = &HD8 AndAlso dati(2) = &HFF Then Return FormatoImmagine.Jpeg
            If dati(0) = &H89 AndAlso dati(1) = &H50 AndAlso dati(2) = &H4E AndAlso dati(3) = &H47 Then Return FormatoImmagine.Png
            If dati(0) = &H47 AndAlso dati(1) = &H49 AndAlso dati(2) = &H46 Then Return FormatoImmagine.Gif
            If dati(0) = &H42 AndAlso dati(1) = &H4D Then Return FormatoImmagine.Bmp
            If (dati(0) = &H49 AndAlso dati(1) = &H49 AndAlso dati(2) = &H2A AndAlso dati(3) = &H0) OrElse
               (dati(0) = &H4D AndAlso dati(1) = &H4D AndAlso dati(2) = &H0 AndAlso dati(3) = &H2A) Then Return FormatoImmagine.Tiff
            If dati(0) = &H01 AndAlso dati(1) = &H0 AndAlso dati(2) = &H0 AndAlso dati(3) = &H0 Then Return FormatoImmagine.Vettoriale
            If dati(0) = &HD7 AndAlso dati(1) = &HCD Then Return FormatoImmagine.Vettoriale
            If dati.Length >= 8 AndAlso dati(0) = &H57 AndAlso dati(1) = &H4D AndAlso dati(2) = &H50 AndAlso dati(3) = &H48 Then Return FormatoImmagine.Vettoriale
            Return FormatoImmagine.Sconosciuto
        Catch ex As Exception
            logger.ScriviLog($"RilevaFormatoImmagine: errore. Errore: {ex.Message}")
            Return FormatoImmagine.Sconosciuto
        End Try
    End Function

    Private Function ConvertePngInJpeg(datiInput As Byte()) As Byte()
        Try
            If datiInput Is Nothing OrElse datiInput.Length = 0 Then Return Nothing
            Dim fmt As FormatoImmagine = RilevaFormatoImmagine(datiInput)
            If fmt = FormatoImmagine.Vettoriale Then
                Return Nothing
            End If
            Using msIn As New IO.MemoryStream(datiInput)
                Using img As System.Drawing.Image = System.Drawing.Image.FromStream(msIn)
                    Using msOut As New IO.MemoryStream()
                        Dim codecJpeg As System.Drawing.Imaging.ImageCodecInfo = Nothing
                        For Each codec As System.Drawing.Imaging.ImageCodecInfo In System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                            If codec.MimeType = "image/jpeg" Then
                                codecJpeg = codec
                                Exit For
                            End If
                        Next
                        If codecJpeg Is Nothing Then
                            Return Nothing
                        End If
                        Dim parametriJpeg As New System.Drawing.Imaging.EncoderParameters(1)
                        parametriJpeg.Param(0) = New System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L)
                        If System.Drawing.Image.IsAlphaPixelFormat(img.PixelFormat) Then
                            Using bmpRgb As New System.Drawing.Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                                Using g As System.Drawing.Graphics = System.Drawing.Graphics.FromImage(bmpRgb)
                                    g.Clear(System.Drawing.Color.White)
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                                    g.DrawImage(img, 0, 0, img.Width, img.Height)
                                End Using
                                bmpRgb.Save(msOut, codecJpeg, parametriJpeg)
                            End Using
                        Else
                            img.Save(msOut, codecJpeg, parametriJpeg)
                        End If
                        Return msOut.ToArray()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"ConvertePngInJpeg: errore conversione. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function CaricaFontPdf(builder As UglyToad.PdfPig.Writer.PdfDocumentBuilder, cartellaFont As String, candidati As String()) As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont
        For Each nomeFile As String In candidati
            Try
                Dim percorsoFont As String = Path.Combine(cartellaFont, nomeFile)
                If File.Exists(percorsoFont) Then
                    Dim chiave As String = nomeFile.ToLower()
                    If Not _cacheFontTrueType.ContainsKey(chiave) Then
                        _cacheFontTrueType(chiave) = File.ReadAllBytes(percorsoFont)
                    End If
                    Return builder.AddTrueTypeFont(_cacheFontTrueType(chiave))
                End If
            Catch ex As Exception
                logger.ScriviLog($"CaricaFontPdf: errore caricamento '{nomeFile}'. Errore: {ex.Message}")
            End Try
        Next
        Return Nothing
    End Function

    Private Function CaricaFontFamigliaPerRun(builder As UglyToad.PdfPig.Writer.PdfDocumentBuilder, cartellaFont As String, famiglia As String, bold As Boolean, italic As Boolean) As UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont
        Try
            Dim nomeFamiglia As String = famiglia.ToLower().Trim()
            Dim suffissoBold As String = If(bold AndAlso italic, "bi", If(bold, "bd", If(italic, "i", "")))

            Dim candidati As String() = {
                nomeFamiglia.Replace(" ", "") & suffissoBold & ".ttf",
                nomeFamiglia.Replace(" ", "-") & "-" & If(bold, "bold", "") & If(italic, "italic", "") & ".ttf",
                nomeFamiglia & ".ttf"
            }

            Return CaricaFontPdf(builder, cartellaFont, candidati)
        Catch ex As Exception
            logger.ScriviLog($"CaricaFontFamigliaPerRun: errore per famiglia '{famiglia}'. Errore: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Private Sub AggiornaBarraProgresso(ctrl As Control, valore As Integer, massimo As Integer)
        If ctrl Is Nothing Then Return
        Try
            If ctrl.IsDisposed Then Return
            If ctrl.InvokeRequired Then
                ctrl.Invoke(New Action(Sub() AggiornaBarraProgresso(ctrl, valore, massimo)))
                Return
            End If
            If massimo <= 0 Then Return

            Dim percentuale As Integer = CInt((CDbl(valore) / CDbl(massimo)) * 100)
            Dim larghezzaBarra As Integer = CInt(ctrl.Width * (CDbl(valore) / CDbl(massimo)))

            Using g As Graphics = ctrl.CreateGraphics()
                Using pennelloSfondo As New SolidBrush(Color.FromArgb(210, 210, 210))
                    g.FillRectangle(pennelloSfondo, New Rectangle(0, 0, ctrl.Width, ctrl.Height))
                End Using

                If larghezzaBarra > 0 Then
                    Dim rettBarra As New Rectangle(0, 0, larghezzaBarra, ctrl.Height)
                    Using gradiente As New System.Drawing.Drawing2D.LinearGradientBrush(
                        rettBarra,
                        Color.FromArgb(70, 130, 200),
                        Color.FromArgb(30, 90, 160),
                        System.Drawing.Drawing2D.LinearGradientMode.Vertical)
                        g.FillRectangle(gradiente, rettBarra)
                    End Using

                    Using pennelloLuce As New SolidBrush(Color.FromArgb(60, 255, 255, 255))
                        g.FillRectangle(pennelloLuce, New Rectangle(0, 0, larghezzaBarra, ctrl.Height \ 2))
                    End Using
                End If

                Dim testo As String = $"{percentuale}%"
                Using fontTesto As New Font("Arial", 8, FontStyle.Bold)
                    Dim dimTesto As SizeF = g.MeasureString(testo, fontTesto)
                    Dim xTesto As Single = (ctrl.Width - dimTesto.Width) / 2
                    Dim yTesto As Single = (ctrl.Height - dimTesto.Height) / 2

                    Using pennelloOmbra As New SolidBrush(Color.FromArgb(80, 0, 0, 0))
                        g.DrawString(testo, fontTesto, pennelloOmbra, xTesto + 1, yTesto + 1)
                    End Using
                    Using pennelloTesto As New SolidBrush(Color.White)
                        g.DrawString(testo, fontTesto, pennelloTesto, xTesto, yTesto)
                    End Using
                End Using

                Using pennaBordo As New Pen(Color.FromArgb(150, 150, 150), 1)
                    g.DrawRectangle(pennaBordo, 0, 0, ctrl.Width - 1, ctrl.Height - 1)
                End Using
            End Using

        Catch ex As Exception
            logger.ScriviLog($"AggiornaBarraProgresso: errore. Errore: {ex.Message}")
        End Try
    End Sub

    Private Function AggiuntaPagina(builder As UglyToad.PdfPig.Writer.PdfDocumentBuilder, larghezza As Double, altezza As Double) As UglyToad.PdfPig.Writer.PdfPageBuilder
        Try
            Return builder.AddPage(larghezza, altezza)
        Catch ex As Exception
            logger.ScriviLog($"AggiuntaPagina: errore ({larghezza}x{altezza}). Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4)
        End Try
    End Function

    Private Class ParametriLayoutPdf
        Public Property MargineSinistro As Double
        Public Property MargineDestro As Double
        Public Property MargineSuperiore As Double
        Public Property MargineInferiore As Double
        Public Property LarghezzaPagina As Double
        Public Property AltezzaPagina As Double
    End Class
End Class