Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

' Dichiarazioni di funzioni API di Windows, ganci per tastiera, eventi e gestione degli appunti.
Friend NotInheritable Class NativoLettore

    Public Const WH_KEYBOARD_LL As Integer = 13
    Public Const WM_KEYDOWN As Integer = &H100
    Public Const WM_KEYUP As Integer = &H101
    Public Const WM_SYSKEYDOWN As Integer = &H104
    Public Const WM_SYSKEYUP As Integer = &H105
    Public Const WM_QUIT As Integer = &H12
    Public Const LLKHF_INJECTED As Integer = &H10
    Public Const KEYEVENTF_EXTENDEDKEY As Integer = &H1
    Public Const KEYEVENTF_KEYUP As Integer = &H2
    Public Const EVENT_SYSTEM_FOREGROUND As UInteger = &H3UI
    Public Const EVENT_OBJECT_FOCUS As UInteger = &H8005UI
    Public Const EVENT_OBJECT_STATECHANGE As UInteger = &H800AUI
    Public Const EVENT_OBJECT_NAMECHANGE As UInteger = &H800CUI
    Public Const EVENT_OBJECT_VALUECHANGE As UInteger = &H800EUI
    Public Const EVENT_OBJECT_TEXTSELECTIONCHANGED As UInteger = &H8014UI
    Public Const WINEVENT_OUTOFCONTEXT As UInteger = &H0UI
    Public Const WINEVENT_SKIPOWNPROCESS As UInteger = &H2UI
    Public Const OBJID_CLIENT As Integer = -4
    Public Const OBJID_CARET As Integer = -8
    Public Const OBJID_CURSOR As Integer = -9
    Public Const OBJID_WINDOW As Integer = 0
    Public Const GA_ROOT As UInteger = 2UI
    Public Const GA_PARENT As UInteger = 1UI
    Public Const WM_GETTEXT As Integer = &HD
    Public Const WM_GETTEXTLENGTH As Integer = &HE
    Public Const EM_GETSEL As Integer = &HB0
    Public Const SMTO_ABORTIFHUNG As UInteger = &H2UI
    Public Const CF_UNICODETEXT As UInteger = 13UI
    Public Const GMEM_MOVEABLE As UInteger = &H2UI
    Public Const VK_CAPITAL As Integer = &H14

    <StructLayout(LayoutKind.Sequential)>
    Public Structure KBDLLHOOKSTRUCT
        Public vkCode As Integer
        Public scanCode As Integer
        Public flags As Integer
        Public time As Integer
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure MSG
        Public hwnd As IntPtr
        Public message As Integer
        Public wParam As IntPtr
        Public lParam As IntPtr
        Public time As Integer
        Public ptX As Integer
        Public ptY As Integer
    End Structure

    Public Delegate Function ProceduraTastiera(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    Public Delegate Sub ProceduraEventi(hWinEventHook As IntPtr, eventType As UInteger, hwnd As IntPtr, idObject As Integer, idChild As Integer, dwEventThread As UInteger, dwmsEventTime As UInteger)

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function SetWindowsHookEx(idHook As Integer, lpfn As ProceduraTastiera, hMod As IntPtr, dwThreadId As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function UnhookWindowsHookEx(hhk As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Public Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Public Shared Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll")>
    Public Shared Function SetWinEventHook(eventMin As UInteger, eventMax As UInteger, hmodWinEventProc As IntPtr, lpfnWinEventProc As ProceduraEventi, idProcess As UInteger, idThread As UInteger, dwFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Public Shared Function UnhookWinEvent(hWinEventHook As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function GetMessage(ByRef lpMsg As MSG, hWnd As IntPtr, wMsgFilterMin As UInteger, wMsgFilterMax As UInteger) As Integer
    End Function

    <DllImport("user32.dll")>
    Public Shared Function TranslateMessage(ByRef lpMsg As MSG) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function DispatchMessage(ByRef lpMsg As MSG) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function PostThreadMessage(idThread As UInteger, msg As Integer, wParam As IntPtr, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Public Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function GetWindowTextLength(hWnd As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function InternalGetWindowText(hWnd As IntPtr, pString As StringBuilder, cchMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function GetClassName(hWnd As IntPtr, lpClassName As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    Public Const GW_OWNER As UInteger = 4UI

    <DllImport("user32.dll")>
    Public Shared Function GetWindow(hWnd As IntPtr, uCmd As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Public Shared Function GetAncestor(hWnd As IntPtr, gaFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Public Shared Function FindWindowEx(hwndParent As IntPtr, hwndChildAfter As IntPtr, lpszClass As String, lpszWindow As String) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Public Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Public Structure PuntoCursore
        Public X As Integer
        Public Y As Integer
    End Structure

    <DllImport("user32.dll")>
    Public Shared Function GetCursorPos(ByRef punto As PuntoCursore) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function SetCursorPos(x As Integer, y As Integer) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    Public Delegate Function ProceduraFinestre(hWnd As IntPtr, lParam As IntPtr) As Boolean

    <DllImport("user32.dll")>
    Public Shared Function EnumWindows(lpEnumFunc As ProceduraFinestre, lParam As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function IsWindowVisible(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Function IsWindow(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowLongW")>
    Public Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("dwmapi.dll")>
    Public Shared Function DwmGetWindowAttribute(hwnd As IntPtr, dwAttribute As Integer, ByRef pvAttribute As Integer, cbAttribute As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Public Shared Function IsHungAppWindow(hWnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("UIAutomationCore.dll")>
    Public Shared Function UiaHasServerSideProvider(hwnd As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function OpenProcess(accessoDesiderato As UInteger, ereditabile As Boolean, idProcesso As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function CloseHandle(maniglia As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Public Shared Function QueryFullProcessImageName(processo As IntPtr, flag As UInteger, percorso As StringBuilder, ByRef dimensione As UInteger) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Public Shared Sub keybd_event(bVk As Byte, bScan As Byte, dwFlags As Integer, dwExtraInfo As IntPtr)
    End Sub

    <DllImport("user32.dll")>
    Public Shared Function GetKeyState(nVirtKey As Integer) As Short
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="SendMessageTimeoutW")>
    Public Shared Function SendMessageTimeout(hWnd As IntPtr, Msg As Integer, wParam As IntPtr, lParam As IntPtr, fuFlags As UInteger, uTimeout As UInteger, ByRef lpdwResult As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="SendMessageTimeoutW")>
    Public Shared Function SendMessageTimeoutTesto(hWnd As IntPtr, Msg As Integer, wParam As IntPtr, lParam As StringBuilder, fuFlags As UInteger, uTimeout As UInteger, ByRef lpdwResult As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function OpenClipboard(hWndNewOwner As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function CloseClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function EmptyClipboard() As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Public Shared Function SetClipboardData(uFormat As UInteger, hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function GlobalAlloc(uFlags As UInteger, dwBytes As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function GlobalLock(hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function GlobalUnlock(hMem As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function GlobalFree(hMem As IntPtr) As IntPtr
    End Function

    Public Shared Function BlocMaiuscoleAttivo() As Boolean
        Try
            Return (GetKeyState(VK_CAPITAL) And 1) <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NativoLettore.BlocMaiuscoleAttivo", exAtteso)
            Return False
        End Try
    End Function

    Public Shared Function LeggiSelezioneEdit(hwnd As IntPtr, ByRef inizio As Integer, ByRef fine As Integer) As Boolean
        Dim puntatoreInizio As IntPtr = IntPtr.Zero
        Dim puntatoreFine As IntPtr = IntPtr.Zero
        Try
            inizio = 0
            fine = 0
            If hwnd = IntPtr.Zero Then Return False
            puntatoreInizio = Marshal.AllocHGlobal(4)
            puntatoreFine = Marshal.AllocHGlobal(4)
            Marshal.WriteInt32(puntatoreInizio, 0)
            Marshal.WriteInt32(puntatoreFine, 0)
            Dim risultato As IntPtr = IntPtr.Zero
            Dim esito As IntPtr = SendMessageTimeout(hwnd, EM_GETSEL, puntatoreInizio, puntatoreFine, SMTO_ABORTIFHUNG, 200UI, risultato)
            If esito = IntPtr.Zero Then Return False
            inizio = Marshal.ReadInt32(puntatoreInizio)
            fine = Marshal.ReadInt32(puntatoreFine)
            If inizio < 0 OrElse fine < 0 Then Return False
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NativoLettore.LeggiSelezioneEdit", exAtteso)
            Return False
        Finally
            If puntatoreInizio <> IntPtr.Zero Then Marshal.FreeHGlobal(puntatoreInizio)
            If puntatoreFine <> IntPtr.Zero Then Marshal.FreeHGlobal(puntatoreFine)
        End Try
    End Function

    Public Shared Function LeggiTestoFinestra(hwnd As IntPtr, massimo As Integer) As String
        Try
            If hwnd = IntPtr.Zero Then Return ""
            Dim risultato As IntPtr = IntPtr.Zero
            Dim esitoLunghezza As IntPtr = SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 200UI, risultato)
            If esitoLunghezza = IntPtr.Zero Then Return ""
            Dim lunghezza As Integer = 0
            Try
                lunghezza = risultato.ToInt32()
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NativoLettore.LeggiTestoFinestra", exAtteso)
                lunghezza = 0
            End Try
            If lunghezza <= 0 Then Return ""
            If lunghezza > massimo Then lunghezza = massimo
            Dim buffer As New StringBuilder(lunghezza + 1)
            Dim scritto As IntPtr = IntPtr.Zero
            Dim esito As IntPtr = SendMessageTimeoutTesto(hwnd, WM_GETTEXT, New IntPtr(buffer.Capacity), buffer, SMTO_ABORTIFHUNG, 300UI, scritto)
            If esito = IntPtr.Zero Then Return ""
            Return buffer.ToString()
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("NativoLettore.LeggiTestoFinestra", exAtteso2)
            Return ""
        End Try
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetClipboardData(uFormat As UInteger) As IntPtr
    End Function

    Public Shared Function LeggiTestoAppunti() As String
        Dim aperto As Boolean = False
        Try
            For tentativo As Integer = 1 To 6
                aperto = OpenClipboard(IntPtr.Zero)
                If aperto Then Exit For
                Thread.Sleep(20)
            Next
            If Not aperto Then Return ""
            Dim memoria As IntPtr = GetClipboardData(CF_UNICODETEXT)
            If memoria = IntPtr.Zero Then Return ""
            Dim blocco As IntPtr = GlobalLock(memoria)
            If blocco = IntPtr.Zero Then Return ""
            Try
                Return If(Marshal.PtrToStringUni(blocco), "")
            Finally
                GlobalUnlock(memoria)
            End Try
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NativoLettore.LeggiTestoAppunti", exAtteso)
            Return ""
        Finally
            If aperto Then CloseClipboard()
        End Try
    End Function

    Public Shared Function CopiaTesto(testo As String) As Boolean
        Dim memoria As IntPtr = IntPtr.Zero
        Dim aperto As Boolean = False
        Try
            If testo Is Nothing Then testo = ""
            Dim dati() As Byte = Encoding.Unicode.GetBytes(testo & ChrW(0))
            memoria = GlobalAlloc(GMEM_MOVEABLE, New IntPtr(dati.Length))
            If memoria = IntPtr.Zero Then Return False
            Dim blocco As IntPtr = GlobalLock(memoria)
            If blocco = IntPtr.Zero Then
                GlobalFree(memoria)
                memoria = IntPtr.Zero
                Return False
            End If
            Marshal.Copy(dati, 0, blocco, dati.Length)
            GlobalUnlock(memoria)
            For tentativo As Integer = 1 To 6
                aperto = OpenClipboard(IntPtr.Zero)
                If aperto Then Exit For
                Thread.Sleep(20)
            Next
            If Not aperto Then
                GlobalFree(memoria)
                memoria = IntPtr.Zero
                Return False
            End If
            EmptyClipboard()
            If SetClipboardData(CF_UNICODETEXT, memoria) = IntPtr.Zero Then
                GlobalFree(memoria)
                memoria = IntPtr.Zero
                Return False
            End If
            memoria = IntPtr.Zero
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NativoLettore.CopiaTesto", exAtteso)
            Return False
        Finally
            If aperto Then CloseClipboard()
            If memoria <> IntPtr.Zero Then GlobalFree(memoria)
        End Try
    End Function

End Class
