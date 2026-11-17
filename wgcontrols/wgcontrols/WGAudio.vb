Imports System.Runtime.InteropServices

' Gestione dei dispositivi audio di sistema: enumerazione di altoparlanti e microfoni, controllo del volume e gestione dello stato
' muto.
Public Structure WGDispositivoAudio
    Public Property Id As String
    Public Property Nome As String
End Structure

Public Class WGAudio

    Private Shared log As New Logger()

    Private Enum EDataFlow
        eRender = 0
        eCapture = 1
        eAll = 2
    End Enum

    Private Enum ERole
        eConsole = 0
        eMultimedia = 1
        eCommunications = 2
    End Enum

    Private Const STGM_READ As Integer = &H0
    Private Const CLSCTX_ALL As Integer = 23
    Private Const DEVICE_STATE_ACTIVE As Integer = &H1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PROPERTYKEY
        Public fmtid As Guid
        Public pid As Integer
    End Structure

    <StructLayout(LayoutKind.Explicit)>
    Private Structure PROPVARIANT
        <FieldOffset(0)> Public decimalValoreCompleto As Decimal
        <FieldOffset(0)> Public vt As Short
        <FieldOffset(8)> Public puntatore As IntPtr
    End Structure

    <DllImport("ole32.dll")>
    Private Shared Function PropVariantClear(ByRef pvar As PROPVARIANT) As Integer
    End Function

    Private Const ClsidMMDeviceEnumerator As String = "BCDE0395-E52F-467C-8E3D-C4579291692E"
    Private Const ClsidPolicyConfigClient As String = "870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"

    Private Shared Function CreaOggettoCom(clsid As String) As Object
        Try
            Dim tipo As Type = Type.GetTypeFromCLSID(New Guid(clsid))
            If tipo Is Nothing Then Return Nothing
            Return Activator.CreateInstance(tipo)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.CreaOggettoCom: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    <ComImport(), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDeviceEnumerator
        Function EnumAudioEndpoints(dataFlow As EDataFlow, dwStateMask As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef ppDevices As IMMDeviceCollection) As Integer
        Function GetDefaultAudioEndpoint(dataFlow As EDataFlow, role As ERole, <MarshalAs(UnmanagedType.Interface)> ByRef ppDevice As IMMDevice) As Integer
        Function GetDevice(<MarshalAs(UnmanagedType.LPWStr)> pwstrId As String, <MarshalAs(UnmanagedType.Interface)> ByRef ppDevice As IMMDevice) As Integer
    End Interface

    <ComImport(), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDeviceCollection
        Function GetCount(ByRef pcDevices As Integer) As Integer
        Function Item(nDevice As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef ppDevice As IMMDevice) As Integer
    End Interface

    <ComImport(), Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMMDevice
        Function Activate(ByRef iid As Guid, dwClsCtx As Integer, pActivationParams As IntPtr, <MarshalAs(UnmanagedType.IUnknown)> ByRef ppInterface As Object) As Integer
        Function OpenPropertyStore(stgmAccess As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef ppProperties As IPropertyStore) As Integer
        Function GetId(<MarshalAs(UnmanagedType.LPWStr)> ByRef ppstrId As String) As Integer
        Function GetState(ByRef pdwState As Integer) As Integer
    End Interface

    <ComImport(), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPropertyStore
        Function GetCount(ByRef cProps As Integer) As Integer
        Function GetAt(iProp As Integer, ByRef pkey As PROPERTYKEY) As Integer
        Function GetValue(ByRef key As PROPERTYKEY, ByRef pv As PROPVARIANT) As Integer
        Function SetValue(ByRef key As PROPERTYKEY, ByRef pv As PROPVARIANT) As Integer
        Function Commit() As Integer
    End Interface

    <ComImport(), Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IAudioEndpointVolume
        Function RegisterControlChangeNotify(pNotify As IntPtr) As Integer
        Function UnregisterControlChangeNotify(pNotify As IntPtr) As Integer
        Function GetChannelCount(ByRef pnChannelCount As Integer) As Integer
        Function SetMasterVolumeLevel(fLevelDB As Single, ByRef pguidEventContext As Guid) As Integer
        Function SetMasterVolumeLevelScalar(fLevel As Single, ByRef pguidEventContext As Guid) As Integer
        Function GetMasterVolumeLevel(ByRef pfLevelDB As Single) As Integer
        Function GetMasterVolumeLevelScalar(ByRef pfLevel As Single) As Integer
        Function SetChannelVolumeLevel(nChannel As Integer, fLevelDB As Single, ByRef pguidEventContext As Guid) As Integer
        Function SetChannelVolumeLevelScalar(nChannel As Integer, fLevel As Single, ByRef pguidEventContext As Guid) As Integer
        Function GetChannelVolumeLevel(nChannel As Integer, ByRef pfLevelDB As Single) As Integer
        Function GetChannelVolumeLevelScalar(nChannel As Integer, ByRef pfLevel As Single) As Integer
        Function SetMute(<MarshalAs(UnmanagedType.Bool)> bMute As Boolean, ByRef pguidEventContext As Guid) As Integer
        Function GetMute(<MarshalAs(UnmanagedType.Bool)> ByRef pbMute As Boolean) As Integer
    End Interface

    <ComImport(), Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPolicyConfig
        Function GetMixFormat(pszDeviceName As String, ByRef ppFormat As IntPtr) As Integer
        Function GetDeviceFormat(pszDeviceName As String, bDefault As Boolean, ByRef ppFormat As IntPtr) As Integer
        Function ResetDeviceFormat(pszDeviceName As String) As Integer
        Function SetDeviceFormat(pszDeviceName As String, pEndpointFormat As IntPtr, mixFormat As IntPtr) As Integer
        Function GetProcessingPeriod(pszDeviceName As String, bDefault As Boolean, ByRef hnsDefault As Long, ByRef hnsMinimum As Long) As Integer
        Function SetProcessingPeriod(pszDeviceName As String, hnsPeriod As Long) As Integer
        Function GetShareMode(pszDeviceName As String, ByRef pMode As IntPtr) As Integer
        Function SetShareMode(pszDeviceName As String, pMode As IntPtr) As Integer
        Function GetPropertyValue(pszDeviceName As String, bFxStore As Boolean, ByRef key As PROPERTYKEY, ByRef pv As PROPVARIANT) As Integer
        Function SetPropertyValue(pszDeviceName As String, bFxStore As Boolean, ByRef key As PROPERTYKEY, ByRef pv As PROPVARIANT) As Integer
        Function SetDefaultEndpoint(<MarshalAs(UnmanagedType.LPWStr)> pszDeviceName As String, role As ERole) As Integer
        Function SetEndpointVisibility(pszDeviceName As String, bVisible As Boolean) As Integer
    End Interface

    Private Shared ReadOnly ChiavePropFriendlyName As New PROPERTYKEY With {.fmtid = New Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), .pid = 14}

    Private Shared Function CreaEnumeratore() As IMMDeviceEnumerator
        Try
            Dim oggetto As Object = CreaOggettoCom(ClsidMMDeviceEnumerator)
            If oggetto Is Nothing Then Return Nothing
            Return DirectCast(oggetto, IMMDeviceEnumerator)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.CreaEnumeratore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function OttieniNomeDispositivo(dispositivo As IMMDevice) As String
        Try
            If dispositivo Is Nothing Then Return ""
            Dim store As IPropertyStore = Nothing
            If dispositivo.OpenPropertyStore(STGM_READ, store) <> 0 OrElse store Is Nothing Then Return ""
            Dim chiave As PROPERTYKEY = ChiavePropFriendlyName
            Dim valore As New PROPVARIANT()
            If store.GetValue(chiave, valore) <> 0 Then Return ""
            Dim nome As String = ""
            If valore.vt = 31 AndAlso valore.puntatore <> IntPtr.Zero Then
                nome = Marshal.PtrToStringUni(valore.puntatore)
            End If
            PropVariantClear(valore)
            Return If(nome, "")
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniNomeDispositivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function OttieniIdDispositivo(dispositivo As IMMDevice) As String
        Try
            If dispositivo Is Nothing Then Return ""
            Dim id As String = ""
            If dispositivo.GetId(id) <> 0 Then Return ""
            Return If(id, "")
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniIdDispositivo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function OttieniDispositivoPerId(id As String) As IMMDevice
        Try
            If String.IsNullOrWhiteSpace(id) Then Return Nothing
            Dim enumeratore As IMMDeviceEnumerator = CreaEnumeratore()
            If enumeratore Is Nothing Then Return Nothing
            Dim dispositivo As IMMDevice = Nothing
            If enumeratore.GetDevice(id, dispositivo) <> 0 Then Return Nothing
            Return dispositivo
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniDispositivoPerId: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function OttieniDispositivoPredefinito(flusso As EDataFlow) As IMMDevice
        Try
            Dim enumeratore As IMMDeviceEnumerator = CreaEnumeratore()
            If enumeratore Is Nothing Then Return Nothing
            Dim dispositivo As IMMDevice = Nothing
            If enumeratore.GetDefaultAudioEndpoint(flusso, ERole.eMultimedia, dispositivo) <> 0 Then Return Nothing
            Return dispositivo
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniDispositivoPredefinito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function OttieniDispositivoDaId(flusso As EDataFlow, id As String) As IMMDevice
        If String.IsNullOrWhiteSpace(id) Then Return OttieniDispositivoPredefinito(flusso)
        Return OttieniDispositivoPerId(id)
    End Function

    Private Shared Function OttieniControlloVolume(dispositivo As IMMDevice) As IAudioEndpointVolume
        Try
            If dispositivo Is Nothing Then Return Nothing
            Dim iid As Guid = GetType(IAudioEndpointVolume).GUID
            Dim oggetto As Object = Nothing
            If dispositivo.Activate(iid, CLSCTX_ALL, IntPtr.Zero, oggetto) <> 0 Then Return Nothing
            Return DirectCast(oggetto, IAudioEndpointVolume)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniControlloVolume: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ElencaDispositivi(flusso As EDataFlow) As List(Of WGDispositivoAudio)
        Dim elenco As New List(Of WGDispositivoAudio)
        Try
            Dim enumeratore As IMMDeviceEnumerator = CreaEnumeratore()
            If enumeratore Is Nothing Then Return elenco
            Dim collezione As IMMDeviceCollection = Nothing
            If enumeratore.EnumAudioEndpoints(flusso, DEVICE_STATE_ACTIVE, collezione) <> 0 OrElse collezione Is Nothing Then Return elenco
            Dim conteggio As Integer = 0
            collezione.GetCount(conteggio)
            For indice As Integer = 0 To conteggio - 1
                Try
                    Dim dispositivo As IMMDevice = Nothing
                    If collezione.Item(indice, dispositivo) <> 0 OrElse dispositivo Is Nothing Then Continue For
                    Dim id As String = OttieniIdDispositivo(dispositivo)
                    Dim nome As String = OttieniNomeDispositivo(dispositivo)
                    If Not String.IsNullOrWhiteSpace(id) Then
                        elenco.Add(New WGDispositivoAudio With {.Id = id, .Nome = If(String.IsNullOrWhiteSpace(nome), id, nome)})
                    End If
                Catch exSingolo As Exception
                    log.ScriviLog($"WGAudio.ElencaDispositivi (elemento {indice}): {exSingolo.Message}")
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog($"WGAudio.ElencaDispositivi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return elenco
    End Function

    Private Shared Function OttieniVolumePercentuale(flusso As EDataFlow, id As String) As Integer
        Try
            Dim dispositivo As IMMDevice = OttieniDispositivoDaId(flusso, id)
            Dim volume As IAudioEndpointVolume = OttieniControlloVolume(dispositivo)
            If volume Is Nothing Then Return -1
            Dim livello As Single = 0
            If volume.GetMasterVolumeLevelScalar(livello) <> 0 Then Return -1
            Return CInt(Math.Round(livello * 100.0F))
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniVolumePercentuale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return -1
        End Try
    End Function

    Private Shared Sub ImpostaVolumePercentuale(flusso As EDataFlow, id As String, percentuale As Integer)
        Try
            If percentuale < 0 Then percentuale = 0
            If percentuale > 100 Then percentuale = 100
            Dim dispositivo As IMMDevice = OttieniDispositivoDaId(flusso, id)
            Dim volume As IAudioEndpointVolume = OttieniControlloVolume(dispositivo)
            If volume Is Nothing Then Return
            Dim contesto As Guid = Guid.Empty
            volume.SetMasterVolumeLevelScalar(percentuale / 100.0F, contesto)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.ImpostaVolumePercentuale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function OttieniMuto(flusso As EDataFlow, id As String) As Boolean
        Try
            Dim dispositivo As IMMDevice = OttieniDispositivoDaId(flusso, id)
            Dim volume As IAudioEndpointVolume = OttieniControlloVolume(dispositivo)
            If volume Is Nothing Then Return False
            Dim muto As Boolean = False
            volume.GetMute(muto)
            Return muto
        Catch ex As Exception
            log.ScriviLog($"WGAudio.OttieniMuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Sub ImpostaMuto(flusso As EDataFlow, id As String, disattivato As Boolean)
        Try
            Dim dispositivo As IMMDevice = OttieniDispositivoDaId(flusso, id)
            Dim volume As IAudioEndpointVolume = OttieniControlloVolume(dispositivo)
            If volume Is Nothing Then Return
            Dim contesto As Guid = Guid.Empty
            volume.SetMute(disattivato, contesto)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.ImpostaMuto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub ImpostaDispositivoPredefinito(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim oggetto As Object = CreaOggettoCom(ClsidPolicyConfigClient)
            If oggetto Is Nothing Then Return
            Dim policy As IPolicyConfig = DirectCast(oggetto, IPolicyConfig)
            policy.SetDefaultEndpoint(id, ERole.eConsole)
            policy.SetDefaultEndpoint(id, ERole.eMultimedia)
            policy.SetDefaultEndpoint(id, ERole.eCommunications)
        Catch ex As Exception
            log.ScriviLog($"WGAudio.ImpostaDispositivoPredefinito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ElencaAltoparlanti() As List(Of WGDispositivoAudio)
        Return ElencaDispositivi(EDataFlow.eRender)
    End Function

    Public Shared Function ElencaMicrofoni() As List(Of WGDispositivoAudio)
        Return ElencaDispositivi(EDataFlow.eCapture)
    End Function

    Public Shared Function AltoparlantePredefinitoId() As String
        Return OttieniIdDispositivo(OttieniDispositivoPredefinito(EDataFlow.eRender))
    End Function

    Public Shared Function MicrofonoPredefinitoId() As String
        Return OttieniIdDispositivo(OttieniDispositivoPredefinito(EDataFlow.eCapture))
    End Function

    Public Shared Sub ImpostaAltoparlantePredefinito(id As String)
        ImpostaDispositivoPredefinito(id)
    End Sub

    Public Shared Sub ImpostaMicrofonoPredefinito(id As String)
        ImpostaDispositivoPredefinito(id)
    End Sub

    Public Shared Function VolumeAltoparlante(Optional id As String = "") As Integer
        Return OttieniVolumePercentuale(EDataFlow.eRender, id)
    End Function

    Public Shared Function VolumeMicrofono(Optional id As String = "") As Integer
        Return OttieniVolumePercentuale(EDataFlow.eCapture, id)
    End Function

    Public Shared Sub ImpostaVolumeAltoparlante(percentuale As Integer, Optional id As String = "")
        ImpostaVolumePercentuale(EDataFlow.eRender, id, percentuale)
    End Sub

    Public Shared Sub ImpostaVolumeMicrofono(percentuale As Integer, Optional id As String = "")
        ImpostaVolumePercentuale(EDataFlow.eCapture, id, percentuale)
    End Sub

    Public Shared Function AltoparlanteDisattivato(Optional id As String = "") As Boolean
        Return OttieniMuto(EDataFlow.eRender, id)
    End Function

    Public Shared Function MicrofonoDisattivato(Optional id As String = "") As Boolean
        Return OttieniMuto(EDataFlow.eCapture, id)
    End Function

    Public Shared Sub ImpostaAltoparlanteDisattivato(disattivato As Boolean, Optional id As String = "")
        ImpostaMuto(EDataFlow.eRender, id, disattivato)
    End Sub

    Public Shared Sub ImpostaMicrofonoDisattivato(disattivato As Boolean, Optional id As String = "")
        ImpostaMuto(EDataFlow.eCapture, id, disattivato)
    End Sub

    Public Shared Sub AttivaMicrofonoPredefinitoSeChiuso()
        Try
            If MicrofonoDisattivato() Then
                ImpostaMicrofonoDisattivato(False)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGAudio.AttivaMicrofonoPredefinitoSeChiuso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Sub AumentaVolumeMicrofonoPredefinitoSeBasso(sogliaPercento As Integer, obiettivoPercento As Integer)
        Try
            Dim attuale As Integer = VolumeMicrofono()
            If attuale >= 0 AndAlso attuale < sogliaPercento Then
                ImpostaVolumeMicrofono(obiettivoPercento)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGAudio.AumentaVolumeMicrofonoPredefinitoSeBasso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

End Class