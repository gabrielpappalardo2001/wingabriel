' ============================================================================================
' WGWifi - reti Wi-Fi con la Native Wifi API di Windows (wlanapi.dll), senza diritti di amministratore
' ============================================================================================
'
' Elenco delle reti vicine, collegamento (con la password, creando il profilo come fa Windows),
' disconnessione, reti salvate da dimenticare, rete a cui si è collegati. Accendere e spegnere il
' Wi-Fi è in WGRadio, con le API di Windows fatte apposta.
' Il collegamento aspetta fino a 20 secondi l'esito: se non riesce e il profilo era nuovo, il profilo
' si cancella, così una password sbagliata non resta salvata.
'
'   Dim reti As List(Of ReteWifi) = Await WGWifi.CercaRetiAsync()
'   Dim esito As String = Await WGWifi.ConnettiAsync(rete, password)   ' "" se collegato
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Security.Cryptography.X509Certificates
Imports System.Text
Imports System.Threading.Tasks
Imports wgcontrols

Public Class ReteWifi
    Public Property Ssid As String = ""
    Public Property SsidByte As Byte() = New Byte() {}
    ' Qualità del segnale da 0 a 100.
    Public Property Segnale As Integer
    Public Property Protetta As Boolean
    Public Property Autenticazione As Integer
    Public Property Cifratura As Integer
    Public Property Connessa As Boolean
    ' Nome del profilo salvato in Windows, vuoto se la rete non è salvata.
    Public Property Profilo As String = ""
    Public Property Collegabile As Boolean = True
    ' Rete che non trasmette il nome: il nome lo scrive l'utente e il profilo la cerca lo stesso.
    Public Property Nascosta As Boolean

    ''' <summary>Rete nascosta scritta dall'utente: nome, protezione (AUTH_*) e cifratura AES se protetta.</summary>
    Public Shared Function Nuova(nome As String, autenticazione As Integer) As ReteWifi
        Dim protetta As Boolean = autenticazione <> WGWifi.AUTH_OPEN
        Return New ReteWifi With {.Ssid = nome, .SsidByte = Encoding.UTF8.GetBytes(nome), .Autenticazione = autenticazione,
                                  .Cifratura = If(protetta OrElse autenticazione = WGWifi.AUTH_OWE, 4, 0), .Protetta = protetta AndAlso autenticazione <> WGWifi.AUTH_OWE, .Nascosta = True}
    End Function

    Public ReadOnly Property NomeMostrato As String
        Get
            Return If(Ssid = "", "Rete nascosta", Ssid)
        End Get
    End Property

    ''' <summary>Reti aziendali (802.1X, come eduroam): ci si collega con nome utente e password (ConnettiAziendaleAsync).</summary>
    Public ReadOnly Property Aziendale As Boolean
        Get
            Return Autenticazione = WGWifi.AUTH_WPA OrElse Autenticazione = WGWifi.AUTH_RSNA OrElse Autenticazione = WGWifi.AUTH_WPA3_ENT192 OrElse Autenticazione = WGWifi.AUTH_WPA3_ENT
        End Get
    End Property

    Public ReadOnly Property Descrizione As String
        Get
            Dim parti As New List(Of String) From {NomeMostrato}
            If Connessa Then parti.Add("connessa")
            parti.Add("segnale " & Segnale & " per cento")
            parti.Add(If(Protetta, If(Aziendale, "rete aziendale", "protetta"), "aperta"))
            If Profilo <> "" AndAlso Not Connessa Then parti.Add("salvata")
            Return String.Join(", ", parti)
        End Get
    End Property
End Class

Public NotInheritable Class WGWifi

