' ============================================================================================
' WGSpartitiRiconoscimento - dal PDF (o dalla foto) di uno spartito al MusicXML
' ============================================================================================
'
' Riconoscere le note stampate su una pagina è un lavoro che fa molto bene il programma libero Audiveris. WinGabriel lo
' cerca sul computer; se non c'è lo scarica una volta sola (versione per console, che porta con sé tutto quello che serve,
' Java compreso) e lo apre senza finestre. Il risultato è un file MusicXML, che WGSpartiti legge e WGSpartitiRiproduzione suona.
'
' I risultati si conservano: riaprire lo stesso PDF non ripete il riconoscimento, che può durare qualche minuto.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGDb

Public NotInheritable Class RiconoscimentoSpartiti

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Function EImmagineOPdf(percorso As String) As Boolean
        Select Case Path.GetExtension(percorso).ToLowerInvariant()
            Case ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif"
                Return True
        End Select
        Return False
    End Function

    Private Shared Function CartellaSpartiti() As String
        Try
            Dim c As String = WGDatabase.CartellaDati("spartiti")
            If Not String.IsNullOrWhiteSpace(c) Then Return c
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.CartellaSpartiti: " & ex.Message)
        End Try
        Dim d As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "spartiti")
        Directory.CreateDirectory(d)
        Return d
    End Function

    ''' <summary>Dove WinGabriel tiene la versione corretta di uno spartito aperto da un certo file, senza toccare l'originale.</summary>
    Friend Shared Function PercorsoCorretto(percorso As String) As String
        Dim d As String = Path.Combine(CartellaSpartiti(), "corretti")
        Directory.CreateDirectory(d)
        Return Path.Combine(d, ImpronteCartella(percorso) & ".musicxml")
    End Function

    ' Il programma di riconoscimento non è un dato dell'utente: sta nella radice dell'applicazione, non nella cartella dei dati.
    Private Shared Function CartellaAudiveris() As String
        Return WGDb.WGPercorsiApplicazione.CartellaComponente("audiveris")
    End Function

    ''' <summary>Il programma di riconoscimento, se c'è: installato dall'utente o scaricato da WinGabriel. Vuoto se manca.</summary>
    Public Shared Function TrovaAudiveris() As String
        Try
            Dim candidati As New List(Of String)()
            For Each radice As String In New String() {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), CartellaAudiveris()}
                If String.IsNullOrWhiteSpace(radice) OrElse Not Directory.Exists(radice) Then Continue For
                Dim cartelle As New List(Of String)()
                If String.Equals(radice, CartellaAudiveris(), StringComparison.OrdinalIgnoreCase) Then
                    cartelle.Add(radice)
                Else
                    Dim a As String = Path.Combine(radice, "Audiveris")
                    If Directory.Exists(a) Then cartelle.Add(a)
                End If
                For Each cartella As String In cartelle
                    Try
                        For Each f As String In Directory.EnumerateFiles(cartella, "Audiveris.exe", SearchOption.AllDirectories)
                            candidati.Add(f)
                        Next
                    Catch
                    End Try
                Next
            Next
            If candidati.Count > 0 Then Return candidati.OrderBy(Function(f) f.Length).First()
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.TrovaAudiveris: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' I nomi degli strumenti scritti all'inizio dei righi della prima pagina, dall'alto in basso, quando il PDF ha il testo
    ''' (un PDF esportato da un programma di notazione: nelle scansioni non c'è testo da leggere). Vuoto se non ne trova almeno tre.
    ''' </summary>
    Public Shared Function NomiStrumentiDaPdf(percorso As String) As List(Of String)
        Dim risultato As New List(Of String)()
        If Path.GetExtension(percorso).ToLowerInvariant() <> ".pdf" Then Return risultato
        Using doc As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorso)
            Dim numero As Integer = 0
            For Each pagina In doc.GetPages()
                numero += 1
                If numero > 3 Then Exit For
                Dim righe As New SortedDictionary(Of Integer, List(Of UglyToad.PdfPig.Content.Word))()
                For Each parola In pagina.GetWords()
                    Dim chiave As Integer = CInt(Math.Round(parola.BoundingBox.Bottom / 4.0))
                    If Not righe.ContainsKey(chiave) Then righe(chiave) = New List(Of UglyToad.PdfPig.Content.Word)()
                    righe(chiave).Add(parola)
                Next
                Dim trovati As New List(Of KeyValuePair(Of Double, String))()
                For Each kv In righe
                    Dim parole = kv.Value.OrderBy(Function(w) w.BoundingBox.Left).ToList()
                    ' dopo il nome ci sono i simboli della chiave e dell'armatura, che il testo del PDF rende con punti interrogativi
                    Dim pezzi As New List(Of String)()
                    For Each w In parole
                        If Not w.Text.Any(Function(c) Char.IsLetterOrDigit(c)) Then Exit For
                        pezzi.Add(w.Text)
                    Next
                    Dim testo As String = String.Join(" ", pezzi).Trim()
                    If testo.Length = 0 OrElse testo.Length > 40 Then Continue For
                    If parole(0).BoundingBox.Left > pagina.Width * 0.4 Then Continue For
                    If Spartito.ProgrammaDalNomeNoto(testo) < 0 Then Continue For
                    trovati.Add(New KeyValuePair(Of Double, String)(parole(0).BoundingBox.Bottom, testo))
                Next
                If trovati.Count >= 3 Then
                    For Each t In trovati.OrderByDescending(Function(x) x.Key)
                        risultato.Add(t.Value)
                    Next
                    Return risultato
                End If
            Next
        End Using
        Return risultato
    End Function

    ' Le indicazioni di tempo in lettere, con i battiti al minuto (semiminime) che si usano quando il numero non c'è.
    ' Le più lunghe prima: "allegro moderato" non è "moderato" e "andantino" non è "andante".
    Private Shared ReadOnly TempiInLettere() As KeyValuePair(Of String, Integer) = {
        New KeyValuePair(Of String, Integer)("prestissimo", 200), New KeyValuePair(Of String, Integer)("allegro moderato", 118),
        New KeyValuePair(Of String, Integer)("allegro assai", 150), New KeyValuePair(Of String, Integer)("allegro vivace", 150),
        New KeyValuePair(Of String, Integer)("allegro con brio", 150), New KeyValuePair(Of String, Integer)("andante moderato", 100),
        New KeyValuePair(Of String, Integer)("andante con moto", 100), New KeyValuePair(Of String, Integer)("andantino", 100),
        New KeyValuePair(Of String, Integer)("allegretto", 112), New KeyValuePair(Of String, Integer)("larghetto", 60),
        New KeyValuePair(Of String, Integer)("moderato", 108), New KeyValuePair(Of String, Integer)("andante", 90),
        New KeyValuePair(Of String, Integer)("allegro", 130), New KeyValuePair(Of String, Integer)("vivace", 160),
        New KeyValuePair(Of String, Integer)("presto", 184), New KeyValuePair(Of String, Integer)("adagio", 70),
        New KeyValuePair(Of String, Integer)("largo", 50), New KeyValuePair(Of String, Integer)("grave", 40)}

    ''' <summary>
    ''' I battiti al minuto (in semiminime) che un testo indica: prima il numero del metronomo ("= 120", con la figura davanti se si legge),
    ''' altrimenti il termine italiano (Allegro, Andante...). 0 se non c'è niente di riconoscibile.
    ''' </summary>
    Friend Shared Function TempoDaTesto(testo As String) As Integer
        If String.IsNullOrWhiteSpace(testo) Then Return 0
        Try
            For Each m As Match In Regex.Matches(testo, "(?<prima>\S?\s?\.?\s?)=\s*(?:c\.|ca\.?|circa)?\s*(?<n>\d{2,3})(?!\d)")
                Dim n As Integer = CInt(m.Groups("n").Value)
                If n < 30 OrElse n > 300 Then Continue For
                Dim prima As String = m.Groups("prima").Value
                Dim fattore As Double = 1.0
                If prima.Contains("♪") OrElse prima.Contains("𝅘𝅥𝅮") Then fattore = 0.5
                If prima.Contains("𝅗𝅥") Then fattore = 2.0
                If prima.Contains(".") Then fattore *= 1.5
                Return CInt(Math.Round(n * fattore))
            Next
            Dim minuscolo As String = testo.ToLowerInvariant()
            For Each t As KeyValuePair(Of String, Integer) In TempiInLettere
                If Regex.IsMatch(minuscolo, "\b" & Regex.Escape(t.Key) & "\b") Then Return t.Value
            Next
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.TempoDaTesto: " & ex.Message)
        End Try
        Return 0
    End Function

    ''' <summary>L'indicazione di tempo scritta nella prima pagina di un PDF con il testo; 0 se non si trova (o nelle scansioni).</summary>
    Friend Shared Function TempoDaPdf(percorso As String) As Integer
        Try
            If Path.GetExtension(percorso).ToLowerInvariant() <> ".pdf" Then Return 0
            Using doc As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorso)
                Dim numero As Integer = 0
                For Each pagina In doc.GetPages()
                    numero += 1
                    If numero > 2 Then Exit For
                    Dim t As Integer = TempoDaTesto(pagina.Text)
                    If t > 0 Then Return t
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.TempoDaPdf: " & ex.Message)
        End Try
        Return 0
    End Function

    ''' <summary>Il tempo letto dal PDF al momento del riconoscimento di questo brano; 0 se non c'è.</summary>
    Public Shared Function TempoLetto(file As String) As Integer
        Try
            Dim f As String = Path.Combine(Path.GetDirectoryName(file), "tempo.txt")
            Dim n As Integer
            If System.IO.File.Exists(f) AndAlso Integer.TryParse(System.IO.File.ReadAllText(f, Encoding.UTF8).Trim(), n) Then Return n
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.TempoLetto: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Shared Async Function ScaricaAudiverisAsync(annullamento As CancellationToken, Optional avanzamento As Action(Of Integer) = Nothing) As Task(Of String)
        Dim cartella As String = CartellaAudiveris()
        Using client As New HttpClient()
            client.Timeout = TimeSpan.FromMinutes(30)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel")
            Dim json As String = Await client.GetStringAsync("https://api.github.com/repos/Audiveris/audiveris/releases/latest")
            Dim release As JObject = JObject.Parse(json)
            Dim url As String = ""
            Dim nome As String = ""
            For Each asset As JToken In CType(release("assets"), JArray)
                Dim n As String = CStr(asset("name"))
                If n.IndexOf("windowsConsole", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso n.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) Then
                    url = CStr(asset("browser_download_url"))
                    nome = n
                    Exit For
                End If
            Next
            If url = "" Then Throw New InvalidDataException("Non ho trovato la versione per Windows del programma di riconoscimento.")
            Dim msi As String = Path.Combine(cartella, nome)
            Using risposta As HttpResponseMessage = Await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, annullamento)
                risposta.EnsureSuccessStatusCode()
                Dim totale As Long = If(risposta.Content.Headers.ContentLength, 0L)
                Using origine As Stream = Await risposta.Content.ReadAsStreamAsync()
                    Using destinazione As New FileStream(msi, FileMode.Create, FileAccess.Write, FileShare.None, 81920)
                        Dim buffer(81919) As Byte
                        Dim letti As Long = 0
                        Dim n As Integer = Await origine.ReadAsync(buffer, 0, buffer.Length, annullamento)
                        While n > 0
                            Await destinazione.WriteAsync(buffer, 0, n, annullamento)
                            letti += n
                            If totale > 0 AndAlso avanzamento IsNot Nothing Then avanzamento(CInt(Math.Min(100L, letti * 100L \ totale)))
                            n = Await origine.ReadAsync(buffer, 0, buffer.Length, annullamento)
                        End While
                    End Using
                End Using
            End Using
            Dim estratto As String = Path.Combine(cartella, "programma")
            If Directory.Exists(estratto) Then Directory.Delete(estratto, True)
            Directory.CreateDirectory(estratto)
            Dim info As New ProcessStartInfo("msiexec.exe", "/a """ & msi & """ /qn TARGETDIR=""" & estratto & """")
            info.UseShellExecute = False
            info.CreateNoWindow = True
            Using p As Process = Process.Start(info)
                Await Task.Run(Sub() p.WaitForExit())
            End Using
            Try
                File.Delete(msi)
            Catch
            End Try
            Dim exe As String = TrovaAudiveris()
            If exe = "" Then Throw New FileNotFoundException("Il programma di riconoscimento non è stato installato correttamente.")
            Return exe
        End Using
    End Function

    Private Shared Function ImpronteCartella(percorso As String) As String
        Dim info As New FileInfo(percorso)
        Dim chiave As String = "v2|" & percorso.ToLowerInvariant() & "|" & info.Length.ToString() & "|" & info.LastWriteTimeUtc.Ticks.ToString()
        Using sha As SHA1 = SHA1.Create()
            Dim h As Byte() = sha.ComputeHash(Encoding.UTF8.GetBytes(chiave))
            Dim sb As New StringBuilder()
            For i As Integer = 0 To 5
                sb.Append(h(i).ToString("x2"))
            Next
            Return Path.GetFileNameWithoutExtension(percorso) & "-" & sb.ToString()
        End Using
    End Function

    Private Shared Function RisultatiInCartella(cartella As String) As List(Of String)
        Dim elenco As New List(Of String)()
        If Not Directory.Exists(cartella) Then Return elenco
        For Each f As String In Directory.EnumerateFiles(cartella, "*.*", SearchOption.AllDirectories)
            Dim e As String = Path.GetExtension(f).ToLowerInvariant()
            If e <> ".mxl" AndAlso e <> ".musicxml" Then Continue For
            ' le cartelle "-seconda" dei brani di uno stesso PDF sono seconde letture, non altri spartiti
            Dim relativa As String = Path.GetDirectoryName(f.Substring(cartella.Length).TrimStart("\"c, "/"c))
            If Not String.IsNullOrEmpty(relativa) AndAlso relativa.Split("\"c, "/"c).Any(Function(x) x.EndsWith("-seconda", StringComparison.OrdinalIgnoreCase)) Then Continue For
            elenco.Add(f)
        Next
        elenco.Sort(StringComparer.OrdinalIgnoreCase)
        Return elenco
    End Function

    ''' <summary>Il titolo di un brano riconosciuto: quello scritto in grande nel PDF, se c'era; altrimenti il nome del file.</summary>
    Public Shared Function TitoloBrano(file As String) As String
        Try
            Dim t As String = Path.ChangeExtension(file, ".titolo.txt")
            If System.IO.File.Exists(t) Then
                Dim righe As String() = System.IO.File.ReadAllLines(t, Encoding.UTF8)
                If righe.Length > 0 AndAlso righe(0).Trim() <> "" Then Return righe(0).Trim()
            End If
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.TitoloBrano: " & ex.Message)
        End Try
        Return Path.GetFileNameWithoutExtension(file)
    End Function

    ''' <summary>
    ''' Salva un brano riconosciuto in un file MusicXML compresso (mxl): la versione corretta se l'utente l'ha già aperto e sistemato,
    ''' altrimenti il riconoscimento con le correzioni sicure ricavate dal PDF. Restituisce vuoto se è andato bene, altrimenti il motivo.
    ''' </summary>
    Public Shared Function SalvaMusicXml(file As String, destinazione As String) As String
        Try
            Dim corretto As String = PercorsoCorretto(file)
            Dim daCorretto As Boolean = System.IO.File.Exists(corretto)
            Dim revisione As RevisioneMusicXml = RevisioneMusicXml.Apri(If(daCorretto, corretto, file))
            If Not daCorretto Then
                Dim glifi As RisultatoGlifi = LeggiGlifi(file)
                If glifi.Correzioni.Count > 0 Then revisione.ApplicaCorrezioniGlifi(glifi.Correzioni)
                revisione.RiparaDaiTipi()
            End If
            revisione.ImpostaTitoloSeMancante(TitoloBrano(file))
            revisione.Salva(destinazione, True)
            Return ""
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.SalvaMusicXml: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "Non è stato possibile salvare lo spartito: " & ex.Message
        End Try
    End Function

    ''' <summary>Un nome leggibile da un nome di file: senza l'impronta in fondo, senza i codici dell'indirizzo, con gli spazi.</summary>
    Public Shared Function NomeLeggibile(nomeFile As String) As String
        Dim n As String = Path.GetFileNameWithoutExtension(nomeFile)
        n = System.Text.RegularExpressions.Regex.Replace(n, "-[0-9a-f]{6,12}(-seconda)?$", "")
        Try
            n = Uri.UnescapeDataString(n)
        Catch
        End Try
        Return n.Replace("_", " ").Trim()
    End Function

    ''' <summary>Gli spartiti scaricati dagli archivi, i più recenti per primi.</summary>
    Public Shared Function ElencoScaricati() As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            Dim cartella As String = Path.Combine(CartellaSpartiti(), "scaricati")
            If Directory.Exists(cartella) Then
                elenco.AddRange(New DirectoryInfo(cartella).GetFiles().OrderByDescending(Function(f) f.LastWriteTimeUtc).Select(Function(f) f.FullName))
            End If
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.ElencoScaricati: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>Gli spartiti già riconosciuti: il nome e i file MusicXML ottenuti, i più recenti per primi.</summary>
    Public Shared Function ElencoRiconosciuti() As List(Of KeyValuePair(Of String, List(Of String)))
        Dim elenco As New List(Of KeyValuePair(Of String, List(Of String)))()
        Try
            Dim radice As String = Path.Combine(CartellaSpartiti(), "riconosciuti")
            If Not Directory.Exists(radice) Then Return elenco
            For Each d As DirectoryInfo In New DirectoryInfo(radice).GetDirectories().OrderByDescending(Function(x) x.LastWriteTimeUtc)
                ' la cartella "-seconda" è la seconda lettura dello stesso PDF, non un altro spartito
                If d.Name.EndsWith("-seconda", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim file As List(Of String) = RisultatiInCartella(d.FullName)
                If file.Count > 0 Then elenco.Add(New KeyValuePair(Of String, List(Of String))(NomeLeggibile(d.Name), file))
            Next
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.ElencoRiconosciuti: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>I file MusicXML già ottenuti per questo spartito, se è stato già riconosciuto; vuoto altrimenti.</summary>
    Public Shared Function GiaRiconosciuto(percorso As String) As List(Of String)
        Try
            Return RisultatiInCartella(Path.Combine(CartellaSpartiti(), "riconosciuti", ImpronteCartella(percorso)))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.GiaRiconosciuto: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>
    ''' Fa partire il riconoscimento in sottofondo, dal centro di controllo, dove si segue la percentuale: può durare anche dieci minuti
    ''' e nel frattempo si può usare il resto di WinGabriel. Alla fine alTermine riceve, sull'interfaccia, i file MusicXML ottenuti
    ''' (non viene chiamato se il riconoscimento è fallito o annullato: l'utente ne ha già avuto notizia).
    ''' </summary>
    Public Shared Sub Riconosci(percorso As String, alTermine As Action(Of List(Of String)), Optional cosaAccadeAllaFine As String = "Quando ha finito ti chiedo se vuoi ascoltarlo.")
        WGCentroDiControllo.RiconoscitoreSpartiti = AddressOf RiconosciInSottofondo
        WGCentroDiControllo.GetInstance().AggiungiRiconoscimentoSpartito(percorso, alTermine)
        AiutoMusica.Messaggio("Il riconoscimento dello spartito è partito in sottofondo. Puoi seguirne l'avanzamento, con la percentuale, dal centro di controllo. " & cosaAccadeAllaFine)
    End Sub

    ''' <summary>Dove Audiveris lascia la seconda lettura dello stesso spartito (fatta con un'altra scala), accanto alla prima.</summary>
    Friend Shared Function CartellaSeconda(cartellaPrimaLettura As String) As String
        Return cartellaPrimaLettura.TrimEnd("\"c, "/"c) & "-seconda"
    End Function

    ''' <summary>La seconda lettura del brano letto in primoFile, se c'è (stesso nome di file nella cartella gemella); vuoto altrimenti.</summary>
    Public Shared Function SecondaLettura(primoFile As String) As String
        Try
            Dim candidato As String = Path.Combine(CartellaSeconda(Path.GetDirectoryName(primoFile)), Path.GetFileName(primoFile))
            If File.Exists(candidato) Then Return candidato
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.SecondaLettura: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>Il controllo con l'immagine fatto al riconoscimento (vuoto se non c'è).</summary>
    Public Shared Function LeggiVerifica(file As String) As RisultatoVerifica
        Try
            Return RisultatoVerifica.Leggi(Path.Combine(Path.GetDirectoryName(file), "verifica.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.LeggiVerifica: " & ex.Message)
            Return New RisultatoVerifica()
        End Try
    End Function

    ''' <summary>Il risultato del confronto fra le due letture, salvato accanto al MusicXML la prima volta che lo spartito si apre.</summary>
    Public Shared Function LeggiConfronto(file As String) As RisultatoVerifica
        Try
            Return RisultatoVerifica.Leggi(Path.Combine(Path.GetDirectoryName(file), "confronto.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.LeggiConfronto: " & ex.Message)
            Return New RisultatoVerifica()
        End Try
    End Function

    Public Shared Sub ScriviConfronto(file As String, esito As EsitoConfronto)
        Try
            Dim r As New RisultatoVerifica()
            r.Dubbi.AddRange(esito.Dubbi)
            For Each k As Long In esito.Sostituite
                r.Sostituite.Add(k)
            Next
            r.Scrivi(Path.Combine(Path.GetDirectoryName(file), "confronto.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.ScriviConfronto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>I dubbi e le battute sostituite dai confronti fatti dopo, con un'altra lettura scelta dall'utente: stanno accanto alla copia corretta.</summary>
    Public Shared Function LeggiDubbiExtra(corretto As String) As RisultatoVerifica
        Try
            Return RisultatoVerifica.Leggi(Path.ChangeExtension(corretto, ".dubbi.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.LeggiDubbiExtra: " & ex.Message)
            Return New RisultatoVerifica()
        End Try
    End Function

    Public Shared Sub AggiungiDubbiExtra(corretto As String, esito As EsitoConfronto)
        Try
            Dim r As RisultatoVerifica = LeggiDubbiExtra(corretto)
            r.Dubbi.AddRange(esito.Dubbi)
            For Each k As Long In esito.Sostituite
                r.Sostituite.Add(k)
            Next
            r.Scrivi(Path.ChangeExtension(corretto, ".dubbi.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.AggiungiDubbiExtra: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Le correzioni e i dubbi ricavati leggendo i caratteri del PDF, se ci sono.</summary>
    Friend Shared Function LeggiGlifi(file As String) As RisultatoGlifi
        Try
            Return RisultatoGlifi.Leggi(Path.Combine(Path.GetDirectoryName(file), "glifi.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.LeggiGlifi: " & ex.Message)
            Return New RisultatoGlifi()
        End Try
    End Function

    ''' <summary>La distanza fra due linee del rigo nella prima pagina letta, in punti dell'immagine di Audiveris; 0 se non si sa.</summary>
    Private Shared Function InterlineaDelLavoro(omr As String) As Double
        Try
            If omr = "" Then Return 0.0
            Using zip As System.IO.Compression.ZipArchive = System.IO.Compression.ZipFile.OpenRead(omr)
                Dim valori As New List(Of Double)()
                For n As Integer = 1 To 6
                    Dim voce As System.IO.Compression.ZipArchiveEntry = zip.GetEntry("sheet#" & n.ToString() & "/sheet#" & n.ToString() & ".xml")
                    If voce Is Nothing Then Continue For
                    Using s As Stream = voce.Open()
                        Dim doc As System.Xml.Linq.XDocument = System.Xml.Linq.XDocument.Load(s)
                        Dim scala As System.Xml.Linq.XElement = doc.Root.Element("scale")
                        If scala IsNot Nothing AndAlso scala.Element("interline") IsNot Nothing AndAlso scala.Element("interline").Attribute("main") IsNot Nothing Then
                            Dim v As Double
                            If Double.TryParse(scala.Element("interline").Attribute("main").Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) AndAlso v > 0 Then valori.Add(v)
                        End If
                    End Using
                Next
                If valori.Count = 0 Then Return 0.0
                valori.Sort()
                Return valori(valori.Count \ 2)
            End Using
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.InterlineaDelLavoro: " & ex.Message)
            Return 0.0
        End Try
    End Function

    ''' <summary>Legge i caratteri del PDF accanto al lavoro di Audiveris di una cartella e scrive accanto al MusicXML il file glifi.txt.</summary>
    Private Shared Sub GlifiCartella(cartella As String, risultati As List(Of String), pdf As String, inizio As Integer, fine As Integer, fase As String, progresso As Action(Of Integer, String), annulla As Func(Of Boolean))
        Try
            If risultati.Count <> 1 Then Return
            Dim omr As String = PrimoOmr(cartella)
            If omr = "" Then Return
            Dim r As RisultatoGlifi = LetturaGlifi.Analizza(pdf, omr, risultati(0),
                Sub(percento As Integer) progresso(inizio + CInt((fine - inizio) * percento / 100.0), fase & ", " & percento.ToString() & " per cento"),
                annulla)
            If annulla() Then Return
            log.ScriviLog("RiconoscimentoSpartiti.GlifiCartella (" & fase & "): " & r.Riassunto)
            r.Scrivi(Path.Combine(Path.GetDirectoryName(risultati(0)), "glifi.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.GlifiCartella: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function PrimoOmr(cartella As String) As String
        If Not Directory.Exists(cartella) Then Return ""
        Dim elenco As String() = Directory.GetFiles(cartella, "*.omr", SearchOption.TopDirectoryOnly)
        Return If(elenco.Length = 1, elenco(0), "")
    End Function

    ' I passi che Audiveris compie su ogni pagina, nell'ordine in cui li scrive nel registro, e quanto durano in proporzione (secondi
    ' misurati su una pagina d'orchestra): servono a far avanzare la percentuale in modo regolare.
    Private Shared ReadOnly PassiAudiveris() As String = {"LOAD", "BINARY", "SCALE", "GRID", "HEADERS", "STEM_SEEDS", "BEAMS", "LEDGERS", "HEADS", "STEMS",
                                                          "REDUCTION", "CUE_BEAMS", "TEXTS", "MEASURES", "CHORDS", "CURVES", "SYMBOLS", "LINKS", "RHYTHMS", "PAGE"}
    Private Shared ReadOnly PesiAudiveris() As Double = {0.25, 0.26, 0.01, 0.35, 1.3, 0.04, 2.3, 0.26, 2.4, 0.5,
                                                         0.07, 0.0, 2.9, 0.01, 0.01, 0.7, 0.6, 0.05, 0.06, 1.0}

    ''' <summary>Quanta parte del lavoro di una pagina è già fatta quando Audiveris comincia il passo indicato (da 0 a 1).</summary>
    Private Shared Function FrazionePagina(passo As Integer) As Double
        Dim totale As Double = PesiAudiveris.Sum()
        Dim fatto As Double = 0
        For i As Integer = 0 To Math.Min(passo, PesiAudiveris.Length) - 1
            fatto += PesiAudiveris(i)
        Next
        Return fatto / totale
    End Function

    ''' <summary>Fa girare Audiveris su un ingresso e scrive i risultati in cartellaUscita; la percentuale va da inizio a fine.</summary>
    Private Shared Sub EseguiAudiveris(exe As String, ingresso As String, cartellaUscita As String, inizio As Integer, fine As Integer, fase As String, progresso As Action(Of Integer, String), annulla As Func(Of Boolean), processoAvviato As Action(Of Process), uscita As StringBuilder)
        Dim info As New ProcessStartInfo(exe, "-batch -export -option org.audiveris.omr.sheet.ProcessingSwitches.fingerings=true -output """ & cartellaUscita & """ """ & ingresso & """")
        info.UseShellExecute = False
        info.CreateNoWindow = True
        info.RedirectStandardOutput = True
        info.RedirectStandardError = True
        info.WorkingDirectory = Path.GetDirectoryName(exe)
        Dim fogli As Integer = 0
        Dim foglio As Integer = 1
        Dim passo As Integer = 0
        Dim sulRiga As DataReceivedEventHandler = Sub(s As Object, d As DataReceivedEventArgs)
                                                      If d.Data Is Nothing Then Return
                                                      SyncLock uscita
                                                          If uscita.Length < 40000 Then uscita.AppendLine(d.Data)
                                                      End SyncLock
                                                      Dim n As Match = Regex.Match(d.Data, "(\d+) sheets? in ")
                                                      If n.Success AndAlso fogli = 0 Then fogli = CInt(n.Groups(1).Value)
                                                      Dim p As Match = Regex.Match(d.Data, "#(\d+)\]\s+StepMonitoring \d+\s*\|\s*(\w+)")
                                                      If Not p.Success Then Return
                                                      foglio = Math.Max(foglio, CInt(p.Groups(1).Value))
                                                      passo = Math.Max(0, Array.IndexOf(PassiAudiveris, p.Groups(2).Value))
                                                      Dim totaleFogli As Integer = Math.Max(fogli, foglio)
                                                      Dim frazione As Double = ((foglio - 1) + FrazionePagina(passo)) / totaleFogli
                                                      progresso(inizio + CInt((fine - inizio) * Math.Min(1.0, frazione)), fase & ", pagina " & foglio.ToString() & If(fogli > 0, " di " & fogli.ToString(), ""))
                                                  End Sub
        Dim processo As Process = Process.Start(info)
        If processoAvviato IsNot Nothing Then processoAvviato(processo)
        AddHandler processo.OutputDataReceived, sulRiga
        AddHandler processo.ErrorDataReceived, sulRiga
        processo.BeginOutputReadLine()
        processo.BeginErrorReadLine()
        While Not processo.WaitForExit(500)
            If annulla() Then
                Try
                    processo.Kill()
                Catch
                End Try
                Throw New OperationCanceledException()
            End If
        End While
        processo.WaitForExit()
        If annulla() Then Throw New OperationCanceledException()
    End Sub

    ''' <summary>Confronta con l'immagine ogni brano riconosciuto in cartella e scrive accanto al MusicXML il file verifica.txt.</summary>
    Private Shared Sub VerificaCartella(cartella As String, risultati As List(Of String), inizio As Integer, fine As Integer, fase As String, progresso As Action(Of Integer, String), annulla As Func(Of Boolean))
        Try
            If risultati.Count <> 1 Then Return ' con più brani nello stesso file non si sa a quale brano appartiene ogni pagina
            Dim omr As String = PrimoOmr(cartella)
            If omr = "" Then Return
            Dim r As RisultatoVerifica = VerificaConImmagine.Analizza(omr,
                Sub(percento As Integer) progresso(inizio + CInt((fine - inizio) * percento / 100.0), fase & ", " & percento.ToString() & " per cento delle pagine"),
                annulla)
            If annulla() Then Return
            r.Scrivi(Path.Combine(Path.GetDirectoryName(risultati(0)), "verifica.txt"))
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.VerificaCartella: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Il riconoscimento vero e proprio, da eseguire su un thread di lavoro: lo chiama il centro di controllo. progresso riceve la
    ''' percentuale (da 0 a 100) e una frase che dice a che punto si è; annulla dice se l'utente ha rinunciato; processoAvviato riceve il
    ''' programma di riconoscimento appena parte, perché si possa fermare. Restituisce i file MusicXML; se è vuoto, in errore c'è il motivo
    ''' (vuoto se l'utente ha annullato).
    '''
    ''' Una scansione si legge due volte, a due scale diverse, e poi si controlla con l'immagine: due letture sbagliano in punti diversi,
    ''' e dove coincidono la battuta è sicura. Un PDF esportato da un programma di notazione si legge una volta sola, con il controllo.
    ''' </summary>
    Friend Shared Function RiconosciInSottofondo(percorso As String, progresso As Action(Of Integer, String), annulla As Func(Of Boolean), processoAvviato As Action(Of Process), ByRef errore As String) As List(Of String)
        errore = ""
        Dim trovati As New List(Of String)()
        Dim cartellaRisultati As String = Path.Combine(CartellaSpartiti(), "riconosciuti", ImpronteCartella(percorso))
        Try
            progresso(0, "preparazione")
            Dim exe As String = TrovaAudiveris()
            Dim quotaScarico As Integer = 0
            If exe = "" Then
                quotaScarico = 15
                progresso(1, "scarico il programma che riconosce gli spartiti, circa duecento megabyte")
                Dim interruzione As New CancellationTokenSource()
                exe = ScaricaAudiverisAsync(interruzione.Token, Sub(percento As Integer)
                                                                    If annulla() Then interruzione.Cancel()
                                                                    progresso(1 + CInt(percento * (quotaScarico - 2) / 100.0), "scarico il programma che riconosce gli spartiti, " & percento.ToString() & " per cento")
                                                                End Sub).GetAwaiter().GetResult()
            End If
            If annulla() Then Throw New OperationCanceledException()
            If Directory.Exists(cartellaRisultati) Then Directory.Delete(cartellaRisultati, True)
            Directory.CreateDirectory(cartellaRisultati)
            Dim cartellaDoppia As String = CartellaSeconda(cartellaRisultati)
            If Directory.Exists(cartellaDoppia) Then Directory.Delete(cartellaDoppia, True)

            ' un titolo scritto in grande in una pagina comincia un altro spartito: ognuno si riconosce per conto suo
            Dim sezioni As List(Of SezioneSpartito) = SezioniDaTitoliGrandi(percorso)
            If sezioni.Count >= 2 Then
                Return RiconosciSezioni(exe, percorso, sezioni, cartellaRisultati, quotaScarico, progresso, annulla, processoAvviato, errore)
            End If

            Dim scalato As Action(Of Integer, String) = Sub(valore As Integer, testo As String) progresso(quotaScarico + CInt((100 - quotaScarico) * valore / 100.0), testo)
            Return RiconosciUno(exe, percorso, cartellaRisultati, scalato, annulla, processoAvviato, errore)
        Catch ex As OperationCanceledException
            errore = ""
            Return New List(Of String)()
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.Riconosci: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Non è stato possibile riconoscere lo spartito: " & ex.Message
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>
    ''' Gli spartiti di un PDF che ne contiene più d'uno, separati da un titolo scritto in grande: ognuno è un PDF a parte, riconosciuto
    ''' e controllato come se fosse un file suo, in una cartella parte-01, parte-02... dentro quella del PDF. Il titolo resta accanto al file.
    ''' </summary>
    Private Shared Function RiconosciSezioni(exe As String, percorso As String, sezioni As List(Of SezioneSpartito), cartellaRadice As String, quotaInizio As Integer, progresso As Action(Of Integer, String), annulla As Func(Of Boolean), processoAvviato As Action(Of Process), ByRef errore As String) As List(Of String)
        errore = ""
        Dim lavoro As String = Path.Combine(CartellaSpartiti(), "lavoro", Guid.NewGuid().ToString("N"))
        Dim ultimoErrore As String = ""
        Try
            Directory.CreateDirectory(lavoro)
            Using doc As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorso)
                For i As Integer = 0 To sezioni.Count - 1
                    If annulla() Then Throw New OperationCanceledException()
                    Dim s As SezioneSpartito = sezioni(i)
                    Dim nomeParte As String = "parte-" & (i + 1).ToString("00")
                    Dim pdfParte As String = Path.Combine(lavoro, nomeParte & ".pdf")
                    Try
                        Dim costruttore As New UglyToad.PdfPig.Writer.PdfDocumentBuilder()
                        For pagina As Integer = s.PrimaPagina To s.UltimaPagina
                            costruttore.AddPage(doc, pagina)
                        Next
                        File.WriteAllBytes(pdfParte, costruttore.Build())
                    Catch ex As Exception
                        log.ScriviLog("RiconoscimentoSpartiti.RiconosciSezioni (taglio del PDF): " & ex.Message & Environment.NewLine & ex.StackTrace)
                        ultimoErrore = "Non sono riuscito a separare gli spartiti di questo PDF."
                        Continue For
                    End Try
                    Dim da As Integer = quotaInizio + CInt((100 - quotaInizio) * i / sezioni.Count)
                    Dim a As Integer = quotaInizio + CInt((100 - quotaInizio) * (i + 1) / sezioni.Count)
                    Dim fase As String = "spartito " & (i + 1).ToString() & " di " & sezioni.Count.ToString()
                    Dim scalato As Action(Of Integer, String) = Sub(valore As Integer, testo As String) progresso(da + CInt((a - da) * valore / 100.0), fase & ", " & testo)
                    Dim errParte As String = ""
                    Dim cartellaParte As String = Path.Combine(cartellaRadice, nomeParte)
                    Dim risultatiParte As List(Of String) = RiconosciUno(exe, pdfParte, cartellaParte, scalato, annulla, processoAvviato, errParte)
                    If annulla() Then Throw New OperationCanceledException()
                    If risultatiParte.Count = 0 Then
                        If errParte <> "" Then ultimoErrore = errParte
                        log.ScriviLog("RiconoscimentoSpartiti.RiconosciSezioni: nessun risultato per " & nomeParte & " (" & s.Titolo & "), pagine " & s.PrimaPagina.ToString() & "-" & s.UltimaPagina.ToString() & ". " & errParte)
                        Continue For
                    End If
                    For k As Integer = 0 To risultatiParte.Count - 1
                        Dim titolo As String = If(s.Titolo <> "", s.Titolo, "Spartito " & (i + 1).ToString())
                        If risultatiParte.Count > 1 Then titolo &= ", brano " & (k + 1).ToString()
                        File.WriteAllText(Path.ChangeExtension(risultatiParte(k), ".titolo.txt"), titolo, New UTF8Encoding(True))
                    Next
                Next
            End Using
        Catch ex As OperationCanceledException
            errore = ""
            Return New List(Of String)()
        Finally
            Try
                If Directory.Exists(lavoro) Then Directory.Delete(lavoro, True)
            Catch
            End Try
        End Try
        Dim trovati As List(Of String) = RisultatiInCartella(cartellaRadice)
        If trovati.Count = 0 Then errore = If(ultimoErrore <> "", ultimoErrore, "Non sono riuscito a riconoscere le note di questo spartito. Prova con una scansione più nitida o con un PDF di migliore qualità.")
        progresso(100, "finito")
        Return trovati
    End Function

    ''' <summary>
    ''' I pezzi di un PDF con il testo (esportato da un programma di notazione) separati da un titolo scritto in grande: una pagina il
    ''' cui titolo, più grande del testo di tutto il resto, è diverso da quello dello spartito in corso comincia un altro spartito.
    ''' Vuoto se il PDF è uno solo. Nelle scansioni non c'è testo da leggere e si lascia fare ad Audiveris.
    ''' </summary>
    Friend Shared Function SezioniDaTitoliGrandi(percorso As String) As List(Of SezioneSpartito)
        Dim sezioni As New List(Of SezioneSpartito)()
        Try
            If Path.GetExtension(percorso).ToLowerInvariant() <> ".pdf" Then Return sezioni
            Dim paginePerParole As New List(Of List(Of ParolaDiTitolo))()
            Dim altezze As New List(Of Double)()
            Dim altezzePagine As New List(Of Double)()
            Using doc As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorso)
                Dim numero As Integer = 0
                For Each pagina In doc.GetPages()
                    numero += 1
                    If numero > 400 Then Exit For
                    Dim parole As New List(Of ParolaDiTitolo)()
                    For Each w In pagina.GetWords()
                        Dim lettere = w.Letters
                        If lettere.Count < 2 Then Continue For
                        If w.Text.Count(Function(c) Char.IsLetter(c)) < 2 Then Continue For
                        Dim misure As List(Of Double) = lettere.Select(Function(l) l.PointSize).OrderBy(Function(x) x).ToList()
                        Dim misura As Double = misure(misure.Count \ 2)
                        If misura <= 0 Then Continue For
                        Dim p As New ParolaDiTitolo()
                        p.Testo = w.Text
                        p.Misura = misura
                        p.Sotto = w.BoundingBox.Bottom
                        p.Sinistra = w.BoundingBox.Left
                        parole.Add(p)
                        altezze.Add(misura)
                    Next
                    paginePerParole.Add(parole)
                    altezzePagine.Add(pagina.Height)
                Next
            End Using
            If paginePerParole.Count < 2 OrElse altezze.Count < 10 Then Return sezioni
            altezze.Sort()
            Dim soglia As Double = Math.Max(altezze(altezze.Count \ 2) * 1.5, 14.0)

            Dim titoloInCorso As String = ""
            For n As Integer = 0 To paginePerParole.Count - 1
                Dim titolo As String = TitoloGrandeDellaPagina(paginePerParole(n), altezzePagine(n), soglia)
                Dim chiave As String = New String(titolo.Where(Function(c) Char.IsLetterOrDigit(c)).ToArray()).ToLowerInvariant()
                If sezioni.Count = 0 OrElse (chiave <> "" AndAlso chiave <> titoloInCorso) Then
                    Dim nuova As New SezioneSpartito()
                    nuova.Titolo = titolo
                    nuova.PrimaPagina = n + 1
                    sezioni.Add(nuova)
                    titoloInCorso = chiave
                End If
                sezioni(sezioni.Count - 1).UltimaPagina = n + 1
            Next
            ' se la prima pagina non ha titolo e la seconda sezione comincia subito dopo, non c'è niente da separare
            If sezioni.Count < 2 Then sezioni.Clear()
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.SezioniDaTitoliGrandi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            sezioni.Clear()
        End Try
        Return sezioni
    End Function

    ''' <summary>Il titolo scritto in grande nella parte alta della pagina, o vuoto se non c'è.</summary>
    Private Shared Function TitoloGrandeDellaPagina(parole As List(Of ParolaDiTitolo), altezzaPagina As Double, soglia As Double) As String
        Dim grandi As List(Of ParolaDiTitolo) = parole.Where(Function(p) p.Misura >= soglia AndAlso p.Sotto >= altezzaPagina * 0.4).ToList()
        If grandi.Count = 0 Then Return ""
        Dim massima As Double = grandi.Max(Function(p) p.Misura)
        Dim titolo As List(Of ParolaDiTitolo) = grandi.Where(Function(p) p.Misura >= massima * 0.85).ToList()
        ' più righe dello stesso titolo non sono altri titoli: conta la riga più in alto
        Dim riga As Double = titolo.Max(Function(p) p.Sotto)
        Dim testo As String = String.Join(" ", titolo.Where(Function(p) Math.Abs(p.Sotto - riga) <= massima * 0.6).OrderBy(Function(p) p.Sinistra).Select(Function(p) p.Testo)).Trim()
        If testo.Count(Function(c) Char.IsLetter(c)) < 3 Then Return ""
        Return testo
    End Function

    ''' <summary>
    ''' Il riconoscimento di un solo spartito (un PDF, una foto o un pezzo di PDF) nella cartella indicata. progresso va da 0 a 100.
    ''' </summary>
    Private Shared Function RiconosciUno(exe As String, percorso As String, cartellaRisultati As String, progresso As Action(Of Integer, String), annulla As Func(Of Boolean), processoAvviato As Action(Of Process), ByRef errore As String) As List(Of String)
        errore = ""
        Dim trovati As New List(Of String)()
        Try
            Dim quotaScarico As Integer = 0
            If Directory.Exists(cartellaRisultati) Then Directory.Delete(cartellaRisultati, True)
            Directory.CreateDirectory(cartellaRisultati)
            Dim cartellaDoppia As String = CartellaSeconda(cartellaRisultati)
            If Directory.Exists(cartellaDoppia) Then Directory.Delete(cartellaDoppia, True)

            progresso(quotaScarico, "pulizia e raddrizzamento delle pagine")
            Dim esitoPreparazione As String = ""
            Dim ingresso As String = PreparazioneScansione.Prepara(percorso, Path.Combine(CartellaSpartiti(), "lavoro", Guid.NewGuid().ToString("N")), esitoPreparazione)
            log.ScriviLog("RiconoscimentoSpartiti.Riconosci: " & esitoPreparazione)
            If annulla() Then Throw New OperationCanceledException()
            If ingresso = "" Then
                errore = esitoPreparazione
                Return trovati
            End If
            Dim eScansione As Boolean = Not String.Equals(ingresso, percorso, StringComparison.OrdinalIgnoreCase)

            Dim uscita As New StringBuilder()
            Dim inizio As Integer = quotaScarico + 4
            Dim eVettoriale As Boolean = Not eScansione AndAlso String.Equals(Path.GetExtension(percorso), ".pdf", StringComparison.OrdinalIgnoreCase)
            Dim finePrima As Integer = If(eScansione OrElse eVettoriale, 55, 90)
            EseguiAudiveris(exe, ingresso, cartellaRisultati, inizio, finePrima, "riconoscimento delle note", progresso, annulla, processoAvviato, uscita)

            trovati = RisultatiInCartella(cartellaRisultati)
            Try
                Dim nomi As List(Of String) = NomiStrumentiDaPdf(percorso)
                If nomi.Count > 0 AndAlso trovati.Count > 0 Then File.WriteAllLines(Path.Combine(Path.GetDirectoryName(trovati(0)), "nomi-parti.txt"), nomi, New UTF8Encoding(True))
            Catch ex As Exception
                log.ScriviLog("RiconoscimentoSpartiti.Riconosci (nomi): " & ex.Message)
            End Try
            Try
                Dim battiti As Integer = TempoDaPdf(percorso)
                If battiti > 0 AndAlso trovati.Count > 0 Then File.WriteAllText(Path.Combine(Path.GetDirectoryName(trovati(0)), "tempo.txt"), battiti.ToString(), New UTF8Encoding(True))
            Catch ex As Exception
                log.ScriviLog("RiconoscimentoSpartiti.Riconosci (tempo): " & ex.Message)
            End Try
            If trovati.Count = 0 Then
                log.ScriviLog("RiconoscimentoSpartiti.Riconosci: nessun risultato. Uscita: " & uscita.ToString())
                If uscita.ToString().Contains("No regularly spaced lines found") Then
                    errore = "In questo PDF non ci sono righi musicali: non è uno spartito, forse è un libro. Scegli un altro file."
                Else
                    errore = "Non sono riuscito a riconoscere le note di questo spartito. Prova con una scansione più nitida o con un PDF di migliore qualità."
                End If
                Return trovati
            End If

            ' la seconda lettura, a una scala diversa, solo per le scansioni
            If eScansione Then
                Try
                    progresso(finePrima, "seconda lettura: preparazione delle pagine")
                    Dim esito2 As String = ""
                    Dim ingresso2 As String = PreparazioneScansione.Prepara(percorso, Path.Combine(CartellaSpartiti(), "lavoro", Guid.NewGuid().ToString("N")), esito2, 26.0)
                    If annulla() Then Throw New OperationCanceledException()
                    If ingresso2 <> "" AndAlso Not String.Equals(ingresso2, percorso, StringComparison.OrdinalIgnoreCase) Then
                        Directory.CreateDirectory(cartellaDoppia)
                        EseguiAudiveris(exe, ingresso2, cartellaDoppia, finePrima, 88, "seconda lettura a scala diversa", progresso, annulla, processoAvviato, uscita)
                    End If
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    log.ScriviLog("RiconoscimentoSpartiti.Riconosci (seconda lettura): " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Try
                        If Directory.Exists(cartellaDoppia) Then Directory.Delete(cartellaDoppia, True)
                    Catch
                    End Try
                End Try
            End If

            ' la seconda lettura di un PDF vero: le pagine si disegnano con il lettore di PDF di Windows a una scala più grande
            If eVettoriale Then
                Try
                    Dim interlinea As Double = InterlineaDelLavoro(PrimoOmr(cartellaRisultati))
                    If interlinea > 0 Then
                        Dim obiettivo As Double = If(interlinea < 23.0, 26.0, 21.0)
                        Dim esito3 As String = ""
                        Dim ingresso3 As String = PreparazioneScansione.RenderizzaPdf(percorso, Path.Combine(CartellaSpartiti(), "lavoro", Guid.NewGuid().ToString("N")), obiettivo / interlinea, esito3,
                            Sub(pagina As Integer, totale As Integer) progresso(finePrima + CInt(5L * pagina \ Math.Max(1, totale)), "seconda lettura, disegno le pagine, pagina " & pagina.ToString() & " di " & totale.ToString()),
                            annulla)
                        log.ScriviLog("RiconoscimentoSpartiti.Riconosci (seconda lettura del PDF): " & esito3)
                        If ingresso3 <> "" Then
                            Directory.CreateDirectory(cartellaDoppia)
                            EseguiAudiveris(exe, ingresso3, cartellaDoppia, finePrima + 5, 88, "seconda lettura a scala diversa", progresso, annulla, processoAvviato, uscita)
                        End If
                    End If
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    log.ScriviLog("RiconoscimentoSpartiti.Riconosci (seconda lettura del PDF): " & ex.Message & Environment.NewLine & ex.StackTrace)
                    Try
                        If Directory.Exists(cartellaDoppia) Then Directory.Delete(cartellaDoppia, True)
                    Catch
                    End Try
                End Try
            End If

            ' il confronto con l'immagine, per ogni lettura
            progresso(88, "confronto con l'immagine delle pagine")
            VerificaCartella(cartellaRisultati, trovati, 88, 91, "confronto con l'immagine, prima lettura", progresso, annulla)
            If Directory.Exists(cartellaDoppia) Then
                VerificaCartella(cartellaDoppia, RisultatiInCartella(cartellaDoppia), 91, 93, "confronto con l'immagine, seconda lettura", progresso, annulla)
            End If
            ' i caratteri del PDF, se è un PDF vero
            If eVettoriale Then
                GlifiCartella(cartellaRisultati, trovati, percorso, 93, 96, "lettura dei caratteri del PDF, prima lettura", progresso, annulla)
                If Directory.Exists(cartellaDoppia) Then
                    GlifiCartella(cartellaDoppia, RisultatiInCartella(cartellaDoppia), percorso, 96, 99, "lettura dei caratteri del PDF, seconda lettura", progresso, annulla)
                End If
            End If
            progresso(100, "finito")
        Catch ex As OperationCanceledException
            errore = ""
            trovati = New List(Of String)()
        Catch ex As Exception
            log.ScriviLog("RiconoscimentoSpartiti.Riconosci: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Non è stato possibile riconoscere lo spartito: " & ex.Message
            trovati = New List(Of String)()
        End Try
        Return trovati
    End Function

End Class

''' <summary>Un pezzo di PDF che è uno spartito a sé: il titolo scritto in grande e le pagine (da 1) in cui si trova.</summary>
Friend NotInheritable Class SezioneSpartito
    Public Titolo As String = ""
    Public PrimaPagina As Integer
    Public UltimaPagina As Integer
End Class

Friend NotInheritable Class ParolaDiTitolo
    Public Testo As String = ""
    Public Misura As Double
    Public Sotto As Double
    Public Sinistra As Double
End Class
