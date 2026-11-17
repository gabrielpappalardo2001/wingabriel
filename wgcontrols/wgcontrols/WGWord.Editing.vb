Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

' Modifica del documento: inserimento di collegamenti, immagini, gestione dei segnalibri e manipolazione completa delle tabelle.
Partial Public Class WGWord
    Public Sub InserisciLink(nome As String, url As String)
        Try
            If SolaLettura Then Return
            If Not String.IsNullOrWhiteSpace(url) AndAlso Not url.StartsWith("http") AndAlso Not url.StartsWith("#") Then
                url = "https://" & url
            End If

            Dim newRun As New DocRun()
            newRun.Text = nome
            newRun.LinkUrl = url
            newRun.IsHyperlink = True
            newRun.ColorHex = "Blu"
            newRun.Underline = True

            SyncLock _elementsLock
                Dim paraCorrente As DocParagraph = GetCurrentParagraph()
                If paraCorrente Is Nothing OrElse _elements Is Nothing OrElse _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return

                Dim testoCorrente As String = paraCorrente.GetText()
                Dim eParagrafoVuoto As Boolean = String.IsNullOrEmpty(testoCorrente)

                If eParagrafoVuoto Then
                    paraCorrente.Runs.Clear()
                    paraCorrente.Runs.Add(newRun)
                    paraCorrente.CachedHeight = -1
                    _caretCharIndex = nome.Length

                    Dim serveParagrafoVuotoDopo As Boolean = True
                    If _caretIndex + 1 < _elements.Count Then
                        Dim elementoDopo As DocElement = _elements(_caretIndex + 1)
                        If TypeOf elementoDopo Is DocParagraph AndAlso String.IsNullOrEmpty(DirectCast(elementoDopo, DocParagraph).GetText()) Then
                            serveParagrafoVuotoDopo = False
                        End If
                    End If
                    If serveParagrafoVuotoDopo Then
                        _elements.Insert(_caretIndex + 1, New DocParagraph())
                    End If
                Else
                    Dim runsPrefisso As List(Of DocRun) = OttieniRunsRitagliatiPrefisso(paraCorrente, _caretCharIndex)
                    Dim runsSuffisso As List(Of DocRun) = OttieniRunsRitagliatiSuffisso(paraCorrente, _caretCharIndex)

                    paraCorrente.Runs.Clear()
                    paraCorrente.Runs.AddRange(runsPrefisso)
                    If paraCorrente.Runs.Count = 0 Then paraCorrente.Runs.Add(New DocRun())
                    paraCorrente.CachedHeight = -1

                    Dim paragrafoLink As New DocParagraph()
                    paragrafoLink.Runs.Add(newRun)

                    Dim posizioneInserimento As Integer = _caretIndex + 1
                    _elements.Insert(posizioneInserimento, paragrafoLink)
                    _elements.Insert(posizioneInserimento + 1, New DocParagraph())

                    If runsSuffisso.Count > 0 Then
                        Dim paragrafoSuccessivo As New DocParagraph()
                        paragrafoSuccessivo.Runs.AddRange(runsSuffisso)
                        _elements.Insert(posizioneInserimento + 2, paragrafoSuccessivo)
                    End If

                    _caretIndex = posizioneInserimento
                    _caretCharIndex = nome.Length
                End If

                _selectionActive = False
            End SyncLock

            _isModified = True
            TriggerLayout()
            UpdateCaretAndNotify(True)
            MostraMessaggioStato("Link inserito")
        Catch ex As Exception
            logger.ScriviLog("InserisciLink: " & ex.Message)
        End Try
    End Sub

    Public Sub InserisciImmagine(percorso As String, didascalia As String)
        Try
            If SolaLettura Then Return
            If Not CaricaImmaginiAttivo Then
                Return
            End If
            Dim newRun As New DocRun()
            newRun.IsImage = True
            newRun.RawContent = percorso

            Dim nomeImm As String = "Immagine"
            If Not String.IsNullOrWhiteSpace(didascalia) Then
                nomeImm &= " " & didascalia
            End If
            newRun.Text = nomeImm

            InsertRuns(New List(Of DocRun) From {newRun})

            _isModified = True
            TriggerLayout()
            UpdateCaretAndNotify(True)
            MostraMessaggioStato(nomeImm & " inserita")
        Catch ex As Exception
            logger.ScriviLog("InserisciImmagine: " & ex.Message)
        End Try
    End Sub

    Public Sub VaiABookmark(bkmk As String)
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If p.BookmarkId = bkmk Then

                            _caretIndex = i
                            _caretCharIndex = 0
                            _selectionActive = False

                            TriggerLayout()
                            ScrollToCaret()
                            UpdateCaretAndNotify(False)
                            Return

                        End If
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("VaiABookmark: " & ex.Message)
        End Try
    End Sub


    Public Sub VaiARiga(numero As Integer)
        Try
            Dim conteggio As Integer = 1
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1

                    If TypeOf _elements(i) Is DocParagraph Then
                        If conteggio = numero Then
                            _caretIndex = i

                            _caretCharIndex = 0
                            _selectionActive = False
                            TriggerLayout()
                            ScrollToCaret()

                            UpdateCaretAndNotify(False)
                            MostraMessaggioStato("Riga " & numero)
                            Return
                        End If
                        conteggio += 1

                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("VaiARiga: " & ex.Message)
        End Try
    End Sub

    Public Sub VaiAPagina(numero As Integer)
        Try

            Dim pageCount As Integer = 1
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsPageBreak Then

                        pageCount += 1
                    End If
                    If pageCount = numero Then
                        _caretIndex = i + 1

                        If _caretIndex >= _elements.Count Then
                            _caretIndex = Math.Max(0, _elements.Count - 1)
                        End If

                        _caretCharIndex = 0
                        _selectionActive = False
                        TriggerLayout()
                        ScrollToCaret()

                        UpdateCaretAndNotify(False)
                        MostraMessaggioStato("Pagina " & numero)

                        Return
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("VaiAPagina: " & ex.Message)
        End Try
    End Sub

    Public Sub StatisticheDocumento()

        Try
            Dim parole As Integer = 0
            Dim caratteri As Integer = 0
            Dim paragrafi As Integer = 0
            Dim righe As Integer = 0
            SyncLock _elementsLock

                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            paragrafi += 1

                            righe += 1
                            Dim txt As String = DirectCast(el, DocParagraph).GetText()
                            caratteri += txt.Length

                            parole += txt.Split(New Char() {" "c, vbCr, vbLf, vbTab}, StringSplitOptions.RemoveEmptyEntries).Length
                        ElseIf TypeOf el Is DocTable Then
                            Dim t As DocTable = DirectCast(el, DocTable)

                            For Each r In t.Rows
                                For Each c In r.Cells
                                    For Each p In c.Paragraphs

                                        paragrafi += 1
                                        righe += 1

                                        Dim txt As String = p.GetText()
                                        caratteri += txt.Length

                                        parole += txt.Split(New Char() {" "c, vbCr, vbLf, vbTab}, StringSplitOptions.RemoveEmptyEntries).Length
                                    Next
                                Next

                            Next
                        End If
                    Next
                End If
            End SyncLock
            Dim msg As String = parole & " parole, " & caratteri & " caratteri, " & paragrafi & " paragrafi, " & righe & " righe."
            MostraMessaggioStato(msg)
        Catch ex As Exception
            logger.ScriviLog("StatisticheDocumento: " & ex.Message)
        End Try
    End Sub


    Public Function EsisteIndice() As Boolean
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _elements.Count > 0 AndAlso TypeOf _elements(0) Is DocParagraph Then
                    Return DirectCast(_elements(0), DocParagraph).IsIndexContainer
                End If

            End SyncLock
            Return False
        Catch ex As Exception
            logger.ScriviLog("EsisteIndice: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub AggiungiAIndice()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then
                Return
            End If

            Dim text As String = para.GetText().Trim()
            If String.IsNullOrEmpty(text) Then

                Return
            End If

            Dim eGiaPresente As Boolean = False
            If Not String.IsNullOrEmpty(para.BookmarkId) Then
                SyncLock _elementsLock
                    If _elements IsNot Nothing Then
                        For Each elIdx In _elements
                            If TypeOf elIdx Is DocParagraph AndAlso DirectCast(elIdx, DocParagraph).IsIndexContainer Then
                                For Each r In DirectCast(elIdx, DocParagraph).Runs
                                    If r.LinkUrl = para.BookmarkId Then
                                        eGiaPresente = True
                                        Exit For
                                    End If
                                Next
                            End If
                        Next
                    End If
                End SyncLock
            End If

            If eGiaPresente Then
                RimuoviVoceDaIndice(para.BookmarkId)
                Return
            End If

            If String.IsNullOrEmpty(para.BookmarkId) Then
                para.BookmarkId = "IND_" & Guid.NewGuid().ToString("N").Substring(0, 8)
            End If

            AggiungiAIndiceLogico(text, para.BookmarkId, 0, True)

            _isModified = True
            TriggerLayout()

            MostraMessaggioStato("Aggiunto all'indice")
        Catch ex As Exception
            logger.ScriviLog("AggiungiAIndice: " & ex.Message)
        End Try
    End Sub

    Public Function EVoceCorrenteGiaIndicizzata() As Boolean
        Try
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing OrElse String.IsNullOrEmpty(para.BookmarkId) Then Return False
            Dim trovato As Boolean = False
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each elIdx In _elements
                        If TypeOf elIdx Is DocParagraph AndAlso DirectCast(elIdx, DocParagraph).IsIndexContainer Then
                            For Each r In DirectCast(elIdx, DocParagraph).Runs
                                If r.LinkUrl = para.BookmarkId Then
                                    trovato = True
                                    Exit For
                                End If
                            Next
                        End If
                    Next
                End If
            End SyncLock
            Return trovato
        Catch ex As Exception
            logger.ScriviLog("EVoceCorrenteGiaIndicizzata: " & ex.Message)
            Return False
        End Try
    End Function


    Private Sub AggiungiAIndiceLogico(testo As String, linkId As String, Optional livello As Integer = 0, Optional collegabile As Boolean = True)
        Try
            If SolaLettura Then Return
            Dim indexEndIdx As Integer = -1

            Dim hasTitle As Boolean = False
            Dim linkGiaPresente As Boolean = False

            If _elements IsNot Nothing AndAlso _elements.Count > 0 AndAlso TypeOf _elements(0) Is DocParagraph AndAlso DirectCast(_elements(0), DocParagraph).IsIndexContainer Then
                hasTitle = True
                For i As Integer = 0 To _elements.Count - 1

                    If TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsIndexContainer Then
                        indexEndIdx = i
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)

                        For Each r In p.Runs
                            If r.LinkUrl = linkId Then
                                linkGiaPresente = True

                            End If
                        Next
                    Else
                        Exit For
                    End If

                Next
            End If

            If linkGiaPresente Then Return

            If Not hasTitle AndAlso _elements IsNot Nothing Then
                Dim titlePara As New DocParagraph()
                titlePara.IsIndexContainer = True

                titlePara.Runs.Add(New DocRun() With {.Text = "INDICE:", .Bold = True})
                _elements.Insert(0, titlePara)
                _caretIndex += 1
                indexEndIdx = 0
            End If

            Dim newEntryPara As New DocParagraph()
            newEntryPara.IsIndexContainer = True
            newEntryPara.IndentLevel = Math.Max(0, livello)
            Dim linkRun As New DocRun()
            linkRun.Text = testo
            If collegabile Then
                linkRun.LinkUrl = linkId
                linkRun.IsHyperlink = True
                linkRun.Underline = True
                linkRun.ColorHex = "Blu"
            Else
                linkRun.Bold = True
            End If
            newEntryPara.Runs.Add(linkRun)
            If _elements IsNot Nothing AndAlso indexEndIdx + 1 <= _elements.Count Then
                _elements.Insert(indexEndIdx + 1, newEntryPara)
            End If
            If _caretIndex > indexEndIdx Then
                _caretIndex += 1
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiungiAIndiceLogico: " & ex.Message)
        End Try
    End Sub

    Private Function FirmaTabella(tbl As DocTable) As String
        Try
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            For Each riga As DocTableRow In tbl.Rows
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                sb.Append("<R>")
                For Each cella As DocTableCell In riga.Cells
                    If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                    sb.Append("<C>")
                    For Each p As DocParagraph In cella.Paragraphs
                        If p Is Nothing Then Continue For
                        If p.Runs IsNot Nothing Then
                            For Each r As DocRun In p.Runs
                                If r Is Nothing Then Continue For
                                If r.IsImage Then
                                    sb.Append("<IMG>")
                                ElseIf ERunMarcatoreNota(r) Then
                                    Continue For
                                Else
                                    sb.Append(If(r.Text, ""))
                                    If Not String.IsNullOrEmpty(r.LinkUrl) Then sb.Append("<L:").Append(r.LinkUrl).Append(">")
                                End If
                            Next
                        End If
                        sb.Append("<P>")
                    Next
                Next
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("FirmaTabella: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function ERunMarcatoreNota(r As DocRun) As Boolean
        Try
            If r Is Nothing OrElse String.IsNullOrEmpty(r.LinkUrl) Then Return False
            Return r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")
        Catch ex As Exception
            logger.ScriviLog("ERunMarcatoreNota: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function RicchezzaTabella(tbl As DocTable) As Integer
        Try
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return 0
            Dim punteggio As Integer = 0
            For Each riga As DocTableRow In tbl.Rows
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                For Each cella As DocTableCell In riga.Cells
                    If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                    For Each p As DocParagraph In cella.Paragraphs
                        If p Is Nothing OrElse p.Runs Is Nothing Then Continue For
                        For Each r As DocRun In p.Runs
                            If ERunMarcatoreNota(r) Then punteggio += 1
                        Next
                    Next
                Next
            Next
            Return punteggio
        Catch ex As Exception
            logger.ScriviLog("RicchezzaTabella: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function IndiciTabelleDuplicate() As List(Of Integer)
        Dim daRimuovere As New List(Of Integer)()
        Try
            If _elements Is Nothing Then Return daRimuovere
            Dim gruppi As New Dictionary(Of String, List(Of Integer))()

            For i As Integer = 0 To _elements.Count - 1
                Dim tbl As DocTable = TryCast(_elements(i), DocTable)
                If tbl Is Nothing Then Continue For
                Dim firma As String = FirmaTabella(tbl)
                If String.IsNullOrEmpty(firma) Then Continue For
                If Not gruppi.ContainsKey(firma) Then gruppi.Add(firma, New List(Of Integer)())
                gruppi(firma).Add(i)
            Next

            For Each coppia As KeyValuePair(Of String, List(Of Integer)) In gruppi
                Dim indici As List(Of Integer) = coppia.Value
                If indici Is Nothing OrElse indici.Count < 2 Then Continue For

                Dim migliore As Integer = indici(0)
                Dim miglioreRicchezza As Integer = RicchezzaTabella(TryCast(_elements(migliore), DocTable))
                For Each idx As Integer In indici
                    Dim ricchezza As Integer = RicchezzaTabella(TryCast(_elements(idx), DocTable))
                    If ricchezza > miglioreRicchezza Then
                        migliore = idx
                        miglioreRicchezza = ricchezza
                    End If
                Next

                For Each idx As Integer In indici
                    If idx <> migliore Then daRimuovere.Add(idx)
                Next
            Next

            daRimuovere.Sort()
        Catch ex As Exception
            logger.ScriviLog("IndiciTabelleDuplicate: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return daRimuovere
    End Function

    Public Function ContaTabelleDuplicate() As Integer
        Try
            SyncLock _elementsLock
                Return IndiciTabelleDuplicate().Count
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("ContaTabelleDuplicate: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Public Function RimuoviTabelleDuplicate() As Integer
        Try
            If SolaLettura Then Return 0
            Dim rimosse As Integer = 0
            SaveUndoState()

            SyncLock _elementsLock
                If _elements Is Nothing Then Return 0
                Dim daRimuovere As List(Of Integer) = IndiciTabelleDuplicate()
                If daRimuovere Is Nothing OrElse daRimuovere.Count = 0 Then Return 0

                For k As Integer = daRimuovere.Count - 1 To 0 Step -1
                    Dim idx As Integer = daRimuovere(k)
                    If idx >= 0 AndAlso idx < _elements.Count Then
                        _elements.RemoveAt(idx)
                        rimosse += 1
                    End If
                Next

                If _elements.Count = 0 Then _elements.Add(New DocParagraph())
                If _caretIndex >= _elements.Count Then _caretIndex = Math.Max(0, _elements.Count - 1)
                If _caretIndex < 0 Then _caretIndex = 0
                _caretCharIndex = 0
                _tblRow = -1
                _tblCol = -1
                _tblPara = -1
                _selectionActive = False
            End SyncLock

            If rimosse > 0 Then
                _isModified = True
                TriggerLayout()
                If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
                ScrollToCaret()
                UpdateCaretAndNotify(True)
            End If

            Return rimosse
        Catch ex As Exception
            logger.ScriviLog("RimuoviTabelleDuplicate: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Public Function CercaTesto(testo As String) As Boolean
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return False
            If String.IsNullOrEmpty(testo) Then Return False

            Dim startIdx = Math.Max(0, _caretIndex)
            Dim startChar = _caretCharIndex + 1

            SyncLock _elementsLock
                If _elements Is Nothing OrElse _elements.Count = 0 Then Return False

                For i As Integer = startIdx To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p = DirectCast(_elements(i), DocParagraph)
                        Dim txt = p.GetText()

                        Dim match = txt.IndexOf(testo, If(i = startIdx AndAlso startChar <= txt.Length, startChar, 0), StringComparison.OrdinalIgnoreCase)
                        If match >= 0 Then
                            _caretIndex = i
                            _caretCharIndex = match
                            _selectionActive = True
                            _selAnchorIndex = i
                            _selAnchorCharIndex = match + testo.Length
                            _selAnchorSubIndex = 0
                            TriggerLayout()
                            ScrollToCaret()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            UpdateCaretAndNotify(False)
                            Return True
                        End If
                    ElseIf TypeOf _elements(i) Is DocTable Then
                        Dim tbl = DirectCast(_elements(i), DocTable)
                        Dim rMin As Integer = 0, cMin As Integer = 0, pMin As Integer = 0, chMin As Integer = 0
                        If i = startIdx Then
                            rMin = _tblRow
                            cMin = _tblCol
                            pMin = _tblPara
                            chMin = _caretCharIndex + 1
                        End If
                        If CercaInTabella(tbl, i, testo, rMin, cMin, pMin, chMin) Then Return True
                    End If
                Next

                For i As Integer = 0 To startIdx
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p = DirectCast(_elements(i), DocParagraph)
                        Dim txt = p.GetText()

                        Dim limiteColonna As Integer = If(i = startIdx, System.Math.Min(startChar, txt.Length), txt.Length)
                        Dim match = txt.IndexOf(testo, 0, StringComparison.OrdinalIgnoreCase)
                        If match >= 0 AndAlso (i < startIdx OrElse match < limiteColonna) Then
                            _caretIndex = i
                            _caretCharIndex = match
                            _selectionActive = True
                            _selAnchorIndex = i
                            _selAnchorCharIndex = match + testo.Length
                            _selAnchorSubIndex = 0
                            TriggerLayout()
                            ScrollToCaret()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            UpdateCaretAndNotify(False)
                            Return True
                        End If
                    ElseIf TypeOf _elements(i) Is DocTable Then
                        Dim tbl = DirectCast(_elements(i), DocTable)
                        If CercaInTabella(tbl, i, testo, 0, 0, 0, 0) Then Return True
                    End If
                Next
            End SyncLock

            Return False
        Catch ex As Exception
            logger.ScriviLog("CercaTesto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CercaInTabella(tbl As DocTable, tblIdx As Integer, testo As String, rigaMin As Integer, colMin As Integer, paraMin As Integer, charMin As Integer) As Boolean
        Try
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return False

            For r As Integer = Math.Max(0, rigaMin) To tbl.Rows.Count - 1
                Dim riga As DocTableRow = tbl.Rows(r)
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For

                Dim colStartEffettivo As Integer = If(r = rigaMin, Math.Max(0, colMin), 0)
                For c As Integer = colStartEffettivo To riga.Cells.Count - 1
                    Dim cella As DocTableCell = riga.Cells(c)
                    If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For

                    Dim paraStartEffettivo As Integer = If(r = rigaMin AndAlso c = colMin, Math.Max(0, paraMin), 0)
                    For pIdx As Integer = paraStartEffettivo To cella.Paragraphs.Count - 1
                        Dim para As DocParagraph = cella.Paragraphs(pIdx)
                        If para Is Nothing Then Continue For

                        Dim txt As String = para.GetText()
                        Dim charStartEffettivo As Integer = If(r = rigaMin AndAlso c = colMin AndAlso pIdx = paraMin, Math.Max(0, charMin), 0)
                        If charStartEffettivo > txt.Length Then Continue For

                        Dim match As Integer = txt.IndexOf(testo, charStartEffettivo, StringComparison.OrdinalIgnoreCase)
                        If match >= 0 Then
                            _caretIndex = tblIdx
                            _tblRow = r
                            _tblCol = c
                            _tblPara = pIdx
                            _caretCharIndex = match
                            _selectionActive = True
                            _selAnchorIndex = tblIdx
                            _selAnchorSubIndex = GetPackedTableIndex()
                            _selAnchorCharIndex = match + testo.Length
                            TriggerLayout()
                            ScrollToCaret()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            UpdateCaretAndNotify(False)
                            Return True
                        End If
                    Next
                Next
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog("CercaInTabella: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function


    Public Sub SostituisciTesto(cerca As String, sostituisci As String)
        Try
            If SolaLettura Then Return
            If String.IsNullOrEmpty(cerca) Then Return
            Dim trovato As Boolean = False
            Dim sostituzioneSicura As String = If(sostituisci, "").Replace("$", "$$")
            Dim regexCerca As New Regex(Regex.Escape(cerca), RegexOptions.IgnoreCase)

            SyncLock _elementsLock
                If _elements Is Nothing Then Return

                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p = DirectCast(_elements(i), DocParagraph)
                        If SostituisciTestoInParagrafo(p, regexCerca, sostituzioneSicura) Then
                            trovato = True
                        End If
                    ElseIf TypeOf _elements(i) Is DocTable Then
                        Dim tbl = DirectCast(_elements(i), DocTable)
                        If tbl.Rows IsNot Nothing Then
                            For Each riga As DocTableRow In tbl.Rows
                                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                                For Each cella As DocTableCell In riga.Cells
                                    If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                                    For Each paraCella As DocParagraph In cella.Paragraphs
                                        If SostituisciTestoInParagrafo(paraCella, regexCerca, sostituzioneSicura) Then
                                            trovato = True
                                        End If
                                    Next
                                Next
                            Next
                        End If
                    End If
                Next
            End SyncLock
            If trovato Then
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)

                MostraMessaggioStato("Sostituzione completata mantenendo la formattazione.")
            Else
                MostraMessaggioStato("Nessuna corrispondenza trovata per " & cerca)
            End If
        Catch ex As Exception
            logger.ScriviLog("SostituisciTesto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

    End Sub

    Private Function SostituisciTestoInParagrafo(p As DocParagraph, regexCerca As Regex, sostituzioneSicura As String) As Boolean
        Try
            If p Is Nothing OrElse p.Runs Is Nothing OrElse p.Runs.Count = 0 Then Return False

            Dim testoCompleto As String = p.GetText()
            If String.IsNullOrEmpty(testoCompleto) Then Return False

            Dim matches As MatchCollection = regexCerca.Matches(testoCompleto)
            If matches Is Nothing OrElse matches.Count = 0 Then Return False

            Dim nuoviRuns As New List(Of DocRun)()
            Dim ri As Integer = 0
            Dim cursoreRun As Integer = 0
            Dim offsetGlobale As Integer = 0
            Dim indiceMatch As Integer = 0

            While ri < p.Runs.Count
                Dim run As DocRun = p.Runs(ri)
                If run Is Nothing Then
                    ri += 1
                    Continue While
                End If
                If run.IsImage Then
                    nuoviRuns.Add(run)
                    ri += 1
                    Continue While
                End If

                Dim testoRun As String = If(run.Text, "")

                If cursoreRun >= testoRun.Length Then
                    offsetGlobale += testoRun.Length
                    ri += 1
                    cursoreRun = 0
                    Continue While
                End If

                Dim posizioneGlobale As Integer = offsetGlobale + cursoreRun

                If indiceMatch < matches.Count AndAlso matches(indiceMatch).Index = posizioneGlobale Then
                    Dim m As Match = matches(indiceMatch)
                    Dim testoSostituito As String = m.Result(sostituzioneSicura)
                    If testoSostituito.Length > 0 Then
                        Dim nuovoRun As DocRun = run.Clone()
                        nuovoRun.Text = testoSostituito
                        nuoviRuns.Add(nuovoRun)
                    End If
                    indiceMatch += 1

                    Dim daSaltare As Integer = m.Length
                    While daSaltare > 0 AndAlso ri < p.Runs.Count
                        Dim runCorrente As DocRun = p.Runs(ri)
                        If runCorrente Is Nothing Then
                            ri += 1
                            Continue While
                        End If
                        If runCorrente.IsImage Then
                            nuoviRuns.Add(runCorrente)
                            ri += 1
                            Continue While
                        End If
                        Dim testoCorrente As String = If(runCorrente.Text, "")
                        Dim disponibili As Integer = testoCorrente.Length - cursoreRun
                        If disponibili <= 0 Then
                            offsetGlobale += testoCorrente.Length
                            ri += 1
                            cursoreRun = 0
                            Continue While
                        End If
                        If daSaltare < disponibili Then
                            cursoreRun += daSaltare
                            daSaltare = 0
                        Else
                            daSaltare -= disponibili
                            offsetGlobale += testoCorrente.Length
                            ri += 1
                            cursoreRun = 0
                        End If
                    End While
                    Continue While
                End If

                Dim fineSegmento As Integer = testoRun.Length
                If indiceMatch < matches.Count AndAlso matches(indiceMatch).Index > posizioneGlobale AndAlso matches(indiceMatch).Index < offsetGlobale + testoRun.Length Then
                    fineSegmento = matches(indiceMatch).Index - offsetGlobale
                End If

                Dim segmentoTesto As String = testoRun.Substring(cursoreRun, fineSegmento - cursoreRun)
                If segmentoTesto.Length > 0 Then
                    Dim runSegmento As DocRun = run.Clone()
                    runSegmento.Text = segmentoTesto
                    nuoviRuns.Add(runSegmento)
                End If
                cursoreRun = fineSegmento

                If cursoreRun >= testoRun.Length Then
                    offsetGlobale += testoRun.Length
                    ri += 1
                    cursoreRun = 0
                End If
            End While

            If nuoviRuns.Count = 0 Then
                nuoviRuns.Add(New DocRun())
            End If

            p.Runs = nuoviRuns
            p.CachedHeight = -1
            Return True
        Catch ex As Exception
            logger.ScriviLog("SostituisciTestoInParagrafo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function



    Public Sub CambiaFormatoPagina(tipoPagina As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If SolaLettura Then Return

            If _documentState Is Nothing Then Return
            If _documentState.PageSettings Is Nothing Then
                _documentState.PageSettings = New Dictionary(Of String, Integer)()
            End If

            WGWordUtils.ApplicaTipoPagina(_documentState.PageSettings, tipoPagina)
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            MostraMessaggioStato("Formato pagina impostato su " & tipoPagina & ".")
        Catch ex As Exception
            logger.ScriviLog("CambiaFormatoPagina: " & ex.Message)
        End Try
    End Sub

    Public Sub NuovoDocumento(Optional titolo As String = "Nuovo Documento")
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    _elements.Clear()

                    _elements.Add(New DocParagraph())
                End If
            End SyncLock
            _caretIndex = 0
            _caretCharIndex = 0
            _tblRow = 0
            _tblCol = 0
            _tblPara = 0
            _selectionActive = False
            _vScrollPos = 0
            _isModified = False
            _docTitle = titolo
            _undoStack.Clear()
            _redoStack.Clear()

            If _documentState IsNot Nothing Then
                _documentState.PageSettings = New Dictionary(Of String, Integer)()
                WGWordUtils.ApplicaTipoPagina(_documentState.PageSettings, FormatoPaginaPredefinito)
            End If

            lettura.LeggiTesto(titolo & " Campo di editazione")
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)

        Catch ex As Exception
            logger.ScriviLog("NuovoDocumento: " & ex.Message)
        End Try
    End Sub

    Public Sub LoadDocx(filePath As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return


            If Not File.Exists(filePath) Then
                Return
            End If
            _docTitle = Path.GetFileNameWithoutExtension(filePath)
            Dim t As New System.Threading.Thread(AddressOf LoadDocxInternal)
            t.IsBackground = True
            t.Start(filePath)

        Catch ex As Exception
            logger.ScriviLog("LoadDocx: " & ex.Message)
        End Try
    End Sub

    Public Sub SaveDocx(outputPath As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If String.IsNullOrWhiteSpace(outputPath) Then
                Return

            End If
            Dim t As New System.Threading.Thread(AddressOf SaveDocxInternal)
            t.IsBackground = True
            t.Start(outputPath)
        Catch ex As Exception
            logger.ScriviLog("SaveDocx: " & ex.Message)
        End Try
    End Sub

    Public Function HaTabelle() As Boolean

        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocTable Then

                            Return True
                        End If
                    Next
                End If
            End SyncLock

            Return False
        Catch ex As Exception
            logger.ScriviLog("HaTabelle: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub MostraElencoTabelle()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            If Not HaTabelle() Then
                Return
            End If

            If _menuTabelleCorrente IsNot Nothing Then
                _menuTabelleCorrente.abbandona()
                _menuTabelleCorrente = Nothing
            End If


            _menuTabelleCorrente = New WGMenuLista()
            _menuTabelleCorrente.impostaTitolo("Tabelle")
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = 0 To _elements.Count - 1

                        If TypeOf _elements(i) Is DocTable Then
                            Dim nomeTabella = "Tabella " & i
                            _menuTabelleCorrente.aggiungiElemento(nomeTabella, i.ToString())

                        End If
                    Next
                End If
            End SyncLock
            AddHandler _menuTabelleCorrente.ElementoSelezionato, AddressOf MenuTabelle_Selezionato
            AddHandler _menuTabelleCorrente.escPremuto, AddressOf MenuTabelle_EscPremuto
            _menuTabelleCorrente.avvia()

        Catch ex As Exception
            logger.ScriviLog("MostraElencoTabelle: " & ex.Message)
        End Try
    End Sub

    Public Sub VaiATabella(index As Integer)
        Try
            Dim tbl As DocTable = Nothing
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso index >= 0 AndAlso index < _elements.Count AndAlso TypeOf _elements(index) Is DocTable Then

                    tbl = DirectCast(_elements(index), DocTable)
                    _caretIndex = index
                    _tblRow = 0
                    _tblCol = 0
                    _tblPara = 0

                    _caretCharIndex = 0
                    _selectionActive = False
                    TriggerLayout()
                    ScrollToCaret()
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                End If
            End SyncLock

            If tbl IsNot Nothing Then
                If _gestoreMovimento IsNot Nothing Then
                    _gestoreMovimento.AnnunciaPosizioneCorrenteTabella(tbl)
                Else
                    UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("VaiATabella: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiRigaTabella()
        Try

            If SolaLettura Then Return
            Dim currentEl As DocElement = Nothing
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    currentEl = _elements(_caretIndex)

                End If
                If currentEl IsNot Nothing AndAlso TypeOf currentEl Is DocTable Then
                    Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                    If tbl.Rows.Count > 0 Then

                        Dim newRow As New DocTableRow()
                        Dim cols As Integer = tbl.Rows(0).Cells.Count
                        For c As Integer = 0 To cols - 1
                            Dim newCell As New DocTableCell()

                            newCell.Paragraphs.Add(New DocParagraph())
                            newRow.Cells.Add(newCell)
                        Next

                        Dim indexToInsert = Math.Max(0, Math.Min(tbl.Rows.Count, _tblRow + 1))
                        tbl.Rows.Insert(indexToInsert, newRow)
                        tbl.CachedHeight = -1
                        _isModified = True

                        TriggerLayout()
                        InvalidateRect(Me.Handle, IntPtr.Zero, False)
                        MostraMessaggioStato("Riga aggiunta")
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("AggiungiRigaTabella: " & ex.Message)
        End Try

    End Sub

    Public Function OttieniSelezioneRigheTabella() As Tuple(Of Integer, Integer)
        If _tblSelAttiva AndAlso _tblSelModalita = "RIGHE" AndAlso _tblSelAncora >= 0 Then
            Dim inizio As Integer = System.Math.Min(_tblSelAncora, _tblRow)
            Dim fine As Integer = System.Math.Max(_tblSelAncora, _tblRow)
            Return Tuple.Create(inizio, fine - inizio + 1)
        End If
        Return Nothing
    End Function

    Public Function OttieniSelezioneColonneTabella() As Tuple(Of Integer, Integer)
        If _tblSelAttiva AndAlso _tblSelModalita = "COLONNE" AndAlso _tblSelAncora >= 0 Then
            Dim inizio As Integer = System.Math.Min(_tblSelAncora, _tblCol)
            Dim fine As Integer = System.Math.Max(_tblSelAncora, _tblCol)
            Return Tuple.Create(inizio, fine - inizio + 1)
        End If
        Return Nothing
    End Function

    Public Sub AzzeraSelezioneTabella()
        _tblSelAttiva = False
        _tblSelModalita = ""
        _tblSelAncora = -1
    End Sub

    Public Sub RimuoviRigheTabellaDaPosizione(rigaIniziale As Integer, quantita As Integer)
        Try
            If SolaLettura OrElse quantita <= 0 Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim currentEl As DocElement = _elements(_caretIndex)
                    If currentEl IsNot Nothing AndAlso TypeOf currentEl Is DocTable Then
                        Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                        rigaIniziale = System.Math.Max(0, System.Math.Min(rigaIniziale, tbl.Rows.Count - 1))
                        Dim daRimuovere As Integer = System.Math.Min(quantita, tbl.Rows.Count - rigaIniziale)
                        If daRimuovere > 0 AndAlso tbl.Rows.Count - daRimuovere >= 1 Then
                            For i As Integer = 1 To daRimuovere
                                tbl.Rows.RemoveAt(rigaIniziale)
                            Next
                            _tblRow = System.Math.Min(rigaIniziale, tbl.Rows.Count - 1)
                            AzzeraSelezioneTabella()
                            tbl.CachedHeight = -1
                            _isModified = True
                            TriggerLayout()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            MostraMessaggioStato(daRimuovere & If(daRimuovere = 1, " riga rimossa", " righe rimosse"))
                        Else
                            MostraMessaggioStato("Impossibile rimuovere: la tabella deve avere almeno una riga")
                        End If
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviRigheTabellaDaPosizione: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviColonneTabellaDaPosizione(colonnaIniziale As Integer, quantita As Integer)
        Try
            If SolaLettura OrElse quantita <= 0 Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)
                    If el IsNot Nothing AndAlso TypeOf el Is DocTable Then
                        Dim tbl = DirectCast(el, DocTable)
                        If tbl.Rows.Count > 0 Then
                            Dim colonneAttuali As Integer = tbl.Rows(0).Cells.Count
                            colonnaIniziale = System.Math.Max(0, System.Math.Min(colonnaIniziale, colonneAttuali - 1))
                            Dim daRimuovere As Integer = System.Math.Min(quantita, colonneAttuali - colonnaIniziale)
                            If daRimuovere > 0 AndAlso colonneAttuali - daRimuovere >= 1 Then
                                For Each row In tbl.Rows
                                    For i As Integer = 1 To daRimuovere
                                        If colonnaIniziale < row.Cells.Count Then row.Cells.RemoveAt(colonnaIniziale)
                                    Next
                                Next
                                _tblCol = System.Math.Min(colonnaIniziale, tbl.Rows(0).Cells.Count - 1)
                                AzzeraSelezioneTabella()
                                tbl.CachedHeight = -1
                                _isModified = True
                                TriggerLayout()
                                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                                MostraMessaggioStato(daRimuovere & If(daRimuovere = 1, " colonna rimossa", " colonne rimosse"))
                            Else
                                MostraMessaggioStato("Impossibile rimuovere: la tabella deve avere almeno una colonna")
                            End If
                        End If
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviColonneTabellaDaPosizione: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviRigheTabella(quantita As Integer)
        Try
            If SolaLettura OrElse quantita <= 0 Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim currentEl As DocElement = _elements(_caretIndex)
                    If currentEl IsNot Nothing AndAlso TypeOf currentEl Is DocTable Then
                        Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                        If _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count Then
                            Dim daRimuovere As Integer = System.Math.Min(quantita, tbl.Rows.Count - 1)
                            daRimuovere = System.Math.Min(daRimuovere, tbl.Rows.Count - _tblRow)
                            If daRimuovere > 0 AndAlso tbl.Rows.Count - daRimuovere >= 1 Then
                                For i As Integer = 1 To daRimuovere
                                    tbl.Rows.RemoveAt(_tblRow)
                                Next
                                If _tblRow >= tbl.Rows.Count Then _tblRow = tbl.Rows.Count - 1
                                tbl.CachedHeight = -1
                                _isModified = True
                                TriggerLayout()
                                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                                MostraMessaggioStato(daRimuovere & If(daRimuovere = 1, " riga rimossa", " righe rimosse"))
                            Else
                                MostraMessaggioStato("Impossibile rimuovere: la tabella deve avere almeno una riga")
                            End If
                        End If
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviRigheTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviRigaTabella()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim currentEl As DocElement = _elements(_caretIndex)

                    If currentEl IsNot Nothing AndAlso TypeOf currentEl Is DocTable Then
                        Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                        If tbl.Rows.Count > 1 AndAlso _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count Then

                            tbl.Rows.RemoveAt(_tblRow)
                            If _tblRow >= tbl.Rows.Count Then
                                _tblRow = tbl.Rows.Count - 1

                            End If
                            tbl.CachedHeight = -1
                            _isModified = True

                            TriggerLayout()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            MostraMessaggioStato("Riga rimossa")
                        End If
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviRigaTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiColonnaTabella()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)
                    If el IsNot Nothing AndAlso TypeOf el Is DocTable Then
                        Dim tbl = DirectCast(el, DocTable)

                        For Each row In tbl.Rows
                            Dim newCell As New DocTableCell()
                            newCell.Paragraphs.Add(New DocParagraph())

                            row.Cells.Add(newCell)
                        Next
                        tbl.CachedHeight = -1
                        _isModified = True

                        TriggerLayout()
                        InvalidateRect(Me.Handle, IntPtr.Zero, False)
                        MostraMessaggioStato("Colonna aggiunta")
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("AggiungiColonnaTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviColonneTabella(quantita As Integer)
        Try
            If SolaLettura OrElse quantita <= 0 Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)
                    If el IsNot Nothing AndAlso TypeOf el Is DocTable Then
                        Dim tbl = DirectCast(el, DocTable)
                        If tbl.Rows.Count > 0 AndAlso _tblCol >= 0 AndAlso _tblCol < tbl.Rows(0).Cells.Count Then
                            Dim colonneAttuali As Integer = tbl.Rows(0).Cells.Count
                            Dim daRimuovere As Integer = System.Math.Min(quantita, colonneAttuali - 1)
                            daRimuovere = System.Math.Min(daRimuovere, colonneAttuali - _tblCol)
                            If daRimuovere > 0 AndAlso colonneAttuali - daRimuovere >= 1 Then
                                For Each row In tbl.Rows
                                    For i As Integer = 1 To daRimuovere
                                        If _tblCol < row.Cells.Count Then row.Cells.RemoveAt(_tblCol)
                                    Next
                                Next
                                If _tblCol >= tbl.Rows(0).Cells.Count Then _tblCol = System.Math.Max(0, tbl.Rows(0).Cells.Count - 1)
                                tbl.CachedHeight = -1
                                _isModified = True
                                TriggerLayout()
                                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                                MostraMessaggioStato(daRimuovere & If(daRimuovere = 1, " colonna rimossa", " colonne rimosse"))
                            Else
                                MostraMessaggioStato("Impossibile rimuovere: la tabella deve avere almeno una colonna")
                            End If
                        End If
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviColonneTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub RimuoviColonnaTabella()
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)

                    If el IsNot Nothing AndAlso TypeOf el Is DocTable Then
                        Dim tbl = DirectCast(el, DocTable)
                        If tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells.Count > 1 AndAlso _tblCol >= 0 Then

                            For Each row In tbl.Rows
                                If _tblCol < row.Cells.Count Then
                                    row.Cells.RemoveAt(_tblCol)

                                End If
                            Next
                            If _tblCol >= tbl.Rows(0).Cells.Count Then

                                _tblCol = Math.Max(0, tbl.Rows(0).Cells.Count - 1)
                            End If
                            tbl.CachedHeight = -1

                            _isModified = True
                            TriggerLayout()
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                            MostraMessaggioStato("Colonna rimossa")
                        End If
                    End If

                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("RimuoviColonnaTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub InserisciTabella(righe As Integer, colonne As Integer)
        Try
            If SolaLettura Then Return

            If righe <= 0 OrElse colonne <= 0 Then
                Return
            End If
            Dim newTbl As New DocTable()
            For r As Integer = 0 To righe - 1

                Dim newRow As New DocTableRow()
                For c As Integer = 0 To colonne - 1
                    Dim newCell As New DocTableCell()
                    newCell.Paragraphs.Add(New DocParagraph())
                    newRow.Cells.Add(newCell)

                Next
                newTbl.Rows.Add(newRow)
            Next

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    Dim indicePosizionamento As Integer
                    If _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then

                        Dim servePrimaParagrafoVuoto As Boolean = True
                        If _caretIndex = 0 Then
                            servePrimaParagrafoVuoto = False
                        ElseIf _caretIndex - 1 >= 0 Then
                            Dim elementoPrima As DocElement = _elements(_caretIndex - 1)
                            If TypeOf elementoPrima Is DocParagraph AndAlso String.IsNullOrEmpty(DirectCast(elementoPrima, DocParagraph).GetText()) Then
                                servePrimaParagrafoVuoto = False
                            End If
                        End If

                        If servePrimaParagrafoVuoto Then
                            _elements.Insert(_caretIndex, New DocParagraph())
                            _caretIndex += 1
                        End If

                        _elements.Insert(_caretIndex, newTbl)
                        indicePosizionamento = _caretIndex
                    Else
                        _elements.Add(newTbl)
                        indicePosizionamento = _elements.Count - 1
                    End If

                    Dim serveParagrafoDopo As Boolean = True
                    If indicePosizionamento + 1 < _elements.Count Then
                        Dim elementoDopo As DocElement = _elements(indicePosizionamento + 1)
                        If TypeOf elementoDopo Is DocParagraph AndAlso String.IsNullOrEmpty(DirectCast(elementoDopo, DocParagraph).GetText()) Then
                            serveParagrafoDopo = False
                        End If
                    End If
                    If serveParagrafoDopo Then
                        _elements.Insert(indicePosizionamento + 1, New DocParagraph())
                    End If

                    _caretIndex = indicePosizionamento
                    _tblRow = 0
                    _tblCol = 0
                    _tblPara = 0
                    _caretCharIndex = 0
                    _selectionActive = False
                End If
            End SyncLock
            _isModified = True
            SyncronizzaElementiInStato()
            TriggerLayout()
            ScrollToCaret()

            If _gestoreMovimento IsNot Nothing Then
                _gestoreMovimento.AnnunciaPosizioneCorrenteTabella(newTbl)
            Else
                UpdateCaretAndNotify(True)
            End If
        Catch ex As Exception
            logger.ScriviLog("InserisciTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaColoreSfondoCella(coloreHex As String)
        Try
            If SolaLettura Then Return
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count AndAlso TypeOf _elements(_caretIndex) Is DocTable Then
                    Dim tbl = DirectCast(_elements(_caretIndex), DocTable)
                    If _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count AndAlso _tblCol >= 0 AndAlso _tblCol < tbl.Rows(_tblRow).Cells.Count Then
                        Dim cell = tbl.Rows(_tblRow).Cells(_tblCol)
                        cell.ShadingColorHex = coloreHex
                        tbl.CachedHeight = -1

                        _isModified = True
                        TriggerLayout()
                        InvalidateRect(Me.Handle, IntPtr.Zero, False)
                        MostraMessaggioStato("Sfondo cella " & coloreHex)
                    End If
                End If
            End SyncLock

        Catch ex As Exception
            logger.ScriviLog("ImpostaColoreSfondoCella: " & ex.Message)
        End Try
    End Sub

    Public Sub AssestaParagrafi()
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        el.CachedHeight = -1
                    Next
                End If
            End SyncLock
            TriggerLayout()

            If Me.IsHandleCreated Then
                Me.Invoke(New MethodInvoker(AddressOf SafeInvalidate))
            End If
        Catch ex As Exception
            logger.ScriviLog("AssestaParagrafi: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniStatoCursore() As StatoCursore
        Dim stato As New StatoCursore()

        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el As DocElement = _elements(_caretIndex)
                    stato.IndiceElemento = _caretIndex

                    stato.IndiceCarattere = _caretCharIndex
                    Dim testoDaAnalizzare As String = ""
                    Dim isTableNow As Boolean = False

                    If TypeOf el Is DocTable Then

                        isTableNow = True
                        stato.InTabella = True
                        stato.RigaTabella = _tblRow
                        stato.ColonnaTabella = _tblCol

                        stato.ParagrafoCella = _tblPara
                        Dim tbl As DocTable = DirectCast(el, DocTable)
                        If _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count AndAlso _tblCol >= 0 AndAlso _tblCol < tbl.Rows(_tblRow).Cells.Count Then

                            Dim cell As DocTableCell = tbl.Rows(_tblRow).Cells(_tblCol)
                            If _tblPara >= 0 AndAlso _tblPara < cell.Paragraphs.Count Then
                                Dim para As DocParagraph = cell.Paragraphs(_tblPara)

                                testoDaAnalizzare = para.GetText()
                            End If
                        End If

                    ElseIf TypeOf el Is DocParagraph Then
                        stato.InTabella = False
                        Dim para As DocParagraph = DirectCast(el, DocParagraph)
                        testoDaAnalizzare = para.GetText()

                    End If

                    If isTableNow AndAlso Not _lastElementWasTable Then
                        stato.AnnunciaIngressoTabella = True
                    End If


                    _lastElementWasTable = isTableNow

                    stato.TestoContenitore = testoDaAnalizzare

                    If testoDaAnalizzare.Length > 0 AndAlso _caretCharIndex >= 0 AndAlso _caretCharIndex <= testoDaAnalizzare.Length Then

                        Dim startI As Integer = Math.Min(_caretCharIndex, testoDaAnalizzare.Length - 1)
                        If startI < 0 Then
                            startI = 0
                        End If

                        Dim i As Integer = startI
                        While i > 0 AndAlso Not Char.IsWhiteSpace(testoDaAnalizzare(i - 1))
                            i -= 1

                        End While
                        Dim j As Integer = startI
                        While j < testoDaAnalizzare.Length AndAlso Not Char.IsWhiteSpace(testoDaAnalizzare(j))
                            j += 1

                        End While
                        If j > i Then
                            stato.ParolaCorrente = testoDaAnalizzare.Substring(i, j - i)

                        End If
                    End If

                    If _selectionActive Then
                        stato.SelezioneAttiva = True

                        stato.InizioSelezioneIndice = _selAnchorIndex
                        stato.InizioSelezioneCarattere = _selAnchorCharIndex
                        stato.FineSelezioneIndice = _caretIndex
                        stato.FineSelezioneCarattere = _caretCharIndex

                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("OttieniStatoCursore: " & ex.Message)
        End Try
        Return stato
    End Function

    Public Sub ImpostaIntestazionePiePagina(header As String, footer As String)
        Try

            If SolaLettura Then Return
            HeaderText = header
            FooterText = footer
            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
        Catch ex As Exception
            logger.ScriviLog("ImpostaIntestazionePiePagina: " & ex.Message)
        End Try
    End Sub

End Class