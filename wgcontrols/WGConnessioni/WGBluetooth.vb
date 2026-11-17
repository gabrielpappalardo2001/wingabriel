' ============================================================================================
' WGBluetooth - dispositivi Bluetooth con le API di Windows (Windows.Devices.Bluetooth e Enumeration)
' ============================================================================================
'
'   - dispositivi associati, con lo stato di connessione (classici come cuffie e tastiere, e Low Energy);
'   - ricerca dei dispositivi vicini non associati, per circa dieci secondi;
'   - associazione: se il dispositivo chiede una conferma la si dà, se mostra un PIN lo si legge,
'     se vuole che si scriva un PIN lo chiede ChiediPin (lo imposta l'interfaccia);
'   - dissociazione.
' Accendere e spegnere il Bluetooth è in WGRadio. Collegare e scollegare al volo cuffie e casse già
' associate è in WGCuffieBluetooth (wgcontrols).
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Windows.Devices.Bluetooth
Imports Windows.Devices.Bluetooth.GenericAttributeProfile
Imports Windows.Devices.Enumeration
Imports Windows.Devices.Radios
Imports wgcontrols

Public Class DispositivoBluetooth
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property Associato As Boolean
    Public Property Connesso As Boolean
    Public Property BassoConsumo As Boolean

    Public ReadOnly Property Descrizione As String
        Get
            Dim nomeMostrato As String = If(String.IsNullOrWhiteSpace(Nome), "Dispositivo senza nome", Nome)
            If Not Associato Then Return nomeMostrato
            Return nomeMostrato & If(Connesso, ", connesso", ", non connesso")
        End Get
    End Property
End Class

