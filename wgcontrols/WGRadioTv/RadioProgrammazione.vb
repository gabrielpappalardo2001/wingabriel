Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Programmazione delle radio, da fonti pubbliche che rispondono in JSON:
'  - RaiPlay Sound (raiplaysound.it), per le radio Rai e le altre del suo palinsesto: l'elenco dei canali e i programmi di ogni giorno;
'  - il feed dei palinsesti di Mediaset, per Radio 105, Virgin Radio, R101 e Radio Monte Carlo;
'  - la guida tv di Sky (apid.sky.it), per RTL 102.5, Radio Italia, Radio Deejay, Radio Norba e RadioFreccia.
' Ogni radio usa una sola fonte e i programmi che cominciano allo stesso minuto si tengono una volta sola. La radio si riconosce dal nome:
' per le altre non c'è una fonte e la programmazione non è disponibile.
Public Class RadioProgrammazione

    Private Class DatiInCache
        Public Property ScaricatoAlle As DateTime
        Public Property Programmi As List(Of TvProgrammazione.VoceProgramma)
    End Class

    Private Enum Fonte
        Nessuna = 0
        Rai = 1
        Mediaset = 2
        Sky = 3
    End Enum

    Private Const URL_RAI_ELENCO As String = "https://www.raiplaysound.it/palinsesto.json"
    Private Const URL_RAI_GIORNO As String = "https://www.raiplaysound.it/palinsesto/app/"
    Private Const MINUTI_VALIDITA_CACHE As Integer = 30

    Private Shared ReadOnly _logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _cache As New Dictionary(Of String, DatiInCache)(StringComparer.OrdinalIgnoreCase)
    Private Shared _canaliRai As Dictionary(Of String, String)
    Private Shared _canaliRaiScaricatiAlle As DateTime = DateTime.MinValue

    ' Nomi (senza spazi, accenti e segni) con cui le radio si presentano negli elenchi, e la sigla Mediaset o il numero Sky della radio.
    Private Shared ReadOnly NomiMediaset As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"radio105", "EC"}, {"radio105fm", "EC"}, {"radio105network", "EC"}, {"105", "EC"},
        {"virginradio", "EW"}, {"virginradioitalia", "EW"}, {"virginradiofm", "EW"},
        {"r101", "ER"}, {"radior101", "ER"},
        {"radiomontecarlo", "BB"}, {"rmc", "BB"}, {"radiomontecarlofm", "BB"}
    }

    Private Shared ReadOnly NomiSky As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
        {"rtl1025", 6885}, {"rtl1025hitradio", 6885}, {"rtl1025fm", 6885}, {"rtl1025radio", 6885}, {"rtl1025it", 6885},
        {"radioitalia", 9833}, {"radioitaliafm", 9833}, {"radioitaliasolomusicaitaliana", 9833},
        {"radiodeejay", 462}, {"deejay", 462},
        {"radionorba", 8213}, {"radionorbafm", 8213},
        {"radiofreccia", 10616}, {"radiofrecciafm", 10616}
    }

    Private Shared ReadOnly NomiRaiAggiuntivi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"isoradio", "raiisoradio"}, {"radiosanmarino", "radiosanmarino"}, {"nonameradio", "nonameradio"}
    }

    Private Sub New()
    End Sub

    Public Shared Function ProgrammazioneDisponibile(nomeRadio As String) As Boolean
        Try
            ' le radio che non sono nelle tabelle fisse si cercano per nome negli elenchi delle fonti (GuidaPalinsesti)
            Return Normalizza(nomeRadio).Length > 0
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.ProgrammazioneDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    ' I programmi in onda adesso e quelli che seguono (oggi e domani). Nothing se non è stato possibile scaricarli, lista vuota se la fonte non ne ha.
    Public Shared Async Function OttieniAsync(nomeRadio As String) As Task(Of List(Of TvProgrammazione.VoceProgramma))
        Try
            Dim chiave As String = Normalizza(nomeRadio)
            If chiave.Length = 0 Then Return Nothing
            Dim fonte As Fonte = Riconosci(chiave)

            Dim chiaveCache As String = CInt(fonte).ToString() & "|" & chiave
            SyncLock _lock
                Dim inCache As DatiInCache = Nothing
                If _cache.TryGetValue(chiaveCache, inCache) Then
                    If (DateTime.Now - inCache.ScaricatoAlle).TotalMinutes < MINUTI_VALIDITA_CACHE Then Return TvProgrammazione.DaOra(inCache.Programmi)
                End If
            End SyncLock

            Dim programmi As List(Of TvProgrammazione.VoceProgramma) = Nothing
            Select Case fonte
                Case Fonte.Nessuna
                    programmi = Await GuidaPalinsesti.CercaPerNomeAsync(nomeRadio, True).ConfigureAwait(False)
                Case Fonte.Rai
                    programmi = Await ScaricaRaiAsync(chiave).ConfigureAwait(False)
                Case Fonte.Mediaset
                    programmi = Await TvProgrammazione.ScaricaMediasetAsync(NomiMediaset(chiave)).ConfigureAwait(False)
                    If (programmi Is Nothing OrElse programmi.Count = 0) AndAlso NomiSky.ContainsKey(chiave) Then
                        programmi = Await TvProgrammazione.ScaricaSkyAsync(NomiSky(chiave)).ConfigureAwait(False)
                    End If
                Case Fonte.Sky
                    programmi = Await TvProgrammazione.ScaricaSkyAsync(NomiSky(chiave)).ConfigureAwait(False)
            End Select

            If (programmi Is Nothing OrElse programmi.Count = 0) AndAlso fonte <> Fonte.Nessuna Then
                Dim perNome As List(Of TvProgrammazione.VoceProgramma) = Await GuidaPalinsesti.CercaPerNomeAsync(nomeRadio, True).ConfigureAwait(False)
                If perNome IsNot Nothing AndAlso perNome.Count > 0 Then programmi = perNome
            End If

            If programmi Is Nothing Then Return Nothing

            Dim pulita As List(Of TvProgrammazione.VoceProgramma) = TvProgrammazione.TogliDuplicati(programmi)
            SyncLock _lock
                Dim nuovi As New DatiInCache()
                nuovi.ScaricatoAlle = DateTime.Now
                nuovi.Programmi = pulita
                _cache(chiaveCache) = nuovi
            End SyncLock
            Return TvProgrammazione.DaOra(pulita)
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.OttieniAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

