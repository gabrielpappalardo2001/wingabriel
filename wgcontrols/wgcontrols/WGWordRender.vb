Imports System.Runtime.InteropServices
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Windows.Forms
Imports System.Linq

' Disegno grafico a schermo del documento, dei caratteri e delle pagine con supporto per temi e zoom.
Public Class WGWordRender
    Inherits NativeWindow
    Implements IDisposable

    <DllImport("gdi32.dll")> Private Shared Function SelectObject(ByVal hdc As IntPtr, ByVal hgdiobj As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteObject(ByVal hObject As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function Rectangle(ByVal hdc As IntPtr, ByVal nLeftRect As Integer, ByVal nTopRect As Integer, ByVal nRightRect As Integer, ByVal nBottomRect As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="CreateFontW")> Private Shared Function CreateFont(ByVal nHeight As Integer, ByVal nWidth As Integer, ByVal nEscapement As Integer, ByVal nOrientation As Integer, ByVal fnWeight As Integer, ByVal fdwItalic As UInteger, ByVal fdwUnderline As UInteger, ByVal fdwStrikeOut As UInteger, ByVal fdwCharSet As UInteger, ByVal fdwOutputPrecision As UInteger, ByVal fdwClipPrecision As UInteger, ByVal fdwQuality As UInteger, ByVal fdwPitchAndFamily As UInteger, ByVal lpszFace As String) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetTextColor(ByVal hdc As IntPtr, ByVal crColor As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetBkMode(ByVal hdc As IntPtr, ByVal iBkMode As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function SetBkColor(ByVal hdc As IntPtr, ByVal crColor As Integer) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="TextOutW")> Private Shared Function TextOut(ByVal hdc As IntPtr, ByVal nXStart As Integer, ByVal nYStart As Integer, ByVal lpString As String, ByVal cbString As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetTextExtentPoint32W")> Private Shared Function GetTextExtentPoint32(ByVal hdc As IntPtr, ByVal lpString As String, ByVal cbString As Integer, ByRef lpSize As SIZE) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreatePen(ByVal fnPenStyle As Integer, ByVal nWidth As Integer, ByVal crColor As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateSolidBrush(ByVal crColor As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetStockObject(ByVal fnObject As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleDC(ByVal hdc As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function CreateCompatibleBitmap(ByVal hdc As IntPtr, ByVal nWidth As Integer, ByVal nHeight As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Private Shared Function BitBlt(ByVal hdcDest As IntPtr, ByVal nXDest As Integer, ByVal nYDest As Integer, ByVal nWidth As Integer, ByVal nHeight As Integer, ByVal hdcSrc As IntPtr, ByVal nXSrc As Integer, ByVal nYSrc As Integer, ByVal dwRop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function DeleteDC(ByVal hdc As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function MoveToEx(ByVal hdc As IntPtr, ByVal X As Integer, ByVal Y As Integer, ByVal lpPoint As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Private Shared Function LineTo(ByVal hdc As IntPtr, ByVal nXEnd As Integer, ByVal nYEnd As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function FillRect(ByVal hDC As IntPtr, ByRef lprc As RECT, ByVal hbr As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function SetCaretPos(ByVal x As Integer, ByVal y As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function BeginPaint(hwnd As IntPtr, ByRef lpPaint As PaintStruct) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function EndPaint(hwnd As IntPtr, ByRef lpPaint As PaintStruct) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetKeyState(ByVal nVirtKey As Integer) As Short
    End Function
    <DllImport("user32.dll")> Private Shared Function GetDC(ByVal hwnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function ReleaseDC(ByVal hwnd As IntPtr, ByVal hdc As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function GetDeviceCaps(ByVal hdc As IntPtr, ByVal nIndex As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Private Shared Function Ellipse(ByVal hdc As IntPtr, ByVal nLeftRect As Integer, ByVal nTopRect As Integer, ByVal nRightRect As Integer, ByVal nBottomRect As Integer) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Public Structure SIZE
        Public cx As Integer
        Public cy As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure PaintStruct
        Public hdc As IntPtr
        Public fErase As Boolean
        Public rcPaint As RECT
        Public fRestore As Boolean
        Public fIncUpdate As Boolean
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=32)> Public rgbReserved As Byte()
    End Structure

    Private Const SRCCOPY As Integer = &HCC0020
    Private Const TRANSPARENT As Integer = 1
    Private Const OPAQUE As Integer = 2
    Private Const PS_SOLID As Integer = 0
    Private Const PS_DASH As Integer = 1
    Private Const FW_NORMAL As Integer = 400
    Private Const FW_BOLD As Integer = 700
    Private Const ANSI_CHARSET As UInteger = 0
    Private Const NULL_BRUSH As Integer = 5
    Private Const OUT_DEFAULT_PRECIS As UInteger = 0
    Private Const CLIP_DEFAULT_PRECIS As UInteger = 0
    Private Const CLEARTYPE_QUALITY As UInteger = 5
    Private Const DEFAULT_PITCH As UInteger = 0
    Private Const WM_PAINT As Integer = &HF
    Private Const PAGE_MARGIN_X As Integer = 30
    Private Const PAGE_MARGIN_TOP As Integer = 20
    Private Const VK_CAPITAL As Integer = &H14
    Private Const LOGPIXELSY As Integer = 90

    Private _zoom As Single = 1.0F
    Public Property Zoom As Single
        Get
            Return _zoom
        End Get
        Set(value As Single)
            Try
                If value <> _zoom Then
                    _zoom = value
                    SvuotaCacheFont()
                    WGWordLayoutMotore.SvuotaCache()
                End If
            Catch ex As Exception
                logger.ScriviLog("Zoom (Set): " & ex.Message)
            End Try
        End Set
    End Property
    Public FullScreenLayout As Boolean = True

    Private _fontCache As Dictionary(Of String, IntPtr)
    Private _fontCacheLock As New Object()
    Private _owner As WGWord
    Private logger As New Logger()

    Public Sub New(owner As WGWord)
        _owner = owner
        If _owner IsNot Nothing Then
            _owner.AccessibleRole = AccessibleRole.None
            _owner.Text = ""
            If _owner.IsHandleCreated Then
                Me.AssignHandle(_owner.Handle)
            End If
        End If
        _fontCache = New Dictionary(Of String, IntPtr)()
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            If m.Msg = WM_PAINT Then
                If _owner IsNot Nothing AndAlso _owner.IsHandleCreated AndAlso Not _owner.IsDisposed Then
                    Dim totalH As Integer = PaintDoubleBuffered(_owner.Handle, _owner.ClientRectangle, _owner)
                    _owner.UpdateScrollBars(totalH)
                    Try
                        _owner.DisegnaBannerStato()
                    Catch exBanner As Exception
                        logger.ScriviLog("WndProc DisegnaBannerStato: " & exBanner.Message)
                    End Try
                End If
                Return
            End If
            MyBase.WndProc(m)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function ConvertColorToWin32(c As Color) As Integer
        Return (c.B << 16) Or (c.G << 8) Or c.R
    End Function

    ''' <summary>Converte un colore hex del tema (senza #) in Color, con fallback in caso di valore mancante o non valido.</summary>
    Private Function ColoreDaTema(hex As String, fallback As Color) As Color
        Try
            If String.IsNullOrWhiteSpace(hex) Then Return fallback
            Return ColorTranslator.FromHtml("#" & hex.TrimStart("#"c))
        Catch
            Return fallback
        End Try
    End Function

    Private Function ParseHexColor(hex As String, fallback As Color) As Integer
        Try
            If String.IsNullOrEmpty(hex) Then Return ConvertColorToWin32(fallback)
            
            Select Case hex.Trim().ToLower()
                Case "nero"
                    Return ConvertColorToWin32(Color.Black)
                Case "bianco"
                    Return ConvertColorToWin32(Color.White)
                Case "blu"
                    Return ConvertColorToWin32(Color.Blue)
                Case "rosso"
                    Return ConvertColorToWin32(Color.Red)
                Case "verde"
                    Return ConvertColorToWin32(Color.Green)
                Case "giallo"
                    Return ConvertColorToWin32(Color.Yellow)
                Case "arancione"
                    Return ConvertColorToWin32(Color.Orange)
                Case "viola"
                    Return ConvertColorToWin32(Color.Purple)
                Case "grigio"
                    Return ConvertColorToWin32(Color.Gray)
                Case "marrone"
                    Return ConvertColorToWin32(Color.Brown)
                Case "celeste"
                    Return ConvertColorToWin32(Color.LightBlue)
                Case "rosa"
                    Return ConvertColorToWin32(Color.Pink)
                Case "nessuno"
                    Return ConvertColorToWin32(fallback)
            End Select

            Dim pulito As String = hex.Trim()
            If pulito.StartsWith("#") Then pulito = pulito.Substring(1)
            If System.Text.RegularExpressions.Regex.IsMatch(pulito, "^[0-9a-fA-F]{3}$") Then
                pulito = New String(pulito(0), 2) & New String(pulito(1), 2) & New String(pulito(2), 2)
            End If
            If System.Text.RegularExpressions.Regex.IsMatch(pulito, "^[0-9a-fA-F]{6}$") Then
                Dim daHex As Color = ColorTranslator.FromHtml("#" & pulito)
                Return ConvertColorToWin32(daHex)
            End If

            Dim c As Color = ColorTranslator.FromHtml(hex)
            Return ConvertColorToWin32(c)
        Catch
            Return ConvertColorToWin32(fallback)
        End Try
    End Function

    Public Function LarghezzaContenuto(clientWidth As Integer) As Integer
        Try
            Dim pageWidth As Integer
            If FullScreenLayout Then
                pageWidth = Math.Max(clientWidth - (PAGE_MARGIN_X * 2), 100)
            Else
                pageWidth = Math.Min(clientWidth - (PAGE_MARGIN_X * 2), CInt(1200 * Zoom))
            End If
            Return Math.Max(1, pageWidth - CInt(40 * Zoom))
        Catch ex As Exception
            logger.ScriviLog("LarghezzaContenuto: " & ex.Message)
            Return Math.Max(100, clientWidth - 80)
        End Try
    End Function

    Public Function MeasureString(text As String, fontName As String, fontSize As Single, bold As Boolean, italic As Boolean, underline As Boolean, strikethrough As Boolean) As SIZE
        Dim s As SIZE
        Try
            If String.IsNullOrEmpty(text) Then
                s.cx = 0
                s.cy = CInt((fontSize * Zoom) * 1.5)
                Return s
            End If
            
            Dim hdc As IntPtr = CreateCompatibleDC(IntPtr.Zero)
            If hdc <> IntPtr.Zero Then
                Try
                    Dim hFont As IntPtr = GetCachedFont(fontName, fontSize, bold, italic, underline, strikethrough)
                    If hFont <> IntPtr.Zero Then
                        Dim oldFont As IntPtr = SelectObject(hdc, hFont)
                        GetTextExtentPoint32(hdc, text, text.Length, s)
                        SelectObject(hdc, oldFont)
                    End If
                Finally
                    DeleteDC(hdc)
                End Try
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            s.cx = 0
            s.cy = CInt((fontSize * Zoom) * 1.5)
        End Try
        Return s
    End Function

    Public Function MeasureParagraphHeight(el As DocElement, w As Integer) As Integer
        If el Is Nothing Then Return CInt(20 * Zoom)
        Try
            Dim para As DocParagraph = TryCast(el, DocParagraph)
            If para Is Nothing Then Return CInt(20 * Zoom)
            If para.IsPageBreak Then Return CInt(60 * Zoom)

            Dim spacing As Single = Math.Max(1.0F, para.LineSpacing)
            Dim fontFactorPara As Single = FattoreFontParagrafo(para)
            Dim larghezzaDisponibile As Integer = Math.Max(40, w - (para.IndentLevel * CInt(40 * Zoom)))

            Dim righe = WGWordLayoutMotore.CalcolaRighe(para, larghezzaDisponibile, Zoom, fontFactorPara)
            Return WGWordLayoutMotore.AltezzaTotaleParagrafo(righe, spacing)
        Catch ex As Exception
            logger.ScriviLog("MeasureParagraphHeight: " & ex.Message)
            Return CInt(20 * Zoom)
        End Try
    End Function

    Private Function FattoreFontParagrafo(para As DocParagraph) As Single
        Try
            If para Is Nothing Then Return 1.0F
            Dim isNotaPie As Boolean = Not String.IsNullOrEmpty(para.BookmarkId) AndAlso (para.BookmarkId.StartsWith("NOTA_PIE_") OrElse para.BookmarkId.StartsWith("NOTA_FINE_"))
            Return If(isNotaPie, 0.9F, 1.0F)
        Catch ex As Exception
            logger.ScriviLog("FattoreFontParagrafo: " & ex.Message)
            Return 1.0F
        End Try
    End Function

    Public Function MeasureTableHeight(el As DocElement, w As Integer) As Integer
        If el Is Nothing Then Return CInt(20 * Zoom)
        Dim hdc As IntPtr = IntPtr.Zero
        Try
            Dim tbl As DocTable = TryCast(el, DocTable)
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return CInt(20 * Zoom)
            
            Dim h As Integer = 0
            hdc = CreateCompatibleDC(IntPtr.Zero)
            
            For Each r As DocTableRow In tbl.Rows
                If r Is Nothing OrElse r.Cells Is Nothing Then Continue For
                
                Dim maxRow As Integer = CInt(20 * Zoom)
                Dim baseColW As Integer = w \ Math.Max(1, r.Cells.Count)
                
                For Each c As DocTableCell In r.Cells
                    If c Is Nothing OrElse c.Paragraphs Is Nothing Then Continue For
                    
                    Dim cellH As Integer = 0
                    Dim actualColW As Integer = baseColW * Math.Max(1, c.ColSpan)
                    
                    For Each p As DocParagraph In c.Paragraphs
                        cellH += MeasureParagraphHeight(p, Math.Max(1, actualColW - CInt(10 * Zoom)))
                    Next
                    
                    If cellH = 0 Then cellH = CInt(20 * Zoom)
                    maxRow = Math.Max(maxRow, cellH + CInt(10 * Zoom))
                Next
                h += maxRow
            Next
            Return h + CInt(20 * Zoom)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return CInt(20 * Zoom)
        Finally
            If hdc <> IntPtr.Zero Then DeleteDC(hdc)
        End Try
    End Function

    Private Function PaintDoubleBuffered(hwnd As IntPtr, rc As Rectangle, control As WGWord) As Integer
        Dim totalH As Integer = 0
        If hwnd = IntPtr.Zero OrElse rc.Width <= 0 OrElse rc.Height <= 0 OrElse control Is Nothing Then Return 0
        
        Dim ps As New PaintStruct()
        Dim hdcScreen As IntPtr = BeginPaint(hwnd, ps)
        If hdcScreen = IntPtr.Zero Then Return 0
        
        Dim hdcMem As IntPtr = IntPtr.Zero
        Dim hbmMem As IntPtr = IntPtr.Zero
        Dim hbmOld As IntPtr = IntPtr.Zero

        Try
            hdcMem = CreateCompatibleDC(hdcScreen)
            If hdcMem = IntPtr.Zero Then Return 0
            
            hbmMem = CreateCompatibleBitmap(hdcScreen, rc.Width, rc.Height)
            If hbmMem = IntPtr.Zero Then Return 0
            
            hbmOld = SelectObject(hdcMem, hbmMem)
            
            ' Lo spazio di lavoro intorno al foglio segue il tema di Windows, come il resto dell'interfaccia; il foglio
            ' stesso resta sempre bianco con testo nero, come in ogni programma di scrittura: deve restare uguale a
            ' come verra' stampato o esportato, non diventare illeggibile (testo nero su pagina nera) al cambio di tema.
            Dim coloreWorkspace As Color = If(WGTema.Scuro, Color.FromArgb(45, 45, 45), Color.FromArgb(235, 235, 235))
            Dim hBrushWorkspace As IntPtr = CreateSolidBrush(ConvertColorToWin32(coloreWorkspace))
            If hBrushWorkspace <> IntPtr.Zero Then
                Dim rectWorkspace As New RECT With {.Left = 0, .Top = 0, .Right = rc.Width, .Bottom = rc.Height}
                FillRect(hdcMem, rectWorkspace, hBrushWorkspace)
                DeleteObject(hBrushWorkspace)
            End If

            Dim pageWidth As Integer
            If FullScreenLayout Then
                pageWidth = Math.Max(rc.Width - (PAGE_MARGIN_X * 2), 100)
            Else
                pageWidth = Math.Min(rc.Width - (PAGE_MARGIN_X * 2), CInt(1200 * Zoom))
            End If

            Dim pageLeft As Integer = Math.Max(PAGE_MARGIN_X, (rc.Width - pageWidth) \ 2)
            Dim pageRight As Integer = pageLeft + pageWidth
            Dim pageBottom As Integer = Math.Max(rc.Height, CInt(2000 * Zoom))

            Dim hBrushShadow As IntPtr = CreateSolidBrush(ConvertColorToWin32(If(WGTema.Scuro, Color.FromArgb(20, 20, 20), Color.FromArgb(200, 200, 200))))
            If hBrushShadow <> IntPtr.Zero Then
                Dim rectShadow As New RECT With {.Left = pageLeft + 4, .Top = PAGE_MARGIN_TOP + 4, .Right = pageRight + 4, .Bottom = pageBottom}
                FillRect(hdcMem, rectShadow, hBrushShadow)
                DeleteObject(hBrushShadow)
            End If

            Dim hBrushPage As IntPtr = CreateSolidBrush(ConvertColorToWin32(Color.White))
            If hBrushPage <> IntPtr.Zero Then
                Dim rectPage As New RECT With {.Left = pageLeft, .Top = PAGE_MARGIN_TOP, .Right = pageRight, .Bottom = pageBottom}
                FillRect(hdcMem, rectPage, hBrushPage)
                DeleteObject(hBrushPage)
            End If

            Dim hPenPageBorder As IntPtr = CreatePen(PS_SOLID, 1, ConvertColorToWin32(Color.FromArgb(210, 210, 210)))
            If hPenPageBorder <> IntPtr.Zero Then
                Dim oldPenBorder As IntPtr = SelectObject(hdcMem, hPenPageBorder)
                Dim hNullBrush As IntPtr = GetStockObject(NULL_BRUSH)
                Dim oldBrushNull As IntPtr = SelectObject(hdcMem, hNullBrush)
                Rectangle(hdcMem, pageLeft, PAGE_MARGIN_TOP, pageRight, pageBottom)
                SelectObject(hdcMem, oldBrushNull)
                SelectObject(hdcMem, oldPenBorder)
                DeleteObject(hPenPageBorder)
            End If

            SetBkMode(hdcMem, TRANSPARENT)

            Dim offsetContenutoAlto As Integer = CInt(15 * Zoom)
            Try
                If control IsNot Nothing AndAlso Not String.IsNullOrEmpty(control.HeaderText) Then
                    Dim hFontHeader As IntPtr = GetCachedFont("Segoe UI", 9, False, True, False, False)
                    If hFontHeader <> IntPtr.Zero Then
                        Dim oldFontHeader As IntPtr = SelectObject(hdcMem, hFontHeader)
                        SetTextColor(hdcMem, ConvertColorToWin32(Color.FromArgb(120, 120, 120)))
                        Dim testoHeader As String = control.HeaderText
                        TextOut(hdcMem, pageLeft + CInt(20 * Zoom), PAGE_MARGIN_TOP + CInt(6 * Zoom), testoHeader, testoHeader.Length)
                        SelectObject(hdcMem, oldFontHeader)
                    End If
                    offsetContenutoAlto = CInt(35 * Zoom)
                End If
            Catch exHeaderRender As Exception
                logger.ScriviLog("PaintDoubleBuffered rendering intestazione: " & exHeaderRender.Message)
            End Try

            Try
                If control IsNot Nothing AndAlso Not String.IsNullOrEmpty(control.FooterText) Then
                    Dim hFontFooter As IntPtr = GetCachedFont("Segoe UI", 9, False, True, False, False)
                    If hFontFooter <> IntPtr.Zero Then
                        Dim oldFontFooter As IntPtr = SelectObject(hdcMem, hFontFooter)
                        SetTextColor(hdcMem, ConvertColorToWin32(Color.FromArgb(120, 120, 120)))
                        Dim testoFooter As String = control.FooterText
                        TextOut(hdcMem, pageLeft + CInt(20 * Zoom), pageBottom - CInt(24 * Zoom), testoFooter, testoFooter.Length)
                        SelectObject(hdcMem, oldFontFooter)
                    End If
                End If
            Catch exFooterRender As Exception
                logger.ScriviLog("PaintDoubleBuffered rendering piè di pagina: " & exFooterRender.Message)
            End Try

            Dim capsOn As Boolean = (GetKeyState(VK_CAPITAL) And 1) = 1
            If capsOn Then
                Dim hFontCaps As IntPtr = GetCachedFont("Segoe UI", 9, True, False, False, False)
                If hFontCaps <> IntPtr.Zero Then
                    Dim oldFontCaps As IntPtr = SelectObject(hdcMem, hFontCaps)
                    SetTextColor(hdcMem, ConvertColorToWin32(Color.Red))
                    Dim capsText As String = "MAIUSCOLE ATTIVE"
                    TextOut(hdcMem, pageLeft + 5, Math.Max(0, PAGE_MARGIN_TOP - 18), capsText, capsText.Length)
                    SelectObject(hdcMem, oldFontCaps)
                End If
            End If

            Dim contentRect As New Rectangle(pageLeft + CInt(20 * Zoom), PAGE_MARGIN_TOP + offsetContenutoAlto, LarghezzaContenuto(rc.Width), rc.Height)
            totalH = DrawContent(hdcMem, contentRect, control)

            Dim hBrushTitle As IntPtr = CreateSolidBrush(ConvertColorToWin32(WGTema.Sfondo))
            If hBrushTitle <> IntPtr.Zero Then
                Dim rectTitle As New RECT With {.Left = 0, .Top = 0, .Right = rc.Width, .Bottom = 30}
                FillRect(hdcMem, rectTitle, hBrushTitle)
                DeleteObject(hBrushTitle)
            End If

            Dim hFontTitle As IntPtr = GetCachedFont("Arial", 12, True, False, False, False)
            If hFontTitle <> IntPtr.Zero Then
                Dim oldFontTitle As IntPtr = SelectObject(hdcMem, hFontTitle)
                SetTextColor(hdcMem, ConvertColorToWin32(WGTema.Testo))
                SetBkMode(hdcMem, TRANSPARENT)
                Dim title As String = If(String.IsNullOrEmpty(control._docTitle), "", control._docTitle)
                TextOut(hdcMem, 10, 5, title, title.Length)
                SelectObject(hdcMem, oldFontTitle)
            End If

            BitBlt(hdcScreen, 0, 0, rc.Width, rc.Height, hdcMem, 0, 0, SRCCOPY)

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        Finally
            If hdcMem <> IntPtr.Zero Then
                If hbmOld <> IntPtr.Zero Then SelectObject(hdcMem, hbmOld)
                If hbmMem <> IntPtr.Zero Then DeleteObject(hbmMem)
                DeleteDC(hdcMem)
            End If
            EndPaint(hwnd, ps)
        End Try
        Return totalH
    End Function

    Private Function DrawContent(hdc As IntPtr, clipRect As Rectangle, control As WGWord) As Integer
        Try
            If control Is Nothing OrElse hdc = IntPtr.Zero Then Return 0

            Dim currentY As Integer = clipRect.Y - control._vScrollPos
            Dim width As Integer = Math.Max(1, clipRect.Width)
            Dim totalH As Integer = clipRect.Y

            Dim localElements As List(Of DocElement)
            SyncLock control._elementsLock
                If control._elements Is Nothing Then Return 0
                localElements = New List(Of DocElement)(control._elements)
            End SyncLock

            Dim noteAPiePagina As New List(Of DocParagraph)()
            Dim noteDiChiusura As New List(Of DocParagraph)()
            Dim elementiCorpo As New List(Of (El As DocElement, Idx As Integer))()

            For i As Integer = 0 To localElements.Count - 1
                If TypeOf localElements(i) Is DocParagraph Then
                    Dim pScan As DocParagraph = DirectCast(localElements(i), DocParagraph)
                    If Not String.IsNullOrEmpty(pScan.BookmarkId) Then
                        If pScan.BookmarkId.StartsWith("NOTA_PIE_") Then
                            noteAPiePagina.Add(pScan)
                            Continue For
                        ElseIf pScan.BookmarkId.StartsWith("NOTA_FINE_") Then
                            noteDiChiusura.Add(pScan)
                            Continue For
                        End If
                    End If
                End If
                elementiCorpo.Add((localElements(i), i))
            Next

            Dim altezzaNotePie As Integer = CalcolaAltezzaNote(noteAPiePagina, width)
            Dim altezzaNoteFine As Integer = CalcolaAltezzaNote(noteDiChiusura, width)
            Dim altezzaTotaleNote As Integer = 0
            If noteAPiePagina.Count > 0 Then altezzaTotaleNote += altezzaNotePie + CInt(24 * Zoom)
            If noteDiChiusura.Count > 0 Then altezzaTotaleNote += altezzaNoteFine + CInt(24 * Zoom)

            Dim idxPrimaNotaPieInCorpo As Integer = -1
            For Each coppia In elementiCorpo
                If TypeOf coppia.El Is DocParagraph Then
                    Dim pC As DocParagraph = DirectCast(coppia.El, DocParagraph)
                    If pC.Runs IsNot Nothing Then
                        For Each r As DocRun In pC.Runs
                            If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl.StartsWith("NOTA_PIE_") Then
                                idxPrimaNotaPieInCorpo = coppia.Idx
                                Exit For
                            End If
                        Next
                    End If
                End If
                If idxPrimaNotaPieInCorpo >= 0 Then Exit For
            Next

            For Each coppia In elementiCorpo
                Dim el As DocElement = coppia.El
                Dim i As Integer = coppia.Idx
                If el Is Nothing Then Continue For

                Dim isTitoloPara As Boolean = False
                If TypeOf el Is DocParagraph Then
                    Dim pCurr As DocParagraph = DirectCast(el, DocParagraph)
                    If control._stiliTitolo IsNot Nothing AndAlso Not String.IsNullOrEmpty(pCurr.BookmarkId) AndAlso control._stiliTitolo.ContainsKey(pCurr.BookmarkId) Then
                        isTitoloPara = True
                    End If
                End If

                If isTitoloPara AndAlso i > 0 Then
                    currentY += CInt(12 * Zoom)
                    totalH += CInt(12 * Zoom)
                End If

                Dim elHeight As Integer = el.CachedHeight
                If elHeight = -1 Then elHeight = CInt(20 * Zoom)

                If currentY + elHeight > 0 AndAlso currentY < clipRect.Bottom + control._vScrollPos Then
                    If TypeOf el Is DocParagraph Then
                        DrawParagraph(hdc, DirectCast(el, DocParagraph), clipRect.X, currentY, width, (i = control._caretIndex), i, -1, control)
                    ElseIf TypeOf el Is DocTable Then
                        DrawTable(hdc, DirectCast(el, DocTable), clipRect.X, currentY, width, (i = control._caretIndex), i, control)
                    End If
                End If

                currentY += elHeight
                totalH += elHeight
            Next

            If noteAPiePagina.Count > 0 Then
                Dim noteY As Integer = currentY + CInt(12 * Zoom)
                totalH += CInt(12 * Zoom)

                Dim lineX1 As Integer = clipRect.X + 5
                Dim lineX2 As Integer = clipRect.X + Math.Max(80, width \ 3)
                Dim hPenSep As IntPtr = CreatePen(PS_SOLID, 1, ConvertColorToWin32(Color.FromArgb(150, 150, 150)))
                If hPenSep <> IntPtr.Zero Then
                    Dim oldPenSep As IntPtr = SelectObject(hdc, hPenSep)
                    MoveToEx(hdc, lineX1, noteY, IntPtr.Zero)
                    LineTo(hdc, lineX2, noteY)
                    SelectObject(hdc, oldPenSep)
                    DeleteObject(hPenSep)
                End If
                noteY += CInt(6 * Zoom)
                totalH += CInt(6 * Zoom)

                For nIdx As Integer = 0 To noteAPiePagina.Count - 1
                    Dim notaPara As DocParagraph = noteAPiePagina(nIdx)
                    Dim pseudoIdx As Integer = localElements.IndexOf(notaPara)
                    Dim isFocused As Boolean = (control._caretIndex = pseudoIdx)
                    Dim hNota As Integer = DrawParagraph(hdc, notaPara, clipRect.X, noteY, width, isFocused, pseudoIdx, -1, control)
                    noteY += hNota
                    totalH += hNota
                Next
            End If

            If noteDiChiusura.Count > 0 Then
                Dim noteY As Integer = currentY + CInt(12 * Zoom)
                If noteAPiePagina.Count > 0 Then noteY += altezzaNotePie + CInt(18 * Zoom)
                totalH += CInt(12 * Zoom)

                Dim lineX1 As Integer = clipRect.X + 5
                Dim lineX2 As Integer = clipRect.X + Math.Max(80, width \ 3)
                Dim hPenSep As IntPtr = CreatePen(PS_SOLID, 1, ConvertColorToWin32(Color.FromArgb(150, 150, 150)))
                If hPenSep <> IntPtr.Zero Then
                    Dim oldPenSep As IntPtr = SelectObject(hdc, hPenSep)
                    MoveToEx(hdc, lineX1, noteY, IntPtr.Zero)
                    LineTo(hdc, lineX2, noteY)
                    MoveToEx(hdc, lineX1, noteY + 3, IntPtr.Zero)
                    LineTo(hdc, lineX2, noteY + 3)
                    SelectObject(hdc, oldPenSep)
                    DeleteObject(hPenSep)
                End If
                noteY += CInt(8 * Zoom)
                totalH += CInt(8 * Zoom)

                For nIdx As Integer = 0 To noteDiChiusura.Count - 1
                    Dim notaPara As DocParagraph = noteDiChiusura(nIdx)
                    Dim pseudoIdx As Integer = localElements.IndexOf(notaPara)
                    Dim isFocused As Boolean = (control._caretIndex = pseudoIdx)
                    Dim hNota As Integer = DrawParagraph(hdc, notaPara, clipRect.X, noteY, width, isFocused, pseudoIdx, -1, control)
                    noteY += hNota
                    totalH += hNota
                Next
            End If

            Return totalH
        Catch ex As Exception
            logger.ScriviLog("DrawContent Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function CalcolaAltezzaNote(note As List(Of DocParagraph), w As Integer) As Integer
        Dim h As Integer = 0
        Try
            For Each notaPara As DocParagraph In note
                Dim elH As Integer = notaPara.CachedHeight
                If elH <= 0 Then
                    elH = MeasureParagraphHeight(notaPara, w)
                    notaPara.CachedHeight = elH
                End If
                h += elH
            Next
        Catch ex As Exception
            logger.ScriviLog("CalcolaAltezzaNote Error: " & ex.Message)
        End Try
        Return h
    End Function

    Private Function DrawParagraph(hdc As IntPtr, para As DocParagraph, x As Integer, y As Integer, w As Integer, isFocusedElement As Boolean, elementIndex As Integer, subIndex As Integer, control As WGWord) As Integer
        Try
            If para Is Nothing OrElse hdc = IntPtr.Zero OrElse control Is Nothing Then Return CInt(20 * Zoom)

            Dim isNotaPara As Boolean = Not String.IsNullOrEmpty(para.BookmarkId) AndAlso (para.BookmarkId.StartsWith("NOTA_PIE_") OrElse para.BookmarkId.StartsWith("NOTA_FINE_"))
            Dim fontFactorPara As Single = If(isNotaPara, 0.9F, 1.0F)
            Dim isTitoloPara As Boolean = control._stiliTitolo IsNot Nothing AndAlso Not String.IsNullOrEmpty(para.BookmarkId) AndAlso control._stiliTitolo.ContainsKey(para.BookmarkId)
            Dim hasCommentiPara As Boolean = control._commentiPerParagrafo IsNot Nothing AndAlso Not String.IsNullOrEmpty(para.BookmarkId) AndAlso control._commentiPerParagrafo.ContainsKey(para.BookmarkId) AndAlso control._commentiPerParagrafo(para.BookmarkId).Count > 0

            If hasCommentiPara Then
                Dim altezzaPara As Integer = para.CachedHeight
                If altezzaPara <= 0 Then altezzaPara = CInt(20 * Zoom)
                Dim hBrushCommento As IntPtr = CreateSolidBrush(ConvertColorToWin32(Color.FromArgb(255, 200, 0)))
                If hBrushCommento <> IntPtr.Zero Then
                    Dim rectCmt As New RECT With {.Left = x, .Top = y, .Right = x + 3, .Bottom = y + altezzaPara}
                    FillRect(hdc, rectCmt, hBrushCommento)
                    DeleteObject(hBrushCommento)
                End If
            End If

            If para.IsPageBreak Then
                Dim hPenLine As IntPtr = CreatePen(PS_DASH, 1, ConvertColorToWin32(Color.LightGray))
                If hPenLine <> IntPtr.Zero Then
                    Dim oldPen As IntPtr = SelectObject(hdc, hPenLine)
                    MoveToEx(hdc, x + CInt(10 * Zoom), y + CInt(30 * Zoom), IntPtr.Zero)
                    LineTo(hdc, x + w - CInt(10 * Zoom), y + CInt(30 * Zoom))
                    SelectObject(hdc, oldPen)
                    DeleteObject(hPenLine)
                End If
                
                Dim lblText As String = "Interruzione di pagina"
                Dim hFontLbl As IntPtr = GetCachedFont("Calibri", 9, False, True, False, False)
                If hFontLbl <> IntPtr.Zero Then
                    Dim oldFontLbl As IntPtr = SelectObject(hdc, hFontLbl)
                    SetTextColor(hdc, ConvertColorToWin32(Color.Gray))
                    Dim lblSize As SIZE
                    GetTextExtentPoint32(hdc, lblText, lblText.Length, lblSize)
                    
                    SetBkMode(hdc, OPAQUE)
                    SetBkColor(hdc, ConvertColorToWin32(Color.White))
                    TextOut(hdc, x + (w - lblSize.cx) \ 2, y + CInt(30 * Zoom) - (lblSize.cy \ 2), lblText, lblText.Length)
                    SetBkMode(hdc, TRANSPARENT)
                    SelectObject(hdc, oldFontLbl)
                End If

                If isFocusedElement AndAlso control.Focused AndAlso control._caretCharIndex = 0 Then
                    SetCaretPos(x, y + CInt(15 * Zoom))
                End If
                Return CInt(60 * Zoom)
            End If
            Return DrawParagrafoRighe(hdc, para, x, y, w, fontFactorPara, isFocusedElement, elementIndex, subIndex, control)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return CInt(20 * Zoom)
        End Try
    End Function

    Private Function DrawParagrafoRighe(hdc As IntPtr, para As DocParagraph, x As Integer, y As Integer, w As Integer, fontFactorPara As Single, isFocusedElement As Boolean, elementIndex As Integer, subIndex As Integer, control As WGWord) As Integer
        Try
            Dim spacing As Single = Math.Max(1.0F, para.LineSpacing)
            Dim indentX As Integer = x + (para.IndentLevel * CInt(40 * Zoom))
            Dim larghezzaDisponibile As Integer = Math.Max(40, w - (para.IndentLevel * CInt(40 * Zoom)))
            Dim righe As List(Of WGWordLayoutMotore.WGRigaVisuale) = WGWordLayoutMotore.CalcolaRighe(para, larghezzaDisponibile, Zoom, fontFactorPara)

            If para.Runs Is Nothing OrElse para.Runs.Count = 0 Then
                If isFocusedElement AndAlso control.Focused AndAlso control._caretCharIndex = 0 Then
                    SetCaretPos(indentX, y)
                End If
                Return WGWordLayoutMotore.AltezzaTotaleParagrafo(righe, spacing)
            End If

            Dim align As String = If(para.Alignment, "")
            Dim selStartIdx, selEndIdx, selStartSub, selEndSub, selStartChar, selEndChar As Integer
            control.CalculateSelectionRange(selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)

            Dim currentY As Integer = y
            Dim altezzaTotale As Integer = 0

            For rIdx As Integer = 0 To righe.Count - 1
                Dim riga As WGWordLayoutMotore.WGRigaVisuale = righe(rIdx)
                Dim baseX As Integer = indentX

                If rIdx = 0 AndAlso para.IsListItem Then
                    baseX = DisegnaMarcatoreLista(hdc, para, indentX, currentY)
                End If

                Dim extraSpaceWidth As Single = 0

                If align.Equals("Center", StringComparison.OrdinalIgnoreCase) Then
                    baseX = x + Math.Max(0, (w - riga.Larghezza) \ 2) + (para.IndentLevel * CInt(40 * Zoom))
                ElseIf align.Equals("Right", StringComparison.OrdinalIgnoreCase) Then
                    baseX = x + Math.Max(0, w - riga.Larghezza) - (para.IndentLevel * CInt(40 * Zoom))
                ElseIf align.Equals("Justify", StringComparison.OrdinalIgnoreCase) AndAlso rIdx < righe.Count - 1 Then
                    Dim spaceCountRiga As Integer = 0
                    For Each seg As WGWordLayoutMotore.WGSegmentoRiga In riga.Segmenti
                        spaceCountRiga += seg.Testo.Count(Function(c) c = " "c)
                    Next
                    If spaceCountRiga > 0 AndAlso riga.Larghezza < larghezzaDisponibile Then
                        extraSpaceWidth = CSng(larghezzaDisponibile - riga.Larghezza) / spaceCountRiga
                    End If
                End If

                Dim currentX As Integer = baseX
                For Each segmento As WGWordLayoutMotore.WGSegmentoRiga In riga.Segmenti
                    currentX = DisegnaSegmento(hdc, segmento, currentX, currentY, extraSpaceWidth, isFocusedElement, elementIndex, subIndex, control, selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)
                Next

                If riga.Segmenti.Count = 0 AndAlso isFocusedElement AndAlso control.Focused AndAlso control._caretCharIndex >= riga.OffsetInizio Then
                    SetCaretPos(baseX, currentY)
                End If

                currentY += CInt(riga.Altezza * spacing)
                altezzaTotale += CInt(riga.Altezza * spacing)
            Next

            Return Math.Max(altezzaTotale, CInt(20 * Zoom))
        Catch ex As Exception
            logger.ScriviLog("DrawParagrafoRighe: " & ex.Message)
            Return CInt(20 * Zoom)
        End Try
    End Function

    Private Function DisegnaMarcatoreLista(hdc As IntPtr, para As DocParagraph, currentX As Integer, y As Integer) As Integer
        Try
            If para.IsListItem AndAlso Not String.IsNullOrEmpty(para.ListNumValue) Then
                Dim testoNumero As String = para.ListNumValue & "."
                Dim hFontNumero As IntPtr = GetCachedFont("Arial", 11 * Zoom, False, False, False, False)
                Dim oldFontNumero As IntPtr = SelectObject(hdc, hFontNumero)
                SetTextColor(hdc, ConvertColorToWin32(Color.Black))
                SetBkMode(hdc, TRANSPARENT)
                TextOut(hdc, currentX + CInt(3 * Zoom), y + CInt(2 * Zoom), testoNumero, testoNumero.Length)
                SelectObject(hdc, oldFontNumero)
                Return currentX + CInt(25 * Zoom)
            ElseIf para.IsListItem Then
                Dim hBrush As IntPtr = CreateSolidBrush(ConvertColorToWin32(Color.Black))
                Dim hPenNull As IntPtr = CreatePen(PS_SOLID, 1, ConvertColorToWin32(Color.Black))
                Dim hBrushVuoto As IntPtr = GetStockObject(NULL_BRUSH)
                If hBrush <> IntPtr.Zero AndAlso hPenNull <> IntPtr.Zero Then
                    Dim oldBrush As IntPtr = SelectObject(hdc, hBrush)
                    Dim oldPenNull As IntPtr = SelectObject(hdc, hPenNull)

                    Dim livello As Integer = Math.Max(0, para.IndentLevel) Mod 3
                    Dim bx1 As Integer = currentX + CInt(5 * Zoom)
                    Dim by1 As Integer = y + CInt(8 * Zoom)
                    Dim bx2 As Integer = currentX + CInt(11 * Zoom)
                    Dim by2 As Integer = y + CInt(14 * Zoom)

                    Select Case livello
                        Case 0
                            Ellipse(hdc, bx1, by1, bx2, by2)
                        Case 1
                            SelectObject(hdc, hBrushVuoto)
                            Ellipse(hdc, bx1, by1, bx2, by2)
                            SelectObject(hdc, hBrush)
                        Case Else
                            Rectangle(hdc, bx1, by1, bx2, by2)
                    End Select

                    SelectObject(hdc, oldPenNull)
                    SelectObject(hdc, oldBrush)
                End If
                If hPenNull <> IntPtr.Zero Then DeleteObject(hPenNull)
                If hBrush <> IntPtr.Zero Then DeleteObject(hBrush)
                Return currentX + CInt(25 * Zoom)
            End If
            Return currentX
        Catch ex As Exception
            logger.ScriviLog("DisegnaMarcatoreLista: " & ex.Message)
            Return currentX
        End Try
    End Function

    Private Function DisegnaSegmento(hdc As IntPtr, segmento As WGWordLayoutMotore.WGSegmentoRiga, currentX As Integer, y As Integer, extraSpaceWidth As Single, isFocusedElement As Boolean, elementIndex As Integer, subIndex As Integer, control As WGWord, selStartIdx As Integer, selStartSub As Integer, selStartChar As Integer, selEndIdx As Integer, selEndSub As Integer, selEndChar As Integer) As Integer
        Try
            Dim run As DocRun = segmento.Run
            If run Is Nothing Then Return currentX

            Dim hFont As IntPtr = WGWordLayoutMotore.OttieniFont(run.FontFamily, segmento.DimensioneFontEffettiva, run.Bold, run.Italic, segmento.Sottolineato, run.Strikethrough)
            Dim oldFont As IntPtr = IntPtr.Zero
            If hFont <> IntPtr.Zero Then oldFont = SelectObject(hdc, hFont)

            Dim runText As String = If(segmento.Testo, "")
            Dim drawY As Integer = y
            If segmento.Sopra Then drawY -= CInt(segmento.Altezza * 0.4)
            If segmento.Sotto Then drawY += CInt(segmento.Altezza * 0.2)

            Dim segStartGlobal As Integer = segmento.OffsetAssoluto
            Dim segEndGlobal As Integer = segmento.OffsetAssoluto + runText.Length

            Dim isSegInSelection As Boolean = False
            Dim selStartInSeg As Integer = -1
            Dim selEndInSeg As Integer = -1

            If control._selectionActive Then
                If subIndex = -1 Then
                    If elementIndex > selStartIdx AndAlso elementIndex < selEndIdx Then
                        isSegInSelection = True
                        selStartInSeg = 0
                        selEndInSeg = runText.Length
                    ElseIf elementIndex = selStartIdx AndAlso elementIndex = selEndIdx Then
                        If segEndGlobal > selStartChar AndAlso segStartGlobal < selEndChar Then
                            isSegInSelection = True
                            selStartInSeg = Math.Max(0, selStartChar - segStartGlobal)
                            selEndInSeg = Math.Min(runText.Length, selEndChar - segStartGlobal)
                        End If
                    ElseIf elementIndex = selStartIdx Then
                        If segEndGlobal > selStartChar Then
                            isSegInSelection = True
                            selStartInSeg = Math.Max(0, selStartChar - segStartGlobal)
                            selEndInSeg = runText.Length
                        End If
                    ElseIf elementIndex = selEndIdx Then
                        If segStartGlobal < selEndChar Then
                            isSegInSelection = True
                            selStartInSeg = 0
                            selEndInSeg = Math.Min(runText.Length, selEndChar - segStartGlobal)
                        End If
                    End If
                Else
                    Dim myPacked As Long = control.PosizioneAssoluta(elementIndex, subIndex, 0)
                    Dim sPacked As Long = control.PosizioneAssoluta(selStartIdx, selStartSub, 0)
                    Dim ePacked As Long = control.PosizioneAssoluta(selEndIdx, selEndSub, 0)

                    If myPacked > sPacked AndAlso myPacked < ePacked Then
                        isSegInSelection = True
                        selStartInSeg = 0
                        selEndInSeg = runText.Length
                    ElseIf myPacked = sPacked AndAlso myPacked = ePacked Then
                        If segEndGlobal > selStartChar AndAlso segStartGlobal < selEndChar Then
                            isSegInSelection = True
                            selStartInSeg = Math.Max(0, selStartChar - segStartGlobal)
                            selEndInSeg = Math.Min(runText.Length, selEndChar - segStartGlobal)
                        End If
                    ElseIf myPacked = sPacked Then
                        If segEndGlobal > selStartChar Then
                            isSegInSelection = True
                            selStartInSeg = Math.Max(0, selStartChar - segStartGlobal)
                            selEndInSeg = runText.Length
                        End If
                    ElseIf myPacked = ePacked Then
                        If segStartGlobal < selEndChar Then
                            isSegInSelection = True
                            selStartInSeg = 0
                            selEndInSeg = Math.Min(runText.Length, selEndChar - segStartGlobal)
                        End If
                    End If
                End If
            End If

            Dim coloreTestoBaseTema As Color = ColoreDaTema(WGWordLayoutMotore.WGTemaDocumento.ColoreTestoScuro1, Color.Black)
            Dim win32TextColor As Integer = ParseHexColor(run.ColorHex, coloreTestoBaseTema)
            If (run.IsHyperlink OrElse Not String.IsNullOrEmpty(run.LinkUrl)) AndAlso (String.IsNullOrEmpty(run.ColorHex) OrElse run.ColorHex.ToLower() = "nero") Then
                Dim coloreHyperlinkTema As Color = ColoreDaTema(WGWordLayoutMotore.WGTemaDocumento.ColoreHyperlink, Color.Blue)
                win32TextColor = ConvertColorToWin32(coloreHyperlinkTema)
            End If

            If Not String.IsNullOrEmpty(run.HighlightColorHex) AndAlso run.HighlightColorHex.ToLower() <> "nessuno" AndAlso Not isSegInSelection AndAlso runText.Length > 0 Then
                Dim hBrushHi As IntPtr = CreateSolidBrush(ParseHexColor(run.HighlightColorHex, Color.Yellow))
                If hBrushHi <> IntPtr.Zero Then
                    Dim hiRect As New RECT With {.Left = currentX, .Top = drawY, .Right = currentX + segmento.Larghezza, .Bottom = drawY + segmento.Altezza}
                    FillRect(hdc, hiRect, hBrushHi)
                    DeleteObject(hBrushHi)
                End If
            End If

            Dim nuovaX As Integer = currentX

            If isSegInSelection AndAlso selEndInSeg > selStartInSeg AndAlso runText.Length > 0 Then
                Dim p1 As String = If(selStartInSeg >= 0 AndAlso selStartInSeg <= runText.Length, runText.Substring(0, selStartInSeg), "")
                Dim p2 As String = If(selStartInSeg >= 0 AndAlso selEndInSeg >= selStartInSeg AndAlso selEndInSeg <= runText.Length, runText.Substring(selStartInSeg, selEndInSeg - selStartInSeg), "")
                Dim p3 As String = If(selEndInSeg >= 0 AndAlso selEndInSeg <= runText.Length, runText.Substring(selEndInSeg), "")

                SetBkMode(hdc, TRANSPARENT)

                If p1.Length > 0 Then
                    SetTextColor(hdc, win32TextColor)
                    TextOut(hdc, nuovaX, drawY, p1, p1.Length)
                    Dim s As SIZE
                    GetTextExtentPoint32(hdc, p1, p1.Length, s)
                    nuovaX += s.cx
                End If

                If p2.Length > 0 Then
                    Dim s As SIZE
                    GetTextExtentPoint32(hdc, p2, p2.Length, s)
                    Dim hBrushSel As IntPtr = CreateSolidBrush(ConvertColorToWin32(Color.FromArgb(0, 120, 215)))
                    If hBrushSel <> IntPtr.Zero Then
                        Dim rRect As New RECT With {.Left = nuovaX, .Top = drawY, .Right = nuovaX + s.cx, .Bottom = drawY + segmento.Altezza}
                        FillRect(hdc, rRect, hBrushSel)
                        DeleteObject(hBrushSel)
                    End If
                    SetTextColor(hdc, ConvertColorToWin32(Color.White))
                    TextOut(hdc, nuovaX, drawY, p2, p2.Length)
                    nuovaX += s.cx
                End If

                If p3.Length > 0 Then
                    SetTextColor(hdc, win32TextColor)
                    TextOut(hdc, nuovaX, drawY, p3, p3.Length)
                    Dim s As SIZE
                    GetTextExtentPoint32(hdc, p3, p3.Length, s)
                    nuovaX += s.cx
                End If
            Else
                SetTextColor(hdc, win32TextColor)
                SetBkMode(hdc, TRANSPARENT)
                If extraSpaceWidth > 0 AndAlso runText.Length > 0 Then
                    For cIdx As Integer = 0 To runText.Length - 1
                        Dim charStr As String = runText(cIdx).ToString()
                        TextOut(hdc, nuovaX, drawY, charStr, 1)
                        Dim s As SIZE
                        GetTextExtentPoint32(hdc, charStr, 1, s)
                        nuovaX += s.cx
                        If charStr = " " Then nuovaX += CInt(Math.Round(extraSpaceWidth))
                    Next
                ElseIf runText.Length > 0 Then
                    TextOut(hdc, nuovaX, drawY, runText, runText.Length)
                    nuovaX += segmento.Larghezza
                End If
            End If

            If isFocusedElement Then
                Dim relIndex As Integer = control._caretCharIndex - segStartGlobal
                If relIndex >= 0 AndAlso relIndex <= runText.Length Then
                    Dim txtPre As String = runText.Substring(0, relIndex)
                    Dim size As SIZE
                    GetTextExtentPoint32(hdc, txtPre, txtPre.Length, size)
                    If control.Focused Then SetCaretPos(currentX + size.cx, drawY)
                End If
            End If

            If oldFont <> IntPtr.Zero Then SelectObject(hdc, oldFont)
            Return nuovaX
        Catch ex As Exception
            logger.ScriviLog("DisegnaSegmento: " & ex.Message)
            Return currentX
        End Try
    End Function

    Private Function DrawTable(hdc As IntPtr, table As DocTable, x As Integer, y As Integer, w As Integer, isTableFocused As Boolean, tableIndex As Integer, control As WGWord) As Integer
        Try
            If table Is Nothing OrElse table.Rows Is Nothing OrElse hdc = IntPtr.Zero OrElse control Is Nothing Then Return CInt(20 * Zoom)
            
            Dim totalHeight As Integer = 0
            Dim rowY As Integer = y
            
            Dim borderCol As Integer = ConvertColorToWin32(Color.FromArgb(180, 180, 180))
            Dim focusCol As Integer = ConvertColorToWin32(Color.FromArgb(0, 120, 215))

            For r As Integer = 0 To table.Rows.Count - 1
                Dim row As DocTableRow = table.Rows(r)
                If row Is Nothing OrElse row.Cells Is Nothing Then Continue For
                
                Dim maxRowHeight As Integer = CInt(20 * Zoom)
                Dim baseColWidth As Integer = w \ Math.Max(1, row.Cells.Count)
                Dim cellX As Integer = x
                
                For c As Integer = 0 To row.Cells.Count - 1
                    Dim cell As DocTableCell = row.Cells(c)
                    If cell Is Nothing OrElse cell.Paragraphs Is Nothing Then Continue For
                    
                    Dim cellHeight As Integer = 0
                    Dim actualColWidth As Integer = baseColWidth * Math.Max(1, cell.ColSpan)
                    
                    For p As Integer = 0 To cell.Paragraphs.Count - 1
                        Dim para As DocParagraph = cell.Paragraphs(p)
                        Dim isParaFocused As Boolean = (isTableFocused AndAlso r = control._tblRow AndAlso c = control._tblCol AndAlso p = control._tblPara)
                        cellHeight += DrawParagraph(hdc, para, cellX + CInt(5 * Zoom), rowY + cellHeight + CInt(5 * Zoom), Math.Max(1, actualColWidth - CInt(10 * Zoom)), isParaFocused, tableIndex, (r * 10000) + (c * 100) + p, control)
                    Next
                    
                    If cell.Paragraphs.Count = 0 Then
                        cellHeight = CInt(20 * Zoom)
                        If isTableFocused AndAlso r = control._tblRow AndAlso c = control._tblCol Then
                            SetCaretPos(cellX + CInt(5 * Zoom), rowY + CInt(5 * Zoom))
                        End If
                    End If

                    Dim finalCellHeight As Integer = Math.Max(cellHeight + CInt(10 * Zoom), CInt(25 * Zoom))
                    
                    Dim penColor As Integer = borderCol
                    Dim penWidth As Integer = 1
                    If isTableFocused AndAlso r = control._tblRow AndAlso c = control._tblCol Then
                        penColor = focusCol
                        penWidth = 2
                    End If
                    
                    Dim hPenBox As IntPtr = CreatePen(PS_SOLID, penWidth, penColor)
                    If hPenBox <> IntPtr.Zero Then
                        Dim oldPenBox As IntPtr = SelectObject(hdc, hPenBox)
                        Dim hNullBrush As IntPtr = GetStockObject(NULL_BRUSH)
                        Dim oldBrush As IntPtr = IntPtr.Zero
                        If hNullBrush <> IntPtr.Zero Then oldBrush = SelectObject(hdc, hNullBrush)
                        
                        Rectangle(hdc, cellX, rowY, cellX + actualColWidth, rowY + finalCellHeight)
                        
                        If oldBrush <> IntPtr.Zero Then SelectObject(hdc, oldBrush)
                        SelectObject(hdc, oldPenBox)
                        DeleteObject(hPenBox)
                    End If
                    
                    maxRowHeight = Math.Max(maxRowHeight, finalCellHeight)
                    cellX += actualColWidth
                Next
                rowY += maxRowHeight
                totalHeight += maxRowHeight
            Next
            Return totalHeight
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return CInt(20 * Zoom)
        End Try
    End Function

    Private Function GetCachedFont(fontName As String, fontSize As Single, bold As Boolean, italic As Boolean, underline As Boolean, strikethrough As Boolean) As IntPtr
        SyncLock _fontCacheLock
            Try
                Dim actualSize As Single = Math.Max(1.0F, fontSize * Zoom)
                Dim safeFontName As String = If(String.IsNullOrEmpty(fontName), "Arial", fontName)
                Dim key As String = safeFontName & "_" & actualSize & "_" & bold & "_" & italic & "_" & underline & "_" & strikethrough
                
                If _fontCache.ContainsKey(key) Then Return _fontCache(key)
                
                Dim hdcScreen As IntPtr = GetDC(IntPtr.Zero)
                Dim dpiY As Integer = 96
                If hdcScreen <> IntPtr.Zero Then
                    dpiY = GetDeviceCaps(hdcScreen, LOGPIXELSY)
                    ReleaseDC(IntPtr.Zero, hdcScreen)
                End If
                
                Dim height As Integer = -CInt(actualSize * dpiY / 72.0F)
                Dim weight As Integer = If(bold, FW_BOLD, FW_NORMAL)
                Dim ulVal As UInteger = If(underline, 1UI, 0UI)
                Dim itVal As UInteger = If(italic, 1UI, 0UI)
                Dim stVal As UInteger = If(strikethrough, 1UI, 0UI)
                
                Dim hFont As IntPtr = CreateFont(height, 0, 0, 0, weight, itVal, ulVal, stVal, ANSI_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, safeFontName)
                
                If hFont <> IntPtr.Zero Then
                    _fontCache.Add(key, hFont)
                End If
                
                Return hFont
            Catch ex As Exception
                logger.ScriviLog(ex.Message)
                Return IntPtr.Zero
            End Try
        End SyncLock
    End Function

    Private Sub SvuotaCacheFont()
        Try
            SyncLock _fontCacheLock
                If _fontCache IsNot Nothing Then
                    For Each hFont As IntPtr In _fontCache.Values
                        If hFont <> IntPtr.Zero Then DeleteObject(hFont)
                    Next
                    _fontCache.Clear()
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("SvuotaCacheFont: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SvuotaCacheFont()
        Me.ReleaseHandle()
    End Sub

End Class