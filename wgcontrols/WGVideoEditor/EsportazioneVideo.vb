' ============================================================================================
' EsportazioneVideo - scrive il video montato, con la scheda grafica dove si può
' ============================================================================================
'
' Si prova prima con l'encoder della scheda grafica (e con la decodifica accelerata); se la scheda non lo regge, si rifà da capo con
' il processore. Mentre lavora, ffmpeg dice a che punto è: da lì si ricavano la percentuale e il tempo che manca.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Threading.Tasks

Public NotInheritable Class EsportazioneVideo

    Private Shared ReadOnly _log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Scrive il video. avanzamento riceve la frazione fatta (da 0 a 1) e i secondi che mancano, circa. Restituisce "" se è andata
    ''' bene, altrimenti il motivo.
    ''' </summary>
    Public Shared Function Esporta(p As ProgettoVideo, imp As ImpostazioniEsportazione, avanzamento As Action(Of Double, Double)) As String
        Try
            If FfmpegUtil.Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            Dim montaggio As Montaggio = CostruttoreMontaggio.Costruisci(p, imp, False)
            Dim gpu As String = FfmpegUtil.EncoderGpu()
            Dim temporaneo As String = Path.Combine(Path.GetDirectoryName(imp.Destinazione), "." & Path.GetFileNameWithoutExtension(imp.Destinazione) & ".wg-parziale" & imp.Estensione())
            Dim errore As String = ""
            Dim riuscito As Boolean = False
            If gpu <> "" Then riuscito = Lancia(p, imp, montaggio, gpu, temporaneo, avanzamento, errore)
            If Not riuscito Then
                If gpu <> "" Then _log.ScriviLog("EsportazioneVideo.Esporta: la scheda grafica non ce l'ha fatta, si riparte con il processore. " & errore)
                errore = ""
                riuscito = Lancia(p, imp, montaggio, "", temporaneo, avanzamento, errore)
            End If
            If Not riuscito Then
                Try
                    If File.Exists(temporaneo) Then File.Delete(temporaneo)
                Catch
                End Try
                Return "Non è stato possibile esportare il video. " & errore
            End If
            If File.Exists(imp.Destinazione) Then File.Delete(imp.Destinazione)
            File.Move(temporaneo, imp.Destinazione)
            Return ""
        Catch ex As Exception
            _log.ScriviLog("EsportazioneVideo.Esporta: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile esportare il video: " & ex.Message
        End Try
    End Function

    Private Shared Function Lancia(p As ProgettoVideo, imp As ImpostazioniEsportazione, montaggio As Montaggio, gpu As String, destinazione As String,
                                   avanzamento As Action(Of Double, Double), ByRef errore As String) As Boolean
        Dim a As New StringBuilder()
        a.Append("-y -hide_banner -loglevel error -progress pipe:1 -nostats ")
        If gpu <> "" Then a.Append("-hwaccel auto ")
        a.Append("-i ").Append(FfmpegUtil.Virgolette(p.Sorgente)).Append(" ")
        For Each ingresso As KeyValuePair(Of String, String) In montaggio.Ingressi
            If ingresso.Key <> "" Then a.Append(ingresso.Key).Append(" ")
            a.Append("-i ").Append(FfmpegUtil.Virgolette(ingresso.Value)).Append(" ")
        Next
        a.Append("-filter_complex ").Append(FfmpegUtil.Virgolette(montaggio.Script)).Append(" -map [vout] -map [aout] ")
        a.Append(CostruttoreMontaggio.OpzioniVideo(imp, gpu)).Append(" -c:a aac -b:a ").Append(imp.BitrateAudio.ToString()).Append("k ")
        a.Append("-t ").Append(FfmpegUtil.Secondi(montaggio.Durata + 0.5)).Append(" ")
        If Not imp.Formato.StartsWith("mkv") Then a.Append("-movflags +faststart ")
        a.Append(FfmpegUtil.Virgolette(destinazione))

        Dim messaggi As New StringBuilder()
        Dim cronometro As Stopwatch = Stopwatch.StartNew()
        Using processo As Process = Process.Start(FfmpegUtil.Prepara(a.ToString()))
            Dim lettura As Task(Of String) = processo.StandardError.ReadToEndAsync()
            Try
                processo.StandardInput.Close()
            Catch
            End Try
            While True
                Dim riga As String = processo.StandardOutput.ReadLine()
                If riga Is Nothing Then Exit While
                If riga.StartsWith("out_time_us=") OrElse riga.StartsWith("out_time_ms=") Then
                    Dim microsecondi As Double
                    If Double.TryParse(riga.Substring(riga.IndexOf("="c) + 1), NumberStyles.Float, CultureInfo.InvariantCulture, microsecondi) AndAlso microsecondi > 0 Then
                        Dim frazione As Double = Math.Max(0.0, Math.Min(1.0, microsecondi / 1000000.0 / Math.Max(0.1, montaggio.Durata)))
                        Dim mancano As Double = If(frazione > 0.01, cronometro.Elapsed.TotalSeconds * (1.0 - frazione) / frazione, -1.0)
                        If avanzamento IsNot Nothing Then avanzamento(frazione, mancano)
                    End If
                End If
            End While
            processo.WaitForExit()
            messaggi.Append(lettura.Result)
            errore = messaggi.ToString().Trim()
            If errore.Length > 600 Then errore = errore.Substring(errore.Length - 600)
            If processo.ExitCode = 0 AndAlso File.Exists(destinazione) AndAlso New FileInfo(destinazione).Length > 1000 Then Return True
        End Using
        Try
            If File.Exists(destinazione) Then File.Delete(destinazione)
        Catch
        End Try
        Return False
    End Function

    ''' <summary>Prepara un file wav con l'audio del montaggio sulla linea del tempo intera: serve per ascoltare mentre si monta.</summary>
    Public Shared Function PreparaAnteprimaAudio(p As ProgettoVideo, destinazioneWav As String) As String
        Try
            If FfmpegUtil.Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            Dim montaggio As Montaggio = CostruttoreMontaggio.Costruisci(p, New ImpostazioniEsportazione(), True)
            Dim a As New StringBuilder("-y -hide_banner -loglevel error -i ")
            a.Append(FfmpegUtil.Virgolette(p.Sorgente)).Append(" ")
            For Each ingresso As KeyValuePair(Of String, String) In montaggio.Ingressi
                If ingresso.Key <> "" Then a.Append(ingresso.Key).Append(" ")
                a.Append("-i ").Append(FfmpegUtil.Virgolette(ingresso.Value)).Append(" ")
            Next
            a.Append("-filter_complex ").Append(FfmpegUtil.Virgolette(montaggio.Script)).Append(" -map [aout] -vn -t ").Append(FfmpegUtil.Secondi(p.Durata + 0.5))
            a.Append(" -c:a pcm_s16le ").Append(FfmpegUtil.Virgolette(destinazioneWav))
            Dim vuoto As String = ""
            Dim errori As String = AnalisiVideo.Lancia(a.ToString(), vuoto, 600000)
            If Not File.Exists(destinazioneWav) OrElse New FileInfo(destinazioneWav).Length < 1000 Then Return "Non è stato possibile preparare l'audio. " & errori.Trim()
            Return ""
        Catch ex As Exception
            _log.ScriviLog("EsportazioneVideo.PreparaAnteprimaAudio: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile preparare l'audio: " & ex.Message
        End Try
    End Function

End Class
