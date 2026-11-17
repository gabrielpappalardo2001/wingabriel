Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text

' Interfaccia e supporto per la lettura di applicazioni basate su piattaforma Java.
Friend NotInheritable Class LetturaJava

    Private Const LunghezzaNome As Integer = 1024
    Private Const LunghezzaRuolo As Integer = 256

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure InfoContestoJava
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaNome)> Public name As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaNome)> Public description As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaRuolo)> Public role As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaRuolo)> Public role_en_US As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaRuolo)> Public states As String
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=LunghezzaRuolo)> Public states_en_US As String
        Public indexInParent As Integer
        Public childrenCount As Integer
        Public x As Integer
        Public y As Integer
        Public width As Integer
        Public height As Integer
        Public accessibleComponent As Integer
        Public accessibleAction As Integer
        Public accessibleSelection As Integer
        Public accessibleText As Integer
        Public accessibleInterfaces As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure InfoTestoJava
        Public charCount As Integer
        Public caretIndex As Integer
        Public indexAtPoint As Integer
    End Structure

    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub DelFuoco(idVm As Integer, evento As Long, sorgente As Long)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub DelImpostaFuoco(callback As DelFuoco)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub DelAvvia()
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function DelInfoContesto(idVm As Integer, contesto As Long, ByRef info As InfoContestoJava) As Integer
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Function DelInfoTesto(idVm As Integer, contesto As Long, ByRef info As InfoTestoJava) As Integer
    <UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet:=CharSet.Unicode)>
    Private Delegate Function DelIntervalloTesto(idVm As Integer, contesto As Long, inizio As Integer, fine As Integer, testo As StringBuilder, lunghezza As Short) As Integer
    <UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet:=CharSet.Unicode)>
    Private Delegate Function DelValore(idVm As Integer, contesto As Long, valore As StringBuilder, lunghezza As Short) As Integer
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)>
    Private Delegate Sub DelRilascia(idVm As Integer, oggetto As Long)

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibrary(nome As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Ansi, SetLastError:=True)>
    Private Shared Function GetProcAddress(modulo As IntPtr, nome As String) As IntPtr
    End Function

    Private Shared _caricata As Boolean
    Private Shared _attiva As Boolean
    Private Shared _fuoco As DelFuoco
    Private Shared _infoContesto As DelInfoContesto
    Private Shared _infoTesto As DelInfoTesto
    Private Shared _intervalloTesto As DelIntervalloTesto
    Private Shared _valore As DelValore
    Private Shared _rilascia As DelRilascia
    Private Shared _destinazione As Action(Of String)
    Private Shared _ultimaFrase As String = ""
    Private Shared _ultimaFraseTick As Integer

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property Attiva As Boolean
        Get
            Return _attiva
        End Get
    End Property

    Public Shared Sub Avvia(destinazione As Action(Of String))
        Try
            If _attiva Then Return
            _destinazione = destinazione
            Dim nomi As String() = If(Environment.Is64BitProcess, New String() {"WindowsAccessBridge-64.dll", "WindowsAccessBridge.dll"}, New String() {"WindowsAccessBridge-32.dll", "WindowsAccessBridge.dll"})
            Dim modulo As IntPtr = IntPtr.Zero
            For Each nome As String In nomi
                modulo = LoadLibrary(nome)
                If modulo <> IntPtr.Zero Then Exit For
            Next
            If modulo = IntPtr.Zero Then Return
            Dim avvia As DelAvvia = Funzione(Of DelAvvia)(modulo, "Windows_run")
            Dim imposta As DelImpostaFuoco = Funzione(Of DelImpostaFuoco)(modulo, "setFocusGainedFP")
            _infoContesto = Funzione(Of DelInfoContesto)(modulo, "getAccessibleContextInfo")
            _infoTesto = Funzione(Of DelInfoTesto)(modulo, "getAccessibleTextInfo")
            _intervalloTesto = Funzione(Of DelIntervalloTesto)(modulo, "getAccessibleTextRange")
            _valore = Funzione(Of DelValore)(modulo, "getCurrentAccessibleValueFromContext")
            _rilascia = Funzione(Of DelRilascia)(modulo, "releaseJavaObject")
            If avvia Is Nothing OrElse imposta Is Nothing OrElse _infoContesto Is Nothing Then Return
            _fuoco = New DelFuoco(AddressOf AlFuoco)
            imposta(_fuoco)
            avvia()
            _attiva = True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaJava.Avvia", exAtteso)
            _attiva = False
        End Try
    End Sub

    Private Shared Sub AlFuoco(idVm As Integer, evento As Long, sorgente As Long)
        Try
            Dim frase As String = Descrivi(idVm, sorgente)
            If _rilascia IsNot Nothing Then
                _rilascia(idVm, evento)
                _rilascia(idVm, sorgente)
            End If
            If frase = "" Then Return
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(frase, _ultimaFrase, StringComparison.Ordinal) AndAlso adesso - _ultimaFraseTick < 400 Then Return
            _ultimaFrase = frase
            _ultimaFraseTick = adesso
            If _destinazione IsNot Nothing Then _destinazione.Invoke(frase)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaJava.AlFuoco", exAtteso)
        End Try
    End Sub

    Private Shared Function Descrivi(idVm As Integer, contesto As Long) As String
        Try
            Dim info As New InfoContestoJava()
            If _infoContesto(idVm, contesto, info) = 0 Then Return ""
            Dim parti As New List(Of String)()
            Dim nome As String = Pulisci(info.name)
            Dim ruolo As String = RuoloItaliano(info.role_en_US)
            Dim stati As String = StatiItaliani(info.states_en_US)
            Dim password As Boolean = String.Equals(info.role_en_US, "password text", StringComparison.OrdinalIgnoreCase)
            If nome <> "" Then parti.Add(nome)
            If ruolo <> "" Then parti.Add(ruolo)
            If Not password Then
                Dim valore As String = ValoreOggetto(idVm, contesto, info)
                If valore <> "" AndAlso Not String.Equals(valore, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(valore)
            Else
                parti.Add("protetto")
            End If
            If stati <> "" Then parti.Add(stati)
            Dim descrizione As String = Pulisci(info.description)
            If descrizione <> "" AndAlso Not String.Equals(descrizione, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(descrizione)
            If info.indexInParent >= 0 AndAlso (info.role_en_US = "page tab" OrElse info.role_en_US = "list item") Then parti.Add((info.indexInParent + 1).ToString())
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaJava.Descrivi", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function ValoreOggetto(idVm As Integer, contesto As Long, info As InfoContestoJava) As String
        Try
            If info.accessibleText <> 0 AndAlso _infoTesto IsNot Nothing AndAlso _intervalloTesto IsNot Nothing Then
                Dim testoInfo As New InfoTestoJava()
                If _infoTesto(idVm, contesto, testoInfo) <> 0 AndAlso testoInfo.charCount > 0 Then
                    Dim fine As Integer = Math.Min(testoInfo.charCount - 1, 299)
                    Dim buffer As New StringBuilder(fine + 2)
                    If _intervalloTesto(idVm, contesto, 0, fine, buffer, CShort(fine + 2)) <> 0 Then Return Pulisci(buffer.ToString())
                End If
            End If
            Select Case info.role_en_US
                Case "slider", "spinner", "progress bar", "scroll bar"
                    If _valore IsNot Nothing Then
                        Dim buffer As New StringBuilder(64)
                        If _valore(idVm, contesto, buffer, 64S) <> 0 Then Return Pulisci(buffer.ToString())
                    End If
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaJava.ValoreOggetto", exAtteso)
        End Try
        Return ""
    End Function

    Private Shared Function RuoloItaliano(nomeRuolo As String) As String
        Select Case If(nomeRuolo, "").ToLowerInvariant()
            Case "push button" : Return "pulsante"
            Case "toggle button" : Return "pulsante a due stati"
            Case "check box" : Return "casella di controllo"
            Case "radio button" : Return "pulsante di opzione"
            Case "text", "text field", "password text" : Return "campo di modifica"
            Case "combo box" : Return "casella combinata"
            Case "list" : Return "elenco"
            Case "list item" : Return "voce di elenco"
            Case "table" : Return "tabella"
            Case "tree" : Return "albero"
            Case "page tab" : Return "scheda"
            Case "page tab list" : Return "gruppo di schede"
            Case "menu" : Return "menu"
            Case "menu item" : Return "voce di menu"
            Case "menu bar" : Return "barra dei menu"
            Case "popup menu" : Return "menu di scelta rapida"
            Case "label" : Return "etichetta"
            Case "slider" : Return "dispositivo di scorrimento"
            Case "progress bar" : Return "barra di avanzamento"
            Case "scroll bar" : Return "barra di scorrimento"
            Case "spinner" : Return "selettore numerico"
            Case "tool bar" : Return "barra degli strumenti"
            Case "status bar" : Return "barra di stato"
            Case "dialog", "alert" : Return "finestra di dialogo"
            Case "frame", "window" : Return "finestra"
            Case "internal frame" : Return "finestra interna"
            Case "hyperlink" : Return "link"
            Case "tool tip" : Return "descrizione comando"
            Case "table cell", "cell renderer" : Return ""
        End Select
        Return ""
    End Function

    Private Shared Function StatiItaliani(elencoStati As String) As String
        Dim parti As New List(Of String)()
        Dim presenti As String = "," & If(elencoStati, "").ToLowerInvariant().Replace(" ", "") & ","
        If presenti.Contains(",checked,") Then parti.Add("spuntato")
        If presenti.Contains(",pressed,") Then parti.Add("premuto")
        If presenti.Contains(",expanded,") Then parti.Add("espanso")
        If presenti.Contains(",collapsed,") Then parti.Add("compresso")
        If presenti.Contains(",selected,") Then parti.Add("selezionato")
        If presenti.Contains(",indeterminate,") Then parti.Add("parziale")
        If presenti.Contains(",readonly,") OrElse (presenti.Contains(",text,") AndAlso Not presenti.Contains(",editable,") AndAlso presenti.Contains(",focusable,")) Then parti.Add("sola lettura")
        If Not presenti.Contains(",enabled,") AndAlso presenti.Length > 2 Then parti.Add("non disponibile")
        Return String.Join(", ", parti)
    End Function

    Private Shared Function Pulisci(testo As String) As String
        If testo Is Nothing Then Return ""
        Dim fine As Integer = testo.IndexOf(ChrW(0))
        If fine >= 0 Then testo = testo.Substring(0, fine)
        Return testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

    Private Shared Function Funzione(Of T As Class)(modulo As IntPtr, nome As String) As T
        Try
            Dim indirizzo As IntPtr = GetProcAddress(modulo, nome)
            If indirizzo = IntPtr.Zero Then Return Nothing
            Return TryCast(Marshal.GetDelegateForFunctionPointer(indirizzo, GetType(T)), T)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("LetturaJava.Funzione", exAtteso)
            Return Nothing
        End Try
    End Function

End Class
