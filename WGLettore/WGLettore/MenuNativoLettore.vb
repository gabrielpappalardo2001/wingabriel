Imports System.Runtime.InteropServices
Imports System.Threading

''' <summary>Una voce del menu dell'icona: il testo detto e quello che si fa scegliendola.</summary>
Friend NotInheritable Class VoceMenuNativo
    Public ReadOnly Testo As String
    Public ReadOnly Azione As Action

    Public Sub New(testo As String, azione As Action)
        Me.Testo = testo
        Me.Azione = azione
    End Sub
End Class

''' <summary>
''' Il menu dell'icona del lettore, come menu nativo di Windows: tastiera, Esc e mouse li gestisce il sistema, sempre allo stesso modo.
''' Lo annuncia il programma, voce per voce, con il messaggio che Windows manda alla finestra proprietaria a ogni voce evidenziata.
''' </summary>
Friend NotInheritable Class MenuNativoLettore
    Inherits NativeWindow

    ''' <param name="testo">Il testo della voce evidenziata; vuoto se all'apertura nessuna voce risulta evidenziata.</param>
    ''' <param name="apertura">Vero per la prima frase dopo l'apertura del menu.</param>
    Public Event VoceEvidenziata(testo As String, apertura As Boolean)

    <DllImport("user32.dll")>
    Private Shared Function CreatePopupMenu() As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function AppendMenu(menu As IntPtr, flag As UInteger, id As UIntPtr, testo As String) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function TrackPopupMenuEx(menu As IntPtr, flag As UInteger, x As Integer, y As Integer, finestra As IntPtr, parametri As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DestroyMenu(menu As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function PostMessage(finestra As IntPtr, messaggio As UInteger, w As IntPtr, l As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(finestra As IntPtr, messaggio As UInteger, w As IntPtr, l As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Sub keybd_event(tasto As Byte, scansione As Byte, flag As UInteger, extra As UIntPtr)
    End Sub

    Private Const WM_MENUSELECT As Integer = &H11F
    Private Const WM_CANCELMODE As UInteger = &H1FUI
    Private Const TPM_RETURNCMD As UInteger = &H100UI
    Private Const MF_HILITE As Integer = &H80
    Private Const VK_DOWN As Byte = &H28

    Private _voci As List(Of VoceMenuNativo)
    Private _aperto As Boolean
    Private _apertura As Boolean
    Private _evidenziata As Integer

    Public ReadOnly Property Aperto As Boolean
        Get
            Return _aperto
        End Get
    End Property

    ''' <summary>Mostra il menu nel punto del cursore e aspetta la scelta. Nothing se si esce senza scegliere.</summary>
    Public Function Mostra(voci As List(Of VoceMenuNativo), proprietario As Form) As VoceMenuNativo
        If voci Is Nothing OrElse voci.Count = 0 OrElse proprietario Is Nothing Then Return Nothing
        Dim menu As IntPtr = IntPtr.Zero
        Try
            _voci = voci
            _aperto = True
            _apertura = True
            _evidenziata = 0
            AssignHandle(proprietario.Handle)
            menu = CreatePopupMenu()
            For indice As Integer = 0 To voci.Count - 1
                AppendMenu(menu, 0UI, New UIntPtr(CUInt(indice + 1)), voci(indice).Testo)
            Next
            ' Aperto con un comando, il menu parte senza voce evidenziata: si porta sulla prima, come aprendolo dall'icona con la tastiera.
            Dim avvio As New Thread(AddressOf EvidenziaLaPrima)
            avvio.IsBackground = True
            avvio.Start()
            Dim posizione As Point = Cursor.Position
            Dim scelta As Integer = TrackPopupMenuEx(menu, TPM_RETURNCMD, posizione.X, posizione.Y, proprietario.Handle, IntPtr.Zero)
            PostMessage(proprietario.Handle, 0UI, IntPtr.Zero, IntPtr.Zero)
            If scelta >= 1 AndAlso scelta <= voci.Count Then Return voci(scelta - 1)
            Return Nothing
        Catch ex As Exception
            Trace.WriteLine("MenuNativoLettore.Mostra: " & ex.Message)
            Return Nothing
        Finally
            If menu <> IntPtr.Zero Then DestroyMenu(menu)
            _aperto = False
            If Handle <> IntPtr.Zero Then ReleaseHandle()
        End Try
    End Function

    ''' <summary>Chiude il menu aperto, senza scegliere niente.</summary>
    Public Sub Chiudi()
        Try
            If _aperto AndAlso Handle <> IntPtr.Zero Then SendMessage(Handle, WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            Trace.WriteLine("MenuNativoLettore.Chiudi: " & ex.Message)
        End Try
    End Sub

    Private Sub EvidenziaLaPrima()
        Try
            Thread.Sleep(120)
            If Not _aperto Then Return
            keybd_event(VK_DOWN, 0, 1UI, UIntPtr.Zero)
            keybd_event(VK_DOWN, 0, 3UI, UIntPtr.Zero)
            Thread.Sleep(500)
            If _aperto AndAlso _apertura Then
                _apertura = False
                RaiseEvent VoceEvidenziata("", True)
            End If
        Catch ex As Exception
            Trace.WriteLine("MenuNativoLettore.EvidenziaLaPrima: " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub WndProc(ByRef m As Message)
        If m.Msg = WM_MENUSELECT Then
            Try
                Dim id As Integer = CInt(m.WParam.ToInt64() And &HFFFFL)
                Dim flag As Integer = CInt((m.WParam.ToInt64() >> 16) And &HFFFFL)
                If _aperto AndAlso (flag And MF_HILITE) <> 0 AndAlso id >= 1 AndAlso id <= _voci.Count AndAlso id <> _evidenziata Then
                    _evidenziata = id
                    Dim apertura As Boolean = _apertura
                    _apertura = False
                    RaiseEvent VoceEvidenziata(_voci(id - 1).Testo, apertura)
                End If
            Catch ex As Exception
                Trace.WriteLine("MenuNativoLettore.WndProc: " & ex.Message)
            End Try
        End If
        MyBase.WndProc(m)
    End Sub
End Class
