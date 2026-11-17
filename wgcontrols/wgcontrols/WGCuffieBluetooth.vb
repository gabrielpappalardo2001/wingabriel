' ============================================================================================
' WGCuffieBluetooth - collegare e scollegare al volo cuffie e casse Bluetooth già associate
' ============================================================================================
'
' È quello che fanno le impostazioni audio di Windows con "Connetti": ogni uscita audio Bluetooth
' (anche spenta o scollegata) ha dietro il driver audio Bluetooth di Windows, a cui si chiede di
' ricollegarsi (KSPROPERTY_ONESHOT_RECONNECT) o di staccarsi (KSPROPERTY_ONESHOT_DISCONNECT).
' Non servono diritti di amministratore. Le cuffie devono essere accese e già associate.
'
'   Dim cuffie As List(Of UscitaBluetooth) = WGCuffieBluetooth.Elenca()
'   Dim riuscito As Boolean = Await WGCuffieBluetooth.CollegaAsync(cuffie(0))   ' aspetta fino a 12 secondi
'   WGCuffieBluetooth.Scollega(cuffie(0))
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports NAudio.CoreAudioApi

Public Class UscitaBluetooth
    ''' <summary>Id dell'uscita audio di Windows.</summary>
    Public Property Id As String = ""
    Public Property Nome As String = ""
    ''' <summary>Vero se le cuffie sono collegate e si possono usare adesso.</summary>
    Public Property Collegata As Boolean
    ''' <summary>Uscita "vivavoce" (Hands-Free): qualità da telefonata.</summary>
    Public Property Vivavoce As Boolean
End Class

Public NotInheritable Class WGCuffieBluetooth

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

#Region "Interfacce di Windows"

    Private Const CLSCTX_ALL As Integer = 23
    Private Shared ReadOnly KSPROPSETID_BtAudio As New Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D")
    Private Const KSPROPERTY_ONESHOT_RECONNECT As Integer = 0
    Private Const KSPROPERTY_ONESHOT_DISCONNECT As Integer = 1
    Private Const KSPROPERTY_TYPE_GET As Integer = 1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KSPROPERTY
        Public Insieme As Guid
        Public Id As Integer
        Public Flags As Integer
    End Structure

    <ComImport(), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IEnumeratore
        Sub EnumAudioEndpoints()
        Sub GetDefaultAudioEndpoint()
        <PreserveSig> Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> id As String, <MarshalAs(UnmanagedType.Interface)> ByRef dispositivo As IDispositivo) As Integer
    End Interface

    <ComImport(), Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IDispositivo
        <PreserveSig> Function Activate(ByRef iid As Guid, clsCtx As Integer, parametri As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef oggetto As Object) As Integer
    End Interface

    <ComImport(), Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface ITopologia
        <PreserveSig> Function GetConnectorCount(ByRef quanti As Integer) As Integer
        <PreserveSig> Function GetConnector(indice As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef connettore As IConnettore) As Integer
    End Interface

    <ComImport(), Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IConnettore
        Sub GetType_()
        Sub GetDataFlow()
        Sub ConnectTo()
        Sub Disconnect()
        Sub IsConnected()
        <PreserveSig> Function GetConnectedTo(<MarshalAs(UnmanagedType.Interface)> ByRef altro As IConnettore) As Integer
        Sub GetConnectorIdConnectedTo()
        <PreserveSig> Function GetDeviceIdConnectedTo(<MarshalAs(UnmanagedType.LPWStr)> ByRef id As String) As Integer
    End Interface

    <ComImport(), Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IParte
        Sub GetName()
        Sub GetLocalId()
        Sub GetGlobalId()
        Sub GetPartType()
        Sub GetSubType()
        Sub GetControlInterfaceCount()
        Sub GetControlInterface()
        Sub EnumPartsIncoming()
        Sub EnumPartsOutgoing()
        Sub GetTopologyObject()
        <PreserveSig> Function Activate(clsCtx As Integer, ByRef iid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef oggetto As Object) As Integer
    End Interface

    <ComImport(), Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IKsControl
        <PreserveSig> Function KsProperty(ByRef proprieta As KSPROPERTY, lunghezza As Integer, dati As IntPtr, lunghezzaDati As Integer, ByRef restituiti As Integer) As Integer
    End Interface

#End Region

    Private Shared Function Rilascia(oggetto As Object) As Object
        Try
            If oggetto IsNot Nothing AndAlso Marshal.IsComObject(oggetto) Then Marshal.ReleaseComObject(oggetto)
        Catch
        End Try
        Return Nothing
    End Function

    ' Il driver dietro l'uscita audio: il suo id dice se è Bluetooth (BTHENUM, BTHHFENUM per il vivavoce).
    ' Con controllo, restituisce anche l'interfaccia per chiedergli di collegarsi o staccarsi.
    Private Shared Function DriverDi(idUscita As String, ByRef idDriver As String, Optional vuoleControllo As Boolean = False) As IKsControl
        idDriver = ""
        Dim enumeratore As IEnumeratore = Nothing
        Dim dispositivo As IDispositivo = Nothing
        Dim oggetto As Object = Nothing
        Dim connettore As IConnettore = Nothing
        Dim altro As IConnettore = Nothing
        Try
            enumeratore = DirectCast(Activator.CreateInstance(Type.GetTypeFromCLSID(New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), True)), IEnumeratore)
            If enumeratore.GetDevice(idUscita, dispositivo) <> 0 OrElse dispositivo Is Nothing Then Return Nothing
            Dim iidTopologia As Guid = GetType(ITopologia).GUID
            If dispositivo.Activate(iidTopologia, CLSCTX_ALL, IntPtr.Zero, oggetto) <> 0 OrElse oggetto Is Nothing Then Return Nothing
            Dim topologia As ITopologia = DirectCast(oggetto, ITopologia)
            Dim quanti As Integer = 0
            If topologia.GetConnectorCount(quanti) <> 0 OrElse quanti = 0 Then Return Nothing
            If topologia.GetConnector(0, connettore) <> 0 OrElse connettore Is Nothing Then Return Nothing
            Dim id As String = Nothing
            If connettore.GetDeviceIdConnectedTo(id) <> 0 OrElse id Is Nothing Then Return Nothing
            idDriver = id
            If Not vuoleControllo Then Return Nothing
            If connettore.GetConnectedTo(altro) <> 0 OrElse altro Is Nothing Then Return Nothing
            Dim parte As IParte = TryCast(altro, IParte)
            If parte Is Nothing Then Return Nothing
            Dim iidKs As Guid = GetType(IKsControl).GUID
            Dim controllo As Object = Nothing
            If parte.Activate(CLSCTX_ALL, iidKs, controllo) <> 0 OrElse controllo Is Nothing Then Return Nothing
            Return TryCast(controllo, IKsControl)
        Catch ex As Exception
            log.ScriviLog("WGCuffieBluetooth.DriverDi: " & ex.Message)
            Return Nothing
        Finally
            Rilascia(altro)
            Rilascia(connettore)
            Rilascia(oggetto)
            Rilascia(dispositivo)
            Rilascia(enumeratore)
        End Try
    End Function

    Private Shared Function EBluetooth(idDriver As String, ByRef vivavoce As Boolean) As Boolean
        Dim minuscolo As String = If(idDriver, "").ToLowerInvariant()
        vivavoce = minuscolo.Contains("bthhfenum")
        Return minuscolo.Contains("bthenum") OrElse vivavoce
    End Function

    ''' <summary>Le uscite audio Bluetooth (cuffie, casse), collegate o no, prima quelle stereo.</summary>
    Public Shared Function Elenca() As List(Of UscitaBluetooth)
        Dim risultato As New List(Of UscitaBluetooth)()
        Try
            Using enumeratore As MMDeviceEnumerator = EnumeratoreAudio.Crea()
                For Each uscita As MMDevice In enumeratore.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active Or DeviceState.Unplugged Or DeviceState.Disabled)
                    Try
                        Dim idDriver As String = ""
                        DriverDi(uscita.ID, idDriver)
                        Dim vivavoce As Boolean
                        If Not EBluetooth(idDriver, vivavoce) Then Continue For
                        risultato.Add(New UscitaBluetooth With {.Id = uscita.ID, .Nome = uscita.FriendlyName, .Collegata = uscita.State = DeviceState.Active, .Vivavoce = vivavoce})
                    Catch exUna As Exception
                        log.ScriviLog("WGCuffieBluetooth.Elenca (uscita): " & exUna.Message)
                    Finally
                        uscita.Dispose()
                    End Try
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGCuffieBluetooth.Elenca: " & ex.Message)
        End Try
        Return risultato.OrderBy(Function(u) u.Vivavoce).ThenByDescending(Function(u) u.Collegata).ThenBy(Function(u) u.Nome).ToList()
    End Function

    ''' <summary>Le uscite audio di un dispositivo Bluetooth associato, cercate per nome.</summary>
    Public Shared Function DiDispositivo(nomeDispositivo As String) As List(Of UscitaBluetooth)
        If String.IsNullOrWhiteSpace(nomeDispositivo) Then Return New List(Of UscitaBluetooth)()
        Dim nome As String = nomeDispositivo.Trim().ToLowerInvariant()
        Return Elenca().Where(Function(u) u.Nome.ToLowerInvariant().Contains(nome)).ToList()
    End Function

    Private Shared Function Chiedi(uscita As UscitaBluetooth, comando As Integer) As Boolean
        Dim controllo As IKsControl = Nothing
        Try
            Dim idDriver As String = ""
            controllo = DriverDi(uscita.Id, idDriver, True)
            If controllo Is Nothing Then Return False
            Dim proprieta As New KSPROPERTY With {.Insieme = KSPROPSETID_BtAudio, .Id = comando, .Flags = KSPROPERTY_TYPE_GET}
            Dim restituiti As Integer = 0
            Dim esito As Integer = controllo.KsProperty(proprieta, Marshal.SizeOf(GetType(KSPROPERTY)), IntPtr.Zero, 0, restituiti)
            If esito <> 0 Then log.ScriviLog("WGCuffieBluetooth.Chiedi " & uscita.Nome & ": codice " & esito.ToString("X8"))
            Return esito = 0
        Catch ex As Exception
            log.ScriviLog("WGCuffieBluetooth.Chiedi: " & ex.Message)
            Return False
        Finally
            Rilascia(controllo)
        End Try
    End Function

    Private Shared Function StatoAttivo(id As String) As Boolean
        Try
            Using enumeratore As MMDeviceEnumerator = EnumeratoreAudio.Crea()
                Using uscita As MMDevice = enumeratore.GetDevice(id)
                    Return uscita.State = DeviceState.Active
                End Using
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Chiede alle cuffie di collegarsi e aspetta fino a 12 secondi che siano pronte.
    ''' Vero se collegate; falso se spente, lontane o se Windows non ha accettato.
    ''' </summary>
    Public Shared Async Function CollegaAsync(uscita As UscitaBluetooth) As Task(Of Boolean)
        If uscita Is Nothing Then Return False
        If StatoAttivo(uscita.Id) Then Return True
        If Not Chiedi(uscita, KSPROPERTY_ONESHOT_RECONNECT) Then Return False
        Dim limite As DateTime = DateTime.Now.AddSeconds(12)
        While DateTime.Now < limite
            Await Task.Delay(500)
            If StatoAttivo(uscita.Id) Then Return True
        End While
        Return False
    End Function

    ''' <summary>Stacca le cuffie (restano associate: si ricollegano con CollegaAsync o riaccendendole).</summary>
    Public Shared Function Scollega(uscita As UscitaBluetooth) As Boolean
        If uscita Is Nothing Then Return False
        Return Chiedi(uscita, KSPROPERTY_ONESHOT_DISCONNECT)
    End Function

End Class
