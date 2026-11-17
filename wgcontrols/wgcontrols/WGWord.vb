Imports System.Windows.Forms
Imports System.Drawing
Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Diagnostics
Imports System.Text.RegularExpressions
Imports SymSpell
Imports System.Threading.Tasks
Imports UglyToad.PdfPig.Fonts.Standard14Fonts
Imports UglyToad.PdfPig.Writer
Imports UglyToad.PdfPig.Content
Imports System.IO.Compression
Imports System.IO.Compression.ZipFile

Imports System.Linq
Imports UglyToad.PdfPig
Partial Public Class WGWord
    Inherits Control
    Private tastiera As WGTastiera = WGTastiera.GetInstance()
    Public Property SolaLettura As Boolean = False
    Public Event menudestroaperto(sender As Object, e As EventArgs)

    Private _menuGestioneIndice As WGMenuLista
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetFocus(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ToUnicodeEx(wVirtKey As UInteger, wScanCode As UInteger, lpKeyState As Byte(), <Out, MarshalAs(UnmanagedType.LPWStr)> pwszBuff As StringBuilder, cchBuff As Integer, wFlags As UInteger, dwhkl As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetKeyboardState(lpKeyState As Byte()) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="MapVirtualKeyW")>
    Private Shared Function MapVirtualKeyAltGr(uCode As UInteger, uMapType As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", EntryPoint:="GetKeyboardLayout")>
    Private Shared Function GetKeyboardLayoutAltGr(idThread As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="GetForegroundWindow")>
    Private Shared Function GetForegroundWindowAltGr() As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId")>
    Private Shared Function GetWindowThreadProcessIdAltGr(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

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

    Private Function CaratterePianoTasto(key As Keys) As String
        Try
            Dim virtualKey As UInteger = CUInt(key And Keys.KeyCode)
            Dim keyboardState(255) As Byte
            If Not GetKeyboardState(keyboardState) Then Return ""
            For i As Integer = 0 To 255
                keyboardState(i) = 0
            Next
            Dim scanCode As UInteger = MapVirtualKeyAltGr(virtualKey, 0)
            Dim fgWnd As IntPtr = GetForegroundWindowAltGr()
            Dim procId As UInteger
            Dim fgThread As UInteger = GetWindowThreadProcessIdAltGr(fgWnd, procId)
            Dim hkl As IntPtr = GetKeyboardLayoutAltGr(fgThread)
            Dim sb As New StringBuilder(5)
            Dim res As Integer = ToUnicodeEx(virtualKey, scanCode, keyboardState, sb, sb.Capacity, 0, hkl)
            If res > 0 Then Return sb.ToString(0, res)
            Return ""
        Catch ex As Exception
            logger.ScriviLog("CaratterePianoTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetFocus() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function CreateCaret(ByVal hWnd As IntPtr, ByVal hBitmap As IntPtr, ByVal nWidth As Integer, ByVal nHeight As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowCaret(ByVal hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function HideCaret(ByVal hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DestroyCaret() As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetCaretPos(ByVal x As Integer, ByVal y As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function InvalidateRect(ByVal hWnd As IntPtr, ByVal lpRect As IntPtr, ByVal bErase As Boolean) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetScrollInfo(ByVal hwnd As IntPtr, ByVal nBar As Integer, ByRef lpsi As SCROLLINFO, ByVal redraw As Boolean) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetScrollInfo(ByVal hwnd As IntPtr, ByVal nBar As Integer, ByRef lpsi As SCROLLINFO) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetDC(ByVal hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(ByVal hWnd As IntPtr, ByVal hDC As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetClientRect(ByVal hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function DrawText(ByVal hDC As IntPtr, ByVal lpString As String, ByVal nCount As Integer, ByRef lpRect As RECT, ByVal uFormat As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function FillRect(ByVal hDC As IntPtr, ByRef lpRect As RECT, ByVal hBrush As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function CreateFont(ByVal nHeight As Integer, ByVal nWidth As Integer, ByVal nEscapement As Integer, ByVal nOrientation As Integer, ByVal fnWeight As Integer, ByVal fdwItalic As UInteger, ByVal fdwUnderline As UInteger, ByVal fdwStrikeOut As UInteger, ByVal fdwCharSet As UInteger, ByVal fdwOutputPrecision As UInteger, ByVal fdwClipPrecision As UInteger, ByVal fdwQuality As UInteger, ByVal fdwPitchAndFamily As UInteger, ByVal lpszFace As String) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(ByVal hDC As IntPtr, ByVal hObject As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(ByVal hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(ByVal hDC As IntPtr, ByVal color As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(ByVal hDC As IntPtr, ByVal mode As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateSolidBrush(ByVal color As Integer) As IntPtr
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Public Structure SCROLLINFO
        Public cbSize As Integer
        Public fMask As Integer
        Public nMin As Integer
        Public nMax As Integer
        Public nPage As Integer
        Public nPos As Integer
        Public nTrackPos As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    Private lettura As sintesivocale = sintesivocale.GetInstance
    Private Const SB_VERT As Integer = 1

    Private Const SIF_RANGE As Integer = &H1
    Private Const SIF_PAGE As Integer = &H2
    Private Const SIF_POS As Integer = &H4
    Private Const SIF_ALL As Integer = (SIF_RANGE Or SIF_PAGE Or SIF_POS Or 16)

    Private Const WM_LBUTTONDOWN As Integer = &H201
    Private Const WM_GETDLGCODE As Integer = &H87
    Private Const WM_SETFOCUS As Integer = &H7
    Private Const WM_KILLFOCUS As Integer = &H8
    Private Const WM_VSCROLL As Integer = &H115
    Private Const WM_MOUSEWHEEL As Integer = &H20A
    Private Const WM_PAINT As Integer = &HF
    Private Const WM_SIZE As Integer = &H5
    Private _ultimaLarghezzaLayout As Integer = -1

    Private Const DLGC_WANTARROWS As Integer = &H1
    Private Const DLGC_WANTTAB As Integer = &H2
    Private Const DLGC_WANTCHARS As Integer = &H80
    Private Const DLGC_WANTALLKEYS As Integer = &H4

    Private Const TRANSPARENT_BK As Integer = 1
    Private Const DT_LEFT As Integer = &H0
    Private Const DT_VCENTER As Integer = &H4
    Private Const DT_SINGLELINE As Integer = &H20
    Private Const DT_NOPREFIX As Integer = &H800
    Private Const DT_END_ELLIPSIS As Integer = &H8000
    Private Const FW_BOLD As Integer = 700
    Private Const DEFAULT_CHARSET As UInteger = 1
    Private Const OUT_DEFAULT_PRECIS As UInteger = 0
    Private Const CLIP_DEFAULT_PRECIS As UInteger = 0
    Private Const CLEARTYPE_QUALITY As UInteger = 5
    Private Const DEFAULT_PITCH As UInteger = 0
    Private Const FF_DONTCARE As UInteger = 0

    Private Const COLORE_SFONDO_BANNER As Integer = &H996600
    Private Const COLORE_TESTO_BANNER As Integer = &HFFFFFF
    Private Const COLORE_BORDO_BANNER As Integer = &H663300
    Private Const ALTEZZA_BANNER_STATO As Integer = 32
    Private Const DURATA_MESSAGGIO_SEC As Integer = 30

    Friend _gestoreMovimento As WGWordGestoreMovimento
    Private _menuTabelleCorrente As WGMenuLista
    Private _menuRecupero As WGMenuLista
    Private _menuFont As WGMenuLista
    Private _menuDimensioni As WGMenuLista
    Private _menuIndice As WGMenuLista
    Private _menuSuggerimenti As WGMenuLista
    Private _menuInserisciEmogi As WGMenuLista
    Private _menuElencoLink As WGMenuLista
    Private _menuSegnalibri As WGMenuLista

    Private _gestoreEmogi As New WGEmogi()
    Private Shared _appuntiFormattati As New List(Of DocRun)()

    Friend _elements As List(Of DocElement)
    Friend _elementsLock As New Object()
    Private _layoutThread As Thread
    Private _layoutSignal As AutoResetEvent
    Private _stopThreads As Boolean = False

    Friend _caretIndex As Integer = 0
    Friend _tblRow As Integer = 0
    Friend _tblCol As Integer = 0
    Friend _tblPara As Integer = 0
    Friend _caretCharIndex As Integer = 0

    Private _lastElementWasTable As Boolean = False

    Friend _selectionActive As Boolean = False
    Friend _selAnchorIndex As Integer = 0
    Friend _selAnchorSubIndex As Integer = 0
    Friend _selAnchorCharIndex As Integer = 0

    Friend _undoStack As New Stack(Of UndoState)
    Friend _redoStack As New Stack(Of UndoState)
    Friend _lineHeight As Integer = 20

    Friend _vScrollPos As Integer = 0
    Private _totalContentHeight As Integer = 0
    Private _isModified As Boolean = False

    Friend _docTitle As String = "Nuovo Documento"
    Private _safeWidthTemp As Integer = 800
    Private _pendingLoadElements As List(Of DocElement)

    Public HeaderText As String = ""
    Public FooterText As String = ""

    Private logger As New Logger()
    Friend config As New WGConfig()
    Friend _renderer As WGWordRender
    Private _symSpell As SymSpell

    Private _sqlite As New WGWordSqlite()
    Private WithEvents _recoveryTimer As New System.Windows.Forms.Timer()

    Private _isFirstLoad As Boolean = True
    Private _shiftPremutoDaHook As Boolean = False
    Private _ctrlSinistroPremutoDaHook As Boolean = False
    Private _ctrlDestroPremutoDaHook As Boolean = False
    Private _altPremutoDaHook As Boolean = False
    Private _altDestroPremutoDaHook As Boolean = False
    Private _altUsatoConAltroTasto As Boolean = False
    Private _altDestroUsatoConAltroTasto As Boolean = False

    Private _messaggioStato As String = ""
    Private _messaggioStatoScadenza As DateTime = DateTime.MinValue
    Private _lockMessaggioStato As New Object()
    Private WithEvents _timerMessaggioStato As New System.Windows.Forms.Timer()
    Friend _commentiPerParagrafo As New Dictionary(Of String, List(Of String))
    Friend _stiliTitolo As New Dictionary(Of String, Integer)
    Friend _pendingStiliTitolo As Dictionary(Of String, Integer)
    Friend _pendingCommenti As Dictionary(Of String, List(Of String))
    Private _letturaInPausa As Boolean = False
    Private _ultimaFraseLetta As String = ""
    Private _modalitaConcentrazione As Boolean = False
    Private _correttoreOrigConcentrazione As String = ""
    Private _ecoOrigConcentrazione As String = ""
    Protected Overrides Sub OnGotFocus(e As EventArgs)
        Try
            MyBase.OnGotFocus(e)
            WGImpostazioniComuni.DisattivaScreenReaderPreferitoSeAvviato()
            If WGTastieraTouch.RiprendiControlloTouch() Then Return
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk IsNot Nothing Then touk.RiassegnaControllo(Me.Handle)
        Catch ex As Exception
            logger.ScriviLog("WGWord.OnGotFocus: " & ex.Message)
        End Try
    End Sub

    Private _campoNotaCorrente As WGCampoTesto
    Friend _tblSelAttiva As Boolean = False
    Friend _tblSelModalita As String = ""
    Friend _tblSelAncora As Integer = -1
    Private _tipoNotaCorrente As String = ""
    Private _campoCommentoCorrente As WGCampoTesto
    Private _campoCarattereSpecialeCorrente As WGCampoTesto
    Private _menuSimboliCorrente As WGMenuLista
    Private _menuStrutturaCorrente As WGMenuLista

    Public Class StatoCursore
        Public Property InTabella As Boolean = False
        Public Property IndiceElemento As Integer = -1
        Public Property RigaTabella As Integer = -1
        Public Property ColonnaTabella As Integer = -1
        Public Property ParagrafoCella As Integer = -1
        Public Property IndiceCarattere As Integer = -1
        Public Property TestoContenitore As String = ""
        Public Property ParolaCorrente As String = ""
        Public Property SelezioneAttiva As Boolean = False
        Public Property InizioSelezioneIndice As Integer = -1
        Public Property InizioSelezioneCarattere As Integer = -1
        Public Property FineSelezioneIndice As Integer = -1
        Public Property FineSelezioneCarattere As Integer = -1
        Public Property AnnunciaIngressoTabella As Boolean = False
    End Class

    Public Sub New()
        Try
            WGBraille.Collega(Me)
            Me.SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.Opaque Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            Me.AccessibleRole = AccessibleRole.None
            Me.BackColor = WGTema.Sfondo
            AddHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema

            Me.Cursor = Cursors.IBeam

            _gestoreMovimento = New WGWordGestoreMovimento(Me)
            AddHandler _gestoreMovimento.menudestroapertura, AddressOf GestoreMovimento_MenuDestroAperto

            _elements = New List(Of DocElement)()
            _elements.Add(New DocParagraph())
            _layoutSignal = New AutoResetEvent(False)
            _layoutThread = New Thread(AddressOf LayoutLoop)

            _layoutThread.IsBackground = True
            _layoutThread.Start()

            Dim intervalloMs As Integer = IntervalloSalvataggioMs
            If intervalloMs > 0 Then
                _recoveryTimer.Interval = intervalloMs
                _recoveryTimer.Start()
            End If

            _timerMessaggioStato.Interval = 1000

            AddHandler tastiera.ShiftPremuto, AddressOf WGTastiera_ShiftPremuto
            AddHandler tastiera.ShiftRilasciato, AddressOf WGTastiera_ShiftRilasciato
            AddHandler tastiera.ControlSinistroPremuto, AddressOf WGTastiera_ControlSinistroPremuto
            AddHandler tastiera.ControlSinistroRilasciato, AddressOf WGTastiera_ControlSinistroRilasciato
            AddHandler tastiera.ControlDestroPremuto, AddressOf WGTastiera_ControlDestroPremuto
            AddHandler tastiera.ControlDestroRilasciato, AddressOf WGTastiera_ControlDestroRilasciato
            AddHandler tastiera.AltPremuto, AddressOf WGTastiera_AltPremuto
            AddHandler tastiera.AltRilasciato, AddressOf WGTastiera_AltRilasciato
            AddHandler tastiera.AltDestroPremuto, AddressOf WGTastiera_AltDestroPremuto
            AddHandler tastiera.AltDestroRilasciato, AddressOf WGTastiera_AltDestroRilasciato

            InizializzaCorrettore()


        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()
        End Try
    End Sub

    Private Sub GestoreMovimento_MenuDestroAperto(sender As Object, e As EventArgs)
        RaiseEvent menudestroaperto(Me, EventArgs.Empty)
    End Sub

    Private Sub WGTastiera_ShiftPremuto()
        _shiftPremutoDaHook = True
    End Sub

    Private Sub WGTastiera_ShiftRilasciato()
        _shiftPremutoDaHook = False
    End Sub

    Private Sub WGTastiera_ControlSinistroPremuto()
        _ctrlSinistroPremutoDaHook = True
    End Sub

    Private Sub WGTastiera_ControlSinistroRilasciato()
        _ctrlSinistroPremutoDaHook = False
    End Sub

    Private Sub WGTastiera_ControlDestroPremuto()
        _ctrlDestroPremutoDaHook = True
    End Sub

    Private Sub WGTastiera_ControlDestroRilasciato()
        _ctrlDestroPremutoDaHook = False
    End Sub

    Private Sub WGTastiera_AltPremuto()
        Try
            _altPremutoDaHook = True
            _altUsatoConAltroTasto = False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub WGTastiera_AltRilasciato()
        Try
            Dim apriMenuDocumento As Boolean = _altPremutoDaHook AndAlso Not _altUsatoConAltroTasto AndAlso _modalitaCampo
            _altPremutoDaHook = False
            _altUsatoConAltroTasto = False
            If Not apriMenuDocumento Then Return
            If Me.IsDisposed OrElse Me.Disposing OrElse Not Me.IsHandleCreated Then Return
            If Not Me.ContainsFocus Then Return
            MostraMenuFormattazione()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub WGTastiera_AltDestroPremuto()
        Try
            _altDestroPremutoDaHook = True
            _altDestroUsatoConAltroTasto = False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub WGTastiera_AltDestroRilasciato()
        Try
            If Not _altDestroUsatoConAltroTasto Then
                _altDestroPremutoDaHook = False
                _altDestroUsatoConAltroTasto = False
                Return
            End If
            _altDestroPremutoDaHook = False
            _altDestroUsatoConAltroTasto = False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMessaggioStato(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            SyncLock _lockMessaggioStato
                _messaggioStato = testo
                _messaggioStatoScadenza = DateTime.Now.AddSeconds(DURATA_MESSAGGIO_SEC)
            End SyncLock
            Try
                If lettura IsNot Nothing Then lettura.LeggiTesto(testo)
            Catch exLet As Exception
                logger.ScriviLog(exLet.Message)
            End Try
            If Me.IsHandleCreated AndAlso Not Me.IsDisposed Then
                If Not _timerMessaggioStato.Enabled Then
                    _timerMessaggioStato.Start()
                End If
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub _timerMessaggioStato_Tick(sender As Object, e As EventArgs) Handles _timerMessaggioStato.Tick
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                _timerMessaggioStato.Stop()
                Return
            End If
            Dim scaduto As Boolean = False
            SyncLock _lockMessaggioStato
                If String.IsNullOrEmpty(_messaggioStato) Then
                    scaduto = True
                ElseIf DateTime.Now >= _messaggioStatoScadenza Then
                    _messaggioStato = ""
                    scaduto = True
                End If
            End SyncLock
            If scaduto Then
                _timerMessaggioStato.Stop()
                If Me.IsHandleCreated Then
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Friend Sub DisegnaBannerStato()
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            Dim testoCorrente As String = ""
            SyncLock _lockMessaggioStato
                If Not String.IsNullOrEmpty(_messaggioStato) AndAlso DateTime.Now < _messaggioStatoScadenza Then
                    testoCorrente = _messaggioStato
                End If
            End SyncLock
            If String.IsNullOrEmpty(testoCorrente) Then Return

            Dim hDC As IntPtr = GetDC(Me.Handle)
            If hDC = IntPtr.Zero Then Return

            Dim brushSfondo As IntPtr = IntPtr.Zero
            Dim brushBordo As IntPtr = IntPtr.Zero
            Dim hFont As IntPtr = IntPtr.Zero
            Dim oldFont As IntPtr = IntPtr.Zero

            Try
                Dim rectClient As RECT
                GetClientRect(Me.Handle, rectClient)

                Dim altezza As Integer = ALTEZZA_BANNER_STATO
                If altezza > rectClient.Bottom Then altezza = rectClient.Bottom

                Dim rectBordo As RECT
                rectBordo.Left = rectClient.Left
                rectBordo.Top = rectClient.Top
                rectBordo.Right = rectClient.Right
                rectBordo.Bottom = rectClient.Top + altezza

                brushBordo = CreateSolidBrush(COLORE_BORDO_BANNER)
                If brushBordo <> IntPtr.Zero Then
                    FillRect(hDC, rectBordo, brushBordo)
                End If

                Dim rectSfondo As RECT
                rectSfondo.Left = rectClient.Left + 1
                rectSfondo.Top = rectClient.Top + 1
                rectSfondo.Right = rectClient.Right - 1
                rectSfondo.Bottom = rectClient.Top + altezza - 1

                brushSfondo = CreateSolidBrush(COLORE_SFONDO_BANNER)
                If brushSfondo <> IntPtr.Zero Then
                    FillRect(hDC, rectSfondo, brushSfondo)
                End If

                hFont = CreateFont(-18, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH Or FF_DONTCARE, "Segoe UI")
                If hFont = IntPtr.Zero Then
                    hFont = CreateFont(-18, 0, 0, 0, FW_BOLD, 0, 0, 0, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH Or FF_DONTCARE, "Arial")
                End If
                If hFont <> IntPtr.Zero Then
                    oldFont = SelectObject(hDC, hFont)
                End If

                SetBkMode(hDC, TRANSPARENT_BK)
                SetTextColor(hDC, COLORE_TESTO_BANNER)

                Dim rectTesto As RECT
                rectTesto.Left = rectClient.Left + 12
                rectTesto.Top = rectClient.Top
                rectTesto.Right = rectClient.Right - 12
                rectTesto.Bottom = rectClient.Top + altezza

                Dim flagTesto As Integer = DT_SINGLELINE Or DT_VCENTER Or DT_LEFT Or DT_NOPREFIX Or DT_END_ELLIPSIS
                DrawText(hDC, testoCorrente, -1, rectTesto, flagTesto)
            Finally
                If oldFont <> IntPtr.Zero Then SelectObject(hDC, oldFont)
                If hFont <> IntPtr.Zero Then DeleteObject(hFont)
                If brushSfondo <> IntPtr.Zero Then DeleteObject(brushSfondo)
                If brushBordo <> IntPtr.Zero Then DeleteObject(brushBordo)
                ReleaseDC(Me.Handle, hDC)
            End Try
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub InizializzaCorrettore()
        Try
            If AbilitaCorrettoreAttivo Then
                Dim t As New Thread(AddressOf InizializzaCorrettoreThread)

                t.IsBackground = True
                t.Start()
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()

        End Try
    End Sub

    Private Sub InizializzaCorrettoreThread()
        Try
            _symSpell = New SymSpell(64000, 2)
            Dim pathDizionario As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frequency_dictionary_en_82_765.txt")
            If File.Exists(pathDizionario) Then
                _symSpell.LoadDictionary(pathDizionario, 0, 1)

            End If

        Catch ex As Exception
            logger.ScriviLog("Errore SymSpell: " & ex.Message)
            If Me.IsHandleCreated Then
                Me.Invoke(New MethodInvoker(Sub()

                                                Dim msgErrore As New WGMessaggi()
                                                msgErrore.Messaggio("Errore SymSpell: " & ex.Message)

                                                msgErrore.ShowDialog()
                                            End Sub))
            End If
        End Try
    End Sub

    Protected Overrides ReadOnly Property CreateParams As CreateParams

        Get
            Try
                Dim cp As CreateParams = MyBase.CreateParams
                cp.Style = cp.Style Or &H200000
                Return cp
            Catch ex As Exception
                logger.ScriviLog("CreateParams: " & ex.Message)
                Return MyBase.CreateParams
            End Try
        End Get
    End Property

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        Try
            MyBase.OnHandleCreated(e)
            If _renderer Is Nothing Then

                _renderer = New WGWordRender(Me)
                _renderer.Zoom = ZoomIniziale
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()
        End Try
    End Sub

    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        Try
            If _renderer IsNot Nothing Then
                _renderer.ReleaseHandle()
                _renderer = Nothing
            End If
            MyBase.OnHandleDestroyed(e)

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        Try
            MyBase.OnVisibleChanged(e)
            If Not Me.IsDisposed AndAlso Not Me.Disposing Then
                If Me.Visible Then
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.attivahook()
                    _isFirstLoad = False
                Else
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.disattivahook()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
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
            Me.Invalidate()
        Catch ex As Exception
            logger.ScriviLog("WGWord.OnCambiatoTema: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            RemoveHandler WGTema.CambiatoTema, AddressOf OnCambiatoTema
            If disposing Then
                If _gestoreMovimento IsNot Nothing Then
                    _gestoreMovimento.disattivahook()
                End If

                Try
                    RemoveHandler tastiera.ShiftPremuto, AddressOf WGTastiera_ShiftPremuto
                    RemoveHandler tastiera.ShiftRilasciato, AddressOf WGTastiera_ShiftRilasciato
                    RemoveHandler tastiera.ControlSinistroPremuto, AddressOf WGTastiera_ControlSinistroPremuto
                    RemoveHandler tastiera.ControlSinistroRilasciato, AddressOf WGTastiera_ControlSinistroRilasciato
                    RemoveHandler tastiera.ControlDestroPremuto, AddressOf WGTastiera_ControlDestroPremuto
                    RemoveHandler tastiera.ControlDestroRilasciato, AddressOf WGTastiera_ControlDestroRilasciato
                    RemoveHandler tastiera.AltPremuto, AddressOf WGTastiera_AltPremuto
                    RemoveHandler tastiera.AltRilasciato, AddressOf WGTastiera_AltRilasciato
                    RemoveHandler tastiera.AltDestroPremuto, AddressOf WGTastiera_AltDestroPremuto
                    RemoveHandler tastiera.AltDestroRilasciato, AddressOf WGTastiera_AltDestroRilasciato
                Catch exHandler As Exception
                    logger.ScriviLog("Dispose (RemoveHandler tastiera): " & exHandler.Message)
                End Try

                If _gestoreMovimento IsNot Nothing Then
                    Try
                        RemoveHandler _gestoreMovimento.menudestroapertura, AddressOf GestoreMovimento_MenuDestroAperto
                    Catch exHandler As Exception
                        logger.ScriviLog("Dispose (RemoveHandler gestoreMovimento): " & exHandler.Message)
                    End Try
                End If

                _stopThreads = True
                If _layoutSignal IsNot Nothing Then
                    _layoutSignal.Set()
                    _layoutSignal.Dispose()
                End If

                If _recoveryTimer IsNot Nothing Then
                    _recoveryTimer.Stop()
                    _recoveryTimer.Dispose()
                End If

                If _timerMessaggioStato IsNot Nothing Then
                    _timerMessaggioStato.Stop()
                    _timerMessaggioStato.Dispose()
                End If

                If _renderer IsNot Nothing Then

                    _renderer.ReleaseHandle()
                    _renderer = Nothing
                End If
            End If
            MyBase.Dispose(disposing)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If Me.IsDisposed OrElse Not Me.Visible Then
                MyBase.WndProc(m)
                Return
            End If

            Select Case m.Msg
                Case WM_GETDLGCODE
                    m.Result = New IntPtr(DLGC_WANTARROWS Or DLGC_WANTTAB Or DLGC_WANTCHARS Or DLGC_WANTALLKEYS)
                    Return
                Case WM_LBUTTONDOWN

                    GestisciMouseDown(m.LParam)
                    If GetFocus() <> Me.Handle Then
                        SetFocus(Me.Handle)
                    End If
                Case WM_SETFOCUS
                    ' Questo ramo non passa da MyBase.WndProc: GotFocus non viene mai sollevato, quindi il braille
                    ' va avvisato qui.
                    WGBraille.RegistraCampoAttivo(Me)
                    CreateCaret(Me.Handle, IntPtr.Zero, 1, _lineHeight)
                    ShowCaret(Me.Handle)
                    UpdateCaretAndNotify(False)
                Case WM_KILLFOCUS
                    WGBraille.DeregistraCampoAttivo(Me)
                    HideCaret(Me.Handle)

                    DestroyCaret()
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                Case WM_VSCROLL
                    GestisciVScroll(m.WParam)
                Case WM_MOUSEWHEEL

                    GestisciMouseWheel(m.WParam)
                Case WM_PAINT
                    HideCaret(Me.Handle)
                    MyBase.WndProc(m)
                    DisegnaBannerStato()
                    ShowCaret(Me.Handle)
                    Return
                Case WM_SIZE
                    MyBase.WndProc(m)
                    Dim larghezzaAttuale As Integer = Me.ClientSize.Width
                    If larghezzaAttuale > 0 AndAlso larghezzaAttuale <> _ultimaLarghezzaLayout Then
                        _ultimaLarghezzaLayout = larghezzaAttuale
                        InvalidaTutteLeAltezze()
                        TriggerLayout()
                        SafeInvalidate()
                    End If
                    Return
                Case Else
                    MyBase.WndProc(m)
            End Select
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private _salvataggioRecoveryInCorso As Integer = 0

    Private Sub _recoveryTimer_Tick(sender As Object, e As EventArgs) Handles _recoveryTimer.Tick
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _isModified AndAlso _elements IsNot Nothing AndAlso _elements.Count > 0 Then
                If Threading.Interlocked.CompareExchange(_salvataggioRecoveryInCorso, 1, 0) <> 0 Then Return

                Dim localElements As List(Of DocElement)
                SyncLock _elementsLock

                    localElements = New List(Of DocElement)(_elements)
                End SyncLock
                Dim titoloCorrente As String = _docTitle
                Dim sqliteCorrente = _sqlite

                Dim t As New Thread(Sub()
                                         Try
                                             Dim serializer As New System.Xml.Serialization.XmlSerializer(GetType(List(Of DocElement)))
                                             Dim sw As New IO.StringWriter()
                                             serializer.Serialize(sw, localElements)

                                             If sqliteCorrente IsNot Nothing Then
                                                 sqliteCorrente.Salva(titoloCorrente, sw.ToString())
                                             End If
                                         Catch exSalva As Exception
                                             logger.ScriviLog("_recoveryTimer_Tick (thread salvataggio): " & exSalva.Message)
                                         Finally
                                             Threading.Interlocked.Exchange(_salvataggioRecoveryInCorso, 0)
                                         End Try
                                     End Sub)
                t.IsBackground = True
                t.Start()
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Shared Function EstraiTestoGrezzo(filePath As String) As String
        Try
            Dim elements = WGWordUtils.CaricaDocumento(filePath, Function() False)
            Dim sb As New StringBuilder()
            For Each el In elements
                If TypeOf el Is DocParagraph Then

                    sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog(ex.Message)

            Return ""
        End Try
    End Function

    Public Function CiSonoTestiDaRecuperare() As Boolean
        Try
            If _sqlite IsNot Nothing Then Return _sqlite.CiSonoTesti()
            Return False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

            Return False
        End Try
    End Function

    Public Sub MostraTestiDaRecuperare()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            Dim elementi = _sqlite.OttieniTutti()
            If elementi.Count = 0 Then
                MostraMessaggioStato("Nessun testo da recuperare.")
                Return
            End If

            If _menuRecupero IsNot Nothing Then
                _menuRecupero.abbandona()

                _menuRecupero = Nothing
            End If

            _menuRecupero = New WGMenuLista()
            _menuRecupero.impostaTitolo("Seleziona testi da recuperare")

            _menuRecupero.multiselezioneConsentita = True
            _menuRecupero.aggiungiOpzione("Apri selezionati", AddressOf AzioneRecuperoApri)
            _menuRecupero.aggiungiOpzione("Elimina selezionati", AddressOf AzioneRecuperoElimina)


            For Each kvp In elementi
                _menuRecupero.aggiungiElemento(kvp.Key & " (" & kvp.Value.ToString("dd/MM/yyyy HH:mm") & ")", kvp.Key)
            Next
            _menuRecupero.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()
        End Try
    End Sub

    Public Sub attivahook()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                Return
            End If
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.attivahook()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub disattivahook()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.disattivahook()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiParolaSottoCursore()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiParolaCorrente()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Function TestoPrimaDelCursore() As String
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return ""
                If _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return ""
                If Not TypeOf _elements(_caretIndex) Is DocParagraph Then Return ""

                Dim paragrafo As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                Dim testo As String = paragrafo.GetText()
                If String.IsNullOrEmpty(testo) Then Return ""

                Dim posizione As Integer = _caretCharIndex
                If posizione < 0 Then posizione = 0
                If posizione > testo.Length Then posizione = testo.Length

                Return testo.Substring(0, posizione)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWord.TestoPrimaDelCursore: " & ex.Message)
            Return ""
        End Try
    End Function

    Public ReadOnly Property SiamoInTabella As Boolean
        Get
            Try
                If Me.IsDisposed OrElse Me.Disposing Then Return False
                Dim stato As StatoCursore = Me.OttieniStatoCursore()
                Return stato IsNot Nothing AndAlso stato.InTabella
            Catch ex As Exception
                logger.ScriviLog("SiamoInTabella Get: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Sub VaiCellaSuccessivaPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.SiamoInTabella Then Return
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.GestisciTabInTabella(False)
        Catch ex As Exception
            logger.ScriviLog("VaiCellaSuccessivaPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub VaiCellaPrecedentePubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Not Me.SiamoInTabella Then Return
            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.GestisciTabInTabella(True)
        Catch ex As Exception
            logger.ScriviLog("VaiCellaPrecedentePubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParolaAvantiPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            If Me.SiamoInTabella Then
                _gestoreMovimento.NavigateTableWordRight(False)
            Else
                _gestoreMovimento.NavigateWordRight(False)
            End If
        Catch ex As Exception
            logger.ScriviLog("SpostaParolaAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParolaIndietroPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            If Me.SiamoInTabella Then
                _gestoreMovimento.NavigateTableWordLeft(False)
            Else
                _gestoreMovimento.NavigateWordLeft(False)
            End If
        Catch ex As Exception
            logger.ScriviLog("SpostaParolaIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCarattereAvantiPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            If Me.SiamoInTabella Then
                _gestoreMovimento.NavigateTableCharRight(False)
            Else
                _gestoreMovimento.NavigateRight(False)
            End If
        Catch ex As Exception
            logger.ScriviLog("SpostaCarattereAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Inserisce un carattere e ne annuncia la lettura, come avviene digitando sulla tastiera
    ''' fisica. GestisciChar da solo si limita a inserire: l'annuncio arriva da GestisciEcoDigitazione,
    ''' che il gestore dei tasti chiama subito dopo. Chi scrive dalla tastiera a schermo deve fare
    ''' altrettanto, altrimenti la lettera entra nel testo senza che nessuno la legga.
    ''' L'eco rispetta l'impostazione echo_digitazione, quindi legge lettere, parole o entrambe
    ''' secondo la scelta dell'utente.
    ''' </summary>
    Public Sub InserisciCarattereConLettura(carattere As Char)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.SolaLettura Then Return
            If Char.IsControl(carattere) Then Return

            GestisciChar(carattere)

            If _gestoreMovimento Is Nothing Then Return
            Dim impostazioneEco As String = ""
            If config IsNot Nothing Then impostazioneEco = config.LeggiValore("echo_digitazione")
            _gestoreMovimento.GestisciEcoDigitazione(carattere, impostazioneEco)
        Catch ex As Exception
            logger.ScriviLog("InserisciCarattereConLettura: " & ex.Message)
        End Try
    End Sub

    Public Sub InserisciCarattereSenzaLettura(carattere As Char)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.SolaLettura Then Return
            If Char.IsControl(carattere) Then Return

            GestisciChar(carattere)
        Catch ex As Exception
            logger.ScriviLog("InserisciCarattereSenzaLettura: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoAvantiPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            _gestoreMovimento.NavigateParagrafoAvanti()
        Catch ex As Exception
            logger.ScriviLog("SpostaParagrafoAvantiPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaParagrafoIndietroPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            _gestoreMovimento.NavigateParagrafoIndietro()
        Catch ex As Exception
            logger.ScriviLog("SpostaParagrafoIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    Public Sub SpostaCarattereIndietroPubblica()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _gestoreMovimento Is Nothing Then Return
            If Me.SiamoInTabella Then
                _gestoreMovimento.NavigateTableCharLeft(False)
            Else
                _gestoreMovimento.NavigateLeft(False)
            End If
        Catch ex As Exception
            logger.ScriviLog("SpostaCarattereIndietroPubblica: " & ex.Message)
        End Try
    End Sub

    Public Function HaModifiche() As Boolean
        Return _isModified
    End Function

    Public Sub RimuoviRighe()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return

            Dim removed As Boolean = False
            SyncLock _elementsLock
                If _elements Is Nothing Then Return

                For i As Integer = _elements.Count - 1 To 0 Step -1
                    Dim el As DocElement = _elements(i)

                    If TypeOf el Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(el, DocParagraph)
                        Dim txt As String = p.GetText()

                        Dim hasImages As Boolean = False
                        For Each r As DocRun In p.Runs
                            If r.IsImage Then

                                hasImages = True
                                Exit For
                            End If
                        Next

                        If String.IsNullOrWhiteSpace(txt) AndAlso Not hasImages Then
                            _elements.RemoveAt(i)
                            removed = True

                        End If
                    End If
                Next

                If _elements.Count = 0 Then
                    _elements.Add(New DocParagraph())

                End If
            End SyncLock

            If removed Then
                _caretIndex = 0
                _caretCharIndex = 0
                _selectionActive = False

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)

                MostraMessaggioStato("Righe vuote rimosse")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()
        End Try

    End Sub

    Private Function TryDeleteImage() As Boolean
        Try
            If SolaLettura Then Return False
            Dim handled As Boolean = False
            Dim didascaliaEliminata As String = ""

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count AndAlso TypeOf _elements(_caretIndex) Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                    Dim charCount As Integer = 0

                    If p.Runs Is Nothing Then Return False
                    For i As Integer = 0 To p.Runs.Count - 1

                        Dim r As DocRun = p.Runs(i)
                        If r Is Nothing Then Continue For
                        If r.IsImage Then
                            If _caretCharIndex = charCount Then

                                Dim didascalia As String = ""
                                If String.IsNullOrEmpty(r.Text) Then
                                    didascalia = "Immagine"

                                Else
                                    didascalia = r.Text
                                End If

                                p.Runs.RemoveAt(i)
                                handled = True
                                _isModified = True
                                didascaliaEliminata = didascalia

                                Exit For
                            End If
                        Else

                            charCount += If(r.Text, "").Length
                        End If
                    Next
                End If
            End SyncLock

            If handled Then
                MostraMessaggioStato(didascaliaEliminata & " eliminata")
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)
            End If

            Return handled

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Friend Function GestisciKeyDown(key As Keys) As Boolean
        Dim url As String = ""
        Try
            If Me.IsDisposed OrElse Me.Disposing Then
                Return False
            End If

            If _altPremutoDaHook Then _altUsatoConAltroTasto = True
            If _altDestroPremutoDaHook Then _altDestroUsatoConAltroTasto = True

            If _modalitaCampo AndAlso (key And Keys.KeyCode) <> Keys.Enter Then _invioConsecutiviCampo = 0
            If (key And Keys.KeyCode) <> Keys.Enter AndAlso _gestoreMovimento IsNot Nothing Then _gestoreMovimento.ResetInvioConsecutiviTabella()

            Dim shiftPremuto As Boolean = _shiftPremutoDaHook
            Dim ctrlPremuto As Boolean = _ctrlSinistroPremutoDaHook OrElse _ctrlDestroPremutoDaHook
            Dim moved As Boolean = False

            Dim handled As Boolean = False

            Dim altDestroReale As Boolean = _altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto
            If altDestroReale Then
                Dim risultatoAltGr As String = SimboloAltGr(key And Keys.KeyCode, shiftPremuto)
                Dim caratterePiano As String = ""
                If risultatoAltGr = "" Then
                    caratterePiano = CaratterePianoTasto(key)
                    risultatoAltGr = SimboloAltGrDaCarattere(caratterePiano, shiftPremuto)
                End If
                If logger IsNot Nothing AndAlso wgcontrols.WGTastiera.LoggingVerboso Then
                End If
                If risultatoAltGr <> "" Then
                    If SolaLettura Then Return True
                    GestisciChar(risultatoAltGr(0))
                    Return True
                End If
            End If

            If key <> Keys.Up AndAlso key <> Keys.Down AndAlso _gestoreMovimento IsNot Nothing Then
                _gestoreMovimento.ResetColonnaDesiderata()
            End If

            If _tblSelAttiva Then
                Dim eFrecciaConCtrlShift As Boolean = ctrlPremuto AndAlso shiftPremuto AndAlso (key = Keys.Up OrElse key = Keys.Down OrElse key = Keys.Left OrElse key = Keys.Right)
                If Not eFrecciaConCtrlShift Then
                    _tblSelAttiva = False
                    _tblSelModalita = ""
                    _tblSelAncora = -1
                End If
            End If


            Dim eTastoNavigazione As Boolean = (key = Keys.Left OrElse key = Keys.Right OrElse key = Keys.Up OrElse key = Keys.Down OrElse
                                                key = Keys.Home OrElse key = Keys.End OrElse key = Keys.PageUp OrElse key = Keys.PageDown OrElse
                                                key = Keys.Tab)

            If shiftPremuto AndAlso Not _selectionActive Then
                _selectionActive = True
                _selAnchorIndex = _caretIndex
                _selAnchorSubIndex = GetPackedTableIndex()
                _selAnchorCharIndex = _caretCharIndex
            ElseIf Not shiftPremuto AndAlso _selectionActive AndAlso eTastoNavigazione Then
                _selectionActive = False
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
            End If

            Dim currentEl As DocElement = Nothing
            SyncLock _elementsLock

                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    currentEl = _elements(_caretIndex)
                End If
            End SyncLock

            Dim isTable As Boolean = (currentEl IsNot Nothing AndAlso TypeOf currentEl Is DocTable)


            Select Case key
                Case Keys.Add, Keys.Oemplus
                    If ctrlPremuto Then
                        AumentaZoom()
                        handled = True
                    ElseIf key = Keys.Oemplus AndAlso (_altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto) Then
                        If SolaLettura Then Return True
                        GestisciChar(If(shiftPremuto, "}"c, "]"c))
                        handled = True
                    End If
                Case Keys.Subtract, Keys.OemMinus
                    If ctrlPremuto Then
                        RiduciZoom()

                        handled = True
                    End If
                Case Keys.Down
                    If isTable AndAlso ctrlPremuto AndAlso shiftPremuto Then
                        If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.EstendiSelezioneRigaTabella(1)
                        moved = True
                        handled = True
                    ElseIf _altPremutoDaHook Then
                        If Not SolaLettura Then RiduciInterlinea()

                        handled = True
                    Else
                        If _gestoreMovimento IsNot Nothing Then

                            If isTable Then
                                _gestoreMovimento.NavigateTableVertical(1)
                            Else
                                _gestoreMovimento.NavigateDown()

                            End If
                        End If
                        moved = True
                        handled = True
                    End If
                Case Keys.Up
                    If isTable AndAlso ctrlPremuto AndAlso shiftPremuto Then
                        If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.EstendiSelezioneRigaTabella(-1)
                        moved = True
                        handled = True
                    ElseIf _altPremutoDaHook Then
                        If Not SolaLettura Then AumentaInterlinea()

                        handled = True
                    Else
                        If _gestoreMovimento IsNot Nothing Then
                            If isTable Then

                                _gestoreMovimento.NavigateTableVertical(-1)
                            Else
                                _gestoreMovimento.NavigateUp()

                            End If
                        End If
                        moved = True
                        handled = True

                    End If
                Case Keys.Left
                    If _altPremutoDaHook Then
                        VaiAlLinkPrecedente()

                        handled = True
                    Else
                        If _gestoreMovimento IsNot Nothing Then
                            If isTable Then

                                If ctrlPremuto Then
                                    _gestoreMovimento.NavigateTableWordLeft(shiftPremuto)
                                Else

                                    _gestoreMovimento.NavigateTableCharLeft(shiftPremuto)
                                End If
                            Else

                                If ctrlPremuto Then
                                    _gestoreMovimento.NavigateWordLeft(shiftPremuto)
                                Else

                                    _gestoreMovimento.NavigateLeft(shiftPremuto)
                                End If
                            End If

                        End If
                        moved = True
                        handled = True
                    End If
                Case Keys.Right
                    If _altPremutoDaHook Then
                        VaiAlLinkSuccessivo()
                        handled = True
                    Else

                        If _gestoreMovimento IsNot Nothing Then
                            If isTable Then
                                If ctrlPremuto Then

                                    _gestoreMovimento.NavigateTableWordRight(shiftPremuto)
                                Else
                                    _gestoreMovimento.NavigateTableCharRight(shiftPremuto)

                                End If
                            Else
                                If ctrlPremuto Then

                                    _gestoreMovimento.NavigateWordRight(shiftPremuto)
                                Else
                                    _gestoreMovimento.NavigateRight(shiftPremuto)

                                End If
                            End If
                        End If

                        moved = True
                        handled = True
                    End If
                Case Keys.Oem3
                    If _altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto Then
                        If SolaLettura Then Return True
                        GestisciChar("@"c)
                        handled = True
                    End If
                Case Keys.Oem7
                    If _altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto Then
                        If SolaLettura Then Return True
                        GestisciChar("#"c)
                        handled = True
                    End If
                Case Keys.Oem5
                    If _altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto Then
                        If SolaLettura Then Return True
                        GestisciChar(If(shiftPremuto, "{"c, "["c))
                        handled = True
                    End If
                Case Keys.E
                    If _altDestroPremutoDaHook OrElse tastiera.AltDestroAttualmentePremuto Then
                        If SolaLettura Then Return True
                        GestisciChar("€"c)
                        handled = True
                    End If
                Case Keys.Delete
                    If SolaLettura Then Return True

                    SaveUndoState()
                    If ctrlPremuto AndAlso Not _selectionActive Then
                        HandleCtrlDeleteKey()
                        moved = True
                        handled = True
                        Return True
                    End If

                    Dim imageHandled As Boolean = False
                    If Not SelezioneAttivaNonVuota() Then
                        imageHandled = TryDeleteImage()

                    End If

                    If Not imageHandled Then
                        If SelezioneAttivaNonVuota() Then
                            DeleteSelection()

                        Else
                            HandleDeleteKey()
                        End If
                    End If

                    moved = True
                    handled = True
                Case Keys.Back
                    If SolaLettura Then Return True
                    If ctrlPremuto Then

                        SaveUndoState()
                        HandleCtrlBackspace()
                    Else
                        SaveUndoState()

                        Dim imageHandled As Boolean = False
                        If Not SelezioneAttivaNonVuota() Then
                            imageHandled = TryDeleteImage()
                        End If


                        If Not imageHandled Then
                            If SelezioneAttivaNonVuota() Then
                                DeleteSelection()

                            Else
                                HandleBackspaceKey()
                            End If
                        End If

                    End If
                    moved = True
                    handled = True
                Case Keys.Enter
                    If ctrlPremuto Then

                        If SolaLettura Then Return True
                        InserisciInterruzionePagina()
                        moved = True

                        handled = True
                    Else
                        Dim isLink As Boolean = False

                        Dim paraLink As DocParagraph = GetCurrentParagraph()

                        SyncLock _elementsLock

                            If paraLink IsNot Nothing AndAlso paraLink.Runs IsNot Nothing Then
                                Dim charCount As Integer = 0

                                For Each r As DocRun In paraLink.Runs
                                    If r IsNot Nothing AndAlso Not r.IsImage Then

                                        Dim runLen As Integer = If(r.Text Is Nothing, 0, r.Text.Length)
                                        If _caretCharIndex >= charCount AndAlso _caretCharIndex < charCount + runLen Then

                                            If Not String.IsNullOrEmpty(r.LinkUrl) Then
                                                isLink = True

                                                url = r.LinkUrl
                                            End If

                                            Exit For
                                        End If
                                        charCount += runLen

                                    End If
                                Next
                            End If

                        End SyncLock

                        If isLink AndAlso Not _selectionActive Then
                            Dim urlPulito As String = If(url.StartsWith("#"), url.Substring(1), url)
                            Dim eBookmarkInterno As Boolean = False
                            SyncLock _elementsLock
                                If _elements IsNot Nothing Then
                                    For Each elBk In _elements
                                        If TypeOf elBk Is DocParagraph AndAlso DirectCast(elBk, DocParagraph).BookmarkId = urlPulito Then
                                            eBookmarkInterno = True
                                            Exit For
                                        End If
                                    Next
                                End If
                            End SyncLock

                            If eBookmarkInterno Then
                                If urlPulito.StartsWith("NOTA_PIE_") OrElse urlPulito.StartsWith("NOTA_FINE_") Then
                                    Dim eGiaNellaNota As Boolean = paraLink IsNot Nothing AndAlso paraLink.BookmarkId = urlPulito
                                    If eGiaNellaNota Then
                                        If Not TornaAlTestoDellaNota(urlPulito) Then
                                            MostraMessaggioStato("Riferimento della nota non trovato nel testo")
                                        End If
                                    Else
                                        Dim impostazioniNota As New WGImpostazioniComuni()
                                        If impostazioniNota.ModificaNoteNelDocumento Then
                                            VaiAllaNotaNelDocumento(urlPulito)
                                        Else
                                            LeggiModificaNote(urlPulito)
                                        End If
                                    End If
                                Else
                                    VaiABookmarkConFruscio(urlPulito)
                                End If
                            Else
                                Try

                                    WGImpostazioniComuni.ApriLinkConBrowserPreferito(url)
                                    MostraMessaggioStato("Apertura link nel browser")
                                Catch exProcess As Exception
                                    logger.ScriviLog(exProcess.Message)

                                    Dim messaggi As New WGMessaggi()
                                    messaggi.Messaggio(exProcess.Message)

                                    messaggi.ShowDialog()
                                End Try
                            End If
                            handled = True

                            moved = True
                        Else
                            If SolaLettura Then Return True

                            If _modalitaCampo AndAlso Not isTable Then
                                If RegistraInvioConsecutivoCampo() Then
                                    NotificaConfermaCampo()
                                    Return True
                                End If
                            End If

                            SaveUndoState()
                            If _selectionActive Then
                                DeleteSelection()

                            End If
                            If isTable Then
                                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.GestisciInvioTabella()
                            Else
                                Dim paraNotaCorrente As DocParagraph = GetCurrentParagraph()
                                Dim eNotaCorrente As Boolean = paraNotaCorrente IsNot Nothing AndAlso Not String.IsNullOrEmpty(paraNotaCorrente.BookmarkId) AndAlso (paraNotaCorrente.BookmarkId.StartsWith("NOTA_PIE_") OrElse paraNotaCorrente.BookmarkId.StartsWith("NOTA_FINE_"))
                                If eNotaCorrente Then
                                    InsertChar(ControlChars.Lf)
                                Else
                                    BreakLine()
                                End If
                                UpdateCaretAndNotify(True)
                            End If
                            moved = True

                            handled = True
                        End If
                    End If
                Case Keys.Z

                    If ctrlPremuto Then
                        If SolaLettura Then Return True
                        Undo()
                        handled = True

                    End If
                Case Keys.Y
                    If ctrlPremuto Then
                        If SolaLettura Then Return True
                        Redo()

                        handled = True
                    End If
                Case Keys.C
                    If ctrlPremuto Then

                        CopiaFormattato()
                        handled = True
                    End If
                Case Keys.X
                    If ctrlPremuto Then

                        If SolaLettura Then Return True
                        SaveUndoState()
                        TagliaFormattato()
                        handled = True

                    End If
                Case Keys.V
                    If ctrlPremuto Then
                        If SolaLettura Then Return True

                        SaveUndoState()
                        IncollaFormattato()
                        handled = True
                    End If
                Case Keys.A

                    If ctrlPremuto Then
                        SelectAll()
                        handled = True
                    End If

                Case Keys.G
                    If ctrlPremuto Then
                        If SolaLettura Then Return True
                        ApplicaGrassetto()

                        MostraMessaggioStato("Grassetto applicato")
                        handled = True
                    End If
                Case Keys.S
                    If ctrlPremuto Then
                        If SolaLettura Then Return True
                        ApplicaSottolineato()

                        MostraMessaggioStato("Sottolineato applicato")
                        handled = True

                    End If
                Case Keys.I
                    If ctrlPremuto Then
                        LeggiInfoFormattazione()

                        handled = True
                    End If
                Case Keys.J
                    If ctrlPremuto Then
                        If SolaLettura Then Return True

                        ImpostaAllineamento("Justify")
                        handled = True
                    End If
                Case Keys.T
                    If ctrlPremuto Then
                        MostraMenuTrovaSostituisci()
                        handled = True
                    End If
                Case Keys.H
                    If ctrlPremuto Then
                        MostraMenuIntestazionePiePaginaNote()
                        handled = True
                    End If
                Case Keys.F1
                    If shiftPremuto Then
                        LeggiNumeroNotaAllaPosizioneCorrente()
                        handled = True
                    End If
                Case Keys.F5

                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LetturaContinua()
                    handled = True
                Case Keys.F6
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiRigaCorrente()
                    handled = True

                Case Keys.F7
                    If Not SolaLettura Then MostraSuggerimentiOrtografici()
                    handled = True
                Case Keys.Tab
                    If isTable Then
                        If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.GestisciTabInTabella(shiftPremuto)
                        moved = True
                        handled = True
                    End If

                Case Keys.PageDown
                    Try
                        If TrovaIndiciInterruzioniPagina().Count > 0 Then
                            VaiAPaginaSuccessiva()
                        Else
                            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.NavigatePage(1)
                        End If
                    Catch exPgDn As Exception
                        logger.ScriviLog(exPgDn.Message)
                    End Try
                    moved = True
                    handled = True

                Case Keys.PageUp
                    Try
                        If TrovaIndiciInterruzioniPagina().Count > 0 Then
                            VaiAPaginaPrecedente()
                        Else
                            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.NavigatePage(-1)
                        End If
                    Catch exPgUp As Exception
                        logger.ScriviLog(exPgUp.Message)
                    End Try
                    moved = True
                    handled = True
                Case Keys.End
                    Try
                        If ctrlPremuto Then
                            SyncLock _elementsLock
                                If _elements IsNot Nothing AndAlso _elements.Count > 0 Then
                                    _caretIndex = _elements.Count - 1
                                    currentEl = _elements(_caretIndex)
                                    If TypeOf currentEl Is DocParagraph Then
                                        _caretCharIndex = ContaCaratteriParagrafo(DirectCast(currentEl, DocParagraph))
                                        _tblRow = -1
                                        _tblCol = -1
                                        _tblPara = -1
                                    ElseIf TypeOf currentEl Is DocTable Then
                                        Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                                        If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                                            _tblRow = tbl.Rows.Count - 1
                                            _tblCol = If(tbl.Rows(_tblRow).Cells.Count > 0, tbl.Rows(_tblRow).Cells.Count - 1, 0)
                                            Dim cell As DocTableCell = tbl.Rows(_tblRow).Cells(_tblCol)
                                            _tblPara = If(cell.Paragraphs.Count > 0, cell.Paragraphs.Count - 1, 0)
                                            If cell.Paragraphs.Count > 0 Then
                                                _caretCharIndex = ContaCaratteriParagrafo(cell.Paragraphs(_tblPara))
                                            Else
                                                _caretCharIndex = 0
                                            End If
                                        End If
                                    End If
                                End If
                            End SyncLock
                            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.ImpostaConfineVerticaleConfermato(1)
                        Else
                            If TypeOf currentEl Is DocParagraph Then
                                _caretCharIndex = ContaCaratteriParagrafo(DirectCast(currentEl, DocParagraph))
                            ElseIf TypeOf currentEl Is DocTable Then
                                Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                                    If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then _tblRow = 0
                                    Dim riga = tbl.Rows(_tblRow)
                                    If riga.Cells.Count > 0 Then
                                        If _tblCol < 0 OrElse _tblCol >= riga.Cells.Count Then _tblCol = 0
                                        Dim cell As DocTableCell = riga.Cells(_tblCol)
                                        If cell.Paragraphs.Count > 0 Then
                                            If _tblPara < 0 OrElse _tblPara >= cell.Paragraphs.Count Then _tblPara = 0
                                            _caretCharIndex = ContaCaratteriParagrafo(cell.Paragraphs(_tblPara))
                                        End If
                                    End If
                                End If
                            End If
                        End If
                        moved = True
                        handled = True
                        If lettura IsNot Nothing Then
                            lettura.LeggiTesto(If(ctrlPremuto, "Fine documento", "Fine riga"))
                        End If
                    Catch exEnd As Exception
                        logger.ScriviLog(exEnd.Message)
                    End Try
                Case Keys.Home
                    Try
                        If ctrlPremuto Then
                            SyncLock _elementsLock
                                If _elements IsNot Nothing AndAlso _elements.Count > 0 Then
                                    _caretIndex = 0
                                    _caretCharIndex = 0
                                    currentEl = _elements(0)
                                    If TypeOf currentEl Is DocTable Then
                                        _tblRow = 0
                                        _tblCol = 0
                                        _tblPara = 0
                                    Else
                                        _tblRow = -1
                                        _tblCol = -1
                                        _tblPara = -1
                                    End If
                                End If
                            End SyncLock
                            If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.ImpostaConfineVerticaleConfermato(-1)
                        Else
                            If TypeOf currentEl Is DocParagraph Then
                                _caretCharIndex = 0
                            ElseIf TypeOf currentEl Is DocTable Then
                                Dim tbl As DocTable = DirectCast(currentEl, DocTable)
                                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                                    If _tblRow < 0 OrElse _tblRow >= tbl.Rows.Count Then _tblRow = 0
                                    Dim riga = tbl.Rows(_tblRow)
                                    If riga.Cells.Count > 0 Then
                                        If _tblCol < 0 OrElse _tblCol >= riga.Cells.Count Then _tblCol = 0
                                        If _tblPara < 0 OrElse _tblPara >= riga.Cells(_tblCol).Paragraphs.Count Then _tblPara = 0
                                    End If
                                End If
                                _caretCharIndex = 0
                            End If
                        End If
                        moved = True
                        handled = True
                        If lettura IsNot Nothing Then
                            If ctrlPremuto Then
                                lettura.LeggiTesto("Inizio documento")
                            Else
                                Dim paraHome As DocParagraph = GetCurrentParagraph()
                                Dim testoHome As String = If(paraHome IsNot Nothing, paraHome.GetText(), "")
                                If String.IsNullOrEmpty(testoHome) Then
                                    lettura.LeggiTesto("Vuoto")
                                Else
                                    lettura.LeggiTesto("Inizio riga, " & WGSegni.Converti(testoHome(0).ToString()))
                                End If
                            End If
                        End If
                    Catch exHome As Exception
                        logger.ScriviLog(exHome.Message)
                    End Try
            End Select

            If moved Then
                ScrollToCaret()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)
            End If


            Return handled
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return False
        End Try
    End Function
End Class