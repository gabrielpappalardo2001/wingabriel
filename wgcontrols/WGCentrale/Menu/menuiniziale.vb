Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGTreni
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Menu di avvio del programma con la gestione della sessione, delle notizie, degli strumenti multimediali e delle opzioni di
' uscita.
Public Class menuiniziale
    Private WithEvents preavvisi As New PreavvisoManager
    Private menu As WGMenu
    Private impostazioni As New Impostazioni()
    Private uscitaInCorso As Boolean = False
    Private menuEnergia As WGMenuLista
    Private menuInformazioniAutore As WGMenuLista
    Private menuNotizieWinGabriel As WGMenuLista
    Private articoliNotizieWinGabriel As New List(Of ArticoloNotiziaWinGabriel)
    Private menuNotizieDaLeggereWinGabriel As WGMenuLista
    Private notizieDaLeggereElenco As New List(Of ArticoloNotiziaWinGabriel)

    Public Sub New()
        Try
            ' Musica più bassa mentre parla la sintesi e un solo file alla volta.
            WGRiproduzione.Avvia()
            ' Le sveglie impostate riprendono da sole.
            WGSveglie.Avvia()
            ' Lo spegnimento programmato dal Timer chiude WinGabriel nel modo solito.
            WGAzioniProgrammate.ChiudiWinGabriel = AddressOf EseguiUscitaConfermata
            ' Gli altri avvisi (quotidiani, posta, video) aspettano le domande dell'avvio.
            PreavvisoManager.AvvioInCorso = True
            ' In modalità smart il menu applicazioni è un'applicazione come le altre: all'avvio non si apre.
            If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.AvviaMenuApplicazioniChiuso()
            avvia()
            AvvioConDomande()
            AvviaTelefono()
        Catch ex As Exception
            PreavvisoManager.AvvioInCorso = False
            log.ScriviLog("New (menuiniziale) eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Prima di scrivania, home e applicazioni dell'ultima volta arrivano le domande (la domanda
    ' sull'aggiornamento è già stata fatta prima ancora di aprire il menu):
    '   1. la prima volta il benvenuto, con la modalità e la prova dei tasti;
    '   2. le notizie di WinGabriel, se ce ne sono di nuove: rispondendo sì si aprono subito, senza
    '      altri annunci, e le applicazioni dell'ultima volta si riaprono quando le notizie si chiudono.
    ' Poi la scrivania (in modalità tablet la home), la ripresa della sessione e la prova guidata.
    Private Async Sub AvvioConDomande()
        Dim leggiNotizie As Boolean = False
        Dim prova As Boolean = False
        Try
            ' La voce di WinGabriel è eSpeak: in una installazione nuova, se manca la scarica, con la finestra di attesa che dice a
            ' che punto è, e la imposta come predefinita. Se l'utente ha scelto le voci di Windows non fa niente.
            ' Solo nelle installazioni nuove: chi aveva già WinGabriel non vede cambiare né la voce né i file.
            If AvvioAmministratore.ProfiloNuovoAllAvvio Then Await WGEspeak.VerificaEInstallaAsync()
            ' Un profilo nuovo (il benvenuto non è mai stato fatto) fa anche la configurazione guidata, dopo il benvenuto.
            Dim profiloNuovo As Boolean = False
            Try
                profiloNuovo = (Not (New WGConfig()).EsisteId("accoglienza_nuova_versione") AndAlso Not (New WGConfig()).EsisteId(ConfigurazioneGuidata.IdFatta)) OrElse ConfigurazioneGuidata.DaContinuareDopoInstallazione()
            Catch
            End Try
            prova = Await WGAccoglienza.ChiediSeServeAsync()
            If profiloNuovo Then Await ConfigurazioneGuidata.EseguiAsync()
            Dim nuove As Integer = 0
            If preavvisi IsNot Nothing AndAlso preavvisi.NotizieDaControllareOggi() Then
                ' Al massimo dieci secondi: senza rete si va avanti.
                Dim lettura As Task(Of Integer) = Task.Run(Function() preavvisi.NotizieDaProporreAllAvvio())
                nuove = Await WGProcessoInCorso.EseguiAsync("Controllo delle notizie di WinGabriel",
                                                            Async Function()
                                                                Dim prima As Task = Await Task.WhenAny(lettura, Task.Delay(10000))
                                                                Return If(prima Is lettura, lettura.Result, 0)
                                                            End Function)
            End If
            If nuove > 0 Then
                leggiNotizie = Await WGDomanda.ChiediAsync(If(nuove = 1, "C'è una nuova notizia da WinGabriel non ancora letta. Vuoi leggerla adesso?",
                                                               "Ci sono " & nuove & " nuove notizie da WinGabriel non ancora lette. Vuoi leggerle adesso?"))
            End If
            ' I plugin non firmati: l'avviso e la domanda, e i plugin che non sono partiti.
            Try
                Await WGGestorePlugin.ChiediConsensiAsync()
            Catch exPlugin As Exception
                log.ScriviLog("AvvioConDomande (plugin): " & exPlugin.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("AvvioConDomande (domande): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        PreavvisoManager.AvvioInCorso = False
        Try
            ' La prima cosa che si vede è la scrivania (in modalità tablet la home); il menu applicazioni
            ' si apre con il tasto Windows. Senza scrivania si apre il menu.
            If Not WGScrivania.Vai() AndAlso WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.VaiAlMenuIniziale()
            If leggiNotizie Then
                ' Le notizie subito davanti, come applicazione a sé; chiudendole si torna alla scrivania
                ' e solo allora si riaprono le applicazioni dell'ultima volta.
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(NomeApplicazioneNotizie)
                Dim notizie As New MenuNotizieDaLeggereWinGabriel()
                WGSessione.RiapriDopo(NomeApplicazioneNotizie)
            Else
                WGSessione.RiapriAsync()
            End If
            If prova Then WGAccoglienza.AvviaProva()
        Catch ex As Exception
            log.ScriviLog("AvvioConDomande (apertura): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Const NomeApplicazioneNotizie As String = "Notizie di WinGabriel"

    ' Dopo quale voce del menu principale va la prossima voce aggiunta da un plugin.
    Private riferimentoPlugin As String = "Altre funzioni"

    Public Sub avvia(Optional finestraapparsa As Boolean = False)
        Try
            uscitaInCorso = False
            ' Il manuale (WGGuida): Control F1, la voce Manuale e l'F1 di scrivania e home.
            WGContenitore.ApriManualeAzione = Sub() Global.WGGuida.WGManuale.Mostra()
            WGContenitore.ApriCapitoloManuale = Sub(chiave As String) Global.WGGuida.WGManuale.MostraCapitolo(chiave)
            ' I messaggi della posta scritti con la grafica nel browser ridotto a lettore.
            WGLettoreHtml.Apri = Function(richiesta As WGRichiestaLettoreHtml) Global.WGBrowser.WGBrowser.ApriLettoreHtml(richiesta)
            ' Il browser legge in una scheda nascosta le pagine che servono ad altre parti (YouTube con l'account).
            WGPaginaNascosta.Esegui = Function(indirizzo As String, script As String) Global.WGBrowser.WGPagineNascoste.EseguiAsync(indirizzo, script)
            WGPaginaNascosta.HaCookie = Function(indirizzo As String, nomi As String()) Global.WGBrowser.WGPagineNascoste.HaCookieAsync(indirizzo, nomi)

            menu = New WGMenu()
            AddHandler menu.abbandonato, AddressOf VerificaUscita

            menu.aggiungi("Editor", AddressOf testi)
            menu.aggiungi("Gestione file", AddressOf gestionefile)
            menu.aggiungi("Multimedia", AddressOf multimedia)
            menu.aggiungi("Organizer", AddressOf organizer)
            menu.aggiungi("Mail", AddressOf Mail)
            menu.aggiungi("Telefono", AddressOf telefono)
            menu.aggiungi("Web", AddressOf web)
            menu.aggiungi("Giochi", AddressOf giochi)
            menu.aggiungi("Chat", AddressOf chat)
            menu.aggiungi("Traduttore", AddressOf traduttore)
            menu.aggiungi("Salute", AddressOf salute)
            menu.aggiungi("Altre funzioni", AddressOf altrefunzioni)
            menu.aggiungi("Intelligenza artificiale", AddressOf intelligenzaArtificiale)
            menu.aggiungi("Centro di controllo", Sub() WGCentroDiControllo.GetInstance().VisualizzaControllo())
            menu.aggiungi("Aggiornamento", AddressOf MostraMenuAggiornamento)
            menu.aggiungi("Impostazioni", AddressOf impostazioni.MostraMenuPrincipale)
            menu.aggiungi("Aiuto", AddressOf menuAiuto)
            menu.aggiungi("Esci", AddressOf VerificaUscita)

            If WGModalita.Smart Then
                menu.aggiungiOpzione("Cerca in WinGabriel, Windows S", Sub() WGRicerca.Mostra())
                menu.aggiungiOpzione("Centro notifiche", Sub() WGNotifiche.WGCentroNotifiche.MostraCentro())
            End If
            If WGScrivania.Attiva Then menu.aggiungiOpzione("Scrivania, Control Alt D", Sub() WGScrivania.Vai())
            menu.aggiungiOpzione("Esporta dati", Sub() ScambioDati.EsportaDati())
            menu.aggiungiOpzione("Importa dati", Sub() ScambioDati.ImportaDati())
            menu.aggiungiOpzione("Guida delle applicazioni", Sub() WGContenitore.MostraGuidaApplicazioni())
            menu.aggiungiOpzione("Manuale, Control F1", Sub() WGContenitore.ApriManuale())
            WGContenitore.ContaNotificheDaLeggere = AddressOf WGNotifiche.WGCentroNotifiche.DaLeggere
            ' Home e centro di controllo della modalità smart: Wi-Fi, Bluetooth, notifiche.
            WGStatoDispositivo.LeggiWifi = Function()
                                               Dim ssid As String = ""
                                               Dim segnale As Integer = 0
                                               If WGConnessioni.WGWifi.ConnessioneCorrente(ssid, segnale) Then Return Tuple.Create(ssid, segnale)
                                               Return Nothing
                                           End Function
            WGPannelloControllo.LeggiRadio = Function(bluetooth As Boolean) WGConnessioni.WGConnessioni.StatoRadioAsync(bluetooth)
            WGPannelloControllo.CommutaRadio = Function(bluetooth As Boolean) WGConnessioni.WGConnessioni.CommutaRadioAsync(bluetooth)
            WGPannelloControllo.TestoNonDisturbare = AddressOf WGNotifiche.WGCentroNotifiche.DescriviNonDisturbare
            WGPannelloControllo.ScegliNonDisturbare = AddressOf WGNotifiche.WGCentroNotifiche.ScegliNonDisturbare
            WGContenitore.ApriCentroNotifiche = Sub() WGNotifiche.WGCentroNotifiche.MostraCentro()
            ' Ricerca globale (Windows S): in fondo ai risultati, lo stesso testo nei file del computer.
            WGRicerca.CercaNeiFile = Sub(testo As String)
                                         Dim gestione As New MenuFile()
                                         gestione.CercaNelComputer(testo)
                                     End Sub
            ' Avvisi di connessione, memoria e disco: nel centro notifiche, che rispetta il non disturbare.
            WGAvvisiSistema.AvvisoNotifica = Sub(testo As String, azione As Action) WGNotifiche.WGCentroNotifiche.Pubblica("Sistema", testo, azione, annuncia:=True)
            WGAvvisiSistema.NomeReteWifi = Function()
                                               Dim ssid As String = ""
                                               Dim segnale As Integer = 0
                                               If WGConnessioni.WGWifi.ConnessioneCorrente(ssid, segnale) Then Return ssid
                                               Return ""
                                           End Function
            ' Promemoria dei programmi televisivi: partono con WinGabriel, non solo quando si apre la Web TV.
            Try
                WGRadioTv.TvPromemoria.Avvia()
            Catch exTv As Exception
                log.ScriviLog("menuiniziale (promemoria televisivi): " & exTv.Message)
            End Try
            ' Avviso dei nuovi articoli di Nel Quotidiano (solo se attivato dalla Configurazione degli articoli).
            Try
                WGNelQuotidiano.NelQuotidianoAvvisi.Avvia()
            Catch exNq As Exception
                log.ScriviLog("menuiniziale (avviso Nel Quotidiano): " & exNq.Message)
            End Try
            ' I compleanni della rubrica in agenda (se la funzione è attiva) si allineano a ogni avvio.
            Try
                WGCompleanniAgenda.SincronizzaInSottofondo()
            Catch exCompleanni As Exception
                log.ScriviLog("menuiniziale (compleanni in agenda): " & exCompleanni.Message)
            End Try
            ' La chat resta in ascolto anche da chiusa: un messaggio nuovo va nel centro notifiche e chiede se leggerlo.
            Try
                WGChat.MenuChat.PubblicaNotifica = Sub(testo As String, azione As Action) WGNotifiche.WGCentroNotifiche.Pubblica("Chat", testo, azione, annuncia:=True)
                WGChat.MenuChat.AvviaInSottofondo()
            Catch exChat As Exception
                log.ScriviLog("menuiniziale (chat): " & exChat.Message)
            End Try
            ' Plugin: le voci che aggiungono al menu principale vengono dopo "Altre funzioni". Anche i plugin attivati più tardi
            ' (dalle impostazioni o con la risposta all'avviso) mettono e tolgono le loro voci subito, senza riaprire WinGabriel.
            Try
                ' Ogni menu di WinGabriel che si apre passa da qui: i plugin possono aggiungerci voci e opzioni, riconoscendolo dal titolo.
                WGMenu.AllApertura = AddressOf WGGestorePlugin.AllAperturaMenu
                riferimentoPlugin = "Altre funzioni"
                WGGestorePlugin.AggiungiAlMenuPrincipale = Sub(testo As String, azione As Action)
                                                               menu.AggiungiDopo(riferimentoPlugin, testo, Sub(mittente As Object, e As EventArgs) azione.Invoke())
                                                               riferimentoPlugin = testo
                                                           End Sub
                WGGestorePlugin.RimuoviDalMenuPrincipale = Sub(testo As String) menu.RimuoviVoce(testo)
                WGGestorePlugin.ApplicaVociMenuPrincipale()
                WGGestorePlugin.CaricaTutti()
            Catch exPlugin As Exception
                log.ScriviLog("menuiniziale (plugin): " & exPlugin.Message)
            End Try
            RegistraScrivania()
            ' Esc nel menu applicazioni: in modalità smart lo chiude, come ogni applicazione, tornando a quella
            ' di prima (se non c'è nient'altro chiede di uscire); nella classica va alla scrivania.
            menu.ImpostaAzioneEsc(Function()
                                      Dim contenitore As WGContenitore = WGContenitore.Istanza
                                      If WGModalita.Smart AndAlso contenitore IsNot Nothing AndAlso WGContenitore.ApplicazioniSeparate Then Return contenitore.ChiudiMenuApplicazioni()
                                      Return WGScrivania.Vai()
                                  End Function)
            menu.SegnaComeMenuIniziale()
            menu.avvia(If(WGModalita.Smart, "Menu applicazioni", "Menù iniziale"))
        Catch ex As Exception
        End Try
    End Sub
    ' Media player, musica, registratore vocale e fotocamera stanno insieme nella voce Multimedia.
    Private Sub multimedia()
        Try
            Dim menuMultimedia As New WGMenu()
            menuMultimedia.aggiungi("Media player", AddressOf mediaplayer)
            menuMultimedia.aggiungi("Musica", AddressOf musica)
            menuMultimedia.aggiungi("Registratore vocale", AddressOf registratoreVocale)
            menuMultimedia.aggiungi("Fotocamera", AddressOf fotocamera)
            menuMultimedia.avvia("Multimedia")
        Catch ex As Exception
            log.ScriviLog("menuiniziale.multimedia: " & ex.Message)
        End Try
    End Sub

    Private Sub musica()
        Try
            Dim menuMusica As New WGMenu()
            menuMusica.aggiungi("Strumenti musicali", AddressOf strumentiMusicali)
            menuMusica.aggiungi("Spartiti musicali", AddressOf spartitiMusicali)
            menuMusica.aggiungi("Configurazione", AddressOf ConfigurazioneMusica.Mostra)
            menuMusica.avvia("Musica")
        Catch ex As Exception
            log.ScriviLog("menuiniziale.musica: " & ex.Message)
        End Try
    End Sub

    Private Sub strumentiMusicali()
        Try
            Dim menuStrumenti As New MenuStrumentiMusicali()
            menuStrumenti.Avvia()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.strumentiMusicali: " & ex.Message)
        End Try
    End Sub

    Private Sub spartitiMusicali()
        Try
            Dim menuSpartiti As New MenuSpartiti()
            menuSpartiti.Avvia()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.spartitiMusicali: " & ex.Message)
        End Try
    End Sub

    Private Sub fotocamera()
        Try
            Dim f As New MenuFotocamera()
            f.Avvia()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.fotocamera: " & ex.Message)
        End Try
    End Sub

    Private Sub registratoreVocale()
        Try
            Dim registratore As New MenuRegistratoreVocale()
            registratore.Avvia()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.registratoreVocale: " & ex.Message)
        End Try
    End Sub

    Private Sub telefono()
        Try
            MenuTelefono.Istanza().Avvia()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.telefono: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaTelefono()
        Try
            MenuTelefono.NotificaChiamataPersa = AddressOf PubblicaChiamataPersa
            MenuTelefono.AvviaAscoltoChiamate()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.AvviaTelefono: " & ex.Message)
        End Try
    End Sub

    Private Sub PubblicaChiamataPersa(testo As String)
        Try
            WGNotifiche.WGCentroNotifiche.Pubblica("Telefono", testo, AddressOf ApriChiamatePerse, annuncia:=True)
        Catch ex As Exception
            log.ScriviLog("menuiniziale.PubblicaChiamataPersa: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriChiamatePerse()
        Try
            MenuTelefono.Istanza().ApriChiamatePerse()
        Catch ex As Exception
            log.ScriviLog("menuiniziale.ApriChiamatePerse: " & ex.Message)
        End Try
    End Sub

    Private Sub Mail()
        Dim menumail As New MenuPosta()
        menumail.avvia()
    End Sub

    Private Sub MostraMenuAggiornamento()
        Try
            Dim menuAgg As New WGMenu()
            menuAgg.aggiungi("Aggiorna il programma", AddressOf AggiornaIlProgramma)
            menuAgg.aggiungi("Verifica e ripara i file", AddressOf RiparaIlProgramma)
            menuAgg.aggiungi("Torna alla versione precedente", AddressOf TornaAllaVersionePrecedente)
            menuAgg.avvia("Aggiornamento")
        Catch ex As Exception
            log.ScriviLog("MostraMenuAggiornamento eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Controlla ogni file del programma, leggendolo per intero, e se ne trova di mancanti o danneggiati propone di rimetterli a posto.
    Private Sub RiparaIlProgramma()
        Try
            Dim verifica As New connessione()

            If Not verifica.VerificaConnessione() Then
                Dim msgConnessione As New WGMessaggi()
                msgConnessione.Messaggio("Non è disponibile una connessione a internet, non è possibile controllare i file.")
                msgConnessione.ShowDialog()
                Return
            End If

            Dim processoincorso As New WGProcessoInCorso()
            Dim syncContext As SynchronizationContext = SynchronizationContext.Current

            Task.Run(Sub()
                         Dim daRiparare As Boolean = False

                         Try
                             daRiparare = VerificaERiparaAdesso()
                         Catch exInner As Exception
                             log.ScriviLog("RiparaIlProgramma (Task.Run): " & exInner.Message & Environment.NewLine & exInner.StackTrace)
                         End Try

                         If syncContext IsNot Nothing Then
                             syncContext.Post(Sub(state)
                                                  Try
                                                      processoincorso.Fine()
                                                      MostraEsitoVerificaAggiornamenti(daRiparare, True)
                                                  Catch exUi As Exception
                                                      log.ScriviLog("RiparaIlProgramma (post): " & exUi.Message & Environment.NewLine & exUi.StackTrace)
                                                  End Try
                                              End Sub, Nothing)
                         End If
                     End Sub)

            processoincorso.Avvia("Controllo dei file in corso, attendi")
        Catch ex As Exception
            log.ScriviLog("RiparaIlProgramma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiornaIlProgramma()
        Try
            Dim verifica As New connessione()

            If Not verifica.VerificaConnessione() Then
                Dim msgConnessione As New WGMessaggi()
                msgConnessione.Messaggio("Non è disponibile una connessione a internet, non è possibile verificare gli aggiornamenti.")
                msgConnessione.ShowDialog()
                Return
            End If

            Dim processoincorso As New WGProcessoInCorso()
            Dim syncContext As SynchronizationContext = SynchronizationContext.Current

            Task.Run(Sub()
                         Dim disponibile As Boolean = False

                         Try
                             disponibile = VerificaAggiornamentiAdesso()
                         Catch exInner As Exception
                             log.ScriviLog("AggiornaIlProgramma (Task.Run): " & exInner.Message & Environment.NewLine & exInner.StackTrace)
                         End Try

                         If syncContext IsNot Nothing Then
                             syncContext.Post(Sub(state)
                                                  Try
                                                      processoincorso.Fine()
                                                      MostraEsitoVerificaAggiornamenti(disponibile)
                                                  Catch exUi As Exception
                                                      log.ScriviLog("AggiornaIlProgramma (post): " & exUi.Message & Environment.NewLine & exUi.StackTrace)
                                                  End Try
                                              End Sub, Nothing)
                         End If
                     End Sub)

            processoincorso.Avvia("Verifica degli aggiornamenti in corso, attendere")
        Catch ex As Exception
            log.ScriviLog("AggiornaIlProgramma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub MostraEsitoVerificaAggiornamenti(disponibile As Boolean, Optional ripara As Boolean = False)
        Try
            If Not disponibile Then
                Dim msgAggiornato As New WGMessaggi()
                msgAggiornato.Messaggio(If(ripara, "Tutti i file di WinGabriel sono a posto.", "WinGabriel è già aggiornato all'ultima versione disponibile."))
                msgAggiornato.ShowDialog()
                Return
            End If

            riparazioneRichiesta = ripara

            If ripara Then
                If Await WGDomanda.ChiediAsync("Sono stati rilevati degli errori nei componenti di WinGabriel. Vuoi eseguire una riparazione?") Then ConfermaAggiornamentoAdesso()
                Return
            End If

            Dim menuConferma As New WGMenu()
            menuConferma.aggiungi("Sì, installa l'aggiornamento adesso", AddressOf ConfermaAggiornamentoAdesso)
            menuConferma.aggiungi("No, non aggiornare adesso", AddressOf AnnullaAggiornamentoAdesso)
            menuConferma.avvia("È disponibile un aggiornamento di WinGabriel, si vuole installarlo adesso?")
        Catch ex As Exception
            log.ScriviLog("MostraEsitoVerificaAggiornamenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private riparazioneRichiesta As Boolean

    Private Sub ConfermaAggiornamentoAdesso()
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(If(riparazioneRichiesta, "WinGabriel verrà chiuso e sta per iniziare la riparazione dei file.", "WinGabriel verrà chiuso e sta per iniziare l'aggiornamento del programma."))
            messaggio.ShowDialog()

            If Not AvviaAggiornatore(False, riparazioneRichiesta) Then
                Dim errore As New WGMessaggi()
                errore.Messaggio("Non è stato possibile avviare il programma di aggiornamento.")
                errore.ShowDialog()
                Return
            End If

            Application.DoEvents()
            SincronizzaCloudPrimaDiUscire()
            WGUtils.uscita()
        Catch ex As Exception
            log.ScriviLog("ConfermaAggiornamentoAdesso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AnnullaAggiornamentoAdesso()
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Aggiornamento annullato.")
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("AnnullaAggiornamentoAdesso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TornaAllaVersionePrecedente()
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("WinGabriel verrà chiuso e verrà aperto il programma di ripristino della versione precedente.")
            messaggio.ShowDialog()

            If Not AvviaAggiornatore(True) Then
                Dim errore As New WGMessaggi()
                errore.Messaggio("Non è stato possibile avviare il programma di ripristino.")
                errore.ShowDialog()
                Return
            End If

            Application.DoEvents()
            SincronizzaCloudPrimaDiUscire()
            WGUtils.uscita()
        Catch ex As Exception
            log.ScriviLog("TornaAllaVersionePrecedente eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub nuovaSegnalazione()
        Dim segnalazioni As New MenuSegnalazioni()
        segnalazioni.Nuova()
    End Sub

    Private Sub menuAiuto()
        Try
            Dim menuaiuto As New WGMenu()
            menuaiuto.aggiungi("Manuale", Sub() WGContenitore.ApriManuale())
            menuaiuto.aggiungi("Segnalazioni", AddressOf nuovaSegnalazione)
            menuaiuto.aggiungi("Informazioni", AddressOf informazioniProgramma)
            menuaiuto.avvia("Aiuto")
        Catch ex As Exception
            log.ScriviLog("menuAiuto eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub mostraVersioneWinGabriel()
        Try
            Dim nome As String = RilascioFirmato.NomeInstallato(WGDb.WGPercorsiApplicazione.CartellaCache(), "wingabriel")
            Dim m As New WGMessaggi()
            m.Messaggio(If(nome = "", "La versione di WinGabriel non è disponibile.", "Versione di WinGabriel: " & nome))
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("mostraVersioneWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub informazioniProgramma()
        Try
            Dim menuinfo As New WGMenu()
            menuinfo.aggiungi("Versione", AddressOf mostraVersioneWinGabriel)
            menuinfo.aggiungi("Informazioni su WinGabriel", AddressOf informazioniSuWinGabriel)
            menuinfo.aggiungi("Ultime notizie", AddressOf ultimeNotizieWinGabriel)
            menuinfo.aggiungi("Notizie da leggere", AddressOf notizieDaLeggereWinGabriel)
            menuinfo.aggiungi("Condizioni d'uso", AddressOf MostraCondizioniUsoInformazioni)
            menuinfo.aggiungi("Privacy policy", AddressOf MostraPrivacyPolicyInformazioni)
            menuinfo.avvia("Informazioni")
        Catch ex As Exception
            log.ScriviLog("informazioniProgramma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private visualizzatoreInformazioniLegali As VisualizzatoreDocumentoLegale

    Private Sub MostraCondizioniUsoInformazioni()
        Try
            visualizzatoreInformazioniLegali = New VisualizzatoreDocumentoLegale()
            visualizzatoreInformazioniLegali.MostraCondizioniUso()
        Catch ex As Exception
            log.ScriviLog("MostraCondizioniUsoInformazioni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraPrivacyPolicyInformazioni()
        Try
            visualizzatoreInformazioniLegali = New VisualizzatoreDocumentoLegale()
            visualizzatoreInformazioniLegali.MostraPrivacyPolicy()
        Catch ex As Exception
            log.ScriviLog("MostraPrivacyPolicyInformazioni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub informazioniSuWinGabriel()
        Try
            menuInformazioniAutore = New WGMenuLista()
            menuInformazioniAutore.multiselezioneConsentita = False
            menuInformazioniAutore.impostaTitolo("Informazioni su WinGabriel")
            Dim nomeVersione As String = RilascioFirmato.NomeInstallato(WGDb.WGPercorsiApplicazione.CartellaCache(), "wingabriel")
            If nomeVersione <> "" Then menuInformazioniAutore.aggiungiElemento("Versione: " & nomeVersione, "versione")
            menuInformazioniAutore.aggiungiElemento("Autore: Gabriel Pappalardo", "autore")
            menuInformazioniAutore.aggiungiElemento("Sito internet: https://www.wingabriel.it", "sito")
            menuInformazioniAutore.aggiungiElemento("Contatti: e-mail: gabriel.pappalardo@icloud.com", "contatti")
            menuInformazioniAutore.aggiungiOpzione("Copia indirizzo e-mail negli appunti", AddressOf CopiaEmailWinGabrielNegliAppunti)

            AddHandler menuInformazioniAutore.escPremuto, AddressOf ChiudiMenuInformazioniAutore

            menuInformazioniAutore.avvia()
        Catch ex As Exception
            log.ScriviLog("informazioniSuWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuInformazioniAutore(sender As Object, e As EventArgs)
        Try
            If menuInformazioniAutore IsNot Nothing Then menuInformazioniAutore.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiMenuInformazioniAutore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CopiaEmailWinGabrielNegliAppunti(sender As Object, e As EventArgs)
        Try
            Clipboard.SetText("gabriel.pappalardo@icloud.com")

            Dim msg As New WGMessaggi()
            msg.Messaggio("Indirizzo e-mail copiato negli appunti.")
            msg.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("CopiaEmailWinGabrielNegliAppunti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ultimeNotizieWinGabriel()
        Try
            Dim processoincorso As New WGProcessoInCorso()
            Dim syncContext As SynchronizationContext = SynchronizationContext.Current

            Task.Run(Sub()
                         Try
                             ' Le ultime notizie sono tutte quelle del sito, dalla più recente, lette o no: le non lette sono nell'altra voce.
                             Dim recuperate As Boolean = False
                             Dim elenco As List(Of ArticoloNotiziaWinGabriel) = RecuperaArticoliWinGabriel(recuperate)
                             If Not recuperate Then elenco = Nothing

                             If syncContext IsNot Nothing Then
                                 syncContext.Post(Sub(state)
                                                      Try
                                                          processoincorso.Fine()
                                                          MostraElencoNotizieWinGabriel(elenco)
                                                      Catch exUi As Exception
                                                          log.ScriviLog("ultimeNotizieWinGabriel (post): " & exUi.Message & Environment.NewLine & exUi.StackTrace)
                                                      End Try
                                                  End Sub, Nothing)
                             End If
                         Catch exInner As Exception
                             log.ScriviLog("ultimeNotizieWinGabriel (Task.Run): " & exInner.Message & Environment.NewLine & exInner.StackTrace)
                             If syncContext IsNot Nothing Then
                                 syncContext.Post(Sub(state)
                                                      Try
                                                          processoincorso.Fine()
                                                      Catch exFine As Exception
                                                          log.ScriviLog("ultimeNotizieWinGabriel (fine): " & exFine.Message & Environment.NewLine & exFine.StackTrace)
                                                      End Try
                                                  End Sub, Nothing)
                             End If
                         End Try
                     End Sub)

            processoincorso.Avvia("Recupero ultime notizie in corso, attendere")
        Catch ex As Exception
            log.ScriviLog("ultimeNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraElencoNotizieWinGabriel(elenco As List(Of ArticoloNotiziaWinGabriel))
        Try
            articoliNotizieWinGabriel = elenco

            If articoliNotizieWinGabriel Is Nothing OrElse articoliNotizieWinGabriel.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio(If(elenco Is Nothing, "Non è stato possibile recuperare le notizie dal sito di WinGabriel. Controlla la connessione a internet.", "Non ci sono articoli disponibili al momento."))
                msg.ShowDialog()
                Return
            End If

            menuNotizieWinGabriel = New WGMenuLista()
            menuNotizieWinGabriel.multiselezioneConsentita = False
            menuNotizieWinGabriel.impostaTitolo("Ultime notizie")

            For i As Integer = 0 To articoliNotizieWinGabriel.Count - 1
                menuNotizieWinGabriel.aggiungiElemento(articoliNotizieWinGabriel(i).Titolo, i.ToString())
            Next

            AddHandler menuNotizieWinGabriel.escPremuto, AddressOf ChiudiMenuNotizieWinGabriel
            AddHandler menuNotizieWinGabriel.ElementoSelezionato, AddressOf ArticoloNotiziaWinGabrielSelezionato

            menuNotizieWinGabriel.avvia()
        Catch ex As Exception
            log.ScriviLog("MostraElencoNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuNotizieWinGabriel(sender As Object, e As EventArgs)
        Try
            If menuNotizieWinGabriel IsNot Nothing Then menuNotizieWinGabriel.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiMenuNotizieWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ArticoloNotiziaWinGabrielSelezionato(id As String)
        Try
            If menuNotizieWinGabriel Is Nothing Then Return

            Dim elementi As List(Of String) = menuNotizieWinGabriel.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim elemento As String = elementi(0)
            Dim indice As Integer = -1

            If Not Integer.TryParse(elemento, indice) Then Return
            If indice < 0 OrElse indice >= articoliNotizieWinGabriel.Count Then Return

            Dim articolo As ArticoloNotiziaWinGabriel = articoliNotizieWinGabriel(indice)

            Try
                menuNotizieWinGabriel.abbandona()
            Catch exAbbandona As Exception
                log.ScriviLog("ArticoloNotiziaWinGabrielSelezionato abbandona eccezione: " & exAbbandona.Message)
            End Try

            Try
                GestoreArchivioNotizieWinGabriel.SegnaNotiziaComeLetta(articolo)
            Catch exArchivio As Exception
                log.ScriviLog("ArticoloNotiziaWinGabrielSelezionato (archiviazione lettura): " & exArchivio.Message & Environment.NewLine & exArchivio.StackTrace)
            End Try

            Dim campoArticolo As New WGCampo()
            campoArticolo.init(articolo.Titolo, articolo.Testo, True, True)
            campoArticolo.avvia()
        Catch ex As Exception
            log.ScriviLog("ArticoloNotiziaWinGabrielSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub notizieDaLeggereWinGabriel()
        Try
            notizieDaLeggereElenco = GestoreArchivioNotizieWinGabriel.OttieniNotizieDaLeggere()

            If notizieDaLeggereElenco Is Nothing OrElse notizieDaLeggereElenco.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non ci sono notizie da leggere al momento.")
                msg.ShowDialog()
                Return
            End If

            menuNotizieDaLeggereWinGabriel = New WGMenuLista()
            menuNotizieDaLeggereWinGabriel.multiselezioneConsentita = False
            menuNotizieDaLeggereWinGabriel.impostaTitolo("Notizie da leggere")

            For i As Integer = 0 To notizieDaLeggereElenco.Count - 1
                menuNotizieDaLeggereWinGabriel.aggiungiElemento(notizieDaLeggereElenco(i).Titolo, i.ToString())
            Next

            AddHandler menuNotizieDaLeggereWinGabriel.escPremuto, AddressOf ChiudiMenuNotizieDaLeggereWinGabriel
            AddHandler menuNotizieDaLeggereWinGabriel.ElementoSelezionato, AddressOf NotiziaDaLeggereSelezionata

            menuNotizieDaLeggereWinGabriel.avvia()
        Catch ex As Exception
            log.ScriviLog("notizieDaLeggereWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuNotizieDaLeggereWinGabriel(sender As Object, e As EventArgs)
        Try
            If menuNotizieDaLeggereWinGabriel IsNot Nothing Then menuNotizieDaLeggereWinGabriel.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiMenuNotizieDaLeggereWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub NotiziaDaLeggereSelezionata(id As String)
        Try
            If menuNotizieDaLeggereWinGabriel Is Nothing Then Return

            Dim elementi As List(Of String) = menuNotizieDaLeggereWinGabriel.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim elemento As String = elementi(0)
            Dim indice As Integer = -1

            If Not Integer.TryParse(elemento, indice) Then Return
            If indice < 0 OrElse indice >= notizieDaLeggereElenco.Count Then Return

            Dim articolo As ArticoloNotiziaWinGabriel = notizieDaLeggereElenco(indice)

            Try
                menuNotizieDaLeggereWinGabriel.abbandona()
            Catch exAbbandona As Exception
                log.ScriviLog("NotiziaDaLeggereSelezionata abbandona eccezione: " & exAbbandona.Message)
            End Try

            Try
                GestoreArchivioNotizieWinGabriel.SegnaNotiziaComeLetta(articolo)
            Catch exArchivio As Exception
                log.ScriviLog("NotiziaDaLeggereSelezionata (archiviazione lettura): " & exArchivio.Message & Environment.NewLine & exArchivio.StackTrace)
            End Try

            Dim campoArticolo As New WGCampo()
            campoArticolo.init(articolo.Titolo, articolo.Testo, True, True)
            campoArticolo.avvia()
        Catch ex As Exception
            log.ScriviLog("NotiziaDaLeggereSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub salute()
        Dim menufarmacia As New MenuFarmacia()
        menufarmacia.Avvia()
    End Sub

    Private Sub VerificaUscita()
        If uscitaInCorso Then Return
        ' L'elenco delle applicazioni per la ripresa resta quello di adesso mentre si chiudono.
        WGSessione.Congela()

        Dim operazioneCorrente As StatoOperazioneCorrente = StatoOperazioneCorrente.Nessuna

        Try
            Dim centroControllo As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centroControllo IsNot Nothing Then
                operazioneCorrente = centroControllo.OttieniOperazioneInCorso()
            End If
        Catch exStato As Exception
            ScriviLogAvvio("VerificaUscita (stato operazioni in corso): " & exStato.Message & Environment.NewLine & exStato.StackTrace)
        End Try

        If operazioneCorrente <> StatoOperazioneCorrente.Nessuna Then
            domandaUscita = New WGDomanda()
            Dim testoDomanda As String = ""

            Select Case operazioneCorrente
                Case StatoOperazioneCorrente.File
                    testoDomanda = "C'è un'operazione su file o cartelle in corso. Se esci, verrà interrotta. Sei sicuro di voler uscire?"
                Case StatoOperazioneCorrente.Video
                    testoDomanda = "Si stanno scaricando dei video. Uscendo l'operazione verrà interrotta. Confermi di voler uscire?"
                Case StatoOperazioneCorrente.Archivio
                    testoDomanda = "È in corso un'operazione di gestione archivio (compressione/decompressione). Uscendo l'operazione verrà interrotta. Vuoi proseguire e uscire?"
                Case StatoOperazioneCorrente.LibriVox
                    testoDomanda = "C'è un'operazione attiva su LibriVox. Uscendo verrà interrotta. Vuoi uscire comunque?"
                Case StatoOperazioneCorrente.Audio
                    testoDomanda = "La riproduzione audio è ancora attiva. Uscendo si interromperà l'ascolto. Vuoi veramente uscire?"
                Case StatoOperazioneCorrente.Bluetooth
                    testoDomanda = "È in corso un trasferimento Bluetooth. Se esci, l'invio verrà interrotto. Confermi l'uscita?"
                Case StatoOperazioneCorrente.DownloadDipendenze
                    testoDomanda = "Il programma sta ancora scaricando dei componenti necessari al suo funzionamento. Se esci, il download verrà interrotto. Sei sicuro di voler uscire?"
                Case Else
                    testoDomanda = "C'è un'operazione in background in corso. Se esci, verrà interrotta. Vuoi uscire comunque?"
            End Select

            AddHandler domandaUscita.rispostadomanda, AddressOf RispostaDomandaUscita
            domandaUscita.avvia(testoDomanda)
        Else
            ' Con le applicazioni separate si può lasciare aperto molto (anche quello riaperto in sottofondo):
            ' uscendo si chiudono una alla volta, con le domande di salvataggio dei testi modificati. La
            ' conferma, con i nomi delle applicazioni aperte, c'è solo se attivata in Impostazioni, Sistema.
            Dim aperte As New List(Of String)()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing Then aperte = contenitore.ElencoApplicazioniAperte()
            Catch exApplicazioni As Exception
                ScriviLogAvvio("VerificaUscita (applicazioni aperte): " & exApplicazioni.Message)
            End Try
            If aperte.Count = 0 Then
                MostraMenuUscita()
            ElseIf WGContenitore.ConfermaUscitaConApplicazioni Then
                Dim nomi As String = If(aperte.Count = 1, aperte(0), String.Join(", ", aperte.Take(aperte.Count - 1)) & " e " & aperte(aperte.Count - 1))
                domandaUscita = New WGDomanda()
                AddHandler domandaUscita.rispostadomanda, AddressOf RispostaUscitaConApplicazioni
                domandaUscita.avvia(If(aperte.Count = 1, "C'è un'applicazione aperta: ", "Ci sono " & aperte.Count & " applicazioni aperte: ") & nomi & ". Uscendo verranno chiuse una alla volta e per ogni testo modificato verrà chiesto se salvarlo. Vuoi uscire?")
            Else
                RispostaUscitaConApplicazioni(DialogResult.Yes)
            End If
        End If
    End Sub

    ' Uscita con applicazioni aperte: si chiudono una alla volta con le loro domande di salvataggio;
    ' se una domanda viene annullata l'uscita si ferma e si torna al menu iniziale.
    Private Sub RispostaUscitaConApplicazioni(risposta As DialogResult)
        Try
            If domandaUscita IsNot Nothing Then RemoveHandler domandaUscita.rispostadomanda, AddressOf RispostaUscitaConApplicazioni
            If risposta <> DialogResult.Yes Then
                TornaDopoUscitaAnnullata()
                Return
            End If
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then
                MostraMenuUscita()
                Return
            End If
            contenitore.ChiudiTutteLeApplicazioni(Sub() MostraMenuUscita(), Sub() TornaDopoUscitaAnnullata())
        Catch ex As Exception
            log.ScriviLog("RispostaUscitaConApplicazioni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            TornaDopoUscitaAnnullata()
        End Try
    End Sub

    Private Sub RispostaDomandaUscita(risposta As DialogResult)
        RemoveHandler domandaUscita.rispostadomanda, AddressOf RispostaDomandaUscita
        If risposta = DialogResult.Yes Then
            MostraMenuUscita()
        Else
            TornaDopoUscitaAnnullata()
        End If
    End Sub

    Private Sub MostraMenuUscita()
        If uscitaInCorso Then Return
        Try
            Dim menuUscita As New WGMenu()
            menuUscita.aggiungi("Esci", AddressOf EseguiUscitaConfermata)

            Dim nvdaAttivo As Boolean = Process.GetProcessesByName("nvda").Length > 0
            If Not nvdaAttivo Then
                Dim percorsoNVDA As String = TrovaPercorsoNVDA()

                If Not String.IsNullOrEmpty(percorsoNVDA) Then
                    menuUscita.aggiungi("Esci e attiva NVDA", Sub()
                                                                  Try
                                                                      uscitaInCorso = True
                                                                      SincronizzaCloudPrimaDiUscire()
                                                                      WGServiceInterop.InviaComando("EXIT")
                                                                      For Each p As Process In Process.GetProcessesByName("jfw")
                                                                          Try
                                                                              p.Kill()
                                                                              p.WaitForExit(3000)
                                                                          Catch ex As Exception
                                                                              log.ScriviLog("Errore chiusura JAWS prima di NVDA: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                          End Try
                                                                      Next
                                                                      ChiudiKsrInUscita()
 LettoreEsterno.ChiudiSeAttivo()
                                                                      Process.Start(percorsoNVDA)
                                                                      WGUtils.uscita()
                                                                  Catch ex As Exception
                                                                      log.ScriviLog("Errore avvio NVDA: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                      WGUtils.uscita()
                                                                  End Try
                                                              End Sub)

                End If
            End If

            Dim jawsAttivo As Boolean = Process.GetProcessesByName("jfw").Length > 0
            If Not jawsAttivo Then
                Dim percorsoJAWS As String = TrovaPercorsoJAWS()

                If Not String.IsNullOrEmpty(percorsoJAWS) Then
                    menuUscita.aggiungi("Esci e attiva JAWS", Sub()
                                                                  Try
                                                                      uscitaInCorso = True
                                                                      SincronizzaCloudPrimaDiUscire()
                                                                      WGServiceInterop.InviaComando("EXIT")
                                                                      For Each p As Process In Process.GetProcessesByName("nvda")
                                                                          Try
                                                                              p.Kill()
                                                                              p.WaitForExit(3000)
                                                                          Catch ex As Exception
                                                                              log.ScriviLog("Errore chiusura NVDA prima di JAWS: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                          End Try
                                                                      Next
                                                                      ChiudiKsrInUscita()
 LettoreEsterno.ChiudiSeAttivo()
                                                                      Process.Start(percorsoJAWS)
                                                                      WGUtils.uscita()
                                                                  Catch ex As Exception
                                                                      log.ScriviLog("Errore avvio JAWS: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                      WGUtils.uscita()
                                                                  End Try
                                                              End Sub)

                End If
            End If

            Dim ksrAttivo As Boolean = Process.GetProcessesByName("KSR").Length > 0
            If Not ksrAttivo Then
                Dim percorsoKSR As String = TrovaPercorsoKSR()
                If Not String.IsNullOrEmpty(percorsoKSR) Then
                    menuUscita.aggiungi("Esci e attiva KSR", Sub()
                                                                 Try
                                                                     uscitaInCorso = True
                                                                     SincronizzaCloudPrimaDiUscire()
                                                                     WGServiceInterop.InviaComando("EXIT")
                                                                     ChiudiLettoriDiversiDaKsrInUscita()
                                                                     LettoreEsterno.ChiudiSeAttivo()
                                                                     Process.Start(New ProcessStartInfo(percorsoKSR) With {.UseShellExecute = True, .WorkingDirectory = Path.GetDirectoryName(percorsoKSR)})
                                                                     WGUtils.uscita()
                                                                 Catch ex As Exception
                                                                     log.ScriviLog("Errore avvio KSR: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                     WGUtils.uscita()
                                                                 End Try
                                                             End Sub)
                End If
            End If

            ' Il lettore di schermo c'è solo se è installato e non è già attivo.
            If LettoreEsterno.PercorsoEseguibile() <> "" AndAlso Not LettoreEsterno.InEsecuzione() Then
                menuUscita.aggiungi("Esci e attiva il lettore di schermo", Sub()
                                                                                Try
                                                                                    uscitaInCorso = True
                                                                                    SincronizzaCloudPrimaDiUscire()
                                                                                    WGServiceInterop.InviaComando("EXIT")
                                                                                    LettoreEsterno.AvviaOAggiorna()
                                                                                    WGUtils.uscita()
                                                                                Catch ex As Exception
                                                                                    log.ScriviLog("Errore avvio lettore di schermo: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                                                    WGUtils.uscita()
                                                                                End Try
                                                                            End Sub)
            End If

            Dim percorsoNarrator As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Narrator.exe")
            If File.Exists(percorsoNarrator) Then
                menuUscita.aggiungi("Esci e attiva assistente vocale", Sub()

                                                                           uscitaInCorso = True

                                                                           WGServiceInterop.InviaComando("EXIT")

                                                                           ChiudiKsrInUscita()
 LettoreEsterno.ChiudiSeAttivo()

                                                                           Process.Start(percorsoNarrator)
                                                                           WGUtils.uscita()
                                                                       End Sub)

            End If

            menuUscita.aggiungi("Arresta il computer", Sub()
                                                           uscitaInCorso = True

                                                           WGServiceInterop.InviaComando("EXIT")

                                                           Try
                                                               Dim psi As New ProcessStartInfo("shutdown", "/s /t 0 /f")
                                                               psi.UseShellExecute = False
                                                               psi.CreateNoWindow = True
                                                               Process.Start(psi)
                                                           Catch exShutdown As Exception
                                                               log.ScriviLog("Errore avvio spegnimento computer: " & exShutdown.Message & Environment.NewLine & exShutdown.StackTrace)
                                                           End Try

                                                           WGUtils.uscita()
                                                       End Sub)
            menuUscita.aggiungi("Riavvia il computer", Sub()

                                                           uscitaInCorso = True

                                                           WGServiceInterop.InviaComando("EXIT")

                                                           Try
                                                               Dim psi As New ProcessStartInfo("shutdown", "/r /t 0 /f")
                                                               psi.UseShellExecute = False
                                                               psi.CreateNoWindow = True
                                                               Process.Start(psi)
                                                           Catch exRiavvio As Exception
                                                               log.ScriviLog("Errore avvio riavvio computer: " & exRiavvio.Message & Environment.NewLine & exRiavvio.StackTrace)
                                                           End Try

                                                           WGUtils.uscita()
                                                       End Sub)
            menuUscita.aggiungi("Non uscire", AddressOf AnnullaUscita)
            AddHandler menuUscita.abbandonato, AddressOf AnnullaUscita

            ' Con uno screen reader acceso si parte sempre da "Esci", l'uscita senza avviarne un altro.
            If WGScreenReader.Attivo() Then menuUscita.posiziona(0)
            ' Durante l'uscita lo screen reader appena avviato non va chiuso.
            WGScreenReader.ChiusuraAutomaticaSospesa = True
            menuUscita.avvia("Menu di uscita")
        Catch ex As Exception
        End Try
    End Sub

    Private Sub EseguiUscitaConfermata()
        uscitaInCorso = True
        SincronizzaCloudPrimaDiUscire()
        WGServiceInterop.InviaComando("EXIT")
        WGUtils.uscita()

    End Sub

    Private Sub AnnullaUscita()
        WGScreenReader.ChiusuraAutomaticaSospesa = False
        TornaDopoUscitaAnnullata()
    End Sub

    ' L'uscita chiude il menu applicazioni per chiedere conferma: annullandola lo si ricrea, ma si torna
    ' alla scrivania, che era rimasta aperta sotto, come dopo Alt F4 annullato sul desktop di Windows.
    ' Senza scrivania (WinGabriel classico o modalità tablet) si resta nel menu.
    Private Sub TornaDopoUscitaAnnullata()
        Try
            WGSessione.Scongela()
            avvia(True)
            WGScrivania.Vai()
        Catch ex As Exception
            log.ScriviLog("TornaDopoUscitaAnnullata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraInfoSistema()
        Try
            Dim menuInfo As New WGMenuLista()
            menuInfo.impostaTitolo("Informazioni sistema")

            menuInfo.aggiungiElemento("Sistema Operativo: " & Environment.OSVersion.ToString(), "info_os")
            menuInfo.aggiungiElemento("Nome Computer: " & Environment.MachineName, "info_pc")
            menuInfo.aggiungiElemento("Architettura: " & If(Environment.Is64BitOperatingSystem, "64 bit", "32 bit"), "info_arch")
            menuInfo.aggiungiElemento("Numero processori: " & Environment.ProcessorCount.ToString(), "info_cpu")
            Try

                Dim ramTotale As Long = OttieniRamTotale()
                If ramTotale > 0 Then
                    menuInfo.aggiungiElemento("RAM totale: " & FormatBytes(ramTotale), "info_ram")
                End If
                Dim ramDisponibile As Long = OttieniRamDisponibile()
                If ramDisponibile > 0 Then
                    menuInfo.aggiungiElemento("RAM disponibile: " & FormatBytes(ramDisponibile), "info_ramdis")
                End If
            Catch ex As Exception
                Dim log As New Logger
                log.ScriviLog("MostraInfoSistema RAM: " & ex.Message)
            End Try

            Try
                Dim lettereDischi As String() = {"C", "D", "E", "F", "G"}
                Dim contatore As Integer = 0
                For Each lettera As String In lettereDischi
                    Try
                        Dim info As New DriveInfo(lettera)
                        If info.IsReady Then
                            Dim totale As Long = info.TotalSize
                            Dim libero As Long = info.AvailableFreeSpace
                            menuInfo.aggiungiElemento(
                                        "Disco " & lettera & ": - Totale: " & FormatBytes(totale) & " | Libero: " & FormatBytes(libero),
                                        "info_disco" & contatore.ToString())
                            contatore += 1
                        End If
                    Catch ex As Exception
                        Dim log As New Logger
                        log.ScriviLog("MostraInfoSistema dischi: " & ex.Message)
                    End Try
                Next
            Catch ex As Exception

                Dim log As New Logger()
                log.ScriviLog("MostraInfoSistema disco: " & ex.Message)
            End Try

            Try
                Dim nomeUtente As String = Environment.UserName
                menuInfo.aggiungiElemento("Utente corrente: " & nomeUtente, "info_user")
            Catch ex As Exception
                Dim log As New Logger()
                log.ScriviLog("MostraInfoSistema utente: " & ex.Message)
            End Try

            Try
                Dim millisecondiAccensione As Long = CLng(Environment.TickCount) And &HFFFFFFFFL
                Dim uptime As TimeSpan = TimeSpan.FromMilliseconds(millisecondiAccensione)
                Dim uptimeTesto As String = $"{CInt(uptime.TotalHours)}h {uptime.Minutes}m {uptime.Seconds}s"
                menuInfo.aggiungiElemento("Uptime sistema: " & uptimeTesto, "info_uptime")
            Catch ex As Exception
                Dim log As New Logger()
                log.ScriviLog("MostraInfoSistema uptime: " & ex.Message)
            End Try

            AddHandler menuInfo.escPremuto, Sub() menuInfo.abbandona()
            menuInfo.avvia()
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("MostraInfoSistema eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniRamTotale() As Long
        Try
            Dim query As New System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")
            For Each obj As System.Management.ManagementObject In query.Get()
                Return CLng(obj("TotalVisibleMemorySize")) * 1024L
            Next
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("OttieniRamTotale: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Function OttieniRamDisponibile() As Long
        Try
            Dim query As New System.Management.ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem")
            For Each obj As System.Management.ManagementObject In query.Get()
                Return CLng(obj("FreePhysicalMemory")) * 1024L
            Next
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("OttieniRamDisponibile: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Function FormatBytes(bytes As Long) As String
        If bytes >= 1073741824L Then
            Return Math.Round(bytes / 1073741824.0, 1).ToString() & " GB"
        ElseIf bytes >= 1048576L Then
            Return Math.Round(bytes / 1048576.0, 1).ToString() & " MB"
        ElseIf bytes >= 1024L Then
            Return Math.Round(bytes / 1024.0, 1).ToString() & " KB"
        Else
            Return bytes.ToString() & " B"
        End If
    End Function

    Private Sub MostraRisparmioEnergetico()
        menuEnergia = New WGMenuLista()
        menuEnergia.impostaTitolo("Risparmio energetico")

        menuEnergia.multiselezioneConsentita = False
        menuEnergia.aggiungiElemento("Sospendi computer", "sospendi")
        menuEnergia.aggiungiElemento("Blocca sessione", "blocca")
        AddHandler menuEnergia.escPremuto, AddressOf ChiudiMenuEnergia
        AddHandler menuEnergia.ElementoSelezionato, AddressOf GestisciSceltaEnergia
        menuEnergia.avvia()
    End Sub

    Private Sub ChiudiMenuEnergia()
        Try
            If menuEnergia IsNot Nothing Then menuEnergia.abbandona()
        Catch ex As Exception
            log.ScriviLog("ChiudiMenuEnergia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub GestisciSceltaEnergia()
        Try
            Dim selezione As List(Of String) = menuEnergia.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return

            Dim scelta As String = selezione(0)

            Try
                menuEnergia.abbandona()
            Catch exAbbandona As Exception
                log.ScriviLog("GestisciSceltaEnergia abbandona eccezione: " & exAbbandona.Message)
            End Try

            If scelta = "sospendi" Then
                Application.SetSuspendState(PowerState.Suspend, True, True)
            ElseIf scelta = "blocca" Then
                Process.Start("rundll32.exe", "user32.dll,LockWorkStation")
            End If
        Catch ex As Exception
            log.ScriviLog("GestisciSceltaEnergia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function TrovaPercorsoNVDA() As String
        Return RilevatoreLettoreSchermo.TrovaNvda()
    End Function

    Private Function TrovaPercorsoJAWS() As String
        Return RilevatoreLettoreSchermo.TrovaJaws()
    End Function

    Private Function TrovaPercorsoKSR() As String
        Return RilevatoreLettoreSchermo.TrovaKsr()
    End Function

    ' All'uscita con un altro lettore: KSR, se acceso, va chiuso come gli altri.
    Private Sub ChiudiKsrInUscita()
        For Each p As Process In Process.GetProcessesByName("KSR")
            Try
                p.Kill()
                p.WaitForExit(3000)
            Catch ex As Exception
                log.ScriviLog("Errore chiusura KSR: " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
        Next
    End Sub

    Private Sub ChiudiLettoriDiversiDaKsrInUscita()
        For Each nome As String In New String() {"nvda", "jfw", "Narrator"}
            For Each p As Process In Process.GetProcessesByName(nome)
                Try
                    p.Kill()
                    p.WaitForExit(3000)
                Catch ex As Exception
                    log.ScriviLog("Errore chiusura " & nome & " prima di KSR: " & ex.Message & Environment.NewLine & ex.StackTrace)
                End Try
            Next
        Next
    End Sub

    Private Sub altrefunzioni()
        Dim menufunzioni As New WGMenu()
        menufunzioni.aggiungi("Calendario", AddressOf calendario)
        menufunzioni.aggiungi("Sveglia", AddressOf sveglia)
        menufunzioni.aggiungi("Consulta data e ora attuali", AddressOf orario)
        menufunzioni.aggiungi("Informazioni sistema", AddressOf MostraInfoSistema)
        menufunzioni.aggiungi("Risparmio energetico", AddressOf MostraRisparmioEnergetico)
        ' Le voci dei plugin le mette WGMenu.AllApertura, come per ogni altro menu.
        menufunzioni.avvia("Altre funzioni")
    End Sub

    Private Sub sveglia()
        Try
            Dim m As New MenuSveglia()
            m.Avvia()
        Catch ex As Exception
            log.ScriviLog("sveglia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub treni()
        Try
            Dim m As New MenuTreni()
            m.Avvia()
        Catch ex As Exception
            log.ScriviLog("treni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub intelligenzaArtificiale()
        Try
            Dim menuai As New MenuAi()
            menuai.Avvia()
        Catch ex As Exception
            log.ScriviLog("intelligenzaArtificiale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub calendario()
        Try
            Dim calendariomenu As New MenuCalendario()
            calendariomenu.Avvia()
        Catch ex As Exception
            log.ScriviLog("calendario eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub organizer()
        Dim menuorganizer As New WGMenu
        menuorganizer.Aggiungi("Rubrica", AddressOf rubrica)
        menuorganizer.aggiungi("Agenda", AddressOf agenda)
        menuorganizer.aggiungi("note", AddressOf note)
        menuorganizer.aggiungi("Dati riservati", AddressOf riservato)
        menuorganizer.avvia("Menù Organizer")
    End Sub
    Private Sub rubrica()
        Dim menurubrica As New menurubrica()
        menurubrica.avvia()
    End Sub

    Private Sub riservato()
        Dim menuriservatodati As New MenuRiservato()
    End Sub

    Private Sub note()
        Dim note As New MenuNote()
        note.Avvia()

    End Sub

    Private Sub testi()
        Dim ed As New editor()
        ed.avvia()
    End Sub

    Private Sub gestionefile()
        Dim menufile As New WGMenu
        menufile.aggiungi("Scelta risorsa", AddressOf sceltarisorsa)
        ' Ricerca in tutto il computer, anche nel contenuto dei documenti se si sceglie.
        menufile.aggiungi("Cerca file nel computer", Sub()
                                                        Dim gestione As New MenuFile()
                                                        gestione.CercaNelComputer()
                                                    End Sub)

        menufile.aggiungi("Preferiti", AddressOf preferiti)
        menufile.aggiungi("Recenti", AddressOf recenti)
        menufile.avvia("Menu Gestione File")
    End Sub

    ' Il Media player sostituisce "Ascolto File Multimediali" di Gestione file: stessi brani
    ' preferiti e recenti, stesse preferenze, più il server Navidrome.
    Private Sub mediaplayer()
        Dim player As New MenuMediaPlayer()
        player.Avvia()
    End Sub

    Private Sub BraniPreferiti()
        Dim gestore As New WGFileManager.GestoreBraniPreferitiERecenti()
        gestore.VisualizzaBraniPreferiti()
    End Sub

    Private Sub BraniRecenti()

        Dim gestore As New WGFileManager.GestoreBraniPreferitiERecenti()
        gestore.VisualizzaBraniRecenti()
    End Sub

    Private Sub sceltarisorsa()
        Dim file As New MenuFile()
        file.Avvia()
    End Sub

    Private Sub preferiti()
        Dim menuPreferiti As New WGMenu()
        menuPreferiti.aggiungi("File multimediali preferiti", AddressOf BraniPreferiti)
        menuPreferiti.aggiungi("File preferiti", AddressOf FilePreferitiView)
        menuPreferiti.aggiungi("Cartelle preferite", AddressOf CartellePreferiteView)
        menuPreferiti.avvia("Menu Preferiti")
    End Sub

    Private Sub CartellePreferiteView()
        Dim preferitiview As New Preferiti()

        preferitiview.VisualizzaPreferiti()
    End Sub

    Private Sub FilePreferitiView()
        Dim filepreferitiview As New FilePreferiti()
        filepreferitiview.VisualizzaPreferiti()
    End Sub

    Private Sub recenti()
        Dim recentiview As New Recenti()

        recentiview.VisualizzaRecenti()
    End Sub

    Private Sub agenda()
        Dim agenda As New MenuAgenda()
        agenda.avvia()
    End Sub

    Private Sub web()
        Dim menuweb As New WGMenu()
        menuweb.aggiungi("Wikipedia", AddressOf wikipediaevent)
        menuweb.aggiungi("Quotidiani Online", AddressOf quotidiano)
        menuweb.aggiungi("Nel Quotidiano", AddressOf nelquotidiano)
        menuweb.aggiungi("Biblioteche Online", AddressOf bibliotecheonline)
        menuweb.aggiungi("Ricerca di luoghi e attività", AddressOf luoghi)

        menuweb.aggiungi("Meteo", AddressOf meteo)
        menuweb.aggiungi("Orario treni e pullman", AddressOf treni)
        menuweb.aggiungi("WebTV", AddressOf webtv)
        menuweb.aggiungi("Radio", AddressOf WebRadio)
        menuweb.aggiungi("Podcast", AddressOf podcast)
        menuweb.aggiungi("youtube", AddressOf youtube)
        menuweb.aggiungi("Browser", AddressOf browserweb)
        menuweb.avvia("Menu web")
    End Sub

    ' Funzioni e aperture offerte dalla scrivania (Control Alt D).
    Private Sub RegistraScrivania()
        Try
            WGScrivania.ApriFile = Sub(percorso As String)
                                       Dim gestione As New MenuFile()
                                       gestione.ApriFileDallaCartella(percorso)
                                   End Sub
            WGScrivania.ApriConnessione = Sub() WGConnessioni.WGConnessioni.MostraWifi()
            WGScrivania.ApriBluetooth = Sub() WGConnessioni.WGConnessioni.MostraBluetooth()
            WGScrivania.TestoBluetooth = Function() WGConnessioni.WGConnessioni.DescrizioneBluetooth()
            WGScrivania.MenuWifiBarra = Sub(griglia As WGGriglia) WGConnessioni.WGConnessioni.MenuWifiNellaGriglia(griglia)
            WGScrivania.MenuBluetoothBarra = Sub(griglia As WGGriglia) WGConnessioni.WGConnessioni.MenuBluetoothNellaGriglia(griglia)
            WGScrivania.ApriCartella = Sub(percorso As String)
                                           Dim gestione As New MenuFile()
                                           gestione.Avvia(percorso)
                                       End Sub
            WGScrivania.RegistraFunzione("Editor", AddressOf testi)
            WGScrivania.RegistraFunzione("Gestione file", AddressOf gestionefile)
            WGScrivania.RegistraFunzione("Media player", AddressOf mediaplayer)
            WGScrivania.RegistraFunzione("Organizer", AddressOf organizer)
            WGScrivania.RegistraFunzione("Agenda", AddressOf agenda)
            WGScrivania.RegistraFunzione("Mail", AddressOf mail)
            WGScrivania.RegistraFunzione("Web", AddressOf web)
            WGScrivania.RegistraFunzione("Browser", AddressOf browserweb)
            WGScrivania.RegistraFunzione("Meteo", AddressOf meteo)
            WGScrivania.RegistraFunzione("Radio", AddressOf WebRadio)
            WGScrivania.RegistraFunzione("Podcast", AddressOf podcast)
            WGScrivania.RegistraFunzione("YouTube", AddressOf youtube)
            WGScrivania.RegistraFunzione("Wikipedia", AddressOf wikipediaevent)
            WGScrivania.RegistraFunzione("Quotidiani online", AddressOf quotidiano)
            WGScrivania.RegistraFunzione("Traduttore", AddressOf traduttore)
            WGScrivania.RegistraFunzione("Salute", AddressOf salute)
            WGScrivania.RegistraFunzione("Altre funzioni", AddressOf altrefunzioni)
            WGScrivania.RegistraFunzione("Intelligenza artificiale", AddressOf intelligenzaArtificiale)
            WGScrivania.RegistraFunzione("Impostazioni", AddressOf impostazioni.MostraMenuPrincipale)
            WGScrivania.RegistraFunzione("Centro notifiche", Sub() WGNotifiche.WGCentroNotifiche.MostraCentro())
        Catch ex As Exception
            log.ScriviLog("RegistraScrivania eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub traduttore()
        Try
            WGTraduttore.MostraMenuTraduttore()
        Catch ex As Exception
            log.ScriviLog("traduttore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub browserweb()
        Try
            Global.WGBrowser.WGBrowser.MostraMenuBrowser()
        Catch ex As Exception
            log.ScriviLog("browserweb eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub bibliotecheonline()
        Dim menubiblioteche As New WGMenu()
        menubiblioteche.aggiungi("Biblioteca di WinGabriel", AddressOf bibliotecawingabriel)
        menubiblioteche.aggiungi("LibriVox", AddressOf libri)
        menubiblioteche.aggiungi("Wikisource", AddressOf libritesto)
        menubiblioteche.aggiungi("Internet Archive", AddressOf internetarchive)
        menubiblioteche.avvia("Biblioteche Online")
    End Sub

    Private Sub internetarchive()
        Dim archivio As New MenuInternetArchive
        archivio.avvia()
    End Sub

    Private Sub bibliotecawingabriel()
        Dim biblio As New WGLibriTesto.MenuBibliotecaWinGabriel
        biblio.avvia()
    End Sub

    Private Sub libritesto()
        Dim libri As New WGLibriTesto.MenuBibliotecaTesti
        libri.avvia()
    End Sub
    Private Sub luoghi()
        Dim menuluoghi As New MenuLuoghi()
        menuluoghi.Avvia()
    End Sub

    Private Sub giochi()
        ' da una partita si può scrivere in privato a un altro giocatore con la chat cifrata
        WGGiochi.MenuGiochi.ScriviInPrivato = Sub(nome As String) WGChat.MenuChat.ScriviA(nome)
        Dim menugiochi As New WGGiochi.MenuGiochi()
        menugiochi.Avvia()
    End Sub

    Private Sub chat()
        Dim menuchat As New WGChat.MenuChat()
        menuchat.Avvia()
    End Sub

    Private Sub nelquotidiano()
        Dim menunq As New WGNelQuotidiano.MenuNelQuotidiano()
        menunq.Avvia()
    End Sub

    Private Sub WebRadio()
        Dim menuradio As New MenuRadio()
        menuradio.Avvia()
    End Sub
    Private Sub podcast()
        Dim menupodcast As New MenuPodcast()
        menupodcast.Avvia()
    End Sub

    Private Sub webtv()
        Dim menutv As New MenuTv()
        menutv.Avvia()
    End Sub

    Private Sub youtube()
        Dim menuyoutube As New MenuYoutube()
        menuyoutube.avvia()
    End Sub

    Private Sub meteo()
        Dim menumeteo As New MenuMeteo()
        menumeteo.Avvia()
    End Sub

    Private Sub libri()
        Dim libriaudio As New MenuLibriVox()
        libriaudio.avvia()
    End Sub

    Private Sub quotidiano()
        Dim menugiornali As New MenuQuotidiani()
    End Sub

    Private Sub wikipediaevent()
        Dim wiki As New MenuWikipedia()
    End Sub

    Private Sub orario()
        Dim messaggio As New WGMessaggi()
        messaggio.Messaggio("Oggi è il " & OttieniData() & " e sono le ore: " & OttieniOrarioCorrente())
        messaggio.ShowDialog()
    End Sub

    Private Function OttieniData() As String
        Return DateTime.Now.ToString("dd-MM-yyyy")
    End Function

    Private Function OttieniOrarioCorrente() As String
        Return DateTime.Now.ToString("HH:mm")
    End Function

End Class
