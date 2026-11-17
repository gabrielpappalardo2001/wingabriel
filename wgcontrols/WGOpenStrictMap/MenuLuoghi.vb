Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGDati
Imports wgcontrols

''' <summary>
''' Menu accessibile per trovare negozi, servizi e luoghi utili in un comune,
''' unendo i dati aperti di OpenStreetMap e di Overture Maps.
''' </summary>
Public Class MenuLuoghi

    Private Const ID_CAMPO_COMUNE As String = "comuneLuoghi"
    Private Const ID_CAMPO_NOME As String = "nomeAttivita"
    Private Const ID_CAMPO_INDIRIZZO As String = "indirizzoLuoghi"

    Private Enum Modalita
        InUnComune
        Vicinanze
    End Enum

    Private ReadOnly _luoghi As CLSLuoghi = CLSLuoghi.GetInstance()
    Private ReadOnly _logger As New Logger()

    Private WithEvents _listaGruppi As WGMenuLista
    Private WithEvents _listaCategorie As WGMenuLista
    Private WithEvents _listaRisultati As WGMenuLista
    Private WithEvents _listaDettaglio As WGMenuLista

    Private _menuPrincipale As WGMenu
    Private _menuScelta As WGMenu
    Private _campi As Campi

    Private _processo As WGProcessoInCorso
    Private _ultimoComune As String = ""
    Private _ultimoNome As String = ""
    Private _ultimoIndirizzo As String = ""
    Private _categorieScelte As New List(Of String)
    Private _modalitaCorrente As Modalita = Modalita.InUnComune
    Private _avvisoMotoreDato As Boolean = False

    Private _esitoOperazione As Boolean
    Private _risultatiOperazione As List(Of CLSLuoghi.Attivita)
    Private _attivitaCorrenti As New List(Of CLSLuoghi.Attivita)

