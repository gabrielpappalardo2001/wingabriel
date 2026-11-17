Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' ============================================================================================
' TempoRealeTreni - partenze, arrivi, binari e ritardi in tempo reale
' ============================================================================================
'
' Fonti
'   Trenitalia (e i treni regionali di tutta Italia): i servizi di ViaggiaTreno, aperti ma non
'   documentati. Possono cambiare senza preavviso: ogni errore si registra e all'utente arriva
'   un messaggio semplice.
'   Italo: i servizi del sito Italo in viaggio. Danno il tabellone di ogni stazione servita da
'   Italo e il percorso di un treno in marcia.
' ============================================================================================

Friend Class ServizioNonDisponibileException
    Inherits Exception

    Public Sub New(fonte As String, inner As Exception)
        MyBase.New("Servizio " & fonte & " non disponibile", inner)
        Me.Fonte = fonte
    End Sub

    Public ReadOnly Fonte As String
End Class

''' <summary>Una stazione scelta dall'utente: per ogni fonte ha il suo codice, se la fonte la conosce.</summary>
Friend Class StazioneScelta
    Public Nome As String = ""
    Public CodiceTrenitalia As String = ""
    Public CodiceItalo As String = ""
End Class

''' <summary>Una riga del tabellone di una stazione.</summary>
Friend Class VoceTabellone
    Public Fonte As String = ""
    Public Categoria As String = ""
    Public Numero As String = ""
    Public Localita As String = ""
    Public Orario As DateTime
    Public OrarioPrevisto As String = ""
    Public Ritardo As Integer
    Public Binario As String = ""
    Public BinarioConfermato As Boolean
    Public Stato As String = ""
    Public Percorso As String = ""

    Public Function Descrizione(partenza As Boolean) As String
        Dim sb As New StringBuilder()
        sb.Append(Orario.ToString("HH:mm"))
        If OrarioPrevisto <> "" AndAlso OrarioPrevisto <> Orario.ToString("HH:mm") Then sb.Append(", previsto " & OrarioPrevisto)
        sb.Append(", " & Categoria)
        If Numero <> "" Then sb.Append(" " & Numero)
        If Localita <> "" Then sb.Append(If(partenza, " per ", " da ") & Localita)
        sb.Append(If(Binario <> "", ", binario " & Binario, ", binario non ancora assegnato"))
        If Stato <> "" Then sb.Append(", " & Stato)
        Return sb.ToString()
    End Function
End Class

Friend Class Tabellone
    Public ReadOnly Voci As New List(Of VoceTabellone)()
    Public ReadOnly Avvisi As New List(Of String)()
End Class

Friend Class FermataDettaglio
    Public Nome As String = ""
    Public Arrivo As String = ""
    Public ArrivoPrevisto As String = ""
    Public Partenza As String = ""
    Public PartenzaPrevista As String = ""
    Public Binario As String = ""
    Public Ritardo As Integer
    Public Passata As Boolean
    ''' <summary>Un orario solo, quando la fonte non dice se è di arrivo o di partenza.</summary>
    Public Orario As String = ""

    Public Function Descrizione() As String
        Dim sb As New StringBuilder(Nome)
        Dim parti As New List(Of String)()
        If Orario <> "" Then parti.Add("alle " & Orario)
        If Arrivo <> "" Then parti.Add("arrivo " & Arrivo & If(ArrivoPrevisto <> "" AndAlso ArrivoPrevisto <> Arrivo, ", previsto " & ArrivoPrevisto, ""))
        If Partenza <> "" Then parti.Add("partenza " & Partenza & If(PartenzaPrevista <> "" AndAlso PartenzaPrevista <> Partenza, ", previsto " & PartenzaPrevista, ""))
        If Binario <> "" Then parti.Add("binario " & Binario)
        If Passata Then parti.Add("già passato")
        If parti.Count > 0 Then sb.Append(": " & String.Join(", ", parti))
        Return sb.ToString()
    End Function
End Class

Friend Class DettaglioTreno
    Public Fonte As String = ""
    Public Etichetta As String = ""
    Public Origine As String = ""
    Public Destinazione As String = ""
    Public Stato As String = ""
    Public ReadOnly Fermate As New List(Of FermataDettaglio)()
End Class

''' <summary>Un treno trovato per numero, prima di sceglierne uno quando ce n'è più d'uno.</summary>
Friend Class TrenoTrovato
    Public Descrizione As String = ""
    Public CodiceOrigine As String = ""
    Public Numero As String = ""
    Public Millisecondi As Long
End Class

