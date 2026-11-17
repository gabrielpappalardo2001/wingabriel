Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.Text

' Gestione della modalità campo per l'inserimento guidato e la conferma dei dati all'interno del documento.
Partial Public Class WGWord

    Private _modalitaCampo As Boolean = False
    Private _invioConsecutiviCampo As Integer = 0

    Public Event RichiestaCampoAdiacente(sender As Object, direzione As Integer)
    Public Event RichiestaConfermaCampo(sender As Object)
    Public Event RichiestaMenuUscita(sender As Object)

    Public Property ModalitaCampo As Boolean
        Get
            Try
                Return _modalitaCampo
            Catch ex As Exception
                logger.ScriviLog("WGWord.ModalitaCampo Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _modalitaCampo = value
                _invioConsecutiviCampo = 0
            Catch ex As Exception
                logger.ScriviLog("WGWord.ModalitaCampo Set: " & ex.Message)
            End Try
        End Set
    End Property

    Friend Sub NotificaCampoAdiacente(direzione As Integer)
        Try
            If Not _modalitaCampo Then Return
            _invioConsecutiviCampo = 0
            RaiseEvent RichiestaCampoAdiacente(Me, direzione)
        Catch ex As Exception
            logger.ScriviLog("WGWord.NotificaCampoAdiacente: " & ex.Message)
        End Try
    End Sub

    Friend Sub NotificaConfermaCampo()
        Try
            If Not _modalitaCampo Then Return
            RaiseEvent RichiestaConfermaCampo(Me)
        Catch ex As Exception
            logger.ScriviLog("WGWord.NotificaConfermaCampo: " & ex.Message)
        End Try
    End Sub

    Public Sub NotificaMenuUscita()
        Try
            If Not _modalitaCampo Then Return
            _invioConsecutiviCampo = 0
            RaiseEvent RichiestaMenuUscita(Me)
        Catch ex As Exception
            logger.ScriviLog("WGWord.NotificaMenuUscita: " & ex.Message)
        End Try
    End Sub

    Friend Sub AzzeraInviiConsecutiviCampo()
        Try
            _invioConsecutiviCampo = 0
        Catch ex As Exception
            logger.ScriviLog("WGWord.AzzeraInviiConsecutiviCampo: " & ex.Message)
        End Try
    End Sub

    Friend Function RegistraInvioConsecutivoCampo() As Boolean
        Try
            If Not _modalitaCampo Then Return False
            _invioConsecutiviCampo += 1
            If _invioConsecutiviCampo < 3 Then Return False
            _invioConsecutiviCampo = 0
            RimuoviParagrafiVuotiPrimaDellaConferma()
            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.RegistraInvioConsecutivoCampo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ParagrafoSenzaContenuto(para As DocParagraph) As Boolean
        Try
            If para Is Nothing Then Return True
            If para.IsPageBreak Then Return False
            If para.Runs Is Nothing OrElse para.Runs.Count = 0 Then Return True
            For Each r As DocRun In para.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then Return False
                If Not String.IsNullOrEmpty(r.Text) Then Return False
            Next
            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.ParagrafoSenzaContenuto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RimuoviParagrafiVuotiPrimaDellaConferma()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _elements.Count = 0 Then Return
                Dim rimossi As Integer = 0
                While rimossi < 2
                    If _elements.Count <= 1 Then Exit While
                    If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Exit While
                    Dim elCorrente As DocElement = _elements(_caretIndex)
                    If Not (TypeOf elCorrente Is DocParagraph) Then Exit While
                    If Not ParagrafoSenzaContenuto(DirectCast(elCorrente, DocParagraph)) Then Exit While
                    _elements.RemoveAt(_caretIndex)
                    rimossi += 1
                    If _caretIndex > 0 Then _caretIndex -= 1
                End While

                If _caretIndex < 0 Then _caretIndex = 0
                If _caretIndex >= _elements.Count Then _caretIndex = Math.Max(0, _elements.Count - 1)

                Dim elFinale As DocElement = Nothing
                If _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then elFinale = _elements(_caretIndex)
                If TypeOf elFinale Is DocParagraph Then
                    _caretCharIndex = DirectCast(elFinale, DocParagraph).GetTextLength()
                Else
                    _caretCharIndex = 0
                End If
                If _caretCharIndex < 0 Then _caretCharIndex = 0
            End SyncLock

            _selectionActive = False
            Try
                UpdateCaretAndNotify(False)
            Catch exCursore As Exception
                logger.ScriviLog("WGWord.RimuoviParagrafiVuotiPrimaDellaConferma (cursore): " & exCursore.Message)
            End Try
            Try
                Me.Invalidate()
            Catch exDisegno As Exception
                logger.ScriviLog("WGWord.RimuoviParagrafiVuotiPrimaDellaConferma (disegno): " & exDisegno.Message)
            End Try
        Catch ex As Exception
            logger.ScriviLog("WGWord.RimuoviParagrafiVuotiPrimaDellaConferma: " & ex.Message)
        End Try
    End Sub

    Public Function TestoDocumento() As String
        Try
            Dim sb As New StringBuilder()
            SyncLock _elementsLock
                If _elements Is Nothing Then Return ""
                For Each el As DocElement In _elements
                    If TypeOf el Is DocParagraph Then
                        sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                    ElseIf TypeOf el Is DocTable Then
                        Dim tabella As DocTable = DirectCast(el, DocTable)
                        If tabella.Rows Is Nothing Then Continue For
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
                    End If
                Next
            End SyncLock
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.TestoDocumento: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function DocumentoVuoto() As Boolean
        Try
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _elements.Count = 0 Then Return True
                For Each el As DocElement In _elements
                    If TypeOf el Is DocTable Then Return False
                    If TypeOf el Is DocParagraph Then
                        If Not ParagrafoSenzaContenuto(DirectCast(el, DocParagraph)) Then Return False
                    End If
                Next
            End SyncLock
            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.DocumentoVuoto: " & ex.Message)
            Return False
        End Try
    End Function

End Class
