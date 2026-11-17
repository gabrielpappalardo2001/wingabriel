Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions

Imports Newtonsoft.Json.Linq

Imports wgcontrols

Friend Class WikipediaAPI
    Private Const apiUrl As String = "https://it.wikipedia.org/w/api.php"
    Private Const indirizzoPagina As String = "https://it.wikipedia.org/?curid="
    Public Event nontrovato As EventHandler
    Public Event RicercaTerminata As EventHandler(Of List(Of Wikipedia))

    ' Sezioni finali che non servono alla lettura della voce intera (restano comunque consultabili dall'indice).
    Private Shared ReadOnly SezioniNonDiLettura As String() = {
        "Note", "Annotazioni", "Bibliografia", "Fonti", "Riferimenti", "Collegamenti esterni", "Altri progetti", "Voci correlate", "Vedi anche"
    }

    Private ReadOnly _logger As Logger
    Private Shared ReadOnly _httpClient As HttpClient = CreaClient()

    Private Shared Function CreaClient() As HttpClient
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Catch
        End Try
        Dim gestore As New HttpClientHandler()
        gestore.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
        Dim client As New HttpClient(gestore)
        client.Timeout = TimeSpan.FromSeconds(20)
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Wikipedia/2.0 (accessibilita; postmaster@wingabriel.net)")
        Return client
    End Function

    Public Sub New()
        Try
            _logger = New Logger()
        Catch ex As Exception
        End Try
    End Sub

    Private Sub Scrivi(messaggio As String)
        Try
            If _logger IsNot Nothing Then _logger.ScriviLog(messaggio)
        Catch
        End Try
    End Sub

    Private Sub MostraErrore(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            Scrivi("MostraErrore: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Scarica il testo di un indirizzo; un solo nuovo tentativo se la rete ha un intoppo passeggero.</summary>
    Private Function DownloadString(url As String) As String
        For tentativo As Integer = 1 To 2
            Try
                Return _httpClient.GetStringAsync(url).ConfigureAwait(False).GetAwaiter().GetResult()
            Catch ex As HttpRequestException When tentativo < 2
                Threading.Thread.Sleep(600)
            Catch ex As TaskCanceledException
                Throw New HttpRequestException("tempo di attesa scaduto")
            End Try
        Next
        Return ""
    End Function

    Public Sub SearchTerm(term As String)
        Try
            If String.IsNullOrWhiteSpace(term) Then
                RaiseEvent nontrovato(Me, Nothing)
                Return
            End If

            Dim results As New List(Of Wikipedia)
            Dim url As String = $"{apiUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(term.Trim())}&srnamespace=0&srlimit=20&srprop=snippet&format=json&formatversion=2"

            Dim response As String = DownloadString(url)
            Dim json As JObject = JObject.Parse(response)

            Dim erroreApi As String = json("error")?("info")?.ToString()
            If Not String.IsNullOrWhiteSpace(erroreApi) Then
                RaiseEvent nontrovato(Me, Nothing)
                Return
            End If

            Dim searchResults As JArray = TryCast(json("query")?("search"), JArray)
            If searchResults IsNot Nothing Then
                For Each result As JToken In searchResults
                    Try
                        Dim title As String = result("title")?.ToString()
                        Dim pageId As String = result("pageid")?.ToString()
                        If String.IsNullOrWhiteSpace(title) OrElse String.IsNullOrWhiteSpace(pageId) Then Continue For

                        results.Add(New Wikipedia With {
                            .Title = title,
                            .Id = pageId,
                            .Snippet = PulisciSnippet(result("snippet")?.ToString())
                        })
                    Catch exRiga As Exception
                        Scrivi("SearchTerm (riga risultato): " & exRiga.Message)
                    End Try
                Next
            End If

            If results.Count > 0 Then
                RaiseEvent RicercaTerminata(Me, results)
            Else
                RaiseEvent nontrovato(Me, Nothing)
            End If
        Catch ex As HttpRequestException
            MostraErrore("Errore di rete durante la ricerca: " & ex.Message)
            Scrivi("HttpRequestException in SearchTerm: " & ex.Message & " StackTrace: " & ex.StackTrace)
            RaiseEvent nontrovato(Me, Nothing)
        Catch ex As Exception
            MostraErrore("Si è verificato un errore inatteso durante la ricerca: " & ex.Message)
            Scrivi("Exception in SearchTerm: " & ex.Message & " StackTrace: " & ex.StackTrace)
            RaiseEvent nontrovato(Me, Nothing)
        End Try
    End Sub

    Private Function PulisciSnippet(snippetGrezzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(snippetGrezzo) Then Return ""
            Dim s As String = Regex.Replace(snippetGrezzo, "</?span[^>]*>", "")
            s = System.Net.WebUtility.HtmlDecode(s)
            s = Regex.Replace(s, "\s+", " ").Trim()
            Return s
        Catch ex As Exception
            Scrivi("Exception in PulisciSnippet: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Legge l'html dalla risposta di action=parse (sia con formatversion 2, dove è una stringa, sia con la 1, dove è in "*").</summary>
    Private Function HtmlDaRisposta(json As JObject) As String
        Try
            Dim testo As JToken = json("parse")?("text")
            If testo Is Nothing Then Return ""
            If testo.Type = JTokenType.String Then Return testo.ToString()
            Return testo("*")?.ToString()
        Catch ex As Exception
            Scrivi("HtmlDaRisposta: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Scarica una voce intera (sectionId vuoto) o una sola sezione ("0" = introduzione) e la fa pulire da WGLettoreArticoli.
    ''' Restituisce il testo per il campo di testo e la pagina html per il browser, entrambi senza note, riquadri e navigazione.
    ''' </summary>
    Public Function GetContenuto(pageId As String, Optional sectionId As String = Nothing, Optional titoloSezione As String = "") As WikipediaContenuto
        Dim contenuto As New WikipediaContenuto()
        Try
            If String.IsNullOrWhiteSpace(pageId) Then Return contenuto
            Dim intera As Boolean = String.IsNullOrWhiteSpace(sectionId)

            Dim url As String = $"{apiUrl}?action=parse&pageid={Uri.EscapeDataString(pageId)}&prop=text&format=json&formatversion=2&redirects=1&disabletoc=1&disableeditsection=1&disablelimitreport=1"
            If Not intera Then url &= "&section=" & Uri.EscapeDataString(sectionId)

            Dim json As JObject = JObject.Parse(DownloadString(url))
            If Not String.IsNullOrWhiteSpace(json("error")?("info")?.ToString()) Then Return contenuto

            Dim html As String = HtmlDaRisposta(json)
            If String.IsNullOrWhiteSpace(html) Then Return contenuto

            Dim titolo As String = If(json("parse")?("title")?.ToString(), "")
            If Not intera AndAlso Not String.IsNullOrWhiteSpace(titoloSezione) Then titolo = (titolo & " - " & titoloSezione).Trim(" "c, "-"c)

            Dim opzioni As New OpzioniEstrazione() With {
                .ContenutoIsolato = True,
                .Titolo = titolo,
                .Sito = "Wikipedia",
                .Lingua = "it"
            }
            If intera Then opzioni.SezioniDaEscludere = SezioniNonDiLettura

            contenuto.Indirizzo = indirizzoPagina & pageId
            Dim articolo As ArticoloEstratto = WGLettoreArticoli.EstrazioneSemplice(contenuto.Indirizzo, html, opzioni)
            contenuto.Titolo = titolo
            contenuto.Testo = articolo.TestoPerLettura()
            If Not String.IsNullOrWhiteSpace(articolo.Corpo) Then contenuto.Html = WGLettoreArticoli.DocumentoHtml(articolo)
        Catch ex As HttpRequestException
            contenuto.Errore = True
            MostraErrore("Errore di rete durante il recupero del contenuto della pagina: " & ex.Message)
            Scrivi("HttpRequestException in GetContenuto: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            contenuto.Errore = True
            MostraErrore("Si è verificato un errore inatteso durante il recupero del contenuto della pagina: " & ex.Message)
            Scrivi("Exception in GetContenuto: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
        End Try
        Return contenuto
    End Function

    Public Function GetPageContent(pageId As String) As String
        If String.IsNullOrWhiteSpace(pageId) Then Return "Pagina non trovata."
        Dim contenuto As WikipediaContenuto = GetContenuto(pageId)
        If contenuto.Errore Then Return ""
        If String.IsNullOrWhiteSpace(contenuto.Testo) Then Return "Il contenuto della pagina è vuoto."
        Return contenuto.Testo
    End Function

    Public Function GetSectionText(pageId As String, sectionId As String) As String
        If String.IsNullOrWhiteSpace(pageId) OrElse String.IsNullOrWhiteSpace(sectionId) Then Return ""
        Return GetContenuto(pageId, sectionId).Testo
    End Function

    ''' <summary>Pulisce la risposta json di action=parse (compatibilità con il codice esistente).</summary>
    Public Function CleanAndConvertHtml(response As String) As String
        Try
            If String.IsNullOrWhiteSpace(response) Then
                Return "Errore: contenuto HTML non trovato nella risposta."
            End If

            Dim json As JObject = JObject.Parse(response)
            If Not String.IsNullOrWhiteSpace(json("error")?("info")?.ToString()) Then
                Return "Errore: contenuto HTML non trovato nella risposta."
            End If

            Dim html As String = HtmlDaRisposta(json)
            If String.IsNullOrWhiteSpace(html) Then
                Return "Errore: contenuto HTML non trovato nella risposta."
            End If

            Return GetPlainTextFromHtml(html)
        Catch ex As Exception
            Scrivi("Exception in CleanAndConvertHtml: " & ex.Message & " StackTrace: " & ex.StackTrace)
            Return "Si è verificato un errore durante la pulizia e conversione dell'HTML."
        End Try
    End Function

    Public Function AddNewLineAfterPeriod(text As String) As String
        Try
            Dim result As New StringBuilder()
            For Each ch As Char In text
                result.Append(ch)
                If ch = "."c Then
                    result.Append(Environment.NewLine)
                End If
            Next
            Return result.ToString()
        Catch ex As Exception
            Scrivi("Exception in AddNewLineAfterPeriod: " & ex.Message & " StackTrace: " & ex.StackTrace)
            Return text
        End Try
    End Function

    Public Function GetPageSections(pageId As String) As List(Of WikipediaSection)
        Dim sections As New List(Of WikipediaSection)
        Try
            If String.IsNullOrWhiteSpace(pageId) Then
                Return sections
            End If

            Dim url As String = $"{apiUrl}?action=parse&pageid={Uri.EscapeDataString(pageId)}&prop=sections&format=json&redirects=1"
            Dim json As JObject = JObject.Parse(DownloadString(url))

            If Not String.IsNullOrWhiteSpace(json("error")?("info")?.ToString()) Then
                Return sections
            End If

            Dim sectionList As JArray = TryCast(json("parse")?("sections"), JArray)
            If sectionList IsNot Nothing Then
                For Each section As JToken In sectionList
                    Try
                        Dim sectionTitle As String = section("line")?.ToString()
                        Dim sectionId As String = section("index")?.ToString()
                        If String.IsNullOrWhiteSpace(sectionTitle) OrElse String.IsNullOrWhiteSpace(sectionId) Then Continue For
                        ' Le sezioni che arrivano da modelli (indice "T-1") non si possono richiedere da sole.
                        Dim numero As Integer
                        If Not Integer.TryParse(sectionId, numero) Then Continue For

                        Dim livello As Integer = 1
                        Integer.TryParse(section("toclevel")?.ToString(), livello)
                        sections.Add(New WikipediaSection With {
                            .Title = Regex.Replace(System.Net.WebUtility.HtmlDecode(sectionTitle), "<[^>]+>", "").Trim(),
                            .Id = sectionId,
                            .Livello = Math.Max(1, livello),
                            .Numero = If(section("number")?.ToString(), "")
                        })
                    Catch exRiga As Exception
                        Scrivi("GetPageSections (riga sezione): " & exRiga.Message)
                    End Try
                Next
            End If

            Return sections
        Catch ex As HttpRequestException
            MostraErrore("Errore di rete durante il recupero delle sezioni della pagina: " & ex.Message)
            Scrivi("HttpRequestException in GetPageSections: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
            Return sections
        Catch ex As Exception
            MostraErrore("Si è verificato un errore inatteso durante il recupero delle sezioni della pagina: " & ex.Message)
            Scrivi("Exception in GetPageSections: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
            Return sections
        End Try
    End Function

    Public Function GetPageHtml(pageId As String) As String
        Try
            If String.IsNullOrWhiteSpace(pageId) Then
                Return "Pagina non trovata."
            End If

            Dim url As String = $"{apiUrl}?action=parse&pageid={Uri.EscapeDataString(pageId)}&prop=text&format=json&formatversion=2&redirects=1"
            Dim json As JObject = JObject.Parse(DownloadString(url))

            If Not String.IsNullOrWhiteSpace(json("error")?("info")?.ToString()) Then
                Return "Pagina non trovata."
            End If

            Dim htmlText As String = HtmlDaRisposta(json)
            If String.IsNullOrWhiteSpace(htmlText) Then
                Return "Pagina non trovata."
            End If

            Return htmlText
        Catch ex As HttpRequestException
            Scrivi("HttpRequestException in GetPageHtml: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
            Return "Errore di rete durante il recupero dell'HTML della pagina."
        Catch ex As Exception
            Scrivi("Exception in GetPageHtml: " & ex.Message & " PageId: " & pageId & " StackTrace: " & ex.StackTrace)
            Return "Si è verificato un errore inatteso durante il recupero dell'HTML della pagina."
        End Try
    End Function

    Public Function GetPlainTextFromHtml(html As String) As String
        Try
            Dim opzioni As New OpzioniEstrazione() With {.ContenutoIsolato = True}
            Return WGLettoreArticoli.EstrazioneSemplice("", html, opzioni).TestoPerLettura()
        Catch ex As Exception
            Scrivi("Exception in GetPlainTextFromHtml: " & ex.Message & " StackTrace: " & ex.StackTrace)
            Return ""
        End Try
    End Function
End Class

Public Class Wikipedia
    Public Property Title As String
    Public Property Id As String
    Public Property Snippet As String = ""
End Class

Public Class WikipediaSection
    Public Property Title As String
    Public Property Id As String
    Public Property Livello As Integer = 1
    Public Property Numero As String = ""

    ''' <summary>Numero e titolo, per esempio "1.2 Storia".</summary>
    Public ReadOnly Property Etichetta As String
        Get
            Return (If(Numero, "") & " " & If(Title, "")).Trim()
        End Get
    End Property
End Class

''' <summary>Contenuto già pulito di una voce o di una sezione di Wikipedia.</summary>
Public Class WikipediaContenuto
    Public Property Titolo As String = ""
    ''' <summary>Testo strutturato per il campo di testo.</summary>
    Public Property Testo As String = ""
    ''' <summary>Pagina html completa per il browser.</summary>
    Public Property Html As String = ""
    Public Property Indirizzo As String = ""
    ''' <summary>True se il recupero è fallito (l'errore è già stato mostrato).</summary>
    Public Property Errore As Boolean = False
End Class