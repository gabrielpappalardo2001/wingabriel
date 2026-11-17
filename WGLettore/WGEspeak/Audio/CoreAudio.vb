' ============================================================================================
' CoreAudio - le interfacce di Windows per l'audio (WASAPI) e le chiamate di sistema che servono
' ============================================================================================
'
' Qui ci sono solo le dichiarazioni per parlare con Windows: i dispositivi audio (MMDevice), il
' client audio (IAudioClient e IAudioClient3, quest'ultimo per la bassa latenza di Windows 10 e 11) e
' il client di scrittura (IAudioRenderClient). L'ordine dei metodi è quello di Windows: non spostarli.
' Niente librerie esterne: è tutto qui.
' ============================================================================================
Imports System.Runtime.InteropServices
Imports System.Text

Friend Module CostantiAudio
    Friend ReadOnly IID_IAudioClient As New Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")
    Friend ReadOnly IID_IAudioClient3 As New Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42")
    Friend ReadOnly IID_IAudioRenderClient As New Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")

    Friend Const CLSCTX_ALL As Integer = 23
    Friend Const AUDCLNT_STREAMFLAGS_EVENTCALLBACK As Integer = &H40000
    Friend Const AUDCLNT_BUFFERFLAGS_SILENT As Integer = 2
    Friend Const DispositivoDiUscita As Integer = 0
    Friend Const RuoloConsole As Integer = 0
    Friend Const RuoloMultimedia As Integer = 1
End Module

#Region "Interfacce COM"

<ComImport(), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IMMDeviceEnumerator
    <PreserveSig()> Function EnumAudioEndpoints(flusso As Integer, stato As Integer, ByRef dispositivi As IntPtr) As Integer
    <PreserveSig()> Function GetDefaultAudioEndpoint(flusso As Integer, ruolo As Integer, <Out(), MarshalAs(UnmanagedType.Interface)> ByRef dispositivo As IMMDevice) As Integer
    <PreserveSig()> Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> id As String, <Out(), MarshalAs(UnmanagedType.Interface)> ByRef dispositivo As IMMDevice) As Integer
    <PreserveSig()> Function RegisterEndpointNotificationCallback(<MarshalAs(UnmanagedType.Interface)> client As IMMNotificationClient) As Integer
    <PreserveSig()> Function UnregisterEndpointNotificationCallback(<MarshalAs(UnmanagedType.Interface)> client As IMMNotificationClient) As Integer
End Interface

<ComImport(), Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IMMDevice
    <PreserveSig()> Function Activate(ByRef iid As Guid, contesto As Integer, parametri As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef interfaccia As Object) As Integer
    <PreserveSig()> Function OpenPropertyStore(accesso As Integer, ByRef proprieta As IntPtr) As Integer
    <PreserveSig()> Function GetId(<MarshalAs(UnmanagedType.LPWStr)> ByRef id As String) As Integer
    <PreserveSig()> Function GetState(ByRef stato As Integer) As Integer
End Interface

<StructLayout(LayoutKind.Sequential)>
Friend Structure ChiaveProprieta
    Public Formato As Guid
    Public Identificativo As Integer
End Structure

<ComImport(), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IMMNotificationClient
    <PreserveSig()> Function OnDeviceStateChanged(<MarshalAs(UnmanagedType.LPWStr)> id As String, stato As Integer) As Integer
    <PreserveSig()> Function OnDeviceAdded(<MarshalAs(UnmanagedType.LPWStr)> id As String) As Integer
    <PreserveSig()> Function OnDeviceRemoved(<MarshalAs(UnmanagedType.LPWStr)> id As String) As Integer
    <PreserveSig()> Function OnDefaultDeviceChanged(flusso As Integer, ruolo As Integer, <MarshalAs(UnmanagedType.LPWStr)> id As String) As Integer
    <PreserveSig()> Function OnPropertyValueChanged(<MarshalAs(UnmanagedType.LPWStr)> id As String, chiave As ChiaveProprieta) As Integer
End Interface

<ComImport(), Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IAudioClient
    <PreserveSig()> Function Initialize(modo As Integer, flags As Integer, durataBuffer As Long, periodicita As Long, formato As IntPtr, ByRef sessione As Guid) As Integer
    <PreserveSig()> Function GetBufferSize(ByRef frame As UInteger) As Integer
    <PreserveSig()> Function GetStreamLatency(ByRef latenza As Long) As Integer
    <PreserveSig()> Function GetCurrentPadding(ByRef frame As UInteger) As Integer
    <PreserveSig()> Function IsFormatSupported(modo As Integer, formato As IntPtr, ByRef piuVicino As IntPtr) As Integer
    <PreserveSig()> Function GetMixFormat(ByRef formato As IntPtr) As Integer
    <PreserveSig()> Function GetDevicePeriod(ByRef predefinito As Long, ByRef minimo As Long) As Integer
    <PreserveSig()> Function Start() As Integer
    <PreserveSig()> Function [Stop]() As Integer
    <PreserveSig()> Function Reset() As Integer
    <PreserveSig()> Function SetEventHandle(evento As IntPtr) As Integer
    <PreserveSig()> Function GetService(ByRef riid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef servizio As Object) As Integer
