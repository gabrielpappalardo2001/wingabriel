Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGDati
Imports wgcontrols
Imports WGPlayerMpv

' Navigazione e ascolto delle stazioni radio tramite elenchi, generi, regioni e ricerca nei palinsesti con gestione dei
' promemoria.
Public Class MenuRadio

    Private Const ID_CAMPO_RICERCA As String = "ricercaRadio"
    Private Const ID_CAMPO_NOME_RADIO As String = "nomeRadioManuale"
    Private Const ID_CAMPO_URL_RADIO As String = "urlRadioManuale"
    Private Const TESTO_AGGIUNGI_PREFERITI As String = "Aggiungi ai preferiti"
    Private Const TESTO_RIMUOVI_PREFERITI As String = "Rimuovi dai preferiti"
    Private Const MODALITA_INTERNAZIONALI As String = "internazionali"
    Private Const MODALITA_GENERE As String = "genere"
    Private Const MODALITA_RICERCA As String = "ricerca"

    Private ReadOnly _db As RadioDb = RadioDb.GetInstance()
    Private ReadOnly _raccolta As RadioTvDatabase = RadioTvDatabase.GetInstance()
    Private ReadOnly _logger As New Logger()

    Private WithEvents _menuPrincipale As WGMenu
    Private WithEvents _listaTutte As WGMenuLista
    Private WithEvents _listaPopolari As WGMenuLista
    Private WithEvents _listaRisultati As WGMenuLista
    Private WithEvents _listaRegioni As WGMenuLista
    Private WithEvents _listaStazioniRegione As WGMenuLista
    Private WithEvents _listaGeneri As WGMenuLista
    Private WithEvents _listaStazioniGenere As WGMenuLista
    Private WithEvents _listaPreferite As WGMenuLista
    Private WithEvents _listaRecenti As WGMenuLista
    Private WithEvents _listaPaesi As WGMenuLista
    Private WithEvents _listaStazioniInternazionali As WGMenuLista
    Private WithEvents _listaProgrammi As WGMenuLista
    Private _programmiCorrenti As New List(Of TvProgrammazione.VoceProgramma)
    Private _idStazioneProgrammi As String = ""
    Private _nomeStazioneProgrammi As String = ""

    Private _campiRicerca As Campi
    Private _campiIndirizzo As Campi
    Private _timerRicerca As Timer
    Private _testoRicercaPendente As String = ""
    Private _ultimaRicerca As String = ""
    Private _regioneCorrente As String = ""
    Private _stazioniCorrenti As New List(Of RadioDb.StazioneRadio)
    Private _testoOpzionePreferitoAttuale As String = TESTO_AGGIUNGI_PREFERITI

    Private _modalitaPaeseCorrente As String = MODALITA_RICERCA
    Private _codicePaeseCorrente As String = RadioDb.CODICE_ITALIA
    Private _nomiPaesi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Private _stazioneInAscolto As RadioDb.StazioneRadio
    Private _stazioneInfoCorrente As RadioDb.StazioneRadio
    Private _stazioneManualeInAttesa As RadioDb.StazioneRadio

#Region "Avvio"

    Public Sub Avvia()
        Try
            TvPromemoria.Avvia()
            Task.Run(New Action(AddressOf CaricaDatiInBackground))
            MostraMenuPrincipale()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.Avvia: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile aprire il menu delle radio.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub CaricaDatiInBackground()
        Try
            _db.Carica(False)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.CaricaDatiInBackground: " & ex.ToString())
        End Try
    End Sub

    Private Function AssicuraDatiCaricati() As Boolean
        Try
            If _db.IsCaricato Then Return True

            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando l'elenco delle radio, attendi", False)
            Try
                Dim ok As Boolean = _db.Carica(False)
                proc.Fine()
                If Not ok OrElse _db.NumeroStazioni = 0 Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Non e' stato possibile scaricare l'elenco delle radio. Controlla la connessione a internet e riprova.")
                    msg.ShowDialog()
                    Return False
                End If
                Return True
            Catch exInterno As Exception
                _logger.ScriviLog("MenuRadio.AssicuraDatiCaricati interno: " & exInterno.ToString())
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuRadio.AssicuraDatiCaricati fine: " & exFine.Message)
                End Try
                Return False
            End Try
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AssicuraDatiCaricati: " & ex.ToString())
            Return False
        End Try
    End Function

#End Region

