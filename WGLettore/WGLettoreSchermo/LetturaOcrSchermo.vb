Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Runtime.InteropServices.WindowsRuntime
Imports UIAutomationClient
Imports Windows.Globalization
Imports Windows.Graphics.Imaging
Imports Windows.Media.Ocr
Imports Windows.Storage.Streams

' Riconoscimento ottico dei caratteri per leggere il contenuto visivo dello schermo.
Friend NotInheritable Class LetturaOcrSchermo

    Private ReadOnly log As New Logger()
    Private _motore As OcrEngine
    Private _provato As Boolean
    Private _assenteAvvisato As Boolean
    Private _ultimoTick As Integer

    Private Const SRCCOPY As Integer = &HCC0020

    <DllImport("user32.dll")>
    Private Shared Function GetDC(finestra As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(finestra As IntPtr, hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleDC(hdc As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleBitmap(hdc As IntPtr, larghezza As Integer, altezza As Integer) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hdc As IntPtr, oggetto As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function BitBlt(dest As IntPtr, x As Integer, y As Integer, larghezza As Integer, altezza As Integer, sorgente As IntPtr, xSorgente As Integer, ySorgente As Integer, operazione As Integer) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(oggetto As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteDC(hdc As IntPtr) As Boolean
    End Function

    Public ReadOnly Property Disponibile As Boolean
        Get
            Return OttieniMotore() IsNot Nothing
        End Get
    End Property

    Public Function MessaggioAssente() As String
        If _assenteAvvisato Then Return ""
        If OttieniMotore() IsNot Nothing Then Return ""
        _assenteAvvisato = True
        Return "Il riconoscimento ottico non è disponibile. Serve il pacchetto lingua italiano di Windows, quello dell'OCR, anche senza rete."
    End Function

    Public Function LeggiElemento(automazione As IUIAutomation, elemento As IUIAutomationElement) As String
        Try
            If Environment.TickCount - _ultimoTick < 700 Then Return ""
            If automazione Is Nothing OrElse elemento Is Nothing Then Return ""
            Dim rettangolo As tagRECT = automazione.VariantToRect(elemento.GetCurrentPropertyValue(DescrittoreElementi.ProprietaRettangolo))
            Dim larghezza As Integer = rettangolo.right - rettangolo.left
            Dim altezza As Integer = rettangolo.bottom - rettangolo.top
            If larghezza < 8 OrElse altezza < 8 Then Return ""
            If larghezza > 1400 Then larghezza = 1400
            If altezza > 900 Then altezza = 900
            _ultimoTick = Environment.TickCount
            Return LeggiSchermo(rettangolo.left, rettangolo.top, larghezza, altezza)
        Catch ex As Exception
            log.ScriviLog("LetturaOcrSchermo.LeggiElemento: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiSchermo(x As Integer, y As Integer, larghezza As Integer, altezza As Integer) As String
        Try
            Dim motoreAttivo As OcrEngine = OttieniMotore()
            If motoreAttivo Is Nothing Then Return ""
            Using bitmap As Bitmap = Cattura(x, y, larghezza, altezza)
                If bitmap Is Nothing Then Return ""
                Using memoria As New MemoryStream()
                    bitmap.Save(memoria, ImageFormat.Bmp)
                    Dim dati() As Byte = memoria.ToArray()
                    Using flusso As New InMemoryRandomAccessStream()
                        flusso.WriteAsync(dati.AsBuffer()).AsTask().Wait(2500)
                        flusso.Seek(0)
                        Dim decodifica As BitmapDecoder = BitmapDecoder.CreateAsync(flusso).AsTask().Result
                        Dim software As SoftwareBitmap = decodifica.GetSoftwareBitmapAsync().AsTask().Result
                        Dim risultato As OcrResult = motoreAttivo.RecognizeAsync(software).AsTask().Result
                        If risultato Is Nothing OrElse risultato.Text Is Nothing Then Return ""
                        Return risultato.Text.Trim()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("LetturaOcrSchermo.LeggiSchermo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function OttieniMotore() As OcrEngine
        If _provato Then Return _motore
        _provato = True
        Try
            _motore = OcrEngine.TryCreateFromLanguage(New Language("it-IT"))
            If _motore Is Nothing Then _motore = OcrEngine.TryCreateFromLanguage(New Language("it"))
            If _motore Is Nothing Then _motore = OcrEngine.TryCreateFromUserProfileLanguages()
        Catch ex As Exception
            log.ScriviLog("LetturaOcrSchermo.Motore: " & ex.Message)
            _motore = Nothing
        End Try
        Return _motore
    End Function

    Private Function Cattura(x As Integer, y As Integer, larghezza As Integer, altezza As Integer) As Bitmap
        Dim schermo As IntPtr = IntPtr.Zero
        Dim memoria As IntPtr = IntPtr.Zero
        Dim bitmap As IntPtr = IntPtr.Zero
        Dim vecchio As IntPtr = IntPtr.Zero
        Try
            schermo = GetDC(IntPtr.Zero)
            If schermo = IntPtr.Zero Then Return Nothing
            memoria = CreateCompatibleDC(schermo)
            bitmap = CreateCompatibleBitmap(schermo, larghezza, altezza)
            vecchio = SelectObject(memoria, bitmap)
            If Not BitBlt(memoria, 0, 0, larghezza, altezza, schermo, x, y, SRCCOPY) Then Return Nothing
            SelectObject(memoria, vecchio)
            vecchio = IntPtr.Zero
            Return Image.FromHbitmap(bitmap)
        Catch ex As Exception
            log.ScriviLog("LetturaOcrSchermo.Cattura: " & ex.Message)
            Return Nothing
        Finally
            If vecchio <> IntPtr.Zero AndAlso memoria <> IntPtr.Zero Then SelectObject(memoria, vecchio)
            If bitmap <> IntPtr.Zero Then DeleteObject(bitmap)
            If memoria <> IntPtr.Zero Then DeleteDC(memoria)
            If schermo <> IntPtr.Zero Then ReleaseDC(IntPtr.Zero, schermo)
        End Try
    End Function

End Class
