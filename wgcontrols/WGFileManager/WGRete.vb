Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Win32

Public NotInheritable Class WGRete

    Private Sub New()
    End Sub

    Public Const TIMEOUT_PREDEFINITO_MS As Integer = 4000
    Public Const TIMEOUT_ELENCO_CONDIVISIONI_MS As Integer = 15000

    Private Const SCADENZA_OFFLINE_SECONDI As Integer = 20
    Private Const INVALID_FILE_ATTRIBUTES As UInteger = &HFFFFFFFFUI
    Private Const FILE_ATTRIBUTE_DIRECTORY As UInteger = &H10UI
    Private Const DRIVE_REMOTE As UInteger = 4UI
    Private Const RESOURCETYPE_DISK As Integer = 1
    Private Const CONNECT_UPDATE_PROFILE As Integer = 1
    Private Const STYPE_DISK As UInteger = 0UI
    Private Const STYPE_MASK As UInteger = &HFFUI
    Private Const MAX_PREFERRED_LENGTH As Integer = -1
    Private Const ERROR_MORE_DATA As Integer = 234
    Private Const ERRORE_NOME_NON_VALIDO As Integer = 67
    Private Const ERRORE_IMPREVISTO As Integer = -1

    Private Shared ReadOnly _log As New wgcontrols.Logger()
    Private Shared ReadOnly _offline As New ConcurrentDictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure NETRESOURCE
        Public dwScope As Integer
        Public dwType As Integer
        Public dwDisplayType As Integer
        Public dwUsage As Integer
        <MarshalAs(UnmanagedType.LPWStr)>
        Public lpLocalName As String
        <MarshalAs(UnmanagedType.LPWStr)>
        Public lpRemoteName As String
        <MarshalAs(UnmanagedType.LPWStr)>
        Public lpComment As String
        <MarshalAs(UnmanagedType.LPWStr)>
        Public lpProvider As String
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure SHARE_INFO_1
        <MarshalAs(UnmanagedType.LPWStr)>
        Public shi1_netname As String
        Public shi1_type As UInteger
        <MarshalAs(UnmanagedType.LPWStr)>
        Public shi1_remark As String
    End Structure

    <DllImport("mpr.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WNetAddConnection2(ByRef lpNetResource As NETRESOURCE, lpPassword As String, lpUserName As String, dwFlags As Integer) As Integer
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WNetCancelConnection2(lpName As String, dwFlags As Integer, fForce As Boolean) As Integer
    End Function

    <DllImport("mpr.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WNetGetConnection(lpLocalName As String, lpRemoteName As StringBuilder, ByRef lpnLength As Integer) As Integer
    End Function

    <DllImport("netapi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function NetShareEnum(serverName As String, level As Integer, ByRef bufPtr As IntPtr, prefMaxLen As Integer, ByRef entriesRead As Integer, ByRef totalEntries As Integer, ByRef resumeHandle As Integer) As Integer
    End Function

    <DllImport("netapi32.dll")>
    Private Shared Function NetApiBufferFree(buffer As IntPtr) As Integer
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetFileAttributesW(lpFileName As String) As UInteger
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetDriveTypeW(lpRootPathName As String) As UInteger
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetDiskFreeSpaceExW(lpDirectoryName As String, ByRef lpFreeBytesAvailable As ULong, ByRef lpTotalNumberOfBytes As ULong, ByRef lpTotalNumberOfFreeBytes As ULong) As Boolean
    End Function

    Public Class InfoCondivisione
        Public Property Nome As String = ""
        Public Property Commento As String = ""
        Public Property Percorso As String = ""
    End Class

    Public Class UnitaDiRete
        Public Property Lettera As String = ""
        Public Property PercorsoUnc As String = ""
    End Class

    Public Class InfoSpazio
        Public Property Disponibile As Boolean = False
        Public Property Totale As Long = 0
        Public Property Libero As Long = 0
    End Class

    Private Class EsitoAttributi
        Public Attributi As UInteger = INVALID_FILE_ATTRIBUTES
        Public Errore As Integer = 0
    End Class

    Private Class EsitoElenco
        Public Elenco As New List(Of InfoCondivisione)()
        Public Errore As Integer = 0
    End Class

    Private Class EsitoSpazio
        Public Spazio As New InfoSpazio()
        Public Errore As Integer = 0
    End Class

    Private Shared Sub Registra(messaggio As String, ex As Exception)
        Try
            If ex Is Nothing Then
                _log.ScriviLog(messaggio)
            Else
                _log.ScriviLog(messaggio & " - Tipo: " & ex.GetType().FullName & " | Messaggio: " & ex.Message & " | HResult: 0x" & ex.HResult.ToString("X8"))
            End If
        Catch logEx As Exception
            System.Diagnostics.Debug.WriteLine("WGRete LOG FAILURE: " & logEx.Message)
        End Try
    End Sub

    Public Shared Function IsUncPath(percorso As String) As Boolean
        Try
            If String.IsNullOrEmpty(percorso) OrElse percorso.Length < 3 Then Return False
            If Not percorso.StartsWith("\\", StringComparison.Ordinal) Then Return False
            If percorso.StartsWith("\\?\", StringComparison.Ordinal) OrElse percorso.StartsWith("\\.\", StringComparison.Ordinal) Then Return False
            Return percorso(2) <> "\"c
        Catch ex As Exception
            Registra("WGRete.IsUncPath - Errore", ex)
            Return False
        End Try
    End Function

    Private Shared Function SegmentiUnc(percorso As String) As String()
        Try
            If Not IsUncPath(percorso) Then Return New String() {}
            Return percorso.Substring(2).Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
        Catch ex As Exception
            Registra("WGRete.SegmentiUnc - Errore", ex)
            Return New String() {}
        End Try
    End Function

    Public Shared Function IsShareRoot(percorso As String) As Boolean
        Try
            Return SegmentiUnc(percorso).Length = 2
        Catch ex As Exception
            Registra("WGRete.IsShareRoot - Errore", ex)
            Return False
        End Try
    End Function

    Public Shared Function IsServerRoot(percorso As String) As Boolean
        Try
            Return SegmentiUnc(percorso).Length = 1
        Catch ex As Exception
            Registra("WGRete.IsServerRoot - Errore", ex)
            Return False
        End Try
    End Function

    Public Shared Function NomeServer(percorso As String) As String
        Try
            Dim segmenti As String() = SegmentiUnc(percorso)
            If segmenti.Length = 0 Then Return String.Empty
            Return "\\" & segmenti(0)
        Catch ex As Exception
            Registra("WGRete.NomeServer - Errore", ex)
            Return String.Empty
        End Try
    End Function

    Public Shared Function RadiceRisorsa(percorso As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return String.Empty
            If IsUncPath(percorso) Then
                Dim segmenti As String() = SegmentiUnc(percorso)
                If segmenti.Length >= 2 Then Return "\\" & segmenti(0) & "\" & segmenti(1)
                If segmenti.Length = 1 Then Return "\\" & segmenti(0)
                Return String.Empty
            End If
            If percorso.Length >= 2 AndAlso Char.IsLetter(percorso(0)) AndAlso percorso(1) = ":"c Then
                Return percorso.Substring(0, 1).ToUpperInvariant() & ":\"
            End If
            Return String.Empty
        Catch ex As Exception
            Registra("WGRete.RadiceRisorsa - Errore", ex)
            Return String.Empty
        End Try
    End Function

    Public Shared Function NormalizzaPercorsoUnc(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return String.Empty
            Dim pulito As String = testo.Trim().Trim(""""c).Trim().Replace("/"c, "\"c)
            If pulito.StartsWith("\\", StringComparison.Ordinal) Then
                While pulito.Length > 3 AndAlso pulito.EndsWith("\\", StringComparison.Ordinal)
                    pulito = pulito.Substring(0, pulito.Length - 1)
                End While
            End If
            Return pulito
        Catch ex As Exception
            Registra("WGRete.NormalizzaPercorsoUnc - Errore", ex)
            Return String.Empty
        End Try
    End Function

    Public Shared Function IsRemoto(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If percorso.StartsWith("WPD|", StringComparison.Ordinal) Then Return False
            If IsUncPath(percorso) Then Return True
            If percorso.Length >= 2 AndAlso Char.IsLetter(percorso(0)) AndAlso percorso(1) = ":"c Then
                Return GetDriveTypeW(percorso.Substring(0, 2) & "\") = DRIVE_REMOTE
            End If
            Return False
        Catch ex As Exception
            Registra("WGRete.IsRemoto - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Public Shared Function IsRadiceRisorsa(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If IsUncPath(percorso) Then Return IsShareRoot(percorso)
            Return percorso.Length = 3 AndAlso percorso.EndsWith(":\", StringComparison.Ordinal)
        Catch ex As Exception
            Registra("WGRete.IsRadiceRisorsa - Errore", ex)
            Return False
        End Try
    End Function

    Private Shared Function SegnataOffline(radice As String) As Boolean
        Try
            If String.IsNullOrEmpty(radice) Then Return False
            Dim quando As DateTime
            If _offline.TryGetValue(radice, quando) Then
                If (DateTime.UtcNow - quando).TotalSeconds < SCADENZA_OFFLINE_SECONDI Then Return True
                _offline.TryRemove(radice, quando)
            End If
            Return False
        Catch ex As Exception
            Registra("WGRete.SegnataOffline - Errore", ex)
            Return False
        End Try
    End Function

    Private Shared Sub SegnaOffline(radice As String)
        Try
            If String.IsNullOrEmpty(radice) Then Return
            _offline(radice) = DateTime.UtcNow
        Catch ex As Exception
            Registra("WGRete.SegnaOffline - Errore", ex)
        End Try
    End Sub

    Private Shared Sub SegnaOnline(radice As String)
        Try
            If String.IsNullOrEmpty(radice) Then Return
            Dim quando As DateTime
            _offline.TryRemove(radice, quando)
        Catch ex As Exception
            Registra("WGRete.SegnaOnline - Errore", ex)
        End Try
    End Sub

    Public Shared Sub AzzeraStatoOffline()
        Try
            _offline.Clear()
        Catch ex As Exception
            Registra("WGRete.AzzeraStatoOffline - Errore", ex)
        End Try
    End Sub

    Private Shared Function ErroreDiRaggiungibilita(codice As Integer) As Boolean
        Select Case codice
            Case 53, 59, 64, 67, 121, 1167, 1203, 1222, 1225, 1231, 1232, 1236, 2250
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function EseguiConTimeout(Of T)(lavoro As Func(Of T), timeoutMs As Integer, ByRef scaduto As Boolean, predefinito As T) As T
        scaduto = False
        Try
            Dim compito As Task(Of T) = Task.Factory.StartNew(Of T)(lavoro, TaskCreationOptions.LongRunning)
            If compito.Wait(timeoutMs) Then
                Return compito.Result
            End If
            scaduto = True
            Return predefinito
        Catch ex As AggregateException
            Registra("WGRete.EseguiConTimeout - Errore nell'operazione", ex.GetBaseException())
            Return predefinito
        Catch ex As Exception
            Registra("WGRete.EseguiConTimeout - Errore", ex)
            Return predefinito
        End Try
    End Function

    Private Shared Function LeggiAttributiDiretto(percorso As String) As EsitoAttributi
        Dim esito As New EsitoAttributi()
        Try
            esito.Attributi = GetFileAttributesW(percorso)
            If esito.Attributi = INVALID_FILE_ATTRIBUTES Then
                esito.Errore = Marshal.GetLastWin32Error()
            End If
        Catch ex As Exception
            Registra("WGRete.LeggiAttributiDiretto - Errore su '" & percorso & "'", ex)
            esito.Errore = ERRORE_IMPREVISTO
        End Try
        Return esito
    End Function

    Private Shared Function ControllaAttributi(percorso As String, timeoutMs As Integer, ByRef attributi As UInteger) As Boolean
        attributi = INVALID_FILE_ATTRIBUTES
        Try
            Dim radice As String = RadiceRisorsa(percorso)
            If SegnataOffline(radice) Then Return False
            Dim scaduto As Boolean = False
            Dim percorsoLetto As String = percorso
            Dim lavoro As Func(Of EsitoAttributi) = Function() LeggiAttributiDiretto(percorsoLetto)
            Dim esito As EsitoAttributi = EseguiConTimeout(Of EsitoAttributi)(lavoro, timeoutMs, scaduto, Nothing)
            If scaduto Then
                SegnaOffline(radice)
                Registra("WGRete.ControllaAttributi - Timeout di " & timeoutMs.ToString() & " ms su '" & percorso & "'", Nothing)
                Return False
            End If
            If esito Is Nothing Then Return False
            If esito.Attributi <> INVALID_FILE_ATTRIBUTES Then
                SegnaOnline(radice)
                attributi = esito.Attributi
                Return True
            End If
            If ErroreDiRaggiungibilita(esito.Errore) Then
                SegnaOffline(radice)
            ElseIf esito.Errore = 2 OrElse esito.Errore = 3 OrElse esito.Errore = 5 Then
                SegnaOnline(radice)
            End If
            Return False
        Catch ex As Exception
            Registra("WGRete.ControllaAttributi - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Public Shared Function EsisteDirectory(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If Not IsRemoto(percorso) Then Return Directory.Exists(percorso)
            Dim attributi As UInteger = INVALID_FILE_ATTRIBUTES
            If Not ControllaAttributi(percorso, timeoutMs, attributi) Then Return False
            Return (attributi And FILE_ATTRIBUTE_DIRECTORY) = FILE_ATTRIBUTE_DIRECTORY
        Catch ex As Exception
            Registra("WGRete.EsisteDirectory - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Public Shared Function EsisteFile(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If Not IsRemoto(percorso) Then Return File.Exists(percorso)
            Dim attributi As UInteger = INVALID_FILE_ATTRIBUTES
            If Not ControllaAttributi(percorso, timeoutMs, attributi) Then Return False
            Return (attributi And FILE_ATTRIBUTE_DIRECTORY) = 0UI
        Catch ex As Exception
            Registra("WGRete.EsisteFile - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Public Shared Function EsisteElemento(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If Not IsRemoto(percorso) Then Return File.Exists(percorso) OrElse Directory.Exists(percorso)
            Dim attributi As UInteger = INVALID_FILE_ATTRIBUTES
            Return ControllaAttributi(percorso, timeoutMs, attributi)
        Catch ex As Exception
            Registra("WGRete.EsisteElemento - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Public Shared Function VerificaAccesso(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As Integer
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return 123
            If Not IsRemoto(percorso) Then Return 0
            Dim radice As String = RadiceRisorsa(percorso)
            If SegnataOffline(radice) Then Return 53
            Dim scaduto As Boolean = False
            Dim percorsoLetto As String = percorso
            Dim lavoro As Func(Of EsitoAttributi) = Function() LeggiAttributiDiretto(percorsoLetto)
            Dim esito As EsitoAttributi = EseguiConTimeout(Of EsitoAttributi)(lavoro, timeoutMs, scaduto, Nothing)
            If scaduto Then
                SegnaOffline(radice)
                Registra("WGRete.VerificaAccesso - Timeout di " & timeoutMs.ToString() & " ms su '" & percorso & "'", Nothing)
                Return 121
            End If
            If esito Is Nothing Then Return ERRORE_IMPREVISTO
            If esito.Attributi <> INVALID_FILE_ATTRIBUTES Then
                SegnaOnline(radice)
                Return 0
            End If
            If ErroreDiRaggiungibilita(esito.Errore) Then
                SegnaOffline(radice)
            ElseIf esito.Errore = 2 OrElse esito.Errore = 3 OrElse esito.Errore = 5 Then
                SegnaOnline(radice)
            End If
            If esito.Errore = 0 Then Return ERRORE_IMPREVISTO
            Return esito.Errore
        Catch ex As Exception
            Registra("WGRete.VerificaAccesso - Errore su '" & percorso & "'", ex)
            Return ERRORE_IMPREVISTO
        End Try
    End Function

    Public Shared Function Raggiungibile(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If Not IsRemoto(percorso) Then Return True
            Dim radice As String = RadiceRisorsa(percorso)
            If String.IsNullOrEmpty(radice) Then Return False
            If Not radice.EndsWith("\", StringComparison.Ordinal) Then radice &= "\"
            Dim attributi As UInteger = INVALID_FILE_ATTRIBUTES
            Return ControllaAttributi(radice, timeoutMs, attributi)
        Catch ex As Exception
            Registra("WGRete.Raggiungibile - Errore su '" & percorso & "'", ex)
            Return False
        End Try
    End Function

    Private Shared Function LeggiSpazioDiretto(percorso As String) As EsitoSpazio
        Dim esito As New EsitoSpazio()
        Try
            Dim liberoUtente As ULong = 0
            Dim totale As ULong = 0
            Dim liberoTotale As ULong = 0
            If GetDiskFreeSpaceExW(percorso, liberoUtente, totale, liberoTotale) Then
                esito.Spazio.Disponibile = True
                esito.Spazio.Totale = CLng(Math.Min(totale, CULng(Long.MaxValue)))
                esito.Spazio.Libero = CLng(Math.Min(liberoUtente, CULng(Long.MaxValue)))
            Else
                esito.Errore = Marshal.GetLastWin32Error()
            End If
        Catch ex As Exception
            Registra("WGRete.LeggiSpazioDiretto - Errore su '" & percorso & "'", ex)
            esito.Errore = ERRORE_IMPREVISTO
        End Try
        Return esito
    End Function

    Public Shared Function LeggiSpazio(percorso As String, Optional timeoutMs As Integer = TIMEOUT_PREDEFINITO_MS) As InfoSpazio
        Dim vuoto As New InfoSpazio()
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return vuoto
            Dim radice As String = RadiceRisorsa(percorso)
            Dim destinazione As String = If(String.IsNullOrEmpty(radice), percorso, radice)
            If Not destinazione.EndsWith("\", StringComparison.Ordinal) Then destinazione &= "\"
            If IsRemoto(destinazione) AndAlso SegnataOffline(radice) Then Return vuoto
            Dim scaduto As Boolean = False
            Dim destinazioneLetta As String = destinazione
            Dim lavoro As Func(Of EsitoSpazio) = Function() LeggiSpazioDiretto(destinazioneLetta)
            Dim esito As EsitoSpazio = EseguiConTimeout(Of EsitoSpazio)(lavoro, timeoutMs, scaduto, Nothing)
            If scaduto Then
                SegnaOffline(radice)
                Registra("WGRete.LeggiSpazio - Timeout di " & timeoutMs.ToString() & " ms su '" & destinazione & "'", Nothing)
                Return vuoto
            End If
            If esito Is Nothing Then Return vuoto
            Return esito.Spazio
        Catch ex As Exception
            Registra("WGRete.LeggiSpazio - Errore su '" & percorso & "'", ex)
            Return vuoto
        End Try
    End Function

    Public Shared Function RisolviUnc(percorso As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return String.Empty
            If IsUncPath(percorso) Then Return percorso
            If percorso.Length < 2 OrElse Not Char.IsLetter(percorso(0)) OrElse percorso(1) <> ":"c Then Return String.Empty
            If GetDriveTypeW(percorso.Substring(0, 2) & "\") <> DRIVE_REMOTE Then Return String.Empty
            Dim capacita As Integer = 1024
            Dim destinazione As New StringBuilder(capacita)
            Dim codice As Integer = WNetGetConnection(percorso.Substring(0, 2), destinazione, capacita)
            If codice = ERROR_MORE_DATA Then
                destinazione = New StringBuilder(capacita + 1)
                codice = WNetGetConnection(percorso.Substring(0, 2), destinazione, capacita)
            End If
            If codice <> 0 Then
                Registra("WGRete.RisolviUnc - WNetGetConnection ha restituito " & codice.ToString() & " per '" & percorso & "'", Nothing)
                Return String.Empty
            End If
            Dim resto As String = If(percorso.Length > 2, percorso.Substring(2), String.Empty)
            Return destinazione.ToString().TrimEnd("\"c) & resto
        Catch ex As Exception
            Registra("WGRete.RisolviUnc - Errore su '" & percorso & "'", ex)
            Return String.Empty
        End Try
    End Function

    Public Shared Function UnitaMappate() As List(Of UnitaDiRete)
        Dim risultato As New List(Of UnitaDiRete)()
        Try
            Dim unita As String() = Environment.GetLogicalDrives()
            For Each radice As String In unita
                Try
                    If GetDriveTypeW(radice) <> DRIVE_REMOTE Then Continue For
                    Dim unc As String = RisolviUnc(radice)
                    risultato.Add(New UnitaDiRete With {.Lettera = radice.Substring(0, 1).ToUpperInvariant(), .PercorsoUnc = unc})
                Catch exUnita As Exception
                    Registra("WGRete.UnitaMappate - Errore su '" & radice & "'", exUnita)
                End Try
            Next
        Catch ex As Exception
            Registra("WGRete.UnitaMappate - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Shared Function LettereLibere() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim occupate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each radice As String In Environment.GetLogicalDrives()
                If Not String.IsNullOrEmpty(radice) Then occupate.Add(radice.Substring(0, 1))
            Next
            For codice As Integer = AscW("Z"c) To AscW("D"c) Step -1
                Dim lettera As String = ChrW(codice).ToString()
                If Not occupate.Contains(lettera) Then risultato.Add(lettera)
            Next
        Catch ex As Exception
            Registra("WGRete.LettereLibere - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Shared Function LeggiMappatureSalvate() As List(Of UnitaDiRete)
        Dim risultato As New List(Of UnitaDiRete)()
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey("Network", False)
                If chiave Is Nothing Then Return risultato
                For Each lettera As String In chiave.GetSubKeyNames()
                    Try
                        Using sotto As RegistryKey = chiave.OpenSubKey(lettera, False)
                            If sotto Is Nothing Then Continue For
                            Dim remoto As String = TryCast(sotto.GetValue("RemotePath"), String)
                            If String.IsNullOrWhiteSpace(remoto) Then Continue For
                            risultato.Add(New UnitaDiRete With {.Lettera = lettera.ToUpperInvariant(), .PercorsoUnc = remoto})
                        End Using
                    Catch exVoce As Exception
                        Registra("WGRete.LeggiMappatureSalvate - Errore sulla voce '" & lettera & "'", exVoce)
                    End Try
                Next
            End Using
        Catch ex As Exception
            Registra("WGRete.LeggiMappatureSalvate - Errore", ex)
        End Try
        Return risultato
    End Function

    Public Shared Function Connetti(percorsoUnc As String, lettera As String, utente As String, password As String, persistente As Boolean) As Integer
        Try
            Dim remoto As String = NormalizzaPercorsoUnc(percorsoUnc)
            If Not IsUncPath(remoto) OrElse SegmentiUnc(remoto).Length < 2 Then Return ERRORE_NOME_NON_VALIDO
            Dim radice As String = RadiceRisorsa(remoto)

            Dim risorsa As New NETRESOURCE()
            risorsa.dwType = RESOURCETYPE_DISK
            risorsa.lpRemoteName = radice

            Dim nomeLocale As String = Nothing
            If Not String.IsNullOrWhiteSpace(lettera) Then
                Dim candidata As String = lettera.Trim().Substring(0, 1).ToUpperInvariant()
                If Not Char.IsLetter(candidata(0)) Then Return 1200
                nomeLocale = candidata & ":"
            End If
            risorsa.lpLocalName = nomeLocale

            Dim flags As Integer = 0
            If persistente AndAlso nomeLocale IsNot Nothing Then flags = CONNECT_UPDATE_PROFILE

            Dim nomeUtente As String = Nothing
            Dim parolaChiave As String = Nothing
            If Not String.IsNullOrWhiteSpace(utente) Then
                nomeUtente = utente.Trim()
                parolaChiave = password
            End If

            Dim codice As Integer = WNetAddConnection2(risorsa, parolaChiave, nomeUtente, flags)
            If codice = 0 Then
                SegnaOnline(radice)
            Else
                Registra("WGRete.Connetti - WNetAddConnection2 ha restituito " & codice.ToString() & " per '" & radice & "'", Nothing)
            End If
            Return codice
        Catch ex As Exception
            Registra("WGRete.Connetti - Errore", ex)
            Return ERRORE_IMPREVISTO
        End Try
    End Function

    Public Shared Function Disconnetti(nome As String, forza As Boolean) As Integer
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ERRORE_NOME_NON_VALIDO
            Dim destinazione As String = nome.Trim()
            If destinazione.Length >= 2 AndAlso destinazione(1) = ":"c Then destinazione = destinazione.Substring(0, 2).ToUpperInvariant()
            Dim codice As Integer = WNetCancelConnection2(destinazione, CONNECT_UPDATE_PROFILE, forza)
            If codice <> 0 Then
                Registra("WGRete.Disconnetti - WNetCancelConnection2 ha restituito " & codice.ToString() & " per '" & destinazione & "'", Nothing)
            Else
                AzzeraStatoOffline()
            End If
            Return codice
        Catch ex As Exception
            Registra("WGRete.Disconnetti - Errore", ex)
            Return ERRORE_IMPREVISTO
        End Try
    End Function

    Public Shared Function RicollegaMappatureSalvate() As List(Of String)
        Dim righe As New List(Of String)()
        Try
            Dim salvate As List(Of UnitaDiRete) = LeggiMappatureSalvate()
            If salvate.Count = 0 Then
                righe.Add("Non risultano unità di rete salvate per questo utente.")
                Return righe
            End If
            Dim presenti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each radice As String In Environment.GetLogicalDrives()
                If Not String.IsNullOrEmpty(radice) Then presenti.Add(radice.Substring(0, 1))
            Next
            For Each voce As UnitaDiRete In salvate
                Try
                    If presenti.Contains(voce.Lettera) Then
                        righe.Add(voce.Lettera & ": " & voce.PercorsoUnc & " - già presente")
                        Continue For
                    End If
                    Dim codice As Integer = Connetti(voce.PercorsoUnc, voce.Lettera, Nothing, Nothing, False)
                    If codice = 0 Then
                        righe.Add(voce.Lettera & ": " & voce.PercorsoUnc & " - collegata")
                    Else
                        righe.Add(voce.Lettera & ": " & voce.PercorsoUnc & " - non collegata. " & DescriviErrore(codice))
                    End If
                Catch exVoce As Exception
                    righe.Add(voce.Lettera & ": " & voce.PercorsoUnc & " - errore imprevisto")
                    Registra("WGRete.RicollegaMappatureSalvate - Errore su '" & voce.Lettera & "'", exVoce)
                End Try
            Next
        Catch ex As Exception
            Registra("WGRete.RicollegaMappatureSalvate - Errore", ex)
            righe.Add("Errore imprevisto durante il ricollegamento. Controlla il log.")
        End Try
        Return righe
    End Function

    Private Shared Function ElencaCondivisioniDiretto(nomeServer As String) As EsitoElenco
        Dim esito As New EsitoElenco()
        Dim buffer As IntPtr = IntPtr.Zero
        Try
            Dim lette As Integer = 0
            Dim totali As Integer = 0
            Dim ripresa As Integer = 0
            Dim codice As Integer = NetShareEnum(nomeServer, 1, buffer, MAX_PREFERRED_LENGTH, lette, totali, ripresa)
            If codice <> 0 AndAlso codice <> ERROR_MORE_DATA Then
                esito.Errore = codice
                Return esito
            End If
            Dim dimensione As Integer = Marshal.SizeOf(GetType(SHARE_INFO_1))
            Dim cursore As Long = buffer.ToInt64()
            For i As Integer = 0 To lette - 1
                Try
                    Dim info As SHARE_INFO_1 = CType(Marshal.PtrToStructure(New IntPtr(cursore), GetType(SHARE_INFO_1)), SHARE_INFO_1)
                    cursore += dimensione
                    If String.IsNullOrWhiteSpace(info.shi1_netname) Then Continue For
                    If (info.shi1_type And STYPE_MASK) <> STYPE_DISK Then Continue For
                    If info.shi1_netname.EndsWith("$", StringComparison.Ordinal) Then Continue For
                    esito.Elenco.Add(New InfoCondivisione With {
                        .Nome = info.shi1_netname,
                        .Commento = If(info.shi1_remark, String.Empty),
                        .Percorso = nomeServer & "\" & info.shi1_netname
                    })
                Catch exVoce As Exception
                    Registra("WGRete.ElencaCondivisioniDiretto - Errore su una voce di '" & nomeServer & "'", exVoce)
                End Try
            Next
            esito.Elenco = esito.Elenco.OrderBy(Function(c) c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList()
        Catch ex As Exception
            esito.Errore = ERRORE_IMPREVISTO
            Registra("WGRete.ElencaCondivisioniDiretto - Errore su '" & nomeServer & "'", ex)
        Finally
            If buffer <> IntPtr.Zero Then
                Try
                    NetApiBufferFree(buffer)
                Catch exFree As Exception
                    Registra("WGRete.ElencaCondivisioniDiretto - Errore rilascio buffer", exFree)
                End Try
            End If
        End Try
        Return esito
    End Function

    Public Shared Function ElencaCondivisioni(server As String, ByRef codiceErrore As Integer) As List(Of InfoCondivisione)
        codiceErrore = 0
        Dim vuoto As New List(Of InfoCondivisione)()
        Try
            Dim nomeServerPulito As String = String.Empty
            Dim testo As String = NormalizzaPercorsoUnc(server)
            If IsUncPath(testo) Then
                nomeServerPulito = NomeServer(testo)
            ElseIf Not String.IsNullOrWhiteSpace(testo) Then
                Dim segmenti As String() = testo.Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
                If segmenti.Length > 0 Then nomeServerPulito = "\\" & segmenti(0)
            End If
            If String.IsNullOrEmpty(nomeServerPulito) Then
                codiceErrore = ERRORE_NOME_NON_VALIDO
                Return vuoto
            End If
            Dim scaduto As Boolean = False
            Dim nomeLetto As String = nomeServerPulito
            Dim lavoro As Func(Of EsitoElenco) = Function() ElencaCondivisioniDiretto(nomeLetto)
            Dim esito As EsitoElenco = EseguiConTimeout(Of EsitoElenco)(lavoro, TIMEOUT_ELENCO_CONDIVISIONI_MS, scaduto, Nothing)
            If scaduto Then
                codiceErrore = 121
                Registra("WGRete.ElencaCondivisioni - Timeout su '" & nomeServerPulito & "'", Nothing)
                Return vuoto
            End If
            If esito Is Nothing Then
                codiceErrore = ERRORE_IMPREVISTO
                Return vuoto
            End If
            codiceErrore = esito.Errore
            If esito.Errore <> 0 Then
                Registra("WGRete.ElencaCondivisioni - NetShareEnum ha restituito " & esito.Errore.ToString() & " per '" & nomeServerPulito & "'", Nothing)
            End If
            Return esito.Elenco
        Catch ex As Exception
            codiceErrore = ERRORE_IMPREVISTO
            Registra("WGRete.ElencaCondivisioni - Errore", ex)
            Return vuoto
        End Try
    End Function

    Public Shared Function DescriviErrore(codice As Integer) As String
        Try
            Select Case codice
                Case 0
                    Return "Operazione completata."
                Case 5
                    Return "Accesso negato. Controlla nome utente e password oppure i permessi sulla condivisione."
                Case 53, 67
                    Return "Percorso di rete non trovato. Controlla il nome del computer e della condivisione."
                Case 64
                    Return "Il nome di rete non è più disponibile."
                Case 85
                    Return "La lettera dell'unità è già in uso."
                Case 86, 1326
                    Return "Nome utente o password non corretti."
                Case 121
                    Return "Il computer non ha risposto in tempo."
                Case 1200
                    Return "La lettera dell'unità non è valida."
                Case 1202
                    Return "La lettera dell'unità è già memorizzata come connessione."
                Case 1203, 1222
                    Return "Nessuna rete disponibile."
                Case 1219
                    Return "Esiste già una connessione a questo computer con credenziali diverse. Disconnetti prima le altre unità di rete dello stesso computer."
                Case 1231, 1232
                    Return "Il computer non è raggiungibile in rete."
                Case 1244
                    Return "Per accedere devi autenticarti con nome utente e password."
                Case 1331
                    Return "L'account utente è disabilitato."
                Case 1909
                    Return "L'account utente è bloccato."
                Case 2250
                    Return "L'unità di rete non è connessa."
                Case 2401
                    Return "Ci sono file aperti su questa unità di rete."
                Case ERRORE_IMPREVISTO
                    Return "Errore imprevisto. Controlla il log."
                Case Else
                    Return "Errore di rete (codice " & codice.ToString() & "): " & New Win32Exception(codice).Message
            End Select
        Catch ex As Exception
            Registra("WGRete.DescriviErrore - Errore", ex)
            Return "Errore di rete (codice " & codice.ToString() & ")."
        End Try
    End Function

End Class
