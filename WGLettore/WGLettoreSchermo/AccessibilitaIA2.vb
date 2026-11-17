Imports System.Collections.Generic
Imports System.Runtime.InteropServices

' Lettura e analisi delle informazioni di accessibilità basate sulle specifiche IAccessible2 per gli elementi delle applicazioni.
Friend NotInheritable Class AccessibilitaIA2

    Private Shared ReadOnly IidServiceProvider As New Guid("6D5140C1-7436-11CE-8034-00AA006009FA")
    Private Shared ReadOnly IidIAccessible2 As New Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478")

    Private Const SlotQueryService As Integer = 3
    Private Const SlotGroupPosition As Integer = 34
    Private Const SlotStates As Integer = 35
    Private Const SlotAttributes As Integer = 45

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DelQueryService(self As IntPtr, ByRef servizio As Guid, ByRef iid As Guid, ByRef risultato As IntPtr) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DelGroupPosition(self As IntPtr, ByRef livello As Integer, ByRef simili As Integer, ByRef posizione As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DelStates(self As IntPtr, ByRef stati As Integer) As Integer

    <UnmanagedFunctionPointer(CallingConvention.StdCall)>
    Private Delegate Function DelAttributes(self As IntPtr, ByRef attributi As IntPtr) As Integer

    Public Structure InfoIA2
        Public Livello As Integer
        Public Simili As Integer
        Public Posizione As Integer
        Public Stati As Integer
        Public RuoloXml As String
        Public Attributi As Dictionary(Of String, String)
    End Structure

    Private Sub New()
    End Sub

    Public Shared Function Leggi(accessibile As Object, ByRef info As InfoIA2) As Boolean
        Dim unknown As IntPtr = IntPtr.Zero
        Dim provider As IntPtr = IntPtr.Zero
        Dim ia2 As IntPtr = IntPtr.Zero
        Try
            info = New InfoIA2()
            info.Attributi = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If accessibile Is Nothing Then Return False
            unknown = Marshal.GetIUnknownForObject(accessibile)
            Dim iidProvider As Guid = IidServiceProvider
            If Marshal.QueryInterface(unknown, iidProvider, provider) < 0 OrElse provider = IntPtr.Zero Then Return False
            Dim servizio As Guid = IidIAccessible2
            Dim iid As Guid = IidIAccessible2
            Dim queryService As DelQueryService = Funzione(Of DelQueryService)(provider, SlotQueryService)
            If queryService Is Nothing OrElse queryService(provider, servizio, iid, ia2) < 0 OrElse ia2 = IntPtr.Zero Then Return False
            Dim gruppo As DelGroupPosition = Funzione(Of DelGroupPosition)(ia2, SlotGroupPosition)
            If gruppo IsNot Nothing Then
                Dim livello As Integer = 0
                Dim simili As Integer = 0
                Dim posizione As Integer = 0
                If gruppo(ia2, livello, simili, posizione) >= 0 Then
                    info.Livello = livello
                    info.Simili = simili
                    info.Posizione = posizione
                End If
            End If
            Dim stati As DelStates = Funzione(Of DelStates)(ia2, SlotStates)
            If stati IsNot Nothing Then
                Dim valore As Integer = 0
                If stati(ia2, valore) >= 0 Then info.Stati = valore
            End If
            Dim attributi As DelAttributes = Funzione(Of DelAttributes)(ia2, SlotAttributes)
            If attributi IsNot Nothing Then
                Dim bstr As IntPtr = IntPtr.Zero
                If attributi(ia2, bstr) >= 0 AndAlso bstr <> IntPtr.Zero Then
                    Dim testo As String = Marshal.PtrToStringBSTR(bstr)
                    Marshal.FreeBSTR(bstr)
                    Analizza(testo, info)
                End If
            End If
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaIA2.Leggi", exAtteso)
            Return False
        Finally
            If ia2 <> IntPtr.Zero Then Marshal.Release(ia2)
            If provider <> IntPtr.Zero Then Marshal.Release(provider)
            If unknown <> IntPtr.Zero Then Marshal.Release(unknown)
        End Try
    End Function

    Public Shared Function Frase(info As InfoIA2) As String
        Dim parti As New List(Of String)()
        Dim ruolo As String = NomeRuoloXml(info.RuoloXml)
        If ruolo <> "" Then parti.Add(ruolo)
        Dim livello As Integer = info.Livello
        If livello <= 0 Then Integer.TryParse(Attributo(info, "level"), livello)
        If livello > 0 Then parti.Add("livello " & livello.ToString())
        Dim posizione As Integer = info.Posizione
        Dim simili As Integer = info.Simili
        If posizione <= 0 Then Integer.TryParse(Attributo(info, "posinset"), posizione)
        If simili <= 0 Then Integer.TryParse(Attributo(info, "setsize"), simili)
        If posizione > 0 AndAlso simili > 0 Then parti.Add(posizione.ToString() & " di " & simili.ToString())
        If (info.Stati And &H1000) <> 0 Then parti.Add("obbligatorio")
        If (info.Stati And &H80) <> 0 Then parti.Add("non valido")
        If Attributo(info, "expanded") = "true" Then parti.Add("espanso")
        If Attributo(info, "expanded") = "false" Then parti.Add("compresso")
        Return String.Join(", ", parti)
    End Function

    Private Shared Function Attributo(info As InfoIA2, chiave As String) As String
        Dim valore As String = Nothing
        If info.Attributi IsNot Nothing AndAlso info.Attributi.TryGetValue(chiave, valore) Then Return valore
        Return ""
    End Function

    Private Shared Sub Analizza(testo As String, ByRef info As InfoIA2)
        If String.IsNullOrEmpty(testo) Then Return
        For Each coppia As String In testo.Split(";"c)
            Dim segno As Integer = coppia.IndexOf(":"c)
            If segno <= 0 Then Continue For
            Dim chiave As String = coppia.Substring(0, segno).Trim()
            Dim valore As String = coppia.Substring(segno + 1).Replace("\", "").Trim()
            info.Attributi(chiave) = valore
        Next
        Dim ruolo As String = Nothing
        If info.Attributi.TryGetValue("xml-roles", ruolo) Then info.RuoloXml = ruolo
    End Sub

    Private Shared Function NomeRuoloXml(ruolo As String) As String
        If String.IsNullOrEmpty(ruolo) Then Return ""
        Select Case ruolo.Split(" "c)(0).ToLowerInvariant()
            Case "treeitem" : Return "voce di albero"
            Case "tree" : Return "albero"
            Case "treegrid" : Return "griglia ad albero"
            Case "grid" : Return "griglia"
            Case "row" : Return "riga"
            Case "gridcell", "cell" : Return "cella"
            Case "columnheader" : Return "intestazione di colonna"
            Case "rowheader" : Return "intestazione di riga"
            Case "listitem" : Return "voce di elenco"
            Case "option" : Return "opzione"
            Case "tab" : Return "scheda"
            Case "menuitem" : Return "voce di menu"
            Case "menuitemcheckbox" : Return "voce di menu con casella"
            Case "menuitemradio" : Return "voce di menu con opzione"
            Case "switch" : Return "interruttore"
            Case "searchbox" : Return "campo di ricerca"
            Case "alert" : Return "avviso"
            Case "dialog", "alertdialog" : Return "finestra di dialogo"
        End Select
        Return ""
    End Function

    Private Shared Function Funzione(Of T As Class)(oggetto As IntPtr, slot As Integer) As T
        Try
            Dim tabella As IntPtr = Marshal.ReadIntPtr(oggetto)
            Dim indirizzo As IntPtr = Marshal.ReadIntPtr(tabella, slot * IntPtr.Size)
            If indirizzo = IntPtr.Zero Then Return Nothing
            Return TryCast(Marshal.GetDelegateForFunctionPointer(indirizzo, GetType(T)), T)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaIA2.Funzione", exAtteso)
            Return Nothing
        End Try
    End Function

End Class
