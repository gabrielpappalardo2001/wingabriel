Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

' Il prelievo dei video di YouTube nel centro di controllo: un video, una playlist intera o un canale intero.
' Si usa yt-dlp, lo stesso programma che la parte YouTube di WinGabriel usa per cercare e leggere i video (e ffmpeg, accanto a lui,
' per unire video e audio o per fare l'mp3). Il lavoro compare nell'elenco del centro di controllo con la percentuale, si può
' annullare come gli altri e alla fine si avvisa con la notifica di fine operazione.
Partial Public Class WGCentroDiControllo

    Private Class LavoroYoutube
        Inherits LavoroBase

        Public Indirizzo As String = ""
        Public Titolo As String = ""
        Public Cartella As String = ""
        Public SoloAudio As Boolean
        Public Intero As Boolean
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public ElementoCorrente As Integer = 0
        Public ElementiTotali As Integer = 0
        Public ElementiFiniti As Integer = 0
        Public Processo As Process
        Public TempoInizio As DateTime = DateTime.Now
    End Class

    ''' <summary>
    ''' Preleva da YouTube. intero è vero per una playlist o per un canale (tutti i suoi video), falso per un singolo video.
    ''' I file vanno nella cartella indicata (le playlist e i canali in una sottocartella con il loro nome); con soloAudio si fa un mp3.
    ''' </summary>
    Public Sub AggiungiDownloadYoutube(indirizzo As String, titolo As String, cartella As String, soloAudio As Boolean, intero As Boolean)
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then
                NotificaFineOperazione("Indirizzo YouTube non indicato.")
                Return
            End If
            If String.IsNullOrWhiteSpace(cartella) Then
                NotificaFineOperazione("Cartella per il prelievo da YouTube non indicata.")
                Return
            End If
            Dim lavoro As New LavoroYoutube()
            lavoro.Indirizzo = indirizzo
            lavoro.Titolo = If(String.IsNullOrWhiteSpace(titolo), "YouTube", titolo)
            lavoro.Cartella = cartella
            lavoro.SoloAudio = soloAudio
            lavoro.Intero = intero
            lavoro.Info = "Preparazione del prelievo"
            lavoro.TempoInizio = DateTime.Now
            RegistraLavoro(lavoro, "Prelievo da YouTube: " & lavoro.Titolo, StatoOperazioneCorrente.ScaricamentoYoutube)
            lavoro.InPreparazione = False
            AggiungiRisorsa(lavoro, "youtube:" & indirizzo)
            lavoro.ThreadLavoro = New Thread(AddressOf EseguiDownloadYoutube)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AggiungiDownloadYoutube: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ArgomentiComuniYoutube() As String
        Dim a As String = "--cache-dir .cache --no-warnings --extractor-args ""youtube:lang=it"""
        Try
            Dim deno As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deno.exe")
            If File.Exists(deno) Then a &= " --js-runtimes ""deno:" & deno & """"
        Catch
        End Try
        Return a
    End Function

    Private Sub EseguiDownloadYoutube(parametro As Object)
        Dim lavoro As LavoroYoutube = TryCast(parametro, LavoroYoutube)
        Dim esito As String = ""
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            lavoro.InEsecuzione = True
            lavoro.TempoInizio = DateTime.Now

            Dim programma As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe")
            If Not File.Exists(programma) Then
                NotificaFineOperazione("Prelievo da YouTube non riuscito: manca yt-dlp, il centro di controllo lo scarica da solo, riprova tra poco.")
                Return
            End If
            Directory.CreateDirectory(lavoro.Cartella)

            Dim modello As String
            If lavoro.Intero Then
                modello = Path.Combine(lavoro.Cartella, "%(playlist_title).100B", "%(playlist_index)03d - %(title).120B [%(id)s].%(ext)s")
            Else
                modello = Path.Combine(lavoro.Cartella, "%(title).150B [%(id)s].%(ext)s")
            End If
            Dim argomenti As New StringBuilder()
            argomenti.Append(ArgomentiComuniYoutube())
            argomenti.Append(" --newline --ignore-errors --no-overwrites --continue --windows-filenames --retries 10 --fragment-retries 10")
            argomenti.Append(" --progress-template ""download:WGP %(progress._percent_str)s""")
            argomenti.Append(If(lavoro.Intero, " --yes-playlist", " --no-playlist"))
            If lavoro.SoloAudio Then
                argomenti.Append(" -x --audio-format mp3 --audio-quality 0")
            Else
                argomenti.Append(" -f ""bv*[height<=1080]+ba/b[height<=1080]/b"" --merge-output-format mp4")
            End If
            argomenti.Append(" -o """).Append(modello).Append(""" """).Append(lavoro.Indirizzo).Append("""")

            Dim info As New ProcessStartInfo(programma, argomenti.ToString())
            info.WorkingDirectory = Path.GetDirectoryName(programma)
            info.UseShellExecute = False
            info.CreateNoWindow = True
            info.RedirectStandardOutput = True
            info.RedirectStandardError = True
            info.StandardOutputEncoding = Encoding.UTF8
            info.StandardErrorEncoding = Encoding.UTF8
            Dim ultimoErrore As New StringBuilder()
            Dim monotono As Integer = 0
            lavoro.Processo = Process.Start(info)
            AddHandler lavoro.Processo.OutputDataReceived,
                Sub(s As Object, e As DataReceivedEventArgs)
                    If e.Data Is Nothing Then Return
                    ElaboraRigaYoutube(lavoro, e.Data)
                End Sub
            AddHandler lavoro.Processo.ErrorDataReceived,
                Sub(s As Object, e As DataReceivedEventArgs)
                    If e.Data Is Nothing Then Return
                    SyncLock ultimoErrore
                        ultimoErrore.AppendLine(e.Data)
                        If ultimoErrore.Length > 2000 Then ultimoErrore.Remove(0, ultimoErrore.Length - 2000)
                    End SyncLock
                End Sub
            lavoro.Processo.BeginOutputReadLine()
            lavoro.Processo.BeginErrorReadLine()
            While Not lavoro.Processo.WaitForExit(250)
                If lavoro.Annulla Then
                    Try
                        lavoro.Processo.Kill()
                    Catch
                    End Try
                    Exit While
                End If
            End While
            Dim codice As Integer = -1
            Try
                lavoro.Processo.WaitForExit()
                codice = lavoro.Processo.ExitCode
            Catch
            End Try

            lavoro.InEsecuzione = False
            If lavoro.Annulla Then
                esito = "Prelievo da YouTube annullato."
            ElseIf codice = 0 Then
                lavoro.Progresso = 100
                esito = "Prelievo da YouTube completato: " & lavoro.Titolo & ". File in " & lavoro.Cartella & "."
            ElseIf lavoro.ElementiFiniti > 0 Then
                esito = "Prelievo da YouTube terminato, ma alcuni video non si sono potuti scaricare: " & lavoro.Titolo & ". File in " & lavoro.Cartella & "."
            Else
                SyncLock ultimoErrore
                    _log.ScriviLog("WGCentroDiControllo.EseguiDownloadYoutube: yt-dlp ha restituito " & codice.ToString() & ": " & ultimoErrore.ToString())
                End SyncLock
                esito = "Prelievo da YouTube non riuscito: " & lavoro.Titolo & ". Controlla il log."
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EseguiDownloadYoutube: " & ex.Message & Environment.NewLine & ex.StackTrace)
            esito = "Errore durante il prelievo da YouTube."
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    If lavoro.Processo IsNot Nothing Then lavoro.Processo.Dispose()
                End If
            Catch
            End Try
            Try
                If lavoro IsNot Nothing Then RimuoviLavoro(lavoro)
            Catch exRim As Exception
                _log.ScriviLog("WGCentroDiControllo.EseguiDownloadYoutube (rimuovi): " & exRim.Message)
            End Try
        End Try
        If esito <> "" Then NotificaFineOperazione(esito)
    End Sub

    ''' <summary>Una riga di quello che scrive yt-dlp: da qui si ricavano il video a cui è arrivato e la percentuale.</summary>
    Private Sub ElaboraRigaYoutube(lavoro As LavoroYoutube, riga As String)
        Try
            Dim m As Match = Regex.Match(riga, "Downloading item (\d+) of (\d+)")
            If m.Success Then
                If lavoro.ElementoCorrente > 0 Then lavoro.ElementiFiniti = Math.Max(lavoro.ElementiFiniti, lavoro.ElementoCorrente - 1)
                lavoro.ElementoCorrente = CInt(m.Groups(1).Value)
                lavoro.ElementiTotali = CInt(m.Groups(2).Value)
                lavoro.Info = "Video " & lavoro.ElementoCorrente.ToString() & " di " & lavoro.ElementiTotali.ToString()
                Return
            End If
            m = Regex.Match(riga, "WGP\s+([\d.]+)%")
            If m.Success Then
                Dim percentuale As Double
                If Double.TryParse(m.Groups(1).Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, percentuale) Then
                    Dim totale As Integer = Math.Max(1, lavoro.ElementiTotali)
                    Dim base As Integer = Math.Max(0, lavoro.ElementoCorrente - 1)
                    Dim globale As Integer = CInt(Math.Min(99.0, (base * 100.0 + percentuale) / totale))
                    If globale > lavoro.Progresso Then lavoro.Progresso = globale
                    If lavoro.ElementiTotali = 0 Then lavoro.ElementiFiniti = If(percentuale >= 100.0, 1, lavoro.ElementiFiniti)
                End If
                Return
            End If
            m = Regex.Match(riga, "\[download\] Destination: (.+)$")
            If m.Success Then
                lavoro.Info = "Scarico " & Path.GetFileNameWithoutExtension(m.Groups(1).Value)
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.ElaboraRigaYoutube: " & ex.Message)
        End Try
    End Sub

    Private Function InfoYoutube(lavoro As LavoroYoutube) As String
        Dim t As New StringBuilder()
        t.AppendLine(lavoro.Descrizione)
        If Not String.IsNullOrWhiteSpace(lavoro.Info) Then t.AppendLine(lavoro.Info)
        t.AppendLine("Formato: " & If(lavoro.SoloAudio, "solo audio, mp3", "video, mp4"))
        t.AppendLine("Destinazione: " & lavoro.Cartella)
        t.AppendLine("Avanzamento: " & lavoro.Progresso.ToString() & " per cento")
        Return t.ToString().TrimEnd()
    End Function

End Class
