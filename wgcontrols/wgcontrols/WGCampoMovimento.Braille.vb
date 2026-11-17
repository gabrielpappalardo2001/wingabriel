' ============================================================================================
' WGCampoMovimento.Braille - il campo di testo semplice letto e scritto dalla barra braille
' ============================================================================================
Partial Public Class WGCampoMovimento
    Implements IWGBrailleCampo

    Public ReadOnly Property BrailleModificabile As Boolean Implements IWGBrailleCampo.BrailleModificabile
        Get
            Return Not Me.ReadOnly
        End Get
    End Property

    Public Function BrailleStato(ByRef riga As String, ByRef cursore As Integer) As Boolean Implements IWGBrailleCampo.BrailleStato
        Try
            WGBraille.RigaDaTesto(Me.Text, Me.SelectionStart, riga, cursore)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.BrailleStato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub BrailleInserisci(testo As String) Implements IWGBrailleCampo.BrailleInserisci
        Try
            If Me.ReadOnly OrElse String.IsNullOrEmpty(testo) Then Return
            For Each c As Char In testo
                InserisciCarattereConLettura(c)
            Next
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.BrailleInserisci: " & ex.Message)
        End Try
    End Sub

    Public Sub BrailleSposta(posizioneNellaRiga As Integer) Implements IWGBrailleCampo.BrailleSposta
        Try
            Dim testo As String = Me.Text
            Dim inizio As Integer = Math.Min(Me.SelectionStart, testo.Length)
            While inizio > 0 AndAlso testo(inizio - 1) <> ControlChars.Lf
                inizio -= 1
            End While
            Dim fine As Integer = inizio
            While fine < testo.Length AndAlso testo(fine) <> ControlChars.Lf
                fine += 1
            End While
            Me.SelectionLength = 0
            Me.SelectionStart = Math.Max(inizio, Math.Min(fine, inizio + posizioneNellaRiga))
        Catch ex As Exception
            log.ScriviLog("WGCampoMovimento.BrailleSposta: " & ex.Message)
        End Try
    End Sub

End Class
