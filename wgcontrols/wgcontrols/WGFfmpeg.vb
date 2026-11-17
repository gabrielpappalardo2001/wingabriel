' WGFfmpeg - dove sta ffmpeg e come lo si lancia (serve a registratore, fotocamera ed editor video)
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>Dove sta ffmpeg e come lo si lancia.</summary>
Public NotInheritable Class FfmpegUtil

    Private Sub New()
    End Sub

    ''' <summary>Il percorso di ffmpeg.exe, o "" se non c'è.</summary>
    Public Shared Function Percorso() As String
        Try
            Dim p As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe")
            If File.Exists(p) Then Return p
        Catch
        End Try
        Return ""
    End Function

    Public Shared Function Prepara(argomenti As String) As ProcessStartInfo
        Dim info As New ProcessStartInfo(Percorso(), argomenti)
        info.UseShellExecute = False
        info.CreateNoWindow = True
        info.RedirectStandardError = True
        info.RedirectStandardOutput = True
        info.RedirectStandardInput = True
        info.StandardErrorEncoding = Encoding.UTF8
        info.StandardOutputEncoding = Encoding.UTF8
        Return info
    End Function

    ''' <summary>Lancia ffmpeg, aspetta la fine e restituisce quello che ha scritto sul canale degli errori (dove ffmpeg scrive le informazioni).</summary>
    Public Shared Function Esegui(argomenti As String, ByRef codiceUscita As Integer, Optional attesaMassimaMs As Integer = 60000) As String
        codiceUscita = -1
        If Percorso() = "" Then Return ""
        Using p As Process = Process.Start(Prepara(argomenti))
            Dim lettura As Threading.Tasks.Task(Of String) = p.StandardError.ReadToEndAsync()
            Dim lettura2 As Threading.Tasks.Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Try
                p.StandardInput.Close()
            Catch
            End Try
            If Not p.WaitForExit(attesaMassimaMs) Then
                Try
                    p.Kill()
                Catch
                End Try
            End If
            codiceUscita = If(p.HasExited, p.ExitCode, -1)
            Return lettura.Result
        End Using
    End Function

    ''' <summary>Come Esegui, ma restituisce anche quello che ffmpeg scrive sul canale normale (per esempio l'elenco dei filtri).</summary>
    Public Shared Function EseguiConUscita(argomenti As String, ByRef codiceUscita As Integer, ByRef standardOutput As String, Optional attesaMassimaMs As Integer = 60000) As String
        codiceUscita = -1
        standardOutput = ""
        If Percorso() = "" Then Return ""
        Using p As Process = Process.Start(Prepara(argomenti))
            Dim lettura As Threading.Tasks.Task(Of String) = p.StandardError.ReadToEndAsync()
            Dim lettura2 As Threading.Tasks.Task(Of String) = p.StandardOutput.ReadToEndAsync()
            Try
                p.StandardInput.Close()
            Catch
            End Try
            If Not p.WaitForExit(attesaMassimaMs) Then
                Try
                    p.Kill()
                Catch
                End Try
            End If
            codiceUscita = If(p.HasExited, p.ExitCode, -1)
            standardOutput = lettura2.Result
            Return lettura.Result
        End Using
    End Function

    Public Shared Function Secondi(valore As Double) As String
        Return valore.ToString("0.###", CultureInfo.InvariantCulture)
    End Function

    ''' <summary>Racchiude tra virgolette un percorso per la riga dei comandi.</summary>
    Public Shared Function Virgolette(percorso As String) As String
        Return """" & percorso.Replace("""", "") & """"
    End Function

    ''' <summary>Gli encoder video che il computer sa usare con la scheda grafica, dal migliore: "h264_nvenc", "h264_qsv", "h264_amf". Vuoto se non ce ne sono.</summary>
    Public Shared Function EncoderGpu() As String
        Static trovato As String = Nothing
        If trovato IsNot Nothing Then Return trovato
        trovato = ""
        Try
            If Percorso() = "" Then Return trovato
            Dim codice As Integer
            Dim elenco As String = Esegui("-hide_banner -encoders", codice, 15000)
            For Each nome As String In New String() {"h264_nvenc", "h264_qsv", "h264_amf"}
                If Not elenco.Contains(nome) Then Continue For
                ' un breve codice di prova: l'encoder esiste nel programma ma il computer potrebbe non avere la scheda
                Dim prova As String = Esegui("-hide_banner -loglevel error -f lavfi -i color=c=black:s=256x256:d=0.2 -frames:v 3 -c:v " & nome & " -f null -", codice, 15000)
                If codice = 0 Then
                    trovato = nome
                    Exit For
                End If
            Next
        Catch
        End Try
        Return trovato
    End Function

    ''' <summary>
    ''' Le videocamere collegate. Contano solo gli ingressi che Windows dichiara come videocamere: i dispositivi che non dichiarano niente (come
    ''' le videocamere virtuali di OBS) e quelli con "virtual" nel nome si contano solo se non c'è altro, e un nome ripetuto si conta una volta.
    ''' </summary>
    Public Shared Function Videocamere() As List(Of String)
        Dim reali As New List(Of String)()
        Dim altre As New List(Of String)()
        Try
            If Percorso() = "" Then Return reali
            Dim codice As Integer
            Dim testo As String = Esegui("-hide_banner -list_devices true -f dshow -i dummy", codice, 15000)
            Dim inVideo As Boolean = False
            For Each riga As String In testo.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                If riga.Contains("DirectShow video devices") Then
                    inVideo = True
                    Continue For
                End If
                If riga.Contains("DirectShow audio devices") Then
                    inVideo = False
                    Continue For
                End If
                If riga.Contains("Alternative name") Then Continue For
                Dim m As Match = Regex.Match(riga, """(.+?)""\s*\((video|audio|none)[^)]*\)")
                Dim nome As String = ""
                Dim tipo As String = ""
                If m.Success Then
                    nome = m.Groups(1).Value
                    tipo = m.Groups(2).Value
                ElseIf inVideo Then
                    Dim v As Match = Regex.Match(riga, """(.+?)""\s*$")
                    If v.Success Then
                        nome = v.Groups(1).Value
                        tipo = "video"
                    End If
                End If
                If nome = "" OrElse tipo = "audio" Then Continue For
                Dim virtuale As Boolean = nome.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0
                Dim destinazione As List(Of String) = If(tipo = "video" AndAlso Not virtuale, reali, altre)
                If Not reali.Contains(nome, StringComparer.OrdinalIgnoreCase) AndAlso Not altre.Contains(nome, StringComparer.OrdinalIgnoreCase) Then destinazione.Add(nome)
            Next
        Catch
        End Try
        If reali.Count > 0 Then Return reali
        Return altre
    End Function

    ''' <summary>Unisce un video senza audio e un audio in un mp4. ritardoAudioMs è di quanto l'audio parte dopo il video.</summary>
    Public Shared Function UnisciVideoAudio(video As String, ritardoAudioMs As Integer, audio As String, destinazione As String) As String
        Try
            If Percorso() = "" Then Return "Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco."
            Dim ritardo As String = FfmpegUtil.Secondi(ritardoAudioMs / 1000.0)
            Dim codice As Integer
            ' Se il video parte prima dell'audio (la videocamera si apre, e per un po' l'immagine è scura finché l'esposizione non si assesta),
            ' quell'inizio si toglie: il video si ricodifica dal punto in cui parte l'audio, così immagine e voce restano allineate.
            If ritardoAudioMs > 250 Then
                Dim taglio As String = Esegui("-y -hide_banner -loglevel error -ss " & ritardo & " -i " & Virgolette(video) & " -i " & Virgolette(audio) &
                                              " -map 0:v:0 -map 1:a:0 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -profile:v high -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv" &
                                              " -c:a aac -b:a 256k -shortest -movflags +faststart " & Virgolette(destinazione), codice, 3600000)
                If codice = 0 AndAlso File.Exists(destinazione) AndAlso New FileInfo(destinazione).Length > 1000 Then Return ""
                Try
                    If File.Exists(destinazione) Then File.Delete(destinazione)
                Catch
                End Try
            End If
            Dim uscita As String = Esegui("-y -hide_banner -loglevel error -i " & Virgolette(video) & " -itsoffset " & ritardo & " -i " & Virgolette(audio) &
                                          " -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 256k -shortest -movflags +faststart " & Virgolette(destinazione), codice, 3600000)
            If codice <> 0 OrElse Not File.Exists(destinazione) Then Return "Non è stato possibile unire il video e l'audio. " & uscita.Trim()
            Return ""
        Catch ex As Exception
            Return "Non è stato possibile unire il video e l'audio: " & ex.Message
        End Try
    End Function

End Class
