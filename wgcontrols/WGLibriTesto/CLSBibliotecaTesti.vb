Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb

Public Enum FonteLibro
    Wikisource
End Enum

Public Class LibroTesto

    Public Titolo As String
    Public Autore As String
    Public Genere As String
    Public Fonte As FonteLibro
    Public TitoloPaginaWikisource As String
    Public HaCapitoli As Boolean
    Public Lingua As String

    Public Sub New()
        Titolo = ""
        Autore = ""
        Genere = ""
        TitoloPaginaWikisource = ""
        HaCapitoli = False
        Lingua = "it"
    End Sub

End Class

Public Class CapitoloLibro

    Public Titolo As String
    Public TitoloPaginaWikisource As String

    Public Sub New()
        Titolo = ""
        TitoloPaginaWikisource = ""
    End Sub

End Class

Public Class CLSBibliotecaTesti

    Public Shared PercorsoDatabase As String = RisolviPercorsoDatabaseTesti()

    Private Const UrlBaseWikisource As String = "https://it.wikisource.org/w/api.php"

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _database As New CLSBibliotecaTestiDatabase(PercorsoDatabase)
    Private Shared ReadOnly _client As HttpClient

    Private Shared Function RisolviPercorsoDatabaseTesti() As String
        Try
            Dim percorso As String = WGDatabase.PercorsoFileDati("Libri", "libritesto.db")
            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch
        End Try
        Try
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati\Libri", "libritesto.db")
        Catch
            Return "libritesto.db"
        End Try
    End Function

    Shared Sub New()
        Try
            _client = New HttpClient()
            _client.Timeout = TimeSpan.FromSeconds(30)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-BibliotecaTesti/1.0 (accessibilita; postmaster@wingabriel.net)")
            _client.DefaultRequestHeaders.Add("Accept", "application/json")
        Catch ex As Exception

        End Try
    End Sub

    Public Function CercaEntrambe(query As String) As List(Of LibroTesto)
        Try
            Return CercaWikisource(query)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.CercaEntrambe: " & ex.ToString())
            Return New List(Of LibroTesto)
        End Try
    End Function

    Public Function CercaWikisource(query As String) As List(Of LibroTesto)
        Dim risultati As New List(Of LibroTesto)
        Try
            If String.IsNullOrWhiteSpace(query) Then Return risultati

            Dim url As String = UrlBaseWikisource & "?action=query&list=search&srsearch=" & Uri.EscapeDataString(query.Trim()) & "&srlimit=20&srnamespace=0&format=json&formatversion=2"
            Dim risposta As String = ScaricaStringa(url)
            If String.IsNullOrWhiteSpace(risposta) Then Return risultati

            Dim json As JObject = JObject.Parse(risposta)

            Dim erroreApi As String = json("error")?("info")?.ToString()
            If Not String.IsNullOrWhiteSpace(erroreApi) Then
                Return risultati
            End If

            Dim ricerca As JArray = TryCast(json("query")?("search"), JArray)
            If ricerca Is Nothing Then Return risultati

            Dim titoliGiaAggiunti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each elemento As JObject In ricerca
                Try
                    Dim titoloPagina As String = elemento("title")?.ToString()
                    If String.IsNullOrWhiteSpace(titoloPagina) Then Continue For

                    Dim posizioneSlash As Integer = titoloPagina.IndexOf("/")
                    Dim haCapitoli As Boolean = False
                    Dim titoloRadice As String = titoloPagina
                    If posizioneSlash > 0 Then
                        titoloRadice = titoloPagina.Substring(0, posizioneSlash)
                        haCapitoli = True
                    End If

                    If titoliGiaAggiunti.Contains(titoloRadice) Then Continue For
                    titoliGiaAggiunti.Add(titoloRadice)

                    Dim libro As New LibroTesto()
                    libro.Fonte = FonteLibro.Wikisource
                    libro.Titolo = titoloRadice
                    libro.TitoloPaginaWikisource = titoloRadice
                    libro.Autore = "Wikisource"
                    libro.Lingua = "it"
                    libro.HaCapitoli = haCapitoli
                    risultati.Add(libro)
                Catch exInterno As Exception
                    _logger.ScriviLog("CLSBibliotecaTesti.CercaWikisource (elemento): " & exInterno.ToString())
                End Try
            Next

            RimuoviPagineNonValide(risultati)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.CercaWikisource: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Private Sub RimuoviPagineNonValide(libri As List(Of LibroTesto))
        Try
            If libri Is Nothing OrElse libri.Count = 0 Then Return

            Dim titoli As New List(Of String)
            For Each libro As LibroTesto In libri
                If Not String.IsNullOrWhiteSpace(libro.TitoloPaginaWikisource) Then
                    titoli.Add(libro.TitoloPaginaWikisource)
                End If
            Next
            If titoli.Count = 0 Then Return

            Dim daEscludere As HashSet(Of String) = OttieniPagineDaEscludere(titoli)
            If daEscludere.Count = 0 Then Return

            Dim indice As Integer = libri.Count - 1
            While indice >= 0
                If daEscludere.Contains(libri(indice).TitoloPaginaWikisource) Then
                    libri.RemoveAt(indice)
                End If
                indice -= 1
            End While
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.RimuoviPagineNonValide: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniPagineDaEscludere(titoli As List(Of String)) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            If titoli Is Nothing OrElse titoli.Count = 0 Then Return risultato

            Dim indice As Integer = 0
            While indice < titoli.Count
                Dim quantitaLotto As Integer = Math.Min(50, titoli.Count - indice)
                Dim lotto As List(Of String) = titoli.GetRange(indice, quantitaLotto)
                Dim titoliUniti As String = String.Join("|", lotto)

                Dim url As String = UrlBaseWikisource & "?action=query&prop=pageprops|info&ppprop=disambiguation&titles=" & Uri.EscapeDataString(titoliUniti) & "&format=json&formatversion=2"
                Dim risposta As String = ScaricaStringa(url)
                If Not String.IsNullOrWhiteSpace(risposta) Then
                    Dim json As JObject = JObject.Parse(risposta)
                    Dim pagine As JArray = TryCast(json("query")?("pages"), JArray)
                    If pagine IsNot Nothing Then
                        For Each pagina As JObject In pagine
                            Try
                                Dim titoloPagina As String = pagina("title")?.ToString()
                                If String.IsNullOrWhiteSpace(titoloPagina) Then Continue For

                                Dim eDaEscludere As Boolean = False

                                Dim props As JObject = TryCast(pagina("pageprops"), JObject)
                                If props IsNot Nothing AndAlso props("disambiguation") IsNot Nothing Then
                                    eDaEscludere = True
                                End If

                                Dim lingua As String = pagina("pagelanguage")?.ToString()
                                If Not String.IsNullOrWhiteSpace(lingua) AndAlso Not lingua.StartsWith("it", StringComparison.OrdinalIgnoreCase) Then
                                    eDaEscludere = True
                                End If

                                If eDaEscludere Then risultato.Add(titoloPagina)
                            Catch exPagina As Exception
                                _logger.ScriviLog("CLSBibliotecaTesti.OttieniPagineDaEscludere (pagina): " & exPagina.ToString())
                            End Try
                        Next
                    End If
                End If

                indice += quantitaLotto
            End While
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniPagineDaEscludere: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniCapitoli(libro As LibroTesto) As List(Of CapitoloLibro)
        Dim risultati As New List(Of CapitoloLibro)
        Try
            If libro Is Nothing OrElse String.IsNullOrWhiteSpace(libro.TitoloPaginaWikisource) Then Return risultati

            Dim continuazione As String = ""
            Dim contatorePagine As Integer = 0

            Do
                Dim url As String = UrlBaseWikisource & "?action=query&generator=allpages&gapprefix=" & Uri.EscapeDataString(libro.TitoloPaginaWikisource & "/") & "&gapnamespace=0&gaplimit=500&prop=info&format=json&formatversion=2"
                If Not String.IsNullOrWhiteSpace(continuazione) Then
                    url &= "&gapcontinue=" & Uri.EscapeDataString(continuazione)
                End If

                Dim risposta As String = ScaricaStringa(url)
                If String.IsNullOrWhiteSpace(risposta) Then Exit Do

                Dim json As JObject = JObject.Parse(risposta)
                Dim pagine As JArray = TryCast(json("query")?("pages"), JArray)
                If pagine IsNot Nothing Then
                    For Each elemento As JObject In pagine
                        Try
                            Dim titoloCompleto As String = elemento("title")?.ToString()
                            If String.IsNullOrWhiteSpace(titoloCompleto) Then Continue For

                            Dim lingua As String = elemento("pagelanguage")?.ToString()
                            If Not String.IsNullOrWhiteSpace(lingua) AndAlso Not lingua.StartsWith("it", StringComparison.OrdinalIgnoreCase) Then
                                Continue For
                            End If

                            Dim capitolo As New CapitoloLibro()
                            capitolo.TitoloPaginaWikisource = titoloCompleto
                            Dim posizioneSlash As Integer = titoloCompleto.IndexOf("/")
                            If posizioneSlash >= 0 AndAlso posizioneSlash < titoloCompleto.Length - 1 Then
                                capitolo.Titolo = titoloCompleto.Substring(posizioneSlash + 1)
                            Else
                                capitolo.Titolo = titoloCompleto
                            End If
                            risultati.Add(capitolo)
                        Catch exInterno As Exception
                            _logger.ScriviLog("CLSBibliotecaTesti.OttieniCapitoli (elemento): " & exInterno.ToString())
                        End Try
                    Next
                End If

                continuazione = json("continue")?("gapcontinue")?.ToString()
                contatorePagine += 1
            Loop While Not String.IsNullOrWhiteSpace(continuazione) AndAlso contatorePagine < 20

            risultati.Sort(AddressOf ConfrontaCapitoli)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniCapitoli: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Private Function ConfrontaCapitoli(a As CapitoloLibro, b As CapitoloLibro) As Integer
        Try
            Dim numeroA As Integer = EstraiPrimoNumero(a.Titolo)
            Dim numeroB As Integer = EstraiPrimoNumero(b.Titolo)

            If numeroA >= 0 AndAlso numeroB >= 0 AndAlso numeroA <> numeroB Then
                Return numeroA.CompareTo(numeroB)
            End If

            Return String.Compare(a.Titolo, b.Titolo, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ConfrontaCapitoli: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function EstraiPrimoNumero(testo As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return -1

            Dim corrispondenzaArabo As Match = Regex.Match(testo, "\d+")
            If corrispondenzaArabo.Success Then
                Dim numero As Integer
                If Integer.TryParse(corrispondenzaArabo.Value, numero) Then Return numero
            End If

            Dim testoMinuscolo As String = testo.ToLowerInvariant()
            Dim paroleContesto As String() = {"capitolo", "libro", "parte", "atto", "canto", "cap.", "cap ", "scena", "volume"}
            Dim haContesto As Boolean = False
            For Each parolaContesto As String In paroleContesto
                If testoMinuscolo.Contains(parolaContesto) Then
                    haContesto = True
                    Exit For
                End If
            Next
            If Not haContesto Then Return -1

            Dim parole As String() = testo.Trim().Split(New Char() {" "c, "-"c, "."c, ":"c}, StringSplitOptions.RemoveEmptyEntries)
            If parole.Length = 0 Then Return -1

            Dim ultimaParola As String = parole(parole.Length - 1)
            If Regex.IsMatch(ultimaParola, "^[MDCLXVImdclxvi]+$") Then
                Dim valoreRomano As Integer = ConvertiNumeroRomano(ultimaParola)
                If valoreRomano > 0 Then Return valoreRomano
            End If

            Return -1
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.EstraiPrimoNumero: " & ex.ToString())
            Return -1
        End Try
    End Function

    Private Function ConvertiNumeroRomano(testo As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return -1

            Dim valori As New Dictionary(Of Char, Integer) From {
                {"I"c, 1}, {"V"c, 5}, {"X"c, 10}, {"L"c, 50}, {"C"c, 100}, {"D"c, 500}, {"M"c, 1000}
            }
            Dim testoMaiuscolo As String = testo.ToUpperInvariant()
            Dim totale As Integer = 0
            Dim precedente As Integer = 0

            For indice As Integer = testoMaiuscolo.Length - 1 To 0 Step -1
                Dim carattere As Char = testoMaiuscolo(indice)
                If Not valori.ContainsKey(carattere) Then Return -1
                Dim valoreCorrente As Integer = valori(carattere)
                If valoreCorrente < precedente Then
                    totale -= valoreCorrente
                Else
                    totale += valoreCorrente
                End If
                precedente = valoreCorrente
            Next

            If totale <= 0 Then Return -1
            Return totale
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ConvertiNumeroRomano: " & ex.ToString())
            Return -1
        End Try
    End Function

    Public Function ScaricaTesto(libro As LibroTesto) As String
        Try
            If libro Is Nothing Then Return ""
            Return ScaricaTestoWikisourcePerPagina(libro.TitoloPaginaWikisource)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ScaricaTesto: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function ScaricaTestoCapitolo(capitolo As CapitoloLibro) As String
        Try
            If capitolo Is Nothing Then Return ""
            Return ScaricaTestoWikisourcePerPagina(capitolo.TitoloPaginaWikisource)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ScaricaTestoCapitolo: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ScaricaTestoWikisourcePerPagina(titoloPagina As String) As String
        Try
            If String.IsNullOrWhiteSpace(titoloPagina) Then Return ""

            Dim url As String = UrlBaseWikisource & "?action=parse&page=" & Uri.EscapeDataString(titoloPagina) & "&redirects=1&disabletoc=1&disableeditsection=1&prop=text&format=json&formatversion=2"
            Dim risposta As String = ScaricaStringa(url)
            If String.IsNullOrWhiteSpace(risposta) Then Return ""

            Dim json As JObject = JObject.Parse(risposta)

            Dim erroreApi As String = json("error")?("info")?.ToString()
            If Not String.IsNullOrWhiteSpace(erroreApi) Then
                Return ""
            End If

            Dim html As String = json("parse")?("text")?.ToString()
            Return ConvertiHtmlInTestoPiano(html)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ScaricaTestoWikisourcePerPagina: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ConvertiHtmlInTestoPiano(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""

            Dim testo As String = html

            testo = Regex.Replace(testo, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<span[^>]*class=""[^""]*mw-editsection[^""]*""[^>]*>.*?</span>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<sup[^>]*class=""[^""]*reference[^""]*""[^>]*>.*?</sup>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<ol[^>]*class=""[^""]*references[^""]*""[^>]*>.*?</ol>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<table[^>]*>.*?</table>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<div[^>]*class=""[^""]*(noprint|navigation-not-searchable|ws-noexport)[^""]*""[^>]*>.*?</div>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)

            testo = Regex.Replace(testo, "<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</p>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</div>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</li>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<li[^>]*>", "- ", RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</h[1-6]>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<h[1-6][^>]*>", Environment.NewLine, RegexOptions.IgnoreCase)

            testo = Regex.Replace(testo, "<[^>]+>", "")

            testo = System.Net.WebUtility.HtmlDecode(testo)

            testo = Regex.Replace(testo, "-?\s*\[\s*p\.\s*\d+\s*modifica\s*\]", "", RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "\[?\s*\d{1,4}\s*\]", "")

            testo = Regex.Replace(testo, "(?m)^[\s◄►\|·▶▷\-–—]*$", "")
            testo = Regex.Replace(testo, "[ \t]+", " ")
            testo = Regex.Replace(testo, "\n[ \t]+", Environment.NewLine)
            testo = Regex.Replace(testo, "\n{3,}", Environment.NewLine & Environment.NewLine)

            Return testo.Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ConvertiHtmlInTestoPiano: " & ex.ToString())
            Return html
        End Try
    End Function

    Public Function OttieniAutoriCatalogati() As List(Of String)
        Try
            Return _database.OttieniAutori()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniAutoriCatalogati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Function OttieniGeneriCatalogati() As List(Of String)
        Try
            Return _database.OttieniGeneri()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniGeneriCatalogati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Function OttieniLibriPerAutore(autore As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of LibroTesto)
        Try
            Return _database.OttieniLibriPerAutore(autore, count, titoloCursore, idCursore, titoloUltimoElemento, idUltimoElemento)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniLibriPerAutore: " & ex.ToString())
            titoloUltimoElemento = titoloCursore
            idUltimoElemento = idCursore
            Return New List(Of LibroTesto)
        End Try
    End Function

    Public Function OttieniLibriPerGenere(genere As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of LibroTesto)
        Try
            Return _database.OttieniLibriPerGenere(genere, count, titoloCursore, idCursore, titoloUltimoElemento, idUltimoElemento)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.OttieniLibriPerGenere: " & ex.ToString())
            titoloUltimoElemento = titoloCursore
            idUltimoElemento = idCursore
            Return New List(Of LibroTesto)
        End Try
    End Function

    Public Function ContaLibriCatalogati() As Integer
        Try
            Return _database.ContaLibri()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ContaLibriCatalogati: " & ex.ToString())
            Return 0
        End Try
    End Function

    Private Function ScaricaStringa(url As String) As String
        Const numeroTentativiMassimo As Integer = 3
        Try
            For tentativo As Integer = 1 To numeroTentativiMassimo
                Try
                    Dim risposta As HttpResponseMessage = _client.GetAsync(url).ConfigureAwait(False).GetAwaiter().GetResult()
                    If risposta.IsSuccessStatusCode Then
                        Return risposta.Content.ReadAsStringAsync().ConfigureAwait(False).GetAwaiter().GetResult()
                    End If

                    Dim codiceStato As Integer = CInt(risposta.StatusCode)

                    If tentativo < numeroTentativiMassimo Then
                        Dim attesaMs As Integer = If(codiceStato = 429, 3000 * tentativo, 1000 * tentativo)
                        If codiceStato = 429 AndAlso risposta.Headers.RetryAfter IsNot Nothing AndAlso risposta.Headers.RetryAfter.Delta.HasValue Then
                            attesaMs = CInt(risposta.Headers.RetryAfter.Delta.Value.TotalMilliseconds)
                        End If
                        Try
                            System.Threading.Thread.Sleep(attesaMs)
                        Catch
                        End Try
                    End If
                Catch ex As Exception
                    _logger.ScriviLog("CLSBibliotecaTesti.ScaricaStringa (" & url & ") tentativo " & tentativo.ToString() & " di " & numeroTentativiMassimo.ToString() & ": " & ex.ToString())
                    If tentativo < numeroTentativiMassimo Then
                        Try
                            System.Threading.Thread.Sleep(1000 * tentativo)
                        Catch
                        End Try
                    End If
                End Try
            Next
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaTesti.ScaricaStringa (" & url & "): " & ex.ToString())
            Return ""
        End Try
    End Function

End Class