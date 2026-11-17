Imports System

''' <summary>
''' Il registro degli errori di Porto Franco: ogni funzione con Try/Catch chiama Registra e rilancia, così nel registro (il log.txt di WinGabriel,
''' scritto dalla classe Logger di wgcontrols) resta la funzione dove l'errore è nato, una volta sola anche se l'eccezione risale tra più funzioni.
''' </summary>
Friend Module RegistroErrori

    Private ReadOnly _logger As New wgcontrols.Logger()

    Friend Sub Registra(dove As String, ex As Exception)
        Try
            If ex Is Nothing Then Return
            If TypeOf ex Is OperationCanceledException Then Return
            If ex.Data.Contains("wgLog") Then Return
            ex.Data("wgLog") = True
            _logger.ScriviLog(dove & ": " & ex.ToString())
        Catch
            ' il registro non deve mai far fallire quello che sta registrando
        End Try
    End Sub

End Module