Public NotInheritable Class WGBluetooth

    Private Shared ReadOnly log As New Logger()

    ''' <summary>Chiede all'utente il PIN da scrivere per l'associazione; Nothing o vuoto per annullare.</summary>
    Public Shared Property ChiediPin As Func(Of String, Task(Of String))

    Private Class ConnessioneMantenuta
        Public Dispositivo As BluetoothLEDevice
        Public Sessione As GattSession
    End Class

    Private Shared ReadOnly connessioniMantenute As New Dictionary(Of String, ConnessioneMantenuta)()

    Private Sub New()
    End Sub

    ''' <summary>I dispositivi associati a questo computer, con lo stato di connessione.</summary>
    Public Shared Async Function AssociatiAsync() As Task(Of List(Of DispositivoBluetooth))
        Dim risultato As New List(Of DispositivoBluetooth)()
        Try
            For Each info As DeviceInformation In Await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(True))
                Dim d As New DispositivoBluetooth With {.Id = info.Id, .Nome = info.Name, .Associato = True}
                Try
                    Using dispositivo As BluetoothDevice = Await BluetoothDevice.FromIdAsync(info.Id)
                        If dispositivo IsNot Nothing Then d.Connesso = dispositivo.ConnectionStatus = BluetoothConnectionStatus.Connected
                    End Using
                Catch exStato As Exception
                    log.ScriviLog("WGBluetooth.AssociatiAsync stato " & info.Name & ": " & exStato.Message)
                End Try
                risultato.Add(d)
            Next
            For Each info As DeviceInformation In Await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(True))
                If risultato.Any(Function(x) x.Nome = info.Name) Then Continue For
                Dim d As New DispositivoBluetooth With {.Id = info.Id, .Nome = info.Name, .Associato = True, .BassoConsumo = True}
                Try
                    Using dispositivo As BluetoothLEDevice = Await BluetoothLEDevice.FromIdAsync(info.Id)
                        If dispositivo IsNot Nothing Then d.Connesso = dispositivo.ConnectionStatus = BluetoothConnectionStatus.Connected
                    End Using
                Catch exStato As Exception
                    log.ScriviLog("WGBluetooth.AssociatiAsync stato LE " & info.Name & ": " & exStato.Message)
                End Try
                risultato.Add(d)
            Next
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.AssociatiAsync: " & ex.Message)
        End Try
        Return risultato.OrderByDescending(Function(d) d.Connesso).ThenBy(Function(d) d.Nome).ToList()
    End Function

    ''' <summary>Cerca i dispositivi vicini non associati per il tempo indicato; solo quelli con un nome.</summary>
    Public Shared Async Function CercaVicinAsync(Optional secondi As Integer = 10) As Task(Of List(Of DispositivoBluetooth))
        Dim trovati As New Dictionary(Of String, DispositivoBluetooth)()
        Dim osservatori As New List(Of DeviceWatcher)()
        Try
            Dim selettori As String() = {BluetoothDevice.GetDeviceSelectorFromPairingState(False), BluetoothLEDevice.GetDeviceSelectorFromPairingState(False)}
            For indice As Integer = 0 To selettori.Length - 1
                Dim selettore As String = selettori(indice)
                ' Il secondo selettore è quello dei dispositivi a basso consumo.
                Dim bassoConsumo As Boolean = indice = 1
                Dim osservatore As DeviceWatcher = DeviceInformation.CreateWatcher(selettore)
                AddHandler osservatore.Added, Sub(s, info)
                                                  If String.IsNullOrWhiteSpace(info.Name) Then Return
                                                  SyncLock trovati
                                                      If Not trovati.ContainsKey(info.Id) AndAlso Not trovati.Values.Any(Function(x) x.Nome = info.Name) Then
                                                          trovati(info.Id) = New DispositivoBluetooth With {.Id = info.Id, .Nome = info.Name, .BassoConsumo = bassoConsumo}
                                                      End If
                                                  End SyncLock
                                              End Sub
                AddHandler osservatore.Updated, Sub(s, aggiornamento)
                                                End Sub
                AddHandler osservatore.Removed, Sub(s, rimozione)
                                                    SyncLock trovati
                                                        trovati.Remove(rimozione.Id)
                                                    End SyncLock
                                                End Sub
                osservatori.Add(osservatore)
                osservatore.Start()
            Next
            Await Task.Delay(TimeSpan.FromSeconds(secondi))
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.CercaVicinAsync: " & ex.Message)
        Finally
            For Each osservatore As DeviceWatcher In osservatori
                Try
                    If osservatore.Status = DeviceWatcherStatus.Started OrElse osservatore.Status = DeviceWatcherStatus.EnumerationCompleted Then osservatore.Stop()
                Catch
                End Try
            Next
        End Try
        SyncLock trovati
            ' Prima i dispositivi classici (cuffie, tastiere, telefoni), poi quelli a basso consumo.
            Return trovati.Values.OrderBy(Function(d) d.BassoConsumo).ThenBy(Function(d) d.Nome).ToList()
        End SyncLock
    End Function

    ''' <summary>Associa il dispositivo; restituisce "" se riuscito, altrimenti il motivo.</summary>
    Public Shared Async Function AssociaAsync(id As String) As Task(Of String)
        Try
            Dim info As DeviceInformation = Await DeviceInformation.CreateFromIdAsync(id)
            If info Is Nothing Then Return "Dispositivo non trovato"
            If info.Pairing.IsPaired Then Return ""
            Dim personalizzata As DeviceInformationCustomPairing = info.Pairing.Custom
            AddHandler personalizzata.PairingRequested, AddressOf RichiestaAssociazione
            Try
                Dim tipi As DevicePairingKinds = DevicePairingKinds.ConfirmOnly Or DevicePairingKinds.DisplayPin Or DevicePairingKinds.ConfirmPinMatch Or DevicePairingKinds.ProvidePin
                Dim esito As DevicePairingResult = Await personalizzata.PairAsync(tipi)
                Select Case esito.Status
                    Case DevicePairingResultStatus.Paired, DevicePairingResultStatus.AlreadyPaired
                        Return ""
                    Case DevicePairingResultStatus.AuthenticationFailure, DevicePairingResultStatus.AuthenticationTimeout
                        Return "Associazione non riuscita: il PIN o la conferma non sono stati accettati"
                    Case DevicePairingResultStatus.NotReadyToPair, DevicePairingResultStatus.ConnectionRejected
                        Return "Il dispositivo non è pronto: mettilo in modalità di associazione e riprova"
                    Case DevicePairingResultStatus.PairingCanceled
                        Return "Associazione annullata"
                    Case Else
                        Return "Associazione non riuscita: " & esito.Status.ToString()
                End Select
            Finally
                RemoveHandler personalizzata.PairingRequested, AddressOf RichiestaAssociazione
            End Try
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.AssociaAsync: " & ex.Message)
            Return "Errore durante l'associazione: " & ex.Message
        End Try
    End Function

    ' Quello che chiede il dispositivo durante l'associazione.
    Private Shared Async Sub RichiestaAssociazione(sender As DeviceInformationCustomPairing, args As DevicePairingRequestedEventArgs)
        Dim rinvio As Global.Windows.Foundation.Deferral = args.GetDeferral()
        Try
            Select Case args.PairingKind
                Case DevicePairingKinds.ConfirmOnly
                    args.Accept()
                Case DevicePairingKinds.DisplayPin
                    sintesivocale.GetInstance().LeggiTesto("Scrivi sul dispositivo il PIN " & String.Join(" ", args.Pin.ToCharArray()) & " e premi Invio")
                    args.Accept()
                Case DevicePairingKinds.ConfirmPinMatch
                    sintesivocale.GetInstance().LeggiTesto("Il dispositivo mostra il codice " & String.Join(" ", args.Pin.ToCharArray()) & ": confermalo anche sul dispositivo")
                    args.Accept()
                Case DevicePairingKinds.ProvidePin
                    Dim pin As String = ""
                    If ChiediPin IsNot Nothing Then pin = Await ChiediPin.Invoke("Scrivi il PIN del dispositivo Bluetooth, di solito 0000 o 1234 se non lo sai")
                    If Not String.IsNullOrWhiteSpace(pin) Then args.Accept(pin.Trim())
            End Select
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.RichiestaAssociazione: " & ex.Message)
        Finally
            rinvio.Complete()
        End Try
    End Sub

    Public Shared Async Function ConnettiAsync(dispositivo As DispositivoBluetooth, Optional secondi As Integer = 15) As Task(Of String)
        Try
            If dispositivo Is Nothing OrElse String.IsNullOrEmpty(dispositivo.Id) Then Return "Dispositivo non trovato"
            If dispositivo.BassoConsumo Then Return Await ConnettiBassoConsumoAsync(dispositivo, secondi)
            Return Await ConnettiClassicoAsync(dispositivo, secondi)
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.ConnettiAsync: " & ex.Message)
            Return "Errore durante il collegamento: " & ex.Message
        End Try
    End Function

    Private Shared Async Function ConnettiClassicoAsync(dispositivo As DispositivoBluetooth, secondi As Integer) As Task(Of String)
        Try
            Using d As BluetoothDevice = Await BluetoothDevice.FromIdAsync(dispositivo.Id)
                If d Is Nothing Then Return "Dispositivo non trovato"
                Dim limite As DateTime = DateTime.Now.AddSeconds(secondi)
                Dim erroreRegistrato As Boolean = False
                Do
                    If d.ConnectionStatus = BluetoothConnectionStatus.Connected Then Return ""
                    If DateTime.Now >= limite Then Exit Do
                    Try
                        Await d.GetRfcommServicesAsync(BluetoothCacheMode.Uncached)
                    Catch exServizi As Exception
                        If Not erroreRegistrato Then
                            log.ScriviLog("WGBluetooth.ConnettiClassicoAsync servizi " & dispositivo.Nome & ": " & exServizi.Message)
                            erroreRegistrato = True
                        End If
                    End Try
                    If d.ConnectionStatus = BluetoothConnectionStatus.Connected Then Return ""
                    Await Task.Delay(1000)
                Loop
            End Using
            Return MessaggioNonCollegato(dispositivo)
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.ConnettiClassicoAsync: " & ex.Message)
            Return "Errore durante il collegamento: " & ex.Message
        End Try
    End Function

    Private Shared Async Function ConnettiBassoConsumoAsync(dispositivo As DispositivoBluetooth, secondi As Integer) As Task(Of String)
        Dim d As BluetoothLEDevice = Nothing
        Dim sessione As GattSession = Nothing
        Try
            RilasciaConnessione(dispositivo.Id)
            d = Await BluetoothLEDevice.FromIdAsync(dispositivo.Id)
            If d Is Nothing Then Return "Dispositivo non trovato"
            Try
                sessione = Await GattSession.FromDeviceIdAsync(d.BluetoothDeviceId)
                If sessione IsNot Nothing Then sessione.MaintainConnection = True
            Catch exSessione As Exception
                log.ScriviLog("WGBluetooth.ConnettiBassoConsumoAsync sessione " & dispositivo.Nome & ": " & exSessione.Message)
            End Try
            Dim limite As DateTime = DateTime.Now.AddSeconds(secondi)
            Dim erroreRegistrato As Boolean = False
            Do
                If d.ConnectionStatus = BluetoothConnectionStatus.Connected Then
                    SyncLock connessioniMantenute
                        connessioniMantenute(dispositivo.Id) = New ConnessioneMantenuta With {.Dispositivo = d, .Sessione = sessione}
                    End SyncLock
                    d = Nothing
                    sessione = Nothing
                    Return ""
                End If
                If DateTime.Now >= limite Then Exit Do
                Try
                    Await d.GetGattServicesAsync(BluetoothCacheMode.Uncached)
                Catch exServizi As Exception
                    If Not erroreRegistrato Then
                        log.ScriviLog("WGBluetooth.ConnettiBassoConsumoAsync servizi " & dispositivo.Nome & ": " & exServizi.Message)
                        erroreRegistrato = True
                    End If
                End Try
                Await Task.Delay(1000)
            Loop
            Return MessaggioNonCollegato(dispositivo)
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.ConnettiBassoConsumoAsync: " & ex.Message)
            Return "Errore durante il collegamento: " & ex.Message
        Finally
            Try
                If sessione IsNot Nothing Then sessione.Dispose()
                If d IsNot Nothing Then d.Dispose()
            Catch exChiusura As Exception
                log.ScriviLog("WGBluetooth.ConnettiBassoConsumoAsync chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Shared Function MessaggioNonCollegato(dispositivo As DispositivoBluetooth) As String
        Try
            Dim nome As String = If(String.IsNullOrWhiteSpace(dispositivo.Nome), "Il dispositivo", dispositivo.Nome)
            Return nome & " non si è collegato: controlla che sia acceso, vicino al computer e non collegato a un altro apparecchio"
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.MessaggioNonCollegato: " & ex.Message)
            Return "Il dispositivo non si è collegato"
        End Try
    End Function

    Public Shared Sub RilasciaConnessione(id As String)
        Try
            If String.IsNullOrEmpty(id) Then Return
            Dim mantenuta As ConnessioneMantenuta = Nothing
            SyncLock connessioniMantenute
                If connessioniMantenute.TryGetValue(id, mantenuta) Then connessioniMantenute.Remove(id)
            End SyncLock
            If mantenuta Is Nothing Then Return
            If mantenuta.Sessione IsNot Nothing Then mantenuta.Sessione.Dispose()
            If mantenuta.Dispositivo IsNot Nothing Then mantenuta.Dispositivo.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.RilasciaConnessione: " & ex.Message)
        End Try
    End Sub

    ' ============================================================================================
    ' RICERCA IN SFONDO DEI TELEFONI
    ' ============================================================================================
    ' Un controllo periodico, in background, elenca i telefoni associati al computer. Quando un
    ' telefono compare (o cambia stato di connessione) viene annunciato con l'evento
    ' TelefonoTrovatoInSfondo, cosi' il telefono si puo' collegare senza aspettare l'utente.
    ' Se il Bluetooth e' spento, o nessun telefono e' raggiungibile, non succede niente: niente
    ' voci, niente messaggi e soprattutto niente errori nel log. Il giro dopo si riprova da solo.

    ''' <summary>Un telefono Bluetooth associato e' stato trovato o e' cambiato il suo stato di connessione.</summary>
    Public Shared Event TelefonoTrovatoInSfondo(id As String, nome As String, connesso As Boolean)

    Private Shared ricercaTelefoniAvviata As Integer
    Private Shared ReadOnly telefoniInSfondo As New Dictionary(Of String, Boolean)()

    ''' <summary>Avvia la ricerca in background dei telefoni; silenziosa, senza errori nel log.</summary>
    Public Shared Sub AvviaRicercaTelefoniInBackground()
        If Interlocked.CompareExchange(ricercaTelefoniAvviata, 1, 0) <> 0 Then Return
        Task.Run(New Func(Of Task)(AddressOf CicloRicercaTelefoniInSfondo))
    End Sub

    Private Shared Async Function CicloRicercaTelefoniInSfondo() As Task
        While True
            Try
                Await GiroRicercaTelefoniInSfondo()
            Catch
                ' La ricerca in sfondo non scrive mai nel log: se il Bluetooth manca o e' spento
                ' non e' un errore, e non c'e' niente da dire.
            End Try
            Await Task.Delay(TimeSpan.FromSeconds(20))
        End While
    End Function

    Private Shared Async Function GiroRicercaTelefoniInSfondo() As Task
        Dim acceso As Boolean? = Await WGRadio.AccesaAsync(RadioKind.Bluetooth)
        If acceso <> True Then Return
        Dim trovati As DeviceInformationCollection = Await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(True))
        Dim visti As New HashSet(Of String)()
        For Each info As DeviceInformation In trovati
            Dim connesso As Boolean = False
            Try
                Using dispositivo As BluetoothDevice = Await BluetoothDevice.FromIdAsync(info.Id)
                    If dispositivo Is Nothing Then Continue For
                    If dispositivo.ClassOfDevice.MajorClass <> BluetoothMajorClass.Phone Then Continue For
                    connesso = dispositivo.ConnectionStatus = BluetoothConnectionStatus.Connected
                End Using
            Catch
                ' Il dispositivo non risponde (spento o non pronto): la ricerca resta in silenzio.
                Continue For
            End Try
            visti.Add(info.Id)
            Dim noto As Boolean = False
            Dim statoPrecedente As Boolean = False
            SyncLock telefoniInSfondo
                noto = telefoniInSfondo.TryGetValue(info.Id, statoPrecedente)
                telefoniInSfondo(info.Id) = connesso
            End SyncLock
            If Not noto OrElse statoPrecedente <> connesso Then
                RaiseEvent TelefonoTrovatoInSfondo(info.Id, If(String.IsNullOrWhiteSpace(info.Name), "Telefono", info.Name), connesso)
            End If
        Next
        ' I telefoni che non ci sono piu' si tolgono dalla memoria.
        SyncLock telefoniInSfondo
            For Each chiave As String In telefoniInSfondo.Keys.ToList()
                If Not visti.Contains(chiave) Then telefoniInSfondo.Remove(chiave)
            Next
        End SyncLock
    End Function

    Public Shared Async Function DissociaAsync(id As String) As Task(Of String)
        Try
            RilasciaConnessione(id)
            Dim info As DeviceInformation = Await DeviceInformation.CreateFromIdAsync(id)
            If info Is Nothing Then Return "Dispositivo non trovato"
            Dim esito As DeviceUnpairingResult = Await info.Pairing.UnpairAsync()
            If esito.Status = DeviceUnpairingResultStatus.Unpaired OrElse esito.Status = DeviceUnpairingResultStatus.AlreadyUnpaired Then Return ""
            Return "Dissociazione non riuscita: " & esito.Status.ToString()
        Catch ex As Exception
            log.ScriviLog("WGBluetooth.DissociaAsync: " & ex.Message)
            Return "Errore durante la dissociazione: " & ex.Message
        End Try
    End Function

End Class