Friend NotInheritable Class TempoRealeTreni

    Private Shared ReadOnly _log As New Logger()
    Private Shared _client As HttpClient
    Private Shared ReadOnly _fuso As TimeZoneInfo = TrovaFuso()

    Private Const BaseTrenitalia As String = "http://www.viaggiatreno.it/infomobilitamobile/resteasy/viaggiatreno/"
    Private Const BaseItalo As String = "https://italoinviaggio.italotreno.com/api/"

    Private Shared Function TrovaFuso() As TimeZoneInfo
        Try
            Return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")
        Catch
            Return TimeZoneInfo.Local
        End Try
    End Function

    Private Shared Function Client() As HttpClient
        If _client Is Nothing Then
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Dim c As New HttpClient()
            c.Timeout = TimeSpan.FromSeconds(20)
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WinGabriel-Treni/1.0)")
            _client = c
        End If
        Return _client
    End Function

    Private Shared Function Adesso() As DateTime
        Return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _fuso)
    End Function

    Friend Shared Function DaMillisecondi(ms As Long) As DateTime
        Return TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, _fuso)
    End Function

    ''' <summary>Legge una pagina con un nuovo tentativo. Se il servizio non risponde lancia ServizioNonDisponibileException.</summary>
    Private Shared Async Function LeggiAsync(url As String, fonte As String) As Task(Of String)
        Dim ultimo As Exception = Nothing
        For tentativo As Integer = 1 To 2
            Try
                Using risposta As HttpResponseMessage = Await Client().GetAsync(url).ConfigureAwait(False)
                    If risposta.StatusCode = HttpStatusCode.NoContent OrElse risposta.StatusCode = HttpStatusCode.NotFound Then Return ""
                    risposta.EnsureSuccessStatusCode()
                    Return Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
                End Using
            Catch ex As Exception
                ultimo = ex
                _log.ScriviLog("TempoRealeTreni.Leggi " & fonte & " (tentativo " & tentativo & "): " & ex.Message)
            End Try
        Next
        Throw New ServizioNonDisponibileException(fonte, ultimo)
    End Function

    Private Shared Function Testo(o As JToken, nome As String) As String
        Try
            If o Is Nothing Then Return ""
            Dim t As JToken = o(nome)
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return ""
            Return WebUtility.HtmlDecode(t.ToString()).Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function SiglaCategoria(o As JToken) As String
        Dim s As String = Testo(o, "categoria")
        If s = "" Then s = Testo(o, "categoriaDescrizione")
        If s = "" Then
            Dim comp As String = Testo(o, "compNumeroTreno")
            If comp <> "" Then s = comp.Split(" "c)(0)
        End If
        Return s.Trim()
    End Function

    Private Shared Function NumeroIntero(o As JToken, nome As String, ByRef presente As Boolean) As Integer
        presente = False
        Try
            If o Is Nothing Then Return 0
            Dim t As JToken = o(nome)
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return 0
            Dim v As Integer
            If Integer.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then
                presente = True
                Return v
            End If
        Catch
        End Try
        Return 0
    End Function

    Private Shared Function Millisecondi(o As JToken, nome As String) As Long
        Try
            If o Is Nothing Then Return 0
            Dim t As JToken = o(nome)
            If t Is Nothing OrElse t.Type = JTokenType.Null Then Return 0
            Dim v As Long
            If Long.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then Return v
        Catch
        End Try
        Return 0
    End Function

    Private Shared Function Ora(ms As Long) As String
        If ms <= 0 Then Return ""
        Return DaMillisecondi(ms).ToString("HH:mm")
    End Function

    Private Shared Function NomeBello(nome As String) As String
        If String.IsNullOrWhiteSpace(nome) Then Return ""
        Dim s As String = nome.Trim()
        Dim lettere As Integer = 0
        Dim maiuscole As Integer = 0
        For Each c As Char In s
            If Char.IsLetter(c) Then
                lettere += 1
                If Char.IsUpper(c) Then maiuscole += 1
            End If
        Next
        If lettere = 0 OrElse maiuscole < lettere * 0.8 Then Return s
        Return CultureInfo.GetCultureInfo("it-IT").TextInfo.ToTitleCase(s.ToLowerInvariant())
    End Function

    Private Shared Function TestoRitardo(ritardo As Integer) As String
        If ritardo > 0 Then Return "ritardo di " & ritardo & If(ritardo = 1, " minuto", " minuti")
        Return "in orario"
    End Function

#Region "Stazioni"

    ''' <summary>Le stazioni di Italo, con i codici del suo servizio.</summary>
    Private Shared ReadOnly StazioniItalo As String(,) = {
        {"AGR", "Agropoli"}, {"FF_", "Ancona"}, {"BAC", "Bari Centrale"}, {"BLT", "Barletta"}, {"BEN", "Benevento"},
        {"BGM", "Bergamo"}, {"BIG", "Bisceglie"}, {"BC_", "Bologna Centrale"}, {"BLZ", "Bolzano"}, {"BSC", "Brescia"},
        {"CEA", "Caserta"}, {"CON", "Conegliano"}, {"DSG", "Desenzano del Garda"}, {"F__", "Ferrara"},
        {"SMN", "Firenze Santa Maria Novella"}, {"FG_", "Foggia"}, {"G__", "Genova Piazza Principe"}, {"GB_", "Genova Brignole"},
        {"LON", "Lamezia Terme Centrale"}, {"LTL", "Latisana Lignano Bibione"}, {"MRT", "Maratea"}, {"MC_", "Milano Centrale"},
        {"RRO", "Milano Rho Fiera"}, {"RG_", "Milano Rogoredo"}, {"ML_", "Molfetta"}, {"MNF", "Monfalcone"},
        {"NAF", "Napoli Afragola"}, {"NAC", "Napoli Centrale"}, {"PD_", "Padova"}, {"PAR", "Paola"}, {"PY_", "Pesaro"},
        {"PSY", "Peschiera del Garda"}, {"PNE", "Pordenone"}, {"PGR", "Portogruaro Caorle"},
        {"RCE", "Reggio di Calabria Centrale"}, {"AAV", "Reggio Emilia AV Mediopadana"}, {"RO_", "Riccione"}, {"J__", "Rimini"},
        {"RMT", "Roma Termini"}, {"RTB", "Roma Tiburtina"}, {"RUT", "Rosarno"}, {"RVR", "Rovereto"}, {"R__", "Rovigo"},
        {"SAL", "Salerno"}, {"SRI", "Sapri"}, {"SDC", "Scalea"}, {"TOP", "Torino Porta Nuova"}, {"OUE", "Torino Porta Susa"},
        {"TR_", "Trani"}, {"TCN", "Trento"}, {"TVC", "Treviso Centrale"}, {"TSC", "Trieste Centrale"}, {"UDN", "Udine"},
        {"VLH", "Vallo della Lucania"}, {"VEM", "Venezia Mestre"}, {"VSL", "Venezia Santa Lucia"}, {"VPN", "Verona Porta Nuova"},
        {"VIP", "Vibo Pizzo"}, {"VIC", "Vicenza"}, {"VSG", "Villa San Giovanni"}
    }

    ''' <summary>Vero se due nomi di stazione sono la stessa stazione (le abbreviazioni come "S." valgono "San" o "Santa").</summary>
    Private Shared Function StessaStazione(a As String, b As String) As Boolean
        Dim pa As String() = OrariTreni.Normalizza(a).Split(" "c)
        Dim pb As String() = OrariTreni.Normalizza(b).Split(" "c)
        If pa.Length <> pb.Length Then Return False
        For i As Integer = 0 To pa.Length - 1
            If Not (pa(i).StartsWith(pb(i), StringComparison.Ordinal) OrElse pb(i).StartsWith(pa(i), StringComparison.Ordinal)) Then Return False
        Next
        Return True
    End Function

    Private Shared Function CodiceItaloPer(nome As String) As String
        For i As Integer = 0 To StazioniItalo.GetLength(0) - 1
            If StessaStazione(nome, StazioniItalo(i, 1)) Then Return StazioniItalo(i, 0)
        Next
        Return ""
    End Function

    ''' <summary>Le stazioni che corrispondono al testo, unendo quelle di Trenitalia e di Italo che sono la stessa.</summary>
    Public Shared Async Function CercaStazioniAsync(testoCercato As String) As Task(Of List(Of StazioneScelta))
        Try
            Dim risultato As New List(Of StazioneScelta)()
            Dim q As String = OrariTreni.Normalizza(testoCercato)
            If q = "" Then Return risultato
            Dim parole As String() = q.Split(" "c)

            Dim errore As ServizioNonDisponibileException = Nothing
            Try
                Dim corpo As String = Await LeggiAsync(BaseTrenitalia & "autocompletaStazione/" & Uri.EscapeDataString(testoCercato.Trim()), "Trenitalia").ConfigureAwait(False)
                For Each riga As String In corpo.Split({vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                    Dim p As Integer = riga.LastIndexOf("|"c)
                    If p <= 0 Then Continue For
                    Dim s As New StazioneScelta()
                    s.Nome = NomeBello(riga.Substring(0, p).Trim())
                    s.CodiceTrenitalia = riga.Substring(p + 1).Trim()
                    s.CodiceItalo = CodiceItaloPer(s.Nome)
                    risultato.Add(s)
                    If risultato.Count >= 40 Then Exit For
                Next
            Catch ex As ServizioNonDisponibileException
                errore = ex
            End Try

            ' Le stazioni di Italo che Trenitalia non ha dato.
            For i As Integer = 0 To StazioniItalo.GetLength(0) - 1
                Dim nome As String = StazioniItalo(i, 1)
                If OrariTreni.Corrispondenza(OrariTreni.Normalizza(nome), q, parole) <= 0 Then Continue For
                Dim presente As Boolean = False
                For Each s As StazioneScelta In risultato
                    If s.CodiceItalo = StazioniItalo(i, 0) Then
                        presente = True
                        Exit For
                    End If
                Next
                If presente Then Continue For
                Dim n As New StazioneScelta()
                n.Nome = nome
                n.CodiceItalo = StazioniItalo(i, 0)
                risultato.Add(n)
            Next
            If risultato.Count = 0 AndAlso errore IsNot Nothing Then Throw errore
            Return risultato
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.CercaStazioniAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

#End Region

#Region "Trainline"

    ''' <summary>Un luogo di Trainline: una stazione, una fermata di pullman o tutte le stazioni di una città.</summary>
    Friend Class LuogoTrainline
        Public Nome As String = ""
        Public Codice As String = ""
        Public Tipo As String = ""
        Public Paese As String = ""

        Public Function Descrizione() As String
            Dim d As String = Nome
            If Tipo = "stationGroup" OrElse Tipo = "city" Then d &= ", tutte le stazioni"
            If Paese <> "" AndAlso Paese <> "IT" Then d &= ", " & Paese
            Return d
        End Function
    End Class

    ''' <summary>I luoghi che Trainline conosce con quel nome (stazioni, fermate di pullman, città), i più probabili per primi.</summary>
    Public Shared Async Function CercaLuoghiTrainlineAsync(nome As String) As Task(Of List(Of LuogoTrainline))
        Try
            Dim risultato As New List(Of LuogoTrainline)()
            If String.IsNullOrWhiteSpace(nome) Then Return risultato
            Dim corpo As String = Await LeggiAsync("https://www.thetrainline.com/api/locations-search/v2/search?searchTerm=" & Uri.EscapeDataString(nome.Trim()) & "&locale=it-IT", "Trainline").ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(corpo) Then Return risultato
            Dim lista As JToken = JObject.Parse(corpo)("searchLocations")
            If lista Is Nothing OrElse lista.Type <> JTokenType.Array Then Return risultato
            For Each x As JToken In lista
                Dim l As New LuogoTrainline()
                l.Codice = Testo(x, "code")
                l.Nome = Testo(x, "name")
                l.Tipo = Testo(x, "locationType")
                l.Paese = Testo(x, "countryCode")
                If l.Codice = "" OrElse l.Nome = "" Then Continue For
                risultato.Add(l)
                If risultato.Count >= 15 Then Exit For
            Next
            Return risultato
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.CercaLuoghiTrainlineAsync: " & ex.Message)
            Return New List(Of LuogoTrainline)()
        End Try
    End Function

    ''' <summary>
    ''' Il codice che Trainline usa per una stazione (per esempio "urn:trainline:generic:loc:8544"), o una stringa vuota se non lo trova.
    ''' Trainline vende i biglietti di Trenitalia, Italo e Trenord e accetta un link ai risultati già impostati.
    ''' </summary>
    Public Shared Async Function CodiceTrainlineAsync(nome As String) As Task(Of String)
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim corpo As String = Await LeggiAsync("https://www.thetrainline.com/api/locations-search/v2/search?searchTerm=" & Uri.EscapeDataString(nome.Trim()) & "&locale=it-IT", "Trainline").ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(corpo) Then Return ""
            Dim radice As JObject = JObject.Parse(corpo)
            Dim lista As JToken = radice("searchLocations")
            If lista Is Nothing OrElse lista.Type <> JTokenType.Array Then Return ""
            Dim primo As String = ""
            For Each x As JToken In lista
                Dim codice As String = Testo(x, "code")
                If codice = "" Then Continue For
                If Testo(x, "countryCode") <> "IT" Then Continue For
                If Testo(x, "locationType") = "station" Then Return codice
                If primo = "" Then primo = codice
            Next
            Return primo
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.CodiceTrainlineAsync: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Tabellone"

    Private Shared Function DataPerServizio() As String
        Dim ora As DateTime = Adesso()
        Dim scarto As TimeSpan = _fuso.GetUtcOffset(ora)
        Dim segno As String = If(scarto < TimeSpan.Zero, "-", "+")
        Dim a As TimeSpan = scarto.Duration()
        Dim t As String = ora.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture) & " GMT" & segno & a.Hours.ToString("00") & a.Minutes.ToString("00")
        Return t.Replace(" ", "%20")
    End Function

    Private Shared Function DataVicina(ora As String) As DateTime
        ' L'orario è solo ore e minuti: si sceglie il giorno più vicino all'adesso.
        Dim adesso As DateTime = TempoRealeTreni.Adesso()
        Dim p As String() = ora.Split(":"c)
        Dim h As Integer
        Dim m As Integer
        If p.Length < 2 OrElse Not Integer.TryParse(p(0), h) OrElse Not Integer.TryParse(p(1), m) Then Return adesso
        Dim d As DateTime = adesso.Date.AddHours(h).AddMinutes(m)
        If d < adesso.AddHours(-6) Then d = d.AddDays(1)
        If d > adesso.AddHours(18) Then d = d.AddDays(-1)
        Return d
    End Function

    Private Shared Async Function TabelloneTrenitaliaAsync(codice As String, partenze As Boolean) As Task(Of List(Of VoceTabellone))
        Dim corpo As String = Await LeggiAsync(BaseTrenitalia & If(partenze, "partenze/", "arrivi/") & Uri.EscapeDataString(codice) & "/" & DataPerServizio(), "Trenitalia").ConfigureAwait(False)
        Dim lista As New List(Of VoceTabellone)()
        If String.IsNullOrWhiteSpace(corpo) Then Return lista
        Dim voci As JArray = JArray.Parse(corpo)
        For Each o As JToken In voci
            Dim v As New VoceTabellone()
            v.Fonte = "Trenitalia"
            Dim sigla As String = SiglaCategoria(o)
            v.Categoria = If(sigla = "", "Treno", OrariTreni.NomeCategoriaPubblico(sigla))
            v.Numero = Testo(o, "numeroTreno")
            Dim ms As Long
            If partenze Then
                v.Localita = NomeBello(Testo(o, "destinazione"))
                ms = Millisecondi(o, "orarioPartenza")
                If ms = 0 Then ms = Millisecondi(o, "dataPartenzaTreno")
            Else
                v.Localita = NomeBello(Testo(o, "origine"))
                ms = Millisecondi(o, "orarioArrivo")
                If ms = 0 Then ms = Millisecondi(o, "dataPartenzaTreno")
            End If
            If ms = 0 Then Continue For
            v.Orario = DaMillisecondi(ms)
            Dim presente As Boolean
            Dim ritardo As Integer = NumeroIntero(o, "ritardo", presente)
            If presente Then
                v.Ritardo = ritardo
                If ritardo > 0 Then v.OrarioPrevisto = v.Orario.AddMinutes(ritardo).ToString("HH:mm")
            End If
            Dim effettivo As String = Testo(o, If(partenze, "binarioEffettivoPartenzaDescrizione", "binarioEffettivoArrivoDescrizione"))
            Dim programmato As String = Testo(o, If(partenze, "binarioProgrammatoPartenzaDescrizione", "binarioProgrammatoArrivoDescrizione"))
            If effettivo <> "" Then
                v.Binario = effettivo
                v.BinarioConfermato = True
            Else
                v.Binario = programmato
            End If
            Dim stati As New List(Of String)()
            Dim prov As Boolean
            Dim provvedimento As Integer = NumeroIntero(o, "provvedimento", prov)
            If prov AndAlso provvedimento = 1 Then
                stati.Add("soppresso")
            Else
                If prov AndAlso provvedimento <> 0 Then stati.Add("con variazioni")
                If Testo(o, "inStazione").ToLowerInvariant() = "true" Then stati.Add("in stazione")
                If presente Then stati.Add(TestoRitardo(ritardo))
            End If
            v.Stato = String.Join(", ", stati)
            lista.Add(v)
        Next
        Return lista
    End Function

    Private Shared Async Function TabelloneItaloAsync(codice As String, partenze As Boolean) As Task(Of List(Of VoceTabellone))
        Dim corpo As String = Await LeggiAsync(BaseItalo & "RicercaStazioneService?CodiceStazione=" & Uri.EscapeDataString(codice), "Italo").ConfigureAwait(False)
        Dim lista As New List(Of VoceTabellone)()
        If String.IsNullOrWhiteSpace(corpo) Then Return lista
        Dim radice As JObject = JObject.Parse(corpo)
        Dim voci As JToken = radice(If(partenze, "ListaTreniPartenza", "ListaTreniArrivo"))
        If voci Is Nothing OrElse voci.Type <> JTokenType.Array Then Return lista
        For Each o As JToken In voci
            Dim v As New VoceTabellone()
            v.Fonte = "Italo"
            v.Categoria = "Italo"
            v.Numero = Testo(o, "Numero")
            v.Localita = NomeBello(Testo(o, "DescrizioneLocalita"))
            Dim ora As String = Testo(o, "OraPassaggio")
            If ora = "" Then Continue For
            v.Orario = DataVicina(ora)
            Dim nuovo As String = Testo(o, "NuovoOrario")
            Dim presente As Boolean
            Dim ritardo As Integer = NumeroIntero(o, "Ritardo", presente)
            v.Ritardo = ritardo
            If ritardo > 0 AndAlso nuovo <> "" Then v.OrarioPrevisto = nuovo
            v.Binario = Testo(o, "Binario")
            v.BinarioConfermato = v.Binario <> ""
            Dim stati As New List(Of String)()
            stati.Add(TestoRitardo(ritardo))
            Dim info As String = Testo(o, "Informazioni")
            If info <> "" Then stati.Add(info.ToLowerInvariant())
            v.Stato = String.Join(", ", stati)
            v.Percorso = Testo(o, "InfoRoute")
            lista.Add(v)
        Next
        Return lista
    End Function

    ''' <summary>Partenze o arrivi di una stazione, da tutte le fonti che la conoscono, in ordine di orario.</summary>
    Public Shared Async Function OttieniTabelloneAsync(stazione As StazioneScelta, partenze As Boolean) As Task(Of Tabellone)
        Try
            Dim tab As New Tabellone()
            Dim compiti As New List(Of Task(Of List(Of VoceTabellone)))()
            Dim fonti As New List(Of String)()
            If stazione.CodiceTrenitalia <> "" Then
                compiti.Add(TabelloneTrenitaliaAsync(stazione.CodiceTrenitalia, partenze))
                fonti.Add("Trenitalia")
            End If
            If stazione.CodiceItalo <> "" Then
                compiti.Add(TabelloneItaloAsync(stazione.CodiceItalo, partenze))
                fonti.Add("Italo")
            End If
            If compiti.Count = 0 Then Throw New ServizioNonDisponibileException("Trenitalia", Nothing)
            Dim riuscite As Integer = 0
            For i As Integer = 0 To compiti.Count - 1
                Try
                    tab.Voci.AddRange(Await compiti(i).ConfigureAwait(False))
                    riuscite += 1
                Catch ex As ServizioNonDisponibileException
                    tab.Avvisi.Add("Il servizio di " & fonti(i) & " non risponde.")
                Catch ex As Exception
                    _log.ScriviLog("TempoRealeTreni.OttieniTabellone " & fonti(i) & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
                    tab.Avvisi.Add("I dati di " & fonti(i) & " non si sono potuti leggere.")
                End Try
            Next
            If riuscite = 0 Then Throw New ServizioNonDisponibileException(fonti(0), Nothing)
            tab.Voci.Sort(Function(a, b) a.Orario.CompareTo(b.Orario))
            Return tab
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.OttieniTabelloneAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

#End Region

#Region "Treno per numero"

    ''' <summary>I treni di Trenitalia con quel numero (può essercene più d'uno, in giorni o con origini diverse).</summary>
    Public Shared Async Function CercaTrenoTrenitaliaAsync(numero As String) As Task(Of List(Of TrenoTrovato))
        Try
            Dim lista As New List(Of TrenoTrovato)()
            Dim corpo As String = Await LeggiAsync(BaseTrenitalia & "cercaNumeroTrenoTrenoAutocomplete/" & Uri.EscapeDataString(numero.Trim()), "Trenitalia").ConfigureAwait(False)
            For Each riga As String In corpo.Split({vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim p As Integer = riga.LastIndexOf("|"c)
                If p <= 0 Then Continue For
                Dim chiave As String() = riga.Substring(p + 1).Trim().Split("-"c)
                If chiave.Length < 3 Then Continue For
                Dim ms As Long
                If Not Long.TryParse(chiave(2), NumberStyles.Integer, CultureInfo.InvariantCulture, ms) Then Continue For
                Dim t As New TrenoTrovato()
                t.Numero = chiave(0)
                t.CodiceOrigine = chiave(1)
                t.Millisecondi = ms
                Dim descr As String = riga.Substring(0, p).Trim()
                Dim parti As String() = descr.Split({" - "}, StringSplitOptions.None)
                If parti.Length >= 3 Then
                    t.Descrizione = "Treno " & parti(0) & " da " & NomeBello(parti(1)) & ", " & DaMillisecondi(ms).ToString("dd/MM/yyyy")
                Else
                    t.Descrizione = descr
                End If
                lista.Add(t)
            Next
            lista.Sort(Function(a, b) b.Millisecondi.CompareTo(a.Millisecondi))
            Return lista
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.CercaTrenoTrenitaliaAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

    Public Shared Async Function AndamentoTrenitaliaAsync(t As TrenoTrovato) As Task(Of DettaglioTreno)
        Try
            Dim corpo As String = Await LeggiAsync(BaseTrenitalia & "andamentoTreno/" & Uri.EscapeDataString(t.CodiceOrigine) & "/" & Uri.EscapeDataString(t.Numero) & "/" & t.Millisecondi.ToString(CultureInfo.InvariantCulture), "Trenitalia").ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(corpo) Then Return Nothing
            Dim o As JObject = JObject.Parse(corpo)
            Dim d As New DettaglioTreno()
            d.Fonte = "Trenitalia"
            Dim sigla As String = SiglaCategoria(o)
            d.Etichetta = (If(sigla = "", "Treno", OrariTreni.NomeCategoriaPubblico(sigla)) & " " & Testo(o, "numeroTreno")).Trim()
            d.Origine = NomeBello(Testo(o, "origine"))
            d.Destinazione = NomeBello(Testo(o, "destinazione"))
            Dim statoParti As New List(Of String)()
            Dim presente As Boolean
            Dim provv As Integer = NumeroIntero(o, "provvedimento", presente)
            If presente AndAlso provv = 1 Then statoParti.Add("soppresso")
            Dim ritardo As Integer = NumeroIntero(o, "ritardo", presente)
            If presente AndAlso provv <> 1 Then statoParti.Add(TestoRitardo(ritardo))
            Dim ultima As String = NomeBello(Testo(o, "stazioneUltimoRilevamento"))
            Dim oraUltima As String = Testo(o, "compOraUltimoRilevamento")
            If ultima <> "" AndAlso ultima <> "--" Then statoParti.Add("ultimo rilevamento a " & ultima & If(oraUltima <> "" AndAlso oraUltima <> "--", " alle " & oraUltima, ""))
            d.Stato = String.Join(", ", statoParti)

            Dim fermate As JToken = o("fermate")
            If fermate IsNot Nothing AndAlso fermate.Type = JTokenType.Array Then
                For Each f As JToken In fermate
                    Dim s As New FermataDettaglio()
                    s.Nome = NomeBello(Testo(f, "stazione"))
                    Dim tipo As String = Testo(f, "tipoFermata")
                    Dim arrivoProg As Long = Millisecondi(f, "arrivo_teorico")
                    Dim partenzaProg As Long = Millisecondi(f, "partenza_teorica")
                    Dim arrivoReale As Long = Millisecondi(f, "arrivoReale")
                    Dim partenzaReale As Long = Millisecondi(f, "partenzaReale")
                    If tipo = "P" Then arrivoProg = 0
                    If tipo = "A" Then partenzaProg = 0
                    s.Arrivo = Ora(arrivoProg)
                    s.Partenza = Ora(partenzaProg)
                    s.ArrivoPrevisto = Ora(arrivoReale)
                    s.PartenzaPrevista = Ora(partenzaReale)
                    Dim bin As String
                    If tipo = "A" Then
                        bin = Testo(f, "binarioEffettivoArrivoDescrizione")
                        If bin = "" Then bin = Testo(f, "binarioProgrammatoArrivoDescrizione")
                    Else
                        bin = Testo(f, "binarioEffettivoPartenzaDescrizione")
                        If bin = "" Then bin = Testo(f, "binarioProgrammatoPartenzaDescrizione")
                    End If
                    s.Binario = bin
                    s.Passata = Millisecondi(f, "effettiva") > 0
                    d.Fermate.Add(s)
                Next
            End If
            Return d
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.AndamentoTrenitaliaAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Le fermate di un treno Italo ricavate dal percorso scritto nel tabellone (per esempio "Bologna centrale (9.08) - Milano Centrale (10.15)").
    ''' Serve quando il treno non è ancora in marcia e Italo non ne dà il dettaglio.
    ''' </summary>
    Public Shared Function DettaglioItaloDaTabellone(v As VoceTabellone, nomeStazione As String, partenza As Boolean) As DettaglioTreno
        Try
            Dim d As New DettaglioTreno()
            d.Fonte = "Italo"
            d.Etichetta = "Italo " & v.Numero
            d.Stato = "Percorso dal tabellone della stazione"
            Dim aggiunte As New List(Of FermataDettaglio)()
            For Each parte As String In v.Percorso.Split({" - "}, StringSplitOptions.RemoveEmptyEntries)
                Dim m As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(parte.Trim(), "^(.*?)\s*\((\d{1,2})\.(\d{2})\)\s*$")
                Dim f As New FermataDettaglio()
                If m.Success Then
                    f.Nome = NomeBello(m.Groups(1).Value)
                    f.Orario = CInt(m.Groups(2).Value).ToString("00") & ":" & m.Groups(3).Value
                Else
                    f.Nome = NomeBello(parte.Trim())
                End If
                aggiunte.Add(f)
            Next
            Dim qui As New FermataDettaglio()
            qui.Nome = nomeStazione
            qui.Orario = v.Orario.ToString("HH:mm")
            If partenza Then
                d.Origine = nomeStazione
                d.Fermate.Add(qui)
                d.Fermate.AddRange(aggiunte)
                If aggiunte.Count > 0 Then d.Destinazione = aggiunte(aggiunte.Count - 1).Nome
            Else
                d.Destinazione = nomeStazione
                d.Fermate.AddRange(aggiunte)
                d.Fermate.Add(qui)
                If aggiunte.Count > 0 Then d.Origine = aggiunte(0).Nome
            End If
            Return d
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.DettaglioItaloDaTabellone: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Un treno Italo in marcia. Nothing se Italo non lo conosce (non è in servizio adesso).</summary>
    Public Shared Async Function TrenoItaloAsync(numero As String) As Task(Of DettaglioTreno)
        Try
            Dim corpo As String = Await LeggiAsync(BaseItalo & "RicercaTrenoService?TrainNumber=" & Uri.EscapeDataString(numero.Trim()), "Italo").ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(corpo) Then Return Nothing
            Dim radice As JObject = JObject.Parse(corpo)
            Dim vuoto As JToken = radice("IsEmpty")
            If vuoto IsNot Nothing AndAlso vuoto.Type = JTokenType.Boolean AndAlso CBool(vuoto) Then Return Nothing
            Dim ts As JToken = radice("TrainSchedule")
            If ts Is Nothing OrElse ts.Type <> JTokenType.Object Then Return Nothing

            Dim d As New DettaglioTreno()
            d.Fonte = "Italo"
            d.Etichetta = "Italo " & Testo(ts, "TrainNumber")
            d.Origine = Testo(ts, "DepartureStationDescription")
            d.Destinazione = Testo(ts, "ArrivalStationDescription")
            Dim disturbo As JToken = ts("Distruption")
            If disturbo IsNot Nothing Then
                If disturbo.Type = JTokenType.Object Then
                    Dim presenteRitardo As Boolean
                    Dim ritardoItalo As Integer = NumeroIntero(disturbo, "DelayAmount", presenteRitardo)
                    If presenteRitardo Then d.Stato = TestoRitardo(ritardoItalo)
                    If Testo(disturbo, "Warning").ToLowerInvariant() = "true" Then d.Stato &= If(d.Stato <> "", ", ", "") & "con avvisi"
                ElseIf disturbo.Type = JTokenType.String Then
                    d.Stato = disturbo.ToString().Trim()
                End If
            End If

            Dim fermate As New SortedDictionary(Of Integer, JToken)()
            Dim partenza As JToken = ts("StazionePartenza")
            If partenza IsNot Nothing AndAlso partenza.Type = JTokenType.Object Then fermate(0) = partenza
            For Each nome As String In New String() {"StazioniFerme", "StazioniNonFerme"}
                Dim lista As JToken = ts(nome)
                If lista Is Nothing OrElse lista.Type <> JTokenType.Array Then Continue For
                For Each f As JToken In lista
                    Dim presente As Boolean
                    Dim n As Integer = NumeroIntero(f, "StationNumber", presente)
                    If presente AndAlso Not fermate.ContainsKey(n) Then fermate(n) = f
                Next
            Next
            Dim primo As Boolean = True
            Dim contatore As Integer = 0
            For Each coppia As KeyValuePair(Of Integer, JToken) In fermate
                contatore += 1
                Dim f As JToken = coppia.Value
                Dim s As New FermataDettaglio()
                s.Nome = Testo(f, "LocationDescription")
                Dim ultima As Boolean = (contatore = fermate.Count)
                If Not primo Then s.Arrivo = Testo(f, "EstimatedArrivalTime")
                If Not ultima Then s.Partenza = Testo(f, "EstimatedDepartureTime")
                s.Passata = Testo(f, "ActualDepartureTime") <> "" AndAlso Not ultima
                d.Fermate.Add(s)
                primo = False
            Next
            Return d
        Catch ex As ServizioNonDisponibileException
            Throw
        Catch ex As Exception
            _log.ScriviLog("TempoRealeTreni.TrenoItaloAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

#End Region

End Class
