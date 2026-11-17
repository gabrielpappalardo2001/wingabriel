Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Timers
Imports System.Windows.Forms
Imports System.Xml.Linq

' Utilità generali per lo stato dell'applicazione, blocco del mouse, finestre di dialogo, indicatori di attesa e aggiornamenti.
Public Module WGUtils

    Public programmadisattivato As Boolean = False
    Private ReadOnly _scrittoreLogMod As New Logger()

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Function DeleteFileW(lpFileName As String) As Boolean
    End Function

    Public Sub RimuoviMarkOfTheWeb(percorsoFile As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) Then Return
            If Not File.Exists(percorsoFile) Then Return

            Dim percorsoStream As String = percorsoFile & ":Zone.Identifier"

            If Not DeleteFileW(percorsoStream) Then
                Dim codiceErrore As Integer = Marshal.GetLastWin32Error()

                If codiceErrore <> 2 AndAlso codiceErrore <> 3 Then
                End If
            End If
        Catch ex As Exception
            _scrittoreLogMod.ScriviLog($"Errore RimuoviMarkOfTheWeb per {percorsoFile}: {ex.Message}")
        End Try
    End Sub

    Public Sub uscita()
        Try
            programmadisattivato = True

            Try
                WGMouseLock.Riattiva()
            Catch ex As Exception
                _scrittoreLogMod.ScriviLog($"Errore riattivazione mouse uscita: {ex.Message}")
            End Try

            Try
                Dim sv As sintesivocale = sintesivocale.GetInstance()
                If sv IsNot Nothing Then
                    sv.FermaVoce()
                End If
            Catch ex As Exception
                _scrittoreLogMod.ScriviLog($"Errore arresto voce uscita: {ex.Message}")
            End Try

            WGStatoApplicazione.ChiusuraRegolare()

            Task.Run(Sub()
                         Thread.Sleep(200)
                         Environment.Exit(0)
                     End Sub)
        Catch ex As Exception
            _scrittoreLogMod.ScriviLog($"Errore uscita: {ex.Message}")
            Task.Run(Sub()
                         Thread.Sleep(200)
                         Environment.Exit(0)
                     End Sub)
        End Try
    End Sub

    Public Class WGStatoApplicazione
        Private Shared mutexApp As Mutex
        Private Shared eventoErrore As EventWaitHandle
        Private Shared ReadOnly _scrittoreLog As New Logger()

        Public Shared Sub AvviaMonitoraggio()
            Try
                Dim createdNew As Boolean
                mutexApp = New Mutex(True, "WGApplicazioneInEsecuzione", createdNew)
                eventoErrore = New EventWaitHandle(False, EventResetMode.ManualReset, "WGApplicazioneErroreCritico")
                eventoErrore.Reset()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore AvviaMonitoraggio: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub SegnalaCrasheTermina()
            Try
                If eventoErrore IsNot Nothing Then eventoErrore.Set()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore SegnalaCrasheTermina: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub ChiusuraRegolare()
            Try
                If mutexApp IsNot Nothing Then
                    mutexApp.ReleaseMutex()
                    mutexApp.Dispose()
                End If
                If eventoErrore IsNot Nothing Then
                    eventoErrore.Dispose()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore ChiusuraRegolare: {ex.Message}")
            End Try
        End Sub
    End Class

    Public Class WGMouseLock

        Private Shared ReadOnly _scrittoreLog As New Logger()
        Private Shared ReadOnly _lock As New Object()
        Private Shared _config As WGConfig

        Private Shared ReadOnly Property Config As WGConfig
            Get
                If _config Is Nothing Then
                    SyncLock _lock
                        If _config Is Nothing Then
                            Try
                                _config = New WGConfig()
                            Catch ex As Exception
                                _scrittoreLog.ScriviLog($"WGMouseLock: impossibile inizializzare WGConfig: {ex.Message}")
                            End Try
                        End If
                    End SyncLock
                End If
                Return _config
            End Get
        End Property

        Public Shared ReadOnly Property MouseDisattivato As Boolean
            Get
                Try
                    Dim tastiera As WGTastiera = WGTastiera.GetInstance()
                    If tastiera Is Nothing Then Return False
                    Return tastiera.BloccoMouseAttivo
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"MouseDisattivato Get: {ex.Message}")
                    Return False
                End Try
            End Get
        End Property

        Public Shared Sub AttivaMouse()
            Try
                Disattiva()
                Try
                    If Config IsNot Nothing Then
                        Config.AggiungiValore("attivamous", "true")
                    End If
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"WGMouseLock.AttivaMouse: errore salvataggio config: {ex.Message}")
                End Try
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore WGMouseLock.AttivaMouse: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub DisattivaMouse()
            Try
                Riattiva()
                Try
                    If Config IsNot Nothing Then
                        Config.AggiungiValore("attivamous", "false")
                    End If
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"WGMouseLock.DisattivaMouse: errore salvataggio config: {ex.Message}")
                End Try
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore WGMouseLock.DisattivaMouse: {ex.Message}")
            End Try
        End Sub

        Public Shared Function LeggiStatoDaConfig() As Boolean
            Try
                If Config Is Nothing Then Return False
                Dim val As String = Config.LeggiValore("attivamous")
                Return String.Equals(val, "true", StringComparison.OrdinalIgnoreCase)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"WGMouseLock.LeggiStatoDaConfig: {ex.Message}")
                Return False
            End Try
        End Function

        Public Shared Sub ApplicaStatoDaConfig()
            Try
                If LeggiStatoDaConfig() Then
                    Disattiva()
                Else
                    Riattiva()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"WGMouseLock.ApplicaStatoDaConfig: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub Disattiva()
            Try
                Dim tastiera As WGTastiera = WGTastiera.GetInstance()
                If tastiera IsNot Nothing Then
                    tastiera.AttivaBloccoMouse()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore WGMouseLock.Disattiva: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub Riattiva()
            Try
                Dim tastiera As WGTastiera = WGTastiera.GetInstance()
                If tastiera IsNot Nothing Then
                    tastiera.DisattivaBloccoMouse()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore WGMouseLock.Riattiva: {ex.Message}")
            End Try
        End Sub

    End Class

    Public Class WGSpinnerOverlay
        Inherits Form

        Private Shared ReadOnly _scrittoreLog As New Logger()
        Private Shared ReadOnly _lock As New Object()
        Private Shared _istanza As WGSpinnerOverlay

        Private Const GWL_EXSTYLE As Integer = -20
        Private Const WS_EX_NOACTIVATE As Integer = &H8000000
        Private Const WS_EX_TOOLWINDOW As Integer = &H80

        Private WithEvents _timerAnimazione As System.Windows.Forms.Timer
        Private _angolo As Single = 0

        Private Sub New()
            Try
                Me.FormBorderStyle = FormBorderStyle.None
                Me.ShowInTaskbar = False
                Me.StartPosition = FormStartPosition.Manual
                Me.TopMost = True
                Me.BackColor = Color.Black
                Me.Opacity = 0.55
                Me.DoubleBuffered = True
                Me.ControlBox = False
                Me.Cursor = Cursors.Default
                Me.Text = "WGSpinnerOverlay"

                _timerAnimazione = New System.Windows.Forms.Timer
                _timerAnimazione.Interval = 40
                AddHandler _timerAnimazione.Tick, AddressOf TimerAnimazione_Tick
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore costruttore WGSpinnerOverlay: {ex.Message}")
            End Try
        End Sub

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Try
                    Dim cp As CreateParams = MyBase.CreateParams
                    cp.ExStyle = cp.ExStyle Or WS_EX_NOACTIVATE Or WS_EX_TOOLWINDOW
                    Return cp
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore CreateParams WGSpinnerOverlay: {ex.Message}")
                    Return MyBase.CreateParams
                End Try
            End Get
        End Property

        Private Sub TimerAnimazione_Tick(sender As Object, e As EventArgs)
            Try
                _angolo += 18
                If _angolo >= 360 Then _angolo -= 360
                Me.Invalidate()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore TimerAnimazione_Tick WGSpinnerOverlay: {ex.Message}")
            End Try
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Try
                MyBase.OnPaint(e)
                Dim g As Graphics = e.Graphics
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

                Dim dimensione As Integer = 90
                Dim cx As Integer = Me.ClientSize.Width \ 2
                Dim cy As Integer = Me.ClientSize.Height \ 2
                Dim rect As New Rectangle(cx - dimensione \ 2, cy - dimensione \ 2, dimensione, dimensione)

                Using penSfondo As New Pen(Color.FromArgb(70, 255, 255, 255), 8)
                    g.DrawEllipse(penSfondo, rect)
                End Using

                Using penArco As New Pen(Color.White, 8)
                    penArco.StartCap = Drawing2D.LineCap.Round
                    penArco.EndCap = Drawing2D.LineCap.Round
                    g.DrawArc(penArco, rect, _angolo, 100)
                End Using

                Using fontTesto As New Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Point)
                    Dim testo As String = "Caricamento in corso..."
                    Dim misura As SizeF = g.MeasureString(testo, fontTesto)
                    Dim xTesto As Single = cx - misura.Width / 2
                    Dim yTesto As Single = cy + dimensione \ 2 + 16
                    Using pennelloTesto As New SolidBrush(Color.FromArgb(210, 255, 255, 255))
                        g.DrawString(testo, fontTesto, pennelloTesto, xTesto, yTesto)
                    End Using
                End Using
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore OnPaint WGSpinnerOverlay: {ex.Message}")
            End Try
        End Sub

        Private Shared Function OttieniIstanza() As WGSpinnerOverlay
            Try
                If _istanza Is Nothing OrElse _istanza.IsDisposed Then
                    SyncLock _lock
                        If _istanza Is Nothing OrElse _istanza.IsDisposed Then
                            _istanza = New WGSpinnerOverlay()
                        End If
                    End SyncLock
                End If
                Return _istanza
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"OttieniIstanza WGSpinnerOverlay: {ex.Message}")
                Return Nothing
            End Try
        End Function

        Public Shared Sub Mostra()
            Try
                Dim overlay As WGSpinnerOverlay = OttieniIstanza()

                Dim azione As Action = Sub()
                                           Try
                                               Dim area As Rectangle = Screen.PrimaryScreen.Bounds
                                               Dim formAttivo As Form = Nothing
                                               Try
                                                   formAttivo = WGContenitore.Istanza
                                               Catch ex As Exception
                                                   _scrittoreLog.ScriviLog($"WGSpinnerOverlay.Mostra (formAttivo): {ex.Message}")
                                               End Try
                                               If formAttivo IsNot Nothing AndAlso Not formAttivo.IsDisposed AndAlso formAttivo.IsHandleCreated Then
                                                   area = Screen.FromControl(formAttivo).Bounds
                                               End If

                                               overlay.Bounds = area
                                               overlay._angolo = 0

                                               If Not overlay.Visible Then
                                                   overlay.Show()
                                               End If
                                               overlay.Bounds = area
                                               overlay.TopMost = True
                                               overlay.BringToFront()
                                               overlay._timerAnimazione.Start()
                                           Catch ex As Exception
                                               _scrittoreLog.ScriviLog($"Errore Mostra (azione) WGSpinnerOverlay: {ex.Message}")
                                           End Try
                                       End Sub

                If overlay.IsHandleCreated AndAlso overlay.InvokeRequired Then
                    overlay.BeginInvoke(azione)
                Else
                    azione.Invoke()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore Mostra WGSpinnerOverlay: {ex.Message}")
            End Try
        End Sub

        Public Shared Sub Nascondi()
            Try
                If _istanza Is Nothing OrElse _istanza.IsDisposed Then Return
                Dim overlay As WGSpinnerOverlay = _istanza

                Dim azione As Action = Sub()
                                           Try
                                               overlay._timerAnimazione.Stop()
                                               If overlay.Visible Then overlay.Hide()
                                           Catch ex As Exception
                                               _scrittoreLog.ScriviLog($"Errore Nascondi (azione) WGSpinnerOverlay: {ex.Message}")
                                           End Try
                                       End Sub

                If overlay.IsHandleCreated AndAlso overlay.InvokeRequired Then
                    overlay.BeginInvoke(azione)
                Else
                    azione.Invoke()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore Nascondi WGSpinnerOverlay: {ex.Message}")
            End Try
        End Sub
    End Class

    Public Class WGFormUtils
        Implements IDisposable

        Private Shared ReadOnly _lock As New Object()
        Private Shared _istanza As WGFormUtils
        Private Shared ReadOnly _scrittoreLog As New Logger()

        Private _ctsSuonoAttesa As CancellationTokenSource
        Private _taskSuonoAttesa As Task

        Private ReadOnly _percorsoFileSuono As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "waiting_sound.wav")
        Private _suonoInterrotto As Boolean = False

        Private Core As WGChore

        Public tastoGiaPremuto As Boolean = False
        Public escPremutoAltroControllo As Boolean
        Public enterPremutoAltroControllo As Boolean
        Public applicazioneGiaAvviata As Boolean

        Private WithEvents _idleSoundTimer As System.Windows.Forms.Timer

        Private _isIdleSoundPlaying As Boolean = False
        Private suonoAttesa As WGSuoni

        Private Sub New()
            Try
                Try
                    If WGContenitore.Istanza Is Nothing Then
                        Dim contenitorePrincipale As New WGContenitore()
                        contenitorePrincipale.Show()
                    End If
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore creazione WGContenitore in WGFormUtils.New: {ex.Message} {ex.StackTrace}")
                End Try

                WGStatoApplicazione.AvviaMonitoraggio()

                Dim tCore As New Thread(Sub()
                                            Core = New WGChore()
                                        End Sub)
                tCore.IsBackground = True
                tCore.Start()

                Dim tCentro As New Thread(Sub()
                                              WGCentroDiControllo.GetInstance.GetInstance()
                                          End Sub)
                tCentro.IsBackground = True
                tCentro.Start()

                Dim tSuoniInit As New Thread(Sub()
                                                 suonoAttesa = New WGSuoni()
                                                 suonoAttesa.Attesa()
                                             End Sub)
                tSuoniInit.IsBackground = True
                tSuoniInit.Start()

                _idleSoundTimer = New System.Windows.Forms.Timer()
                _idleSoundTimer.Interval = 1000
                AddHandler _idleSoundTimer.Tick, AddressOf IdleSoundTimer_Tick
                _idleSoundTimer.Start()

                Try
                    WGMouseLock.Disattiva()
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore disattivazione mouse iniziale: {ex.Message}")
                End Try
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in WGFormUtils.New: {ex.Message} {ex.StackTrace}")
            End Try
        End Sub

        Public Shared Function GetInstance() As WGFormUtils
            If _istanza Is Nothing Then
                SyncLock _lock
                    If _istanza Is Nothing Then
                        Try
                            _istanza = New WGFormUtils()
                        Catch ex As Exception
                            _scrittoreLog.ScriviLog($"FATAL: Impossibile creare l'istanza di WGFormUtils.{ex.ToString()}")
                            If Debugger.IsAttached Then Throw
                            Return Nothing
                        End Try
                    End If
                End SyncLock
            End If
            Return _istanza
        End Function

        Public Sub InterrompiSuonoAttesa()
            Try
                If _suonoInterrotto Then
                    _suonoInterrotto = True
                End If

                If suonoAttesa IsNot Nothing Then
                    suonoAttesa.Dispose()
                End If
                If _ctsSuonoAttesa IsNot Nothing Then
                    _ctsSuonoAttesa.Cancel()
                End If

            Catch ex As AggregateException
                _scrittoreLog.ScriviLog($"Errore (AggregateException) durante l'interruzione del suono di attesa: {ex.ToString()}")
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore generico durante l'interruzione del suono di attesa: {ex.ToString()}")
            Finally
                Try
                    If _ctsSuonoAttesa IsNot Nothing Then
                        _ctsSuonoAttesa.Dispose()
                        _ctsSuonoAttesa = Nothing
                    End If
                    _taskSuonoAttesa = Nothing
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore in Finally InterrompiSuonoAttesa: {ex.Message}")
                End Try
            End Try
        End Sub

        Private Sub IdleSoundTimer_Tick(sender As Object, e As System.EventArgs) Handles _idleSoundTimer.Tick
            Try
                If _isIdleSoundPlaying Then Return

                SyncLock _lock
                    Dim contenitoreAttivo As WGContenitore = WGContenitore.Istanza
                    If contenitoreAttivo IsNot Nothing AndAlso Not contenitoreAttivo.IsDisposed AndAlso contenitoreAttivo.ContaControlli() > 0 Then
                        InterrompiSuonoAttesa()
                        Return
                    End If

                    If programmadisattivato = False Then
                        _isIdleSoundPlaying = True

                        Dim tSuono As New Thread(Sub()
                                                     Try
                                                         If suonoAttesa IsNot Nothing Then
                                                             suonoAttesa.Attesa()
                                                         End If
                                                     Catch ex As Exception
                                                         _scrittoreLog.ScriviLog($"Errore thread suono: {ex.Message}")
                                                     Finally
                                                         _isIdleSoundPlaying = False
                                                     End Try
                                                 End Sub)
                        tSuono.IsBackground = True
                        tSuono.Start()
                    Else
                        InterrompiSuonoAttesa()
                    End If
                End SyncLock
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in IdleSoundTimer_Tick: {ex.Message} {ex.StackTrace}")
            End Try
        End Sub

        Public Sub Chiudi()
            Try
                Dispose()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in Chiudi: {ex.Message}")
            End Try
        End Sub

        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            Try
                If Not disposedValue Then
                    If disposing Then
                        InterrompiSuonoAttesa()
                        If _idleSoundTimer IsNot Nothing Then
                            _idleSoundTimer.Stop()
                            _idleSoundTimer.Dispose()
                            _idleSoundTimer = Nothing
                        End If
                    End If
                End If

                Try
                    WGMouseLock.Riattiva()
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore riattivazione mouse in Dispose: {ex.Message}")
                End Try

                If suonoAttesa IsNot Nothing Then
                    suonoAttesa.Dispose()
                End If
                disposedValue = True
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in Dispose: {ex.Message}")
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"WGFormUtils.Dispose: {ex.Message}")
            End Try
        End Sub
    End Class

    Public Class WGFormDialogs
        Implements IDisposable

        Private Shared ReadOnly _lockIstanza As New Object()
        Private Shared _istanza As WGFormDialogs
        Private Shared ReadOnly _scrittoreLog As New Logger()
        Private disposedValue As Boolean

        Public Shared ReadOnly Property Istanza As WGFormDialogs
            Get
                Try
                    If _istanza Is Nothing Then
                        SyncLock _lockIstanza
                            If _istanza Is Nothing Then
                                _istanza = New WGFormDialogs()
                            End If
                        End SyncLock
                    End If
                    Return _istanza
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"WGFormDialogs.Istanza: {ex.Message}")
                    Return Nothing
                End Try
            End Get
        End Property

        Public Shared Property GuardiaFocusSospesa As Boolean
            Get
                Try
                    Return WGContenitore.GuardiaFocusSospesa
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"WGFormDialogs.GuardiaFocusSospesa (Get): {ex.Message}")
                    Return False
                End Try
            End Get
            Set(value As Boolean)
                Try
                    WGContenitore.GuardiaFocusSospesa = value
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"WGFormDialogs.GuardiaFocusSospesa (Set): {ex.Message}")
                End Try
            End Set
        End Property

        Public Shared Sub SospendiGuardiaFocus()
            Try
                WGContenitore.SospendiGuardiaFocus()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"SospendiGuardiaFocus (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Shared Sub RiprendiGuardiaFocus()
            Try
                WGContenitore.RiprendiGuardiaFocus()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"RiprendiGuardiaFocus (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub AggiungiWgForm(controllo As Control)
            Try
                If controllo Is Nothing Then Return
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore Is Nothing OrElse contenitore.IsDisposed Then
                    Return
                End If
                contenitore.MostraControllo(controllo)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"AggiungiWgForm (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub RimuoviWGForm(controllo As Control)
            Try
                If controllo Is Nothing Then Return
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return
                contenitore.RimuoviControllo(controllo)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"RimuoviWGForm (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub AggiungiWGFormDialogo(controllo As Control)
            AggiungiWgForm(controllo)
        End Sub

        Public Sub RimuoviWGFormDialogo(controllo As Control)
            RimuoviWGForm(controllo)
        End Sub

        Public Function PuoRicevereInput(controllo As Control) As Boolean
            Try
                If controllo Is Nothing Then Return False
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False
                Return contenitore.PuoRicevereInput(controllo)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"PuoRicevereInput (compatibilità): {ex.Message}")
                Return False
            End Try
        End Function

        Public Function HaFinestreVisibili() As Boolean
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False
                Return contenitore.ContaControlli() > 0
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"HaFinestreVisibili (compatibilità): {ex.Message}")
                Return False
            End Try
        End Function

        Public Function HaFinestreAperte() As Boolean
            Return HaFinestreVisibili()
        End Function

        Public ReadOnly Property UltimoFormNelloStack As Form
            Get
                Try
                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                    If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return Nothing
                    Return contenitore
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"UltimoFormNelloStack (compatibilità): {ex.Message}")
                    Return Nothing
                End Try
            End Get
        End Property

        Public Sub ForzaPrimoPiano(hwnd As IntPtr)
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.PortaInPrimoPiano()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"ForzaPrimoPiano (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub PulisciApplicazioniDisposte()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.PulisciControlliDisposti()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"PulisciApplicazioniDisposte (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub NavigaFormSuccessivo()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.NavigaControlloSuccessivo()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"NavigaFormSuccessivo (compatibilità): {ex.Message}")
            End Try
        End Sub

        Public Sub NavigaFormPrecedente()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.NavigaControlloPrecedente()
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"NavigaFormPrecedente (compatibilità): {ex.Message}")
            End Try
        End Sub

        Protected Overridable Sub Dispose(disposing As Boolean)
            Try
                If Not disposedValue Then
                    disposedValue = True
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"WGFormDialogs.Dispose: {ex.Message}")
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                Dispose(disposing:=True)
                GC.SuppressFinalize(Me)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"WGFormDialogs.Dispose (pubblico): {ex.Message}")
            End Try
        End Sub
    End Class

    Friend Class WGChore
        Public Sub New()
        End Sub
    End Class

    ''' <summary>
    ''' Rappresenta una singola voce (file) del rilascio firmato.
    ''' </summary>
    Public Class WGVoceFileRemoto
        Public Property PercorsoRelativo As String = String.Empty
        Public Property Tipo As String = "normale" ' "normale" oppure "libmpv"
        Public Property DataModificaRemota As DateTime = DateTime.MinValue
        Public Property UrlRelativaX86 As String = String.Empty
        Public Property UrlRelativaX64 As String = String.Empty
    End Class

    ''' <summary>
    ''' Elenco completo (cartelle + file) del rilascio firmato.
    ''' </summary>
    Public Class WGElencoFileRemoto
        Public Property Cartelle As New List(Of String)
        Public Property Voci As New List(Of WGVoceFileRemoto)
    End Class

    ''' <summary>
    ''' Esito dell'operazione di scaricamento file mancanti/aggiornati.
    ''' </summary>
    Public Class WGRisultatoAggiornamentoFile
        Public Property Successo As Boolean = False
        Public Property Annullato As Boolean = False
        Public Property FileScaricati As Integer = 0
        Public Property FileSaltati As Integer = 0
        Public Property FileFalliti As Integer = 0
        Public Property Errori As New List(Of String)
    End Class

    Public Class WGAggiornatoreFile

        Private Shared ReadOnly _scrittoreLog As New Logger()
        Private Shared ReadOnly _lockEsecuzione As New Object()

        Private Const APP_NOME As String = "wingabriel"
        ' Indirizzo della cartella con i file del rilascio scelto (lo imposta OttieniElencoFileRemoto).
        Private Shared _urlBaseFile As String = ""

        Private Const NUMERO_MASSIMO_DOWNLOAD_PARALLELI As Integer = 6

        Private Shared ReadOnly _httpClient As HttpClient = CreaHttpClient()

        Private Shared _fileTotali As Integer = 0
        Private Shared _fileCompletati As Integer = 0

        Private Shared Function CreaHttpClient() As HttpClient
            Try
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
                ServicePointManager.Expect100Continue = False
                ServicePointManager.UseNagleAlgorithm = False
                ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, NUMERO_MASSIMO_DOWNLOAD_PARALLELI * 2)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore configurazione ServicePointManager: {ex.Message}")
            End Try

            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromMinutes(10)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Updater/1.0")
            Return client
        End Function

        ''' <summary>
        ''' Percentuale corrente (0-100) dell'operazione di aggiornamento in corso.
        ''' Calcolata sui contatori reali dei file completati, quindi precisa.
        ''' Se non c'è alcuna operazione in corso restituisce 100.
        ''' </summary>
        Public Shared ReadOnly Property PercentualeCorrente As Integer
            Get
                Try
                    Dim totale As Integer = Interlocked.CompareExchange(_fileTotali, 0, 0)
                    Dim completati As Integer = Interlocked.CompareExchange(_fileCompletati, 0, 0)
                    If totale <= 0 Then Return 100
                    Dim percentuale As Integer = CInt(Math.Floor((completati / CDbl(totale)) * 100))
                    Return Math.Min(Math.Max(percentuale, 0), 100)
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore lettura PercentualeCorrente: {ex.Message}")
                    Return 0
                End Try
            End Get
        End Property

        ''' <summary>
        ''' Scarica tutti i file mancanti o modificati rispetto a quanto presente sul
        ''' server (in base alla data di ultima modifica) creando le cartelle
        ''' necessarie. Gestisce separatamente i file libmpv (x86/x64). Chiamata
        ''' bloccante: ritorna solo a operazione conclusa (o annullata).
        ''' </summary>
        ''' <param name="cartellaInstallazione">Cartella dell'applicazione. Se non specificata viene usata la cartella dell'eseguibile corrente.</param>
        ''' <param name="tokenAnnullamento">Token opzionale per annullare l'operazione in corso.</param>
        Public Shared Function ScaricaFileMancantiEAggiornati(Optional cartellaInstallazione As String = Nothing,
                                                                Optional tokenAnnullamento As CancellationToken = Nothing) As WGRisultatoAggiornamentoFile
            Dim risultato As New WGRisultatoAggiornamentoFile()

            ' Un solo aggiornamento alla volta: eventuali altre chiamate restano
            ' bloccate qui finché quella in corso non è terminata.
            SyncLock _lockEsecuzione
                Try
                    If String.IsNullOrWhiteSpace(cartellaInstallazione) Then
                        cartellaInstallazione = AppDomain.CurrentDomain.BaseDirectory
                    End If

                    Interlocked.Exchange(_fileTotali, 0)
                    Interlocked.Exchange(_fileCompletati, 0)

                    Dim elenco As WGElencoFileRemoto = OttieniElencoFileRemoto()
                    If elenco Is Nothing OrElse (elenco.Voci.Count = 0 AndAlso elenco.Cartelle.Count = 0) Then
                        risultato.Successo = True
                        Return risultato
                    End If

                    ' Tiene solo i file/cartelle x86 oppure x64 compatibili con il sistema
                    ' operativo corrente: se il PC è a 64 bit ignora completamente
                    ' l'eventuale cartella x86 (anche se presente localmente da
                    ' un'installazione precedente) e viceversa. I file libmpv non sono
                    ' toccati qui: la loro architettura è già scelta in ScaricaVoceFile.
                    FiltraVociPerArchitettura(elenco)

                    CreaCartelleMancanti(cartellaInstallazione, elenco)

                    Dim daScaricare As List(Of WGVoceFileRemoto) =
                        elenco.Voci.Where(Function(v) FileDaAggiornare(cartellaInstallazione, v)).ToList()

                    risultato.FileSaltati = elenco.Voci.Count - daScaricare.Count

                    If daScaricare.Count = 0 Then
                        risultato.Successo = True
                        Return risultato
                    End If

                    Interlocked.Exchange(_fileTotali, daScaricare.Count)

                    Dim scaricatiOk As Integer = 0
                    Dim falliti As Integer = 0
                    Dim erroriSync As New List(Of String)
                    Dim lockErrori As New Object()

                    Dim opzioniParallelo As New ParallelOptions() With {
                        .MaxDegreeOfParallelism = NUMERO_MASSIMO_DOWNLOAD_PARALLELI,
                        .CancellationToken = tokenAnnullamento
                    }

                    Try
                        Parallel.ForEach(daScaricare, opzioniParallelo,
                            Sub(voce)
                                Try
                                    tokenAnnullamento.ThrowIfCancellationRequested()
                                    ScaricaVoceFile(cartellaInstallazione, voce)
                                    Interlocked.Increment(scaricatiOk)
                                Catch exAnnulla As OperationCanceledException
                                    Throw
                                Catch ex As Exception
                                    Interlocked.Increment(falliti)
                                    SyncLock lockErrori
                                        erroriSync.Add($"{voce.PercorsoRelativo}: {ex.Message}")
                                    End SyncLock
                                    _scrittoreLog.ScriviLog($"Errore download {voce.PercorsoRelativo}: {ex.Message}")
                                Finally
                                    Interlocked.Increment(_fileCompletati)
                                End Try
                            End Sub)
                    Catch exAnnullaEsterno As OperationCanceledException
                        risultato.Annullato = True
                    Catch exAgg As AggregateException
                        If exAgg.InnerExceptions.Any(Function(x) TypeOf x Is OperationCanceledException) Then
                            risultato.Annullato = True
                        Else
                            Throw
                        End If
                    End Try

                    risultato.FileScaricati = scaricatiOk
                    risultato.FileFalliti = falliti
                    risultato.Errori = erroriSync
                    risultato.Successo = Not risultato.Annullato AndAlso falliti = 0
                Catch ex As Exception
                    _scrittoreLog.ScriviLog($"Errore ScaricaFileMancantiEAggiornati: {ex.Message} {ex.StackTrace}")
                    risultato.Successo = False
                    risultato.Errori.Add(ex.Message)
                Finally
                    Interlocked.Exchange(_fileTotali, 0)
                    Interlocked.Exchange(_fileCompletati, 0)
                End Try
            End SyncLock

            Return risultato
        End Function

        ''' <summary>L'elenco dei file del rilascio firmato (stabile o beta, secondo l'impostazione). Solo i file di quella versione: del resto che sta sul sito non si scarica niente.</summary>
        Private Shared Function OttieniElencoFileRemoto() As WGElencoFileRemoto
            Dim elenco As New WGElencoFileRemoto()
            Try
                Dim canale As String = RilascioFirmato.NormalizzaCanale(New WGConfig().LeggiValore("canale_aggiornamenti"))
                Dim errore As String = ""
                Dim manifest As RilascioFirmato.Manifest = RilascioFirmato.Leggi(APP_NOME, canale, errore)
                If manifest Is Nothing Then
                    _scrittoreLog.ScriviLog($"OttieniElencoFileRemoto: nessun rilascio valido ({errore})")
                    Return elenco
                End If
                _urlBaseFile = manifest.UrlBaseFile
                For Each c As String In manifest.Cartelle
                    If Not String.IsNullOrWhiteSpace(c) AndAlso Not c.Contains("..") Then elenco.Cartelle.Add(c)
                Next
                For Each v As RilascioFirmato.VoceManifest In manifest.Voci
                    If String.IsNullOrWhiteSpace(v.Percorso) OrElse v.Percorso.Contains("..") OrElse RilascioFirmato.FileDaNonScaricare(v.Percorso) Then Continue For
                    Dim voce As New WGVoceFileRemoto()
                    voce.PercorsoRelativo = v.Percorso
                    Dim dataModifica As DateTime
                    If DateTime.TryParse(v.DataUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, dataModifica) Then voce.DataModificaRemota = dataModifica
                    If v.Libmpv Then
                        voce.Tipo = "libmpv"
                        voce.UrlRelativaX86 = v.UrlX86
                        voce.UrlRelativaX64 = v.UrlX64
                    End If
                    elenco.Voci.Add(voce)
                Next
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore OttieniElencoFileRemoto: {ex.Message} {ex.StackTrace}")
            End Try
            Return elenco
        End Function

        ''' <summary>
        ''' Rimuove dall'elenco remoto tutte le cartelle e i file "normali" che si
        ''' trovano dentro la cartella x86 o x64 non compatibile con il sistema
        ''' operativo corrente (es. le voci "x86/..." su un PC a 64 bit, o "x64/..."
        ''' su un PC a 32 bit). In questo modo non vengono mai scaricati né creati
        ''' file dell'architettura sbagliata, anche se sul server sono presenti
        ''' entrambe le cartelle. Le voci di tipo "libmpv" non sono toccate: la loro
        ''' architettura è già scelta correttamente in ScaricaVoceFile.
        ''' </summary>
        Private Shared Sub FiltraVociPerArchitettura(elenco As WGElencoFileRemoto)
            Try
                Dim cartellaArchSbagliata As String = If(Environment.Is64BitOperatingSystem, "x86", "x64")

                elenco.Cartelle = elenco.Cartelle.
                    Where(Function(c) Not PercorsoInCartella(c, cartellaArchSbagliata)).
                    ToList()

                elenco.Voci = elenco.Voci.
                    Where(Function(v) v.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) OrElse
                                       Not PercorsoInCartella(v.PercorsoRelativo, cartellaArchSbagliata)).
                    ToList()
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore FiltraVociPerArchitettura: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' Vero se percorsoRelativo coincide con nomeCartella oppure si trova al suo
        ''' interno (a qualsiasi profondità), es. "x86" o "x86/qualcosa/altro.dll"
        ''' rispetto a nomeCartella = "x86".
        ''' </summary>
        Private Shared Function PercorsoInCartella(percorsoRelativo As String, nomeCartella As String) As Boolean
            If String.IsNullOrEmpty(percorsoRelativo) Then Return False
            Dim normalizzato As String = percorsoRelativo.Replace("\"c, "/"c).TrimStart("/"c)
            Return normalizzato.Equals(nomeCartella, StringComparison.OrdinalIgnoreCase) OrElse
                   normalizzato.StartsWith(nomeCartella & "/", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Sub CreaCartelleMancanti(cartellaBase As String, elenco As WGElencoFileRemoto)
            Try
                For Each percorsoCartella As String In elenco.Cartelle
                    Try
                        Dim percorsoCompleto As String = Path.Combine(cartellaBase, percorsoCartella.Replace("/"c, Path.DirectorySeparatorChar))
                        If Not Directory.Exists(percorsoCompleto) Then
                            Directory.CreateDirectory(percorsoCompleto)
                        End If
                    Catch ex As Exception
                        _scrittoreLog.ScriviLog($"Errore creazione cartella {percorsoCartella}: {ex.Message}")
                    End Try
                Next

                For Each voce As WGVoceFileRemoto In elenco.Voci
                    If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then Continue For ' va nella radice, nessuna sottocartella
                    Try
                        Dim percorsoCompleto As String = OttieniPercorsoLocale(cartellaBase, voce)
                        Dim cartellaFile As String = Path.GetDirectoryName(percorsoCompleto)
                        If Not String.IsNullOrEmpty(cartellaFile) AndAlso Not Directory.Exists(cartellaFile) Then
                            Directory.CreateDirectory(cartellaFile)
                        End If
                    Catch ex As Exception
                        _scrittoreLog.ScriviLog($"Errore creazione cartella per file {voce.PercorsoRelativo}: {ex.Message}")
                    End Try
                Next
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore CreaCartelleMancanti: {ex.Message}")
            End Try
        End Sub

        Private Shared Function OttieniPercorsoLocale(cartellaBase As String, voce As WGVoceFileRemoto) As String
            If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                ' Il file libmpv va sempre nella cartella dell'applicazione, nome invariato.
                Return Path.Combine(cartellaBase, Path.GetFileName(voce.PercorsoRelativo))
            Else
                Return Path.Combine(cartellaBase, voce.PercorsoRelativo.Replace("/"c, Path.DirectorySeparatorChar))
            End If
        End Function

        Private Shared Function FileDaAggiornare(cartellaBase As String, voce As WGVoceFileRemoto) As Boolean
            Try
                Dim percorsoLocale As String = OttieniPercorsoLocale(cartellaBase, voce)
                Return Not File.Exists(percorsoLocale)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore FileDaAggiornare {voce.PercorsoRelativo}: {ex.Message}")
                Return True
            End Try
        End Function

        Private Shared Sub ScaricaVoceFile(cartellaBase As String, voce As WGVoceFileRemoto)
            Dim urlRelativa As String

            If voce.Tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase) Then
                urlRelativa = If(Environment.Is64BitOperatingSystem, voce.UrlRelativaX64, voce.UrlRelativaX86)
            Else
                urlRelativa = voce.PercorsoRelativo
            End If

            If String.IsNullOrWhiteSpace(urlRelativa) Then
                Throw New InvalidOperationException("URL di download non disponibile per la voce.")
            End If

            If String.IsNullOrEmpty(_urlBaseFile) Then Throw New InvalidOperationException("Indirizzo del rilascio non disponibile.")
            Dim urlCompleto As String = RilascioFirmato.UrlFile(_urlBaseFile, urlRelativa)
            Dim percorsoLocale As String = OttieniPercorsoLocale(cartellaBase, voce)
            Dim cartellaFile As String = Path.GetDirectoryName(percorsoLocale)
            If Not String.IsNullOrEmpty(cartellaFile) AndAlso Not Directory.Exists(cartellaFile) Then
                Directory.CreateDirectory(cartellaFile)
            End If

            Dim percorsoTemporaneo As String = percorsoLocale & ".wgtmp"

            Dim datiFile As Byte() = _httpClient.GetByteArrayAsync(urlCompleto).GetAwaiter().GetResult()
            File.WriteAllBytes(percorsoTemporaneo, datiFile)

            If File.Exists(percorsoLocale) Then
                File.Delete(percorsoLocale)
            End If
            File.Move(percorsoTemporaneo, percorsoLocale)
            WGUtils.RimuoviMarkOfTheWeb(percorsoLocale)
        End Sub
    End Class

    Public Class WGUtilsDataOra

        Private Shared ReadOnly _scrittoreLog As New Logger()
        Private Shared ReadOnly _regexIlNumero As New Regex("il (\w+)(?!di)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexTraGiorni As New Regex("tra (uno|due|tre|quattro|cinque|sei|sette|otto|nove|dieci|\d+) (giorni|giorno)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexTraSettimane As New Regex("tra (una|due|tre|quattro|\d+) (settimane|settimana)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexTraMesi As New Regex("tra (uno|due|tre|\d+) mesi", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexTraGiornoSettimana As New Regex("tra (uno|due|tre|quattro|cinque|sei|sette|otto|nove|dieci|\d+) (lunedì|martedì|mercoledì|giovedì|venerdì|sabato|domenica)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexGiornoSettimanaProssima As New Regex("(lunedì|martedì|mercoledì|giovedì|venerdì|sabato|domenica) prossimo", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexGiornoSettimanaScorso As New Regex("(lunedì|martedì|mercoledì|giovedì|venerdì|sabato|domenica) scorso", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexDataColloquiale As New Regex("il (\w+)(?: di)?(\w+)(?: (\d{4}))?", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexOrarioSoloOra As New Regex("le? (\d{1,2}|uno|una|due|tre|quattro|cinque|sei|sette|otto|nove|dieci|undici|dodici|tredici|quattordici|quindici|sedici|diciassette|diciotto|diciannove|venti|ventuno|ventidue|ventitre)(?! e)", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexOrarioCompleto As New Regex("le? (\w+)(?: (?:e) (\w+))?(?: (di|del))? (mattina|pomeriggio|sera|notte)?", RegexOptions.Compiled Or RegexOptions.IgnoreCase)
        Private Shared ReadOnly _regexOrarioMeno As New Regex("(?:le|la) (\w+)(?: meno )?(\w+)?", RegexOptions.Compiled Or RegexOptions.IgnoreCase)

        Public Function AnalizzaData(input As String, ByRef dataRisultato As DateTime?) As Boolean
            Try
                dataRisultato = Nothing
                If String.IsNullOrWhiteSpace(input) Then Return False
                Dim inputLower As String = input.Trim().ToLower()
                Dim oggi As DateTime = DateTime.Today

                Select Case inputLower
                    Case "oggi"
                        dataRisultato = oggi
                        Return True
                    Case "domani"
                        dataRisultato = oggi.AddDays(1)
                        Return True
                    Case "ieri"
                        dataRisultato = oggi.AddDays(-1)
                        Return True
                    Case "dopodomani", "domani l'altro"
                        dataRisultato = oggi.AddDays(2)
                        Return True
                    Case "settimana prossima"
                        Dim nextWeek As DateTime = oggi.AddDays(7)
                        dataRisultato = nextWeek
                        Return True
                    Case "mese prossimo"
                        dataRisultato = AddMonths(oggi, 1)
                        Return True
                    Case "anno prossimo"
                        dataRisultato = oggi.AddYears(1)
                        Return True
                    Case "anno scorso"
                        dataRisultato = oggi.AddYears(-1)
                        Return True
                    Case "verso la metà del mese"
                        dataRisultato = New DateTime(oggi.Year, oggi.Month, 15)
                        Return True
                    Case "gli ultimi giorni del mese"
                        dataRisultato = New DateTime(oggi.Year, oggi.Month, DateTime.DaysInMonth(oggi.Year, oggi.Month))
                        Return True
                End Select

                Dim matchIlNumero As Match = _regexIlNumero.Match(inputLower)
                If matchIlNumero.Success Then
                    Dim numeroGiornoStr As String = matchIlNumero.Groups(1).Value
                    Dim numeroGiorno As Integer = ParseNumeroColloquiale(numeroGiornoStr)
                    If numeroGiorno > 0 AndAlso numeroGiorno <= DateTime.DaysInMonth(oggi.Year, oggi.Month) Then
                        dataRisultato = New DateTime(oggi.Year, oggi.Month, numeroGiorno)
                        Return True
                    End If
                End If

                Dim matchTraGiorni As Match = _regexTraGiorni.Match(inputLower)
                If matchTraGiorni.Success Then
                    Dim numeroStr As String = matchTraGiorni.Groups(1).Value
                    Dim numero As Integer = ParseNumeroColloquiale(numeroStr)
                    If numero > 0 Then
                        dataRisultato = oggi.AddDays(numero)
                        Return True
                    End If
                End If

                Dim matchTraSettimane As Match = _regexTraSettimane.Match(inputLower)
                If matchTraSettimane.Success Then
                    Dim numeroStr As String = matchTraSettimane.Groups(1).Value
                    Dim numero As Integer = ParseNumeroColloquiale(numeroStr)
                    If numero > 0 Then
                        dataRisultato = oggi.AddDays(numero * 7)
                        Return True
                    End If
                End If

                Dim matchTraMesi As Match = _regexTraMesi.Match(inputLower)
                If matchTraMesi.Success Then
                    Dim numeroStr As String = matchTraMesi.Groups(1).Value
                    Dim numero As Integer = ParseNumeroColloquiale(numeroStr)
                    If numero > 0 Then
                        dataRisultato = AddMonths(oggi, numero)
                        Return True
                    End If
                End If

                Dim matchTraGiornoSettimana As Match = _regexTraGiornoSettimana.Match(inputLower)
                If matchTraGiornoSettimana.Success Then
                    Dim numeroStr As String = matchTraGiornoSettimana.Groups(1).Value
                    Dim giornoSettimanaStr As String = matchTraGiornoSettimana.Groups(2).Value
                    Dim numero As Integer = ParseNumeroColloquiale(numeroStr)
                    Dim targetDayOfWeek As DayOfWeek = ParseDayOfWeek(giornoSettimanaStr)

                    If numero > 0 AndAlso targetDayOfWeek <> CType(-1, DayOfWeek) Then
                        Dim dataCorrente As DateTime = oggi
                        Dim giorniDaAggiungere As Integer = ((targetDayOfWeek - dataCorrente.DayOfWeek + 7) Mod 7)
                        If giorniDaAggiungere = 0 Then
                            giorniDaAggiungere = 7
                        End If
                        dataRisultato = dataCorrente.AddDays(giorniDaAggiungere + (numero - 1) * 7)
                        Return True
                    End If
                End If

                Dim matchGiornoSettimanaProssima As Match = _regexGiornoSettimanaProssima.Match(inputLower)
                If matchGiornoSettimanaProssima.Success Then
                    Dim giornoSettimanaStr As String = matchGiornoSettimanaProssima.Groups(1).Value
                    Dim targetDayOfWeek As DayOfWeek = ParseDayOfWeek(giornoSettimanaStr)

                    If targetDayOfWeek <> CType(-1, DayOfWeek) Then
                        Dim giorniDaAggiungere As Integer = ((targetDayOfWeek - oggi.DayOfWeek + 7) Mod 7)
                        If giorniDaAggiungere = 0 Then
                            giorniDaAggiungere = 7
                        End If
                        dataRisultato = oggi.AddDays(giorniDaAggiungere)
                        Return True
                    End If
                End If

                Dim matchGiornoSettimanaScorso As Match = _regexGiornoSettimanaScorso.Match(inputLower)
                If matchGiornoSettimanaScorso.Success Then
                    Dim giornoSettimanaStr As String = matchGiornoSettimanaScorso.Groups(1).Value
                    Dim targetDayOfWeek As DayOfWeek = ParseDayOfWeek(giornoSettimanaStr)
                    If targetDayOfWeek <> CType(-1, DayOfWeek) Then
                        Dim giorniDaSottrarre As Integer = ((oggi.DayOfWeek - targetDayOfWeek + 7) Mod 7)
                        If giorniDaSottrarre = 0 Then
                            giorniDaSottrarre = 7
                        End If
                        dataRisultato = oggi.AddDays(-giorniDaSottrarre)
                        Return True
                    End If
                End If

                Dim matchDataColloquiale As Match = _regexDataColloquiale.Match(inputLower)
                If matchDataColloquiale.Success Then
                    Dim giornoStr As String = matchDataColloquiale.Groups(1).Value
                    Dim numeroGiorno As Integer = ParseNumeroColloquiale(giornoStr)
                    Dim nomeMese As String = matchDataColloquiale.Groups(2).Value
                    Dim anno As Integer = If(matchDataColloquiale.Groups(3).Success, Integer.Parse(matchDataColloquiale.Groups(3).Value), oggi.Year)
                    If numeroGiorno > 0 Then
                        Dim mese As Integer = NomeMeseToNumero(nomeMese)
                        If mese > 0 Then
                            Dim tempDate As DateTime
                            If DateTime.TryParseExact($"{numeroGiorno}/{mese}/{anno}", "d/M/yyyy", CultureInfo.CurrentCulture, DateTimeStyles.None, tempDate) Then
                                dataRisultato = tempDate
                                Return True
                            End If
                        End If
                    End If
                End If

                Dim dataTemp As DateTime
                If DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, dataTemp) Then
                    dataRisultato = dataTemp
                    Return True
                End If

                Return False
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in AnalizzaData: {ex.Message} {ex.StackTrace}")
                dataRisultato = Nothing
                Return False
            End Try
        End Function

        Public Function AnalizzaOrario(input As String, ByRef orarioRisultato As DateTime?) As Boolean
            Try
                orarioRisultato = Nothing
                If String.IsNullOrWhiteSpace(input) Then Return False
                Dim inputLower As String = input.Trim().ToLower()

                ' Le ore scritte in numeri: "9", "09", "6", "930", "0930", "9.30", "9,30", "9:30", "9 30".
                Dim soloNumeri As Match = Regex.Match(inputLower, "^(\d{1,2})(?:\s*[:.,\s]\s*(\d{1,2}))?$")
                If Not soloNumeri.Success Then soloNumeri = Regex.Match(inputLower, "^(\d{1,2})(\d{2})$")
                If soloNumeri.Success Then
                    Dim oraNum As Integer = Integer.Parse(soloNumeri.Groups(1).Value, CultureInfo.InvariantCulture)
                    Dim minNum As Integer = If(soloNumeri.Groups(2).Success AndAlso soloNumeri.Groups(2).Value <> "", Integer.Parse(soloNumeri.Groups(2).Value, CultureInfo.InvariantCulture), 0)
                    If oraNum >= 0 AndAlso oraNum <= 23 AndAlso minNum >= 0 AndAlso minNum <= 59 Then
                        orarioRisultato = DateTime.Today.AddHours(oraNum).AddMinutes(minNum)
                        Return True
                    End If
                End If

                Select Case inputLower
                    Case "a metà mattinata"
                        orarioRisultato = DateTime.Today.AddHours(10).AddMinutes(30)
                        Return True
                    Case "a metà pomeriggio"
                        orarioRisultato = DateTime.Today.AddHours(15).AddMinutes(30)
                        Return True
                    Case "sul far della sera"
                        orarioRisultato = DateTime.Today.AddHours(18).AddMinutes(30)
                        Return True
                    Case "dopo cena"
                        orarioRisultato = DateTime.Today.AddHours(21)
                        Return True
                    Case "a mezzogiorno"
                        orarioRisultato = DateTime.Today.AddHours(12).AddMinutes(0)
                        Return True
                    Case "a i tocco", "all'una"
                        orarioRisultato = DateTime.Today.AddHours(13).AddMinutes(0)
                        Return True
                    Case "a i tocco e mezzo", "all'una e mezza"
                        orarioRisultato = DateTime.Today.AddHours(13).AddMinutes(30)
                        Return True
                    Case "l'una spaccata"
                        orarioRisultato = DateTime.Today.AddHours(13).AddMinutes(0)
                        Return True
                    Case "a mezzanotte"
                        orarioRisultato = DateTime.Today
                        Return True
                    Case "le 24", "le ventiquattro"
                        orarioRisultato = DateTime.Today.AddDays(1)
                        Return True
                End Select

                Dim matchOrarioSoloOra As Match = _regexOrarioSoloOra.Match(inputLower)
                If matchOrarioSoloOra.Success Then
                    Dim oraStr As String = matchOrarioSoloOra.Groups(1).Value
                    Dim ora As Integer = ParseNumeroColloquiale(oraStr)
                    If ora >= 0 AndAlso ora <= 23 Then
                        orarioRisultato = DateTime.Today.AddHours(ora).AddMinutes(0)
                        Return True
                    End If
                End If

                Dim matchOrarioCompleto As Match = _regexOrarioCompleto.Match(inputLower)
                If matchOrarioCompleto.Success Then
                    Dim oraStr As String = matchOrarioCompleto.Groups(1).Value
                    Dim minutiStr As String = matchOrarioCompleto.Groups(2).Value
                    Dim ampm As String = matchOrarioCompleto.Groups(4).Value
                    Dim ora As Integer = ParseNumeroColloquiale(oraStr)
                    Dim minuti As Integer = If(String.IsNullOrEmpty(minutiStr), 0, ParseNumeroMinutiColloquiale(minutiStr))

                    If Not String.IsNullOrEmpty(ampm) Then
                        If ampm = "mattina" Then
                            If ora > 12 Then
                                ora -= 12
                            ElseIf ora = 12 Then
                                ora = 0
                            End If
                        ElseIf ampm = "pomeriggio" OrElse ampm = "sera" Then
                            If ora < 12 Then
                                ora += 12
                            End If
                        ElseIf ampm = "notte" Then
                            If ora > 12 Then
                                ora -= 12
                            ElseIf ora = 12 Then
                                ora = 0
                            End If
                        End If
                    End If

                    If ora >= 0 AndAlso ora <= 23 AndAlso minuti >= 0 AndAlso minuti <= 59 Then
                        orarioRisultato = DateTime.Today.AddHours(ora).AddMinutes(minuti)
                        Return True
                    End If
                End If

                Dim matchOrarioMeno As Match = _regexOrarioMeno.Match(inputLower)
                If matchOrarioMeno.Success Then
                    Dim oraStr As String = matchOrarioMeno.Groups(1).Value
                    Dim minutiMenoStr As String = matchOrarioMeno.Groups(2).Value
                    Dim ora As Integer = ParseNumeroColloquiale(oraStr)
                    Dim minutiMeno As Integer = ParseNumeroMinutiColloquiale(minutiMenoStr)
                    If ora > 0 AndAlso minutiMeno > 0 Then
                        orarioRisultato = DateTime.Today.AddHours(ora).AddMinutes(-minutiMeno)
                        Return True
                    End If
                End If

                Dim orarioTemp As DateTime
                If DateTime.TryParseExact(input, "H:mm", CultureInfo.CurrentCulture, DateTimeStyles.None, orarioTemp) Then
                    orarioRisultato = orarioTemp
                    Return True
                End If

                If DateTime.TryParse(input, CultureInfo.CurrentCulture, DateTimeStyles.None, orarioTemp) Then
                    orarioRisultato = orarioTemp
                    Return True
                End If

                Return False
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in AnalizzaOrario: {ex.Message} {ex.StackTrace}")
                orarioRisultato = Nothing
                Return False
            End Try
        End Function

        Public Function DaDataAStringa(data As DateTime) As String
            Try
                Dim oggi As DateTime = DateTime.Today

                If data.Date = oggi.Date Then
                    Return "oggi"
                ElseIf data.Date = oggi.AddDays(1).Date Then
                    Return "domani"
                ElseIf data.Date = oggi.AddDays(-1).Date Then
                    Return "ieri"
                ElseIf data.Date = oggi.AddDays(2).Date Then
                    Return "dopodomani"
                Else
                    Return $"il {data.Day} {NomeMeseDaNumero(data.Month)} {data.Year}"
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in DaDataAStringa: {ex.Message}")
                Return ""
            End Try
        End Function

        Public Function DaOrarioAStringa(orario As DateTime) As String
            Try
                If orario.Hour = 12 AndAlso orario.Minute = 0 Then
                    Return "a mezzogiorno"
                ElseIf orario.Hour = 13 AndAlso orario.Minute = 0 Then
                    Return "a i tocco"
                ElseIf orario.Hour = 13 AndAlso orario.Minute = 30 Then
                    Return "a i tocco e mezzo"
                ElseIf orario.Hour = 0 AndAlso orario.Minute = 0 Then
                    Return "a mezzanotte"
                ElseIf orario.Minute = 0 Then
                    Return $"alle {orario.Hour}"
                Else
                    Dim ampm As String = ""
                    If orario.Hour >= 6 AndAlso orario.Hour < 12 Then
                        ampm = "mattina"
                    ElseIf orario.Hour >= 12 AndAlso orario.Hour < 18 Then
                        ampm = "pomeriggio"
                    ElseIf orario.Hour >= 18 AndAlso orario.Hour < 22 Then
                        ampm = "sera"
                    Else
                        ampm = "notte"
                    End If
                    Return $"alle {orario.Hour} e {orario.Minute} di {ampm}"
                End If
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in DaOrarioAStringa: {ex.Message}")
                Return ""
            End Try
        End Function

        Private Function ParseNumeroMinutiColloquiale(input As String) As Integer
            Try
                Select Case input.ToLower()
                    Case "un quarto"
                        Return 15
                    Case "mezzo", "mezza"
                        Return 30
                    Case "quarantacinque"
                        Return 45
                    Case "venti"
                        Return 20
                    Case "dieci"
                        Return 10
                    Case "cinque"
                        Return 5
                    Case "quaranta"
                        Return 40
                    Case "trenta"
                        Return 30
                    Case Else
                        Dim numero As Integer
                        If Integer.TryParse(input, numero) Then
                            Return numero
                        Else
                            Return ParseNumeroColloquiale(input)
                        End If
                End Select
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in ParseNumeroMinutiColloquiale: {ex.Message}")
                Return 0
            End Try
        End Function

        Private Function ParseNumeroColloquiale(input As String) As Integer
            Try
                Select Case input.ToLower()
                    Case "uno", "una"
                        Return 1
                    Case "due"
                        Return 2
                    Case "tre"
                        Return 3
                    Case "quattro"
                        Return 4
                    Case "cinque"
                        Return 5
                    Case "sei"
                        Return 6
                    Case "sette"
                        Return 7
                    Case "otto"
                        Return 8
                    Case "nove"
                        Return 9
                    Case "dieci"
                        Return 10
                    Case "undici"
                        Return 11
                    Case "dodici"
                        Return 12
                    Case "tredici"
                        Return 13
                    Case "quattordici"
                        Return 14
                    Case "quindici"
                        Return 15
                    Case "sedici"
                        Return 16
                    Case "diciassette"
                        Return 17
                    Case "diciotto"
                        Return 18
                    Case "diciannove"
                        Return 19
                    Case "venti"
                        Return 20
                    Case "ventuno"
                        Return 21
                    Case "ventidue"
                        Return 22
                    Case "ventitre"
                        Return 23
                    Case "ventiquattro"
                        Return 24
                    Case "venticinque"
                        Return 25
                    Case "ventisei"
                        Return 26
                    Case "ventisette"
                        Return 27
                    Case "ventotto"
                        Return 28
                    Case "ventinove"
                        Return 29
                    Case "trenta"
                        Return 30
                    Case "trentuno"
                        Return 31
                    Case "trentadue"
                        Return 32
                    Case "trentatre"
                        Return 33
                    Case "trentaquattro"
                        Return 34
                    Case "trentacinque"
                        Return 35
                    Case "trentasei"
                        Return 36
                    Case "trentasette"
                        Return 37
                    Case "trentotto"
                        Return 38
                    Case "trentanove"
                        Return 39
                    Case "quaranta"
                        Return 40
                    Case "quarantuno"
                        Return 41
                    Case "quarantadue"
                        Return 42
                    Case "quarantatre"
                        Return 43
                    Case "quarantaquattro"
                        Return 44
                    Case "quarantacinque"
                        Return 45
                    Case "quarantasei"
                        Return 46
                    Case "quarantasette"
                        Return 47
                    Case "quarantotto"
                        Return 48
                    Case "quarantanove"
                        Return 49
                    Case "cinquanta"
                        Return 50
                    Case "cinquantuno"
                        Return 51
                    Case "cinquantadue"
                        Return 52
                    Case "cinquantatre"
                        Return 53
                    Case "cinquantaquattro"
                        Return 54
                    Case "cinquantacinque"
                        Return 55
                    Case "cinquantasei"
                        Return 56
                    Case "cinquantasette"
                        Return 57
                    Case "cinquantotto"
                        Return 58
                    Case "cinquantanove"
                        Return 59
                    Case Else
                        Dim numero As Integer
                        If Integer.TryParse(input, numero) Then
                            Return numero
                        Else
                            Return 0
                        End If
                End Select
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in ParseNumeroColloquiale: {ex.Message}")
                Return 0
            End Try
        End Function

        Private Function AddMonths(data As DateTime, mesi As Integer) As DateTime
            Try
                Dim anno As Integer = data.Year
                Dim mese As Integer = data.Month
                Dim giorno As Integer = data.Day

                mese += mesi

                If mese > 12 Then
                    anno += (mese - 1) \ 12
                    mese = (mese - 1) Mod 12 + 1
                ElseIf mese < 1 Then
                    anno += mese \ 12 - 1
                    mese = (12 + (mese Mod 12)) Mod 12
                    If mese = 0 Then
                        mese = 12
                    End If
                End If

                Dim giorniNelMese As Integer = DateTime.DaysInMonth(anno, mese)
                If giorno > giorniNelMese Then
                    giorno = giorniNelMese
                End If

                Return New DateTime(anno, mese, giorno)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in AddMonths: {ex.Message}")
                Return data
            End Try
        End Function

        Private Function NomeMeseToNumero(nomeMese As String) As Integer
            Try
                Select Case nomeMese
                    Case "gennaio"
                        Return 1
                    Case "febbraio"
                        Return 2
                    Case "marzo"
                        Return 3
                    Case "aprile"
                        Return 4
                    Case "maggio"
                        Return 5
                    Case "giugno"
                        Return 6
                    Case "luglio"
                        Return 7
                    Case "agosto"
                        Return 8
                    Case "settembre"
                        Return 9
                    Case "ottobre"
                        Return 10
                    Case "novembre"
                        Return 11
                    Case "dicembre"
                        Return 12
                    Case Else
                        Return -1
                End Select
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in NomeMeseToNumero: {ex.Message}")
                Return -1
            End Try
        End Function

        Private Function NomeMeseDaNumero(numeroMese As Integer) As String
            Try
                Select Case numeroMese
                    Case 1
                        Return "gennaio"
                    Case 2
                        Return "febbraio"
                    Case 3
                        Return "marzo"
                    Case 4
                        Return "aprile"
                    Case 5
                        Return "maggio"
                    Case 6
                        Return "giugno"
                    Case 7
                        Return "luglio"
                    Case 8
                        Return "agosto"
                    Case 9
                        Return "settembre"
                    Case 10
                        Return "ottobre"
                    Case 11
                        Return "novembre"
                    Case 12
                        Return "dicembre"
                    Case Else
                        Return ""
                End Select
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in NomeMeseDaNumero: {ex.Message}")
                Return ""
            End Try
        End Function

        Private Function ParseDayOfWeek(dayName As String) As DayOfWeek
            Try
                Select Case dayName
                    Case "lunedì"
                        Return DayOfWeek.Monday
                    Case "martedì"
                        Return DayOfWeek.Tuesday
                    Case "mercoledì"
                        Return DayOfWeek.Wednesday
                    Case "giovedì"
                        Return DayOfWeek.Thursday
                    Case "venerdì"
                        Return DayOfWeek.Friday
                    Case "sabato"
                        Return DayOfWeek.Saturday
                    Case "domenica"
                        Return DayOfWeek.Sunday
                    Case Else
                        Return CType(-1, DayOfWeek)
                End Select
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore in ParseDayOfWeek: {ex.Message}")
                Return CType(-1, DayOfWeek)
            End Try
        End Function
    End Class

    Public Class WGGestoreSuoniSistema
        Private Shared ReadOnly _scrittoreLog As New Logger()
        <Guid("5CDF2C82-151E-4045-9E0F-DB97F2AA1603"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
        Private Interface IAudioEndpointVolume
            Function NotUsed1() As Integer
            Function NotUsed2() As Integer
            Function NotUsed3() As Integer
            Function NotUsed4() As Integer
            Function NotUsed5() As Integer
            Function NotUsed6() As Integer
            Function SetMute(<MarshalAs(UnmanagedType.Bool)> bMute As Boolean, <MarshalAs(UnmanagedType.LPStruct)> EventContext As Guid) As Integer
            Function GetMute(<MarshalAs(UnmanagedType.Bool)> ByRef pbMute As Boolean) As Integer
        End Interface

        <Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
        Private Interface IMMDevice
            Function Activate(<MarshalAs(UnmanagedType.LPStruct)> iid As Guid, dwClsCtx As Integer, pActivationParams As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppInterface As Object) As Integer
        End Interface

        <Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
        Private Interface IMMDeviceEnumerator
            Function NotUsed1() As Integer
            Function GetDefaultAudioEndpoint(dataFlow As Integer, role As Integer, ByRef ppDevice As IMMDevice) As Integer
        End Interface

        Public Sub SilenziaSuoniWindows(stato As Boolean)
            Dim enumerator As IMMDeviceEnumerator = Nothing
            Dim device As IMMDevice = Nothing
            Dim endpointVolume As Object = Nothing
            Try
                enumerator = CType(Activator.CreateInstance(Type.GetTypeFromCLSID(New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), True)), IMMDeviceEnumerator)
                enumerator.GetDefaultAudioEndpoint(0, 1, device)

                Dim iidIAudioEndpointVolume As New Guid("5CDF2C82-151E-4045-9E0F-DB97F2AA1603")
                device.Activate(iidIAudioEndpointVolume, 23, IntPtr.Zero, endpointVolume)

                Dim volume As IAudioEndpointVolume = CType(endpointVolume, IAudioEndpointVolume)
                volume.SetMute(stato, Guid.Empty)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore SilenziaSuoniWindows: {ex.Message} {ex.StackTrace}")
            Finally
                Try
                    If endpointVolume IsNot Nothing Then Marshal.ReleaseComObject(endpointVolume)
                    If device IsNot Nothing Then Marshal.ReleaseComObject(device)
                    If enumerator IsNot Nothing Then Marshal.ReleaseComObject(enumerator)
                Catch exFin As Exception
                    _scrittoreLog.ScriviLog($"Errore rilascio risorse in SilenziaSuoniWindows: {exFin.Message}")
                End Try
            End Try
        End Sub

        Public Sub RiattivaSuoniWindows()
            Dim enumerator As IMMDeviceEnumerator = Nothing
            Dim device As IMMDevice = Nothing
            Dim endpointVolume As Object = Nothing
            Try
                enumerator = CType(Activator.CreateInstance(Type.GetTypeFromCLSID(New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), True)), IMMDeviceEnumerator)
                enumerator.GetDefaultAudioEndpoint(0, 1, device)

                Dim iidIAudioEndpointVolume As New Guid("5CDF2C82-151E-4045-9E0F-DB97F2AA1603")
                device.Activate(iidIAudioEndpointVolume, 23, IntPtr.Zero, endpointVolume)

                Dim volume As IAudioEndpointVolume = CType(endpointVolume, IAudioEndpointVolume)
                volume.SetMute(False, Guid.Empty)
            Catch ex As Exception
                _scrittoreLog.ScriviLog($"Errore RiattivaSuoniWindows: {ex.Message} {ex.StackTrace}")
            Finally
                Try
                    If endpointVolume IsNot Nothing Then Marshal.ReleaseComObject(endpointVolume)
                    If device IsNot Nothing Then Marshal.ReleaseComObject(device)
                    If enumerator IsNot Nothing Then Marshal.ReleaseComObject(enumerator)
                Catch exFin As Exception
                    _scrittoreLog.ScriviLog($"Errore rilascio risorse in RiattivaSuoniWindows: {exFin.Message}")
                End Try
            End Try
        End Sub
    End Class
End Module