Imports wgcontrols
Imports WGDati
Imports WGMail
Imports System.Windows.Forms
Imports System.Globalization
Imports System.Xml
Imports System.IO
Imports System.Threading
Imports System.Collections.Generic

Partial Friend Class MenuAgenda
    Private guidaagenda As New GuidaAgenda
    Private classeagenda As CLSAgenda
    Private _config As WGConfig
    Private ReadOnly Property config As WGConfig
        Get
            If _config Is Nothing Then
                _config = New WGConfig()
            End If
            Return _config
        End Get
    End Property
    Private WithEvents campi As wgcontrols.Campi
    Private WithEvents menuPrincipale As WGMenu
    Private WithEvents menuRicercaObj As WGMenu
    Private WithEvents menuCategorieObj As WGMenu
    Private WithEvents menuConfigObj As WGMenu
    Private WithEvents menuAvvisiObj As WGMenu
    Private WithEvents menuSceltaScadenzaObj As WGMenu
    Private WithEvents menuNotificheObj As WGMenu
    Private WithEvents menuConsultaObj As WGMenu

    Private _menuOrariEsclusi As WGMenu
    Private _menuDurate As WGMenu
    Private menuDurataRic As WGMenu
    Private _menuSceltaOrario As WGMenu
    Private _menuListaOrariEsclusi As WGMenuLista
    Private _orarioDaEliminare As String

    Private Carica As Thread
    Private menuDate As WGMenuLista
    Private menuListaCat As WGMenuLista
    Private menuListaFreq As WGMenuLista
    Private menuListaSlot As WGMenuLista
    Private menuListaDettaglio As WGMenuLista
    Private menuListaAppuntamenti As WGMenuLista
    Private _tempMenuGiorni As WGMenuLista

    Private mnuPre As WGMenu

    Private oggetto As String = ""
    Private nome As String = ""
    Private data As String = ""
    Private dataFine As String = ""
    Private orainizio As String = ""
    Private orafine As String = ""
    Private frequenza As CLSAgenda.Frequenza
    Private note As String = ""
    Private preavviso As Integer
    Private categoriaSelezionata As String = ""
    Private idmodifica As String = ""
    Private idDuplica As String = ""
    Private idsOperazione As New List(Of String)
    Private catDaModificare As String = ""
    Private idCorrente As String = ""
    Private idDettaglioCorrente As String = ""

    Private campoGuida As WGCampo

    Private Enum ModalitaVisualizzazione
        Tutti
        Futuri
        Giornaliero
        RicercaTesto
        RicercaPeriodo
        RicercaMista
        Mese
        Categoria
        InScadenza
        Scaduti
        InScadenzaPerCategoria
        ScadutiPerCategoria
        Ricorrenze
    End Enum

    Private _modalitaCorrente As ModalitaVisualizzazione = ModalitaVisualizzazione.Tutti
    Private _filtroData As String = ""
    Private _filtroTesto As String = ""
    Private _filtroDataInizio As DateTime
    Private _filtroDataFine As DateTime
    Private _filtroCategoria As String = ""
    Private _filtroMese As Integer = 0
    Private _filtroAnno As Integer = 0

    Private ReadOnly _logger As Logger
    Private WithEvents attesa As WGProcessoInCorso
    Private _uiContext As SynchronizationContext
    Private _tempElencoAppuntamenti As List(Of CLSAgenda.AppuntamentoLista)
    Private _tempListaDinamica As List(Of ElementoLista)

    Private _cts As CancellationTokenSource

    Private _threadResultCats As List(Of String)
    Private _threadResultAvvisi As Tuple(Of Integer, Integer)
    Private _threadResultInt As Integer
    Private _threadResultBool As Boolean
    Private _threadResultDateDisp As List(Of String)
    Private _threadResultSlots As List(Of String)
    Private _threadResultAccountAgenda As List(Of ImpostazioniAccountMail)
    Private _threadResultCalendariDav As List(Of CalendarioDav)

    ' Stato dell'account/calendario attualmente in configurazione nel menu "Sincronizzazione
    ' calendari" (vedi menuSincronizzazioneCalendari e le funzioni collegate).
    Private menuListaSync As WGMenuLista
    Private _syncAccountCorrente As String = ""
    Private _syncNomeAccountCorrente As String = ""
    Private _syncCalendarioCorrente As String = ""
    Private _syncNomeCalendarioCorrente As String = ""

    Private _catDaEliminare As String
    Private _catDestinazioneSpostamento As String
    Private _dettaglioSovrapposizione As CLSAgenda.AppuntamentoDettaglio
    Private _dtCorrenteSlot As DateTime

    Private _search_durataMin As Integer
    Private _search_durataMax As Integer
    Private _search_oraInizioLavoro As DateTime
    Private _search_oraFineLavoro As DateTime
    Private _search_giorniConsentiti As List(Of DayOfWeek)
    Private _search_escludiOrariLavorativi As Boolean
    Private _search_orariDaEscludere As List(Of Tuple(Of TimeSpan, TimeSpan))
    Private _search_giorniRicerca As Integer = 365

    Private tmpDurOre As Integer
    Private tmpGg As Integer
    Private tmpMin As Integer
    Private _modalitaEvento As Boolean = False

    Public Sub New()
        Try
            _logger = New Logger()
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("MenuAgenda.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function FormattaDataAgenda(valoreData As String) As String
        Try
            If String.IsNullOrWhiteSpace(valoreData) Then
                Return "data non definita"
            End If
            Return valoreData
        Catch ex As Exception
            _logger.ScriviLog("Errore in FormattaDataAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "data non definita"
        End Try
    End Function

    Private Sub MostraGuidaMenu(testoGuida As String, titoloGuida As String)
        If campoGuida IsNot Nothing Then
            Try
                RemoveHandler campoGuida.chiusura, AddressOf chiusuraCampoGuida
            Catch ex As Exception
            End Try
        End If
        campoGuida = New WGCampo()
        AddHandler campoGuida.chiusura, AddressOf chiusuraCampoGuida
        campoGuida.init("Guida - " & titoloGuida, testoGuida, True, True, False, False)
        campoGuida.avvia()
    End Sub

    Private Sub chiusuraCampoGuida()
        If campoGuida IsNot Nothing Then
            RemoveHandler campoGuida.chiusura, AddressOf chiusuraCampoGuida
            campoGuida = Nothing
        End If
    End Sub

    Private Sub AttendiThread(t As Thread)
        While t.IsAlive
            Application.DoEvents()
            Thread.Sleep(10)
        End While

        If attesa IsNot Nothing Then
            attesa.Fine()
            attesa = Nothing
        End If
    End Sub

    Private Sub InizializzaCategorieThread()
        Dim cats As List(Of String) = classeagenda.OttieniElencoCategorie()

        If cats.Count = 0 Then
            classeagenda.AggiungiCategoria("Lavoro")
            classeagenda.AggiungiCategoria("Personale")
            classeagenda.AggiungiCategoria("Urgente")
            classeagenda.AggiungiCategoria("Famiglia")
        End If
    End Sub

    Private Sub ContaAvvisiThread()
        _threadResultAvvisi = classeagenda.ContaAvvisiNotifiche()
    End Sub

    Private Sub ContaAppuntamentiFuturiThread()
        _threadResultInt = classeagenda.ContaAppuntamentiFuturi()
    End Sub

    Private Sub EseguitiMassivoAttivitaThread()
        If classeagenda.SegnaPassati(DateTime.Now.ToString("dd/MM/yyyy HH:mm")) Then
            _threadResultInt += 1
        End If
        Dim inScadenzaCount As Integer = classeagenda.ContaAppuntamentiInScadenza()

        If inScadenzaCount > 0 Then
            Dim listaInScadenza As List(Of CLSAgenda.AppuntamentoLista) = classeagenda.OttieniAppuntamentiInScadenzaPaginati(0, 1000)

            Dim idsInScadenza As New List(Of String)
            For Each a In listaInScadenza
                idsInScadenza.Add(a.Id)
            Next
            If idsInScadenza.Count > 0 Then
                If classeagenda.SegnaPassatiMassivi(idsInScadenza) Then
                    _threadResultInt += idsInScadenza.Count
                End If
            End If
        End If
    End Sub

    Private Sub OttieniCategorieThread()
        _threadResultCats = classeagenda.OttieniElencoCategorie()
    End Sub

    Private Sub ThreadCaricaDatiWrapper()
        ThreadCaricaDati(_cts.Token)
    End Sub

    Private Sub VerificaSovrapposizioniThread()
        _threadResultBool = classeagenda.VerificaSovrapposizioni(_dettaglioSovrapposizione)
    End Sub

    Private Sub RicercaDateLibereThread()
        _threadResultDateDisp = classeagenda.RicercaDateLibere(
            DateTime.Today, _search_giorniRicerca, _search_durataMin,
            _search_oraInizioLavoro, _search_oraFineLavoro,
            _search_giorniConsentiti, _search_escludiOrariLavorativi,
            _search_orariDaEscludere, 50)
    End Sub

    Private Sub RicercaOrariLiberiThread()
        _threadResultSlots = classeagenda.RicercaOrariLiberi(_dtCorrenteSlot, _search_durataMin, _search_durataMax, _search_oraInizioLavoro, _search_oraFineLavoro, _search_escludiOrariLavorativi, _search_orariDaEscludere)
    End Sub

    Private Sub RicRicTh()
        Try
            Dim dt As DateTime
            If Not DateTime.TryParseExact(data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                dt = DateTime.Today
            End If
            _threadResultSlots = classeagenda.RicercaSlotGiornalieriConsecutivi(tmpGg, tmpMin, dt)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.RicRicTh - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _threadResultSlots = New List(Of String)()
        End Try
    End Sub

    Private Sub SalvaAppuntamentoThread()
        If idmodifica <> "" Then
            _threadResultBool = classeagenda.ModificaAppuntamento(idmodifica, oggetto, nome, orainizio, orafine, note, preavviso, frequenza, data, categoriaSelezionata, dataFine)
        Else
            _threadResultBool = classeagenda.AggiungiAppuntamento(oggetto, nome, orainizio, orafine, note, preavviso, frequenza, data, categoriaSelezionata, dataFine)
        End If
        If _threadResultBool Then AvviaSincronizzazioneAgendaDopoModifica()
    End Sub

    Private Sub UI_MostraListaWrapper(state As Object)
        UI_MostraLista()
    End Sub

    Private Sub MsgAnnullataWrapper(state As Object)
        If attesa IsNot Nothing Then attesa.Fine()
        Dim msg As New WGMessaggi()
        msg.Messaggio("Operazione annullata.")
        msg.ShowDialog()
    End Sub

    Private Sub FineAttesaWrapper(state As Object)
        If attesa IsNot Nothing Then attesa.Fine()
    End Sub

    Private Sub EseguiAutoCompletamentoSilenteThread()
        Try
            If config.LeggiValore("AutoCompletamentoAttivo") = "1" Then
                Dim minutiStr As String = config.LeggiValore("AutoCompletamentoMinuti")
                Dim minuti As Integer = 0
                Integer.TryParse(minutiStr, minuti)
                classeagenda.EseguiAutoCompletamentoAppuntamenti(minuti)
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore EseguiAutoCompletamentoSilenteThread - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub avvia()
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            classeagenda = CLSAgenda.GetInstance()

            Dim orariEsclusiDef As String = config.LeggiValore("OrariEsclusi")
            If String.IsNullOrEmpty(orariEsclusiDef) Then
                config.SalvaValore("OrariEsclusi", "00:00-06:00")
            End If

            Dim tAuto As New Thread(AddressOf EseguiAutoCompletamentoSilenteThread)
            tAuto.Start()

            attesa = New WGProcessoInCorso()
            attesa.Avvia("Avvio agenda in corso...", False)

            Dim tInit As New Thread(AddressOf InizializzaCategorieThread)
            tInit.Start()
            AttendiThread(tInit)

            CaricaAgendaPredefinitaCorrente()
            If _agendaPredefinitaImpostata AndAlso Not String.IsNullOrWhiteSpace(classeagenda.AgendaCorrente) AndAlso
               classeagenda.AgendaProtetta(classeagenda.AgendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(classeagenda.AgendaCorrente) Then
                _operazionePendenteAgenda = AddressOf MostraMenuPrincipaleAgenda
                ChiediPasswordAgenda(classeagenda.AgendaCorrente, ScopoPasswordAgenda.Apertura,
                    "Password dell'agenda " & classeagenda.NomeAgenda(classeagenda.AgendaCorrente))
            Else
                MostraMenuPrincipaleAgenda()
            End If
        Catch ex As InvalidOperationException
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore avvio menu: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("InvalidOperationException in avvia - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore generico menu: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in avvia - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuPrincipaleAgenda()
        Try
            menuPrincipale = New WGMenu()
            menuPrincipale.aggiungi("Nuovo appuntamento", AddressOf nuovo)
            menuPrincipale.aggiungi("Consulta", AddressOf menuConsulta)
            menuPrincipale.aggiungi("Cerca", AddressOf menuCerca)
            menuPrincipale.aggiungi("Categorie", AddressOf menuCategorie)
            menuPrincipale.aggiungi("Agende", AddressOf VoceAgende)
            menuPrincipale.aggiungi("Configurazione", AddressOf menuConfigurazione)
            menuPrincipale.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.MenuPrincipale, "Principale"))

            menuPrincipale.avvia("Menu - Agenda Digitale")
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.MostraMenuPrincipaleAgenda - " & ex.Message)
        End Try
    End Sub

    Public Sub consultascadenza()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Recupero informazioni in corso...", False)

            Dim tAvvisi As New Thread(AddressOf ContaAvvisiThread)
            tAvvisi.Start()
            AttendiThread(tAvvisi)

            Dim countScaduti As Integer = _threadResultAvvisi.Item1
            Dim countInScadenza As Integer = _threadResultAvvisi.Item2

            menuAvvisiObj = New WGMenu()
            menuAvvisiObj.aggiungi($"Visualizza attività scadute ({countScaduti})", AddressOf VisualizzaScadutiMenu)
            menuAvvisiObj.aggiungi($"Visualizza attività in arrivo ({countInScadenza})", AddressOf VisualizzaInScadenzaMenu)
            menuAvvisiObj.aggiungi("Segna tutte le attività come completate", AddressOf ImpostaTuttiEseguitiMassivo)
            menuAvvisiObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.MenuAvvisi, "Avvisi e Scadenze"))

            menuAvvisiObj.avvia("Menu - Gestione Scadenze e Notifiche")
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore consultascadenza - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnMenuAvvisiRiattivato(sender As Object, e As EventArgs) Handles menuAvvisiObj.MenuRiattivato
        Try
            If menuAvvisiObj IsNot Nothing Then
                menuAvvisiObj.chiudi()
                consultascadenza()
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub VisualizzaInScadenzaMenu()
        Try
            menuSceltaScadenzaObj = New WGMenu()
            menuSceltaScadenzaObj.aggiungi("Elenco completo", AddressOf VisualizzaInScadenzaTutti)
            menuSceltaScadenzaObj.aggiungi("Filtra per categoria", AddressOf VisualizzaInScadenzaCategoriaScelta)
            menuSceltaScadenzaObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SceltaScadenza, "Attività in scadenza"))
            menuSceltaScadenzaObj.avvia("Attività in scadenza")
        Catch ex As Exception
            _logger.ScriviLog("Errore VisualizzaInScadenzaMenu - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VisualizzaScadutiMenu()
        Try
            menuSceltaScadenzaObj = New WGMenu()
            menuSceltaScadenzaObj.aggiungi("Elenco completo", AddressOf VisualizzaScadutiTutti)
            menuSceltaScadenzaObj.aggiungi("Filtra per categoria", AddressOf VisualizzaScadutiCategoriaScelta)
            menuSceltaScadenzaObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SceltaScadenza, "Attività scadute"))
            menuSceltaScadenzaObj.avvia("Attività scadute")
        Catch ex As Exception
            _logger.ScriviLog("Errore VisualizzaScadutiMenu - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Prima di mostrare qualunque elenco di appuntamenti, verifica che non ci sia di mezzo
    ' un'agenda protetta e ancora bloccata: se c'e', chiede la password (una alla volta, come
    ' avviene gia' in avvia()) e riprova solo dopo lo sblocco corretto. Se l'utente annulla la
    ' richiesta, l'elenco semplicemente non si apre.
    Private Sub RichiediSbloccoEProsegui(azioneProsegui As Action)
        Try
            Dim daSbloccare As String = ""
            If Not String.IsNullOrWhiteSpace(classeagenda.AgendaCorrente) Then
                If classeagenda.AgendaProtetta(classeagenda.AgendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(classeagenda.AgendaCorrente) Then
                    daSbloccare = classeagenda.AgendaCorrente
                End If
            Else
                For Each voce As CLSAgenda.DescrizioneAgenda In classeagenda.OttieniAgende()
                    If voce.Protetta AndAlso Not classeagenda.AgendaSbloccata(voce.Chiave) Then
                        daSbloccare = voce.Chiave
                        Exit For
                    End If
                Next
            End If

            If String.IsNullOrWhiteSpace(daSbloccare) Then
                azioneProsegui.Invoke()
                Return
            End If

            _operazionePendenteAgenda = Sub() RichiediSbloccoEProsegui(azioneProsegui)
            ChiediPasswordAgenda(daSbloccare, ScopoPasswordAgenda.Apertura,
                "Password dell'agenda " & classeagenda.NomeAgenda(daSbloccare))
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.RichiediSbloccoEProsegui - " & ex.Message & Environment.NewLine & ex.StackTrace)
            azioneProsegui.Invoke()
        End Try
    End Sub

    Private Sub VisualizzaRicorrenze()
        RichiediSbloccoEProsegui(Sub()
                                      _modalitaCorrente = ModalitaVisualizzazione.Ricorrenze
                                      AvviaCaricamento()
                                  End Sub)
    End Sub

    Private Sub VisualizzaInScadenzaTutti()
        RichiediSbloccoEProsegui(Sub()
                                      _modalitaCorrente = ModalitaVisualizzazione.InScadenza
                                      AvviaCaricamento()
                                  End Sub)
    End Sub

    Private Sub VisualizzaScadutiTutti()
        RichiediSbloccoEProsegui(Sub()
                                      _modalitaCorrente = ModalitaVisualizzazione.Scaduti
                                      AvviaCaricamento()
                                  End Sub)
    End Sub

    Private Sub VisualizzaInScadenzaCategoriaScelta()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona Filtro Categoria (In Scadenza)")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) MostraInScadenzaCategoriaHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneCategoriaFiltro, "Filtro Categoria"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna categoria disponibile nel sistema.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore VisualizzaInScadenzaCategoriaScelta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraInScadenzaCategoriaHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        MostraInScadenzaCategoria(elemento, menuListaCat)
    End Sub

    Private Sub ChiudiMenuListaCat()
        menuListaCat.abbandona()
    End Sub

    Private Sub MostraInScadenzaCategoria(cat As String, lista As WGMenuLista)
        Try
            lista.abbandona()
            RichiediSbloccoEProsegui(Sub()
                                          _modalitaCorrente = ModalitaVisualizzazione.InScadenzaPerCategoria
                                          _filtroCategoria = cat
                                          AvviaCaricamento()
                                      End Sub)
        Catch ex As Exception
            _logger.ScriviLog("Errore MostraInScadenzaCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VisualizzaScadutiCategoriaScelta()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona Filtro Categoria (Scaduti)")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) MostraScadutiCategoriaHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneCategoriaFiltro, "Filtro Categoria"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna categoria disponibile nel sistema.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore VisualizzaScadutiCategoriaScelta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraScadutiCategoriaHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        MostraScadutiCategoria(elemento, menuListaCat)
    End Sub

    Private Sub MostraScadutiCategoria(cat As String, lista As WGMenuLista)
        Try
            lista.abbandona()
            RichiediSbloccoEProsegui(Sub()
                                          _modalitaCorrente = ModalitaVisualizzazione.ScadutiPerCategoria
                                          _filtroCategoria = cat
                                          AvviaCaricamento()
                                      End Sub)
        Catch ex As Exception
            _logger.ScriviLog("Errore MostraScadutiCategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTuttiEseguitiMassivo()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Analisi attività...", False)

            Dim tAvvisi As New Thread(AddressOf ContaAvvisiThread)
            tAvvisi.Start()
            AttendiThread(tAvvisi)
            Dim avvisi As Tuple(Of Integer, Integer) = _threadResultAvvisi

            Dim countScaduti As Integer = avvisi.Item1
            Dim countInScadenza As Integer = avvisi.Item2
            Dim totaleDaProcessare As Integer = countScaduti + countInScadenza

            If totaleDaProcessare = 0 Then
                Dim msgInfo As New WGMessaggi()
                msgInfo.Messaggio("Non ci sono attività scadute o in scadenza da aggiornare.")
                msgInfo.ShowDialog()
                Exit Sub
            End If

            Dim testoDomanda As String = ""
            If countScaduti > 0 AndAlso countInScadenza > 0 Then
                testoDomanda = $"Confermi di voler segnare come completate {totaleDaProcessare} attività ({countScaduti} scadute e {countInScadenza} in arrivo)?"
            ElseIf countScaduti > 0 Then
                testoDomanda = $"Confermi di voler segnare come completate tutte le {countScaduti} attività scadute?"
            Else
                testoDomanda = $"Confermi di voler segnare come completate tutte le {countInScadenza} attività attualmente in scadenza?"
            End If

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf ConfermaEseguitiMassivo
            domanda.avvia(testoDomanda)
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore ImpostaTuttiEseguitiMassivo - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaEseguitiMassivo(risp As DialogResult)
        If risp = DialogResult.Yes Then
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Aggiornamento in corso...", False)

            _threadResultInt = 0
            Dim tAgg As New Thread(AddressOf EseguitiMassivoAttivitaThread)
            tAgg.Start()
            AttendiThread(tAgg)

            Dim msg As New WGMessaggi()
            msg.Messaggio($"Operazione completata.{Environment.NewLine}Agenda aggiornata correttamente.")
            msg.ShowDialog()
        End If
    End Sub

    Private Function IsModalitaScadenza() As Boolean
        Return _modalitaCorrente = ModalitaVisualizzazione.InScadenza OrElse
               _modalitaCorrente = ModalitaVisualizzazione.Scaduti OrElse
               _modalitaCorrente = ModalitaVisualizzazione.InScadenzaPerCategoria OrElse
               _modalitaCorrente = ModalitaVisualizzazione.ScadutiPerCategoria
    End Function

    Public Sub menuConfigurazione()
        Try
            menuConfigObj = New WGMenu()
            menuConfigObj.aggiungi("Agenda predefinita per le operazioni", AddressOf VoceAgende)
            menuConfigObj.aggiungi("Gestione del Tempo", AddressOf menuGestioneTempo)
            menuConfigObj.aggiungi("Impostazioni Appuntamenti", AddressOf menuImpostazioniAppuntamenti)
            menuConfigObj.aggiungi("Avvisi e Automazioni", AddressOf menuAvvisiAutomazioni)
            menuConfigObj.aggiungi("Ricerca e Visualizzazione", AddressOf menuRicercaVisualizzazione)
            menuConfigObj.aggiungi("Sincronizzazione con il server", AddressOf menuSincronizzazioneAgenda)
            menuConfigObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Configurazione, "Configurazione"))
            menuConfigObj.avvia("Configurazione")
        Catch ex As Exception
            _logger.ScriviLog("Errore menuConfigurazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuSincronizzazioneAgenda()
        Try
            Dim mnu As New WGMenu()
            mnu.aggiungi("Sincronizza adesso", AddressOf SincronizzaAgendaAdesso)

            Dim stato As String = "Attiva"
            If SincronizzazioneWG.AgendaAutomaticaAttiva() Then stato = "Disattiva"
            mnu.aggiungi(stato & " la sincronizzazione automatica", AddressOf CambiaStatoSincronizzazioneAgenda)

            mnu.aggiungi("Intervallo di sincronizzazione, adesso " & SincronizzazioneWG.IntervalloAgendaMinuti().ToString() & " minuti", AddressOf ImpostaIntervalloSincronizzazioneAgenda)
            mnu.avvia("Sincronizzazione con il server", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore menuSincronizzazioneAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SincronizzaAgendaAdesso()
        Try
            If Not SincronizzazioneWG.EsisteAgendaSincronizzabile() Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Nessun account di posta con il supporto alla sincronizzazione dei calendari.")
                avviso.ShowDialog()
                Exit Sub
            End If

            attesa = New WGProcessoInCorso()
            attesa.Avvia("Sincronizzazione dell'agenda in corso...", False)

            Dim tSinc As New Thread(AddressOf SincronizzaAgendaThread)
            tSinc.Start()
            AttendiThread(tSinc)

            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If

            Dim msg As New WGMessaggi()
            msg.Messaggio(SincronizzazioneWG.UltimoMessaggio)
            msg.ShowDialog()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore SincronizzaAgendaAdesso - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SincronizzaAgendaThread()
        Try
            _threadResultInt = SincronizzazioneWG.SincronizzaAgenda()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.SincronizzaAgendaThread - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _threadResultInt = 0
        End Try
    End Sub

    Private Sub AvviaSincronizzazioneAgendaDopoModifica()
        Try
            If Not SincronizzazioneWG.AgendaAutomaticaAttiva() Then Exit Sub
            If Not SincronizzazioneWG.EsisteAgendaSincronizzabile() Then Exit Sub
            SincronizzazioneWG.SincronizzaAgendaInSottofondo()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AvviaSincronizzazioneAgendaDopoModifica - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CambiaStatoSincronizzazioneAgenda()
        Try
            Dim nuovoStato As Boolean = Not SincronizzazioneWG.AgendaAutomaticaAttiva()
            SincronizzazioneWG.ImpostaAgendaAutomatica(nuovoStato)
            RiallineaPreavvisiAgenda()

            Dim msg As New WGMessaggi()
            If nuovoStato Then
                msg.Messaggio("Sincronizzazione automatica dell'agenda attivata.")
            Else
                msg.Messaggio("Sincronizzazione automatica dell'agenda disattivata.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore CambiaStatoSincronizzazioneAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaIntervalloSincronizzazioneAgenda()
        Try
            Dim mnu As New WGMenu()
            mnu.aggiungi("Ogni 5 minuti", AddressOf IntervalloAgendaCinque)
            mnu.aggiungi("Ogni 15 minuti", AddressOf IntervalloAgendaQuindici)
            mnu.aggiungi("Ogni 30 minuti", AddressOf IntervalloAgendaTrenta)
            mnu.aggiungi("Ogni ora", AddressOf IntervalloAgendaSessanta)
            mnu.aggiungi("Ogni 3 ore", AddressOf IntervalloAgendaCentottanta)
            mnu.aggiungi("Una volta al giorno", AddressOf IntervalloAgendaGiornaliero)
            mnu.avvia("Ogni quanto sincronizzare l'agenda", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore ImpostaIntervalloSincronizzazioneAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub IntervalloAgendaCinque()
        SalvaIntervalloAgenda(5)
    End Sub

    Private Sub IntervalloAgendaQuindici()
        SalvaIntervalloAgenda(15)
    End Sub

    Private Sub IntervalloAgendaTrenta()
        SalvaIntervalloAgenda(30)
    End Sub

    Private Sub IntervalloAgendaSessanta()
        SalvaIntervalloAgenda(60)
    End Sub

    Private Sub IntervalloAgendaCentottanta()
        SalvaIntervalloAgenda(180)
    End Sub

    Private Sub IntervalloAgendaGiornaliero()
        SalvaIntervalloAgenda(1440)
    End Sub

    Private Sub SalvaIntervalloAgenda(minuti As Integer)
        Try
            SincronizzazioneWG.ImpostaIntervalloAgendaMinuti(minuti)
            RiallineaPreavvisiAgenda()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Intervallo di sincronizzazione impostato a " & minuti.ToString() & " minuti.")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore SalvaIntervalloAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RiallineaPreavvisiAgenda()
        Try
            If PreavvisoManager.IstanzaAttiva IsNot Nothing Then
                PreavvisoManager.IstanzaAttiva.RiallineaTimerSincronizzazione()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore RiallineaPreavvisiAgenda - " & ex.Message)
        End Try
    End Sub

    Private Sub menuGestioneTempo()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Giorni lavorativi", AddressOf impostaGiorniLavorativi)
        mnu.aggiungi("Orario lavorativo", AddressOf impostaOrarioLavoro)
        mnu.aggiungi("Pause e orari esclusi", AddressOf gestisciOrariEsclusi)
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.GestioneTempo, "Gestione del Tempo"))
        mnu.avvia("Gestione del Tempo", True)
    End Sub

    Private Sub menuImpostazioniAppuntamenti()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Categoria predefinita", AddressOf impostaCategoriaDefault)
        mnu.aggiungi("Durata appuntamenti", AddressOf impostaDurate)
        mnu.aggiungi("Sincronizzazione calendari", AddressOf menuSincronizzazioneCalendari)
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Impostazioni Appuntamenti"))
        mnu.avvia("Impostazioni Appuntamenti", True)
    End Sub

    ' ============================================================================================
    ' SINCRONIZZAZIONE CALENDARI: per ogni account con agenda CalDAV configurata, permette di
    ' scegliere in quale Categoria locale devono fluire gli appuntamenti sincronizzati (a livello
    ' di intero account, e/o per ogni singolo calendario dell'account quando l'account ne ha piu'
    ' di uno), e quali calendari includere nella sincronizzazione. Se l'utente non sceglie nulla,
    ' resta il comportamento di sempre: nessuna categoria e un solo calendario predefinito.
    ' ============================================================================================

    Private Sub CaricaAccountAgendaThread()
        Try
            Using posta As New CLSMail()
                _threadResultAccountAgenda = posta.OttieniAccountConAgenda(True)
            End Using
        Catch ex As Exception
            _logger.ScriviLog("Errore CaricaAccountAgendaThread - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _threadResultAccountAgenda = New List(Of ImpostazioniAccountMail)
        End Try
    End Sub

    Private Sub menuSincronizzazioneCalendari()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento account in corso...", False)

            Dim t As New Thread(AddressOf CaricaAccountAgendaThread)
            t.Start()
            AttendiThread(t)
            Dim account As List(Of ImpostazioniAccountMail) = _threadResultAccountAgenda

            If account Is Nothing OrElse account.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun account con sincronizzazione calendari (CalDAV) configurato.")
                msg.ShowDialog()
                Return
            End If

            menuListaSync = New WGMenuLista()
            menuListaSync.impostaTitolo("Seleziona Account")
            menuListaSync.multiselezioneConsentita = False

            Dim elementi As New List(Of ElementoLista)
            For Each a As ImpostazioniAccountMail In account
                Dim label As String = If(String.IsNullOrWhiteSpace(a.NomeVisualizzato), a.IndirizzoEmail, a.NomeVisualizzato)
                elementi.Add(New ElementoLista(label, a.Chiave))
            Next
            menuListaSync.CaricaListaDinamica(elementi)

            AddHandler menuListaSync.ElementoSelezionato, Sub(v As String) AccountSincronizzazioneSceltoHandler(v)
            AddHandler menuListaSync.escPremuto, AddressOf ChiudiMenuListaSync
            menuListaSync.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
            menuListaSync.avvia()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore menuSincronizzazioneCalendari - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuListaSync()
        Try
            If menuListaSync IsNot Nothing Then menuListaSync.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Errore ChiudiMenuListaSync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AccountSincronizzazioneSceltoHandler(v As String)
        Dim listaid As List(Of String) = menuListaSync.ottieniElementi()
        Dim chiaveAccount As String = listaid(0)
        Try
            _syncAccountCorrente = chiaveAccount
            _syncNomeAccountCorrente = chiaveAccount
            If _threadResultAccountAgenda IsNot Nothing Then
                For Each a As ImpostazioniAccountMail In _threadResultAccountAgenda
                    If a.Chiave = chiaveAccount Then
                        _syncNomeAccountCorrente = If(String.IsNullOrWhiteSpace(a.NomeVisualizzato), a.IndirizzoEmail, a.NomeVisualizzato)
                        Exit For
                    End If
                Next
            End If
            menuListaSync.abbandona()
            menuOpzioniAccountSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("Errore AccountSincronizzazioneSceltoHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuOpzioniAccountSincronizzazione()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Categoria per gli appuntamenti dell'account (" & _syncNomeAccountCorrente & ")", AddressOf impostaCategoriaAccountSincronizzazione)
        mnu.aggiungi("Calendari dell'account", AddressOf menuCalendariAccountSincronizzazione)
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
        mnu.avvia("Sincronizzazione: " & _syncNomeAccountCorrente, True)
    End Sub

    ' Categoria di ripiego per l'intero account (usata quando il calendario di provenienza non ha
    ' una categoria propria configurata, o quando l'account ha un solo calendario e non si vuole
    ' configurare ogni singolo calendario separatamente).
    Private Sub impostaCategoriaAccountSincronizzazione()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            menuListaCat = New WGMenuLista()
            menuListaCat.impostaTitolo("Categoria per '" & _syncNomeAccountCorrente & "'")
            menuListaCat.multiselezioneConsentita = False

            Dim attuale As String = classeagenda.OttieniCategoriaSincronizzazioneAccount(_syncAccountCorrente)

            Dim elementi As New List(Of ElementoLista)
            Dim labelNessuna As String = "Nessuna"
            If String.IsNullOrWhiteSpace(attuale) Then labelNessuna &= " (Attuale)"
            elementi.Add(New ElementoLista(labelNessuna, ""))
            If cats IsNot Nothing Then
                For Each c As String In cats
                    Dim label As String = c
                    If Not String.IsNullOrWhiteSpace(attuale) AndAlso String.Equals(c, attuale, StringComparison.OrdinalIgnoreCase) Then label &= " (Attuale)"
                    elementi.Add(New ElementoLista(label, c))
                Next
            End If
            menuListaCat.CaricaListaDinamica(elementi)

            AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) FineImpostaCategoriaAccountSyncHandler(v)
            AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
            menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
            menuListaCat.avvia()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore impostaCategoriaAccountSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineImpostaCategoriaAccountSyncHandler(v As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim cat As String = listaid(0)
        Try
            classeagenda.ImpostaCategoriaSincronizzazioneAccount(_syncAccountCorrente, cat)
            Dim msg As New WGMessaggi()
            If String.IsNullOrWhiteSpace(cat) Then
                msg.Messaggio("Nessuna categoria impostata per l'account '" & _syncNomeAccountCorrente & "': gli appuntamenti sincronizzati resteranno senza categoria (a meno di una categoria specifica del calendario di provenienza).")
            Else
                msg.Messaggio("Categoria '" & cat & "' impostata per gli appuntamenti sincronizzati dell'account '" & _syncNomeAccountCorrente & "'.")
            End If
            msg.ShowDialog()
            menuListaCat.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Errore FineImpostaCategoriaAccountSyncHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CaricaCalendariAccountThread()
        Try
            Using posta As New CLSMail()
                _threadResultCalendariDav = posta.OttieniElencoCalendari(_syncAccountCorrente)
            End Using
        Catch ex As Exception
            _logger.ScriviLog("Errore CaricaCalendariAccountThread - " & ex.Message & Environment.NewLine & ex.StackTrace)
            _threadResultCalendariDav = New List(Of CalendarioDav)
        End Try
    End Sub

    ' Elenca i calendari CalDAV trovati sull'account (interrogando il server) mostrando, per
    ' ciascuno, se e' incluso nella sincronizzazione e in quale categoria fa fluire i suoi
    ' appuntamenti, secondo quanto eventualmente gia' configurato in precedenza.
    Private Sub menuCalendariAccountSincronizzazione()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Ricerca calendari sul server in corso...", False)

            Dim t As New Thread(AddressOf CaricaCalendariAccountThread)
            t.Start()
            AttendiThread(t)
            Dim calendari As List(Of CalendarioDav) = _threadResultCalendariDav

            If calendari Is Nothing OrElse calendari.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun calendario trovato sul server per questo account.")
                msg.ShowDialog()
                Return
            End If

            Dim configurati As List(Of CLSAgenda.ImpostazioneCalendarioSincronizzazione) = classeagenda.OttieniCalendariConfigurati(_syncAccountCorrente)

            menuListaSync = New WGMenuLista()
            menuListaSync.impostaTitolo("Calendari di '" & _syncNomeAccountCorrente & "'")
            menuListaSync.multiselezioneConsentita = False

            Dim elementi As New List(Of ElementoLista)
            For Each cal As CalendarioDav In calendari
                Dim config As CLSAgenda.ImpostazioneCalendarioSincronizzazione = Nothing
                For Each c In configurati
                    If String.Equals(c.UrlCalendario, cal.Url, StringComparison.OrdinalIgnoreCase) Then
                        config = c
                        Exit For
                    End If
                Next

                Dim stato As String
                If config Is Nothing Then
                    stato = "non incluso, nessuna categoria"
                ElseIf config.Selezionato Then
                    stato = "incluso, categoria: " & If(String.IsNullOrWhiteSpace(config.Categoria), "Nessuna", config.Categoria)
                Else
                    stato = "non incluso, categoria: " & If(String.IsNullOrWhiteSpace(config.Categoria), "Nessuna", config.Categoria)
                End If

                Dim label As String = cal.Nome & " (" & stato & ")"
                elementi.Add(New ElementoLista(label, cal.Url))
            Next
            menuListaSync.CaricaListaDinamica(elementi)

            AddHandler menuListaSync.ElementoSelezionato, Sub(v As String) CalendarioSincronizzazioneSceltoHandler(v)
            AddHandler menuListaSync.escPremuto, AddressOf ChiudiMenuListaSync
            menuListaSync.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
            menuListaSync.avvia()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore menuCalendariAccountSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CalendarioSincronizzazioneSceltoHandler(v As String)
        Dim listaid As List(Of String) = menuListaSync.ottieniElementi()
        Dim urlCalendario As String = listaid(0)
        Try
            _syncCalendarioCorrente = urlCalendario
            _syncNomeCalendarioCorrente = urlCalendario
            If _threadResultCalendariDav IsNot Nothing Then
                For Each cal As CalendarioDav In _threadResultCalendariDav
                    If String.Equals(cal.Url, urlCalendario, StringComparison.OrdinalIgnoreCase) Then
                        _syncNomeCalendarioCorrente = cal.Nome
                        Exit For
                    End If
                Next
            End If
            menuListaSync.abbandona()
            menuOpzioniCalendarioSincronizzazione()
        Catch ex As Exception
            _logger.ScriviLog("Errore CalendarioSincronizzazioneSceltoHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function OttieniConfigurazioneCalendarioCorrente() As CLSAgenda.ImpostazioneCalendarioSincronizzazione
        For Each c In classeagenda.OttieniCalendariConfigurati(_syncAccountCorrente)
            If String.Equals(c.UrlCalendario, _syncCalendarioCorrente, StringComparison.OrdinalIgnoreCase) Then Return c
        Next
        Return Nothing
    End Function

    Private Sub menuOpzioniCalendarioSincronizzazione()
        Dim config As CLSAgenda.ImpostazioneCalendarioSincronizzazione = OttieniConfigurazioneCalendarioCorrente()
        Dim incluso As Boolean = config IsNot Nothing AndAlso config.Selezionato

        Dim mnu As New WGMenu()
        mnu.aggiungi(If(incluso, "Escludi dalla sincronizzazione", "Includi nella sincronizzazione"), AddressOf toggleCalendarioSincronizzazioneSelezionato)
        mnu.aggiungi("Categoria per questo calendario", AddressOf impostaCategoriaCalendarioSincronizzazione)
        If config IsNot Nothing Then
            mnu.aggiungi("Rimuovi configurazione di questo calendario", AddressOf rimuoviConfigurazioneCalendarioCorrente)
        End If
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
        mnu.avvia(_syncNomeCalendarioCorrente, True)
    End Sub

    Private Sub toggleCalendarioSincronizzazioneSelezionato()
        Try
            Dim config As CLSAgenda.ImpostazioneCalendarioSincronizzazione = OttieniConfigurazioneCalendarioCorrente()
            Dim categoriaAttuale As String = If(config IsNot Nothing, config.Categoria, "")
            Dim nuovoStato As Boolean = Not (config IsNot Nothing AndAlso config.Selezionato)

            classeagenda.ImpostaCalendarioSincronizzazione(_syncAccountCorrente, _syncCalendarioCorrente, _syncNomeCalendarioCorrente, nuovoStato, categoriaAttuale)

            Dim msg As New WGMessaggi()
            msg.Messaggio(If(nuovoStato,
                "Calendario '" & _syncNomeCalendarioCorrente & "' incluso nella sincronizzazione.",
                "Calendario '" & _syncNomeCalendarioCorrente & "' escluso dalla sincronizzazione."))
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore toggleCalendarioSincronizzazioneSelezionato - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub rimuoviConfigurazioneCalendarioCorrente()
        Try
            classeagenda.RimuoviConfigurazioneCalendario(_syncAccountCorrente, _syncCalendarioCorrente)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Configurazione rimossa per il calendario '" & _syncNomeCalendarioCorrente & "'.")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore rimuoviConfigurazioneCalendarioCorrente - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Categoria specifica di questo calendario: ha priorita' sulla categoria di ripiego
    ' dell'account (RisolviCategoriaSincronizzazione la controlla per prima). Scegliendola si
    ' include automaticamente il calendario nella sincronizzazione, se non lo era gia'.
    Private Sub impostaCategoriaCalendarioSincronizzazione()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            Dim config As CLSAgenda.ImpostazioneCalendarioSincronizzazione = OttieniConfigurazioneCalendarioCorrente()
            Dim attuale As String = If(config IsNot Nothing, config.Categoria, "")

            menuListaCat = New WGMenuLista()
            menuListaCat.impostaTitolo("Categoria per '" & _syncNomeCalendarioCorrente & "'")
            menuListaCat.multiselezioneConsentita = False

            Dim elementi As New List(Of ElementoLista)
            Dim labelNessuna As String = "Nessuna"
            If String.IsNullOrWhiteSpace(attuale) Then labelNessuna &= " (Attuale)"
            elementi.Add(New ElementoLista(labelNessuna, ""))
            If cats IsNot Nothing Then
                For Each c As String In cats
                    Dim label As String = c
                    If Not String.IsNullOrWhiteSpace(attuale) AndAlso String.Equals(c, attuale, StringComparison.OrdinalIgnoreCase) Then label &= " (Attuale)"
                    elementi.Add(New ElementoLista(label, c))
                Next
            End If
            menuListaCat.CaricaListaDinamica(elementi)

            AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) FineImpostaCategoriaCalendarioSyncHandler(v)
            AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
            menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioniAppuntamenti, "Sincronizzazione Calendari"))
            menuListaCat.avvia()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore impostaCategoriaCalendarioSincronizzazione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineImpostaCategoriaCalendarioSyncHandler(v As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim cat As String = listaid(0)
        Try
            ' Scegliere una categoria per il calendario implica volerlo includere nella
            ' sincronizzazione: se non era ancora selezionato lo selezioniamo ora, cosi' l'utente
            ' non deve fare un secondo passaggio per "Includi nella sincronizzazione".
            classeagenda.ImpostaCalendarioSincronizzazione(_syncAccountCorrente, _syncCalendarioCorrente, _syncNomeCalendarioCorrente, True, cat)

            Dim msg As New WGMessaggi()
            If String.IsNullOrWhiteSpace(cat) Then
                msg.Messaggio("Nessuna categoria specifica impostata per '" & _syncNomeCalendarioCorrente & "': verra' usata la categoria dell'account (se impostata), altrimenti nessuna categoria.")
            Else
                msg.Messaggio("Categoria '" & cat & "' impostata per gli appuntamenti del calendario '" & _syncNomeCalendarioCorrente & "'.")
            End If
            msg.ShowDialog()
            menuListaCat.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Errore FineImpostaCategoriaCalendarioSyncHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuAvvisiAutomazioni()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Notifiche", AddressOf gestisciOpzioneNotifiche)
        mnu.aggiungi("Preavviso standard", AddressOf impostaPreavvisoDefault)
        mnu.aggiungi("Impostazione come eseguito", AddressOf menuAutoCompletamento)
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.AvvisiAutomazioni, "Avvisi e Automazioni"))
        mnu.avvia("Avvisi e Automazioni", True)
    End Sub

    Private Sub menuRicercaVisualizzazione()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Ordinamento", AddressOf menuOrdinamento)
        mnu.aggiungi("Orizzonte ricerca date libere", AddressOf impostaOrizzonteRicerca)
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.RicercaVisualizzazione, "Ricerca e Visualizzazione"))
        mnu.avvia("Ricerca e Visualizzazione", True)
    End Sub

    Private Sub menuOrdinamento()
        Try
            Dim menuOrd As New WGMenu()
            menuOrd.aggiungi("Per Data (Crescente)", AddressOf OrdDataCresc)
            menuOrd.aggiungi("Per Data (Decrescente)", AddressOf OrdDataDecresc)
            menuOrd.aggiungi("Per Nome (Crescente)", AddressOf OrdNomeCresc)
            menuOrd.aggiungi("Per Nome (Decrescente)", AddressOf OrdNomeDecresc)
            menuOrd.aggiungi("Per Oggetto (Crescente)", AddressOf OrdOggCresc)
            menuOrd.aggiungi("Per Oggetto (Decrescente)", AddressOf OrdOggDecresc)
            menuOrd.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Ordinamento, "Ordinamento"))
            menuOrd.avvia("Imposta Ordinamento", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore menuOrdinamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OrdDataCresc()
        impostaOrdinamento("DataCrescente")
    End Sub

    Private Sub OrdDataDecresc()
        impostaOrdinamento("DataDecrescente")
    End Sub

    Private Sub OrdNomeCresc()
        impostaOrdinamento("NomeCrescente")
    End Sub

    Private Sub OrdNomeDecresc()
        impostaOrdinamento("NomeDecrescente")
    End Sub

    Private Sub OrdOggCresc()
        impostaOrdinamento("OggettoCrescente")
    End Sub

    Private Sub OrdOggDecresc()
        impostaOrdinamento("OggettoDecrescente")
    End Sub

    Private Sub impostaOrdinamento(valore As String)
        Try
            config.SalvaValore("Ordinamento", valore)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Ordinamento salvato correttamente.")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore impostaOrdinamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub impostaOrizzonteRicerca()
        Try
            Dim mnu As New WGMenu()
            mnu.aggiungi("3 mesi (90 giorni)", Sub() SalvaOrizzonte("90"))
            mnu.aggiungi("6 mesi (180 giorni)", Sub() SalvaOrizzonte("180"))
            mnu.aggiungi("1 anno (365 giorni)", Sub() SalvaOrizzonte("365"))
            mnu.aggiungi("2 anni (730 giorni)", Sub() SalvaOrizzonte("730"))
            mnu.aggiungi("3 anni (1095 giorni)", Sub() SalvaOrizzonte("1095"))
            mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.OrizzonteRicerca, "Orizzonte Ricerca"))
            mnu.avvia("Orizzonte ricerca date libere", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore impostaOrizzonteRicerca - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SalvaOrizzonte(val As String)
        config.SalvaValore("GiorniRicercaDate", val)
        Dim msg As New WGMessaggi()
        msg.Messaggio("Orizzonte di ricerca salvato correttamente.")
        msg.ShowDialog()
    End Sub

    Private Sub menuAutoCompletamento()
        Try
            Dim mnu As New WGMenu()
            Dim stato As String = If(config.LeggiValore("AutoCompletamentoAttivo") = "1", "Disattiva", "Attiva")
            mnu.aggiungi(stato & " impostazione come eseguito", AddressOf ToggleAutoCompletamento)
            mnu.aggiungi("Imposta tempo per eseguito", AddressOf ImpostaTempoAutoCompletamento)
            mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioneEseguito, "Impostazione come eseguito"))
            mnu.avvia("Impostazione come eseguito", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore menuAutoCompletamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ToggleAutoCompletamento()
        Dim attuale As String = config.LeggiValore("AutoCompletamentoAttivo")
        Dim nuovoStato As String = If(attuale = "1", "0", "1")
        config.SalvaValore("AutoCompletamentoAttivo", nuovoStato)
        Dim msg As New WGMessaggi()
        msg.Messaggio(If(nuovoStato = "1", "Impostazione come eseguito attivata.", "Impostazione come eseguito disattivata."))
        msg.ShowDialog()
    End Sub

    Private Sub ImpostaTempoAutoCompletamento()
        Dim mnu As New WGMenu()
        mnu.aggiungi("Subito dopo la scadenza", Sub() SalvaTempoAuto("0"))
        mnu.aggiungi("Dopo 1 giorno", Sub() SalvaTempoAuto("1440"))
        mnu.aggiungi("Dopo 3 giorni", Sub() SalvaTempoAuto("4320"))
        mnu.aggiungi("Dopo 1 settimana", Sub() SalvaTempoAuto("10080"))
        mnu.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ImpostazioneEseguitoTempo, "Tempo Esecuzione"))
        mnu.avvia("Tempo impostazione come eseguito", True)
    End Sub

    Private Sub SalvaTempoAuto(minuti As String)
        config.SalvaValore("AutoCompletamentoMinuti", minuti)
        Dim msg As New WGMessaggi()
        msg.Messaggio("Tempo per l'impostazione come eseguito salvato.")
        msg.ShowDialog()
    End Sub

    Private Sub impostaGiorniLavorativi()
        _tempMenuGiorni = New WGMenuLista()

        _tempMenuGiorni.impostaTitolo("Imposta Giorni Lavorativi")
        _tempMenuGiorni.multiselezioneConsentita = True

        Dim correnti As List(Of DayOfWeek) = GetGiorniLavorativiConfig()

        _tempMenuGiorni.aggiungiElemento("Domenica", "0", correnti.Contains(DayOfWeek.Sunday))
        _tempMenuGiorni.aggiungiElemento("Lunedì", "1", correnti.Contains(DayOfWeek.Monday))
        _tempMenuGiorni.aggiungiElemento("Martedì", "2", correnti.Contains(DayOfWeek.Tuesday))
        _tempMenuGiorni.aggiungiElemento("Mercoledì", "3", correnti.Contains(DayOfWeek.Wednesday))
        _tempMenuGiorni.aggiungiElemento("Giovedì", "4", correnti.Contains(DayOfWeek.Thursday))
        _tempMenuGiorni.aggiungiElemento("Venerdì", "5", correnti.Contains(DayOfWeek.Friday))
        _tempMenuGiorni.aggiungiElemento("Sabato", "6", correnti.Contains(DayOfWeek.Saturday))

        _tempMenuGiorni.aggiungiOpzione("Salva selezione", AddressOf SalvaGiorni)
        _tempMenuGiorni.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.GiorniLavorativi, "Giorni Lavorativi"))
        AddHandler _tempMenuGiorni.escPremuto, Sub() _tempMenuGiorni.abbandona()
        _tempMenuGiorni.avvia()
    End Sub

    Private Sub SalvaGiorni()
        Dim scelti As List(Of String) = _tempMenuGiorni.ottieniElementi()
        config.SalvaValore("GiorniLavorativi", String.Join(",", scelti))

        Dim m As New WGMessaggi()
        m.Messaggio("Giorni lavorativi salvati correttamente.")
        m.ShowDialog()
        _tempMenuGiorni.abbandona()
    End Sub

    Private Sub impostaDurate()
        _menuDurate = New WGMenu()
        _menuDurate.aggiungi("5 minuti", Sub() SalvaDurata("5"))
        _menuDurate.aggiungi("10 minuti", Sub() SalvaDurata("10"))
        _menuDurate.aggiungi("15 minuti", Sub() SalvaDurata("15"))
        _menuDurate.aggiungi("20 minuti", Sub() SalvaDurata("20"))
        _menuDurate.aggiungi("30 minuti", Sub() SalvaDurata("30"))
        _menuDurate.aggiungi("45 minuti", Sub() SalvaDurata("45"))
        _menuDurate.aggiungi("60 minuti", Sub() SalvaDurata("60"))
        _menuDurate.aggiungi("75 minuti", Sub() SalvaDurata("75"))
        _menuDurate.aggiungi("90 minuti", Sub() SalvaDurata("90"))
        _menuDurate.aggiungi("120 minuti", Sub() SalvaDurata("120"))
        _menuDurate.aggiungi("Altra durata", AddressOf impostaDurataManuale)
        _menuDurate.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.DurataAppuntamenti, "Durata Appuntamenti"))
        _menuDurate.avvia("Imposta Durata Appuntamenti")
    End Sub

    Private Sub SalvaDurata(valore As String)
        config.SalvaValore("DurataMinima", valore)
        config.SalvaValore("DurataMassima", valore)
        Dim m As New WGMessaggi()
        m.Messaggio("Durata appuntamenti impostata a " & valore & " minuti.")
        m.ShowDialog()
        If _menuDurate IsNot Nothing Then
            _menuDurate.chiudi()
        End If
    End Sub

    Private Sub impostaDurataManuale()
        campi = New wgcontrols.Campi()
        Dim dAttuale As String = config.LeggiValore("DurataMinima")
        If String.IsNullOrEmpty(dAttuale) Then dAttuale = "30"
        campi.AggiungiNumerico("Durata in minuti", "c.durata", True, , dAttuale)
        AddHandler campi.Fine, AddressOf fineImpostaDurataManuale
        campi.Avvia()
    End Sub

    Private Sub fineImpostaDurataManuale()
        If campi.TuttiObbligatoriRiempiti() Then
            Dim val As String = campi.RecuperaValore("c.durata")
            Dim valInt As Integer
            If Not Integer.TryParse(val, valInt) OrElse valInt <= 0 Then

                campi = New wgcontrols.Campi()
                campi.AggiungiNumerico("Durata in minuti", "c.durata", True)
                Dim msg As New WGMessaggi()
                msg.Messaggio("La durata deve essere un numero intero maggiore di zero.")
                msg.ShowDialog()
                AddHandler campi.Fine, AddressOf fineImpostaDurataManuale
                campi.Avvia()
                Exit Sub
            End If
            config.SalvaValore("DurataMinima", val)
            config.SalvaValore("DurataMassima", val)
            Dim m As New WGMessaggi()
            m.Messaggio("Durata appuntamenti impostata a " & val & " minuti.")
            m.ShowDialog()

            campi = Nothing
        Else
            campi = Nothing
        End If
    End Sub

    Private Sub gestisciOrariEsclusi()
        _menuOrariEsclusi = New WGMenu()
        _menuOrariEsclusi.aggiungi("Aggiungi orario da escludere", AddressOf AggiungiOrarioEscluso)
        _menuOrariEsclusi.aggiungi("Visualizza orari salvati", AddressOf VisualizzaOrariEsclusi)
        _menuOrariEsclusi.aggiungi("Svuota orari esclusi", AddressOf SvuotaOrariEsclusi)
        _menuOrariEsclusi.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.OrariEsclusi, "Orari Esclusi"))
        _menuOrariEsclusi.avvia("Pause e orari esclusi")
    End Sub

    Private Sub AggiungiOrarioEscluso()
        campi = New wgcontrols.Campi()
        campi.AggiungiOrario("Ora Inizio Blocco", "c.ini", True)
        campi.AggiungiOrario("Ora Fine Blocco", "c.fin", True)
        AddHandler campi.Fine, AddressOf FineAggiungiOrarioEscluso
        campi.Avvia()
    End Sub

    Private Sub FineAggiungiOrarioEscluso()
        Dim ini As String = campi.RecuperaValore("c.ini")
        Dim fin As String = campi.RecuperaValore("c.fin")
        campi = Nothing

        Dim tsIni As TimeSpan
        Dim tsFin As TimeSpan
        If String.IsNullOrWhiteSpace(ini) OrElse String.IsNullOrWhiteSpace(fin) OrElse
           Not TimeSpan.TryParse(ini, tsIni) OrElse Not TimeSpan.TryParse(fin, tsFin) Then
            Dim msg As New WGMessaggi()
            msg.Messaggio("Formato orario non valido. Inserire un orario nel formato HH:mm.")
            msg.ShowDialog()
            AggiungiOrarioEscluso()
            Exit Sub
        End If
        If tsIni >= tsFin Then
            Dim msg As New WGMessaggi()
            msg.Messaggio("L'ora di inizio blocco deve essere precedente all'ora di fine blocco.")
            msg.ShowDialog()
            AggiungiOrarioEscluso()
            Exit Sub
        End If

        Dim attuali As String = config.LeggiValore("OrariEsclusi")

        If Not String.IsNullOrEmpty(attuali) Then
            attuali &= "|"
        Else
            attuali = ""
        End If
        attuali &= $"{ini}-{fin}"
        config.SalvaValore("OrariEsclusi", attuali)
        Dim m As New WGMessaggi()
        m.Messaggio($"L'orario {ini}-{fin} è stato aggiunto alle esclusioni.")
        m.ShowDialog()
    End Sub

    Private Sub VisualizzaOrariEsclusi()
        Dim attuali As String = config.LeggiValore("OrariEsclusi")
        If String.IsNullOrEmpty(attuali) Then
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun orario per le pause o altro salvato.")
            msg.ShowDialog()
            Exit Sub
        End If

        _menuListaOrariEsclusi = New WGMenuLista()
        _menuListaOrariEsclusi.impostaTitolo("Orari esclusi salvati")
        _menuListaOrariEsclusi.multiselezioneConsentita = False

        Dim blocchi As String() = attuali.Split("|"c)
        For Each b In blocchi
            If Not String.IsNullOrWhiteSpace(b) Then
                _menuListaOrariEsclusi.aggiungiElemento(b, b)
            End If
        Next

        _menuListaOrariEsclusi.aggiungiOpzione("Elimina orario", AddressOf EliminaOrarioSelezionatoDaLista)
        _menuListaOrariEsclusi.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ListaOrariEsclusi, "Lista Orari Esclusi"))
        AddHandler _menuListaOrariEsclusi.escPremuto, Sub() _menuListaOrariEsclusi.abbandona()
        _menuListaOrariEsclusi.avvia()
    End Sub

    Private Sub EliminaOrarioSelezionatoDaLista()
        Dim elementiSelezionati As List(Of String) = _menuListaOrariEsclusi.ottieniElementi()
        If elementiSelezionati.Count > 0 Then
            _orarioDaEliminare = elementiSelezionati(0)

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf ConfermaEliminaOrario
            domanda.avvia($"Vuoi eliminare l'orario {_orarioDaEliminare}?")
        Else
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nessun orario selezionato.")
            msg.ShowDialog()
        End If
    End Sub

    Private Sub ConfermaEliminaOrario(risp As DialogResult)
        If risp = DialogResult.Yes Then
            Dim attuali As String = config.LeggiValore("OrariEsclusi")
            If Not String.IsNullOrEmpty(attuali) Then
                Dim blocchi As List(Of String) = attuali.Split("|"c).ToList()
                If blocchi.Contains(_orarioDaEliminare) Then
                    blocchi.Remove(_orarioDaEliminare)
                    config.SalvaValore("OrariEsclusi", String.Join("|", blocchi))
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Orario eliminato correttamente.")
                    msg.ShowDialog()
                    _menuListaOrariEsclusi.abbandona()
                    VisualizzaOrariEsclusi()
                End If
            End If
        End If
    End Sub

    Private Sub SvuotaOrariEsclusi()
        Dim domanda As New WGDomanda()
        AddHandler domanda.rispostadomanda, AddressOf ConfermaSvuotaOrari
        domanda.avvia("Sei sicuro di voler svuotare tutti gli orari esclusi?")
    End Sub

    Private Sub ConfermaSvuotaOrari(risp As DialogResult)
        If risp = DialogResult.Yes Then
            config.SalvaValore("OrariEsclusi", "")
            Dim m As New WGMessaggi()
            m.Messaggio("La lista degli orari esclusi è stata svuotata.")
            m.ShowDialog()
        End If
    End Sub

    Private Function GetGiorniLavorativiConfig() As List(Of DayOfWeek)
        Dim str As String = config.LeggiValore("GiorniLavorativi")
        Dim lista As New List(Of DayOfWeek)
        If String.IsNullOrEmpty(str) Then
            lista.AddRange({DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday})
        Else
            Dim parts As String() = str.Split(","c)
            For Each p In parts
                Dim res As Integer
                If Integer.TryParse(p, res) AndAlso res >= 0 AndAlso res <= 6 Then
                    lista.Add(CType(res, DayOfWeek))
                End If
            Next
        End If
        Return lista
    End Function

    Private Function GetOrariEsclusiConfig() As List(Of Tuple(Of TimeSpan, TimeSpan))
        Dim lista As New List(Of Tuple(Of TimeSpan, TimeSpan))
        Dim str As String = config.LeggiValore("OrariEsclusi")

        If Not String.IsNullOrEmpty(str) Then
            Dim blocchi As String() = str.Split("|"c)
            For Each b In blocchi
                Dim orari As String() = b.Split("-"c)
                If orari.Length = 2 Then
                    Dim tsInizio, tsFine As TimeSpan
                    If TimeSpan.TryParse(orari(0), tsInizio) AndAlso TimeSpan.TryParse(orari(1), tsFine) Then
                        lista.Add(New Tuple(Of TimeSpan, TimeSpan)(tsInizio, tsFine))
                    End If
                End If
            Next
        End If
        Return lista
    End Function

    Private Sub ImpostaParametriRicerca(escludiLavoro As Boolean)
        Try
            Dim durMinStr As String = config.LeggiValore("DurataMinima")
            Dim durMaxStr As String = config.LeggiValore("DurataMassima")
            If Not Integer.TryParse(durMinStr, _search_durataMin) OrElse _search_durataMin <= 0 Then _search_durataMin = 30
            If Not Integer.TryParse(durMaxStr, _search_durataMax) OrElse _search_durataMax <= 0 Then _search_durataMax = 60

            Dim startW As String = config.LeggiValore("InizioLavoro")
            Dim endW As String = config.LeggiValore("FineLavoro")
            If String.IsNullOrEmpty(startW) Then startW = "09:00"
            If String.IsNullOrEmpty(endW) Then endW = "18:00"

            Dim dtStart As DateTime
            Dim dtEnd As DateTime
            If Not DateTime.TryParse(startW, dtStart) Then dtStart = DateTime.Parse("09:00")
            If Not DateTime.TryParse(endW, dtEnd) Then dtEnd = DateTime.Parse("18:00")
            _search_oraInizioLavoro = dtStart
            _search_oraFineLavoro = dtEnd

            If escludiLavoro Then
                _search_giorniConsentiti = New List(Of DayOfWeek) From {DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday}
            Else
                _search_giorniConsentiti = GetGiorniLavorativiConfig()
            End If

            _search_orariDaEscludere = GetOrariEsclusiConfig()
            _search_escludiOrariLavorativi = escludiLavoro

            Dim ggRicercaStr As String = config.LeggiValore("GiorniRicercaDate")
            If Not Integer.TryParse(ggRicercaStr, _search_giorniRicerca) OrElse _search_giorniRicerca <= 0 Then
                _search_giorniRicerca = 365
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpostaParametriRicerca - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub gestisciOpzioneNotifiche()
        Try
            menuNotificheObj = New WGMenu()
            menuNotificheObj.aggiungi("Sempre", AddressOf NotSempre)
            menuNotificheObj.aggiungi("Mattina e Pomeriggio", AddressOf NotFasce)
            menuNotificheObj.aggiungi("Mai", AddressOf NotMai)
            menuNotificheObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Notifiche, "Notifiche"))
            menuNotificheObj.avvia("Notifiche")
        Catch ex As Exception
            _logger.ScriviLog("Errore gestisciOpzioneNotifiche - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub NotSempre()
        SalvaStatoNotifiche("Sempre")
    End Sub

    Private Sub NotFasce()
        SalvaStatoNotifiche("Mattina e Pomeriggio")
    End Sub

    Private Sub NotMai()
        SalvaStatoNotifiche("Mai")
    End Sub

    Private Sub SalvaStatoNotifiche(stato As String)
        Try
            config.SalvaValore("NotificheAttive", stato)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impostazione notifiche salvata: " & stato)
            msg.ShowDialog()
            If menuNotificheObj IsNot Nothing Then menuNotificheObj.chiudi()
        Catch ex As Exception
            _logger.ScriviLog("Errore SalvaStatoNotifiche - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub impostaOrarioLavoro()
        Try
            campi = New wgcontrols.Campi()
            Dim startW As String = config.LeggiValore("InizioLavoro")
            Dim endW As String = config.LeggiValore("FineLavoro")
            If String.IsNullOrEmpty(startW) Then startW = "09:00"
            If String.IsNullOrEmpty(endW) Then endW = "18:00"
            campi.AggiungiOrario("Inizio turno lavorativo", "c.inizioL", True, , startW)
            campi.AggiungiOrario("Fine turno lavorativo", "c.fineL", True, , endW)
            AddHandler campi.Fine, AddressOf fineImpostaOrarioLavoro
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore impostaOrarioLavoro - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineImpostaOrarioLavoro()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim ini As String = campi.RecuperaValore("c.inizioL")
                Dim fin As String = campi.RecuperaValore("c.fineL")

                Dim tsIni As TimeSpan
                Dim tsFin As TimeSpan
                Dim iniOk As Boolean = TimeSpan.TryParse(ini, tsIni)
                Dim finOk As Boolean = TimeSpan.TryParse(fin, tsFin)

                If Not iniOk OrElse Not finOk Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    If Not iniOk Then campi.AggiungiOrario("Inizio turno lavorativo", "c.inizioL", True)
                    If Not finOk Then campi.AggiungiOrario("Fine turno lavorativo", "c.fineL", True)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Formato orario non valido. Inserire un orario nel formato HH:mm.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf fineImpostaOrarioLavoro
                    campi.Avvia()
                    Exit Sub
                End If

                If tsIni >= tsFin Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    campi.AggiungiOrario("Inizio turno lavorativo", "c.inizioL", True, , ini)
                    campi.AggiungiOrario("Fine turno lavorativo", "c.fineL", True, , fin)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("L'orario di inizio turno deve essere precedente all'orario di fine turno.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf fineImpostaOrarioLavoro
                    campi.Avvia()
                    Exit Sub
                End If

                config.SalvaValore("InizioLavoro", ini)
                config.SalvaValore("FineLavoro", fin)
                Dim m As New WGMessaggi()
                m.Messaggio("Orario lavorativo aggiornato correttamente.")
                m.ShowDialog()
                campi = Nothing
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.fineImpostaOrarioLavoro - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub impostaPreavvisoDefault()
        Try
            Dim mPreD As New WGMenu()
            mPreD.aggiungi("1 ora prima", AddressOf PreDefOra)
            mPreD.aggiungi("4 ore prima", AddressOf PreDefQua)
            mPreD.aggiungi("1 giorno prima", AddressOf PreDefGio)
            mPreD.aggiungi("2 giorni prima", AddressOf PreDefDue)
            mPreD.aggiungi("3 giorni prima", AddressOf PreDefTre)
            mPreD.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Preavviso, "Preavviso Standard"))
            mPreD.avvia("Preavviso standard")
        Catch ex As Exception
            _logger.ScriviLog("Errore impostaPreavvisoDefault - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PreDefOra()
        SalvaPreDef("60")
    End Sub

    Private Sub PreDefQua()
        SalvaPreDef("240")
    End Sub

    Private Sub PreDefGio()
        SalvaPreDef("1440")
    End Sub

    Private Sub PreDefDue()
        SalvaPreDef("2880")
    End Sub

    Private Sub PreDefTre()
        SalvaPreDef("4320")
    End Sub

    Private Sub SalvaPreDef(val As String)
        Try
            config.SalvaValore("PreavvisoDefault", val)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Valore di preavviso standard aggiornato.")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Errore SalvaPreDef - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub impostaCategoriaDefault()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona Categoria Predefinita")
                menuListaCat.multiselezioneConsentita = False

                Dim currentDefault As String = config.LeggiValore("CategoriaDefault")

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    Dim label As String = c
                    If c = currentDefault Then label &= " (Attuale)"
                    elementi.Add(New ElementoLista(label, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) FineImpostaCatDefaultHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneCategoriaPredefinita, "Categoria Predefinita"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna categoria disponibile per la selezione.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore impostaCategoriaDefault - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineImpostaCatDefaultHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        fineImpostaCategoriaDefault(elemento, menuListaCat)
    End Sub

    Private Sub fineImpostaCategoriaDefault(cat As String, lista As WGMenuLista)
        Try
            config.SalvaValore("CategoriaDefault", cat)

            Dim msg As New WGMessaggi()
            msg.Messaggio($"Categoria '{cat}' impostata come predefinita.")
            msg.ShowDialog()
            lista.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Errore fineImpostaCategoriaDefault - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuCerca()
        UsaAgendaEPoi(AddressOf menuCercaInterna)
    End Sub

    Private Sub menuCercaInterna()
        Try
            menuRicercaObj = New WGMenu()
            menuRicercaObj.aggiungi("Ricerca per data o parola", AddressOf ricercaUnificata)
            menuRicercaObj.aggiungi("Ricerca per intervallo date", AddressOf ricercadata)
            menuRicercaObj.aggiungi("Ricerca per testo", AddressOf ricercaparola)
            menuRicercaObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Ricerca, "Cerca"))
            menuRicercaObj.avvia("Cerca")
        Catch ex As Exception
            _logger.ScriviLog("Errore menuCerca - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuConsulta()
        UsaAgendaEPoi(AddressOf menuConsultaInterna)
    End Sub

    Private Sub menuConsultaInterna()
        Try
            menuConsultaObj = New WGMenu()
            menuConsultaObj.aggiungi("Agenda completa", AddressOf consulta)
            menuConsultaObj.aggiungi("Appuntamenti futuri", AddressOf consultaFuturi)
            menuConsultaObj.aggiungi("Consultazione preavvisi e scadenze", AddressOf consultascadenza)
            menuConsultaObj.aggiungi("Ricorrenze", AddressOf VisualizzaRicorrenze)
            menuConsultaObj.aggiungi("Filtra per categoria", AddressOf visualizzapercategoria)
            menuConsultaObj.aggiungi("Programma giornaliero", AddressOf consultaprogramma)
            menuConsultaObj.aggiungi("Programma mensile", AddressOf consultamese)
            menuConsultaObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Consulta, "Consulta"))
            menuConsultaObj.avvia("Consulta")
        Catch ex As Exception
            _logger.ScriviLog("Errore menuConsulta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub menuCategorie()
        Try
            menuCategorieObj = New WGMenu()
            menuCategorieObj.aggiungi("Crea nuova categoria", AddressOf aggiungicategoria)
            menuCategorieObj.aggiungi("Rinomina categoria", AddressOf modificacategoria)
            menuCategorieObj.aggiungi("Elimina categoria", AddressOf eliminacategoria)
            menuCategorieObj.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Categorie, "Categorie"))
            menuCategorieObj.avvia("Categorie")
        Catch ex As Exception
            _logger.ScriviLog("Errore menuCategorie - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub GestisciAnnullamento(sender As Object, e As EventArgs) Handles attesa.cancella
        Try
            If _cts IsNot Nothing Then
                _cts.Cancel()
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private Sub AvviaCaricamento()
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
            End If

            _cts = New CancellationTokenSource()
            attesa = New WGProcessoInCorso()
            AddHandler attesa.cancella, AddressOf cancellathreadcaricamento
            attesa.Avvia("Caricamento in corso...", True)

            Carica = New Thread(AddressOf ThreadCaricaDatiWrapper)
            Carica.Start()
        Catch ex As Exception
            _logger.ScriviLog("Errore in AvviaCaricamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub cancellathreadcaricamento()
        Try
            If _cts IsNot Nothing Then
                _cts.Cancel()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.cancellathreadcaricamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ThreadCaricaDati(token As CancellationToken)
        Try
            Dim ordStr As String = config.LeggiValore("Ordinamento")
            Dim ordinamentoAttuale As CLSAgenda.TipoOrdinamento = CLSAgenda.TipoOrdinamento.DataCrescente
            If Not String.IsNullOrEmpty(ordStr) Then
                If [Enum].IsDefined(GetType(CLSAgenda.TipoOrdinamento), ordStr) Then
                    ordinamentoAttuale = CType([Enum].Parse(GetType(CLSAgenda.TipoOrdinamento), ordStr), CLSAgenda.TipoOrdinamento)
                End If
            End If

            Select Case _modalitaCorrente
                Case ModalitaVisualizzazione.Tutti
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiPaginati(0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Futuri
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiFuturiPaginati(0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Giornaliero
                    _tempElencoAppuntamenti = classeagenda.OttieniProgrammaGiornalieroPaginato(_filtroData, 0, 10000, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Mese
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiPerMesePaginati(0, 10000, _filtroMese, _filtroAnno, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.RicercaMista
                    _tempElencoAppuntamenti = classeagenda.CercaAppuntamentiDataOParolaPaginati(_filtroTesto, 0, 10000, _filtroData, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.RicercaTesto
                    _tempElencoAppuntamenti = classeagenda.CercaAppuntamentiPaginati(_filtroTesto, 0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.RicercaPeriodo
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiPerPeriodoPaginati(_filtroDataInizio, _filtroDataFine, 0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Categoria
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiPerCategoriaPaginati(_filtroCategoria, 0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.InScadenza
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiInScadenzaPaginati(0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Scaduti
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiScadutiPaginati(0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.InScadenzaPerCategoria
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiInScadenzaPerCategoriaPaginati(_filtroCategoria, 0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.ScadutiPerCategoria
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiScadutiPerCategoriaPaginati(_filtroCategoria, 0, 10000, token, ordinamentoAttuale)

                Case ModalitaVisualizzazione.Ricorrenze
                    _tempElencoAppuntamenti = classeagenda.OttieniAppuntamentiRicorrentiPaginati(0, 10000, token, ordinamentoAttuale)
            End Select

            token.ThrowIfCancellationRequested()

            _tempListaDinamica = New List(Of ElementoLista)()
            If _tempElencoAppuntamenti IsNot Nothing Then
                For Each appt In _tempElencoAppuntamenti
                    token.ThrowIfCancellationRequested()
                    Dim titolo As String = $"{appt.Oggetto} ({FormattaDataAgenda(appt.Data)})"
                    _tempListaDinamica.Add(New ElementoLista(titolo, appt.Id.ToString()))
                Next
            End If

            If _uiContext IsNot Nothing Then
                _uiContext.Post(AddressOf UI_MostraListaWrapper, Nothing)
            Else
                UI_MostraLista()
            End If

        Catch ex As OperationCanceledException
            If _uiContext IsNot Nothing Then
                _uiContext.Post(AddressOf MsgAnnullataWrapper, Nothing)
            Else
                If attesa IsNot Nothing Then attesa.Fine()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in ThreadCaricaDati - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If _uiContext IsNot Nothing Then
                _uiContext.Post(AddressOf FineAttesaWrapper, Nothing)
            Else
                If attesa IsNot Nothing Then attesa.Fine()
            End If
        End Try
    End Sub

    Private Sub UI_MostraLista()
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If

            If _tempListaDinamica IsNot Nothing AndAlso _tempListaDinamica.Count > 0 Then
                menuListaAppuntamenti = New WGMenuLista()
                menuListaAppuntamenti.ciclono = True
                menuListaAppuntamenti.multiselezioneConsentita = True

                AddHandler menuListaAppuntamenti.VoceCambiata, AddressOf GestioneOpzioniDinamiche
                AddHandler menuListaAppuntamenti.ElementoSelezionato, Sub(v As String) visualizzaappuntamento(v)
                AddHandler menuListaAppuntamenti.escPremuto, AddressOf ChiudiListaAppuntamenti

                menuListaAppuntamenti.aggiungiOpzione("Modifica dettagli attività", AddressOf ModificaAppDaLista)
                menuListaAppuntamenti.aggiungiOpzione("Elimina attività", AddressOf EliminaAppDaLista)
                menuListaAppuntamenti.aggiungiOpzione("Segna come completata", AddressOf EseguiAppDaLista)
                menuListaAppuntamenti.aggiungiOpzione("Duplica attività", AddressOf DuplicaAppDaLista)
                menuListaAppuntamenti.aggiungiOpzione("Sposta in altra categoria", AddressOf SpostaAppDaLista)

                If IsModalitaScadenza() Then
                    menuListaAppuntamenti.aggiungiOpzione("Segna TUTTI come completati", AddressOf SegnaTuttiCompletatiDaLista)
                End If

                Dim titoloBase As String = ""

                Select Case _modalitaCorrente
                    Case ModalitaVisualizzazione.Tutti : titoloBase = "Consultazione Globale"
                    Case ModalitaVisualizzazione.Futuri : titoloBase = "Attività Future"
                    Case ModalitaVisualizzazione.Giornaliero : titoloBase = "Agenda del " & _filtroData
                    Case ModalitaVisualizzazione.Mese
                        Dim mName As String = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(If(_filtroMese > 0, _filtroMese, DateTime.Now.Month))
                        titoloBase = $"Agenda Mensile ({mName})"
                    Case ModalitaVisualizzazione.RicercaMista : titoloBase = "Ricerca Combinata"
                    Case ModalitaVisualizzazione.RicercaTesto : titoloBase = "Risultati Ricerca: " & _filtroTesto
                    Case ModalitaVisualizzazione.RicercaPeriodo : titoloBase = "Periodo " & _filtroDataInizio.ToShortDateString() & " - " & _filtroDataFine.ToShortDateString()
                    Case ModalitaVisualizzazione.Categoria : titoloBase = "Categoria: " & _filtroCategoria
                    Case ModalitaVisualizzazione.InScadenza : titoloBase = "In Scadenza (Tutti)"
                    Case ModalitaVisualizzazione.Scaduti : titoloBase = "Scaduti (Tutti)"
                    Case ModalitaVisualizzazione.InScadenzaPerCategoria : titoloBase = $"In Scadenza ({_filtroCategoria})"
                    Case ModalitaVisualizzazione.ScadutiPerCategoria : titoloBase = $"Scaduti ({_filtroCategoria})"
                    Case ModalitaVisualizzazione.Ricorrenze : titoloBase = "Ricorrenze"
                End Select
                menuListaAppuntamenti.impostaTitolo(titoloBase)
                menuListaAppuntamenti.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ListaAppuntamenti, "Lista Appuntamenti"))
                menuListaAppuntamenti.CaricaListaDinamica(_tempListaDinamica)
                menuListaAppuntamenti.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun appuntamento trovato.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore in UI_MostraLista - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaAppuntamenti()
        Try
            menuListaAppuntamenti.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Errore chiusura lista - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModificaAppDaLista()
        modificaappuntamento(menuListaAppuntamenti)
    End Sub

    Private Sub EliminaAppDaLista()
        eliminaappuntamento(menuListaAppuntamenti)
    End Sub

    Private Sub EseguiAppDaLista()
        impostaeseguito(menuListaAppuntamenti)
    End Sub

    Private Sub DuplicaAppDaLista()
        duplicaappuntamento(menuListaAppuntamenti)
    End Sub

    Private Sub SpostaAppDaLista()
        spostaappuntamentocategoria(menuListaAppuntamenti)
    End Sub

    Private Sub SegnaTuttiCompletatiDaLista()
        menuListaAppuntamenti.abbandona()
        ImpostaTuttiEseguitiMassivo()
    End Sub

    Private Sub EseguiAppDaOpzioneDinamica()
        impostaeseguito(menuListaAppuntamenti)
    End Sub

    Private Sub GestioneOpzioniDinamiche(sender As Object, e As VoceCambiataEventArgs)
        Try
            Dim lista As WGMenuLista = DirectCast(sender, WGMenuLista)
            Dim id As String = e.IdVoce
            If String.IsNullOrEmpty(id) Then Exit Sub

            Dim idVal As Integer
            If Not Integer.TryParse(id, idVal) Then Exit Sub

            Dim stato As String = classeagenda.GetAppuntamentoStato(idVal)

            lista.rimuoviOpzione("Segna come completata")

            If stato <> "passato" Then
                lista.aggiungiOpzioneDopo("Elimina attività", "Segna come completata", AddressOf EseguiAppDaOpzioneDinamica)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.GestioneOpzioniDinamiche - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub consulta()
        Try
            _modalitaCorrente = ModalitaVisualizzazione.Tutti
            AvviaCaricamento()
        Catch ex As Exception
            _logger.ScriviLog("Errore consulta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub consultaFuturi()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Verifica appuntamenti...", False)

            Dim tFuturi As New Thread(AddressOf ContaAppuntamentiFuturiThread)
            tFuturi.Start()
            AttendiThread(tFuturi)
            Dim count As Integer = _threadResultInt

            If count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sono presenti appuntamenti futuri in agenda.")
                msg.ShowDialog()
                Exit Sub
            End If

            _modalitaCorrente = ModalitaVisualizzazione.Futuri
            AvviaCaricamento()
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore consultaFuturi - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub consultaprogramma()
        visualizzaprogramma(Date.Today.ToString("dd/MM/yyyy"))
    End Sub

    Private Sub consultamese()
        Try
            campi = New wgcontrols.Campi()
            campi.AggiungiNumerico("Mese (1-12)", "c.mese", True)
            campi.AggiungiNumerico("Anno", "c.anno", True)
            AddHandler campi.Fine, AddressOf fineconsultamese
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore consultamese - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineconsultamese()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim mStr As String = campi.RecuperaValore("c.mese")
                Dim yStr As String = campi.RecuperaValore("c.anno")

                Dim mVal As Integer
                Dim yVal As Integer
                Dim mOk As Boolean = Integer.TryParse(mStr, mVal) AndAlso mVal >= 1 AndAlso mVal <= 12
                Dim yOk As Boolean = Integer.TryParse(yStr, yVal) AndAlso yVal >= 1 AndAlso yVal <= 9999

                If Not mOk OrElse Not yOk Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    If Not mOk Then
                        campi.AggiungiNumerico("Mese (1-12)", "c.mese", True)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Il mese deve essere un numero compreso tra 1 e 12.")
                        msg.ShowDialog()
                    End If
                    If Not yOk Then
                        campi.AggiungiNumerico("Anno", "c.anno", True)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("L'anno inserito non è valido.")
                        msg.ShowDialog()
                    End If
                    AddHandler campi.Fine, AddressOf fineconsultamese
                    campi.Avvia()
                    Exit Sub
                End If

                campi = Nothing
                _filtroMese = mVal
                _filtroAnno = yVal
                _modalitaCorrente = ModalitaVisualizzazione.Mese
                AvviaCaricamento()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.fineconsultamese - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub visualizzaprogramma(dataStr As String)
        Try
            _modalitaCorrente = ModalitaVisualizzazione.Giornaliero
            _filtroData = dataStr
            AvviaCaricamento()
        Catch ex As Exception
            _logger.ScriviLog("Errore visualizzaprogramma - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ricercaUnificata()
        Try
            campi = New wgcontrols.Campi()
            campi.Aggiungi("Parola chiave", "c.parola", True)
            campi.AggiungiData("Data", "c.data", True)
            AddHandler campi.Fine, AddressOf finericercaUnificata
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore ricercaUnificata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finericercaUnificata()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim parola As String = campi.RecuperaValore("c.parola")
                Dim dataR As String = campi.RecuperaValore("c.data")
                campi = Nothing

                _filtroTesto = parola
                _filtroData = dataR
                _modalitaCorrente = ModalitaVisualizzazione.RicercaMista
                AvviaCaricamento()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore finericercaUnificata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ricercadata()
        Try
            campi = New wgcontrols.Campi()
            campi.AggiungiData("Data Inizio Ricerca", "c.inizio", True)
            campi.AggiungiData("Data Fine Ricerca", "c.fine", True)
            AddHandler campi.Fine, AddressOf finericercadata
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore ricercadata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finericercadata()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim iniStr As String = campi.RecuperaValore("c.inizio")
                Dim finStr As String = campi.RecuperaValore("c.fine")
                Dim dtIni As DateTime
                Dim dtFin As DateTime
                Dim iniOk As Boolean = DateTime.TryParse(iniStr, dtIni)
                Dim finOk As Boolean = DateTime.TryParse(finStr, dtFin)

                If Not iniOk OrElse Not finOk Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    If Not iniOk Then campi.AggiungiData("Data Inizio Ricerca", "c.inizio", True)
                    If Not finOk Then campi.AggiungiData("Data Fine Ricerca", "c.fine", True)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("La data inserita non è valida.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf finericercadata
                    campi.Avvia()
                    Exit Sub
                End If

                If dtIni > dtFin Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    campi.AggiungiData("Data Inizio Ricerca", "c.inizio", True)
                    campi.AggiungiData("Data Fine Ricerca", "c.fine", True)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("La data di inizio non può essere successiva alla data di fine.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf finericercadata
                    campi.Avvia()
                    Exit Sub
                End If

                _filtroDataInizio = dtIni
                _filtroDataFine = dtFin
                campi = Nothing
                _modalitaCorrente = ModalitaVisualizzazione.RicercaPeriodo
                AvviaCaricamento()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.finericercadata - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ricercaparola()
        Try
            campi = New wgcontrols.Campi()
            campi.Aggiungi("Testo da cercare", "c.parola", True)
            AddHandler campi.Fine, AddressOf finericercaparola
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore ricercaparola - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finericercaparola()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim parola As String = campi.RecuperaValore("c.parola")
                campi = Nothing

                _modalitaCorrente = ModalitaVisualizzazione.RicercaTesto
                _filtroTesto = parola
                AvviaCaricamento()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore finericercaparola - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub aggiungicategoria()
        Try
            campi = New wgcontrols.Campi()
            campi.Aggiungi("Nome nuova categoria", "c.nome", True)
            AddHandler campi.Fine, AddressOf fineaggiungicategoria
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Errore aggiungicategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineaggiungicategoria()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim nomeCat As String = campi.RecuperaValore("c.nome")
                If classeagenda.AggiungiCategoria(nomeCat) Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Nuova categoria creata.")
                    msg.ShowDialog()
                Else
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Impossibile creare la categoria: nome già esistente o non valido.")
                    msg.ShowDialog()
                End If
                campi = Nothing
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore fineaggiungicategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub modificacategoria()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona la categoria da rinominare")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) SceltaCatModificaHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.ModificaCategoria, "Modifica Categoria"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sono presenti categorie modificabili.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in modificacategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in modificacategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SceltaCatModificaHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        sceltacategoriamodifica(elemento, menuListaCat)
    End Sub

    Private Sub sceltacategoriamodifica(cat As String, lista As WGMenuLista)
        Try
            catDaModificare = cat
            lista.abbandona()
            campi = New wgcontrols.Campi()
            campi.Aggiungi("Nuovo nome", "c.nome", True, , cat)
            AddHandler campi.Fine, AddressOf finemodificacategoria
            campi.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in sceltacategoriamodifica: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in sceltacategoriamodifica: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finemodificacategoria()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim nuovoNome As String = campi.RecuperaValore("c.nome")
                If classeagenda.ModificaCategoria(catDaModificare, nuovoNome) Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Categoria rinominata correttamente.")
                    msg.ShowDialog()
                Else
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Si è verificato un errore durante la modifica della categoria.")
                    msg.ShowDialog()
                End If
                campi = Nothing
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Operazione annullata.")
                messaggio.ShowDialog()
                campi = Nothing
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in finemodificacategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in finemodificacategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub eliminacategoria()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona la categoria da eliminare")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) SceltaCatEliminaHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.EliminaCategoria, "Elimina Categoria"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sono presenti categorie eliminabili.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in eliminacategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in eliminacategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SceltaCatEliminaHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        sceltacategoriaelimina(elemento, menuListaCat)
    End Sub

    Private Sub sceltacategoriaelimina(cat As String, lista As WGMenuLista)
        Try
            lista.abbandona()
            _catDaEliminare = cat
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf ConfermaEliminaCategoria
            domanda.avvia($"Confermi l'eliminazione definitiva della categoria '{cat}'?")
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in sceltacategoriaelimina: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in sceltacategoriaelimina: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaEliminaCategoria(risp As DialogResult)
        If risp = DialogResult.Yes Then
            If classeagenda.EliminaCategoria(_catDaEliminare) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Categoria rimossa definitivamente.")
                msg.ShowDialog()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile eliminare la categoria.")
                msg.ShowDialog()
            End If
        End If
    End Sub

    Private Sub spostaappuntamentocategoria(listaAttiva As WGMenuLista)
        Try
            idsOperazione.Clear()
            Dim selection As List(Of String) = listaAttiva.ottieniElementi()
            If selection.Count > 0 Then
                idsOperazione.AddRange(selection)
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Selezionare almeno un elemento per procedere con lo spostamento.")
                msg.ShowDialog()
                Exit Sub
            End If

            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona la Categoria di Destinazione")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)
                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) FineSpostaCatHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SpostaCategoria, "Sposta Categoria"))
                menuListaCat.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sono disponibili categorie di destinazione.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in spostaappuntamentocategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in spostaappuntamentocategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineSpostaCatHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        fineSpostamentoCategoria(elemento, menuListaCat, menuListaAppuntamenti)
    End Sub

    Private Sub fineSpostamentoCategoria(cat As String, listaCat As WGMenuLista, listaOrigine As WGMenuLista)
        Try
            listaCat.abbandona()
            _catDestinazioneSpostamento = cat
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf ConfermaSpostamentoCategoria
            Dim countMsg As String = If(idsOperazione.Count > 1, $"{idsOperazione.Count} elementi selezionati", "l'elemento selezionato")
            domanda.avvia($"Confermi di voler spostare {countMsg} nella categoria '{cat}'?")
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in fineSpostamentoCategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in fineSpostamentoCategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaSpostamentoCategoria(risp As DialogResult)
        If risp = DialogResult.Yes Then
            Dim successCount As Integer = 0
            For Each id As String In idsOperazione
                If classeagenda.SpostaAppuntamentoCategoria(id, _catDestinazioneSpostamento) Then
                    successCount += 1
                End If
            Next

            If successCount > 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio($"{successCount} elementi spostati correttamente nella categoria '{_catDestinazioneSpostamento}'.")
                msg.ShowDialog()

                If menuListaAppuntamenti IsNot Nothing Then
                    Try
                        menuListaAppuntamenti.abbandona()
                    Catch ex As Exception
                    End Try
                End If
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Si è verificato un errore durante lo spostamento.")
                msg.ShowDialog()
            End If
        End If
    End Sub

    Private Sub OnElementoEvidenziato(sender As Object, e As VoceCambiataEventArgs)
        idCorrente = e.IdVoce
    End Sub

    Private Sub duplicaappuntamento(listaAttiva As WGMenuLista)
        Try
            Dim ids As List(Of String) = listaAttiva.ottieniElementi()
            If ids.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Selezionare un appuntamento da duplicare.")
                msg.ShowDialog()
                Exit Sub
            End If
            If ids.Count > 1 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La duplicazione è consentita per un solo appuntamento alla volta.")
                msg.ShowDialog()
                Exit Sub
            End If
            idDuplica = ids(0)
            campi = New wgcontrols.Campi()
            campi.AggiungiData("Data nuovo appuntamento", "c.data", True)
            AddHandler campi.Fine, AddressOf fineduplicazione
            campi.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in duplicaappuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in duplicaappuntamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub fineduplicazione()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim nuovaData As String = campi.RecuperaValore("c.data")
                If classeagenda.ClonaAppuntamento(idDuplica, nuovaData) Then
                    AvviaSincronizzazioneAgendaDopoModifica()
                    If menuListaAppuntamenti IsNot Nothing Then
                        Try
                            menuListaAppuntamenti.abbandona()
                        Catch exChiusura As Exception
                        End Try
                    End If
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Appuntamento duplicato correttamente.")
                    msg.ShowDialog()
                    AvviaCaricamento()
                Else
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Errore durante la duplicazione.")
                    msg.ShowDialog()
                End If
                campi = Nothing
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Operazione annullata.")
                messaggio.ShowDialog()
                campi = Nothing
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in fineduplicazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in fineduplicazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub nuovo()
        UsaAgendaEPoi(AddressOf nuovoInterna)
    End Sub

    Private Sub nuovoInterna()
        Dim mnuNvo As New WGMenu()
        mnuNvo.aggiungi("Inserimento appuntamento", AddressOf NvoApp)
        mnuNvo.aggiungi("Inserimento ricorrenza", AddressOf InserimentoRicorrenza)
        mnuNvo.aggiungi("Inserimento evento", AddressOf InserimentoEvento)
        mnuNvo.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.NuovoInserimento, "Nuovo Inserimento"))
        mnuNvo.avvia("Scegli tipo inserimento", True)
    End Sub

    Private Sub NvoApp()
        nuovoappuntamento()
    End Sub

    Public Sub nuovoappuntamento(Optional dataappuntamento As String = "")
        Try
            idmodifica = ""
            data = dataappuntamento
            orainizio = ""
            orafine = ""
            dataFine = ""
            oggetto = ""
            nome = ""
            note = ""
            preavviso = 0
            frequenza = CType(0, CLSAgenda.Frequenza)
            categoriaSelezionata = ""
            _modalitaEvento = False

            campi = New wgcontrols.Campi()
            campi.Aggiungi("Oggetto", "c.oggetto", True)
            campi.Aggiungi("Nome appuntamento", "c.nome", True)
            AddHandler campi.Fine, AddressOf finecampi_dati_base
            campi.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore inatteso creazione appuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in nuovoappuntamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub NuovaRicorrenzaOnomastico(Optional nomePredefinito As String = "", Optional dataOnomastico As String = "")
        Try
            classeagenda = CLSAgenda.GetInstance()

            idmodifica = ""
            oggetto = "Onomastico"
            nome = nomePredefinito
            data = dataOnomastico
            dataFine = ""
            orainizio = "00:00"
            orafine = "23:59"
            note = ""
            preavviso = 0
            frequenza = CLSAgenda.Frequenza.Annuale
            categoriaSelezionata = ""

            campi = New wgcontrols.Campi()
            campi.Aggiungi("Nome della persona", "c.nome", True, , nomePredefinito)
            If String.IsNullOrWhiteSpace(dataOnomastico) Then
                campi.AggiungiData("Data dell'onomastico", "c.data", True)
            End If
            AddHandler campi.Fine, AddressOf FineCampiOnomastico
            campi.Avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore inatteso creazione onomastico: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("MenuAgenda.NuovaRicorrenzaOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCampiOnomastico()
        Try
            If campi Is Nothing Then Exit Sub

            If Not campi.TuttiObbligatoriRiempiti() Then
                campi = Nothing
                Exit Sub
            End If

            nome = campi.RecuperaValore("c.nome").Trim()

            Dim dataCampo As String = ""
            Try
                dataCampo = campi.RecuperaValore("c.data")
            Catch exData As Exception
                _logger.ScriviLog("MenuAgenda.FineCampiOnomastico (lettura data) - " & exData.Message)
            End Try
            If Not String.IsNullOrWhiteSpace(dataCampo) Then
                data = dataCampo.Trim()
            End If

            oggetto = "Onomastico"
            orainizio = "00:00"
            orafine = "23:59"
            dataFine = ""
            frequenza = CLSAgenda.Frequenza.Annuale
            categoriaSelezionata = ""

            campi = Nothing

            If String.IsNullOrWhiteSpace(nome) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nome della persona non valido.")
                msg.ShowDialog()
                Exit Sub
            End If

            Dim dtControllo As DateTime
            If Not DateTime.TryParseExact(data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dtControllo) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Data dell'onomastico non valida.")
                msg.ShowDialog()
                Exit Sub
            End If

            AvviaPreavvisoOnomastico()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.FineCampiOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaPreavvisoOnomastico()
        Try
            Dim modNot As String = config.LeggiValore("NotificheAttive")
            If modNot = "Sempre" OrElse modNot = "Mattina e Pomeriggio" Then
                mnuPre = New WGMenu()
                mnuPre.aggiungi("Il giorno stesso", AddressOf ImpPreOnoStesso)
                mnuPre.aggiungi("1 giorno prima", AddressOf ImpPreOnoUnGiorno)
                mnuPre.aggiungi("3 giorni prima", AddressOf ImpPreOnoTreGiorni)
                mnuPre.aggiungi("1 settimana prima", AddressOf ImpPreOnoUnaSettimana)
                mnuPre.avvia("Quanto tempo prima avvisare", True)
            Else
                preavviso = 0
                SelezionaCategoria(True)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AvviaPreavvisoOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpPreOnoStesso()
        Try
            preavviso = 0
            If mnuPre IsNot Nothing Then mnuPre.chiudi()
            SelezionaCategoria(True)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpPreOnoStesso - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpPreOnoUnGiorno()
        Try
            preavviso = 1440
            If mnuPre IsNot Nothing Then mnuPre.chiudi()
            SelezionaCategoria(True)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpPreOnoUnGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpPreOnoTreGiorni()
        Try
            preavviso = 4320
            If mnuPre IsNot Nothing Then mnuPre.chiudi()
            SelezionaCategoria(True)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpPreOnoTreGiorni - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpPreOnoUnaSettimana()
        Try
            preavviso = 10080
            If mnuPre IsNot Nothing Then mnuPre.chiudi()
            SelezionaCategoria(True)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ImpPreOnoUnaSettimana - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InserimentoRicorrenza()
        menuListaFreq = New WGMenuLista()
        menuListaFreq.impostaTitolo("Imposta Ripetizione")
        menuListaFreq.multiselezioneConsentita = False
        menuListaFreq.aggiungiElemento("Giornaliera", "1")
        menuListaFreq.aggiungiElemento("Settimanale", "2")
        menuListaFreq.aggiungiElemento("Mensile", "4")
        menuListaFreq.aggiungiElemento("Annuale", "5")
        AddHandler menuListaFreq.ElementoSelezionato, Sub(v As String) FreqRic(v)
        AddHandler menuListaFreq.escPremuto, Sub() menuListaFreq.abbandona()
        menuListaFreq.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Frequenza, "Frequenza Ripetizione"))
        menuListaFreq.avvia()
    End Sub

    Private Sub FreqRic(val As String)
        Dim ls As List(Of String) = menuListaFreq.ottieniElementi()
        Dim el As String = ls(0)
        Dim freqVal As Integer
        If Not Integer.TryParse(el, freqVal) Then
            menuListaFreq.abbandona()
            Exit Sub
        End If
        frequenza = CType(freqVal, CLSAgenda.Frequenza)
        menuListaFreq.abbandona()

        idmodifica = ""
        orainizio = "00:00"
        orafine = "23:59"
        dataFine = ""
        categoriaSelezionata = ""

        campi = New wgcontrols.Campi()
        campi.Aggiungi("Oggetto", "c.oggetto", True)
        campi.Aggiungi("Nome", "c.nome", True)
        campi.AggiungiData("Data di partenza", "c.data", True)
        campi.AggiungiOrario("Ora (opzionale)", "c.inizio", False)
        AddHandler campi.Fine, AddressOf FineRic
        campi.Avvia()
    End Sub

    Private Sub FineRic()
        If campi.TuttiObbligatoriRiempiti() Then
            oggetto = campi.RecuperaValore("c.oggetto")
            nome = campi.RecuperaValore("c.nome")
            data = campi.RecuperaValore("c.data")
            Dim o As String = campi.RecuperaValore("c.inizio")
            If Not String.IsNullOrEmpty(o) Then
                orainizio = o
                orafine = o
            Else
                orainizio = "00:00"
                orafine = "23:59"
            End If
            campi = Nothing

            ScegliDurataRicorrenza()
        Else
            campi = Nothing
        End If
    End Sub

    Private Sub ScegliDurataRicorrenza()
        menuDurataRic = New WGMenu()
        Select Case frequenza
            Case CLSAgenda.Frequenza.Giornaliera
                menuDurataRic.aggiungi("1 settimana", Sub() ImpostaDurataRic(7))
                menuDurataRic.aggiungi("1 mese", Sub() ImpostaDurataRic(30))
                menuDurataRic.aggiungi("3 mesi", Sub() ImpostaDurataRic(90))
                menuDurataRic.aggiungi("6 mesi", Sub() ImpostaDurataRic(180))
                menuDurataRic.aggiungi("1 anno", Sub() ImpostaDurataRic(365))
                menuDurataRic.aggiungi("2 anni", Sub() ImpostaDurataRic(730))
                menuDurataRic.aggiungi("10 anni", Sub() ImpostaDurataRic(3650))
            Case CLSAgenda.Frequenza.Settimanale, CLSAgenda.Frequenza.Bisettimanale
                menuDurataRic.aggiungi("2 settimane", Sub() ImpostaDurataRic(14))
                menuDurataRic.aggiungi("1 mese", Sub() ImpostaDurataRic(30))
                menuDurataRic.aggiungi("3 mesi", Sub() ImpostaDurataRic(90))
                menuDurataRic.aggiungi("6 mesi", Sub() ImpostaDurataRic(180))
                menuDurataRic.aggiungi("1 anno", Sub() ImpostaDurataRic(365))
                menuDurataRic.aggiungi("2 anni", Sub() ImpostaDurataRic(730))
                menuDurataRic.aggiungi("10 anni", Sub() ImpostaDurataRic(3650))
            Case CLSAgenda.Frequenza.Mensile
                menuDurataRic.aggiungi("3 mesi", Sub() ImpostaDurataRic(90))
                menuDurataRic.aggiungi("6 mesi", Sub() ImpostaDurataRic(180))
                menuDurataRic.aggiungi("1 anno", Sub() ImpostaDurataRic(365))
                menuDurataRic.aggiungi("2 anni", Sub() ImpostaDurataRic(730))
                menuDurataRic.aggiungi("10 anni", Sub() ImpostaDurataRic(3650))
            Case CLSAgenda.Frequenza.Annuale
                menuDurataRic.aggiungi("2 anni", Sub() ImpostaDurataRic(730))
                menuDurataRic.aggiungi("3 anni", Sub() ImpostaDurataRic(1095))
                menuDurataRic.aggiungi("5 anni", Sub() ImpostaDurataRic(1825))
                menuDurataRic.aggiungi("10 anni", Sub() ImpostaDurataRic(3650))
                menuDurataRic.aggiungi("20 anni", Sub() ImpostaDurataRic(7300))
            Case Else
                menuDurataRic.aggiungi("1 mese", Sub() ImpostaDurataRic(30))
                menuDurataRic.aggiungi("6 mesi", Sub() ImpostaDurataRic(180))
                menuDurataRic.aggiungi("1 anno", Sub() ImpostaDurataRic(365))
        End Select
        menuDurataRic.aggiungi("Indefinitamente", Sub() ImpostaDurataRic(-1))
        menuDurataRic.aggiungi("Data scelta dall'utente", AddressOf ScegliDataFineManuale)
        menuDurataRic.avvia("Scegli durata della ricorrenza", True)
    End Sub

    Private Sub ImpostaDurataRic(giorni As Integer)
        If menuDurataRic IsNot Nothing Then menuDurataRic.chiudi()
        If giorni = -1 Then
            dataFine = ""
        Else
            Dim dInizio As DateTime
            If DateTime.TryParseExact(data, "dd/MM/yyyy", Nothing, Globalization.DateTimeStyles.None, dInizio) Then
                dataFine = dInizio.AddDays(giorni).ToString("dd/MM/yyyy")
            End If
        End If
        AvviaSceltaPreavvisoRicorrenza()
    End Sub

    Private Sub ScegliDataFineManuale()
        If menuDurataRic IsNot Nothing Then menuDurataRic.chiudi()
        campi = New wgcontrols.Campi()
        campi.AggiungiData("Data di fine ricorrenza", "c.datafine", True)
        AddHandler campi.Fine, AddressOf FineDataManualeRicorrenza
        campi.Avvia()
    End Sub

    Private Sub FineDataManualeRicorrenza()
        If campi.TuttiObbligatoriRiempiti() Then
            dataFine = campi.RecuperaValore("c.datafine")
            campi = Nothing
            AvviaSceltaPreavvisoRicorrenza()
        Else
            campi = Nothing
        End If
    End Sub

    Private Sub AvviaSceltaPreavvisoRicorrenza()
        Dim modNot As String = config.LeggiValore("NotificheAttive")
        If modNot = "Sempre" OrElse modNot = "Mattina e Pomeriggio" Then
            mnuPre = New WGMenu()
            mnuPre.aggiungi("1 ora prima", Sub() ImpPreRic(60))
            mnuPre.aggiungi("4 ore prima", Sub() ImpPreRic(240))
            mnuPre.aggiungi("1 giorno prima", Sub() ImpPreRic(1440))
            mnuPre.aggiungi("2 giorni prima", Sub() ImpPreRic(2880))
            mnuPre.aggiungi("3 giorni prima", Sub() ImpPreRic(4320))
            mnuPre.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Preavviso, "Preavviso"))
            mnuPre.avvia("Preavviso", True)
        Else
            preavviso = 0
            SelezionaCategoria()
        End If
    End Sub

    Private Sub ImpPreRic(min As Integer)
        preavviso = min
        If mnuPre IsNot Nothing Then mnuPre.chiudi()
        SelezionaCategoria()
    End Sub

    Private Sub InserimentoEvento()
        idmodifica = ""
        data = ""
        orainizio = ""
        orafine = ""
        dataFine = ""
        oggetto = ""
        nome = ""
        note = ""
        preavviso = 0
        frequenza = CType(0, CLSAgenda.Frequenza)
        categoriaSelezionata = ""
        tmpDurOre = 0
        _modalitaEvento = True

        campi = New wgcontrols.Campi()
        campi.Aggiungi("Oggetto", "c.oggetto", True)
        campi.Aggiungi("Nome evento", "c.nome", True)
        AddHandler campi.Fine, AddressOf FineDatiBaseEvento
        campi.Avvia()
    End Sub

    Private Sub FineDatiBaseEvento()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                oggetto = campi.RecuperaValore("c.oggetto")
                nome = campi.RecuperaValore("c.nome")
                campi = Nothing
                ChiediDurataEvento()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Operazione annullata.")
                messaggio.ShowDialog()
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in FineDatiBaseEvento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediDurataEvento()
        Try
            campi = New wgcontrols.Campi()
            campi.AggiungiNumerico("Durata evento in ore", "c.durata", True)
            AddHandler campi.Fine, AddressOf FineDurataEvento
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in ChiediDurataEvento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineDurataEvento()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim durStr As String = campi.RecuperaValore("c.durata")
                campi = Nothing

                If Not Integer.TryParse(durStr, tmpDurOre) OrElse tmpDurOre <= 0 Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("La durata deve essere un numero intero maggiore di zero.")
                    msg.ShowDialog()
                    ChiediDurataEvento()
                    Exit Sub
                End If

                data = ""
                ScegliDataConMenu()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in FineDurataEvento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finecampi_dati_base()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                oggetto = campi.RecuperaValore("c.oggetto")
                nome = campi.RecuperaValore("c.nome")
                campi = Nothing

                If data <> "" Then
                    campi = New wgcontrols.Campi()
                    campi.AggiungiOrario("Ora inizio", "c.inizio", True)
                    campi.AggiungiOrario("Ora fine", "c.fine", True)
                    AddHandler campi.Fine, AddressOf finecampi_orari_preimpostati
                    campi.Avvia()
                Else
                    ScegliDataConMenu()
                End If
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Operazione annullata.")
                messaggio.ShowDialog()
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in finecampi_dati_base - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub finecampi_orari_preimpostati()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                orainizio = campi.RecuperaValore("c.inizio")
                orafine = campi.RecuperaValore("c.fine")
                campi = Nothing
                dataFine = data
                ChiediNoteEPreavviso()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in finecampi_orari_preimpostati - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliDataConMenu()
        Try
            _menuSceltaOrario = New WGMenu()
            _menuSceltaOrario.aggiungi("Cerca all'interno degli orari lavorativi", AddressOf AvviaRicercaDateLavorative)
            _menuSceltaOrario.aggiungi("Cerca fuori dagli orari lavorativi", AddressOf AvviaRicercaDateExtra)
            _menuSceltaOrario.aggiungi("Inserisci data e orario manualmente", AddressOf ScegliInserimentoManuale)
            _menuSceltaOrario.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SceltaOrario, "Scelta Orario"))
            _menuSceltaOrario.avvia("Modalità ricerca orari", True)
        Catch ex As Exception
            _logger.ScriviLog("Errore ScegliDataConMenu - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliInserimentoManuale()
        If _modalitaEvento Then
            InserimentoManualeDataOrarioEvento()
        Else
            InserimentoManualeDataOrario()
        End If
    End Sub

    Private Sub AvviaRicercaDateLavorative()
        AvviaRicercaDate(False)
    End Sub

    Private Sub AvviaRicercaDateExtra()
        AvviaRicercaDate(True)
    End Sub

    Private Sub InserimentoManualeDataOrario()
        campi = New wgcontrols.Campi()
        campi.AggiungiData("Data Inizio", "c.data", True)
        campi.AggiungiOrario("Ora inizio", "c.inizio", True)
        campi.AggiungiOrario("Ora fine", "c.fine", True)
        AddHandler campi.Fine, AddressOf FineCampiDataManuale
        campi.Avvia()
    End Sub

    Private Sub InserimentoManualeDataOrarioEvento()
        campi = New wgcontrols.Campi()
        campi.AggiungiData("Data Inizio", "c.data", True)
        campi.AggiungiOrario("Ora inizio", "c.inizio", True)
        AddHandler campi.Fine, AddressOf FineCampiDataManualeEvento
        campi.Avvia()
    End Sub

    Private Sub FineCampiDataManualeEvento()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim dataVal As String = campi.RecuperaValore("c.data")
                Dim oraIniVal As String = campi.RecuperaValore("c.inizio")

                Dim tsIni As TimeSpan
                If Not TimeSpan.TryParse(oraIniVal, tsIni) Then
                    campi = Nothing
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Formato orario non valido. Inserire un orario nel formato HH:mm.")
                    msg.ShowDialog()
                    InserimentoManualeDataOrarioEvento()
                    Exit Sub
                End If
                campi = Nothing

                Dim dtInizio As DateTime
                If Not DateTime.TryParseExact(dataVal, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dtInizio) Then
                    dtInizio = DateTime.Today
                End If

                data = dataVal
                orainizio = oraIniVal

                Dim tsFin As TimeSpan = tsIni.Add(TimeSpan.FromHours(tmpDurOre))
                Dim giorniAggiunti As Integer = CInt(Math.Floor(tsFin.TotalDays))
                Dim oraFineNormalizzata As New TimeSpan(tsFin.Hours, tsFin.Minutes, 0)

                dataFine = dtInizio.AddDays(giorniAggiunti).ToString("dd/MM/yyyy")
                orafine = oraFineNormalizzata.ToString("hh\:mm")

                ChiediNoteEPreavviso()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.FineCampiDataManualeEvento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaRicercaDate(escludiLavoro As Boolean)
        Try
            ImpostaParametriRicerca(escludiLavoro)
            If _modalitaEvento Then
                _search_durataMin = tmpDurOre * 60
                _search_durataMax = tmpDurOre * 60
            End If

            attesa = New WGProcessoInCorso()
            attesa.Avvia("Ricerca date disponibili in corso...", False)

            Dim t As New Thread(AddressOf RicercaDateLibereThread)
            t.Start()
            AttendiThread(t)

            Dim dateDisponibili As List(Of String) = _threadResultDateDisp

            If dateDisponibili Is Nothing OrElse dateDisponibili.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna data libera trovata.")
                msg.ShowDialog()
                ScegliDataConMenu()
            Else
                menuDate = New WGMenuLista()
                menuDate.impostaTitolo("Seleziona un giorno disponibile")
                menuDate.multiselezioneConsentita = False

                For Each d In dateDisponibili
                    menuDate.aggiungiElemento(d, d)
                Next

                AddHandler menuDate.ElementoSelezionato, Sub(v As String) DataSelezionataHandler(v)
                AddHandler menuDate.escPremuto, AddressOf ChiudiMenuDate
                menuDate.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneDataLibera, "Selezione Data"))
                menuDate.avvia()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then attesa.Fine()
            _logger.ScriviLog("Errore AvviaRicercaDate - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DataSelezionataHandler(val As String)
        Try
            Dim listaid As List(Of String) = menuDate.ottieniElementi()
            Dim elemento As String = listaid(0)
            _dtCorrenteSlot = DateTime.ParseExact(elemento, "dd/MM/yyyy", Nothing)

            attesa = New WGProcessoInCorso()
            attesa.Avvia($"Ricerca orari liberi per il {elemento}...", False)

            Dim t As New Thread(AddressOf RicercaOrariLiberiThread)
            t.Start()
            AttendiThread(t)

            If _threadResultSlots Is Nothing OrElse _threadResultSlots.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun orario libero trovato.")
                msg.ShowDialog()
            Else
                menuListaSlot = New WGMenuLista()
                menuListaSlot.impostaTitolo($"Seleziona l'orario per il giorno {elemento}")
                menuListaSlot.multiselezioneConsentita = False

                For Each s In _threadResultSlots
                    menuListaSlot.aggiungiElemento(s, s)
                Next

                AddHandler menuListaSlot.ElementoSelezionato, Sub(v As String) OrarioSelezionatoHandler(v)
                AddHandler menuListaSlot.escPremuto, AddressOf chiudiorariolista
                menuListaSlot.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneOrarioLibero, "Selezione Orario"))
                menuListaSlot.avvia()
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore DataSelezionataHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub chiudiorariolista()
        Try
            RemoveHandler menuListaSlot.escPremuto, AddressOf chiudiorariolista
            menuListaSlot.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.chiudiorariolista - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Try
            If menuDate IsNot Nothing Then
                RemoveHandler menuDate.escPremuto, AddressOf ChiudiMenuDate
                menuDate.abbandona()
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.chiudiorariolista (menuDate) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        ScegliDataConMenu()
    End Sub

    Private Sub ChiudiMenuDate()
        Try
            RemoveHandler menuDate.escPremuto, AddressOf ChiudiMenuDate
            menuDate.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ChiudiMenuDate - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        ScegliDataConMenu()
    End Sub

    Private Sub OrarioSelezionatoHandler(val As String)
        Try
            RemoveHandler menuListaSlot.escPremuto, AddressOf chiudiorariolista
            RemoveHandler menuDate.escPremuto, AddressOf ChiudiMenuDate
            Dim listaid As List(Of String) = menuListaSlot.ottieniElementi()
            Dim elemento As String = listaid(0)
            menuListaSlot.abbandona()
            menuDate.abbandona()

            Dim parts As String() = elemento.Split(New String() {" - "}, StringSplitOptions.None)
            If parts.Length >= 2 Then
                data = _dtCorrenteSlot.ToString("dd/MM/yyyy")
                orainizio = parts(0).Trim()
                orafine = parts(1).Trim()
                dataFine = data
            End If

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf ConfermaOrarioFineNuovo
            domanda.avvia($"Hai scelto: {data} dalle {orainizio} alle {orafine}.{Environment.NewLine}L'orario di fine è corretto?")
        Catch ex As Exception
            _logger.ScriviLog("Errore OrarioSelezionatoHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaOrarioFineNuovo(risp As DialogResult)
        If risp = DialogResult.Yes Then
            dataFine = data
            ChiediNoteEPreavviso()
        ElseIf risp = DialogResult.No Then
            campi = New wgcontrols.Campi()
            campi.AggiungiOrario("Nuova Ora fine", "c.fine", True, , orafine)
            AddHandler campi.Fine, AddressOf FineModificaOraFine
            campi.Avvia()
        End If
    End Sub

    Private Sub FineCampiDataManuale()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim dataVal As String = campi.RecuperaValore("c.data")
                Dim oraIniVal As String = campi.RecuperaValore("c.inizio")
                Dim oraFinVal As String = campi.RecuperaValore("c.fine")

                Dim tsIni As TimeSpan
                Dim tsFin As TimeSpan
                Dim iniOk As Boolean = TimeSpan.TryParse(oraIniVal, tsIni)
                Dim finOk As Boolean = TimeSpan.TryParse(oraFinVal, tsFin)

                If Not iniOk OrElse Not finOk Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    If Not iniOk Then campi.AggiungiOrario("Ora inizio", "c.inizio", True)
                    If Not finOk Then campi.AggiungiOrario("Ora fine", "c.fine", True)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Formato orario non valido. Inserire un orario nel formato HH:mm.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf FineCampiDataManuale
                    campi.Avvia()
                    Exit Sub
                End If

                data = dataVal
                orainizio = oraIniVal
                orafine = oraFinVal
                campi = Nothing
                dataFine = data
                ChiediNoteEPreavviso()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.FineCampiDataManuale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineModificaOraFine()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                orafine = campi.RecuperaValore("c.fine")
                campi = Nothing
                dataFine = data
                ChiediNoteEPreavviso()
            Else
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in FineModificaOraFine - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediNoteEPreavviso()
        Try
            campi = New wgcontrols.Campi()
            campi.Aggiungi("Note", "c.note", True)
            AddHandler campi.Fine, AddressOf CompletamentoDati
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in ChiediNoteEPreavviso - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletamentoDati()
        Try
            If Not campi.TuttiObbligatoriRiempiti() Then
                campi = Nothing
                Exit Sub
            End If
            note = campi.RecuperaValore("c.note")
            campi = Nothing

            Dim dtIni, dtFin As DateTime
            If DateTime.TryParse(orainizio, dtIni) AndAlso DateTime.TryParse(orafine, dtFin) Then
                If dtFin < dtIni AndAlso data = dataFine Then
                    Dim domNxt As New WGDomanda()
                    AddHandler domNxt.rispostadomanda, AddressOf ConfermaEstendiGiornoSuccessivo
                    domNxt.avvia($"L'ora di fine precede l'inizio.{Environment.NewLine}Estendere l'attività al giorno successivo?")
                    Exit Sub
                End If
            End If

            AvviaSceltaPreavviso()
        Catch ex As Exception
            _logger.ScriviLog("Exception in CompletamentoDati - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaSceltaPreavviso()
        Dim modNot As String = config.LeggiValore("NotificheAttive")
        Dim notifOk As Boolean = (modNot = "Sempre" OrElse modNot = "Mattina e Pomeriggio")
        If notifOk Then
            mnuPre = New WGMenu()
            mnuPre.aggiungi("1 ora prima", AddressOf PreOra)
            mnuPre.aggiungi("4 ore prima", AddressOf PreQua)
            mnuPre.aggiungi("1 giorno prima", AddressOf PreGio)
            mnuPre.aggiungi("2 giorni prima", AddressOf PreDue)
            mnuPre.aggiungi("3 giorni prima", AddressOf PreTre)
            mnuPre.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Preavviso, "Preavviso"))
            mnuPre.avvia("Preavviso", True)
        Else
            preavviso = 0
            CheckOverlapAndProceed()
        End If
    End Sub

    Private Sub PreOra()
        ImpPre(60)
    End Sub

    Private Sub PreQua()
        ImpPre(240)
    End Sub

    Private Sub PreGio()
        ImpPre(1440)
    End Sub

    Private Sub PreDue()
        ImpPre(2880)
    End Sub

    Private Sub PreTre()
        ImpPre(4320)
    End Sub

    Private Sub ImpPre(min As Integer)
        preavviso = min
        If mnuPre IsNot Nothing Then mnuPre.chiudi()
        CheckOverlapAndProceed()
    End Sub

    Private Sub ConfermaEstendiGiornoSuccessivo(r As DialogResult)
        If r = DialogResult.Yes Then
            Dim dSt As DateTime
            If DateTime.TryParse(data, dSt) Then
                dataFine = dSt.AddDays(1).ToString("dd/MM/yyyy")
            End If
            AvviaSceltaPreavviso()
        End If
    End Sub

    Private Sub finecampi_dati_modifica()
        Try
            If campi.TuttiObbligatoriRiempiti() Then
                Dim nomeVal As String = campi.RecuperaValore("c.nome")
                Dim dataVal As String = campi.RecuperaValore("c.data")
                Dim dataFineVal As String = campi.RecuperaValore("c.datafine")
                Dim oggettoVal As String = campi.RecuperaValore("c.oggetto")
                Dim oraIniVal As String = campi.RecuperaValore("c.inizio")
                Dim oraFinVal As String = campi.RecuperaValore("c.fine")
                Dim noteVal As String = campi.RecuperaValore("c.note")

                If String.IsNullOrEmpty(dataFineVal) Then dataFineVal = dataVal

                Dim dtIni As DateTime
                Dim dtFin As DateTime
                Dim dtIniOk As Boolean = DateTime.TryParseExact(dataVal, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dtIni)
                Dim dtFinOk As Boolean = DateTime.TryParseExact(dataFineVal, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, dtFin)

                If Not dtIniOk OrElse Not dtFinOk Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    If Not dtIniOk Then campi.AggiungiData("Modifica Data Inizio", "c.data", True)
                    If Not dtFinOk Then campi.AggiungiData("Modifica Data Fine", "c.datafine", True)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("La data inserita non è valida.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf finecampi_dati_modifica
                    campi.Avvia()
                    Exit Sub
                End If

                If dtIni > dtFin Then
                    campi = Nothing
                    campi = New wgcontrols.Campi()
                    campi.AggiungiData("Modifica Data Inizio", "c.data", True, , dataVal)
                    campi.AggiungiData("Modifica Data Fine", "c.datafine", True, , dataFineVal)
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("La data di inizio non può essere successiva alla data di fine.")
                    msg.ShowDialog()
                    AddHandler campi.Fine, AddressOf finecampi_dati_modifica
                    campi.Avvia()
                    Exit Sub
                End If

                nome = nomeVal
                data = dataVal
                dataFine = dataFineVal
                oggetto = oggettoVal
                orainizio = oraIniVal
                orafine = oraFinVal
                note = noteVal
                campi = Nothing

                Dim dtOraIni, dtOraFin As DateTime
                If DateTime.TryParse(orainizio, dtOraIni) AndAlso DateTime.TryParse(orafine, dtOraFin) Then
                    If dtOraFin < dtOraIni AndAlso data = dataFine Then
                        Dim domNxt As New WGDomanda()
                        AddHandler domNxt.rispostadomanda, AddressOf ConfermaEstendiGiornoSuccessivo
                        domNxt.avvia($"L'ora di fine precede l'inizio.{Environment.NewLine}Estendere l'attività al giorno successivo?")
                        Exit Sub
                    End If
                End If
                AvviaSceltaPreavviso()
            Else
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Operazione annullata.")
                messaggio.ShowDialog()
                campi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.finecampi_dati_modifica - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CheckOverlapAndProceed()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Verifica sovrapposizioni in corso...", False)

            Dim dettaglio As New CLSAgenda.AppuntamentoDettaglio()
            dettaglio.DataInizio = data
            dettaglio.DataFine = dataFine
            dettaglio.OraInizio = orainizio
            dettaglio.OraFine = orafine
            dettaglio.IdEscluso = idmodifica

            _dettaglioSovrapposizione = dettaglio
            Dim tVerifica As New Thread(AddressOf VerificaSovrapposizioniThread)
            tVerifica.Start()
            AttendiThread(tVerifica)

            Dim isSovrapposto As Boolean = _threadResultBool

            If isSovrapposto Then
                Dim domanda As New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf ConfermaSovrapposizione
                domanda.avvia($"Rilevata una sovrapposizione di orario.{Environment.NewLine}Procedere comunque con l'inserimento?")
            Else
                ProcediDopoSovrapposizione()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore in CheckOverlapAndProceed - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ProcediDopoSovrapposizione()
        If idmodifica <> "" Then
            SalvaAppuntamento()
        Else
            SelezionaCategoria()
        End If
    End Sub

    Private Sub ConfermaSovrapposizione(risp As DialogResult)
        If risp = DialogResult.Yes Then
            ProcediDopoSovrapposizione()
        ElseIf risp = DialogResult.No Then
            Dim domandaData As New WGDomanda()
            AddHandler domandaData.rispostadomanda, AddressOf ConfermaCambiaDataSlot
            domandaData.avvia("Vuoi cambiare data e scegliere uno slot libero?")
        End If
    End Sub

    Private Sub ConfermaCambiaDataSlot(rispData As DialogResult)
        If rispData = DialogResult.Yes Then
            ScegliDataConMenu()
        ElseIf rispData = DialogResult.No Then
            Dim msg As New WGMessaggi()
            msg.Messaggio("Operazione annullata.")
            msg.ShowDialog()
        End If
    End Sub

    Private Sub SelezionaCategoria(Optional escludiLavoro As Boolean = False)
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats
            If cats Is Nothing OrElse cats.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessuna categoria trovata, aggiungerne una dalla funzione categorie nel menu agenda prima di procedere all'inserimento di un'appuntamento")
                messaggio.ShowDialog()
            Else
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Assegna Categoria")
                menuListaCat.multiselezioneConsentita = False
                Dim defaultCat As String = config.LeggiValore("CategoriaDefault")

                Dim elementi As New List(Of ElementoLista)
                elementi.Add(New ElementoLista("Nessuna", ""))
                For Each c As String In cats
                    If escludiLavoro AndAlso Not String.IsNullOrWhiteSpace(c) AndAlso c.Trim().ToLower() = "lavoro" Then
                        Continue For
                    End If
                    Dim label As String = c
                    If Not String.IsNullOrEmpty(defaultCat) AndAlso c.ToLower() = defaultCat.ToLower() Then
                        label &= " (Predefinita)"
                    End If
                    elementi.Add(New ElementoLista(label, c))
                Next

                menuListaCat.CaricaListaDinamica(elementi)

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) CategoriaSceltaNuovoAppHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.AssegnaCategoria, "Assegna Categoria"))
                menuListaCat.avvia()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in SelezionaCategoria: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in SelezionaCategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CategoriaSceltaNuovoAppHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        categoriaScelta(elemento, menuListaCat)
    End Sub

    Private Sub categoriaScelta(cat As String, listaCat As WGMenuLista)
        Try
            categoriaSelezionata = cat
            If cat.ToLower() = "lavoro" Then
                Dim startLavoroStr As String = config.LeggiValore("InizioLavoro")
                Dim endLavoroStr As String = config.LeggiValore("FineLavoro")
                If String.IsNullOrEmpty(startLavoroStr) Then startLavoroStr = "09:00"
                If String.IsNullOrEmpty(endLavoroStr) Then endLavoroStr = "18:00"
                Dim startL, endL, startAppt, endAppt As DateTime
                If DateTime.TryParse(startLavoroStr, startL) AndAlso DateTime.TryParse(endLavoroStr, endL) AndAlso DateTime.TryParse(orainizio, startAppt) AndAlso DateTime.TryParse(orafine, endAppt) Then
                    Dim now As DateTime = DateTime.Now.Date
                    Dim workStartFull As DateTime = now.Add(startL.TimeOfDay)
                    Dim workEndFull As DateTime = now.Add(endL.TimeOfDay)
                    Dim apptStartFull As DateTime = now.Add(startAppt.TimeOfDay)
                    Dim apptEndFull As DateTime = now.Add(endAppt.TimeOfDay)

                    If apptStartFull < workStartFull OrElse apptEndFull > workEndFull Then
                        Dim domandaWork As New WGDomanda()
                        AddHandler domandaWork.rispostadomanda, AddressOf ConfermaSlotFuoriLavoro
                        domandaWork.avvia($"Orario selezionato fuori dal turno lavorativo ({startLavoroStr}-{endLavoroStr}).{Environment.NewLine}Cercare uno slot libero alternativo?")
                        Exit Sub
                    End If
                End If
            End If

            SalvaAppuntamento()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in categoriaScelta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in categoriaScelta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaSlotFuoriLavoro(r As DialogResult)
        If r = DialogResult.Yes Then
            TrovaEScegliSlotExtra()
        ElseIf r = DialogResult.No Then
            SalvaAppuntamento()
        End If
    End Sub

    Private Sub TrovaEScegliSlotExtra()
        Try
            ImpostaParametriRicerca(False)
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Ricerca slot liberi...", False)

            Dim dtCorrente As DateTime
            If Not DateTime.TryParse(data, dtCorrente) Then dtCorrente = DateTime.Today

            _dtCorrenteSlot = dtCorrente
            Dim tSlot As New Thread(AddressOf RicercaOrariLiberiThread)
            tSlot.Start()
            AttendiThread(tSlot)
            Dim slots As List(Of String) = _threadResultSlots

            If slots Is Nothing OrElse slots.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun orario libero trovato.")
                msg.ShowDialog()
            Else
                menuListaSlot = New WGMenuLista()
                menuListaSlot.impostaTitolo("Slot Liberi disponibili per il " & dtCorrente.ToShortDateString())
                menuListaSlot.multiselezioneConsentita = False
                For Each s As String In slots
                    menuListaSlot.aggiungiElemento(s, s)
                Next
                menuListaSlot.aggiungiElemento("Inserimento manuale orario...", "MANUAL")

                AddHandler menuListaSlot.ElementoSelezionato, Sub(v As String) SlotSelezionatoFuoriLavoroHandler(v)
                AddHandler menuListaSlot.escPremuto, Sub() menuListaSlot.abbandona()
                menuListaSlot.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneOrarioExtra, "Orari Extra"))
                menuListaSlot.avvia()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then attesa.Fine()
            _logger.ScriviLog("Errore in TrovaEScegliSlotExtra - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SlotSelezionatoFuoriLavoroHandler(val As String)
        Try
            Dim listaid As List(Of String) = menuListaSlot.ottieniElementi()
            Dim elemento As String = listaid(0)

            If elemento = "MANUAL" Then
                menuListaSlot.abbandona()
                nuovoappuntamento(data)
            Else
                Dim parts As String() = elemento.Split(New String() {" - "}, StringSplitOptions.None)
                If parts.Length >= 2 Then
                    orainizio = parts(0).Trim()
                    orafine = parts(1).Trim()
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Orario aggiornato correttamente a: " & elemento)
                    msg.ShowDialog()
                    menuListaSlot.abbandona()
                    SalvaAppuntamento()
                Else
                    menuListaSlot.abbandona()
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore SlotSelezionatoFuoriLavoroHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SelezionaFrequenza()
        Try
            menuListaFreq = New WGMenuLista()
            menuListaFreq.impostaTitolo("Imposta Ripetizione (Ricorrenza)")
            menuListaFreq.multiselezioneConsentita = False
            menuListaFreq.aggiungiElemento("Nessuna ripetizione", "0")
            menuListaFreq.aggiungiElemento("Giornaliera", "1")
            menuListaFreq.aggiungiElemento("Settimanale", "2")
            menuListaFreq.aggiungiElemento("Bisettimanale", "3")
            menuListaFreq.aggiungiElemento("Mensile", "4")
            menuListaFreq.aggiungiElemento("Annuale", "5")
            AddHandler menuListaFreq.ElementoSelezionato, Sub(v As String) FrequenzaSceltaHandler(v)
            AddHandler menuListaFreq.escPremuto, AddressOf ChiudiListaFreq
            menuListaFreq.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.Frequenza, "Frequenza"))
            menuListaFreq.avvia()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in SelezionaFrequenza: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in SelezionaFrequenza: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaFreq()
        menuListaFreq.abbandona()
    End Sub

    Private Sub FrequenzaSceltaHandler(valore As String)
        Try
            Dim listaid As List(Of String) = menuListaFreq.ottieniElementi()
            Dim elemento As String = listaid(0)
            Dim freqVal As Integer
            If Not Integer.TryParse(elemento, freqVal) Then
                Exit Sub
            End If
            frequenza = CType(freqVal, CLSAgenda.Frequenza)
            SalvaAppuntamento()
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in FrequenzaSceltaHandler: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("MenuAgenda.FrequenzaSceltaHandler - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub SalvaAppuntamento()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Salvataggio appuntamento in corso...", False)

            Dim tSalva As New Thread(AddressOf SalvaAppuntamentoThread)
            tSalva.Start()
            AttendiThread(tSalva)
            Dim risultato As Boolean = _threadResultBool

            If risultato Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Salvataggio completato.")
                msg.ShowDialog()

                If menuListaFreq IsNot Nothing Then
                    Try
                        menuListaFreq.abbandona()
                    Catch ex As Exception
                    End Try
                End If
                If menuListaCat IsNot Nothing Then
                    Try
                        menuListaCat.abbandona()
                    Catch ex As Exception
                    End Try
                End If

                If Not String.IsNullOrWhiteSpace(idmodifica) Then
                    If menuListaAppuntamenti IsNot Nothing Then
                        Try
                            menuListaAppuntamenti.abbandona()
                        Catch exChiusura As Exception
                        End Try
                    End If
                    AvviaCaricamento()
                End If
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Errore durante il salvataggio dei dati.")
                msg.ShowDialog()

                If menuListaFreq IsNot Nothing Then
                    Try
                        menuListaFreq.abbandona()
                    Catch ex As Exception
                    End Try
                End If
                If menuListaCat IsNot Nothing Then
                    Try
                        menuListaCat.abbandona()
                    Catch ex As Exception
                    End Try
                End If
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in SalvaAppuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in SalvaAppuntamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub visualizzaappuntamento(id As String)
        Try
            Dim listaid As List(Of String) = menuListaAppuntamenti.ottieniElementi()
            Dim elemento As String = listaid(0)

            menuListaDettaglio = New WGMenuLista()
            menuListaDettaglio.invioNonConsentito = True
            menuListaDettaglio.multiselezioneConsentita = False
            idDettaglioCorrente = elemento
            Dim notifOk As Boolean = (config.LeggiValore("NotificheAttive") <> "Mai")

            Dim appt As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(elemento)
            If appt.HasValue Then
                Dim val As CLSAgenda.Appuntamento = appt.Value

                Dim titoloMenu As String = "Dettaglio Appuntamento"
                If val.Frequenza <> CLSAgenda.Frequenza.Nessuna Then
                    titoloMenu = "Dettaglio Ricorrenza"
                ElseIf val.Data <> val.DataFine OrElse (val.OraInizio = "00:00" AndAlso val.OraFine = "23:59") Then
                    titoloMenu = "Dettaglio Evento"
                End If
                menuListaDettaglio.impostaTitolo(titoloMenu)

                menuListaDettaglio.aggiungiElemento("Oggetto: " & val.Oggetto, elemento)
                menuListaDettaglio.aggiungiElemento("Nome: " & val.Nome, elemento)
                menuListaDettaglio.aggiungiElemento("Data inizio: " & FormattaDataAgenda(val.Data), elemento)
                menuListaDettaglio.aggiungiElemento("Data fine: " & FormattaDataAgenda(val.DataFine), elemento)
                menuListaDettaglio.aggiungiElemento("Ora inizio: " & val.OraInizio, elemento)
                menuListaDettaglio.aggiungiElemento("Ora fine: " & val.OraFine, elemento)
                menuListaDettaglio.aggiungiElemento("Categoria: " & val.Categoria, elemento)
                If val.Frequenza <> CLSAgenda.Frequenza.Nessuna Then
                    menuListaDettaglio.aggiungiElemento("Ripetizione: " & val.Frequenza.ToString(), elemento)
                End If
                menuListaDettaglio.aggiungiElemento("Stato attuale: " & val.Stato, elemento)
                menuListaDettaglio.aggiungiElemento("Note: " & val.Note, elemento)

                If notifOk Then
                    menuListaDettaglio.aggiungiElemento("Preavviso: " & val.Preavviso & " min", elemento)
                End If

                menuListaDettaglio.aggiungiOpzione("Modifica", AddressOf ModificaDaDettaglioOpz)
                menuListaDettaglio.aggiungiOpzione("Elimina", AddressOf EliminaDaDettaglioOpz)

                If val.Stato <> "passato" Then
                    menuListaDettaglio.aggiungiOpzione("Segna come Completato", AddressOf EseguiDaDettaglioOpz)
                End If

                AddHandler menuListaDettaglio.escPremuto, AddressOf ChiudiListaDettaglio
                menuListaDettaglio.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.DettaglioAppuntamento, "Dettaglio Appuntamento"))
                menuListaDettaglio.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile recuperare i dettagli dell'appuntamento.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore visualizzaappuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in visualizzaappuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaDettaglio()
        menuListaDettaglio.abbandona()
    End Sub

    Private Sub ModificaDaDettaglioOpz()
        idmodifica = idDettaglioCorrente

        menuListaDettaglio.abbandona()
        Dim appt As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(idmodifica)
        If appt.HasValue Then
            Dim val As CLSAgenda.Appuntamento = appt.Value
            data = val.Data
            categoriaSelezionata = val.Categoria
            frequenza = val.Frequenza

            campi = New wgcontrols.Campi()
            campi.Aggiungi("Modifica Oggetto", "c.oggetto", True, , val.Oggetto)
            campi.Aggiungi("Modifica Nome", "c.nome", True, , val.Nome)
            campi.AggiungiData("Modifica Data Inizio", "c.data", True, , val.Data)
            campi.AggiungiData("Modifica Data Fine", "c.datafine", True, , val.DataFine)
            campi.AggiungiOrario("Modifica Ora Inizio", "c.inizio", True, , val.OraInizio)
            campi.AggiungiOrario("Modifica Ora Fine", "c.fine", True, , val.OraFine)
            campi.Aggiungi("Modifica Note", "c.note", True, , val.Note)

            AddHandler campi.Fine, AddressOf finecampi_dati_modifica
            campi.Avvia()
        End If
    End Sub

    Private Sub EliminaDaDettaglioOpz()
        idsOperazione.Clear()
        idsOperazione.Add(idDettaglioCorrente)
        menuListaDettaglio.abbandona()
        Dim domanda As New WGDomanda()
        AddHandler domanda.rispostadomanda, AddressOf risposta
        domanda.avvia("Confermare l'eliminazione definitiva di questo appuntamento?")
    End Sub

    Private Sub EseguiDaDettaglioOpz()
        idsOperazione.Clear()
        idsOperazione.Add(idDettaglioCorrente)
        menuListaDettaglio.abbandona()
        Dim domanda As New WGDomanda()
        AddHandler domanda.rispostadomanda, AddressOf confermaImpostaEseguito
        domanda.avvia("Segnare questo appuntamento come Completato?")
    End Sub

    Private Sub modificaappuntamento(listaAttiva As WGMenuLista)
        Try
            Dim ids As List(Of String) = listaAttiva.ottieniElementi()

            If ids.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Selezionare un elemento dalla lista per la modifica.")
                msg.ShowDialog()
                Exit Sub
            End If

            If ids.Count > 1 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È possibile modificare un solo elemento alla volta.")
                msg.ShowDialog()
                Exit Sub
            End If
            idmodifica = ids(0)
            Dim appt As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(idmodifica)
            If appt.HasValue Then
                Dim val As CLSAgenda.Appuntamento = appt.Value
                data = val.Data
                categoriaSelezionata = val.Categoria
                frequenza = val.Frequenza

                campi = New wgcontrols.Campi()
                campi.Aggiungi("Modifica Oggetto", "c.oggetto", True, , val.Oggetto)
                campi.Aggiungi("Modifica Nome", "c.nome", True, , val.Nome)
                campi.AggiungiData("Modifica Data Inizio", "c.data", True, , val.Data)
                campi.AggiungiData("Modifica Data Fine", "c.datafine", True, , val.DataFine)
                campi.AggiungiOrario("Modifica Ora Inizio", "c.inizio", True, , val.OraInizio)
                campi.AggiungiOrario("Modifica Ora Fine", "c.fine", True, , val.OraFine)
                campi.Aggiungi("Modifica Note", "c.note", True, , val.Note)

                AddHandler campi.Fine, AddressOf finecampi_dati_modifica
                campi.Avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile recuperare i dettagli per la modifica.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in modificaappuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Errore in modificaappuntamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub eliminaappuntamento(listaAttiva As WGMenuLista)
        Try
            idsOperazione.Clear()
            Dim selection As List(Of String) = listaAttiva.ottieniElementi()
            If selection.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Selezionare almeno un elemento da eliminare.")
                msg.ShowDialog()
                Exit Sub
            End If
            idsOperazione.AddRange(selection)
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf risposta
            Dim testoDomanda As String = If(idsOperazione.Count > 1, $"Confermi l'eliminazione definitiva di {idsOperazione.Count} appuntamenti selezionati?", "Confermi l'eliminazione definitiva dell'appuntamento selezionato?")
            domanda.avvia(testoDomanda)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore eliminaappuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in eliminaappuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub risposta(rispostadom As DialogResult)
        Try
            If rispostadom = DialogResult.Yes Then
                Dim deletedCount As Integer = 0
                For Each id As String In idsOperazione
                    If classeagenda.EliminaAppuntamento(id) Then
                        deletedCount += 1
                    End If
                Next
                If deletedCount > 0 Then
                    AvviaSincronizzazioneAgendaDopoModifica()
                    If menuListaAppuntamenti IsNot Nothing Then
                        Try
                            menuListaAppuntamenti.abbandona()
                        Catch exChiusura As Exception
                        End Try
                    End If
                    Dim msg As New WGMessaggi()
                    msg.Messaggio($"{deletedCount} elementi eliminati.")
                    msg.ShowDialog()
                    AvviaCaricamento()
                Else
                    Dim msg As New WGMessaggi()
                    msg.Messaggio($"Nessun elemento eliminato.{Environment.NewLine}Si è verificato un errore.")
                    msg.ShowDialog()
                End If
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore in risposta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in risposta - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub rispostaappuntamento(rispostadom As DialogResult)
        Try
            If rispostadom = DialogResult.Yes Then
                nuovoappuntamento()
            End If
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore rispostaappuntamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in rispostaappuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub impostaeseguito(listaAttiva As WGMenuLista)
        Try
            idsOperazione.Clear()
            Dim selection As List(Of String) = listaAttiva.ottieniElementi()
            If selection.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Selezionare almeno un elemento.")
                msg.ShowDialog()
                Exit Sub
            End If
            idsOperazione.AddRange(selection)

            Dim ciSonoFuturi As Boolean = False
            For Each id As String In idsOperazione
                Dim appt As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(id)
                If appt.HasValue Then
                    Dim val As CLSAgenda.Appuntamento = appt.Value
                    Dim dtAppt As DateTime
                    If DateTime.TryParse(val.Data & " " & val.OraInizio, dtAppt) Then
                        If dtAppt > DateTime.Now Then
                            ciSonoFuturi = True
                            Exit For
                        End If
                    End If
                End If
            Next

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf confermaImpostaEseguito

            Dim testo As String
            If ciSonoFuturi Then
                testo = "ATTENZIONE: La selezione corrente contiene appuntamenti FUTURI." & Environment.NewLine &
                        "Sì = Completa TUTTI i " & idsOperazione.Count & " selezionati (inclusi i futuri)." & Environment.NewLine &
                        "No = Completa SOLO quelli passati/scaduti."
            Else
                Dim countMsg As String = If(idsOperazione.Count > 1, $"{idsOperazione.Count} appuntamenti", "l'appuntamento selezionato")
                testo = $"Confermi di voler segnare come completati {countMsg}?"
            End If

            domanda.avvia(testo)
        Catch ex As Exception
            _logger.ScriviLog("Errore impostaeseguito - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub confermaImpostaEseguito(risp As DialogResult)
        Try
            Dim idsDaEseguire As New List(Of String)

            Dim ciSonoFuturi As Boolean = False
            Dim idsPassati As New List(Of String)
            Dim idsFuturi As New List(Of String)

            For Each id As String In idsOperazione
                Dim appt As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(id)
                If appt.HasValue Then
                    Dim val As CLSAgenda.Appuntamento = appt.Value
                    Dim dtAppt As DateTime
                    If DateTime.TryParse(val.Data & " " & val.OraInizio, dtAppt) Then
                        If dtAppt > DateTime.Now Then
                            idsFuturi.Add(id)
                            ciSonoFuturi = True
                        Else
                            idsPassati.Add(id)
                        End If
                    End If
                End If
            Next

            If ciSonoFuturi Then
                If risp = DialogResult.Yes Then
                    idsDaEseguire.AddRange(idsOperazione)
                ElseIf risp = DialogResult.No Then
                    idsDaEseguire.AddRange(idsPassati)
                End If
            Else
                If risp = DialogResult.Yes Then
                    idsDaEseguire.AddRange(idsOperazione)
                ElseIf risp = DialogResult.No Then
                End If
            End If

            If idsDaEseguire.Count > 0 Then
                If classeagenda.SegnaPassatiMassivi(idsDaEseguire) Then
                    If menuListaAppuntamenti IsNot Nothing Then
                        Try
                            menuListaAppuntamenti.abbandona()
                        Catch exChiusura As Exception
                        End Try
                    End If
                    Dim msg As New WGMessaggi()
                    msg.Messaggio($"{idsDaEseguire.Count} elementi aggiornati correttamente.")
                    msg.ShowDialog()
                    AvviaCaricamento()
                Else
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Errore durante l'aggiornamento.")
                    msg.ShowDialog()
                End If
            Else
                If ciSonoFuturi AndAlso risp = DialogResult.No Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Nessun appuntamento idoneo (passato) trovato nella selezione.")
                    msg.ShowDialog()
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("Errore confermaImpostaEseguito - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub visualizzapercategoria()
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Caricamento categorie in corso...", False)

            Dim tCats As New Thread(AddressOf OttieniCategorieThread)
            tCats.Start()
            AttendiThread(tCats)
            Dim cats As List(Of String) = _threadResultCats

            If cats.Count > 0 Then
                menuListaCat = New WGMenuLista()
                menuListaCat.impostaTitolo("Seleziona Filtro Categoria")
                menuListaCat.multiselezioneConsentita = False

                Dim elementi As New List(Of ElementoLista)

                For Each c As String In cats
                    elementi.Add(New ElementoLista(c, c))
                Next
                menuListaCat.CaricaListaDinamica(elementi)

                If attesa IsNot Nothing Then
                    attesa.Fine()
                    attesa = Nothing
                End If

                AddHandler menuListaCat.ElementoSelezionato, Sub(v As String) MostraCategoriaHandler(v)
                AddHandler menuListaCat.escPremuto, AddressOf ChiudiMenuListaCat
                menuListaCat.aggiungiOpzione("Guida", Sub() MostraGuidaMenu(GuidaAgenda.SelezioneCategoriaFiltro, "Filtro Categoria"))
                menuListaCat.avvia()
            Else
                If attesa IsNot Nothing Then
                    attesa.Fine()
                    attesa = Nothing
                End If

                Dim msg As New WGMessaggi()
                msg.Messaggio("Non sono presenti categorie configurate nel sistema.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("Errore visualizzapercategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraCategoriaHandler(cat As String)
        Dim listaid As List(Of String) = menuListaCat.ottieniElementi()
        Dim elemento As String = listaid(0)
        mostracategoria(elemento, menuListaCat)
    End Sub

    Private Sub mostracategoria(cat As String, lista As WGMenuLista)
        Try
            lista.abbandona()
            _modalitaCorrente = ModalitaVisualizzazione.Categoria
            _filtroCategoria = cat
            AvviaCaricamento()
        Catch ex As Exception
            _logger.ScriviLog("Errore mostracategoria - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

#Region "Comandi vocali"

    Private Class RichiestaAppuntamentoVocale
        Public Property Descrizione As String = ""
        Public Property Oggetto As String = "Appuntamento"
        Public Property Inizio As DateTime = DateTime.MinValue
        Public Property Fine As DateTime = DateTime.MinValue
        Public Property TuttoIlGiorno As Boolean = False
        Public Property Note As String = ""
        Public Property PreavvisoMinuti As Integer = -1
        Public Property Categoria As String = ""
        Public Property Riuscito As Boolean = False
    End Class

    Private _richiestaAppuntamentoVocale As RichiestaAppuntamentoVocale
    Private Shared ReadOnly _culturaAgendaVocale As CultureInfo = CultureInfo.GetCultureInfo("it-IT")

    Public Sub ComandoNuovoAppuntamento(descrizione As String, inizio As DateTime, Optional fine As DateTime = Nothing, Optional tuttoIlGiorno As Boolean = False, Optional oggettoAppuntamento As String = "Appuntamento", Optional noteAppuntamento As String = "", Optional preavvisoMinuti As Integer = -1, Optional categoria As String = "")
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            If String.IsNullOrWhiteSpace(descrizione) Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Indica la descrizione dell'appuntamento.")
                avviso.ShowDialog()
                Exit Sub
            End If

            If inizio = DateTime.MinValue Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Indica la data dell'appuntamento.")
                avviso.ShowDialog()
                Exit Sub
            End If

            classeagenda = CLSAgenda.GetInstance()

            Dim richiesta As New RichiestaAppuntamentoVocale()
            richiesta.Descrizione = descrizione.Trim()
            richiesta.Oggetto = If(String.IsNullOrWhiteSpace(oggettoAppuntamento), "Appuntamento", oggettoAppuntamento.Trim())
            richiesta.Inizio = inizio
            richiesta.Fine = fine
            richiesta.TuttoIlGiorno = tuttoIlGiorno
            richiesta.Note = If(noteAppuntamento, "").Trim()
            richiesta.PreavvisoMinuti = preavvisoMinuti
            richiesta.Categoria = If(categoria, "").Trim()
            _richiestaAppuntamentoVocale = richiesta

            CaricaAgendaPredefinitaCorrente()

            Dim agendaCorrente As String = classeagenda.AgendaCorrente
            If Not String.IsNullOrWhiteSpace(agendaCorrente) AndAlso
               classeagenda.AgendaProtetta(agendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(agendaCorrente) Then
                _operazionePendenteAgenda = AddressOf SalvaAppuntamentoVocale
                ChiediPasswordAgenda(agendaCorrente, ScopoPasswordAgenda.Apertura,
                    "Password dell'agenda " & classeagenda.NomeAgenda(agendaCorrente))
                Exit Sub
            End If

            SalvaAppuntamentoVocale()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ComandoNuovoAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Non è stato possibile aggiungere l'appuntamento.")
            msgBox.ShowDialog()
        End Try
    End Sub

    Private Sub SalvaAppuntamentoVocale()
        Try
            If _richiestaAppuntamentoVocale Is Nothing Then Exit Sub

            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Salvataggio appuntamento in corso...", False)

            Dim lavoro As New Thread(AddressOf LavoroSalvaAppuntamentoVocale)
            lavoro.IsBackground = True
            lavoro.Start(_richiestaAppuntamentoVocale)
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("MenuAgenda.SalvaAppuntamentoVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LavoroSalvaAppuntamentoVocale(stato As Object)
        Dim richiesta As RichiestaAppuntamentoVocale = Nothing
        Try
            richiesta = TryCast(stato, RichiestaAppuntamentoVocale)
            If richiesta IsNot Nothing Then
                richiesta.Riuscito = AggiungiAppuntamentoSenzaInterfaccia(richiesta.Descrizione, richiesta.Inizio, richiesta.Fine, richiesta.TuttoIlGiorno, richiesta.Oggetto, richiesta.Note, richiesta.PreavvisoMinuti, richiesta.Categoria)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.LavoroSalvaAppuntamentoVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
            If richiesta IsNot Nothing Then richiesta.Riuscito = False
        Finally
            PubblicaSuInterfacciaAgenda(AddressOf CompletaSalvaAppuntamentoVocale, richiesta)
        End Try
    End Sub

    Private Sub PubblicaSuInterfacciaAgenda(callback As SendOrPostCallback, stato As Object)
        Try
            If _uiContext IsNot Nothing Then
                _uiContext.Post(callback, stato)
                Return
            End If

            If Application.OpenForms.Count > 0 Then
                Dim finestra As Form = Application.OpenForms(0)
                If finestra IsNot Nothing AndAlso finestra.IsHandleCreated AndAlso Not finestra.IsDisposed Then
                    finestra.BeginInvoke(callback, New Object() {stato})
                    Return
                End If
            End If

            callback(stato)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.PubblicaSuInterfacciaAgenda - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletaSalvaAppuntamentoVocale(stato As Object)
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If

            Dim richiesta As RichiestaAppuntamentoVocale = TryCast(stato, RichiestaAppuntamentoVocale)
            Dim msg As New WGMessaggi()
            If richiesta IsNot Nothing AndAlso richiesta.Riuscito Then
                msg.Messaggio(TestoConfermaAppuntamento(richiesta.Descrizione, richiesta.Inizio, richiesta.TuttoIlGiorno))
            Else
                msg.Messaggio("Non è stato possibile salvare l'appuntamento.")
            End If
            msg.ShowDialog()
            _richiestaAppuntamentoVocale = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CompletaSalvaAppuntamentoVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function AggiungiAppuntamentoSenzaInterfaccia(descrizione As String, inizio As DateTime, Optional fine As DateTime = Nothing, Optional tuttoIlGiorno As Boolean = False, Optional oggettoAppuntamento As String = "Appuntamento", Optional noteAppuntamento As String = "", Optional preavvisoMinuti As Integer = -1, Optional categoria As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(descrizione) Then Return False
            If inizio = DateTime.MinValue Then Return False

            If classeagenda Is Nothing Then classeagenda = CLSAgenda.GetInstance()

            Dim agendaCorrente As String = classeagenda.AgendaCorrente
            If Not String.IsNullOrWhiteSpace(agendaCorrente) AndAlso
               classeagenda.AgendaProtetta(agendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(agendaCorrente) Then
                Return False
            End If

            Dim fineEffettiva As DateTime = fine
            Dim oraInizioTesto As String
            Dim oraFineTesto As String

            If tuttoIlGiorno Then
                oraInizioTesto = "00:00"
                oraFineTesto = "23:59"
                If fineEffettiva = DateTime.MinValue OrElse fineEffettiva.Date < inizio.Date Then fineEffettiva = inizio.Date
            Else
                If fineEffettiva = DateTime.MinValue OrElse fineEffettiva <= inizio Then fineEffettiva = inizio.AddHours(1)
                If fineEffettiva.Date <> inizio.Date AndAlso fineEffettiva.Date = inizio.Date.AddDays(1) AndAlso fineEffettiva.TimeOfDay = TimeSpan.Zero Then
                    fineEffettiva = inizio.Date.AddHours(23).AddMinutes(59)
                End If
                oraInizioTesto = inizio.ToString("HH:mm", CultureInfo.InvariantCulture)
                oraFineTesto = fineEffettiva.ToString("HH:mm", CultureInfo.InvariantCulture)
            End If

            Dim dataTesto As String = inizio.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            Dim dataFineTesto As String = fineEffettiva.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)

            Dim preavvisoEffettivo As Integer = preavvisoMinuti
            If preavvisoEffettivo < 0 Then preavvisoEffettivo = If(tuttoIlGiorno, 1440, 60)

            Dim categoriaEffettiva As String = CategoriaEsistente(categoria)
            Dim oggettoEffettivo As String = If(String.IsNullOrWhiteSpace(oggettoAppuntamento), "Appuntamento", oggettoAppuntamento.Trim())

            Dim riuscito As Boolean = classeagenda.AggiungiAppuntamento(oggettoEffettivo, descrizione.Trim(), oraInizioTesto, oraFineTesto, If(noteAppuntamento, "").Trim(), preavvisoEffettivo, CType(0, CLSAgenda.Frequenza), dataTesto, categoriaEffettiva, dataFineTesto)
            If riuscito Then AvviaSincronizzazioneAgendaDopoModifica()
            Return riuscito
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.AggiungiAppuntamentoSenzaInterfaccia - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CategoriaEsistente(categoria As String) As String
        Try
            If String.IsNullOrWhiteSpace(categoria) Then Return ""
            Dim categorie As List(Of String) = classeagenda.OttieniElencoCategorie()
            If categorie Is Nothing Then Return ""
            For Each voce As String In categorie
                If String.Equals(If(voce, "").Trim(), categoria.Trim(), StringComparison.CurrentCultureIgnoreCase) Then Return voce
            Next
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CategoriaEsistente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Function TestoConfermaAppuntamento(descrizione As String, inizio As DateTime, tuttoIlGiorno As Boolean) As String
        Try
            Dim quando As String
            Dim differenza As Integer = CInt((inizio.Date - DateTime.Today).TotalDays)
            If differenza = 0 Then
                quando = "oggi"
            ElseIf differenza = 1 Then
                quando = "domani"
            ElseIf differenza = 2 Then
                quando = "dopodomani"
            ElseIf inizio.Year = DateTime.Today.Year Then
                quando = inizio.ToString("dddd d MMMM", _culturaAgendaVocale)
            Else
                quando = inizio.ToString("dddd d MMMM yyyy", _culturaAgendaVocale)
            End If

            If tuttoIlGiorno Then
                Return "Appuntamento " & descrizione & " aggiunto per " & quando & ", per tutta la giornata."
            End If
            Return "Appuntamento " & descrizione & " aggiunto per " & quando & " alle " & inizio.ToString("HH:mm", CultureInfo.InvariantCulture) & "."
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.TestoConfermaAppuntamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Appuntamento aggiunto."
        End Try
    End Function

    Private Class RichiestaProgrammaVocale
        Public Property Data As DateTime = DateTime.Today
        Public Property Risultato As String = ""
    End Class

    Private _richiestaProgrammaVocale As RichiestaProgrammaVocale

    Public Sub ComandoAppuntamentiGiorno(Optional data As DateTime = Nothing)
        Try
            If _uiContext Is Nothing Then
                _uiContext = SynchronizationContext.Current
            End If

            classeagenda = CLSAgenda.GetInstance()

            Dim richiesta As New RichiestaProgrammaVocale()
            richiesta.Data = If(data = DateTime.MinValue, DateTime.Today, data.Date)
            _richiestaProgrammaVocale = richiesta

            CaricaAgendaPredefinitaCorrente()

            Dim agendaCorrente As String = classeagenda.AgendaCorrente
            If Not String.IsNullOrWhiteSpace(agendaCorrente) AndAlso
               classeagenda.AgendaProtetta(agendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(agendaCorrente) Then
                _operazionePendenteAgenda = AddressOf LeggiProgrammaVocale
                ChiediPasswordAgenda(agendaCorrente, ScopoPasswordAgenda.Apertura,
                    "Password dell'agenda " & classeagenda.NomeAgenda(agendaCorrente))
                Exit Sub
            End If

            LeggiProgrammaVocale()
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ComandoAppuntamentiGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Non è stato possibile leggere gli appuntamenti.")
            msgBox.ShowDialog()
        End Try
    End Sub

    Private Sub LeggiProgrammaVocale()
        Try
            If _richiestaProgrammaVocale Is Nothing Then Exit Sub

            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Lettura degli appuntamenti in corso...", False)

            Dim lavoro As New Thread(AddressOf LavoroProgrammaVocale)
            lavoro.IsBackground = True
            lavoro.Start(_richiestaProgrammaVocale)
        Catch ex As Exception
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If
            _logger.ScriviLog("MenuAgenda.LeggiProgrammaVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LavoroProgrammaVocale(stato As Object)
        Dim richiesta As RichiestaProgrammaVocale = Nothing
        Try
            richiesta = TryCast(stato, RichiestaProgrammaVocale)
            If richiesta IsNot Nothing Then richiesta.Risultato = TestoAppuntamentiGiorno(richiesta.Data)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.LavoroProgrammaVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            PubblicaSuInterfacciaAgenda(AddressOf CompletaProgrammaVocale, richiesta)
        End Try
    End Sub

    Private Sub CompletaProgrammaVocale(stato As Object)
        Try
            If attesa IsNot Nothing Then
                attesa.Fine()
                attesa = Nothing
            End If

            Dim richiesta As RichiestaProgrammaVocale = TryCast(stato, RichiestaProgrammaVocale)
            Dim testo As String = If(richiesta IsNot Nothing, richiesta.Risultato, "")
            If String.IsNullOrWhiteSpace(testo) Then testo = "Non è stato possibile leggere gli appuntamenti."

            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
            _richiestaProgrammaVocale = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.CompletaProgrammaVocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Class VoceProgrammaVocale
        Public Property Ora As String = ""
        Public Property TuttoIlGiorno As Boolean = False
        Public Property Descrizione As String = ""
    End Class

    Private Function ConfrontaVociProgramma(a As VoceProgrammaVocale, b As VoceProgrammaVocale) As Integer
        Try
            If a.TuttoIlGiorno AndAlso Not b.TuttoIlGiorno Then Return -1
            If b.TuttoIlGiorno AndAlso Not a.TuttoIlGiorno Then Return 1
            Return String.CompareOrdinal(a.Ora, b.Ora)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.ConfrontaVociProgramma - " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function TestoAppuntamentiGiorno(data As DateTime) As String
        Try
            If classeagenda Is Nothing Then classeagenda = CLSAgenda.GetInstance()

            Dim agendaCorrente As String = classeagenda.AgendaCorrente
            If Not String.IsNullOrWhiteSpace(agendaCorrente) AndAlso
               classeagenda.AgendaProtetta(agendaCorrente) AndAlso Not classeagenda.AgendaSbloccata(agendaCorrente) Then
                Return "L'agenda è protetta da password: aprirla prima dal menu dell'agenda."
            End If

            Dim quando As String = DescriviGiornoVocale(data)
            Dim elenco As List(Of CLSAgenda.AppuntamentoLista) = classeagenda.OttieniProgrammaGiornalieroPaginato(data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 0, 1000, CLSAgenda.TipoOrdinamento.DataCrescente)

            Dim voci As New List(Of VoceProgrammaVocale)
            If elenco IsNot Nothing Then
                For Each elemento As CLSAgenda.AppuntamentoLista In elenco
                    Try
                        Dim voce As New VoceProgrammaVocale()
                        voce.Descrizione = If(elemento.Oggetto, "").Trim()
                        Dim dettaglio As CLSAgenda.Appuntamento? = classeagenda.OttieniDettagliAppuntamento(elemento.Id.ToString())
                        If dettaglio.HasValue Then
                            Dim valore As CLSAgenda.Appuntamento = dettaglio.Value
                            Dim nome As String = If(valore.Nome, "").Trim()
                            Dim oggetto As String = If(valore.Oggetto, "").Trim()
                            If nome = "" Then
                                voce.Descrizione = oggetto
                            ElseIf oggetto = "" OrElse String.Equals(oggetto, nome, StringComparison.CurrentCultureIgnoreCase) OrElse String.Equals(oggetto, "Appuntamento", StringComparison.CurrentCultureIgnoreCase) Then
                                voce.Descrizione = nome
                            Else
                                voce.Descrizione = oggetto & ", " & nome
                            End If
                            voce.Ora = If(valore.OraInizio, "").Trim()
                            voce.TuttoIlGiorno = voce.Ora = "" OrElse (voce.Ora = "00:00" AndAlso If(valore.OraFine, "").Trim() = "23:59")
                        Else
                            voce.TuttoIlGiorno = True
                        End If
                        If voce.Descrizione = "" Then voce.Descrizione = "appuntamento senza descrizione"
                        voci.Add(voce)
                    Catch exElemento As Exception
                        _logger.ScriviLog("MenuAgenda.TestoAppuntamentiGiorno (elemento) - " & exElemento.Message)
                    End Try
                Next
            End If

            If voci.Count = 0 Then Return MaiuscolaVocale(quando) & " non ci sono appuntamenti."

            voci.Sort(AddressOf ConfrontaVociProgramma)

            Dim frasi As New List(Of String)
            If voci.Count = 1 Then
                frasi.Add(MaiuscolaVocale(quando) & " c'è un appuntamento.")
            Else
                frasi.Add(MaiuscolaVocale(quando) & " ci sono " & voci.Count.ToString() & " appuntamenti.")
            End If

            For Each voce As VoceProgrammaVocale In voci
                If voce.TuttoIlGiorno Then
                    frasi.Add("Per tutta la giornata: " & voce.Descrizione & ".")
                Else
                    frasi.Add("Alle " & voce.Ora & ": " & voce.Descrizione & ".")
                End If
            Next

            Return String.Join(Environment.NewLine, frasi)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.TestoAppuntamentiGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile leggere gli appuntamenti."
        End Try
    End Function

    Private Function DescriviGiornoVocale(data As DateTime) As String
        Try
            Dim differenza As Integer = CInt((data.Date - DateTime.Today).TotalDays)
            If differenza = 0 Then Return "oggi"
            If differenza = 1 Then Return "domani"
            If differenza = 2 Then Return "dopodomani"
            If differenza = -1 Then Return "ieri"
            If data.Year = DateTime.Today.Year Then Return data.ToString("dddd d MMMM", _culturaAgendaVocale)
            Return data.ToString("dddd d MMMM yyyy", _culturaAgendaVocale)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.DescriviGiornoVocale - " & ex.Message)
            Return "quel giorno"
        End Try
    End Function

    Private Function MaiuscolaVocale(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return testo.Substring(0, 1).ToUpper(_culturaAgendaVocale) & testo.Substring(1)
        Catch ex As Exception
            _logger.ScriviLog("MenuAgenda.MaiuscolaVocale - " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

#End Region

End Class