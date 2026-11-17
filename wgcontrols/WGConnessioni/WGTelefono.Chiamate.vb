Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading.Tasks
Imports InTheHand.Net
Imports InTheHand.Net.Sockets
Imports Windows.Devices.Bluetooth
Imports Windows.Devices.Bluetooth.Rfcomm
Imports Windows.Devices.Enumeration
Imports Windows.Devices.Radios
Imports wgcontrols

' Gestione delle chiamate telefoniche recenti tramite connessione Bluetooth Handsfree e comandi AT.
Public Enum TipoChiamata
    Sconosciuta = 0
    Persa = 1
    Ricevuta = 2
    Effettuata = 3
End Enum

Public Class ChiamataRecente
    Public Property Nome As String = ""
    Public Property Numero As String = ""
    Public Property DataOra As DateTime? = Nothing
    Public Property Tipo As TipoChiamata = TipoChiamata.Sconosciuta
End Class

Public Class TelefonoCollegato
    Public Property Id As String = ""
    Public Property Nome As String = ""
End Class

' Estensione di WGTelefono (la classe originale va dichiarata "Partial").
Partial Public Class WGTelefono

    ' UUID del ruolo "Handsfree Audio Gateway": e' quello che espone il telefono.
    Private Shared ReadOnly ServizioHandsfreeAudioGateway As Guid = New Guid("0000111F-0000-1000-8000-00805F9B34FB")

    ''' <summary>True se la sessione AT/HFP con il telefono e' aperta e il thread di lettura e' attivo.</summary>
    Public ReadOnly Property SessioneChiamateAperta As Boolean
        Get
            Return inEsecuzione AndAlso streamHfp IsNot Nothing
        End Get
    End Property

    ' ---------------------------------------------------------------------------------------
    ' ATTESA
    ' ---------------------------------------------------------------------------------------
    ' AT+CHLD=2 mette in attesa la chiamata attiva; se non ce ne sono di attive riprende quella
    ' in attesa. Funziona solo se telefono e PC hanno dichiarato il supporto "3-way calling"
    ' (vedi BRSF nell'handshake).
    Public Function MettiInAttesa() As Boolean
        Return InviaComando("AT+CHLD=2", 5000)
    End Function

    Public Function RiprendiChiamata() As Boolean
        Return InviaComando("AT+CHLD=2", 5000)
    End Function

    ' ---------------------------------------------------------------------------------------
    ' RILEVAMENTO DEL TELEFONO
    ' ---------------------------------------------------------------------------------------
    ''' <summary>
    ''' Elenca i telefoni associati che espongono il profilo vivavoce (HFP): prima quelli gia'
    ''' collegati in Bluetooth, poi quelli non collegati ma raggiungibili nelle vicinanze.
    ''' Lista vuota = nessun telefono utilizzabile.
    ''' </summary>
    Public Async Function TrovaTelefoniCollegati() As Task(Of List(Of TelefonoCollegato))
        Dim collegati As New List(Of TelefonoCollegato)()
        Dim vicini As New List(Of TelefonoCollegato)()
        ' Se il Bluetooth e' spento o il computer non ce l'ha non c'e' niente da cercare:
        ' lista vuota, niente voci e niente errori nel log.
        Try
            Dim acceso As Boolean? = Await WGRadio.AccesaAsync(RadioKind.Bluetooth)
            If acceso <> True Then Return New List(Of TelefonoCollegato)()
        Catch
            Return New List(Of TelefonoCollegato)()
        End Try
        Try
            Dim selettore As String = BluetoothDevice.GetDeviceSelectorFromPairingState(True)
            Dim trovati As DeviceInformationCollection = Await DeviceInformation.FindAllAsync(selettore)
            For Each info As DeviceInformation In trovati
                Try
                    Using dispositivo As BluetoothDevice = Await BluetoothDevice.FromIdAsync(info.Id)
                        If dispositivo Is Nothing Then Continue For
                        If dispositivo.ClassOfDevice.MajorClass <> BluetoothMajorClass.Phone Then Continue For
                        Dim giaCollegato As Boolean = dispositivo.ConnectionStatus = BluetoothConnectionStatus.Connected
                        If giaCollegato Then
                            If Not Await SupportaHandsfree(dispositivo) Then Continue For
                        ElseIf Not Await RaggiungibileConHandsfree(dispositivo) Then
                            Continue For
                        End If
                        Dim t As New TelefonoCollegato()
                        t.Id = info.Id
                        t.Nome = If(String.IsNullOrWhiteSpace(dispositivo.Name), "Telefono", dispositivo.Name)
                        If giaCollegato Then collegati.Add(t) Else vicini.Add(t)
                    End Using
                Catch exSingolo As Exception
                    ' Il dispositivo non pronto (Bluetooth spento o appena tolto) non e' un errore,
                    ' non si scrive nel log: si salta il telefono e si continua in silenzio.
                    If exSingolo.Message.IndexOf("non è pronto", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
                       exSingolo.Message.IndexOf("800710DF", StringComparison.OrdinalIgnoreCase) < 0 Then
                        log.ScriviLog("WGTelefono.TrovaTelefoniCollegati singolo: " & exSingolo.Message)
                    End If
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGTelefono.TrovaTelefoniCollegati: " & ex.Message)
        End Try
        Dim risultato As New List(Of TelefonoCollegato)(collegati)
        risultato.AddRange(vicini)
        Return risultato
    End Function

    Private Async Function RaggiungibileConHandsfree(dispositivo As BluetoothDevice) As Task(Of Boolean)
        Try
            Dim r As RfcommDeviceServicesResult = Await dispositivo.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(ServizioHandsfreeAudioGateway), BluetoothCacheMode.Uncached)
            If r Is Nothing OrElse r.Error <> Windows.Devices.Bluetooth.BluetoothError.Success Then Return False
            Dim presente As Boolean = r.Services.Count > 0
            For Each s As RfcommDeviceService In r.Services
                Try
                    s.Dispose()
                Catch exChiusura As Exception
                    log.ScriviLog("WGTelefono.RaggiungibileConHandsfree chiusura: " & exChiusura.Message)
                End Try
            Next
            Return presente
        Catch ex As Exception
            log.ScriviLog("WGTelefono.RaggiungibileConHandsfree: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function SupportaHandsfree(dispositivo As BluetoothDevice) As Task(Of Boolean)
        Try
            Dim uuidDaCercare As Guid() = New Guid() {ServizioHandsfreeAudioGateway, ServizioHandsfree}
            For Each uuid As Guid In uuidDaCercare
                Dim r As RfcommDeviceServicesResult = Await dispositivo.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(uuid), BluetoothCacheMode.Uncached)
                ' Se Windows non riesce a interrogare il telefono non si puo' escludere: si accetta.
                If r.Error <> Windows.Devices.Bluetooth.BluetoothError.Success Then Return True
                Dim presente As Boolean = (r.Services.Count > 0)
                For Each s As RfcommDeviceService In r.Services
                    Try
                        s.Dispose()
                    Catch
                    End Try
                Next
                If presente Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGTelefono.SupportaHandsfree: " & ex.Message)
            Return False
        End Try
    End Function

    ' ---------------------------------------------------------------------------------------
    ' CRONOLOGIA CHIAMATE (PBAP)
    ' ---------------------------------------------------------------------------------------
    ''' <summary>
    ''' Legge dal telefono le chiamate recenti (tutte: ricevute, effettuate e perse) oppure solo
    ''' quelle perse. Restituisce Nothing se la lettura fallisce (connessione, permessi negati
    ''' sul telefono...) e una lista vuota se non ci sono chiamate.
    ''' </summary>
    Public Async Function ImportaCronologia(idDispositivo As String, soloPerse As Boolean, numeroMassimo As Integer) As Task(Of List(Of ChiamataRecente))
        Try
            Dim addr As BluetoothAddress = Await RisolviIndirizzo(idDispositivo)
            If addr Is Nothing Then Return Nothing
            Return Await Task.Run(Function() ImportaCronologiaSync(addr, soloPerse, numeroMassimo))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ImportaCronologia: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ImportaCronologiaSync(addr As BluetoothAddress, soloPerse As Boolean, numeroMassimo As Integer) As List(Of ChiamataRecente)
        Dim client As BluetoothClient = Nothing
        Dim stream As NetworkStream = Nothing
        Try
            client = New BluetoothClient()
            client.Connect(addr, ServizioPbap)
            stream = client.GetStream()
            Dim connId As Byte() = ObexConnetti(stream, TargetPbap)
            If connId Is Nothing Then Return Nothing
            ' cch = cronologia combinata, mch = solo chiamate perse
            Dim nomeFile As String = If(soloPerse, "telecom/mch.vcf", "telecom/cch.vcf")
            Dim corpo As Byte() = ObexGet(stream, connId, nomeFile, "x-bt/phonebook", CostruisciParametriCronologia(numeroMassimo))
            ObexDisconnetti(stream, connId)
            If corpo Is Nothing Then Return Nothing
            Return AnalizzaCronologia(Encoding.UTF8.GetString(corpo), soloPerse)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ImportaCronologiaSync: " & ex.Message)
            Return Nothing
        Finally
            ChiudiTranquillo(stream, client)
        End Try
    End Function

    ' Parametri applicativi PBAP: 0x04 = MaxListCount (2 byte), 0x07 = Format (1 = vCard 3.0).
    Private Function CostruisciParametriCronologia(numeroMassimo As Integer) As Byte()
        Dim m As Integer = Math.Max(1, Math.Min(numeroMassimo, 65535))
        Return New Byte() {&H4, &H2, CByte((m >> 8) And &HFF), CByte(m And &HFF), &H7, &H1, &H1}
    End Function

    Private Function AnalizzaCronologia(testo As String, soloPerse As Boolean) As List(Of ChiamataRecente)
        Dim lista As New List(Of ChiamataRecente)()
        Try
            Dim corrente As ChiamataRecente = Nothing
            For Each rigaRaw As String In testo.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
                Dim riga As String = rigaRaw.Trim()
                If riga.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) Then
                    corrente = New ChiamataRecente()
                    If soloPerse Then corrente.Tipo = TipoChiamata.Persa
                ElseIf riga.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase) Then
                    If corrente IsNot Nothing Then
                        If corrente.Numero.Length > 0 OrElse corrente.Nome.Length > 0 Then lista.Add(corrente)
                        corrente = Nothing
                    End If
                ElseIf corrente IsNot Nothing Then
                    Dim idx As Integer = riga.IndexOf(":"c)
                    If idx < 0 Then Continue For
                    Dim chiave As String = riga.Substring(0, idx).ToUpperInvariant()
                    Dim valore As String = riga.Substring(idx + 1).Trim()
                    If chiave = "FN" OrElse chiave.StartsWith("FN;", StringComparison.Ordinal) Then
                        corrente.Nome = valore
                    ElseIf (chiave = "N" OrElse chiave.StartsWith("N;", StringComparison.Ordinal)) AndAlso corrente.Nome.Length = 0 Then
                        corrente.Nome = String.Join(" ", valore.Split(";"c).Where(Function(p) p.Trim().Length > 0).Reverse()).Trim()
                    ElseIf chiave.StartsWith("TEL", StringComparison.Ordinal) Then
                        If corrente.Numero.Length = 0 Then corrente.Numero = valore
                    ElseIf chiave.StartsWith("X-IRMC-CALL-DATETIME", StringComparison.Ordinal) Then
                        If chiave.Contains("MISSED") Then
                            corrente.Tipo = TipoChiamata.Persa
                        ElseIf chiave.Contains("RECEIVED") Then
                            corrente.Tipo = TipoChiamata.Ricevuta
                        ElseIf chiave.Contains("DIALED") Then
                            corrente.Tipo = TipoChiamata.Effettuata
                        End If
                        Dim data As DateTime
                        If DateTime.TryParseExact(valore, New String() {"yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmmss'Z'"}, CultureInfo.InvariantCulture, DateTimeStyles.None, data) Then
                            corrente.DataOra = data
                        End If
                    End If
                End If
            Next
            ' Dalla piu' recente; OrderBy e' stabile, quindi senza date resta l'ordine del telefono.
            lista = lista.OrderByDescending(Function(c) If(c.DataOra.HasValue, c.DataOra.Value, DateTime.MinValue)).ToList()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.AnalizzaCronologia: " & ex.Message)
        End Try
        Return lista
    End Function

End Class