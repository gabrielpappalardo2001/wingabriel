Imports System

''' <summary>
''' Il registro degli errori della libreria dei giochi: ogni funzione che ha un Try/Catch chiama Registra e rilancia, così nel registro resta la funzione
''' dove l'errore è nato (una volta sola, anche se l'eccezione risale tra più funzioni). Le eccezioni previste dal gioco (una mossa non ammessa,
''' un errore del server già spiegato all'utente, un'operazione annullata) non sono errori e non si registrano.
''' Nel registro entrano solo la funzione e l'errore tecnico, mai il contenuto dei messaggi o dei dati dei giocatori.
''' </summary>
Friend Module RegistroErrori

    Private ReadOnly _logger As New wgcontrols.Logger()

    Friend Sub Registra(dove As String, ex As Exception)
        Try
            If ex Is Nothing Then Return
            If TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is MossaNonConsentita OrElse TypeOf ex Is GiochiErrore Then Return
            If ex.Data.Contains("wgLog") Then Return
            ex.Data("wgLog") = True
            _logger.ScriviLog(dove & ": " & ex.ToString())
        Catch
            ' il registro non deve mai far fallire quello che sta registrando
        End Try
    End Sub

End Module
