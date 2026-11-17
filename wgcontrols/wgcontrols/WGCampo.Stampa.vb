Imports System.IO
Imports WGStampante

' Funzionalità di stampa e conversione in formato MP3 per i singoli campi dei moduli.
Partial Public Class WGCampo
    Private _listaStampantiCampo As WGMenuLista
    Private _dialogoConversioneMp3 As WGFileDialog
    Private _campiNomeMp3 As Campi
    Private _cartellaMp3 As String = ""

    Private Sub StampaCampo(sender As Object, e As EventArgs)
        Try
            Dim nomeStampante As String = New WGImpostazioniComuni().NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                msgManca.ShowDialog()
                Return
            End If
            StampanteCampoSelezionata(nomeStampante)
        Catch ex As Exception
            log.ScriviLog("WGCampo.StampaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaStampantiCampoEsc()
        Try
            If _listaStampantiCampo IsNot Nothing Then _listaStampantiCampo.abbandona()
        Catch ex As Exception
            log.ScriviLog("WGCampo.ListaStampantiCampoEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteCampoSelezionata(nomeStampante As String)
        Try
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            Dim ok As Boolean = False
            Dim messaggioStato As String = ""
            Dim messaggioErrore As String = ""
            Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                ok = campoWord.Stampa(nomeStampante, titolo)
                If campoWord.UltimoEsitoStampa IsNot Nothing Then
                    messaggioErrore = campoWord.UltimoEsitoStampa.Errore
                    messaggioStato = campoWord.UltimoEsitoStampa.StatoFinale
                End If
                If Not ok AndAlso String.IsNullOrWhiteSpace(messaggioErrore) Then messaggioErrore = Global.WGStampante.WGStampante.UltimoErrore()
            Else
                Dim testo As String = ottienitesto()
                Dim blocco As New WGBloccoStampa()
                blocco.Tipo = "testo"
                blocco.Testo = If(testo, "")
                Dim elenco As New List(Of WGBloccoStampa) From {blocco}
                Dim esito As Global.WGStampante.WGEsitoStampa = Global.WGStampante.WGStampante.StampaBlocchi(elenco, nomeStampante, titolo, impostazioniStampa, True)
                ok = esito IsNot Nothing AndAlso esito.Riuscita
                If esito IsNot Nothing Then
                    messaggioErrore = esito.Errore
                    messaggioStato = esito.StatoFinale
                End If
            End If
            If _listaStampantiCampo IsNot Nothing Then _listaStampantiCampo.abbandona()
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
            log.ScriviLog("WGCampo.StampanteCampoSelezionata: " & ex.Message)
            Try
                If _listaStampantiCampo IsNot Nothing Then _listaStampantiCampo.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ConvertiCampoInMp3(sender As Object, e As EventArgs)
        Try
            Dim testo As String = OttieniTestoPerMp3()
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun testo da convertire in mp3.")
                msg.ShowDialog()
                Return
            End If
            _dialogoConversioneMp3 = New WGFileDialog()
            _dialogoConversioneMp3.MultiselezioneConsentita = False
            AddHandler _dialogoConversioneMp3.singolaselezione, AddressOf CartellaMp3Scelta
            _dialogoConversioneMp3.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGCampo.ConvertiCampoInMp3: " & ex.Message)
        End Try
    End Sub

    Private Sub CartellaMp3Scelta(cartella As String)
        Try
            _cartellaMp3 = cartella
            _campiNomeMp3 = New Campi()
            _campiNomeMp3.Aggiungi("Scrivi il nome del file mp3", "v.nomemp3", True)
            AddHandler _campiNomeMp3.Fine, AddressOf NomeMp3Inserito
            _campiNomeMp3.avvia()
        Catch ex As Exception
            log.ScriviLog("WGCampo.CartellaMp3Scelta: " & ex.Message)
        End Try
    End Sub

    Private Sub NomeMp3Inserito()
        Try
            Dim nome As String = ""
            If _campiNomeMp3 IsNot Nothing Then
                nome = _campiNomeMp3.RecuperaValore("v.nomemp3")

                _campiNomeMp3 = Nothing
            End If
            If String.IsNullOrWhiteSpace(nome) Then Return
            If Not nome.ToLowerInvariant().EndsWith(".mp3") Then nome &= ".mp3"
            Dim destinazione As String = Path.Combine(_cartellaMp3, nome)
            Dim testo As String = OttieniTestoPerMp3()
            If String.IsNullOrWhiteSpace(testo) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun testo da convertire in mp3.")
                msg.ShowDialog()
                Return
            End If
            WGCentroDiControllo.GetInstance().AggiungiConversioneMp3DaTesto(testo, destinazione)
        Catch ex As Exception
            log.ScriviLog("WGCampo.NomeMp3Inserito: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniTestoPerMp3() As String
        Try
            If isWordMode AndAlso campoWord IsNot Nothing AndAlso Not campoWord.IsDisposed Then
                Return campoWord.OttieniTestoPerSintesi()
            End If
            Return ottienitesto()
        Catch ex As Exception
            log.ScriviLog("WGCampo.OttieniTestoPerMp3: " & ex.Message)
            Return ""
        End Try
    End Function
End Class