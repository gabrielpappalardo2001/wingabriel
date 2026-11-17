Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports UIAutomationClient

' Accesso alla memoria dei processi remoti e lettura delle strutture native per liste e alberi di Windows.
Friend NotInheritable Class ControlliNativi

    Private Const PROCESS_VM_OPERATION As UInteger = &H8UI
    Private Const PROCESS_VM_READ As UInteger = &H10UI
    Private Const PROCESS_VM_WRITE As UInteger = &H20UI
    Private Const PROCESS_QUERY_INFORMATION As UInteger = &H400UI
    Private Const MEM_COMMIT As UInteger = &H1000UI
    Private Const MEM_RESERVE As UInteger = &H2000UI
    Private Const MEM_RELEASE As UInteger = &H8000UI
    Private Const PAGE_READWRITE As UInteger = &H4UI
    Private Const GWL_STYLE As Integer = -16

    Private Const LVM_GETITEMCOUNT As Integer = &H1004
    Private Const LVM_GETNEXTITEM As Integer = &H100C
    Private Const LVM_GETHEADER As Integer = &H101F
    Private Const LVM_GETITEMSTATE As Integer = &H102C
    Private Const LVM_GETCOLUMNORDERARRAY As Integer = &H103B
    Private Const LVM_GETEXTENDEDLISTVIEWSTYLE As Integer = &H1037
    Private Const LVM_GETVIEW As Integer = &H108F
    Private Const LVM_GETCOLUMNW As Integer = &H105F
    Private Const LVM_GETITEMTEXTW As Integer = &H1073
    Private Const HDM_GETITEMCOUNT As Integer = &H1200
    Private Const LVNI_FOCUSED As Integer = 1
    Private Const LVNI_SELECTED As Integer = 2
    Private Const LVIS_SELECTED As Integer = 2
    Private Const LVIS_STATEIMAGEMASK As Integer = &HF000
    Private Const LVS_EX_CHECKBOXES As Integer = 4
    Private Const LVCF_WIDTH As Integer = 2
    Private Const LVCF_TEXT As Integer = 4

    Private Const TVM_GETNEXTITEM As Integer = &H110A
    Private Const TVM_GETITEMW As Integer = &H113E
    Private Const TVGN_NEXT As Integer = 1
    Private Const TVGN_PREVIOUS As Integer = 2
    Private Const TVGN_PARENT As Integer = 3
    Private Const TVGN_CHILD As Integer = 4
    Private Const TVGN_CARET As Integer = 9
    Private Const TVIF_TEXT As Integer = 1
    Private Const TVIF_STATE As Integer = 8
    Private Const TVIF_HANDLE As Integer = &H10
    Private Const TVIF_CHILDREN As Integer = &H40
    Private Const TVIS_SELECTED As Integer = 2
    Private Const TVIS_EXPANDED As Integer = &H20
    Private Const TVIS_STATEIMAGEMASK As Integer = &HF000
    Private Const TVS_CHECKBOXES As Integer = &H100

    Private Const LimiteColonne As Integer = 24
    Private Const LimiteTesto As Integer = 260
    Private Const LimiteFratelli As Integer = 400

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function VirtualAllocEx(processo As IntPtr, indirizzo As IntPtr, dimensione As UIntPtr, tipo As UInteger, protezione As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function VirtualFreeEx(processo As IntPtr, indirizzo As IntPtr, dimensione As UIntPtr, tipo As UInteger) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WriteProcessMemory(processo As IntPtr, indirizzo As IntPtr, dati As Byte(), dimensione As UIntPtr, ByRef scritti As UIntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function ReadProcessMemory(processo As IntPtr, indirizzo As IntPtr, dati As Byte(), dimensione As UIntPtr, ByRef letti As UIntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function IsWow64Process(processo As IntPtr, ByRef wow64 As Boolean) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowLongW")>
    Private Shared Function GetWindowLong(hwnd As IntPtr, indice As Integer) As Integer
    End Function

    Public Structure RigaNativa
        Public Nome As String
        Public Colonne As List(Of KeyValuePair(Of String, String))
        Public Indice As Integer
        Public Totale As Integer
        Public Selezionata As Boolean
        Public ConCasella As Boolean
        Public Spuntata As Boolean
    End Structure

    Public Structure NodoNativo
        Public Nome As String
        Public Livello As Integer
        Public ConFigli As Boolean
        Public Aperto As Boolean
        Public Selezionato As Boolean
        Public ConCasella As Boolean
        Public Spuntato As Boolean
        Public Indice As Integer
        Public Totale As Integer
    End Structure

    Private NotInheritable Class ProcessoRemoto
        Implements IDisposable

        Private _maniglia As IntPtr
        Private ReadOnly _blocchi As New List(Of IntPtr)()
        Public ReadOnly A64 As Boolean

        Private Sub New(maniglia As IntPtr, a64 As Boolean)
            _maniglia = maniglia
            Me.A64 = a64
        End Sub

        Public ReadOnly Property DimensioneIndirizzo As Integer
            Get
                Return If(A64, 8, 4)
            End Get
        End Property

        Public Shared Function Apri(finestra As IntPtr) As ProcessoRemoto
            Dim maniglia As IntPtr = IntPtr.Zero
            Try
                If finestra = IntPtr.Zero Then Return Nothing
                Dim pid As UInteger = 0UI
                NativoLettore.GetWindowThreadProcessId(finestra, pid)
                If pid = 0UI Then Return Nothing
                maniglia = NativoLettore.OpenProcess(PROCESS_VM_OPERATION Or PROCESS_VM_READ Or PROCESS_VM_WRITE Or PROCESS_QUERY_INFORMATION, False, pid)
                If maniglia = IntPtr.Zero Then Return Nothing
                Dim wow64 As Boolean = False
                IsWow64Process(maniglia, wow64)
                Dim a64 As Boolean = Environment.Is64BitOperatingSystem AndAlso Not wow64
                Return New ProcessoRemoto(maniglia, a64)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("ControlliNativi.ProcessoRemoto.Apri", exAtteso)
                If maniglia <> IntPtr.Zero Then NativoLettore.CloseHandle(maniglia)
                Return Nothing
            End Try
        End Function

        Public Function Alloca(byteTotali As Integer) As IntPtr
            Try
                Dim blocco As IntPtr = VirtualAllocEx(_maniglia, IntPtr.Zero, New UIntPtr(CUInt(byteTotali)), MEM_COMMIT Or MEM_RESERVE, PAGE_READWRITE)
                If blocco <> IntPtr.Zero Then _blocchi.Add(blocco)
                Return blocco
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("ControlliNativi.ProcessoRemoto.Alloca", exAtteso)
                Return IntPtr.Zero
            End Try
        End Function

        Public Function Scrivi(indirizzo As IntPtr, dati As Byte()) As Boolean
            Try
                Dim scritti As UIntPtr = UIntPtr.Zero
                Return WriteProcessMemory(_maniglia, indirizzo, dati, New UIntPtr(CUInt(dati.Length)), scritti)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("ControlliNativi.ProcessoRemoto.Scrivi", exAtteso)
                Return False
            End Try
        End Function

        Public Function Leggi(indirizzo As IntPtr, quanti As Integer) As Byte()
            Try
                Dim dati(quanti - 1) As Byte
                Dim letti As UIntPtr = UIntPtr.Zero
                If Not ReadProcessMemory(_maniglia, indirizzo, dati, New UIntPtr(CUInt(quanti)), letti) Then Return Nothing
                Return dati
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("ControlliNativi.ProcessoRemoto.Leggi", exAtteso)
                Return Nothing
            End Try
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                For Each blocco As IntPtr In _blocchi
                    VirtualFreeEx(_maniglia, blocco, UIntPtr.Zero, MEM_RELEASE)
                Next
                _blocchi.Clear()
                If _maniglia <> IntPtr.Zero Then NativoLettore.CloseHandle(_maniglia)
                _maniglia = IntPtr.Zero
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("ControlliNativi.ProcessoRemoto.Dispose", exAtteso)
            End Try
        End Sub
    End Class

    Private Sub New()
    End Sub

    Public Shared Function ClasseFinestra(finestra As IntPtr) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim testo As New StringBuilder(128)
            NativoLettore.GetClassName(finestra, testo, testo.Capacity)
            Return testo.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.ClasseFinestra", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function EListaNativa(classe As String) As Boolean
        Return Not String.IsNullOrEmpty(classe) AndAlso classe.IndexOf("SysListView32", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Public Shared Function EAlberoNativo(classe As String) As Boolean
        Return Not String.IsNullOrEmpty(classe) AndAlso classe.IndexOf("SysTreeView32", StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Public Shared Function TrovaControllo(elemento As IUIAutomationElement, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest, lista As Boolean) As IntPtr
        Try
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 4
                If corrente Is Nothing Then Return IntPtr.Zero
                Dim maniglia As IntPtr = IntPtr.Zero
                Try
                    maniglia = CType(corrente.CurrentNativeWindowHandle, IntPtr)
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("ControlliNativi.TrovaControllo", exAtteso)
                    maniglia = IntPtr.Zero
                End Try
                If maniglia <> IntPtr.Zero Then
                    Dim classe As String = ClasseFinestra(maniglia)
                    If lista AndAlso EListaNativa(classe) Then Return maniglia
                    If Not lista AndAlso EAlberoNativo(classe) Then Return maniglia
                End If
                If walker Is Nothing Then Return IntPtr.Zero
                corrente = walker.GetParentElementBuildCache(corrente, cache)
            Next
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("ControlliNativi.TrovaControllo", exAtteso2)
        End Try
        Return IntPtr.Zero
    End Function

    Public Shared Function LeggiRigaLista(lista As IntPtr, ByRef riga As RigaNativa) As Boolean
        Dim processo As ProcessoRemoto = Nothing
        Try
            riga = New RigaNativa()
            riga.Colonne = New List(Of KeyValuePair(Of String, String))()
            If lista = IntPtr.Zero Then Return False
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(lista, LVM_GETNEXTITEM, New IntPtr(-1), New IntPtr(LVNI_FOCUSED), risultato) = IntPtr.Zero Then Return False
            Dim indice As Integer = Basso(risultato)
            If indice < 0 Then
                If Invia(lista, LVM_GETNEXTITEM, New IntPtr(-1), New IntPtr(LVNI_SELECTED), risultato) = IntPtr.Zero Then Return False
                indice = Basso(risultato)
            End If
            If indice < 0 Then Return False
            riga.Indice = indice + 1
            If Invia(lista, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, risultato) <> IntPtr.Zero Then riga.Totale = Basso(risultato)
            Dim vista As Integer = 1
            If Invia(lista, LVM_GETVIEW, IntPtr.Zero, IntPtr.Zero, risultato) <> IntPtr.Zero Then vista = Basso(risultato)
            Dim estesi As Integer = 0
            If Invia(lista, LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero, risultato) <> IntPtr.Zero Then estesi = Basso(risultato)
            If Invia(lista, LVM_GETITEMSTATE, New IntPtr(indice), New IntPtr(LVIS_SELECTED Or LVIS_STATEIMAGEMASK), risultato) <> IntPtr.Zero Then
                Dim stato As Integer = Basso(risultato)
                riga.Selezionata = (stato And LVIS_SELECTED) <> 0
                If (estesi And LVS_EX_CHECKBOXES) <> 0 Then
                    riga.ConCasella = True
                    riga.Spuntata = ((stato And LVIS_STATEIMAGEMASK) >> 12) = 2
                End If
            End If
            processo = ProcessoRemoto.Apri(lista)
            If processo Is Nothing Then Return False
            Dim colonne As Integer = NumeroColonne(lista)
            If vista <> 1 OrElse colonne <= 0 Then colonne = 1
            If colonne > LimiteColonne Then colonne = LimiteColonne
            Dim ordine As Integer() = OrdineColonne(lista, processo, colonne)
            Dim bufferTesto As IntPtr = processo.Alloca(LimiteTesto * 2 + 2)
            Dim strutturaVoce As IntPtr = processo.Alloca(96)
            Dim strutturaColonna As IntPtr = processo.Alloca(64)
            If bufferTesto = IntPtr.Zero OrElse strutturaVoce = IntPtr.Zero OrElse strutturaColonna = IntPtr.Zero Then Return False
            For posizione As Integer = 0 To colonne - 1
                Dim colonna As Integer = ordine(posizione)
                Dim valore As String = TestoVoce(lista, processo, strutturaVoce, bufferTesto, indice, colonna)
                If colonna = 0 Then
                    riga.Nome = valore
                    Continue For
                End If
                Dim larghezza As Integer = 1
                Dim intestazione As String = TestoColonna(lista, processo, strutturaColonna, bufferTesto, colonna, larghezza)
                If larghezza <= 0 Then Continue For
                If valore = "" Then Continue For
                riga.Colonne.Add(New KeyValuePair(Of String, String)(intestazione, valore))
            Next
            Return riga.Nome IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.LeggiRigaLista", exAtteso)
            Return False
        Finally
            If processo IsNot Nothing Then processo.Dispose()
        End Try
    End Function

    Public Shared Function FraseRigaLista(riga As RigaNativa, conColonne As Boolean) As String
        Dim parti As New List(Of String)()
        If Not String.IsNullOrEmpty(riga.Nome) Then parti.Add(riga.Nome)
        If conColonne AndAlso riga.Colonne IsNot Nothing Then
            For Each coppia As KeyValuePair(Of String, String) In riga.Colonne
                If String.IsNullOrEmpty(coppia.Key) OrElse String.Equals(coppia.Key, coppia.Value, StringComparison.CurrentCultureIgnoreCase) Then
                    parti.Add(coppia.Value)
                Else
                    parti.Add(coppia.Key & ": " & coppia.Value)
                End If
            Next
        End If
        If riga.ConCasella Then parti.Add(If(riga.Spuntata, "spuntato", "non spuntato"))
        If riga.Totale > 1 AndAlso riga.Indice > 0 Then parti.Add(riga.Indice.ToString() & " di " & riga.Totale.ToString())
        Return String.Join(", ", parti)
    End Function

    ' Come è mostrata la lista classica: icone, dettagli, icone piccole, elenco, riquadri.
    Public Shared Function NomeVistaLista(lista As IntPtr) As String
        Try
            Dim risultato As IntPtr = IntPtr.Zero
            If lista = IntPtr.Zero OrElse Invia(lista, LVM_GETVIEW, IntPtr.Zero, IntPtr.Zero, risultato) = IntPtr.Zero Then Return ""
            Select Case Basso(risultato)
                Case 0 : Return "visualizzazione a icone"
                Case 1 : Return "visualizzazione a dettagli"
                Case 2 : Return "visualizzazione a icone piccole"
                Case 3 : Return "visualizzazione ad elenco"
                Case 4 : Return "visualizzazione a riquadri"
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.NomeVistaLista", exAtteso)
        End Try
        Return ""
    End Function

    Public Shared Function SoloColonne(riga As RigaNativa) As String
        Dim parti As New List(Of String)()
        If riga.Colonne IsNot Nothing Then
            For Each coppia As KeyValuePair(Of String, String) In riga.Colonne
                If String.IsNullOrEmpty(coppia.Key) OrElse String.Equals(coppia.Key, coppia.Value, StringComparison.CurrentCultureIgnoreCase) Then
                    parti.Add(coppia.Value)
                Else
                    parti.Add(coppia.Key & ": " & coppia.Value)
                End If
            Next
        End If
        Return String.Join(", ", parti)
    End Function

    Private Shared Function NumeroColonne(lista As IntPtr) As Integer
        Try
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(lista, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero, risultato) = IntPtr.Zero Then Return 0
            Dim intestazione As IntPtr = risultato
            If intestazione = IntPtr.Zero Then Return 0
            If Invia(intestazione, HDM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, risultato) = IntPtr.Zero Then Return 0
            Return Basso(risultato)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.NumeroColonne", exAtteso)
            Return 0
        End Try
    End Function

    Private Shared Function OrdineColonne(lista As IntPtr, processo As ProcessoRemoto, colonne As Integer) As Integer()
        Dim ordine(colonne - 1) As Integer
        For indice As Integer = 0 To colonne - 1
            ordine(indice) = indice
        Next
        Try
            If colonne <= 1 Then Return ordine
            Dim memoria As IntPtr = processo.Alloca(colonne * 4)
            If memoria = IntPtr.Zero Then Return ordine
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(lista, LVM_GETCOLUMNORDERARRAY, New IntPtr(colonne), memoria, risultato) = IntPtr.Zero OrElse risultato = IntPtr.Zero Then Return ordine
            Dim dati As Byte() = processo.Leggi(memoria, colonne * 4)
            If dati Is Nothing Then Return ordine
            Dim letti(colonne - 1) As Integer
            Dim visti As New HashSet(Of Integer)()
            For indice As Integer = 0 To colonne - 1
                letti(indice) = BitConverter.ToInt32(dati, indice * 4)
                If letti(indice) < 0 OrElse letti(indice) >= colonne OrElse Not visti.Add(letti(indice)) Then Return ordine
            Next
            Return letti
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.OrdineColonne", exAtteso)
            Return ordine
        End Try
    End Function

    Private Shared Function TestoVoce(lista As IntPtr, processo As ProcessoRemoto, struttura As IntPtr, buffer As IntPtr, indice As Integer, colonna As Integer) As String
        Try
            Dim a64 As Boolean = processo.A64
            Dim dati(95) As Byte
            Array.Copy(BitConverter.GetBytes(1), 0, dati, 0, 4)
            Array.Copy(BitConverter.GetBytes(indice), 0, dati, 4, 4)
            Array.Copy(BitConverter.GetBytes(colonna), 0, dati, 8, 4)
            ScriviPuntatore(dati, If(a64, 24, 20), buffer, a64)
            Array.Copy(BitConverter.GetBytes(LimiteTesto), 0, dati, If(a64, 32, 24), 4)
            If Not processo.Scrivi(struttura, dati) Then Return ""
            Dim azzera(LimiteTesto * 2 + 1) As Byte
            processo.Scrivi(buffer, azzera)
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(lista, LVM_GETITEMTEXTW, New IntPtr(indice), struttura, risultato) = IntPtr.Zero Then Return ""
            Return LeggiStringa(processo, buffer, Basso(risultato))
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.TestoVoce", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function TestoColonna(lista As IntPtr, processo As ProcessoRemoto, struttura As IntPtr, buffer As IntPtr, colonna As Integer, ByRef larghezza As Integer) As String
        larghezza = 1
        Try
            Dim a64 As Boolean = processo.A64
            Dim dati(63) As Byte
            Array.Copy(BitConverter.GetBytes(LVCF_TEXT Or LVCF_WIDTH), 0, dati, 0, 4)
            ScriviPuntatore(dati, If(a64, 16, 12), buffer, a64)
            Array.Copy(BitConverter.GetBytes(LimiteTesto), 0, dati, If(a64, 24, 16), 4)
            If Not processo.Scrivi(struttura, dati) Then Return ""
            Dim azzera(LimiteTesto * 2 + 1) As Byte
            processo.Scrivi(buffer, azzera)
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(lista, LVM_GETCOLUMNW, New IntPtr(colonna), struttura, risultato) = IntPtr.Zero OrElse risultato = IntPtr.Zero Then Return ""
            Dim letto As Byte() = processo.Leggi(struttura, 16)
            If letto IsNot Nothing Then larghezza = BitConverter.ToInt32(letto, 8)
            Return LeggiStringa(processo, buffer, LimiteTesto)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.TestoColonna", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function LeggiNodoSelezionato(albero As IntPtr, ByRef nodo As NodoNativo) As Boolean
        Dim processo As ProcessoRemoto = Nothing
        Try
            nodo = New NodoNativo()
            If albero = IntPtr.Zero Then Return False
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(albero, TVM_GETNEXTITEM, New IntPtr(TVGN_CARET), IntPtr.Zero, risultato) = IntPtr.Zero Then Return False
            Dim voce As IntPtr = risultato
            If voce = IntPtr.Zero Then Return False
            processo = ProcessoRemoto.Apri(albero)
            If processo Is Nothing Then Return False
            Dim conCaselle As Boolean = (GetWindowLong(albero, GWL_STYLE) And TVS_CHECKBOXES) <> 0
            If Not LeggiVoceAlbero(albero, processo, voce, nodo) Then Return False
            nodo.ConCasella = conCaselle
            If Not conCaselle Then nodo.Spuntato = False
            Dim livello As Integer = 1
            Dim corrente As IntPtr = voce
            For salto As Integer = 1 To 40
                If Invia(albero, TVM_GETNEXTITEM, New IntPtr(TVGN_PARENT), corrente, risultato) = IntPtr.Zero Then Exit For
                corrente = risultato
                If corrente = IntPtr.Zero Then Exit For
                livello += 1
            Next
            nodo.Livello = livello
            Dim prima As Integer = 0
            corrente = voce
            For passo As Integer = 1 To LimiteFratelli
                If Invia(albero, TVM_GETNEXTITEM, New IntPtr(TVGN_PREVIOUS), corrente, risultato) = IntPtr.Zero Then Exit For
                corrente = risultato
                If corrente = IntPtr.Zero Then Exit For
                prima += 1
            Next
            Dim dopo As Integer = 0
            corrente = voce
            For passo As Integer = 1 To LimiteFratelli
                If Invia(albero, TVM_GETNEXTITEM, New IntPtr(TVGN_NEXT), corrente, risultato) = IntPtr.Zero Then Exit For
                corrente = risultato
                If corrente = IntPtr.Zero Then Exit For
                dopo += 1
            Next
            If prima < LimiteFratelli AndAlso dopo < LimiteFratelli Then
                nodo.Indice = prima + 1
                nodo.Totale = prima + dopo + 1
            End If
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.LeggiNodoSelezionato", exAtteso)
            Return False
        Finally
            If processo IsNot Nothing Then processo.Dispose()
        End Try
    End Function

    Public Shared Function FraseNodo(nodo As NodoNativo) As String
        Dim parti As New List(Of String)()
        If Not String.IsNullOrEmpty(nodo.Nome) Then parti.Add(nodo.Nome)
        parti.Add("livello " & nodo.Livello.ToString())
        If nodo.ConFigli Then parti.Add(If(nodo.Aperto, "aperto", "chiuso"))
        If nodo.ConCasella Then parti.Add(If(nodo.Spuntato, "spuntato", "non spuntato"))
        If nodo.Totale > 1 AndAlso nodo.Indice > 0 Then parti.Add(nodo.Indice.ToString() & " di " & nodo.Totale.ToString())
        Return String.Join(", ", parti)
    End Function

    Private Shared Function LeggiVoceAlbero(albero As IntPtr, processo As ProcessoRemoto, voce As IntPtr, ByRef nodo As NodoNativo) As Boolean
        Try
            Dim a64 As Boolean = processo.A64
            Dim struttura As IntPtr = processo.Alloca(64)
            Dim buffer As IntPtr = processo.Alloca(LimiteTesto * 2 + 2)
            If struttura = IntPtr.Zero OrElse buffer = IntPtr.Zero Then Return False
            Dim dati(63) As Byte
            Array.Copy(BitConverter.GetBytes(TVIF_TEXT Or TVIF_STATE Or TVIF_HANDLE Or TVIF_CHILDREN), 0, dati, 0, 4)
            ScriviPuntatore(dati, If(a64, 8, 4), voce, a64)
            Array.Copy(BitConverter.GetBytes(TVIS_EXPANDED Or TVIS_SELECTED Or TVIS_STATEIMAGEMASK), 0, dati, If(a64, 20, 12), 4)
            ScriviPuntatore(dati, If(a64, 24, 16), buffer, a64)
            Array.Copy(BitConverter.GetBytes(LimiteTesto), 0, dati, If(a64, 32, 20), 4)
            If Not processo.Scrivi(struttura, dati) Then Return False
            Dim risultato As IntPtr = IntPtr.Zero
            If Invia(albero, TVM_GETITEMW, IntPtr.Zero, struttura, risultato) = IntPtr.Zero OrElse risultato = IntPtr.Zero Then Return False
            Dim letto As Byte() = processo.Leggi(struttura, 64)
            If letto Is Nothing Then Return False
            Dim stato As Integer = BitConverter.ToInt32(letto, If(a64, 16, 8))
            Dim figli As Integer = BitConverter.ToInt32(letto, If(a64, 44, 32))
            nodo.Nome = LeggiStringa(processo, buffer, LimiteTesto)
            nodo.Aperto = (stato And TVIS_EXPANDED) <> 0
            nodo.Selezionato = (stato And TVIS_SELECTED) <> 0
            nodo.ConFigli = figli <> 0
            nodo.Spuntato = ((stato And TVIS_STATEIMAGEMASK) >> 12) = 2
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.LeggiVoceAlbero", exAtteso)
            Return False
        End Try
    End Function

    Private Shared Function Invia(finestra As IntPtr, messaggio As Integer, wParam As IntPtr, lParam As IntPtr, ByRef risultato As IntPtr) As IntPtr
        Return NativoLettore.SendMessageTimeout(finestra, messaggio, wParam, lParam, NativoLettore.SMTO_ABORTIFHUNG, 300UI, risultato)
    End Function

    Private Shared Function Basso(valore As IntPtr) As Integer
        Return BitConverter.ToInt32(BitConverter.GetBytes(valore.ToInt64()), 0)
    End Function

    Private Shared Sub ScriviPuntatore(dati As Byte(), scostamento As Integer, valore As IntPtr, a64 As Boolean)
        If a64 Then
            Array.Copy(BitConverter.GetBytes(valore.ToInt64()), 0, dati, scostamento, 8)
        Else
            Array.Copy(BitConverter.GetBytes(CInt(valore.ToInt64() And &HFFFFFFFFL)), 0, dati, scostamento, 4)
        End If
    End Sub

    Private Shared Function LeggiStringa(processo As ProcessoRemoto, buffer As IntPtr, caratteri As Integer) As String
        Try
            If caratteri <= 0 Then caratteri = LimiteTesto
            If caratteri > LimiteTesto Then caratteri = LimiteTesto
            Dim dati As Byte() = processo.Leggi(buffer, caratteri * 2)
            If dati Is Nothing Then Return ""
            Dim testo As String = Encoding.Unicode.GetString(dati)
            Dim fine As Integer = testo.IndexOf(ChrW(0))
            If fine >= 0 Then testo = testo.Substring(0, fine)
            Return testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("ControlliNativi.LeggiStringa", exAtteso)
            Return ""
        End Try
    End Function

End Class
