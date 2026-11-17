Imports System.Runtime.InteropServices
Imports UIAutomationClient

' Controllo del puntatore del mouse, rilevamento degli elementi sotto il cursore e simulazione dei clic.
Friend NotInheritable Class NavigatoreOggetti

    Private ReadOnly log As New Logger()
    Private ReadOnly _automazione As IUIAutomation
    Private ReadOnly _cache As IUIAutomationCacheRequest
    Private _ultimoPuntoX As Integer
    Private _ultimoPuntoY As Integer
    Private _ultimoTick As Integer
    Private _chiave As String = ""

    Private Const SRCCOPY As Integer = &HCC0020
    Private Const MOUSEEVENTF_LEFTDOWN As Integer = &H2
    Private Const MOUSEEVENTF_LEFTUP As Integer = &H4

    <DllImport("user32.dll")>
    Private Shared Function GetCursorPos(ByRef punto As tagPOINT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetCursorPos(x As Integer, y As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Sub mouse_event(flag As Integer, dx As Integer, dy As Integer, dati As Integer, extra As IntPtr)
    End Sub

    Public Sub New(automazione As IUIAutomation, cache As IUIAutomationCacheRequest)
        _automazione = automazione
        _cache = cache
    End Sub

    Public Function ElementoSottoPuntatore(ByRef spostato As Boolean) As IUIAutomationElement
        spostato = False
        Try
            If _automazione Is Nothing Then Return Nothing
            Dim punto As New tagPOINT()
            If Not GetCursorPos(punto) Then Return Nothing
            If Math.Abs(punto.x - _ultimoPuntoX) < 6 AndAlso Math.Abs(punto.y - _ultimoPuntoY) < 6 Then Return Nothing
            If Environment.TickCount - _ultimoTick < 120 Then Return Nothing
            _ultimoPuntoX = punto.x
            _ultimoPuntoY = punto.y
            _ultimoTick = Environment.TickCount
            spostato = True
            Dim elemento As IUIAutomationElement = _automazione.ElementFromPointBuildCache(punto, _cache)
            If elemento Is Nothing Then Return Nothing
            Dim identita As String = Chiave(elemento)
            If identita <> "" AndAlso identita = _chiave Then Return Nothing
            _chiave = identita
            Return elemento
        Catch ex As Exception
            log.ScriviLog("NavigatoreOggetti.ElementoSottoPuntatore: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function PortaMouse(elemento As IUIAutomationElement) As Boolean
        Try
            Dim x As Integer = 0
            Dim y As Integer = 0
            If Not PuntoCliccabile(elemento, x, y) Then Return False
            Return SetCursorPos(x, y)
        Catch ex As Exception
            log.ScriviLog("NavigatoreOggetti.PortaMouse: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Clicca(elemento As IUIAutomationElement) As Boolean
        Try
            Dim x As Integer = 0
            Dim y As Integer = 0
            If Not PuntoCliccabile(elemento, x, y) Then Return False
            If Not SetCursorPos(x, y) Then Return False
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero)
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero)
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigatoreOggetti.Clicca: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SegnaElemento(elemento As IUIAutomationElement)
        _chiave = Chiave(elemento)
    End Sub

    Private Function PuntoCliccabile(elemento As IUIAutomationElement, ByRef x As Integer, ByRef y As Integer) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim punto As New tagPOINT()
            Dim trovato As Integer = 0
            Try
                trovato = elemento.GetClickablePoint(punto)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigatoreOggetti.PuntoCliccabile", exAtteso)
                trovato = 0
            End Try
            If trovato <> 0 AndAlso (punto.x <> 0 OrElse punto.y <> 0) Then
                x = punto.x
                y = punto.y
                Return True
            End If
            If _automazione Is Nothing Then Return False
            Dim rettangolo As tagRECT = _automazione.VariantToRect(elemento.GetCurrentPropertyValue(DescrittoreElementi.ProprietaRettangolo))
            Dim larghezza As Integer = rettangolo.right - rettangolo.left
            Dim altezza As Integer = rettangolo.bottom - rettangolo.top
            If larghezza < 2 OrElse altezza < 2 Then Return False
            x = rettangolo.left + larghezza \ 2
            y = rettangolo.top + altezza \ 2
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigatoreOggetti.PuntoCliccabile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function Chiave(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigatoreOggetti.Chiave", exAtteso)
            Return ""
        End Try
    End Function

End Class
