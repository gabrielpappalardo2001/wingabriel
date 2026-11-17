Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Security.Cryptography
Imports System.Text
Imports System.Web.Script.Serialization
Imports System.Threading

''' <summary>
''' I rilasci firmati di WinGabriel e del lettore di schermo. Sul sito, in rilasci/&lt;prodotto&gt;/, ci sono due puntatori (stable.txt e
''' beta.txt: prima riga i dati in JSON, seconda riga la firma RSA SHA-256 in base64) e le versioni (v/&lt;versione&gt;/manifest.json con i
''' file in v/&lt;versione&gt;/f/). La firma si controlla con la chiave pubblica qui sotto; il puntatore dice l'impronta del manifest e il
''' manifest l'impronta di ogni file: se anche solo una cosa non torna, il rilascio non si usa. La chiave privata sta solo sul
''' computer di chi pubblica. Questo file è uguale in WinGabriel, nell'aggiornatore e nell'aggiornatore del lettore.
''' Se il sito non risponde, puntatore, manifest e file si prendono in silenzio dai rilasci di GitHub (stessa firma, stesse impronte):
''' ogni versione su GitHub è un rilascio con il puntatore (&lt;prodotto&gt;-&lt;canale&gt;.txt) e il manifest (&lt;prodotto&gt;-manifest.json);
''' i file stanno nel rilascio "file", ognuno una volta sola con il nome h.&lt;impronta&gt;.
''' </summary>
Friend NotInheritable Class RilascioFirmato

    Public Const BASE_RILASCI As String = "https://www.wingabriel.net/rilasci/"
    Public Const CANALE_STABILE As String = "stable"
    Public Const CANALE_BETA As String = "beta"

    ''' <summary>Il repository pubblico di WinGabriel: i suoi rilasci sono la riserva quando il sito non risponde.</summary>
    Public Const REPO_GITHUB As String = "gabrielpappalardo2001/wingabriel"
    Private Const API_RILASCI_GITHUB As String = "https://api.github.com/repos/" & REPO_GITHUB & "/releases?per_page=30"
    Public Const BASE_DOWNLOAD_GITHUB As String = "https://github.com/" & REPO_GITHUB & "/releases/download/"
    ''' <summary>Il rilascio di GitHub che contiene i file di tutte le versioni, ognuno una volta sola (nome h.&lt;impronta&gt;).</summary>
    Public Const TAG_FILE_GITHUB As String = "file"
    Private Shared ReadOnly _impronteGithub As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.OrdinalIgnoreCase)
    ' Quando il sito non ha risposto, per qualche minuto si va subito su GitHub invece di aspettare ogni volta il limite di tempo.
    Private Shared _sitoNonRispondeDal As DateTime = DateTime.MinValue

    Private Const CHIAVE_PUBBLICA As String = "<RSAKeyValue><Modulus>sDDPIyXZS5qeaxTLdHvLJi2Uj9PESDgJXq9fhvmGFVtAfnTHGPGlgZG0M5EmBIRXXahBLfCbpO6ZfPNQXu3j+p+xS3EvVOfTIXtx1rmHBuLD1oF76NaUg7ottVnHcaKsrkz49F0jOyzuTHvT6UMfOzHx7bvScUhczVwH9QpVP8sHZyDsB5WYtsW5auPpkhtMrZQciYkVYGuyLf+e8ncfw/G0KElYIhTE6aWlgi16LSQi8iKmcb/JkQn1uYA1o7plSCLhqnniAdSGtYF6vvJ+ZWtEX2uI12X+boZp2mKVWwvOakm9nSAj4QST/FUcvJHOWi8EXOGxRhXUsHco8qFTtigsIlzf0hhqlW1VWRVY4HusQJPtIWB+Vr6cmN/0c6zFNh7L4NzUAkqUe02NMKuwgH/UCFYPzOsRrcNf9t7Y14GPdU+DgSKIt/IwNqFOOYxSv8GdGTqNLICLUzRI/PqQsZw6+FGNdUbihFjpt6D91Xt/jOi2Tcj+NmK9RuCn5mOd</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"

    Public NotInheritable Class Puntatore
        Public Prodotto As String = ""
        Public Canale As String = ""
        Public Versione As String = ""
        Public ManifestSha256 As String = ""
        Public Data As String = ""
        Public Note As String = ""
        ''' <summary>Il nome della versione scelto da chi pubblica (quello che WinGabriel mostra); se manca vale Versione.</summary>
        Public Nome As String = ""
        Public Sequenza As Long = 0
        ''' <summary>Vuoto se il puntatore viene dal sito; altrimenti il tag del rilascio di GitHub da cui viene.</summary>
        Public TagGithub As String = ""
    End Class

    Public NotInheritable Class VoceManifest
        Public Percorso As String = ""
        Public Dimensione As Long = -1
        Public Impronta As String = ""
        Public DataUtc As String = ""
        Public Libmpv As Boolean = False
        Public UrlX86 As String = ""
        Public UrlX64 As String = ""
        Public DimensioneX86 As Long = -1
        Public DimensioneX64 As Long = -1
        Public ImprontaX86 As String = ""
        Public ImprontaX64 As String = ""
    End Class

    Public NotInheritable Class Manifest
        Public Puntatore As Puntatore
        Public Cartelle As New List(Of String)()
        Public Voci As New List(Of VoceManifest)()
        ''' <summary>L'indirizzo da cui si scaricano i file, con la barra finale.</summary>
        Public UrlBaseFile As String = ""
    End Class

    Private Shared ReadOnly _bloccoMemoria As New Object()
    Private Shared _memoria As New Dictionary(Of String, Tuple(Of Puntatore, DateTime))()

    Private Sub New()
    End Sub

    ''' <summary>
    ''' File che stanno sul server ma non fanno parte del programma: i file del vecchio sistema (script PHP, .htaccess), le copie di
    ''' riserva (.bak) e il registro che WinGabriel scrive accanto a sé. Non si scaricano mai, qualunque cosa dica l'elenco.
    ''' </summary>
    Public Shared Function FileDaNonScaricare(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim nome As String = Path.GetFileName(percorso.Replace("/"c, Path.DirectorySeparatorChar)).ToLowerInvariant()
            If nome.EndsWith(".php") OrElse nome.EndsWith(".aspx") OrElse nome.EndsWith(".asax") Then Return True
            If nome.Contains(".php.") OrElse nome.Contains(".bak") Then Return True
            If nome = ".htaccess" OrElse nome = "web.config" OrElse nome = ".user.ini" Then Return True
            If nome = "logwingabriel.txt" Then Return True
            Return False
        Catch
            Return False
        End Try
    End Function

    Private Shared Function Scarica(url As String, attesaMs As Integer) As Byte()
        ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
        Dim richiesta As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
        richiesta.Method = "GET"
        richiesta.UserAgent = "WinGabriel/1.0"
        richiesta.Timeout = attesaMs
        richiesta.ReadWriteTimeout = attesaMs
        richiesta.KeepAlive = False
        richiesta.Headers.Add("Cache-Control", "no-cache")
        richiesta.Headers.Add("Pragma", "no-cache")
        Using risposta As HttpWebResponse = CType(richiesta.GetResponse(), HttpWebResponse)
            Using flusso As Stream = risposta.GetResponseStream()
                Using memoria As New MemoryStream()
                    flusso.CopyTo(memoria)
                    Return memoria.ToArray()
                End Using
            End Using
        End Using
    End Function

    Private Shared Function FirmaValida(dati As Byte(), firmaBase64 As String) As Boolean
        Try
            Using rsa As New RSACryptoServiceProvider()
                rsa.FromXmlString(CHIAVE_PUBBLICA)
                Return rsa.VerifyData(dati, Convert.FromBase64String(firmaBase64.Trim()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            End Using
        Catch
            Return False
        End Try
    End Function

    Private Shared Function Testo(dizionario As Dictionary(Of String, Object), chiave As String) As String
        Dim v As Object = Nothing
        If dizionario.TryGetValue(chiave, v) AndAlso v IsNot Nothing Then Return Convert.ToString(v, CultureInfo.InvariantCulture)
        Return ""
    End Function

    Private Shared Function Numero(dizionario As Dictionary(Of String, Object), chiave As String, predefinito As Long) As Long
        Dim v As Object = Nothing
        If dizionario.TryGetValue(chiave, v) AndAlso v IsNot Nothing Then
            Dim risultato As Long
            If Long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, risultato) Then Return risultato
        End If
        Return predefinito
    End Function

    Private Const URL_CONTEGGIO As String = "https://www.wingabriel.net/conta.php"
    Private Shared _conteggioInCorso As Boolean = False

    ''' <summary>
    ''' Conteggio anonimo degli utenti. Al massimo una volta a settimana e una al mese (lo ricorda il file conteggio-&lt;prodotto&gt;.txt nella
    ''' cartella di stato) chiede al sito conta.php con il solo prodotto, la versione installata e se è la prima volta della settimana (s)
    ''' e/o del mese (m). Non c'è nessun identificativo: il sito aumenta un contatore e non registra nemmeno l'indirizzo IP. La richiesta
    ''' parte in un altro thread, non fa aspettare nessuno; se non riesce si ritenta al prossimo controllo.
    ''' </summary>
    Public Shared Sub ContaUtente(cartellaCache As String, prodotto As String)
        Try
            If _conteggioInCorso OrElse String.IsNullOrWhiteSpace(cartellaCache) Then Return
            Dim ora As DateTime = DateTime.UtcNow
            Dim giorniDaLunedi As Integer = (CInt(ora.DayOfWeek) + 6) Mod 7
            Dim settimana As String = ora.Date.AddDays(-giorniDaLunedi).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            Dim mese As String = ora.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            Dim percorso As String = Path.Combine(cartellaCache, "conteggio-" & prodotto & ".txt")

            Dim ultimaSettimana As String = ""
            Dim ultimoMese As String = ""
            If File.Exists(percorso) Then
                Dim righe As String() = File.ReadAllLines(percorso, Encoding.UTF8)
                If righe.Length > 0 Then ultimaSettimana = righe(0).Trim()
                If righe.Length > 1 Then ultimoMese = righe(1).Trim()
            End If
            Dim tipi As String = ""
            If ultimaSettimana <> settimana Then tipi &= "s"
            If ultimoMese <> mese Then tipi &= "m"
            If tipi.Length = 0 Then Return

            ' Si conta per nome della versione (quello scelto da chi pubblica): solo lettere, cifre, punto, trattino, trattino basso e spazio.
            Dim versione As String = System.Text.RegularExpressions.Regex.Replace(NomeInstallato(cartellaCache, prodotto), "[^\p{L}\p{N}._ -]", "").Trim()
            If versione.Length > 40 Then versione = versione.Substring(0, 40).Trim()
            If versione = "" Then versione = "0"

            Dim indirizzo As String = URL_CONTEGGIO & "?p=" & Uri.EscapeDataString(prodotto) & "&v=" & Uri.EscapeDataString(versione) & "&t=" & tipi
            _conteggioInCorso = True
            Dim filo As New Thread(Sub()
                                       Try
                                           Scarica(indirizzo, 8000)
                                           If Not Directory.Exists(cartellaCache) Then Directory.CreateDirectory(cartellaCache)
                                           File.WriteAllText(percorso, settimana & vbLf & mese & vbLf, Encoding.UTF8)
                                       Catch
                                       Finally
                                           _conteggioInCorso = False
                                       End Try
                                   End Sub)
            filo.IsBackground = True
            filo.Start()
        Catch
            _conteggioInCorso = False
        End Try
    End Sub

    ''' <summary>Il canale scelto dall'utente: "beta" solo se lo ha scelto, altrimenti stabile.</summary>
    Public Shared Function NormalizzaCanale(valore As String) As String
        If String.Equals(If(valore, "").Trim(), CANALE_BETA, StringComparison.OrdinalIgnoreCase) Then Return CANALE_BETA
        Return CANALE_STABILE
    End Function

    ''' <summary>
    ''' Legge e controlla il puntatore del canale. Con la beta, se non c'è una beta pubblicata, vale la stabile. Restituisce Nothing
    ''' se il sito non risponde, se non ci sono rilasci o se la firma non è valida (in quel caso il motivo è in errore).
    ''' </summary>
    Public Shared Function LeggiPuntatore(prodotto As String, canale As String, ByRef errore As String, Optional attesaMs As Integer = 15000) As Puntatore
        errore = ""
        ' Lo stesso puntatore, letto più volte nello stesso minuto (avvio, controllo, aggiornatore), si chiede al sito una volta sola.
        Dim chiave As String = prodotto & "|" & NormalizzaCanale(canale)
        SyncLock _bloccoMemoria
            Dim memorizzato As Tuple(Of Puntatore, DateTime) = Nothing
            If _memoria.TryGetValue(chiave, memorizzato) AndAlso (DateTime.UtcNow - memorizzato.Item2).TotalSeconds < 60 Then Return memorizzato.Item1
        End SyncLock
        Dim letto As Puntatore = Nothing
        If (DateTime.UtcNow - _sitoNonRispondeDal).TotalMinutes >= 5 Then
            letto = LeggiPuntatoreDalSito(prodotto, canale, errore, attesaMs)
            If letto Is Nothing Then _sitoNonRispondeDal = DateTime.UtcNow
        End If
        If letto Is Nothing Then
            ' Il sito non risponde (o non ha un rilascio valido): si prova GitHub senza dire niente. L'errore resta solo se fallisce anche lì.
            Dim erroreGithub As String = ""
            letto = LeggiPuntatoreDaGithub(prodotto, canale, erroreGithub, attesaMs)
            If letto IsNot Nothing Then
                errore = ""
            ElseIf errore = "" Then
                errore = erroreGithub
            End If
        End If
        If letto IsNot Nothing Then
            SyncLock _bloccoMemoria
                _memoria(chiave) = Tuple.Create(letto, DateTime.UtcNow)
            End SyncLock
        End If
        Return letto
    End Function

    Private Shared Function LeggiPuntatoreDalSito(prodotto As String, canale As String, ByRef errore As String, attesaMs As Integer) As Puntatore
        errore = ""
        Dim canali As New List(Of String)()
        canali.Add(NormalizzaCanale(canale))
        ' Con la beta si guardano tutti e due i puntatori e vale il più recente (la sequenza cresce a ogni rilascio, di qualunque canale):
        ' se dopo una beta esce una versione pubblica, anche chi ha scelto la beta la riceve subito.
        If canali(0) = CANALE_BETA Then canali.Add(CANALE_STABILE)
        Dim migliore As Puntatore = Nothing
        For Each c As String In canali
            Try
                Dim dati As Byte() = Scarica(BASE_RILASCI & prodotto & "/" & c & ".txt", attesaMs)
                Dim testoCompleto As String = Encoding.UTF8.GetString(dati)
                Dim p As Puntatore = PuntatoreDaTesto(testoCompleto, prodotto, c, errore)
                If p Is Nothing Then Continue For
                If migliore Is Nothing OrElse p.Sequenza > migliore.Sequenza Then migliore = p
            Catch ex As Exception
                errore = "puntatore " & c & ": " & ex.Message
            End Try
        Next
        If migliore IsNot Nothing Then errore = ""
        Return migliore
    End Function

    ''' <summary>Controlla firma e coerenza di un puntatore (prima riga i dati, seconda la firma). Nothing se qualcosa non torna.</summary>
    Private Shared Function PuntatoreDaTesto(testoCompleto As String, prodotto As String, canale As String, ByRef errore As String) As Puntatore
        Try
            Dim fineRiga As Integer = testoCompleto.IndexOf(ControlChars.Lf)
            If fineRiga <= 0 Then
                errore = "puntatore " & canale & " non valido"
                Return Nothing
            End If
            Dim rigaDati As String = testoCompleto.Substring(0, fineRiga)
            Dim resto As String = testoCompleto.Substring(fineRiga + 1)
            Dim fineFirma As Integer = resto.IndexOf(ControlChars.Lf)
            Dim firma As String = If(fineFirma >= 0, resto.Substring(0, fineFirma), resto)
            If Not FirmaValida(Encoding.UTF8.GetBytes(rigaDati), firma) Then
                errore = "firma del puntatore " & canale & " non valida"
                Return Nothing
            End If
            Dim serializzatore As New JavaScriptSerializer()
            Dim d As Dictionary(Of String, Object) = serializzatore.Deserialize(Of Dictionary(Of String, Object))(rigaDati)
            Dim p As New Puntatore()
            p.Prodotto = Testo(d, "prodotto")
            p.Canale = Testo(d, "canale")
            p.Versione = Testo(d, "versione")
            p.ManifestSha256 = Testo(d, "manifestSha256").ToLowerInvariant()
            p.Data = Testo(d, "data")
            p.Note = Testo(d, "note")
            p.Nome = Testo(d, "nome")
            p.Sequenza = Numero(d, "sequenza", 0)
            If Not String.Equals(p.Prodotto, prodotto, StringComparison.Ordinal) OrElse Not String.Equals(p.Canale, canale, StringComparison.Ordinal) OrElse p.Versione = "" OrElse p.ManifestSha256.Length <> 64 Then
                errore = "puntatore " & canale & " non coerente"
                Return Nothing
            End If
            Return p
        Catch ex As Exception
            errore = "puntatore " & canale & ": " & ex.Message
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' L'indirizzo di un file del rilascio, sia sul sito sia su GitHub (urlBase è UrlBaseFile del manifest o del puntatore).
    ''' Su GitHub ogni file sta una volta sola nel rilascio "file", con il nome h.&lt;impronta SHA-256&gt;: l'impronta si prende dal
    ''' manifest già controllato di quel rilascio.
    ''' </summary>
    Public Shared Function UrlFile(urlBase As String, percorso As String) As String
        Dim relativo As String = If(percorso, "").Replace("\"c, "/"c).TrimStart("/"c)
        If If(urlBase, "").StartsWith(BASE_DOWNLOAD_GITHUB, StringComparison.OrdinalIgnoreCase) Then
            Dim impronta As String = ""
            SyncLock _bloccoMemoria
                Dim impronte As Dictionary(Of String, String) = Nothing
                If _impronteGithub.TryGetValue(urlBase, impronte) Then impronte.TryGetValue(relativo, impronta)
            End SyncLock
            If Not String.IsNullOrEmpty(impronta) Then Return BASE_DOWNLOAD_GITHUB & TAG_FILE_GITHUB & "/h." & impronta
        End If
        Return urlBase & relativo
    End Function

    ''' <summary>Ricorda percorso e impronta di ogni file di un manifest preso da GitHub, per costruire gli indirizzi dei file.</summary>
    Private Shared Sub RegistraImpronteGithub(m As Manifest)
        Try
            Dim impronte As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each v As VoceManifest In m.Voci
                If v.Libmpv Then
                    If v.UrlX86 <> "" Then impronte(v.UrlX86.Replace("\"c, "/"c).TrimStart("/"c)) = v.ImprontaX86
                    If v.UrlX64 <> "" Then impronte(v.UrlX64.Replace("\"c, "/"c).TrimStart("/"c)) = v.ImprontaX64
                Else
                    impronte(v.Percorso.Replace("\"c, "/"c).TrimStart("/"c)) = v.Impronta
                End If
            Next
            SyncLock _bloccoMemoria
                _impronteGithub(m.UrlBaseFile) = impronte
            End SyncLock
        Catch
        End Try
    End Sub

    ''' <summary>
    ''' Il puntatore preso dai rilasci di GitHub: si guardano gli ultimi rilasci e vale il più recente (per sequenza) tra quelli del canale
    ''' (con la beta anche quelli stabili). Le regole sono le stesse del sito: firma valida, prodotto e canale coerenti.
    ''' </summary>
    Private Shared Function LeggiPuntatoreDaGithub(prodotto As String, canale As String, ByRef errore As String, attesaMs As Integer) As Puntatore
        errore = ""
        Try
            Dim canali As New List(Of String)()
            canali.Add(NormalizzaCanale(canale))
            If canali(0) = CANALE_BETA Then canali.Add(CANALE_STABILE)
            Dim serializzatore As New JavaScriptSerializer()
            serializzatore.MaxJsonLength = Integer.MaxValue
            Dim rilasci As Object() = serializzatore.Deserialize(Of Object())(Encoding.UTF8.GetString(Scarica(API_RILASCI_GITHUB, attesaMs)))
            Dim migliore As Puntatore = Nothing
            Dim trovati As New HashSet(Of String)()
            For Each o As Object In rilasci
                Dim r As Dictionary(Of String, Object) = TryCast(o, Dictionary(Of String, Object))
                If r Is Nothing Then Continue For
                If String.Equals(Testo(r, "draft"), "True", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim tag As String = Testo(r, "tag_name")
                If tag = "" Then Continue For
                Dim nomiAsset As New HashSet(Of String)(StringComparer.Ordinal)
                Dim asset As Object = Nothing
                If r.TryGetValue("assets", asset) AndAlso asset IsNot Nothing Then
                    For Each a As Object In CType(asset, System.Collections.IEnumerable)
                        Dim da As Dictionary(Of String, Object) = TryCast(a, Dictionary(Of String, Object))
                        If da IsNot Nothing Then nomiAsset.Add(Testo(da, "name"))
                    Next
                End If
                For Each c As String In canali
                    ' Per ogni canale basta il rilascio più nuovo che lo contiene (GitHub li dà dal più nuovo).
                    If trovati.Contains(c) OrElse Not nomiAsset.Contains(prodotto & "-" & c & ".txt") Then Continue For
                    trovati.Add(c)
                    Dim testoCompleto As String = Encoding.UTF8.GetString(Scarica(BASE_DOWNLOAD_GITHUB & Uri.EscapeDataString(tag) & "/" & prodotto & "-" & c & ".txt", attesaMs))
                    Dim p As Puntatore = PuntatoreDaTesto(testoCompleto, prodotto, c, errore)
                    If p Is Nothing Then Continue For
                    p.TagGithub = tag
                    If migliore Is Nothing OrElse p.Sequenza > migliore.Sequenza Then migliore = p
                Next
                If trovati.Count = canali.Count Then Exit For
            Next
            If migliore IsNot Nothing Then
                errore = ""
                ' Il manifest si legge subito: servono le impronte per gli indirizzi dei file anche a chi usa solo il puntatore.
                Dim erroreManifest As String = ""
                LeggiManifest(migliore, erroreManifest)
            ElseIf errore = "" Then
                errore = "nessun rilascio su GitHub"
            End If
            Return migliore
        Catch ex As Exception
            errore = "GitHub: " & ex.Message
            Return Nothing
        End Try
    End Function

    ''' <summary>Scarica il manifest del puntatore e ne controlla l'impronta. Restituisce Nothing se qualcosa non torna.</summary>
    Public Shared Function LeggiManifest(p As Puntatore, ByRef errore As String) As Manifest
        errore = ""
        Try
            Dim urlManifest As String
            If p.TagGithub <> "" Then
                urlManifest = BASE_DOWNLOAD_GITHUB & Uri.EscapeDataString(p.TagGithub) & "/" & p.Prodotto & "-manifest.json"
            Else
                urlManifest = BASE_RILASCI & p.Prodotto & "/v/" & Uri.EscapeDataString(p.Versione) & "/manifest.json"
            End If
            Dim dati As Byte() = Scarica(urlManifest, 60000)
            Dim impronta As String
            Using sha As SHA256 = SHA256.Create()
                impronta = BitConverter.ToString(sha.ComputeHash(dati)).Replace("-", "").ToLowerInvariant()
            End Using
            If Not String.Equals(impronta, p.ManifestSha256, StringComparison.Ordinal) Then
                errore = "l'impronta del manifest non corrisponde al puntatore"
                Return Nothing
            End If
            Dim serializzatore As New JavaScriptSerializer()
            serializzatore.MaxJsonLength = Integer.MaxValue
            Dim d As Dictionary(Of String, Object) = serializzatore.Deserialize(Of Dictionary(Of String, Object))(Encoding.UTF8.GetString(dati))
            Dim m As New Manifest()
            m.Puntatore = p
            m.UrlBaseFile = UrlBaseFile(p)
            Dim cartelle As Object = Nothing
            If d.TryGetValue("cartelle", cartelle) AndAlso cartelle IsNot Nothing Then
                For Each c As Object In CType(cartelle, System.Collections.IEnumerable)
                    m.Cartelle.Add(Convert.ToString(c, CultureInfo.InvariantCulture))
                Next
            End If
            Dim file As Object = Nothing
            If d.TryGetValue("file", file) AndAlso file IsNot Nothing Then
                For Each o As Object In CType(file, System.Collections.IEnumerable)
                    Dim v As Dictionary(Of String, Object) = CType(o, Dictionary(Of String, Object))
                    Dim voce As New VoceManifest()
                    voce.Percorso = Testo(v, "p")
                    voce.Dimensione = Numero(v, "s", -1)
                    voce.Impronta = Testo(v, "h").ToLowerInvariant()
                    voce.DataUtc = Testo(v, "m")
                    m.Voci.Add(voce)
                Next
            End If
            Dim libmpv As Object = Nothing
            If d.TryGetValue("libmpv", libmpv) AndAlso libmpv IsNot Nothing Then
                For Each o As Object In CType(libmpv, System.Collections.IEnumerable)
                    Dim v As Dictionary(Of String, Object) = CType(o, Dictionary(Of String, Object))
                    Dim voce As New VoceManifest()
                    voce.Libmpv = True
                    voce.Percorso = Testo(v, "p")
                    voce.DataUtc = Testo(v, "m")
                    Dim x86 As Dictionary(Of String, Object) = CType(v("x86"), Dictionary(Of String, Object))
                    Dim x64 As Dictionary(Of String, Object) = CType(v("x64"), Dictionary(Of String, Object))
                    voce.UrlX86 = Testo(x86, "u")
                    voce.UrlX64 = Testo(x64, "u")
                    voce.DimensioneX86 = Numero(x86, "s", -1)
                    voce.DimensioneX64 = Numero(x64, "s", -1)
                    voce.ImprontaX86 = Testo(x86, "h").ToLowerInvariant()
                    voce.ImprontaX64 = Testo(x64, "h").ToLowerInvariant()
                    m.Voci.Add(voce)
                Next
            End If
            If p.TagGithub <> "" Then RegistraImpronteGithub(m)
            Return m
        Catch ex As Exception
            errore = ex.Message
            Return Nothing
        End Try
    End Function

    ''' <summary>Puntatore e manifest del canale in un colpo solo; Nothing se il sito non ha un rilascio valido.</summary>
    Public Shared Function Leggi(prodotto As String, canale As String, ByRef errore As String) As Manifest
        Dim p As Puntatore = LeggiPuntatore(prodotto, canale, errore)
        If p Is Nothing Then Return Nothing
        Dim m As Manifest = LeggiManifest(p, errore)
        Return m
    End Function

    ' --------------------------------------------------------------------------------------------------------------
    ' Quello che c'è installato: versione e sequenza dell'ultimo rilascio applicato (nella cartella della cache).
    ' --------------------------------------------------------------------------------------------------------------

    Private Shared Function PercorsoStato(cartellaCache As String, prodotto As String) As String
        Return Path.Combine(cartellaCache, "rilascio-" & prodotto & ".txt")
    End Function

    Public Shared Sub SalvaStato(cartellaCache As String, p As Puntatore)
        Try
            If Not Directory.Exists(cartellaCache) Then Directory.CreateDirectory(cartellaCache)
            File.WriteAllText(PercorsoStato(cartellaCache, p.Prodotto), p.Canale & "|" & p.Versione & "|" & p.Sequenza.ToString(CultureInfo.InvariantCulture) & "|" & p.ManifestSha256 & "|" & NomeVisibile(p).Replace("|"c, " "c).Replace(ControlChars.Cr, " "c).Replace(ControlChars.Lf, " "c), Encoding.UTF8)
        Catch
        End Try
    End Sub

    ''' <summary>Il nome della versione da mostrare: quello scelto da chi pubblica, altrimenti l'identificativo tecnico.</summary>
    Public Shared Function NomeVisibile(p As Puntatore) As String
        Dim n As String = If(p.Nome, "").Trim()
        If n = "" Then n = p.Versione
        Return n
    End Function

    ''' <summary>Il nome della versione applicata sul computer (vuoto se non si sa).</summary>
    Public Shared Function NomeInstallato(cartellaCache As String, prodotto As String) As String
        Try
            Dim percorso As String = PercorsoStato(cartellaCache, prodotto)
            If Not File.Exists(percorso) Then Return ""
            Dim parti As String() = File.ReadAllText(percorso, Encoding.UTF8).Trim().Split("|"c)
            If parti.Length > 4 AndAlso parti(4).Trim() <> "" Then Return parti(4).Trim()
            If parti.Length > 1 Then Return parti(1).Trim()
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>L'indirizzo della cartella con i file del rilascio indicato dal puntatore.</summary>
    Public Shared Function UrlBaseFile(p As Puntatore) As String
        If p.TagGithub <> "" Then Return BASE_DOWNLOAD_GITHUB & Uri.EscapeDataString(p.TagGithub) & "/"
        Return BASE_RILASCI & p.Prodotto & "/v/" & Uri.EscapeDataString(p.Versione) & "/f/"
    End Function

    ''' <summary>Vero se questo rilascio è già quello applicato (stessa versione e stesso manifest).</summary>
    Public Shared Function GiaApplicato(cartellaCache As String, p As Puntatore) As Boolean
        Try
            Dim percorso As String = PercorsoStato(cartellaCache, p.Prodotto)
            If Not File.Exists(percorso) Then Return False
            Dim parti As String() = File.ReadAllText(percorso, Encoding.UTF8).Trim().Split("|"c)
            If parti.Length < 4 Then Return False
            Return parti(1) = p.Versione AndAlso parti(3) = p.ManifestSha256
        Catch
            Return False
        End Try
    End Function

    Private Shared Function PercorsoFotografia(cartellaCache As String, prodotto As String) As String
        Return Path.Combine(cartellaCache, "rilascio-" & prodotto & ".fotografia")
    End Function

    Private Shared Function PercorsoLocaleDi(cartellaBase As String, voce As VoceManifest) As String
        Return Path.Combine(cartellaBase, voce.Percorso.Replace("/"c, Path.DirectorySeparatorChar))
    End Function

    ''' <summary>
    ''' Salva la fotografia dei file del rilascio così come sono ora sul disco (percorso, dimensione, data). Si fa quando si sa che sono
    ''' giusti: dopo un aggiornamento riuscito o dopo un confronto completo senza differenze. All'avvio basta rileggere dimensione e data
    ''' (pochi millisecondi) invece di calcolare le impronte. Se un file manca la fotografia non si salva.
    ''' </summary>
    Public Shared Sub SalvaFotografia(cartellaCache As String, cartellaBase As String, m As Manifest)
        Try
            Dim righe As New List(Of String)()
            For Each v As VoceManifest In m.Voci
                Dim info As New FileInfo(PercorsoLocaleDi(cartellaBase, v))
                If Not info.Exists Then
                    If File.Exists(PercorsoFotografia(cartellaCache, m.Puntatore.Prodotto)) Then File.Delete(PercorsoFotografia(cartellaCache, m.Puntatore.Prodotto))
                    Return
                End If
                righe.Add(v.Percorso & "|" & info.Length.ToString(CultureInfo.InvariantCulture) & "|" & info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
            Next
            If Not Directory.Exists(cartellaCache) Then Directory.CreateDirectory(cartellaCache)
            File.WriteAllLines(PercorsoFotografia(cartellaCache, m.Puntatore.Prodotto), righe.ToArray(), Encoding.UTF8)
        Catch
        End Try
    End Sub

    ''' <summary>Vero se i file sono ancora come nella fotografia (stessa dimensione e stessa data): nessuno cambiato o tolto.</summary>
    Public Shared Function FotografiaIntegra(cartellaCache As String, cartellaBase As String, prodotto As String) As Boolean
        Try
            Dim percorso As String = PercorsoFotografia(cartellaCache, prodotto)
            If Not File.Exists(percorso) Then Return False
            Dim righe As String() = File.ReadAllLines(percorso, Encoding.UTF8)
            If righe.Length = 0 Then Return False
            For Each riga As String In righe
                Dim parti As String() = riga.Split("|"c)
                If parti.Length <> 3 Then Return False
                Dim info As New FileInfo(Path.Combine(cartellaBase, parti(0).Replace("/"c, Path.DirectorySeparatorChar)))
                If Not info.Exists Then Return False
                If info.Length.ToString(CultureInfo.InvariantCulture) <> parti(1) Then Return False
                If info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) <> parti(2) Then Return False
            Next
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Il controllo dell'avvio: il sito ha un rilascio firmato, è già quello applicato e i file sono come nella fotografia. Una sola
    ''' richiesta breve al sito e pochi millisecondi sul disco. Se è vero non c'è niente da scaricare né da confrontare.
    ''' </summary>
    Public Shared Function AggiornatoESano(prodotto As String, canale As String, cartellaCache As String, cartellaBase As String, attesaMs As Integer) As Boolean
        Try
            Dim errore As String = ""
            Dim p As Puntatore = LeggiPuntatore(prodotto, canale, errore, attesaMs)
            If p Is Nothing Then Return False
            If PiuVecchioDelloInstallato(cartellaCache, p) Then Return False
            If Not GiaApplicato(cartellaCache, p) Then Return False
            Return FotografiaIntegra(cartellaCache, cartellaBase, prodotto)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Vero se il puntatore è più vecchio di quello già applicato dallo stesso canale (un puntatore vecchio rimesso sul sito).</summary>
    Public Shared Function PiuVecchioDelloInstallato(cartellaCache As String, p As Puntatore) As Boolean
        Try
            Dim percorso As String = PercorsoStato(cartellaCache, p.Prodotto)
            If Not File.Exists(percorso) Then Return False
            Dim parti As String() = File.ReadAllText(percorso, Encoding.UTF8).Trim().Split("|"c)
            If parti.Length < 4 OrElse parti(0) <> p.Canale Then Return False
            Dim sequenza As Long
            If Not Long.TryParse(parti(2), NumberStyles.Integer, CultureInfo.InvariantCulture, sequenza) Then Return False
            Return p.Sequenza < sequenza
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Il manifest nello stesso formato XML dell'elenco del vecchio sistema (Voce con percorso, tipo, dimensione, hash, dataModifica),
    ''' così i controlli già scritti per quell'elenco valgono anche per i rilasci.
    ''' </summary>
    Public Shared Function ComeElencoXml(m As Manifest) As String
        Dim sb As New StringBuilder()
        sb.Append("<?xml version=""1.0"" encoding=""utf-8""?><WGFileList><Cartelle>")
        For Each c As String In m.Cartelle
            sb.Append("<Cartella percorso=""").Append(Esc(c)).Append("""/>")
        Next
        sb.Append("</Cartelle><ElencoFile>")
        For Each v As VoceManifest In m.Voci
            If v.Libmpv Then
                sb.Append("<Voce percorso=""").Append(Esc(v.Percorso)).Append(""" tipo=""libmpv"" urlX86=""").Append(Esc(v.UrlX86)).Append(""" urlX64=""").Append(Esc(v.UrlX64))
                sb.Append(""" dimensioneX86=""").Append(v.DimensioneX86.ToString(CultureInfo.InvariantCulture)).Append(""" dimensioneX64=""").Append(v.DimensioneX64.ToString(CultureInfo.InvariantCulture))
                sb.Append(""" hashX86=""").Append(v.ImprontaX86).Append(""" hashX64=""").Append(v.ImprontaX64).Append(""" dataModifica=""").Append(Esc(v.DataUtc)).Append("""/>")
            Else
                sb.Append("<Voce percorso=""").Append(Esc(v.Percorso)).Append(""" tipo=""normale"" dimensione=""").Append(v.Dimensione.ToString(CultureInfo.InvariantCulture))
                sb.Append(""" dataModifica=""").Append(Esc(v.DataUtc)).Append(""" hash=""").Append(v.Impronta).Append("""/>")
            End If
        Next
        sb.Append("</ElencoFile></WGFileList>")
        Return sb.ToString()
    End Function

    Private Shared Function Esc(testo As String) As String
        Return System.Security.SecurityElement.Escape(testo)
    End Function

End Class
