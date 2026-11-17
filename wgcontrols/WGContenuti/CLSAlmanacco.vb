Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

Public Structure CoordinateCitta
    Public Lat As Double
    Public Lon As Double
    Public NomeRisolto As String
    Public Paese As String
    Public Regione As String
End Structure

Public Structure DatiSolari
    Public Valido As Boolean
    Public Alba As String
    Public AlbaCivile As String
    Public Tramonto As String
    Public TramontoCivile As String
    Public MezzogiornoSolare As String
    Public OraDoro As String
    Public DurataGiorno As String
End Structure

Public Structure VoceStorica
    Public Anno As Integer
    Public Testo As String
End Structure


Public Class Santo
    Public Property Nome As String = ""
    Public Property Descrizione As String = ""
    Public Property Categoria As String = ""
    Public Property TitoloPagina As String = ""

    Public ReadOnly Property TestoMenu As String
        Get
            Dim parti As New List(Of String)()
            If Not String.IsNullOrWhiteSpace(Nome) Then parti.Add(Nome.Trim())
            If Not String.IsNullOrWhiteSpace(Descrizione) Then parti.Add(Descrizione.Trim())
            If Not String.IsNullOrWhiteSpace(Categoria) Then parti.Add("[" & Categoria.Trim() & "]")
            Return String.Join(" - ", parti)
        End Get
    End Property
End Class

Public Class Onomastico
    Public Property Id As Integer = 0
    Public Property Nome As String = ""

    Public ReadOnly Property TestoMenu As String
        Get
            If String.IsNullOrWhiteSpace(Nome) Then Return ""
            Return Nome.Trim()
        End Get
    End Property
End Class

