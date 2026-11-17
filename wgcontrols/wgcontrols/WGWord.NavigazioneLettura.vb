Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

Partial Public Class WGWord
    Public Sub VaiAImmagineSuccessiva()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = _caretIndex
            Dim nuovoChar As Integer = _caretCharIndex
            Dim nomeImmagine As String = ""
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        Dim charCount As Integer = 0
                        For Each r As DocRun In p.Runs
                            Dim runLen As Integer = If(r.IsImage, 1, If(String.IsNullOrEmpty(r.Text), 0, r.Text.Length))
                            If r.IsImage AndAlso (i > _caretIndex OrElse charCount > _caretCharIndex) Then
                                nuovoIndex = i
                                nuovoChar = charCount
                                nomeImmagine = r.Text
                                trovato = True
                                Exit For
                            End If
                            charCount += runLen
                        Next
                    End If
                    If trovato Then Exit For
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _caretCharIndex = nuovoChar
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Immagine successiva: " & nomeImmagine)
            Else
                MostraMessaggioStato("Nessuna immagine successiva trovata.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAImmaginePrecedente()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = _caretIndex
            Dim nuovoChar As Integer = _caretCharIndex
            Dim nomeImmagine As String = ""
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        Dim charCount As Integer = 0
                        For Each rTot As DocRun In p.Runs
                            charCount += If(rTot.IsImage, 1, If(String.IsNullOrEmpty(rTot.Text), 0, rTot.Text.Length))
                        Next
                        For j As Integer = p.Runs.Count - 1 To 0 Step -1
                            Dim r As DocRun = p.Runs(j)
                            Dim runLen As Integer = If(r.IsImage, 1, If(String.IsNullOrEmpty(r.Text), 0, r.Text.Length))
                            charCount -= runLen
                            If r.IsImage AndAlso (i < _caretIndex OrElse charCount < _caretCharIndex) Then
                                nuovoIndex = i
                                nuovoChar = charCount
                                nomeImmagine = r.Text
                                trovato = True
                                Exit For
                            End If
                        Next
                    End If
                    If trovato Then Exit For
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _caretCharIndex = nuovoChar
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Immagine precedente: " & nomeImmagine)
            Else
                MostraMessaggioStato("Nessuna immagine precedente trovata.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiATabellaSuccessiva()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = -1
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex + 1 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocTable Then
                        nuovoIndex = i
                        trovato = True
                        Exit For
                    End If
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _tblRow = 0
                _tblCol = 0
                _tblPara = 0
                _caretCharIndex = 0
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Tabella successiva")
            Else
                MostraMessaggioStato("Nessuna tabella successiva.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiATabellaPrecedente()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = -1
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocTable Then
                        nuovoIndex = i
                        trovato = True
                        Exit For
                    End If
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _tblRow = 0
                _tblCol = 0
                _tblPara = 0
                _caretCharIndex = 0
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Tabella precedente")
            Else
                MostraMessaggioStato("Nessuna tabella precedente.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAIntestazioneSuccessiva()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = -1
            Dim livello As String = ""
            Dim testoTitolo As String = ""
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex + 1 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If Not String.IsNullOrEmpty(p.StyleId) AndAlso _stiliTitolo.ContainsKey(p.StyleId) Then
                            nuovoIndex = i
                            livello = _stiliTitolo(p.StyleId)
                            testoTitolo = p.GetText()
                            trovato = True
                            Exit For
                        End If
                    End If
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _caretCharIndex = 0
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Titolo livello " & livello & ": " & testoTitolo)
            Else
                MostraMessaggioStato("Nessuna intestazione successiva.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAIntestazionePrecedente()
        Try
            Dim trovato As Boolean = False
            Dim nuovoIndex As Integer = -1
            Dim livello As String = ""
            Dim testoTitolo As String = ""
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If Not String.IsNullOrEmpty(p.StyleId) AndAlso _stiliTitolo.ContainsKey(p.StyleId) Then
                            nuovoIndex = i
                            livello = _stiliTitolo(p.StyleId)
                            testoTitolo = p.GetText()
                            trovato = True
                            Exit For
                        End If
                    End If
                Next
            End SyncLock
            If trovato Then
                _caretIndex = nuovoIndex
                _caretCharIndex = 0
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Titolo livello " & livello & ": " & testoTitolo)
            Else
                MostraMessaggioStato("Nessuna intestazione precedente.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MettiInPausaLettura()
        Try
            If lettura IsNot Nothing Then lettura.FermaVoce()
            _letturaInPausa = True
            MostraMessaggioStato("Lettura in pausa")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RiprendiLettura()
        Try
            _letturaInPausa = False
            If Not String.IsNullOrEmpty(_ultimaFraseLetta) Then
                lettura.LeggiTesto(_ultimaFraseLetta)
            Else
                LeggiParagrafoCorrente()
            End If
            MostraMessaggioStato("Lettura ripresa")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RileggiUltimaFrase()
        Try
            If String.IsNullOrEmpty(_ultimaFraseLetta) Then
                LeggiFraseCorrente()
            Else
                lettura.LeggiTesto(_ultimaFraseLetta)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiSelezione()
        Try
            If Not _selectionActive Then
                MostraMessaggioStato("Nessuna selezione attiva")
                Return
            End If
            Dim selStartIdx As Integer = 0
            Dim selEndIdx As Integer = 0
            Dim selStartSub As Integer = 0
            Dim selEndSub As Integer = 0
            Dim selStartChar As Integer = 0
            Dim selEndChar As Integer = 0
            Dim sb As New StringBuilder()
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                CalculateSelectionRange(selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)
                If selStartIdx > selEndIdx OrElse (selStartIdx = selEndIdx AndAlso selStartChar >= selEndChar) Then Return
                For i As Integer = selStartIdx To Math.Min(selEndIdx, _elements.Count - 1)
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        Dim testoP As String = p.GetText()
                        Dim startInP As Integer = If(i = selStartIdx, selStartChar, 0)
                        Dim endInP As Integer = If(i = selEndIdx, selEndChar, testoP.Length)
                        If startInP < 0 Then startInP = 0
                        If endInP > testoP.Length Then endInP = testoP.Length
                        If endInP > startInP Then
                            sb.AppendLine(testoP.Substring(startInP, endInP - startInP))
                        End If
                    End If
                Next
            End SyncLock
            Dim risultato As String = sb.ToString().Trim()
            If String.IsNullOrEmpty(risultato) Then
                MostraMessaggioStato("Selezione vuota")
            Else
                lettura.LeggiTesto(risultato)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AnteprimaLetturaDocumento()
        Try
            Dim sb As New StringBuilder()
            Dim limite As Integer = 1000
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For Each el In _elements
                    If TypeOf el Is DocParagraph Then
                        Dim tLine As String = DirectCast(el, DocParagraph).GetText()
                        sb.AppendLine(tLine)
                        If sb.Length >= limite Then Exit For
                    End If
                Next
            End SyncLock
            Dim testo As String = sb.ToString().Trim()
            If String.IsNullOrEmpty(testo) Then
                MostraMessaggioStato("Documento vuoto")
                Return
            End If
            If testo.Length > limite Then
                Dim taglio As Integer = testo.LastIndexOf(" "c, limite)
                If taglio <= 0 Then taglio = limite
                testo = testo.Substring(0, taglio) & "..."
            End If
            lettura.LeggiTesto(testo)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiCellaCorrente()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then
                    MostraMessaggioStato("Non sei in una tabella")
                    Return
                End If
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then Return
                If _tblCol < 0 OrElse _tblCol >= tbl.Rows(_tblRow).Cells.Count Then Return
                Dim cell = tbl.Rows(_tblRow).Cells(_tblCol)
                Dim sb As New StringBuilder()
                For Each p In cell.Paragraphs
                    sb.AppendLine(p.GetText())
                Next
                Dim testo As String = sb.ToString().Trim()
                Dim msg As String = "Riga " & (_tblRow + 1) & " colonna " & (_tblCol + 1) & ": "
                If String.IsNullOrEmpty(testo) Then
                    msg &= "vuota"
                Else
                    msg &= testo
                End If
                lettura.LeggiTesto(msg, sopprimi500Ms:=True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiIntestazioneRiga()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then
                    MostraMessaggioStato("Non sei in una tabella")
                    Return
                End If
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then Return
                If tbl.Rows(_tblRow).Cells.Count = 0 Then Return
                Dim primaCella = tbl.Rows(_tblRow).Cells(0)
                Dim sb As New StringBuilder()
                For Each p In primaCella.Paragraphs
                    sb.AppendLine(p.GetText())
                Next
                Dim testo As String = sb.ToString().Trim()
                If String.IsNullOrEmpty(testo) Then testo = "vuota"
                lettura.LeggiTesto("Intestazione riga: " & testo, sopprimi500Ms:=True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiIntestazioneColonna()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then
                    MostraMessaggioStato("Non sei in una tabella")
                    Return
                End If
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If tbl.Rows.Count = 0 Then Return
                If _tblCol < 0 OrElse _tblCol >= tbl.Rows(0).Cells.Count Then Return
                Dim primaCella = tbl.Rows(0).Cells(_tblCol)
                Dim sb As New StringBuilder()
                For Each p In primaCella.Paragraphs
                    sb.AppendLine(p.GetText())
                Next
                Dim testo As String = sb.ToString().Trim()
                If String.IsNullOrEmpty(testo) Then testo = "vuota"
                lettura.LeggiTesto("Intestazione colonna: " & testo, sopprimi500Ms:=True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiInteraRiga()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then
                    MostraMessaggioStato("Non sei in una tabella")
                    Return
                End If
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then Return
                Dim sb As New StringBuilder()
                Dim numCol As Integer = 0
                For Each cell In tbl.Rows(_tblRow).Cells
                    numCol += 1
                    Dim testoCella As New StringBuilder()
                    For Each p In cell.Paragraphs
                        testoCella.Append(p.GetText() & " ")
                    Next
                    Dim t As String = testoCella.ToString().Trim()
                    If String.IsNullOrEmpty(t) Then t = "vuota"
                    sb.Append("Colonna " & numCol & ": " & t & ". ")
                Next
                lettura.LeggiTesto("Riga " & (_tblRow + 1) & ", " & sb.ToString(), sopprimi500Ms:=True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiInteraColonna()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then
                    MostraMessaggioStato("Non sei in una tabella")
                    Return
                End If
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If tbl.Rows.Count = 0 Then Return
                If _tblCol < 0 OrElse _tblCol >= tbl.Rows(0).Cells.Count Then Return
                Dim sb As New StringBuilder()
                Dim numRiga As Integer = 0
                For Each row In tbl.Rows
                    numRiga += 1
                    If _tblCol >= row.Cells.Count Then Continue For
                    Dim cell = row.Cells(_tblCol)
                    Dim testoCella As New StringBuilder()
                    For Each p In cell.Paragraphs
                        testoCella.Append(p.GetText() & " ")
                    Next
                    Dim t As String = testoCella.ToString().Trim()
                    If String.IsNullOrEmpty(t) Then t = "vuota"
                    sb.Append("Riga " & numRiga & ": " & t & ". ")
                Next
                lettura.LeggiTesto("Colonna " & (_tblCol + 1) & ", " & sb.ToString(), sopprimi500Ms:=True)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AllineaCella(allineamento As String)
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
                Dim el = _elements(_caretIndex)
                If TypeOf el IsNot DocTable Then Return
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then Return
                If _tblCol < 0 OrElse _tblCol >= tbl.Rows(_tblRow).Cells.Count Then Return
                Dim cell = tbl.Rows(_tblRow).Cells(_tblCol)
                For Each p In cell.Paragraphs
                    p.Alignment = allineamento
                Next
                tbl.CachedHeight = -1
            End SyncLock
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            MostraMessaggioStato("Cella allineata " & allineamento)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

End Class