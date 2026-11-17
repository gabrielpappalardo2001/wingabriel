Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

Partial Public Class WGWord
    Public Function OttieniSezioneCorrenteIndice() As String
        Try
            Dim bkmk As String = ""
            SyncLock _elementsLock

                If _elements Is Nothing Then Return ""
                Dim startI = Math.Min(_elements.Count - 1, Math.Max(0, _caretIndex))
                For i As Integer = startI To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then

                        Dim p = DirectCast(_elements(i), DocParagraph)
                        If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso Not p.IsIndexContainer Then
                            bkmk = p.BookmarkId
                            Exit For

                        End If
                    End If
                Next

                If Not String.IsNullOrEmpty(bkmk) Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph AndAlso DirectCast(el, DocParagraph).IsIndexContainer Then
                            Dim contenitore As DocParagraph = DirectCast(el, DocParagraph)
                            If contenitore.Runs Is Nothing Then Continue For
                            For Each r In contenitore.Runs
                                If r IsNot Nothing AndAlso r.LinkUrl = bkmk Then
                                    Return If(r.Text, "")
                                End If

                            Next
                        End If
                    Next
                End If
            End SyncLock
            Return "Il cursore non si trova all'interno di una sezione indicizzata."

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Public Sub VaiASezioneSuccessivaIndice()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return

                Dim startI = Math.Max(0, _caretIndex + 1)

                For i As Integer = startI To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then

                        Dim p = DirectCast(_elements(i), DocParagraph)
                        If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso Not p.IsIndexContainer Then
                            _caretIndex = i

                            _caretCharIndex = 0
                            _selectionActive = False
                            TriggerLayout()
                            ScrollToCaret()

                            UpdateCaretAndNotify(False)
                            Dim sezione As String = OttieniSezioneCorrenteIndice()
                            MostraMessaggioStato("Ti trovi nella sezione: " & sezione)
                            Return
                        End If

                    End If
                Next
            End SyncLock
            MostraMessaggioStato("Nessuna sezione successiva.")
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiASezionePrecedenteIndice()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                Dim startI = Math.Min(_elements.Count - 1, Math.Max(0, _caretIndex - 1))

                For i As Integer = startI To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p = DirectCast(_elements(i), DocParagraph)

                        If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso Not p.IsIndexContainer Then
                            _caretIndex = i
                            _caretCharIndex = 0
                            _selectionActive = False

                            TriggerLayout()
                            ScrollToCaret()
                            UpdateCaretAndNotify(False)

                            Dim sezione As String = OttieniSezioneCorrenteIndice()
                            MostraMessaggioStato("Ti trovi nella sezione: " & sezione)
                            Return
                        End If
                    End If
                Next

            End SyncLock
            MostraMessaggioStato("Nessuna sezione precedente.")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuGestioneIndice()

        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _menuGestioneIndice IsNot Nothing Then
                _menuGestioneIndice.abbandona()
                _menuGestioneIndice = Nothing
            End If


            _menuGestioneIndice = New WGMenuLista()
            _menuGestioneIndice.impostaTitolo("Gestione Indice")
            _menuGestioneIndice.multiselezioneConsentita = False

            Dim vociTrovate As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then

                    For i As Integer = 0 To _elements.Count - 1
                        If TypeOf _elements(i) Is DocParagraph Then
                            Dim p = DirectCast(_elements(i), DocParagraph)

                            If p.IsIndexContainer AndAlso p.Runs IsNot Nothing Then
                                For Each r In p.Runs
                                    If r IsNot Nothing AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso If(r.Text, "").Trim() <> "INDICE:" Then

                                        _menuGestioneIndice.aggiungiElemento(If(r.Text, "").Trim(), r.LinkUrl)
                                        vociTrovate += 1

                                    End If
                                Next
                            End If
                        End If

                    Next
                End If
            End SyncLock

            If vociTrovate = 0 Then
                MostraMessaggioStato("L'indice non contiene voci o non esiste.")
                Return
            End If

            _menuGestioneIndice.aggiungiOpzione("Vai alla sezione", AddressOf AzioneVaiASezioneMenu)
            If Not SolaLettura Then _menuGestioneIndice.aggiungiOpzione("Elimina voce dall'indice", AddressOf AzioneEliminaVoceMenu)


            AddHandler _menuGestioneIndice.ElementoSelezionato, AddressOf AzioneVaiASezioneMenu

            _menuGestioneIndice.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AzioneVaiASezioneMenu()
        Try
            If _menuGestioneIndice IsNot Nothing Then
                Dim ids = _menuGestioneIndice.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    VaiABookmarkConFruscio(ids(0))
                End If
                _menuGestioneIndice.abbandona()

                _menuGestioneIndice = Nothing
            End If
        Catch ex As Exception
