Imports System.IO
Imports System.IO.Pipes
Imports System.Security.AccessControl
Imports System.Security.Principal
Imports System.Text
Imports System.Threading

''' <summary>
''' Il canale con cui WinGabriel (o un altro programma dello stesso utente) comanda il lettore già in esecuzione:
''' una pipe con nome, una riga di comando e una riga di risposta. Comandi: STATO, IMPOSTAZIONI, RICARICA,
''' CONFIG percorso, PAUSA, RIPRENDI, ESCI. Il nome contiene la sessione e il desktop, così l'istanza dell'utente
''' e quella della schermata di accesso non si confondono.
''' </summary>
Friend NotInheritable Class ControlloLettore

    Friend Shared Function NomePipe() As String
        Return "WGLettoreControllo_" & NativoApp.IdSessione().ToString() & "_" & NativoApp.NomeDesktop()
    End Function

    ''' <summary>Manda un comando al lettore in esecuzione. Restituisce la risposta, oppure Nothing se non c'è nessun lettore.</summary>
    Friend Shared Function Invia(comando As String, Optional attesaMs As Integer = 800) As String
        Try
            Using client As New NamedPipeClientStream(".", NomePipe(), PipeDirection.InOut)
                client.Connect(attesaMs)
                Dim scrittore As New StreamWriter(client, New UTF8Encoding(False))
                scrittore.AutoFlush = True
                scrittore.WriteLine(comando)
                Dim lettore As New StreamReader(client, Encoding.UTF8)
                Return lettore.ReadLine()
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Friend Shared Function InEsecuzione() As Boolean
        Return Invia("STATO", 300) IsNot Nothing
    End Function

    Private ReadOnly _gestore As Func(Of String, String)
    Private _thread As Thread
    Private _attivo As Integer

    Public Sub New(gestore As Func(Of String, String))
        _gestore = gestore
    End Sub

    Public Sub Avvia()
        If Interlocked.Exchange(_attivo, 1) = 1 Then Return
        _thread = New Thread(AddressOf Ciclo)
        _thread.IsBackground = True
        _thread.Name = "WGLettoreControllo"
        _thread.Start()
    End Sub

    Public Sub Ferma()
        Interlocked.Exchange(_attivo, 0)
        Try
            Using client As New NamedPipeClientStream(".", NomePipe(), PipeDirection.InOut)
                client.Connect(200)
            End Using
        Catch
        End Try
    End Sub

    Private Shared Function Sicurezza() As PipeSecurity
        Dim regole As New PipeSecurity()
        regole.AddAccessRule(New PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow))
        regole.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.LocalSystemSid, Nothing), PipeAccessRights.FullControl, AccessControlType.Allow))
        regole.AddAccessRule(New PipeAccessRule(New SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, Nothing), PipeAccessRights.FullControl, AccessControlType.Allow))
        Return regole
    End Function

    Private Sub Ciclo()
        While Volatile.Read(_attivo) = 1
            Try
                Using server As New NamedPipeServerStream(NomePipe(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, Sicurezza())
                    server.WaitForConnection()
                    If Volatile.Read(_attivo) = 0 Then Exit While
                    Dim lettore As New StreamReader(server, Encoding.UTF8)
                    Dim scrittore As New StreamWriter(server, New UTF8Encoding(False))
                    scrittore.AutoFlush = True
                    Dim comando As String = lettore.ReadLine()
                    Dim risposta As String = "ERRORE"
                    If comando IsNot Nothing Then
                        Try
                            risposta = _gestore(comando.Trim())
                        Catch
                            risposta = "ERRORE"
                        End Try
                    End If
                    scrittore.WriteLine(risposta)
                    Try
                        server.WaitForPipeDrain()
                    Catch
                    End Try
                End Using
            Catch
                Thread.Sleep(200)
            End Try
        End While
    End Sub

End Class
