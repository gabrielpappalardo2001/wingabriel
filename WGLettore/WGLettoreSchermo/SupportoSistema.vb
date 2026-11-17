Imports System.Runtime.InteropServices
Imports System.Text

' Interazione con le API di Windows per la gestione dei processi, dei token di sicurezza, dei desktop e delle metriche di sistema.
Friend NotInheritable Class SupportoSistema

    Private Const TokenIntegrityLevel As Integer = 25
    Private Const TokenUiAccess As Integer = 26
    Private Const MediumRid As Integer = &H2000
    Private Const HighRid As Integer = &H3000
    Private Const SystemRid As Integer = &H4000

    Private ReadOnly log As New Logger()
    Private _dettoPrivilegi As Boolean

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcessToken(processo As IntPtr, accesso As UInteger, ByRef token As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(maniglia As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Shared Function GetTokenInformation(token As IntPtr, classe As Integer, informazione As IntPtr, lunghezza As Integer, ByRef restituita As Integer) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function OpenInputDesktop(flag As UInteger, eredita As Boolean, accesso As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Shared Function GetUserObjectInformation(oggetto As IntPtr, indice As Integer, buffer As StringBuilder, lunghezza As Integer, ByRef necessaria As Integer) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function CloseDesktop(desktop As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(indice As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(finestra As IntPtr, ByRef processo As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(accesso As UInteger, eredita As Boolean, processo As UInteger) As IntPtr
    End Function

    Public Function UiAccessAttivo() As Boolean
        Return LeggiInteroToken(TokenUiAccess) <> 0
    End Function

    Public Function LivelloIntegrita() As String
        Dim rid As Integer = RidIntegrita()
        If rid >= SystemRid Then Return "system"
        If rid >= HighRid Then Return "alto"
        If rid >= MediumRid Then Return "medio"
        If rid > 0 Then Return "basso"
        Return "sconosciuto"
    End Function

    Public Function DesktopCorrente() As String
        Dim desktop As IntPtr = IntPtr.Zero
        Try
            desktop = OpenInputDesktop(0UI, False, 1UI)
            If desktop = IntPtr.Zero Then Return ""
            Dim nome As New StringBuilder(256)
            Dim necessaria As Integer = 0
            If Not GetUserObjectInformation(desktop, 2, nome, nome.Capacity, necessaria) Then Return ""
            Return nome.ToString()
        Catch ex As Exception
            log.ScriviLog("SupportoSistema.DesktopCorrente: " & ex.Message)
            Return ""
        Finally
            If desktop <> IntPtr.Zero Then CloseDesktop(desktop)
        End Try
    End Function

    Public Function SuDesktopSicuro() As Boolean
        Dim nome As String = DesktopCorrente()
        If nome = "" Then Return False
        Return Not nome.Equals("Default", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Function RettangoloVirtuale() As String
        Try
            Dim x As Integer = GetSystemMetrics(76)
            Dim y As Integer = GetSystemMetrics(77)
            Dim larghezza As Integer = GetSystemMetrics(78)
            Dim altezza As Integer = GetSystemMetrics(79)
            Return x.ToString() & "," & y.ToString() & " " & larghezza.ToString() & "x" & altezza.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("SupportoSistema.RettangoloVirtuale", exAtteso)
            Return ""
        End Try
    End Function

    Public Function FinestraPiuElevata(finestra As IntPtr) As Boolean
        Dim processo As IntPtr = IntPtr.Zero
        Try
            If finestra = IntPtr.Zero OrElse UiAccessAttivo() Then Return False
            Dim pid As UInteger = 0UI
            GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI Then Return False
            processo = OpenProcess(&H1000UI, False, pid)
            If processo = IntPtr.Zero Then Return False
            Dim loro As Integer = RidIntegritaProcesso(processo)
            Dim nostro As Integer = RidIntegrita()
            If loro = 0 OrElse nostro = 0 Then Return False
            Return loro > nostro
        Catch ex As Exception
            log.ScriviLog("SupportoSistema.FinestraPiuElevata: " & ex.Message)
            Return False
        Finally
            If processo <> IntPtr.Zero Then CloseHandle(processo)
        End Try
    End Function

    Public Function AvvisoPrivilegi(finestraElevata As Boolean) As String
        Try
            If Not finestraElevata Then Return ""
            If UiAccessAttivo() Then Return ""
            If _dettoPrivilegi Then Return ""
            _dettoPrivilegi = True
            Return "Questa finestra ha privilegi più alti. Senza UIAccess il lettore non la vede per intero."
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("SupportoSistema.AvvisoPrivilegi", exAtteso)
            Return ""
        End Try
    End Function

    Public Function Riassunto() As String
        Dim parti As New List(Of String)()
        parti.Add("integrità " & LivelloIntegrita())
        parti.Add(If(UiAccessAttivo(), "UIAccess attivo", "UIAccess assente"))
        Dim desktop As String = DesktopCorrente()
        If desktop <> "" Then parti.Add("desktop " & desktop)
        Dim schermo As String = RettangoloVirtuale()
        If schermo <> "" Then parti.Add("schermo " & schermo)
        Return String.Join(", ", parti)
    End Function

    Private Function RidIntegritaProcesso(processo As IntPtr) As Integer
        Dim token As IntPtr = IntPtr.Zero
        Try
            If processo = IntPtr.Zero Then Return 0
            If Not OpenProcessToken(processo, &H20008UI, token) Then Return 0
            Return LeggiRid(token)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("SupportoSistema.RidIntegritaProcesso", exAtteso)
            Return 0
        Finally
            If token <> IntPtr.Zero Then CloseHandle(token)
        End Try
    End Function

    Private Function RidIntegrita() As Integer
        Return RidIntegritaProcesso(ProcessHandle())
    End Function

    Private Function LeggiRid(token As IntPtr) As Integer
        Dim memoria As IntPtr = IntPtr.Zero
        Try
            If token = IntPtr.Zero Then Return 0
            Dim serve As Integer = 0
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, serve)
            If serve <= 0 Then Return 0
            memoria = Marshal.AllocHGlobal(serve)
            If Not GetTokenInformation(token, TokenIntegrityLevel, memoria, serve, serve) Then Return 0
            Dim sid As IntPtr = Marshal.ReadIntPtr(memoria)
            Dim conteggio As Integer = Marshal.ReadByte(IntPtr.Add(sid, 1))
            If conteggio <= 0 Then Return 0
            Dim autorita As Integer = 2 + 6
            Dim ultimo As IntPtr = IntPtr.Add(sid, autorita + (conteggio - 1) * 4)
            Return Marshal.ReadInt32(ultimo)
        Catch ex As Exception
            log.ScriviLog("SupportoSistema.RidIntegrita: " & ex.Message)
            Return 0
        Finally
            If memoria <> IntPtr.Zero Then Marshal.FreeHGlobal(memoria)
        End Try
    End Function

    Private Function LeggiInteroToken(classe As Integer) As Integer
        Dim token As IntPtr = IntPtr.Zero
        Dim memoria As IntPtr = IntPtr.Zero
        Try
            If Not OpenProcessToken(ProcessHandle(), &H20008UI, token) Then Return 0
            memoria = Marshal.AllocHGlobal(4)
            Dim serve As Integer = 0
            If Not GetTokenInformation(token, classe, memoria, 4, serve) Then Return 0
            Return Marshal.ReadInt32(memoria)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("SupportoSistema.LeggiInteroToken", exAtteso)
            Return 0
        Finally
            If memoria <> IntPtr.Zero Then Marshal.FreeHGlobal(memoria)
            If token <> IntPtr.Zero Then CloseHandle(token)
        End Try
    End Function

    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentProcess() As IntPtr
    End Function

    Private Function ProcessHandle() As IntPtr
        Try
            Return GetCurrentProcess()
        Catch ex As Exception
            log.ScriviLog("SupportoSistema.ProcessHandle: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

End Class
