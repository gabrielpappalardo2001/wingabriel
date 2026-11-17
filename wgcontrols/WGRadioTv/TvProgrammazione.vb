Imports System
Imports System.Collections.Generic
Imports System.Net
Imports System.Net.Http
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Programmazione dei canali televisivi, presa da due fonti pubbliche che rispondono in JSON:
'  - il feed dei palinsesti di Mediaset (theplatform), per i canali Mediaset;
'  - la guida tv di Sky (apid.sky.it), per tutti gli altri canali e come ripiego per quelli Mediaset.
' Ogni canale usa una sola fonte alla volta e i programmi uguali (stesso inizio e stesso titolo)
' vengono tenuti una volta sola.
Public Class TvProgrammazione

    Public Class VoceProgramma
        Public Property Inizio As DateTime
        Public Property Fine As DateTime
        Public Property Titolo As String = ""
        Public Property Descrizione As String = ""
    End Class

    Private Class DatiInCache
        Public Property ScaricatoAlle As DateTime
        Public Property Programmi As List(Of VoceProgramma)
    End Class

    Private Const URL_MEDIASET As String = "https://feed.entertainment.tv.theplatform.eu/f/PR1GhC/mediaset-prod-all-listings-v2"
    Private Const URL_SKY As String = "https://apid.sky.it/gtv/v1/events"
    Private Const GIORNI_DA_SCARICARE As Integer = 2
    Private Const ORE_MEDIASET As Integer = 34
    Private Const MINUTI_VALIDITA_CACHE As Integer = 30
    Private Const SECONDI_TIMEOUT As Integer = 25
    Private Const PAGINA_SKY As Integer = 300
    Private Const MASSIMO_PAGINE_SKY As Integer = 5

    Private Shared ReadOnly _logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _cache As New Dictionary(Of String, DatiInCache)(StringComparer.OrdinalIgnoreCase)
    Private Shared _client As HttpClient

    ' Sigla del canale nel feed Mediaset, per tvg-id del canale.
    Private Shared ReadOnly SigleMediaset As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"rete4.it", "R4"}, {"canale5.it", "C5"}, {"italia1.it", "I1"}, {"italia2.it", "I2"},
        {"la5.it", "KA"}, {"iris.it", "KI"}, {"cine34.it", "B6"}, {"focus.it", "FU"},
        {"topcrime.it", "LT"}, {"boing.it", "KB"}, {"cartoonito.it", "LA"},
        {"mediasetextra.it", "KQ"}, {"tgcom24.it", "KF"}, {"20mediaset.it", "LB"},
        {"twentyseven.it", "TS"}
    }

    ' Numero del canale nella guida Sky, per tvg-id del canale.
    Private Shared ReadOnly CanaliSky As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
        {"la7.it", 319}, {"tv8.it", 8195}, {"cielotv.it", 8133}, {"skytg24.it", 9117},
        {"nove.it", 12116}, {"realtime.it", 12117}, {"giallotv.it", 12126}, {"dmax.it", 12120},
        {"rete4.it", 10464}, {"canale5.it", 10354}, {"italia1.it", 10454}, {"italia2.it", 10469},
        {"la5.it", 10466}, {"iris.it", 10467}, {"cine34.it", 10893}, {"focus.it", 10470},
        {"topcrime.it", 10468}, {"boing.it", 6628}, {"cartoonito.it", 8132},
        {"mediasetextra.it", 10465}, {"tgcom24.it", 10473}, {"20mediaset.it", 10458},
        {"twentyseven.it", 11342},
        {"la7d.it", 11430}
    }

    Public Shared Function ProgrammazioneDisponibile(tvgId As String, Optional nome As String = "") As Boolean
        Try
            If Not String.IsNullOrWhiteSpace(tvgId) AndAlso (SigleMediaset.ContainsKey(tvgId) OrElse CanaliSky.ContainsKey(tvgId)) Then Return True
            ' i canali che non sono nelle tabelle fisse si cercano per nome negli elenchi delle fonti (GuidaPalinsesti)
            Return Not String.IsNullOrWhiteSpace(nome)
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.ProgrammazioneDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    ' Restituisce i programmi in onda adesso e quelli che seguono (oggi e domani).
    ' Nothing se non è stato possibile scaricarli, lista vuota se la fonte non ne ha.
    Public Shared Async Function OttieniAsync(tvgId As String, Optional nome As String = "") As Task(Of List(Of VoceProgramma))
        Try
            If Not ProgrammazioneDisponibile(tvgId, nome) Then Return Nothing

            Dim chiaveCache As String = If(String.IsNullOrWhiteSpace(tvgId), "nome|" & GuidaPalinsesti.Chiave(nome), tvgId)
            Dim inTabella As Boolean = Not String.IsNullOrWhiteSpace(tvgId) AndAlso (SigleMediaset.ContainsKey(tvgId) OrElse CanaliSky.ContainsKey(tvgId))

            SyncLock _lock
                Dim inCache As DatiInCache = Nothing
                If _cache.TryGetValue(chiaveCache, inCache) Then
                    If (DateTime.Now - inCache.ScaricatoAlle).TotalMinutes < MINUTI_VALIDITA_CACHE Then
                        Return DaOra(inCache.Programmi)
                    End If
                End If
            End SyncLock

            Dim programmi As List(Of VoceProgramma) = Nothing

            If inTabella Then
                If SigleMediaset.ContainsKey(tvgId) Then
                    programmi = Await ScaricaMediasetAsync(SigleMediaset(tvgId)).ConfigureAwait(False)
                End If

                If (programmi Is Nothing OrElse programmi.Count = 0) AndAlso CanaliSky.ContainsKey(tvgId) Then
                    programmi = Await ScaricaSkyAsync(CanaliSky(tvgId)).ConfigureAwait(False)
                End If
            End If

            ' un canale fuori dalle tabelle, o che la sua fonte non ha dato, si cerca per nome in RaiPlay, Mediaset e Sky
            If (programmi Is Nothing OrElse programmi.Count = 0) AndAlso Not String.IsNullOrWhiteSpace(nome) Then
                Dim perNome As List(Of VoceProgramma) = Await GuidaPalinsesti.CercaPerNomeAsync(nome, False).ConfigureAwait(False)
                If perNome IsNot Nothing AndAlso perNome.Count > 0 Then programmi = perNome
                If programmi Is Nothing Then programmi = perNome
            End If

            If programmi Is Nothing Then Return Nothing

            Dim pulita As List(Of VoceProgramma) = TogliDuplicati(programmi)

            SyncLock _lock
                Dim nuovi As New DatiInCache()
                nuovi.ScaricatoAlle = DateTime.Now
                nuovi.Programmi = pulita
                _cache(chiaveCache) = nuovi
            End SyncLock

            Return DaOra(pulita)
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.OttieniAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

