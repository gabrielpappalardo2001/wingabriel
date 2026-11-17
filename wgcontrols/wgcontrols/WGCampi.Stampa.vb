Imports System.Collections.Generic
Imports WGStampante

Partial public Class Campi
    Private Sub StampaCampoCorrente()
        Try
            If _campoConFocusMenu IsNot Nothing AndAlso _campoConFocusMenu.IsCampoPassword Then
                Dim msgPassword As New WGMessaggi()
                msgPassword.Messaggio("Non si stampa un campo password.")
                msgPassword.ShowDialog()
                Return
            End If
            Dim nomeStampante As String = New WGImpostazioniComuni().NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                msgManca.ShowDialog()
                Return
            End If
            StampanteCampiSelezionata(nomeStampante)
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.StampaCampoCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaStampantiCampiEsc()
        Try
            If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ListaStampantiCampiEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteCampiSelezionata(nomeStampante As String)
        Try
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            Dim ok As Boolean = False
            Dim messaggioStato As String = ""
            Dim messaggioErrore As String = ""
            Dim editorWord As WGWord = TryCast(_controlloConFocusMenu, WGWord)
            Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
            If editorWord IsNot Nothing AndAlso Not editorWord.IsDisposed Then
                ok = editorWord.Stampa(nomeStampante, "Documento")
                If editorWord.UltimoEsitoStampa IsNot Nothing Then
                    messaggioErrore = editorWord.UltimoEsitoStampa.Errore
                    messaggioStato = editorWord.UltimoEsitoStampa.StatoFinale
                End If
                If Not ok AndAlso String.IsNullOrWhiteSpace(messaggioErrore) Then messaggioErrore = Global.WGStampante.WGStampante.UltimoErrore()
            ElseIf _campoConFocusMenu IsNot Nothing Then
                Dim testo As String = _campoConFocusMenu.Text
                Dim blocco As New WGBloccoStampa()
                blocco.Tipo = "testo"
                blocco.Testo = If(testo, "")
                Dim elenco As New List(Of WGBloccoStampa) From {blocco}
                Dim esito As Global.WGStampante.WGEsitoStampa = Global.WGStampante.WGStampante.StampaBlocchi(elenco, nomeStampante, "Campo", impostazioniStampa, True)
                ok = esito IsNot Nothing AndAlso esito.Riuscita
                If esito IsNot Nothing Then
                    messaggioErrore = esito.Errore
                    messaggioStato = esito.StatoFinale
                End If
            Else
                ImpostaMessaggioStampaFallita("Non c'è nulla da stampare.")
                If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
                Return
            End If
            If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
            Dim msg As New WGMessaggi()
            If ok Then
                If Not String.IsNullOrWhiteSpace(messaggioStato) Then
                    msg.Messaggio(messaggioStato & ".")
                Else
                    msg.Messaggio("Stampa avviata.")
                End If
            Else
                Dim errore As String = messaggioErrore
                If String.IsNullOrWhiteSpace(errore) Then errore = "La stampa non è riuscita."
                msg.Messaggio(errore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.StampanteCampiSelezionata: " & ex.Message)
            Try
                If _listaStampantiCampi IsNot Nothing Then _listaStampantiCampi.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ImpostaMessaggioStampaFallita(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("CampiFormInterno.ImpostaMessaggioStampaFallita: " & ex.Message)
        End Try
    End Sub
End Class