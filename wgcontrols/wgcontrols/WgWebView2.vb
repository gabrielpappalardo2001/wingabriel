Imports System.Threading
Imports System.Windows.Forms
Imports System.Drawing
Imports Microsoft.Web.WebView2.WinForms
Imports Microsoft.Web.WebView2.Core

' Integrazione del motore WebView2 per la navigazione web interna e il rendering di contenuti in formato HTML.
Public NotInheritable Class WGWebView
    Private Shared ReadOnly logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared _istanza As WGWebView

    Private Const AttesaAvvioMs As Integer = 20000
    Private Const AttesaDopoCaricamentoMs As Integer = 1500
    Private Const AttesaConsensoMs As Integer = 1500
    Private Const ControlliConsensoMassimi As Integer = 4
    Private Const MargineAttesaMs As Integer = 5000
    Private Const DurataMinimaMs As Integer = 3000
    Private Const PaginaVuota As String = "about:blank"

    Private Const ScriptHtml As String = "document.documentElement.outerHTML"

    Private Const ScriptBannerVisibile As String =
        "(function(){try{return (typeof window.__wgBannerConsensoVisibile==='function'&&window.__wgBannerConsensoVisibile())?1:0;}catch(e){return 0;}})();"

    Private Const ScriptConsenso As String =
        "(function(){" &
        "if(window.__wgConsensoAttivo){return;}" &
        "window.__wgConsensoAttivo=true;" &
        "var selettori=['#didomi-notice-agree-button','#onetrust-accept-btn-handler','#accept-recommended-btn-handler','.iubenda-cs-accept-btn','#iubenda-cs-accept-btn','#CybotCookiebotDialogBodyLevelButtonLevelOptinAllowAll','#CybotCookiebotDialogBodyButtonAccept','.qc-cmp2-summary-buttons button[mode=primary]','#qc-cmp2-ui button[mode=primary]','.sp_choice_type_11','.cmpboxbtnyes','.fc-cta-consent','#truste-consent-button','.cky-btn-accept','#wt-cli-accept-all-btn','.cmplz-accept','#pt-accept-all','.pt-acceptAll','[data-testid=uc-accept-all-button]','#axeptio_btn_acceptAll','.evidon-banner-acceptbutton','.cc-allow','.cc-accept-all','#cookie-accept','#accept-cookies','.accept-cookies'];" &
        "var contenitori=['didomi','onetrust','iubenda','cybot','cookiebot','qc-cmp','sp_message','sp-message','cmpbox','fc-consent','truste','cookie','consent','consenso','gdpr','cmp','privacy'];" &
        "var testi=['accetta e continua','accetta e chiudi','accetta tutto','accetta tutti','accetta tutti i cookie','accetta i cookie','accetta','accetto','acconsento','consenti tutti','consenti','accept all','accept and continue','accept','agree','i agree','ok'];" &
        "var esclusi=['abbon','rifiut','personalizz','gestisci','impostazion','preferenz','scopri','maggiori','reject','manage','settings','subscribe','options'];" &
        "function visibile(e){try{var r=e.getBoundingClientRect();if(r.width<=0||r.height<=0){return false;}var s=window.getComputedStyle(e);return s.visibility!=='hidden'&&s.display!=='none'&&s.opacity!=='0';}catch(x){return false;}}" &
        "function dentroFrameConsenso(){try{if(window.top===window.self){return false;}var h=(location.hostname||'').toLowerCase();var p=(location.pathname||'').toLowerCase();return /consent|cmp|privacy|sourcepoint|didomi|onetrust|quantcast|iubenda|cookie|gdpr/.test(h)||/consent|cmp|privacy|message/.test(p);}catch(x){return false;}}" &
        "function dentroBanner(e){var n=e;for(var i=0;i<20&&n&&n!==document.body;i++){var id=(n.id||'').toString().toLowerCase();var cl=(typeof n.className==='string'?n.className:'').toLowerCase();for(var j=0;j<contenitori.length;j++){if(id.indexOf(contenitori[j])>=0||cl.indexOf(contenitori[j])>=0){return true;}}var ruolo=((n.getAttribute&&n.getAttribute('role'))||'').toLowerCase();if(ruolo==='dialog'||ruolo==='alertdialog'){var t=(n.innerText||'').toLowerCase();if(t.indexOf('cookie')>=0||t.indexOf('consenso')>=0||t.indexOf('privacy')>=0){return true;}}n=n.parentElement;}return dentroFrameConsenso();}" &
        "function testoPulsante(e){return ((e.innerText||e.value||(e.getAttribute&&e.getAttribute('aria-label'))||'')+'').trim().toLowerCase().replace(/\s+/g,' ');}" &
        "function ammesso(t){if(t.length===0||t.length>40){return false;}for(var i=0;i<esclusi.length;i++){if(t.indexOf(esclusi[i])>=0){return false;}}for(var k=0;k<testi.length;k++){if(t===testi[k]){return true;}}return false;}" &
        "function trovaSelettore(){for(var i=0;i<selettori.length;i++){var el=null;try{el=document.querySelector(selettori[i]);}catch(x){el=null;}if(el&&visibile(el)){return el;}}return null;}" &
        "function provaSelettori(){var el=trovaSelettore();if(el){el.click();return true;}return false;}" &
        "function provaTesti(){var c=document.querySelectorAll('button,[role=button],a,input[type=button],input[type=submit]');for(var i=0;i<c.length;i++){var e=c[i];if(!visibile(e)){continue;}var t=testoPulsante(e);if(!ammesso(t)){continue;}if(dentroBanner(e)){e.click();return true;}}return false;}" &
        "function bannerVisibile(){if(trovaSelettore()){return true;}var f=document.querySelectorAll('iframe');for(var j=0;j<f.length;j++){var d=((f[j].id||'')+' '+(f[j].src||'')+' '+(f[j].title||'')).toLowerCase();if(/sp_message|consent|cmp|privacy|cookie/.test(d)&&visibile(f[j])){return true;}}return false;}" &
        "window.__wgBannerConsensoVisibile=function(){try{return bannerVisibile();}catch(x){return false;}};" &
        "var tentativi=0;" &
        "function ciclo(){tentativi++;var cliccato=false;try{if(document.body){cliccato=provaSelettori()||provaTesti();}}catch(x){}if(!cliccato&&tentativi<40){setTimeout(ciclo,500);}}" &
        "ciclo();" &
        "})();"

    Private _thread As Thread
    Private _formNascosta As Form
    Private _webView As WebView2
    Private _timerAttesa As System.Windows.Forms.Timer
    Private _timerTimeout As System.Windows.Forms.Timer
    Private ReadOnly _prontoOAvviaFallito As New ManualResetEventSlim(False)
    Private _erroreInizializzazione As String = Nothing
    Private _motorePronto As Boolean = False
    Private ReadOnly _codaLock As New Object()
    Private ReadOnly _statoLock As New Object()
    Private _contatoreRichieste As Long = 0
    Private _idAttivo As Long = 0
    Private _richiestaAttiva As Boolean = False
    Private _risultatoRichiesta As String = Nothing
    Private _eventoCompletamento As ManualResetEventSlim = Nothing
    Private _navigazioneCompletata As Boolean = False
    Private _estrazioneInCorso As Boolean = False
    Private _controlliConsenso As Integer = 0
    ''' <summary>Se non è vuoto, la richiesta attiva aspetta la risposta di rete il cui indirizzo contiene questo testo (invece dell'HTML).</summary>
    Private _filtroRete As String = ""

    Private Sub New()
    End Sub

    Public Shared Function GetInstance() As WGWebView
        Try
            ' Senza runtime WebView2 si avvia l'installazione guidata (con avanzamento) e per ora non c'e' istanza:
            ' alla chiamata successiva, a installazione conclusa, il motore parte normalmente.
            If _istanza Is Nothing AndAlso Not WGRuntimeWebView2.RuntimePresente() Then
                WGRuntimeWebView2.InstallaConAvanzamento(Nothing, True)
                Return Nothing
            End If
            SyncLock _lock
                If _istanza Is Nothing Then
                    _istanza = New WGWebView()
                    _istanza.Avvia()
                End If
                Return _istanza
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWebView.GetInstance: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub Avvia()
        Try
            _thread = New Thread(AddressOf CicloThread)
            _thread.SetApartmentState(ApartmentState.STA)
            _thread.IsBackground = True
            _thread.Name = "WGWebView"
            _thread.Start()

            If Not _prontoOAvviaFallito.Wait(AttesaAvvioMs) Then
                _erroreInizializzazione = "il motore WebView2 non si e' avviato entro il tempo previsto"
                logger.ScriviLog("WGWebView.Avvia: " & _erroreInizializzazione)
            End If
        Catch ex As Exception
            logger.ScriviLog("WGWebView.Avvia: " & ex.Message)
            _erroreInizializzazione = ex.Message
        End Try
    End Sub

    Private Sub CicloThread()
        Try
            _formNascosta = New Form()
            _formNascosta.ShowInTaskbar = False
            _formNascosta.FormBorderStyle = FormBorderStyle.FixedToolWindow
            _formNascosta.StartPosition = FormStartPosition.Manual
            _formNascosta.Location = New Point(-32000, -32000)
            _formNascosta.Size = New Size(1280, 900)
            _formNascosta.Opacity = 0

            _webView = New WebView2()
            _webView.Dock = DockStyle.Fill
            _formNascosta.Controls.Add(_webView)

            _timerAttesa = New System.Windows.Forms.Timer()
            AddHandler _timerAttesa.Tick, AddressOf AllaScadenzaAttesa
            _timerTimeout = New System.Windows.Forms.Timer()
            AddHandler _timerTimeout.Tick, AddressOf AllaScadenzaTimeout

            _formNascosta.Show()
            _formNascosta.Hide()

            InizializzaWebViewAsync()

            Application.Run()
        Catch ex As Exception
            logger.ScriviLog("WGWebView.CicloThread: " & ex.Message)
            _erroreInizializzazione = ex.Message
            _motorePronto = False
            _prontoOAvviaFallito.Set()
        End Try
    End Sub

    Private Async Sub InizializzaWebViewAsync()
        Try
            ' questa pagina nascosta gira su un thread suo: l'ambiente condiviso è del thread dell'interfaccia e qui non si può usare
            Dim ambiente As CoreWebView2Environment = Await WGMotoreBrowser.CreaAmbientePerQuestoThreadAsync()
            If ambiente Is Nothing Then
                _erroreInizializzazione = "il motore WebView2 non è disponibile"
                _motorePronto = False
                Return
            End If
            Await _webView.EnsureCoreWebView2Async(ambiente)
            ConfiguraMotore()
            Await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ScriptConsenso)
            _erroreInizializzazione = Nothing
            _motorePronto = True
        Catch ex As Exception
            logger.ScriviLog("WGWebView.InizializzaWebViewAsync: " & ex.Message)
            _erroreInizializzazione = ex.Message
            _motorePronto = False
        Finally
            _prontoOAvviaFallito.Set()
        End Try
    End Sub

    Private Sub ConfiguraMotore()
        Try
            Dim impostazioni As CoreWebView2Settings = _webView.CoreWebView2.Settings
            impostazioni.AreDefaultScriptDialogsEnabled = False
            impostazioni.AreDevToolsEnabled = False
            impostazioni.IsStatusBarEnabled = False
            impostazioni.AreDefaultContextMenusEnabled = False

            AddHandler _webView.CoreWebView2.NavigationStarting, AddressOf AllInizioNavigazione
            AddHandler _webView.CoreWebView2.NavigationCompleted, AddressOf AllaFineNavigazione
            AddHandler _webView.CoreWebView2.NewWindowRequested, AddressOf AllaRichiestaNuovaFinestra
            AddHandler _webView.CoreWebView2.ScriptDialogOpening, AddressOf AllaFinestraDialogo
            AddHandler _webView.CoreWebView2.WebResourceResponseReceived, AddressOf AllaRispostaRete
        Catch ex As Exception
            logger.ScriviLog("WGWebView.ConfiguraMotore: " & ex.Message)
        End Try

        Try
            _webView.CoreWebView2.IsMuted = True
        Catch ex As Exception
            logger.ScriviLog("WGWebView.ConfiguraMotore (audio): " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Disponibile As Boolean
        Get
            Try
                If Not _motorePronto Then Return False
                If Not String.IsNullOrEmpty(_erroreInizializzazione) Then Return False
                If _webView Is Nothing OrElse _formNascosta Is Nothing Then Return False
                Return Not _formNascosta.IsDisposed
            Catch ex As Exception
                logger.ScriviLog("WGWebView.Disponibile: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Function OttieniHtmlRenderizzato(ByVal url As String, Optional ByVal timeoutMs As Integer = 15000) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return String.Empty
            If Not Disponibile Then Return String.Empty
            If Not _formNascosta.IsHandleCreated Then Return String.Empty

            Dim durata As Integer = Math.Max(DurataMinimaMs, timeoutMs)

            SyncLock _codaLock
                Dim evento As New ManualResetEventSlim(False)
                Dim id As Long = Interlocked.Increment(_contatoreRichieste)

                SyncLock _statoLock
                    _idAttivo = id
                    _richiestaAttiva = True
                    _risultatoRichiesta = Nothing
                    _eventoCompletamento = evento
                End SyncLock

                _formNascosta.BeginInvoke(New Action(Of String, Integer, Long)(AddressOf AvviaRichiesta), url, durata, id)

                Dim completata As Boolean = evento.Wait(durata + MargineAttesaMs)
                Dim risultato As String = Nothing

                SyncLock _statoLock
                    If Not completata AndAlso _idAttivo = id Then _richiestaAttiva = False
                    risultato = _risultatoRichiesta
                    _risultatoRichiesta = Nothing
                    _eventoCompletamento = Nothing
                End SyncLock

                If Not completata Then
                    _formNascosta.BeginInvoke(New Action(AddressOf InterrompiRichiesta))
                    Return String.Empty
                End If

                Return If(risultato, String.Empty)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in WGWebView.OttieniHtmlRenderizzato per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    ''' <summary>
    ''' Carica una pagina in un browser vero (nascosto) e restituisce il corpo della risposta di rete che la pagina stessa chiede
    ''' e il cui indirizzo contiene parteIndirizzo (per esempio un servizio JSON). Stringa vuota se non arriva entro il tempo.
    ''' La pagina fa le sue richieste come sempre: qui si legge soltanto quello che riceve.
    ''' </summary>
    Public Function OttieniRispostaRete(ByVal url As String, ByVal parteIndirizzo As String, Optional ByVal timeoutMs As Integer = 30000) As String
        Try
            If String.IsNullOrWhiteSpace(url) OrElse String.IsNullOrWhiteSpace(parteIndirizzo) Then Return String.Empty
            If Not Disponibile Then Return String.Empty
            If Not _formNascosta.IsHandleCreated Then Return String.Empty
            Dim durata As Integer = Math.Max(DurataMinimaMs, timeoutMs)
            SyncLock _codaLock
                Dim evento As New ManualResetEventSlim(False)
                Dim id As Long = Interlocked.Increment(_contatoreRichieste)
                SyncLock _statoLock
                    _idAttivo = id
                    _richiestaAttiva = True
                    _risultatoRichiesta = Nothing
                    _eventoCompletamento = evento
                    _filtroRete = parteIndirizzo
                End SyncLock
                _formNascosta.BeginInvoke(New Action(Of String, Integer, Long)(AddressOf AvviaRichiesta), url, durata, id)
                Dim completata As Boolean = evento.Wait(durata + MargineAttesaMs)
                Dim risultato As String = Nothing
                SyncLock _statoLock
                    If Not completata AndAlso _idAttivo = id Then _richiestaAttiva = False
                    risultato = _risultatoRichiesta
                    _risultatoRichiesta = Nothing
                    _eventoCompletamento = Nothing
                    _filtroRete = ""
                End SyncLock
                If Not completata Then
                    _formNascosta.BeginInvoke(New Action(AddressOf InterrompiRichiesta))
                    Return String.Empty
                End If
                Return If(risultato, String.Empty)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWebView.OttieniRispostaRete: " & ex.Message & " - StackTrace: " & ex.StackTrace)
            SyncLock _statoLock
                _filtroRete = ""
            End SyncLock
            Return String.Empty
        End Try
    End Function

    Private Async Sub AllaRispostaRete(ByVal sender As Object, ByVal e As CoreWebView2WebResourceResponseReceivedEventArgs)
        Try
            Dim id As Long = IdRichiestaAttiva()
            If id = 0 Then Return
            Dim filtro As String
            SyncLock _statoLock
                filtro = _filtroRete
            End SyncLock
            If String.IsNullOrEmpty(filtro) Then Return
            If e.Request.Uri.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0 Then Return
            If e.Response.StatusCode <> 200 Then Return
            Using flusso As IO.Stream = Await e.Response.GetContentAsync()
                If flusso Is Nothing Then Return
                Using lettore As New IO.StreamReader(flusso, System.Text.Encoding.UTF8)
                    Dim corpo As String = Await lettore.ReadToEndAsync()
                    If Not String.IsNullOrWhiteSpace(corpo) Then CompletaRichiesta(id, corpo)
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaRispostaRete: " & ex.Message)
        End Try
    End Sub

    Private Function InModalitaRete() As Boolean
        SyncLock _statoLock
            Return Not String.IsNullOrEmpty(_filtroRete)
        End SyncLock
    End Function

    Private Function RichiestaCorrente(ByVal id As Long) As Boolean
        Try
            SyncLock _statoLock
                Return _richiestaAttiva AndAlso _idAttivo = id
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWebView.RichiestaCorrente: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function IdRichiestaAttiva() As Long
        Try
            SyncLock _statoLock
                If Not _richiestaAttiva Then Return 0
                Return _idAttivo
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWebView.IdRichiestaAttiva: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub AvviaRichiesta(ByVal url As String, ByVal durata As Integer, ByVal id As Long)
        Try
            If Not RichiestaCorrente(id) Then Return
            FermaTimer()
            _navigazioneCompletata = False
            _estrazioneInCorso = False
            _controlliConsenso = 0
            _timerTimeout.Interval = durata
            _timerTimeout.Start()
            _webView.CoreWebView2.Navigate(url)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in WGWebView.AvviaRichiesta per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            CompletaRichiesta(id, String.Empty)
        End Try
    End Sub

    Private Sub AllInizioNavigazione(ByVal sender As Object, ByVal e As CoreWebView2NavigationStartingEventArgs)
        Try
            If IdRichiestaAttiva() = 0 Then Return
            _timerAttesa.Stop()
            _navigazioneCompletata = False
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllInizioNavigazione: " & ex.Message)
        End Try
    End Sub

    Private Sub AllaFineNavigazione(ByVal sender As Object, ByVal e As CoreWebView2NavigationCompletedEventArgs)
        Try
            If IdRichiestaAttiva() = 0 Then Return
            _navigazioneCompletata = True
            _timerAttesa.Stop()
            _timerAttesa.Interval = AttesaDopoCaricamentoMs
            _timerAttesa.Start()
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaFineNavigazione: " & ex.Message)
        End Try
    End Sub

    Private Sub AllaRichiestaNuovaFinestra(ByVal sender As Object, ByVal e As CoreWebView2NewWindowRequestedEventArgs)
        Try
            e.Handled = True
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaRichiestaNuovaFinestra: " & ex.Message)
        End Try
    End Sub

    Private Sub AllaFinestraDialogo(ByVal sender As Object, ByVal e As CoreWebView2ScriptDialogOpeningEventArgs)
        Try
            If e Is Nothing Then Return
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaFinestraDialogo: " & ex.Message)
        End Try
    End Sub

    Private Sub AllaScadenzaAttesa(ByVal sender As Object, ByVal e As EventArgs)
        Try
            _timerAttesa.Stop()
            Dim id As Long = IdRichiestaAttiva()
            If id = 0 Then Return
            ' Aspettando una risposta di rete non si legge l'HTML: arriva la risposta, oppure scade il tempo.
            If InModalitaRete() Then Return
            ControllaEdEstrai(id)
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaScadenzaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub AllaScadenzaTimeout(ByVal sender As Object, ByVal e As EventArgs)
        Try
            _timerTimeout.Stop()
            _timerAttesa.Stop()
            Dim id As Long = IdRichiestaAttiva()
            If id = 0 Then Return
            If InModalitaRete() Then
                CompletaRichiesta(id, String.Empty)
                Return
            End If
            _controlliConsenso = ControlliConsensoMassimi
            Try
                _webView.CoreWebView2.Stop()
            Catch exArresto As Exception
                logger.ScriviLog("WGWebView.AllaScadenzaTimeout (arresto caricamento): " & exArresto.Message)
            End Try
            ControllaEdEstrai(id)
        Catch ex As Exception
            logger.ScriviLog("WGWebView.AllaScadenzaTimeout: " & ex.Message)
        End Try
    End Sub

    Private Async Sub ControllaEdEstrai(ByVal id As Long)
        Dim avviata As Boolean = False
        Try
            If _estrazioneInCorso Then Return
            _estrazioneInCorso = True
            avviata = True

            If _controlliConsenso < ControlliConsensoMassimi Then
                Dim esitoBanner As String = Await _webView.CoreWebView2.ExecuteScriptAsync(ScriptBannerVisibile)
                If Not RichiestaCorrente(id) Then Return
                If String.Equals(esitoBanner, "1", StringComparison.Ordinal) Then
                    _controlliConsenso += 1
                    _timerAttesa.Stop()
                    _timerAttesa.Interval = AttesaConsensoMs
                    _timerAttesa.Start()
                    Return
                End If
            End If

            Dim risultatoJson As String = Await _webView.CoreWebView2.ExecuteScriptAsync(ScriptHtml)
            If Not RichiestaCorrente(id) Then Return

            Dim html As String = String.Empty
            If Not String.IsNullOrEmpty(risultatoJson) AndAlso Not String.Equals(risultatoJson, "null", StringComparison.Ordinal) Then
                html = Newtonsoft.Json.JsonConvert.DeserializeObject(Of String)(risultatoJson)
            End If
            CompletaRichiesta(id, If(html, String.Empty))
        Catch ex As Exception
            logger.ScriviLog("WGWebView.ControllaEdEstrai: " & ex.Message)
            CompletaRichiesta(id, String.Empty)
        Finally
            If avviata Then _estrazioneInCorso = False
        End Try
    End Sub

    Private Sub CompletaRichiesta(ByVal id As Long, ByVal html As String)
        Try
            Dim evento As ManualResetEventSlim = Nothing
            SyncLock _statoLock
                If Not _richiestaAttiva OrElse _idAttivo <> id Then Return
                _richiestaAttiva = False
                _risultatoRichiesta = html
                evento = _eventoCompletamento
            End SyncLock

            FermaTimer()
            If evento IsNot Nothing Then evento.Set()
            SvuotaPagina()
        Catch ex As Exception
            logger.ScriviLog("WGWebView.CompletaRichiesta: " & ex.Message)
        End Try
    End Sub

    Private Sub InterrompiRichiesta()
        Try
            If IdRichiestaAttiva() <> 0 Then Return
            FermaTimer()
            Try
                _webView.CoreWebView2.Stop()
            Catch exArresto As Exception
                logger.ScriviLog("WGWebView.InterrompiRichiesta (arresto caricamento): " & exArresto.Message)
            End Try
            SvuotaPagina()
        Catch ex As Exception
            logger.ScriviLog("WGWebView.InterrompiRichiesta: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaTimer()
        Try
            If _timerAttesa IsNot Nothing Then _timerAttesa.Stop()
            If _timerTimeout IsNot Nothing Then _timerTimeout.Stop()
        Catch ex As Exception
            logger.ScriviLog("WGWebView.FermaTimer: " & ex.Message)
        End Try
    End Sub

    Private Sub SvuotaPagina()
        Try
            If _webView Is Nothing OrElse _webView.CoreWebView2 Is Nothing Then Return
            _webView.CoreWebView2.Navigate(PaginaVuota)
        Catch ex As Exception
            logger.ScriviLog("WGWebView.SvuotaPagina: " & ex.Message)
        End Try
    End Sub
End Class