#Region "Mediaset"

    Friend Shared Async Function ScaricaMediasetAsync(sigla As String) As Task(Of List(Of VoceProgramma))
        Try
            ' Il feed rifiuta le richieste oltre 2000 ore di palinsesto (conta 50 canali anche se se ne
            ' chiede uno solo): si chiedono le prossime 34 ore, cioè il programma in onda e tutto domani.
            Dim inizio As DateTime = DateTime.Now
            Dim fine As DateTime = inizio.AddHours(ORE_MEDIASET)
            Dim da As Long = New DateTimeOffset(inizio).ToUnixTimeMilliseconds()
            Dim a As Long = New DateTimeOffset(fine).ToUnixTimeMilliseconds()

            Dim indirizzo As String = URL_MEDIASET & "?form=cjson&byCallSign=" & Uri.EscapeDataString(sigla) &
                "&byListingTime=" & da.ToString() & "~" & a.ToString()

            Dim testo As String = Await ScaricaTestoAsync(indirizzo).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing

            Dim radice As JObject = AnalizzaJson(testo)
            If radice("isException") IsNot Nothing Then
                _logger.ScriviLog("TvProgrammazione.ScaricaMediasetAsync eccezione: " & Convert.ToString(radice("description")))
                Return Nothing
            End If

            Dim risultato As New List(Of VoceProgramma)
            Dim voci As JArray = TryCast(radice("entries"), JArray)
            If voci Is Nothing Then Return risultato

            For Each voce As JToken In voci
                Dim elenco As JArray = TryCast(voce("listings"), JArray)
                If elenco Is Nothing Then Continue For

                For Each p As JToken In elenco
                    Try
                        Dim msInizio As Long = p.Value(Of Long)("startTime")
                        Dim msFine As Long = p.Value(Of Long)("endTime")
                        If msInizio <= 0 OrElse msFine <= msInizio Then Continue For

                        Dim titolo As String = TestoDi(p("mediasetlisting$epgTitle"))
                        If titolo.Length = 0 Then titolo = TestoDi(p("program")?("title"))
                        If titolo.Length = 0 Then Continue For

                        Dim descrizione As String = TestoDi(p("description"))
                        If descrizione.Length = 0 Then descrizione = TestoDi(p("mediasetlisting$shortDescription"))
                        If descrizione.Length = 0 Then descrizione = TestoDi(p("program")?("description"))

                        Dim v As New VoceProgramma()
                        v.Inizio = DateTimeOffset.FromUnixTimeMilliseconds(msInizio).LocalDateTime
                        v.Fine = DateTimeOffset.FromUnixTimeMilliseconds(msFine).LocalDateTime
                        v.Titolo = titolo
                        v.Descrizione = descrizione
                        risultato.Add(v)
                    Catch exVoce As Exception
                        _logger.ScriviLog("TvProgrammazione.ScaricaMediasetAsync voce: " & exVoce.Message)
                    End Try
                Next
            Next

            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.ScaricaMediasetAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

