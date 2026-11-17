Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text

''' <summary>
''' Chiamate a Windows per sapere su quale desktop gira il lettore, in quale cartella sta e se il processo
''' ha i privilegi di amministratore.
''' </summary>
Friend Module NativoApp

    <DllImport("user32.dll")>
    Private Function GetThreadDesktop(idThread As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Function GetUserObjectInformationW(oggetto As IntPtr, indice As Integer, nome As StringBuilder, lunghezza As Integer, ByRef necessaria As Integer) As Boolean
    End Function

    ''' <summary>Il nome del desktop su cui gira il programma: Default per l'utente, Winlogon per accesso, blocco e UAC.</summary>
    Friend Function NomeDesktop() As String
        Try
            Dim desktop As IntPtr = GetThreadDesktop(GetCurrentThreadId())
            If desktop = IntPtr.Zero Then Return "Default"
            Dim nome As New StringBuilder(256)
            Dim necessaria As Integer = 0
            If GetUserObjectInformationW(desktop, 2, nome, nome.Capacity * 2, necessaria) Then Return nome.ToString()
        Catch
        End Try
        Return "Default"
    End Function

    Friend Function IdSessione() As Integer
        Try
            Return Process.GetCurrentProcess().SessionId
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>Vero se il programma gira con i privilegi di amministratore (o come SYSTEM).</summary>
    Friend Function Elevato() As Boolean
        Try
            Using identita As WindowsIdentity = WindowsIdentity.GetCurrent()
                If identita.IsSystem Then Return True
                Return New WindowsPrincipal(identita).IsInRole(WindowsBuiltInRole.Administrator)
            End Using
        Catch
            Return False
        End Try
    End Function

    Friend Function ESistema() As Boolean
        Try
            Using identita As WindowsIdentity = WindowsIdentity.GetCurrent()
                Return identita.IsSystem
            End Using
        Catch
            Return False
        End Try
    End Function

    Friend Function CartellaProgramma() As String
        Return AppDomain.CurrentDomain.BaseDirectory
    End Function

End Module