Public Class CLSAlmanacco

    Private logger As New wgcontrols.Logger()
    Private Shared ReadOnly _client As New HttpClient()

    Private Const WikipediaOnThisDayUrl As String = "https://it.wikipedia.org/api/rest_v1/feed/onthisday/events/{0}/{1}"
    Private Const WikipediaSelectedUrl As String = "https://it.wikipedia.org/api/rest_v1/feed/onthisday/selected/{0}/{1}"
    Private Const WikipediaBirthsUrl As String = "https://it.wikipedia.org/api/rest_v1/feed/onthisday/births/{0}/{1}"
    Private Const WikipediaDeathsUrl As String = "https://it.wikipedia.org/api/rest_v1/feed/onthisday/deaths/{0}/{1}"
    Private Const WikipediaSummaryUrl As String = "https://it.wikipedia.org/api/rest_v1/page/summary/{0}"
    Private Const WikipediaParseUrl As String = "https://it.wikipedia.org/w/api.php?action=parse&page={0}&prop=wikitext&format=json&formatversion=2"
    Private Const WikipediaSearchUrl As String = "https://it.wikipedia.org/w/api.php?action=query&list=search&srsearch={0}&srlimit=5&format=json&formatversion=2"
    Private Const SunriseSunsetUrl As String = "https://api.sunrisesunset.io/json?lat={0}&lng={1}&date=today&time_format=24"
    Private Const WikidataSparqlUrl As String = "https://query.wikidata.org/sparql?format=json&query={0}"

    Private Const MaxAccaddeOggi As Integer = 12
    Private Const MaxCuriosita As Integer = 5
    Private Const MaxNati As Integer = 6
    Private Const MaxMorti As Integer = 6
    Private Const MaxSanti As Integer = 20

    Private Const RiferimentoLunaNuovaJd As Double = 2451550.26
    Private Const PeriodoSinodico As Double = 29.53058867

    Public Sub New()
        Try
            If _client.DefaultRequestHeaders.UserAgent.Count = 0 Then
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Almanacco/2.0 (accessibilita; postmaster@wingabriel.net)")
            End If
            If Not _client.DefaultRequestHeaders.Contains("Accept") Then
                _client.DefaultRequestHeaders.Add("Accept", "application/json")
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Async Function RisolviCoordinate(lat As Double, lon As Double, Optional nomeCitta As String = "") As Task(Of CoordinateCitta)
        Dim risultato As New CoordinateCitta With {
            .Lat = lat,
            .Lon = lon,
            .NomeRisolto = "",
            .Paese = "",
            .Regione = ""
        }

        If String.IsNullOrWhiteSpace(nomeCitta) Then
            Return risultato
        End If

        Try
            Dim url As String = String.Format(CLSMeteo.OpenMeteoGeocodingUrlPublic, Uri.EscapeDataString(nomeCitta.Trim()))
            Dim json As String = Await _client.GetStringAsync(url)
            Dim obj As JObject = JObject.Parse(json)
            Dim results As JArray = TryCast(obj("results"), JArray)

            If results Is Nothing OrElse results.Count = 0 Then
                Return risultato
            End If

            Dim primo As JToken = results(0)
            Dim latVal As Double? = JsonDouble(primo("latitude"))
            Dim lonVal As Double? = JsonDouble(primo("longitude"))

            If latVal.HasValue AndAlso lonVal.HasValue Then
                risultato.Lat = latVal.Value
                risultato.Lon = lonVal.Value
            End If

            risultato.NomeRisolto = If(primo("name")?.ToString(), "")
            risultato.Paese = If(primo("country")?.ToString(), "")
            risultato.Regione = If(primo("admin1")?.ToString(), "")

        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RisolviCoordinate (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RisolviCoordinate - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return risultato
    End Function

    Public Async Function OttieniTestoAlmanacco(lat As Double, lon As Double, Optional nomeCitta As String = "", Optional dataRichiesta As DateTime? = Nothing) As Task(Of String)
        Dim sb As New StringBuilder()
        Dim oggi As DateTime = If(dataRichiesta.HasValue, dataRichiesta.Value, DateTime.Now)
        Dim cul As New CultureInfo("it-IT")
        Dim cal As Calendar = cul.Calendar

        Dim coord As CoordinateCitta = Await RisolviCoordinate(lat, lon, nomeCitta)

        Try
            Dim nmGio As String = oggi.ToString("dddd", cul).ToUpper()
            Dim dtCmp As String = oggi.ToString("d MMMM yyyy", cul).ToUpper()
            Dim sgnZod As String = CalcolaSegnoZodiacale(oggi)
            Dim nSet As Integer = cal.GetWeekOfYear(oggi, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)
            Dim ggAnn As Integer = oggi.DayOfYear

            sb.AppendLine($"DATA: {nmGio} {dtCmp}")
            sb.AppendLine($"SEGNO ZODIACALE: {sgnZod}")
            sb.AppendLine($"Settimana n. {nSet} - Giorno dell'anno: {ggAnn}")

            If Not String.IsNullOrWhiteSpace(coord.NomeRisolto) Then
                Dim localita As String = coord.NomeRisolto
                If Not String.IsNullOrWhiteSpace(coord.Regione) Then localita &= $", {coord.Regione}"
                If Not String.IsNullOrWhiteSpace(coord.Paese) Then localita &= $" ({coord.Paese})"
                sb.AppendLine($"LOCALITA': {localita}")
            End If

            sb.AppendLine("--------------------------------------------------")

            Dim nat As New DateTime(oggi.Year, 12, 25)
            If oggi.Date > nat Then nat = nat.AddYears(1)
            Dim finAnn As New DateTime(oggi.Year, 12, 31)
            sb.AppendLine($"Giorni mancanti a Natale: {(nat - oggi.Date).Days}")
            sb.AppendLine($"Giorni mancanti alla fine dell'anno: {(finAnn - oggi.Date).Days}")
            sb.AppendLine()

        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniTestoAlmanacco (dati base) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("Impossibile calcolare i dettagli della data.")
        End Try

        Await AggiungiFaseLunareEAstronomia(sb, oggi, coord.Lat, coord.Lon)
        sb.AppendLine()

        Await AggiungiSantoDelGiorno(sb, oggi)
        sb.AppendLine()

        Await AggiungiAccaddeOggi(sb, oggi)
        sb.AppendLine()

        Await AggiungiCuriosita(sb, oggi)
        sb.AppendLine()

        Await AggiungiNoteDelGiorno(sb, oggi)

        Return sb.ToString()
    End Function


    Public Async Function OttieniSantiDelGiorno(dataRif As DateTime) As Task(Of List(Of Santo))
        Dim risultato As New List(Of Santo)()
        Try
            Dim santi As List(Of Santo) = Await RecuperaSantiDelGiornoAsync(dataRif)
            If santi IsNot Nothing Then
                risultato = santi
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniSantiDelGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Public Async Function CercaOnomastici(dataRif As DateTime) As Task(Of List(Of Onomastico))
        Dim risultato As New List(Of Onomastico)()
        Try
            Dim santi As List(Of Santo) = Await RecuperaSantiDelGiornoAsync(dataRif)
            If santi Is Nothing OrElse santi.Count = 0 Then
                Return risultato
            End If

            Dim nomiUnici As New List(Of String)()
            Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each s As Santo In santi
                If s Is Nothing Then Continue For
                Dim nomi As List(Of String) = EstraiNomiOnomastico(s.Nome)
                For Each nome As String In nomi
                    If String.IsNullOrWhiteSpace(nome) Then Continue For
                    If visti.Add(nome) Then
                        nomiUnici.Add(nome)
                    End If
                Next
            Next

            nomiUnici.Sort(AddressOf ConfrontaOnomasticiAlfabetico)

            Dim progressivo As Integer = 1
            For Each nome As String In nomiUnici
                Dim voce As New Onomastico With {
                    .Id = progressivo,
                    .Nome = nome
                }
                risultato.Add(voce)
                progressivo += 1
            Next
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CercaOnomastici - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Public Async Function OttieniInfoOnomastico(nomeOnomastico As String) As Task(Of String)
        Try
            If String.IsNullOrWhiteSpace(nomeOnomastico) Then
                Return "Nessun onomastico specificato."
            End If

            Dim nomePulito As String = NormalizzaNomeProprio(nomeOnomastico.Trim())

            ' 1° tentativo: pagina di disambiguazione standard del nome ("Gabriele (nome)")
            Dim estratto As EstrattoWiki = Await RecuperaEstrattoOnomastico(nomePulito & " (nome)")

            ' 2° tentativo: pagina diretta col solo nome (es. "Gabriele Arcangelo" o santo principale)
            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                estratto = Await RecuperaEstrattoOnomastico(nomePulito)
            End If

            ' 3° tentativo: pagina diretta "San {Nome}" (copre molti santi noti senza voce "(nome)")
            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                estratto = Await RecuperaEstrattoOnomastico("San " & nomePulito)
            End If

            ' 4° tentativo: ricerca testuale su Wikipedia per trovare il titolo più pertinente
            ' (utile quando il nome ha una grafia diversa, un titolo composto, o è poco comune)
            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                Dim titoloTrovato As String = Await CercaTitoloWikipedia(nomePulito & " nome")
                If Not String.IsNullOrWhiteSpace(titoloTrovato) Then
                    estratto = Await RecuperaEstrattoOnomastico(titoloTrovato)
                End If
            End If

            If estratto Is Nothing OrElse String.IsNullOrWhiteSpace(estratto.Testo) Then
                Return $"Onomastico di {nomePulito}: informazioni non disponibili."
            End If

            Dim intestazione As String = $"Onomastico di {nomePulito}."
            If estratto.Disambigua Then
                ' Non abbiamo trovato una voce specifica sul nome: avvisiamo che il testo
                ' proviene da una pagina più generica, ma lo forniamo comunque come spunto utile.
                intestazione &= " (informazioni generali)"
            End If

            Return intestazione & Environment.NewLine & estratto.Testo
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniInfoOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Impossibile recuperare le informazioni sull'onomastico."
        End Try
    End Function

    Public Async Function OttieniInfoSanto(santo As Santo) As Task(Of String)
        Try
            If santo Is Nothing OrElse String.IsNullOrWhiteSpace(santo.Nome) Then
                Return "Nessun santo specificato."
            End If

            Dim nome As String = santo.Nome.Trim()
            Dim estratto As EstrattoWiki = Nothing

            If Not String.IsNullOrWhiteSpace(santo.TitoloPagina) Then
                estratto = Await RecuperaEstrattoOnomastico(santo.TitoloPagina)
            End If

            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                estratto = Await RecuperaEstrattoOnomastico(nome)
            End If

            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                Dim nl As String = nome.ToLowerInvariant()
                If Not (nl.StartsWith("san") OrElse nl.StartsWith("beat")) Then
                    estratto = Await RecuperaEstrattoOnomastico("San " & nome)
                End If
            End If

            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                Dim titoloTrovato As String = Await CercaTitoloWikipedia(nome & " santo")
                If Not String.IsNullOrWhiteSpace(titoloTrovato) Then
                    estratto = Await RecuperaEstrattoOnomastico(titoloTrovato)
                End If
            End If

            If estratto Is Nothing OrElse Not estratto.Riuscito Then
                Dim titoloTrovato As String = Await CercaTitoloWikipedia(nome)
                If Not String.IsNullOrWhiteSpace(titoloTrovato) Then
                    estratto = Await RecuperaEstrattoOnomastico(titoloTrovato)
                End If
            End If

            Dim sb As New StringBuilder()
            sb.Append(nome).Append(".")
            If Not String.IsNullOrWhiteSpace(santo.Descrizione) Then
                sb.Append(" ").Append(santo.Descrizione.Trim())
                If Not santo.Descrizione.Trim().EndsWith(".") Then sb.Append(".")
            End If

            If estratto IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(estratto.Testo) Then
                sb.AppendLine()
                If estratto.Disambigua Then
                    sb.AppendLine("(informazioni generali)")
                End If
                sb.Append(estratto.Testo)
            ElseIf String.IsNullOrWhiteSpace(santo.Descrizione) Then
                sb.AppendLine()
                sb.Append("Non sono disponibili informazioni dettagliate su questo santo.")
            End If

            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniInfoSanto - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Impossibile recuperare le informazioni sul santo."
        End Try
    End Function

    Public Async Function OttieniInfoSanto(nomeSanto As String) As Task(Of String)
        Try
            Return Await OttieniInfoSanto(New Santo With {.Nome = If(nomeSanto, "").Trim()})
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniInfoSanto (nome) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Impossibile recuperare le informazioni sul santo."
        End Try
    End Function

    ''' <summary>Esito della ricerca di un estratto Wikipedia per l'onomastico.</summary>
    Private Class EstrattoWiki
        Public Property Riuscito As Boolean = False
        Public Property Disambigua As Boolean = False
        Public Property Testo As String = ""
        Public Property TitoloEffettivo As String = ""
    End Class

    Private Async Function RecuperaEstrattoOnomastico(titoloPagina As String) As Task(Of EstrattoWiki)
        Dim esito As New EstrattoWiki()
        Try
            If String.IsNullOrWhiteSpace(titoloPagina) Then Return esito

            Dim titoloEnc As String = Uri.EscapeDataString(titoloPagina.Replace(" "c, "_"c))
            Dim url As String = String.Format(WikipediaSummaryUrl, titoloEnc)
            Dim json As String = Await GetStringSafeAsync(url)

            ' Pagina inesistente (404): non è un errore, semplicemente questo titolo non esiste.
            If json Is Nothing Then Return esito

            Dim obj As JObject = JObject.Parse(json)
            esito.TitoloEffettivo = If(obj("title")?.ToString(), titoloPagina)

            Dim tipo As String = If(obj("type")?.ToString(), "")
            Dim estratto As String = RimuoviTagHtml(If(obj("extract")?.ToString(), "")).Trim()

            If String.IsNullOrWhiteSpace(estratto) Then Return esito

            If tipo = "disambiguation" Then
                ' Pagina di disambiguazione: la teniamo solo come ultima risorsa,
                ' segnalandola come tale, ma non la consideriamo un successo "pieno".
                esito.Disambigua = True
                esito.Testo = estratto
                esito.Riuscito = False
                Return esito
            End If

            esito.Testo = estratto
            esito.Riuscito = True
            Return esito
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RecuperaEstrattoOnomastico (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return esito
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RecuperaEstrattoOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return esito
        End Try
    End Function

    ''' <summary>
    ''' Cerca su Wikipedia il titolo della pagina più pertinente per la query data,
    ''' usando l'API di ricerca testuale (action=query&list=search). Utile come
    ''' fallback quando il titolo "diretto" (es. "{Nome} (nome)") non esiste.
    ''' </summary>
    Private Async Function CercaTitoloWikipedia(query As String) As Task(Of String)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return ""
            Dim url As String = String.Format(WikipediaSearchUrl, Uri.EscapeDataString(query))
            Dim json As String = Await GetStringSafeAsync(url)
            If json Is Nothing Then Return ""

            Dim obj As JObject = JObject.Parse(json)
            Dim risultati As JArray = TryCast(obj("query")?("search"), JArray)
            If risultati Is Nothing OrElse risultati.Count = 0 Then Return ""

            Dim primoTitolo As String = If(risultati(0)("title")?.ToString(), "")
            Return primoTitolo
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.CercaTitoloWikipedia (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CercaTitoloWikipedia - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function



    Private Function ConfrontaOnomasticiAlfabetico(a As String, b As String) As Integer
        Try
            Return String.Compare(a, b, StringComparison.CurrentCultureIgnoreCase)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.ConfrontaOnomasticiAlfabetico - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function EstraiNomiOnomastico(nomeSanto As String) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(nomeSanto) Then Return risultato

            Dim testo As String = nomeSanto.Trim()
            testo = Regex.Replace(testo, "\(.*?\)", " ")
            testo = testo.Replace("’", "'")

            Dim prefissi() As String = {"santissima ", "santissimo ", "santi ", "sante ", "santa ", "santo ", "san ", "beati ", "beate ", "beata ", "beato ", "b. ", "ss. ", "s. "}
            Dim lavoro As String = testo
            Dim modificato As Boolean = True
            Do While modificato
                modificato = False
                Dim lavoroLower As String = lavoro.ToLowerInvariant()
                For Each pref As String In prefissi
                    If lavoroLower.StartsWith(pref) Then
                        lavoro = lavoro.Substring(pref.Length).Trim()
                        modificato = True
                        Exit For
                    End If
                Next
                If lavoro.ToLowerInvariant().StartsWith("sant'") Then
                    lavoro = lavoro.Substring(5).Trim()
                    modificato = True
                End If
            Loop

            If String.IsNullOrWhiteSpace(lavoro) Then Return risultato

            ' Individua il punto in cui termina la parte "anagrafica" (nome/i propri) e
            ' inizia la descrizione (titolo, provenienza, ruolo...). " e "/" ed " NON sono
            ' terminatori: separano più nomi di una stessa celebrazione (es. "Pietro e Paolo").
            Dim terminatori() As String = {" di ", " da ", " del ", " della ", " dei ", " degli ", " delle ", " d'", " vescovo", " martire", " papa", " abate", " confessore", " vergine", " apostolo", " apostoli", " evangelista", " profeta", " re ", " regina", " sacerdote", " diacono", " monaco", " eremita", " fondatore", " fondatrice"}
            Dim lavoroLower2 As String = lavoro.ToLowerInvariant()
            Dim taglio As Integer = lavoro.Length
            For Each term As String In terminatori
                Dim idx As Integer = lavoroLower2.IndexOf(term, StringComparison.Ordinal)
                If idx > 0 AndAlso idx < taglio Then
                    taglio = idx
                End If
            Next

            Dim parteNomi As String = lavoro.Substring(0, taglio).Trim()
            parteNomi = parteNomi.Trim(" "c, ","c, ";"c, "."c, ":"c)
            If String.IsNullOrWhiteSpace(parteNomi) Then Return risultato

            ' Suddivide eventuali nomi multipli congiunti da "e"/"ed"/virgola
            ' (es. "Pietro e Paolo" -> "Pietro", "Paolo"; "Cosma e Damiano" -> ...).
            Dim segmenti As String() = Regex.Split(parteNomi, "\s*,\s*|\s+ed?\s+", RegexOptions.IgnoreCase)

            For Each segmento As String In segmenti
                Dim nomeProprio As String = segmento.Trim(" "c, ","c, ";"c, "."c, ":"c)
                If String.IsNullOrWhiteSpace(nomeProprio) Then Continue For
                If nomeProprio.Length < 2 Then Continue For
                risultato.Add(NormalizzaNomeProprio(nomeProprio))
            Next
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiNomiOnomastico - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function NormalizzaNomeProprio(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim parole As String() = nome.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            Dim sb As New StringBuilder()
            For i As Integer = 0 To parole.Length - 1
                Dim p As String = parole(i)
                If p.Length = 1 Then
                    sb.Append(Char.ToUpper(p(0)))
                Else
                    sb.Append(Char.ToUpper(p(0)))
                    sb.Append(p.Substring(1).ToLowerInvariant())
                End If
                If i < parole.Length - 1 Then
                    sb.Append(" "c)
                End If
            Next
            Return sb.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.NormalizzaNomeProprio - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return nome
        End Try
    End Function

    Public Async Function OttieniDatiSolari(dataLocale As DateTime, lat As Double, lon As Double) As Task(Of DatiSolari)
        Try
            Return Await RecuperaDatiSolariAsync(dataLocale, lat, lon)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.OttieniDatiSolari - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New DatiSolari With {.Valido = False}
        End Try
    End Function

    Private Async Function RecuperaSantiDelGiornoAsync(dataRif As DateTime) As Task(Of List(Of Santo))
        Dim risultato As New List(Of Santo)()
        Try
            risultato = Await RecuperaSantiDaWikidataAsync(dataRif)

            If risultato.Count = 0 Then
                Dim cul As New CultureInfo("it-IT")
                Dim nomePagina As String = $"{dataRif.Day}_{dataRif.ToString("MMMM", cul)}"
                Dim url As String = String.Format(WikipediaParseUrl, Uri.EscapeDataString(nomePagina))
                Dim json As String = Await _client.GetStringAsync(url)
                Dim obj As JObject = JObject.Parse(json)
                Dim wikitext As String = If(obj("parse")?("wikitext")?.ToString(), "")

                risultato = EstraiSantiConDettagli(wikitext)
            End If

            If risultato.Count = 0 Then
                Dim santoSintetico As Santo = Await RecuperaSantoSinteticoAsync(dataRif)
                If santoSintetico IsNot Nothing Then
                    risultato.Add(santoSintetico)
                End If
            End If

            risultato = RimuoviDuplicatiSanti(risultato)
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RecuperaSantiDelGiornoAsync (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RecuperaSantiDelGiornoAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Async Function RecuperaSantiDaWikidataAsync(dataRif As DateTime) As Task(Of List(Of Santo))
        Dim risultato As New List(Of Santo)()
        Try
            Dim sparql As String = "SELECT ?santo ?santoLabel ?articolo ?desc WHERE { " &
                "?santo p:P841 ?fsStmt . " &
                "?fsStmt psv:P841 ?fsNode . " &
                "?fsNode wikibase:timeValue ?time . " &
                "FILTER(MONTH(?time) = " & dataRif.Month.ToString(CultureInfo.InvariantCulture) &
                " && DAY(?time) = " & dataRif.Day.ToString(CultureInfo.InvariantCulture) & ") " &
                "OPTIONAL { ?articolo schema:about ?santo ; schema:isPartOf <https://it.wikipedia.org/> . } " &
                "OPTIONAL { ?santo schema:description ?desc . FILTER(LANG(?desc) = ""it"") } " &
                "SERVICE wikibase:label { bd:serviceParam wikibase:language ""it,en"". } " &
                "} LIMIT 300"

            Dim url As String = String.Format(WikidataSparqlUrl, Uri.EscapeDataString(sparql))
            Dim json As String = Await GetStringSafeAsync(url)
            If json Is Nothing Then Return risultato

            Dim obj As JObject = JObject.Parse(json)
            Dim bindings As JArray = TryCast(obj("results")?("bindings"), JArray)
            If bindings Is Nothing Then Return risultato

            For Each b As JToken In bindings
                Try
                    Dim nomeCompleto As String = b("santoLabel")?("value")?.ToString()
                    If String.IsNullOrWhiteSpace(nomeCompleto) Then Continue For

                    Dim titoloPagina As String = ""
                    Dim articoloUrl As String = b("articolo")?("value")?.ToString()
                    If Not String.IsNullOrWhiteSpace(articoloUrl) Then
                        Dim prefisso As String = "https://it.wikipedia.org/wiki/"
                        If articoloUrl.StartsWith(prefisso, StringComparison.OrdinalIgnoreCase) Then
                            titoloPagina = Uri.UnescapeDataString(articoloUrl.Substring(prefisso.Length)).Replace("_"c, " "c)
                        End If
                    End If

                    Dim descrizione As String = ""
                    Dim descRaw As String = b("desc")?("value")?.ToString()
                    If Not String.IsNullOrWhiteSpace(descRaw) Then
                        descrizione = descRaw.Trim()
                        If descrizione.Length > 0 Then
                            descrizione = Char.ToUpper(descrizione(0), CultureInfo.InvariantCulture) & descrizione.Substring(1)
                        End If
                    End If

                    Dim santo As New Santo With {
                        .Nome = nomeCompleto.Trim(),
                        .Descrizione = descrizione,
                        .Categoria = "",
                        .TitoloPagina = titoloPagina
                    }
                    risultato.Add(santo)
                Catch exRiga As Exception
                    logger.ScriviLog("CLSAlmanacco.RecuperaSantiDaWikidataAsync (riga) - " & exRiga.Message)
                End Try
            Next
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RecuperaSantiDaWikidataAsync (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RecuperaSantiDaWikidataAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function EstraiSantiConDettagli(wikitext As String) As List(Of Santo)
        Dim risultato As New List(Of Santo)()
        Try
            If String.IsNullOrWhiteSpace(wikitext) Then Return risultato

            Dim righe As String() = wikitext.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
            Dim sezioneCorrente As String = ""
            Dim sezioneRilevante As Boolean = False
            Dim marcatoreReligione As String = ""

            For Each rigaRaw As String In righe
                Dim riga As String = rigaRaw.Trim()
                If String.IsNullOrWhiteSpace(riga) Then Continue For

                If Regex.IsMatch(riga, "^={2,6}.*={2,6}$") Then
                    sezioneCorrente = PulisciTitoloSezione(riga)
                    sezioneRilevante = SezioneSantiRilevante(sezioneCorrente)
                    marcatoreReligione = ""
                    Continue For
                End If

                If Not sezioneRilevante Then Continue For

                If riga.StartsWith("*") OrElse riga.StartsWith("#") Then
                    Dim rigaLista As String = riga.TrimStart("*"c, "#"c).Trim()
                    Dim titoloPagina As String = EstraiPrimoWikilink(rigaLista)
                    Dim contenuto As String = PulisciWikitext(rigaLista)
                    Dim categoria As String = If(String.IsNullOrWhiteSpace(marcatoreReligione), sezioneCorrente, marcatoreReligione)
                    Dim santo As Santo = CostruisciSanto(contenuto, categoria)
                    If santo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(santo.Nome) AndAlso santo.Nome.Length >= 3 Then
                        santo.TitoloPagina = titoloPagina
                        risultato.Add(santo)
                    End If
                Else
                    Dim rigaPulita As String = PulisciWikitext(riga)
                    Dim marcatore As String = EstraiMarcatoreReligione(rigaPulita)
                    If Not String.IsNullOrWhiteSpace(marcatore) Then
                        marcatoreReligione = marcatore
                    End If
                End If
            Next

            If risultato.Count = 0 Then
                For Each rigaRaw As String In righe
                    Dim riga As String = rigaRaw.Trim()
                    If Not (riga.StartsWith("*") OrElse riga.StartsWith("#")) Then Continue For
                    Dim rigaLista As String = riga.TrimStart("*"c, "#"c).Trim()
                    Dim contenuto As String = PulisciWikitext(rigaLista)
                    If Not ContieneRiferimentiSanti(contenuto) Then Continue For
                    Dim santo As Santo = CostruisciSanto(contenuto, "Santi del giorno")
                    If santo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(santo.Nome) AndAlso santo.Nome.Length >= 3 Then
                        santo.TitoloPagina = EstraiPrimoWikilink(rigaLista)
                        risultato.Add(santo)
                    End If
                Next
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiSantiConDettagli - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return risultato
    End Function

    Private Function PulisciTitoloSezione(titolo As String) As String
        Try
            If String.IsNullOrWhiteSpace(titolo) Then Return ""
            Dim s As String = Regex.Replace(titolo, "^=+", "")
            s = Regex.Replace(s, "=+$", "")
            Return s.Trim()
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.PulisciTitoloSezione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return titolo
        End Try
    End Function

    Private Function SezioneSantiRilevante(sezione As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(sezione) Then Return False
            Dim s As String = sezione.ToLowerInvariant()
            Return s.Contains("santo") OrElse
                   s.Contains("santi") OrElse
                   s.Contains("santa") OrElse
                   s.Contains("beati") OrElse
                   s.Contains("beato") OrElse
                   s.Contains("onomastico") OrElse
                   s.Contains("martirolog") OrElse
                   s.Contains("religio")
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.SezioneSantiRilevante - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function EstraiPrimoWikilink(rigaWikitext As String) As String
        Try
            If String.IsNullOrWhiteSpace(rigaWikitext) Then Return ""
            For Each m As Match In Regex.Matches(rigaWikitext, "\[\[([^\]\|]+)(?:\|[^\]]*)?\]\]")
                Dim titolo As String = m.Groups(1).Value.Trim()
                If String.IsNullOrWhiteSpace(titolo) Then Continue For
                Dim tl As String = titolo.ToLowerInvariant()
                If tl.StartsWith("file:") OrElse tl.StartsWith("immagine:") OrElse tl.StartsWith("image:") OrElse tl.StartsWith("categoria:") OrElse tl.StartsWith("category:") Then Continue For
                If titolo.Contains("#") Then titolo = titolo.Substring(0, titolo.IndexOf("#"c)).Trim()
                Return titolo
            Next
            Return ""
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiPrimoWikilink - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function EstraiMarcatoreReligione(rigaPulita As String) As String
        Try
            If String.IsNullOrWhiteSpace(rigaPulita) Then Return ""
            Dim r As String = rigaPulita.Trim()
            If Not r.EndsWith(":") Then Return ""
            r = r.TrimEnd(":"c).Trim()
            If r.Length = 0 OrElse r.Length > 60 Then Return ""
            Return r
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiMarcatoreReligione - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function ContieneRiferimentiSanti(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim s As String = testo.ToLowerInvariant()
            Return s.Contains("san ") OrElse
                   s.Contains("santa ") OrElse
                   s.Contains("santi") OrElse
                   s.Contains("beato") OrElse
                   s.Contains("beata") OrElse
                   s.Contains("martire") OrElse
                   s.Contains("onomastico")
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.ContieneRiferimentiSanti - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CostruisciSanto(testo As String, categoria As String) As Santo
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing

            Dim pulito As String = testo.Trim()
            Dim pl As String = pulito.ToLowerInvariant()
            If pl.StartsWith("vedi ") OrElse pl.StartsWith("cfr") OrElse pl.StartsWith("si veda") OrElse pl.StartsWith("per un elenco") Then Return Nothing

            Dim nome As String = pulito
            Dim descrizione As String = ""

            Dim separatori() As String = {" — ", " – ", " - ", " : "}
            For Each sep As String In separatori
                Dim idx As Integer = pulito.IndexOf(sep, StringComparison.Ordinal)
                If idx > 0 Then
                    nome = pulito.Substring(0, idx).Trim()
                    descrizione = pulito.Substring(idx + sep.Length).Trim()
                    Exit For
                End If
            Next

            If String.IsNullOrWhiteSpace(descrizione) Then
                Dim idxComma As Integer = pulito.IndexOf(","c)
                If idxComma > 0 Then
                    nome = pulito.Substring(0, idxComma).Trim()
                    descrizione = pulito.Substring(idxComma + 1).Trim()
                End If
            End If

            If String.IsNullOrWhiteSpace(nome) Then Return Nothing

            Return New Santo With {
                .Nome = nome,
                .Descrizione = descrizione,
                .Categoria = categoria
            }
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CostruisciSanto - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function RimuoviDuplicatiSanti(santi As List(Of Santo)) As List(Of Santo)
        Dim risultato As New List(Of Santo)()
        Try
            If santi Is Nothing Then Return risultato

            Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each s As Santo In santi
                If s Is Nothing Then Continue For
                Dim chiave As String
                If Not String.IsNullOrWhiteSpace(s.TitoloPagina) Then
                    chiave = "T|" & s.TitoloPagina.Trim()
                Else
                    chiave = "N|" & Regex.Replace(If(s.Nome, ""), "\s+", " ").Trim()
                End If
                If chiave = "T|" OrElse chiave = "N|" Then Continue For
                If visti.Add(chiave) Then
                    risultato.Add(s)
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RimuoviDuplicatiSanti - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Async Function RecuperaSantoSinteticoAsync(dataRif As DateTime) As Task(Of Santo)
        Try
            Dim titoloPagina As String = Uri.EscapeDataString($"{dataRif.Day}_{dataRif.ToString("MMMM", New CultureInfo("it-IT"))}")
            Dim url As String = String.Format(WikipediaSummaryUrl, titoloPagina)
            Dim json As String = Await GetStringSafeAsync(url)
            If json Is Nothing Then Return Nothing

            Dim obj As JObject = JObject.Parse(json)
            Dim estratto As String = If(obj("extract")?.ToString(), "")

            If Not String.IsNullOrWhiteSpace(estratto) Then
                Dim righe As String() = estratto.Split(New String() {Environment.NewLine, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Dim primaRiga As String = RimuoviTagHtml(If(righe.Length > 0, righe(0), estratto))
                If Not String.IsNullOrWhiteSpace(primaRiga) Then
                    Return New Santo With {
                        .Nome = "Santo del giorno",
                        .Descrizione = primaRiga.Trim(),
                        .Categoria = "Sintesi Wikipedia"
                    }
                End If
            End If
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RecuperaSantoSinteticoAsync (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RecuperaSantoSinteticoAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return Nothing
    End Function

    Private Async Function RecuperaDatiSolariAsync(dataLocale As DateTime, lat As Double, lon As Double) As Task(Of DatiSolari)
        Dim risultato As New DatiSolari With {.Valido = False}
        Try
            Dim url As String = String.Format(
                CultureInfo.InvariantCulture,
                "https://api.sunrisesunset.io/json?lat={0}&lng={1}&date={2}&time_format=24",
                lat,
                lon,
                dataLocale.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))

            Dim json As String = Await _client.GetStringAsync(url)
            Dim obj As JObject = JObject.Parse(json)

            If obj("status") Is Nothing OrElse obj("status").ToString() <> "OK" Then
                Return risultato
            End If

            Dim res As JToken = obj("results")
            If res Is Nothing Then
                Return risultato
            End If

            risultato.Alba = If(res("sunrise")?.ToString(), "N/D")
            risultato.AlbaCivile = If(res("dawn")?.ToString(), If(res("first_light")?.ToString(), "N/D"))
            risultato.Tramonto = If(res("sunset")?.ToString(), "N/D")
            risultato.TramontoCivile = If(res("dusk")?.ToString(), If(res("last_light")?.ToString(), "N/D"))
            risultato.MezzogiornoSolare = If(res("solar_noon")?.ToString(), "N/D")
            risultato.OraDoro = If(res("golden_hour")?.ToString(), "N/D")
            risultato.DurataGiorno = FormattaDurataGiorno(If(res("day_length")?.ToString(), ""))

            risultato.Valido = True
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.RecuperaDatiSolariAsync (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RecuperaDatiSolariAsync - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function FormattaDurataGiorno(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return "N/D"

            Dim ts As TimeSpan
            If TimeSpan.TryParse(valore, ts) Then
                Return $"{ts.Hours} ore e {ts.Minutes} minuti"
            End If

            Dim seconds As Double
            If Double.TryParse(valore, NumberStyles.Float, CultureInfo.InvariantCulture, seconds) Then
                Dim durata As TimeSpan = TimeSpan.FromSeconds(seconds)
                Return $"{durata.Hours} ore e {durata.Minutes} minuti"
            End If

            Return valore
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.FormattaDurataGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return valore
        End Try
    End Function


    ''' <summary>
    ''' Esegue una GET e restituisce il corpo della risposta come stringa.
    ''' Se il server risponde 404 (pagina/risorsa inesistente) restituisce Nothing
    ''' invece di lanciare un'eccezione, evitando rumore nei log per casi "normali"
    ''' (es. un nome proprio che non ha una pagina Wikipedia dedicata).
    ''' Per qualsiasi altro errore HTTP, l'eccezione viene comunque propagata.
    ''' </summary>
    Private Async Function GetStringSafeAsync(url As String) As Task(Of String)
        Using risposta As HttpResponseMessage = Await _client.GetAsync(url)
            If risposta.StatusCode = Net.HttpStatusCode.NotFound Then
                Return Nothing
            End If
            risposta.EnsureSuccessStatusCode()
            Return Await risposta.Content.ReadAsStringAsync()
        End Using
    End Function

    Private Function JsonDouble(token As JToken) As Double?
        Try
            If token Is Nothing OrElse token.Type = JTokenType.Null OrElse token.Type = JTokenType.Undefined Then
                Return Nothing
            End If
            Return token.Value(Of Double)()
        Catch ex As Exception
            Try
                Dim s As String = token.ToString().Trim().Replace(","c, "."c)
                Dim d As Double = 0
                If Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, d) Then
                    Return d
                End If
            Catch exInterno As Exception
                logger.ScriviLog("CLSAlmanacco.JsonDouble (fallback) - " & exInterno.Message)
            End Try
            Return Nothing
        End Try
    End Function

    Private Function FormatOrario(orario As String) As String
        If String.IsNullOrWhiteSpace(orario) Then Return "N/D"
        Dim ts As TimeSpan
        If TimeSpan.TryParse(orario, ts) Then Return ts.ToString("hh\:mm")
        Dim dt As DateTime
        If DateTime.TryParse(orario, dt) Then Return dt.ToString("HH:mm")
        Return orario
    End Function

    Private Function RimuoviTagHtml(inp As String) As String
        If String.IsNullOrEmpty(inp) Then Return String.Empty
        Dim risultato As String = Regex.Replace(inp, "<.*?>", String.Empty)
        risultato = System.Net.WebUtility.HtmlDecode(risultato)
        Return risultato.Trim()
    End Function

    Private Function GradiToRad(gradi As Double) As Double
        Return gradi * Math.PI / 180.0
    End Function

    Private Function RadToGradi(rad As Double) As Double
        Return rad * 180.0 / Math.PI
    End Function

    Private Function CalcolaFaseLunareLocale(dataUtc As DateTime) As Tuple(Of String, Double)
        Try
            Dim jd As Double = dataUtc.ToOADate() + 2415018.5
            Dim diff As Double = jd - RiferimentoLunaNuovaJd
            Dim eta As Double = diff - (PeriodoSinodico * Math.Floor(diff / PeriodoSinodico))
            Dim illum As Double = (1.0 - Math.Cos(2.0 * Math.PI * eta / PeriodoSinodico)) / 2.0 * 100.0

            Dim fase As String
            If eta < 1.84566 Then
                fase = "Luna Nuova"
            ElseIf eta < 5.53699 Then
                fase = "Luna Crescente"
            ElseIf eta < 9.22831 Then
                fase = "Primo Quarto"
            ElseIf eta < 12.91963 Then
                fase = "Gibbosa Crescente"
            ElseIf eta < 16.61096 Then
                fase = "Luna Piena"
            ElseIf eta < 20.30228 Then
                fase = "Gibbosa Calante"
            ElseIf eta < 23.99361 Then
                fase = "Ultimo Quarto"
            ElseIf eta < 27.68493 Then
                fase = "Luna Calante"
            Else
                fase = "Luna Nuova"
            End If

            Return New Tuple(Of String, Double)(fase, illum)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CalcolaFaseLunareLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New Tuple(Of String, Double)("Non disponibile", -1.0)
        End Try
    End Function

    Private Function CalcolaMinutiSolari(zenithGradi As Double, lat As Double, lon As Double, eqtime As Double, declRad As Double, alAlba As Boolean) As Double?
        Try
            Dim zenRad As Double = GradiToRad(zenithGradi)
            Dim latRad As Double = GradiToRad(lat)
            Dim cosHa As Double = (Math.Cos(zenRad) - Math.Sin(latRad) * Math.Sin(declRad)) / (Math.Cos(latRad) * Math.Cos(declRad))

            If cosHa > 1.0 OrElse cosHa < -1.0 Then
                Return Nothing
            End If

            Dim haGradi As Double = RadToGradi(Math.Acos(cosHa))
            Dim minutiUtc As Double
            If alAlba Then
                minutiUtc = 720.0 - 4.0 * (lon + haGradi) - eqtime
            Else
                minutiUtc = 720.0 - 4.0 * (lon - haGradi) - eqtime
            End If

            Return minutiUtc
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CalcolaMinutiSolari - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Function MinutiUtcAOraLocale(minutiUtc As Double?, dataLocale As DateTime) As String
        Try
            If Not minutiUtc.HasValue Then Return "N/D"
            Dim offsetMin As Double = TimeZoneInfo.Local.GetUtcOffset(dataLocale).TotalMinutes
            Dim tot As Double = minutiUtc.Value + offsetMin

            Do While tot < 0.0
                tot += 1440.0
            Loop
            Do While tot >= 1440.0
                tot -= 1440.0
            Loop

            Dim h As Integer = CInt(Math.Floor(tot / 60.0))
            Dim m As Integer = CInt(Math.Round(tot - (h * 60.0)))
            If m = 60 Then
                m = 0
                h += 1
                If h = 24 Then h = 0
            End If

            Return h.ToString("00") & ":" & m.ToString("00")
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.MinutiUtcAOraLocale - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "N/D"
        End Try
    End Function

    Private Function CalcolaDatiSolari(dataLocale As DateTime, lat As Double, lon As Double) As DatiSolari
        Dim risultato As New DatiSolari With {.Valido = False}
        Try
            Dim giornoAnno As Integer = dataLocale.DayOfYear
            Dim gamma As Double = (2.0 * Math.PI / 365.0) * (giornoAnno - 1 + (12.0 - 12.0) / 24.0)

            Dim eqtime As Double = 229.18 * (0.000075 +
                0.001868 * Math.Cos(gamma) -
                0.032077 * Math.Sin(gamma) -
                0.014615 * Math.Cos(2.0 * gamma) -
                0.040849 * Math.Sin(2.0 * gamma))

            Dim declRad As Double = 0.006918 -
                0.399912 * Math.Cos(gamma) +
                0.070257 * Math.Sin(gamma) -
                0.006758 * Math.Cos(2.0 * gamma) +
                0.000907 * Math.Sin(2.0 * gamma) -
                0.002697 * Math.Cos(3.0 * gamma) +
                0.00148 * Math.Sin(3.0 * gamma)

            Dim albaMin As Double? = CalcolaMinutiSolari(90.833, lat, lon, eqtime, declRad, True)
            Dim tramontoMin As Double? = CalcolaMinutiSolari(90.833, lat, lon, eqtime, declRad, False)
            Dim albaCivMin As Double? = CalcolaMinutiSolari(96.0, lat, lon, eqtime, declRad, True)
            Dim tramontoCivMin As Double? = CalcolaMinutiSolari(96.0, lat, lon, eqtime, declRad, False)
            Dim oraDoroMin As Double? = CalcolaMinutiSolari(84.0, lat, lon, eqtime, declRad, False)
            Dim mezzogiornoMin As Double = 720.0 - 4.0 * lon - eqtime

            risultato.Alba = MinutiUtcAOraLocale(albaMin, dataLocale)
            risultato.Tramonto = MinutiUtcAOraLocale(tramontoMin, dataLocale)
            risultato.AlbaCivile = MinutiUtcAOraLocale(albaCivMin, dataLocale)
            risultato.TramontoCivile = MinutiUtcAOraLocale(tramontoCivMin, dataLocale)
            risultato.OraDoro = MinutiUtcAOraLocale(oraDoroMin, dataLocale)
            risultato.MezzogiornoSolare = MinutiUtcAOraLocale(mezzogiornoMin, dataLocale)

            If albaMin.HasValue AndAlso tramontoMin.HasValue Then
                Dim durataMin As Double = tramontoMin.Value - albaMin.Value
                Do While durataMin < 0.0
                    durataMin += 1440.0
                Loop
                Dim ore As Integer = CInt(Math.Floor(durataMin / 60.0))
                Dim minuti As Integer = CInt(Math.Round(durataMin - (ore * 60.0)))
                If minuti = 60 Then
                    minuti = 0
                    ore += 1
                End If
                risultato.DurataGiorno = $"{ore} ore e {minuti} minuti"
            Else
                risultato.DurataGiorno = "N/D"
            End If

            risultato.Valido = True
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.CalcolaDatiSolari - " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Valido = False
        End Try

        Return risultato
    End Function

    Private Async Function AggiungiFaseLunareEAstronomia(sb As StringBuilder, oggi As DateTime, lat As Double, lon As Double) As Task
        Try
            Dim faseInfo As Tuple(Of String, Double) = CalcolaFaseLunareLocale(oggi.ToUniversalTime())
            sb.AppendLine($"FASE LUNARE: {faseInfo.Item1}")
            If faseInfo.Item2 >= 0 Then
                sb.AppendLine($"Illuminazione lunare: {faseInfo.Item2.ToString("0.0", CultureInfo.InvariantCulture)}%")
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiFaseLunareEAstronomia (luna) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("FASE LUNARE: non disponibile")
        End Try

        Try
            Dim solari As DatiSolari = CalcolaDatiSolari(oggi, lat, lon)
            If solari.Valido Then
                sb.AppendLine()
                sb.AppendLine("ASTRONOMIA:")
                sb.AppendLine($"Alba: {solari.Alba}")
                sb.AppendLine($"Alba civile: {solari.AlbaCivile}")
                sb.AppendLine($"Tramonto: {solari.Tramonto}")
                sb.AppendLine($"Crepuscolo civile: {solari.TramontoCivile}")
                sb.AppendLine($"Mezzogiorno Solare: {solari.MezzogiornoSolare}")
                sb.AppendLine($"Ora d'oro: {solari.OraDoro}")
                sb.AppendLine($"Durata del giorno: {solari.DurataGiorno}")
            Else
                sb.AppendLine()
                sb.AppendLine("ASTRONOMIA: dati solari non disponibili")
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiFaseLunareEAstronomia (sole) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("ASTRONOMIA: dati solari non disponibili")
        End Try

        Try
            Dim url As String = String.Format(
                CultureInfo.InvariantCulture,
                "https://api.sunrisesunset.io/json?lat={0}&lng={1}&date={2}&time_format=24",
                lat,
                lon,
                oggi.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            Dim json As String = Await _client.GetStringAsync(url)
            Dim obj As JObject = JObject.Parse(json)

            If obj("status")?.ToString() = "OK" Then
                Dim res As JToken = obj("results")
                Dim moonrise As String = If(res("moonrise")?.ToString(), "")
                Dim moonset As String = If(res("moonset")?.ToString(), "")
                If Not String.IsNullOrWhiteSpace(moonrise) AndAlso moonrise <> "No moonrise" Then
                    sb.AppendLine($"Sorgere della Luna: {FormatOrario(moonrise)}")
                End If
                If Not String.IsNullOrWhiteSpace(moonset) AndAlso moonset <> "No moonset" Then
                    sb.AppendLine($"Tramonto della Luna: {FormatOrario(moonset)}")
                End If
            End If
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiFaseLunareEAstronomia (moon API HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiFaseLunareEAstronomia (moon API) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Function

    Private Async Function AggiungiSantoDelGiorno(sb As StringBuilder, oggi As DateTime) As Task
        sb.AppendLine("SANTI DEL GIORNO:")
        Try
            Dim cul As New CultureInfo("it-IT")
            Dim nomePagina As String = $"{oggi.Day}_{oggi.ToString("MMMM", cul)}"
            Dim url As String = String.Format(WikipediaParseUrl, Uri.EscapeDataString(nomePagina))
            Dim json As String = Await _client.GetStringAsync(url)
            Dim obj As JObject = JObject.Parse(json)
            Dim wikitext As String = If(obj("parse")?("wikitext")?.ToString(), "")

            Dim santi As List(Of String) = EstraiSantiDaWikitext(wikitext)

            If santi IsNot Nothing AndAlso santi.Count > 0 Then
                Dim conteggio As Integer = 0
                For Each s As String In santi
                    If conteggio >= MaxSanti Then Exit For
                    sb.AppendLine($"- {s}")
                    conteggio += 1
                Next
            Else
                Await AggiungiSantoDelGiornoFallback(sb, oggi)
            End If

        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiSantoDelGiorno (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            AggiungiSantoDelGiornoFallback(sb, oggi)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiSantoDelGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            AggiungiSantoDelGiornoFallback(sb, oggi)
        End Try
    End Function

    Private Function EstraiSantiDaWikitext(wikitext As String) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(wikitext) Then Return risultato

            Dim mSezione As Match = Regex.Match(wikitext, "===?\s*Religiose\s*===?", RegexOptions.IgnoreCase)
            If Not mSezione.Success Then
                mSezione = Regex.Match(wikitext, "===?\s*Religion[ei]\s*===?", RegexOptions.IgnoreCase)
            End If

            Dim blocco As String
            If mSezione.Success Then
                Dim inizio As Integer = mSezione.Index + mSezione.Length
                Dim resto As String = wikitext.Substring(inizio)
                Dim mFine As Match = Regex.Match(resto, "(\r?\n)==[^=]")
                If mFine.Success Then
                    blocco = resto.Substring(0, mFine.Index)
                Else
                    blocco = resto
                End If
            Else
                blocco = wikitext
            End If

            Dim righe As String() = blocco.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
            For Each riga As String In righe
                Dim trim As String = riga.Trim()
                If trim.StartsWith("*") Then
                    Dim contenuto As String = trim.TrimStart("*"c).Trim()
                    contenuto = PulisciWikitext(contenuto)
                    If Not String.IsNullOrWhiteSpace(contenuto) AndAlso contenuto.Length > 2 Then
                        If RigaSantoValida(contenuto) Then
                            risultato.Add(contenuto)
                        End If
                    End If
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiSantiDaWikitext - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return risultato
    End Function

    Private Function RigaSantoValida(testo As String) As Boolean
        Try
            Dim l As String = testo.ToLowerInvariant()
            If l.Contains("religione cristiana") Then Return False
            If l.Contains("chiesa cattolica") Then Return False
            If l.StartsWith("cristianesimo") Then Return False
            Return True
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.RigaSantoValida - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Private Function PulisciWikitext(inp As String) As String
        Try
            If String.IsNullOrEmpty(inp) Then Return String.Empty
            Dim s As String = inp

            s = Regex.Replace(s, "<ref[^>]*?/>", String.Empty, RegexOptions.IgnoreCase)
            s = Regex.Replace(s, "<ref[^>]*?>.*?</ref>", String.Empty, RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            s = Regex.Replace(s, "<[^>]+>", String.Empty)

            Do While Regex.IsMatch(s, "\{\{[^\{\}]*\}\}")
                s = Regex.Replace(s, "\{\{[^\{\}]*\}\}", String.Empty)
            Loop

            s = Regex.Replace(s, "\[\[([^\]\|]*)\|([^\]]*)\]\]", "$2")
            s = Regex.Replace(s, "\[\[([^\]]*)\]\]", "$1")

            s = s.Replace("'''", "").Replace("''", "")
            s = Regex.Replace(s, "\s{2,}", " ")
            s = System.Net.WebUtility.HtmlDecode(s)

            Return s.Trim()
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.PulisciWikitext - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return inp
        End Try
    End Function

    Private Async Function AggiungiSantoDelGiornoFallback(sb As StringBuilder, oggi As DateTime) As Task
        Try
            Dim titoloPagina As String = Uri.EscapeDataString($"{oggi.Day}_{oggi.ToString("MMMM", New CultureInfo("it-IT"))}")
            Dim url As String = String.Format(WikipediaSummaryUrl, titoloPagina)
            Dim json As String = Await GetStringSafeAsync(url)

            If json Is Nothing Then
                sb.AppendLine("- Informazioni santi non disponibili")
                Return
            End If

            Dim obj As JObject = JObject.Parse(json)
            Dim estratto As String = If(obj("extract")?.ToString(), "")

            If Not String.IsNullOrWhiteSpace(estratto) Then
                Dim righe As String() = estratto.Split(New String() {Environment.NewLine, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Dim santiRiga As String = ""
                For Each riga As String In righe
                    Dim lr As String = riga.ToLowerInvariant()
                    If lr.Contains("onomastico") OrElse lr.Contains("santo") OrElse lr.Contains("santi") Then
                        santiRiga = riga.Trim()
                        Exit For
                    End If
                Next
                Dim testo As String = If(Not String.IsNullOrWhiteSpace(santiRiga), santiRiga, righe.FirstOrDefault())
                sb.AppendLine($"- {RimuoviTagHtml(testo)}")
            Else
                sb.AppendLine("- Informazioni santi non disponibili")
            End If
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiSantoDelGiornoFallback (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("- Errore di connessione")
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiSantoDelGiornoFallback - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("- Informazioni santi non disponibili")
        End Try
    End Function

    Private Function EstraiVociStoriche(arr As JArray) As List(Of VoceStorica)
        Dim risultato As New List(Of VoceStorica)()
        Try
            If arr Is Nothing Then Return risultato
            For Each token As JToken In arr
                Dim testo As String = RimuoviTagHtml(If(token("text")?.ToString(), ""))
                If String.IsNullOrWhiteSpace(testo) Then Continue For
                Dim anno As Integer = 0
                Dim annoStr As String = If(token("year")?.ToString(), "")
                Integer.TryParse(annoStr, NumberStyles.Integer, CultureInfo.InvariantCulture, anno)
                Dim voce As New VoceStorica With {.Anno = anno, .Testo = testo}
                risultato.Add(voce)
            Next
            risultato.Sort(AddressOf ConfrontaVociPerAnnoCrescente)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.EstraiVociStoriche - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function ConfrontaVociPerAnnoCrescente(a As VoceStorica, b As VoceStorica) As Integer
        Try
            Return a.Anno.CompareTo(b.Anno)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.ConfrontaVociPerAnnoCrescente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Private Function FormattaAnno(anno As Integer) As String
        Try
            If anno < 0 Then
                Return $"{Math.Abs(anno)} a.C."
            ElseIf anno = 0 Then
                Return "anno ignoto"
            Else
                Return anno.ToString()
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.FormattaAnno - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return anno.ToString()
        End Try
    End Function

    Private Async Function AggiungiAccaddeOggi(sb As StringBuilder, oggi As DateTime) As Task
        Try
            Dim ms As String = oggi.Month.ToString("00")
            Dim gg As String = oggi.Day.ToString("00")
            Dim json As String = Await _client.GetStringAsync(String.Format(WikipediaOnThisDayUrl, ms, gg))
            Dim obj As JObject = JObject.Parse(json)
            Dim eventi As JArray = TryCast(obj("events"), JArray)

            Dim voci As List(Of VoceStorica) = EstraiVociStoriche(eventi)
            If voci.Count = 0 Then
                sb.AppendLine("ACCADDE OGGI: Nessun evento disponibile")
                Return
            End If

            sb.AppendLine("ACCADDE OGGI:")
            Dim conteggio As Integer = 0
            For Each voce As VoceStorica In voci
                If conteggio >= MaxAccaddeOggi Then Exit For
                sb.AppendLine($"[{FormattaAnno(voce.Anno)}] {voce.Testo}")
                conteggio += 1
            Next
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiAccaddeOggi (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("ACCADDE OGGI: Errore di connessione")
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiAccaddeOggi - " & ex.Message & Environment.NewLine & ex.StackTrace)
            sb.AppendLine("ACCADDE OGGI: dati non disponibili")
        End Try
    End Function

    Private Async Function AggiungiCuriosita(sb As StringBuilder, oggi As DateTime) As Task
        Try
            Dim ms As String = oggi.Month.ToString("00")
            Dim gg As String = oggi.Day.ToString("00")
            Dim json As String = Await _client.GetStringAsync(String.Format(WikipediaSelectedUrl, ms, gg))
            Dim obj As JObject = JObject.Parse(json)
            Dim selezionati As JArray = TryCast(obj("selected"), JArray)

            Dim voci As List(Of VoceStorica) = EstraiVociStoriche(selezionati)
            If voci.Count = 0 Then
                Return
            End If

            sb.AppendLine("CURIOSITÀ E APPROFONDIMENTI:")
            Dim conteggio As Integer = 0
            For Each voce As VoceStorica In voci
                If conteggio >= MaxCuriosita Then Exit For
                sb.AppendLine($"Nel {FormattaAnno(voce.Anno)}: {voce.Testo}")
                conteggio += 1
            Next
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiCuriosita (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiCuriosita - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Function

    Private Async Function AggiungiNoteDelGiorno(sb As StringBuilder, oggi As DateTime) As Task
        Try
            Dim ms As String = oggi.Month.ToString("00")
            Dim gg As String = oggi.Day.ToString("00")

            Dim jsonN As String = Await _client.GetStringAsync(String.Format(WikipediaBirthsUrl, ms, gg))
            Dim nascite As JArray = TryCast(JObject.Parse(jsonN)("births"), JArray)
            Dim vociNascite As List(Of VoceStorica) = EstraiVociStoriche(nascite)
            If vociNascite.Count > 0 Then
                sb.AppendLine("NATI OGGI:")
                Dim cn As Integer = 0
                For Each voce As VoceStorica In vociNascite
                    If cn >= MaxNati Then Exit For
                    sb.AppendLine($"[{FormattaAnno(voce.Anno)}] {voce.Testo}")
                    cn += 1
                Next
                sb.AppendLine()
            End If

            Dim jsonM As String = Await _client.GetStringAsync(String.Format(WikipediaDeathsUrl, ms, gg))
            Dim morti As JArray = TryCast(JObject.Parse(jsonM)("deaths"), JArray)
            Dim vociMorti As List(Of VoceStorica) = EstraiVociStoriche(morti)
            If vociMorti.Count > 0 Then
                sb.AppendLine("MORTI OGGI:")
                Dim cm As Integer = 0
                For Each voce As VoceStorica In vociMorti
                    If cm >= MaxMorti Then Exit For
                    sb.AppendLine($"[{FormattaAnno(voce.Anno)}] {voce.Testo}")
                    cm += 1
                Next
            End If
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSAlmanacco.AggiungiNoteDelGiorno (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSAlmanacco.AggiungiNoteDelGiorno - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Function

    Private Function CalcolaSegnoZodiacale(dt As DateTime) As String
        Dim g As Integer = dt.Day
        Dim m As Integer = dt.Month
        If (m = 1 AndAlso g <= 20) OrElse (m = 12 AndAlso g >= 22) Then Return "Capricorno"
        If (m = 1 AndAlso g >= 21) OrElse (m = 2 AndAlso g <= 19) Then Return "Acquario"
        If (m = 2 AndAlso g >= 20) OrElse (m = 3 AndAlso g <= 20) Then Return "Pesci"
        If (m = 3 AndAlso g >= 21) OrElse (m = 4 AndAlso g <= 20) Then Return "Ariete"
        If (m = 4 AndAlso g >= 21) OrElse (m = 5 AndAlso g <= 20) Then Return "Toro"
        If (m = 5 AndAlso g >= 21) OrElse (m = 6 AndAlso g <= 21) Then Return "Gemelli"
        If (m = 6 AndAlso g >= 22) OrElse (m = 7 AndAlso g <= 22) Then Return "Cancro"
        If (m = 7 AndAlso g >= 23) OrElse (m = 8 AndAlso g <= 23) Then Return "Leone"
        If (m = 8 AndAlso g >= 24) OrElse (m = 9 AndAlso g <= 22) Then Return "Vergine"
        If (m = 9 AndAlso g >= 23) OrElse (m = 10 AndAlso g <= 22) Then Return "Bilancia"
        If (m = 10 AndAlso g >= 23) OrElse (m = 11 AndAlso g <= 22) Then Return "Scorpione"
        If (m = 11 AndAlso g >= 23) OrElse (m = 12 AndAlso g <= 21) Then Return "Sagittario"
        Return ""
    End Function

End Class