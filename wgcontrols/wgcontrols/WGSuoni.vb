' ============================================================================================
' WGSuoni - effetti sonori e musiche di attesa di WinGabriel
' ============================================================================================
'
' A cosa serve
'   Raccoglie tutti i suoni non parlati del programma: il "din" di conferma, il fruscio di
'   pagina, gli avvisi, le notifiche, il click dei campi e la musica di attesa. I suoni sono
'   generati via MIDI (sintetizzatore di Windows) tranne l'attesa personalizzata, che e' un file
'   audio scelto dall'utente. Ogni suono parte su un thread suo, quindi le chiamate ritornano
'   subito e non rallentano la vocalizzazione.
'
' Come si usa
'   Dim suoni As New WGSuoni()
'   suoni.ClickClack()                       ' spostamento tra i campi
'   suoni.Attesa()                           ' musica finche' non si chiama StopAttesa
'   suoni.StopAttesa()
'   suoni.FruscioPagina()                    ' cambio pagina
'   WGSuoni.EmettiDin()                      ' segnale breve, non serve l'istanza
'   WGSuoni.VolumeGlobale = 90               ' 0..127
'   suoni.Dispose()                          ' ferma tutto quello che sta suonando
'
' Metodi pubblici principali
'   LinkAttivato / EntrataCampo / UscitaCampo  suoni del browser: clic su un link, entrata e
'                     uscita da un campo.
'   Avviso / Notifica / ClickClack / Attesa / AttesaLunga  avviano un suono; le rispettive
'                     StopXxx lo fermano. Un solo suono per volta: se ce n'e' gia' uno in corso
'                     la chiamata non fa nulla.
'   FruscioPagina     True se il fruscio e' stato avviato (o era gia' in corso), False se i
'                     suoni sono spenti o non e' disponibile il MIDI; al termine scatta l'evento
'                     FruscioPaginaTerminato, che arriva da un thread di sfondo.
'   EmettiDin         segnale breve condiviso, utilizzabile senza creare l'oggetto.
'   VolumeGlobale     volume MIDI 0..127, sempre limitato a questo intervallo.
'   ImpostaSuonoAttesaPersonalizzato  copia il file scelto nella cartella dati e lo imposta come
'                     musica di attesa; False con il motivo in messaggioErrore se non riesce.
'   RipristinaSuonoAttesaPredefinito  torna alle musiche interne.
'   StopAllSounds     ferma tutto (chiamato anche da Dispose).
'
' Errori
'   Nessun metodo solleva eccezioni: gli errori finiscono nel log (dati\logs\log.txt) e la
'   chiamata termina senza suono. Se il MIDI di Windows non e' disponibile il programma continua
'   a funzionare in silenzio; il tentativo di recupero della libreria NAudio viene fatto una
'   volta sola, non a ogni suono.
'
' Attenzione
'   I suoni rispettano l'impostazione "SuoniAttivi" e il volume globale della configurazione.
'   L'evento FruscioPaginaTerminato viene sollevato da un thread di sfondo: chi lo gestisce e
'   deve toccare l'interfaccia grafica deve rientrare nel thread dell'interfaccia da solo.
'   Il file di attesa personalizzato viene copiato nella cartella dati (sottocartella "suoni").
' ============================================================================================
Imports NAudio.Midi
Imports System.Threading
Imports System.Runtime.InteropServices
Imports System.IO
Imports System.Net
Imports System.Collections.Generic
Imports NAudio.Wave
Imports System.Diagnostics
Imports WGDb
Friend Class WGMidi
    Private Shared instance As WGMidi
    Private Shared ReadOnly lockObject As New Object()
    Public ReadOnly midiOut As MidiOut
    Private Shared ReadOnly log As New Logger()
    Private Shared _creazioneFallita As Boolean = False

    Private Sub New()
        Try
            midiOut = New MidiOut(0)
        Catch ex As DllNotFoundException
            Try
                Dim downloadUrl As String = "https://www.wingabriel.net/wingabriel/naudio.dll"
                Dim dllPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NAudio.dll")
                Using webClient As New WebClient()
                    webClient.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36")
                    webClient.DownloadFile(downloadUrl, dllPath)
                End Using
                midiOut = New MidiOut(0)
            Catch downloadEx As Exception
                log.ScriviLog($"WGMidi download: {downloadEx.ToString()}")
                Throw
            End Try
        Catch ex As COMException
            log.ScriviLog($"WGMidi COMException: {ex.ToString()}")
            Throw
        Catch ex As Exception
            log.ScriviLog($"WGMidi: {ex.ToString()}")
            Throw
        End Try
    End Sub

    Public Shared Function GetInstance() As WGMidi
        Try
            If instance IsNot Nothing Then Return instance
            SyncLock lockObject
                If instance IsNot Nothing Then Return instance
                ' Dopo un tentativo fallito non si riprova: la creazione puo' scaricare una dll
                ' dalla rete e bloccherebbe il chiamante a ogni singolo suono.
                If _creazioneFallita Then Return Nothing
                Try
                    instance = New WGMidi()
                Catch exCreazione As Exception
                    _creazioneFallita = True
                    log.ScriviLog($"WGMidi.GetInstance: {exCreazione.ToString()}")
                    Return Nothing
                End Try
                Return instance
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGMidi.GetInstance: {ex.ToString()}")
            Return Nothing
        End Try
    End Function
End Class

Public Class WGSuoni
    Implements IDisposable

    Private ReadOnly midiOut As MidiOut
    Private _isDisposed As Boolean
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _runningSounds As New Dictionary(Of String, CancellationTokenSource)()
    Private Shared ReadOnly _lockObject As New Object()
    Private Shared ReadOnly _config As New WGConfig()
    Private Shared ReadOnly _rng As New Random()
    Private Shared ReadOnly _variantHistory As New List(Of Integer)()
    Private Shared ReadOnly _variantHistoryLock As New Object()
    Private Shared _volumeGlobale As Integer = 100
    Private Shared ReadOnly _dinLock As New Object()
    Private Shared _dinThread As Thread = Nothing
    Private Shared ReadOnly _fruscioLock As New Object()
    Private Shared _fruscioInCorso As Boolean = False

    Public Event FruscioPaginaTerminato()

    Public Shared Property VolumeGlobale As Integer
        Get
            Return _volumeGlobale
        End Get
        Set(value As Integer)
            _volumeGlobale = Math.Max(0, Math.Min(127, value))
        End Set
    End Property

    Private Shared Function SuoniAttivi() As Boolean
        Try
            If _config.EsisteId("SuoniAttivi") Then
                Dim val As Boolean = True
                Boolean.TryParse(_config.LeggiValore("SuoniAttivi"), val)
                Return val
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog($"SuoniAttivi: {ex.ToString()}")
            Return True
        End Try
    End Function

    Private Shared Sub AggiornaConfigurazioneVolume()
        Try
            If _config.EsisteId("VolumeGlobale") Then
                Dim v As Integer = 100
                Integer.TryParse(_config.LeggiValore("VolumeGlobale"), v)
                _volumeGlobale = Math.Max(0, Math.Min(127, v))
            End If
        Catch ex As Exception
            log.ScriviLog($"AggiornaConfigurazioneVolume: {ex.ToString()}")
        End Try
    End Sub

    Private Shared Function ScalaVelocity(v As Integer) As Integer
        Try
            Return Math.Max(1, Math.Min(127, CInt(v * _volumeGlobale / 127.0)))
        Catch ex As Exception
            log.ScriviLog($"ScalaVelocity: {ex.ToString()}")
            Return 1
        End Try
    End Function

    Private Shared Function CartellaSuoni() As String
        Try
            Return WGDb.WGPercorsiApplicazione.CartellaComponente("suoni")
        Catch ex As Exception
            log.ScriviLog($"CartellaSuoni: {ex.ToString()}")
            Return ""
        End Try
    End Function

    Private Const NOME_FILE_ATTESA_PERSONALIZZATA As String = "attesa_personalizzata"

    Public Shared Property SuonoAttesaPersonalizzato As String
        Get
            Try
                Dim percorso As String = _config.LeggiValore("SuonoAttesaPersonalizzato")
                If String.IsNullOrWhiteSpace(percorso) Then Return ""
                If Not File.Exists(percorso) Then Return ""
                Return percorso
            Catch ex As Exception
                log.ScriviLog("SuonoAttesaPersonalizzato Get: " & ex.Message)
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                _config.AggiungiValore("SuonoAttesaPersonalizzato", If(value, ""))
            Catch ex As Exception
                log.ScriviLog("SuonoAttesaPersonalizzato Set: " & ex.Message)
            End Try
        End Set
    End Property

    Private Shared Sub RimuoviCopiaSuonoAttesaEsistente()
        Try
            Dim cartella As String = CartellaSuoni()
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return
            For Each fileEsistente In Directory.GetFiles(cartella, NOME_FILE_ATTESA_PERSONALIZZATA & ".*")
                Try
                    File.Delete(fileEsistente)
                Catch ex As Exception
                    log.ScriviLog("RimuoviCopiaSuonoAttesaEsistente (file '" & fileEsistente & "'): " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("RimuoviCopiaSuonoAttesaEsistente: " & ex.Message)
        End Try
    End Sub

    Public Shared Function ImpostaSuonoAttesaPersonalizzato(percorsoOriginale As String, ByRef messaggioErrore As String) As Boolean
        messaggioErrore = ""
        Try
            If String.IsNullOrWhiteSpace(percorsoOriginale) OrElse Not File.Exists(percorsoOriginale) Then
                messaggioErrore = "Il file selezionato non esiste."
                Return False
            End If

            Dim cartella As String = CartellaSuoni()
            If String.IsNullOrWhiteSpace(cartella) Then
                messaggioErrore = "Non è possibile usare la cartella dei suoni."
                Return False
            End If

            If Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            Dim estensione As String = Path.GetExtension(percorsoOriginale)
            If String.IsNullOrEmpty(estensione) Then estensione = ".mp3"
            Dim percorsoCopia As String = Path.Combine(cartella, NOME_FILE_ATTESA_PERSONALIZZATA & estensione)

            ' Se l'utente riseleziona la copia gia' presente non va cancellata, altrimenti la copia fallisce.
            If String.Equals(Path.GetFullPath(percorsoOriginale), Path.GetFullPath(percorsoCopia), StringComparison.OrdinalIgnoreCase) Then
                SuonoAttesaPersonalizzato = percorsoCopia
                Return True
            End If

            RimuoviCopiaSuonoAttesaEsistente()

            File.Copy(percorsoOriginale, percorsoCopia, True)

            SuonoAttesaPersonalizzato = percorsoCopia
            Return True
        Catch ex As UnauthorizedAccessException
            log.ScriviLog("ImpostaSuonoAttesaPersonalizzato UnauthorizedAccess: " & ex.Message)
            messaggioErrore = "Non è possibile copiare il file: permessi insufficienti."
            Return False
        Catch ex As IOException
            log.ScriviLog("ImpostaSuonoAttesaPersonalizzato IOException: " & ex.Message)
            messaggioErrore = "Non è possibile copiare il file: errore di lettura o scrittura su disco."
            Return False
        Catch ex As Exception
            log.ScriviLog("ImpostaSuonoAttesaPersonalizzato: " & ex.Message)
            messaggioErrore = "Errore imprevisto durante la copia del file: " & ex.Message
            Return False
        End Try
    End Function

    Public Shared Sub RipristinaSuonoAttesaPredefinito()
        Try
            RimuoviCopiaSuonoAttesaEsistente()
            SuonoAttesaPersonalizzato = ""
        Catch ex As Exception
            log.ScriviLog("RipristinaSuonoAttesaPredefinito: " & ex.Message)
        End Try
    End Sub

    Public Sub New()
        Try
            Dim istanzaMidi As WGMidi = WGMidi.GetInstance()
            If istanzaMidi IsNot Nothing Then midiOut = istanzaMidi.midiOut
            AggiornaConfigurazioneVolume()
        Catch ex As Exception
            log.ScriviLog($"WGSuoni costruttore: {ex.ToString()}")
        End Try
    End Sub

    Protected Overrides Sub Finalize()
        Try
            Dispose(False)
        Catch ex As Exception
            log.ScriviLog($"WGSuoni Finalize: {ex.ToString()}")
        Finally
            MyBase.Finalize()
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Dispose(True)
            GC.SuppressFinalize(Me)
        Catch ex As Exception
            log.ScriviLog($"WGSuoni Dispose: {ex.ToString()}")
        End Try
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not _isDisposed Then
                If disposing Then StopAllSounds()
                _isDisposed = True
            End If
        Catch ex As Exception
            log.ScriviLog($"WGSuoni Dispose(disposing): {ex.ToString()}")
        End Try
    End Sub

    Private Shared Sub PreciseSleep(ms As Integer, token As CancellationToken)
        Try
            If ms <= 0 Then Return
            Dim sw As Stopwatch = Stopwatch.StartNew()
            Do While sw.ElapsedMilliseconds < ms
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
                Dim rimasto As Integer = CInt(ms - sw.ElapsedMilliseconds)
                If rimasto > 15 Then
                    token.WaitHandle.WaitOne(10)
                ElseIf rimasto > 1 Then
                    Thread.SpinWait(500)
                End If
            Loop
        Catch ex As Exception
            log.ScriviLog($"PreciseSleep: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SendNote(channel As Integer, note As Integer, velocity As Integer, isOn As Boolean)
        Try
            If midiOut Is Nothing Then Return
            If isOn Then
                midiOut.Send(MidiMessage.StartNote(note, ScalaVelocity(velocity), channel).RawData)
            Else
                midiOut.Send(MidiMessage.StopNote(note, 0, channel).RawData)
            End If
        Catch ex As Exception
            log.ScriviLog($"SendNote: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SendPatch(channel As Integer, patch As Integer)
        Try
            If midiOut Is Nothing Then Return
            midiOut.Send(MidiMessage.ChangePatch(patch, channel).RawData)
        Catch ex As Exception
            log.ScriviLog($"SendPatch: {ex.ToString()}")
        End Try
    End Sub

    Private Sub SendCC(channel As Integer, controller As Integer, value As Integer)
        Try
            If midiOut Is Nothing Then Return
            midiOut.Send(&HB0 Or ((channel - 1) And &HF) Or (controller << 8) Or (value << 16))
        Catch ex As Exception
            log.ScriviLog($"SendCC: {ex.ToString()}")
        End Try
    End Sub

    Private Sub AllNotesOff(ParamArray channels() As Integer)
        Try
            If midiOut Is Nothing Then Return
            For Each ch In channels
                midiOut.Send((&HB0 + ch - 1) Or (123 << 8))
            Next
        Catch ex As Exception
            log.ScriviLog($"AllNotesOff: {ex.ToString()}")
        End Try
    End Sub

    Private Sub StartSound(soundName As String, playAction As Action(Of CancellationToken))
        Try
            If midiOut Is Nothing OrElse _isDisposed OrElse WGUtils.programmadisattivato Then Return
            If playAction Is Nothing OrElse String.IsNullOrEmpty(soundName) Then Return
            If Not SuoniAttivi() Then Return
            AggiornaConfigurazioneVolume()
            SyncLock _lockObject
                If _runningSounds.Count > 0 Then Return
                Dim cts As New CancellationTokenSource()
                _runningSounds.Add(soundName, cts)
                Dim t As New Thread(Sub()
                                        Try
                                            playAction(cts.Token)
                                        Catch ex As Exception
                                            log.ScriviLog($"StartSound {soundName}: {ex.ToString()}")
                                        Finally
                                            Try
                                                SyncLock _lockObject
                                                    If _runningSounds.ContainsKey(soundName) AndAlso _runningSounds(soundName) Is cts Then
                                                        _runningSounds.Remove(soundName)
                                                    End If
                                                    cts.Dispose()
                                                End SyncLock
                                            Catch exPulizia As Exception
                                                log.ScriviLog($"StartSound pulizia {soundName}: {exPulizia.ToString()}")
                                            End Try
                                        End Try
                                    End Sub)
                t.IsBackground = True
                t.Priority = ThreadPriority.Highest
                Try
                    t.Start()
                Catch exAvvio As Exception
                    ' Senza questa pulizia il nome resterebbe in elenco e nessun suono ripartirebbe piu'.
                    log.ScriviLog($"StartSound avvio thread {soundName}: {exAvvio.ToString()}")
                    _runningSounds.Remove(soundName)
                    cts.Dispose()
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"StartSound {soundName}: {ex.ToString()}")
        End Try
    End Sub

    Private Sub StopSound(soundName As String)
        Try
            SyncLock _lockObject
                Dim cts As CancellationTokenSource = Nothing
                If _runningSounds.TryGetValue(soundName, cts) AndAlso cts IsNot Nothing Then
                    cts.Cancel()
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"StopSound {soundName}: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopAllSounds()
        Try
            SyncLock _lockObject
                Try
                    If midiOut IsNot Nothing Then midiOut.Reset()
                Catch ex As Exception
                    log.ScriviLog($"StopAllSounds Reset: {ex.ToString()}")
                End Try
                Dim toCancel = _runningSounds.Values.ToList()
                _runningSounds.Clear()
                For Each cts In toCancel
                    Try
                        ' Solo Cancel: il thread del suono libera da solo il token quando esce.
                        If cts IsNot Nothing Then cts.Cancel()
                    Catch ex As Exception
                        log.ScriviLog($"StopAllSounds cancel: {ex.ToString()}")
                    End Try
                Next
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"StopAllSounds: {ex.ToString()}")
        End Try
    End Sub

    Public Shared Sub EmettiDin()
        Try
            If Not SuoniAttivi() Then Return
            AggiornaConfigurazioneVolume()
            Dim istanzaMidi As WGMidi = WGMidi.GetInstance()
            If istanzaMidi Is Nothing Then Return
            Dim midi As MidiOut = istanzaMidi.midiOut
            If midi Is Nothing Then Return
            SyncLock _dinLock
                If _dinThread IsNot Nothing AndAlso _dinThread.IsAlive Then Return
                _dinThread = New Thread(Sub()
                                            Try
                                                midi.Send(MidiMessage.ChangePatch(9, 1).RawData)
                                                midi.Send(MidiMessage.StartNote(79, ScalaVelocity(112), 1).RawData)
                                                Thread.Sleep(60)
                                                midi.Send(MidiMessage.StopNote(79, 0, 1).RawData)
                                                Thread.Sleep(28)
                                                midi.Send(MidiMessage.StartNote(79, ScalaVelocity(52), 1).RawData)
                                                Thread.Sleep(80)
                                                midi.Send(MidiMessage.StopNote(79, 0, 1).RawData)
                                                Thread.Sleep(18)
                                                midi.Send(MidiMessage.StartNote(84, ScalaVelocity(26), 1).RawData)
                                                Thread.Sleep(100)
                                                midi.Send(MidiMessage.StopNote(84, 0, 1).RawData)
                                            Catch ex As Exception
                                                log.ScriviLog($"EmettiDin thread: {ex.ToString()}")
                                            End Try
                                        End Sub)
                _dinThread.IsBackground = True
                _dinThread.Priority = ThreadPriority.Highest
                _dinThread.Start()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"EmettiDin: {ex.ToString()}")
        End Try
    End Sub

    Public Function FruscioPagina() As Boolean
        Try
            If midiOut Is Nothing OrElse _isDisposed OrElse WGUtils.programmadisattivato Then Return False
            If Not SuoniAttivi() Then Return False
            AggiornaConfigurazioneVolume()

            SyncLock _fruscioLock
                If _fruscioInCorso Then Return True
                _fruscioInCorso = True
            End SyncLock

            Dim t As New Thread(AddressOf PlayFruscioPagina)
            t.IsBackground = True
            t.Priority = ThreadPriority.Highest
            t.Start()
            Return True
        Catch ex As Exception
            log.ScriviLog($"FruscioPagina: {ex.ToString()}")
            SyncLock _fruscioLock
                _fruscioInCorso = False
            End SyncLock
            Return False
        End Try
    End Function

    Public Function FruscioPaginaInRiproduzione() As Boolean
        Try
            SyncLock _fruscioLock
                Return _fruscioInCorso
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"FruscioPaginaInRiproduzione: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub PlayFruscioPagina()
        Try
            Dim token As CancellationToken = CancellationToken.None

            SendPatch(2, 120)
            SendCC(2, 91, 35)
            SendCC(10, 91, 25)

            SendNote(10, 82, ScalaVelocity(55), True)
            PreciseSleep(22, token)
            SendNote(10, 82, 0, False)

            SendNote(10, 69, ScalaVelocity(62), True)
            PreciseSleep(20, token)
            SendNote(10, 69, 0, False)

            SendNote(2, 72, ScalaVelocity(50), True)
            SendNote(10, 82, ScalaVelocity(72), True)
            PreciseSleep(24, token)
            SendNote(10, 82, 0, False)

            SendNote(10, 70, ScalaVelocity(66), True)
            PreciseSleep(22, token)
            SendNote(10, 70, 0, False)
            SendNote(2, 72, 0, False)

            SendNote(2, 76, ScalaVelocity(58), True)
            SendNote(10, 82, ScalaVelocity(85), True)
            PreciseSleep(30, token)
            SendNote(10, 82, 0, False)
            SendNote(2, 76, 0, False)

            PreciseSleep(28, token)

            SendNote(10, 69, ScalaVelocity(48), True)
            PreciseSleep(20, token)
            SendNote(10, 69, 0, False)

            SendNote(10, 36, ScalaVelocity(42), True)
            SendNote(10, 82, ScalaVelocity(38), True)
            PreciseSleep(45, token)
            SendNote(10, 36, 0, False)
            SendNote(10, 82, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayFruscioPagina: {ex.ToString()}")
        Finally
            Try
                AllNotesOff(2, 10)
            Catch ex2 As Exception
                log.ScriviLog($"PlayFruscioPagina AllNotesOff: {ex2.ToString()}")
            End Try
            SyncLock _fruscioLock
                _fruscioInCorso = False
            End SyncLock
            Try
                RaiseEvent FruscioPaginaTerminato()
            Catch ex3 As Exception
                log.ScriviLog($"PlayFruscioPagina evento: {ex3.ToString()}")
            End Try
        End Try
    End Sub

    Public Sub Avviso()
        Try
            StartSound("Avviso", AddressOf PlayAvviso)
        Catch ex As Exception
            log.ScriviLog($"Avviso: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopAvviso()
        Try
            StopSound("Avviso")
        Catch ex As Exception
            log.ScriviLog($"StopAvviso: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayAvviso(token As CancellationToken)
        Try
            SendPatch(1, 14)
            SendCC(1, 91, 80)
            SendCC(1, 93, 60)
            Dim seq As New List(Of Tuple(Of Integer, Integer, Integer))()
            seq.Add(Tuple.Create(72, 110, 180))
            seq.Add(Tuple.Create(76, 105, 180))
            seq.Add(Tuple.Create(79, 100, 180))
            seq.Add(Tuple.Create(84, 127, 350))
            seq.Add(Tuple.Create(79, 90, 180))
            seq.Add(Tuple.Create(76, 80, 180))
            seq.Add(Tuple.Create(72, 70, 300))
            For Each item In seq
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit For
                SendNote(1, item.Item1, item.Item2, True)
                PreciseSleep(item.Item3 - 30, token)
                SendNote(1, item.Item1, 0, False)
                PreciseSleep(30, token)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayAvviso: {ex.ToString()}")
        Finally
            AllNotesOff(1)
        End Try
    End Sub

    Public Sub Notifica()
        Try
            StartSound("Notifica", AddressOf PlayNotifica)
        Catch ex As Exception
            log.ScriviLog($"Notifica: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopNotifica()
        Try
            StopSound("Notifica")
        Catch ex As Exception
            log.ScriviLog($"StopNotifica: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayNotifica(token As CancellationToken)
        Try
            SendPatch(1, 11)
            SendCC(1, 91, 70)
            SendCC(1, 93, 50)
            Dim seq As New List(Of Tuple(Of Integer, Integer, Integer))()
            seq.Add(Tuple.Create(60, 90, 120))
            seq.Add(Tuple.Create(64, 95, 120))
            seq.Add(Tuple.Create(67, 100, 120))
            seq.Add(Tuple.Create(72, 115, 280))
            seq.Add(Tuple.Create(76, 105, 150))
            seq.Add(Tuple.Create(79, 95, 150))
            seq.Add(Tuple.Create(72, 85, 200))
            For Each item In seq
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit For
                SendNote(1, item.Item1, item.Item2, True)
                PreciseSleep(item.Item3 - 25, token)
                SendNote(1, item.Item1, 0, False)
                PreciseSleep(25, token)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayNotifica: {ex.ToString()}")
        Finally
            AllNotesOff(1)
        End Try
    End Sub

    Public Sub ClickClack()
        Try
            StartSound("ClickClack", AddressOf PlayClickClack)
        Catch ex As Exception
            log.ScriviLog($"ClickClack: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopClickClack()
        Try
            StopSound("ClickClack")
        Catch ex As Exception
            log.ScriviLog($"StopClickClack: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayClickClack(token As CancellationToken)
        Try
            SendCC(10, 91, 20)
            SendNote(10, 42, ScalaVelocity(100), True)
            PreciseSleep(25, token)
            SendNote(10, 42, 0, False)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            PreciseSleep(15, token)
            SendNote(10, 38, ScalaVelocity(115), True)
            PreciseSleep(30, token)
            SendNote(10, 38, 0, False)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            PreciseSleep(10, token)
            SendNote(10, 36, ScalaVelocity(70), True)
            PreciseSleep(20, token)
            SendNote(10, 36, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayClickClack: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub TastoPremuto()
        Try
            StartSound("TastoPremuto", AddressOf PlayTastoPremuto)
        Catch ex As Exception
            log.ScriviLog($"TastoPremuto: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopTastoPremuto()
        Try
            StopSound("TastoPremuto")
        Catch ex As Exception
            log.ScriviLog($"StopTastoPremuto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayTastoPremuto(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            SendNote(10, 37, 78, True)
            PreciseSleep(22, token)
            SendNote(10, 37, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayTastoPremuto: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub TastoEsplorato()
        Try
            StartSound("TastoEsplorato", AddressOf PlayTastoEsplorato)
        Catch ex As Exception
            log.ScriviLog($"TastoEsplorato: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopTastoEsplorato()
        Try
            StopSound("TastoEsplorato")
        Catch ex As Exception
            log.ScriviLog($"StopTastoEsplorato: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayTastoEsplorato(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            SendNote(10, 42, 38, True)
            PreciseSleep(14, token)
            SendNote(10, 42, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayTastoEsplorato: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub DoppioTocco()
        Try
            StartSound("DoppioTocco", AddressOf PlayDoppioTocco)
        Catch ex As Exception
            log.ScriviLog($"DoppioTocco: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopDoppioTocco()
        Try
            StopSound("DoppioTocco")
        Catch ex As Exception
            log.ScriviLog($"StopDoppioTocco: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayDoppioTocco(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            Dim seq As New List(Of Tuple(Of Integer, Integer, Integer))()
            seq.Add(Tuple.Create(77, 70, 45))
            seq.Add(Tuple.Create(76, 90, 70))
            For Each item In seq
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit For
                SendNote(10, item.Item1, item.Item2, True)
                PreciseSleep(item.Item3 - 12, token)
                SendNote(10, item.Item1, 0, False)
                PreciseSleep(12, token)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayDoppioTocco: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub FlickAvanti()
        Try
            StartSound("FlickAvanti", AddressOf PlayFlickAvanti)
        Catch ex As Exception
            log.ScriviLog($"FlickAvanti: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopFlickAvanti()
        Try
            StopSound("FlickAvanti")
        Catch ex As Exception
            log.ScriviLog($"StopFlickAvanti: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayFlickAvanti(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            Dim seq As New List(Of Tuple(Of Integer, Integer, Integer))()
            seq.Add(Tuple.Create(77, 55, 32))
            seq.Add(Tuple.Create(76, 68, 45))
            For Each item In seq
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit For
                SendNote(10, item.Item1, item.Item2, True)
                PreciseSleep(item.Item3 - 10, token)
                SendNote(10, item.Item1, 0, False)
                PreciseSleep(10, token)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayFlickAvanti: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub FlickIndietro()
        Try
            StartSound("FlickIndietro", AddressOf PlayFlickIndietro)
        Catch ex As Exception
            log.ScriviLog($"FlickIndietro: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopFlickIndietro()
        Try
            StopSound("FlickIndietro")
        Catch ex As Exception
            log.ScriviLog($"StopFlickIndietro: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayFlickIndietro(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            Dim seq As New List(Of Tuple(Of Integer, Integer, Integer))()
            seq.Add(Tuple.Create(76, 68, 32))
            seq.Add(Tuple.Create(77, 55, 45))
            For Each item In seq
                If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit For
                SendNote(10, item.Item1, item.Item2, True)
                PreciseSleep(item.Item3 - 10, token)
                SendNote(10, item.Item1, 0, False)
                PreciseSleep(10, token)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayFlickIndietro: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    Public Sub LimiteRaggiunto()
        Try
            StartSound("LimiteRaggiunto", AddressOf PlayLimiteRaggiunto)
        Catch ex As Exception
            log.ScriviLog($"LimiteRaggiunto: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopLimiteRaggiunto()
        Try
            StopSound("LimiteRaggiunto")
        Catch ex As Exception
            log.ScriviLog($"StopLimiteRaggiunto: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayLimiteRaggiunto(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            SendNote(10, 41, 72, True)
            PreciseSleep(55, token)
            SendNote(10, 41, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayLimiteRaggiunto: {ex.ToString()}")
        Finally
            AllNotesOff(10)
        End Try
    End Sub

    ' Clic su un link nel browser: un colpo secco di legnetti seguito da una nota breve e chiara di xilofono.
    Public Sub LinkAttivato()
        Try
            StartSound("LinkAttivato", AddressOf PlayLinkAttivato)
        Catch ex As Exception
            log.ScriviLog($"LinkAttivato: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopLinkAttivato()
        Try
            StopSound("LinkAttivato")
        Catch ex As Exception
            log.ScriviLog($"StopLinkAttivato: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayLinkAttivato(token As CancellationToken)
        Try
            SendCC(10, 91, 0)
            SendCC(10, 93, 0)
            SendPatch(3, 13)
            SendCC(3, 91, 12)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            SendNote(10, 75, 100, True)
            PreciseSleep(20, token)
            SendNote(10, 75, 0, False)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            PreciseSleep(10, token)
            SendNote(3, 88, 70, True)
            PreciseSleep(60, token)
            SendNote(3, 88, 0, False)
        Catch ex As Exception
            log.ScriviLog($"PlayLinkAttivato: {ex.ToString()}")
        Finally
            AllNotesOff(3, 10)
        End Try
    End Sub

    ' Entrata in un campo di modifica o in una casella combinata: due note di vibrafono che salgono.
    Public Sub EntrataCampo()
        Try
            StartSound("EntrataCampo", AddressOf PlayEntrataCampo)
        Catch ex As Exception
            log.ScriviLog($"EntrataCampo: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopEntrataCampo()
        Try
            StopSound("EntrataCampo")
        Catch ex As Exception
            log.ScriviLog($"StopEntrataCampo: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayEntrataCampo(token As CancellationToken)
        Try
            SuonaDueNoteCampo(72, 79, token)
        Catch ex As Exception
            log.ScriviLog($"PlayEntrataCampo: {ex.ToString()}")
        Finally
            AllNotesOff(3)
        End Try
    End Sub

    ' Uscita dal campo: le stesse due note in discesa.
    Public Sub UscitaCampo()
        Try
            StartSound("UscitaCampo", AddressOf PlayUscitaCampo)
        Catch ex As Exception
            log.ScriviLog($"UscitaCampo: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopUscitaCampo()
        Try
            StopSound("UscitaCampo")
        Catch ex As Exception
            log.ScriviLog($"StopUscitaCampo: {ex.ToString()}")
        End Try
    End Sub

    Private Sub PlayUscitaCampo(token As CancellationToken)
        Try
            SuonaDueNoteCampo(79, 72, token)
        Catch ex As Exception
            log.ScriviLog($"PlayUscitaCampo: {ex.ToString()}")
        Finally
            AllNotesOff(3)
        End Try
    End Sub

    Private Sub SuonaDueNoteCampo(prima As Integer, seconda As Integer, token As CancellationToken)
        Try
            SendPatch(3, 11)
            SendCC(3, 91, 18)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            SendNote(3, prima, 72, True)
            PreciseSleep(55, token)
            SendNote(3, prima, 0, False)
            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            PreciseSleep(8, token)
            SendNote(3, seconda, 86, True)
            PreciseSleep(75, token)
            SendNote(3, seconda, 0, False)
        Catch ex As Exception
            log.ScriviLog($"SuonaDueNoteCampo: {ex.ToString()}")
        End Try
    End Sub

    Public Sub Attesa()
        Try
            StartSound("Attesa", AddressOf PlayAttesaCore)
        Catch ex As Exception
            log.ScriviLog($"Attesa: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopAttesa()
        Try
            StopSound("Attesa")
        Catch ex As Exception
            log.ScriviLog($"StopAttesa: {ex.ToString()}")
        End Try
    End Sub

    Public Sub AttesaLunga()
        Try
            StartSound("AttesaLunga", AddressOf PlayAttesaCore)
        Catch ex As Exception
            log.ScriviLog($"AttesaLunga: {ex.ToString()}")
        End Try
    End Sub

    Public Sub StopAttesaLunga()
        Try
            StopSound("AttesaLunga")
        Catch ex As Exception
            log.ScriviLog($"StopAttesaLunga: {ex.ToString()}")
        End Try
    End Sub

    Private Class NoteEvent
        Public Beat As Double
        Public Note As Integer
        Public Velocity As Integer
        Public DurationBeats As Double
        Public Channel As Integer
        Public Sub New(b As Double, n As Integer, v As Integer, d As Double, ch As Integer)
            Beat = b : Note = n : Velocity = v : DurationBeats = d : Channel = ch
        End Sub
    End Class

    Private Class DrumEvent
        Public Beat As Double
        Public Note As Integer
        Public Velocity As Integer
        Public Sub New(b As Double, n As Integer, v As Integer)
            Beat = b : Note = n : Velocity = v
        End Sub
    End Class

    Private Class VariantDef
        Public Bpm As Integer
        Public TotalBeats As Double
        Public Melody As List(Of NoteEvent)
        Public Drums As List(Of DrumEvent)
        Public Patches As Dictionary(Of Integer, Integer)
        Public CCs As Dictionary(Of Integer, Integer())
        Public Sub New()
            Melody = New List(Of NoteEvent)()
            Drums = New List(Of DrumEvent)()
        End Sub
    End Class

    Private Function ScegliVarianteNonRipetuta() As Integer
        Try
            SyncLock _variantHistoryLock
                Dim totale As Integer = 50
                Dim maxHistory As Integer = Math.Min(25, totale - 1)
                Dim candidati As New List(Of Integer)()
                For i As Integer = 0 To totale - 1
                    If Not _variantHistory.Contains(i) Then candidati.Add(i)
                Next
                If candidati.Count = 0 Then
                    _variantHistory.Clear()
                    For i As Integer = 0 To totale - 1
                        candidati.Add(i)
                    Next
                End If
                If candidati.Count = 0 Then Return 0
                Dim scelta As Integer = candidati(_rng.Next(candidati.Count))
                _variantHistory.Add(scelta)
                If _variantHistory.Count > maxHistory Then _variantHistory.RemoveAt(0)
                Return scelta
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"ScegliVarianteNonRipetuta: {ex.ToString()}")
            Return 0
        End Try
    End Function

    Private Sub PlayAttesaCore(token As CancellationToken)
        Try
            Dim percorsoPersonalizzato As String = SuonoAttesaPersonalizzato
            If Not String.IsNullOrWhiteSpace(percorsoPersonalizzato) Then
                Dim riuscito As Boolean = PlayAttesaMp3(percorsoPersonalizzato, token)
                If riuscito OrElse token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Return
            End If

            Do While Not token.IsCancellationRequested AndAlso Not WGUtils.programmadisattivato
                Dim idx As Integer = ScegliVarianteNonRipetuta()
                Dim v As VariantDef = CreaVariante(idx)
                If v Is Nothing Then
                    PreciseSleep(500, token)
                Else
                    PlayVariant(v, token)
                End If
                If Not token.IsCancellationRequested AndAlso Not WGUtils.programmadisattivato Then
                    PreciseSleep(600, token)
                End If
            Loop
        Catch ex As Exception
            log.ScriviLog($"PlayAttesaCore: {ex.ToString()}")
        Finally
            AllNotesOff(1, 2, 3, 4, 10)
        End Try
    End Sub

    Private Function PlayAttesaMp3(percorso As String, token As CancellationToken) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                Return False
            End If

            Using audioFile As New AudioFileReader(percorso)
                audioFile.Volume = CSng(Math.Max(0.0, Math.Min(1.0, _volumeGlobale / 127.0)))
                Using output As New WaveOutEvent()
                    output.Init(audioFile)
                    Do While Not token.IsCancellationRequested AndAlso Not WGUtils.programmadisattivato
                        output.Play()
                        Do While output.PlaybackState = PlaybackState.Playing
                            If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then
                                output.Stop()
                                Exit Do
                            End If
                            token.WaitHandle.WaitOne(50)
                        Loop
                        If token.IsCancellationRequested OrElse WGUtils.programmadisattivato Then Exit Do
                        audioFile.Position = 0
                        ' Se la riproduzione non parte, senza questa pausa il ciclo girerebbe a vuoto occupando la CPU.
                        token.WaitHandle.WaitOne(50)
                    Loop
                    output.Stop()
                End Using
            End Using
            Return True
        Catch ex As Exception
            log.ScriviLog($"PlayAttesaMp3: file personalizzato non riproducibile ('{percorso}'), ricado sul suono predefinito. Errore: {ex.ToString()}")
            Return False
        End Try
    End Function

    Private Sub PlayVariant(v As VariantDef, token As CancellationToken)
        Try
            ' Senza queste verifiche msPerBeat diventa infinito e il ciclo non finirebbe mai.
            If v Is Nothing OrElse v.Bpm <= 0 OrElse v.TotalBeats <= 0.0R Then Return
            If v.Melody Is Nothing OrElse v.Drums Is Nothing Then Return
            If v.Patches IsNot Nothing Then
                For Each kv In v.Patches
                    SendPatch(kv.Key, kv.Value)
                Next
            End If
            If v.CCs IsNot Nothing Then
                For Each kv In v.CCs
                    For Each cc In kv.Value
                        SendCC(kv.Key, cc And &HFF, (cc >> 8) And &HFF)
                    Next
                Next
            End If

            Dim msPerBeat As Double = 60000.0R / v.Bpm
            Dim totalMs As Double = v.TotalBeats * msPerBeat
            Dim melody As List(Of NoteEvent) = v.Melody
            Dim drumsL As List(Of DrumEvent) = v.Drums
            melody.Sort(Function(a, b) a.Beat.CompareTo(b.Beat))
            drumsL.Sort(Function(a, b) a.Beat.CompareTo(b.Beat))

            Dim activeNotes As New List(Of Tuple(Of Double, Integer, Integer))()
            Dim noteIdx As Integer = 0
            Dim drumIdx As Integer = 0
            Dim sw As Stopwatch = Stopwatch.StartNew()

            Do While Not token.IsCancellationRequested AndAlso Not WGUtils.programmadisattivato
                Dim elapsedMs As Double = sw.Elapsed.TotalMilliseconds
                If elapsedMs >= totalMs Then Exit Do
                Dim elapsedBeats As Double = elapsedMs / msPerBeat

                Do While noteIdx < melody.Count AndAlso melody(noteIdx).Beat <= elapsedBeats
                    Dim ne As NoteEvent = melody(noteIdx)
                    SendNote(ne.Channel, ne.Note, ne.Velocity, True)
                    Dim offMs As Double = sw.Elapsed.TotalMilliseconds + ne.DurationBeats * msPerBeat * 0.9R
                    activeNotes.Add(Tuple.Create(offMs, ne.Note, ne.Channel))
                    noteIdx += 1
                Loop

                Do While drumIdx < drumsL.Count AndAlso drumsL(drumIdx).Beat <= elapsedBeats
                    Dim de As DrumEvent = drumsL(drumIdx)
                    SendNote(10, de.Note, de.Velocity, True)
                    Dim offMs As Double = sw.Elapsed.TotalMilliseconds + 40.0R
                    activeNotes.Add(Tuple.Create(offMs, de.Note, 10))
                    drumIdx += 1
                Loop

                Dim now As Double = sw.Elapsed.TotalMilliseconds
                Dim toRemove As New List(Of Tuple(Of Double, Integer, Integer))()
                For Each an In activeNotes
                    If now >= an.Item1 Then
                        SendNote(an.Item3, an.Item2, 0, False)
                        toRemove.Add(an)
                    End If
                Next
                For Each r In toRemove
                    activeNotes.Remove(r)
                Next

                Dim nextEventMs As Double = totalMs
                If noteIdx < melody.Count Then
                    nextEventMs = Math.Min(nextEventMs, melody(noteIdx).Beat * msPerBeat)
                End If
                If drumIdx < drumsL.Count Then
                    nextEventMs = Math.Min(nextEventMs, drumsL(drumIdx).Beat * msPerBeat)
                End If
                For Each an In activeNotes
                    nextEventMs = Math.Min(nextEventMs, an.Item1)
                Next

                Dim sleepMs As Integer = CInt(Math.Max(1, nextEventMs - sw.Elapsed.TotalMilliseconds - 2.0R))
                If sleepMs > 1 Then
                    token.WaitHandle.WaitOne(Math.Min(sleepMs, 15))
                End If
            Loop

            For Each an In activeNotes
                SendNote(an.Item3, an.Item2, 0, False)
            Next
        Catch ex As Exception
            log.ScriviLog($"PlayVariant: {ex.ToString()}")
        Finally
            AllNotesOff(1, 2, 3, 4, 10)
        End Try
    End Sub

    Private Function CreaVariante(idx As Integer) As VariantDef
        Try
            Select Case idx
                Case 0 To 9 : Return CreaJazz(idx)
                Case 10 To 19 : Return CreaEpico(idx - 10)
                Case 20 To 29 : Return CreaElettronico(idx - 20)
                Case 30 To 39 : Return CreaLatino(idx - 30)
                Case 40 To 49 : Return CreaAmbient(idx - 40)
                Case Else : Return CreaJazz(0)
            End Select
        Catch ex As Exception
            log.ScriviLog($"CreaVariante ({idx}): {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Private Function MkPatches(ParamArray pairs() As Integer) As Dictionary(Of Integer, Integer)
        Dim d As New Dictionary(Of Integer, Integer)()
        Try
            If pairs Is Nothing Then Return d
            Dim i As Integer = 0
            Do While i < pairs.Length - 1
                d(pairs(i)) = pairs(i + 1)
                i += 2
            Loop
        Catch ex As Exception
            log.ScriviLog($"MkPatches: {ex.ToString()}")
        End Try
        Return d
    End Function

    Private Function MkCC2(ch1 As Integer, v1() As Integer, ch2 As Integer, v2() As Integer) As Dictionary(Of Integer, Integer())
        Dim d As New Dictionary(Of Integer, Integer())()
        Try
            d(ch1) = v1
            d(ch2) = v2
        Catch ex As Exception
            log.ScriviLog($"MkCC2: {ex.ToString()}")
        End Try
        Return d
    End Function

    Private Sub A(m As List(Of NoteEvent), b As Double, n As Integer, v As Integer, d As Double, ch As Integer)
        Try
            If m Is Nothing Then Return
            m.Add(New NoteEvent(b, n, v, d, ch))
        Catch ex As Exception
            log.ScriviLog($"A (nota {n}): {ex.ToString()}")
        End Try
    End Sub

    Private Sub D(drums As List(Of DrumEvent), b As Double, n As Integer, v As Integer)
        Try
            If drums Is Nothing Then Return
            drums.Add(New DrumEvent(b, n, v))
        Catch ex As Exception
            log.ScriviLog($"D (percussione {n}): {ex.ToString()}")
        End Try
    End Sub

    Private Sub BuildDrumLoop(drums As List(Of DrumEvent), loopBeats As Double, totalBeats As Double,
                               kick() As Double, snare() As Double, hat() As Double,
                               Optional crash() As Double = Nothing)
        Try
            If drums Is Nothing OrElse loopBeats <= 0.0R OrElse totalBeats <= 0.0R Then Return
            If kick Is Nothing OrElse snare Is Nothing OrElse hat Is Nothing Then Return
            Dim b As Double = 0.0R
            Do While b < totalBeats
                For Each k In kick
                    If b + k < totalBeats Then drums.Add(New DrumEvent(b + k, 36, 115))
                Next
                For Each s In snare
                    If b + s < totalBeats Then drums.Add(New DrumEvent(b + s, 38, 100))
                Next
                For Each h In hat
                    If b + h < totalBeats Then drums.Add(New DrumEvent(b + h, 42, 75))
                Next
                If crash IsNot Nothing Then
                    For Each c In crash
                        If b + c < totalBeats Then drums.Add(New DrumEvent(b + c, 49, 110))
                    Next
                End If
                b += loopBeats
            Loop
        Catch ex As Exception
            log.ScriviLog($"BuildDrumLoop: {ex.ToString()}")
        End Try
    End Sub

    Private Sub RepeatMelody(src As List(Of NoteEvent), dst As List(Of NoteEvent), totalBeats As Double)
        Try
            If src Is Nothing OrElse dst Is Nothing OrElse src.Count = 0 OrElse totalBeats <= 0.0R Then Return
            Dim last As Double = 0.0R
            For Each ne In src
                last = Math.Max(last, ne.Beat + ne.DurationBeats)
            Next
            Dim loopLen As Double = Math.Ceiling(last / 4.0R) * 4.0R
            If loopLen <= 0.0R Then loopLen = 4.0R
            Dim offset As Double = 0.0R
            Do While offset < totalBeats
                For Each ne In src
                    Dim nb As Double = offset + ne.Beat
                    If nb < totalBeats Then
                        dst.Add(New NoteEvent(nb, ne.Note, ne.Velocity, ne.DurationBeats, ne.Channel))
                    End If
                Next
                offset += loopLen
            Loop
        Catch ex As Exception
            log.ScriviLog($"RepeatMelody: {ex.ToString()}")
        End Try
    End Sub

    Private Function CreaJazz(sub_idx As Integer) As VariantDef
        Dim v As New VariantDef()
        v.TotalBeats = 64.0R
        Dim src As New List(Of NoteEvent)()
        Select Case sub_idx
            Case 0
                v.Bpm = 132
                v.Patches = MkPatches(1, 0, 2, 33, 3, 26)
                v.CCs = MkCC2(1, New Integer() {91 Or (50 << 8), 93 Or (30 << 8)}, 3, New Integer() {91 Or (40 << 8)})
                A(src, 0.0R, 64, 90, 0.4R, 1) : A(src, 0.5R, 67, 85, 0.4R, 1) : A(src, 1.0R, 69, 88, 0.4R, 1) : A(src, 1.5R, 71, 92, 0.4R, 1)
                A(src, 2.0R, 72, 95, 0.7R, 1) : A(src, 3.0R, 71, 88, 0.4R, 1) : A(src, 3.5R, 69, 85, 0.4R, 1)
                A(src, 4.0R, 67, 88, 0.4R, 1) : A(src, 4.5R, 64, 82, 0.4R, 1) : A(src, 5.0R, 62, 85, 0.4R, 1) : A(src, 5.5R, 60, 88, 0.4R, 1)
                A(src, 6.0R, 64, 92, 0.7R, 1) : A(src, 7.0R, 67, 88, 0.4R, 1) : A(src, 7.5R, 69, 85, 0.4R, 1)
                A(src, 0.0R, 48, 75, 0.35R, 2) : A(src, 1.0R, 52, 70, 0.35R, 2) : A(src, 2.0R, 55, 72, 0.35R, 2) : A(src, 3.0R, 52, 68, 0.35R, 2)
                A(src, 4.0R, 50, 72, 0.35R, 2) : A(src, 5.0R, 48, 70, 0.35R, 2) : A(src, 6.0R, 45, 72, 0.35R, 2) : A(src, 7.0R, 48, 70, 0.35R, 2)
                A(src, 0.0R, 36, 65, 0.3R, 3) : A(src, 2.0R, 40, 62, 0.3R, 3) : A(src, 4.0R, 36, 65, 0.3R, 3) : A(src, 6.0R, 40, 62, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {1.0R, 2.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R}, New Double() {0.0R})
            Case 1
                v.Bpm = 120
                v.Patches = MkPatches(1, 4, 2, 33, 3, 25)
                v.CCs = MkCC2(1, New Integer() {91 Or (55 << 8)}, 3, New Integer() {91 Or (35 << 8)})
                A(src, 0.0R, 69, 88, 0.45R, 1) : A(src, 0.67R, 71, 84, 0.45R, 1) : A(src, 1.33R, 72, 90, 0.45R, 1) : A(src, 2.0R, 74, 94, 0.45R, 1)
                A(src, 2.67R, 72, 88, 0.45R, 1) : A(src, 3.33R, 71, 84, 0.45R, 1)
                A(src, 4.0R, 69, 88, 0.45R, 1) : A(src, 4.67R, 67, 82, 0.45R, 1) : A(src, 5.33R, 65, 85, 0.45R, 1) : A(src, 6.0R, 64, 90, 0.45R, 1)
                A(src, 6.67R, 65, 85, 0.45R, 1) : A(src, 7.33R, 67, 82, 0.45R, 1)
                A(src, 0.0R, 45, 72, 0.35R, 2) : A(src, 1.0R, 48, 68, 0.35R, 2) : A(src, 2.0R, 52, 72, 0.35R, 2) : A(src, 3.0R, 55, 70, 0.35R, 2)
                A(src, 4.0R, 53, 72, 0.35R, 2) : A(src, 5.0R, 50, 68, 0.35R, 2) : A(src, 6.0R, 48, 72, 0.35R, 2) : A(src, 7.0R, 45, 70, 0.35R, 2)
                A(src, 0.0R, 33, 68, 0.3R, 3) : A(src, 2.0R, 36, 65, 0.3R, 3) : A(src, 4.0R, 38, 68, 0.3R, 3) : A(src, 6.0R, 36, 65, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 2
                v.Bpm = 144
                v.Patches = MkPatches(1, 1, 2, 35, 3, 26)
                v.CCs = MkCC2(1, New Integer() {91 Or (45 << 8), 93 Or (25 << 8)}, 2, New Integer() {91 Or (50 << 8)})
                A(src, 0.0R, 60, 92, 0.35R, 1) : A(src, 0.33R, 62, 88, 0.35R, 1) : A(src, 0.67R, 64, 92, 0.35R, 1)
                A(src, 1.0R, 65, 96, 0.65R, 1) : A(src, 1.67R, 64, 88, 0.35R, 1) : A(src, 2.0R, 62, 85, 0.35R, 1) : A(src, 2.33R, 60, 88, 0.35R, 1)
                A(src, 2.67R, 59, 85, 0.35R, 1) : A(src, 3.0R, 60, 92, 0.65R, 1) : A(src, 3.67R, 62, 88, 0.35R, 1)
                A(src, 4.0R, 64, 92, 0.35R, 1) : A(src, 4.33R, 65, 88, 0.35R, 1) : A(src, 4.67R, 67, 92, 0.35R, 1)
                A(src, 5.0R, 69, 96, 0.65R, 1) : A(src, 5.67R, 67, 88, 0.35R, 1) : A(src, 6.0R, 65, 85, 0.35R, 1) : A(src, 6.33R, 64, 88, 0.35R, 1)
                A(src, 6.67R, 62, 85, 0.35R, 1) : A(src, 7.0R, 60, 92, 0.95R, 1)
                A(src, 0.0R, 48, 72, 0.35R, 2) : A(src, 1.0R, 52, 68, 0.35R, 2) : A(src, 2.0R, 55, 72, 0.35R, 2) : A(src, 3.0R, 52, 70, 0.35R, 2)
                A(src, 4.0R, 50, 72, 0.35R, 2) : A(src, 5.0R, 48, 68, 0.35R, 2) : A(src, 6.0R, 45, 72, 0.35R, 2) : A(src, 7.0R, 48, 70, 0.35R, 2)
                A(src, 0.0R, 36, 65, 0.3R, 3) : A(src, 2.0R, 40, 62, 0.3R, 3) : A(src, 4.0R, 36, 65, 0.3R, 3) : A(src, 6.0R, 40, 62, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 1.5R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.33R, 0.67R, 1.0R, 1.33R, 1.67R, 2.0R, 2.33R, 2.67R, 3.0R, 3.33R, 3.67R})
            Case 3
                v.Bpm = 126
                v.Patches = MkPatches(1, 0, 2, 33, 3, 24)
                v.CCs = MkCC2(1, New Integer() {91 Or (48 << 8)}, 3, New Integer() {91 Or (38 << 8)})
                A(src, 0.0R, 67, 90, 0.45R, 1) : A(src, 0.5R, 69, 86, 0.45R, 1) : A(src, 1.0R, 71, 90, 0.45R, 1) : A(src, 1.5R, 72, 94, 0.45R, 1)
                A(src, 2.0R, 74, 96, 0.9R, 1) : A(src, 3.0R, 72, 88, 0.45R, 1) : A(src, 3.5R, 71, 85, 0.45R, 1)
                A(src, 4.0R, 69, 90, 0.45R, 1) : A(src, 4.5R, 67, 86, 0.45R, 1) : A(src, 5.0R, 65, 88, 0.45R, 1) : A(src, 5.5R, 64, 92, 0.45R, 1)
                A(src, 6.0R, 65, 94, 0.9R, 1) : A(src, 7.0R, 67, 88, 0.45R, 1) : A(src, 7.5R, 69, 85, 0.45R, 1)
                A(src, 0.0R, 43, 74, 0.4R, 2) : A(src, 1.0R, 47, 70, 0.4R, 2) : A(src, 2.0R, 50, 74, 0.4R, 2) : A(src, 3.0R, 47, 70, 0.4R, 2)
                A(src, 4.0R, 45, 74, 0.4R, 2) : A(src, 5.0R, 43, 70, 0.4R, 2) : A(src, 6.0R, 40, 74, 0.4R, 2) : A(src, 7.0R, 43, 70, 0.4R, 2)
                A(src, 0.0R, 31, 66, 0.35R, 3) : A(src, 2.0R, 35, 62, 0.35R, 3) : A(src, 4.0R, 36, 66, 0.35R, 3) : A(src, 6.0R, 35, 62, 0.35R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R}, New Double() {0.0R})
            Case 4
                v.Bpm = 138
                v.Patches = MkPatches(1, 7, 2, 35, 3, 25)
                v.CCs = MkCC2(1, New Integer() {91 Or (52 << 8), 93 Or (28 << 8)}, 2, New Integer() {91 Or (45 << 8)})
                A(src, 0.0R, 72, 88, 0.4R, 1) : A(src, 0.5R, 74, 84, 0.4R, 1) : A(src, 1.0R, 76, 90, 0.4R, 1) : A(src, 1.5R, 77, 94, 0.4R, 1)
                A(src, 2.0R, 76, 88, 0.4R, 1) : A(src, 2.5R, 74, 84, 0.4R, 1) : A(src, 3.0R, 72, 88, 0.4R, 1) : A(src, 3.5R, 71, 84, 0.4R, 1)
                A(src, 4.0R, 69, 90, 0.4R, 1) : A(src, 4.5R, 71, 86, 0.4R, 1) : A(src, 5.0R, 72, 90, 0.4R, 1) : A(src, 5.5R, 74, 94, 0.4R, 1)
                A(src, 6.0R, 72, 88, 0.9R, 1) : A(src, 7.0R, 71, 84, 0.4R, 1) : A(src, 7.5R, 69, 82, 0.4R, 1)
                A(src, 0.0R, 48, 70, 0.38R, 2) : A(src, 1.0R, 52, 66, 0.38R, 2) : A(src, 2.0R, 55, 70, 0.38R, 2) : A(src, 3.0R, 52, 68, 0.38R, 2)
                A(src, 4.0R, 50, 70, 0.38R, 2) : A(src, 5.0R, 48, 66, 0.38R, 2) : A(src, 6.0R, 45, 70, 0.38R, 2) : A(src, 7.0R, 48, 68, 0.38R, 2)
                A(src, 0.0R, 36, 64, 0.3R, 3) : A(src, 2.0R, 38, 60, 0.3R, 3) : A(src, 4.0R, 36, 64, 0.3R, 3) : A(src, 6.0R, 38, 60, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 5
                v.Bpm = 116
                v.Patches = MkPatches(1, 4, 2, 33, 3, 26)
                v.CCs = MkCC2(1, New Integer() {91 Or (42 << 8)}, 3, New Integer() {91 Or (36 << 8)})
                A(src, 0.0R, 65, 86, 0.45R, 1) : A(src, 0.5R, 67, 82, 0.45R, 1) : A(src, 1.0R, 69, 86, 0.45R, 1) : A(src, 1.5R, 71, 90, 0.45R, 1)
                A(src, 2.0R, 72, 94, 0.45R, 1) : A(src, 2.5R, 71, 88, 0.45R, 1) : A(src, 3.0R, 69, 85, 0.45R, 1) : A(src, 3.5R, 67, 82, 0.45R, 1)
                A(src, 4.0R, 65, 86, 0.45R, 1) : A(src, 4.5R, 64, 82, 0.45R, 1) : A(src, 5.0R, 62, 85, 0.45R, 1) : A(src, 5.5R, 60, 88, 0.45R, 1)
                A(src, 6.0R, 62, 90, 0.45R, 1) : A(src, 6.5R, 64, 86, 0.45R, 1) : A(src, 7.0R, 65, 88, 0.9R, 1)
                A(src, 0.0R, 41, 72, 0.4R, 2) : A(src, 1.0R, 45, 68, 0.4R, 2) : A(src, 2.0R, 48, 72, 0.4R, 2) : A(src, 3.0R, 45, 70, 0.4R, 2)
                A(src, 4.0R, 43, 72, 0.4R, 2) : A(src, 5.0R, 41, 68, 0.4R, 2) : A(src, 6.0R, 38, 72, 0.4R, 2) : A(src, 7.0R, 41, 70, 0.4R, 2)
                A(src, 0.0R, 29, 64, 0.3R, 3) : A(src, 2.0R, 33, 60, 0.3R, 3) : A(src, 4.0R, 34, 64, 0.3R, 3) : A(src, 6.0R, 33, 60, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 6
                v.Bpm = 128
                v.Patches = MkPatches(1, 1, 2, 35, 3, 24)
                A(src, 0.0R, 60, 90, 0.33R, 1) : A(src, 0.33R, 64, 86, 0.33R, 1) : A(src, 0.67R, 67, 90, 0.33R, 1)
                A(src, 1.0R, 69, 94, 0.33R, 1) : A(src, 1.33R, 67, 88, 0.33R, 1) : A(src, 1.67R, 64, 85, 0.33R, 1)
                A(src, 2.0R, 60, 90, 0.33R, 1) : A(src, 2.33R, 62, 86, 0.33R, 1) : A(src, 2.67R, 65, 90, 0.33R, 1)
                A(src, 3.0R, 67, 94, 0.33R, 1) : A(src, 3.33R, 65, 88, 0.33R, 1) : A(src, 3.67R, 62, 85, 0.33R, 1)
                A(src, 4.0R, 64, 90, 0.33R, 1) : A(src, 4.33R, 67, 86, 0.33R, 1) : A(src, 4.67R, 71, 90, 0.33R, 1)
                A(src, 5.0R, 72, 94, 0.33R, 1) : A(src, 5.33R, 71, 88, 0.33R, 1) : A(src, 5.67R, 67, 85, 0.33R, 1)
                A(src, 6.0R, 65, 90, 0.33R, 1) : A(src, 6.33R, 67, 86, 0.33R, 1) : A(src, 6.67R, 69, 90, 0.33R, 1)
                A(src, 7.0R, 67, 94, 0.95R, 1)
                A(src, 0.0R, 48, 70, 0.35R, 2) : A(src, 1.0R, 52, 66, 0.35R, 2) : A(src, 2.0R, 55, 70, 0.35R, 2) : A(src, 3.0R, 52, 68, 0.35R, 2)
                A(src, 4.0R, 50, 70, 0.35R, 2) : A(src, 5.0R, 48, 66, 0.35R, 2) : A(src, 6.0R, 45, 70, 0.35R, 2) : A(src, 7.0R, 48, 68, 0.35R, 2)
                A(src, 0.0R, 36, 64, 0.3R, 3) : A(src, 2.0R, 40, 60, 0.3R, 3) : A(src, 4.0R, 36, 64, 0.3R, 3) : A(src, 6.0R, 40, 60, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.33R, 0.67R, 1.0R, 1.33R, 1.67R, 2.0R, 2.33R, 2.67R, 3.0R, 3.33R, 3.67R})
            Case 7
                v.Bpm = 140
                v.Patches = MkPatches(1, 0, 2, 33, 3, 25)
                A(src, 0.0R, 76, 90, 0.45R, 1) : A(src, 0.5R, 74, 86, 0.45R, 1) : A(src, 1.0R, 72, 90, 0.45R, 1) : A(src, 1.5R, 71, 86, 0.45R, 1)
                A(src, 2.0R, 69, 94, 0.45R, 1) : A(src, 2.5R, 71, 88, 0.45R, 1) : A(src, 3.0R, 72, 90, 0.45R, 1) : A(src, 3.5R, 74, 86, 0.45R, 1)
                A(src, 4.0R, 76, 90, 0.45R, 1) : A(src, 4.5R, 77, 94, 0.45R, 1) : A(src, 5.0R, 79, 96, 0.45R, 1) : A(src, 5.5R, 77, 90, 0.45R, 1)
                A(src, 6.0R, 76, 92, 0.45R, 1) : A(src, 6.5R, 74, 88, 0.45R, 1) : A(src, 7.0R, 72, 90, 0.9R, 1)
                A(src, 0.0R, 52, 72, 0.4R, 2) : A(src, 1.0R, 55, 68, 0.4R, 2) : A(src, 2.0R, 57, 72, 0.4R, 2) : A(src, 3.0R, 55, 70, 0.4R, 2)
                A(src, 4.0R, 53, 72, 0.4R, 2) : A(src, 5.0R, 52, 68, 0.4R, 2) : A(src, 6.0R, 50, 72, 0.4R, 2) : A(src, 7.0R, 52, 70, 0.4R, 2)
                A(src, 0.0R, 40, 66, 0.3R, 3) : A(src, 2.0R, 43, 62, 0.3R, 3) : A(src, 4.0R, 40, 66, 0.3R, 3) : A(src, 6.0R, 43, 62, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 1.5R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 8
                v.Bpm = 118
                v.Patches = MkPatches(1, 4, 2, 33, 3, 26)
                A(src, 0.0R, 62, 88, 0.45R, 1) : A(src, 0.5R, 64, 84, 0.45R, 1) : A(src, 1.0R, 65, 88, 0.45R, 1) : A(src, 1.5R, 67, 92, 0.45R, 1)
                A(src, 2.0R, 69, 94, 0.9R, 1) : A(src, 3.0R, 67, 88, 0.45R, 1) : A(src, 3.5R, 65, 85, 0.45R, 1)
                A(src, 4.0R, 64, 88, 0.45R, 1) : A(src, 4.5R, 62, 84, 0.45R, 1) : A(src, 5.0R, 60, 88, 0.45R, 1) : A(src, 5.5R, 62, 84, 0.45R, 1)
                A(src, 6.0R, 64, 90, 0.9R, 1) : A(src, 7.0R, 65, 86, 0.45R, 1) : A(src, 7.5R, 67, 84, 0.45R, 1)
                A(src, 0.0R, 50, 70, 0.4R, 2) : A(src, 1.0R, 53, 66, 0.4R, 2) : A(src, 2.0R, 57, 70, 0.4R, 2) : A(src, 3.0R, 53, 68, 0.4R, 2)
                A(src, 4.0R, 52, 70, 0.4R, 2) : A(src, 5.0R, 50, 66, 0.4R, 2) : A(src, 6.0R, 48, 70, 0.4R, 2) : A(src, 7.0R, 50, 68, 0.4R, 2)
                A(src, 0.0R, 38, 64, 0.3R, 3) : A(src, 2.0R, 41, 60, 0.3R, 3) : A(src, 4.0R, 38, 64, 0.3R, 3) : A(src, 6.0R, 41, 60, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case Else
                v.Bpm = 124
                v.Patches = MkPatches(1, 7, 2, 35, 3, 24)
                A(src, 0.0R, 69, 90, 0.45R, 1) : A(src, 0.5R, 71, 86, 0.45R, 1) : A(src, 1.0R, 72, 90, 0.45R, 1) : A(src, 1.5R, 74, 94, 0.45R, 1)
                A(src, 2.0R, 76, 96, 0.9R, 1) : A(src, 3.0R, 74, 90, 0.45R, 1) : A(src, 3.5R, 72, 86, 0.45R, 1)
                A(src, 4.0R, 71, 90, 0.45R, 1) : A(src, 4.5R, 69, 86, 0.45R, 1) : A(src, 5.0R, 67, 90, 0.45R, 1) : A(src, 5.5R, 65, 86, 0.45R, 1)
                A(src, 6.0R, 67, 92, 0.9R, 1) : A(src, 7.0R, 69, 88, 0.45R, 1) : A(src, 7.5R, 71, 85, 0.45R, 1)
                A(src, 0.0R, 45, 72, 0.4R, 2) : A(src, 1.0R, 48, 68, 0.4R, 2) : A(src, 2.0R, 52, 72, 0.4R, 2) : A(src, 3.0R, 48, 70, 0.4R, 2)
                A(src, 4.0R, 47, 72, 0.4R, 2) : A(src, 5.0R, 45, 68, 0.4R, 2) : A(src, 6.0R, 43, 72, 0.4R, 2) : A(src, 7.0R, 45, 70, 0.4R, 2)
                A(src, 0.0R, 33, 65, 0.3R, 3) : A(src, 2.0R, 36, 61, 0.3R, 3) : A(src, 4.0R, 38, 65, 0.3R, 3) : A(src, 6.0R, 36, 61, 0.3R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R}, New Double() {0.0R})
        End Select
        Return v
    End Function

    Private Function CreaEpico(sub_idx As Integer) As VariantDef
        Dim v As New VariantDef()
        v.TotalBeats = 64.0R
        Dim src As New List(Of NoteEvent)()
        Select Case sub_idx
            Case 0
                v.Bpm = 80
                v.Patches = MkPatches(1, 40, 2, 44, 3, 32)
                v.CCs = MkCC2(1, New Integer() {91 Or (100 << 8)}, 2, New Integer() {91 Or (85 << 8)})
                A(src, 0.0R, 60, 100, 1.9R, 1) : A(src, 0.0R, 67, 85, 1.9R, 2)
                A(src, 2.0R, 62, 96, 1.9R, 1) : A(src, 2.0R, 69, 82, 1.9R, 2)
                A(src, 4.0R, 65, 102, 1.9R, 1) : A(src, 4.0R, 72, 88, 1.9R, 2)
                A(src, 6.0R, 63, 98, 1.9R, 1) : A(src, 6.0R, 70, 84, 1.9R, 2)
                A(src, 0.0R, 48, 90, 1.9R, 3) : A(src, 2.0R, 50, 88, 1.9R, 3) : A(src, 4.0R, 53, 92, 1.9R, 3) : A(src, 6.0R, 51, 88, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 1
                v.Bpm = 88
                v.Patches = MkPatches(1, 56, 2, 40, 3, 58, 4, 32)
                v.CCs = MkCC2(1, New Integer() {91 Or (90 << 8)}, 2, New Integer() {91 Or (95 << 8)})
                A(src, 0.0R, 67, 105, 1.9R, 1) : A(src, 0.0R, 60, 88, 1.9R, 2)
                A(src, 2.0R, 69, 100, 1.9R, 1) : A(src, 2.0R, 62, 85, 1.9R, 2)
                A(src, 4.0R, 72, 108, 1.9R, 1) : A(src, 4.0R, 65, 90, 1.9R, 2)
                A(src, 6.0R, 70, 102, 1.9R, 1) : A(src, 6.0R, 63, 86, 1.9R, 2)
                A(src, 0.0R, 55, 95, 1.9R, 3) : A(src, 2.0R, 57, 92, 1.9R, 3) : A(src, 4.0R, 60, 98, 1.9R, 3) : A(src, 6.0R, 58, 94, 1.9R, 3)
                A(src, 0.0R, 43, 88, 1.9R, 4) : A(src, 2.0R, 45, 85, 1.9R, 4) : A(src, 4.0R, 48, 90, 1.9R, 4) : A(src, 6.0R, 46, 86, 1.9R, 4)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {2.0R, 3.5R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 2
                v.Bpm = 76
                v.Patches = MkPatches(1, 48, 2, 40, 3, 32)
                v.CCs = MkCC2(1, New Integer() {91 Or (110 << 8)}, 2, New Integer() {91 Or (100 << 8)})
                A(src, 0.0R, 60, 96, 0.9R, 1) : A(src, 1.0R, 62, 92, 0.9R, 1) : A(src, 2.0R, 65, 98, 0.9R, 1) : A(src, 3.0R, 63, 94, 0.9R, 1)
                A(src, 4.0R, 67, 100, 1.9R, 1) : A(src, 6.0R, 65, 96, 0.9R, 1) : A(src, 7.0R, 63, 92, 0.9R, 1)
                A(src, 0.0R, 48, 86, 1.9R, 2) : A(src, 2.0R, 50, 82, 1.9R, 2) : A(src, 4.0R, 53, 88, 1.9R, 2) : A(src, 6.0R, 51, 84, 1.9R, 2)
                A(src, 0.0R, 36, 90, 1.9R, 3) : A(src, 2.0R, 38, 86, 1.9R, 3) : A(src, 4.0R, 41, 92, 1.9R, 3) : A(src, 6.0R, 39, 88, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 3.0R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R, 4.0R})
            Case 3
                v.Bpm = 92
                v.Patches = MkPatches(1, 56, 2, 44, 3, 58, 4, 34)
                A(src, 0.0R, 64, 100, 0.45R, 1) : A(src, 0.5R, 67, 96, 0.45R, 1) : A(src, 1.0R, 71, 102, 0.45R, 1) : A(src, 1.5R, 72, 98, 0.45R, 1)
                A(src, 2.0R, 74, 105, 1.9R, 1)
                A(src, 4.0R, 72, 100, 0.45R, 1) : A(src, 4.5R, 69, 96, 0.45R, 1) : A(src, 5.0R, 67, 100, 0.45R, 1) : A(src, 5.5R, 65, 96, 0.45R, 1)
                A(src, 6.0R, 64, 102, 1.9R, 1)
                A(src, 0.0R, 52, 88, 1.9R, 2) : A(src, 2.0R, 55, 85, 1.9R, 2) : A(src, 4.0R, 57, 90, 1.9R, 2) : A(src, 6.0R, 52, 86, 1.9R, 2)
                A(src, 0.0R, 60, 82, 1.9R, 3) : A(src, 2.0R, 63, 78, 1.9R, 3) : A(src, 4.0R, 65, 84, 1.9R, 3) : A(src, 6.0R, 60, 80, 1.9R, 3)
                A(src, 0.0R, 40, 92, 1.9R, 4) : A(src, 2.0R, 43, 88, 1.9R, 4) : A(src, 4.0R, 45, 94, 1.9R, 4) : A(src, 6.0R, 40, 90, 1.9R, 4)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {2.0R, 3.5R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 4
                v.Bpm = 84
                v.Patches = MkPatches(1, 40, 2, 48, 3, 32)
                v.CCs = MkCC2(1, New Integer() {91 Or (105 << 8)}, 2, New Integer() {91 Or (110 << 8)})
                A(src, 0.0R, 65, 98, 1.9R, 1) : A(src, 0.0R, 72, 82, 1.9R, 2)
                A(src, 2.0R, 67, 94, 1.9R, 1) : A(src, 2.0R, 74, 78, 1.9R, 2)
                A(src, 4.0R, 70, 100, 1.9R, 1) : A(src, 4.0R, 77, 84, 1.9R, 2)
                A(src, 6.0R, 68, 96, 1.9R, 1) : A(src, 6.0R, 75, 80, 1.9R, 2)
                A(src, 0.0R, 53, 88, 1.9R, 3) : A(src, 2.0R, 55, 84, 1.9R, 3) : A(src, 4.0R, 58, 90, 1.9R, 3) : A(src, 6.0R, 56, 86, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 3.0R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 5
                v.Bpm = 96
                v.Patches = MkPatches(1, 56, 2, 40, 3, 58)
                A(src, 0.0R, 60, 102, 0.9R, 1) : A(src, 1.0R, 64, 98, 0.9R, 1) : A(src, 2.0R, 67, 104, 0.9R, 1) : A(src, 3.0R, 65, 100, 0.9R, 1)
                A(src, 4.0R, 69, 106, 1.9R, 1) : A(src, 6.0R, 67, 102, 0.9R, 1) : A(src, 7.0R, 65, 98, 0.9R, 1)
                A(src, 0.0R, 48, 90, 1.9R, 2) : A(src, 2.0R, 52, 86, 1.9R, 2) : A(src, 4.0R, 55, 92, 1.9R, 2) : A(src, 6.0R, 53, 88, 1.9R, 2)
                A(src, 0.0R, 55, 84, 1.9R, 3) : A(src, 2.0R, 59, 80, 1.9R, 3) : A(src, 4.0R, 62, 86, 1.9R, 3) : A(src, 6.0R, 60, 82, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {2.0R, 3.5R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 6
                v.Bpm = 78
                v.Patches = MkPatches(1, 44, 2, 40, 3, 32, 4, 58)
                A(src, 0.0R, 67, 96, 1.9R, 1) : A(src, 0.0R, 55, 80, 1.9R, 2)
                A(src, 2.0R, 69, 92, 1.9R, 1) : A(src, 2.0R, 57, 76, 1.9R, 2)
                A(src, 4.0R, 72, 98, 1.9R, 1) : A(src, 4.0R, 60, 82, 1.9R, 2)
                A(src, 6.0R, 70, 94, 1.9R, 1) : A(src, 6.0R, 58, 78, 1.9R, 2)
                A(src, 0.0R, 43, 88, 1.9R, 3) : A(src, 2.0R, 45, 84, 1.9R, 3) : A(src, 4.0R, 48, 90, 1.9R, 3) : A(src, 6.0R, 46, 86, 1.9R, 3)
                A(src, 0.0R, 55, 78, 1.9R, 4) : A(src, 2.0R, 57, 74, 1.9R, 4) : A(src, 4.0R, 60, 80, 1.9R, 4) : A(src, 6.0R, 58, 76, 1.9R, 4)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R, 4.0R})
            Case 7
                v.Bpm = 90
                v.Patches = MkPatches(1, 48, 2, 56, 3, 34)
                A(src, 0.0R, 62, 98, 0.45R, 1) : A(src, 0.5R, 65, 94, 0.45R, 1) : A(src, 1.0R, 69, 100, 0.45R, 1) : A(src, 1.5R, 70, 96, 0.45R, 1)
                A(src, 2.0R, 72, 103, 1.9R, 1)
                A(src, 4.0R, 70, 98, 0.45R, 1) : A(src, 4.5R, 67, 94, 0.45R, 1) : A(src, 5.0R, 65, 98, 0.45R, 1) : A(src, 5.5R, 62, 94, 0.45R, 1)
                A(src, 6.0R, 60, 100, 1.9R, 1)
                A(src, 0.0R, 50, 86, 1.9R, 2) : A(src, 2.0R, 53, 82, 1.9R, 2) : A(src, 4.0R, 57, 88, 1.9R, 2) : A(src, 6.0R, 55, 84, 1.9R, 2)
                A(src, 0.0R, 57, 80, 1.9R, 3) : A(src, 2.0R, 60, 76, 1.9R, 3) : A(src, 4.0R, 64, 82, 1.9R, 3) : A(src, 6.0R, 62, 78, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 3.0R}, New Double() {2.0R, 3.5R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case 8
                v.Bpm = 86
                v.Patches = MkPatches(1, 40, 2, 44, 3, 58, 4, 32)
                A(src, 0.0R, 65, 100, 1.9R, 1) : A(src, 0.0R, 72, 84, 1.9R, 2)
                A(src, 2.0R, 63, 96, 1.9R, 1) : A(src, 2.0R, 70, 80, 1.9R, 2)
                A(src, 4.0R, 67, 102, 1.9R, 1) : A(src, 4.0R, 74, 86, 1.9R, 2)
                A(src, 6.0R, 65, 98, 1.9R, 1) : A(src, 6.0R, 72, 82, 1.9R, 2)
                A(src, 0.0R, 53, 90, 1.9R, 3) : A(src, 2.0R, 51, 86, 1.9R, 3) : A(src, 4.0R, 55, 92, 1.9R, 3) : A(src, 6.0R, 53, 88, 1.9R, 3)
                A(src, 0.0R, 41, 92, 1.9R, 4) : A(src, 2.0R, 39, 88, 1.9R, 4) : A(src, 4.0R, 43, 94, 1.9R, 4) : A(src, 6.0R, 41, 90, 1.9R, 4)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
            Case Else
                v.Bpm = 82
                v.Patches = MkPatches(1, 56, 2, 40, 3, 32)
                A(src, 0.0R, 60, 100, 0.9R, 1) : A(src, 1.0R, 63, 96, 0.9R, 1) : A(src, 2.0R, 67, 102, 0.9R, 1) : A(src, 3.0R, 65, 98, 0.9R, 1)
                A(src, 4.0R, 68, 104, 1.9R, 1) : A(src, 6.0R, 67, 100, 0.9R, 1) : A(src, 7.0R, 65, 96, 0.9R, 1)
                A(src, 0.0R, 48, 88, 1.9R, 2) : A(src, 2.0R, 51, 84, 1.9R, 2) : A(src, 4.0R, 55, 90, 1.9R, 2) : A(src, 6.0R, 53, 86, 1.9R, 2)
                A(src, 0.0R, 36, 92, 1.9R, 3) : A(src, 2.0R, 39, 88, 1.9R, 3) : A(src, 4.0R, 43, 94, 1.9R, 3) : A(src, 6.0R, 41, 90, 1.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.5R}, New Double() {2.0R}, New Double() {0.0R, 1.0R, 2.0R, 3.0R}, New Double() {0.0R})
        End Select
        Return v
    End Function

    Private Function CreaElettronico(sub_idx As Integer) As VariantDef
        Dim v As New VariantDef()
        v.TotalBeats = 64.0R
        Dim src As New List(Of NoteEvent)()
        Select Case sub_idx
            Case 0
                v.Bpm = 140
                v.Patches = MkPatches(1, 81, 2, 38, 3, 39)
                A(src, 0.0R, 60, 100, 0.45R, 1) : A(src, 0.5R, 63, 95, 0.45R, 1) : A(src, 1.0R, 65, 100, 0.45R, 1) : A(src, 1.5R, 67, 95, 0.45R, 1)
                A(src, 2.0R, 70, 105, 0.9R, 1) : A(src, 3.0R, 68, 98, 0.45R, 1) : A(src, 3.5R, 65, 94, 0.45R, 1)
                A(src, 4.0R, 63, 100, 0.45R, 1) : A(src, 4.5R, 60, 95, 0.45R, 1) : A(src, 5.0R, 58, 100, 0.45R, 1) : A(src, 5.5R, 55, 95, 0.45R, 1)
                A(src, 6.0R, 58, 105, 0.9R, 1) : A(src, 7.0R, 60, 98, 0.45R, 1) : A(src, 7.5R, 63, 94, 0.45R, 1)
                A(src, 0.0R, 36, 90, 0.45R, 2) : A(src, 0.5R, 39, 85, 0.45R, 2) : A(src, 1.0R, 41, 90, 0.45R, 2) : A(src, 1.5R, 43, 85, 0.45R, 2)
                A(src, 2.0R, 46, 95, 0.9R, 2) : A(src, 3.0R, 44, 88, 0.45R, 2) : A(src, 3.5R, 41, 84, 0.45R, 2)
                A(src, 4.0R, 39, 90, 0.45R, 2) : A(src, 4.5R, 36, 85, 0.45R, 2) : A(src, 5.0R, 34, 90, 0.45R, 2) : A(src, 5.5R, 31, 85, 0.45R, 2)
                A(src, 6.0R, 34, 95, 0.9R, 2) : A(src, 7.0R, 36, 88, 0.45R, 2) : A(src, 7.5R, 39, 84, 0.45R, 2)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.25R, 0.5R, 0.75R, 1.0R, 1.25R, 1.5R, 1.75R, 2.0R, 2.25R, 2.5R, 2.75R, 3.0R, 3.25R, 3.5R, 3.75R})
            Case 1
                v.Bpm = 128
                v.Patches = MkPatches(1, 80, 2, 87, 3, 38)
                A(src, 0.0R, 72, 98, 0.45R, 1) : A(src, 0.5R, 74, 94, 0.45R, 1) : A(src, 1.0R, 76, 98, 0.45R, 1) : A(src, 1.5R, 77, 94, 0.45R, 1)
                A(src, 2.0R, 79, 102, 0.9R, 1) : A(src, 3.0R, 77, 96, 0.45R, 1) : A(src, 3.5R, 76, 92, 0.45R, 1)
                A(src, 4.0R, 74, 98, 0.45R, 1) : A(src, 4.5R, 72, 94, 0.45R, 1) : A(src, 5.0R, 71, 98, 0.45R, 1) : A(src, 5.5R, 69, 94, 0.45R, 1)
                A(src, 6.0R, 71, 102, 0.9R, 1) : A(src, 7.0R, 72, 96, 0.45R, 1) : A(src, 7.5R, 74, 92, 0.45R, 1)
                A(src, 0.0R, 48, 88, 0.9R, 2) : A(src, 1.0R, 51, 84, 0.9R, 2) : A(src, 2.0R, 55, 90, 0.9R, 2) : A(src, 3.0R, 53, 86, 0.9R, 2)
                A(src, 4.0R, 50, 88, 0.9R, 2) : A(src, 5.0R, 48, 84, 0.9R, 2) : A(src, 6.0R, 46, 90, 0.9R, 2) : A(src, 7.0R, 48, 86, 0.9R, 2)
                A(src, 0.0R, 36, 82, 0.45R, 3) : A(src, 1.0R, 38, 78, 0.45R, 3) : A(src, 2.0R, 41, 82, 0.45R, 3) : A(src, 3.0R, 39, 80, 0.45R, 3)
                A(src, 4.0R, 38, 82, 0.45R, 3) : A(src, 5.0R, 36, 78, 0.45R, 3) : A(src, 6.0R, 34, 82, 0.45R, 3) : A(src, 7.0R, 36, 80, 0.45R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 2
                v.Bpm = 150
                v.Patches = MkPatches(1, 85, 2, 38, 3, 81)
                A(src, 0.0R, 65, 102, 0.33R, 1) : A(src, 0.33R, 67, 98, 0.33R, 1) : A(src, 0.67R, 69, 102, 0.33R, 1)
                A(src, 1.0R, 72, 106, 0.33R, 1) : A(src, 1.33R, 69, 100, 0.33R, 1) : A(src, 1.67R, 67, 96, 0.33R, 1)
                A(src, 2.0R, 65, 102, 0.33R, 1) : A(src, 2.33R, 64, 98, 0.33R, 1) : A(src, 2.67R, 62, 102, 0.33R, 1)
                A(src, 3.0R, 60, 106, 0.33R, 1) : A(src, 3.33R, 62, 100, 0.33R, 1) : A(src, 3.67R, 64, 96, 0.33R, 1)
                A(src, 4.0R, 65, 102, 0.33R, 1) : A(src, 4.33R, 67, 98, 0.33R, 1) : A(src, 4.67R, 70, 102, 0.33R, 1)
                A(src, 5.0R, 72, 106, 0.33R, 1) : A(src, 5.33R, 70, 100, 0.33R, 1) : A(src, 5.67R, 67, 96, 0.33R, 1)
                A(src, 6.0R, 65, 102, 0.33R, 1) : A(src, 6.33R, 67, 98, 0.33R, 1) : A(src, 6.67R, 69, 102, 0.33R, 1)
                A(src, 7.0R, 70, 106, 0.95R, 1)
                A(src, 0.0R, 41, 88, 0.45R, 2) : A(src, 1.0R, 44, 84, 0.45R, 2) : A(src, 2.0R, 46, 88, 0.45R, 2) : A(src, 3.0R, 48, 84, 0.45R, 2)
                A(src, 4.0R, 46, 88, 0.45R, 2) : A(src, 5.0R, 44, 84, 0.45R, 2) : A(src, 6.0R, 41, 88, 0.45R, 2) : A(src, 7.0R, 44, 84, 0.45R, 2)
                A(src, 0.0R, 29, 82, 0.45R, 3) : A(src, 2.0R, 32, 78, 0.45R, 3) : A(src, 4.0R, 34, 82, 0.45R, 3) : A(src, 6.0R, 32, 78, 0.45R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 1.5R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.25R, 0.5R, 0.75R, 1.0R, 1.25R, 1.5R, 1.75R, 2.0R, 2.25R, 2.5R, 2.75R, 3.0R, 3.25R, 3.5R, 3.75R})
            Case 3
                v.Bpm = 134
                v.Patches = MkPatches(1, 89, 2, 81, 3, 38)
                A(src, 0.0R, 60, 96, 0.45R, 1) : A(src, 0.5R, 62, 92, 0.45R, 1) : A(src, 1.0R, 65, 96, 0.45R, 1) : A(src, 1.5R, 67, 92, 0.45R, 1)
                A(src, 2.0R, 69, 100, 0.45R, 1) : A(src, 2.5R, 67, 94, 0.45R, 1) : A(src, 3.0R, 65, 96, 0.45R, 1) : A(src, 3.5R, 62, 92, 0.45R, 1)
                A(src, 4.0R, 60, 96, 0.45R, 1) : A(src, 4.5R, 59, 92, 0.45R, 1) : A(src, 5.0R, 57, 96, 0.45R, 1) : A(src, 5.5R, 55, 92, 0.45R, 1)
                A(src, 6.0R, 57, 100, 0.45R, 1) : A(src, 6.5R, 59, 94, 0.45R, 1) : A(src, 7.0R, 60, 96, 0.9R, 1)
                A(src, 0.0R, 48, 86, 0.9R, 2) : A(src, 1.0R, 50, 82, 0.9R, 2) : A(src, 2.0R, 53, 86, 0.9R, 2) : A(src, 3.0R, 55, 82, 0.9R, 2)
                A(src, 4.0R, 53, 86, 0.9R, 2) : A(src, 5.0R, 50, 82, 0.9R, 2) : A(src, 6.0R, 48, 86, 0.9R, 2) : A(src, 7.0R, 50, 82, 0.9R, 2)
                A(src, 0.0R, 36, 80, 0.45R, 3) : A(src, 2.0R, 38, 76, 0.45R, 3) : A(src, 4.0R, 36, 80, 0.45R, 3) : A(src, 6.0R, 38, 76, 0.45R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case 4
                v.Bpm = 145
                v.Patches = MkPatches(1, 80, 2, 38, 3, 87)
                A(src, 0.0R, 76, 100, 0.45R, 1) : A(src, 0.5R, 74, 96, 0.45R, 1) : A(src, 1.0R, 72, 100, 0.45R, 1) : A(src, 1.5R, 71, 96, 0.45R, 1)
                A(src, 2.0R, 69, 104, 0.9R, 1) : A(src, 3.0R, 71, 98, 0.45R, 1) : A(src, 3.5R, 72, 94, 0.45R, 1)
                A(src, 4.0R, 74, 100, 0.45R, 1) : A(src, 4.5R, 76, 96, 0.45R, 1) : A(src, 5.0R, 77, 100, 0.45R, 1) : A(src, 5.5R, 79, 96, 0.45R, 1)
                A(src, 6.0R, 77, 104, 0.9R, 1) : A(src, 7.0R, 76, 98, 0.45R, 1) : A(src, 7.5R, 74, 94, 0.45R, 1)
                A(src, 0.0R, 52, 88, 0.45R, 2) : A(src, 1.0R, 50, 84, 0.45R, 2) : A(src, 2.0R, 48, 88, 0.45R, 2) : A(src, 3.0R, 47, 84, 0.45R, 2)
                A(src, 4.0R, 45, 88, 0.45R, 2) : A(src, 5.0R, 47, 84, 0.45R, 2) : A(src, 6.0R, 48, 88, 0.45R, 2) : A(src, 7.0R, 50, 84, 0.45R, 2)
                A(src, 0.0R, 40, 82, 0.45R, 3) : A(src, 2.0R, 38, 78, 0.45R, 3) : A(src, 4.0R, 36, 82, 0.45R, 3) : A(src, 6.0R, 38, 78, 0.45R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 1.5R, 2.5R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.5R, 1.0R, 1.5R, 2.0R, 2.5R, 3.0R, 3.5R})
            Case Else
                v.Bpm = 136
                v.Patches = MkPatches(1, 85, 2, 81, 3, 38)
                A(src, 0.0R, 69, 98, 0.45R, 1) : A(src, 0.5R, 71, 94, 0.45R, 1) : A(src, 1.0R, 72, 98, 0.45R, 1) : A(src, 1.5R, 74, 94, 0.45R, 1)
                A(src, 2.0R, 76, 102, 0.9R, 1) : A(src, 3.0R, 74, 96, 0.45R, 1) : A(src, 3.5R, 72, 92, 0.45R, 1)
                A(src, 4.0R, 71, 98, 0.45R, 1) : A(src, 4.5R, 69, 94, 0.45R, 1) : A(src, 5.0R, 67, 98, 0.45R, 1) : A(src, 5.5R, 65, 94, 0.45R, 1)
                A(src, 6.0R, 67, 102, 0.9R, 1) : A(src, 7.0R, 69, 96, 0.45R, 1) : A(src, 7.5R, 71, 92, 0.45R, 1)
                A(src, 0.0R, 45, 86, 0.9R, 2) : A(src, 1.0R, 47, 82, 0.9R, 2) : A(src, 2.0R, 50, 86, 0.9R, 2) : A(src, 3.0R, 52, 82, 0.9R, 2)
                A(src, 4.0R, 50, 86, 0.9R, 2) : A(src, 5.0R, 48, 82, 0.9R, 2) : A(src, 6.0R, 45, 86, 0.9R, 2) : A(src, 7.0R, 47, 82, 0.9R, 2)
                A(src, 0.0R, 33, 80, 0.45R, 3) : A(src, 2.0R, 35, 76, 0.45R, 3) : A(src, 4.0R, 36, 80, 0.45R, 3) : A(src, 6.0R, 35, 76, 0.45R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                BuildDrumLoop(v.Drums, 4.0R, v.TotalBeats, New Double() {0.0R, 2.0R}, New Double() {1.0R, 3.0R}, New Double() {0.0R, 0.25R, 0.5R, 0.75R, 1.0R, 1.25R, 1.5R, 1.75R, 2.0R, 2.25R, 2.5R, 2.75R, 3.0R, 3.25R, 3.5R, 3.75R})
        End Select
        Return v
    End Function

    Private Function CreaLatino(sub_idx As Integer) As VariantDef
        Dim v As New VariantDef()
        v.TotalBeats = 64.0R
        Dim src As New List(Of NoteEvent)()
        Select Case sub_idx
            Case 0
                v.Bpm = 116
                v.Patches = MkPatches(1, 25, 2, 33, 3, 73)
                v.CCs = MkCC2(1, New Integer() {91 Or (40 << 8)}, 3, New Integer() {91 Or (85 << 8)})
                A(src, 0.0R, 64, 92, 0.4R, 1) : A(src, 0.5R, 67, 88, 0.4R, 1) : A(src, 1.0R, 69, 92, 0.4R, 1) : A(src, 1.5R, 71, 96, 0.4R, 1)
                A(src, 2.0R, 72, 98, 0.9R, 1) : A(src, 3.0R, 71, 92, 0.4R, 1) : A(src, 3.5R, 69, 88, 0.4R, 1)
                A(src, 4.0R, 67, 92, 0.4R, 1) : A(src, 4.5R, 64, 88, 0.4R, 1) : A(src, 5.0R, 62, 92, 0.4R, 1) : A(src, 5.5R, 60, 96, 0.4R, 1)
                A(src, 6.0R, 62, 98, 0.9R, 1) : A(src, 7.0R, 64, 92, 0.4R, 1) : A(src, 7.5R, 67, 88, 0.4R, 1)
                A(src, 0.0R, 52, 78, 0.38R, 2) : A(src, 0.75R, 55, 74, 0.38R, 2) : A(src, 1.5R, 57, 78, 0.38R, 2) : A(src, 2.25R, 55, 74, 0.38R, 2)
                A(src, 3.0R, 52, 78, 0.38R, 2) : A(src, 3.75R, 50, 74, 0.38R, 2) : A(src, 4.5R, 48, 78, 0.38R, 2) : A(src, 5.25R, 50, 74, 0.38R, 2)
                A(src, 6.0R, 52, 78, 0.38R, 2) : A(src, 6.75R, 55, 74, 0.38R, 2)
                A(src, 0.0R, 52, 88, 0.4R, 3) : A(src, 1.0R, 55, 84, 0.4R, 3) : A(src, 2.0R, 57, 88, 0.4R, 3) : A(src, 3.0R, 55, 84, 0.4R, 3)
                A(src, 4.0R, 52, 88, 0.4R, 3) : A(src, 5.0R, 50, 84, 0.4R, 3) : A(src, 6.0R, 48, 88, 0.4R, 3) : A(src, 7.0R, 50, 84, 0.4R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b As Double = 0.0R
                Do While b < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b, 36, 112)) : v.Drums.Add(New DrumEvent(b + 0.5R, 42, 70))
                    v.Drums.Add(New DrumEvent(b + 1.0R, 38, 98)) : v.Drums.Add(New DrumEvent(b + 1.5R, 42, 70))
                    v.Drums.Add(New DrumEvent(b + 2.0R, 36, 108)) : v.Drums.Add(New DrumEvent(b + 2.5R, 42, 70))
                    v.Drums.Add(New DrumEvent(b + 2.75R, 36, 85)) : v.Drums.Add(New DrumEvent(b + 3.0R, 38, 98))
                    v.Drums.Add(New DrumEvent(b + 3.5R, 42, 70))
                    b += 4.0R
                Loop
            Case 1
                v.Bpm = 104
                v.Patches = MkPatches(1, 26, 2, 33, 3, 19)
                A(src, 0.0R, 69, 90, 0.45R, 1) : A(src, 0.5R, 71, 86, 0.45R, 1) : A(src, 1.0R, 72, 90, 0.45R, 1) : A(src, 1.5R, 74, 94, 0.45R, 1)
                A(src, 2.0R, 76, 96, 0.9R, 1) : A(src, 3.0R, 74, 90, 0.45R, 1) : A(src, 3.5R, 72, 86, 0.45R, 1)
                A(src, 4.0R, 71, 90, 0.45R, 1) : A(src, 4.5R, 69, 86, 0.45R, 1) : A(src, 5.0R, 67, 90, 0.45R, 1) : A(src, 5.5R, 65, 94, 0.45R, 1)
                A(src, 6.0R, 67, 96, 0.9R, 1) : A(src, 7.0R, 69, 90, 0.45R, 1) : A(src, 7.5R, 71, 86, 0.45R, 1)
                A(src, 0.0R, 45, 76, 0.38R, 2) : A(src, 0.75R, 48, 72, 0.38R, 2) : A(src, 1.5R, 50, 76, 0.38R, 2) : A(src, 2.25R, 48, 72, 0.38R, 2)
                A(src, 3.0R, 45, 76, 0.38R, 2) : A(src, 3.75R, 43, 72, 0.38R, 2) : A(src, 4.5R, 41, 76, 0.38R, 2) : A(src, 5.25R, 43, 72, 0.38R, 2)
                A(src, 6.0R, 45, 76, 0.38R, 2) : A(src, 6.75R, 48, 72, 0.38R, 2)
                A(src, 0.0R, 45, 86, 0.9R, 3) : A(src, 2.0R, 48, 82, 0.9R, 3) : A(src, 4.0R, 50, 86, 0.9R, 3) : A(src, 6.0R, 48, 82, 0.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b2 As Double = 0.0R
                Do While b2 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b2, 36, 110)) : v.Drums.Add(New DrumEvent(b2 + 0.67R, 42, 72))
                    v.Drums.Add(New DrumEvent(b2 + 1.0R, 38, 96)) : v.Drums.Add(New DrumEvent(b2 + 1.33R, 42, 72))
                    v.Drums.Add(New DrumEvent(b2 + 1.67R, 42, 68)) : v.Drums.Add(New DrumEvent(b2 + 2.0R, 36, 106))
                    v.Drums.Add(New DrumEvent(b2 + 2.33R, 42, 72)) : v.Drums.Add(New DrumEvent(b2 + 2.67R, 38, 90))
                    v.Drums.Add(New DrumEvent(b2 + 3.0R, 42, 72)) : v.Drums.Add(New DrumEvent(b2 + 3.33R, 36, 82))
                    v.Drums.Add(New DrumEvent(b2 + 3.67R, 42, 68))
                    b2 += 4.0R
                Loop
            Case 2
                v.Bpm = 120
                v.Patches = MkPatches(1, 25, 2, 35, 3, 73)
                A(src, 0.0R, 60, 94, 0.4R, 1) : A(src, 0.5R, 62, 90, 0.4R, 1) : A(src, 1.0R, 65, 94, 0.4R, 1) : A(src, 1.5R, 67, 98, 0.4R, 1)
                A(src, 2.0R, 69, 100, 0.9R, 1) : A(src, 3.0R, 67, 94, 0.4R, 1) : A(src, 3.5R, 65, 90, 0.4R, 1)
                A(src, 4.0R, 62, 94, 0.4R, 1) : A(src, 4.5R, 60, 90, 0.4R, 1) : A(src, 5.0R, 59, 94, 0.4R, 1) : A(src, 5.5R, 57, 98, 0.4R, 1)
                A(src, 6.0R, 59, 100, 0.9R, 1) : A(src, 7.0R, 60, 94, 0.4R, 1) : A(src, 7.5R, 62, 90, 0.4R, 1)
                A(src, 0.0R, 48, 80, 0.38R, 2) : A(src, 0.75R, 50, 76, 0.38R, 2) : A(src, 1.5R, 53, 80, 0.38R, 2) : A(src, 2.25R, 50, 76, 0.38R, 2)
                A(src, 3.0R, 48, 80, 0.38R, 2) : A(src, 3.75R, 45, 76, 0.38R, 2) : A(src, 4.5R, 43, 80, 0.38R, 2) : A(src, 5.25R, 45, 76, 0.38R, 2)
                A(src, 6.0R, 48, 80, 0.38R, 2) : A(src, 6.75R, 50, 76, 0.38R, 2)
                A(src, 0.0R, 48, 90, 0.9R, 3) : A(src, 2.0R, 50, 86, 0.9R, 3) : A(src, 4.0R, 53, 90, 0.9R, 3) : A(src, 6.0R, 50, 86, 0.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b3 As Double = 0.0R
                Do While b3 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b3, 36, 112)) : v.Drums.Add(New DrumEvent(b3 + 0.5R, 42, 72))
                    v.Drums.Add(New DrumEvent(b3 + 1.0R, 38, 100)) : v.Drums.Add(New DrumEvent(b3 + 1.5R, 42, 70))
                    v.Drums.Add(New DrumEvent(b3 + 2.0R, 36, 108)) : v.Drums.Add(New DrumEvent(b3 + 2.5R, 42, 72))
                    v.Drums.Add(New DrumEvent(b3 + 3.0R, 38, 100)) : v.Drums.Add(New DrumEvent(b3 + 3.25R, 36, 80))
                    v.Drums.Add(New DrumEvent(b3 + 3.5R, 42, 70))
                    b3 += 4.0R
                Loop
            Case Else
                v.Bpm = 112
                v.Patches = MkPatches(1, 26, 2, 33, 3, 19)
                A(src, 0.0R, 67, 92, 0.45R, 1) : A(src, 0.5R, 69, 88, 0.45R, 1) : A(src, 1.0R, 71, 92, 0.45R, 1) : A(src, 1.5R, 72, 96, 0.45R, 1)
                A(src, 2.0R, 74, 98, 0.9R, 1) : A(src, 3.0R, 72, 92, 0.45R, 1) : A(src, 3.5R, 71, 88, 0.45R, 1)
                A(src, 4.0R, 69, 92, 0.45R, 1) : A(src, 4.5R, 67, 88, 0.45R, 1) : A(src, 5.0R, 65, 92, 0.45R, 1) : A(src, 5.5R, 64, 96, 0.45R, 1)
                A(src, 6.0R, 65, 98, 0.9R, 1) : A(src, 7.0R, 67, 92, 0.45R, 1) : A(src, 7.5R, 69, 88, 0.45R, 1)
                A(src, 0.0R, 43, 78, 0.38R, 2) : A(src, 0.75R, 47, 74, 0.38R, 2) : A(src, 1.5R, 50, 78, 0.38R, 2) : A(src, 2.25R, 47, 74, 0.38R, 2)
                A(src, 3.0R, 43, 78, 0.38R, 2) : A(src, 3.75R, 41, 74, 0.38R, 2) : A(src, 4.5R, 38, 78, 0.38R, 2) : A(src, 5.25R, 41, 74, 0.38R, 2)
                A(src, 6.0R, 43, 78, 0.38R, 2) : A(src, 6.75R, 47, 74, 0.38R, 2)
                A(src, 0.0R, 43, 88, 0.9R, 3) : A(src, 2.0R, 47, 84, 0.9R, 3) : A(src, 4.0R, 50, 88, 0.9R, 3) : A(src, 6.0R, 47, 84, 0.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b4 As Double = 0.0R
                Do While b4 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b4, 36, 110)) : v.Drums.Add(New DrumEvent(b4 + 0.5R, 42, 70))
                    v.Drums.Add(New DrumEvent(b4 + 0.75R, 42, 65)) : v.Drums.Add(New DrumEvent(b4 + 1.0R, 38, 98))
                    v.Drums.Add(New DrumEvent(b4 + 1.5R, 42, 70)) : v.Drums.Add(New DrumEvent(b4 + 2.0R, 36, 106))
                    v.Drums.Add(New DrumEvent(b4 + 2.5R, 42, 70)) : v.Drums.Add(New DrumEvent(b4 + 3.0R, 38, 98))
                    v.Drums.Add(New DrumEvent(b4 + 3.5R, 42, 70)) : v.Drums.Add(New DrumEvent(b4 + 3.75R, 36, 82))
                    b4 += 4.0R
                Loop
        End Select
        Return v
    End Function

    Private Function CreaAmbient(sub_idx As Integer) As VariantDef
        Dim v As New VariantDef()
        v.TotalBeats = 64.0R
        Dim src As New List(Of NoteEvent)()
        Select Case sub_idx
            Case 0
                v.Bpm = 72
                v.Patches = MkPatches(1, 89, 2, 0, 3, 48)
                v.CCs = MkCC2(1, New Integer() {91 Or (120 << 8), 93 Or (80 << 8)}, 2, New Integer() {91 Or (70 << 8)})
                A(src, 0.0R, 60, 72, 3.9R, 1) : A(src, 0.0R, 64, 65, 3.9R, 2)
                A(src, 4.0R, 62, 68, 3.9R, 1) : A(src, 4.0R, 67, 62, 3.9R, 2)
                A(src, 0.0R, 48, 80, 7.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b As Double = 0.0R
                Do While b < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b, 36, 70)) : v.Drums.Add(New DrumEvent(b + 1.0R, 42, 45))
                    v.Drums.Add(New DrumEvent(b + 2.0R, 38, 55)) : v.Drums.Add(New DrumEvent(b + 3.0R, 42, 45))
                    b += 4.0R
                Loop
            Case 1
                v.Bpm = 66
                v.Patches = MkPatches(1, 88, 2, 91, 3, 0, 4, 32)
                v.CCs = MkCC2(1, New Integer() {91 Or (115 << 8)}, 2, New Integer() {91 Or (110 << 8), 93 Or (70 << 8)})
                A(src, 0.0R, 67, 68, 7.9R, 1) : A(src, 0.0R, 74, 60, 7.9R, 2)
                A(src, 0.0R, 55, 75, 3.9R, 3) : A(src, 4.0R, 53, 72, 3.9R, 3)
                A(src, 0.0R, 43, 82, 7.9R, 4)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b2 As Double = 0.0R
                Do While b2 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b2, 36, 65)) : v.Drums.Add(New DrumEvent(b2 + 1.0R, 42, 42))
                    v.Drums.Add(New DrumEvent(b2 + 2.0R, 38, 50)) : v.Drums.Add(New DrumEvent(b2 + 3.0R, 42, 42))
                    b2 += 4.0R
                Loop
            Case 2
                v.Bpm = 80
                v.Patches = MkPatches(1, 89, 2, 0, 3, 48)
                v.CCs = MkCC2(1, New Integer() {91 Or (118 << 8), 93 Or (75 << 8)}, 2, New Integer() {91 Or (65 << 8)})
                A(src, 0.0R, 65, 70, 1.9R, 1) : A(src, 2.0R, 67, 66, 1.9R, 1) : A(src, 4.0R, 69, 72, 1.9R, 1) : A(src, 6.0R, 67, 68, 1.9R, 1)
                A(src, 0.0R, 53, 62, 1.9R, 2) : A(src, 2.0R, 55, 58, 1.9R, 2) : A(src, 4.0R, 57, 64, 1.9R, 2) : A(src, 6.0R, 55, 60, 1.9R, 2)
                A(src, 0.0R, 53, 78, 3.9R, 3) : A(src, 4.0R, 55, 74, 3.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b3 As Double = 0.0R
                Do While b3 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b3, 36, 68)) : v.Drums.Add(New DrumEvent(b3 + 1.0R, 42, 44))
                    v.Drums.Add(New DrumEvent(b3 + 2.0R, 38, 52)) : v.Drums.Add(New DrumEvent(b3 + 3.0R, 42, 44))
                    b3 += 4.0R
                Loop
            Case 3
                v.Bpm = 68
                v.Patches = MkPatches(1, 90, 2, 89, 3, 0)
                v.CCs = MkCC2(1, New Integer() {91 Or (120 << 8), 93 Or (90 << 8)}, 2, New Integer() {91 Or (110 << 8)})
                A(src, 0.0R, 64, 65, 7.9R, 1) : A(src, 0.0R, 71, 58, 7.9R, 2)
                A(src, 0.0R, 52, 72, 3.9R, 3) : A(src, 4.0R, 50, 68, 3.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b4 As Double = 0.0R
                Do While b4 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b4, 36, 62)) : v.Drums.Add(New DrumEvent(b4 + 1.0R, 42, 40))
                    v.Drums.Add(New DrumEvent(b4 + 2.0R, 38, 48)) : v.Drums.Add(New DrumEvent(b4 + 3.0R, 42, 40))
                    b4 += 4.0R
                Loop
            Case Else
                v.Bpm = 76
                v.Patches = MkPatches(1, 88, 2, 0, 3, 48)
                v.CCs = MkCC2(1, New Integer() {91 Or (115 << 8)}, 2, New Integer() {91 Or (68 << 8)})
                A(src, 0.0R, 69, 68, 3.9R, 1) : A(src, 4.0R, 71, 64, 3.9R, 1)
                A(src, 0.0R, 57, 60, 3.9R, 2) : A(src, 4.0R, 59, 56, 3.9R, 2)
                A(src, 0.0R, 45, 76, 7.9R, 3)
                RepeatMelody(src, v.Melody, v.TotalBeats)
                Dim b5 As Double = 0.0R
                Do While b5 < v.TotalBeats
                    v.Drums.Add(New DrumEvent(b5, 36, 66)) : v.Drums.Add(New DrumEvent(b5 + 1.0R, 42, 42))
                    v.Drums.Add(New DrumEvent(b5 + 2.0R, 38, 50)) : v.Drums.Add(New DrumEvent(b5 + 3.0R, 42, 42))
                    b5 += 4.0R
                Loop
        End Select
        Return v
    End Function

End Class