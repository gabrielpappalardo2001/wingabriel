Imports System.Globalization
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' ============================================================================================
' RicercaTrainline - corse (treni, pullman, FlixBus), prezzi e operatori da Trainline, letti dal JSON
' ============================================================================================
'
' Come funziona
'   Si apre in un browser vero (la pagina nascosta di WinGabriel) il link dei risultati di Trainline. La pagina chiede da sola il
'   suo servizio JSON (/api/journey-search/, che dà insieme treni, pullman e FlixBus) e qui si legge la risposta che riceve: non si
'   tocca l'HTML della pagina, che cambia spesso, e non si fa nessuna richiesta fuori dal browser.
'
' Il JSON (data.journeySearch) è una raccolta di oggetti collegati fra loro per identificativo:
'   journeys        le corse: departAt, arriveAt, duration, legs (identificativi dei tratti), sections, unsellableReason se non si vende;
'   legs            i tratti: departAt, arriveAt, transportDesignation ("FR", "Italo"), vendorQuotaIdentifier ("FR 9620"),
'                   timetableId (il numero), transportMode e carrier (codici senza nome), vehicleAttributes;
'   sections        i gruppi di offerte di una corsa: alternatives (identificativi delle offerte);
'   alternatives    le offerte: price.amount, vendorInformation.name ("Trenitalia", "NTV"), flexibility.
' Le stazioni dei tratti sono solo codici: si conoscono i nomi della partenza e dell'arrivo (quelli cercati).
' Le date vanno lette come testo (DateParseHandling.None): se Newtonsoft le trasforma in date, cambiano formato e non si leggono più.
' Se la struttura cambia e non si legge nessuna corsa, la ricerca dà Nothing e il menu usa gli orari programmati, senza messaggi.
' ============================================================================================

