Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Le API di WordPress usate dal browser (lettura semplificata) e dai quotidiani: scaricare il JSON, leggere le informazioni di un articolo
''' o di una pagina e i commenti. Tutto di sola lettura, dalle API pubbliche del sito.
''' </summary>
Public NotInheritable Class WGWordPress

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _http As HttpClient = CreaHttp()

    Private Sub New()
    End Sub

    Private Shared Function CreaHttp() As HttpClient
        Dim c As New HttpClient()
        c.Timeout = TimeSpan.FromSeconds(25)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json")
        Return c
    End Function

    ''' <summary>L'indirizzo base delle API (con wp-json/ in fondo) di un sito, dato l'indirizzo del sito o delle sue API.</summary>
    Public Shared Function RadiceDaSito(urlSito As String) As String
        Dim s As String = If(urlSito, "").Trim().TrimEnd("/"c)
        s = Regex.Replace(s, "/wp-json(/wp/v2)?$", "", RegexOptions.IgnoreCase)
        Return s & "/wp-json/"
    End Function

    Public Shared Function UrlApi(radice As String, percorso As String, query As String) As String
        Dim u As String = radice.TrimEnd("/"c) & "/" & percorso
        If query <> "" Then u &= If(u.Contains("?"), "&", "?") & query
        Return u
    End Function

    ''' <summary>Il JSON di un indirizzo delle API e il numero totale di pagine (intestazione X-WP-TotalPages); Nothing se non risponde.</summary>
    Public Shared Async Function ScaricaJsonAsync(url As String) As Task(Of Tuple(Of JToken, Integer))
        Try
            Using risposta As HttpResponseMessage = Await _http.GetAsync(url).ConfigureAwait(True)
                If Not risposta.IsSuccessStatusCode Then Return Nothing
                Dim testo As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(True)
                Dim pagine As Integer = 1
                Dim valori As IEnumerable(Of String) = Nothing
                If risposta.Headers.TryGetValues("X-WP-TotalPages", valori) Then
                    For Each v As String In valori
                        Integer.TryParse(v, pagine)
                        Exit For
                    Next
                End If
                Return Tuple.Create(JToken.Parse(testo), Math.Max(1, pagine))
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGWordPress.ScaricaJsonAsync " & url & ": " & ex.GetType().Name & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Vero se il sito ha le API di WordPress funzionanti (risposta rapida: serve prima di aprire una pagina).</summary>
    Public Shared Async Function ApiFunzionaAsync(radice As String) As Task(Of Boolean)
        Try
            Using cancella As New System.Threading.CancellationTokenSource(6000)
                Using risposta As HttpResponseMessage = Await _http.GetAsync(UrlApi(radice, "wp/v2/posts", "per_page=1&_fields=id"), cancella.Token).ConfigureAwait(True)
                    If Not risposta.IsSuccessStatusCode Then Return False
                    Dim testo As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(True)
                    Return JToken.Parse(testo).Type = JTokenType.Array
                End Using
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>Il testo semplice di un pezzo di HTML (i titoli e i commenti di WordPress arrivano con tag ed entità).</summary>
    Public Shared Function TestoSemplice(html As String) As String
        If String.IsNullOrEmpty(html) Then Return ""
        Dim conAcapo As String = Regex.Replace(html, "<br\s*/?>|</(p|div|h[1-6]|li|blockquote)>", "$0" & vbLf, RegexOptions.IgnoreCase)
        Dim senzaTag As String = Regex.Replace(conAcapo, "<[^>]+>", "")
        Dim testo As String = WebUtility.HtmlDecode(senzaTag).Replace(ChrW(160), " ")
        testo = Regex.Replace(testo, "[ \t]+", " ")
        testo = Regex.Replace(testo, " ?(\r\n|\r|\n) ?", vbLf)
        testo = Regex.Replace(testo, "\n{3,}", vbLf & vbLf)
        Return testo.Trim().Replace(vbLf, Environment.NewLine)
    End Function

    Private Shared Function DataLeggibile(valore As String) As String
        Dim d As DateTime
        If DateTime.TryParse(valore, CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then
            Return d.ToString("d MMMM yyyy 'alle' HH:mm", CultureInfo.GetCultureInfo("it-IT"))
        End If
        Return ""
    End Function

    ''' <summary>
    ''' Le informazioni di un articolo o di una pagina: tipo, date, autore, categorie, etichette, indirizzo, numero di parole e commenti.
    ''' "rotta" è per esempio wp/v2/posts/12 o wp/v2/pages/7. Nothing se il sito non risponde.
    ''' </summary>
    Public Shared Async Function InformazioniAsync(radice As String, rotta As String) As Task(Of String)
        Try
            Dim r As Tuple(Of JToken, Integer) = Await ScaricaJsonAsync(UrlApi(radice, rotta, "_embed=author,wp:term&_fields=id,type,date,modified,link,comment_status,content,author,_embedded"))
            If r Is Nothing OrElse Not TypeOf r.Item1 Is JObject Then Return Nothing
            Dim p As JObject = CType(r.Item1, JObject)
            Dim righe As New List(Of String)()
            Dim tipo As String = Convert.ToString(p("type"))
            righe.Add("Tipo: " & If(tipo = "page", "pagina", If(tipo = "post", "articolo", If(tipo = "", "contenuto", tipo))))
            Dim pubblicato As String = DataLeggibile(Convert.ToString(p("date")))
            If pubblicato <> "" Then righe.Add("Pubblicato il " & pubblicato)
            Dim modificato As String = DataLeggibile(Convert.ToString(p("modified")))
            If modificato <> "" AndAlso modificato <> pubblicato Then righe.Add("Ultima modifica il " & modificato)
            Dim incorporato As JToken = p("_embedded")
            If incorporato IsNot Nothing Then
                Dim autori As JToken = incorporato("author")
                If autori IsNot Nothing AndAlso autori.Type = JTokenType.Array AndAlso autori.Count > 0 Then
                    Dim nomeAutore As String = TestoSemplice(Convert.ToString(autori(0)("name")))
                    If nomeAutore <> "" Then righe.Add("Autore: " & nomeAutore)
                End If
                Dim termini As JToken = incorporato("wp:term")
                If termini IsNot Nothing AndAlso termini.Type = JTokenType.Array Then
                    Dim categorie As New List(Of String)()
                    For Each gruppo As JToken In termini
                        If gruppo.Type <> JTokenType.Array Then Continue For
                        For Each t As JToken In gruppo
                            Dim nome As String = TestoSemplice(Convert.ToString(t("name")))
                            If nome = "" Then Continue For
                            If Convert.ToString(t("taxonomy")) = "category" Then categorie.Add(nome)
                        Next
                    Next
                    If categorie.Count > 0 Then righe.Add(If(categorie.Count = 1, "Categoria: ", "Categorie: ") & String.Join(", ", categorie))
                End If
            End If
            Dim testo As String = TestoSemplice(Convert.ToString(If(p("content") IsNot Nothing, p("content")("rendered"), "")))
            If testo <> "" Then
                Dim parole As Integer = Regex.Matches(testo, "\S+").Count
                Dim minuti As Integer = Math.Max(1, CInt(Math.Round(parole / 200.0)))
                righe.Add(parole.ToString(CultureInfo.InvariantCulture) & " parole, circa " & minuti.ToString(CultureInfo.InvariantCulture) & If(minuti = 1, " minuto", " minuti") & " di lettura")
            End If
            Dim link As String = Convert.ToString(p("link"))
            If link <> "" Then righe.Add("Indirizzo: " & link)
            Dim stato As String = Convert.ToString(p("comment_status"))
            Dim id As String = Convert.ToString(p("id"))
            Dim quanti As Integer = Await ContaCommentiAsync(radice, id)
            If quanti >= 0 Then
                righe.Add("Commenti: " & If(quanti = 0, "nessuno", quanti.ToString(CultureInfo.InvariantCulture)) & If(stato = "closed", ", i commenti sono chiusi", ""))
            ElseIf stato = "closed" Then
                righe.Add("I commenti sono chiusi")
            End If
            Return String.Join(Environment.NewLine, righe)
        Catch ex As Exception
            _log.ScriviLog("WGWordPress.InformazioniAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ''' <summary>Quanti commenti ha un articolo o una pagina; -1 se non si sa (API dei commenti non disponibile).</summary>
    Public Shared Async Function ContaCommentiAsync(radice As String, idPost As String) As Task(Of Integer)
        Try
            Using risposta As HttpResponseMessage = Await _http.GetAsync(UrlApi(radice, "wp/v2/comments", "post=" & Uri.EscapeDataString(idPost) & "&per_page=1&_fields=id")).ConfigureAwait(True)
                If Not risposta.IsSuccessStatusCode Then Return -1
                Dim valori As IEnumerable(Of String) = Nothing
                If risposta.Headers.TryGetValues("X-WP-Total", valori) Then
                    For Each v As String In valori
                        Dim n As Integer
                        If Integer.TryParse(v, n) Then Return n
                    Next
                End If
                Return -1
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGWordPress.ContaCommentiAsync: " & ex.Message)
            Return -1
        End Try
    End Function

    ''' <summary>Un commento di un articolo o di una pagina.</summary>
    Public NotInheritable Class CommentoWp
        Public Property Autore As String = ""
        Public Property Data As String = ""
        Public Property InRispostaA As String = ""
        Public Property Testo As String = ""
    End Class

    ''' <summary>Oltre questa lunghezza un commento nell'elenco si accorcia e si legge per intero in un campo di testo, come i commenti di YouTube.</summary>
    Private Const SogliaTestoLungo As Integer = 200

    ''' <summary>
    ''' I commenti di un articolo o di una pagina, dal più vecchio. Un elenco vuoto se non ce ne sono; Nothing se non si possono leggere.
    ''' </summary>
    Public Shared Async Function CommentiAsync(radice As String, idPost As String) As Task(Of List(Of CommentoWp))
        Try
            Dim tutti As New List(Of JToken)()
            Dim pagina As Integer = 1
            Dim totalePagine As Integer = 1
            Do
                Dim r As Tuple(Of JToken, Integer) = Await ScaricaJsonAsync(UrlApi(radice, "wp/v2/comments", "post=" & Uri.EscapeDataString(idPost) & "&per_page=100&page=" & pagina & "&order=asc&orderby=date&_fields=id,parent,author_name,date,content"))
                If r Is Nothing OrElse Not TypeOf r.Item1 Is JArray Then
                    If pagina = 1 Then Return Nothing
                    Exit Do
                End If
                totalePagine = r.Item2
                For Each c As JToken In CType(r.Item1, JArray)
                    tutti.Add(c)
                Next
                pagina += 1
            Loop While pagina <= totalePagine AndAlso pagina <= 5
            Dim risultato As New List(Of CommentoWp)()
            Dim autori As New Dictionary(Of String, String)()
            For Each c As JToken In tutti
                Dim nomeC As String = TestoSemplice(Convert.ToString(c("author_name")))
                autori(Convert.ToString(c("id"))) = If(nomeC = "", "Un lettore", nomeC)
            Next
            For Each c As JToken In tutti
                Dim commento As New CommentoWp()
                commento.Autore = autori(Convert.ToString(c("id")))
                commento.Data = DataLeggibile(Convert.ToString(c("date")))
                Dim padre As String = Convert.ToString(c("parent"))
                Dim nomePadre As String = Nothing
                If padre <> "" AndAlso padre <> "0" AndAlso autori.TryGetValue(padre, nomePadre) Then commento.InRispostaA = nomePadre
                commento.Testo = TestoSemplice(Convert.ToString(c("content")("rendered")))
                risultato.Add(commento)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog("WGWordPress.CommentiAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function EtichettaCommento(c As CommentoWp, numero As Integer, totale As Integer) As String
        Dim intestazione As String = "Commento " & numero.ToString(CultureInfo.InvariantCulture) & " di " & totale.ToString(CultureInfo.InvariantCulture) & ", " & c.Autore
        If c.Data <> "" Then intestazione &= ", " & c.Data
        If c.InRispostaA <> "" Then intestazione &= ", in risposta a " & c.InRispostaA
        Dim testo As String = c.Testo.Replace(vbCr, " ").Replace(vbLf, " ")
        If testo.Length > SogliaTestoLungo Then testo = testo.Substring(0, SogliaTestoLungo) & "..."
        Return intestazione & ": " & testo
    End Function

    ''' <summary>
    ''' L'elenco dei commenti, uno per voce (i lunghi accorciati). Invio su un commento lo apre per intero in un campo di testo di sola lettura;
    ''' nelle opzioni c'è anche la lettura di tutti i commenti in un campo solo.
    ''' </summary>
    Public Shared Sub MostraCommenti(titoloArticolo As String, commenti As List(Of CommentoWp))
        Try
            If commenti Is Nothing OrElse commenti.Count = 0 Then Return
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Commenti: " & titoloArticolo)
            For i As Integer = 0 To commenti.Count - 1
                lista.aggiungiElemento(EtichettaCommento(commenti(i), i + 1, commenti.Count), i.ToString(CultureInfo.InvariantCulture))
            Next
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        Dim n As Integer
                        If Not Integer.TryParse(id, n) OrElse n < 0 OrElse n >= commenti.Count Then Return
                        Dim c As CommentoWp = commenti(n)
                        Dim intestazione As String = c.Autore & If(c.Data <> "", Environment.NewLine & c.Data, "") & If(c.InRispostaA <> "", Environment.NewLine & "In risposta a " & c.InRispostaA, "")
                        Dim campo As New WGCampo()
                        campo.init("Commento di " & c.Autore, intestazione & Environment.NewLine & Environment.NewLine & c.Testo, True, True)
                        campo.avvia()
                    Catch ex As Exception
                        _log.ScriviLog("WGWordPress.MostraCommenti (voce): " & ex.Message)
                    End Try
                End Sub
            lista.aggiungiOpzione("Leggi tutti i commenti in un campo di testo",
                Sub(s, e)
                    Try
                        Dim sb As New StringBuilder()
                        For i As Integer = 0 To commenti.Count - 1
                            Dim c As CommentoWp = commenti(i)
                            sb.AppendLine("Commento " & (i + 1).ToString(CultureInfo.InvariantCulture) & " di " & commenti.Count.ToString(CultureInfo.InvariantCulture) & ", " & c.Autore & If(c.Data <> "", ", " & c.Data, "") & If(c.InRispostaA <> "", ", in risposta a " & c.InRispostaA, "") & ".")
                            sb.AppendLine(c.Testo)
                            sb.AppendLine()
                        Next
                        Dim campo As New WGCampo()
                        campo.init("Commenti: " & titoloArticolo, sb.ToString().Trim(), True, True)
                        campo.avvia()
                    Catch ex As Exception
                        _log.ScriviLog("WGWordPress.MostraCommenti (tutti): " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("WGWordPress.MostraCommenti: " & ex.ToString())
        End Try
    End Sub

End Class
