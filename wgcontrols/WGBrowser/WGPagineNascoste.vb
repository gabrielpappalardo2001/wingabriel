' ============================================================================================
' WGPagineNascoste - una scheda del browser che non si vede, per leggere una pagina con uno script
' ============================================================================================
'
' È quello che sta dietro a WGPaginaNascosta (in wgcontrols): apre l'indirizzo in una pagina del
' motore del browser che non viene mai mostrata, con lo stesso profilo delle schede normali (quindi
' gli stessi accessi ai siti) e la stessa identità che il browser usa per quel sito. A pagina
' caricata esegue lo script e ne restituisce il risultato.
' Le richieste le fa il motore del browser, come in una scheda normale: i cookie non escono da lì.
' La pagina è muta, non apre finestre e non scarica niente.
'
' La pagina è una sola e resta aperta per tutta la sessione: tra un uso e l'altro torna vuota (about:blank).
' Non la si chiude e non si distrugge la finestra che la ospita: chiudere il controller e distruggere subito
' la finestra faceva cadere il programma (violazione di accesso dentro EmbeddedBrowserWebView.dll, quando
' il motore rispondeva a una finestra ormai sparita). Se il motore si ferma o cambia, la pagina si rifà
' da capo; quella vecchia si chiude ma la sua finestra resta in un elenco, mai distrutta.
' Un uso alla volta: gli altri aspettano il loro turno.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Public NotInheritable Class WGPagineNascoste

    Private Shared ReadOnly log As New Logger()
    Private Const AttesaCaricamento As Integer = 30000
    Private Const AttesaScript As Integer = 120000
    Private Const AttesaPaginaVuota As Integer = 5000

    ' Come nelle schede normali, la pagina non deve vedere il ponte verso il programma.
    Private Const ScriptSenzaPonte As String =
        "(function(){try{var c=window.chrome;if(!c||!c.webview)return;try{delete c.webview;}catch(e){}" &
        "if(c.webview){try{Object.defineProperty(c,'webview',{value:undefined,configurable:true,writable:true});}catch(e){}}}catch(e){}})();"

    Private Sub New()
    End Sub

    ' La finestra che ospita la pagina (mai mostrata) e il suo controller.
    Private Class Pagina
        Public Ospite As Form
        Public Controller As CoreWebView2Controller
        Public Ambiente As CoreWebView2Environment
        Public Guasta As Boolean
    End Class

    Private Shared _pagina As Pagina
    ' Le pagine messe da parte: le loro finestre non si distruggono mai, nemmeno da garbage collector.
    Private Shared ReadOnly _messeDaParte As New List(Of Pagina)()
    Private Shared ReadOnly _turno As New SemaphoreSlim(1, 1)

    ' Il motore del browser vive sul thread dell'interfaccia.
    Private Shared Function SulThreadInterfaccia(Of T)(lavoro As Func(Of Task(Of T))) As Task(Of T)
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then Return CType(contenitore.Invoke(lavoro), Task(Of T))
        Return lavoro()
    End Function

    Private Shared Sub MettiDaParte(pagina As Pagina)
        Try
            If pagina Is Nothing Then Return
            pagina.Guasta = True
            Try
                If pagina.Controller IsNot Nothing Then WGPaginaWeb.ChiudiController(pagina.Controller)
            Catch ex As Exception
                log.ScriviLog("WGPagineNascoste.MettiDaParte: " & ex.Message)
            End Try
            pagina.Controller = Nothing
            SyncLock _messeDaParte
                _messeDaParte.Add(pagina)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGPagineNascoste.MettiDaParte: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Function CreaPaginaAsync(ambiente As CoreWebView2Environment) As Task(Of Pagina)
        Dim pagina As New Pagina() With {.Ambiente = ambiente}
        Try
            pagina.Ospite = New Form() With {.ShowInTaskbar = False, .FormBorderStyle = FormBorderStyle.None,
                                             .StartPosition = FormStartPosition.Manual, .Size = New Size(1280, 900)}
            Dim finestra As IntPtr = pagina.Ospite.Handle
            Dim opzioni As CoreWebView2ControllerOptions = WGMotoreBrowser.OpzioniController(ambiente, False)
            If opzioni IsNot Nothing Then
                pagina.Controller = Await ambiente.CreateCoreWebView2ControllerAsync(finestra, opzioni)
            Else
                pagina.Controller = Await ambiente.CreateCoreWebView2ControllerAsync(finestra)
            End If
            If pagina.Controller Is Nothing Then
                MettiDaParte(pagina)
                Return Nothing
            End If
            pagina.Controller.Bounds = New Rectangle(0, 0, 1280, 900)
            pagina.Controller.IsVisible = False
            Dim core As CoreWebView2 = pagina.Controller.CoreWebView2
            core.IsMuted = True
            core.Settings.AreDevToolsEnabled = False
            core.Settings.AreDefaultContextMenusEnabled = False
            core.Settings.AreDefaultScriptDialogsEnabled = False
            AddHandler core.NewWindowRequested, Sub(s As Object, e As CoreWebView2NewWindowRequestedEventArgs) e.Handled = True
            AddHandler core.DownloadStarting, Sub(s As Object, e As CoreWebView2DownloadStartingEventArgs) e.Cancel = True
            AddHandler core.PermissionRequested, Sub(s As Object, e As CoreWebView2PermissionRequestedEventArgs) e.State = CoreWebView2PermissionState.Deny
            AddHandler core.ProcessFailed, Sub(s As Object, e As CoreWebView2ProcessFailedEventArgs) pagina.Guasta = True
            Await core.AddScriptToExecuteOnDocumentCreatedAsync(WGIdentitaBrowser.ScriptAntiRilevamento & ScriptSenzaPonte)
            Return pagina
        Catch ex As Exception
            log.ScriviLog("WGPagineNascoste.CreaPaginaAsync: " & ex.Message)
            MettiDaParte(pagina)
            Return Nothing
        End Try
    End Function

    ' Un uso alla volta della pagina condivisa, rifatta se il motore è cambiato o si è fermato.
    Private Shared Async Function UsaPaginaAsync(Of T)(lavoro As Func(Of Pagina, Task(Of T))) As Task(Of T)
        Await _turno.WaitAsync()
        Try
            Dim ambiente As CoreWebView2Environment = Await WGMotoreBrowser.AssicuraAmbienteAsync()
            If ambiente Is Nothing Then Return Nothing
            If _pagina IsNot Nothing AndAlso (_pagina.Guasta OrElse Not ReferenceEquals(_pagina.Ambiente, ambiente)) Then
                MettiDaParte(_pagina)
                _pagina = Nothing
            End If
            If _pagina Is Nothing Then _pagina = Await CreaPaginaAsync(ambiente)
            If _pagina Is Nothing Then Return Nothing
            Dim inUso As Pagina = _pagina
            Try
                Return Await lavoro(inUso)
            Catch ex As Exception
                log.ScriviLog("WGPagineNascoste.UsaPaginaAsync: " & ex.Message)
                MettiDaParte(inUso)
                If ReferenceEquals(_pagina, inUso) Then _pagina = Nothing
                Return Nothing
            End Try
        Finally
            _turno.Release()
        End Try
    End Function

    ''' <summary>
    ''' Apre l'indirizzo nella pagina nascosta, aspetta che sia caricato ed esegue lo script (un'espressione
    ''' che dà una stringa, anche una promessa). Restituisce la stringa, Nothing se qualcosa non è riuscito.
    ''' </summary>
    Public Shared Function EseguiAsync(indirizzo As String, script As String) As Task(Of String)
        Return SulThreadInterfaccia(Function() UsaPaginaAsync(Of String)(Function(pagina As Pagina) EseguiNellaPaginaAsync(pagina, indirizzo, script)))
    End Function

    Private Shared Async Function EseguiNellaPaginaAsync(pagina As Pagina, indirizzo As String, script As String) As Task(Of String)
        Dim core As CoreWebView2 = pagina.Controller.CoreWebView2
        Dim risultato As String = Nothing
        Dim errore As Exception = Nothing
        Try
            risultato = Await CaricaEValutaAsync(core, indirizzo, script)
        Catch ex As Exception
            errore = ex
        End Try
        ' In ogni caso la pagina torna vuota (non si può aspettare dentro un Finally).
        Await TornaVuotaAsync(core)
        If errore IsNot Nothing Then Throw errore
        Return risultato
    End Function

    Private Shared Async Function CaricaEValutaAsync(core As CoreWebView2, indirizzo As String, script As String) As Task(Of String)
        ' La stessa identità che il browser usa per quel sito, così per il sito è la stessa sessione di sempre.
        Dim identita As JObject = WGIdentitaBrowser.ParametriOverride(WGIdentitaBrowser.IdentitaPerIndirizzo(indirizzo))
        Await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", identita.ToString(Formatting.None))
        Dim caricata As New TaskCompletionSource(Of Boolean)()
        Dim alCaricamento As EventHandler(Of CoreWebView2NavigationCompletedEventArgs) = Sub(s As Object, e As CoreWebView2NavigationCompletedEventArgs) caricata.TrySetResult(e.IsSuccess)
        AddHandler core.NavigationCompleted, alCaricamento
        Try
            core.Navigate(indirizzo)
            Dim primo As Task = Await Task.WhenAny(caricata.Task, Task.Delay(AttesaCaricamento))
            If primo IsNot caricata.Task OrElse Not caricata.Task.Result Then
                log.ScriviLog("WGPagineNascoste.EseguiAsync: pagina non caricata, " & indirizzo)
                Return Nothing
            End If
            ' Se YouTube (o un altro sito) manda la pagina altrove con più passaggi, si aspetta che anche l'ultima sia caricata.
            If Not Await AspettaPaginaPronta(core) Then
                log.ScriviLog("WGPagineNascoste.EseguiAsync: la pagina non è diventata pronta, " & indirizzo & " (" & core.Source & ")")
            End If
            Dim parametri As New JObject()
            parametri("expression") = script
            parametri("awaitPromise") = True
            parametri("returnByValue") = True
            Dim valutazione As Task(Of String) = core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parametri.ToString(Formatting.None))
            primo = Await Task.WhenAny(valutazione, Task.Delay(AttesaScript))
            If primo IsNot valutazione Then
                log.ScriviLog("WGPagineNascoste.EseguiAsync: lo script non ha risposto in tempo, " & indirizzo)
                Return Nothing
            End If
            Dim risposta As JObject = JObject.Parse(Await valutazione)
            If risposta("exceptionDetails") IsNot Nothing Then
                log.ScriviLog("WGPagineNascoste.EseguiAsync: errore dello script, " & risposta("exceptionDetails").ToString(Formatting.None))
                Return Nothing
            End If
            Dim valore As JToken = risposta.SelectToken("result.value")
            Return If(valore Is Nothing, Nothing, valore.ToString())
        Finally
            RemoveHandler core.NavigationCompleted, alCaricamento
        End Try
    End Function

    ' Aspetta (al massimo cinque secondi) che il documento sia del tutto caricato.
    Private Shared Async Function AspettaPaginaPronta(core As CoreWebView2) As Task(Of Boolean)
        Try
            For i As Integer = 0 To 19
                Dim stato As String = Await core.ExecuteScriptAsync("document.readyState")
                If stato IsNot Nothing AndAlso stato.Contains("complete") Then Return True
                Await Task.Delay(250)
            Next
        Catch ex As Exception
            log.ScriviLog("WGPagineNascoste.AspettaPaginaPronta: " & ex.Message)
        End Try
        Return False
    End Function

    ' Tra un uso e l'altro la pagina resta vuota; si aspetta che lo sia, così il caricamento della pagina
    ' vuota non può scambiarsi con quello della pagina dopo.
    Private Shared Async Function TornaVuotaAsync(core As CoreWebView2) As Task
        Try
            Dim vuota As New TaskCompletionSource(Of Boolean)()
            Dim alCaricamento As EventHandler(Of CoreWebView2NavigationCompletedEventArgs) = Sub(s As Object, e As CoreWebView2NavigationCompletedEventArgs) vuota.TrySetResult(True)
            AddHandler core.NavigationCompleted, alCaricamento
            Try
                core.Navigate("about:blank")
                Dim dopo As Task = Await Task.WhenAny(vuota.Task, Task.Delay(AttesaPaginaVuota))
            Finally
                RemoveHandler core.NavigationCompleted, alCaricamento
            End Try
        Catch ex As Exception
            log.ScriviLog("WGPagineNascoste.TornaVuotaAsync: " & ex.Message)
        End Try
    End Function

    ''' <summary>Vero se il browser ha, per quel sito, almeno uno dei cookie indicati, non vuoto e non scaduto.</summary>
    Public Shared Function HaCookieAsync(indirizzo As String, nomi As String()) As Task(Of Boolean)
        If nomi Is Nothing OrElse nomi.Length = 0 Then Return Task.FromResult(False)
        Return SulThreadInterfaccia(Function() UsaPaginaAsync(Of Boolean)(Function(pagina As Pagina) HaCookieNellaPaginaAsync(pagina, indirizzo, nomi)))
    End Function

    Private Shared Async Function HaCookieNellaPaginaAsync(pagina As Pagina, indirizzo As String, nomi As String()) As Task(Of Boolean)
        Dim cookie As List(Of CoreWebView2Cookie) = Await pagina.Controller.CoreWebView2.CookieManager.GetCookiesAsync(indirizzo)
        If cookie Is Nothing Then Return False
        Dim adesso As DateTime = DateTime.Now
        Return cookie.Any(Function(c) c IsNot Nothing AndAlso nomi.Contains(c.Name) AndAlso Not String.IsNullOrEmpty(c.Value) AndAlso (c.IsSession OrElse c.Expires > adesso))
    End Function

End Class
