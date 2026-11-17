' ============================================================================================
' WGFotocameraMotore - la videocamera che manda i fotogrammi all'anteprima e, se serve, li registra
' ============================================================================================
'
' Una videocamera si può aprire una volta sola, quindi un solo ffmpeg fa tutto: legge la videocamera e da lì escono due strade. Una
' è l'anteprima (fotogrammi ridotti, senza compressione, che arrivano a WinGabriel dal canale di uscita e si disegnano a tutto schermo),
' l'altra è la registrazione del video (mp4 senza audio, a fotogrammi regolari) oppure, in modalità foto, un file jpg che si riscrive
' quattro volte al secondo con l'ultimo fotogramma a piena risoluzione: lo scatto prende quello.
'
' Orizzontale o verticale: la verticale ritaglia al centro un'immagine 9:16, prima di tutto, così anteprima e video hanno lo stesso taglio.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading

Public NotInheritable Class RipresaFotocamera
    Public Camera As String = ""
    Public Larghezza As Integer = 1280
    Public Altezza As Integer = 720
    Public Fps As Integer = 30
    Public Codec As String = ""
    Public FormatoPixel As String = ""
    Public Verticale As Boolean
    ''' <summary>0 alta, 1 media, 2 bassa.</summary>
    Public Qualita As Integer = 1
    ''' <summary>Se non è vuoto, il motore registra il video in questo file mp4 (senza audio).</summary>
    Public FileVideo As String = ""
    ''' <summary>Se non è vuoto (modalità foto), il motore tiene aggiornato questo jpg con l'ultimo fotogramma.</summary>
    Public FileFotogramma As String = ""
End Class

Public NotInheritable Class MotoreFotocamera

    Private ReadOnly _log As New Logger()
    Private _processo As Process
    Private _thread As Thread
    Private _ripresa As RipresaFotocamera
    Private ReadOnly _primo As New ManualResetEvent(False)
    Private _istantePrimoFotogramma As Long
    Private _fermato As Boolean
    Private ReadOnly _errori As New StringBuilder()
    Private _larghezzaAnteprima As Integer
    Private _altezzaAnteprima As Integer

    ''' <summary>Un fotogramma dell'anteprima. Chi lo riceve ne diventa il proprietario e lo elimina.</summary>
    Public Event Fotogramma(immagine As Bitmap)
    ''' <summary>La videocamera si è fermata da sola (scollegata, occupata da un altro programma).</summary>
    Public Event Interrotta()

    ''' <summary>Di quanti millisecondi il primo fotogramma precede l'istante dato (preso con Stopwatch.GetTimestamp).</summary>
    Public Function MillisecondiPrimaDi(istante As Long) As Integer
        Return CInt(Math.Max(0L, (istante - _istantePrimoFotogramma) * 1000L \ Stopwatch.Frequency))
    End Function

    Public ReadOnly Property InFunzione As Boolean
        Get
            Try
                Return _processo IsNot Nothing AndAlso Not _processo.HasExited
            Catch
                Return False
            End Try
        End Get
    End Property

    Private Shared Function Pari(valore As Double) As Integer
        Return Math.Max(2, CInt(Math.Floor(valore / 2.0)) * 2)
    End Function

    ''' <summary>Le dimensioni dell'immagine dopo il taglio verticale, e quelle dell'anteprima (dentro 1280 per 720).</summary>
    Private Sub CalcolaDimensioni(r As RipresaFotocamera)
        Dim w As Integer = r.Larghezza
        Dim h As Integer = r.Altezza
        If r.Verticale Then w = Pari(h * 9.0 / 16.0)
        Dim rapporto As Double = Math.Min(1.0, Math.Min(1280.0 / w, 720.0 / h))
        _larghezzaAnteprima = Pari(w * rapporto)
        _altezzaAnteprima = Pari(h * rapporto)
    End Sub

    ''' <summary>Apre la videocamera e aspetta il primo fotogramma. Restituisce "" se è andata bene, altrimenti il motivo.</summary>
    Public Function Avvia(r As RipresaFotocamera) As String
        Try
            If FfmpegUtil.Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            _ripresa = r
            _fermato = False
            _primo.Reset()
            CalcolaDimensioni(r)
            Dim taglio As String = If(r.Verticale, "crop='trunc(ih*9/16/2)*2':ih,", "")
            Dim crf As Integer = If(r.Qualita = 0, 16, If(r.Qualita = 1, 19, 24))
            Dim a As New StringBuilder("-y -hide_banner -loglevel error -f dshow -rtbufsize 512M -use_wallclock_as_timestamps 1")
            a.Append(" -video_size ").Append(r.Larghezza).Append("x").Append(r.Altezza).Append(" -framerate ").Append(r.Fps)
            If r.Codec <> "" Then
                a.Append(" -vcodec ").Append(r.Codec)
            ElseIf r.FormatoPixel <> "" Then
                a.Append(" -pixel_format ").Append(r.FormatoPixel)
            End If
            a.Append(" -i video=").Append(FfmpegUtil.Virgolette(r.Camera))
            ' Le due strade partono dall'immagine intera della videocamera, prima di qualsiasi riduzione dei colori: l'anteprima non eredita il
            ' taglio dei colori del video (yuv 4:2:0) e il video non eredita quello dell'anteprima. Le conversioni usano il calcolo preciso.
            Dim anteprima As String = "scale=" & _larghezzaAnteprima.ToString() & ":" & _altezzaAnteprima.ToString() & ":flags=bicubic+accurate_rnd+full_chroma_int,format=bgr24"
            ' i colori dei video in alta definizione sono quelli della norma bt709 (le videocamere danno bt601): senza questa conversione i colori si spostano
            Dim conversione As String = "scale=out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int,format=yuv420p"
            Dim fpsAnteprima As Integer = Math.Max(10, Math.Min(30, r.Fps))
            If r.FileVideo <> "" Then
                a.Append(" -filter_complex ").Append(FfmpegUtil.Virgolette("[0:v]" & taglio & "split=2[reg][ant];[reg]" & conversione & "[rv];[ant]" & anteprima & "[pv]"))
                a.Append(" -map [rv] -an -c:v libx264 -preset veryfast -crf ").Append(crf).Append(" -profile:v high -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv")
                a.Append(" -fps_mode cfr -r ").Append(r.Fps).Append(" -g ").Append(r.Fps * 2).Append(" -movflags +faststart ").Append(FfmpegUtil.Virgolette(r.FileVideo))
            ElseIf r.FileFotogramma <> "" Then
                a.Append(" -filter_complex ").Append(FfmpegUtil.Virgolette("[0:v]" & taglio & "split=2[foto][ant];[foto]fps=4,scale=out_range=pc:flags=accurate_rnd+full_chroma_int,format=yuvj420p[fo];[ant]" & anteprima & "[pv]"))
                a.Append(" -map [fo] -an -c:v mjpeg -q:v 1 -f image2 -update 1 -atomic_writing 1 ").Append(FfmpegUtil.Virgolette(r.FileFotogramma))
            Else
                a.Append(" -filter_complex ").Append(FfmpegUtil.Virgolette("[0:v]" & taglio & anteprima & "[pv]"))
            End If
            a.Append(" -map [pv] -an -f rawvideo -pix_fmt bgr24 -r ").Append(fpsAnteprima).Append(" pipe:1")

            Dim info As ProcessStartInfo = FfmpegUtil.Prepara(a.ToString())
            _processo = Process.Start(info)
            AddHandler _processo.ErrorDataReceived, Sub(s As Object, e As DataReceivedEventArgs)
                                                        If e.Data IsNot Nothing Then
                                                            SyncLock _errori
                                                                _errori.AppendLine(e.Data)
                                                            End SyncLock
                                                        End If
                                                    End Sub
            _processo.BeginErrorReadLine()
            _thread = New Thread(AddressOf LeggiFotogrammi)
            _thread.IsBackground = True
            _thread.Start()
            Dim attesa As Integer = 0
            While attesa < 20000
                If _primo.WaitOne(100) Then Return ""
                If _processo.HasExited Then Exit While
                attesa += 100
            End While
            Dim motivo As String
            SyncLock _errori
                motivo = _errori.ToString().Trim()
            End SyncLock
            Ferma()
            Return "Non è stato possibile aprire la videocamera. " & motivo
        Catch ex As Exception
            _log.ScriviLog("MotoreFotocamera.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Ferma()
            Return "Non è stato possibile aprire la videocamera: " & ex.Message
        End Try
    End Function

    Private Sub LeggiFotogrammi()
        Try
            Dim w As Integer = _larghezzaAnteprima
            Dim h As Integer = _altezzaAnteprima
            Dim byteFotogramma As Integer = w * h * 3
            Dim buffer(byteFotogramma - 1) As Byte
            Dim flusso As Stream = _processo.StandardOutput.BaseStream
            While True
                Dim letti As Integer = 0
                While letti < byteFotogramma
                    Dim n As Integer = flusso.Read(buffer, letti, byteFotogramma - letti)
                    If n <= 0 Then Exit While
                    letti += n
                End While
                If letti < byteFotogramma Then Exit While
                If Not _primo.WaitOne(0) Then
                    _istantePrimoFotogramma = Stopwatch.GetTimestamp()
                    _primo.Set()
                End If
                Dim bmp As New Bitmap(w, h, PixelFormat.Format24bppRgb)
                Dim dati As BitmapData = bmp.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb)
                Try
                    For y As Integer = 0 To h - 1
                        System.Runtime.InteropServices.Marshal.Copy(buffer, y * w * 3, IntPtr.Add(dati.Scan0, y * dati.Stride), w * 3)
                    Next
                Finally
                    bmp.UnlockBits(dati)
                End Try
                RaiseEvent Fotogramma(bmp)
            End While
        Catch ex As Exception
            If Not _fermato Then _log.ScriviLog("MotoreFotocamera.LeggiFotogrammi: " & ex.Message)
        End Try
        If Not _fermato Then RaiseEvent Interrotta()
    End Sub

    ''' <summary>Ferma la videocamera. Con la registrazione chiude bene il file mp4. Restituisce vero se il video c'è.</summary>
    Public Function Ferma() As Boolean
        Dim r As RipresaFotocamera = _ripresa
        _fermato = True
        Try
            If _processo IsNot Nothing Then
                Try
                    If Not _processo.HasExited Then
                        _processo.StandardInput.WriteLine("q")
                        _processo.StandardInput.Flush()
                    End If
                Catch
                End Try
                If Not _processo.WaitForExit(15000) Then
                    Try
                        _processo.Kill()
                    Catch
                    End Try
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("MotoreFotocamera.Ferma: " & ex.Message)
        Finally
            Try
                If _processo IsNot Nothing Then _processo.Dispose()
            Catch
            End Try
            _processo = Nothing
        End Try
        Try
            If _thread IsNot Nothing Then _thread.Join(2000)
        Catch
        End Try
        _thread = Nothing
        If r IsNot Nothing AndAlso r.FileVideo <> "" Then Return File.Exists(r.FileVideo) AndAlso New FileInfo(r.FileVideo).Length > 1000
        Return False
    End Function

End Class
