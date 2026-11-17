' ============================================================================================
' WGFotocameraLettore - il lettore dei video registrati, dentro la fotocamera
' ============================================================================================
'
' Guardare un video appena registrato non deve aprire un'altra finestra: Esc deve riportare alla fotocamera, non chiuderne due. Qui ffmpeg
' decodifica il file in due processi, a velocità reale: uno manda i fotogrammi (senza compressione, della misura dell'anteprima) che la
' fotocamera disegna a tutto schermo come fa con la videocamera, l'altro manda l'audio (16 bit, 48 kHz, stereo) a NAudio, che lo suona.
' L'audio ha una piccola latenza della scheda audio: i fotogrammi partono un po' dopo, per restare insieme.
' ============================================================================================
Imports System
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Threading
Imports NAudio.Wave

Public NotInheritable Class LettoreVideoInterno

    Private ReadOnly _log As New Logger()
    Private _video As Process
    Private _audio As Process
    Private _uscita As WaveOutEvent
    Private _buffer As BufferedWaveProvider
    Private _threadVideo As Thread
    Private _threadAudio As Thread
    Private _fermato As Boolean
    Private _inizio As Double
    Private ReadOnly _orologio As New Stopwatch()
    Private _larghezza As Integer
    Private _altezza As Integer

    ''' <summary>Un fotogramma da mostrare. Chi lo riceve ne diventa il proprietario e lo elimina.</summary>
    Public Event Fotogramma(immagine As Bitmap)
    ''' <summary>Il video è arrivato in fondo da solo (non perché si è fermato il lettore).</summary>
    Public Event Finito()

    ''' <summary>Il punto del video in cui si è, in secondi.</summary>
    Public ReadOnly Property Posizione As Double
        Get
            Return _inizio + _orologio.Elapsed.TotalSeconds
        End Get
    End Property

    ''' <summary>La durata del video in secondi e le sue dimensioni (le legge da ffmpeg). Vero se si sono lette.</summary>
    Public Shared Function Informazioni(percorso As String, ByRef durata As Double, ByRef larghezza As Integer, ByRef altezza As Integer) As Boolean
        Try
            Dim codice As Integer
            Dim testo As String = FfmpegUtil.Esegui("-hide_banner -i " & FfmpegUtil.Virgolette(percorso), codice, 20000)
            Dim d As Match = Regex.Match(testo, "Duration:\s*(\d+):(\d+):([\d.]+)")
            If d.Success Then
                durata = CInt(d.Groups(1).Value) * 3600.0 + CInt(d.Groups(2).Value) * 60.0 + Double.Parse(d.Groups(3).Value, Globalization.CultureInfo.InvariantCulture)
            End If
            Dim v As Match = Regex.Match(testo, "Video:.*?(\d{2,5})x(\d{2,5})")
            If Not v.Success Then Return False
            larghezza = CInt(v.Groups(1).Value)
            altezza = CInt(v.Groups(2).Value)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Shared Function Pari(valore As Double) As Integer
        Return Math.Max(2, CInt(Math.Floor(valore / 2.0)) * 2)
    End Function

    ''' <summary>Parte da un punto del video. Restituisce "" se va bene, altrimenti il motivo.</summary>
    Public Function Avvia(percorso As String, daSecondi As Double, larghezza As Integer, altezza As Integer) As String
        Try
            If FfmpegUtil.Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            _fermato = False
            _inizio = Math.Max(0.0, daSecondi)
            Dim rapporto As Double = Math.Min(1.0, Math.Min(1280.0 / larghezza, 720.0 / altezza))
            _larghezza = Pari(larghezza * rapporto)
            _altezza = Pari(altezza * rapporto)
            Dim da As String = FfmpegUtil.Secondi(_inizio)

            ' l'audio
            _buffer = New BufferedWaveProvider(New WaveFormat(48000, 16, 2), TimeSpan.FromSeconds(4))
            _buffer.DiscardOnBufferOverflow = True
            Dim infoAudio As ProcessStartInfo = FfmpegUtil.Prepara("-hide_banner -loglevel error -re -ss " & da & " -i " & FfmpegUtil.Virgolette(percorso) & " -vn -f s16le -ar 48000 -ac 2 pipe:1")
            _audio = Process.Start(infoAudio)
            _audio.BeginErrorReadLine()
            _threadAudio = New Thread(AddressOf LeggiAudio)
            _threadAudio.IsBackground = True
            _threadAudio.Start()
            _uscita = New WaveOutEvent()
            _uscita.DesiredLatency = 120
            _uscita.Init(_buffer)
            _uscita.Play()

            ' le immagini, un attimo dopo l'audio
            Thread.Sleep(120)
            Dim infoVideo As ProcessStartInfo = FfmpegUtil.Prepara("-hide_banner -loglevel error -re -ss " & da & " -i " & FfmpegUtil.Virgolette(percorso) &
                                                                  " -an -vf ""scale=" & _larghezza.ToString() & ":" & _altezza.ToString() & ":flags=bicubic,format=bgr24"" -f rawvideo -pix_fmt bgr24 pipe:1")
            _video = Process.Start(infoVideo)
            _video.BeginErrorReadLine()
            _orologio.Restart()
            _threadVideo = New Thread(AddressOf LeggiFotogrammi)
            _threadVideo.IsBackground = True
            _threadVideo.Start()
            Return ""
        Catch ex As Exception
            _log.ScriviLog("LettoreVideoInterno.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Ferma()
            Return "Non è stato possibile aprire il video: " & ex.Message
        End Try
    End Function

    Private Sub LeggiAudio()
        Try
            Dim flusso As Stream = _audio.StandardOutput.BaseStream
            Dim blocco(16383) As Byte
            While Not _fermato
                Dim n As Integer = flusso.Read(blocco, 0, blocco.Length)
                If n <= 0 Then Exit While
                ' un po' alla volta: ffmpeg manda a velocità reale, ma con -re può dare qualche secondo in anticipo all'inizio
                While Not _fermato AndAlso _buffer.BufferedDuration.TotalSeconds > 1.5
                    Thread.Sleep(20)
                End While
                _buffer.AddSamples(blocco, 0, n)
            End While
        Catch ex As Exception
            If Not _fermato Then _log.ScriviLog("LettoreVideoInterno.LeggiAudio: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiFotogrammi()
        Try
            Dim byteFotogramma As Integer = _larghezza * _altezza * 3
            Dim buffer(byteFotogramma - 1) As Byte
            Dim flusso As Stream = _video.StandardOutput.BaseStream
            While Not _fermato
                Dim letti As Integer = 0
                While letti < byteFotogramma
                    Dim n As Integer = flusso.Read(buffer, letti, byteFotogramma - letti)
                    If n <= 0 Then Exit While
                    letti += n
                End While
                If letti < byteFotogramma Then Exit While
                Dim bmp As New Bitmap(_larghezza, _altezza, PixelFormat.Format24bppRgb)
                Dim dati As BitmapData = bmp.LockBits(New Rectangle(0, 0, _larghezza, _altezza), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb)
                Try
                    For y As Integer = 0 To _altezza - 1
                        System.Runtime.InteropServices.Marshal.Copy(buffer, y * _larghezza * 3, IntPtr.Add(dati.Scan0, y * dati.Stride), _larghezza * 3)
                    Next
                Finally
                    bmp.UnlockBits(dati)
                End Try
                RaiseEvent Fotogramma(bmp)
            End While
        Catch ex As Exception
            If Not _fermato Then _log.ScriviLog("LettoreVideoInterno.LeggiFotogrammi: " & ex.Message)
        End Try
        If Not _fermato Then
            ' il video è finito: si lascia suonare quello che resta dell'audio, poi si avvisa
            Try
                Dim limite As DateTime = DateTime.Now.AddSeconds(3)
                While Not _fermato AndAlso _buffer IsNot Nothing AndAlso _buffer.BufferedDuration.TotalMilliseconds > 50 AndAlso DateTime.Now < limite
                    Thread.Sleep(30)
                End While
            Catch
            End Try
            If Not _fermato Then RaiseEvent Finito()
        End If
    End Sub

    ''' <summary>Ferma immagini e suono.</summary>
    Public Sub Ferma()
        _fermato = True
        _orologio.Stop()
        For Each p As Process In New Process() {_video, _audio}
            Try
                If p IsNot Nothing AndAlso Not p.HasExited Then p.Kill()
            Catch
            End Try
        Next
        Try
            If _uscita IsNot Nothing Then
                _uscita.Stop()
                _uscita.Dispose()
            End If
        Catch
        End Try
        _uscita = Nothing
        For Each t As Thread In New Thread() {_threadVideo, _threadAudio}
            Try
                If t IsNot Nothing AndAlso t IsNot Thread.CurrentThread Then t.Join(1500)
            Catch
            End Try
        Next
        For Each p As Process In New Process() {_video, _audio}
            Try
                If p IsNot Nothing Then p.Dispose()
            Catch
            End Try
        Next
        _video = Nothing
        _audio = Nothing
        _threadVideo = Nothing
        _threadAudio = Nothing
    End Sub

End Class