logger.ScriviLog("AzioneVaiASezioneMenu: " & ex.ToString())
        End Try
    End Sub

    Private Sub AzioneEliminaVoceMenu()
        Try
            If _menuGestioneIndice IsNot Nothing Then
                Dim ids = _menuGestioneIndice.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    RimuoviVoceDaIndice(ids(0))
                End If
                _menuGestioneIndice.abbandona()
                _menuGestioneIndice = Nothing

            End If
        Catch ex As Exception
logger.ScriviLog("AzioneEliminaVoceMenu: " & ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviVoceDaIndice(bookmarkId As String)
        Try
            If SolaLettura Then Return
            Dim trovato As Boolean = False
            SyncLock _elementsLock

                If _elements Is Nothing Then Return
                For i As Integer = _elements.Count - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsIndexContainer Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If p.Runs Is Nothing Then Continue For
                        For j As Integer = p.Runs.Count - 1 To 0 Step -1
                            If p.Runs(j) IsNot Nothing AndAlso p.Runs(j).LinkUrl = bookmarkId Then

                                p.Runs.RemoveAt(j)
                                trovato = True
                            End If
                        Next

                        If p.Runs.Count = 0 OrElse (p.Runs.Count = 1 AndAlso p.Runs(0) IsNot Nothing AndAlso p.Runs(0).Text IsNot Nothing AndAlso p.Runs(0).Text.Trim() = "INDICE:") Then
                            _elements.RemoveAt(i)
                        End If
                    End If

                Next

                For Each el In _elements
                    If TypeOf el Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(el, DocParagraph)
                        If p.BookmarkId = bookmarkId Then p.BookmarkId = ""
                    End If
                Next
            End SyncLock

            If trovato Then

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Voce rimossa dall'indice.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RimuoviVoceCorrenteDaIndice()
        Try
            If SolaLettura Then Return
            Dim bkmk As String = ""
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)
                    If TypeOf el Is DocParagraph Then

                        bkmk = DirectCast(el, DocParagraph).BookmarkId
                    End If
                End If
            End SyncLock

            If Not String.IsNullOrEmpty(bkmk) Then

                RimuoviVoceDaIndice(bkmk)
            Else
                MostraMessaggioStato("Il paragrafo corrente non è una voce dell'indice.")
            End If
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub EliminaIndiceCompletamente()
        Try
            If SolaLettura Then Return
            Dim rimossi As Integer = 0
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _elements.Count - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsIndexContainer Then
                        _elements.RemoveAt(i)

                        rimossi += 1
                    End If
                Next

                For Each el In _elements
                    If TypeOf el Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(el, DocParagraph)
                        If p.BookmarkId.StartsWith("IND_") OrElse p.BookmarkId.StartsWith("AUTO_IND_") Then
                            p.BookmarkId = ""

                        End If
                    End If
                Next
                If _caretIndex >= _elements.Count Then _caretIndex = Math.Max(0, _elements.Count - 1)
            End SyncLock


            If rimossi > 0 Then
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("L'intero indice è stato rimosso.")
            Else
                MostraMessaggioStato("Nessun indice presente.")
            End If

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RimuoviRigheVuoteSuperflue()
        Try
            If SolaLettura Then Return
            Dim rimossi As Integer = 0
            SyncLock _elementsLock

                If _elements Is Nothing Then Return
                For i As Integer = _elements.Count - 1 To 1 Step -1
                    If TypeOf _elements(i) Is DocParagraph AndAlso TypeOf _elements(i - 1) Is DocParagraph Then
                        Dim p1 = DirectCast(_elements(i), DocParagraph)
                        Dim p0 = DirectCast(_elements(i - 1), DocParagraph)
                        Dim isP1Empty = (p1.GetTextLength() = 0 AndAlso (p1.Runs Is Nothing OrElse Not p1.Runs.Any(Function(r) r IsNot Nothing AndAlso r.IsImage)))
                        Dim isP0Empty = (p0.GetTextLength() = 0 AndAlso (p0.Runs Is Nothing OrElse Not p0.Runs.Any(Function(r) r IsNot Nothing AndAlso r.IsImage)))


                        If isP1Empty AndAlso isP0Empty AndAlso Not p1.IsPageBreak AndAlso Not p0.IsPageBreak AndAlso Not p1.IsIndexContainer Then
                            _elements.RemoveAt(i)
                            rimossi += 1

                        End If
                    End If
                Next
                If _caretIndex >= _elements.Count Then _caretIndex = Math.Max(0, _elements.Count - 1)
            End SyncLock


            If rimossi > 0 Then
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Rimosse " & rimossi & " righe vuote superflue consecutive.")
            Else
                MostraMessaggioStato("Nessuna riga vuota superflua da rimuovere.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

End Class