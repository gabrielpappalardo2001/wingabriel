' ============================================================================================
' WGAvvisiSistema - avvisi di connessione, memoria e disco, tutti configurabili
' ============================================================================================
'
' - Connessione: quando internet si perde o torna (con il tipo: Wi-Fi e nome della rete, cavo).
'   Si avvisa solo quando lo stato resta uguale per qualche secondo, per non parlare a ogni
'   sussulto della rete.
' - Memoria: quando la memoria usata supera la soglia (80, 85, 90 o 95 per cento).
' - Disco: quando lo spazio libero di un disco fisso scende sotto la soglia (5, 10, 15 o 20 per cento).
' Ogni avviso si ripete al massimo ogni quindici minuti (il disco ogni ora) finché la situazione
' resta la stessa, e finisce nel centro notifiche (rispettando il non disturbare) se WinGabriel
' collega AvvisoNotifica; altrimenti viene solo letto. Invio sulla notifica porta dove serve: il Wi-Fi
' per la connessione, l'elenco delle applicazioni (F10) per la memoria, il cestino per il disco.
'
'   WGAvvisiSistema.Avvia()
'   WGAvvisiSistema.MostraImpostazioni()
'   WGAvvisiSistema.AvvisoNotifica = Sub(testo, azione) ...   ' dal programma principale
'   WGAvvisiSistema.NomeReteWifi = Function() ...        ' dal programma principale
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net.NetworkInformation
Imports System.Windows.Forms
Imports WGDb

Public NotInheritable Class WGAvvisiSistema

    Private Shared ReadOnly log As New Logger()

    Private Const ChiaveConnessione As String = "avviso_connessione"
    Private Const ChiaveMemoria As String = "avviso_memoria_soglia"
    Private Const ChiaveDisco As String = "avviso_disco_soglia"
    Private Const MemoriaPredefinita As Integer = 90
    Private Const DiscoPredefinito As Integer = 10

    Private Shared WithEvents _timer As Timer
    Private Shared _avviato As Boolean
    Private Shared _contesto As Threading.SynchronizationContext

    ' Connessione: ultimo stato annunciato e stato in osservazione.
    Private Shared _connessoAnnunciato As Boolean?
    Private Shared _connessoOsservato As Boolean
    Private Shared _osservatoDa As DateTime
    Private Shared _descrizioneAnnunciata As String = ""

    Private Shared _ultimoAvvisoMemoria As DateTime = DateTime.MinValue
    Private Shared _ultimoControlloDischi As DateTime = DateTime.MinValue
    Private Shared ReadOnly _ultimoAvvisoDisco As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)

    Private Sub New()
    End Sub

    ''' <summary>Dove mandare un avviso con la sua azione (il centro notifiche); se manca, l'avviso viene solo letto.</summary>
    Public Shared Property AvvisoNotifica As Action(Of String, Action)

    ''' <summary>Nome della rete Wi-Fi collegata, per dire dove è tornata la connessione.</summary>
    Public Shared Property NomeReteWifi As Func(Of String)

