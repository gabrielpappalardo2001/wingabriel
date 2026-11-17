Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Il client per un server Navidrome. Parla con l'API Subsonic (versione 1.16.1) che Navidrome espone
''' sotto /rest. L'accesso usa nome utente e password: la password non viaggia mai in chiaro, si manda
''' il token previsto dall'API (MD5 di password e sale casuale).
''' Gli errori diventano NavidromeEccezione, con un messaggio italiano già pronto per l'utente.
''' </summary>
Public Class NavidromeClient

    Private Const VERSIONE_API As String = "1.16.1"
    Private Const NOME_CLIENT As String = "WinGabriel"
    Private Const MAX_ALBUM_PER_RICHIESTA As Integer = 500

    Private Shared ReadOnly _http As HttpClient = CreaHttp()
    Private Shared ReadOnly _casuale As RandomNumberGenerator = RandomNumberGenerator.Create()

    Private ReadOnly _indirizzo As String
    Private ReadOnly _utente As String
    Private ReadOnly _password As String

    Public Sub New(indirizzo As String, utente As String, password As String)
        _indirizzo = NormalizzaIndirizzo(indirizzo)
        _utente = If(utente, "").Trim()
        _password = If(password, "")
    End Sub

    ''' <summary>Identifica server e utente: serve a tenere separati cache e brani scaricati di server diversi.</summary>
    Public ReadOnly Property IdServer As String
        Get
            Return _indirizzo.ToLowerInvariant() & "|" & _utente.ToLowerInvariant()
        End Get
    End Property

    Public ReadOnly Property Indirizzo As String
        Get
            Return _indirizzo
        End Get
    End Property

    Private Shared Function CreaHttp() As HttpClient
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
        Catch
        End Try
        Dim c As New HttpClient()
        c.Timeout = TimeSpan.FromSeconds(40)
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Navidrome/1.0")
        Return c
    End Function

    ''' <summary>
    ''' Rende pulito l'indirizzo scritto dall'utente: aggiunge http:// se manca lo schema, toglie spazi,
    ''' barra finale e un eventuale /app o /rest in coda.
    ''' </summary>
    Public Shared Function NormalizzaIndirizzo(indirizzo As String) As String
        Dim s As String = If(indirizzo, "").Trim()
        If s = "" Then Return ""
        If Not s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso Not s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            ' https di default: senza, il token del protocollo Subsonic (derivato dalla password) viaggerebbe in chiaro.
            s = "https://" & s
        End If
        s = s.TrimEnd("/"c)
        For Each coda As String In New String() {"/app", "/rest"}
            If s.EndsWith(coda, StringComparison.OrdinalIgnoreCase) Then s = s.Substring(0, s.Length - coda.Length)
        Next
        Return s.TrimEnd("/"c)
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Chiamata di base
    ' ------------------------------------------------------------------------------------------

    Private Shared Function NuovoSale() As String
        Dim b(7) As Byte
        _casuale.GetBytes(b)
        Dim sb As New StringBuilder()
        For Each x As Byte In b
            sb.Append(x.ToString("x2", CultureInfo.InvariantCulture))
        Next
        Return sb.ToString()
    End Function

    Private Shared Function CalcolaMd5(testo As String) As String
        Using m As MD5 = MD5.Create()
            Dim h As Byte() = m.ComputeHash(Encoding.UTF8.GetBytes(testo))
            Dim sb As New StringBuilder()
            For Each x As Byte In h
                sb.Append(x.ToString("x2", CultureInfo.InvariantCulture))
            Next
            Return sb.ToString()
        End Using
    End Function

    Private Function CostruisciIndirizzo(metodo As String, parametri As IEnumerable(Of KeyValuePair(Of String, String)), Optional formatoJson As Boolean = True) As String
        If _indirizzo = "" Then Throw New NavidromeEccezione("L'indirizzo del server Navidrome non è stato impostato.")
        Dim sale As String = NuovoSale()
        Dim sb As New StringBuilder()
        sb.Append(_indirizzo).Append("/rest/").Append(metodo).Append(".view")
        sb.Append("?u=").Append(Uri.EscapeDataString(_utente))
        sb.Append("&t=").Append(CalcolaMd5(_password & sale))
        sb.Append("&s=").Append(sale)
        sb.Append("&v=").Append(VERSIONE_API)
        sb.Append("&c=").Append(NOME_CLIENT)
        If formatoJson Then sb.Append("&f=json")
        If parametri IsNot Nothing Then
            For Each p As KeyValuePair(Of String, String) In parametri
                sb.Append("&").Append(Uri.EscapeDataString(p.Key)).Append("=").Append(Uri.EscapeDataString(If(p.Value, "")))
            Next
        End If
        Return sb.ToString()
    End Function

    Private Shared Function Coppia(chiave As String, valore As String) As KeyValuePair(Of String, String)
        Return New KeyValuePair(Of String, String)(chiave, valore)
    End Function

    Private Shared Function Coppia(chiave As String, valore As Integer) As KeyValuePair(Of String, String)
        Return New KeyValuePair(Of String, String)(chiave, valore.ToString(CultureInfo.InvariantCulture))
    End Function

    Private Async Function ChiamaAsync(metodo As String, ParamArray parametri As KeyValuePair(Of String, String)()) As Task(Of JObject)
        Return Await ChiamaAsync(metodo, DirectCast(parametri, IEnumerable(Of KeyValuePair(Of String, String))))
    End Function

    Private Async Function ChiamaAsync(metodo As String, parametri As IEnumerable(Of KeyValuePair(Of String, String))) As Task(Of JObject)
        Dim indirizzo As String = CostruisciIndirizzo(metodo, parametri)
        Dim corpo As String
        Try
            Using risposta As HttpResponseMessage = Await _http.GetAsync(indirizzo).ConfigureAwait(False)
                If Not risposta.IsSuccessStatusCode Then
                    Dim codice As Integer = CInt(risposta.StatusCode)
                    If codice = 401 OrElse codice = 403 Then
                        Throw New NavidromeEccezione("Il server ha rifiutato l'accesso. Controlla nome utente e password.", codice)
                    End If
                    If codice = 404 Then
                        Throw New NavidromeEccezione("A questo indirizzo non risponde un server Navidrome. Controlla l'indirizzo.", codice)
                    End If
                    Throw New NavidromeEccezione("Il server ha risposto con un errore (" & codice.ToString() & ").", codice)
                End If
                corpo = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
            End Using
        Catch ex As NavidromeEccezione
            Throw
        Catch ex As TaskCanceledException
            Throw New NavidromeEccezione("Il server Navidrome non risponde. Riprova tra poco.", 0, ex)
        Catch ex As HttpRequestException
            Throw New NavidromeEccezione("Impossibile raggiungere il server Navidrome. Controlla l'indirizzo e la connessione.", 0, ex)
        Catch ex As WebException
            Throw New NavidromeEccezione("Impossibile raggiungere il server Navidrome. Controlla l'indirizzo e la connessione.", 0, ex)
        End Try

        Dim radice As JObject
        Try
            radice = JObject.Parse(corpo)
        Catch ex As JsonException
            Throw New NavidromeEccezione("L'indirizzo non risponde come un server Navidrome. Controlla l'indirizzo.", 0, ex)
        End Try

        Dim sr As JToken = radice("subsonic-response")
        If sr Is Nothing Then
            Throw New NavidromeEccezione("L'indirizzo non risponde come un server Navidrome. Controlla l'indirizzo.")
        End If
        If Not String.Equals(NavidromeJson.Testo(sr, "status"), "ok", StringComparison.OrdinalIgnoreCase) Then
            Dim errore As JToken = sr("error")
            Dim codice As Integer = NavidromeJson.Intero(errore, "code")
            Throw New NavidromeEccezione(TraduciErrore(codice, NavidromeJson.Testo(errore, "message")), codice)
        End If
        Return DirectCast(sr, JObject)
    End Function

    Private Shared Function TraduciErrore(codice As Integer, messaggioServer As String) As String
        Select Case codice
            Case 10
                Return "La richiesta al server non è completa."
            Case 20, 30
                Return "Il server Navidrome usa una versione dell'interfaccia non compatibile con WinGabriel."
            Case 40
                Return "Nome utente o password errati."
            Case 41
                Return "Il server non accetta questo tipo di accesso."
            Case 50
                Return "L'utente non ha il permesso di fare questa operazione sul server."
            Case 60
                Return "La prova del server è terminata."
            Case 70
                Return "L'elemento richiesto non esiste più sul server."
        End Select
        If Not String.IsNullOrWhiteSpace(messaggioServer) Then Return "Il server Navidrome ha risposto: " & messaggioServer
        Return "Il server Navidrome ha risposto con un errore."
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Server
    ' ------------------------------------------------------------------------------------------

    ''' <summary>Verifica indirizzo, utente e password. Se va a buon fine restituisce i dati del server.</summary>
    Public Async Function VerificaAsync() As Task(Of NavidromeInfoServer)
        Dim r As JObject = Await ChiamaAsync("ping")
        Dim info As New NavidromeInfoServer()
        info.Tipo = NavidromeJson.Testo(r, "type")
        info.VersioneServer = NavidromeJson.Testo(r, "serverVersion")
        info.VersioneApi = NavidromeJson.Testo(r, "version")
        Return info
    End Function

    ''' <summary>Fa leggere al server i file nuovi o cambiati della libreria. Con completa rilegge tutto.</summary>
    Public Async Function AvviaScansioneAsync(completa As Boolean) As Task(Of NavidromeStatoScansione)
        Dim r As JObject = Await ChiamaAsync("startScan", Coppia("fullScan", If(completa, "true", "false")))
        Return LeggiStatoScansione(r)
    End Function

    Public Async Function StatoScansioneAsync() As Task(Of NavidromeStatoScansione)
        Dim r As JObject = Await ChiamaAsync("getScanStatus")
        Return LeggiStatoScansione(r)
    End Function

    Private Shared Function LeggiStatoScansione(r As JObject) As NavidromeStatoScansione
        Dim s As New NavidromeStatoScansione()
        Dim n As JToken = r("scanStatus")
        s.InCorso = NavidromeJson.Booleano(n, "scanning")
        s.Conteggio = NavidromeJson.Lungo(n, "count")
        Return s
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Esplorazione della libreria
    ' ------------------------------------------------------------------------------------------

    Public Async Function ArtistiAsync() As Task(Of List(Of NavidromeArtista))
        Dim r As JObject = Await ChiamaAsync("getArtists")
        Dim risultato As New List(Of NavidromeArtista)
        For Each indice As JToken In NavidromeJson.Elenco(r("artists"), "index")
            For Each a As JToken In NavidromeJson.Elenco(indice, "artist")
                risultato.Add(NavidromeArtista.Da(a))
            Next
        Next
        Return risultato
    End Function

    Public Async Function AlbumDiArtistaAsync(idArtista As String) As Task(Of List(Of NavidromeAlbum))
        Dim r As JObject = Await ChiamaAsync("getArtist", Coppia("id", idArtista))
        Dim risultato As New List(Of NavidromeAlbum)
        For Each a As JToken In NavidromeJson.Elenco(r("artist"), "album")
            risultato.Add(NavidromeAlbum.Da(a))
        Next
        Return risultato
    End Function

    ''' <summary>I brani di un album, nell'ordine di disco e traccia.</summary>
    Public Async Function BraniDiAlbumAsync(idAlbum As String) As Task(Of List(Of NavidromeBrano))
        Dim r As JObject = Await ChiamaAsync("getAlbum", Coppia("id", idAlbum))
        Dim risultato As New List(Of NavidromeBrano)
        For Each b As JToken In NavidromeJson.Elenco(r("album"), "song")
            risultato.Add(NavidromeBrano.Da(b))
        Next
        Return risultato
    End Function

    Public Async Function ElencoAlbumAsync(tipo As NavidromeElencoAlbum, quanti As Integer, inizio As Integer, Optional genere As String = "") As Task(Of List(Of NavidromeAlbum))
        Dim nome As String
        Select Case tipo
            Case NavidromeElencoAlbum.PiuRecenti : nome = "newest"
            Case NavidromeElencoAlbum.AscoltatiDiRecente : nome = "recent"
            Case NavidromeElencoAlbum.PiuAscoltati : nome = "frequent"
            Case NavidromeElencoAlbum.Casuali : nome = "random"
            Case NavidromeElencoAlbum.OrdineAlfabetico : nome = "alphabeticalByName"
            Case NavidromeElencoAlbum.OrdinePerArtista : nome = "alphabeticalByArtist"
            Case NavidromeElencoAlbum.Preferiti : nome = "starred"
            Case NavidromeElencoAlbum.PiuVotati : nome = "highest"
            Case NavidromeElencoAlbum.PerGenere : nome = "byGenre"
            Case Else : nome = "newest"
        End Select
        Dim par As New List(Of KeyValuePair(Of String, String)) From {
            Coppia("type", nome),
            Coppia("size", Math.Min(Math.Max(quanti, 1), MAX_ALBUM_PER_RICHIESTA)),
            Coppia("offset", Math.Max(inizio, 0))
        }
        If tipo = NavidromeElencoAlbum.PerGenere Then par.Add(Coppia("genre", genere))
        Dim r As JObject = Await ChiamaAsync("getAlbumList2", par)
        Dim risultato As New List(Of NavidromeAlbum)
        For Each a As JToken In NavidromeJson.Elenco(r("albumList2"), "album")
            risultato.Add(NavidromeAlbum.Da(a))
        Next
        Return risultato
    End Function

    Public Async Function GeneriAsync() As Task(Of List(Of NavidromeGenere))
        Dim r As JObject = Await ChiamaAsync("getGenres")
        Dim risultato As New List(Of NavidromeGenere)
        For Each g As JToken In NavidromeJson.Elenco(r("genres"), "genre")
            Dim genere As NavidromeGenere = NavidromeGenere.Da(g)
            If genere.Nome <> "" Then risultato.Add(genere)
        Next
        Return risultato
    End Function

    Public Async Function BraniPerGenereAsync(genere As String, quanti As Integer, inizio As Integer) As Task(Of List(Of NavidromeBrano))
        Dim r As JObject = Await ChiamaAsync("getSongsByGenre", Coppia("genre", genere), Coppia("count", quanti), Coppia("offset", Math.Max(inizio, 0)))
        Return LeggiBrani(r("songsByGenre"), "song")
    End Function

    Public Async Function BraniCasualiAsync(quanti As Integer, Optional genere As String = "") As Task(Of List(Of NavidromeBrano))
        Dim par As New List(Of KeyValuePair(Of String, String)) From {Coppia("size", Math.Min(Math.Max(quanti, 1), 500))}
        If Not String.IsNullOrWhiteSpace(genere) Then par.Add(Coppia("genre", genere))
        Dim r As JObject = Await ChiamaAsync("getRandomSongs", par)
        Return LeggiBrani(r("randomSongs"), "song")
    End Function

    ''' <summary>
    ''' Tutti i brani della libreria in un solo elenco. Il server li dà a blocchi di 500 (una ricerca
    ''' vuota di Navidrome restituisce l'intero catalogo): qui si scaricano uno dopo l'altro fino alla fine.
    ''' </summary>
    Public Async Function TuttiIBraniAsync() As Task(Of List(Of NavidromeBrano))
        Const BLOCCO As Integer = 500
        Dim risultato As New List(Of NavidromeBrano)
        Do
            Dim r As JObject = Await ChiamaAsync("search3", Coppia("query", ""),
                                                 Coppia("artistCount", 0), Coppia("albumCount", 0),
                                                 Coppia("songCount", BLOCCO), Coppia("songOffset", risultato.Count))
            Dim ricevuti As List(Of NavidromeBrano) = LeggiRisultati(r("searchResult3")).Brani
            risultato.AddRange(ricevuti)
            If ricevuti.Count < BLOCCO Then Exit Do
        Loop
        Return risultato
    End Function

    Public Async Function CercaAsync(testo As String, Optional massimoPerTipo As Integer = 50) As Task(Of NavidromeRisultatiRicerca)
        Dim r As JObject = Await ChiamaAsync("search3", Coppia("query", testo),
                                             Coppia("artistCount", massimoPerTipo), Coppia("albumCount", massimoPerTipo), Coppia("songCount", massimoPerTipo))
        Return LeggiRisultati(r("searchResult3"))
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Preferiti, voti, ascolti
    ' ------------------------------------------------------------------------------------------

    Public Async Function PreferitiAsync() As Task(Of NavidromeRisultatiRicerca)
        Dim r As JObject = Await ChiamaAsync("getStarred2")
        Return LeggiRisultati(r("starred2"))
    End Function

    Public Async Function ImpostaPreferitoBraniAsync(idBrani As IEnumerable(Of String), preferito As Boolean) As Task
        Await ImpostaPreferitoAsync("id", idBrani, preferito)
    End Function

    Public Async Function ImpostaPreferitoAlbumAsync(idAlbum As String, preferito As Boolean) As Task
        Await ImpostaPreferitoAsync("albumId", New String() {idAlbum}, preferito)
    End Function

    Public Async Function ImpostaPreferitoArtistaAsync(idArtista As String, preferito As Boolean) As Task
        Await ImpostaPreferitoAsync("artistId", New String() {idArtista}, preferito)
    End Function

    Private Async Function ImpostaPreferitoAsync(parametro As String, ids As IEnumerable(Of String), preferito As Boolean) As Task
        Dim par As New List(Of KeyValuePair(Of String, String))
        For Each id As String In ids
            par.Add(Coppia(parametro, id))
        Next
        If par.Count = 0 Then Return
        Await ChiamaAsync(If(preferito, "star", "unstar"), par)
    End Function

    ''' <summary>Il voto da 1 a 5 stelle; zero toglie il voto.</summary>
    Public Async Function ImpostaVotoAsync(idElemento As String, voto As Integer) As Task
        Await ChiamaAsync("setRating", Coppia("id", idElemento), Coppia("rating", Math.Min(Math.Max(voto, 0), 5)))
    End Function

    ''' <summary>Dice al server che un brano è stato ascoltato (aumenta il conteggio degli ascolti).</summary>
    Public Async Function SegnaAscoltatoAsync(idBrano As String) As Task
        Await ChiamaAsync("scrobble", Coppia("id", idBrano), Coppia("submission", "true"))
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Playlist
    ' ------------------------------------------------------------------------------------------

    Public Async Function PlaylistAsync() As Task(Of List(Of NavidromePlaylist))
        Dim r As JObject = Await ChiamaAsync("getPlaylists")
        Dim risultato As New List(Of NavidromePlaylist)
        For Each p As JToken In NavidromeJson.Elenco(r("playlists"), "playlist")
            risultato.Add(NavidromePlaylist.Da(p))
        Next
        Return risultato
    End Function

    Public Async Function BraniDiPlaylistAsync(idPlaylist As String) As Task(Of List(Of NavidromeBrano))
        Dim r As JObject = Await ChiamaAsync("getPlaylist", Coppia("id", idPlaylist))
        Return LeggiBrani(r("playlist"), "entry")
    End Function

    Public Async Function CreaPlaylistAsync(nome As String, idBrani As IEnumerable(Of String)) As Task
        Dim par As New List(Of KeyValuePair(Of String, String)) From {Coppia("name", nome)}
        If idBrani IsNot Nothing Then
            For Each id As String In idBrani
                par.Add(Coppia("songId", id))
            Next
        End If
        Await ChiamaAsync("createPlaylist", par)
    End Function

    Public Async Function AggiungiAPlaylistAsync(idPlaylist As String, idBrani As IEnumerable(Of String)) As Task
        Dim par As New List(Of KeyValuePair(Of String, String)) From {Coppia("playlistId", idPlaylist)}
        For Each id As String In idBrani
            par.Add(Coppia("songIdToAdd", id))
        Next
        Await ChiamaAsync("updatePlaylist", par)
    End Function

    ''' <summary>Toglie dalla playlist i brani nelle posizioni indicate (la prima posizione è zero).</summary>
    Public Async Function RimuoviDaPlaylistAsync(idPlaylist As String, posizioni As IEnumerable(Of Integer)) As Task
        Dim par As New List(Of KeyValuePair(Of String, String)) From {Coppia("playlistId", idPlaylist)}
        For Each posizione As Integer In posizioni
            par.Add(Coppia("songIndexToRemove", posizione))
        Next
        Await ChiamaAsync("updatePlaylist", par)
    End Function

    Public Async Function RinominaPlaylistAsync(idPlaylist As String, nuovoNome As String) As Task
        Await ChiamaAsync("updatePlaylist", Coppia("playlistId", idPlaylist), Coppia("name", nuovoNome))
    End Function

    Public Async Function EliminaPlaylistAsync(idPlaylist As String) As Task
        Await ChiamaAsync("deletePlaylist", Coppia("id", idPlaylist))
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Radio internet configurate sul server
    ' ------------------------------------------------------------------------------------------

    ''' <summary>I brani che gli utenti del server stanno ascoltando adesso (il prefisso dice chi e da quanto).</summary>
    Public Async Function InAscoltoAsync() As Task(Of List(Of NavidromeBrano))
        Dim r As JObject = Await ChiamaAsync("getNowPlaying")
        Dim risultato As New List(Of NavidromeBrano)
        For Each e As JToken In NavidromeJson.Elenco(r("nowPlaying"), "entry")
            Dim b As NavidromeBrano = NavidromeBrano.Da(e)
            Dim utente As String = NavidromeJson.Testo(e, "username")
            Dim minuti As Integer = NavidromeJson.Intero(e, "minutesAgo")
            b.Prefisso = utente & If(minuti > 0, ", " & minuti.ToString() & If(minuti = 1, " minuto fa", " minuti fa"), ", adesso") & ": "
            risultato.Add(b)
        Next
        Return risultato
    End Function

    Public Async Function RadioAsync() As Task(Of List(Of NavidromeRadio))
        Dim r As JObject = Await ChiamaAsync("getInternetRadioStations")
        Dim risultato As New List(Of NavidromeRadio)
        For Each s As JToken In NavidromeJson.Elenco(r("internetRadioStations"), "internetRadioStation")
            risultato.Add(NavidromeRadio.Da(s))
        Next
        Return risultato
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Riproduzione
    ' ------------------------------------------------------------------------------------------

    ''' <summary>
    ''' L'indirizzo da dare al lettore per ascoltare un brano in streaming, con l'accesso già incluso.
    ''' Il brano arriva nel formato originale, senza ricodifica.
    ''' </summary>
    Public Function IndirizzoStream(idBrano As String) As String
        Return CostruisciIndirizzo("stream", New KeyValuePair(Of String, String)() {Coppia("id", idBrano), Coppia("format", "raw")}, False)
    End Function

    ''' <summary>
    ''' Scarica il file originale di un brano (funzione download del server) nel percorso indicato.
    ''' Il file arriva prima con estensione .part e prende il nome vero solo a download finito.
    ''' </summary>
    Public Async Function ScaricaAsync(idBrano As String, percorsoDestinazione As String) As Task
        Dim indirizzo As String = CostruisciIndirizzo("download", New KeyValuePair(Of String, String)() {Coppia("id", idBrano)}, False)
        Dim temporaneo As String = percorsoDestinazione & ".part"
        Try
            Dim cartella As String = System.IO.Path.GetDirectoryName(percorsoDestinazione)
            If Not System.IO.Directory.Exists(cartella) Then System.IO.Directory.CreateDirectory(cartella)
            Using risposta As HttpResponseMessage = Await _http.GetAsync(indirizzo, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(False)
                If Not risposta.IsSuccessStatusCode Then
                    Throw New NavidromeEccezione("Il server ha rifiutato lo scaricamento (" & CInt(risposta.StatusCode).ToString() & ").", CInt(risposta.StatusCode))
                End If
                ' Se il server non può dare il file risponde con un documento di errore invece dell'audio.
                Dim tipo As String = If(risposta.Content.Headers.ContentType IsNot Nothing, risposta.Content.Headers.ContentType.MediaType, "")
                If tipo.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 OrElse tipo.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0 OrElse tipo.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Throw New NavidromeEccezione("Il server non ha dato il file di questo brano. L'utente potrebbe non avere il permesso di scaricare.")
                End If
                Using sorgente As System.IO.Stream = Await risposta.Content.ReadAsStreamAsync().ConfigureAwait(False)
                    Using destinazione As System.IO.FileStream = System.IO.File.Create(temporaneo)
                        Await sorgente.CopyToAsync(destinazione, 81920).ConfigureAwait(False)
                    End Using
                End Using
            End Using
            If System.IO.File.Exists(percorsoDestinazione) Then System.IO.File.Delete(percorsoDestinazione)
            System.IO.File.Move(temporaneo, percorsoDestinazione)
        Catch ex As NavidromeEccezione
            Try
                If System.IO.File.Exists(temporaneo) Then System.IO.File.Delete(temporaneo)
            Catch
            End Try
            Throw
        Catch ex As TaskCanceledException
            Throw New NavidromeEccezione("Il server Navidrome non risponde. Riprova tra poco.", 0, ex)
        Catch ex As HttpRequestException
            Throw New NavidromeEccezione("Impossibile raggiungere il server Navidrome. Controlla l'indirizzo e la connessione.", 0, ex)
        Catch ex As System.IO.IOException
            Throw New NavidromeEccezione("Impossibile salvare il file sul computer: " & ex.Message, 0, ex)
        Catch ex As UnauthorizedAccessException
            Throw New NavidromeEccezione("Non ho il permesso di scrivere nella cartella dei brani scaricati.", 0, ex)
        End Try
    End Function

    Public Function IndirizziStream(brani As IEnumerable(Of NavidromeBrano)) As List(Of String)
        Dim r As New List(Of String)
        For Each b As NavidromeBrano In brani
            r.Add(IndirizzoStream(b.Id))
        Next
        Return r
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Lettura delle risposte
    ' ------------------------------------------------------------------------------------------

    Private Shared Function LeggiBrani(nodo As JToken, chiave As String) As List(Of NavidromeBrano)
        Dim risultato As New List(Of NavidromeBrano)
        For Each b As JToken In NavidromeJson.Elenco(nodo, chiave)
            risultato.Add(NavidromeBrano.Da(b))
        Next
        Return risultato
    End Function

    Private Shared Function LeggiRisultati(nodo As JToken) As NavidromeRisultatiRicerca
        Dim r As New NavidromeRisultatiRicerca()
        For Each a As JToken In NavidromeJson.Elenco(nodo, "artist")
            r.Artisti.Add(NavidromeArtista.Da(a))
        Next
        For Each a As JToken In NavidromeJson.Elenco(nodo, "album")
            r.Album.Add(NavidromeAlbum.Da(a))
        Next
        r.Brani = LeggiBrani(nodo, "song")
        Return r
    End Function

End Class
