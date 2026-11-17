Imports System.Collections.Generic
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Public Class WGPaginaWeb
    Private ReadOnly log As New Logger()
    Private ReadOnly _controllo As Control
    Private ReadOnly _lockFrame As New Object()
    Private ReadOnly _frame As New List(Of CoreWebView2Frame)()
    Private _controller As CoreWebView2Controller = Nothing
    Private _core As CoreWebView2 = Nothing
    Private _chiusa As Boolean = False
    Private _visibile As Boolean = False

    Public Sub New(controllo As Control)
        _controllo = controllo
    End Sub

    Public ReadOnly Property ControlloInterfaccia As Control
        Get
            Return _controllo
        End Get
    End Property

    Public ReadOnly Property Controller As CoreWebView2Controller
        Get
            If _chiusa Then Return Nothing
            Return _controller
        End Get
    End Property

    Public ReadOnly Property Core As CoreWebView2
        Get
            If _chiusa Then Return Nothing
            Return _core
        End Get
    End Property

    Public ReadOnly Property Pronta As Boolean
        Get
            Return Not _chiusa AndAlso _core IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property IsDisposed As Boolean
        Get
            Return _chiusa
        End Get
    End Property

    Public ReadOnly Property Visibile As Boolean
        Get
            Return _visibile
        End Get
    End Property

    Friend Sub Imposta(nuovoController As CoreWebView2Controller)
        Try
            If nuovoController Is Nothing Then Return
            _controller = nuovoController
            _core = nuovoController.CoreWebView2
            ApplicaVista()
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.Imposta: " & ex.ToString())
        End Try
    End Sub

    Private Sub ApplicaVista()
        Dim c As CoreWebView2Controller = _controller
        If c Is Nothing Then Return
        Try
            c.BoundsMode = CoreWebView2BoundsMode.UseRawPixels
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista modalità: " & ex.Message)
        End Try
        Try
            c.ShouldDetectMonitorScaleChanges = False
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista scala del monitor: " & ex.Message)
        End Try
        Try
            c.RasterizationScale = 1.0
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista scala: " & ex.Message)
        End Try
        Try
            c.DefaultBackgroundColor = Color.White
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista sfondo: " & ex.Message)
        End Try
        Try
            c.Bounds = New Rectangle(0, 0, WGMotoreBrowser.LarghezzaVista, WGMotoreBrowser.AltezzaVista)
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista dimensioni: " & ex.Message)
        End Try
        Try
            c.IsVisible = _visibile
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ApplicaVista visibilità: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaVisibile(valore As Boolean)
        Try
            _visibile = valore
            If Not SuThreadInterfaccia() Then
                SuInterfaccia(Sub() ImpostaVisibile(valore))
                Return
            End If
            Dim c As CoreWebView2Controller = Controller
            If c Is Nothing Then Return
            If c.IsVisible <> valore Then c.IsVisible = valore
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ImpostaVisibile: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaFinestraPadre(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero Then Return
            Dim c As CoreWebView2Controller = Controller
            If c Is Nothing Then Return
            If c.ParentWindow <> finestra Then c.ParentWindow = finestra
            c.NotifyParentWindowPositionChanged()
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.AggiornaFinestraPadre: " & ex.Message)
        End Try
    End Sub

    Public Property Zoom As Double
        Get
            Try
                Dim c As CoreWebView2Controller = Controller
                If c Is Nothing Then Return 1.0
                Return c.ZoomFactor
            Catch ex As Exception
                log.ScriviLog("WGPaginaWeb.Zoom lettura: " & ex.Message)
                Return 1.0
            End Try
        End Get
        Set(value As Double)
            Try
                Dim c As CoreWebView2Controller = Controller
                If c Is Nothing Then Return
                c.ZoomFactor = Math.Max(0.25, Math.Min(5.0, value))
            Catch ex As Exception
                log.ScriviLog("WGPaginaWeb.Zoom scrittura: " & ex.Message)
            End Try
        End Set
    End Property

    Friend Sub AggiungiFrame(frame As CoreWebView2Frame)
        Try
            If frame Is Nothing Then Return
            SyncLock _lockFrame
                If Not _frame.Contains(frame) Then _frame.Add(frame)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.AggiungiFrame: " & ex.Message)
        End Try
    End Sub

    Friend Sub RimuoviFrame(frame As CoreWebView2Frame)
        Try
            If frame Is Nothing Then Return
            SyncLock _lockFrame
                _frame.Remove(frame)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.RimuoviFrame: " & ex.Message)
        End Try
    End Sub

    Friend Function FrameAttivi() As List(Of CoreWebView2Frame)
        Try
            SyncLock _lockFrame
                Return _frame.ToList()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.FrameAttivi: " & ex.Message)
            Return New List(Of CoreWebView2Frame)()
        End Try
    End Function

    Friend Sub DimenticaFrame()
        Try
            SyncLock _lockFrame
                _frame.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.DimenticaFrame: " & ex.Message)
        End Try
    End Sub

    Public Function SuThreadInterfaccia() As Boolean
        Try
            If _controllo Is Nothing OrElse _controllo.IsDisposed OrElse Not _controllo.IsHandleCreated Then Return WGMotoreBrowser.SuThreadInterfaccia
            Return Not _controllo.InvokeRequired
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.SuThreadInterfaccia: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SuInterfaccia(azione As Action)
        Try
            If azione Is Nothing Then Return
            If _controllo Is Nothing OrElse _controllo.IsDisposed OrElse Not _controllo.IsHandleCreated Then
                If WGMotoreBrowser.SuThreadInterfaccia Then azione()
                Return
            End If
            If _controllo.InvokeRequired Then
                _controllo.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As ObjectDisposedException
            log.ScriviLog("WGPaginaWeb.SuInterfaccia chiuso: " & ex.Message)
        Catch ex As InvalidOperationException
            log.ScriviLog("WGPaginaWeb.SuInterfaccia non valido: " & ex.Message)
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.SuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Public Function Posticipa(azione As Action) As Boolean
        Try
            If azione Is Nothing OrElse _chiusa Then Return False
            If _controllo Is Nothing OrElse _controllo.IsDisposed OrElse Not _controllo.IsHandleCreated Then Return False
            _controllo.BeginInvoke(azione)
            Return True
        Catch ex As ObjectDisposedException
            log.ScriviLog("WGPaginaWeb.Posticipa chiuso: " & ex.Message)
            Return False
        Catch ex As InvalidOperationException
            log.ScriviLog("WGPaginaWeb.Posticipa non valido: " & ex.Message)
            Return False
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.Posticipa: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function EseguiSuInterfacciaAsync(Of T)(funzione As Func(Of Task(Of T))) As Task(Of T)
        Try
            If funzione Is Nothing Then Return Task.FromResult(Of T)(Nothing)
            If SuThreadInterfaccia() Then Return funzione()
            If _controllo Is Nothing OrElse _controllo.IsDisposed OrElse Not _controllo.IsHandleCreated Then Return Task.FromResult(Of T)(Nothing)
            Dim attesa As New TaskCompletionSource(Of T)(TaskCreationOptions.RunContinuationsAsynchronously)
            _controllo.BeginInvoke(New Action(Async Sub()
                                                   Try
                                                       Dim risultato As T = Await funzione()
                                                       attesa.TrySetResult(risultato)
                                                   Catch exFunzione As Exception
                                                       log.ScriviLog("WGPaginaWeb.EseguiSuInterfacciaAsync funzione: " & exFunzione.Message)
                                                       attesa.TrySetResult(Nothing)
                                                   End Try
                                               End Sub))
            Return attesa.Task
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.EseguiSuInterfacciaAsync: " & ex.Message)
            Return Task.FromResult(Of T)(Nothing)
        End Try
    End Function

    Friend Shared Sub OsservaCompito(compito As Task, origine As String)
        Try
            If compito Is Nothing Then Return
            compito.ContinueWith(Sub(t As Task)
                                     Try
                                         If t.Exception IsNot Nothing Then
                                             Dim registro As New Logger()
                                             registro.ScriviLog("WGPaginaWeb." & origine & ": " & t.Exception.GetBaseException().Message)
                                         End If
                                     Catch exOsserva As Exception
                                         Dim registroOsserva As New Logger()
                                         registroOsserva.ScriviLog("WGPaginaWeb.OsservaCompito: " & exOsserva.Message)
                                     End Try
                                 End Sub, TaskContinuationOptions.OnlyOnFaulted)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGPaginaWeb.OsservaCompito: " & ex.Message)
        End Try
    End Sub

    Public Sub EseguiScript(script As String)
        Try
            If String.IsNullOrEmpty(script) Then Return
            If Not SuThreadInterfaccia() Then
                SuInterfaccia(Sub() EseguiScript(script))
                Return
            End If
            Dim c As CoreWebView2 = Core
            If c Is Nothing Then Return
            WGTracciaMotore.Scrivi("script principale " & script.Length.ToString())
            OsservaCompito(c.ExecuteScriptAsync(script), "EseguiScript")
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.EseguiScript: " & ex.Message)
        End Try
    End Sub

    Public Sub EseguiScriptNelFrame(frame As CoreWebView2Frame, script As String)
        Try
            If frame Is Nothing OrElse String.IsNullOrEmpty(script) OrElse _chiusa Then Return
            If Not SuThreadInterfaccia() Then
                SuInterfaccia(Sub() EseguiScriptNelFrame(frame, script))
                Return
            End If
            If frame.IsDestroyed() <> 0 Then
                RimuoviFrame(frame)
                Return
            End If
            WGTracciaMotore.Scrivi("script cornice " & script.Length.ToString())
            OsservaCompito(frame.ExecuteScriptAsync(script), "EseguiScriptNelFrame")
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.EseguiScriptNelFrame: " & ex.Message)
            RimuoviFrame(frame)
        End Try
    End Sub

    Public Sub EseguiScriptInTuttiIFrame(script As String)
        Try
            If String.IsNullOrEmpty(script) Then Return
            If Not SuThreadInterfaccia() Then
                SuInterfaccia(Sub() EseguiScriptInTuttiIFrame(script))
                Return
            End If
            EseguiScript(script)
            For Each frame As CoreWebView2Frame In FrameAttivi()
                EseguiScriptNelFrame(frame, script)
            Next
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.EseguiScriptInTuttiIFrame: " & ex.Message)
        End Try
    End Sub

    Public Async Function ValutaJsonAsync(script As String, Optional millisecondi As Integer = 4000) As Task(Of String)
        Try
            If String.IsNullOrEmpty(script) Then Return Nothing
            If Not SuThreadInterfaccia() Then Return Await EseguiSuInterfacciaAsync(Function() ValutaJsonAsync(script, millisecondi))
            Dim c As CoreWebView2 = Core
            If c Is Nothing Then Return Nothing
            WGTracciaMotore.Scrivi("valuta " & script.Length.ToString())
            Dim compito As Task(Of String) = c.ExecuteScriptAsync(script)
            Dim finito As Task = Await Task.WhenAny(compito, Task.Delay(Math.Max(100, millisecondi)))
            If finito IsNot compito Then
                OsservaCompito(compito, "ValutaJsonAsync")
                Return Nothing
            End If
            Return Await compito
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ValutaJsonAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function ValutaInTuttiIFrameAsync(script As String, Optional millisecondi As Integer = 4000) As Task(Of List(Of String))
        Dim risultati As New List(Of String)()
        Try
            If String.IsNullOrEmpty(script) OrElse _chiusa Then Return risultati
            If Not SuThreadInterfaccia() Then
                Dim daInterfaccia As List(Of String) = Await EseguiSuInterfacciaAsync(Function() ValutaInTuttiIFrameAsync(script, millisecondi))
                Return If(daInterfaccia, risultati)
            End If
            Dim compiti As New List(Of Task(Of String))()
            Dim c As CoreWebView2 = Core
            WGTracciaMotore.Scrivi("valuta in tutte le cornici " & script.Length.ToString())
            If c IsNot Nothing Then compiti.Add(c.ExecuteScriptAsync(script))
            For Each frame As CoreWebView2Frame In FrameAttivi()
                Try
                    If frame.IsDestroyed() <> 0 Then
                        RimuoviFrame(frame)
                        Continue For
                    End If
                    compiti.Add(frame.ExecuteScriptAsync(script))
                Catch exFrame As Exception
                    log.ScriviLog("WGPaginaWeb.ValutaInTuttiIFrameAsync frame: " & exFrame.Message)
                    RimuoviFrame(frame)
                End Try
            Next
            If compiti.Count = 0 Then Return risultati
            For Each compito As Task(Of String) In compiti
                OsservaCompito(compito, "ValutaInTuttiIFrameAsync")
            Next
            Await Task.WhenAny(Task.WhenAll(compiti), Task.Delay(Math.Max(100, millisecondi)))
            For Each compito As Task(Of String) In compiti
                If compito.Status = TaskStatus.RanToCompletion Then risultati.Add(If(compito.Result, ""))
            Next
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ValutaInTuttiIFrameAsync: " & ex.Message)
        End Try
        Return risultati
    End Function

    Public Async Function ValutaAsync(script As String, Optional millisecondi As Integer = 4000) As Task(Of String)
        Try
            Dim json As String = Await ValutaJsonAsync(script, millisecondi)
            Return RisultatoComeTesto(json)
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.ValutaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Function AnalizzaJson(json As String) As JToken
        Try
            If String.IsNullOrEmpty(json) Then Return Nothing
            Using lettore As New StringReader(json)
                Using lettoreJson As New JsonTextReader(lettore)
                    lettoreJson.DateParseHandling = DateParseHandling.None
                    lettoreJson.FloatParseHandling = FloatParseHandling.Double
                    lettoreJson.MaxDepth = Nothing
                    Return JToken.Load(lettoreJson)
                End Using
            End Using
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGPaginaWeb.AnalizzaJson: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Function RisultatoComeTesto(json As String) As String
        Try
            If String.IsNullOrEmpty(json) OrElse json = "null" OrElse json = "undefined" Then Return Nothing
            Dim valore As JToken = AnalizzaJson(json)
            If valore Is Nothing Then Return Nothing
            Select Case valore.Type
                Case JTokenType.String
                    Return valore.Value(Of String)()
                Case JTokenType.Boolean
                    Return If(valore.Value(Of Boolean)(), "True", "False")
                Case JTokenType.Integer
                    Return Convert.ToString(valore.Value(Of Long)(), CultureInfo.InvariantCulture)
                Case JTokenType.Float
                    Return Convert.ToString(valore.Value(Of Double)(), CultureInfo.InvariantCulture)
                Case JTokenType.Null, JTokenType.Undefined
                    Return Nothing
                Case Else
                    Return valore.ToString(Formatting.None)
            End Select
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGPaginaWeb.RisultatoComeTesto: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub Chiudi()
        Try
            If _chiusa Then Return
            _chiusa = True
            Dim vecchio As CoreWebView2Controller = _controller
            _controller = Nothing
            _core = Nothing
            DimenticaFrame()
            If vecchio Is Nothing Then Return
            If SuThreadInterfaccia() Then
                ChiudiController(vecchio)
            Else
                SuInterfaccia(Sub() ChiudiController(vecchio))
            End If
        Catch ex As Exception
            log.ScriviLog("WGPaginaWeb.Chiudi: " & ex.Message)
        End Try
    End Sub

    Friend Shared Sub ChiudiController(controllerDaChiudere As CoreWebView2Controller)
        Try
            If controllerDaChiudere Is Nothing Then Return
            Try
                controllerDaChiudere.IsVisible = False
            Catch exVisibile As Exception
                Dim registroVisibile As New Logger()
                registroVisibile.ScriviLog("WGPaginaWeb.ChiudiController visibilità: " & exVisibile.Message)
            End Try
            WGTracciaMotore.Scrivi("chiusura controller")
            Try
                controllerDaChiudere.Close()
            Finally
                WGMotoreBrowser.ControllerChiuso()
            End Try
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGPaginaWeb.ChiudiController: " & ex.Message)
        End Try
    End Sub
End Class