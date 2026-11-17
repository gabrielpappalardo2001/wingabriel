Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGContatti
Imports WGDati
Imports WGMail
Partial Public Class MenuRubrica

    Private ReadOnly log As New wgcontrols.Logger()
    Private ReadOnly _contatti As CLSContatti
    Private ReadOnly _contattiEsterni As Boolean = False
    Private ReadOnly _posta As CLSMail
    Private ReadOnly _postaEsterna As Boolean = False
    Private _marshal As System.Windows.Forms.Control

    Private _menuPrincipale As WGMenu
    Private _menuCategorie As WGMenu
    Private _menuConferma As WGMenu
    Private _menuRicerca As WGMenu
    Private _listaContatti As WGMenuLista
    Private _listaCategorie As WGMenuLista
    Private _listaAccount As WGMenuLista
    Private _campiContatto As Campi
    Private _campiCategoria As Campi
    Private _campiRicerca As Campi
    Private _listaDettaglio As WGMenuLista
    Private _menuSincronizzazione As WGMenu
    Private _menuIntervallo As WGMenu
    Private _menuAccountComportamento As WGMenu
    Private _listaAccountPredefinito As WGMenuLista
    Private _listaRicercheRecenti As WGMenuLista
    Private _listaRubriche As WGMenuLista
    Private _campiRubrica As Campi
    Private _attesa As WGProcessoInCorso

    ' Rubrica "di lavoro" scelta dall'utente (voce Rubriche, o su richiesta), valida per tutta
    ' la sessione e salvata in configurazione cosi' da restare la stessa alla riapertura.
    ' Se protetta da password va sbloccata prima di poter operare.
    Private _rubricaPredefinitaCorrente As String = ""
    Private _operazionePendenteRubrica As Action = Nothing

    Private _azioneMenuEseguita As Boolean = False
    Private _chiuso As Boolean = False
    Private _filtroCategoria As String = ""
    Private _soloPreferiti As Boolean = False
    Private _filtroRubrica As String = ""
    Private _chiaveRubricaDaEliminare As String = ""
    Private _chiaveRubricaDaRinominare As String = ""
    Private _chiaveAccountRubricaCorrente As String = ""
    Private _chiaveContattoCorrente As String = ""
    Private _chiaveCategoriaCorrente As String = ""
    Private _chiaveAccountSinc As String = ""
    Private _elencoTabella As DataTable
    Private _rigaCorrente As DataRow
    Private _numeroSincronizzati As Integer = 0
    Private ReadOnly _chiaviDaEliminare As New List(Of String)
    Private _statoPreferitoMostratoInLista As Boolean = False
    Private _statoPreferitoMostratoInDettaglio As Boolean = False
    Private _preferitoInModifica As Boolean = False

    ' ============================================================================================
    ' MONITORAGGIO AUTOMATICO DELLE MODIFICHE
    ' ============================================================================================
    ' Mentre un elenco (contatti, dettaglio di un contatto o rubriche) e' visualizzato, questo
    ' timer controlla periodicamente se i dati sottostanti sono cambiati (es. una sincronizzazione
    ' in sottofondo ha aggiunto o modificato dei contatti) e, se serve, ricarica automaticamente
    ' solo gli elementi della lista gia' aperta con "pulisci()" + un nuovo riempimento, senza mai
    ' chiudere e riaprire la lista stessa.
    Private WithEvents _timerMonitoraggio As New System.Windows.Forms.Timer()
    Private _firmaElencoContattiCorrente As String = ""
    Private _firmaListaRubricheCorrente As String = ""
    Private _firmaDettaglioContattoCorrente As String = ""
    Private Const IntervalloMonitoraggioMs As Integer = 4000

    Public Event Uscita As EventHandler

    Public Sub New()
        Try
            _contatti = New CLSContatti()
            _contattiEsterni = False
            _posta = New CLSMail()
            _postaEsterna = False
            PreparaMarshal()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.New: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub New(contatti As CLSContatti, Optional posta As CLSMail = Nothing)
        Try
            If contatti Is Nothing Then
                _contatti = New CLSContatti()
                _contattiEsterni = False
            Else
                _contatti = contatti
                _contattiEsterni = True
            End If
            If posta Is Nothing Then
                _posta = New CLSMail()
                _postaEsterna = False
            Else
                _posta = posta
                _postaEsterna = True
            End If
            PreparaMarshal()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.New (istanza esterna): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PreparaMarshal()
        Try
            _marshal = New System.Windows.Forms.Control()
            Dim riferimento As IntPtr = _marshal.Handle
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.PreparaMarshal: " & ex.Message)
        End Try
        Try
            _timerMonitoraggio.Interval = IntervalloMonitoraggioMs
            _timerMonitoraggio.Start()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.PreparaMarshal timer: " & ex.Message)
        End Try
    End Sub

    ' Ferma il monitoraggio automatico. Da richiamare quando l'istanza di MenuRubrica non serve
    ' piu' (es. alla chiusura della form che la ospita), per non lasciare un timer attivo a
    ' interrogare inutilmente il database in sottofondo.
    Public Sub FermaMonitoraggioAutomatico()
        Try
            _timerMonitoraggio.Stop()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.FermaMonitoraggioAutomatico: " & ex.Message)
        End Try
    End Sub

    ' Calcola una "firma" del contenuto di una tabella: basta che una riga, una colonna o il
    ' numero di righe cambi perche' la firma cambi. Usata per capire, senza confronti pesanti,
    ' se un elenco visualizzato e' rimasto sincronizzato con il database oppure va ricaricato.
    Private Function FirmaTabella(tabella As DataTable) As String
        Try
            If tabella Is Nothing Then Return ""
            Dim generatore As New StringBuilder()
            generatore.Append(tabella.Rows.Count).Append("|")
            For Each riga As DataRow In tabella.Rows
                Try
                    For Each colonna As DataColumn In tabella.Columns
                        generatore.Append(Convert.ToString(riga(colonna))).Append(Chr(31))
                    Next
                    generatore.Append(Chr(30))
                Catch exRiga As Exception
                    log.ScriviLog("MenuRubrica.FirmaTabella riga: " & exRiga.Message)
                End Try
            Next
            Return generatore.ToString().GetHashCode().ToString()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.FirmaTabella: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Tick periodico: per ciascun elenco attualmente aperto, ricalcola i dati e, se sono diversi
    ' da quelli mostrati, ricarica solo gli elementi (mai l'intera lista) tramite pulisci().
    Private Sub TimerMonitoraggio_Tick(sender As Object, e As EventArgs) Handles _timerMonitoraggio.Tick
        Try
            If _chiuso Then Return
            If _marshal Is Nothing OrElse _marshal.IsDisposed Then Return

            If _listaContatti IsNot Nothing Then
                Try
                    VerificaAggiornamentoElencoContatti()
                Catch exElenco As Exception
                    log.ScriviLog("MenuRubrica.TimerMonitoraggio_Tick elenco contatti: " & exElenco.Message)
                End Try
            End If

            If _listaDettaglio IsNot Nothing Then
                Try
                    VerificaAggiornamentoDettaglioContatto()
                Catch exDettaglio As Exception
                    log.ScriviLog("MenuRubrica.TimerMonitoraggio_Tick dettaglio contatto: " & exDettaglio.Message)
                End Try
            End If

            If _listaRubriche IsNot Nothing Then
                Try
                    VerificaAggiornamentoListaRubriche()
                Catch exRubriche As Exception
                    log.ScriviLog("MenuRubrica.TimerMonitoraggio_Tick lista rubriche: " & exRubriche.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.TimerMonitoraggio_Tick: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSuInterfaccia(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _marshal Is Nothing OrElse _marshal.IsDisposed OrElse Not _marshal.IsHandleCreated Then
                azione.Invoke()
                Return
            End If
            If _marshal.InvokeRequired Then
                _marshal.BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As ObjectDisposedException
        Catch ex As InvalidOperationException
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.EseguiSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaLavoro(lavoro As Action)
        Try
            If lavoro Is Nothing Then Return
            Task.Run(lavoro)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AvviaLavoro: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraAttesa(testo As String)
        Try
            ChiudiAttesa()
            _attesa = New WGProcessoInCorso()
            _attesa.Avvia(testo)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiAttesa()
        Try
            If _attesa IsNot Nothing Then
                _attesa.Fine()
                _attesa = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiAttesa: " & ex.Message)
            _attesa = Nothing
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggi As New WGMessaggi()
            messaggi.Messaggio(testo)
            messaggi.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _chiuso = False
            AllineaAccountPredefinito()
            CaricaRubricaPredefinitaCorrente()
            If Not String.IsNullOrWhiteSpace(_rubricaPredefinitaCorrente) AndAlso
               _contatti.RubricaProtetta(_rubricaPredefinitaCorrente) AndAlso
               Not _contatti.RubricaSbloccata(_rubricaPredefinitaCorrente) Then
                _operazionePendenteRubrica = AddressOf MostraMenuPrincipale
                ChiediPassword(_rubricaPredefinitaCorrente, ScopoPassword.Apertura,
                    "Password della rubrica " & _contatti.NomeRubrica(_rubricaPredefinitaCorrente))
                Return
            End If
            MostraMenuPrincipale()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.Avvia: " & ex.Message)
            MostraMessaggio("Non è stato possibile aprire la rubrica: " & ex.Message)
        End Try
    End Sub

    ' Senza GUI: per timer / controllo periodico esterno.
    Public Function CiSonoContattiDaSincronizzare(Optional chiaveAccount As String = "") As Boolean
        Try
            Return _contatti.CiSonoContattiDaSincronizzare(chiaveAccount)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CiSonoContattiDaSincronizzare: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function SincronizzaSenzaInterfaccia(Optional chiaveAccount As String = "") As Integer
        Try
            Return _posta.SincronizzaRubricaCompleta(_contatti, chiaveAccount)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.SincronizzaSenzaInterfaccia: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub MostraMenuPrincipale()
        Try
            If _chiuso Then Return
            _azioneMenuEseguita = False
            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Nuovo contatto", AddressOf VoceNuovo)
            _menuPrincipale.aggiungi("Elenco contatti", AddressOf VoceElenco)
            _menuPrincipale.aggiungi("Cerca un contatto", AddressOf VoceCerca)

            _menuPrincipale.aggiungi("Preferiti", AddressOf VocePreferiti)
            _menuPrincipale.aggiungi("Categorie", AddressOf VoceCategorie)
            ' Le rubriche esistono anche senza account, quindi la voce c'e' sempre.
            _menuPrincipale.aggiungi("Rubriche", AddressOf VoceRubriche)
            _menuPrincipale.aggiungi("Compleanni in agenda", AddressOf VoceCompleanniAgenda)
            If _posta IsNot Nothing AndAlso _posta.EsisteAccountConRubrica() Then
                _menuPrincipale.aggiungi("Sincronizza la rubrica", AddressOf VoceSincronizza)
                _menuPrincipale.aggiungi("Impostazioni", AddressOf VoceMenuSincronizzazione)
            End If

            _menuPrincipale.avvia("Rubrica")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuPrincipale: " & ex.Message)
        End Try
    End Sub

    ' Se è attiva, ogni compleanno inserito in rubrica diventa un appuntamento annuale in agenda (vedi WGCompleanniAgenda).
    Private Sub VoceCompleanniAgenda(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            Dim menu As New WGMenu()
            menu.aggiungi("Disattivati", Sub() ScegliCompleanniAgenda(menu, WGCompleanniAgenda.Spento))
            menu.aggiungi("Avviso lo stesso giorno", Sub() ScegliCompleanniAgenda(menu, WGCompleanniAgenda.AvvisoGiornoStesso))
            menu.aggiungi("Avviso il giorno prima", Sub() ScegliCompleanniAgenda(menu, WGCompleanniAgenda.AvvisoGiornoPrima))
            Dim attuale As String = WGCompleanniAgenda.Modalita()
            menu.posiziona(If(attuale = WGCompleanniAgenda.AvvisoGiornoStesso, 1, If(attuale = WGCompleanniAgenda.AvvisoGiornoPrima, 2, 0)))
            menu.avvia("Compleanni in agenda", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCompleanniAgenda: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliCompleanniAgenda(menu As WGMenu, modalita As String)
        Try
            WGCompleanniAgenda.Imposta(modalita)
            Try
                menu.abbandona()
            Catch exChiusura As Exception
                log.ScriviLog("MenuRubrica.ScegliCompleanniAgenda chiusura: " & exChiusura.Message)
            End Try
            Select Case modalita
                Case WGCompleanniAgenda.AvvisoGiornoStesso
                    MostraMessaggio("I compleanni della rubrica andranno in agenda, con l'avviso il giorno del compleanno.")
                Case WGCompleanniAgenda.AvvisoGiornoPrima
                    MostraMessaggio("I compleanni della rubrica andranno in agenda, con l'avviso il giorno prima.")
                Case Else
                    MostraMessaggio("I compleanni non andranno più in agenda. Quelli già messi dalla rubrica sono stati tolti.")
            End Select
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ScegliCompleanniAgenda: " & ex.Message)
        End Try
    End Sub

    ' La data come si scrive nella maschera del contatto: 15/03/1980 oppure 15/03.
    Private Shared Function CompleannoPerModifica(canonico As String) As String
        Dim giorno, mese, anno As Integer
        If Not CLSContatti.AnalizzaCompleanno(canonico, giorno, mese, anno) Then Return ""
        Dim testo As String = giorno.ToString("00") & "/" & mese.ToString("00")
        If anno > 0 Then testo &= "/" & anno.ToString("0000")
        Return testo
    End Function

    Private Sub VoceElenco(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            _filtroCategoria = ""
            _soloPreferiti = False
            UsaRubricaEPoi(AddressOf MostraElencoContatti)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCerca(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraMenuRicerca()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCerca: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuRicerca()
        Try
            _azioneMenuEseguita = False
            _menuRicerca = New WGMenu()
            _menuRicerca.aggiungi("Nuova ricerca", AddressOf VoceNuovaRicerca)
            _menuRicerca.aggiungi("Ricerche recenti", AddressOf VoceRicercheRecenti)
            _menuRicerca.avvia("Cerca un contatto")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceNuovaRicerca(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            ApriCampoRicercaLibera()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceNuovaRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRicercheRecenti(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraRicercheRecenti()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceRicercheRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraRicercheRecenti()
        Try
            Dim recenti As List(Of String) = _contatti.OttieniRicercheRecenti()
            If recenti Is Nothing OrElse recenti.Count = 0 Then
                MostraMessaggio("Non ci sono ricerche recenti.")
                Return
            End If

            _listaRicercheRecenti = New WGMenuLista()
            _listaRicercheRecenti.multiselezioneConsentita = False
            _listaRicercheRecenti.impostaTitolo("Ricerche recenti")
            For Each termine As String In recenti
                _listaRicercheRecenti.aggiungiElemento(termine, termine)
            Next
            _listaRicercheRecenti.aggiungiOpzione("Nuova ricerca", AddressOf OpzioneNuovaRicercaDaLista)
            _listaRicercheRecenti.aggiungiOpzione("Svuota ricerche", AddressOf OpzioneSvuotaRicercheRecenti)
            AddHandler _listaRicercheRecenti.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
            AddHandler _listaRicercheRecenti.escPremuto, AddressOf ListaRicercheRecentiAbbandonata
            _listaRicercheRecenti.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraRicercheRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiRicercheRecenti()
        Try
            If _listaRicercheRecenti Is Nothing Then Return
            RemoveHandler _listaRicercheRecenti.ElementoSelezionato, AddressOf RicercaRecenteSelezionata
            RemoveHandler _listaRicercheRecenti.escPremuto, AddressOf ListaRicercheRecentiAbbandonata
            _listaRicercheRecenti.abbandona()
            _listaRicercheRecenti = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiRicercheRecenti: " & ex.Message)
            _listaRicercheRecenti = Nothing
        End Try
    End Sub

    Private Sub ListaRicercheRecentiAbbandonata()
        Try
            If _listaRicercheRecenti IsNot Nothing Then _listaRicercheRecenti.abbandona()
            _listaRicercheRecenti = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaRicercheRecentiAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub RicercaRecenteSelezionata(idEvento As String)
        Try
            Dim termine As String = ElementoScelto(_listaRicercheRecenti, idEvento)
            ChiudiRicercheRecenti()
            If String.IsNullOrWhiteSpace(termine) Then Return
            EseguiRicerca(termine)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RicercaRecenteSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneNuovaRicercaDaLista(sender As Object, e As EventArgs)
        Try
            ChiudiRicercheRecenti()
            ApriCampoRicercaLibera()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneNuovaRicercaDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSvuotaRicercheRecenti(sender As Object, e As EventArgs)
        Try
            _contatti.SvuotaRicercheRecenti()
            ChiudiRicercheRecenti()
            MostraMessaggio("Ricerche recenti cancellate.")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneSvuotaRicercheRecenti: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoRicercaLibera()
        Try
            _campiRicerca = New Campi()
            _campiRicerca.Aggiungi("Testo da cercare", "rub.cerca", True, False, "")
            AddHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            AddHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ApriCampoRicercaLibera: " & ex.Message)
        End Try
    End Sub

    Private Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ""
            If _campiRicerca IsNot Nothing Then testo = _campiRicerca.RecuperaValore("rub.cerca")
            LiberaRicerca()
            EseguiRicerca(testo)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RicercaConfermata: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiRicerca(testo As String)
        Try
            UsaRubricaEPoi(Sub() EseguiRicercaNellaRubricaCorrente(testo))
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.EseguiRicerca: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiRicercaNellaRubricaCorrente(testo As String)
        Try
            If Not String.IsNullOrWhiteSpace(testo) Then _contatti.AggiungiRicercaRecente(testo)
            _soloPreferiti = False
            _filtroCategoria = ""
            _testoRicercaCorrente = If(testo, "")
            _titoloElencoContattiCorrente = "Risultati della ricerca"
            _elencoTabella = OttieniTabellaElencoCorrente()
            MostraListaDaTabella(_titoloElencoContattiCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.EseguiRicercaNellaRubricaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub RicercaAnnullata(sender As Object, e As EventArgs)
        Try
            LiberaRicerca()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RicercaAnnullata: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaRicerca()
        Try
            If _campiRicerca Is Nothing Then Return
            RemoveHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            RemoveHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.LiberaRicerca: " & ex.Message)
            _campiRicerca = Nothing
        End Try
    End Sub

    Private Sub VoceNuovo(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            _chiaveContattoCorrente = ""
            _rigaCorrente = Nothing
            UsaRubricaEPoi(AddressOf ApriCampiContatto)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceNuovo: " & ex.Message)
        End Try
    End Sub

    Private Sub VocePreferiti(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            _soloPreferiti = True
            _filtroCategoria = ""
            _filtroRubrica = ""
            MostraElencoContatti()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VocePreferiti: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCategorie(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraMenuCategorie()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCategorie: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceSincronizza(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraAttesa("Sincronizzazione della rubrica in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSincronizza))
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceSincronizza: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSincronizza()
        Try
            _numeroSincronizzati = _posta.SincronizzaRubricaCompleta(_contatti, "")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.LavoroSincronizza: " & ex.Message)
            _numeroSincronizzati = 0
        Finally
            EseguiSuInterfaccia(AddressOf CompletaSincronizza)
        End Try
    End Sub

    Private Sub CompletaSincronizza()
        Try
            ChiudiAttesa()
            Dim extra As String = _posta.UltimoErrore
            If String.IsNullOrWhiteSpace(extra) Then extra = _contatti.UltimoErrore
            If String.IsNullOrWhiteSpace(extra) Then
                MostraMessaggio("Sincronizzazione completata. Operazioni eseguite: " & _numeroSincronizzati.ToString() & ".")
            Else
                MostraMessaggio("Sincronizzazione terminata con avvisi. Operazioni: " & _numeroSincronizzati.ToString() & ". " & extra)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CompletaSincronizza: " & ex.Message)
        End Try
    End Sub


    Private _titoloElencoContattiCorrente As String = ""
    ' Testo della ricerca in corso (Nothing = l'elenco mostrato non e' un risultato di ricerca).
    ' Serve al monitoraggio automatico e a "Aggiorna l'elenco": prima, dopo pochi secondi,
    ' sostituivano i risultati della ricerca con l'elenco completo dei contatti.
    Private _testoRicercaCorrente As String = Nothing

    ' Dati dell'elenco contatti attualmente aperto: risultati della ricerca, preferiti oppure
    ' elenco (per categoria/rubrica). Usata sia per aprirlo sia per tenerlo aggiornato.
    Private Function OttieniTabellaElencoCorrente() As DataTable
        If _testoRicercaCorrente IsNot Nothing Then Return _contatti.Cerca(_testoRicercaCorrente, "", _filtroRubrica)
        If _soloPreferiti Then Return _contatti.OttieniPreferiti()
        Return _contatti.OttieniElenco(_filtroCategoria, "", _filtroRubrica)
    End Function

    ' Ricarica gli elementi dell'elenco aperto restando nello stesso contesto (se era una
    ' ricerca resta una ricerca).
    Private Sub RicaricaElencoCorrente()
        Try
            _elencoTabella = OttieniTabellaElencoCorrente()
            MostraListaDaTabella(_titoloElencoContattiCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RicaricaElencoCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoContatti()
        Try
            _testoRicercaCorrente = Nothing ' elenco normale, non piu' risultati di ricerca
            _elencoTabella = OttieniTabellaElencoCorrente()
            Dim titolo As String = "Contatti"
            If _soloPreferiti Then titolo = "Contatti preferiti"
            If Not String.IsNullOrWhiteSpace(_filtroCategoria) Then titolo = "Contatti, " & _contatti.NomeCategoria(_filtroCategoria)
            If Not String.IsNullOrWhiteSpace(_filtroRubrica) Then titolo = "Rubrica """ & _contatti.NomeRubrica(_filtroRubrica) & """"
            _titoloElencoContattiCorrente = titolo
            MostraListaDaTabella(titolo)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraElencoContatti: " & ex.Message)
        End Try
    End Sub

    ' Se non ci sono contatti da mostrare, sceglie il messaggio piu' adatto in base al titolo
    ' dell'elenco richiesto (contatti, preferiti, categoria, rubrica o risultati di ricerca).
    Private Function MessaggioElencoContattiVuoto(titolo As String) As String
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return "Non ci sono contatti da mostrare."
            If titolo.StartsWith("Rubrica ", StringComparison.Ordinal) Then Return "Questa rubrica non contiene contatti."
            If String.Equals(titolo, "Contatti preferiti", StringComparison.Ordinal) Then Return "Non hai contatti preferiti."
            If titolo.StartsWith("Contatti, ", StringComparison.Ordinal) Then Return "Non ci sono contatti in questa categoria."
            If String.Equals(titolo, "Risultati della ricerca", StringComparison.Ordinal) Then Return "Nessun contatto trovato."
            Return "Non ci sono contatti da mostrare."
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MessaggioElencoContattiVuoto: " & ex.Message)
            Return "Non ci sono contatti da mostrare."
        End Try
    End Function

    ' Mostra l'elenco dei contatti. Se e' vuoto non si apre nessuna lista: si avvisa con un
    ' messaggio e basta (se una lista era gia' aperta e i dati sono nel frattempo spariti, si
    ' chiude senza messaggio, perche' il controllo puo' scattare anche in sottofondo). Se la lista
    ' e' gia' aperta e ci sono dati (l'utente non ha mai navigato via) non viene mai chiusa e
    ' riaperta: si richiama pulisci() e si reinseriscono solo gli elementi, cosi' come richiesto
    ' per ogni forma di "aggiornamento" della stessa schermata. La lista viene creata da zero
    ' soltanto la prima volta che viene mostrata.
    Private Sub MostraListaDaTabella(titolo As String)
        Try
            Dim elencoVuoto As Boolean = (_elencoTabella Is Nothing OrElse _elencoTabella.Rows.Count = 0)
            Dim listaGiaAperta As Boolean = (_listaContatti IsNot Nothing)
            _firmaElencoContattiCorrente = FirmaTabella(_elencoTabella)

            If elencoVuoto Then
                If listaGiaAperta Then
                    ChiudiElencoContatti()
                Else
                    MostraMessaggio(MessaggioElencoContattiVuoto(titolo))
                End If
                Return
            End If

            If Not listaGiaAperta Then
                _listaContatti = New WGMenuLista()
                _listaContatti.multiselezioneConsentita = True
                AddHandler _listaContatti.ElementoSelezionato, AddressOf ContattoSelezionato
                AddHandler _listaContatti.VoceCambiata, AddressOf GestioneOpzionePreferitoLista
                AddHandler _listaContatti.escPremuto, AddressOf chiudilista
            Else
                Try
                    _listaContatti.pulisci()
                Catch exPulisci As Exception
                    log.ScriviLog("MenuRubrica.MostraListaDaTabella pulisci: " & exPulisci.Message)
                End Try
            End If

            _listaContatti.impostaTitolo(titolo)
            For Each riga As DataRow In _elencoTabella.Rows
                Try
                    _listaContatti.aggiungiElemento(DescriviContatto(riga), riga("ChiaveWG").ToString())
                Catch exRiga As Exception
                    log.ScriviLog("MenuRubrica.MostraListaDaTabella riga: " & exRiga.Message)
                End Try
            Next

            _statoPreferitoMostratoInLista = False

            If Not listaGiaAperta Then
                _listaContatti.aggiungiOpzione("Chiama il contatto selezionato", AddressOf OpzioneChiamaDaLista)
                _listaContatti.aggiungiOpzione("Modifica contatto", AddressOf OpzioneModificaDaLista)
                _listaContatti.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoDaLista)
                _listaContatti.aggiungiOpzione("Elimina i contatti selezionati", AddressOf OpzioneEliminaSelezionati)
                _listaContatti.aggiungiOpzione("Aggiorna l'elenco", AddressOf OpzioneAggiornaElenco)
                If _posta IsNot Nothing AndAlso _posta.EsisteAccountConRubrica() Then
                    _listaContatti.aggiungiOpzione("Sincronizza adesso", AddressOf OpzioneSincronizzaDaElenco)
                End If
                _listaContatti.avvia()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraListaDaTabella: " & ex.Message)
        End Try
    End Sub

    ' Richiamato dal timer di monitoraggio: se l'elenco contatti aperto non riflette piu' i dati
    ' nel database (es. una sincronizzazione in sottofondo ha cambiato qualcosa), lo ricarica sul
    ' posto, senza chiudere la lista che l'utente ha davanti.
    Private Sub VerificaAggiornamentoElencoContatti()
        Try
            If _listaContatti Is Nothing Then Return
            Dim tabellaAttuale As DataTable = OttieniTabellaElencoCorrente()
            Dim firmaAttuale As String = FirmaTabella(tabellaAttuale)
            If String.Equals(firmaAttuale, _firmaElencoContattiCorrente, StringComparison.Ordinal) Then Return

            _elencoTabella = tabellaAttuale
            MostraListaDaTabella(_titoloElencoContattiCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VerificaAggiornamentoElencoContatti: " & ex.Message)
        End Try
    End Sub

    Private Sub GestioneOpzionePreferitoLista(sender As Object, e As VoceCambiataEventArgs)
        Try
            Dim id As String = e.IdVoce
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim lista As WGMenuLista = TryCast(sender, WGMenuLista)
            If lista Is Nothing Then Return

            Dim riga As DataRow = _contatti.OttieniContatto(id)
            If riga Is Nothing Then Return

            Dim preferito As Boolean = False
            Try
                preferito = Convert.ToInt32(riga("Preferito")) = 1
            Catch exParsing As Exception
                log.ScriviLog("MenuRubrica.GestioneOpzionePreferitoLista parsing Preferito: " & exParsing.Message)
            End Try

            If preferito AndAlso Not _statoPreferitoMostratoInLista Then
                lista.sostituisciOpzione("Aggiungi ai preferiti", "Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferitoDaLista)
                _statoPreferitoMostratoInLista = True
            ElseIf Not preferito AndAlso _statoPreferitoMostratoInLista Then
                lista.sostituisciOpzione("Rimuovi dai preferiti", "Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferitoDaLista)
                _statoPreferitoMostratoInLista = False
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.GestioneOpzionePreferitoLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAggiungiPreferitoDaLista(sender As Object, e As EventArgs)
        Try
            TogglePreferitoDaLista(True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneAggiungiPreferitoDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRimuoviPreferitoDaLista(sender As Object, e As EventArgs)
        Try
            TogglePreferitoDaLista(False)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneRimuoviPreferitoDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub TogglePreferitoDaLista(preferito As Boolean)
        Try
            If _listaContatti Is Nothing Then Return
            Dim selezione As List(Of String) = _listaContatti.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then
                MostraMessaggio("Seleziona un contatto.")
                Return
            End If
            Dim chiave As String = selezione(0)
            If String.IsNullOrWhiteSpace(chiave) Then Return

            If _contatti.ImpostaPreferito(chiave, preferito) Then
                If preferito Then
                    MostraMessaggio("Contatto aggiunto ai preferiti.")
                Else
                    MostraMessaggio("Contatto rimosso dai preferiti.")
                End If
            Else
                MostraMessaggio("Non è stato possibile aggiornare i preferiti.")
            End If

            ' Nessuna navigazione: l'elenco resta lo stesso, si ricaricano solo gli elementi.
            RicaricaElencoCorrente()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.TogglePreferitoDaLista: " & ex.Message)
        End Try
    End Sub
    Private Sub chiudilista()
        Try
            ChiudiElencoContatti()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.chiudilista: " & ex.Message)
        End Try
    End Sub


    Private Function DescriviContatto(riga As DataRow) As String
        Try
            Dim nome As String = (riga("Nome").ToString() & " " & riga("Cognome").ToString()).Trim()
            If String.IsNullOrWhiteSpace(nome) Then nome = riga("Email").ToString()
            If String.IsNullOrWhiteSpace(nome) Then nome = "Senza nome"
            Dim extra As New StringBuilder(nome)
            Dim email As String = riga("Email").ToString()
            If Not String.IsNullOrWhiteSpace(email) Then extra.Append(", ").Append(email)
            Dim cell As String = riga("Cellulare").ToString()
            If String.IsNullOrWhiteSpace(cell) Then cell = riga("Telefono").ToString()
            If Not String.IsNullOrWhiteSpace(cell) Then extra.Append(", ").Append(cell)
            Try
                If Convert.ToInt32(riga("Preferito")) = 1 Then extra.Append(", preferito")
            Catch exParsing As Exception
                log.ScriviLog("MenuRubrica.DescriviContatto parsing Preferito: " & exParsing.Message)
            End Try
            Return extra.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.DescriviContatto: " & ex.Message)
            Return "Contatto"
        End Try
    End Function

    Private Sub ChiudiElencoContatti()
        Try
            If _listaContatti Is Nothing Then Return
            RemoveHandler _listaContatti.ElementoSelezionato, AddressOf ContattoSelezionato
            RemoveHandler _listaContatti.VoceCambiata, AddressOf GestioneOpzionePreferitoLista
            RemoveHandler _listaContatti.escPremuto, AddressOf chiudilista
            _listaContatti.abbandona()
            _listaContatti = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiElencoContatti: " & ex.Message)
            _listaContatti = Nothing
        End Try
    End Sub

    Private Sub OpzioneAggiornaElenco(sender As Object, e As EventArgs)
        Try
            RicaricaElencoCorrente()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneAggiornaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub ContattoSelezionato(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaContatti, idEvento)
            If String.IsNullOrWhiteSpace(id) Then Return
            ' L'elenco resta aperto sotto il dettaglio: chiudendo il dettaglio si torna all'elenco.
            _chiaveContattoCorrente = id
            _rigaCorrente = _contatti.OttieniContatto(id)
            If _rigaCorrente Is Nothing Then
                MostraMessaggio("Contatto non trovato.")
                Return
            End If
            MostraLetturaContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ContattoSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraLetturaContatto()
        Try
            Dim listaGiaAperta As Boolean = (_listaDettaglio IsNot Nothing)
            Dim titolo As String = (_rigaCorrente("Nome").ToString() & " " & _rigaCorrente("Cognome").ToString()).Trim()
            If String.IsNullOrWhiteSpace(titolo) Then titolo = "Contatto"

            If Not listaGiaAperta Then
                _listaDettaglio = New WGMenuLista()
                _listaDettaglio.multiselezioneConsentita = False
                AddHandler _listaDettaglio.escPremuto, AddressOf chiudilettura
            Else
                Try
                    _listaDettaglio.pulisci()
                Catch exPulisci As Exception
                    log.ScriviLog("MenuRubrica.MostraLetturaContatto pulisci: " & exPulisci.Message)
                End Try
            End If

            _listaDettaglio.impostaTitolo(titolo)
            _listaDettaglio.aggiungiElemento("Nome: " & _rigaCorrente("Nome").ToString())
            _listaDettaglio.aggiungiElemento("Cognome: " & _rigaCorrente("Cognome").ToString())

            Dim recapiti As Dictionary(Of String, List(Of String)) = _contatti.OttieniTuttiIRecapiti(_chiaveContattoCorrente)
            _listaDettaglio.aggiungiElemento("Email: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoEmail, _rigaCorrente("Email").ToString()))
            _listaDettaglio.aggiungiElemento("Telefono: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoTelefono, _rigaCorrente("Telefono").ToString()))
            _listaDettaglio.aggiungiElemento("Cellulare: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoCellulare, _rigaCorrente("Cellulare").ToString()))
            _listaDettaglio.aggiungiElemento("Azienda: " & _rigaCorrente("Azienda").ToString())
            If _rigaCorrente.Table.Columns.Contains("Compleanno") Then
                Dim descrizioneCompleanno As String = CLSContatti.DescriviCompleanno(_rigaCorrente("Compleanno").ToString())
                If descrizioneCompleanno.Length > 0 Then _listaDettaglio.aggiungiElemento("Data di nascita o compleanno: " & descrizioneCompleanno)
            End If
            Dim indirizzi As String = DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoIndirizzo, "")
            If Not String.IsNullOrWhiteSpace(indirizzi) Then _listaDettaglio.aggiungiElemento("Indirizzo: " & indirizzi)
            Dim siti As String = DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoSito, "")
            If Not String.IsNullOrWhiteSpace(siti) Then _listaDettaglio.aggiungiElemento("Sito web: " & siti)

            Dim cat As String = _contatti.NomeCategoria(_rigaCorrente("ChiaveCategoria").ToString())
            If String.IsNullOrWhiteSpace(cat) Then cat = "nessuna"
            _listaDettaglio.aggiungiElemento("Categoria: " & cat)
            Dim contattoPreferito As Boolean = False
            Try
                contattoPreferito = Convert.ToInt32(_rigaCorrente("Preferito")) = 1
            Catch exParsing As Exception
                log.ScriviLog("MenuRubrica.MostraLetturaContatto parsing Preferito: " & exParsing.Message)
            End Try
            _listaDettaglio.aggiungiElemento("Preferito: " & If(contattoPreferito, "sì", "no"))
            Dim note As String = _rigaCorrente("Note").ToString()
            If Not String.IsNullOrWhiteSpace(note) Then _listaDettaglio.aggiungiElemento("Note: " & note)
            Dim uid As String = _rigaCorrente("UidServer").ToString()
            If Not String.IsNullOrWhiteSpace(uid) Then _listaDettaglio.aggiungiElemento("Identificativo server: " & uid)

            If listaGiaAperta Then
                ' L'opzione preferiti cambia etichetta/azione a seconda dello stato: essendo gia'
                ' presente nella lista, va sostituita (come fa gia' GestioneOpzionePreferitoLista
                ' per l'elenco), non riaggiunta come voce duplicata. Si sostituisce solo se lo
                ' stato e' davvero cambiato rispetto a quanto mostrato finora.
                If contattoPreferito AndAlso Not _statoPreferitoMostratoInDettaglio Then
                    Try
                        _listaDettaglio.sostituisciOpzione("Aggiungi ai preferiti", "Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferito)
                        _statoPreferitoMostratoInDettaglio = True
                    Catch exOpzione As Exception
                        log.ScriviLog("MenuRubrica.MostraLetturaContatto sostituisci opzione preferiti: " & exOpzione.Message)
                    End Try
                ElseIf Not contattoPreferito AndAlso _statoPreferitoMostratoInDettaglio Then
                    Try
                        _listaDettaglio.sostituisciOpzione("Rimuovi dai preferiti", "Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferito)
                        _statoPreferitoMostratoInDettaglio = False
                    Catch exOpzione As Exception
                        log.ScriviLog("MenuRubrica.MostraLetturaContatto sostituisci opzione preferiti: " & exOpzione.Message)
                    End Try
                End If
            Else
                _listaDettaglio.aggiungiOpzione("Chiama", AddressOf OpzioneChiama)
                _statoPreferitoMostratoInDettaglio = contattoPreferito
                If contattoPreferito Then
                    _listaDettaglio.aggiungiOpzione("Rimuovi dai preferiti", AddressOf OpzioneRimuoviPreferito)
                Else
                    _listaDettaglio.aggiungiOpzione("Aggiungi ai preferiti", AddressOf OpzioneAggiungiPreferito)
                End If

                If _posta IsNot Nothing AndAlso _posta.EsisteAccountConRubrica() Then
                    _listaDettaglio.aggiungiOpzione("Marca per la sincronizzazione", AddressOf OpzioneMarcaSinc)
                End If

                _listaDettaglio.aggiungiOpzione("Modifica", AddressOf OpzioneModifica)
                _listaDettaglio.aggiungiOpzione("Elimina", AddressOf OpzioneElimina)

                _listaDettaglio.avvia()
            End If

            _firmaDettaglioContattoCorrente = FirmaDettaglioContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraLetturaContatto: " & ex.Message)
        End Try
    End Sub

    ' Firma testuale del contatto attualmente mostrato in dettaglio: riusa ComponiTestoContatto,
    ' gia' presente per l'esportazione testuale, cosi' qualunque campo cambiato (anche un recapito
    ' multiplo) si riflette nella firma.
    Private Function FirmaDettaglioContatto() As String
        Try
            If _rigaCorrente Is Nothing Then Return ""
            Return ComponiTestoContatto(_rigaCorrente).GetHashCode().ToString()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.FirmaDettaglioContatto: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Richiamato dal timer di monitoraggio: se il contatto in dettaglio e' stato modificato altrove
    ' (es. da una sincronizzazione in sottofondo), ricarica gli elementi della lista di dettaglio
    ' sul posto, senza chiuderla.
    Private Sub VerificaAggiornamentoDettaglioContatto()
        Try
            If _listaDettaglio Is Nothing Then Return
            If String.IsNullOrWhiteSpace(_chiaveContattoCorrente) Then Return
            Dim rigaAttuale As DataRow = _contatti.OttieniContatto(_chiaveContattoCorrente)
            If rigaAttuale Is Nothing Then Return

            Dim firmaAttuale As String = ComponiTestoContatto(rigaAttuale).GetHashCode().ToString()
            If String.Equals(firmaAttuale, _firmaDettaglioContattoCorrente, StringComparison.Ordinal) Then Return

            _rigaCorrente = rigaAttuale
            MostraLetturaContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VerificaAggiornamentoDettaglioContatto: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviRecapiti(recapiti As Dictionary(Of String, List(Of String)), categoria As String, valorePrincipale As String) As String
        Try
            If recapiti IsNot Nothing AndAlso recapiti.ContainsKey(categoria) AndAlso recapiti(categoria).Count > 0 Then
                Return String.Join(", ", recapiti(categoria))
            End If
            Return If(valorePrincipale, "")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.DescriviRecapiti: " & ex.Message)
            Return If(valorePrincipale, "")
        End Try
    End Function

    Private Sub OpzioneAggiungiPreferito(sender As Object, e As EventArgs)
        Try
            ImpostaPreferitoCorrente(True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneAggiungiPreferito: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRimuoviPreferito(sender As Object, e As EventArgs)
        Try
            ImpostaPreferitoCorrente(False)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneRimuoviPreferito: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPreferitoCorrente(preferito As Boolean)
        Try
            If _contatti.ImpostaPreferito(_chiaveContattoCorrente, preferito) Then
                If preferito Then
                    MostraMessaggio("Contatto aggiunto ai preferiti.")
                Else
                    MostraMessaggio("Contatto rimosso dai preferiti.")
                End If
            Else
                MostraMessaggio("Non è stato possibile aggiornare i preferiti.")
            End If

            If _soloPreferiti Then
                ' Nell'elenco "solo preferiti" il contatto potrebbe non farne piu' parte: il
                ' dettaglio si chiude e basta, senza riaprire esplicitamente l'elenco.
                ChiudiCampoLettura()
            Else
                _rigaCorrente = _contatti.OttieniContatto(_chiaveContattoCorrente)
                If _rigaCorrente IsNot Nothing Then
                    MostraLetturaContatto()
                Else
                    ChiudiCampoLettura()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ImpostaPreferitoCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub chiudilettura()
        Try
            ' Oltre a chiudere il dettaglio va azzerato: se restasse abbandonato ma non vuoto, il contatto aperto dopo
            ' verrebbe caricato in una lista chiusa e non si vedrebbe niente.
            ChiudiCampoLettura()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.chiudilettura: " & ex.Message)
        End Try
    End Sub
    Private Function ComponiTestoContatto(riga As DataRow) As String
        Dim c As New StringBuilder()
        Try
            c.AppendLine("Nome: " & riga("Nome").ToString())
            c.AppendLine("Cognome: " & riga("Cognome").ToString())
            Dim chiaveContatto As String = ""
            If riga.Table.Columns.Contains("ChiaveWG") AndAlso Not riga.IsNull("ChiaveWG") Then chiaveContatto = riga("ChiaveWG").ToString()
            Dim recapiti As Dictionary(Of String, List(Of String)) = _contatti.OttieniTuttiIRecapiti(chiaveContatto)
            c.AppendLine("Email: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoEmail, riga("Email").ToString()))
            c.AppendLine("Telefono: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoTelefono, riga("Telefono").ToString()))
            c.AppendLine("Cellulare: " & DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoCellulare, riga("Cellulare").ToString()))
            c.AppendLine("Azienda: " & riga("Azienda").ToString())
            Dim indirizzi As String = DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoIndirizzo, "")
            If Not String.IsNullOrWhiteSpace(indirizzi) Then c.AppendLine("Indirizzo: " & indirizzi)
            Dim siti As String = DescriviRecapiti(recapiti, CLSContatti.CategoriaRecapitoSito, "")
            If Not String.IsNullOrWhiteSpace(siti) Then c.AppendLine("Sito web: " & siti)
            Dim cat As String = _contatti.NomeCategoria(riga("ChiaveCategoria").ToString())
            If String.IsNullOrWhiteSpace(cat) Then cat = "nessuna"
            c.AppendLine("Categoria: " & cat)
            Dim pref As String = "no"
            Try
                If Convert.ToInt32(riga("Preferito")) = 1 Then pref = "sì"
            Catch exParsing As Exception
                log.ScriviLog("MenuRubrica.ComponiTestoContatto parsing Preferito: " & exParsing.Message)
            End Try
            c.AppendLine("Preferito: " & pref)
            c.AppendLine("Note: " & riga("Note").ToString())
            Dim uid As String = riga("UidServer").ToString()
            If Not String.IsNullOrWhiteSpace(uid) Then c.AppendLine("Identificativo server: " & uid)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ComponiTestoContatto: " & ex.Message)
        End Try
        Return c.ToString()
    End Function

    Private Sub ChiudiCampoLettura()
        Try
            If _listaDettaglio Is Nothing Then Return
            RemoveHandler _listaDettaglio.escPremuto, AddressOf chiudilettura
            _listaDettaglio.abbandona()
            _listaDettaglio = Nothing
            _firmaDettaglioContattoCorrente = ""
            _statoPreferitoMostratoInDettaglio = False
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiCampoLettura: " & ex.Message)
            _listaDettaglio = Nothing
        End Try
    End Sub

    Private Sub OpzioneModifica(sender As Object, e As EventArgs)
        Try
            ' La maschera di modifica e' una schermata diversa: il dettaglio va chiuso per
            ' lasciarle spazio. Alla chiusura della modifica ci pensa il programma a ripristinare
            ' la schermata precedente.
            ChiudiCampoLettura()
            ApriCampiContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneModifica: " & ex.Message)
        End Try
    End Sub

    ' Dall'elenco dei contatti: apre la maschera di modifica del contatto evidenziato, senza passare dal dettaglio.
    Private Sub OpzioneModificaDaLista(sender As Object, e As EventArgs)
        Try
            If _listaContatti Is Nothing Then Return
            Dim chiave As String = ElementoScelto(_listaContatti, "")
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona un contatto.")
                Return
            End If
            Dim riga As DataRow = _contatti.OttieniContatto(chiave)
            If riga Is Nothing Then
                MostraMessaggio("Contatto non trovato.")
                Return
            End If
            ChiudiCampoLettura()
            _chiaveContattoCorrente = chiave
            _rigaCorrente = riga
            ApriCampiContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneModificaDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneMarcaSinc(sender As Object, e As EventArgs)
        Try
            Dim account As List(Of ImpostazioniAccountMail) = _posta.OttieniAccountConRubrica(True)
            If account Is Nothing OrElse account.Count = 0 Then
                MostraMessaggio("Nessun account con rubrica CardDAV disponibile. Outlook/Hotmail senza registrazione non sono supportati.")
                Return
            End If
            If account.Count = 1 Then
                If _contatti.MarcaPerSincronizzazione(_chiaveContattoCorrente, account(0).Chiave) Then
                    MostraMessaggio("Contatto marcato per la sincronizzazione.")
                Else
                    MostraMessaggio("Non è stato possibile marcare il contatto.")
                End If
                Return
            End If
            _listaAccount = New WGMenuLista()
            _listaAccount.multiselezioneConsentita = False
            _listaAccount.impostaTitolo("Account per la sincronizzazione")
            For Each a As ImpostazioniAccountMail In account
                Dim d As String = a.NomeVisualizzato
                If String.IsNullOrWhiteSpace(d) Then d = a.IndirizzoEmail
                _listaAccount.aggiungiElemento(d, a.Chiave)
            Next
            AddHandler _listaAccount.ElementoSelezionato, AddressOf AccountSincSelezionato
            AddHandler _listaAccount.escPremuto, AddressOf ListaAccountEsc
            _listaAccount.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneMarcaSinc: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAccountEsc(sender As Object, e As EventArgs)
        Try
            If _listaAccount IsNot Nothing Then _listaAccount.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaAccountEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAccountAbbandonata(sender As Object, e As EventArgs)
        Try
            If _listaAccount IsNot Nothing Then
                RemoveHandler _listaAccount.ElementoSelezionato, AddressOf AccountSincSelezionato
                RemoveHandler _listaAccount.escPremuto, AddressOf ListaAccountEsc
                _listaAccount.abbandona()
                _listaAccount = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaAccountAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub AccountSincSelezionato(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaAccount, idEvento)
            ListaAccountAbbandonata(Nothing, EventArgs.Empty)
            If String.IsNullOrWhiteSpace(id) Then Return
            If _contatti.MarcaPerSincronizzazione(_chiaveContattoCorrente, id) Then
                MostraMessaggio("Contatto marcato per la sincronizzazione.")
            Else
                MostraMessaggio("Non è stato possibile marcare il contatto.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AccountSincSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneElimina(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = False
            _menuConferma = New WGMenu()
            _menuConferma.aggiungi("No, non eliminare", AddressOf ConfermaEliminaNo)
            _menuConferma.aggiungi("Sì, elimina il contatto", AddressOf ConfermaEliminaSi)
            _menuConferma.avvia("Vuoi davvero eliminare questo contatto?", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneElimina: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaEliminaNo(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ConfermaEliminaNo: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaEliminaSi(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            ChiudiCampoLettura()
            If _contatti.EliminaContatto(_chiaveContattoCorrente) Then
                If _listaContatti IsNot Nothing Then RicaricaElencoCorrente()
                MostraMessaggio("Contatto eliminato.")
                AvviaSincronizzazioneDopoModifica()
            Else
                MostraMessaggio("Non è stato possibile eliminare il contatto.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ConfermaEliminaSi: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampiContatto()
        Try
            Dim nome As String = ""
            Dim cognome As String = ""
            Dim listaEmail As New List(Of String)
            Dim listaTelefono As New List(Of String)
            Dim listaCellulare As New List(Of String)
            Dim listaIndirizzo As New List(Of String)
            Dim listaSito As New List(Of String)
            Dim azienda As String = ""
            Dim note As String = ""
            Dim compleanno As String = ""
            _preferitoInModifica = False
            If _rigaCorrente IsNot Nothing Then
                nome = _rigaCorrente("Nome").ToString()
                cognome = _rigaCorrente("Cognome").ToString()
                azienda = _rigaCorrente("Azienda").ToString()
                note = _rigaCorrente("Note").ToString()
                If _rigaCorrente.Table.Columns.Contains("Compleanno") Then compleanno = CompleannoPerModifica(_rigaCorrente("Compleanno").ToString())
                If Not String.IsNullOrWhiteSpace(_chiaveContattoCorrente) Then
                    listaEmail = _contatti.OttieniRecapiti(_chiaveContattoCorrente, CLSContatti.CategoriaRecapitoEmail)
                    listaTelefono = _contatti.OttieniRecapiti(_chiaveContattoCorrente, CLSContatti.CategoriaRecapitoTelefono)
                    listaCellulare = _contatti.OttieniRecapiti(_chiaveContattoCorrente, CLSContatti.CategoriaRecapitoCellulare)
                    listaIndirizzo = _contatti.OttieniRecapiti(_chiaveContattoCorrente, CLSContatti.CategoriaRecapitoIndirizzo)
                    listaSito = _contatti.OttieniRecapiti(_chiaveContattoCorrente, CLSContatti.CategoriaRecapitoSito)
                End If
                Try
                    _preferitoInModifica = Convert.ToInt32(_rigaCorrente("Preferito")) = 1
                Catch exParsing As Exception
                    log.ScriviLog("MenuRubrica.ApriCampiContatto parsing Preferito: " & exParsing.Message)
                End Try
            End If
            _campiContatto = New Campi()
            _campiContatto.SalvaSuEscape = False
            _campiContatto.Aggiungi("Nome", "rub.nome", True, False, nome)
            _campiContatto.Aggiungi("Cognome", "rub.cognome", False, False, cognome)
            _campiContatto.AggiungiMultiConValori("Email", "rub.email", listaEmail, False)
            _campiContatto.AggiungiMultiConValori("Telefono", "rub.telefono", listaTelefono, False)
            _campiContatto.AggiungiMultiConValori("Cellulare", "rub.cellulare", listaCellulare, False)
            _campiContatto.Aggiungi("Azienda", "rub.azienda", False, False, azienda)
            _campiContatto.Aggiungi("Data di nascita o compleanno", "rub.compleanno", False, False, compleanno)
            _campiContatto.AggiungiMultiConValori("Indirizzo", "rub.indirizzo", listaIndirizzo, False)
            _campiContatto.AggiungiMultiConValori("Sito web", "rub.sito", listaSito, False)
            _campiContatto.AggiungiMultiriga("Note", "rub.note", False, False, note)
            AddHandler _campiContatto.Fine, AddressOf CampiContattoConfermato
            AddHandler _campiContatto.Chiusura, AddressOf CampiContattoAnnullato
            _campiContatto.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ApriCampiContatto: " & ex.Message)
            MostraMessaggio("Non è stato possibile aprire la maschera del contatto.")
        End Try
    End Sub

    Private Sub LiberaCampiContatto()
        Try
            If _campiContatto Is Nothing Then Return
            RemoveHandler _campiContatto.Fine, AddressOf CampiContattoConfermato
            RemoveHandler _campiContatto.Chiusura, AddressOf CampiContattoAnnullato
            _campiContatto = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.LiberaCampiContatto: " & ex.Message)
            _campiContatto = Nothing
        End Try
    End Sub

    Private Sub CampiContattoAnnullato(sender As Object, e As EventArgs)
        Try
            LiberaCampiContatto()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiContattoAnnullato: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiContattoConfermato(sender As Object, e As EventArgs)
        Try
            If _campiContatto Is Nothing Then Return
            Dim nome As String = _campiContatto.RecuperaValore("rub.nome")
            Dim cognome As String = _campiContatto.RecuperaValore("rub.cognome")
            Dim listaEmail As List(Of String) = _campiContatto.RecuperaListaValori("rub.email")
            Dim listaTelefono As List(Of String) = _campiContatto.RecuperaListaValori("rub.telefono")
            Dim listaCellulare As List(Of String) = _campiContatto.RecuperaListaValori("rub.cellulare")
            Dim azienda As String = _campiContatto.RecuperaValore("rub.azienda")
            Dim listaIndirizzo As List(Of String) = _campiContatto.RecuperaListaValori("rub.indirizzo")
            Dim listaSito As List(Of String) = _campiContatto.RecuperaListaValori("rub.sito")
            Dim note As String = _campiContatto.RecuperaValore("rub.note")
            Dim compleanno As String = CLSContatti.NormalizzaCompleanno(_campiContatto.RecuperaValore("rub.compleanno"))
            LiberaCampiContatto()
            If compleanno Is Nothing Then
                MostraMessaggio("La data del compleanno non è valida: il contatto non è stato salvato. Scrivila con il giorno, il mese e l'anno, per esempio 15/03/1980, oppure solo il giorno e il mese.")
                Return
            End If
            Dim ok As Boolean = False
            Dim eNuovo As Boolean = String.IsNullOrWhiteSpace(_chiaveContattoCorrente)
            If eNuovo Then
                Dim nuova As String = _contatti.AggiungiContatto(nome, cognome, listaEmail, listaTelefono, listaCellulare, listaIndirizzo, listaSito, azienda, "", note, False, "", _filtroRubrica, compleanno)
                ok = Not String.IsNullOrWhiteSpace(nuova)
            Else
                ok = _contatti.AggiornaContatto(_chiaveContattoCorrente, nome, cognome, listaEmail, listaTelefono, listaCellulare, listaIndirizzo, listaSito, azienda, "", note, _preferitoInModifica, "", _filtroRubrica, compleanno)
            End If
            If ok Then
                If _listaContatti IsNot Nothing Then RicaricaElencoCorrente()
                MostraMessaggio(If(eNuovo, "Contatto salvato.", "Modifica contatto riuscita."))
                AvviaSincronizzazioneDopoModifica()
            Else
                MostraMessaggio("Non è stato possibile salvare il contatto.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiContattoConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub AllineaAccountPredefinito()
        Try
            If _contatti Is Nothing OrElse _posta Is Nothing Then Return
            Dim account As List(Of ImpostazioniAccountMail) = _posta.OttieniAccountConRubrica(True)
            If account Is Nothing OrElse account.Count = 0 Then
                _contatti.AccountPredefinitoSincronizzazione = ""
                _chiaveAccountSinc = ""
                Return
            End If

            Dim chiavePreferita As String = ""
            Try
                Dim configurazione As New WGConfig()
                chiavePreferita = configurazione.LeggiValore("RubricaAccountPredefinito")
            Catch exConfig As Exception
                log.ScriviLog("MenuRubrica.AllineaAccountPredefinito lettura preferenza: " & exConfig.Message)
            End Try

            Dim scelto As String = ""
            If Not String.IsNullOrWhiteSpace(chiavePreferita) Then
                For Each a As ImpostazioniAccountMail In account
                    If String.Equals(a.Chiave, chiavePreferita, StringComparison.OrdinalIgnoreCase) Then
                        scelto = a.Chiave
                        Exit For
                    End If
                Next
            End If
            If String.IsNullOrWhiteSpace(scelto) Then scelto = account(0).Chiave

            _contatti.AccountPredefinitoSincronizzazione = scelto
            _chiaveAccountSinc = scelto
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AllineaAccountPredefinito: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaSincronizzazioneDopoModifica()
        Try
            ' i compleanni in agenda si allineano a ogni modifica dei contatti, anche senza account di posta
            WGCompleanniAgenda.SincronizzaInSottofondo()
            If _posta Is Nothing OrElse Not _posta.EsisteAccountConRubrica() Then Return
            SincronizzazioneWG.SincronizzaRubricaInSottofondo()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AvviaSincronizzazioneDopoModifica: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneSincronizzaDaElenco(sender As Object, e As EventArgs)
        Try
            VoceSincronizza(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneSincronizzaDaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaSelezionati(sender As Object, e As EventArgs)
        Try
            _chiaviDaEliminare.Clear()
            If _listaContatti Is Nothing Then Return

            Dim selezione As List(Of String) = _listaContatti.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then
                MostraMessaggio("Seleziona almeno un contatto da eliminare.")
                Return
            End If

            For Each chiave As String In selezione
                If Not String.IsNullOrWhiteSpace(chiave) Then _chiaviDaEliminare.Add(chiave)
            Next

            If _chiaviDaEliminare.Count = 0 Then
                MostraMessaggio("Seleziona almeno un contatto da eliminare.")
                Return
            End If

            Dim testoDomanda As String
            If _chiaviDaEliminare.Count = 1 Then
                testoDomanda = "Confermi l'eliminazione definitiva del contatto selezionato?"
            Else
                testoDomanda = "Confermi l'eliminazione definitiva di " & _chiaviDaEliminare.Count.ToString() & " contatti selezionati?"
            End If

            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaEliminaSelezionati
            domanda.avvia(testoDomanda)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneEliminaSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaEliminaSelezionati(rispostaDomanda As DialogResult)
        Try
            If rispostaDomanda <> DialogResult.Yes Then
                _chiaviDaEliminare.Clear()
                Return
            End If

            Dim eliminati As Integer = _contatti.EliminaContatti(_chiaviDaEliminare)
            _chiaviDaEliminare.Clear()

            If eliminati > 0 Then
                If eliminati = 1 Then
                    MostraMessaggio("Contatto eliminato.")
                Else
                    MostraMessaggio(eliminati.ToString() & " contatti eliminati.")
                End If
                AvviaSincronizzazioneDopoModifica()
            Else
                MostraMessaggio("Nessun contatto eliminato.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RispostaEliminaSelezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuSincronizzazione(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraMenuImpostazioni()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceMenuSincronizzazione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuImpostazioni()
        Try
            _azioneMenuEseguita = False
            _menuSincronizzazione = New WGMenu()
            _menuSincronizzazione.aggiungi("Rubrica predefinita per le operazioni", AddressOf VoceRubriche)
            _menuSincronizzazione.aggiungi("Sincronizzazione", AddressOf VoceMenuSincronizzazioneDettaglio)
            _menuSincronizzazione.aggiungi("Account e comportamento", AddressOf VoceMenuAccountComportamento)
            _menuSincronizzazione.avvia("Impostazioni della rubrica", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuSincronizzazioneDettaglio(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraMenuSincronizzazioneDettaglio()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceMenuSincronizzazioneDettaglio: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuSincronizzazioneDettaglio()
        Try
            Dim mnu As New WGMenu()
            mnu.aggiungi("Sincronizza adesso", AddressOf VoceSincronizzaDaConfigurazione)

            Dim stato As String = "Attiva"
            If SincronizzazioneWG.RubricaAutomaticaAttiva() Then stato = "Disattiva"
            mnu.aggiungi(stato & " la sincronizzazione automatica", AddressOf VoceCambiaStatoAutomatico)

            mnu.aggiungi("Intervallo di sincronizzazione, adesso " & SincronizzazioneWG.IntervalloRubricaMinuti().ToString() & " minuti", AddressOf VoceIntervalloSincronizzazione)
            mnu.avvia("Sincronizzazione", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuSincronizzazioneDettaglio: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuAccountComportamento(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraMenuAccountComportamento()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceMenuAccountComportamento: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuAccountComportamento()
        Try
            _menuAccountComportamento = New WGMenu()
            _menuAccountComportamento.aggiungi("Account predefinito per i nuovi contatti", AddressOf VoceAccountPredefinito)

            Dim statoEliminazioni As String = "Attiva"
            If CLSContatti.SincronizzaEliminazioniAttiva() Then statoEliminazioni = "Disattiva"
            _menuAccountComportamento.aggiungi(statoEliminazioni & " la sincronizzazione delle eliminazioni verso il server", AddressOf VoceCambiaStatoEliminazioni)

            _menuAccountComportamento.avvia("Account e comportamento", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuAccountComportamento: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCambiaStatoEliminazioni(sender As Object, e As EventArgs)
        Try
            Dim nuovoStato As Boolean = Not CLSContatti.SincronizzaEliminazioniAttiva()
            CLSContatti.ImpostaSincronizzaEliminazioni(nuovoStato)
            If nuovoStato Then
                MostraMessaggio("Le eliminazioni verranno sincronizzate anche verso il server.")
            Else
                MostraMessaggio("Le eliminazioni resteranno solo locali: i contatti cancellati qui non verranno più rimossi dal server.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCambiaStatoEliminazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAccountPredefinito(sender As Object, e As EventArgs)
        Try
            Dim account As List(Of ImpostazioniAccountMail) = _posta.OttieniAccountConRubrica(True)
            If account Is Nothing OrElse account.Count = 0 Then
                MostraMessaggio("Nessun account con rubrica CardDAV disponibile.")
                Return
            End If
            If account.Count = 1 Then
                MostraMessaggio("È configurato un solo account con rubrica: " & account(0).IndirizzoEmail & ".")
                Return
            End If

            _listaAccountPredefinito = New WGMenuLista()
            _listaAccountPredefinito.multiselezioneConsentita = False
            _listaAccountPredefinito.impostaTitolo("Account predefinito per i nuovi contatti")
            For Each a As ImpostazioniAccountMail In account
                Dim d As String = a.NomeVisualizzato
                If String.IsNullOrWhiteSpace(d) Then d = a.IndirizzoEmail
                If String.Equals(a.Chiave, _contatti.AccountPredefinitoSincronizzazione, StringComparison.OrdinalIgnoreCase) Then d &= " (attuale)"
                _listaAccountPredefinito.aggiungiElemento(d, a.Chiave)
            Next
            AddHandler _listaAccountPredefinito.ElementoSelezionato, AddressOf AccountPredefinitoSelezionato
            AddHandler _listaAccountPredefinito.escPremuto, AddressOf ListaAccountPredefinitoEsc
            _listaAccountPredefinito.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceAccountPredefinito: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAccountPredefinitoEsc()
        Try
            If _listaAccountPredefinito IsNot Nothing Then _listaAccountPredefinito.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaAccountPredefinitoEsc: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaAccountPredefinito()
        Try
            If _listaAccountPredefinito Is Nothing Then Return
            RemoveHandler _listaAccountPredefinito.ElementoSelezionato, AddressOf AccountPredefinitoSelezionato
            RemoveHandler _listaAccountPredefinito.escPremuto, AddressOf ListaAccountPredefinitoEsc
            _listaAccountPredefinito.abbandona()
            _listaAccountPredefinito = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiListaAccountPredefinito: " & ex.Message)
            _listaAccountPredefinito = Nothing
        End Try
    End Sub

    Private Sub AccountPredefinitoSelezionato(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaAccountPredefinito, idEvento)
            ChiudiListaAccountPredefinito()
            If String.IsNullOrWhiteSpace(id) Then Return
            SalvaAccountPredefinito(id)
            _contatti.AccountPredefinitoSincronizzazione = id
            MostraMessaggio("Account predefinito impostato.")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AccountPredefinitoSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaAccountPredefinito(chiaveAccount As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.SalvaValore("RubricaAccountPredefinito", chiaveAccount)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.SalvaAccountPredefinito: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceSincronizzaDaConfigurazione(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            VoceSincronizza(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceSincronizzaDaConfigurazione: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCambiaStatoAutomatico(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            Dim nuovoStato As Boolean = Not SincronizzazioneWG.RubricaAutomaticaAttiva()
            SincronizzazioneWG.ImpostaRubricaAutomatica(nuovoStato)
            RiallineaPreavvisi()
            If nuovoStato Then
                MostraMessaggio("Sincronizzazione automatica della rubrica attivata.")
            Else
                MostraMessaggio("Sincronizzazione automatica della rubrica disattivata.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceCambiaStatoAutomatico: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceIntervalloSincronizzazione(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            _menuIntervallo = New WGMenu()
            _menuIntervallo.aggiungi("Ogni 5 minuti", AddressOf IntervalloCinque)
            _menuIntervallo.aggiungi("Ogni 15 minuti", AddressOf IntervalloQuindici)
            _menuIntervallo.aggiungi("Ogni 30 minuti", AddressOf IntervalloTrenta)
            _menuIntervallo.aggiungi("Ogni ora", AddressOf IntervalloSessanta)
            _menuIntervallo.aggiungi("Ogni 3 ore", AddressOf IntervalloCentottanta)
            _menuIntervallo.aggiungi("Una volta al giorno", AddressOf IntervalloGiornaliero)
            _menuIntervallo.avvia("Ogni quanto sincronizzare la rubrica", True)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceIntervalloSincronizzazione: " & ex.Message)
        End Try
    End Sub

    Private Sub IntervalloCinque(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(5)
    End Sub

    Private Sub IntervalloQuindici(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(15)
    End Sub

    Private Sub IntervalloTrenta(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(30)
    End Sub

    Private Sub IntervalloSessanta(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(60)
    End Sub

    Private Sub IntervalloCentottanta(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(180)
    End Sub

    Private Sub IntervalloGiornaliero(sender As Object, e As EventArgs)
        SalvaIntervalloRubrica(1440)
    End Sub

    Private Sub SalvaIntervalloRubrica(minuti As Integer)
        Try
            SincronizzazioneWG.ImpostaIntervalloRubricaMinuti(minuti)
            RiallineaPreavvisi()
            MostraMessaggio("Intervallo di sincronizzazione impostato a " & minuti.ToString() & " minuti.")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.SalvaIntervalloRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub RiallineaPreavvisi()
        Try
            If PreavvisoManager.IstanzaAttiva IsNot Nothing Then
                PreavvisoManager.IstanzaAttiva.RiallineaTimerSincronizzazione()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RiallineaPreavvisi: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuCategorie()
        Try
            _azioneMenuEseguita = False
            _menuCategorie = New WGMenu()
            _menuCategorie.aggiungi("Elenco delle categorie", AddressOf VoceElencoCategorie)
            _menuCategorie.aggiungi("Nuova categoria", AddressOf VoceNuovaCategoria)
            _menuCategorie.avvia("Categorie")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraMenuCategorie: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoCategorie(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            Dim tabella As DataTable = _contatti.OttieniCategorie()
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then
                MostraMessaggio("Non ci sono categorie.")
                Return
            End If
            _listaCategorie = New WGMenuLista()
            _listaCategorie.multiselezioneConsentita = False
            _listaCategorie.impostaTitolo("Categorie")
            For Each riga As DataRow In tabella.Rows
                _listaCategorie.aggiungiElemento(riga("Nome").ToString(), riga("ChiaveWG").ToString())
            Next
            _listaCategorie.aggiungiOpzione("Nuova categoria", AddressOf VoceNuovaCategoriaDaLista)
            _listaCategorie.aggiungiOpzione("Elimina la categoria selezionata dopo invio", AddressOf VoceDummy)
            AddHandler _listaCategorie.ElementoSelezionato, AddressOf CategoriaSelezionata
            AddHandler _listaCategorie.escPremuto, AddressOf ListaCategorieAbbandonata
            _listaCategorie.avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceElencoCategorie: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceDummy(sender As Object, e As EventArgs)
    End Sub

    Private Sub VoceNuovaCategoriaDaLista(sender As Object, e As EventArgs)
        Try
            ChiudiListaCategorie()
            VoceNuovaCategoria(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceNuovaCategoriaDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaCategorie()
        Try
            If _listaCategorie Is Nothing Then Return
            RemoveHandler _listaCategorie.ElementoSelezionato, AddressOf CategoriaSelezionata
            RemoveHandler _listaCategorie.escPremuto, AddressOf ListaCategorieAbbandonata
            _listaCategorie.abbandona()
            _listaCategorie = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiListaCategorie: " & ex.Message)
            _listaCategorie = Nothing
        End Try
    End Sub

    Private Sub ListaCategorieAbbandonata(sender As Object, e As EventArgs)
        Try
            If _listaCategorie IsNot Nothing Then _listaCategorie.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaCategorieAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub CategoriaSelezionata(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaCategorie, idEvento)
            If String.IsNullOrWhiteSpace(id) Then Return
            ChiudiListaCategorie()
            _filtroCategoria = id
            _soloPreferiti = False
            UsaRubricaEPoi(AddressOf MostraElencoContatti)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CategoriaSelezionata: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceNuovaCategoria(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            _campiCategoria = New Campi()
            _campiCategoria.SalvaSuEscape = False
            _campiCategoria.Aggiungi("Nome della categoria", "cat.nome", True, False, "")
            AddHandler _campiCategoria.Fine, AddressOf CampiCategoriaConfermato
            AddHandler _campiCategoria.Chiusura, AddressOf CampiCategoriaAnnullato
            _campiCategoria.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceNuovaCategoria: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiCategoriaAnnullato(sender As Object, e As EventArgs)
        Try
            LiberaCampiCategoria()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiCategoriaAnnullato: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiCategoriaConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiCategoria IsNot Nothing Then nome = _campiCategoria.RecuperaValore("cat.nome")
            LiberaCampiCategoria()
            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della categoria è obbligatorio.")
                Return
            End If
            Dim chiave As String = _contatti.AggiungiCategoria(nome)
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Non è stato possibile creare la categoria.")
            Else
                MostraMessaggio("Categoria creata.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiCategoriaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiCategoria()
        Try
            If _campiCategoria Is Nothing Then Return
            RemoveHandler _campiCategoria.Fine, AddressOf CampiCategoriaConfermato
            RemoveHandler _campiCategoria.Chiusura, AddressOf CampiCategoriaAnnullato
            _campiCategoria = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.LiberaCampiCategoria: " & ex.Message)
            _campiCategoria = Nothing
        End Try
    End Sub

    ' ============================================================================================
    ' RUBRICHE INDIPENDENTI PER ACCOUNT
    ' ============================================================================================
    ' A differenza delle Categorie (un'unica etichetta trasversale a tutti i contatti), qui ogni
    ' account di posta ha una o piu' rubriche proprie e indipendenti (una "predefinita", creata in
    ' automatico, piu' altre eventualmente create dall'utente, es. "Lavoro"/"Famiglia"): i
    ' contatti sincronizzati da CardDAV o da Google/Nylas finiscono sempre nella rubrica
    ' predefinita del loro account, senza mai mescolarsi con quelli di un altro account.

    ' Le rubriche vengono presentate tutte insieme, quelle degli account e quelle locali, e
    ' non si sceglie piu' l'account prima: una rubrica puo' non appartenere ad alcun account.
    Private Sub VoceRubriche(sender As Object, e As EventArgs)
        Try
            _azioneMenuEseguita = True
            MostraElencoRubriche("")
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceRubriche: " & ex.Message)
        End Try
    End Sub

    Private Sub ListaAccountRubricaAbbandonata(sender As Object, e As EventArgs)
        Try
            If _listaAccount IsNot Nothing Then _listaAccount.abbandona()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaAccountRubricaAbbandonata: " & ex.Message)
        End Try
    End Sub

    Private Sub AccountRubricaSelezionato(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaAccount, idEvento)
            ListaAccountRubricaAbbandonata(Nothing, EventArgs.Empty)
            If String.IsNullOrWhiteSpace(id) Then Return
            MostraElencoRubriche(id)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.AccountRubricaSelezionato: " & ex.Message)
        End Try
    End Sub

    ' Il parametro resta per i rientri dalle maschere, ma l'elenco mostra sempre tutte le
    ' rubriche: quelle collegate a un account e quelle locali. Se la lista e' gia' aperta viene
    ' solo ripulita e riempita di nuovo, senza chiuderla e riaprirla.
    Private Sub MostraElencoRubriche(chiaveAccount As String)
        Try
            _chiaveAccountRubricaCorrente = If(chiaveAccount, "")
            Dim tabella As DataTable = _contatti.OttieniRubriche()
            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then
                _contatti.OttieniORCreaRubricaPredefinita("")
                tabella = _contatti.OttieniRubriche()
            End If

            Dim listaGiaAperta As Boolean = (_listaRubriche IsNot Nothing)
            _firmaListaRubricheCorrente = FirmaTabella(tabella)

            If tabella Is Nothing OrElse tabella.Rows.Count = 0 Then
                If listaGiaAperta Then
                    ChiudiListaRubriche()
                Else
                    MostraMessaggio("Non ci sono rubriche.")
                End If
                Return
            End If

            If Not listaGiaAperta Then
                _listaRubriche = New WGMenuLista()
                _listaRubriche.multiselezioneConsentita = False
                AddHandler _listaRubriche.ElementoSelezionato, AddressOf RubricaSelezionata
                AddHandler _listaRubriche.escPremuto, AddressOf ListaRubricheAbbandonata
            Else
                Try
                    _listaRubriche.pulisci()
                Catch exPulisci As Exception
                    log.ScriviLog("MenuRubrica.MostraElencoRubriche pulisci: " & exPulisci.Message)
                End Try
            End If

            _listaRubriche.impostaTitolo("Rubriche")
            For Each riga As DataRow In tabella.Rows
                Try
                    _listaRubriche.aggiungiElemento(DescriviRubrica(riga), riga("ChiaveWG").ToString())
                Catch exRiga As Exception
                    log.ScriviLog("MenuRubrica.MostraElencoRubriche riga: " & exRiga.Message)
                End Try
            Next

            If Not listaGiaAperta Then
                _listaRubriche.aggiungiOpzione("Nuova rubrica locale", AddressOf OpzioneNuovaRubricaLocale)
                _listaRubriche.aggiungiOpzione("Rinomina la rubrica selezionata", AddressOf OpzioneRinominaRubrica)
                _listaRubriche.aggiungiOpzione("Collega la rubrica a un account", AddressOf OpzioneCollegaRubrica)
                _listaRubriche.aggiungiOpzione("Password della rubrica", AddressOf OpzionePasswordRubrica)
                _listaRubriche.aggiungiOpzione("Importa contatti da un file vCard", AddressOf OpzioneImportaVCard)
                _listaRubriche.aggiungiOpzione("Esporta la rubrica in un file vCard", AddressOf OpzioneEsportaVCard)
                _listaRubriche.aggiungiOpzione("Rendi predefinita la rubrica selezionata", AddressOf OpzioneImpostaRubricaPredefinita)
                _listaRubriche.aggiungiOpzione("Elimina la rubrica selezionata", AddressOf OpzioneEliminaRubrica)
                _listaRubriche.avvia()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.MostraElencoRubriche: " & ex.Message)
        End Try
    End Sub

    ' Richiamato dal timer di monitoraggio: se la lista delle rubriche aperta non riflette piu' i
    ' dati nel database, la ricarica sul posto (pulisci + reinserimento), senza chiuderla.
    Private Sub VerificaAggiornamentoListaRubriche()
        Try
            If _listaRubriche Is Nothing Then Return
            Dim tabellaAttuale As DataTable = _contatti.OttieniRubriche()
            Dim firmaAttuale As String = FirmaTabella(tabellaAttuale)
            If String.Equals(firmaAttuale, _firmaListaRubricheCorrente, StringComparison.Ordinal) Then Return
            MostraElencoRubriche(_chiaveAccountRubricaCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VerificaAggiornamentoListaRubriche: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaRubriche()
        Try
            If _listaRubriche Is Nothing Then Return
            RemoveHandler _listaRubriche.ElementoSelezionato, AddressOf RubricaSelezionata
            RemoveHandler _listaRubriche.escPremuto, AddressOf ListaRubricheAbbandonata
            _listaRubriche.abbandona()
            _listaRubriche = Nothing
            _firmaListaRubricheCorrente = ""
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ChiudiListaRubriche: " & ex.Message)
            _listaRubriche = Nothing
        End Try
    End Sub

    Private Sub ListaRubricheAbbandonata(sender As Object, e As EventArgs)
        Try
            ' Chiude E azzera _listaRubriche: se restava valorizzata, alla riapertura la lista
            ' risultava "gia' aperta" e veniva solo riempita, senza mai essere mostrata.
            ChiudiListaRubriche()
            ' Se la scelta era stata richiesta per poter eseguire un'operazione (inserimento,
            ' elenco, ricerca, categorie...) e l'utente annulla, l'operazione non si esegue.
            If _operazionePendenteRubrica IsNot Nothing Then _operazionePendenteRubrica = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ListaRubricheAbbandonata: " & ex.Message)
        End Try
    End Sub

    ' Selezionare una rubrica (invio) NON mostra i suoi contatti: la rende semplicemente la
    ' rubrica di lavoro predefinita, cosi' che inserimento, elenco, ricerca, categorie e
    ' modifica operino su di essa senza dover piu' chiedere quale usare.
    Private Sub RubricaSelezionata(idEvento As String)
        Try
            Dim id As String = ElementoScelto(_listaRubriche, idEvento)
            If String.IsNullOrWhiteSpace(id) Then Return
            ChiudiListaRubriche()

            ' Una rubrica protetta e ancora chiusa non diventa predefinita finche' non arriva
            ' la password. La stessa password, se appartiene a un'altra rubrica, apre quella.
            If _contatti.RubricaProtetta(id) AndAlso Not _contatti.RubricaSbloccata(id) Then
                ChiediPassword(id, ScopoPassword.Apertura, "Password della rubrica " & _contatti.NomeRubrica(id))
                Return
            End If

            RubricaSbloccataERisolta(id)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RubricaSelezionata: " & ex.Message)
        End Try
    End Sub

    ' Elemento scelto in una lista: si legge sempre dalla lista con ottieniElementi(), in tutti
    ' i contesti. L'id che arriva con l'evento resta solo come ripiego se la lista non risponde.
    ' Va chiamata PRIMA di chiudere la lista.
    Private Function ElementoScelto(lista As WGMenuLista, idEvento As String) As String
        Try
            If lista IsNot Nothing Then
                Dim selezione As List(Of String) = lista.ottieniElementi()
                If selezione IsNot Nothing AndAlso selezione.Count > 0 AndAlso Not String.IsNullOrWhiteSpace(selezione(0)) Then Return selezione(0)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ElementoScelto: " & ex.Message)
        End Try
        Return If(idEvento, "")
    End Function

    ''' <summary>
    ''' Legge quale rubrica e' correntemente evidenziata nella lista, senza dover premere Invio
    ''' (usata dalle opzioni "Rinomina"/"Elimina"/"Rendi predefinita la rubrica selezionata").
    ''' </summary>
    Private Function RubricaEvidenziata() As String
        Try
            If _listaRubriche Is Nothing Then Return ""
            Dim selezione As List(Of String) = _listaRubriche.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return ""
            Return selezione(0)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.RubricaEvidenziata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub VoceNuovaRubricaDaLista(sender As Object, e As EventArgs)
        Try
            ChiudiListaRubriche()
            _campiRubrica = New Campi()
            _campiRubrica.SalvaSuEscape = False
            _campiRubrica.Aggiungi("Nome della rubrica", "rub.nome", True, False, "")
            AddHandler _campiRubrica.Fine, AddressOf CampiRubricaConfermato
            AddHandler _campiRubrica.Chiusura, AddressOf CampiRubricaAnnullato
            _campiRubrica.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.VoceNuovaRubricaDaLista: " & ex.Message)
        End Try
    End Sub

    Private Sub LiberaCampiRubrica()
        Try
            If _campiRubrica Is Nothing Then Return
            RemoveHandler _campiRubrica.Fine, AddressOf CampiRubricaConfermato
            RemoveHandler _campiRubrica.Chiusura, AddressOf CampiRubricaAnnullato
            _campiRubrica = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.LiberaCampiRubrica: " & ex.Message)
            _campiRubrica = Nothing
        End Try
    End Sub

    Private Sub CampiRubricaAnnullato(sender As Object, e As EventArgs)
        Try
            LiberaCampiRubrica()
            _operazionePendenteRubrica = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiRubricaAnnullato: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiRubricaConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiRubrica IsNot Nothing Then nome = _campiRubrica.RecuperaValore("rub.nome")
            LiberaCampiRubrica()
            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della rubrica è obbligatorio.")
            ElseIf String.IsNullOrWhiteSpace(_contatti.CreaRubrica(_chiaveAccountRubricaCorrente, nome)) Then
                MostraMessaggio(MessaggioErroreContatti("Non è stato possibile creare la rubrica."))
            Else
                MostraMessaggio("Rubrica creata.")
            End If
            ' Se la rubrica serviva per proseguire un'operazione (nuovo contatto, elenco, ricerca...)
            ' si torna all'elenco per sceglierla. Prima l'operazione restava in sospeso e partiva
            ' da sola alla prima rubrica scelta in seguito.
            If _operazionePendenteRubrica IsNot Nothing Then MostraElencoRubriche(_chiaveAccountRubricaCorrente)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiRubricaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneRinominaRubrica(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona una rubrica.")
                Return
            End If
            _chiaveRubricaDaRinominare = chiave
            ChiudiListaRubriche()
            _campiRubrica = New Campi()
            _campiRubrica.SalvaSuEscape = False
            _campiRubrica.Aggiungi("Nuovo nome della rubrica", "rub.nome", True, False, _contatti.NomeRubrica(chiave))
            AddHandler _campiRubrica.Fine, AddressOf CampiRinominaRubricaConfermato
            AddHandler _campiRubrica.Chiusura, AddressOf CampiRubricaAnnullato
            _campiRubrica.Avvia()
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneRinominaRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiRinominaRubricaConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            If _campiRubrica IsNot Nothing Then nome = _campiRubrica.RecuperaValore("rub.nome")
            Dim chiave As String = _chiaveRubricaDaRinominare
            _chiaveRubricaDaRinominare = ""
            LiberaCampiRubrica()
            If String.IsNullOrWhiteSpace(nome) Then
                MostraMessaggio("Il nome della rubrica è obbligatorio.")
            ElseIf _contatti.RinominaRubrica(chiave, nome) Then
                MostraMessaggio("Rubrica rinominata.")
            Else
                MostraMessaggio("Non è stato possibile rinominare la rubrica.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.CampiRinominaRubricaConfermato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneImpostaRubricaPredefinita(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona una rubrica.")
                Return
            End If
            If _contatti.ImpostaRubricaPredefinita(chiave) Then
                MostraMessaggio("Rubrica impostata come predefinita: i nuovi contatti sincronizzati per questo account finiranno qui.")
            Else
                MostraMessaggio("Non è stato possibile impostare la rubrica come predefinita.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneImpostaRubricaPredefinita: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaRubrica(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = RubricaEvidenziata()
            If String.IsNullOrWhiteSpace(chiave) Then
                MostraMessaggio("Seleziona una rubrica.")
                Return
            End If

            If _contatti.EUnicaRubricaAccount(chiave) Then
                _chiaveRubricaDaEliminare = chiave
                _menuConferma = New WGMenu()
                _menuConferma.aggiungi("No, non eliminare", AddressOf ConfermaEliminaRubricaNo)
                _menuConferma.aggiungi("Sì, elimina comunque", AddressOf ConfermaEliminaRubricaSi)
                _menuConferma.avvia("Attenzione: questa è l'unica rubrica di questo account. Eliminandola i suoi contatti passeranno alla rubrica locale personale, e l'account resterà senza una rubrica propria finché non ne sincronizzi una nuova. Vuoi eliminarla comunque?", True)
                Return
            End If

            EseguiEliminaRubrica(chiave)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.OpzioneEliminaRubrica: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaEliminaRubricaNo(sender As Object, e As EventArgs)
        Try
            _chiaveRubricaDaEliminare = ""
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ConfermaEliminaRubricaNo: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaEliminaRubricaSi(sender As Object, e As EventArgs)
        Try
            Dim chiave As String = _chiaveRubricaDaEliminare
            _chiaveRubricaDaEliminare = ""
            EseguiEliminaRubrica(chiave)
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.ConfermaEliminaRubricaSi: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiEliminaRubrica(chiave As String)
        Try
            ' I contatti non vengono mai persi: EliminaRubrica li sposta automaticamente su
            ' un'altra rubrica dello stesso account, o sulla rubrica locale se era l'unica
            ' rimasta, prima di rimuovere quella scelta.
            If _contatti.EliminaRubrica(chiave) Then
                If String.Equals(_rubricaPredefinitaCorrente, chiave, StringComparison.Ordinal) Then
                    ImpostaRubricaComePredefinitaCorrente("")
                End If
                MostraMessaggio("Rubrica eliminata. I suoi contatti sono stati spostati su un'altra rubrica.")
            Else
                Dim motivo As String = _contatti.UltimoErrore
                If String.IsNullOrWhiteSpace(motivo) Then motivo = "Impossibile eliminare questa rubrica."
                MostraMessaggio(motivo)
            End If
        Catch ex As Exception
            log.ScriviLog("MenuRubrica.EseguiEliminaRubrica: " & ex.Message)
        End Try
    End Sub

End Class