#Region "Avvio"

    Public Sub Avvia()
        Try
            MostraMenuPrincipale()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.Avvia: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire la sezione luoghi e servizi.")
        End Try
    End Sub

    Private Sub MostraMenuPrincipale()
        Try
            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Cerca in un comune", AddressOf VoceInUnComune)
            _menuPrincipale.aggiungi("Cerca vicino a un indirizzo", AddressOf VoceVicinanze)
            _menuPrincipale.aggiungi("Cerca un'attivita' per nome", AddressOf VoceCercaPerNome)
            _menuPrincipale.avvia("luoghi e servizi")
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraMenuPrincipale: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Voci del menu principale"

    Private Sub VoceInUnComune(sender As Object, e As EventArgs)
        Try
            _modalitaCorrente = Modalita.InUnComune
            AvvisaSeMotoreDaScaricare()
            MostraCampoComune()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.VoceInUnComune: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceVicinanze(sender As Object, e As EventArgs)
        Try
            _modalitaCorrente = Modalita.Vicinanze
            AvvisaSeMotoreDaScaricare()
            MostraCampoIndirizzo()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.VoceVicinanze: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceCercaPerNome(sender As Object, e As EventArgs)
        Try
            _modalitaCorrente = Modalita.InUnComune
            AvvisaSeMotoreDaScaricare()
            MostraCampoNome()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.VoceCercaPerNome: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' La prima ricerca comporta lo scaricamento del motore di interrogazione dei dati
    ''' Overture, alcune decine di megabyte. Meglio dirlo prima una volta sola, invece di
    ''' lasciare l'utente davanti a un'attesa che non si spiega.
    ''' </summary>
    Private Sub AvvisaSeMotoreDaScaricare()
        Try
            If _avvisoMotoreDato Then Return
            _avvisoMotoreDato = True
            If _luoghi.MotoreOverturePronto() Then Return

            MostraMessaggio("Questa è la prima ricerca di luoghi: il programma scaricherà, una sola volta, un componente di alcune decine di megabyte, necessario per consultare l'archivio Overture Maps. Le ricerche successive saranno immediate. Se lo scaricamento non riesce, la ricerca funzionerà comunque con i soli dati di OpenStreetMap.")
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.AvvisaSeMotoreDaScaricare: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Esecuzione con finestra di attesa"

    Private Sub EseguiConAttesa(messaggio As String, operazione As Action)
        Try
            If operazione Is Nothing Then Return

            _processo = New WGProcessoInCorso()
            _processo.Avvia(messaggio, False)

            Dim attivita As Task = Task.Run(operazione)

            While Not attivita.IsCompleted
                Try
                    Application.DoEvents()
                    Thread.Sleep(30)
                Catch exAttesa As Exception
                    _logger.ScriviLog("MenuLuoghi.EseguiConAttesa ciclo: " & exAttesa.Message)
                End Try
            End While

            If attivita.IsFaulted AndAlso attivita.Exception IsNot Nothing Then
            End If

            Try
                _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.EseguiConAttesa: " & ex.ToString())
            Try
                If _processo IsNot Nothing Then _processo.Fine()
            Catch
            End Try
            _processo = Nothing
        End Try
    End Sub

    Private Sub ScriviAvanzamento(testo As String)
        Try
            If _processo Is Nothing Then Return
            _processo.AggiornaStato(testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ScriviAvanzamento: " & ex.Message)
        End Try
    End Sub

    Private Sub OperazioneInUnComune()
        Try
            _risultatiOperazione = _luoghi.CercaAttivitaPiuCategorie(_ultimoComune, _categorieScelte, False, New CLSLuoghi.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = True
        Catch ex As Exception
            _esitoOperazione = False
            _risultatiOperazione = Nothing
            _logger.ScriviLog("MenuLuoghi.OperazioneInUnComune: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneVicinanze()
        Try
            _risultatiOperazione = _luoghi.CercaVicinoPiuCategorie(_ultimoIndirizzo, _categorieScelte, 1500, New CLSLuoghi.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = True
        Catch ex As Exception
            _esitoOperazione = False
            _risultatiOperazione = Nothing
            _logger.ScriviLog("MenuLuoghi.OperazioneVicinanze: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneSenzaCategorie()
        Try
            Dim soloAperte As Boolean = False
            _risultatiOperazione = _luoghi.CercaTutteLeAttivita(_ultimoComune, soloAperte, New CLSLuoghi.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = True
        Catch ex As Exception
            _esitoOperazione = False
            _risultatiOperazione = Nothing
            _logger.ScriviLog("MenuLuoghi.OperazioneSenzaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub OperazioneCercaNome()
        Try
            _risultatiOperazione = _luoghi.CercaPerNome(_ultimoComune, _ultimoNome, New CLSLuoghi.Avanzamento(AddressOf ScriviAvanzamento))
            _esitoOperazione = True
        Catch ex As Exception
            _esitoOperazione = False
            _risultatiOperazione = Nothing
            _logger.ScriviLog("MenuLuoghi.OperazioneCercaNome: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Campi di immissione"

    Private Sub MostraCampoComune()
        Try
            RilasciaCampi()

            _campi = New Campi()
            _campi.Aggiungi("Comune in cui cercare", ID_CAMPO_COMUNE, True, False, _ultimoComune)

            AddHandler _campi.Fine, AddressOf ComuneConfermato
            AddHandler _campi.Chiusura, AddressOf CampoAnnullato

            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraCampoComune: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il campo per il comune.")
        End Try
    End Sub

    Private Sub ComuneConfermato(sender As Object, e As EventArgs)
        Try
            Dim comune As String = LeggiValoreCampo(ID_CAMPO_COMUNE)
            RilasciaCampi()

            If String.IsNullOrWhiteSpace(comune) Then Return
            _ultimoComune = comune.Trim()

            MostraSceltaElencoCategorie()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ComuneConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraCampoNome()
        Try
            RilasciaCampi()

            _campi = New Campi()
            _campi.Aggiungi("Comune in cui cercare", ID_CAMPO_COMUNE, True, False, _ultimoComune)
            _campi.Aggiungi("Nome dell'attivita' da cercare", ID_CAMPO_NOME, True, False, _ultimoNome)

            AddHandler _campi.Fine, AddressOf NomeConfermato
            AddHandler _campi.Chiusura, AddressOf CampoAnnullato

            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraCampoNome: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il campo di ricerca.")
        End Try
    End Sub

    Private Sub NomeConfermato(sender As Object, e As EventArgs)
        Try
            Dim comune As String = LeggiValoreCampo(ID_CAMPO_COMUNE)
            Dim nome As String = LeggiValoreCampo(ID_CAMPO_NOME)
            RilasciaCampi()

            If String.IsNullOrWhiteSpace(comune) OrElse String.IsNullOrWhiteSpace(nome) Then Return
            _ultimoComune = comune.Trim()
            _ultimoNome = nome.Trim()

            EseguiConAttesa("Cerco " & _ultimoNome & " a " & _ultimoComune & ", attendi", New Action(AddressOf OperazioneCercaNome))

            If Not _esitoOperazione OrElse _risultatiOperazione Is Nothing OrElse _risultatiOperazione.Count = 0 Then
                MostraMessaggio("Non ho trovato nessuna attivita' con questo nome a " & _ultimoComune & ". Puo' darsi che non sia ancora registrata, oppure che il nome sia scritto in modo diverso.")
                Return
            End If

            MostraRisultati(_risultatiOperazione, "Risultati per " & _ultimoNome & " a " & _ultimoComune)
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.NomeConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraCampoIndirizzo()
        Try
            RilasciaCampi()

            _campi = New Campi()
            _campi.Aggiungi("Indirizzo di partenza, per esempio via Roma 1 Poggibonsi", ID_CAMPO_INDIRIZZO, True, False, _ultimoIndirizzo)

            AddHandler _campi.Fine, AddressOf IndirizzoConfermato
            AddHandler _campi.Chiusura, AddressOf CampoAnnullato

            _campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraCampoIndirizzo: " & ex.ToString())
            MostraMessaggio("Non riesco ad aprire il campo per l'indirizzo.")
        End Try
    End Sub

    Private Sub IndirizzoConfermato(sender As Object, e As EventArgs)
        Try
            Dim indirizzo As String = LeggiValoreCampo(ID_CAMPO_INDIRIZZO)
            RilasciaCampi()

            If String.IsNullOrWhiteSpace(indirizzo) Then Return
            _ultimoIndirizzo = indirizzo.Trim()

            MostraSceltaElencoCategorie()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.IndirizzoConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub CampoAnnullato(sender As Object, e As EventArgs)
        Try
            RilasciaCampi()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.CampoAnnullato: " & ex.ToString())
        End Try
    End Sub

    Private Function LeggiValoreCampo(identificativo As String) As String
        Try
            If _campi Is Nothing Then Return ""
            Dim contenuto As String = _campi.RecuperaValore(identificativo)
            If contenuto Is Nothing Then Return ""
            Return contenuto.Trim()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.LeggiValoreCampo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RilasciaCampi()
        Try
            If _campi Is Nothing Then Return
            Try
                RemoveHandler _campi.Fine, AddressOf ComuneConfermato
                RemoveHandler _campi.Fine, AddressOf NomeConfermato
                RemoveHandler _campi.Fine, AddressOf IndirizzoConfermato
                RemoveHandler _campi.Chiusura, AddressOf CampoAnnullato
            Catch
            End Try
            Try
                
            Catch
            End Try
            _campi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.RilasciaCampi: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Scelta delle categorie"

    ''' <summary>
    ''' Con 34 categorie un elenco unico e' faticoso da scorrere a voce, quindi si sceglie
    ''' se passare dai gruppi o vedere tutto insieme.
    ''' </summary>
    Private Sub MostraSceltaElencoCategorie()
        Try
            _menuScelta = New WGMenu()
            _menuScelta.aggiungi("Ricerca per categorie", AddressOf VoceSceltaPerGruppo)
            _menuScelta.aggiungi("Cerca tutte le attivita'", AddressOf VoceSenzaCategorie)
            _menuScelta.avvia("come cercare")
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraSceltaElencoCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceSenzaCategorie(sender As Object, e As EventArgs)
        Try
            If _modalitaCorrente = Modalita.Vicinanze Then
                MostraMessaggio("La ricerca senza categorie funziona solo su un comune intero. Scegli una o piu' categorie per cercare vicino a un indirizzo.")
                Return
            End If

            _categorieScelte = New List(Of String)
            EseguiRicercaSenzaCategorie()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.VoceSenzaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiRicercaSenzaCategorie()
        Try
            Dim soloAperte As Boolean = False
            Dim descrizione As String
            If soloAperte Then
                descrizione = "Cerco tutto quello che e' aperto adesso a " & _ultimoComune & ", attendi"
            Else
                descrizione = "Cerco tutte le attivita' di " & _ultimoComune & ", attendi"
            End If

            EseguiConAttesa(descrizione, New Action(AddressOf OperazioneSenzaCategorie))

            If Not _esitoOperazione OrElse _risultatiOperazione Is Nothing OrElse _risultatiOperazione.Count = 0 Then
                If soloAperte Then
                    MostraMessaggio("In questo momento non risulta aperta nessuna attivita' a " & _ultimoComune & ". Tieni presente che molte attivita' non dichiarano il proprio orario.")
                Else
                    MostraMessaggio("Non ho trovato attivita' a " & _ultimoComune & ". Prova a scrivere il comune in modo diverso, oppure ad aggiungere la provincia.")
                End If
                Return
            End If

            Dim titolo As String
            If soloAperte Then
                titolo = "Attivita' aperte adesso a " & _ultimoComune
            Else
                titolo = "Tutte le attivita' di " & _ultimoComune
            End If

            MostraRisultati(_risultatiOperazione, titolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.EseguiRicercaSenzaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceSceltaPerGruppo(sender As Object, e As EventArgs)
        Try
            MostraElencoGruppi()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.VoceSceltaPerGruppo: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraElencoGruppi()
        Try
            Dim gruppi = _luoghi.GruppiDisponibili()
            If gruppi Is Nothing OrElse gruppi.Count = 0 Then Return

            ChiudiListe()

            _listaGruppi = New WGMenuLista()
            _listaGruppi.ciclono = True
            _listaGruppi.selezioneMenu = True
            _listaGruppi.multiselezioneConsentita = False

            _listaGruppi.BeginUpdate()
            For Each g In gruppi
                Try
                    _listaGruppi.aggiungiElemento(g, g)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuLuoghi.MostraElencoGruppi elemento: " & exElemento.Message)
                End Try
            Next
            _listaGruppi.EndUpdate()

            _listaGruppi.impostaTitolo("Scegli il tipo di luogo")
            _listaGruppi.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraElencoGruppi: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaGruppi_ElementoSelezionato(id As String) Handles _listaGruppi.ElementoSelezionato
        Try
            GruppoConfermato(PrimoIdentificativo(RaccogliIdentificativi(id)))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaGruppi_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaGruppi_ElementiSelezionati(ids As List(Of String)) Handles _listaGruppi.ElementiSelezionati
        Try
            GruppoConfermato(PrimoIdentificativo(RaccogliIdentificativi(ids)))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaGruppi_ElementiSelezionati: " & ex.ToString())
        End Try
    End Sub

    Private Sub GruppoConfermato(gruppo As String)
        Try
            If String.IsNullOrWhiteSpace(gruppo) Then Return
            ChiudiListaGruppi()
            MostraElencoCategorie(_luoghi.CategorieDelGruppo(gruppo), gruppo)
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.GruppoConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaGruppi_escPremuto(sender As Object, e As EventArgs) Handles _listaGruppi.escPremuto
        Try
            ChiudiListaGruppi()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaGruppi_escPremuto: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Unico elenco di categorie, sempre a selezione multipla: si puo' scegliere una sola
    ''' categoria oppure diverse insieme, senza dover decidere prima quale modo usare.
    ''' </summary>
    Private Sub MostraElencoCategorie(categorie As List(Of String), titolo As String)
        Try
            If categorie Is Nothing OrElse categorie.Count = 0 Then Return

            ChiudiListaCategorie()

            _listaCategorie = New WGMenuLista()
            _listaCategorie.ciclono = True
            _listaCategorie.selezioneMenu = True
            _listaCategorie.multiselezioneConsentita = True

            _listaCategorie.BeginUpdate()
            For Each c In categorie
                Try
                    _listaCategorie.aggiungiElemento(c, c)
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuLuoghi.MostraElencoCategorie elemento: " & exElemento.Message)
                End Try
            Next
            _listaCategorie.EndUpdate()

            ' Con la multiselezione le frecce spostano soltanto il fuoco: la selezione
            ' si fa con la barra spaziatrice. Lo si dice nel titolo perche' altrimenti
            ' premendo Invio senza aver selezionato niente non succederebbe nulla.
            _listaCategorie.impostaTitolo(titolo & ", seleziona con la barra spaziatrice una o piu' categorie, poi premi Invio")
            _listaCategorie.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraElencoCategorie: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' La lista puo' sollevare un evento diverso a seconda che la multiselezione sia
    ''' attiva o no. Vengono gestiti entrambi, cosi' il comportamento resta corretto
    ''' qualunque sia l'impostazione della lista.
    ''' </summary>
    Private Sub _listaCategorie_ElementoSelezionato(id As String) Handles _listaCategorie.ElementoSelezionato
        Try
            CategorieConfermate(RaccogliIdentificativi(id))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaCategorie_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaCategorie_ElementiSelezionati(ids As List(Of String)) Handles _listaCategorie.ElementiSelezionati
        Try
            CategorieConfermate(RaccogliIdentificativi(ids))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaCategorie_ElementiSelezionati: " & ex.ToString())
        End Try
    End Sub

    Private Sub CategorieConfermate(scelte As List(Of String))
        Try
            If scelte Is Nothing OrElse scelte.Count = 0 Then Return

            _categorieScelte = scelte
            ChiudiListaCategorie()
            EseguiRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.CategorieConfermate: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaCategorie_escPremuto(sender As Object, e As EventArgs) Handles _listaCategorie.escPremuto
        Try
            ChiudiListaCategorie()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaCategorie_escPremuto: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Ricerca"

    Private Sub EseguiRicerca()
        Try
            If _categorieScelte Is Nothing OrElse _categorieScelte.Count = 0 Then Return

            Dim descrizione As String = DescrizioneAttesa()

            Select Case _modalitaCorrente
                Case Modalita.Vicinanze
                    EseguiConAttesa(descrizione, New Action(AddressOf OperazioneVicinanze))
                Case Else
                    EseguiConAttesa(descrizione, New Action(AddressOf OperazioneInUnComune))
            End Select

            If Not _esitoOperazione OrElse _risultatiOperazione Is Nothing OrElse _risultatiOperazione.Count = 0 Then
                MostraMessaggioNessunRisultato()
                Return
            End If

            MostraRisultati(_risultatiOperazione, TitoloRisultati())
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Function DescrizioneCategorieScelte() As String
        Try
            If _categorieScelte Is Nothing OrElse _categorieScelte.Count = 0 Then Return "luoghi"
            If _categorieScelte.Count = 1 Then Return _categorieScelte(0).ToLowerInvariant()
            Return _categorieScelte.Count.ToString() & " categorie"
        Catch
            Return "luoghi"
        End Try
    End Function

    Private Function DescrizioneAttesa() As String
        Try
            Dim cosa As String = DescrizioneCategorieScelte()
            Select Case _modalitaCorrente
                Case Modalita.Vicinanze
                    Return "Cerco " & cosa & " vicino a " & _ultimoIndirizzo & ", attendi"
                Case Else
                    Return "Cerco " & cosa & " a " & _ultimoComune & ", attendi"
            End Select
        Catch
            Return "Ricerca in corso, attendi"
        End Try
    End Function

    Private Function TitoloRisultati() As String
        Try
            Dim cosa As String
            If _categorieScelte IsNot Nothing AndAlso _categorieScelte.Count = 1 Then
                cosa = _categorieScelte(0)
            Else
                cosa = "Luoghi"
            End If

            Select Case _modalitaCorrente
                Case Modalita.Vicinanze
                    Return cosa & " vicino a " & _ultimoIndirizzo
                Case Else
                    Return cosa & " a " & _ultimoComune
            End Select
        Catch
            Return "Risultati"
        End Try
    End Function

    Private Sub MostraMessaggioNessunRisultato()
        Try
            Select Case _modalitaCorrente
                Case Modalita.Vicinanze
                    MostraMessaggio("Non ho trovato niente di quello che hai chiesto vicino a " & _ultimoIndirizzo & ".")
                Case Else
                    MostraMessaggio("Non ho trovato niente di quello che hai chiesto a " & _ultimoComune & ". Puo' darsi che nessuno l'abbia ancora registrato negli archivi che consulto.")
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraMessaggioNessunRisultato: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Risultati e dettaglio"

    Private Sub MostraRisultati(elenco As List(Of CLSLuoghi.Attivita), titolo As String)
        Try
            ChiudiListe()

            _attivitaCorrenti = elenco

            _listaRisultati = New WGMenuLista()
            _listaRisultati.ciclono = True
            _listaRisultati.selezioneMenu = True
            _listaRisultati.multiselezioneConsentita = False

            _listaRisultati.BeginUpdate()
            Dim progressivo As Integer = 0
            For Each a In elenco
                Try
                    progressivo += 1
                    _listaRisultati.aggiungiElemento(_luoghi.EtichettaAttivita(a), "att" & progressivo.ToString())
                Catch exElemento As Exception
                    _logger.ScriviLog("MenuLuoghi.MostraRisultati elemento: " & exElemento.Message)
                End Try
            Next
            _listaRisultati.EndUpdate()

            _listaRisultati.impostaTitolo(titolo)
            _listaRisultati.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_ElementoSelezionato(id As String) Handles _listaRisultati.ElementoSelezionato
        Try
            AttivitaConfermata(PrimoIdentificativo(RaccogliIdentificativi(id)))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaRisultati_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_ElementiSelezionati(ids As List(Of String)) Handles _listaRisultati.ElementiSelezionati
        Try
            AttivitaConfermata(PrimoIdentificativo(RaccogliIdentificativi(ids)))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaRisultati_ElementiSelezionati: " & ex.ToString())
        End Try
    End Sub

    Private Sub AttivitaConfermata(identificativo As String)
        Try
            If String.IsNullOrWhiteSpace(identificativo) Then Return

            Dim indice As Integer = 0
            If Not Integer.TryParse(identificativo.Trim().Replace("att", ""), indice) Then Return
            If _attivitaCorrenti Is Nothing OrElse indice < 1 OrElse indice > _attivitaCorrenti.Count Then Return

            MostraDettaglio(_attivitaCorrenti(indice - 1))
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.AttivitaConfermata: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_escPremuto(sender As Object, e As EventArgs) Handles _listaRisultati.escPremuto
        Try
            ChiudiListaRisultati()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaRisultati_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraDettaglio(a As CLSLuoghi.Attivita)
        Try
            Dim righe = _luoghi.InformazioniAttivita(a)
            If righe Is Nothing OrElse righe.Count = 0 Then
                MostraMessaggio("Non ci sono informazioni disponibili per questa attivita'.")
                Return
            End If

            ChiudiListaDettaglio()

            _listaDettaglio = New WGMenuLista()
            _listaDettaglio.ciclono = True
            _listaDettaglio.selezioneMenu = True
            _listaDettaglio.multiselezioneConsentita = False

            _listaDettaglio.BeginUpdate()
            Dim progressivo As Integer = 0
            For Each riga In righe
                Try
                    progressivo += 1
                    _listaDettaglio.aggiungiElemento(riga, "det" & progressivo.ToString())
                Catch exRiga As Exception
                    _logger.ScriviLog("MenuLuoghi.MostraDettaglio elemento: " & exRiga.Message)
                End Try
            Next
            _listaDettaglio.EndUpdate()

            _listaDettaglio.impostaTitolo("Informazioni su " & Valore(a.Nome))
            _listaDettaglio.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraDettaglio: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaDettaglio_escPremuto(sender As Object, e As EventArgs) Handles _listaDettaglio.escPremuto
        Try
            ChiudiListaDettaglio()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi._listaDettaglio_escPremuto: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Chiusure e utilita"

    Private Sub MostraMessaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.MostraMessaggio: " & ex.ToString())
        End Try
    End Sub

    Private Function Valore(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return "non disponibile"
            Return testo.Trim()
        Catch
            Return "non disponibile"
        End Try
    End Function

    ''' <summary>
    ''' Riduce a un elenco pulito quello che arriva dai due eventi della lista:
    ''' un identificativo singolo oppure un elenco, senza vuoti ne' doppioni.
    ''' </summary>
    Private Function RaccogliIdentificativi(id As String) As List(Of String)
        Dim elenco As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return elenco
            elenco.Add(id.Trim())
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.RaccogliIdentificativi: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Function RaccogliIdentificativi(ids As List(Of String)) As List(Of String)
        Dim elenco As New List(Of String)
        Try
            If ids Is Nothing Then Return elenco
            For Each voce In ids
                Try
                    If String.IsNullOrWhiteSpace(voce) Then Continue For
                    Dim pulita As String = voce.Trim()
                    If Not elenco.Contains(pulita) Then elenco.Add(pulita)
                Catch
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.RaccogliIdentificativi: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Function PrimoIdentificativo(elenco As List(Of String)) As String
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return ""
            Return elenco(0)
        Catch
            Return ""
        End Try
    End Function

    Private Sub ChiudiListe()
        Try
            ChiudiListaGruppi()
            ChiudiListaCategorie()
            ChiudiListaRisultati()
            ChiudiListaDettaglio()
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ChiudiListe: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaGruppi()
        Try
            If _listaGruppi Is Nothing Then Return
            Try
                _listaGruppi.abbandona()
            Catch
            End Try
            _listaGruppi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ChiudiListaGruppi: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaCategorie()
        Try
            If _listaCategorie Is Nothing Then Return
            Try
                _listaCategorie.abbandona()
            Catch
            End Try
            _listaCategorie = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ChiudiListaCategorie: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaRisultati()
        Try
            If _listaRisultati Is Nothing Then Return
            Try
                _listaRisultati.abbandona()
            Catch
            End Try
            _listaRisultati = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ChiudiListaRisultati: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaDettaglio()
        Try
            If _listaDettaglio Is Nothing Then Return
            Try
                _listaDettaglio.abbandona()
            Catch
            End Try
            _listaDettaglio = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuLuoghi.ChiudiListaDettaglio: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class