Imports System.Diagnostics
Imports System.Runtime.InteropServices

' Controllo automatico del blocco del sistema. Monitora il processo di Esplora risorse per rilevare blocchi e gestire il riavvio.
Friend NotInheritable Class WatchdogAutomazione

    Private Const RpcServerNonDisponibile As Integer = &H800706BA
    Private Const RpcChiamataFallita As Integer = &H800706BE
    Private Const RpcDisconnesso As Integer = &H80010108
    Private Const RpcRiprova As Integer = &H8001010A
    Private Const TimeoutAttesa As Integer = &H800705B4
    Private Const ServerFault As Integer = &H80010105

    Private ReadOnly log As New Logger()
    Private _errori As Integer
    Private _ultimoRiavvio As Integer
    Private _ultimoControlloExplorer As Integer
    Private _pidExplorer As Integer

    Public Sub New()
        _pidExplorer = PidExplorer()
    End Sub

    Public Sub Successo()
        _errori = 0
    End Sub

    Public Function Segna(ex As Exception) As Boolean
        Try
            If ex Is Nothing OrElse Not EBlocco(ex) Then Return False
            _errori += 1
            Return _errori >= 3 AndAlso Environment.TickCount - _ultimoRiavvio > 15000
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WatchdogAutomazione.Segna", exAtteso)
            Return False
        End Try
    End Function

    Public Function ExplorerRiavviato() As Boolean
        Try
            If Environment.TickCount - _ultimoControlloExplorer < 2000 Then Return False
            _ultimoControlloExplorer = Environment.TickCount
            Dim attuale As Integer = PidExplorer()
            If attuale = 0 OrElse _pidExplorer = 0 Then
                _pidExplorer = attuale
                Return False
            End If
            If attuale = _pidExplorer Then Return False
            If Environment.TickCount - _ultimoRiavvio < 15000 Then
                _pidExplorer = attuale
                Return False
            End If
            _pidExplorer = attuale
            Return True
        Catch ex As Exception
            log.ScriviLog("WatchdogAutomazione.ExplorerRiavviato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SegnaRiavvio()
        _ultimoRiavvio = Environment.TickCount
        _errori = 0
        _pidExplorer = PidExplorer()
    End Sub

    Private Function EBlocco(ex As Exception) As Boolean
        Dim com As COMException = TryCast(ex, COMException)
        If com IsNot Nothing Then
            Dim codice As Integer = com.ErrorCode
            If codice = RpcServerNonDisponibile OrElse codice = RpcChiamataFallita OrElse codice = RpcDisconnesso OrElse codice = RpcRiprova OrElse codice = TimeoutAttesa OrElse codice = ServerFault Then Return True
        End If
        If TypeOf ex Is TimeoutException OrElse TypeOf ex Is InvalidComObjectException Then Return True
        Dim messaggio As String = If(ex.Message, "").ToLowerInvariant()
        Return messaggio.Contains("timeout") OrElse messaggio.Contains("rpc") OrElse messaggio.Contains("0x800706ba")
    End Function

    Private Function PidExplorer() As Integer
        Dim processi() As Process = Nothing
        Try
            processi = Process.GetProcessesByName("explorer")
            If processi Is Nothing OrElse processi.Length = 0 Then Return 0
            Return processi(0).Id
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WatchdogAutomazione.PidExplorer", exAtteso)
            Return 0
        Finally
            If processi IsNot Nothing Then
                For Each processo In processi
                    Try
                        processo.Dispose()
                    Catch exAtteso2 As Exception
                        RegistroErrori.Scrivi("WatchdogAutomazione.PidExplorer", exAtteso2)
                    End Try
                Next
            End If
        End Try
    End Function

End Class
