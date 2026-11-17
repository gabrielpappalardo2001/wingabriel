' ============================================================================================
' WGWord.Braille - il documento Word letto e scritto dalla barra braille
' ============================================================================================
'
' La barra mostra il paragrafo in cui si trova il cursore (anche dentro una cella di tabella) con il cursore al
' suo posto; i punti battuti scrivono come la tastiera (con l'eco di lettura), l'instradamento sposta il cursore
' dentro il paragrafo.
' ============================================================================================
Partial Public Class WGWord
    Implements IWGBrailleCampo
    Implements IWGBrailleCampoAttributi

    Public ReadOnly Property BrailleModificabile As Boolean Implements IWGBrailleCampo.BrailleModificabile
        Get
            Return Not Me.SolaLettura
        End Get
    End Property

    Public Function BrailleStato(ByRef riga As String, ByRef cursore As Integer) As Boolean Implements IWGBrailleCampo.BrailleStato
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) OrElse para Is Nothing Then Return False
            riga = para.GetText()
            If riga Is Nothing Then riga = ""
            cursore = Math.Max(0, Math.Min(_caretCharIndex, riga.Length))
            Return True
        Catch ex As Exception
            logger.ScriviLog("WGWord.BrailleStato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub BrailleInserisci(testo As String) Implements IWGBrailleCampo.BrailleInserisci
        Try
            If Me.IsDisposed OrElse Me.SolaLettura OrElse String.IsNullOrEmpty(testo) Then Return
            For Each c As Char In testo
                InserisciCarattereConLettura(c)
            Next
        Catch ex As Exception
            logger.ScriviLog("WGWord.BrailleInserisci: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Grassetto, corsivo e sottolineato di ogni carattere del paragrafo mostrato sulla barra (bit 1, 2, 4).</summary>
    Public Function BrailleAttributi() As Byte() Implements IWGBrailleCampoAttributi.BrailleAttributi
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) OrElse para Is Nothing OrElse para.Runs Is Nothing Then Return Nothing
            Dim lunghezza As Integer = para.GetTextLength()
            If lunghezza <= 0 Then Return Nothing
            Dim attributi(lunghezza - 1) As Byte
            Dim indice As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage OrElse r.Text Is Nothing Then Continue For
                Dim valore As Byte = 0
                If r.Bold Then valore = valore Or 1
                If r.Italic Then valore = valore Or 2
                If r.Underline Then valore = valore Or 4
                For i As Integer = 0 To r.Text.Length - 1
                    If indice < attributi.Length Then attributi(indice) = valore
                    indice += 1
                Next
            Next
            Return attributi
        Catch ex As Exception
            logger.ScriviLog("WGWord.BrailleAttributi: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub BrailleSposta(posizioneNellaRiga As Integer) Implements IWGBrailleCampo.BrailleSposta
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) OrElse para Is Nothing Then Return
            Dim testo As String = para.GetText()
            If testo Is Nothing Then testo = ""
            SyncLock _elementsLock
                _caretCharIndex = Math.Max(0, Math.Min(posizioneNellaRiga, testo.Length))
                _selectionActive = False
            End SyncLock
            Me.Invalidate()
            UpdateCaretAndNotify(True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.BrailleSposta: " & ex.Message)
        End Try
    End Sub

End Class
