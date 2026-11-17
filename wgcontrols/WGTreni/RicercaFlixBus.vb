Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net.Http
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' ============================================================================================
' RicercaFlixBus - corse di FlixBus (e FlixTrain) dalle API pubbliche del sito di FlixBus, senza chiavi e senza browser.
' ============================================================================================
'
'   Le città si cercano con l'autocompletamento (global.api.flixbus.com/search/autocomplete/cities) e le corse con la ricerca per città
'   (search/service/v4/search): una sola richiesta dà tutte le corse del giorno, con fermate di partenza e arrivo, durata, cambi e prezzo.
'   Le corse diventano OffertaTrainline, così il menu le mostra insieme a quelle di Trainline; il link per acquistare porta al sito di FlixBus.
' ============================================================================================
Friend NotInheritable Class RicercaFlixBus

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _http As HttpClient = CreaHttp()

    Private Sub New()
    End Sub

    Private Shared Function CreaHttp() As HttpClient
        Dim c As New HttpClient()
        c.Timeout = TimeSpan.FromSeconds(25)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
        Return c
    End Function

    ''' <summary>L'identificativo della città di FlixBus che corrisponde al nome di una stazione (per esempio "Roma Termini" è Roma); Nothing se non c'è.</summary>
    Public Shared Async Function IdCittaAsync(nome As String) As Task(Of String)
        Try
            Dim tentativi As New List(Of String)()
            Dim pulito As String = System.Text.RegularExpressions.Regex.Replace(If(nome, ""), "\(.*?\)", "").Trim()
            If pulito <> "" Then tentativi.Add(pulito)
            Dim primaParola As String = If(pulito.Contains(" "), pulito.Substring(0, pulito.IndexOf(" "c)), "")
            If primaParola.Length >= 3 Then tentativi.Add(primaParola)
            For Each q As String In tentativi
                Dim testo As String = Await _http.GetStringAsync("https://global.api.flixbus.com/search/autocomplete/cities?q=" & Uri.EscapeDataString(q) & "&lang=it&country=it").ConfigureAwait(True)
                Dim citta As JArray = JArray.Parse(testo)
                Dim primoAltro As String = Nothing
                For Each c As JToken In citta
                    If Convert.ToString(c("is_flixbus_city")).ToLowerInvariant() <> "true" Then Continue For
                    If Convert.ToString(c("country")).ToLowerInvariant() = "it" Then Return Convert.ToString(c("id"))
                    If primoAltro Is Nothing Then primoAltro = Convert.ToString(c("id"))
                Next
                If primoAltro IsNot Nothing Then Return primoAltro
            Next
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog("RicercaFlixBus.IdCittaAsync: " & ex.GetType().Name & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Function UrlAcquisto(idDa As String, idA As String, giorno As DateTime, passeggeri As Integer) As String
        Return "https://shop.global.flixbus.com/search?departureCity=" & idDa & "&arrivalCity=" & idA & "&rideDate=" & giorno.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) &
               "&adult=" & Math.Max(1, passeggeri).ToString(CultureInfo.InvariantCulture) & "&_locale=it"
    End Function

    Private Shared Function Ora(t As JToken, nome As String) As DateTime
        Dim d As DateTimeOffset
        If t IsNot Nothing AndAlso DateTimeOffset.TryParse(Convert.ToString(t(nome)), CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Return d.DateTime
        Return DateTime.MinValue
    End Function

    ''' <summary>
    ''' Le corse del giorno di "dalle" tra due città di FlixBus, da quell'ora in poi. Nothing se il servizio non risponde o non c'è niente.
    ''' </summary>
    Public Shared Async Function CercaAsync(idDa As String, idA As String, dalle As DateTime, passeggeri As Integer) As Task(Of List(Of OffertaTrainline))
        Try
            Dim prodotti As String = "{""adult"":" & Math.Max(1, passeggeri).ToString(CultureInfo.InvariantCulture) & "}"
            Dim url As String = "https://global.api.flixbus.com/search/service/v4/search?from_city_id=" & idDa & "&to_city_id=" & idA &
                "&departure_date=" & dalle.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) & "&products=" & Uri.EscapeDataString(prodotti) &
                "&currency=EUR&locale=it&search_by=cities&include_after_midnight_rides=1"
            Dim testo As String = Await _http.GetStringAsync(url).ConfigureAwait(True)
            Dim radice As JObject = JObject.Parse(testo)
            Dim stazioni As JObject = TryCast(radice("stations"), JObject)
            Dim marchi As JObject = TryCast(radice("brands"), JObject)
            Dim viaggi As JArray = TryCast(radice("trips"), JArray)
            If viaggi Is Nothing OrElse viaggi.Count = 0 Then Return Nothing
            Dim risultati As JObject = TryCast(viaggi(0)("results"), JObject)
            If risultati Is Nothing Then Return Nothing

            Dim nomeStazione As Func(Of JToken, String) =
                Function(punto As JToken) As String
                    If stazioni Is Nothing OrElse punto Is Nothing Then Return ""
                    Dim s As JToken = stazioni(Convert.ToString(punto("station_id")))
                    Return If(s Is Nothing, "", Convert.ToString(s("name")))
                End Function

            Dim lista As New List(Of OffertaTrainline)()
            For Each p As JProperty In risultati.Properties()
                Dim r As JToken = p.Value
                Dim partenza As DateTime = Ora(r("departure"), "date")
                Dim arrivo As DateTime = Ora(r("arrival"), "date")
                If partenza = DateTime.MinValue OrElse arrivo = DateTime.MinValue Then Continue For
                If partenza < dalle OrElse partenza.Date <> dalle.Date Then Continue For
                Dim o As New OffertaTrainline()
                o.Partenza = partenza
                o.Arrivo = arrivo
                o.Durata = arrivo - partenza
                o.Passeggeri = Math.Max(1, passeggeri)
                o.Acquistabile = String.Equals(Convert.ToString(r("status")), "available", StringComparison.OrdinalIgnoreCase)
                Dim prezzo As JToken = r("price")
                Dim importo As Decimal
                If prezzo IsNot Nothing Then
                    Dim valore As String = Convert.ToString(If(prezzo("total_with_platform_fee"), prezzo("total")), CultureInfo.InvariantCulture)
                    If Decimal.TryParse(valore, NumberStyles.Number, CultureInfo.InvariantCulture, importo) Then o.Prezzo = importo
                End If
                Dim tratti As JArray = TryCast(r("legs"), JArray)
                Dim haTreno As Boolean = False
                Dim haPullman As Boolean = False
                Dim marchio As String = "FlixBus"
                If tratti IsNot Nothing Then
                    o.Cambi = Math.Max(0, tratti.Count - 1)
                    For Each leg As JToken In tratti
                        Dim t As New TrattoTrainline()
                        t.Da = nomeStazione(leg("departure"))
                        t.A = nomeStazione(leg("arrival"))
                        t.Partenza = Ora(leg("departure"), "date")
                        t.Arrivo = Ora(leg("arrival"), "date")
                        Dim treno As Boolean = String.Equals(Convert.ToString(leg("means_of_transport")), "train", StringComparison.OrdinalIgnoreCase)
                        t.Mezzo = If(treno, "Treno", "Pullman")
                        Dim idMarchio As String = Convert.ToString(leg("brand_id"))
                        Dim nomeMarchio As String = ""
                        If marchi IsNot Nothing AndAlso idMarchio <> "" AndAlso marchi(idMarchio) IsNot Nothing Then nomeMarchio = Convert.ToString(marchi(idMarchio)("name"))
                        If nomeMarchio = "" Then nomeMarchio = If(treno, "FlixTrain", "FlixBus")
                        t.Nome = nomeMarchio
                        marchio = nomeMarchio
                        If treno Then haTreno = True Else haPullman = True
                        o.Tratti.Add(t)
                    Next
                End If
                o.Mezzi = If(haTreno AndAlso haPullman, "Treno e pullman", If(haTreno, "Treno", "Pullman"))
                o.Operatore = marchio
                o.LinkAcquisto = UrlAcquisto(idDa, idA, dalle, passeggeri)
                lista.Add(o)
            Next
            lista.Sort(Function(x, y) x.Partenza.CompareTo(y.Partenza))
            Return If(lista.Count = 0, Nothing, lista)
        Catch ex As Exception
            _log.ScriviLog("RicercaFlixBus.CercaAsync: " & ex.GetType().Name & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

End Class
