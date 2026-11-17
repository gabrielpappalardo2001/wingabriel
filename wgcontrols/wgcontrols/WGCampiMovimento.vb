Imports System.Windows.Forms
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Globalization
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.Threading

' Gestione del movimento e dell'interazione all'interno dei campi di testo, con supporto per tastiera touch e feedback vocale.
Public Class WGCampiMovimento
    Inherits Control
    Implements IWGBrailleCampo
    Implements IWGBrailleElencoCampi

    <DllImport("gdi32.dll")> Private Shared Function SelectObject(hdc As IntPtr, hgdiobj As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function Rectangle(hdc As IntPtr, nLeftRect As Integer, nTopRect As Integer, nRightRect As Integer, nBottomRect As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="CreateFontW")> Private Shared Function CreateFont(nHeight As Integer, nWidth As Integer, nEscapement As Integer, nOrientation As Integer, fnWeight As Integer, fdwItalic As UInteger, fdwUnderline As UInteger, fdwStrikeOut As UInteger, fdwCharSet As UInteger, fdwOutputPrecision As UInteger, fdwClipPrecision As UInteger, fdwQuality As UInteger, fdwPitchAndFamily As UInteger, lpszFace As String) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="TextOutW")> Private Shared Function TextOut(hdc As IntPtr, nXStart As Integer, nYStart As Integer, lpString As String, cbString As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetTextExtentPoint32W")> Private Shared Function GetTextExtentPoint32(hdc As IntPtr, lpString As String, cbString As Integer, ByRef lpSize As SIZE) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreatePen(fnPenStyle As Integer, nWidth As Integer, crColor As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateSolidBrush(crColor As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetStockObject(fnObject As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleDC(hdc As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleBitmap(hdc As IntPtr, nWidth As Integer, nHeight As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function BitBlt(hdcDest As IntPtr, nXDest As Integer, nYDest As Integer, nWidth As Integer, nHeight As Integer, hdcSrc As IntPtr, nXSrc As Integer, nYSrc As Integer, dwRop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteDC(hdc As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function FillRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function BeginPaint(hwnd As IntPtr, ByRef lpPaint As PaintStruct) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function EndPaint(hwnd As IntPtr, ByRef lpPaint As PaintStruct) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetDC(hwnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function InvalidateRect(hWnd As IntPtr, lpRect As IntPtr, bErase As Boolean) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function ReleaseDC(hwnd As IntPtr, hdc As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetDeviceCaps(hdc As IntPtr, nIndex As Integer) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function CreateCaret(hWnd As IntPtr, hBitmap As IntPtr, nWidth As Integer, nHeight As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function DestroyCaret() As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function ShowCaret(hWnd As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetFocus() As IntPtr
    End Function

    <DllImport("user32.dll")> Private Shared Function HideCaret(hWnd As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function SetCaretPos(x As Integer, y As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetKeyboardState(lpKeyState As Byte()) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function MapVirtualKey(uCode As UInteger, uMapType As UInteger) As UInteger
    End Function
    <DllImport("user32.dll")> Private Shared Function ToUnicodeEx(wVirtKey As UInteger, wScanCode As UInteger, lpKeyState As Byte(), pwszBuff As StringBuilder, cchBuff As Integer, wFlags As UInteger, dwhkl As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function GetKeyboardLayout(idThread As UInteger) As IntPtr
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SIZE
        Public cx As Integer
        Public cy As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PaintStruct
        Public hdc As IntPtr
        Public fErase As Boolean
        Public rcPaintLeft As Integer
        Public rcPaintTop As Integer
        Public rcPaintRight As Integer
        Public rcPaintBottom As Integer
        Public fRestore As Boolean
        Public fIncUpdate As Boolean
        Public reserved1 As Integer
        Public reserved2 As Integer
        Public reserved3 As Integer
        Public reserved4 As Integer
        Public reserved5 As Integer
        Public reserved6 As Integer
        Public reserved7 As Integer
        Public reserved8 As Integer
    End Structure

    Private Const TRANSPARENT As Integer = 1
    Private Const OPAQUE As Integer = 2
    Private Const PS_SOLID As Integer = 0
    Private Const NULL_BRUSH As Integer = 5
    Private Const FW_NORMAL As Integer = 400
    Private Const FW_BOLD As Integer = 700
    Private Const DEFAULT_CHARSET As UInteger = 1
    Private Const OUT_DEFAULT_PRECIS As UInteger = 0
    Private Const CLIP_DEFAULT_PRECIS As UInteger = 0
    Private Const DEFAULT_QUALITY As UInteger = 0
    Private Const DEFAULT_PITCH As UInteger = 0
    Private Const FF_DONTCARE As UInteger = 0
    Private Const LOGPIXELSY As Integer = 90
    Private Const WM_PAINT As Integer = &HF
    Private Const WM_IME_STARTCOMPOSITION As Integer = &H10D
    Private Const WM_IME_ENDCOMPOSITION As Integer = &H10E
    Private Const WM_IME_COMPOSITION As Integer = &H10F
    Private Const WM_IME_CHAR As Integer = &H286
    Private Const SRCCOPY As Integer = &HCC0020

    Private _logger As New Logger()
    Private _sintesiVocale As sintesivocale = sintesivocale.GetInstance()
    Private _impostazioniComuni As New WGImpostazioniComuni()
    Private _anchorPoint As Integer = -1
    Private WithEvents _tastiera As WGTastiera
    Private WithEvents _touk As WGTouk
    Private _tastieraTouch As WGTastieraTouch
    Private _shiftPremutoDaHook As Boolean = False
    Private _altPremutoDaHook As Boolean = False
    Private _altDestroPremutoDaHook As Boolean = False
    Private _ctrlSinistroPremutoDaHook As Boolean = False
    Private _ctrlDestroPremutoDaHook As Boolean = False
    Private _isCampoPassword As Boolean = False
    Private _useSystemPasswordChar As Boolean = False

    Private _testo As String = ""
    Private _selectionStart As Integer = 0
    Private _selectionLength As Integer = 0
    Private _multiline As Boolean = False
    Private _readOnlyCampo As Boolean = False
    Private _maxLength As Integer = 0
    Private _modified As Boolean = False
    Private _colonnaPreferita As Integer = -1
    Private _confermatoLimiteVerticale As Boolean = False
    Private _vuotoOrizzontaleAnnunciato As Boolean = False
    Public Property NavigazioneDirettaCampi As Boolean = False
    Private _overrideEcho As String = Nothing
    Private _overrideLivelloPunteggiaturaScrittura As Nullable(Of Integer) = Nothing

    Public Sub ImpostaImpostazioniTemporanee(echoDigitazione As String, livelloPunteggiaturaScrittura As Integer)
        Try
            If Not String.IsNullOrEmpty(echoDigitazione) Then _overrideEcho = echoDigitazione
            _overrideLivelloPunteggiaturaScrittura = livelloPunteggiaturaScrittura
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.ImpostaImpostazioniTemporanee: " & ex.Message)
        End Try
    End Sub

    Private Function NormalizzaEchoDigitazione(valore As String) As String
        Try
            If valore Is Nothing Then Return ""
            Dim ripulito As String = valore.Trim()
            If ripulito = "0" OrElse ripulito = "1" OrElse ripulito = "2" OrElse ripulito = "3" Then Return ripulito
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.NormalizzaEchoDigitazione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EchoDigitazioneEffettivo() As String
        Try
            Dim daOverride As String = NormalizzaEchoDigitazione(_overrideEcho)
            If daOverride.Length > 0 Then Return daOverride

            Dim daConfigurazione As String = ""
            Try
                Dim configurazione As New WGConfig()
                daConfigurazione = NormalizzaEchoDigitazione(configurazione.LeggiValore("echo_digitazione"))
            Catch exConfig As Exception
                _logger.ScriviLog("WGCampiMovimento.EchoDigitazioneEffettivo (configurazione): " & exConfig.Message)
            End Try
            If daConfigurazione.Length > 0 Then Return daConfigurazione

            Dim daComuni As String = ""
            Try
                daComuni = NormalizzaEchoDigitazione(_impostazioniComuni.EchoDigitazione)
            Catch exComuni As Exception
                _logger.ScriviLog("WGCampiMovimento.EchoDigitazioneEffettivo (comuni): " & exComuni.Message)
            End Try
            If daComuni.Length > 0 Then Return daComuni

            Return "3"
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.EchoDigitazioneEffettivo: " & ex.Message)
            Return "3"
        End Try
    End Function

    Private Function LivelloPunteggiaturaScritturaEffettivo() As Integer
        Try
            If _overrideLivelloPunteggiaturaScrittura.HasValue Then Return _overrideLivelloPunteggiaturaScrittura.Value
            Return _impostazioniComuni.LivelloScritturaPunteggiatura
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.LivelloPunteggiaturaScritturaEffettivo: " & ex.Message)
            Return 3
        End Try
    End Function

    Private Function SimboloDaLeggereInScrittura(c As Char) As Boolean
        Return LivelloPunteggiaturaScritturaEffettivo() >= WGImpostazioniComuni.LivelloSimboloScrittura(c)
    End Function
    Private _posizioneVirtualeVerticale As Integer = 0
    Private _direzioneLimiteConfermata As Integer = 0
    Private _scrollOffsetX As Integer = 0
    Private _primaRigaVisibile As Integer = 0

    Private _hFontCorrente As IntPtr = IntPtr.Zero
    Private _fontFirmaCorrente As String = ""
    Private _altezzaRiga As Integer = 20

    Private Structure StatoTesto
        Public Testo As String
        Public InizioSel As Integer
        Public LunghSel As Integer
    End Structure

    Private _undoStack As New Stack(Of StatoTesto)
    Private _redoStack As New Stack(Of StatoTesto)

    Public Event RichiestaCampoAdiacente(sender As Object, direzione As Integer)
    Public Event RichiestaConfermaCampo(sender As Object)
    Public Property MaiuscolaAutomatica As Boolean = False
    Private _invioConsecutivi As Integer = 0

    Public Property IsCampoPassword As Boolean
        Get
            Try
                Return _isCampoPassword
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.IsCampoPassword Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _isCampoPassword = value
                _useSystemPasswordChar = value
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.IsCampoPassword Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property UseSystemPasswordChar As Boolean
        Get
            Try
                Return _useSystemPasswordChar
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.UseSystemPasswordChar Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _useSystemPasswordChar = value
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.UseSystemPasswordChar Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property leggipassword As Boolean
        Get
            Try
                Return _impostazioniComuni.LeggiPasswordAttivo
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.leggipassword Get: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                _impostazioniComuni.LeggiPasswordAttivo = value
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.leggipassword Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property Multiline As Boolean
        Get
            Return _multiline
        End Get
        Set(value As Boolean)
            Try
                _multiline = value
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.Multiline Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property [ReadOnly] As Boolean
        Get
            Return _readOnlyCampo
        End Get
        Set(value As Boolean)
            _readOnlyCampo = value
        End Set
    End Property

    Public Property MaxLength As Integer
        Get
            Return _maxLength
        End Get
        Set(value As Integer)
            _maxLength = value
        End Set
    End Property

    Public ReadOnly Property Modified As Boolean
        Get
            Return _modified
        End Get
    End Property

    Public ReadOnly Property TextLength As Integer
        Get
            Try
                Return _testo.Length
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.TextLength: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Public Overrides Property Text As String
        Get
            Return _testo
        End Get
        Set(value As String)
            Try
                Dim nuovoTesto As String = If(value, "")
                nuovoTesto = nuovoTesto.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
                _testo = nuovoTesto
                InvalidaLayoutRighe()
                _selectionStart = Math.Min(_selectionStart, _testo.Length)
                _selectionLength = 0
                _anchorPoint = -1
                _colonnaPreferita = -1
                _vuotoOrizzontaleAnnunciato = False
                _posizioneVirtualeVerticale = 0
                _confermatoLimiteVerticale = False
                _scrollOffsetX = 0
                _primaRigaVisibile = 0
                _modified = True
                MyBase.OnTextChanged(EventArgs.Empty)
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.Text Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property SelectionStart As Integer
        Get
            Return _selectionStart
        End Get
        Set(value As Integer)
            Try
                _selectionStart = Math.Max(0, Math.Min(value, _testo.Length))
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.SelectionStart Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property SelectionLength As Integer
        Get
            Return _selectionLength
        End Get
        Set(value As Integer)
            Try
                Dim massimo As Integer = _testo.Length - _selectionStart
                _selectionLength = Math.Max(0, Math.Min(value, Math.Max(0, massimo)))
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.SelectionLength Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property SelectedText As String
        Get
            Try
                If _selectionLength <= 0 Then Return ""
                Return _testo.Substring(_selectionStart, _selectionLength)
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.SelectedText Get: " & ex.Message)
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                If _readOnlyCampo Then Return
                Dim testoInserire As String = If(value, "")
                testoInserire = testoInserire.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
                If Not _multiline Then testoInserire = testoInserire.Replace(vbLf, "")

                If _maxLength > 0 Then
                    Dim spazioDisponibile As Integer = _maxLength - (_testo.Length - _selectionLength)
                    If spazioDisponibile < 0 Then spazioDisponibile = 0
                    If testoInserire.Length > spazioDisponibile Then testoInserire = testoInserire.Substring(0, spazioDisponibile)
                End If

                Dim inizio As Integer = _selectionStart
                Dim fine As Integer = _selectionStart + _selectionLength
                If fine > _testo.Length Then fine = _testo.Length
                If inizio > fine Then inizio = fine

                _testo = _testo.Substring(0, inizio) & testoInserire & _testo.Substring(fine)
                InvalidaLayoutRighe()
                _selectionStart = inizio + testoInserire.Length
                _selectionLength = 0
                _modified = True
                MyBase.OnTextChanged(EventArgs.Empty)
                RichiediRidisegno()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.SelectedText Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.Opaque Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            Me.Height = 24
            Me.BackColor = Color.White
            Me.ImeMode = ImeMode.Disable
            _tastiera = WGTastiera.GetInstance()
            _touk = WGTouk.GetInstance()
            WGBraille.Collega(Me)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.New: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                If _tastiera IsNot Nothing Then
                    Try
                        RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                        RemoveHandler _tastiera.ShiftPremuto, AddressOf _tastiera_ShiftPremuto
                        RemoveHandler _tastiera.ShiftRilasciato, AddressOf _tastiera_ShiftRilasciato
                        RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                        RemoveHandler _tastiera.AltRilasciato, AddressOf _tastiera_AltRilasciato
                        RemoveHandler _tastiera.AltDestroPremuto, AddressOf _tastiera_AltDestroPremuto
                        RemoveHandler _tastiera.AltDestroRilasciato, AddressOf _tastiera_AltDestroRilasciato
                        RemoveHandler _tastiera.ControlSinistroPremuto, AddressOf _tastiera_ControlSinistroPremuto
                        RemoveHandler _tastiera.ControlSinistroRilasciato, AddressOf _tastiera_ControlSinistroRilasciato
                        RemoveHandler _tastiera.ControlDestroPremuto, AddressOf _tastiera_ControlDestroPremuto
                        RemoveHandler _tastiera.ControlDestroRilasciato, AddressOf _tastiera_ControlDestroRilasciato
                        _tastiera = Nothing
                    Catch ex As Exception
                        _logger.ScriviLog("WGCampiMovimento.Dispose (Tastiera): " & ex.Message)
                    End Try
                End If
                If _hFontCorrente <> IntPtr.Zero Then
                    DeleteObject(_hFontCorrente)
                    _hFontCorrente = IntPtr.Zero
                End If
                If _touk IsNot Nothing Then
                    Try
                        RemoveHandler _touk.FlickAvanti, AddressOf _touk_FlickAvantiTesto
                        RemoveHandler _touk.FlickIndietro, AddressOf _touk_FlickIndietroTesto
                        RemoveHandler _touk.RichiestaLetturaCompleta, AddressOf _touk_RichiestaLetturaCompleta
                        RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppioCampo
                        _touk = Nothing
                    Catch exTouk As Exception
                        _logger.ScriviLog("WGCampiMovimento.Dispose (Touk): " & exTouk.Message)
                    End Try
                End If
                If _tastieraTouch IsNot Nothing Then
                    Try
                        RemoveHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                        RemoveHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                        RemoveHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
                        If Not _tastieraTouch.IsDisposed Then _tastieraTouch.Dispose()
                        _tastieraTouch = Nothing
                    Catch exTastiera As Exception
                        _logger.ScriviLog("WGCampiMovimento.Dispose (TastieraTouch): " & exTastiera.Message)
                    End Try
                End If
            End If
            MyBase.Dispose(disposing)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.Dispose: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = &H300 OrElse m.Msg = &H301 OrElse m.Msg = &H302 OrElse m.Msg = &H303 OrElse m.Msg = &H7B Then
                Return
            End If
            If m.Msg = WM_IME_STARTCOMPOSITION OrElse m.Msg = WM_IME_ENDCOMPOSITION OrElse m.Msg = WM_IME_COMPOSITION OrElse m.Msg = WM_IME_CHAR Then
                Return
            End If
            If m.Msg = WM_PAINT Then
                DisegnaControllo()
                Return
            End If
            MyBase.WndProc(m)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.WndProc: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Try
            e.Handled = True
            e.SuppressKeyPress = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnKeyDown: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnKeyPress(e As KeyPressEventArgs)
        Try
            e.Handled = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnKeyPress: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
            InvalidaLayoutRighe()
            OttieniFontGdi()
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnHandleCreated: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            InvalidaLayoutRighe()
            DisponiTastieraToukFiglia()
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnResize: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        Try
            MyBase.OnSizeChanged(e)
            DisponiTastieraToukFiglia()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnSizeChanged: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Try
            MyBase.OnGotFocus(e)
            _confermatoLimiteVerticale = False
            _posizioneVirtualeVerticale = 0
            CreateCaret(Me.Handle, IntPtr.Zero, 1, _altezzaRiga - 4)
            ShowCaret(Me.Handle)
            PosizionaCaretSuSchermo()
            RichiediRidisegno()
            Try
                If Not WGTastieraTouch.RiprendiControlloTouch() Then
                    If _touk IsNot Nothing Then _touk.RiassegnaControllo(Me.Handle)
                End If
            Catch exTouk As Exception
                _logger.ScriviLog("WGCampiMovimento.OnGotFocus (RiassegnaControllo): " & exTouk.Message)
            End Try
            If Not _rientroDaTastieraTouch Then MostraTastieraTouchSeAttiva()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnGotFocus: " & ex.Message)
        End Try
    End Sub

    Private Const QuotaCampiTouk As Double = 0.6

    Private Function TastieraToukOspitataDalPadre() As Boolean
        Try
            Return TypeOf Me.Parent Is CampiFormInterno
        Catch
            Return False
        End Try
    End Function

    Private Function TastieraToukVisibileFiglia() As Boolean
        Try
            Return _tastieraTouch IsNot Nothing AndAlso Not _tastieraTouch.IsDisposed AndAlso _tastieraTouch.Visible AndAlso Me.Controls.Contains(_tastieraTouch)
        Catch
            Return False
        End Try
    End Function

    Private Function QuotaCampiEffettivaFiglia() As Double
        Try
            If _touk Is Nothing Then Return QuotaCampiTouk
            Return 1.0 - _touk.QuotaAltezzaTastiera(Me.ClientSize.Width, Me.ClientSize.Height, 1.0 - QuotaCampiTouk)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.QuotaCampiEffettivaFiglia: " & ex.Message)
            Return QuotaCampiTouk
        End Try
    End Function

    Private Function AltezzaCampiToukFiglia() As Integer
        Try
            Dim clientH As Integer = Me.ClientSize.Height
            If Not TastieraToukVisibileFiglia() Then Return Math.Max(1, clientH)
            Dim campiH As Integer = CInt(clientH * QuotaCampiEffettivaFiglia())
            If campiH < 1 Then campiH = 1
            If campiH >= clientH Then campiH = Math.Max(1, clientH - 1)
            Return campiH
        Catch
            Return Math.Max(1, Me.ClientSize.Height)
        End Try
    End Function

    Private Function AreaCampoGdiTouk() As Rectangle
        Try
            Return New Rectangle(0, 0, Me.ClientSize.Width, AltezzaCampiToukFiglia())
        Catch
            Return New Rectangle(0, 0, Me.ClientSize.Width, Me.ClientSize.Height)
        End Try
    End Function

    Private Sub MostraTastieraTouchSeAttiva()
        Try
            If [ReadOnly] Then Return
            If _touk Is Nothing OrElse Not _touk.TouchAttivo Then
                NascondiTastieraTouch()
                Return
            End If

            If TastieraToukOspitataDalPadre() Then
                Dim padreCampi As CampiFormInterno = TryCast(Me.Parent, CampiFormInterno)
                If padreCampi IsNot Nothing Then padreCampi.AggiornaTastieraTouk()
                Return
            End If

            If _tastieraTouch Is Nothing Then
                _tastieraTouch = New WGTastieraTouch()
                _tastieraTouch.LayoutGestitoDalContenitore = True
                AddHandler _tastieraTouch.CarattereInserito, AddressOf OnCarattereInseritoDaTastieraTouch
                AddHandler _tastieraTouch.EliminaRichiesto, AddressOf OnEliminaRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.InvioRichiesto, AddressOf OnInvioRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.ChiudiRichiesto, AddressOf OnChiudiRichiestoDaTastieraTouch
                AddHandler _tastieraTouch.TestoDettatoInserito, AddressOf OnTestoDettatoDaTastieraTouch
            End If

            _tastieraTouch.LayoutGestitoDalContenitore = True
            _tastieraTouch.Dock = DockStyle.None

            If Not Me.Controls.Contains(_tastieraTouch) Then
                Me.Controls.Add(_tastieraTouch)
            End If

            _tastieraTouch.Mostra()
            _tastieraTouch.Visible = True
            DisponiTastieraToukFiglia()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.MostraTastieraTouchSeAttiva: " & ex.Message)
        End Try
    End Sub

    Private Sub DisponiTastieraToukFiglia()
        Try
            If Not TastieraToukVisibileFiglia() Then
                RichiediRidisegno()
                Return
            End If
            Dim clientW As Integer = Me.ClientSize.Width
            Dim clientH As Integer = Me.ClientSize.Height
            If clientW <= 0 OrElse clientH <= 0 Then Return
            Dim campiH As Integer = AltezzaCampiToukFiglia()
            _tastieraTouch.SetBounds(0, campiH, clientW, clientH - campiH)
            _tastieraTouch.BringToFront()
            Me.Invalidate(AreaCampoGdiTouk())
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.DisponiTastieraToukFiglia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Nasconde ogni altra tastiera a schermo presente nella finestra. Ogni campo crea la propria
    ''' e la aggiunge alla finestra, quindi possono coesistere: se ne resta visibile una che
    ''' appartiene a un altro campo, i tasti premuti finiscono in quel campo invece che in quello
    ''' che ha il fuoco, e chi scrive non sente nulla perche' il carattere viene letto altrove.
    ''' </summary>
    Private Shared Sub NascondiAltreTastiereTouch(formaOspite As Form, daMantenere As WGTastieraTouch)
        Try
            If formaOspite Is Nothing OrElse formaOspite.IsDisposed Then Return

            For Each controllo As Control In formaOspite.Controls
                Dim altraTastiera As WGTastieraTouch = TryCast(controllo, WGTastieraTouch)
                If altraTastiera Is Nothing Then Continue For
                If ReferenceEquals(altraTastiera, daMantenere) Then Continue For
                If altraTastiera.IsDisposed OrElse Not altraTastiera.Visible Then Continue For
                altraTastiera.Nascondi()
            Next
        Catch ex As Exception
            Try
                Dim registro As New wgcontrols.Logger()
                registro.ScriviLog("WGCampiMovimento.NascondiAltreTastiereTouch: " & ex.Message)
            Catch
            End Try
        End Try
    End Sub

    Private Sub NascondiTastieraTouch()
        Try
            If _tastieraTouch IsNot Nothing Then
                _tastieraTouch.Nascondi()
                _tastieraTouch.Visible = False
                If Me.Controls.Contains(_tastieraTouch) Then Me.Controls.Remove(_tastieraTouch)
            End If
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.NascondiTastieraTouch: " & ex.Message)
        End Try
    End Sub

    ' Lo stato e' condiviso fra tutti i campi perche' la riduzione riguarda un contenitore solo,
    ' comune a tutti. Passando da un campo all'altro Windows Forms genera GotFocus del campo nuovo
    ' PRIMA di LostFocus del vecchio: con uno stato per singolo campo il ripristino del vecchio
    ' arriverebbe dopo la riduzione del nuovo e cancellerebbe lo spazio appena liberato.
    Private Shared _contenitoreRidotto As Control = Nothing
    Private Shared _altezzaContenitoreOriginale As Integer = 0
    Private Shared _autoScrollOriginale As Boolean = False

    ''' <summary>
    ''' Restituisce il controllo che sta direttamente dentro la finestra e che contiene questo
    ''' campo. E' l'area da rimpicciolire per fare posto alla tastiera.
    ''' </summary>
    Private Function ContenitoreDentroLaFinestra(formaOspite As Form) As Control
        Try
            Dim risalita As Control = Me
            While risalita IsNot Nothing AndAlso risalita.Parent IsNot Nothing AndAlso risalita.Parent IsNot formaOspite
                risalita = risalita.Parent
            End While

            If risalita IsNot Nothing AndAlso risalita.Parent Is formaOspite Then Return risalita
            Return Nothing
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.ContenitoreDentroLaFinestra: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Riduce l'area dei campi all'altezza lasciata libera dalla tastiera e vi attiva lo
    ''' scorrimento, poi porta in vista il campo che ha il fuoco. Senza questo i campi in basso
    ''' resterebbero sotto la tastiera e irraggiungibili, dato che qui hanno posizioni fisse e il
    ''' programma non prevede alcuno scorrimento.
    ''' </summary>
    Private Sub RiduciContenitorePerTastiera(formaOspite As Form, altezzaTastiera As Integer)
        Try
            Dim contenitore As Control = ContenitoreDentroLaFinestra(formaOspite)
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return

            If _contenitoreRidotto Is Nothing Then
                _contenitoreRidotto = contenitore
                _altezzaContenitoreOriginale = contenitore.Height

                Dim scorrevoleIniziale As ScrollableControl = TryCast(contenitore, ScrollableControl)
                If scorrevoleIniziale IsNot Nothing Then
                    _autoScrollOriginale = scorrevoleIniziale.AutoScroll
                    scorrevoleIniziale.AutoScroll = True
                End If
            End If

            Dim altezzaDisponibile As Integer = formaOspite.ClientSize.Height - altezzaTastiera
            If altezzaDisponibile > 0 AndAlso contenitore.Height <> altezzaDisponibile Then
                contenitore.Height = altezzaDisponibile
            End If

            Dim scorrevole As ScrollableControl = TryCast(contenitore, ScrollableControl)
            If scorrevole IsNot Nothing Then scorrevole.ScrollControlIntoView(Me)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.RiduciContenitorePerTastiera: " & ex.Message)
        End Try
    End Sub

    Private Function EsisteTastieraTouchVisibile(contenitore As Control) As Boolean
        Try
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return False

            Dim formaOspite As Form = contenitore.FindForm()
            If formaOspite Is Nothing OrElse formaOspite.IsDisposed Then Return False

            For Each controllo As Control In formaOspite.Controls
                Dim tastiera As WGTastieraTouch = TryCast(controllo, WGTastieraTouch)
                If tastiera IsNot Nothing AndAlso Not tastiera.IsDisposed AndAlso tastiera.Visible Then Return True
            Next

            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.EsisteTastieraTouchVisibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RipristinaContenitoreDopoTastiera()
        Try
            If _contenitoreRidotto Is Nothing Then Return

            ' Se un altro campo ha gia' mostrato la propria tastiera, lo spazio serve ancora.
            If EsisteTastieraTouchVisibile(_contenitoreRidotto) Then Return

            If Not _contenitoreRidotto.IsDisposed Then
                If _altezzaContenitoreOriginale > 0 Then _contenitoreRidotto.Height = _altezzaContenitoreOriginale

                Dim scorrevole As ScrollableControl = TryCast(_contenitoreRidotto, ScrollableControl)
                If scorrevole IsNot Nothing Then scorrevole.AutoScroll = _autoScrollOriginale
            End If

            _contenitoreRidotto = Nothing
            _altezzaContenitoreOriginale = 0
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.RipristinaContenitoreDopoTastiera: " & ex.Message)
        End Try
    End Sub

#Region "Braille"

    Public ReadOnly Property BrailleModificabile As Boolean Implements IWGBrailleCampo.BrailleModificabile
        Get
            Return Not Me.ReadOnly
        End Get
    End Property

    Public Function BrailleStato(ByRef riga As String, ByRef cursore As Integer) As Boolean Implements IWGBrailleCampo.BrailleStato
        Try
            Dim testo As String = _testo
            ' Una password non va mai mostrata sulla barra se la voce non la legge.
            If IsCampoPassword AndAlso Not leggipassword Then testo = New String("*"c, If(testo, "").Length)
            WGBraille.RigaDaTesto(testo, _selectionStart, riga, cursore)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.BrailleStato: " & ex.Message)
            Return False
        End Try
    End Function

    Public ReadOnly Property BrailleInElencoCampi As Boolean Implements IWGBrailleElencoCampi.BrailleInElencoCampi
        Get
            Return NavigazioneDirettaCampi
        End Get
    End Property

    Public Sub BrailleCampoAdiacente(direzione As Integer) Implements IWGBrailleElencoCampi.BrailleCampoAdiacente
        Try
            _colonnaPreferita = -1
            _posizioneVirtualeVerticale = 0
            RaiseEvent RichiestaCampoAdiacente(Me, direzione)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.BrailleCampoAdiacente: " & ex.Message)
        End Try
    End Sub

    Public Sub BrailleInserisci(testo As String) Implements IWGBrailleCampo.BrailleInserisci
        Try
            If Me.ReadOnly OrElse String.IsNullOrEmpty(testo) Then Return
            For Each c As Char In testo
                OnCarattereInseritoDaTastieraTouch(c.ToString())
            Next
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.BrailleInserisci: " & ex.Message)
        End Try
    End Sub

    Public Sub BrailleSposta(posizioneNellaRiga As Integer) Implements IWGBrailleCampo.BrailleSposta
        Try
            Dim testo As String = _testo
            Dim inizio As Integer = Math.Min(_selectionStart, testo.Length)
            While inizio > 0 AndAlso testo(inizio - 1) <> ControlChars.Lf
                inizio -= 1
            End While
            Dim fine As Integer = inizio
            While fine < testo.Length AndAlso testo(fine) <> ControlChars.Lf
                fine += 1
            End While
            Me.SelectionLength = 0
            Me.SelectionStart = Math.Max(inizio, Math.Min(fine, inizio + posizioneNellaRiga))
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.BrailleSposta: " & ex.Message)
        End Try
    End Sub

#End Region

    Friend Sub InserisciCarattereDaTastieraTouch(carattere As String)
        OnCarattereInseritoDaTastieraTouch(carattere)
    End Sub

    Friend Sub EliminaDaTastieraTouch()
        OnEliminaRichiestoDaTastieraTouch()
    End Sub

    Friend Sub InvioDaTastieraTouch()
        OnInvioRichiestoDaTastieraTouch()
    End Sub

    Friend Sub InserisciTestoDettatoDaTastieraTouch(testo As String)
        OnTestoDettatoDaTastieraTouch(testo)
    End Sub

    Private Sub OnCarattereInseritoDaTastieraTouch(carattere As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnCarattereInseritoDaTastieraTouch), carattere)
                Return
            End If
            If [ReadOnly] OrElse String.IsNullOrEmpty(carattere) Then Return

            ' Una lettera dalla tastiera a schermo non deve mai confermare il campo: solo il tasto
            ' Invio passa oltre. CR/LF arrivano da InvioRichiesto, non da CarattereInserito.
            If carattere(0) = ControlChars.Cr OrElse carattere(0) = ControlChars.Lf Then Return

            ' Si segue lo stesso percorso della tastiera fisica: maiuscola automatica, punto di
            ' annullamento, inserimento, scorrimento e infine l'eco di digitazione, che e' quello
            ' che legge la lettera appena scritta. Inserendo il solo testo, come faceva prima, la
            ' lettera finiva nel campo senza essere annunciata.
            Dim carattereDaInserire As Char = carattere(0)

            ' Il percorso della tastiera fisica solleva KeyPress prima di inserire, e chi ascolta
            ' quell'evento reagisce di conseguenza. Saltarlo, come faceva la versione precedente,
            ' rendeva le due strade diverse proprio nel punto che conta.
            Try
                Dim argomentiTasto As New KeyPressEventArgs(carattereDaInserire)
                MyBase.OnKeyPress(argomentiTasto)
                If argomentiTasto.Handled Then Return
                carattereDaInserire = argomentiTasto.KeyChar
            Catch exKeyPress As Exception
                _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch (KeyPress): " & exKeyPress.Message)
            End Try

            Try
                If MaiuscolaAutomatica AndAlso Char.IsLetter(carattereDaInserire) AndAlso DeveInserireMaiuscola() Then
                    carattereDaInserire = Char.ToUpper(carattereDaInserire)
                End If
            Catch exMaiuscola As Exception
                _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch (maiuscola): " & exMaiuscola.Message)
            End Try

            Try
                SalvaStatoUndo()
            Catch exUndo As Exception
                _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch (undo): " & exUndo.Message)
            End Try

            SelectedText = carattereDaInserire.ToString()
            _colonnaPreferita = -1

            Try
                Me.ScrollToCaret()
            Catch exScorrimento As Exception
                _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch (scorrimento): " & exScorrimento.Message)
            End Try

            ' L'eco sta in un blocco suo: se un errore in una delle operazioni precedenti facesse
            ' saltare l'esecuzione al Catch generale, la lettera resterebbe inserita e l'annuncio
            ' non verrebbe mai eseguito, senza che nulla lo faccia notare.
            Try
                ' L'annuncio del carattere lo fa la tastiera a schermo, che legge lettere e parole
                ' secondo la propria configurazione. Ripeterlo qui sovrappone due voci e le lettere
                ' ripetute vanno perse.
            Catch exEco As Exception
                _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch (eco): " & exEco.Message)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnCarattereInseritoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnEliminaRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnEliminaRichiestoDaTastieraTouch))
                Return
            End If
            If [ReadOnly] Then Return
            If SelectionLength > 0 Then
                SelectedText = ""
            ElseIf SelectionStart > 0 Then
                SelectionStart -= 1
                SelectionLength = 1
                SelectedText = ""
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnEliminaRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnInvioRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnInvioRichiestoDaTastieraTouch))
                Return
            End If
            If [ReadOnly] Then Return
            If _multiline Then
                ' Campo multiriga: l'invio va a capo nel testo.
                SelectedText = Environment.NewLine
            Else
                ' Campo a riga singola: solo il tasto Invio passa al campo successivo.
                ' SendKeys TAB saltava i campi (TabStop = False) e un doppio tocco lettera
                ' non deve mai arrivare qui: ci arriva solo InvioRichiesto dalla tastiera.
                RaiseEvent RichiestaConfermaCampo(Me)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnInvioRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnChiudiRichiestoDaTastieraTouch()
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf OnChiudiRichiestoDaTastieraTouch))
                Return
            End If
            NascondiTastieraTouch()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnChiudiRichiestoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    Private Sub OnTestoDettatoDaTastieraTouch(testo As String)
        Try
            If Me.InvokeRequired Then
                Me.BeginInvoke(New Action(Of String)(AddressOf OnTestoDettatoDaTastieraTouch), testo)
                Return
            End If
            If [ReadOnly] OrElse String.IsNullOrEmpty(testo) Then Return
            SelectedText = testo
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnTestoDettatoDaTastieraTouch: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Vero se il fuoco e' passato alla tastiera a schermo o a un suo tasto. Toccando la tastiera
    ''' il campo perde il fuoco pur restando quello su cui si sta scrivendo: senza questa verifica
    ''' OnLostFocus nasconderebbe la tastiera al primo tasto premuto.
    ''' </summary>
    Private Function FuocoPassatoAllaTastieraTouch() As Boolean
        Try
            Dim controlloConFuoco As Control = Control.FromHandle(GetFocus())
            If controlloConFuoco Is Nothing Then
                controlloConFuoco = Control.FromChildHandle(GetFocus())
            End If

            While controlloConFuoco IsNot Nothing
                If TypeOf controlloConFuoco Is WGTastieraTouch Then Return True
                controlloConFuoco = controlloConFuoco.Parent
            End While

            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.FuocoPassatoAllaTastieraTouch: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared _rientroDaTastieraTouch As Boolean = False

    Public Shared ReadOnly Property RientroDaTastieraTouchInCorso As Boolean
        Get
            Return _rientroDaTastieraTouch
        End Get
    End Property

    Private Sub RiprendiFuocoDallaTastiera()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.IsHandleCreated OrElse Not Me.Visible OrElse Not Me.Enabled Then Return
            If Me.Focused Then Return
            _rientroDaTastieraTouch = True
            Try
                Me.Focus()
            Finally
                _rientroDaTastieraTouch = False
            End Try
        Catch ex As Exception
            _rientroDaTastieraTouch = False
            _logger.ScriviLog("WGCampiMovimento.RiprendiFuocoDallaTastiera: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        Try
            If FuocoPassatoAllaTastieraTouch() Then
                MyBase.OnLostFocus(e)
                Me.BeginInvoke(New MethodInvoker(AddressOf RiprendiFuocoDallaTastiera))
                Return
            End If

            HideCaret(Me.Handle)
            DestroyCaret()
            MyBase.OnLostFocus(e)
            RichiediRidisegno()
            NascondiTastieraTouch()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OnLostFocus: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_StatoCambiatoTastiera() Handles _touk.StatoCambiato
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(New MethodInvoker(AddressOf _touk_StatoCambiatoTastiera))
                Return
            End If
            If _touk IsNot Nothing AndAlso _touk.TouchAttivo AndAlso Me.Focused Then
                MostraTastieraTouchSeAttiva()
            ElseIf _touk Is Nothing OrElse Not _touk.TouchAttivo Then
                NascondiTastieraTouch()
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._touk_StatoCambiatoTastiera: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_TapDoppioCampo() Handles _touk.TapDoppio
        Try
            ' Premere due volte una lettera sulla tastiera a schermo non passa al campo
            ' successivo: la lettera e' gia' stata scritta al pointer-up. Solo il tasto Invio
            ' (InvioRichiesto) conferma e va oltre.
            If WGTastieraTouch.GestoSullaTastieraAttiva(_touk) Then Return
            If _touk IsNot Nothing AndAlso _touk.GestoIniziatoSuTastieraTouch() Then Return
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._touk_TapDoppioCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickAvantiTesto() Handles _touk.FlickAvanti
        Try
            If Not Me.Focused Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=True)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._touk_FlickAvantiTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_FlickIndietroTesto() Handles _touk.FlickIndietro
        Try
            If Not Me.Focused Then Return
            EseguiMovimentoTouchSecondoModalita(avanti:=False)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._touk_FlickIndietroTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiMovimentoTouchSecondoModalita(avanti As Boolean)
        Try
            If _touk Is Nothing Then Return
            If Not Me.Focused AndAlso Not _touk.EDestinatarioDiretto(Me) Then Return
            Dim direzione As Integer = If(avanti, 1, -1)
            Select Case _touk.ModalitaNavigazione
                Case WGTouk.ModalitaNavigazioneTouch.Riga
                    SpostaCursoreVerticalePubblico(direzione)
                Case WGTouk.ModalitaNavigazioneTouch.Carattere
                    SpostaCursoreOrizzontalePubblico(direzione)
                Case Else
                    ' Campo e Azioni: in un campo isolato non c'e' altro campo o azione touch
                    ' su cui muoversi, quindi non facciamo nulla in queste due modalita' qui.
            End Select
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.EseguiMovimentoTouchSecondoModalita: " & ex.Message)
        End Try
    End Sub

    Private Sub _touk_RichiestaLetturaCompleta() Handles _touk.RichiestaLetturaCompleta
        Try
            If Not Me.Focused Then Return
            If _sintesiVocale IsNot Nothing Then
                _sintesiVocale.LeggiTesto(Me.Text)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._touk_RichiestaLetturaCompleta: " & ex.Message)
        End Try
    End Sub

    Private _righeVisuali As New List(Of Tuple(Of Integer, Integer))
    Private _righeVisualiValide As Boolean = False

    Private Sub InvalidaLayoutRighe()
        _righeVisualiValide = False
    End Sub

    Private Sub AssicuraRigheVisuali()
        Try
            If _righeVisualiValide Then Return
            _righeVisuali.Clear()

            If Not _multiline Then
                _righeVisuali.Add(Tuple.Create(0, _testo.Length))
                _righeVisualiValide = True
                Return
            End If

            Dim larghezzaUtile As Integer = System.Math.Max(20, Me.Width - 12)
            Dim hdcMisura As IntPtr = GetDC(Me.Handle)
            Try
                Dim hFont As IntPtr = OttieniFontGdi()
                Dim oldFont As IntPtr = SelectObject(hdcMisura, hFont)

                Dim inizioSegmento As Integer = 0
                Dim k As Integer = 0
                While k <= _testo.Length
                    If k = _testo.Length OrElse _testo(k) = ControlChars.Lf Then
                        Dim lunghezzaSegmento As Integer = k - inizioSegmento
                        If lunghezzaSegmento = 0 Then
                            _righeVisuali.Add(Tuple.Create(inizioSegmento, 0))
                        Else
                            Dim inizioRiga As Integer = inizioSegmento
                            While inizioRiga < k
                                Dim fineRiga As Integer = inizioRiga
                                Dim ultimoSpazio As Integer = -1
                                Dim larghezzaAccumulata As Integer = 0
                                While fineRiga < k
                                    Dim misuraChar As SIZE
                                    GetTextExtentPoint32(hdcMisura, _testo(fineRiga).ToString(), 1, misuraChar)
                                    If larghezzaAccumulata + misuraChar.cx > larghezzaUtile AndAlso fineRiga > inizioRiga Then Exit While
                                    larghezzaAccumulata += misuraChar.cx
                                    If _testo(fineRiga) = " "c Then ultimoSpazio = fineRiga
                                    fineRiga += 1
                                End While
                                If fineRiga < k AndAlso ultimoSpazio > inizioRiga Then
                                    fineRiga = ultimoSpazio + 1
                                End If
                                _righeVisuali.Add(Tuple.Create(inizioRiga, fineRiga - inizioRiga))
                                inizioRiga = fineRiga
                            End While
                        End If
                        inizioSegmento = k + 1
                    End If
                    k += 1
                End While

                SelectObject(hdcMisura, oldFont)
            Finally
                ReleaseDC(Me.Handle, hdcMisura)
            End Try

            If _righeVisuali.Count = 0 Then _righeVisuali.Add(Tuple.Create(0, 0))
            _righeVisualiValide = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.AssicuraRigheVisuali: " & ex.Message)
            _righeVisuali.Clear()
            _righeVisuali.Add(Tuple.Create(0, _testo.Length))
            _righeVisualiValide = True
        End Try
    End Sub

    Private Function OttieniRighe() As String()
        Try
            AssicuraRigheVisuali()
            Dim risultato(_righeVisuali.Count - 1) As String
            For i As Integer = 0 To _righeVisuali.Count - 1
                Dim inizio As Integer = _righeVisuali(i).Item1
                Dim lunghezza As Integer = System.Math.Min(_righeVisuali(i).Item2, System.Math.Max(0, _testo.Length - inizio))
                risultato(i) = If(lunghezza > 0, _testo.Substring(inizio, lunghezza), "")
            Next
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniRighe: " & ex.Message)
            Return New String() {""}
        End Try
    End Function

    Private Function OttieniRigaEColonnaDaIndice(indice As Integer) As Tuple(Of Integer, Integer)
        Try
            AssicuraRigheVisuali()
            For i As Integer = 0 To _righeVisuali.Count - 1
                Dim inizio As Integer = _righeVisuali(i).Item1
                Dim fine As Integer = inizio + _righeVisuali(i).Item2
                Dim eUltima As Boolean = (i = _righeVisuali.Count - 1)
                Dim inizioSuccessiva As Integer = If(eUltima, Integer.MaxValue, _righeVisuali(i + 1).Item1)
                If indice >= inizio AndAlso (indice < inizioSuccessiva AndAlso indice <= fine OrElse eUltima) Then
                    Return Tuple.Create(i, System.Math.Min(indice - inizio, _righeVisuali(i).Item2))
                End If
            Next
            Dim ultima As Integer = _righeVisuali.Count - 1
            Return Tuple.Create(ultima, _righeVisuali(ultima).Item2)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniRigaEColonnaDaIndice: " & ex.Message)
            Return Tuple.Create(0, 0)
        End Try
    End Function

    Private Function OttieniIndiceDaRigaEColonna(riga As Integer, colonna As Integer) As Integer
        Try
            AssicuraRigheVisuali()
            riga = System.Math.Max(0, System.Math.Min(riga, _righeVisuali.Count - 1))
            colonna = System.Math.Max(0, System.Math.Min(colonna, _righeVisuali(riga).Item2))
            Return System.Math.Min(_righeVisuali(riga).Item1 + colonna, _testo.Length)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniIndiceDaRigaEColonna: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function SiamoPrimaRiga() As Boolean
        Try
            Return OttieniRigaEColonnaDaIndice(_selectionStart).Item1 = 0
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SiamoPrimaRiga: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function SiamoUltimaRiga() As Boolean
        Try
            Dim righe = OttieniRighe()
            Return OttieniRigaEColonnaDaIndice(_selectionStart).Item1 >= righe.Length - 1
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SiamoUltimaRiga: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub GestisciLimiteVerticale(direzione As Integer)
        Try
            _colonnaPreferita = -1

            ' Anche le note (campo su più righe): arrivati al limite del testo la freccia passa al campo vicino,
            ' come nei campi a riga singola.
            If NavigazioneDirettaCampi Then
                _posizioneVirtualeVerticale = 0
                RaiseEvent RichiestaCampoAdiacente(Me, direzione)
                Return
            End If

            _posizioneVirtualeVerticale = direzione
            _sintesiVocale.LeggiTesto(If(direzione < 0, "inizio testo", "fine testo"))
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciLimiteVerticale: " & ex.Message)
        End Try
    End Sub

    Private Function GestisciRientroDaConfineVirtuale(direzione As Integer) As Boolean
        Try
            If _posizioneVirtualeVerticale = -1 Then
                If direzione > 0 Then
                    _posizioneVirtualeVerticale = 0
                    LeggiRigaCorrenteCampo()
                Else
                    _sintesiVocale.LeggiTesto("inizio testo")
                End If
                Return True
            ElseIf _posizioneVirtualeVerticale = 1 Then
                If direzione < 0 Then
                    _posizioneVirtualeVerticale = 0
                    LeggiRigaCorrenteCampo()
                Else
                    _sintesiVocale.LeggiTesto("fine testo")
                End If
                Return True
            End If
            Return False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciRientroDaConfineVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub LeggiRigaCorrenteCampo()
        Try
            If _isCampoPassword AndAlso Not leggipassword Then
                _sintesiVocale.LeggiTesto("asterischi")
                Return
            End If
            Dim rigaColonna = OttieniRigaEColonnaDaIndice(_selectionStart)
            Dim righe = OttieniRighe()
            Dim testoRiga As String = If(rigaColonna.Item1 >= 0 AndAlso rigaColonna.Item1 < righe.Length, righe(rigaColonna.Item1), "")
            If String.IsNullOrEmpty(testoRiga) AndAlso Not _multiline AndAlso _testo.Length > 0 Then testoRiga = _testo
            If String.IsNullOrEmpty(testoRiga) Then
                _sintesiVocale.LeggiTesto("Riga vuota")
            Else
                _sintesiVocale.LeggiTesto(testoRiga)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.LeggiRigaCorrenteCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaCursoreVerticale(direzione As Integer, shift As Boolean)
        Try
            Dim rigaColonna = OttieniRigaEColonnaDaIndice(_selectionStart)
            If _colonnaPreferita < 0 Then _colonnaPreferita = rigaColonna.Item2
            Dim rigaTarget As Integer = rigaColonna.Item1 + direzione
            Dim nuovoIndice As Integer = OttieniIndiceDaRigaEColonna(rigaTarget, _colonnaPreferita)

            If Not shift Then
                _anchorPoint = -1
                Me.Select(nuovoIndice, 0)
            Else
                If _anchorPoint = -1 Then _anchorPoint = _selectionStart
                Dim inizio = Math.Min(_anchorPoint, nuovoIndice)
                Dim lunghezza = Math.Abs(_anchorPoint - nuovoIndice)
                Me.Select(inizio, lunghezza)
            End If
            Me.ScrollToCaret()

            Dim righeTarget = OttieniRighe()
            Dim rigaEffettiva As Integer = Math.Max(0, Math.Min(rigaTarget, righeTarget.Length - 1))
            Dim testoRiga As String = righeTarget(rigaEffettiva)

            If _isCampoPassword AndAlso Not leggipassword Then
                _sintesiVocale.LeggiTesto("asterischi")
            ElseIf String.IsNullOrEmpty(testoRiga) Then
                _sintesiVocale.LeggiTesto("Riga vuota")
            Else
                _sintesiVocale.LeggiTesto(testoRiga)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SpostaCursoreVerticale: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaStatoUndo()
        Try
            Dim s As StatoTesto
            s.Testo = Me.Text
            s.InizioSel = Me.SelectionStart
            s.LunghSel = Me.SelectionLength
            _undoStack.Push(s)
            _redoStack.Clear()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SalvaStatoUndo: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriGuidaCampo()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf ApriGuidaCampo))
            Return
        End If
        Try
            Dim tipoCampo As String = TryCast(Me.Tag, String)
            WGGuida.MostraGuidaTipoCampo(tipoCampo)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.ApriGuidaCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggioCampo(messaggio As String)
        If Me.InvokeRequired Then
            Me.Invoke(New Action(Of String)(AddressOf MostraMessaggioCampo), messaggio)
            Return
        End If
        Try
            Dim msg As New WGMessaggi
            Try
                msg.Messaggio(messaggio)
                msg.ShowDialog()
            Finally
                RipristinaFocusCampo()
            End Try
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.MostraMessaggioCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaFocusCampo()
        Try
            Dim attesaMassima As DateTime = DateTime.UtcNow.AddSeconds(2)
            While Not Me.IsDisposed AndAlso Me.IsHandleCreated AndAlso Not Me.Visible AndAlso DateTime.UtcNow < attesaMassima
                Application.DoEvents()
                Thread.Sleep(15)
            End While

            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then
                Return
            End If

            Try
                Me.Focus()
            Catch exFocus As Exception
                _logger.ScriviLog("RipristinaFocusCampo (Focus): " & exFocus.Message)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("RipristinaFocusCampo: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf Annulla))
            Return
        End If
        Try
            If Me.ReadOnly Then
                MostraMessaggioCampo("Il campo è di sola lettura")
                Return
            End If
            If _undoStack.Count = 0 Then
                _sintesiVocale.LeggiTesto("Niente da annullare")
                Return
            End If

            Dim corrente As StatoTesto
            corrente.Testo = Me.Text
            corrente.InizioSel = Me.SelectionStart
            corrente.LunghSel = Me.SelectionLength
            _redoStack.Push(corrente)

            Dim precedente As StatoTesto = _undoStack.Pop()
            Me.Text = precedente.Testo
            Dim posizione As Integer = Math.Min(Math.Max(precedente.InizioSel, 0), Me.TextLength)
            Me.SelectionStart = posizione
            Me.SelectionLength = 0
            _sintesiVocale.LeggiTesto("Annullato")
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.Annulla: " & ex.Message)
        End Try
    End Sub

    Public Sub Ripristina()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf Ripristina))
            Return
        End If
        Try
            If Me.ReadOnly Then
                MostraMessaggioCampo("Il campo è di sola lettura")
                Return
            End If
            If _redoStack.Count = 0 Then
                _sintesiVocale.LeggiTesto("Niente da ripristinare")
                Return
            End If

            Dim corrente As StatoTesto
            corrente.Testo = Me.Text
            corrente.InizioSel = Me.SelectionStart
            corrente.LunghSel = Me.SelectionLength
            _undoStack.Push(corrente)

            Dim successivo As StatoTesto = _redoStack.Pop()
            Me.Text = successivo.Testo
            Dim posizione As Integer = Math.Min(Math.Max(successivo.InizioSel, 0), Me.TextLength)
            Me.SelectionStart = posizione
            Me.SelectionLength = 0
            _sintesiVocale.LeggiTesto("Ripristinato")
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.Ripristina: " & ex.Message)
        End Try
    End Sub

    Public Overloads Sub [Select](start As Integer, length As Integer)
        Try
            _selectionStart = Math.Max(0, Math.Min(start, _testo.Length))
            _selectionLength = Math.Max(0, Math.Min(length, _testo.Length - _selectionStart))
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.Select: " & ex.Message)
        End Try
    End Sub

    Public Sub SelectAll()
        Try
            _selectionStart = 0
            _selectionLength = _testo.Length
            _anchorPoint = 0
            _colonnaPreferita = -1
            _vuotoOrizzontaleAnnunciato = False
            _posizioneVirtualeVerticale = 0
            _confermatoLimiteVerticale = False
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SelectAll: " & ex.Message)
        End Try
    End Sub

    Private Function PuoGestireTasti() As Boolean
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return False
            If Not Me.IsHandleCreated Then Return False
            If Me.Focused Then Return True
            If Not Me.Visible OrElse Not Me.Enabled Then Return False
            Dim contenitore As ContainerControl = TryCast(Me.Parent, ContainerControl)
            If contenitore Is Nothing Then Return False
            If Not contenitore.Visible Then Return False
            If contenitore.ActiveControl IsNot Me Then Return False
            Dim finestra As Form = Me.FindForm()
            Dim focusInFamiglia As Boolean = contenitore.ContainsFocus
            If Not focusInFamiglia AndAlso finestra IsNot Nothing Then focusInFamiglia = finestra.ContainsFocus
            If Not focusInFamiglia Then Return False
            Try
                Me.Focus()
            Catch exFocus As Exception
                _logger.ScriviLog("WGCampiMovimento.PuoGestireTasti (Focus): " & exFocus.Message)
            End Try
            Return True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.PuoGestireTasti: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RichiediRidisegno()
        Try
            If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.RichiediRidisegno: " & ex.Message)
        End Try
    End Sub

    Public Sub ScrollToCaret()
        Try
            If _multiline Then
                Dim rigaColonna = OttieniRigaEColonnaDaIndice(_selectionStart)
                Dim righeVisibili As Integer = Math.Max(1, AreaCampoGdiTouk().Height \ _altezzaRiga)
                If rigaColonna.Item1 < _primaRigaVisibile Then
                    _primaRigaVisibile = rigaColonna.Item1
                ElseIf rigaColonna.Item1 >= _primaRigaVisibile + righeVisibili Then
                    _primaRigaVisibile = rigaColonna.Item1 - righeVisibili + 1
                End If
                If _primaRigaVisibile < 0 Then _primaRigaVisibile = 0
            Else
                Dim hdcScreen As IntPtr = GetDC(Me.Handle)
                Try
                    Dim hFont As IntPtr = OttieniFontGdi()
                    Dim oldFont As IntPtr = SelectObject(hdcScreen, hFont)
                    Dim testoPrimaDelCaret As String = OttieniTestoVisualizzato().Substring(0, Math.Min(_selectionStart, OttieniTestoVisualizzato().Length))
                    Dim misura As SIZE
                    GetTextExtentPoint32(hdcScreen, testoPrimaDelCaret, testoPrimaDelCaret.Length, misura)
                    SelectObject(hdcScreen, oldFont)
                    Dim xCaret As Integer = misura.cx
                    Dim larghezzaVisibile As Integer = Math.Max(1, Me.Width - 10)
                    If xCaret - _scrollOffsetX > larghezzaVisibile Then
                        _scrollOffsetX = xCaret - larghezzaVisibile
                    ElseIf xCaret - _scrollOffsetX < 0 Then
                        _scrollOffsetX = xCaret
                    End If
                    If _scrollOffsetX < 0 Then _scrollOffsetX = 0
                Finally
                    ReleaseDC(Me.Handle, hdcScreen)
                End Try
            End If
            PosizionaCaretSuSchermo()
            RichiediRidisegno()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.ScrollToCaret: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCursoreVerticalePubblico(direzione As Integer)
        Try
            If [ReadOnly] Then Return
            If Not _multiline Then Return
            If direzione < 0 AndAlso SiamoPrimaRiga() Then Return
            If direzione > 0 AndAlso SiamoUltimaRiga() Then Return
            SpostaCursoreVerticale(direzione, False)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SpostaCursoreVerticalePubblico: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCursoreOrizzontalePubblico(direzione As Integer)
        Try
            If [ReadOnly] Then Return
            GestisciMovimentoOrizzontale(direzione > 0, False, False)
            _colonnaPreferita = -1
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SpostaCursoreOrizzontalePubblico: " & ex.Message)
        End Try
    End Sub

    Private Function SimboloAltGr(baseKey As Keys, shift As Boolean) As String
        Select Case baseKey
            Case Keys.Oem3, Keys.Oem1
                Return "@"
            Case Keys.Oem7
                Return "#"
            Case Keys.Oem5, Keys.OemOpenBrackets
                Return If(shift, "{", "[")
            Case Keys.Oemplus, Keys.OemCloseBrackets
                Return If(shift, "}", "]")
            Case Keys.E
                Return "€"
        End Select
        Return ""
    End Function

    Private Function SimboloAltGrDaCarattere(carattere As String, shift As Boolean) As String
        Select Case carattere
            Case "ò", "Ò"
                Return "@"
            Case "à", "À"
                Return "#"
            Case "è", "È"
                Return If(shift, "{", "[")
            Case "+"
                Return If(shift, "}", "]")
            Case "e", "E"
                Return "€"
        End Select
        Return ""
    End Function

    Private Function CaratterePianoTasto(virtualKey As UInteger) As String
        Try
            Dim statoTastieraPiano(255) As Byte
            If Not GetKeyboardState(statoTastieraPiano) Then Return ""
            For i As Integer = 0 To 255
                statoTastieraPiano(i) = 0
            Next
            statoTastieraPiano(CInt(Keys.CapsLock)) = If(Control.IsKeyLocked(Keys.CapsLock), CByte(1), CByte(0))
            Dim scanCodePiano As UInteger = MapVirtualKey(virtualKey, 0)
            Dim hklPiano As IntPtr = GetKeyboardLayout(0)
            Dim bufferPiano As New StringBuilder(8)
            Dim risultatoPiano As Integer = ToUnicodeEx(virtualKey, scanCodePiano, statoTastieraPiano, bufferPiano, bufferPiano.Capacity, 0, hklPiano)
            If risultatoPiano > 0 Then Return bufferPiano.ToString(0, risultatoPiano)
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("CaratterePianoTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ConvertiTastoInCarattere(tasto As Keys) As String
        Try
            Dim virtualKey As Keys = tasto And Keys.KeyCode

            Select Case virtualKey
                Case Keys.Back, Keys.Delete, Keys.Enter, Keys.Escape, Keys.Tab, Keys.Left, Keys.Right, Keys.Up, Keys.Down,
                     Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Insert, Keys.ShiftKey, Keys.ControlKey,
                     Keys.LControlKey, Keys.RControlKey, Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin,
                     Keys.Apps, Keys.CapsLock, Keys.NumLock, Keys.Scroll, Keys.PrintScreen, Keys.Pause
                    Return ""
            End Select

            If virtualKey >= Keys.F1 AndAlso virtualKey <= Keys.F24 Then Return ""

            ' Stessa fotografia usata in _tastiera_KeyDown (questa funzione gira solo da li'): vedi il commento.
            Dim altDestroReale As Boolean = (_tastiera IsNot Nothing AndAlso _tastiera.AltGrUltimoTasto)

            If altDestroReale Then
                Dim risultatoAltGr As String = SimboloAltGr(virtualKey, _shiftPremutoDaHook)
                Dim caratterePiano As String = ""
                If risultatoAltGr = "" Then
                    caratterePiano = CaratterePianoTasto(CUInt(virtualKey))
                    risultatoAltGr = SimboloAltGrDaCarattere(caratterePiano, _shiftPremutoDaHook)
                End If
                If wgcontrols.WGTastiera.LoggingVerboso Then
                End If
                If risultatoAltGr <> "" Then Return risultatoAltGr
            End If

            If _shiftPremutoDaHook AndAlso virtualKey >= Keys.D0 AndAlso virtualKey <= Keys.D9 Then
                Select Case virtualKey
                    Case Keys.D1 : Return "!"
                    Case Keys.D2 : Return """"
                    Case Keys.D3 : Return "£"
                    Case Keys.D4 : Return "$"
                    Case Keys.D5 : Return "%"
                    Case Keys.D6 : Return "&"
                    Case Keys.D7 : Return "/"
                    Case Keys.D8 : Return "("
                    Case Keys.D9 : Return ")"
                    Case Keys.D0 : Return "="
                End Select
            End If

            Dim statoTastiera(255) As Byte
            GetKeyboardState(statoTastiera)

            statoTastiera(CInt(Keys.ShiftKey)) = If(_shiftPremutoDaHook, CByte(&H80), CByte(0))
            statoTastiera(CInt(Keys.LShiftKey)) = If(_shiftPremutoDaHook, CByte(&H80), CByte(0))
            statoTastiera(CInt(Keys.CapsLock)) = If(Control.IsKeyLocked(Keys.CapsLock), CByte(1), CByte(0))

            If altDestroReale Then
                statoTastiera(CInt(Keys.ControlKey)) = CByte(&H80)
                statoTastiera(CInt(Keys.Menu)) = CByte(&H80)
                statoTastiera(CInt(Keys.RMenu)) = CByte(&H80)
            Else
                statoTastiera(CInt(Keys.ControlKey)) = 0
                statoTastiera(CInt(Keys.Menu)) = 0
                statoTastiera(CInt(Keys.RMenu)) = 0
                statoTastiera(CInt(Keys.LMenu)) = 0
            End If

            Dim scanCode As UInteger = MapVirtualKey(CUInt(virtualKey), 0)
            Dim hkl As IntPtr = GetKeyboardLayout(0)
            Dim buffer As New StringBuilder(8)
            Dim risultato As Integer = ToUnicodeEx(CUInt(virtualKey), scanCode, statoTastiera, buffer, buffer.Capacity, 0, hkl)

            If risultato > 0 Then
                Return buffer.ToString(0, risultato)
            End If
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.ConvertiTastoInCarattere: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub _tastiera_ShiftPremuto(sender As Object, e As EventArgs) Handles _tastiera.ShiftPremuto
        Try
            _shiftPremutoDaHook = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ShiftPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ShiftRilasciato
        Try
            _shiftPremutoDaHook = False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ShiftRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        Try
            _altPremutoDaHook = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_AltPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltRilasciato
        Try
            _altPremutoDaHook = False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_AltRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltDestroPremuto
        Try
            _altDestroPremutoDaHook = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_AltDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_AltDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltDestroRilasciato
        Try
            _altDestroPremutoDaHook = False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_AltDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroPremuto
        Try
            _ctrlSinistroPremutoDaHook = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ControlSinistroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlSinistroRilasciato
        Try
            _ctrlSinistroPremutoDaHook = False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ControlSinistroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlDestroPremuto(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroPremuto
        Try
            _ctrlDestroPremutoDaHook = True
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ControlDestroPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlDestroRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ControlDestroRilasciato
        Try
            _ctrlDestroPremutoDaHook = False
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_ControlDestroRilasciato: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If Not PuoGestireTasti() Then Return
            If (tasto And Keys.KeyCode) = Keys.None Then Return

            Dim shiftPremuto As Boolean = _shiftPremutoDaHook
            Dim altPremuto As Boolean = _altPremutoDaHook
            ' AltGr si legge dalla fotografia scattata dall'hook nell'istante in cui il tasto e' stato premuto
            ' (WGTastiera.AltGrUltimoTasto), non dallo stato di adesso. Tra la pressione e il momento in cui
            ' questo gestore gira ci sono la coda degli eventi e il thread dell'interfaccia: su un computer
            ' lento o occupato AltGr puo' essere gia' stato rilasciato, e letto in diretta la chiocciola
            ' usciva come "o" accentata oppure spariva. Vale anche al contrario: un tasto premuto subito
            ' prima di AltGr non viene piu' scambiato per una combinazione con AltGr.
            Dim altDestro As Boolean = (_tastiera IsNot Nothing AndAlso _tastiera.AltGrUltimoTasto)
            Dim ctrlPremuto As Boolean = _ctrlSinistroPremutoDaHook OrElse _ctrlDestroPremutoDaHook

            Dim tastoBase As Keys = tasto And Keys.KeyCode

            If tastoBase <> Keys.Up AndAlso tastoBase <> Keys.Down Then
                _confermatoLimiteVerticale = False
                _posizioneVirtualeVerticale = 0
            End If

            If tastoBase <> Keys.Enter Then
                _invioConsecutivi = 0
            End If

            If altPremuto AndAlso Not altDestro Then Return

            Select Case tastoBase
                Case Keys.F2
                    ApriGuidaCampo()
                    tasto = Keys.None
                    Return
                Case Keys.LControlKey, Keys.RControlKey
                    Return
                Case Keys.Left
                    GestisciMovimentoOrizzontale(False, ctrlPremuto, shiftPremuto)
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
                Case Keys.Right
                    GestisciMovimentoOrizzontale(True, ctrlPremuto, shiftPremuto)
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
                Case Keys.Up
                    If GestisciRientroDaConfineVirtuale(-1) Then
                        tasto = Keys.None
                        Return
                    End If
                    If _multiline AndAlso Not SiamoPrimaRiga() Then
                        SpostaCursoreVerticale(-1, shiftPremuto)
                    Else
                        GestisciLimiteVerticale(-1)
                    End If
                    tasto = Keys.None
                    Return
                Case Keys.Down
                    If Control.IsKeyLocked(Keys.Insert) Then Return
                    If GestisciRientroDaConfineVirtuale(1) Then
                        tasto = Keys.None
                        Return
                    End If
                    If _multiline AndAlso Not SiamoUltimaRiga() Then
                        SpostaCursoreVerticale(1, shiftPremuto)
                    Else
                        GestisciLimiteVerticale(1)
                    End If
                    tasto = Keys.None
                    Return
                Case Keys.Home
                    If ctrlPremuto Then
                        _anchorPoint = -1
                        Me.Select(0, 0)
                        Me.ScrollToCaret()
                        _sintesiVocale.LeggiTesto("inizio testo")
                        LeggeCarattereCorrente(False)
                    Else
                        GestisciHome(shiftPremuto)
                    End If
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
                Case Keys.End
                    If ctrlPremuto Then
                        _anchorPoint = -1
                        Me.Select(_testo.Length, 0)
                        Me.ScrollToCaret()
                        _sintesiVocale.LeggiTesto("fine testo")
                    Else
                        GestisciEnd(shiftPremuto)
                    End If
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
                Case Keys.PageUp
                    GestisciPagina(-1)
                    tasto = Keys.None
                    Return
                Case Keys.PageDown
                    GestisciPagina(1)
                    tasto = Keys.None
                    Return
                Case Keys.Enter
                    If _multiline AndAlso Not ctrlPremuto Then
                        If Me.ReadOnly Then
                            RaiseEvent RichiestaConfermaCampo(Me)
                            tasto = Keys.None
                            Return
                        End If
                        _invioConsecutivi += 1
                        If _invioConsecutivi >= 3 Then
                            _invioConsecutivi = 0
                            Try
                                Dim testoAttuale As String = _testo
                                Dim rimossi As Integer = 0
                                While testoAttuale.EndsWith(ControlChars.Lf) AndAlso rimossi < 2
                                    testoAttuale = testoAttuale.Substring(0, testoAttuale.Length - 1)
                                    rimossi += 1
                                End While
                                _testo = testoAttuale
                                _selectionStart = System.Math.Min(_selectionStart, _testo.Length)
                                _selectionLength = 0
                            Catch exPulizia As Exception
                                _logger.ScriviLog("WGCampiMovimento pulizia triplo invio: " & exPulizia.Message)
                            End Try
                            RaiseEvent RichiestaConfermaCampo(Me)
                            tasto = Keys.None
                            Return
                        End If
                        SalvaStatoUndo()
                        Me.SelectedText = ControlChars.Lf.ToString()
                        _colonnaPreferita = -1
                        Me.ScrollToCaret()
                        _sintesiVocale.LeggiTesto("A capo")
                        tasto = Keys.None
                        Return
                    Else
                        _invioConsecutivi = 0
                    End If
                Case Keys.A
                    If ctrlPremuto Then
                        SelezionaTutto()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.C
                    If ctrlPremuto Then
                        CopiaTesto()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.X
                    If ctrlPremuto Then
                        TagliaTesto()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.V
                    If ctrlPremuto Then
                        IncollaTesto()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.Z
                    If ctrlPremuto Then
                        Annulla()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.Y
                    If ctrlPremuto Then
                        Ripristina()
                        tasto = Keys.None
                        Return
                    End If
                Case Keys.Back
                    If ctrlPremuto Then
                        GestisciCtrlBackspace()
                    Else
                        GestisciBackspace()
                    End If
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
                Case Keys.Delete
                    If ctrlPremuto Then
                        GestisciCtrlDelete()
                    Else
                        GestisciDelete()
                    End If
                    _colonnaPreferita = -1
                    tasto = Keys.None
                    Return
            End Select

            If ctrlPremuto AndAlso Not altDestro Then Return

            Dim strCarattere As String = ConvertiTastoInCarattere(tasto)

            If Not String.IsNullOrEmpty(strCarattere) AndAlso Not Char.IsControl(strCarattere(0)) Then
                Dim charInserito As Char = strCarattere(0)

                Dim kpe As New KeyPressEventArgs(charInserito)
                MyBase.OnKeyPress(kpe)
                If kpe.Handled Then
                    tasto = Keys.None
                    Return
                End If
                charInserito = kpe.KeyChar

                If Not Me.ReadOnly Then
                    If MaiuscolaAutomatica AndAlso Char.IsLetter(charInserito) AndAlso DeveInserireMaiuscola() Then
                        charInserito = Char.ToUpper(charInserito)
                    End If
                    SalvaStatoUndo()
                    Me.SelectedText = charInserito.ToString()
                    _colonnaPreferita = -1
                    Me.ScrollToCaret()
                    GestisciEcoDigitazione(charInserito)
                End If

                tasto = Keys.None
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento._tastiera_KeyDown: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciMovimentoOrizzontale(avanti As Boolean, ctrl As Boolean, shift As Boolean)
        Try
            Dim textLen As Integer = Me.TextLength
            Dim oldLen As Integer = Me.SelectionLength

            Dim cursoreAttuale As Integer
            If oldLen > 0 AndAlso _anchorPoint <> -1 Then
                If Me.SelectionStart = _anchorPoint Then
                    cursoreAttuale = Me.SelectionStart + oldLen
                Else
                    cursoreAttuale = Me.SelectionStart
                End If
            Else
                cursoreAttuale = Me.SelectionStart
            End If

            Dim readBoundary As String = ""
            If avanti AndAlso cursoreAttuale >= textLen Then
                readBoundary = "fine testo"
            ElseIf Not avanti AndAlso cursoreAttuale <= 0 Then
                readBoundary = "inizio testo"
            End If

            If textLen > 0 Then
                _vuotoOrizzontaleAnnunciato = False
            End If

            If Not shift Then
                _anchorPoint = -1
            ElseIf _anchorPoint = -1 Then
                _anchorPoint = Me.SelectionStart
            End If

            Dim nuovoCursore As Integer = cursoreAttuale

            If ctrl Then
                If avanti Then
                    nuovoCursore = TrovaInizioProssimaParola(cursoreAttuale)
                Else
                    nuovoCursore = TrovaInizioPrecedenteParola(cursoreAttuale)
                End If
            Else
                If avanti Then
                    nuovoCursore = Math.Min(cursoreAttuale + 1, textLen)
                Else
                    nuovoCursore = Math.Max(cursoreAttuale - 1, 0)
                End If
            End If

            Dim isDeselecting As Boolean = False
            If oldLen > 0 Then
                If (cursoreAttuale > _anchorPoint AndAlso nuovoCursore < cursoreAttuale) OrElse
                   (cursoreAttuale < _anchorPoint AndAlso nuovoCursore > cursoreAttuale) Then
                    isDeselecting = True
                End If
            End If

            If shift Then
                Dim start As Integer = Math.Min(_anchorPoint, nuovoCursore)
                Dim length As Integer = Math.Abs(_anchorPoint - nuovoCursore)
                Me.Select(start, length)
            Else
                Me.Select(nuovoCursore, 0)
            End If

            Me.ScrollToCaret()

            If shift Then
                Dim startRead As Integer = Math.Min(cursoreAttuale, nuovoCursore)
                Dim lenRead As Integer = Math.Abs(cursoreAttuale - nuovoCursore)

                If lenRead > 0 AndAlso startRead + lenRead <= Me.TextLength Then
                    Dim testoLetto As String = Me.Text.Substring(startRead, lenRead)
                    Dim prefix As String = If(isDeselecting, "Deselezionato ", "Selezionato ")

                    If _isCampoPassword AndAlso Not leggipassword Then
                        _sintesiVocale.LeggiTesto(prefix & "asterischi")
                    Else
                        _sintesiVocale.LeggiTesto(prefix & testoLetto)
                    End If
                ElseIf readBoundary <> "" Then
                    _sintesiVocale.LeggiTesto(readBoundary)
                End If
            Else
                If readBoundary <> "" AndAlso oldLen = 0 Then
                    ' Anche a campo vuoto si dice il limite, "inizio testo" o "fine testo", sempre.
                    _sintesiVocale.LeggiTesto(readBoundary, False, "Alta")
                Else
                    If ctrl Then
                        LeggeParolaCorrente(nuovoCursore)
                    Else
                        Dim indiceLettura As Integer = If(avanti, nuovoCursore - 1, nuovoCursore)
                        LeggeCarattereCorrente(False, indiceLettura)
                    End If
                End If
            End If

        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciMovimentoOrizzontale: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaInizioProssimaParola(startIndex As Integer) As Integer
        Try
            Dim testostr As String = Me.Text
            Dim len As Integer = testostr.Length
            If startIndex >= len Then Return len

            Dim i As Integer = startIndex
            While i < len AndAlso Not Char.IsWhiteSpace(testostr(i)) AndAlso Not Char.IsPunctuation(testostr(i))
                i += 1
            End While
            While i < len AndAlso (Char.IsWhiteSpace(testostr(i)) OrElse Char.IsPunctuation(testostr(i)))
                i += 1
            End While

            Return i
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.TrovaInizioProssimaParola: " & ex.Message)
            Return startIndex
        End Try
    End Function

    Private Function TrovaInizioPrecedenteParola(startIndex As Integer) As Integer
        Try
            Dim testostr As String = Me.Text
            If startIndex <= 0 Then Return 0

            Dim i As Integer = startIndex - 1
            While i >= 0 AndAlso (Char.IsWhiteSpace(testostr(i)) OrElse Char.IsPunctuation(testostr(i)))
                i -= 1
            End While
            While i >= 0 AndAlso Not Char.IsWhiteSpace(testostr(i)) AndAlso Not Char.IsPunctuation(testostr(i))
                i -= 1
            End While

            Return i + 1
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.TrovaInizioPrecedenteParola: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function TestoPerCarattereSingolo(c As Char) As String
        Try
            Select Case c
                Case "à"c, "è"c, "é"c, "ì"c, "ò"c, "ù"c, "À"c, "È"c, "É"c, "Ì"c, "Ò"c, "Ù"c
                    Dim accentata As String = _sintesiVocale.ConvertiLetteraAccentataPerLettura(c)
                    If Not String.IsNullOrWhiteSpace(accentata) Then Return accentata
                    Return c.ToString()
                Case ControlChars.Lf, ControlChars.Cr
                    Return "a capo"
                Case ControlChars.Tab
                    Return "tabulazione"
                Case " "c
                    Return "spazio"
            End Select

            If Char.IsDigit(c) OrElse Char.IsLetter(c) Then Return c.ToString()

            Dim tradotto As String = WGSegni.Converti(c.ToString())
            If Not String.IsNullOrWhiteSpace(tradotto) Then Return tradotto
            If Char.IsWhiteSpace(c) Then Return "spazio"
            If Char.IsControl(c) Then Return ""
            Return c.ToString()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.TestoPerCarattereSingolo: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Private Sub LeggeCarattereCorrente(isSelection As Boolean, Optional customIndex As Integer = -1)
        Try
            Dim index As Integer = If(customIndex <> -1, customIndex, Me.SelectionStart)

            If index >= 0 AndAlso index < Me.TextLength Then
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("asterisco")
                Else
                    Dim c As Char = Me.Text(index)
                    If c = ControlChars.Lf Then
                        _sintesiVocale.LeggiTesto("a capo")
                    ElseIf Char.IsLetter(c) Then
                        If Char.IsUpper(c) Then
                            _sintesiVocale.LeggiTesto(c.ToString() & " maiuscola")
                        Else
                            _sintesiVocale.LeggiTesto(c.ToString())
                        End If
                    Else
                        Dim tradotto As String = TestoPerCarattereSingolo(c)
                        If String.IsNullOrWhiteSpace(tradotto) Then tradotto = c.ToString()
                        If Not String.IsNullOrEmpty(tradotto) Then _sintesiVocale.LeggiTesto(tradotto)
                    End If
                End If
            ElseIf Me.TextLength = 0 Then
                _sintesiVocale.LeggiTesto("Vuoto")
            ElseIf index >= Me.TextLength Then
                _sintesiVocale.LeggiTesto("fine testo")
            Else
                _sintesiVocale.LeggiTesto("inizio testo")
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.LeggeCarattereCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggeParolaCorrente(Optional customIndex As Integer = -1)
        Try
            If _isCampoPassword AndAlso Not leggipassword Then
                _sintesiVocale.LeggiTesto("asterischi")
                Return
            End If
            Dim wordInfo = OttieniParolaAlCursore(customIndex)
            If wordInfo Is Nothing Then
                LeggeCarattereCorrente(False, customIndex)
                Return
            End If
            Dim parola As String = wordInfo.Item1.Trim()
            If String.IsNullOrEmpty(parola) Then
                LeggeCarattereCorrente(False, customIndex)
            Else
                _sintesiVocale.LeggiTesto(parola)
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.LeggeParolaCorrente: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniParolaAlCursore(Optional customIndex As Integer = -1) As Tuple(Of String, Integer, Integer)
        Try
            Dim cursorPos As Integer = If(customIndex <> -1, customIndex, Me.SelectionStart)
            Dim testolen As Integer = Me.TextLength

            If testolen = 0 OrElse cursorPos < 0 Then Return Nothing

            Dim testostr As String = Me.Text
            Dim wordSeparators As Char() = {" "c, vbCr, vbLf, vbTab, "."c, ","c, ";"c, ":"c, "!"c, "?"c, """"c, "("c, ")"c, "["c, "]"c, "{"c, "}"c, "•"c, "-"c, "/"c, "\"c, "|"c}

            Dim startIndex As Integer = cursorPos

            If startIndex > 0 AndAlso (startIndex = testolen OrElse wordSeparators.Contains(testostr(startIndex))) Then
                startIndex -= 1
            End If

            If startIndex >= 0 AndAlso startIndex < testolen AndAlso wordSeparators.Contains(testostr(startIndex)) Then
                Return Nothing
            End If

            Dim wordStart As Integer = testostr.LastIndexOfAny(wordSeparators, startIndex)
            wordStart = If(wordStart = -1, 0, wordStart + 1)

            Dim wordEnd As Integer = testostr.IndexOfAny(wordSeparators, wordStart)
            If wordEnd = -1 Then wordEnd = testolen

            If wordStart >= wordEnd Then Return Nothing

            Dim word As String = testostr.Substring(wordStart, wordEnd - wordStart)
            Return Tuple.Create(word, wordStart, word.Length)

        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniParolaAlCursore: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub GestisciHome(shift As Boolean)
        Try
            Dim rigaColonna = OttieniRigaEColonnaDaIndice(Me.SelectionStart)
            Dim inizioRiga As Integer = If(_multiline, OttieniIndiceDaRigaEColonna(rigaColonna.Item1, 0), 0)

            If shift Then
                If _anchorPoint = -1 Then _anchorPoint = Me.SelectionStart
                Dim inizio = Math.Min(_anchorPoint, inizioRiga)
                Dim lunghezza = Math.Abs(_anchorPoint - inizioRiga)
                Me.Select(inizio, lunghezza)
                _sintesiVocale.LeggiTesto("Selezionato inizio " & If(_multiline, "riga", "testo"))
            Else
                _anchorPoint = -1
                Me.SelectionStart = inizioRiga
                Me.SelectionLength = 0
                _sintesiVocale.LeggiTesto("inizio " & If(_multiline, "riga", "testo"))
                LeggeCarattereCorrente(False)
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciHome: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciEnd(shift As Boolean)
        Try
            Dim rigaColonna = OttieniRigaEColonnaDaIndice(Me.SelectionStart)
            Dim righe = OttieniRighe()
            Dim fineRiga As Integer = If(_multiline, OttieniIndiceDaRigaEColonna(rigaColonna.Item1, righe(rigaColonna.Item1).Length), Me.TextLength)

            If shift Then
                If _anchorPoint = -1 Then _anchorPoint = Me.SelectionStart
                Dim inizio = Math.Min(_anchorPoint, fineRiga)
                Dim lunghezza = Math.Abs(_anchorPoint - fineRiga)
                Me.Select(inizio, lunghezza)
                _sintesiVocale.LeggiTesto("Selezionato fine " & If(_multiline, "riga", "testo"))
            Else
                _anchorPoint = -1
                Me.SelectionStart = fineRiga
                Me.SelectionLength = 0
                _sintesiVocale.LeggiTesto("fine " & If(_multiline, "riga", "testo"))
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciEnd: " & ex.Message)
        End Try
    End Sub

    Public Sub SelezionaTutto()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf SelezionaTutto))
            Return
        End If
        Try
            If Me.TextLength > 0 Then
                Me.SelectAll()
                _sintesiVocale.LeggiTesto("Selezionato tutto")
            Else
                MostraMessaggioCampo("Non è presente testo da poter selezionare")
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.SelezionaTutto: " & ex.Message)
        End Try
    End Sub

    Public Sub CopiaTesto()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf CopiaTesto))
            Return
        End If
        Try
            If Me.SelectionLength > 0 Then
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("Impossibile copiare il contenuto di un campo password protetto")
                    Return
                End If
                Clipboard.SetText(Me.SelectedText)
                _sintesiVocale.LeggiTesto("Copiato negli appunti")
            Else
                MostraMessaggioCampo("Nessun testo selezionato")
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.CopiaTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub TagliaTesto()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf TagliaTesto))
            Return
        End If
        Try
            If Me.ReadOnly Then
                MostraMessaggioCampo("Il campo è di sola lettura")
            ElseIf Me.SelectionLength > 0 Then
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("Impossibile tagliare il contenuto di un campo password protetto")
                    Return
                End If
                SalvaStatoUndo()
                Clipboard.SetText(Me.SelectedText)
                Me.SelectedText = ""
                Me.ScrollToCaret()
                _sintesiVocale.LeggiTesto("Tagliato")
            Else
                MostraMessaggioCampo("Nessun testo selezionato")
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.TagliaTesto: " & ex.Message)
        End Try
    End Sub

    Public Sub IncollaTesto()
        If Me.InvokeRequired Then
            Me.Invoke(New MethodInvoker(AddressOf IncollaTesto))
            Return
        End If
        Try
            If Me.ReadOnly Then
                MostraMessaggioCampo("Il campo è di sola lettura")
            ElseIf Clipboard.ContainsText Then
                SalvaStatoUndo()
                Me.SelectedText = Clipboard.GetText()
                Me.ScrollToCaret()
                _sintesiVocale.LeggiTesto("Incollato")
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.IncollaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciBackspace()
        Try
            If Me.ReadOnly Then Return
            If Me.TextLength > 0 Then
                _vuotoOrizzontaleAnnunciato = False
            End If
            If Me.SelectionLength > 0 Then
                SalvaStatoUndo()
                Me.SelectedText = ""
                ' Se la selezione era tutto il testo il campo è vuoto e il cursore all'inizio: lo si dice.
                If Me.TextLength = 0 Then
                    _sintesiVocale.LeggiTesto("inizio testo", False, "Alta")
                Else
                    _sintesiVocale.LeggiTesto("Selezione")
                End If
            ElseIf Me.SelectionStart > 0 Then
                SalvaStatoUndo()
                Me.SelectionStart -= 1
                Me.SelectionLength = 1
                Dim c As String = Me.SelectedText
                Me.SelectedText = ""
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("asterisco")
                ElseIf c.Length > 0 Then
                    Dim daLeggere As String = TestoPerCarattereSingolo(c(0))
                    If String.IsNullOrWhiteSpace(daLeggere) Then daLeggere = c.Substring(0, 1)
                    If Char.IsLetter(c(0)) AndAlso Char.IsUpper(c(0)) Then daLeggere = c(0).ToString() & " maiuscola"
                    If Not String.IsNullOrEmpty(daLeggere) Then _sintesiVocale.LeggiTesto(daLeggere)
                End If
            ElseIf Me.TextLength = 0 Then
                _sintesiVocale.LeggiTesto("inizio testo", False, "Alta")
            Else
                _sintesiVocale.LeggiTesto("inizio testo", False, "Alta")
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciBackspace: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciDelete()
        Try
            If Me.ReadOnly Then Return
            If Me.TextLength > 0 Then
                _vuotoOrizzontaleAnnunciato = False
            End If
            If Me.SelectionLength > 0 Then
                SalvaStatoUndo()
                Me.SelectedText = ""
                If Me.TextLength = 0 Then
                    _sintesiVocale.LeggiTesto("fine testo", False, "Alta")
                Else
                    _sintesiVocale.LeggiTesto("Selezione")
                End If
            ElseIf Me.SelectionStart < Me.TextLength Then
                SalvaStatoUndo()
                Me.SelectionLength = 1
                Dim c As String = Me.SelectedText
                Me.SelectedText = ""
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("asterisco")
                ElseIf c.Length > 0 Then
                    Dim daLeggere As String = TestoPerCarattereSingolo(c(0))
                    If String.IsNullOrWhiteSpace(daLeggere) Then daLeggere = c.Substring(0, 1)
                    If Char.IsLetter(c(0)) AndAlso Char.IsUpper(c(0)) Then daLeggere = c(0).ToString() & " maiuscola"
                    If Not String.IsNullOrEmpty(daLeggere) Then _sintesiVocale.LeggiTesto(daLeggere)
                End If
            ElseIf Me.TextLength = 0 Then
                _sintesiVocale.LeggiTesto("fine testo", False, "Alta")
            Else
                _sintesiVocale.LeggiTesto("fine testo", False, "Alta")
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciDelete: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciPagina(direzione As Integer)
        Try
            AssicuraRigheVisuali()
            Dim righePerPagina As Integer = System.Math.Max(1, AreaCampoGdiTouk().Height \ _altezzaRiga)
            Dim rigaColonna = OttieniRigaEColonnaDaIndice(_selectionStart)
            Dim rigaTarget As Integer = rigaColonna.Item1 + direzione * righePerPagina
            Dim ultimaRiga As Integer = _righeVisuali.Count - 1

            If rigaTarget < 0 Then rigaTarget = 0
            If rigaTarget > ultimaRiga Then rigaTarget = ultimaRiga

            If rigaTarget = rigaColonna.Item1 Then
                _sintesiVocale.LeggiTesto(If(direzione < 0, "inizio testo", "fine testo"))
                Return
            End If

            If _colonnaPreferita < 0 Then _colonnaPreferita = rigaColonna.Item2
            _anchorPoint = -1
            Me.Select(OttieniIndiceDaRigaEColonna(rigaTarget, _colonnaPreferita), 0)
            Me.ScrollToCaret()
            LeggiRigaCorrenteCampo()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciCtrlDelete()
        Try
            If Me.ReadOnly Then Return
            If Me.SelectionLength > 0 Then
                SalvaStatoUndo()
                Me.SelectedText = ""
                _sintesiVocale.LeggiTesto("Selezione")
            ElseIf Me.SelectionStart < Me.TextLength Then
                SalvaStatoUndo()
                Dim fineParola As Integer = TrovaInizioProssimaParola(Me.SelectionStart)
                If fineParola <= Me.SelectionStart Then fineParola = Me.SelectionStart + 1
                Me.SelectionLength = fineParola - Me.SelectionStart
                Dim testoRimosso As String = Me.SelectedText
                Me.SelectedText = ""
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("Parola")
                ElseIf Not String.IsNullOrWhiteSpace(testoRimosso) Then
                    _sintesiVocale.LeggiTesto("Cancellato " & testoRimosso.Trim())
                Else
                    _sintesiVocale.LeggiTesto(". spazio", escludiPunteggiatura:=True)
                End If
            Else
                _sintesiVocale.LeggiTesto("fine testo")
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciCtrlDelete: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciCtrlBackspace()
        Try
            If Me.ReadOnly Then Return
            If Me.SelectionLength > 0 Then
                SalvaStatoUndo()
                Me.SelectedText = ""
                _sintesiVocale.LeggiTesto("Selezione")
            ElseIf Me.SelectionStart > 0 Then
                SalvaStatoUndo()
                Dim startIdx = TrovaInizioPrecedenteParola(Me.SelectionStart)
                Me.SelectionLength = Me.SelectionStart - startIdx
                Me.SelectionStart = startIdx
                Dim parola As String = Me.SelectedText
                Me.SelectedText = ""
                If _isCampoPassword AndAlso Not leggipassword Then
                    _sintesiVocale.LeggiTesto("Parola")
                Else
                    _sintesiVocale.LeggiTesto(parola)
                End If
            Else
                _sintesiVocale.LeggiTesto("inizio testo")
            End If
            Me.ScrollToCaret()
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciCtrlBackspace: " & ex.Message)
        End Try
    End Sub

    Private Function DeveInserireMaiuscola() As Boolean
        Try
            Dim posizione As Integer = _selectionStart
            Dim i As Integer = posizione - 1
            While i >= 0 AndAlso i < _testo.Length AndAlso (_testo(i) = " "c OrElse _testo(i) = ControlChars.Lf)
                i -= 1
            End While
            If i < 0 Then Return True
            Dim precedente As Char = _testo(i)
            Return precedente = "."c OrElse precedente = "!"c OrElse precedente = "?"c
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.DeveInserireMaiuscola: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaCarattereDiRipiego(c As Char)
        Try
            ' Si legge la stessa chiave usata dal campo Word, dove la lettura funziona. La
            ' sorgente propria del campo puo' valere "2", cioe' solo parole, e in quel caso la
            ' lettera non verrebbe mai annunciata pur essendo scritta: e' questa la differenza di
            ' comportamento fra i campi e Word. Resta come ripiego se la chiave non e' impostata.
            Dim impostazione As String = ""
            Try
                Dim configurazione As New WGConfig()
                impostazione = configurazione.LeggiValore("echo_digitazione")
            Catch exConfig As Exception
                _logger.ScriviLog("WGCampiMovimento.AnnunciaCarattereDiRipiego (config): " & exConfig.Message)
            End Try

            If String.IsNullOrWhiteSpace(impostazione) Then
                Try
                    impostazione = EchoDigitazioneEffettivo()
                Catch exImpostazione As Exception
                    _logger.ScriviLog("WGCampiMovimento.AnnunciaCarattereDiRipiego (impostazione): " & exImpostazione.Message)
                End Try
            End If

            If impostazione = "0" Then Return

            Dim leggiCaratteri As Boolean = (impostazione = "1" OrElse impostazione = "3" OrElse String.IsNullOrWhiteSpace(impostazione))
            Dim leggiParole As Boolean = (impostazione = "2" OrElse impostazione = "3" OrElse String.IsNullOrWhiteSpace(impostazione))
            Dim fineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))

            If fineParola AndAlso leggiParole Then
                Dim parola As String = UltimaParolaPrimaDelCursore()
                If Not String.IsNullOrWhiteSpace(parola) Then
                    _sintesiVocale.LeggiTesto(parola)
                    Return
                End If
            End If

            If Not leggiCaratteri Then Return

            Dim tradotto As String = TestoPerCarattereSingolo(c)
            If String.IsNullOrWhiteSpace(tradotto) Then tradotto = c.ToString()
            If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then tradotto = c.ToString() & " maiuscola"
            _sintesiVocale.LeggiTesto(tradotto)
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.AnnunciaCarattereDiRipiego: " & ex.Message)
        End Try
    End Sub

    Private Function UltimaParolaPrimaDelCursore() As String
        Try
            Dim testo As String = Me.Text
            Dim cursore As Integer = Me.SelectionStart
            If cursore <= 0 OrElse cursore > testo.Length Then Return ""

            Dim i As Integer = cursore - 1
            If i >= 0 AndAlso i < testo.Length AndAlso (testo(i) = " "c OrElse Char.IsPunctuation(testo(i))) Then i -= 1
            While i >= 0 AndAlso Char.IsWhiteSpace(testo(i))
                i -= 1
            End While

            Dim fine As Integer = i + 1
            While i >= 0 AndAlso Not Char.IsWhiteSpace(testo(i)) AndAlso Not Char.IsPunctuation(testo(i))
                i -= 1
            End While

            Dim inizio As Integer = i + 1
            If fine > inizio Then Return testo.Substring(inizio, fine - inizio)
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.UltimaParolaPrimaDelCursore: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub GestisciEcoDigitazione(c As Char)
        Try
            Dim echoSetting As String = EchoDigitazioneEffettivo()
            If echoSetting = "0" Then Return
            If echoSetting <> "1" AndAlso echoSetting <> "2" AndAlso echoSetting <> "3" Then echoSetting = "3"

            If _isCampoPassword AndAlso Not leggipassword Then
                _sintesiVocale.LeggiTesto("asterisco")
                Return
            End If

            Dim isSimbolo As Boolean = WGTastiera.SimboloDaLeggereSubito(c)
            Dim isFineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))
            Dim isFineParolaPerSimboli As Boolean = (isFineParola OrElse isSimbolo)
            Dim wordDaLeggere As String = ""
            Dim spazioDopoSpazio As Boolean = False

            If c = " "c Then
                Dim txtSpc As String = Me.Text
                Dim cursoreSpc As Integer = Me.SelectionStart
                Dim prevIdx As Integer = cursoreSpc - 2
                If prevIdx < 0 Then
                    spazioDopoSpazio = True
                ElseIf prevIdx < txtSpc.Length AndAlso Char.IsWhiteSpace(txtSpc(prevIdx)) Then
                    spazioDopoSpazio = True
                End If
            End If

            If ((echoSetting = "2" AndAlso isFineParola) OrElse (echoSetting = "3" AndAlso isFineParolaPerSimboli)) AndAlso Not spazioDopoSpazio Then
                Dim txt As String = Me.Text
                Dim cursore As Integer = Me.SelectionStart
                If cursore > 0 Then
                    Dim i As Integer = cursore - 1
                    If i >= 0 AndAlso i < txt.Length AndAlso txt(i) = c Then i -= 1
                    While i >= 0 AndAlso i < txt.Length AndAlso Char.IsWhiteSpace(txt(i))
                        i -= 1
                    End While
                    Dim endWord As Integer = i + 1
                    While i >= 0 AndAlso i < txt.Length AndAlso Not Char.IsWhiteSpace(txt(i)) AndAlso Not Char.IsPunctuation(txt(i))
                        i -= 1
                    End While
                    Dim startWord As Integer = i + 1
                    If endWord > startWord AndAlso startWord >= 0 AndAlso endWord <= txt.Length Then
                        wordDaLeggere = txt.Substring(startWord, endWord - startWord)
                    End If
                End If
            End If

            If c = " "c Then
                If echoSetting = "1" Then
                    _sintesiVocale.LeggiTesto(". spazio", escludiPunteggiatura:=True)
                ElseIf echoSetting = "2" Then
                    If wordDaLeggere.Length > 0 Then
                        _sintesiVocale.LeggiTesto(wordDaLeggere)
                    Else
                        _sintesiVocale.LeggiTesto(". spazio", escludiPunteggiatura:=True)
                    End If
                Else
                    If wordDaLeggere.Length > 0 Then
                        _sintesiVocale.LeggiTesto(wordDaLeggere & ". spazio", escludiPunteggiatura:=True)
                    Else
                        _sintesiVocale.LeggiTesto(". spazio", escludiPunteggiatura:=True)
                    End If
                End If
                Return
            End If

            If echoSetting = "3" Then
                Dim leggiSimboloVocale As Boolean = True
                Dim nomeSimbolo As String = TestoPerCarattereSingolo(c)
                If String.IsNullOrWhiteSpace(nomeSimbolo) Then nomeSimbolo = c.ToString()
                If c = "@"c Then nomeSimbolo = "chiocciola"
                If c = "#"c Then nomeSimbolo = "cancelletto"
                If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then nomeSimbolo = c.ToString() & " maiuscola"

                If isFineParolaPerSimboli AndAlso wordDaLeggere.Length > 1 Then
                    If isSimbolo Then
                        If SimboloDaLeggereInScrittura(c) Then
                            _sintesiVocale.LeggiTesto(wordDaLeggere & " " & nomeSimbolo)
                        Else
                            _sintesiVocale.LeggiTesto(wordDaLeggere)
                        End If
                    Else
                        _sintesiVocale.LeggiTesto(wordDaLeggere & " " & nomeSimbolo)
                    End If
                Else
                    If isSimbolo AndAlso Not SimboloDaLeggereInScrittura(c) Then Return
                    If Not String.IsNullOrWhiteSpace(nomeSimbolo) Then _sintesiVocale.LeggiTesto(nomeSimbolo)
                End If
            Else
                If c = "@"c Then
                    _sintesiVocale.LeggiTesto("chiocciola")
                ElseIf c = "#"c Then
                    _sintesiVocale.LeggiTesto("cancelletto")
                ElseIf Char.IsLetter(c) AndAlso Char.IsUpper(c) Then
                    _sintesiVocale.LeggiTesto(c.ToString() & " maiuscola")
                Else
                    If echoSetting = "1" Then
                        If isSimbolo AndAlso Not SimboloDaLeggereInScrittura(c) Then
                            Return
                        End If
                        Dim tradotto As String = TestoPerCarattereSingolo(c)
                        If String.IsNullOrWhiteSpace(tradotto) Then tradotto = c.ToString()
                        If Not String.IsNullOrEmpty(tradotto) Then _sintesiVocale.LeggiTesto(tradotto)
                    ElseIf echoSetting = "2" Then
                        If isFineParola AndAlso wordDaLeggere.Length > 0 Then
                            If Char.IsPunctuation(c) Then
                                Dim nomePunteggiatura As String = TestoPerCarattereSingolo(c)
                                If String.IsNullOrWhiteSpace(nomePunteggiatura) Then nomePunteggiatura = c.ToString()
                                If Not String.IsNullOrEmpty(nomePunteggiatura) Then
                                    _sintesiVocale.LeggiTesto(wordDaLeggere & " " & nomePunteggiatura)
                                Else
                                    _sintesiVocale.LeggiTesto(wordDaLeggere)
                                End If
                            Else
                                _sintesiVocale.LeggiTesto(wordDaLeggere)
                            End If
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.GestisciEcoDigitazione: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniTestoVisualizzato() As String
        Try
            If _useSystemPasswordChar AndAlso _testo.Length > 0 Then
                Return New String("•"c, _testo.Length)
            End If
            Return _testo
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniTestoVisualizzato: " & ex.Message)
            Return _testo
        End Try
    End Function

    Private Function OttieniFontGdi() As IntPtr
        Try
            Dim firma As String = Me.Font.Name & "|" & Me.Font.Size & "|" & Me.Font.Bold & "|" & Me.Font.Italic
            If _hFontCorrente <> IntPtr.Zero AndAlso firma = _fontFirmaCorrente Then Return _hFontCorrente
            If _hFontCorrente <> IntPtr.Zero Then
                DeleteObject(_hFontCorrente)
                _hFontCorrente = IntPtr.Zero
            End If
            Dim hdcScreen As IntPtr = GetDC(IntPtr.Zero)
            Dim dpiY As Integer = GetDeviceCaps(hdcScreen, LOGPIXELSY)
            ReleaseDC(IntPtr.Zero, hdcScreen)
            If dpiY <= 0 Then dpiY = 96
            Dim altezza As Integer = -CInt(Me.Font.Size * dpiY / 72)
            _hFontCorrente = CreateFont(altezza, 0, 0, 0, If(Me.Font.Bold, FW_BOLD, FW_NORMAL), CUInt(If(Me.Font.Italic, 1, 0)), 0, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, DEFAULT_QUALITY, DEFAULT_PITCH Or FF_DONTCARE, Me.Font.Name)
            _fontFirmaCorrente = firma
            InvalidaLayoutRighe()

            Dim hdcMisura As IntPtr = GetDC(IntPtr.Zero)
            Dim oldFontMisura As IntPtr = SelectObject(hdcMisura, _hFontCorrente)
            Dim misuraTesto As SIZE
            GetTextExtentPoint32(hdcMisura, "Ag", 2, misuraTesto)
            SelectObject(hdcMisura, oldFontMisura)
            ReleaseDC(IntPtr.Zero, hdcMisura)
            _altezzaRiga = Math.Max(16, misuraTesto.cy + 6)

            Return _hFontCorrente
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.OttieniFontGdi: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Sub PosizionaCaretSuSchermo()
        Try
            If Not Me.Focused OrElse Not Me.IsHandleCreated Then Return
            Dim hdcScreen As IntPtr = GetDC(Me.Handle)
            Try
                Dim hFont As IntPtr = OttieniFontGdi()
                Dim oldFont As IntPtr = SelectObject(hdcScreen, hFont)

                If _multiline Then
                    Dim rigaColonna = OttieniRigaEColonnaDaIndice(_selectionStart)
                    Dim righe = OttieniRighe()
                    Dim testoRigaPrimaCaret As String = righe(rigaColonna.Item1).Substring(0, Math.Min(rigaColonna.Item2, righe(rigaColonna.Item1).Length))
                    Dim misura As SIZE
                    GetTextExtentPoint32(hdcScreen, testoRigaPrimaCaret, testoRigaPrimaCaret.Length, misura)
                    Dim y As Integer = AreaCampoGdiTouk().Top + (rigaColonna.Item1 - _primaRigaVisibile) * _altezzaRiga + 3
                    SetCaretPos(5 + misura.cx, y)
                Else
                    Dim testoVisualizzato As String = OttieniTestoVisualizzato()
                    Dim testoPrimaCaret As String = testoVisualizzato.Substring(0, Math.Min(_selectionStart, testoVisualizzato.Length))
                    Dim misura As SIZE
                    GetTextExtentPoint32(hdcScreen, testoPrimaCaret, testoPrimaCaret.Length, misura)
                    SetCaretPos(5 + misura.cx - _scrollOffsetX, AreaCampoGdiTouk().Top + 3)
                End If

                SelectObject(hdcScreen, oldFont)
            Finally
                ReleaseDC(Me.Handle, hdcScreen)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.PosizionaCaretSuSchermo: " & ex.Message)
        End Try
    End Sub

    Private Function ConvertiColore(c As Color) As Integer
        Return c.R Or (c.G << 8) Or (c.B << 16)
    End Function

    Private Sub DisegnaControllo()
        Try
            If Not Me.IsHandleCreated Then Return
            Dim ps As New PaintStruct()
            Dim hdcScreen As IntPtr = BeginPaint(Me.Handle, ps)
            If hdcScreen = IntPtr.Zero Then Return

            Dim hdcMem As IntPtr = IntPtr.Zero
            Dim hbmMem As IntPtr = IntPtr.Zero
            Dim hbmOld As IntPtr = IntPtr.Zero

            Try
                Dim areaCampo As Rectangle = AreaCampoGdiTouk()
                hdcMem = CreateCompatibleDC(hdcScreen)
                hbmMem = CreateCompatibleBitmap(hdcScreen, Math.Max(1, Me.Width), Math.Max(1, Me.Height))
                hbmOld = SelectObject(hdcMem, hbmMem)

                Dim coloreSfondo As Color = If(Me.ReadOnly, Color.FromArgb(240, 240, 240), Color.White)
                Dim hBrushSfondo As IntPtr = CreateSolidBrush(ConvertiColore(coloreSfondo))
                Dim rectSfondo As New RECT With {.Left = areaCampo.Left, .Top = areaCampo.Top, .Right = areaCampo.Right, .Bottom = areaCampo.Bottom}
                FillRect(hdcMem, rectSfondo, hBrushSfondo)
                DeleteObject(hBrushSfondo)

                Dim coloreBordo As Color = If(Me.Focused, Color.FromArgb(0, 90, 200), Color.FromArgb(150, 150, 150))
                Dim hPenBordo As IntPtr = CreatePen(PS_SOLID, If(Me.Focused, 2, 1), ConvertiColore(coloreBordo))
                Dim oldPen As IntPtr = SelectObject(hdcMem, hPenBordo)
                Dim hNullBrush As IntPtr = GetStockObject(NULL_BRUSH)
                Dim oldBrush As IntPtr = SelectObject(hdcMem, hNullBrush)
                Rectangle(hdcMem, areaCampo.Left, areaCampo.Top, areaCampo.Right, areaCampo.Bottom)
                SelectObject(hdcMem, oldBrush)
                SelectObject(hdcMem, oldPen)
                DeleteObject(hPenBordo)

                Dim hFont As IntPtr = OttieniFontGdi()
                Dim oldFontMem As IntPtr = SelectObject(hdcMem, hFont)
                SetBkMode(hdcMem, TRANSPARENT)
                SetTextColor(hdcMem, ConvertiColore(Color.Black))

                Dim hBrushSelezione As IntPtr = CreateSolidBrush(ConvertiColore(Color.FromArgb(51, 153, 255)))

                If _multiline Then
                    AssicuraRigheVisuali()
                    Dim righe = OttieniRighe()
                    Dim righeVisibili As Integer = Math.Max(1, areaCampo.Height \ _altezzaRiga)
                    For i As Integer = 0 To righe.Length - 1
                        Dim rigaTesto As String = righe(i)
                        If i >= _primaRigaVisibile AndAlso i < _primaRigaVisibile + righeVisibili Then
                            Dim y As Integer = areaCampo.Top + (i - _primaRigaVisibile) * _altezzaRiga + 3
                            Dim inizioRigaAssoluto As Integer = _righeVisuali(i).Item1
                            Dim fineRigaAssoluto As Integer = inizioRigaAssoluto + rigaTesto.Length

                            If _selectionLength > 0 Then
                                Dim selInizio As Integer = Math.Max(_selectionStart, inizioRigaAssoluto)
                                Dim selFine As Integer = Math.Min(_selectionStart + _selectionLength, fineRigaAssoluto)
                                If selFine > selInizio Then
                                    Dim prima As String = rigaTesto.Substring(0, selInizio - inizioRigaAssoluto)
                                    Dim mezzo As String = rigaTesto.Substring(selInizio - inizioRigaAssoluto, selFine - selInizio)
                                    Dim misuraPrima As SIZE
                                    GetTextExtentPoint32(hdcMem, prima, prima.Length, misuraPrima)
                                    Dim misuraMezzo As SIZE
                                    GetTextExtentPoint32(hdcMem, mezzo, mezzo.Length, misuraMezzo)
                                    Dim rectSel As New RECT With {.Left = 5 + misuraPrima.cx, .Top = y, .Right = 5 + misuraPrima.cx + misuraMezzo.cx, .Bottom = y + _altezzaRiga}
                                    FillRect(hdcMem, rectSel, hBrushSelezione)
                                End If
                            End If

                            If rigaTesto.Length > 0 Then
                                TextOut(hdcMem, 5, y, rigaTesto, rigaTesto.Length)
                            End If
                        End If
                    Next
                Else
                    Dim testoVisualizzato As String = OttieniTestoVisualizzato()

                    If _selectionLength > 0 Then
                        Dim prima As String = testoVisualizzato.Substring(0, _selectionStart)
                        Dim mezzo As String = testoVisualizzato.Substring(_selectionStart, _selectionLength)
                        Dim misuraPrima As SIZE
                        GetTextExtentPoint32(hdcMem, prima, prima.Length, misuraPrima)
                        Dim misuraMezzo As SIZE
                        GetTextExtentPoint32(hdcMem, mezzo, mezzo.Length, misuraMezzo)
                        Dim rectSel As New RECT With {.Left = 5 + misuraPrima.cx - _scrollOffsetX, .Top = areaCampo.Top + 2, .Right = 5 + misuraPrima.cx + misuraMezzo.cx - _scrollOffsetX, .Bottom = areaCampo.Bottom - 2}
                        FillRect(hdcMem, rectSel, hBrushSelezione)
                    End If

                    If testoVisualizzato.Length > 0 Then
                        TextOut(hdcMem, 5 - _scrollOffsetX, areaCampo.Top + 3, testoVisualizzato, testoVisualizzato.Length)
                    End If
                End If

                DeleteObject(hBrushSelezione)
                SelectObject(hdcMem, oldFontMem)

                BitBlt(hdcScreen, areaCampo.Left, areaCampo.Top, areaCampo.Width, areaCampo.Height, hdcMem, areaCampo.Left, areaCampo.Top, SRCCOPY)

                If Me.Focused Then PosizionaCaretSuSchermo()
            Catch ex As Exception
                _logger.ScriviLog("WGCampiMovimento.DisegnaControllo (interno): " & ex.Message)
            Finally
                If hdcMem <> IntPtr.Zero Then
                    If hbmOld <> IntPtr.Zero Then SelectObject(hdcMem, hbmOld)
                    If hbmMem <> IntPtr.Zero Then DeleteObject(hbmMem)
                    DeleteDC(hdcMem)
                End If
                EndPaint(Me.Handle, ps)
            End Try
        Catch ex As Exception
            _logger.ScriviLog("WGCampiMovimento.DisegnaControllo: " & ex.Message)
        End Try
    End Sub

End Class