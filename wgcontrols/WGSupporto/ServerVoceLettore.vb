Imports System.IO
Imports System.IO.Pipes
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports System.Threading
Imports wgcontrols

Friend NotInheritable Class ServerVoceLettore

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _bloccoStato As New Object()
    Private Shared _thread As Thread
    Private Shared _attivo As Integer
    Private Shared _scrittore As StreamWriter
    Private Shared ReadOnly _bloccoScrittura As New Object()

    Friend Shared Function NomePipe() As String
        Try
            Return "WGVoceLettore_" & Process.GetCurrentProcess().SessionId.ToString()
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.NomePipe: " & ex.Message)
            Return "WGVoceLettore_0"
        End Try
    End Function

    Friend Shared Sub Avvia()
        Try
            SyncLock _bloccoStato
                If Interlocked.Exchange(_attivo, 1) = 1 Then Return
                _thread = New Thread(AddressOf Ciclo)
                _thread.IsBackground = True
                _thread.Name = "WGVoceLettorePipe"
                _thread.Start()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Shared Sub Ferma()
        Try
            Interlocked.Exchange(_attivo, 0)
            Try
                Using client As New NamedPipeClientStream(".", NomePipe(), PipeDirection.InOut)
                    client.Connect(200)
                End Using
            Catch
            End Try
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Sicurezza() As PipeSecurity
        Dim regole As New PipeSecurity()
        regole.AddAccessRule(New PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite, AccessControlType.Allow))
        regole.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing), PipeAccessRights.ReadWrite, AccessControlType.Allow))
        regole.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, Nothing), PipeAccessRights.ReadWrite, AccessControlType.Allow))
        Return regole
    End Function

    Private Shared Sub Ciclo()
        While Volatile.Read(_attivo) = 1
            Try
                Using server As New NamedPipeServerStream(NomePipe(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 8192, 8192, Sicurezza())
                    server.WaitForConnection()
                    If Volatile.Read(_attivo) = 0 Then Exit While
                    Servi(server)
                End Using
            Catch ex As Exception
                log.ScriviLog("ServerVoceLettore.Ciclo: " & ex.Message)
                Thread.Sleep(500)
            End Try
        End While
    End Sub

    Private Shared Sub Servi(server As NamedPipeServerStream)
        Dim sintesi As sintesivocale = Nothing
        Dim collegato As Boolean = False
        Try
            sintesi = sintesivocale.GetInstance()
            Dim lettore As New StreamReader(server, Encoding.UTF8)
            SyncLock _bloccoScrittura
                _scrittore = New StreamWriter(server, New UTF8Encoding(False))
                _scrittore.AutoFlush = True
            End SyncLock
            AddHandler sintesi.OnFineLettura, AddressOf AlFineLettura
            AddHandler sintesi.OnStatoParlatoCambiato, AddressOf AlCambioStato
            collegato = True
            While Volatile.Read(_attivo) = 1
                Dim riga As String = lettore.ReadLine()
                If riga Is Nothing Then Exit While
                Try
                    Elabora(sintesi, riga)
                Catch exRiga As Exception
                    log.ScriviLog("ServerVoceLettore.Servi (comando): " & exRiga.Message)
                End Try
            End While
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.Servi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If collegato AndAlso sintesi IsNot Nothing Then
                    RemoveHandler sintesi.OnFineLettura, AddressOf AlFineLettura
                    RemoveHandler sintesi.OnStatoParlatoCambiato, AddressOf AlCambioStato
                End If
            Catch exRimozione As Exception
                log.ScriviLog("ServerVoceLettore.Servi (rimozione): " & exRimozione.Message)
            End Try
            SyncLock _bloccoScrittura
                _scrittore = Nothing
            End SyncLock
        End Try
    End Sub

    Private Shared Sub Elabora(sintesi As sintesivocale, riga As String)
        Try
            Dim comando As String = riga.Trim()
            If comando = "" Then Return
            If comando = "STATO" Then
                Manda("STATO " & If(sintesi.StaParlando, "1", "0"))
            ElseIf comando = "FERMA" Then
                sintesi.FermaVoce()
            ElseIf comando.StartsWith("PARLA ", StringComparison.Ordinal) Then
                Dim parti As String() = comando.Substring(6).Split(New Char() {" "c}, 3)
                If parti.Length < 3 Then Return
                Dim priorita As String = If(parti(0) = "1", "Alta", "Normale")
                Dim senzaPunteggiatura As Boolean = (parti(1) = "1")
                If parti(2).Trim() = "" Then Return
                sintesi.LeggiTesto(parti(2), priorita:=priorita, escludiPunteggiatura:=senzaPunteggiatura)
            End If
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.Elabora: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Manda(riga As String)
        Try
            SyncLock _bloccoScrittura
                If _scrittore Is Nothing Then Return
                _scrittore.WriteLine(riga)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.Manda: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub AlFineLettura()
        Try
            Manda("FINE")
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.AlFineLettura: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub AlCambioStato(staParlando As Boolean)
        Try
            Manda("STATO " & If(staParlando, "1", "0"))
        Catch ex As Exception
            log.ScriviLog("ServerVoceLettore.AlCambioStato: " & ex.Message)
        End Try
    End Sub

End Class
