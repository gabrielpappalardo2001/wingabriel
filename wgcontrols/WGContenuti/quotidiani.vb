Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.IO
Imports System.Diagnostics
Imports System.Xml.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb
Imports AngleDom = AngleSharp.Dom
Imports AngleHtml = AngleSharp.Html.Parser
Imports AngleHtmlDom = AngleSharp.Html.Dom

Public Enum TipoQuotidiano
    WordPress
    Rss
    SitemapNews
    Sconosciuto
End Enum

Public Class Quotidiano
    Public Property Titolo As String
    Public Property Url As String
    Public Property CategoriaId As String
    Public Property Tipo As TipoQuotidiano
    Public Property FeedUrl As String
    Public Property Preferito As Boolean
    Public Property UltimoAccesso As DateTime?
End Class

Public Class CategoriaQuotidiani
    Public Property Id As String
    Public Property Nome As String
End Class

Public Class Articolo
    Public Property Id As String
    Public Property Titolo As String
    Public Property Testo As String
    Public Property Url As String
    Public Property Categorie As New List(Of String)
    Public Property APagamento As Boolean
End Class

Public Class Categoria
    Public Property Id As String
    Public Property Nome As String
End Class

Public Interface IGestoreArticoli
    Function OttieniUltimiArticoli(Optional ByVal numero As Integer = 100) As List(Of Articolo)
    Function OttieniArticoloPerId(ByVal id As String) As Articolo
    Function OttieniArticoliPerCategoria(ByVal categoriaId As String, Optional ByVal numero As Integer = 100, Optional ByVal paginaIniziale As Integer = 1) As List(Of Articolo)
    Function OttieniCategorie() As List(Of Categoria)
    Function RicercaArticoli(ByVal termineRicerca As String) As List(Of Articolo)
End Interface

Friend Module UtilitaHttp
    Public Const UserAgent As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    Private ReadOnly logger As New Logger()

    Public Function ScaricaConRetry(ByVal url As String, Optional ByVal maxTentativi As Integer = 3, Optional ByVal timeoutMs As Integer = 8000) As String
        Try
            Dim risultato As RisultatoDownloadPagina = ScaricatorePagineWeb.ScaricaTesto(url, maxTentativi, timeoutMs)
            If risultato Is Nothing Then Return Nothing
            Return risultato.Html
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricaConRetry per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function ScaricaConHeaders(ByVal url As String, ByRef headers As WebHeaderCollection, Optional ByVal maxTentativi As Integer = 3, Optional ByVal timeoutMs As Integer = 8000) As String
        Try
            Dim risultato As RisultatoDownloadPagina = ScaricatorePagineWeb.ScaricaTesto(url, maxTentativi, timeoutMs)
            If risultato Is Nothing Then Return Nothing
            headers = risultato.Intestazioni
            Return risultato.Html
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricaConHeaders per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function Raggiungibile(ByVal url As String, Optional ByVal timeoutMs As Integer = 5000) As Boolean
        Try
            Return ScaricatorePagineWeb.Raggiungibile(url, timeoutMs)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in Raggiungibile per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function
End Module


Friend Class CandidatoTesto
    Public Property Priorita As Integer
    Public Property Testo As String
End Class

Friend Class VoceVisitaNodo
    Public Property Nodo As AngleDom.INode
    Public Property Uscita As Boolean
End Class

Friend NotInheritable Class PrioritaCandidato
    Public Const LettoreMozilla As Integer = 1
    Public Const SelettoriNoti As Integer = 2
    Public Const DatiStrutturati As Integer = 3
    Public Const DensitaParagrafi As Integer = 4
    Public Const LettoreIncerto As Integer = 5

    Private Sub New()
    End Sub
End Class

Friend Class RisultatoDownloadPagina
    Public Property Html As String
    Public Property UrlFinale As String
    Public Property Intestazioni As WebHeaderCollection
End Class

Friend Class EsitoTentativoDownload
    Public Property Risultato As RisultatoDownloadPagina
    Public Property Ritentabile As Boolean
    Public Property RitardoMs As Integer
End Class

Friend Class EccezioneRispostaHttp
    Inherits Exception

    Public ReadOnly Property CodiceStato As Integer
    Public ReadOnly Property RitardoRichiestoMs As Integer

    Public Sub New(ByVal codice As Integer, ByVal indirizzo As String, ByVal ritardoMs As Integer)
        MyBase.New($"Il server ha risposto con il codice HTTP {codice} per {indirizzo}")
        CodiceStato = codice
        RitardoRichiestoMs = ritardoMs
    End Sub

    Public ReadOnly Property Transitoria As Boolean
        Get
            Return CodiceStato = 408 OrElse CodiceStato = 429 OrElse (CodiceStato >= 500 AndAlso CodiceStato <= 599)
        End Get
    End Property
End Class

