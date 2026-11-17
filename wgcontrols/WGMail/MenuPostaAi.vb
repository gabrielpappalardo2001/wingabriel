Imports System
Imports System.Collections.Generic
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

' Integrazione dell'intelligenza artificiale per la posta elettronica. Permette di generare riassunti dei messaggi e risposte
' automatiche sia dalla lista che in lettura.
Partial Public Class MenuPosta

    Private Enum ScopoIstruzioniAi
        Nessuno
        Risposta
        Composizione
    End Enum

    Private Const LunghezzaMassimaTestoPerAi As Integer = 60000

    Private _campiIstruzioniAi As Campi
    Private _campoRiassuntoAi As WGCampo
    Private _scopoIstruzioniAi As ScopoIstruzioniAi = ScopoIstruzioniAi.Nessuno
    Private _rispostaAiATutti As Boolean = False
    Private _idOperazioneAi As Integer = 0

    Friend Function IntelligenzaArtificialeDisponibile() As Boolean
        Try
            Return WGAi.AlmenoUnProviderConfigurato()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.IntelligenzaArtificialeDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiungiOpzioniAiLista(lista As WGMenuLista)
        Try
            If lista Is Nothing Then Return
            If Not IntelligenzaArtificialeDisponibile() Then Return
            lista.aggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiAiDaLista)
            lista.aggiungiOpzione("Rispondi con l'intelligenza artificiale", AddressOf OpzioneRispondiAiDaLista)
            lista.aggiungiOpzione("Rispondi a tutti con l'intelligenza artificiale", AddressOf OpzioneRispondiTuttiAiDaLista)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiungiOpzioniAiLista: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiOpzioniAiCampo(campo As WGCampo)
        Try
            If campo Is Nothing Then Return
            If Not IntelligenzaArtificialeDisponibile() Then Return
            campo.aggiungiopzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiAi)
            campo.aggiungiopzione("Rispondi con l'intelligenza artificiale", AddressOf OpzioneRispondiAi)
            campo.aggiungiopzione("Rispondi a tutti con l'intelligenza artificiale", AddressOf OpzioneRispondiTuttiAi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiungiOpzioniAiCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiOpzioniAiLettura(campi As Campi)
        Try
            If campi Is Nothing Then Return
            If Not IntelligenzaArtificialeDisponibile() Then Return
            campi.AggiungiOpzione("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiAi)
            campi.AggiungiOpzione("Rispondi con l'intelligenza artificiale", AddressOf OpzioneRispondiAi)
            campi.AggiungiOpzione("Rispondi a tutti con l'intelligenza artificiale", AddressOf OpzioneRispondiTuttiAi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AggiungiOpzioniAiLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRiassumiAiDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.RiassumiAi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRiassumiAiDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiAiDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.RispondiAi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiAiDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiTuttiAiDaLista(sender As Object, e As EventArgs)
        Try
            PreparaAzioneSuMessaggioInLista(AzioneSuMessaggio.RispondiTuttiAi)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiTuttiAiDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRiassumiAi(sender As Object, e As EventArgs)
        Try
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            AvviaRiassuntoAi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRiassumiAi: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiAi(sender As Object, e As EventArgs)
        Try
            ChiudiCampoMessaggio()
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            ChiediIstruzioniRispostaAi(False)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiAi: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRispondiTuttiAi(sender As Object, e As EventArgs)
        Try
            ChiudiCampoMessaggio()
            If _messaggioCorrente Is Nothing Then
                MostraMessaggio("Apri prima il messaggio.")
                Return
            End If
            ChiediIstruzioniRispostaAi(True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRispondiTuttiAi: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneScriviTestoAi(sender As Object, e As EventArgs)
        Try
            If _campiComposizione Is Nothing Then Return
            SalvaDatiComposizioneDaCampi()
            _scopoIstruzioniAi = ScopoIstruzioniAi.Composizione
            ApriCampiIstruzioniAi("Cosa vuoi scrivere, in poche parole")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneScriviTestoAi: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediIstruzioniRispostaAi(aTutti As Boolean)
        Try
            If _messaggioCorrente Is Nothing Then Return
            _rispostaAiATutti = aTutti
            _scopoIstruzioniAi = ScopoIstruzioniAi.Risposta
            ApriCampiIstruzioniAi("Cosa vuoi rispondere, in poche parole")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiediIstruzioniRispostaAi: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiIstruzioniAi(etichetta As String)
        Try
            RilasciaCampiIstruzioniAi()
            _campiIstruzioniAi = New Campi()
            _campiIstruzioniAi.SalvaSuEscape = False
            _campiIstruzioniAi.Aggiungi(etichetta, "ai.istruzioni", True, False, "")
            AddHandler _campiIstruzioniAi.Fine, AddressOf CampiIstruzioniAiFine
            AddHandler _campiIstruzioniAi.Chiusura, AddressOf CampiIstruzioniAiChiusura
            _campiIstruzioniAi.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ApriCampiIstruzioniAi: " & ex.Message)
        End Try
    End Sub

    Private Sub RilasciaCampiIstruzioniAi()
        Try
            If _campiIstruzioniAi Is Nothing Then Return
            RemoveHandler _campiIstruzioniAi.Fine, AddressOf CampiIstruzioniAiFine
            RemoveHandler _campiIstruzioniAi.Chiusura, AddressOf CampiIstruzioniAiChiusura
            _campiIstruzioniAi = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RilasciaCampiIstruzioniAi: " & ex.Message)
            _campiIstruzioniAi = Nothing
        End Try
    End Sub

    Private Sub CampiIstruzioniAiChiusura(sender As Object, e As EventArgs)
        Try
            RilasciaCampiIstruzioniAi()
            _scopoIstruzioniAi = ScopoIstruzioniAi.Nessuno
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiIstruzioniAiChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiIstruzioniAiFine(sender As Object, e As EventArgs)
        Try
            Dim istruzioni As String = ""
            If _campiIstruzioniAi IsNot Nothing Then istruzioni = If(_campiIstruzioniAi.RecuperaValore("ai.istruzioni"), "").Trim()
            RilasciaCampiIstruzioniAi()
            Dim scopo As ScopoIstruzioniAi = _scopoIstruzioniAi
            _scopoIstruzioniAi = ScopoIstruzioniAi.Nessuno
            If String.IsNullOrWhiteSpace(istruzioni) Then Return
            Select Case scopo
                Case ScopoIstruzioniAi.Risposta
                    GeneraRispostaAi(istruzioni)
                Case ScopoIstruzioniAi.Composizione
                    GeneraTestoComposizioneAi(istruzioni)
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiIstruzioniAiFine: " & ex.Message)
        End Try
    End Sub

    Private Function NuovaOperazioneAi() As Integer
        Try
            _idOperazioneAi += 1
            Return _idOperazioneAi
        Catch ex As Exception
            log.ScriviLog("MenuPosta.NuovaOperazioneAi: " & ex.Message)
            Return _idOperazioneAi
        End Try
    End Function

    Private Function DescriviErroreAi(risultato As Tuple(Of String, String), predefinito As String) As String
        Try
            If risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2) Then Return risultato.Item2
            Return predefinito
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DescriviErroreAi: " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Function TestoMessaggioPerAi(messaggio As MessaggioCompleto) As String
        Try
            If messaggio Is Nothing Then Return ""
            Dim testo As New StringBuilder()
            Dim mittente As String = If(messaggio.Mittente, "")
            Dim indirizzoMittente As String = If(messaggio.MittenteEmail, "")
            If Not String.IsNullOrWhiteSpace(indirizzoMittente) AndAlso Not String.Equals(mittente, indirizzoMittente, StringComparison.OrdinalIgnoreCase) Then
                testo.AppendLine("Da: " & mittente & " <" & indirizzoMittente & ">")
            Else
                testo.AppendLine("Da: " & If(String.IsNullOrWhiteSpace(mittente), indirizzoMittente, mittente))
            End If
            If Not String.IsNullOrWhiteSpace(messaggio.Destinatari) Then testo.AppendLine("A: " & messaggio.Destinatari)
            If Not String.IsNullOrWhiteSpace(messaggio.Cc) Then testo.AppendLine("Cc: " & messaggio.Cc)
            testo.AppendLine("Oggetto: " & If(String.IsNullOrWhiteSpace(messaggio.Oggetto), "Senza oggetto", messaggio.Oggetto))
            If messaggio.Data <> DateTimeOffset.MinValue Then testo.AppendLine("Data: " & messaggio.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm"))
            testo.AppendLine()
            testo.Append(CorpoLeggibilePerAi(messaggio))
            Dim risultato As String = testo.ToString()
            If risultato.Length > LunghezzaMassimaTestoPerAi Then risultato = risultato.Substring(0, LunghezzaMassimaTestoPerAi)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TestoMessaggioPerAi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function CorpoLeggibilePerAi(messaggio As MessaggioCompleto) As String
        Try
            If messaggio Is Nothing Then Return ""
            Dim corpo As String = CLSMail.CorpoLeggibile(messaggio.CorpoTesto, messaggio.CorpoHtml)
            If String.IsNullOrWhiteSpace(corpo) AndAlso Not String.IsNullOrWhiteSpace(messaggio.CorpoHtml) Then
                corpo = CLSMail.HtmlVersoTesto(messaggio.CorpoHtml)
            End If
            Return If(corpo, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CorpoLeggibilePerAi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Sub AvviaRiassuntoAi()
        Dim idOperazione As Integer = 0
        Try
            If _messaggioCorrente Is Nothing Then Return
            Dim messaggio As MessaggioCompleto = _messaggioCorrente
            Dim titolo As String = "Riassunto: " & If(String.IsNullOrWhiteSpace(messaggio.Oggetto), "Senza oggetto", messaggio.Oggetto)
            Dim testo As String = TestoMessaggioPerAi(messaggio)
            If String.IsNullOrWhiteSpace(testo) Then
                MostraMessaggio("Il messaggio non contiene testo da riassumere.")
                Return
            End If

            idOperazione = NuovaOperazioneAi()
            MostraAttesa("Riassunto del messaggio in corso, attendere")
            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiEmailAsync(testo)
            If idOperazione <> _idOperazioneAi Then Return
            Dim annullata As Boolean = OperazioneAnnullata()
            ChiudiAttesa()
            If annullata Then Return

            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                MostraMessaggio(DescriviErroreAi(risultato, "Riassunto non riuscito."))
                Return
            End If
            MostraRiassuntoAi(titolo, risultato.Item1)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaRiassuntoAi: " & ex.Message)
            If idOperazione = _idOperazioneAi Then ChiudiAttesa()
        End Try
    End Sub

    Private Sub MostraRiassuntoAi(titolo As String, testo As String)
        Try
            ChiudiRiassuntoAi()
            _campoRiassuntoAi = New WGCampo()
            _campoRiassuntoAi.init(titolo, If(testo, ""), True, True, False, False)
            AddHandler _campoRiassuntoAi.chiusura, AddressOf CampoRiassuntoAiChiusura
            _campoRiassuntoAi.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraRiassuntoAi: " & ex.Message)
        End Try
    End Sub

    Private Sub CampoRiassuntoAiChiusura(sender As Object, e As EventArgs)
        Try
            ChiudiRiassuntoAi()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampoRiassuntoAiChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiRiassuntoAi()
        Try
            If _campoRiassuntoAi Is Nothing Then Return
            RemoveHandler _campoRiassuntoAi.chiusura, AddressOf CampoRiassuntoAiChiusura
            _campoRiassuntoAi.chiudi()
            _campoRiassuntoAi = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiRiassuntoAi: " & ex.Message)
            _campoRiassuntoAi = Nothing
        End Try
    End Sub

    Private Async Sub GeneraRispostaAi(istruzioni As String)
        Dim idOperazione As Integer = 0
        Try
            If _messaggioCorrente Is Nothing Then Return
            Dim messaggio As MessaggioCompleto = _messaggioCorrente
            Dim aTutti As Boolean = _rispostaAiATutti
            Dim conWord As Boolean = ScritturaMessaggiConWord()
            Dim testoOriginale As String = TestoMessaggioPerAi(messaggio)

            idOperazione = NuovaOperazioneAi()
            MostraAttesa("Scrittura della risposta in corso, attendere")
            Dim risultato As Tuple(Of String, String) = Await WGAi.ScriviRispostaEmailAsync(testoOriginale, istruzioni, conWord)
            If idOperazione <> _idOperazioneAi Then Return
            Dim annullata As Boolean = OperazioneAnnullata()
            ChiudiAttesa()
            If annullata Then Return

            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                MostraMessaggio(DescriviErroreAi(risultato, "Scrittura della risposta non riuscita."))
                Return
            End If

            _messaggioCorrente = messaggio
            PreparaIntestazioneRisposta(aTutti)
            If conWord Then
                _htmlBozzaDaCaricare = risultato.Item1 & CitazioneHtml(messaggio)
                _immaginiBozzaDaCaricare = Nothing
                _composizioneCorpo = ""
            Else
                _composizioneCorpo = risultato.Item1 & CorpoCitato()
            End If
            ApriComposizione()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.GeneraRispostaAi: " & ex.Message)
            If idOperazione = _idOperazioneAi Then ChiudiAttesa()
        End Try
    End Sub

    Private Async Sub GeneraTestoComposizioneAi(istruzioni As String)
        Dim idOperazione As Integer = 0
        Try
            Dim conWord As Boolean = ScritturaMessaggiConWord()
            Dim testoAttuale As String = If(_composizioneCorpo, "")
            Dim destinatario As String = If(_composizioneDestinatari, "")
            Dim oggetto As String = If(_composizioneOggetto, "")

            idOperazione = NuovaOperazioneAi()
            MostraAttesa("Scrittura del messaggio in corso, attendere")
            Dim risultato As Tuple(Of String, String) = Await WGAi.ScriviEmailAsync(istruzioni, destinatario, oggetto, testoAttuale, conWord)
            If idOperazione <> _idOperazioneAi Then Return
            Dim annullata As Boolean = OperazioneAnnullata()
            ChiudiAttesa()
            If annullata Then Return

            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                MostraMessaggio(DescriviErroreAi(risultato, "Scrittura del messaggio non riuscita."))
                Return
            End If

            If conWord Then
                If _campiComposizione IsNot Nothing Then
                    _campiComposizione.CaricaWordDaHtml("msg.corpo", risultato.Item1, Nothing, "Messaggio")
                Else
                    _htmlBozzaDaCaricare = risultato.Item1
                    _immaginiBozzaDaCaricare = Nothing
                    ApriComposizioneDettagli()
                End If
                Return
            End If

            Dim testoGenerato As String = risultato.Item1.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, vbCrLf)
            _composizioneCorpo = testoGenerato
            If _campiComposizione IsNot Nothing Then
                _campiComposizione.AggiornaTesto("msg.corpo", testoGenerato)
            Else
                ApriComposizioneDettagli()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.GeneraTestoComposizioneAi: " & ex.Message)
            If idOperazione = _idOperazioneAi Then ChiudiAttesa()
        End Try
    End Sub

    Private Function CitazioneHtml(messaggio As MessaggioCompleto) As String
        Try
            If messaggio Is Nothing Then Return ""
            Dim html As New StringBuilder()
            html.Append("<p><br></p><hr>")
            html.Append("<p>")
            Dim mittente As String = If(messaggio.Mittente, "")
            If String.IsNullOrWhiteSpace(mittente) Then mittente = If(messaggio.MittenteEmail, "")
            html.Append("<b>Da:</b> ").Append(System.Net.WebUtility.HtmlEncode(mittente))
            If messaggio.Data <> DateTimeOffset.MinValue Then
                html.Append("<br><b>Data:</b> ").Append(System.Net.WebUtility.HtmlEncode(messaggio.Data.LocalDateTime.ToString("dd/MM/yyyy HH:mm")))
            End If
            html.Append("<br><b>Oggetto:</b> ").Append(System.Net.WebUtility.HtmlEncode(If(messaggio.Oggetto, "")))
            html.Append("</p>")
            html.Append(TestoInParagrafiHtml(CorpoLeggibilePerAi(messaggio)))
            Return html.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CitazioneHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoInParagrafiHtml(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim normalizzato As String = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim blocchi() As String = normalizzato.Split(New String() {vbLf & vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim html As New StringBuilder()
            For Each blocco As String In blocchi
                Dim contenuto As String = blocco.Trim(New Char() {ControlChars.Lf, " "c, ControlChars.Tab})
                If contenuto.Length = 0 Then Continue For
                html.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(contenuto).Replace(vbLf, "<br>")).Append("</p>")
            Next
            Return html.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.TestoInParagrafiHtml: " & ex.Message)
            Return ""
        End Try
    End Function

End Class