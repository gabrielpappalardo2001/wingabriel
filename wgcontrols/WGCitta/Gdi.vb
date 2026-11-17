Imports System
Imports System.Runtime.InteropServices

''' <summary>Le funzioni di Windows per disegnare (GDI) e per leggere i tasti tenuti premuti.</summary>
Friend NotInheritable Class Gdi

    <StructLayout(LayoutKind.Sequential)>
    Public Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
        Public Sub New(l As Integer, t As Integer, r As Integer, b As Integer)
            Try
                Left = l
                Top = t
                Right = r
                Bottom = b
            Catch ex As Exception
                RegistroErrori.Registra("Gdi.RECT.New", ex)
                Throw
            End Try
        End Sub
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure PUNTO
        Public X As Integer
        Public Y As Integer
    End Structure

    <DllImport("gdi32.dll")> Public Shared Function CreateCompatibleDC(hdc As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Public Shared Function CreateCompatibleBitmap(hdc As IntPtr, w As Integer, h As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Public Shared Function SelectObject(hdc As IntPtr, h As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Public Shared Function DeleteObject(h As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function DeleteDC(hdc As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function BitBlt(dest As IntPtr, x As Integer, y As Integer, w As Integer, h As Integer, src As IntPtr, sx As Integer, sy As Integer, rop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function CreateSolidBrush(colore As Integer) As IntPtr
    End Function
    <DllImport("user32.dll")> Public Shared Function FillRect(hdc As IntPtr, ByRef r As RECT, pennello As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")> Public Shared Function CreatePen(stile As Integer, larghezza As Integer, colore As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")> Public Shared Function MoveToEx(hdc As IntPtr, x As Integer, y As Integer, vecchio As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function LineTo(hdc As IntPtr, x As Integer, y As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function Ellipse(hdc As IntPtr, l As Integer, t As Integer, r As Integer, b As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function Rectangle(hdc As IntPtr, l As Integer, t As Integer, r As Integer, b As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function Polygon(hdc As IntPtr, punti As PUNTO(), n As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")> Public Shared Function SetBkMode(hdc As IntPtr, modo As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")> Public Shared Function SetTextColor(hdc As IntPtr, colore As Integer) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)> Public Shared Function DrawText(hdc As IntPtr, testo As String, n As Integer, ByRef r As RECT, formato As UInteger) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)> Public Shared Function CreateFontW(h As Integer, w As Integer, esc As Integer, ori As Integer, peso As Integer, corsivo As UInteger, sottolineato As UInteger, barrato As UInteger,
                                                                                        charset As UInteger, precisione As UInteger, clip As UInteger, qualita As UInteger, famiglia As UInteger, nome As String) As IntPtr
    End Function
    <DllImport("user32.dll")> Public Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    Public Const SRCCOPY As Integer = &HCC0020
    Public Const TRASPARENTE As Integer = 1
    Public Const DT_LEFT As UInteger = 0
    Public Const DT_CENTER As UInteger = 1
    Public Const DT_SINGLELINE As UInteger = &H20
    Public Const DT_NOPREFIX As UInteger = &H800
    Public Const DT_END_ELLIPSIS As UInteger = &H8000

    ''' <summary>Un colore di GDI (che è BGR) da rosso, verde e blu.</summary>
    Public Shared Function Rgb(r As Integer, g As Integer, b As Integer) As Integer
        Try
            Return r Or (g << 8) Or (b << 16)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Rgb", ex)
            Throw
        End Try
    End Function

    Public Shared Sub Riempi(hdc As IntPtr, r As RECT, colore As Integer)
        Try
            Dim p As IntPtr = CreateSolidBrush(colore)
            FillRect(hdc, r, p)
            DeleteObject(p)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Riempi", ex)
            Throw
        End Try
    End Sub

    Public Shared Sub Linea(hdc As IntPtr, x1 As Integer, y1 As Integer, x2 As Integer, y2 As Integer, colore As Integer, larghezza As Integer)
        Try
            Dim p As IntPtr = CreatePen(0, Math.Max(1, larghezza), colore)
            Dim vecchia As IntPtr = SelectObject(hdc, p)
            MoveToEx(hdc, x1, y1, IntPtr.Zero)
            LineTo(hdc, x2, y2)
            SelectObject(hdc, vecchia)
            DeleteObject(p)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Linea", ex)
            Throw
        End Try
    End Sub

    Public Shared Sub Cerchio(hdc As IntPtr, cx As Integer, cy As Integer, raggio As Integer, riempimento As Integer, bordo As Integer)
        Try
            Dim b As IntPtr = CreateSolidBrush(riempimento)
            Dim p As IntPtr = CreatePen(0, 1, bordo)
            Dim vb As IntPtr = SelectObject(hdc, b)
            Dim vp As IntPtr = SelectObject(hdc, p)
            Ellipse(hdc, cx - raggio, cy - raggio, cx + raggio + 1, cy + raggio + 1)
            SelectObject(hdc, vb)
            SelectObject(hdc, vp)
            DeleteObject(b)
            DeleteObject(p)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Cerchio", ex)
            Throw
        End Try
    End Sub

    Public Shared Sub Rettangolo(hdc As IntPtr, l As Integer, t As Integer, r As Integer, b As Integer, riempimento As Integer, bordo As Integer)
        Try
            Dim br As IntPtr = CreateSolidBrush(riempimento)
            Dim p As IntPtr = CreatePen(0, 1, bordo)
            Dim vb As IntPtr = SelectObject(hdc, br)
            Dim vp As IntPtr = SelectObject(hdc, p)
            Rectangle(hdc, l, t, r, b)
            SelectObject(hdc, vb)
            SelectObject(hdc, vp)
            DeleteObject(br)
            DeleteObject(p)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Rettangolo", ex)
            Throw
        End Try
    End Sub

    Public Shared Sub Poligono(hdc As IntPtr, punti As PUNTO(), riempimento As Integer, bordo As Integer)
        Try
            Dim br As IntPtr = CreateSolidBrush(riempimento)
            Dim p As IntPtr = CreatePen(0, 1, bordo)
            Dim vb As IntPtr = SelectObject(hdc, br)
            Dim vp As IntPtr = SelectObject(hdc, p)
            Polygon(hdc, punti, punti.Length)
            SelectObject(hdc, vb)
            SelectObject(hdc, vp)
            DeleteObject(br)
            DeleteObject(p)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Poligono", ex)
            Throw
        End Try
    End Sub

    Public Shared Sub Scrivi(hdc As IntPtr, testo As String, r As RECT, colore As Integer, formato As UInteger)
        Try
            SetBkMode(hdc, TRASPARENTE)
            SetTextColor(hdc, colore)
            DrawText(hdc, testo, testo.Length, r, formato Or DT_NOPREFIX)
        Catch ex As Exception
            RegistroErrori.Registra("Gdi.Scrivi", ex)
            Throw
        End Try
    End Sub
End Class