Friend Class TrattoTrainline
    Public Da As String = ""
    Public A As String = ""
    Public Partenza As DateTime = DateTime.MinValue
    Public Arrivo As DateTime = DateTime.MinValue
    Public Mezzo As String = ""
    Public Nome As String = ""
    ''' <summary>Le caratteristiche del mezzo dette da Trainline, in minuscolo: highspeed, intercity, regional.</summary>
    Public Attributi As String = ""

    Public Function Descrizione() As String
        Dim sb As New StringBuilder()
        If Mezzo <> "" Then sb.Append(Mezzo)
        If Nome <> "" Then sb.Append(If(sb.Length > 0, " ", "") & Nome)
        If Partenza <> DateTime.MinValue Then sb.Append(If(sb.Length > 0, ", parte", "Parte") & If(Da <> "", " da " & Da, "") & " alle " & Partenza.ToString("HH:mm"))
        If Arrivo <> DateTime.MinValue Then sb.Append(", arriva" & If(A <> "", " a " & A, "") & " alle " & Arrivo.ToString("HH:mm"))
        Return sb.ToString().Trim()
    End Function
End Class

Friend Class OffertaTrainline
    Public Partenza As DateTime
    Public Arrivo As DateTime
    Public Durata As TimeSpan
    Public Cambi As Integer
    Public Operatore As String = ""
    Public Mezzi As String = ""
    Public Prezzo As Decimal = -1D
    Public Acquistabile As Boolean = True
    ' Se non è vuoto, l'indirizzo da aprire per acquistare questa corsa (per esempio il sito di FlixBus); altrimenti si usa Trainline.
    Public LinkAcquisto As String = ""
    ''' <summary>Per quanti passeggeri è il prezzo (il prezzo è il totale di tutti).</summary>
    Public Passeggeri As Integer = 1
    Public ReadOnly Tratti As New List(Of TrattoTrainline)()

    ''' <summary>
    ''' Vero se i treni della corsa sono del tipo scelto: tutti, solo alta velocità, senza regionali (alta velocità e Intercity) o solo
    ''' regionali. I tratti in pullman non contano.
    ''' </summary>
    Public Function CorrispondeTipo(tipo As TipoTreni) As Boolean
        If tipo = TipoTreni.Tutti Then Return True
        For Each t As TrattoTrainline In Tratti
            If t.Mezzo <> "Treno" Then Continue For
            Dim veloce As Boolean = t.Attributi.Contains("highspeed")
            Dim intercity As Boolean = t.Attributi.Contains("intercity")
            Select Case tipo
                Case TipoTreni.AltaVelocita
                    If Not veloce Then Return False
                Case TipoTreni.Veloci
                    If Not veloce AndAlso Not intercity Then Return False
                Case TipoTreni.Regionali
                    If veloce OrElse intercity Then Return False
            End Select
        Next
        Return True
    End Function

    Public Function Descrizione() As String
        Dim sb As New StringBuilder()
        Dim nomi As New List(Of String)()
        For Each t As TrattoTrainline In Tratti
            If t.Nome <> "" Then nomi.Add(t.Nome)
        Next
        If nomi.Count > 0 Then
            sb.Append(String.Join(" e ", nomi) & ", ")
        ElseIf Mezzi <> "" Then
            sb.Append(Mezzi & ", ")
        End If
        sb.Append("parte alle " & Partenza.ToString("HH:mm") & ", arriva alle " & Arrivo.ToString("HH:mm"))
        If Arrivo.Date > Partenza.Date Then sb.Append(" del giorno dopo")
        Dim minuti As Integer = CInt(Math.Round(Durata.TotalMinutes))
        Dim h As Integer = minuti \ 60
        Dim m As Integer = minuti Mod 60
        sb.Append(", " & If(h = 0, "", h & If(h = 1, " ora", " ore") & If(m > 0, " e ", "")) & If(m > 0 OrElse h = 0, m & If(m = 1, " minuto", " minuti"), ""))
        sb.Append(If(Cambi = 0, ", senza cambi", If(Cambi = 1, ", un cambio", ", " & Cambi & " cambi")))
        ' L'operatore si dice solo se non è già nel nome del treno (per esempio "Italo 9974").
        Dim giaDetto As Boolean = False
        For Each n As String In nomi
            If Operatore <> "" AndAlso n.IndexOf(Operatore, StringComparison.OrdinalIgnoreCase) >= 0 Then giaDetto = True
        Next
        If Operatore <> "" AndAlso Not giaDetto Then sb.Append(", " & Operatore)
        If Not Acquistabile Then
            sb.Append(", non acquistabile")
        ElseIf Prezzo >= 0D Then
            sb.Append(", da " & Prezzo.ToString("0.00", CultureInfo.GetCultureInfo("it-IT")) & " euro" & If(Passeggeri > 1, " in tutto per " & Passeggeri & " passeggeri", ""))
        End If
        ' La prima lettera è maiuscola.
        Dim testo As String = sb.ToString()
        Return Char.ToUpper(testo(0)) & testo.Substring(1)
    End Function
End Class