#Region "Menu principale"

    ' Le viste sulla programmazione di tutte le radio insieme: 1 in onda adesso, 2 ricerca nei palinsesti, 3 programmi seguiti.
    Private Sub ApriVistaPalinsesto(vista As Integer)
        Try
            Dim palinsesto As New MenuPalinsesto(TvPromemoria.TIPO_RADIO)
            Select Case vista
                Case 1 : palinsesto.InOndaAdesso()
                Case 2 : palinsesto.Cerca()
                Case Else : palinsesto.Seguiti()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriVistaPalinsesto: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraMenuPrincipale()
        Try
            ChiudiListe()

            _menuPrincipale = New WGMenu()
            _menuPrincipale.aggiungi("Cerca una radio", AddressOf ApriRicerca)
            _menuPrincipale.aggiungi("Tutte le radio italiane", AddressOf ApriTutte)
            _menuPrincipale.aggiungi("Radio per regione", AddressOf ApriElencoRegioni)
            _menuPrincipale.aggiungi("Radio per genere", AddressOf ApriElencoGeneri)
            _menuPrincipale.aggiungi("Radio internazionali", AddressOf ApriRadioInternazionali)
            _menuPrincipale.aggiungi("Ascolto radio attuale", AddressOf ApriAscoltoAttuale)
            _menuPrincipale.aggiungi("Scrivi indirizzo", AddressOf ApriScriviIndirizzo)
            _menuPrincipale.aggiungi("Radio preferite", AddressOf ApriPreferite)
            _menuPrincipale.aggiungi("Radio ascoltate di recente", AddressOf ApriRecenti)
            _menuPrincipale.aggiungi("Le piu' ascoltate", AddressOf ApriPopolari)
            _menuPrincipale.aggiungi("In onda adesso", Sub() ApriVistaPalinsesto(1))
            _menuPrincipale.aggiungi("Cerca nei palinsesti", Sub() ApriVistaPalinsesto(2))
            _menuPrincipale.aggiungi("Programmi seguiti", Sub() ApriVistaPalinsesto(3))
            _menuPrincipale.aggiungiOpzione("Aggiorna elenco radio", AddressOf AggiornaElenco)
            _menuPrincipale.avvia("Radio")
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.MostraMenuPrincipale: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaElenco(sender As Object, e As EventArgs)
        Try
            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto aggiornando l'elenco delle radio, attendi", False)
            Dim ok As Boolean = _db.Carica(True)
            proc.Fine()

            Dim msg As New WGMessaggi()
            If ok Then
                msg.Messaggio("Elenco aggiornato. Trovate " & _db.NumeroStazioni.ToString() & " radio italiane.")
            Else
                msg.Messaggio("Aggiornamento non riuscito. Verra' usata la copia gia' presente, se disponibile.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AggiornaElenco: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Ricerca"

    Private Sub ApriRicerca(sender As Object, e As EventArgs)
        Try
            If Not AssicuraPaesiCaricati() Then Return
            _modalitaPaeseCorrente = MODALITA_RICERCA
            MostraListaPaesi("Cerca radio, scegli il paese")
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneNuovaRicerca(sender As Object, e As EventArgs)
        Try
            MostraCampoRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneNuovaRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraCampoRicerca()
        Try
            RilasciaCampiRicerca()

            _campiRicerca = New Campi()
            _campiRicerca.Aggiungi("Scrivi il nome della radio o un genere", ID_CAMPO_RICERCA, True, False, _ultimaRicerca)

            AddHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            AddHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata

            _campiRicerca.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.MostraCampoRicerca: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile aprire il campo di ricerca.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            Dim testo As String = ""

            Try
                If _campiRicerca IsNot Nothing Then
                    testo = _campiRicerca.RecuperaValore(ID_CAMPO_RICERCA)
                End If
            Catch exValore As Exception
                _logger.ScriviLog("MenuRadio.RicercaConfermata lettura: " & exValore.Message)
            End Try

            If testo Is Nothing Then testo = ""
            testo = testo.Trim()

            If String.IsNullOrWhiteSpace(testo) Then
                RilasciaCampiRicerca()
                Return
            End If

            _ultimaRicerca = testo
            _testoRicercaPendente = testo

            AvviaTimerRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RicercaConfermata: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaAnnullata(sender As Object, e As EventArgs)
        Try
            _testoRicercaPendente = ""
            RilasciaCampiRicerca()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RicercaAnnullata: " & ex.ToString())
        End Try
    End Sub

    Private Sub AvviaTimerRicerca()
        Try
            If _timerRicerca Is Nothing Then
                _timerRicerca = New Timer()
                _timerRicerca.Interval = 1
                AddHandler _timerRicerca.Tick, AddressOf RicercaDifferita
            End If

            _timerRicerca.Stop()
            _timerRicerca.Start()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AvviaTimerRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub RicercaDifferita(sender As Object, e As EventArgs)
        Try
            If _timerRicerca IsNot Nothing Then _timerRicerca.Stop()

            RilasciaCampiRicerca()

            Dim testo As String = _testoRicercaPendente
            _testoRicercaPendente = ""

            If String.IsNullOrWhiteSpace(testo) Then Return

            EseguiRicerca(testo)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RicercaDifferita: " & ex.ToString())
        End Try
    End Sub

    Private Sub RilasciaCampiRicerca()
        Try
            If _campiRicerca Is Nothing Then Return

            Try
                RemoveHandler _campiRicerca.Fine, AddressOf RicercaConfermata
                RemoveHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            Catch exHandler As Exception
                _logger.ScriviLog("MenuRadio.RilasciaCampiRicerca handler: " & exHandler.Message)
            End Try

            Try
                
            Catch exDispose As Exception
                _logger.ScriviLog("MenuRadio.RilasciaCampiRicerca dispose: " & exDispose.Message)
            End Try

            _campiRicerca = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RilasciaCampiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiRicerca(testo As String)
        Try
            Dim risultati As List(Of RadioDb.StazioneRadio)
            Dim suffissoPaese As String = ""

            If String.IsNullOrWhiteSpace(_codicePaeseCorrente) OrElse String.Equals(_codicePaeseCorrente, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then
                risultati = _db.Cerca(testo)
            Else
                risultati = _db.CercaPaese(_codicePaeseCorrente, testo)
                suffissoPaese = ", " & NomePaeseDaCodice(_codicePaeseCorrente)
            End If

            If risultati Is Nothing OrElse risultati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio trovata per " & testo & ".")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = risultati
            _regioneCorrente = ""

            ChiudiListeStazioni()

            _listaRisultati = New WGMenuLista()
            _listaRisultati.ciclono = True
            _listaRisultati.selezioneMenu = True
            _listaRisultati.multiselezioneConsentita = False

            RiempiLista(_listaRisultati, risultati)
            ConfiguraOpzioniListaStazioni(_listaRisultati, risultati)
            _listaRisultati.aggiungiOpzione("Nuova ricerca", AddressOf OpzioneNuovaRicerca)

            _listaRisultati.impostaTitolo("Risultati per " & testo & suffissoPaese & ", " & risultati.Count.ToString() & " radio")
            _listaRisultati.avvia()

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto("Trovate " & risultati.Count.ToString() & " radio.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_ElementoSelezionato(id As String) Handles _listaRisultati.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRisultati_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_escPremuto(sender As Object, e As EventArgs) Handles _listaRisultati.escPremuto
        Try
            If _listaRisultati IsNot Nothing Then
                _listaRisultati.abbandona()
                _listaRisultati = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRisultati_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRisultati_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaRisultati.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaRisultati, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRisultati_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Tutte le radio"

    Private Sub ApriTutte(sender As Object, e As EventArgs)
        Try
            If Not AssicuraDatiCaricati() Then Return

            Dim stazioni As List(Of RadioDb.StazioneRadio) = _db.OttieniTutte()
            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = ""

            ChiudiListeStazioni()

            _listaTutte = New WGMenuLista()
            _listaTutte.ciclono = True
            _listaTutte.selezioneMenu = True
            _listaTutte.multiselezioneConsentita = False

            RiempiLista(_listaTutte, stazioni)
            ConfiguraOpzioniListaStazioni(_listaTutte, stazioni)

            _listaTutte.impostaTitolo("Tutte le radio italiane")
            _listaTutte.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriTutte: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaTutte_ElementoSelezionato(id As String) Handles _listaTutte.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaTutte_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaTutte_escPremuto(sender As Object, e As EventArgs) Handles _listaTutte.escPremuto
        Try
            If _listaTutte IsNot Nothing Then
                _listaTutte.abbandona()
                _listaTutte = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaTutte_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaTutte_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaTutte.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaTutte, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaTutte_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Le piu ascoltate"

    Private Sub ApriPopolari(sender As Object, e As EventArgs)
        Try
            If Not AssicuraDatiCaricati() Then Return

            Dim stazioni As List(Of RadioDb.StazioneRadio) = _db.OttieniPiuAscoltate(100)
            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = ""

            ChiudiListeStazioni()

            _listaPopolari = New WGMenuLista()
            _listaPopolari.ciclono = True
            _listaPopolari.selezioneMenu = True
            _listaPopolari.multiselezioneConsentita = False

            RiempiLista(_listaPopolari, stazioni)
            ConfiguraOpzioniListaStazioni(_listaPopolari, stazioni)

            _listaPopolari.impostaTitolo("Le radio piu' ascoltate")
            _listaPopolari.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriPopolari: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPopolari_ElementoSelezionato(id As String) Handles _listaPopolari.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPopolari_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPopolari_escPremuto(sender As Object, e As EventArgs) Handles _listaPopolari.escPremuto
        Try
            If _listaPopolari IsNot Nothing Then
                _listaPopolari.abbandona()
                _listaPopolari = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPopolari_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPopolari_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaPopolari.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaPopolari, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPopolari_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Radio per regione"

    Private Sub ApriElencoRegioni(sender As Object, e As EventArgs)
        Try
            If Not AssicuraDatiCaricati() Then Return

            Dim regioni As List(Of RadioDb.RegioneRadio) = _db.OttieniRegioni()
            If regioni Is Nothing OrElse regioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna regione con radio disponibili al momento.")
                msg.ShowDialog()
                Return
            End If

            ChiudiListeStazioni()

            _listaRegioni = New WGMenuLista()
            _listaRegioni.ciclono = True
            _listaRegioni.selezioneMenu = True
            _listaRegioni.multiselezioneConsentita = False

            _listaRegioni.BeginUpdate()
            For i As Integer = 0 To regioni.Count - 1
                Dim etichetta As String = regioni(i).Nome & ", " & regioni(i).Stazioni.Count.ToString() & " radio"
                _listaRegioni.aggiungiElemento(etichetta, regioni(i).Nome)
            Next
            _listaRegioni.EndUpdate()

            _listaRegioni.impostaTitolo("Radio per regione")
            _listaRegioni.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriElencoRegioni: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRegioni_ElementoSelezionato(id As String) Handles _listaRegioni.ElementoSelezionato
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            ApriStazioniRegione(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRegioni_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRegioni_escPremuto(sender As Object, e As EventArgs) Handles _listaRegioni.escPremuto
        Try
            If _listaRegioni IsNot Nothing Then
                _listaRegioni.abbandona()
                _listaRegioni = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRegioni_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriStazioniRegione(nomeRegione As String)
        Try
            Dim stazioni As List(Of RadioDb.StazioneRadio) = _db.OttieniStazioniRegione(nomeRegione)
            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio trovata per " & nomeRegione & ".")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = nomeRegione

            If _listaStazioniRegione IsNot Nothing Then
                Try
                    _listaStazioniRegione.abbandona()
                Catch exAbb As Exception
                    _logger.ScriviLog("MenuRadio.ApriStazioniRegione abbandona: " & exAbb.Message)
                End Try
                _listaStazioniRegione = Nothing
            End If

            _listaStazioniRegione = New WGMenuLista()
            _listaStazioniRegione.ciclono = True
            _listaStazioniRegione.selezioneMenu = True
            _listaStazioniRegione.multiselezioneConsentita = False

            RiempiLista(_listaStazioniRegione, stazioni)
            ConfiguraOpzioniListaStazioni(_listaStazioniRegione, stazioni)

            _listaStazioniRegione.impostaTitolo("Radio " & nomeRegione)
            _listaStazioniRegione.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriStazioniRegione: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniRegione_ElementoSelezionato(id As String) Handles _listaStazioniRegione.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniRegione_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniRegione_escPremuto(sender As Object, e As EventArgs) Handles _listaStazioniRegione.escPremuto
        Try
            If _listaStazioniRegione IsNot Nothing Then
                _listaStazioniRegione.abbandona()
                _listaStazioniRegione = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniRegione_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniRegione_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaStazioniRegione.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaStazioniRegione, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniRegione_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Radio per genere"

    Private Sub ApriElencoGeneri(sender As Object, e As EventArgs)
        Try
            If Not AssicuraPaesiCaricati() Then Return
            _modalitaPaeseCorrente = MODALITA_GENERE
            MostraListaPaesi("Radio per genere, scegli il paese")
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriElencoGeneri: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriElencoGeneriPaese(codicePaese As String)
        Try
            Dim generi As List(Of RadioDb.GenereRadio)

            If String.Equals(codicePaese, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then
                If Not AssicuraDatiCaricati() Then Return
                generi = _db.OttieniGeneri(3)
            Else
                If Not AssicuraStazioniPaeseCaricate(codicePaese) Then Return
                generi = _db.OttieniGeneriPaese(codicePaese, 2)
            End If

            If generi Is Nothing OrElse generi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun genere disponibile per " & NomePaeseDaCodice(codicePaese) & ".")
                msg.ShowDialog()
                Return
            End If

            ChiudiListeStazioni()

            _listaGeneri = New WGMenuLista()
            _listaGeneri.ciclono = True
            _listaGeneri.selezioneMenu = True
            _listaGeneri.multiselezioneConsentita = False

            _listaGeneri.BeginUpdate()
            For i As Integer = 0 To generi.Count - 1
                Dim etichetta As String = generi(i).Nome & ", " & generi(i).Numero.ToString() & " radio"
                _listaGeneri.aggiungiElemento(etichetta, generi(i).Nome)
            Next
            _listaGeneri.EndUpdate()

            _listaGeneri.impostaTitolo("Radio per genere, " & NomePaeseDaCodice(codicePaese))
            _listaGeneri.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriElencoGeneriPaese: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaGeneri_ElementoSelezionato(id As String) Handles _listaGeneri.ElementoSelezionato
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            ApriStazioniGenere(id, _codicePaeseCorrente)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaGeneri_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaGeneri_escPremuto(sender As Object, e As EventArgs) Handles _listaGeneri.escPremuto
        Try
            If _listaGeneri IsNot Nothing Then
                _listaGeneri.abbandona()
                _listaGeneri = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaGeneri_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriStazioniGenere(nomeGenere As String, codicePaese As String)
        Try
            Dim stazioni As List(Of RadioDb.StazioneRadio)

            If String.Equals(codicePaese, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then
                stazioni = _db.OttieniStazioniGenere(nomeGenere)
            Else
                stazioni = _db.OttieniStazioniGenerePaese(codicePaese, nomeGenere)
            End If

            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio trovata per il genere " & nomeGenere & ".")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = ""

            If _listaStazioniGenere IsNot Nothing Then
                Try
                    _listaStazioniGenere.abbandona()
                Catch exAbb As Exception
                    _logger.ScriviLog("MenuRadio.ApriStazioniGenere abbandona: " & exAbb.Message)
                End Try
                _listaStazioniGenere = Nothing
            End If

            _listaStazioniGenere = New WGMenuLista()
            _listaStazioniGenere.ciclono = True
            _listaStazioniGenere.selezioneMenu = True
            _listaStazioniGenere.multiselezioneConsentita = False

            RiempiLista(_listaStazioniGenere, stazioni)
            ConfiguraOpzioniListaStazioni(_listaStazioniGenere, stazioni)

            _listaStazioniGenere.impostaTitolo("Radio genere " & nomeGenere & ", " & NomePaeseDaCodice(codicePaese))
            _listaStazioniGenere.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriStazioniGenere: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniGenere_ElementoSelezionato(id As String) Handles _listaStazioniGenere.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniGenere_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniGenere_escPremuto(sender As Object, e As EventArgs) Handles _listaStazioniGenere.escPremuto
        Try
            If _listaStazioniGenere IsNot Nothing Then
                _listaStazioniGenere.abbandona()
                _listaStazioniGenere = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniGenere_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniGenere_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaStazioniGenere.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaStazioniGenere, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniGenere_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Paesi"

    Private Function AssicuraPaesiCaricati() As Boolean
        Try
            If _db.IsPaesiCaricato Then Return True

            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando l'elenco dei paesi, attendi", False)
            Try
                Dim ok As Boolean = _db.CaricaPaesi(False)
                proc.Fine()
                If Not ok Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Non e' stato possibile scaricare l'elenco dei paesi. Controlla la connessione a internet e riprova.")
                    msg.ShowDialog()
                    Return False
                End If
                Return True
            Catch exInterno As Exception
                _logger.ScriviLog("MenuRadio.AssicuraPaesiCaricati interno: " & exInterno.ToString())
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuRadio.AssicuraPaesiCaricati fine: " & exFine.Message)
                End Try
                Return False
            End Try
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AssicuraPaesiCaricati: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function AssicuraStazioniPaeseCaricate(codicePaese As String) As Boolean
        Try
            If _db.IsPaeseCaricato(codicePaese) Then Return True

            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando le radio di questo paese, attendi", False)
            Try
                Dim ok As Boolean = _db.CaricaStazioniPaese(codicePaese, False)
                proc.Fine()
                If Not ok Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Non e' stato possibile scaricare le radio di questo paese. Controlla la connessione a internet e riprova.")
                    msg.ShowDialog()
                    Return False
                End If
                Return True
            Catch exInterno As Exception
                _logger.ScriviLog("MenuRadio.AssicuraStazioniPaeseCaricate interno: " & exInterno.ToString())
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuRadio.AssicuraStazioniPaeseCaricate fine: " & exFine.Message)
                End Try
                Return False
            End Try
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AssicuraStazioniPaeseCaricate: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub MostraListaPaesi(titolo As String)
        Try
            ChiudiListaPaesi()

            Dim escludiItalia As Boolean = String.Equals(_modalitaPaeseCorrente, MODALITA_INTERNAZIONALI, StringComparison.Ordinal)
            Dim paesi As List(Of RadioDb.PaeseRadio) = _db.OttieniPaesi(escludiItalia)

            If paesi Is Nothing OrElse paesi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Elenco paesi non disponibile al momento.")
                msg.ShowDialog()
                Return
            End If

            _listaPaesi = New WGMenuLista()
            _listaPaesi.ciclono = True
            _listaPaesi.selezioneMenu = True
            _listaPaesi.multiselezioneConsentita = False

            _listaPaesi.BeginUpdate()
            For i As Integer = 0 To paesi.Count - 1
                Dim nomeVisualizzato As String = paesi(i).Nome
                If String.Equals(paesi(i).Codice, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then
                    nomeVisualizzato = "Italia"
                End If
                _nomiPaesi(paesi(i).Codice) = nomeVisualizzato

                Dim etichetta As String = nomeVisualizzato & ", " & paesi(i).NumeroStazioni.ToString() & " radio"
                _listaPaesi.aggiungiElemento(etichetta, paesi(i).Codice)
            Next
            _listaPaesi.EndUpdate()

            _listaPaesi.impostaTitolo(titolo)
            _listaPaesi.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.MostraListaPaesi: " & ex.ToString())
        End Try
    End Sub

    Private Function NomePaeseDaCodice(codicePaese As String) As String
        Try
            If String.IsNullOrWhiteSpace(codicePaese) Then Return ""
            If String.Equals(codicePaese, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then Return "Italia"
            If _nomiPaesi.ContainsKey(codicePaese) Then Return _nomiPaesi(codicePaese)
            Return codicePaese
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.NomePaeseDaCodice: " & ex.Message)
            Return codicePaese
        End Try
    End Function

    Private Sub _listaPaesi_ElementoSelezionato(id As String) Handles _listaPaesi.ElementoSelezionato
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            _codicePaeseCorrente = id.Trim().ToUpperInvariant()

            Select Case _modalitaPaeseCorrente
                Case MODALITA_INTERNAZIONALI
                    ApriStazioniInternazionali(_codicePaeseCorrente)
                Case MODALITA_GENERE
                    ApriElencoGeneriPaese(_codicePaeseCorrente)
                Case MODALITA_RICERCA
                    If String.Equals(_codicePaeseCorrente, RadioDb.CODICE_ITALIA, StringComparison.OrdinalIgnoreCase) Then
                        If Not AssicuraDatiCaricati() Then Return
                    Else
                        If Not AssicuraStazioniPaeseCaricate(_codicePaeseCorrente) Then Return
                    End If
                    MostraCampoRicerca()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPaesi_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPaesi_escPremuto(sender As Object, e As EventArgs) Handles _listaPaesi.escPremuto
        Try
            ChiudiListaPaesi()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPaesi_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiudiListaPaesi()
        Try
            If _listaPaesi IsNot Nothing Then
                Try
                    _listaPaesi.abbandona()
                Catch exAbb As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListaPaesi: " & exAbb.Message)
                End Try
                _listaPaesi = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiudiListaPaesi: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Radio internazionali"

    Private Sub ApriRadioInternazionali(sender As Object, e As EventArgs)
        Try
            If Not AssicuraPaesiCaricati() Then Return
            _modalitaPaeseCorrente = MODALITA_INTERNAZIONALI
            MostraListaPaesi("Radio internazionali, scegli il paese")
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriRadioInternazionali: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApriStazioniInternazionali(codicePaese As String)
        Try
            If Not AssicuraStazioniPaeseCaricate(codicePaese) Then Return

            Dim stazioni As List(Of RadioDb.StazioneRadio) = _db.OttieniStazioniPaese(codicePaese)
            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio trovata per " & NomePaeseDaCodice(codicePaese) & ".")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = NomePaeseDaCodice(codicePaese)

            ChiudiListeStazioni()

            _listaStazioniInternazionali = New WGMenuLista()
            _listaStazioniInternazionali.ciclono = True
            _listaStazioniInternazionali.selezioneMenu = True
            _listaStazioniInternazionali.multiselezioneConsentita = False

            RiempiLista(_listaStazioniInternazionali, stazioni)
            ConfiguraOpzioniListaStazioni(_listaStazioniInternazionali, stazioni)

            _listaStazioniInternazionali.impostaTitolo("Radio " & NomePaeseDaCodice(codicePaese))
            _listaStazioniInternazionali.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriStazioniInternazionali: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniInternazionali_ElementoSelezionato(id As String) Handles _listaStazioniInternazionali.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniInternazionali_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniInternazionali_escPremuto(sender As Object, e As EventArgs) Handles _listaStazioniInternazionali.escPremuto
        Try
            If _listaStazioniInternazionali IsNot Nothing Then
                _listaStazioniInternazionali.abbandona()
                _listaStazioniInternazionali = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniInternazionali_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaStazioniInternazionali_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaStazioniInternazionali.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaStazioniInternazionali, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaStazioniInternazionali_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Scrivi indirizzo"

    Private Sub ApriScriviIndirizzo(sender As Object, e As EventArgs)
        Try
            MostraCampoIndirizzo()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriScriviIndirizzo: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraCampoIndirizzo()
        Try
            RilasciaCampiIndirizzo()

            _campiIndirizzo = New Campi()
            _campiIndirizzo.Aggiungi("Nome della radio", ID_CAMPO_NOME_RADIO, True, False, "")
            _campiIndirizzo.Aggiungi("Indirizzo dello stream, ad esempio un link che inizia con http", ID_CAMPO_URL_RADIO, True, False, "")

            AddHandler _campiIndirizzo.Fine, AddressOf IndirizzoConfermato
            AddHandler _campiIndirizzo.Chiusura, AddressOf IndirizzoAnnullato

            _campiIndirizzo.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.MostraCampoIndirizzo: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile aprire il campo per l'indirizzo.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub IndirizzoAnnullato(sender As Object, e As EventArgs)
        Try
            RilasciaCampiIndirizzo()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.IndirizzoAnnullato: " & ex.ToString())
        End Try
    End Sub

    Private Sub RilasciaCampiIndirizzo()
        Try
            If _campiIndirizzo Is Nothing Then Return

            Try
                RemoveHandler _campiIndirizzo.Fine, AddressOf IndirizzoConfermato
                RemoveHandler _campiIndirizzo.Chiusura, AddressOf IndirizzoAnnullato
            Catch exHandler As Exception
                _logger.ScriviLog("MenuRadio.RilasciaCampiIndirizzo handler: " & exHandler.Message)
            End Try

            Try
                
            Catch exDispose As Exception
                _logger.ScriviLog("MenuRadio.RilasciaCampiIndirizzo dispose: " & exDispose.Message)
            End Try

            _campiIndirizzo = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RilasciaCampiIndirizzo: " & ex.ToString())
        End Try
    End Sub

    Private Sub IndirizzoConfermato(sender As Object, e As EventArgs)
        Try
            Dim nome As String = ""
            Dim url As String = ""

            Try
                If _campiIndirizzo IsNot Nothing Then
                    nome = _campiIndirizzo.RecuperaValore(ID_CAMPO_NOME_RADIO)
                    url = _campiIndirizzo.RecuperaValore(ID_CAMPO_URL_RADIO)
                End If
            Catch exValore As Exception
                _logger.ScriviLog("MenuRadio.IndirizzoConfermato lettura: " & exValore.Message)
            End Try

            RilasciaCampiIndirizzo()

            If nome Is Nothing Then nome = ""
            If url Is Nothing Then url = ""
            nome = nome.Trim()
            url = url.Trim()

            If String.IsNullOrWhiteSpace(nome) OrElse String.IsNullOrWhiteSpace(url) Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Devi indicare sia il nome che l'indirizzo della radio.")
                msgVuoto.ShowDialog()
                Return
            End If

            Dim esistente As RadioDb.StazioneRadio = _db.TrovaPerUrl(url)
            If esistente IsNot Nothing Then
                Dim msgEsiste As New WGMessaggi()
                msgEsiste.Messaggio("Questo indirizzo e' gia' presente nel database con il nome " & esistente.Nome & ".")
                msgEsiste.ShowDialog()
                Return
            End If

            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto verificando l'indirizzo, attendi", False)
            Dim compatibile As Boolean = _db.VerificaCompatibilitaIndirizzo(url)
            proc.Fine()

            If Not compatibile Then
                Dim msgNo As New WGMessaggi()
                msgNo.Messaggio("L'indirizzo non sembra essere uno stream audio compatibile con mpv. La radio non e' stata aggiunta.")
                msgNo.ShowDialog()
                Return
            End If

            Dim nuova As RadioDb.StazioneRadio = _db.AggiungiStazioneManuale(nome, url, "", RadioDb.CODICE_ITALIA)
            If nuova Is Nothing Then
                Dim msgErr As New WGMessaggi()
                msgErr.Messaggio("Non e' stato possibile aggiungere la radio.")
                msgErr.ShowDialog()
                Return
            End If

            AggiornaListaAttivaConNuovaStazione(nuova)

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto(nuova.Nome & " aggiunta al database.")
            End If

            ChiediInvioRadioBrowser(nuova)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.IndirizzoConfermato: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiornaListaAttivaConNuovaStazione(stazione As RadioDb.StazioneRadio)
        Try
            If stazione Is Nothing Then Return

            Dim lista As WGMenuLista = ListaStazioniAttiva()
            If lista Is Nothing OrElse lista.IsDisposed Then Return

            Dim elementi As List(Of String) = lista.OttieniElementi()
            Dim etichetta As String = CreaEtichetta(stazione)

            If elementi IsNot Nothing AndAlso elementi.Contains(etichetta) Then Return

            lista.aggiungiElemento(etichetta, stazione.Id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AggiornaListaAttivaConNuovaStazione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiediInvioRadioBrowser(stazione As RadioDb.StazioneRadio)
        Try
            _stazioneManualeInAttesa = stazione
            Dim nome As String = "questa radio"
            If stazione IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(stazione.Nome) Then nome = stazione.Nome

            Dim menu As New WGMenu()
            menu.aggiungi("Si, invia " & nome & " a radio-browser.info", AddressOf ConfermaInvioRadioBrowser)
            menu.aggiungi("No, non inviare", AddressOf AnnullaInvioRadioBrowser)
            menu.avvia("Condividere questa radio su radio-browser.info?", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiediInvioRadioBrowser: " & ex.ToString())
        End Try
    End Sub

    Private Sub ConfermaInvioRadioBrowser(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = _stazioneManualeInAttesa
            If stazione IsNot Nothing Then
                Dim inviata As Boolean = _db.InviaARadioBrowser(stazione.Nome, stazione.UrlRiproduzione, stazione.Sito, "")
                Dim msgInvio As New WGMessaggi()
                If inviata Then
                    msgInvio.Messaggio("Radio inviata a radio-browser.info.")
                Else
                    msgInvio.Messaggio("Non e' stato possibile inviare la radio a radio-browser.info.")
                End If
                msgInvio.ShowDialog()
            End If
            ChiediAscoltoSubito(stazione)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ConfermaInvioRadioBrowser: " & ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaInvioRadioBrowser(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = _stazioneManualeInAttesa
            ChiediAscoltoSubito(stazione)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AnnullaInvioRadioBrowser: " & ex.ToString())
        End Try
    End Sub

    Private Sub ChiediAscoltoSubito(stazione As RadioDb.StazioneRadio)
        Try
            _stazioneManualeInAttesa = stazione
            Dim nomeStazione As String = "questa radio"
            If stazione IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(stazione.Nome) Then nomeStazione = stazione.Nome

            Dim menu As New WGMenu()
            menu.aggiungi("Si, ascolta subito " & nomeStazione, AddressOf ConfermaAscoltoSubito)
            menu.aggiungi("No, torna al menu radio", AddressOf AnnullaAscoltoSubito)
            menu.avvia("Ascoltare subito questa radio?", True)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiediAscoltoSubito: " & ex.ToString())
        End Try
    End Sub

    Private Sub ConfermaAscoltoSubito(sender As Object, e As EventArgs)
        Try
            If _stazioneManualeInAttesa IsNot Nothing Then
                RiproduciStazione(_stazioneManualeInAttesa)
            End If
            _stazioneManualeInAttesa = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ConfermaAscoltoSubito: " & ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaAscoltoSubito(sender As Object, e As EventArgs)
        Try
            _stazioneManualeInAttesa = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AnnullaAscoltoSubito: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Ascolto radio attuale"

    Private Sub ApriAscoltoAttuale(sender As Object, e As EventArgs)
        Try
            If _stazioneInAscolto Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non stai ascoltando nessuna radio al momento.")
                msg.ShowDialog()
                Return
            End If

            Dim stazione As RadioDb.StazioneRadio = _stazioneInAscolto
            _stazioneInfoCorrente = stazione

            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto recuperando le informazioni sul brano in onda, attendi", False)
            Dim titoloBrano As String = RecuperaNowPlaying(stazione.UrlRiproduzione)
            proc.Fine()

            Dim menu As New WGMenu()
            menu.aggiungi("Radio: " & stazione.Nome, AddressOf VoceInformativaSelezionata)

            If Not String.IsNullOrWhiteSpace(titoloBrano) Then
                menu.aggiungi("In onda: " & titoloBrano, AddressOf VoceInformativaSelezionata)
            Else
                menu.aggiungi("In onda: informazione non disponibile per questa radio", AddressOf VoceInformativaSelezionata)
            End If

            If Not String.IsNullOrWhiteSpace(stazione.Sito) Then
                menu.aggiungi("Sito web, seleziona per copiare: " & stazione.Sito, AddressOf VoceCopiaSitoInfo)
            End If
            menu.aggiungi("Indirizzo streaming, seleziona per copiare: " & stazione.UrlRiproduzione, AddressOf VoceCopiaIndirizzoInfo)

            menu.avvia("Ascolto attuale")
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriAscoltoAttuale: " & ex.ToString())
        End Try
    End Sub

    Private Function RecuperaNowPlaying(url As String) As String
        Dim risposta As HttpWebResponse = Nothing
        Dim flusso As Stream = Nothing
        Try
            If String.IsNullOrWhiteSpace(url) Then Return ""

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = 8000
            richiesta.ReadWriteTimeout = 8000
            richiesta.UserAgent = "WinGabriel/1.0"
            richiesta.Headers.Add("Icy-MetaData", "1")
            richiesta.AllowAutoRedirect = True

            risposta = CType(richiesta.GetResponse(), HttpWebResponse)

            Dim intervalloTesto As String = risposta.Headers("icy-metaint")
            Dim intervallo As Integer = 0
            ' Un intervallo enorme farebbe allocare un buffer da centinaia di megabyte e può sollevare OutOfMemoryException.
            If String.IsNullOrWhiteSpace(intervalloTesto) OrElse Not Integer.TryParse(intervalloTesto, intervallo) OrElse intervallo <= 0 OrElse intervallo > 262144 Then
                Return ""
            End If

            flusso = risposta.GetResponseStream()
            If flusso Is Nothing Then Return ""

            Dim bufferAudio(intervallo - 1) As Byte
            Dim lettiAudio As Integer = LeggiEsatto(flusso, bufferAudio, intervallo)
            If lettiAudio < intervallo Then Return ""

            Dim byteLunghezza As Integer = flusso.ReadByte()
            If byteLunghezza <= 0 Then Return ""

            Dim lunghezzaMetadati As Integer = byteLunghezza * 16
            Dim bufferMetadati(lunghezzaMetadati - 1) As Byte
            Dim lettiMetadati As Integer = LeggiEsatto(flusso, bufferMetadati, lunghezzaMetadati)
            If lettiMetadati <= 0 Then Return ""

            Dim testo As String = Encoding.UTF8.GetString(bufferMetadati, 0, lettiMetadati)
            Dim indiceInizio As Integer = testo.IndexOf("StreamTitle='", StringComparison.OrdinalIgnoreCase)
            If indiceInizio < 0 Then Return ""
            indiceInizio += "StreamTitle='".Length

            Dim indiceFine As Integer = testo.IndexOf("';", indiceInizio, StringComparison.Ordinal)
            If indiceFine < 0 Then Return ""

            Return testo.Substring(indiceInizio, indiceFine - indiceInizio).Trim()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RecuperaNowPlaying: " & ex.Message)
            Return ""
        Finally
            Try
                If flusso IsNot Nothing Then flusso.Close()
            Catch exFlusso As Exception
                _logger.ScriviLog("MenuRadio.RecuperaNowPlaying chiusura flusso: " & exFlusso.Message)
            End Try
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exRisposta As Exception
                _logger.ScriviLog("MenuRadio.RecuperaNowPlaying chiusura risposta: " & exRisposta.Message)
            End Try
        End Try
    End Function

    Private Function LeggiEsatto(flusso As Stream, buffer As Byte(), quantita As Integer) As Integer
        Try
            Dim totaleLetti As Integer = 0
            While totaleLetti < quantita
                Dim letti As Integer = flusso.Read(buffer, totaleLetti, quantita - totaleLetti)
                If letti <= 0 Then Exit While
                totaleLetti += letti
            End While
            Return totaleLetti
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.LeggiEsatto: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub VoceInformativaSelezionata(sender As Object, e As EventArgs)
    End Sub

    Private Sub VoceCopiaSitoInfo(sender As Object, e As EventArgs)
        Try
            If _stazioneInfoCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_stazioneInfoCorrente.Sito) Then
                Clipboard.SetText(_stazioneInfoCorrente.Sito)
                Dim lettura As sintesivocale = sintesivocale.GetInstance()
                If lettura IsNot Nothing Then lettura.LeggiTesto("Sito web copiato negli appunti.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.VoceCopiaSitoInfo: " & ex.ToString())
        End Try
    End Sub

    Private Sub VoceCopiaIndirizzoInfo(sender As Object, e As EventArgs)
        Try
            If _stazioneInfoCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_stazioneInfoCorrente.UrlRiproduzione) Then
                Clipboard.SetText(_stazioneInfoCorrente.UrlRiproduzione)
                Dim lettura As sintesivocale = sintesivocale.GetInstance()
                If lettura IsNot Nothing Then lettura.LeggiTesto("Indirizzo copiato negli appunti.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.VoceCopiaIndirizzoInfo: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Radio preferite"

    Private Sub ApriPreferite(sender As Object, e As EventArgs)
        Try
            If Not AssicuraDatiCaricati() Then Return

            Dim voci As List(Of RadioTvDatabase.VoceRaccolta) = _raccolta.OttieniPreferiti(RadioTvDatabase.TipoElemento.Radio)
            Dim stazioni As List(Of RadioDb.StazioneRadio) = RisolviStazioniDaVoci(voci)

            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai ancora nessuna radio tra i preferiti.")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = ""

            ChiudiListeStazioni()

            _listaPreferite = New WGMenuLista()
            _listaPreferite.ciclono = True
            _listaPreferite.selezioneMenu = True
            _listaPreferite.multiselezioneConsentita = False

            RiempiLista(_listaPreferite, stazioni)
            ConfiguraOpzioniListaStazioni(_listaPreferite, stazioni)

            _listaPreferite.impostaTitolo("Radio preferite")
            _listaPreferite.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriPreferite: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferite_ElementoSelezionato(id As String) Handles _listaPreferite.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPreferite_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferite_escPremuto(sender As Object, e As EventArgs) Handles _listaPreferite.escPremuto
        Try
            If _listaPreferite IsNot Nothing Then
                _listaPreferite.abbandona()
                _listaPreferite = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPreferite_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaPreferite_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaPreferite.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaPreferite, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaPreferite_VoceCambiata: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Radio recenti"

    Private Sub ApriRecenti(sender As Object, e As EventArgs)
        Try
            If Not AssicuraDatiCaricati() Then Return

            Dim voci As List(Of RadioTvDatabase.VoceRaccolta) = _raccolta.OttieniRecenti(RadioTvDatabase.TipoElemento.Radio)
            Dim stazioni As List(Of RadioDb.StazioneRadio) = RisolviStazioniDaVoci(voci)

            If stazioni Is Nothing OrElse stazioni.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai ancora ascoltato nessuna radio.")
                msg.ShowDialog()
                Return
            End If

            _stazioniCorrenti = stazioni
            _regioneCorrente = ""

            ChiudiListeStazioni()

            _listaRecenti = New WGMenuLista()
            _listaRecenti.ciclono = True
            _listaRecenti.selezioneMenu = True
            _listaRecenti.multiselezioneConsentita = False

            RiempiLista(_listaRecenti, stazioni)
            ConfiguraOpzioniListaStazioni(_listaRecenti, stazioni)
            _listaRecenti.aggiungiOpzione("Rimuovi dai recenti", AddressOf OpzioneRimuoviRecente)

            _listaRecenti.impostaTitolo("Radio ascoltate di recente")
            _listaRecenti.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriRecenti: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneRimuoviRecente(sender As Object, e As EventArgs)
        Try
            Dim id As String = IdSelezionatoCorrente()
            If String.IsNullOrWhiteSpace(id) Then Return

            _raccolta.RimuoviRecente(RadioTvDatabase.TipoElemento.Radio, id)

            If _listaRecenti IsNot Nothing AndAlso Not _listaRecenti.IsDisposed Then
                _listaRecenti.RimuoviElemento(id)
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneRimuoviRecente: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_ElementoSelezionato(id As String) Handles _listaRecenti.ElementoSelezionato
        Try
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRecenti_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_escPremuto(sender As Object, e As EventArgs) Handles _listaRecenti.escPremuto
        Try
            If _listaRecenti IsNot Nothing Then
                _listaRecenti.abbandona()
                _listaRecenti = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRecenti_escPremuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaRecenti_VoceCambiata(sender As Object, e As VoceCambiataEventArgs) Handles _listaRecenti.VoceCambiata
        Try
            AggiornaOpzionePreferito(_listaRecenti, e.IdVoce)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaRecenti_VoceCambiata: " & ex.Message)
        End Try
    End Sub

    Private Function RisolviStazioniDaVoci(voci As List(Of RadioTvDatabase.VoceRaccolta)) As List(Of RadioDb.StazioneRadio)
        Dim risultato As New List(Of RadioDb.StazioneRadio)
        Try
            If voci Is Nothing Then Return risultato

            For i As Integer = 0 To voci.Count - 1
                Dim stazione As RadioDb.StazioneRadio = _db.TrovaPerId(voci(i).Id)
                If stazione Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(stazione.UrlRiproduzione) Then Continue For
                risultato.Add(stazione)
            Next
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RisolviStazioniDaVoci: " & ex.ToString())
        End Try
        Return risultato
    End Function

#End Region

#Region "Opzioni comuni e preferito dinamico"

    Private Sub ConfiguraOpzioniListaStazioni(lista As WGMenuLista, stazioni As List(Of RadioDb.StazioneRadio))
        Try
            If lista Is Nothing Then Return

            lista.aggiungiOpzione("Riproduci", AddressOf OpzioneRiproduci)
            lista.aggiungiOpzione("Programmazione", AddressOf OpzioneProgrammazione)
            lista.aggiungiOpzione("Copia indirizzo", AddressOf OpzioneCopiaIndirizzo)
            lista.aggiungiOpzione("Copia sito web", AddressOf OpzioneCopiaSito)
            lista.aggiungiOpzione("Informazioni", AddressOf OpzioneInformazioni)

            Dim testoIniziale As String = TESTO_AGGIUNGI_PREFERITI
            If stazioni IsNot Nothing AndAlso stazioni.Count > 0 Then
                testoIniziale = TestoOpzionePreferito(stazioni(0).Id)
            End If

            lista.aggiungiOpzione(testoIniziale, AddressOf OpzionePreferito)
            _testoOpzionePreferitoAttuale = testoIniziale
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ConfiguraOpzioniListaStazioni: " & ex.ToString())
        End Try
    End Sub

    Private Function TestoOpzionePreferito(id As String) As String
        Try
            If _raccolta.EPreferito(RadioTvDatabase.TipoElemento.Radio, id) Then Return TESTO_RIMUOVI_PREFERITI
            Return TESTO_AGGIUNGI_PREFERITI
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.TestoOpzionePreferito: " & ex.Message)
            Return TESTO_AGGIUNGI_PREFERITI
        End Try
    End Function

    Private Sub AggiornaOpzionePreferito(lista As WGMenuLista, id As String)
        Try
            If lista Is Nothing OrElse lista.IsDisposed Then Return
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim nuovoTesto As String = TestoOpzionePreferito(id)
            If String.Equals(nuovoTesto, _testoOpzionePreferitoAttuale, StringComparison.Ordinal) Then Return

            lista.sostituisciOpzione(_testoOpzionePreferitoAttuale, nuovoTesto, AddressOf OpzionePreferito)
            _testoOpzionePreferitoAttuale = nuovoTesto
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.AggiornaOpzionePreferito: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzionePreferito(sender As Object, e As EventArgs)
        Try
            Dim lista As WGMenuLista = ListaStazioniAttiva()
            dim elementi as list(of string) = lista.OttieniElementi()
dim id as string = elementi(0)

            Dim stazione As RadioDb.StazioneRadio = TrovaStazione(id)
            If stazione Is Nothing Then Return

            Dim nuovoStato As Boolean = _raccolta.CommutaPreferito(RadioTvDatabase.TipoElemento.Radio, id, stazione.Nome)
            Dim nuovoTesto As String = TESTO_AGGIUNGI_PREFERITI
            If nuovoStato Then nuovoTesto = TESTO_RIMUOVI_PREFERITI

            If lista IsNot Nothing Then
                lista.sostituisciOpzione(_testoOpzionePreferitoAttuale, nuovoTesto, AddressOf OpzionePreferito)
                _testoOpzionePreferitoAttuale = nuovoTesto

                If lista Is _listaPreferite AndAlso Not nuovoStato Then
                    lista.RimuoviElemento(id)
                End If
            End If

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                If nuovoStato Then
                    lettura.LeggiTesto(stazione.Nome & " aggiunta ai preferiti.")
                Else
                    lettura.LeggiTesto(stazione.Nome & " rimossa dai preferiti.")
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzionePreferito: " & ex.ToString())
        End Try
    End Sub

    Private Function ListaStazioniAttiva() As WGMenuLista
        Try
            If _listaRisultati IsNot Nothing AndAlso Not _listaRisultati.IsDisposed Then Return _listaRisultati
            If _listaStazioniGenere IsNot Nothing AndAlso Not _listaStazioniGenere.IsDisposed Then Return _listaStazioniGenere
            If _listaStazioniRegione IsNot Nothing AndAlso Not _listaStazioniRegione.IsDisposed Then Return _listaStazioniRegione
            If _listaStazioniInternazionali IsNot Nothing AndAlso Not _listaStazioniInternazionali.IsDisposed Then Return _listaStazioniInternazionali
            If _listaPreferite IsNot Nothing AndAlso Not _listaPreferite.IsDisposed Then Return _listaPreferite
            If _listaRecenti IsNot Nothing AndAlso Not _listaRecenti.IsDisposed Then Return _listaRecenti
            If _listaPopolari IsNot Nothing AndAlso Not _listaPopolari.IsDisposed Then Return _listaPopolari
            If _listaTutte IsNot Nothing AndAlso Not _listaTutte.IsDisposed Then Return _listaTutte
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ListaStazioniAttiva: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub OpzioneRiproduci(sender As Object, e As EventArgs)
        Try
            Dim id As String = IdSelezionatoCorrente()
            If String.IsNullOrWhiteSpace(id) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio selezionata.")
                msg.ShowDialog()
                Return
            End If
            RiproduciPerId(id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneRiproduci: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneCopiaIndirizzo(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = StazioneSelezionata()
            If stazione Is Nothing OrElse String.IsNullOrWhiteSpace(stazione.UrlRiproduzione) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Indirizzo non disponibile per questa radio.")
                msg.ShowDialog()
                Return
            End If

            Clipboard.SetText(stazione.UrlRiproduzione)

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto("Indirizzo di " & stazione.Nome & " copiato negli appunti.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneCopiaIndirizzo: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile copiare l'indirizzo.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub OpzioneCopiaSito(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = StazioneSelezionata()
            If stazione Is Nothing OrElse String.IsNullOrWhiteSpace(stazione.Sito) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Sito web non disponibile per questa radio.")
                msg.ShowDialog()
                Return
            End If

            Clipboard.SetText(stazione.Sito)

            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If lettura IsNot Nothing Then
                lettura.LeggiTesto("Sito web di " & stazione.Nome & " copiato negli appunti.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneCopiaSito: " & ex.ToString())
        End Try
    End Sub

    Private Sub OpzioneInformazioni(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = StazioneSelezionata()
            If stazione Is Nothing Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Nessuna radio selezionata.")
                avviso.ShowDialog()
                Return
            End If

            _stazioneInfoCorrente = stazione

            Dim menu As New WGMenu()
            menu.aggiungi("Nome: " & stazione.Nome, AddressOf VoceInformativaSelezionata)

            If Not String.IsNullOrWhiteSpace(stazione.Regione) Then
                menu.aggiungi("Regione o paese: " & stazione.Regione, AddressOf VoceInformativaSelezionata)
            End If
            If Not String.IsNullOrWhiteSpace(stazione.Lingua) Then
                menu.aggiungi("Lingua: " & stazione.Lingua, AddressOf VoceInformativaSelezionata)
            End If
            If Not String.IsNullOrWhiteSpace(stazione.Codec) Then
                menu.aggiungi("Formato: " & stazione.Codec, AddressOf VoceInformativaSelezionata)
            End If
            If stazione.Bitrate > 0 Then
                menu.aggiungi("Qualita': " & stazione.Bitrate.ToString() & " kilobit al secondo", AddressOf VoceInformativaSelezionata)
            End If
            If Not String.IsNullOrWhiteSpace(stazione.Tag) Then
                menu.aggiungi("Generi: " & stazione.Tag.Replace(",", ", "), AddressOf VoceInformativaSelezionata)
            End If

            menu.aggiungi("Voti: " & stazione.Voti.ToString(), AddressOf VoceInformativaSelezionata)
            menu.aggiungi("Ascolti totali: " & stazione.Ascolti.ToString(), AddressOf VoceInformativaSelezionata)
            menu.aggiungi("Stato: " & If(stazione.Attiva, "attiva", "segnalata in errore"), AddressOf VoceInformativaSelezionata)

            If Not String.IsNullOrWhiteSpace(stazione.Sito) Then
                menu.aggiungi("Sito web, seleziona per copiare: " & stazione.Sito, AddressOf VoceCopiaSitoInfo)
            End If
            menu.aggiungi("Indirizzo streaming, seleziona per copiare: " & stazione.UrlRiproduzione, AddressOf VoceCopiaIndirizzoInfo)

            menu.avvia("Informazioni " & stazione.Nome)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneInformazioni: " & ex.ToString())
        End Try
    End Sub

    ' La programmazione della radio selezionata: si scarica con WGProcessoInCorso, e solo a download finito si apre l'elenco dei programmi;
    ' se non c'è niente da mostrare o il download non riesce, c'è solo il messaggio.
    Private Sub OpzioneProgrammazione(sender As Object, e As EventArgs)
        Try
            Dim stazione As RadioDb.StazioneRadio = StazioneSelezionata()
            If stazione Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio selezionata.")
                msg.ShowDialog()
                Return
            End If

            If Not RadioProgrammazione.ProgrammazioneDisponibile(stazione.Nome) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per " & stazione.Nome & " la programmazione non è disponibile.")
                msg.ShowDialog()
                Return
            End If

            Dim nomeRadio As String = stazione.Nome
            Dim programmi As List(Of TvProgrammazione.VoceProgramma) = Nothing
            Dim proc As New WGProcessoInCorso()
            proc.Avvia("Sto scaricando la programmazione di " & stazione.Nome & ", attendi.", False)
            Try
                programmi = Task.Run(Function() RadioProgrammazione.OttieniAsync(nomeRadio)).GetAwaiter().GetResult()
            Catch exScarico As Exception
                _logger.ScriviLog("MenuRadio.OpzioneProgrammazione scarico: " & exScarico.ToString())
                programmi = Nothing
            Finally
                Try
                    proc.Fine()
                Catch exFine As Exception
                    _logger.ScriviLog("MenuRadio.OpzioneProgrammazione fine: " & exFine.Message)
                End Try
            End Try

            If programmi Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile scaricare la programmazione. Controlla la connessione a internet e riprova.")
                msg.ShowDialog()
                Return
            End If

            If programmi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ho trovato la programmazione di " & stazione.Nome & ": nessuna fonte la pubblica oppure non ci sono programmi.")
                msg.ShowDialog()
                Return
            End If

            ApriProgrammazione(stazione.Id, stazione.Nome, programmi)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneProgrammazione: " & ex.ToString())
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile mostrare la programmazione.")
            msg.ShowDialog()
        End Try
    End Sub

    Private Sub ApriProgrammazione(idStazione As String, nomeRadio As String, programmi As List(Of TvProgrammazione.VoceProgramma))
        Try
            ChiudiListaProgrammi()

            _programmiCorrenti = programmi
            _idStazioneProgrammi = idStazione
            _nomeStazioneProgrammi = nomeRadio

            _listaProgrammi = New WGMenuLista()
            _listaProgrammi.ciclono = True
            _listaProgrammi.selezioneMenu = True
            _listaProgrammi.multiselezioneConsentita = False

            _listaProgrammi.BeginUpdate()
            For i As Integer = 0 To programmi.Count - 1
                _listaProgrammi.aggiungiElemento(EtichettaProgramma(programmi(i), i = 0), i.ToString())
            Next
            _listaProgrammi.EndUpdate()

            _listaProgrammi.impostaTitolo("Programmazione di " & nomeRadio)
            _listaProgrammi.aggiungiOpzione("Avvisami quando inizia", AddressOf OpzioneAvvisamiProgramma)
            _listaProgrammi.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ApriProgrammazione: " & ex.ToString())
        End Try
    End Sub

    Private Function EtichettaProgramma(p As TvProgrammazione.VoceProgramma, inOnda As Boolean) As String
        Try
            Dim ora As String = p.Inizio.ToString("HH:mm")
            If inOnda AndAlso p.Inizio <= DateTime.Now Then Return "In onda ora, dalle " & ora & ", " & p.Titolo
            If p.Inizio.Date = DateTime.Today Then Return "Alle " & ora & ", " & p.Titolo
            If p.Inizio.Date = DateTime.Today.AddDays(1) Then Return "Domani alle " & ora & ", " & p.Titolo
            Return p.Inizio.ToString("dddd d MMMM") & " alle " & ora & ", " & p.Titolo
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.EtichettaProgramma: " & ex.Message)
            Return "Programma"
        End Try
    End Function

    ' Invio su un programma: un menu con le informazioni e, se non è ancora iniziato, il promemoria; se è in onda, si può ascoltare la radio.
    Private Sub _listaProgrammi_ElementoSelezionato(id As String) Handles _listaProgrammi.ElementoSelezionato
        Try
            Dim indice As Integer
            If Not Integer.TryParse(id, indice) Then Return
            If indice < 0 OrElse indice >= _programmiCorrenti.Count Then Return

            Dim p As TvProgrammazione.VoceProgramma = _programmiCorrenti(indice)
            Dim idStazione As String = _idStazioneProgrammi
            Dim nomeRadio As String = _nomeStazioneProgrammi

            Dim menu As New WGMenu()
            menu.aggiungi("Informazioni", Sub() MostraInformazioniProgramma(p))

            If p.Inizio > DateTime.Now Then
                If TvPromemoria.Esiste(idStazione, p.Inizio, TvPromemoria.TIPO_RADIO) Then
                    menu.aggiungi("Rimuovi il promemoria", Sub() RimuoviPromemoria(idStazione, p))
                Else
                    menu.aggiungi("Imposta un promemoria", Sub() ImpostaPromemoria(idStazione, nomeRadio, p))
                End If
            Else
                menu.aggiungi("Ascolta la radio adesso", Sub() TvPromemoria.AvviaRadio(idStazione))
            End If

            menu.avvia(p.Titolo)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaProgrammi_ElementoSelezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraInformazioniProgramma(p As TvProgrammazione.VoceProgramma)
        Try
            Dim testo As String = p.Titolo & ", dalle " & p.Inizio.ToString("HH:mm") & " alle " & p.Fine.ToString("HH:mm") & "."
            If Not String.IsNullOrWhiteSpace(p.Descrizione) Then testo = testo & " " & p.Descrizione

            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.MostraInformazioniProgramma: " & ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaPromemoria(idStazione As String, nomeRadio As String, p As TvProgrammazione.VoceProgramma)
        Try
            AzioniProgramma.ImpostaPromemoria(TvPromemoria.TIPO_RADIO, idStazione, nomeRadio, "", p)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ImpostaPromemoria: " & ex.ToString())
        End Try
    End Sub

    Private Sub RimuoviPromemoria(idStazione As String, p As TvProgrammazione.VoceProgramma)
        AzioniProgramma.RimuoviPromemoria(TvPromemoria.TIPO_RADIO, idStazione, p)
    End Sub

    ' Opzione "Avvisami quando inizia" dell'elenco dei programmi: un promemoria solo per quella volta.
    Private Sub OpzioneAvvisamiProgramma(sender As Object, e As EventArgs)
        Try
            If _listaProgrammi Is Nothing Then Return
            Dim indice As Integer
            If Not Integer.TryParse(_listaProgrammi.ValoreSelezionato, indice) OrElse indice < 0 OrElse indice >= _programmiCorrenti.Count Then Return
            AzioniProgramma.AvvisamiQuandoInizia(TvPromemoria.TIPO_RADIO, _idStazioneProgrammi, _nomeStazioneProgrammi, _programmiCorrenti(indice))
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.OpzioneAvvisamiProgramma: " & ex.ToString())
        End Try
    End Sub

    Private Sub _listaProgrammi_escPremuto(sender As Object, e As EventArgs) Handles _listaProgrammi.escPremuto
        Try
            ChiudiListaProgrammi()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio._listaProgrammi_escPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListaProgrammi()
        Try
            If _listaProgrammi Is Nothing Then Return
            Try
                _listaProgrammi.abbandona()
            Catch exAbb As Exception
                _logger.ScriviLog("MenuRadio.ChiudiListaProgrammi abbandona: " & exAbb.Message)
            End Try
            _listaProgrammi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiudiListaProgrammi: " & ex.Message)
        End Try
    End Sub

    Private Function IdSelezionatoCorrente() As String
        Try
            Dim lista As WGMenuLista = ListaStazioniAttiva()
            If lista Is Nothing Then Return ""
            Dim v As String = lista.ValoreSelezionato
            If String.IsNullOrWhiteSpace(v) Then Return ""
            Return v
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.IdSelezionatoCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function StazioneSelezionata() As RadioDb.StazioneRadio
        Try
            Return TrovaStazione(IdSelezionatoCorrente())
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.StazioneSelezionata: " & ex.ToString())
            Return Nothing
        End Try
    End Function

#End Region

#Region "Riproduzione"

    Private Function TrovaStazione(id As String) As RadioDb.StazioneRadio
        Try
            If String.IsNullOrWhiteSpace(id) Then Return Nothing

            Dim stazione As RadioDb.StazioneRadio = _db.TrovaPerId(id)
            If stazione IsNot Nothing Then Return stazione

            For i As Integer = 0 To _stazioniCorrenti.Count - 1
                If String.Equals(_stazioniCorrenti(i).Id, id, StringComparison.OrdinalIgnoreCase) Then
                    Return _stazioniCorrenti(i)
                End If
            Next

            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.TrovaStazione: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub RiproduciPerId(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return

            Dim stazione As RadioDb.StazioneRadio = TrovaStazione(id)
            If stazione Is Nothing OrElse String.IsNullOrWhiteSpace(stazione.UrlRiproduzione) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Radio non trovata o senza indirizzo di streaming.")
                msg.ShowDialog()
                Return
            End If

            RiproduciStazione(stazione)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RiproduciPerId: " & ex.ToString())
        End Try
    End Sub

    Private Sub RiproduciStazione(stazione As RadioDb.StazioneRadio)
        Try
            If stazione Is Nothing OrElse String.IsNullOrWhiteSpace(stazione.UrlRiproduzione) Then Return

            _db.SegnalaAscolto(stazione.Id)
            _raccolta.AggiungiRecente(RadioTvDatabase.TipoElemento.Radio, stazione.Id, stazione.Nome)
            _stazioneInAscolto = stazione

            ' sottofondo o lettore a schermo, come nelle impostazioni (o con la richiesta ogni volta)
            WGModalitaAscolto.Avvia(stazione.Nome,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciStreamRadio(stazione.UrlRiproduzione, stazione.Nome),
                                    Sub()
                                        Dim player As New WGPlayerFormInt()
                                        AddHandler player.ErroreRadio, AddressOf PlayerSegnalaErroreRadio
                                        player.AggiungiElemento(stazione.UrlRiproduzione, ElementType.WebRadio, stazione.Nome)
                                        player.avvia(stazione.Nome)
                                    End Sub)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RiproduciStazione: " & ex.ToString())
            Dim nome As String = "questa radio"
            Try
                If stazione IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(stazione.Nome) Then nome = stazione.Nome
            Catch exNome As Exception
                _logger.ScriviLog("MenuRadio.RiproduciStazione nome: " & exNome.Message)
            End Try
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile avviare la riproduzione di " & nome & ".")
            msg.ShowDialog()
        End Try
    End Sub

    Public Sub RiproduciCodaRegione(nomeRegione As String)
        Try
            Dim stazioni As List(Of RadioDb.StazioneRadio) = _db.OttieniStazioniRegione(nomeRegione)
            If stazioni Is Nothing OrElse stazioni.Count = 0 Then Return

            Dim indirizzi As New List(Of String)()
            Dim nomi As New List(Of String)()
            For i As Integer = 0 To stazioni.Count - 1
                If String.IsNullOrWhiteSpace(stazioni(i).UrlRiproduzione) Then Continue For
                indirizzi.Add(stazioni(i).UrlRiproduzione)
                nomi.Add(stazioni(i).Nome)
            Next
            If indirizzi.Count = 0 Then Return

            ' sottofondo o lettore a schermo, come nelle impostazioni (o con la richiesta ogni volta)
            WGModalitaAscolto.Avvia("Radio " & nomeRegione,
                                    Sub() WGCentroDiControllo.GetInstance().RiproduciCodaRadio(indirizzi, nomi),
                                    Sub()
                                        Dim player As New WGPlayerFormInt()
                                        AddHandler player.ErroreRadio, AddressOf PlayerSegnalaErroreRadio
                                        For i As Integer = 0 To indirizzi.Count - 1
                                            player.AggiungiElemento(indirizzi(i), ElementType.WebRadio, nomi(i))
                                        Next
                                        player.avvia("Radio " & nomeRegione)
                                    End Sub)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RiproduciCodaRegione: " & ex.ToString())
        End Try
    End Sub

    Private Sub PlayerSegnalaErroreRadio(sender As Object, e As ErroreRadioPlayerEventArgs)
        Try
            If e Is Nothing Then Return

            Dim stazione As RadioDb.StazioneRadio = Nothing
            If Not String.IsNullOrWhiteSpace(e.Url) Then
                stazione = _db.TrovaPerUrl(e.Url)
            End If
            If stazione Is Nothing AndAlso _stazioneInAscolto IsNot Nothing Then
                stazione = _stazioneInAscolto
            End If
            If stazione Is Nothing Then Return

            _db.SegnalaErroreStazione(stazione.Id)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.PlayerSegnalaErroreRadio: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Utilita"

    Private Sub RiempiLista(lista As WGMenuLista, stazioni As List(Of RadioDb.StazioneRadio))
        Try
            If lista Is Nothing OrElse stazioni Is Nothing Then Return

            lista.BeginUpdate()
            For i As Integer = 0 To stazioni.Count - 1
                lista.aggiungiElemento(CreaEtichetta(stazioni(i)), stazioni(i).Id)
            Next
            lista.EndUpdate()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RiempiLista: " & ex.ToString())
        End Try
    End Sub

    Private Function CreaEtichetta(s As RadioDb.StazioneRadio) As String
        Try
            If s Is Nothing Then Return "Radio"

            Dim nome As String = "Radio senza nome"
            If Not String.IsNullOrWhiteSpace(s.Nome) Then nome = s.Nome.Trim()

            If String.IsNullOrWhiteSpace(_regioneCorrente) Then
                If Not String.IsNullOrWhiteSpace(s.Regione) AndAlso Not String.Equals(s.Regione, RadioDb.ALTRE_REGIONI, StringComparison.CurrentCultureIgnoreCase) Then
                    Return nome & ", " & s.Regione
                End If
            End If

            Return nome
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.CreaEtichetta: " & ex.Message)
            Return "Radio"
        End Try
    End Function

    Private Sub ChiudiListeStazioni()
        Try
            If _listaTutte IsNot Nothing Then
                Try
                    _listaTutte.abbandona()
                Catch exA As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni tutte: " & exA.Message)
                End Try
                _listaTutte = Nothing
            End If
            If _listaPopolari IsNot Nothing Then
                Try
                    _listaPopolari.abbandona()
                Catch exB As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni popolari: " & exB.Message)
                End Try
                _listaPopolari = Nothing
            End If
            If _listaRisultati IsNot Nothing Then
                Try
                    _listaRisultati.abbandona()
                Catch exC As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni risultati: " & exC.Message)
                End Try
                _listaRisultati = Nothing
            End If
            If _listaStazioniRegione IsNot Nothing Then
                Try
                    _listaStazioniRegione.abbandona()
                Catch exD As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni regione: " & exD.Message)
                End Try
                _listaStazioniRegione = Nothing
            End If
            If _listaStazioniGenere IsNot Nothing Then
                Try
                    _listaStazioniGenere.abbandona()
                Catch exE As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni genere: " & exE.Message)
                End Try
                _listaStazioniGenere = Nothing
            End If
            If _listaPreferite IsNot Nothing Then
                Try
                    _listaPreferite.abbandona()
                Catch exF As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni preferite: " & exF.Message)
                End Try
                _listaPreferite = Nothing
            End If
            If _listaRecenti IsNot Nothing Then
                Try
                    _listaRecenti.abbandona()
                Catch exG As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni recenti: " & exG.Message)
                End Try
                _listaRecenti = Nothing
            End If
            If _listaStazioniInternazionali IsNot Nothing Then
                Try
                    _listaStazioniInternazionali.abbandona()
                Catch exI As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListeStazioni internazionali: " & exI.Message)
                End Try
                _listaStazioniInternazionali = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiudiListeStazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiListe()
        Try
            ChiudiListaProgrammi()
            ChiudiListeStazioni()
            ChiudiListaPaesi()
            If _listaRegioni IsNot Nothing Then
                Try
                    _listaRegioni.abbandona()
                Catch exR As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListe regioni: " & exR.Message)
                End Try
                _listaRegioni = Nothing
            End If
            If _listaGeneri IsNot Nothing Then
                Try
                    _listaGeneri.abbandona()
                Catch exH As Exception
                    _logger.ScriviLog("MenuRadio.ChiudiListe generi: " & exH.Message)
                End Try
                _listaGeneri = Nothing
            End If
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.ChiudiListe: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Comandi vocali"

    Public Sub RiproduciStazionePerNome(nome As String)
        Try
            If String.IsNullOrWhiteSpace(nome) Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Indica il nome della radio da ascoltare.")
                avviso.ShowDialog()
                Return
            End If

            If Not AssicuraDatiCaricati() Then Return

            Dim stazione As RadioDb.StazioneRadio = TrovaStazionePerNome(nome)
            If stazione Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna radio trovata con il nome " & nome.Trim() & ".")
                msg.ShowDialog()
                Return
            End If

            RiproduciStazione(stazione)
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.RiproduciStazionePerNome: " & ex.ToString())
        End Try
    End Sub

    Public Function TrovaStazionePerNome(nome As String) As RadioDb.StazioneRadio
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return Nothing

            Dim candidati As List(Of RadioDb.StazioneRadio) = _db.Cerca(nome.Trim())
            If candidati Is Nothing OrElse candidati.Count = 0 Then Return Nothing

            Dim richiesta As String = NormalizzaNomeRadio(nome)
            Dim migliore As RadioDb.StazioneRadio = Nothing
            Dim punteggioMigliore As Integer = -1

            For i As Integer = 0 To candidati.Count - 1
                Dim s As RadioDb.StazioneRadio = candidati(i)
                If s Is Nothing OrElse String.IsNullOrWhiteSpace(s.UrlRiproduzione) Then Continue For

                Dim nomeStazione As String = NormalizzaNomeRadio(s.Nome)
                Dim punteggio As Integer = 0
                If String.Equals(nomeStazione, richiesta, StringComparison.Ordinal) Then
                    punteggio = 1000
                ElseIf nomeStazione.StartsWith(richiesta, StringComparison.Ordinal) Then
                    punteggio = 500
                ElseIf nomeStazione.Contains(richiesta) Then
                    punteggio = 200
                End If

                If punteggio > punteggioMigliore Then
                    punteggioMigliore = punteggio
                    migliore = s
                End If
            Next

            Return migliore
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.TrovaStazionePerNome: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function NormalizzaNomeRadio(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim scomposto As String = testo.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim risultato As New StringBuilder(scomposto.Length)
            For Each carattere As Char In scomposto
                If Char.GetUnicodeCategory(carattere) = System.Globalization.UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(carattere) Then risultato.Append(carattere)
            Next
            Return risultato.ToString()
        Catch ex As Exception
            _logger.ScriviLog("MenuRadio.NormalizzaNomeRadio: " & ex.ToString())
            Return If(testo, "").ToLowerInvariant()
        End Try
    End Function

#End Region

End Class