Imports System.ComponentModel
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Drawing.Text
Imports wgcontrols
Imports System.Diagnostics
Imports System.Linq
Imports System.Text

' Controllo del menu delle opzioni con intercettazione globale degli eventi di tastiera tramite ganci di sistema Windows.
Public Class WGMenuOpzioni
    Inherits UserControl

    Private utils As WGFormUtils = WGFormUtils.GetInstance()
    Private backnonconsentito As Boolean = False

    Private WithEvents Menuopzioni As WGListaView

    Private optionEventsOpzioni As New List(Of opzione)
    Private menuopzioniaperto As Boolean = False
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private writelog As New Logger
    Private ultimoelementoopzioni As Integer
    Private tastogiapremuto As Boolean = False

    Private ultimoTempoTasto As DateTime = DateTime.MinValue
    Private formAttivo As Boolean = True

    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const WH_KEYBOARD_LL As Integer = 13

    Private Delegate Function LowLevelKeyboardProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    Private _proc As LowLevelKeyboardProc
    Private _hookID As IntPtr = IntPtr.Zero

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function SetWindowsHookEx(idHook As Integer, lpfn As LowLevelKeyboardProc, hMod As IntPtr, dwThreadId As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(hhk As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function CallNextHookEx(hhk As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    Private Class opzione
        Public nome As String
        Public evento As EventHandler
    End Class

    Public Event chiudi As EventHandler
    Public Event opzioniaperte As EventHandler
    Public Event opzionichiuse As EventHandler
    Public Event TastoPremuto As EventHandler(Of KeyEventArgs)
    Public Event TastoRilasciato As EventHandler(Of KeyEventArgs)

    Private Sub AttivaHook()
        Try
            If _hookID = IntPtr.Zero Then
                _proc = AddressOf HookCallback
                Using curProcess As Process = Process.GetCurrentProcess()
                    Using curModule As ProcessModule = curProcess.MainModule
                        _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0)
                    End Using
                End Using
            End If
        Catch ex As Exception
            ScriviLogCompleto("AttivaHook - Errore", ex)
        End Try
    End Sub

    Private Sub DisattivaHook()
        Try
            If _hookID <> IntPtr.Zero Then
                UnhookWindowsHookEx(_hookID)
                _hookID = IntPtr.Zero
            End If
        Catch ex As Exception
            ScriviLogCompleto("DisattivaHook - Errore", ex)
        End Try
    End Sub

    Private Function HookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        Try
            If nCode >= 0 AndAlso (wParam = CType(WM_KEYDOWN, IntPtr) OrElse wParam = CType(WM_SYSKEYDOWN, IntPtr)) Then
                If formAttivo AndAlso (Me.ParentForm IsNot Nothing AndAlso Me.ParentForm.Visible) Then
                    Dim vkCode As Integer = Marshal.ReadInt32(lParam)
                    Dim keyData As Keys = CType(vkCode, Keys)
                    Dim altPremuto As Boolean = (Control.ModifierKeys And Keys.Alt) = Keys.Alt

                    If keyData = Keys.Tab AndAlso altPremuto Then
                        If menuopzioniaperto Then
                            Me.BeginInvoke(New Action(AddressOf ChiudiMenuOpzioni))
                        End If
                        Return CallNextHookEx(_hookID, nCode, wParam, lParam)
                    End If

                    If menuopzioniaperto Then
                        If keyData = Keys.Escape Then
                            Me.BeginInvoke(New Action(AddressOf HandleEscapeKey))
                            Return CType(1, IntPtr)
                        ElseIf keyData = Keys.Enter OrElse keyData = Keys.Return Then
                            Me.BeginInvoke(New Action(AddressOf HandleEnterKey))
                            Return CType(1, IntPtr)
                        End If
                    Else
                        If keyData = Keys.Menu OrElse keyData = Keys.LMenu OrElse keyData = Keys.RMenu OrElse (altPremuto AndAlso keyData <> Keys.Tab) Then
                            Me.BeginInvoke(New Action(AddressOf apriopzioni))
                            Return CType(1, IntPtr)
                        ElseIf keyData = Keys.Escape Then
                            Dim e As New KeyEventArgs(keyData Or Control.ModifierKeys)
                            Me.BeginInvoke(New Action(Of KeyEventArgs)(Sub(args)
                                                                           Me.OnKeyDown(args)
                                                                           RaiseEvent TastoPremuto(Me, args)
                                                                       End Sub), e)
                            Return CType(1, IntPtr)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("HookCallback - Errore", ex)
        End Try
        Return CallNextHookEx(_hookID, nCode, wParam, lParam)
    End Function

    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        Try
            If Me.ParentForm IsNot Nothing Then
                AddHandler Me.ParentForm.Activated, AddressOf ParentForm_Activated
                AddHandler Me.ParentForm.Deactivate, AddressOf ParentForm_Deactivate
                formAttivo = Me.ParentForm.ContainsFocus OrElse Me.ParentForm.Focused
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnLoad - Errore aggancio eventi form", ex)
        End Try
    End Sub

    Private Sub ParentForm_Activated(sender As Object, e As EventArgs)
        formAttivo = True
    End Sub

    Private Sub ParentForm_Deactivate(sender As Object, e As EventArgs)
        formAttivo = False
    End Sub

    Private Sub ScriviLogCompleto(messaggio As String, ex As Exception)
        Try
            If ex Is Nothing Then
                Return
            End If
            Dim stackTrace As New System.Diagnostics.StackTrace(ex, True)
            Dim frame As System.Diagnostics.StackFrame = stackTrace.GetFrame(0)
            Dim linea As Integer = 0
            Dim nomeFile As String = "N/A"
            Dim metodo As String = "N/A"
            If frame IsNot Nothing Then
                linea = frame.GetFileLineNumber()
                nomeFile = frame.GetFileName()
                metodo = frame.GetMethod()?.Name
            End If
            Dim eccezioneDettagliata As New StringBuilder()
            eccezioneDettagliata.Append($"{messaggio} - ")
            eccezioneDettagliata.Append($"Tipo: {ex.GetType().FullName} | ")
            eccezioneDettagliata.Append($"Messaggio: {ex.Message} | ")
            eccezioneDettagliata.Append($"HResult: 0x{ex.HResult:X8} | ")
            If ex.InnerException IsNot Nothing Then
                eccezioneDettagliata.Append($"InnerException: {ex.InnerException.Message} | ")
            End If
            eccezioneDettagliata.Append($"File: {nomeFile}, Linea: {linea}, Metodo: {metodo}")
        Catch logEx As Exception
            System.Diagnostics.Debug.WriteLine($"FALLIMENTO LOG CRITICO: {logEx.Message}")
        End Try
    End Sub

    Public Sub New()
        Try
            Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint, True)
            Me.UpdateStyles()
            inizializza()
        Catch ex As Exception
            ScriviLogCompleto("WGMenuOpzioni.New - Errore critico nel costruttore", ex)
        End Try
    End Sub

    Private Sub inizializza()
        Try
            AttivaHook()
            Me.Dock = DockStyle.Fill
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            ScriviLogCompleto("inizializza - Errore di operazione non valida", ex)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore: " & ex.Message)
            messaggio.ShowDialog()
            Application.DoEvents()
            Application.Exit()
        Catch ex As NullReferenceException
            ScriviLogCompleto("inizializza - Errore di riferimento nullo", ex)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore: " & ex.Message)
            messaggio.ShowDialog()
            Application.DoEvents()
            Application.Exit()
        Catch ex As Exception
            ScriviLogCompleto("inizializza - Errore generico", ex)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore: " & ex.Message)
            messaggio.ShowDialog()
            Application.DoEvents()
            Application.Exit()
        End Try
    End Sub

    Private Sub CaricaListaOpzioni()
        Try
            Menuopzioni = New WGListaView()
            Menuopzioni.Titolo = "Opzioni"
            Menuopzioni.SelectionMode = SelectionMode.One
            Menuopzioni.Dock = DockStyle.Fill

            For Each opz As opzione In optionEventsOpzioni
                Menuopzioni.Items.Add(opz.nome)
            Next

            If lettura.NvdaInEsecuzione() Then
                Menuopzioni.AccessibleRole = AccessibleRole.None
            Else
                Menuopzioni.Enabled = False
            End If

            AddHandler Menuopzioni.SelectedIndexChanged, AddressOf menuopzioni_SelectedIndexChanged
            Me.Controls.Add(Menuopzioni)
        Catch ex As Exception
            ScriviLogCompleto("CaricaListaOpzioni - Errore", ex)
        End Try
    End Sub

    Public Sub apriopzioni()
        Try
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf apriopzioni))
                Return
            End If
            If menuopzioniaperto Then
                Exit Sub
            Else
                If optionEventsOpzioni.Count > 0 Then
                    If Menuopzioni Is Nothing Then
                        CaricaListaOpzioni()
                    End If

                    menuopzioniaperto = True
                    Menuopzioni.Visible = True
                    If Not lettura.NvdaInEsecuzione() Then
                        Menuopzioni.Enabled = True
                        Menuopzioni.AccessibleRole = AccessibleRole.None
                    End If
                    Menuopzioni.Focus()
                    Menuopzioni.SelectedIndex = 0
                    RaiseEvent opzioniaperte(Me, Nothing)
                Else
                    Dim visualizza As New WGMessaggi()
                    visualizza.Messaggio("Non sono presenti opzioni per questa funzionalità")
                    visualizza.Show()
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("apriopzioni - Errore", ex)
        End Try
    End Sub

    Private Sub HandleEnterKey()
        Try
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf HandleEnterKey))
                Return
            End If
            If menuopzioniaperto AndAlso Menuopzioni IsNot Nothing AndAlso Menuopzioni.SelectedItem IsNot Nothing Then
                Dim testoVoce As String = Menuopzioni.SelectedItem.ToString()
                Dim op As opzione = optionEventsOpzioni.FirstOrDefault(Function(x) x.nome = testoVoce)
                If op IsNot Nothing Then
                    op.evento.Invoke(Me, EventArgs.Empty)
                End If
                ChiudiMenuOpzioni()
            End If
        Catch ex As Exception
            ScriviLogCompleto("HandleEnterKey - Errore", ex)
        End Try
    End Sub

    Private Sub HandleEscapeKey()
        Try
            If Me.InvokeRequired Then
                Me.Invoke(New MethodInvoker(AddressOf HandleEscapeKey))
                Return
            End If
            If menuopzioniaperto = True Then
                ChiudiMenuOpzioni()
            Else
                RaiseEvent chiudi(Me, Nothing)
            End If
        Catch ex As Exception
            ScriviLogCompleto("HandleEscapeKey - Errore", ex)
        End Try
    End Sub

    Private Sub ChiudiMenuOpzioni()
        Try
            menuopzioniaperto = False
            If Menuopzioni IsNot Nothing Then
                Menuopzioni.Visible = False
                If Not lettura.NvdaInEsecuzione() Then
                    Menuopzioni.Enabled = False
                End If

                RemoveHandler Menuopzioni.SelectedIndexChanged, AddressOf menuopzioni_SelectedIndexChanged
                Me.Controls.Remove(Menuopzioni)
                Menuopzioni.Dispose()
                Menuopzioni = Nothing
            End If
            RaiseEvent opzionichiuse(Me, Nothing)
        Catch ex As Exception
            ScriviLogCompleto("ChiudiMenuOpzioni - Errore", ex)
        End Try
    End Sub

    Public Sub aggiungiopzione(testoVoce As String, evento As EventHandler)
        Try
            Dim esiste As Boolean = optionEventsOpzioni.Any(Function(x) x.nome = testoVoce)
            If Not esiste Then
                optionEventsOpzioni.Add(New opzione With {.nome = testoVoce, .evento = evento})
                If Menuopzioni IsNot Nothing Then
                    If Menuopzioni.InvokeRequired Then
                        Menuopzioni.Invoke(New Action(Of String)(AddressOf Menuopzioni.Items.Add), testoVoce)
                    Else
                        Menuopzioni.Items.Add(testoVoce)
                    End If
                End If
            Else
            End If
        Catch ex As Exception
            ScriviLogCompleto("aggiungiopzione - Errore", ex)
        End Try
    End Sub

    Public Function opzioneesiste(nome As String) As Boolean
        Try
            Return optionEventsOpzioni.Any(Function(x) x.nome = nome)
        Catch ex As Exception
            ScriviLogCompleto("opzioneesiste - Errore", ex)
            Return False
        End Try
    End Function

    Public Sub aggiungiopzionedopo(nomeOpzioneEsistente As String, nuovoNomeOpzione As String, nuovoEvento As EventHandler)
        Try
            Dim index As Integer = optionEventsOpzioni.FindIndex(Function(x) x.nome = nomeOpzioneEsistente)
            If index > -1 Then
                Dim esisteNuovo As Boolean = optionEventsOpzioni.Any(Function(x) x.nome = nuovoNomeOpzione)
                If Not esisteNuovo Then
                    optionEventsOpzioni.Insert(index + 1, New opzione With {.nome = nuovoNomeOpzione, .evento = nuovoEvento})
                    If Menuopzioni IsNot Nothing Then
                        If Menuopzioni.InvokeRequired Then
                            Menuopzioni.Invoke(Sub() Menuopzioni.Items.Insert(index + 1, nuovoNomeOpzione))
                        Else
                            Menuopzioni.Items.Insert(index + 1, nuovoNomeOpzione)
                        End If
                    End If
                Else
                End If
            Else
                aggiungiopzione(nuovoNomeOpzione, nuovoEvento)
            End If
        Catch ex As Exception
            ScriviLogCompleto("aggiungiopzionedopo - Errore", ex)
        End Try
    End Sub

    Public Sub sostituireopzione(nomeOpzioneEsistente As String, nuovoNomeOpzione As String, nuovoEvento As EventHandler)
        Try
            Dim index As Integer = optionEventsOpzioni.FindIndex(Function(x) x.nome = nomeOpzioneEsistente)
            If index > -1 Then
                optionEventsOpzioni(index) = New opzione With {.nome = nuovoNomeOpzione, .evento = nuovoEvento}
                If Menuopzioni IsNot Nothing Then
                    If Menuopzioni.InvokeRequired Then
                        Menuopzioni.Invoke(Sub() Menuopzioni.Items(index) = nuovoNomeOpzione)
                    Else
                        Menuopzioni.Items(index) = nuovoNomeOpzione
                    End If
                End If
            Else
            End If
        Catch ex As Exception
            ScriviLogCompleto("sostituireopzione - Errore", ex)
        End Try
    End Sub

    Private Sub menuopzioni_SelectedIndexChanged(sender As Object, e As EventArgs)
        Try
            If DesignMode Then Exit Sub
            If Menuopzioni IsNot Nothing AndAlso Menuopzioni.Items.Count > 0 AndAlso Menuopzioni.SelectedItem IsNot Nothing Then
                ultimoelementoopzioni = Menuopzioni.SelectedIndex
            ElseIf Menuopzioni IsNot Nothing AndAlso Menuopzioni.Items.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non ci sono voci da visualizzare")
                messaggio.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("menuopzioni_SelectedIndexChanged - Errore", ex)
        End Try
    End Sub

    Private Sub RimuoviEvento(chiave As String)
        Try
            Dim op As opzione = optionEventsOpzioni.FirstOrDefault(Function(x) x.nome = chiave)
            If op IsNot Nothing Then
                optionEventsOpzioni.Remove(op)
            End If
        Catch ex As Exception
            ScriviLogCompleto("RimuoviEvento - Errore", ex)
        End Try
    End Sub

    Public Sub rimuoviopzione(nome As String)
        Try
            RimuoviEvento(nome)
            If Menuopzioni IsNot Nothing Then
                If Menuopzioni.InvokeRequired Then
                    Menuopzioni.Invoke(New Action(Of String)(AddressOf rimuoviopzione), nome)
                Else
                    If Menuopzioni.Items.Contains(nome) Then
                        Menuopzioni.Items.Remove(nome)
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("rimuoviopzione - Errore", ex)
        End Try
    End Sub

    Public Sub abbandona()
        Try
            If Me.ParentForm IsNot Nothing Then
                RemoveHandler Me.ParentForm.Activated, AddressOf ParentForm_Activated
                RemoveHandler Me.ParentForm.Deactivate, AddressOf ParentForm_Deactivate
            End If
            DisattivaHook()
            ChiudiMenuOpzioni()
        Catch ex As Exception
            ScriviLogCompleto("abbandona - Errore", ex)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Si è verificato un errore durante l'abbandono: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Public Sub pulisci()
        Try
            If Menuopzioni IsNot Nothing Then
                Menuopzioni.Items.Clear()
            End If
            optionEventsOpzioni.Clear()
        Catch ex As Exception
            ScriviLogCompleto("pulisci - Errore", ex)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing Then
                DisattivaHook()
                If Menuopzioni IsNot Nothing Then
                    Menuopzioni.Dispose()
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("Dispose - Errore", ex)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class