#Region "Impostazioni"

    Private Shared Function Leggi(chiave As String, predefinito As Integer) As Integer
        Try
            Dim valore As String = New WGConfig().LeggiValore(chiave)
            Dim numero As Integer
            If Integer.TryParse(valore, numero) Then Return numero
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.Leggi " & chiave & ": " & ex.Message)
        End Try
        Return predefinito
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As Integer)
        Try
            Dim config As New WGConfig()
            config.AggiungiValore(chiave, valore.ToString())
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.Scrivi " & chiave & ": " & ex.Message)
        End Try
    End Sub

    ''' <summary>Avvisare quando internet si perde o torna (predefinito: sì).</summary>
    Public Shared Property AvvisoConnessione As Boolean
        Get
            Return Leggi(ChiaveConnessione, 1) <> 0
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveConnessione, If(value, 1, 0))
        End Set
    End Property

    ''' <summary>Percentuale di memoria usata oltre cui avvisare; 0 = nessun avviso.</summary>
    Public Shared Property SogliaMemoria As Integer
        Get
            Return Leggi(ChiaveMemoria, MemoriaPredefinita)
        End Get
        Set(value As Integer)
            Scrivi(ChiaveMemoria, value)
        End Set
    End Property

    ''' <summary>Percentuale di spazio libero sotto cui avvisare; 0 = nessun avviso.</summary>
    Public Shared Property SogliaDisco As Integer
        Get
            Return Leggi(ChiaveDisco, DiscoPredefinito)
        End Get
        Set(value As Integer)
            Scrivi(ChiaveDisco, value)
        End Set
    End Property

    Public Shared Function DescriviConnessione() As String
        Return If(AvvisoConnessione, "sì", "no")
    End Function

    Public Shared Function DescriviMemoria() As String
        Dim soglia As Integer = SogliaMemoria
        Return If(soglia <= 0, "disattivato", "oltre il " & soglia & " per cento usato")
    End Function

    Public Shared Function DescriviDisco() As String
        Dim soglia As Integer = SogliaDisco
        Return If(soglia <= 0, "disattivato", "sotto il " & soglia & " per cento libero")
    End Function

    ''' <summary>Menu delle impostazioni degli avvisi (dal menu Sistema).</summary>
    Public Shared Sub MostraImpostazioni()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Connessione a internet persa e tornata: " & DescriviConnessione(), Sub() ScegliConnessione())
            menu.aggiungi("Memoria quasi piena: " & DescriviMemoria(), Sub() ScegliSoglia("Memoria", {80, 85, 90, 95}, SogliaMemoria, Function(v) "Oltre il " & v & " per cento usato", Sub(v) SogliaMemoria = v))
            menu.aggiungi("Disco quasi pieno: " & DescriviDisco(), Sub() ScegliSoglia("Disco", {5, 10, 15, 20}, SogliaDisco, Function(v) "Sotto il " & v & " per cento libero", Sub(v) SogliaDisco = v))
            menu.aggiungi("Stato attuale", Sub() MostraStato())
            menu.avvia("Avvisi", True)
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.MostraImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliConnessione()
        Dim menu As New WGMenu()
        menu.aggiungi("Sì, avvisa quando internet si perde o torna", Sub()
                                                                          AvvisoConnessione = True
                                                                          sintesivocale.GetInstance().LeggiTesto("Avviso di connessione attivo")
                                                                      End Sub)
        menu.aggiungi("No", Sub()
                                AvvisoConnessione = False
                                sintesivocale.GetInstance().LeggiTesto("Avviso di connessione disattivato")
                            End Sub)
        menu.posiziona(If(AvvisoConnessione, 0, 1))
        menu.avvia("Connessione", True)
    End Sub

    Private Shared Sub ScegliSoglia(titolo As String, valori As Integer(), attuale As Integer, descrivi As Func(Of Integer, String), salva As Action(Of Integer))
        Dim menu As New WGMenu()
        For Each valore As Integer In valori
            Dim v As Integer = valore
            menu.aggiungi(descrivi(v) & If(v = attuale, ", scelto", ""), Sub()
                                                                         salva(v)
                                                                         sintesivocale.GetInstance().LeggiTesto(titolo & ": " & descrivi(v).ToLowerInvariant())
                                                                     End Sub)
        Next
        menu.aggiungi("Nessun avviso" & If(attuale <= 0, ", scelto", ""), Sub()
                                                                          salva(0)
                                                                          sintesivocale.GetInstance().LeggiTesto(titolo & " disattivato")
                                                                      End Sub)
        Dim indice As Integer = Array.IndexOf(valori, attuale)
        menu.posiziona(If(indice >= 0, indice, valori.Length))
        menu.avvia(titolo, True)
    End Sub

    ' Lo stato di adesso in un messaggio: connessione, memoria e dischi.
    Private Shared Sub MostraStato()
        Try
            Dim righe As New List(Of String)()
            Dim tipo As String = ""
            righe.Add(If(ConnessoAInternet(tipo), "Internet: connesso" & If(tipo <> "", ", " & tipo, ""), "Internet: non connesso"))
            Dim usata As Integer = MemoriaUsata()
            If usata >= 0 Then righe.Add("Memoria usata: " & usata & " per cento")
            For Each disco As DriveInfo In DischiFissi()
                righe.Add("Disco " & disco.Name.TrimEnd("\"c) & ": liberi " & DescriviSpazio(disco.AvailableFreeSpace) & " su " & DescriviSpazio(disco.TotalSize) & ", " & PercentualeLibera(disco) & " per cento")
            Next
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(String.Join(vbCrLf, righe))
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.MostraStato: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Controlli"

    ''' <summary>Avvia i controlli (una volta sola, dal thread dell'interfaccia).</summary>
    Public Shared Sub Avvia()
        Try
            If _avviato Then Return
            _avviato = True
            _contesto = Threading.SynchronizationContext.Current
            Dim tipo As String = ""
            _connessoOsservato = ConnessoAInternet(tipo)
            _connessoAnnunciato = _connessoOsservato
            _descrizioneAnnunciata = tipo
            _osservatoDa = DateTime.Now
            AddHandler NetworkChange.NetworkAddressChanged, AddressOf CambioRete
            AddHandler NetworkChange.NetworkAvailabilityChanged, AddressOf CambioDisponibilita
            _timer = New Timer() With {.Interval = 5000}
            _timer.Start()
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.Avvia: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub CambioRete(sender As Object, e As EventArgs)
        SegnaCambio()
    End Sub

    Private Shared Sub CambioDisponibilita(sender As Object, e As NetworkAvailabilityEventArgs)
        SegnaCambio()
    End Sub

    ' Un cambio di rete fa ricominciare l'osservazione: si annuncia dopo che lo stato è stabile.
    Private Shared Sub SegnaCambio()
        _osservatoDa = DateTime.Now
    End Sub

    Private Shared Sub _timer_Tick(sender As Object, e As EventArgs) Handles _timer.Tick
        _timer.Stop()
        Try
            ControllaConnessione()
            ControllaMemoria()
            If (DateTime.Now - _ultimoControlloDischi).TotalMinutes >= 5 Then
                _ultimoControlloDischi = DateTime.Now
                ControllaDischi()
            End If
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.Tick: " & ex.Message)
        Finally
            _timer.Start()
        End Try
    End Sub

    Private Shared Sub ControllaConnessione()
        Dim tipo As String = ""
        Dim connesso As Boolean = ConnessoAInternet(tipo)
        If connesso <> _connessoOsservato Then
            _connessoOsservato = connesso
            _osservatoDa = DateTime.Now
            Return
        End If
        ' Stato stabile da almeno otto secondi.
        If (DateTime.Now - _osservatoDa).TotalSeconds < 8 Then Return
        If _connessoAnnunciato.HasValue AndAlso _connessoAnnunciato.Value = connesso AndAlso (Not connesso OrElse tipo = _descrizioneAnnunciata) Then Return
        Dim primaConnesso As Boolean? = _connessoAnnunciato
        _connessoAnnunciato = connesso
        _descrizioneAnnunciata = tipo
        If Not AvvisoConnessione Then Return
        Dim apriConnessione As Action = Sub()
                                            If WGScrivania.ApriConnessione IsNot Nothing Then WGScrivania.ApriConnessione.Invoke()
                                        End Sub
        If connesso Then
            If primaConnesso.GetValueOrDefault() Then
                Avvisa("Connessione a internet passata " & If(tipo <> "", "a " & tipo, "a un'altra rete"), apriConnessione)
            Else
                Avvisa("Connessione a internet tornata" & If(tipo <> "", ", " & tipo, ""), apriConnessione)
            End If
        Else
            Avvisa("Connessione a internet persa", apriConnessione)
        End If
    End Sub

    Private Shared Sub ControllaMemoria()
        Dim soglia As Integer = SogliaMemoria
        If soglia <= 0 OrElse (DateTime.Now - _ultimoAvvisoMemoria).TotalMinutes < 15 Then Return
        Dim usata As Integer = MemoriaUsata()
        If usata < soglia Then Return
        _ultimoAvvisoMemoria = DateTime.Now
        Dim aperte As Integer = 0
        Try
            If WGContenitore.Istanza IsNot Nothing AndAlso WGContenitore.ApplicazioniSeparate Then aperte = WGContenitore.Istanza.ApplicazioniAperte()
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.ControllaMemoria (applicazioni): " & ex.Message)
        End Try
        Avvisa("Memoria quasi piena, usata al " & usata & " per cento" & If(aperte >= 2, ", con " & aperte & " applicazioni aperte: F10 per chiudere quelle che non servono", ""),
               Sub()
                   If WGContenitore.Istanza IsNot Nothing Then WGContenitore.Istanza.MostraElencoApplicazioni()
               End Sub)
    End Sub

    Private Shared Sub ControllaDischi()
        Dim soglia As Integer = SogliaDisco
        If soglia <= 0 Then Return
        For Each disco As DriveInfo In DischiFissi()
            Try
                Dim libera As Integer = PercentualeLibera(disco)
                If libera >= soglia Then
                    _ultimoAvvisoDisco.Remove(disco.Name)
                    Continue For
                End If
                Dim ultimo As DateTime
                If _ultimoAvvisoDisco.TryGetValue(disco.Name, ultimo) AndAlso (DateTime.Now - ultimo).TotalMinutes < 60 Then Continue For
                _ultimoAvvisoDisco(disco.Name) = DateTime.Now
                Avvisa("Disco " & disco.Name.TrimEnd("\"c) & " quasi pieno: liberi " & DescriviSpazio(disco.AvailableFreeSpace) & ", il " & libera & " per cento. Svuota il cestino o cancella i file che non servono",
                       Sub() WGScrivania.ApriCestino())
            Catch ex As Exception
                log.ScriviLog("WGAvvisiSistema.ControllaDischi " & disco.Name & ": " & ex.Message)
            End Try
        Next
    End Sub

    Private Shared Sub Avvisa(testo As String, azione As Action)
        Try
            log.ScriviLog("WGAvvisiSistema: " & testo)
            Dim invia As Action = Sub()
                                      Try
                                          Dim notifica As Action(Of String, Action) = AvvisoNotifica
                                          If notifica IsNot Nothing Then
                                              notifica.Invoke(testo, azione)
                                          Else
                                              sintesivocale.GetInstance().LeggiTesto(testo)
                                          End If
                                      Catch ex As Exception
                                          log.ScriviLog("WGAvvisiSistema.Avvisa (invio): " & ex.Message)
                                      End Try
                                  End Sub
            If _contesto IsNot Nothing Then _contesto.Post(Sub(s) invia(), Nothing) Else invia()
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.Avvisa: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Misure"

    ' Connessi a internet se c'è un'interfaccia attiva (non di loopback né tunnel) con un gateway;
    ' tipo dice "Wi-Fi, rete Casa" o "cavo".
    Private Shared Function ConnessoAInternet(ByRef tipo As String) As Boolean
        tipo = ""
        Try
            If Not NetworkInterface.GetIsNetworkAvailable() Then Return False
            For Each scheda As NetworkInterface In NetworkInterface.GetAllNetworkInterfaces()
                If scheda.OperationalStatus <> OperationalStatus.Up Then Continue For
                If scheda.NetworkInterfaceType = NetworkInterfaceType.Loopback OrElse scheda.NetworkInterfaceType = NetworkInterfaceType.Tunnel Then Continue For
                Dim descrizione As String = (scheda.Description & " " & scheda.Name).ToLowerInvariant()
                If descrizione.Contains("virtual") OrElse descrizione.Contains("hyper-v") OrElse descrizione.Contains("vmware") OrElse descrizione.Contains("virtualbox") Then Continue For
                Dim proprieta As IPInterfaceProperties = scheda.GetIPProperties()
                If Not proprieta.GatewayAddresses.Any(Function(g) g.Address IsNot Nothing AndAlso Not g.Address.ToString().StartsWith("0.")) Then Continue For
                Select Case scheda.NetworkInterfaceType
                    Case NetworkInterfaceType.Wireless80211
                        Dim rete As String = ""
                        Try
                            If NomeReteWifi IsNot Nothing Then rete = NomeReteWifi.Invoke()
                        Catch ex As Exception
                            log.ScriviLog("WGAvvisiSistema.ConnessoAInternet (nome rete): " & ex.Message)
                        End Try
                        tipo = "Wi-Fi" & If(String.IsNullOrEmpty(rete), "", ", rete " & rete)
                    Case NetworkInterfaceType.Ethernet, NetworkInterfaceType.GigabitEthernet, NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx
                        tipo = If(descrizione.Contains("bluetooth"), "Bluetooth", If(descrizione.Contains("rndis") OrElse descrizione.Contains("remote ndis"), "telefono collegato con il cavo USB", "cavo di rete"))
                    Case NetworkInterfaceType.Wwanpp, NetworkInterfaceType.Wwanpp2
                        tipo = "rete cellulare"
                    Case Else
                        tipo = ""
                End Select
                Return True
            Next
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.ConnessoAInternet: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function MemoriaUsata() As Integer
        Try
            Dim info As New Microsoft.VisualBasic.Devices.ComputerInfo()
            If info.TotalPhysicalMemory = 0UL Then Return -1
            Return CInt(Math.Round(100.0 * (1.0 - CDbl(info.AvailablePhysicalMemory) / CDbl(info.TotalPhysicalMemory))))
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.MemoriaUsata: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Shared Function DischiFissi() As List(Of DriveInfo)
        Dim dischi As New List(Of DriveInfo)()
        Try
            For Each disco As DriveInfo In DriveInfo.GetDrives()
                Try
                    If disco.DriveType = DriveType.Fixed AndAlso disco.IsReady AndAlso disco.TotalSize > 0 Then dischi.Add(disco)
                Catch ex As Exception
                    log.ScriviLog("WGAvvisiSistema.DischiFissi " & disco.Name & ": " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGAvvisiSistema.DischiFissi: " & ex.Message)
        End Try
        Return dischi
    End Function

    Private Shared Function PercentualeLibera(disco As DriveInfo) As Integer
        Return CInt(Math.Floor(100.0 * CDbl(disco.AvailableFreeSpace) / CDbl(disco.TotalSize)))
    End Function

    Private Shared Function DescriviSpazio(byte_ As Long) As String
        Dim giga As Double = byte_ / 1073741824.0
        If giga >= 10 Then Return Math.Round(giga).ToString() & " gigabyte"
        If giga >= 1 Then Return giga.ToString("0.0") & " gigabyte"
        Return Math.Round(byte_ / 1048576.0).ToString() & " megabyte"
    End Function

#End Region

End Class
