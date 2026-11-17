' ============================================================================================
' sintesivocale - la voce di WinGabriel
' ============================================================================================
'
' A cosa serve
'   E' il punto unico da cui passa tutto quello che il programma deve dire. Parla tramite il
'   server SAPI di WinGabriel (WGSapi.exe, avviato e sorvegliato da questa classe); e' prevista
'   anche la modalita' "ScreenReader" (NVDA), oggi disattivata, e la modalita' "Nessun lettore",
'   con cui il programma resta muto. Gestisce voce, velocita', volume, dispositivo audio,
'   dizionario di pronuncia, lettura della punteggiatura e dei numeri.
'
' Come si usa
'   Dim voce As sintesivocale = sintesivocale.GetInstance()   ' unico esemplare, non usare New
'   voce.LeggiTesto("Buongiorno")
'   voce.LeggiTesto("Attenzione", priorita:="Alta")           ' interrompe cio' che sta dicendo
'   voce.FermaVoce()                                          ' zittisce subito
'   voce.IncrementaVelocita() : voce.DecrementaVolume()
'   If voce.StaParlando Then ...
'
' Metodi ed eventi pubblici principali
'   GetInstance       restituisce l'esemplare condiviso (Nothing solo in caso di errore grave).
'   LeggiTesto        mette in lettura un testo; con priorita' "Alta" interrompe il testo in
'                     corso. Se il canale di voce non e' ancora pronto il testo viene messo da
'                     parte e detto appena possibile (entro 30 secondi).
'   FermaVoce        interrompe la lettura.
'   StaParlando / SintesiInParlato / CanalePronto / AttendiCanalePronto  stato della voce.
'   OttieniSintesiSapi, ImpostaSintesiSapi, ImpostaVoceNvda, IncrementaVelocita,
'   DecrementaVelocita, IncrementaVolume, DecrementaVolume, ApplicaImpostazioniSapi  gestione
'   di voce, velocita' e volume; le impostazioni sono lette e scritte nella configurazione.
'   AvviaAnteprimaImpostazioni / AnteprimaVoce / AnteprimaVelocita / AnteprimaVolume /
'   ConfermaAnteprimaImpostazioni / AnnullaAnteprimaImpostazioni  provano una voce senza
'   salvarla e permettono di tornare indietro.
'   FermaTutto        ferma sorveglianza, monitor e connessioni (chiusura del programma).
'   OnTestoRicevuto, OnFineLettura, OnParolaLetta, OnImpostazioniNvdaSalvate  eventi, riportati
'   sul thread dell'interfaccia quando c'e' una finestra aperta.
'
' Errori
'   Nessun metodo solleva eccezioni: tutto finisce nel log (dati\logs\log.txt) e le funzioni
'   restituiscono un valore neutro (False, Nothing, elenco vuoto). Se il server SAPI non
'   risponde viene riavviato da solo; se manca WGSapi.exe il programma resta muto ma continua a
'   funzionare.
'
' Attenzione
'   La classe e' un singolo esemplare condiviso: si ottiene solo con GetInstance(). LeggiTesto
'   non aspetta la fine della lettura, e i testi lunghi vengono spezzati e detti in sequenza.
'   Gli eventi possono arrivare da thread di sfondo quando non c'e' nessuna finestra aperta.
' ============================================================================================
Imports System.Collections.Concurrent
Imports System.Diagnostics
Imports System.IO
Imports System.IO.MemoryMappedFiles
Imports System.IO.Pipes
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Runtime
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Security.Cryptography
Imports NAudio.CoreAudioApi
Imports NAudio.CoreAudioApi.Interfaces
Imports wgcontrols

Public Class Sintesi
    Private log As New wgcontrols.Logger()

    Public Property Id As String

    Public Property Nome As String

    Public Property Attiva As Boolean

    Public Property Tipo As String

    Public Sub New()
        Try
        Catch ex As Exception
            log.ScriviLog("Sintesi.New: " & ex.Message)
        End Try
    End Sub

    Public Sub New(id As String, nome As String, attiva As Boolean, tipo As String)
        Try
            Me.Id = id
            Me.Nome = nome
            Me.Attiva = attiva
            Me.Tipo = tipo
        Catch ex As Exception
            log.ScriviLog("Sintesi.New (parametrico): " & ex.Message)
        End Try
    End Sub

    Public Overrides Function ToString() As String
        Try
            Return Nome
        Catch ex As Exception
            log.ScriviLog("Sintesi.ToString: " & ex.Message)
            Return String.Empty
        End Try
    End Function
End Class

Partial Public Class sintesivocale
    Implements IDisposable

    Private Class NotificaCambioDeviceAudio
        Implements IMMNotificationClient

        Private ReadOnly proprietario As sintesivocale

        Public Sub New(proprietario As sintesivocale)
            Me.proprietario = proprietario
        End Sub

        Public Sub OnDeviceStateChanged(deviceId As String, newState As DeviceState) Implements IMMNotificationClient.OnDeviceStateChanged
            If newState = DeviceState.Active OrElse proprietario Is Nothing Then Return
            ' Fuori dalla chiamata di Windows: qui non si deve aspettare.
            Task.Run(Sub() proprietario.GestisciDispositivoAudioNonDisponibile(deviceId))
        End Sub

        Public Sub OnDeviceAdded(pwstrDeviceId As String) Implements IMMNotificationClient.OnDeviceAdded
        End Sub

        Public Sub OnDeviceRemoved(deviceId As String) Implements IMMNotificationClient.OnDeviceRemoved
            If proprietario Is Nothing Then Return
            Task.Run(Sub() proprietario.GestisciDispositivoAudioNonDisponibile(deviceId))
        End Sub

        Public Sub OnDefaultDeviceChanged(flow As DataFlow, role As Role, defaultDeviceId As String) Implements IMMNotificationClient.OnDefaultDeviceChanged
            Try
                If proprietario Is Nothing Then Return
                proprietario.GestisciCambioDeviceAudioPredefinito(flow, role, defaultDeviceId)
            Catch ex As Exception
                Try
                    proprietario.log.ScriviLog("NotificaCambioDeviceAudio.OnDefaultDeviceChanged: " & ex.Message)
                Catch ex2 As Exception
                    proprietario.log.ScriviLog(ex2.Message & ex2.StackTrace)
                End Try
            End Try
        End Sub

        Public Sub OnPropertyValueChanged(pwstrDeviceId As String, key As PropertyKey) Implements IMMNotificationClient.OnPropertyValueChanged
        End Sub
    End Class

    Private Shared instance As sintesivocale
    Private Shared ReadOnly instanceLock As New Object()
    Private nvdaPipeClient As NamedPipeClientStream
    Private isListening As Boolean
    Private ReadOnly serverLock As New Object()

    Public Event OnTestoRicevuto(testo As String)
    Public Event OnFineLettura()
    Public Event OnParolaLetta(posizione As Integer, lunghezza As Integer)
    Public Event OnImpostazioniNvdaSalvate(successo As Boolean)
    Public Event OnProgressoMp3(idLavoro As String, percentuale As Integer)
    Public Event OnFineConversioneMp3(idLavoro As String, esito As Boolean, messaggio As String)
