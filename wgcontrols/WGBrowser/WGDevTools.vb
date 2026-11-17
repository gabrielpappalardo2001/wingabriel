Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Public Class WGDevTools
    Implements IDisposable

    Public Class SessioneFrame
        Public Property IdSessione As String = ""
        Public Property IdFrame As String = ""
        Public Property Url As String = ""
        Public Property SessionePadre As String = ""
        Public Property IdProprietario As Integer = 0
    End Class

    Public Event EventoRicevuto(metodo As String, parametri As JObject, sessione As String)

    Private Shared ReadOnly EventiAscoltati As String() = {"Target.attachedToTarget", "Target.detachedFromTarget", "Input.dragIntercepted", "Page.fileChooserOpened", "Runtime.bindingCalled", "Network.responseReceivedExtraInfo"}
    Private Const LunghezzaAnalisiInSfondo As Integer = 100000
    Public Const NomeLegame As String = "__wgLegame"
    Private _ultimoNodoObsoleto As DateTime = DateTime.MinValue

    Private ReadOnly log As New Logger()
    Private ReadOnly _pagina As WGPaginaWeb
    Private ReadOnly _ricevitori As New List(Of KeyValuePair(Of CoreWebView2DevToolsProtocolEventReceiver, EventHandler(Of CoreWebView2DevToolsProtocolEventReceivedEventArgs)))()
    Private ReadOnly sessioni As New ConcurrentDictionary(Of String, SessioneFrame)()
    Private ReadOnly sessioniPreparate As New ConcurrentDictionary(Of String, Boolean)()
    Private ReadOnly sessioniConSnapshot As New ConcurrentDictionary(Of String, Boolean)()
    Private ReadOnly erroriRegistrati As New ConcurrentDictionary(Of String, DateTime)()
    Private _coreCollegato As CoreWebView2 = Nothing
    Private collegato As Boolean = False
    Private _chiuso As Boolean = False
    Private _parametriUserAgent As JObject = Nothing
    Private _focusEmulato As Boolean? = Nothing

    Public Sub New(pagina As WGPaginaWeb)
        _pagina = pagina
    End Sub

    Public ReadOnly Property Frame As IEnumerable(Of SessioneFrame)
        Get
            Try
                Return sessioni.Values.ToList()
            Catch ex As Exception
                log.ScriviLog("WGDevTools.Frame: " & ex.Message)
                Return New List(Of SessioneFrame)()
            End Try
        End Get
    End Property

    Public ReadOnly Property ECollegato As Boolean
        Get
            Return collegato AndAlso Not _chiuso AndAlso _pagina IsNot Nothing AndAlso _pagina.Pronta
        End Get
    End Property

    Public ReadOnly Property Pagina As WGPaginaWeb
        Get
            Return _pagina
        End Get
    End Property

    Private Shared Function ErroreAtteso(metodo As String) As Boolean
        Return String.Equals(metodo, "Runtime.releaseObject", StringComparison.Ordinal) OrElse String.Equals(metodo, "Runtime.releaseObjectGroup", StringComparison.Ordinal) OrElse String.Equals(metodo, "DOM.resolveNode", StringComparison.Ordinal)
    End Function

    Private Sub RegistraErrore(metodo As String, messaggio As String)
        Try
            Dim chiave As String = If(metodo, "")
            Dim ora As DateTime = DateTime.Now
            Dim precedente As DateTime
            If erroriRegistrati.TryGetValue(chiave, precedente) AndAlso (ora - precedente).TotalSeconds < 60 Then Return
            erroriRegistrati(chiave) = ora
            log.ScriviLog("WGDevTools " & chiave & ": " & messaggio)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.RegistraErrore: " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraRicevitori(core As CoreWebView2)
        Try
            If core Is Nothing OrElse core Is _coreCollegato Then Return
            RimuoviRicevitori()
            _coreCollegato = core
            For Each nomeEvento As String In EventiAscoltati
                Try
                    Dim metodo As String = nomeEvento
                    Dim ricevitore As CoreWebView2DevToolsProtocolEventReceiver = core.GetDevToolsProtocolEventReceiver(metodo)
                    If ricevitore Is Nothing Then Continue For
                    Dim gestore As EventHandler(Of CoreWebView2DevToolsProtocolEventReceivedEventArgs) = Sub(mittente As Object, argomenti As CoreWebView2DevToolsProtocolEventReceivedEventArgs) EventoProtocollo(metodo, argomenti)
                    AddHandler ricevitore.DevToolsProtocolEventReceived, gestore
                    _ricevitori.Add(New KeyValuePair(Of CoreWebView2DevToolsProtocolEventReceiver, EventHandler(Of CoreWebView2DevToolsProtocolEventReceivedEventArgs))(ricevitore, gestore))
                Catch exEvento As Exception
                    log.ScriviLog("WGDevTools.RegistraRicevitori " & nomeEvento & ": " & exEvento.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGDevTools.RegistraRicevitori: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviRicevitori()
        Try
            For Each coppia As KeyValuePair(Of CoreWebView2DevToolsProtocolEventReceiver, EventHandler(Of CoreWebView2DevToolsProtocolEventReceivedEventArgs)) In _ricevitori
                Try
                    RemoveHandler coppia.Key.DevToolsProtocolEventReceived, coppia.Value
                Catch exRimuovi As Exception
                    log.ScriviLog("WGDevTools.RimuoviRicevitori ricevitore: " & exRimuovi.Message)
                End Try
            Next
            _ricevitori.Clear()
            _coreCollegato = Nothing
        Catch ex As Exception
            log.ScriviLog("WGDevTools.RimuoviRicevitori: " & ex.Message)
        End Try
    End Sub

    Public Async Function CollegaAsync() As Task
        Try
            If _chiuso OrElse _pagina Is Nothing Then Return
            If Not _pagina.SuThreadInterfaccia() Then
                Await _pagina.EseguiSuInterfacciaAsync(Of Boolean)(Async Function()
                                                                       Await CollegaAsync()
                                                                       Return True
                                                                   End Function)
                Return
            End If
            Dim core As CoreWebView2 = _pagina.Core
            If core Is Nothing Then Return
            RegistraRicevitori(core)
            collegato = True
            Dim rimosso As Boolean
            sessioniPreparate.TryRemove("", rimosso)
            Await PreparaSessioneAsync("")
            Await AttivaCollegamentoAutomaticoAsync("")
            If _parametriUserAgent IsNot Nothing Then Await InviaAsync("Emulation.setUserAgentOverride", CType(_parametriUserAgent.DeepClone(), JObject), "", 3000)
            If _focusEmulato.HasValue Then Await InviaFocusAsync(_focusEmulato.Value)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.CollegaAsync: " & ex.Message)
        End Try
    End Function

    Private Async Function PreparaSessioneAsync(sessione As String) As Task
        Try
            If Not sessioniPreparate.TryAdd(If(sessione, ""), True) Then Return
            Dim abilitata As JObject = Await InviaAsync("Page.enable", Nothing, sessione, 3000)
            Dim parametri As New JObject()
            parametri("enabled") = True
            Dim intercettazione As JObject = Await InviaAsync("Page.setInterceptFileChooserDialog", parametri, sessione, 3000)
            Dim parLegame As New JObject()
            parLegame("name") = NomeLegame
            Await InviaAsync("Runtime.addBinding", parLegame, sessione, 3000)
            ' Set-Cookie non è visibile negli eventi del motore: solo qui si vede che è arrivato un cookie.
            Await InviaAsync("Network.enable", Nothing, sessione, 3000)
            If abilitata Is Nothing AndAlso intercettazione Is Nothing Then
                Dim rimosso As Boolean
                sessioniPreparate.TryRemove(If(sessione, ""), rimosso)
            End If
        Catch ex As Exception
            log.ScriviLog("WGDevTools.PreparaSessioneAsync: " & ex.Message)
        End Try
    End Function

    Private Async Function AttivaCollegamentoAutomaticoAsync(sessione As String) As Task
        Try
            Dim parametri As New JObject()
            parametri("autoAttach") = True
            parametri("waitForDebuggerOnStart") = False
            parametri("flatten") = True
            Await InviaAsync("Target.setAutoAttach", parametri, sessione, 3000)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.AttivaCollegamentoAutomaticoAsync: " & ex.Message)
        End Try
    End Function

    Private Shared Function AnalizzaOggetto(testo As String) As JObject
        Using lettore As New StringReader(testo)
            Using json As New JsonTextReader(lettore)
                json.DateParseHandling = DateParseHandling.None
                json.MaxDepth = Nothing
                Return TryCast(JToken.Load(json), JObject)
            End Using
        End Using
    End Function

    Private Async Function AnalizzaRispostaAsync(testo As String) As Task(Of JObject)
        Try
            If String.IsNullOrEmpty(testo) Then Return New JObject()
            If testo.Length > LunghezzaAnalisiInSfondo Then
                Return Await Task.Run(Function()
                                          Try
                                              Return AnalizzaOggetto(testo)
                                          Catch exSfondo As Exception
                                              Dim registroSfondo As New Logger()
                                              registroSfondo.ScriviLog("WGDevTools.AnalizzaRispostaAsync sfondo: " & exSfondo.Message)
                                              Return Nothing
                                          End Try
                                      End Function)
            End If
            Return AnalizzaOggetto(testo)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.AnalizzaRispostaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function InviaAsync(metodo As String, Optional parametri As JObject = Nothing, Optional sessione As String = "", Optional millisecondi As Integer = 5000) As Task(Of JObject)
        Try
            If _chiuso OrElse _pagina Is Nothing OrElse String.IsNullOrEmpty(metodo) Then Return Nothing
            If Not _pagina.SuThreadInterfaccia() Then Return Await _pagina.EseguiSuInterfacciaAsync(Function() InviaAsync(metodo, parametri, sessione, millisecondi))
            Dim core As CoreWebView2 = _pagina.Core
            If core Is Nothing Then Return Nothing
            Dim testoParametri As String = If(parametri, New JObject()).ToString(Formatting.None)
            WGTracciaMotore.Scrivi("cdp " & metodo & If(String.IsNullOrEmpty(sessione), "", " sessione " & sessione))
            Dim compito As Task(Of String)
            If String.IsNullOrEmpty(sessione) Then
                compito = core.CallDevToolsProtocolMethodAsync(metodo, testoParametri)
            Else
                compito = core.CallDevToolsProtocolMethodForSessionAsync(sessione, metodo, testoParametri)
            End If
            Dim finito As Task = Await Task.WhenAny(compito, Task.Delay(Math.Max(100, millisecondi)))
            If finito IsNot compito Then
                WGPaginaWeb.OsservaCompito(compito, "InviaAsync " & metodo)
                Return Nothing
            End If
            If compito.IsFaulted Then
                Dim errore As Exception = If(compito.Exception IsNot Nothing, compito.Exception.GetBaseException(), Nothing)
                If Not ErroreAtteso(metodo) Then RegistraErrore(metodo, If(errore IsNot Nothing, errore.Message, "errore sconosciuto"))
                Return Nothing
            End If
            If compito.IsCanceled Then Return Nothing
            Return Await AnalizzaRispostaAsync(compito.Result)
        Catch ex As Exception
            If Not ErroreAtteso(metodo) Then RegistraErrore(metodo, ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub InviaSenzaAttesa(metodo As String, Optional parametri As JObject = Nothing, Optional sessione As String = "")
        Try
            Dim compito As Task(Of JObject) = InviaAsync(metodo, parametri, sessione)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.InviaSenzaAttesa " & metodo & ": " & ex.Message)
        End Try
    End Sub

    Public Async Function ChiamaSuNodoAsync(sessione As String, idNodo As Integer, funzione As String, Optional millisecondi As Integer = 3000, Optional argomenti As JArray = Nothing) As Task(Of JToken)
        Try
            If idNodo <= 0 Then Return Nothing
            If Not String.IsNullOrEmpty(sessione) AndAlso Not sessioni.ContainsKey(sessione) Then Return Nothing
            Dim parRisolvi As New JObject()
            parRisolvi("backendNodeId") = idNodo
            parRisolvi("objectGroup") = "wgbrowser"
            Dim risolto As JObject = Await InviaAsync("DOM.resolveNode", parRisolvi, sessione, millisecondi)
            If risolto Is Nothing OrElse risolto("object") Is Nothing Then
                SegnalaNodoObsoleto(sessione)
                Return Nothing
            End If
            Dim idOggetto As String = risolto("object").Value(Of String)("objectId")
            If String.IsNullOrEmpty(idOggetto) Then Return Nothing
            Dim parChiama As New JObject()
            parChiama("objectId") = idOggetto
            parChiama("functionDeclaration") = funzione
            parChiama("returnByValue") = True
            parChiama("userGesture") = True
            If argomenti IsNot Nothing Then parChiama("arguments") = argomenti
            Dim risultato As JObject = Await InviaAsync("Runtime.callFunctionOn", parChiama, sessione, millisecondi)
            Dim parRilascia As New JObject()
            parRilascia("objectId") = idOggetto
            InviaSenzaAttesa("Runtime.releaseObject", parRilascia, sessione)
            If risultato Is Nothing OrElse risultato("result") Is Nothing Then Return Nothing
            Return risultato("result")("value")
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ChiamaSuNodoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub SegnalaNodoObsoleto(sessione As String)
        Try
            Dim ora As DateTime = DateTime.Now
            If (ora - _ultimoNodoObsoleto).TotalMilliseconds < 1500 Then Return
            _ultimoNodoObsoleto = ora
            RaiseEvent EventoRicevuto("WG.nodoObsoleto", New JObject(), If(sessione, ""))
        Catch ex As Exception
            log.ScriviLog("WGDevTools.SegnalaNodoObsoleto: " & ex.Message)
        End Try
    End Sub

    Public Async Function ValutaAsync(sessione As String, espressione As String, Optional millisecondi As Integer = 3000) As Task(Of JToken)
        Try
            Dim parametri As New JObject()
            parametri("expression") = espressione
            parametri("returnByValue") = True
            parametri("userGesture") = True
            Dim risultato As JObject = Await InviaAsync("Runtime.evaluate", parametri, sessione, millisecondi)
            If risultato Is Nothing OrElse risultato("result") Is Nothing Then Return Nothing
            Return risultato("result")("value")
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ValutaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function IdNodoDaEspressioneAsync(sessione As String, espressione As String) As Task(Of Integer)
        Try
            Dim parametri As New JObject()
            parametri("expression") = espressione
            parametri("returnByValue") = False
            parametri("objectGroup") = "wgbrowser"
            Dim valutato As JObject = Await InviaAsync("Runtime.evaluate", parametri, sessione, 3000)
            If valutato Is Nothing OrElse valutato("result") Is Nothing Then Return 0
            Dim idOggetto As String = valutato("result").Value(Of String)("objectId")
            If String.IsNullOrEmpty(idOggetto) Then Return 0
            Dim parDescrivi As New JObject()
            parDescrivi("objectId") = idOggetto
            Dim descritto As JObject = Await InviaAsync("DOM.describeNode", parDescrivi, sessione, 3000)
            Dim parRilascia As New JObject()
            parRilascia("objectId") = idOggetto
            InviaSenzaAttesa("Runtime.releaseObject", parRilascia, sessione)
            If descritto Is Nothing OrElse descritto("node") Is Nothing Then Return 0
            Return descritto("node").Value(Of Integer?)("backendNodeId").GetValueOrDefault()
        Catch ex As Exception
            log.ScriviLog("WGDevTools.IdNodoDaEspressioneAsync: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Async Function ProprietarioAsync(info As SessioneFrame) As Task(Of Integer)
        Try
            If info Is Nothing Then Return 0
            If info.IdProprietario > 0 Then Return info.IdProprietario
            Dim parametri As New JObject()
            parametri("frameId") = info.IdFrame
            Dim risultato As JObject = Await InviaAsync("DOM.getFrameOwner", parametri, info.SessionePadre, 3000)
            If risultato IsNot Nothing Then info.IdProprietario = risultato.Value(Of Integer?)("backendNodeId").GetValueOrDefault()
            Return info.IdProprietario
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ProprietarioAsync: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Const EspressioneElementoAttivo As String =
        "(function(){var e=document.activeElement;var g=0;" &
        "while(e&&g<30){g++;" &
        "if(e.shadowRoot&&e.shadowRoot.activeElement){e=e.shadowRoot.activeElement;continue;}" &
        "if(e.localName==='iframe'||e.localName==='frame'){var d=null;try{d=e.contentDocument;}catch(x){d=null;}" &
        "if(d&&d.activeElement&&d.activeElement!==d.body){e=d.activeElement;continue;}}" &
        "break;}" &
        "if(!e||e===document.body||e===document.documentElement)return null;" &
        "if(e.ownerDocument&&(e===e.ownerDocument.body))return null;" &
        "return e;})()"

    Public Async Function ElementoAttivoAsync() As Task(Of Tuple(Of String, Integer))
        Dim sessione As String = ""
        Try
            For giro As Integer = 1 To 6
                Dim idNodo As Integer = 0
                Dim nome As String = ""
                Dim parametri As New JObject()
                parametri("expression") = EspressioneElementoAttivo
                parametri("returnByValue") = False
                parametri("objectGroup") = "wgbrowser"
                Dim valutato As JObject = Await InviaAsync("Runtime.evaluate", parametri, sessione, 2000)
                If valutato Is Nothing OrElse valutato("result") Is Nothing Then Return Tuple.Create(sessione, 0)
                Dim idOggetto As String = valutato("result").Value(Of String)("objectId")
                If String.IsNullOrEmpty(idOggetto) Then Return Tuple.Create(sessione, 0)
                Dim parDescrivi As New JObject()
                parDescrivi("objectId") = idOggetto
                Dim descritto As JObject = Await InviaAsync("DOM.describeNode", parDescrivi, sessione, 2000)
                Dim parRilascia As New JObject()
                parRilascia("objectId") = idOggetto
                InviaSenzaAttesa("Runtime.releaseObject", parRilascia, sessione)
                If descritto Is Nothing OrElse descritto("node") Is Nothing Then Return Tuple.Create(sessione, 0)
                idNodo = descritto("node").Value(Of Integer?)("backendNodeId").GetValueOrDefault()
                nome = If(descritto("node").Value(Of String)("nodeName"), "").ToUpperInvariant()
                If nome <> "IFRAME" AndAlso nome <> "FRAME" Then Return Tuple.Create(sessione, idNodo)
                Dim figlio As SessioneFrame = Nothing
                For Each info As SessioneFrame In sessioni.Values
                    If info.SessionePadre <> sessione Then Continue For
                    If Await ProprietarioAsync(info) = idNodo Then
                        figlio = info
                        Exit For
                    End If
                Next
                If figlio Is Nothing Then Return Tuple.Create(sessione, idNodo)
                sessione = figlio.IdSessione
            Next
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ElementoAttivoAsync: " & ex.Message)
        End Try
        Return Tuple.Create(sessione, 0)
    End Function

    Public Async Function AbilitaSnapshotAsync(sessione As String) As Task
        Try
            If Not sessioniConSnapshot.TryAdd(If(sessione, ""), True) Then Return
            Dim risultato As JObject = Await InviaAsync("DOMSnapshot.enable", Nothing, sessione, 3000)
            If risultato Is Nothing Then
                Dim rimosso As Boolean
                sessioniConSnapshot.TryRemove(If(sessione, ""), rimosso)
            End If
        Catch ex As Exception
            log.ScriviLog("WGDevTools.AbilitaSnapshotAsync: " & ex.Message)
        End Try
    End Function

    Public Async Function CatturaSchermataAsync(sessione As String, x As Double, y As Double, larghezza As Double, altezza As Double) As Task(Of Byte())
        Try
            Dim parametri As New JObject()
            parametri("format") = "png"
            parametri("captureBeyondViewport") = True
            If larghezza >= 1 AndAlso altezza >= 1 Then
                Dim ritaglio As New JObject()
                ritaglio("x") = Math.Max(0, x)
                ritaglio("y") = Math.Max(0, y)
                ritaglio("width") = larghezza
                ritaglio("height") = altezza
                ritaglio("scale") = 1
                parametri("clip") = ritaglio
            End If
            Dim risultato As JObject = Await InviaAsync("Page.captureScreenshot", parametri, sessione, 15000)
            If risultato IsNot Nothing Then
                Dim dati As String = risultato.Value(Of String)("data")
                If Not String.IsNullOrEmpty(dati) Then Return Convert.FromBase64String(dati)
            End If
            If larghezza >= 1 AndAlso altezza >= 1 Then Return Nothing
            Return Await AnteprimaAsync()
        Catch ex As Exception
            log.ScriviLog("WGDevTools.CatturaSchermataAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Function AnteprimaAsync() As Task(Of Byte())
        Try
            If _pagina Is Nothing Then Return Nothing
            If Not _pagina.SuThreadInterfaccia() Then Return Await _pagina.EseguiSuInterfacciaAsync(Function() AnteprimaAsync())
            Dim core As CoreWebView2 = _pagina.Core
            If core Is Nothing Then Return Nothing
            Using flusso As New MemoryStream()
                Await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, flusso)
                If flusso.Length = 0 Then Return Nothing
                Return flusso.ToArray()
            End Using
        Catch ex As Exception
            log.ScriviLog("WGDevTools.AnteprimaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function ImpostaUserAgentAsync(parametri As JObject) As Task
        Try
            _parametriUserAgent = parametri
            If parametri Is Nothing OrElse Not ECollegato Then Return
            Await InviaAsync("Emulation.setUserAgentOverride", CType(parametri.DeepClone(), JObject), "", 3000)
            For Each info As SessioneFrame In sessioni.Values.ToList()
                Await InviaAsync("Emulation.setUserAgentOverride", CType(parametri.DeepClone(), JObject), info.IdSessione, 3000)
            Next
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ImpostaUserAgentAsync: " & ex.Message)
        End Try
    End Function

    Public Sub ImpostaFocus(attivo As Boolean)
        Try
            _focusEmulato = attivo
            If Not ECollegato Then Return
            Dim compito As Task = InviaFocusAsync(attivo)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ImpostaFocus: " & ex.Message)
        End Try
    End Sub

    Private Async Function InviaFocusAsync(attivo As Boolean) As Task
        Try
            Dim parametri As New JObject()
            parametri("enabled") = attivo
            Await InviaAsync("Emulation.setFocusEmulationEnabled", parametri, "", 2000)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.InviaFocusAsync: " & ex.Message)
        End Try
    End Function

    Public Async Function InserisciTestoAsync(sessione As String, testo As String) As Task(Of Boolean)
        Try
            If String.IsNullOrEmpty(testo) Then Return False
            Dim parametri As New JObject()
            parametri("text") = testo
            Dim risultato As JObject = Await InviaAsync("Input.insertText", parametri, If(sessione, ""), 5000)
            Return risultato IsNot Nothing
        Catch ex As Exception
            log.ScriviLog("WGDevTools.InserisciTestoAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub DimenticaFrame()
        Try
            For Each idSessione As String In sessioni.Keys.ToList()
                Dim rimosso As Boolean
                sessioniPreparate.TryRemove(idSessione, rimosso)
                sessioniConSnapshot.TryRemove(idSessione, rimosso)
            Next
            sessioni.Clear()
        Catch ex As Exception
            log.ScriviLog("WGDevTools.DimenticaFrame: " & ex.Message)
        End Try
    End Sub

    Private Shared Function AnalizzaParametri(testo As String) As JObject
        Try
            If String.IsNullOrEmpty(testo) Then Return New JObject()
            Return AnalizzaOggetto(testo)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGDevTools.AnalizzaParametri: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub EventoProtocollo(metodo As String, argomenti As CoreWebView2DevToolsProtocolEventReceivedEventArgs)
        Try
            If _chiuso OrElse argomenti Is Nothing Then Return
            If Not String.Equals(metodo, "Network.responseReceivedExtraInfo", StringComparison.Ordinal) Then WGTracciaMotore.Scrivi("evento cdp " & metodo)
            Dim testo As String = If(argomenti.ParameterObjectAsJson, "")
            Dim sessione As String = ""
            Try
                sessione = If(argomenti.SessionId, "")
            Catch exSessione As Exception
                log.ScriviLog("WGDevTools.EventoProtocollo sessione: " & exSessione.Message)
            End Try
            Dim elabora As Action = Sub() ElaboraEvento(metodo, testo, sessione)
            If _pagina Is Nothing OrElse Not _pagina.Posticipa(elabora) Then elabora()
        Catch ex As Exception
            log.ScriviLog("WGDevTools.EventoProtocollo " & metodo & ": " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraEvento(metodo As String, testo As String, sessione As String)
        Try
            If _chiuso Then Return
            Dim parametri As JObject = AnalizzaParametri(testo)
            Select Case metodo
                Case "Target.attachedToTarget"
                    SessioneCollegata(parametri, sessione)
                Case "Target.detachedFromTarget"
                    SessioneScollegata(parametri)
                Case Else
                    Try
                        RaiseEvent EventoRicevuto(metodo, parametri, sessione)
                    Catch exEvento As Exception
                        log.ScriviLog("WGDevTools.EventoRicevuto " & metodo & ": " & exEvento.Message)
                    End Try
            End Select
        Catch ex As Exception
            log.ScriviLog("WGDevTools.ElaboraEvento " & metodo & ": " & ex.Message)
        End Try
    End Sub

    Private Sub SessioneCollegata(parametri As JObject, sessionePadre As String)
        Try
            If parametri Is Nothing Then Return
            Dim infoBersaglio As JObject = TryCast(parametri("targetInfo"), JObject)
            Dim idSessione As String = parametri.Value(Of String)("sessionId")
            If String.IsNullOrEmpty(idSessione) Then Return
            If infoBersaglio IsNot Nothing AndAlso infoBersaglio.Value(Of String)("type") = "iframe" Then
                Dim info As New SessioneFrame With {
                    .IdSessione = idSessione,
                    .IdFrame = If(infoBersaglio.Value(Of String)("targetId"), ""),
                    .Url = If(infoBersaglio.Value(Of String)("url"), ""),
                    .SessionePadre = If(sessionePadre, "")
                }
                sessioni(idSessione) = info
                Dim compito As Task = PreparaFrameAsync(idSessione)
            ElseIf parametri.Value(Of Boolean?)("waitingForDebugger").GetValueOrDefault() Then
                InviaSenzaAttesa("Runtime.runIfWaitingForDebugger", Nothing, idSessione)
            End If
        Catch ex As Exception
            log.ScriviLog("WGDevTools.SessioneCollegata: " & ex.Message)
        End Try
    End Sub

    Private Async Function PreparaFrameAsync(idSessione As String) As Task
        Try
            Await PreparaSessioneAsync(idSessione)
            Await AttivaCollegamentoAutomaticoAsync(idSessione)
            If _parametriUserAgent IsNot Nothing Then Await InviaAsync("Emulation.setUserAgentOverride", CType(_parametriUserAgent.DeepClone(), JObject), idSessione, 3000)
            Await InviaAsync("Runtime.runIfWaitingForDebugger", Nothing, idSessione, 2000)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.PreparaFrameAsync: " & ex.Message)
        End Try
    End Function

    Private Sub SessioneScollegata(parametri As JObject)
        Try
            If parametri Is Nothing Then Return
            Dim idSessione As String = parametri.Value(Of String)("sessionId")
            If String.IsNullOrEmpty(idSessione) Then Return
            Dim rimossa As SessioneFrame = Nothing
            sessioni.TryRemove(idSessione, rimossa)
            Dim rimosso As Boolean
            sessioniPreparate.TryRemove(idSessione, rimosso)
            sessioniConSnapshot.TryRemove(idSessione, rimosso)
        Catch ex As Exception
            log.ScriviLog("WGDevTools.SessioneScollegata: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _chiuso = True
            collegato = False
            RimuoviRicevitori()
            sessioni.Clear()
            sessioniPreparate.Clear()
            sessioniConSnapshot.Clear()
        Catch ex As Exception
            log.ScriviLog("WGDevTools.Dispose: " & ex.Message)
        End Try
    End Sub
End Class