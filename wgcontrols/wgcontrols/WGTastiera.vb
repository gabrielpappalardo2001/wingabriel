Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports System.Diagnostics
Imports System.Security
Imports System.Threading
Imports System.Text
Imports System.Collections.Concurrent

' Gestione globale della tastiera, traduzione dei tasti secondo il layout scelto, ganci di sistema e invio di input.
Public NotInheritable Class WGTastiera
    Implements IDisposable

#Region "Tastiera per lingua, scelta da WinGabriel"

    ' La lingua della tastiera non segue quella che Windows ha attiva (il cambio lingua di Windows non si legge e non
    ' si usa: vedi la regola "niente interfacce di Windows"): si scieglie qui, in Impostazioni, Tastiera. Il layout si
    ' carica con LoadKeyboardLayout e il flag KLF_NOTELLSHELL, che lo rende disponibile a questo processo per tradurre
    ' i tasti senza toccare né avvisare la shell di Windows: nessuna finestra, nessuna icona che cambia nella barra.

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function LoadKeyboardLayout(pwszKLID As String, flags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function MapVirtualKeyEx(uCode As UInteger, uMapType As UInteger, dwhkl As IntPtr) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ToUnicodeEx(wVirtKey As UInteger, wScanCode As UInteger, lpKeyState As Byte(), pwszBuff As StringBuilder, cchBuff As Integer, wFlags As UInteger, dwhkl As IntPtr) As Integer
    End Function

    Private Const KLF_NOTELLSHELL As UInteger = &H80
    Private Const KLF_SUBSTITUTE_OK As UInteger = &H2

    Private Const ChiaveLayoutTastiera As String = "tastiera_layout"

    ''' <summary>Codice, nome italiano e identificativo Windows (KLID) delle tastiere proposte nelle impostazioni.</summary>
    Public Shared ReadOnly LingueTastiera As String()() = {
        New String() {"it", "Italiano", "00000410"},
        New String() {"en", "Inglese (Stati Uniti)", "00000409"},
        New String() {"en-gb", "Inglese (Regno Unito)", "00000809"},
        New String() {"fr", "Francese", "0000040C"},
        New String() {"de", "Tedesco", "00000407"},
        New String() {"es", "Spagnolo", "0000040A"},
        New String() {"pt", "Portoghese", "00000816"},
        New String() {"pt-br", "Portoghese (Brasile)", "00000416"},
        New String() {"nl", "Olandese", "00000413"},
        New String() {"pl", "Polacco", "00000415"},
        New String() {"ru", "Russo", "00000419"},
        New String() {"el", "Greco", "00000408"},
        New String() {"tr", "Turco", "0000041F"},
        New String() {"sv", "Svedese", "0000041D"},
        New String() {"da", "Danese", "00000406"},
        New String() {"nb", "Norvegese", "00000414"},
        New String() {"fi", "Finlandese", "0000040B"},
        New String() {"cs", "Ceco", "00000405"},
        New String() {"ro", "Rumeno", "00000418"}
    }

    Private Shared ReadOnly _bloccoLayout As New Object()
    Private Shared _hklCaricato As IntPtr = IntPtr.Zero
    Private Shared _codiceLayoutCaricato As String = ""

    ''' <summary>La tastiera scelta in WinGabriel per scrivere nei campi, in Word e negli altri editor (non quella che
    ''' Windows ha attiva): di base l'italiana.</summary>
    Public Shared Property CodiceLinguaTastiera As String
        Get
            Try
                Dim v As String = New WGConfig().LeggiValore(ChiaveLayoutTastiera)
                If String.IsNullOrWhiteSpace(v) Then Return "it"
                If Not LingueTastiera.Any(Function(l) l(0) = v) Then Return "it"
                Return v.Trim()
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGTastiera.CodiceLinguaTastiera Get: " & ex.Message)
                Return "it"
            End Try
        End Get
        Set(value As String)
            Try
                Dim v As String = If(value, "it").Trim()
                If Not LingueTastiera.Any(Function(l) l(0) = v) Then v = "it"
                Dim cfg As New WGConfig()
                cfg.SalvaValore(ChiaveLayoutTastiera, v)
                SyncLock _bloccoLayout
                    _hklCaricato = IntPtr.Zero
                    _codiceLayoutCaricato = ""
                End SyncLock
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGTastiera.CodiceLinguaTastiera Set: " & ex.Message)
            End Try
        End Set
    End Property

    ''' <summary>Il nome della tastiera scelta, per gli annunci.</summary>
    Public Shared Function NomeLinguaTastiera(Optional codice As String = Nothing) As String
        Dim c As String = If(codice, CodiceLinguaTastiera)
        Dim voce As String() = LingueTastiera.FirstOrDefault(Function(l) l(0) = c)
        Return If(voce IsNot Nothing, voce(1), "Italiano")
    End Function

    ''' <summary>Vero se la tastiera scelta in WinGabriel (non quella di Windows) e' l'italiana: i campi e il motore
    ''' Word usano le combinazioni "AltGr" (@ # [ ] { } €) e i simboli sopra i numeri della tastiera italiana solo in
    ''' questo caso; altrimenti lasciano fare a CarattereDaLayoutScelto, che traduce secondo la tastiera scelta.</summary>
    Public Shared Function TastieraItalianaAttiva() As Boolean
        Return String.Equals(CodiceLinguaTastiera, "it", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Shared Function HklLayoutScelto() As IntPtr
        Try
            Dim codice As String = CodiceLinguaTastiera
            SyncLock _bloccoLayout
                If _hklCaricato <> IntPtr.Zero AndAlso _codiceLayoutCaricato = codice Then Return _hklCaricato
                Dim voce As String() = LingueTastiera.FirstOrDefault(Function(l) l(0) = codice)
                Dim klid As String = If(voce IsNot Nothing, voce(2), "00000410")
                Dim h As IntPtr = LoadKeyboardLayout(klid, KLF_NOTELLSHELL Or KLF_SUBSTITUTE_OK)
                If h = IntPtr.Zero Then
                    Dim registro As New Logger()
                    registro.ScriviLog("WGTastiera.HklLayoutScelto: impossibile caricare il layout " & klid)
                    Return IntPtr.Zero
                End If
                _hklCaricato = h
                _codiceLayoutCaricato = codice
                Return h
            End SyncLock
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGTastiera.HklLayoutScelto: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    ''' <summary>Traduce un tasto (codice virtuale piu' lo stato di maiuscole/accenti pronto nell'array di 256 byte,
    ''' come vuole l'API di Windows) nel testo che produce secondo la tastiera scelta in WinGabriel, non quella che
    ''' Windows ha attiva. Stringa vuota se il tasto non produce testo.</summary>
    Public Shared Function CarattereDaLayoutScelto(virtualKey As UInteger, keyboardState As Byte()) As String
        Try
            Dim hkl As IntPtr = HklLayoutScelto()
            If hkl = IntPtr.Zero Then Return ""
            Dim scanCode As UInteger = MapVirtualKeyEx(virtualKey, 0, hkl)
            Dim sb As New StringBuilder(5)
            Dim risultato As Integer = ToUnicodeEx(virtualKey, scanCode, keyboardState, sb, sb.Capacity, 0, hkl)
            If risultato > 0 Then Return sb.ToString(0, risultato)
            Return ""
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGTastiera.CarattereDaLayoutScelto: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Simboli per l'eco di digitazione, di tutte le tastiere"

    ''' <summary>I caratteri che l'eco di digitazione legge subito mentre si scrive (non solo a fine parola): la
    ''' punteggiatura e le lettere accentate di tutte le tastiere che si possono scegliere in Impostazioni, non solo
    ''' quella italiana. Le lettere senza accento non ci sono: quelle le legge la sintesi come lettere normali.</summary>
    Public Shared Function SimboloDaLeggereSubito(c As Char) As Boolean
        Try
            Const simboli As String = "!""£$%&/()=?^€@#][}{§°*_:;<>'`~+-\|¤µ²³·¨´½¼¾¬÷×–—„‹›№₺"
            If simboli.IndexOf(c) >= 0 Then Return True
            Const lettereAccentate As String =
                "àèéìòùÀÈÉÌÒÙ" & ' italiano
                "âêîôûäëïöüÇçœŒæÆ" & ' francese e portoghese (lettere in piu')
                "ÄÖÜß" & ' tedesco (le minuscole ä ö ü sono gia' sopra)
                "ñÑ" & ' spagnolo
                "ãÃõÕ" & ' portoghese
                "åÅøØ" & ' tastiere nordiche
                "ąĄćĆęĘłŁńŃśŚźŹżŻ" & ' polacco
                "čČďĎěĚňŇřŘšŠťŤůŮýÝžŽ" & ' ceco
                "ğĞıİşŞ" & ' turco
                "ăĂâÂîÎșȘțȚ" ' rumeno
            Return lettereAccentate.IndexOf(c) >= 0
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGTastiera.SimboloDaLeggereSubito: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Il nome pronunciato di un simbolo di punteggiatura o di valuta, per leggerlo mentre si scrive invece
    ''' di lasciarlo muto: copre tutte le tastiere scelte in Impostazioni (non solo l'italiana), quindi anche i segni
    ''' che arrivano con l'AltGr o la riga dei numeri di francese, tedesco, spagnolo, portoghese, nordiche, polacca,
    ''' ceca, turca e rumena. Le lettere (anche accentate) non sono qui: restituisce il carattere stesso, che la
    ''' sintesi legge come lettera.</summary>
    Public Shared Function NomeSimbolo(c As Char) As String
        Try
            Select Case AscW(c)
                Case 32 : Return "Spazio"
                Case 9 : Return "Tabulazione"
                Case 13, 10 : Return "A capo"
                Case 8226 : Return "Elenco puntato"
                Case 8230 : Return "Puntini di sospensione"
            End Select
            Select Case c
                Case "."c : Return "Punto"
                Case ","c : Return "Virgola"
                Case ";"c : Return "Punto e virgola"
                Case ":"c : Return "Due punti"
                Case "!"c : Return "Punto esclamativo"
                Case "?"c : Return "Punto interrogativo"
                Case "("c : Return "Aperta parentesi"
                Case ")"c : Return "Chiusa parentesi"
                Case "["c : Return "Aperta quadra"
                Case "]"c : Return "Chiusa quadra"
                Case "{"c : Return "Aperta graffa"
                Case "}"c : Return "Chiusa graffa"
                Case "<"c : Return "Minore"
                Case ">"c : Return "Maggiore"
                Case "="c : Return "Uguale"
                Case "+"c : Return "Più"
                Case "-"c : Return "Meno"
                Case "*"c : Return "Asterisco"
                Case "/"c : Return "Barra"
                Case "\"c : Return "Barra rovesciata"
                Case "|"c : Return "Barra verticale"
                Case "_"c : Return "Sottolineato"
                Case "@"c : Return "Chiocciola"
                Case "#"c : Return "Cancelletto"
                Case "€"c : Return "Euro"
                Case "$"c : Return "Dollaro"
                Case "%"c : Return "Percentuale"
                Case "&"c : Return "E commerciale"
                Case "'"c : Return "Apostrofo"
                Case """"c : Return "Virgolette"
                Case "^"c : Return "Accento circonflesso"
                Case "~"c : Return "Tilde"
                Case "`"c : Return "Apice retroverso"
                Case "£"c : Return "Sterlina"
                Case "§"c : Return "Paragrafo"
                Case "°"c : Return "Grado"
                Case "«"c : Return "Virgolette basse aperte"
                Case "»"c : Return "Virgolette basse chiuse"
                Case "¿"c : Return "Punto interrogativo rovesciato"
                Case "¡"c : Return "Punto esclamativo rovesciato"
                Case "¥"c : Return "Yen"
                Case "©"c : Return "Copyright"
                Case "®"c : Return "Marchio registrato"
                Case "™"c : Return "Marchio"
                ' Francese, tedesco, spagnolo, portoghese, nordiche, polacca, ceca, turca e rumena: simboli
                ' dell'AltGr o della riga dei numeri che la sola tastiera italiana non usa.
                Case "¤"c : Return "Simbolo di valuta"
                Case "µ"c : Return "Micro"
                Case "²"c : Return "Due a esponente"
                Case "³"c : Return "Tre a esponente"
                Case "¨"c : Return "Dieresi"
                Case "´"c : Return "Accento acuto"
                Case "·"c : Return "Punto medio"
                Case "½"c : Return "Un mezzo"
                Case "¼"c : Return "Un quarto"
                Case "¾"c : Return "Tre quarti"
                Case "¬"c : Return "Negazione"
                Case "÷"c : Return "Diviso"
                Case "×"c : Return "Per"
                Case "–"c : Return "Trattino medio"
                Case "—"c : Return "Trattino lungo"
                Case ChrW(8216) : Return "Apice singolo aperto"
                Case ChrW(8217) : Return "Apice singolo chiuso"
                Case ChrW(8220) : Return "Virgolette tipografiche aperte"
                Case ChrW(8221) : Return "Virgolette tipografiche chiuse"
                Case "„"c : Return "Virgolette basse"
                Case "‹"c : Return "Virgoletta angolare aperta"
                Case "›"c : Return "Virgoletta angolare chiusa"
                Case "№"c : Return "Numero"
                Case "₺"c : Return "Lira turca"
                Case Else : Return c.ToString()
            End Select
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGTastiera.NomeSimbolo: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

#End Region

    Private WithEvents _mpv As WGMpv
    Private istanzacontrollo As WGCentroDiControllo = WGCentroDiControllo.GetInstance()

    Private Const WH_KEYBOARD_LL As Integer = 13
    Private Const HC_ACTION As Integer = 0
    Private Const VK_CAPITAL As Integer = &H14
    Private Const VK_NUMLOCK As Integer = &H90
    Private Const VK_SCROLL As Integer = &H91
    Private Const SOGLIA_CRITICA_HOOK_MS As Long = 100
    ' Windows disinstalla da solo gli hook di basso livello la cui callback supera il tempo massimo
    ' consentito (300 ms nella configurazione predefinita) senza avvisare in alcun modo.
    ' Windows rimuove l'hook quando la callback supera LowLevelHooksTimeout, che di norma
    ' vale alcuni secondi. A 280 millisecondi il sospetto scattava anche per una pausa del
    ' garbage collector o del disco, facendo reinstallare un hook perfettamente sano e
    ' perdendo tasti durante il ricambio.
    Private Const SOGLIA_DISINSTALLAZIONE_HOOK_MS As Long = 1000
    Private Const COOLDOWN_RIAVVIO_HOOK_MS As Long = 5000
    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_KEYUP As Integer = &H101
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const WM_SYSKEYUP As Integer = &H105

    Private Const GW_OWNER As UInteger = 4

    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WS_EX_APPWINDOW As Integer = &H40000
    Private Const GWL_EXSTYLE As Integer = -20

    Private Const SW_RESTORE As Integer = 9
    Private Const SW_SHOWNORMAL As Integer = 1
    Private Const SW_SHOW As Integer = 5
    Private Const SW_MINIMIZE As Integer = 6

    ' Usati solo per riconoscere un click esplicito dell'utente in MouseHookCallback (vedi
    ' CambioFocusRecenteUtente): non servono a bloccare o modificare il messaggio in alcun
    ' modo, solo a registrare che l'utente ha fisicamente cliccato da qualche parte.
    Private Const WM_LBUTTONDOWN As Integer = &H201
    Private Const WM_RBUTTONDOWN As Integer = &H204
    Private Const WM_MBUTTONDOWN As Integer = &H207

    Private Shared ReadOnly TASTO_BLOCCATO As New IntPtr(1)

    Private Delegate Function LowLevelKeyboardProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    Private Delegate Function EnumWindowsProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelKeyboardProc, hMod As IntPtr, dwThreadId As UInteger) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(hhk As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Private Const INPUT_KEYBOARD As UInteger = 1
    Private Const KEYEVENTF_KEYUP As UInteger = &H2
    Private Const KEYEVENTF_EXTENDEDKEY As UInteger = &H1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MOUSEINPUT
        Public dx As Integer
        Public dy As Integer
        Public mouseData As Integer
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KEYBDINPUT
        Public wVk As UShort
        Public wScan As UShort
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure HARDWAREINPUT
        Public uMsg As UInteger
        Public wParamL As UShort
        Public wParamH As UShort
    End Structure

    ' L'unione DEVE includere tutti e tre i membri (mi/ki/hi) con FieldOffset(0), esattamente
    ' come nella vera struct INPUT di Windows. Dichiarare solo "ki" (come era prima) rende
    ' l'unione grande quanto KEYBDINPUT (24 byte su x64) invece che quanto il membro piu'
    ' grande, MOUSEINPUT (32 byte su x64): Marshal.SizeOf(GetType(INPUT)) risultava quindi
    ' piu' piccolo del vero cbSize atteso da SendInput, con conseguente corruzione dell'heap
    ' nativo nel marshaling dell'array di INPUT.
    <StructLayout(LayoutKind.Explicit)>
    Private Structure InputUnion
        <FieldOffset(0)> Public mi As MOUSEINPUT
        <FieldOffset(0)> Public ki As KEYBDINPUT
        <FieldOffset(0)> Public hi As HARDWAREINPUT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure INPUT
        Public type As UInteger
        Public U As InputUnion
    End Structure

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SendInput(nInputs As UInteger, pInputs As INPUT(), cbSize As Integer) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetWinEventHook(eventMin As UInteger, eventMax As UInteger, hmodWinEventProc As IntPtr, lpfnWinEventProc As WinEventDelegate, idProcess As UInteger, idThread As UInteger, dwFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function UnhookWinEvent(hWinEventHook As IntPtr) As Boolean
    End Function

    Private Delegate Sub WinEventDelegate(hWinEventHook As IntPtr, eventType As UInteger, hwnd As IntPtr, idObject As Integer, idChild As Integer, dwEventThread As UInteger, dwmsEventTime As UInteger)

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function GetKeyState(vKey As Integer) As Short
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function EnumWindows(lpEnumFunc As EnumWindowsProc, lParam As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function GetWindow(hWnd As IntPtr, uCmd As UInteger) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetWindowTextLength(hWnd As IntPtr) As Integer
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", EntryPoint:="GetWindowLongPtr", SetLastError:=True)>
    Private Shared Function GetWindowLongPtr64(hWnd As IntPtr, nIndex As Integer) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function ShowWindowAsync(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function SetFocus(hWnd As IntPtr) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function SwitchToThisWindow(hWnd As IntPtr, fUnknown As Boolean) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function FindWindow(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("kernel32.dll")>
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("kernel32.dll")>
    Private Shared Function FormatMessage(dwFlags As UInteger, lpSource As IntPtr, dwMessageId As UInteger, dwLanguageId As UInteger, lpBuffer As StringBuilder, nSize As UInteger, Arguments As IntPtr) As UInteger
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MSG
        Public hwnd As IntPtr
        Public message As UInteger
        Public wParam As IntPtr
        Public lParam As IntPtr
        Public time As UInteger
        Public pt As POINT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINT
        Public x As Integer
        Public y As Integer
    End Structure

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function GetMessage(ByRef lpMsg As MSG, hwnd As IntPtr, wMsgFilterMin As UInteger, wMsgFilterMax As UInteger) As Integer
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function TranslateMessage(ByRef lpMsg As MSG) As Boolean
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll")>
    Private Shared Function DispatchMessage(ByRef lpMsg As MSG) As IntPtr
    End Function

    <SuppressUnmanagedCodeSecurity>
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function PostThreadMessage(idThread As UInteger, msg As UInteger, wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function

    Private Const WM_QUIT As UInteger = &H12UI

    Private Shared _instanceLock As New Object()
    Private Shared _instance As WGTastiera = Nothing
    Private Shared _inCreazioneIstanza As Integer = 0

    Private hookProc As LowLevelKeyboardProc = Nothing
    Private hookProcHandle As GCHandle

    Private mouseHookProc As LowLevelKeyboardProc = Nothing
    Private mouseHookProcHandle As GCHandle
    Private _mouseHookIDLong As Long = 0
    Private Property mouseHookID As IntPtr
        Get
            Return New IntPtr(Interlocked.Read(_mouseHookIDLong))
        End Get
        Set(value As IntPtr)
            Interlocked.Exchange(_mouseHookIDLong, value.ToInt64())
        End Set
    End Property
    Private _bloccoMouseAttivo As Integer = 0
    Private Const WH_MOUSE_LL As Integer = 14

    Private _hookIDLong As Long = 0
    Private Property hookID As IntPtr
        Get
            Return New IntPtr(Interlocked.Read(_hookIDLong))
        End Get
        Set(value As IntPtr)
            Interlocked.Exchange(_hookIDLong, value.ToInt64())
        End Set
    End Property

    Private _threadHook As Thread = Nothing
    Private _threadMonitor As Thread = Nothing
    Private _threadHookId As UInteger = 0
    Private _threadHookPronto As New ManualResetEventSlim(False)
    Private _riavvioHookInCorso As Integer = 0
    Private _hookDaReinstallare As Integer = 0
    Private _contatoreRiavviiHook As Integer = 0
    Private _ultimoTentativoRiavvioHookTick As Long = 0

    Private _statoCapsLock As Integer = 0
    Private _statoNumLock As Integer = 0
    Private _statoScrollLock As Integer = 0

    Private _winEventHookProc As WinEventDelegate = Nothing
    Private _winEventHookHandle As IntPtr = IntPtr.Zero
    Private _winEventHookProcHandle As GCHandle
    Private Const EVENT_SYSTEM_FOREGROUND As UInteger = &H3UI
    Private Const WINEVENT_OUTOFCONTEXT As UInteger = &H0UI
    Private _wingabrielInForeground As Integer = 1

    Private resyncTimer As System.Windows.Forms.Timer

    Private writelog As New wgcontrols.Logger()

    Private disposedValue As Boolean
    Private currentProcessId As UInteger

    Private isAltPremuto As Integer = 0
    Private isCtrlPremuto As Integer = 0
    Private isShiftPremuto As Integer = 0
    Private isLWinPremuto As Integer = 0
    Private _windowsDaSolo As Integer = 0
    Private _windowsTrattenuto As Integer = 0
    Private _windowsInviato As Integer = 0
    ' Vale 1 se a Windows e' stato rimandato finora il tasto Windows e SOLO uno Shift (Windows poi Shift,
    ' in quest'ordine): in quel caso Windows non ha ricevuto nessuna combinazione vera e Windows Tab
    ' (Shift Windows Tab) puo' ancora essere gestito da WinGabriel. Vedi il blocco Tab sotto.
    Private _windowsInviatoSoloShift As Integer = 0
    Private _tastoWindowsTrattenuto As Integer = CInt(Keys.LWin)
    Private Const TASTO_MASCHERA As Byte = &HE8
    ' Vale 1 dopo che Control Shift W e' stato rimandato a Windows: serve a rimandare anche il rilascio.
    Private _controlShiftWInviato As Integer = 0

    <DllImport("user32.dll")>
    Private Shared Sub keybd_event(bVk As Byte, bScan As Byte, dwFlags As UInteger, dwExtraInfo As UIntPtr)
    End Sub
    Private isRWinPremuto As Integer = 0
    Private isTabPremuto As Integer = 0
    Private isAltDestroPremuto As Integer = 0
    Private isCtrlSinistroPremuto As Integer = 0
    Private isCtrlDestroPremuto As Integer = 0
    Private _ctrlUsatoPerNavigazione As Integer = 0
    Private _ctrlUsatoConAltroTasto As Integer = 0
    Private _altUsatoConAltroTasto As Integer = 0
    Private _shiftUsatoConAltroTasto As Integer = 0
    Private _tabUsatoConAlt As Integer = 0
    Private _tabUsatoConCtrl As Integer = 0
    Private _tickUltimoClicUtente As Long = 0
    Private Const GRAZIA_CLIC_UTENTE_MS As Integer = 1500

    Private _guardiaFocusSospesaDaAltTab As Integer = 0
    Private _tickInizioSospensioneGuardia As Long = 0
    Private Const DURATA_MASSIMA_SOSPENSIONE_GUARDIA_MS As Integer = 8000
    Private Const GRAZIA_SOSPENSIONE_GUARDIA_MS As Integer = 700

    ' Un singolo istante in cui la nostra finestra risulta in primo piano NON basta a considerare
    ' l'utente "tornato davvero" su WinGabriel: Windows puo' generare un fotogramma transitorio in
    ' cui l'handle principale riceve per un attimo il primo piano anche mentre l'utente e' ancora
    ' sul desktop (es. durante il ripristino dopo Win+D, o mentre lo screen reader temporaneo si
    ' avvia) - vedi anche il commento su GRAZIA_SOSPENSIONE_GUARDIA_MS, pensato per il momento
    ' immediatamente successivo alla pressione del tasto. Per evitare che uno di questi lampi
    ' riporti a fuoco WinGabriel e i suoi controlli, il ritorno viene confermato solo se il primo
    ' piano resta nostro con continuita' per almeno SOGLIA_CONFERMA_RITORNO_MS: un ritorno vero e
    ' duraturo (l'utente che clicca o naviga davvero su WinGabriel e ci resta), non un istante
    ' isolato. Vedi _tickCandidatoRitorno, WinEventCallback e MonitorApplicazioniLoop.
    Private _tickCandidatoRitorno As Long = 0
    Private Const SOGLIA_CONFERMA_RITORNO_MS As Integer = 1500

    Private _ctrlSinPendenteAltGr As Integer = 0
    Private _tempoCtrlSinPendenteAltGr As UInteger = 0
    Private _altGrInCorso As Integer = 0
    Private _ctrlSinRilascioAltGrPendente As Integer = 0
    Private _tempoAttesaRilascioCtrlAltGr As UInteger = 0
    Private _altGrConCarattere As Integer = 0
    Private _tickUltimoAltGrConCarattere As Long = 0
    Private Const TOLLERANZA_ALTGR_MS As UInteger = 30
    Private Const TOLLERANZA_ALTGR_RILASCIO_MS As UInteger = 150
    Private Const FINESTRA_ALTGR_CARATTERE_MS As Integer = 800

    Private _uscitaTemporaneaAttiva As Integer = 0
    Private _screenReaderTemporaneoAvviato As Integer = 0
    Private _nomeProcessoScreenReaderTemporaneo As String = ""
    Private ReadOnly _lockScreenReaderTemporaneo As New Object()
    Private ReadOnly tastiNonModificatoriPremuti As New ConcurrentDictionary(Of Integer, Byte)()
    Private ReadOnly tastiVistiDalSistema As New ConcurrentDictionary(Of Integer, Byte)()
    ''' <summary>Tutti i tasti tenuti premuti (anche Shift) con l'istante dell'ultimo KEYDOWN: l'hook blocca i tasti, quindi GetAsyncKeyState non li vede.</summary>
    Private ReadOnly tastiTenutiConTick As New ConcurrentDictionary(Of Integer, Long)()

    Private ultimoForegroundPid As UInteger = 0

    Private indiceAltTab As Integer = 0
    Private ultimaSequenzaAltTabTick As Long = 0

    Private codaEventi As BlockingCollection(Of EventoTasto)
    Private threadConsumer As Thread

    Private formPrincipaleRegistrato As Form = Nothing
    ' Handle del form principale, aggiornato SOLO sul thread UI (costruttore del form e eventi
    ' HandleCreated/HandleDestroyed). ForegroundRealeDelProcesso, chiamato dal thread dell'hook,
    ' legge questo campo invece di Form.Handle: quella proprieta' esegue il controllo di
    ' cross-thread anche quando l'handle esiste gia', e chiamarla da un thread diverso da quello
    ' di creazione del form solleva un'InvalidOperationException ("Operazione cross-thread non
    ' valida"). Vedi ForegroundRealeDelProcesso e RegistraFormPrincipale.
    Private _formPrincipaleHandleCache As IntPtr = IntPtr.Zero
    Private ultimoFormValido As Form = Nothing
    Private ReadOnly formLock As New Object()

    Private ReadOnly handlersLock As New Object()

    Private Enum TipoEvento
        KeyDownEvento
        AltPremutoEvento
        AltRilasciatoEvento
        AltDestroPremutoEvento
        AltDestroRilasciatoEvento
        ShiftPremutoEvento
        ShiftRilasciatoEvento
        TabPremutoEvento
        ChiusuraEvento
        CtrlSinistroPremutoEvento
        CtrlSinistroRilasciatoEvento
        CtrlDestroPremutoEvento
        CtrlDestroRilasciatoEvento
        SvDecrementaVolumeEvento
        SvIncrementaVolumeEvento
        SvDecrementaVelocitaEvento
        SvIncrementaVelocitaEvento
        AltTabEvento
        AltTabFineEvento
        PlayerTracciaSuccessivaEvento
        PlayerTracciaPrecedenteEvento
        PlayerPlayPausaEvento
        PlayerTerminaEvento
        F6VisualizzaControlloEvento
        NavigaFormSuccessivoEvento
        NavigaFormPrecedenteEvento
        NavigaApplicazioneSuccessivaEvento
        NavigaApplicazionePrecedenteEvento
        ElencoApplicazioniEvento
        ChiudiApplicazioneEvento
        MenuInizialeEvento
        ScrivaniaEvento
        UltimaApplicazioneEvento
        RicercaEvento
        CentroControlloEvento
        CentroNotificheEvento
        ManualeEvento
        FermaVoceEvento
        UscitaTemporaneaEvento
        BloccoMaiuscoleEvento
        BloccoNumeriEvento
        BloccoScorrimentoEvento
    End Enum

    Private Class EventoTasto
        Public Tipo As TipoEvento
        Public Tasto As Keys
        Public Modificatori As Keys
        Public AltGr As Boolean
    End Class

    Private _modificatoriUltimoTasto As Keys = Keys.None
    Private _altGrUltimoTasto As Boolean = False
    Private _passaProssimoTasto As Integer = 0
    Private _tickRichiestaPassaggio As Long = 0
    Private _intercettazioneSospesa As Integer = 0
    Private _gestoreMusicale As IGestoreTastiMusicali

    ''' <summary>Gli strumenti musicali si registrano qui per ricevere i tasti direttamente dal gancio della tastiera; Nothing per toglierli.</summary>
    Public Sub ImpostaGestoreTastiMusicali(gestore As IGestoreTastiMusicali)
        _gestoreMusicale = gestore
    End Sub
    Private _tickSospensioneIntercettazione As Long = 0
    Private _tickUltimoEventoTastiera As Long = 0
    Private Const DURATA_MASSIMA_PASSAGGIO_TASTO_MS As Long = 15000
    Private Const FINESTRA_MODIFICATORI_TENUTI_MS As Long = 10000
    Private Const DURATA_MASSIMA_SOSPENSIONE_INTERCETTAZIONE_MS As Long = 900000

    Public Delegate Sub KeyDownEventHandler(ByRef tasto As Keys)
    Private _keyDownEvent As KeyDownEventHandler

    Public Custom Event KeyDown As KeyDownEventHandler
        AddHandler(value As KeyDownEventHandler)
            SyncLock handlersLock
                _keyDownEvent = DirectCast([Delegate].Combine(_keyDownEvent, value), KeyDownEventHandler)
            End SyncLock
        End AddHandler
        RemoveHandler(value As KeyDownEventHandler)
            SyncLock handlersLock
                _keyDownEvent = DirectCast([Delegate].Remove(_keyDownEvent, value), KeyDownEventHandler)
            End SyncLock
        End RemoveHandler
        RaiseEvent(ByRef tasto As Keys)
            Dim listaHandler As [Delegate]() = Nothing
            SyncLock handlersLock
                If _keyDownEvent IsNot Nothing Then
                    listaHandler = _keyDownEvent.GetInvocationList()
                End If
            End SyncLock

            If listaHandler Is Nothing Then Return

            Dim daRimuovere As List(Of KeyDownEventHandler) = Nothing

            For i As Integer = listaHandler.Length - 1 To 0 Step -1
                Dim handler As KeyDownEventHandler = TryCast(listaHandler(i), KeyDownEventHandler)
                If handler Is Nothing Then Continue For

                Dim handlerStale As Boolean = False
                Try
                    Dim target As Object = handler.Target
                    If target IsNot Nothing Then
                        Dim ctrl As Control = TryCast(target, Control)
                        If ctrl IsNot Nothing AndAlso ctrl.IsDisposed Then
                            handlerStale = True
                        End If
                    End If
                Catch exStale As Exception
                    LogErroreStatico("KeyDown RaiseEvent - verifica handler stale: " & exStale.GetType().Name & " - " & exStale.Message)
                End Try

                If handlerStale Then
                    If daRimuovere Is Nothing Then daRimuovere = New List(Of KeyDownEventHandler)()
                    daRimuovere.Add(handler)
                    Continue For
                End If

                Try
                    handler.Invoke(tasto)
                Catch exDisp As ObjectDisposedException
                    If daRimuovere Is Nothing Then daRimuovere = New List(Of KeyDownEventHandler)()
                    daRimuovere.Add(handler)
                    LogErroreStatico("KeyDown handler #" & i.ToString() & " stale (Disposed), rimosso automaticamente")
                Catch ex As Exception
                    LogErroreStatico("KeyDown handler #" & i.ToString() & ": " & ex.GetType().Name & " - " & ex.Message & vbCrLf & If(ex.StackTrace, ""))
                End Try
            Next

            If daRimuovere IsNot Nothing Then
                SyncLock handlersLock
                    For Each h As KeyDownEventHandler In daRimuovere
                        Try
                            _keyDownEvent = DirectCast([Delegate].Remove(_keyDownEvent, h), KeyDownEventHandler)
                        Catch exRem As Exception
                            LogErroreStatico("KeyDown RaiseEvent - rimozione handler stale: " & exRem.GetType().Name & " - " & exRem.Message)
                        End Try
                    Next
                End SyncLock
            End If
        End RaiseEvent
    End Event

    Public Event AltPremuto As EventHandler
    Public Event AltRilasciato As EventHandler
    Public Event AltDestroPremuto As EventHandler
    Public Event AltDestroRilasciato As EventHandler
    Public Event ShiftPremuto As EventHandler
    Public Event ShiftRilasciato As EventHandler
    Public Event TabPremuto As EventHandler
    Public Event Chiusura As EventHandler
    Public Event ControlSinistroPremuto As EventHandler
    Public Event ControlSinistroRilasciato As EventHandler
    Public Event ControlDestroPremuto As EventHandler
    Public Event ControlDestroRilasciato As EventHandler

    Private Shared logStaticoLock As New Object()
    Private Shared logStaticoIstanza As wgcontrols.Logger = Nothing
    Private Shared inShutdownStatico As Integer = 0

    Private Shared Sub LogErroreStatico(messaggio As String)
        Try
            If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
            SyncLock logStaticoLock
                If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
                If logStaticoIstanza Is Nothing Then
                    Try
                        logStaticoIstanza = New wgcontrols.Logger()
                    Catch
                        Return
                    End Try
                End If
                Try
                Catch
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

    Private Sub LogErrore(contesto As String, ex As Exception)
        Try
            Dim dettaglio As String = "[WGTastiera] " & contesto & ": " & ex.GetType().Name & " - " & ex.Message
            If ex.StackTrace IsNot Nothing Then
                dettaglio &= vbCrLf & ex.StackTrace
            End If
            writelog.ScriviLog(dettaglio)
        Catch
        End Try
    End Sub

    Private Sub LogErroreWin32(contesto As String, codiceErrore As Integer)
        Try
            Dim descrizione As String = ""
            Try
                Dim buffer As New StringBuilder(512)
                Dim ret As UInteger = FormatMessage(&H1000UI Or &H200UI, IntPtr.Zero, CUInt(codiceErrore), 0UI, buffer, CUInt(buffer.Capacity), IntPtr.Zero)
                If ret > 0 Then
                    descrizione = buffer.ToString(0, CInt(ret)).Trim()
                End If
            Catch
            End Try
            writelog.ScriviLog("[WGTastiera] " & contesto & ": codice errore " & codiceErrore & If(String.IsNullOrEmpty(descrizione), "", " - " & descrizione))
        Catch
        End Try
    End Sub

    Private Shared _loggingVerboso As Integer = 0

    Public Shared Property LoggingVerboso As Boolean
        Get
            Return Interlocked.CompareExchange(_loggingVerboso, 0, 0) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_loggingVerboso, If(value, 1, 0))
        End Set
    End Property

    Private Sub LogVerbose(messaggio As String)
        Try
            If Interlocked.CompareExchange(_loggingVerboso, 0, 0) <> 1 Then Return
            If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
            writelog.ScriviLog("[WGTastiera] " & messaggio)
        Catch
        End Try
    End Sub

    Private Shared Function OttieniTick() As Long
        Return Stopwatch.GetTimestamp()
    End Function

    Private Shared Function MillisecondiTrascorsi(tickInizio As Long, tickFine As Long) As Long
        Return CLng((tickFine - tickInizio) * 1000L / Stopwatch.Frequency)
    End Function

    Private Sub New()
        Try
            hookProc = New LowLevelKeyboardProc(AddressOf HookCallback)
            hookProcHandle = GCHandle.Alloc(hookProc)
            mouseHookProc = New LowLevelKeyboardProc(AddressOf MouseHookCallback)
            mouseHookProcHandle = GCHandle.Alloc(mouseHookProc)

            Try
                currentProcessId = CUInt(Process.GetCurrentProcess().Id)
            Catch ex As Exception
                LogErrore("New - GetCurrentProcessId", ex)
                currentProcessId = 0
            End Try

            codaEventi = New BlockingCollection(Of EventoTasto)(New ConcurrentQueue(Of EventoTasto)())

            threadConsumer = New Thread(AddressOf ConsumerLoop)
            threadConsumer.IsBackground = True
            threadConsumer.Name = "WGTastieraConsumer"
            threadConsumer.Start()

            _threadHook = New Thread(AddressOf ThreadHookLoop)
            _threadHook.IsBackground = True
            _threadHook.Name = "WGTastieraHook"
            _threadHook.Priority = ThreadPriority.Highest
            _threadHook.SetApartmentState(ApartmentState.STA)
            _threadHook.Start()

            _threadHookPronto.Wait(5000)

            resyncTimer = New System.Windows.Forms.Timer()
            resyncTimer.Interval = 250
            AddHandler resyncTimer.Tick, AddressOf ResyncTick
            resyncTimer.Start()

            AvviaMonitorApplicazioni()

        Catch ex As Exception
            LogErrore("New", ex)
        End Try
    End Sub

    Private Sub AvviaMonitorApplicazioni()
        Try
            Dim t As New Thread(AddressOf MonitorApplicazioniLoop)
            t.IsBackground = True
            t.Name = "WGMonitorApplicazioni"
            _threadMonitor = t
            t.Start()
        Catch ex As Exception
            LogErrore("AvviaMonitorApplicazioni", ex)
        End Try
    End Sub

    Private Sub MonitorApplicazioniLoop()
        While Not disposedValue AndAlso Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 0
            Try
                Thread.Sleep(500)
                If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Exit While
                WGContenitore.Istanza?.PulisciControlliDisposti()

                If Interlocked.CompareExchange(_guardiaFocusSospesaDaAltTab, 0, 0) = 1 Then
                    Dim inizioSospensione As Long = Interlocked.Read(_tickInizioSospensioneGuardia)
                    If inizioSospensione <> 0 AndAlso MillisecondiTrascorsi(inizioSospensione, OttieniTick()) > DURATA_MASSIMA_SOSPENSIONE_GUARDIA_MS AndAlso ForegroundRealeDelProcesso() Then
                        RiprendiGuardiaFocusSeNecessario(True)
                    End If
                End If

                If Interlocked.CompareExchange(_guardiaFocusSospesaDaAltTab, 0, 0) = 1 OrElse Interlocked.CompareExchange(_uscitaTemporaneaAttiva, 0, 0) = 1 Then
                    ' NOTA (richiesta esplicita dell'utente): qui c'era un tentativo di indovinare
                    ' se l'utente era "tornato davvero" su WinGabriel guardando quale finestra
                    ' risultava in primo piano con continuita' per un certo tempo, per poi
                    ' rimassimizzare la finestra da soli. Questa euristica si basava su
                    ' GetForegroundWindow(), che per una finestra fullscreen senza bordo puo'
                    ' comportarsi in modo incoerente (vedi i commenti storici piu' sotto), ed era
                    ' evidentemente troppo aggressiva: rimetteva su WinGabriel anche quando
                    ' l'utente era ancora sul desktop, e ripetendo l'uscita piu' volte di seguito
                    ' generava piu' cicli di CicloForzaturaUscita/AttachThreadInput sovrapposti.
                    ' Il ripristino ora avviene SOLO quando l'utente lo fa davvero, di persona
                    ' (click sulla finestra/barra applicazioni): vedi WGContenitore.OnSizeChanged,
                    ' che alla transizione reale da Minimized chiama NotificaRipristinoManualeUtente
                    ' qui sotto. Non serve piu' indovinare nulla.
                    Interlocked.Exchange(_tickCandidatoRitorno, 0)
                End If

                Try
                    If Not HookInstallato() Then
                        RiavviaHookTastiera()
                    End If
                Catch ex As Exception
                    LogErrore("MonitorApplicazioniLoop - verifica hook", ex)
                End Try
            Catch ex As Exception
                LogErrore("MonitorApplicazioniLoop", ex)
            End Try
        End While
    End Sub

    Private Sub RiavviaHookTastiera()
        Try
            If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
            If Interlocked.CompareExchange(_riavvioHookInCorso, 1, 0) <> 0 Then Return

            Try
                Dim ora As Long = OttieniTick()
                Dim ultimoTentativo As Long = Interlocked.Read(_ultimoTentativoRiavvioHookTick)
                If ultimoTentativo <> 0 AndAlso MillisecondiTrascorsi(ultimoTentativo, ora) < COOLDOWN_RIAVVIO_HOOK_MS Then Return
                Interlocked.Exchange(_ultimoTentativoRiavvioHookTick, ora)

                LogErroreStatico("RiavviaHookTastiera - hook non installato, tentativo di reinstallazione")

                Try
                    Dim vecchioTid As UInteger = Interlocked.CompareExchange(_threadHookId, 0, 0)
                    If vecchioTid <> 0 Then
                        PostThreadMessage(vecchioTid, WM_QUIT, IntPtr.Zero, IntPtr.Zero)
                    End If
                Catch ex As Exception
                    LogErrore("RiavviaHookTastiera - PostThreadMessage vecchio thread", ex)
                End Try

                Try
                    If _threadHook IsNot Nothing AndAlso _threadHook.IsAlive Then
                        _threadHook.Join(2000)
                    End If
                Catch ex As Exception
                    LogErrore("RiavviaHookTastiera - Join vecchio thread", ex)
                End Try

                Try
                    _threadHookPronto.Reset()
                    _threadHook = New Thread(AddressOf ThreadHookLoop)
                    _threadHook.IsBackground = True
                    _threadHook.Name = "WGTastieraHook"
                    _threadHook.Priority = ThreadPriority.Highest
                    _threadHook.SetApartmentState(ApartmentState.STA)
                    _threadHook.Start()
                    _threadHookPronto.Wait(5000)

                    ' Il sospetto riguardava l'hook vecchio: ora ce n'e' uno nuovo, quindi
                    ' si azzera qui e la verifica guarda solo l'esito della reinstallazione.
                    Interlocked.Exchange(_hookDaReinstallare, 0)

                    If HookInstallato() Then
                        Interlocked.Increment(_contatoreRiavviiHook)
                    Else
                        LogErroreStatico("RiavviaHookTastiera - reinstallazione non riuscita")
                    End If
                Catch ex As Exception
                    LogErrore("RiavviaHookTastiera - avvio nuovo thread", ex)
                End Try
            Finally
                Interlocked.Exchange(_riavvioHookInCorso, 0)
            End Try
        Catch ex As Exception
            LogErrore("RiavviaHookTastiera", ex)
        End Try
    End Sub

    Public Shared Function GetInstance() As WGTastiera
        SyncLock _instanceLock
            If _instance Is Nothing OrElse _instance.disposedValue Then
                ' SyncLock e' rientrante: senza questa guardia una GetInstance richiamata dal
                ' costruttore stesso ricadrebbe qui e ricomincerebbe da capo all'infinito.
                If Interlocked.CompareExchange(_inCreazioneIstanza, 1, 0) <> 0 Then Return _instance
                Try
                    Interlocked.Exchange(inShutdownStatico, 0)
                    _instance = New WGTastiera()
                Catch exCreazione As Exception
                    LogErroreStatico("GetInstance - creazione istanza: " & exCreazione.GetType().Name & " - " & exCreazione.Message)
                Finally
                    Interlocked.Exchange(_inCreazioneIstanza, 0)
                End Try
            End If
            Return _instance
        End SyncLock
    End Function

    Public Sub RegistraFormPrincipale(form As Form)
        Try
            Dim precedente As Form = Nothing
            SyncLock formLock
                precedente = formPrincipaleRegistrato
                formPrincipaleRegistrato = form
                _formPrincipaleHandleCache = IntPtr.Zero
            End SyncLock

            If precedente IsNot Nothing AndAlso Not ReferenceEquals(precedente, form) Then
                RemoveHandler precedente.HandleCreated, AddressOf OnHandleCreatedFormPrincipale
                RemoveHandler precedente.HandleDestroyed, AddressOf OnHandleDestroyedFormPrincipale
            End If

            If form IsNot Nothing Then
                ' Siamo di sicuro sul thread UI qui: la registrazione avviene nel costruttore
                ' del form stesso. Possiamo quindi leggere Handle in sicurezza, e da qui in poi
                ' aggiornarlo solo tramite eventi che WinForms solleva sempre sul thread UI.
                AddHandler form.HandleCreated, AddressOf OnHandleCreatedFormPrincipale
                AddHandler form.HandleDestroyed, AddressOf OnHandleDestroyedFormPrincipale
                If form.IsHandleCreated Then
                    SyncLock formLock
                        _formPrincipaleHandleCache = form.Handle
                    End SyncLock
                End If
            End If
        Catch ex As Exception
            LogErrore("RegistraFormPrincipale", ex)
        End Try
    End Sub

    Private Sub OnHandleCreatedFormPrincipale(sender As Object, e As EventArgs)
        Try
            Dim f As Form = TryCast(sender, Form)
            If f Is Nothing Then Return
            SyncLock formLock
                If ReferenceEquals(formPrincipaleRegistrato, f) Then
                    _formPrincipaleHandleCache = f.Handle
                End If
            End SyncLock
        Catch ex As Exception
            LogErrore("OnHandleCreatedFormPrincipale", ex)
        End Try
    End Sub

    Private Sub OnHandleDestroyedFormPrincipale(sender As Object, e As EventArgs)
        Try
            Dim f As Form = TryCast(sender, Form)
            If f Is Nothing Then Return
            SyncLock formLock
                If ReferenceEquals(formPrincipaleRegistrato, f) Then
                    _formPrincipaleHandleCache = IntPtr.Zero
                End If
            End SyncLock
        Catch ex As Exception
            LogErrore("OnHandleDestroyedFormPrincipale", ex)
        End Try
    End Sub

    ' Se un riavvio dell'hook non riesce a fermare il thread precedente entro il Join, i due thread
    ' convivono: senza copie locali il vecchio, uscendo, disinstallerebbe gli hook appena installati
    ' dal nuovo e libererebbe il GCHandle del suo delegate, con crash nativo alla notifica seguente.
    Private Sub ThreadHookLoop()
        Dim hookLocale As IntPtr = IntPtr.Zero
        Dim mouseHookLocale As IntPtr = IntPtr.Zero
        Dim winEventLocale As IntPtr = IntPtr.Zero
        Dim winEventHandleLocale As GCHandle = Nothing

        Try
            If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
            _threadHookId = GetCurrentThreadId()

            Dim baseAddr As IntPtr = IntPtr.Zero
            Try
                Using currentProcess As Process = Process.GetCurrentProcess()
                    Using mainModule As ProcessModule = currentProcess.MainModule
                        baseAddr = mainModule.BaseAddress
                    End Using
                End Using
            Catch ex As Exception
                LogErrore("ThreadHookLoop - MainModule", ex)
            End Try

            hookLocale = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, baseAddr, 0)
            hookID = hookLocale

            If hookID = IntPtr.Zero Then
                Dim err As Integer = Marshal.GetLastWin32Error()
                LogErroreWin32("ThreadHookLoop - SetWindowsHookEx fallita", err)
            Else
                LogVerbose("Hook tastiera installato, threadId=" & _threadHookId.ToString())
            End If

            mouseHookLocale = SetWindowsHookEx(WH_MOUSE_LL, mouseHookProc, baseAddr, 0)
            mouseHookID = mouseHookLocale

            If mouseHookID = IntPtr.Zero Then
                Dim errMouse As Integer = Marshal.GetLastWin32Error()
                LogErroreWin32("ThreadHookLoop - SetWindowsHookEx mouse fallita", errMouse)
            Else
                LogVerbose("Hook mouse installato")
            End If

            Try
                Dim procWinEventLocale As WinEventDelegate = New WinEventDelegate(AddressOf WinEventCallback)
                winEventHandleLocale = GCHandle.Alloc(procWinEventLocale)
                _winEventHookProc = procWinEventLocale
                _winEventHookProcHandle = winEventHandleLocale
                winEventLocale = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, procWinEventLocale, 0, 0, WINEVENT_OUTOFCONTEXT)
                _winEventHookHandle = winEventLocale
                If _winEventHookHandle = IntPtr.Zero Then
                    LogErrore("ThreadHookLoop - SetWinEventHook fallita", New Exception("handle nullo"))
                End If
            Catch ex As Exception
                LogErrore("ThreadHookLoop - installazione WinEventHook", ex)
            End Try

            Try
                Interlocked.Exchange(_statoCapsLock, If(LeggiStatoToggleReale(VK_CAPITAL), 1, 0))
                Interlocked.Exchange(_statoNumLock, If(LeggiStatoToggleReale(VK_NUMLOCK), 1, 0))
                Interlocked.Exchange(_statoScrollLock, If(LeggiStatoToggleReale(VK_SCROLL), 1, 0))
            Catch ex As Exception
                LogErrore("ThreadHookLoop - inizializzazione stato tasti di blocco", ex)
            End Try

            _threadHookPronto.Set()

            Dim msg As MSG
            Dim ret As Integer = GetMessage(msg, IntPtr.Zero, 0, 0)
            While ret <> 0 AndAlso ret <> -1
                TranslateMessage(msg)
                DispatchMessage(msg)
                ret = GetMessage(msg, IntPtr.Zero, 0, 0)
            End While

        Catch ex As Exception
            LogErrore("ThreadHookLoop", ex)
        Finally
            Try
                If winEventLocale <> IntPtr.Zero Then
                    UnhookWinEvent(winEventLocale)
                    If _winEventHookHandle = winEventLocale Then _winEventHookHandle = IntPtr.Zero
                End If
            Catch ex As Exception
                LogErrore("ThreadHookLoop - UnhookWinEvent", ex)
            End Try
            Try
                If winEventHandleLocale.IsAllocated Then
                    winEventHandleLocale.Free()
                End If
            Catch ex As Exception
                LogErrore("ThreadHookLoop - Free winEventHookProcHandle", ex)
            End Try
            If hookLocale <> IntPtr.Zero Then
                Try
                    UnhookWindowsHookEx(hookLocale)
                Catch ex As Exception
                    LogErrore("ThreadHookLoop - UnhookWindowsHookEx", ex)
                End Try
                If hookID = hookLocale Then hookID = IntPtr.Zero
            End If
            If mouseHookLocale <> IntPtr.Zero Then
                Try
                    UnhookWindowsHookEx(mouseHookLocale)
                Catch ex As Exception
                    LogErrore("ThreadHookLoop - UnhookWindowsHookEx mouse", ex)
                End Try
                If mouseHookID = mouseHookLocale Then mouseHookID = IntPtr.Zero
            End If
        End Try
    End Sub

    Private Sub WinEventCallback(hWinEventHook As IntPtr, eventType As UInteger, hwnd As IntPtr, idObject As Integer, idChild As Integer, dwEventThread As UInteger, dwmsEventTime As UInteger)
        Try
            If eventType <> EVENT_SYSTEM_FOREGROUND Then Return
            If hwnd = IntPtr.Zero Then Return

            Dim fgPid As UInteger = 0
            Try
                GetWindowThreadProcessId(hwnd, fgPid)
            Catch ex As Exception
                LogErrore("WinEventCallback - GetWindowThreadProcessId", ex)
                Return
            End Try

            Dim registrato As Form = Nothing
            Dim handleCache As IntPtr = IntPtr.Zero
            SyncLock formLock
                registrato = formPrincipaleRegistrato
                handleCache = _formPrincipaleHandleCache
            End SyncLock

            ' "Torna in primo piano" deve significare specificamente "torna in primo piano la
            ' finestra principale (WGContenitore)", non "una finestra qualunque del nostro
            ' processo". WinGabriel apre molte altre finestre nello stesso processo (posta, file
            ' manager, word processor, player...): se una di queste, o la stessa finestra
            ' principale durante un fotogramma transitorio della propria minimizzazione, riceve
            ' per un istante EVENT_SYSTEM_FOREGROUND, un confronto sul solo PID lo scambierebbe per
            ' un ritorno vero e annullerebbe subito un'uscita temporanea (es. Win+D) anche se
            ' l'utente e' ancora sul desktop. Il confronto sull'handle in cache (vedi
            ' RegistraFormPrincipale) elimina questo falso positivo. Se l'handle non e' ancora
            ' disponibile, ripieghiamo sul confronto sul PID di prima.
            Dim dentro As Boolean
            If handleCache <> IntPtr.Zero AndAlso registrato IsNot Nothing Then
                dentro = (hwnd = handleCache)
            Else
                dentro = (fgPid = currentProcessId)
            End If
            Dim altAncoraPremuto As Boolean = (Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1) OrElse (Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1)

            ' Durante il ciclo di Alt+Tab, Windows puo' portare per un istante in primo piano la
            ' finestra su cui si passa nell'anteprima, anche se l'utente sta andando verso
            ' un'altra finestra e Alt e' ancora premuto. Non e' un ritorno vero: se aggiornassimo
            ' lo stato qui perderemmo la transizione "prima=0" quando l'utente si ferma davvero
            ' su WinGabriel al rilascio di Alt.
            If dentro AndAlso altAncoraPremuto Then Return

            Dim prima As Integer = Interlocked.Exchange(_wingabrielInForeground, If(dentro, 1, 0))

            If Not dentro Then
                ' L'utente ha lasciato di nuovo la nostra finestra: un eventuale candidato di
                ' ritorno rilevato poco prima era un lampo transitorio, non un ritorno vero. Va
                ' azzerato subito, altrimenti MonitorApplicazioniLoop lo confermerebbe piu' tardi
                ' calcolando la continuita' a partire da un istante in cui l'utente non c'era gia'
                ' piu'.
                Interlocked.Exchange(_tickCandidatoRitorno, 0)
                Return
            End If

            If prima = 0 Then
                If SospensioneGuardiaInGrazia() Then
                    Interlocked.Exchange(_wingabrielInForeground, 0)
                    Return
                End If

                ' Non si riprende la guardia qui: si registra solo il primo istante in cui la
                ' nostra finestra e' tornata in primo piano. La ripresa vera e propria (e lo
                ' spegnimento dello screen reader temporaneo) avviene in MonitorApplicazioniLoop,
                ' e solo se il primo piano resta nostro con continuita' per almeno
                ' SOGLIA_CONFERMA_RITORNO_MS: vedi il commento su _tickCandidatoRitorno.
                Interlocked.CompareExchange(_tickCandidatoRitorno, OttieniTick(), 0)
            End If
        Catch ex As Exception
            LogErrore("WinEventCallback", ex)
        End Try
    End Sub

    ' Quando Windows rimuove l'hook perche' la callback ha superato il tempo massimo consentito non
    ' azzera l'handle: senza questo controllo il programma resterebbe senza tastiera per sempre,
    ' convinto che l'hook sia ancora al suo posto.
    Private Function HookInstallato() As Boolean
        ' La lettura non deve azzerare il sospetto. Consumandolo qui, se la reinstallazione
        ' non partiva davvero (cooldown ancora attivo, riavvio gia' in corso, eccezione) il
        ' segnale andava perso per sempre: Windows non azzera l'handle e il thread resta
        ' vivo, quindi al controllo successivo l'hook sembrava sano e la tastiera restava
        ' morta fino alla chiusura del programma. Il flag lo azzera solo chi reinstalla.
        If Interlocked.CompareExchange(_hookDaReinstallare, 0, 0) = 1 Then Return False
        Return hookID <> IntPtr.Zero AndAlso _threadHook IsNot Nothing AndAlso _threadHook.IsAlive
    End Function

    Private Function ForegroundRealeDelProcesso() As Boolean
        Try
            Dim fgHwnd As IntPtr = GetForegroundWindow()
            If fgHwnd = IntPtr.Zero Then Return False

            Dim formTarget As Form = TrovaFormUI()

            ' Confronto preciso sull'handle: possibile solo per il form principale registrato,
            ' il cui handle e' stato messo in cache sul thread UI (vedi RegistraFormPrincipale).
            ' NON si puo' leggere formTarget.Handle qui: questo metodo gira sul thread dell'hook
            ' tastiera/mouse, e Form.Handle esegue il controllo di cross-thread anche quando
            ' l'handle esiste gia', sollevando un'InvalidOperationException ("Operazione
            ' cross-thread non valida... accesso al controllo da un thread diverso da quello in
            ' cui e' stato creato").
            If formTarget IsNot Nothing Then
                Dim registrato As Form = Nothing
                Dim handleCache As IntPtr = IntPtr.Zero
                SyncLock formLock
                    registrato = formPrincipaleRegistrato
                    handleCache = _formPrincipaleHandleCache
                End SyncLock

                If ReferenceEquals(formTarget, registrato) AndAlso handleCache <> IntPtr.Zero Then
                    ' Vedi il commento gemello in MonitorApplicazioniLoop: una finestra gia'
                    ' minimizzata puo' restare segnalata da Windows come foreground se il
                    ' passaggio del focus al desktop dopo Win+D/Win+M e' stato negato. Non va
                    ' considerata "in primo piano" in quel caso.
                    If WGContenitore.FinestraMinimizzata Then Return False
                    Return fgHwnd = handleCache
                End If
            End If

            ' Fallback se non riusciamo a identificare la finestra principale specifica tramite
            ' la cache (form trovato per scansione, o handle non ancora disponibile): confronto
            ' sul processo, meno preciso (puo' dare falsi positivi se il processo ha altre
            ' finestre in primo piano), ma meglio di niente e non tocca mai .Handle da qui.
            Dim fgPid As UInteger = 0
            GetWindowThreadProcessId(fgHwnd, fgPid)
            Return fgPid = currentProcessId
        Catch ex As Exception
            LogErrore("ForegroundRealeDelProcesso", ex)
            Return False
        End Try
    End Function

    Public Sub AttivaBloccoMouse()
        Try
            Interlocked.Exchange(_bloccoMouseAttivo, 1)
            LogVerbose("Blocco mouse attivato")
        Catch ex As Exception
            LogErrore("AttivaBloccoMouse", ex)
        End Try
    End Sub

    Public Sub DisattivaBloccoMouse()
        Try
            Interlocked.Exchange(_bloccoMouseAttivo, 0)
            LogVerbose("Blocco mouse disattivato")
        Catch ex As Exception
            LogErrore("DisattivaBloccoMouse", ex)
        End Try
    End Sub

    Public ReadOnly Property BloccoMouseAttivo As Boolean
        Get
            Return Interlocked.CompareExchange(_bloccoMouseAttivo, 0, 0) = 1
        End Get
    End Property

    Public ReadOnly Property ContatoreRiavviiHook As Integer
        Get
            Return Interlocked.CompareExchange(_contatoreRiavviiHook, 0, 0)
        End Get
    End Property

    Public ReadOnly Property AltDestroAttualmentePremuto As Boolean
        Get
            Return Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1
        End Get
    End Property

    Public ReadOnly Property AltSinistroAttualmentePremuto As Boolean
        Get
            Return Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1
        End Get
    End Property

    ''' <summary>
    ''' Vero se il tasto (codice virtuale, per esempio Keys.Up) è tenuto premuto adesso. Vale solo mentre WinGabriel è in primo piano, perché
    ''' l'hook blocca i tasti e Windows non li registra. Con la ripetizione automatica il KEYDOWN arriva di continuo: se non ne arriva
    ''' uno da un secondo e mezzo si considera rilasciato (per esempio se il KEYUP è andato perso).
    ''' </summary>
    Public Function TastoTenuto(vk As Integer) As Boolean
        Try
            Dim tick As Long
            If Not tastiTenutiConTick.TryGetValue(vk, tick) Then Return False
            If MillisecondiTrascorsi(tick, OttieniTick()) > 1500 Then
                Dim tolto As Long
                tastiTenutiConTick.TryRemove(vk, tolto)
                Return False
            End If
            Return True
        Catch ex As Exception
            LogErrore("TastoTenuto", ex)
            Return False
        End Try
    End Function

    Public ReadOnly Property ModificatoriUltimoTasto As Keys
        Get
            Return _modificatoriUltimoTasto
        End Get
    End Property

    Public ReadOnly Property AltGrUltimoTasto As Boolean
        Get
            Return _altGrUltimoTasto
        End Get
    End Property

    Public ReadOnly Property PassaggioTastoInAttesa As Boolean
        Get
            Try
                If Interlocked.CompareExchange(_passaProssimoTasto, 0, 0) = 0 Then Return False
                Return MillisecondiTrascorsi(Interlocked.Read(_tickRichiestaPassaggio), OttieniTick()) <= DURATA_MASSIMA_PASSAGGIO_TASTO_MS
            Catch ex As Exception
                LogErrore("PassaggioTastoInAttesa", ex)
                Return False
            End Try
        End Get
    End Property

    Public Sub PassaProssimoTasto()
        Try
            Interlocked.Exchange(_tickRichiestaPassaggio, OttieniTick())
            Interlocked.Exchange(_passaProssimoTasto, 1)
        Catch ex As Exception
            LogErrore("PassaProssimoTasto", ex)
        End Try
    End Sub

    Public Sub AnnullaPassaggioTasto()
        Try
            Interlocked.Exchange(_passaProssimoTasto, 0)
        Catch ex As Exception
            LogErrore("AnnullaPassaggioTasto", ex)
        End Try
    End Sub

    Public ReadOnly Property IntercettazioneSospesa As Boolean
        Get
            Return Interlocked.CompareExchange(_intercettazioneSospesa, 0, 0) = 1
        End Get
    End Property

    Public Sub SospendiIntercettazione()
        Try
            Interlocked.Exchange(_tickSospensioneIntercettazione, OttieniTick())
            Interlocked.Exchange(_intercettazioneSospesa, 1)
        Catch ex As Exception
            LogErrore("SospendiIntercettazione", ex)
        End Try
    End Sub

    Public Sub RiprendiIntercettazione()
        Try
            Interlocked.Exchange(_intercettazioneSospesa, 0)
        Catch ex As Exception
            LogErrore("RiprendiIntercettazione", ex)
        End Try
    End Sub

    Private Function ModificatoriCorrenti() As Keys
        Dim risultato As Keys = Keys.None
        Try
            Dim altGrInCorso As Boolean = Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 1
            If Interlocked.CompareExchange(isShiftPremuto, 0, 0) = 1 Then risultato = risultato Or Keys.Shift
            Dim ctrlDestro As Boolean = Interlocked.CompareExchange(isCtrlDestroPremuto, 0, 0) = 1
            Dim ctrlSinistro As Boolean = Interlocked.CompareExchange(isCtrlSinistroPremuto, 0, 0) = 1 AndAlso Not altGrInCorso
            If ctrlDestro OrElse ctrlSinistro Then risultato = risultato Or Keys.Control
            If Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1 Then risultato = risultato Or Keys.Alt
        Catch ex As Exception
            LogErrore("ModificatoriCorrenti", ex)
        End Try
        Return risultato
    End Function

    Private Function AltGrCorrente() As Boolean
        Try
            Return Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 1 OrElse Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1
        Catch ex As Exception
            LogErrore("AltGrCorrente", ex)
            Return False
        End Try
    End Function

    ' Su tastiera italiana AltGr e' fisicamente il tasto Alt destro: la stessa pressione che
    ' apre il menu delle opzioni serve anche a comporre caratteri come la chiocciola. Quando
    ' AltGr viene usato insieme a un altro tasto per produrre un carattere non deve essere
    ' interpretato come apertura di menu, nemmeno se l'evento arriva con qualche millisecondo
    ' di ritardo rispetto al rilascio del tasto.
    Public Function AltGrUsatoPerCarattereDiRecente(Optional finestraMs As Integer = FINESTRA_ALTGR_CARATTERE_MS) As Boolean
        Try
            If finestraMs <= 0 Then finestraMs = FINESTRA_ALTGR_CARATTERE_MS

            If Interlocked.CompareExchange(_altGrConCarattere, 0, 0) = 1 Then Return True

            Dim tickUltimo As Long = Interlocked.Read(_tickUltimoAltGrConCarattere)
            If tickUltimo = 0 Then Return False

            Dim trascorsi As Long = MillisecondiTrascorsi(tickUltimo, OttieniTick())
            If trascorsi < 0 Then Return False

            Return trascorsi <= CLng(finestraMs)
        Catch ex As Exception
            LogErrore("AltGrUsatoPerCarattereDiRecente", ex)
            Return False
        End Try
    End Function

    Private Shared Function ETastoModificatore(baseKey As Keys) As Boolean
        Try
            Select Case baseKey
                Case Keys.LMenu, Keys.RMenu, Keys.Menu,
                     Keys.LControlKey, Keys.RControlKey, Keys.ControlKey,
                     Keys.LShiftKey, Keys.RShiftKey, Keys.ShiftKey,
                     Keys.LWin, Keys.RWin,
                     Keys.Capital, Keys.NumLock, Keys.Scroll
                    Return True
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            LogErroreStatico("ETastoModificatore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LeggiStatoToggleReale(vKey As Integer) As Boolean
        Try
            Return (CInt(GetKeyState(vKey)) And 1) <> 0
        Catch ex As Exception
            LogErrore("LeggiStatoToggleReale vKey=" & vKey.ToString(), ex)
            Return False
        End Try
    End Function

    Private _cacheForegroundTick As Long = 0
    Private _cacheForegroundEsito As Integer = 0

    Private Function ForegroundDelProcessoConCache() As Boolean
        Try
            Dim ora As Long = OttieniTick()
            Dim ultimoTick As Long = Interlocked.Read(_cacheForegroundTick)
            If ultimoTick <> 0 AndAlso MillisecondiTrascorsi(ultimoTick, ora) < 250 Then
                Return Interlocked.CompareExchange(_cacheForegroundEsito, 0, 0) = 1
            End If
            Dim esito As Boolean = ForegroundRealeDelProcesso()
            Interlocked.Exchange(_cacheForegroundEsito, If(esito, 1, 0))
            Interlocked.Exchange(_cacheForegroundTick, ora)
            Return esito
        Catch ex As Exception
            LogErrore("ForegroundDelProcessoConCache", ex)
            Return False
        End Try
    End Function

    Private Function MouseHookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        Try
            If nCode < HC_ACTION Then
                Return CallNextHookEx(mouseHookID, nCode, wParam, lParam)
            End If

            ' Registriamo il click a prescindere dal blocco mouse: serve solo a
            ' CambioFocusRecenteUtente per capire se un cambio di finestra e' stato voluto
            ' dall'utente. Non altera in alcun modo cosa viene fatto del messaggio.
            Dim msgMouse As Integer = wParam.ToInt32()
            If msgMouse = WM_LBUTTONDOWN OrElse msgMouse = WM_RBUTTONDOWN OrElse msgMouse = WM_MBUTTONDOWN Then
                Interlocked.Exchange(_tickUltimoClicUtente, OttieniTick())
            End If

            If Interlocked.CompareExchange(_bloccoMouseAttivo, 0, 0) = 0 Then
                Return CallNextHookEx(mouseHookID, nCode, wParam, lParam)
            End If

            If Interlocked.CompareExchange(_wingabrielInForeground, 1, 1) = 1 AndAlso ForegroundDelProcessoConCache() Then
                Return TASTO_BLOCCATO
            End If

            Return CallNextHookEx(mouseHookID, nCode, wParam, lParam)
        Catch ex As Exception
            LogErrore("MouseHookCallback", ex)
            Return CallNextHookEx(mouseHookID, nCode, wParam, lParam)
        End Try
    End Function

    Private Sub ResyncTick(sender As Object, e As EventArgs)
        Try
            Dim fgHwnd As IntPtr = GetForegroundWindow()
            Dim fgPid As UInteger = 0
            If fgHwnd <> IntPtr.Zero Then
                Try
                    GetWindowThreadProcessId(fgHwnd, fgPid)
                Catch ex As Exception
                    LogErrore("ResyncTick - GetWindowThreadProcessId", ex)
                End Try
            End If

            ' Come in WinEventCallback/MonitorApplicazioniLoop: "dentro" deve significare
            ' specificamente la finestra principale (WGContenitore), non una finestra qualunque
            ' del nostro processo, altrimenti un'altra finestra dell'app (posta, file manager,
            ' player, ecc.) che riceve per un istante il primo piano farebbe risultare
            ' _wingabrielInForeground erroneamente vero mentre l'utente e' altrove.
            Dim registratoResync As Form = Nothing
            Dim handleCacheResync As IntPtr = IntPtr.Zero
            SyncLock formLock
                registratoResync = formPrincipaleRegistrato
                handleCacheResync = _formPrincipaleHandleCache
            End SyncLock

            Dim adessoDentro As Boolean
            If handleCacheResync <> IntPtr.Zero AndAlso registratoResync IsNot Nothing Then
                adessoDentro = (fgHwnd = handleCacheResync)
            Else
                adessoDentro = (fgPid = currentProcessId)
            End If

            Dim eraFuori As Boolean = (ultimoForegroundPid <> currentProcessId)
            ultimoForegroundPid = fgPid

            Interlocked.Exchange(_wingabrielInForeground, If(adessoDentro, 1, 0))

            Dim emetti As Boolean = eraFuori AndAlso adessoDentro
            Threading.Tasks.Task.Run(Sub()
                                         Try
                                             RisincronizzaStatiModificatori(emetti)
                                         Catch ex As Exception
                                             LogErrore("ResyncTick - Task.Run RisincronizzaStatiModificatori", ex)
                                         End Try
                                     End Sub)
        Catch ex As Exception
            LogErrore("ResyncTick", ex)
        End Try
    End Sub

    Private Function TastoRealmentePremuto(vKey As Integer) As Boolean
        Try
            Dim stato As Short = GetAsyncKeyState(vKey)
            Return (CInt(stato) And &H8000) <> 0
        Catch ex As Exception
            LogErrore("TastoRealmentePremuto vKey=" & vKey.ToString(), ex)
            Return False
        End Try
    End Function

    Private Sub RisincronizzaStatiModificatori(emettiEventi As Boolean)
        Try
            Dim altReale1 As Boolean = TastoRealmentePremuto(Keys.LMenu) OrElse TastoRealmentePremuto(Keys.RMenu)
            Dim ctrlSinReale1 As Boolean = TastoRealmentePremuto(Keys.LControlKey)
            Dim ctrlDestReale1 As Boolean = TastoRealmentePremuto(Keys.RControlKey)
            Dim shiftReale1 As Boolean = TastoRealmentePremuto(Keys.LShiftKey) OrElse TastoRealmentePremuto(Keys.RShiftKey)
            Dim lwinReale1 As Boolean = TastoRealmentePremuto(Keys.LWin)
            Dim rwinReale1 As Boolean = TastoRealmentePremuto(Keys.RWin)
            Dim altDestroReale1 As Boolean = TastoRealmentePremuto(Keys.RMenu)

            Thread.Sleep(2)

            Dim altReale2 As Boolean = TastoRealmentePremuto(Keys.LMenu) OrElse TastoRealmentePremuto(Keys.RMenu)
            Dim ctrlSinReale2 As Boolean = TastoRealmentePremuto(Keys.LControlKey)
            Dim ctrlDestReale2 As Boolean = TastoRealmentePremuto(Keys.RControlKey)
            Dim shiftReale2 As Boolean = TastoRealmentePremuto(Keys.LShiftKey) OrElse TastoRealmentePremuto(Keys.RShiftKey)
            Dim lwinReale2 As Boolean = TastoRealmentePremuto(Keys.LWin)
            Dim rwinReale2 As Boolean = TastoRealmentePremuto(Keys.RWin)
            Dim altDestroReale2 As Boolean = TastoRealmentePremuto(Keys.RMenu)

            Dim tenutiInWinGabriel As Boolean = False
            Try
                tenutiInWinGabriel = Not emettiEventi AndAlso Interlocked.CompareExchange(_wingabrielInForeground, 0, 0) = 1 AndAlso
                                     MillisecondiTrascorsi(Interlocked.Read(_tickUltimoEventoTastiera), OttieniTick()) < FINESTRA_MODIFICATORI_TENUTI_MS
            Catch exTenuti As Exception
                LogErrore("RisincronizzaStatiModificatori - modificatori tenuti", exTenuti)
                tenutiInWinGabriel = False
            End Try
            Dim altTenuto As Boolean = tenutiInWinGabriel AndAlso Not altReale2 AndAlso Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1
            Dim altDestroTenuto As Boolean = tenutiInWinGabriel AndAlso Not altDestroReale2 AndAlso Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1
            Dim ctrlSinTenuto As Boolean = tenutiInWinGabriel AndAlso Not ctrlSinReale2 AndAlso Interlocked.CompareExchange(isCtrlSinistroPremuto, 0, 0) = 1
            Dim ctrlDestTenuto As Boolean = tenutiInWinGabriel AndAlso Not ctrlDestReale2 AndAlso Interlocked.CompareExchange(isCtrlDestroPremuto, 0, 0) = 1
            Dim shiftTenuto As Boolean = tenutiInWinGabriel AndAlso Not shiftReale2 AndAlso Interlocked.CompareExchange(isShiftPremuto, 0, 0) = 1

            If altReale1 = altReale2 AndAlso Not altTenuto Then
                Dim prima As Integer = Interlocked.Exchange(isAltPremuto, If(altReale2, 1, 0))
                If prima = 1 AndAlso Not altReale2 Then
                    Dim altroTastoUsatoResyncSx As Integer = Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                    If isAltDestroPremuto = 1 AndAlso altroTastoUsatoResyncSx = 1 Then
                        Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    End If
                    If emettiEventi AndAlso altroTastoUsatoResyncSx = 0 Then
                        AccodaEvento(TipoEvento.AltRilasciatoEvento, Keys.None)
                    End If
                End If
            End If

            If altDestroReale1 = altDestroReale2 AndAlso Not altDestroTenuto Then
                Dim prima As Integer = Interlocked.Exchange(isAltDestroPremuto, If(altDestroReale2, 1, 0))
                If prima = 1 AndAlso Not altDestroReale2 Then
                    Dim altroTastoUsatoResyncDx As Integer = Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                    If isAltPremuto = 1 AndAlso altroTastoUsatoResyncDx = 1 Then
                        Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    End If
                    Dim altGrConCarattereResync As Integer = Interlocked.Exchange(_altGrConCarattere, 0)
                    If altGrConCarattereResync = 1 Then
                        Interlocked.Exchange(_tickUltimoAltGrConCarattere, OttieniTick())
                    End If
                    If emettiEventi AndAlso altroTastoUsatoResyncDx = 0 AndAlso altGrConCarattereResync = 0 Then
                        AccodaEvento(TipoEvento.AltDestroRilasciatoEvento, Keys.None)
                    End If
                End If
            End If

            If altReale1 = altReale2 AndAlso altDestroReale1 = altDestroReale2 AndAlso Not altReale2 AndAlso Not altDestroReale2 AndAlso Not altTenuto AndAlso Not altDestroTenuto Then
                Interlocked.Exchange(_altUsatoConAltroTasto, 0)
            End If

            If ctrlSinReale1 = ctrlSinReale2 AndAlso Not ctrlSinTenuto Then
                If Interlocked.Exchange(isCtrlSinistroPremuto, If(ctrlSinReale2, 1, 0)) = 1 AndAlso Not ctrlSinReale2 Then
                    If emettiEventi Then
                        AccodaEvento(TipoEvento.CtrlSinistroRilasciatoEvento, Keys.None)
                    End If
                End If
            End If

            If ctrlDestReale1 = ctrlDestReale2 AndAlso Not ctrlDestTenuto Then
                If Interlocked.Exchange(isCtrlDestroPremuto, If(ctrlDestReale2, 1, 0)) = 1 AndAlso Not ctrlDestReale2 Then
                    If emettiEventi Then
                        AccodaEvento(TipoEvento.CtrlDestroRilasciatoEvento, Keys.None)
                    End If
                End If
            End If

            Dim ctrlReale2 As Boolean = ctrlSinReale2 OrElse ctrlDestReale2 OrElse ctrlSinTenuto OrElse ctrlDestTenuto
            Interlocked.Exchange(isCtrlPremuto, If(ctrlReale2, 1, 0))

            If shiftReale1 = shiftReale2 AndAlso Not shiftTenuto Then
                If Interlocked.Exchange(isShiftPremuto, If(shiftReale2, 1, 0)) = 1 AndAlso Not shiftReale2 Then
                    If emettiEventi Then
                        AccodaEvento(TipoEvento.ShiftRilasciatoEvento, Keys.None)
                    End If
                End If
            End If

            If lwinReale1 = lwinReale2 Then
                Interlocked.Exchange(isLWinPremuto, If(lwinReale2, 1, 0))
            End If
            If rwinReale1 = rwinReale2 Then
                Interlocked.Exchange(isRWinPremuto, If(rwinReale2, 1, 0))
            End If

            If Not tastiNonModificatoriPremuti.IsEmpty Then
                Dim vkTracciati As New List(Of Integer)(tastiNonModificatoriPremuti.Keys)
                For Each vkTracciato As Integer In vkTracciati
                    Try
                        If Not TastoRealmentePremuto(vkTracciato) Then
                            Dim rimosso As Byte
                            tastiNonModificatoriPremuti.TryRemove(vkTracciato, rimosso)
                        End If
                    Catch ex As Exception
                        LogErrore("RisincronizzaStatiModificatori - pulizia tasto " & vkTracciato.ToString(), ex)
                    End Try
                Next
            End If

            Try
                Interlocked.Exchange(_statoCapsLock, If(LeggiStatoToggleReale(VK_CAPITAL), 1, 0))
            Catch ex As Exception
                LogErrore("RisincronizzaStatiModificatori - CapsLock", ex)
            End Try

            Try
                Interlocked.Exchange(_statoNumLock, If(LeggiStatoToggleReale(VK_NUMLOCK), 1, 0))
            Catch ex As Exception
                LogErrore("RisincronizzaStatiModificatori - NumLock", ex)
            End Try

            Try
                Interlocked.Exchange(_statoScrollLock, If(LeggiStatoToggleReale(VK_SCROLL), 1, 0))
            Catch ex As Exception
                LogErrore("RisincronizzaStatiModificatori - ScrollLock", ex)
            End Try

        Catch ex As Exception
            LogErrore("RisincronizzaStatiModificatori", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Simula la pressione di un tasto, come se fosse premuto sulla tastiera fisica: lo usa il braille per le frecce, l'invio e
    ''' gli altri tasti di comando. Con WinGabriel in primo piano il tasto entra nella stessa coda dei tasti veri (l'hook scarta
    ''' i tasti iniettati con SendInput, quindi non basterebbe SendInput); con un'altra applicazione in primo piano va a Windows.
    ''' </summary>
    Public Sub SimulaTasto(tasto As Keys)
        Try
            If tasto = Keys.None Then Return
            If ForegroundRealeDelProcesso() Then
                AccodaEvento(TipoEvento.KeyDownEvento, tasto)
            Else
                InviaTastoAlSistema(tasto)
            End If
        Catch ex As Exception
            LogErrore("SimulaTasto", ex)
        End Try
    End Sub

    ''' <summary>Simula un tocco del tasto Alt (premuto e subito rilasciato), come quello che apre il menu delle opzioni. Lo usa il braille.</summary>
    Public Sub SimulaAlt()
        Try
            If ForegroundRealeDelProcesso() Then
                AccodaEvento(TipoEvento.AltPremutoEvento, Keys.None)
                AccodaEvento(TipoEvento.AltRilasciatoEvento, Keys.None)
            Else
                InviaTastoAlSistema(Keys.Menu)
            End If
        Catch ex As Exception
            LogErrore("SimulaAlt", ex)
        End Try
    End Sub

    ''' <summary>Passa al brano successivo o al precedente del lettore multimediale (lo usa il braille).</summary>
    Public Sub SimulaCambioBrano(avanti As Boolean)
        Try
            AccodaEvento(If(avanti, TipoEvento.PlayerTracciaSuccessivaEvento, TipoEvento.PlayerTracciaPrecedenteEvento), Keys.None)
        Catch ex As Exception
            LogErrore("SimulaCambioBrano", ex)
        End Try
    End Sub

    Private Sub InviaTastoAlSistema(tasto As Keys)
        Try
            Dim vk As Integer = CInt(tasto And Keys.KeyCode)
            Dim flagEstesi As UInteger = 0UI
            Select Case tasto And Keys.KeyCode
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Insert, Keys.Delete
                    flagEstesi = KEYEVENTF_EXTENDEDKEY
            End Select
            Dim inputs(1) As INPUT
            For i As Integer = 0 To 1
                Dim inp As New INPUT()
                inp.type = INPUT_KEYBOARD
                inp.U.ki.wVk = CUShort(vk)
                inp.U.ki.wScan = 0
                inp.U.ki.dwFlags = flagEstesi Or If(i = 1, KEYEVENTF_KEYUP, 0UI)
                inp.U.ki.time = 0
                inp.U.ki.dwExtraInfo = IntPtr.Zero
                inputs(i) = inp
            Next
            SendInput(2UI, inputs, Marshal.SizeOf(GetType(INPUT)))
        Catch ex As Exception
            LogErrore("InviaTastoAlSistema", ex)
        End Try
    End Sub

    Private Sub AccodaEvento(tipo As TipoEvento, tasto As Keys)
        Try
            If codaEventi Is Nothing Then Return
            If Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 1 Then Return
            If codaEventi.IsAddingCompleted Then Return
            Try
                Dim evento As New EventoTasto With {.Tipo = tipo, .Tasto = tasto}
                If tipo = TipoEvento.KeyDownEvento Then
                    evento.Modificatori = ModificatoriCorrenti()
                    evento.AltGr = AltGrCorrente()
                End If
                codaEventi.Add(evento)
                LogVerbose("Accodato evento " & tipo.ToString() & If(tasto <> Keys.None, " tasto=" & tasto.ToString(), ""))
                If tipo = TipoEvento.KeyDownEvento AndAlso LoggingVerboso Then
                    LogVerbose("KeyDown altGr=" & evento.AltGr.ToString() & " modificatori=" & evento.Modificatori.ToString())
                End If
            Catch exInv As InvalidOperationException
                LogErrore("AccodaEvento - coda completata tipo=" & tipo.ToString(), exInv)
            Catch exObj As ObjectDisposedException
                LogErrore("AccodaEvento - coda disposta tipo=" & tipo.ToString(), exObj)
            End Try
        Catch ex As Exception
            LogErrore("AccodaEvento tipo=" & tipo.ToString(), ex)
        End Try
    End Sub

    Private Sub ConsumerLoop()
        Try
            For Each ev As EventoTasto In codaEventi.GetConsumingEnumerable()
                Try
                    InvocaEventoUI(ev)
                Catch ex As Exception
                    LogErrore("ConsumerLoop - InvocaEventoUI", ex)
                End Try
            Next
        Catch ex As Exception
            LogErrore("ConsumerLoop", ex)
        Finally
            ' Il Finally gira su un thread di background: una eccezione qui non e' intercettabile da
            ' nessuno e chiuderebbe il processo. La pausa evita inoltre il ciclo di riavvii a vuoto
            ' se la coda e' gia' stata smaltita.
            Try
                If Not disposedValue AndAlso Interlocked.CompareExchange(inShutdownStatico, 0, 0) = 0 AndAlso
                   codaEventi IsNot Nothing AndAlso Not codaEventi.IsAddingCompleted Then
                    Thread.Sleep(200)
                    Dim nuovoThread As New Thread(AddressOf ConsumerLoop)
                    nuovoThread.IsBackground = True
                    nuovoThread.Name = "WGTastieraConsumer"
                    nuovoThread.Start()
                    threadConsumer = nuovoThread
                End If
            Catch ex As Exception
                LogErrore("ConsumerLoop - riavvio thread fallito", ex)
            End Try
        End Try
    End Sub

    Private Sub InvocaEventoUI(ev As EventoTasto)
        Try
            Dim formTarget As Form = Nothing
            Try
                formTarget = TrovaFormUI()
            Catch ex As Exception
                LogErrore("InvocaEventoUI - TrovaFormUI", ex)
            End Try

            If formTarget IsNot Nothing Then
                Try
                    If formTarget.InvokeRequired Then
                        Try
                            formTarget.BeginInvoke(New MethodInvoker(Sub()
                                                                         Try
                                                                             EmettiEvento(ev)
                                                                         Catch ex As Exception
                                                                             LogErrore("InvocaEventoUI - BeginInvoke callback", ex)
                                                                         End Try
                                                                     End Sub))
                        Catch exInvalid As InvalidOperationException
                            SyncLock formLock
                                If ReferenceEquals(ultimoFormValido, formTarget) Then ultimoFormValido = Nothing
                            End SyncLock
                            EmettiEvento(ev)
                        Catch exObj As ObjectDisposedException
                            SyncLock formLock
                                If ReferenceEquals(ultimoFormValido, formTarget) Then ultimoFormValido = Nothing
                            End SyncLock
                            EmettiEvento(ev)
                        End Try
                    Else
                        EmettiEvento(ev)
                    End If
                Catch ex As Exception
                    LogErrore("InvocaEventoUI - Marshal", ex)
                    Try
                        EmettiEvento(ev)
                    Catch exFallback As Exception
                        LogErrore("InvocaEventoUI - Fallback emissione", exFallback)
                    End Try
                End Try
            Else
                EmettiEvento(ev)
            End If
        Catch ex As Exception
            LogErrore("InvocaEventoUI", ex)
        End Try
    End Sub

    Private Function TrovaFormUI() As Form
        Try
            Dim cached As Form = Nothing
            Dim registered As Form = Nothing
            SyncLock formLock
                cached = ultimoFormValido
                registered = formPrincipaleRegistrato
            End SyncLock

            If registered IsNot Nothing AndAlso FormValido(registered) Then
                Return registered
            End If

            If cached IsNot Nothing AndAlso FormValido(cached) Then
                Return cached
            End If

            Dim trovato As Form = ScansionaOpenForms()
            If trovato IsNot Nothing Then
                SyncLock formLock
                    ultimoFormValido = trovato
                End SyncLock
            End If
            Return trovato
        Catch ex As Exception
            LogErrore("TrovaFormUI", ex)
            Return Nothing
        End Try
    End Function

    Private Function FormValido(f As Form) As Boolean
        Try
            If f Is Nothing Then Return False
            If f.IsDisposed Then Return False
            If Not f.IsHandleCreated Then Return False
            Return True
        Catch ex As Exception
            LogErrore("FormValido", ex)
            Return False
        End Try
    End Function

    Private Function ScansionaOpenForms() As Form
        Try
            Dim snapshot As Form() = Nothing
            Try
                Dim openForms As FormCollection = Application.OpenForms
                If openForms Is Nothing Then Return Nothing
                Dim count As Integer = openForms.Count
                Dim temp As New List(Of Form)(count)
                For i As Integer = 0 To count - 1
                    Try
                        Dim f As Form = openForms.Item(i)
                        If f IsNot Nothing Then temp.Add(f)
                    Catch exItem As Exception
                        LogErrore("ScansionaOpenForms - item " & i.ToString(), exItem)
                    End Try
                Next
                snapshot = temp.ToArray()
            Catch ex As Exception
                LogErrore("ScansionaOpenForms - snapshot", ex)
                Return Nothing
            End Try

            If snapshot Is Nothing Then Return Nothing

            For Each f As Form In snapshot
                If FormValido(f) Then Return f
            Next

            Return Nothing
        Catch ex As Exception
            LogErrore("ScansionaOpenForms", ex)
            Return Nothing
        End Try
    End Function

    ' Legge lo stato reale del tasto di blocco dopo che Windows lo ha aggiornato e lo
    ' annuncia. La breve attesa serve perche' l'hook viene richiamato prima che il
    ' sistema abbia commutato lo stato del tasto.
    Private Sub AnnunciaStatoBlocco(attivo As Boolean, descrizione As String)
        Try
            Dim sv = wgcontrols.sintesivocale.GetInstance()
            If sv Is Nothing Then Return

            If attivo Then
                sv.LeggiTesto(descrizione & " attivato", False, "Alta", False)
            Else
                sv.LeggiTesto(descrizione & " disattivato", False, "Alta", False)
            End If
        Catch ex As Exception
            LogErrore("AnnunciaStatoBlocco " & descrizione, ex)
        End Try
    End Sub

    ' Il tasto Blocco Maiuscole (sopra la Shift) non viene intercettato dall'hook: raggiunge
    ' sempre Windows, che lo commuta normalmente (se acceso lo spegne, se spento lo accende).
    ' Qui ci limitiamo a leggere lo stato risultante e ad annunciarlo con la dicitura richiesta,
    ' distinta da quella generica usata per Bloc Num / Bloc Scorr.
    Private Sub AnnunciaStatoMaiuscole()
        Try
            Dim attivo As Boolean = Interlocked.CompareExchange(_statoCapsLock, 0, 0) = 1

            Dim sv = wgcontrols.sintesivocale.GetInstance()
            If sv Is Nothing Then Return

            If attivo Then
                sv.LeggiTesto("Maiuscole attive", False, "Alta", False)
            Else
                sv.LeggiTesto("Maiuscole non attive", False, "Alta", False)
            End If
        Catch ex As Exception
            LogErrore("AnnunciaStatoMaiuscole", ex)
        End Try
    End Sub

    Private Sub EmettiEvento(ev As EventoTasto)
        Try
            LogVerbose("Emetto evento " & ev.Tipo.ToString() & If(ev.Tasto <> Keys.None, " tasto=" & ev.Tasto.ToString(), ""))
            Select Case ev.Tipo
                Case TipoEvento.KeyDownEvento
                    _modificatoriUltimoTasto = ev.Modificatori
                    _altGrUltimoTasto = ev.AltGr
                    Dim t As Keys = ev.Tasto
                    RaiseEvent KeyDown(t)
                Case TipoEvento.AltPremutoEvento
                    RaiseEvent AltPremuto(Me, EventArgs.Empty)
                Case TipoEvento.AltRilasciatoEvento
                    RaiseEvent AltRilasciato(Me, EventArgs.Empty)
                Case TipoEvento.AltDestroPremutoEvento
                    RaiseEvent AltDestroPremuto(Me, EventArgs.Empty)
                Case TipoEvento.AltDestroRilasciatoEvento
                    RaiseEvent AltDestroRilasciato(Me, EventArgs.Empty)
                Case TipoEvento.ShiftPremutoEvento
                    RaiseEvent ShiftPremuto(Me, EventArgs.Empty)
                Case TipoEvento.ShiftRilasciatoEvento
                    RaiseEvent ShiftRilasciato(Me, EventArgs.Empty)
                Case TipoEvento.TabPremutoEvento
                    RaiseEvent TabPremuto(Me, EventArgs.Empty)
                Case TipoEvento.ChiusuraEvento
                    RaiseEvent Chiusura(Me, EventArgs.Empty)
                Case TipoEvento.CtrlSinistroPremutoEvento
                    RaiseEvent ControlSinistroPremuto(Me, EventArgs.Empty)
                Case TipoEvento.CtrlSinistroRilasciatoEvento
                    RaiseEvent ControlSinistroRilasciato(Me, EventArgs.Empty)
                Case TipoEvento.CtrlDestroPremutoEvento
                    RaiseEvent ControlDestroPremuto(Me, EventArgs.Empty)
                Case TipoEvento.CtrlDestroRilasciatoEvento
                    RaiseEvent ControlDestroRilasciato(Me, EventArgs.Empty)
                Case TipoEvento.BloccoMaiuscoleEvento
                    AnnunciaStatoMaiuscole()
                Case TipoEvento.BloccoNumeriEvento
                    AnnunciaStatoBlocco(Interlocked.CompareExchange(_statoNumLock, 0, 0) = 1, "Blocco numeri")
                Case TipoEvento.BloccoScorrimentoEvento
                    AnnunciaStatoBlocco(Interlocked.CompareExchange(_statoScrollLock, 0, 0) = 1, "Blocco scorrimento")
                Case TipoEvento.SvDecrementaVolumeEvento,
                     TipoEvento.SvIncrementaVolumeEvento,
                     TipoEvento.SvDecrementaVelocitaEvento,
                     TipoEvento.SvIncrementaVelocitaEvento
                    Try
                        Dim sv = wgcontrols.sintesivocale.GetInstance()
                        If sv IsNot Nothing Then
                            Select Case ev.Tipo
                                Case TipoEvento.SvDecrementaVolumeEvento
                                    sv.DecrementaVolume()
                                Case TipoEvento.SvIncrementaVolumeEvento
                                    sv.IncrementaVolume()
                                Case TipoEvento.SvDecrementaVelocitaEvento
                                    sv.DecrementaVelocita()
                                Case TipoEvento.SvIncrementaVelocitaEvento
                                    sv.IncrementaVelocita()
                            End Select
                        End If
                    Catch ex As Exception
                        LogErrore("EmettiEvento - sintesivocale Fn", ex)
                    End Try
                Case TipoEvento.AltTabEvento
                    Try
                        Dim restaInWinGabriel As Boolean = False
                        Try
                            If Interlocked.CompareExchange(_wingabrielInForeground, 1, 1) = 1 AndAlso ForegroundDelProcessoConCache() Then
                                Dim contenitoreAltTab As WGContenitore = WGContenitore.Istanza
                                restaInWinGabriel = True
                                If contenitoreAltTab IsNot Nothing Then contenitoreAltTab.AltTabTraApplicazioni(ev.Tasto = Keys.Shift)
                            End If
                        Catch ex As Exception
                            LogErrore("EmettiEvento - AltTab tra applicazioni", ex)
                        End Try
                        If restaInWinGabriel Then Exit Try
                        Dim svAltTab = wgcontrols.sintesivocale.GetInstance()
                        If svAltTab IsNot Nothing Then
                            Try
                                svAltTab.NotificaFocusPerso()
                            Catch ex As Exception
                                LogErrore("EmettiEvento - AltTab NotificaFocusPerso", ex)
                            End Try
                        End If
                        PortaInForegroundFinestraSuccessiva()
                        EseguiUscitaTemporanea(True)
                    Catch ex As Exception
                        LogErrore("EmettiEvento - AltTab", ex)
                    End Try
                Case TipoEvento.AltTabFineEvento
                    Try
                        WGContenitore.Istanza?.AltTabFine()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - AltTabFine", ex)
                    End Try
                Case TipoEvento.UscitaTemporaneaEvento
                    Try
                        ' EseguiEsplicitaMinimizzazioneEDesktop va lanciata per PRIMA e non deve
                        ' mai poter essere ritardata da nient'altro in questo blocco: se
                        ' NotificaFocusPerso() (scrittura sulla pipe verso NVDA, piu' sotto) si
                        ' blocca per un pipe rimasto in uno stato anomalo, bloccherebbe l'intero
                        ' thread consumatore degli eventi e la minimizzazione non partirebbe mai -
                        ' esattamente il sintomo "la finestra resta a fuoco e non si muove".
                        ' Percio' e' fuori da qualunque Try/SyncLock condiviso con le altre due
                        ' azioni e viene lanciata per prima, sincrona ma innocua (si limita a
                        ' delegare a Task.Run).
                        EseguiEsplicitaMinimizzazioneEDesktop()

                        Threading.Tasks.Task.Run(Sub()
                                                     Try
                                                         Dim svUscita = wgcontrols.sintesivocale.GetInstance()
                                                         If svUscita IsNot Nothing Then
                                                             svUscita.NotificaFocusPerso()
                                                         End If
                                                     Catch ex As Exception
                                                         LogErrore("EmettiEvento - UscitaTemporanea NotificaFocusPerso", ex)
                                                     End Try
                                                 End Sub)

                        Threading.Tasks.Task.Run(Sub()
                                                     Try
                                                         AvviaScreenReaderTemporaneo()
                                                     Catch ex As Exception
                                                         LogErrore("EmettiEvento - AvviaScreenReaderTemporaneo", ex)
                                                     End Try
                                                 End Sub)
                    Catch ex As Exception
                        LogErrore("EmettiEvento - UscitaTemporanea", ex)
                    End Try
                Case TipoEvento.PlayerTracciaSuccessivaEvento
                    Try
                        If istanzacontrollo._inEsecuzioneAudio Then istanzacontrollo.TracciaSuccessiva()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - TracciaSuccessiva", ex)
                    End Try
                Case TipoEvento.PlayerTracciaPrecedenteEvento
                    Try
                        If istanzacontrollo._inEsecuzioneAudio Then istanzacontrollo.TracciaPrecedente()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - TracciaPrecedente", ex)
                    End Try
                Case TipoEvento.PlayerPlayPausaEvento
                    Try
                        If istanzacontrollo._inEsecuzioneAudio Then istanzacontrollo.PlayPausa()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - PlayPausa", ex)
                    End Try
                Case TipoEvento.PlayerTerminaEvento
                    Try
                        If istanzacontrollo._inEsecuzioneAudio Then istanzacontrollo.TerminaAscoltoAudio()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - TerminaAscoltoAudio", ex)
                    End Try
                Case TipoEvento.F6VisualizzaControlloEvento
                    Try
                        Dim centro = wgcontrols.WGCentroDiControllo.GetInstance()
                        If centro IsNot Nothing Then centro.VisualizzaControllo()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - F6VisualizzaControllo", ex)
                    End Try
                Case TipoEvento.FermaVoceEvento
                    Try
                        Dim sv = wgcontrols.sintesivocale.GetInstance()
                        If sv IsNot Nothing Then sv.FermaVoce()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - FermaVoce", ex)
                    End Try
                Case TipoEvento.NavigaFormSuccessivoEvento
                    Try
                        WGContenitore.Istanza?.NavigaControlloSuccessivo()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - NavigaFormSuccessivo", ex)
                    End Try
                Case TipoEvento.NavigaFormPrecedenteEvento
                    Try
                        WGContenitore.Istanza?.NavigaControlloPrecedente()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - NavigaFormPrecedente", ex)
                    End Try
                Case TipoEvento.MenuInizialeEvento
                    Try
                        WGContenitore.Istanza?.CommutaMenuApplicazioni()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - MenuIniziale", ex)
                    End Try
                Case TipoEvento.ScrivaniaEvento
                    Try
                        WGScrivania.Vai()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - Scrivania", ex)
                    End Try
                Case TipoEvento.ElencoApplicazioniEvento
                    Try
                        WGContenitore.Istanza?.MostraElencoApplicazioni()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - ElencoApplicazioni", ex)
                    End Try
                Case TipoEvento.UltimaApplicazioneEvento
                    Try
                        WGContenitore.Istanza?.VaiAllUltimaApplicazione()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - UltimaApplicazione", ex)
                    End Try
                Case TipoEvento.RicercaEvento
                    Try
                        WGRicerca.Mostra()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - Ricerca", ex)
                    End Try
                Case TipoEvento.CentroControlloEvento
                    Try
                        WGCentroDiControllo.GetInstance()?.VisualizzaControllo()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - CentroControllo", ex)
                    End Try
                Case TipoEvento.CentroNotificheEvento
                    Try
                        WGContenitore.Istanza?.MostraCentroNotifiche()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - CentroNotifiche", ex)
                    End Try
                Case TipoEvento.ManualeEvento
                    Try
                        WGContenitore.ApriManuale()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - Manuale", ex)
                    End Try
                Case TipoEvento.ChiudiApplicazioneEvento
                    Try
                        WGContenitore.Istanza?.ChiudiApplicazioneCorrente()
                    Catch ex As Exception
                        LogErrore("EmettiEvento - ChiudiApplicazione", ex)
                    End Try
                Case TipoEvento.NavigaApplicazioneSuccessivaEvento, TipoEvento.NavigaApplicazionePrecedenteEvento
                    Try
                        WGContenitore.Istanza?.NavigaApplicazione(ev.Tipo = TipoEvento.NavigaApplicazioneSuccessivaEvento)
                    Catch ex As Exception
                        LogErrore("EmettiEvento - NavigaApplicazione", ex)
                    End Try
            End Select
        Catch ex As Exception
            LogErrore("EmettiEvento tipo=" & ev.Tipo.ToString(), ex)
        End Try
    End Sub

    Private Function GestisciComandiPlayer(baseKey As Keys, isKeyDown As Boolean) As IntPtr?
        Try
            If Not isKeyDown Then Return Nothing

            Dim ctrlPremuto As Boolean = (isCtrlPremuto = 1)
            Dim altPremuto As Boolean = (isAltPremuto = 1)

            If ctrlPremuto AndAlso altPremuto Then
                Select Case baseKey
                    Case Keys.F
                        AccodaEvento(TipoEvento.PlayerTracciaSuccessivaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    Case Keys.S
                        AccodaEvento(TipoEvento.PlayerTracciaPrecedenteEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    Case Keys.P
                        AccodaEvento(TipoEvento.PlayerPlayPausaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    Case Keys.T
                        AccodaEvento(TipoEvento.PlayerTerminaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                End Select
            End If

            Select Case baseKey
                Case Keys.MediaNextTrack
                    If istanzacontrollo._inEsecuzioneAudio Then
                        AccodaEvento(TipoEvento.PlayerTracciaSuccessivaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    End If
                Case Keys.MediaPreviousTrack
                    If istanzacontrollo._inEsecuzioneAudio Then
                        AccodaEvento(TipoEvento.PlayerTracciaPrecedenteEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    End If
                Case Keys.MediaPlayPause
                    If istanzacontrollo._inEsecuzioneAudio Then
                        AccodaEvento(TipoEvento.PlayerPlayPausaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    End If
                Case Keys.MediaStop
                    If istanzacontrollo._inEsecuzioneAudio Then
                        AccodaEvento(TipoEvento.PlayerTerminaEvento, Keys.None)
                        Return TASTO_BLOCCATO
                    End If
            End Select

            Return Nothing
        Catch ex As Exception
            LogErrore("GestisciComandiPlayer", ex)
            Return Nothing
        End Try
    End Function

    Private Const LLKHF_INJECTED As Integer = &H10

    Private Sub AggiornaTastiNonModificatori(vkCode As Integer, baseKey As Keys, isKeyDown As Boolean)
        Try
            If isKeyDown Then
                tastiTenutiConTick(vkCode) = OttieniTick()
            Else
                Dim tolto As Long
                tastiTenutiConTick.TryRemove(vkCode, tolto)
            End If
            Select Case baseKey
                Case Keys.LMenu, Keys.RMenu, Keys.Menu, Keys.LControlKey, Keys.RControlKey, Keys.ControlKey, Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin, Keys.Tab
                    Return
            End Select
            If isKeyDown Then
                tastiNonModificatoriPremuti.TryAdd(vkCode, 0)
            Else
                Dim rimosso As Byte
                tastiNonModificatoriPremuti.TryRemove(vkCode, rimosso)
            End If
        Catch ex As Exception
            LogErrore("AggiornaTastiNonModificatori", ex)
        End Try
    End Sub

    Private Function NessunAltroTastoAttualmentePremuto() As Boolean
        Try
            Return isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 AndAlso isShiftPremuto = 0 AndAlso
                   isLWinPremuto = 0 AndAlso isRWinPremuto = 0 AndAlso isTabPremuto = 0 AndAlso
                   tastiNonModificatoriPremuti.IsEmpty
        Catch ex As Exception
            LogErrore("NessunAltroTastoAttualmentePremuto", ex)
            Return False
        End Try
    End Function

    Private Function NessunAltroTastoOltreAltPremuto() As Boolean
        Try
            Return isShiftPremuto = 0 AndAlso isLWinPremuto = 0 AndAlso isRWinPremuto = 0 AndAlso
                   isTabPremuto = 0 AndAlso isCtrlSinistroPremuto = 0 AndAlso isCtrlDestroPremuto = 0 AndAlso
                   isCtrlPremuto = 0 AndAlso tastiNonModificatoriPremuti.IsEmpty
        Catch ex As Exception
            LogErrore("NessunAltroTastoOltreAltPremuto", ex)
            Return False
        End Try
    End Function

    Private Function TrovaPercorsoNVDAScreenReader() As String
        Try
            Dim path32 As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA", "nvda.exe")
            If System.IO.File.Exists(path32) Then Return path32
            Dim path64 As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA", "nvda.exe")
            If System.IO.File.Exists(path64) Then Return path64
            Return ""
        Catch ex As Exception
            LogErrore("TrovaPercorsoNVDAScreenReader", ex)
            Return ""
        End Try
    End Function

    Private Function TrovaPercorsoJAWSScreenReader() As String
        Try
            Dim basePath As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Freedom Scientific", "JAWS")
            If System.IO.Directory.Exists(basePath) Then
                Dim dirs() As String = System.IO.Directory.GetDirectories(basePath)
                Array.Sort(dirs)
                Array.Reverse(dirs)
                For Each d As String In dirs
                    Dim jfwPath As String = System.IO.Path.Combine(d, "jfw.exe")
                    If System.IO.File.Exists(jfwPath) Then Return jfwPath
                Next
            End If
            Return ""
        Catch ex As Exception
            LogErrore("TrovaPercorsoJAWSScreenReader", ex)
            Return ""
        End Try
    End Function

    Private Function TrovaPercorsoKsrScreenReader() As String
        Try
            Dim candidati As String() = {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KSR ScreenReader", "KSR.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KSR ScreenReader", "KSR.exe")
            }
            For Each candidato As String In candidati
                If System.IO.File.Exists(candidato) Then Return candidato
            Next
            Return ""
        Catch ex As Exception
            LogErrore("TrovaPercorsoKsrScreenReader", ex)
            Return ""
        End Try
    End Function

    Private Function TrovaPercorsoNarratorScreenReader() As String
        Try
            Dim percorso As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Narrator.exe")
            If System.IO.File.Exists(percorso) Then Return percorso
            Return ""
        Catch ex As Exception
            LogErrore("TrovaPercorsoNarratorScreenReader", ex)
            Return ""
        End Try
    End Function

    Private Function ScreenReaderGiaInEsecuzione() As Boolean
        Try
            If Process.GetProcessesByName("jfw").Length > 0 Then Return True
            If Process.GetProcessesByName("nvda").Length > 0 Then Return True
            If Process.GetProcessesByName("KSR").Length > 0 Then Return True
            If Process.GetProcessesByName("Narrator").Length > 0 Then Return True
            Return False
        Catch ex As Exception
            LogErrore("ScreenReaderGiaInEsecuzione", ex)
            Return False
        End Try
    End Function

    Private Sub AvviaScreenReaderTemporaneo()
        Try
            SyncLock _lockScreenReaderTemporaneo
                If Interlocked.CompareExchange(_screenReaderTemporaneoAvviato, 0, 0) = 1 Then Return
                If WGScreenReader.AvvioInCorso() Then
                    LogVerbose("Avvio di WinGabriel in corso: nessuno screen reader temporaneo")
                    Return
                End If
                If ScreenReaderGiaInEsecuzione() Then
                    LogVerbose("Screen reader già in esecuzione, nessun avvio temporaneo necessario")
                    Return
                End If

                Dim preferenza As String = "Automatico"
                Try
                    Dim impostazioni As New WGImpostazioniComuni()
                    If Not String.IsNullOrEmpty(impostazioni.ScreenReaderPreferito) Then
                        preferenza = impostazioni.ScreenReaderPreferito
                    End If
                Catch ex As Exception
                    LogErrore("AvviaScreenReaderTemporaneo - lettura impostazioni", ex)
                End Try

                If String.Equals(preferenza.Trim(), "WinGabriel", StringComparison.OrdinalIgnoreCase) Then Return

                Dim percorsoJaws As String = TrovaPercorsoJAWSScreenReader()
                Dim percorsoNvda As String = TrovaPercorsoNVDAScreenReader()
                Dim percorsoNarrator As String = TrovaPercorsoNarratorScreenReader()
                Dim percorsoKsr As String = TrovaPercorsoKsrScreenReader()

                Dim percorsoScelto As String = ""
                Dim nomeProcesso As String = ""

                Select Case preferenza
                    Case "KSR"
                        If Not String.IsNullOrEmpty(percorsoKsr) Then
                            percorsoScelto = percorsoKsr : nomeProcesso = "KSR"
                        ElseIf Not String.IsNullOrEmpty(percorsoNvda) Then
                            percorsoScelto = percorsoNvda : nomeProcesso = "nvda"
                        ElseIf Not String.IsNullOrEmpty(percorsoJaws) Then
                            percorsoScelto = percorsoJaws : nomeProcesso = "jfw"
                        ElseIf Not String.IsNullOrEmpty(percorsoNarrator) Then
                            percorsoScelto = percorsoNarrator : nomeProcesso = "Narrator"
                        End If
                    Case "NVDA"
                        If Not String.IsNullOrEmpty(percorsoNvda) Then
                            percorsoScelto = percorsoNvda : nomeProcesso = "nvda"
                        ElseIf Not String.IsNullOrEmpty(percorsoJaws) Then
                            percorsoScelto = percorsoJaws : nomeProcesso = "jfw"
                        ElseIf Not String.IsNullOrEmpty(percorsoNarrator) Then
                            percorsoScelto = percorsoNarrator : nomeProcesso = "Narrator"
                        End If
                    Case "Narrator"
                        If Not String.IsNullOrEmpty(percorsoNarrator) Then
                            percorsoScelto = percorsoNarrator : nomeProcesso = "Narrator"
                        ElseIf Not String.IsNullOrEmpty(percorsoJaws) Then
                            percorsoScelto = percorsoJaws : nomeProcesso = "jfw"
                        ElseIf Not String.IsNullOrEmpty(percorsoNvda) Then
                            percorsoScelto = percorsoNvda : nomeProcesso = "nvda"
                        End If
                    Case Else
                        If Not String.IsNullOrEmpty(percorsoJaws) Then
                            percorsoScelto = percorsoJaws : nomeProcesso = "jfw"
                        ElseIf Not String.IsNullOrEmpty(percorsoNvda) Then
                            percorsoScelto = percorsoNvda : nomeProcesso = "nvda"
                        ElseIf Not String.IsNullOrEmpty(percorsoKsr) Then
                            percorsoScelto = percorsoKsr : nomeProcesso = "KSR"
                        ElseIf Not String.IsNullOrEmpty(percorsoNarrator) Then
                            percorsoScelto = percorsoNarrator : nomeProcesso = "Narrator"
                        End If
                End Select

                If String.IsNullOrEmpty(percorsoScelto) Then
                    LogVerbose("Nessuno screen reader disponibile per l'avvio temporaneo")
                    Return
                End If

                Try
                    Process.Start(percorsoScelto)
                    _nomeProcessoScreenReaderTemporaneo = nomeProcesso
                    Interlocked.Exchange(_screenReaderTemporaneoAvviato, 1)
                    LogVerbose("Screen reader temporaneo avviato: " & nomeProcesso & " (" & percorsoScelto & ")")
                    LogErroreStatico("Screen reader temporaneo avviato: " & nomeProcesso & " (" & percorsoScelto & ")")
                Catch ex As Exception
                    LogErrore("AvviaScreenReaderTemporaneo - Process.Start " & percorsoScelto, ex)
                End Try
            End SyncLock
        Catch ex As Exception
            LogErrore("AvviaScreenReaderTemporaneo", ex)
        End Try
    End Sub

    Private Sub FermaScreenReaderTemporaneo()
        Try
            If Interlocked.Exchange(_screenReaderTemporaneoAvviato, 0) <> 1 Then Return

            Dim nomeProcesso As String = ""
            SyncLock _lockScreenReaderTemporaneo
                nomeProcesso = _nomeProcessoScreenReaderTemporaneo
                _nomeProcessoScreenReaderTemporaneo = ""
            End SyncLock

            If String.IsNullOrEmpty(nomeProcesso) Then Return

            If nomeProcesso = "nvda" Then
                Try
                    Dim percorsoNvda As String = TrovaPercorsoNVDAScreenReader()
                    If Not String.IsNullOrEmpty(percorsoNvda) Then
                        Dim pQuit As Process = Process.Start(percorsoNvda, "-q")
                        If pQuit IsNot Nothing Then pQuit.WaitForExit(3000)
                        Dim attesa As DateTime = DateTime.UtcNow.AddSeconds(3)
                        While Process.GetProcessesByName("nvda").Length > 0 AndAlso DateTime.UtcNow < attesa
                            Thread.Sleep(100)
                        End While
                    End If
                Catch ex As Exception
                    LogErrore("FermaScreenReaderTemporaneo - chiusura NVDA con -q", ex)
                End Try
            End If

            For Each p As Process In Process.GetProcessesByName(nomeProcesso)
                Try
                    p.Kill()
                    p.WaitForExit(3000)
                Catch ex As Exception
                    LogErrore("FermaScreenReaderTemporaneo - Kill " & nomeProcesso, ex)
                Finally
                    Try
                        p.Dispose()
                    Catch
                    End Try
                End Try
            Next
            LogVerbose("Screen reader temporaneo fermato: " & nomeProcesso)
        Catch ex As Exception
            LogErrore("FermaScreenReaderTemporaneo", ex)
        End Try
    End Sub

    Private Function SospensioneGuardiaInGrazia() As Boolean
        Try
            If Interlocked.CompareExchange(_guardiaFocusSospesaDaAltTab, 0, 0) = 0 Then Return False
            Dim inizio As Long = Interlocked.Read(_tickInizioSospensioneGuardia)
            If inizio = 0 Then Return False
            Return MillisecondiTrascorsi(inizio, OttieniTick()) < CLng(GRAZIA_SOSPENSIONE_GUARDIA_MS)
        Catch ex As Exception
            LogErrore("SospensioneGuardiaInGrazia", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se, negli ultimi istanti, c'e' un segnale concreto che il cambio della finestra
    ''' in primo piano e' stato voluto dall'utente: un click del mouse, Alt+Tab in corso, o
    ''' Win+D/Win+M (che passano gia' dalla "grazia" di SospensioneGuardiaInGrazia). In tutti
    ''' questi casi GuardiaFocusTick (WGContenitore) NON deve riportare a tutti i costi
    ''' WinGabriel in primo piano: lo fa solo quando manca uno di questi segnali, cioe'
    ''' quando una finestra esterna sembra essere comparsa da sola.
    ''' </summary>
    Public Function CambioFocusRecenteUtente() As Boolean
        Try
            If SospensioneGuardiaInGrazia() Then Return True

            If (Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1) OrElse
               (Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1) OrElse
               (Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 1) OrElse
               (Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 1) Then
                Return True
            End If

            Dim ultimoClic As Long = Interlocked.Read(_tickUltimoClicUtente)
            If ultimoClic <> 0 AndAlso MillisecondiTrascorsi(ultimoClic, OttieniTick()) < GRAZIA_CLIC_UTENTE_MS Then
                Return True
            End If

            Return False
        Catch ex As Exception
            LogErrore("CambioFocusRecenteUtente", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Vero se la finestra indicata appartiene alla shell di Windows (desktop, barra delle
    ''' applicazioni, menu Start, ricerca...). Passare a una di queste e' sempre un gesto
    ''' dell'utente (click sul desktop, apertura del menu Start...): la guardia del focus non
    ''' deve contrastarlo. Corrisponde al "diversa da Windows" della richiesta: solo una
    ''' finestra ESTERNA alla shell, di un altro programma, puo' far scattare il ri-aggancio.
    ''' </summary>
    Public Function FinestraDiSistema(hwnd As IntPtr) As Boolean
        Try
            If hwnd = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0
            GetWindowThreadProcessId(hwnd, pid)
            If pid = 0 Then Return False

            Dim nomeProcesso As String = Nothing
            Try
                Using p As Process = Process.GetProcessById(CInt(pid))
                    nomeProcesso = p.ProcessName
                End Using
            Catch
                Return False ' processo gia' terminato nel frattempo: non e' un caso interessante
            End Try

            If String.IsNullOrEmpty(nomeProcesso) Then Return False

            Return String.Equals(nomeProcesso, "explorer", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nomeProcesso, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nomeProcesso, "SearchHost", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nomeProcesso, "SearchApp", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nomeProcesso, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(nomeProcesso, "TextInputHost", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            LogErrore("FinestraDiSistema", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Unico punto che riattiva tutto dopo un'uscita (Win+D/Win+M, o il fallback del
    ''' servizio): va chiamato SOLO quando WGContenitore rileva che l'utente ha ripristinato
    ''' la finestra di persona (transizione reale da Minimized, vedi OnSizeChanged), mai da
    ''' un'euristica basata sul solo GetForegroundWindow().
    ''' </summary>
    Public Sub NotificaRipristinoManualeUtente()
        Try
            Interlocked.Exchange(_uscitaTemporaneaAttiva, 0)
            Interlocked.Exchange(_tickCandidatoRitorno, 0)
            RiprendiGuardiaFocusSeNecessario(True)
            ' In ogni caso, anche se il flag interno non risultava (piu') impostato - es. la
            ' sospensione era stata chiesta dal servizio, non da questo hook - un ripristino
            ' manuale della finestra deve sempre riportare la guardia attiva.
            WGContenitore.RiprendiGuardiaFocus()
            Try
                FermaScreenReaderTemporaneo()
            Catch exSchermo As Exception
                LogErrore("NotificaRipristinoManualeUtente - FermaScreenReaderTemporaneo", exSchermo)
            End Try
        Catch ex As Exception
            LogErrore("NotificaRipristinoManualeUtente", ex)
        End Try
    End Sub

    Private Sub SospendiGuardiaFocusSeNecessario()
        Try
            Interlocked.Exchange(_tickInizioSospensioneGuardia, OttieniTick())
            Interlocked.Exchange(_cacheForegroundTick, 0)
            If Interlocked.Exchange(_guardiaFocusSospesaDaAltTab, 1) = 0 Then
                Try
                    WGContenitore.SospendiGuardiaFocus()
                Catch ex As Exception
                    LogErrore("SospendiGuardiaFocusSeNecessario - Sospendi guardia focus", ex)
                End Try
            End If
        Catch ex As Exception
            LogErrore("SospendiGuardiaFocusSeNecessario", ex)
        End Try
    End Sub

    Private Sub RiprendiGuardiaFocusSeNecessario(notificaSintesi As Boolean)
        Try
            If Interlocked.Exchange(_guardiaFocusSospesaDaAltTab, 0) <> 1 Then Return
            Interlocked.Exchange(_tickInizioSospensioneGuardia, 0)
            Try
                WGContenitore.RiprendiGuardiaFocus()
            Catch ex As Exception
                LogErrore("RiprendiGuardiaFocusSeNecessario - Riprendi guardia focus", ex)
            End Try
            If notificaSintesi Then
                Try
                    Dim svRitorno = wgcontrols.sintesivocale.GetInstance()
                    If svRitorno IsNot Nothing Then
                        svRitorno.NotificaFocusRiacquisito()
                    End If
                Catch ex As Exception
                    LogErrore("RiprendiGuardiaFocusSeNecessario - NotificaFocusRiacquisito", ex)
                End Try
            End If
        Catch ex As Exception
            LogErrore("RiprendiGuardiaFocusSeNecessario", ex)
        End Try
    End Sub

    Private Sub EseguiUscitaTemporanea(sospendiGuardia As Boolean)
        Try
            If sospendiGuardia Then
                SospendiGuardiaFocusSeNecessario()
            End If

            If Interlocked.CompareExchange(_uscitaTemporaneaAttiva, 1, 0) <> 0 Then Return

            AccodaEvento(TipoEvento.UscitaTemporaneaEvento, Keys.None)
        Catch ex As Exception
            LogErrore("EseguiUscitaTemporanea", ex)
        End Try
    End Sub

    Private Const DURATA_MASSIMA_FORZATURA_USCITA_MS As Integer = 2500
    Private Const INTERVALLO_FORZATURA_USCITA_MS As Integer = 100
    Private Const STABILITA_RICHIESTA_USCITA_MS As Integer = 300

    Private Sub EseguiEsplicitaMinimizzazioneEDesktop()
        Try
            Dim formTarget As Form = TrovaFormUI()
            If formTarget Is Nothing Then Return
            If formTarget.IsDisposed OrElse formTarget.Disposing Then Return
            If Not formTarget.IsHandleCreated Then Return
            Threading.Tasks.Task.Run(Sub() CicloForzaturaUscita(formTarget))
        Catch ex As Exception
            LogErrore("EseguiEsplicitaMinimizzazioneEDesktop", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Un singolo tentativo di minimizzare + passare il focus al desktop si e' dimostrato
    ''' inaffidabile: a volte la finestra torna Maximized quasi subito dopo, per una causa che
    ''' non siamo riusciti a individuare con certezza nemmeno intercettando WM_SYSCOMMAND e
    ''' WM_WINDOWPOSCHANGED. Invece di continuare a rincorrere la causa esatta, qui IMPONIAMO lo
    ''' stato desiderato ripetutamente per una finestra di tempo, esattamente come gia' facciamo
    ''' in senso opposto per confermare un ritorno (vedi MonitorApplicazioniLoop): a ogni ciclo,
    ''' se la finestra non e' minimizzata la minimizziamo di nuovo, e se Win non e' piu' premuto
    ''' proviamo ad attivare il desktop; ci fermiamo solo quando lo stato resta buono con
    ''' continuita' per STABILITA_RICHIESTA_USCITA_MS, oppure scade DURATA_MASSIMA_FORZATURA_USCITA_MS,
    ''' oppure l'uscita risulta gia' conclusa (RITORNO CONFERMATO azzera _uscitaTemporaneaAttiva).
    ''' Gira su un thread dedicato (Task.Run), mai sul thread dell'hook di tastiera.
    ''' </summary>
    Private Sub CicloForzaturaUscita(formTarget As Form)
        Try
            Dim inizio As Long = OttieniTick()
            Dim statoBuonoDa As Long = 0

            Do
                If Interlocked.CompareExchange(_uscitaTemporaneaAttiva, 0, 0) <> 1 Then Return
                If formTarget.IsDisposed OrElse formTarget.Disposing Then Return
                If Not formTarget.IsHandleCreated Then Return
                If MillisecondiTrascorsi(inizio, OttieniTick()) > DURATA_MASSIMA_FORZATURA_USCITA_MS Then Return

                Dim statoOraBuono As Boolean = False
                Try
                    formTarget.Invoke(New MethodInvoker(Sub()
                                                            Try
                                                                If formTarget.IsDisposed OrElse formTarget.Disposing Then Return

                                                                If formTarget.WindowState <> FormWindowState.Minimized Then
                                                                    formTarget.WindowState = FormWindowState.Minimized
                                                                End If

                                                                ' Finche' Win risulta ancora fisicamente premuto, Windows nega i
                                                                ' cambi di foreground fatti da programmi terzi (stesso principio
                                                                ' del "foreground lock"): non proviamo nemmeno ad attivare il
                                                                ' desktop in quel caso, solo a restare minimizzati; il ciclo
                                                                ' successivo (100ms dopo) ci riprovera' da capo.
                                                                Dim winAncoraPremuto As Boolean = (Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 1) OrElse (Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 1)
                                                                If winAncoraPremuto Then Return

                                                                Dim hwndDesktop As IntPtr = FindWindow("Progman", Nothing)
                                                                If hwndDesktop = IntPtr.Zero Then Return

                                                                ' Le finestre fullscreen senza bordo (FormBorderStyle.None, come
                                                                ' questa) sono un caso noto in cui minimizzarle non sposta da sole
                                                                ' il focus altrove. PortaFinestraEsternaInPrimoPiano usa la stessa
                                                                ' tecnica gia' collaudata con cui WinGabriel riprende il proprio
                                                                ' fuoco (AllowSetForegroundWindow, e se non basta AttachThreadInput):
                                                                ' la applichiamo qui al desktop invece che a noi stessi.
                                                                WGContenitore.PortaFinestraEsternaInPrimoPiano(hwndDesktop)

                                                                statoOraBuono = (formTarget.WindowState = FormWindowState.Minimized) AndAlso (GetForegroundWindow() = hwndDesktop)
                                                            Catch ex As Exception
                                                                LogErrore("CicloForzaturaUscita - callback", ex)
                                                            End Try
                                                        End Sub))
                Catch exInvoke As Exception
                    LogErrore("CicloForzaturaUscita - Invoke", exInvoke)
                    Return
                End Try

                If statoOraBuono Then
                    If statoBuonoDa = 0 Then statoBuonoDa = OttieniTick()
                    If MillisecondiTrascorsi(statoBuonoDa, OttieniTick()) >= STABILITA_RICHIESTA_USCITA_MS Then Return
                Else
                    statoBuonoDa = 0
                End If

                Threading.Thread.Sleep(INTERVALLO_FORZATURA_USCITA_MS)
            Loop
        Catch ex As Exception
            LogErrore("CicloForzaturaUscita", ex)
        End Try
    End Sub

    Private Sub RipristinaFormPrincipale()
        Try
            Dim formTarget As Form = TrovaFormUI()
            If formTarget Is Nothing Then Return
            If formTarget.IsDisposed OrElse formTarget.Disposing Then Return
            If Not formTarget.IsHandleCreated Then Return
            Try
                formTarget.BeginInvoke(New MethodInvoker(Sub()
                                                             Try
                                                                 If formTarget.IsDisposed OrElse formTarget.Disposing Then Return
                                                                 If formTarget.WindowState = FormWindowState.Minimized Then
                                                                     formTarget.WindowState = FormWindowState.Normal
                                                                 End If
                                                             Catch ex As Exception
                                                                 LogErrore("RipristinaFormPrincipale - callback", ex)
                                                             End Try
                                                         End Sub))
            Catch ex As Exception
                LogErrore("RipristinaFormPrincipale - BeginInvoke", ex)
            End Try
        Catch ex As Exception
            LogErrore("RipristinaFormPrincipale", ex)
        End Try
    End Sub

    Private Sub GestisciRitornoInForeground()
        Try
            If Interlocked.Exchange(_uscitaTemporaneaAttiva, 0) <> 1 Then Return
            Threading.Tasks.Task.Run(Sub()
                                         Try
                                             FermaScreenReaderTemporaneo()
                                         Catch ex As Exception
                                             LogErrore("GestisciRitornoInForeground - FermaScreenReaderTemporaneo", ex)
                                         End Try
                                         Try
                                             RipristinaFormPrincipale()
                                         Catch ex As Exception
                                             LogErrore("GestisciRitornoInForeground - RipristinaFormPrincipale", ex)
                                         End Try
                                     End Sub)
        Catch ex As Exception
            LogErrore("GestisciRitornoInForeground", ex)
        End Try
    End Sub

    Private Function HookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode < HC_ACTION Then
            Return CallNextHookEx(hookID, nCode, wParam, lParam)
        End If

        Dim tickInizioCallback As Long = OttieniTick()

        Try
            Dim vkCode As Integer = Marshal.ReadInt32(lParam)

            Dim keyData As Keys = CType(vkCode, Keys)
            Dim baseKey As Keys = keyData And Keys.KeyCode
            Dim eventType As Integer = wParam.ToInt32()
            Dim flags As Integer = Marshal.ReadInt32(lParam, 8)
            Dim isTastoIniettato As Boolean = (flags And LLKHF_INJECTED) <> 0

            Dim isKeyDown As Boolean = (eventType = WM_KEYDOWN OrElse eventType = WM_SYSKEYDOWN)
            Dim isKeyUp As Boolean = (eventType = WM_KEYUP OrElse eventType = WM_SYSKEYUP)

            If Not isKeyDown AndAlso Not isKeyUp Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If isTastoIniettato Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            Interlocked.Exchange(_tickUltimoEventoTastiera, tickInizioCallback)
            AggiornaStatiInterni(baseKey, isKeyDown)
            AggiornaTastiNonModificatori(vkCode, baseKey, isKeyDown)

            ' Win+D/Win+M devono sospendere la guardia del focus SEMPRE, indipendentemente da
            ' wingabrielInForeground: quel flag e' soggetto a una cache di 250ms e puo' risultare
            ' momentaneamente disallineato. Il controllo piu' sotto su isLWinPremuto/isRWinPremuto
            ' e' raggiungibile solo quando wingabrielInForeground=True: se in quel preciso istante
            ' il flag risultasse (anche solo per un attimo) falso, Windows mostrerebbe comunque il
            ' desktop ma la guardia non verrebbe mai sospesa, e WGContenitore si riprenderebbe il
            ' focus entro mezzo secondo. Controllandolo qui, subito dopo l'aggiornamento dello
            ' stato dei modificatori e prima di qualunque filtro sul foreground, la sospensione
            ' parte sempre in tempo. EseguiUscitaTemporanea e' gia' protetta da Interlocked, quindi
            ' e' sicura da chiamare anche se il ramo piu' sotto tenta di richiamarla di nuovo.
            If isKeyDown AndAlso (baseKey = Keys.D OrElse baseKey = Keys.M) AndAlso ((Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 1) OrElse (Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 1)) Then
                EseguiUscitaTemporanea(True)
            End If

            ' Il flag da solo non basta: se resta erroneamente a 1 (WinEvent non installato, thread
            ' UI fermo) l'hook bloccherebbe i tasti di tutto il sistema anche con WinGabriel non a
            ' fuoco. Come nell'hook del mouse, l'esito va confermato con la finestra in primo piano.
            Dim wingabrielInForeground As Boolean = (Interlocked.CompareExchange(_wingabrielInForeground, 1, 1) = 1)

            If wingabrielInForeground Then
                wingabrielInForeground = ForegroundDelProcessoConCache()
                If Not wingabrielInForeground Then
                    Interlocked.Exchange(_wingabrielInForeground, 0)
                End If
            Else
                wingabrielInForeground = ForegroundRealeDelProcesso()
                If wingabrielInForeground Then
                    Interlocked.Exchange(_wingabrielInForeground, 1)
                End If
            End If

            ' NOTA: qui sotto NON si chiama piu' direttamente RiprendiGuardiaFocusSeNecessario/
            ' GestisciRitornoInForeground al primo rilevamento del nostro foreground: la conferma
            ' del ritorno (con la finestra di dwell SOGLIA_CONFERMA_RITORNO_MS) e' centralizzata in
            ' WinEventCallback (che marca il candidato) e MonitorApplicazioniLoop (che lo conferma
            ' solo se persiste). Questo ramo, raggiungibile solo con wingabrielInForeground=True,
            ' resta qui solo perche' storicamente gestiva anche la tastiera durante il ritorno; la
            ' ripresa effettiva della guardia passa sempre dal percorso centralizzato.
            If wingabrielInForeground Then
                Dim altAncoraPremutoHook As Boolean = (Interlocked.CompareExchange(isAltPremuto, 0, 0) = 1) OrElse (Interlocked.CompareExchange(isAltDestroPremuto, 0, 0) = 1)
                Dim winAncoraPremutoHook As Boolean = (Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 1) OrElse (Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 1)
                If altAncoraPremutoHook OrElse winAncoraPremutoHook Then
                    ' Alt o Win ancora premuti: siamo dentro un ciclo di Alt+Tab/Win+D non ancora
                    ' concluso, non un ritorno stabile. Azzera il candidato per non farlo maturare
                    ' nel frattempo in MonitorApplicazioniLoop.
                    Interlocked.Exchange(_tickCandidatoRitorno, 0)
                End If
            End If

            If Not wingabrielInForeground Then
                If isKeyDown Then
                    tastiVistiDalSistema.TryAdd(vkCode, 0)
                Else
                    Dim vistoFuoriFuoco As Byte
                    tastiVistiDalSistema.TryRemove(vkCode, vistoFuoriFuoco)
                End If
                If baseKey = Keys.LMenu OrElse baseKey = Keys.Tab Then
                    Interlocked.Exchange(_tabUsatoConAlt, 0)
                End If
                If isKeyUp AndAlso (baseKey = Keys.LMenu OrElse baseKey = Keys.RMenu) AndAlso isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 Then
                    Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                End If
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If Interlocked.CompareExchange(_intercettazioneSospesa, 0, 0) = 1 Then
                If MillisecondiTrascorsi(Interlocked.Read(_tickSospensioneIntercettazione), OttieniTick()) > DURATA_MASSIMA_SOSPENSIONE_INTERCETTAZIONE_MS Then
                    Interlocked.Exchange(_intercettazioneSospesa, 0)
                Else
                    If isKeyDown Then
                        tastiVistiDalSistema.TryAdd(vkCode, 0)
                    Else
                        Dim vistoDuranteSospensione As Byte
                        tastiVistiDalSistema.TryRemove(vkCode, vistoDuranteSospensione)
                    End If
                    Return CallNextHookEx(hookID, nCode, wParam, lParam)
                End If
            End If

            ' Strumenti musicali: con l'ambiente degli strumenti aperto i tasti delle note vanno diretti al motore audio, senza
            ' passare dalla coda degli eventi (così il suono parte subito) e senza arrivare a nessun'altra parte di WinGabriel.
            Dim gestoreMusicale As IGestoreTastiMusicali = _gestoreMusicale
            If gestoreMusicale IsNot Nothing Then
                Dim scanCodeMusicale As Integer = Marshal.ReadInt32(lParam, 4)
                Dim modificatoriMusicali As Boolean = isAltPremuto = 1 OrElse isAltDestroPremuto = 1 OrElse isCtrlPremuto = 1 OrElse
                                                      isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1 OrElse isLWinPremuto = 1 OrElse isRWinPremuto = 1
                If gestoreMusicale.GestisciTasto(vkCode, scanCodeMusicale, isKeyDown, isShiftPremuto = 1, modificatoriMusicali) Then Return TASTO_BLOCCATO
            End If

            ' Valvola di sicurezza: se il consumatore degli eventi si e' fermato non si possono
            ' continuare a bloccare i tasti, altrimenti la tastiera resta morta per tutto il
            ' sistema. Meglio lasciarli passare finche' la coda non torna a smaltirsi.
            If codaEventi IsNot Nothing AndAlso codaEventi.Count > 200 Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            Dim tempoEvento As UInteger = 0
            Try
                tempoEvento = CUInt(Marshal.ReadInt32(lParam, 12))
            Catch ex As Exception
                LogErrore("HookCallback - lettura tempo evento AltGr", ex)
            End Try

            If Interlocked.CompareExchange(_ctrlSinRilascioAltGrPendente, 0, 0) = 1 Then
                Dim differenzaRilascio As Long = CLng(tempoEvento) - CLng(_tempoAttesaRilascioCtrlAltGr)
                If differenzaRilascio < 0 Then differenzaRilascio = CLng(TOLLERANZA_ALTGR_RILASCIO_MS) + 1L
                If baseKey = Keys.LControlKey AndAlso isKeyUp AndAlso differenzaRilascio <= TOLLERANZA_ALTGR_RILASCIO_MS Then
                    Interlocked.Exchange(_ctrlSinRilascioAltGrPendente, 0)
                    Return TASTO_BLOCCATO
                ElseIf differenzaRilascio > TOLLERANZA_ALTGR_RILASCIO_MS Then
                    Interlocked.Exchange(_ctrlSinRilascioAltGrPendente, 0)
                End If
            End If

            If Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 1 AndAlso baseKey = Keys.LControlKey AndAlso isKeyUp Then
                Return TASTO_BLOCCATO
            End If

            If Interlocked.CompareExchange(_ctrlSinPendenteAltGr, 0, 0) = 1 Then
                Interlocked.Exchange(_ctrlSinPendenteAltGr, 0)
                Dim differenzaPressione As Long = CLng(tempoEvento) - CLng(_tempoCtrlSinPendenteAltGr)
                If differenzaPressione < 0 Then differenzaPressione = CLng(TOLLERANZA_ALTGR_MS) + 1L
                If isKeyDown AndAlso baseKey = Keys.RMenu AndAlso differenzaPressione <= TOLLERANZA_ALTGR_MS Then
                    Interlocked.Exchange(_altGrInCorso, 1)
                    Interlocked.Exchange(_altGrConCarattere, 0)
                Else
                    AccodaEvento(TipoEvento.CtrlSinistroPremutoEvento, Keys.None)
                    If isCtrlDestroPremuto = 1 OrElse Not NessunAltroTastoAttualmentePremuto() Then
                        Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
                    End If
                    If isTabPremuto = 1 Then
                        Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                        Interlocked.Exchange(_tabUsatoConCtrl, 1)
                    End If
                End If
            End If

            If isKeyDown AndAlso baseKey = Keys.LControlKey Then
                Interlocked.Exchange(_ctrlSinPendenteAltGr, 1)
                _tempoCtrlSinPendenteAltGr = tempoEvento
                Return TASTO_BLOCCATO
            End If

            If baseKey = Keys.LWin OrElse baseKey = Keys.RWin Then
                ' Con WinGabriel in primo piano il tasto Windows non arriva mai a Windows, quindi il menu Start non
                ' può aprirsi su nessun computer. Da solo porta al menu applicazioni; se durante la pressione arriva
                ' un altro tasto (Windows R, Windows E...) la combinazione viene rimandata completa a Windows, con un
                ' tasto neutro prima del rilascio perché Windows non la scambi per una pressione singola.
                If isKeyDown Then
                    If Interlocked.Exchange(_windowsTrattenuto, 1) = 0 Then
                        Interlocked.Exchange(_windowsDaSolo, 1)
                        Interlocked.Exchange(_tastoWindowsTrattenuto, vkCode)
                    End If
                    Return TASTO_BLOCCATO
                End If
                If isKeyUp Then
                    Interlocked.Exchange(_windowsTrattenuto, 0)
                    Interlocked.Exchange(_windowsInviatoSoloShift, 0)
                    Dim daSolo As Boolean = Interlocked.Exchange(_windowsDaSolo, 0) = 1
                    If Interlocked.Exchange(_windowsInviato, 0) = 1 Then
                        keybd_event(TASTO_MASCHERA, 0, 0, UIntPtr.Zero)
                        keybd_event(TASTO_MASCHERA, 0, KEYEVENTF_KEYUP, UIntPtr.Zero)
                        keybd_event(CByte(vkCode), 0, KEYEVENTF_KEYUP Or KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero)
                    End If
                    If daSolo AndAlso WGContenitore.ApplicazioniSeparate Then AccodaEvento(TipoEvento.MenuInizialeEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If (isLWinPremuto = 1) OrElse (isRWinPremuto = 1) Then
                If isKeyDown Then Interlocked.Exchange(_windowsDaSolo, 0)
                ' AGGIORNAMENTO 14/09/2026: la teoria "lasciamo fare a Explorer" (vedi la
                ' vecchia versione di questo commento, sostituita qui sotto) si e' rivelata
                ' sbagliata: i log del test delle 01:05 mostrano che, non bloccando piu' D/M,
                ' NON compare mai un OnSizeChanged a Minimized - Explorer semplicemente non
                ' minimizza questa finestra (probabile per via di FormBorderStyle.None: il
                ' "mostra desktop" nativo tende a saltare le finestre senza titlebar). Il
                ' risultato era: NVDA temporaneo partiva (quella parte non dipende da Explorer)
                ' ma il focus non si spostava mai, esattamente il sintomo "NVDA parte pero'
                ' WinGabriel resta a fuoco". Quindi torniamo a gestire noi minimizzazione e
                ' ripristino (vedi EseguiEsplicitaMinimizzazioneEDesktop, chiamata da
                ' EmettiEvento - TipoEvento.UscitaTemporaneaEvento) e blocchiamo D/M qui:
                ' inoltrarli a Explorer non ci aiuta (li ignora) e rischia solo di minimizzare
                ' finestre di ALTRE applicazioni lasciando la nostra intatta, uno stato
                ' desktop incoerente. Il rimbalzo a Maximized osservato nei log del
                ' 13/09/2026 NON viene quindi da un doppio toggle di Explorer: la causa vera
                ' resta da individuare con la diagnostica aggiuntiva in WndProc (vedi sotto,
                ' intercettazione di WM_SYSCOMMAND/WM_WINDOWPOSCHANGED in WGContenitore).
                If baseKey = Keys.D OrElse baseKey = Keys.M Then
                    Return TASTO_BLOCCATO
                End If
                ' Windows Tab e Shift Windows Tab: applicazione successiva e precedente di WinGabriel
                ' (al posto della Visualizzazione attivita' di Windows). Vale con le
                ' applicazioni separate. Si accetta anche il caso Windows poi Shift: lo Shift premuto con Windows gia'
                ' tenuto e' stato rimandato a Windows, ma da solo non e' una combinazione (vedi _windowsInviatoSoloShift).
                ' Il rilascio di Tab viene bloccato come la pressione, cosi' Windows non vede mai un Tab orfano.
                If baseKey = Keys.Tab AndAlso WGContenitore.ApplicazioniSeparate AndAlso
                   isCtrlSinistroPremuto = 0 AndAlso isCtrlDestroPremuto = 0 AndAlso isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 AndAlso
                   (Interlocked.CompareExchange(_windowsInviato, 0, 0) = 0 OrElse Interlocked.CompareExchange(_windowsInviatoSoloShift, 0, 0) = 1) Then
                    If isKeyDown Then
                        AccodaEvento(If(isShiftPremuto = 1, TipoEvento.NavigaApplicazionePrecedenteEvento, TipoEvento.NavigaApplicazioneSuccessivaEvento), Keys.None)
                    End If
                    Return TASTO_BLOCCATO
                End If
                ' Windows S: la ricerca di WinGabriel (modalità smart), al posto di quella di Windows.
                If baseKey = Keys.S AndAlso WGModalita.Smart AndAlso Interlocked.CompareExchange(_windowsInviato, 0, 0) = 0 Then
                    If isKeyDown Then AccodaEvento(TipoEvento.RicercaEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If
                ' Windows A e Windows N: centro di controllo e centro notifiche di WinGabriel, come in Windows 11.
                If (baseKey = Keys.A OrElse baseKey = Keys.N) AndAlso WGModalita.Smart AndAlso Interlocked.CompareExchange(_windowsInviato, 0, 0) = 0 Then
                    If isKeyDown Then AccodaEvento(If(baseKey = Keys.A, TipoEvento.CentroControlloEvento, TipoEvento.CentroNotificheEvento), Keys.None)
                    Return TASTO_BLOCCATO
                End If
                ' Altre combinazioni: Windows non ha visto il tasto Windows (trattenuto qui sopra), quindi gli si
                ' manda prima il tasto Windows e poi questo tasto, nell'ordine giusto.
                If isKeyDown AndAlso Interlocked.CompareExchange(_windowsTrattenuto, 0, 0) = 1 Then
                    Dim primoInvio As Boolean = (Interlocked.Exchange(_windowsInviato, 1) = 0)
                    If primoInvio Then
                        keybd_event(CByte(Interlocked.CompareExchange(_tastoWindowsTrattenuto, 0, 0)), 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero)
                    End If
                    Dim soloShift As Boolean = (baseKey = Keys.LShiftKey OrElse baseKey = Keys.RShiftKey) AndAlso
                                               (primoInvio OrElse Interlocked.CompareExchange(_windowsInviatoSoloShift, 0, 0) = 1)
                    Interlocked.Exchange(_windowsInviatoSoloShift, If(soloShift, 1, 0))
                    keybd_event(CByte(vkCode), 0, If((flags And 1) <> 0, KEYEVENTF_EXTENDEDKEY, 0UI), UIntPtr.Zero)
                    Return TASTO_BLOCCATO
                End If
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If baseKey = Keys.VolumeMute OrElse baseKey = Keys.VolumeDown OrElse baseKey = Keys.VolumeUp Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If isKeyDown AndAlso (isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1) AndAlso baseKey <> Keys.LControlKey AndAlso baseKey <> Keys.RControlKey AndAlso baseKey <> Keys.ControlKey Then
                Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
            End If

            If isKeyUp AndAlso (isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1) AndAlso baseKey <> Keys.LControlKey AndAlso baseKey <> Keys.RControlKey AndAlso baseKey <> Keys.ControlKey Then
                Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
            End If

            ' Stesso meccanismo di tracciamento usato per Ctrl, applicato anche ad Alt e Shift:
            ' se il modificatore è tenuto premuto mentre viene premuto o rilasciato un altro tasto,
            ' lo ricordiamo per evitare di trattare il successivo rilascio del modificatore come
            ' una pressione "nuda" (che altrimenti interromperebbe la sintesi vocale).
            If isKeyDown AndAlso (isAltPremuto = 1 OrElse isAltDestroPremuto = 1) AndAlso baseKey <> Keys.LMenu AndAlso baseKey <> Keys.RMenu AndAlso baseKey <> Keys.Menu Then
                Interlocked.Exchange(_altUsatoConAltroTasto, 1)
            End If

            If isKeyUp AndAlso (isAltPremuto = 1 OrElse isAltDestroPremuto = 1) AndAlso baseKey <> Keys.LMenu AndAlso baseKey <> Keys.RMenu AndAlso baseKey <> Keys.Menu Then
                Interlocked.Exchange(_altUsatoConAltroTasto, 1)
            End If

            ' AltGr (Alt destro + Ctrl sinistro sintetico) usato insieme a un tasto normale sta
            ' componendo un carattere, per esempio la chiocciola: memorizziamo l'informazione in
            ' modo indipendente dal tracciamento generico dei modificatori, cosi' il rilascio di
            ' AltGr non venga mai scambiato per la richiesta di apertura del menu delle opzioni.
            If Not ETastoModificatore(baseKey) AndAlso (Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 1 OrElse isAltDestroPremuto = 1) Then
                Interlocked.Exchange(_altGrConCarattere, 1)
                Interlocked.Exchange(_tickUltimoAltGrConCarattere, OttieniTick())
            End If

            If isKeyDown AndAlso isShiftPremuto = 1 AndAlso baseKey <> Keys.LShiftKey AndAlso baseKey <> Keys.RShiftKey Then
                Interlocked.Exchange(_shiftUsatoConAltroTasto, 1)
            End If

            If isKeyUp AndAlso isShiftPremuto = 1 AndAlso baseKey <> Keys.LShiftKey AndAlso baseKey <> Keys.RShiftKey Then
                Interlocked.Exchange(_shiftUsatoConAltroTasto, 1)
            End If

            If isKeyDown AndAlso Not ETastoModificatore(baseKey) AndAlso Interlocked.CompareExchange(_passaProssimoTasto, 0, 0) = 1 Then
                Interlocked.Exchange(_passaProssimoTasto, 0)
                If MillisecondiTrascorsi(Interlocked.Read(_tickRichiestaPassaggio), OttieniTick()) <= DURATA_MASSIMA_PASSAGGIO_TASTO_MS Then
                    AccodaEvento(TipoEvento.KeyDownEvento, keyData)
                    Return TASTO_BLOCCATO
                End If
            End If

            If isKeyDown AndAlso isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 AndAlso isCtrlSinistroPremuto = 0 AndAlso isCtrlDestroPremuto = 0 AndAlso isShiftPremuto = 0 Then
                If baseKey = Keys.F2 OrElse baseKey = Keys.F3 OrElse baseKey = Keys.F4 OrElse baseKey = Keys.F5 Then
                    Select Case baseKey
                        Case Keys.F2
                            AccodaEvento(TipoEvento.SvDecrementaVolumeEvento, Keys.None)
                        Case Keys.F3
                            AccodaEvento(TipoEvento.SvIncrementaVolumeEvento, Keys.None)
                        Case Keys.F4
                            AccodaEvento(TipoEvento.SvDecrementaVelocitaEvento, Keys.None)
                        Case Keys.F5
                            AccodaEvento(TipoEvento.SvIncrementaVelocitaEvento, Keys.None)
                    End Select
                    Return TASTO_BLOCCATO
                End If
            End If

            If isKeyDown AndAlso isAltPremuto = 1 AndAlso baseKey = Keys.Tab Then
                Interlocked.Exchange(_tabUsatoConAlt, 1)
                Return TASTO_BLOCCATO
            End If

            If isKeyDown AndAlso isTabPremuto = 1 AndAlso baseKey = Keys.LMenu Then
                Interlocked.Exchange(_tabUsatoConAlt, 1)
            End If

            If isAltPremuto = 1 AndAlso baseKey = Keys.Escape Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If (isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1) AndAlso baseKey = Keys.Escape Then
                If isKeyDown Then
                    EseguiUscitaTemporanea(True)
                End If
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            ' Control Shift W va al sistema, dove il servizio che riavvia WinGabriel lo aspetta. Control e Shift
            ' sono trattenuti qui e Windows non li ha visti: si rimandano insieme al tasto, nell'ordine giusto, e
            ' al rilascio di W si rilasciano tutti, cosi' nessun tasto resta premuto per il sistema.
            If baseKey = Keys.W Then
                If isKeyDown AndAlso isShiftPremuto = 1 AndAlso (isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1) AndAlso
                   isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 AndAlso
                   Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 0 AndAlso Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 0 Then
                    If Interlocked.Exchange(_controlShiftWInviato, 1) = 0 Then
                        keybd_event(CByte(Keys.LControlKey), 0, 0, UIntPtr.Zero)
                        keybd_event(CByte(Keys.LShiftKey), 0, 0, UIntPtr.Zero)
                    End If
                    keybd_event(CByte(Keys.W), 0, 0, UIntPtr.Zero)
                    Return TASTO_BLOCCATO
                ElseIf isKeyUp AndAlso Interlocked.Exchange(_controlShiftWInviato, 0) = 1 Then
                    keybd_event(CByte(Keys.W), 0, KEYEVENTF_KEYUP, UIntPtr.Zero)
                    keybd_event(CByte(Keys.LShiftKey), 0, KEYEVENTF_KEYUP, UIntPtr.Zero)
                    keybd_event(CByte(Keys.LControlKey), 0, KEYEVENTF_KEYUP, UIntPtr.Zero)
                    Return TASTO_BLOCCATO
                End If
            End If

            Dim risultatoPlayer As IntPtr? = GestisciComandiPlayer(baseKey, isKeyDown)
            If risultatoPlayer.HasValue Then
                Return risultatoPlayer.Value
            End If

            If baseKey = Keys.MediaNextTrack OrElse baseKey = Keys.MediaPreviousTrack OrElse
               baseKey = Keys.MediaPlayPause OrElse baseKey = Keys.MediaStop Then
                Return CallNextHookEx(hookID, nCode, wParam, lParam)
            End If

            If isKeyDown Then
                If baseKey = Keys.RControlKey Then
                    AccodaEvento(TipoEvento.CtrlDestroPremutoEvento, Keys.None)
                    If isCtrlSinistroPremuto = 1 OrElse Not NessunAltroTastoAttualmentePremuto() Then
                        Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
                    End If
                    If isTabPremuto = 1 Then
                        Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                        Interlocked.Exchange(_tabUsatoConCtrl, 1)
                    End If
                End If

                If baseKey = Keys.RMenu Then
                    If Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 0 Then
                        If isAltPremuto = 1 OrElse Not NessunAltroTastoOltreAltPremuto() Then
                            Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                        End If
                    End If
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.LMenu Then
                    If isAltDestroPremuto = 1 OrElse Not NessunAltroTastoOltreAltPremuto() Then
                        Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    End If
                    Return TASTO_BLOCCATO
                End If

                If isAltPremuto = 1 AndAlso baseKey = Keys.F4 Then
                    AccodaEvento(TipoEvento.ChiusuraEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.F6 AndAlso isCtrlSinistroPremuto = 0 AndAlso isCtrlDestroPremuto = 0 Then
                    AccodaEvento(TipoEvento.F6VisualizzaControlloEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.LShiftKey OrElse baseKey = Keys.RShiftKey Then
                    AccodaEvento(TipoEvento.ShiftPremutoEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If

                ' I tasti di blocco non vanno intercettati: devono raggiungere Windows,
                ' altrimenti lo stato non cambia mai. Si accoda solo l'annuncio dello stato
                ' che il tasto sta per assumere, cioe' l'opposto di quello attuale.
                If baseKey = Keys.Capital OrElse baseKey = Keys.NumLock OrElse baseKey = Keys.Scroll Then
                    Try
                        Select Case baseKey
                            Case Keys.Capital
                                Interlocked.Exchange(_statoCapsLock, If(Interlocked.CompareExchange(_statoCapsLock, 0, 0) = 1, 0, 1))
                                AccodaEvento(TipoEvento.BloccoMaiuscoleEvento, Keys.None)
                            Case Keys.NumLock
                                Interlocked.Exchange(_statoNumLock, If(Interlocked.CompareExchange(_statoNumLock, 0, 0) = 1, 0, 1))
                                AccodaEvento(TipoEvento.BloccoNumeriEvento, Keys.None)
                            Case Keys.Scroll
                                Interlocked.Exchange(_statoScrollLock, If(Interlocked.CompareExchange(_statoScrollLock, 0, 0) = 1, 0, 1))
                                AccodaEvento(TipoEvento.BloccoScorrimentoEvento, Keys.None)
                        End Select
                    Catch ex As Exception
                        LogErrore("HookCallback - commutazione stato tasto di blocco", ex)
                    End Try

                    Return CallNextHookEx(hookID, nCode, wParam, lParam)
                End If

                ' F10: elenco delle applicazioni. Control F4: chiusura dell'applicazione corrente.
                Dim nessunAltroModificatore As Boolean = isShiftPremuto = 0 AndAlso isAltPremuto = 0 AndAlso isAltDestroPremuto = 0 AndAlso
                    Interlocked.CompareExchange(isLWinPremuto, 0, 0) = 0 AndAlso Interlocked.CompareExchange(isRWinPremuto, 0, 0) = 0
                Dim ctrlPremuto As Boolean = isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1
                ' Control Alt D: la scrivania, come "mostra desktop" su Linux. Non con AltGr, che è Control più Alt destro.
                If baseKey = Keys.D AndAlso ctrlPremuto AndAlso isAltPremuto = 1 AndAlso isShiftPremuto = 0 AndAlso
                   Interlocked.CompareExchange(_altGrInCorso, 0, 0) = 0 AndAlso WGContenitore.ApplicazioniSeparate Then
                    Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                    Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    AccodaEvento(TipoEvento.ScrivaniaEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.F10 AndAlso Not ctrlPremuto AndAlso nessunAltroModificatore AndAlso WGContenitore.ApplicazioniSeparate Then
                    AccodaEvento(TipoEvento.ElencoApplicazioniEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If
                ' Control F10: torna all'ultima applicazione usata, come Alt Tab premuto una volta.
                If baseKey = Keys.F10 AndAlso ctrlPremuto AndAlso nessunAltroModificatore AndAlso WGContenitore.ApplicazioniSeparate Then
                    Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                    AccodaEvento(TipoEvento.UltimaApplicazioneEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If
                ' Control F1: il manuale di WinGabriel, in tutte e due le modalità.
                If baseKey = Keys.F1 AndAlso ctrlPremuto AndAlso nessunAltroModificatore AndAlso WGContenitore.ManualeDisponibile Then
                    Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                    AccodaEvento(TipoEvento.ManualeEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If
                If baseKey = Keys.F4 AndAlso ctrlPremuto AndAlso nessunAltroModificatore AndAlso WGContenitore.ApplicazioniSeparate Then
                    Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                    AccodaEvento(TipoEvento.ChiudiApplicazioneEvento, Keys.None)
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.Tab Then
                    If isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1 Then
                        Interlocked.Exchange(_ctrlUsatoPerNavigazione, 1)
                        Interlocked.Exchange(_tabUsatoConCtrl, 1)
                        If isShiftPremuto = 1 Then
                            AccodaEvento(TipoEvento.NavigaFormPrecedenteEvento, Keys.None)
                        Else
                            AccodaEvento(TipoEvento.NavigaFormSuccessivoEvento, Keys.None)
                        End If
                    Else
                        AccodaEvento(TipoEvento.TabPremutoEvento, Keys.None)
                        AccodaEvento(TipoEvento.KeyDownEvento, baseKey)
                    End If
                    Return TASTO_BLOCCATO
                End If

                AccodaEvento(TipoEvento.KeyDownEvento, keyData)
                Return TASTO_BLOCCATO
            End If

            If isKeyUp Then
                ' Se il KEYDOWN di questo tasto era stato consegnato al sistema, perche' in quel
                ' momento WinGabriel non era in primo piano, anche il KEYUP deve arrivare a Windows:
                ' altrimenti il tasto resta premuto per tutte le altre applicazioni.
                Dim vistoDalSistema As Byte
                If tastiVistiDalSistema.TryRemove(vkCode, vistoDalSistema) Then
                    Return CallNextHookEx(hookID, nCode, wParam, lParam)
                End If

                ' Anche il rilascio dei tasti di blocco deve arrivare a Windows, altrimenti
                ' il sistema resterebbe con il tasto risultante ancora premuto.
                If baseKey = Keys.Capital OrElse baseKey = Keys.NumLock OrElse baseKey = Keys.Scroll Then
                    Return CallNextHookEx(hookID, nCode, wParam, lParam)
                End If

                If baseKey = Keys.LControlKey Then
                    AccodaEvento(TipoEvento.CtrlSinistroRilasciatoEvento, Keys.None)
                    Dim statoTabCtrlL As Integer = Interlocked.CompareExchange(_tabUsatoConCtrl, 0, 0)
                    Dim navigazioneUsataL As Integer = Interlocked.Exchange(_ctrlUsatoPerNavigazione, 0)
                    Dim altroCtrlAncoraPremutoL As Boolean = (isCtrlDestroPremuto = 1)
                    Dim altroTastoUsatoL As Integer = Interlocked.Exchange(_ctrlUsatoConAltroTasto, 0)
                    If altroCtrlAncoraPremutoL AndAlso altroTastoUsatoL = 1 Then
                        Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
                    End If
                    If statoTabCtrlL = 2 Then
                        Interlocked.Exchange(_tabUsatoConCtrl, 0)
                    ElseIf navigazioneUsataL = 0 AndAlso altroTastoUsatoL = 0 AndAlso Not altroCtrlAncoraPremutoL AndAlso NessunAltroTastoAttualmentePremuto() Then
                        AccodaEvento(TipoEvento.FermaVoceEvento, Keys.None)
                    End If
                ElseIf baseKey = Keys.RControlKey Then
                    AccodaEvento(TipoEvento.CtrlDestroRilasciatoEvento, Keys.None)
                    Dim statoTabCtrlR As Integer = Interlocked.CompareExchange(_tabUsatoConCtrl, 0, 0)
                    Dim navigazioneUsataR As Integer = Interlocked.Exchange(_ctrlUsatoPerNavigazione, 0)
                    Dim altroCtrlAncoraPremutoR As Boolean = (isCtrlSinistroPremuto = 1)
                    Dim altroTastoUsatoR As Integer = Interlocked.Exchange(_ctrlUsatoConAltroTasto, 0)
                    If altroCtrlAncoraPremutoR AndAlso altroTastoUsatoR = 1 Then
                        Interlocked.Exchange(_ctrlUsatoConAltroTasto, 1)
                    End If
                    If statoTabCtrlR = 2 Then
                        Interlocked.Exchange(_tabUsatoConCtrl, 0)
                    ElseIf navigazioneUsataR = 0 AndAlso altroTastoUsatoR = 0 AndAlso Not altroCtrlAncoraPremutoR AndAlso NessunAltroTastoAttualmentePremuto() Then
                        AccodaEvento(TipoEvento.FermaVoceEvento, Keys.None)
                    End If
                End If

                If baseKey = Keys.RMenu Then
                    Dim altroTastoUsatoAltDx As Integer = Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                    If isAltPremuto = 1 AndAlso altroTastoUsatoAltDx = 1 Then
                        Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    End If
                    Dim altGrConCarattereAlRilascio As Integer = Interlocked.Exchange(_altGrConCarattere, 0)
                    If altGrConCarattereAlRilascio = 1 Then
                        Interlocked.Exchange(_tickUltimoAltGrConCarattere, OttieniTick())
                    End If
                    If altroTastoUsatoAltDx = 0 AndAlso altGrConCarattereAlRilascio = 0 Then
                        AccodaEvento(TipoEvento.AltDestroPremutoEvento, Keys.None)
                        AccodaEvento(TipoEvento.AltDestroRilasciatoEvento, Keys.None)
                    End If
                    If Interlocked.Exchange(_altGrInCorso, 0) = 1 Then
                        Interlocked.Exchange(_ctrlSinRilascioAltGrPendente, 1)
                        _tempoAttesaRilascioCtrlAltGr = tempoEvento
                    End If
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.Tab Then
                    Dim statoTabAltAlKeyUp As Integer = Interlocked.CompareExchange(_tabUsatoConAlt, 0, 0)
                    If statoTabAltAlKeyUp = 1 Then
                        AccodaEvento(TipoEvento.AltTabEvento, If(isShiftPremuto = 1, Keys.Shift, Keys.None))
                        Interlocked.Exchange(_tabUsatoConAlt, 2)
                    ElseIf statoTabAltAlKeyUp <> 2 Then
                        Interlocked.Exchange(_tabUsatoConAlt, 0)
                    End If
                    If isCtrlSinistroPremuto = 1 OrElse isCtrlDestroPremuto = 1 Then
                        Interlocked.Exchange(_tabUsatoConCtrl, 2)
                    Else
                        Interlocked.Exchange(_tabUsatoConCtrl, 0)
                    End If
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.LMenu Then
                    Dim statoTabAlt As Integer = Interlocked.CompareExchange(_tabUsatoConAlt, 0, 0)
                    If statoTabAlt = 2 Then
                        AccodaEvento(TipoEvento.AltTabFineEvento, Keys.None)
                        Interlocked.Exchange(_tabUsatoConAlt, 0)
                        Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                        Return TASTO_BLOCCATO
                    End If
                    If statoTabAlt = 1 Then
                        If isTabPremuto = 1 Then
                            AccodaEvento(TipoEvento.AltTabEvento, If(isShiftPremuto = 1, Keys.Shift, Keys.None))
                        End If
                        AccodaEvento(TipoEvento.AltTabFineEvento, Keys.None)
                        Interlocked.Exchange(_tabUsatoConAlt, 0)
                        Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                        Return TASTO_BLOCCATO
                    End If
                    Dim altroTastoUsatoAltSx As Integer = Interlocked.Exchange(_altUsatoConAltroTasto, 0)
                    If isAltDestroPremuto = 1 AndAlso altroTastoUsatoAltSx = 1 Then
                        Interlocked.Exchange(_altUsatoConAltroTasto, 1)
                    End If
                    If altroTastoUsatoAltSx = 0 Then
                        AccodaEvento(TipoEvento.AltPremutoEvento, Keys.None)
                        AccodaEvento(TipoEvento.AltRilasciatoEvento, Keys.None)
                    End If
                    Return TASTO_BLOCCATO
                End If

                If baseKey = Keys.LShiftKey OrElse baseKey = Keys.RShiftKey Then
                    Interlocked.Exchange(_shiftUsatoConAltroTasto, 0)
                    AccodaEvento(TipoEvento.ShiftRilasciatoEvento, Keys.None)
                End If
                Return TASTO_BLOCCATO
            End If

        Catch ex As Exception
            LogErrore("HookCallback", ex)
        Finally
            Try
                Dim durataMs As Long = MillisecondiTrascorsi(tickInizioCallback, OttieniTick())
                If durataMs >= SOGLIA_DISINSTALLAZIONE_HOOK_MS Then
                    Interlocked.Exchange(_hookDaReinstallare, 1)
                End If
            Catch exDurata As Exception
                LogErrore("HookCallback - verifica durata", exDurata)
            End Try
        End Try

        Return CallNextHookEx(hookID, nCode, wParam, lParam)
    End Function

    Private Sub AggiornaStatiInterni(baseKey As Keys, isKeyDown As Boolean)
        Try
            Dim valore As Integer = If(isKeyDown, 1, 0)

            Select Case baseKey
                Case Keys.LMenu
                    Interlocked.Exchange(isAltPremuto, valore)
                Case Keys.RMenu
                    Interlocked.Exchange(isAltDestroPremuto, valore)
                Case Keys.LControlKey
                    Interlocked.Exchange(isCtrlSinistroPremuto, valore)
                    Dim qualunque As Integer = If(isKeyDown OrElse isCtrlDestroPremuto = 1, 1, 0)
                    Interlocked.Exchange(isCtrlPremuto, qualunque)
                Case Keys.RControlKey
                    Interlocked.Exchange(isCtrlDestroPremuto, valore)
                    Dim qualunque As Integer = If(isKeyDown OrElse isCtrlSinistroPremuto = 1, 1, 0)
                    Interlocked.Exchange(isCtrlPremuto, qualunque)
                Case Keys.ControlKey
                    Interlocked.Exchange(isCtrlPremuto, valore)
                Case Keys.LShiftKey, Keys.RShiftKey
                    Interlocked.Exchange(isShiftPremuto, valore)
                Case Keys.LWin
                    Interlocked.Exchange(isLWinPremuto, valore)
                Case Keys.RWin
                    Interlocked.Exchange(isRWinPremuto, valore)
                Case Keys.Tab
                    Interlocked.Exchange(isTabPremuto, valore)
            End Select
        Catch ex As Exception
            LogErrore("AggiornaStatiInterni baseKey=" & baseKey.ToString(), ex)
        End Try
    End Sub

    Private Function GetWindowLongCompatibile(hWnd As IntPtr, nIndex As Integer) As Long
        Try
            If IntPtr.Size = 8 Then
                Return GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            Else
                Return CLng(GetWindowLong(hWnd, nIndex))
            End If
        Catch ex As Exception
            LogErrore("GetWindowLongCompatibile", ex)
            Return 0L
        End Try
    End Function

    Private Function FinestraAltTabValida(hWnd As IntPtr) As Boolean
        Try
            If hWnd = IntPtr.Zero Then Return False
            If Not IsWindowVisible(hWnd) Then Return False

            Try
                If GetWindow(hWnd, GW_OWNER) <> IntPtr.Zero Then Return False
            Catch ex As Exception
                LogErrore("FinestraAltTabValida - GetWindow", ex)
                Return False
            End Try

            Dim exStyle As Long = 0L
            Try
                exStyle = GetWindowLongCompatibile(hWnd, GWL_EXSTYLE)
            Catch ex As Exception
                LogErrore("FinestraAltTabValida - GetWindowLong", ex)
                Return False
            End Try

            If (exStyle And CLng(WS_EX_TOOLWINDOW)) <> 0L Then
                If (exStyle And CLng(WS_EX_APPWINDOW)) = 0L Then
                    Return False
                End If
            End If

            Dim lunghezzaTitolo As Integer = 0
            Try
                lunghezzaTitolo = GetWindowTextLength(hWnd)
            Catch ex As Exception
                LogErrore("FinestraAltTabValida - GetWindowTextLength", ex)
                Return False
            End Try

            If lunghezzaTitolo <= 0 Then Return False

            Return True
        Catch ex As Exception
            LogErrore("FinestraAltTabValida", ex)
            Return False
        End Try
    End Function

    Private Function EnumeraFinestreAltTab() As List(Of IntPtr)
        Dim risultato As New List(Of IntPtr)()
        Try
            Dim callback As New EnumWindowsProc(Function(hWnd As IntPtr, lParam As IntPtr) As Boolean
                                                    Try
                                                        Dim pid As UInteger = 0
                                                        Try
                                                            GetWindowThreadProcessId(hWnd, pid)
                                                        Catch
                                                            Return True
                                                        End Try

                                                        If pid = currentProcessId Then Return True

                                                        If FinestraAltTabValida(hWnd) Then
                                                            risultato.Add(hWnd)
                                                        End If
                                                    Catch ex As Exception
                                                        LogErrore("EnumeraFinestreAltTab - callback", ex)
                                                    End Try
                                                    Return True
                                                End Function)

            Try
                EnumWindows(callback, IntPtr.Zero)
            Catch ex As Exception
                LogErrore("EnumeraFinestreAltTab - EnumWindows", ex)
            End Try
        Catch ex As Exception
            LogErrore("EnumeraFinestreAltTab", ex)
        End Try
        Return risultato
    End Function

    Private Sub PortaInForegroundFinestraSuccessiva()
        Try
            Dim finestre As List(Of IntPtr) = EnumeraFinestreAltTab()

            If finestre Is Nothing OrElse finestre.Count = 0 Then
                Return
            End If

            Dim adesso As Long = OttieniTick()
            Dim ultimoTick As Long = Interlocked.Read(ultimaSequenzaAltTabTick)

            Dim indiceScelto As Integer = 0
            If isAltPremuto = 1 AndAlso ultimoTick > 0 AndAlso MillisecondiTrascorsi(ultimoTick, adesso) < 3000 Then
                indiceScelto = indiceAltTab + 1
            End If
            If indiceScelto < 0 OrElse indiceScelto >= finestre.Count Then indiceScelto = 0
            indiceAltTab = indiceScelto

            Interlocked.Exchange(ultimaSequenzaAltTabTick, OttieniTick())

            Dim hWndTarget As IntPtr = finestre(indiceScelto)

            SospendiGuardiaFocusSeNecessario()

            Try
                AttivaFinestra(hWndTarget)
            Catch ex As Exception
                LogErrore("PortaInForegroundFinestraSuccessiva - AttivaFinestra", ex)
            End Try
        Catch ex As Exception
            LogErrore("PortaInForegroundFinestraSuccessiva", ex)
        End Try
    End Sub

    Private Sub AttivaFinestra(hWnd As IntPtr)
        Try
            If hWnd = IntPtr.Zero Then Return

            Try
                If IsIconic(hWnd) Then
                    ShowWindow(hWnd, SW_RESTORE)
                Else
                    ShowWindow(hWnd, SW_SHOW)
                End If
            Catch ex As Exception
                LogErrore("AttivaFinestra - ShowWindow iniziale", ex)
            End Try

            Dim fgHwnd As IntPtr = IntPtr.Zero
            Dim fgThread As UInteger = 0
            Dim targetThread As UInteger = 0
            Dim correnteThread As UInteger = 0

            Try
                fgHwnd = GetForegroundWindow()
            Catch ex As Exception
                LogErrore("AttivaFinestra - GetForegroundWindow", ex)
            End Try

            Try
                Dim dummy As UInteger = 0
                If fgHwnd <> IntPtr.Zero Then
                    fgThread = GetWindowThreadProcessId(fgHwnd, dummy)
                End If
                targetThread = GetWindowThreadProcessId(hWnd, dummy)
            Catch ex As Exception
                LogErrore("AttivaFinestra - GetWindowThreadProcessId", ex)
            End Try

            Try
                correnteThread = GetCurrentThreadId()
            Catch ex As Exception
                LogErrore("AttivaFinestra - GetCurrentThreadId", ex)
            End Try

            Dim agganciatoFg As Boolean = False
            Dim agganciatoTarget As Boolean = False

            Try
                If fgThread <> 0UI AndAlso fgThread <> correnteThread Then
                    Try
                        agganciatoFg = AttachThreadInput(correnteThread, fgThread, True)
                        If Not agganciatoFg Then
                            LogErroreWin32("AttivaFinestra - AttachThreadInput fg fallito", Marshal.GetLastWin32Error())
                        End If
                    Catch ex As Exception
                        LogErrore("AttivaFinestra - AttachThreadInput fg", ex)
                    End Try
                End If
                If targetThread <> 0UI AndAlso targetThread <> correnteThread AndAlso targetThread <> fgThread Then
                    Try
                        agganciatoTarget = AttachThreadInput(correnteThread, targetThread, True)
                        If Not agganciatoTarget Then
                            LogErroreWin32("AttivaFinestra - AttachThreadInput target fallito", Marshal.GetLastWin32Error())
                        End If
                    Catch ex As Exception
                        LogErrore("AttivaFinestra - AttachThreadInput target", ex)
                    End Try
                End If

                Try
                    If IsIconic(hWnd) Then
                        ShowWindow(hWnd, SW_RESTORE)
                    Else
                        ShowWindow(hWnd, SW_SHOWNORMAL)
                    End If
                Catch ex As Exception
                    LogErrore("AttivaFinestra - ShowWindow normal", ex)
                End Try

                Try
                    BringWindowToTop(hWnd)
                Catch ex As Exception
                    LogErrore("AttivaFinestra - BringWindowToTop", ex)
                End Try

                Dim portatoForeground As Boolean = False
                Try
                    portatoForeground = SetForegroundWindow(hWnd)
                Catch ex As Exception
                    LogErrore("AttivaFinestra - SetForegroundWindow", ex)
                End Try

                If Not portatoForeground Then
                    Try
                        SwitchToThisWindow(hWnd, True)
                    Catch ex As Exception
                        LogErrore("AttivaFinestra - SwitchToThisWindow", ex)
                    End Try
                End If

                Try
                    SetFocus(hWnd)
                Catch ex As Exception
                    LogErrore("AttivaFinestra - SetFocus", ex)
                End Try

                Try
                    Dim verificaFg As IntPtr = GetForegroundWindow()
                    If verificaFg <> hWnd Then
                        Try
                            ShowWindowAsync(hWnd, SW_SHOWNORMAL)
                        Catch ex As Exception
                            LogErrore("AttivaFinestra - ShowWindowAsync", ex)
                        End Try
                        Try
                            SetForegroundWindow(hWnd)
                        Catch ex As Exception
                            LogErrore("AttivaFinestra - SetForegroundWindow retry", ex)
                        End Try
                        Try
                            BringWindowToTop(hWnd)
                        Catch ex As Exception
                            LogErrore("AttivaFinestra - BringWindowToTop retry", ex)
                        End Try
                        Try
                            SetFocus(hWnd)
                        Catch ex As Exception
                            LogErrore("AttivaFinestra - SetFocus retry", ex)
                        End Try
                    End If
                Catch ex As Exception
                    LogErrore("AttivaFinestra - verifica foreground", ex)
                End Try
            Finally
                Try
                    If agganciatoFg Then
                        AttachThreadInput(correnteThread, fgThread, False)
                    End If
                Catch ex As Exception
                    LogErrore("AttivaFinestra - Detach fg", ex)
                End Try
                Try
                    If agganciatoTarget Then
                        AttachThreadInput(correnteThread, targetThread, False)
                    End If
                Catch ex As Exception
                    LogErrore("AttivaFinestra - Detach target", ex)
                End Try
            End Try
        Catch ex As Exception
            LogErrore("AttivaFinestra", ex)
        End Try
    End Sub

    ' Invia un KEYUP sintetico (LLKHF_INJECTED) per ogni tasto che il nostro stato interno
    ' considera ancora premuto. Necessario perché l'hook blocca (TASTO_BLOCCATO) sia il
    ' KEYDOWN sia il KEYUP dei modificatori: se l'hook viene rimosso mentre l'utente sta
    ' ancora fisicamente tenendo premuto un tasto (es. chiusura con Alt+F4, Ctrl+qualcosa),
    ' Windows/le altre applicazioni non hanno mai visto il KEYDOWN originale (bloccato da noi)
    ' e più tardi riceveranno un KEYUP "orfano" reale, oppure - se l'utente rilascia mentre
    ' l'hook è ancora attivo un istante dopo la disinstallazione - nessuno saprà mai che il
    ' tasto è stato rilasciato. In entrambi i casi il sistema può percepire il tasto come
    ' "incollato". Sincronizziamo esplicitamente lo stato prima di terminare.
    Private Sub RilasciaForzatamenteTastiPremuti()
        Try
            Dim tastiDaRilasciare As New List(Of Integer)()

            If Interlocked.Exchange(isAltPremuto, 0) = 1 Then tastiDaRilasciare.Add(CInt(Keys.LMenu))
            If Interlocked.Exchange(isAltDestroPremuto, 0) = 1 Then tastiDaRilasciare.Add(CInt(Keys.RMenu))
            If Interlocked.Exchange(isCtrlSinistroPremuto, 0) = 1 Then tastiDaRilasciare.Add(CInt(Keys.LControlKey))
            If Interlocked.Exchange(isCtrlDestroPremuto, 0) = 1 Then tastiDaRilasciare.Add(CInt(Keys.RControlKey))
            Interlocked.Exchange(isCtrlPremuto, 0)
            Interlocked.Exchange(_ctrlUsatoConAltroTasto, 0)
            Interlocked.Exchange(_altUsatoConAltroTasto, 0)
            Interlocked.Exchange(_shiftUsatoConAltroTasto, 0)
            If Interlocked.Exchange(isShiftPremuto, 0) = 1 Then
                tastiDaRilasciare.Add(CInt(Keys.LShiftKey))
                tastiDaRilasciare.Add(CInt(Keys.RShiftKey))
            End If
            If Interlocked.Exchange(isTabPremuto, 0) = 1 Then tastiDaRilasciare.Add(CInt(Keys.Tab))
            Interlocked.Exchange(isLWinPremuto, 0)
            Interlocked.Exchange(isRWinPremuto, 0)
            Interlocked.Exchange(_ctrlSinPendenteAltGr, 0)
            Interlocked.Exchange(_altGrInCorso, 0)
            Interlocked.Exchange(_altGrConCarattere, 0)
            Interlocked.Exchange(_tickUltimoAltGrConCarattere, 0L)
            Interlocked.Exchange(_ctrlSinRilascioAltGrPendente, 0)

            If Not tastiNonModificatoriPremuti.IsEmpty Then
                Dim residui As New List(Of Integer)(tastiNonModificatoriPremuti.Keys)
                For Each vk As Integer In residui
                    tastiDaRilasciare.Add(vk)
                    Dim rimosso As Byte
                    tastiNonModificatoriPremuti.TryRemove(vk, rimosso)
                Next
            End If

            If tastiDaRilasciare.Count = 0 Then Return

            Dim inputs(tastiDaRilasciare.Count - 1) As INPUT
            For i As Integer = 0 To tastiDaRilasciare.Count - 1
                Dim vk As Integer = tastiDaRilasciare(i)
                Dim flags As UInteger = KEYEVENTF_KEYUP
                If vk = CInt(Keys.RMenu) OrElse vk = CInt(Keys.RControlKey) OrElse vk = CInt(Keys.RShiftKey) Then
                    flags = flags Or KEYEVENTF_EXTENDEDKEY
                End If

                Dim inp As New INPUT()
                inp.type = INPUT_KEYBOARD
                inp.U.ki.wVk = CUShort(vk)
                inp.U.ki.wScan = 0
                inp.U.ki.dwFlags = flags
                inp.U.ki.time = 0
                inp.U.ki.dwExtraInfo = IntPtr.Zero
                inputs(i) = inp
            Next

            Dim inviati As UInteger = SendInput(CUInt(inputs.Length), inputs, Marshal.SizeOf(GetType(INPUT)))
            If inviati <> CUInt(inputs.Length) Then
                Dim err As Integer = Marshal.GetLastWin32Error()
                LogErroreWin32("RilasciaForzatamenteTastiPremuti - SendInput incompleto", err)
            End If
        Catch ex As Exception
            LogErrore("RilasciaForzatamenteTastiPremuti", ex)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Private _disposeInCorso As Integer = 0

    Protected Sub Dispose(disposing As Boolean)
        If disposedValue Then Return
        ' Due chiamate concorrenti libererebbero due volte i GCHandle mentre l'altra aspetta ancora
        ' il Join: l'hook resterebbe installato con il delegate non piu' ancorato.
        If Interlocked.CompareExchange(_disposeInCorso, 1, 0) <> 0 Then Return

        Try
            Interlocked.Exchange(inShutdownStatico, 1)

            RiprendiGuardiaFocusSeNecessario(False)

            If resyncTimer IsNot Nothing Then
                Try
                    resyncTimer.Stop()
                    RemoveHandler resyncTimer.Tick, AddressOf ResyncTick
                    resyncTimer.Dispose()
                Catch ex As Exception
                    LogErrore("Dispose - resyncTimer", ex)
                End Try
            End If

            Dim tid As UInteger = Interlocked.CompareExchange(_threadHookId, 0, 0)
            If tid <> 0 Then
                Try
                    PostThreadMessage(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero)
                Catch ex As Exception
                    LogErrore("Dispose - PostThreadMessage WM_QUIT", ex)
                End Try
            End If

            Try
                If _threadHook IsNot Nothing AndAlso _threadHook.IsAlive Then
                    _threadHook.Join(2000)
                End If
            Catch ex As Exception
                LogErrore("Dispose - threadHook.Join", ex)
            End Try

            Try
                If _threadMonitor IsNot Nothing AndAlso _threadMonitor.IsAlive Then
                    _threadMonitor.Join(2000)
                End If
            Catch ex As Exception
                LogErrore("Dispose - threadMonitor.Join", ex)
            End Try

            Try
                RilasciaForzatamenteTastiPremuti()
            Catch ex As Exception
                LogErrore("Dispose - RilasciaForzatamenteTastiPremuti", ex)
            End Try

            Try
                If codaEventi IsNot Nothing Then
                    codaEventi.CompleteAdding()
                End If
            Catch ex As Exception
                LogErrore("Dispose - CompleteAdding", ex)
            End Try

            Try
                If threadConsumer IsNot Nothing AndAlso threadConsumer.IsAlive Then
                    threadConsumer.Join(1000)
                End If
            Catch ex As Exception
                LogErrore("Dispose - threadConsumer.Join", ex)
            End Try

            Try
                If codaEventi IsNot Nothing Then
                    codaEventi.Dispose()
                End If
            Catch ex As Exception
                LogErrore("Dispose - codaEventi.Dispose", ex)
            End Try

            Try
                Dim registrato As Form = Nothing
                SyncLock formLock
                    registrato = formPrincipaleRegistrato
                    formPrincipaleRegistrato = Nothing
                    _formPrincipaleHandleCache = IntPtr.Zero
                End SyncLock
                If registrato IsNot Nothing Then
                    RemoveHandler registrato.HandleCreated, AddressOf OnHandleCreatedFormPrincipale
                    RemoveHandler registrato.HandleDestroyed, AddressOf OnHandleDestroyedFormPrincipale
                End If
            Catch ex As Exception
                LogErrore("Dispose - rimozione handler form principale", ex)
            End Try

            ' Se il thread dell'hook non e' uscito entro il Join gli hook sono ancora installati:
            ' liberare qui i GCHandle lascerebbe Windows con un puntatore a un delegate raccolto
            ' dal GC, cioe' un crash nativo al primo tasto premuto.
            Try
                If hookID <> IntPtr.Zero Then
                    UnhookWindowsHookEx(hookID)
                    hookID = IntPtr.Zero
                End If
            Catch ex As Exception
                LogErrore("Dispose - UnhookWindowsHookEx residuo", ex)
            End Try

            Try
                If mouseHookID <> IntPtr.Zero Then
                    UnhookWindowsHookEx(mouseHookID)
                    mouseHookID = IntPtr.Zero
                End If
            Catch ex As Exception
                LogErrore("Dispose - UnhookWindowsHookEx mouse residuo", ex)
            End Try

            Try
                If hookProcHandle.IsAllocated Then
                    hookProcHandle.Free()
                End If
            Catch ex As Exception
                LogErrore("Dispose - hookProcHandle.Free", ex)
            End Try

            Try
                If mouseHookProcHandle.IsAllocated Then
                    mouseHookProcHandle.Free()
                End If
            Catch ex As Exception
                LogErrore("Dispose - mouseHookProcHandle.Free", ex)
            End Try

            hookProc = Nothing
            mouseHookProc = Nothing
            disposedValue = True
        Catch ex As Exception
            LogErrore("Dispose", ex)
        End Try
    End Sub
End Class