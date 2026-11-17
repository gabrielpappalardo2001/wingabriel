Imports System
Imports System.Windows.Forms
Imports System.Text.RegularExpressions
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Net.NetworkInformation
Imports Newtonsoft.Json.Linq
Imports System.Net.Http
Imports System.IO.Compression
Imports System.Runtime.InteropServices

Public Class WGYoutubePlayer
    Inherits UserControl
    Implements IDisposable

    Private _webView As WebView2
    Private _isReady As Boolean = False
    Private _isInitializing As Boolean = False
    Private _pendingUrl As String = String.Empty
    Private _logger As Logger
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private _watchdogTimer As System.Windows.Forms.Timer
    Private _ultimaAttivita As DateTime
    Private _tentativiRicarica As Integer = 0
    Private _threadIniziale As Thread
    Private Const INDIRIZZO_SERVER As String = "https://www.wingabriel.net/player.aspx?v="

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetParentYoutube(hWndChild As IntPtr, hWndNewParent As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function MoveWindowYoutube(hWnd As IntPtr, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, bRepaint As Boolean) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT_YOUTUBE
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetClientRectYoutube(hWnd As IntPtr, ByRef lpRect As RECT_YOUTUBE) As Boolean
    End Function

    Private WithEvents _tastiera As WGTastiera
    Private _isHookActive As Boolean = False
    Private _ultimoTastoSpazio As DateTime = DateTime.MinValue
    Private _ctrlSinistroPremutoDaHook As Boolean = False
    Private _ctrlDestroPremutoDaHook As Boolean = False

    Private _timerTempo As System.Windows.Forms.Timer
    Private _posizioneSecondi As Double = -1
    Private _durataSecondi As Double = -1
    Private _statoRiproduzione As Integer = -1
    Private _inPausa As Boolean = True
    Private _fineSegnalata As Boolean = False
    Private _letturaTempoInCorso As Boolean = False
    Private ReadOnly _lockTempo As New Object()
    Private _formSottofondo As Form
    Private _inSottofondo As Boolean = False
    Private _agganciataAPannello As Boolean = False

    Private Shared ReadOnly DOMINI_CONSENTITI As String() = {
        "wingabriel.net",
        "www.wingabriel.net",
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "youtube-nocookie.com",
        "www.youtube-nocookie.com",
        "googlevideo.com",
        "ytimg.com",
        "gstatic.com",
        "google.com",
        "googleapis.com",
        "googleusercontent.com",
        "doubleclick.net"
    }

    Public Event MediaCaricato(mittente As Object, e As EventArgs)
    Public Event VideoTerminato(mittente As Object, e As EventArgs)
    Public Event ErroreMedia(mittente As Object, e As EventArgs)
    Public Event VideoNonDisponibile(mittente As Object, e As EventArgs)
    Public Event ConnessioneAssente(mittente As Object, e As EventArgs)
    Public Event altpremuto(mittente As Object, e As EventArgs)
    Public Event escpremuto(mittente As Object, e As EventArgs)
    Public Event tastopremuto(tasto As Keys, conControllo As Boolean)

    Public Sub New()
        Try
            _logger = New Logger()
            _ultimaAttivita = DateTime.Now

            If Not Me.IsHandleCreated Then
                Dim h As IntPtr = Me.Handle
            End If

            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf GestioneEccezioniNonGestite
            AddHandler Application.ThreadException, AddressOf GestioneEccezioniThread

            _tastiera = WGTastiera.GetInstance()
            AddHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown

            _threadIniziale = New Thread(AddressOf EseguiPreparazioneInSottofondo)
            _threadIniziale.IsBackground = True
            _threadIniziale.Start()

            attivatastiera()

            ' Mentre parla la sintesi il video si abbassa; e WinGabriel sa se sta suonando.
            _lettoreRegistrato = New WGRiproduzione.Lettore() With {
                .StaSuonando = Function() Not Me.IsDisposed AndAlso _isReady AndAlso Not _inPausa,
                .Attenua = AddressOf Attenua,
                .Interrompi = Sub() If Not Me.IsDisposed Then Pausa()}
            WGRiproduzione.RegistraLettore(_lettoreRegistrato)
        Catch ex As Exception
            GestisciErroreAvanzato("New", ex)
        End Try
    End Sub

    Private _lettoreRegistrato As WGRiproduzione.Lettore

    ''' <summary>Porta il volume del video al fattore indicato del volume scelto (1 = come prima).</summary>
    Public Sub Attenua(fattore As Double)
        Try
            If Me.IsDisposed Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of Double)(AddressOf Attenua), fattore)
                Return
            End If
            Dim testoFattore As String = fattore.ToString(System.Globalization.CultureInfo.InvariantCulture)
            Dim script As String = "(function(){try{var f=" & testoFattore & ";var p=window.player;" &
                "if(p&&typeof p.getVolume==='function'&&typeof p.setVolume==='function'){" &
                "if(f<1){if(window.__wgVolBase===undefined){window.__wgVolBase=p.getVolume();}p.setVolume(Math.round(window.__wgVolBase*f));}" &
                "else if(window.__wgVolBase!==undefined){p.setVolume(window.__wgVolBase);window.__wgVolBase=undefined;}}" &
                "else{var v=document.querySelector('video');if(v){if(f<1){if(v.__wgBase===undefined){v.__wgBase=v.volume;}v.volume=v.__wgBase*f;}" &
                "else if(v.__wgBase!==undefined){v.volume=v.__wgBase;v.__wgBase=undefined;}}}}catch(e){}})()"
            InviaScriptVideo(script)
        Catch ex As Exception
            GestisciErroreAvanzato("Attenua", ex)
        End Try
    End Sub

    Public Sub attivatastiera()
        Try
            _isHookActive = True
        Catch ex As Exception
            GestisciErroreAvanzato("attivatastiera", ex)
        End Try
    End Sub

    Public Sub disattivatastiera()
        Try
            _isHookActive = False
        Catch ex As Exception
            GestisciErroreAvanzato("disattivatastiera", ex)
        End Try
    End Sub

    Private Function PuoElaborareInputBrowser() As Boolean
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return False
            If Not _isHookActive Then Return False
            If Not Me.Visible Then Return False

            Dim fgHwnd As IntPtr = GetForegroundWindow()
            If fgHwnd = IntPtr.Zero Then Return False

            Dim fgPid As UInteger = 0
            GetWindowThreadProcessId(fgHwnd, fgPid)
            Return fgPid = CUInt(System.Diagnostics.Process.GetCurrentProcess().Id)
        Catch ex As Exception
            GestisciErroreAvanzato("PuoElaborareInputBrowser", ex)
            Return False
        End Try
    End Function

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys)
        Try
            If tasto = Keys.None Then Return
            If Not PuoElaborareInputBrowser() Then Return

            If tasto = Keys.Escape Then
                RaiseEvent escpremuto(Me, EventArgs.Empty)
                Return
            End If

            If tastopremutoEvent IsNot Nothing Then
                Select Case tasto
                    Case Keys.Right, Keys.Left, Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown,
                         Keys.Home, Keys.End, Keys.Space, Keys.B, Keys.F, Keys.T, Keys.R,
                         Keys.D, Keys.F1, Keys.F10
                        RaiseEvent tastopremuto(tasto, _ctrlSinistroPremutoDaHook OrElse _ctrlDestroPremutoDaHook)
                        Return
                End Select
                Return
            End If

            Select Case tasto
                Case Keys.Right
                    Scorri(5)
                Case Keys.Left
                    Scorri(-5)
                Case Keys.Up
                    CambiaVolume(0.1)
                Case Keys.Down
                    CambiaVolume(-0.1)
                Case Keys.Space
                    If (DateTime.Now - _ultimoTastoSpazio).TotalMilliseconds > 300 Then
                        _ultimoTastoSpazio = DateTime.Now
                        Dim script As String = "try { if (typeof wgToggle === 'function') { wgToggle(); } else if (typeof window.player !== 'undefined' && typeof window.player.getPlayerState === 'function') { if (window.player.getPlayerState() === 1) { window.player.pauseVideo(); } else { window.player.playVideo(); } } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_TOGGLE: ' + e.message); } }"
                        InviaScriptVideo(script)
                    End If
            End Select
        Catch ex As Exception
            GestisciErroreAvanzato("_tastiera_KeyDown", ex)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            If Not PuoElaborareInputBrowser() Then Return

            RaiseEvent altpremuto(Me, EventArgs.Empty)
        Catch ex As Exception
            GestisciErroreAvanzato("_tastiera_AltPremuto", ex)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroPremuto
        _ctrlSinistroPremutoDaHook = True
    End Sub

    Private Sub _tastiera_ControlSinistroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroRilasciato
        _ctrlSinistroPremutoDaHook = False
    End Sub

    Private Sub _tastiera_ControlDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroPremuto
        _ctrlDestroPremutoDaHook = True
    End Sub

    Private Sub _tastiera_ControlDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroRilasciato
        _ctrlDestroPremutoDaHook = False
    End Sub

    Private Sub GestioneEccezioniNonGestite(sender As Object, e As UnhandledExceptionEventArgs)
        If TypeOf e.ExceptionObject Is Exception Then
            GestisciErroreAvanzato("AppDomain_UnhandledException", DirectCast(e.ExceptionObject, Exception))
        End If
    End Sub

    Private Sub GestioneEccezioniThread(sender As Object, e As ThreadExceptionEventArgs)
        GestisciErroreAvanzato("Application_ThreadException", e.Exception)
    End Sub

    Private Sub GestisciErroreAvanzato(contesto As String, ex As Exception)
        Try
            Dim dettaglio As String = "[ERRORE_AVANZATO][" & contesto & "] " & DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") & vbCrLf
            Dim eccezioneCorrente As Exception = ex
            Dim livello As Integer = 1

            While eccezioneCorrente IsNot Nothing
                dettaglio &= "--- Livello " & livello & " ---" & vbCrLf
                dettaglio &= "Tipo: " & eccezioneCorrente.GetType().FullName & vbCrLf
                dettaglio &= "Messaggio: " & eccezioneCorrente.Message & vbCrLf
                dettaglio &= "Sorgente: " & eccezioneCorrente.Source & vbCrLf
                If eccezioneCorrente.TargetSite IsNot Nothing Then
                    dettaglio &= "Metodo Operativo: " & eccezioneCorrente.TargetSite.Name & vbCrLf
                End If
                dettaglio &= "Stack Trace: " & vbCrLf & eccezioneCorrente.StackTrace & vbCrLf
                eccezioneCorrente = eccezioneCorrente.InnerException
                livello += 1
            End While

        Catch
        End Try
    End Sub

    Private Function UrlConsentito(url As String) As Boolean
        If String.IsNullOrEmpty(url) Then Return False
        Try
            If url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) Then Return True
            If url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then Return False
            If url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) Then Return False
            If url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then Return False

            Dim u As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, u) Then Return False
            If u.Scheme <> Uri.UriSchemeHttp AndAlso u.Scheme <> Uri.UriSchemeHttps Then Return False

            Dim host As String = u.Host.ToLowerInvariant()
            For Each dominio In DOMINI_CONSENTITI
                If host = dominio Then Return True
                If host.EndsWith("." & dominio) Then Return True
            Next
            Return False
        Catch
            Return False
        End Try
    End Function

    Private Sub EseguiPreparazioneInSottofondo()
        Try
            Dim cartellaEstensioni As String = Path.Combine(Application.StartupPath, "Extensions")

            Try
                If Not Directory.Exists(cartellaEstensioni) Then Directory.CreateDirectory(cartellaEstensioni)
            Catch ex As Exception
                GestisciErroreAvanzato("EseguiPreparazioneInSottofondo_FileSystem", ex)
            End Try

            Try
                Dim urlCrxList As String() = {
                    "https://clients2.googleusercontent.com/crx/blobs/Aa54OTD_vlvDD2nyoiz79pVK_UUhtk4L-3Hrq14x5Rlgu6duoLEBH7NES84Fg4zh0j3pYxBYwiIw8RrPV31FYmS0F5dSUCBQe6mE8-bG0ZadO0nqsH5x07JZaPsx_aAoCvTMAMZSmuXYBPV5LhJIsjtbqimzcv7656vFGA/DDKJIAHEJLHFCAFBDDMGIAHCPHECMPFH_2026_315_1814_0.crx",
                    "https://www.wingabriel.net/ublock.crx"
                }
                Dim operazione = ScaricaEdEstraiCrxAsync(urlCrxList, cartellaEstensioni, "uBlockOrigin")
                operazione.Wait()
            Catch ex As Exception
                GestisciErroreAvanzato("EseguiPreparazioneInSottofondo_DownloadEstensione", ex)
            End Try

            If Not Me.IsDisposed Then
                While Not Me.IsHandleCreated AndAlso Not Me.IsDisposed
                    Thread.Sleep(50)
                End While

                If Not Me.IsDisposed Then
                    Me.Invoke(Sub()
                                  Try
                                      CreaInterfacciaWebView()
                                  Catch ex As Exception
                                      GestisciErroreAvanzato("Invoke_CreaInterfaccia", ex)
                                  End Try
                              End Sub)
                End If
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("EseguiPreparazioneInSottofondo", ex)
        End Try
    End Sub

    Private Sub CreaInterfacciaWebView()
        Try
            _webView = New WebView2()
            _webView.Dock = DockStyle.Fill
            Try
                _webView.AllowExternalDrop = False
            Catch
            End Try
            Me.Controls.Add(_webView)

            _watchdogTimer = New System.Windows.Forms.Timer()
            _watchdogTimer.Interval = 4000
            AddHandler _watchdogTimer.Tick, AddressOf OnWatchdogTick

            AvviaInizializzazioneWebView()
        Catch ex As Exception
            GestisciErroreAvanzato("CreaInterfacciaWebView", ex)
        End Try
    End Sub

    Private Async Sub AvviaInizializzazioneWebView()
        Try
            If _isInitializing OrElse _webView Is Nothing OrElse _webView.IsDisposed Then Return
            _isInitializing = True

            If Not WGRuntimeWebView2.RuntimePresente() Then
                If Not Await WGRuntimeWebView2.InstallaConAvanzamentoAsync() Then
                    _logger.ScriviLog("[FATAL_RUNTIME_CHECK] runtime WebView2 non installato")
                    Return
                End If
                If _webView Is Nothing OrElse _webView.IsDisposed Then Return
            End If

            Dim env As CoreWebView2Environment = Await WGMotoreBrowser.AssicuraAmbienteAsync()
            If env Is Nothing Then
                _logger.ScriviLog("[FATAL_RUNTIME_CHECK] motore WebView2 condiviso non disponibile")
                Return
            End If

            If _webView IsNot Nothing AndAlso Not _webView.IsDisposed Then
                AddHandler _webView.CoreWebView2InitializationCompleted, AddressOf OnWebViewInitialized
                Await _webView.EnsureCoreWebView2Async(env)
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("AvviaInizializzazioneWebView", ex)
        Finally
            _isInitializing = False
        End Try
    End Sub

    Private Sub ApplicaImpostazioniSicurezza()
        Try
            Dim s = _webView.CoreWebView2.Settings

            s.IsScriptEnabled = True
            s.IsWebMessageEnabled = True

            Try : s.AreDevToolsEnabled = False : Catch : End Try
            Try : s.AreDefaultContextMenusEnabled = False : Catch : End Try
            Try : s.AreDefaultScriptDialogsEnabled = False : Catch : End Try
            Try : s.AreBrowserAcceleratorKeysEnabled = False : Catch : End Try
            Try : s.IsZoomControlEnabled = False : Catch : End Try
            Try : s.IsStatusBarEnabled = False : Catch : End Try
            Try : s.AreHostObjectsAllowed = False : Catch : End Try
            Try : s.IsBuiltInErrorPageEnabled = False : Catch : End Try
            Try : s.IsPasswordAutosaveEnabled = False : Catch : End Try
            Try : s.IsGeneralAutofillEnabled = False : Catch : End Try
            Try : s.IsPinchZoomEnabled = False : Catch : End Try
            Try : s.IsSwipeNavigationEnabled = False : Catch : End Try
            Try : s.IsReputationCheckingRequired = False : Catch : End Try
        Catch ex As Exception
            GestisciErroreAvanzato("ApplicaImpostazioniSicurezza", ex)
        End Try
    End Sub

    Private Async Sub OnWebViewInitialized(sender As Object, e As CoreWebView2InitializationCompletedEventArgs)
        Try
            If Not e.IsSuccess Then
                GestisciErroreAvanzato("OnWebViewInitialized_Failure", e.InitializationException)
                Return
            End If

            _isReady = True

            ApplicaImpostazioniSicurezza()

            _webView.CoreWebView2.AddWebResourceRequestedFilter("https://www.youtube.com/*", CoreWebView2WebResourceContext.All)
            _webView.CoreWebView2.AddWebResourceRequestedFilter("https://*.youtube.com/*", CoreWebView2WebResourceContext.All)
            _webView.CoreWebView2.AddWebResourceRequestedFilter("https://*.googlevideo.com/*", CoreWebView2WebResourceContext.All)

            Dim scriptSicurezza As String = "
                window.addEventListener('error', function(e) {
                    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_FATAL: ' + e.message); }
                }, true);
                window.addEventListener('unhandledrejection', function(e) {
                    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_REJECT: ' + (e.reason ? String(e.reason) : 'Unknown')); }
                }, true);"
            Await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(scriptSicurezza)

            Dim scriptSottofondo As String = "
                try {
                    Object.defineProperty(document, 'hidden', { get: function() { return false; }, configurable: true });
                    Object.defineProperty(document, 'visibilityState', { get: function() { return 'visible'; }, configurable: true });
                    Object.defineProperty(document, 'webkitHidden', { get: function() { return false; }, configurable: true });
                    Object.defineProperty(document, 'webkitVisibilityState', { get: function() { return 'visible'; }, configurable: true });
                    var eventiBloccati = ['visibilitychange', 'webkitvisibilitychange', 'blur', 'pagehide', 'freeze'];
                    var addEventListenerOriginale = window.addEventListener.bind(window);
                    window.addEventListener = function(tipo, gestore, opzioni) {
                        if (eventiBloccati.indexOf(tipo) > -1) { return; }
                        return addEventListenerOriginale(tipo, gestore, opzioni);
                    };
                    var addEventListenerDocOriginale = document.addEventListener.bind(document);
                    document.addEventListener = function(tipo, gestore, opzioni) {
                        if (eventiBloccati.indexOf(tipo) > -1) { return; }
                        return addEventListenerDocOriginale(tipo, gestore, opzioni);
                    };
                    document.hasFocus = function() { return true; };
                } catch (e) {
                    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_SOTTOFONDO: ' + e.message); }
                }"
            Await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(scriptSottofondo)

            AddHandler _webView.CoreWebView2.DOMContentLoaded, AddressOf OnDOMContentLoaded
            AddHandler _webView.CoreWebView2.WebMessageReceived, AddressOf OnWebMessageReceived
            AddHandler _webView.CoreWebView2.ProcessFailed, AddressOf OnProcessFailed
            AddHandler _webView.CoreWebView2.NavigationCompleted, AddressOf OnNavigationCompleted
            AddHandler _webView.CoreWebView2.NavigationStarting, AddressOf OnNavigationStarting
            AddHandler _webView.CoreWebView2.FrameNavigationStarting, AddressOf OnFrameNavigationStarting
            AddHandler _webView.CoreWebView2.NewWindowRequested, AddressOf OnNewWindowRequested
            AddHandler _webView.CoreWebView2.PermissionRequested, AddressOf OnPermissionRequested
            Try
                AddHandler _webView.CoreWebView2.DownloadStarting, AddressOf OnDownloadStarting
            Catch
            End Try

            If Not String.IsNullOrEmpty(_pendingUrl) Then
                Dim urlTmp As String = _pendingUrl
                _pendingUrl = String.Empty
                RiproduciVideoYoutube(urlTmp)
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("OnWebViewInitialized", ex)
        End Try
    End Sub

    Private Sub OnNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        Try
            Dim url As String = e.Uri
            If Not UrlConsentito(url) Then
                e.Cancel = True
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("OnNavigationStarting", ex)
        End Try
    End Sub

    Private Sub OnFrameNavigationStarting(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        Try
            Dim url As String = e.Uri
            If Not UrlConsentito(url) Then
                e.Cancel = True
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("OnFrameNavigationStarting", ex)
        End Try
    End Sub

    Private Sub OnNewWindowRequested(sender As Object, e As CoreWebView2NewWindowRequestedEventArgs)
        Try
            e.Handled = True
        Catch ex As Exception
            GestisciErroreAvanzato("OnNewWindowRequested", ex)
        End Try
    End Sub

    Private Sub OnPermissionRequested(sender As Object, e As CoreWebView2PermissionRequestedEventArgs)
        Try
            e.State = CoreWebView2PermissionState.Deny
        Catch ex As Exception
            GestisciErroreAvanzato("OnPermissionRequested", ex)
        End Try
    End Sub

    Private Sub OnDownloadStarting(sender As Object, e As CoreWebView2DownloadStartingEventArgs)
        Try
            e.Cancel = True
        Catch ex As Exception
            GestisciErroreAvanzato("OnDownloadStarting", ex)
        End Try
    End Sub

    Private Sub OnProcessFailed(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        Try
        Catch ex As Exception
            GestisciErroreAvanzato("OnProcessFailed", ex)
        End Try
    End Sub

    Private Sub OnNavigationCompleted(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        Try
            If Not e.IsSuccess Then
                If e.WebErrorStatus = CoreWebView2WebErrorStatus.HostNameNotResolved Or e.WebErrorStatus = CoreWebView2WebErrorStatus.ConnectionAborted Then
                    RaiseEvent ConnessioneAssente(Me, EventArgs.Empty)
                End If
            Else
                _ultimaAttivita = DateTime.Now
                _watchdogTimer.Start()
                AvviaMonitoraggioTempo()
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("OnNavigationCompleted", ex)
        End Try
    End Sub

    Private Sub OnWatchdogTick(sender As Object, e As EventArgs)
        Try
            If Not _isReady OrElse _webView Is Nothing OrElse _webView.IsDisposed Then Return

            If Not NetworkInterface.GetIsNetworkAvailable() Then Return

            Dim inPausaOra As Boolean
            Dim fineGiaSegnalata As Boolean
            SyncLock _lockTempo
                inPausaOra = _inPausa
                fineGiaSegnalata = _fineSegnalata
            End SyncLock

            If inPausaOra OrElse fineGiaSegnalata Then
                _ultimaAttivita = DateTime.Now
                Return
            End If

            Dim secInattivita As Double = (DateTime.Now - _ultimaAttivita).TotalSeconds
            If secInattivita > 25 Then
                If _tentativiRicarica < 3 Then
                    _tentativiRicarica += 1
                    _webView.CoreWebView2.Reload()
                    _ultimaAttivita = DateTime.Now
                Else
                    _watchdogTimer.Stop()
                    RaiseEvent ErroreMedia(Me, EventArgs.Empty)
                End If
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("OnWatchdogTick", ex)
        End Try
    End Sub

    Public Sub RiproduciVideoYoutube(url As String)
        Try
            If Not _isReady OrElse _webView Is Nothing Then
                _pendingUrl = url
                Return
            End If

            If _webView.IsDisposed Then
                Return
            End If

            If _webView.InvokeRequired Then
                _webView.Invoke(Sub()
                                    Try
                                        RiproduciVideoYoutube(url)
                                    Catch ex As Exception
                                        GestisciErroreAvanzato("RiproduciVideoYoutube_Invoke", ex)
                                    End Try
                                End Sub)
                Return
            End If

            Dim idVideo As String = EstraiIdVideo(url)
            _ultimaAttivita = DateTime.Now
            _tentativiRicarica = 0

            AzzeraTempo()
            SyncLock _lockTempo
                _inPausa = True
                _fineSegnalata = False
            End SyncLock

            _watchdogTimer.Stop()

            If Not String.IsNullOrEmpty(idVideo) Then
                Dim urlPlayer As String = INDIRIZZO_SERVER & idVideo
                If UrlConsentito(urlPlayer) Then
                    _webView.CoreWebView2.Navigate(urlPlayer)
                Else
                    RaiseEvent VideoNonDisponibile(Me, EventArgs.Empty)
                End If
            ElseIf UrlConsentito(url) Then
                _webView.CoreWebView2.Navigate(url)
            Else
                RaiseEvent VideoNonDisponibile(Me, EventArgs.Empty)
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("RiproduciVideoYoutube", ex)
        End Try
    End Sub

    Private Function EstraiIdVideo(url As String) As String
        Try
            If String.IsNullOrEmpty(url) Then Return String.Empty
            Dim match = Regex.Match(url, "(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?|shorts|live)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/\s]{11})")
            If Not match.Success Then Return String.Empty
            Dim id As String = match.Groups(1).Value
            If Not Regex.IsMatch(id, "^[A-Za-z0-9_\-]{11}$") Then Return String.Empty
            Return id
        Catch ex As Exception
            GestisciErroreAvanzato("EstraiIdVideo", ex)
            Return String.Empty
        End Try
    End Function

    Private Async Sub OnDOMContentLoaded(sender As Object, e As CoreWebView2DOMContentLoadedEventArgs)
        Try
            Dim scriptJs As String = "let jsChecks = 0; let isReadySent = false; window.addEventListener('keydown', function(e) { if([32, 33, 34, 35, 36, 37, 38, 39, 40].indexOf(e.keyCode) > -1) { e.preventDefault(); } }, false); setInterval(() => { try { jsChecks++; if (typeof window.player !== 'undefined' && typeof window.player.getPlayerState === 'function') { let stato = window.player.getPlayerState(); if (stato === 1) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('playing'); } } else if (stato === 0) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('ended'); } } else if (stato === 2) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('paused'); } } else { if (!isReadySent && (stato === 5 || stato === -1)) { isReadySent = true; if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('pronto'); } } } } else { let iframes = document.querySelectorAll('iframe'); if (iframes.length > 0) { if (!isReadySent) { isReadySent = true; if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('pronto'); window.chrome.webview.postMessage('playing'); } } } else { if (jsChecks === 1) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_LOG: Ricerca player...'); } } if (jsChecks > 8) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_NO_PLAYER: Il player non si e generato.'); } } } } } catch(ex) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_INIT: ' + ex.message); } } }, 1500);"
            Await _webView.CoreWebView2.ExecuteScriptAsync(scriptJs)
        Catch ex As Exception
            GestisciErroreAvanzato("OnDOMContentLoaded", ex)
        End Try
    End Sub

    Private Sub OnWebMessageReceived(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Try
            Dim msgTesto As String = e.TryGetWebMessageAsString()
            If msgTesto Is Nothing Then Return
            If msgTesto.Length > 4096 Then msgTesto = msgTesto.Substring(0, 4096)

            _ultimaAttivita = DateTime.Now

            Select Case True
                Case msgTesto = "pronto" OrElse msgTesto = "ready"
                    _tentativiRicarica = 0
                    AvviaMonitoraggioTempo()
                    RaiseEvent MediaCaricato(Me, EventArgs.Empty)
                Case msgTesto = "playing"
                    _tentativiRicarica = 0
                    SyncLock _lockTempo
                        _inPausa = False
                        _fineSegnalata = False
                    End SyncLock
                Case msgTesto = "paused"
                    _tentativiRicarica = 0
                    SyncLock _lockTempo
                        _inPausa = True
                    End SyncLock
                Case msgTesto = "ended"
                    Dim giaSegnalata As Boolean
                    SyncLock _lockTempo
                        giaSegnalata = _fineSegnalata
                        _inPausa = True
                        _fineSegnalata = True
                    End SyncLock
                    If Not giaSegnalata Then RaiseEvent VideoTerminato(Me, EventArgs.Empty)
                Case msgTesto = "video_unavailable"
                    RaiseEvent VideoNonDisponibile(Me, EventArgs.Empty)
                Case msgTesto.StartsWith("JS_FATAL") OrElse msgTesto.StartsWith("JS_REJECT") OrElse msgTesto.StartsWith("JS_ERR_")
                    If msgTesto.Contains("NO_PLAYER") Then
                        RaiseEvent ErroreMedia(Me, EventArgs.Empty)
                    End If
                Case msgTesto.Contains("ERR") OrElse msgTesto.Contains("BLOCKED")
                    RaiseEvent ErroreMedia(Me, EventArgs.Empty)
            End Select
        Catch ex As Exception
            GestisciErroreAvanzato("OnWebMessageReceived", ex)
        End Try
    End Sub

    Public Sub Riproduci()
        Try
            Dim script As String = "try { let cmd = JSON.stringify({event: 'command', func: 'playVideo', args: ''}); let fs = document.querySelectorAll('iframe'); for(let i=0; i<fs.length; i++) { fs[i].contentWindow.postMessage(cmd, '*'); } if (typeof wgPlay === 'function') { wgPlay(); } else if (typeof window.player !== 'undefined' && typeof window.player.playVideo === 'function') { window.player.playVideo(); } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_PLAY: ' + e.message); } }"
            InviaScriptVideo(script)
            SyncLock _lockTempo
                _inPausa = False
            End SyncLock
        Catch ex As Exception
            GestisciErroreAvanzato("Riproduci", ex)
        End Try
    End Sub

    Public Sub Pausa()
        Try
            Dim script As String = "try { let cmd = JSON.stringify({event: 'command', func: 'pauseVideo', args: ''}); let fs = document.querySelectorAll('iframe'); for(let i=0; i<fs.length; i++) { fs[i].contentWindow.postMessage(cmd, '*'); } if (typeof wgPause === 'function') { wgPause(); } else if (typeof window.player !== 'undefined' && typeof window.player.pauseVideo === 'function') { window.player.pauseVideo(); } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_PAUSE: ' + e.message); } }"
            InviaScriptVideo(script)
            SyncLock _lockTempo
                _inPausa = True
            End SyncLock
        Catch ex As Exception
            GestisciErroreAvanzato("Pausa", ex)
        End Try
    End Sub

    Public Sub Scorri(secondi As Integer)
        Try
            Dim secStr As String = secondi.ToString(System.Globalization.CultureInfo.InvariantCulture)
            Dim script As String = "try { if (typeof wgScorri === 'function') { wgScorri(" & secStr & "); } else if (typeof window.player !== 'undefined' && typeof window.player.getCurrentTime === 'function') { window.player.seekTo(window.player.getCurrentTime() + " & secStr & ", true); } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_SCORRI: ' + e.message); } }"
            InviaScriptVideo(script)
            AggiornaTempoAdesso()
        Catch ex As Exception
            GestisciErroreAvanzato("Scorri", ex)
        End Try
    End Sub

    Public Sub ScorriAssoluto(secondi As Double)
        Try
            If Double.IsNaN(secondi) OrElse Double.IsInfinity(secondi) Then Return
            If secondi < 0 Then secondi = 0

            Dim secStr As String = secondi.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            Dim script As String = "try { if (typeof wgVaiA === 'function') { wgVaiA(" & secStr & "); } else if (typeof window.player !== 'undefined' && typeof window.player.seekTo === 'function') { window.player.seekTo(" & secStr & ", true); } else { var v = document.querySelector('video'); if (v) { v.currentTime = " & secStr & "; } } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_VAIA: ' + e.message); } }"
            InviaScriptVideo(script)
            AggiornaTempoAdesso()
        Catch ex As Exception
            GestisciErroreAvanzato("ScorriAssoluto", ex)
        End Try
    End Sub

    Public ReadOnly Property TempoDisponibile As Boolean
        Get
            Try
                SyncLock _lockTempo
                    Return _durataSecondi > 0 AndAlso _posizioneSecondi >= 0
                End SyncLock
            Catch ex As Exception
                GestisciErroreAvanzato("TempoDisponibile", ex)
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property PosizioneTemporale As TimeSpan
        Get
            Try
                SyncLock _lockTempo
                    If _posizioneSecondi < 0 Then Return TimeSpan.Zero
                    Return TimeSpan.FromSeconds(_posizioneSecondi)
                End SyncLock
            Catch ex As Exception
                GestisciErroreAvanzato("PosizioneTemporale", ex)
                Return TimeSpan.Zero
            End Try
        End Get
    End Property

    Public ReadOnly Property Durata As TimeSpan
        Get
            Try
                SyncLock _lockTempo
                    If _durataSecondi < 0 Then Return TimeSpan.Zero
                    Return TimeSpan.FromSeconds(_durataSecondi)
                End SyncLock
            Catch ex As Exception
                GestisciErroreAvanzato("Durata", ex)
                Return TimeSpan.Zero
            End Try
        End Get
    End Property

    Public ReadOnly Property InPausa As Boolean
        Get
            Try
                SyncLock _lockTempo
                    Return _inPausa
                End SyncLock
            Catch ex As Exception
                GestisciErroreAvanzato("InPausa", ex)
                Return True
            End Try
        End Get
    End Property

    Private Sub AzzeraTempo()
        Try
            SyncLock _lockTempo
                _posizioneSecondi = -1
                _durataSecondi = -1
                _statoRiproduzione = -1
            End SyncLock
        Catch ex As Exception
            GestisciErroreAvanzato("AzzeraTempo", ex)
        End Try
    End Sub

    Private Sub AvviaMonitoraggioTempo()
        Try
            If _timerTempo Is Nothing Then
                _timerTempo = New System.Windows.Forms.Timer()
                _timerTempo.Interval = 1000
                AddHandler _timerTempo.Tick, AddressOf OnTimerTempoTick
            End If
            _timerTempo.Stop()
            _timerTempo.Start()
        Catch ex As Exception
            GestisciErroreAvanzato("AvviaMonitoraggioTempo", ex)
        End Try
    End Sub

    Private Sub FermaMonitoraggioTempo()
        Try
            If _timerTempo IsNot Nothing Then
                _timerTempo.Stop()
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("FermaMonitoraggioTempo", ex)
        End Try
    End Sub

    Private Sub OnTimerTempoTick(sender As Object, e As EventArgs)
        Try
            AggiornaTempoAdesso()
        Catch ex As Exception
            GestisciErroreAvanzato("OnTimerTempoTick", ex)
        End Try
    End Sub

    Private Sub AggiornaTempoAdesso()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not _isReady Then Return
            If _webView Is Nothing OrElse _webView.IsDisposed OrElse _webView.CoreWebView2 Is Nothing Then Return
            If _letturaTempoInCorso Then Return

            _letturaTempoInCorso = True
            LeggiTempoDalPlayer()
        Catch ex As Exception
            _letturaTempoInCorso = False
            GestisciErroreAvanzato("AggiornaTempoAdesso", ex)
        End Try
    End Sub

    Private Async Sub LeggiTempoDalPlayer()
        Try
            Dim script As String = "(function(){ try { var t=-1, d=-1, s=-1; if (typeof wgTempo === 'function') { var r = wgTempo(); if (r) { if (r.t !== undefined && r.t !== null) t = r.t; if (r.d !== undefined && r.d !== null) d = r.d; if (r.s !== undefined && r.s !== null) s = r.s; } } else if (typeof window.player !== 'undefined' && typeof window.player.getCurrentTime === 'function') { t = window.player.getCurrentTime(); if (typeof window.player.getDuration === 'function') { d = window.player.getDuration(); } if (typeof window.player.getPlayerState === 'function') { s = window.player.getPlayerState(); } } else { var v = document.querySelector('video'); if (v) { t = v.currentTime; d = v.duration; s = (v.paused ? 2 : 1); } } if (typeof t !== 'number' || !isFinite(t)) t = -1; if (typeof d !== 'number' || !isFinite(d)) d = -1; if (typeof s !== 'number' || !isFinite(s)) s = -1; return { t: t, d: d, s: s }; } catch(e) { return { t: -1, d: -1, s: -1 }; } })()"

            Dim risultato As String = Await _webView.CoreWebView2.ExecuteScriptAsync(script)

            If String.IsNullOrWhiteSpace(risultato) Then Return
            If Not risultato.TrimStart().StartsWith("{") Then Return

            Dim oggetto As JObject = JObject.Parse(risultato)

            Dim tempo As Double = -1
            Dim durataLetta As Double = -1
            Dim stato As Integer = -1

            If oggetto("t") IsNot Nothing AndAlso oggetto("t").Type <> JTokenType.Null Then
                tempo = CDbl(oggetto("t"))
            End If
            If oggetto("d") IsNot Nothing AndAlso oggetto("d").Type <> JTokenType.Null Then
                durataLetta = CDbl(oggetto("d"))
            End If
            If oggetto("s") IsNot Nothing AndAlso oggetto("s").Type <> JTokenType.Null Then
                stato = CInt(oggetto("s"))
            End If

            SyncLock _lockTempo
                _posizioneSecondi = tempo
                _durataSecondi = durataLetta
                If stato >= 0 Then
                    _statoRiproduzione = stato
                    _inPausa = (stato <> 1 AndAlso stato <> 3)
                End If
            End SyncLock
        Catch ex As Exception
            GestisciErroreAvanzato("LeggiTempoDalPlayer", ex)
        Finally
            _letturaTempoInCorso = False
        End Try
    End Sub

    Public Sub CambiaVolume(delta As Double)
        Try
            Dim cult As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
            Dim deltaStr As String = delta.ToString(cult)
            Dim script As String = "try { if (typeof wgVolume === 'function') { wgVolume(" & deltaStr & "); } else if (typeof window.player !== 'undefined' && typeof window.player.getVolume === 'function') { let v = window.player.getVolume() + (" & deltaStr & " * 100); window.player.setVolume(Math.max(0, Math.min(100, v))); } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_VOL: ' + e.message); } }"
            InviaScriptVideo(script)
        Catch ex As Exception
            GestisciErroreAvanzato("CambiaVolume", ex)
        End Try
    End Sub

    Public Sub ImpostaVelocita(velocita As Double)
        Try
            Dim cult As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
            Dim velStr As String = velocita.ToString(cult)
            Dim script As String = "try { if (typeof wgVelocita === 'function') { wgVelocita(" & velStr & "); } else if (typeof window.player !== 'undefined' && typeof window.player.setPlaybackRate === 'function') { window.player.setPlaybackRate(" & velStr & "); } } catch(e) { if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage('JS_ERR_VEL: ' + e.message); } }"
            InviaScriptVideo(script)
        Catch ex As Exception
            GestisciErroreAvanzato("ImpostaVelocita", ex)
        End Try
    End Sub

    Public Sub ImpostaVisibilita(visibile As Boolean)
        Try
            If _webView IsNot Nothing AndAlso Not _webView.IsDisposed Then
                If _webView.InvokeRequired Then
                    _webView.Invoke(Sub()
                                        Try
                                            _webView.Visible = visibile
                                        Catch ex As Exception
                                            GestisciErroreAvanzato("ImpostaVisibilita_Invoke", ex)
                                        End Try
                                    End Sub)
                Else
                    _webView.Visible = visibile
                End If
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("ImpostaVisibilita", ex)
        End Try
    End Sub

    Public Sub ContinuaInSottofondo()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _webView Is Nothing OrElse _webView.IsDisposed Then Return

            disattivatastiera()

            If _formSottofondo Is Nothing OrElse _formSottofondo.IsDisposed Then
                _formSottofondo = New Form()
                _formSottofondo.ShowInTaskbar = False
                _formSottofondo.FormBorderStyle = FormBorderStyle.None
                _formSottofondo.StartPosition = FormStartPosition.Manual
                _formSottofondo.Location = New System.Drawing.Point(-32000, -32000)
                _formSottofondo.Width = 2
                _formSottofondo.Height = 2
                _formSottofondo.Opacity = 0
                AddHandler _formSottofondo.FormClosing, AddressOf OnFormSottofondoClosing
                _formSottofondo.Show()
            End If

            If Me.Parent IsNot Nothing AndAlso Not Me.Parent.IsDisposed Then
                Me.Parent.Controls.Remove(Me)
            End If

            Me.Dock = DockStyle.Fill
            _formSottofondo.Controls.Add(Me)

            _inSottofondo = True
            ImpostaVisibilita(True)
        Catch ex As Exception
            GestisciErroreAvanzato("ContinuaInSottofondo", ex)
        End Try
    End Sub

    Private Sub OnFormSottofondoClosing(sender As Object, e As FormClosingEventArgs)
        Try
            e.Cancel = True
        Catch ex As Exception
            GestisciErroreAvanzato("OnFormSottofondoClosing", ex)
        End Try
    End Sub

    Public ReadOnly Property InSottofondo As Boolean
        Get
            Return _inSottofondo
        End Get
    End Property

    Public Sub AgganciaAPannelloVideo(handlePannello As IntPtr)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If handlePannello = IntPtr.Zero Then Return
            If _formSottofondo Is Nothing OrElse _formSottofondo.IsDisposed Then Return
            If Not _formSottofondo.IsHandleCreated Then Return

            SetParentYoutube(_formSottofondo.Handle, handlePannello)

            Dim rettangolo As New RECT_YOUTUBE()
            If GetClientRectYoutube(handlePannello, rettangolo) Then
                Dim larghezza As Integer = rettangolo.Right - rettangolo.Left
                Dim altezza As Integer = rettangolo.Bottom - rettangolo.Top
                If larghezza > 0 AndAlso altezza > 0 Then
                    MoveWindowYoutube(_formSottofondo.Handle, 0, 0, larghezza, altezza, True)
                End If
            End If

            _formSottofondo.Opacity = 1
            _formSottofondo.Visible = True
            _agganciataAPannello = True
        Catch ex As Exception
            GestisciErroreAvanzato("AgganciaAPannelloVideo", ex)
        End Try
    End Sub

    Public Sub SganciaDaPannelloVideo()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not _agganciataAPannello Then Return
            If _formSottofondo Is Nothing OrElse _formSottofondo.IsDisposed Then Return

            If _formSottofondo.IsHandleCreated Then
                SetParentYoutube(_formSottofondo.Handle, IntPtr.Zero)
            End If

            _formSottofondo.Opacity = 0
            _formSottofondo.StartPosition = FormStartPosition.Manual
            _formSottofondo.Location = New System.Drawing.Point(-32000, -32000)
            _formSottofondo.Width = 2
            _formSottofondo.Height = 2

            _agganciataAPannello = False
        Catch ex As Exception
            GestisciErroreAvanzato("SganciaDaPannelloVideo", ex)
        End Try
    End Sub

    Public Sub PortaInPrimoPiano()
        Try
            If _webView IsNot Nothing AndAlso Not _webView.IsDisposed Then
                If _webView.InvokeRequired Then
                    _webView.Invoke(Sub()
                                        Try
                                            _webView.BringToFront()
                                        Catch ex As Exception
                                            GestisciErroreAvanzato("PortaInPrimoPiano_Invoke", ex)
                                        End Try
                                    End Sub)
                Else
                    _webView.BringToFront()
                End If
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("PortaInPrimoPiano", ex)
        End Try
    End Sub

    Private Async Sub InviaScriptVideo(script As String)
        Try
            If _isReady AndAlso _webView IsNot Nothing AndAlso _webView.CoreWebView2 IsNot Nothing Then
                Await _webView.CoreWebView2.ExecuteScriptAsync(script)
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("InviaScriptVideo", ex)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            RemoveHandler AppDomain.CurrentDomain.UnhandledException, AddressOf GestioneEccezioniNonGestite
            RemoveHandler Application.ThreadException, AddressOf GestioneEccezioniThread
            WGRiproduzione.RimuoviLettore(_lettoreRegistrato)

            If _watchdogTimer IsNot Nothing Then
                _watchdogTimer.Stop()
                _watchdogTimer.Dispose()
            End If

            If Not Me.IsDisposed Then
                If Me.InvokeRequired Then
                    Me.Invoke(Sub()
                                  Try
                                      LiberaRisorseAmbiente()
                                  Catch ex As Exception
                                      GestisciErroreAvanzato("Dispose_Invoke", ex)
                                  End Try
                              End Sub)
                    Return
                End If
            End If
            LiberaRisorseAmbiente()
        Catch ex As Exception
            GestisciErroreAvanzato("Dispose", ex)
        End Try
    End Sub

    Private Sub LiberaRisorseAmbiente()
        Try
            disattivatastiera()

            Try
                RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
            Catch exHook As Exception
                GestisciErroreAvanzato("LiberaRisorseAmbiente_Hook", exHook)
            End Try

            FermaMonitoraggioTempo()
            If _timerTempo IsNot Nothing Then
                RemoveHandler _timerTempo.Tick, AddressOf OnTimerTempoTick
                _timerTempo.Dispose()
                _timerTempo = Nothing
            End If

            If _webView IsNot Nothing Then
                Try
                    If _webView.CoreWebView2 IsNot Nothing Then
                        RemoveHandler _webView.CoreWebView2.NavigationStarting, AddressOf OnNavigationStarting
                        RemoveHandler _webView.CoreWebView2.FrameNavigationStarting, AddressOf OnFrameNavigationStarting
                        RemoveHandler _webView.CoreWebView2.NewWindowRequested, AddressOf OnNewWindowRequested
                        RemoveHandler _webView.CoreWebView2.PermissionRequested, AddressOf OnPermissionRequested
                        Try
                            RemoveHandler _webView.CoreWebView2.DownloadStarting, AddressOf OnDownloadStarting
                        Catch
                        End Try
                    End If
                Catch
                End Try
                _webView.Dispose()
                _webView = Nothing
            End If

            If _formSottofondo IsNot Nothing AndAlso Not _formSottofondo.IsDisposed Then
                Try
                    RemoveHandler _formSottofondo.FormClosing, AddressOf OnFormSottofondoClosing
                Catch
                End Try
                Try
                    If _agganciataAPannello AndAlso _formSottofondo.IsHandleCreated Then
                        SetParentYoutube(_formSottofondo.Handle, IntPtr.Zero)
                        _agganciataAPannello = False
                    End If
                    _formSottofondo.Controls.Clear()
                    _formSottofondo.Close()
                    _formSottofondo.Dispose()
                Catch exForm As Exception
                    GestisciErroreAvanzato("LiberaRisorseAmbiente_FormSottofondo", exForm)
                End Try
                _formSottofondo = Nothing
            End If
        Catch ex As Exception
            GestisciErroreAvanzato("LiberaRisorseAmbiente", ex)
        End Try
    End Sub

    Private Async Function ScaricaEdEstraiCrxAsync(urls As String(), percorsoRadice As String, nomeFolder As String) As Task
        Try
            Dim pathExt As String = Path.Combine(percorsoRadice, nomeFolder)
            Dim pathFlag As String = Path.Combine(percorsoRadice, nomeFolder & "_ok.txt")

            If Directory.Exists(pathExt) AndAlso File.Exists(pathFlag) Then Return

            Using client As New HttpClient()
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")

                For Each url In urls
                    Try
                        Dim risposta = Await client.GetAsync(url)
                        If risposta.IsSuccessStatusCode Then
                            Dim tempCrx As String = Path.Combine(percorsoRadice, "temp.crx")
                            Dim tempZip As String = Path.Combine(percorsoRadice, "temp.zip")

                            Using fs As New FileStream(tempCrx, FileMode.Create)
                                Await risposta.Content.CopyToAsync(fs)
                            End Using

                            Dim bytes As Byte() = File.ReadAllBytes(tempCrx)
                            Dim zipStart As Integer = -1

                            For i As Integer = 0 To bytes.Length - 4
                                If bytes(i) = &H50 AndAlso bytes(i + 1) = &H4B AndAlso bytes(i + 2) = &H3 AndAlso bytes(i + 3) = &H4 Then
                                    zipStart = i
                                    Exit For
                                End If
                            Next

                            If zipStart >= 0 Then
                                If Directory.Exists(pathExt) Then Directory.Delete(pathExt, True)
                                Dim zipBytes(bytes.Length - zipStart - 1) As Byte
                                Array.Copy(bytes, zipStart, zipBytes, 0, zipBytes.Length)
                                File.WriteAllBytes(tempZip, zipBytes)
                                ZipFile.ExtractToDirectory(tempZip, pathExt)
                                File.WriteAllText(pathFlag, "OK")
                            End If

                            If File.Exists(tempCrx) Then File.Delete(tempCrx)
                            If File.Exists(tempZip) Then File.Delete(tempZip)

                            If File.Exists(pathFlag) Then Return
                        End If
                    Catch exSub As Exception
                        _logger.ScriviLog("[ERR_DOWNLOAD_URL] " & url & " - " & exSub.Message)
                    End Try
                Next
            End Using
        Catch ex As Exception
            GestisciErroreAvanzato("ScaricaEdEstraiCrxAsync", ex)
        End Try
    End Function
End Class