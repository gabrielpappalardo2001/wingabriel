Imports System.Net.NetworkInformation

Public Class connessione
    Public Function VerificaConnessione() As Boolean
        Try
            Using ping As New Ping()
                Dim reply As PingReply = ping.Send("www.google.com")
                Return reply.Status = IPStatus.Success
            End Using
        Catch ex As Exception
            ' Gestisci eventuali eccezioni (ad esempio, nessuna connessione di rete)
            Return False
        End Try
    End Function
End Class