#End Region

#Region "Sky"

    Friend Shared Async Function ScaricaSkyAsync(numeroCanale As Integer) As Task(Of List(Of VoceProgramma))
        Try
            Dim inizio As DateTime = DateTime.Today
            Dim fine As DateTime = inizio.AddDays(GIORNI_DA_SCARICARE)
            Dim da As String = inizio.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Globalization.CultureInfo.InvariantCulture)
            Dim a As String = fine.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Globalization.CultureInfo.InvariantCulture)

            Dim risultato As New List(Of VoceProgramma)
            Dim pagina As Integer = 0
            Dim totale As Integer = Integer.MaxValue

            While pagina < MASSIMO_PAGINE_SKY AndAlso risultato.Count < totale
                Dim indirizzo As String = URL_SKY & "?env=DTH&channels=" & numeroCanale.ToString() &
                    "&from=" & Uri.EscapeDataString(da) & "&to=" & Uri.EscapeDataString(a) &
                    "&pageSize=" & PAGINA_SKY.ToString() & "&pageNum=" & pagina.ToString()

                Dim testo As String = Await ScaricaTestoAsync(indirizzo).ConfigureAwait(False)
                If String.IsNullOrWhiteSpace(testo) Then
                    If pagina = 0 Then Return Nothing
                    Exit While
                End If

                Dim radice As JObject = AnalizzaJson(testo)
                Dim eventi As JArray = TryCast(radice("events"), JArray)
                If eventi Is Nothing OrElse eventi.Count = 0 Then Exit While

                Dim t As JToken = radice("total")
                If t IsNot Nothing Then totale = t.Value(Of Integer)()

                For Each e As JToken In eventi
                    Try
                        Dim titolo As String = TestoDi(e("eventTitle"))
                        If titolo.Length = 0 Then titolo = TestoDi(e("epgEventTitle"))
                        If titolo.Length = 0 Then titolo = TestoDi(e("content")?("contentTitle"))
                        If titolo.Length = 0 Then Continue For

                        Dim v As New VoceProgramma()
                        v.Inizio = DateTime.Parse(TestoDi(e("starttime")), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal).ToLocalTime()
                        v.Fine = DateTime.Parse(TestoDi(e("endtime")), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal).ToLocalTime()
                        If v.Fine <= v.Inizio Then Continue For
                        v.Titolo = titolo
                        v.Descrizione = TestoDi(e("eventSynopsis"))
                        risultato.Add(v)
                    Catch exVoce As Exception
                        _logger.ScriviLog("TvProgrammazione.ScaricaSkyAsync voce: " & exVoce.Message)
                    End Try
                Next

                pagina = pagina + 1
            End While

            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.ScaricaSkyAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

