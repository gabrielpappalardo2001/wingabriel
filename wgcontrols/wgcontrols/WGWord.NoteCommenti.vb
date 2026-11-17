Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq
Imports DocumentFormat.OpenXml.Spreadsheet

Partial Public Class WGWord
    Private _campiNotaInserimento As Campi = Nothing

    Private Function ParolaCorrenteHaGiaUnaNota() As Boolean
        Try
            Dim bookmarkEsistente As String = EBookmarkNotaAllaPosizioneCorrente()
            If String.IsNullOrEmpty(bookmarkEsistente) Then Return False
            If lettura IsNot Nothing Then lettura.LeggiTesto("Questa parola ha già una nota. Eliminala prima di inserirne un'altra.")
            Return True
        Catch ex As Exception
            logger.ScriviLog("ParolaCorrenteHaGiaUnaNota: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub InserisciNotaAPiePagina()
        Try
            If SolaLettura Then Return
            If ParolaCorrenteHaGiaUnaNota() Then Return
            _tipoNotaCorrente = "PIE"
            AvviaCampoInserimentoNota("Nuova nota a piè di pagina")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InserisciNotaDiChiusura()
        Try
            If SolaLettura Then Return
            If ParolaCorrenteHaGiaUnaNota() Then Return
            _tipoNotaCorrente = "FINE"
            AvviaCampoInserimentoNota("Nuova nota di chiusura")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AvviaCampoInserimentoNota(titolo As String)
        Try
            _campiNotaInserimento = New Campi()
            _campiNotaInserimento.AggiungiMultiriga(titolo, "c.notaNuova", False, False, "")
            _campiNotaInserimento.SalvaSuEscape = True
            _campiNotaInserimento.ImpostaMaiuscolaAutomatica("c.notaNuova", MaiuscolaAutomaticaAttiva)
            _campiNotaInserimento.ImpostaImpostazioniTemporanee(config.LeggiValore("echo_digitazione"), LivelloPunteggiaturaScrittura)
            AddHandler _campiNotaInserimento.Fine, AddressOf NotaTestoConfermato
            _campiNotaInserimento.Avvia()
        Catch ex As Exception
            logger.ScriviLog("AvviaCampoInserimentoNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub NotaTestoConfermato(sender As Object, e As EventArgs)
        Try
            If _campiNotaInserimento Is Nothing Then Return
            Dim testoNota As String = _campiNotaInserimento.RecuperaValore("c.notaNuova")
            RemoveHandler _campiNotaInserimento.Fine, AddressOf NotaTestoConfermato
            _campiNotaInserimento = Nothing
            If String.IsNullOrWhiteSpace(testoNota) Then Return

            Dim prefissoBkmk As String
            If _tipoNotaCorrente = "FINE" Then
                prefissoBkmk = "NOTA_FINE_"
            Else
                prefissoBkmk = "NOTA_PIE_"
            End If

            Dim numeroNota As Integer = 1
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each elTmp In _elements
                        If TypeOf elTmp Is DocParagraph Then
                            Dim pTmp As DocParagraph = DirectCast(elTmp, DocParagraph)
                            If Not String.IsNullOrEmpty(pTmp.BookmarkId) AndAlso pTmp.BookmarkId.StartsWith(prefissoBkmk) Then
                                numeroNota += 1
                            End If
                        End If
                    Next
                End If
            End SyncLock

            Dim bookmarkIdNota As String = prefissoBkmk & numeroNota

            SaveUndoState()

            Dim runMarcatore As New DocRun()
            runMarcatore.Text = numeroNota.ToString()
            runMarcatore.IsSuperscript = True
            runMarcatore.Bold = True
            runMarcatore.FontSize = 8
            runMarcatore.LinkUrl = bookmarkIdNota

            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim posizioneInserimento As Integer = TrovaFineParolaCorrente(para.GetText(), _caretCharIndex)
                Dim currentPos As Integer = 0
                Dim runIndex As Integer = -1
                Dim splitOffset As Integer = 0
                If para.Runs IsNot Nothing Then
                    For i As Integer = 0 To para.Runs.Count - 1
                        Dim r As DocRun = para.Runs(i)
                        If r.Text IsNot Nothing Then
                            Dim rLen As Integer = r.Text.Length
                            If currentPos + rLen >= posizioneInserimento Then
                                runIndex = i
                                splitOffset = Math.Max(0, posizioneInserimento - currentPos)
                                Exit For
                            End If
                            currentPos += rLen
                        End If
                    Next
                End If

                SyncLock _elementsLock
                    para.CachedHeight = -1
                    If runIndex = -1 OrElse para.Runs Is Nothing Then
                        If para.Runs Is Nothing Then para.Runs = New List(Of DocRun)()
                        para.Runs.Add(runMarcatore)
                    Else
                        Dim targetRun As DocRun = para.Runs(runIndex)
                        If targetRun.Text Is Nothing Then targetRun.Text = ""
                        splitOffset = Math.Min(splitOffset, targetRun.Text.Length)
                        Dim leftText As String = targetRun.Text.Substring(0, splitOffset)
                        Dim rightText As String = targetRun.Text.Substring(splitOffset)
                        targetRun.Text = leftText
                        Dim insertIdx As Integer = runIndex + 1
                        para.Runs.Insert(insertIdx, runMarcatore)
                        insertIdx += 1
                        If rightText.Length > 0 Then
                            Dim rightRun As DocRun = targetRun.Clone()
                            rightRun.Text = rightText
                            para.Runs.Insert(insertIdx, rightRun)
                        End If
                    End If
                End SyncLock
            End If

            Dim primaNotaStessaTipo As Integer = -1
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For idxScan As Integer = 0 To _elements.Count - 1
                        If TypeOf _elements(idxScan) Is DocParagraph Then
                            Dim pScan As DocParagraph = DirectCast(_elements(idxScan), DocParagraph)
                            If Not String.IsNullOrEmpty(pScan.BookmarkId) AndAlso pScan.BookmarkId.StartsWith(prefissoBkmk) Then
                                primaNotaStessaTipo = idxScan
                                Exit For
                            End If
                        End If
                    Next
                End If
            End SyncLock

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    Dim notaPara As New DocParagraph()
                    notaPara.BookmarkId = bookmarkIdNota
                    notaPara.LineSpacing = 1.0F

                    Dim runNumero As New DocRun()
                    runNumero.Text = numeroNota.ToString()
                    runNumero.IsSuperscript = True
                    runNumero.Bold = True
                    runNumero.FontSize = 8
                    runNumero.LinkUrl = bookmarkIdNota
                    notaPara.Runs.Add(runNumero)

                    Dim runSpazio As New DocRun()
                    runSpazio.Text = " "
                    notaPara.Runs.Add(runSpazio)

                    Dim runsNota As List(Of DocRun) = CreaRunsDaTestoPiano(testoNota, 9.0F)
                    For Each rn In runsNota
                        notaPara.Runs.Add(rn)
                    Next

                    If primaNotaStessaTipo = -1 Then
                        _elements.Add(notaPara)
                    Else
                        Dim insertPosNota As Integer = _elements.Count
                        For idxIns As Integer = primaNotaStessaTipo To _elements.Count - 1
                            If TypeOf _elements(idxIns) Is DocParagraph Then
                                Dim pIns As DocParagraph = DirectCast(_elements(idxIns), DocParagraph)
                                If Not String.IsNullOrEmpty(pIns.BookmarkId) AndAlso pIns.BookmarkId.StartsWith(prefissoBkmk) Then
                                    insertPosNota = idxIns + 1
                                End If
                            End If
                        Next
                        _elements.Insert(insertPosNota, notaPara)
                    End If
                End If
            End SyncLock

            SyncronizzaElementiInStato()
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(True)
            If _tipoNotaCorrente = "FINE" Then
                MostraMessaggioStato("Nota di chiusura " & numeroNota & " inserita")
            Else
                MostraMessaggioStato("Nota a piè di pagina " & numeroNota & " inserita")
            End If
        Catch ex As Exception
            logger.ScriviLog("NotaTestoConfermato Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub InserisciCommento()
        Try
            If SolaLettura Then Return
            If _campoCommentoCorrente IsNot Nothing Then
                _campoCommentoCorrente.chiudi()
                _campoCommentoCorrente = Nothing
            End If
            _campoCommentoCorrente = New WGCampoTesto()
            _campoCommentoCorrente.init("Testo del commento", "", True, False, False, True)
            AddHandler _campoCommentoCorrente.chiusura, AddressOf CommentoTestoConfermato
            _campoCommentoCorrente.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub CommentoTestoConfermato(sender As Object, e As EventArgs)
        Try
            If _campoCommentoCorrente Is Nothing Then Return
            Dim testoCommento As String = _campoCommentoCorrente.OttieniTesto()
            Dim segmentiCommento As List(Of SegmentoTesto) = _campoCommentoCorrente.OttieniMetadatiTesto()
            _campoCommentoCorrente.chiudi()
            _campoCommentoCorrente = Nothing
            If String.IsNullOrWhiteSpace(testoCommento) Then Return

            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            If String.IsNullOrEmpty(para.BookmarkId) Then
                para.BookmarkId = "CMT_" & Guid.NewGuid().ToString("N").Substring(0, 8)
            End If
            If Not _commentiPerParagrafo.ContainsKey(para.BookmarkId) Then
                _commentiPerParagrafo(para.BookmarkId) = New List(Of String)
            End If

            Dim testoFormattato As String = CreaTestoFormattatoRtfCompact(segmentiCommento)
            _commentiPerParagrafo(para.BookmarkId).Add(testoFormattato)

            _isModified = True
            MostraMessaggioStato("Commento aggiunto al paragrafo")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function TrovaFineParolaCorrente(testo As String, posizione As Integer) As Integer
        Try
            If String.IsNullOrEmpty(testo) Then Return 0
            Dim p As Integer = Math.Max(0, Math.Min(posizione, testo.Length))
            While p < testo.Length AndAlso Not Char.IsWhiteSpace(testo(p))
                p += 1
            End While
            Return p
        Catch ex As Exception
            logger.ScriviLog("TrovaFineParolaCorrente: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Math.Max(0, Math.Min(posizione, If(testo Is Nothing, 0, testo.Length)))
        End Try
    End Function

    Private Function CreaRunsDaTestoPiano(testo As String, dimensioneBase As Single) As List(Of DocRun)
        Dim risultato As New List(Of DocRun)()
        Try
            If String.IsNullOrEmpty(testo) Then
                Dim rFallback As New DocRun()
                rFallback.Text = ""
                rFallback.FontSize = dimensioneBase
                risultato.Add(rFallback)
                Return risultato
            End If

            Dim testoNormalizzato As String = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim righe As String() = testoNormalizzato.Split(ControlChars.Lf)
            For rigaIdx As Integer = 0 To righe.Length - 1
                Dim rigaTesto As String = righe(rigaIdx)
                If Not String.IsNullOrEmpty(rigaTesto) Then
                    Dim r As New DocRun()
                    r.Text = rigaTesto
                    r.FontFamily = "Arial"
                    r.FontSize = dimensioneBase
                    risultato.Add(r)
                End If
                If rigaIdx < righe.Length - 1 Then
                    Dim rNewline As New DocRun()
                    rNewline.Text = vbLf
                    rNewline.FontSize = dimensioneBase
                    risultato.Add(rNewline)
                End If
            Next

            If risultato.Count = 0 Then
                Dim rVuoto As New DocRun()
                rVuoto.Text = ""
                rVuoto.FontSize = dimensioneBase
                risultato.Add(rVuoto)
            End If
        Catch ex As Exception
            logger.ScriviLog("CreaRunsDaTestoPiano: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function CreaRunsDaSegmenti(segmenti As List(Of SegmentoTesto), dimensioneBase As Single) As List(Of DocRun)
        Dim risultato As New List(Of DocRun)()
        Try
            If segmenti Is Nothing OrElse segmenti.Count = 0 Then
                Dim rFallback As New DocRun()
                rFallback.Text = ""
                rFallback.FontSize = dimensioneBase
                risultato.Add(rFallback)
                Return risultato
            End If

            For Each seg In segmenti
                If seg Is Nothing Then Continue For
                Dim testo As String = If(seg.Testo, "")
                If String.IsNullOrEmpty(testo) Then Continue For

                Dim righe As String() = testo.Split(New String() {Environment.NewLine, vbLf}, StringSplitOptions.None)
                For rigaIdx As Integer = 0 To righe.Length - 1
                    Dim rigaTesto As String = righe(rigaIdx)
                    If Not String.IsNullOrEmpty(rigaTesto) Then
                        Dim r As New DocRun()
                        r.Text = rigaTesto
                        r.FontFamily = If(Not String.IsNullOrEmpty(seg.NomeFont), seg.NomeFont, "Arial")
                        r.FontSize = If(seg.Dimensione > 0, seg.Dimensione, dimensioneBase)
                        r.ColorHex = ConvertColorToHexName(seg.Colore)
                        r.Bold = seg.Grassetto
                        r.Italic = seg.Corsivo
                        r.Underline = seg.Sottolineato
                        risultato.Add(r)
                    End If
                    If rigaIdx < righe.Length - 1 Then
                        Dim rNewline As New DocRun()
                        rNewline.Text = vbLf
                        risultato.Add(rNewline)
                    End If
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog("CreaRunsDaSegmenti: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function CreaTestoFormattatoRtfCompact(segmenti As List(Of SegmentoTesto)) As String
        Try
            If segmenti Is Nothing OrElse segmenti.Count = 0 Then Return ""
            Dim tuttiUguali As Boolean = True
            Dim primoNominalFont As String = segmenti(0).NomeFont
            Dim primaDim As Single = segmenti(0).Dimensione
            Dim primoColore As System.Drawing.Color = segmenti(0).Colore
            Dim primoGrassetto As Boolean = segmenti(0).Grassetto
            Dim primoCorsivo As Boolean = segmenti(0).Corsivo
            Dim primoSotto As Boolean = segmenti(0).Sottolineato
            For Each seg In segmenti
                If seg.NomeFont <> primoNominalFont OrElse
                   Math.Abs(seg.Dimensione - primaDim) > 0.01F OrElse
                   seg.Colore <> primoColore OrElse
                   seg.Grassetto <> primoGrassetto OrElse
                   seg.Corsivo <> primoCorsivo OrElse
                   seg.Sottolineato <> primoSotto Then
                    tuttiUguali = False
                    Exit For
                End If
            Next
            If tuttiUguali Then
                Dim sb As New StringBuilder()
                For Each seg In segmenti
                    sb.Append(seg.Testo)
                Next
                Return sb.ToString()
            End If
            Dim sbRtf As New StringBuilder()
            sbRtf.Append("WGFMT:")
            For Each seg In segmenti
                sbRtf.Append("[")
                sbRtf.Append(seg.NomeFont.Replace("|", ""))
                sbRtf.Append("|")
                sbRtf.Append(seg.Dimensione.ToString("F1", Globalization.CultureInfo.InvariantCulture))
                sbRtf.Append("|")
                sbRtf.Append(seg.Colore.R.ToString() & "," & seg.Colore.G.ToString() & "," & seg.Colore.B.ToString())
                sbRtf.Append("|")
                sbRtf.Append(If(seg.Grassetto, "1", "0"))
                sbRtf.Append("|")
                sbRtf.Append(If(seg.Corsivo, "1", "0"))
                sbRtf.Append("|")
                sbRtf.Append(If(seg.Sottolineato, "1", "0"))
                sbRtf.Append("|")
                sbRtf.Append(seg.Testo.Replace("[", "&#91;").Replace("]", "&#93;"))
                sbRtf.Append("]")
            Next
            Return sbRtf.ToString()
        Catch ex As Exception
            logger.ScriviLog("CreaTestoFormattatoRtfCompact: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ConvertColorToHexName(c As System.Drawing.Color) As String
        Try
            If c = System.Drawing.Color.Empty OrElse c = System.Drawing.Color.Black OrElse (c.R = 0 AndAlso c.G = 0 AndAlso c.B = 0) Then
                Return "Nero"
            End If
            If c.R = 255 AndAlso c.G = 0 AndAlso c.B = 0 Then Return "Rosso"
            If c.R = 0 AndAlso c.G = 128 AndAlso c.B = 0 Then Return "Verde"
            If c.R = 0 AndAlso c.G = 0 AndAlso c.B = 255 Then Return "Blu"
            If c.R = 255 AndAlso c.G = 255 AndAlso c.B = 0 Then Return "Giallo"
            If c.R = 255 AndAlso c.G = 165 AndAlso c.B = 0 Then Return "Arancione"
            If c.R = 128 AndAlso c.G = 0 AndAlso c.B = 128 Then Return "Viola"
            If c.R = 128 AndAlso c.G = 128 AndAlso c.B = 128 Then Return "Grigio"
            If c.R = 255 AndAlso c.G = 255 AndAlso c.B = 255 Then Return "Bianco"
            Return "Nero"
        Catch ex As Exception
            logger.ScriviLog("ConvertColorToHexName: " & ex.Message)
            Return "Nero"
        End Try
    End Function

    Public Sub LeggiCommentiParagrafo()
        Try
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            If String.IsNullOrEmpty(para.BookmarkId) OrElse Not _commentiPerParagrafo.ContainsKey(para.BookmarkId) Then
                MostraMessaggioStato("Nessun commento in questo paragrafo")
                Return
            End If
            Dim lista = _commentiPerParagrafo(para.BookmarkId)
            If lista.Count = 0 Then
                MostraMessaggioStato("Nessun commento in questo paragrafo")
                Return
            End If
            Dim sb As New StringBuilder()
            For i As Integer = 0 To lista.Count - 1
                Dim testoLeggibile As String = EstraiTestoPiattoDaCommento(lista(i))
                sb.AppendLine("Commento " & (i + 1) & ": " & testoLeggibile)
            Next
            lettura.LeggiTesto(sb.ToString())
            MostraMessaggioStato(lista.Count & " commenti letti")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function EstraiTestoPiattoDaCommento(testoCommento As String) As String
        Try
            If String.IsNullOrEmpty(testoCommento) Then Return ""
            If Not testoCommento.StartsWith("WGFMT:") Then Return testoCommento
            Dim segmentiRicavati As List(Of SegmentoTesto) = ParseCommento(testoCommento)
            Dim sb As New StringBuilder()
            For Each seg In segmentiRicavati
                sb.Append(seg.Testo)
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("EstraiTestoPiattoDaCommento: " & ex.Message)
            Return testoCommento
        End Try
    End Function

    Private Function ParseCommento(testoCommento As String) As List(Of SegmentoTesto)
        Dim risultato As New List(Of SegmentoTesto)()
        Try
            If Not testoCommento.StartsWith("WGFMT:") Then
                risultato.Add(New SegmentoTesto() With {.Testo = testoCommento})
                Return risultato
            End If
            Dim corpo As String = testoCommento.Substring("WGFMT:".Length)
            Dim matches As MatchCollection = Regex.Matches(corpo, "\[([^\]]*)\]")
            For Each m As Match In matches
                Dim contenuto As String = m.Groups(1).Value
                Dim parti As String() = contenuto.Split(New Char() {"|"c}, 7)
                If parti.Length < 7 Then Continue For
                Dim seg As New SegmentoTesto()
                seg.NomeFont = If(Not String.IsNullOrEmpty(parti(0)), parti(0), "Arial")
                Dim dimensione As Single = 11.0F
                Single.TryParse(parti(1), Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, dimensione)
                seg.Dimensione = If(dimensione > 0, dimensione, 11.0F)
                Dim rgb As String() = parti(2).Split(","c)
                If rgb.Length = 3 Then
                    Dim r As Integer = 0, g As Integer = 0, b As Integer = 0
                    Integer.TryParse(rgb(0), r)
                    Integer.TryParse(rgb(1), g)
                    Integer.TryParse(rgb(2), b)
                    seg.Colore = System.Drawing.Color.FromArgb(r, g, b)
                Else
                    seg.Colore = System.Drawing.Color.Black
                End If
                seg.Grassetto = (parti(3) = "1")
                seg.Corsivo = (parti(4) = "1")
                seg.Sottolineato = (parti(5) = "1")
                seg.Testo = parti(6).Replace("&#91;", "[").Replace("&#93;", "]")
                risultato.Add(seg)
            Next
        Catch ex As Exception
            logger.ScriviLog("ParseCommento: " & ex.Message)
            risultato.Add(New SegmentoTesto() With {.Testo = testoCommento})
        End Try
        Return risultato
    End Function

    Friend Function CommentoARunsPerWord(testoCommento As String) As List(Of DocRun)
        Try
            Dim segmenti As List(Of SegmentoTesto) = ParseCommento(testoCommento)
            Return CreaRunsDaSegmenti(segmenti, 11.0F)
        Catch ex As Exception
            logger.ScriviLog("CommentoARunsPerWord: " & ex.Message)
            Dim fallback As New List(Of DocRun)()
            Dim r As New DocRun()
            r.Text = testoCommento
            r.FontSize = 11.0F
            fallback.Add(r)
            Return fallback
        End Try
    End Function

    Public Sub EliminaCommentoCorrente()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            If String.IsNullOrEmpty(para.BookmarkId) OrElse Not _commentiPerParagrafo.ContainsKey(para.BookmarkId) Then
                MostraMessaggioStato("Nessun commento da eliminare")
                Return
            End If
            Dim lista = _commentiPerParagrafo(para.BookmarkId)
            If lista.Count = 0 Then
                MostraMessaggioStato("Nessun commento da eliminare")
                Return
            End If
            lista.RemoveAt(lista.Count - 1)
            If lista.Count = 0 Then _commentiPerParagrafo.Remove(para.BookmarkId)
            _isModified = True
            MostraMessaggioStato("Ultimo commento eliminato")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuSimboli()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return
            If _menuSimboliCorrente IsNot Nothing Then
                _menuSimboliCorrente.abbandona()
                _menuSimboliCorrente = Nothing
            End If
            _menuSimboliCorrente = New WGMenuLista()
            _menuSimboliCorrente.impostaTitolo("Inserisci simbolo")
            Dim simboli As New List(Of Tuple(Of String, String))
            simboli.Add(New Tuple(Of String, String)("Euro", "€"))
            simboli.Add(New Tuple(Of String, String)("Copyright", "©"))
            simboli.Add(New Tuple(Of String, String)("Marchio registrato", "®"))
            simboli.Add(New Tuple(Of String, String)("Marchio", "™"))
            simboli.Add(New Tuple(Of String, String)("Grado", "°"))
            simboli.Add(New Tuple(Of String, String)("Paragrafo", "§"))
            simboli.Add(New Tuple(Of String, String)("Pilcrow", "¶"))
            simboli.Add(New Tuple(Of String, String)("Punto medio", "·"))
            simboli.Add(New Tuple(Of String, String)("Puntini di sospensione", "…"))
            simboli.Add(New Tuple(Of String, String)("Trattino lungo em dash", "—"))
            simboli.Add(New Tuple(Of String, String)("Trattino medio en dash", "–"))
            simboli.Add(New Tuple(Of String, String)("Caporale aperta", "«"))
            simboli.Add(New Tuple(Of String, String)("Caporale chiusa", "»"))
            simboli.Add(New Tuple(Of String, String)("Virgolette doppie aperta", """"))
            simboli.Add(New Tuple(Of String, String)("Virgolette doppie chiusa", """"))
            simboli.Add(New Tuple(Of String, String)("Apice singolo aperta", "'"))
            simboli.Add(New Tuple(Of String, String)("Apice singolo chiusa", "'"))
            simboli.Add(New Tuple(Of String, String)("Freccia destra", "→"))
            simboli.Add(New Tuple(Of String, String)("Freccia sinistra", "←"))
            simboli.Add(New Tuple(Of String, String)("Freccia su", "↑"))
            simboli.Add(New Tuple(Of String, String)("Freccia giù", "↓"))
            simboli.Add(New Tuple(Of String, String)("Spunta", "✓"))
            simboli.Add(New Tuple(Of String, String)("Croce", "✗"))
            simboli.Add(New Tuple(Of String, String)("Stella piena", "★"))
            simboli.Add(New Tuple(Of String, String)("Stella vuota", "☆"))
            simboli.Add(New Tuple(Of String, String)("Mezzo", "½"))
            simboli.Add(New Tuple(Of String, String)("Un terzo", "⅓"))
            simboli.Add(New Tuple(Of String, String)("Un quarto", "¼"))
            simboli.Add(New Tuple(Of String, String)("Tre quarti", "¾"))
            simboli.Add(New Tuple(Of String, String)("Più o meno", "±"))
            simboli.Add(New Tuple(Of String, String)("Per", "×"))
            simboli.Add(New Tuple(Of String, String)("Diviso", "÷"))
            simboli.Add(New Tuple(Of String, String)("Pi greco", "π"))
            simboli.Add(New Tuple(Of String, String)("Alpha", "α"))
            simboli.Add(New Tuple(Of String, String)("Beta", "β"))
            simboli.Add(New Tuple(Of String, String)("Gamma", "γ"))
            simboli.Add(New Tuple(Of String, String)("Delta", "δ"))
            simboli.Add(New Tuple(Of String, String)("Sigma", "Σ"))
            simboli.Add(New Tuple(Of String, String)("Omega", "Ω"))
            For Each tupla In simboli
                _menuSimboliCorrente.aggiungiElemento(tupla.Item1 & " " & tupla.Item2, tupla.Item2)
            Next
            AddHandler _menuSimboliCorrente.ElementoSelezionato, AddressOf SimboloSelezionato
            _menuSimboliCorrente.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SimboloSelezionato()
        Try
            If _menuSimboliCorrente Is Nothing Then Return
            Dim ids As List(Of String) = _menuSimboliCorrente.ottieniElementi()
            If ids IsNot Nothing AndAlso ids.Count > 0 Then
                Dim simbolo As String = ids(0)
                SaveUndoState()
                If _selectionActive Then DeleteSelection()
                For Each c As Char In simbolo
                    InsertChar(c)
                Next
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(True)
                MostraMessaggioStato("Simbolo " & simbolo & " inserito")
            End If
            _menuSimboliCorrente.abbandona()
            _menuSimboliCorrente = Nothing
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InserisciCarattereSpeciale()
        Try
            If SolaLettura Then Return
            If _campoCarattereSpecialeCorrente IsNot Nothing Then
                _campoCarattereSpecialeCorrente.chiudi()
                _campoCarattereSpecialeCorrente = Nothing
            End If
            _campoCarattereSpecialeCorrente = New WGCampoTesto()
            _campoCarattereSpecialeCorrente.init("Codice Unicode (es. 00A9 oppure 169)", "", False, False, False, True)
            AddHandler _campoCarattereSpecialeCorrente.chiusura, AddressOf CarattereSpecialeConfermato
            _campoCarattereSpecialeCorrente.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub CarattereSpecialeConfermato(sender As Object, e As EventArgs)
        Try
            If _campoCarattereSpecialeCorrente Is Nothing Then Return
            Dim codice As String = _campoCarattereSpecialeCorrente.OttieniTesto().Trim()
            _campoCarattereSpecialeCorrente.chiudi()
            _campoCarattereSpecialeCorrente = Nothing
            If String.IsNullOrEmpty(codice) Then Return

            Dim valore As Integer = -1
            If codice.StartsWith("U+", StringComparison.OrdinalIgnoreCase) Then codice = codice.Substring(2)
            If codice.StartsWith("0x", StringComparison.OrdinalIgnoreCase) Then codice = codice.Substring(2)
            If Not Integer.TryParse(codice, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, valore) Then
                If Not Integer.TryParse(codice, valore) Then
                    MostraMessaggioStato("Codice non valido")
                    Return
                End If
            End If
            If valore < 0 OrElse valore > &H10FFFF Then
                MostraMessaggioStato("Codice fuori intervallo Unicode")
                Return
            End If
            Dim carattere As String = Char.ConvertFromUtf32(valore)
            SaveUndoState()
            If _selectionActive Then DeleteSelection()
            For Each c As Char In carattere
                InsertChar(c)
            Next
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(True)
            MostraMessaggioStato("Carattere " & carattere & " inserito")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoSu()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _caretIndex <= 0 Then
                    MostraMessaggioStato("Già al primo paragrafo")
                    Return
                End If
                SaveUndoState()
                Dim corrente = _elements(_caretIndex)
                _elements.RemoveAt(_caretIndex)
                _caretIndex -= 1
                _elements.Insert(_caretIndex, corrente)
            End SyncLock
            _isModified = True
            TriggerLayout()
            ScrollToCaret()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Paragrafo spostato in alto")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoGiu()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _caretIndex >= _elements.Count - 1 Then
                    MostraMessaggioStato("Già all'ultimo paragrafo")
                    Return
                End If
                SaveUndoState()
                Dim corrente = _elements(_caretIndex)
                _elements.RemoveAt(_caretIndex)
                _caretIndex += 1
                If _caretIndex > _elements.Count Then _caretIndex = _elements.Count
                _elements.Insert(_caretIndex, corrente)
            End SyncLock
            _isModified = True
            TriggerLayout()
            ScrollToCaret()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Paragrafo spostato in basso")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub UnisciConParagrafoPrecedente()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _caretIndex <= 0 Then
                    MostraMessaggioStato("Nessun paragrafo precedente")
                    Return
                End If
                If TypeOf _elements(_caretIndex) IsNot DocParagraph Then Return
                If TypeOf _elements(_caretIndex - 1) IsNot DocParagraph Then
                    MostraMessaggioStato("Il precedente non è un paragrafo")
                    Return
                End If
                SaveUndoState()
                Dim pPrec As DocParagraph = DirectCast(_elements(_caretIndex - 1), DocParagraph)
                Dim pCurr As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                Dim lunghezzaPrec As Integer = pPrec.GetText().Length
                For Each r In pCurr.Runs
                    pPrec.Runs.Add(r.Clone())
                Next
                pPrec.CachedHeight = -1
                _elements.RemoveAt(_caretIndex)
                _caretIndex -= 1
                _caretCharIndex = lunghezzaPrec
            End SyncLock
            _isModified = True
            TriggerLayout()
            ScrollToCaret()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Paragrafi uniti")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub UnisciConParagrafoSuccessivo()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _caretIndex >= _elements.Count - 1 Then
                    MostraMessaggioStato("Nessun paragrafo successivo")
                    Return
                End If
                If TypeOf _elements(_caretIndex) IsNot DocParagraph Then Return
                If TypeOf _elements(_caretIndex + 1) IsNot DocParagraph Then
                    MostraMessaggioStato("Il successivo non è un paragrafo")
                    Return
                End If
                SaveUndoState()
                Dim pCurr As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                Dim pSucc As DocParagraph = DirectCast(_elements(_caretIndex + 1), DocParagraph)
                For Each r In pSucc.Runs
                    pCurr.Runs.Add(r.Clone())
                Next
                pCurr.CachedHeight = -1
                _elements.RemoveAt(_caretIndex + 1)
            End SyncLock
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Paragrafi uniti")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub Trasponi()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            Dim testo As String = para.GetText()
            If _caretCharIndex <= 0 OrElse _caretCharIndex >= testo.Length Then
                MostraMessaggioStato("Posizione non valida per trasporre")
                Return
            End If
            SaveUndoState()
            Dim charPrima As Char = testo(_caretCharIndex - 1)
            Dim charDopo As Char = testo(_caretCharIndex)
            Dim posCorrente As Integer = _caretCharIndex
            _caretCharIndex = posCorrente - 1
            _selectionActive = True
            _selAnchorIndex = _caretIndex
            _selAnchorSubIndex = GetPackedTableIndex()
            _selAnchorCharIndex = posCorrente + 1
            DeleteSelection()
            InsertChar(charDopo)
            InsertChar(charPrima)
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(True)
            MostraMessaggioStato("Caratteri trasposti")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub CapitalizzaFrase()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            Dim testo As String = para.GetText()
            Dim idxFrase As Integer = TrovaIndiceFraseCorrente(testo, _caretCharIndex)
            Dim startFrase As Integer = TrovaInizioFrase(testo, idxFrase)
            Dim primaLettera As Integer = startFrase
            While primaLettera < testo.Length AndAlso Not Char.IsLetter(testo(primaLettera))
                primaLettera += 1
            End While
            If primaLettera >= testo.Length Then
                MostraMessaggioStato("Nessuna lettera da capitalizzare")
                Return
            End If
            If Char.IsUpper(testo(primaLettera)) Then
                MostraMessaggioStato("Già maiuscola")
                Return
            End If
            SaveUndoState()
            SyncLock _elementsLock
                Dim charCount As Integer = 0
                For Each r As DocRun In para.Runs
                    If r.IsImage Then Continue For
                    Dim runLen As Integer = r.Text.Length
                    If primaLettera >= charCount AndAlso primaLettera < charCount + runLen Then
                        Dim posInRun As Integer = primaLettera - charCount
                        Dim testoRun As String = r.Text
                        Dim nuovoTesto As String = testoRun.Substring(0, posInRun) & Char.ToUpper(testoRun(posInRun)) & testoRun.Substring(posInRun + 1)
                        r.Text = nuovoTesto
                        para.CachedHeight = -1
                        Exit For
                    End If
                    charCount += runLen
                Next
            End SyncLock
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(False)
            MostraMessaggioStato("Frase capitalizzata")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ApplicaStileTitolo(livello As Integer)
        Try
            If SolaLettura Then Return
            If livello < 1 OrElse livello > 6 Then
                MostraMessaggioStato("Livello titolo non valido")
                Return
            End If
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            If String.IsNullOrEmpty(para.BookmarkId) Then
                para.BookmarkId = "STL_" & Guid.NewGuid().ToString("N").Substring(0, 8)
            End If
            para.HeadingLevel = livello
            _stiliTitolo(para.BookmarkId) = livello
            Dim dimensioneTitoloScelta As Single = DimensioneTitolo(livello)
            For Each r In para.Runs
                r.Bold = True
                r.FontSize = dimensioneTitoloScelta
            Next
            para.CachedHeight = -1
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            MostraMessaggioStato("Titolo livello " & livello & " applicato")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraStrutturaDocumento()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _menuStrutturaCorrente IsNot Nothing Then
                _menuStrutturaCorrente.abbandona()
                _menuStrutturaCorrente = Nothing
            End If
            _menuStrutturaCorrente = New WGMenuLista()
            _menuStrutturaCorrente.impostaTitolo("Struttura del documento")
            Dim trovati As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            If p.HeadingLevel >= 1 AndAlso p.HeadingLevel <= 6 Then
                                If String.IsNullOrEmpty(p.BookmarkId) Then
                                    p.BookmarkId = "STL_" & Guid.NewGuid().ToString("N").Substring(0, 8)
                                End If
                                Dim livello As Integer = p.HeadingLevel
                                Dim prefisso As String = New String(" "c, (livello - 1) * 2)
                                Dim testo As String = p.GetText().Trim()
                                If String.IsNullOrEmpty(testo) Then testo = "(senza testo)"
                                _menuStrutturaCorrente.aggiungiElemento(prefisso & "H" & livello & " " & testo, p.BookmarkId)
                                trovati += 1
                            End If
                        End If
                    Next
                End If
            End SyncLock
            If trovati = 0 Then
                MostraMessaggioStato("Nessuna intestazione nel documento")
                _menuStrutturaCorrente = Nothing
                Return
            End If
            AddHandler _menuStrutturaCorrente.ElementoSelezionato, AddressOf StrutturaSelezionata
            _menuStrutturaCorrente.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub StrutturaSelezionata()
        Try
            If _menuStrutturaCorrente Is Nothing Then Return
            Dim ids As List(Of String) = _menuStrutturaCorrente.ottieniElementi()
            If ids IsNot Nothing AndAlso ids.Count > 0 Then
                VaiABookmark(ids(0))
            End If
            _menuStrutturaCorrente.abbandona()
            _menuStrutturaCorrente = Nothing
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ContaParoleSelezione()
        Try
            If Not _selectionActive Then
                MostraMessaggioStato("Nessuna selezione attiva")
                Return
            End If
            Dim selStartIdx, selEndIdx, selStartSub, selEndSub, selStartChar, selEndChar As Integer
            CalculateSelectionRange(selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)
            Dim sb As New StringBuilder()
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = selStartIdx To Math.Min(selEndIdx, _elements.Count - 1)
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        Dim testoP As String = p.GetText()
                        Dim startInP As Integer = If(i = selStartIdx, selStartChar, 0)
                        Dim endInP As Integer = If(i = selEndIdx, selEndChar, testoP.Length)
                        If startInP < testoP.Length AndAlso endInP > startInP Then
                            sb.AppendLine(testoP.Substring(startInP, Math.Min(endInP, testoP.Length) - startInP))
                        End If
                    End If
                Next
            End SyncLock
            Dim testoSel As String = sb.ToString()
            Dim parole As Integer = testoSel.Split(New Char() {" "c, vbCr, vbLf, vbTab}, StringSplitOptions.RemoveEmptyEntries).Length
            Dim caratteri As Integer = testoSel.Length
            Dim msg As String = parole & " parole, " & caratteri & " caratteri nella selezione"
            MostraMessaggioStato(msg)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

End Class