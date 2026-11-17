Imports System.Collections.Generic
Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports wgcontrols

Partial Public Class WGContenitore
    Inherits Form

    Private Shared ReadOnly _lockIstanza As New Object()
    Private Shared _istanza As WGContenitore
    Private Shared ReadOnly _logShared As New Logger()

    Private ReadOnly _log As New Logger()
    Private ReadOnly _lockStack As New Object()
    Private Shared _guardiaFocusSospesa As Boolean = False
    Private Shared ReadOnly _lockGuardiaFocus As New Object()
    Private ReadOnly _stackControlli As New List(Of Control)()
    Private ReadOnly _atteseAttive As New List(Of Control)()
    Private ReadOnly _controlliMostratiDuranteAttesa As New List(Of Control)()
    Private ReadOnly _applicazioneAttesa As New Dictionary(Of Control, Integer)()
    Private _cambioDuranteAttesa As Boolean = False
    Private ReadOnly _controlliInAttesaDiMessaggio As New List(Of Control)()
    Private ReadOnly _controlliMessaggio As New List(Of Control)()
    Private _tmrRivelazione As System.Windows.Forms.Timer
    Private _sintesiVerificata As Boolean = False
    Private _attesaSintesiInCorso As Boolean = False
    Private _vistaAttesa As VistaAttesa
    Private _tmrGuardiaFocus As System.Windows.Forms.Timer
    Private _tmrImpediscaStandby As System.Windows.Forms.Timer
    Private _suoni As WGSuoni
    Private _lettura As sintesivocale
    Private WithEvents _tastiera As WGTastiera
    Private _suonoAttesaAttivo As Boolean = False
    Private ReadOnly _lockSuono As New Object()
    Private _letturaAttesaCts As CancellationTokenSource
    Private ReadOnly _lockLetturaAttesa As New Object()
    Private _letturaAttesaVuotaCts As CancellationTokenSource
    Private ReadOnly _lockLetturaAttesaVuota As New Object()
    Private Const RITARDO_INIZIALE_LETTURA_ATTESA_VUOTA_MS As Integer = 500
    Private Const INTERVALLO_LETTURA_ATTESA_VUOTA_MS As Integer = 5000

    Private _hwndPannelloVideo As IntPtr = IntPtr.Zero
    Private _hwndSottotitolo As IntPtr = IntPtr.Zero
    Private _brushSfondoPannello As IntPtr = IntPtr.Zero
    Private _pannelloVideoVisibile As Boolean = False
    Private _timerPuliziaSottotitolo As System.Windows.Forms.Timer
    Private Const LARGHEZZA_PANNELLO_VIDEO As Integer = 480
    Private Const ALTEZZA_ETICHETTA_SOTTOTITOLO As Integer = 60
    Private Const SECONDI_PULIZIA_SOTTOTITOLO As Integer = 12

    Private _hwndTestoAssistente As IntPtr = IntPtr.Zero
    Private _testoAssistenteVisibile As Boolean = False
    Private _fontTestoAssistente As Font
    Private _timerTestoAssistente As System.Windows.Forms.Timer
    Private Const SECONDI_TESTO_ASSISTENTE As Integer = 120
    Private Const MARGINE_TESTO_ASSISTENTE As Integer = 12
    Private Const WM_SETFONT As Integer = &H30

    Private _hwndBarraProgresso As IntPtr = IntPtr.Zero
    Private _barraProgressoVisibile As Boolean = False
    Private Const ALTEZZA_BARRA_PROGRESSO As Integer = 36
    Private Const LARGHEZZA_BARRA_PROGRESSO_PROPORZIONE As Double = 0.5
    Private Const POSIZIONE_VERTICALE_BARRA_PROGRESSO As Double = 0.72

    Private Const WS_CHILD As Integer = &H40000000
    Private Const WS_VISIBLE As Integer = &H10000000
    Private Const WS_DISABLED As Integer = &H8000000
    Private Const SS_LEFT As Integer = &H0
    Private Const SS_NOPREFIX As Integer = &H80
    Private Const SW_SHOW As Integer = 5
    Private Const SW_HIDE As Integer = 0
    Private Const WM_CTLCOLORSTATIC As Integer = &H138
    Private Const WM_ACTIVATEAPP As Integer = &H1C
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const WM_SYSKEYUP As Integer = &H105
    Private Const WM_SYSCOMMAND As Integer = &H112
    Private Const SC_KEYMENU As Long = &HF100L
    Private Const VK_MENU As Integer = &H12
    Private Const VK_F10 As Integer = &H79
    Private Const TRANSPARENT_BKMODE As Integer = 1
    Private Const SWP_NOACTIVATE As UInteger = &H10
    Private Const SWP_NOZORDER As UInteger = &H4
    Private Shared ReadOnly HWND_TOP As IntPtr = IntPtr.Zero

    Private Const PBS_SMOOTH As Integer = &H1
    Private Const PBM_SETRANGE32 As Integer = &H406
    Private Const PBM_SETPOS As Integer = &H402

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function CreateWindowEx(dwExStyle As Integer, lpClassName As String, lpWindowName As String, dwStyle As Integer, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, hWndParent As IntPtr, hMenu As IntPtr, hInstance As IntPtr, lpParam As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DestroyWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function MoveWindow(hWnd As IntPtr, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, bRepaint As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SetWindowText(hWnd As IntPtr, lpString As String) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function InvalidateRect(hWnd As IntPtr, lpRect As IntPtr, bErase As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetWindowPos(hWnd As IntPtr, hWndInsertAfter As IntPtr, x As Integer, y As Integer, cx As Integer, cy As Integer, uFlags As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateSolidBrush(crColor As Integer) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentProcessId() As UInteger
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function SetThreadExecutionState(esFlags As UInteger) As UInteger
    End Function

    Private Const ES_CONTINUOUS As UInteger = &H80000000UI
    Private Const ES_SYSTEM_REQUIRED As UInteger = &H1UI
    Private Const ES_DISPLAY_REQUIRED As UInteger = &H2UI

    <DllImport("user32.dll")>
    Private Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AllowSetForegroundWindow(processId As UInteger) As Boolean
    End Function

    Private Const ASFW_ANY As UInteger = &HFFFFFFFFUI

    Public Shared ReadOnly Property Istanza As WGContenitore
        Get
            Try
                SyncLock _lockIstanza
                    If _istanza IsNot Nothing AndAlso (_istanza.IsDisposed OrElse _istanza.Disposing) Then Return Nothing
                    Return _istanza
                End SyncLock
            Catch ex As Exception
                _logShared.ScriviLog("Istanza: " & ex.Message)
                Return Nothing
            End Try
        End Get
    End Property

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
            Me.FormBorderStyle = FormBorderStyle.None
            Me.WindowState = FormWindowState.Maximized
            Me.BackColor = WGTema.Sfondo
            Me.ForeColor = WGTema.Testo
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            Me.DoubleBuffered = True
            Me.KeyPreview = False
            Me.ShowInTaskbar = True
            Me.Text = "WinGabriel"
            Try
                Dim percorsoEseguibile As String = Nothing
                Dim assemblyAvvio As System.Reflection.Assembly = System.Reflection.Assembly.GetEntryAssembly()
                If assemblyAvvio IsNot Nothing Then percorsoEseguibile = assemblyAvvio.Location
                If Not String.IsNullOrEmpty(percorsoEseguibile) Then
                    Me.Icon = System.Drawing.Icon.ExtractAssociatedIcon(percorsoEseguibile)
                End If
            Catch exIcona As Exception
                _log.ScriviLog("New (icona): " & exIcona.Message)
            End Try
            Me.AccessibleRole = AccessibleRole.Client
            Me.AccessibleName = " "

            Try
                SegnalaRipresaManualeDalServizio()
            Catch exResetServizio As Exception
                _log.ScriviLog("New (reset sospensione servizio): " & exResetServizio.Message)
            End Try

            Try
                WGTema.ApplicaAFinestra(Me)
            Catch exTema As Exception
                _log.ScriviLog("New (tema): " & exTema.Message)
            End Try

            _lettura = sintesivocale.GetInstance()
            _tastiera = WGTastiera.GetInstance()

            Try
                If _tastiera IsNot Nothing Then
                    _tastiera.RegistraFormPrincipale(Me)
                End If
            Catch exRegistra As Exception
                _log.ScriviLog("New (RegistraFormPrincipale): " & exRegistra.Message)
            End Try

            Try
                If _tastiera IsNot Nothing Then
                    Dim cfgMouse As New WGConfig()
                    Dim valMouse As String = cfgMouse.LeggiValore("attivamous")
                    If String.IsNullOrEmpty(valMouse) OrElse String.Equals(valMouse, "true", StringComparison.OrdinalIgnoreCase) Then
                        _tastiera.AttivaBloccoMouse()
                    End If
                End If
            Catch exMouse As Exception
                _log.ScriviLog("New (AttivaBloccoMouse): " & exMouse.Message)
            End Try

            _vistaAttesa = New VistaAttesa()
            _vistaAttesa.Dock = DockStyle.Fill
            _vistaAttesa.Visible = True
            Me.Controls.Add(_vistaAttesa)

            _tmrGuardiaFocus = New System.Windows.Forms.Timer()
            _tmrGuardiaFocus.Interval = 500
            AddHandler _tmrGuardiaFocus.Tick, AddressOf GuardiaFocusTick
            _tmrGuardiaFocus.Start()

            Try
                ImpediscaStandbyTick(Nothing, EventArgs.Empty)
                _tmrImpediscaStandby = New System.Windows.Forms.Timer()
                _tmrImpediscaStandby.Interval = 30000
                AddHandler _tmrImpediscaStandby.Tick, AddressOf ImpediscaStandbyTick
                _tmrImpediscaStandby.Start()
            Catch exStandby As Exception
                _log.ScriviLog("New (timer anti-standby): " & exStandby.Message)
            End Try

            _tmrRivelazione = New System.Windows.Forms.Timer()
            _tmrRivelazione.Interval = 300
            AddHandler _tmrRivelazione.Tick, AddressOf RivelazioneTick

            Try
                _suoni = New WGSuoni()
            Catch exSuoni As Exception
                _log.ScriviLog("New (WGSuoni): " & exSuoni.Message)
            End Try

            SyncLock _lockIstanza
                _istanza = Me
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("New: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
            SyncLock _lockIstanza
                _istanza = Me
            End SyncLock
            CreaPannelloVideo()
            CreaBarraProgresso()
            AggiornaVistaAttesa()

            ConfiguraTouch()
            WGTema.ApplicaAFinestra(Me)
        Catch ex As Exception
            _log.ScriviLog("OnHandleCreated: " & ex.Message)
        End Try
    End Sub

    ' Windows ha cambiato tema chiaro/scuro mentre il programma era aperto: si segue.
    Private Sub OnCambiatoTema(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(Sub() OnCambiatoTema(sender, e)))
                Return
            End If
            Me.BackColor = WGTema.Sfondo
            Me.ForeColor = WGTema.Testo
            WGTema.ApplicaAFinestra(Me)
            Me.Invalidate(True)
        Catch ex As Exception
            _log.ScriviLog("OnCambiatoTema: " & ex.Message)
        End Try
    End Sub

    Private Const ID_CONFIG_TOUCH_ATTIVO As String = "touch_attivo"
    Private Const ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO As String = "touch_adatta_dispositivo"
    Private Const ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO As String = "touch_orientamento_bloccato"
    Private Const MASSIMA_DISCESA_CONTROLLI As Integer = 32

    Private Sub ConfiguraTouch()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing Then Exit Sub

            gestoreTouch.AdattaAlDispositivo = AdattamentoTouchAlDispositivoAttivo()
            gestoreTouch.OrientamentoBloccato = OrientamentoTouchBloccatoAttivo()
            gestoreTouch.IdentificaElemento = AddressOf IdentificativoElementoSottoIlDito

            RemoveHandler gestoreTouch.OrientamentoCambiato, AddressOf OnOrientamentoCambiato
            AddHandler gestoreTouch.OrientamentoCambiato, AddressOf OnOrientamentoCambiato

            Dim cfgTouch As New WGConfig()
            Dim valTouch As String = cfgTouch.LeggiValore(ID_CONFIG_TOUCH_ATTIVO)
            If Not String.Equals(valTouch, "true", StringComparison.OrdinalIgnoreCase) Then Exit Sub

            If gestoreTouch.TouchAttivo Then gestoreTouch.Disattiva()
            gestoreTouch.Attiva(Me.Handle)
        Catch ex As Exception
            _log.ScriviLog("ConfiguraTouch: " & ex.Message)
        End Try
    End Sub

    Private _ultimoLayoutOrizzontale As Integer = -1

    Private Sub AnnunciaCambioOrientamento()
        Try
            Dim larghezza As Integer = Me.ClientSize.Width
            Dim altezza As Integer = Me.ClientSize.Height
            If larghezza <= 0 OrElse altezza <= 0 Then Return

            Dim attuale As Integer = If(larghezza >= altezza, 1, 0)
            Dim precedente As Integer = _ultimoLayoutOrizzontale
            _ultimoLayoutOrizzontale = attuale
            If precedente < 0 OrElse precedente = attuale Then Return

            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing OrElse Not gestoreTouch.TouchAttivo OrElse Not gestoreTouch.LayoutAdattivo Then Return

            If _lettura Is Nothing Then _lettura = sintesivocale.GetInstance()
            If _lettura Is Nothing Then Return
            _lettura.LeggiTesto(If(attuale = 1, "Orientamento orizzontale", "Orientamento verticale"))
        Catch ex As Exception
            _log.ScriviLog("AnnunciaCambioOrientamento: " & ex.Message)
        End Try
    End Sub

    Private Function OrientamentoTouchBloccatoAttivo() As Boolean
        Try
            Dim cfgOrientamento As New WGConfig()
            If Not cfgOrientamento.EsisteId(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO) Then Return False

            Dim valore As String = cfgOrientamento.LeggiValore(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO)
            If String.IsNullOrWhiteSpace(valore) Then Return False

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _log.ScriviLog("OrientamentoTouchBloccatoAttivo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub OnOrientamentoCambiato()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnOrientamentoCambiato))
                Return
            End If
            RiposizionaPannelloVideo()
            RidimensionaControlloCorrente()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            _log.ScriviLog("OnOrientamentoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Function AdattamentoTouchAlDispositivoAttivo() As Boolean
        Try
            Dim cfgAdattamento As New WGConfig()
            If Not cfgAdattamento.EsisteId(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO) Then Return True

            Dim valore As String = cfgAdattamento.LeggiValore(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO)
            If String.IsNullOrWhiteSpace(valore) Then Return True

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _log.ScriviLog("AdattamentoTouchAlDispositivoAttivo: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function IdentificativoElementoSottoIlDito(puntoSchermo As Point) As String
        Try
            Dim controllo As Control = ControlloSottoIlPunto(puntoSchermo)
            If controllo Is Nothing Then Return Nothing

            Dim tattile As IWGElementoTattile = TryCast(controllo, IWGElementoTattile)
            If tattile Is Nothing Then Return Nothing

            Return tattile.IdentificativoElementoTattile(puntoSchermo)
        Catch ex As Exception
            _log.ScriviLog("IdentificativoElementoSottoIlDito: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function FinestraSottoIlPunto(puntoSchermo As Point) As Form
        Try
            Dim attiva As Form = Form.ActiveForm
            If attiva IsNot Nothing AndAlso Not attiva.IsDisposed AndAlso attiva.IsHandleCreated Then
                If attiva.Bounds.Contains(puntoSchermo) Then Return attiva
            End If

            For Each aperta As Form In Application.OpenForms
                If aperta Is Nothing OrElse aperta.IsDisposed OrElse Not aperta.IsHandleCreated Then Continue For
                If Not aperta.Visible Then Continue For
                If aperta.Bounds.Contains(puntoSchermo) Then Return aperta
            Next

            If Not Me.IsDisposed AndAlso Me.IsHandleCreated Then Return Me
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog("FinestraSottoIlPunto: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ControlloSottoIlPunto(puntoSchermo As Point) As Control
        Try
            Dim corrente As Control = FinestraSottoIlPunto(puntoSchermo)
            If corrente Is Nothing Then Return Nothing

            Dim discesa As Integer = 0
            Do While discesa < MASSIMA_DISCESA_CONTROLLI
                discesa += 1
                If Not corrente.IsHandleCreated Then Exit Do
                Dim figlio As Control = corrente.GetChildAtPoint(corrente.PointToClient(puntoSchermo), GetChildAtPointSkip.Invisible)
                If figlio Is Nothing OrElse figlio.IsDisposed Then Exit Do
                corrente = figlio
            Loop

            Return corrente
        Catch ex As Exception
            _log.ScriviLog("ControlloSottoIlPunto: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        Try
            MyBase.OnSizeChanged(e)
            Dim eraMinimizzata As Boolean = FinestraMinimizzata
            FinestraMinimizzata = (Me.WindowState = FormWindowState.Minimized)

            If eraMinimizzata AndAlso Not FinestraMinimizzata Then
                If GuardiaSospesaDalServizio() Then SegnalaRipresaManualeDalServizio()
                Try
                    If _tastiera IsNot Nothing Then _tastiera.NotificaRipristinoManualeUtente()
                Catch exTastieraRipristino As Exception
                    _log.ScriviLog("OnSizeChanged (NotificaRipristinoManualeUtente): " & exTastieraRipristino.Message)
                End Try
            End If
        Catch ex As Exception
            _log.ScriviLog("OnSizeChanged: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        Try
            MyBase.OnShown(e)
            If Not GuardiaFocusSospesa AndAlso Me.WindowState <> FormWindowState.Minimized Then
                Me.WindowState = FormWindowState.Maximized
            End If
            AggiornaVistaAttesa()
            ' All'apertura parla la voce di WinGabriel: gli screen reader accesi si chiudono.
            WGScreenReader.DopoAvvio()
        Catch ex As Exception
            _log.ScriviLog("OnShown: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        Try
            If Not programmadisattivato Then
                e.Cancel = True
                Return
            End If
            MyBase.OnFormClosing(e)
        Catch ex As Exception
            _log.ScriviLog("OnFormClosing: " & ex.Message)
            e.Cancel = True
        End Try
    End Sub

    Private Function EControlloAttesa(controllo As Control) As Boolean
        Try
            If controllo Is Nothing Then Return False
            Return TypeOf controllo Is WGProcessoInCorso
        Catch ex As Exception
            _log.ScriviLog("EControlloAttesa: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function IstanzaOCrea() As WGContenitore
        Try
            Dim esistente As WGContenitore = Istanza
            If esistente IsNot Nothing Then Return esistente
            Dim nuovo As New WGContenitore()
            nuovo.Show()
            Return nuovo
        Catch ex As Exception
            _logShared.ScriviLog("IstanzaOCrea: " & ex.Message)
            Return Istanza
        End Try
    End Function

    Public Sub RegistraControlloMessaggio(controllo As Control)
        Try
            If controllo Is Nothing Then Return
            SyncLock _lockStack
                _controlliMessaggio.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
                If Not _controlliMessaggio.Contains(controllo) Then _controlliMessaggio.Add(controllo)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("RegistraControlloMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function EControlloMessaggio(controllo As Control) As Boolean
        Try
            If controllo Is Nothing Then Return False
            SyncLock _lockStack
                Return _controlliMessaggio.Contains(controllo)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("EControlloMessaggio: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Il messaggio aperto più recente (Nothing se non ce ne sono): sta sempre davanti a tutto il resto.</summary>
    Private Function UltimoMessaggioAperto() As Control
        Try
            SyncLock _lockStack
                For i As Integer = _stackControlli.Count - 1 To 0 Step -1
                    Dim c As Control = _stackControlli(i)
                    If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso _controlliMessaggio.Contains(c) Then Return c
                Next
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("UltimoMessaggioAperto: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function MessaggioAperto() As Boolean
        Try
            SyncLock _lockStack
                For Each elemento As Control In _stackControlli
                    If elemento IsNot Nothing AndAlso Not elemento.IsDisposed AndAlso EControlloMessaggio(elemento) Then Return True
                Next
            End SyncLock
            Return False
        Catch ex As Exception
            _log.ScriviLog("MessaggioAperto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AccodaControlloDopoMessaggio(controllo As Control)
        Try
            SyncLock _lockStack
                If Not _controlliInAttesaDiMessaggio.Contains(controllo) Then _controlliInAttesaDiMessaggio.Add(controllo)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AccodaControlloDopoMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraControlliAccodatiSeMessaggioChiuso()
        Try
            If MessaggioAperto() Then Return
            Dim daMostrare As New List(Of Control)
            SyncLock _lockStack
                If _controlliInAttesaDiMessaggio.Count = 0 Then Return
                daMostrare.AddRange(_controlliInAttesaDiMessaggio)
                _controlliInAttesaDiMessaggio.Clear()
            End SyncLock
            For Each elemento As Control In daMostrare
                If elemento Is Nothing OrElse elemento.IsDisposed Then Continue For
                MostraControllo(elemento)
                If MessaggioAperto() Then
                    SyncLock _lockStack
                        Dim posizione As Integer = daMostrare.IndexOf(elemento) + 1
                        For indice As Integer = posizione To daMostrare.Count - 1
                            If Not _controlliInAttesaDiMessaggio.Contains(daMostrare(indice)) Then _controlliInAttesaDiMessaggio.Add(daMostrare(indice))
                        Next
                    End SyncLock
                    Exit For
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog("MostraControlliAccodatiSeMessaggioChiuso: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraControllo(controllo As Control)
        Try
            If controllo Is Nothing Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New Action(Of Control)(AddressOf MostraControllo), controllo)
                Return
            End If

            If EControlloAttesa(controllo) Then
                ' Attesa di un'applicazione riaperta in sottofondo: resta dietro, senza pannello, voce né
                ' suono, e non blocca chi è davanti.
                Dim processo As WGProcessoInCorso = TryCast(controllo, WGProcessoInCorso)
                If processo IsNot Nothing AndAlso processo.InSottofondo Then Return
                RegistraAttesa(controllo)
                Return
            End If

            If Not EControlloMessaggio(controllo) AndAlso MessaggioAperto() Then
                AccodaControlloDopoMessaggio(controllo)
                Return
            End If

            ' Finestra di un'applicazione che non è davanti (un elenco che si aggiorna, la scrivania che si
            ' ridisegna): resta dietro, senza portare davanti la sua applicazione.
            If RestaDietro(controllo) Then Return

            _tmrRivelazione.Stop()

            AttendiSintesiProntaAlPrimoAvvio()

            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If controllo.IsDisposed Then Return

            Dim formInnestato As Form = TryCast(controllo, Form)
            If formInnestato IsNot Nothing Then
                PreparaFormPerInnesto(formInnestato)
            End If

            controllo.Dock = DockStyle.None
            controllo.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right

            ' Ripresa della sessione: la finestra entra già nascosta, senza comparire nemmeno un istante.
            Dim inSottofondo As Boolean = _aperturaInSottofondo AndAlso Not EControlloMessaggio(controllo)
            ' Menu applicazioni chiuso (modalità smart): pronto dietro a tutto, nascosto e zitto.
            Dim menuChiuso As Boolean = controllo Is _menuIniziale AndAlso Not MenuApplicazioniAperto
            ' Menu attraversato per arrivare alla voce di un collegamento: entra nella pila, nella sua
            ' applicazione, ma non si vede e non si sente; davanti resta quello che c'era.
            Dim diPassaggio As Boolean = _menuDiPassaggioNascosti AndAlso Not inSottofondo AndAlso Not menuChiuso AndAlso TypeOf controllo Is WGMenuInt
            If inSottofondo OrElse menuChiuso OrElse diPassaggio Then controllo.Visible = False
            If Not Me.Controls.Contains(controllo) Then
                Me.Controls.Add(controllo)
            End If

            controllo.Bounds = New Rectangle(0, 0, LarghezzaContenutoDisponibile(), AltezzaContenutoDisponibile())

            AssegnaPercorsoMenu(controllo)
            ImparaMenuPerRicerca(controllo)
            AssegnaApplicazione(controllo)
            SyncLock _lockStack
                If _stackControlli.Contains(controllo) Then
                    _stackControlli.Remove(controllo)
                End If
                _stackControlli.Add(controllo)
                If menuChiuso Then
                    _stackControlli.Remove(controllo)
                    _stackControlli.Insert(0, controllo)
                ElseIf ApplicazioniSeparate AndAlso _menuIniziale IsNot Nothing Then
                    PortaApplicazioneInCima(ApplicazioneDi(controllo))
                End If
                If _atteseAttive.Count > 0 AndAlso Not _controlliMostratiDuranteAttesa.Contains(controllo) Then
                    _controlliMostratiDuranteAttesa.Add(controllo)
                End If
            End SyncLock

            If menuChiuso Then
                controllo.Visible = False
                Return
            End If

            ' Ripresa della sessione: resta dietro, nascosta e zitta; davanti non cambia niente.
            If inSottofondo Then
                controllo.Visible = False
                TieniInSottofondo(controllo)
                Return
            End If

            If diPassaggio Then
                controllo.Visible = False
                Return
            End If

            NascondiTutticontrolliTranne(controllo)

            controllo.Visible = True
            controllo.BringToFront()

            AggiornaVistaAttesa()
            ForzaFocusControllo(controllo)
            AssicuraPrimoPiano()
        Catch ex As Exception
            _log.ScriviLog("MostraControllo: " & ex.Message)
        End Try
    End Sub

    Private Sub CreaPannelloVideo()
        Try
            If _hwndPannelloVideo <> IntPtr.Zero Then Return
            If Not Me.IsHandleCreated Then Return

            _brushSfondoPannello = CreateSolidBrush(0)

            Dim rettangolo As Rectangle = CalcolaRettangoloPannello()

            _hwndSottotitolo = CreateWindowEx(0, "STATIC", "",
                WS_CHILD Or WS_DISABLED Or SS_LEFT Or SS_NOPREFIX,
                rettangolo.X, rettangolo.Y, rettangolo.Width, ALTEZZA_ETICHETTA_SOTTOTITOLO,
                Me.Handle, IntPtr.Zero, GetModuleHandle(Nothing), IntPtr.Zero)

            _hwndPannelloVideo = CreateWindowEx(0, "STATIC", "",
                WS_CHILD Or WS_DISABLED Or SS_LEFT,
                rettangolo.X, rettangolo.Y + ALTEZZA_ETICHETTA_SOTTOTITOLO, rettangolo.Width, rettangolo.Height - ALTEZZA_ETICHETTA_SOTTOTITOLO,
                Me.Handle, IntPtr.Zero, GetModuleHandle(Nothing), IntPtr.Zero)

            _timerPuliziaSottotitolo = New System.Windows.Forms.Timer()
            _timerPuliziaSottotitolo.Interval = SECONDI_PULIZIA_SOTTOTITOLO * 1000
            AddHandler _timerPuliziaSottotitolo.Tick, AddressOf PuliziaSottotitoloTick
        Catch ex As Exception
            _log.ScriviLog("CreaPannelloVideo: " & ex.Message)
        End Try
    End Sub

    Private Function CalcolaRettangoloPannello() As Rectangle
        Try
            If PannelloVideoInBasso() Then
                Dim altezzaBasso As Integer = AltezzaPannelloVideoInBasso()
                Dim yBasso As Integer = Me.ClientSize.Height - altezzaBasso
                If yBasso < 0 Then yBasso = 0
                Return New Rectangle(0, yBasso, Math.Max(1, Me.ClientSize.Width), altezzaBasso)
            End If

            Dim larghezza As Integer = LARGHEZZA_PANNELLO_VIDEO
            Dim x As Integer = Me.ClientSize.Width - larghezza
            If x < 0 Then x = 0
            Dim altezza As Integer = Me.ClientSize.Height
            If altezza < ALTEZZA_ETICHETTA_SOTTOTITOLO + 1 Then altezza = ALTEZZA_ETICHETTA_SOTTOTITOLO + 1
            Return New Rectangle(x, 0, larghezza, altezza)
        Catch ex As Exception
            _log.ScriviLog("CalcolaRettangoloPannello: " & ex.Message)
            Return New Rectangle(0, 0, LARGHEZZA_PANNELLO_VIDEO, 600)
        End Try
    End Function

    Private Sub RiposizionaPannelloVideo()
        Try
            If _hwndPannelloVideo = IntPtr.Zero OrElse _hwndSottotitolo = IntPtr.Zero Then Return
            Dim rettangolo As Rectangle = CalcolaRettangoloPannello()
            MoveWindow(_hwndSottotitolo, rettangolo.X, rettangolo.Y, rettangolo.Width, ALTEZZA_ETICHETTA_SOTTOTITOLO, True)
            MoveWindow(_hwndPannelloVideo, rettangolo.X, rettangolo.Y + ALTEZZA_ETICHETTA_SOTTOTITOLO, rettangolo.Width, rettangolo.Height - ALTEZZA_ETICHETTA_SOTTOTITOLO, True)
            SetWindowPos(_hwndSottotitolo, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER Or SWP_NOACTIVATE)
            SetWindowPos(_hwndPannelloVideo, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER Or SWP_NOACTIVATE)
            RidimensionaControlloCorrente()
        Catch ex As Exception
            _log.ScriviLog("RiposizionaPannelloVideo: " & ex.Message)
        End Try
    End Sub

    Private Sub CreaBarraProgresso()
        Try
            If _hwndBarraProgresso <> IntPtr.Zero Then Return
            If Not Me.IsHandleCreated Then Return

            Dim rettangolo As Rectangle = CalcolaRettangoloBarraProgresso()

            _hwndBarraProgresso = CreateWindowEx(0, "msctls_progress32", "",
                WS_CHILD Or WS_DISABLED Or PBS_SMOOTH,
                rettangolo.X, rettangolo.Y, rettangolo.Width, rettangolo.Height,
                Me.Handle, IntPtr.Zero, GetModuleHandle(Nothing), IntPtr.Zero)

            If _hwndBarraProgresso <> IntPtr.Zero Then
                SendMessage(_hwndBarraProgresso, PBM_SETRANGE32, IntPtr.Zero, New IntPtr(100))
                SendMessage(_hwndBarraProgresso, PBM_SETPOS, IntPtr.Zero, IntPtr.Zero)
            End If
        Catch ex As Exception
            _log.ScriviLog("CreaBarraProgresso: " & ex.Message)
        End Try
    End Sub

    Private Function CalcolaRettangoloBarraProgresso() As Rectangle
        Try
            Dim larghezza As Integer = CInt(Me.ClientSize.Width * LARGHEZZA_BARRA_PROGRESSO_PROPORZIONE)
            If larghezza < 200 Then larghezza = 200
            Dim x As Integer = (Me.ClientSize.Width - larghezza) \ 2
            If x < 0 Then x = 0
            Dim y As Integer = CInt(Me.ClientSize.Height * POSIZIONE_VERTICALE_BARRA_PROGRESSO)
            If y < 0 Then y = 0
            Return New Rectangle(x, y, larghezza, ALTEZZA_BARRA_PROGRESSO)
        Catch ex As Exception
            _log.ScriviLog("CalcolaRettangoloBarraProgresso: " & ex.Message)
            Return New Rectangle(100, 400, 400, ALTEZZA_BARRA_PROGRESSO)
        End Try
    End Function

    Private Sub PosizionaBarraProgresso()
        Try
            If _hwndBarraProgresso = IntPtr.Zero Then Return
            Dim rettangolo As Rectangle = CalcolaRettangoloBarraProgresso()
            MoveWindow(_hwndBarraProgresso, rettangolo.X, rettangolo.Y, rettangolo.Width, rettangolo.Height, True)
            SetWindowPos(_hwndBarraProgresso, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER Or SWP_NOACTIVATE)
        Catch ex As Exception
            _log.ScriviLog("PosizionaBarraProgresso: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraBarraProgresso()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf MostraBarraProgresso))
                Return
            End If

            CreaBarraProgresso()
            If _hwndBarraProgresso = IntPtr.Zero Then Return

            _barraProgressoVisibile = True
            SendMessage(_hwndBarraProgresso, PBM_SETPOS, IntPtr.Zero, IntPtr.Zero)
            PosizionaBarraProgresso()
            ShowWindow(_hwndBarraProgresso, SW_SHOW)
            SetWindowPos(_hwndBarraProgresso, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER Or SWP_NOACTIVATE)
        Catch ex As Exception
            _log.ScriviLog("MostraBarraProgresso: " & ex.Message)
        End Try
    End Sub

    Public Sub NascondiBarraProgresso()
        Try
            If _hwndBarraProgresso = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf NascondiBarraProgresso))
                Return
            End If

            _barraProgressoVisibile = False
            ShowWindow(_hwndBarraProgresso, SW_HIDE)
            SendMessage(_hwndBarraProgresso, PBM_SETPOS, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            _log.ScriviLog("NascondiBarraProgresso: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaPercentualeBarra(percentuale As Integer)
        Try
            If _hwndBarraProgresso = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of Integer)(AddressOf ImpostaPercentualeBarra), percentuale)
                Return
            End If

            Dim valore As Integer = percentuale
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            SendMessage(_hwndBarraProgresso, PBM_SETPOS, New IntPtr(valore), IntPtr.Zero)
        Catch ex As Exception
            _log.ScriviLog("ImpostaPercentualeBarra: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnClientSizeChanged(e As EventArgs)
        Try
            MyBase.OnClientSizeChanged(e)
            RiposizionaPannelloVideo()
            PosizionaTestoAssistente()
            PosizionaBarraProgresso()
            If Me.WindowState <> FormWindowState.Minimized AndAlso Me.ClientSize.Width > 0 AndAlso Me.ClientSize.Height > 0 Then
                RidimensionaControlloCorrente()
                Me.Invalidate(True)
                AnnunciaCambioOrientamento()
            End If
        Catch ex As Exception
            _log.ScriviLog("OnClientSizeChanged: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = WM_CTLCOLORSTATIC Then
                If m.LParam = _hwndSottotitolo OrElse m.LParam = _hwndPannelloVideo OrElse (m.LParam = _hwndTestoAssistente AndAlso _hwndTestoAssistente <> IntPtr.Zero) Then
                    Dim hdc As IntPtr = m.WParam
                    SetTextColor(hdc, RgbColore(255, 255, 255))
                    SetBkMode(hdc, TRANSPARENT_BKMODE)
                    m.Result = _brushSfondoPannello
                    Return
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("WndProc (pannello video): " & ex.Message)
        End Try

        ' Alt da solo e F10 non devono mai far entrare Windows nel "modo menu" della finestra: WinGabriel non ha una barra dei
        ' menu e i tasti li gestisce da sé (WGTastiera). Normalmente il gancio della tastiera trattiene Alt, ma quando un Alt arriva
        ' lo stesso alla finestra (gancio sospeso o appena reinstallato, tasto rimandato da un'altra applicazione) Windows
        ' avvia il modo menu, consuma il tasto successivo e toglie il fuoco ai controlli: da lì Alt sembra non funzionare più.
        ' Alt con lo spazio resta com'è (il menu di sistema della finestra).
        Try
            If m.Msg = WM_SYSCOMMAND AndAlso (m.WParam.ToInt64() And &HFFF0L) = SC_KEYMENU AndAlso m.LParam.ToInt64() <> 32L Then
                m.Result = IntPtr.Zero
                Return
            End If
            If (m.Msg = WM_SYSKEYDOWN OrElse m.Msg = WM_SYSKEYUP) AndAlso (m.WParam.ToInt32() = VK_MENU OrElse m.WParam.ToInt32() = VK_F10) Then
                m.Result = IntPtr.Zero
                Return
            End If
        Catch exAlt As Exception
            _log.ScriviLog("WndProc (Alt): " & exAlt.Message)
        End Try

        ' WinGabriel torna davanti da un'altra applicazione (impostazioni di Windows, un programma
        ' esterno chiuso, Alt Tab): gli screen reader rimasti accesi si chiudono.
        Try
            If m.Msg = WM_ACTIVATEAPP Then
                If m.WParam <> IntPtr.Zero Then
                    WGScreenReader.DopoRitornoDaAltraApplicazione()
                Else
                    WGScreenReader.DopoPassaggioInSecondoPiano()
                End If
            End If
        Catch exSchermo As Exception
            _log.ScriviLog("WndProc (screen reader): " & exSchermo.Message)
        End Try

        Try
            MyBase.WndProc(m)
        Catch exBase As Exception
            _log.ScriviLog("WndProc (base): " & exBase.Message)
        End Try
    End Sub

    Private Shared Function RgbColore(r As Integer, g As Integer, b As Integer) As Integer
        Try
            Return r Or (g << 8) Or (b << 16)
        Catch
            Return 0
        End Try
    End Function

    Private Sub LarghezzaRiservataCambiata()
        Try
            RidimensionaControlloCorrente()
        Catch ex As Exception
            _log.ScriviLog("LarghezzaRiservataCambiata: " & ex.Message)
        End Try
    End Sub

    Private Function LarghezzaContenutoDisponibile() As Integer
        Try
            Dim larghezza As Integer = Me.ClientSize.Width
            If _pannelloVideoVisibile AndAlso Not PannelloVideoInBasso() Then larghezza = larghezza - LARGHEZZA_PANNELLO_VIDEO
            If larghezza < 0 Then larghezza = 0
            Return larghezza
        Catch ex As Exception
            _log.ScriviLog("LarghezzaContenutoDisponibile: " & ex.Message)
            Return Me.ClientSize.Width
        End Try
    End Function

    Private Function AltezzaContenutoDisponibile() As Integer
        Try
            Dim altezza As Integer = Me.ClientSize.Height
            If _pannelloVideoVisibile AndAlso PannelloVideoInBasso() Then altezza = altezza - AltezzaPannelloVideoInBasso()
            If _testoAssistenteVisibile Then altezza = altezza - AltezzaTestoAssistente()
            If altezza < 0 Then altezza = 0
            Return altezza
        Catch ex As Exception
            _log.ScriviLog("AltezzaContenutoDisponibile: " & ex.Message)
            Return Me.ClientSize.Height
        End Try
    End Function

    Private Function PannelloVideoInBasso() As Boolean
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing OrElse Not gestoreTouch.TouchAttivo Then Return False
            Return gestoreTouch.UsaLayoutVerticale(Me.ClientSize.Width, Me.ClientSize.Height)
        Catch ex As Exception
            _log.ScriviLog("PannelloVideoInBasso: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AltezzaPannelloVideoInBasso() As Integer
        Try
            Dim altezzaTotale As Integer = Me.ClientSize.Height
            Dim altezzaVoluta As Integer = CInt(altezzaTotale * 0.4)
            If altezzaVoluta < 240 Then altezzaVoluta = 240
            If altezzaVoluta > altezzaTotale \ 2 Then altezzaVoluta = altezzaTotale \ 2
            If altezzaVoluta < ALTEZZA_ETICHETTA_SOTTOTITOLO + 1 Then altezzaVoluta = ALTEZZA_ETICHETTA_SOTTOTITOLO + 1
            Return altezzaVoluta
        Catch ex As Exception
            _log.ScriviLog("AltezzaPannelloVideoInBasso: " & ex.Message)
            Return 240
        End Try
    End Function

    ' Quando cambia la dimensione della finestra (per esempio ruotando lo schermo) si adattano tutte le
    ' finestre della pila, non solo quella davanti: con le applicazioni separate le altre restavano
    ' con le misure vecchie e tornandoci sembravano non ruotate.
    Private Sub RidimensionaControlloCorrente()
        Try
            Dim controlli As Control()
            SyncLock _lockStack
                controlli = _stackControlli.ToArray()
            End SyncLock
            Dim area As New Rectangle(0, 0, LarghezzaContenutoDisponibile(), AltezzaContenutoDisponibile())
            For Each controllo As Control In controlli
                Try
                    If controllo Is Nothing OrElse controllo.IsDisposed Then Continue For
                    If controllo.Bounds <> area Then controllo.Bounds = area
                Catch exSingolo As Exception
                    _log.ScriviLog("RidimensionaControlloCorrente (singolo): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("RidimensionaControlloCorrente: " & ex.Message)
        End Try
    End Sub

    ' Tornando su WinGabriel da un'altra finestra il fuoco va subito al controllo davanti: il menu
    ' riceve i tasti solo quando ha il fuoco, e prima restava in attesa finché non lo si toccava.
    Protected Overrides Sub OnActivated(e As EventArgs)
        MyBase.OnActivated(e)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Me.BeginInvoke(New MethodInvoker(Sub()
                                                 Try
                                                     Dim cima As Control = ControlloInCima()
                                                     If cima IsNot Nothing Then ForzaFocusControllo(cima)
                                                 Catch exFuoco As Exception
                                                     _log.ScriviLog("OnActivated (fuoco): " & exFuoco.Message)
                                                 End Try
                                             End Sub))
        Catch ex As Exception
            _log.ScriviLog("OnActivated: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraPannelloVideo()
        Try
            If _hwndPannelloVideo = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf MostraPannelloVideo))
                Return
            End If
            _pannelloVideoVisibile = True
            ShowWindow(_hwndSottotitolo, SW_SHOW)
            ShowWindow(_hwndPannelloVideo, SW_SHOW)
            RiposizionaPannelloVideo()
            PosizionaTestoAssistente()
        Catch ex As Exception
            _log.ScriviLog("MostraPannelloVideo: " & ex.Message)
        End Try
    End Sub

    Public Sub NascondiPannelloVideo()
        Try
            If _hwndPannelloVideo = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf NascondiPannelloVideo))
                Return
            End If
            _pannelloVideoVisibile = False
            ShowWindow(_hwndSottotitolo, SW_HIDE)
            ShowWindow(_hwndPannelloVideo, SW_HIDE)
            SetWindowText(_hwndSottotitolo, "")
            PosizionaTestoAssistente()
            RidimensionaControlloCorrente()
        Catch ex As Exception
            _log.ScriviLog("NascondiPannelloVideo: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniHandlePannelloVideo() As IntPtr
        Try
            Return _hwndPannelloVideo
        Catch ex As Exception
            _log.ScriviLog("OttieniHandlePannelloVideo: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Public Sub MostraSottotitolo(testo As String)
        Try
            If _hwndSottotitolo = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf MostraSottotitolo), testo)
                Return
            End If

            SetWindowText(_hwndSottotitolo, If(testo, ""))
            InvalidateRect(_hwndSottotitolo, IntPtr.Zero, True)

            If _timerPuliziaSottotitolo IsNot Nothing Then
                _timerPuliziaSottotitolo.Stop()
                If Not String.IsNullOrWhiteSpace(testo) Then
                    _timerPuliziaSottotitolo.Start()
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("MostraSottotitolo: " & ex.Message)
        End Try
    End Sub

    Private Function AltezzaTestoAssistente() As Integer
        Try
            Dim altezza As Integer = CInt(Me.ClientSize.Height * 0.25)
            If altezza < 120 Then altezza = 120
            If altezza > 320 Then altezza = 320
            If altezza > Me.ClientSize.Height \ 2 Then altezza = Me.ClientSize.Height \ 2
            If altezza < 1 Then altezza = 1
            Return altezza
        Catch ex As Exception
            _log.ScriviLog("AltezzaTestoAssistente: " & ex.Message)
            Return 160
        End Try
    End Function

    Private Function CalcolaRettangoloTestoAssistente() As Rectangle
        Try
            Dim altezza As Integer = AltezzaTestoAssistente()
            Dim fondo As Integer = Me.ClientSize.Height
            If _pannelloVideoVisibile AndAlso PannelloVideoInBasso() Then fondo = fondo - AltezzaPannelloVideoInBasso()
            Dim y As Integer = fondo - altezza
            If y < 0 Then y = 0
            Dim larghezza As Integer = LarghezzaContenutoDisponibile() - (MARGINE_TESTO_ASSISTENTE * 2)
            If larghezza < 1 Then larghezza = 1
            Return New Rectangle(MARGINE_TESTO_ASSISTENTE, y + MARGINE_TESTO_ASSISTENTE \ 2, larghezza, Math.Max(1, altezza - MARGINE_TESTO_ASSISTENTE))
        Catch ex As Exception
            _log.ScriviLog("CalcolaRettangoloTestoAssistente: " & ex.Message)
            Return New Rectangle(0, 0, 400, 160)
        End Try
    End Function

    Private Sub CreaTestoAssistente()
        Try
            If _hwndTestoAssistente <> IntPtr.Zero Then Return
            If Not Me.IsHandleCreated Then Return

            If _brushSfondoPannello = IntPtr.Zero Then _brushSfondoPannello = CreateSolidBrush(0)

            Dim rettangolo As Rectangle = CalcolaRettangoloTestoAssistente()
            _hwndTestoAssistente = CreateWindowEx(0, "STATIC", "",
                WS_CHILD Or WS_DISABLED Or SS_LEFT Or SS_NOPREFIX,
                rettangolo.X, rettangolo.Y, rettangolo.Width, rettangolo.Height,
                Me.Handle, IntPtr.Zero, GetModuleHandle(Nothing), IntPtr.Zero)

            If _hwndTestoAssistente <> IntPtr.Zero Then
                If _fontTestoAssistente Is Nothing Then _fontTestoAssistente = New Font("Segoe UI", 20.0F, FontStyle.Regular, GraphicsUnit.Point)
                SendMessage(_hwndTestoAssistente, WM_SETFONT, _fontTestoAssistente.ToHfont(), New IntPtr(1))
            End If

            If _timerTestoAssistente Is Nothing Then
                _timerTestoAssistente = New System.Windows.Forms.Timer()
                _timerTestoAssistente.Interval = SECONDI_TESTO_ASSISTENTE * 1000
                AddHandler _timerTestoAssistente.Tick, AddressOf TestoAssistenteScaduto
            End If
        Catch ex As Exception
            _log.ScriviLog("CreaTestoAssistente: " & ex.Message)
        End Try
    End Sub

    Private Sub PosizionaTestoAssistente()
        Try
            If _hwndTestoAssistente = IntPtr.Zero OrElse Not _testoAssistenteVisibile Then Return
            Dim rettangolo As Rectangle = CalcolaRettangoloTestoAssistente()
            MoveWindow(_hwndTestoAssistente, rettangolo.X, rettangolo.Y, rettangolo.Width, rettangolo.Height, True)
            SetWindowPos(_hwndTestoAssistente, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER Or SWP_NOACTIVATE)
        Catch ex As Exception
            _log.ScriviLog("PosizionaTestoAssistente: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraTestoAssistente(testo As String)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf MostraTestoAssistente), testo)
                Return
            End If

            If String.IsNullOrWhiteSpace(testo) Then
                NascondiTestoAssistente()
                Return
            End If

            CreaTestoAssistente()
            If _hwndTestoAssistente = IntPtr.Zero Then Return

            SetWindowText(_hwndTestoAssistente, testo.Replace(vbCrLf, vbLf).Replace(vbLf, vbCrLf))
            _testoAssistenteVisibile = True
            PosizionaTestoAssistente()
            ShowWindow(_hwndTestoAssistente, SW_SHOW)
            InvalidateRect(_hwndTestoAssistente, IntPtr.Zero, True)
            RidimensionaControlloCorrente()

            If _timerTestoAssistente IsNot Nothing Then
                _timerTestoAssistente.Stop()
                _timerTestoAssistente.Start()
            End If
        Catch ex As Exception
            _log.ScriviLog("MostraTestoAssistente: " & ex.Message)
        End Try
    End Sub

    Public Sub NascondiTestoAssistente()
        Try
            If _hwndTestoAssistente = IntPtr.Zero Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(AddressOf NascondiTestoAssistente))
                Return
            End If

            If _timerTestoAssistente IsNot Nothing Then _timerTestoAssistente.Stop()
            _testoAssistenteVisibile = False
            ShowWindow(_hwndTestoAssistente, SW_HIDE)
            SetWindowText(_hwndTestoAssistente, "")
            RidimensionaControlloCorrente()
        Catch ex As Exception
            _log.ScriviLog("NascondiTestoAssistente: " & ex.Message)
        End Try
    End Sub

    Private Sub TestoAssistenteScaduto(sender As Object, e As EventArgs)
        Try
            NascondiTestoAssistente()
        Catch ex As Exception
            _log.ScriviLog("TestoAssistenteScaduto: " & ex.Message)
        End Try
    End Sub

    Private Sub PuliziaSottotitoloTick(sender As Object, e As EventArgs)
        Try
            _timerPuliziaSottotitolo.Stop()
            If _hwndSottotitolo <> IntPtr.Zero Then
                SetWindowText(_hwndSottotitolo, "")
                InvalidateRect(_hwndSottotitolo, IntPtr.Zero, True)
            End If
        Catch ex As Exception
            _log.ScriviLog("PuliziaSottotitoloTick: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaSuonoAttesa()
        Try
            SyncLock _lockSuono
                If _suonoAttesaAttivo Then Return
                If _suoni Is Nothing Then Return
                _suoni.Attesa()
                _suonoAttesaAttivo = True
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AvviaSuonoAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaSuonoAttesa()
        Try
            SyncLock _lockSuono
                If Not _suonoAttesaAttivo Then Return
                If _suoni Is Nothing Then Return
                _suoni.StopAttesa()
                _suonoAttesaAttivo = False
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("FermaSuonoAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaLetturaAttesa()
        Try
            SyncLock _lockLetturaAttesa
                If _letturaAttesaCts IsNot Nothing AndAlso Not _letturaAttesaCts.IsCancellationRequested Then Return

                If _lettura Is Nothing Then
                    _lettura = sintesivocale.GetInstance()
                End If

                _letturaAttesaCts = New CancellationTokenSource()
                Dim tokenLocale As CancellationToken = _letturaAttesaCts.Token
                Task.Run(Sub() CicloLetturaAttesa(tokenLocale), tokenLocale)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AvviaLetturaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaLetturaAttesa()
        Try
            SyncLock _lockLetturaAttesa
                If _letturaAttesaCts IsNot Nothing Then
                    Try
                        _letturaAttesaCts.Cancel()
                        _letturaAttesaCts.Dispose()
                    Catch exCancella As Exception
                        _log.ScriviLog("FermaLetturaAttesa (cancellazione): " & exCancella.Message)
                    End Try
                    _letturaAttesaCts = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("FermaLetturaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaLetturaAttesaVuota()
        Try
            SyncLock _lockLetturaAttesaVuota
                If _letturaAttesaVuotaCts IsNot Nothing AndAlso Not _letturaAttesaVuotaCts.IsCancellationRequested Then Return

                If _lettura Is Nothing Then
                    _lettura = sintesivocale.GetInstance()
                End If

                _letturaAttesaVuotaCts = New CancellationTokenSource()
                Dim tokenLocale As CancellationToken = _letturaAttesaVuotaCts.Token
                Task.Run(Sub() CicloLetturaAttesaVuota(tokenLocale), tokenLocale)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AvviaLetturaAttesaVuota: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaLetturaAttesaVuota()
        Try
            SyncLock _lockLetturaAttesaVuota
                If _letturaAttesaVuotaCts IsNot Nothing Then
                    Try
                        _letturaAttesaVuotaCts.Cancel()
                        _letturaAttesaVuotaCts.Dispose()
                    Catch exCancella As Exception
                        _log.ScriviLog("FermaLetturaAttesaVuota (cancellazione): " & exCancella.Message)
                    End Try
                    _letturaAttesaVuotaCts = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("FermaLetturaAttesaVuota: " & ex.Message)
        End Try
    End Sub

    Private Function DeveFermarsiLetturaAttesaVuota() As Boolean
        Try
            If EsistonoAtteseAttive() Then Return True
            If EsisteControlloInterattivoVisibile() Then Return True
            Return False
        Catch ex As Exception
            _log.ScriviLog("DeveFermarsiLetturaAttesaVuota: " & ex.Message)
            Return True
        End Try
    End Function

    Private Async Sub CicloLetturaAttesaVuota(token As CancellationToken)
        Try
            Await Task.Delay(RITARDO_INIZIALE_LETTURA_ATTESA_VUOTA_MS, token)

            While Not token.IsCancellationRequested
                If DeveFermarsiLetturaAttesaVuota() Then Exit While
                If _lettura Is Nothing Then
                    _lettura = sintesivocale.GetInstance()
                End If

                If _lettura IsNot Nothing Then
                    While _lettura.StaParlando AndAlso Not token.IsCancellationRequested AndAlso Not DeveFermarsiLetturaAttesaVuota()
                        Await Task.Delay(200, token)
                    End While

                    If token.IsCancellationRequested Then Exit While
                    If DeveFermarsiLetturaAttesaVuota() Then Exit While

                    _lettura.LeggiTesto("Attendere", escludiPunteggiatura:=True)
                End If

                Await Task.Delay(INTERVALLO_LETTURA_ATTESA_VUOTA_MS, token)
            End While
        Catch ex As TaskCanceledException
        Catch ex As Exception
            _log.ScriviLog("CicloLetturaAttesaVuota: " & ex.Message)
        End Try
    End Sub

    Private Function ControlloAttesaGestisceProprioAnnuncio() As Boolean
        Try
            SyncLock _lockStack
                If _atteseAttive.Count = 0 Then Return False
                Dim cima As Control = _atteseAttive(_atteseAttive.Count - 1)
                Return TypeOf cima Is WGProcessoInCorso
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ControlloAttesaGestisceProprioAnnuncio: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Sub CicloLetturaAttesa(token As CancellationToken)
        Try
            While Not token.IsCancellationRequested
                Await Task.Delay(2000, token)

                If token.IsCancellationRequested Then Exit While
                If Not EsistonoAtteseAttive() Then Exit While
                If ControlloAttesaGestisceProprioAnnuncio() Then Continue While
                If _lettura Is Nothing Then Continue While

                While _lettura.StaParlando AndAlso Not token.IsCancellationRequested AndAlso EsistonoAtteseAttive()
                    Await Task.Delay(200, token)
                End While

                If token.IsCancellationRequested Then Exit While
                If Not EsistonoAtteseAttive() Then Exit While
                If ControlloAttesaGestisceProprioAnnuncio() Then Continue While

                _lettura.LeggiTesto("Attendere", escludiPunteggiatura:=True)
            End While
        Catch ex As TaskCanceledException
        Catch ex As Exception
            _log.ScriviLog("CicloLetturaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraAttesa(attesa As Control)
        Try
            _tmrRivelazione.Stop()

            Dim primaAttesa As Boolean = False
            SyncLock _lockStack
                If Not _atteseAttive.Contains(attesa) Then
                    If _atteseAttive.Count = 0 Then
                        primaAttesa = True
                    End If
                    _atteseAttive.Add(attesa)
                End If
                _applicazioneAttesa(attesa) = _applicazioneCorrente
            End SyncLock

            If primaAttesa Then
                AvviaSuonoAttesa()
                AvviaLetturaAttesa()
            End If

            Dim testo As String = LeggiTestoAttesa(attesa)
            MostraTestoAttesa(testo)
        Catch ex As Exception
            _log.ScriviLog("RegistraAttesa: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiTestoAttesa(attesa As Control) As String
        Try
            Dim processo As WGProcessoInCorso = TryCast(attesa, WGProcessoInCorso)
            If processo IsNot Nothing Then
                Return processo.TestoAttesa
            End If
            Return ""
        Catch ex As Exception
            _log.ScriviLog("LeggiTestoAttesa: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub RimuoviControllo(controllo As Control)
        Try
            If controllo Is Nothing Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New Action(Of Control)(AddressOf RimuoviControllo), controllo)
                Return
            End If

            If EControlloAttesa(controllo) Then
                RimuoviAttesa(controllo)
                Return
            End If

            SyncLock _lockStack
                If _stackControlli.Contains(controllo) Then
                    _stackControlli.Remove(controllo)
                End If
                If _controlliMostratiDuranteAttesa.Contains(controllo) Then
                    _controlliMostratiDuranteAttesa.Remove(controllo)
                End If
                If _controlliInAttesaDiMessaggio.Contains(controllo) Then
                    _controlliInAttesaDiMessaggio.Remove(controllo)
                End If
            End SyncLock
            _istanteUltimaRimozione = DateTime.Now
            DopoRimozioneApplicazione(controllo)
            ProseguiChiusuraDopoRimozione(controllo)

            Try
                If Me.Controls.Contains(controllo) Then
                    Me.Controls.Remove(controllo)
                End If
            Catch exRem As Exception
                _log.ScriviLog("RimuoviControllo (Remove): " & exRem.Message)
            End Try

            If EControlloMessaggio(controllo) Then
                SyncLock _lockStack
                    _controlliMessaggio.Remove(controllo)
                End SyncLock
                MostraControlliAccodatiSeMessaggioChiuso()
            End If

            _tmrRivelazione.Stop()
            _tmrRivelazione.Start()
        Catch ex As Exception
            _log.ScriviLog("RimuoviControllo: " & ex.Message)
        End Try
    End Sub

    Private Const RitardoRitornoScrivaniaMs As Integer = 400
    Private _istanteUltimaRimozione As DateTime = DateTime.MinValue

    Private Sub RivelazioneTick(sender As Object, e As EventArgs)
        Try
            _tmrRivelazione.Stop()
            _tmrRivelazione.Interval = 300
            If programmadisattivato Then Return
            ' La scrivania (o la home) non torna davanti subito dopo che si è chiusa una finestra: aspetta 400 ms,
            ' perché chi ha chiuso la finestra potrebbe aprirne subito un'altra (una notifica, un elenco).
            Dim cimaPila As Control = CimaDellaPila()
            If cimaPila IsNot Nothing AndAlso ApplicazioniSeparate AndAlso EApplicazioneProtetta(ApplicazioneDi(cimaPila)) Then
                Dim trascorsi As Double = (DateTime.Now - _istanteUltimaRimozione).TotalMilliseconds
                If trascorsi >= 0 AndAlso trascorsi < RitardoRitornoScrivaniaMs Then
                    _tmrRivelazione.Interval = CInt(Math.Max(20, RitardoRitornoScrivaniaMs - trascorsi))
                    _tmrRivelazione.Start()
                    Return
                End If
            End If
            If GuardiaFocusSospesa Then
                _tmrRivelazione.Start()
                Return
            End If
            If EsistonoAtteseAttive() Then
                _tmrRivelazione.Start()
                Return
            End If
            ' Mentre un collegamento attraversa i menu la cima della pila è un menu di passaggio: si aspetta.
            ' Lo stesso mentre un lavoro è appena partito e la sua attesa non è ancora comparsa.
            If _percorsiInCorso > 0 OrElse _rivelazioniRimandate > 0 Then
                _tmrRivelazione.Start()
                Return
            End If
            MostraControlliAccodatiSeMessaggioChiuso()
            MostraControlloInCima()
        Catch ex As Exception
            _log.ScriviLog("RivelazioneTick: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviAttesa(attesa As Control)
        Try
            SyncLock _lockStack
                If _atteseAttive.Contains(attesa) Then
                    _atteseAttive.Remove(attesa)
                End If
                _applicazioneAttesa.Remove(attesa)
                If _atteseAttive.Count = 0 Then
                    _controlliMostratiDuranteAttesa.Clear()
                    _applicazioneAttesa.Clear()
                    _cambioDuranteAttesa = False
                End If
            End SyncLock

            AggiornaVistaAttesa()

            If Not EsistonoAtteseAttive() Then
                FermaSuonoAttesa()
                FermaLetturaAttesa()
                _tmrRivelazione.Stop()
                _tmrRivelazione.Start()
            End If
        Catch ex As Exception
            _log.ScriviLog("RimuoviAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaFormPerInnesto(f As Form)
        Try
            If f Is Nothing Then Return

            If f.Visible AndAlso f.TopLevel Then
                Try
                    f.Hide()
                Catch exHide As Exception
                    _log.ScriviLog("PreparaFormPerInnesto (Hide): " & exHide.Message)
                End Try
            End If

            If f.TopLevel Then
                Try
                    f.TopLevel = False
                Catch exTop As Exception
                    _log.ScriviLog("PreparaFormPerInnesto (TopLevel): " & exTop.Message)
                End Try
            End If

            f.FormBorderStyle = FormBorderStyle.None
            f.ControlBox = False
            f.TopMost = False

            Try
                If f.WindowState <> FormWindowState.Normal Then
                    f.WindowState = FormWindowState.Normal
                End If
            Catch exState As Exception
                _log.ScriviLog("PreparaFormPerInnesto (WindowState): " & exState.Message)
            End Try

            f.Dock = DockStyle.Fill
        Catch ex As Exception
            _log.ScriviLog("PreparaFormPerInnesto: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTutticontrolliTranne(eccetto As Control)
        Try
            Dim istantanea As Control() = New Control(Me.Controls.Count - 1) {}
            Me.Controls.CopyTo(istantanea, 0)

            For Each c As Control In istantanea
                Try
                    If c Is Nothing Then Continue For
                    If c Is eccetto Then Continue For
                    If c Is _vistaAttesa Then Continue For

                    If c.IsDisposed Then
                        Try
                            If Me.Controls.Contains(c) Then Me.Controls.Remove(c)
                        Catch exRimuoviDisposto As Exception
                            _log.ScriviLog("NascondiTutticontrolliTranne (rimozione disposto): " & exRimuoviDisposto.Message)
                        End Try
                        Continue For
                    End If

                    If c.Visible Then
                        c.Visible = False
                    End If
                Catch exSingolo As Exception
                    _log.ScriviLog("NascondiTutticontrolliTranne (singolo): " & exSingolo.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("NascondiTutticontrolliTranne: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraControlloInCima()
        Try
            Dim cima As Control = Nothing

            SyncLock _lockStack
                For i As Integer = _stackControlli.Count - 1 To 0 Step -1
                    Dim c As Control = _stackControlli(i)
                    If c Is Nothing OrElse c.IsDisposed Then
                        _stackControlli.RemoveAt(i)
                    End If
                Next

                If _stackControlli.Count > 0 Then
                    cima = _stackControlli(_stackControlli.Count - 1)
                End If
            End SyncLock

            ' Un messaggio aperto sta davanti a tutto, qualunque sia l'ordine della pila: la scrivania o un'altra applicazione che torna
            ' davanti non lo nascondono, e i tasti (Esc, Invio) vanno al messaggio e non a quello che ci sarebbe sotto.
            Dim messaggioDavanti As Control = UltimoMessaggioAperto()
            If messaggioDavanti IsNot Nothing Then cima = messaggioDavanti

            ' Il menu applicazioni chiuso (modalità smart) non si mostra anche se è l'unico rimasto.
            If cima IsNot Nothing AndAlso cima Is _menuIniziale AndAlso Not MenuApplicazioniAperto Then
                cima.Visible = False
                AggiornaVistaAttesa()
                Return
            End If

            If cima IsNot Nothing Then
                NascondiTutticontrolliTranne(cima)
                Dim area As New Rectangle(0, 0, LarghezzaContenutoDisponibile(), AltezzaContenutoDisponibile())
                If area.Width > 0 AndAlso area.Height > 0 AndAlso cima.Bounds <> area Then cima.Bounds = area
                cima.Visible = True
                cima.BringToFront()
                ConcediPrecedenzaPerCambioApplicazione(cima)
                AggiornaVistaAttesa()
                ForzaFocusControllo(cima)
                AssicuraPrimoPiano()
            Else
                AggiornaVistaAttesa()
            End If
        Catch ex As Exception
            _log.ScriviLog("MostraControlloInCima: " & ex.Message)
        End Try
    End Sub

    Private Function ControlloInCima() As Control
        Try
            SyncLock _lockStack
                For i As Integer = _stackControlli.Count - 1 To 0 Step -1
                    Dim c As Control = _stackControlli(i)
                    If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.Visible Then
                        Return c
                    End If
                Next
            End SyncLock
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog("ControlloInCima: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function EsistonoAtteseAttive() As Boolean
        Try
            SyncLock _lockStack
                For i As Integer = _atteseAttive.Count - 1 To 0 Step -1
                    Dim a As Control = _atteseAttive(i)
                    If a Is Nothing OrElse a.IsDisposed Then
                        _atteseAttive.RemoveAt(i)
                    End If
                Next
                Return _atteseAttive.Count > 0
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("EsistonoAtteseAttive: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiornaVistaAttesa()
        Try
            If _vistaAttesa Is Nothing OrElse _vistaAttesa.IsDisposed Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf AggiornaVistaAttesa))
                Return
            End If

            If GuardiaFocusSospesa Then
                Return
            End If

            If EsistonoAtteseAttive() Then
                FermaLetturaAttesaVuota()

                Dim attesaInSecondoPiano As Boolean = PrecedenzaAttiva()
                If attesaInSecondoPiano Then
                    _vistaAttesa.Visible = False
                    If _cambioDuranteAttesa Then
                        FermaSuonoAttesa()
                        FermaLetturaAttesa()
                    End If
                Else
                    _vistaAttesa.Visible = True
                    _vistaAttesa.BringToFront()
                    If _cambioDuranteAttesa Then
                        AvviaSuonoAttesa()
                        AvviaLetturaAttesa()
                    End If
                End If
                Return
            End If

            If EsisteControlloInterattivoVisibile() Then
                _vistaAttesa.Visible = False
                FermaSuonoAttesa()
                FermaLetturaAttesaVuota()
            Else
                _vistaAttesa.Testo = ""
                _vistaAttesa.Visible = True
                _vistaAttesa.BringToFront()
                AvviaSuonoAttesa()
                AvviaLetturaAttesaVuota()
            End If
        Catch ex As Exception
            _log.ScriviLog("AggiornaVistaAttesa: " & ex.Message)
        End Try
    End Sub

    Private Function PrecedenzaAttiva() As Boolean
        Try
            Dim cima As Control = ControlloInCima()
            SyncLock _lockStack
                If _controlliMostratiDuranteAttesa.Count = 0 Then Return False
                If cima Is Nothing Then Return True
                Return _controlliMostratiDuranteAttesa.Contains(cima)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("PrecedenzaAttiva: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ConcediPrecedenzaPerCambioApplicazione(cima As Control)
        Try
            If cima Is Nothing OrElse cima.IsDisposed Then Return
            If Not ApplicazioniSeparate Then Return
            SyncLock _lockStack
                If _atteseAttive.Count = 0 Then Return
                Dim applicazioneCima As Integer = ApplicazioneDi(cima)
                For Each attesa As Control In _atteseAttive
                    Dim applicazioneAttesa As Integer
                    If _applicazioneAttesa.TryGetValue(attesa, applicazioneAttesa) AndAlso applicazioneAttesa = applicazioneCima Then Return
                Next
                If Not _controlliMostratiDuranteAttesa.Contains(cima) Then
                    _controlliMostratiDuranteAttesa.Add(cima)
                End If
                _cambioDuranteAttesa = True
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ConcediPrecedenzaPerCambioApplicazione: " & ex.Message)
        End Try
    End Sub

    Friend Function AttesaInSecondoPiano() As Boolean
        Try
            If Not _cambioDuranteAttesa Then Return False
            Return PrecedenzaAttiva()
        Catch ex As Exception
            _log.ScriviLog("AttesaInSecondoPiano: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EsisteControlloInterattivoVisibile() As Boolean
        Try
            SyncLock _lockStack
                For Each c As Control In _stackControlli
                    If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.Visible Then
                        Return True
                    End If
                Next
            End SyncLock
            Return False
        Catch ex As Exception
            _log.ScriviLog("EsisteControlloInterattivoVisibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub MostraTestoAttesa(testo As String)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf MostraTestoAttesa), testo)
                Return
            End If

            If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                _vistaAttesa.Testo = testo
                Dim attesaInSecondoPiano As Boolean = PrecedenzaAttiva()
                If Not attesaInSecondoPiano Then
                    _vistaAttesa.Visible = True
                    _vistaAttesa.BringToFront()
                End If
            End If
            ParlaSeNvda(testo)
        Catch ex As Exception
            _log.ScriviLog("MostraTestoAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaControlloCorrenteEliminato()
        Try
            Dim cimaAttesa As Control = Nothing
            Dim rimosso As Boolean = False

            SyncLock _lockStack
                If _stackControlli.Count = 0 Then Return
                cimaAttesa = _stackControlli(_stackControlli.Count - 1)
                If cimaAttesa Is Nothing OrElse cimaAttesa.IsDisposed Then
                    _stackControlli.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
                    rimosso = True
                End If
            End SyncLock

            If rimosso Then MostraControlloInCima()
        Catch ex As Exception
            _log.ScriviLog("ControllaControlloCorrenteEliminato: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpediscaStandbyTick(sender As Object, e As EventArgs)
        Try
            SetThreadExecutionState(ES_CONTINUOUS Or ES_SYSTEM_REQUIRED Or ES_DISPLAY_REQUIRED)
        Catch ex As Exception
            _log.ScriviLog("ImpediscaStandbyTick: " & ex.Message)
        End Try
    End Sub

    Private Sub GuardiaFocusTick(sender As Object, e As EventArgs)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return

            ControllaControlloCorrenteEliminato()

            If programmadisattivato Then Return
            If GuardiaFocusSospesa Then Return
            If Me.WindowState = FormWindowState.Minimized Then Return
            If InModalitaWordAttiva() Then Return

            Dim fgHwnd As IntPtr = GetForegroundWindow()

            If fgHwnd = Me.Handle Then
                If Me.ActiveControl Is Nothing AndAlso Not EsistonoAtteseAttive() Then
                    Dim cima As Control = ControlloInCima()
                    If cima IsNot Nothing AndAlso Not cima.ContainsFocus Then
                        ForzaFocusControllo(cima)
                    End If
                End If
                Return
            End If

            If fgHwnd <> IntPtr.Zero Then
                Dim fgPid As UInteger = 0
                GetWindowThreadProcessId(fgHwnd, fgPid)
                If fgPid = GetCurrentProcessId() Then Return
            End If

            If _tastiera IsNot Nothing Then
                Try
                    If _tastiera.CambioFocusRecenteUtente() Then Return
                    If _tastiera.FinestraDiSistema(fgHwnd) Then Return
                Catch exTastiera As Exception
                    _log.ScriviLog("GuardiaFocusTick (verifica cambio focus): " & exTastiera.Message)
                End Try
            End If

            AssicuraPrimoPiano()
        Catch ex As Exception
            _log.ScriviLog("GuardiaFocusTick: " & ex.Message)
        End Try
    End Sub

    Private Sub AssicuraPrimoPiano()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If GuardiaFocusSospesa Then Return
            If GetForegroundWindow() = Me.Handle Then Return

            Me.Activate()
            BringWindowToTop(Me.Handle)

            If SetForegroundWindow(Me.Handle) Then Return

            Dim hwndCorrente As IntPtr = GetForegroundWindow()
            If hwndCorrente <> IntPtr.Zero AndAlso hwndCorrente <> Me.Handle Then
                Dim pidCorrente As UInteger = 0
                Dim threadCorrente As UInteger = GetWindowThreadProcessId(hwndCorrente, pidCorrente)
                Dim threadMio As UInteger = GetCurrentThreadId()

                If threadCorrente <> 0 AndAlso threadCorrente <> threadMio Then
                    Dim aggancioRiuscito As Boolean = False
                    Try
                        aggancioRiuscito = AttachThreadInput(threadMio, threadCorrente, True)
                        BringWindowToTop(Me.Handle)
                        SetForegroundWindow(Me.Handle)
                    Catch exAggancio As Exception
                        _log.ScriviLog("AssicuraPrimoPiano (AttachThreadInput): " & exAggancio.Message)
                    Finally
                        If aggancioRiuscito Then
                            Try
                                AttachThreadInput(threadMio, threadCorrente, False)
                            Catch exSgancio As Exception
                                _log.ScriviLog("AssicuraPrimoPiano (sgancio): " & exSgancio.Message)
                            End Try
                        End If
                    End Try
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("AssicuraPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Public Sub PortaInPrimoPiano()
        AssicuraPrimoPiano()
    End Sub

    Public Shared Sub PortaFinestraEsternaInPrimoPiano(handleFinestra As IntPtr)
        Try
            If handleFinestra = IntPtr.Zero Then Return
            If GetForegroundWindow() = handleFinestra Then Return

            AllowSetForegroundWindow(ASFW_ANY)
            BringWindowToTop(handleFinestra)
            If SetForegroundWindow(handleFinestra) Then Return

            Dim hwndCorrente As IntPtr = GetForegroundWindow()
            If hwndCorrente = IntPtr.Zero OrElse hwndCorrente = handleFinestra Then Return

            Dim pidCorrente As UInteger = 0
            Dim threadCorrente As UInteger = GetWindowThreadProcessId(hwndCorrente, pidCorrente)
            Dim threadMio As UInteger = GetCurrentThreadId()
            If threadCorrente = 0 OrElse threadCorrente = threadMio Then Return

            Dim aggancioRiuscito As Boolean = False
            Try
                aggancioRiuscito = AttachThreadInput(threadMio, threadCorrente, True)
                AllowSetForegroundWindow(ASFW_ANY)
                BringWindowToTop(handleFinestra)
                SetForegroundWindow(handleFinestra)
                SetFocus(handleFinestra)
            Catch exAggancio As Exception
                _logShared.ScriviLog("PortaFinestraEsternaInPrimoPiano (AttachThreadInput): " & exAggancio.Message)
            Finally
                If aggancioRiuscito Then
                    Try
                        AttachThreadInput(threadMio, threadCorrente, False)
                    Catch exSgancio As Exception
                        _logShared.ScriviLog("PortaFinestraEsternaInPrimoPiano (sgancio): " & exSgancio.Message)
                    End Try
                End If
            End Try
        Catch ex As Exception
            _logShared.ScriviLog("PortaFinestraEsternaInPrimoPiano: " & ex.Message)
        End Try
    End Sub

    Private Function DelegaCambioScheda(avanti As Boolean) As Boolean
        Try
            Dim cima As Control = ControlloInCima()
            Dim conSchede As IWGControlloConSchede = TryCast(cima, IWGControlloConSchede)
            If conSchede Is Nothing Then Return False
            Return conSchede.CambiaScheda(avanti)
        Catch ex As Exception
            _log.ScriviLog("DelegaCambioScheda: " & ex.Message)
            Return False
        End Try
    End Function

    ' Control Tab vale solo per chi ha più schede o documenti (browser, testi dell'editor): altrove non fa nulla.
    ' Per passare tra le applicazioni si usano Windows Tab e Shift Windows Tab.
    Public Sub NavigaControlloSuccessivo()
        Try
            DelegaCambioScheda(True)
        Catch ex As Exception
            _log.ScriviLog("NavigaControlloSuccessivo: " & ex.Message)
        End Try
    End Sub

    Public Sub NavigaControlloPrecedente()
        Try
            DelegaCambioScheda(False)
        Catch ex As Exception
            _log.ScriviLog("NavigaControlloPrecedente: " & ex.Message)
        End Try
    End Sub

    Public Sub PulisciControlliDisposti()
        Try
            SyncLock _lockStack
                _stackControlli.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
                _atteseAttive.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
                _controlliInAttesaDiMessaggio.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
            End SyncLock
            PulisciApplicazioni()
            AggiornaVistaAttesa()
            MostraControlliAccodatiSeMessaggioChiuso()
        Catch ex As Exception
            _log.ScriviLog("PulisciControlliDisposti: " & ex.Message)
        End Try
    End Sub

    Private Const TENTATIVI_MASSIMI_FOCUS As Integer = 6

    Private Sub ForzaFocusControllo(controllo As Control)
        Try
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            If GuardiaFocusSospesa Then Return
            If EsistonoAtteseAttive() Then Return

            If Not controllo.IsHandleCreated Then
                Try
                    Dim forzaCreazioneHandle As IntPtr = controllo.Handle
                Catch exHandle As Exception
                    _log.ScriviLog("ForzaFocusControllo (creazione handle): " & exHandle.Message)
                    Return
                End Try
            End If

            If controllo.ContainsFocus Then Return

            Try
                If Me.ActiveControl IsNot controllo Then
                    Me.ActiveControl = controllo
                End If
            Catch exActive As Exception
                _log.ScriviLog("ForzaFocusControllo (ActiveControl): " & exActive.Message)
            End Try

            Try
                controllo.Select()
            Catch exSelect As Exception
                _log.ScriviLog("ForzaFocusControllo (Select): " & exSelect.Message)
            End Try

            Try
                controllo.Focus()
            Catch exFocus As Exception
                _log.ScriviLog("ForzaFocusControllo (Focus): " & exFocus.Message)
            End Try

            If Not controllo.ContainsFocus Then
                Try
                    SetFocus(controllo.Handle)
                Catch exWin32 As Exception
                    _log.ScriviLog("ForzaFocusControllo (SetFocus): " & exWin32.Message)
                End Try
            End If

            If Not controllo.ContainsFocus Then
                Try
                    controllo.BeginInvoke(New MethodInvoker(Sub() RitentaFocus(controllo, 1)))
                Catch exRetry As Exception
                    _log.ScriviLog("ForzaFocusControllo (pianificazione ritentativo): " & exRetry.Message)
                End Try
            End If
        Catch ex As Exception
            _log.ScriviLog("ForzaFocusControllo: " & ex.Message)
        End Try
    End Sub

    Private Sub RitentaFocus(controllo As Control, tentativo As Integer)
        Try
            If controllo Is Nothing OrElse controllo.IsDisposed Then Return
            If GuardiaFocusSospesa Then Return
            If Not controllo.Visible Then Return
            If controllo.ContainsFocus Then Return
            If EsistonoAtteseAttive() Then Return
            If ControlloInCima() IsNot controllo Then Return

            If Not controllo.IsHandleCreated Then
                Try
                    Dim forzaCreazioneHandle As IntPtr = controllo.Handle
                Catch exHandle As Exception
                    _log.ScriviLog("RitentaFocus (creazione handle): " & exHandle.Message)
                    Return
                End Try
            End If

            Try
                Me.ActiveControl = controllo
            Catch exActive As Exception
                _log.ScriviLog("RitentaFocus (ActiveControl): " & exActive.Message)
            End Try

            Try
                controllo.Select()
            Catch exSelect As Exception
                _log.ScriviLog("RitentaFocus (Select): " & exSelect.Message)
            End Try

            Try
                controllo.Focus()
            Catch exFocus As Exception
                _log.ScriviLog("RitentaFocus (Focus): " & exFocus.Message)
            End Try

            If Not controllo.ContainsFocus Then
                Try
                    SetFocus(controllo.Handle)
                Catch exWin32 As Exception
                    _log.ScriviLog("RitentaFocus (SetFocus): " & exWin32.Message)
                End Try
            End If

            If Not controllo.ContainsFocus Then
                If tentativo < TENTATIVI_MASSIMI_FOCUS Then
                    Try
                        Dim prossimoTentativo As Integer = tentativo + 1
                        controllo.BeginInvoke(New MethodInvoker(Sub() RitentaFocus(controllo, prossimoTentativo)))
                    Catch exRetry As Exception
                        _log.ScriviLog("RitentaFocus (pianificazione ritentativo " & (tentativo + 1) & "): " & exRetry.Message)
                    End Try
                Else
                    _log.ScriviLog("RitentaFocus: impossibile portare a fuoco il controllo dopo " & TENTATIVI_MASSIMI_FOCUS & " tentativi.")
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("RitentaFocus: " & ex.Message)
        End Try
    End Sub

    Private Function ForegroundDelProcessoCorrente() As Boolean
        Try
            Dim hwnd As IntPtr = GetForegroundWindow()
            If hwnd = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0
            GetWindowThreadProcessId(hwnd, pid)
            Return pid = GetCurrentProcessId()
        Catch ex As Exception
            _log.ScriviLog("ForegroundDelProcessoCorrente: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function PuoRicevereInput(controllo As Control) As Boolean
        Try
            If controllo Is Nothing OrElse controllo.IsDisposed OrElse Not controllo.IsHandleCreated Then Return False
            If Not controllo.Visible Then Return False
            If programmadisattivato Then Return False
            If EsistonoAtteseAttive() Then
                Dim haPrecedenza As Boolean = False
                SyncLock _lockStack
                    haPrecedenza = _controlliMostratiDuranteAttesa.Contains(controllo)
                End SyncLock
                If Not haPrecedenza Then Return False
            End If
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False

            ' Con un messaggio aperto i tasti vanno solo a lui.
            Dim messaggioAperto As Control = UltimoMessaggioAperto()
            If messaggioAperto IsNot Nothing Then Return messaggioAperto Is controllo

            Dim cima As Control = ControlloInCima()
            If cima IsNot controllo Then Return False

            If GetForegroundWindow() = Me.Handle Then Return True
            If ForegroundDelProcessoCorrente() Then Return True
            Return True
        Catch ex As Exception
            _log.ScriviLog("PuoRicevereInput: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function GestisceControllo(controllo As Control) As Boolean
        Try
            If controllo Is Nothing Then Return False
            SyncLock _lockStack
                Return _stackControlli.Contains(controllo)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("GestisceControllo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ContaControlli() As Integer
        Try
            SyncLock _lockStack
                _stackControlli.RemoveAll(Function(c) c Is Nothing OrElse c.IsDisposed)
                Return _stackControlli.Count
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("ContaControlli: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Sub MostraAttesa(testo As String)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New Action(Of String)(AddressOf MostraAttesa), testo)
                Return
            End If
            MostraTestoAttesa(testo)
        Catch ex As Exception
            _log.ScriviLog("MostraAttesa: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiornaTestoAttesa(testo As String)
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New Action(Of String)(AddressOf AggiornaTestoAttesa), testo)
                Return
            End If

            If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                _vistaAttesa.Testo = testo
            End If

            ParlaSeNvda(testo)
        Catch ex As Exception
            _log.ScriviLog("AggiornaTestoAttesa: " & ex.Message)
        End Try
    End Sub

    Public Sub NascondiAttesa()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf NascondiAttesa))
                Return
            End If

            If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                _vistaAttesa.Testo = ""
            End If

            AggiornaVistaAttesa()

            If Not EsistonoAtteseAttive() Then
                Dim cima As Control = ControlloInCima()
                If cima IsNot Nothing Then
                    ForzaFocusControllo(cima)
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("NascondiAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub AttendiSintesiProntaAlPrimoAvvio()
        Try
            If _sintesiVerificata Then Return
            If _attesaSintesiInCorso Then Return
            If programmadisattivato Then Return
            _attesaSintesiInCorso = True

            Try
                If _lettura Is Nothing Then
                    _lettura = sintesivocale.GetInstance()
                End If

                If _lettura Is Nothing Then
                    _sintesiVerificata = True
                    Return
                End If

                If _lettura.CanalePronto Then
                    _sintesiVerificata = True
                    Return
                End If

                If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                    _vistaAttesa.Testo = "Avvio dei componenti vocali in corso, attendere"
                    _vistaAttesa.Visible = True
                    _vistaAttesa.BringToFront()
                    Try
                        _vistaAttesa.Update()
                    Catch exPaint As Exception
                        _log.ScriviLog("AttendiSintesiProntaAlPrimoAvvio (Update): " & exPaint.Message)
                    End Try
                End If

                Dim cronometro As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
                Const timeoutMs As Integer = 120000
                Dim suonoAvviatoQui As Boolean = False

                Try
                    SyncLock _lockSuono
                        If Not _suonoAttesaAttivo Then
                            suonoAvviatoQui = True
                        End If
                    End SyncLock
                    If suonoAvviatoQui Then AvviaSuonoAttesa()

                    While Not _lettura.CanalePronto AndAlso Not programmadisattivato
                        If cronometro.ElapsedMilliseconds >= timeoutMs Then
                            Exit While
                        End If
                        Application.DoEvents()
                        System.Threading.Thread.Sleep(50)
                    End While
                Finally
                    If suonoAvviatoQui Then FermaSuonoAttesa()
                End Try

                _sintesiVerificata = True

                If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                    _vistaAttesa.Testo = ""
                End If
            Finally
                _attesaSintesiInCorso = False
            End Try
        Catch ex As Exception
            _log.ScriviLog("AttendiSintesiProntaAlPrimoAvvio: " & ex.Message)
            _sintesiVerificata = True
            _attesaSintesiInCorso = False
        End Try
    End Sub

    Private Sub ParlaSeNvda(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            If _lettura Is Nothing Then
                _lettura = sintesivocale.GetInstance()
            End If
            If _lettura IsNot Nothing AndAlso _lettura.NvdaInEsecuzione() Then
                _lettura.LeggiTesto(testo)
            End If
        Catch ex As Exception
            _log.ScriviLog("ParlaSeNvda: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None Then Return
            If programmadisattivato Then Return
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If GetForegroundWindow() <> Me.Handle Then Return
            If InModalitaWordAttiva() Then Return
            If ControlloInCima() IsNot Nothing Then Return
            If EsistonoAtteseAttive() Then Return
            If ContaControlli() > 0 Then
                MostraControlloInCima()
                If ControlloInCima() IsNot Nothing Then Return
            End If
            If ETastoFreccia(tasto) Then
                ParlaCaricamentoInCorso()
            End If
            If ETastoUtile(tasto) Then
                tasto = Keys.None
            End If
        Catch ex As Exception
            _log.ScriviLog("_tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Function ETastoFreccia(tasto As Keys) As Boolean
        Try
            Dim codice As Keys = tasto And Keys.KeyCode
            Select Case codice
                Case Keys.Left, Keys.Right, Keys.Up, Keys.Down
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            _log.ScriviLog("ETastoFreccia: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ParlaCaricamentoInCorso()
        Try
            If _lettura Is Nothing Then
                _lettura = sintesivocale.GetInstance()
            End If
            If _lettura IsNot Nothing Then
                _lettura.LeggiTesto("Caricamento in corso")
            End If
        Catch ex As Exception
            _log.ScriviLog("ParlaCaricamentoInCorso: " & ex.Message)
        End Try
    End Sub

    Private Function InModalitaWordAttiva() As Boolean
        Try
            Dim cima As Control = ControlloInCima()
            Dim campo As WGCampo = TryCast(cima, WGCampo)
            If campo IsNot Nothing AndAlso campo.InModalitaWord Then Return True
            Return False
        Catch ex As Exception
            _log.ScriviLog("InModalitaWordAttiva: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ETastoUtile(tasto As Keys) As Boolean
        Try
            Dim codice As Keys = tasto And Keys.KeyCode
            Select Case codice
                Case Keys.None, Keys.ShiftKey, Keys.ControlKey, Keys.Menu, Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin, Keys.Capital, Keys.NumLock, Keys.Scroll
                    Return False
                Case Else
                    Return True
            End Select
        Catch ex As Exception
            _log.ScriviLog("ETastoUtile: " & ex.Message)
            Return False
        End Try
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                SyncLock _lockIstanza
                    If _istanza Is Me Then
                        _istanza = Nothing
                    End If
                End SyncLock

                If _vistaAttesa IsNot Nothing AndAlso Not _vistaAttesa.IsDisposed Then
                    _vistaAttesa.Dispose()
                    _vistaAttesa = Nothing
                End If

                Try
                    If _hwndPannelloVideo <> IntPtr.Zero Then
                        DestroyWindow(_hwndPannelloVideo)
                        _hwndPannelloVideo = IntPtr.Zero
                    End If
                    If _hwndSottotitolo <> IntPtr.Zero Then
                        DestroyWindow(_hwndSottotitolo)
                        _hwndSottotitolo = IntPtr.Zero
                    End If
                    If _hwndBarraProgresso <> IntPtr.Zero Then
                        DestroyWindow(_hwndBarraProgresso)
                        _hwndBarraProgresso = IntPtr.Zero
                    End If
                    If _hwndTestoAssistente <> IntPtr.Zero Then
                        DestroyWindow(_hwndTestoAssistente)
                        _hwndTestoAssistente = IntPtr.Zero
                    End If
                    If _timerTestoAssistente IsNot Nothing Then
                        _timerTestoAssistente.Stop()
                        RemoveHandler _timerTestoAssistente.Tick, AddressOf TestoAssistenteScaduto
                        _timerTestoAssistente.Dispose()
                        _timerTestoAssistente = Nothing
                    End If
                    If _fontTestoAssistente IsNot Nothing Then
                        _fontTestoAssistente.Dispose()
                        _fontTestoAssistente = Nothing
                    End If
                    If _brushSfondoPannello <> IntPtr.Zero Then
                        DeleteObject(_brushSfondoPannello)
                        _brushSfondoPannello = IntPtr.Zero
                    End If
                    If _timerPuliziaSottotitolo IsNot Nothing Then
                        _timerPuliziaSottotitolo.Stop()
                        RemoveHandler _timerPuliziaSottotitolo.Tick, AddressOf PuliziaSottotitoloTick
                        _timerPuliziaSottotitolo.Dispose()
                        _timerPuliziaSottotitolo = Nothing
                    End If
                Catch exPannello As Exception
                    _log.ScriviLog("Dispose (pannello video): " & exPannello.Message)
                End Try

                Try
                    If _tmrGuardiaFocus IsNot Nothing Then
                        _tmrGuardiaFocus.Stop()
                        RemoveHandler _tmrGuardiaFocus.Tick, AddressOf GuardiaFocusTick
                        _tmrGuardiaFocus.Dispose()
                        _tmrGuardiaFocus = Nothing
                    End If
                Catch exGuardia As Exception
                    _log.ScriviLog("Dispose (timer guardia focus): " & exGuardia.Message)
                End Try

                Try
                    If _tmrImpediscaStandby IsNot Nothing Then
                        _tmrImpediscaStandby.Stop()
                        RemoveHandler _tmrImpediscaStandby.Tick, AddressOf ImpediscaStandbyTick
                        _tmrImpediscaStandby.Dispose()
                        _tmrImpediscaStandby = Nothing
                    End If
                    SetThreadExecutionState(ES_CONTINUOUS)
                Catch exStandby As Exception
                    _log.ScriviLog("Dispose (timer anti-standby): " & exStandby.Message)
                End Try

                Try
                    If _tmrRivelazione IsNot Nothing Then
                        _tmrRivelazione.Stop()
                        RemoveHandler _tmrRivelazione.Tick, AddressOf RivelazioneTick
                        _tmrRivelazione.Dispose()
                        _tmrRivelazione = Nothing
                    End If
                Catch exRivelazione As Exception
                    _log.ScriviLog("Dispose (timer rivelazione): " & exRivelazione.Message)
                End Try

                Try
                    Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
                    If gestoreTouch IsNot Nothing Then
                        RemoveHandler gestoreTouch.OrientamentoCambiato, AddressOf OnOrientamentoCambiato
                        gestoreTouch.IdentificaElemento = Nothing
                        gestoreTouch.ZonaDocumento = Nothing
                        gestoreTouch.Disattiva()
                    End If
                Catch exTouch As Exception
                    _log.ScriviLog("Dispose (Touch): " & exTouch.Message)
                End Try

                FermaSuonoAttesa()
                FermaLetturaAttesa()
                FermaLetturaAttesaVuota()
            End If
        Catch ex As Exception
            _log.ScriviLog("Dispose: " & ex.Message)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Shared _finestraMinimizzata As Boolean = False
    Private Shared ReadOnly _lockFinestraMinimizzata As New Object()

    Public Shared Property FinestraMinimizzata As Boolean
        Get
            SyncLock _lockFinestraMinimizzata
                Return _finestraMinimizzata
            End SyncLock
        End Get
        Set(value As Boolean)
            SyncLock _lockFinestraMinimizzata
                _finestraMinimizzata = value
            End SyncLock
        End Set
    End Property

    Private Shared ReadOnly NOME_EVENTO_GUARDIA_SOSPESA_DAL_SERVIZIO As String = "WGGuardiaFocusSospesaDaServizio"
    Private Shared _eventoGuardiaSospesaDalServizio As EventWaitHandle = Nothing
    Private Shared ReadOnly _lockEventoServizio As New Object()

    Private Shared Function OttieniEventoGuardiaServizio() As EventWaitHandle
        Try
            SyncLock _lockEventoServizio
                If _eventoGuardiaSospesaDalServizio Is Nothing Then
                    Dim creatoOra As Boolean
                    _eventoGuardiaSospesaDalServizio = New EventWaitHandle(False, EventResetMode.ManualReset, NOME_EVENTO_GUARDIA_SOSPESA_DAL_SERVIZIO, creatoOra)
                End If
                Return _eventoGuardiaSospesaDalServizio
            End SyncLock
        Catch ex As Exception
            _logShared.ScriviLog("OttieniEventoGuardiaServizio: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Function GuardiaSospesaDalServizio() As Boolean
        Try
            Dim ev As EventWaitHandle = OttieniEventoGuardiaServizio()
            If ev Is Nothing Then Return False
            Return ev.WaitOne(0)
        Catch ex As Exception
            _logShared.ScriviLog("GuardiaSospesaDalServizio: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub SegnalaRipresaManualeDalServizio()
        Try
            Dim ev As EventWaitHandle = OttieniEventoGuardiaServizio()
            If ev IsNot Nothing Then ev.Reset()
        Catch ex As Exception
            _logShared.ScriviLog("SegnalaRipresaManualeDalServizio: " & ex.Message)
        End Try
    End Sub

    Public Shared Property GuardiaFocusSospesa As Boolean
        Get
            SyncLock _lockGuardiaFocus
                If _guardiaFocusSospesa Then Return True
            End SyncLock
            Return GuardiaSospesaDalServizio()
        End Get
        Set(value As Boolean)
            SyncLock _lockGuardiaFocus
                _guardiaFocusSospesa = value
            End SyncLock
        End Set
    End Property

    Public Shared Sub SospendiGuardiaFocus()
        GuardiaFocusSospesa = True
    End Sub

    Public Shared Sub RiprendiGuardiaFocus()
        GuardiaFocusSospesa = False
        Try
            Dim istanzaCorrente As WGContenitore = Istanza
            If istanzaCorrente IsNot Nothing Then
                istanzaCorrente.AggiornaVistaAttesa()
            End If
        Catch ex As Exception
            _logShared.ScriviLog("RiprendiGuardiaFocus: " & ex.Message)
        End Try
    End Sub

    Friend Class VistaAttesa
        Inherits Control

        Private ReadOnly _logVista As New Logger()
        Private _testo As String = ""

        Public Sub New()
            Try
                Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
                Me.DoubleBuffered = True
                Me.BackColor = WGTema.Sfondo
                Me.ForeColor = WGTema.Testo
                Me.TabStop = False
                AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            Catch ex As Exception
                _logVista.ScriviLog("VistaAttesa.New: " & ex.Message)
            End Try
        End Sub

        Private Sub OnCambiatoTema(sender As Object, e As EventArgs)
            Try
                If Me.IsDisposed Then Return
                If Me.InvokeRequired Then
                    Me.BeginInvoke(New MethodInvoker(Sub() OnCambiatoTema(sender, e)))
                    Return
                End If
                Me.BackColor = WGTema.Sfondo
                Me.ForeColor = WGTema.Testo
                Me.Invalidate()
            Catch ex As Exception
                _logVista.ScriviLog("VistaAttesa.OnCambiatoTema: " & ex.Message)
            End Try
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            Try
                RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            Catch ex As Exception
                _logVista.ScriviLog("VistaAttesa.Dispose: " & ex.Message)
            End Try
            MyBase.Dispose(disposing)
        End Sub

        Public Property Testo As String
            Get
                Try
                    Return _testo
                Catch ex As Exception
                    _logVista.ScriviLog("VistaAttesa.Testo Get: " & ex.Message)
                    Return ""
                End Try
            End Get
            Set(value As String)
                Try
                    _testo = value
                    If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                        If Me.InvokeRequired Then
                            Me.Invoke(New MethodInvoker(AddressOf RidisegnaInterno))
                        Else
                            RidisegnaInterno()
                        End If
                    End If
                Catch ex As Exception
                    _logVista.ScriviLog("VistaAttesa.Testo Set: " & ex.Message)
                End Try
            End Set
        End Property

        Private Sub RidisegnaInterno()
            Try
                Me.Invalidate()
            Catch ex As Exception
                _logVista.ScriviLog("VistaAttesa.RidisegnaInterno: " & ex.Message)
            End Try
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Try
                MyBase.OnPaint(e)
                e.Graphics.Clear(Me.BackColor)

                If String.IsNullOrEmpty(_testo) Then Return

                Dim larghezza As Integer = Me.ClientSize.Width - 20
                Dim altezza As Integer = Me.ClientSize.Height - 20
                If larghezza < 1 Then larghezza = 1
                If altezza < 1 Then altezza = 1

                Dim rettangolo As New RectangleF(10, 10, larghezza, altezza)

                Using formato As New StringFormat()
                    formato.Alignment = StringAlignment.Center
                    formato.LineAlignment = StringAlignment.Center
                    formato.Trimming = StringTrimming.Word

                    Using carattere As New Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Point)
                        Using pennello As New SolidBrush(Me.ForeColor)
                            e.Graphics.DrawString(_testo, carattere, pennello, rettangolo, formato)
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                _logVista.ScriviLog("VistaAttesa.OnPaint: " & ex.Message)
            End Try
        End Sub
    End Class
End Class