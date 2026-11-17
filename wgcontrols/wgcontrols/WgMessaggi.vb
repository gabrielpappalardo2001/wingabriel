Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Diagnostics
Imports wgcontrols

' Gestione dei messaggi a schermo e delle finestre di dialogo accessibili con comandi da tastiera e gesti.
Public Class WGMessaggi
    Implements IDisposable

    Private _messaggioTesto As String
    Private _msgControllo As WGMessaggiInt
    Private disposedValue As Boolean
    Private WriteLogEsterno As New Logger

    Private ReadOnly _chiuso As New ManualResetEventSlim(False)

    Private Const QS_ALLINPUT As UInteger = &H4FF
    Private Const MWMO_INPUTAVAILABLE As UInteger = &H4

    <DllImport("user32.dll")>
    Private Shared Function MsgWaitForMultipleObjectsEx(nCount As UInteger, pHandles As IntPtr, dwMilliseconds As UInteger, dwWakeMask As UInteger, dwFlags As UInteger) As UInteger
    End Function

    Public Event Load As EventHandler
    Public Event FormClosed As EventHandler

    Private Class WGMessaggiInt
        Inherits UserControl

        Private lettura As sintesivocale = sintesivocale.GetInstance()
        Private WriteLog As New Logger
        Private messaggiotesto As String
        Private utils As WGFormUtils = WGFormUtils.GetInstance()
        Private threadTimer As Thread
        Private WithEvents _tastiera As WGTastiera
        Private _touk As WGTouk
        Private _chiusuraGestita As Boolean = False
        Private _lettoInMostra As Boolean = False

        Private Const DT_CENTER As Integer = &H1
        Private Const DT_VCENTER As Integer = &H4
        Private Const DT_WORDBREAK As Integer = &H10

        <StructLayout(LayoutKind.Sequential)>
        Private Structure RECT
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        <DllImport("user32.dll")>
        Private Shared Function DrawText(hDC As IntPtr, lpString As String, nCount As Integer, ByRef lpRect As RECT, uFormat As Integer) As Integer
        End Function

        <DllImport("gdi32.dll")>
        Private Shared Function SelectObject(hdc As IntPtr, hgdiobj As IntPtr) As IntPtr
        End Function

        <DllImport("gdi32.dll")>
        Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
        End Function

        <DllImport("gdi32.dll")>
        Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
        End Function

        <DllImport("gdi32.dll")>
        Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
        End Function

        Public Event InternalMostrato As EventHandler
        Public Event InternalChiuso As EventHandler

        Public Sub New()
            Try
                Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
                Me.BackColor = Color.Black
                Me.ForeColor = Color.White
                Me.DoubleBuffered = True
                Me.TabStop = True
                Me.Dock = DockStyle.Fill
                Me.AccessibleRole = AccessibleRole.Client
                Me.AccessibleName = " "
                Try
                    _tastiera = WGTastiera.GetInstance()
                Catch exTastiera As Exception
                    WriteLog.ScriviLog("WGMessaggiInt.New (Tastiera): " & exTastiera.Message)
                End Try
                IscriviTouk()
            Catch ex As Exception
                WriteLog.ScriviLog("WGMessaggiInt.New: " & ex.Message)
            End Try
        End Sub

        Public Sub ImpostaMessaggio(msg As String)
            Try
                messaggiotesto = If(msg, String.Empty)
                _chiusuraGestita = False
            Catch ex As Exception
                WriteLog.ScriviLog("ImpostaMessaggio: " & ex.Message)
            End Try
        End Sub

        Private Function PuoRicevere() As Boolean
            Try
                If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False
                If Not Me.Visible Then Return False
                Dim c As WGContenitore = WGContenitore.Istanza
                If c Is Nothing Then Return False
                Return c.PuoRicevereInput(Me)
            Catch ex As Exception
                WriteLog.ScriviLog("PuoRicevere: " & ex.Message)
                Return False
            End Try
        End Function

        Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
            Try
                If Not PuoRicevere() Then Return

                Dim ctrlPremuto As Boolean = My.Computer.Keyboard.CtrlKeyDown

                Select Case tasto
                    Case Keys.Escape, Keys.Back, Keys.Enter
                        ChiudiMessaggio()
                        tasto = Keys.None
                    Case Keys.Up, Keys.Down
                        LeggiMessaggioCompleto()
                        tasto = Keys.None
                    Case Keys.C
                        If ctrlPremuto Then
                            Clipboard.SetText(messaggiotesto)
                            lettura.LeggiTesto("messaggio copiato negli appunti")
                            tasto = Keys.None
                        End If
                End Select
            Catch ex As Exception
                WriteLog.ScriviLog("Errore in _tastiera_KeyDown: " & ex.Message)
            End Try
        End Sub

        ' Quando la schermata del messaggio torna davanti (dopo essere stata nascosta da un'altra schermata) rilegge il messaggio.
        Protected Overrides Sub OnVisibleChanged(e As EventArgs)
            MyBase.OnVisibleChanged(e)
            Try
                If Me.Visible AndAlso _lettoInMostra AndAlso Not _chiusuraGestita AndAlso Not Me.IsDisposed Then LeggiMessaggioCompleto()
            Catch ex As Exception
                WriteLog.ScriviLog("OnVisibleChanged: " & ex.Message)
            End Try
        End Sub

        Private Sub LeggiMessaggioCompleto()
            Try
                Dim testoPulito As String = messaggiotesto.Trim()
                If Not testoPulito.EndsWith(".") Then
                    testoPulito &= "."
                End If
                While testoPulito.Contains(" .")
                    testoPulito = testoPulito.Replace(" .", ".")
                End While
                Dim testoCompleto As String = "Messaggio " & testoPulito & " Per chiudere il messaggio premere il tasto escape, back o invio."
                lettura.LeggiTesto(testoCompleto, escludiPunteggiatura:=True)
            Catch ex As Exception
                WriteLog.ScriviLog("LeggiMessaggioCompleto: " & ex.Message)
            End Try
        End Sub

        Private Sub IscriviTouk()
            Try
                If _touk Is Nothing Then _touk = WGTouk.GetInstance()
                If _touk Is Nothing Then Return
                DisiscriviTouk()
                AddHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                AddHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                AddHandler _touk.FlickSu, AddressOf _touk_FlickSu
                AddHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
            Catch ex As Exception
                WriteLog.ScriviLog("IscriviTouk: " & ex.Message)
            End Try
        End Sub

        Private Sub DisiscriviTouk()
            Try
                If _touk Is Nothing Then Return
                RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                RemoveHandler _touk.FlickGiu, AddressOf _touk_FlickGiu
            Catch ex As Exception
                WriteLog.ScriviLog("DisiscriviTouk: " & ex.Message)
            End Try
        End Sub

        Private Sub EseguiSuUI(azione As MethodInvoker)
            Try
                If azione Is Nothing Then Return
                If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
                Me.BeginInvoke(azione)
            Catch exDisposto As ObjectDisposedException
                Return
            Catch ex As Exception
                WriteLog.ScriviLog("EseguiSuUI: " & ex.Message)
            End Try
        End Sub

        Private Sub _touk_TapDoppio()
            Try
                If Not PuoRicevere() Then Return
                EseguiSuUI(AddressOf ChiudiMessaggio)
            Catch ex As Exception
                WriteLog.ScriviLog("_touk_TapDoppio: " & ex.Message)
            End Try
        End Sub

        Private Sub _touk_EscapeGesto()
            Try
                If Not PuoRicevere() Then Return
                EseguiSuUI(AddressOf ChiudiMessaggio)
            Catch ex As Exception
                WriteLog.ScriviLog("_touk_EscapeGesto: " & ex.Message)
            End Try
        End Sub

        Private Sub _touk_FlickSu()
            Try
                If Not PuoRicevere() Then Return
                LeggiMessaggioCompleto()
            Catch ex As Exception
                WriteLog.ScriviLog("_touk_FlickSu: " & ex.Message)
            End Try
        End Sub

        Private Sub _touk_FlickGiu()
            Try
                If Not PuoRicevere() Then Return
                LeggiMessaggioCompleto()
            Catch ex As Exception
                WriteLog.ScriviLog("_touk_FlickGiu: " & ex.Message)
            End Try
        End Sub

        Public Function Mostra() As Boolean
            Dim contenitore As WGContenitore = Nothing
            Try
                contenitore = OttieniContenitore()
                If contenitore Is Nothing Then Return False

                _chiusuraGestita = False

                contenitore.RegistraControlloMessaggio(Me)
                contenitore.MostraControllo(Me)
            Catch ex As Exception
                WriteLog.ScriviLog("Errore in Mostra: " & ex.Message)
                Try
                    If contenitore IsNot Nothing Then contenitore.RimuoviControllo(Me)
                Catch exRimozione As Exception
                    WriteLog.ScriviLog("Errore in Mostra (RimuoviControllo): " & exRimozione.Message)
                End Try
                Return False
            End Try

            Try
                If Me.IsHandleCreated AndAlso _touk IsNot Nothing Then
                    _touk.RiassegnaControllo(Me.Handle)
                End If
            Catch exTouk As Exception
                WriteLog.ScriviLog("Mostra (RiassegnaControllo): " & exTouk.Message)
            End Try

            Try
                Dim testoPulito As String = If(messaggiotesto, String.Empty).Trim()
                If Not testoPulito.EndsWith(".") Then
                    testoPulito &= "."
                End If
                While testoPulito.Contains(" .")
                    testoPulito = testoPulito.Replace(" .", ".")
                End While

                Dim testoCompleto As String = "Messaggio " & testoPulito & " Per chiudere il messaggio premere il tasto esc, back o enter."
                lettura.LeggiTesto(testoCompleto, escludiPunteggiatura:=True)
                _lettoInMostra = True
            Catch exVoce As Exception
                WriteLog.ScriviLog("Mostra (lettura): " & exVoce.Message)
            End Try

            AvviaTimerChiusura()

            Try
                RaiseEvent InternalMostrato(Me, EventArgs.Empty)
            Catch exEvento As Exception
                WriteLog.ScriviLog("Mostra (InternalMostrato): " & exEvento.Message)
            End Try

            Return True
        End Function

        Private Sub AvviaTimerChiusura()
            Try
                threadTimer = New Thread(AddressOf TimerLoop)
                threadTimer.IsBackground = True
                threadTimer.Start()
            Catch ex As Exception
                WriteLog.ScriviLog("AvviaTimerChiusura: " & ex.Message)
            End Try
        End Sub

        Private Sub TimerLoop()
            Try
                Thread.Sleep(60000)
                ChiudiMessaggioThreadSafe()
            Catch ex As Exception
                WriteLog.ScriviLog("TimerLoop: " & ex.Message)
            End Try
        End Sub

        Private Sub ChiudiMessaggioThreadSafe()
            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                    contenitore.Invoke(New MethodInvoker(AddressOf ChiudiMessaggio))
                Else
                    ChiudiMessaggio()
                End If
            Catch ex As Exception
                WriteLog.ScriviLog("ChiudiMessaggioThreadSafe: " & ex.Message)
            End Try
        End Sub

        Private Sub ChiudiMessaggio()
            Try
                If _chiusuraGestita Then Return
                If Me.IsDisposed Then Return
                _chiusuraGestita = True

                Try
                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                    If contenitore IsNot Nothing Then
                        contenitore.RimuoviControllo(Me)
                    End If
                Catch exRimuovi As Exception
                    WriteLog.ScriviLog("ChiudiMessaggio (RimuoviControllo): " & exRimuovi.Message)
                End Try

                Try
                    lettura.FermaVoce()
                Catch exVoce As Exception
                    WriteLog.ScriviLog("ChiudiMessaggio (FermaVoce): " & exVoce.Message)
                End Try

                RaiseEvent InternalChiuso(Me, EventArgs.Empty)
            Catch ex As Exception
                WriteLog.ScriviLog("ChiudiMessaggio: " & ex.Message)
            End Try
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Try
                Dim hdc As IntPtr = e.Graphics.GetHdc()
                Try
                    Dim rect As New RECT With {
                        .Left = 10,
                        .Top = 10,
                        .Right = Me.ClientSize.Width - 10,
                        .Bottom = Me.ClientSize.Height - 10
                    }

                    Using font As New Font("Arial", 30, FontStyle.Regular, GraphicsUnit.Point)
                        Dim hFont As IntPtr = font.ToHfont()
                        Dim oldFont As IntPtr = SelectObject(hdc, hFont)

                        SetTextColor(hdc, ColorTranslator.ToWin32(Color.White))
                        SetBkMode(hdc, 1)

                        DrawText(hdc, messaggiotesto, -1, rect, DT_CENTER Or DT_VCENTER Or DT_WORDBREAK)

                        SelectObject(hdc, oldFont)
                        DeleteObject(hFont)
                    End Using
                Finally
                    e.Graphics.ReleaseHdc(hdc)
                End Try
            Catch ex As Exception
                WriteLog.ScriviLog("Errore disegno: " & ex.Message)
            End Try
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            Try
                If disposing Then
                    If _tastiera IsNot Nothing Then
                        RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                        _tastiera = Nothing
                    End If
                    DisiscriviTouk()
                    _touk = Nothing
                End If
            Catch ex As Exception
                WriteLog.ScriviLog("Dispose: " & ex.Message)
            Finally
                MyBase.Dispose(disposing)
            End Try
        End Sub
    End Class

    Public Sub New()
    End Sub

    Public Sub Messaggio(messaggio As String)
        _messaggioTesto = messaggio
    End Sub

    Public Sub Show()
        MostraControllo()
    End Sub

    Public Sub ShowDialog()
        Try
            Dim contenitore As WGContenitore = OttieniContenitore()
            If contenitore Is Nothing Then Return

            _chiuso.Reset()
            If Not MostraControllo() Then Return

            If contenitore.InvokeRequired Then
                Do Until _chiuso.Wait(250)
                    If disposedValue OrElse contenitore.IsDisposed Then Exit Do
                Loop
            Else
                PompaFinoAllaChiusura(contenitore)
            End If
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("Errore in ShowDialog: " & ex.Message)
        End Try
    End Sub

    Private Sub PompaFinoAllaChiusura(contenitore As WGContenitore)
        Do While Not _chiuso.IsSet AndAlso Not disposedValue AndAlso Not contenitore.IsDisposed
            Application.DoEvents()
            If _chiuso.IsSet Then Exit Do
            MsgWaitForMultipleObjectsEx(0UI, IntPtr.Zero, 50UI, QS_ALLINPUT, MWMO_INPUTAVAILABLE)
        Loop
    End Sub

    Private Function MostraControllo() As Boolean
        Try
            Dim contenitore As WGContenitore = OttieniContenitore()
            If contenitore Is Nothing Then Return False

            If contenitore.InvokeRequired Then
                Dim risultato As Boolean = False
                contenitore.Invoke(New MethodInvoker(Sub() risultato = MostraControllo()))
                Return risultato
            End If

            If disposedValue Then Return False

            If _msgControllo Is Nothing OrElse _msgControllo.IsDisposed Then
                _msgControllo = New WGMessaggiInt()
                AddHandler _msgControllo.InternalMostrato, AddressOf OnMostrato
                AddHandler _msgControllo.InternalChiuso, AddressOf OnChiuso
            End If
            _msgControllo.ImpostaMessaggio(_messaggioTesto)
            Return _msgControllo.Mostra()
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("Errore in MostraControllo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LiberaControllo()
        Try
            If _msgControllo Is Nothing Then Return
            RemoveHandler _msgControllo.InternalMostrato, AddressOf OnMostrato
            RemoveHandler _msgControllo.InternalChiuso, AddressOf OnChiuso
            If Not _msgControllo.IsDisposed Then
                _msgControllo.Dispose()
            End If
            _msgControllo = Nothing
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("Errore dispose messaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub OnMostrato(sender As Object, e As EventArgs)
        Try
            RaiseEvent Load(sender, e)
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("OnMostrato: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiuso(sender As Object, e As EventArgs)
        Try
            RaiseEvent FormClosed(sender, e)
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("OnChiuso: " & ex.Message)
        Finally
            _chiuso.Set()
        End Try

        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.IsHandleCreated AndAlso Not contenitore.IsDisposed Then
                contenitore.BeginInvoke(New MethodInvoker(AddressOf LiberaControllo))
            Else
                LiberaControllo()
            End If
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("OnChiuso (liberazione): " & ex.Message)
        End Try
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not disposedValue Then
                If disposing Then
                    _chiuso.Set()
                    If _msgControllo IsNot Nothing Then
                        RemoveHandler _msgControllo.InternalMostrato, AddressOf OnMostrato
                        RemoveHandler _msgControllo.InternalChiuso, AddressOf OnChiuso
                        Try
                            Dim contenitore As WGContenitore = WGContenitore.Istanza
                            If contenitore IsNot Nothing Then
                                contenitore.RimuoviControllo(_msgControllo)
                            End If
                        Catch exRimuovi As Exception
                            WriteLogEsterno.ScriviLog("Dispose (RimuoviControllo): " & exRimuovi.Message)
                        End Try
                        _msgControllo.Dispose()
                        _msgControllo = Nothing
                    End If
                End If
                disposedValue = True
            End If
        Catch ex As Exception
            WriteLogEsterno.ScriviLog("Dispose: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Friend Shared Function OttieniContenitore() As WGContenitore
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then Return contenitore
            If Thread.CurrentThread.GetApartmentState() <> ApartmentState.STA Then Return Nothing
            Return WGContenitore.IstanzaOCrea()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("Errore in OttieniContenitore: " & ex.Message)
            Return Nothing
        End Try
    End Function

End Class