#End Region

#Region "Utilita"

    ' Legge il JSON lasciando le date come testo: Newtonsoft altrimenti trasforma da sé "2026-11-07T00:22:02Z" in una data e poi, riletta come testo,
    ' esce nel formato del computer (il 7 novembre diventa l'11 luglio se la si legge all'americana).
    Friend Shared Function AnalizzaJson(testo As String) As JObject
        Using lettore As New Newtonsoft.Json.JsonTextReader(New System.IO.StringReader(testo))
            lettore.DateParseHandling = Newtonsoft.Json.DateParseHandling.None
            Return JObject.Load(lettore)
        End Using
    End Function

    Friend Shared Function TestoDi(t As JToken) As String
        Try
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return ""
            Dim s As String = Convert.ToString(t)
            If s Is Nothing Then Return ""
            Return s.Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function ClienteHttp() As HttpClient
        SyncLock _lock
            If _client Is Nothing Then
                Try
                    ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType) Or CType(12288, SecurityProtocolType)
                Catch
                    Try
                        ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
                    Catch exTls As Exception
                        _logger.ScriviLog("TvProgrammazione.ClienteHttp tls: " & exTls.Message)
                    End Try
                End Try

                Dim gestore As New HttpClientHandler()
                gestore.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
                _client = New HttpClient(gestore)
                _client.Timeout = TimeSpan.FromSeconds(SECONDI_TIMEOUT)
                _client.DefaultRequestHeaders.UserAgent.ParseAdd(TvCanaliDb.UA_BROWSER)
                _client.DefaultRequestHeaders.Accept.ParseAdd("application/json")
            End If
            Return _client
        End SyncLock
    End Function

    Friend Shared Async Function ScaricaTestoAsync(indirizzo As String) As Task(Of String)
        Try
            Using risposta As HttpResponseMessage = Await ClienteHttp().GetAsync(indirizzo).ConfigureAwait(False)
                If Not risposta.IsSuccessStatusCode Then
                    _logger.ScriviLog("TvProgrammazione.ScaricaTestoAsync stato " & CInt(risposta.StatusCode).ToString() & " per " & indirizzo)
                    Return Nothing
                End If
                Return Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
            End Using
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.ScaricaTestoAsync (" & indirizzo & "): " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' Un programma è lo stesso se ha lo stesso inizio e lo stesso titolo; se due programmi diversi
    ' cominciano alla stessa ora si tiene il primo.
    Friend Shared Function TogliDuplicati(programmi As List(Of VoceProgramma)) As List(Of VoceProgramma)
        Dim risultato As New List(Of VoceProgramma)
        Try
            Dim viste As New HashSet(Of Long)
            Dim ordinati As New List(Of VoceProgramma)(programmi)
            ordinati.Sort(Function(x As VoceProgramma, y As VoceProgramma) x.Inizio.CompareTo(y.Inizio))

            For i As Integer = 0 To ordinati.Count - 1
                Dim p As VoceProgramma = ordinati(i)
                Dim chiave As Long = p.Inizio.Ticks \ TimeSpan.TicksPerMinute
                If Not viste.Add(chiave) Then Continue For
                risultato.Add(p)
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.TogliDuplicati: " & ex.Message)
            Return programmi
        End Try
        Return risultato
    End Function

    Friend Shared Function DaOra(programmi As List(Of VoceProgramma)) As List(Of VoceProgramma)
        Dim risultato As New List(Of VoceProgramma)
        Try
            Dim adesso As DateTime = DateTime.Now
            For i As Integer = 0 To programmi.Count - 1
                If programmi(i).Fine > adesso Then risultato.Add(programmi(i))
            Next
        Catch ex As Exception
            _logger.ScriviLog("TvProgrammazione.DaOra: " & ex.Message)
        End Try
        Return risultato
    End Function

#End Region

End Class