Friend NotInheritable Class RicercaTrainline

    Private Shared ReadOnly _log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>Il link dei risultati di Trainline per due luoghi (codici Trainline), un giorno e un'ora di partenza.</summary>
    Public Shared Function UrlRisultati(origine As String, destinazione As String, dopoLe As DateTime, Optional passeggeri As Integer = 1) As String
        Dim ora As String = dopoLe.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
        Return "https://www.thetrainline.com/book/results?origin=" & Uri.EscapeDataString(origine) &
               "&destination=" & Uri.EscapeDataString(destinazione) &
               "&outwardDate=" & Uri.EscapeDataString(ora) &
               "&outwardDateType=departAfter&journeySearchType=single" & Adulti(passeggeri) & "&lang=it"
    End Function

    ''' <summary>Un adulto per ogni passeggero: Trainline vuole la data di nascita di ciascuno e il prezzo è il totale di tutti.</summary>
    Private Shared Function Adulti(passeggeri As Integer) As String
        Dim sb As New StringBuilder()
        For i As Integer = 1 To Math.Max(1, Math.Min(passeggeri, 9))
            sb.Append("&passengers%5B%5D=1996-01-01")
        Next
        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Le corse (treni, pullman, FlixBus) con prezzi e operatori da Trainline, o Nothing se non si sono potute leggere. Si aspetta la
    ''' risposta della pagina in un browser nascosto, quindi può durare una decina di secondi.
    ''' </summary>
    Public Shared Async Function CercaAsync(origine As String, destinazione As String, partenza As DateTime, nomeDa As String, nomeA As String, Optional passeggeri As Integer = 1) As Task(Of List(Of OffertaTrainline))
        Try
            Dim url As String = UrlRisultati(origine, destinazione, partenza.AddMinutes(-1), passeggeri)
            Dim json As String = Await Task.Run(
                Function() As String
                    Dim pagina As WGWebView = WGWebView.GetInstance()
                    If pagina Is Nothing Then Return ""
                    Return pagina.OttieniRispostaRete(url, "/api/journey-search/", 30000)
                End Function).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(json) Then Return Nothing
            Dim offerte As List(Of OffertaTrainline) = LeggiRisposta(json, nomeDa, nomeA)
            For Each o As OffertaTrainline In offerte
                o.Passeggeri = Math.Max(1, passeggeri)
            Next
            Return If(offerte.Count = 0, Nothing, offerte)
        Catch ex As Exception
            _log.ScriviLog("RicercaTrainline.CercaAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Shared Function Testo(t As JToken, nome As String) As String
        Try
            If t Is Nothing OrElse t.Type <> JTokenType.Object Then Return ""
            Dim v As JToken = t(nome)
            If v Is Nothing OrElse v.Type = JTokenType.Null OrElse v.Type = JTokenType.Object OrElse v.Type = JTokenType.Array Then Return ""
            ' I numeri si leggono con il punto decimale, qualunque sia la lingua del computer.
            Dim valore As JValue = TryCast(v, JValue)
            If valore IsNot Nothing Then Return valore.ToString(CultureInfo.InvariantCulture).Trim()
            Return v.ToString().Trim()
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Tutte le stringhe contenute in un oggetto, a qualsiasi profondità.</summary>
    Private Shared Function Stringhe(t As JToken) As List(Of String)
        Dim elenco As New List(Of String)()
        If t Is Nothing Then Return elenco
        If t.Type = JTokenType.String Then elenco.Add(t.ToString())
        Dim contenitore As JContainer = TryCast(t, JContainer)
        If contenitore IsNot Nothing Then
            For Each d As JToken In contenitore.Descendants()
                If d.Type = JTokenType.String Then elenco.Add(d.ToString())
            Next
        End If
        Return elenco
    End Function

    Private Shared Function NomeOperatore(nome As String) As String
        If String.Equals(nome, "NTV", StringComparison.OrdinalIgnoreCase) Then Return "Italo"
        If String.Equals(nome, "flixbus", StringComparison.OrdinalIgnoreCase) Then Return "FlixBus"
        Return nome
    End Function

    Private Shared Function LeggiOra(t As JToken, nome As String) As DateTime
        Dim v As String = Testo(t, nome)
        Dim d As DateTimeOffset
        If v <> "" AndAlso DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Return d.DateTime
        Return DateTime.MinValue
    End Function

    ''' <summary>Il nome del tratto: "Frecciarossa 9620", "Italo 9974", "FlixBus 1234".</summary>
    Private Shared Function NomeTratto(leg As JToken) As String
        Dim quota As String = Testo(leg, "vendorQuotaIdentifier")
        ' Italo: il tratto si presenta come "Italo", "NTV" o "ITA" (sigla della compagnia): il nome è sempre "Italo" e il numero del treno.
        Dim designazione As String = Testo(leg, "transportDesignation")
        Dim mq As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(quota, "^([A-Za-z]+)\s+(\S+)$")
        Dim siglaQuota As String = If(mq.Success, mq.Groups(1).Value, "")
        If designazione.IndexOf("italo", StringComparison.OrdinalIgnoreCase) >= 0 OrElse String.Equals(siglaQuota, "NTV", StringComparison.OrdinalIgnoreCase) OrElse
           String.Equals(siglaQuota, "ITALO", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(siglaQuota, "ITA", StringComparison.OrdinalIgnoreCase) Then
            Dim numeroItalo As String = If(mq.Success, mq.Groups(2).Value, Testo(leg, "timetableId"))
            Return ("Italo " & numeroItalo).Trim()
        End If
        Dim m As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(quota, "^([A-Za-z]+)\s+(\S+)$")
        If m.Success Then
            Dim sigla As String = m.Groups(1).Value
            Dim nome As String = OrariTreni.NomeCategoriaPubblico(sigla.ToUpperInvariant())
            If String.Equals(nome, sigla, StringComparison.OrdinalIgnoreCase) Then nome = sigla
            Return nome & " " & m.Groups(2).Value
        End If
        If quota <> "" Then Return quota
        Return (Testo(leg, "transportDesignation") & " " & Testo(leg, "timetableId")).Trim()
    End Function

    Friend Shared Function LeggiRisposta(json As String, nomeDa As String, nomeA As String) As List(Of OffertaTrainline)
        Try
            ' Le date restano testo: altrimenti Newtonsoft le trasforma in date e il loro testo cambia formato.
            Dim radice As JObject = Newtonsoft.Json.JsonConvert.DeserializeObject(Of JObject)(json, New Newtonsoft.Json.JsonSerializerSettings With {.DateParseHandling = Newtonsoft.Json.DateParseHandling.None})
            Dim ricerca As JToken = radice.SelectToken("data.journeySearch")
            If ricerca Is Nothing Then ricerca = radice
            Dim corse As JObject = TryCast(ricerca("journeys"), JObject)
            If corse Is Nothing Then Return New List(Of OffertaTrainline)()
            Dim alternative As JObject = TryCast(ricerca("alternatives"), JObject)
            Dim sezioni As JObject = TryCast(ricerca("sections"), JObject)
            Dim tratti As JObject = TryCast(ricerca("legs"), JObject)

            ' I mezzi di trasporto hanno solo un codice: sono pullman quelli dei tratti che si presentano come bus o FlixBus.
            Dim modiPullman As New HashSet(Of String)()
            If tratti IsNot Nothing Then
                For Each lp As JProperty In tratti.Properties()
                    Dim descrizione As String = (Testo(lp.Value, "transportDesignation") & " " & Testo(lp.Value, "vendorQuotaIdentifier") & " " & String.Join(" ", Stringhe(lp.Value("vehicleAttributes")))).ToLowerInvariant()
                    If descrizione.Contains("bus") OrElse descrizione.Contains("flix") OrElse descrizione.Contains("pullman") OrElse descrizione.Contains("coach") Then
                        modiPullman.Add(Testo(lp.Value, "transportMode"))
                    End If
                Next
            End If

            Dim risultato As New List(Of OffertaTrainline)()
            For Each c As JProperty In corse.Properties()
                Dim o As New OffertaTrainline()
                Dim partenza As DateTime = LeggiOra(c.Value, "departAt")
                Dim arrivo As DateTime = LeggiOra(c.Value, "arriveAt")
                If partenza = DateTime.MinValue OrElse arrivo = DateTime.MinValue Then Continue For
                o.Partenza = partenza
                o.Arrivo = arrivo
                Try
                    o.Durata = Xml.XmlConvert.ToTimeSpan(Testo(c.Value, "duration"))
                Catch
                    o.Durata = arrivo - partenza
                End Try
                Dim elencoTratti As JArray = TryCast(c.Value("legs"), JArray)
                o.Cambi = If(elencoTratti Is Nothing OrElse elencoTratti.Count = 0, 0, elencoTratti.Count - 1)
                o.Acquistabile = c.Value("unsellableReason") Is Nothing OrElse c.Value("unsellableReason").Type = JTokenType.Null

                ' Il dettaglio dei tratti. Le stazioni intermedie nel JSON hanno solo un codice: si conoscono i nomi della partenza e dell'arrivo.
                Dim haTreno As Boolean = False
                Dim haPullman As Boolean = False
                If elencoTratti IsNot Nothing AndAlso tratti IsNot Nothing Then
                    For i As Integer = 0 To elencoTratti.Count - 1
                        Dim leg As JToken = tratti(elencoTratti(i).ToString())
                        If leg Is Nothing Then Continue For
                        Dim t As New TrattoTrainline()
                        If i = 0 Then t.Da = nomeDa
                        If i = elencoTratti.Count - 1 Then t.A = nomeA
                        t.Partenza = LeggiOra(leg, "departAt")
                        t.Arrivo = LeggiOra(leg, "arriveAt")
                        t.Mezzo = If(modiPullman.Contains(Testo(leg, "transportMode")), "Pullman", "Treno")
                        t.Nome = NomeTratto(leg)
                        t.Attributi = String.Join(" ", Stringhe(leg("vehicleAttributes"))).ToLowerInvariant()
                        If t.Mezzo = "Pullman" Then haPullman = True Else haTreno = True
                        o.Tratti.Add(t)
                    Next
                End If
                o.Mezzi = If(haTreno AndAlso haPullman, "Treno e pullman", If(haPullman, "Pullman", If(haTreno, "Treno", "")))

                ' Le offerte della corsa: quelle elencate nelle sue sezioni.
                Dim idOfferte As New List(Of String)()
                Dim elencoSezioni As JArray = TryCast(c.Value("sections"), JArray)
                If elencoSezioni IsNot Nothing AndAlso sezioni IsNot Nothing Then
                    For Each s As JToken In elencoSezioni
                        Dim sezione As JToken = sezioni(s.ToString())
                        Dim lista As JArray = If(sezione Is Nothing, Nothing, TryCast(sezione("alternatives"), JArray))
                        If lista IsNot Nothing Then
                            For Each a As JToken In lista
                                idOfferte.Add(a.ToString())
                            Next
                        End If
                    Next
                End If
                If alternative IsNot Nothing Then
                    For Each idOfferta As String In idOfferte
                        Dim alt As JToken = alternative(idOfferta)
                        If alt Is Nothing Then Continue For
                        Dim importo As Decimal
                        If Not Decimal.TryParse(Testo(alt("price"), "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, importo) Then Continue For
                        If o.Prezzo < 0D OrElse importo < o.Prezzo Then
                            o.Prezzo = importo
                            o.Operatore = NomeOperatore(Testo(alt("vendorInformation"), "name"))
                        End If
                    Next
                End If
                ' L'operatore: dai treni della corsa (un Italo è Italo anche se l'offerta più economica la vende un altro), non solo dal venditore.
                Dim nTreni As Integer = 0
                Dim nItalo As Integer = 0
                For Each tr As TrattoTrainline In o.Tratti
                    If tr.Mezzo <> "Treno" Then Continue For
                    nTreni += 1
                    If tr.Nome.StartsWith("Italo", StringComparison.OrdinalIgnoreCase) Then nItalo += 1
                Next
                If nItalo > 0 AndAlso nItalo = nTreni Then
                    o.Operatore = "Italo"
                ElseIf nItalo > 0 AndAlso o.Operatore.IndexOf("Italo", StringComparison.OrdinalIgnoreCase) < 0 Then
                    o.Operatore = "Italo e " & If(o.Operatore = "", "altri", o.Operatore)
                End If
                risultato.Add(o)
            Next
            risultato.Sort(Function(x, y) x.Partenza.CompareTo(y.Partenza))
            Return risultato
        Catch ex As Exception
            _log.ScriviLog("RicercaTrainline.LeggiRisposta: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of OffertaTrainline)()
        End Try
    End Function

End Class