Friend NotInheritable Class ScaricatorePagineWeb
    Private Shared ReadOnly logger As New Logger()
    Private Shared ReadOnly lockClient As New Object()
    Private Shared ReadOnly lockCodifica As New Object()
    Private Shared clientCondiviso As HttpClient = Nothing
    Private Shared codificaOccidentaleCache As Encoding = Nothing
    Private Const DimensioneMassimaByte As Integer = 15 * 1024 * 1024
    Private Const RedirectMassimi As Integer = 10
    Private Const TentativiPagina As Integer = 2
    Private Const TimeoutPaginaMs As Integer = 25000
    Private Const TimeoutMassimoClientSecondi As Integer = 180
    Private Const RitardoBaseMs As Integer = 500
    Private Const RitardoBaseLimiteRichiesteMs As Integer = 2000
    Private Const RitardoMassimoMs As Integer = 30000
    Private Const AccettazionePagina As String = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
    Private Const AccettazioneGenerica As String = "*/*"
    Private Shared ReadOnly RegexMetaCharset As New Regex("<meta[^>]+charset\s*=\s*[""']?\s*([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)
    Private Shared ReadOnly RegexDichiarazioneXml As New Regex("^\s*<\?xml[^>]*\bencoding\s*=\s*[""']\s*([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Sub New()
    End Sub

    Public Shared Function Scarica(ByVal url As String) As RisultatoDownloadPagina
        Try
            Return ScaricaConTentativi(url, TentativiPagina, TimeoutPaginaMs, True)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.Scarica per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Shared Function ScaricaTesto(ByVal url As String, ByVal maxTentativi As Integer, ByVal timeoutMs As Integer) As RisultatoDownloadPagina
        Try
            Return ScaricaConTentativi(url, maxTentativi, timeoutMs, False)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ScaricaTesto per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ScaricaConTentativi(ByVal url As String, ByVal maxTentativi As Integer, ByVal timeoutMs As Integer, ByVal modalitaPagina As Boolean) As RisultatoDownloadPagina
        Try
            Dim uriIniziale As Uri = Nothing
            If Not ConvalidaUrl(url, uriIniziale) Then Return Nothing

            Dim tentativi As Integer = Math.Max(1, maxTentativi)
            Dim durata As Integer = Math.Max(1000, timeoutMs)

            For tentativo As Integer = 1 To tentativi
                Dim esito As EsitoTentativoDownload = EseguiTentativo(uriIniziale, durata, modalitaPagina)
                If esito Is Nothing Then Return Nothing
                If esito.Risultato IsNot Nothing Then Return esito.Risultato
                If Not esito.Ritentabile Then Return Nothing
                If tentativo < tentativi Then
                    Dim attesa As Integer = Math.Max(esito.RitardoMs, RitardoBaseMs * tentativo)
                    Thread.Sleep(Math.Min(attesa, RitardoMassimoMs))
                End If
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ScaricaConTentativi per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Shared Function Raggiungibile(ByVal url As String, ByVal timeoutMs As Integer) As Boolean
        Dim uriValido As Uri = Nothing
        Try
            If Not ConvalidaUrl(url, uriValido) Then Return False
            Dim client As HttpClient = OttieniClient()
            If client Is Nothing Then Return False

            Using annullamento As New CancellationTokenSource(Math.Max(1000, timeoutMs))
                Using richiesta As New HttpRequestMessage(HttpMethod.Head, uriValido)
                    ImpostaIntestazioniRichiesta(richiesta, AccettazioneGenerica)
                    Using risposta As HttpResponseMessage = client.SendAsync(richiesta, HttpCompletionOption.ResponseHeadersRead, annullamento.Token).GetAwaiter().GetResult()
                        Return risposta IsNot Nothing
                    End Using
                End Using
            End Using
        Catch ex As OperationCanceledException
            logger.ScriviLog($"Raggiungibile fallito per {url}: tempo scaduto, {ex.Message}")
            Return False
        Catch ex As HttpRequestException
            logger.ScriviLog($"Raggiungibile fallito per {url}: {ex.Message} {MessaggioInterno(ex)}")
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in ScaricatorePagineWeb.Raggiungibile per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function ConvalidaUrl(ByVal url As String, ByRef uriValido As Uri) As Boolean
        uriValido = Nothing
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False
            Dim candidato As Uri = Nothing
            If Not Uri.TryCreate(url.Trim(), UriKind.Absolute, candidato) Then Return False
            If Not SchemaConsentito(candidato) Then Return False
            uriValido = candidato
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ConvalidaUrl per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            uriValido = Nothing
            Return False
        End Try
    End Function

    Private Shared Function SchemaConsentito(ByVal indirizzo As Uri) As Boolean
        Try
            If indirizzo Is Nothing OrElse Not indirizzo.IsAbsoluteUri Then Return False
            If String.Equals(indirizzo.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) Then Return True
            Return String.Equals(indirizzo.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.SchemaConsentito: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function OttieniClient() As HttpClient
        Try
            SyncLock lockClient
                If clientCondiviso Is Nothing Then
                    AssicuraProtocolliSicuri()
                    Dim gestore As New HttpClientHandler()
                    gestore.AllowAutoRedirect = False
                    gestore.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
                    gestore.UseCookies = True
                    gestore.CookieContainer = New CookieContainer()
                    Dim client As New HttpClient(gestore, True)
                    client.Timeout = TimeSpan.FromSeconds(TimeoutMassimoClientSecondi)
                    client.MaxResponseContentBufferSize = DimensioneMassimaByte
                    clientCondiviso = client
                End If
                Return clientCondiviso
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.OttieniClient: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Sub AssicuraProtocolliSicuri()
        Try
            Dim attuali As SecurityProtocolType = ServicePointManager.SecurityProtocol
            If CInt(attuali) = 0 Then Return
            If (attuali And SecurityProtocolType.Tls12) <> SecurityProtocolType.Tls12 Then
                ServicePointManager.SecurityProtocol = attuali Or SecurityProtocolType.Tls12
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.AssicuraProtocolliSicuri: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub ImpostaIntestazioniRichiesta(ByVal richiesta As HttpRequestMessage, ByVal accettazione As String)
        Try
            richiesta.Headers.TryAddWithoutValidation("User-Agent", UtilitaHttp.UserAgent)
            richiesta.Headers.TryAddWithoutValidation("Accept", accettazione)
            richiesta.Headers.TryAddWithoutValidation("Accept-Language", "it-IT,it;q=0.9,en-US;q=0.6,en;q=0.5")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ImpostaIntestazioniRichiesta: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function EseguiTentativo(ByVal uriIniziale As Uri, ByVal timeoutMs As Integer, ByVal modalitaPagina As Boolean) As EsitoTentativoDownload
        Dim uriCorrente As Uri = uriIniziale
        Try
            Dim client As HttpClient = OttieniClient()
            If client Is Nothing Then Return New EsitoTentativoDownload With {.Ritentabile = False}

            Dim accettazione As String = AccettazioneGenerica
            If modalitaPagina Then accettazione = AccettazionePagina

            Using annullamento As New CancellationTokenSource(timeoutMs)
                For passo As Integer = 0 To RedirectMassimi
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, uriCorrente)
                        ImpostaIntestazioniRichiesta(richiesta, accettazione)

                        Using risposta As HttpResponseMessage = client.SendAsync(richiesta, HttpCompletionOption.ResponseContentRead, annullamento.Token).GetAwaiter().GetResult()
                            Dim codice As Integer = CInt(risposta.StatusCode)

                            If codice >= 300 AndAlso codice <= 399 AndAlso risposta.Headers.Location IsNot Nothing Then
                                Dim destinazione As Uri = RisolviDestinazione(uriCorrente, risposta)
                                If destinazione Is Nothing Then Return New EsitoTentativoDownload With {.Ritentabile = False}
                                uriCorrente = destinazione
                                Continue For
                            End If

                            If codice < 200 OrElse codice > 299 Then
                                Throw New EccezioneRispostaHttp(codice, uriCorrente.AbsoluteUri, CalcolaRitardoRichiesto(risposta))
                            End If

                            If modalitaPagina AndAlso Not ContenutoTestuale(risposta) Then Return New EsitoTentativoDownload With {.Ritentabile = False}

                            Dim dati As Byte() = risposta.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                            If dati Is Nothing OrElse dati.Length = 0 Then Return New EsitoTentativoDownload With {.Ritentabile = False}

                            Dim charset As String = Nothing
                            If risposta.Content.Headers.ContentType IsNot Nothing Then charset = risposta.Content.Headers.ContentType.CharSet

                            Dim testo As String = DecodificaTesto(dati, charset)
                            If String.IsNullOrWhiteSpace(testo) Then Return New EsitoTentativoDownload With {.Ritentabile = False}

                            Return New EsitoTentativoDownload With {
                                .Risultato = New RisultatoDownloadPagina With {
                                    .Html = testo,
                                    .UrlFinale = uriCorrente.AbsoluteUri,
                                    .Intestazioni = CopiaIntestazioni(risposta)
                                },
                                .Ritentabile = False
                            }
                        End Using
                    End Using
                Next
            End Using

            Throw New InvalidOperationException($"Troppi reindirizzamenti consecutivi per {uriIniziale.AbsoluteUri}")
        Catch ex As EccezioneRispostaHttp
            logger.ScriviLog($"Download non riuscito: {ex.Message}")
            Dim ritardo As Integer = RitardoBaseMs
            If ex.CodiceStato = 429 Then ritardo = Math.Max(RitardoBaseLimiteRichiesteMs, ex.RitardoRichiestoMs)
            Return New EsitoTentativoDownload With {.Ritentabile = ex.Transitoria, .RitardoMs = ritardo}
        Catch ex As OperationCanceledException
            logger.ScriviLog($"Tempo scaduto scaricando {uriCorrente}: {ex.Message}")
            Return New EsitoTentativoDownload With {.Ritentabile = True, .RitardoMs = RitardoBaseMs}
        Catch ex As HttpRequestException
            logger.ScriviLog($"HttpRequestException scaricando {uriCorrente}: {ex.Message} {MessaggioInterno(ex)}")
            Return New EsitoTentativoDownload With {.Ritentabile = ErroreDiReteTransitorio(ex), .RitardoMs = RitardoBaseMs}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.EseguiTentativo per {uriCorrente}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New EsitoTentativoDownload With {.Ritentabile = False}
        End Try
    End Function

    Private Shared Function CalcolaRitardoRichiesto(ByVal risposta As HttpResponseMessage) As Integer
        Try
            If risposta Is Nothing OrElse risposta.Headers.RetryAfter Is Nothing Then Return 0
            Dim intestazione As RetryConditionHeaderValue = risposta.Headers.RetryAfter
            If intestazione.Delta.HasValue Then
                Return CInt(Math.Min(intestazione.Delta.Value.TotalMilliseconds, RitardoMassimoMs))
            End If
            If intestazione.Date.HasValue Then
                Dim differenza As Double = (intestazione.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds
                If differenza <= 0 Then Return 0
                Return CInt(Math.Min(differenza, RitardoMassimoMs))
            End If
            Return 0
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.CalcolaRitardoRichiesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Shared Function ErroreDiReteTransitorio(ByVal ex As HttpRequestException) As Boolean
        Try
            Dim interna As Exception = ex.InnerException
            While interna IsNot Nothing
                Dim erroreWeb As WebException = TryCast(interna, WebException)
                If erroreWeb IsNot Nothing AndAlso (erroreWeb.Status = WebExceptionStatus.NameResolutionFailure OrElse erroreWeb.Status = WebExceptionStatus.TrustFailure OrElse erroreWeb.Status = WebExceptionStatus.SecureChannelFailure) Then
                    Return False
                End If
                Dim erroreSocket As System.Net.Sockets.SocketException = TryCast(interna, System.Net.Sockets.SocketException)
                If erroreSocket IsNot Nothing AndAlso (erroreSocket.SocketErrorCode = System.Net.Sockets.SocketError.HostNotFound OrElse erroreSocket.SocketErrorCode = System.Net.Sockets.SocketError.NoData) Then
                    Return False
                End If
                If TypeOf interna Is System.Security.Authentication.AuthenticationException Then Return False
                interna = interna.InnerException
            End While
            Return True
        Catch exControllo As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ErroreDiReteTransitorio: {exControllo.Message} - StackTrace: {exControllo.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function MessaggioInterno(ByVal ex As Exception) As String
        Try
            Dim messaggi As New StringBuilder()
            Dim interna As Exception = ex.InnerException
            While interna IsNot Nothing
                If messaggi.Length > 0 Then messaggi.Append(" | ")
                messaggi.Append(interna.Message)
                interna = interna.InnerException
            End While
            Return messaggi.ToString()
        Catch exMessaggio As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.MessaggioInterno: {exMessaggio.Message} - StackTrace: {exMessaggio.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function CopiaIntestazioni(ByVal risposta As HttpResponseMessage) As WebHeaderCollection
        Dim collezione As New WebHeaderCollection()
        Try
            If risposta Is Nothing Then Return collezione
            For Each intestazione As KeyValuePair(Of String, IEnumerable(Of String)) In risposta.Headers
                AggiungiIntestazione(collezione, intestazione.Key, intestazione.Value)
            Next
            If risposta.Content IsNot Nothing Then
                For Each intestazione As KeyValuePair(Of String, IEnumerable(Of String)) In risposta.Content.Headers
                    AggiungiIntestazione(collezione, intestazione.Key, intestazione.Value)
                Next
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.CopiaIntestazioni: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return collezione
    End Function

    Private Shared Sub AggiungiIntestazione(ByVal collezione As WebHeaderCollection, ByVal nome As String, ByVal valori As IEnumerable(Of String))
        Try
            If String.IsNullOrEmpty(nome) OrElse valori Is Nothing Then Return
            collezione.Add(nome, String.Join(", ", valori))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.AggiungiIntestazione ({nome}): {ex.Message}")
        End Try
    End Sub

    Private Shared Function RisolviDestinazione(ByVal uriCorrente As Uri, ByVal risposta As HttpResponseMessage) As Uri
        Try
            If risposta Is Nothing OrElse risposta.Headers.Location Is Nothing Then Return Nothing
            Dim destinazione As Uri = risposta.Headers.Location
            If Not destinazione.IsAbsoluteUri Then destinazione = New Uri(uriCorrente, destinazione)
            If Not SchemaConsentito(destinazione) Then Return Nothing
            Return destinazione
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.RisolviDestinazione per {uriCorrente}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ContenutoTestuale(ByVal risposta As HttpResponseMessage) As Boolean
        Try
            If risposta Is Nothing OrElse risposta.Content Is Nothing Then Return False
            Dim tipo As MediaTypeHeaderValue = risposta.Content.Headers.ContentType
            If tipo Is Nothing OrElse String.IsNullOrEmpty(tipo.MediaType) Then Return True
            Dim media As String = tipo.MediaType.ToLowerInvariant()
            If media.StartsWith("text/", StringComparison.Ordinal) Then Return True
            If media.Contains("html") Then Return True
            Return media.Contains("xml")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ContenutoTestuale: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Friend Shared Function DecodificaTesto(ByVal dati As Byte(), ByVal charsetDichiarato As String) As String
        Try
            If dati Is Nothing OrElse dati.Length = 0 Then Return String.Empty

            If dati.Length >= 3 AndAlso dati(0) = &HEF AndAlso dati(1) = &HBB AndAlso dati(2) = &HBF Then
                Return New UTF8Encoding(False).GetString(dati, 3, dati.Length - 3)
            End If
            If dati.Length >= 2 AndAlso dati(0) = &HFF AndAlso dati(1) = &HFE Then
                Return Encoding.Unicode.GetString(dati, 2, dati.Length - 2)
            End If
            If dati.Length >= 2 AndAlso dati(0) = &HFE AndAlso dati(1) = &HFF Then
                Return Encoding.BigEndianUnicode.GetString(dati, 2, dati.Length - 2)
            End If

            Dim codifica As Encoding = RisolviCodifica(charsetDichiarato)
            If codifica Is Nothing Then
                Dim intestazione As String = Encoding.ASCII.GetString(dati, 0, Math.Min(dati.Length, 16384))
                Dim dichiarazioneXml As Match = RegexDichiarazioneXml.Match(intestazione)
                If dichiarazioneXml.Success Then
                    codifica = RisolviCodifica(dichiarazioneXml.Groups(1).Value)
                Else
                    Dim metaCharset As Match = RegexMetaCharset.Match(intestazione)
                    If metaCharset.Success Then codifica = RisolviCodifica(metaCharset.Groups(1).Value)
                End If
            End If

            If codifica Is Nothing OrElse codifica.CodePage = 65001 Then
                If Utf8Valido(dati) Then Return New UTF8Encoding(False).GetString(dati)
                Return CodificaOccidentale().GetString(dati)
            End If

            If (codifica.CodePage = 1252 OrElse codifica.CodePage = 28591) AndAlso ContieneByteEstesi(dati) AndAlso Utf8Valido(dati) Then
                Return New UTF8Encoding(False).GetString(dati)
            End If

            Return codifica.GetString(dati)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.DecodificaTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function RisolviCodifica(ByVal nome As String) As Encoding
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return Nothing
            Dim pulito As String = nome.Trim().Trim(""""c, "'"c).ToLowerInvariant()

            Select Case pulito
                Case "utf-8", "utf8", "unicode-1-1-utf-8", "utf-16", "utf-16le", "utf-16be", "unicode"
                    Return New UTF8Encoding(False)
                Case "iso-8859-1", "iso8859-1", "iso_8859-1", "latin1", "latin-1", "l1", "us-ascii", "ascii", "windows-1252", "cp1252", "x-cp1252"
                    Return CodificaOccidentale()
            End Select

            For Each informazioni As EncodingInfo In Encoding.GetEncodings()
                If String.Equals(informazioni.Name, pulito, StringComparison.OrdinalIgnoreCase) Then
                    Return informazioni.GetEncoding()
                End If
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.RisolviCodifica per '{nome}': {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function CodificaOccidentale() As Encoding
        Try
            SyncLock lockCodifica
                If codificaOccidentaleCache Is Nothing Then
                    For Each informazioni As EncodingInfo In Encoding.GetEncodings()
                        If informazioni.CodePage = 1252 Then
                            codificaOccidentaleCache = informazioni.GetEncoding()
                            Exit For
                        End If
                    Next
                    If codificaOccidentaleCache Is Nothing Then codificaOccidentaleCache = Encoding.GetEncoding(28591)
                End If
                Return codificaOccidentaleCache
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.CodificaOccidentale: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New UTF8Encoding(False)
        End Try
    End Function

    Private Shared Function ContieneByteEstesi(ByVal dati As Byte()) As Boolean
        Try
            For indice As Integer = 0 To dati.Length - 1
                If dati(indice) >= &H80 Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.ContieneByteEstesi: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function Utf8Valido(ByVal dati As Byte()) As Boolean
        Try
            Dim indice As Integer = 0
            Dim lunghezza As Integer = dati.Length
            While indice < lunghezza
                Dim primo As Integer = dati(indice)
                If primo < &H80 Then
                    indice += 1
                    Continue While
                End If

                Dim seguenti As Integer
                If primo >= &HC2 AndAlso primo <= &HDF Then
                    seguenti = 1
                ElseIf primo >= &HE0 AndAlso primo <= &HEF Then
                    seguenti = 2
                ElseIf primo >= &HF0 AndAlso primo <= &HF4 Then
                    seguenti = 3
                Else
                    Return False
                End If

                If indice + seguenti >= lunghezza Then Return True

                For posizione As Integer = 1 To seguenti
                    Dim continuazione As Integer = dati(indice + posizione)
                    If continuazione < &H80 OrElse continuazione > &HBF Then Return False
                Next
                indice += seguenti + 1
            End While
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricatorePagineWeb.Utf8Valido: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function
End Class

Friend NotInheritable Class ConvertitoreHtmlTesto
    Private Shared ReadOnly logger As New Logger()
    Private Const LunghezzaMassimaRigaServizio As Integer = 90
    Private Const LunghezzaMassimaRigaRimando As Integer = 260
    Private Const LunghezzaMinimaSuddivisione As Integer = 900
    Private Const LunghezzaParagrafoSuddiviso As Integer = 450
    Private Const QuotaTestoProtetta As Double = 0.5
    Private Const RigheMassimeCoda As Integer = 15
    Private Const LunghezzaMassimaRigaCoda As Integer = 200
    Private Const QuotaMassimaCoda As Double = 0.35
    Private Const RigheMassimeTestata As Integer = 8
    Private Const LunghezzaMassimaRigaTestata As Integer = 600
    Private Const LunghezzaMassimaFirma As Integer = 250
    Private Const LunghezzaMassimaRigaFirmaSemplice As Integer = 120
    Private Const LunghezzaMassimaRigaLink As Integer = 80
    Private Const RigheLinkMassime As Integer = 3
    Private Const LunghezzaMassimaSegmentoSigla As Integer = 4
    Private Const CaratteriChiusura As String = "»""” ')]"

    Private Shared ReadOnly RegexConfineFrase As New Regex(
        "(?<=[.!?…][»""\u201D’)\]]*)\s+(?=[«""\u201C‘(\[]?\p{Lu})",
        RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly Abbreviazioni As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "sig", "sigg", "sigra", "dott", "dr", "prof", "ing", "avv", "arch", "geom", "rag", "on", "sen", "mons",
        "gen", "col", "magg", "ten", "cap", "mr", "mrs", "ms", "st", "sr", "jr", "ecc", "etc", "cfr", "pag", "pagg",
        "pp", "nn", "vol", "voll", "art", "artt", "co", "lett", "fig", "figg", "tab", "es", "tel", "ca", "vs", "nr",
        "num", "cit", "ed", "edd", "trad", "sez", "par", "all", "rif", "approx", "inc", "ltd", "corp", "spa", "srl"
    }

    Private Shared ReadOnly RegexRigaCoda As New Regex(
        "^(?:pubblicat[oa]\s+(?:da|il)|nel canale)\b",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexFirmaTestata As New Regex(
        "(?:^|\s)(?:di\s+(?-i:\p{Lu}[\p{L}'’.\-]*)(?:\s+(?-i:\p{Lu}[\p{L}'’.\-]*)){0,3}\s+)?pubblicat[oa]\s+(?:il|da)\s[^\r\n]*$",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexConfermaFirma As New Regex(
        "\bnel canale\b|\balle\s+\d{1,2}[:.]\d{2}\b|\b(?:19|20)\d{2}\b",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexPresenzaCanale As New Regex(
        "\bnel canale\b",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexFineFraseCoda As New Regex(
        "[.!?…][»""\u201D’)]*$",
        RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexSpazi As New Regex("\s+", RegexOptions.Compiled)
    Private Shared ReadOnly RegexMarcatore As New Regex("<[^>]*>", RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexRigaServizio As New Regex(
        "^(?:leggi anche|leggi pure|continua a leggere|leggi l['’]articolo completo|leggi tutto|potrebbe interessarti|ti potrebbe interessare|potrebbero interessarti|articoli correlati|notizie correlate|correlati|condividi|condividi su \S+|condividi (?:questo|l['’]) ?articolo|iscriviti alla (?:nostra )?newsletter|seguici su \S+|pubblicit[aà]|advertisement|sponsorizzato|contenuto sponsorizzato|©?\s*riproduzione riservata|tutti i diritti riservati|clicca qui|torna su|torna all['’]inizio|ascolta l['’]articolo|ascolta questo articolo|(?:tempo di lettura:?\s*)?\d+\s*min(?:uti|\.)?(?: di lettura)?|foto|video|galleria fotografica|tag|commenti|lascia un commento)[\s:.!»›©]*$",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexRigaRimando As New Regex(
        "^(?:leggi anche|leggi pure|potrebbe interessarti|ti potrebbe interessare|approfondisci|guarda anche|vedi anche|guarda il video|ascolta)\s*[:>»›\-–—]",
        RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexFineFrase As New Regex(
        "(?<=[.!?…][»""\u201D’]?)\s+(?=[«""\u201C‘]?\p{Lu})",
        RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Shared ReadOnly ElementiBlocco As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "p", "div", "section", "article", "main", "header", "footer", "aside", "nav", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "dl", "dt", "dd",
        "blockquote", "pre", "table", "thead", "tbody", "tfoot", "tr", "caption",
        "figure", "figcaption", "hr", "address", "details", "summary", "center", "fieldset", "body", "html"
    }

    Private Shared ReadOnly ElementiIgnorati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "script", "style", "noscript", "template", "iframe", "object", "embed", "svg", "canvas",
        "video", "audio", "picture", "img", "source", "track", "map", "button", "input", "select",
        "option", "textarea", "head", "title", "meta", "link", "math", "dialog"
    }

    Private Shared ReadOnly ElementiProtetti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "html", "body", "main", "article"
    }

    Private Shared ReadOnly SelettoriRumoreCerto As String() = {
        "script", "style", "noscript", "template", "iframe", "svg", "canvas", "object", "embed",
        "video", "audio", "picture", "button", "select", "textarea", "input", "dialog", "figcaption",
        "ins.adsbygoogle", ".sharedaddy", ".jp-relatedposts", ".addtoany_share_save_container", ".a2a_kit",
        ".yarpp-related", ".crp_related", ".wp-block-embed", ".screen-reader-text", ".sr-only", ".visually-hidden"
    }

    Private Shared ReadOnly SelettoriRumorePagina As String() = {
        "header", "footer", "[role='banner']", "[role='contentinfo']"
    }

    Private Shared ReadOnly FrammentiRumore As String() = {
        "share", "social", "related", "correlat", "newsletter", "advert", "adv-", "banner", "pubblicit",
        "comment", "sidebar", "cookie", "consent", "breadcrumb", "taboola", "outbrain", "sponsor",
        "subscribe", "abbonat", "leggi-anche", "leggianche", "read-also", "author-box", "popup"
    }

    Private Shared ReadOnly SelettoriRumoreProbabile As String() = CostruisciSelettoriRumoreProbabile()

    Private Sub New()
    End Sub

    Private Shared Function CostruisciSelettoriRumoreProbabile() As String()
        Dim elenco As New List(Of String)()
        Try
            elenco.Add("nav")
            elenco.Add("aside")
            elenco.Add("form")
            elenco.Add("[role='navigation']")
            elenco.Add("[role='complementary']")
            elenco.Add("[aria-hidden='true']")
            For Each frammento As String In FrammentiRumore
                elenco.Add("[class*='" & frammento & "']")
                elenco.Add("[id*='" & frammento & "']")
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.CostruisciSelettoriRumoreProbabile: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return elenco.ToArray()
    End Function

    Friend Shared Function ConvertiFrammento(ByVal html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty
            Dim analizzatore As New AngleHtml.HtmlParser()
            Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument("<!DOCTYPE html><html><head></head><body>" & html & "</body></html>")
                If documento Is Nothing OrElse documento.Body Is Nothing Then Return String.Empty
                RimuoviRumore(documento.Body, False)
                Return TestoDaNodo(documento.Body)
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.ConvertiFrammento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Friend Shared Sub RimuoviRumore(ByVal radice As AngleDom.IElement, ByVal livelloPagina As Boolean)
        Try
            If radice Is Nothing Then Return
            RimuoviSelettori(radice, SelettoriRumoreCerto, False, 0)
            Dim lunghezzaTotale As Integer = LunghezzaTesto(radice)
            RimuoviSelettori(radice, SelettoriRumoreProbabile, True, lunghezzaTotale)
            If livelloPagina Then RimuoviSelettori(radice, SelettoriRumorePagina, True, lunghezzaTotale)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RimuoviRumore: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub RimuoviSelettori(ByVal radice As AngleDom.IElement, ByVal selettori As String(), ByVal conControllo As Boolean, ByVal lunghezzaTotale As Integer)
        Try
            For Each selettore As String In selettori
                RimuoviSelettore(radice, selettore, conControllo, lunghezzaTotale)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RimuoviSelettori: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub RimuoviSelettore(ByVal radice As AngleDom.IElement, ByVal selettore As String, ByVal conControllo As Boolean, ByVal lunghezzaTotale As Integer)
        Try
            Dim trovati As New List(Of AngleDom.IElement)()
            For Each elemento As AngleDom.IElement In radice.QuerySelectorAll(selettore)
                trovati.Add(elemento)
            Next

            For Each elemento As AngleDom.IElement In trovati
                If Object.ReferenceEquals(elemento, radice) Then Continue For
                If Not radice.Contains(elemento) Then Continue For
                If conControllo Then
                    If ElementiProtetti.Contains(elemento.LocalName) Then Continue For
                    If ContieneCorpoArticolo(elemento) Then Continue For
                    If lunghezzaTotale > 0 AndAlso LunghezzaTesto(elemento) > lunghezzaTotale * QuotaTestoProtetta Then Continue For
                End If
                elemento.Remove()
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RimuoviSelettore ({selettore}): {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function ContieneCorpoArticolo(ByVal elemento As AngleDom.IElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If String.Equals(elemento.GetAttribute("itemprop"), "articleBody", StringComparison.OrdinalIgnoreCase) Then Return True
            Return elemento.QuerySelector("[itemprop='articleBody']") IsNot Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.ContieneCorpoArticolo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Friend Shared Function LunghezzaTesto(ByVal nodo As AngleDom.INode) As Integer
        Try
            If nodo Is Nothing Then Return 0
            Dim testo As String = nodo.TextContent
            If String.IsNullOrEmpty(testo) Then Return 0
            Return testo.Length
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.LunghezzaTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return 0
        End Try
    End Function

    Friend Shared Function LunghezzaTestoParagrafi(ByVal elemento As AngleDom.IElement) As Integer
        Try
            If elemento Is Nothing Then Return 0
            Dim somma As Integer = 0
            For Each paragrafo As AngleDom.IElement In elemento.QuerySelectorAll("p")
                Dim testo As String = paragrafo.TextContent
                If Not String.IsNullOrEmpty(testo) Then somma += testo.Trim().Length
            Next
            Dim metaTestoTotale As Integer = LunghezzaTesto(elemento) \ 2
            Return Math.Max(somma, metaTestoTotale)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.LunghezzaTestoParagrafi: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return 0
        End Try
    End Function

    Friend Shared Function LunghezzaTestoLink(ByVal elemento As AngleDom.IElement) As Integer
        Try
            If elemento Is Nothing Then Return 0
            Dim somma As Integer = 0
            For Each collegamento As AngleDom.IElement In elemento.QuerySelectorAll("a")
                Dim testo As String = collegamento.TextContent
                If Not String.IsNullOrEmpty(testo) Then somma += testo.Trim().Length
            Next
            Return somma
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.LunghezzaTestoLink: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return 0
        End Try
    End Function

    Friend Shared Function TestoDaNodo(ByVal radice As AngleDom.INode) As String
        Try
            If radice Is Nothing Then Return String.Empty
            Dim costruttore As New StringBuilder()
            Dim pila As New Stack(Of VoceVisitaNodo)()
            pila.Push(New VoceVisitaNodo With {.Nodo = radice, .Uscita = False})

            While pila.Count > 0
                Dim voce As VoceVisitaNodo = pila.Pop()
                Dim nodo As AngleDom.INode = voce.Nodo
                If nodo Is Nothing Then Continue While

                If voce.Uscita Then
                    AggiungiInterruzione(costruttore)
                    Continue While
                End If

                If nodo.NodeType = AngleDom.NodeType.Text Then
                    AggiungiTesto(costruttore, nodo.TextContent)
                    Continue While
                End If

                If nodo.NodeType <> AngleDom.NodeType.Element AndAlso nodo.NodeType <> AngleDom.NodeType.Document AndAlso nodo.NodeType <> AngleDom.NodeType.DocumentFragment Then
                    Continue While
                End If

                Dim elemento As AngleDom.IElement = TryCast(nodo, AngleDom.IElement)
                If elemento IsNot Nothing Then
                    Dim nome As String = elemento.LocalName
                    If ElementiIgnorati.Contains(nome) Then Continue While
                    If ElementoNascosto(elemento) Then Continue While

                    If String.Equals(nome, "br", StringComparison.OrdinalIgnoreCase) Then
                        costruttore.Append(ControlChars.Lf)
                        Continue While
                    End If

                    If String.Equals(nome, "td", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(nome, "th", StringComparison.OrdinalIgnoreCase) Then
                        AggiungiSpazio(costruttore)
                    End If

                    If ElementiBlocco.Contains(nome) Then
                        AggiungiInterruzione(costruttore)
                        pila.Push(New VoceVisitaNodo With {.Nodo = nodo, .Uscita = True})
                    End If
                End If

                Dim figli As AngleDom.INodeList = nodo.ChildNodes
                If figli Is Nothing Then Continue While
                For indice As Integer = figli.Length - 1 To 0 Step -1
                    pila.Push(New VoceVisitaNodo With {.Nodo = figli(indice), .Uscita = False})
                Next
            End While

            Return NormalizzaTesto(costruttore.ToString())
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.TestoDaNodo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function ElementoNascosto(ByVal elemento As AngleDom.IElement) As Boolean
        Try
            If elemento.HasAttribute("hidden") Then Return True
            Dim stile As String = elemento.GetAttribute("style")
            If String.IsNullOrEmpty(stile) Then Return False
            Dim compatto As String = stile.Replace(" ", String.Empty).ToLowerInvariant()
            Return compatto.Contains("display:none") OrElse compatto.Contains("visibility:hidden")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.ElementoNascosto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Sub AggiungiInterruzione(ByVal costruttore As StringBuilder)
        Try
            If costruttore.Length = 0 Then Return
            If costruttore(costruttore.Length - 1) <> ControlChars.Lf Then costruttore.Append(ControlChars.Lf)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.AggiungiInterruzione: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AggiungiSpazio(ByVal costruttore As StringBuilder)
        Try
            If costruttore.Length = 0 Then Return
            Dim ultimo As Char = costruttore(costruttore.Length - 1)
            If ultimo <> " "c AndAlso ultimo <> ControlChars.Lf Then costruttore.Append(" "c)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.AggiungiSpazio: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AggiungiTesto(ByVal costruttore As StringBuilder, ByVal testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Dim compattato As String = RegexSpazi.Replace(testo, " ")
            If compattato = " " Then
                AggiungiSpazio(costruttore)
                Return
            End If
            costruttore.Append(compattato)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.AggiungiTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Friend Shared Function NormalizzaTesto(ByVal testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return String.Empty

            Dim preparato As String = testo.Replace(ChrW(&HA0), " "c)
            preparato = preparato.Replace(ChrW(&H200B).ToString(), String.Empty)
            preparato = preparato.Replace(ChrW(&H200C).ToString(), String.Empty)
            preparato = preparato.Replace(ChrW(&H200D).ToString(), String.Empty)
            preparato = preparato.Replace(ChrW(&HFEFF).ToString(), String.Empty)
            preparato = preparato.Replace(ChrW(&HAD).ToString(), String.Empty)
            preparato = preparato.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)

            Dim righeGrezze As String() = preparato.Split(ControlChars.Lf)
            Dim righe As New List(Of String)()
            Dim precedente As String = Nothing

            For Each rigaGrezza As String In righeGrezze
                Dim riga As String = RegexSpazi.Replace(rigaGrezza, " ").Trim()
                If riga.Length = 0 Then Continue For
                If RigaDiServizio(riga) Then Continue For
                If precedente IsNot Nothing AndAlso String.Equals(riga, precedente, StringComparison.Ordinal) Then Continue For
                righe.Add(riga)
                precedente = riga
            Next

            RimuoviTestataFirma(righe)
            RimuoviCodaFirma(righe)

            Return String.Join(vbCrLf, righe)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.NormalizzaTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(testo, String.Empty)
        End Try
    End Function

    Private Shared Sub RimuoviTestataFirma(ByVal righe As List(Of String))
        Try
            If righe Is Nothing OrElse righe.Count < 2 Then Return

            Dim lunghezzaTotale As Integer = 0
            For Each riga As String In righe
                lunghezzaTotale += riga.Length
            Next
            If lunghezzaTotale = 0 Then Return

            Dim limite As Integer = Math.Min(righe.Count - 1, RigheMassimeTestata)
            For indice As Integer = 0 To limite - 1
                Dim riga As String = righe(indice)
                If riga.Length > LunghezzaMassimaRigaTestata Then Continue For

                Dim parteFirma As String = Nothing
                Dim prefisso As String = String.Empty

                If RegexRigaCoda.IsMatch(riga) AndAlso riga.Length <= LunghezzaMassimaRigaFirmaSemplice AndAlso Not RegexFineFraseCoda.IsMatch(riga) Then
                    parteFirma = riga
                Else
                    Dim corrispondenza As Match = RegexFirmaTestata.Match(riga)
                    If Not corrispondenza.Success Then Continue For
                    If corrispondenza.Value.Length > LunghezzaMassimaFirma Then Continue For
                    If Not RegexConfermaFirma.IsMatch(corrispondenza.Value) Then Continue For
                    parteFirma = corrispondenza.Value
                    prefisso = riga.Substring(0, corrispondenza.Index).Trim()
                End If

                Dim fine As Integer = indice + 1
                If RegexPresenzaCanale.IsMatch(parteFirma) Then
                    While fine < righe.Count - 1 AndAlso fine - indice <= RigheLinkMassime AndAlso RigaDiLink(righe(fine))
                        fine += 1
                    End While
                End If

                Dim lunghezzaRimossa As Integer = parteFirma.Length
                For posizione As Integer = indice + 1 To fine - 1
                    lunghezzaRimossa += righe(posizione).Length
                Next
                If lunghezzaRimossa > lunghezzaTotale * QuotaMassimaCoda Then Return

                If fine > indice + 1 Then righe.RemoveRange(indice + 1, fine - indice - 1)
                If prefisso.Length > 0 Then
                    righe(indice) = prefisso
                Else
                    righe.RemoveAt(indice)
                End If
                Return
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RimuoviTestataFirma: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RigaDiLink(ByVal riga As String) As Boolean
        Try
            If String.IsNullOrEmpty(riga) Then Return False
            If riga.Length > LunghezzaMassimaRigaLink Then Return False
            Return Not RegexFineFraseCoda.IsMatch(riga)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RigaDiLink: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Sub RimuoviCodaFirma(ByVal righe As List(Of String))
        Try
            If righe Is Nothing OrElse righe.Count < 2 Then Return

            Dim limite As Integer = Math.Max(1, righe.Count - RigheMassimeCoda)
            Dim taglio As Integer = -1
            For indice As Integer = righe.Count - 1 To limite Step -1
                Dim riga As String = righe(indice)
                If riga.Length > LunghezzaMassimaRigaCoda Then Exit For
                If RegexRigaCoda.IsMatch(riga) AndAlso Not RegexFineFraseCoda.IsMatch(riga) Then taglio = indice
            Next
            If taglio < 1 Then Return

            Dim lunghezzaTotale As Integer = 0
            Dim lunghezzaRimossa As Integer = 0
            For indice As Integer = 0 To righe.Count - 1
                lunghezzaTotale += righe(indice).Length
                If indice >= taglio Then lunghezzaRimossa += righe(indice).Length
            Next
            If lunghezzaTotale = 0 OrElse lunghezzaRimossa > lunghezzaTotale * QuotaMassimaCoda Then Return

            righe.RemoveRange(taglio, righe.Count - taglio)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RimuoviCodaFirma: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RigaDiServizio(ByVal riga As String) As Boolean
        Try
            If riga.Length <= LunghezzaMassimaRigaServizio AndAlso RegexRigaServizio.IsMatch(riga) Then Return True
            If riga.Length <= LunghezzaMassimaRigaRimando AndAlso RegexRigaRimando.IsMatch(riga) Then Return True
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RigaDiServizio: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Friend Shared Function UnaFrasePerRiga(ByVal testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return If(testo, String.Empty)

            Dim righe As String() = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
            Dim risultato As New StringBuilder()

            For Each riga As String In righe
                Dim pulita As String = riga.Trim()
                If pulita.Length = 0 Then Continue For
                For Each frase As String In DividiInFrasi(pulita)
                    If risultato.Length > 0 Then risultato.Append(vbCrLf)
                    risultato.Append(frase)
                Next
            Next

            Return risultato.ToString()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.UnaFrasePerRiga: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(testo, String.Empty)
        End Try
    End Function

    Friend Shared Function RigheInHtml(ByVal testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return String.Empty
            Dim righe As String() = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
            Dim risultato As New StringBuilder()
            For Each riga As String In righe
                Dim pulita As String = riga.Trim()
                If pulita.Length = 0 Then Continue For
                If risultato.Length > 0 Then risultato.Append(vbCrLf)
                risultato.Append("<p>")
                risultato.Append(pulita.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"))
                risultato.Append("</p>")
            Next
            Return risultato.ToString()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.RigheInHtml: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(testo, String.Empty)
        End Try
    End Function

    Friend Shared Function TestoSemplice(ByVal testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return String.Empty
            Return WebUtility.HtmlDecode(RegexMarcatore.Replace(testo, " "))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.TestoSemplice: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return If(testo, String.Empty)
        End Try
    End Function

    Private Shared Function DividiInFrasi(ByVal riga As String) As List(Of String)
        Dim frasi As New List(Of String)()
        Try
            Dim inizio As Integer = 0
            For Each corrispondenza As Match In RegexConfineFrase.Matches(riga)
                If Not ConfineValido(riga, corrispondenza.Index) Then Continue For
                Dim frase As String = riga.Substring(inizio, corrispondenza.Index - inizio).Trim()
                If frase.Length > 0 Then frasi.Add(frase)
                inizio = corrispondenza.Index + corrispondenza.Length
            Next

            If inizio < riga.Length Then
                Dim ultima As String = riga.Substring(inizio).Trim()
                If ultima.Length > 0 Then frasi.Add(ultima)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.DividiInFrasi: {ex.Message} - StackTrace: {ex.StackTrace}")
            frasi.Clear()
            frasi.Add(riga)
        End Try
        Return frasi
    End Function

    Private Shared Function ConfineValido(ByVal riga As String, ByVal posizioneSpazio As Integer) As Boolean
        Try
            Dim posizione As Integer = posizioneSpazio - 1
            While posizione >= 0 AndAlso CaratteriChiusura.IndexOf(riga(posizione)) >= 0
                posizione -= 1
            End While
            If posizione < 0 Then Return False
            If riga(posizione) <> "."c Then Return True
            If posizione > 0 AndAlso riga(posizione - 1) = "."c Then Return True

            Dim inizioParola As Integer = posizione - 1
            While inizioParola >= 0 AndAlso Not Char.IsWhiteSpace(riga(inizioParola)) AndAlso riga(inizioParola) <> "("c AndAlso riga(inizioParola) <> "«"c
                inizioParola -= 1
            End While
            Dim parola As String = riga.Substring(inizioParola + 1, posizione - inizioParola - 1)
            If parola.Length = 0 Then Return True
            If parola.Length = 1 AndAlso Char.IsLetter(parola(0)) Then Return False
            If Abbreviazioni.Contains(parola) Then Return False
            If Sigla(parola) Then Return False
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.ConfineValido: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return True
        End Try
    End Function

    Private Shared Function Sigla(ByVal parola As String) As Boolean
        Try
            If parola.IndexOf("."c) < 0 Then Return False
            Dim contieneLettere As Boolean = False
            For Each carattere As Char In parola
                If Char.IsLetter(carattere) Then
                    contieneLettere = True
                    Exit For
                End If
            Next
            If Not contieneLettere Then Return False
            For Each segmento As String In parola.Split("."c)
                If segmento.Length > LunghezzaMassimaSegmentoSigla Then Return False
            Next
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.Sigla: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Friend Shared Function SuddividiInParagrafi(ByVal testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.Length < LunghezzaMinimaSuddivisione Then Return testo
            If testo.IndexOf(ControlChars.Lf) >= 0 OrElse testo.IndexOf(ControlChars.Cr) >= 0 Then Return testo

            Dim frasi As String() = RegexFineFrase.Split(testo)
            Dim risultato As New StringBuilder()
            Dim paragrafo As New StringBuilder()

            For Each frase As String In frasi
                Dim pulita As String = frase.Trim()
                If pulita.Length = 0 Then Continue For
                If paragrafo.Length > 0 Then paragrafo.Append(" "c)
                paragrafo.Append(pulita)
                If paragrafo.Length >= LunghezzaParagrafoSuddiviso Then
                    If risultato.Length > 0 Then risultato.Append(ControlChars.Lf)
                    risultato.Append(paragrafo.ToString())
                    paragrafo.Clear()
                End If
            Next

            If paragrafo.Length > 0 Then
                If risultato.Length > 0 Then risultato.Append(ControlChars.Lf)
                risultato.Append(paragrafo.ToString())
            End If

            Return risultato.ToString()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ConvertitoreHtmlTesto.SuddividiInParagrafi: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return testo
        End Try
    End Function
End Class

Friend NotInheritable Class SelezionatoreCandidati
    Private Shared ReadOnly logger As New Logger()
    Private Const LunghezzaMinima As Integer = 150
    Private Const LunghezzaTestoCompleto As Integer = 1500
    Private Const RapportoEquivalenza As Double = 0.8
    Private Const RapportoDatiStrutturati As Double = 0.9
    Private Const RapportoLunghezzaSospetta As Double = 1.25
    Private Shared ReadOnly RegexUrl As New Regex("\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.CultureInvariant)

    Private Sub New()
    End Sub

    Public Shared Sub Aggiungi(ByVal candidati As List(Of CandidatoTesto), ByVal priorita As Integer, ByVal testo As String)
        Try
            If candidati Is Nothing OrElse String.IsNullOrWhiteSpace(testo) Then Return
            Dim pulito As String = ConvertitoreHtmlTesto.NormalizzaTesto(RegexUrl.Replace(testo, String.Empty))
            If pulito.Length < LunghezzaMinima Then Return
            candidati.Add(New CandidatoTesto With {.Priorita = priorita, .Testo = pulito})
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionatoreCandidati.Aggiungi: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function RimuoviUrl(ByVal testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return String.Empty
            Return ConvertitoreHtmlTesto.NormalizzaTesto(RegexUrl.Replace(testo, String.Empty))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionatoreCandidati.RimuoviUrl: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function CandidatoAffidabile(ByVal candidato As CandidatoTesto) As Boolean
        Try
            Return candidato.Priorita = PrioritaCandidato.LettoreMozilla OrElse candidato.Priorita = PrioritaCandidato.SelettoriNoti OrElse candidato.Priorita = PrioritaCandidato.DatiStrutturati
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionatoreCandidati.CandidatoAffidabile: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function ScegliMigliore(ByVal candidati As List(Of CandidatoTesto)) As CandidatoTesto
        Try
            If candidati Is Nothing OrElse candidati.Count = 0 Then Return Nothing

            Dim esistonoAffidabili As Boolean = False
            For Each candidato As CandidatoTesto In candidati
                If candidato IsNot Nothing AndAlso CandidatoAffidabile(candidato) Then
                    esistonoAffidabili = True
                    Exit For
                End If
            Next

            Dim lunghezzaMassima As Integer = 0
            For Each candidato As CandidatoTesto In candidati
                If candidato Is Nothing OrElse String.IsNullOrEmpty(candidato.Testo) Then Continue For
                If esistonoAffidabili AndAlso Not CandidatoAffidabile(candidato) Then Continue For
                If candidato.Testo.Length > lunghezzaMassima Then lunghezzaMassima = candidato.Testo.Length
            Next
            If lunghezzaMassima = 0 Then Return Nothing

            Dim soglia As Double = lunghezzaMassima * RapportoEquivalenza
            Dim migliore As CandidatoTesto = Nothing
            For Each candidato As CandidatoTesto In candidati
                If candidato Is Nothing OrElse String.IsNullOrEmpty(candidato.Testo) Then Continue For
                If esistonoAffidabili AndAlso Not CandidatoAffidabile(candidato) Then Continue For
                If candidato.Testo.Length < soglia Then Continue For
                If migliore Is Nothing Then
                    migliore = candidato
                ElseIf candidato.Priorita < migliore.Priorita Then
                    migliore = candidato
                ElseIf candidato.Priorita = migliore.Priorita AndAlso candidato.Testo.Length > migliore.Testo.Length Then
                    migliore = candidato
                End If
            Next

            Return migliore
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionatoreCandidati.ScegliMigliore: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Shared Function RisultatoSufficiente(ByVal candidati As List(Of CandidatoTesto)) As Boolean
        Try
            Dim migliore As CandidatoTesto = ScegliMigliore(candidati)
            If migliore Is Nothing Then Return False

            If migliore.Priorita = PrioritaCandidato.DatiStrutturati Then
                Dim lunghezzaMassimaAssoluta As Integer = 0
                For Each candidato As CandidatoTesto In candidati
                    If candidato Is Nothing OrElse String.IsNullOrEmpty(candidato.Testo) Then Continue For
                    If candidato.Testo.Length > lunghezzaMassimaAssoluta Then lunghezzaMassimaAssoluta = candidato.Testo.Length
                Next
                If lunghezzaMassimaAssoluta > migliore.Testo.Length * RapportoLunghezzaSospetta Then Return False
            End If

            Dim lunghezzaDatiStrutturati As Integer = 0
            For Each candidato As CandidatoTesto In candidati
                If candidato Is Nothing OrElse String.IsNullOrEmpty(candidato.Testo) Then Continue For
                If candidato.Priorita = PrioritaCandidato.DatiStrutturati AndAlso candidato.Testo.Length > lunghezzaDatiStrutturati Then
                    lunghezzaDatiStrutturati = candidato.Testo.Length
                End If
            Next

            If lunghezzaDatiStrutturati > 0 Then
                Return migliore.Testo.Length >= lunghezzaDatiStrutturati * RapportoDatiStrutturati
            End If

            If Not CandidatoAffidabile(migliore) Then Return False
            Return migliore.Testo.Length >= LunghezzaTestoCompleto
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SelezionatoreCandidati.RisultatoSufficiente: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function
End Class

Friend NotInheritable Class AnalizzatorePaginaArticolo
    Private Shared ReadOnly logger As New Logger()
    Private Const LunghezzaMinimaSelettore As Integer = 400
    Private Const LunghezzaMinimaParagrafo As Integer = 40
    Private Const PunteggioMinimoDensita As Double = 400
    Private Const QuotaMassimaLink As Double = 0.5
    Private Const NodiMassimiJson As Integer = 20000

    Private Shared ReadOnly SelettoriCorpoArticolo As String() = {
        "[itemprop='articleBody']", ".entry-content", ".post-content", ".article-body", ".articleBody",
        ".article__body", ".article-content", ".article__content", ".article-text", ".story__text",
        ".story-content", ".story-body", ".td-post-content", ".single-post-content", ".news-content",
        ".testo-articolo", ".testoArticolo", ".articolo-testo", ".content-article", ".post-body",
        ".entry-body", ".elementor-widget-theme-post-content", "#article-body", "#articleBody",
        "#articolo", "#testo", ".atext", ".news-txt", ".txt-articolo"
    }

    Private Sub New()
    End Sub

    Public Shared Function Analizza(ByVal url As String, ByVal html As String, ByVal candidati As List(Of CandidatoTesto)) As String
        Dim urlAmp As String = Nothing
        Try
            If String.IsNullOrWhiteSpace(html) OrElse candidati Is Nothing Then Return Nothing

            AggiungiCandidatoLettoreMozilla(url, html, candidati)

            Dim analizzatore As New AngleHtml.HtmlParser()
            Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument(html)
                If documento Is Nothing Then Return Nothing
                urlAmp = TrovaUrlAmp(documento, url)
                AggiungiCandidatiDatiStrutturati(documento, url, candidati)
                ConvertitoreHtmlTesto.RimuoviRumore(documento.DocumentElement, True)
                AggiungiCandidatoSelettori(documento, candidati)
                AggiungiCandidatoDensita(documento, candidati)
            End Using

            Return urlAmp
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.Analizza per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return urlAmp
        End Try
    End Function

    Private Shared Sub AggiungiCandidatoLettoreMozilla(ByVal url As String, ByVal html As String, ByVal candidati As List(Of CandidatoTesto))
        Try
            Dim lettore As New SmartReader.Reader(url, html)
            lettore.ContinueIfNotReadable = True

            Dim articolo As SmartReader.Article = lettore.GetArticle()
            If articolo Is Nothing OrElse String.IsNullOrWhiteSpace(articolo.Content) Then Return

            Dim testo As String = ConvertitoreHtmlTesto.ConvertiFrammento(articolo.Content)
            Dim priorita As Integer = PrioritaCandidato.LettoreIncerto
            If articolo.IsReadable Then priorita = PrioritaCandidato.LettoreMozilla

            SelezionatoreCandidati.Aggiungi(candidati, priorita, testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.AggiungiCandidatoLettoreMozilla per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function TrovaUrlAmp(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal url As String) As String
        Try
            Dim collegamento As AngleDom.IElement = documento.QuerySelector("link[rel~='amphtml']")
            If collegamento Is Nothing Then Return Nothing

            Dim href As String = collegamento.GetAttribute("href")
            If String.IsNullOrWhiteSpace(href) Then Return Nothing

            Dim uriBase As Uri = Nothing
            If Not ScaricatorePagineWeb.ConvalidaUrl(url, uriBase) Then Return Nothing

            Dim uriAmp As Uri = Nothing
            If Not Uri.TryCreate(uriBase, href.Trim(), uriAmp) Then Return Nothing

            Dim uriVerificato As Uri = Nothing
            If Not ScaricatorePagineWeb.ConvalidaUrl(uriAmp.AbsoluteUri, uriVerificato) Then Return Nothing
            If Uri.Compare(uriVerificato, uriBase, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) = 0 Then Return Nothing

            Return uriVerificato.AbsoluteUri
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.TrovaUrlAmp per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Sub AggiungiCandidatiDatiStrutturati(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal url As String, ByVal candidati As List(Of CandidatoTesto))
        Try
            For Each script As AngleDom.IElement In documento.QuerySelectorAll("script[type*='ld+json']")
                Dim radice As JToken = InterpretaJson(script.TextContent, url)
                If radice Is Nothing Then Continue For
                RaccogliCorpiArticolo(radice, url, candidati)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.AggiungiCandidatiDatiStrutturati per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function InterpretaJson(ByVal contenuto As String, ByVal url As String) As JToken
        Try
            If String.IsNullOrWhiteSpace(contenuto) Then Return Nothing
            Dim pulito As String = contenuto.Trim()
            pulito = pulito.Replace("/*<![CDATA[*/", String.Empty).Replace("/*]]>*/", String.Empty)
            pulito = pulito.Replace("<![CDATA[", String.Empty).Replace("]]>", String.Empty)
            pulito = pulito.Replace("<!--", String.Empty).Replace("-->", String.Empty).Trim()
            If pulito.Length = 0 Then Return Nothing
            If Not pulito.StartsWith("{", StringComparison.Ordinal) AndAlso Not pulito.StartsWith("[", StringComparison.Ordinal) Then Return Nothing
            Return JToken.Parse(pulito)
        Catch ex As JsonException
            logger.ScriviLog($"Dati strutturati JSON-LD non validi in {url}: {ex.Message}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.InterpretaJson per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Sub RaccogliCorpiArticolo(ByVal radice As JToken, ByVal url As String, ByVal candidati As List(Of CandidatoTesto))
        Try
            Dim pila As New Stack(Of JToken)()
            pila.Push(radice)
            Dim visitati As Integer = 0

            While pila.Count > 0 AndAlso visitati < NodiMassimiJson
                Dim token As JToken = pila.Pop()
                visitati += 1
                If token Is Nothing Then Continue While

                If token.Type = JTokenType.Object Then
                    Dim oggetto As JObject = CType(token, JObject)
                    Dim corpo As JToken = oggetto("articleBody")
                    If corpo IsNot Nothing AndAlso corpo.Type = JTokenType.String Then
                        AggiungiCorpoDatiStrutturati(corpo.ToString(), url, candidati)
                    End If
                    For Each proprieta As JProperty In oggetto.Properties()
                        If proprieta.Value Is Nothing Then Continue For
                        If proprieta.Value.Type = JTokenType.Object OrElse proprieta.Value.Type = JTokenType.Array Then
                            pila.Push(proprieta.Value)
                        End If
                    Next
                ElseIf token.Type = JTokenType.Array Then
                    For Each figlio As JToken In token.Children()
                        If figlio IsNot Nothing Then pila.Push(figlio)
                    Next
                End If
            End While
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.RaccogliCorpiArticolo per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AggiungiCorpoDatiStrutturati(ByVal corpo As String, ByVal url As String, ByVal candidati As List(Of CandidatoTesto))
        Try
            If String.IsNullOrWhiteSpace(corpo) Then Return
            Dim testo As String
            If corpo.IndexOf("<"c) >= 0 AndAlso corpo.IndexOf(">"c) > 0 Then
                testo = ConvertitoreHtmlTesto.ConvertiFrammento(corpo)
            Else
                testo = WebUtility.HtmlDecode(corpo)
            End If
            testo = ConvertitoreHtmlTesto.SuddividiInParagrafi(testo)
            SelezionatoreCandidati.Aggiungi(candidati, PrioritaCandidato.DatiStrutturati, testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.AggiungiCorpoDatiStrutturati per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AggiungiCandidatoSelettori(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal candidati As List(Of CandidatoTesto))
        Try
            For Each selettore As String In SelettoriCorpoArticolo
                Dim testo As String = TestoDaSelettore(documento, selettore)
                If String.IsNullOrEmpty(testo) Then Continue For
                SelezionatoreCandidati.Aggiungi(candidati, PrioritaCandidato.SelettoriNoti, testo)
                Return
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.AggiungiCandidatoSelettori: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function TestoDaSelettore(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal selettore As String) As String
        Try
            Dim elementiValidi As New List(Of AngleDom.IElement)()
            For Each elemento As AngleDom.IElement In documento.QuerySelectorAll(selettore)
                Dim lunghezza As Integer = ConvertitoreHtmlTesto.LunghezzaTestoParagrafi(elemento)
                If lunghezza < LunghezzaMinimaSelettore Then Continue For
                If elementiValidi.Any(Function(e) Not Object.ReferenceEquals(e, elemento) AndAlso e.Contains(elemento)) Then Continue For
                elementiValidi.Add(elemento)
            Next

            If elementiValidi.Count = 0 Then Return Nothing

            Dim pezzi As New List(Of String)()
            For Each elemento As AngleDom.IElement In elementiValidi
                Dim testoBlocco As String = ConvertitoreHtmlTesto.TestoDaNodo(elemento)
                If Not String.IsNullOrWhiteSpace(testoBlocco) Then pezzi.Add(testoBlocco)
            Next
            If pezzi.Count = 0 Then Return Nothing
            Return String.Join(ControlChars.Lf, pezzi)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.TestoDaSelettore ({selettore}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Sub AggiungiCandidatoDensita(ByVal documento As AngleHtmlDom.IHtmlDocument, ByVal candidati As List(Of CandidatoTesto))
        Try
            Dim corpo As AngleDom.IElement = documento.Body
            If corpo Is Nothing Then Return

            Dim punteggi As New Dictionary(Of AngleDom.IElement, Double)()
            For Each paragrafo As AngleDom.IElement In corpo.QuerySelectorAll("p")
                Dim testoParagrafo As String = paragrafo.TextContent
                If String.IsNullOrEmpty(testoParagrafo) Then Continue For

                Dim lunghezza As Integer = testoParagrafo.Trim().Length
                If lunghezza < LunghezzaMinimaParagrafo Then Continue For
                If ConvertitoreHtmlTesto.LunghezzaTestoLink(paragrafo) > lunghezza * QuotaMassimaLink Then Continue For

                Dim genitore As AngleDom.IElement = paragrafo.ParentElement
                If genitore Is Nothing Then Continue For
                IncrementaPunteggio(punteggi, genitore, lunghezza)

                Dim nonno As AngleDom.IElement = genitore.ParentElement
                If nonno IsNot Nothing Then IncrementaPunteggio(punteggi, nonno, lunghezza * 0.5)
            Next

            Dim migliore As AngleDom.IElement = Nothing
            Dim punteggioMigliore As Double = 0
            For Each coppia As KeyValuePair(Of AngleDom.IElement, Double) In punteggi
                If coppia.Value > punteggioMigliore Then
                    punteggioMigliore = coppia.Value
                    migliore = coppia.Key
                End If
            Next

            If migliore Is Nothing OrElse punteggioMigliore < PunteggioMinimoDensita Then Return
            SelezionatoreCandidati.Aggiungi(candidati, PrioritaCandidato.DensitaParagrafi, ConvertitoreHtmlTesto.TestoDaNodo(migliore))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.AggiungiCandidatoDensita: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub IncrementaPunteggio(ByVal punteggi As Dictionary(Of AngleDom.IElement, Double), ByVal elemento As AngleDom.IElement, ByVal valore As Double)
        Try
            Dim attuale As Double = 0
            If punteggi.TryGetValue(elemento, attuale) Then
                punteggi(elemento) = attuale + valore
            Else
                punteggi.Add(elemento, valore)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AnalizzatorePaginaArticolo.IncrementaPunteggio: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub
End Class

Friend NotInheritable Class RilevatoreArticoliAPagamento
    Private Shared ReadOnly logger As New Logger()
    Private Const NodiMassimiJson As Integer = 20000
    Private Const ProprietaGratuito As String = "isAccessibleForFree"
    Private Const ProprietaLivello As String = "article:content_tier"
    Private Const LivelloBloccato As String = "locked"
    Private Const ProprietaAnaliticaPagamento As String = "k5a:paid"

    Private Shared ReadOnly ValoriNonGratuiti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "false", "no", "0"
    }

    Private Sub New()
    End Sub

    Public Shared Function APagamento(ByVal html As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(html) Then Return False

            Dim segnaleDati As Boolean = html.IndexOf(ProprietaGratuito, StringComparison.OrdinalIgnoreCase) >= 0
            Dim segnaleLivello As Boolean = html.IndexOf("content_tier", StringComparison.OrdinalIgnoreCase) >= 0
            If Not segnaleDati AndAlso Not segnaleLivello Then Return False

            Dim analizzatore As New AngleHtml.HtmlParser()
            Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument(html)
                If documento Is Nothing Then Return False
                If MetaDichiaraGratuito(documento) Then Return False
                If MetaBloccati(documento) Then Return True
                If segnaleDati AndAlso DatiStrutturatiNonGratuiti(documento) Then Return True
            End Using
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.APagamento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function MetaDichiaraGratuito(ByVal documento As AngleHtmlDom.IHtmlDocument) As Boolean
        Try
            For Each meta As AngleDom.IElement In documento.QuerySelectorAll("meta")
                Dim nome As String = meta.GetAttribute("name")
                If String.IsNullOrEmpty(nome) Then nome = meta.GetAttribute("property")
                If Not String.Equals(nome, ProprietaAnaliticaPagamento, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim contenuto As String = meta.GetAttribute("content")
                If String.IsNullOrWhiteSpace(contenuto) Then Return False
                Return String.Equals(contenuto.Trim(), "0", StringComparison.Ordinal)
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.MetaDichiaraGratuito: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function MetaBloccati(ByVal documento As AngleHtmlDom.IHtmlDocument) As Boolean
        Try
            For Each meta As AngleDom.IElement In documento.QuerySelectorAll("meta")
                Dim contenuto As String = meta.GetAttribute("content")
                If String.IsNullOrWhiteSpace(contenuto) Then Continue For
                contenuto = contenuto.Trim()

                Dim proprieta As String = meta.GetAttribute("property")
                If String.IsNullOrEmpty(proprieta) Then proprieta = meta.GetAttribute("name")
                If String.Equals(proprieta, ProprietaLivello, StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(contenuto, LivelloBloccato, StringComparison.OrdinalIgnoreCase) Then Return True

                Dim proprietaMicrodati As String = meta.GetAttribute("itemprop")
                If String.Equals(proprietaMicrodati, ProprietaGratuito, StringComparison.OrdinalIgnoreCase) AndAlso ValoriNonGratuiti.Contains(contenuto) Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.MetaBloccati: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function DatiStrutturatiNonGratuiti(ByVal documento As AngleHtmlDom.IHtmlDocument) As Boolean
        Try
            For Each script As AngleDom.IElement In documento.QuerySelectorAll("script[type*='ld+json']")
                Dim contenuto As String = script.TextContent
                If String.IsNullOrWhiteSpace(contenuto) Then Continue For
                If contenuto.IndexOf(ProprietaGratuito, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                Dim radice As JToken = InterpretaJson(contenuto)
                If radice Is Nothing Then Continue For
                If ContieneNonGratuito(radice) Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.DatiStrutturatiNonGratuiti: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function InterpretaJson(ByVal contenuto As String) As JToken
        Try
            Dim pulito As String = contenuto.Trim()
            pulito = pulito.Replace("/*<![CDATA[*/", String.Empty).Replace("/*]]>*/", String.Empty)
            pulito = pulito.Replace("<![CDATA[", String.Empty).Replace("]]>", String.Empty)
            pulito = pulito.Replace("<!--", String.Empty).Replace("-->", String.Empty).Trim()
            If pulito.Length = 0 Then Return Nothing
            If Not pulito.StartsWith("{", StringComparison.Ordinal) AndAlso Not pulito.StartsWith("[", StringComparison.Ordinal) Then Return Nothing
            Return JToken.Parse(pulito)
        Catch ex As JsonException
            logger.ScriviLog($"Dati strutturati JSON-LD non validi nel controllo dell'accesso a pagamento: {ex.Message}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.InterpretaJson: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ContieneNonGratuito(ByVal radice As JToken) As Boolean
        Try
            Dim pila As New Stack(Of JToken)()
            pila.Push(radice)
            Dim visitati As Integer = 0

            While pila.Count > 0 AndAlso visitati < NodiMassimiJson
                Dim token As JToken = pila.Pop()
                visitati += 1
                If token Is Nothing Then Continue While

                If token.Type = JTokenType.Object Then
                    Dim oggetto As JObject = CType(token, JObject)
                    Dim valore As JToken = oggetto.GetValue(ProprietaGratuito, StringComparison.OrdinalIgnoreCase)
                    If ValoreNonGratuito(valore) Then Return True
                    For Each proprieta As JProperty In oggetto.Properties()
                        If proprieta.Value Is Nothing Then Continue For
                        If proprieta.Value.Type = JTokenType.Object OrElse proprieta.Value.Type = JTokenType.Array Then
                            pila.Push(proprieta.Value)
                        End If
                    Next
                ElseIf token.Type = JTokenType.Array Then
                    For Each figlio As JToken In token.Children()
                        If figlio IsNot Nothing Then pila.Push(figlio)
                    Next
                End If
            End While
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.ContieneNonGratuito: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function ValoreNonGratuito(ByVal valore As JToken) As Boolean
        Try
            If valore Is Nothing Then Return False
            Select Case valore.Type
                Case JTokenType.Boolean
                    Return Not valore.Value(Of Boolean)()
                Case JTokenType.Integer
                    Return valore.Value(Of Long)() = 0
                Case JTokenType.String
                    Dim testo As String = valore.Value(Of String)()
                    If String.IsNullOrWhiteSpace(testo) Then Return False
                    Return ValoriNonGratuiti.Contains(testo.Trim())
                Case Else
                    Return False
            End Select
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RilevatoreArticoliAPagamento.ValoreNonGratuito: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function
End Class

Friend Enum EsitoScambioTrafilatura
    Riuscito
    ErrorePagina
    ErroreMotore
End Enum

Friend Class EccezionePaginaTrafilatura
    Inherits Exception

    Public Sub New(ByVal messaggio As String)
        MyBase.New(messaggio)
    End Sub
End Class

Friend NotInheritable Class EstrattoreTrafilatura
    Private Shared ReadOnly logger As New Logger()
    Private Shared ReadOnly lockProcesso As New Object()
    Private Shared processo As Process = Nothing
    Private Shared contatore As Long = 0
    Private Shared erroriConsecutivi As Integer = 0
    Private Shared sospesoFino As DateTime = DateTime.MinValue
    Private Shared ultimoUso As DateTime = DateTime.MinValue
    Private Shared timerInattivita As System.Threading.Timer = Nothing
    Private Shared gestoreUscitaRegistrato As Boolean = False
    Private Const TimeoutAvvioMs As Integer = 60000
    Private Const TimeoutEstrazioneMs As Integer = 30000
    Private Const ErroriPrimaDellaSospensione As Integer = 3
    Private Const MinutiSospensione As Integer = 10
    Private Const MinutiInattivita As Integer = 10
    Private Const IntervalloControlloMs As Integer = 60000
    Private Const AttesaChiusuraMs As Integer = 2000
    Private Const DimensioneMassimaHtml As Integer = 8 * 1024 * 1024
    Private Shared ReadOnly ImpostazioniJson As New JsonSerializerSettings With {
        .StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
        .Formatting = Formatting.None
    }

    Private Sub New()
    End Sub

    Public Shared Function Disponibile() As Boolean
        Try
            Return InstallatoreTrafilatura.Installato()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.Disponibile: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Friend Shared Sub Ferma()
        Try
            SyncLock lockProcesso
                FermaProcesso()
                erroriConsecutivi = 0
                sospesoFino = DateTime.MinValue
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.Ferma: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function Estrai(ByVal url As String, ByVal html As String) As String
        Try
            Dim risposta As JObject = Richiedi(url, html, "testo")
            If risposta Is Nothing Then Return String.Empty
            Dim nodoTesto As JToken = risposta("testo")
            If nodoTesto Is Nothing OrElse nodoTesto.Type <> JTokenType.String Then Return String.Empty
            Return If(nodoTesto.Value(Of String)(), String.Empty)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.Estrai per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Function EstraiCompleto(ByVal url As String, ByVal html As String, Optional ByVal conStruttura As Boolean = True) As JObject
        Try
            Return Richiedi(url, html, If(conStruttura, "completo", "testo"))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.EstraiCompleto per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function Richiedi(ByVal url As String, ByVal html As String, ByVal modo As String) As JObject
        Try
            If String.IsNullOrWhiteSpace(html) OrElse html.Length > DimensioneMassimaHtml Then Return Nothing
            If Not Disponibile() Then Return Nothing

            SyncLock lockProcesso
                If DateTime.Now < sospesoFino Then Return Nothing
                ultimoUso = DateTime.Now

                If Not AssicuraProcesso() Then
                    RegistraErroreMotore()
                    Return Nothing
                End If

                Dim risposta As JObject = Nothing
                Dim esito As EsitoScambioTrafilatura = ScambiaRichiesta(url, html, modo, risposta)
                ultimoUso = DateTime.Now

                Select Case esito
                    Case EsitoScambioTrafilatura.Riuscito
                        erroriConsecutivi = 0
                        Return risposta
                    Case EsitoScambioTrafilatura.ErrorePagina
                        erroriConsecutivi = 0
                        Return Nothing
                    Case Else
                        FermaProcesso()
                        RegistraErroreMotore()
                        Return Nothing
                End Select
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.Richiedi per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Sub AggiornaScript(ByVal cartella As String)
        Try
            Dim percorso As String = Path.Combine(cartella, "estrai.py")
            Dim attuale As String = String.Empty
            If File.Exists(percorso) Then attuale = File.ReadAllText(percorso, Encoding.UTF8)
            If String.Equals(attuale, InstallatoreTrafilatura.ScriptEstrazione, StringComparison.Ordinal) Then Return
            File.WriteAllText(percorso, InstallatoreTrafilatura.ScriptEstrazione, New UTF8Encoding(False))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.AggiornaScript: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub RegistraErroreMotore()
        Try
            erroriConsecutivi += 1
            If erroriConsecutivi >= ErroriPrimaDellaSospensione Then
                sospesoFino = DateTime.Now.AddMinutes(MinutiSospensione)
                erroriConsecutivi = 0
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.RegistraErroreMotore: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function AssicuraProcesso() As Boolean
        Try
            If processo IsNot Nothing AndAlso Not processo.HasExited Then Return True
            FermaProcesso()

            Dim cartella As String = InstallatoreTrafilatura.CartellaMotore()
            AggiornaScript(cartella)
            Dim avvio As New ProcessStartInfo()
            avvio.FileName = Path.Combine(cartella, "python.exe")
            avvio.Arguments = "-X utf8 -u -I """ & Path.Combine(cartella, "estrai.py") & """"
            avvio.WorkingDirectory = cartella
            avvio.UseShellExecute = False
            avvio.CreateNoWindow = True
            avvio.RedirectStandardInput = True
            avvio.RedirectStandardOutput = True
            avvio.RedirectStandardError = True
            avvio.StandardOutputEncoding = New UTF8Encoding(False)
            avvio.StandardErrorEncoding = New UTF8Encoding(False)

            Dim nuovo As New Process()
            nuovo.StartInfo = avvio
            AddHandler nuovo.ErrorDataReceived, AddressOf ScartaErrori
            If Not nuovo.Start() Then
                nuovo.Dispose()
                Throw New InvalidOperationException("avvio del motore trafilatura non riuscito")
            End If
            nuovo.BeginErrorReadLine()
            processo = nuovo

            Dim prima As String = LeggiRiga(TimeoutAvvioMs)
            If prima Is Nothing Then Throw New TimeoutException($"il motore trafilatura non ha risposto entro {TimeoutAvvioMs \ 1000} secondi dall'avvio")

            Dim segnale As JObject = JObject.Parse(prima)
            Dim nodoPronto As JToken = segnale("pronto")
            If nodoPronto Is Nothing OrElse nodoPronto.Type <> JTokenType.Boolean OrElse Not nodoPronto.Value(Of Boolean)() Then
                Dim nodoErrore As JToken = segnale("errore")
                Dim descrizione As String = prima
                If nodoErrore IsNot Nothing Then descrizione = nodoErrore.ToString()
                Throw New InvalidOperationException("motore trafilatura non pronto: " & descrizione)
            End If

            RegistraChiusuraEInattivita()
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.AssicuraProcesso: {ex.Message} - StackTrace: {ex.StackTrace}")
            FermaProcesso()
            Return False
        End Try
    End Function

    Private Shared Function LeggiRiga(ByVal timeoutMs As Integer) As String
        Try
            If processo Is Nothing Then Return Nothing
            Dim lettura As Task(Of String) = processo.StandardOutput.ReadLineAsync()
            If Not lettura.Wait(timeoutMs) Then Return Nothing
            Return lettura.Result
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.LeggiRiga: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function ScambiaRichiesta(ByVal url As String, ByVal html As String, ByVal modo As String, ByRef risultato As JObject) As EsitoScambioTrafilatura
        risultato = Nothing
        Try
            If processo Is Nothing OrElse processo.HasExited Then Throw New InvalidOperationException("motore trafilatura non attivo")

            contatore += 1
            Dim identificativo As Long = contatore
            Dim richiesta As New Dictionary(Of String, Object)()
            richiesta.Add("id", identificativo)
            richiesta.Add("url", If(url, String.Empty))
            richiesta.Add("html", html)
            richiesta.Add("modo", If(String.IsNullOrEmpty(modo), "testo", modo))

            processo.StandardInput.WriteLine(JsonConvert.SerializeObject(richiesta, ImpostazioniJson))
            processo.StandardInput.Flush()

            Dim riga As String = LeggiRiga(TimeoutEstrazioneMs)
            If riga Is Nothing Then Throw New TimeoutException($"il motore trafilatura non ha risposto entro {TimeoutEstrazioneMs \ 1000} secondi per {url}")

            Dim risposta As JObject = JObject.Parse(riga)
            Dim nodoId As JToken = risposta("id")
            If nodoId Is Nothing OrElse nodoId.Type <> JTokenType.Integer OrElse nodoId.Value(Of Long)() <> identificativo Then
                Throw New InvalidOperationException($"risposta del motore trafilatura fuori sequenza per {url}")
            End If

            Dim nodoEsito As JToken = risposta("ok")
            If nodoEsito Is Nothing OrElse nodoEsito.Type <> JTokenType.Boolean OrElse Not nodoEsito.Value(Of Boolean)() Then
                Dim nodoErrore As JToken = risposta("errore")
                Dim descrizione As String = "errore sconosciuto"
                If nodoErrore IsNot Nothing Then descrizione = nodoErrore.ToString()
                Throw New EccezionePaginaTrafilatura($"Trafilatura non ha estratto il testo di {url}: {descrizione}")
            End If

            risultato = risposta
            Return EsitoScambioTrafilatura.Riuscito
        Catch ex As EccezionePaginaTrafilatura
            logger.ScriviLog(ex.Message)
            Return EsitoScambioTrafilatura.ErrorePagina
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.ScambiaRichiesta per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return EsitoScambioTrafilatura.ErroreMotore
        End Try
    End Function

    Private Shared Sub ScartaErrori(ByVal sender As Object, ByVal e As DataReceivedEventArgs)
        Try
            If e Is Nothing Then Return
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.ScartaErrori: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub RegistraChiusuraEInattivita()
        Try
            If Not gestoreUscitaRegistrato Then
                AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf AllaChiusura
                gestoreUscitaRegistrato = True
            End If
            If timerInattivita Is Nothing Then
                timerInattivita = New System.Threading.Timer(AddressOf ControllaInattivita, Nothing, IntervalloControlloMs, IntervalloControlloMs)
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.RegistraChiusuraEInattivita: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub ControllaInattivita(ByVal stato As Object)
        Try
            If Not Monitor.TryEnter(lockProcesso) Then Return
            Try
                If processo Is Nothing Then Return
                If (DateTime.Now - ultimoUso).TotalMinutes >= MinutiInattivita Then FermaProcesso()
            Finally
                Monitor.Exit(lockProcesso)
            End Try
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.ControllaInattivita: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub AllaChiusura(ByVal sender As Object, ByVal e As EventArgs)
        Try
            If timerInattivita IsNot Nothing Then
                timerInattivita.Dispose()
                timerInattivita = Nothing
            End If
            If Monitor.TryEnter(lockProcesso, AttesaChiusuraMs) Then
                Try
                    FermaProcesso()
                Finally
                    Monitor.Exit(lockProcesso)
                End Try
            Else
                FermaProcesso()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.AllaChiusura: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub FermaProcesso()
        Dim vecchio As Process = Nothing
        Try
            vecchio = processo
            processo = Nothing
            If vecchio Is Nothing Then Return
            RemoveHandler vecchio.ErrorDataReceived, AddressOf ScartaErrori
            If Not vecchio.HasExited Then
                Try
                    vecchio.StandardInput.Close()
                Catch exChiusura As Exception
                    logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.FermaProcesso chiudendo l'ingresso: {exChiusura.Message}")
                End Try
                If Not vecchio.WaitForExit(AttesaChiusuraMs) Then vecchio.Kill()
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.FermaProcesso: {ex.Message} - StackTrace: {ex.StackTrace}")
        Finally
            Try
                If vecchio IsNot Nothing Then vecchio.Dispose()
            Catch exRilascio As Exception
                logger.ScriviLog($"Eccezione in EstrattoreTrafilatura.FermaProcesso rilasciando il processo: {exRilascio.Message}")
            End Try
        End Try
    End Sub
End Class

Public Class EstrattoreTestoPulito
    Private Shared ReadOnly logger As New Logger()
    Private Const LunghezzaMinimaTrafilatura As Integer = 200
    Private Const LunghezzaMinimaTestoLibero As Integer = 1500

    Public Shared Function PulisciHtml(ByVal html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty
            Return ConvertitoreHtmlTesto.RigheInHtml(ConvertitoreHtmlTesto.UnaFrasePerRiga(SelezionatoreCandidati.RimuoviUrl(ConvertitoreHtmlTesto.ConvertiFrammento(html))))
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in PulisciHtml: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Function EstraiTestoDaUrl(ByVal url As String) As String
        Try
            Dim aPagamento As Boolean = False
            Return EstraiTestoDaUrl(url, aPagamento)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstraiTestoDaUrl per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Function EstraiTestoDaUrl(ByVal url As String, ByRef aPagamento As Boolean) As String
        aPagamento = False
        Try
            Dim uriArticolo As Uri = Nothing
            If Not ScaricatorePagineWeb.ConvalidaUrl(url, uriArticolo) Then Return String.Empty

            If GiaSegnatoAPagamento(url, uriArticolo) Then
                aPagamento = True
                Return String.Empty
            End If

            Dim htmlRenderizzato As String = Nothing
            If InstallatoreTrafilatura.MotoreTrafilaturaAttivo() Then
                Dim pagamentoRilevato As Boolean = False
                Dim testoTrafilatura As String = EstraiConTrafilatura(uriArticolo.AbsoluteUri, htmlRenderizzato, pagamentoRilevato)
                If pagamentoRilevato Then
                    RegistraAPagamento(url, uriArticolo)
                    aPagamento = True
                    Return String.Empty
                End If
                If Not String.IsNullOrWhiteSpace(testoTrafilatura) Then Return ConvertitoreHtmlTesto.RigheInHtml(testoTrafilatura)
            End If

            Dim candidati As New List(Of CandidatoTesto)()
            Dim urlAmp As String = Nothing
            Dim segnalePagamento As Boolean = False

            Dim pagina As RisultatoDownloadPagina = ScaricatorePagineWeb.Scarica(uriArticolo.AbsoluteUri)
            If pagina IsNot Nothing Then
                If RilevatoreArticoliAPagamento.APagamento(pagina.Html) Then segnalePagamento = True
                urlAmp = AnalizzatorePaginaArticolo.Analizza(pagina.UrlFinale, pagina.Html, candidati)
            End If

            If Not SelezionatoreCandidati.RisultatoSufficiente(candidati) AndAlso Not String.IsNullOrEmpty(urlAmp) Then
                Dim paginaAmp As RisultatoDownloadPagina = ScaricatorePagineWeb.Scarica(urlAmp)
                If paginaAmp IsNot Nothing Then
                    If RilevatoreArticoliAPagamento.APagamento(paginaAmp.Html) Then segnalePagamento = True
                    AnalizzatorePaginaArticolo.Analizza(paginaAmp.UrlFinale, paginaAmp.Html, candidati)
                End If
            End If

            If Not SelezionatoreCandidati.RisultatoSufficiente(candidati) Then
                If htmlRenderizzato Is Nothing Then htmlRenderizzato = OttieniHtmlRenderizzato(uriArticolo.AbsoluteUri)
                If Not String.IsNullOrWhiteSpace(htmlRenderizzato) Then
                    If RilevatoreArticoliAPagamento.APagamento(htmlRenderizzato) Then segnalePagamento = True
                    AnalizzatorePaginaArticolo.Analizza(uriArticolo.AbsoluteUri, htmlRenderizzato, candidati)
                End If
            End If

            Dim migliore As CandidatoTesto = SelezionatoreCandidati.ScegliMigliore(candidati)
            Dim testo As String = String.Empty
            If migliore IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(migliore.Testo) Then
                Dim completo As String = migliore.Testo
                If pagina IsNot Nothing AndAlso Not TestoInsufficiente(completo) Then
                    Dim seguito As String = EstrattoreArticoli.TestoPagineSuccessive(pagina.UrlFinale, pagina.Html, False, completo)
                    If seguito <> String.Empty Then completo &= ControlChars.Lf & seguito
                End If
                testo = ConvertitoreHtmlTesto.UnaFrasePerRiga(completo)
            End If

            If segnalePagamento AndAlso TestoInsufficiente(testo) Then
                RegistraAPagamento(url, uriArticolo)
                aPagamento = True
                Return String.Empty
            End If
            Return ConvertitoreHtmlTesto.RigheInHtml(testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstraiTestoDaUrl per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function TestoInsufficiente(ByVal testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return True
            Return testo.Trim().Length < LunghezzaMinimaTestoLibero
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTestoPulito.TestoInsufficiente: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return True
        End Try
    End Function

    Public Shared Function PaginaAPagamento(ByVal url As String) As Boolean
        Try
            Dim uriArticolo As Uri = Nothing
            If Not ScaricatorePagineWeb.ConvalidaUrl(url, uriArticolo) Then Return False
            If GiaSegnatoAPagamento(url, uriArticolo) Then Return True

            Dim pagina As RisultatoDownloadPagina = ScaricatorePagineWeb.Scarica(uriArticolo.AbsoluteUri)
            If pagina Is Nothing Then Return False
            If Not RilevatoreArticoliAPagamento.APagamento(pagina.Html) Then Return False

            Dim candidati As New List(Of CandidatoTesto)()
            AnalizzatorePaginaArticolo.Analizza(pagina.UrlFinale, pagina.Html, candidati)
            Dim migliore As CandidatoTesto = SelezionatoreCandidati.ScegliMigliore(candidati)
            If migliore IsNot Nothing AndAlso Not TestoInsufficiente(migliore.Testo) Then Return False

            RegistraAPagamento(url, uriArticolo)
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTestoPulito.PaginaAPagamento per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function GiaSegnatoAPagamento(ByVal url As String, ByVal uriArticolo As Uri) As Boolean
        Try
            If GestoreArchivioNotizieQuotidiani.ArticoloAPagamento(url) Then Return True
            If uriArticolo Is Nothing Then Return False
            If String.Equals(url, uriArticolo.AbsoluteUri, StringComparison.Ordinal) Then Return False
            Return GestoreArchivioNotizieQuotidiani.ArticoloAPagamento(uriArticolo.AbsoluteUri)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTestoPulito.GiaSegnatoAPagamento per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Sub RegistraAPagamento(ByVal url As String, ByVal uriArticolo As Uri)
        Try
            GestoreArchivioNotizieQuotidiani.SegnaArticoloAPagamento(url)
            If uriArticolo Is Nothing Then Return
            If String.Equals(url, uriArticolo.AbsoluteUri, StringComparison.Ordinal) Then Return
            GestoreArchivioNotizieQuotidiani.SegnaArticoloAPagamento(uriArticolo.AbsoluteUri)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTestoPulito.RegistraAPagamento per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function EstraiConTrafilatura(ByVal url As String, ByRef htmlRenderizzato As String, ByRef aPagamento As Boolean) As String
        aPagamento = False
        Try
            htmlRenderizzato = OttieniHtmlRenderizzato(url)
            If htmlRenderizzato Is Nothing Then htmlRenderizzato = String.Empty

            Dim html As String = htmlRenderizzato
            Dim indirizzo As String = url
            If String.IsNullOrWhiteSpace(html) Then
                Dim pagina As RisultatoDownloadPagina = ScaricatorePagineWeb.Scarica(url)
                If pagina IsNot Nothing Then
                    html = pagina.Html
                    indirizzo = pagina.UrlFinale
                End If
            End If
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim articolo As ArticoloEstratto = EstrattoreArticoli.EstraiDaHtml(indirizzo, html, True, False)
            Dim testo As String = If(articolo IsNot Nothing, If(articolo.Testo, String.Empty), String.Empty)
            If RilevatoreArticoliAPagamento.APagamento(html) AndAlso TestoInsufficiente(testo) Then
                aPagamento = True
                Return String.Empty
            End If
            If String.IsNullOrWhiteSpace(testo) OrElse testo.Length < LunghezzaMinimaTrafilatura Then Return String.Empty
            Dim seguito As String = EstrattoreArticoli.TestoPagineSuccessive(indirizzo, html, True, testo)
            If seguito <> String.Empty Then testo &= ControlChars.Lf & seguito
            Return ConvertitoreHtmlTesto.UnaFrasePerRiga(testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstrattoreTestoPulito.EstraiConTrafilatura per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function OttieniHtmlRenderizzato(ByVal url As String) As String
        Try
            Dim webView As WGWebView = WGWebView.GetInstance()
            If webView Is Nothing OrElse Not webView.Disponibile Then Return Nothing
            Return webView.OttieniHtmlRenderizzato(url)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in OttieniHtmlRenderizzato per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class RisultatoRilevazione
    Public Property UrlBase As String
    Public Property Tipo As TipoQuotidiano
    Public Property FeedUrl As String
End Class

Public Class RilevatoreQuotidiano
    Private Shared ReadOnly logger As New Logger()

    Private Shared ReadOnly PercorsiFeedComuni As String() = {
        "feed/", "feed", "rss/", "rss", "rss.xml", "atom.xml", "feed/atom/", "?feed=rss2"
    }

    Private Shared ReadOnly PercorsiSitemapNewsComuni As String() = {
        "sitemap_news.xml", "sitemap-news.xml", "news-sitemap.xml", "sitemap/news.xml", "sitemap-news-it.xml"
    }

    Public Shared Function Rileva(ByVal inputGrezzo As String) As RisultatoRilevazione
        Dim host As String = EstraiHost(inputGrezzo)
        If String.IsNullOrEmpty(host) Then Return Nothing

        Dim urlBase As String = RisolviUrlBase(host)
        If String.IsNullOrEmpty(urlBase) Then Return Nothing

        If VerificaCompatibilitaWordPressApi(urlBase) Then
            Return New RisultatoRilevazione With {.UrlBase = urlBase, .Tipo = TipoQuotidiano.WordPress, .FeedUrl = Nothing}
        End If

        Dim feed As String = TrovaFeedRss(urlBase)
        If Not String.IsNullOrEmpty(feed) Then
            Return New RisultatoRilevazione With {.UrlBase = urlBase, .Tipo = TipoQuotidiano.Rss, .FeedUrl = feed}
        End If

        Dim sitemapNews As String = TrovaSitemapNews(urlBase)
        If Not String.IsNullOrEmpty(sitemapNews) Then
            Return New RisultatoRilevazione With {.UrlBase = urlBase, .Tipo = TipoQuotidiano.SitemapNews, .FeedUrl = sitemapNews}
        End If

        Return New RisultatoRilevazione With {.UrlBase = urlBase, .Tipo = TipoQuotidiano.Sconosciuto, .FeedUrl = Nothing}
    End Function

    Private Shared Function TrovaSitemapNews(ByVal urlBase As String) As String
        Try
            For Each percorso In PercorsiSitemapNewsComuni
                Dim candidato As String = urlBase.TrimEnd("/"c) & "/" & percorso
                If ValidaSitemapNews(candidato) Then Return candidato
            Next

            Dim indiceUrl As String = urlBase.TrimEnd("/"c) & "/sitemap.xml"
            Dim contenutoIndice As String = UtilitaHttp.ScaricaConRetry(indiceUrl, maxTentativi:=1, timeoutMs:=8000)
            If Not String.IsNullOrEmpty(contenutoIndice) Then
                Try
                    Dim doc As XDocument = XDocument.Parse(contenutoIndice)
                    Dim nodiLoc = doc.Descendants().Where(Function(e) e.Name.LocalName = "loc")
                    For Each nodoLoc In nodiLoc
                        Dim valore As String = nodoLoc.Value
                        If valore.IndexOf("news", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso ValidaSitemapNews(valore) Then
                            Return valore
                        End If
                    Next
                Catch exParse As Exception
                    logger.ScriviLog($"Eccezione nel parsing dell'indice sitemap per {urlBase}: {exParse.Message}")
                End Try
            End If

            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in TrovaSitemapNews per {urlBase}: {ex.Message}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function ValidaSitemapNews(ByVal url As String) As Boolean
        Try
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url, maxTentativi:=1, timeoutMs:=6000)
            If String.IsNullOrEmpty(contenuto) Then Return False
            Dim doc As XDocument = XDocument.Parse(contenuto)

            For Each nodoUrl In doc.Descendants().Where(Function(e) e.Name.LocalName = "url")
                Dim haLoc As Boolean = nodoUrl.Elements().Any(Function(e) e.Name.LocalName = "loc" AndAlso Not String.IsNullOrWhiteSpace(e.Value))
                If Not haLoc Then Continue For

                Dim nodoNews = nodoUrl.Elements().FirstOrDefault(Function(e) e.Name.LocalName = "news")
                If nodoNews Is Nothing Then Continue For

                Dim haTitolo As Boolean = nodoNews.Elements().Any(Function(e) e.Name.LocalName = "title" AndAlso Not String.IsNullOrWhiteSpace(e.Value))
                If haTitolo Then Return True
            Next

            Return False
        Catch
            Return False
        End Try
    End Function

    Private Shared Function EstraiHost(ByVal inputGrezzo As String) As String
        Try
            Dim testo As String = inputGrezzo.Trim()
            If String.IsNullOrEmpty(testo) Then Return String.Empty

            If Not testo.Contains("://") Then
                testo = "https://" & testo
            End If

            Dim uri As Uri = Nothing
            If Uri.TryCreate(testo, UriKind.Absolute, uri) Then
                Return uri.Host
            End If
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in EstraiHost per '{inputGrezzo}': {ex.Message}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function RisolviUrlBase(ByVal host As String) As String
        Dim hostSenzaWww As String = If(host.StartsWith("www.", StringComparison.OrdinalIgnoreCase), host.Substring(4), host)
        Dim candidatiHost As String() = {"www." & hostSenzaWww, hostSenzaWww}

        For Each h In candidatiHost
            For Each protocollo In {"https://", "http://"}
                Dim candidato As String = protocollo & h & "/"
                If UtilitaHttp.Raggiungibile(candidato) Then
                    Return candidato
                End If
            Next
        Next
        Return String.Empty
    End Function

    Public Shared Function VerificaCompatibilitaWordPressApi(ByVal url As String) As Boolean
        Try
            Dim cleanedUrl As String = url.TrimEnd("/"c)
            Dim apiUrl As String
            If cleanedUrl.EndsWith("/wp-json", StringComparison.OrdinalIgnoreCase) Then
                apiUrl = cleanedUrl & "/"
            ElseIf cleanedUrl.Contains("/wp-json/") Then
                apiUrl = cleanedUrl
            Else
                apiUrl = cleanedUrl & "/wp-json/"
            End If

            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(apiUrl, maxTentativi:=1, timeoutMs:=6000)
            If String.IsNullOrEmpty(contenuto) Then Return False

            Try
                JObject.Parse(contenuto)
                If Not (contenuto.Contains("wp/v2") OrElse contenuto.Contains("namespaces")) Then Return False
            Catch
                Return False
            End Try

            Return VerificaArticoliRecuperabili(cleanedUrl)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica durante la verifica API di WordPress per '{url}': {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Function VerificaArticoliRecuperabili(ByVal urlBase As String) As Boolean
        Try
            Dim urlTest As String = urlBase.TrimEnd("/"c) & "/wp-json/wp/v2/posts?per_page=1&_fields=id"
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(urlTest, maxTentativi:=1, timeoutMs:=6000)
            If String.IsNullOrEmpty(contenuto) Then Return False

            Dim jsonArray As JArray = JArray.Parse(contenuto)
            Return jsonArray IsNot Nothing AndAlso jsonArray.Count > 0
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in VerificaArticoliRecuperabili per {urlBase}: {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Function TrovaFeedRss(ByVal urlBase As String) As String
        Try
            Dim html As String = UtilitaHttp.ScaricaConRetry(urlBase, maxTentativi:=1, timeoutMs:=8000)
            If Not String.IsNullOrEmpty(html) Then
                Dim feedDaHtml As String = CercaLinkFeedInHtml(html, urlBase)
                If Not String.IsNullOrEmpty(feedDaHtml) AndAlso ValidaFeed(feedDaHtml) Then
                    Return feedDaHtml
                End If
            End If

            For Each percorso In PercorsiFeedComuni
                Dim candidato As String = urlBase.TrimEnd("/"c) & "/" & percorso
                If ValidaFeed(candidato) Then
                    Return candidato
                End If
            Next

            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in TrovaFeedRss per {urlBase}: {ex.Message}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function CercaLinkFeedInHtml(ByVal html As String, ByVal urlBase As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) OrElse String.IsNullOrWhiteSpace(urlBase) Then Return String.Empty
            Dim uriBase As Uri = Nothing
            If Not Uri.TryCreate(urlBase, UriKind.Absolute, uriBase) Then Return String.Empty

            Dim analizzatore As New AngleHtml.HtmlParser()
            Using documento As AngleHtmlDom.IHtmlDocument = analizzatore.ParseDocument(html)
                If documento Is Nothing Then Return String.Empty
                For Each nodo As AngleDom.IElement In documento.QuerySelectorAll("link[rel~='alternate']")
                    Dim tipo As String = If(nodo.GetAttribute("type"), String.Empty).Trim().ToLowerInvariant()
                    If tipo <> "application/rss+xml" AndAlso tipo <> "application/atom+xml" Then Continue For
                    Dim href As String = If(nodo.GetAttribute("href"), String.Empty).Trim()
                    If href.Length = 0 Then Continue For
                    Dim uriRisolto As Uri = Nothing
                    If Uri.TryCreate(uriBase, href, uriRisolto) Then
                        Return uriRisolto.ToString()
                    End If
                Next
            End Using
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in CercaLinkFeedInHtml: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function ValidaFeed(ByVal url As String) As Boolean
        Try
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url, maxTentativi:=1, timeoutMs:=6000)
            If String.IsNullOrEmpty(contenuto) Then Return False
            Dim doc As XDocument = XDocument.Parse(contenuto)
            Dim nomeRadice As String = doc.Root.Name.LocalName.ToLowerInvariant()
            If nomeRadice <> "rss" AndAlso nomeRadice <> "feed" Then Return False

            Dim contieneArticoli As Boolean = doc.Descendants("item").Any()
            If Not contieneArticoli Then
                contieneArticoli = doc.Descendants(XName.Get("entry", "http://www.w3.org/2005/Atom")).Any()
            End If
            Return contieneArticoli
        Catch
            Return False
        End Try
    End Function
End Class

Friend Module UtilitaXml
    Private ReadOnly logger As New Logger()

    Public Function PrimoFiglio(ByVal elemento As XElement, ByVal nomeLocale As String) As XElement
        Try
            If elemento Is Nothing Then Return Nothing
            For Each figlio As XElement In elemento.Elements()
                If String.Equals(figlio.Name.LocalName, nomeLocale, StringComparison.OrdinalIgnoreCase) Then Return figlio
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaXml.PrimoFiglio ({nomeLocale}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function Figli(ByVal elemento As XElement, ByVal nomeLocale As String) As List(Of XElement)
        Dim risultato As New List(Of XElement)()
        Try
            If elemento Is Nothing Then Return risultato
            For Each figlio As XElement In elemento.Elements()
                If String.Equals(figlio.Name.LocalName, nomeLocale, StringComparison.OrdinalIgnoreCase) Then risultato.Add(figlio)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaXml.Figli ({nomeLocale}): {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Public Function Discendenti(ByVal documento As XDocument, ByVal nomeLocale As String) As List(Of XElement)
        Dim risultato As New List(Of XElement)()
        Try
            If documento Is Nothing Then Return risultato
            For Each discendente As XElement In documento.Descendants()
                If String.Equals(discendente.Name.LocalName, nomeLocale, StringComparison.OrdinalIgnoreCase) Then risultato.Add(discendente)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaXml.Discendenti ({nomeLocale}): {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Public Function ValoreTesto(ByVal elemento As XElement) As String
        Try
            If elemento Is Nothing Then Return String.Empty
            Return If(elemento.Value, String.Empty).Trim()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaXml.ValoreTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function
End Module

Friend Module UtilitaArticoli
    Private ReadOnly logger As New Logger()

    Public Function TrovaPerId(ByVal articoli As List(Of Articolo), ByVal id As String) As Articolo
        Try
            If articoli Is Nothing OrElse String.IsNullOrEmpty(id) Then Return Nothing
            For Each articolo As Articolo In articoli
                If articolo IsNot Nothing AndAlso String.Equals(articolo.Id, id, StringComparison.Ordinal) Then Return articolo
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaArticoli.TrovaPerId per {id}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Sub RimuoviPerId(ByVal articoli As List(Of Articolo), ByVal id As String)
        Try
            If articoli Is Nothing OrElse String.IsNullOrEmpty(id) Then Return
            For indice As Integer = articoli.Count - 1 To 0 Step -1
                Dim articolo As Articolo = articoli(indice)
                If articolo IsNot Nothing AndAlso String.Equals(articolo.Id, id, StringComparison.Ordinal) Then articoli.RemoveAt(indice)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaArticoli.RimuoviPerId per {id}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Function ContieneTesto(ByVal articolo As Articolo, ByVal termine As String, ByVal ancheNelTesto As Boolean) As Boolean
        Try
            If articolo Is Nothing OrElse String.IsNullOrEmpty(termine) Then Return False
            If articolo.Titolo IsNot Nothing AndAlso articolo.Titolo.IndexOf(termine, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If ancheNelTesto AndAlso articolo.Testo IsNot Nothing AndAlso ConvertitoreHtmlTesto.TestoSemplice(articolo.Testo).IndexOf(termine, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaArticoli.ContieneTesto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function ArticoloEscluso(ByVal id As String, ByVal titolo As String, ByVal url As String) As Articolo
        Try
            Return New Articolo With {.Id = id, .Titolo = titolo, .Testo = String.Empty, .Url = url, .APagamento = True}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in UtilitaArticoli.ArticoloEscluso per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Module

Public Class GestoreArticoliWordPress
    Implements IGestoreArticoli

    Private ReadOnly BaseUrl As String
    Private currentCategoryPage As New Dictionary(Of String, Integer)()
    Private ReadOnly logger As New Logger()
    Private ReadOnly suonoAttesa As New WGSuoni()
    Private Const LunghezzaMinimaContenutoApi As Integer = 400

    Public Sub New(urlsito As String)
        BaseUrl = If(urlsito, String.Empty).TrimEnd("/"c)
    End Sub

    Private Function CostruisciApiUrl(endpoint As String) As String
        Try
            If BaseUrl.EndsWith("/wp-json/wp/v2", StringComparison.OrdinalIgnoreCase) Then
                Return $"{BaseUrl}/{endpoint}"
            Else
                Return $"{BaseUrl}/wp-json/wp/v2/{endpoint}"
            End If
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliWordPress.CostruisciApiUrl ({endpoint}): {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Function CreaArticoloDaJson(ByVal item As JToken) As Articolo
        Try
            If item Is Nothing Then Return Nothing
            Dim nodoId As JToken = item("id")
            Dim nodoTitolo As JToken = item("title")
            If nodoId Is Nothing OrElse nodoTitolo Is Nothing Then Return Nothing
            Dim titolo As String = String.Empty
            If nodoTitolo.Type = JTokenType.Object AndAlso nodoTitolo("rendered") IsNot Nothing Then titolo = WebUtility.HtmlDecode(nodoTitolo("rendered").ToString())
            Dim link As String = String.Empty
            If item("link") IsNot Nothing Then link = item("link").ToString()
            Return New Articolo With {.Id = nodoId.ToString(), .Titolo = titolo, .Testo = String.Empty, .Url = link}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliWordPress.CreaArticoloDaJson: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Function OttieniUltimiArticoli(Optional ByVal numero As Integer = 100) As List(Of Articolo) Implements IGestoreArticoli.OttieniUltimiArticoli
        Dim articoli As New List(Of Articolo)
        suonoAttesa.Attesa()
        Try
            Dim pagina As Integer = 1
            While articoli.Count < numero
                Dim url As String = CostruisciApiUrl($"posts?per_page=100&page={pagina}&_fields=id,title,link")
                Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url)
                If String.IsNullOrEmpty(contenuto) Then Exit While

                Dim jsonArray As JArray = JArray.Parse(contenuto)
                If jsonArray.Count = 0 Then Exit While

                For Each item As JToken In jsonArray
                    If articoli.Count >= numero Then Exit For
                    Dim articolo As Articolo = CreaArticoloDaJson(item)
                    If articolo IsNot Nothing Then articoli.Add(articolo)
                Next
                pagina += 1
            End While
            Return GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(articoli)
        Catch ex As JsonException
            logger.ScriviLog($"JsonException in OttieniUltimiArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in OttieniUltimiArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function OttieniArticoloPerId(ByVal id As String) As Articolo Implements IGestoreArticoli.OttieniArticoloPerId
        suonoAttesa.Attesa()
        Try
            Dim url As String = CostruisciApiUrl($"posts/{id}")
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url)
            If String.IsNullOrEmpty(contenuto) Then Return Nothing

            Dim jsonObject As JObject = JObject.Parse(contenuto)
            Dim link As String = If(jsonObject("link") IsNot Nothing, jsonObject("link").ToString(), "")
            Dim idArticolo As String = If(jsonObject("id") IsNot Nothing, jsonObject("id").ToString(), id)
            Dim titolo As String = String.Empty
            Dim nodoTitolo As JToken = jsonObject("title")
            If nodoTitolo IsNot Nothing AndAlso nodoTitolo.Type = JTokenType.Object AndAlso nodoTitolo("rendered") IsNot Nothing Then
                titolo = WebUtility.HtmlDecode(nodoTitolo("rendered").ToString())
            End If

            If Not String.IsNullOrEmpty(link) AndAlso EstrattoreTestoPulito.PaginaAPagamento(link) Then
                Return UtilitaArticoli.ArticoloEscluso(idArticolo, titolo, link)
            End If

            Dim testo As String = String.Empty
            Dim nodoContenuto As JToken = jsonObject("content")
            If nodoContenuto IsNot Nothing AndAlso nodoContenuto.Type = JTokenType.Object AndAlso nodoContenuto("rendered") IsNot Nothing Then
                testo = EstrattoreTestoPulito.PulisciHtml(nodoContenuto("rendered").ToString())
            End If

            If testo.Length < LunghezzaMinimaContenutoApi AndAlso Not String.IsNullOrEmpty(link) Then
                Dim aPagamento As Boolean = False
                Dim testoPagina As String = EstrattoreTestoPulito.EstraiTestoDaUrl(link, aPagamento)
                If aPagamento Then Return UtilitaArticoli.ArticoloEscluso(idArticolo, titolo, link)
                If Not String.IsNullOrEmpty(testoPagina) AndAlso testoPagina.Length > testo.Length Then testo = testoPagina
            End If

            Return New Articolo With {
                .Id = idArticolo,
                .Titolo = titolo,
                .Testo = testo,
                .Url = link
            }
        Catch ex As JsonException
            logger.ScriviLog($"JsonException in OttieniArticoloPerId: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in OttieniArticoloPerId: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function OttieniArticoliPerCategoria(ByVal categoriaId As String, Optional ByVal numero As Integer = 100, Optional ByVal paginaIniziale As Integer = 1) As List(Of Articolo) Implements IGestoreArticoli.OttieniArticoliPerCategoria
        Dim articoli As New List(Of Articolo)
        suonoAttesa.Attesa()
        Try
            If Not currentCategoryPage.ContainsKey(categoriaId) Then
                currentCategoryPage(categoriaId) = paginaIniziale
            End If
            Dim pagina As Integer = currentCategoryPage(categoriaId)

            While articoli.Count < numero
                Dim url As String = CostruisciApiUrl($"posts?categories={categoriaId}&per_page={numero}&page={pagina}&_fields=id,title,link")
                Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url)
                If String.IsNullOrEmpty(contenuto) Then Exit While

                Dim jsonArray As JArray = JArray.Parse(contenuto)
                If jsonArray.Count = 0 Then Exit While

                For Each item As JToken In jsonArray
                    Dim articolo As Articolo = CreaArticoloDaJson(item)
                    If articolo IsNot Nothing Then articoli.Add(articolo)
                Next
                pagina += 1
            End While
            currentCategoryPage(categoriaId) = pagina
            Return GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(articoli)
        Catch ex As JsonException
            logger.ScriviLog($"JsonException in OttieniArticoliPerCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in OttieniArticoliPerCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function OttieniCategorie() As List(Of Categoria) Implements IGestoreArticoli.OttieniCategorie
        Dim categorie As New List(Of Categoria)
        suonoAttesa.Attesa()
        Try
            Dim pagina As Integer = 1
            Dim totalPages As Integer = 1
            Do
                Dim url As String = CostruisciApiUrl($"categories?page={pagina}&per_page=100")
                Dim headers As WebHeaderCollection = Nothing
                Dim contenuto As String = UtilitaHttp.ScaricaConHeaders(url, headers)
                If String.IsNullOrEmpty(contenuto) Then Exit Do

                Dim jsonArray As JArray = JArray.Parse(contenuto)
                For Each item As JToken In jsonArray
                    If item("id") Is Nothing OrElse item("name") Is Nothing Then Continue For
                    categorie.Add(New Categoria With {
                        .Id = item("id").ToString(),
                        .Nome = item("name").ToString()
                    })
                Next

                If pagina = 1 AndAlso headers IsNot Nothing Then
                    Dim totalPagesHeader As String = headers("X-WP-TotalPages")
                    If String.IsNullOrEmpty(totalPagesHeader) OrElse Not Integer.TryParse(totalPagesHeader, totalPages) Then
                        totalPages = 1
                    End If
                End If
                pagina += 1
            Loop While pagina <= totalPages
            Return categorie
        Catch ex As JsonException
            logger.ScriviLog($"JsonException in OttieniCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Categoria)()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in OttieniCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Categoria)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function RicercaArticoli(ByVal termineRicerca As String) As List(Of Articolo) Implements IGestoreArticoli.RicercaArticoli
        Dim articoliTrovati As New List(Of Articolo)
        suonoAttesa.Attesa()
        Try
            If String.IsNullOrWhiteSpace(termineRicerca) Then Return articoliTrovati
            Dim url As String = CostruisciApiUrl($"posts?search={Uri.EscapeDataString(termineRicerca)}&per_page=100&_fields=id,title,link")
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(url)
            If String.IsNullOrEmpty(contenuto) Then Return articoliTrovati

            Dim jsonArray As JArray = JArray.Parse(contenuto)
            For Each item As JToken In jsonArray
                Dim articolo As Articolo = CreaArticoloDaJson(item)
                If articolo IsNot Nothing Then articoliTrovati.Add(articolo)
            Next
            Return GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(articoliTrovati)
        Catch ex As JsonException
            logger.ScriviLog($"JsonException in RicercaArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione generica in RicercaArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function
End Class

Public Class GestoreArticoliRss
    Implements IGestoreArticoli

    Private ReadOnly feedUrl As String
    Private ReadOnly logger As New Logger()
    Private ReadOnly suonoAttesa As New WGSuoni()
    Private cacheArticoli As New List(Of Articolo)
    Private ReadOnly bloccoCache As New Object()
    Private cacheCategorie As New List(Of Categoria)
    Private Const NamespaceAtom As String = "http://www.w3.org/2005/Atom"
    Private Const LunghezzaMinimaContenutoCompleto As Integer = 200

    Public Sub New(urlFeed As String)
        feedUrl = urlFeed
    End Sub

    Public Function OttieniUltimiArticoli(Optional ByVal numero As Integer = 100) As List(Of Articolo) Implements IGestoreArticoli.OttieniUltimiArticoli
        suonoAttesa.Attesa()
        Try
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(feedUrl)
            If String.IsNullOrEmpty(contenuto) Then Return New List(Of Articolo)

            Dim doc As XDocument
            Try
                doc = XDocument.Parse(contenuto)
            Catch exParse As Exception
                logger.ScriviLog($"XML non valido dal feed {feedUrl}, provo a ripulirlo: {exParse.Message}")
                doc = XDocument.Parse(PulisciXmlNonValido(contenuto))
            End Try

            Dim elementiItem As List(Of XElement) = UtilitaXml.Discendenti(doc, "item")
            If elementiItem.Count = 0 Then
                elementiItem = New List(Of XElement)(doc.Descendants(XName.Get("entry", NamespaceAtom)))
            End If

            Dim tutti As New List(Of Articolo)
            For Each item As XElement In elementiItem
                If tutti.Count >= numero Then Exit For
                Dim articolo As Articolo = CreaArticoloDaItem(item)
                If articolo IsNot Nothing AndAlso Not String.IsNullOrEmpty(articolo.Url) Then tutti.Add(articolo)
            Next

            Dim risultato As List(Of Articolo) = GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(tutti)

            Dim categorieTrovate As New List(Of Categoria)
            For Each articolo As Articolo In risultato
                If articolo.Categorie Is Nothing Then Continue For
                For Each nomeCategoria As String In articolo.Categorie
                    If Not CategoriaPresente(categorieTrovate, nomeCategoria) Then
                        categorieTrovate.Add(New Categoria With {.Id = nomeCategoria, .Nome = nomeCategoria})
                    End If
                Next
            Next

            SyncLock bloccoCache
                cacheArticoli = New List(Of Articolo)(risultato)
                cacheCategorie = categorieTrovate
            End SyncLock
            Return risultato
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.OttieniUltimiArticoli per {feedUrl}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Private Function CopiaCache() As List(Of Articolo)
        Try
            SyncLock bloccoCache
                If cacheArticoli Is Nothing Then Return New List(Of Articolo)()
                Return New List(Of Articolo)(cacheArticoli)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.CopiaCache: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        End Try
    End Function

    Private Function CategoriaPresente(ByVal categorie As List(Of Categoria), ByVal nome As String) As Boolean
        Try
            For Each categoria As Categoria In categorie
                If categoria IsNot Nothing AndAlso String.Equals(categoria.Nome, nome, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.CategoriaPresente: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Function PulisciXmlNonValido(ByVal xml As String) As String
        Try
            Return System.Text.RegularExpressions.Regex.Replace(xml, "[\x00-\x08\x0B\x0C\x0E-\x1F]", "")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in PulisciXmlNonValido: {ex.Message}")
            Return xml
        End Try
    End Function

    Private Function LinkDaItem(ByVal item As XElement) As String
        Try
            For Each nodoLink As XElement In UtilitaXml.Figli(item, "link")
                Dim valore As String = UtilitaXml.ValoreTesto(nodoLink)
                If Not String.IsNullOrEmpty(valore) Then Return valore
            Next
            For Each nodoLink As XElement In UtilitaXml.Figli(item, "link")
                Dim attributoHref As XAttribute = nodoLink.Attribute("href")
                If attributoHref Is Nothing OrElse String.IsNullOrWhiteSpace(attributoHref.Value) Then Continue For
                Dim attributoRel As XAttribute = nodoLink.Attribute("rel")
                If attributoRel Is Nothing OrElse String.Equals(attributoRel.Value, "alternate", StringComparison.OrdinalIgnoreCase) Then Return attributoHref.Value.Trim()
            Next
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.LinkDaItem: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Function TestoDaItem(ByVal item As XElement) As String
        Try
            Dim contenutoCompleto As XElement = UtilitaXml.PrimoFiglio(item, "encoded")
            If contenutoCompleto IsNot Nothing AndAlso UtilitaXml.ValoreTesto(contenutoCompleto).Length > LunghezzaMinimaContenutoCompleto Then
                Return EstrattoreTestoPulito.PulisciHtml(contenutoCompleto.Value)
            End If

            Dim contenutoAtom As XElement = UtilitaXml.PrimoFiglio(item, "content")
            If contenutoAtom IsNot Nothing AndAlso UtilitaXml.ValoreTesto(contenutoAtom).Length > LunghezzaMinimaContenutoCompleto Then
                Return EstrattoreTestoPulito.PulisciHtml(contenutoAtom.Value)
            End If

            Dim descrizione As XElement = UtilitaXml.PrimoFiglio(item, "description")
            If descrizione Is Nothing Then descrizione = UtilitaXml.PrimoFiglio(item, "summary")
            If descrizione IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(descrizione.Value) Then
                Return EstrattoreTestoPulito.PulisciHtml(descrizione.Value)
            End If
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.TestoDaItem: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Function CategorieDaItem(ByVal item As XElement) As List(Of String)
        Dim categorie As New List(Of String)
        Try
            For Each nodoCategoria As XElement In UtilitaXml.Figli(item, "category")
                Dim nome As String = UtilitaXml.ValoreTesto(nodoCategoria)
                If String.IsNullOrEmpty(nome) Then
                    Dim attributoTermine As XAttribute = nodoCategoria.Attribute("term")
                    If attributoTermine IsNot Nothing Then nome = If(attributoTermine.Value, String.Empty).Trim()
                End If
                If String.IsNullOrEmpty(nome) Then Continue For
                If Not categorie.Contains(nome) Then categorie.Add(nome)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.CategorieDaItem: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return categorie
    End Function

    Private Function CreaArticoloDaItem(ByVal item As XElement) As Articolo
        Try
            If item Is Nothing Then Return Nothing
            Dim titolo As String = UtilitaXml.ValoreTesto(UtilitaXml.PrimoFiglio(item, "title"))
            Dim link As String = LinkDaItem(item)
            Dim testo As String = TestoDaItem(item)
            Dim categorieItem As List(Of String) = CategorieDaItem(item)
            Return New Articolo With {.Id = link, .Titolo = WebUtility.HtmlDecode(titolo), .Testo = testo, .Url = link, .Categorie = categorieItem}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.CreaArticoloDaItem: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function ScaricaEEstraiTesto(ByVal url As String, ByRef aPagamento As Boolean) As String
        aPagamento = False
        Try
            If String.IsNullOrEmpty(url) Then Return String.Empty
            Return EstrattoreTestoPulito.EstraiTestoDaUrl(url, aPagamento)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ScaricaEEstraiTesto per {url}: {ex.Message}")
            Return String.Empty
        End Try
    End Function

    Public Function OttieniArticoloPerId(ByVal id As String) As Articolo Implements IGestoreArticoli.OttieniArticoloPerId
        suonoAttesa.Attesa()
        Try
            Dim inCache As Articolo = UtilitaArticoli.TrovaPerId(CopiaCache(), id)
            Dim titolo As String = If(inCache IsNot Nothing, inCache.Titolo, id)
            Dim aPagamento As Boolean = False
            Dim testo As String = ScaricaEEstraiTesto(id, aPagamento)
            If aPagamento Then
                SyncLock bloccoCache
                    UtilitaArticoli.RimuoviPerId(cacheArticoli, id)
                End SyncLock
                Return UtilitaArticoli.ArticoloEscluso(id, titolo, id)
            End If
            If String.IsNullOrWhiteSpace(testo) AndAlso Not InstallatoreTrafilatura.MotoreTrafilaturaAttivo() AndAlso inCache IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(inCache.Testo) Then
                testo = inCache.Testo
            End If
            Return New Articolo With {.Id = id, .Titolo = titolo, .Testo = If(testo, String.Empty), .Url = id}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.OttieniArticoloPerId per {id}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return Nothing
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function OttieniArticoliPerCategoria(ByVal categoriaId As String, Optional ByVal numero As Integer = 100, Optional ByVal paginaIniziale As Integer = 1) As List(Of Articolo) Implements IGestoreArticoli.OttieniArticoliPerCategoria
        Dim risultato As New List(Of Articolo)
        Try
            Dim tutti As List(Of Articolo) = CopiaCache()
            If tutti Is Nothing OrElse tutti.Count = 0 Then tutti = OttieniUltimiArticoli(numero)
            For Each articolo As Articolo In GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(tutti)
                If articolo Is Nothing OrElse articolo.Categorie Is Nothing Then Continue For
                For Each categoria As String In articolo.Categorie
                    If String.Equals(categoria, categoriaId, StringComparison.OrdinalIgnoreCase) Then
                        risultato.Add(articolo)
                        Exit For
                    End If
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.OttieniArticoliPerCategoria per {categoriaId}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Public Function OttieniCategorie() As List(Of Categoria) Implements IGestoreArticoli.OttieniCategorie
        Try
            If CopiaCache().Count = 0 Then OttieniUltimiArticoli()
            SyncLock bloccoCache
                If cacheCategorie Is Nothing Then Return New List(Of Categoria)()
                Return New List(Of Categoria)(cacheCategorie)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.OttieniCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Categoria)()
        End Try
    End Function

    Public Function RicercaArticoli(ByVal termineRicerca As String) As List(Of Articolo) Implements IGestoreArticoli.RicercaArticoli
        Dim risultato As New List(Of Articolo)
        Try
            If String.IsNullOrWhiteSpace(termineRicerca) Then Return risultato
            Dim tutti As List(Of Articolo) = CopiaCache()
            If tutti Is Nothing OrElse tutti.Count = 0 Then tutti = OttieniUltimiArticoli()
            For Each articolo As Articolo In GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(tutti)
                If UtilitaArticoli.ContieneTesto(articolo, termineRicerca, True) Then risultato.Add(articolo)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliRss.RicercaArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function
End Class

Public Class GestoreArticoliSitemapNews
    Implements IGestoreArticoli

    Private ReadOnly sitemapUrl As String
    Private ReadOnly logger As New Logger()
    Private ReadOnly suonoAttesa As New WGSuoni()
    Private cacheArticoli As New List(Of Articolo)
    Private ReadOnly bloccoCache As New Object()

    Private Shared ReadOnly AccessiRiservati As String() = {"subscription", "registration"}

    Public Sub New(urlSitemap As String)
        sitemapUrl = urlSitemap
    End Sub

    Public Function OttieniUltimiArticoli(Optional ByVal numero As Integer = 100) As List(Of Articolo) Implements IGestoreArticoli.OttieniUltimiArticoli
        suonoAttesa.Attesa()
        Try
            Dim contenuto As String = UtilitaHttp.ScaricaConRetry(sitemapUrl)
            If String.IsNullOrEmpty(contenuto) Then Return New List(Of Articolo)

            Dim doc As XDocument
            Try
                doc = XDocument.Parse(contenuto)
            Catch exParse As Exception
                logger.ScriviLog($"XML non valido dalla sitemap {sitemapUrl}: {exParse.Message}")
                Return New List(Of Articolo)
            End Try

            Dim tutti As New List(Of Articolo)
            For Each nodoUrl As XElement In UtilitaXml.Discendenti(doc, "url")
                If tutti.Count >= numero Then Exit For
                Try
                    Dim loc As String = UtilitaXml.ValoreTesto(UtilitaXml.PrimoFiglio(nodoUrl, "loc"))
                    If String.IsNullOrEmpty(loc) Then Continue For

                    Dim nodoNews As XElement = UtilitaXml.PrimoFiglio(nodoUrl, "news")
                    If nodoNews Is Nothing Then Continue For
                    If AccessoRiservato(nodoNews) Then Continue For

                    Dim titolo As String = UtilitaXml.ValoreTesto(UtilitaXml.PrimoFiglio(nodoNews, "title"))
                    If String.IsNullOrEmpty(titolo) Then Continue For

                    tutti.Add(New Articolo With {.Id = loc, .Titolo = WebUtility.HtmlDecode(titolo), .Testo = "", .Url = loc})
                Catch exVoce As Exception
                    logger.ScriviLog($"Eccezione elaborando una voce della sitemap {sitemapUrl}: {exVoce.Message}")
                End Try
            Next

            Dim risultato As List(Of Articolo) = GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(tutti)
            SyncLock bloccoCache
                cacheArticoli = New List(Of Articolo)(risultato)
            End SyncLock
            Return risultato
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.OttieniUltimiArticoli per {sitemapUrl}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Private Function CopiaCache() As List(Of Articolo)
        Try
            SyncLock bloccoCache
                If cacheArticoli Is Nothing Then Return New List(Of Articolo)()
                Return New List(Of Articolo)(cacheArticoli)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.CopiaCache: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        End Try
    End Function

    Private Function AccessoRiservato(ByVal nodoNews As XElement) As Boolean
        Try
            Dim accesso As String = UtilitaXml.ValoreTesto(UtilitaXml.PrimoFiglio(nodoNews, "access"))
            If String.IsNullOrEmpty(accesso) Then Return False
            For Each valore As String In AccessiRiservati
                If accesso.IndexOf(valore, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.AccessoRiservato: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function OttieniArticoloPerId(ByVal id As String) As Articolo Implements IGestoreArticoli.OttieniArticoloPerId
        suonoAttesa.Attesa()
        Try
            Dim inCache As Articolo = UtilitaArticoli.TrovaPerId(CopiaCache(), id)
            Dim titolo As String = If(inCache IsNot Nothing, inCache.Titolo, id)
            Dim aPagamento As Boolean = False
            Dim testo As String = EstrattoreTestoPulito.EstraiTestoDaUrl(id, aPagamento)
            If aPagamento Then
                SyncLock bloccoCache
                    UtilitaArticoli.RimuoviPerId(cacheArticoli, id)
                End SyncLock
                Return UtilitaArticoli.ArticoloEscluso(id, titolo, id)
            End If
            Return New Articolo With {.Id = id, .Titolo = titolo, .Testo = If(testo, String.Empty), .Url = id}
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.OttieniArticoloPerId per {id}: {ex.Message}")
            Return Nothing
        Finally
            suonoAttesa.StopAttesa()
        End Try
    End Function

    Public Function OttieniArticoliPerCategoria(ByVal categoriaId As String, Optional ByVal numero As Integer = 100, Optional ByVal paginaIniziale As Integer = 1) As List(Of Articolo) Implements IGestoreArticoli.OttieniArticoliPerCategoria
        Try
            Dim copia As List(Of Articolo) = CopiaCache()
            If copia.Count > 0 Then Return GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(copia)
            Return OttieniUltimiArticoli(numero)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.OttieniArticoliPerCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        End Try
    End Function

    Public Function OttieniCategorie() As List(Of Categoria) Implements IGestoreArticoli.OttieniCategorie
        Try
            Return New List(Of Categoria)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.OttieniCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Categoria)
        End Try
    End Function

    Public Function RicercaArticoli(ByVal termineRicerca As String) As List(Of Articolo) Implements IGestoreArticoli.RicercaArticoli
        Dim risultato As New List(Of Articolo)
        Try
            If String.IsNullOrWhiteSpace(termineRicerca) Then Return risultato
            Dim tutti As List(Of Articolo) = CopiaCache()
            If tutti Is Nothing OrElse tutti.Count = 0 Then tutti = OttieniUltimiArticoli()
            For Each articolo As Articolo In GestoreArchivioNotizieQuotidiani.FiltraArticoliAPagamento(tutti)
                If UtilitaArticoli.ContieneTesto(articolo, termineRicerca, False) Then risultato.Add(articolo)
            Next
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArticoliSitemapNews.RicercaArticoli: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function
End Class


Public Class FabbricaGestoreArticoli
    Public Shared Function Crea(ByVal quotidiano As Quotidiano) As IGestoreArticoli
        Select Case quotidiano.Tipo
            Case TipoQuotidiano.WordPress
                Return New GestoreArticoliWordPress(quotidiano.Url)
            Case TipoQuotidiano.Rss
                Return New GestoreArticoliRss(quotidiano.FeedUrl)
            Case TipoQuotidiano.SitemapNews
                Return New GestoreArticoliSitemapNews(quotidiano.FeedUrl)
            Case Else
                Return New GestoreArticoliWordPress(quotidiano.Url)
        End Select
    End Function
End Class

Public Class ArticoloQuotidianoNonLetto
    Public Property IdArticolo As String
    Public Property UrlQuotidiano As String
    Public Property TitoloQuotidiano As String
    Public Property TitoloArticolo As String
    Public Property UrlArticolo As String
    Public Property DataTrovata As DateTime
End Class

Public Class GestoreArchivioNotizieQuotidiani
    Private Shared ReadOnly logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared Function RisolviCartellaNotizie() As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("quotidiani")
            If Not String.IsNullOrWhiteSpace(cartella) Then Return cartella
        Catch
        End Try
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "quotidiani")
    End Function

    Private Shared Function RisolviFileDbNotizie() As String
        Try
            Dim percorso As String = WGDatabase.PercorsoFileDati("quotidiani", "quotidiani-notizie.sqlite")
            If Not String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Catch
        End Try
        Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "quotidiani", "quotidiani-notizie.sqlite")
    End Function

    Private Shared ReadOnly dataFolderPath As String = RisolviCartellaNotizie()
    Private Shared ReadOnly dbPath As String = RisolviFileDbNotizie()
    Private Shared ReadOnly connectionString As String = $"Data Source={dbPath};Version=3;"
    Private Shared _databaseInizializzato As Boolean = False
    Private Const GiorniConservazioneAPagamento As Integer = 90
    Private Const ChiaveRevisioneRilevatore As String = "RevisioneRilevatorePagamento"
    Private Const RevisioneRilevatoreCorrente As String = "2"

    Private Shared Function OttieniConnessione() As SQLiteConnection
        Dim conn As SQLiteConnection = WGDatabase.ApriConnessione(dbPath, "Version=3;Journal Mode=WAL;Synchronous=Normal;Pooling=True;")
        If conn Is Nothing Then
            conn = New SQLiteConnection(connectionString)
            conn.Open()
            Using cmdPragma As New SQLiteCommand("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", conn)
                cmdPragma.ExecuteNonQuery()
            End Using
        End If
        Return conn
    End Function

    Private Shared Sub InizializzaDatabase()
        If _databaseInizializzato Then Return
        If Not Directory.Exists(dataFolderPath) Then Directory.CreateDirectory(dataFolderPath)
        If Not File.Exists(dbPath) Then SQLiteConnection.CreateFile(dbPath)
        Using conn As SQLiteConnection = OttieniConnessione()
            Using cmd As New SQLiteCommand(conn)
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS ArticoliConosciuti (IdArticolo TEXT NOT NULL, UrlQuotidiano TEXT NOT NULL, PRIMARY KEY (IdArticolo, UrlQuotidiano));" &
                    "CREATE INDEX IF NOT EXISTS idx_articoliconosciuti_url ON ArticoliConosciuti(UrlQuotidiano);" &
                    "CREATE TABLE IF NOT EXISTS ArticoliNonLetti (IdArticolo TEXT NOT NULL, UrlQuotidiano TEXT NOT NULL, TitoloQuotidiano TEXT, TitoloArticolo TEXT, UrlArticolo TEXT, DataTrovata TEXT NOT NULL, DataGiorno TEXT, PRIMARY KEY (IdArticolo, UrlQuotidiano));" &
                    "CREATE INDEX IF NOT EXISTS idx_articolinonletti_giorno ON ArticoliNonLetti(DataGiorno);" &
                    "CREATE TABLE IF NOT EXISTS ArticoliAPagamento (UrlArticolo TEXT NOT NULL PRIMARY KEY, DataRilevata TEXT NOT NULL);"
                cmd.ExecuteNonQuery()
            End Using

            Try
                Using cmdPulizia As New SQLiteCommand("DELETE FROM ArticoliAPagamento WHERE DataRilevata < @limite", conn)
                    cmdPulizia.Parameters.AddWithValue("@limite", DateTime.Now.AddDays(-GiorniConservazioneAPagamento).ToString("yyyy-MM-dd HH:mm:ss"))
                    cmdPulizia.ExecuteNonQuery()
                End Using
            Catch exPulizia As Exception
                logger.ScriviLog("GestoreArchivioNotizieQuotidiani.InizializzaDatabase pulizia articoli a pagamento: " & exPulizia.Message)
            End Try

            AzzeraSegnalazioniObsolete(conn)

            Using cmdMigra As New SQLiteCommand("UPDATE ArticoliNonLetti SET DataGiorno = substr(DataTrovata, 1, 10) WHERE DataGiorno IS NULL", conn)
                cmdMigra.ExecuteNonQuery()
            End Using

            Try
                WGDatabase.AssicuraColonnaChiave(conn, "ArticoliNonLetti")
            Catch exChiave As Exception
                logger.ScriviLog("GestoreArchivioNotizieQuotidiani.InizializzaDatabase chiave: " & exChiave.Message)
            End Try
        End Using
        _databaseInizializzato = True
    End Sub

    Private Shared Sub AzzeraSegnalazioniObsolete(ByVal conn As SQLiteConnection)
        Try
            Using cmdTabella As New SQLiteCommand("CREATE TABLE IF NOT EXISTS ImpostazioniArchivio (Chiave TEXT NOT NULL PRIMARY KEY, Valore TEXT);", conn)
                cmdTabella.ExecuteNonQuery()
            End Using

            Dim revisione As String = String.Empty
            Using cmdLettura As New SQLiteCommand("SELECT Valore FROM ImpostazioniArchivio WHERE Chiave = @chiave LIMIT 1", conn)
                cmdLettura.Parameters.AddWithValue("@chiave", ChiaveRevisioneRilevatore)
                Dim valore As Object = cmdLettura.ExecuteScalar()
                If valore IsNot Nothing AndAlso Not TypeOf valore Is DBNull Then revisione = Convert.ToString(valore)
            End Using
            If String.Equals(revisione, RevisioneRilevatoreCorrente, StringComparison.Ordinal) Then Return

            Using transazione As SQLiteTransaction = conn.BeginTransaction()
                Using cmdAzzera As New SQLiteCommand("DELETE FROM ArticoliAPagamento", conn, transazione)
                    cmdAzzera.ExecuteNonQuery()
                End Using
                Using cmdScrittura As New SQLiteCommand("INSERT OR REPLACE INTO ImpostazioniArchivio (Chiave, Valore) VALUES (@chiave, @valore)", conn, transazione)
                    cmdScrittura.Parameters.AddWithValue("@chiave", ChiaveRevisioneRilevatore)
                    cmdScrittura.Parameters.AddWithValue("@valore", RevisioneRilevatoreCorrente)
                    cmdScrittura.ExecuteNonQuery()
                End Using
                transazione.Commit()
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArchivioNotizieQuotidiani.AzzeraSegnalazioniObsolete: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function OttieniIdArticoliConosciuti(ByVal urlQuotidiano As String) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT IdArticolo FROM ArticoliConosciuti WHERE UrlQuotidiano = @url", connessione)
                    comando.Parameters.AddWithValue("@url", urlQuotidiano)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultato.Add(lettore.GetString(0))
                        End While
                    End Using
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in OttieniIdArticoliConosciuti per {urlQuotidiano}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
        Return risultato
    End Function

    Public Shared Sub SalvaArticoliConosciuti(ByVal urlQuotidiano As String, ByVal idArticoli As List(Of String))
        If idArticoli Is Nothing OrElse idArticoli.Count = 0 Then Return
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                For Each id In idArticoli
                    Try
                        If String.IsNullOrEmpty(id) Then Continue For
                        Using comando As New SQLiteCommand("INSERT OR IGNORE INTO ArticoliConosciuti (IdArticolo, UrlQuotidiano) VALUES (@id, @url)", connessione)
                            comando.Parameters.AddWithValue("@id", id)
                            comando.Parameters.AddWithValue("@url", urlQuotidiano)
                            comando.ExecuteNonQuery()
                        End Using
                    Catch exArticolo As Exception
                        logger.ScriviLog($"Eccezione in SalvaArticoliConosciuti (articolo {id}): {exArticolo.Message} - StackTrace: {exArticolo.StackTrace}")
                    End Try
                Next
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in SalvaArticoliConosciuti per {urlQuotidiano}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Sub SalvaArticoliNonLetti(ByVal urlQuotidiano As String, ByVal titoloQuotidiano As String, ByVal articoli As List(Of Articolo))
        If articoli Is Nothing OrElse articoli.Count = 0 Then Return
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Dim esclusi As HashSet(Of String) = LeggiUrlAPagamento(connessione)
                For Each articolo In articoli
                    Try
                        If articolo Is Nothing OrElse String.IsNullOrEmpty(articolo.Id) Then Continue For
                        If esclusi.Contains(ChiaveUrl(articolo.Url)) OrElse esclusi.Contains(ChiaveUrl(articolo.Id)) Then Continue For
                        Dim adesso As DateTime = DateTime.Now
                        Dim chiaveWGArticolo As String = ""
                        Try
                            chiaveWGArticolo = WGDatabase.GeneraChiaveUnivocaPerTabella(connessione, "ArticoliNonLetti")
                        Catch exChiave As Exception
                            logger.ScriviLog($"Eccezione generazione chiave in SalvaArticoliNonLetti (articolo {articolo.Id}): {exChiave.Message}")
                        End Try
                        Using comando As New SQLiteCommand("INSERT OR IGNORE INTO ArticoliNonLetti (IdArticolo, UrlQuotidiano, TitoloQuotidiano, TitoloArticolo, UrlArticolo, DataTrovata, DataGiorno, ChiaveWG) VALUES (@id, @url, @titoloQuot, @titoloArt, @urlArt, @data, @giorno, @chiaveWG)", connessione)
                            comando.Parameters.AddWithValue("@id", articolo.Id)
                            comando.Parameters.AddWithValue("@url", urlQuotidiano)
                            comando.Parameters.AddWithValue("@titoloQuot", titoloQuotidiano)
                            comando.Parameters.AddWithValue("@titoloArt", articolo.Titolo)
                            comando.Parameters.AddWithValue("@urlArt", articolo.Url)
                            comando.Parameters.AddWithValue("@data", adesso.ToString("yyyy-MM-dd HH:mm:ss"))
                            comando.Parameters.AddWithValue("@giorno", adesso.ToString("yyyy-MM-dd"))
                            comando.Parameters.AddWithValue("@chiaveWG", chiaveWGArticolo)
                            comando.ExecuteNonQuery()
                        End Using
                    Catch exArticolo As Exception
                        logger.ScriviLog($"Eccezione in SalvaArticoliNonLetti (articolo {articolo.Id}): {exArticolo.Message} - StackTrace: {exArticolo.StackTrace}")
                    End Try
                Next
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in SalvaArticoliNonLetti per {urlQuotidiano}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function OttieniQuotidianiConNuoviArticoliOggi() As List(Of String)
        Dim risultato As New List(Of String)
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
                Using comando As New SQLiteCommand("SELECT DISTINCT TitoloQuotidiano FROM ArticoliNonLetti WHERE DataGiorno = @oggi ORDER BY TitoloQuotidiano COLLATE NOCASE", connessione)
                    comando.Parameters.AddWithValue("@oggi", oggi)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            risultato.Add(lettore.GetString(0))
                        End While
                    End Using
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in OttieniQuotidianiConNuoviArticoliOggi: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
        Return risultato
    End Function

    Public Shared Function OttieniArticoliNonLettiOggi() As List(Of ArticoloQuotidianoNonLetto)
        Dim risultato As New List(Of ArticoloQuotidianoNonLetto)
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
                Using comando As New SQLiteCommand("SELECT IdArticolo, UrlQuotidiano, TitoloQuotidiano, TitoloArticolo, UrlArticolo, DataTrovata FROM ArticoliNonLetti WHERE DataGiorno = @oggi ORDER BY DataTrovata DESC", connessione)
                    comando.Parameters.AddWithValue("@oggi", oggi)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Try
                                Dim dataTrovata As DateTime = DateTime.Now
                                DateTime.TryParse(lettore.GetString(5), dataTrovata)
                                risultato.Add(New ArticoloQuotidianoNonLetto With {
                                    .IdArticolo = lettore.GetString(0),
                                    .UrlQuotidiano = lettore.GetString(1),
                                    .TitoloQuotidiano = lettore.GetString(2),
                                    .TitoloArticolo = lettore.GetString(3),
                                    .UrlArticolo = lettore.GetString(4),
                                    .DataTrovata = dataTrovata
                                })
                            Catch exRiga As Exception
                                logger.ScriviLog($"Eccezione in OttieniArticoliNonLettiOggi (riga): {exRiga.Message}")
                            End Try
                        End While
                    End Using
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in OttieniArticoliNonLettiOggi: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
        Return risultato
    End Function

    Public Shared Sub SegnaArticoloComeLetto(ByVal idArticolo As String, ByVal urlQuotidiano As String)
        If String.IsNullOrEmpty(idArticolo) OrElse String.IsNullOrEmpty(urlQuotidiano) Then Return
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Using comando As New SQLiteCommand("DELETE FROM ArticoliNonLetti WHERE IdArticolo = @id AND UrlQuotidiano = @url", connessione)
                    comando.Parameters.AddWithValue("@id", idArticolo)
                    comando.Parameters.AddWithValue("@url", urlQuotidiano)
                    comando.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in SegnaArticoloComeLetto per {idArticolo}/{urlQuotidiano}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch
                End Try
            End Try
        End SyncLock
    End Sub

    Private Shared Function ChiaveUrl(ByVal url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return String.Empty
            Dim pulito As String = url.Trim()
            Dim posizione As Integer = pulito.IndexOf("#"c)
            If posizione >= 0 Then pulito = pulito.Substring(0, posizione)
            Return pulito
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArchivioNotizieQuotidiani.ChiaveUrl per {url}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Private Shared Function LeggiUrlAPagamento(ByVal connessione As SQLiteConnection) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.Ordinal)
        Try
            Using comando As New SQLiteCommand("SELECT UrlArticolo FROM ArticoliAPagamento", connessione)
                Using lettore As SQLiteDataReader = comando.ExecuteReader()
                    While lettore.Read()
                        If Not lettore.IsDBNull(0) Then risultato.Add(lettore.GetString(0))
                    End While
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArchivioNotizieQuotidiani.LeggiUrlAPagamento: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Public Shared Sub SegnaArticoloAPagamento(ByVal urlArticolo As String)
        Dim chiave As String = ChiaveUrl(urlArticolo)
        If String.IsNullOrEmpty(chiave) Then Return
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO ArticoliAPagamento (UrlArticolo, DataRilevata) VALUES (@url, @data)", connessione)
                    comando.Parameters.AddWithValue("@url", chiave)
                    comando.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    comando.ExecuteNonQuery()
                End Using
                Using comandoNonLetti As New SQLiteCommand("DELETE FROM ArticoliNonLetti WHERE UrlArticolo = @url OR IdArticolo = @url", connessione)
                    comandoNonLetti.Parameters.AddWithValue("@url", chiave)
                    comandoNonLetti.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in SegnaArticoloAPagamento per {urlArticolo}: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch exChiusura As Exception
                    logger.ScriviLog($"Eccezione in SegnaArticoloAPagamento chiudendo la connessione: {exChiusura.Message}")
                End Try
            End Try
        End SyncLock
    End Sub

    Public Shared Function ArticoloAPagamento(ByVal urlArticolo As String) As Boolean
        Dim chiave As String = ChiaveUrl(urlArticolo)
        If String.IsNullOrEmpty(chiave) Then Return False
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                Using comando As New SQLiteCommand("SELECT 1 FROM ArticoliAPagamento WHERE UrlArticolo = @url LIMIT 1", connessione)
                    comando.Parameters.AddWithValue("@url", chiave)
                    Dim valore As Object = comando.ExecuteScalar()
                    Return valore IsNot Nothing AndAlso Not TypeOf valore Is DBNull
                End Using
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in ArticoloAPagamento per {urlArticolo}: {ex.Message} - StackTrace: {ex.StackTrace}")
                Return False
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch exChiusura As Exception
                    logger.ScriviLog($"Eccezione in ArticoloAPagamento chiudendo la connessione: {exChiusura.Message}")
                End Try
            End Try
        End SyncLock
    End Function

    Public Shared Function OttieniUrlArticoliAPagamento() As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.Ordinal)
        SyncLock _lock
            Dim connessione As SQLiteConnection = Nothing
            Try
                InizializzaDatabase()
                connessione = OttieniConnessione()
                risultato = LeggiUrlAPagamento(connessione)
            Catch ex As Exception
                logger.ScriviLog($"Eccezione in OttieniUrlArticoliAPagamento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Finally
                Try
                    If connessione IsNot Nothing Then connessione.Close()
                Catch exChiusura As Exception
                    logger.ScriviLog($"Eccezione in OttieniUrlArticoliAPagamento chiudendo la connessione: {exChiusura.Message}")
                End Try
            End Try
        End SyncLock
        Return risultato
    End Function

    Private Shared Function CopiaElencoSicura(ByVal articoli As List(Of Articolo)) As List(Of Articolo)
        Try
            If articoli Is Nothing Then Return New List(Of Articolo)()
            Return New List(Of Articolo)(articoli)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in GestoreArchivioNotizieQuotidiani.CopiaElencoSicura: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return New List(Of Articolo)()
        End Try
    End Function

    Public Shared Function FiltraArticoliAPagamento(ByVal articoli As List(Of Articolo)) As List(Of Articolo)
        If articoli Is Nothing Then Return New List(Of Articolo)()
        Try
            If articoli.Count = 0 Then Return New List(Of Articolo)()
            Dim esclusi As HashSet(Of String) = OttieniUrlArticoliAPagamento()
            If esclusi.Count = 0 Then Return New List(Of Articolo)(articoli)

            Dim risultato As New List(Of Articolo)(articoli.Count)
            For Each articolo As Articolo In articoli
                If articolo Is Nothing Then Continue For
                If esclusi.Contains(ChiaveUrl(articolo.Url)) Then Continue For
                If esclusi.Contains(ChiaveUrl(articolo.Id)) Then Continue For
                risultato.Add(articolo)
            Next
            Return risultato
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in FiltraArticoliAPagamento: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return CopiaElencoSicura(articoli)
        End Try
    End Function
End Class

Public Class ElencoQuotidiani
    Private ReadOnly dataFolderPath As String
    Private ReadOnly dbPath As String
    Private ReadOnly connectionString As String
    Private ReadOnly logger As New Logger()
    Private Shared ReadOnly dbLock As New Object()

    Public Sub New()
        Try
            Try
                dbPath = WGDatabase.PercorsoFileDati("quotidiani", "quotidiani.sqlite")
            Catch
                dbPath = ""
            End Try
            If String.IsNullOrWhiteSpace(dbPath) Then
                dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "quotidiani", "quotidiani.sqlite")
            End If

            Try
                dataFolderPath = Path.GetDirectoryName(dbPath)
            Catch
                dataFolderPath = ""
            End Try
            If String.IsNullOrWhiteSpace(dataFolderPath) Then
                dataFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "quotidiani")
            End If
            connectionString = $"Data Source={dbPath};Version=3;"

            SyncLock dbLock
                If Not Directory.Exists(dataFolderPath) Then
                    Directory.CreateDirectory(dataFolderPath)
                End If
                If Not File.Exists(dbPath) Then
                    SQLiteConnection.CreateFile(dbPath)
                End If
                InizializzaStrutturaDatabase()
                MigraDaXmlSeNecessario()
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Errore nel costruttore di ElencoQuotidiani: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Function ApriConnessione() As SQLiteConnection
        Dim conn As SQLiteConnection = Nothing
        If Not String.IsNullOrWhiteSpace(dbPath) Then
            conn = WGDatabase.ApriConnessione(dbPath)
        End If
        If conn Is Nothing Then
            conn = New SQLiteConnection(connectionString)
            conn.Open()
        End If
        Return conn
    End Function

    Private Sub InizializzaStrutturaDatabase()
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand(conn)
                cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS Categorie (Id TEXT PRIMARY KEY, Nome TEXT NOT NULL);" &
                    "CREATE TABLE IF NOT EXISTS Quotidiani (" &
                    " Url TEXT PRIMARY KEY," &
                    " Titolo TEXT NOT NULL," &
                    " CategoriaId TEXT," &
                    " Tipo TEXT NOT NULL," &
                    " FeedUrl TEXT," &
                    " Preferito INTEGER NOT NULL DEFAULT 0," &
                    " UltimoAccesso TEXT," &
                    " DataAggiunta TEXT NOT NULL);"
                cmd.ExecuteNonQuery()
            End Using

            Try
                WGDatabase.AssicuraColonnaChiave(conn, "Categorie")
                WGDatabase.AssicuraColonnaChiave(conn, "Quotidiani")
            Catch exChiave As Exception
                logger.ScriviLog("ElencoQuotidiani.InizializzaStrutturaDatabase chiave: " & exChiave.Message)
            End Try

            PreparaSincronizzazioneFile(conn)
        End Using
    End Sub

    Private Const EspressioneOraTestateSync As String = "strftime('%Y-%m-%dT%H:%M:%fZ','now')"
    Private Const GiorniConservazioneEliminatiTestateSync As Integer = 180

    Private Sub PreparaSincronizzazioneFile(conn As SQLiteConnection)
        Try
            AggiungiColonnaSeMancaSync(conn, "Quotidiani", "ModificaSync", "TEXT")
            AggiungiColonnaSeMancaSync(conn, "Categorie", "ModificaSync", "TEXT")

            Dim istruzioni As String() = {
                "CREATE TABLE IF NOT EXISTS QuotidianiSyncEliminati (Url TEXT PRIMARY KEY, Data TEXT NOT NULL);",
                "CREATE TABLE IF NOT EXISTS CategorieSyncEliminate (Id TEXT PRIMARY KEY, Data TEXT NOT NULL);",
                "UPDATE Quotidiani SET ModificaSync = " & EspressioneOraTestateSync & " WHERE ModificaSync IS NULL OR ModificaSync = '';",
                "UPDATE Categorie SET ModificaSync = " & EspressioneOraTestateSync & " WHERE ModificaSync IS NULL OR ModificaSync = '';",
                "CREATE TRIGGER IF NOT EXISTS trg_quotidiani_sync_ins AFTER INSERT ON Quotidiani BEGIN " &
                    "DELETE FROM QuotidianiSyncEliminati WHERE Url = NEW.Url; " &
                    "UPDATE Quotidiani SET ModificaSync = " & EspressioneOraTestateSync & " WHERE Url = NEW.Url AND (ModificaSync IS NULL OR ModificaSync = ''); END;",
                "CREATE TRIGGER IF NOT EXISTS trg_quotidiani_sync_upd AFTER UPDATE OF Url, Titolo, CategoriaId, Tipo, FeedUrl, Preferito, UltimoAccesso ON Quotidiani " &
                    "WHEN NEW.ModificaSync IS OLD.ModificaSync BEGIN " &
                    "UPDATE Quotidiani SET ModificaSync = " & EspressioneOraTestateSync & " WHERE Url = NEW.Url; END;",
                "CREATE TRIGGER IF NOT EXISTS trg_quotidiani_sync_url AFTER UPDATE OF Url ON Quotidiani WHEN OLD.Url <> NEW.Url BEGIN " &
                    "INSERT OR REPLACE INTO QuotidianiSyncEliminati (Url, Data) VALUES (OLD.Url, " & EspressioneOraTestateSync & "); " &
                    "DELETE FROM QuotidianiSyncEliminati WHERE Url = NEW.Url; END;",
                "CREATE TRIGGER IF NOT EXISTS trg_quotidiani_sync_del AFTER DELETE ON Quotidiani BEGIN " &
                    "INSERT OR REPLACE INTO QuotidianiSyncEliminati (Url, Data) VALUES (OLD.Url, " & EspressioneOraTestateSync & "); END;",
                "CREATE TRIGGER IF NOT EXISTS trg_categorie_sync_ins AFTER INSERT ON Categorie BEGIN " &
                    "DELETE FROM CategorieSyncEliminate WHERE Id = NEW.Id; " &
                    "UPDATE Categorie SET ModificaSync = " & EspressioneOraTestateSync & " WHERE Id = NEW.Id AND (ModificaSync IS NULL OR ModificaSync = ''); END;",
                "CREATE TRIGGER IF NOT EXISTS trg_categorie_sync_upd AFTER UPDATE OF Nome ON Categorie WHEN NEW.ModificaSync IS OLD.ModificaSync BEGIN " &
                    "UPDATE Categorie SET ModificaSync = " & EspressioneOraTestateSync & " WHERE Id = NEW.Id; END;",
                "CREATE TRIGGER IF NOT EXISTS trg_categorie_sync_del AFTER DELETE ON Categorie BEGIN " &
                    "INSERT OR REPLACE INTO CategorieSyncEliminate (Id, Data) VALUES (OLD.Id, " & EspressioneOraTestateSync & "); END;"
            }
            For Each istruzione As String In istruzioni
                Try
                    Using cmd As New SQLiteCommand(istruzione, conn)
                        cmd.ExecuteNonQuery()
                    End Using
                Catch exIstruzione As Exception
                    logger.ScriviLog("ElencoQuotidiani.PreparaSincronizzazioneFile istruzione: " & exIstruzione.ToString())
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.PreparaSincronizzazioneFile: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiColonnaSeMancaSync(conn As SQLiteConnection, tabella As String, colonna As String, definizione As String)
        Try
            Dim presente As Boolean = False
            Using cmd As New SQLiteCommand("PRAGMA table_info(" & tabella & ");", conn)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        If String.Equals(Convert.ToString(reader("name")), colonna, StringComparison.OrdinalIgnoreCase) Then
                            presente = True
                            Exit While
                        End If
                    End While
                End Using
            End Using
            If presente Then Return
            Using cmd As New SQLiteCommand("ALTER TABLE " & tabella & " ADD COLUMN " & colonna & " " & definizione & ";", conn)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.AggiungiColonnaSeMancaSync (" & tabella & "." & colonna & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub MigraDaXmlSeNecessario()
        Try
            Dim vecchioXml As String = Path.Combine(dataFolderPath, "siti.xml")
            If Not File.Exists(vecchioXml) Then
                vecchioXml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "quotidiani", "siti.xml")
            End If
            If Not File.Exists(vecchioXml) Then Return
            If ContaQuotidiani() > 0 Then Return

            Dim doc As XDocument = XDocument.Load(vecchioXml)
            Using conn As SQLiteConnection = ApriConnessione()
                Using transazione = conn.BeginTransaction()
                    Dim categorieNode = doc.Root.Element("Categorie")
                    If categorieNode IsNot Nothing Then
                        For Each cat In categorieNode.Elements("Categoria")
                            EseguiInserimentoCategoria(conn, cat.Attribute("ID").Value, cat.Value)
                        Next
                    End If
                    For Each sitoNode In doc.Root.Elements("Sito")
                        Dim url As String = sitoNode.Element("Url").Value
                        Dim nome As String = sitoNode.Element("Nome").Value
                        Dim categoriaId As String = If(sitoNode.Attribute("CategoriaID") IsNot Nothing, sitoNode.Attribute("CategoriaID").Value, Nothing)
                        EseguiInserimentoQuotidiano(conn, url, nome, categoriaId, TipoQuotidiano.WordPress, Nothing)
                    Next
                    transazione.Commit()
                End Using
            End Using
            File.Move(vecchioXml, vecchioXml & ".migrato")
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in MigraDaXmlSeNecessario: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub EseguiInserimentoCategoria(conn As SQLiteConnection, id As String, nome As String)
        Using cmd As New SQLiteCommand("INSERT OR IGNORE INTO Categorie (Id, Nome, ChiaveWG) VALUES (@id, @nome, @chiaveWG)", conn)
            cmd.Parameters.AddWithValue("@id", id)
            cmd.Parameters.AddWithValue("@nome", nome)
            cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Sub EseguiInserimentoQuotidiano(conn As SQLiteConnection, url As String, titolo As String, categoriaId As String, tipo As TipoQuotidiano, feedUrl As String)
        Using cmd As New SQLiteCommand("INSERT OR IGNORE INTO Quotidiani (Url,Titolo,CategoriaId,Tipo,FeedUrl,Preferito,UltimoAccesso,DataAggiunta,ChiaveWG) VALUES (@url,@titolo,@cat,@tipo,@feed,0,NULL,@data,@chiaveWG)", conn)
            cmd.Parameters.AddWithValue("@url", url)
            cmd.Parameters.AddWithValue("@titolo", titolo)
            cmd.Parameters.AddWithValue("@cat", If(String.IsNullOrEmpty(categoriaId), CObj(DBNull.Value), categoriaId))
            cmd.Parameters.AddWithValue("@tipo", tipo.ToString())
            cmd.Parameters.AddWithValue("@feed", If(String.IsNullOrEmpty(feedUrl), CObj(DBNull.Value), feedUrl))
            cmd.Parameters.AddWithValue("@data", DateTime.UtcNow.ToString("o"))
            cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
            cmd.ExecuteNonQuery()
        End Using
    End Sub

    Private Function ContaQuotidiani() As Integer
        Using conn As SQLiteConnection = ApriConnessione()
            Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Quotidiani", conn)
                Return CInt(cmd.ExecuteScalar())
            End Using
        End Using
    End Function

    Private Function LeggiQuotidiano(reader As SQLiteDataReader) As Quotidiano
        Dim ultimoAccessoRaw As Object = reader("UltimoAccesso")
        Dim ultimoAccesso As DateTime? = Nothing
        If Not IsDBNull(ultimoAccessoRaw) Then
            Dim valore As DateTime
            If DateTime.TryParse(CStr(ultimoAccessoRaw), valore) Then ultimoAccesso = valore
        End If

        Dim tipo As TipoQuotidiano = TipoQuotidiano.Sconosciuto
        [Enum].TryParse(Of TipoQuotidiano)(CStr(reader("Tipo")), tipo)

        Return New Quotidiano With {
            .Url = CStr(reader("Url")),
            .Titolo = CStr(reader("Titolo")),
            .CategoriaId = If(IsDBNull(reader("CategoriaId")), String.Empty, CStr(reader("CategoriaId"))),
            .Tipo = tipo,
            .FeedUrl = If(IsDBNull(reader("FeedUrl")), String.Empty, CStr(reader("FeedUrl"))),
            .Preferito = CInt(reader("Preferito")) = 1,
            .UltimoAccesso = ultimoAccesso
        }
    End Function

    Public Function QuotidianoEsistente(ByVal url As String) As Boolean
        Try
            Dim urlNorm As String = url.TrimEnd("/"c) & "/"
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Quotidiani WHERE Url = @url", conn)
                    cmd.Parameters.AddWithValue("@url", urlNorm)
                    Return CInt(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in QuotidianoEsistente: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function AggiungiQuotidiano(ByVal nome As String, ByVal urlBase As String, ByVal tipo As TipoQuotidiano, ByVal feedUrl As String, Optional ByVal categoriaId As String = "") As Boolean
        Try
            Dim nomeEsistente As Boolean
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Quotidiani WHERE Titolo = @titolo COLLATE NOCASE", conn)
                    cmd.Parameters.AddWithValue("@titolo", nome)
                    nomeEsistente = CInt(cmd.ExecuteScalar()) > 0
                End Using
            End Using
            If nomeEsistente Then Return False
            If QuotidianoEsistente(urlBase) Then Return False

            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    EseguiInserimentoQuotidiano(conn, urlBase, nome, categoriaId, tipo, feedUrl)
                End Using
            End SyncLock
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AggiungiQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function AggiornaQuotidiano(ByVal vecchioUrl As String, ByVal nuovoNome As String, ByVal nuovoUrl As String, ByVal nuovoTipo As TipoQuotidiano, ByVal nuovoFeedUrl As String) As Boolean
        Try
            If Not String.Equals(vecchioUrl, nuovoUrl, StringComparison.OrdinalIgnoreCase) AndAlso QuotidianoEsistente(nuovoUrl) Then
                Return False
            End If

            Dim nomeInUsoDaAltri As Boolean
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT COUNT(1) FROM Quotidiani WHERE Titolo = @titolo COLLATE NOCASE AND Url <> @vecchioUrl", conn)
                    cmd.Parameters.AddWithValue("@titolo", nuovoNome)
                    cmd.Parameters.AddWithValue("@vecchioUrl", vecchioUrl)
                    nomeInUsoDaAltri = CInt(cmd.ExecuteScalar()) > 0
                End Using
            End Using
            If nomeInUsoDaAltri Then Return False

            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET Url = @nuovoUrl, Titolo = @titolo, Tipo = @tipo, FeedUrl = @feed WHERE Url = @vecchioUrl", conn)
                        cmd.Parameters.AddWithValue("@nuovoUrl", nuovoUrl)
                        cmd.Parameters.AddWithValue("@titolo", nuovoNome)
                        cmd.Parameters.AddWithValue("@tipo", nuovoTipo.ToString())
                        cmd.Parameters.AddWithValue("@feed", If(String.IsNullOrEmpty(nuovoFeedUrl), CObj(DBNull.Value), nuovoFeedUrl))
                        cmd.Parameters.AddWithValue("@vecchioUrl", vecchioUrl)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
            Return True
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AggiornaQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Sub SvuotaPreferiti()
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET Preferito = 0 WHERE Preferito = 1", conn)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SvuotaPreferiti: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub SvuotaRecenti()
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET UltimoAccesso = NULL WHERE UltimoAccesso IS NOT NULL", conn)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SvuotaRecenti: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub RimuoviQuotidiano(ByVal url As String)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("DELETE FROM Quotidiani WHERE Url = @url", conn)
                        cmd.Parameters.AddWithValue("@url", url)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RimuoviQuotidiano: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub SpostaQuotidianoCategoria(ByVal urlQuotidiano As String, ByVal nuovaCategoriaId As String)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET CategoriaId = @cat WHERE Url = @url", conn)
                        cmd.Parameters.AddWithValue("@cat", If(String.IsNullOrEmpty(nuovaCategoriaId), CObj(DBNull.Value), nuovaCategoriaId))
                        cmd.Parameters.AddWithValue("@url", urlQuotidiano)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in SpostaQuotidianoCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub ImpostaPreferito(ByVal url As String, ByVal preferito As Boolean)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET Preferito = @pref WHERE Url = @url", conn)
                        cmd.Parameters.AddWithValue("@pref", If(preferito, 1, 0))
                        cmd.Parameters.AddWithValue("@url", url)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ImpostaPreferito: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub AggiornaUltimoAccesso(ByVal url As String)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Quotidiani SET UltimoAccesso = @adesso WHERE Url = @url", conn)
                        cmd.Parameters.AddWithValue("@adesso", DateTime.UtcNow.ToString("o"))
                        cmd.Parameters.AddWithValue("@url", url)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AggiornaUltimoAccesso: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Function OttieniTuttiIQuotidiani() As List(Of Quotidiano)
        Dim risultato As New List(Of Quotidiano)
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT * FROM Quotidiani ORDER BY Titolo COLLATE NOCASE", conn)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            risultato.Add(LeggiQuotidiano(reader))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in OttieniTuttiIQuotidiani: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return risultato
    End Function

    Public Function OttieniQuotidianiPerCategoria(ByVal categoriaId As String) As List(Of Quotidiano)
        If String.IsNullOrEmpty(categoriaId) Then
            Return OttieniTuttiIQuotidiani().Where(Function(q) String.IsNullOrEmpty(q.CategoriaId)).ToList()
        Else
            Return OttieniTuttiIQuotidiani().Where(Function(q) q.CategoriaId = categoriaId).ToList()
        End If
    End Function

    Public Function OttieniPreferiti() As List(Of Quotidiano)
        Return OttieniTuttiIQuotidiani().Where(Function(q) q.Preferito).ToList()
    End Function

    Public Function OttieniRecenti(Optional ByVal numero As Integer = 20) As List(Of Quotidiano)
        Return OttieniTuttiIQuotidiani().Where(Function(q) q.UltimoAccesso.HasValue).OrderByDescending(Function(q) q.UltimoAccesso.Value).Take(numero).ToList()
    End Function

    Public Function AggiungiCategoria(ByVal nomeCategoria As String) As String
        Try
            Dim categorieEsistenti = OttieniCategorie()
            If categorieEsistenti.Any(Function(c) c.Nome.Equals(nomeCategoria, StringComparison.OrdinalIgnoreCase)) Then
                Return "categoria già esistente"
            End If

            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    EseguiInserimentoCategoria(conn, Guid.NewGuid().ToString(), nomeCategoria)
                End Using
            End SyncLock
            Return String.Empty
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in AggiungiCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return "errore sconosciuto"
        End Try
    End Function

    Public Sub ModificaCategoria(ByVal categoriaId As String, ByVal nuovoNome As String)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using cmd As New SQLiteCommand("UPDATE Categorie SET Nome = @nome WHERE Id = @id", conn)
                        cmd.Parameters.AddWithValue("@nome", nuovoNome)
                        cmd.Parameters.AddWithValue("@id", categoriaId)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in ModificaCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Sub RimuoviCategoria(ByVal categoriaId As String)
        Try
            SyncLock dbLock
                Using conn As SQLiteConnection = ApriConnessione()
                    Using transazione = conn.BeginTransaction()
                        Using cmd As New SQLiteCommand("UPDATE Quotidiani SET CategoriaId = NULL WHERE CategoriaId = @id", conn)
                            cmd.Parameters.AddWithValue("@id", categoriaId)
                            cmd.ExecuteNonQuery()
                        End Using
                        Using cmd As New SQLiteCommand("DELETE FROM Categorie WHERE Id = @id", conn)
                            cmd.Parameters.AddWithValue("@id", categoriaId)
                            cmd.ExecuteNonQuery()
                        End Using
                        transazione.Commit()
                    End Using
                End Using
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in RimuoviCategoria: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Public Function OttieniCategorie() As List(Of CategoriaQuotidiani)
        Dim categorie As New List(Of CategoriaQuotidiani)
        Try
            Using conn As SQLiteConnection = ApriConnessione()
                Using cmd As New SQLiteCommand("SELECT * FROM Categorie ORDER BY Nome COLLATE NOCASE", conn)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            categorie.Add(New CategoriaQuotidiani With {
                                .Id = CStr(reader("Id")),
                                .Nome = CStr(reader("Nome"))
                            })
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in OttieniCategorie: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
        Return categorie
    End Function
    Private Class StatoVoceTestataSync
        Public Property Id As String = ""
        Public Property Data As DateTime = DateTime.MinValue
        Public Property Eliminata As Boolean = False
        Public Property Valori As New Dictionary(Of String, Object)(StringComparer.Ordinal)
    End Class

    Public Function UnisciCategorieTestateFile(ByVal vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        SyncLock dbLock
            Try
                If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
                Using conn As SQLiteConnection = ApriConnessione()
                    PulisciEliminazioniTestateSync(conn, "CategorieSyncEliminate")
                    Dim remote As Dictionary(Of String, StatoVoceTestataSync) = IndicizzaVociTestateSync(vociRemote)
                    Dim eliminati As Dictionary(Of String, StatoVoceTestataSync) = LeggiEliminazioniTestateSync(conn, "CategorieSyncEliminate", "Id")
                    CollegaCategorieOmonimeSync(conn, remote, eliminati)
                    Dim locali As Dictionary(Of String, StatoVoceTestataSync) = LeggiCategorieTestateSync(conn)
                    Dim finali As List(Of StatoVoceTestataSync) = ConfrontaVociTestateSync(locali, eliminati, remote)
                    Dim risultato As New List(Of Dictionary(Of String, Object))

                    For Each voce As StatoVoceTestataSync In finali
                        Try
                            If remote.ContainsKey(voce.Id) AndAlso Object.ReferenceEquals(remote(voce.Id), voce) Then ApplicaCategoriaTestataSync(conn, voce, locali.ContainsKey(voce.Id))
                            risultato.Add(VoceTestataSync(voce))
                        Catch exVoce As Exception
                            logger.ScriviLog("ElencoQuotidiani.UnisciCategorieTestateFile voce (" & If(voce.Id, "") & "): " & exVoce.ToString())
                        End Try
                    Next
                    Return risultato
                End Using
            Catch ex As Exception
                logger.ScriviLog("ElencoQuotidiani.UnisciCategorieTestateFile: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Public Function UnisciTestateFile(ByVal vociRemote As List(Of Dictionary(Of String, Object))) As List(Of Dictionary(Of String, Object))
        SyncLock dbLock
            Try
                If vociRemote Is Nothing Then vociRemote = New List(Of Dictionary(Of String, Object))()
                Using conn As SQLiteConnection = ApriConnessione()
                    PulisciEliminazioniTestateSync(conn, "QuotidianiSyncEliminati")
                    Dim locali As Dictionary(Of String, StatoVoceTestataSync) = LeggiTestateSync(conn)
                    Dim eliminati As Dictionary(Of String, StatoVoceTestataSync) = LeggiEliminazioniTestateSync(conn, "QuotidianiSyncEliminati", "Url")
                    Dim remote As Dictionary(Of String, StatoVoceTestataSync) = IndicizzaVociTestateSync(vociRemote)
                    Dim categorie As New HashSet(Of String)(StringComparer.Ordinal)
                    For Each chiaveCategoria As String In LeggiCategorieTestateSync(conn).Keys
                        categorie.Add(chiaveCategoria)
                    Next
                    Dim finali As List(Of StatoVoceTestataSync) = ConfrontaVociTestateSync(locali, eliminati, remote)
                    Dim risultato As New List(Of Dictionary(Of String, Object))

                    For Each voce As StatoVoceTestataSync In finali
                        Try
                            If remote.ContainsKey(voce.Id) AndAlso Object.ReferenceEquals(remote(voce.Id), voce) Then ApplicaTestataSync(conn, voce, locali.ContainsKey(voce.Id), categorie)
                            risultato.Add(VoceTestataSync(voce))
                        Catch exVoce As Exception
                            logger.ScriviLog("ElencoQuotidiani.UnisciTestateFile voce (" & If(voce.Id, "") & "): " & exVoce.ToString())
                        End Try
                    Next
                    Return risultato
                End Using
            Catch ex As Exception
                logger.ScriviLog("ElencoQuotidiani.UnisciTestateFile: " & ex.ToString())
                Return Nothing
            End Try
        End SyncLock
    End Function

    Private Sub CollegaCategorieOmonimeSync(ByVal conn As SQLiteConnection, ByVal remote As Dictionary(Of String, StatoVoceTestataSync), ByVal eliminati As Dictionary(Of String, StatoVoceTestataSync))
        Try
            Dim locali As Dictionary(Of String, StatoVoceTestataSync) = LeggiCategorieTestateSync(conn)
            Dim identificativi As New List(Of String)(remote.Keys)
            identificativi.Sort(StringComparer.Ordinal)
            For Each identificativo As String In identificativi
                Try
                    Dim remota As StatoVoceTestataSync = remote(identificativo)
                    If remota.Eliminata OrElse locali.ContainsKey(identificativo) OrElse eliminati.ContainsKey(identificativo) Then Continue For
                    Dim nomeRemoto As String = TestoValoreTestataSync(remota, "nome")
                    If nomeRemoto.Length = 0 Then Continue For
                    Dim trovato As String = ""
                    For Each coppia As KeyValuePair(Of String, StatoVoceTestataSync) In locali
                        If remote.ContainsKey(coppia.Key) Then Continue For
                        If String.Equals(TestoValoreTestataSync(coppia.Value, "nome"), nomeRemoto, StringComparison.CurrentCultureIgnoreCase) Then
                            trovato = coppia.Key
                            Exit For
                        End If
                    Next
                    If trovato.Length = 0 Then Continue For
                    Using transazione As SQLiteTransaction = conn.BeginTransaction()
                        Using cmd As New SQLiteCommand("UPDATE Quotidiani SET CategoriaId = @nuovo WHERE CategoriaId = @vecchio", conn, transazione)
                            cmd.Parameters.AddWithValue("@nuovo", identificativo)
                            cmd.Parameters.AddWithValue("@vecchio", trovato)
                            cmd.ExecuteNonQuery()
                        End Using
                        Using cmd As New SQLiteCommand("UPDATE Categorie SET Id = @nuovo WHERE Id = @vecchio", conn, transazione)
                            cmd.Parameters.AddWithValue("@nuovo", identificativo)
                            cmd.Parameters.AddWithValue("@vecchio", trovato)
                            cmd.ExecuteNonQuery()
                        End Using
                        transazione.Commit()
                    End Using
                    Dim spostata As StatoVoceTestataSync = locali(trovato)
                    locali.Remove(trovato)
                    spostata.Id = identificativo
                    locali(identificativo) = spostata
                Catch exVoce As Exception
                    logger.ScriviLog("ElencoQuotidiani.CollegaCategorieOmonimeSync voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.CollegaCategorieOmonimeSync: " & ex.ToString())
        End Try
    End Sub

    Private Function ConfrontaVociTestateSync(ByVal locali As Dictionary(Of String, StatoVoceTestataSync), ByVal eliminati As Dictionary(Of String, StatoVoceTestataSync), ByVal remote As Dictionary(Of String, StatoVoceTestataSync)) As List(Of StatoVoceTestataSync)
        Dim risultato As New List(Of StatoVoceTestataSync)
        Try
            Dim identificativi As New HashSet(Of String)(StringComparer.Ordinal)
            For Each chiave As String In locali.Keys
                identificativi.Add(chiave)
            Next
            For Each chiave As String In eliminati.Keys
                identificativi.Add(chiave)
            Next
            For Each chiave As String In remote.Keys
                identificativi.Add(chiave)
            Next
            For Each identificativo As String In identificativi
                Try
                    Dim locale As StatoVoceTestataSync = Nothing
                    If Not locali.TryGetValue(identificativo, locale) Then eliminati.TryGetValue(identificativo, locale)
                    Dim remota As StatoVoceTestataSync = Nothing
                    remote.TryGetValue(identificativo, remota)
                    If remota IsNot Nothing AndAlso (locale Is Nothing OrElse remota.Data > locale.Data) Then
                        risultato.Add(remota)
                    ElseIf locale IsNot Nothing Then
                        risultato.Add(locale)
                    End If
                Catch exVoce As Exception
                    logger.ScriviLog("ElencoQuotidiani.ConfrontaVociTestateSync voce (" & If(identificativo, "") & "): " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.ConfrontaVociTestateSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Sub ApplicaCategoriaTestataSync(ByVal conn As SQLiteConnection, ByVal voce As StatoVoceTestataSync, ByVal esisteLocale As Boolean)
        Try
            Dim modificato As String = FormatoDataTestateSync(voce.Data)
            If voce.Eliminata Then
                Using cmd As New SQLiteCommand("UPDATE Quotidiani SET CategoriaId = NULL WHERE CategoriaId = @id", conn)
                    cmd.Parameters.AddWithValue("@id", voce.Id)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("DELETE FROM Categorie WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@id", voce.Id)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO CategorieSyncEliminate (Id, Data) VALUES (@id, @data)", conn)
                    cmd.Parameters.AddWithValue("@id", voce.Id)
                    cmd.Parameters.AddWithValue("@data", modificato)
                    cmd.ExecuteNonQuery()
                End Using
                Return
            End If

            Dim nome As String = TestoValoreTestataSync(voce, "nome")
            If nome.Length = 0 Then nome = "Senza nome"
            If esisteLocale Then
                Using cmd As New SQLiteCommand("UPDATE Categorie SET Nome = @nome, ModificaSync = @modificato WHERE Id = @id", conn)
                    cmd.Parameters.AddWithValue("@nome", nome)
                    cmd.Parameters.AddWithValue("@modificato", modificato)
                    cmd.Parameters.AddWithValue("@id", voce.Id)
                    cmd.ExecuteNonQuery()
                End Using
            Else
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Categorie (Id, Nome, ChiaveWG, ModificaSync) VALUES (@id, @nome, @chiaveWG, @modificato)", conn)
                    cmd.Parameters.AddWithValue("@id", voce.Id)
                    cmd.Parameters.AddWithValue("@nome", nome)
                    cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                    cmd.Parameters.AddWithValue("@modificato", modificato)
                    cmd.ExecuteNonQuery()
                End Using
            End If
            Using cmd As New SQLiteCommand("DELETE FROM CategorieSyncEliminate WHERE Id = @id", conn)
                cmd.Parameters.AddWithValue("@id", voce.Id)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.ApplicaCategoriaTestataSync (" & If(voce Is Nothing, "", voce.Id) & "): " & ex.ToString())
        End Try
    End Sub

    Private Sub ApplicaTestataSync(ByVal conn As SQLiteConnection, ByVal voce As StatoVoceTestataSync, ByVal esisteLocale As Boolean, ByVal categorie As HashSet(Of String))
        Try
            Dim modificato As String = FormatoDataTestateSync(voce.Data)
            If voce.Eliminata Then
                Using cmd As New SQLiteCommand("DELETE FROM Quotidiani WHERE Url = @url", conn)
                    cmd.Parameters.AddWithValue("@url", voce.Id)
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO QuotidianiSyncEliminati (Url, Data) VALUES (@url, @data)", conn)
                    cmd.Parameters.AddWithValue("@url", voce.Id)
                    cmd.Parameters.AddWithValue("@data", modificato)
                    cmd.ExecuteNonQuery()
                End Using
                Return
            End If

            Dim titolo As String = TestoValoreTestataSync(voce, "titolo")
            If titolo.Length = 0 Then titolo = voce.Id
            Dim tipo As TipoQuotidiano = TipoQuotidiano.Sconosciuto
            If Not [Enum].TryParse(Of TipoQuotidiano)(TestoValoreTestataSync(voce, "tipo"), True, tipo) Then tipo = TipoQuotidiano.Sconosciuto
            Dim feedUrl As String = TestoValoreTestataSync(voce, "feedUrl")
            Dim categoria As String = TestoValoreTestataSync(voce, "categoria")
            If categoria.Length > 0 AndAlso Not categorie.Contains(categoria) Then categoria = ""
            Dim preferitoTesto As String = TestoValoreTestataSync(voce, "preferito").ToLowerInvariant()
            Dim preferito As Integer = If(preferitoTesto = "true" OrElse preferitoTesto = "1", 1, 0)
            Dim ultimoAccesso As String = TestoValoreTestataSync(voce, "ultimoAccesso")
            Dim dataAggiunta As String = TestoValoreTestataSync(voce, "dataAggiunta")
            If dataAggiunta.Length = 0 Then dataAggiunta = DateTime.UtcNow.ToString("o")

            If esisteLocale Then
                Using cmd As New SQLiteCommand("UPDATE Quotidiani SET Titolo = @titolo, CategoriaId = @cat, Tipo = @tipo, FeedUrl = @feed, Preferito = @pref, UltimoAccesso = @accesso, ModificaSync = @modificato WHERE Url = @url", conn)
                    cmd.Parameters.AddWithValue("@titolo", titolo)
                    cmd.Parameters.AddWithValue("@cat", If(categoria.Length = 0, CObj(DBNull.Value), categoria))
                    cmd.Parameters.AddWithValue("@tipo", tipo.ToString())
                    cmd.Parameters.AddWithValue("@feed", If(feedUrl.Length = 0, CObj(DBNull.Value), feedUrl))
                    cmd.Parameters.AddWithValue("@pref", preferito)
                    cmd.Parameters.AddWithValue("@accesso", If(ultimoAccesso.Length = 0, CObj(DBNull.Value), ultimoAccesso))
                    cmd.Parameters.AddWithValue("@modificato", modificato)
                    cmd.Parameters.AddWithValue("@url", voce.Id)
                    cmd.ExecuteNonQuery()
                End Using
            Else
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Quotidiani (Url, Titolo, CategoriaId, Tipo, FeedUrl, Preferito, UltimoAccesso, DataAggiunta, ChiaveWG, ModificaSync) VALUES (@url, @titolo, @cat, @tipo, @feed, @pref, @accesso, @data, @chiaveWG, @modificato)", conn)
                    cmd.Parameters.AddWithValue("@url", voce.Id)
                    cmd.Parameters.AddWithValue("@titolo", titolo)
                    cmd.Parameters.AddWithValue("@cat", If(categoria.Length = 0, CObj(DBNull.Value), categoria))
                    cmd.Parameters.AddWithValue("@tipo", tipo.ToString())
                    cmd.Parameters.AddWithValue("@feed", If(feedUrl.Length = 0, CObj(DBNull.Value), feedUrl))
                    cmd.Parameters.AddWithValue("@pref", preferito)
                    cmd.Parameters.AddWithValue("@accesso", If(ultimoAccesso.Length = 0, CObj(DBNull.Value), ultimoAccesso))
                    cmd.Parameters.AddWithValue("@data", dataAggiunta)
                    cmd.Parameters.AddWithValue("@chiaveWG", WGDatabase.GeneraChiave())
                    cmd.Parameters.AddWithValue("@modificato", modificato)
                    cmd.ExecuteNonQuery()
                End Using
            End If
            Using cmd As New SQLiteCommand("DELETE FROM QuotidianiSyncEliminati WHERE Url = @url", conn)
                cmd.Parameters.AddWithValue("@url", voce.Id)
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.ApplicaTestataSync (" & If(voce Is Nothing, "", voce.Id) & "): " & ex.ToString())
        End Try
    End Sub

    Private Function LeggiTestateSync(ByVal conn As SQLiteConnection) As Dictionary(Of String, StatoVoceTestataSync)
        Dim risultato As New Dictionary(Of String, StatoVoceTestataSync)(StringComparer.Ordinal)
        Try
            Using cmd As New SQLiteCommand("SELECT Url, Titolo, CategoriaId, Tipo, FeedUrl, Preferito, UltimoAccesso, DataAggiunta, ModificaSync FROM Quotidiani", conn)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        Dim voce As New StatoVoceTestataSync()
                        voce.Id = TestoReaderTestateSync(reader, "Url")
                        If voce.Id.Length = 0 Then Continue While
                        voce.Data = LeggiDataTestateSync(TestoReaderTestateSync(reader, "ModificaSync"))
                        voce.Valori("titolo") = TestoReaderTestateSync(reader, "Titolo")
                        voce.Valori("url") = voce.Id
                        voce.Valori("tipo") = TestoReaderTestateSync(reader, "Tipo")
                        voce.Valori("feedUrl") = TestoReaderTestateSync(reader, "FeedUrl")
                        voce.Valori("categoria") = TestoReaderTestateSync(reader, "CategoriaId")
                        voce.Valori("preferito") = TestoReaderTestateSync(reader, "Preferito") = "1"
                        voce.Valori("ultimoAccesso") = TestoReaderTestateSync(reader, "UltimoAccesso")
                        voce.Valori("dataAggiunta") = TestoReaderTestateSync(reader, "DataAggiunta")
                        risultato(voce.Id) = voce
                    End While
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.LeggiTestateSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function LeggiCategorieTestateSync(ByVal conn As SQLiteConnection) As Dictionary(Of String, StatoVoceTestataSync)
        Dim risultato As New Dictionary(Of String, StatoVoceTestataSync)(StringComparer.Ordinal)
        Try
            Using cmd As New SQLiteCommand("SELECT Id, Nome, ModificaSync FROM Categorie", conn)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        Dim voce As New StatoVoceTestataSync()
                        voce.Id = TestoReaderTestateSync(reader, "Id")
                        If voce.Id.Length = 0 Then Continue While
                        voce.Data = LeggiDataTestateSync(TestoReaderTestateSync(reader, "ModificaSync"))
                        voce.Valori("nome") = TestoReaderTestateSync(reader, "Nome")
                        risultato(voce.Id) = voce
                    End While
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.LeggiCategorieTestateSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function LeggiEliminazioniTestateSync(ByVal conn As SQLiteConnection, ByVal tabella As String, ByVal colonnaChiave As String) As Dictionary(Of String, StatoVoceTestataSync)
        Dim risultato As New Dictionary(Of String, StatoVoceTestataSync)(StringComparer.Ordinal)
        Try
            Using cmd As New SQLiteCommand("SELECT " & colonnaChiave & " AS Chiave, Data FROM " & tabella, conn)
                Using reader As SQLiteDataReader = cmd.ExecuteReader()
                    While reader.Read()
                        Dim voce As New StatoVoceTestataSync()
                        voce.Id = TestoReaderTestateSync(reader, "Chiave")
                        If voce.Id.Length = 0 Then Continue While
                        voce.Data = LeggiDataTestateSync(TestoReaderTestateSync(reader, "Data"))
                        voce.Eliminata = True
                        risultato(voce.Id) = voce
                    End While
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.LeggiEliminazioniTestateSync (" & If(tabella, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Sub PulisciEliminazioniTestateSync(ByVal conn As SQLiteConnection, ByVal tabella As String)
        Try
            Using cmd As New SQLiteCommand("DELETE FROM " & tabella & " WHERE Data < @limite", conn)
                cmd.Parameters.AddWithValue("@limite", FormatoDataTestateSync(DateTime.UtcNow.AddDays(-GiorniConservazioneEliminatiTestateSync)))
                cmd.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.PulisciEliminazioniTestateSync (" & If(tabella, "") & "): " & ex.ToString())
        End Try
    End Sub

    Private Function IndicizzaVociTestateSync(ByVal voci As List(Of Dictionary(Of String, Object))) As Dictionary(Of String, StatoVoceTestataSync)
        Dim risultato As New Dictionary(Of String, StatoVoceTestataSync)(StringComparer.Ordinal)
        Try
            For Each origine As Dictionary(Of String, Object) In voci
                Try
                    If origine Is Nothing Then Continue For
                    Dim voce As New StatoVoceTestataSync()
                    For Each coppia As KeyValuePair(Of String, Object) In origine
                        voce.Valori(coppia.Key) = coppia.Value
                    Next
                    voce.Id = TestoValoreTestataSync(voce, "id")
                    If voce.Id.Length = 0 Then Continue For
                    voce.Data = LeggiDataTestateSync(TestoValoreTestataSync(voce, "modificato"))
                    Dim eliminato As String = TestoValoreTestataSync(voce, "eliminato").ToLowerInvariant()
                    voce.Eliminata = eliminato = "true" OrElse eliminato = "1"
                    Dim esistente As StatoVoceTestataSync = Nothing
                    If risultato.TryGetValue(voce.Id, esistente) AndAlso esistente.Data >= voce.Data Then Continue For
                    risultato(voce.Id) = voce
                Catch exVoce As Exception
                    logger.ScriviLog("ElencoQuotidiani.IndicizzaVociTestateSync voce: " & exVoce.ToString())
                End Try
            Next
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.IndicizzaVociTestateSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function VoceTestataSync(ByVal voce As StatoVoceTestataSync) As Dictionary(Of String, Object)
        Dim risultato As New Dictionary(Of String, Object)
        Try
            If Not voce.Eliminata Then
                For Each coppia As KeyValuePair(Of String, Object) In voce.Valori
                    risultato(coppia.Key) = coppia.Value
                Next
            End If
            risultato("id") = voce.Id
            risultato("modificato") = FormatoDataTestateSync(voce.Data)
            risultato("eliminato") = voce.Eliminata
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.VoceTestataSync: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function TestoValoreTestataSync(ByVal voce As StatoVoceTestataSync, ByVal chiave As String) As String
        Try
            Dim valore As Object = Nothing
            If voce Is Nothing OrElse Not voce.Valori.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
            Return Convert.ToString(valore, Globalization.CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.TestoValoreTestataSync (" & If(chiave, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function TestoReaderTestateSync(ByVal reader As SQLiteDataReader, ByVal colonna As String) As String
        Try
            Dim indice As Integer = reader.GetOrdinal(colonna)
            If reader.IsDBNull(indice) Then Return ""
            Return Convert.ToString(reader.GetValue(indice), Globalization.CultureInfo.InvariantCulture)
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.TestoReaderTestateSync (" & If(colonna, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function LeggiDataTestateSync(ByVal testo As String) As DateTime
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return DateTime.MinValue
            Dim risultato As DateTime
            If DateTime.TryParse(testo.Trim(), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.LeggiDataTestateSync: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Private Function FormatoDataTestateSync(ByVal data As DateTime) As String
        Try
            Dim utc As DateTime = data
            If utc.Kind = DateTimeKind.Local Then utc = utc.ToUniversalTime()
            Return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Globalization.CultureInfo.InvariantCulture)
        Catch ex As Exception
            logger.ScriviLog("ElencoQuotidiani.FormatoDataTestateSync: " & ex.ToString())
            Return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Globalization.CultureInfo.InvariantCulture)
        End Try
    End Function
End Class