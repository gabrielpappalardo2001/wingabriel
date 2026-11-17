Imports WGStampante

' Gestione della stampa e selezione delle stampanti per i campi di testo dedicati.
Partial Public Class WGCampoTesto
    Private Sub StampaCampoTesto(sender As Object, e As EventArgs)
        Try
            Dim testo As String = OttieniTesto()
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Non c'è nulla da stampare.")
                msgVuoto.ShowDialog()
                Return
            End If
            Dim nomeStampante As String = New WGImpostazioniComuni().NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                msgManca.ShowDialog()
                Return
            End If
            StampanteCampoTestoSelezionata(nomeStampante)
        Catch ex As Exception
            log.ScriviLog("WGCampoTesto.StampaCampoTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaStampantiCampoTestoEsc()
        Try
            If _listaStampantiCampoTesto IsNot Nothing Then _listaStampantiCampoTesto.abbandona()
        Catch ex As Exception
            log.ScriviLog("WGCampoTesto.ListaStampantiCampoTestoEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteCampoTestoSelezionata(nomeStampante As String)
        Try
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            Dim testo As String = OttieniTesto()
            Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
            Dim ok As Boolean = Global.WGStampante.WGStampante.StampaTesto(testo, nomeStampante, titolo, impostazioniStampa)
            If _listaStampantiCampoTesto IsNot Nothing Then _listaStampantiCampoTesto.abbandona()
            Dim msg As New WGMessaggi()
            If ok Then
                msg.Messaggio("Stampa avviata.")
            Else
                Dim errore As String = Global.WGStampante.WGStampante.UltimoErrore()
                If String.IsNullOrWhiteSpace(errore) Then errore = "La stampa non è riuscita."
                msg.Messaggio(errore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGCampoTesto.StampanteCampoTestoSelezionata: " & ex.Message)
            Try
                If _listaStampantiCampoTesto IsNot Nothing Then _listaStampantiCampoTesto.abbandona()
            Catch
            End Try
        End Try
    End Sub
End Class