public event vocestoppata as EventHandler 
    Public Event OnInizioLettura(testo As String, origine As String)
    Public Event OnStatoParlatoCambiato(staParlando As Boolean)
    Private log As New wgcontrols.Logger()
    Private Shared ReadOnly logShared As New wgcontrols.Logger()

    Private modalitaCorrente As String = ""
    Private ReadOnly setupLock As New Object()
    Private disposedValue As Boolean

    Private masterCts As New CancellationTokenSource()

    Private ReadOnly tcpWriteLock As New Object()
    Private ReadOnly tcpReadLock As New Object()

    Private nvdaWriter As StreamWriter
    Public NvdaVoci As New ConcurrentDictionary(Of String, String)()
    Public NvdaSintetizzatori As New ConcurrentDictionary(Of String, String)()
    Private NvdaStato As New ConcurrentDictionary(Of String, String)()

    Private sapiInizializzato As Boolean = False
    Private sapiInAscolto As Boolean = False
    Private sapiProcess As Process
    Private sapiClient As TcpClient
    Private sapiStream As Stream
    Private sapiWriter As StreamWriter
    Private sapiReader As StreamReader
    Private sapiThread As Thread
    Private sapiToken As String = ""
    Private sapiPort As Integer = 44445
    Private sapiPrivateKeyXml As String = ""

    Private sapiTransport As String = ""
    Private sapiPipe As NamedPipeClientStream
    Private sapiShm As MemoryMappedFile
    Private sapiShmAccessor As MemoryMappedViewAccessor
    Private hShmEventC2S As EventWaitHandle
    Private hShmEventC2SAck As EventWaitHandle
    Private hShmEventS2C As EventWaitHandle
    Private hShmEventS2CAck As EventWaitHandle

    Private _sapiVoci As New ConcurrentDictionary(Of String, String)()
    Private _sapiVociIndicizzate As New ConcurrentDictionary(Of String, String)()
    Private _sapiAudioDevices As New ConcurrentDictionary(Of String, String)()
    Private tentativiFailover As New List(Of String)()

    Private deviceEnumerator As MMDeviceEnumerator
    Private notificaDeviceAudio As NotificaCambioDeviceAudio
    Private ReadOnly deviceAudioLock As New Object()

    Private pingThread As Thread
    Private pingRunning As Boolean = False

    Private sapiAes As Aes
    Private sapiIsEncrypted As Boolean = False
    Private currentSapiTestoId As Integer = 0
    Private ReadOnly _codaComandiSapi As New BlockingCollection(Of String)()
    Private _threadCodaComandiSapiAvviato As Boolean = False
    Private ReadOnly _threadCodaComandiSapiLock As New Object()

    Private Sub AssicuraThreadCodaComandiSapi()
        Try
            If _threadCodaComandiSapiAvviato Then Return
            SyncLock _threadCodaComandiSapiLock
                If _threadCodaComandiSapiAvviato Then Return
                Dim t As New Thread(AddressOf EseguiConsumerCodaComandiSapi)
                t.IsBackground = True
                t.Start()
                _threadCodaComandiSapiAvviato = True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AssicuraThreadCodaComandiSapi: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiConsumerCodaComandiSapi()
        Try
            For Each comando As String In _codaComandiSapi.GetConsumingEnumerable()
                Try
                    ' Scarta i chunk SPEAK ormai obsoleti: se nel frattempo e' stato
                    ' chiamato FermaVoce() (che incrementa currentSapiTestoId), un
                    ' chunk gia' accodato per la lettura precedente non deve piu'
                    ' essere inviato a WGSapi.exe. Questo garantisce che uno stop
                    ' annulli davvero TUTTI i chunk residui, anche quelli che erano
                    ' gia' in coda prima dello stop stesso, non solo quelli non
                    ' ancora generati dal ciclo di chunking in LeggiTesto.
                    If comando.StartsWith("SPEAK|", StringComparison.Ordinal) Then
                        Dim parti() As String = comando.Split("|"c)
                        Dim msgIdComando As Integer
                        If parti.Length > 1 AndAlso Integer.TryParse(parti(1), msgIdComando) Then
                            If msgIdComando <> currentSapiTestoId Then
                                Continue For
                            End If
                        End If
                    End If

                    InviaComandoSapiSicuro(comando)
                Catch ex As Exception
                    log.ScriviLog("EseguiConsumerCodaComandiSapi (invio): " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("EseguiConsumerCodaComandiSapi: " & ex.Message)
        End Try
    End Sub

    ' Punto unico di invio verso WGSapi: garantisce che i comandi (STOP, SPEAK, impostazioni)
    ' vengano trasmessi nello stesso ordine in cui sono stati richiesti dal chiamante.
    ' Non usare Task.Run indipendenti per inviare comandi SAPI: due Task.Run separati non hanno
    ' alcuna garanzia sull'ordine di esecuzione reale, e uno STOP inviato per secondo potrebbe
    ' arrivare prima di uno SPEAK inviato per primo, interrompendo un annuncio a metà.
    Private Sub InviaComandoSapiInCoda(comando As String)
        Try
            AssicuraThreadCodaComandiSapi()
            _codaComandiSapi.Add(comando)
        Catch ex As Exception
            log.ScriviLog("InviaComandoSapiInCoda: " & ex.Message)
        End Try
    End Sub

    Private sapiDoneMsgId As Integer = -1
    Private sapiUltimoSegnaleTick As Long = 0
    Private ReadOnly sapiSyncLock As New Object()

    Private watchdogRunning As Boolean = False

    Private monitorImpostazioniRunning As Boolean = False
    Private _ultimoRateApplicato As String = Nothing
    Private _ultimoVolumeApplicato As String = Nothing
    Private _ultimaVoceApplicata As String = Nothing
    Private _chiaveVoceStaccata As String = Nothing
    Private Shared ReadOnly _puntoNelNome As New System.Text.RegularExpressions.Regex("(?<=[\p{L}\p{N}])\.(?=\p{L})", System.Text.RegularExpressions.RegexOptions.Compiled)
    Private _voceStaccata As Boolean
    Private _voceConfigurata As String = ""
    Private _tickVoceConfigurata As Long
    Private _ultimoDeviceApplicato As String = Nothing
    Private ReadOnly _lockUltimiApplicati As New Object()
    Private _ultimoNvdaVoceApplicata As String = Nothing
    Private _ultimoNvdaRateApplicato As String = Nothing
    Private _ultimoNvdaVolumeApplicato As String = Nothing
    Private dictPronuncia As New List(Of KeyValuePair(Of String, String))()
    Private dictPronunciaLock As New Object()

    Private conteggioRamCritica As Integer = 0

    Private _soppressione500MsTick As Long = 0
    Private ReadOnly _soppressione500MsLock As New Object()

    Private _staParlando As Integer = 0
    Private _nvdaCollegato As Integer = 0
    Private _tickInizioParlato As Long = 0
    Private _zeriParlato As Integer = 0
    Private Const GraziaParlatoMs As Integer = 50
    Private Const ZeriParlatoConferma As Integer = 1

    Private ReadOnly _canaleProntoEvento As New ManualResetEventSlim(False)
    Private _ricaricaConfigInCorso As Integer = 0
    Private _ultimaModalitaMonitorata As String = Nothing

    Private WithEvents _tastiera As WGTastiera

    Private ReadOnly _testoInAttesaLock As New Object()
    Private _testoInAttesa As String = ""
    Private _prioritaInAttesa As String = "Normale"
    Private _tracciaParoleInAttesa As Boolean = False
    Private _escludiPunteggiaturaInAttesa As Boolean = False
    Private _tickTestoInAttesa As Long = 0
    Private Const ScadenzaTestoInAttesaMs As Integer = 30000

    Private ReadOnly _anteprimaLock As New Object()
    Private _anteprimaAttiva As Boolean = False
    Private _anteprimaModalita As String = ""
    Private _anteprimaRateOriginale As String = ""
    Private _anteprimaVolumeOriginale As String = ""
    Private _anteprimaVoceOriginale As String = ""
    Private _anteprimaRateModificato As Boolean = False
    Private _anteprimaVolumeModificato As Boolean = False
    Private _anteprimaVoceModificata As Boolean = False

    Private Const ModalitaLetturaPredefinita As String = "SAPI"
    Private Const SapiRatePredefinito As String = "50"
    Private Const SapiVolumePredefinito As String = "80"
    Private Const SapiVocePredefinita As String = "0"
    Private _configurazioneCreataOra As Integer = 0

    ' Environment.TickCount torna a zero dopo circa 24 giorni di acceso e le differenze fra
    ' due letture diventano negative: qui si usa un cronometro che non va mai in overflow.
    Private Shared ReadOnly _cronometroTick As Stopwatch = Stopwatch.StartNew()

    Private Shared Function TickCorrenti() As Long
        Try
            Return _cronometroTick.ElapsedMilliseconds
        Catch ex As Exception
            logShared.ScriviLog("TickCorrenti: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function CodificaHex(dati As Byte()) As String
        Try
            If dati Is Nothing Then Return String.Empty
            Dim hexStr As New StringBuilder(dati.Length * 2)
            For Each b As Byte In dati
                hexStr.AppendFormat("{0:X2}", b)
            Next
            Return hexStr.ToString()
        Catch ex As Exception
            log.ScriviLog("CodificaHex: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Function DecodificaHex(hexStr As String) As Byte()
        Try
            If String.IsNullOrEmpty(hexStr) Then Return New Byte() {}
            Dim length As Integer = hexStr.Length
            ' Con un numero dispari di caratteri il vettore avrebbe dimensione sbagliata.
            If length < 2 OrElse (length Mod 2) <> 0 Then Return New Byte() {}
            Dim bytes((length \ 2) - 1) As Byte
            For i As Integer = 0 To length - 1 Step 2
                bytes(i \ 2) = Convert.ToByte(hexStr.Substring(i, 2), 16)
            Next
            Return bytes
        Catch ex As Exception
            log.ScriviLog("DecodificaHex: " & ex.Message)
            Return New Byte() {}
        End Try
    End Function

    Private Function DecodificaCampoNvda(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return valore
            If valore.IndexOf("%"c) < 0 Then Return valore

            Dim sb As New StringBuilder(valore.Length)
            Dim i As Integer = 0
            While i < valore.Length
                If valore(i) = "%"c AndAlso i + 2 < valore.Length Then
                    Dim codice As String = valore.Substring(i + 1, 2).ToUpperInvariant()
                    Select Case codice
                        Case "25"
                            sb.Append("%"c)
                            i += 3
                        Case "3A"
                            sb.Append(":"c)
                            i += 3
                        Case "3D"
                            sb.Append("="c)
                            i += 3
                        Case "7C"
                            sb.Append("|"c)
                            i += 3
                        Case "0D"
                            sb.Append(vbCr)
                            i += 3
                        Case "0A"
                            sb.Append(vbLf)
                            i += 3
                        Case Else
                            sb.Append(valore(i))
                            i += 1
                    End Select
                Else
                    sb.Append(valore(i))
                    i += 1
                End If
            End While
            Return sb.ToString()
        Catch ex As Exception
            log.ScriviLog("DecodificaCampoNvda: " & ex.Message)
            Return valore
        End Try
    End Function

    Private Function OttieniPortaLiberaSapi() As Integer
        Try
            Dim prop = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
            Dim activeConns = prop.GetActiveTcpConnections()
            Dim activeListeners = prop.GetActiveTcpListeners()
            Dim usedPorts As New HashSet(Of Integer)()
            For Each c In activeConns
                usedPorts.Add(c.LocalEndPoint.Port)
            Next
            For Each l In activeListeners
                usedPorts.Add(l.Port)
            Next
            Dim rnd As New Random()
            For i As Integer = 0 To 100
                Dim port As Integer = rnd.Next(45000, 60000)
                If Not usedPorts.Contains(port) Then Return port
            Next
        Catch ex As Exception
            log.ScriviLog("OttieniPortaLiberaSapi: " & ex.Message)
        End Try
        Return 44445
    End Function

    Private Sub InviaComandoSapiSicuro(comando As String)
        Try
            ' Con eSpeak i comandi non vanno a WGSapi.exe: li esegue il motore di eSpeak (sintesivocale.Espeak.vb).
            If _espeakAttivo Then
                ElaboraComandoEspeak(comando)
                Return
            End If
            Dim eventoAckDaAttendere As EventWaitHandle = Nothing

            SyncLock tcpWriteLock
                Dim finale As String = comando
                If sapiIsEncrypted AndAlso sapiAes IsNot Nothing Then
                    Dim data As Byte() = Encoding.UTF8.GetBytes(comando)
                    Using encryptor As ICryptoTransform = sapiAes.CreateEncryptor()
                        Dim cipher As Byte() = encryptor.TransformFinalBlock(data, 0, data.Length)
                        finale = CodificaHex(cipher)
                    End Using
                End If

                If sapiTransport = "TCP" OrElse sapiTransport = "PIPE" Then
                    If sapiWriter IsNot Nothing Then
                        Try
                            sapiWriter.WriteLine(finale)
                        Catch ex As Exception
                            log.ScriviLog("InviaComandoSapiSicuro (WriteLine): " & ex.Message)
                            sapiInizializzato = False
                            pingRunning = False
                        End Try
                    End If
                ElseIf sapiTransport = "SHM" AndAlso sapiShmAccessor IsNot Nothing Then
                    Try
                        Dim msgBytes As Byte() = Encoding.UTF8.GetBytes(finale & vbLf)
                        ' La metà C2S è di 65536 byte. Un comando più lungo scrive sopra la metà S2C
                        ' e la voce cade o riceve eventi illeggibili.
                        If msgBytes.Length <= 0 OrElse msgBytes.Length > 65532 Then
                            log.ScriviLog("InviaComandoSapiSicuro (SHM): comando di " & msgBytes.Length.ToString() & " byte, fuori dalla memoria condivisa")
                            Return
                        End If
                        sapiShmAccessor.Write(0, msgBytes.Length)
                        sapiShmAccessor.WriteArray(4, msgBytes, 0, msgBytes.Length)
                        If hShmEventC2S IsNot Nothing Then hShmEventC2S.Set()
                        ' L'attesa dell'ack (fino a 2s) viene fatta FUORI dal lock: se restasse
                        ' qui dentro, qualsiasi altro comando (es. STOP inviato da FermaVoce,
                        ' spesso chiamato dal thread UI) resterebbe bloccato in coda sullo
                        ' stesso lock per tutta la durata dell'attesa.
                        eventoAckDaAttendere = hShmEventC2SAck
                    Catch ex As Exception
                        log.ScriviLog("InviaComandoSapiSicuro (SHM): " & ex.Message)
                        sapiInizializzato = False
                        pingRunning = False
                    End Try
                End If
            End SyncLock

            If eventoAckDaAttendere IsNot Nothing Then
                eventoAckDaAttendere.WaitOne(2000)
            End If
        Catch ex As Exception
            log.ScriviLog("InviaComandoSapiSicuro (Lock): " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property SapiVoci As Dictionary(Of String, String)
        Get
            Try
                If _sapiVoci.Count = 0 AndAlso sapiInizializzato Then
                    Dim richiedi As Boolean = False
                    Try
                        InviaComandoSapiSicuro("GET_VOICES")
                        richiedi = True
                    Catch ex As Exception
                        log.ScriviLog("SapiVoci Get (Invio): " & ex.Message)
                    End Try
                    If richiedi Then
                        Dim sw As Stopwatch = Stopwatch.StartNew()
                        While _sapiVoci.Count = 0 AndAlso sw.ElapsedMilliseconds < 1000 AndAlso Not masterCts.IsCancellationRequested
                            Thread.Sleep(20)
                        End While
                    End If
                End If
            Catch ex As Exception
                log.ScriviLog("SapiVoci Get: " & ex.Message)
            End Try
            Return New Dictionary(Of String, String)(_sapiVoci)
        End Get
    End Property

    Public ReadOnly Property SapiAudioDevices As Dictionary(Of String, String)
        Get
            Try
                If _sapiAudioDevices.Count = 0 AndAlso sapiInizializzato Then
                    Dim richiedi As Boolean = False
                    Try
                        InviaComandoSapiSicuro("GET_AUDIO_DEVICES")
                        richiedi = True
                    Catch ex As Exception
                        log.ScriviLog("SapiAudioDevices Get (Invio): " & ex.Message)
                    End Try
                    If richiedi Then
                        Dim sw As Stopwatch = Stopwatch.StartNew()
                        While _sapiAudioDevices.Count = 0 AndAlso sw.ElapsedMilliseconds < 1000 AndAlso Not masterCts.IsCancellationRequested
                            Thread.Sleep(50)
                        End While
                    End If
                End If
            Catch ex As Exception
                log.ScriviLog("SapiAudioDevices Get: " & ex.Message)
            End Try
            Return New Dictionary(Of String, String)(_sapiAudioDevices)
        End Get
    End Property

    Public Shared Function GetInstance() As sintesivocale
        Try
            SyncLock instanceLock
                If instance Is Nothing Then
                    instance = New sintesivocale()
                End If
            End SyncLock
            ' All'avvio del programma il braille cerca da solo la barra collegata.
            Try
                WGBraille.Avvia()
            Catch exBraille As Exception
                logShared.ScriviLog("GetInstance (braille): " & exBraille.Message)
            End Try
            Return instance
        Catch ex As Exception
            logShared.ScriviLog("GetInstance: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public ReadOnly Property CanalePronto As Boolean
        Get
            Try
                Select Case modalitaCorrente
                    Case "Nessun lettore"
                        Return True
                    Case "SAPI"
                        Return sapiInizializzato
                    Case "ScreenReader"
                        Return NvdaCollegato
                    Case Else
                        Return False
                End Select
            Catch ex As Exception
                log.ScriviLog("CanalePronto: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Function AttendiCanalePronto(Optional timeoutMs As Integer = 120000) As Boolean
        Try
            Dim inizio As Long = TickCorrenti()
            While Not masterCts.IsCancellationRequested
                If CanalePronto Then Return True
                If timeoutMs > 0 AndAlso (TickCorrenti() - inizio) >= timeoutMs Then
                    Return False
                End If
                Try
                    _canaleProntoEvento.Wait(100)
                    _canaleProntoEvento.Reset()
                Catch ex As ObjectDisposedException
                    Return False
                End Try
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog("AttendiCanalePronto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SegnalaCanalePronto()
        Try
            SyncLock setupLock
                tentativiFailover.Clear()
            End SyncLock
            _canaleProntoEvento.Set()
        Catch ex As Exception
            log.ScriviLog("SegnalaCanalePronto: " & ex.Message)
        End Try
    End Sub

    Private Sub New()
        Try
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency

            Try
                ' Il lettore di schermo (WGLettore.exe) carica questa libreria solo per parlare: la tastiera di WinGabriel
                ' installa gli hook globali e avvia il centro di controllo, che scarica yt-dlp e ffmpeg nella cartella del lettore.
                If Not String.Equals(Process.GetCurrentProcess().ProcessName, "WGLettore", StringComparison.OrdinalIgnoreCase) Then
                    _tastiera = WGTastiera.GetInstance()
                End If
            Catch ex As Exception
                log.ScriviLog("New (WGTastiera): " & ex.Message)
            End Try

            AvviaWatchdog()
            AvviaMonitorImpostazioni()
            AvviaMonitorDeviceAudio()
            CaricaDizionarioPronuncia()
            VerificaAggiornamenti()

            Try
                Dim modalita As String = AssicuraConfigurazionePredefinita()
                If String.IsNullOrWhiteSpace(modalita) Then
                    modalita = ModalitaLetturaPredefinita
                End If
                ' Modalità ScreenReader (NVDA) neutralizzata in questa versione: viene sempre ignorata.
                If modalita = "ScreenReader" Then
                    modalita = "Nessun lettore"
                End If
                modalitaCorrente = modalita
                _ultimaModalitaMonitorata = modalita
            Catch ex As Exception
                log.ScriviLog("New (LetturaModalita): " & ex.Message)
                modalitaCorrente = ModalitaLetturaPredefinita
                _ultimaModalitaMonitorata = ModalitaLetturaPredefinita
            End Try

            Task.Run(Sub()
                         Try
                             RicaricaConfigurazione()
                         Catch ex As Exception
                             log.ScriviLog("New (RicaricaConfigurazione Task): " & ex.Message)
                         End Try
                     End Sub)

        Catch ex As Exception
            log.ScriviLog("New: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiAvvioConnessioneNvda()
        Try
            AttendiPrimaConnessione()
        Catch ex As Exception
            log.ScriviLog("EseguiAvvioConnessioneNvda: " & ex.Message)
        End Try
    End Sub

    Private Function AttendiPrimaConnessione() As Boolean
        Try
            While Not masterCts.IsCancellationRequested
                If modalitaCorrente <> "ScreenReader" Then
                    Thread.Sleep(100)
                    Continue While
                End If
                If isListening Then Return True
                AvviaConnessioneNvdaSeNecessario()
                If isListening Then Return True
                Thread.Sleep(50)
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog("AttendiPrimaConnessione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub CaricaDizionarioPronuncia()
        Try
            SyncLock dictPronunciaLock
                dictPronuncia.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CaricaDizionarioPronuncia: " & ex.Message)
        End Try
    End Sub

    Private Function ApplicaFiltriDizionario(testo As String) As String
        Try
            SyncLock dictPronunciaLock
                If dictPronuncia.Count = 0 Then Return testo
                Dim sb As New StringBuilder(testo)
                For Each kvp In dictPronuncia
                    sb.Replace(kvp.Key, kvp.Value)
                Next
                Return sb.ToString()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ApplicaFiltriDizionario: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Sub AvviaWatchdog()
        Try
            watchdogRunning = True
            Task.Run(Sub()
                         While watchdogRunning AndAlso Not masterCts.IsCancellationRequested
                             Try
                                 If sapiProcess IsNot Nothing AndAlso Not sapiProcess.HasExited Then
                                     If Not sapiProcess.Responding Then
                                         ChiudiSapi()
                                         EseguiFailover()
                                     End If
                                 End If
                             Catch ex As Exception
                                 log.ScriviLog("AvviaWatchdog (Ciclo): " & ex.Message)
                             End Try
                             Thread.Sleep(3000)
                         End While
                     End Sub, masterCts.Token)
        Catch ex As Exception
            log.ScriviLog("AvviaWatchdog: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaMonitorImpostazioni()
        Try
            If monitorImpostazioniRunning Then Return
            monitorImpostazioniRunning = True
            Task.Run(Sub()
                         While monitorImpostazioniRunning AndAlso Not masterCts.IsCancellationRequested
                             Try
                                 If modalitaCorrente = "SAPI" AndAlso sapiInizializzato AndAlso CanaleSapiScrivibile() Then
                                     ControllaEApplicaImpostazioniSapi()
                                 End If
                             Catch ex As Exception
                                 log.ScriviLog("AvviaMonitorImpostazioni (Ciclo): " & ex.Message)
                             End Try
                             Try
                                 If modalitaCorrente = "SAPI" AndAlso sapiInizializzato Then
                                     RiproduciTestoInAttesa()
                                 End If
                             Catch ex As Exception
                                 log.ScriviLog("AvviaMonitorImpostazioni (TestoInAttesa): " & ex.Message)
                             End Try
                             Try
                                 ControllaCambioModalita()
                             Catch ex As Exception
                                 log.ScriviLog("AvviaMonitorImpostazioni (CambioModalita): " & ex.Message)
                             End Try
                             Try
                                 Thread.Sleep(50)
                             Catch ex As Exception
                                 log.ScriviLog("AvviaMonitorImpostazioni (Sleep): " & ex.Message)
                             End Try
                         End While
                     End Sub, masterCts.Token)
        Catch ex As Exception
            log.ScriviLog("AvviaMonitorImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaCambioModalita()
        Try
            Dim cfg As New WGConfig()
            Dim modalitaLetta As String = Nothing
            Try
                modalitaLetta = cfg.LeggiValore("ModalitaLettura")
            Catch ex As Exception
                log.ScriviLog("ControllaCambioModalita (Lettura): " & ex.Message)
                Return
            End Try

            If String.IsNullOrEmpty(modalitaLetta) Then Return
            If modalitaLetta = _ultimaModalitaMonitorata Then Return
            If modalitaLetta = modalitaCorrente Then
                _ultimaModalitaMonitorata = modalitaLetta
                Return
            End If

            _ultimaModalitaMonitorata = modalitaLetta
            RicaricaConfigurazione()
        Catch ex As Exception
            log.ScriviLog("ControllaCambioModalita: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaEApplicaImpostazioniSapi()
        Try
            Dim cfg As New WGConfig()

            Dim rateNuovo As String = Nothing
            Try
                rateNuovo = cfg.LeggiValore("SapiRate")
            Catch ex As Exception
                log.ScriviLog("ControllaEApplicaImpostazioniSapi (LeggiRate): " & ex.Message)
            End Try

            Dim volumeNuovo As String = Nothing
            Try
                volumeNuovo = cfg.LeggiValore("SapiVolume")
            Catch ex As Exception
                log.ScriviLog("ControllaEApplicaImpostazioniSapi (LeggiVolume): " & ex.Message)
            End Try

            Dim voceNuova As String = Nothing
            Try
                voceNuova = cfg.LeggiValore("SapiVoice")
            Catch ex As Exception
                log.ScriviLog("ControllaEApplicaImpostazioniSapi (LeggiVoce): " & ex.Message)
            End Try

            Dim deviceNuovo As String = Nothing
            Try
                deviceNuovo = cfg.LeggiValore("SapiDevice")
            Catch ex As Exception
                log.ScriviLog("ControllaEApplicaImpostazioniSapi (LeggiDevice): " & ex.Message)
            End Try

            Dim applicaRate As Boolean = False
            Dim applicaVolume As Boolean = False
            Dim applicaVoce As Boolean = False
            Dim applicaDevice As Boolean = False

            SyncLock _lockUltimiApplicati
                If Not String.IsNullOrEmpty(rateNuovo) AndAlso rateNuovo <> _ultimoRateApplicato Then
                    applicaRate = True
                    _ultimoRateApplicato = rateNuovo
                End If
                If Not String.IsNullOrEmpty(volumeNuovo) AndAlso volumeNuovo <> _ultimoVolumeApplicato Then
                    applicaVolume = True
                    _ultimoVolumeApplicato = volumeNuovo
                End If
                If Not String.IsNullOrEmpty(voceNuova) AndAlso voceNuova <> _ultimaVoceApplicata Then
                    applicaVoce = True
                    _ultimaVoceApplicata = voceNuova
                End If
                If Not String.IsNullOrEmpty(deviceNuovo) AndAlso deviceNuovo <> _ultimoDeviceApplicato Then
                    applicaDevice = True
                    _ultimoDeviceApplicato = deviceNuovo
                End If

                ' Il cambio voce in WGSapi può resettare velocità e volume ai valori di
                ' default della nuova voce: quando cambia la voce vanno sempre riapplicati
                ' anche velocità e volume correnti, così restano coerenti con le impostazioni.
                If applicaVoce Then
                    If String.IsNullOrEmpty(rateNuovo) Then rateNuovo = _ultimoRateApplicato
                    If Not String.IsNullOrEmpty(rateNuovo) Then
                        applicaRate = True
                        _ultimoRateApplicato = rateNuovo
                    End If
                    If String.IsNullOrEmpty(volumeNuovo) Then volumeNuovo = _ultimoVolumeApplicato
                    If Not String.IsNullOrEmpty(volumeNuovo) Then
                        applicaVolume = True
                        _ultimoVolumeApplicato = volumeNuovo
                    End If
                End If
            End SyncLock

            If applicaVoce Then
                Try
                    InviaComandoSapiSicuro("SET_VOICE|" & voceNuova)
                Catch ex As Exception
                    log.ScriviLog("ControllaEApplicaImpostazioniSapi (SET_VOICE): " & ex.Message)
                End Try
            End If

            If applicaRate Then
                Try
                    InviaComandoSapiSicuro("SET_RATE|" & rateNuovo)
                Catch ex As Exception
                    log.ScriviLog("ControllaEApplicaImpostazioniSapi (SET_RATE): " & ex.Message)
                End Try
            End If

            If applicaVolume Then
                Try
                    InviaComandoSapiSicuro("SET_VOLUME|" & volumeNuovo)
                Catch ex As Exception
                    log.ScriviLog("ControllaEApplicaImpostazioniSapi (SET_VOLUME): " & ex.Message)
                End Try
            End If

            If applicaDevice Then
                Try
                    InviaComandoSapiSicuro("SET_AUDIO_DEVICE|" & deviceNuovo)
                Catch ex As Exception
                    log.ScriviLog("ControllaEApplicaImpostazioniSapi (SET_AUDIO_DEVICE): " & ex.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("ControllaEApplicaImpostazioniSapi: " & ex.Message)
        End Try
    End Sub

    Private Sub ResetUltimiApplicati()
        Try
            SyncLock _lockUltimiApplicati
                _ultimoRateApplicato = Nothing
                _ultimoVolumeApplicato = Nothing
                _ultimaVoceApplicata = Nothing
                _ultimoDeviceApplicato = Nothing
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ResetUltimiApplicati: " & ex.Message)
        End Try
    End Sub

    Private Sub SincronizzaUltimiApplicati()
        Try
            Dim cfg As New WGConfig()
            Dim rate As String = Nothing
            Dim volume As String = Nothing
            Dim voce As String = Nothing
            Dim device As String = Nothing
            Try
                rate = cfg.LeggiValore("SapiRate")
            Catch ex As Exception
                log.ScriviLog("SincronizzaUltimiApplicati (LeggiRate): " & ex.Message)
            End Try
            Try
                volume = cfg.LeggiValore("SapiVolume")
            Catch ex As Exception
                log.ScriviLog("SincronizzaUltimiApplicati (LeggiVolume): " & ex.Message)
            End Try
            Try
                voce = cfg.LeggiValore("SapiVoice")
            Catch ex As Exception
                log.ScriviLog("SincronizzaUltimiApplicati (LeggiVoce): " & ex.Message)
            End Try
            Try
                device = cfg.LeggiValore("SapiDevice")
            Catch ex As Exception
                log.ScriviLog("SincronizzaUltimiApplicati (LeggiDevice): " & ex.Message)
            End Try

            SyncLock _lockUltimiApplicati
                _ultimoRateApplicato = rate
                _ultimoVolumeApplicato = volume
                _ultimaVoceApplicata = voce
                _ultimoDeviceApplicato = device
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("SincronizzaUltimiApplicati: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaMonitorDeviceAudio()
        Try
            deviceEnumerator = EnumeratoreAudio.Crea()
            notificaDeviceAudio = New NotificaCambioDeviceAudio(Me)
            deviceEnumerator.RegisterEndpointNotificationCallback(notificaDeviceAudio)
        Catch ex As Exception
            log.ScriviLog("AvviaMonitorDeviceAudio: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaMonitorDeviceAudio()
        Try
            If deviceEnumerator IsNot Nothing AndAlso notificaDeviceAudio IsNot Nothing Then
                Try
                    deviceEnumerator.UnregisterEndpointNotificationCallback(notificaDeviceAudio)
                Catch ex As Exception
                    log.ScriviLog("FermaMonitorDeviceAudio (Unregister): " & ex.Message)
                End Try
            End If
            If deviceEnumerator IsNot Nothing Then
                Try
                    deviceEnumerator.Dispose()
                Catch ex As Exception
                    log.ScriviLog("FermaMonitorDeviceAudio (Dispose): " & ex.Message)
                End Try
                deviceEnumerator = Nothing
            End If
            notificaDeviceAudio = Nothing
        Catch ex As Exception
            log.ScriviLog("FermaMonitorDeviceAudio: " & ex.Message)
        End Try
    End Sub

    Private Function TrovaChiaveDeviceSapi(nomeDispositivo As String) As String
        Try
            If String.IsNullOrEmpty(nomeDispositivo) Then Return Nothing
            Dim nomeConfronto As String = nomeDispositivo.Trim().ToLowerInvariant()
            For Each kvp In _sapiAudioDevices
                Try
                    If Not String.IsNullOrEmpty(kvp.Value) Then
                        Dim valoreConfronto As String = kvp.Value.Trim().ToLowerInvariant()
                        If valoreConfronto = nomeConfronto OrElse valoreConfronto.Contains(nomeConfronto) OrElse nomeConfronto.Contains(valoreConfronto) Then
                            Return kvp.Key
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog("TrovaChiaveDeviceSapi (Confronto): " & ex.Message)
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("TrovaChiaveDeviceSapi: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Un dispositivo audio si è staccato (le cuffie Bluetooth spente o fuori portata): se la voce
    ''' usava proprio quello, passa al dispositivo predefinito di Windows, di solito gli altoparlanti,
    ''' invece di restare muta.
    ''' </summary>
    Friend Sub GestisciDispositivoAudioNonDisponibile(deviceId As String)
        Try
            If deviceEnumerator Is Nothing OrElse String.IsNullOrEmpty(deviceId) Then Return
            Dim deviceScelto As String = ""
            Try
                deviceScelto = New WGConfig().LeggiValore("SapiDevice")
            Catch ex As Exception
                log.ScriviLog("GestisciDispositivoAudioNonDisponibile (lettura scelta): " & ex.Message)
            End Try
            ' Chi segue il predefinito ci pensa WGSapi da solo.
            If String.IsNullOrWhiteSpace(deviceScelto) OrElse String.Equals(deviceScelto.Trim(), "PREDEFINITO", StringComparison.OrdinalIgnoreCase) Then Return
            Dim nomeScelto As String = Nothing
            If Not _sapiAudioDevices.TryGetValue(deviceScelto, nomeScelto) OrElse String.IsNullOrEmpty(nomeScelto) Then Return
            Dim nomeStaccato As String = Nothing
            Try
                Using dispositivo As MMDevice = deviceEnumerator.GetDevice(deviceId)
                    If dispositivo.DataFlow <> DataFlow.Render Then Return
                    nomeStaccato = dispositivo.FriendlyName
                End Using
            Catch ex As Exception
                log.ScriviLog("GestisciDispositivoAudioNonDisponibile (GetDevice): " & ex.Message)
            End Try
            If String.IsNullOrEmpty(nomeStaccato) Then Return
            Dim a As String = nomeScelto.Trim().ToLowerInvariant()
            Dim b As String = nomeStaccato.Trim().ToLowerInvariant()
            If Not (a = b OrElse a.Contains(b) OrElse b.Contains(a)) Then Return
            ' Windows sceglie il nuovo predefinito dopo un attimo.
            Thread.Sleep(700)
            Dim predefinito As String = Nothing
            Try
                Using dispositivo As MMDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    predefinito = dispositivo.ID
                End Using
            Catch ex As Exception
                log.ScriviLog("GestisciDispositivoAudioNonDisponibile (predefinito): " & ex.Message)
            End Try
            If String.IsNullOrEmpty(predefinito) Then Return
            log.ScriviLog("Dispositivo audio della voce staccato (" & nomeStaccato & "): passo al predefinito")
            GestisciCambioDeviceAudioPredefinito(DataFlow.Render, Role.Multimedia, predefinito)
        Catch ex As Exception
            log.ScriviLog("GestisciDispositivoAudioNonDisponibile: " & ex.Message)
        End Try
    End Sub

    Friend Sub GestisciCambioDeviceAudioPredefinito(flow As DataFlow, role As Role, defaultDeviceId As String)
        Try
            If flow <> DataFlow.Render Then Return
            If role <> Role.Multimedia AndAlso role <> Role.Console Then Return
            If deviceEnumerator Is Nothing Then Return

            ' Se non e' stato scelto un dispositivo preciso, WGSapi segue gia' da solo
            ' il predefinito di sistema e lo fa in pochi millisecondi. Agganciarsi qui
            ' all'ID del nuovo predefinito lo fisserebbe per sempre: dal collegamento
            ' successivo della cuffia il programma resterebbe sull'altoparlante.
            Try
                Dim cfgSegue As New WGConfig()
                Dim deviceScelto As String = cfgSegue.LeggiValore("SapiDevice")
                If String.IsNullOrWhiteSpace(deviceScelto) OrElse
                   String.Equals(deviceScelto.Trim(), "PREDEFINITO", StringComparison.OrdinalIgnoreCase) Then
                    Return
                End If
            Catch ex As Exception
                log.ScriviLog("GestisciCambioDeviceAudioPredefinito (lettura scelta): " & ex.Message)
            End Try

            SyncLock deviceAudioLock
                Try
                    Dim nomeDispositivo As String = Nothing

                    If Not String.IsNullOrEmpty(defaultDeviceId) Then
                        Try
                            Using dispositivo As MMDevice = deviceEnumerator.GetDevice(defaultDeviceId)
                                nomeDispositivo = dispositivo.FriendlyName
                            End Using
                        Catch ex As Exception
                            log.ScriviLog("GestisciCambioDeviceAudioPredefinito (GetDevice): " & ex.Message)
                        End Try
                    End If

                    If String.IsNullOrEmpty(nomeDispositivo) Then Return
                    If modalitaCorrente <> "SAPI" OrElse Not sapiInizializzato Then Return

                    Dim chiaveTrovata As String = TrovaChiaveDeviceSapi(nomeDispositivo)
                    Dim tentativi As Integer = 0

                    While chiaveTrovata Is Nothing AndAlso tentativi < 20 AndAlso Not masterCts.IsCancellationRequested
                        Try
                            InviaComandoSapiSicuro("GET_AUDIO_DEVICES")
                        Catch ex As Exception
                            log.ScriviLog("GestisciCambioDeviceAudioPredefinito (Richiesta lista): " & ex.Message)
                        End Try
                        Thread.Sleep(100)
                        chiaveTrovata = TrovaChiaveDeviceSapi(nomeDispositivo)
                        tentativi += 1
                    End While

                    If chiaveTrovata Is Nothing Then Return

                    Try
                        Dim cfg As New WGConfig()
                        cfg.SalvaValore("SapiDevice", chiaveTrovata)
                    Catch ex As Exception
                        log.ScriviLog("GestisciCambioDeviceAudioPredefinito (Salvataggio config): " & ex.Message)
                    End Try

                    SyncLock _lockUltimiApplicati
                        _ultimoDeviceApplicato = chiaveTrovata
                    End SyncLock

                    Try
                        InviaComandoSapiSicuro("SET_AUDIO_DEVICE|" & chiaveTrovata)
                    Catch ex As Exception
                        log.ScriviLog("GestisciCambioDeviceAudioPredefinito (Invio): " & ex.Message)
                    End Try
                Catch ex As Exception
                    log.ScriviLog("GestisciCambioDeviceAudioPredefinito (Interno): " & ex.Message)
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("GestisciCambioDeviceAudioPredefinito: " & ex.Message)
        End Try
    End Sub

    Private Sub DispatchOnTestoRicevuto(testo As String)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnTestoRicevuto(testo)))
            Else
                RaiseEvent OnTestoRicevuto(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnTestoRicevuto: " & ex.Message)
            Try
                RaiseEvent OnTestoRicevuto(testo)
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnTestoRicevuto (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub

    ''' <summary>Vero se la voce e' spenta (modalita' "Nessun lettore").</summary>
    Public ReadOnly Property VoceSpenta As Boolean
        Get
            Return modalitaCorrente = "Nessun lettore"
        End Get
    End Property

    Private ReadOnly _annunciLock As New Object()
    Private ReadOnly _codaAnnunci As New Queue(Of Action)()
    Private _annuncioInCorso As Boolean = False
    Private _tickAnnuncio As Long = 0
    Private _timerAnnuncio As System.Threading.Timer

    ' Vero se la lettura va messa in coda perche' un annuncio e' ancora in corso. Se e' un annuncio e non ce n'e' altri in corso,
    ' lo segna come in lettura.
    Private Function AccodaAnnuncio(azione As Action, eAnnuncio As Boolean) As Boolean
        Try
            SyncLock _annunciLock
                Dim adesso As Long = TickCorrenti()
                Dim durataMs As Long = CLng(WGBraille.TempoMessaggio) * 1000L
                If _annuncioInCorso AndAlso adesso - _tickAnnuncio < durataMs Then
                    _codaAnnunci.Enqueue(azione)
                    Return True
                End If
                If Not eAnnuncio Then Return False
                _annuncioInCorso = True
                _tickAnnuncio = adesso
                If _timerAnnuncio IsNot Nothing Then _timerAnnuncio.Dispose()
                _timerAnnuncio = New System.Threading.Timer(AddressOf AnnuncioScaduto, Nothing, durataMs, Timeout.Infinite)
            End SyncLock
            Return False
        Catch ex As Exception
            log.ScriviLog("AccodaAnnuncio: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnuncioScaduto(stato As Object)
        Try
            RilasciaProssimoAnnuncio()
        Catch ex As Exception
            log.ScriviLog("AnnuncioScaduto: " & ex.Message)
        End Try
    End Sub

    ' L'annuncio in corso e' finito (fine lettura o tempo scaduto): se ce n'e' uno in attesa, lo dice adesso.
    Private Sub RilasciaProssimoAnnuncio()
        Try
            Dim inAttesa As Action() = Nothing
            SyncLock _annunciLock
                If Not _annuncioInCorso Then Return
                _annuncioInCorso = False
                If _timerAnnuncio IsNot Nothing Then
                    _timerAnnuncio.Dispose()
                    _timerAnnuncio = Nothing
                End If
                inAttesa = _codaAnnunci.ToArray()
                _codaAnnunci.Clear()
            End SyncLock
            ' In ordine: se uno di loro e' un annuncio, quelli dopo tornano in coda e aspettano il suo turno.
            For Each azione As Action In inAttesa
                azione()
            Next
        Catch ex As Exception
            log.ScriviLog("RilasciaProssimoAnnuncio: " & ex.Message)
        End Try
    End Sub

    Private Sub DispatchOnFineLettura()
        Try
            AzzeraParlato()
            RilasciaProssimoAnnuncio()
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnFineLettura()))
            Else
                RaiseEvent OnFineLettura()
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnFineLettura: " & ex.Message)
            Try
                RaiseEvent OnFineLettura()
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnFineLettura (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub


    Private Sub DispatchOnProgressoMp3(idLavoro As String, percentuale As Integer)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnProgressoMp3(idLavoro, percentuale)))
            Else
                RaiseEvent OnProgressoMp3(idLavoro, percentuale)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnProgressoMp3: " & ex.Message)
            Try
                RaiseEvent OnProgressoMp3(idLavoro, percentuale)
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnProgressoMp3 (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub

    Private Sub DispatchOnFineConversioneMp3(idLavoro As String, esito As Boolean, messaggio As String)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnFineConversioneMp3(idLavoro, esito, messaggio)))
            Else
                RaiseEvent OnFineConversioneMp3(idLavoro, esito, messaggio)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnFineConversioneMp3: " & ex.Message)
            Try
                RaiseEvent OnFineConversioneMp3(idLavoro, esito, messaggio)
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnFineConversioneMp3 (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub

    Private Function PulisciTestoPerComandoMp3(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim ripulito As String = testo.Replace(vbCr, " ").Replace(vbLf, " ").Replace(vbTab, " ")
            ripulito = ripulito.Replace("|", " ")
            Return ripulito.Trim()
        Catch ex As Exception
            log.ScriviLog("PulisciTestoPerComandoMp3: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function AvviaConversioneMp3(idLavoro As String, voce As String, velocita As Integer, volume As Integer, percorsoMp3 As String, pezzi As List(Of String), Optional numeroThread As Integer = 0) As Boolean
        Try
            If _espeakAttivo Then Return AvviaConversioneMp3Espeak(idLavoro, voce, velocita, volume, percorsoMp3, pezzi)
            If String.IsNullOrWhiteSpace(idLavoro) Then
                log.ScriviLog("AvviaConversioneMp3: identificativo lavoro vuoto")
                Return False
            End If
            If String.IsNullOrWhiteSpace(percorsoMp3) Then
                log.ScriviLog("AvviaConversioneMp3: percorso mp3 vuoto")
                Return False
            End If
            If pezzi Is Nothing OrElse pezzi.Count = 0 Then
                log.ScriviLog("AvviaConversioneMp3: nessun testo da convertire")
                Return False
            End If

            If Not AttendiCanalePronto() Then
                log.ScriviLog("AvviaConversioneMp3: canale sapi non pronto")
                Return False
            End If

            Dim velocitaSicura As Integer = velocita
            If velocitaSicura < 0 Then velocitaSicura = 0
            If velocitaSicura > 100 Then velocitaSicura = 100

            Dim volumeSicuro As Integer = volume
            If volumeSicuro < 0 Then volumeSicuro = 0
            If volumeSicuro > 100 Then volumeSicuro = 100

            Dim voceSicura As String = PulisciTestoPerComandoMp3(voce)

            Dim threadRichiesti As Integer = numeroThread
            If threadRichiesti < 0 Then threadRichiesti = 0
            If threadRichiesti > 32 Then threadRichiesti = 32

            InviaComandoSapiSicuro("MP3_START|" & idLavoro & "|" & voceSicura & "|" & velocitaSicura.ToString() & "|" & volumeSicuro.ToString() & "|" & threadRichiesti.ToString() & "|" & percorsoMp3)

            Dim sequenza As Integer = 0
            For Each pezzo As String In pezzi
                Try
                    Dim testoPezzo As String = PulisciTestoPerComandoMp3(pezzo)
                    If Not String.IsNullOrWhiteSpace(testoPezzo) Then
                        InviaComandoSapiSicuro("MP3_TEXT|" & idLavoro & "|" & sequenza.ToString() & "|" & testoPezzo)
                        sequenza += 1
                    End If
                Catch exPezzo As Exception
                    log.ScriviLog("AvviaConversioneMp3 (pezzo " & sequenza.ToString() & "): " & exPezzo.Message)
                End Try
            Next

            If sequenza = 0 Then
                log.ScriviLog("AvviaConversioneMp3: nessun pezzo valido inviato")
                InviaComandoSapiSicuro("MP3_ABORT|" & idLavoro)
                Return False
            End If

            InviaComandoSapiSicuro("MP3_END|" & idLavoro)
            Return True
        Catch ex As Exception
            log.ScriviLog("AvviaConversioneMp3: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function


    Public Sub AnteprimaVoceMp3(voce As String, velocita As Integer, volume As Integer, testo As String)
        Try
            If _espeakAttivo Then
                AnteprimaVoceMp3Espeak(voce, velocita, volume, testo)
                Return
            End If
            If String.IsNullOrWhiteSpace(testo) Then Return
            If Not AttendiCanalePronto(3000) Then
                log.ScriviLog("AnteprimaVoceMp3: canale sapi non pronto")
                Return
            End If

            Dim velocitaSicura As Integer = velocita
            If velocitaSicura < 0 Then velocitaSicura = 0
            If velocitaSicura > 100 Then velocitaSicura = 100

            Dim volumeSicuro As Integer = volume
            If volumeSicuro < 0 Then volumeSicuro = 0
            If volumeSicuro > 100 Then volumeSicuro = 100

            InviaComandoSapiSicuro("MP3_PREVIEW|" & PulisciTestoPerComandoMp3(voce) & "|" & velocitaSicura.ToString() & "|" & volumeSicuro.ToString() & "|" & PulisciTestoPerComandoMp3(testo))
        Catch ex As Exception
            log.ScriviLog("AnteprimaVoceMp3: " & ex.Message)
        End Try
    End Sub

    Public Sub FermaAnteprimaVoceMp3()
        Try
            If _espeakAttivo Then
                FermaAnteprimaVoceMp3Espeak()
                Return
            End If
            InviaComandoSapiSicuro("MP3_PREVIEW_STOP")
        Catch ex As Exception
            log.ScriviLog("FermaAnteprimaVoceMp3: " & ex.Message)
        End Try
    End Sub

    Public Sub AnnullaConversioneMp3(idLavoro As String)
        Try
            If String.IsNullOrWhiteSpace(idLavoro) Then Return
            If AnnullaConversioneMp3Espeak(idLavoro) Then Return
            InviaComandoSapiSicuro("MP3_ABORT|" & idLavoro)
        Catch ex As Exception
            log.ScriviLog("AnnullaConversioneMp3: " & ex.Message)
        End Try
    End Sub

    Private Sub DispatchOnParolaLetta(posizione As Integer, lunghezza As Integer)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnParolaLetta(posizione, lunghezza)))
            Else
                RaiseEvent OnParolaLetta(posizione, lunghezza)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnParolaLetta: " & ex.Message)
            Try
                RaiseEvent OnParolaLetta(posizione, lunghezza)
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnParolaLetta (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub

    Private Sub DispatchOnImpostazioniNvdaSalvate(successo As Boolean)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Sub() RaiseEvent OnImpostazioniNvdaSalvate(successo)))
            Else
                RaiseEvent OnImpostazioniNvdaSalvate(successo)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnImpostazioniNvdaSalvate: " & ex.Message)
            Try
                RaiseEvent OnImpostazioniNvdaSalvate(successo)
            Catch exEvento As Exception
                log.ScriviLog("DispatchOnImpostazioniNvdaSalvate (evento): " & exEvento.Message)
            End Try
        End Try
    End Sub

    Private Sub PassaAScreenReader()
        Try
            ' Modalità ScreenReader (NVDA) neutralizzata in questa versione: nessuna commutazione automatica verso NVDA.
            ' La funzione resta per essere ripristinata in futuro, ma oggi non deve avere alcun effetto.
        Catch ex As Exception
            log.ScriviLog("PassaAScreenReader: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiFailover()
        Try
            AzzeraParlato()
            SyncLock setupLock
                If Not tentativiFailover.Contains(modalitaCorrente) Then
                    tentativiFailover.Add(modalitaCorrente)
                End If

                Dim nuovaModalita As String = ""

                If Not tentativiFailover.Contains("SAPI") Then
                    nuovaModalita = "SAPI"
                Else
                    Return
                End If

                modalitaCorrente = nuovaModalita

                Task.Run(Sub()
                             Try
                                 If modalitaCorrente = "SAPI" Then
                                     ChiudiConnessioneNvda()
                                     If Not sapiInizializzato Then InizializzaSapiInterno()
                                 End If
                             Catch ex As Exception
                                 log.ScriviLog("EseguiFailover (Task): " & ex.Message)
                             End Try
                         End Sub)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("EseguiFailover: " & ex.Message)
        End Try
    End Sub

    Private Sub VerificaAggiornamenti()
        Try
            Task.Run(Sub()
                         Try
                             AggiornaAddonNvda()
                         Catch ex As Exception
                             log.ScriviLog("VerificaAggiornamenti (Task): " & ex.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("VerificaAggiornamenti: " & ex.Message)
        End Try
    End Sub

    Private Async Function ScaricaFile(url As String, destinazione As String, dataLocale As DateTime) As Task(Of Boolean)
        Try
            If Not url.ToLower().StartsWith("https://") Then Return False
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 Or SecurityProtocolType.Tls13
            If Not My.Computer.Network.IsAvailable Then Return False
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(10)
                Dim request As New HttpRequestMessage(HttpMethod.Head, url)
                Dim response = Await client.SendAsync(request, masterCts.Token)
                If response.StatusCode = HttpStatusCode.NotFound Then Return False
                response.EnsureSuccessStatusCode()
                If response.Content.Headers.LastModified.HasValue Then
                    Dim dataRemota As DateTime = response.Content.Headers.LastModified.Value.UtcDateTime
                    If dataRemota > dataLocale Then
                        Dim fileBytes = Await client.GetByteArrayAsync(url)
                        File.WriteAllBytes(destinazione, fileBytes)
                        File.SetLastWriteTimeUtc(destinazione, dataRemota)
                        Return True
                    End If
                End If
            End Using
        Catch ex As Exception
            log.ScriviLog("ScaricaFile: " & ex.Message)
        End Try
        Return False
    End Function

    Private Sub AggiornaAddonNvda()
        Try
            Dim cfg As New WGConfig()
            Dim url As String = cfg.LeggiValore("UrlAddonNvda")
            If String.IsNullOrEmpty(url) Then Return
            Dim appPath As String = AppDomain.CurrentDomain.BaseDirectory
            Dim fileLocale As String = Path.Combine(appPath, "wingabriel.nvda-addon")

            Dim dataLocale As DateTime = DateTime.MinValue
            If File.Exists(fileLocale) Then
                dataLocale = File.GetLastWriteTimeUtc(fileLocale)
            End If

            ScaricaFile(url, fileLocale, dataLocale).GetAwaiter().GetResult()
        Catch ex As Exception
            log.ScriviLog("AggiornaAddonNvda: " & ex.Message)
        End Try
    End Sub

    Public Sub InviaComandoNvda(comando As String)
        Try
            SyncLock tcpWriteLock
                If nvdaWriter IsNot Nothing Then
                    nvdaWriter.WriteLine(comando)
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("InviaComandoNvda: " & ex.Message)
        End Try
    End Sub

    Public Sub NotificaFocusPerso()
        Try
            InviaComandoNvda("FOCUS_LOST")
        Catch ex As Exception
            log.ScriviLog("NotificaFocusPerso: " & ex.Message)
        End Try
    End Sub

    Public Sub NotificaFocusRiacquisito()
        Try
            InviaComandoNvda("FOCUS_GAINED")
        Catch ex As Exception
            log.ScriviLog("NotificaFocusRiacquisito: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaImpostazioniNvda()
        Try
            Dim voce As String = Nothing
            Dim rate As String = Nothing
            Dim volume As String = Nothing

            SyncLock _lockUltimiApplicati
                voce = _ultimoNvdaVoceApplicata
                rate = _ultimoNvdaRateApplicato
                volume = _ultimoNvdaVolumeApplicato
            End SyncLock

            If Not String.IsNullOrEmpty(voce) Then InviaComandoNvda("SET_VOICE:" & voce.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim())
            If Not String.IsNullOrEmpty(rate) Then InviaComandoNvda("SET_RATE:" & rate.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim())
            If Not String.IsNullOrEmpty(volume) Then InviaComandoNvda("SET_VOLUME:" & volume.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim())
        Catch ex As Exception
            log.ScriviLog("ApplicaImpostazioniNvda: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniUltimaVelocitaNvda() As Integer
        Try
            Dim valore As Integer = -1
            SyncLock _lockUltimiApplicati
                If Not String.IsNullOrEmpty(_ultimoNvdaRateApplicato) Then
                    Integer.TryParse(_ultimoNvdaRateApplicato, valore)
                End If
            End SyncLock
            Return valore
        Catch ex As Exception
            log.ScriviLog("OttieniUltimaVelocitaNvda: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function OttieniUltimoVolumeNvda() As Integer
        Try
            Dim valore As Integer = -1
            SyncLock _lockUltimiApplicati
                If Not String.IsNullOrEmpty(_ultimoNvdaVolumeApplicato) Then
                    Integer.TryParse(_ultimoNvdaVolumeApplicato, valore)
                End If
            End SyncLock
            Return valore
        Catch ex As Exception
            log.ScriviLog("OttieniUltimoVolumeNvda: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Sub ImpostaVoceNvda(voce As String, Optional salvaImpostazioni As Boolean = False)
        Try
            If voce Is Nothing Then voce = ""
            Dim valore As String = voce.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()

            SyncLock _lockUltimiApplicati
                _ultimoNvdaVoceApplicata = valore
            End SyncLock

            InviaComandoNvda("SET_VOICE:" & valore)

            If salvaImpostazioni Then
                InviaComandoNvda("SAVE_SETTINGS")
            End If
        Catch ex As Exception
            log.ScriviLog("ImpostaVoceNvda: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaVelocitaNvda(velocita As Integer, Optional salvaImpostazioni As Boolean = False)
        Try
            If velocita < 0 Then velocita = 0
            If velocita > 100 Then velocita = 100

            SyncLock _lockUltimiApplicati
                _ultimoNvdaRateApplicato = velocita.ToString()
            End SyncLock

            InviaComandoNvda("SET_RATE:" & velocita.ToString())

            If salvaImpostazioni Then
                InviaComandoNvda("SAVE_SETTINGS")
            End If
        Catch ex As Exception
            log.ScriviLog("ImpostaVelocitaNvda: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaVolumeNvda(volume As Integer, Optional salvaImpostazioni As Boolean = False)
        Try
            If volume < 0 Then volume = 0
            If volume > 100 Then volume = 100

            SyncLock _lockUltimiApplicati
                _ultimoNvdaVolumeApplicato = volume.ToString()
            End SyncLock

            InviaComandoNvda("SET_VOLUME:" & volume.ToString())

            If salvaImpostazioni Then
                InviaComandoNvda("SAVE_SETTINGS")
            End If
        Catch ex As Exception
            log.ScriviLog("ImpostaVolumeNvda: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniSintesiAttive(Optional timeoutMs As Integer = 1500) As List(Of Sintesi)
        Try
            Select Case modalitaCorrente
                Case "ScreenReader"
                    Return OttieniSintesiNvda(timeoutMs)
                Case "SAPI"
                    Return OttieniSintesiSapi(timeoutMs)
                Case Else
                    Return New List(Of Sintesi)
            End Select
        Catch ex As Exception
            log.ScriviLog("OttieniSintesiAttive: " & ex.Message)
            Return New List(Of Sintesi)
        End Try
    End Function

    Public Function OttieniSintetizzatoriAttivi(Optional timeoutMs As Integer = 1500) As List(Of Sintesi)
        Try
            Select Case modalitaCorrente
                Case "ScreenReader"
                    Return OttieniSintetizzatoriNvda(timeoutMs)
                Case Else
                    Return New List(Of Sintesi)
            End Select
        Catch ex As Exception
            log.ScriviLog("OttieniSintetizzatoriAttivi: " & ex.Message)
            Return New List(Of Sintesi)
        End Try
    End Function

    Public Sub ImpostaSintesiAttiva(sintesi As Sintesi, Optional salvaImpostazioni As Boolean = False)
        Try
            If sintesi Is Nothing Then Return

            Dim tipo As String = sintesi.Tipo
            If String.IsNullOrEmpty(tipo) Then
                Select Case modalitaCorrente
                    Case "ScreenReader"
                        tipo = "NVDA"
                    Case "SAPI"
                        tipo = "SAPI"
                End Select
            End If

            Select Case tipo
                Case "NVDA"
                    ImpostaSintesiNvda(sintesi, salvaImpostazioni)
                Case "SAPI"
                    ImpostaSintesiSapi(sintesi)
                Case Else
            End Select
        Catch ex As Exception
            log.ScriviLog("ImpostaSintesiAttiva: " & ex.Message)
        End Try
    End Sub

    Public Sub SalvaImpostazioniNvda()
        Try
            InviaComandoNvda("SAVE_SETTINGS")
        Catch ex As Exception
            log.ScriviLog("SalvaImpostazioniNvda: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniSintesiNvda(Optional timeoutMs As Integer = 1500) As List(Of Sintesi)
        Dim risultato As New List(Of Sintesi)
        Try
            If modalitaCorrente <> "ScreenReader" Then Return risultato
            If nvdaWriter Is Nothing Then Return risultato

            NvdaVoci.Clear()
            NvdaStato.Clear()

            InviaComandoNvda("GET_VOICES")
            InviaComandoNvda("GET_STATE")

            Dim sw As Stopwatch = Stopwatch.StartNew()
            While (NvdaVoci.Count = 0 OrElse NvdaStato.Count = 0) AndAlso sw.ElapsedMilliseconds < timeoutMs AndAlso Not masterCts.IsCancellationRequested
                Thread.Sleep(50)
            End While

            Dim voceAttiva As String = ""
            NvdaStato.TryGetValue("voice", voceAttiva)

            For Each kvp In NvdaVoci
                risultato.Add(New Sintesi(
                    kvp.Key,
                    kvp.Value,
                    (Not String.IsNullOrEmpty(voceAttiva) AndAlso kvp.Key = voceAttiva),
                    "NVDA"))
            Next
        Catch ex As Exception
            log.ScriviLog("OttieniSintesiNvda: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function OttieniSintetizzatoriNvda(Optional timeoutMs As Integer = 1500) As List(Of Sintesi)
        Dim risultato As New List(Of Sintesi)
        Try
            If modalitaCorrente <> "ScreenReader" Then Return risultato
            If nvdaWriter Is Nothing Then Return risultato

            NvdaSintetizzatori.Clear()
            NvdaStato.Clear()

            InviaComandoNvda("GET_SYNTHS")
            InviaComandoNvda("GET_STATE")

            Dim sw As Stopwatch = Stopwatch.StartNew()
            While (NvdaSintetizzatori.Count = 0 OrElse NvdaStato.Count = 0) AndAlso sw.ElapsedMilliseconds < timeoutMs AndAlso Not masterCts.IsCancellationRequested
                Thread.Sleep(50)
            End While

            Dim synthAttivo As String = ""
            NvdaStato.TryGetValue("synth", synthAttivo)

            For Each kvp In NvdaSintetizzatori
                risultato.Add(New Sintesi(
                    kvp.Key,
                    kvp.Value,
                    (Not String.IsNullOrEmpty(synthAttivo) AndAlso kvp.Key = synthAttivo),
                    "NVDA"))
            Next
        Catch ex As Exception
            log.ScriviLog("OttieniSintetizzatoriNvda: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Sub ImpostaSintesiNvda(sintesi As Sintesi, Optional salvaImpostazioni As Boolean = False)
        Try
            If sintesi Is Nothing Then Return
            ImpostaVoceNvda(sintesi.Id, salvaImpostazioni)
        Catch ex As Exception
            log.ScriviLog("ImpostaSintesiNvda: " & ex.Message)
        End Try
    End Sub

    Public Function OttieniSintesiSapi(Optional timeoutMs As Integer = 1500) As List(Of Sintesi)
        Dim risultato As New List(Of Sintesi)
        Try
            If Not sapiInizializzato Then Return risultato

            If _sapiVociIndicizzate.Count = 0 Then
                SyncLock tcpWriteLock
                    Try
                        InviaComandoSapiSicuro("GET_VOICES")
                    Catch ex As Exception
                        log.ScriviLog("OttieniSintesiSapi (Invio): " & ex.Message)
                    End Try
                End SyncLock

                Dim sw As Stopwatch = Stopwatch.StartNew()
                While _sapiVociIndicizzate.Count = 0 AndAlso sw.ElapsedMilliseconds < timeoutMs AndAlso Not masterCts.IsCancellationRequested
                    Thread.Sleep(50)
                End While
            End If

            Dim cfg As New WGConfig()
            Dim voceAttiva As String = cfg.LeggiValore("SapiVoice")
            If String.IsNullOrEmpty(voceAttiva) Then voceAttiva = "0"
            voceAttiva = voceAttiva.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()

            For Each kvp In _sapiVociIndicizzate
                risultato.Add(New Sintesi(
                    kvp.Key,
                    kvp.Value,
                    (kvp.Key = voceAttiva),
                    "SAPI"))
            Next
        Catch ex As Exception
            log.ScriviLog("OttieniSintesiSapi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Sub ImpostaSintesiSapi(sintesi As Sintesi)
        Try
            If sintesi Is Nothing Then Return
            If Not sapiInizializzato Then Return

            SyncLock tcpWriteLock
                Try
                    InviaComandoSapiSicuro("SET_VOICE|" & sintesi.Id)
                Catch ex As Exception
                    log.ScriviLog("ImpostaSintesiSapi (Invio): " & ex.Message)
                End Try
            End SyncLock

            Try
                Dim cfg As New WGConfig()
                cfg.SalvaValore("SapiVoice", sintesi.Id)
            Catch ex As Exception
                log.ScriviLog("ImpostaSintesiSapi (Salvataggio config locale): " & ex.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("ImpostaSintesiSapi: " & ex.Message)
        End Try
    End Sub

    ''' <summary>La voce SAPI installata (tra quelle di Windows) per la lingua indicata (codice a due lettere, es. "en"),
    ''' o Nothing se non c'e' nessuna voce per quella lingua. Il confronto e' per nome: la stessa voce ha lo stesso nome
    ''' sia qui sia nel server SAPI separato, anche se i due enumerano le voci con indici diversi.</summary>
    Public Function TrovaSintesiSapiPerLingua(codiceLingua As String) As Sintesi
        Try
            If String.IsNullOrWhiteSpace(codiceLingua) Then Return Nothing
            Dim nomeVoce As String = ""
            Using motore As New System.Speech.Synthesis.SpeechSynthesizer()
                For Each v As System.Speech.Synthesis.InstalledVoice In motore.GetInstalledVoices()
                    If Not v.Enabled Then Continue For
                    Try
                        If String.Equals(v.VoiceInfo.Culture.TwoLetterISOLanguageName, codiceLingua, StringComparison.OrdinalIgnoreCase) Then
                            nomeVoce = v.VoiceInfo.Name
                            Exit For
                        End If
                    Catch exVoce As Exception
                        log.ScriviLog("TrovaSintesiSapiPerLingua (voce): " & exVoce.Message)
                    End Try
                Next
            End Using
            If nomeVoce = "" Then Return Nothing
            For Each s As Sintesi In OttieniSintesiSapi()
                If String.Equals(s.Nome, nomeVoce, StringComparison.Ordinal) Then Return s
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("TrovaSintesiSapiPerLingua: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub IncrementaVelocita()
        Try
            ModificaVelocita(5)
        Catch ex As Exception
            log.ScriviLog("IncrementaVelocita: " & ex.Message)
        End Try
    End Sub

    Public Sub DecrementaVelocita()
        Try
            ModificaVelocita(-5)
        Catch ex As Exception
            log.ScriviLog("DecrementaVelocita: " & ex.Message)
        End Try
    End Sub

    Public Sub IncrementaVolume()
        Try
            ModificaVolume(5)
        Catch ex As Exception
            log.ScriviLog("IncrementaVolume: " & ex.Message)
        End Try
    End Sub

    Public Sub DecrementaVolume()
        Try
            ModificaVolume(-5)
        Catch ex As Exception
            log.ScriviLog("DecrementaVolume: " & ex.Message)
        End Try
    End Sub

    Private Sub ModificaVelocita(delta As Integer)
        Try
            Dim valoreMin As Integer = 0
            Dim valoreMax As Integer = 100
            Dim valoreDefault As Integer = 50

            If modalitaCorrente = "SAPI" Then
                Dim cfg As New WGConfig()
                Dim valoreAttualeStr As String = ""
                Try
                    valoreAttualeStr = cfg.LeggiValore("SapiRate")
                Catch ex As Exception
                    log.ScriviLog("ModificaVelocita (Lettura): " & ex.Message)
                End Try

                Dim valoreAttuale As Integer = valoreDefault
                Integer.TryParse(valoreAttualeStr, valoreAttuale)

                Dim nuovoValore As Integer = valoreAttuale + delta
                If nuovoValore < valoreMin Then nuovoValore = valoreMin
                If nuovoValore > valoreMax Then nuovoValore = valoreMax

                If nuovoValore = valoreAttuale Then
                    Return
                End If

                Try
                    cfg.SalvaValore("SapiRate", nuovoValore.ToString())
                Catch ex As Exception
                    log.ScriviLog("ModificaVelocita (Salvataggio): " & ex.Message)
                    Return
                End Try

                Try
                    SyncLock _lockUltimiApplicati
                        _ultimoRateApplicato = nuovoValore.ToString()
                    End SyncLock

                    InviaComandoSapiInCoda("SET_RATE|" & nuovoValore.ToString())
                Catch ex As Exception
                    log.ScriviLog("ModificaVelocita (Applicazione immediata): " & ex.Message)
                End Try

                FermaVoce()
                LeggiTesto("velocità " & nuovoValore.ToString(), False, "Alta", False)
            Else
                Dim valoreAttuale As Integer = valoreDefault
                Dim valoreAttualeStr As String = Nothing

                SyncLock _lockUltimiApplicati
                    valoreAttualeStr = _ultimoNvdaRateApplicato
                End SyncLock

                If Not String.IsNullOrEmpty(valoreAttualeStr) Then
                    Integer.TryParse(valoreAttualeStr, valoreAttuale)
                End If

                Dim nuovoValore As Integer = valoreAttuale + delta
                If nuovoValore < valoreMin Then nuovoValore = valoreMin
                If nuovoValore > valoreMax Then nuovoValore = valoreMax

                If nuovoValore = valoreAttuale Then
                    Return
                End If

                ImpostaVelocitaNvda(nuovoValore, False)
                FermaVoce()
                LeggiTesto("velocità " & nuovoValore.ToString(), False, "Alta", False)
            End If
        Catch ex As Exception
            log.ScriviLog("ModificaVelocita: " & ex.Message)
        End Try
    End Sub

    Private Sub ModificaVolume(delta As Integer)
        Try
            Dim valoreMin As Integer = 0
            Dim valoreMax As Integer = 100
            Dim valoreDefault As Integer = 80

            If modalitaCorrente = "SAPI" Then
                Dim cfg As New WGConfig()
                Dim valoreAttualeStr As String = ""
                Try
                    valoreAttualeStr = cfg.LeggiValore("SapiVolume")
                Catch ex As Exception
                    log.ScriviLog("ModificaVolume (Lettura): " & ex.Message)
                End Try

                Dim valoreAttuale As Integer = valoreDefault
                Integer.TryParse(valoreAttualeStr, valoreAttuale)

                Dim nuovoValore As Integer = valoreAttuale + delta
                If nuovoValore < valoreMin Then nuovoValore = valoreMin
                If nuovoValore > valoreMax Then nuovoValore = valoreMax

                If nuovoValore = valoreAttuale Then
                    Return
                End If

                Try
                    cfg.SalvaValore("SapiVolume", nuovoValore.ToString())
                Catch ex As Exception
                    log.ScriviLog("ModificaVolume (Salvataggio): " & ex.Message)
                    Return
                End Try

                Try
                    SyncLock _lockUltimiApplicati
                        _ultimoVolumeApplicato = nuovoValore.ToString()
                    End SyncLock

                    InviaComandoSapiInCoda("SET_VOLUME|" & nuovoValore.ToString())
                Catch ex As Exception
                    log.ScriviLog("ModificaVolume (Applicazione immediata): " & ex.Message)
                End Try

                FermaVoce()
                LeggiTesto("volume " & nuovoValore.ToString(), False, "Alta", False)
            Else
                Dim valoreAttuale As Integer = valoreDefault
                Dim valoreAttualeStr As String = Nothing

                SyncLock _lockUltimiApplicati
                    valoreAttualeStr = _ultimoNvdaVolumeApplicato
                End SyncLock

                If Not String.IsNullOrEmpty(valoreAttualeStr) Then
                    Integer.TryParse(valoreAttualeStr, valoreAttuale)
                End If

                Dim nuovoValore As Integer = valoreAttuale + delta
                If nuovoValore < valoreMin Then nuovoValore = valoreMin
                If nuovoValore > valoreMax Then nuovoValore = valoreMax

                If nuovoValore = valoreAttuale Then
                    Return
                End If

                ImpostaVolumeNvda(nuovoValore, False)
                FermaVoce()
                LeggiTesto("volume " & nuovoValore.ToString(), False, "Alta", False)
            End If
        Catch ex As Exception
            log.ScriviLog("ModificaVolume: " & ex.Message)
        End Try
    End Sub

    Public Sub ApplicaImpostazioniSapi()
        Try
            Dim cfg As New WGConfig()

            SyncLock tcpWriteLock
                Dim v As String = cfg.LeggiValore("SapiVoice")
                If String.IsNullOrWhiteSpace(v) Then v = SapiVocePredefinita
                v = v.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                If v.Length = 0 Then v = SapiVocePredefinita
                Try
                    InviaComandoSapiSicuro("SET_VOICE|" & v)
                Catch ex As Exception
                    log.ScriviLog("ApplicaImpostazioniSapi (Voce): " & ex.Message)
                End Try

                Dim r As String = cfg.LeggiValore("SapiRate")
                If String.IsNullOrWhiteSpace(r) Then r = SapiRatePredefinito
                r = r.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                Try
                    InviaComandoSapiSicuro("SET_RATE|" & r)
                Catch ex As Exception
                    log.ScriviLog("ApplicaImpostazioniSapi (Velocita): " & ex.Message)
                End Try

                Dim vol As String = cfg.LeggiValore("SapiVolume")
                If String.IsNullOrWhiteSpace(vol) Then vol = SapiVolumePredefinito
                vol = vol.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                Try
                    InviaComandoSapiSicuro("SET_VOLUME|" & vol)
                Catch ex As Exception
                    log.ScriviLog("ApplicaImpostazioniSapi (Volume): " & ex.Message)
                End Try

                Dim dev As String = cfg.LeggiValore("SapiDevice")
                dev = If(dev, "").Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()

                ' Il valore veniva scritto solo dal monitor automatico, mai da una scelta
                ' dell'utente: un identificativo rimasto in configurazione da una sessione
                ' precedente inchioda l'audio a quella scheda e la cuffia non viene mai
                ' usata. Si riporta la configurazione a "segui il predefinito di sistema",
                ' che e' l'unico comportamento che l'utente puo' davvero avere scelto.
                If Not String.IsNullOrWhiteSpace(dev) AndAlso
                   Not String.Equals(dev, "PREDEFINITO", StringComparison.OrdinalIgnoreCase) Then
                    Try
                        cfg.SalvaValore("SapiDevice", "PREDEFINITO")
                    Catch ex As Exception
                        log.ScriviLog("ApplicaImpostazioniSapi (normalizzazione device): " & ex.Message)
                    End Try
                    dev = "PREDEFINITO"
                End If

                If String.IsNullOrWhiteSpace(dev) Then dev = "PREDEFINITO"

                Try
                    InviaComandoSapiSicuro("SET_AUDIO_DEVICE|" & dev)
                Catch ex As Exception
                    log.ScriviLog("ApplicaImpostazioniSapi (Audio Device): " & ex.Message)
                End Try
            End SyncLock

            ' Allinea la cache degli "ultimi valori applicati" usata dal monitor di sfondo
            ' (ControllaEApplicaImpostazioniSapi), altrimenti il monitor rileva un valore
            ' "cambiato" rispetto alla cache non aggiornata e riapplica di nuovo voce/velocità/
            ' volume subito dopo, causando riapplicazioni ridondanti o incoerenti.
            Try
                SincronizzaUltimiApplicati()
            Catch ex As Exception
                log.ScriviLog("ApplicaImpostazioniSapi (SincronizzaUltimiApplicati): " & ex.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("ApplicaImpostazioniSapi: " & ex.Message)
        End Try
    End Sub

    Private Sub MemorizzaTestoInAttesa(testo As String, priorita As String, tracciaParole As Boolean, escludiPunteggiatura As Boolean)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            SyncLock _testoInAttesaLock
                _testoInAttesa = testo
                _prioritaInAttesa = priorita
                _tracciaParoleInAttesa = tracciaParole
                _escludiPunteggiaturaInAttesa = escludiPunteggiatura
                _tickTestoInAttesa = TickCorrenti()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("MemorizzaTestoInAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub ScartaTestoInAttesa()
        Try
            SyncLock _testoInAttesaLock
                _testoInAttesa = ""
                _tickTestoInAttesa = 0
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ScartaTestoInAttesa: " & ex.Message)
        End Try
    End Sub

    ' Durante l'avvio il server SAPI non e' ancora pronto e ogni annuncio verrebbe
    ' perso: viene invece conservato l'ultimo testo richiesto e pronunciato appena
    ' il canale diventa disponibile, cosi' il programma non resta muto per secondi.
    Private Sub RiproduciTestoInAttesa()
        Try
            Dim testo As String = ""
            Dim priorita As String = "Normale"
            Dim tracciaParole As Boolean = False
            Dim escludiPunteggiatura As Boolean = False

            SyncLock _testoInAttesaLock
                If String.IsNullOrWhiteSpace(_testoInAttesa) Then Return
                If _tickTestoInAttesa > 0 AndAlso (TickCorrenti() - _tickTestoInAttesa) > ScadenzaTestoInAttesaMs Then
                    _testoInAttesa = ""
                    _tickTestoInAttesa = 0
                    Return
                End If
                testo = _testoInAttesa
                priorita = _prioritaInAttesa
                tracciaParole = _tracciaParoleInAttesa
                escludiPunteggiatura = _escludiPunteggiaturaInAttesa
                _testoInAttesa = ""
                _tickTestoInAttesa = 0
            End SyncLock

            LeggiTesto(testo, False, priorita, tracciaParole, False, escludiPunteggiatura)
        Catch ex As Exception
            log.ScriviLog("RiproduciTestoInAttesa: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property AnteprimaInCorso As Boolean
        Get
            Try
                SyncLock _anteprimaLock
                    Return _anteprimaAttiva
                End SyncLock
            Catch ex As Exception
                log.ScriviLog("AnteprimaInCorso: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Sub AvviaAnteprimaImpostazioni()
        Try
            Dim modalita As String = modalitaCorrente
            Dim rate As String = ""
            Dim volume As String = ""
            Dim voce As String = ""

            If modalita = "SAPI" Then
                Try
                    Dim cfg As New WGConfig()
                    Try
                        rate = cfg.LeggiValore("SapiRate")
                    Catch ex As Exception
                        log.ScriviLog("AvviaAnteprimaImpostazioni (LeggiRate): " & ex.Message)
                    End Try
                    Try
                        volume = cfg.LeggiValore("SapiVolume")
                    Catch ex As Exception
                        log.ScriviLog("AvviaAnteprimaImpostazioni (LeggiVolume): " & ex.Message)
                    End Try
                    Try
                        voce = cfg.LeggiValore("SapiVoice")
                    Catch ex As Exception
                        log.ScriviLog("AvviaAnteprimaImpostazioni (LeggiVoce): " & ex.Message)
                    End Try
                Catch ex As Exception
                    log.ScriviLog("AvviaAnteprimaImpostazioni (Configurazione): " & ex.Message)
                End Try

                If String.IsNullOrWhiteSpace(rate) Then rate = SapiRatePredefinito
                If String.IsNullOrWhiteSpace(volume) Then volume = SapiVolumePredefinito
                If String.IsNullOrWhiteSpace(voce) Then voce = SapiVocePredefinita
            ElseIf modalita = "ScreenReader" Then
                SyncLock _lockUltimiApplicati
                    rate = _ultimoNvdaRateApplicato
                    volume = _ultimoNvdaVolumeApplicato
                    voce = _ultimoNvdaVoceApplicata
                End SyncLock
            End If

            If rate Is Nothing Then rate = ""
            If volume Is Nothing Then volume = ""
            If voce Is Nothing Then voce = ""

            SyncLock _anteprimaLock
                _anteprimaAttiva = True
                _anteprimaModalita = modalita
                _anteprimaRateOriginale = rate.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                _anteprimaVolumeOriginale = volume.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                _anteprimaVoceOriginale = voce.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                _anteprimaRateModificato = False
                _anteprimaVolumeModificato = False
                _anteprimaVoceModificata = False
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AvviaAnteprimaImpostazioni: " & ex.Message)
        End Try
    End Sub

    Public Sub AnteprimaVelocita(valore As Integer)
        Try
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            Dim modalita As String = ""
            SyncLock _anteprimaLock
                If Not _anteprimaAttiva Then
                    modalita = ""
                Else
                    modalita = _anteprimaModalita
                    _anteprimaRateModificato = True
                End If
            End SyncLock

            If modalita.Length = 0 Then
                AvviaAnteprimaImpostazioni()
                SyncLock _anteprimaLock
                    modalita = _anteprimaModalita
                    _anteprimaRateModificato = True
                End SyncLock
            End If

            If modalita = "SAPI" Then
                InviaComandoSapiInCoda("SET_RATE|" & valore.ToString())
            ElseIf modalita = "ScreenReader" Then
                InviaComandoNvda("SET_RATE:" & valore.ToString())
            End If
        Catch ex As Exception
            log.ScriviLog("AnteprimaVelocita: " & ex.Message)
        End Try
    End Sub

    Public Sub AnteprimaVolume(valore As Integer, Optional annuncia As Boolean = False)
        Try
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            Dim modalita As String = ""
            SyncLock _anteprimaLock
                If Not _anteprimaAttiva Then
                    modalita = ""
                Else
                    modalita = _anteprimaModalita
                    _anteprimaVolumeModificato = True
                End If
            End SyncLock

            If modalita.Length = 0 Then
                AvviaAnteprimaImpostazioni()
                SyncLock _anteprimaLock
                    modalita = _anteprimaModalita
                    _anteprimaVolumeModificato = True
                End SyncLock
            End If

            If modalita = "SAPI" Then
                InviaComandoSapiInCoda("SET_VOLUME|" & valore.ToString())
            ElseIf modalita = "ScreenReader" Then
                InviaComandoNvda("SET_VOLUME:" & valore.ToString())
            End If

            If annuncia AndAlso modalita.Length > 0 Then
                Try
                    FermaVoce()
                    LeggiTesto(valore.ToString() & " per cento", False, "Alta", False)
                Catch exAnnuncio As Exception
                    log.ScriviLog("AnteprimaVolume (annuncio): " & exAnnuncio.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("AnteprimaVolume: " & ex.Message)
        End Try
    End Sub

    Public Sub AnteprimaVoce(idVoce As String)
        Try
            If String.IsNullOrWhiteSpace(idVoce) Then Return
            Dim valore As String = idVoce.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            If valore.Length = 0 Then Return

            Dim modalita As String = ""
            Dim rate As String = ""
            Dim volume As String = ""
            SyncLock _anteprimaLock
                If Not _anteprimaAttiva Then
                    modalita = ""
                Else
                    modalita = _anteprimaModalita
                    _anteprimaVoceModificata = True
                End If
            End SyncLock

            If modalita.Length = 0 Then
                AvviaAnteprimaImpostazioni()
                SyncLock _anteprimaLock
                    modalita = _anteprimaModalita
                    _anteprimaVoceModificata = True
                End SyncLock
            End If

            SyncLock _anteprimaLock
                rate = _anteprimaRateOriginale
                volume = _anteprimaVolumeOriginale
            End SyncLock

            ' Il cambio voce in WGSapi riporta velocità e volume ai default della nuova
            ' voce: vanno sempre riapplicati subito dopo, altrimenti l'anteprima farebbe
            ' sentire la voce con parametri diversi da quelli realmente impostati.
            If modalita = "SAPI" Then
                InviaComandoSapiInCoda("SET_VOICE|" & valore)
                If rate.Length > 0 Then InviaComandoSapiInCoda("SET_RATE|" & rate)
                If volume.Length > 0 Then InviaComandoSapiInCoda("SET_VOLUME|" & volume)
            ElseIf modalita = "ScreenReader" Then
                InviaComandoNvda("SET_VOICE:" & valore)
                If rate.Length > 0 Then InviaComandoNvda("SET_RATE:" & rate)
                If volume.Length > 0 Then InviaComandoNvda("SET_VOLUME:" & volume)
            End If
        Catch ex As Exception
            log.ScriviLog("AnteprimaVoce: " & ex.Message)
        End Try
    End Sub

    Public Sub ConfermaAnteprimaImpostazioni()
        Try
            SyncLock _anteprimaLock
                _anteprimaAttiva = False
                _anteprimaModalita = ""
                _anteprimaRateOriginale = ""
                _anteprimaVolumeOriginale = ""
                _anteprimaVoceOriginale = ""
                _anteprimaRateModificato = False
                _anteprimaVolumeModificato = False
                _anteprimaVoceModificata = False
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("ConfermaAnteprimaImpostazioni: " & ex.Message)
        End Try
    End Sub

    Public Sub AnnullaAnteprimaImpostazioni()
        Try
            Dim modalita As String = ""
            Dim rate As String = ""
            Dim volume As String = ""
            Dim voce As String = ""
            Dim ripristinaRate As Boolean = False
            Dim ripristinaVolume As Boolean = False
            Dim ripristinaVoce As Boolean = False

            SyncLock _anteprimaLock
                If Not _anteprimaAttiva Then Return
                modalita = _anteprimaModalita
                rate = _anteprimaRateOriginale
                volume = _anteprimaVolumeOriginale
                voce = _anteprimaVoceOriginale
                ripristinaRate = _anteprimaRateModificato
                ripristinaVolume = _anteprimaVolumeModificato
                ripristinaVoce = _anteprimaVoceModificata

                _anteprimaAttiva = False
                _anteprimaModalita = ""
                _anteprimaRateOriginale = ""
                _anteprimaVolumeOriginale = ""
                _anteprimaVoceOriginale = ""
                _anteprimaRateModificato = False
                _anteprimaVolumeModificato = False
                _anteprimaVoceModificata = False
            End SyncLock

            If modalita = "SAPI" Then
                If ripristinaVoce AndAlso voce.Length > 0 Then
                    InviaComandoSapiInCoda("SET_VOICE|" & voce)
                End If
                If ripristinaRate AndAlso rate.Length > 0 Then
                    InviaComandoSapiInCoda("SET_RATE|" & rate)
                End If
                If ripristinaVolume AndAlso volume.Length > 0 Then
                    InviaComandoSapiInCoda("SET_VOLUME|" & volume)
                End If
            ElseIf modalita = "ScreenReader" Then
                If ripristinaVoce AndAlso voce.Length > 0 Then
                    InviaComandoNvda("SET_VOICE:" & voce)
                End If
                If ripristinaRate AndAlso rate.Length > 0 Then
                    InviaComandoNvda("SET_RATE:" & rate)
                End If
                If ripristinaVolume AndAlso volume.Length > 0 Then
                    InviaComandoNvda("SET_VOLUME:" & volume)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("AnnullaAnteprimaImpostazioni: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiPingSapi()
        Try
            While pingRunning AndAlso Not masterCts.IsCancellationRequested
                If sapiInizializzato Then
                    Try
                        InviaComandoSapiSicuro("PING")
                    Catch ex As Exception
                        log.ScriviLog("EseguiPingSapi (Invio): " & ex.Message)
                    End Try
                End If
                Thread.Sleep(5000)
            End While
        Catch ex As Exception
            log.ScriviLog("EseguiPingSapi: " & ex.Message)
        End Try
    End Sub

    Private Function ProcessiWGSapiAttivi() As Boolean
        Dim procs() As Process = Nothing
        Try
            procs = Process.GetProcessesByName("WGSapi")
            Return procs.Length > 0
        Catch ex As Exception
            log.ScriviLog("ProcessiWGSapiAttivi: " & ex.Message)
            Return False
        Finally
            If procs IsNot Nothing Then
                For Each p As Process In procs
                    Try
                        p.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ProcessiWGSapiAttivi (Dispose): " & ex.Message)
                    End Try
                Next
            End If
        End Try
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure InformazioniBaseProcesso
        Public StatoUscita As IntPtr
        Public IndirizzoPeb As IntPtr
        Public MascheraAffinita As IntPtr
        Public PrioritaBase As IntPtr
        Public IdProcesso As IntPtr
        Public IdProcessoPadre As IntPtr
    End Structure

    <DllImport("ntdll.dll")>
    Private Shared Function NtQueryInformationProcess(processo As IntPtr, classe As Integer, ByRef informazioni As InformazioniBaseProcesso, dimensione As Integer, ByRef restituiti As Integer) As Integer
    End Function

    ''' <summary>Vero se il processo che ha avviato questo WGSapi non esiste più. Se non si riesce a saperlo, falso: nel dubbio non si chiude.</summary>
    Private Function WGSapiOrfano(sapi As Process) As Boolean
        Try
            Dim informazioni As InformazioniBaseProcesso
            Dim restituiti As Integer = 0
            If NtQueryInformationProcess(sapi.Handle, 0, informazioni, Marshal.SizeOf(GetType(InformazioniBaseProcesso)), restituiti) <> 0 Then Return False
            Dim padre As Integer = informazioni.IdProcessoPadre.ToInt32()
            If padre <= 0 Then Return True
            Try
                Using genitore As Process = Process.GetProcessById(padre)
                    Return genitore.HasExited
                End Using
            Catch ex As ArgumentException
                Return True
            End Try
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub InizializzaSapiInterno()
        Dim lastTcpErr As String = "Nessun tentativo"
        Dim lastShmErr As String = "Nessun tentativo"
        Dim lastPipeErr As String = "Nessun tentativo"
        Dim handshakeErr As String = ""

        Try
            Try
                ' Si chiudono soltanto i WGSapi rimasti orfani (il programma che li aveva avviati non c'è più). Quelli di un altro
                ' programma che sta parlando, per esempio il lettore di schermo che gira accanto a WinGabriel, restano al loro posto.
                Dim processi() As Process = Process.GetProcessesByName("WGSapi")
                If processi.Length > 0 Then
                    Dim chiusi As New List(Of Integer)
                    For Each p As Process In processi
                        Try
                            If WGSapiOrfano(p) Then
                                chiusi.Add(p.Id)
                                p.Kill()
                            End If
                        Catch ex As Exception
                            log.ScriviLog("InizializzaSapiInterno (Kill singolo processo): " & ex.Message)
                        Finally
                            Try
                                p.Dispose()
                            Catch ex As Exception
                                log.ScriviLog("InizializzaSapiInterno (Dispose singolo processo): " & ex.Message)
                            End Try
                        End Try
                    Next
                    Dim attendi As Integer = 0
                    While chiusi.Count > 0 AndAlso attendi < 50
                        Dim ancoraVivo As Boolean = False
                        For Each idChiuso As Integer In chiusi
                            Try
                                Using vivo As Process = Process.GetProcessById(idChiuso)
                                    ancoraVivo = ancoraVivo OrElse Not vivo.HasExited
                                End Using
                            Catch
                            End Try
                        Next
                        If Not ancoraVivo Then Exit While
                        Thread.Sleep(100)
                        attendi += 1
                    End While
                End If
            Catch ex As Exception
                log.ScriviLog("InizializzaSapiInterno (Ricerca processi): " & ex.Message)
            End Try

            If sapiProcess IsNot Nothing AndAlso Not sapiProcess.HasExited Then
                Try
                    sapiProcess.Kill()
                    sapiProcess.Dispose()
                Catch ex As Exception
                    log.ScriviLog("InizializzaSapiInterno (Kill Process): " & ex.Message)
                End Try
                sapiProcess = Nothing
            End If

            Dim exePath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WGSapi.exe")
            If Not File.Exists(exePath) Then
                Dim cfg As New WGConfig()
                If cfg.LeggiValore("ModalitaLettura") = "SAPI" Then
                End If
                PassaAScreenReader()
                Return
            End If

            If sapiProcess Is Nothing OrElse sapiProcess.HasExited Then
                sapiToken = Guid.NewGuid().ToString("N")
                sapiPort = OttieniPortaLiberaSapi()
                Dim startInfo As New ProcessStartInfo()
                startInfo.FileName = exePath
                startInfo.Arguments = sapiToken & " " & sapiPort.ToString()
                startInfo.WindowStyle = ProcessWindowStyle.Hidden
                startInfo.CreateNoWindow = True
                startInfo.UseShellExecute = False
                Try
                    sapiProcess = Process.Start(startInfo)
                Catch ex As Exception
                    log.ScriviLog("InizializzaSapiInterno (Start Process): " & ex.Message)
                End Try
            End If

            Try
                Dim tempRsa As New RSACryptoServiceProvider(2048)
                Dim pubKeyString As String = tempRsa.ToXmlString(False)
                sapiPrivateKeyXml = tempRsa.ToXmlString(True)
                tempRsa.Dispose()

                Dim startTag As String = "<Modulus>"
                Dim endTag As String = "</Modulus>"
                Dim startIdx As Integer = pubKeyString.IndexOf(startTag)
                Dim endIdx As Integer = pubKeyString.IndexOf(endTag)

                If startIdx >= 0 AndAlso endIdx > startIdx Then
                    startIdx += startTag.Length
                    Dim modB64 As String = pubKeyString.Substring(startIdx, endIdx - startIdx)
                    Dim modBytes As Byte() = Convert.FromBase64String(modB64)
                    If modBytes.Length = 257 AndAlso modBytes(0) = 0 Then
                        Dim newMod(255) As Byte
                        Array.Copy(modBytes, 1, newMod, 0, 256)
                        Dim newModB64 As String = Convert.ToBase64String(newMod)
                        pubKeyString = pubKeyString.Substring(0, startIdx - startTag.Length) &
                                       startTag & newModB64 & endTag &
                                       pubKeyString.Substring(endIdx + endTag.Length)
                    End If
                End If

                Dim pubKeyBytes As Byte() = Encoding.UTF8.GetBytes(pubKeyString)
                Dim pubKeyHex As String = CodificaHex(pubKeyBytes)

                Dim tentativi As Integer = 0
                While tentativi < 150 AndAlso Not masterCts.IsCancellationRequested
                    Try
                        If sapiProcess Is Nothing Then Exit While

                        If sapiProcess IsNot Nothing Then
                            Try
                                If sapiProcess.HasExited Then Exit While
                            Catch exProc As Exception
                                log.ScriviLog("InizializzaSapiInterno (Check Exited): " & exProc.Message)
                            End Try
                        End If

                        Dim connesso As Boolean = False
                        sapiTransport = ""

                        Try
                            sapiClient = New TcpClient()
                            sapiClient.NoDelay = True
                            sapiClient.ReceiveTimeout = 10000
                            sapiClient.SendTimeout = 10000
                            sapiClient.Connect(IPAddress.Loopback, sapiPort)
                            sapiStream = sapiClient.GetStream()
                            sapiTransport = "TCP"
                            connesso = True
                            lastTcpErr = ""
                        Catch ex As Exception
                            lastTcpErr = ex.Message
                            If sapiClient IsNot Nothing Then sapiClient.Close()
                            sapiClient = Nothing
                        End Try

                        If Not connesso Then
                            Try
                                sapiShm = MemoryMappedFile.OpenExisting("Local\WGSapi_SHM_" & sapiToken)
                                sapiShmAccessor = sapiShm.CreateViewAccessor(0, 131072)
                                hShmEventC2S = EventWaitHandle.OpenExisting("Local\WGSapi_SHM_C2S_" & sapiToken)
                                hShmEventC2SAck = EventWaitHandle.OpenExisting("Local\WGSapi_SHM_C2S_ACK_" & sapiToken)
                                hShmEventS2C = EventWaitHandle.OpenExisting("Local\WGSapi_SHM_S2C_" & sapiToken)
                                hShmEventS2CAck = EventWaitHandle.OpenExisting("Local\WGSapi_SHM_S2C_ACK_" & sapiToken)
                                sapiTransport = "SHM"
                                connesso = True
                                lastShmErr = ""
                            Catch ex As Exception
                                lastShmErr = ex.Message
                                sapiShm = Nothing
                            End Try
                        End If

                        If Not connesso Then
                            Try
                                sapiPipe = New NamedPipeClientStream(".", "WGSapiPipe_" & sapiToken, PipeDirection.InOut, PipeOptions.Asynchronous)
                                sapiPipe.Connect(100)
                                sapiStream = sapiPipe
                                sapiTransport = "PIPE"
                                connesso = True
                                lastPipeErr = ""
                            Catch ex As Exception
                                lastPipeErr = ex.Message
                                sapiPipe = Nothing
                            End Try
                        End If

                        If connesso Then
                            If sapiTransport = "TCP" OrElse sapiTransport = "PIPE" Then
                                sapiWriter = New StreamWriter(sapiStream, New System.Text.UTF8Encoding(False))
                                sapiWriter.AutoFlush = True
                                sapiReader = New StreamReader(sapiStream, New System.Text.UTF8Encoding(False))
                            End If

                            InviaComandoSapiSicuro("AUTH|" & sapiToken & "|" & pubKeyHex)

                            sapiInAscolto = True
                            sapiThread = New Thread(AddressOf AscoltaSapi)
                            sapiThread.IsBackground = True
                            sapiThread.Start()

                            Dim tentativiHandshake As Integer = 0
                            While Not sapiIsEncrypted AndAlso tentativiHandshake < 400 AndAlso Not masterCts.IsCancellationRequested
                                If sapiProcess Is Nothing OrElse sapiProcess.HasExited Then
                                    handshakeErr = "Il processo WGSapi si è chiuso improvvisamente durante l'attesa delle chiavi"
                                    Exit While
                                End If
                                Thread.Sleep(50)
                                tentativiHandshake += 1
                            End While

                            If sapiIsEncrypted Then
                                pingRunning = True
                                pingThread = New Thread(AddressOf EseguiPingSapi)
                                pingThread.IsBackground = True
                                pingThread.Start()

                                sapiInizializzato = True
                                SegnalaCanalePronto()
                                InviaComandoSapiSicuro("GET_VOICES")
                                ApplicaImpostazioniSapi()
                                SincronizzaUltimiApplicati()
                                RiproduciTestoInAttesa()
                                Exit While
                            Else
                                If String.IsNullOrEmpty(handshakeErr) Then
                                    handshakeErr = "Nessuna risposta dal server ricevuta entro il timeout di handshake"
                                End If
                                ChiudiSapi()
                                connesso = False
                                Exit While
                            End If
                        End If

                        If Not connesso Then
                            Thread.Sleep(50)
                            tentativi += 1
                        End If
                    Catch ex As Exception
                        log.ScriviLog("InizializzaSapiInterno (Loop tentativo): " & ex.Message)
                        Thread.Sleep(50)
                        tentativi += 1
                    End Try
                End While
            Catch ex As Exception
                log.ScriviLog("InizializzaSapiInterno (Preparazione chiavi): " & ex.Message)
            End Try

            If Not sapiInizializzato Then
                Dim cfg As New WGConfig()
                If cfg.LeggiValore("ModalitaLettura") = "SAPI" Then
                End If
                PassaAScreenReader()
            End If
        Catch ex As Exception
            log.ScriviLog("InizializzaSapiInterno: " & ex.Message)
            PassaAScreenReader()
        End Try
    End Sub

    Private Function ConnessioneSapiInterrotta(ex As Exception) As Boolean
        Try
            If ex Is Nothing Then Return False
            If TypeOf ex Is IOException Then Return True
            If TypeOf ex Is ObjectDisposedException Then Return True
            If TypeOf ex Is SocketException Then Return True
            Dim messaggio As String = If(ex.Message, "")
            If messaggio.IndexOf("interrotta", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("forzatamente", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If messaggio.IndexOf("forzata", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Return False
        Catch exInterno As Exception
            log.ScriviLog("ConnessioneSapiInterrotta: " & exInterno.Message)
            Return False
        End Try
    End Function

    Private Sub AscoltaSapi()
        Try
            While sapiInAscolto AndAlso Not masterCts.IsCancellationRequested
                Dim line As String = Nothing

                If sapiTransport = "TCP" OrElse sapiTransport = "PIPE" Then
                    If sapiReader Is Nothing Then Exit While
                    Try
                        line = sapiReader.ReadLine()
                    Catch ex As Exception
                        If Not ConnessioneSapiInterrotta(ex) Then
                            log.ScriviLog("AscoltaSapi (ReadLine TCP/PIPE): " & ex.Message)
                        End If
                        line = Nothing
                    End Try
                ElseIf sapiTransport = "SHM" Then
                    Try
                        If hShmEventS2C IsNot Nothing AndAlso hShmEventS2C.WaitOne(1000) Then
                            If sapiShmAccessor Is Nothing Then
                                line = Nothing
                            Else
                                Dim len As Integer = sapiShmAccessor.ReadInt32(65536)
                                If len > 0 AndAlso len <= 65532 Then
                                    Dim buf(len - 1) As Byte
                                    sapiShmAccessor.ReadArray(65540, buf, 0, len)
                                    line = Encoding.UTF8.GetString(buf)
                                    If line.EndsWith(vbLf) Then line = line.Substring(0, line.Length - 1)
                                    If line.EndsWith(vbCr) Then line = line.Substring(0, line.Length - 1)
                                Else
                                    ' Un frame vuoto o con lunghezza illeggibile non è la chiusura del canale:
                                    ' trattarlo come disconnessione faceva passare la voce al lettore di schermo.
                                    If hShmEventS2CAck IsNot Nothing Then hShmEventS2CAck.Set()
                                    Continue While
                                End If
                            End If
                            If hShmEventS2CAck IsNot Nothing Then hShmEventS2CAck.Set()
                        Else
                            Continue While
                        End If
                    Catch ex As Exception
                        log.ScriviLog("AscoltaSapi (Read SHM): " & ex.Message)
                        line = Nothing
                    End Try
                Else
                    Exit While
                End If

                If line Is Nothing Then
                    If Not sapiInAscolto OrElse masterCts.IsCancellationRequested Then
                        Exit While
                    End If
                    sapiInizializzato = False
                    pingRunning = False
                    PassaAScreenReader()
                    Exit While
                End If

                If line.StartsWith("WGSAPI_SHUTDOWN|") OrElse line.StartsWith("WGSAPI_ERROR|") Then
                    sapiInizializzato = False
                    pingRunning = False
                    PassaAScreenReader()
                    Exit While
                End If

                If line.StartsWith("PONG") Then
                    SyncLock sapiSyncLock
                        sapiUltimoSegnaleTick = TickCorrenti()
                    End SyncLock
                    Dim data As String() = line.Split("|"c)
                    If data.Length >= 2 Then
                        Dim memoriaRam As Long = 0
                        If Long.TryParse(data(1), memoriaRam) Then
                            If memoriaRam > 314572800L Then
                                conteggioRamCritica += 1
                                If conteggioRamCritica >= 2 Then
                                    sapiInizializzato = False
                                    pingRunning = False
                                    PassaAScreenReader()
                                    Exit While
                                End If
                            Else
                                conteggioRamCritica = 0
                            End If
                        End If
                    End If
                End If

                If line.StartsWith("KEY|") Then
                    Dim parts As String() = line.Split("|"c)
                    If parts.Length = 3 Then
                        Try
                            sapiAes = Aes.Create()
                            sapiAes.Mode = CipherMode.CBC
                            sapiAes.Padding = PaddingMode.PKCS7

                            Using localRsa As New RSACryptoServiceProvider(2048)
                                localRsa.FromXmlString(sapiPrivateKeyXml)

                                Dim encKey As Byte() = DecodificaHex(parts(1))
                                Dim decKey As Byte()
                                Try
                                    decKey = localRsa.Decrypt(encKey, False)
                                Catch exDec1 As Exception
                                    Array.Reverse(encKey)
                                    decKey = localRsa.Decrypt(encKey, False)
                                End Try
                                sapiAes.Key = decKey

                                Dim encIv As Byte() = DecodificaHex(parts(2))
                                Dim decIv As Byte()
                                Try
                                    decIv = localRsa.Decrypt(encIv, False)
                                Catch exDec2 As Exception
                                    Array.Reverse(encIv)
                                    decIv = localRsa.Decrypt(encIv, False)
                                End Try
                                sapiAes.IV = decIv
                            End Using

                            sapiIsEncrypted = True
                        Catch ex As Exception
                            log.ScriviLog("AscoltaSapi (Gestione KEY): " & ex.Message)
                        End Try
                    End If
                    Continue While
                End If

                If sapiIsEncrypted AndAlso sapiAes IsNot Nothing AndAlso Not line.StartsWith("AUTH|") Then
                    Try
                        Dim cipher As Byte() = DecodificaHex(line)
                        Using decryptor As ICryptoTransform = sapiAes.CreateDecryptor()
                            Dim plain As Byte() = decryptor.TransformFinalBlock(cipher, 0, cipher.Length)
                            line = Encoding.UTF8.GetString(plain)
                        End Using
                    Catch ex As Exception
                        log.ScriviLog("AscoltaSapi (Decrypt): " & ex.Message)
                        Continue While
                    End Try
                End If

                If line.StartsWith("VOICES|") Then
                    Dim voiceData As String = line.Substring(7)
                    Dim voiceArray As String() = voiceData.Split(New Char() {"|"c}, StringSplitOptions.RemoveEmptyEntries)
                    _sapiVoci.Clear()
                    _sapiVociIndicizzate.Clear()
                    For i As Integer = 0 To voiceArray.Length - 1
                        _sapiVoci(voiceArray(i)) = voiceArray(i)
                        _sapiVociIndicizzate(i.ToString()) = voiceArray(i)
                    Next
                    Try
                        ApplicaVocePredefinitaSeNecessario()
                    Catch ex As Exception
                        log.ScriviLog("AscoltaSapi (VocePredefinita): " & ex.Message)
                    End Try
                ElseIf line.StartsWith("AUDIO_DEVICES|") Then
                    Dim devData As String = line.Substring(14)
                    Dim devArray As String() = devData.Split(New Char() {"|"c}, StringSplitOptions.RemoveEmptyEntries)
                    _sapiAudioDevices.Clear()
                    For Each dev In devArray
                        Dim parts As String() = dev.Split("="c)
                        If parts.Length >= 2 Then
                            _sapiAudioDevices(parts(0)) = parts(1)
                        End If
                    Next
                ElseIf line.StartsWith("WORD|") Then
                    Dim data As String() = line.Split("|"c)
                    If data.Length >= 3 Then
                        Dim pos As Integer = 0
                        Dim length As Integer = 0
                        Integer.TryParse(data(1), pos)
                        Integer.TryParse(data(2), length)
                        SyncLock sapiSyncLock
                            sapiUltimoSegnaleTick = TickCorrenti()
                        End SyncLock
                        DispatchOnParolaLetta(pos, length)
                    End If
                ElseIf line.StartsWith("DONE|") Then
                    Dim data As String() = line.Split("|"c)
                    If data.Length >= 2 Then
                        Dim mId As Integer = 0
                        Integer.TryParse(data(1), mId)
                        SyncLock sapiSyncLock
                            sapiDoneMsgId = mId
                            sapiUltimoSegnaleTick = TickCorrenti()
                        End SyncLock
                    End If
                ElseIf line.StartsWith("MP3_PROGRESS|") Then
                    Dim data As String() = line.Split("|"c)
                    If data.Length >= 3 Then
                        Dim percentuale As Integer = 0
                        Integer.TryParse(data(2), percentuale)
                        SyncLock sapiSyncLock
                            sapiUltimoSegnaleTick = TickCorrenti()
                        End SyncLock
                        DispatchOnProgressoMp3(data(1), percentuale)
                    End If
                ElseIf line.StartsWith("MP3_DONE|") Then
                    Dim data As String() = line.Split("|"c)
                    If data.Length >= 3 Then
                        Dim esito As Boolean = (data(2) = "1")
                        Dim messaggio As String = ""
                        If data.Length >= 4 Then messaggio = data(3)
                        SyncLock sapiSyncLock
                            sapiUltimoSegnaleTick = TickCorrenti()
                        End SyncLock
                        DispatchOnFineConversioneMp3(data(1), esito, messaggio)
                    End If
                ElseIf line.StartsWith("ALIVE|") Then
                    SyncLock sapiSyncLock
                        sapiUltimoSegnaleTick = TickCorrenti()
                    End SyncLock
                End If
            End While
        Catch ex As Exception
            If sapiInAscolto AndAlso Not masterCts.IsCancellationRequested Then
                If Not ConnessioneSapiInterrotta(ex) Then
                    log.ScriviLog("AscoltaSapi: " & ex.Message)
                End If
                sapiInizializzato = False
                pingRunning = False
                PassaAScreenReader()
            End If
        End Try
    End Sub

    Private Sub InizializzaSapi()
        SyncLock setupLock
            Try
                If sapiInizializzato Then Return
                ' Se la voce scelta è eSpeak parte il suo motore, senza il programma WGSapi.exe.
                If ProvaAvviaEspeak() Then Return
                InizializzaSapiInterno()
            Catch ex As Exception
                log.ScriviLog("InizializzaSapi: " & ex.Message)
                ChiudiSapi()
                EseguiFailover()
            End Try
        End SyncLock
    End Sub

    Private Sub ChiudiSapi()
        ChiudiEspeak()
        Try
            pingRunning = False
            sapiInAscolto = False
            ResetUltimiApplicati()
        Catch ex As Exception
            log.ScriviLog("ChiudiSapi (preparazione): " & ex.Message)
        End Try
        SyncLock setupLock
            Try
                If sapiWriter IsNot Nothing Then
                    Try
                        InviaComandoSapiSicuro("EXIT")
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Exit Command): " & ex.Message)
                    End Try
                    Try
                        sapiWriter.Close()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Writer): " & ex.Message)
                    End Try
                End If
                If sapiReader IsNot Nothing Then
                    Try
                        sapiReader.Close()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Reader): " & ex.Message)
                    End Try
                End If
                If sapiStream IsNot Nothing Then
                    Try
                        sapiStream.Close()
                        sapiStream.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Stream): " & ex.Message)
                    End Try
                End If
                If sapiClient IsNot Nothing Then
                    Try
                        sapiClient.Close()
                        sapiClient.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Client): " & ex.Message)
                    End Try
                End If

                If sapiPipe IsNot Nothing Then
                    Try
                        sapiPipe.Close()
                        sapiPipe.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Pipe): " & ex.Message)
                    End Try
                End If
                If sapiShmAccessor IsNot Nothing Then
                    Try
                        sapiShmAccessor.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (ShmAccessor): " & ex.Message)
                    End Try
                End If
                If sapiShm IsNot Nothing Then
                    Try
                        sapiShm.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Shm): " & ex.Message)
                    End Try
                End If
                If hShmEventC2S IsNot Nothing Then hShmEventC2S.Dispose()
                If hShmEventC2SAck IsNot Nothing Then hShmEventC2SAck.Dispose()
                If hShmEventS2C IsNot Nothing Then hShmEventS2C.Dispose()
                If hShmEventS2CAck IsNot Nothing Then hShmEventS2CAck.Dispose()

                sapiPipe = Nothing
                sapiShm = Nothing
                sapiShmAccessor = Nothing
                hShmEventC2S = Nothing
                hShmEventC2SAck = Nothing
                hShmEventS2C = Nothing
                hShmEventS2CAck = Nothing
                sapiTransport = ""

                sapiWriter = Nothing
                sapiReader = Nothing
                sapiStream = Nothing
                sapiClient = Nothing

                sapiIsEncrypted = False
                If sapiAes IsNot Nothing Then
                    sapiAes.Dispose()
                    sapiAes = Nothing
                End If

                If sapiProcess IsNot Nothing Then
                    Try
                        If Not sapiProcess.HasExited Then
                            sapiProcess.Kill()
                        End If
                        sapiProcess.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiSapi (Process): " & ex.Message)
                    End Try
                    sapiProcess = Nothing
                End If

                sapiInizializzato = False
            Catch ex As Exception
                log.ScriviLog("ChiudiSapi: " & ex.Message)
            End Try
        End SyncLock
    End Sub

    Private Sub ChiudiConnessioneNvda()
        Try
            SyncLock serverLock
                isListening = False
                If nvdaPipeClient IsNot Nothing Then
                    Try
                        nvdaPipeClient.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("ChiudiConnessioneNvda (Pipe): " & ex.Message)
                    End Try
                    nvdaPipeClient = Nothing
                End If
            End SyncLock
            If nvdaWriter IsNot Nothing Then
                Try
                    nvdaWriter.Close()
                Catch ex As Exception
                    log.ScriviLog("ChiudiConnessioneNvda (Writer): " & ex.Message)
                End Try
                nvdaWriter = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("ChiudiConnessioneNvda: " & ex.Message)
        End Try
    End Sub

    Private _ultimoTentativoChiusuraNvda As Long = 0

    Private Function PercorsoNvdaExe() As String
        Try
            Dim percorso As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA", "nvda.exe")
            If Not File.Exists(percorso) Then
                percorso = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA", "nvda.exe")
            End If
            If File.Exists(percorso) Then Return percorso
            Return ""
        Catch ex As Exception
            log.ScriviLog("PercorsoNvdaExe: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ChiudiProcessoNvda()
        Try
            If Not NvdaInEsecuzione() Then Return

            Dim nvdaExe As String = PercorsoNvdaExe()
            If Not String.IsNullOrEmpty(nvdaExe) Then
                Try
                    Dim quitInfo As New ProcessStartInfo()
                    quitInfo.FileName = nvdaExe
                    quitInfo.Arguments = "-q"
                    quitInfo.WindowStyle = ProcessWindowStyle.Hidden
                    ' nvda.exe ha uiAccess="true" nel manifesto: con CreateProcess dà sempre errore 740 (privilegi elevati), con la shell parte
                    quitInfo.UseShellExecute = True
                    Using quitProc As Process = Process.Start(quitInfo)
                    End Using
                Catch ex As Exception
                End Try

                Dim attese As Integer = 0
                While NvdaInEsecuzione() AndAlso attese < 50 AndAlso Not masterCts.IsCancellationRequested
                    Thread.Sleep(100)
                    attese += 1
                End While
            End If

            If Not NvdaInEsecuzione() Then Return

            For Each p As Process In Process.GetProcessesByName("nvda")
                Try
                    p.Kill()
                    p.WaitForExit(3000)
                Catch ex As Exception
                Finally
                    Try
                        p.Dispose()
                    Catch
                    End Try
                End Try
            Next
        Catch ex As Exception
        End Try
    End Sub

    Public Function OttieniListaVoci() As String()
        Try
            Return New String() {}
        Catch ex As Exception
            log.ScriviLog("OttieniListaVoci: " & ex.Message)
            Return New String() {}
        End Try
    End Function

    Private Sub ScriviValoreConfigSeAssente(cfg As WGConfig, chiave As String, valore As String)
        Try
            If cfg Is Nothing Then Return
            If String.IsNullOrWhiteSpace(chiave) Then Return

            Dim attuale As String = Nothing
            Try
                attuale = cfg.LeggiValore(chiave)
            Catch ex As Exception
                log.ScriviLog("ScriviValoreConfigSeAssente (Lettura " & chiave & "): " & ex.Message)
            End Try

            If Not String.IsNullOrWhiteSpace(attuale) Then Return

            Try
                cfg.AggiungiValore(chiave, valore)
            Catch ex As Exception
                log.ScriviLog("ScriviValoreConfigSeAssente (AggiungiValore " & chiave & "): " & ex.Message)
                Try
                    cfg.SalvaValore(chiave, valore)
                Catch exSalva As Exception
                    log.ScriviLog("ScriviValoreConfigSeAssente (SalvaValore " & chiave & "): " & exSalva.Message)
                End Try
            End Try
        Catch ex As Exception
            log.ScriviLog("ScriviValoreConfigSeAssente: " & ex.Message)
        End Try
    End Sub

    Private Function AssicuraConfigurazionePredefinita() As String
        Dim modalita As String = ModalitaLetturaPredefinita
        Try
            Dim cfg As New WGConfig()
            Dim modalitaLetta As String = Nothing

            Try
                modalitaLetta = cfg.LeggiValore("ModalitaLettura")
            Catch ex As Exception
                log.ScriviLog("AssicuraConfigurazionePredefinita (Lettura): " & ex.Message)
            End Try

            If String.IsNullOrWhiteSpace(modalitaLetta) Then
                Try
                    Thread.Sleep(100)
                Catch ex As Exception
                    log.ScriviLog("AssicuraConfigurazionePredefinita (Attesa): " & ex.Message)
                End Try
                Try
                    cfg = New WGConfig()
                    modalitaLetta = cfg.LeggiValore("ModalitaLettura")
                Catch ex As Exception
                    log.ScriviLog("AssicuraConfigurazionePredefinita (Rilettura): " & ex.Message)
                End Try
            End If

            If Not String.IsNullOrWhiteSpace(modalitaLetta) Then
                Return modalitaLetta.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            End If

            Interlocked.Exchange(_configurazioneCreataOra, 1)

            Try
                cfg.AggiungiValore("ModalitaLettura", ModalitaLetturaPredefinita)
            Catch ex As Exception
                log.ScriviLog("AssicuraConfigurazionePredefinita (AggiungiModalita): " & ex.Message)
                Try
                    cfg.SalvaValore("ModalitaLettura", ModalitaLetturaPredefinita)
                Catch exSalva As Exception
                    log.ScriviLog("AssicuraConfigurazionePredefinita (SalvaModalita): " & exSalva.Message)
                End Try
            End Try

            ScriviValoreConfigSeAssente(cfg, "SapiRate", SapiRatePredefinito)
            ScriviValoreConfigSeAssente(cfg, "SapiVolume", SapiVolumePredefinito)
            ScriviValoreConfigSeAssente(cfg, "SapiVoice", SapiVocePredefinita)


            modalita = ModalitaLetturaPredefinita
        Catch ex As Exception
            log.ScriviLog("AssicuraConfigurazionePredefinita: " & ex.Message)
            modalita = ModalitaLetturaPredefinita
        End Try
        Return modalita
    End Function

    Private Function ConfrontaChiaviVoce(x As String, y As String) As Integer
        Try
            Dim ix As Integer = 0
            Dim iy As Integer = 0
            Dim okX As Boolean = Integer.TryParse(x, ix)
            Dim okY As Boolean = Integer.TryParse(y, iy)
            If okX AndAlso okY Then Return ix.CompareTo(iy)
            If okX Then Return -1
            If okY Then Return 1
            Return String.Compare(x, y, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("ConfrontaChiaviVoce: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function VoceCodeFactory() As Boolean
        Try
            Dim chiave As String = VoceConfigurata()
            If String.IsNullOrEmpty(chiave) Then
                SyncLock _lockUltimiApplicati
                    chiave = _ultimaVoceApplicata
                End SyncLock
            End If
            If String.IsNullOrEmpty(chiave) Then chiave = "0"
            If String.Equals(chiave, _chiaveVoceStaccata, StringComparison.Ordinal) Then Return _voceStaccata
            Dim nomeVoce As String = Nothing
            If Not _sapiVociIndicizzate.TryGetValue(chiave, nomeVoce) Then
                If _sapiVociIndicizzate.Count = 0 Then Return False
                nomeVoce = chiave
            End If
            _voceStaccata = NomeCodeFactory(nomeVoce)
            _chiaveVoceStaccata = chiave
            Return _voceStaccata
        Catch ex As Exception
            log.ScriviLog("VoceCodeFactory: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function UnisciRighe(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.IndexOf(ControlChars.Lf) < 0 Then Return If(testo, "")
            Dim righe As String() = testo.Split(ControlChars.Lf)
            Dim risultato As New StringBuilder(testo.Length + righe.Length * 2)
            For Each riga As String In righe
                Dim pulita As String = riga.Trim()
                If pulita.Length = 0 Then Continue For
                If risultato.Length > 0 Then
                    Dim ultimo As Char = risultato(risultato.Length - 1)
                    If ".,;:!?".IndexOf(ultimo) >= 0 Then
                        risultato.Append(" "c)
                    Else
                        risultato.Append(". ")
                    End If
                End If
                risultato.Append(pulita)
            Next
            Return risultato.ToString()
        Catch ex As Exception
            log.ScriviLog("UnisciRighe: " & ex.Message)
            Return If(testo, "").Replace(ControlChars.Lf, " "c)
        End Try
    End Function

    Private Function LeggiPuntiNeiNomi(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.IndexOf("."c) < 0 Then Return If(testo, "")
            Return _puntoNelNome.Replace(testo, " punto ")
        Catch ex As Exception
            log.ScriviLog("LeggiPuntiNeiNomi: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Private Function VoceConfigurata() As String
        Try
            Dim adesso As Long = TickCorrenti()
            If adesso - _tickVoceConfigurata < 2000 AndAlso _tickVoceConfigurata <> 0 Then Return _voceConfigurata
            _tickVoceConfigurata = adesso
            Dim valore As String = New WGConfig().LeggiValore("SapiVoice")
            If valore Is Nothing Then valore = ""
            _voceConfigurata = valore.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            Return _voceConfigurata
        Catch ex As Exception
            log.ScriviLog("VoceConfigurata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function NomeCodeFactory(nomeVoce As String) As Boolean
        If String.IsNullOrEmpty(nomeVoce) Then Return False
        Dim compatto As String = nomeVoce.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "")
        Return compatto.Contains("codefactory") OrElse compatto.Contains("eloquence")
    End Function

    Private Function SeparaParoleComposte(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim risultato As New StringBuilder(testo.Length + 16)
            risultato.Append(testo(0))
            For indice As Integer = 1 To testo.Length - 1
                If ConfineParolaComposta(testo, indice) Then risultato.Append(" "c)
                risultato.Append(testo(indice))
            Next
            Return risultato.ToString()
        Catch ex As Exception
            log.ScriviLog("SeparaParoleComposte: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function ConfineParolaComposta(testo As String, indice As Integer) As Boolean
        Dim precedente As Char = testo(indice - 1)
        Dim corrente As Char = testo(indice)
        If Char.IsLower(precedente) AndAlso Char.IsUpper(corrente) Then Return True
        If Char.IsUpper(precedente) AndAlso Char.IsUpper(corrente) Then
            If indice + 2 < testo.Length AndAlso Char.IsLower(testo(indice + 1)) AndAlso Char.IsLower(testo(indice + 2)) Then Return True
            Return False
        End If
        If Char.IsLetter(precedente) AndAlso Char.IsDigit(corrente) Then Return ParolaMista(testo, indice)
        If Char.IsDigit(precedente) AndAlso Char.IsUpper(corrente) Then Return ParolaMista(testo, indice)
        Return False
    End Function

    Private Shared Function ParolaMista(testo As String, indice As Integer) As Boolean
        Dim inizio As Integer = indice
        While inizio > 0 AndAlso Char.IsLetterOrDigit(testo(inizio - 1))
            inizio -= 1
        End While
        Dim fine As Integer = indice
        While fine < testo.Length AndAlso Char.IsLetterOrDigit(testo(fine))
            fine += 1
        End While
        Dim lettere As Integer = 0
        For posizione As Integer = inizio To fine - 1
            If Char.IsLetter(testo(posizione)) Then lettere += 1
        Next
        Return lettere >= 2
    End Function

    Private Function VoceItaliana(nomeVoce As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(nomeVoce) Then Return False
            Dim nome As String = nomeVoce.ToLowerInvariant()
            If nome.Contains("ital") Then Return True
            If nome.Contains("it-it") Then Return True
            If nome.Contains("it_it") Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("VoceItaliana: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ApplicaVocePredefinitaSeNecessario()
        Try
            If _sapiVociIndicizzate Is Nothing Then Return
            If _sapiVociIndicizzate.Count = 0 Then Return

            Dim primoAvvio As Boolean = (Interlocked.Exchange(_configurazioneCreataOra, 0) = 1)

            Dim cfg As New WGConfig()
            Dim voceConfig As String = Nothing
            Try
                voceConfig = cfg.LeggiValore("SapiVoice")
            Catch ex As Exception
                log.ScriviLog("ApplicaVocePredefinitaSeNecessario (LeggiVoce): " & ex.Message)
            End Try

            If Not String.IsNullOrWhiteSpace(voceConfig) Then
                voceConfig = voceConfig.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
            Else
                voceConfig = String.Empty
            End If

            Dim voceValida As Boolean = False
            If voceConfig.Length > 0 Then
                If _sapiVociIndicizzate.ContainsKey(voceConfig) Then
                    voceValida = True
                Else
                    For Each kvpVoce In _sapiVociIndicizzate
                        If String.Equals(kvpVoce.Value, voceConfig, StringComparison.OrdinalIgnoreCase) Then
                            voceValida = True
                            Exit For
                        End If
                    Next
                End If
            End If

            If voceValida AndAlso Not primoAvvio Then Return

            Dim chiaviOrdinate As New List(Of String)(_sapiVociIndicizzate.Keys)
            Try
                chiaviOrdinate.Sort(AddressOf ConfrontaChiaviVoce)
            Catch ex As Exception
                log.ScriviLog("ApplicaVocePredefinitaSeNecessario (Ordinamento): " & ex.Message)
            End Try

            If chiaviOrdinate.Count = 0 Then Return

            Dim indiceScelto As String = String.Empty
            Dim nomeScelto As String = String.Empty

            For Each chiave As String In chiaviOrdinate
                Dim nomeVoce As String = Nothing
                If _sapiVociIndicizzate.TryGetValue(chiave, nomeVoce) Then
                    If VoceItaliana(nomeVoce) Then
                        indiceScelto = chiave
                        nomeScelto = nomeVoce
                        Exit For
                    End If
                End If
            Next

            If indiceScelto.Length = 0 Then
                For Each chiave As String In chiaviOrdinate
                    Dim nomeVoce As String = Nothing
                    If _sapiVociIndicizzate.TryGetValue(chiave, nomeVoce) Then
                        If Not String.IsNullOrWhiteSpace(nomeVoce) Then
                            indiceScelto = chiave
                            nomeScelto = nomeVoce
                            Exit For
                        End If
                    End If
                Next
            End If

            If indiceScelto.Length = 0 Then Return
            If voceValida AndAlso String.Equals(indiceScelto, voceConfig, StringComparison.Ordinal) Then Return

            Try
                cfg.SalvaValore("SapiVoice", indiceScelto)
            Catch ex As Exception
                log.ScriviLog("ApplicaVocePredefinitaSeNecessario (SalvaVoce): " & ex.Message)
                Try
                    cfg.AggiungiValore("SapiVoice", indiceScelto)
                Catch exAgg As Exception
                    log.ScriviLog("ApplicaVocePredefinitaSeNecessario (AggiungiVoce): " & exAgg.Message)
                End Try
            End Try

            Try
                InviaComandoSapiInCoda("SET_VOICE|" & indiceScelto)
            Catch ex As Exception
                log.ScriviLog("ApplicaVocePredefinitaSeNecessario (InvioVoce): " & ex.Message)
            End Try

            Try
                Dim rateDaApplicare As String = Nothing
                Dim volumeDaApplicare As String = Nothing
                Try
                    rateDaApplicare = cfg.LeggiValore("SapiRate")
                Catch ex As Exception
                    log.ScriviLog("ApplicaVocePredefinitaSeNecessario (LeggiRate): " & ex.Message)
                End Try
                Try
                    volumeDaApplicare = cfg.LeggiValore("SapiVolume")
                Catch ex As Exception
                    log.ScriviLog("ApplicaVocePredefinitaSeNecessario (LeggiVolume): " & ex.Message)
                End Try

                If String.IsNullOrWhiteSpace(rateDaApplicare) Then rateDaApplicare = SapiRatePredefinito
                If String.IsNullOrWhiteSpace(volumeDaApplicare) Then volumeDaApplicare = SapiVolumePredefinito

                rateDaApplicare = rateDaApplicare.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()
                volumeDaApplicare = volumeDaApplicare.Replace(vbCr, "").Replace(vbLf, "").Replace(vbTab, "").Trim()

                InviaComandoSapiInCoda("SET_RATE|" & rateDaApplicare)
                InviaComandoSapiInCoda("SET_VOLUME|" & volumeDaApplicare)

                SyncLock _lockUltimiApplicati
                    _ultimaVoceApplicata = indiceScelto
                    _ultimoRateApplicato = rateDaApplicare
                    _ultimoVolumeApplicato = volumeDaApplicare
                End SyncLock
            Catch ex As Exception
                log.ScriviLog("ApplicaVocePredefinitaSeNecessario (Impostazioni): " & ex.Message)
            End Try

        Catch ex As Exception
            log.ScriviLog("ApplicaVocePredefinitaSeNecessario: " & ex.Message)
        End Try
    End Sub

    Public Sub RicaricaConfigurazione()
        If Interlocked.CompareExchange(_ricaricaConfigInCorso, 1, 0) <> 0 Then Return
        Try
            Dim modalita As String = AssicuraConfigurazionePredefinita()

            If String.IsNullOrWhiteSpace(modalita) Then
                modalita = ModalitaLetturaPredefinita
            End If

            ' Modalità ScreenReader (NVDA) neutralizzata in questa versione: viene sempre ignorata.
            If modalita = "ScreenReader" Then
                modalita = "Nessun lettore"
            End If

            modalitaCorrente = modalita
            _ultimaModalitaMonitorata = modalita

            Task.Run(Sub()
                         Try
                             ChiudiScreenReaderIndesiderati(modalitaCorrente)
                         Catch ex As Exception
                             log.ScriviLog("RicaricaConfigurazione (ChiudiScreenReaderIndesiderati Task): " & ex.Message)
                         End Try
                     End Sub)

            If modalitaCorrente = "SAPI" Then
                ChiudiConnessioneNvda()
                If Not sapiInizializzato Then InizializzaSapi()
            ElseIf modalitaCorrente = "Nessun lettore" Then
                ChiudiConnessioneNvda()
                ChiudiSapi()
                SegnalaCanalePronto()
            End If
        Catch ex As Exception
            log.ScriviLog("RicaricaConfigurazione: " & ex.Message)
        Finally
            Interlocked.Exchange(_ricaricaConfigInCorso, 0)
        End Try
    End Sub

    Private Sub ChiudiScreenReaderIndesiderati(modalita As String)
        Try
            Dim nomiJaws As String() = {"jfw", "jaws", "jaws32", "jaws64"}
            For Each nome As String In nomiJaws
                Try
                    Dim procs() As Process = Process.GetProcessesByName(nome)
                    For Each p As Process In procs
                        Try
                            p.Kill()
                            p.WaitForExit(3000)
                        Catch ex As Exception
                        Finally
                            Try
                                p.Dispose()
                            Catch
                            End Try
                        End Try
                    Next
                Catch ex As Exception
                End Try
            Next

            Try
                Dim ksrProcs() As Process = Process.GetProcessesByName("KSR")
                For Each p As Process In ksrProcs
                    Try
                        p.Kill()
                        p.WaitForExit(3000)
                    Catch ex As Exception
                    Finally
                        Try
                            p.Dispose()
                        Catch
                        End Try
                    End Try
                Next
            Catch ex As Exception
            End Try

            Try
                Dim narrProcs() As Process = Process.GetProcessesByName("Narrator")
                For Each p As Process In narrProcs
                    Try
                        p.Kill()
                        p.WaitForExit(3000)
                    Catch ex As Exception
                    Finally
                        Try
                            p.Dispose()
                        Catch
                        End Try
                    End Try
                Next
            Catch ex As Exception
            End Try

            If modalita <> "ScreenReader" Then
                ChiudiProcessoNvda()
            End If
        Catch ex As Exception
        End Try
    End Sub

    Private _supervisorNvdaAttivo As Integer = 0

    Private Sub AvviaSupervisorNvdaSeNecessario()
        Try
            If Interlocked.CompareExchange(_supervisorNvdaAttivo, 1, 0) <> 0 Then Return
            Dim supervisorThread As New Thread(AddressOf EseguiSupervisorNvda)
            supervisorThread.IsBackground = True
            supervisorThread.Start()
        Catch ex As Exception
            Interlocked.Exchange(_supervisorNvdaAttivo, 0)
            log.ScriviLog("AvviaSupervisorNvdaSeNecessario: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiSupervisorNvda()
        Try
            Dim nvdaExeSup As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA", "nvda.exe")
            If Not File.Exists(nvdaExeSup) Then nvdaExeSup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA", "nvda.exe")

            Dim avvioRichiesto As Boolean = False
            Dim componenteInstallata As Boolean = False

            While Not masterCts.IsCancellationRequested
                Try
                    If modalitaCorrente <> "ScreenReader" Then
                        avvioRichiesto = False
                        componenteInstallata = False
                        If Not NvdaInEsecuzione() Then
                            Exit While
                        End If
                        If (TickCorrenti() - _ultimoTentativoChiusuraNvda) >= 10000 Then
                            _ultimoTentativoChiusuraNvda = TickCorrenti()
                            ChiudiConnessioneNvda()
                            ChiudiProcessoNvda()
                        End If
                        Thread.Sleep(250)
                        Continue While
                    End If

                    If Not NvdaInEsecuzione() Then
                        componenteInstallata = False
                        If Not avvioRichiesto Then
                            avvioRichiesto = True
                            If File.Exists(nvdaExeSup) Then
                                Try
                                    Process.Start(nvdaExeSup)
                                Catch ex As Exception
                                    log.ScriviLog("EseguiSupervisorNvda (Avvio): " & ex.Message)
                                End Try
                            End If
                        End If
                    Else
                        avvioRichiesto = False
                        If Not componenteInstallata Then
                            Try
                                InstallaComponenteNvda()
                                componenteInstallata = True
                            Catch ex As Exception
                                log.ScriviLog("EseguiSupervisorNvda (InstallaComponente): " & ex.Message)
                            End Try
                        End If
                    End If
                Catch ex As Exception
                    log.ScriviLog("EseguiSupervisorNvda (Ciclo): " & ex.Message)
                End Try
                Thread.Sleep(250)
            End While
        Catch ex As Exception
            log.ScriviLog("EseguiSupervisorNvda: " & ex.Message)
        Finally
            Interlocked.Exchange(_supervisorNvdaAttivo, 0)
        End Try
    End Sub

    Private Sub AvviaScreenReader()
        Try
            Dim nvdaExe As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA", "nvda.exe")
            If Not File.Exists(nvdaExe) Then nvdaExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA", "nvda.exe")

            Task.Run(Sub()
                         Try
                             AvviaSupervisorNvdaSeNecessario()
                             Dim nvdaGiaAvviato As Boolean = NvdaInEsecuzione()
                             If Not nvdaGiaAvviato Then
                                 If File.Exists(nvdaExe) Then
                                     Try
                                         Process.Start(nvdaExe)
                                         Dim attesePoll As Integer = 0
                                         While Not NvdaInEsecuzione() AndAlso attesePoll < 30 AndAlso Not masterCts.IsCancellationRequested
                                             Thread.Sleep(100)
                                             attesePoll += 1
                                         End While
                                         nvdaGiaAvviato = NvdaInEsecuzione()
                                     Catch ex As Exception
                                         log.ScriviLog("AvviaScreenReader (Avvio Processo): " & ex.Message)
                                     End Try
                                 End If
                             End If

                             If nvdaGiaAvviato Then
                                 InstallaComponenteNvda()
                                 AvviaConnessioneNvdaSeNecessario()
                             Else
                                 EseguiFailover()
                             End If
                         Catch taskEx As Exception
                             log.ScriviLog("AvviaScreenReader (Task): " & taskEx.Message)
                         End Try
                     End Sub)

        Catch ex As Exception
            log.ScriviLog("AvviaScreenReader: " & ex.Message)
        End Try
    End Sub

    Private Sub InstallaComponenteNvda()
        Try
            Dim cartellaAddonNvda As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvda", "addons", "wingabriel")
            Dim fileName As String = "wingabriel.nvda-addon"
            Dim appPath As String = AppDomain.CurrentDomain.BaseDirectory
            Dim filePath As String = Path.Combine(appPath, fileName)

            If File.Exists(filePath) Then
                Dim dataNvda As DateTime = DateTime.MinValue
                If Directory.Exists(cartellaAddonNvda) Then dataNvda = Directory.GetLastWriteTimeUtc(cartellaAddonNvda)
                Dim dataLocale As DateTime = File.GetLastWriteTimeUtc(filePath)
                If dataLocale > dataNvda Then
                    Dim startInfo As New ProcessStartInfo(filePath)
                    startInfo.UseShellExecute = True
                    Process.Start(startInfo)
                    Thread.Sleep(500)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("InstallaComponenteNvda: " & ex.Message)
        End Try
    End Sub

    Private Function AspettaDoneOAlive(msgIdAtteso As Integer, Optional budgetComplessivoMs As Integer = 0) As Boolean
        Try
            ' Il ciclo di PING invia un PING ogni 5 secondi e il server risponde sempre PONG
            ' (che rinfresca sapiUltimoSegnaleTick): 12 secondi senza ALCUN segnale coprono
            ' piu' di due periodi di PING e indicano quindi un processo davvero morto o
            ' bloccato, non semplicemente silenzioso durante la riproduzione audio.
            Const timeoutSenzaSegnaleMs As Integer = 12000

            SyncLock sapiSyncLock
                sapiUltimoSegnaleTick = TickCorrenti()
            End SyncLock

            Dim inizioAttesa As Long = TickCorrenti()

            While Not masterCts.IsCancellationRequested
                If currentSapiTestoId <> msgIdAtteso Then Return False
                If Not sapiInizializzato Then Return False

                Dim arrivato As Boolean = False
                Dim ultimoSegnale As Long = 0
                SyncLock sapiSyncLock
                    If sapiDoneMsgId = msgIdAtteso Then arrivato = True
                    ultimoSegnale = sapiUltimoSegnaleTick
                End SyncLock

                If arrivato Then Return True

                Dim elapsed As Long = TickCorrenti() - ultimoSegnale
                If elapsed > timeoutSenzaSegnaleMs Then
                    ' Prima di dichiarare il server bloccato, verifichiamolo attivamente con
                    ' un PING diretto (bypassa la coda comandi, cosi' non resta impilato
                    ' dietro eventuali SPEAK ancora da processare). Un processo davvero
                    ' bloccato non rispondera' nemmeno a questo; un semplice rallentamento
                    ' momentaneo (GC, carico di sistema, audio lungo con poche parole) invece
                    ' si fara' vivo in tempo e non deve costare un failover.
                    Const timeoutVerificaPingMs As Integer = 3000
                    Dim tickPrimaVerifica As Long = TickCorrenti()

                    Try
                        InviaComandoSapiSicuro("PING")
                    Catch ex As Exception
                        log.ScriviLog("AspettaDoneOAlive (Ping verifica): " & ex.Message)
                    End Try

                    Dim segnaleArrivatoDopoVerifica As Boolean = False
                    While TickCorrenti() - tickPrimaVerifica < timeoutVerificaPingMs
                        If masterCts.IsCancellationRequested Then Exit While
                        If currentSapiTestoId <> msgIdAtteso Then Return False
                        If Not sapiInizializzato Then Return False

                        SyncLock sapiSyncLock
                            If sapiDoneMsgId = msgIdAtteso Then Return True
                            If sapiUltimoSegnaleTick <> ultimoSegnale Then segnaleArrivatoDopoVerifica = True
                        End SyncLock
                        If segnaleArrivatoDopoVerifica Then Exit While

                        Thread.Sleep(20)
                    End While

                    If segnaleArrivatoDopoVerifica Then
                        ' Il server ha risposto: era solo un rallentamento momentaneo, non un
                        ' blocco. Si prosegue normalmente (il timer e' gia' stato aggiornato).
                        Continue While
                    End If

                    Return False
                End If

                ' Protezione contro un DONE perso con server ancora vivo: oltre il tetto
                ' complessivo (proporzionato alla lunghezza del testo) la lettura viene
                ' considerata conclusa e si prosegue, senza failover: i segnali ricevuti
                ' dimostrano che il processo e' vivo, riavviarlo sarebbe solo dannoso.
                If budgetComplessivoMs > 0 AndAlso (TickCorrenti() - inizioAttesa) > budgetComplessivoMs Then
                    Return True
                End If

                Thread.Sleep(20)
            End While

            Return False
        Catch ex As Exception
            log.ScriviLog("AspettaDoneOAlive: " & ex.Message)
            Return False
        End Try
    End Function

    Public ReadOnly Property StaParlando As Boolean
        Get
            Try
                Return Interlocked.CompareExchange(_staParlando, 0, 0) = 1
            Catch ex As Exception
                log.ScriviLog("StaParlando Get: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Function SintesiInParlato() As Boolean
        Try
            Return Interlocked.CompareExchange(_staParlando, 0, 0) = 1
        Catch ex As Exception
            log.ScriviLog("SintesiInParlato: " & ex.Message)
            Return False
        End Try
    End Function

    Public ReadOnly Property NvdaCollegato As Boolean
        Get
            Try
                Return Interlocked.CompareExchange(_nvdaCollegato, 0, 0) = 1
            Catch ex As Exception
                log.ScriviLog("NvdaCollegato Get: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Sub ImpostaCollegato()
        Try
            Interlocked.Exchange(_nvdaCollegato, 1)
            SegnalaCanalePronto()
        Catch ex As Exception
            log.ScriviLog("ImpostaCollegato: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraCollegato()
        Try
            Interlocked.Exchange(_nvdaCollegato, 0)
        Catch ex As Exception
            log.ScriviLog("AzzeraCollegato: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaParlato()
        Try
            Dim precedente As Integer = Interlocked.Exchange(_staParlando, 1)
            Interlocked.Exchange(_zeriParlato, 0)
            Interlocked.Exchange(_tickInizioParlato, CLng(TickCorrenti()))
            If precedente = 0 Then DispatchOnStatoParlatoCambiato(True)
        Catch ex As Exception
            log.ScriviLog("ImpostaParlato: " & ex.Message)
        End Try
    End Sub

    Private Sub AzzeraParlato()
        Try
            Dim precedente As Integer = Interlocked.Exchange(_staParlando, 0)
            Interlocked.Exchange(_zeriParlato, 0)
            If precedente = 1 Then DispatchOnStatoParlatoCambiato(False)
        Catch ex As Exception
            log.ScriviLog("AzzeraParlato: " & ex.Message)
        End Try
    End Sub

    Private Sub DispatchOnStatoParlatoCambiato(valore As Boolean)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Of Boolean)(AddressOf SollevaStatoParlatoCambiato), valore)
            Else
                SollevaStatoParlatoCambiato(valore)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnStatoParlatoCambiato: " & ex.Message)
            SollevaStatoParlatoCambiato(valore)
        End Try
    End Sub

    Private Sub SollevaStatoParlatoCambiato(valore As Boolean)
        Try
            RaiseEvent OnStatoParlatoCambiato(valore)
        Catch ex As Exception
            log.ScriviLog("SollevaStatoParlatoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub DispatchOnInizioLettura(testo As String, origine As String)
        Try
            If Application.OpenForms.Count > 0 AndAlso Application.OpenForms(0).InvokeRequired Then
                Application.OpenForms(0).BeginInvoke(New Action(Of String, String)(AddressOf SollevaInizioLettura), testo, origine)
            Else
                SollevaInizioLettura(testo, origine)
            End If
        Catch ex As Exception
            log.ScriviLog("DispatchOnInizioLettura: " & ex.Message)
            SollevaInizioLettura(testo, origine)
        End Try
    End Sub

    Private Sub SollevaInizioLettura(testo As String, origine As String)
        Try
            RaiseEvent OnInizioLettura(testo, If(origine, ""))
        Catch ex As Exception
            log.ScriviLog("SollevaInizioLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnalaParlatoAttivoDaPoll()
        Try
            Interlocked.Exchange(_zeriParlato, 0)
            ImpostaParlato()
        Catch ex As Exception
            log.ScriviLog("SegnalaParlatoAttivoDaPoll: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnalaParlatoFermoDaPoll()
        Try
            If Interlocked.CompareExchange(_staParlando, 0, 0) <> 1 Then Return
            Dim inizio As Long = Interlocked.Read(_tickInizioParlato)
            If TickCorrenti() - inizio < GraziaParlatoMs Then Return
            Dim zeri As Integer = Interlocked.Increment(_zeriParlato)
            If zeri >= ZeriParlatoConferma Then
                AzzeraParlato()
            End If
        Catch ex As Exception
            log.ScriviLog("SegnalaParlatoFermoDaPoll: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Interrompe subito la voce, qualunque cosa stia dicendo.</summary>
    Public Sub FermaVoce()
        Try
            WGBraille.AnnullaFine()
            AzzeraParlato()
            ScartaTestoInAttesa()
            If modalitaCorrente = "Nessun lettore" Then Return

            If modalitaCorrente = "SAPI" Then
                ' Incrementare currentSapiTestoId qui e' cio' che rende uno stop
                ' "totale" sui chunk: il ciclo di invio in LeggiTesto smette di
                ' accodarne di nuovi (controlla questo valore ad ogni iterazione)
                ' e EseguiConsumerCodaComandiSapi scarta silenziosamente ogni
                ' chunk SPEAK gia' in coda ma appartenente alla lettura precedente,
                ' cosi' non arriva a WGSapi.exe nemmeno un pezzo di testo "vecchio".
                If _espeakAttivo AndAlso _motoreEspeak IsNot Nothing Then
                    ' Con eSpeak lo stop è subito, senza passare dalla coda: quella coda era un altro ritardo.
                    SyncLock _bloccoEspeakComandi
                        Interlocked.Increment(currentSapiTestoId)
                        SyncLock sapiSyncLock
                            sapiDoneMsgId = -1
                        End SyncLock
                        _motoreEspeak.Ferma()
                    End SyncLock
                Else
                    Interlocked.Increment(currentSapiTestoId)
                    SyncLock sapiSyncLock
                        sapiDoneMsgId = -1
                    End SyncLock
                    If CanaleSapiScrivibile() Then InviaComandoSapiInCoda("STOP")
                End If
                RaiseEvent vocestoppata(Me, Nothing)
            Else
                Task.Run(Sub()
                             Try
                                 InviaComandoNvda("STOP")
                             Catch ex As Exception
                                 log.ScriviLog("FermaVoce (NVDA Command): " & ex.Message)
                             End Try
                         End Sub)
            End If
        Catch ex As Exception
            log.ScriviLog("FermaVoce: " & ex.Message)
        End Try
    End Sub

    Private ReadOnly _configLetturaPunteggiatura As New WGConfig()

    Public Property LetturaPunteggiaturaGenerale As Boolean
        Get
            Try
                Dim valore As String = _configLetturaPunteggiatura.LeggiValore("lettura_punteggiatura_lettura")
                If String.IsNullOrEmpty(valore) Then Return True
                Return valore <> "0"
            Catch ex As Exception
                log.ScriviLog("LetturaPunteggiaturaGenerale Get: " & ex.Message)
                Return True
            End Try
        End Get
        Set(value As Boolean)
            Try
                _configLetturaPunteggiatura.AggiungiValore("lettura_punteggiatura_lettura", If(value, "1", "0"))
            Catch ex As Exception
                log.ScriviLog("LetturaPunteggiaturaGenerale Set: " & ex.Message)
            End Try
        End Set
    End Property

    Private Function LivelloSimbolo(c As Char) As Integer
        Try
            Select Case c
                Case "."c, ","c, "?"c, "!"c
                    Return 3
                Case "("c, ")"c, "["c, "]"c, "{"c, "}"c, """"c, "'"c, "`"c, "«"c, "»"c, "-"c, "–"c, "—"c, ";"c, ":"c, "…"c
                    Return 2
                Case Else
                    Return 1
            End Select
        Catch ex As Exception
            log.ScriviLog("LivelloSimbolo: " & ex.Message)
            Return 1
        End Try
    End Function

    Public ReadOnly Property LivelloPunteggiaturaLettura As Integer
        Get
            Try
                Dim valoreLivello As String = _configLetturaPunteggiatura.LeggiValore("livello_punteggiatura_lettura")
                If Not String.IsNullOrEmpty(valoreLivello) Then
                    Dim livelloParse As Integer
                    If Integer.TryParse(valoreLivello, livelloParse) Then
                        If livelloParse < 0 Then livelloParse = 0
                        If livelloParse > 3 Then livelloParse = 3
                        Return livelloParse
                    End If
                End If
                Dim legacy As String = _configLetturaPunteggiatura.LeggiValore("lettura_punteggiatura_lettura")
                If legacy = "0" Then Return 0
                Return 3
            Catch ex As Exception
                log.ScriviLog("LivelloPunteggiaturaLettura Get: " & ex.Message)
                Return 3
            End Try
        End Get
    End Property

    Private Function TraduciSimboloPerLettura(c As Char) As String
        Try
            Select Case c
                Case "."c : Return "punto"
                Case ","c : Return "virgola"
                Case ";"c : Return "punto e virgola"
                Case ":"c : Return "due punti"
                Case "!"c : Return "punto esclamativo"
                Case "?"c : Return "punto interrogativo"
                Case "("c : Return "aperta parentesi"
                Case ")"c : Return "chiusa parentesi"
                Case "["c : Return "aperta quadra"
                Case "]"c : Return "chiusa quadra"
                Case "{"c : Return "aperta graffa"
                Case "}"c : Return "chiusa graffa"
                Case "<"c : Return "minore"
                Case ">"c : Return "maggiore"
                Case "="c : Return "uguale"
                Case "+"c : Return "più"
                Case "*"c : Return "asterisco"
                Case "/"c : Return "barra"
                Case "\"c : Return "barra rovesciata"
                Case "|"c : Return "barra verticale"
                Case "_"c : Return "sottolineato"
                Case "@"c : Return "chiocciola"
                Case "#"c : Return "cancelletto"
                Case "€"c : Return "euro"
                Case "$"c : Return "dollaro"
                Case "%"c : Return "percento"
                Case "&"c : Return "e commerciale"
                Case Else : Return Nothing
            End Select
        Catch ex As Exception
            log.ScriviLog("TraduciSimboloPerLettura: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private _impostazioniNumeri As New WGImpostazioniComuni()

    Private Function ApplicaLetturaNumeri(testo As String) As String
        Try
            Dim modalita As String = _impostazioniNumeri.ModalitaLetturaNumeri
            If modalita = "intero" OrElse String.IsNullOrEmpty(testo) Then Return testo

            Dim risultato As New StringBuilder(testo.Length + 16)
            Dim i As Integer = 0
            While i < testo.Length
                If Char.IsDigit(testo(i)) Then
                    Dim inizio As Integer = i
                    While i < testo.Length AndAlso Char.IsDigit(testo(i))
                        i += 1
                    End While
                    Dim numero As String = testo.Substring(inizio, i - inizio)
                    risultato.Append(FormattaNumeroPerLettura(numero, modalita))
                Else
                    risultato.Append(testo(i))
                    i += 1
                End If
            End While
            Return risultato.ToString()
        Catch ex As Exception
            log.ScriviLog("ApplicaLetturaNumeri: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function FormattaNumeroPerLettura(numero As String, modalita As String) As String
        Try
            If numero.Length <= 1 Then Return numero
            Dim sb As New StringBuilder()
            Select Case modalita
                Case "cifra"
                    For Each cifraSingola As Char In numero
                        sb.Append(cifraSingola).Append(" ")
                    Next
                Case "coppie"
                    Dim resto As Integer = numero.Length Mod 2
                    Dim idx As Integer = 0
                    If resto = 1 Then
                        sb.Append(numero(0)).Append(" ")
                        idx = 1
                    End If
                    While idx < numero.Length
                        sb.Append(numero.Substring(idx, 2)).Append(" ")
                        idx += 2
                    End While
                Case "triplette"
                    Dim restoT As Integer = numero.Length Mod 3
                    Dim idxT As Integer = 0
                    If restoT > 0 Then
                        sb.Append(numero.Substring(0, restoT)).Append(" ")
                        idxT = restoT
                    End If
                    While idxT < numero.Length
                        sb.Append(numero.Substring(idxT, 3)).Append(" ")
                        idxT += 3
                    End While
                Case Else
                    Return numero
            End Select
            Return sb.ToString().TrimEnd()
        Catch ex As Exception
            log.ScriviLog("FormattaNumeroPerLettura: " & ex.Message)
            Return numero
        End Try
    End Function
    Private Function ApplicaLetturaPunteggiatura(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return testo
            Dim livelloAttivo As Integer = LivelloPunteggiaturaLettura
            If livelloAttivo <= 0 Then Return testo

            Dim contieneSimboli As Boolean = False
            For Each c As Char In testo
                If TraduciSimboloPerLettura(c) IsNot Nothing AndAlso LivelloSimbolo(c) <= livelloAttivo Then
                    contieneSimboli = True
                    Exit For
                End If
            Next
            If Not contieneSimboli Then Return testo

            Dim sb As New StringBuilder(testo.Length)
            For Each c As Char In testo
                Dim parola As String = TraduciSimboloPerLettura(c)
                If parola IsNot Nothing AndAlso LivelloSimbolo(c) <= livelloAttivo Then
                    sb.Append(" ").Append(parola).Append(" ")
                Else
                    sb.Append(c)
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            log.ScriviLog("ApplicaLetturaPunteggiatura: " & ex.Message)
            Return testo
        End Try
    End Function

    Public Function ConvertiLetteraAccentataPerLettura(c As Char) As String
        Try
            Select Case c
                Case "à"c
                    Return "à"
                Case "è"c
                    Return "è"
                Case "é"c
                    Return "é"
                Case "ì"c
                    Return "ì"
                Case "ò"c
                    Return "ò"
                Case "ù"c
                    Return "ù"
                Case "À"c
                    Return "À"
                Case "È"c
                    Return "È"
                Case "É"c
                    Return "É"
                Case "Ì"c
                    Return "Ì"
                Case "Ò"c
                    Return "Ò"
                Case "Ù"c
                    Return "Ù"
                Case Else
                    Return c.ToString()
            End Select
        Catch ex As Exception
            log.ScriviLog("sintesivocale.ConvertiLetteraAccentataPerLettura: " & ex.Message)
            Return c.ToString()
        End Try
    End Function

    Private ReadOnly _doppioneLock As New Object()
    Private _ultimoTestoLetto As String = ""
    Private _tickUltimoTestoLetto As Long

    Public Sub LeggiTesto(testo As String, Optional sincrono As Boolean = False, Optional priorita As String = "Normale", Optional tracciaParole As Boolean = False, Optional sopprimi500Ms As Boolean = False, Optional escludiPunteggiatura As Boolean = False, Optional origine As String = "")
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            ' Gli annunci di sistema (per esempio "Audio su cuffie" quando si collega un dispositivo Bluetooth) non si
            ' interrompono tra loro: un annuncio che arriva mentre un altro e' ancora in lettura aspetta che finisca, oppure
            ' che passi il tempo scelto per i messaggi (con la sola barra braille nessun evento dice che e' finito).
            If priorita = "Annuncio" Then
                Dim testoAnnuncio As String = testo
                If AccodaAnnuncio(Sub() LeggiTesto(testoAnnuncio, False, "Annuncio"), True) Then Return
                priorita = "Normale"
            ElseIf priorita <> "Alta" Then
                ' Mentre un annuncio e' in lettura, i testi normali (per esempio il menu che si apre in quel momento)
                ' aspettano la sua fine: non lo interrompono e non lo coprono sulla barra braille.
                Dim t0 As String = testo, s0 As Boolean = sincrono, p0 As String = priorita, tr0 As Boolean = tracciaParole
                Dim so0 As Boolean = sopprimi500Ms, e0 As Boolean = escludiPunteggiatura, o0 As String = origine
                If AccodaAnnuncio(Sub() LeggiTesto(t0, s0, p0, tr0, so0, e0, o0), False) Then Return
            End If
            ' Il braille riceve il testo da qui, l'unico punto da cui passa tutto quello che il programma dice,
            ' anche quando la voce e' spenta.
            Try
                WGBraille.Mostra(testo, priorita)
            Catch exBraille As Exception
                log.ScriviLog("LeggiTesto (braille): " & exBraille.Message)
            End Try
            ' Se il pezzo di testo che si sta per dire e' in una lingua diversa, e le impostazioni del traduttore lo
            ' prevedono, la voce e la tabella braille cambiano da sole anche dentro lo stesso documento o pagina (non solo
            ' una volta all'apertura): WGTraduttore riconosce solo testi lunghi abbastanza da non confondersi su un
            ' titolo di menu o un breve annuncio dell'interfaccia.
            Try
                WGTraduttore.ApplicaLinguaTestoLetto(testo)
            Catch exLingua As Exception
                log.ScriviLog("LeggiTesto (lingua): " & exLingua.Message)
            End Try
            If modalitaCorrente = "Nessun lettore" Then
                ' Voce spenta: con la barra braille collegata la lettura si considera finita dopo il tempo scelto dall'utente
                ' (messaggi: 10 secondi di norma; lettura continua: il tempo della lettura continua).
                If WGBraille.Collegata Then
                    DispatchOnInizioLettura(testo, origine)
                    WGBraille.PianificaFine(AddressOf DispatchOnFineLettura)
                End If
                Return
            End If

            Dim testoProcessato As String = ApplicaFiltriDizionario(testo)
            If Not escludiPunteggiatura Then testoProcessato = ApplicaLetturaPunteggiatura(testoProcessato)
            testoProcessato = LeggiPuntiNeiNomi(testoProcessato)
            testoProcessato = UnisciRighe(testoProcessato.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)).Replace(vbTab, " ")
            testoProcessato = ApplicaLetturaNumeri(testoProcessato)
            Dim sbPulito As New StringBuilder(testoProcessato.Length)
            For Each chSanitizza As Char In testoProcessato
                Dim codiceCarattere As Integer = AscW(chSanitizza)
                If codiceCarattere >= 32 AndAlso codiceCarattere <> 127 Then
                    sbPulito.Append(chSanitizza)
                Else
                    sbPulito.Append(" "c)
                End If
            Next
            testoProcessato = sbPulito.ToString()
            If String.IsNullOrWhiteSpace(testoProcessato) Then Return

            ' Lo stesso annuncio chiesto due volte di seguito da due parti del programma (la finestra che
            ' compare e il contenitore che la annuncia) si legge una volta sola. Un terzo di secondo è meno
            ' di quanto serve a una persona per premere di nuovo un tasto per farsi rileggere qualcosa.
            ' Vale solo per testi di più parole: le lettere e le parole scritte due volte di seguito si ripetono.
            SyncLock _doppioneLock
                Dim adesso As Long = TickCorrenti()
                If testoProcessato.Length >= 8 AndAlso testoProcessato.Trim().Contains(" ") AndAlso
                   String.Equals(testoProcessato, _ultimoTestoLetto, StringComparison.Ordinal) AndAlso adesso - _tickUltimoTestoLetto < 350 Then Return
                _ultimoTestoLetto = testoProcessato
                _tickUltimoTestoLetto = adesso
            End SyncLock

            If sopprimi500Ms Then
                SyncLock _soppressione500MsLock
                    Dim ora As Long = TickCorrenti()
                    If ora - _soppressione500MsTick < 500 Then Return
                    _soppressione500MsTick = ora
                End SyncLock
            End If

            Dim usaSapi As Boolean = (modalitaCorrente = "SAPI")

            If priorita = "Alta" AndAlso usaSapi Then
                FermaVoce()
            End If

            DispatchOnInizioLettura(testo, origine)
            ImpostaParlato()

            If usaSapi Then
                If sapiInizializzato = False Then
                    MemorizzaTestoInAttesa(testo, priorita, tracciaParole, escludiPunteggiatura)
                    Task.Run(Sub()
                                 Try
                                     InizializzaSapi()
                                 Catch ex As Exception
                                     log.ScriviLog("LeggiTesto (InizializzaSapi Task): " & ex.Message)
                                 End Try
                             End Sub)
                    Return
                End If
                If CanaleSapiScrivibile() Then
                    If _espeakAttivo AndAlso _motoreEspeak IsNot Nothing Then
                        ParlaSubitoEspeak(testoProcessato, tracciaParole)
                        Return
                    End If
                    If VoceCodeFactory() Then testoProcessato = SeparaParoleComposte(testoProcessato)
                    Dim myMsgId As Integer = Interlocked.Increment(currentSapiTestoId)

                    SyncLock sapiSyncLock
                        sapiDoneMsgId = -1
                    End SyncLock

                    Task.Run(Sub()
                                 Try
                                     Dim maxLen As Integer = 1000
                                     Dim chunks As New List(Of String)()
                                     Dim offsets As New List(Of Integer)()
                                     Dim startIdx As Integer = 0

                                     While startIdx < testoProcessato.Length
                                         Dim len As Integer = Math.Min(maxLen, testoProcessato.Length - startIdx)

                                         If startIdx + len < testoProcessato.Length Then
                                             Dim lastPunct As Integer = testoProcessato.LastIndexOfAny(New Char() {" "c, "."c, ","c, ";"c, ":"c, "?"c, "!"c, ControlChars.Lf, ControlChars.Cr}, startIdx + len - 1, len)
                                             If lastPunct > startIdx Then
                                                 len = lastPunct - startIdx + 1
                                             End If
                                         End If

                                         offsets.Add(startIdx)
                                         chunks.Add(testoProcessato.Substring(startIdx, len))
                                         startIdx += len
                                     End While

                                     For i As Integer = 0 To chunks.Count - 1
                                         If currentSapiTestoId <> myMsgId Then Exit For
                                         If masterCts.IsCancellationRequested Then Exit For
                                         If Not sapiInizializzato Then Exit For

                                         Dim isLast As Integer = If(i = chunks.Count - 1, 1, 0)
                                         Dim testoChunkSicuro As String = chunks(i).Replace(ControlChars.Cr, " "c).Replace(ControlChars.Lf, " "c)
                                         InviaComandoSapiInCoda("SPEAK|" & myMsgId.ToString() & "|" & i.ToString() & "|" & isLast.ToString() & "|" & offsets(i).ToString() & "|" & testoChunkSicuro)
                                     Next

                                     If currentSapiTestoId = myMsgId AndAlso sapiInizializzato Then
                                         Dim budgetMs As Integer = CInt(Math.Min(1800000L, 20000L + CLng(testoProcessato.Length) * 200L))
                                         Dim arrivato As Boolean = AspettaDoneOAlive(myMsgId, budgetMs)
                                         If arrivato Then
                                             If currentSapiTestoId = myMsgId Then
                                                 AzzeraParlato()
                                                 DispatchOnFineLettura()
                                             End If
                                         ElseIf currentSapiTestoId = myMsgId AndAlso Not masterCts.IsCancellationRequested Then
                                             ' Nessun failover su NVDA: si usa sempre e solo il server SAPI.
                                             ' AspettaDoneOAlive ha gia' verificato attivamente il server con
                                             ' un PING diretto prima di arrivare qui, quindi a questo punto e'
                                             ' davvero non responsivo: lo riavviamo (resta SAPI a tutti gli
                                             ' effetti) e chiudiamo comunque questo messaggio, cosi' l'app non
                                             ' resta in attesa indefinita di un DONE che non arrivera' piu'.
                                             AzzeraParlato()
                                             DispatchOnFineLettura()
                                             sapiInizializzato = False
                                             Task.Run(Sub()
                                                          Try
                                                              InizializzaSapiInterno()
                                                          Catch ex As Exception
                                                              log.ScriviLog("LeggiTesto (Riavvio SAPI): " & ex.Message)
                                                          End Try
                                                      End Sub)
                                         End If
                                     End If
                                 Catch ex As Exception
                                     log.ScriviLog("LeggiTesto (SAPI Task): " & ex.Message)
                                 End Try
                             End Sub, masterCts.Token)
                End If
                Return
            End If

            Dim testoUnico As String = testoProcessato.Replace(vbCr, " ").Replace(vbLf, " ").Trim()

            If String.IsNullOrEmpty(testoUnico) Then Return

            ' L'invio viene eseguito su un thread separato: la scrittura sulla pipe verso NVDA
            ' e' un'operazione di I/O sincrona e senza timeout. Se venisse eseguita sul thread
            ' chiamante (spesso il thread UI, es. da LeggiParagrafoCorrente via BeginInvoke),
            ' un rallentamento del processo NVDA bloccherebbe l'intera interfaccia.
            Task.Run(Sub()
                         Try
                             InviaComandoNvda("SPEAK_NOW:" & testoUnico)
                         Catch ex As Exception
                             log.ScriviLog("LeggiTesto (NVDA): " & ex.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("LeggiTesto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dice se il processo di NVDA è in esecuzione.</summary>
    Public Function NvdaInEsecuzione() As Boolean
        Dim procs() As Process = Nothing
        Try
            procs = Process.GetProcessesByName("nvda")
            Return procs.Length > 0
        Catch ex As Exception
            log.ScriviLog("NvdaInEsecuzione: " & ex.Message)
            Return False
        Finally
            If procs IsNot Nothing Then
                For Each p As Process In procs
                    Try
                        p.Dispose()
                    Catch ex As Exception
                        log.ScriviLog("NvdaInEsecuzione (Dispose): " & ex.Message)
                    End Try
                Next
            End If
        End Try
    End Function

    Private avvioConnessioneInCorso As Boolean = False
    Private ReadOnly avvioConnessioneLock As New Object()

    Private Function AvviaConnessioneNvdaSeNecessario() As Boolean
        Try
            SyncLock avvioConnessioneLock
                If isListening Then
                    Return False
                End If
                If avvioConnessioneInCorso Then
                    Return False
                End If
                avvioConnessioneInCorso = True
            End SyncLock

            Try
                If ConnettiClientNvda() Then
                    AvviaGestoreClient(primaConnessione:=True)
                    Return True
                End If
                Return False
            Finally
                SyncLock avvioConnessioneLock
                    avvioConnessioneInCorso = False
                End SyncLock
            End Try
        Catch ex As Exception
            log.ScriviLog("AvviaConnessioneNvdaSeNecessario: " & ex.Message)
            SyncLock avvioConnessioneLock
                avvioConnessioneInCorso = False
            End SyncLock
            Return False
        End Try
    End Function

    Private Function ConnettiClientNvda() As Boolean
        Try
            Dim attesaMs As Integer = 50
            While Not masterCts.IsCancellationRequested
                If modalitaCorrente <> "ScreenReader" Then
                    Return False
                End If

                Dim client As NamedPipeClientStream = Nothing
                Try
                    client = New NamedPipeClientStream(".", "WingGabrielNvda", PipeDirection.InOut, PipeOptions.Asynchronous)
                Catch ex As Exception
                    log.ScriviLog("ConnettiClientNvda (Creazione): " & ex.Message)
                End Try

                If client IsNot Nothing Then
                    Try
                        client.Connect(500)
                        SyncLock serverLock
                            nvdaPipeClient = client
                        End SyncLock
                        Return True
                    Catch ex As TimeoutException
                        Try
                            client.Dispose()
                        Catch exDispose As Exception
                            log.ScriviLog("ConnettiClientNvda (Dispose Timeout): " & exDispose.Message)
                        End Try
                    Catch ex As Exception
                        log.ScriviLog("ConnettiClientNvda (Connect): " & ex.Message)
                        Try
                            client.Dispose()
                        Catch exDispose As Exception
                            log.ScriviLog("ConnettiClientNvda (Dispose Errore): " & exDispose.Message)
                        End Try
                    End Try
                End If

                Thread.Sleep(attesaMs)
            End While
            Return False
        Catch ex As Exception
            log.ScriviLog("ConnettiClientNvda: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AvviaGestoreClient(primaConnessione As Boolean)
        Try
            isListening = True
            Dim gestoreThread As New Thread(AddressOf EseguiGestoreClient)
            gestoreThread.IsBackground = True
            gestoreThread.Start()
        Catch ex As Exception
            log.ScriviLog("AvviaGestoreClient: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiGestoreClient()
        Try
            While isListening AndAlso Not masterCts.IsCancellationRequested
                Dim client As NamedPipeClientStream = Nothing
                SyncLock serverLock
                    client = nvdaPipeClient
                End SyncLock

                If client Is Nothing Then
                    Exit While
                End If

                GestisciClient(client)

                Try
                    client.Dispose()
                Catch ex As Exception
                    log.ScriviLog("EseguiGestoreClient (Dispose): " & ex.Message)
                End Try

                SyncLock serverLock
                    If nvdaPipeClient Is client Then
                        nvdaPipeClient = Nothing
                    End If
                End SyncLock

                If Not isListening OrElse masterCts.IsCancellationRequested Then
                    Exit While
                End If

                If Not ConnettiClientNvda() Then
                    Exit While
                End If
            End While
        Catch ex As Exception
            log.ScriviLog("EseguiGestoreClient: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciClient(client As NamedPipeClientStream)
        Dim pollThread As Thread = Nothing
        Try
            If client Is Nothing Then Return

            Try
                nvdaWriter = New StreamWriter(client, New System.Text.UTF8Encoding(False))
                nvdaWriter.AutoFlush = True
            Catch ex As Exception
                log.ScriviLog("GestisciClient (Writer): " & ex.Message)
                Return
            End Try

            ImpostaCollegato()
            ApplicaImpostazioniNvda()

            pollThread = New Thread(AddressOf PollParlato)
            pollThread.IsBackground = True
            pollThread.Start(client)

            Dim reader As New StreamReader(client, New System.Text.UTF8Encoding(False))
            While isListening AndAlso Not masterCts.IsCancellationRequested AndAlso client.IsConnected
                Dim line As String
                Try
                    line = reader.ReadLine()
                Catch ex As IOException
                    Exit While
                Catch ex As ObjectDisposedException
                    Exit While
                Catch ex As Exception
                    log.ScriviLog("GestisciClient (ReadLine): " & ex.Message)
                    Exit While
                End Try

                If line Is Nothing Then Exit While
                ImpostaCollegato()
                ElaboraRigaNvda(line.Trim())
            End While
        Catch ex As Exception
            log.ScriviLog("GestisciClient: " & ex.Message)
        Finally
            Try
                AzzeraCollegato()
                AzzeraParlato()
            Catch ex As Exception
                log.ScriviLog("GestisciClient (Reset Stato): " & ex.Message)
            End Try

            nvdaWriter = Nothing
        End Try
    End Sub

    Private Sub PollParlato(statoClient As Object)
        Dim client As NamedPipeClientStream = TryCast(statoClient, NamedPipeClientStream)
        Try
            If client Is Nothing Then Return
            While isListening AndAlso Not masterCts.IsCancellationRequested
                If Not client.IsConnected Then Exit While
                Try
                    InviaComandoNvda("GET_SPEAKING")
                Catch ex As Exception
                    log.ScriviLog("PollParlato (Invio): " & ex.Message)
                    Exit While
                End Try
                Thread.Sleep(50)
            End While
        Catch ex As Exception
            log.ScriviLog("PollParlato: " & ex.Message)
        End Try
    End Sub

    Private Sub ElaboraRigaNvda(line As String)
        Try
            If String.IsNullOrEmpty(line) Then Return

            If line.StartsWith("VOICES:") Then
                Dim voiceData As String = line.Substring(7)
                Dim voicePairs As String() = voiceData.Split("|"c)
                NvdaVoci.Clear()
                For Each pair In voicePairs
                    If String.IsNullOrEmpty(pair) Then Continue For
                    Dim sep As Integer = pair.IndexOf("="c)
                    If sep > 0 Then
                        Dim k As String = DecodificaCampoNvda(pair.Substring(0, sep))
                        Dim v As String = DecodificaCampoNvda(pair.Substring(sep + 1))
                        NvdaVoci(k) = v
                    End If
                Next
            ElseIf line.StartsWith("SYNTHS:") Then
                Dim synthData As String = line.Substring(7)
                Dim synthPairs As String() = synthData.Split("|"c)
                NvdaSintetizzatori.Clear()
                For Each pair In synthPairs
                    If String.IsNullOrEmpty(pair) Then Continue For
                    Dim sep As Integer = pair.IndexOf("="c)
                    If sep > 0 Then
                        Dim k As String = DecodificaCampoNvda(pair.Substring(0, sep))
                        Dim v As String = DecodificaCampoNvda(pair.Substring(sep + 1))
                        NvdaSintetizzatori(k) = v
                    End If
                Next
            ElseIf line.StartsWith("STATE:") Then
                Dim stateData As String = line.Substring(6)
                Dim stateParts As String() = stateData.Split("|"c)
                NvdaStato.Clear()
                For Each parte In stateParts
                    If String.IsNullOrEmpty(parte) Then Continue For
                    Dim sep As Integer = parte.IndexOf("="c)
                    If sep > 0 Then
                        Dim k As String = parte.Substring(0, sep)
                        Dim v As String = DecodificaCampoNvda(parte.Substring(sep + 1))
                        NvdaStato(k) = v
                    End If
                Next
            ElseIf line.StartsWith("SPEECH:") OrElse line.StartsWith("BRAILLE:") Then
                DispatchOnTestoRicevuto(line)
            ElseIf line.StartsWith("SAVED:") Then
                Dim esito As String = line.Substring(6).Trim()
                DispatchOnImpostazioniNvdaSalvate(esito = "1")
            ElseIf line = "READING_DONE" Then
                AzzeraParlato()
                DispatchOnFineLettura()
            ElseIf line.StartsWith("SPEAKING:") Then
                Try
                    Dim valSp As String = line.Substring(9).Trim()
                    If valSp = "1" Then
                        SegnalaParlatoAttivoDaPoll()
                    Else
                        SegnalaParlatoFermoDaPoll()
                    End If
                Catch ex As Exception
                    log.ScriviLog("ElaboraRigaNvda (SPEAKING): " & ex.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("ElaboraRigaNvda: " & ex.Message)
        End Try
    End Sub

    Public Sub FermaTutto()
        Try
            Try
                WGBraille.Ferma()
            Catch exBraille As Exception
                log.ScriviLog("FermaTutto (braille): " & exBraille.Message)
            End Try
            AzzeraParlato()
            watchdogRunning = False
            monitorImpostazioniRunning = False
            masterCts.Cancel()
            ChiudiConnessioneNvda()
            FermaMonitorDeviceAudio()
            Try
                _canaleProntoEvento.Set()
            Catch ex As Exception
                log.ScriviLog("FermaTutto (SbloccoAttese): " & ex.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("FermaTutto: " & ex.Message)
        End Try
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not disposedValue Then
                If disposing Then
                    Try
                        ' Senza questa chiusura restano aperti il processo WGSapi.exe e i suoi canali.
                        ChiudiSapi()
                    Catch ex As Exception
                        log.ScriviLog("Dispose (ChiudiSapi): " & ex.Message)
                    End Try
                    Try
                        _codaComandiSapi.CompleteAdding()
                    Catch ex As Exception
                        log.ScriviLog("Dispose (CodaComandi): " & ex.Message)
                    End Try
                End If
                Try
                    FermaTutto()
                    masterCts.Dispose()
                Catch ex As Exception
                    log.ScriviLog("Dispose (Interne): " & ex.Message)
                End Try
                Try
                    _canaleProntoEvento.Dispose()
                Catch ex As Exception
                    log.ScriviLog("Dispose (EventoCanale): " & ex.Message)
                End Try
                disposedValue = True
            End If
        Catch ex As Exception
            log.ScriviLog("Dispose (disposing): " & ex.Message)
        End Try
    End Sub

    Protected Overrides Sub Finalize()
        Try
            Dispose(False)
        Catch ex As Exception
            log.ScriviLog("Finalize: " & ex.Message)
        Finally
            MyBase.Finalize()
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Dispose(True)
            GC.SuppressFinalize(Me)
        Catch ex As Exception
            log.ScriviLog("Dispose: " & ex.Message)
        End Try
    End Sub
End Class