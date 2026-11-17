Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports InTheHand.Net
Imports InTheHand.Net.Sockets
Imports Windows.Devices.Bluetooth
Imports wgcontrols
Imports System.IO

Public Class Contatto
    Public Property Nome As String = ""
    Public Property Numeri As New List(Of String)()
End Class

Public Class Messaggio
    Public Property Handle As String = ""
    Public Property Mittente As String = ""
    Public Property DataOra As String = ""
    Public Property Testo As String = ""
    Public Property Letto As Boolean = False
End Class

Partial Public Class WGTelefono
    Implements IDisposable

    Public Event ChiamataInArrivo(numero As String)
    Public Event StatoChiamataCambiato(stato As String)
    Public Event TelefonoDisconnesso()
    Public Event IdentificativoChiamante(numero As String, nome As String)

    Private ReadOnly log As New Logger()

    Private Shared ReadOnly ServizioHandsfree As Guid = New Guid("0000111E-0000-1000-8000-00805F9B34FB")
    Private Shared ReadOnly ServizioPbap As Guid = New Guid("0000112F-0000-1000-8000-00805F9B34FB")
    Private Shared ReadOnly ServizioMap As Guid = New Guid("00001132-0000-1000-8000-00805F9B34FB")

    Private Shared ReadOnly TargetPbap As Byte() = New Byte() {&H79, &H61, &H35, &HF0, &HF0, &HC5, &H11, &HD8, &H9, &H66, &H8, &H0, &H20, &H0, &HC9, &HA6}
    Private Shared ReadOnly TargetMap As Byte() = New Byte() {&HBB, &H58, &H2B, &H40, &H42, &HC, &H11, &HDB, &HB0, &HDE, &H8, &H0, &H20, &H0, &HC9, &HA6}

    Private Const TentativiConnessione As Integer = 4
    Private Const AttesaTraTentativiMs As Integer = 1500

    Private Shared istanzaAttiva As WGTelefono
    Private Shared ReadOnly bloccoConnessione As New Object()

    Private clientHfp As BluetoothClient
    Private streamHfp As NetworkStream
    Private lettore As Thread
    Private inEsecuzione As Boolean = False
    Private eliminato As Boolean = False
    Private ultimoIndirizzo As BluetoothAddress
    Private ReadOnly segnaleEsito As New ManualResetEventSlim(False)
    Private ultimoEsito As String = ""
    Private ReadOnly bloccoScrittura As New Object()
    Private ReadOnly bloccoRisorse As New Object()

    Public ReadOnly Property Connesso As Boolean
        Get
            Try
                Return Volatile.Read(inEsecuzione) AndAlso streamHfp IsNot Nothing
            Catch ex As Exception
                log.ScriviLog("WGTelefono.Connesso: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Async Function ApriChiamate(idDispositivo As String) As Task(Of Boolean)
        Try
            Dim addr As BluetoothAddress = Await RisolviIndirizzo(idDispositivo)
            If addr Is Nothing Then Return False
            ultimoIndirizzo = addr
            Return Await Task.Run(Function() ApriConnessioneSync(addr))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ApriChiamate: " & ex.Message)
            ChiudiChiamate()
            Return False
        End Try
    End Function

    Public Sub ChiudiChiamate()
        Try
            Volatile.Write(inEsecuzione, False)
            Dim lettorePrecedente As Thread = lettore
            lettore = Nothing
            ChiudiRisorse()
            Try
                If lettorePrecedente IsNot Nothing AndAlso lettorePrecedente IsNot Thread.CurrentThread AndAlso lettorePrecedente.IsAlive Then lettorePrecedente.Join(2000)
            Catch exJoin As Exception
                log.ScriviLog("WGTelefono.ChiudiChiamate attesa lettore: " & exJoin.Message)
            End Try
            Interlocked.CompareExchange(Of WGTelefono)(istanzaAttiva, Nothing, Me)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ChiudiChiamate: " & ex.Message)
        End Try
    End Sub

    Public Function Chiama(numero As String) As Boolean
        Try
            Dim pulito As String = PulisciNumero(numero)
            If String.IsNullOrEmpty(pulito) Then Return False
            Return InviaComando("ATD" & pulito & ";", 8000)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.Chiama: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Riaggancia() As Boolean
        Return InviaComando("AT+CHUP", 5000)
    End Function

    Public Function Rispondi() As Boolean
        Return InviaComando("ATA", 5000)
    End Function

    Public Function RichiamaUltimo() As Boolean
        Return InviaComando("AT+BLDN", 8000)
    End Function

    Public Function InviaDtmf(carattere As Char) As Boolean
        Return InviaComando("AT+VTS=" & carattere, 4000)
    End Function

    Public Async Function ImportaRubrica(idDispositivo As String) As Task(Of List(Of Contatto))
        Try
            Dim addr As BluetoothAddress = Await RisolviIndirizzo(idDispositivo)
            If addr Is Nothing Then Return New List(Of Contatto)()
            Return Await Task.Run(Function() ImportaRubricaSync(addr))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ImportaRubrica: " & ex.Message)
            Return New List(Of Contatto)()
        End Try
    End Function

    Public Async Function LeggiMessaggi(idDispositivo As String, numeroMassimo As Integer) As Task(Of List(Of Messaggio))
        Try
            Dim addr As BluetoothAddress = Await RisolviIndirizzo(idDispositivo)
            If addr Is Nothing Then Return New List(Of Messaggio)()
            Return Await Task.Run(Function() LeggiMessaggiSync(addr, numeroMassimo))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.LeggiMessaggi: " & ex.Message)
            Return New List(Of Messaggio)()
        End Try
    End Function

    Public Async Function InviaSms(idDispositivo As String, numeroDestinatario As String, testo As String) As Task(Of Boolean)
        Try
            Dim addr As BluetoothAddress = Await RisolviIndirizzo(idDispositivo)
            If addr Is Nothing Then Return False
            Return Await Task.Run(Function() InviaSmsSync(addr, PulisciNumero(numeroDestinatario), testo))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.InviaSms: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function RisolviIndirizzo(idDispositivo As String) As Task(Of BluetoothAddress)
        Try
            If String.IsNullOrWhiteSpace(idDispositivo) Then Return Nothing
            Using dispositivo As BluetoothDevice = Await BluetoothDevice.FromIdAsync(idDispositivo)
                If dispositivo Is Nothing Then Return Nothing
                Dim numerico As ULong = dispositivo.BluetoothAddress
                Return BluetoothAddress.Parse(numerico.ToString("X12", CultureInfo.InvariantCulture))
            End Using
        Catch ex As Exception
            log.ScriviLog("WGTelefono.RisolviIndirizzo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ApriConnessioneSync(addr As BluetoothAddress) As Boolean
        Try
            SyncLock bloccoConnessione
                If eliminato Then Return False
                ChiudiChiamate()
                Dim precedente As WGTelefono = Interlocked.Exchange(Of WGTelefono)(istanzaAttiva, Nothing)
                If precedente IsNot Nothing AndAlso precedente IsNot Me Then precedente.ChiudiChiamate()
                If Not ConnettiHfpSync(addr) Then
                    ChiudiRisorse()
                    Return False
                End If
                Volatile.Write(inEsecuzione, True)
                Interlocked.Exchange(Of WGTelefono)(istanzaAttiva, Me)
                Dim nuovoLettore As New Thread(AddressOf CicloLettura)
                nuovoLettore.IsBackground = True
                lettore = nuovoLettore
                nuovoLettore.Start()
                Return True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ApriConnessioneSync: " & ex.Message)
            Volatile.Write(inEsecuzione, False)
            ChiudiRisorse()
            Return False
        End Try
    End Function

    Private Function AssicuraConnessione() As Boolean
        Try
            If Connesso Then Return True
            Dim addr As BluetoothAddress = ultimoIndirizzo
            If addr Is Nothing OrElse eliminato Then Return False
            Return ApriConnessioneSync(addr)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.AssicuraConnessione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ConnettiHfpSync(addr As BluetoothAddress) As Boolean
        Dim tentativo As Integer = 0
        While tentativo < TentativiConnessione
            tentativo += 1
            Dim client As BluetoothClient = Nothing
            Dim riprova As Boolean = False
            Try
                client = New BluetoothClient()
                client.Connect(addr, ServizioHandsfree)
                Dim stream As NetworkStream = client.GetStream()
                SyncLock bloccoRisorse
                    clientHfp = client
                    streamHfp = stream
                End SyncLock
                client = Nothing
                Return EseguiHandshakeHfp()
            Catch ex As Exception
                ChiudiTranquillo(Nothing, client)
                ChiudiRisorse()
                If IndirizzoOccupato(ex) AndAlso tentativo < TentativiConnessione Then
                    riprova = True
                ElseIf IndirizzoOccupato(ex) Then
                    log.ScriviLog("WGTelefono.ConnettiHfpSync: canale vivavoce del telefono " & addr.ToString() & " occupato da un'altra connessione dopo " & tentativo.ToString(CultureInfo.InvariantCulture) & " tentativi (connessione precedente non chiusa o servizio Telefonia vivavoce di Windows attivo): " & ex.Message)
                    Return False
                Else
                    log.ScriviLog("WGTelefono.ConnettiHfpSync: " & ex.Message)
                    Return False
                End If
            End Try
            If riprova Then
                Try
                    Thread.Sleep(AttesaTraTentativiMs)
                Catch exAttesa As Exception
                    log.ScriviLog("WGTelefono.ConnettiHfpSync attesa: " & exAttesa.Message)
                    Return False
                End Try
            End If
        End While
        Return False
    End Function

    Private Function IndirizzoOccupato(ex As Exception) As Boolean
        Try
            Dim corrente As Exception = ex
            While corrente IsNot Nothing
                Dim errSocket As SocketException = TryCast(corrente, SocketException)
                If errSocket IsNot Nothing Then
                    If errSocket.SocketErrorCode = SocketError.AddressAlreadyInUse OrElse errSocket.ErrorCode = 10048 Then Return True
                End If
                corrente = corrente.InnerException
            End While
            Return False
        Catch exInterna As Exception
            log.ScriviLog("WGTelefono.IndirizzoOccupato: " & exInterna.Message)
            Return False
        End Try
    End Function

    Private Function ScadenzaLettura(ex As Exception) As Boolean
        Try
            Dim corrente As Exception = ex
            While corrente IsNot Nothing
                Dim errSocket As SocketException = TryCast(corrente, SocketException)
                If errSocket IsNot Nothing Then
                    Return errSocket.SocketErrorCode = SocketError.TimedOut OrElse errSocket.SocketErrorCode = SocketError.WouldBlock
                End If
                corrente = corrente.InnerException
            End While
            Dim client As BluetoothClient = clientHfp
            Return client IsNot Nothing AndAlso client.Connected
        Catch exInterna As Exception
            log.ScriviLog("WGTelefono.ScadenzaLettura: " & exInterna.Message)
            Return False
        End Try
    End Function

    Private Function EseguiHandshakeHfp() As Boolean
        Try
            ScriviRiga("AT+BRSF=22")
            If Not AttendiRispostaSincrona(3000) Then Return False
            ScriviRiga("AT+CIND=?")
            If Not AttendiRispostaSincrona(3000) Then Return False
            ScriviRiga("AT+CIND?")
            If Not AttendiRispostaSincrona(3000) Then Return False
            ScriviRiga("AT+CMER=3,0,0,1")
            If Not AttendiRispostaSincrona(3000) Then Return False
            ScriviRiga("AT+CHLD=?")
            AttendiRispostaSincrona(2000)
            ScriviRiga("AT+CLIP=1")
            AttendiRispostaSincrona(2000)
            ScriviRiga("AT+XAPL=WinGabriel-0001,6")
            AttendiRispostaSincrona(2000)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EseguiHandshakeHfp: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function AttendiRispostaSincrona(timeoutMs As Integer) As Boolean
        Try
            Dim stream As NetworkStream = streamHfp
            If stream Is Nothing Then Return False
            Dim sb As New StringBuilder()
            Dim buffer(1023) As Byte
            Dim scadenza As DateTime = DateTime.UtcNow.AddMilliseconds(timeoutMs)
            stream.ReadTimeout = timeoutMs
            While DateTime.UtcNow < scadenza
                Dim letti As Integer = 0
                Try
                    letti = stream.Read(buffer, 0, buffer.Length)
                Catch exIo As IOException
                    Return False
                End Try
                If letti <= 0 Then Return False
                sb.Append(Encoding.ASCII.GetString(buffer, 0, letti))
                Dim contenuto As String = sb.ToString()
                If contenuto.IndexOf("OK", StringComparison.Ordinal) >= 0 Then Return True
                If contenuto.IndexOf("ERROR", StringComparison.Ordinal) >= 0 Then Return False
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog("WGTelefono.AttendiRispostaSincrona: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function InviaComando(comando As String, timeoutMs As Integer) As Boolean
        Try
            For tentativo As Integer = 1 To 2
                If Not AssicuraConnessione() Then Return False
                segnaleEsito.Reset()
                ultimoEsito = ""
                Dim scritto As Boolean = False
                Try
                    ScriviRiga(comando)
                    scritto = True
                Catch exScrittura As Exception
                    If tentativo = 2 Then log.ScriviLog("WGTelefono.InviaComando " & comando & " scrittura: " & exScrittura.Message)
                    GestisciDisconnessione()
                End Try
                If scritto Then
                    If Not segnaleEsito.Wait(timeoutMs) Then Return False
                    Return String.Equals(ultimoEsito, "OK", StringComparison.Ordinal)
                End If
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGTelefono.InviaComando " & comando & ": " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ScriviRiga(comando As String)
        SyncLock bloccoScrittura
            Dim stream As NetworkStream = streamHfp
            If stream Is Nothing Then Throw New IOException("Connessione vivavoce non attiva")
            Dim dati As Byte() = Encoding.ASCII.GetBytes(comando & vbCr)
            stream.Write(dati, 0, dati.Length)
            stream.Flush()
        End SyncLock
    End Sub

    Private Sub ChiudiRisorse()
        Try
            Dim stream As NetworkStream
            Dim client As BluetoothClient
            SyncLock bloccoRisorse
                stream = streamHfp
                client = clientHfp
                streamHfp = Nothing
                clientHfp = Nothing
            End SyncLock
            ChiudiTranquillo(stream, client)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ChiudiRisorse: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciDisconnessione()
        Try
            If Not Volatile.Read(inEsecuzione) AndAlso streamHfp Is Nothing Then Return
            Volatile.Write(inEsecuzione, False)
            ultimoEsito = "ERROR"
            Try
                segnaleEsito.Set()
            Catch exSegnale As ObjectDisposedException
                Return
            End Try
            ChiudiRisorse()
            Interlocked.CompareExchange(Of WGTelefono)(istanzaAttiva, Nothing, Me)
            RaiseEvent TelefonoDisconnesso()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.GestisciDisconnessione: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloLettura()
        Try
            Dim stream As NetworkStream = streamHfp
            If stream Is Nothing Then Return
            Dim buffer(2047) As Byte
            Dim accumulato As New StringBuilder()
            Try
                stream.ReadTimeout = 500
            Catch exTimeout As Exception
                log.ScriviLog("WGTelefono.CicloLettura timeout: " & exTimeout.Message)
            End Try
            While Volatile.Read(inEsecuzione)
                Dim letti As Integer = 0
                Try
                    letti = stream.Read(buffer, 0, buffer.Length)
                Catch exIo As IOException
                    If Not Volatile.Read(inEsecuzione) Then Exit While
                    If ScadenzaLettura(exIo) Then Continue While
                    log.ScriviLog("WGTelefono.CicloLettura connessione persa: " & exIo.Message)
                    GestisciDisconnessione()
                    Exit While
                Catch exChiuso As ObjectDisposedException
                    If Volatile.Read(inEsecuzione) Then
                        log.ScriviLog("WGTelefono.CicloLettura flusso chiuso: " & exChiuso.Message)
                        GestisciDisconnessione()
                    End If
                    Exit While
                End Try
                If letti <= 0 Then
                    If Volatile.Read(inEsecuzione) Then GestisciDisconnessione()
                    Exit While
                End If
                Try
                    accumulato.Append(Encoding.ASCII.GetString(buffer, 0, letti))
                    Dim testo As String = accumulato.ToString()
                    Dim righe() As String = testo.Split(New String() {vbCrLf, vbLf, vbCr}, StringSplitOptions.None)
                    For i As Integer = 0 To righe.Length - 2
                        ElaboraRiga(righe(i).Trim())
                    Next
                    accumulato.Clear()
                    accumulato.Append(righe(righe.Length - 1))
                Catch exElabora As Exception
                    log.ScriviLog("WGTelefono.CicloLettura elaborazione: " & exElabora.Message)
                    accumulato.Clear()
                End Try
            End While
        Catch ex As Exception
            log.ScriviLog("WGTelefono.CicloLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraRiga(riga As String)
        If String.IsNullOrEmpty(riga) Then Return
        Try
            If riga = "OK" OrElse riga = "ERROR" Then
                ultimoEsito = riga
                segnaleEsito.Set()
                Return
            End If
            If riga = "RING" Then
                RaiseEvent StatoChiamataCambiato("in_arrivo")
                Return
            End If
            If riga = "NO CARRIER" Then
                RaiseEvent StatoChiamataCambiato("terminata")
                Return
            End If
            If riga.StartsWith("+CLIP:", StringComparison.Ordinal) Then
                Dim numeroClip As String = EstraiNumeroClip(riga)
                RaiseEvent ChiamataInArrivo(numeroClip)
                RaiseEvent IdentificativoChiamante(numeroClip, EstraiNomeClip(riga))
                Return
            End If
            If riga.StartsWith("+CIEV:", StringComparison.Ordinal) Then
                RaiseEvent StatoChiamataCambiato(riga.Substring(6).Trim())
                Return
            End If
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ElaboraRiga: " & ex.Message)
        End Try
    End Sub

    Private Function EstraiNumeroClip(riga As String) As String
        Try
            Dim corpo As String = riga.Substring(6).Trim()
            Dim parti() As String = corpo.Split(","c)
            If parti.Length >= 1 Then Return parti(0).Trim().Trim(""""c)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiNumeroClip: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiNomeClip(riga As String) As String
        Try
            Dim corpo As String = riga.Substring(6).Trim()
            Dim parti() As String = corpo.Split(","c)
            If parti.Length >= 5 Then Return parti(4).Trim().Trim(""""c).Trim()
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiNomeClip: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ImportaRubricaSync(addr As BluetoothAddress) As List(Of Contatto)
        Dim contatti As New List(Of Contatto)()
        Dim client As BluetoothClient = Nothing
        Dim stream As NetworkStream = Nothing
        Try
            client = New BluetoothClient()
            client.Connect(addr, ServizioPbap)
            stream = client.GetStream()
            Dim connId As Byte() = ObexConnetti(stream, TargetPbap)
            If connId Is Nothing Then Return contatti
            Dim corpo As Byte() = ObexGet(stream, connId, "telecom/pb.vcf", "x-bt/phonebook", CostruisciParametriPbap())
            ObexDisconnetti(stream, connId)
            If corpo IsNot Nothing Then contatti = AnalizzaVCard(Encoding.UTF8.GetString(corpo))
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ImportaRubricaSync: " & ex.Message)
        Finally
            ChiudiTranquillo(stream, client)
        End Try
        Return contatti
    End Function

    Private Function LeggiMessaggiSync(addr As BluetoothAddress, numeroMassimo As Integer) As List(Of Messaggio)
        Dim messaggi As New List(Of Messaggio)()
        Dim client As BluetoothClient = Nothing
        Dim stream As NetworkStream = Nothing
        Try
            client = New BluetoothClient()
            client.Connect(addr, ServizioMap)
            stream = client.GetStream()
            Dim connId As Byte() = ObexConnetti(stream, TargetMap)
            If connId Is Nothing Then Return messaggi
            ObexImpostaPercorso(stream, connId, New String() {"telecom", "msg", "inbox"})
            Dim listing As Byte() = ObexGet(stream, connId, "", "x-bt/MAP-msg-listing", CostruisciParametriListing(numeroMassimo))
            If listing IsNot Nothing Then
                For Each m As Messaggio In AnalizzaListing(Encoding.UTF8.GetString(listing))
                    Try
                        Dim corpo As Byte() = ObexGet(stream, connId, m.Handle, "x-bt/message", CostruisciParametriMessaggio())
                        If corpo IsNot Nothing Then m.Testo = EstraiTestoBMessage(Encoding.UTF8.GetString(corpo))
                        messaggi.Add(m)
                    Catch exMsg As Exception
                        log.ScriviLog("WGTelefono.LeggiMessaggiSync singolo: " & exMsg.Message)
                    End Try
                Next
            End If
            ObexDisconnetti(stream, connId)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.LeggiMessaggiSync: " & ex.Message)
        Finally
            ChiudiTranquillo(stream, client)
        End Try
        Return messaggi
    End Function

    Private Function InviaSmsSync(addr As BluetoothAddress, numeroDestinatario As String, testo As String) As Boolean
        Dim client As BluetoothClient = Nothing
        Dim stream As NetworkStream = Nothing
        Try
            client = New BluetoothClient()
            client.Connect(addr, ServizioMap)
            stream = client.GetStream()
            Dim connId As Byte() = ObexConnetti(stream, TargetMap)
            If connId Is Nothing Then Return False
            ObexImpostaPercorso(stream, connId, New String() {"telecom", "msg", "outbox"})
            Dim bmessage As String = CostruisciBMessage(numeroDestinatario, testo)
            Dim esito As Boolean = ObexPut(stream, connId, "", "x-bt/message", Encoding.UTF8.GetBytes(bmessage))
            ObexDisconnetti(stream, connId)
            Return esito
        Catch ex As Exception
            log.ScriviLog("WGTelefono.InviaSmsSync: " & ex.Message)
            Return False
        Finally
            ChiudiTranquillo(stream, client)
        End Try
    End Function

    Private Function ObexConnetti(stream As NetworkStream, target As Byte()) As Byte()
        Try
            Dim headers As New List(Of Byte)()
            headers.Add(&H46)
            Dim lung As Integer = 3 + target.Length
            headers.Add(CByte((lung >> 8) And &HFF))
            headers.Add(CByte(lung And &HFF))
            headers.AddRange(target)

            Dim pacchetto As New List(Of Byte)()
            pacchetto.Add(&H80)
            Dim totale As Integer = 7 + headers.Count
            pacchetto.Add(CByte((totale >> 8) And &HFF))
            pacchetto.Add(CByte(totale And &HFF))
            pacchetto.Add(&H10)
            pacchetto.Add(&H0)
            pacchetto.Add(&H20)
            pacchetto.Add(&H0)
            pacchetto.AddRange(headers)

            stream.Write(pacchetto.ToArray(), 0, pacchetto.Count)
            stream.Flush()

            Dim risposta As Byte() = LeggiPacchettoObex(stream)
            If risposta Is Nothing OrElse risposta.Length < 7 Then Return Nothing
            If risposta(0) <> &HA0 Then Return Nothing
            Return EstraiHeaderObex(risposta, 7, &HCB)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ObexConnetti: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub ObexDisconnetti(stream As NetworkStream, connId As Byte())
        Try
            Dim pacchetto As New List(Of Byte)()
            pacchetto.Add(&H81)
            Dim corpo As New List(Of Byte)()
            AggiungiConnectionId(corpo, connId)
            Dim totale As Integer = 3 + corpo.Count
            pacchetto.Add(CByte((totale >> 8) And &HFF))
            pacchetto.Add(CByte(totale And &HFF))
            pacchetto.AddRange(corpo)
            stream.Write(pacchetto.ToArray(), 0, pacchetto.Count)
            stream.Flush()
            LeggiPacchettoObex(stream)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ObexDisconnetti: " & ex.Message)
        End Try
    End Sub

    Private Sub ObexImpostaPercorso(stream As NetworkStream, connId As Byte(), cartelle As String())
        Try
            InviaSetPath(stream, connId, "", True)
            For Each cartella As String In cartelle
                InviaSetPath(stream, connId, cartella, False)
            Next
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ObexImpostaPercorso: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaSetPath(stream As NetworkStream, connId As Byte(), nome As String, versoRadice As Boolean)
        Try
            Dim corpo As New List(Of Byte)()
            AggiungiConnectionId(corpo, connId)
            Dim flags As Byte = If(versoRadice, CByte(&H2), CByte(&H0))
            If Not versoRadice Then AggiungiHeaderNome(corpo, nome)
            Dim pacchetto As New List(Of Byte)()
            pacchetto.Add(&H85)
            Dim totale As Integer = 3 + 2 + corpo.Count
            pacchetto.Add(CByte((totale >> 8) And &HFF))
            pacchetto.Add(CByte(totale And &HFF))
            pacchetto.Add(flags)
            pacchetto.Add(&H0)
            pacchetto.AddRange(corpo)
            stream.Write(pacchetto.ToArray(), 0, pacchetto.Count)
            stream.Flush()
            LeggiPacchettoObex(stream)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.InviaSetPath: " & ex.Message)
        End Try
    End Sub

    Private Function ObexGet(stream As NetworkStream, connId As Byte(), nome As String, tipo As String, parametri As Byte()) As Byte()
        Try
            Dim corpoRisultato As New List(Of Byte)()
            Dim primaIterazione As Boolean = True
            Dim codice As Byte = &H90
            While codice = &H90
                Dim corpo As New List(Of Byte)()
                AggiungiConnectionId(corpo, connId)
                If primaIterazione Then
                    If Not String.IsNullOrEmpty(nome) Then AggiungiHeaderNome(corpo, nome)
                    If Not String.IsNullOrEmpty(tipo) Then AggiungiHeaderTipo(corpo, tipo)
                    If parametri IsNot Nothing AndAlso parametri.Length > 0 Then AggiungiHeaderParametri(corpo, parametri)
                    primaIterazione = False
                End If
                Dim pacchetto As New List(Of Byte)()
                pacchetto.Add(&H83)
                Dim totale As Integer = 3 + corpo.Count
                pacchetto.Add(CByte((totale >> 8) And &HFF))
                pacchetto.Add(CByte(totale And &HFF))
                pacchetto.AddRange(corpo)
                stream.Write(pacchetto.ToArray(), 0, pacchetto.Count)
                stream.Flush()

                Dim risposta As Byte() = LeggiPacchettoObex(stream)
                If risposta Is Nothing OrElse risposta.Length < 3 Then Return Nothing
                codice = risposta(0)
                If codice <> &H90 AndAlso codice <> &HA0 Then Return Nothing
                Dim corpoParziale As Byte() = EstraiCorpoObex(risposta, 3)
                If corpoParziale IsNot Nothing Then corpoRisultato.AddRange(corpoParziale)
            End While
            Return corpoRisultato.ToArray()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ObexGet: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function ObexPut(stream As NetworkStream, connId As Byte(), nome As String, tipo As String, dati As Byte()) As Boolean
        Try
            Dim intestazioneBase As New List(Of Byte)()
            AggiungiConnectionId(intestazioneBase, connId)
            If Not String.IsNullOrEmpty(nome) Then AggiungiHeaderNome(intestazioneBase, nome)
            If Not String.IsNullOrEmpty(tipo) Then AggiungiHeaderTipo(intestazioneBase, tipo)

            Dim corpoChunk As Integer = Math.Min(dati.Length, 512)
            Dim finale As Boolean = (corpoChunk = dati.Length)
            Dim headerCorpo As New List(Of Byte)()
            headerCorpo.Add(If(finale, CByte(&H49), CByte(&H48)))
            Dim lungCorpo As Integer = 3 + corpoChunk
            headerCorpo.Add(CByte((lungCorpo >> 8) And &HFF))
            headerCorpo.Add(CByte(lungCorpo And &HFF))
            headerCorpo.AddRange(dati.Take(corpoChunk))

            Dim pacchettoUno As New List(Of Byte)()
            pacchettoUno.Add(If(finale, CByte(&H82), CByte(&H2)))
            Dim totale As Integer = 3 + intestazioneBase.Count + headerCorpo.Count
            pacchettoUno.Add(CByte((totale >> 8) And &HFF))
            pacchettoUno.Add(CByte(totale And &HFF))
            pacchettoUno.AddRange(intestazioneBase)
            pacchettoUno.AddRange(headerCorpo)
            stream.Write(pacchettoUno.ToArray(), 0, pacchettoUno.Count)
            stream.Flush()
            Dim risposta As Byte() = LeggiPacchettoObex(stream)
            If risposta Is Nothing OrElse risposta.Length < 1 Then Return False

            Dim inviati As Integer = corpoChunk
            While inviati < dati.Length
                Dim pezzo As Integer = Math.Min(dati.Length - inviati, 512)
                Dim ultimo As Boolean = (inviati + pezzo = dati.Length)
                Dim p As New List(Of Byte)()
                p.Add(If(ultimo, CByte(&H82), CByte(&H2)))
                Dim hc As New List(Of Byte)()
                hc.Add(If(ultimo, CByte(&H49), CByte(&H48)))
                Dim lc As Integer = 3 + pezzo
                hc.Add(CByte((lc >> 8) And &HFF))
                hc.Add(CByte(lc And &HFF))
                hc.AddRange(dati.Skip(inviati).Take(pezzo))
                Dim tot As Integer = 3 + hc.Count
                p.Add(CByte((tot >> 8) And &HFF))
                p.Add(CByte(tot And &HFF))
                p.AddRange(hc)
                stream.Write(p.ToArray(), 0, p.Count)
                stream.Flush()
                risposta = LeggiPacchettoObex(stream)
                inviati += pezzo
            End While
            If risposta Is Nothing OrElse risposta.Length < 1 Then Return False
            Return risposta(0) = &HA0
        Catch ex As Exception
            log.ScriviLog("WGTelefono.ObexPut: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LeggiPacchettoObex(stream As NetworkStream) As Byte()
        Try
            stream.ReadTimeout = 10000
            Dim testa(2) As Byte
            If Not LeggiEsatto(stream, testa, 3) Then Return Nothing
            Dim lunghezza As Integer = (CInt(testa(1)) << 8) Or CInt(testa(2))
            Dim pacchetto(lunghezza - 1) As Byte
            pacchetto(0) = testa(0)
            pacchetto(1) = testa(1)
            pacchetto(2) = testa(2)
            If lunghezza > 3 Then
                Dim resto(lunghezza - 4) As Byte
                If Not LeggiEsatto(stream, resto, lunghezza - 3) Then Return Nothing
                Array.Copy(resto, 0, pacchetto, 3, lunghezza - 3)
            End If
            Return pacchetto
        Catch ex As Exception
            log.ScriviLog("WGTelefono.LeggiPacchettoObex: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function LeggiEsatto(stream As NetworkStream, buffer As Byte(), quantita As Integer) As Boolean
        Dim totali As Integer = 0
        While totali < quantita
            Dim letti As Integer = stream.Read(buffer, totali, quantita - totali)
            If letti <= 0 Then Return False
            totali += letti
        End While
        Return True
    End Function

    Private Function EstraiHeaderObex(pacchetto As Byte(), inizio As Integer, idHeader As Byte) As Byte()
        Try
            Dim i As Integer = inizio
            While i < pacchetto.Length
                Dim id As Byte = pacchetto(i)
                Dim altoNibble As Integer = (id >> 6) And &H3
                If altoNibble = 0 OrElse altoNibble = 1 Then
                    Dim lung As Integer = (CInt(pacchetto(i + 1)) << 8) Or CInt(pacchetto(i + 2))
                    If id = idHeader Then
                        Dim ris(lung - 3 - 1) As Byte
                        Array.Copy(pacchetto, i + 3, ris, 0, lung - 3)
                        Return ris
                    End If
                    i += lung
                ElseIf altoNibble = 2 Then
                    If id = idHeader Then
                        Dim ris(0) As Byte
                        ris(0) = pacchetto(i + 1)
                        Return ris
                    End If
                    i += 2
                Else
                    If id = idHeader Then
                        Dim ris(3) As Byte
                        Array.Copy(pacchetto, i + 1, ris, 0, 4)
                        Return ris
                    End If
                    i += 5
                End If
            End While
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiHeaderObex: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function EstraiCorpoObex(pacchetto As Byte(), inizio As Integer) As Byte()
        Try
            Dim risultato As New List(Of Byte)()
            Dim i As Integer = inizio
            While i < pacchetto.Length
                Dim id As Byte = pacchetto(i)
                Dim altoNibble As Integer = (id >> 6) And &H3
                If altoNibble = 0 OrElse altoNibble = 1 Then
                    Dim lung As Integer = (CInt(pacchetto(i + 1)) << 8) Or CInt(pacchetto(i + 2))
                    If id = &H48 OrElse id = &H49 Then
                        For j As Integer = i + 3 To i + lung - 1
                            risultato.Add(pacchetto(j))
                        Next
                    End If
                    i += lung
                ElseIf altoNibble = 2 Then
                    i += 2
                Else
                    i += 5
                End If
            End While
            If risultato.Count = 0 Then Return Nothing
            Return risultato.ToArray()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiCorpoObex: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub AggiungiConnectionId(corpo As List(Of Byte), connId As Byte())
        If connId Is Nothing OrElse connId.Length < 4 Then Return
        corpo.Add(&HCB)
        corpo.Add(connId(0))
        corpo.Add(connId(1))
        corpo.Add(connId(2))
        corpo.Add(connId(3))
    End Sub

    Private Sub AggiungiHeaderNome(corpo As List(Of Byte), nome As String)
        Dim unicode As Byte() = Encoding.BigEndianUnicode.GetBytes(nome)
        Dim lung As Integer = 3 + unicode.Length + 2
        corpo.Add(&H1)
        corpo.Add(CByte((lung >> 8) And &HFF))
        corpo.Add(CByte(lung And &HFF))
        corpo.AddRange(unicode)
        corpo.Add(&H0)
        corpo.Add(&H0)
    End Sub

    Private Sub AggiungiHeaderTipo(corpo As List(Of Byte), tipo As String)
        Dim ascii As Byte() = Encoding.ASCII.GetBytes(tipo)
        Dim lung As Integer = 3 + ascii.Length + 1
        corpo.Add(&H42)
        corpo.Add(CByte((lung >> 8) And &HFF))
        corpo.Add(CByte(lung And &HFF))
        corpo.AddRange(ascii)
        corpo.Add(&H0)
    End Sub

    Private Sub AggiungiHeaderParametri(corpo As List(Of Byte), parametri As Byte())
        Dim lung As Integer = 3 + parametri.Length
        corpo.Add(&H4C)
        corpo.Add(CByte((lung >> 8) And &HFF))
        corpo.Add(CByte(lung And &HFF))
        corpo.AddRange(parametri)
    End Sub

    Private Function CostruisciParametriPbap() As Byte()
        Return New Byte() {&H4, &H1, &H1}
    End Function

    Private Function CostruisciParametriListing(numeroMassimo As Integer) As Byte()
        Return New Byte() {&H1, &H2, CByte((numeroMassimo >> 8) And &HFF), CByte(numeroMassimo And &HFF)}
    End Function

    Private Function CostruisciParametriMessaggio() As Byte()
        Return New Byte() {&HA, &H1, &H1}
    End Function

    Private Function AnalizzaVCard(testo As String) As List(Of Contatto)
        Dim contatti As New List(Of Contatto)()
        Try
            Dim corrente As Contatto = Nothing
            For Each rigaRaw As String In testo.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
                Dim riga As String = rigaRaw.Trim()
                If riga.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) Then
                    corrente = New Contatto()
                ElseIf riga.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase) Then
                    If corrente IsNot Nothing Then
                        contatti.Add(corrente)
                        corrente = Nothing
                    End If
                ElseIf corrente IsNot Nothing Then
                    If riga.StartsWith("FN", StringComparison.OrdinalIgnoreCase) Then
                        Dim idx As Integer = riga.IndexOf(":"c)
                        If idx >= 0 Then corrente.Nome = riga.Substring(idx + 1).Trim()
                    ElseIf riga.StartsWith("TEL", StringComparison.OrdinalIgnoreCase) Then
                        Dim idx As Integer = riga.IndexOf(":"c)
                        If idx >= 0 Then
                            Dim numero As String = riga.Substring(idx + 1).Trim()
                            If Not String.IsNullOrEmpty(numero) Then corrente.Numeri.Add(numero)
                        End If
                    End If
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGTelefono.AnalizzaVCard: " & ex.Message)
        End Try
        Return contatti
    End Function

    Private Function AnalizzaListing(xml As String) As List(Of Messaggio)
        Dim lista As New List(Of Messaggio)()
        Try
            Dim posizione As Integer = 0
            While True
                Dim inizio As Integer = xml.IndexOf("<msg ", posizione, StringComparison.OrdinalIgnoreCase)
                If inizio < 0 Then Exit While
                Dim fine As Integer = xml.IndexOf(">", inizio, StringComparison.Ordinal)
                If fine < 0 Then Exit While
                Dim elemento As String = xml.Substring(inizio, fine - inizio + 1)
                Dim m As New Messaggio()
                m.Handle = EstraiAttributo(elemento, "handle")
                m.Mittente = EstraiAttributo(elemento, "sender_addressing")
                m.DataOra = EstraiAttributo(elemento, "datetime")
                m.Letto = String.Equals(EstraiAttributo(elemento, "read"), "yes", StringComparison.OrdinalIgnoreCase)
                If Not String.IsNullOrEmpty(m.Handle) Then lista.Add(m)
                posizione = fine + 1
            End While
        Catch ex As Exception
            log.ScriviLog("WGTelefono.AnalizzaListing: " & ex.Message)
        End Try
        Return lista
    End Function

    Private Function EstraiAttributo(elemento As String, nome As String) As String
        Try
            Dim chiave As String = nome & "="""
            Dim idx As Integer = elemento.IndexOf(chiave, StringComparison.OrdinalIgnoreCase)
            If idx < 0 Then Return ""
            idx += chiave.Length
            Dim fine As Integer = elemento.IndexOf(""""c, idx)
            If fine < 0 Then Return ""
            Return elemento.Substring(idx, fine - idx)
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiAttributo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiTestoBMessage(bmessage As String) As String
        Try
            Dim inizio As Integer = bmessage.IndexOf("BEGIN:MSG", StringComparison.OrdinalIgnoreCase)
            Dim fine As Integer = bmessage.IndexOf("END:MSG", StringComparison.OrdinalIgnoreCase)
            If inizio < 0 OrElse fine < 0 OrElse fine <= inizio Then Return ""
            Dim daInizio As Integer = bmessage.IndexOf(vbLf, inizio)
            If daInizio < 0 Then Return ""
            Return bmessage.Substring(daInizio + 1, fine - daInizio - 1).Trim(New Char() {CChar(vbCr), CChar(vbLf), " "c})
        Catch ex As Exception
            log.ScriviLog("WGTelefono.EstraiTestoBMessage: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function CostruisciBMessage(numero As String, testo As String) As String
        Dim testoSicuro As String = If(testo, "")
        Dim sb As New StringBuilder()
        sb.Append("BEGIN:BMSG" & vbCrLf)
        sb.Append("VERSION:1.0" & vbCrLf)
        sb.Append("STATUS:UNREAD" & vbCrLf)
        sb.Append("TYPE:SMS_GSM" & vbCrLf)
        sb.Append("FOLDER:telecom/msg/outbox" & vbCrLf)
        sb.Append("BEGIN:VCARD" & vbCrLf)
        sb.Append("VERSION:2.1" & vbCrLf)
        sb.Append("N:" & vbCrLf)
        sb.Append("TEL:" & numero & vbCrLf)
        sb.Append("END:VCARD" & vbCrLf)
        sb.Append("BEGIN:BENV" & vbCrLf)
        sb.Append("BEGIN:BBODY" & vbCrLf)
        sb.Append("CHARSET:UTF-8" & vbCrLf)
        sb.Append("LENGTH:" & (testoSicuro.Length + 22).ToString(CultureInfo.InvariantCulture) & vbCrLf)
        sb.Append("BEGIN:MSG" & vbCrLf)
        sb.Append(testoSicuro & vbCrLf)
        sb.Append("END:MSG" & vbCrLf)
        sb.Append("END:BBODY" & vbCrLf)
        sb.Append("END:BENV" & vbCrLf)
        sb.Append("END:BMSG" & vbCrLf)
        Return sb.ToString()
    End Function

    Private Function PulisciNumero(numero As String) As String
        Try
            If String.IsNullOrEmpty(numero) Then Return ""
            Dim sb As New StringBuilder()
            For Each c As Char In numero
                If Char.IsDigit(c) OrElse c = "+"c OrElse c = "*"c OrElse c = "#"c Then sb.Append(c)
            Next
            Return sb.ToString()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.PulisciNumero: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ChiudiTranquillo(stream As NetworkStream, client As BluetoothClient)
        Try
            If stream IsNot Nothing Then stream.Close()
        Catch
        End Try
        Try
            If client IsNot Nothing Then client.Close()
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            eliminato = True
            ultimoIndirizzo = Nothing
            ChiudiChiamate()
        Catch ex As Exception
            log.ScriviLog("WGTelefono.Dispose: " & ex.Message)
        End Try
        Try
            segnaleEsito.Dispose()
        Catch exSegnale As Exception
            log.ScriviLog("WGTelefono.Dispose segnale: " & exSegnale.Message)
        End Try
    End Sub

End Class