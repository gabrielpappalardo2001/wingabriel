' ============================================================================================
' WGSpartitiArchivi - cercare spartiti, in MusicXML, MIDI e PDF, negli archivi online
' ============================================================================================
'
' Gli archivi che hanno un'interfaccia pubblica (JSON) o pagine regolari e danno l'indirizzo diretto del file:
'   - OpenScore e music21 su GitHub: spartiti digitali in MusicXML (canzoni, quartetti d'archi, orchestra, corali di Bach...); l'elenco
'     di tutti i file si scarica una volta, si conserva per trenta giorni e si cerca in locale;
'   - Mutopia: PDF e MIDI di spartiti composti al computer;
'   - Wikimedia Commons: PDF e MIDI;
'   - Internet Archive: file MIDI;
'   - BitMidi: file MIDI.
' Si cerca per parole (compositore, titolo, opera: "beethoven sinfonia 5"). Le parole si cercano così come sono scritte negli archivi,
' quasi tutti in inglese o nella lingua del compositore ("symphony", "sonata", "quartet").
' Chi sceglie un risultato lo scarica in una cartella di WinGabriel e lo apre: un PDF si legge, un MusicXML o un MIDI si suona.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGDb

Public NotInheritable Class RisultatoArchivio
    Public Titolo As String = ""
    ''' <summary>"MusicXML", "PDF" o "MIDI".</summary>
    Public Formato As String = ""
    Public Fonte As String = ""
    Public Indirizzo As String = ""
    Public NomeFile As String = ""
    ''' <summary>Per Internet Archive: l'elemento, che contiene più file e va aperto per scegliere.</summary>
    Public Elemento As String = ""

    Public Function Etichetta() As String
        If Elemento <> "" Then Return Titolo & ", " & Fonte & ", più file"
        Return Titolo & ", " & Formato & ", " & Fonte
    End Function
End Class

