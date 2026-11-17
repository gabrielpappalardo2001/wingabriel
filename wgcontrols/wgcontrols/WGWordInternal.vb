Imports System.Windows.Forms
Imports System.Drawing
Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Diagnostics

' Funzioni interne per la modifica del testo, la gestione degli appunti, la cronologia delle azioni e lo scorrimento.
Partial Public Class WGWord

    Private _documentState As WGDocumentState = New WGDocumentState()
    Private _pendingDocumentState As WGDocumentState = Nothing

    Private Sub AzioneRecuperoApri(sender As Object, e As EventArgs)
        Try
            Dim scelti As List(Of String) = Nothing
            If _menuRecupero IsNot Nothing Then
                scelti = _menuRecupero.ottieniElementi()
            End If
   
            If scelti IsNot Nothing AndAlso scelti.Count > 0 Then
                Dim idDaAprire As String = scelti(0)
                Dim xmlStr As String = _sqlite.OttieniContenuto(idDaAprire)
                If Not String.IsNullOrEmpty(xmlStr) Then
                   
                    Dim serializer As New System.Xml.Serialization.XmlSerializer(GetType(List(Of DocElement)))
                    Dim sr As New IO.StringReader(xmlStr)
                    Dim elements = DirectCast(serializer.Deserialize(sr), List(Of DocElement))
                    If elements IsNot Nothing Then
                        SyncLock _elementsLock
                            _elements = elements
                            _documentState.Elements = _elements
                        End SyncLock
                        _caretIndex = 0
                        _caretCharIndex = 0
                        _tblRow = 0
                        _tblCol = 0
                        _tblPara = 0
                        _vScrollPos = 0
                        _docTitle = idDaAprire
                        _isModified = True
     
                        TriggerLayout()
                        If Me.IsHandleCreated Then
                            InvalidateRect(Me.Handle, IntPtr.Zero, False)
                        End If
                        UpdateCaretAndNotify(False)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Testo recuperato e caricato.")
                        msg.ShowDialog()
                    End If
                End If
            End If
            If _menuRecupero IsNot Nothing Then
                _menuRecupero.abbandona()
                _menuRecupero = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("AzioneRecuperoApri Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AzioneRecuperoElimina(sender As Object, e As EventArgs)
        Try
            Dim scelti As List(Of String) = Nothing
            If _menuRecupero IsNot Nothing Then
                scelti = _menuRecupero.ottieniElementi()
            End If
            If scelti IsNot Nothing AndAlso scelti.Count > 0 Then
                For Each id In scelti
                    If Not String.IsNullOrEmpty(id) Then
                        _sqlite.Elimina(id)
                    End If
                Next
                Dim msg As New WGMessaggi()
                msg.Messaggio("Elementi eliminati.")
                msg.ShowDialog()
            End If
    
            If _menuRecupero IsNot Nothing Then
                _menuRecupero.abbandona()
                _menuRecupero = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("AzioneRecuperoElimina Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub UpdateCaretAndNotify(Optional textChanged As Boolean = False)
        Try
            Dim para As DocParagraph = GetCurrentParagraph()

            InvalidateRect(Me.Handle, IntPtr.Zero, False)

            If para Is Nothing Then Return

            If textChanged Then
                _isModified = True
                SyncronizzaElementiInStato()
            End If
        Catch ex As Exception
            logger.ScriviLog("UpdateCaretAndNotify Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in UpdateCaretAndNotify: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub SyncronizzaElementiInStato()
        Try
            SyncLock _elementsLock
                If _documentState IsNot Nothing AndAlso _elements IsNot Nothing Then
                    _documentState.Elements = _elements
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("SyncronizzaElementiInStato Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Function GetLinkAtCaret() As String
        Try
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing AndAlso para.Runs IsNot Nothing Then
                Dim currentPos As Integer = 0
                For Each r As DocRun In para.Runs
                    If r.Text IsNot Nothing AndAlso _caretCharIndex >= currentPos AndAlso _caretCharIndex <= currentPos + r.Text.Length Then
                        Return r.LinkUrl
                    End If
                    If r.Text IsNot Nothing Then
                        currentPos += r.Text.Length
                    End If
                Next
            End If
            Return ""
        Catch ex As Exception
            logger.ScriviLog("GetLinkAtCaret Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Friend Function SelezioneAttivaNonVuota() As Boolean
        Try
            If Not _selectionActive Then Return False
            Dim sIdx, sSub, sChar, eIdx, eSub, eChar As Integer
            CalculateSelectionRange(sIdx, sSub, sChar, eIdx, eSub, eChar)
            If sIdx <> eIdx Then Return True
            If sSub <> eSub Then Return True
            Return sChar <> eChar
        Catch ex As Exception
            logger.ScriviLog("SelezioneAttivaNonVuota: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub HandleDeleteKey()
        Try
            If SolaLettura Then Return
            If SelezioneAttivaNonVuota() Then
                SaveUndoState()
                DeleteSelection()
                Return
            End If
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing Then Return
            
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    _elements(_caretIndex).CachedHeight = -1
                End If
            End SyncLock
            
            Dim text As String = para.GetText()
            If Not String.IsNullOrEmpty(text) AndAlso _caretCharIndex < text.Length Then
                Dim charToDelete As Char = text(_caretCharIndex)
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(WGSegni.Converti(charToDelete.ToString()))
                
                If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 Then
                    Dim posizioneTrovata As Tuple(Of DocRun, Integer) = TrovaRunEOffsetDaPosizione(para, _caretCharIndex)
                    If posizioneTrovata IsNot Nothing Then
                        Dim run As DocRun = posizioneTrovata.Item1
                        Dim offsetLocale As Integer = posizioneTrovata.Item2
                        If run.Text IsNot Nothing AndAlso offsetLocale >= 0 AndAlso offsetLocale < run.Text.Length Then
                            run.Text = run.Text.Remove(offsetLocale, 1)
                            RimuoviRunVuoti(para)
                            TriggerLayout()
                            UpdateCaretAndNotify(True)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("HandleDeleteKey Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la cancellazione: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub HandleBackspaceKey()
        Try
            If SolaLettura Then Return
            If SelezioneAttivaNonVuota() Then
                SaveUndoState()
                DeleteSelection()
                Return
            End If
            If _caretCharIndex > 0 Then
                Dim para As DocParagraph = GetCurrentParagraph()
                If para Is Nothing Then Return
                
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements(_caretIndex).CachedHeight = -1
                    End If
                End SyncLock
                
                Dim text As String = para.GetText()
                If Not String.IsNullOrEmpty(text) AndAlso _caretCharIndex - 1 < text.Length Then
                    Dim charToDelete As Char = text(_caretCharIndex - 1)
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(WGSegni.Converti(charToDelete.ToString()))
                End If
                
                If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 Then
                    Dim posizioneTrovata As Tuple(Of DocRun, Integer) = TrovaRunEOffsetDaPosizione(para, _caretCharIndex - 1)
                    If posizioneTrovata IsNot Nothing Then
                        Dim run As DocRun = posizioneTrovata.Item1
                        Dim offsetLocale As Integer = posizioneTrovata.Item2
                        If run.Text IsNot Nothing AndAlso offsetLocale >= 0 AndAlso offsetLocale < run.Text.Length Then
                            run.Text = run.Text.Remove(offsetLocale, 1)
                            RimuoviRunVuoti(para)
                            _caretCharIndex -= 1
                            TriggerLayout()
                            UpdateCaretAndNotify(True)
                        End If
                    End If
                End If
            Else
                Dim paraNotaBackspace As DocParagraph = GetCurrentParagraph()
                Dim eNotaBackspace As Boolean = paraNotaBackspace IsNot Nothing AndAlso Not String.IsNullOrEmpty(paraNotaBackspace.BookmarkId) AndAlso (paraNotaBackspace.BookmarkId.StartsWith("NOTA_PIE_") OrElse paraNotaBackspace.BookmarkId.StartsWith("NOTA_FINE_"))
                If Not eNotaBackspace Then
                    MergeWithPrevious()
                    UpdateCaretAndNotify(True)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("HandleBackspaceKey Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la cancellazione: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub HandleCtrlBackspace()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                If _caretCharIndex > 0 Then
                    Dim text As String = para.GetText()
                    If String.IsNullOrEmpty(text) Then Return
                    
                    Dim i As Integer = _caretCharIndex
                    While i > 0 AndAlso i <= text.Length AndAlso Char.IsWhiteSpace(text(i - 1))
                        i -= 1
                    End While
                    While i > 0 AndAlso Not Char.IsWhiteSpace(text(i - 1))
                        i -= 1
                    End While
                    
                    Dim count As Integer = _caretCharIndex - i
                    If count > 0 Then
                        Dim word As String = text.Substring(i, count)
                        If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(word)
                    
                        SyncLock _elementsLock
                            If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                                _elements(_caretIndex).CachedHeight = -1
                            End If
                        End SyncLock
                        
                        If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 Then
                            RimuoviIntervalloDaiRun(para, i, count)
                            RimuoviRunVuoti(para)
                            _caretCharIndex = i
                            TriggerLayout()
                            UpdateCaretAndNotify(True)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("HandleCtrlBackspace Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la cancellazione della parola: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub HandleCtrlDeleteKey()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim text As String = para.GetText()
                If String.IsNullOrEmpty(text) OrElse _caretCharIndex >= text.Length Then Return

                Dim i As Integer = _caretCharIndex
                While i < text.Length AndAlso Char.IsWhiteSpace(text(i))
                    i += 1
                End While
                While i < text.Length AndAlso Not Char.IsWhiteSpace(text(i))
                    i += 1
                End While

                Dim count As Integer = i - _caretCharIndex
                If count > 0 Then
                    Dim word As String = text.Substring(_caretCharIndex, count)
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(word)

                    SyncLock _elementsLock
                        If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                            _elements(_caretIndex).CachedHeight = -1
                        End If
                    End SyncLock

                    If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 Then
                        RimuoviIntervalloDaiRun(para, _caretCharIndex, count)
                        RimuoviRunVuoti(para)
                        TriggerLayout()
                        UpdateCaretAndNotify(True)
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("HandleCtrlDeleteKey Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la cancellazione della parola: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub MergeWithPrevious()
        Try
            If SolaLettura Then Return
            Dim currentEl As DocElement = Nothing
            Dim prevEl As DocElement = Nothing

            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                If _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then currentEl = _elements(_caretIndex)
                If _caretIndex > 0 AndAlso _caretIndex - 1 < _elements.Count Then prevEl = _elements(_caretIndex - 1)
            End SyncLock

            If TypeOf currentEl Is DocParagraph AndAlso TypeOf prevEl Is DocParagraph Then
                Dim currPara As DocParagraph = DirectCast(currentEl, DocParagraph)
                Dim prevPara As DocParagraph = DirectCast(prevEl, DocParagraph)
 
                Dim oldLen As Integer = prevPara.GetTextLength()
                If currPara.Runs IsNot Nothing AndAlso prevPara.Runs IsNot Nothing Then
                    For Each r As DocRun In currPara.Runs
                        prevPara.Runs.Add(r)
                    Next
                End If

                SyncLock _elementsLock
                    If _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements.RemoveAt(_caretIndex)
                    End If
                    If prevEl IsNot Nothing Then
                        prevEl.CachedHeight = -1
                    End If
                End SyncLock

                If _caretIndex > 0 Then _caretIndex -= 1
                _caretCharIndex = oldLen
          
                TriggerLayout()
                ScrollToCaret()
                If Me.IsHandleCreated Then
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                End If
                UpdateCaretAndNotify(True)
            End If
        Catch ex As Exception
            logger.ScriviLog("MergeWithPrevious Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore nell'unione della riga: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub DividiRunsAOffset(runsOrigine As List(Of DocRun), offset As Integer, ByRef runsSinistra As List(Of DocRun), ByRef runsDestra As List(Of DocRun))
        runsSinistra = New List(Of DocRun)()
        runsDestra = New List(Of DocRun)()
        Try
            If runsOrigine IsNot Nothing Then
                Dim posizione As Integer = 0
                For Each r As DocRun In runsOrigine
                    If r Is Nothing Then Continue For

                    If r.IsImage Then
                        If posizione < offset Then
                            runsSinistra.Add(r)
                        Else
                            runsDestra.Add(r)
                        End If
                        Continue For
                    End If

                    Dim testoRun As String = If(r.Text, "")
                    Dim inizioRun As Integer = posizione
                    Dim fineRun As Integer = posizione + testoRun.Length

                    If fineRun <= offset Then
                        runsSinistra.Add(r)
                    ElseIf inizioRun >= offset Then
                        runsDestra.Add(r)
                    Else
                        Dim taglio As Integer = offset - inizioRun
                        Dim runSx As DocRun = r.Clone()
                        runSx.Text = testoRun.Substring(0, taglio)
                        runsSinistra.Add(runSx)
                        Dim runDx As DocRun = r.Clone()
                        runDx.Text = testoRun.Substring(taglio)
                        runsDestra.Add(runDx)
                    End If

                    posizione = fineRun
                Next
            End If

            If runsSinistra.Count = 0 Then runsSinistra.Add(New DocRun() With {.Text = ""})
            If runsDestra.Count = 0 Then runsDestra.Add(New DocRun() With {.Text = ""})
        Catch ex As Exception
            logger.ScriviLog("DividiRunsAOffset Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub BreakLine()
        Try
            If SolaLettura Then Return
            Dim currentPara As DocParagraph = GetCurrentParagraph()
            If currentPara Is Nothing Then Return

            Dim text As String = currentPara.GetText()
            If text Is Nothing Then text = ""
            
            Dim leftText As String = ""
            Dim rightText As String = ""
            
            If _caretCharIndex >= 0 AndAlso _caretCharIndex <= text.Length Then
                leftText = text.Substring(0, _caretCharIndex)
                rightText = text.Substring(_caretCharIndex)
            Else
                leftText = text
            End If

            Dim newLeftText As String = leftText
            Dim nextLinePrefix As String = ""
            Dim tipoNuovaRiga As String = "Nuova riga"

            If leftText.Trim() = "•" Then
                newLeftText = ""
                rightText = ""
                _caretCharIndex = 0
                If currentPara.Runs IsNot Nothing Then
                    currentPara.Runs.Clear()
                    currentPara.Runs.Add(New DocRun() With {.Text = ""})
                End If
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements(_caretIndex).CachedHeight = -1
                    End If
                End SyncLock
              
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(True)
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto("Elenco rimosso")
                Return
            End If

            Dim numMatch = Regex.Match(leftText, "^(\d+)\.\s$")
            If numMatch.Success Then
                newLeftText = ""
                rightText = ""
                _caretCharIndex = 0
                If currentPara.Runs IsNot Nothing Then
                    currentPara.Runs.Clear()
                    currentPara.Runs.Add(New DocRun() With {.Text = ""})
                End If
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements(_caretIndex).CachedHeight = -1
                    End If
                End SyncLock
                TriggerLayout()
                ScrollToCaret()
                UpdateCaretAndNotify(True)
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto("Elenco rimosso")
                Return
            End If

            If leftText.StartsWith("• ") Then
                nextLinePrefix = "• "
                tipoNuovaRiga = "Punto ad elenco"
            Else
                Dim listMatch = Regex.Match(leftText, "^(\d+)\.\s")
                If listMatch.Success Then
                    Dim num As Integer
                    If Integer.TryParse(listMatch.Groups(1).Value, num) Then
                        nextLinePrefix = (num + 1).ToString() & ". "
                        tipoNuovaRiga = "Punto numerato"
                    End If
                End If
            End If

            Dim runsSinistra As List(Of DocRun) = Nothing
            Dim runsDestra As List(Of DocRun) = Nothing
            DividiRunsAOffset(currentPara.Runs, _caretCharIndex, runsSinistra, runsDestra)

            If currentPara.Runs IsNot Nothing Then
                currentPara.Runs.Clear()
                For Each runSx As DocRun In runsSinistra
                    currentPara.Runs.Add(runSx)
                Next
            End If

            Dim newPara As New DocParagraph()
            newPara.IndentLevel = currentPara.IndentLevel
            newPara.Alignment = currentPara.Alignment
            If newPara.Runs Is Nothing Then newPara.Runs = New List(Of DocRun)()
            newPara.Runs.Clear()
            If Not String.IsNullOrEmpty(nextLinePrefix) Then newPara.Runs.Add(New DocRun() With {.Text = nextLinePrefix})
            For Each runDx As DocRun In runsDestra
                newPara.Runs.Add(runDx)
            Next

            If currentPara.IsListItem Then
                Dim testoVoceCorrente As String = If(newLeftText, "").Trim()
                If testoVoceCorrente = "" AndAlso rightText = "" Then
                    currentPara.IsListItem = False
                    currentPara.NumberingId = 0
                    currentPara.ListNumValue = ""
                    tipoNuovaRiga = "Elenco terminato"
                Else
                    newPara.IsListItem = True
                    newPara.NumberingId = currentPara.NumberingId
                    newPara.NumberingLevel = currentPara.NumberingLevel
                    If Not String.IsNullOrEmpty(currentPara.ListNumValue) Then
                        Dim numeroCorrente As Integer = 0
                        Integer.TryParse(currentPara.ListNumValue, numeroCorrente)
                        newPara.ListNumValue = (numeroCorrente + 1).ToString()
                        tipoNuovaRiga = "Punto " & newPara.ListNumValue
                    Else
                        newPara.ListNumValue = ""
                        tipoNuovaRiga = "Punto ad elenco"
                    End If
                End If
            End If

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    _elements(_caretIndex).CachedHeight = -1
                    Dim currentEl As DocElement = _elements(_caretIndex)
                    If TypeOf currentEl Is DocParagraph Then
                        _elements.Insert(_caretIndex + 1, newPara)
                        _caretIndex += 1
                        _caretCharIndex = nextLinePrefix.Length
                    ElseIf TypeOf currentEl Is DocTable Then
                        Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                        If tbl.Rows IsNot Nothing AndAlso _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count Then
                            Dim row As DocTableRow = tbl.Rows(_tblRow)
                            If row.Cells IsNot Nothing AndAlso _tblCol >= 0 AndAlso _tblCol < row.Cells.Count Then
                                Dim cell As DocTableCell = row.Cells(_tblCol)
                                If cell.Paragraphs IsNot Nothing AndAlso _tblPara >= 0 AndAlso _tblPara < cell.Paragraphs.Count Then
                                    cell.Paragraphs.Insert(_tblPara + 1, newPara)
                                    tbl.CachedHeight = -1
                                    _tblPara += 1
                                    _caretCharIndex = nextLinePrefix.Length
                                End If
                            End If
                        End If
                    End If
                End If
            End SyncLock
            TriggerLayout()
            ScrollToCaret()
            UpdateCaretAndNotify(True)
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(tipoNuovaRiga)
        Catch ex As Exception
            logger.ScriviLog("BreakLine Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore nell'andare a capo: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub GestisciVScroll(wParam As IntPtr)
        Try
            Dim nScrollCode As Integer = CInt(wParam) And &HFFFF
            Dim si As New SCROLLINFO()
            si.cbSize = Marshal.SizeOf(si)
            si.fMask = SIF_ALL
            If Me.IsHandleCreated Then
                GetScrollInfo(Me.Handle, SB_VERT, si)
            End If
            Dim newPos As Integer = si.nPos
            Select Case nScrollCode
                Case 0 : newPos -= _lineHeight
                Case 1 : newPos += _lineHeight
                Case 2 : newPos -= si.nPage
                Case 3 : newPos += si.nPage
                Case 5 : newPos = si.nTrackPos
            End Select
            UpdateScrollPos(newPos)
        Catch ex As Exception
            logger.ScriviLog("GestisciVScroll Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GestisciVScroll: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub GestisciMouseWheel(wParam As IntPtr)
        Try
            Dim delta As Integer = CInt(wParam) >> 16
            Dim lines As Integer = delta / 120
            Dim scrollAmount As Integer = lines * _lineHeight * 3
            UpdateScrollPos(_vScrollPos - scrollAmount)
        Catch ex As Exception
            logger.ScriviLog("GestisciMouseWheel Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GestisciMouseWheel: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub UpdateScrollPos(newPos As Integer)
        Try
            Dim maxPos As Integer = Math.Max(0, _totalContentHeight - Me.Height)
            newPos = Math.Max(0, Math.Min(newPos, maxPos))
            If newPos <> _vScrollPos Then
                _vScrollPos = newPos
                Dim si As New SCROLLINFO()
                si.cbSize = Marshal.SizeOf(si)
                si.fMask = SIF_POS
                si.nPos = _vScrollPos
                If Me.IsHandleCreated Then
                    SetScrollInfo(Me.Handle, SB_VERT, si, True)
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("UpdateScrollPos Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in UpdateScrollPos: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub UpdateScrollBars(contentHeight As Integer)
        Try
            If contentHeight = _totalContentHeight AndAlso Me.Height > 0 Then Return
            _totalContentHeight = contentHeight
            Dim si As New SCROLLINFO()
            si.cbSize = Marshal.SizeOf(si)
            si.fMask = SIF_RANGE Or SIF_PAGE Or SIF_POS
            si.nMin = 0
            si.nMax = _totalContentHeight
            si.nPage = Me.Height
            si.nPos = _vScrollPos
            If Me.IsHandleCreated Then
                SetScrollInfo(Me.Handle, SB_VERT, si, True)
            End If
        Catch ex As Exception
            logger.ScriviLog("UpdateScrollBars Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in UpdateScrollBars: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub InvalidaTutteLeAltezze()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For Each el As DocElement In _elements
                    If el Is Nothing Then Continue For
                    el.CachedHeight = -1
                    Dim tbl As DocTable = TryCast(el, DocTable)
                    If tbl IsNot Nothing AndAlso tbl.Rows IsNot Nothing Then
                        For Each riga As DocTableRow In tbl.Rows
                            If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                            For Each cella As DocTableCell In riga.Cells
                                If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                                For Each p As DocParagraph In cella.Paragraphs
                                    If p IsNot Nothing Then p.CachedHeight = -1
                                Next
                            Next
                        Next
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("InvalidaTutteLeAltezze Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiornaSafeWidth()
        Try
            If Me.IsDisposed Then Return
            If _renderer IsNot Nothing Then
                _safeWidthTemp = _renderer.LarghezzaContenuto(Me.ClientSize.Width)
            Else
                _safeWidthTemp = Math.Max(0, Me.Width - 20)
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiornaSafeWidth Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SafeInvalidate()
        Try
            If Not Me.IsDisposed AndAlso Me.IsHandleCreated Then 
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
            End If
        Catch ex As Exception
            logger.ScriviLog("SafeInvalidate Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LayoutLoop()
        Try
            While Not _stopThreads
                _layoutSignal.WaitOne()
                If _stopThreads Then Exit While
                Dim needsRedraw As Boolean = False
                ' Alla chiusura il controllo può essere eliminato mentre questo thread aspetta: niente Invoke su un oggetto eliminato.
                If _stopThreads OrElse Me.IsDisposed OrElse Me.Disposing Then Exit While
                If Me.IsHandleCreated Then
                    Try
                        Me.Invoke(New MethodInvoker(AddressOf AggiornaSafeWidth))
                    Catch exEliminato As ObjectDisposedException
                        Exit While
                    Catch exInvokeWidth As Exception
                        Try
                            logger.ScriviLog("LayoutLoop AggiornaSafeWidth Invoke: " & exInvokeWidth.Message)
                        Catch
                        End Try
                    End Try
                End If
                
                If _renderer Is Nothing Then Continue While

                Dim elementsSnapshot As List(Of DocElement)
                SyncLock _elementsLock
                    If _elements IsNot Nothing Then
                        elementsSnapshot = New List(Of DocElement)(_elements)
                    Else
                        elementsSnapshot = New List(Of DocElement)()
                    End If
                End SyncLock
                
                For Each el In elementsSnapshot
                    If _stopThreads Then Exit While
                    If el IsNot Nothing AndAlso el.CachedHeight = -1 Then
                        Dim newH As Integer = _lineHeight
                        Try
                            If TypeOf el Is DocParagraph Then
                                newH = _renderer.MeasureParagraphHeight(DirectCast(el, DocParagraph), _safeWidthTemp)
                            ElseIf TypeOf el Is DocTable Then
                                newH = _renderer.MeasureTableHeight(DirectCast(el, DocTable), _safeWidthTemp)
                            End If
                        Catch exRender As Exception
                            Try
                                If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                                    Me.Invoke(New MethodInvoker(Sub() logger.ScriviLog("Errore Measure: " & exRender.Message)))
                                End If
                            Catch exInvokeLog As Exception
                                Try
                                    logger.ScriviLog("Errore Measure (fallback diretto): " & exRender.Message & " | Invoke fallito: " & exInvokeLog.Message)
                                Catch
                                End Try
                            End Try
                            newH = _lineHeight
                        End Try
                        el.CachedHeight = newH
                        needsRedraw = True
                    End If
                Next
                
                If needsRedraw AndAlso Not _stopThreads AndAlso Me.IsHandleCreated Then
                    Try
                        Me.Invoke(New MethodInvoker(AddressOf SafeInvalidate))
                    Catch exInvokeInvalidate As Exception
                        Try
                            logger.ScriviLog("LayoutLoop SafeInvalidate Invoke: " & exInvokeInvalidate.Message)
                        Catch
                        End Try
                    End Try
                End If
            End While
        Catch ex As Exception
            Try
                If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                    Me.Invoke(New MethodInvoker(Sub() logger.ScriviLog("LayoutLoop Error: " & ex.Message)))
                End If
            Catch innerEx As Exception
                Try
                    logger.ScriviLog("LayoutLoop Error (fallback diretto): " & ex.Message & " | Invoke fallito: " & innerEx.Message)
                Catch
                End Try
            End Try
        End Try
    End Sub

    Private Sub TriggerLayout()
        Try
            If _layoutSignal IsNot Nothing Then _layoutSignal.Set()
        Catch ex As Exception
            logger.ScriviLog("TriggerLayout Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in TriggerLayout: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub GestisciMouseDown(lParam As IntPtr)
        Try
            Me.Focus()
            _selectionActive = False
            If Me.IsHandleCreated Then
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
            End If
        Catch ex As Exception
            logger.ScriviLog("GestisciMouseDown Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GestisciMouseDown: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Function TrovaRunEOffsetDaPosizione(para As DocParagraph, posizioneCarattere As Integer) As Tuple(Of DocRun, Integer)
        Try
            If para Is Nothing OrElse para.Runs Is Nothing OrElse para.Runs.Count = 0 Then Return Nothing

            Dim accumulato As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage Then Continue For
                Dim lunghezzaRun As Integer = If(r.Text, "").Length
                If posizioneCarattere <= accumulato + lunghezzaRun Then
                    Return Tuple.Create(r, posizioneCarattere - accumulato)
                End If
                accumulato += lunghezzaRun
            Next

            Dim ultimoRunTestuale As DocRun = Nothing
            For Each r As DocRun In para.Runs
                If r IsNot Nothing AndAlso Not r.IsImage Then ultimoRunTestuale = r
            Next
            If ultimoRunTestuale IsNot Nothing Then Return Tuple.Create(ultimoRunTestuale, If(ultimoRunTestuale.Text, "").Length)

            Return Nothing
        Catch ex As Exception
            logger.ScriviLog("TrovaRunEOffsetDaPosizione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Friend Sub RimuoviIntervalloDaiRun(para As DocParagraph, inizio As Integer, lunghezza As Integer)
        Try
            If para Is Nothing OrElse para.Runs Is Nothing OrElse lunghezza <= 0 Then Return

            Dim fine As Integer = inizio + lunghezza
            Dim accumulato As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage OrElse r.Text Is Nothing Then Continue For
                Dim lunghezzaRun As Integer = r.Text.Length
                Dim inizioRun As Integer = accumulato
                Dim fineRun As Integer = accumulato + lunghezzaRun

                Dim sovrapInizio As Integer = System.Math.Max(inizio, inizioRun)
                Dim sovrapFine As Integer = System.Math.Min(fine, fineRun)

                If sovrapFine > sovrapInizio Then
                    Dim offsetLocaleInizio As Integer = sovrapInizio - inizioRun
                    Dim lunghezzaLocale As Integer = sovrapFine - sovrapInizio
                    r.Text = r.Text.Remove(offsetLocaleInizio, lunghezzaLocale)
                End If

                accumulato = fineRun
            Next
        Catch ex As Exception
            logger.ScriviLog("RimuoviIntervalloDaiRun: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub RimuoviRunVuoti(para As DocParagraph)
        Try
            If para Is Nothing OrElse para.Runs Is Nothing OrElse para.Runs.Count <= 1 Then Return
            Dim impostazioniCascata As New WGImpostazioniComuni()
            Dim eliminaNotaAttivo As Boolean = impostazioniCascata.EliminaNotaConParola

            Dim i As Integer = para.Runs.Count - 1
            While i >= 0
                Dim r As DocRun = para.Runs(i)
                If r IsNot Nothing AndAlso Not r.IsImage AndAlso String.IsNullOrEmpty(r.Text) Then
                    If eliminaNotaAttivo AndAlso i + 1 < para.Runs.Count Then
                        Dim runSuccessivo As DocRun = para.Runs(i + 1)
                        If runSuccessivo IsNot Nothing AndAlso runSuccessivo.IsSuperscript AndAlso Not String.IsNullOrEmpty(runSuccessivo.LinkUrl) AndAlso (runSuccessivo.LinkUrl.StartsWith("NOTA_PIE_") OrElse runSuccessivo.LinkUrl.StartsWith("NOTA_FINE_")) Then
                            EliminaParagrafoNotaPerBookmark(runSuccessivo.LinkUrl)
                            para.Runs.RemoveAt(i + 1)
                        End If
                    End If
                    para.Runs.RemoveAt(i)
                End If
                i -= 1
            End While
            If para.Runs.Count = 0 Then para.Runs.Add(New DocRun())
        Catch ex As Exception
            logger.ScriviLog("RimuoviRunVuoti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub EliminaParagrafoNotaPerBookmark(bookmarkId As String)
        Try
            If String.IsNullOrEmpty(bookmarkId) Then Return
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _elements.Count - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim pNota As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If pNota.BookmarkId = bookmarkId Then
                            _elements.RemoveAt(i)
                            If _caretIndex > i Then _caretIndex -= 1
                            Exit For
                        End If
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("EliminaParagrafoNotaPerBookmark: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub InsertChar(c As Char)
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements(_caretIndex).CachedHeight = -1
                    End If
                End SyncLock
                If para.Runs IsNot Nothing Then
                    If para.Runs.Count = 0 Then para.Runs.Add(New DocRun())
                    Dim posizioneTrovata As Tuple(Of DocRun, Integer) = TrovaRunEOffsetDaPosizione(para, _caretCharIndex)
                    If posizioneTrovata IsNot Nothing Then
                        Dim run As DocRun = posizioneTrovata.Item1
                        Dim offsetLocale As Integer = posizioneTrovata.Item2
                        If run.Text Is Nothing Then run.Text = ""
                        offsetLocale = System.Math.Max(0, System.Math.Min(offsetLocale, run.Text.Length))
                        run.Text = run.Text.Insert(offsetLocale, c.ToString())
                        _caretCharIndex += 1
                    Else
                        Dim run As DocRun = para.Runs(para.Runs.Count - 1)
                        If run.Text Is Nothing Then run.Text = ""
                        run.Text += c.ToString()
                        _caretCharIndex = run.Text.Length
                    End If
                    TriggerLayout()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("InsertChar Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante l'inserimento: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub ScrollToCaret()
        Try
            Dim currentY As Integer = 35
            Dim localElements As List(Of DocElement) = Nothing
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    localElements = New List(Of DocElement)(_elements)
                End If
            End SyncLock
            
            If localElements IsNot Nothing Then
                For i As Integer = 0 To Math.Min(_caretIndex - 1, localElements.Count - 1)
                    Dim h As Integer = localElements(i).CachedHeight
                    If h = -1 Then h = _lineHeight
                    currentY += h
                Next
                If _caretIndex >= 0 AndAlso _caretIndex < localElements.Count AndAlso TypeOf localElements(_caretIndex) Is DocTable Then
                    Dim tbl As DocTable = DirectCast(localElements(_caretIndex), DocTable)
                    For r As Integer = 0 To _tblRow - 1
                        currentY += _lineHeight
                    Next
                End If
            End If
            
            Dim viewportTop As Integer = _vScrollPos
            Dim viewportBottom As Integer = viewportTop + Me.Height
            If currentY < viewportTop Then
                UpdateScrollPos(currentY)
            ElseIf currentY + _lineHeight > viewportBottom Then
                UpdateScrollPos(currentY + _lineHeight - Me.Height + 20)
            End If
        Catch ex As Exception
            logger.ScriviLog("ScrollToCaret Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in ScrollToCaret: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Function GetCurrentParagraph() As DocParagraph
        Try
            Dim el As DocElement = Nothing
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then el = _elements(_caretIndex)
            End SyncLock
            If el Is Nothing Then Return Nothing
            If TypeOf el Is DocParagraph Then
                Return DirectCast(el, DocParagraph)
            ElseIf TypeOf el Is DocTable Then
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If tbl.Rows IsNot Nothing AndAlso _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count Then
                    Dim row As DocTableRow = tbl.Rows(_tblRow)
                    If row.Cells IsNot Nothing AndAlso _tblCol >= 0 AndAlso _tblCol < row.Cells.Count Then
                        Dim cell As DocTableCell = row.Cells(_tblCol)
                        If cell.Paragraphs IsNot Nothing AndAlso _tblPara >= 0 AndAlso _tblPara < cell.Paragraphs.Count Then
                            Return cell.Paragraphs(_tblPara)
                        End If
                    End If
                End If
            End If
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog("GetCurrentParagraph Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GetCurrentParagraph: " & ex.Message)
            messaggi.ShowDialog()
            Return Nothing
        End Try
    End Function

    Friend Function PosizioneAssoluta(indiceElemento As Integer, indiceCella As Integer, indiceCarattere As Integer) As Long
        Try
            Return CLng(Math.Max(0, indiceElemento)) * 1000000000000L + CLng(indiceCella) * 1000000L + CLng(Math.Max(0, indiceCarattere))
        Catch ex As Exception
            logger.ScriviLog("PosizioneAssoluta Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Friend Function GetPackedTableIndex() As Integer
        Try
            Return (_tblRow * 10000) + (_tblCol * 100) + _tblPara
        Catch ex As Exception
            logger.ScriviLog("GetPackedTableIndex Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GetPackedTableIndex: " & ex.Message)
            messaggi.ShowDialog()
            Return 0
        End Try
    End Function

    Friend Sub SaveUndoState()
        Try
            If _redoStack IsNot Nothing Then _redoStack.Clear()
            Dim state As New UndoState()
            state.Elements = New List(Of DocElement)
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If el IsNot Nothing Then state.Elements.Add(el.Clone())
                    Next
                End If
            End SyncLock
            state.CaretIndex = _caretIndex
            state.TblRow = _tblRow
            state.TblCol = _tblCol
            state.TblPara = _tblPara
            state.CaretCharIndex = _caretCharIndex
            
            If _undoStack IsNot Nothing Then
                _undoStack.Push(state)
                If _undoStack.Count > 50 Then
                    Dim arr = _undoStack.ToArray()
                    _undoStack.Clear()
                    For i As Integer = arr.Length - 2 To 0 Step -1
                        _undoStack.Push(arr(i))
                    Next
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("SaveUndoState: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in SaveUndoState: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub Undo()
        Try
            If SolaLettura Then Return
            If _undoStack IsNot Nothing AndAlso _undoStack.Count > 0 Then
                Dim state As UndoState = _undoStack.Pop()
                Dim currentState As New UndoState()
                currentState.Elements = New List(Of DocElement)
                SyncLock _elementsLock
                    If _elements IsNot Nothing Then
                        For Each el In _elements
                            If el IsNot Nothing Then currentState.Elements.Add(el.Clone())
                        Next
                    End If
                End SyncLock
                currentState.CaretIndex = _caretIndex
                currentState.TblRow = _tblRow
                currentState.TblCol = _tblCol
                currentState.TblPara = _tblPara
                currentState.CaretCharIndex = _caretCharIndex
                
                If _redoStack IsNot Nothing Then _redoStack.Push(currentState)

                SyncLock _elementsLock
                    _elements = state.Elements
                    If _elements IsNot Nothing Then
                        For Each el In _elements
                            If el IsNot Nothing Then el.CachedHeight = -1
                        Next
                    End If
                    _documentState.Elements = _elements
                End SyncLock
                _caretIndex = state.CaretIndex
                _tblRow = state.TblRow
                _tblCol = state.TblCol
                _tblPara = state.TblPara
                _caretCharIndex = state.CaretCharIndex
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                Me.Invalidate()
            End If
        Catch ex As Exception
            logger.ScriviLog("Undo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante l'annullamento: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub Redo()
        Try
            If SolaLettura Then Return
            If _redoStack IsNot Nothing AndAlso _redoStack.Count > 0 Then
                Dim state As UndoState = _redoStack.Pop()
                Dim currentState As New UndoState()
                currentState.Elements = New List(Of DocElement)
                SyncLock _elementsLock
                    If _elements IsNot Nothing Then
                        For Each el In _elements
                            If el IsNot Nothing Then currentState.Elements.Add(el.Clone())
                        Next
                    End If
                End SyncLock
                currentState.CaretIndex = _caretIndex
                currentState.TblRow = _tblRow
                currentState.TblCol = _tblCol
                currentState.TblPara = _tblPara
                currentState.CaretCharIndex = _caretCharIndex
                
                If _undoStack IsNot Nothing Then _undoStack.Push(currentState)

                SyncLock _elementsLock
                    _elements = state.Elements
                    If _elements IsNot Nothing Then
                        For Each el In _elements
                            If el IsNot Nothing Then el.CachedHeight = -1
                        Next
                    End If
                    _documentState.Elements = _elements
                End SyncLock
                _caretIndex = state.CaretIndex
                _tblRow = state.TblRow
                _tblCol = state.TblCol
                _tblPara = state.TblPara
                _caretCharIndex = state.CaretCharIndex
                _selectionActive = False
                TriggerLayout()
                ScrollToCaret()
                Me.Invalidate()
            End If
        Catch ex As Exception
            logger.ScriviLog("Redo Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub FormattaSelezione(formatAction As Func(Of DocRun, DocRun))
        Try
            If SolaLettura Then Return
            If Not _selectionActive Then Return

            Dim startIdx, endIdx, startSub, endSub, startChar, endChar As Integer
            CalculateSelectionRange(startIdx, startSub, startChar, endIdx, endSub, endChar)

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = Math.Max(0, startIdx) To Math.Min(endIdx, _elements.Count - 1)
                        Dim el As DocElement = _elements(i)
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            p.CachedHeight = -1
                            
                            Dim sChar As Integer = If(i = startIdx, startChar, 0)
                            Dim eChar As Integer = If(i = endIdx, endChar, p.GetTextLength())

                            Dim newRuns As New List(Of DocRun)()
                            Dim currentPos As Integer = 0

                            If p.Runs IsNot Nothing Then
                                For Each r As DocRun In p.Runs
                                    If r.Text Is Nothing Then r.Text = ""
                                    Dim runLen As Integer = r.Text.Length
                                    If currentPos >= eChar OrElse currentPos + runLen <= sChar Then
                                        newRuns.Add(r)
                                    Else
                                        Dim runStart As Integer = Math.Max(0, sChar - currentPos)
                                        Dim runEnd As Integer = Math.Min(runLen, eChar - currentPos)

                                        If runStart > 0 Then
                                            Dim leftRun = r.Clone()
                                            leftRun.Text = r.Text.Substring(0, runStart)
                                            newRuns.Add(leftRun)
                                        End If

                                        Dim midRun = r.Clone()
                                        midRun.Text = r.Text.Substring(runStart, runEnd - runStart)
                                        If formatAction IsNot Nothing Then formatAction(midRun)
                                        newRuns.Add(midRun)

                                        If runEnd < runLen Then
                                            Dim rightRun = r.Clone()
                                            rightRun.Text = r.Text.Substring(runEnd)
                                            newRuns.Add(rightRun)
                                        End If
                                    End If
                                    currentPos += runLen
                                Next
                                p.Runs = newRuns
                            End If
                        End If
                    Next
                End If
            End SyncLock
            _isModified = True
            SyncronizzaElementiInStato()
            TriggerLayout()
            Me.Invalidate()
            UpdateCaretAndNotify(True)
        Catch ex As Exception
            logger.ScriviLog("FormattaSelezione Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore nella formattazione: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Function GetSelectedElements() As List(Of DocElement)
        Dim list As New List(Of DocElement)()
        Try
            If Not _selectionActive Then Return list
            Dim startIdx, endIdx, startSub, endSub, startChar, endChar As Integer
            CalculateSelectionRange(startIdx, startSub, startChar, endIdx, endSub, endChar)

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = Math.Max(0, startIdx) To Math.Min(endIdx, _elements.Count - 1)
                        Dim el As DocElement = _elements(i)
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            Dim newP As New DocParagraph()
                            newP.IsListItem = p.IsListItem

                            Dim sChar As Integer = If(i = startIdx, startChar, 0)
                            Dim eChar As Integer = If(i = endIdx, endChar, p.GetTextLength())

                            Dim currentPos As Integer = 0
                            If p.Runs IsNot Nothing Then
                                For Each r As DocRun In p.Runs
                                    If r.Text Is Nothing Then r.Text = ""
                                    Dim runLen As Integer = r.Text.Length
                                    If currentPos + runLen > sChar AndAlso currentPos < eChar Then
                                        Dim runStart As Integer = Math.Max(0, sChar - currentPos)
                                        Dim runEnd As Integer = Math.Min(runLen, eChar - currentPos)
                                        Dim newRun As DocRun = r.Clone()
                                        newRun.Text = r.Text.Substring(runStart, runEnd - runStart)
                                        newP.Runs.Add(newRun)
                                    End If
                                    currentPos += runLen
                                Next
                            End If
                            If newP.Runs.Count > 0 OrElse (sChar = 0 AndAlso eChar = 0) Then
                                list.Add(newP)
                            End If
                        ElseIf TypeOf el Is DocTable Then
                            list.Add(el.Clone())
                        End If
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("GetSelectedElements Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GetSelectedElements: " & ex.Message)
            messaggi.ShowDialog()
        End Try
        Return list
    End Function

    Friend Sub CopySelection()
        Try
            Dim elementsToCopy = GetSelectedElements()
            If elementsToCopy Is Nothing OrElse elementsToCopy.Count = 0 Then Return
            MettiElementiNegliAppunti(elementsToCopy)
        Catch ex As Exception
            logger.ScriviLog("CopySelection Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la copia: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub CutSelection()
        Try
            If SolaLettura Then Return
            Dim txt As String = GetSelectedText()
            If Not String.IsNullOrEmpty(txt) Then
                CopySelection()
                DeleteSelection()
                _isModified = True
            End If
        Catch ex As Exception
            logger.ScriviLog("CutSelection Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante il taglio: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub MenuTabelle_EscPremuto(sender As Object, e As EventArgs)
        Try
            If _menuTabelleCorrente IsNot Nothing Then
                _menuTabelleCorrente.abbandona()
                _menuTabelleCorrente = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("MenuTabelle_EscPremuto Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in MenuTabelle_EscPremuto: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub MenuTabelle_Selezionato(id As String)
        Try
            If _menuTabelleCorrente IsNot Nothing Then
                Dim listaId As List(Of String) = _menuTabelleCorrente.ottieniElementi()
                If listaId IsNot Nothing AndAlso listaId.Count > 0 Then
                    Dim primoId As String = listaId(0)
                    Dim indice As Integer
                    If Integer.TryParse(primoId, indice) Then
                        VaiATabella(indice)
                    End If
                Else
                    Dim indiceSingolo As Integer
                    If Integer.TryParse(id, indiceSingolo) Then
                        VaiATabella(indiceSingolo)
                    End If
                End If
                _menuTabelleCorrente.abbandona()
                _menuTabelleCorrente = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("MenuTabelle_Selezionato Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in MenuTabelle_Selezionato: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    ''' <summary>Testo selezionato nel documento, vuoto se non c'è selezione.</summary>
    Public Function TestoSelezionato() As String
        Try
            If Not SelezioneAttivaNonVuota() Then Return ""
            Return GetSelectedText()
        Catch ex As Exception
            logger.ScriviLog("WGWord.TestoSelezionato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function GetSelectedText() As String
        Try
            If Not _selectionActive Then Return ""
            Dim startIdx, endIdx, startSub, endSub, startChar, endChar As Integer
            CalculateSelectionRange(startIdx, startSub, startChar, endIdx, endSub, endChar)
            Dim sb As New StringBuilder()
            
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = Math.Max(0, startIdx) To Math.Min(endIdx, _elements.Count - 1)
                        Dim el As DocElement = _elements(i)
                        If TypeOf el Is DocParagraph Then
                            Dim txt As String = DirectCast(el, DocParagraph).GetText()
                            If txt Is Nothing Then txt = ""
                            Dim s As Integer = If(i = startIdx, startChar, 0)
                            Dim e As Integer = If(i = endIdx, endChar, txt.Length)
                            
                            s = Math.Max(0, s)
                            e = Math.Min(txt.Length, e)
                            
                            If e > s Then sb.Append(txt.Substring(s, e - s))
                            If i < endIdx Then sb.AppendLine()
                        ElseIf TypeOf el Is DocTable Then
                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            If tbl.Rows IsNot Nothing Then
                                Dim subDa As Integer = If(i = startIdx, startSub, Integer.MinValue)
                                Dim subA As Integer = If(i = endIdx, endSub, Integer.MaxValue)
                                Dim primaCella As Boolean = True

                                For r As Integer = 0 To tbl.Rows.Count - 1
                                    Dim riga As DocTableRow = tbl.Rows(r)
                                    If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                                    For c As Integer = 0 To riga.Cells.Count - 1
                                        Dim cella As DocTableCell = riga.Cells(c)
                                        If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                                        For p As Integer = 0 To cella.Paragraphs.Count - 1
                                            Dim packedCella As Integer = (r * 10000) + (c * 100) + p
                                            If packedCella < subDa OrElse packedCella > subA Then Continue For

                                            Dim testoCella As String = cella.Paragraphs(p).GetText()
                                            If testoCella Is Nothing Then testoCella = ""
                                            Dim sCella As Integer = If(i = startIdx AndAlso packedCella = startSub, startChar, 0)
                                            Dim eCella As Integer = If(i = endIdx AndAlso packedCella = endSub, endChar, testoCella.Length)

                                            sCella = Math.Max(0, sCella)
                                            eCella = Math.Min(testoCella.Length, eCella)

                                            If Not primaCella Then sb.AppendLine()
                                            If eCella > sCella Then sb.Append(testoCella.Substring(sCella, eCella - sCella))
                                            primaCella = False
                                        Next
                                    Next
                                Next
                            End If
                            If i < endIdx Then sb.AppendLine()
                        End If
                    Next
                End If
            End SyncLock
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("GetSelectedText Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in GetSelectedText: " & ex.Message)
            messaggi.ShowDialog()
            Return ""
        End Try
    End Function

    Friend Sub CalculateSelectionRange(ByRef sIdx As Integer, ByRef sSub As Integer, ByRef sChar As Integer, ByRef eIdx As Integer, ByRef eSub As Integer, ByRef eChar As Integer)
        Try
            Dim currPacked As Long = PosizioneAssoluta(_caretIndex, GetPackedTableIndex(), _caretCharIndex)
            Dim anchPacked As Long = PosizioneAssoluta(_selAnchorIndex, _selAnchorSubIndex, _selAnchorCharIndex)
            
            If currPacked < anchPacked Then
                sIdx = _caretIndex : sSub = GetPackedTableIndex() : sChar = _caretCharIndex
                eIdx = _selAnchorIndex : eSub = _selAnchorSubIndex : eChar = _selAnchorCharIndex
            Else
                sIdx = _selAnchorIndex : sSub = _selAnchorSubIndex : sChar = _selAnchorCharIndex
                eIdx = _caretIndex : eSub = GetPackedTableIndex() : eChar = _caretCharIndex
            End If
        Catch ex As Exception
            logger.ScriviLog("CalculateSelectionRange Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in CalculateSelectionRange: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Function OttieniRunsRitagliatiPrefisso(para As DocParagraph, lunghezzaDaTenere As Integer) As List(Of DocRun)
        Dim risultato As New List(Of DocRun)
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return risultato
            Dim accumulato As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then
                    If accumulato < lunghezzaDaTenere Then risultato.Add(r.Clone())
                    Continue For
                End If
                Dim testoRun As String = If(r.Text, "")
                If accumulato >= lunghezzaDaTenere Then Exit For
                Dim daTenereQui As Integer = System.Math.Min(testoRun.Length, lunghezzaDaTenere - accumulato)
                If daTenereQui > 0 Then
                    Dim runClonato As DocRun = r.Clone()
                    runClonato.Text = testoRun.Substring(0, daTenereQui)
                    risultato.Add(runClonato)
                End If
                accumulato += testoRun.Length
            Next
        Catch ex As Exception
            logger.ScriviLog("OttieniRunsRitagliatiPrefisso: " & ex.Message)
        End Try
        Return risultato
    End Function

    Friend Function OttieniRunsRitagliatiSuffisso(para As DocParagraph, daPosizione As Integer) As List(Of DocRun)
        Dim risultato As New List(Of DocRun)
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return risultato
            Dim accumulato As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then
                    If accumulato >= daPosizione Then risultato.Add(r.Clone())
                    Continue For
                End If
                Dim testoRun As String = If(r.Text, "")
                Dim fineRun As Integer = accumulato + testoRun.Length
                If fineRun > daPosizione Then
                    Dim offsetLocale As Integer = System.Math.Max(0, daPosizione - accumulato)
                    Dim runClonato As DocRun = r.Clone()
                    runClonato.Text = testoRun.Substring(offsetLocale)
                    If runClonato.Text.Length > 0 Then risultato.Add(runClonato)
                End If
                accumulato = fineRun
            Next
        Catch ex As Exception
            logger.ScriviLog("OttieniRunsRitagliatiSuffisso: " & ex.Message)
        End Try
        Return risultato
    End Function

    Friend Sub DeleteSelection()
        Try
            If SolaLettura Then Return
            Dim startIdx, endIdx, startSub, endSub, startChar, endChar As Integer
            CalculateSelectionRange(startIdx, startSub, startChar, endIdx, endSub, endChar)
 
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso startIdx >= 0 AndAlso endIdx < _elements.Count Then
                    If startIdx = endIdx Then
                        Dim el As DocElement = _elements(startIdx)
                        el.CachedHeight = -1
                        Dim paraDaPulire As DocParagraph = Nothing
                        If TypeOf el Is DocParagraph Then
                            paraDaPulire = DirectCast(el, DocParagraph)
                        ElseIf TypeOf el Is DocTable AndAlso startSub = endSub Then
                            paraDaPulire = ParagrafoDaSubIndice(DirectCast(el, DocTable), startSub)
                            If paraDaPulire IsNot Nothing Then paraDaPulire.CachedHeight = -1
                        End If
                        If paraDaPulire IsNot Nothing Then
                            Dim txt As String = paraDaPulire.GetText()
                            If txt Is Nothing Then txt = ""
                            Dim len As Integer = endChar - startChar
          
                            If len > 0 AndAlso startChar >= 0 AndAlso startChar + len <= txt.Length Then
                                RimuoviIntervalloDaiRun(paraDaPulire, startChar, len)
                                RimuoviRunVuoti(paraDaPulire)
                            End If
                        End If
                    Else
                        Dim paraStart As DocParagraph = TryCast(_elements(startIdx), DocParagraph)
                        Dim paraEnd As DocParagraph = TryCast(_elements(endIdx), DocParagraph)
                        If paraStart Is Nothing OrElse paraEnd Is Nothing Then Return
                        
                        Dim runsPrefisso As List(Of DocRun) = OttieniRunsRitagliatiPrefisso(paraStart, startChar)
                        Dim runsSuffisso As List(Of DocRun) = OttieniRunsRitagliatiSuffisso(paraEnd, endChar)

                        paraStart.Runs.Clear()
                        paraStart.Runs.AddRange(runsPrefisso)
                        paraStart.Runs.AddRange(runsSuffisso)
                        If paraStart.Runs.Count = 0 Then paraStart.Runs.Add(New DocRun())
                        
                        _elements(startIdx).CachedHeight = -1
                        For i As Integer = endIdx To startIdx + 1 Step -1
                            If i < _elements.Count Then
                                _elements.RemoveAt(i)
                            End If
                        Next
                    End If
                End If
            End SyncLock
            
            _caretIndex = Math.Max(0, startIdx)
            _caretCharIndex = Math.Max(0, startChar)
            _selectionActive = False
            TriggerLayout()
            Me.Invalidate()
            UpdateCaretAndNotify(True)
            _isModified = True
        Catch ex As Exception
            logger.ScriviLog("DeleteSelection: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore nell'eliminazione della selezione: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub InsertRuns(runs As List(Of DocRun))
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing OrElse runs Is Nothing Then Return
            
            SyncLock _elementsLock
                para.CachedHeight = -1
            End SyncLock

            If para.Runs IsNot Nothing Then
                If para.Runs.Count = 0 Then
                    For Each r In runs
                        If r IsNot Nothing Then
                            para.Runs.Add(r.Clone())
                            If r.Text IsNot Nothing Then _caretCharIndex += r.Text.Length
                        End If
                    Next
                    Return
                End If

                Dim currentPos As Integer = 0
                Dim runIndex As Integer = -1
                Dim splitOffset As Integer = 0

                For i As Integer = 0 To para.Runs.Count - 1
                    If para.Runs(i) IsNot Nothing AndAlso para.Runs(i).Text IsNot Nothing Then
                        Dim rLen As Integer = para.Runs(i).Text.Length
                        If currentPos + rLen >= _caretCharIndex Then
                            runIndex = i
                            splitOffset = Math.Max(0, _caretCharIndex - currentPos)
                            Exit For
                        End If
                        currentPos += rLen
                    End If
                Next

                If runIndex = -1 Then
                    For Each r In runs
                        If r IsNot Nothing Then
                            para.Runs.Add(r.Clone())
                            If r.Text IsNot Nothing Then _caretCharIndex += r.Text.Length
                        End If
                    Next
                Else
                    Dim targetRun = para.Runs(runIndex)
                    If targetRun Is Nothing Then Return
                    If targetRun.Text Is Nothing Then targetRun.Text = ""
                    
                    splitOffset = Math.Min(splitOffset, targetRun.Text.Length)
                    
                    Dim leftText = targetRun.Text.Substring(0, splitOffset)
                    Dim rightText = targetRun.Text.Substring(splitOffset)

                    targetRun.Text = leftText
                    Dim insertIdx = runIndex + 1

                    For Each r In runs
                        If r IsNot Nothing Then
                            para.Runs.Insert(insertIdx, r.Clone())
                            insertIdx += 1
                            If r.Text IsNot Nothing Then _caretCharIndex += r.Text.Length
                        End If
                    Next

                    If rightText.Length > 0 Then
                        Dim rightRun = targetRun.Clone()
                        rightRun.Text = rightText
                        para.Runs.Insert(insertIdx, rightRun)
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("InsertRuns Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in InsertRuns: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub PasteText()
        Try
            If SolaLettura Then Return
            If _selectionActive Then DeleteSelection()

            If Clipboard.ContainsData("WGWordFormat") Then
                Dim xmlStr As String = TryCast(Clipboard.GetData("WGWordFormat"), String)
                If Not String.IsNullOrEmpty(xmlStr) Then
                    Dim serializer As New System.Xml.Serialization.XmlSerializer(GetType(List(Of DocElement)))
                    Dim sr As New IO.StringReader(xmlStr)
                    Dim copiedElements As List(Of DocElement) = DirectCast(serializer.Deserialize(sr), List(Of DocElement))

                    If copiedElements IsNot Nothing AndAlso copiedElements.Count > 0 Then
                        Dim firstEl = copiedElements(0)
                        If firstEl IsNot Nothing Then
                            If TypeOf firstEl Is DocParagraph Then
                                InsertRuns(DirectCast(firstEl, DocParagraph).Runs)
                            ElseIf TypeOf firstEl Is DocTable Then
                                SyncLock _elementsLock
                                    If _elements IsNot Nothing Then
                                        _elements.Insert(Math.Max(0, Math.Min(_caretIndex, _elements.Count)), firstEl.Clone())
                                        _caretIndex += 1
                                    End If
                                End SyncLock
                            End If
                        End If

                        For i As Integer = 1 To copiedElements.Count - 1
                            Dim el = copiedElements(i)
                            If el IsNot Nothing Then
                                If TypeOf el Is DocParagraph Then
                                    BreakLine()
                                    Dim p = DirectCast(el, DocParagraph)
                                    Dim curr = GetCurrentParagraph()
                                    If curr IsNot Nothing AndAlso p.Runs IsNot Nothing AndAlso curr.Runs IsNot Nothing Then
                                        curr.Runs.Clear()
                                        For Each r In p.Runs
                                            If r IsNot Nothing Then curr.Runs.Add(r.Clone())
                                        Next
                                        _caretCharIndex = curr.GetTextLength()
                                    End If
                                ElseIf TypeOf el Is DocTable Then
                                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                                        If TypeOf _elements(_caretIndex) Is DocParagraph AndAlso DirectCast(_elements(_caretIndex), DocParagraph).GetTextLength() > 0 Then
                                            BreakLine()
                                        End If
                                    End If
                                    SyncLock _elementsLock
                                        If _elements IsNot Nothing Then
                                            _elements.Insert(Math.Max(0, Math.Min(_caretIndex, _elements.Count)), el.Clone())
                                            _caretIndex += 1
                                        End If
                                    End SyncLock
                                End If
                            End If
                        Next

                        _isModified = True
                        SyncronizzaElementiInStato()
                        TriggerLayout()
                        ScrollToCaret()
                        UpdateCaretAndNotify(True)
                        Return
                    End If
                End If
            End If

            If Clipboard.ContainsText() Then
                Dim txt As String = Clipboard.GetText()
                If Not String.IsNullOrEmpty(txt) Then
                    If txt.Contains(vbTab) Then
                        Dim righeControllo() As String = txt.Split(New String() {vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        If righeControllo.Length > 0 AndAlso righeControllo(0).Contains(vbTab) Then
                            InserisciTabellaDaTesto(txt)
                            Return
                        End If
                    End If
                    For Each c As Char In txt
                        If c = vbCr Or c = vbLf Then
                            If c = vbCr Then BreakLine()
                        Else
                            InsertChar(c)
                        End If
                    Next
                    Me.Invalidate()
                    _isModified = True
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("PasteText Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante l'incolla: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub InserisciTabellaDaTesto(testo As String)
        Try
            If SolaLettura OrElse String.IsNullOrEmpty(testo) Then Return
            Dim righe() As String = testo.Split(New String() {vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            If righe.Length = 0 Then Return
            
            Dim newTbl As New DocTable()
            For Each riga As String In righe
                Dim colonne() As String = riga.Split(vbTab)
                Dim newRow As New DocTableRow()
                For Each col As String In colonne
                    Dim newCell As New DocTableCell()
                    Dim p As New DocParagraph()
                    If p.Runs IsNot Nothing Then
                        p.Runs.Add(New DocRun() With {.Text = col})
                    End If
                    If newCell.Paragraphs IsNot Nothing Then
                        newCell.Paragraphs.Add(p)
                    End If
                    If newRow.Cells IsNot Nothing Then
                        newRow.Cells.Add(newCell)
                    End If
                Next
                If newTbl.Rows IsNot Nothing Then
                    newTbl.Rows.Add(newRow)
                End If
            Next
            
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    If _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements.Insert(_caretIndex, newTbl)
                    Else
                        _elements.Add(newTbl)
                        _caretIndex = _elements.Count - 1
                    End If
                End If
            End SyncLock
            
            _tblRow = 0
            _tblCol = 0
            _tblPara = 0
            _caretCharIndex = 0

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
            logger.ScriviLog("InserisciTabellaDaTesto Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in InserisciTabellaDaTesto: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Friend Sub SelectAll()
        Try
            _selectionActive = True
            _selAnchorIndex = 0
            _selAnchorSubIndex = 0
            _selAnchorCharIndex = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _elements.Count > 0 Then
                    _caretIndex = _elements.Count - 1
                    Dim lastEl As DocElement = _elements(_caretIndex)
                    If TypeOf lastEl Is DocParagraph Then
                        _caretCharIndex = DirectCast(lastEl, DocParagraph).GetTextLength()
                    ElseIf TypeOf lastEl Is DocTable Then
                        Dim t As DocTable = DirectCast(lastEl, DocTable)
                        If t.Rows IsNot Nothing AndAlso t.Rows.Count > 0 Then
                            _tblRow = t.Rows.Count - 1
                            Dim row As DocTableRow = t.Rows(_tblRow)
                            If row.Cells IsNot Nothing AndAlso row.Cells.Count > 0 Then
                                _tblCol = row.Cells.Count - 1
                                Dim c As DocTableCell = row.Cells(_tblCol)
                                If c.Paragraphs IsNot Nothing AndAlso c.Paragraphs.Count > 0 Then
                                    _tblPara = c.Paragraphs.Count - 1
                                    _caretCharIndex = c.Paragraphs(_tblPara).GetTextLength()
                                End If
                            End If
                        End If
                    End If
                End If
            End SyncLock
            Me.Invalidate()
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto("Selezionato tutto")
        Catch ex As Exception
            logger.ScriviLog("SelectAll Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore durante la selezione: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub ApplicaNuovoDocumento()
        Try
            If Not Me.IsDisposed Then
                If Me.IsHandleCreated Then
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                    UpdateCaretAndNotify(False)
                    SetFocus(Me.Handle)
                End If
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(_docTitle & " aperto")
            End If
        Catch ex As Exception
            logger.ScriviLog("ApplicaNuovoDocumento Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in ApplicaNuovoDocumento: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Sub ApplicaDocumentoCaricato()
        Try
            If Not Me.IsDisposed Then
                SyncLock _elementsLock
                    If _pendingDocumentState IsNot Nothing Then
                        _documentState = _pendingDocumentState
                        _pendingDocumentState = Nothing
                    End If

                    _elements = _documentState.Elements
                    If _elements Is Nothing Then _elements = New List(Of DocElement)()
                    If _elements.Count = 0 Then
                        Dim p As New DocParagraph()
                        If p.Runs IsNot Nothing Then
                            p.Runs.Add(New DocRun() With {.Text = ""})
                        End If
                        _elements.Add(p)
                        _documentState.Elements = _elements
                    End If
                End SyncLock

                _stiliTitolo.Clear()
                If _documentState.StiliTitolo IsNot Nothing Then
                    For Each kvp In _documentState.StiliTitolo
                        _stiliTitolo(kvp.Key) = kvp.Value
                    Next
                End If
                _commentiPerParagrafo.Clear()
                If _documentState.CommentiPerParagrafo IsNot Nothing Then
                    For Each kvp In _documentState.CommentiPerParagrafo
                        _commentiPerParagrafo(kvp.Key) = kvp.Value
                    Next
                End If

                Try
                    HeaderText = If(_documentState.HeaderText, "")
                    FooterText = If(_documentState.FooterText, "")
                Catch exIntestazione As Exception
                    logger.ScriviLog("ApplicaDocumentoCaricato intestazione/piè di pagina: " & exIntestazione.Message)
                End Try

                _caretIndex = 0
                _caretCharIndex = 0
                _vScrollPos = 0
                _isModified = False
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.ImpostaConfineVerticaleConfermato(-1)
                TriggerLayout()
            
                If Me.IsHandleCreated Then
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                    SetFocus(Me.Handle)
                End If

                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiTesto(_docTitle & " aperto")

                UpdateCaretAndNotify(False)
            End If
        Catch ex As Exception
            logger.ScriviLog("ApplicaDocumentoCaricato Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in ApplicaDocumentoCaricato: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

    Private Function IsStopThreadsRequested() As Boolean
        Try
            Return _stopThreads
        Catch ex As Exception
            logger.ScriviLog("IsStopThreadsRequested Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Private Sub LoadDocxInternal(state As Object)
        Try
            Dim filePath As String = CStr(state)
            Dim func As Func(Of Boolean) = AddressOf IsStopThreadsRequested

            Dim nuovoStato As WGDocumentState = WGWordUtils.CaricaStato(filePath, func)

            If nuovoStato IsNot Nothing AndAlso nuovoStato.Elements IsNot Nothing Then
                While nuovoStato.Elements.Count > 0
                    Dim first = nuovoStato.Elements(0)
                    If TypeOf first Is DocParagraph Then
                        Dim p = DirectCast(first, DocParagraph)
                        Dim txt = p.GetText()
                        Dim hasImg As Boolean = False
                        If p.Runs IsNot Nothing Then
                            For Each r In p.Runs
                                If r IsNot Nothing AndAlso r.IsImage Then hasImg = True
                            Next
                        End If
                        If String.IsNullOrWhiteSpace(txt) AndAlso Not hasImg Then
                            nuovoStato.Elements.RemoveAt(0)
                        Else
                            Exit While
                        End If
                    Else
                        Exit While
                    End If
                End While
            End If

            If Not _stopThreads AndAlso Me.IsHandleCreated Then
                _pendingDocumentState = nuovoStato
                Me.Invoke(New MethodInvoker(AddressOf ApplicaDocumentoCaricato))
            End If
        Catch ex As Exception
            Try
                If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                    Me.Invoke(New MethodInvoker(Sub() logger.ScriviLog("Thread Load Error: " & ex.Message & Environment.NewLine & ex.StackTrace)))
                    Me.Invoke(New MethodInvoker(Sub()
                                                    Dim messaggi As New WGMessaggi()
                                                    messaggi.messaggio("Errore nel caricamento del documento: " & ex.Message)
                                                    messaggi.ShowDialog()
                                                End Sub))
                End If
            Catch innerEx As Exception
                Try
                    logger.ScriviLog("Thread Load Error (fallback diretto): " & ex.Message & Environment.NewLine & ex.StackTrace & " | Invoke fallito: " & innerEx.Message)
                Catch
                End Try
            End Try
        End Try
    End Sub

    Private Sub ImpostaNonModificato()
        Try
            _isModified = False
        Catch ex As Exception
            logger.ScriviLog("ImpostaNonModificato Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SaveDocxInternal(state As Object)
        Try
            Dim outputPath As String = CStr(state)

            Dim localState As WGDocumentState = Nothing
            SyncLock _elementsLock
                If _documentState IsNot Nothing Then
                    localState = New WGDocumentState()
                    localState.ImmaginiMedia = _documentState.ImmaginiMedia
                    localState.MappaRelazioni = _documentState.MappaRelazioni
                    localState.SourcePath = _documentState.SourcePath
                    localState.PageSettings = If(_documentState.PageSettings IsNot Nothing, New Dictionary(Of String, Integer)(_documentState.PageSettings), New Dictionary(Of String, Integer)())
                    localState.HeaderText = HeaderText
                    localState.FooterText = FooterText

                    If _stiliTitolo IsNot Nothing Then
                        localState.StiliTitolo = New Dictionary(Of String, Integer)(_stiliTitolo)
                    End If
                    If _commentiPerParagrafo IsNot Nothing Then
                        localState.CommentiPerParagrafo = New Dictionary(Of String, List(Of String))()
                        For Each kvp In _commentiPerParagrafo
                            localState.CommentiPerParagrafo(kvp.Key) = New List(Of String)(kvp.Value)
                        Next
                    End If

                    localState.Elements = New List(Of DocElement)()
                    If _elements IsNot Nothing Then
                        For Each el In _elements
                            If el IsNot Nothing Then localState.Elements.Add(el)
                        Next
                    End If
                End If
            End SyncLock

            If localState IsNot Nothing AndAlso localState.Elements IsNot Nothing Then
                WGWordUtils.SalvaDocumentoCompleto(
                    localState.Elements,
                    outputPath,
                    localState.ImmaginiMedia,
                    localState.StiliTitolo,
                    localState.CommentiPerParagrafo,
                    localState.PageSettings,
                    localState.SourcePath,
                    localState.HeaderText,
                    localState.FooterText)
            End If
            
            _sqlite.Elimina(_docTitle)
            If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                Me.Invoke(New MethodInvoker(AddressOf ImpostaNonModificato))
            End If

        Catch ex As Exception
            Try
                If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                    Me.Invoke(New MethodInvoker(Sub() logger.ScriviLog("SaveDocxInternal: " & ex.Message & Environment.NewLine & ex.StackTrace)))
                    Me.Invoke(New MethodInvoker(Sub()
                                                    Dim messaggi As New WGMessaggi()
                                                    messaggi.messaggio("Errore nel salvataggio: " & ex.Message)
                                                    messaggi.ShowDialog()
                                                End Sub))
                End If
            Catch innerEx As Exception
                Try
                    logger.ScriviLog("SaveDocxInternal (fallback diretto): " & ex.Message & Environment.NewLine & ex.StackTrace & " | Invoke fallito: " & innerEx.Message)
                Catch
                End Try
            End Try
        End Try
    End Sub

    Friend Sub LoadTableState(tbl As DocTable)
        Try
            If tbl IsNot Nothing Then
                _tblRow = tbl.SavedRow
                _tblCol = tbl.SavedCol
                _tblPara = tbl.SavedPara
                _caretCharIndex = tbl.SavedCharIndex
            End If
        Catch ex As Exception
            logger.ScriviLog("LoadTableState Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim messaggi As New WGMessaggi()
            messaggi.messaggio("Errore in LoadTableState: " & ex.Message)
            messaggi.ShowDialog()
        End Try
    End Sub

End Class