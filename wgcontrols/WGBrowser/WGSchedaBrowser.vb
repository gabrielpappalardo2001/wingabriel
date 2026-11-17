Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

' Classe principale della scheda del browser web: gestione del ciclo di vita della pagina, caricamento, eventi e configurazione
' iniziale.
Partial Public Class WGSchedaBrowser
    Implements IDisposable

    Private Shared _contatoreSchede As Integer = 0
    Private Shared ReadOnly _avvisiMostrati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _lockAvvisi As New Object()

    Private ReadOnly log As New Logger()
    Private ReadOnly lettura As sintesivocale = sintesivocale.GetInstance()
    Private ReadOnly impostazioni As New WGImpostazioniComuni()
    Private ReadOnly _contenitore As WGBrowser
    Private ReadOnly _id As Integer
    Private ReadOnly _popup As Boolean

    Private _pagina As WGPaginaWeb
    Private _devTools As WGDevTools
    Private _albero As WGAlberoAccessibilita
    Private _cdp As WGCdpInterazione
    Private _cursore As WGCursorVirtuale
    Private _navigatore As WGNavigatoreDOM
    Private _ariaMonitor As WGAriaMonitor
    Private _ponte As WGPonteNativo

    Private _gestoreVita As WGBrowserGestoreVita
    Private _gestoreDialoghi As WGBrowserGestoreDialoghi
    Private _gestoreRichieste As WGBrowserGestoreRichieste
    Private _gestoreRisorse As WGBrowserGestoreRisorse
    Private _gestoreDownload As WGBrowserGestoreDownload
    Private _gestorePermessi As WGBrowserGestorePermessi
    Private _gestoreMenu As WGBrowserGestoreMenuContestuale
    Private _gestoreVisualizzazione As WGBrowserGestoreVisualizzazione
    Private _gestoreCaricamento As WGBrowserGestoreCaricamento
    Private _gestoreFrame As WGBrowserGestoreFrame

    Private _indirizzoIniziale As String = ""
    Private _chiusa As Boolean = False
    Private _attiva As Boolean = False
    Private _sospesa As Boolean = False
    Private _titolo As String = ""
    Private _indirizzo As String = ""
    Private _hostPagina As String = ""
    Private _identitaCorrente As String = "chrome"
    Private _istanteUltimoUso As DateTime = DateTime.Now
    Private _annunciaCaricamentoSfondo As Boolean = False
    Private _creataDallaPagina As Boolean = False

    Private _idScriptIniziale As String = ""
    Private _versioneScriptIniziale As Integer = 0
    Private _caricamentoInCorso As Boolean = False
    Private _documentoVisibile As Boolean = False
    Private _idNavigazioneCorrente As ULong = 0UL
    Private _idNavigazioneDownload As ULong = 0UL
    Private _idContenutoCaricato As ULong = 0UL
    Private _indirizzoNavigazionePrincipale As String = ""
    Private _progressoStimato As Double = 0
    Private _timerProgresso As System.Windows.Forms.Timer = Nothing
    Private _istanteUltimoDownload As DateTime = DateTime.MinValue
    Private _istanteRicaricaRichiesta As DateTime = DateTime.MinValue
    Private _istanteUltimoBlocco As DateTime = DateTime.MinValue

    Private modalitaDiretta As Boolean = False
    Private sessioneCampoChiave As String = ""
    Private idCampoChiave As Integer = 0
    Private sessioneUltimoFocus As String = ""
    Private idUltimoFocus As Integer = 0
    Private istanteUltimoFocus As DateTime = DateTime.MinValue
    Private statiUltimoAttivo As String = ""
    Private _valoreUltimoAttivo As String = ""
    Private _contestiFocus As New List(Of String)()
    Private _ultimaCellaFocus As Tuple(Of String, Integer, Integer) = Nothing
    Private _ultimoDiscendenteAnnunciato As String = ""

    Private Class FocusDescritto
        Public Riga As RigaVirtuale
        Public Contesto As String = ""
        Public IdDiscendente As Integer = 0
        Public Ruoli As New List(Of String)()
    End Class

    Private inCampoTesto As Boolean = False
    Private campoMultiriga As Boolean = False
    Private inCasellaCombinata As Boolean = False
    Private campoPassword As Boolean = False
    Private campoConSuggerimenti As Boolean = False
    Private ruoloCampo As String = ""
    Private idCampo As Integer = 0

    Private testoCampo As String = ""
    Private inizioSelezione As Integer = 0
    Private fineSelezione As Integer = 0
    Private versioneTestoCampo As Integer = 0
    Private _versioneSincronizzazione As Integer = 0

    Private paginaPronta As Boolean = False
    Private paginaCostruita As Boolean = False
    Private caricamentoAnnunciato As Boolean = False
    Private numeroNavigazione As Integer = 0
    Private ultimaRicerca As String = ""

    Private attesaFocusUtenteFino As DateTime = DateTime.MinValue
    Private idFocusDaIgnorare As Integer = 0
    Private sessioneFocusDaIgnorare As String = ""
    Private ignoraFocusFino As DateTime = DateTime.MinValue

    Private _istanteUltimoSporco As DateTime = DateTime.MinValue
    Private _aggiornamentoProgrammato As Boolean = False
    Private _istanteUltimoAggiornamentoSfondo As DateTime = DateTime.MinValue
    Private _intervalloMinimoSfondo As Double = 1500
    Private _ultimoTestoInviato As String = ""
    Private _istanteUltimoTestoInviato As DateTime = DateTime.MinValue
    Private _istanteUltimoTasto As DateTime = DateTime.MinValue
    Private _istanteUltimoPassaggio As DateTime = DateTime.MinValue
    Private _ultimoSuggerimento As String = ""
    Private _passwordAnnunciataPer As String = ""
    Private _istanteInizioCaricamento As DateTime = DateTime.MinValue
    Private _istanteUltimoProgresso As DateTime = DateTime.MinValue
    Private _risorseBloccate As Integer = 0
    Private _istanteUltimoCrash As DateTime = DateTime.MinValue
    Private _istanteInizioVerifica As DateTime = DateTime.MinValue
    Private _avvisoAreeDisegnate As Boolean = False
    Private _ultimoVideoAlLettore As String = ""
    Private _videoDallaPagina As String = ""
    Private _privata As Boolean = False
    Private Shared ReadOnly _permessiPrivati As New System.Collections.Concurrent.ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Private Const ScriptFermaVideoYoutube As String =
        "(function(){try{var h=location.hostname.toLowerCase(),p=location.pathname.split('/').filter(function(x){return !!x;}),id='';" &
        "if(h==='youtu.be'||h==='www.youtu.be'){id=p[0]||'';}" &
        "else if(h==='youtube.com'||/\.youtube\.com$/.test(h)){if(p.length===1&&p[0].toLowerCase()==='watch'){id=new URLSearchParams(location.search).get('v')||'';}" &
        "else if(p.length>=2&&(p[0].toLowerCase()==='shorts'||p[0].toLowerCase()==='live')){id=p[1];}}" &
        "if(/^[A-Za-z0-9_-]{11}$/.test(id)){var lib='';try{lib=sessionStorage.getItem('__wgPaginaYt')||'';}catch(e){}if(lib!==id){" & ScriptFermaVideo & "}}}catch(e){}})();"

    Public Sub New(contenitore As WGBrowser, url As String, Optional privata As Boolean = False)
        Try
            _contenitore = contenitore
            _id = Interlocked.Increment(_contatoreSchede)
            _privata = privata
            _indirizzoIniziale = If(url, "")
            _identitaCorrente = WGIdentitaBrowser.IdentitaPerIndirizzo(_indirizzoIniziale)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.New: " & ex.ToString())
        End Try
    End Sub

    Friend Sub New(contenitore As WGBrowser, apritore As WGSchedaBrowser, url As String)
        Try
            _contenitore = contenitore
            _id = Interlocked.Increment(_contatoreSchede)
            _popup = True
            Opener = apritore
            _privata = apritore IsNot Nothing AndAlso apritore.Privata
            _indirizzoIniziale = If(url, "")
            _identitaCorrente = If(apritore IsNot Nothing, apritore.IdentitaCorrente, WGIdentitaBrowser.IdentitaPerIndirizzo(_indirizzoIniziale))
            _hostPagina = WGArchivioBrowser.HostDi(_indirizzoIniziale)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.New popup: " & ex.ToString())
        End Try
    End Sub

    Public Property Opener As WGSchedaBrowser = Nothing

    Public ReadOnly Property Id As Integer
        Get
            Return _id
        End Get
    End Property

    Public ReadOnly Property EPopup As Boolean
        Get
            Return _popup
        End Get
    End Property

    Public ReadOnly Property Contenitore As WGBrowser
        Get
            Return _contenitore
        End Get
    End Property

    Public ReadOnly Property Pagina As WGPaginaWeb
        Get
            Return _pagina
        End Get
    End Property

    Friend ReadOnly Property Core As CoreWebView2
        Get
            Return CoreAttivo()
        End Get
    End Property

    Public ReadOnly Property Chiusa As Boolean
        Get
            Return _chiusa
        End Get
    End Property

    Public ReadOnly Property Sospesa As Boolean
        Get
            Return _sospesa
        End Get
    End Property

    Public ReadOnly Property IstanteUltimoUso As DateTime
        Get
            Return _istanteUltimoUso
        End Get
    End Property

    Public ReadOnly Property ModalitaDirettaAttiva As Boolean
        Get
            Return modalitaDiretta
        End Get
    End Property

    Public ReadOnly Property InCampo As Boolean
        Get
            Return inCampoTesto OrElse inCasellaCombinata
        End Get
    End Property

    Public ReadOnly Property IdentitaCorrente As String
        Get
            Return _identitaCorrente
        End Get
    End Property

    Public ReadOnly Property HostPagina As String
        Get
            Return _hostPagina
        End Get
    End Property

    Public ReadOnly Property Indirizzo As String
        Get
            If _sospesa OrElse (_indirizzo = "" AndAlso _indirizzoIniziale <> "") Then Return _indirizzoIniziale
            Return _indirizzo
        End Get
    End Property

    Public ReadOnly Property Titolo As String
        Get
            Try
                If Not String.IsNullOrWhiteSpace(_titolo) Then Return _titolo
                If _cursore IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_cursore.Titolo) Then Return _cursore.Titolo
                Dim host As String = WGArchivioBrowser.HostDi(Indirizzo)
                If host <> "" Then Return host
            Catch ex As Exception
                log.ScriviLog("WGSchedaBrowser.Titolo: " & ex.Message)
            End Try
            Return "Nuova scheda"
        End Get
    End Property

    Public ReadOnly Property Cursore As WGCursorVirtuale
        Get
            Return _cursore
        End Get
    End Property

    Public Property Attiva As Boolean
        Get
            Return _attiva
        End Get
        Set(value As Boolean)
            ImpostaAttiva(value)
        End Set
    End Property

    Public ReadOnly Property CaricamentoInCorso As Boolean
        Get
            Try
                Return _caricamentoInCorso AndAlso _pagina IsNot Nothing AndAlso _pagina.Pronta
            Catch ex As Exception
                log.ScriviLog("WGSchedaBrowser.CaricamentoInCorso: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Function CoreAttivo() As CoreWebView2
        Try
            If _chiusa OrElse _pagina Is Nothing Then Return Nothing
            Return _pagina.Core
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CoreAttivo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub SuUI(azione As Action)
        Try
            If _chiusa OrElse azione Is Nothing OrElse _contenitore Is Nothing Then Return
            _contenitore.Posticipa(azione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SuUI: " & ex.Message)
        End Try
    End Sub

    Friend Sub DopoEvento(azione As Action, Optional riserva As Action = Nothing)
        Try
            If azione Is Nothing Then Return
            If _chiusa OrElse _contenitore Is Nothing Then
                EseguiRiserva(riserva)
                Return
            End If
            Dim posticipata As Boolean = _contenitore.Posticipa(Sub()
                                                                   Try
                                                                       If _chiusa Then
                                                                           EseguiRiserva(riserva)
                                                                           Return
                                                                       End If
                                                                       azione()
                                                                   Catch exAzione As Exception
                                                                       log.ScriviLog("WGSchedaBrowser.DopoEvento azione: " & exAzione.ToString())
                                                                       EseguiRiserva(riserva)
                                                                   End Try
                                                               End Sub)
            If Not posticipata Then EseguiRiserva(riserva)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DopoEvento: " & ex.Message)
            EseguiRiserva(riserva)
        End Try
    End Sub

    Private Sub EseguiRiserva(riserva As Action)
        Try
            If riserva IsNot Nothing Then riserva()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EseguiRiserva: " & ex.Message)
        End Try
    End Sub

    Private Sub Annuncia(testo As String, Optional urgente As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse Not _attiva Then Return
            If urgente Then
                lettura.LeggiTesto(testo, False, "Alta")
            Else
                lettura.LeggiTesto(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Annuncia: " & ex.Message)
        End Try
    End Sub

    ' Un messaggio all'utente (conferma di un'impostazione, esito): solo WGMessaggi, la voce da sola verrebbe interrotta dal menu che segue.
    Private Sub MessaggioBrowser(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.Show()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MessaggioBrowser: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaSempre(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If _attiva Then
                lettura.LeggiTesto(testo)
            Else
                lettura.LeggiTesto("Scheda " & Titolo & ": " & testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaSempre: " & ex.Message)
        End Try
    End Sub

    Friend Sub AnnunciaDaThread(testo As String)
        SuUI(Sub() AnnunciaSempre(testo))
    End Sub

    Private Sub MostraErrore(testoErrore As String)
        Try
            Dim fm As New WGMessaggi()
            fm.Messaggio(testoErrore)
            fm.Show()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraErrore: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PrimaVolta(chiave As String) As Boolean
        Try
            SyncLock _lockAvvisi
                Return _avvisiMostrati.Add(chiave)
            End SyncLock
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.PrimaVolta: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Sub CompletaRinvio(rinvio As CoreWebView2Deferral)
        Try
            If rinvio IsNot Nothing Then rinvio.Complete()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.CompletaRinvio: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaValore(descrizione As String, azione As Action)
        Try
            If azione IsNot Nothing Then azione()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ImpostaValore " & descrizione & ": " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            If _pagina IsNot Nothing OrElse _chiusa Then Return
            CreaPagina(Nothing, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Avvia: " & ex.ToString())
            MostraErrore("Impossibile creare la scheda del browser: " & ex.Message)
        End Try
    End Sub

    Friend Sub AvviaComePopup(richiesta As CoreWebView2NewWindowRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Try
            If _pagina IsNot Nothing OrElse _chiusa Then
                If richiesta IsNot Nothing Then richiesta.Handled = True
                CompletaRinvio(rinvio)
                Return
            End If
            CreaPagina(richiesta, rinvio)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvviaComePopup: " & ex.ToString())
            Try
                If richiesta IsNot Nothing Then richiesta.Handled = True
            Catch exRichiesta As Exception
                log.ScriviLog("WGSchedaBrowser.AvviaComePopup richiesta: " & exRichiesta.Message)
            End Try
            CompletaRinvio(rinvio)
        End Try
    End Sub

    Private Async Function CreaControllerConAmbienteAsync(ambiente As CoreWebView2Environment, finestra As IntPtr) As Task(Of CoreWebView2Controller)
        Dim opzioni As CoreWebView2ControllerOptions = WGMotoreBrowser.OpzioniController(ambiente, _privata)
        WGTracciaMotore.Scrivi("creazione controller" & If(_privata, " privato", ""))
        If opzioni IsNot Nothing Then Return Await ambiente.CreateCoreWebView2ControllerAsync(finestra, opzioni)
        If _privata Then Return Nothing
        Return Await ambiente.CreateCoreWebView2ControllerAsync(finestra)
    End Function

    Private Async Function CreaControllerAsync(ambiente As CoreWebView2Environment, finestra As IntPtr) As Task(Of CoreWebView2Controller)
        Try
            If ambiente Is Nothing OrElse finestra = IntPtr.Zero Then Return Nothing
            Dim controller As CoreWebView2Controller = Nothing
            Dim conflitto As Exception = Nothing
            Try
                controller = Await CreaControllerConAmbienteAsync(ambiente, finestra)
            Catch exCreazione As Exception
                If Not WGMotoreBrowser.EStatoNonValido(exCreazione) Then Throw
                conflitto = exCreazione
            End Try
            If conflitto IsNot Nothing Then controller = Await RecuperaControllerAsync(ambiente, finestra, conflitto)
            If controller IsNot Nothing Then WGMotoreBrowser.ControllerCreato()
            Return controller
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CreaControllerAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Function RecuperaControllerAsync(ambiente As CoreWebView2Environment, finestra As IntPtr, conflitto As Exception) As Task(Of CoreWebView2Controller)
        Dim entrato As Boolean = False
        Try
            Await WGMotoreBrowser.AttendiRecuperoAsync()
            entrato = True
            If _chiusa Then Return Nothing
            Dim erroreRiprova As String = ""
            Dim attuale As CoreWebView2Environment = WGMotoreBrowser.Ambiente
            If attuale IsNot Nothing AndAlso attuale IsNot ambiente Then
                Try
                    Dim riuscito As CoreWebView2Controller = Await CreaControllerConAmbienteAsync(attuale, finestra)
                    If riuscito IsNot Nothing Then Return riuscito
                Catch exAttuale As Exception
                    erroreRiprova = exAttuale.Message
                End Try
            End If
            Dim stato As String = WGMotoreBrowser.DescriviStato(ambiente, finestra)
            Dim esito As WGEsitoRecuperoMotore = Await WGMotoreBrowser.LiberaMotoreInConflittoAsync()
            If esito Is Nothing Then esito = New WGEsitoRecuperoMotore()
            Dim controller As CoreWebView2Controller = Nothing
            If esito.Liberato AndAlso Not _chiusa Then
                WGMotoreBrowser.AmbienteTerminato(Nothing)
                Dim nuovo As CoreWebView2Environment = Await WGMotoreBrowser.AssicuraAmbienteAsync()
                If nuovo IsNot Nothing Then
                    Try
                        controller = Await CreaControllerConAmbienteAsync(nuovo, finestra)
                    Catch exRiprova As Exception
                        erroreRiprova = exRiprova.Message
                    End Try
                Else
                    erroreRiprova = "il motore non si è riavviato"
                End If
            End If
            If controller Is Nothing AndAlso Not _chiusa Then
                Throw New InvalidOperationException(conflitto.Message & " " & stato & " " & esito.Descrizione & If(erroreRiprova <> "", " Nuovo tentativo non riuscito: " & erroreRiprova, ""), conflitto)
            End If
            Return controller
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RecuperaControllerAsync: " & ex.Message)
            Return Nothing
        Finally
            If entrato Then WGMotoreBrowser.EsciRecupero()
        End Try
    End Function

    Private Async Sub CreaPagina(richiesta As CoreWebView2NewWindowRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim assegnata As Boolean = False
        Try
            assegnata = Await PreparaPaginaAsync(richiesta)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CreaPagina: " & ex.ToString())
            If Not _chiusa Then MostraErrore("Impossibile creare la scheda del browser: " & ex.Message)
        End Try
        Try
            If richiesta IsNot Nothing AndAlso Not assegnata Then richiesta.Handled = True
        Catch exRichiesta As Exception
            log.ScriviLog("WGSchedaBrowser.CreaPagina richiesta: " & exRichiesta.Message)
        End Try
        CompletaRinvio(rinvio)
        Try
            If richiesta Is Nothing OrElse assegnata OrElse _chiusa Then Return
            Dim indirizzoPopup As String = _indirizzoIniziale
            Dim apritore As WGSchedaBrowser = Opener
            SuUI(Sub()
                     Try
                         _contenitore.ChiudiScheda(Me, True)
                         If Not String.IsNullOrWhiteSpace(indirizzoPopup) AndAlso Not indirizzoPopup.StartsWith("about:", StringComparison.OrdinalIgnoreCase) Then _contenitore.NuovaScheda(indirizzoPopup, False, apritore)
                     Catch exRiserva As Exception
                         log.ScriviLog("WGSchedaBrowser.CreaPagina riserva: " & exRiserva.Message)
                     End Try
                 End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CreaPagina chiusura: " & ex.Message)
        End Try
    End Sub

    Private Async Function PreparaPaginaAsync(richiesta As CoreWebView2NewWindowRequestedEventArgs) As Task(Of Boolean)
        Try
            Dim pagina As New WGPaginaWeb(_contenitore)
            _pagina = pagina
            Configura(pagina)
            Dim finestra As IntPtr = _contenitore.FinestraOspite
            If finestra = IntPtr.Zero Then
                log.ScriviLog("WGSchedaBrowser.PreparaPaginaAsync: finestra del browser non disponibile")
                Return False
            End If
            Dim controller As CoreWebView2Controller = Nothing
            Dim motoreDisponibile As Boolean = False
            For tentativo As Integer = 1 To 5
                Dim ambiente As CoreWebView2Environment = Await WGMotoreBrowser.AssicuraAmbienteAsync()
                If ambiente Is Nothing Then Exit For
                motoreDisponibile = True
                controller = Await CreaControllerAsync(ambiente, finestra)
                If controller IsNot Nothing OrElse _chiusa OrElse _pagina IsNot pagina Then Exit For
                log.ScriviLog("WGSchedaBrowser.PreparaPaginaAsync: tentativo " & tentativo & " di creare la pagina non riuscito")
                WGMotoreBrowser.AmbienteTerminato(ambiente)
            Next
            If _chiusa OrElse _pagina IsNot pagina Then
                If controller IsNot Nothing Then WGPaginaWeb.ChiudiController(controller)
                Return False
            End If
            If controller Is Nothing Then
                If PrimaVolta("motore|" & If(motoreDisponibile, "controller", "ambiente") & "|" & DateTime.Now.ToString("yyyyMMddHH")) Then
                    MostraErrore(If(motoreDisponibile, "Il motore del browser non è riuscito ad aprire la scheda. Riprovare tra qualche istante.", "Il motore del browser Microsoft Edge WebView2 non si è avviato. Riavviare WinGabriel e riprovare."))
                End If
                Return False
            End If
            pagina.Imposta(controller)
            ConfiguraController(controller)
            Dim core As CoreWebView2 = controller.CoreWebView2
            If core Is Nothing Then Return False
            ConfiguraCore(core)
            CollegaGestori(core)
            Await RegistraScriptInizialeAsync(core)
            If _chiusa OrElse _pagina IsNot pagina Then Return False
            If _devTools IsNot Nothing Then
                Await _devTools.ImpostaUserAgentAsync(WGIdentitaBrowser.ParametriOverride(_identitaCorrente))
                Await _devTools.CollegaAsync()
            End If
            If _chiusa OrElse _pagina IsNot pagina Then Return False
            pagina.ImpostaVisibile(_attiva)
            If _cdp IsNot Nothing Then _cdp.ImpostaFocusMotore(_attiva)
            If richiesta IsNot Nothing Then
                richiesta.NewWindow = core
                richiesta.Handled = True
                Return True
            End If
            If Not _privata Then
                Dim ripristino As Task = WGAccessiSiti.RipristinaSeServeAsync(core)
                Await Task.WhenAny(ripristino, Task.Delay(6000))
                Dim avvisoAccessi As String = WGAccessiSiti.AvvisoInSospeso()
                If avvisoAccessi <> "" Then _contenitore.Annuncia(avvisoAccessi)
                If _chiusa OrElse _pagina IsNot pagina Then Return False
            End If
            If String.IsNullOrWhiteSpace(_indirizzoIniziale) Then Return True
            Dim destinazione As String = WGBrowser.NormalizzaIndirizzo(_indirizzoIniziale)
            _identitaCorrente = WGIdentitaBrowser.IdentitaPerIndirizzo(destinazione)
            _hostPagina = WGArchivioBrowser.HostDi(destinazione)
            Await ApplicaIdentitaAsync()
            If _chiusa OrElse _pagina IsNot pagina Then Return False
            ' Un sito WordPress si controlla prima di caricarlo: con la lettura semplificata la pagina non si carica affatto.
            If ControllaWordPressPrimaDiAprire(destinazione) Then Return True
            NavigaVerso(core, destinazione)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PreparaPaginaAsync: " & ex.ToString())
            If Not _chiusa Then MostraErrore("Impossibile creare la scheda del browser: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub Configura(pagina As WGPaginaWeb)
        _devTools = New WGDevTools(pagina)
        AddHandler _devTools.EventoRicevuto, AddressOf EventoDevTools
        _albero = New WGAlberoAccessibilita(_devTools)
        _cdp = New WGCdpInterazione(pagina)
        _cdp.DevTools = _devTools
        _cdp.LarghezzaVista = WGMotoreBrowser.LarghezzaVista
        _cdp.AltezzaVista = WGMotoreBrowser.AltezzaVista
        _cursore = New WGCursorVirtuale(pagina, _contenitore, _cdp, _albero)
        _navigatore = New WGNavigatoreDOM(pagina, _cursore, _cdp)
        _ariaMonitor = New WGAriaMonitor(pagina, _contenitore)
        _ariaMonitor.SchedaAttiva = Function() _attiva
        _ariaMonitor.TitoloPagina = Function() Titolo
        _ponte = New WGPonteNativo(Me)
        _gestoreVita = New WGBrowserGestoreVita(Me)
        _gestoreDialoghi = New WGBrowserGestoreDialoghi(Me)
        _gestoreRichieste = New WGBrowserGestoreRichieste(Me)
        _gestoreRisorse = New WGBrowserGestoreRisorse(Me)
        _gestoreDownload = New WGBrowserGestoreDownload(Me)
        _gestorePermessi = New WGBrowserGestorePermessi(Me)
        _gestoreMenu = New WGBrowserGestoreMenuContestuale(Me)
        _gestoreVisualizzazione = New WGBrowserGestoreVisualizzazione(Me)
        _gestoreCaricamento = New WGBrowserGestoreCaricamento(Me)
        _gestoreFrame = New WGBrowserGestoreFrame(Me, pagina)
    End Sub

    Private Sub ConfiguraController(controller As CoreWebView2Controller)
        Try
            If controller Is Nothing Then Return
            ImpostaValore("trascinamento dall'esterno", Sub() controller.AllowExternalDrop = False)
            AddHandler controller.GotFocus, AddressOf ControllerConFocus
            AddHandler controller.MoveFocusRequested, AddressOf SpostamentoFocusRichiesto
            AddHandler controller.AcceleratorKeyPressed, AddressOf TastoAcceleratore
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ConfiguraController: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaController(controller As CoreWebView2Controller)
        Try
            If controller Is Nothing Then Return
            RemoveHandler controller.GotFocus, AddressOf ControllerConFocus
            RemoveHandler controller.MoveFocusRequested, AddressOf SpostamentoFocusRichiesto
            RemoveHandler controller.AcceleratorKeyPressed, AddressOf TastoAcceleratore
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScollegaController: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllerConFocus(sender As Object, e As Object)
        Try
            _contenitore.RiprendiFocus()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllerConFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostamentoFocusRichiesto(sender As Object, e As CoreWebView2MoveFocusRequestedEventArgs)
        Try
            If e IsNot Nothing Then e.Handled = True
            _contenitore.RiprendiFocus()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostamentoFocusRichiesto: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoAcceleratore(sender As Object, e As CoreWebView2AcceleratorKeyPressedEventArgs)
        Try
            If e IsNot Nothing Then e.Handled = True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TastoAcceleratore: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfiguraCore(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Dim impostazioniCore As CoreWebView2Settings = Nothing
            Try
                impostazioniCore = core.Settings
            Catch exImpostazioni As Exception
                log.ScriviLog("WGSchedaBrowser.ConfiguraCore impostazioni: " & exImpostazioni.Message)
            End Try
            If impostazioniCore IsNot Nothing Then
                ImpostaValore("dialoghi", Sub() impostazioniCore.AreDefaultScriptDialogsEnabled = False)
                ImpostaValore("menu contestuale", Sub() impostazioniCore.AreDefaultContextMenusEnabled = True)
                ImpostaValore("strumenti di sviluppo", Sub() impostazioniCore.AreDevToolsEnabled = False)
                ImpostaValore("oggetti host", Sub() impostazioniCore.AreHostObjectsAllowed = False)
                ImpostaValore("barra di stato", Sub() impostazioniCore.IsStatusBarEnabled = False)
                ImpostaValore("controllo dello zoom", Sub() impostazioniCore.IsZoomControlEnabled = False)
                ImpostaValore("messaggi", Sub() impostazioniCore.IsWebMessageEnabled = True)
                ImpostaValore("script", Sub() impostazioniCore.IsScriptEnabled = True)
                ImpostaValore("pagine di errore", Sub() impostazioniCore.IsBuiltInErrorPageEnabled = True)
                ImpostaValore("tasti del browser", Sub() impostazioniCore.AreBrowserAcceleratorKeysEnabled = False)
                ImpostaValore("compilazione automatica", Sub() impostazioniCore.IsGeneralAutofillEnabled = False)
                ImpostaValore("salvataggio delle password", Sub() impostazioniCore.IsPasswordAutosaveEnabled = False)
                ImpostaValore("zoom con le dita", Sub() impostazioniCore.IsPinchZoomEnabled = False)
                ImpostaValore("scorrimento per navigare", Sub() impostazioniCore.IsSwipeNavigationEnabled = False)
                ImpostaValore("barra dei PDF", Sub() impostazioniCore.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save Or CoreWebView2PdfToolbarItems.SaveAs Or CoreWebView2PdfToolbarItems.Print Or CoreWebView2PdfToolbarItems.FullScreen Or CoreWebView2PdfToolbarItems.MoreSettings)
                ImpostaValore("identità", Sub() impostazioniCore.UserAgent = WGIdentitaBrowser.UserAgent(_identitaCorrente))
            End If
            Dim profilo As CoreWebView2Profile = Nothing
            Try
                profilo = core.Profile
            Catch exProfilo As Exception
                log.ScriviLog("WGSchedaBrowser.ConfiguraCore profilo: " & exProfilo.Message)
            End Try
            If profilo IsNot Nothing Then
                If Not _privata Then ImpostaValore("cartella dei download", Sub() profilo.DefaultDownloadFolderPath = WGBrowserGestoreDownload.CartellaScaricamenti())
                ImpostaValore("compilazione automatica del profilo", Sub() profilo.IsGeneralAutofillEnabled = False)
                ImpostaValore("password del profilo", Sub() profilo.IsPasswordAutosaveEnabled = False)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ConfiguraCore: " & ex.Message)
        End Try
    End Sub

    Private Sub CollegaGestori(core As CoreWebView2)
        ImpostaValore("gestore della vita", Sub() _gestoreVita.Collega(core))
        ImpostaValore("gestore dei dialoghi", Sub() _gestoreDialoghi.Collega(core))
        ImpostaValore("gestore delle richieste", Sub() _gestoreRichieste.Collega(core))
        ImpostaValore("gestore delle risorse", Sub() _gestoreRisorse.Collega(core))
        ImpostaValore("gestore dei download", Sub() _gestoreDownload.Collega(core))
        ImpostaValore("gestore dei permessi", Sub() _gestorePermessi.Collega(core))
        ImpostaValore("gestore del menu contestuale", Sub() _gestoreMenu.Collega(core))
        ImpostaValore("gestore della visualizzazione", Sub() _gestoreVisualizzazione.Collega(core))
        ImpostaValore("gestore del caricamento", Sub() _gestoreCaricamento.Collega(core))
        ImpostaValore("gestore dei frame", Sub() _gestoreFrame.Collega(core))
    End Sub

    Private Sub ScollegaGestori()
        If _gestoreVita IsNot Nothing Then ImpostaValore("gestore della vita", Sub() _gestoreVita.Scollega())
        If _gestoreDialoghi IsNot Nothing Then ImpostaValore("gestore dei dialoghi", Sub() _gestoreDialoghi.Scollega())
        If _gestoreRichieste IsNot Nothing Then ImpostaValore("gestore delle richieste", Sub() _gestoreRichieste.Scollega())
        If _gestoreRisorse IsNot Nothing Then ImpostaValore("gestore delle risorse", Sub() _gestoreRisorse.Scollega())
        If _gestoreDownload IsNot Nothing Then ImpostaValore("gestore dei download", Sub() _gestoreDownload.Scollega())
        If _gestorePermessi IsNot Nothing Then ImpostaValore("gestore dei permessi", Sub() _gestorePermessi.Scollega())
        If _gestoreMenu IsNot Nothing Then ImpostaValore("gestore del menu contestuale", Sub() _gestoreMenu.Scollega())
        If _gestoreVisualizzazione IsNot Nothing Then ImpostaValore("gestore della visualizzazione", Sub() _gestoreVisualizzazione.Scollega())
        If _gestoreCaricamento IsNot Nothing Then ImpostaValore("gestore del caricamento", Sub() _gestoreCaricamento.Scollega())
        If _gestoreFrame IsNot Nothing Then ImpostaValore("gestore dei frame", Sub() _gestoreFrame.Scollega())
    End Sub

    Private Async Function RegistraScriptInizialeAsync(core As CoreWebView2) As Task
        Try
            If core Is Nothing Then Return
            _versioneScriptIniziale += 1
            Dim versione As Integer = _versioneScriptIniziale
            Dim nuovo As String = Await core.AddScriptToExecuteOnDocumentCreatedAsync(ScriptInizioDocumento())
            If versione <> _versioneScriptIniziale OrElse _chiusa OrElse CoreAttivo() IsNot core Then
                If Not String.IsNullOrEmpty(nuovo) Then ImpostaValore("rimozione dello script superato", Sub() core.RemoveScriptToExecuteOnDocumentCreated(nuovo))
                Return
            End If
            Dim vecchio As String = _idScriptIniziale
            _idScriptIniziale = If(nuovo, "")
            If Not String.IsNullOrEmpty(vecchio) Then ImpostaValore("rimozione dello script precedente", Sub() core.RemoveScriptToExecuteOnDocumentCreated(vecchio))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RegistraScriptInizialeAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub AggiornaScriptIniziale()
        SuUI(Async Sub()
                 Try
                     Dim core As CoreWebView2 = CoreAttivo()
                     If core IsNot Nothing Then Await RegistraScriptInizialeAsync(core)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AggiornaScriptIniziale: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Function MappaNotifiche() As JObject
        Dim mappa As New JObject()
        Try
            Dim archivioSiti As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivioSiti IsNot Nothing Then
                For Each permesso As PermessoSitoBrowser In archivioSiti.ElencoPermessi()
                    If permesso Is Nothing OrElse permesso.Tipo <> "notifiche" OrElse String.IsNullOrWhiteSpace(permesso.Origine) Then Continue For
                    If _privata AndAlso permesso.Valore <> "blocca" Then Continue For
                    If permesso.Valore = "consenti" Then mappa(permesso.Origine) = "granted"
                    If permesso.Valore = "blocca" Then mappa(permesso.Origine) = "denied"
                Next
            End If
            If _privata Then
                Const Suffisso As String = "|notifiche"
                For Each coppia As KeyValuePair(Of String, String) In _permessiPrivati
                    If coppia.Key Is Nothing OrElse Not coppia.Key.EndsWith(Suffisso, StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim origine As String = coppia.Key.Substring(0, coppia.Key.Length - Suffisso.Length).ToLowerInvariant()
                    If origine = "" Then Continue For
                    If coppia.Value = "consenti" Then mappa(origine) = "granted"
                    If coppia.Value = "blocca" Then mappa(origine) = "denied"
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MappaNotifiche: " & ex.Message)
        End Try
        Return mappa
    End Function

    Friend Function ScriptInizioDocumento() As String
        Try
            Dim parti As New List(Of String)()
            parti.Add("window.__wgNotifiche=" & MappaNotifiche().ToString(Formatting.None) & ";")
            parti.Add(WGCursorVirtuale.ScriptAgente)
            parti.Add(WGIdentitaBrowser.ScriptAntiRilevamento)
            parti.Add(WGIdentitaBrowser.ScriptNascondiPonte)
            If WGImpostazioniBrowser.RiproduzioneVideo <> WGImpostazioniBrowser.RiproduzionePagina Then parti.Add(ScriptFermaVideoYoutube)
            parti.Add("try{delete window.__wgNotifiche;}catch(e){}")
            Return String.Join(";" & vbLf, parti)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScriptInizioDocumento: " & ex.Message)
            Return WGCursorVirtuale.ScriptAgente
        End Try
    End Function

    Private Sub ImpostaAttiva(valore As Boolean)
        Try
            _attiva = valore
            _istanteUltimoUso = DateTime.Now
            If Not valore Then
                If _cursore IsNot Nothing Then _cursore.FermaLetturaContinua()
            End If
            If _pagina IsNot Nothing Then _pagina.ImpostaVisibile(valore)
            If _cdp IsNot Nothing AndAlso _pagina IsNot Nothing AndAlso _pagina.Pronta Then _cdp.ImpostaFocusMotore(valore)
            If valore AndAlso Not String.IsNullOrEmpty(_indirizzo) Then ControllaVideoYoutube(_indirizzo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ImpostaAttiva: " & ex.Message)
        End Try
    End Sub

    Public Sub SegnaUso()
        _istanteUltimoUso = DateTime.Now
    End Sub

    Friend Sub SegnaAperturaInSfondo()
        _annunciaCaricamentoSfondo = True
    End Sub

    Friend Sub SegnaCreataDallaPagina()
        _creataDallaPagina = True
    End Sub

    Public ReadOnly Property Privata As Boolean
        Get
            Return _privata
        End Get
    End Property

    Friend Shared Sub DimenticaSessionePrivata()
        Try
            _permessiPrivati.Clear()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.DimenticaSessionePrivata: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiPermesso(archivioSiti As WGArchivioBrowser, origine As String, tipo As String) As String
        Try
            If _privata Then
                Dim valore As String = Nothing
                If _permessiPrivati.TryGetValue(If(origine, "") & "|" & If(tipo, ""), valore) Then Return If(valore, "")
                Dim salvato As String = If(archivioSiti IsNot Nothing, archivioSiti.PermessoSito(origine, tipo), "")
                Return If(salvato = "blocca", salvato, "")
            End If
            Return If(archivioSiti IsNot Nothing, archivioSiti.PermessoSito(origine, tipo), "")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiPermesso: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ScriviPermesso(archivioSiti As WGArchivioBrowser, origine As String, tipo As String, valore As String)
        Try
            If _privata Then
                _permessiPrivati(If(origine, "") & "|" & If(tipo, "")) = If(valore, "")
            ElseIf archivioSiti IsNot Nothing Then
                archivioSiti.ImpostaPermessoSito(origine, tipo, valore)
            End If
            If tipo = "notifiche" Then _contenitore.AggiornaScriptIniziali()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScriviPermesso: " & ex.Message)
        End Try
    End Sub

    Private ReadOnly Property VoceSempre As String
        Get
            Return If(_privata, "fino alla chiusura della navigazione privata", "sempre")
        End Get
    End Property

    Public Function DescrizioneBreve() As String
        Try
            Dim testo As String = Titolo
            If _privata Then testo &= ", navigazione privata"
            If _sospesa Then testo &= ", sospesa"
            If modalitaDiretta Then testo &= ", controllo completo"
            If CaricamentoInCorso AndAlso Not _documentoVisibile Then testo &= ", caricamento in corso"
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescrizioneBreve: " & ex.Message)
            Return "Scheda"
        End Try
    End Function

    Public Sub AnnunciaAttivazione(prefisso As String)
        Try
            Dim testo As String = If(String.IsNullOrWhiteSpace(prefisso), "", prefisso & ": ") & DescrizioneBreve()
            If _sospesa Then
                lettura.LeggiTesto(testo & ". Riattivazione")
                Riattiva()
                Return
            End If
            If modalitaDiretta Then
                lettura.LeggiTesto(testo)
                Return
            End If
            Dim riga As RigaVirtuale = If(_cursore IsNot Nothing, _cursore.RigaCorrente, Nothing)
            If riga IsNot Nothing Then testo &= ". " & riga.CostruisciAnnuncio(False)
            lettura.LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaAttivazione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaUserAgentImpostazioni()
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then Return
            Dim agente As String = WGIdentitaBrowser.UserAgent(_identitaCorrente)
            If Not String.Equals(core.Settings.UserAgent, agente, StringComparison.Ordinal) Then core.Settings.UserAgent = agente
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaUserAgentImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Async Function ApplicaIdentitaAsync() As Task
        Try
            AggiornaUserAgentImpostazioni()
            If _devTools IsNot Nothing Then Await _devTools.ImpostaUserAgentAsync(WGIdentitaBrowser.ParametriOverride(_identitaCorrente))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ApplicaIdentitaAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub AggiornaPreferenze()
        Try
            If _chiusa Then Return
            AggiornaUserAgentImpostazioni()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaPreferenze: " & ex.Message)
        End Try
    End Sub

    Friend Sub RiapplicaIdentita()
        Try
            If _chiusa OrElse _devTools Is Nothing Then Return
            Dim compito As Task = ApplicaIdentitaAsync()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiapplicaIdentita: " & ex.Message)
        End Try
    End Sub

    Friend Sub PrimaDellaNavigazione(url As String)
        Try
            Dim host As String = WGArchivioBrowser.HostDi(url)
            If host = "" Then Return
            _hostPagina = host
            Dim identita As String = WGIdentitaBrowser.IdentitaPerIndirizzo(url)
            If identita <> _identitaCorrente Then
                _identitaCorrente = identita
                Dim compito As Task = ApplicaIdentitaAsync()
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PrimaDellaNavigazione: " & ex.Message)
        End Try
    End Sub

    Friend Function ENavigazionePrincipale(uri As String) As Boolean
        Try
            If String.IsNullOrEmpty(uri) OrElse String.IsNullOrEmpty(_indirizzoNavigazionePrincipale) Then Return False
            Return String.Equals(SenzaAncora(uri), SenzaAncora(_indirizzoNavigazionePrincipale), StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ENavigazionePrincipale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub NavigaVerso(core As CoreWebView2, destinazione As String)
        Try
            If core Is Nothing OrElse String.IsNullOrWhiteSpace(destinazione) Then Return
            _indirizzoNavigazionePrincipale = destinazione
            WGTracciaMotore.Scrivi("navigate " & If(destinazione, ""))
            core.Navigate(destinazione)
        Catch ex As ArgumentException
            log.ScriviLog("WGSchedaBrowser.NavigaVerso indirizzo non valido: " & ex.Message)
            caricamentoAnnunciato = False
            AnnunciaSempre("Indirizzo non valido: " & destinazione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NavigaVerso: " & ex.Message)
            caricamentoAnnunciato = False
            AnnunciaSempre("Impossibile aprire l'indirizzo " & destinazione)
        End Try
    End Sub

    Friend Sub NavigazioneIniziata(uri As String, idNavigazione As ULong, reindirizzamento As Boolean)
        Try
            If _chiusa Then Return
            _idNavigazioneCorrente = idNavigazione
            _indirizzoNavigazionePrincipale = If(uri, "")
            _caricamentoInCorso = True
            _documentoVisibile = False
            If Not reindirizzamento Then
                _progressoStimato = 0.05
                _istanteInizioCaricamento = DateTime.Now
                _istanteUltimoProgresso = DateTime.Now
            End If
            AvviaStimaProgresso()
            PrimaDellaNavigazione(uri)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NavigazioneIniziata: " & ex.Message)
        End Try
    End Sub

    Friend Sub ContenutoInCaricamento(idNavigazione As ULong, paginaErrore As Boolean)
        SuUI(Sub()
                 Try
                     _idContenutoCaricato = idNavigazione
                     If _progressoStimato < 0.35 Then _progressoStimato = 0.35
                     numeroNavigazione += 1
                     paginaPronta = False
                     paginaCostruita = False
                     _risorseBloccate = 0
                     _passwordAnnunciataPer = ""
                     _avvisoAreeDisegnate = False
                     _istanteUltimoProgresso = DateTime.Now
                     If _devTools IsNot Nothing Then _devTools.DimenticaFrame()
                     _contestiFocus = New List(Of String)()
                     DimenticaFocusDiretto()
                     _ultimaCellaFocus = Nothing
                     _ultimoDiscendenteAnnunciato = ""
                     statiUltimoAttivo = ""
                     _valoreUltimoAttivo = ""
                     If inCampoTesto OrElse inCasellaCombinata Then EsciDalCampo(True)
                     If _cursore IsNot Nothing Then _cursore.FermaLetturaContinua()
                     SospendiTraduzionePerNuovaPagina()
                     DimenticaVerificaAntiRobot()
                     If _ariaMonitor IsNot Nothing Then _ariaMonitor.Ferma()
                     If Not caricamentoAnnunciato AndAlso _attiva Then
                         caricamentoAnnunciato = True
                         lettura.LeggiTesto("Caricamento")
                     End If
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.ContenutoInCaricamento: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    ' Il documento è leggibile: da qui in poi niente più annunci di percentuale, anche se il sito continua
    ' a scaricare immagini, video e pubblicità fino all'evento di navigazione completata.
    Friend Sub DocumentoPronto(idNavigazione As ULong)
        Try
            If idNavigazione = _idNavigazioneCorrente AndAlso _progressoStimato < 0.7 Then _progressoStimato = 0.7
            If idNavigazione = _idNavigazioneCorrente Then
                SuUI(Sub()
                         _documentoVisibile = True
                         FermaStimaProgresso()
                     End Sub)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DocumentoPronto: " & ex.Message)
        End Try
    End Sub

    Friend Sub NavigazioneCompletata(idNavigazione As ULong, riuscita As Boolean, errore As CoreWebView2WebErrorStatus, codiceHttp As Integer)
        SuUI(Sub()
                 Try
                     If _idNavigazioneCorrente <> 0UL AndAlso idNavigazione <> _idNavigazioneCorrente Then Return
                     _caricamentoInCorso = False
                     FermaStimaProgresso()
                     Dim contenutoCaricato As Boolean = (idNavigazione = _idContenutoCaricato)
                     If Not riuscita Then
                         caricamentoAnnunciato = False
                         If errore <> CoreWebView2WebErrorStatus.OperationCanceled AndAlso Not EDownloadDellaNavigazione(idNavigazione) Then
                             SegnalaErroreCaricamento(idNavigazione, errore)
                         End If
                     End If
                     If riuscita OrElse contenutoCaricato Then
                         If _attiva AndAlso _cdp IsNot Nothing Then _cdp.ImpostaFocusMotore(True)
                         If _cursore IsNot Nothing Then _cursore.InserisciAgente()
                         ApplicaZoomSito()
                         RegistraNellaCronologia()
                         AvvisiSito()
                         AggiornaDopoCaricamento()
                     End If
                     If _annunciaCaricamentoSfondo AndAlso Not _attiva Then
                         _annunciaCaricamentoSfondo = False
                         lettura.LeggiTesto("Scheda in secondo piano caricata: " & Titolo)
                     End If
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.NavigazioneCompletata: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub DownloadInArrivo()
        Try
            _istanteUltimoDownload = DateTime.Now
            _idNavigazioneDownload = _idNavigazioneCorrente
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DownloadInArrivo: " & ex.Message)
        End Try
    End Sub

    Private Function EDownloadDellaNavigazione(idNavigazione As ULong) As Boolean
        Try
            If _idNavigazioneDownload <> 0UL AndAlso _idNavigazioneDownload = idNavigazione Then Return True
            Return (DateTime.Now - _istanteUltimoDownload).TotalSeconds < 8
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EDownloadDellaNavigazione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Sub SegnalaErroreCaricamento(idNavigazione As ULong, errore As CoreWebView2WebErrorStatus)
        Try
            Await Task.Delay(1200)
            If _chiusa Then Return
            If _idNavigazioneCorrente <> 0UL AndAlso idNavigazione <> _idNavigazioneCorrente Then Return
            If EDownloadDellaNavigazione(idNavigazione) Then Return
            Dim testoErrore As String = "Impossibile caricare la pagina: " & DescriviErrore(errore)
            If _attiva Then
                MostraErrore(testoErrore)
            Else
                AnnunciaSempre(testoErrore)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SegnalaErroreCaricamento: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaStimaProgresso()
        Try
            If _chiusa Then Return
            If _timerProgresso Is Nothing Then
                _timerProgresso = New System.Windows.Forms.Timer() With {.Interval = 1000}
                AddHandler _timerProgresso.Tick, AddressOf StimaProgresso
            End If
            If Not _timerProgresso.Enabled Then _timerProgresso.Start()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvviaStimaProgresso: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaStimaProgresso()
        Try
            If _timerProgresso IsNot Nothing Then _timerProgresso.Stop()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.FermaStimaProgresso: " & ex.Message)
        End Try
    End Sub

    Private Sub StimaProgresso(sender As Object, e As EventArgs)
        Try
            If _chiusa OrElse Not _caricamentoInCorso OrElse _documentoVisibile Then
                FermaStimaProgresso()
                Return
            End If
            _progressoStimato += (0.95 - _progressoStimato) * 0.08
            ProgressoCaricamento(_progressoStimato)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.StimaProgresso: " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraNellaCronologia()
        Try
            If _privata OrElse Not WGImpostazioniBrowser.SalvaCronologia Then Return
            Dim indirizzoPagina As String = _indirizzo
            Dim titoloPagina As String = _titolo
            If Not WGArchivioBrowser.IndirizzoRegistrabile(indirizzoPagina) Then Return
            Task.Run(Sub()
                         Try
                             Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
                             If archivio IsNot Nothing Then archivio.RegistraVisita(indirizzoPagina, titoloPagina)
                         Catch exArchivio As Exception
                             log.ScriviLog("WGSchedaBrowser.RegistraNellaCronologia archivio: " & exArchivio.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RegistraNellaCronologia: " & ex.Message)
        End Try
    End Sub

    Private Function SitoConContenutiProtetti(host As String) As Boolean
        Try
            Return WGImpostazioniBrowser.EServizioConContenutiProtetti(host)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SitoConContenutiProtetti: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AvvisoContenutiProtetti() As String
        Try
            Dim testo As New System.Text.StringBuilder()
            If _identitaCorrente = WGImpostazioniBrowser.IdentitaEdge Then
                testo.Append("Il browser si presenta come Microsoft Edge e riproduce i contenuti protetti con PlayReady, la protezione di Microsoft. ")
            Else
                testo.Append("Su questo sito il browser si presenta come ").Append(WGImpostazioniBrowser.NomeIdentita(_identitaCorrente)).Append(". Per i contenuti protetti conviene presentarsi come Edge, dal menu Alt, Impostazioni di questo sito, Identità verso il sito: così il sito usa PlayReady, la protezione di Microsoft. ")
            End If
            testo.Append("Widevine di Google non è disponibile nel motore WebView2, quindi i servizi che accettano solo Widevine possono rifiutare la riproduzione")
            If WGMotoreBrowser.UsaRuntimeFisso Then testo.Append(". Attenzione: è in uso la versione fissa del motore WebView2, che non supporta PlayReady; per i contenuti protetti serve il motore WebView2 installato in Windows")
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvvisoContenutiProtetti: " & ex.Message)
            Return "Il browser riproduce i contenuti protetti con PlayReady, la protezione di Microsoft, se il sito lo accetta"
        End Try
    End Function

    Private Sub AvvisiSito()
        Try
            Dim host As String = WGArchivioBrowser.HostDi(_indirizzo)
            If host = "" Then Return
            If SitoConContenutiProtetti(host) AndAlso PrimaVolta("drm|" & host) Then
                AnnunciaSempre("Questo sito usa video o musica protetti. " & AvvisoContenutiProtetti())
            End If
            If host = "docs.google.com" AndAlso System.Text.RegularExpressions.Regex.IsMatch(_indirizzo, "/(document|spreadsheets|presentation)/d/", System.Text.RegularExpressions.RegexOptions.IgnoreCase) AndAlso PrimaVolta("docs|" & host) Then
                AnnunciaSempre("Google Documenti disegna il testo come un'immagine. Per leggerlo e scriverlo attivare una volta il supporto per lettori di schermo: F9 per il controllo completo, poi Control Alt Z")
            End If
            If host.StartsWith("accounts.google.", StringComparison.Ordinal) AndAlso _indirizzo.IndexOf("signin", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso _indirizzo.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0 Then
                GestisciRifiutoGoogle(host)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvvisiSito: " & ex.Message)
        End Try
    End Sub

    Private Async Sub GestisciRifiutoGoogle(host As String)
        Try
            Dim precedente As String = _identitaCorrente
            Dim archivioSiti As WGArchivioBrowser = Archivio
            Dim sceltaDelSito As String = If(archivioSiti IsNot Nothing, WGImpostazioniBrowser.NormalizzaIdentita(archivioSiti.ValoreSito(host, "identita"), ""), "")
            Dim sceltaFissa As Boolean = WGImpostazioniBrowser.EIdentitaFissa(sceltaDelSito) OrElse WGImpostazioniBrowser.IdentitaPredefinita <> WGImpostazioniBrowser.IdentitaAutomatica
            If sceltaFissa Then
                If PrimaVolta("rifiutogoogle|fissa|" & precedente) Then AnnunciaSempre("Google non accetta l'accesso con il browser presentato come " & WGImpostazioniBrowser.NomeIdentita(precedente) & ". L'identità è scelta nelle impostazioni: rimettendola automatica il browser prova da solo l'altra")
                Return
            End If
            Dim altra As String = If(precedente = WGImpostazioniBrowser.IdentitaFirefox, WGImpostazioniBrowser.IdentitaChrome, WGImpostazioniBrowser.IdentitaFirefox)
            If Not PrimaVolta("rifiutogoogle|" & altra) Then
                If PrimaVolta("rifiutogoogle|entrambe") Then AnnunciaSempre("Google non accetta l'accesso né come Chrome né come Firefox. Si può riprovare più tardi: a volte Google chiede prima una verifica dal telefono")
                Return
            End If
            WGImpostazioniBrowser.IdentitaGoogle = altra
            _identitaCorrente = altra
            Await ApplicaIdentitaAsync()
            AnnunciaSempre("Google non accetta l'accesso con il browser presentato come " & WGImpostazioniBrowser.NomeIdentita(precedente) & ": riprovo presentandomi come " & WGImpostazioniBrowser.NomeIdentita(altra) & " e lo ricordo per tutti i servizi Google")
            NavigaIndietro()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciRifiutoGoogle: " & ex.ToString())
        End Try
    End Sub

    Friend Sub AggiornaDaPaginaPronta()
        SuUI(Sub()
                 If Not paginaCostruita Then AggiornaDopoCaricamento()
             End Sub)
    End Sub

    Private Async Sub AggiornaDopoCaricamento()
        Try
            Dim navigazione As Integer = numeroNavigazione
            If Not paginaCostruita Then
                paginaCostruita = True
                _documentoVisibile = True
                FermaStimaProgresso()
                If _devTools IsNot Nothing Then Await _devTools.CollegaAsync()
                Await Task.Delay(150)
                If _chiusa OrElse _cursore Is Nothing Then Return
                Dim riuscito As Boolean = Await _cursore.AggiornaAsync(True)
                If _chiusa OrElse navigazione <> numeroNavigazione Then Return
                If Not riuscito Then
                    paginaCostruita = False
                    Return
                End If
                Await AttendiStabilitaPaginaAsync(navigazione)
                If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
                Await RiepilogoConTraduzioneAsync(navigazione)
            Else
                If _cursore Is Nothing Then Return
                Await _cursore.AggiornaAsync(False)
            End If
            Await Task.Delay(2500)
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
            Await _cursore.AggiornaAsync(False)
            ControllaVerificaSito(navigazione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaDopoCaricamento: " & ex.ToString())
        End Try
    End Sub

    Private Function FirmaPagina() As String
        Try
            If _cursore Is Nothing Then Return ""
            Dim intestazioni As Integer = 0
            Dim righe As List(Of RigaVirtuale) = _cursore.Righe
            For Each r As RigaVirtuale In righe
                If r.Ruolo = "heading" Then intestazioni += 1
            Next
            Return intestazioni.ToString() & "|" & righe.Count.ToString()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.FirmaPagina: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function AttendiStabilitaPaginaAsync(navigazione As Integer) As Task
        Try
            If _cursore Is Nothing Then Return
            ' Sulle pagine pesanti ogni ricostruzione dura già molto: basta un controllo stabile e un tetto più basso,
            ' così il titolo e il riepilogo arrivano prima. Il contenuto continua comunque ad aggiornarsi in sottofondo.
            Dim ultima As String = FirmaPagina()
            Dim stabili As Integer = 0
            Dim richiesti As Integer = 2
            Dim inizio As DateTime = DateTime.Now
            Do While stabili < richiesti AndAlso (DateTime.Now - inizio).TotalMilliseconds < 2500
                Await Task.Delay(300)
                If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
                Dim inizioGiro As DateTime = DateTime.Now
                Await _cursore.AggiornaAsync(False)
                If (DateTime.Now - inizioGiro).TotalMilliseconds > 600 Then richiesti = 1
                Dim attuale As String = FirmaPagina()
                If attuale = ultima Then
                    stabili += 1
                Else
                    stabili = 0
                    ultima = attuale
                End If
            Loop
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttendiStabilitaPaginaAsync: " & ex.Message)
        End Try
    End Function

    Private Function EPaginaDiVerifica() As Boolean
        Try
            Dim t As String = If(_titolo, "").ToLowerInvariant()
            If t.Contains("just a moment") OrElse t.Contains("un momento") OrElse t.Contains("attention required") OrElse t.Contains("checking your browser") OrElse t.Contains("verifica in corso") OrElse t.Contains("ddos-guard") Then Return True
            Return _cursore IsNot Nothing AndAlso _cursore.Righe.Count < 25 AndAlso _cursore.Righe.Exists(Function(r) r.Testo.IndexOf("Cloudflare", StringComparison.OrdinalIgnoreCase) >= 0 OrElse r.Testo.IndexOf("verify you are human", StringComparison.OrdinalIgnoreCase) >= 0 OrElse r.Testo.IndexOf("verifica di essere un essere umano", StringComparison.OrdinalIgnoreCase) >= 0)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EPaginaDiVerifica: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Sub ControllaVerificaSito(navigazione As Integer)
        Try
            If Not EPaginaDiVerifica() Then
                _istanteInizioVerifica = DateTime.MinValue
                Return
            End If
            If _istanteInizioVerifica = DateTime.MinValue Then
                _istanteInizioVerifica = DateTime.Now
                Annuncia("Il sito sta verificando il browser, attendere qualche secondo. Se compare una casella da selezionare, attivarla con Invio")
            End If
            Await Task.Delay(12000)
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse Not EPaginaDiVerifica() Then Return
            Dim host As String = _hostPagina
            If _identitaCorrente = "firefox" AndAlso host <> "" Then
                If Not _privata Then WGImpostazioniBrowser.ImpostaIdentitaSito(host, "chrome")
                _identitaCorrente = "chrome"
                Await ApplicaIdentitaAsync()
                Annuncia("La verifica non procede: riprovo presentandomi come Chrome")
                Ricarica()
            ElseIf PrimaVolta("verifica|" & host) Then
                Annuncia("La verifica del sito non procede. Si può ricaricare la pagina con Control R o riprovare più tardi")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaVerificaSito: " & ex.Message)
        End Try
    End Sub

    ' Prima del riepilogo della pagina si decide la traduzione: se va tradotta da sola, la prima parte viene
    ' tradotta subito e il riepilogo lo dice; se va chiesta, il riepilogo viene letto dopo la risposta.
    Private Async Function RiepilogoConTraduzioneAsync(navigazione As Integer) As Task
        Try
            Dim decisione As Integer = Await DecidiTraduzioneAsync()
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
            If decisione = DecisioneNessuna Then WGTraduttore.ApplicaLinguaLettura(_linguaPaginaTraduzione)
            Dim nota As String = ""
            If decisione = DecisioneTraduci Then nota = Await TraduciPerRiepilogoAsync(navigazione)
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
            Dim riepilogo As String = PaginaCaricataCompletamente(nota, decisione <> DecisioneChiedi)
            If decisione = DecisioneChiedi Then ChiediTraduzioneSito(riepilogo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiepilogoConTraduzioneAsync: " & ex.ToString())
        End Try
    End Function

    Private Function PaginaCaricataCompletamente(Optional nota As String = "", Optional parla As Boolean = True) As String
        Dim testo As String = ""
        Try
            If _ultimoVideoAlLettore <> "" Then FermaVideoDellaPagina()
            paginaPronta = True
            caricamentoAnnunciato = False
            _ariaMonitor.Avvia()
            Dim intestazioni As Integer = 0
            Dim link As Integer = 0
            Dim ultimoLink As Integer = 0
            For Each r As RigaVirtuale In _cursore.Righe
                If r.Ruolo = "heading" Then intestazioni += 1
                If (r.Ruolo = "link" OrElse r.RuoloAttivazione = "link") AndAlso r.IdDaAttivare <> ultimoLink Then
                    link += 1
                    ultimoLink = r.IdDaAttivare
                End If
            Next
            Dim titoloPagina As String = If(String.IsNullOrWhiteSpace(_cursore.Titolo), _titolo, _cursore.Titolo)
            If String.IsNullOrWhiteSpace(titoloPagina) Then titoloPagina = "Pagina senza titolo"
            testo = titoloPagina & ". " & intestazioni & If(intestazioni = 1, " intestazione, ", " intestazioni, ") & link & " link"
            If _cursore.ContieneAreeDisegnate AndAlso Not _avvisoAreeDisegnate Then
                _avvisoAreeDisegnate = True
                testo &= ". Contiene aree disegnate senza testo: il tasto Applicazioni su di esse permette di riconoscerne il testo"
            End If
            Dim ricordato As Boolean = WGImpostazioniBrowser.SitoInControlloCompleto(WGArchivioBrowser.HostDi(_indirizzo))
            If ricordato Then
                If Not modalitaDiretta Then AttivaModalitaDiretta(False, False)
                testo &= ". Controllo completo alla pagina attivo, F9 per la navigazione"
            Else
                If modalitaDiretta Then DisattivaModalitaDiretta(False, False)
                Dim indice As Integer = _cursore.IndiceFocusPagina
                If indice >= 0 AndAlso indice < _cursore.Righe.Count AndAlso _cursore.Righe(indice).IsCampo Then
                    _cursore.SpostaA(indice, False)
                    testo &= ". " & _cursore.Righe(indice).CostruisciAnnuncio(False) & ". Premere Invio per scrivere"
                End If
            End If
            testo = ComponiRiepilogo(testo, nota)
            Dim avvisoVerifica As String = AvvisoVerificaAntiRobot()
            If avvisoVerifica <> "" Then testo = ComponiRiepilogo(testo, avvisoVerifica)
            If parla AndAlso _attiva Then lettura.LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PaginaCaricataCompletamente: " & ex.ToString())
        End Try
        Return testo
    End Function

    Private Shared Function DescriviErrore(codice As CoreWebView2WebErrorStatus) As String
        Select Case codice
            Case CoreWebView2WebErrorStatus.HostNameNotResolved : Return "il sito non esiste o non è raggiungibile"
            Case CoreWebView2WebErrorStatus.Disconnected : Return "la connessione a internet non è disponibile"
            Case CoreWebView2WebErrorStatus.CannotConnect : Return "il sito ha rifiutato la connessione"
            Case CoreWebView2WebErrorStatus.ServerUnreachable : Return "il sito non è raggiungibile"
            Case CoreWebView2WebErrorStatus.Timeout : Return "il sito non risponde"
            Case CoreWebView2WebErrorStatus.ConnectionReset, CoreWebView2WebErrorStatus.ConnectionAborted : Return "la connessione è stata interrotta"
            Case CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect, CoreWebView2WebErrorStatus.CertificateExpired, CoreWebView2WebErrorStatus.CertificateRevoked, CoreWebView2WebErrorStatus.CertificateIsInvalid, CoreWebView2WebErrorStatus.ClientCertificateContainsErrors : Return "il certificato di sicurezza del sito non è valido"
            Case CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse : Return "il sito ha inviato una risposta non valida"
            Case CoreWebView2WebErrorStatus.RedirectFailed : Return "il reindirizzamento del sito non è riuscito"
            Case CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired : Return "il sito richiede nome utente e password validi"
            Case CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired : Return "il proxy richiede nome utente e password validi"
            Case CoreWebView2WebErrorStatus.OperationCanceled : Return "caricamento interrotto"
        End Select
        Return "errore imprevisto"
    End Function

    Friend Sub TitoloCambiato(titoloNuovo As String)
        Try
            If String.IsNullOrWhiteSpace(titoloNuovo) Then Return
            SuUI(Sub()
                     Try
                         _titolo = titoloNuovo
                         If Not _privata AndAlso WGImpostazioniBrowser.SalvaCronologia AndAlso WGArchivioBrowser.IndirizzoRegistrabile(_indirizzo) Then
                             Dim indirizzoPagina As String = _indirizzo
                             Task.Run(Sub()
                                          Try
                                              Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
                                              If archivio IsNot Nothing Then archivio.AggiornaTitoloVisita(indirizzoPagina, titoloNuovo)
                                          Catch exArchivio As Exception
                                              log.ScriviLog("WGSchedaBrowser.TitoloCambiato archivio: " & exArchivio.Message)
                                          End Try
                                      End Sub)
                         End If
                     Catch ex As Exception
                         log.ScriviLog("WGSchedaBrowser.TitoloCambiato UI: " & ex.Message)
                     End Try
                 End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TitoloCambiato: " & ex.Message)
        End Try
    End Sub

    Friend Sub IndirizzoCambiato(nuovo As String)
        SuUI(Sub() GestisciCambioIndirizzo(nuovo))
    End Sub

    Private Shared Function SenzaAncora(url As String) As String
        If String.IsNullOrEmpty(url) Then Return ""
        Dim p As Integer = url.IndexOf("#"c)
        Return If(p >= 0, url.Substring(0, p), url)
    End Function

    Private Shared Function Ancora(url As String) As String
        If String.IsNullOrEmpty(url) Then Return ""
        Dim p As Integer = url.IndexOf("#"c)
        Return If(p >= 0 AndAlso p < url.Length - 1, url.Substring(p + 1), "")
    End Function

    Private Async Sub GestisciCambioIndirizzo(nuovo As String)
        Try
            Dim vecchio As String = _indirizzo
            _indirizzo = nuovo
            Dim host As String = WGArchivioBrowser.HostDi(nuovo)
            If host <> "" Then _hostPagina = host
            If _attiva OrElse _ultimoVideoAlLettore <> "" Then ControllaVideoYoutube(nuovo)
            If Not paginaPronta Then Return
            If String.Equals(SenzaAncora(vecchio), SenzaAncora(nuovo), StringComparison.OrdinalIgnoreCase) Then
                Dim frammento As String = Ancora(nuovo)
                If frammento <> "" AndAlso frammento <> Ancora(vecchio) Then
                    Await SpostaAllAncoraAsync(frammento)
                ElseIf _cursore IsNot Nothing Then
                    _cursore.BufferSporco = True
                End If
                Return
            End If
            Dim navigazione As Integer = numeroNavigazione
            Await Task.Delay(900)
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse Not paginaPronta OrElse _cursore Is Nothing Then Return
            If Await _cursore.AggiornaAsync(True) Then
                RegistraNellaCronologia()
                Await AttendiStabilitaPaginaAsync(navigazione)
                If _chiusa OrElse navigazione <> numeroNavigazione OrElse _cursore Is Nothing Then Return
                Await RiepilogoConTraduzioneAsync(navigazione)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciCambioIndirizzo: " & ex.ToString())
        End Try
    End Sub

    Private Async Function SpostaAllAncoraAsync(frammento As String) As Task(Of Boolean)
        Try
            If String.IsNullOrEmpty(frammento) OrElse _cdp Is Nothing OrElse _cursore Is Nothing Then Return False
            Dim idNodo As Integer = Await _cdp.IdDestinazioneAncoraAsync(frammento)
            If idNodo <= 0 Then Return False
            Await _cursore.AggiornaAsync(False)
            Dim indice As Integer = _cursore.IndiceDestinazione(idNodo)
            If indice < 0 Then Return False
            _cursore.SpostaA(indice, True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostaAllAncoraAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Sub CookieArrivato()
        Try
            If _privata OrElse _chiusa Then Return
            WGAccessiSiti.RichiediSalvataggio()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CookieArrivato: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Un messaggio dello script di WinGabriel nella pagina. origineVera è il sito del documento che l'ha mandato, detto da WebView2;
    ''' se manca (messaggi dal legame DevTools) vale il sito della pagina principale. Il campo "origine" scritto nel messaggio non si usa mai.
    ''' </summary>
    Friend Sub MessaggioDallaPagina(testo As String, Optional origineVera As String = "")
        Try
            If String.IsNullOrEmpty(testo) OrElse _ponte Is Nothing OrElse _chiusa Then Return
            Dim ponte As WGPonteNativo = _ponte
            Dim origine As String = If(String.IsNullOrEmpty(origineVera), WGArchivioBrowser.OrigineDi(_indirizzo), origineVera)
            DopoEvento(Sub() ponte.RiceviMessaggio(testo, origine))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MessaggioDallaPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub EventoDevTools(metodo As String, parametri As JObject, sessione As String)
        Try
            If _chiusa OrElse parametri Is Nothing Then Return
            If metodo = "Runtime.bindingCalled" Then
                If parametri.Value(Of String)("name") = WGDevTools.NomeLegame Then
                    Dim carico As String = parametri.Value(Of String)("payload")
                    If Not String.IsNullOrEmpty(carico) Then MessaggioDallaPagina(carico)
                End If
                Return
            End If
            If metodo = "WG.nodoObsoleto" Then
                SegnaBufferSporco()
                Return
            End If
            If metodo = "Network.responseReceivedExtraInfo" Then
                If Not _privata Then WGAccessiSiti.NotificaRispostaRete(parametri)
                Return
            End If
            If metodo = "Page.fileChooserOpened" Then DopoEvento(Sub() SceltaFileRichiesta(parametri, sessione))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EventoDevTools: " & ex.Message)
        End Try
    End Sub

    Friend Sub SegnaBufferSporco()
        SuUI(Sub()
                 Try
                     _istanteUltimoSporco = DateTime.Now
                     If _cursore IsNot Nothing Then _cursore.SegnaSporcoDalSito()
                     ProgrammaAggiornamentoSfondo()
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.SegnaBufferSporco: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Sub ProgrammaAggiornamentoSfondo()
        Try
            If _chiusa OrElse Not paginaPronta OrElse _aggiornamentoProgrammato Then Return
            If Not (_attiva OrElse modalitaDiretta) Then Return
            _aggiornamentoProgrammato = True
            Dim attesa As Integer = 600
            Dim trascorso As Double = (DateTime.Now - _istanteUltimoAggiornamentoSfondo).TotalMilliseconds
            If trascorso < _intervalloMinimoSfondo Then attesa = Math.Max(attesa, CInt(_intervalloMinimoSfondo - trascorso))
            Task.Delay(attesa).ContinueWith(Sub(t) SuUI(Sub()
                                                            _aggiornamentoProgrammato = False
                                                            EseguiAggiornamentoSfondo()
                                                        End Sub))
        Catch ex As Exception
            _aggiornamentoProgrammato = False
            log.ScriviLog("WGSchedaBrowser.ProgrammaAggiornamentoSfondo: " & ex.Message)
        End Try
    End Sub

    Private Async Sub EseguiAggiornamentoSfondo()
        Try
            If _chiusa OrElse _sospesa OrElse Not paginaPronta OrElse _cursore Is Nothing Then Return
            If Not (_attiva OrElse modalitaDiretta) Then Return
            If _cursore.AggiornamentoInCorso OrElse _cursore.IsLetturaContinuaAttiva Then
                ProgrammaAggiornamentoSfondo()
                Return
            End If
            ' Mentre si salta velocemente tra intestazioni o righe la ricostruzione aspetta la pausa successiva,
            ' ma non oltre qualche secondo, così i contenuti nuovi arrivano comunque anche premendo a lungo.
            If Not modalitaDiretta AndAlso _cursore.NavigazioneRapida AndAlso (DateTime.Now - _istanteUltimoAggiornamentoSfondo).TotalMilliseconds < 4000 Then
                ProgrammaAggiornamentoSfondo()
                Return
            End If
            Dim navigazione As Integer = numeroNavigazione
            Dim inizio As DateTime = DateTime.Now
            Dim riuscito As Boolean = Await _cursore.AggiornaAsync(False)
            Dim durata As Double = (DateTime.Now - inizio).TotalMilliseconds
            _istanteUltimoAggiornamentoSfondo = DateTime.Now
            _intervalloMinimoSfondo = Math.Min(8000, Math.Max(1500, durata * 3))
            If _chiusa OrElse navigazione <> numeroNavigazione Then Return
            If riuscito Then
                AnnunciaNuoviContenuti()
                ControllaVerificaAntiRobotComparsa()
            End If
            If _istanteUltimoSporco > inizio Then ProgrammaAggiornamentoSfondo()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EseguiAggiornamentoSfondo: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaNuoviContenuti()
        Try
            Dim abilitato As Boolean = If(modalitaDiretta, WGImpostazioniBrowser.AnnunciaNuoviContenuti, WGImpostazioniBrowser.AnnunciaNuoviContenutiInNavigazione)
            If Not abilitato OrElse _cursore.IsLetturaContinuaAttiva Then Return
            Dim righe As List(Of RigaVirtuale) = _cursore.UltimeRigheNuove.Where(Function(r) r.Ruolo <> "option" AndAlso r.Ruolo <> "listbox" AndAlso r.Ruolo <> "menuitem" AndAlso r.Ruolo <> "tooltip").ToList()
            If righe.Count = 0 Then Return
            Dim escludi As String = If((DateTime.Now - _istanteUltimoTestoInviato).TotalSeconds < 15, _ultimoTestoInviato, "")
            _ariaMonitor.AnnunciaNuoviContenuti(righe, escludi)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaNuoviContenuti: " & ex.Message)
        End Try
    End Sub

    Private Async Function AttendiStabilitaAsync(massimoMs As Integer) As Task
        Try
            Dim inizio As DateTime = DateTime.Now
            Await Task.Delay(150)
            While (DateTime.Now - inizio).TotalMilliseconds < massimoMs
                If (DateTime.Now - inizio).TotalMilliseconds >= 350 AndAlso (DateTime.Now - _istanteUltimoSporco).TotalMilliseconds >= 300 Then Exit While
                Await Task.Delay(80)
            End While
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttendiStabilitaAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub AggiornaDaFocus(riga As RigaVirtuale)
        SuUI(Sub() GestisciFocus(riga))
    End Sub

    Private Async Function ChiaveElementoAttivoAsync(idAgente As Integer) As Task(Of Tuple(Of String, Integer))
        Try
            If _cursore IsNot Nothing AndAlso _cursore.UsaAlbero AndAlso _devTools IsNot Nothing AndAlso _devTools.ECollegato Then
                Dim attivo As Tuple(Of String, Integer) = Await _devTools.ElementoAttivoAsync()
                If attivo.Item2 > 0 Then Return attivo
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiaveElementoAttivoAsync: " & ex.Message)
        End Try
        Return Tuple.Create("", idAgente)
    End Function

    Private Shared Function Componi(ParamArray parti As String()) As String
        Dim valide As New List(Of String)()
        Try
            For Each parte As String In parti
                If Not String.IsNullOrWhiteSpace(parte) Then valide.Add(parte.Trim())
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.Componi: " & ex.Message)
        End Try
        Return String.Join(", ", valide)
    End Function

    Private ReadOnly Property AlberoDisponibile As Boolean
        Get
            Return _albero IsNot Nothing AndAlso _cursore IsNot Nothing AndAlso _cursore.UsaAlbero AndAlso _devTools IsNot Nothing AndAlso _devTools.ECollegato
        End Get
    End Property

    Private Async Function DescriviDaAlberoAsync(sessione As String, idNodo As Integer, conContesto As Boolean) As Task(Of WGAlberoAccessibilita.DescrizioneNodo)
        Try
            If Not AlberoDisponibile OrElse idNodo <= 0 Then Return Nothing
            Return Await _albero.DescriviNodoAsync(sessione, idNodo, conContesto)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviDaAlberoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function RigaDelBuffer(sessione As String, idNodo As Integer) As RigaVirtuale
        Try
            If _cursore Is Nothing OrElse idNodo <= 0 Then Return Nothing
            Dim indice As Integer = _cursore.IndiceDiNodoOAttivazione(sessione, idNodo)
            If indice < 0 OrElse indice >= _cursore.Righe.Count Then Return Nothing
            Return _cursore.Righe(indice)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RigaDelBuffer: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function UnisciRighe(dalAlbero As RigaVirtuale, sessione As String, idNodo As Integer, rigaAgente As RigaVirtuale) As RigaVirtuale
        Try
            Dim dalBuffer As RigaVirtuale = RigaDelBuffer(sessione, idNodo)
            If dalAlbero Is Nothing Then
                If dalBuffer IsNot Nothing Then Return dalBuffer.Copia()
                Return rigaAgente
            End If
            If dalAlbero.Testo = "" AndAlso dalBuffer IsNot Nothing AndAlso dalBuffer.Testo <> "" Then
                dalAlbero.Testo = dalBuffer.Testo
                If dalBuffer.StessoElemento(sessione, idNodo) AndAlso dalBuffer.Ruolo = "cliccabile" Then dalAlbero.Ruolo = "cliccabile"
            End If
            If dalAlbero.Testo = "" AndAlso rigaAgente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(rigaAgente.Testo) Then dalAlbero.Testo = rigaAgente.Testo
            If dalBuffer IsNot Nothing AndAlso dalAlbero.Lingua = "" Then dalAlbero.Lingua = dalBuffer.Lingua
            RigaVirtuale.Completa(dalAlbero)
            Return dalAlbero
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.UnisciRighe: " & ex.Message)
            Return If(dalAlbero, rigaAgente)
        End Try
    End Function

    Private Function TestoNuoviContesti(descrizione As WGAlberoAccessibilita.DescrizioneNodo) As String
        Try
            If descrizione Is Nothing Then Return ""
            Dim chiavi As New List(Of String)()
            Dim nuovi As New List(Of String)()
            For Each contesto As Tuple(Of String, String) In descrizione.Contesti
                chiavi.Add(contesto.Item1)
                If Not _contestiFocus.Contains(contesto.Item1) Then nuovi.Add(contesto.Item2)
            Next
            _contestiFocus = chiavi
            Return String.Join(", ", nuovi)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TestoNuoviContesti: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoCella(sessione As String, idNodo As Integer, completo As Boolean) As String
        Try
            If _cursore Is Nothing Then Return ""
            Dim trovata As Tuple(Of TabellaVirtuale, CellaVirtuale) = _cursore.CellaDiNodo(sessione, idNodo)
            If trovata Is Nothing Then
                If Not completo Then _ultimaCellaFocus = Nothing
                Return ""
            End If
            Dim tabella As TabellaVirtuale = trovata.Item1
            Dim cella As CellaVirtuale = trovata.Item2
            Dim precedente As Tuple(Of String, Integer, Integer) = _ultimaCellaFocus
            Dim stessaTabella As Boolean = Not completo AndAlso precedente IsNot Nothing AndAlso precedente.Item1 = tabella.Id
            Dim parti As New List(Of String)()
            If Not stessaTabella OrElse precedente.Item2 <> cella.Riga Then
                Dim intestazioneRiga As String = tabella.IntestazioneRiga(cella.Riga, cella)
                parti.Add(Componi(intestazioneRiga, "riga " & cella.Riga.ToString()))
            End If
            If Not stessaTabella OrElse precedente.Item3 <> cella.Colonna Then
                Dim intestazioneColonna As String = tabella.IntestazioneColonna(cella.Colonna, cella)
                parti.Add(Componi(intestazioneColonna, "colonna " & cella.Colonna.ToString()))
            End If
            If Not completo Then _ultimaCellaFocus = Tuple.Create(tabella.Id, cella.Riga, cella.Colonna)
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TestoCella: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function RigaFocusDaAlberoAsync(chiave As Tuple(Of String, Integer), rigaAgente As RigaVirtuale, conContesto As Boolean) As Task(Of FocusDescritto)
        Dim risultato As New FocusDescritto() With {.Riga = rigaAgente}
        Try
            Dim sessione As String = chiave.Item1
            Dim idNodo As Integer = chiave.Item2
            Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(sessione, idNodo, conContesto)
            Dim rigaAlbero As RigaVirtuale = UnisciRighe(If(descrizione IsNot Nothing, descrizione.Riga, Nothing), sessione, idNodo, rigaAgente)
            If rigaAlbero IsNot Nothing Then risultato.Riga = rigaAlbero
            If descrizione IsNot Nothing Then
                risultato.IdDiscendente = descrizione.IdDiscendenteAttivo
                risultato.Ruoli = RuoliDiContesto(descrizione)
            End If
            If conContesto Then risultato.Contesto = Componi(TestoNuoviContesti(descrizione), TestoCella(sessione, idNodo, False))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RigaFocusDaAlberoAsync: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Async Function TestoDiscendenteAsync(sessione As String, idDiscendente As Integer, rigaAgente As RigaVirtuale, soloSeNuovo As Boolean) As Task(Of String)
        Try
            If idDiscendente <= 0 Then Return ""
            Dim chiaveDiscendente As String = If(sessione, "") & "|" & idDiscendente.ToString()
            If soloSeNuovo AndAlso chiaveDiscendente = _ultimoDiscendenteAnnunciato Then Return ""
            Dim discendente As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(sessione, idDiscendente, False)
            Dim rigaAlbero As RigaVirtuale = UnisciRighe(If(discendente IsNot Nothing, discendente.Riga, Nothing), sessione, idDiscendente, rigaAgente)
            If rigaAlbero Is Nothing Then Return ""
            _ultimoDiscendenteAnnunciato = chiaveDiscendente
            If _cursore IsNot Nothing Then _cursore.SincronizzaConNodo(sessione, idDiscendente)
            Return Componi(TestoCella(sessione, idDiscendente, False), rigaAlbero.CostruisciAnnuncio(False))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TestoDiscendenteAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Async Function DescriviOpzioneDaAlberoAsync() As Task(Of String)
        Try
            If Not AlberoDisponibile Then Return Nothing
            Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
            If chiave.Item2 <= 0 Then Return Nothing
            Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, False)
            If descrizione Is Nothing OrElse descrizione.Riga Is Nothing Then Return Nothing
            If descrizione.IdDiscendenteAttivo > 0 Then Return Await TestoDiscendenteAsync(chiave.Item1, descrizione.IdDiscendenteAttivo, Nothing, False)
            Dim fuoriDalCampo As Boolean = idCampoChiave > 0 AndAlso (chiave.Item2 <> idCampoChiave OrElse If(chiave.Item1, "") <> If(sessioneCampoChiave, ""))
            If fuoriDalCampo AndAlso Not descrizione.Riga.IsCampo Then
                If _cursore IsNot Nothing Then _cursore.SincronizzaConNodo(chiave.Item1, chiave.Item2)
                Return descrizione.Riga.CostruisciAnnuncio(False)
            End If
            Dim testo As String = If(descrizione.Riga.Valore, "")
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato IsNot Nothing Then
                Dim posizione As Integer = stato.Value(Of Integer?)("x").GetValueOrDefault()
                Dim totale As Integer = stato.Value(Of Integer?)("n").GetValueOrDefault()
                If testo = "" Then testo = If(stato.Value(Of String)("v"), "")
                If posizione > 0 AndAlso totale > 0 Then testo = Componi(testo, posizione & " di " & totale)
            End If
            Return Componi(testo, descrizione.Riga.Stato)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviOpzioneDaAlberoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function StessoContenuto(dalAlbero As RigaVirtuale, rigaAgente As RigaVirtuale) As Boolean
        Try
            Dim a As String = System.Text.RegularExpressions.Regex.Replace(If(dalAlbero.Testo, "").ToLowerInvariant(), "\s+", "")
            Dim b As String = System.Text.RegularExpressions.Regex.Replace(If(rigaAgente.Testo, "").ToLowerInvariant(), "\s+", "")
            If a = "" OrElse b = "" Then Return True
            Return a.Contains(b) OrElse b.Contains(a)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.StessoContenuto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function RigaAgenteDaAlberoAsync(rigaAgente As RigaVirtuale) As Task(Of RigaVirtuale)
        Try
            If rigaAgente Is Nothing OrElse rigaAgente.IdElemento <= 0 OrElse Not AlberoDisponibile Then Return rigaAgente
            Dim sessioni As New List(Of String)()
            Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
            If chiave.Item2 > 0 Then sessioni.Add(chiave.Item1)
            If Not sessioni.Contains("") Then sessioni.Add("")
            Dim espressione As String = "(window.__wgAgente&&window.__wgAgente.elemento)?window.__wgAgente.elemento(" & rigaAgente.IdElemento.ToString() & "):null"
            For Each sessione As String In sessioni
                Dim idNodo As Integer = Await _devTools.IdNodoDaEspressioneAsync(sessione, espressione)
                If idNodo <= 0 Then Continue For
                Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(sessione, idNodo, False)
                If descrizione Is Nothing OrElse descrizione.Riga Is Nothing Then Continue For
                If Not StessoContenuto(descrizione.Riga, rigaAgente) Then Continue For
                Dim unita As RigaVirtuale = UnisciRighe(descrizione.Riga, sessione, idNodo, rigaAgente)
                If _cursore IsNot Nothing Then _cursore.SincronizzaConNodo(sessione, idNodo)
                Return unita
            Next
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RigaAgenteDaAlberoAsync: " & ex.Message)
        End Try
        Return rigaAgente
    End Function

    Private Function DescriviCampoDaAlbero(rigaAlbero As RigaVirtuale, stato As JObject) As String
        Try
            If rigaAlbero Is Nothing Then Return DescriviCampo(stato)
            If stato IsNot Nothing AndAlso stato.Value(Of Integer?)("i").GetValueOrDefault() <> 0 Then
                If stato.Value(Of Integer?)("pw").GetValueOrDefault() = 1 Then
                    rigaAlbero.Password = True
                    rigaAlbero.LunghezzaPassword = stato.Value(Of Integer?)("n").GetValueOrDefault()
                    rigaAlbero.Valore = ""
                ElseIf Not rigaAlbero.Password AndAlso stato("v") IsNot Nothing Then
                    rigaAlbero.Valore = If(stato.Value(Of String)("v"), "")
                End If
                If rigaAlbero.Testo = "" Then rigaAlbero.Testo = If(stato.Value(Of String)("t"), "")
            End If
            RigaVirtuale.Completa(rigaAlbero)
            Dim testo As String = rigaAlbero.CostruisciAnnuncio(False)
            If stato IsNot Nothing AndAlso Not rigaAlbero.Password AndAlso Not rigaAlbero.Modificabile Then
                Dim posizione As Integer = stato.Value(Of Integer?)("x").GetValueOrDefault()
                Dim totale As Integer = stato.Value(Of Integer?)("n").GetValueOrDefault()
                If posizione > 0 AndAlso totale > 0 Then testo &= ", " & posizione & " di " & totale
            End If
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviCampoDaAlbero: " & ex.Message)
            Return DescriviCampo(stato)
        End Try
    End Function

    Private Async Sub GestisciFocus(riga As RigaVirtuale)
        Try
            If riga Is Nothing OrElse riga.IdElemento = 0 OrElse _cursore Is Nothing Then Return
            Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(riga.IdElemento)
            Dim sessione As String = chiave.Item1
            Dim idNodo As Integer = chiave.Item2
            If idNodo = idUltimoFocus AndAlso sessione = sessioneUltimoFocus AndAlso (DateTime.Now - istanteUltimoFocus).TotalMilliseconds < 500 Then Return
            sessioneUltimoFocus = sessione
            idUltimoFocus = idNodo
            istanteUltimoFocus = DateTime.Now
            If idNodo = idFocusDaIgnorare AndAlso sessione = sessioneFocusDaIgnorare AndAlso DateTime.Now <= ignoraFocusFino Then
                idFocusDaIgnorare = 0
                Return
            End If
            Dim stessoCampo As Boolean = (idNodo = idCampoChiave AndAlso sessione = sessioneCampoChiave)

            If modalitaDiretta Then
                If stessoCampo AndAlso (inCampoTesto OrElse inCasellaCombinata) Then Return
                If WidgetGestitoInCorso Then
                    RimandaFocusDiretto(chiave, riga)
                    Return
                End If
                Await AnnunciaFocusDirettoAsync(chiave, riga)
                Return
            End If

            Dim chiestoDallUtente As Boolean = DateTime.Now <= attesaFocusUtenteFino
            If chiestoDallUtente Then
                attesaFocusUtenteFino = DateTime.MinValue
                Dim corrente As RigaVirtuale = _cursore.RigaCorrente
                If corrente IsNot Nothing AndAlso Not riga.IsCampo AndAlso (corrente.StessoDaAttivare(sessione, idNodo) OrElse corrente.StessoElemento(sessione, idNodo)) Then Return
                Dim focusUtente As FocusDescritto = Await RigaFocusDaAlberoAsync(chiave, riga, False)
                Dim rigaUtente As RigaVirtuale = If(focusUtente.Riga, riga)
                _cursore.SincronizzaConNodo(sessione, idNodo)
                If riga.IsCampo OrElse rigaUtente.IsCampo Then
                    _cdp.SessioneCampo = If(_cursore.UsaAlbero, sessione, Nothing)
                    Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
                    EntraNelCampo(stato, True, sessione, idNodo, rigaUtente)
                Else
                    If inCampoTesto OrElse inCasellaCombinata Then EsciDalCampo(True)
                    Annuncia(rigaUtente.CostruisciAnnuncio(False))
                End If
                Return
            End If
            If (inCampoTesto OrElse inCasellaCombinata) AndAlso Not stessoCampo Then
                Dim focusCambiato As FocusDescritto = Await RigaFocusDaAlberoAsync(chiave, riga, inCasellaCombinata)
                Dim rigaCambiata As RigaVirtuale = If(focusCambiato.Riga, riga)
                _cursore.SincronizzaConNodo(sessione, idNodo)
                If inCasellaCombinata AndAlso Not rigaCambiata.IsCampo AndAlso Not riga.IsCampo AndAlso ElementoDentroWidget(focusCambiato) Then
                    If Not WidgetGestitoInCorso Then Annuncia(rigaCambiata.CostruisciAnnuncio(False))
                    Return
                End If
                If riga.IsCampo OrElse rigaCambiata.IsCampo Then
                    _cdp.SessioneCampo = If(_cursore.UsaAlbero, sessione, Nothing)
                    Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
                    EntraNelCampo(stato, True, sessione, idNodo, rigaCambiata)
                Else
                    EsciDalCampo(True)
                    Annuncia(rigaCambiata.CostruisciAnnuncio(False))
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciFocus: " & ex.ToString())
        End Try
    End Sub

    Private Async Function AnnunciaFocusDirettoAsync(chiave As Tuple(Of String, Integer), rigaAgente As RigaVirtuale) As Task
        Try
            Dim sessione As String = chiave.Item1
            Dim idNodo As Integer = chiave.Item2
            _ultimoDiscendenteAnnunciato = ""
            Dim focusDiretto As FocusDescritto = Await RigaFocusDaAlberoAsync(chiave, rigaAgente, True)
            If Not modalitaDiretta OrElse _chiusa OrElse idUltimoFocus <> idNodo OrElse sessioneUltimoFocus <> sessione Then Return
            Dim rigaDiretta As RigaVirtuale = focusDiretto.Riga
            If rigaDiretta Is Nothing Then Return
            _cursore.SincronizzaConNodo(sessione, idNodo)
            RicordaFocusDiretto(rigaDiretta, focusDiretto.IdDiscendente, focusDiretto.Ruoli)
            statiUltimoAttivo = If(rigaDiretta.Stato, "")
            _valoreUltimoAttivo = If(rigaDiretta.Valore, "")
            Dim discendente As String = Await TestoDiscendenteAsync(sessione, focusDiretto.IdDiscendente, Nothing, False)
            If rigaDiretta.IsCampo OrElse (rigaAgente IsNot Nothing AndAlso rigaAgente.IsCampo) Then
                _cdp.SessioneCampo = If(_cursore.UsaAlbero, sessione, Nothing)
                Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
                EntraNelCampo(stato, False, sessione, idNodo)
                Annuncia(Componi(focusDiretto.Contesto, DescriviCampoDaAlbero(rigaDiretta, stato), discendente))
            Else
                EsciDalCampo(True)
                Annuncia(Componi(focusDiretto.Contesto, rigaDiretta.CostruisciAnnuncio(False), discendente))
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaFocusDirettoAsync: " & ex.ToString())
        End Try
    End Function

    Private Async Function DescriviDiscendenteAttivoAsync(rigaAgente As RigaVirtuale) As Task(Of String)
        Try
            If AlberoDisponibile Then
                Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
                If chiave.Item2 > 0 Then
                    Dim contenitore As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, False)
                    If contenitore IsNot Nothing AndAlso contenitore.IdDiscendenteAttivo > 0 Then
                        Return Await TestoDiscendenteAsync(chiave.Item1, contenitore.IdDiscendenteAttivo, rigaAgente, True)
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviDiscendenteAttivoAsync: " & ex.Message)
        End Try
        Return If(rigaAgente IsNot Nothing, rigaAgente.CostruisciAnnuncio(False), "")
    End Function

    Friend Async Function DescriviElementoAFuocoAsync() As Task(Of String)
        Try
            Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
            If chiave.Item2 > 0 AndAlso AlberoDisponibile Then
                Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, True)
                Dim rigaAlbero As RigaVirtuale = UnisciRighe(If(descrizione IsNot Nothing, descrizione.Riga, Nothing), chiave.Item1, chiave.Item2, Nothing)
                If rigaAlbero IsNot Nothing Then
                    Dim contesti As String = If(descrizione IsNot Nothing, String.Join(", ", descrizione.Contesti.Select(Function(c) c.Item2)), "")
                    Dim testo As String = Componi(contesti, TestoCella(chiave.Item1, chiave.Item2, True), rigaAlbero.CostruisciAnnuncio(False))
                    If descrizione IsNot Nothing AndAlso descrizione.IdDiscendenteAttivo > 0 Then
                        Dim discendente As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, descrizione.IdDiscendenteAttivo, False)
                        If discendente IsNot Nothing AndAlso discendente.Riga IsNot Nothing Then testo = Componi(testo, discendente.Riga.CostruisciAnnuncio(False))
                    End If
                    _cursore.SincronizzaConNodo(chiave.Item1, chiave.Item2)
                    Return testo
                End If
            End If
            Dim rigaAgente As RigaVirtuale = RigaVirtuale.DaJson(Await _cdp.DescriviAttivoAsync())
            If rigaAgente Is Nothing OrElse rigaAgente.IdElemento = 0 Then Return "Nessun elemento a fuoco"
            Return rigaAgente.CostruisciAnnuncio(False)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviElementoAFuocoAsync: " & ex.Message)
            Return "Nessun elemento a fuoco"
        End Try
    End Function

    Friend Async Function AnnunciaCambioStatoAsync(rigaAgente As RigaVirtuale) As Task
        Try
            Dim stati As String = Nothing
            Dim valore As String = Nothing
            Dim ruolo As String = ""
            If AlberoDisponibile Then
                Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
                If chiave.Item2 > 0 Then
                    Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, False)
                    If descrizione IsNot Nothing AndAlso descrizione.Riga IsNot Nothing Then
                        stati = If(descrizione.Riga.Stato, "")
                        valore = If(descrizione.Riga.Valore, "")
                        ruolo = If(descrizione.Riga.Ruolo, "")
                    End If
                End If
            End If
            If stati Is Nothing Then
                If rigaAgente Is Nothing Then rigaAgente = RigaVirtuale.DaJson(Await _cdp.DescriviAttivoAsync())
                If rigaAgente Is Nothing Then Return
                stati = If(rigaAgente.Stato, "")
                valore = If(rigaAgente.Valore, "")
                ruolo = If(rigaAgente.Ruolo, "")
            End If
            If Not modalitaDiretta OrElse inCampoTesto Then Return
            If stati <> statiUltimoAttivo Then
                statiUltimoAttivo = stati
                _valoreUltimoAttivo = valore
                If stati <> "" Then
                    Annuncia(stati)
                ElseIf valore <> "" Then
                    Annuncia(valore)
                End If
                Return
            End If
            If valore <> _valoreUltimoAttivo Then
                _valoreUltimoAttivo = valore
                If valore <> "" Then
                    If ruolo = "slider" AndAlso EValoreTempo(valore) Then
                        If SliderTempoRecente Then Return
                        Annuncia(PosizioneDaValoreTempo(valore))
                    Else
                        Annuncia(valore)
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaCambioStatoAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub AggiornaDaDiscendenteAttivo(riga As RigaVirtuale)
        SuUI(Async Sub()
                 Try
                     If riga Is Nothing OrElse Not modalitaDiretta OrElse inCampoTesto OrElse WidgetGestitoInCorso Then Return
                     Dim testo As String = Await DescriviDiscendenteAttivoAsync(riga)
                     If Not modalitaDiretta OrElse inCampoTesto OrElse String.IsNullOrWhiteSpace(testo) Then Return
                     Annuncia(testo)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AggiornaDaDiscendenteAttivo: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub AggiornaDaSelezione(riga As RigaVirtuale)
        SuUI(Async Sub()
                 Try
                     If riga Is Nothing OrElse Not modalitaDiretta OrElse inCampoTesto OrElse WidgetGestitoInCorso Then Return
                     If Not WGImpostazioniBrowser.AnnunciaSelezione Then Return
                     Dim rigaAlbero As RigaVirtuale = Await RigaAgenteDaAlberoAsync(riga)
                     If Not modalitaDiretta OrElse inCampoTesto OrElse rigaAlbero Is Nothing Then Return
                     Annuncia(rigaAlbero.CostruisciAnnuncio(False))
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AggiornaDaSelezione: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub AggiornaDaStato(riga As RigaVirtuale)
        SuUI(Async Sub()
                 Try
                     If riga Is Nothing OrElse Not modalitaDiretta OrElse inCampoTesto OrElse WidgetGestitoInCorso Then Return
                     Await AnnunciaCambioStatoAsync(riga)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AggiornaDaStato: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub AggiornaDaRegioneLive(testo As String, assertivo As Boolean)
        SuUI(Sub()
                 If SliderTempoRecente Then Return
                 If _ariaMonitor IsNot Nothing Then _ariaMonitor.RiceviNotifica(testo, assertivo)
             End Sub)
    End Sub

    Friend Sub AggiornaDaDialogo(riga As RigaVirtuale)
        SuUI(Async Sub()
                 Try
                     Dim rigaDialogo As RigaVirtuale = riga
                     If riga IsNot Nothing Then rigaDialogo = Await RigaAgenteDaAlberoAsync(riga)
                     If _ariaMonitor IsNot Nothing Then _ariaMonitor.RiceviDialogo(rigaDialogo)
                     If _cursore IsNot Nothing Then
                         _cursore.BufferSporco = True
                         Await _cursore.AggiornaAsync(False)
                     End If
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AggiornaDaDialogo: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub NotificaDelSito(titoloNotifica As String, corpo As String, origine As String)
        SuUI(Sub()
                 Try
                     Dim sito As String = If(WGArchivioBrowser.HostDi(origine) <> "", WGArchivioBrowser.HostDi(origine), Titolo)
                     ' Se il browser è in sottofondo la notifica la legge il centro notifiche, con il nome dell'applicazione.
                     Dim inSottofondo As Boolean = _contenitore IsNot Nothing AndAlso _contenitore.InSottofondo()
                     If _ariaMonitor IsNot Nothing Then _ariaMonitor.RiceviNotificaSito(titoloNotifica, corpo, sito, Not inSottofondo)
                     Dim testoNotifica As String = (If(titoloNotifica, "").Trim() & If(String.IsNullOrWhiteSpace(corpo), "", ": " & corpo.Trim())).Trim(":"c, " "c)
                     If testoNotifica <> "" AndAlso _contenitore IsNot Nothing Then
                         Dim scheda As WGSchedaBrowser = Me
                         WGNotifiche.WGCentroNotifiche.Pubblica("", sito & ": " & testoNotifica, Sub() _contenitore.MostraSchedaDaNotifica(scheda), _contenitore, annuncia:=inSottofondo)
                     End If
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.NotificaDelSito: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub FlutterAttivato()
        SuUI(Async Sub()
                 Try
                     Await Task.Delay(600)
                     If _cursore Is Nothing OrElse _chiusa Then Return
                     _cursore.ContieneSemanticaFlutter = True
                     If Await _cursore.AggiornaAsync(False) Then Annuncia("Accessibilità dell'applicazione Flutter attivata, " & _cursore.Righe.Count & " righe")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.FlutterAttivato: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub ErroreMedia(sorgente As String, codice As Integer, video As Boolean)
        SuUI(Sub()
                 Try
                     If Not PrimaVolta("media|" & _indirizzo & "|" & sorgente) Then Return
                     If WGImpostazioniBrowser.NonSupportatiNelLettore AndAlso _ultimoVideoAlLettore = "" AndAlso PrimaVolta("medialettore|" & SenzaAncora(_indirizzo)) Then
                         Dim indirizzoMedia As String = If(sorgente.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse sorgente.StartsWith("https://", StringComparison.OrdinalIgnoreCase), sorgente, _indirizzo)
                         AffidaAlLettore(indirizzoMedia, If(video, "Il video", "L'audio") & " di questa pagina è in un formato che il browser non supporta: lo riproduco con il lettore di WinGabriel, che ha tutti i codec")
                         Return
                     End If
                     AnnunciaSempre(If(video, "Il video", "L'audio") & " di questa pagina non è supportato dal browser. Control Shift V per riprodurlo con il lettore di WinGabriel")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.ErroreMedia: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub MediaProtetto()
        SuUI(Sub()
                 Try
                     If Not PrimaVolta("drmpagina|" & _hostPagina) Then Return
                     If SitoConContenutiProtetti(_hostPagina) Then Return
                     AnnunciaSempre("La pagina usa contenuti protetti. " & AvvisoContenutiProtetti())
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.MediaProtetto: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub RisorsaBloccata()
        Interlocked.Increment(_risorseBloccate)
    End Sub

    Friend Sub ProgressoCaricamento(progresso As Double)
        Try
            If progresso >= 1 OrElse progresso <= 0 Then Return
            SuUI(Sub()
                     Try
                         If Not _attiva OrElse modalitaDiretta OrElse _documentoVisibile Then Return
                         If (DateTime.Now - _istanteInizioCaricamento).TotalSeconds < 4 Then Return
                         If (DateTime.Now - _istanteUltimoProgresso).TotalSeconds < 5 Then Return
                         _istanteUltimoProgresso = DateTime.Now
                         lettura.LeggiTesto("Caricamento " & CInt(progresso * 100) & " percento")
                     Catch ex As Exception
                         log.ScriviLog("WGSchedaBrowser.ProgressoCaricamento UI: " & ex.Message)
                     End Try
                 End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ProgressoCaricamento: " & ex.Message)
        End Try
    End Sub

    Friend Sub SuggerimentoCambiato(testo As String)
        Try
            Dim pulito As String = WGAlberoAccessibilita.Normalizza(testo)
            SuUI(Sub()
                     Try
                         If pulito = "" OrElse pulito = _ultimoSuggerimento Then
                             _ultimoSuggerimento = pulito
                             Return
                         End If
                         _ultimoSuggerimento = pulito
                         If (DateTime.Now - _istanteUltimoPassaggio).TotalSeconds > 4 AndAlso Not modalitaDiretta Then Return
                         WGAriaMonitor.Registra(pulito, "regione", Titolo)
                         Annuncia("Suggerimento: " & pulito)
                     Catch ex As Exception
                         log.ScriviLog("WGSchedaBrowser.SuggerimentoCambiato UI: " & ex.Message)
                     End Try
                 End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SuggerimentoCambiato: " & ex.Message)
        End Try
    End Sub

    Friend Sub SchermoInteroCambiato(schermoIntero As Boolean)
        SuUI(Sub() Annuncia(If(schermoIntero, "Schermo intero attivato", "Schermo intero disattivato")))
    End Sub

    Friend Sub ProcessoTerminato(nonRisponde As Boolean)
        SuUI(Sub()
                 Try
                     If _chiusa Then Return
                     If nonRisponde Then
                         If (DateTime.Now - _istanteUltimoBlocco).TotalSeconds < 60 Then Return
                         _istanteUltimoBlocco = DateTime.Now
                         AnnunciaSempre("La pagina non risponde. Control R per ricaricarla")
                         Return
                     End If
                     Dim ripetuto As Boolean = (DateTime.Now - _istanteUltimoCrash).TotalSeconds < 60
                     _istanteUltimoCrash = DateTime.Now
                     If ripetuto Then
                         AnnunciaSempre("La pagina si è interrotta di nuovo. Control R per ricaricarla")
                         Return
                     End If
                     AnnunciaSempre("La pagina si è interrotta in modo anomalo e viene ricaricata")
                     Ricarica()
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.ProcessoTerminato: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub MotoreArrestato()
        SuUI(Sub()
                 Try
                     If _chiusa Then Return
                     _contenitore.NotificaArrestoMotore()
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.MotoreArrestato: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub RiavviaDopoArresto()
        Try
            If _chiusa OrElse _sospesa Then Return
            Sospendi()
            If _attiva Then Riattiva()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiavviaDopoArresto: " & ex.Message)
        End Try
    End Sub

    Friend Sub BrowserChiuso()
        SuUI(Sub()
                 Try
                     If _chiusa OrElse _sospesa Then Return
                     _contenitore.SchedaChiusaDallaPagina(Me)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.BrowserChiuso: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub GestisciNuovaFinestra(e As CoreWebView2NewWindowRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim affidata As Boolean = False
        Dim url As String = ""
        Try
            e.Handled = True
            If _chiusa Then Return
            url = If(e.Uri, "")
            If _contenitore IsNot Nothing AndAlso _contenitore.ModoLettore Then
                ' Il lettore ha una pagina sola: le nuove finestre si aprono fuori, come i collegamenti.
                Dim destinazione As String = url
                If destinazione <> "" Then DopoEvento(Sub() WGImpostazioniComuni.ApriLinkConBrowserPreferito(destinazione))
                Return
            End If
            Dim gestoUtente As Boolean = False
            Try
                gestoUtente = e.IsUserInitiated
            Catch exGesto As Exception
                log.ScriviLog("WGSchedaBrowser.GestisciNuovaFinestra gesto: " & exGesto.Message)
            End Try
            Dim intenzione As String = If(_cdp IsNot Nothing, _cdp.IntenzioneNuovaFinestra(), "")
            Dim sfondo As Boolean = (intenzione = "sfondo")
            If Not gestoUtente AndAlso intenzione = "" AndAlso WGImpostazioniBrowser.BloccoPubblicita Then
                AnnunciaDaThread("Finestra aperta dalla pagina senza richiesta, bloccata")
                Return
            End If
            Dim scheda As New WGSchedaBrowser(_contenitore, Me, url)
            scheda.SegnaCreataDallaPagina()
            _contenitore.AggiungiSchedaDaThread(scheda, Not sfondo)
            If scheda.Chiusa Then Return
            affidata = True
            scheda.AvviaComePopup(e, rinvio)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciNuovaFinestra: " & ex.ToString())
            Try
                If Not affidata AndAlso Not String.IsNullOrEmpty(url) Then ApriInNuovaSchedaDaThread(url, False)
            Catch exRiserva As Exception
                log.ScriviLog("WGSchedaBrowser.GestisciNuovaFinestra riserva: " & exRiserva.Message)
            End Try
        Finally
            If Not affidata Then CompletaRinvio(rinvio)
        End Try
    End Sub

    Friend Sub ApriInNuovaSchedaDaThread(url As String, sfondo As Boolean)
        SuUI(Sub()
                 Try
                     _contenitore.NuovaScheda(url, sfondo, Me)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.ApriInNuovaSchedaDaThread: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Public Sub Naviga(url As String)
        Try
            If _sospesa Then
                _indirizzoIniziale = url
                Riattiva()
                Return
            End If
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then
                _indirizzoIniziale = If(url, "")
                If _pagina Is Nothing AndAlso Not _chiusa Then Avvia()
                Return
            End If
            Dim destinazione As String = WGBrowser.NormalizzaIndirizzo(url)
            ' Un sito WordPress si controlla prima di caricarlo: con la lettura semplificata la pagina non si carica affatto.
            If ControllaWordPressPrimaDiAprire(destinazione) Then Return
            PrimaDellaNavigazione(destinazione)
            caricamentoAnnunciato = True
            NavigaVerso(core, destinazione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Naviga: " & ex.Message)
        End Try
    End Sub

    Public Sub NavigaAvanti()
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then Return
            If core.CanGoForward Then
                caricamentoAnnunciato = True
                WGTracciaMotore.Scrivi("avanti")
                core.GoForward()
                lettura.LeggiTesto("Pagina successiva")
            Else
                lettura.LeggiTesto("Nessuna pagina successiva")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NavigaAvanti: " & ex.Message)
        End Try
    End Sub

    Public Sub NavigaIndietro()
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then Return
            If core.CanGoBack Then
                caricamentoAnnunciato = True
                WGTracciaMotore.Scrivi("indietro")
                core.GoBack()
                lettura.LeggiTesto("Pagina precedente")
            ElseIf _popup AndAlso Opener IsNot Nothing AndAlso Not Opener.Chiusa Then
                lettura.LeggiTesto("Nessuna pagina precedente in questa scheda. Control W per chiuderla e tornare alla scheda da cui è stata aperta")
            Else
                lettura.LeggiTesto("Nessuna pagina precedente")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NavigaIndietro: " & ex.Message)
        End Try
    End Sub

    Public Sub Ricarica()
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then Return
            If _cursore IsNot Nothing Then _cursore.FermaLetturaContinua()
            If _ariaMonitor IsNot Nothing Then _ariaMonitor.Ferma()
            caricamentoAnnunciato = True
            _istanteRicaricaRichiesta = DateTime.Now
            WGTracciaMotore.Scrivi("ricarica")
            core.Reload()
            lettura.LeggiTesto("Pagina ricaricata")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Ricarica: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core Is Nothing Then Return
            WGTracciaMotore.Scrivi("ferma")
            core.Stop()
            lettura.LeggiTesto("Caricamento interrotto")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Ferma: " & ex.Message)
        End Try
    End Sub

    Public Sub Sospendi()
        Try
            If _sospesa OrElse _chiusa OrElse _pagina Is Nothing Then Return
            If _indirizzo <> "" Then _indirizzoIniziale = _indirizzo
            DistruggiBrowser()
            _sospesa = True
            paginaPronta = False
            paginaCostruita = False
            modalitaDiretta = False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Sospendi: " & ex.Message)
        End Try
    End Sub

    Public Sub Riattiva()
        Try
            If Not _sospesa OrElse _chiusa Then Return
            _sospesa = False
            _indirizzo = ""
            Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Riattiva: " & ex.Message)
        End Try
    End Sub

    Private Sub DistruggiBrowser()
        Try
            If _cursore IsNot Nothing Then _cursore.FermaLetturaContinua()
            If _ariaMonitor IsNot Nothing Then _ariaMonitor.Ferma()
            FermaStimaProgresso()
            _caricamentoInCorso = False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DistruggiBrowser arresto: " & ex.Message)
        End Try
        Try
            Dim collegamento As WGDevTools = _devTools
            _devTools = Nothing
            If _cdp IsNot Nothing Then _cdp.DevTools = Nothing
            If collegamento IsNot Nothing Then
                RemoveHandler collegamento.EventoRicevuto, AddressOf EventoDevTools
                collegamento.Dispose()
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DistruggiBrowser devtools: " & ex.Message)
        End Try
        Try
            ScollegaGestori()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DistruggiBrowser gestori: " & ex.Message)
        End Try
        Try
            Dim vecchia As WGPaginaWeb = _pagina
            _pagina = Nothing
            _idScriptIniziale = ""
            If vecchia IsNot Nothing Then
                ScollegaController(vecchia.Controller)
                vecchia.Chiudi()
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DistruggiBrowser pagina: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            If _chiusa Then Return
            _chiusa = True
            _attiva = False
            WGTraduttore.RipristinaLinguaLettura()
            _tentativoCondivisione += 1
            RipristinaFinestraMarcata()
            ChiudiMenuAperti()
            DistruggiBrowser()
            Dim timer As System.Windows.Forms.Timer = _timerProgresso
            _timerProgresso = Nothing
            If timer IsNot Nothing Then
                RemoveHandler timer.Tick, AddressOf StimaProgresso
                timer.Dispose()
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Chiudi: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub
End Class

Public Class WGPonteNativo
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly log As New Logger()

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub RiceviMessaggio(testoJson As String, Optional origineVera As String = "")
        Try
            Dim messaggio As JObject = JObject.Parse(testoJson)
            ' l'origine scritta dalla pagina non conta: vale quella vera (vuota se non è un sito http o https)
            messaggio("origine") = If(origineVera, "")
            Select Case messaggio.Value(Of String)("tipo")
                Case "focus"
                    Dim dati As JObject = TryCast(messaggio("riga"), JObject)
                    If dati IsNot Nothing Then _scheda.AggiornaDaFocus(RigaVirtuale.DaJson(dati))
                Case "attivo"
                    _scheda.AggiornaDaDiscendenteAttivo(RigaVirtuale.DaJson(TryCast(messaggio("riga"), JObject)))
                Case "selezione"
                    _scheda.AggiornaDaSelezione(RigaVirtuale.DaJson(TryCast(messaggio("riga"), JObject)))
                Case "stato"
                    _scheda.AggiornaDaStato(RigaVirtuale.DaJson(TryCast(messaggio("riga"), JObject)))
                Case "live"
                    _scheda.AggiornaDaRegioneLive(messaggio.Value(Of String)("testo"), messaggio.Value(Of Integer?)("assertivo").GetValueOrDefault() = 1)
                Case "dialogo"
                    _scheda.AggiornaDaDialogo(RigaVirtuale.DaJson(TryCast(messaggio("riga"), JObject)))
                Case "sporco"
                    _scheda.SegnaBufferSporco()
                Case "pronto"
                    _scheda.AggiornaDaPaginaPronta()
                Case "notifica"
                    _scheda.NotificaDelSito(If(messaggio.Value(Of String)("titolo"), ""), If(messaggio.Value(Of String)("testo"), ""), If(messaggio.Value(Of String)("origine"), ""))
                Case "permessoNotifiche"
                    _scheda.RichiestaPermessoNotifiche(messaggio.Value(Of Integer?)("id").GetValueOrDefault(), If(messaggio.Value(Of String)("origine"), ""))
                Case "credenziali"
                    _scheda.CredenzialiInviate(If(messaggio.Value(Of String)("origine"), ""), If(messaggio.Value(Of String)("utente"), ""), If(messaggio.Value(Of String)("password"), ""))
                Case "campoLogin"
                    _scheda.CampoLoginRaggiunto(If(messaggio.Value(Of String)("origine"), ""))
                Case "mediaErrore"
                    _scheda.ErroreMedia(If(messaggio.Value(Of String)("src"), ""), messaggio.Value(Of Integer?)("codice").GetValueOrDefault(), messaggio.Value(Of Integer?)("video").GetValueOrDefault() = 1)
                Case "mediaProtetto"
                    _scheda.MediaProtetto()
                Case "flutter"
                    _scheda.FlutterAttivato()
                Case "condivisione"
                    _scheda.RichiestaCondivisioneSchermo(If(messaggio.Value(Of String)("id"), ""), messaggio.Value(Of Integer?)("audio").GetValueOrDefault() = 1, If(messaggio.Value(Of String)("origine"), ""))
                Case "condivisioneFinita"
                    _scheda.CondivisioneTerminata()
                Case "condivisioneNonRiuscita"
                    _scheda.CondivisioneNonRiuscita(If(messaggio.Value(Of String)("motivo"), ""))
                Case "condivisioneAvviata"
                    _scheda.CondivisioneAvviata(If(messaggio.Value(Of String)("superficie"), ""))
                Case "colore"
                    _scheda.SceltaColoreRichiesta(If(messaggio.Value(Of String)("id"), ""), If(messaggio.Value(Of String)("valore"), ""), If(messaggio.Value(Of String)("etichetta"), ""), If(messaggio.Value(Of String)("origine"), ""))
                Case "stampa"
                    _scheda.StampaRichiestaDallaPagina(If(messaggio.Value(Of String)("origine"), ""))
                Case "funzioneNonDisponibile"
                    _scheda.FunzioneNonDisponibile(If(messaggio.Value(Of String)("funzione"), ""))
                Case "cookie"
                    _scheda.CookieArrivato()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGPonteNativo.RiceviMessaggio: " & ex.Message)
        End Try
    End Sub
End Class