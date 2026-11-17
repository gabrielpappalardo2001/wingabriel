Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports wgcontrols
Imports WGDb
Public Class MenuAi

    Private Shared log As New Logger()

    Private Enum TipoElencoConversazioni
        Recenti
        Preferite
        Tutte
    End Enum

    Private Const IdVoceNuovaDomanda As String = "NUOVA_DOMANDA"

    Private _clsAi As CLSAi
    Private _menuConversazione As WGMenuLista
    Private _campoTestoCompleto As WGCampo
    Private _campiNuovaDomanda As Campi
    Private _processoAi As WGProcessoInCorso
    Private _processoGenerico As WGProcessoInCorso
    Private _percorsoAllegatoProssimo As String = ""
    Private _modelloSovrascrittoProssimo As String = Nothing
    Private _ultimoTestoRisposta As String = ""

    Private _dialogoAllegato As WGFileDialog
    Private _dialogoSalvaAllegato As WGFileDialog
    Private _dialogoSalvaRisposta As WGFileDialog
    Private _dialogoRiassuntoFile As WGFileDialog
    Private _menuConversazioniElenco As WGMenuLista
    Private _menuModelliDomanda As WGMenuLista
    Private _campoUrlVideo As WGCampoTesto

    Private _percorsoAllegatoDaSalvare As String = ""

    ''' <summary>I file che il selettore mostra per allegare o riassumere: documenti, testo, PDF, immagini, audio e video.</summary>
    Private Shared ReadOnly EstensioniAllegabili As String() = {
        ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm",
        ".docx", ".odt", ".rtf", ".pdf",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".mp3", ".wav", ".mp4"}

    Private Shared Sub ImpostaEstensioniAllegabili(dialogo As WGFileDialog)
        For Each estensione As String In EstensioniAllegabili
            dialogo.AggiungiEstensione(estensione)
        Next
    End Sub
    Private _nomeAllegatoDaSalvare As String = ""

    Public Sub Avvia()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Nuova conversazione", AddressOf NuovaConversazione)
            menu.aggiungi("Conversazioni recenti", AddressOf MostraConversazioniRecenti)
            menu.aggiungi("Conversazioni preferite", AddressOf MostraConversazioniPreferite)
            menu.aggiungi("Tutte le conversazioni", AddressOf MostraTutteLeConversazioni)
            menu.aggiungi("Riassumi un file", AddressOf AvviaRiassuntoFile)
            menu.aggiungi("Riassumi un video", AddressOf AvviaRiassuntoVideo)
            menu.aggiungi("Verifica consumo giornaliero", AddressOf VerificaConsumoGiornaliero)
            menu.avvia("Intelligenza artificiale")
        Catch ex As Exception
            log.ScriviLog($"MenuAi.Avvia: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Elenco conversazioni (recenti/preferite/tutte) =====

    Private Sub MostraConversazioniRecenti()
        MostraElencoConversazioni(TipoElencoConversazioni.Recenti)
    End Sub

    Private Sub MostraConversazioniPreferite()
        MostraElencoConversazioni(TipoElencoConversazioni.Preferite)
    End Sub

    Private Sub MostraTutteLeConversazioni()
        MostraElencoConversazioni(TipoElencoConversazioni.Tutte)
    End Sub

    Private Sub MostraElencoConversazioni(tipo As TipoElencoConversazioni)
        Try
            If Not CLSAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If

            Dim tabella As DataTable
            Select Case tipo
                Case TipoElencoConversazioni.Recenti
                    tabella = CLSAi.ElencaConversazioni(False, 20)
                Case TipoElencoConversazioni.Preferite
                    tabella = CLSAi.ElencaConversazioni(True, 0)
                Case Else
                    tabella = CLSAi.ElencaConversazioni(False, 0)
            End Select

            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna conversazione trovata.")
                msg.ShowDialog()
                Return
            End If

            _menuConversazioniElenco = New WGMenuLista()
            _menuConversazioniElenco.multiselezioneConsentita = False
            _menuConversazioniElenco.impostaTitolo("Scegli una conversazione")
            For Each riga As DataRow In tabella.Rows
                Dim id As String = CStr(riga(WGDatabase.NomeColonnaChiavePredefinita))
                Dim titolo As String = CStr(riga("Titolo"))
                _menuConversazioniElenco.aggiungiElemento(titolo, id)
            Next
            AddHandler _menuConversazioniElenco.escPremuto, AddressOf ChiudiMenuConversazioniElenco
            AddHandler _menuConversazioniElenco.ElementoSelezionato, AddressOf ConversazioneSelezionata
            _menuConversazioniElenco.aggiungiOpzione("Apri", AddressOf OpzioneApriConversazione)
            _menuConversazioniElenco.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferiti)
            _menuConversazioniElenco.aggiungiOpzione("Togli dai preferiti", AddressOf OpzioneTogliPreferiti)
            _menuConversazioniElenco.aggiungiOpzione("Elimina conversazione", AddressOf OpzioneEliminaConversazione)
            _menuConversazioniElenco.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraElencoConversazioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiMenuConversazioniElenco()
        Try
            _menuConversazioniElenco.abbandona()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ChiudiMenuConversazioniElenco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ConversazioneSelezionata()
        Try
            Dim elementi As List(Of String) = _menuConversazioniElenco.ottieniElementi()
            If elementi.Count = 0 Then Return
            Dim id As String = elementi(0)
            _menuConversazioniElenco.abbandona()
            ApriConversazione(id)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ConversazioneSelezionata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OpzioneApriConversazione(sender As Object, e As EventArgs)
        Try
            Dim elementi As List(Of String) = _menuConversazioniElenco.ottieniElementi()
            If elementi.Count = 0 Then Return
            Dim id As String = elementi(0)
            _menuConversazioniElenco.abbandona()
            ApriConversazione(id)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneApriConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OpzioneAggiungiPreferiti(sender As Object, e As EventArgs)
        Try
            Dim elementi As List(Of String) = _menuConversazioniElenco.ottieniElementi()
            If elementi.Count = 0 Then Return
            CLSAi.ImpostaPreferita(elementi(0), True)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Conversazione aggiunta ai preferiti.")
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneAggiungiPreferiti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OpzioneTogliPreferiti(sender As Object, e As EventArgs)
        Try
            Dim elementi As List(Of String) = _menuConversazioniElenco.ottieniElementi()
            If elementi.Count = 0 Then Return
            CLSAi.ImpostaPreferita(elementi(0), False)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Conversazione tolta dai preferiti.")
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneTogliPreferiti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OpzioneEliminaConversazione(sender As Object, e As EventArgs)
        Try
            Dim elementi As List(Of String) = _menuConversazioniElenco.ottieniElementi()
            If elementi.Count = 0 Then Return
            Dim id As String = elementi(0)
            CLSAi.EliminaConversazione(id)
            _menuConversazioniElenco.RimuoviElemento(id)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Conversazione eliminata.")
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneEliminaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Apertura di una conversazione: elenco dei turni =====

    Private Sub NuovaConversazione()
        Try
            If Not CLSAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If

            IniziaSessione()
            _clsAi.NuovaConversazione()
            ' una conversazione nuova non ha ancora niente da elencare: si apre subito il campo per scrivere la domanda
            AvviaComposizioneDomanda()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.NuovaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApriConversazione(idConversazione As String)
        Try
            IniziaSessione()
            _clsAi.CaricaConversazione(idConversazione)
            MostraElencoTurni()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ApriConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub IniziaSessione()
        Try
            If _clsAi Is Nothing Then
                _percorsoAllegatoProssimo = ""
                _modelloSovrascrittoProssimo = Nothing
                _ultimoTestoRisposta = ""

                _clsAi = New CLSAi()
                AddHandler _clsAi.RispostaCompletata, AddressOf OnRispostaCompletata
                AddHandler _clsAi.RispostaFallita, AddressOf OnRispostaFallita
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.IniziaSessione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function CostruisciEtichettaMessaggio(messaggio As CLSAi.MessaggioChat) As String
        Try
            Dim prefisso As String = If(messaggio.Ruolo = "assistente", "IA: ", "Tu: ")
            If Not String.IsNullOrWhiteSpace(messaggio.PercorsoFileAllegato) Then
                Return prefisso & "[File allegato] " & messaggio.NomeFileAllegato
            End If
            Dim testo As String = If(messaggio.Testo, "").Replace(Environment.NewLine, " ").Replace(vbLf, " ").Replace(vbCr, " ").Trim()
            If testo.Length > 40 Then Return prefisso & testo.Substring(0, 40) & "..."
            Return prefisso & testo
        Catch ex As Exception
            log.ScriviLog($"MenuAi.CostruisciEtichettaMessaggio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Messaggio"
        End Try
    End Function

    ''' <summary>Torna al menu della conversazione; se la conversazione è ancora vuota non c'è niente da elencare e si esce.</summary>
    Private Sub TornaAllaConversazione()
        If _clsAi Is Nothing Then Return
        If _clsAi.Cronologia.Count = 0 Then
            TerminaSessione()
        Else
            MostraElencoTurni()
        End If
    End Sub

    Private Sub MostraElencoTurni()
        Try
            If _clsAi Is Nothing Then Return

            _menuConversazione = New WGMenuLista()
            _menuConversazione.multiselezioneConsentita = False
            _menuConversazione.impostaTitolo("Conversazione. Premi Invio su una voce per leggerla per intero")

            For Each messaggio As CLSAi.MessaggioChat In _clsAi.Cronologia
                If String.IsNullOrWhiteSpace(messaggio.Chiave) Then Continue For
                _menuConversazione.aggiungiElemento(CostruisciEtichettaMessaggio(messaggio), messaggio.Chiave)
            Next
            _menuConversazione.aggiungiElemento("Scrivi nuova domanda", IdVoceNuovaDomanda)

            AddHandler _menuConversazione.escPremuto, AddressOf ChiudiConversazione
            AddHandler _menuConversazione.ElementoSelezionato, AddressOf TurnoSelezionato
            If Not String.IsNullOrWhiteSpace(_ultimoTestoRisposta) Then
                _menuConversazione.aggiungiOpzione("Salva l'ultima risposta su file", AddressOf OpzioneSalvaUltimaRisposta)
            End If

            _menuConversazione.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraElencoTurni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiConversazione()
        Try
            _menuConversazione.abbandona()
            TerminaSessione()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ChiudiConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub TerminaSessione()
        Try
            If _clsAi IsNot Nothing Then
                _clsAi.Annulla()
                RemoveHandler _clsAi.RispostaCompletata, AddressOf OnRispostaCompletata
                RemoveHandler _clsAi.RispostaFallita, AddressOf OnRispostaFallita
                _clsAi.Dispose()
                _clsAi = Nothing
            End If
            NascondiProcessoAi()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.TerminaSessione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub TurnoSelezionato()
        Try
            Dim elementi As List(Of String) = _menuConversazione.ottieniElementi()
            If elementi.Count = 0 Then Return
            Dim id As String = elementi(0)

            If id = IdVoceNuovaDomanda Then
                _menuConversazione.abbandona()
                AvviaComposizioneDomanda()
                Return
            End If

            Dim messaggio As CLSAi.MessaggioChat = _clsAi.Cronologia.FirstOrDefault(Function(m) m.Chiave = id)
            If messaggio.Chiave Is Nothing Then Return

            _menuConversazione.abbandona()

            If Not String.IsNullOrWhiteSpace(messaggio.PercorsoFileAllegato) Then
                MostraSalvaAllegato(messaggio.PercorsoFileAllegato, messaggio.NomeFileAllegato)
            Else
                Dim titoloCampo As String = If(messaggio.Ruolo = "assistente", "Risposta", "Domanda")
                MostraTestoCompleto(titoloCampo, messaggio.Testo, messaggio.Ruolo = "assistente")
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.TurnoSelezionato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ''' <summary>
    ''' Mostra un turno per intero. In una risposta dell'intelligenza artificiale Invio apre il campo della domanda successiva, per
    ''' avere un botta e risposta continuo; Esc torna al menu della conversazione.
    ''' </summary>
    Private Sub MostraTestoCompleto(titolo As String, testo As String, Optional invioPerDomandaSuccessiva As Boolean = False)
        Try
            _campoTestoCompleto = New WGCampo()
            _campoTestoCompleto.init(titolo, If(testo, ""), True, True, False, False)
            AddHandler _campoTestoCompleto.chiusura, AddressOf OnChiusuraTestoCompleto
            If invioPerDomandaSuccessiva Then
                _campoTestoCompleto.InvioSollevaEvento = True
                AddHandler _campoTestoCompleto.InvioPremuto, AddressOf OnInvioSuRisposta
            End If
            _campoTestoCompleto.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraTestoCompleto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' Invio sulla risposta: il campo della risposta si chiude e si apre quello della domanda. Una volta scritta la domanda anche
    ' quello si chiude, e la risposta nuova riparte da qui; con Esc dalla risposta si torna al menu della conversazione.
    Private Sub OnInvioSuRisposta(sender As Object, e As EventArgs)
        Try
            Dim campo As WGCampo = _campoTestoCompleto
            If campo Is Nothing OrElse _clsAi Is Nothing Then Return
            RemoveHandler campo.chiusura, AddressOf OnChiusuraTestoCompleto
            RemoveHandler campo.InvioPremuto, AddressOf OnInvioSuRisposta
            _campoTestoCompleto = Nothing
            campo.chiudi()
            AvviaComposizioneDomanda()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnInvioSuRisposta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnChiusuraTestoCompleto(sender As Object, e As EventArgs)
        Try
            If _campoTestoCompleto IsNot Nothing Then
                RemoveHandler _campoTestoCompleto.chiusura, AddressOf OnChiusuraTestoCompleto
                RemoveHandler _campoTestoCompleto.InvioPremuto, AddressOf OnInvioSuRisposta
                _campoTestoCompleto = Nothing
            End If
            If _clsAi IsNot Nothing Then MostraElencoTurni()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnChiusuraTestoCompleto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Composizione di una nuova domanda =====

    Private Sub AvviaComposizioneDomanda()
        Try
            _campiNuovaDomanda = New Campi()
            AddHandler _campiNuovaDomanda.Chiusura, AddressOf OnComposizioneDomandaChiusura
            AddHandler _campiNuovaDomanda.Fine, AddressOf OnComposizioneDomandaConfermata

            _campiNuovaDomanda.AggiungiOpzione("Allega un file", AddressOf OpzioneAllegaFile)
            _campiNuovaDomanda.AggiungiOpzione("Cambia modello per questa domanda", AddressOf OpzioneCambiaModello)

            _campiNuovaDomanda.AggiungiMultiriga("Scrivi la tua domanda (premi Invio tre volte per inviare)", "domanda")
            _campiNuovaDomanda.Avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.AvviaComposizioneDomanda: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub PuliziaComposizioneDomanda()
        Try
            If _campiNuovaDomanda IsNot Nothing Then
                RemoveHandler _campiNuovaDomanda.Chiusura, AddressOf OnComposizioneDomandaChiusura
                RemoveHandler _campiNuovaDomanda.Fine, AddressOf OnComposizioneDomandaConfermata
                _campiNuovaDomanda = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.PuliziaComposizioneDomanda: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnComposizioneDomandaChiusura(sender As Object, e As EventArgs)
        Try
            PuliziaComposizioneDomanda()
            TornaAllaConversazione()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnComposizioneDomandaChiusura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnComposizioneDomandaConfermata(sender As Object, e As EventArgs)
        Try
            Dim domanda As String = If(_campiNuovaDomanda IsNot Nothing, _campiNuovaDomanda.RecuperaValore("domanda"), "")
            PuliziaComposizioneDomanda()

            If String.IsNullOrWhiteSpace(domanda) Then
                TornaAllaConversazione()
                Return
            End If

            Dim percorsoAllegato As String = _percorsoAllegatoProssimo
            Dim modelloSovrascritto As String = _modelloSovrascrittoProssimo
            _percorsoAllegatoProssimo = ""
            _modelloSovrascrittoProssimo = Nothing

            MostraProcessoAi()
            _clsAi.InviaDomandaAsync(domanda, percorsoAllegato, Nothing, modelloSovrascritto)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnComposizioneDomandaConfermata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnRispostaCompletata(testo As String)
        Try
            NascondiProcessoAi()
            _ultimoTestoRisposta = testo
            MostraTestoCompleto("Risposta", testo, True)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnRispostaCompletata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnRispostaFallita(messaggioErrore As String)
        Try
            NascondiProcessoAi()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Non è stato possibile ottenere una risposta: " & messaggioErrore)
            msg.ShowDialog()
            MostraElencoTurni()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnRispostaFallita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Attesa durante l'elaborazione (con annullamento per la conversazione) =====

    Private Sub MostraProcessoAi()
        Try
            _processoAi = New WGProcessoInCorso()
            AddHandler _processoAi.cancella, AddressOf OnAnnullaProcessoAi
            _processoAi.Avvia("Sto elaborando la risposta, attendere", True)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraProcessoAi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub NascondiProcessoAi()
        Try
            If _processoAi IsNot Nothing Then
                Try
                    RemoveHandler _processoAi.cancella, AddressOf OnAnnullaProcessoAi
                Catch exRimozione As Exception
                    log.ScriviLog($"MenuAi.NascondiProcessoAi rimozione gestore: {exRimozione.Message}")
                End Try
                _processoAi.Fine()
                _processoAi = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.NascondiProcessoAi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnAnnullaProcessoAi(sender As Object, e As EventArgs)
        Try
            _processoAi = Nothing
            If _clsAi IsNot Nothing Then _clsAi.Annulla()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Richiesta annullata.")
            msg.ShowDialog()
            MostraElencoTurni()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnAnnullaProcessoAi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraAttesaGenerica(messaggio As String)
        Try
            _processoGenerico = New WGProcessoInCorso()
            _processoGenerico.Avvia(messaggio, False)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraAttesaGenerica: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub NascondiAttesaGenerica()
        Try
            If _processoGenerico IsNot Nothing Then
                _processoGenerico.Fine()
                _processoGenerico = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.NascondiAttesaGenerica: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Allegati: scelta per l'invio, salvataggio dall'archivio =====

    Private Sub OpzioneAllegaFile(sender As Object, e As EventArgs)
        Try
            _dialogoAllegato = New WGFileDialog()
            _dialogoAllegato.MultiselezioneConsentita = False
            ImpostaEstensioniAllegabili(_dialogoAllegato)
            AddHandler _dialogoAllegato.selezioneterminata, AddressOf FileAllegatoScelto
            _dialogoAllegato.Avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneAllegaFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub FileAllegatoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoAllegato.selezioneterminata, AddressOf FileAllegatoScelto
            Catch exRimozione As Exception
                log.ScriviLog($"MenuAi.FileAllegatoScelto rimozione gestore: {exRimozione.Message}")
            End Try

            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            _percorsoAllegatoProssimo = elementi(0)

            Dim msg As New WGMessaggi()
            msg.Messaggio("File allegato per la prossima domanda: " & Path.GetFileName(_percorsoAllegatoProssimo))
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.FileAllegatoScelto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraSalvaAllegato(percorsoAllegato As String, nomeFile As String)
        Try
            If Not File.Exists(percorsoAllegato) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il file allegato non è più disponibile.")
                msg.ShowDialog()
                MostraElencoTurni()
                Return
            End If

            _percorsoAllegatoDaSalvare = percorsoAllegato
            _nomeAllegatoDaSalvare = If(String.IsNullOrWhiteSpace(nomeFile), Path.GetFileName(percorsoAllegato), nomeFile)

            _dialogoSalvaAllegato = New WGFileDialog()
            AddHandler _dialogoSalvaAllegato.singolaselezione, AddressOf CartellaSalvataggioAllegatoScelta
            _dialogoSalvaAllegato.Avvia("")
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraSalvaAllegato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub CartellaSalvataggioAllegatoScelta(elemento As String)
        Try
            Try
                RemoveHandler _dialogoSalvaAllegato.singolaselezione, AddressOf CartellaSalvataggioAllegatoScelta
            Catch exRimozione As Exception
                log.ScriviLog($"MenuAi.CartellaSalvataggioAllegatoScelta rimozione gestore: {exRimozione.Message}")
            End Try

            Dim cartella As String = If(elemento, "").Trim()
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Try
                    cartella = Path.GetDirectoryName(cartella)
                Catch exCartella As Exception
                    log.ScriviLog($"MenuAi.CartellaSalvataggioAllegatoScelta (risalita cartella): {exCartella.Message}")
                    cartella = ""
                End Try
            End If

            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Directory.Exists(cartella) AndAlso File.Exists(_percorsoAllegatoDaSalvare) Then
                Dim percorsoDestinazione As String = Path.Combine(cartella, _nomeAllegatoDaSalvare)
                File.Copy(_percorsoAllegatoDaSalvare, percorsoDestinazione, True)
                Dim msg As New WGMessaggi()
                msg.Messaggio("File salvato in " & percorsoDestinazione)
                msg.ShowDialog()
            End If

            _percorsoAllegatoDaSalvare = ""
            _nomeAllegatoDaSalvare = ""
            MostraElencoTurni()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.CartellaSalvataggioAllegatoScelta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OpzioneSalvaUltimaRisposta(sender As Object, e As EventArgs)
        Try
            If String.IsNullOrWhiteSpace(_ultimoTestoRisposta) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non c'è ancora nessuna risposta da salvare.")
                msg.ShowDialog()
                Return
            End If

            _dialogoSalvaRisposta = New WGFileDialog()
            AddHandler _dialogoSalvaRisposta.singolaselezione, AddressOf CartellaSalvataggioRispostaScelta
            _dialogoSalvaRisposta.Avvia("")
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneSalvaUltimaRisposta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub CartellaSalvataggioRispostaScelta(elemento As String)
        Try
            Try
                RemoveHandler _dialogoSalvaRisposta.singolaselezione, AddressOf CartellaSalvataggioRispostaScelta
            Catch exRimozione As Exception
                log.ScriviLog($"MenuAi.CartellaSalvataggioRispostaScelta rimozione gestore: {exRimozione.Message}")
            End Try

            Dim cartella As String = If(elemento, "").Trim()
            If String.IsNullOrWhiteSpace(cartella) Then Return
            If Not Directory.Exists(cartella) Then
                Try
                    cartella = Path.GetDirectoryName(cartella)
                Catch exCartella As Exception
                    log.ScriviLog($"MenuAi.CartellaSalvataggioRispostaScelta (risalita cartella): {exCartella.Message}")
                    cartella = ""
                End Try
            End If
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return

            Dim nomeFile As String = "risposta_ai_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".txt"
            Dim percorsoCompleto As String = Path.Combine(cartella, nomeFile)
            File.WriteAllText(percorsoCompleto, _ultimoTestoRisposta, Encoding.UTF8)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Risposta salvata in " & percorsoCompleto)
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.CartellaSalvataggioRispostaScelta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Cambio modello per la singola domanda =====

    Private Async Sub OpzioneCambiaModello(sender As Object, e As EventArgs)
        Try
            Dim provider As CLSAi.ProviderAi = CLSAi.LeggiProviderPreferito()
            If Not CLSAi.ChiaveApiConfigurata(provider) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata per il motore predefinito.")
                msg.ShowDialog()
                Return
            End If

            Dim modelli As List(Of String) = Await CLSAi.ElencaModelliAsync(provider)
            If modelli Is Nothing OrElse modelli.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile recuperare l'elenco dei modelli disponibili.")
                msg.ShowDialog()
                Return
            End If

            _menuModelliDomanda = New WGMenuLista()
            _menuModelliDomanda.multiselezioneConsentita = False
            _menuModelliDomanda.impostaTitolo("Scegli il modello per la prossima domanda")
            For Each modello As String In modelli
                _menuModelliDomanda.aggiungiElemento(modello, modello)
            Next
            AddHandler _menuModelliDomanda.escPremuto, AddressOf ChiudiMenuModelliDomanda
            AddHandler _menuModelliDomanda.ElementoSelezionato, AddressOf ModelloDomandaSelezionato
            _menuModelliDomanda.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OpzioneCambiaModello: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ChiudiMenuModelliDomanda()
        Try
            _menuModelliDomanda.abbandona()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ChiudiMenuModelliDomanda: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ModelloDomandaSelezionato()
        Try
            Dim elementi As List(Of String) = _menuModelliDomanda.ottieniElementi()
            If elementi.Count > 0 Then
                _modelloSovrascrittoProssimo = elementi(0)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Modello impostato per la prossima domanda: " & elementi(0))
                msg.ShowDialog()
            End If
            _menuModelliDomanda.abbandona()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.ModelloDomandaSelezionato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Riassunto di un file o di un video (utilizzabile anche indipendentemente da una conversazione) =====

    Private Sub AvviaRiassuntoFile()
        Try
            If Not CLSAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If

            _dialogoRiassuntoFile = New WGFileDialog()
            _dialogoRiassuntoFile.MultiselezioneConsentita = False
            ImpostaEstensioniAllegabili(_dialogoRiassuntoFile)
            AddHandler _dialogoRiassuntoFile.selezioneterminata, AddressOf FileRiassuntoScelto
            _dialogoRiassuntoFile.Avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.AvviaRiassuntoFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub FileRiassuntoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoRiassuntoFile.selezioneterminata, AddressOf FileRiassuntoScelto
            Catch exRimozione As Exception
                log.ScriviLog($"MenuAi.FileRiassuntoScelto rimozione gestore: {exRimozione.Message}")
            End Try

            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorsoFile As String = elementi(0)

            MostraAttesaGenerica("Sto elaborando il riassunto del file, attendere")
            Dim risultato As Tuple(Of String, String) = Await CLSAi.RiassumiFileAsync(percorsoFile)
            NascondiAttesaGenerica()

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile ottenere il riassunto: " & If(risultato IsNot Nothing, risultato.Item2, "errore sconosciuto"))
                msg.ShowDialog()
                Return
            End If

            _ultimoTestoRisposta = risultato.Item1
            MostraTestoCompletoIndipendente("Riassunto di " & Path.GetFileName(percorsoFile), risultato.Item1)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.FileRiassuntoScelto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub AvviaRiassuntoVideo()
        Try
            If Not WGDettatura.ChiaveApiGeminiConfigurata() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il riassunto video richiede una chiave API di Gemini, configurala dalle impostazioni.")
                msg.ShowDialog()
                Return
            End If

            _campoUrlVideo = New WGCampoTesto()
            _campoUrlVideo.init("Indirizzo del video da riassumere", "", False, False)
            AddHandler _campoUrlVideo.chiusura, AddressOf OnChiusuraCampoUrlVideo
            _campoUrlVideo.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.AvviaRiassuntoVideo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Async Sub OnChiusuraCampoUrlVideo(sender As Object, e As EventArgs)
        Try
            If _campoUrlVideo Is Nothing Then Return
            Dim url As String = If(_campoUrlVideo.OttieniTesto(), "").Trim()
            Try
                RemoveHandler _campoUrlVideo.chiusura, AddressOf OnChiusuraCampoUrlVideo
            Catch exRimozione As Exception
                log.ScriviLog($"MenuAi.OnChiusuraCampoUrlVideo rimozione gestore: {exRimozione.Message}")
            End Try
            _campoUrlVideo = Nothing

            If String.IsNullOrWhiteSpace(url) Then Return

            MostraAttesaGenerica("Sto elaborando il riassunto del video, attendere")
            Dim risultato As Tuple(Of String, String) = Await CLSAi.RiassumiVideoAsync(url)
            NascondiAttesaGenerica()

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile ottenere il riassunto: " & If(risultato IsNot Nothing, risultato.Item2, "errore sconosciuto"))
                msg.ShowDialog()
                Return
            End If

            _ultimoTestoRisposta = risultato.Item1
            MostraTestoCompletoIndipendente("Riassunto del video", risultato.Item1)
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnChiusuraCampoUrlVideo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraTestoCompletoIndipendente(titolo As String, testo As String)
        Try
            _campoTestoCompleto = New WGCampo()
            _campoTestoCompleto.init(titolo, If(testo, ""), True, True, False, False)
            AddHandler _campoTestoCompleto.chiusura, AddressOf OnChiusuraTestoCompletoIndipendente
            _campoTestoCompleto.avvia()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraTestoCompletoIndipendente: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub OnChiusuraTestoCompletoIndipendente(sender As Object, e As EventArgs)
        Try
            If _campoTestoCompleto IsNot Nothing Then
                RemoveHandler _campoTestoCompleto.chiusura, AddressOf OnChiusuraTestoCompletoIndipendente
                _campoTestoCompleto = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.OnChiusuraTestoCompletoIndipendente: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' ===== Verifica consumo giornaliero =====

    Private Sub VerificaConsumoGiornaliero()
        Try
            Dim geminiConfigurato As Boolean = WGAi.ChiaveApiConfigurata(WGAi.ProviderAi.Gemini)
            Dim groqConfigurato As Boolean = WGAi.ChiaveApiConfigurata(WGAi.ProviderAi.Groq)

            If Not geminiConfigurato AndAlso Not groqConfigurato Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If

            If geminiConfigurato AndAlso groqConfigurato Then
                Dim menu As New WGMenu()
                menu.aggiungi("Gemini", AddressOf MostraConsumoGemini)
                menu.aggiungi("Groq", AddressOf MostraConsumoGroq)
                menu.avvia("Verifica consumo giornaliero", True)
            ElseIf geminiConfigurato Then
                MostraConsumoGemini()
            Else
                MostraConsumoGroq()
            End If
        Catch ex As Exception
            log.ScriviLog($"MenuAi.VerificaConsumoGiornaliero: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraConsumoGemini()
        Try
            Dim stato As Tuple(Of Integer, Integer, Integer, Integer) = WGAi.OttieniStatoRichiesteGemini()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Gemini (stima calcolata da WinGabriel, in base ai limiti tipici del livello gratuito, dato che Gemini non comunica il consumo reale)." & Environment.NewLine &
                          "Nell'ultimo minuto: " & stato.Item1.ToString() & " di circa " & stato.Item2.ToString() & " richieste." & Environment.NewLine &
                          "Oggi: " & stato.Item3.ToString() & " di circa " & stato.Item4.ToString() & " richieste.")
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraConsumoGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub MostraConsumoGroq()
        Try
            Dim stato As Tuple(Of Integer, Integer, Integer, Integer, Boolean) = WGAi.OttieniStatoRichiesteGroq()
            Dim msg As New WGMessaggi()
            If stato.Item5 Then
                msg.Messaggio("Groq (dato reale, fornito da Groq nell'ultima risposta ricevuta)." & Environment.NewLine &
                              "Richieste rimanenti: " & stato.Item1.ToString() & " di " & stato.Item2.ToString() & "." & Environment.NewLine &
                              "Token rimanenti: " & stato.Item3.ToString() & " di " & stato.Item4.ToString() & ".")
            Else
                msg.Messaggio("Groq: non è ancora stata effettuata nessuna richiesta in questa sessione, il dato non è ancora disponibile.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog($"MenuAi.MostraConsumoGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

End Class