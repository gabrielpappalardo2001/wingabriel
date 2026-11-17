Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Controllo diretto del volume audio di sistema e delle sessioni audio tramite le interfacce Core Audio di Windows.
Public NotInheritable Class WGVolumeWindows

    Private Const CLSCTX_ALL As Integer = 23
    Private Const EDATAFLOW_ERENDER As Integer = 0
    Private Const EROLE_EMULTIMEDIA As Integer = 1
    Private Const NUMERO_TENTATIVI_SESSIONE As Integer = 6
    Private Const ATTESA_TRA_TENTATIVI_MILLISECONDI As Integer = 150
    Private Shared ReadOnly ID_CLASSE_ENUMERATORE_DISPOSITIVI As New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")

    <ComImport(), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDeviceEnumerator
        <PreserveSig()> Function EnumAudioEndpoints(dataFlow As Integer, dwStateMask As Integer, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppDevices As Object) As Integer
        <PreserveSig()> Function GetDefaultAudioEndpoint(dataFlow As Integer, role As Integer, <Out()> ByRef ppEndpoint As IMMDevice) As Integer
        <PreserveSig()> Function GetDevice(pwstrId As String, <Out()> ByRef ppDevice As IMMDevice) As Integer
        <PreserveSig()> Function RegisterEndpointNotificationCallback(<MarshalAs(UnmanagedType.IUnknown)> pClient As Object) As Integer
        <PreserveSig()> Function UnregisterEndpointNotificationCallback(<MarshalAs(UnmanagedType.IUnknown)> pClient As Object) As Integer
    End Interface

    <ComImport(), Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDevice
        <PreserveSig()> Function Activate(ByRef iid As Guid, dwClsCtx As Integer, pActivationParams As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppInterface As Object) As Integer
        <PreserveSig()> Function OpenPropertyStore(stgmAccess As Integer, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppProperties As Object) As Integer
        <PreserveSig()> Function GetId(<MarshalAs(UnmanagedType.LPWStr)> ByRef ppstrId As String) As Integer
        <PreserveSig()> Function GetState(ByRef pdwState As Integer) As Integer
    End Interface

    <ComImport(), Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioEndpointVolume
        <PreserveSig()> Function RegisterControlChangeNotify(<MarshalAs(UnmanagedType.IUnknown)> pNotify As Object) As Integer
        <PreserveSig()> Function UnregisterControlChangeNotify(<MarshalAs(UnmanagedType.IUnknown)> pNotify As Object) As Integer
        <PreserveSig()> Function GetChannelCount(ByRef pnChannelCount As Integer) As Integer
        <PreserveSig()> Function SetMasterVolumeLevel(fLevelDB As Single, pguidEventContext As IntPtr) As Integer
        <PreserveSig()> Function SetMasterVolumeLevelScalar(fLevel As Single, pguidEventContext As IntPtr) As Integer
        <PreserveSig()> Function GetMasterVolumeLevel(ByRef pfLevelDB As Single) As Integer
        <PreserveSig()> Function GetMasterVolumeLevelScalar(ByRef pfLevel As Single) As Integer
        <PreserveSig()> Function SetChannelVolumeLevel(nChannel As Integer, fLevelDB As Single, pguidEventContext As IntPtr) As Integer
        <PreserveSig()> Function SetChannelVolumeLevelScalar(nChannel As Integer, fLevel As Single, pguidEventContext As IntPtr) As Integer
        <PreserveSig()> Function GetChannelVolumeLevel(nChannel As Integer, ByRef pfLevelDB As Single) As Integer
        <PreserveSig()> Function GetChannelVolumeLevelScalar(nChannel As Integer, ByRef pfLevel As Single) As Integer
        <PreserveSig()> Function SetMute(bMute As Boolean, pguidEventContext As IntPtr) As Integer
        <PreserveSig()> Function GetMute(ByRef pbMute As Boolean) As Integer
    End Interface

    <ComImport(), Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioSessionManager2
        <PreserveSig()> Function GetAudioSessionControl(ByRef audioSessionGuid As Guid, streamFlags As Integer, <MarshalAs(UnmanagedType.IUnknown)> ByRef sessionControl As Object) As Integer
        <PreserveSig()> Function GetSimpleAudioVolume(ByRef audioSessionGuid As Guid, streamFlags As Integer, <MarshalAs(UnmanagedType.IUnknown)> ByRef audioSimpleVolume As Object) As Integer
        <PreserveSig()> Function GetSessionEnumerator(<Out()> ByRef sessionEnum As IAudioSessionEnumerator) As Integer
    End Interface

    <ComImport(), Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioSessionEnumerator
        <PreserveSig()> Function GetCount(ByRef sessionCount As Integer) As Integer
        <PreserveSig()> Function GetSession(sessionCount As Integer, <Out()> ByRef session As IAudioSessionControl2) As Integer
    End Interface

    <ComImport(), Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioSessionControl2
        <PreserveSig()> Function GetState(ByRef pRetVal As Integer) As Integer
        <PreserveSig()> Function GetDisplayName(<MarshalAs(UnmanagedType.LPWStr)> ByRef pRetVal As String) As Integer
        <PreserveSig()> Function SetDisplayName(<MarshalAs(UnmanagedType.LPWStr)> value As String, ByRef eventContext As Guid) As Integer
        <PreserveSig()> Function GetIconPath(<MarshalAs(UnmanagedType.LPWStr)> ByRef pRetVal As String) As Integer
        <PreserveSig()> Function SetIconPath(<MarshalAs(UnmanagedType.LPWStr)> value As String, ByRef eventContext As Guid) As Integer
        <PreserveSig()> Function GetGroupingParam(ByRef pRetVal As Guid) As Integer
        <PreserveSig()> Function SetGroupingParam(ByRef override As Guid, ByRef eventContext As Guid) As Integer
        <PreserveSig()> Function RegisterAudioSessionNotification(<MarshalAs(UnmanagedType.IUnknown)> client As Object) As Integer
        <PreserveSig()> Function UnregisterAudioSessionNotification(<MarshalAs(UnmanagedType.IUnknown)> client As Object) As Integer
        <PreserveSig()> Function GetSessionIdentifier(<MarshalAs(UnmanagedType.LPWStr)> ByRef pRetVal As String) As Integer
        <PreserveSig()> Function GetSessionInstanceIdentifier(<MarshalAs(UnmanagedType.LPWStr)> ByRef pRetVal As String) As Integer
        <PreserveSig()> Function GetProcessId(ByRef pRetVal As Integer) As Integer
    End Interface

    <ComImport(), Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface ISimpleAudioVolume
        <PreserveSig()> Function SetMasterVolume(fLevel As Single, ByRef eventContext As Guid) As Integer
        <PreserveSig()> Function GetMasterVolume(ByRef pfLevel As Single) As Integer
        <PreserveSig()> Function SetMute(bMute As Boolean, ByRef eventContext As Guid) As Integer
        <PreserveSig()> Function GetMute(ByRef pbMute As Boolean) As Integer
    End Interface

    Private Shared Function OttieniDispositivoAudioPredefinito() As IMMDevice
        Try
            Dim tipoEnumeratore As Type = Type.GetTypeFromCLSID(ID_CLASSE_ENUMERATORE_DISPOSITIVI)
            If tipoEnumeratore Is Nothing Then Return Nothing

            Dim enumeratoreObj As Object = Activator.CreateInstance(tipoEnumeratore)
            Dim enumeratore As IMMDeviceEnumerator = TryCast(enumeratoreObj, IMMDeviceEnumerator)
            If enumeratore Is Nothing Then Return Nothing

            Dim dispositivo As IMMDevice = Nothing
            Dim esito As Integer = enumeratore.GetDefaultAudioEndpoint(EDATAFLOW_ERENDER, EROLE_EMULTIMEDIA, dispositivo)

            If esito <> 0 Then Return Nothing
            Return dispositivo
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.OttieniDispositivoAudioPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Public Shared Function OttieniVolumeSistema() As Integer
        Try
            Dim dispositivo As IMMDevice = OttieniDispositivoAudioPredefinito()
            If dispositivo Is Nothing Then Return -1

            Dim iidEndpointVolume As Guid = GetType(IAudioEndpointVolume).GUID
            Dim interfacciaObj As Object = Nothing
            Dim esitoAttivazione As Integer = dispositivo.Activate(iidEndpointVolume, CLSCTX_ALL, IntPtr.Zero, interfacciaObj)

            If esitoAttivazione <> 0 OrElse interfacciaObj Is Nothing Then Return -1

            Dim volumeEndpoint As IAudioEndpointVolume = TryCast(interfacciaObj, IAudioEndpointVolume)
            If volumeEndpoint Is Nothing Then Return -1

            Dim livello As Single = 0
            If volumeEndpoint.GetMasterVolumeLevelScalar(livello) <> 0 Then Return -1

            Return CInt(Math.Round(livello * 100.0F))
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.OttieniVolumeSistema eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Public Shared Function ImpostaVolumeSistema(percentuale As Integer) As Boolean
        Try
            If percentuale < 0 Then percentuale = 0
            If percentuale > 100 Then percentuale = 100

            Dim dispositivo As IMMDevice = OttieniDispositivoAudioPredefinito()
            If dispositivo Is Nothing Then Return False

            Dim iidEndpointVolume As Guid = GetType(IAudioEndpointVolume).GUID
            Dim interfacciaObj As Object = Nothing
            Dim esitoAttivazione As Integer = dispositivo.Activate(iidEndpointVolume, CLSCTX_ALL, IntPtr.Zero, interfacciaObj)

            If esitoAttivazione <> 0 OrElse interfacciaObj Is Nothing Then Return False

            Dim volumeEndpoint As IAudioEndpointVolume = TryCast(interfacciaObj, IAudioEndpointVolume)
            If volumeEndpoint Is Nothing Then Return False

            Return volumeEndpoint.SetMasterVolumeLevelScalar(CSng(percentuale / 100.0), IntPtr.Zero) = 0
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.ImpostaVolumeSistema eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function CercaControlloVolumeSessioneProcessoCorrente() As ISimpleAudioVolume
        Dim dispositivo As IMMDevice = OttieniDispositivoAudioPredefinito()
        If dispositivo Is Nothing Then Return Nothing

        Dim iidSessionManager As Guid = GetType(IAudioSessionManager2).GUID
        Dim interfacciaObj As Object = Nothing
        Dim esitoAttivazione As Integer = dispositivo.Activate(iidSessionManager, CLSCTX_ALL, IntPtr.Zero, interfacciaObj)

        If esitoAttivazione <> 0 OrElse interfacciaObj Is Nothing Then Return Nothing

        Dim gestoreSessioni As IAudioSessionManager2 = TryCast(interfacciaObj, IAudioSessionManager2)
        If gestoreSessioni Is Nothing Then Return Nothing

        Dim enumeratoreSessioni As IAudioSessionEnumerator = Nothing
        If gestoreSessioni.GetSessionEnumerator(enumeratoreSessioni) <> 0 OrElse enumeratoreSessioni Is Nothing Then Return Nothing

        Dim numeroSessioni As Integer = 0
        If enumeratoreSessioni.GetCount(numeroSessioni) <> 0 Then Return Nothing

        Dim idProcessoCorrente As Integer = Process.GetCurrentProcess().Id

        For indice As Integer = 0 To numeroSessioni - 1
            Try
                Dim sessione As IAudioSessionControl2 = Nothing
                If enumeratoreSessioni.GetSession(indice, sessione) <> 0 OrElse sessione Is Nothing Then Continue For

                Dim idProcessoSessione As Integer = 0
                If sessione.GetProcessId(idProcessoSessione) <> 0 Then Continue For

                If idProcessoSessione = idProcessoCorrente Then
                    Return TryCast(sessione, ISimpleAudioVolume)
                End If
            Catch exSessione As Exception
                ScriviLogBootstrap("WGVolumeWindows.CercaControlloVolumeSessioneProcessoCorrente (sessione " & indice.ToString(Globalization.CultureInfo.InvariantCulture) & "): " & exSessione.Message)
            End Try
        Next

        Return Nothing
    End Function

    Private Shared Function OttieniControlloVolumeSessioneProcessoCorrente() As ISimpleAudioVolume
        For tentativo As Integer = 1 To NUMERO_TENTATIVI_SESSIONE
            Try
                Dim controllo As ISimpleAudioVolume = CercaControlloVolumeSessioneProcessoCorrente()
                If controllo IsNot Nothing Then Return controllo
            Catch exTentativo As Exception
                ScriviLogBootstrap("WGVolumeWindows.OttieniControlloVolumeSessioneProcessoCorrente (tentativo " & tentativo.ToString(Globalization.CultureInfo.InvariantCulture) & "): " & exTentativo.Message & Environment.NewLine & exTentativo.StackTrace)
            End Try

            If tentativo < NUMERO_TENTATIVI_SESSIONE Then
                Try
                    Threading.Thread.Sleep(ATTESA_TRA_TENTATIVI_MILLISECONDI)
                Catch exAttesa As Exception
                    ScriviLogBootstrap("WGVolumeWindows.OttieniControlloVolumeSessioneProcessoCorrente (attesa): " & exAttesa.Message)
                End Try
            End If
        Next

        Return Nothing
    End Function

    Public Shared Function OttieniVolumeSuoniProcessoCorrente() As Integer
        Try
            Dim controllo As ISimpleAudioVolume = OttieniControlloVolumeSessioneProcessoCorrente()
            If controllo Is Nothing Then Return -1

            Dim livello As Single = 0
            If controllo.GetMasterVolume(livello) <> 0 Then Return -1

            Return CInt(Math.Round(livello * 100.0F))
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.OttieniVolumeSuoniProcessoCorrente eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Public Shared Function ImpostaVolumeSuoniProcessoCorrente(percentuale As Integer) As Boolean
        Try
            If percentuale < 0 Then percentuale = 0
            If percentuale > 100 Then percentuale = 100

            Dim controllo As ISimpleAudioVolume = OttieniControlloVolumeSessioneProcessoCorrente()
            If controllo Is Nothing Then
                Return False
            End If

            Dim contestoEvento As Guid = Guid.Empty
            Return controllo.SetMasterVolume(CSng(percentuale / 100.0), contestoEvento) = 0
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.ImpostaVolumeSuoniProcessoCorrente eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Shared Function ImpostaSilenziamentoSuoniProcessoCorrente(muto As Boolean) As Boolean
        Try
            Dim controllo As ISimpleAudioVolume = OttieniControlloVolumeSessioneProcessoCorrente()
            If controllo Is Nothing Then Return False

            Dim contestoEvento As Guid = Guid.Empty
            Return controllo.SetMute(muto, contestoEvento) = 0
        Catch ex As Exception
            ScriviLogBootstrap("WGVolumeWindows.ImpostaSilenziamentoSuoniProcessoCorrente eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

End Class