Public NotInheritable Class ArchiviSpartiti

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly client As HttpClient = CreaClient()

    Private Sub New()
    End Sub

    Private Shared Function CreaClient() As HttpClient
        Try
            System.Net.ServicePointManager.SecurityProtocol = System.Net.ServicePointManager.SecurityProtocol Or System.Net.SecurityProtocolType.Tls12
        Catch
        End Try
        Dim c As New HttpClient()
        c.Timeout = TimeSpan.FromSeconds(40)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (software per non vedenti)")
        Return c
    End Function

    Private Class CorpusGitHub
        Public Nome As String = ""
        Public Proprietario As String = ""
        Public Deposito As String = ""
        Public Prefisso As String = ""
        Public Estensioni As String() = New String() {}
    End Class

    Private Shared ReadOnly Corpora() As CorpusGitHub = {
        New CorpusGitHub With {.Nome = "OpenScore canzoni", .Proprietario = "OpenScore", .Deposito = "Lieder", .Prefisso = "scores/", .Estensioni = New String() {".mxl"}},
        New CorpusGitHub With {.Nome = "OpenScore quartetti", .Proprietario = "OpenScore", .Deposito = "StringQuartets", .Prefisso = "scores/", .Estensioni = New String() {".mxl", ".pdf"}},
        New CorpusGitHub With {.Nome = "OpenScore orchestra", .Proprietario = "MarkGotham", .Deposito = "Hauptstimme", .Prefisso = "data/", .Estensioni = New String() {".mxl"}},
        New CorpusGitHub With {.Nome = "music21", .Proprietario = "cuthbertLab", .Deposito = "music21", .Prefisso = "music21/corpus/", .Estensioni = New String() {".mxl", ".xml", ".musicxml", ".mid", ".midi"}}
    }

    ' ---- utilità ------------------------------------------------------------------------------------------------

    Private Shared Function CartellaBase() As String
        Try
            Dim c As String = WGDatabase.CartellaDati("spartiti")
            If Not String.IsNullOrWhiteSpace(c) Then Return c
        Catch ex As Exception
            log.ScriviLog("ArchiviSpartiti.CartellaBase: " & ex.Message)
        End Try
        Dim d As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "spartiti")
        Directory.CreateDirectory(d)
        Return d
    End Function

    Private Shared Function Normalizza(testo As String) As String
        Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder()
        For Each ch As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(ch) <> UnicodeCategory.NonSpacingMark Then sb.Append(ch)
        Next
        Return sb.ToString()
    End Function

    Private Shared Function Parole(testo As String) As List(Of String)
        Return Normalizza(testo).Split(New Char() {" "c, ","c, ";"c, "-"c, "_"c, "."c}, StringSplitOptions.RemoveEmptyEntries).ToList()
    End Function

    ''' <summary>
    ''' Vero se il testo contiene tutte le parole cercate. Un numero ("5") deve essere una parola intera: la sinfonia 5 non è l'opera 55.
    ''' </summary>
    Private Shared Function Corrisponde(normalizzato As String, parole As List(Of String)) As Boolean
        Dim pezzi As HashSet(Of String) = New HashSet(Of String)(normalizzato.Split(New Char() {" "c, ","c, "."c, "-"c, "_"c, "/"c, "("c, ")"c, "'"c}, StringSplitOptions.RemoveEmptyEntries))
        For Each w As String In parole
            If w.All(Function(c) Char.IsDigit(c)) Then
                If Not pezzi.Contains(w) Then Return False
            ElseIf Not normalizzato.Contains(w) Then
                Return False
            End If
        Next
        Return True
    End Function

    Private Shared Function Leggi(url As String, annulla As CancellationToken) As String
        ' troppe richieste in poco tempo (429): si aspetta un momento e si riprova una volta
        For tentativo As Integer = 1 To 2
            Using r As HttpResponseMessage = client.GetAsync(url, annulla).GetAwaiter().GetResult()
                If CInt(r.StatusCode) = 429 AndAlso tentativo = 1 Then
                    annulla.WaitHandle.WaitOne(1500)
                    annulla.ThrowIfCancellationRequested()
                    Continue For
                End If
                r.EnsureSuccessStatusCode()
                Return Encoding.UTF8.GetString(r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
            End Using
        Next
        Return ""
    End Function

    Private Shared Function Breve(testo As String, massimo As Integer) As String
        Dim t As String = Regex.Replace(If(testo, ""), "\s+", " ").Trim()
        If t.Length <= massimo Then Return t
        Return t.Substring(0, massimo - 1).TrimEnd() & "…"
    End Function

    Private Shared Function FormatoDaEstensione(percorso As String) As String
        Select Case Path.GetExtension(percorso).ToLowerInvariant()
            Case ".mxl", ".xml", ".musicxml" : Return "MusicXML"
            Case ".pdf" : Return "PDF"
            Case ".mid", ".midi", ".kar" : Return "MIDI"
        End Select
        Return ""
    End Function

    Private Shared Function Rango(formato As String) As Integer
        Select Case formato
            Case "MusicXML" : Return 0
            Case "MIDI" : Return 1
        End Select
        Return 2
    End Function

    ' ---- la ricerca -------------------------------------------------------------------------------------------

    ''' <summary>Cerca in tutti gli archivi insieme; un archivio che non risponde si salta.</summary>
    Public Shared Function Cerca(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim compiti As New List(Of Task(Of List(Of RisultatoArchivio)))()
        ' gli archivi hanno i titoli in inglese: si cerca anche con le parole italiane tradotte ("quinta sinfonia" -> "5 symphony")
        Dim varianti As New List(Of String)()
        varianti.Add(testo)
        ' gli archivi cercano parola per parola: un nome scritto male ("beetoven") non trova niente, quindi si cerca anche con quello corretto
        Dim corretto As String = CorreggiNomi(testo)
        If corretto <> "" Then varianti.Add(corretto)
        Dim inglese As String = TraduciInInglese(If(corretto <> "", corretto, testo))
        If inglese <> "" AndAlso Not varianti.Any(Function(x) String.Equals(x, inglese, StringComparison.OrdinalIgnoreCase)) Then varianti.Add(inglese)
        For Each v As String In varianti
            Dim t As String = v
            compiti.Add(Task.Run(Function() Sicuro("GitHub", Function() CercaGitHub(t, annulla))))
            compiti.Add(Task.Run(Function() Sicuro("Mutopia", Function() CercaMutopia(t, annulla))))
            compiti.Add(Task.Run(Function() Sicuro("Commons", Function() CercaCommons(t, annulla))))
            compiti.Add(Task.Run(Function() Sicuro("Internet Archive", Function() CercaInternetArchive(t, annulla))))
            compiti.Add(Task.Run(Function() Sicuro("BitMidi", Function() CercaBitMidi(t, annulla))))
        Next
        Dim tutti As New List(Of RisultatoArchivio)()
        For Each c As Task(Of List(Of RisultatoArchivio)) In compiti
            Try
                tutti.AddRange(c.GetAwaiter().GetResult())
            Catch ex As Exception
                log.ScriviLog("ArchiviSpartiti.Cerca: " & ex.Message)
            End Try
        Next
        annulla.ThrowIfCancellationRequested()
        Dim viste As New HashSet(Of String)()
        Dim ordinati As New List(Of RisultatoArchivio)()
        For Each r As RisultatoArchivio In tutti.OrderBy(Function(x) Rango(x.Formato))
            If viste.Add(r.Indirizzo & "|" & r.Elemento) Then ordinati.Add(r)
        Next
        ' i PDF vengono per ultimi e in genere restano nascosti: non devono togliere posto ai MIDI
        Return ordinati.Take(150).ToList()
    End Function

    Private Shared ReadOnly Compositori As String() = {"beethoven", "mozart", "bach", "chopin", "schubert", "schumann", "brahms", "haydn", "handel", "vivaldi",
        "liszt", "debussy", "ravel", "satie", "tchaikovsky", "rachmaninoff", "rachmaninov", "mendelssohn", "dvorak", "grieg", "sibelius", "verdi", "puccini",
        "rossini", "bellini", "donizetti", "paganini", "scarlatti", "monteverdi", "corelli", "albinoni", "pachelbel", "telemann", "purcell", "mahler",
        "bruckner", "wagner", "strauss", "offenbach", "bizet", "faure", "saint-saens", "gounod", "massenet", "berlioz", "franck", "elgar", "holst", "britten",
        "gershwin", "joplin", "stravinsky", "prokofiev", "shostakovich", "mussorgsky", "borodin", "rimsky-korsakov", "glinka", "smetana", "janacek",
        "bartok", "kodaly", "albeniz", "granados", "tarrega", "sor", "giuliani", "clementi", "czerny", "kuhlau", "diabelli", "weber", "field", "foster",
        "morricone", "boccherini", "cherubini", "palestrina", "gabrieli", "buxtehude", "couperin", "rameau", "lully", "gluck", "salieri", "hummel",
        "symphony", "sonata", "quartet", "quintet", "concerto", "nocturne", "prelude", "etude", "waltz", "minuet", "fugue", "overture", "serenade",
        "rhapsody", "mazurka", "polonaise", "ballade", "impromptu", "lieder", "moonlight", "elise", "pathetique", "appassionata", "eroica", "pastoral"}

    ''' <summary>Distanza di Levenshtein fra due parole.</summary>
    Private Shared Function Distanza(a As String, b As String) As Integer
        Dim d(a.Length, b.Length) As Integer
        For i As Integer = 0 To a.Length
            d(i, 0) = i
        Next
        For j As Integer = 0 To b.Length
            d(0, j) = j
        Next
        For i As Integer = 1 To a.Length
            For j As Integer = 1 To b.Length
                Dim costo As Integer = If(a(i - 1) = b(j - 1), 0, 1)
                d(i, j) = Math.Min(Math.Min(d(i - 1, j) + 1, d(i, j - 1) + 1), d(i - 1, j - 1) + costo)
            Next
        Next
        Return d(a.Length, b.Length)
    End Function

    ''' <summary>
    ''' Il testo con i nomi di compositori e di forme musicali scritti male messi giusti ("beetoven" diventa "beethoven").
    ''' Vuoto se non c'era niente da correggere.
    ''' </summary>
    Private Shared Function CorreggiNomi(testo As String) As String
        Dim fuori As New List(Of String)()
        Dim cambiato As Boolean = False
        For Each w As String In Parole(testo)
            Dim scelta As String = w
            If w.Length >= 4 AndAlso Not w.All(Function(c) Char.IsDigit(c)) AndAlso Array.IndexOf(Compositori, w) < 0 AndAlso Not Traduzioni.ContainsKey(w) Then
                Dim massimo As Integer = If(w.Length >= 8, 2, 1)
                Dim migliore As Integer = massimo + 1
                For Each c As String In Compositori
                    If Math.Abs(c.Length - w.Length) > massimo Then Continue For
                    Dim dist As Integer = Distanza(w, c)
                    If dist < migliore Then
                        migliore = dist
                        scelta = c
                    End If
                Next
            End If
            If scelta <> w Then cambiato = True
            fuori.Add(scelta)
        Next
        If Not cambiato Then Return ""
        Return String.Join(" ", fuori)
    End Function

    Private Shared ReadOnly Traduzioni As Dictionary(Of String, String) = CreaTraduzioni()

    Private Shared Function CreaTraduzioni() As Dictionary(Of String, String)
        Dim d As New Dictionary(Of String, String)()
        Dim coppie As String() = {"sinfonia=symphony", "sinfonie=symphony", "quartetto=quartet", "quintetto=quintet", "sestetto=sextet", "settimino=septet",
            "concerto=concerto", "pianoforte=piano", "violino=violin", "violoncello=cello", "flauto=flute", "chitarra=guitar", "organo=organ",
            "valzer=waltz", "notturno=nocturne", "preludio=prelude", "studio=etude", "danza=dance", "danze=dances", "marcia=march", "messa=mass",
            "canzone=song", "canzoni=songs", "primavera=spring", "estate=summer", "autunno=autumn", "inverno=winter", "sogno=dream", "luna=moon",
            "elisa=elise", "gioia=joy", "inno=ode", "fuga=fugue", "ouverture=overture", "romanza=romance", "variazioni=variations", "scherzo=scherzo",
            "prima=1", "primo=1", "seconda=2", "secondo=2", "terza=3", "terzo=3", "quarta=4", "quarto=4", "quinta=5", "quinto=5", "sesta=6", "sesto=6",
            "settima=7", "settimo=7", "ottava=8", "ottavo=8", "nona=9", "nono=9", "decima=10", "decimo=10", "numero=no", "opera=op",
            "maggiore=major", "minore=minor", "bemolle=flat", "diesis=sharp", "ave=ave", "maria=maria", "pastorale=pastoral", "eroica=eroica"}
        For Each c As String In coppie
            Dim i As Integer = c.IndexOf("="c)
            d(c.Substring(0, i)) = c.Substring(i + 1)
        Next
        Return d
    End Function

    ''' <summary>
    ''' Le parole italiane più comuni dei titoli messe in inglese, i numeri ordinali in cifre, senza le parole che non servono (di, la, per).
    ''' Vuoto se non c'era niente da cambiare.
    ''' </summary>
    Private Shared Function TraduciInInglese(testo As String) As String
        Dim inutili As String() = {"di", "del", "della", "dei", "delle", "il", "lo", "la", "le", "i", "gli", "un", "una", "per", "in", "a", "da", "van", "von", "de", "n"}
        Dim fuori As New List(Of String)()
        Dim cambiato As Boolean = False
        For Each w As String In Parole(testo)
            Dim t As String = Nothing
            If Traduzioni.TryGetValue(w, t) Then
                If t <> w Then cambiato = True
                fuori.Add(t)
            ElseIf Array.IndexOf(inutili, w) >= 0 Then
                cambiato = True
            Else
                fuori.Add(w)
            End If
        Next
        If Not cambiato OrElse fuori.Count = 0 Then Return ""
        Return String.Join(" ", fuori)
    End Function

    Private Shared Function Sicuro(nome As String, ricerca As Func(Of List(Of RisultatoArchivio))) As List(Of RisultatoArchivio)
        Try
            Return ricerca()
        Catch ex As Exception
            log.ScriviLog("ArchiviSpartiti." & nome & ": " & ex.Message)
            Return New List(Of RisultatoArchivio)()
        End Try
    End Function

    ' ---- OpenScore e music21 su GitHub -------------------------------------------------------------------------

    Private Shared Function PercorsiDelCorpus(c As CorpusGitHub, annulla As CancellationToken) As List(Of String)
        Dim cartella As String = Path.Combine(CartellaBase(), "archivi")
        Directory.CreateDirectory(cartella)
        Dim file As String = Path.Combine(cartella, c.Deposito & ".txt")
        If System.IO.File.Exists(file) AndAlso (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(file)).TotalDays < 30 Then
            Return System.IO.File.ReadAllLines(file, Encoding.UTF8).ToList()
        End If
        Try
            Dim json As String = Leggi("https://api.github.com/repos/" & c.Proprietario & "/" & c.Deposito & "/git/trees/HEAD?recursive=1", annulla)
            Dim elenco As New List(Of String)()
            For Each t As JToken In CType(JObject.Parse(json)("tree"), JArray)
                If CStr(t("type")) <> "blob" Then Continue For
                Dim p As String = CStr(t("path"))
                If Not p.StartsWith(c.Prefisso, StringComparison.Ordinal) Then Continue For
                If Not c.Estensioni.Contains(Path.GetExtension(p).ToLowerInvariant()) Then Continue For
                If p.Contains("_melody") Then Continue For
                elenco.Add(p)
            Next
            ' prima in un file temporaneo: un elenco scritto a metà resterebbe buono per trenta giorni
            Dim temporaneo As String = file & ".nuovo"
            System.IO.File.WriteAllLines(temporaneo, elenco, New UTF8Encoding(False))
            If elenco.Count > 0 Then
                If System.IO.File.Exists(file) Then System.IO.File.Delete(file)
                System.IO.File.Move(temporaneo, file)
            Else
                System.IO.File.Delete(temporaneo)
            End If
            Return elenco
        Catch ex As Exception
            If System.IO.File.Exists(file) Then Return System.IO.File.ReadAllLines(file, Encoding.UTF8).ToList()
            Throw
        End Try
    End Function

    Private Shared Function TitoloDaPercorso(c As CorpusGitHub, percorso As String) As String
        Dim relativo As String = percorso.Substring(c.Prefisso.Length)
        Dim pezzi As List(Of String) = relativo.Split("/"c).ToList()
        Dim nomeFile As String = pezzi(pezzi.Count - 1)
        pezzi.RemoveAt(pezzi.Count - 1)
        Dim parteSingola As String = ""
        Dim m As Match = Regex.Match(nomeFile, "-Part-([^.]+)", RegexOptions.IgnoreCase)
        If m.Success Then parteSingola = ", parte " & m.Groups(1).Value.Replace("_", " ").ToLowerInvariant()
        Dim titolo As String
        If pezzi.Count = 0 Then
            titolo = Path.GetFileNameWithoutExtension(nomeFile)
        Else
            Dim compositore As String = pezzi(0).Replace("_", " ")
            If compositore.Contains(",") Then compositore = compositore.Substring(0, compositore.IndexOf(","c))
            Dim resto As List(Of String) = pezzi.Skip(1).Select(Function(x) x.Replace("_", " ")).Where(Function(x) x <> " " AndAlso x <> "").ToList()
            titolo = compositore & If(resto.Count > 0, ", " & String.Join(", ", resto), "")
        End If
        Return Breve(titolo & parteSingola, 90)
    End Function

    Private Shared Function CercaGitHub(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim parole As List(Of String) = ArchiviSpartiti.Parole(testo)
        Dim ris As New List(Of RisultatoArchivio)()
        If parole.Count = 0 Then Return ris
        For Each c As CorpusGitHub In Corpora
            annulla.ThrowIfCancellationRequested()
            Dim percorsi As List(Of String)
            Try
                percorsi = PercorsiDelCorpus(c, annulla)
            Catch ex As Exception
                log.ScriviLog("ArchiviSpartiti.CercaGitHub (" & c.Deposito & "): " & ex.Message)
                Continue For
            End Try
            Dim n As Integer = 0
            For Each p As String In percorsi
                Dim norm As String = Normalizza(p.Replace("_", " ").Replace(",", " "))
                If Not Corrisponde(norm, parole) Then Continue For
                Dim r As New RisultatoArchivio()
                r.Titolo = TitoloDaPercorso(c, p)
                r.Formato = FormatoDaEstensione(p)
                r.Fonte = c.Nome
                r.NomeFile = Path.GetFileName(p)
                r.Indirizzo = "https://raw.githubusercontent.com/" & c.Proprietario & "/" & c.Deposito & "/HEAD/" & String.Join("/", p.Split("/"c).Select(Function(x) Uri.EscapeDataString(x)))
                ris.Add(r)
                n += 1
                If n >= 15 Then Exit For
            Next
        Next
        Return ris
    End Function

    ' ---- Mutopia ------------------------------------------------------------------------------------------------

    Private Shared Function SenzaTag(html As String) As String
        Return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), "\s+", " ").Trim()
    End Function

    Private Shared Function CercaMutopia(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim ris As New List(Of RisultatoArchivio)()
        Dim html As String = Leggi("https://www.mutopiaproject.org/cgibin/make-table.cgi?searchingfor=" & Uri.EscapeDataString(testo), annulla)
        For Each m As Match In Regex.Matches(html, "<table class=""table-bordered result-table"">(.*?)</table>", RegexOptions.Singleline)
            Dim blocco As String = m.Groups(1).Value
            Dim celle As MatchCollection = Regex.Matches(blocco, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline)
            If celle.Count < 2 Then Continue For
            Dim titolo As String = SenzaTag(celle(0).Groups(1).Value)
            Dim autore As String = SenzaTag(celle(1).Groups(1).Value)
            If autore.StartsWith("by ", StringComparison.OrdinalIgnoreCase) Then autore = autore.Substring(3)
            If autore.Contains("(") Then autore = autore.Substring(0, autore.IndexOf("("c)).Trim()
            Dim cognome As String = If(autore.Contains(" "), autore.Substring(autore.LastIndexOf(" "c) + 1), autore)
            Dim etichetta As String = Breve(titolo & If(cognome <> "", ", " & cognome, ""), 90)
            Dim pdf As Match = Regex.Match(blocco, "href=""([^""]+-a4\.pdf)""")
            If pdf.Success Then
                Dim r As New RisultatoArchivio()
                r.Titolo = etichetta
                r.Formato = "PDF"
                r.Fonte = "Mutopia"
                r.Indirizzo = pdf.Groups(1).Value
                r.NomeFile = Path.GetFileName(r.Indirizzo)
                ris.Add(r)
            End If
            Dim mid As Match = Regex.Match(blocco, "href=""([^""]+\.mid)""")
            If mid.Success Then
                Dim r As New RisultatoArchivio()
                r.Titolo = etichetta
                r.Formato = "MIDI"
                r.Fonte = "Mutopia"
                r.Indirizzo = mid.Groups(1).Value
                r.NomeFile = Path.GetFileName(r.Indirizzo)
                ris.Add(r)
            End If
            If ris.Count >= 30 Then Exit For
        Next
        Return ris
    End Function

    ' ---- Wikimedia Commons -------------------------------------------------------------------------------------

    Private Shared Function CercaCommons(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim titoli As New List(Of String)()
        For Each ricerca As String In New String() {testo & " filetype:pdf", testo & " mid"}
            Dim json As String = Leggi("https://commons.wikimedia.org/w/api.php?action=query&list=search&srnamespace=6&srlimit=40&format=json&srsearch=" & Uri.EscapeDataString(ricerca), annulla)
            Dim trovati As New List(Of String)()
            For Each t As JToken In CType(JObject.Parse(json)("query")("search"), JArray)
                Dim titolo As String = CStr(t("title"))
                ' i libri di Internet Archive (ID "IA ...") non sono spartiti, salvo quelli di IMSLP
                If titolo.Contains("(IA ") AndAlso Not titolo.Contains("imslp") Then Continue For
                If FormatoDaEstensione(titolo) <> "" Then trovati.Add(titolo)
            Next
            titoli.AddRange(trovati.Take(15))
        Next
        Dim ris As New List(Of RisultatoArchivio)()
        If titoli.Count = 0 Then Return ris
        Dim json2 As String = Leggi("https://commons.wikimedia.org/w/api.php?action=query&prop=imageinfo&iiprop=url&format=json&titles=" & Uri.EscapeDataString(String.Join("|", titoli.Distinct().Take(50))), annulla)
        Dim pagine As JObject = TryCast(JObject.Parse(json2)("query")("pages"), JObject)
        If pagine Is Nothing Then Return ris
        For Each p As JProperty In pagine.Properties()
            Dim info As JToken = p.Value("imageinfo")
            If info Is Nothing OrElse Not info.HasValues Then Continue For
            Dim titolo As String = CStr(p.Value("title"))
            Dim url As String = CStr(info(0)("url"))
            If url.Contains("?") Then url = url.Substring(0, url.IndexOf("?"c))
            Dim r As New RisultatoArchivio()
            r.Titolo = Breve(Path.GetFileNameWithoutExtension(titolo.Replace("File:", "")).Replace("_", " "), 90)
            r.Formato = FormatoDaEstensione(titolo)
            r.Fonte = "Wikimedia Commons"
            r.Indirizzo = url
            r.NomeFile = Path.GetFileName(url)
            ris.Add(r)
        Next
        Return ris
    End Function

    ' ---- Internet Archive --------------------------------------------------------------------------------------

    Private Shared Function CercaInternetArchive(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim ris As New List(Of RisultatoArchivio)()
        Dim domanda As String = "(" & testo.Replace("""", " ") & ") AND format:MIDI"
        Dim json As String = Leggi("https://archive.org/advancedsearch.php?fl%5B%5D=identifier&fl%5B%5D=title&rows=15&output=json&q=" & Uri.EscapeDataString(domanda), annulla)
        For Each d As JToken In CType(JObject.Parse(json)("response")("docs"), JArray)
            Dim r As New RisultatoArchivio()
            r.Elemento = CStr(d("identifier"))
            Dim titolo As JToken = d("title")
            r.Titolo = Breve(If(titolo IsNot Nothing AndAlso titolo.Type = JTokenType.String, CStr(titolo), If(titolo IsNot Nothing AndAlso titolo.Type = JTokenType.Array AndAlso titolo.HasValues, CStr(titolo(0)), r.Elemento)), 70)
            r.Formato = "MIDI"
            r.Fonte = "Internet Archive"
            ris.Add(r)
        Next
        Return ris
    End Function

    ''' <summary>I file MIDI di un elemento di Internet Archive.</summary>
    Public Shared Function FileDelElemento(elemento As RisultatoArchivio, annulla As CancellationToken, Optional testoCercato As String = "") As List(Of RisultatoArchivio)
        Dim ris As New List(Of RisultatoArchivio)()
        Dim json As String = Leggi("https://archive.org/metadata/" & Uri.EscapeDataString(elemento.Elemento), annulla)
        Dim files As JArray = TryCast(JObject.Parse(json)("files"), JArray)
        If files Is Nothing Then Return ris
        For Each f As JToken In files
            Dim nome As String = CStr(f("name"))
            If FormatoDaEstensione(nome) <> "MIDI" Then Continue For
            Dim r As New RisultatoArchivio()
            r.Titolo = Breve(Path.GetFileNameWithoutExtension(nome.Substring(nome.LastIndexOf("/"c) + 1)).Replace("_", " "), 80)
            r.Formato = "MIDI"
            r.Fonte = "Internet Archive"
            r.NomeFile = nome.Substring(nome.LastIndexOf("/"c) + 1)
            r.Indirizzo = "https://archive.org/download/" & Uri.EscapeDataString(elemento.Elemento) & "/" & String.Join("/", nome.Split("/"c).Select(Function(x) Uri.EscapeDataString(x)))
            ris.Add(r)
            If ris.Count >= 400 Then Exit For
        Next
        ' prima i file che hanno le parole cercate nel nome
        Dim parole As List(Of String) = ArchiviSpartiti.Parole(testoCercato)
        If parole.Count > 0 Then
            Dim sceltiPerNome As List(Of RisultatoArchivio) = ris.Where(Function(x) Corrisponde(Normalizza(x.Titolo), parole)).ToList()
            If sceltiPerNome.Count > 0 Then Return sceltiPerNome.Take(200).ToList()
            Dim conAlcune As List(Of RisultatoArchivio) = ris.Where(Function(x) parole.Any(Function(w) Normalizza(x.Titolo).Contains(w))).ToList()
            If conAlcune.Count > 0 Then Return conAlcune.Take(200).ToList()
        End If
        Return ris.Take(200).ToList()
    End Function

    ' ---- BitMidi ---------------------------------------------------------------------------------------------------

    Private Shared Function CercaBitMidi(testo As String, annulla As CancellationToken) As List(Of RisultatoArchivio)
        Dim ris As New List(Of RisultatoArchivio)()
        Dim json As String = Leggi("https://bitmidi.com/api/midi/search?q=" & Uri.EscapeDataString(testo), annulla)
        For Each d As JToken In CType(JObject.Parse(json)("result")("results"), JArray)
            Dim url As String = CStr(d("downloadUrl"))
            If String.IsNullOrEmpty(url) Then Continue For
            Dim r As New RisultatoArchivio()
            Dim nome As String = CStr(d("name"))
            r.Titolo = Breve(Path.GetFileNameWithoutExtension(nome).Replace("'", "").Replace("-", " "), 80)
            r.Formato = "MIDI"
            r.Fonte = "BitMidi"
            r.NomeFile = nome
            r.Indirizzo = If(url.StartsWith("http", StringComparison.OrdinalIgnoreCase), url, "https://bitmidi.com" & url)
            ris.Add(r)
        Next
        Return ris
    End Function

    ' ---- lo scaricamento ---------------------------------------------------------------------------------------

    Private Shared Function IntestazioneValida(percorso As String, formato As String) As Boolean
        Dim b(3) As Byte
        Using f As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read)
            If f.Read(b, 0, 4) < 4 Then Return False
        End Using
        Dim t As String = Encoding.ASCII.GetString(b)
        Select Case formato
            Case "PDF" : Return t = "%PDF"
            Case "MIDI" : Return t = "MThd" OrElse t = "RIFF"
            Case Else : Return (b(0) = &H50 AndAlso b(1) = &H4B) OrElse t.StartsWith("<?xm") OrElse t.StartsWith("<sco") OrElse (b(0) = &HEF AndAlso b(1) = &HBB)
        End Select
    End Function

    ''' <summary>Scarica il file del risultato nella cartella degli spartiti scaricati e ne restituisce il percorso.</summary>
    Public Shared Function Scarica(r As RisultatoArchivio, annulla As CancellationToken) As String
        Dim cartella As String = Path.Combine(CartellaBase(), "scaricati")
        Directory.CreateDirectory(cartella)
        Dim estensione As String = Path.GetExtension(r.NomeFile).ToLowerInvariant()
        If FormatoDaEstensione("x" & estensione) <> r.Formato Then estensione = If(r.Formato = "PDF", ".pdf", If(r.Formato = "MIDI", ".mid", ".mxl"))
        Dim nome As String = AiutoMusica.NomeFileSicuro(Path.GetFileNameWithoutExtension(If(r.NomeFile <> "", r.NomeFile, r.Titolo)))
        If nome.Length > 60 Then nome = nome.Substring(0, 60).Trim()
        If nome = "" Then nome = "spartito"
        Dim impronta As String
        Using sha As SHA1 = SHA1.Create()
            Dim h As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(r.Indirizzo))
            impronta = h(0).ToString("x2") & h(1).ToString("x2") & h(2).ToString("x2")
        End Using
        Dim destinazione As String = Path.Combine(cartella, nome & "-" & impronta & estensione)
        If System.IO.File.Exists(destinazione) AndAlso New FileInfo(destinazione).Length > 0 Then Return destinazione
        Dim temporaneo As String = destinazione & ".scarico"
        Using risposta As HttpResponseMessage = client.GetAsync(r.Indirizzo, HttpCompletionOption.ResponseHeadersRead, annulla).GetAwaiter().GetResult()
            risposta.EnsureSuccessStatusCode()
            Using origine As Stream = risposta.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                Using f As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
                    origine.CopyToAsync(f, 81920, annulla).GetAwaiter().GetResult()
                End Using
            End Using
        End Using
        If Not IntestazioneValida(temporaneo, r.Formato) Then
            Try
                System.IO.File.Delete(temporaneo)
            Catch
            End Try
            Throw New InvalidDataException("Il file scaricato non è un " & r.Formato & " valido.")
        End If
        If System.IO.File.Exists(destinazione) Then System.IO.File.Delete(destinazione)
        System.IO.File.Move(temporaneo, destinazione)
        Return destinazione
    End Function
End Class