End Interface

' IAudioClient3 (Windows 10 e 11): in più c'è la scelta del periodo del motore audio, fino a pochi millisecondi.
' In COM le interfacce derivate ripetono tutti i metodi di quelle da cui derivano, nello stesso ordine.
<ComImport(), Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IAudioClient3
    <PreserveSig()> Function Initialize(modo As Integer, flags As Integer, durataBuffer As Long, periodicita As Long, formato As IntPtr, ByRef sessione As Guid) As Integer
    <PreserveSig()> Function GetBufferSize(ByRef frame As UInteger) As Integer
    <PreserveSig()> Function GetStreamLatency(ByRef latenza As Long) As Integer
    <PreserveSig()> Function GetCurrentPadding(ByRef frame As UInteger) As Integer
    <PreserveSig()> Function IsFormatSupported(modo As Integer, formato As IntPtr, ByRef piuVicino As IntPtr) As Integer
    <PreserveSig()> Function GetMixFormat(ByRef formato As IntPtr) As Integer
    <PreserveSig()> Function GetDevicePeriod(ByRef predefinito As Long, ByRef minimo As Long) As Integer
    <PreserveSig()> Function Start() As Integer
    <PreserveSig()> Function [Stop]() As Integer
    <PreserveSig()> Function Reset() As Integer
    <PreserveSig()> Function SetEventHandle(evento As IntPtr) As Integer
    <PreserveSig()> Function GetService(ByRef riid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef servizio As Object) As Integer
    <PreserveSig()> Function IsOffloadCapable(categoria As Integer, ByRef supportato As Integer) As Integer
    <PreserveSig()> Function SetClientProperties(proprieta As IntPtr) As Integer
    <PreserveSig()> Function GetBufferSizeLimits(formato As IntPtr, guidatoDaEventi As Integer, ByRef minimo As Long, ByRef massimo As Long) As Integer
    <PreserveSig()> Function GetSharedModeEnginePeriod(formato As IntPtr, ByRef predefinito As UInteger, ByRef fondamentale As UInteger, ByRef minimo As UInteger, ByRef massimo As UInteger) As Integer
    <PreserveSig()> Function GetCurrentSharedModeEnginePeriod(ByRef formato As IntPtr, ByRef periodoCorrente As UInteger) As Integer
    <PreserveSig()> Function InitializeSharedAudioStream(flags As Integer, periodoInFrame As UInteger, formato As IntPtr, ByRef sessione As Guid) As Integer
End Interface

<ComImport(), Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IAudioRenderClient
    <PreserveSig()> Function GetBuffer(frame As UInteger, ByRef dati As IntPtr) As Integer
    <PreserveSig()> Function ReleaseBuffer(frame As UInteger, flags As Integer) As Integer
End Interface

' Chi riceve gli avvisi di Windows sui dispositivi audio (scheda predefinita cambiata, cuffia staccata...).
<ClassInterface(ClassInterfaceType.None), ComVisible(True)>
Friend NotInheritable Class NotificaDispositivi
    Implements IMMNotificationClient

    Private ReadOnly _idDispositivo As String
    Private ReadOnly _alCambio As Action

    Public Sub New(idDispositivo As String, alCambio As Action)
        _idDispositivo = If(idDispositivo, "")
        _alCambio = alCambio
    End Sub

    Private Sub Segnala()
        Try
            If _alCambio IsNot Nothing Then _alCambio.Invoke()
        Catch
        End Try
    End Sub

    Public Function OnDeviceStateChanged(id As String, stato As Integer) As Integer Implements IMMNotificationClient.OnDeviceStateChanged
        ' Stato 1 = attivo: qualsiasi altro stato dei nostri dispositivi (scollegato, disabilitato) la rende inutilizzabile.
        If stato <> 1 AndAlso String.Equals(id, _idDispositivo, StringComparison.OrdinalIgnoreCase) Then Segnala()
        Return 0
    End Function

    Public Function OnDeviceAdded(id As String) As Integer Implements IMMNotificationClient.OnDeviceAdded
        Return 0
    End Function

    Public Function OnDeviceRemoved(id As String) As Integer Implements IMMNotificationClient.OnDeviceRemoved
        If String.Equals(id, _idDispositivo, StringComparison.OrdinalIgnoreCase) Then Segnala()
        Return 0
    End Function

    Public Function OnDefaultDeviceChanged(flusso As Integer, ruolo As Integer, id As String) As Integer Implements IMMNotificationClient.OnDefaultDeviceChanged
        ' Cambia l'uscita predefinita: l'audio deve seguirla.
        If flusso = DispositivoDiUscita AndAlso (ruolo = RuoloConsole OrElse ruolo = RuoloMultimedia) Then Segnala()
        Return 0
    End Function

    Public Function OnPropertyValueChanged(id As String, chiave As ChiaveProprieta) As Integer Implements IMMNotificationClient.OnPropertyValueChanged
        Return 0
    End Function
