Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Threading

' Gestione degli agganci per la tastiera e gli eventi di sistema, traduzione dei comandi rapidi e controllo dell'interazione
' utente.
Friend NotInheritable Class AgganciLettore

    Private Const VK_BACK As Integer = &H8
    Private Const VK_TAB As Integer = &H9
    Private Const VK_RETURN As Integer = &HD
    Private Const VK_SHIFT As Integer = &H10
    Private Const VK_CONTROL As Integer = &H11
    Private Const VK_MENU As Integer = &H12
    Private Const VK_CAPITAL As Integer = &H14
    Private Const VK_ESCAPE As Integer = &H1B
    Private Const VK_SPACE As Integer = &H20
    Private Const VK_PRIOR As Integer = &H21
    Private Const VK_NEXT As Integer = &H22
    Private Const VK_END As Integer = &H23
    Private Const VK_HOME As Integer = &H24
    Private Const VK_LEFT As Integer = &H25
    Private Const VK_UP As Integer = &H26
    Private Const VK_RIGHT As Integer = &H27
    Private Const VK_DOWN As Integer = &H28
    Private Const VK_INSERT As Integer = &H2D
    Private Const VK_DELETE As Integer = &H2E
    Private Const VK_A As Integer = &H41
    Private Const VK_B As Integer = &H42
    Private Const VK_C As Integer = &H43
    Private Const VK_D As Integer = &H44
    Private Const VK_E As Integer = &H45
    Private Const VK_F As Integer = &H46
    Private Const VK_G As Integer = &H47
    Private Const VK_H As Integer = &H48
    Private Const VK_I As Integer = &H49
    Private Const VK_J As Integer = &H4A
    Private Const VK_K As Integer = &H4B
    Private Const VK_M As Integer = &H4D
    Private Const VK_L As Integer = &H4C
    Private Const VK_N As Integer = &H4E
    Private Const VK_O As Integer = &H4F
    Private Const VK_P As Integer = &H50
    Private Const VK_Q As Integer = &H51
    Private Const VK_R As Integer = &H52
    Private Const VK_S As Integer = &H53
    Private Const VK_T As Integer = &H54
    Private Const VK_V As Integer = &H56
    Private Const VK_W As Integer = &H57
    Private Const VK_X As Integer = &H58
    Private Const VK_Y As Integer = &H59
    Private Const VK_Z As Integer = &H5A
    Private Const VK_F1 As Integer = &H70
    Private Const VK_F10 As Integer = &H79
    Private Const VK_LWIN As Integer = &H5B
    Private Const VK_RWIN As Integer = &H5C
    Private Const VK_F11 As Integer = &H7A
    Private Const VK_F12 As Integer = &H7B
    Private Const VK_LSHIFT As Integer = &HA0
    Private Const VK_RSHIFT As Integer = &HA1
    Private Const VK_LCONTROL As Integer = &HA2
    Private Const VK_RCONTROL As Integer = &HA3
    Private Const VK_LMENU As Integer = &HA4
    Private Const VK_RMENU As Integer = &HA5
    Private Const IntervalloDoppioInsert As Long = 500

    Private ReadOnly log As New Logger()
    Private ReadOnly _lettore As WGLettoreSchermo
    Private ReadOnly _tastiSoppressi As New HashSet(Of Integer)()
    Private ReadOnly _bloccoGiu As New HashSet(Of Integer)()
    Private _maiuscoloGiu As Boolean
    Private _idComandoComponente As String = ""
    Private _maiuscoloSolo As Boolean
    Private _maiuscoloTick As Integer
    Private ReadOnly _orologio As Stopwatch = Stopwatch.StartNew()
    Private _thread As Thread
    Private _idThread As UInteger
    Private _threadTastiera As Thread
    Private _idThreadTastiera As UInteger
    Private _hookTastiera As IntPtr = IntPtr.Zero
    Private _hookPrimoPiano As IntPtr = IntPtr.Zero
    Private _hookStato As IntPtr = IntPtr.Zero
    Private _hookNome As IntPtr = IntPtr.Zero
    Private _hookValore As IntPtr = IntPtr.Zero
    Private _hookSelezione As IntPtr = IntPtr.Zero
    Private _hookFocus As IntPtr = IntPtr.Zero
    Private _hookAvviso As IntPtr = IntPtr.Zero
    Private _hookMenu As IntPtr = IntPtr.Zero
    Private _hookMostra As IntPtr = IntPtr.Zero
    Private _hookDescrizione As IntPtr = IntPtr.Zero
    Private _hookTestoIa2 As IntPtr = IntPtr.Zero
    Private _pidGecko As UInteger
    Private _riquadroGecko As Boolean
    Private Const EventoAvvisoSistema As UInteger = &H2UI
    Private Const EventoInizioMenu As UInteger = &H4UI
    Private Const EventoFineMenu As UInteger = &H5UI
    Private Const EventoInizioRiquadroMenu As UInteger = &H6UI
    Private Const EventoFineRiquadroMenu As UInteger = &H7UI
    Private Const EventoMostraOggetto As UInteger = &H8002UI
    Private Const EventoDescrizioneOggetto As UInteger = &H800DUI
    Private Const EventoTestoInseritoIa2 As UInteger = &H11EUI
    Private Const EventoTestoRimossoIa2 As UInteger = &H11FUI
    Private Const EventoTestoAggiornatoIa2 As UInteger = &H120UI
    Private _proceduraTastiera As NativoLettore.ProceduraTastiera
    Private _proceduraEventi As NativoLettore.ProceduraEventi
    Private _insertPremuto As Boolean
    Private _altMascherare As Boolean
    Private _capsPremuto As Boolean
    Private _insertUsato As Boolean
    Private _capsUsato As Boolean
    Private _ultimoInsertSolo As Long = -1
    Private _ultimoCapsSolo As Long = -1

    Public Sub New(lettore As WGLettoreSchermo)
        Try
            _lettore = lettore
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.New: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _threadTastiera = New Thread(AddressOf CicloTastiera)
            _threadTastiera.IsBackground = True
            _threadTastiera.Name = "WGLettoreSchermoTastiera"
            _threadTastiera.Priority = ThreadPriority.AboveNormal
            _threadTastiera.SetApartmentState(ApartmentState.STA)
            _threadTastiera.Start()
            _thread = New Thread(AddressOf CicloMessaggi)
            _thread.IsBackground = True
            _thread.Name = "WGLettoreSchermoAgganci"
            _thread.SetApartmentState(ApartmentState.STA)
            _thread.Start()
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            If _idThreadTastiera <> 0UI Then NativoLettore.PostThreadMessage(_idThreadTastiera, NativoLettore.WM_QUIT, IntPtr.Zero, IntPtr.Zero)
            If _idThread <> 0UI Then NativoLettore.PostThreadMessage(_idThread, NativoLettore.WM_QUIT, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.Ferma: " & ex.Message)
        End Try
    End Sub

    Public Sub Attendi()
        Try
            If _threadTastiera IsNot Nothing Then
                _threadTastiera.Join(2000)
                _threadTastiera = Nothing
            End If
            If _thread IsNot Nothing Then
                _thread.Join(2000)
                _thread = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.Attendi: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloMessaggi()
        Try
            _idThread = NativoLettore.GetCurrentThreadId()
            InstallaAgganciEventi()
            LetturaJava.Avvia(AddressOf _lettore.AccodaFocusJava)
            Dim messaggio As New NativoLettore.MSG()
            While True
                Dim esito As Integer = NativoLettore.GetMessage(messaggio, IntPtr.Zero, 0UI, 0UI)
                If esito = 0 OrElse esito = -1 Then Exit While
                NativoLettore.TranslateMessage(messaggio)
                NativoLettore.DispatchMessage(messaggio)
            End While
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.CicloMessaggi: " & ex.Message)
        End Try
        RimuoviAgganciEventi()
    End Sub

    Private Sub CicloTastiera()
        Try
            _idThreadTastiera = NativoLettore.GetCurrentThreadId()
            InstallaAggancioTastiera()
            Dim messaggio As New NativoLettore.MSG()
            While True
                Dim esito As Integer = NativoLettore.GetMessage(messaggio, IntPtr.Zero, 0UI, 0UI)
                If esito = 0 OrElse esito = -1 Then Exit While
                NativoLettore.TranslateMessage(messaggio)
                NativoLettore.DispatchMessage(messaggio)
            End While
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.CicloTastiera: " & ex.Message)
        End Try
        RimuoviAggancioTastiera()
    End Sub

    Private Sub InstallaAggancioTastiera()
        Try
            _proceduraTastiera = New NativoLettore.ProceduraTastiera(AddressOf ProceduraTastieraCallback)
            Dim modulo As IntPtr = NativoLettore.GetModuleHandle(Nothing)
            _hookTastiera = NativoLettore.SetWindowsHookEx(NativoLettore.WH_KEYBOARD_LL, _proceduraTastiera, modulo, 0UI)
            If _hookTastiera = IntPtr.Zero Then Throw New Win32Exception(Marshal.GetLastWin32Error(), "aggancio della tastiera non riuscito")
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.InstallaAggancioTastiera: " & ex.Message)
        End Try
    End Sub

    Private Sub RimuoviAggancioTastiera()
        Try
            If _hookTastiera <> IntPtr.Zero Then NativoLettore.UnhookWindowsHookEx(_hookTastiera)
            _hookTastiera = IntPtr.Zero
            _idThreadTastiera = 0UI
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.RimuoviAggancioTastiera: " & ex.Message)
        End Try
    End Sub

    Private Sub InstallaAgganciEventi()
        Try
            _proceduraEventi = New NativoLettore.ProceduraEventi(AddressOf ProceduraEventiCallback)
            Dim opzioni As UInteger = NativoLettore.WINEVENT_OUTOFCONTEXT
            _hookPrimoPiano = NativoLettore.SetWinEventHook(NativoLettore.EVENT_SYSTEM_FOREGROUND, NativoLettore.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookStato = NativoLettore.SetWinEventHook(NativoLettore.EVENT_OBJECT_STATECHANGE, NativoLettore.EVENT_OBJECT_STATECHANGE, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookNome = NativoLettore.SetWinEventHook(NativoLettore.EVENT_OBJECT_NAMECHANGE, NativoLettore.EVENT_OBJECT_NAMECHANGE, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookValore = NativoLettore.SetWinEventHook(NativoLettore.EVENT_OBJECT_VALUECHANGE, NativoLettore.EVENT_OBJECT_VALUECHANGE, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookSelezione = NativoLettore.SetWinEventHook(NativoLettore.EVENT_OBJECT_TEXTSELECTIONCHANGED, NativoLettore.EVENT_OBJECT_TEXTSELECTIONCHANGED, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookFocus = NativoLettore.SetWinEventHook(NativoLettore.EVENT_OBJECT_FOCUS, NativoLettore.EVENT_OBJECT_FOCUS, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookAvviso = NativoLettore.SetWinEventHook(EventoAvvisoSistema, EventoAvvisoSistema, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            _hookMenu = NativoLettore.SetWinEventHook(EventoInizioMenu, EventoFineRiquadroMenu, IntPtr.Zero, _proceduraEventi, 0UI, 0UI, opzioni)
            AggiornaAgganciGecko(NativoLettore.GetForegroundWindow())
            If _hookPrimoPiano = IntPtr.Zero Then Throw New InvalidOperationException("aggancio degli eventi di primo piano non riuscito")
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.InstallaAgganciEventi: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaAgganciGecko(finestra As IntPtr)
        Try
            Dim pidGecko As UInteger = 0UI
            If finestra <> IntPtr.Zero Then
                Dim classe As New System.Text.StringBuilder(64)
                If NativoLettore.GetClassName(finestra, classe, classe.Capacity) > 0 AndAlso classe.ToString().StartsWith("Mozilla", StringComparison.Ordinal) Then NativoLettore.GetWindowThreadProcessId(finestra, pidGecko)
            End If
            If pidGecko = _pidGecko Then Return
            TogliAgganciGecko()
            If pidGecko = 0UI OrElse _proceduraEventi Is Nothing Then Return
            Dim opzioni As UInteger = NativoLettore.WINEVENT_OUTOFCONTEXT Or NativoLettore.WINEVENT_SKIPOWNPROCESS
            _hookMostra = NativoLettore.SetWinEventHook(EventoMostraOggetto, EventoMostraOggetto, IntPtr.Zero, _proceduraEventi, pidGecko, 0UI, opzioni)
            _hookDescrizione = NativoLettore.SetWinEventHook(EventoDescrizioneOggetto, EventoDescrizioneOggetto, IntPtr.Zero, _proceduraEventi, pidGecko, 0UI, opzioni)
            _hookTestoIa2 = NativoLettore.SetWinEventHook(EventoTestoInseritoIa2, EventoTestoAggiornatoIa2, IntPtr.Zero, _proceduraEventi, pidGecko, 0UI, opzioni)
            _pidGecko = pidGecko
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.AggiornaAgganciGecko: " & ex.Message)
        End Try
    End Sub

    Private Function FinestraChromiumEvento(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As New System.Text.StringBuilder(64)
            NativoLettore.GetClassName(finestra, classe, classe.Capacity)
            Return classe.ToString().StartsWith("Chrome_", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.FinestraChromiumEvento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FinestraMenuEvento(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim classe As New System.Text.StringBuilder(128)
            NativoLettore.GetClassName(finestra, classe, classe.Capacity)
            Select Case classe.ToString()
                Case "#32768", "Xaml_WindowedPopupClass", "Microsoft.UI.Content.PopupWindowSiteBridge"
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.FinestraMenuEvento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ControllaRiquadroGecko(finestra As IntPtr, primoPiano As IntPtr)
        Try
            Dim classe As New System.Text.StringBuilder(64)
            NativoLettore.GetClassName(finestra, classe, classe.Capacity)
            Dim nome As String = classe.ToString()
            If nome = "MozillaDropShadowWindowClass" Then
                If Not _riquadroGecko Then
                    _riquadroGecko = True
                    TogliAgganciGecko()
                End If
            ElseIf _riquadroGecko Then
                _riquadroGecko = False
                AggiornaAgganciGecko(primoPiano)
            End If
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ControllaRiquadroGecko: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliAgganciGecko()
        Try
            If _hookMostra <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookMostra)
            If _hookDescrizione <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookDescrizione)
            If _hookTestoIa2 <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookTestoIa2)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.TogliAgganciGecko: " & ex.Message)
        Finally
            _hookMostra = IntPtr.Zero
            _hookDescrizione = IntPtr.Zero
            _hookTestoIa2 = IntPtr.Zero
            _pidGecko = 0UI
        End Try
    End Sub

    Private Sub RimuoviAgganciEventi()
        Try
            If _hookPrimoPiano <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookPrimoPiano)
            If _hookStato <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookStato)
            If _hookNome <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookNome)
            If _hookValore <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookValore)
            If _hookSelezione <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookSelezione)
            If _hookFocus <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookFocus)
            If _hookAvviso <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookAvviso)
            If _hookMenu <> IntPtr.Zero Then NativoLettore.UnhookWinEvent(_hookMenu)
            TogliAgganciGecko()
            _hookPrimoPiano = IntPtr.Zero
            _hookStato = IntPtr.Zero
            _hookNome = IntPtr.Zero
            _hookValore = IntPtr.Zero
            _hookSelezione = IntPtr.Zero
            _hookFocus = IntPtr.Zero
            _hookAvviso = IntPtr.Zero
            _hookMenu = IntPtr.Zero
            _idThread = 0UI
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.RimuoviAgganciEventi: " & ex.Message)
        End Try
    End Sub

    Private Function ProceduraTastieraCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        Try
            If nCode >= 0 Then
                Dim dati As NativoLettore.KBDLLHOOKSTRUCT = Marshal.PtrToStructure(Of NativoLettore.KBDLLHOOKSTRUCT)(lParam)
                If (dati.flags And NativoLettore.LLKHF_INJECTED) = 0 Then
                    Dim messaggioTasto As Integer = wParam.ToInt32()
                    Dim codiceTasto As Integer = dati.vkCode
                    Dim tastoGiuAdesso As Boolean = (messaggioTasto = NativoLettore.WM_KEYDOWN OrElse messaggioTasto = NativoLettore.WM_SYSKEYDOWN)
                    Dim tastoAlt As Boolean = (codiceTasto = VK_MENU OrElse codiceTasto = VK_LMENU OrElse codiceTasto = VK_RMENU)
                    If tastoAlt AndAlso tastoGiuAdesso Then _altMascherare = False
                    Dim soppresso As Boolean = GestisciTasto(codiceTasto, messaggioTasto, dati.scanCode)
                    If soppresso AndAlso tastoGiuAdesso AndAlso Not tastoAlt AndAlso TastoGiu(VK_MENU) Then _altMascherare = True
                    If tastoAlt AndAlso Not tastoGiuAdesso AndAlso _altMascherare Then
                        _altMascherare = False
                        ThreadPool.QueueUserWorkItem(AddressOf RilascioAltMascherato, codiceTasto)
                        Return New IntPtr(1)
                    End If
                    If soppresso Then Return New IntPtr(1)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ProceduraTastieraCallback: " & ex.Message)
        End Try
        Return NativoLettore.CallNextHookEx(_hookTastiera, nCode, wParam, lParam)
    End Function

    Private Function GestisciTasto(codice As Integer, messaggio As Integer, scansione As Integer) As Boolean
        Try
            Dim premuto As Boolean = (messaggio = NativoLettore.WM_KEYDOWN OrElse messaggio = NativoLettore.WM_SYSKEYDOWN)
            Dim rilasciato As Boolean = (messaggio = NativoLettore.WM_KEYUP OrElse messaggio = NativoLettore.WM_SYSKEYUP)
            If Not _lettore.InEsecuzione Then Return False
            If rilasciato AndAlso (codice = VK_MENU OrElse codice = VK_LMENU OrElse codice = VK_RMENU) Then _lettore.AlRilascioAlt()
            If rilasciato AndAlso _tastiSoppressi.Contains(codice) Then
                _tastiSoppressi.Remove(codice)
                Return True
            End If
            If _lettore.SilenzioEsterno() OrElse _lettore.PrimoPianoInWinGabriel() Then
                _insertPremuto = False
                _capsPremuto = False
                _insertUsato = False
                _capsUsato = False
                Return False
            End If
            ControllaMaiuscoloSolo(codice, premuto, rilasciato)
            If premuto AndAlso Not EModificatore(codice) AndAlso (TastoGiu(VK_LWIN) OrElse TastoGiu(VK_RWIN)) Then Return False
            If codice = VK_INSERT AndAlso UsaInsert() Then Return GestisciModificatore(False, premuto, rilasciato)
            If codice = VK_CAPITAL AndAlso UsaCaps() Then Return GestisciModificatore(True, premuto, rilasciato)
            If codice = VK_CAPITAL OrElse codice = &H90 OrElse codice = &H91 Then
                If rilasciato Then
                    _bloccoGiu.Remove(codice)
                ElseIf premuto AndAlso _bloccoGiu.Add(codice) Then
                    ThreadPool.QueueUserWorkItem(AddressOf AnnunciaBlocco, codice)
                End If
            End If
            If Not premuto Then Return False
            If _insertPremuto OrElse _capsPremuto Then
                If EModificatore(codice) Then Return False
                If _insertPremuto Then _insertUsato = True
                If _capsPremuto Then _capsUsato = True
                _tastiSoppressi.Add(codice)
                Dim comando As ComandoLettore
                If TraduciComando(codice, TastoGiu(VK_CONTROL), TastoGiu(VK_SHIFT), comando) Then
                    If _lettore.AiutoTastiAttivo AndAlso comando <> ComandoLettore.AiutoTasti Then
                        _lettore.SegnaTasto()
                        _lettore.ParlaAiuto(ImpostazioniLettore.Descrizione(comando))
                    ElseIf comando = ComandoLettore.ComponenteEsterno Then
                        _lettore.AccodaComandoComponente(_idComandoComponente)
                    Else
                        _lettore.AccodaComando(comando)
                    End If
                ElseIf _lettore.AiutoTastiAttivo Then
                    _lettore.ParlaAiuto("nessuna azione")
                End If
                Return True
            End If
            If codice = VK_CONTROL OrElse codice = VK_LCONTROL OrElse codice = VK_RCONTROL Then
                _lettore.RichiediInterruzione()
                Return False
            End If
            If premuto AndAlso (codice = VK_MENU OrElse codice = VK_LMENU OrElse codice = VK_RMENU) AndAlso
               Not TastoGiu(VK_CONTROL) AndAlso Not TastoGiu(VK_SHIFT) Then
                _lettore.AltPremutoTasto()
            End If
            If EModificatore(codice) Then Return False
            If Not _lettore.AiutoTastiAttivo Then _lettore.RichiediInterruzione()
            If _lettore.InRicercaAttiva AndAlso CatturaRicerca(codice) Then Return True
            If _lettore.AiutoTastiAttivo Then
                _tastiSoppressi.Add(codice)
                _lettore.SegnaTasto()
                _lettore.ParlaAiuto(DescrizioneTastoLibero(codice, scansione))
                Return True
            End If
            If _lettore.CatturaTastierino() AndAlso Not TastoGiu(VK_CONTROL) AndAlso Not TastoGiu(VK_MENU) Then
                Dim comandoPad As ComandoLettore
                If ComandoTastierino(codice, scansione, comandoPad) Then
                    _tastiSoppressi.Add(codice)
                    _lettore.AccodaComando(comandoPad)
                    Return True
                End If
            End If
            If codice >= &H21 AndAlso codice <= &H28 Then _lettore.RichiediInterruzione()
            Dim ctrl As Boolean = TastoGiu(VK_CONTROL)
            Dim alt As Boolean = TastoGiu(VK_MENU) OrElse messaggio = NativoLettore.WM_SYSKEYDOWN
            Dim maiuscolo As Boolean = TastoGiu(VK_SHIFT)
            If _lettore.MenuVirtualeCatturaTasti AndAlso Not ctrl AndAlso Not alt AndAlso
               (codice = VK_UP OrElse codice = VK_DOWN OrElse codice = VK_LEFT OrElse codice = VK_RIGHT OrElse codice = VK_HOME OrElse codice = VK_END OrElse codice = VK_RETURN) Then
                _tastiSoppressi.Add(codice)
                _lettore.TastoMenuVirtuale(codice)
                Return True
            End If
            If codice = VK_ESCAPE AndAlso _lettore.MenuVirtualeAttivo Then _lettore.ChiudiMenuVirtualeDaTasto()
            If _lettore.EscDaScrittura AndAlso codice = VK_ESCAPE AndAlso Not ctrl AndAlso Not alt Then
                _tastiSoppressi.Add(codice)
                _lettore.AccodaComando(ComandoLettore.EsciScrittura)
                Return True
            End If
            If codice = VK_ESCAPE AndAlso Not alt Then _lettore.AccodaTasto(codice, ctrl, maiuscolo, AzioneTastoLettore.SoloModulo)
            If (codice = VK_TAB OrElse codice = VK_LEFT OrElse codice = VK_RIGHT OrElse codice = VK_UP OrElse codice = VK_DOWN) Then
                Dim daCommutatore As Boolean = alt AndAlso codice = VK_TAB
                Dim commutatoreAperto As Boolean = False
                If Not daCommutatore Then commutatoreAperto = _lettore.CommutatoreAperto()
                If daCommutatore OrElse commutatoreAperto OrElse (alt AndAlso Not ctrl) Then
                    _lettore.AccodaComando(ComandoLettore.Commutatore)
                End If
                If daCommutatore OrElse commutatoreAperto Then Return False
            End If
            If codice = VK_TAB AndAlso Not alt Then
                _lettore.TastoSpostamentoFuoco()
                If Not ctrl AndAlso _lettore.TabDalCursoreVirtuale() Then
                    _tastiSoppressi.Add(codice)
                    _lettore.AccodaTasto(codice, False, maiuscolo, AzioneTastoLettore.NavigazioneWeb)
                    Return True
                End If
                _lettore.AccodaTasto(codice, ctrl, maiuscolo, AzioneTastoLettore.SoloModulo)
            End If
            If _lettore.TastiVirtuali AndAlso Not alt AndAlso (codice = &H5D OrElse (codice = VK_F10 AndAlso maiuscolo AndAlso Not ctrl)) Then
                _tastiSoppressi.Add(codice)
                _lettore.AccodaTasto(codice, ctrl, maiuscolo, AzioneTastoLettore.NavigazioneWeb, alt)
                Return True
            End If
            Dim frecciaTabella As Boolean = alt AndAlso ctrl AndAlso (codice = VK_LEFT OrElse codice = VK_RIGHT OrElse codice = VK_UP OrElse codice = VK_DOWN)
            If _lettore.TastiVirtuali AndAlso (Not alt OrElse frecciaTabella) AndAlso TastoVirtuale(codice, ctrl) Then
                _tastiSoppressi.Add(codice)
                _lettore.RichiediInterruzione()
                If codice <> VK_RETURN AndAlso codice <> VK_SPACE AndAlso (Not ctrl OrElse ETastoNavigazione(codice) OrElse codice = VK_BACK OrElse codice = VK_DELETE) Then _lettore.SegnaCursoreDaTasti()
                Dim azione As AzioneTastoLettore = AzioneTastoLettore.NavigazioneWeb
                If ctrl AndAlso (codice = VK_C OrElse codice = VK_INSERT) Then azione = AzioneTastoLettore.Copia
                _lettore.AccodaTasto(codice, ctrl, maiuscolo, azione, alt)
                Return True
            End If
            If ctrl AndAlso Not alt AndAlso codice = &H20 Then
                _lettore.AccodaTasto(codice, True, maiuscolo, AzioneTastoLettore.SelezioneVoce)
                Return False
            End If
            If ETastoNavigazione(codice) Then
                Dim azione As AzioneTastoLettore = If(maiuscolo, AzioneTastoLettore.Selezione, AzioneTastoLettore.Navigazione)
                _lettore.AccodaTasto(codice, ctrl, maiuscolo, azione, alt)
                Return False
            End If
            If ETastoModifica(codice, ctrl, alt, maiuscolo) Then
                Dim azione As AzioneTastoLettore = AzioneTastoLettore.Modifica
                If ctrl AndAlso (codice = VK_C OrElse codice = VK_INSERT) Then azione = AzioneTastoLettore.Copia
                If ctrl AndAlso codice = VK_A Then azione = AzioneTastoLettore.Selezione
                _lettore.AccodaTasto(codice, ctrl, maiuscolo, azione)
                Return False
            End If
            If ETastoScrivibile(codice) Then
                If (Not ctrl AndAlso Not alt) OrElse (ctrl AndAlso alt) Then
                    _lettore.AccodaTasto(codice, False, maiuscolo, AzioneTastoLettore.Modifica, False, ctrl AndAlso alt)
                ElseIf ctrl AndAlso Not alt Then
                    Dim azione As AzioneTastoLettore = AzioneTastoLettore.Modifica
                    If codice = VK_C Then azione = AzioneTastoLettore.Copia
                    If codice = VK_A Then azione = AzioneTastoLettore.Selezione
                    If codice = VK_A OrElse codice = VK_C OrElse codice = VK_X OrElse codice = VK_V OrElse codice = VK_Z OrElse codice = VK_Y Then
                        _lettore.AccodaTasto(codice, True, maiuscolo, azione)
                    End If
                End If
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.GestisciTasto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function UsaInsert() As Boolean
        Try
            Return Not String.Equals(_lettore.TastoModificatore, "capslock", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.UsaInsert: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function UsaCaps() As Boolean
        Try
            Dim modo As String = _lettore.TastoModificatore
            Return String.Equals(modo, "capslock", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(modo, "entrambi", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.UsaCaps: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function GestisciModificatore(caps As Boolean, premuto As Boolean, rilasciato As Boolean) As Boolean
        Try
            If premuto Then
                If caps Then
                    If Not _capsPremuto Then
                        _capsPremuto = True
                        _capsUsato = False
                    End If
                ElseIf Not _insertPremuto Then
                    _insertPremuto = True
                    _insertUsato = False
                End If
                Return True
            End If
            If Not rilasciato Then Return True
            Dim usato As Boolean
            If caps Then
                usato = _capsUsato
                _capsPremuto = False
                _capsUsato = False
            Else
                usato = _insertUsato
                _insertPremuto = False
                _insertUsato = False
            End If
            If usato OrElse _insertPremuto OrElse _capsPremuto Then Return True
            Dim adesso As Long = _orologio.ElapsedMilliseconds
            Dim ultimo As Long = If(caps, _ultimoCapsSolo, _ultimoInsertSolo)
            If ultimo >= 0 AndAlso adesso - ultimo < IntervalloDoppioInsert Then
                If caps Then
                    _ultimoCapsSolo = -1
                Else
                    _ultimoInsertSolo = -1
                End If
                ThreadPool.QueueUserWorkItem(AddressOf InviaTasto, If(caps, VK_CAPITAL, VK_INSERT))
            ElseIf caps Then
                _ultimoCapsSolo = adesso
            Else
                _ultimoInsertSolo = adesso
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.GestisciModificatore: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub RilascioAltMascherato(stato As Object)
        Try
            Dim codice As Integer = VK_MENU
            If TypeOf stato Is Integer Then codice = CInt(stato)
            Dim esteso As Integer = If(codice = VK_RMENU, &H1, 0)
            NativoLettore.keybd_event(CByte(&HE8), 0, 0, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(&HE8), 0, NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(codice), 0, esteso Or NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.RilascioAltMascherato: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaMaiuscoloSolo(codice As Integer, premuto As Boolean, rilasciato As Boolean)
        Try
            Dim eMaiuscolo As Boolean = codice = VK_SHIFT OrElse codice = VK_LSHIFT OrElse codice = VK_RSHIFT
            If Not eMaiuscolo Then
                If premuto Then _maiuscoloSolo = False
                Return
            End If
            If premuto Then
                If Not _maiuscoloGiu Then
                    _maiuscoloGiu = True
                    _maiuscoloSolo = Not _insertPremuto AndAlso Not _capsPremuto AndAlso Not TastoGiu(VK_CONTROL) AndAlso Not TastoGiu(VK_MENU)
                    _maiuscoloTick = Environment.TickCount
                End If
                Return
            End If
            If Not rilasciato Then Return
            Dim solo As Boolean = _maiuscoloGiu AndAlso _maiuscoloSolo AndAlso Environment.TickCount - _maiuscoloTick < 600
            _maiuscoloGiu = False
            _maiuscoloSolo = False
            If solo AndAlso _lettore.PausaConMaiuscolo Then ThreadPool.QueueUserWorkItem(AddressOf PausaDaMaiuscolo)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ControllaMaiuscoloSolo: " & ex.Message)
        End Try
    End Sub

    Private Sub PausaDaMaiuscolo(stato As Object)
        Try
            _lettore.AlternaPausaMaiuscolo()
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.PausaDaMaiuscolo: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaBlocco(stato As Object)
        Try
            If Not TypeOf stato Is Integer Then Return
            Dim codice As Integer = CInt(stato)
            Thread.Sleep(60)
            Dim attivo As Boolean = (CInt(NativoLettore.GetKeyState(codice)) And 1) <> 0
            Dim frase As String
            Select Case codice
                Case VK_CAPITAL
                    frase = If(attivo, "Maiuscole attivate", "Maiuscole disattivate")
                Case &H90
                    frase = If(attivo, "Bloc Num attivato", "Bloc Num disattivato")
                Case Else
                    frase = If(attivo, "Bloc Scorr attivato", "Bloc Scorr disattivato")
            End Select
            _lettore.ParlaApp(frase, True)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.AnnunciaBlocco: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaTasto(stato As Object)
        Try
            Dim codice As Integer = VK_INSERT
            If stato IsNot Nothing Then codice = CInt(stato)
            Dim flag As Integer = 0
            If codice = VK_INSERT Then flag = NativoLettore.KEYEVENTF_EXTENDEDKEY
            NativoLettore.keybd_event(CByte(codice), 0, flag, IntPtr.Zero)
            NativoLettore.keybd_event(CByte(codice), 0, flag Or NativoLettore.KEYEVENTF_KEYUP, IntPtr.Zero)
            If codice = VK_CAPITAL Then _lettore.AccodaComando(ComandoLettore.StatoMaiuscole)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.InviaTasto: " & ex.Message)
        End Try
    End Sub

    Private Function CatturaRicerca(codice As Integer) As Boolean
        Try
            If codice = VK_RETURN Then
                _tastiSoppressi.Add(codice)
                _lettore.ConfermaRicerca()
                Return True
            End If
            If codice = VK_ESCAPE Then
                _tastiSoppressi.Add(codice)
                _lettore.AnnullaRicerca()
                Return True
            End If
            If codice = VK_BACK Then
                _tastiSoppressi.Add(codice)
                _lettore.CancellaRicerca()
                Return True
            End If
            If TastoGiu(VK_CONTROL) OrElse TastoGiu(VK_MENU) Then Return False
            Dim carattere As String = CarattereDaTasto(codice, TastoGiu(VK_SHIFT))
            If carattere = "" Then Return False
            _tastiSoppressi.Add(codice)
            _lettore.AccodaCarattereRicerca(carattere)
            Return True
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.CatturaRicerca: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CarattereDaTasto(codice As Integer, maiuscolo As Boolean) As String
        Try
            If codice >= VK_A AndAlso codice <= VK_Z Then
                Dim lettera As String = ChrW(codice).ToString()
                If Not maiuscolo Then lettera = lettera.ToLowerInvariant()
                Return lettera
            End If
            If codice >= &H30 AndAlso codice <= &H39 AndAlso Not maiuscolo Then Return ChrW(codice).ToString()
            If codice = VK_SPACE Then Return " "
            Return ""
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.CarattereDaTasto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ComandoTastierino(codice As Integer, scansione As Integer, ByRef comando As ComandoLettore) As Boolean
        Try
            Dim tasto As Integer = codice
            If (codice >= &H25 AndAlso codice <= &H28) OrElse codice = &HC Then
                Select Case scansione
                    Case &H4B : tasto = &H64
                    Case &H4D : tasto = &H66
                    Case &H48 : tasto = &H68
                    Case &H50 : tasto = &H62
                    Case &H4C : tasto = &H65
                End Select
            End If
            Select Case tasto
                Case &H68
                    comando = ComandoLettore.Contenitore
                Case &H62
                    comando = ComandoLettore.Contenuto
                Case &H64
                    comando = ComandoLettore.Precedente
                Case &H66
                    comando = ComandoLettore.Successivo
                Case &H65
                    comando = ComandoLettore.Focus
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ComandoTastierino: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function DescrizioneTastoLibero(codice As Integer, scansione As Integer) As String
        Try
            Dim comando As ComandoLettore
            If _lettore.CatturaTastierino() AndAlso ComandoTastierino(codice, scansione, comando) Then Return ImpostazioniLettore.Descrizione(comando)
            Return "tasto " & ImpostazioniLettore.NomeTasto(codice)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.DescrizioneTastoLibero: " & ex.Message)
            Return "nessuna azione"
        End Try
    End Function

    Private Function TraduciComando(codice As Integer, ctrl As Boolean, maiuscolo As Boolean, ByRef comando As ComandoLettore) As Boolean
        Try
            Dim rimappato As ComandoLettore
            If _lettore.ComandoDaGesto(codice, ctrl, maiuscolo, rimappato) Then
                comando = rimappato
                Return True
            End If
            Select Case codice
                Case VK_T
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Titolo
                Case VK_TAB
                    comando = ComandoLettore.Focus
                Case VK_F12
                    comando = ComandoLettore.Ora
                Case VK_F11
                    comando = ComandoLettore.Data
                Case VK_END
                    If ctrl Then Return False
                    comando = ComandoLettore.BarraStato
                Case VK_UP
                    If ctrl Then
                        comando = ComandoLettore.Contenitore
                    ElseIf maiuscolo Then
                        comando = ComandoLettore.Selezione
                    Else
                        comando = ComandoLettore.Riga
                    End If
                Case VK_DOWN
                    If ctrl Then
                        comando = ComandoLettore.Contenuto
                    ElseIf maiuscolo Then
                        comando = ComandoLettore.Selezione
                    Else
                        comando = ComandoLettore.LetturaContinua
                    End If
                Case VK_LEFT
                    If Not ctrl Then Return False
                    comando = ComandoLettore.Precedente
                Case VK_RIGHT
                    If Not ctrl Then Return False
                    comando = ComandoLettore.Successivo
                Case VK_B
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Finestra
                Case VK_Q
                    comando = ComandoLettore.AttivaDisattiva
                Case VK_SPACE
                    comando = If(maiuscolo, ComandoLettore.LettereRapide, ComandoLettore.NavigazioneWeb)
                Case VK_F1
                    comando = ComandoLettore.Aiuto
                Case VK_RETURN
                    comando = ComandoLettore.AttivaElemento
                Case VK_R
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.AggiornaNavigazione
                Case VK_D
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Descrizione
                Case VK_C
                    If ctrl Then Return False
                    comando = If(maiuscolo, ComandoLettore.Compita, ComandoLettore.Carattere)
                Case VK_W
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Parola
                Case VK_M
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Messaggio
                Case VK_Y
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.RigaTabella
                Case VK_J
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.ColonnaTabella
                Case VK_S
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Selezione
                Case &H31
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.AiutoTasti
                Case VK_F
                    If ctrl AndAlso Not maiuscolo Then
                        comando = ComandoLettore.CercaPagina
                    ElseIf maiuscolo AndAlso Not ctrl Then
                        comando = ComandoLettore.RevisionePiatta
                    ElseIf Not ctrl AndAlso Not maiuscolo Then
                        comando = ComandoLettore.Formattazione
                    Else
                        Return False
                    End If
                Case VK_P
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.PausaLettura
                Case VK_Z
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.TracciaMouse
                Case &H38
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.PortaMouse
                Case &H39
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Clicca
                Case &H37, &H76
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.ElencoElementi
                Case VK_F10
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.ScaricaAlbero
                Case &H30
                    If ctrl AndAlso maiuscolo Then Return False
                    comando = If(ctrl, ComandoLettore.SalvaProfilo, If(maiuscolo, ComandoLettore.TogliProfilo, ComandoLettore.Prolissita))
                Case VK_V
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Braille
                Case VK_O
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Ocr
                Case VK_L
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.MenuLettore
                Case VK_H
                    If ctrl AndAlso maiuscolo Then Return False
                    comando = If(ctrl, ComandoLettore.CopiaUltimo, If(maiuscolo, ComandoLettore.StoricoVoce, ComandoLettore.RipetiUltimo))
                Case VK_A
                    If ctrl OrElse maiuscolo Then Return False
                    comando = ComandoLettore.Appunti
                Case Else
                    ' Un tasto che il lettore non conosce può essere un comando di un componente aggiuntivo.
                    Dim idComponente As String = ComponentiAggiuntivi.CercaComando(codice, ctrl, maiuscolo, _lettore.NomeProgrammaPrimoPiano())
                    If idComponente = "" Then Return False
                    _idComandoComponente = idComponente
                    comando = ComandoLettore.ComponenteEsterno
            End Select
            If _lettore.IgnoraPredefinito(comando) Then Return False
            Return True
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.TraduciComando: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TastoVirtuale(codice As Integer, ctrl As Boolean) As Boolean
        Try
            Select Case codice
                Case VK_LEFT, VK_RIGHT, VK_UP, VK_DOWN, VK_HOME, VK_END, VK_PRIOR, VK_NEXT, VK_RETURN, VK_SPACE, VK_BACK, VK_DELETE
                    Return True
            End Select
            If ctrl Then Return codice = VK_C OrElse codice = VK_A OrElse codice = VK_INSERT
            If _lettore.LettereRapideSpente Then Return False
            If codice >= &H31 AndAlso codice <= &H36 Then Return True
            Select Case codice
                Case VK_H, VK_K, VK_B, VK_E, VK_F, VK_X, VK_R, VK_C, VK_L, VK_I, VK_T, VK_G, VK_D, VK_N
                    Return True
            End Select
            Return ETastoScrivibile(codice)
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.TastoVirtuale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ETastoModifica(codice As Integer, ctrl As Boolean, alt As Boolean, maiuscolo As Boolean) As Boolean
        Try
            If alt AndAlso Not ctrl Then Return False
            If codice = VK_BACK OrElse codice = VK_DELETE OrElse codice = VK_RETURN Then Return True
            If codice = VK_INSERT AndAlso (ctrl OrElse maiuscolo) Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ETastoModifica: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EModificatore(codice As Integer) As Boolean
        Try
            Select Case codice
                Case VK_SHIFT, VK_CONTROL, VK_MENU, VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN, VK_CAPITAL
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.EModificatore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ETastoNavigazione(codice As Integer) As Boolean
        Try
            Select Case codice
                Case VK_LEFT, VK_RIGHT, VK_UP, VK_DOWN, VK_HOME, VK_END, VK_PRIOR, VK_NEXT
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ETastoNavigazione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ETastoScrivibile(codice As Integer) As Boolean
        Try
            If codice = VK_SPACE Then Return True
            If codice >= &H30 AndAlso codice <= &H5A Then Return True
            If codice >= &H60 AndAlso codice <= &H6F Then Return True
            If codice >= &HBA AndAlso codice <= &HC0 Then Return True
            If codice >= &HDB AndAlso codice <= &HDF Then Return True
            If codice = &HE2 Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ETastoScrivibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TastoGiu(codice As Integer) As Boolean
        Try
            Return (NativoLettore.GetAsyncKeyState(codice) And &H8000S) <> 0
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.TastoGiu: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function AppartieneAlPrimoPiano(primoPiano As IntPtr, pidPrimoPiano As UInteger, threadPrimoPiano As UInteger, hwnd As IntPtr, threadEvento As UInteger) As Boolean
        Try
            If threadPrimoPiano = threadEvento Then Return True
            If hwnd = IntPtr.Zero Then Return False
            Dim pidEvento As UInteger = 0UI
            NativoLettore.GetWindowThreadProcessId(hwnd, pidEvento)
            If pidEvento <> 0UI AndAlso pidEvento = pidPrimoPiano Then Return True
            If NativoLettore.GetAncestor(hwnd, NativoLettore.GA_ROOT) = primoPiano Then Return True
            If NativoLettore.GetAncestor(hwnd, 3UI) = primoPiano Then Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AgganciLettore.AppartieneAlPrimoPiano", exAtteso)
        End Try
        Return False
    End Function

    Private Sub ProceduraEventiCallback(hWinEventHook As IntPtr, eventType As UInteger, hwnd As IntPtr, idObject As Integer, idChild As Integer, dwEventThread As UInteger, dwmsEventTime As UInteger)
        Try
            If Not _lettore.InEsecuzione Then Return
            If eventType = NativoLettore.EVENT_SYSTEM_FOREGROUND Then
                _riquadroGecko = False
                AggiornaAgganciGecko(hwnd)
                _lettore.AccodaPrimoPiano(hwnd)
                Return
            End If
            If eventType = EventoInizioMenu OrElse eventType = EventoInizioRiquadroMenu Then
                _lettore.InizioMenuSistema(hwnd)
                Return
            End If
            If eventType = EventoFineMenu OrElse eventType = EventoFineRiquadroMenu Then
                _lettore.FineMenuClassico()
                _lettore.FineMenuSistema()
                Return
            End If
            If eventType = EventoTestoRimossoIa2 Then Return
            Dim primoPiano As IntPtr = NativoLettore.GetForegroundWindow()
            If primoPiano = IntPtr.Zero Then Return
            Dim pid As UInteger = 0UI
            Dim threadPrimoPiano As UInteger = NativoLettore.GetWindowThreadProcessId(primoPiano, pid)
            Dim fuocoInMenu As Boolean = eventType = NativoLettore.EVENT_OBJECT_FOCUS AndAlso FinestraMenuEvento(hwnd)
            If Not fuocoInMenu AndAlso Not AppartieneAlPrimoPiano(primoPiano, pid, threadPrimoPiano, hwnd, dwEventThread) Then Return
            If eventType = EventoAvvisoSistema OrElse eventType = EventoMostraOggetto OrElse eventType = EventoDescrizioneOggetto OrElse eventType = EventoTestoInseritoIa2 OrElse eventType = EventoTestoAggiornatoIa2 Then
                _lettore.SegnalaRegioneVivaIa2(hwnd, idObject, idChild, eventType)
                Return
            End If
            If eventType = NativoLettore.EVENT_OBJECT_TEXTSELECTIONCHANGED Then
                _lettore.AccodaSelezione()
                Return
            End If
            If eventType = NativoLettore.EVENT_OBJECT_NAMECHANGE AndAlso idObject = NativoLettore.OBJID_WINDOW Then
                _lettore.AccodaTitolo(hwnd)
                Return
            End If
            If idObject = NativoLettore.OBJID_CARET OrElse idObject = NativoLettore.OBJID_CURSOR Then Return
            If eventType = NativoLettore.EVENT_OBJECT_FOCUS Then
                ControllaRiquadroGecko(hwnd, primoPiano)
                _lettore.AccodaFocusMsaa(hwnd, idObject, idChild)
                Return
            End If
            If eventType = NativoLettore.EVENT_OBJECT_VALUECHANGE Then _lettore.SegnalaValoreCambiato(hwnd, idObject, idChild)
            If eventType = NativoLettore.EVENT_OBJECT_STATECHANGE Then _lettore.SegnalaStatoCambiato(hwnd, idObject, idChild)
            If eventType = NativoLettore.EVENT_OBJECT_NAMECHANGE AndAlso _pidGecko <> 0UI AndAlso pid = _pidGecko Then _lettore.SegnalaRegioneVivaIa2(hwnd, idObject, idChild, eventType)
            If FinestraChromiumEvento(hwnd) Then Return
            _lettore.AccodaCambioValore()
        Catch ex As Exception
            log.ScriviLog("AgganciLettore.ProceduraEventiCallback: " & ex.Message)
        End Try
    End Sub

End Class