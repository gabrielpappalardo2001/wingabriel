Imports System

''' <summary>
''' Il registro degli errori di WGDb (cifratura nativa): ogni funzione con Try/Catch chiama Registra e rilancia, così nel registro resta la funzione dove
''' l'errore è nato, una volta sola. Nel registro entrano solo la funzione e l'errore tecnico: mai testi cifrati, password, chiavi o altri dati.
''' </summary>
Friend Module RegistroErrori

    Private ReadOnly _logger As New LoggerWGDb()

    Friend Sub Registra(dove As String, ex As Exception)
        Try
            If ex Is Nothing Then Return
            If TypeOf ex Is OperationCanceledException Then Return
            If ex.Data.Contains("wgLog") Then Return
            ex.Data("wgLog") = True
            _logger.ScriviLog(dove & ": " & ex.GetType().Name & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch
            ' il registro non deve mai far fallire quello che sta registrando
        End Try
    End Sub

End Module