End Class

#End Region

#Region "Formato dei campioni"

Friend Enum TipoCampione
    NonSupportato = 0
    Virgola32 = 1
    Intero16 = 2
    Intero24 = 3
    Intero32 = 4
End Enum

' Il formato in cui Windows vuole l'audio (WAVEFORMATEX o WAVEFORMATEXTENSIBLE).
Friend NotInheritable Class FormatoAudio
    Public Canali As Integer
    Public Frequenza As Integer
    Public Tipo As TipoCampione = TipoCampione.NonSupportato

    Friend Shared Function Leggi(puntatore As IntPtr) As FormatoAudio
        Dim f As New FormatoAudio()
        If puntatore = IntPtr.Zero Then Return f
        Dim tag As Integer = Marshal.ReadInt16(puntatore, 0) And &HFFFF
        f.Canali = Marshal.ReadInt16(puntatore, 2)
        f.Frequenza = Marshal.ReadInt32(puntatore, 4)
        Dim bit As Integer = Marshal.ReadInt16(puntatore, 14)
        Dim estensione As Integer = Marshal.ReadInt16(puntatore, 16)
        Dim sottoTipo As Integer = tag
        If tag = &HFFFE AndAlso estensione >= 22 Then sottoTipo = Marshal.ReadInt32(puntatore, 24)
        If sottoTipo = 3 AndAlso bit = 32 Then
            f.Tipo = TipoCampione.Virgola32
        ElseIf sottoTipo = 1 Then
            Select Case bit
                Case 16 : f.Tipo = TipoCampione.Intero16
                Case 24 : f.Tipo = TipoCampione.Intero24
                Case 32 : f.Tipo = TipoCampione.Intero32
            End Select
        End If
        Return f
    End Function
End Class

#End Region

#Region "Chiamate di sistema"

Friend Module SistemaNativo

    Private ReadOnly ClsidEnumeratoreDispositivi As New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")

    ''' <summary>
    ''' Crea l'enumeratore dei dispositivi audio di Windows. Non si usa "New" su una classe ComImport:
    ''' NAudio dichiara già una classe con lo stesso CLSID (MMDeviceEnumeratorComObject) e il CLR
    ''' restituisce quella, per cui il cast alla nostra classe falliva. Con Type.GetTypeFromCLSID
    ''' l'oggetto è un COM generico, e il cast all'interfaccia passa da QueryInterface.
    ''' </summary>
    Friend Function CreaEnumeratoreDispositivi() As IMMDeviceEnumerator
        Dim tipo As Type = Type.GetTypeFromCLSID(ClsidEnumeratoreDispositivi, True)
        Dim oggetto As Object = Activator.CreateInstance(tipo)
        Return CType(oggetto, IMMDeviceEnumerator)
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Function CreateEventW(attributi As IntPtr, ripristinoManuale As Boolean, statoIniziale As Boolean, nome As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Friend Function SetEvent(evento As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Friend Function WaitForSingleObject(oggetto As IntPtr, attesaMs As Integer) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Friend Function CloseHandle(oggetto As IntPtr) As Boolean
    End Function

    <DllImport("avrt.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Friend Function AvSetMmThreadCharacteristicsW(attivita As String, ByRef indice As Integer) As IntPtr
    End Function

    <DllImport("avrt.dll", SetLastError:=True)>
    Friend Function AvRevertMmThreadCharacteristics(maniglia As IntPtr) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Friend Structure InformazioniVersione
        Public Dimensione As Integer
        Public Maggiore As Integer
        Public Minore As Integer
        Public Build As Integer
        Public Piattaforma As Integer
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=128)>
        Public Aggiornamento As String
    End Structure

    <DllImport("ntdll.dll")>
    Private Function RtlGetVersion(ByRef informazioni As InformazioniVersione) As Integer
    End Function

    ''' <summary>
    ''' La versione vera di Windows. Environment.OSVersion dice 6.2 a un programma senza il dichiarativo di
    ''' compatibilità, anche su Windows 10 e 11: serve la chiamata di sistema.
    ''' </summary>
    Friend Function VersioneWindows() As Version
        Try
            Dim v As New InformazioniVersione()
            v.Dimensione = Marshal.SizeOf(GetType(InformazioniVersione))
            If RtlGetVersion(v) = 0 Then Return New Version(v.Maggiore, v.Minore, v.Build)
        Catch
        End Try
        Return Environment.OSVersion.Version
    End Function

End Module

#End Region
