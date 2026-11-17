Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb

Public Class ElementoArchivio

    Public Titolo As String
    Public Autore As String
    Public Genere As String
    Public Identificatore As String
    Public EAudio As Boolean
    Public Lingua As String

    Public Sub New()
        Titolo = ""
        Autore = ""
        Genere = ""
        Identificatore = ""
        EAudio = False
        Lingua = "it"
    End Sub

End Class

Public Class CLSInternetArchive

    Public Shared PercorsoDatabase As String = RisolviPercorsoDatabaseArchivio()
    Public Shared PercorsoAudioLibri As String = RisolviPercorsoSottocartellaArchivio("Audio")
    Public Shared PercorsoTesti As String = RisolviPercorsoSottocartellaArchivio("Testi")

    Private Const UrlBaseArchive As String = "https://archive.org"

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _database As New CLSInternetArchiveDatabase(PercorsoDatabase)
    Private Shared ReadOnly _client As HttpClient

    Private Shared Function RisolviPercorsoDatabaseArchivio() As String
        Try
            Dim percorso As String = WGDatabase.PercorsoFileDati("InternetArchive", "internetarchive.db")
            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch
        End Try
        Try
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati\InternetArchive", "internetarchive.db")
        Catch
            Return "internetarchive.db"
        End Try
    End Function

    Private Shared Function RisolviPercorsoSottocartellaArchivio(nomeSottocartella As String) As String
        Try
            Dim cartellaBase As String = WGDatabase.CartellaDati("InternetArchive")
            If Not String.IsNullOrWhiteSpace(cartellaBase) Then
                Dim percorso As String = Path.Combine(cartellaBase, nomeSottocartella)
                If Not Directory.Exists(percorso) Then Directory.CreateDirectory(percorso)
                Return percorso
            End If
        Catch
        End Try
        Try
            Dim percorsoDiRiserva As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati\InternetArchive", nomeSottocartella)
            If Not Directory.Exists(percorsoDiRiserva) Then Directory.CreateDirectory(percorsoDiRiserva)
            Return percorsoDiRiserva
        Catch
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati\InternetArchive", nomeSottocartella)
        End Try
    End Function

    Shared Sub New()
        Try
            _client = New HttpClient()
            _client.Timeout = TimeSpan.FromSeconds(30)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-InternetArchive/1.0 (accessibilita; postmaster@wingabriel.net)")
            _client.DefaultRequestHeaders.Add("Accept", "application/json")
        Catch ex As Exception
        End Try
    End Sub

    Public Function Cerca(query As String) As List(Of ElementoArchivio)
        Try
            Return Cerca(query, 20, 0)
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.Cerca: " & ex.ToString())
            Return New List(Of ElementoArchivio)
        End Try
    End Function

    Public Function Cerca(query As String, count As Integer, paginaRichiesta As Integer, Optional tipoSupporto As String = "entrambi") As List(Of ElementoArchivio)
        Dim risultati As New List(Of ElementoArchivio)
        Try
            If String.IsNullOrWhiteSpace(query) OrElse count <= 0 OrElse paginaRichiesta < 0 Then Return risultati

            Dim clausolaSupporto As String
            If String.Equals(tipoSupporto, "testo", StringComparison.OrdinalIgnoreCase) Then
                clausolaSupporto = "mediatype:(texts) AND format:(DjVu)"
            ElseIf String.Equals(tipoSupporto, "audio", StringComparison.OrdinalIgnoreCase) Then
                clausolaSupporto = "mediatype:(audio) AND collection:(librivoxaudio)"
            Else
                clausolaSupporto = "((mediatype:(texts) AND format:(DjVu)) OR (mediatype:(audio) AND collection:(librivoxaudio)))"
            End If

            Dim queryCompleta As String = "title:(" & query.Trim() & ") AND (language:(ita) OR language:(Italian) OR language:(it)) AND " & clausolaSupporto
            Dim offset As Integer = paginaRichiesta * count
            Dim url As String = UrlBaseArchive & "/advancedsearch.php?q=" & Uri.EscapeDataString(queryCompleta) & "&fl[]=identifier&fl[]=title&fl[]=creator&fl[]=subject&fl[]=mediatype&sort[]=downloads+desc&rows=" & count.ToString() & "&start=" & offset.ToString() & "&output=json"
            Dim risposta As String = ScaricaStringa(url)
            If String.IsNullOrWhiteSpace(risposta) Then Return risultati

            Dim json As JObject = JObject.Parse(risposta)
            Dim documenti As JArray = TryCast(json("response")?("docs"), JArray)
            If documenti Is Nothing Then Return risultati

            For Each elemento As JObject In documenti
                Try
                    Dim voce As ElementoArchivio = CostruisciElemento(elemento)
                    If voce IsNot Nothing Then risultati.Add(voce)
                Catch exInterno As Exception
                    _logger.ScriviLog("CLSInternetArchive.Cerca (elemento): " & exInterno.ToString())
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.Cerca (paginata): " & ex.ToString())
        End Try
        Return risultati
    End Function

    Private Function CostruisciElemento(elemento As JObject) As ElementoArchivio
        Try
            Dim voce As New ElementoArchivio()
            voce.Titolo = If(elemento("title")?.ToString(), "Titolo sconosciuto")
            voce.Identificatore = If(elemento("identifier")?.ToString(), "")
            voce.Lingua = "it"

            If String.IsNullOrWhiteSpace(voce.Identificatore) Then Return Nothing

            Dim mediatype As String = elemento("mediatype")?.ToString()
            voce.EAudio = String.Equals(mediatype, "audio", StringComparison.OrdinalIgnoreCase)

            voce.Autore = NormalizzaAutore(elemento("creator"))
            voce.Genere = ClassificaGenere(elemento("subject"))

            Return voce
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.CostruisciElemento: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function EstraiPrimoValore(valore As JToken) As String
        Try
            If valore Is Nothing Then Return ""
            If valore.Type = JTokenType.Array Then
                Dim elenco As JArray = CType(valore, JArray)
                If elenco.Count = 0 Then Return ""
                Return elenco(0).ToString()
            End If
            Return valore.ToString()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.EstraiPrimoValore: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EstraiElencoValori(valore As JToken) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If valore Is Nothing Then Return risultato
            If valore.Type = JTokenType.Array Then
                For Each voce As JToken In CType(valore, JArray)
                    Dim testo As String = voce?.ToString()
                    If Not String.IsNullOrWhiteSpace(testo) Then risultato.Add(testo)
                Next
            Else
                Dim testo As String = valore.ToString()
                If Not String.IsNullOrWhiteSpace(testo) Then risultato.Add(testo)
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.EstraiElencoValori: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function NormalizzaAutore(valoreCreator As JToken) As String
        Try
            Dim autore As String = EstraiPrimoValore(valoreCreator)
            autore = If(autore, "").Trim()

            If String.IsNullOrWhiteSpace(autore) Then Return "Autore sconosciuto"

            Dim autoriIgnoti As String() = {"unknown", "sconosciuto", "anonymous", "anonimo", "s.n.", "n.a.", "non identificato"}
            For Each valoreIgnoto As String In autoriIgnoti
                If String.Equals(autore, valoreIgnoto, StringComparison.OrdinalIgnoreCase) Then Return "Autore sconosciuto"
            Next

            Dim posizioneVirgola As Integer = autore.IndexOf(",")
            If posizioneVirgola > 0 AndAlso posizioneVirgola < autore.Length - 1 Then
                Dim cognome As String = autore.Substring(0, posizioneVirgola).Trim()
                Dim resto As String = autore.Substring(posizioneVirgola + 1).Trim()
                Dim posizioneVirgolaInterna As Integer = resto.IndexOf(",")
                If posizioneVirgolaInterna >= 0 Then resto = resto.Substring(0, posizioneVirgolaInterna).Trim()

                Dim contieneCifre As Boolean = False
                For Each carattere As Char In resto
                    If Char.IsDigit(carattere) Then
                        contieneCifre = True
                        Exit For
                    End If
                Next

                If Not String.IsNullOrWhiteSpace(cognome) AndAlso Not String.IsNullOrWhiteSpace(resto) AndAlso resto.Length < 60 AndAlso Not contieneCifre Then
                    Return resto & " " & cognome
                End If
            End If

            Return autore
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.NormalizzaAutore: " & ex.ToString())
            Return "Autore sconosciuto"
        End Try
    End Function

    Private Function ClassificaGenere(valoriSubject As JToken) As String
        Const generePredefinito As String = "Da classificare"
        Try
            Dim elenco As List(Of String) = EstraiElencoValori(valoriSubject)
            If elenco.Count = 0 Then Return generePredefinito

            Dim mappaGeneri As New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("romanz", "Romanzi"),
                New KeyValuePair(Of String, String)("novel", "Romanzi"),
                New KeyValuePair(Of String, String)("racconti", "Racconti"),
                New KeyValuePair(Of String, String)("novelle", "Racconti"),
                New KeyValuePair(Of String, String)("short stor", "Racconti"),
                New KeyValuePair(Of String, String)("fiaba", "Fiabe"),
                New KeyValuePair(Of String, String)("fiabe", "Fiabe"),
                New KeyValuePair(Of String, String)("fairy tale", "Fiabe"),
                New KeyValuePair(Of String, String)("poesia", "Poesia"),
                New KeyValuePair(Of String, String)("poesie", "Poesia"),
                New KeyValuePair(Of String, String)("poetry", "Poesia"),
                New KeyValuePair(Of String, String)("poem", "Poesia"),
                New KeyValuePair(Of String, String)("teatro", "Teatro"),
                New KeyValuePair(Of String, String)("drama", "Teatro"),
                New KeyValuePair(Of String, String)("play", "Teatro"),
                New KeyValuePair(Of String, String)("commedia", "Teatro"),
                New KeyValuePair(Of String, String)("biograf", "Biografie"),
                New KeyValuePair(Of String, String)("biography", "Biografie"),
                New KeyValuePair(Of String, String)("storia", "Storia"),
                New KeyValuePair(Of String, String)("history", "Storia"),
                New KeyValuePair(Of String, String)("filosofia", "Filosofia"),
                New KeyValuePair(Of String, String)("philosophy", "Filosofia"),
                New KeyValuePair(Of String, String)("saggi", "Saggistica"),
                New KeyValuePair(Of String, String)("saggistica", "Saggistica"),
                New KeyValuePair(Of String, String)("essay", "Saggistica"),
                New KeyValuePair(Of String, String)("religion", "Religione"),
                New KeyValuePair(Of String, String)("religione", "Religione"),
                New KeyValuePair(Of String, String)("scien", "Scienza"),
                New KeyValuePair(Of String, String)("art", "Arte"),
                New KeyValuePair(Of String, String)("arte", "Arte"),
                New KeyValuePair(Of String, String)("viagg", "Viaggi"),
                New KeyValuePair(Of String, String)("travel", "Viaggi"),
                New KeyValuePair(Of String, String)("music", "Musica"),
                New KeyValuePair(Of String, String)("musica", "Musica"),
                New KeyValuePair(Of String, String)("fantas", "Fantasy"),
                New KeyValuePair(Of String, String)("giallo", "Giallo"),
                New KeyValuePair(Of String, String)("detective", "Giallo"),
                New KeyValuePair(Of String, String)("avventur", "Avventura"),
                New KeyValuePair(Of String, String)("adventure", "Avventura"),
                New KeyValuePair(Of String, String)("politic", "Politica"),
                New KeyValuePair(Of String, String)("economia", "Economia"),
                New KeyValuePair(Of String, String)("economics", "Economia"),
                New KeyValuePair(Of String, String)("diritto", "Diritto"),
                New KeyValuePair(Of String, String)("law", "Diritto"),
                New KeyValuePair(Of String, String)("medicina", "Medicina"),
                New KeyValuePair(Of String, String)("medic", "Medicina")
            }

            For Each voce As String In elenco
                Dim voceMinuscola As String = voce.ToLowerInvariant()
                For Each coppia As KeyValuePair(Of String, String) In mappaGeneri
                    If voceMinuscola.Contains(coppia.Key) Then Return coppia.Value
                Next
            Next

            Return generePredefinito
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.ClassificaGenere: " & ex.ToString())
            Return generePredefinito
        End Try
    End Function

    Public Function OttieniAutoriCatalogati() As List(Of String)
        Try
            Return _database.OttieniAutori()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniAutoriCatalogati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Function OttieniGeneriCatalogati() As List(Of String)
        Try
            Return _database.OttieniGeneri()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniGeneriCatalogati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Function OttieniSupportiCatalogati() As List(Of String)
        Try
            Return _database.OttieniSupporti()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniSupportiCatalogati: " & ex.ToString())
            Return New List(Of String)
        End Try
    End Function

    Public Function OttieniElementiPerAutore(autore As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Try
            Return _database.OttieniElementiPerAutore(autore, count, titoloCursore, idCursore, titoloUltimoElemento, idUltimoElemento)
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniElementiPerAutore: " & ex.ToString())
            titoloUltimoElemento = titoloCursore
            idUltimoElemento = idCursore
            Return New List(Of ElementoArchivio)
        End Try
    End Function

    Public Function OttieniElementiPerGenere(genere As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Try
            Return _database.OttieniElementiPerGenere(genere, count, titoloCursore, idCursore, titoloUltimoElemento, idUltimoElemento)
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniElementiPerGenere: " & ex.ToString())
            titoloUltimoElemento = titoloCursore
            idUltimoElemento = idCursore
            Return New List(Of ElementoArchivio)
        End Try
    End Function

    Public Function OttieniElementiPerSupporto(tipoSupporto As String, count As Integer, titoloCursore As String, idCursore As Long, ByRef titoloUltimoElemento As String, ByRef idUltimoElemento As Long) As List(Of ElementoArchivio)
        Try
            Return _database.OttieniElementiPerSupporto(tipoSupporto, count, titoloCursore, idCursore, titoloUltimoElemento, idUltimoElemento)
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.OttieniElementiPerSupporto: " & ex.ToString())
            titoloUltimoElemento = titoloCursore
            idUltimoElemento = idCursore
            Return New List(Of ElementoArchivio)
        End Try
    End Function

    Public Function ContaElementiCatalogati() As Integer
        Try
            Return _database.ContaElementi()
        Catch ex As Exception
            _logger.ScriviLog("CLSInternetArchive.ContaElementiCatalogati: " & ex.ToString())
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
                    _logger.ScriviLog("CLSInternetArchive.ScaricaStringa (" & url & ") tentativo " & tentativo.ToString() & " di " & numeroTentativiMassimo.ToString() & ": " & ex.ToString())
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
            _logger.ScriviLog("CLSInternetArchive.ScaricaStringa (" & url & "): " & ex.ToString())
            Return ""
        End Try
    End Function

End Class