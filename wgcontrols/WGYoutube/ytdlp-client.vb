Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Net
Imports System.Xml
Imports System.Globalization
Imports System.Linq
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' I valori numerici sono espliciti e non vanno modificati: vengono salvati nel database
''' (preferiti, recenti, ricerche, iscrizioni) e cambiarli renderebbe illeggibili gli archivi esistenti.
''' </summary>
Public Enum YoutubeItemType
    Video = 0
    Playlist = 1
    Channel = 2
    Unknown = 6
End Enum

''' <summary>
''' Criteri di ordinamento per elenchi di risultati (video, playlist, canali, ecc.).
''' Nota: non tutti i campi sono sempre disponibili (dipende da yt-dlp --flat-playlist e dalla sorgente).
''' Usa SonoDisponibiliDatiOrdinamento / OttieniOrdinamentiDisponibili per verificare.
''' </summary>
Public Enum YoutubeSortBy
    None = 0
    DateAscending          ' Data di pubblicazione crescente (meno recente prima)
    DateDescending         ' Data di pubblicazione decrescente (più recente prima)
    ViewsAscending         ' Visualizzazioni crescenti
    ViewsDescending        ' Visualizzazioni decrescenti
    LikesAscending         ' Mi piace crescenti
    LikesDescending        ' Mi piace decrescenti
    SubscribersAscending   ' Iscritti crescenti (utile soprattutto per canali)
    SubscribersDescending  ' Iscritti decrescenti
    TitleAscending         ' Titolo A-Z
    TitleDescending        ' Titolo Z-A
    DurationAscending      ' Durata crescente
    DurationDescending     ' Durata decrescente
    VideoCountAscending    ' Numero video (per playlist) crescente
    VideoCountDescending   ' Numero video (per playlist) decrescente
End Enum

Public Class YoutubeSearchResult
    Public Property Title As String
    Public Property Id As String
    Public Property Url As String
    Public Property ChannelName As String
    Public Property ChannelId As String
    Public Property DurationText As String
    Public Property ItemType As YoutubeItemType
    Public Property DurationSeconds As Long?
    ''' <summary>Data di pubblicazione (se disponibile da yt-dlp).</summary>
    Public Property UploadDate As Date?
    ''' <summary>Numero di visualizzazioni (se disponibile).</summary>
    Public Property ViewCount As Long?
    ''' <summary>Numero di mi piace (spesso assente con --flat-playlist).</summary>
    Public Property LikeCount As Long?
    ''' <summary>Numero di iscritti (principalmente per ItemType = Channel).</summary>
    Public Property SubscriberCount As Long?
    ''' <summary>Numero di commenti (se disponibile).</summary>
    Public Property CommentCount As Long?
    ''' <summary>Numero di video (principalmente per playlist).</summary>
    Public Property VideoCount As Integer?
    ''' <summary>Vero se è uno Short, falso se non lo è, Nothing se non si sa (dal feed RSS o dall'indirizzo si capisce).</summary>
    Public Property IsShort As Boolean?
End Class

Public Class YoutubeChapter
    Public Property Title As String
    Public Property StartSeconds As Double
    Public Property EndSeconds As Double
End Class

Public Class YoutubeSponsorSegment
    Public Property Category As String
    Public Property Title As String
    Public Property StartSeconds As Double
    Public Property EndSeconds As Double
End Class

Public Class YoutubeComment
    Public Property Author As String
    Public Property Text As String
    Public Property Timestamp As String
    Public Property LikeCount As Long
    Public Property Id As String
    Public Property ParentId As String
    Public Property IsPinned As Boolean
    Public Property AuthorIsUploader As Boolean
    Public Property Replies As New List(Of YoutubeComment)
End Class

Public Class YoutubeItemDetails
    Public Property ItemType As YoutubeItemType
    Public Property Title As String
    Public Property Url As String
    Public Property Id As String
    Public Property Description As String
    Public Property UploadDate As Date?
    Public Property DurationText As String
    Public Property ViewCount As Long?
    Public Property LikeCount As Long?
    Public Property CommentCount As Long?
    Public Property ChannelName As String
    Public Property ChannelId As String
    Public Property ChannelUrl As String
    Public Property SubscriberCount As Long?
    Public Property TotalVideoCount As Long?
    Public Property VideoCount As Integer?
    Public Property EstimatedDurationText As String
    Public Property Comments As List(Of YoutubeComment)
    Public Property Tags As String
    Public Property Categories As String
    Public Property IsLive As Boolean?
    Public Property WasLive As Boolean?
    Public Property AgeLimit As Integer?
    Public Property License As String
    Public Property ChannelVerificato As Boolean?
    Public Property TotalViewCount As Long?
End Class

''' <summary>Quando è stato pubblicato il video, come nei filtri di YouTube.</summary>
Public Enum FiltroData
    Qualsiasi = 0
    UltimaOra = 1
    Oggi = 2
    QuestaSettimana = 3
    QuestoMese = 4
    QuestoAnno = 5
End Enum

''' <summary>
''' I filtri di una ricerca. Data e funzioni (diretta, sottotitoli, alta definizione, 4K, Creative Commons) li applica
''' YouTube stesso, con il parametro "sp" dell'indirizzo di ricerca, quindi sono precisi e non perdono risultati.
''' "Nascondi gli Short" si applica dopo, sui risultati: YouTube non ha un filtro per toglierli.
''' </summary>
Public Class FiltriRicerca
    Public Property Data As FiltroData = FiltroData.Qualsiasi
    Public Property SoloDirette As Boolean
    Public Property ConSottotitoli As Boolean
    Public Property AltaDefinizione As Boolean
    Public Property QuattroK As Boolean
    Public Property CreativeCommons As Boolean
    Public Property NascondiShort As Boolean

    ''' <summary>Vero se c'è almeno un filtro acceso.</summary>
    Public ReadOnly Property Attivo As Boolean
        Get
            Return ServeServer OrElse NascondiShort
        End Get
    End Property

    ''' <summary>Vero se almeno un filtro lo applica YouTube.</summary>
    Public ReadOnly Property ServeServer As Boolean
        Get
            Return Data <> FiltroData.Qualsiasi OrElse SoloDirette OrElse ConSottotitoli OrElse AltaDefinizione OrElse QuattroK OrElse CreativeCommons
        End Get
    End Property

    Public Function Copia() As FiltriRicerca
        Return CType(Me.MemberwiseClone(), FiltriRicerca)
    End Function

    ''' <summary>I filtri accesi a parole ("ultima settimana, con sottotitoli"), o "nessun filtro".</summary>
    Public Function Descrizione() As String
        Dim parti As New List(Of String)()
        Select Case Data
            Case FiltroData.UltimaOra : parti.Add("ultima ora")
            Case FiltroData.Oggi : parti.Add("oggi")
            Case FiltroData.QuestaSettimana : parti.Add("questa settimana")
            Case FiltroData.QuestoMese : parti.Add("questo mese")
            Case FiltroData.QuestoAnno : parti.Add("quest'anno")
        End Select
        If SoloDirette Then parti.Add("solo dirette")
        If ConSottotitoli Then parti.Add("con sottotitoli")
        If AltaDefinizione Then parti.Add("alta definizione")
        If QuattroK Then parti.Add("4K")
        If CreativeCommons Then parti.Add("Creative Commons")
        If NascondiShort Then parti.Add("senza Short")
        Return If(parti.Count = 0, "nessun filtro", String.Join(", ", parti))
    End Function

    ''' <summary>
    ''' Il parametro "sp" di YouTube con i filtri e il tipo di risultato (video, canali, playlist; Unknown per tutti).
    ''' È un piccolo messaggio protobuf in base64: un campo per la data, uno per il tipo, uno per ogni funzione.
    ''' Vuoto se non c'è niente da chiedere.
    ''' </summary>
    Friend Function CostruisciParametroSp(tipo As YoutubeItemType) As String
        Dim campi As New SortedDictionary(Of Integer, Byte)()
        If Data <> FiltroData.Qualsiasi Then campi(1) = CByte(Data)
        Select Case tipo
            Case YoutubeItemType.Video : campi(2) = 1
            Case YoutubeItemType.Channel : campi(2) = 2
            Case YoutubeItemType.Playlist : campi(2) = 3
        End Select
        If AltaDefinizione Then campi(4) = 1
        If ConSottotitoli Then campi(5) = 1
        If CreativeCommons Then campi(6) = 1
        If SoloDirette Then campi(8) = 1
        If QuattroK Then campi(14) = 1
        If campi.Count = 0 Then Return ""
        Dim contenuto As New List(Of Byte)()
        For Each coppia As KeyValuePair(Of Integer, Byte) In campi
            contenuto.Add(CByte((coppia.Key << 3) Or 0))
            contenuto.Add(coppia.Value)
        Next
        Dim byteSp As New List(Of Byte)()
        byteSp.Add(&H12)
        byteSp.Add(CByte(contenuto.Count))
        byteSp.AddRange(contenuto)
        Return Uri.EscapeDataString(Convert.ToBase64String(byteSp.ToArray()))
    End Function
End Class

Public Class YtdlpClient
    Private logger As New Logger()
    Private Const YTDLP_NOME_FILE As String = "yt-dlp.exe"
    Private Const CACHE_DIR As String = ".cache"
    ' --no-warnings/--no-progress riducono I/O su stderr; flat + skip-download già usati dove serve
    Private Const ARGOMENTI_BASE As String = "--cache-dir " & CACHE_DIR & " --no-warnings --no-progress --extractor-args ""youtube:lang=it"""

    ' Per leggere YouTube yt-dlp ha bisogno di un motore JavaScript. Quello che scarica WinGabriel è deno.exe, accanto al programma:
    ' si indica a yt-dlp dove sta, così lo trova anche se non è nel PATH.
    Private Shared ReadOnly Property COMMON_ARGS As String
        Get
            Try
                Dim deno As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deno.exe")
                If File.Exists(deno) AndAlso YtdlpSupportaMotoreJs() Then Return ARGOMENTI_BASE & " --js-runtimes ""deno:" & deno & """"
            Catch
            End Try
            Return ARGOMENTI_BASE
        End Get
    End Property

    ''' <summary>Mette un valore (url o testo di ricerca scritto dall'utente) tra virgolette secondo le regole di
    ''' Windows per gli argomenti di un processo (CommandLineToArgvW): una virgoletta o una barra rovesciata dentro
    ''' il valore non puo' piu' chiudere la stringa in anticipo e aggiungere altri argomenti a yt-dlp (per esempio
    ''' --exec), perche' qui vengono gia' raddoppiate/protette come servirebbe a ProcessStartInfo.ArgumentList, non
    ''' disponibile in .NET Framework.</summary>
    Private Shared Function VirgolettaArgomento(valore As String) As String
        If valore Is Nothing Then valore = ""
        Dim sb As New StringBuilder()
        sb.Append(""""c)
        Dim i As Integer = 0
        While i < valore.Length
            Dim nBackslash As Integer = 0
            While i < valore.Length AndAlso valore(i) = "\"c
                nBackslash += 1
                i += 1
            End While
            If i = valore.Length Then
                sb.Append("\"c, nBackslash * 2)
            ElseIf valore(i) = """"c Then
                sb.Append("\"c, nBackslash * 2 + 1)
                sb.Append(""""c)
                i += 1
            Else
                sb.Append("\"c, nBackslash)
                sb.Append(valore(i))
                i += 1
            End If
        End While
        sb.Append(""""c)
        Return sb.ToString()
    End Function

    Private Shared _ytdlpSupportaMotoreJs As Integer = -1

    ' L'opzione --js-runtimes esiste dalle versioni di yt-dlp della fine del 2025: con un yt-dlp più vecchio ogni chiamata fallirebbe
    ' con "opzione sconosciuta". Si guarda la versione del file; se non si legge si presume recente (WinGabriel lo aggiorna ogni giorno).
    Private Shared Function YtdlpSupportaMotoreJs() As Boolean
        If _ytdlpSupportaMotoreJs >= 0 Then Return _ytdlpSupportaMotoreJs = 1
        Dim supporta As Boolean = True
        Try
            Dim percorso As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, YTDLP_NOME_FILE)
            If File.Exists(percorso) Then
                Dim versione As String = If(FileVersionInfo.GetVersionInfo(percorso).FileVersion, "")
                Dim parti As String() = versione.Split("."c)
                Dim anno As Integer
                Dim mese As Integer
                Dim giorno As Integer
                If parti.Length >= 3 AndAlso Integer.TryParse(parti(0), anno) AndAlso Integer.TryParse(parti(1), mese) AndAlso Integer.TryParse(parti(2), giorno) Then
                    supporta = (anno * 10000 + mese * 100 + giorno) >= 20251112
                End If
            End If
        Catch
        End Try
        _ytdlpSupportaMotoreJs = If(supporta, 1, 0)
        Return supporta
    End Function
    Private Const TENTATIVI_MASSIMI As Integer = 2
    Private Const ATTESA_TRA_TENTATIVI_MS As Integer = 400
    ' Timeout processo yt-dlp (ms) — alto per canali/playlist molto grandi
    Private Const TIMEOUT_YTDLP_MS As Integer = 600000
    ' Parallelismo controllato: abbastanza per velocità, poco per evitare rate-limit YouTube
    Private Const MAX_CHUNK_PARALLELI As Integer = 5
    Private Const CHUNK_SIZE_VIDEO As Integer = 100
    ' Sotto questa soglia una sola chiamata yt-dlp è più veloce e affidabile
    Private Const SOGLIA_SEQUENZIALE As Integer = 200
    ' Retry per singolo chunk fallito
    Private Const TENTATIVI_CHUNK As Integer = 2
    ' Timeout richieste HTTP dirette (feed RSS dei canali)
    Private Const TIMEOUT_HTTP_MS As Integer = 20000

    Private Shared ReadOnly _lockAggiornamento As New Object()
    Private Shared _ultimoControlloAggiornamento As Date = Date.MinValue
    Private Shared ReadOnly IntervalloControlloAggiornamento As TimeSpan = TimeSpan.FromHours(24)

    Private Shared ReadOnly _lockPercorsoYtdlp As New Object()
    Private Shared _percorsoYtdlpCache As String = Nothing

    Private ReadOnly _lockSessioniPaginate As New Object()
    Private ReadOnly _sessioniPaginate As New Dictionary(Of String, SessionePaginata)

    Private ReadOnly _lockSessioniCommenti As New Object()
    Private ReadOnly _sessioniCommenti As New Dictionary(Of String, SessionePaginataCommenti)

    Private Enum ModalitaSessionePaginata
        Ricerca
        ElencoUrl
        ElencoLocale
    End Enum

    ''' <summary>
    ''' Se True (ordinamento o filtro durata attivi), carica tutti i risultati una volta,
    ''' applica filtro+ordinamento globale e pagina in memoria (Skip/Take).
    ''' Se False, pagina lato yt-dlp nell'ordine nativo di YouTube.
    ''' </summary>
    Private Class SessionePaginata
        Public Query As String = ""
        Public Url As String = ""
        Public Modalita As ModalitaSessionePaginata = ModalitaSessionePaginata.Ricerca
        Public Tipo As YoutubeItemType = YoutubeItemType.Unknown
        Public DimensionePagina As Integer = 20
        Public PaginaCorrente As Integer = 0
        Public SortBy As YoutubeSortBy = YoutubeSortBy.None
        Public MinDurationSeconds As Long? = Nothing
        Public MaxDurationSeconds As Long? = Nothing
        Public Filtri As FiltriRicerca = Nothing
        Public UsaPaginazioneLocale As Boolean = False
        Public TuttiRisultati As List(Of YoutubeSearchResult) = Nothing
        Public ReadOnly Cache As New Dictionary(Of Integer, List(Of YoutubeSearchResult))
    End Class

    ' Quanti risultati massimi caricare in una ricerca quando serve ordinamento/filtro globale
    Private Const MAX_RISULTATI_ORDINAMENTO_RICERCA As Integer = 300

    Private Class SessionePaginataCommenti
        Public ReadOnly TuttiCommenti As New List(Of YoutubeComment)
        Public DimensionePagina As Integer = 20
        Public PaginaCorrente As Integer = 0
    End Class

    Private Function OttieniPercorsoYtdlp() As String
        Try
            SyncLock _lockPercorsoYtdlp
                If Not String.IsNullOrEmpty(_percorsoYtdlpCache) AndAlso File.Exists(_percorsoYtdlpCache) Then
                    Return _percorsoYtdlpCache
                End If

                Dim percorsoBase As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, YTDLP_NOME_FILE)
                If File.Exists(percorsoBase) Then
                    _percorsoYtdlpCache = percorsoBase
                    Return _percorsoYtdlpCache
                End If

                Dim percorsoCorrente As String = Path.Combine(Environment.CurrentDirectory, YTDLP_NOME_FILE)
                If File.Exists(percorsoCorrente) Then
                    _percorsoYtdlpCache = percorsoCorrente
                    Return _percorsoYtdlpCache
                End If

                Dim percorsoDalPath As String = CercaYtdlpNelPath()
                If Not String.IsNullOrEmpty(percorsoDalPath) Then
                    _percorsoYtdlpCache = percorsoDalPath
                    Return _percorsoYtdlpCache
                End If

                Return percorsoBase
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniPercorsoYtdlp]: {ex.ToString()}")
            Return YTDLP_NOME_FILE
        End Try
    End Function

    Private Function CercaYtdlpNelPath() As String
        Try
            Dim pathEnv As String = Environment.GetEnvironmentVariable("PATH")
            If String.IsNullOrEmpty(pathEnv) Then Return Nothing

            For Each cartella As String In pathEnv.Split(Path.PathSeparator)
                Try
                    If String.IsNullOrWhiteSpace(cartella) Then Continue For
                    Dim candidato As String = Path.Combine(cartella, YTDLP_NOME_FILE)
                    If File.Exists(candidato) Then
                        Return candidato
                    End If
                Catch
                    Continue For
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CercaYtdlpNelPath]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Public Sub AssicuraYtdlpAggiornato(Optional forza As Boolean = False)
        Try
            SyncLock _lockAggiornamento
                If Not forza AndAlso (Date.Now - _ultimoControlloAggiornamento) < IntervalloControlloAggiornamento Then
                    Return
                End If
                _ultimoControlloAggiornamento = Date.Now
            End SyncLock

            Dim percorsoYtdlp As String = OttieniPercorsoYtdlp()

            If Not File.Exists(percorsoYtdlp) Then
                Return
            End If

            Dim startInfo As New ProcessStartInfo()
            startInfo.FileName = percorsoYtdlp
            startInfo.WorkingDirectory = Path.GetDirectoryName(percorsoYtdlp)
            startInfo.Arguments = "-U"
            startInfo.UseShellExecute = False
            startInfo.RedirectStandardOutput = True
            startInfo.RedirectStandardError = True
            startInfo.CreateNoWindow = True
            startInfo.StandardOutputEncoding = Encoding.UTF8

            Using process As Process = Process.Start(startInfo)
                If process Is Nothing Then Return
                ' I due flussi vanno svuotati insieme: leggerli in fila blocca yt-dlp -U
                ' quando il buffer dell'altro si riempie.
                Dim letturaUscita As Task(Of String) = process.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As Task(Of String) = process.StandardError.ReadToEndAsync()
                If Not process.WaitForExit(30000) Then
                    Try
                        process.Kill()
                    Catch exArresto As Exception
                        logger.ScriviLog($"ERRORE [YtdlpClient.AssicuraYtdlpAggiornato] arresto: {exArresto.Message}")
                    End Try
                    process.WaitForExit(5000)
                End If
                Try
                    Task.WaitAll(New Task() {letturaUscita, letturaErrori}, 5000)
                Catch exLettura As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.AssicuraYtdlpAggiornato] lettura: {exLettura.Message}")
                End Try
                If process.HasExited AndAlso process.ExitCode <> 0 Then
                    Dim dettaglio As String = ""
                    If letturaErrori.Status = TaskStatus.RanToCompletion Then dettaglio = If(letturaErrori.Result, "")
                    logger.ScriviLog($"ERRORE [YtdlpClient.AssicuraYtdlpAggiornato]: yt-dlp -U terminato con codice {process.ExitCode}. {dettaglio}")
                End If
            End Using
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.AssicuraYtdlpAggiornato]: {ex.ToString()}")
        End Try
    End Sub

    ' La data e l'ora esatte di pubblicazione dei video indicati, lette da yt-dlp (cinque video per volta, quattro chiamate insieme).
    ' I video di cui non si riesce a saperla non compaiono nel risultato.

    Public Function OttieniDatePubblicazione(idVideo As IEnumerable(Of String)) As Dictionary(Of String, DateTime)
        Dim risultato As New Dictionary(Of String, DateTime)()
        Try
            Dim ids As List(Of String) = idVideo.Where(Function(i) Not String.IsNullOrWhiteSpace(i) AndAlso i.Length = 11).Distinct().ToList()
            If ids.Count = 0 Then Return risultato
            Dim gruppi As New List(Of List(Of String))()
            For i As Integer = 0 To ids.Count - 1 Step 5
                gruppi.Add(ids.Skip(i).Take(5).ToList())
            Next
            Using turno As New System.Threading.SemaphoreSlim(4)
                Dim lavori As List(Of Task) = gruppi.Select(Function(gruppo As List(Of String)) Task.Run(
                    Sub()
                        turno.Wait()
                        Try
                            Dim indirizzi As String = String.Join(" ", gruppo.Select(Function(id) """https://www.youtube.com/watch?v=" & id & """"))
                            Dim output As String = EseguiYtdlpTesto("--skip-download --ignore-errors --no-playlist --print ""%(id)s|%(timestamp)s|%(upload_date)s"" " & COMMON_ARGS & " " & indirizzi, 120000)
                            For Each riga As String In output.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                                Dim campi As String() = riga.Trim().Split("|"c)
                                If campi.Length < 3 OrElse campi(0).Length <> 11 Then Continue For
                                Dim secondi As Long
                                Dim giorno As DateTime
                                If Long.TryParse(campi(1), secondi) Then
                                    SyncLock risultato
                                        risultato(campi(0)) = DateTimeOffset.FromUnixTimeSeconds(secondi).LocalDateTime
                                    End SyncLock
                                ElseIf DateTime.TryParseExact(campi(2), "yyyyMMdd", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, giorno) Then
                                    SyncLock risultato
                                        risultato(campi(0)) = giorno.AddHours(12)
                                    End SyncLock
                                End If
                            Next
                        Catch ex As Exception
                            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniDatePubblicazione]: {ex.Message}")
                        Finally
                            turno.Release()
                        End Try
                    End Sub)).Cast(Of Task)().ToList()
                Task.WaitAll(lavori.ToArray())
            End Using
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniDatePubblicazione]: {ex.ToString()}")
        End Try
        Return risultato
    End Function

    ' Esegue yt-dlp e restituisce quello che scrive, anche se per qualche video è andato in errore.
    Private Function EseguiYtdlpTesto(arguments As String, timeoutMs As Integer) As String
        Dim percorsoYtdlp As String = OttieniPercorsoYtdlp()
        If Not File.Exists(percorsoYtdlp) Then Throw New FileNotFoundException($"Il file {percorsoYtdlp} non è stato trovato.", percorsoYtdlp)
        Dim startInfo As New ProcessStartInfo()
        startInfo.FileName = percorsoYtdlp
        startInfo.WorkingDirectory = Path.GetDirectoryName(percorsoYtdlp)
        startInfo.Arguments = arguments
        startInfo.UseShellExecute = False
        startInfo.RedirectStandardOutput = True
        startInfo.RedirectStandardError = True
        startInfo.CreateNoWindow = True
        startInfo.StandardOutputEncoding = Encoding.UTF8
        startInfo.StandardErrorEncoding = Encoding.UTF8
        Using process As Process = Process.Start(startInfo)
            Dim tOut = Task.Run(Function() process.StandardOutput.ReadToEnd())
            Dim tErr = Task.Run(Function() process.StandardError.ReadToEnd())
            If Not process.WaitForExit(timeoutMs) Then
                Try
                    process.Kill()
                Catch
                End Try
                Throw New TimeoutException($"yt-dlp timeout dopo {timeoutMs}ms")
            End If
            Task.WaitAll(tOut, tErr)
            Return tOut.Result
        End Using
    End Function

    Private Function RunYtdlpJson(arguments As String) As JToken
        Dim ultimaEccezione As Exception = Nothing

        For tentativo As Integer = 1 To TENTATIVI_MASSIMI
            Try
                Dim percorsoYtdlp As String = OttieniPercorsoYtdlp()

                If Not File.Exists(percorsoYtdlp) Then
                    Throw New FileNotFoundException($"Il file {percorsoYtdlp} non è stato trovato.", percorsoYtdlp)
                End If

                Dim startInfo As New ProcessStartInfo()
                startInfo.FileName = percorsoYtdlp
                startInfo.WorkingDirectory = Path.GetDirectoryName(percorsoYtdlp)
                startInfo.Arguments = arguments
                startInfo.UseShellExecute = False
                startInfo.RedirectStandardOutput = True
                startInfo.RedirectStandardError = True
                startInfo.CreateNoWindow = True
                startInfo.StandardOutputEncoding = Encoding.UTF8
                startInfo.StandardErrorEncoding = Encoding.UTF8

                Using process As Process = Process.Start(startInfo)
                    ' Lettura parallela di stdout/stderr: evita deadlock e riduce latenza
                    Dim tOut = Task.Run(Function() process.StandardOutput.ReadToEnd())
                    Dim tErr = Task.Run(Function() process.StandardError.ReadToEnd())

                    If Not process.WaitForExit(TIMEOUT_YTDLP_MS) Then
                        Try
                            process.Kill()
                        Catch
                        End Try
                        Throw New TimeoutException($"yt-dlp timeout dopo {TIMEOUT_YTDLP_MS}ms")
                    End If

                    Task.WaitAll(tOut, tErr)
                    Dim output As String = tOut.Result
                    Dim errorOutput As String = tErr.Result

                    If process.ExitCode <> 0 Then
                        Throw New Exception($"yt-dlp fallito con codice {process.ExitCode}. Dettagli: {errorOutput}")
                    End If

                    If String.IsNullOrWhiteSpace(output) Then
                        Throw New Exception("yt-dlp ha restituito un output vuoto.")
                    End If

                    Return JToken.Parse(output)
                End Using

            Catch ex As FileNotFoundException
                Throw
            Catch ex As Exception
                ultimaEccezione = ex
                logger.ScriviLog($"ERRORE [YtdlpClient.RunYtdlpJson] tentativo {tentativo} di {TENTATIVI_MASSIMI}: {ex.Message}")
                If tentativo < TENTATIVI_MASSIMI Then
                    Threading.Thread.Sleep(ATTESA_TRA_TENTATIVI_MS * tentativo)
                End If
            End Try
        Next

        Throw ultimaEccezione
    End Function

    Public Function Search(query As String,
                           Optional maxResults As Integer = 100,
                           Optional searchType As YoutubeItemType = YoutubeItemType.Unknown,
                           Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                           Optional minDurationSeconds As Long? = Nothing,
                           Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Dim searchResults As New List(Of YoutubeSearchResult)()
        Try
            Dim initialQuery As String = query
            initialQuery = Regex.Replace(initialQuery, "[^\w\s-]", "")
            initialQuery = Regex.Replace(initialQuery.Trim(), "\s+", " ")

            Dim searchPrefix As String = "ytsearch"
            Dim usaPaginaRisultati As Boolean = False
            Dim filtroSp As String = ""

            If searchType = YoutubeItemType.Channel Then
                usaPaginaRisultati = True
                filtroSp = "EgIQAg%3D%3D"
            ElseIf searchType = YoutubeItemType.Playlist Then
                usaPaginaRisultati = True
                filtroSp = "EgIQAw%3D%3D"
            ElseIf searchType = YoutubeItemType.Unknown Then
                usaPaginaRisultati = True
                filtroSp = ""
            End If

            Dim queriesToTry As New List(Of String)
            queriesToTry.Add(initialQuery)

            If initialQuery <> initialQuery.ToLower() Then
                queriesToTry.Add(initialQuery.ToLower())
            End If

            For Each currentQuery In queriesToTry
                If searchResults.Count > 0 Then Exit For

                Dim arguments As String

                If usaPaginaRisultati Then
                    Dim urlRicerca As String = "https://www.youtube.com/results?search_query=" & Uri.EscapeDataString(currentQuery)
                    If Not String.IsNullOrEmpty(filtroSp) Then
                        urlRicerca &= "&sp=" & filtroSp
                    End If
                    arguments = $"-J --flat-playlist --skip-download --ignore-errors --playlist-end {maxResults} {COMMON_ARGS} -- {VirgolettaArgomento(urlRicerca)}"
                Else
                    Dim searchString As String = $"{searchPrefix}{maxResults}:{currentQuery}"
                    arguments = $"-J --flat-playlist --skip-download --ignore-errors {COMMON_ARGS} -- {VirgolettaArgomento(searchString)}"
                End If

                Dim jsonToken As JToken = RunYtdlpJson(arguments)
                Dim entries = jsonToken.SelectToken("entries")

                If entries Is Nothing Then Continue For

                For Each entry In entries
                    Dim res = ParseEntry(entry, searchType)
                    If res IsNot Nothing Then searchResults.Add(res)
                    If searchResults.Count >= maxResults Then Exit For
                Next

                searchResults.RemoveAll(Function(x) x Is Nothing)

            Next currentQuery

            If searchResults.Count = 0 AndAlso (searchType = YoutubeItemType.Channel OrElse searchType = YoutubeItemType.Playlist) Then
                Try
                    Dim urlMista As String = "https://www.youtube.com/results?search_query=" & Uri.EscapeDataString(queriesToTry(0))
                    Dim argomentiMisti As String = $"-J --flat-playlist --skip-download --ignore-errors --playlist-end {Math.Max(maxResults, 50)} {COMMON_ARGS} -- {VirgolettaArgomento(urlMista)}"

                    Dim jsonMisto As JToken = RunYtdlpJson(argomentiMisti)
                    Dim entriesMiste = jsonMisto.SelectToken("entries")

                    If entriesMiste IsNot Nothing Then
                        For Each entry In entriesMiste
                            Dim res = ParseEntry(entry, searchType)
                            If res IsNot Nothing Then searchResults.Add(res)
                            If searchResults.Count >= maxResults Then Exit For
                        Next
                    End If
                Catch exFallback As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.Search] ripiego ricerca mista: {exFallback.Message}")
                End Try
            End If

            searchResults = ApplicaFiltroDurata(searchResults, minDurationSeconds, maxDurationSeconds)
            ApplicaOrdinamento(searchResults, sortBy)
            Return searchResults

        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.Search]: {ex.ToString()}")
        End Try
        searchResults = ApplicaFiltroDurata(searchResults, minDurationSeconds, maxDurationSeconds)
        ApplicaOrdinamento(searchResults, sortBy)
        Return searchResults
    End Function

    ' ==================== PAGINAZIONE RICERCA (per query) ====================

    Private Function ServePaginazioneLocale(sortBy As YoutubeSortBy, minDurationSeconds As Long?, maxDurationSeconds As Long?, Optional filtri As FiltriRicerca = Nothing) As Boolean
        Return sortBy <> YoutubeSortBy.None OrElse minDurationSeconds.HasValue OrElse maxDurationSeconds.HasValue OrElse (filtri IsNot Nothing AndAlso filtri.NascondiShort)
    End Function

    ''' <summary>I filtri che non può applicare YouTube: per ora togliere gli Short (video sotto il minuto o con l'indirizzo degli Short).</summary>
    Private Function ApplicaFiltriLocali(risultati As List(Of YoutubeSearchResult), filtri As FiltriRicerca) As List(Of YoutubeSearchResult)
        If risultati Is Nothing OrElse filtri Is Nothing OrElse Not filtri.NascondiShort Then Return risultati
        Return risultati.Where(Function(r) Not (r.ItemType = YoutubeItemType.Video AndAlso (r.IsShort.GetValueOrDefault() OrElse (r.DurationSeconds.HasValue AndAlso r.DurationSeconds.Value > 0 AndAlso r.DurationSeconds.Value <= 60)))).ToList()
    End Function

    Public Function IniziaRicercaPaginata(sessionKey As String, query As String,
                                          Optional dimensionePagina As Integer = 20,
                                          Optional searchType As YoutubeItemType = YoutubeItemType.Unknown,
                                          Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                          Optional minDurationSeconds As Long? = Nothing,
                                          Optional maxDurationSeconds As Long? = Nothing,
                                          Optional filtri As FiltriRicerca = Nothing) As List(Of YoutubeSearchResult)
        Try
            Dim sessione As New SessionePaginata()
            sessione.Filtri = filtri
            sessione.Query = query
            sessione.Modalita = ModalitaSessionePaginata.Ricerca
            sessione.Tipo = searchType
            sessione.DimensionePagina = Math.Max(1, dimensionePagina)
            sessione.PaginaCorrente = 1
            sessione.SortBy = sortBy
            sessione.MinDurationSeconds = minDurationSeconds
            sessione.MaxDurationSeconds = maxDurationSeconds
            sessione.UsaPaginazioneLocale = ServePaginazioneLocale(sortBy, minDurationSeconds, maxDurationSeconds, filtri)

            If sessione.UsaPaginazioneLocale Then
                ' Carica un blocco ampio, filtra e ordina GLOBALMENTE, poi pagina in memoria
                Dim tutti = EseguiRicercaPagina(query, searchType, MAX_RISULTATI_ORDINAMENTO_RICERCA, 1, filtri)
                tutti = ApplicaFiltriLocali(tutti, filtri)
                tutti = ApplicaFiltroDurata(tutti, minDurationSeconds, maxDurationSeconds)
                ApplicaOrdinamento(tutti, sortBy)
                sessione.TuttiRisultati = tutti
            End If

            SyncLock _lockSessioniPaginate
                _sessioniPaginate(sessionKey) = sessione
            End SyncLock
            Return OttieniPaginaSessione(sessionKey, 1)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaRicercaPaginata sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    ' ==================== PAGINAZIONE ELENCHI (video canale/playlist, playlist canale) ====================

    Public Function IniziaElencoPaginato(sessionKey As String, url As String,
                                         Optional dimensionePagina As Integer = 20,
                                         Optional tipo As YoutubeItemType = YoutubeItemType.Video,
                                         Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                         Optional minDurationSeconds As Long? = Nothing,
                                         Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Try
            Dim sessione As New SessionePaginata()
            sessione.Url = url
            sessione.Modalita = ModalitaSessionePaginata.ElencoUrl
            sessione.Tipo = tipo
            sessione.DimensionePagina = Math.Max(1, dimensionePagina)
            sessione.PaginaCorrente = 1
            sessione.SortBy = sortBy
            sessione.MinDurationSeconds = minDurationSeconds
            sessione.MaxDurationSeconds = maxDurationSeconds
            sessione.UsaPaginazioneLocale = ServePaginazioneLocale(sortBy, minDurationSeconds, maxDurationSeconds)

            If sessione.UsaPaginazioneLocale Then
                ' Ordinamento/filtro: scarica l'INTERO elenco (canale o playlist), poi ordina globalmente
                Dim tutti As List(Of YoutubeSearchResult)
                If tipo = YoutubeItemType.Playlist Then
                    tutti = GetPlaylistsFromUrl(url)
                Else
                    tutti = GetVideosFromUrl(url) ' tutti i video, senza limite
                End If
                tutti = ApplicaFiltroDurata(tutti, minDurationSeconds, maxDurationSeconds)
                ApplicaOrdinamento(tutti, sortBy)
                sessione.TuttiRisultati = tutti
            End If

            SyncLock _lockSessioniPaginate
                _sessioniPaginate(sessionKey) = sessione
            End SyncLock
            Return OttieniPaginaSessione(sessionKey, 1)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaElencoPaginato sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Public Function IniziaVideoCanalePaginato(sessionKey As String, channelId As String,
                                              Optional dimensionePagina As Integer = 20,
                                              Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                              Optional minDurationSeconds As Long? = Nothing,
                                              Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Return IniziaElencoPaginato(sessionKey, $"https://www.youtube.com/channel/{channelId}/videos", dimensionePagina, YoutubeItemType.Video, sortBy, minDurationSeconds, maxDurationSeconds)
    End Function

    Public Function IniziaVideoPlaylistPaginato(sessionKey As String, playlistId As String,
                                                Optional dimensionePagina As Integer = 20,
                                                Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                                Optional minDurationSeconds As Long? = Nothing,
                                                Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Return IniziaElencoPaginato(sessionKey, $"https://www.youtube.com/playlist?list={playlistId}", dimensionePagina, YoutubeItemType.Video, sortBy, minDurationSeconds, maxDurationSeconds)
    End Function

    Public Function IniziaPlaylistCanalePaginato(sessionKey As String, channelId As String,
                                                 Optional dimensionePagina As Integer = 20,
                                                 Optional sortBy As YoutubeSortBy = YoutubeSortBy.None) As List(Of YoutubeSearchResult)
        ' Per le playlist di un canale il filtro durata non si applica (non sono video)
        Return IniziaElencoPaginato(sessionKey, $"https://www.youtube.com/channel/{channelId}/playlists", dimensionePagina, YoutubeItemType.Playlist, sortBy, Nothing, Nothing)
    End Function

    ' ==================== FILTRO CONTENUTI NON DISPONIBILI O RISERVATI ====================

    ''' <summary>
    ''' Valori del campo availability di yt-dlp che indicano contenuti da non mostrare mai:
    ''' video privati, riservati agli iscritti al canale (abbonamento), riservati agli abbonati Premium
    ''' o comunque non accessibili senza autenticazione.
    ''' </summary>
    Private Shared ReadOnly DISPONIBILITA_VIETATE As String() = {
        "private", "subscriber_only", "premium_only", "needs_auth"}

    ''' <summary>
    ''' Marcatori che YouTube usa nei titoli degli elenchi per i contenuti non piu' accessibili
    ''' o riservati. Confrontati in minuscolo, in italiano e in inglese.
    ''' </summary>
    Private Shared ReadOnly TITOLI_VIETATI As String() = {
        "[private video]", "[video privato]",
        "[deleted video]", "[video eliminato]", "[video cancellato]",
        "[unavailable video]", "[video non disponibile]", "[non disponibile]",
        "[restricted video]", "[video con restrizioni]",
        "[members only]", "[members-only]", "[solo per gli iscritti]",
        "[riservato agli iscritti]", "[contenuto riservato]"}

    ''' <summary>True se il valore di availability e' accettabile (campo assente compreso).</summary>
    Private Function DisponibilitaAmmessa(disponibilita As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(disponibilita) Then Return True

            Dim valore As String = disponibilita.Trim().ToLowerInvariant()
            For i As Integer = 0 To DISPONIBILITA_VIETATE.Length - 1
                If valore = DISPONIBILITA_VIETATE(i) Then Return False
            Next
            Return True
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.DisponibilitaAmmessa]: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>True se il titolo non contiene marcatori di contenuto rimosso o riservato.</summary>
    Public Function TitoloAmmesso(titolo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return True

            Dim valore As String = titolo.Trim().ToLowerInvariant()
            For i As Integer = 0 To TITOLI_VIETATI.Length - 1
                If valore.IndexOf(TITOLI_VIETATI(i), StringComparison.Ordinal) >= 0 Then Return False
            Next
            Return True
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.TitoloAmmesso]: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>Verifica una singola voce JSON restituita da yt-dlp.</summary>
    Private Function VoceAmmessa(entry As JToken) As Boolean
        Try
            If entry Is Nothing Then Return False

            If Not DisponibilitaAmmessa(entry.Value(Of String)("availability")) Then Return False
            If Not TitoloAmmesso(entry.Value(Of String)("title")) Then Return False

            Dim riservato As Boolean? = entry.Value(Of Boolean?)("is_members_only")
            If riservato.HasValue AndAlso riservato.Value Then Return False

            Return True
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.VoceAmmessa]: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>Verifica il nodo principale di un contenuto (video, playlist o canale).</summary>
    Private Function ContenutoAmmesso(token As JToken, titolo As String) As Boolean
        Try
            If token Is Nothing Then Return False

            If Not DisponibilitaAmmessa(token.Value(Of String)("availability")) Then Return False
            If Not TitoloAmmesso(titolo) Then Return False

            Return True
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.ContenutoAmmesso]: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>Rimuove da un elenco gia' costruito gli elementi non disponibili o riservati.</summary>
    Public Function FiltraNonDisponibili(risultati As List(Of YoutubeSearchResult)) As List(Of YoutubeSearchResult)
        Dim ammessi As New List(Of YoutubeSearchResult)()
        Try
            If risultati Is Nothing Then Return ammessi

            For Each elemento As YoutubeSearchResult In risultati
                Try
                    If elemento Is Nothing Then Continue For
                    If Not TitoloAmmesso(elemento.Title) Then Continue For
                    ammessi.Add(elemento)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.FiltraNonDisponibili.Voce]: {exVoce.Message}")
                End Try
            Next

            Return ammessi
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.FiltraNonDisponibili]: {ex.ToString()}")
            Return ammessi
        End Try
    End Function
    ' ==================== RICERCA DENTRO CANALE O PLAYLIST ====================

    Public Function CostruisciUrlRicercaCanale(channelId As String, query As String) As String
        Try
            If String.IsNullOrWhiteSpace(channelId) Then Return Nothing
            Dim testo As String = query
            If testo Is Nothing Then testo = ""
            testo = testo.Trim()

            Dim baseUrl As String
            If channelId.StartsWith("@", StringComparison.Ordinal) Then
                baseUrl = "https://www.youtube.com/" & channelId
            ElseIf channelId.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                baseUrl = channelId.TrimEnd("/"c)
            Else
                baseUrl = "https://www.youtube.com/channel/" & channelId
            End If

            Return baseUrl & "/search?query=" & Uri.EscapeDataString(testo)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CostruisciUrlRicercaCanale]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Public Function CercaNelCanale(channelId As String, query As String,
                                   Optional maxResults As Integer = 100,
                                   Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                   Optional minDurationSeconds As Long? = Nothing,
                                   Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            Dim url As String = CostruisciUrlRicercaCanale(channelId, query)
            If String.IsNullOrEmpty(url) Then Return risultati

            risultati = GetVideosFromUrl(url, maxResults)
            risultati = ApplicaFiltroDurata(risultati, minDurationSeconds, maxDurationSeconds)
            ApplicaOrdinamento(risultati, sortBy)
            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CercaNelCanale]: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    Public Function IniziaRicercaCanalePaginata(sessionKey As String, channelId As String, query As String,
                                                Optional dimensionePagina As Integer = 20,
                                                Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                                Optional minDurationSeconds As Long? = Nothing,
                                                Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Try
            Dim url As String = CostruisciUrlRicercaCanale(channelId, query)
            If String.IsNullOrEmpty(url) Then Return New List(Of YoutubeSearchResult)()
            Return IniziaElencoPaginato(sessionKey, url, dimensionePagina, YoutubeItemType.Video, sortBy, minDurationSeconds, maxDurationSeconds)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaRicercaCanalePaginata sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Public Function CercaNellaPlaylist(playlistId As String, query As String,
                                       Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                       Optional minDurationSeconds As Long? = Nothing,
                                       Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            If String.IsNullOrWhiteSpace(playlistId) Then Return risultati

            Dim url As String
            If playlistId.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                url = playlistId
            Else
                url = "https://www.youtube.com/playlist?list=" & playlistId
            End If

            Dim tutti As List(Of YoutubeSearchResult) = GetVideosFromUrl(url)
            risultati = FiltraPerTesto(tutti, query)
            risultati = ApplicaFiltroDurata(risultati, minDurationSeconds, maxDurationSeconds)
            ApplicaOrdinamento(risultati, sortBy)
            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CercaNellaPlaylist]: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    Public Function IniziaRicercaPlaylistPaginata(sessionKey As String, playlistId As String, query As String,
                                                  Optional dimensionePagina As Integer = 20,
                                                  Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                                  Optional minDurationSeconds As Long? = Nothing,
                                                  Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Try
            Dim risultati As List(Of YoutubeSearchResult) = CercaNellaPlaylist(playlistId, query, sortBy, minDurationSeconds, maxDurationSeconds)
            Return IniziaElencoLocalePaginato(sessionKey, risultati, dimensionePagina, YoutubeItemType.Video)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaRicercaPlaylistPaginata sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    ''' <summary>Apre una sessione paginata su un elenco gia' disponibile in memoria.</summary>
    Public Function IniziaElencoLocalePaginato(sessionKey As String, elementi As List(Of YoutubeSearchResult),
                                               Optional dimensionePagina As Integer = 20,
                                               Optional tipo As YoutubeItemType = YoutubeItemType.Video) As List(Of YoutubeSearchResult)
        Try
            Dim sessione As New SessionePaginata()
            sessione.Modalita = ModalitaSessionePaginata.ElencoLocale
            sessione.Tipo = tipo
            sessione.DimensionePagina = Math.Max(1, dimensionePagina)
            sessione.PaginaCorrente = 1
            sessione.UsaPaginazioneLocale = True
            If elementi Is Nothing Then
                sessione.TuttiRisultati = New List(Of YoutubeSearchResult)()
            Else
                sessione.TuttiRisultati = elementi
            End If

            SyncLock _lockSessioniPaginate
                _sessioniPaginate(sessionKey) = sessione
            End SyncLock

            Return OttieniPaginaSessione(sessionKey, 1)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaElencoLocalePaginato sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Public Function FiltraPerTesto(risultati As List(Of YoutubeSearchResult), testo As String) As List(Of YoutubeSearchResult)
        Dim filtrati As New List(Of YoutubeSearchResult)()
        Try
            If risultati Is Nothing Then Return filtrati
            If String.IsNullOrWhiteSpace(testo) Then Return risultati

            Dim termini() As String = NormalizzaTesto(testo).Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
            If termini.Length = 0 Then Return risultati

            For Each elemento As YoutubeSearchResult In risultati
                Try
                    If elemento Is Nothing Then Continue For

                    Dim contenuto As String = NormalizzaTesto(elemento.Title & " " & elemento.ChannelName)
                    Dim corrisponde As Boolean = True

                    For i As Integer = 0 To termini.Length - 1
                        If contenuto.IndexOf(termini(i), StringComparison.Ordinal) < 0 Then
                            corrisponde = False
                            Exit For
                        End If
                    Next

                    If corrisponde Then filtrati.Add(elemento)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.FiltraPerTesto.Voce]: {exVoce.Message}")
                End Try
            Next

            Return filtrati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.FiltraPerTesto]: {ex.ToString()}")
            Return filtrati
        End Try
    End Function

    Private Function NormalizzaTesto(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""

            Dim scomposto As String = testo.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            Dim risultato As New StringBuilder(scomposto.Length)

            For i As Integer = 0 To scomposto.Length - 1
                If CharUnicodeInfo.GetUnicodeCategory(scomposto(i)) <> UnicodeCategory.NonSpacingMark Then
                    risultato.Append(scomposto(i))
                End If
            Next

            Return risultato.ToString().Normalize(NormalizationForm.FormC)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.NormalizzaTesto]: {ex.ToString()}")
            If testo Is Nothing Then Return ""
            Return testo.ToLowerInvariant()
        End Try
    End Function

    ' ==================== ULTIMI VIDEO DI UN CANALE (per iscrizioni) ====================

    ''' <summary>
    ''' Restituisce gli ultimi video pubblicati da un canale, dal piu' recente.
    ''' Usa prima il feed RSS pubblico (veloce e con data di pubblicazione precisa),
    ''' con ripiego su yt-dlp se il feed non e' disponibile.
    ''' </summary>
    Public Function OttieniUltimiVideoCanale(channelId As String, Optional quanti As Integer = 15) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            If String.IsNullOrWhiteSpace(channelId) Then Return risultati
            If quanti <= 0 Then quanti = 15

            risultati = OttieniUltimiVideoCanaleRss(channelId)

            If risultati Is Nothing OrElse risultati.Count = 0 Then
                risultati = OttieniUltimiVideoCanaleYtdlp(channelId, quanti)
            End If

            If risultati Is Nothing Then Return New List(Of YoutubeSearchResult)()
            If risultati.Count > quanti Then
                risultati = risultati.GetRange(0, quanti)
            End If

            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniUltimiVideoCanale channelId={channelId}]: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    Private Function OttieniUltimiVideoCanaleRss(channelId As String) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            If String.IsNullOrWhiteSpace(channelId) Then Return risultati
            If Not channelId.StartsWith("UC", StringComparison.Ordinal) Then Return risultati

            Dim indirizzo As String = "https://www.youtube.com/feeds/videos.xml?channel_id=" & Uri.EscapeDataString(channelId)
            Dim contenuto As String = ScaricaTesto(indirizzo)
            If String.IsNullOrWhiteSpace(contenuto) AndAlso channelId.StartsWith("UC", StringComparison.Ordinal) AndAlso channelId.Length > 2 Then
                Dim playlistId As String = "UU" & channelId.Substring(2)
                contenuto = ScaricaTesto("https://www.youtube.com/feeds/videos.xml?playlist_id=" & Uri.EscapeDataString(playlistId))
            End If
            If String.IsNullOrWhiteSpace(contenuto) Then Return risultati

            Dim documento As New XmlDocument()
            documento.XmlResolver = Nothing
            documento.LoadXml(contenuto)

            Dim spazi As New XmlNamespaceManager(documento.NameTable)
            spazi.AddNamespace("a", "http://www.w3.org/2005/Atom")
            spazi.AddNamespace("yt", "http://www.youtube.com/xml/schemas/2015")
            spazi.AddNamespace("media", "http://search.yahoo.com/mrss/")

            Dim nomeCanale As String = ""
            Dim nodoTitoloCanale As XmlNode = documento.SelectSingleNode("/a:feed/a:title", spazi)
            If nodoTitoloCanale IsNot Nothing Then nomeCanale = nodoTitoloCanale.InnerText

            Dim voci As XmlNodeList = documento.SelectNodes("/a:feed/a:entry", spazi)
            If voci Is Nothing Then Return risultati

            For Each voce As XmlNode In voci
                Try
                    Dim nodoId As XmlNode = voce.SelectSingleNode("yt:videoId", spazi)
                    If nodoId Is Nothing Then Continue For

                    Dim identificativo As String = nodoId.InnerText
                    If String.IsNullOrWhiteSpace(identificativo) Then Continue For

                    Dim elemento As New YoutubeSearchResult()
                    elemento.Id = identificativo
                    elemento.Url = "https://www.youtube.com/watch?v=" & identificativo
                    elemento.ItemType = YoutubeItemType.Video
                    elemento.ChannelId = channelId
                    elemento.ChannelName = nomeCanale

                    Dim nodoTitolo As XmlNode = voce.SelectSingleNode("a:title", spazi)
                    If nodoTitolo IsNot Nothing Then elemento.Title = nodoTitolo.InnerText
                    If String.IsNullOrWhiteSpace(elemento.Title) Then elemento.Title = identificativo
                    If Not TitoloAmmesso(elemento.Title) Then Continue For

                    Dim nodoAutore As XmlNode = voce.SelectSingleNode("a:author/a:name", spazi)
                    If nodoAutore IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(nodoAutore.InnerText) Then
                        elemento.ChannelName = nodoAutore.InnerText
                    End If

                    Dim nodoData As XmlNode = voce.SelectSingleNode("a:published", spazi)
                    If nodoData IsNot Nothing Then
                        Dim dataConvertita As Date
                        If Date.TryParse(nodoData.InnerText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, dataConvertita) Then
                            elemento.UploadDate = dataConvertita.ToLocalTime()
                        End If
                    End If

                    Dim nodoVisualizzazioni As XmlNode = voce.SelectSingleNode("media:group/media:community/media:statistics", spazi)
                    If nodoVisualizzazioni IsNot Nothing AndAlso nodoVisualizzazioni.Attributes IsNot Nothing Then
                        Dim attributo As XmlAttribute = nodoVisualizzazioni.Attributes("views")
                        If attributo IsNot Nothing Then
                            Dim conteggio As Long
                            If Long.TryParse(attributo.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, conteggio) Then
                                elemento.ViewCount = conteggio
                            End If
                        End If
                    End If

                    risultati.Add(elemento)
                Catch exVoce As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.OttieniUltimiVideoCanaleRss.Voce]: {exVoce.Message}")
                End Try
            Next

            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniUltimiVideoCanaleRss channelId={channelId}]: {ex.Message}")
            Return risultati
        End Try
    End Function

    Private Function OttieniUltimiVideoCanaleYtdlp(channelId As String, quanti As Integer) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            If String.IsNullOrWhiteSpace(channelId) Then Return risultati

            Dim url As String
            If channelId.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                url = channelId.TrimEnd("/"c) & "/videos"
            ElseIf channelId.StartsWith("@", StringComparison.Ordinal) Then
                url = "https://www.youtube.com/" & channelId & "/videos"
            Else
                url = "https://www.youtube.com/channel/" & channelId & "/videos"
            End If

            risultati = EseguiElencoPagina(url, YoutubeItemType.Video, Math.Max(1, quanti), 1)
            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniUltimiVideoCanaleYtdlp channelId={channelId}]: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    Private Function ScaricaTesto(indirizzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return Nothing

            Try
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Catch exProtocollo As Exception
                logger.ScriviLog($"ERRORE [YtdlpClient.ScaricaTesto.Protocollo]: {exProtocollo.Message}")
            End Try

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_HTTP_MS
            richiesta.ReadWriteTimeout = TIMEOUT_HTTP_MS
            richiesta.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"
            richiesta.Accept = "application/atom+xml,application/xml,text/xml"
            richiesta.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate

            Using risposta As HttpWebResponse = CType(richiesta.GetResponse(), HttpWebResponse)
                If risposta.StatusCode <> HttpStatusCode.OK Then Return Nothing
                Using flusso As Stream = risposta.GetResponseStream()
                    If flusso Is Nothing Then Return Nothing
                    Using lettore As New StreamReader(flusso, Encoding.UTF8)
                        Return lettore.ReadToEnd()
                    End Using
                End Using
            End Using
        Catch exWeb As WebException
            Dim rispostaErrore As HttpWebResponse = TryCast(exWeb.Response, HttpWebResponse)
            Try
                If rispostaErrore IsNot Nothing Then
                    If rispostaErrore.StatusCode = HttpStatusCode.NotFound OrElse rispostaErrore.StatusCode = HttpStatusCode.Gone Then
                        Return Nothing
                    End If
                End If
            Finally
                Try
                    If rispostaErrore IsNot Nothing Then rispostaErrore.Dispose()
                Catch
                End Try
            End Try
            logger.ScriviLog($"ERRORE [YtdlpClient.ScaricaTesto indirizzo={indirizzo}]: {exWeb.Message}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.ScaricaTesto indirizzo={indirizzo}]: {ex.Message}")
            Return Nothing
        End Try
    End Function
    ' ==================== NAVIGAZIONE COMUNE (ricerca ed elenchi) ====================

    Public Function PaginaSuccessiva(sessionKey As String) As List(Of YoutubeSearchResult)
        Try
            Dim numeroPagina As Integer
            SyncLock _lockSessioniPaginate
                If Not _sessioniPaginate.ContainsKey(sessionKey) Then Return New List(Of YoutubeSearchResult)()
                _sessioniPaginate(sessionKey).PaginaCorrente += 1
                numeroPagina = _sessioniPaginate(sessionKey).PaginaCorrente
            End SyncLock
            Return OttieniPaginaSessione(sessionKey, numeroPagina)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.PaginaSuccessiva sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Public Function PaginaPrecedente(sessionKey As String) As List(Of YoutubeSearchResult)
        Try
            Dim numeroPagina As Integer
            SyncLock _lockSessioniPaginate
                If Not _sessioniPaginate.ContainsKey(sessionKey) Then Return New List(Of YoutubeSearchResult)()
                If _sessioniPaginate(sessionKey).PaginaCorrente > 1 Then
                    _sessioniPaginate(sessionKey).PaginaCorrente -= 1
                End If
                numeroPagina = _sessioniPaginate(sessionKey).PaginaCorrente
            End SyncLock
            Return OttieniPaginaSessione(sessionKey, numeroPagina)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.PaginaPrecedente sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Public Function PaginaAttuale(sessionKey As String) As Integer
        Try
            SyncLock _lockSessioniPaginate
                Dim sessione As SessionePaginata = Nothing
                If _sessioniPaginate.TryGetValue(sessionKey, sessione) Then
                    Return sessione.PaginaCorrente
                End If
            End SyncLock
            Return 0
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.PaginaAttuale sessionKey={sessionKey}]: {ex.ToString()}")
            Return 0
        End Try
    End Function

    Public Sub TerminaRicercaPaginata(sessionKey As String)
        Try
            SyncLock _lockSessioniPaginate
                _sessioniPaginate.Remove(sessionKey)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.TerminaRicercaPaginata sessionKey={sessionKey}]: {ex.ToString()}")
        End Try
    End Sub

    Private Function OttieniPaginaSessione(sessionKey As String, numeroPagina As Integer) As List(Of YoutubeSearchResult)
        Try
            Dim sessione As SessionePaginata = Nothing

            SyncLock _lockSessioniPaginate
                If Not _sessioniPaginate.TryGetValue(sessionKey, sessione) Then
                    Return New List(Of YoutubeSearchResult)()
                End If
                Dim cacheEsistente As List(Of YoutubeSearchResult) = Nothing
                If sessione.Cache.TryGetValue(numeroPagina, cacheEsistente) Then
                    Return cacheEsistente
                End If
            End SyncLock

            Dim risultati As List(Of YoutubeSearchResult)

            If sessione.UsaPaginazioneLocale AndAlso sessione.TuttiRisultati IsNot Nothing Then
                ' Paginazione locale su elenco già filtrato e ordinato GLOBALMENTE
                Dim skip As Integer = (numeroPagina - 1) * sessione.DimensionePagina
                If skip < 0 OrElse skip >= sessione.TuttiRisultati.Count Then
                    risultati = New List(Of YoutubeSearchResult)()
                Else
                    risultati = sessione.TuttiRisultati.Skip(skip).Take(sessione.DimensionePagina).ToList()
                End If
            Else
                ' Paginazione nativa yt-dlp (ordine YouTube, senza ordinamento client)
                If sessione.Modalita = ModalitaSessionePaginata.Ricerca Then
                    If String.IsNullOrEmpty(sessione.Query) Then Return New List(Of YoutubeSearchResult)()
                    risultati = EseguiRicercaPagina(sessione.Query, sessione.Tipo, sessione.DimensionePagina, numeroPagina, sessione.Filtri)
                Else
                    If String.IsNullOrEmpty(sessione.Url) Then Return New List(Of YoutubeSearchResult)()
                    risultati = EseguiElencoPagina(sessione.Url, sessione.Tipo, sessione.DimensionePagina, numeroPagina)
                End If
            End If

            SyncLock _lockSessioniPaginate
                If _sessioniPaginate.ContainsKey(sessionKey) Then
                    _sessioniPaginate(sessionKey).Cache(numeroPagina) = risultati
                End If
            End SyncLock

            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniPaginaSessione sessionKey={sessionKey} pagina={numeroPagina}]: {ex.ToString()}")
            Return New List(Of YoutubeSearchResult)()
        End Try
    End Function

    Private Function EseguiElencoPagina(url As String, tipo As YoutubeItemType, dimensionePagina As Integer, numeroPagina As Integer) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            Dim startIdx As Integer = (numeroPagina - 1) * dimensionePagina + 1
            Dim endIdx As Integer = numeroPagina * dimensionePagina

            Dim arguments As String = $"-J --flat-playlist --skip-download --ignore-errors --playlist-start {startIdx} --playlist-end {endIdx} {COMMON_ARGS} -- {VirgolettaArgomento(url)}"

            Dim jsonToken As JToken
            Try
                jsonToken = RunYtdlpJson(arguments)
            Catch exFetch As Exception
                logger.ScriviLog($"ERRORE [YtdlpClient.EseguiElencoPagina] pagina={numeroPagina} url={url}: {exFetch.Message}")
                Return risultati
            End Try

            Dim entries = jsonToken.SelectToken("entries")
            If entries Is Nothing Then Return risultati

            Dim channelName As String = jsonToken.Value(Of String)("uploader")
            Dim channelId As String = jsonToken.Value(Of String)("uploader_id")
            If String.IsNullOrEmpty(channelId) Then
                channelId = jsonToken.Value(Of String)("channel_id")
            End If

            For Each entry In entries
                Try
                    If tipo = YoutubeItemType.Playlist Then
                        If Not VoceAmmessa(entry) Then Continue For

                        Dim id As String = entry.Value(Of String)("id")
                        Dim title As String = entry.Value(Of String)("title")
                        If String.IsNullOrEmpty(id) Then Continue For

                        Dim result As New YoutubeSearchResult()
                        result.Id = id
                        result.Title = title
                        result.Url = $"https://www.youtube.com/playlist?list={id}"
                        result.ChannelName = channelName
                        result.ChannelId = channelId
                        result.ItemType = YoutubeItemType.Playlist
                        result.UploadDate = ParseDate(entry.Value(Of String)("upload_date"))
                        result.ViewCount = entry.Value(Of Long?)("view_count")
                        result.LikeCount = entry.Value(Of Long?)("like_count")
                        result.VideoCount = entry.Value(Of Integer?)("playlist_count")
                        If Not result.VideoCount.HasValue Then
                            result.VideoCount = entry.Value(Of Integer?)("video_count")
                        End If
                        Dim dur As Long? = entry.Value(Of Long?)("duration")
                        If dur.HasValue Then
                            result.DurationSeconds = dur
                            result.DurationText = FormatDuration(dur)
                        End If
                        risultati.Add(result)
                    Else
                        Dim v = ParseVideoEntry(entry, channelName, channelId)
                        If v IsNot Nothing Then risultati.Add(v)
                    End If
                Catch exEntry As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.EseguiElencoPagina.Loop] pagina={numeroPagina}: {exEntry.Message}")
                End Try
            Next

            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.EseguiElencoPagina] pagina={numeroPagina} url={url}: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    ' ==================== PAGINAZIONE COMMENTI (fetch unico, slicing lato client) ====================

    Public Function IniziaCommentiPaginati(sessionKey As String, videoId As String, Optional dimensionePagina As Integer = 20, Optional ordine As String = "top") As List(Of YoutubeComment)
        Try
            Dim tuttiCommenti As List(Of YoutubeComment) = GetVideoComments(videoId, ordine)

            Dim sessione As New SessionePaginataCommenti()
            sessione.DimensionePagina = Math.Max(1, dimensionePagina)
            sessione.PaginaCorrente = 1
            If tuttiCommenti IsNot Nothing Then
                sessione.TuttiCommenti.AddRange(tuttiCommenti)
            End If

            SyncLock _lockSessioniCommenti
                _sessioniCommenti(sessionKey) = sessione
            End SyncLock

            Return OttieniPaginaCommenti(sessionKey, 1)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.IniziaCommentiPaginati sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeComment)()
        End Try
    End Function

    Public Function CommentiPaginaSuccessiva(sessionKey As String) As List(Of YoutubeComment)
        Try
            Dim numeroPagina As Integer
            SyncLock _lockSessioniCommenti
                If Not _sessioniCommenti.ContainsKey(sessionKey) Then Return New List(Of YoutubeComment)()
                _sessioniCommenti(sessionKey).PaginaCorrente += 1
                numeroPagina = _sessioniCommenti(sessionKey).PaginaCorrente
            End SyncLock
            Return OttieniPaginaCommenti(sessionKey, numeroPagina)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CommentiPaginaSuccessiva sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeComment)()
        End Try
    End Function

    Public Function CommentiPaginaPrecedente(sessionKey As String) As List(Of YoutubeComment)
        Try
            Dim numeroPagina As Integer
            SyncLock _lockSessioniCommenti
                If Not _sessioniCommenti.ContainsKey(sessionKey) Then Return New List(Of YoutubeComment)()
                If _sessioniCommenti(sessionKey).PaginaCorrente > 1 Then
                    _sessioniCommenti(sessionKey).PaginaCorrente -= 1
                End If
                numeroPagina = _sessioniCommenti(sessionKey).PaginaCorrente
            End SyncLock
            Return OttieniPaginaCommenti(sessionKey, numeroPagina)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CommentiPaginaPrecedente sessionKey={sessionKey}]: {ex.ToString()}")
            Return New List(Of YoutubeComment)()
        End Try
    End Function

    Public Function CommentiPaginaAttuale(sessionKey As String) As Integer
        Try
            SyncLock _lockSessioniCommenti
                Dim sessione As SessionePaginataCommenti = Nothing
                If _sessioniCommenti.TryGetValue(sessionKey, sessione) Then
                    Return sessione.PaginaCorrente
                End If
            End SyncLock
            Return 0
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.CommentiPaginaAttuale sessionKey={sessionKey}]: {ex.ToString()}")
            Return 0
        End Try
    End Function

    Public Sub TerminaCommentiPaginati(sessionKey As String)
        Try
            SyncLock _lockSessioniCommenti
                _sessioniCommenti.Remove(sessionKey)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.TerminaCommentiPaginati sessionKey={sessionKey}]: {ex.ToString()}")
        End Try
    End Sub

    Private Function OttieniPaginaCommenti(sessionKey As String, numeroPagina As Integer) As List(Of YoutubeComment)
        Try
            Dim sessione As SessionePaginataCommenti = Nothing
            SyncLock _lockSessioniCommenti
                If Not _sessioniCommenti.TryGetValue(sessionKey, sessione) Then
                    Return New List(Of YoutubeComment)()
                End If
            End SyncLock

            Dim skip As Integer = (numeroPagina - 1) * sessione.DimensionePagina
            If skip < 0 OrElse skip >= sessione.TuttiCommenti.Count Then
                Return New List(Of YoutubeComment)()
            End If

            Return sessione.TuttiCommenti.Skip(skip).Take(sessione.DimensionePagina).ToList()
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.OttieniPaginaCommenti sessionKey={sessionKey} pagina={numeroPagina}]: {ex.ToString()}")
            Return New List(Of YoutubeComment)()
        End Try
    End Function

    Private Function EseguiRicercaPagina(query As String, searchType As YoutubeItemType, dimensionePagina As Integer, numeroPagina As Integer, Optional filtri As FiltriRicerca = Nothing) As List(Of YoutubeSearchResult)
        Dim risultati As New List(Of YoutubeSearchResult)()
        Try
            Dim initialQuery As String = query
            initialQuery = Regex.Replace(initialQuery, "[^\w\s-]", "")
            initialQuery = Regex.Replace(initialQuery.Trim(), "\s+", " ")

            Dim searchPrefix As String = "ytsearch"
            Dim usaPaginaRisultati As Boolean = False
            Dim filtroSp As String = ""

            If searchType = YoutubeItemType.Channel Then
                usaPaginaRisultati = True
                filtroSp = "EgIQAg%3D%3D"
            ElseIf searchType = YoutubeItemType.Playlist Then
                usaPaginaRisultati = True
                filtroSp = "EgIQAw%3D%3D"
            ElseIf searchType = YoutubeItemType.Unknown Then
                usaPaginaRisultati = True
                filtroSp = ""
            End If

            ' I filtri di YouTube (data, funzioni) vanno nell'indirizzo della pagina dei risultati, insieme al tipo.
            If filtri IsNot Nothing AndAlso filtri.ServeServer Then
                Dim parametro As String = filtri.CostruisciParametroSp(searchType)
                If parametro <> "" Then
                    usaPaginaRisultati = True
                    filtroSp = parametro
                End If
            End If

            Dim startIdx As Integer = (numeroPagina - 1) * dimensionePagina + 1
            Dim endIdx As Integer = numeroPagina * dimensionePagina

            Dim queriesToTry As New List(Of String)
            queriesToTry.Add(initialQuery)

            If initialQuery <> initialQuery.ToLower() Then
                queriesToTry.Add(initialQuery.ToLower())
            End If

            For Each currentQuery In queriesToTry
                If risultati.Count > 0 Then Exit For

                Dim arguments As String

                If usaPaginaRisultati Then
                    Dim urlRicerca As String = "https://www.youtube.com/results?search_query=" & Uri.EscapeDataString(currentQuery)
                    If Not String.IsNullOrEmpty(filtroSp) Then
                        urlRicerca &= "&sp=" & filtroSp
                    End If
                    arguments = $"-J --flat-playlist --skip-download --ignore-errors --playlist-start {startIdx} --playlist-end {endIdx} {COMMON_ARGS} -- {VirgolettaArgomento(urlRicerca)}"
                Else
                    Dim searchString As String = $"{searchPrefix}{endIdx}:{currentQuery}"
                    arguments = $"-J --flat-playlist --skip-download --ignore-errors --playlist-start {startIdx} --playlist-end {endIdx} {COMMON_ARGS} -- {VirgolettaArgomento(searchString)}"
                End If

                Dim jsonToken As JToken
                Try
                    jsonToken = RunYtdlpJson(arguments)
                Catch exFetch As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.EseguiRicercaPagina] pagina={numeroPagina} query={currentQuery}: {exFetch.Message}")
                    Continue For
                End Try

                Dim entries = jsonToken.SelectToken("entries")
                If entries Is Nothing Then Continue For

                For Each entry In entries
                    Dim res = ParseEntry(entry, searchType)
                    If res IsNot Nothing Then risultati.Add(res)
                Next

                risultati.RemoveAll(Function(x) x Is Nothing)
            Next currentQuery

            Return risultati
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.EseguiRicercaPagina] pagina={numeroPagina}: {ex.ToString()}")
            Return risultati
        End Try
    End Function

    Public Function RecuperaMetadatiUrl(url As String) As YoutubeSearchResult
        Try
            Dim arguments As String = $"-J --flat-playlist --playlist-items 0 --skip-download {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            Dim jsonToken As JToken = RunYtdlpJson(arguments)

            Dim id As String = jsonToken.Value(Of String)("id")
            Dim title As String = jsonToken.Value(Of String)("title")
            Dim uploader As String = jsonToken.Value(Of String)("uploader")
            Dim webpage_url As String = jsonToken.Value(Of String)("webpage_url")
            Dim typeStr As String = jsonToken.Value(Of String)("_type")

            If Not ContenutoAmmesso(jsonToken, title) Then
                Return Nothing
            End If

            Dim type As YoutubeItemType = YoutubeItemType.Unknown

            If typeStr = "playlist" Then
                If webpage_url.Contains("playlist?list=") Then
                    type = YoutubeItemType.Playlist
                Else
                    type = YoutubeItemType.Channel
                End If
            Else
                type = YoutubeItemType.Video
            End If

            Dim result As New YoutubeSearchResult()
            result.Id = id
            result.Title = title
            result.Url = webpage_url
            result.ItemType = type
            result.ChannelName = uploader
            result.ChannelId = jsonToken.Value(Of String)("channel_id")

            If type = YoutubeItemType.Video Then
                Dim durationSeconds As Long? = jsonToken.Value(Of Long?)("duration")
                result.DurationSeconds = durationSeconds
                result.DurationText = If(durationSeconds IsNot Nothing, FormatDuration(durationSeconds), "")
            End If

            ' Campi opzionali
            result.UploadDate = ParseDate(jsonToken.Value(Of String)("upload_date"))
            result.ViewCount = jsonToken.Value(Of Long?)("view_count")
            result.LikeCount = jsonToken.Value(Of Long?)("like_count")
            result.CommentCount = jsonToken.Value(Of Long?)("comment_count")
            result.VideoCount = jsonToken.Value(Of Integer?)("playlist_count")
            If type = YoutubeItemType.Channel Then
                result.SubscriberCount = jsonToken.Value(Of Long?)("channel_follower_count")
                If Not result.SubscriberCount.HasValue Then
                    result.SubscriberCount = jsonToken.Value(Of Long?)("subscriber_count")
                End If
            End If

            Return result
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.RecuperaMetadatiUrl]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Public Function GetCapitoli(url As String, includiSponsorBlock As Boolean) As (Capitoli As List(Of YoutubeChapter), Sponsor As List(Of YoutubeSponsorSegment))
        Dim capitoli As New List(Of YoutubeChapter)
        Dim sponsor As New List(Of YoutubeSponsorSegment)
        Try
            Dim argomentoSponsorBlock As String = ""
            If includiSponsorBlock Then
                argomentoSponsorBlock = " --sponsorblock-mark all"
            End If

            Dim arguments As String = $"-J --skip-download --ignore-errors{argomentoSponsorBlock} {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            Dim jsonToken As JToken = RunYtdlpJson(arguments)

            Dim capitoliToken = jsonToken.SelectToken("chapters")
            If capitoliToken IsNot Nothing AndAlso capitoliToken.Type = JTokenType.Array Then
                For Each entry As JToken In capitoliToken
                    Try
                        Dim titolo As String = entry.Value(Of String)("title")
                        Dim inizio As Double? = entry.Value(Of Double?)("start_time")
                        Dim fine As Double? = entry.Value(Of Double?)("end_time")
                        If Not inizio.HasValue Then Continue For

                        Dim capitolo As New YoutubeChapter()
                        capitolo.Title = If(String.IsNullOrWhiteSpace(titolo), "Capitolo", titolo)
                        capitolo.StartSeconds = inizio.Value
                        capitolo.EndSeconds = If(fine.HasValue, fine.Value, -1)
                        capitoli.Add(capitolo)
                    Catch exEntry As Exception
                        logger.ScriviLog($"ERRORE [YtdlpClient.GetCapitoli.LoopCapitoli]: {exEntry.Message}")
                    End Try
                Next
            End If

            If includiSponsorBlock Then
                Dim sponsorToken = jsonToken.SelectToken("sponsorblock_chapters")
                If sponsorToken IsNot Nothing AndAlso sponsorToken.Type = JTokenType.Array Then
                    For Each entry As JToken In sponsorToken
                        Try
                            Dim inizio As Double? = entry.Value(Of Double?)("start_time")
                            Dim fine As Double? = entry.Value(Of Double?)("end_time")
                            If Not inizio.HasValue OrElse Not fine.HasValue Then Continue For

                            Dim segmento As New YoutubeSponsorSegment()
                            segmento.Title = entry.Value(Of String)("title")
                            segmento.Category = entry.Value(Of String)("category")
                            segmento.StartSeconds = inizio.Value
                            segmento.EndSeconds = fine.Value
                            sponsor.Add(segmento)
                        Catch exEntry As Exception
                            logger.ScriviLog($"ERRORE [YtdlpClient.GetCapitoli.LoopSponsor]: {exEntry.Message}")
                        End Try
                    Next
                End If
            End If

            Return (capitoli, sponsor)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetCapitoli for {url}]: {ex.ToString()}")
            Return (capitoli, sponsor)
        End Try
    End Function

    Private Function ParseEntry(entry As JToken, Optional filterType As YoutubeItemType = YoutubeItemType.Unknown) As YoutubeSearchResult
        If Not VoceAmmessa(entry) Then Return Nothing

        Dim id As String = entry.Value(Of String)("id")
        Dim title As String = entry.Value(Of String)("title")
        Dim uploader As String = entry.Value(Of String)("uploader")
        Dim itemTypeString As String = entry.Value(Of String)("_type")
        Dim channelId As String = entry.Value(Of String)("channel_id")
        Dim urlEntry As String = If(entry.Value(Of String)("url"), entry.Value(Of String)("webpage_url"))
        Dim urlLower As String = If(urlEntry, "").ToLowerInvariant()

        Dim idIndicaPlaylist As Boolean = (id?.StartsWith("PL") = True OrElse id?.StartsWith("RD") = True OrElse id?.StartsWith("OLAK5") = True)
        Dim idIndicaCanale As Boolean = (id?.StartsWith("UC") = True AndAlso id?.Length = 24)
        Dim urlIndicaPlaylist As Boolean = urlLower.Contains("/playlist?list=") OrElse urlLower.Contains("&list=")
        Dim urlIndicaCanale As Boolean = urlLower.Contains("/channel/") OrElse urlLower.Contains("/@")

        Dim type As YoutubeItemType

        If itemTypeString = "playlist" OrElse idIndicaPlaylist OrElse (urlIndicaPlaylist AndAlso Not idIndicaCanale) Then
            type = YoutubeItemType.Playlist
        ElseIf idIndicaCanale OrElse (urlIndicaCanale AndAlso Not idIndicaPlaylist) Then
            type = YoutubeItemType.Channel
        Else
            type = YoutubeItemType.Video
        End If

        If type = YoutubeItemType.Video AndAlso filterType = YoutubeItemType.Channel Then
            If String.IsNullOrWhiteSpace(channelId) OrElse channelId.Length <> 24 OrElse Not channelId.StartsWith("UC") Then
                Return Nothing
            End If
            type = YoutubeItemType.Channel
            id = channelId
            title = uploader
        End If

        If filterType <> YoutubeItemType.Unknown AndAlso type <> filterType Then
            Return Nothing
        End If

        Dim urlFinal As String
        Dim durationText As String = ""
        Dim durationSeconds As Long? = Nothing

        Select Case type
            Case YoutubeItemType.Video
                urlFinal = $"https://www.youtube.com/watch?v={id}"
                durationSeconds = entry.Value(Of Long?)("duration")
                durationText = If(durationSeconds IsNot Nothing, FormatDuration(durationSeconds), "")
            Case YoutubeItemType.Playlist
                urlFinal = $"https://www.youtube.com/playlist?list={id}"
            Case YoutubeItemType.Channel
                urlFinal = $"https://www.youtube.com/channel/{id}"
            Case Else
                urlFinal = ""
        End Select

        Dim result As New YoutubeSearchResult()
        result.Id = id
        result.Title = title
        result.Url = urlFinal
        result.ChannelName = uploader
        result.ChannelId = channelId
        result.DurationText = durationText
        result.ItemType = type
        result.DurationSeconds = durationSeconds
        If type = YoutubeItemType.Video AndAlso urlLower.Contains("/shorts/") Then result.IsShort = True

        ' Campi opzionali per ordinamento e filtri (possono essere Nothing)
        result.UploadDate = ParseDate(entry.Value(Of String)("upload_date"))
        result.ViewCount = entry.Value(Of Long?)("view_count")
        result.LikeCount = entry.Value(Of Long?)("like_count")
        result.CommentCount = entry.Value(Of Long?)("comment_count")
        result.VideoCount = entry.Value(Of Integer?)("playlist_count")
        If Not result.VideoCount.HasValue Then
            result.VideoCount = entry.Value(Of Integer?)("video_count")
        End If

        If type = YoutubeItemType.Channel Then
            result.SubscriberCount = entry.Value(Of Long?)("channel_follower_count")
            If Not result.SubscriberCount.HasValue Then
                result.SubscriberCount = entry.Value(Of Long?)("subscriber_count")
            End If
            If String.IsNullOrWhiteSpace(result.Title) Then
                result.Title = If(entry.Value(Of String)("channel"), uploader)
            End If
        End If

        Return result
    End Function

    Private Function GetChannelAboutMetadata(channelId As String) As JToken
        Try
            Dim url As String = $"https://www.youtube.com/channel/{channelId}/about"
            Dim arguments As String = $"-J --skip-download --ignore-errors {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            Return RunYtdlpJson(arguments)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetChannelAboutMetadata channelId={channelId}]: {ex.ToString()}")
            Return Nothing
        End Try
    End Function

    Public Function GetItemDetails(urlOrId As String, itemType As YoutubeItemType) As YoutubeItemDetails
        Dim details As New YoutubeItemDetails()
        details.ItemType = itemType
        details.Id = urlOrId

        Try
            Dim url As String
            Select Case itemType
                Case YoutubeItemType.Video
                    url = $"https://www.youtube.com/watch?v={urlOrId}"
                Case YoutubeItemType.Playlist
                    url = $"https://www.youtube.com/playlist?list={urlOrId}"
                Case YoutubeItemType.Channel
                    url = $"https://www.youtube.com/channel/{urlOrId}/videos"
                Case Else
                    Throw New ArgumentException("Tipo di elemento non supportato per i dettagli.")
            End Select

            Dim arguments As String
            If itemType = YoutubeItemType.Channel Then
                arguments = $"-J --flat-playlist --skip-download --ignore-errors {COMMON_ARGS} --playlist-items 0 -- {VirgolettaArgomento(url)}"
            Else
                arguments = $"-J --skip-download --ignore-errors {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            End If

            Dim jsonToken As JToken = RunYtdlpJson(arguments)

            Dim mainEntry As JToken = If(itemType = YoutubeItemType.Channel AndAlso jsonToken.SelectToken("entries") IsNot Nothing AndAlso jsonToken.SelectToken("entries").Count > 0, jsonToken.SelectToken("entries")(0), jsonToken)

            If itemType = YoutubeItemType.Channel Then
                Dim nomeCanalePulito As String = jsonToken.Value(Of String)("channel")
                If String.IsNullOrWhiteSpace(nomeCanalePulito) Then
                    nomeCanalePulito = jsonToken.Value(Of String)("uploader")
                End If
                If String.IsNullOrWhiteSpace(nomeCanalePulito) AndAlso mainEntry IsNot Nothing Then
                    nomeCanalePulito = mainEntry.Value(Of String)("channel")
                    If String.IsNullOrWhiteSpace(nomeCanalePulito) Then
                        nomeCanalePulito = mainEntry.Value(Of String)("uploader")
                    End If
                End If
                If String.IsNullOrWhiteSpace(nomeCanalePulito) Then
                    nomeCanalePulito = jsonToken.Value(Of String)("title")
                End If
                details.Title = nomeCanalePulito
            Else
                details.Title = jsonToken.Value(Of String)("title")
            End If
            details.Url = jsonToken.Value(Of String)("webpage_url")
            details.Description = jsonToken.Value(Of String)("description")
            details.UploadDate = ParseDate(jsonToken.Value(Of String)("upload_date"))
            details.ChannelName = jsonToken.Value(Of String)("uploader")
            details.ChannelId = jsonToken.Value(Of String)("channel_id")
            details.ChannelUrl = jsonToken.Value(Of String)("channel_url")
            details.Tags = UnisciArrayStringhe(jsonToken, "tags")
            details.Categories = UnisciArrayStringhe(jsonToken, "categories")

            If itemType = YoutubeItemType.Video Then
                details.DurationText = FormatDuration(jsonToken.Value(Of Long?)("duration"))
                details.ViewCount = jsonToken.Value(Of Long?)("view_count")
                details.LikeCount = jsonToken.Value(Of Long?)("like_count")
                details.CommentCount = jsonToken.Value(Of Long?)("comment_count")
                details.IsLive = jsonToken.Value(Of Boolean?)("is_live")
                details.WasLive = jsonToken.Value(Of Boolean?)("was_live")
                details.AgeLimit = jsonToken.Value(Of Integer?)("age_limit")
                details.License = jsonToken.Value(Of String)("license")
            ElseIf itemType = YoutubeItemType.Playlist Then
                details.VideoCount = jsonToken.Value(Of Integer?)("playlist_count")
                details.EstimatedDurationText = FormatDuration(jsonToken.Value(Of Long?)("duration"))
            ElseIf itemType = YoutubeItemType.Channel Then
                details.SubscriberCount = jsonToken.Value(Of Long?)("channel_follower_count")
                If Not details.SubscriberCount.HasValue Then
                    details.SubscriberCount = mainEntry.Value(Of Long?)("channel_follower_count")
                End If

                details.TotalVideoCount = jsonToken.Value(Of Long?)("playlist_count")
                If Not details.TotalVideoCount.HasValue Then
                    details.TotalVideoCount = jsonToken.Value(Of Long?)("size")
                End If

                details.ChannelVerificato = jsonToken.Value(Of Boolean?)("channel_is_verified")
                If Not details.ChannelVerificato.HasValue Then
                    details.ChannelVerificato = mainEntry.Value(Of Boolean?)("channel_is_verified")
                End If

                Dim idCanalePerAbout As String = If(String.IsNullOrWhiteSpace(details.ChannelId), urlOrId, details.ChannelId)
                Dim aboutJson As JToken = GetChannelAboutMetadata(idCanalePerAbout)
                If aboutJson IsNot Nothing Then
                    If Not details.SubscriberCount.HasValue Then
                        details.SubscriberCount = aboutJson.Value(Of Long?)("channel_follower_count")
                    End If
                    details.TotalViewCount = aboutJson.Value(Of Long?)("view_count")
                    If Not details.TotalVideoCount.HasValue Then
                        details.TotalVideoCount = aboutJson.Value(Of Long?)("playlist_count")
                    End If
                    If String.IsNullOrWhiteSpace(details.Description) Then
                        details.Description = aboutJson.Value(Of String)("description")
                    End If
                    If String.IsNullOrWhiteSpace(details.Tags) Then
                        details.Tags = UnisciArrayStringhe(aboutJson, "tags")
                    End If
                    If Not details.ChannelVerificato.HasValue Then
                        details.ChannelVerificato = aboutJson.Value(Of Boolean?)("channel_is_verified")
                    End If
                End If
            End If

        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetItemDetails for {urlOrId}]: {ex.ToString()}")
        End Try
        Return details
    End Function

    ''' <summary>I commenti di primo livello di un video, ognuno con le sue risposte. L'ordine è "top" (più rilevanti) o "new" (più recenti).</summary>
    Public Function GetVideoComments(videoId As String, Optional ordine As String = "top") As List(Of YoutubeComment)
        Dim comments As New List(Of YoutubeComment)
        Try
            Dim url As String = $"https://www.youtube.com/watch?v={videoId}"
            Dim ordineValido As String = If(ordine = "new", "new", "top")
            ' Limite di commenti e risposte, altrimenti nei video molto commentati l'attesa non finisce più.
            Dim arguments As String = $"-J --get-comments --skip-download --ignore-errors --extractor-args ""youtube:comment_sort={ordineValido};max_comments=200,all,all,30"" {COMMON_ARGS} -- {VirgolettaArgomento(url)}"

            Dim jsonToken As JToken = RunYtdlpJson(arguments)
            Dim commentsToken = jsonToken.SelectToken("comments")

            If commentsToken IsNot Nothing AndAlso commentsToken.Type = JTokenType.Array Then
                Dim perId As New Dictionary(Of String, YoutubeComment)
                Dim risposte As New List(Of YoutubeComment)
                For Each commentEntry In commentsToken.Children(Of JObject)
                    Dim commentText As String = commentEntry.Value(Of String)("text")
                    If Not String.IsNullOrWhiteSpace(commentText) Then
                        Dim comment As New YoutubeComment()
                        comment.Author = commentEntry.Value(Of String)("author")
                        comment.Text = commentText
                        comment.Timestamp = commentEntry.Value(Of String)("_time_text")
                        comment.LikeCount = If(commentEntry.Value(Of Long?)("like_count"), 0)
                        comment.Id = commentEntry.Value(Of String)("id")
                        comment.ParentId = commentEntry.Value(Of String)("parent")
                        comment.IsPinned = If(commentEntry.Value(Of Boolean?)("is_pinned"), False)
                        comment.AuthorIsUploader = If(commentEntry.Value(Of Boolean?)("author_is_uploader"), False)
                        If String.IsNullOrEmpty(comment.ParentId) OrElse comment.ParentId = "root" Then
                            comments.Add(comment)
                            If Not String.IsNullOrEmpty(comment.Id) Then perId(comment.Id) = comment
                        Else
                            risposte.Add(comment)
                        End If
                    End If
                Next
                ' Le risposte vanno sotto il loro commento (nell'ordine in cui arrivano, cioè cronologico).
                For Each r As YoutubeComment In risposte
                    Dim padre As YoutubeComment = Nothing
                    If perId.TryGetValue(r.ParentId, padre) Then padre.Replies.Add(r)
                Next
            End If

        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetVideoComments]: {ex.ToString()}")
        End Try
        Return comments
    End Function

    Private Function GetPlaylistMetadata(url As String) As (TotalCount As Integer?, Uploader As String, UploaderId As String)
        Try
            Dim args As String = $"-J --flat-playlist --playlist-items 0 {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            Dim json As JToken = RunYtdlpJson(args)

            Dim count As Integer? = json.Value(Of Integer?)("playlist_count")
            Dim uploader As String = json.Value(Of String)("uploader")
            Dim uploaderId As String = json.Value(Of String)("uploader_id")

            Return (count, uploader, uploaderId)
        Catch
            Return (Nothing, Nothing, Nothing)
        End Try
    End Function

    ''' <summary>
    ''' Carica TUTTI i video da URL canale/playlist (nessun limite artificiale).
    ''' Strategia:
    ''' 1) Metadati per conoscere il totale
    ''' 2) Se pochi o totale sconosciuto → una sola chiamata sequenziale (affidabile)
    ''' 3) Se molti → chunk paralleli (max MAX_CHUNK_PARALLELI) con retry per chunk
    ''' 4) Se il parallelo restituisce troppo poco rispetto al totale → fallback sequenziale completo
    ''' 5) Deduplicazione per Id
    ''' </summary>
    Private Function GetVideosFromUrl(urlOrId As String, Optional maxVideos As Integer = 0) As List(Of YoutubeSearchResult)
        Try
            Dim metadata = GetPlaylistMetadata(urlOrId)
            Dim totalVideos As Integer = If(metadata.TotalCount.HasValue, metadata.TotalCount.Value, 0)

            If maxVideos > 0 AndAlso totalVideos > maxVideos Then
                totalVideos = maxVideos
            End If

            ' Sequenziale: totale sconosciuto, piccolo, o maxVideos esplicito piccolo
            If totalVideos <= 0 OrElse totalVideos <= SOGLIA_SEQUENZIALE Then
                Return DeduplicaPerId(GetVideosSequential(urlOrId, If(maxVideos > 0, maxVideos, 0)))
            End If

            ' Parallelo controllato sull'INTERO canale/playlist
            Dim ranges As New List(Of Tuple(Of Integer, Integer))
            For i As Integer = 1 To totalVideos Step CHUNK_SIZE_VIDEO
                ranges.Add(Tuple.Create(i, Math.Min(i + CHUNK_SIZE_VIDEO - 1, totalVideos)))
            Next

            Dim risultatiPerChunk(ranges.Count - 1) As List(Of YoutubeSearchResult)
            Dim sem As New Threading.SemaphoreSlim(MAX_CHUNK_PARALLELI)
            Dim tasks As New List(Of Task)

            For idx As Integer = 0 To ranges.Count - 1
                Dim i = idx
                Dim r = ranges(i)
                tasks.Add(Task.Run(Sub()
                                       sem.Wait()
                                       Try
                                           risultatiPerChunk(i) = GetVideosChunkConRetry(urlOrId, r.Item1, r.Item2, metadata.Uploader, metadata.UploaderId)
                                       Finally
                                           sem.Release()
                                       End Try
                                   End Sub))
            Next

            Task.WaitAll(tasks.ToArray())

            Dim allVideos As New List(Of YoutubeSearchResult)(Math.Max(totalVideos, 16))
            For Each chunk In risultatiPerChunk
                If chunk IsNot Nothing AndAlso chunk.Count > 0 Then
                    allVideos.AddRange(chunk)
                End If
            Next

            allVideos = DeduplicaPerId(allVideos)

            ' Se abbiamo ottenuto meno dell'80% del totale dichiarato, riprova in sequenziale (più completo)
            If totalVideos > 0 AndAlso allVideos.Count < CInt(totalVideos * 0.8) Then
                Dim seq = DeduplicaPerId(GetVideosSequential(urlOrId, 0))
                If seq.Count > allVideos.Count Then
                    Return seq
                End If
            End If

            Return allVideos

        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetVideosFromUrl for {urlOrId}]: {ex.ToString()}")
            ' Ultimo tentativo: sequenziale completo
            Try
                Return DeduplicaPerId(GetVideosSequential(urlOrId, 0))
            Catch
                Return New List(Of YoutubeSearchResult)()
            End Try
        End Try
    End Function

    ''' <summary>Carica tutte le playlist da URL (es. pagina playlists di un canale).</summary>
    Private Function GetPlaylistsFromUrl(url As String) As List(Of YoutubeSearchResult)
        Dim playlists As New List(Of YoutubeSearchResult)()
        Try
            Dim arguments As String = $"-J --flat-playlist --skip-download --ignore-errors --compat-options no-youtube-unavailable-videos {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
            Dim jsonToken As JToken = RunYtdlpJson(arguments)
            Dim entries = jsonToken.SelectToken("entries")
            If entries Is Nothing Then Return playlists

            Dim channelName As String = jsonToken.Value(Of String)("uploader")
            Dim channelId As String = jsonToken.Value(Of String)("channel_id")
            If String.IsNullOrEmpty(channelId) Then
                channelId = jsonToken.Value(Of String)("uploader_id")
            End If

            For Each entry In entries
                Try
                    If Not VoceAmmessa(entry) Then Continue For

                    Dim id As String = entry.Value(Of String)("id")
                    Dim title As String = entry.Value(Of String)("title")
                    If String.IsNullOrEmpty(id) Then Continue For

                    Dim result As New YoutubeSearchResult()
                    result.Id = id
                    result.Title = title
                    result.Url = $"https://www.youtube.com/playlist?list={id}"
                    result.ChannelName = channelName
                    result.ChannelId = channelId
                    result.ItemType = YoutubeItemType.Playlist
                    result.UploadDate = ParseDate(entry.Value(Of String)("upload_date"))
                    result.ViewCount = entry.Value(Of Long?)("view_count")
                    result.VideoCount = entry.Value(Of Integer?)("playlist_count")
                    If Not result.VideoCount.HasValue Then
                        result.VideoCount = entry.Value(Of Integer?)("video_count")
                    End If
                    playlists.Add(result)
                Catch
                End Try
            Next

            Return DeduplicaPerId(playlists)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetPlaylistsFromUrl]: {ex.ToString()}")
            Return playlists
        End Try
    End Function

    Private Function DeduplicaPerId(lista As List(Of YoutubeSearchResult)) As List(Of YoutubeSearchResult)
        If lista Is Nothing OrElse lista.Count <= 1 Then Return If(lista, New List(Of YoutubeSearchResult)())
        Dim visti As New HashSet(Of String)(StringComparer.Ordinal)
        Dim out As New List(Of YoutubeSearchResult)(lista.Count)
        For Each item In lista
            If item Is Nothing OrElse String.IsNullOrEmpty(item.Id) Then Continue For
            If visti.Add(item.Id) Then
                out.Add(item)
            End If
        Next
        Return out
    End Function

    Private Function GetVideosSequential(url As String, Optional maxVideos As Integer = 0) As List(Of YoutubeSearchResult)
        Dim videos As New List(Of YoutubeSearchResult)()
        Dim limitArg As String = ""
        If maxVideos > 0 Then
            limitArg = $" --playlist-end {maxVideos}"
        End If
        Dim arguments As String = $"-J --flat-playlist --skip-download --ignore-errors --compat-options no-youtube-unavailable-videos{limitArg} {COMMON_ARGS} -- {VirgolettaArgomento(url)}"

        Dim jsonToken As JToken = RunYtdlpJson(arguments)
        Dim entries = jsonToken.SelectToken("entries")
        If entries Is Nothing Then Return videos

        Dim channelName As String = jsonToken.Value(Of String)("uploader")
        Dim channelId As String = jsonToken.Value(Of String)("uploader_id")

        For Each entry In entries
            Dim v = ParseVideoEntry(entry, channelName, channelId)
            If v IsNot Nothing Then videos.Add(v)
        Next
        Return videos
    End Function

    Private Function GetVideosChunkConRetry(url As String, startIdx As Integer, endIdx As Integer,
                                            defaultUploader As String, defaultUploaderId As String) As List(Of YoutubeSearchResult)
        Dim ultima As List(Of YoutubeSearchResult) = Nothing
        For tentativo As Integer = 1 To TENTATIVI_CHUNK
            Try
                ultima = GetVideosChunk(url, startIdx, endIdx, defaultUploader, defaultUploaderId)
                If ultima IsNot Nothing AndAlso ultima.Count > 0 Then
                    Return ultima
                End If
            Catch ex As Exception
                logger.ScriviLog($"ERRORE CHUNK {startIdx}-{endIdx} tentativo {tentativo}: {ex.Message}")
            End Try
            If tentativo < TENTATIVI_CHUNK Then
                Threading.Thread.Sleep(ATTESA_TRA_TENTATIVI_MS * tentativo)
            End If
        Next
        Return If(ultima, New List(Of YoutubeSearchResult)())
    End Function

    Private Function GetVideosChunk(url As String, startIdx As Integer, endIdx As Integer, defaultUploader As String, defaultUploaderId As String) As List(Of YoutubeSearchResult)
        Dim videos As New List(Of YoutubeSearchResult)()
        Dim arguments As String = $"-J --flat-playlist --skip-download --ignore-errors --playlist-start {startIdx} --playlist-end {endIdx} {COMMON_ARGS} -- {VirgolettaArgomento(url)}"
        Dim jsonToken As JToken = RunYtdlpJson(arguments)
        Dim entries = jsonToken.SelectToken("entries")

        If entries IsNot Nothing Then
            For Each entry In entries
                Dim v = ParseVideoEntry(entry, defaultUploader, defaultUploaderId)
                If v IsNot Nothing Then videos.Add(v)
            Next
        End If
        Return videos
    End Function

    Private Function ParseVideoEntry(entry As JToken, defaultChannelName As String, defaultChannelId As String) As YoutubeSearchResult
        Try
            If Not VoceAmmessa(entry) Then Return Nothing

            Dim id As String = entry.Value(Of String)("id")
            Dim title As String = entry.Value(Of String)("title")
            Dim durationSeconds As Long? = entry.Value(Of Long?)("duration")

            Dim video As New YoutubeSearchResult()
            video.Id = id
            video.Title = title
            video.Url = $"https://www.youtube.com/watch?v={id}"
            video.ChannelName = If(String.IsNullOrEmpty(entry.Value(Of String)("uploader")), defaultChannelName, entry.Value(Of String)("uploader"))
            video.ChannelId = If(String.IsNullOrEmpty(entry.Value(Of String)("uploader_id")), defaultChannelId, entry.Value(Of String)("uploader_id"))
            video.DurationSeconds = durationSeconds
            video.DurationText = If(durationSeconds IsNot Nothing, FormatDuration(durationSeconds), "")
            video.ItemType = YoutubeItemType.Video

            ' Campi opzionali per ordinamento e filtri
            video.UploadDate = ParseDate(entry.Value(Of String)("upload_date"))
            video.ViewCount = entry.Value(Of Long?)("view_count")
            video.LikeCount = entry.Value(Of Long?)("like_count")
            video.CommentCount = entry.Value(Of Long?)("comment_count")

            Return video
        Catch
            Return Nothing
        End Try
    End Function

    Public Function GetPlaylistVideos(playlistId As String,
                                      Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                      Optional minDurationSeconds As Long? = Nothing,
                                      Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Dim risultati = GetVideosFromUrl($"https://www.youtube.com/playlist?list={playlistId}")
        risultati = ApplicaFiltroDurata(risultati, minDurationSeconds, maxDurationSeconds)
        ApplicaOrdinamento(risultati, sortBy)
        Return risultati
    End Function

    Public Function GetChannelVideos(channelId As String,
                                     Optional sortBy As YoutubeSortBy = YoutubeSortBy.None,
                                     Optional minDurationSeconds As Long? = Nothing,
                                     Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Dim risultati = GetVideosFromUrl($"https://www.youtube.com/channel/{channelId}/videos")
        risultati = ApplicaFiltroDurata(risultati, minDurationSeconds, maxDurationSeconds)
        ApplicaOrdinamento(risultati, sortBy)
        Return risultati
    End Function

    Public Function GetChannelPlaylists(channelId As String,
                                        Optional sortBy As YoutubeSortBy = YoutubeSortBy.None) As List(Of YoutubeSearchResult)
        Dim playlists As New List(Of YoutubeSearchResult)()
        Try
            Dim url As String = $"https://www.youtube.com/channel/{channelId}/playlists"
            Dim arguments As String = $"-J --flat-playlist --skip-download --ignore-errors {COMMON_ARGS} -- {VirgolettaArgomento(url)}"

            Dim jsonToken As JToken = RunYtdlpJson(arguments)
            Dim entries = jsonToken.SelectToken("entries")

            If entries Is Nothing Then Return playlists

            Dim channelName As String = jsonToken.Value(Of String)("uploader")
            Dim chId As String = jsonToken.Value(Of String)("channel_id")

            For Each entry In entries
                If Not VoceAmmessa(entry) Then Continue For

                Dim id As String = entry.Value(Of String)("id")
                Dim title As String = entry.Value(Of String)("title")

                Dim result As New YoutubeSearchResult()
                result.Id = id
                result.Title = title
                result.Url = $"https://www.youtube.com/playlist?list={id}"
                result.ChannelName = channelName
                result.ChannelId = chId
                result.ItemType = YoutubeItemType.Playlist
                result.UploadDate = ParseDate(entry.Value(Of String)("upload_date"))
                result.ViewCount = entry.Value(Of Long?)("view_count")
                result.LikeCount = entry.Value(Of Long?)("like_count")
                result.VideoCount = entry.Value(Of Integer?)("playlist_count")
                If Not result.VideoCount.HasValue Then
                    result.VideoCount = entry.Value(Of Integer?)("video_count")
                End If
                Dim dur As Long? = entry.Value(Of Long?)("duration")
                If dur.HasValue Then
                    result.DurationSeconds = dur
                    result.DurationText = FormatDuration(dur)
                End If
                playlists.Add(result)
            Next

            ApplicaOrdinamento(playlists, sortBy)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.GetChannelPlaylists]: {ex.ToString()}")
        End Try
        Return playlists
    End Function

    Private Function UnisciArrayStringhe(token As JToken, nomeCampo As String) As String
        Try
            If token Is Nothing Then Return ""
            Dim arrayToken As JToken = token.SelectToken(nomeCampo)
            If arrayToken Is Nothing OrElse arrayToken.Type <> JTokenType.Array Then Return ""

            Dim valori As New List(Of String)
            For Each elemento In arrayToken
                Try
                    Dim testo As String = elemento.ToString()
                    If Not String.IsNullOrWhiteSpace(testo) Then valori.Add(testo)
                Catch exElemento As Exception
                    logger.ScriviLog($"ERRORE [YtdlpClient.UnisciArrayStringhe.Loop campo={nomeCampo}]: {exElemento.Message}")
                End Try
            Next
            Return String.Join(", ", valori)
        Catch ex As Exception
            logger.ScriviLog($"ERRORE [YtdlpClient.UnisciArrayStringhe campo={nomeCampo}]: {ex.ToString()}")
            Return ""
        End Try
    End Function

    Private Function ParseDate(dateString As String) As Date?
        If String.IsNullOrWhiteSpace(dateString) OrElse dateString.Length <> 8 Then Return Nothing
        Try
            Dim anno As Integer = Integer.Parse(dateString.Substring(0, 4))
            Dim mese As Integer = Integer.Parse(dateString.Substring(4, 2))
            Dim giorno As Integer = Integer.Parse(dateString.Substring(6, 2))
            Return New Date(anno, mese, giorno)
        Catch
            Return Nothing
        End Try
    End Function

    Private Function FormatDuration(durationInSeconds As Long?) As String
        If Not durationInSeconds.HasValue OrElse durationInSeconds.Value <= 0 Then Return ""

        ' TimeSpan.FromSeconds solleva OverflowException se il valore è enorme, e TimeSpan.Hours
        ' è solo il resto delle 24 ore: un video più lungo veniva mostrato con l'ora sbagliata.
        Dim secondi As Long = durationInSeconds.Value
        Dim ore As Long = secondi \ 3600
        Dim minuti As Long = (secondi \ 60) Mod 60
        Dim resto As Long = secondi Mod 60
        If ore > 0 Then
            Return ore.ToString() & ":" & minuti.ToString("00") & ":" & resto.ToString("00")
        End If
        Return minuti.ToString() & ":" & resto.ToString("00")
    End Function

    ' ==================== FILTRO DURATA + ORDINAMENTO ====================

    ''' <summary>
    ''' Filtra la lista per durata (in secondi). Gli elementi senza DurationSeconds vengono esclusi
    ''' se e attivo almeno un limite (min o max). Se entrambi Nothing, restituisce la lista invariata.
    ''' </summary>
    Private Function ApplicaFiltroDurata(risultati As List(Of YoutubeSearchResult),
                                         minDurationSeconds As Long?,
                                         maxDurationSeconds As Long?) As List(Of YoutubeSearchResult)
        If risultati Is Nothing Then Return New List(Of YoutubeSearchResult)()
        If Not minDurationSeconds.HasValue AndAlso Not maxDurationSeconds.HasValue Then
            Return risultati
        End If

        Return risultati.Where(Function(r)
                                   If Not r.DurationSeconds.HasValue Then Return False
                                   Dim d As Long = r.DurationSeconds.Value
                                   If minDurationSeconds.HasValue AndAlso d < minDurationSeconds.Value Then Return False
                                   If maxDurationSeconds.HasValue AndAlso d > maxDurationSeconds.Value Then Return False
                                   Return True
                               End Function).ToList()
    End Function

    ''' <summary>
    ''' Applica l'ordinamento a una lista gia in memoria (senza rifare la query).
    ''' </summary>
    Public Sub OrdinaRisultati(risultati As List(Of YoutubeSearchResult), sortBy As YoutubeSortBy)
        ApplicaOrdinamento(risultati, sortBy)
    End Sub

    ''' <summary>
    ''' Filtra per durata una lista gia in memoria.
    ''' </summary>
    Public Function FiltraPerDurata(risultati As List(Of YoutubeSearchResult),
                                    Optional minDurationSeconds As Long? = Nothing,
                                    Optional maxDurationSeconds As Long? = Nothing) As List(Of YoutubeSearchResult)
        Return ApplicaFiltroDurata(risultati, minDurationSeconds, maxDurationSeconds)
    End Function

    ''' <summary>
    ''' Applica l'ordinamento richiesto. Gli elementi con valore mancante (Nothing)
    ''' vengono posizionati alla fine indipendentemente dalla direzione.
    ''' </summary>
    Private Sub ApplicaOrdinamento(risultati As List(Of YoutubeSearchResult), sortBy As YoutubeSortBy)
        If risultati Is Nothing OrElse risultati.Count <= 1 OrElse sortBy = YoutubeSortBy.None Then Return

        Select Case sortBy
            Case YoutubeSortBy.DateAscending
                risultati.Sort(Function(a, b) ConfrontaNullableDate(a.UploadDate, b.UploadDate, ascending:=True))
            Case YoutubeSortBy.DateDescending
                risultati.Sort(Function(a, b) ConfrontaNullableDate(a.UploadDate, b.UploadDate, ascending:=False))
            Case YoutubeSortBy.ViewsAscending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.ViewCount, b.ViewCount, ascending:=True))
            Case YoutubeSortBy.ViewsDescending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.ViewCount, b.ViewCount, ascending:=False))
            Case YoutubeSortBy.LikesAscending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.LikeCount, b.LikeCount, ascending:=True))
            Case YoutubeSortBy.LikesDescending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.LikeCount, b.LikeCount, ascending:=False))
            Case YoutubeSortBy.SubscribersAscending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.SubscriberCount, b.SubscriberCount, ascending:=True))
            Case YoutubeSortBy.SubscribersDescending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.SubscriberCount, b.SubscriberCount, ascending:=False))
            Case YoutubeSortBy.TitleAscending
                risultati.Sort(Function(a, b) String.Compare(If(a.Title, ""), If(b.Title, ""), StringComparison.CurrentCultureIgnoreCase))
            Case YoutubeSortBy.TitleDescending
                risultati.Sort(Function(a, b) String.Compare(If(b.Title, ""), If(a.Title, ""), StringComparison.CurrentCultureIgnoreCase))
            Case YoutubeSortBy.DurationAscending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.DurationSeconds, b.DurationSeconds, ascending:=True))
            Case YoutubeSortBy.DurationDescending
                risultati.Sort(Function(a, b) ConfrontaNullableLong(a.DurationSeconds, b.DurationSeconds, ascending:=False))
            Case YoutubeSortBy.VideoCountAscending
                risultati.Sort(Function(a, b) ConfrontaNullableInt(a.VideoCount, b.VideoCount, ascending:=True))
            Case YoutubeSortBy.VideoCountDescending
                risultati.Sort(Function(a, b) ConfrontaNullableInt(a.VideoCount, b.VideoCount, ascending:=False))
        End Select
    End Sub

    Private Function ConfrontaNullableDate(d1 As Date?, d2 As Date?, ascending As Boolean) As Integer
        If Not d1.HasValue AndAlso Not d2.HasValue Then Return 0
        If Not d1.HasValue Then Return 1
        If Not d2.HasValue Then Return -1
        Dim cmp As Integer = d1.Value.CompareTo(d2.Value)
        Return If(ascending, cmp, -cmp)
    End Function

    Private Function ConfrontaNullableLong(v1 As Long?, v2 As Long?, ascending As Boolean) As Integer
        If Not v1.HasValue AndAlso Not v2.HasValue Then Return 0
        If Not v1.HasValue Then Return 1
        If Not v2.HasValue Then Return -1
        Dim cmp As Integer = v1.Value.CompareTo(v2.Value)
        Return If(ascending, cmp, -cmp)
    End Function

    Private Function ConfrontaNullableInt(v1 As Integer?, v2 As Integer?, ascending As Boolean) As Integer
        If Not v1.HasValue AndAlso Not v2.HasValue Then Return 0
        If Not v1.HasValue Then Return 1
        If Not v2.HasValue Then Return -1
        Dim cmp As Integer = v1.Value.CompareTo(v2.Value)
        Return If(ascending, cmp, -cmp)
    End Function

    ''' <summary>
    ''' Verifica se nella lista sono presenti i dati necessari per un determinato ordinamento.
    ''' Restituisce True se almeno un elemento ha il campo valorizzato.
    ''' </summary>
    Public Function SonoDisponibiliDatiOrdinamento(risultati As List(Of YoutubeSearchResult), sortBy As YoutubeSortBy) As Boolean
        If risultati Is Nothing OrElse risultati.Count = 0 Then Return False

        Select Case sortBy
            Case YoutubeSortBy.None
                Return True
            Case YoutubeSortBy.DateAscending, YoutubeSortBy.DateDescending
                Return risultati.Any(Function(r) r.UploadDate.HasValue)
            Case YoutubeSortBy.ViewsAscending, YoutubeSortBy.ViewsDescending
                Return risultati.Any(Function(r) r.ViewCount.HasValue)
            Case YoutubeSortBy.LikesAscending, YoutubeSortBy.LikesDescending
                Return risultati.Any(Function(r) r.LikeCount.HasValue)
            Case YoutubeSortBy.SubscribersAscending, YoutubeSortBy.SubscribersDescending
                Return risultati.Any(Function(r) r.SubscriberCount.HasValue)
            Case YoutubeSortBy.TitleAscending, YoutubeSortBy.TitleDescending
                Return risultati.Any(Function(r) Not String.IsNullOrWhiteSpace(r.Title))
            Case YoutubeSortBy.DurationAscending, YoutubeSortBy.DurationDescending
                Return risultati.Any(Function(r) r.DurationSeconds.HasValue)
            Case YoutubeSortBy.VideoCountAscending, YoutubeSortBy.VideoCountDescending
                Return risultati.Any(Function(r) r.VideoCount.HasValue)
            Case Else
                Return False
        End Select
    End Function

    ''' <summary>
    ''' Verifica se e possibile applicare un filtro per durata (almeno un elemento ha DurationSeconds).
    ''' </summary>
    Public Function SonoDisponibiliDatiFiltroDurata(risultati As List(Of YoutubeSearchResult)) As Boolean
        If risultati Is Nothing OrElse risultati.Count = 0 Then Return False
        Return risultati.Any(Function(r) r.DurationSeconds.HasValue)
    End Function

    ''' <summary>
    ''' Restituisce l'elenco dei criteri di ordinamento effettivamente utilizzabili
    ''' in base ai dati presenti nella lista fornita.
    ''' </summary>
    Public Function OttieniOrdinamentiDisponibili(risultati As List(Of YoutubeSearchResult)) As List(Of YoutubeSortBy)
        Dim disponibili As New List(Of YoutubeSortBy)()
        disponibili.Add(YoutubeSortBy.None)

        If risultati Is Nothing OrElse risultati.Count = 0 Then Return disponibili

        If risultati.Any(Function(r) r.UploadDate.HasValue) Then
            disponibili.Add(YoutubeSortBy.DateAscending)
            disponibili.Add(YoutubeSortBy.DateDescending)
        End If
        If risultati.Any(Function(r) r.ViewCount.HasValue) Then
            disponibili.Add(YoutubeSortBy.ViewsAscending)
            disponibili.Add(YoutubeSortBy.ViewsDescending)
        End If
        If risultati.Any(Function(r) r.LikeCount.HasValue) Then
            disponibili.Add(YoutubeSortBy.LikesAscending)
            disponibili.Add(YoutubeSortBy.LikesDescending)
        End If
        If risultati.Any(Function(r) r.SubscriberCount.HasValue) Then
            disponibili.Add(YoutubeSortBy.SubscribersAscending)
            disponibili.Add(YoutubeSortBy.SubscribersDescending)
        End If
        If risultati.Any(Function(r) Not String.IsNullOrWhiteSpace(r.Title)) Then
            disponibili.Add(YoutubeSortBy.TitleAscending)
            disponibili.Add(YoutubeSortBy.TitleDescending)
        End If
        If risultati.Any(Function(r) r.DurationSeconds.HasValue) Then
            disponibili.Add(YoutubeSortBy.DurationAscending)
            disponibili.Add(YoutubeSortBy.DurationDescending)
        End If
        If risultati.Any(Function(r) r.VideoCount.HasValue) Then
            disponibili.Add(YoutubeSortBy.VideoCountAscending)
            disponibili.Add(YoutubeSortBy.VideoCountDescending)
        End If

        Return disponibili
    End Function
End Class