Imports System
Imports System.Collections.Generic
Imports wgcontrols
Imports WGDati
Imports WGMail

' Gestione delle agende di lavoro. Permette la selezione, la creazione, la ridenominazione, l'eliminazione e la protezione tramite
' password.
Partial Friend Class MenuAgenda

    Private Enum ScopoPasswordAgenda
        Apertura = 0
        Impostazione = 1
        Rimozione = 2
    End Enum

    Private Const TutteLeAgende As String = "__tutte__"
    Private Const NessunAccountAgenda As String = "__locale__"

    Private _listaAgende As WGMenuLista
    Private _listaAccountAgenda As WGMenuLista
    Private _campiAgenda As Campi
    Private _campiPasswordAgenda As Campi
    Private _menuProtezioneAgenda As WGMenu
    Private _agendaInLavorazione As String = ""
    Private _scopoPasswordAgenda As ScopoPasswordAgenda = ScopoPasswordAgenda.Apertura

    ' Agenda "di lavoro" predefinita per inserimento, consultazione, ricerca, categorie e
    ' modifica: viene ricordata in configurazione, e se protetta va sbloccata con la password
    ' prima di poter operare. E' un concetto diverso dal campo "Predefinita" del database (che
    ' sceglie l'agenda di destinazione quando non se ne specifica una).
    Private _operazionePendenteAgenda As Action = Nothing
    Private _agendaPredefinitaImpostata As Boolean = False

    Friend Sub MostraElencoAgende()
        Try
            ChiudiListaAgende()
            If classeagenda Is Nothing Then classeagenda = CLSAgenda.GetInstance()
            Dim agende As List(Of CLSAgenda.DescrizioneAgenda) = classeagenda.OttieniAgende()
            If agende Is Nothing OrElse agende.Count = 0 Then
                classeagenda.OttieniOCreaAgendaPredefinita()
                agende = classeagenda.OttieniAgende()
            End If

            _listaAgende = New WGMenuLista()
            _listaAgende.multiselezioneConsentita = False
            _listaAgende.impostaTitolo("Agende, premi Invio per consultarne gli appuntamenti")
            _listaAgende.aggiungiElemento("Tutte le agende", TutteLeAgende)
            For Each voce As CLSAgenda.DescrizioneAgenda In agende
                _listaAgende.aggiungiElemento(DescriviAgenda(voce), voce.Chiave)
            Next

            _listaAgende.aggiungiOpzione("Nuova agenda", AddressOf OpzioneNuovaAgenda)
            _listaAgende.aggiungiOpzione("Rinomina l'agenda selezionata", AddressOf OpzioneRinominaAgenda)
            _listaAgende.aggiungiOpzione("Collega l'agenda a un account", AddressOf OpzioneCollegaAgenda)
            _listaAgende.aggiungiOpzione("Password dell'agenda", AddressOf OpzionePasswordAgenda)
            _listaAgende.aggiungiOpzione("Rendi predefinita l'agenda selezionata", AddressOf OpzioneAgendaPredefinita)
            _listaAgende.aggiungiOpzione("Elimina l'agenda selezionata", AddressOf OpzioneEliminaAgenda)
            AddHandler _listaAgende.ElementoSelezionato, AddressOf AgendaSelezionata
            AddHandler _listaAgende.escPremuto, AddressOf ListaAgendeEsc
            _listaAgende.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.MostraElencoAgende - " & ex.Message)
        End Try
    End Sub

    Private Function DescriviAgenda(voce As CLSAgenda.DescrizioneAgenda) As String
        Try
            Dim parti As New List(Of String)
            parti.Add(voce.Nome)
            If String.IsNullOrWhiteSpace(voce.ChiaveAccount) Then
                parti.Add("locale")
            Else
                parti.Add(NomeAccountAgenda(voce.ChiaveAccount))
            End If
            If voce.Predefinita Then parti.Add("predefinita")
            If voce.Protetta Then parti.Add(If(classeagenda.AgendaSbloccata(voce.Chiave), "protetta, aperta", "protetta, chiusa"))
            Return String.Join(", ", parti.ToArray())
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.DescriviAgenda - " & ex.Message)
            Return "Agenda"
        End Try
    End Function

    Private Function NomeAccountAgenda(chiaveAccount As String) As String
        Try
            Using posta As New CLSMail()
                Dim impostazioni As ImpostazioniAccountMail = posta.OttieniAccount(chiaveAccount)
                If impostazioni Is Nothing Then Return "account non più presente"
                If Not String.IsNullOrWhiteSpace(impostazioni.NomeVisualizzato) Then Return impostazioni.NomeVisualizzato
                Return If(impostazioni.IndirizzoEmail, chiaveAccount)
            End Using
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.NomeAccountAgenda - " & ex.Message)
            Return chiaveAccount
        End Try
    End Function

    Private Sub ChiudiListaAgende()
        Try
            If _listaAgende Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaAgende
            _listaAgende = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf AgendaSelezionata
                RemoveHandler precedente.escPremuto, AddressOf ListaAgendeEsc
            Catch exHandler As Exception
                _logger.ScriviLog("MenuAgenda.ChiudiListaAgende handler - " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ChiudiListaAgende - " & ex.Message)
            _listaAgende = Nothing
        End Try
    End Sub

    Friend Sub VoceAgende(sender As Object, e As EventArgs)
        Try
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.VoceAgende - " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAgendeEsc(sender As Object, e As EventArgs)
        Try
            ChiudiListaAgende()
            ' Se la scelta era stata richiesta per poter eseguire un'operazione (inserimento,
            ' consultazione, ricerca, categorie...) e l'utente annulla, l'operazione non parte.
            If _operazionePendenteAgenda IsNot Nothing Then
                _operazionePendenteAgenda = Nothing
                MostraMenuPrincipaleAgenda()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ListaAgendeEsc - " & ex.Message)
        End Try
    End Sub

    Private Function AgendaEvidenziata() As String
        Try
            If _listaAgende Is Nothing Then Return ""
            Dim selezione As List(Of String) = _listaAgende.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return ""
            Dim chiave As String = selezione(0)
            If String.Equals(chiave, TutteLeAgende, StringComparison.Ordinal) Then Return ""
            Return chiave
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AgendaEvidenziata - " & ex.Message)
            Return ""
        End Try
    End Function

    ' Selezionare un'agenda (invio) non ne mostra piu' gli appuntamenti: la rende semplicemente
    ' l'agenda di lavoro predefinita (ricordata anche alla riapertura), cosi' che inserimento,
    ' consultazione, ricerca, categorie e modifica operino su di essa senza dover piu' chiedere
    ' quale usare.
    Private Sub AgendaSelezionata(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            ChiudiListaAgende()

            If String.Equals(id, TutteLeAgende, StringComparison.Ordinal) Then
                ImpostaAgendaComePredefinitaCorrente("")
                Dim azioneTutte As Action = _operazionePendenteAgenda
                _operazionePendenteAgenda = Nothing
                If azioneTutte IsNot Nothing Then
                    azioneTutte()
                Else
                    MostraMessaggioAgenda("Vengono mostrate tutte le agende aperte, senza doverlo più chiedere.")
                End If
                Return
            End If

            If classeagenda.AgendaProtetta(id) AndAlso Not classeagenda.AgendaSbloccata(id) Then
                ChiediPasswordAgenda(id, ScopoPasswordAgenda.Apertura, "Password dell'agenda " & classeagenda.NomeAgenda(id))
                Return
            End If

            AgendaSbloccataERisolta(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AgendaSelezionata - " & ex.Message)
        End Try
    End Sub

    ' Un'agenda e' stata scelta ed e' sbloccata (o non protetta): diventa la predefinita, e se
    ' c'era un'operazione in attesa la esegue adesso; altrimenti avvisa soltanto che e' stata
    ' impostata, senza mostrarne gli appuntamenti.
    Private Sub AgendaSbloccataERisolta(chiave As String)
        Try
            ImpostaAgendaComePredefinitaCorrente(chiave)
            Dim azione As Action = _operazionePendenteAgenda
            _operazionePendenteAgenda = Nothing
            If azione IsNot Nothing Then
                azione()
            Else
                MostraMessaggioAgenda("Agenda """ & classeagenda.NomeAgenda(chiave) & """ impostata come predefinita per le operazioni.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AgendaSbloccataERisolta - " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' AGENDA DI LAVORO PREDEFINITA (usata da inserimento, consulta, cerca, categorie, modifica)
    ' ============================================================================================

    Private Sub ImpostaAgendaComePredefinitaCorrente(chiave As String)
        Try
            classeagenda.AgendaCorrente = If(chiave, "")
            _agendaPredefinitaImpostata = True
            Dim configurazione As New WGConfig()
            Dim daSalvare As String = If(String.IsNullOrWhiteSpace(chiave), TutteLeAgende, chiave)
            configurazione.SalvaValore("AgendaCorrentePredefinita", daSalvare)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpostaAgendaComePredefinitaCorrente - " & ex.Message)
        End Try
    End Sub

    ' Letta all'avvio: distingue "mai scelta" (chiede la prima volta che serve) da "scelta
    ' esplicitamente Tutte le agende" (non chiede più, mostra tutto come richiesto).
    Friend Sub CaricaAgendaPredefinitaCorrente()
        Try
            _agendaPredefinitaImpostata = False
            Dim configurazione As New WGConfig()
            Dim chiave As String = configurazione.LeggiValore("AgendaCorrentePredefinita")
            If String.IsNullOrWhiteSpace(chiave) Then Return

            If String.Equals(chiave, TutteLeAgende, StringComparison.Ordinal) Then
                classeagenda.AgendaCorrente = ""
                _agendaPredefinitaImpostata = True
                Return
            End If

            If String.IsNullOrWhiteSpace(classeagenda.NomeAgenda(chiave)) Then Return
            classeagenda.AgendaCorrente = chiave
            _agendaPredefinitaImpostata = True
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CaricaAgendaPredefinitaCorrente - " & ex.Message)
            _agendaPredefinitaImpostata = False
        End Try
    End Sub

    Private Sub CancellaAgendaPredefinitaCorrente()
        Try
            _agendaPredefinitaImpostata = False
            classeagenda.AgendaCorrente = ""
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore("AgendaCorrentePredefinita", "")
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CancellaAgendaPredefinitaCorrente - " & ex.Message)
        End Try
    End Sub

    ' Punto di ingresso comune per Nuovo appuntamento, Consulta, Cerca e Categorie: se non c'e'
    ' ancora un'agenda di lavoro la si chiede, se e' protetta e chiusa si chiede la password,
    ' altrimenti l'operazione richiesta parte subito.
    Friend Sub UsaAgendaEPoi(azione As Action)
        Try
            If classeagenda Is Nothing Then classeagenda = CLSAgenda.GetInstance()
            If Not _agendaPredefinitaImpostata Then
                _operazionePendenteAgenda = azione
                MostraElencoAgende()
                Return
            End If
            Dim corrente As String = classeagenda.AgendaCorrente
            If Not String.IsNullOrWhiteSpace(corrente) AndAlso classeagenda.AgendaProtetta(corrente) AndAlso Not classeagenda.AgendaSbloccata(corrente) Then
                _operazionePendenteAgenda = azione
                ChiediPasswordAgenda(corrente, ScopoPasswordAgenda.Apertura, "Password dell'agenda " & classeagenda.NomeAgenda(corrente))
                Return
            End If
            azione()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.UsaAgendaEPoi - " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggioAgenda(testo As String)
        Try
            Dim messaggi As New WGMessaggi()
            messaggi.Messaggio(testo)
            messaggi.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.MostraMessaggioAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneNuovaAgenda(sender As Object, e As EventArgs)
        Try
            ChiudiListaAgende()
            _agendaInLavorazione = ""
            ApriCampoNomeAgenda("Nome della nuova agenda", "")
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzioneNuovaAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRinominaAgenda(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = AgendaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggioAgenda("Scegli prima un'agenda.")
                Return
            End If
            ChiudiListaAgende()
            _agendaInLavorazione = chiave
            ApriCampoNomeAgenda("Nuovo nome dell'agenda", classeagenda.NomeAgenda(chiave))
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzioneRinominaAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoNomeAgenda(titolo As String, valore As String)
        Try
            _campiAgenda = New Campi()
            _campiAgenda.SalvaSuEscape = False
            _campiAgenda.Aggiungi(titolo, "agenda.nome", True, False, If(valore, ""))
            AddHandler _campiAgenda.Fine, AddressOf CampiAgendaFine
            AddHandler _campiAgenda.Chiusura, AddressOf CampiAgendaChiusura
            _campiAgenda.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ApriCampoNomeAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiAgenda()
        Try
            If _campiAgenda Is Nothing Then Return
            RemoveHandler _campiAgenda.Fine, AddressOf CampiAgendaFine
            RemoveHandler _campiAgenda.Chiusura, AddressOf CampiAgendaChiusura
            _campiAgenda = Nothing
        Catch
            _campiAgenda = Nothing
        End Try
    End Sub

    Private Sub CampiAgendaChiusura(sender As Object, e As EventArgs)
        Try
            LiberaCampiAgenda()
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CampiAgendaChiusura - " & ex.Message)
        End Try
    End Sub

    Private Sub CampiAgendaFine(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiAgenda IsNot Nothing Then nome = _campiAgenda.RecuperaValore("agenda.nome")
            LiberaCampiAgenda()

            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggioAgenda("Il nome è obbligatorio.")
            ElseIf String.IsNullOrWhiteSpace(_agendaInLavorazione) Then
                If String.IsNullOrWhiteSpace(classeagenda.CreaAgenda(nome)) Then
                    MostraMessaggioAgenda("Non è stato possibile creare l'agenda.")
                Else
                    MostraMessaggioAgenda("Agenda creata.")
                End If
            ElseIf classeagenda.RinominaAgenda(_agendaInLavorazione, nome) Then
                MostraMessaggioAgenda("Agenda rinominata.")
            Else
                MostraMessaggioAgenda("Non è stato possibile rinominare l'agenda.")
            End If
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CampiAgendaFine - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAgendaPredefinita(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = AgendaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggioAgenda("Scegli prima un'agenda.")
                Return
            End If
            ChiudiListaAgende()
            If classeagenda.ImpostaAgendaPredefinita(chiave) Then
                MostraMessaggioAgenda("Agenda predefinita cambiata.")
            Else
                MostraMessaggioAgenda("Non è stato possibile cambiare l'agenda predefinita.")
            End If
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzioneAgendaPredefinita - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaAgenda(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = AgendaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggioAgenda("Scegli prima un'agenda.")
                Return
            End If
            Dim eraAgendaPredefinitaCorrente As Boolean = _agendaPredefinitaImpostata AndAlso String.Equals(chiave, classeagenda.AgendaCorrente, StringComparison.Ordinal)
            ChiudiListaAgende()
            If classeagenda.EliminaAgenda(chiave) Then
                If eraAgendaPredefinitaCorrente Then CancellaAgendaPredefinitaCorrente()
                MostraMessaggioAgenda("Agenda eliminata. Gli appuntamenti sono stati spostati in un'altra agenda.")
            Else
                MostraMessaggioAgenda("Non è stato possibile eliminare l'agenda. Deve restarne almeno una.")
            End If
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzioneEliminaAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCollegaAgenda(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = AgendaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggioAgenda("Scegli prima un'agenda.")
                Return
            End If

            Dim account As List(Of ImpostazioniAccountMail)
            Using posta As New CLSMail()
                account = posta.OttieniAccountConAgenda(True)
            End Using
            If account Is Nothing OrElse account.Count = 0 Then
                MostraMessaggioAgenda("Nessun account con la sincronizzazione del calendario.")
                Return
            End If

            ChiudiListaAgende()
            _agendaInLavorazione = chiave
            _listaAccountAgenda = New WGMenuLista()
            _listaAccountAgenda.multiselezioneConsentita = False
            _listaAccountAgenda.impostaTitolo("Account a cui collegare l'agenda")
            _listaAccountAgenda.aggiungiElemento("Nessun account, agenda locale", NessunAccountAgenda)
            For Each impostazioni As ImpostazioniAccountMail In account
                Dim etichetta As String = impostazioni.NomeVisualizzato
                If String.IsNullOrWhiteSpace(etichetta) Then etichetta = impostazioni.IndirizzoEmail
                _listaAccountAgenda.aggiungiElemento(etichetta, impostazioni.Chiave)
            Next
            AddHandler _listaAccountAgenda.ElementoSelezionato, AddressOf AccountAgendaScelto
            AddHandler _listaAccountAgenda.escPremuto, AddressOf ListaAccountAgendaEsc
            _listaAccountAgenda.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzioneCollegaAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaAccountAgenda()
        Try
            If _listaAccountAgenda Is Nothing Then Return
            Dim precedente As WGMenuLista = _listaAccountAgenda
            _listaAccountAgenda = Nothing
            Try
                RemoveHandler precedente.ElementoSelezionato, AddressOf AccountAgendaScelto
                RemoveHandler precedente.escPremuto, AddressOf ListaAccountAgendaEsc
            Catch exHandler As Exception
                _logger.ScriviLog("MenuAgenda.ChiudiListaAccountAgenda handler - " & exHandler.Message)
            End Try
            If Not precedente.IsDisposed Then precedente.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ChiudiListaAccountAgenda - " & ex.Message)
            _listaAccountAgenda = Nothing
        End Try
    End Sub

    Private Sub ListaAccountAgendaEsc(sender As Object, e As EventArgs)
        Try
            ChiudiListaAccountAgenda()
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ListaAccountAgendaEsc - " & ex.Message)
        End Try
    End Sub

    Private Sub AccountAgendaScelto(id As String)
        Try
            ChiudiListaAccountAgenda()
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim account As String = id
            If String.Equals(id, NessunAccountAgenda, StringComparison.Ordinal) Then account = ""
            If classeagenda.CollegaAgendaAdAccount(_agendaInLavorazione, account, "") Then
                MostraMessaggioAgenda(If(account.Length = 0, "Agenda resa locale.", "Agenda collegata all'account."))
            Else
                MostraMessaggioAgenda("Non è stato possibile collegare l'agenda.")
            End If
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AccountAgendaScelto - " & ex.Message)
        End Try
    End Sub

    Private Sub OpzionePasswordAgenda(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = AgendaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggioAgenda("Scegli prima un'agenda.")
                Return
            End If
            ChiudiListaAgende()
            _agendaInLavorazione = chiave

            If Not classeagenda.AgendaProtetta(chiave) Then
                ChiediPasswordAgenda(chiave, ScopoPasswordAgenda.Impostazione, "Password con cui proteggere l'agenda")
                Return
            End If

            _menuProtezioneAgenda = New WGMenu()
            _menuProtezioneAgenda.aggiungi("Togli la password", AddressOf VoceTogliPasswordAgenda)
            _menuProtezioneAgenda.aggiungi("Chiudi l'agenda adesso", AddressOf VoceChiudiAgenda)
            _menuProtezioneAgenda.avvia("Protezione dell'agenda")
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.OpzionePasswordAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuProtezioneAgenda()
        Try
            If _menuProtezioneAgenda IsNot Nothing Then _menuProtezioneAgenda.chiudi()
            _menuProtezioneAgenda = Nothing
        Catch
            _menuProtezioneAgenda = Nothing
        End Try
    End Sub

    Private Sub VoceTogliPasswordAgenda(sender As Object, e As EventArgs)
        Try
            ChiudiMenuProtezioneAgenda()
            ChiediPasswordAgenda(_agendaInLavorazione, ScopoPasswordAgenda.Rimozione, "Password attuale dell'agenda")
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.VoceTogliPasswordAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub VoceChiudiAgenda(sender As Object, e As EventArgs)
        Try
            ChiudiMenuProtezioneAgenda()
            classeagenda.BloccaAgenda(_agendaInLavorazione)
            MostraMessaggioAgenda("Agenda chiusa. Per rivederne gli appuntamenti serve di nuovo la password.")
            MostraElencoAgende()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.VoceChiudiAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediPasswordAgenda(chiaveAgenda As String, scopo As ScopoPasswordAgenda, titolo As String)
        Try
            _agendaInLavorazione = If(chiaveAgenda, "")
            _scopoPasswordAgenda = scopo
            _campiPasswordAgenda = New Campi()
            _campiPasswordAgenda.SalvaSuEscape = False
            _campiPasswordAgenda.Aggiungi(titolo, "agenda.password", True, False, "")
            AddHandler _campiPasswordAgenda.Fine, AddressOf CampiPasswordAgendaFine
            AddHandler _campiPasswordAgenda.Chiusura, AddressOf CampiPasswordAgendaChiusura
            _campiPasswordAgenda.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ChiediPasswordAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiPasswordAgenda()
        Try
            If _campiPasswordAgenda Is Nothing Then Return
            RemoveHandler _campiPasswordAgenda.Fine, AddressOf CampiPasswordAgendaFine
            RemoveHandler _campiPasswordAgenda.Chiusura, AddressOf CampiPasswordAgendaChiusura
            _campiPasswordAgenda = Nothing
        Catch
            _campiPasswordAgenda = Nothing
        End Try
    End Sub

    Private Sub CampiPasswordAgendaChiusura(sender As Object, e As EventArgs)
        Try
            LiberaCampiPasswordAgenda()
            RipristinaDopoPasswordAgendaAnnullata()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CampiPasswordAgendaChiusura - " & ex.Message)
        End Try
    End Sub

    ' Se la password era stata chiesta per un'operazione in attesa (inserimento, consultazione,
    ' ricerca, categorie...) e viene annullata o sbagliata, l'operazione non parte e si torna al
    ' menu principale; altrimenti (gestione delle agende) si torna all'elenco agende.
    Private Sub RipristinaDopoPasswordAgendaAnnullata()
        Try
            If _operazionePendenteAgenda IsNot Nothing Then
                _operazionePendenteAgenda = Nothing
                MostraMenuPrincipaleAgenda()
            Else
                MostraElencoAgende()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.RipristinaDopoPasswordAgendaAnnullata - " & ex.Message)
        End Try
    End Sub

    Private Sub CampiPasswordAgendaFine(sender As Object, e As EventArgs)
        Try
            Dim password As String = ""
            If _campiPasswordAgenda IsNot Nothing Then password = _campiPasswordAgenda.RecuperaValore("agenda.password")
            LiberaCampiPasswordAgenda()

            If String.IsNullOrEmpty(password) Then
                MostraMessaggioAgenda("Password non indicata.")
                RipristinaDopoPasswordAgendaAnnullata()
                Return
            End If

            Select Case _scopoPasswordAgenda
                Case ScopoPasswordAgenda.Apertura
                    Dim trovata As String = classeagenda.TrovaAgendaPerPassword(password)
                    If String.IsNullOrWhiteSpace(trovata) Then
                        MostraMessaggioAgenda("Password errata.")
                        RipristinaDopoPasswordAgendaAnnullata()
                        Return
                    End If
                    If Not String.Equals(trovata, _agendaInLavorazione, StringComparison.Ordinal) Then
                        MostraMessaggioAgenda("Questa password apre l'agenda " & classeagenda.NomeAgenda(trovata) & ".")
                    End If
                    AgendaSbloccataERisolta(trovata)

                Case ScopoPasswordAgenda.Impostazione
                    If classeagenda.ImpostaPasswordAgenda(_agendaInLavorazione, password) Then
                        MostraMessaggioAgenda("Agenda protetta. Gli appuntamenti sono stati cifrati.")
                    Else
                        MostraMessaggioAgenda("Non è stato possibile proteggere l'agenda.")
                    End If
                    MostraElencoAgende()

                Case ScopoPasswordAgenda.Rimozione
                    If Not classeagenda.SbloccaAgenda(_agendaInLavorazione, password) Then
                        MostraMessaggioAgenda("Password errata.")
                    ElseIf classeagenda.TogliPasswordAgenda(_agendaInLavorazione) Then
                        MostraMessaggioAgenda("Password tolta. Gli appuntamenti sono di nuovo in chiaro.")
                    Else
                        MostraMessaggioAgenda("Non è stato possibile togliere la password.")
                    End If
                    MostraElencoAgende()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CampiPasswordAgendaFine - " & ex.Message)
        End Try
    End Sub

End Class