#Region "Native Wifi API"

    Private Const VERSIONE_CLIENT As UInteger = 2
    Private Const ERRORE_NESSUNO As UInteger = 0
    Private Const ERRORE_ACCESSO_NEGATO As UInteger = 5
    Private Const WLAN_PROFILE_USER As UInteger = 2
    Private Const WLAN_AVAILABLE_NETWORK_CONNECTED As UInteger = 1
    Private Const WLAN_AVAILABLE_NETWORK_HAS_PROFILE As UInteger = 2
    Private Const OPCODE_RADIO As Integer = 4
    Private Const OPCODE_CONNESSIONE_CORRENTE As Integer = 7
    Private Const STATO_CONNESSO As Integer = 1
    Private Const STATO_DISCONNESSO As Integer = 4
    Private Const MODO_PROFILO As Integer = 0
    Private Const DOT11_BSS_INFRASTRUTTURA As Integer = 1
    Private Const DIMENSIONE_RETE As Integer = 628
    Private Const DIMENSIONE_INTERFACCIA As Integer = 532

    Friend Const AUTH_OPEN As Integer = 1
    Friend Const AUTH_SHARED As Integer = 2
    Friend Const AUTH_WPA As Integer = 3
    Friend Const AUTH_WPA_PSK As Integer = 4
    Friend Const AUTH_RSNA As Integer = 6
    Friend Const AUTH_RSNA_PSK As Integer = 7
    Friend Const AUTH_WPA3_ENT192 As Integer = 8
    Friend Const AUTH_WPA3_SAE As Integer = 9
    Friend Const AUTH_OWE As Integer = 10
    Friend Const AUTH_WPA3_ENT As Integer = 11
    Private Const CIPHER_NESSUNA As Integer = 0
    Private Const CIPHER_WEP40 As Integer = 1
    Private Const CIPHER_TKIP As Integer = 2
    Private Const CIPHER_CCMP As Integer = 4
    Private Const CIPHER_WEP104 As Integer = 5
    Private Const CIPHER_WEP As Integer = &H101

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure WLAN_CONNECTION_PARAMETERS
        Public wlanConnectionMode As Integer
        <MarshalAs(UnmanagedType.LPWStr)> Public strProfile As String
        Public pDot11Ssid As IntPtr
        Public pDesiredBssidList As IntPtr
        Public dot11BssType As Integer
        Public dwFlags As UInteger
    End Structure

    <DllImport("wlanapi.dll")>
    Private Shared Function WlanOpenHandle(dwClientVersion As UInteger, pReserved As IntPtr, ByRef pdwNegotiatedVersion As UInteger, ByRef phClientHandle As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanCloseHandle(hClientHandle As IntPtr, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Sub WlanFreeMemory(pMemory As IntPtr)
    End Sub
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanEnumInterfaces(hClientHandle As IntPtr, pReserved As IntPtr, ByRef ppInterfaceList As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanScan(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, pDot11Ssid As IntPtr, pIeData As IntPtr, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanGetAvailableNetworkList(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, dwFlags As UInteger, pReserved As IntPtr, ByRef ppAvailableNetworkList As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WlanSetProfile(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, dwFlags As UInteger, strProfileXml As String, strAllUserProfileSecurity As String, bOverwrite As Boolean, pReserved As IntPtr, ByRef pdwReasonCode As UInteger) As UInteger
    End Function
    <DllImport("wlanapi.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WlanSetProfileEapXmlUserData(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, strProfileName As String, dwFlags As UInteger, strEapXmlUserData As String, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanConnect(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, ByRef pConnectionParameters As WLAN_CONNECTION_PARAMETERS, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanDisconnect(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function WlanDeleteProfile(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, strProfileName As String, pReserved As IntPtr) As UInteger
    End Function
    <DllImport("wlanapi.dll")>
    Private Shared Function WlanQueryInterface(hClientHandle As IntPtr, ByRef pInterfaceGuid As Guid, OpCode As Integer, pReserved As IntPtr, ByRef pdwDataSize As UInteger, ByRef ppData As IntPtr, ByRef pWlanOpcodeValueType As Integer) As UInteger
    End Function

#End Region

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ' Ogni operazione apre e chiude la sua sessione con il servizio Wi-Fi: così non resta niente appeso.
    Private Shared Function ConSessione(Of T)(azione As Func(Of IntPtr, Guid, T), predefinito As T) As T
        Dim sessione As IntPtr = IntPtr.Zero
        Dim versione As UInteger
        Try
            If WlanOpenHandle(VERSIONE_CLIENT, IntPtr.Zero, versione, sessione) <> ERRORE_NESSUNO Then Return predefinito
            Dim interfaccia As Guid
            If Not PrimaInterfaccia(sessione, interfaccia) Then Return predefinito
            Return azione(sessione, interfaccia)
        Catch ex As Exception
            log.ScriviLog("WGWifi.ConSessione: " & ex.Message)
            Return predefinito
        Finally
            If sessione <> IntPtr.Zero Then WlanCloseHandle(sessione, IntPtr.Zero)
        End Try
    End Function

    Private Shared Function PrimaInterfaccia(sessione As IntPtr, ByRef interfaccia As Guid) As Boolean
        Dim elenco As IntPtr = IntPtr.Zero
        Try
            If WlanEnumInterfaces(sessione, IntPtr.Zero, elenco) <> ERRORE_NESSUNO Then Return False
            Dim quante As Integer = Marshal.ReadInt32(elenco, 0)
            If quante = 0 Then Return False
            Dim byteGuid(15) As Byte
            Marshal.Copy(elenco + 8, byteGuid, 0, 16)
            interfaccia = New Guid(byteGuid)
            Return True
        Finally
            If elenco <> IntPtr.Zero Then WlanFreeMemory(elenco)
        End Try
    End Function

    ''' <summary>Codice dell'ultima lettura delle reti (0 = riuscita, 5 = accesso negato).</summary>
    Public Shared Property UltimoErrore As UInteger

    ''' <summary>
    ''' Windows 11 dà i nomi delle reti Wi-Fi solo ai programmi con il permesso Posizione: senza, l'elenco
    ''' è negato. È spesso il motivo per cui "il Wi-Fi non trova niente".
    ''' </summary>
    Public Shared ReadOnly Property ServePermessoPosizione As Boolean
        Get
            Return UltimoErrore = ERRORE_ACCESSO_NEGATO
        End Get
    End Property

    ''' <summary>
    ''' Chiede a Windows il permesso Posizione per WinGabriel (Windows può mostrare la sua domanda).
    ''' Vero se concesso; se i servizi di posizione sono spenti va attivato dalle impostazioni.
    ''' </summary>
    Public Shared Async Function ChiediPermessoPosizioneAsync() As Task(Of Boolean)
        Try
            Dim stato As Global.Windows.Devices.Geolocation.GeolocationAccessStatus = Await Global.Windows.Devices.Geolocation.Geolocator.RequestAccessAsync()
            Return stato = Global.Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed
        Catch ex As Exception
            log.ScriviLog("WGWifi.ChiediPermessoPosizioneAsync: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Vero se il computer ha una scheda Wi-Fi.</summary>
    Public Shared Function Disponibile() As Boolean
        Return ConSessione(Function(s, i) True, False)
    End Function

#Region "Reti"

    ''' <summary>Chiede una nuova scansione, aspetta che finisca e restituisce le reti, dalla più forte.</summary>
    Public Shared Async Function CercaRetiAsync() As Task(Of List(Of ReteWifi))
        ConSessione(Function(s, i)
                        WlanScan(s, i, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
                        Return True
                    End Function, False)
        ' La scansione dura qualche secondo: Windows avvisa con una notifica, qui basta aspettare.
        Await Task.Delay(3500)
        Return Reti()
    End Function

    ''' <summary>Le reti già note a Windows adesso, senza nuova scansione.</summary>
    Public Shared Function Reti() As List(Of ReteWifi)
        Return ConSessione(Function(sessione, interfaccia)
                               Dim risultato As New List(Of ReteWifi)()
                               Dim elenco As IntPtr = IntPtr.Zero
                               Try
                                   Dim esito As UInteger = WlanGetAvailableNetworkList(sessione, interfaccia, 0UI, IntPtr.Zero, elenco)
                                   UltimoErrore = esito
                                   If esito <> ERRORE_NESSUNO Then Return risultato
                                   Dim quante As Integer = Marshal.ReadInt32(elenco, 0)
                                   For n As Integer = 0 To quante - 1
                                       Dim voce As IntPtr = elenco + 8 + n * DIMENSIONE_RETE
                                       Dim rete As New ReteWifi()
                                       rete.Profilo = Marshal.PtrToStringUni(voce).TrimEnd(ChrW(0))
                                       Dim lunghezza As Integer = Math.Min(32, Marshal.ReadInt32(voce, 512))
                                       Dim ssid(lunghezza - 1) As Byte
                                       If lunghezza > 0 Then Marshal.Copy(voce + 516, ssid, 0, lunghezza)
                                       rete.SsidByte = ssid
                                       rete.Ssid = Encoding.UTF8.GetString(ssid)
                                       If Marshal.ReadInt32(voce, 548) <> DOT11_BSS_INFRASTRUTTURA Then Continue For
                                       rete.Collegabile = Marshal.ReadInt32(voce, 556) <> 0
                                       rete.Segnale = Marshal.ReadInt32(voce, 604)
                                       rete.Protetta = Marshal.ReadInt32(voce, 608) <> 0
                                       rete.Autenticazione = Marshal.ReadInt32(voce, 612)
                                       rete.Cifratura = Marshal.ReadInt32(voce, 616)
                                       Dim segnali As UInteger = CUInt(Marshal.ReadInt32(voce, 620))
                                       rete.Connessa = (segnali And WLAN_AVAILABLE_NETWORK_CONNECTED) <> 0
                                       If (segnali And WLAN_AVAILABLE_NETWORK_HAS_PROFILE) = 0 Then rete.Profilo = ""
                                       risultato.Add(rete)
                                   Next
                               Finally
                                   If elenco <> IntPtr.Zero Then WlanFreeMemory(elenco)
                               End Try
                               ' Windows elenca la stessa rete più volte (con e senza profilo): se ne tiene una sola,
                               ' preferendo quella connessa, poi quella salvata.
                               Return risultato.GroupBy(Function(r) r.Ssid & "|" & r.Autenticazione).
                                   Select(Function(g) g.OrderByDescending(Function(r) r.Connessa).ThenByDescending(Function(r) r.Profilo <> "").ThenByDescending(Function(r) r.Segnale).First()).
                                   OrderByDescending(Function(r) r.Connessa).ThenByDescending(Function(r) r.Segnale).ToList()
                           End Function, New List(Of ReteWifi)())
    End Function

    ''' <summary>La rete a cui si è collegati e il segnale; ssid vuoto se non si è collegati.</summary>
    Public Shared Function ConnessioneCorrente(ByRef ssid As String, ByRef segnale As Integer) As Boolean
        Dim nome As String = ""
        Dim qualita As Integer = 0
        Dim collegato As Boolean = ConSessione(Function(sessione, interfaccia)
                                                   Dim dimensione As UInteger
                                                   Dim dati As IntPtr = IntPtr.Zero
                                                   Dim tipo As Integer
                                                   Try
                                                       If WlanQueryInterface(sessione, interfaccia, OPCODE_CONNESSIONE_CORRENTE, IntPtr.Zero, dimensione, dati, tipo) <> ERRORE_NESSUNO Then Return False
                                                       If Marshal.ReadInt32(dati, 0) <> STATO_CONNESSO Then Return False
                                                       Dim lunghezza As Integer = Math.Min(32, Marshal.ReadInt32(dati, 520))
                                                       Dim byteSsid(Math.Max(0, lunghezza - 1)) As Byte
                                                       If lunghezza > 0 Then Marshal.Copy(dati + 524, byteSsid, 0, lunghezza)
                                                       nome = If(lunghezza > 0, Encoding.UTF8.GetString(byteSsid, 0, lunghezza), "")
                                                       qualita = Marshal.ReadInt32(dati, 576)
                                                       Return True
                                                   Finally
                                                       If dati <> IntPtr.Zero Then WlanFreeMemory(dati)
                                                   End Try
                                               End Function, False)
        ssid = nome
        segnale = qualita
        Return collegato
    End Function

    ' Stato della connessione: 1 connesso, 4 disconnesso, altri valori durante il collegamento.
    Private Shared Function StatoInterfaccia() As Integer
        Return ConSessione(Function(sessione, interfaccia)
                               Dim dimensione As UInteger
                               Dim dati As IntPtr = IntPtr.Zero
                               Dim tipo As Integer
                               Try
                                   If WlanQueryInterface(sessione, interfaccia, OPCODE_CONNESSIONE_CORRENTE, IntPtr.Zero, dimensione, dati, tipo) <> ERRORE_NESSUNO Then Return STATO_DISCONNESSO
                                   Return Marshal.ReadInt32(dati, 0)
                               Finally
                                   If dati <> IntPtr.Zero Then WlanFreeMemory(dati)
                               End Try
                           End Function, STATO_DISCONNESSO)
    End Function

#End Region

#Region "Collegamento"

    ''' <summary>
    ''' Si collega alla rete. Per una rete protetta non salvata serve la password. Restituisce "" se
    ''' collegato, altrimenti il motivo da leggere all'utente.
    ''' </summary>
    Public Shared Async Function ConnettiAsync(rete As ReteWifi, Optional password As String = "", Optional automatica As Boolean = True) As Task(Of String)
        Try
            If rete Is Nothing Then Return "Rete non valida"
            If rete.Aziendale AndAlso rete.Profilo = "" Then Return "Per questa rete aziendale servono nome utente e password"
            Dim profilo As String = rete.Profilo
            Dim profiloNuovo As Boolean = False
            If Not rete.Aziendale AndAlso (profilo = "" OrElse Not String.IsNullOrEmpty(password)) Then
                If rete.Protetta AndAlso String.IsNullOrEmpty(password) Then Return "Serve la password"
                Dim errore As String = ""
                profilo = CreaProfilo(rete, password, automatica, errore)
                If profilo = "" Then Return errore
                profiloNuovo = True
            End If
            Return Await CollegaConProfiloAsync(rete, profilo, profiloNuovo,
                                                If(rete.Aziendale, "Collegamento non riuscito: nome utente o password potrebbero essere sbagliati",
                                                   If(rete.Protetta, "Collegamento non riuscito: la password potrebbe essere sbagliata, oppure la rete è troppo lontana", "Collegamento non riuscito: la rete non risponde")))
        Catch ex As Exception
            log.ScriviLog("WGWifi.ConnettiAsync: " & ex.Message)
            Return "Errore durante il collegamento"
        End Try
    End Function

    ''' <summary>
    ''' Rete aziendale (WPA o WPA2 Enterprise, WPA3 Enterprise) con nome utente e password, come eduroam:
    ''' PEAP con MSCHAPv2, il metodo più diffuso. Il nome utente può essere "DOMINIO\utente".
    ''' Restituisce "" se collegato, altrimenti il motivo. Se non riesce il profilo nuovo si cancella.
    ''' </summary>
    Public Shared Async Function ConnettiAziendaleAsync(rete As ReteWifi, utente As String, password As String) As Task(Of String)
        Try
            If rete Is Nothing OrElse Not rete.Aziendale Then Return "Rete non valida"
            If String.IsNullOrWhiteSpace(utente) OrElse String.IsNullOrEmpty(password) Then Return "Servono nome utente e password"
            Dim errore As String = ""
            Dim profilo As String = CreaProfiloAziendale(rete, errore)
            If profilo = "" Then Return errore
            Dim dominio As String = ""
            Dim nome As String = utente.Trim()
            If nome.Contains("\") Then
                dominio = nome.Substring(0, nome.IndexOf("\"c))
                nome = nome.Substring(nome.IndexOf("\"c) + 1)
            End If
            Dim credenziali As String =
                "<EapHostUserCredentials xmlns=""http://www.microsoft.com/provisioning/EapHostUserCredentials"" xmlns:eapCommon=""http://www.microsoft.com/provisioning/EapCommonTypes"" xmlns:baseEap=""http://www.microsoft.com/provisioning/BaseEapMethodUserCredentials"">" &
                "<EapMethod><eapCommon:Type>25</eapCommon:Type><eapCommon:AuthorId>0</eapCommon:AuthorId></EapMethod>" &
                "<Credentials xmlns:eapUser=""http://www.microsoft.com/provisioning/EapUserPropertiesV1"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:baseEap=""http://www.microsoft.com/provisioning/BaseEapUserPropertiesV1"" xmlns:MsPeap=""http://www.microsoft.com/provisioning/MsPeapUserPropertiesV1"" xmlns:MsChapV2=""http://www.microsoft.com/provisioning/MsChapV2UserPropertiesV1"">" &
                "<baseEap:Eap><baseEap:Type>25</baseEap:Type><MsPeap:EapType><MsPeap:RoutingIdentity>" & SecurityElement.Escape(utente.Trim()) & "</MsPeap:RoutingIdentity>" &
                "<baseEap:Eap><baseEap:Type>26</baseEap:Type><MsChapV2:EapType><MsChapV2:Username>" & SecurityElement.Escape(nome) & "</MsChapV2:Username>" &
                "<MsChapV2:Password>" & SecurityElement.Escape(password) & "</MsChapV2:Password><MsChapV2:LogonDomain>" & SecurityElement.Escape(dominio) & "</MsChapV2:LogonDomain>" &
                "</MsChapV2:EapType></baseEap:Eap></MsPeap:EapType></baseEap:Eap></Credentials></EapHostUserCredentials>"
            Dim esito As UInteger = ConSessione(Function(sessione, interfaccia) WlanSetProfileEapXmlUserData(sessione, interfaccia, profilo, 0UI, credenziali, IntPtr.Zero), UInteger.MaxValue)
            If esito <> ERRORE_NESSUNO Then
                DimenticaProfilo(profilo)
                Return "Windows non ha accettato nome utente e password, codice " & esito
            End If
            Return Await CollegaConProfiloAsync(rete, profilo, True, "Collegamento non riuscito: nome utente o password potrebbero essere sbagliati, oppure la rete usa un metodo diverso da PEAP")
        Catch ex As Exception
            log.ScriviLog("WGWifi.ConnettiAziendaleAsync: " & ex.Message)
            Return "Errore durante il collegamento"
        End Try
    End Function

    ' Profilo aziendale PEAP-MSCHAPv2. Il certificato del server si controlla davvero (contro le stesse radici
    ' fidate di Windows, prese al momento), ma senza mai mostrare la finestra di Windows che chiede di fidarsi:
    ' se il certificato non risulta fidato il collegamento fallisce in silenzio, come un collegamento sbagliato
    ' qualsiasi, e lo dice la sintesi (CollegaConProfiloAsync). Cosi' un finto punto d'accesso con lo stesso nome
    ' non puo' farsi credere il vero server RADIUS e rubare nome utente e password.
    Private Shared Function RadiciFidateXml() As String
        Dim sb As New StringBuilder()
        Try
            Dim impronte As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each posizione As StoreLocation In New StoreLocation() {StoreLocation.LocalMachine, StoreLocation.CurrentUser}
                Try
                    Using store As New X509Store(StoreName.Root, posizione)
                        store.Open(OpenFlags.ReadOnly)
                        For Each certificato As X509Certificate2 In store.Certificates
                            Try
                                If impronte.Add(certificato.Thumbprint) Then
                                    sb.Append("<TrustedRootCA>").Append(certificato.Thumbprint).Append("</TrustedRootCA>")
                                End If
                            Catch exCert As Exception
                                log.ScriviLog("WGWifi.RadiciFidateXml (certificato): " & exCert.Message)
                            End Try
                        Next
                    End Using
                Catch exStore As Exception
                    log.ScriviLog("WGWifi.RadiciFidateXml (" & posizione.ToString() & "): " & exStore.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGWifi.RadiciFidateXml: " & ex.Message)
        End Try
        Return sb.ToString()
    End Function

    Private Shared Function CreaProfiloAziendale(rete As ReteWifi, ByRef errore As String) As String
        Dim autenticazione As String
        Select Case rete.Autenticazione
            Case AUTH_WPA : autenticazione = "WPA"
            Case AUTH_RSNA : autenticazione = "WPA2"
            Case AUTH_WPA3_ENT : autenticazione = "WPA3ENT"
            Case Else
                errore = "Questa rete aziendale richiede un certificato personale: con nome utente e password non ci si può collegare"
                Return ""
        End Select
        Dim nome As String = rete.Ssid
        Dim esadecimale As String = BitConverter.ToString(rete.SsidByte).Replace("-", "")
        Dim cifratura As String = If(rete.Cifratura = CIPHER_TKIP, "TKIP", "AES")
        Dim xml As New StringBuilder()
        xml.Append("<?xml version=""1.0""?><WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">")
        xml.Append("<name>").Append(SecurityElement.Escape(nome)).Append("</name>")
        xml.Append("<SSIDConfig><SSID><hex>").Append(esadecimale).Append("</hex><name>").Append(SecurityElement.Escape(nome)).Append("</name></SSID>")
        If rete.Nascosta Then xml.Append("<nonBroadcast>true</nonBroadcast>")
        xml.Append("</SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode>")
        xml.Append("<MSM><security><authEncryption><authentication>").Append(autenticazione).Append("</authentication><encryption>").Append(cifratura).Append("</encryption><useOneX>true</useOneX></authEncryption>")
        xml.Append("<OneX xmlns=""http://www.microsoft.com/networking/OneX/v1""><authMode>user</authMode><EAPConfig>")
        xml.Append("<EapHostConfig xmlns=""http://www.microsoft.com/provisioning/EapHostConfig""><EapMethod>")
        xml.Append("<Type xmlns=""http://www.microsoft.com/provisioning/EapCommonTypes"">25</Type><VendorId xmlns=""http://www.microsoft.com/provisioning/EapCommonTypes"">0</VendorId>")
        xml.Append("<VendorType xmlns=""http://www.microsoft.com/provisioning/EapCommonTypes"">0</VendorType><AuthorId xmlns=""http://www.microsoft.com/provisioning/EapCommonTypes"">0</AuthorId></EapMethod>")
        xml.Append("<Config xmlns=""http://www.microsoft.com/provisioning/EapHostConfig""><Eap xmlns=""http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1""><Type>25</Type>")
        xml.Append("<EapType xmlns=""http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1""><ServerValidation><DisableUserPromptForServerValidation>true</DisableUserPromptForServerValidation>").Append(RadiciFidateXml()).Append("</ServerValidation>")
        xml.Append("<FastReconnect>true</FastReconnect><InnerEapOptional>false</InnerEapOptional>")
        xml.Append("<Eap xmlns=""http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1""><Type>26</Type><EapType xmlns=""http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1""><UseWinLogonCredentials>false</UseWinLogonCredentials></EapType></Eap>")
        xml.Append("<EnableQuarantineChecks>false</EnableQuarantineChecks><RequireCryptoBinding>false</RequireCryptoBinding>")
        xml.Append("<PeapExtensions><PerformServerValidation xmlns=""http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV2"">true</PerformServerValidation><AcceptServerName xmlns=""http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV2"">true</AcceptServerName></PeapExtensions>")
        xml.Append("</EapType></Eap></Config></EapHostConfig></EAPConfig></OneX></security></MSM></WLANProfile>")
        Dim testoXml As String = xml.ToString()
        Dim esito As UInteger = ConSessione(Function(sessione, interfaccia)
                                                 Dim motivo As UInteger
                                                 Dim risultato As UInteger = WlanSetProfile(sessione, interfaccia, 0UI, testoXml, Nothing, True, IntPtr.Zero, motivo)
                                                 If risultato = ERRORE_ACCESSO_NEGATO Then risultato = WlanSetProfile(sessione, interfaccia, WLAN_PROFILE_USER, testoXml, Nothing, True, IntPtr.Zero, motivo)
                                                 Return risultato
                                             End Function, UInteger.MaxValue)
        If esito <> ERRORE_NESSUNO Then
            errore = "Windows non ha accettato le impostazioni della rete aziendale, codice " & esito
            Return ""
        End If
        Return nome
    End Function

    ' Avvia il collegamento con il profilo e aspetta l'esito, fino a 20 secondi. Se non riesce e il
    ' profilo era nuovo lo si cancella, così niente di sbagliato resta salvato.
    Private Shared Async Function CollegaConProfiloAsync(rete As ReteWifi, profilo As String, profiloNuovo As Boolean, motivoFallimento As String) As Task(Of String)
        Try
            Dim avvio As UInteger = ConSessione(Function(sessione, interfaccia)
                                                     Dim parametri As New WLAN_CONNECTION_PARAMETERS With {
                                                         .wlanConnectionMode = MODO_PROFILO, .strProfile = profilo,
                                                         .dot11BssType = DOT11_BSS_INFRASTRUTTURA, .dwFlags = 0}
                                                     Return WlanConnect(sessione, interfaccia, parametri, IntPtr.Zero)
                                                 End Function, UInteger.MaxValue)
            If avvio <> ERRORE_NESSUNO Then
                If profiloNuovo Then DimenticaProfilo(profilo)
                Return "Windows non ha accettato il collegamento, codice " & avvio
            End If
            ' Si aspetta l'esito: connesso, oppure di nuovo disconnesso dopo aver provato.
            Dim inizio As DateTime = DateTime.Now
            Dim haProvato As Boolean = False
            While (DateTime.Now - inizio).TotalSeconds < 20
                Await Task.Delay(500)
                Dim stato As Integer = StatoInterfaccia()
                If stato = STATO_CONNESSO Then
                    Dim ssidCorrente As String = ""
                    Dim segnale As Integer
                    ConnessioneCorrente(ssidCorrente, segnale)
                    If ssidCorrente = rete.Ssid OrElse rete.Ssid = "" Then Return ""
                ElseIf stato <> STATO_DISCONNESSO Then
                    haProvato = True
                ElseIf haProvato AndAlso (DateTime.Now - inizio).TotalSeconds > 6 Then
                    Exit While
                End If
            End While
            If profiloNuovo Then DimenticaProfilo(profilo)
            Return motivoFallimento
        Catch ex As Exception
            log.ScriviLog("WGWifi.CollegaConProfiloAsync: " & ex.Message)
            Return "Errore durante il collegamento"
        End Try
    End Function

    ' Profilo come quello che crea Windows: nome della rete, sicurezza e password.
    Private Shared Function CreaProfilo(rete As ReteWifi, password As String, automatica As Boolean, ByRef errore As String) As String
        Dim autenticazione As String
        Dim cifratura As String
        Dim chiave As String = ""
        Select Case rete.Autenticazione
            Case AUTH_OPEN
                If rete.Cifratura = CIPHER_WEP OrElse rete.Cifratura = CIPHER_WEP40 OrElse rete.Cifratura = CIPHER_WEP104 Then
                    autenticazione = "open" : cifratura = "WEP" : chiave = "networkKey"
                Else
                    autenticazione = "open" : cifratura = "none"
                End If
            Case AUTH_SHARED
                autenticazione = "shared" : cifratura = "WEP" : chiave = "networkKey"
            Case AUTH_WPA_PSK
                autenticazione = "WPAPSK" : cifratura = If(rete.Cifratura = CIPHER_TKIP, "TKIP", "AES") : chiave = "passPhrase"
            Case AUTH_RSNA_PSK
                autenticazione = "WPA2PSK" : cifratura = If(rete.Cifratura = CIPHER_TKIP, "TKIP", "AES") : chiave = "passPhrase"
            Case AUTH_WPA3_SAE
                autenticazione = "WPA3SAE" : cifratura = "AES" : chiave = "passPhrase"
            Case AUTH_OWE
                autenticazione = "OWE" : cifratura = "AES"
            Case Else
                errore = "Questo tipo di protezione della rete non è gestito da WinGabriel"
                Return ""
        End Select
        If rete.Ssid = "" OrElse rete.SsidByte Is Nothing OrElse rete.SsidByte.Length = 0 Then
            errore = "Per una rete nascosta serve il nome della rete"
            Return ""
        End If
        Dim nome As String = rete.Ssid
        Dim esadecimale As String = BitConverter.ToString(rete.SsidByte).Replace("-", "")
        Dim xml As New StringBuilder()
        xml.Append("<?xml version=""1.0""?><WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">")
        xml.Append("<name>").Append(SecurityElement.Escape(nome)).Append("</name>")
        xml.Append("<SSIDConfig><SSID><hex>").Append(esadecimale).Append("</hex><name>").Append(SecurityElement.Escape(nome)).Append("</name></SSID>")
        ' Rete nascosta: Windows la cerca anche se non trasmette il nome.
        If rete.Nascosta Then xml.Append("<nonBroadcast>true</nonBroadcast>")
        xml.Append("</SSIDConfig>")
        xml.Append("<connectionType>ESS</connectionType><connectionMode>").Append(If(automatica, "auto", "manual")).Append("</connectionMode>")
        xml.Append("<MSM><security><authEncryption><authentication>").Append(autenticazione).Append("</authentication><encryption>").Append(cifratura).Append("</encryption><useOneX>false</useOneX></authEncryption>")
        If chiave <> "" Then
            xml.Append("<sharedKey><keyType>").Append(chiave).Append("</keyType><protected>false</protected><keyMaterial>").Append(SecurityElement.Escape(password)).Append("</keyMaterial></sharedKey>")
        End If
        xml.Append("</security></MSM></WLANProfile>")
        Dim testoXml As String = xml.ToString()
        Dim esito As UInteger = ConSessione(Function(sessione, interfaccia)
                                                 Dim motivo As UInteger
                                                 ' Profilo per tutti gli utenti; se Windows lo nega, profilo solo per questo utente.
                                                 Dim risultato As UInteger = WlanSetProfile(sessione, interfaccia, 0UI, testoXml, Nothing, True, IntPtr.Zero, motivo)
                                                 If risultato = ERRORE_ACCESSO_NEGATO Then risultato = WlanSetProfile(sessione, interfaccia, WLAN_PROFILE_USER, testoXml, Nothing, True, IntPtr.Zero, motivo)
                                                 Return risultato
                                             End Function, UInteger.MaxValue)
        If esito <> ERRORE_NESSUNO Then
            errore = If(esito = 1206UI, "La password non è valida: per le reti WPA deve avere da 8 a 63 caratteri", "Windows non ha accettato le impostazioni della rete, codice " & esito)
            Return ""
        End If
        Return nome
    End Function

    Public Shared Function Disconnetti() As Boolean
        Return ConSessione(Function(sessione, interfaccia) WlanDisconnect(sessione, interfaccia, IntPtr.Zero) = ERRORE_NESSUNO, False)
    End Function

    ''' <summary>Dimentica la rete salvata (il profilo con la password).</summary>
    Public Shared Function DimenticaProfilo(profilo As String) As Boolean
        If String.IsNullOrEmpty(profilo) Then Return False
        Return ConSessione(Function(sessione, interfaccia) WlanDeleteProfile(sessione, interfaccia, profilo, IntPtr.Zero) = ERRORE_NESSUNO, False)
    End Function

#End Region

End Class