#Region "Riconoscere la radio"

    ' Il nome senza maiuscole, accenti, spazi e segni; "radio rai" diventa "rai radio" come nell'elenco della Rai.
    Private Shared Function Normalizza(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim scomposto As String = nome.Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder()
            For Each c As Char In scomposto
                If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(c) Then sb.Append(Char.ToLowerInvariant(c))
            Next
            Dim r As String = sb.ToString()
            If r.StartsWith("radiorai", StringComparison.Ordinal) Then r = "rairadio" & r.Substring(8)
            Return r
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.Normalizza: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Una chiave uguale per tutte le radio che usano la stessa fonte e lo stesso canale (per non mostrare due volte la stessa emittente);
    ' vuota se per quel nome non c'è una fonte.
    Friend Shared Function ChiaveFonte(nomeRadio As String) As String
        Dim chiave As String = Normalizza(nomeRadio)
        If chiave.Length = 0 Then Return ""
        Select Case Riconosci(chiave)
            Case Fonte.Mediaset : Return "mediaset|" & NomiMediaset(chiave)
            Case Fonte.Sky : Return "sky|" & NomiSky(chiave)
            Case Fonte.Rai : Return "rai|" & If(NomiRaiAggiuntivi.ContainsKey(chiave), NomiRaiAggiuntivi(chiave), chiave)
        End Select
        Return ""
    End Function

    Private Shared Function Riconosci(chiave As String) As Fonte
        If chiave.Length = 0 Then Return Fonte.Nessuna
        If NomiMediaset.ContainsKey(chiave) Then Return Fonte.Mediaset
        If NomiSky.ContainsKey(chiave) Then Return Fonte.Sky
        If NomiRaiAggiuntivi.ContainsKey(chiave) Then Return Fonte.Rai
        If chiave.StartsWith("rairadio", StringComparison.Ordinal) OrElse chiave.StartsWith("raigr", StringComparison.Ordinal) OrElse chiave.StartsWith("raiisoradio", StringComparison.Ordinal) Then Return Fonte.Rai
        Return Fonte.Nessuna
    End Function

#End Region

#Region "RaiPlay Sound"

    ' L'elenco dei canali della Rai (nome normalizzato e indirizzo del palinsesto), scaricato una volta ogni mezz'ora.
    Private Shared Async Function CanaliRaiAsync() As Task(Of Dictionary(Of String, String))
        Try
            SyncLock _lock
                If _canaliRai IsNot Nothing AndAlso (DateTime.Now - _canaliRaiScaricatiAlle).TotalMinutes < MINUTI_VALIDITA_CACHE Then Return _canaliRai
            End SyncLock

            Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(URL_RAI_ELENCO).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing

            Dim radice As JObject = TvProgrammazione.AnalizzaJson(testo)
            Dim canali As JArray = TryCast(radice("channels"), JArray)
            If canali Is Nothing Then Return Nothing

            Dim risultato As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each c As JToken In canali
                Dim nome As String = TvProgrammazione.TestoDi(c("name"))
                Dim indirizzo As String = TvProgrammazione.TestoDi(c("palinsesto_url"))
                If nome.Length = 0 OrElse indirizzo.Length = 0 Then Continue For
                risultato(Normalizza(nome)) = indirizzo
            Next

            SyncLock _lock
                _canaliRai = risultato
                _canaliRaiScaricatiAlle = DateTime.Now
            End SyncLock
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.CanaliRaiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Async Function ScaricaRaiAsync(chiave As String) As Task(Of List(Of TvProgrammazione.VoceProgramma))
        Try
            Dim canali As Dictionary(Of String, String) = Await CanaliRaiAsync().ConfigureAwait(False)
            If canali Is Nothing Then Return Nothing

            Dim indirizzoCanale As String = Nothing
            Dim cercato As String = chiave
            If NomiRaiAggiuntivi.ContainsKey(cercato) Then cercato = NomiRaiAggiuntivi(cercato)
            If Not canali.TryGetValue(cercato, indirizzoCanale) Then Return New List(Of TvProgrammazione.VoceProgramma)()

            Dim risultato As New List(Of TvProgrammazione.VoceProgramma)
            For giorno As Integer = 0 To 1
                Dim data As DateTime = DateTime.Today.AddDays(giorno)
                Dim url As String = URL_RAI_GIORNO & indirizzoCanale & "/" & data.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) & ".json"
                Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(url).ConfigureAwait(False)
                If String.IsNullOrWhiteSpace(testo) Then
                    ' se manca il giorno di oggi la fonte non risponde; domani può non essere ancora pubblicato
                    If giorno = 0 Then Return Nothing
                    Continue For
                End If
                AggiungiGiornoRai(testo, risultato)
            Next
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.ScaricaRaiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Friend Shared Sub AggiungiGiornoRai(testo As String, risultato As List(Of TvProgrammazione.VoceProgramma))
        Try
            Dim radice As JObject = TvProgrammazione.AnalizzaJson(testo)
            Dim eventi As JArray = TryCast(radice("events"), JArray)
            If eventi Is Nothing Then Return

            For Each e As JToken In eventi
                Try
                    Dim data As String = TvProgrammazione.TestoDi(e("date"))
                    Dim ora As String = TvProgrammazione.TestoDi(e("hour"))
                    Dim inizio As DateTime
                    If Not DateTime.TryParseExact(data & " " & ora, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, inizio) Then Continue For

                    Dim durata As TimeSpan
                    If Not TimeSpan.TryParse(TvProgrammazione.TestoDi(e("duration")), CultureInfo.InvariantCulture, durata) OrElse durata <= TimeSpan.Zero Then durata = TimeSpan.FromMinutes(30)

                    Dim nome As String = TvProgrammazione.TestoDi(e("name"))
                    Dim programma As String = TvProgrammazione.TestoDi(e("title"))
                    Dim titolo As String = nome
                    If titolo.Length = 0 Then
                        titolo = programma
                    ElseIf programma.Length > 0 AndAlso Not String.Equals(programma, nome, StringComparison.CurrentCultureIgnoreCase) Then
                        titolo = programma & ": " & nome
                    End If
                    If titolo.Length = 0 Then Continue For

                    Dim v As New TvProgrammazione.VoceProgramma()
                    v.Inizio = inizio
                    v.Fine = inizio + durata
                    v.Titolo = titolo
                    v.Descrizione = TvProgrammazione.TestoDi(e("description"))
                    risultato.Add(v)
                Catch exEvento As Exception
                    _logger.ScriviLog("RadioProgrammazione.AggiungiGiornoRai evento: " & exEvento.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("RadioProgrammazione.AggiungiGiornoRai: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
