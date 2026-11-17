Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports UglyToad.PdfPig
Imports WGDb

' Integrazione con i servizi di intelligenza artificiale per il riassunto di testi e file. Gestisce la comunicazione con diversi
' provider.
Public Class WGAi

    Public Enum ProviderAi
        Gemini
        Groq
        Ovh
        Horde
    End Enum

    Private Shared log As New Logger()

    Private Const NumeroMassimoTentativi As Integer = 8
    Private Const AttesaMassimaTraTentativiSecondi As Integer = 30
    Private Const LunghezzaMassimaTestoAllegatoGroq As Integer = 16000
    Private Const LunghezzaMassimaDomandaGroq As Integer = 16000
    Private Const MassimoParoleBlocco As Integer = 6000
    Private Const MassimoParoleBloccoGroq As Integer = 2200
    Private Const ArticoliMassimiResocontoGroq As Integer = 12
    Private Const LunghezzaMassimaArticoloGroq As Integer = 6000
    Public Const MassimoParoleBloccoTts As Integer = 700

    Private Const MassimoRichiesteTtsAlMinuto As Integer = 8
    Private Shared _tempiRichiesteTtsGemini As New Queue(Of DateTime)
    Private Shared _lockLimiteTtsGemini As New Object()

    Private Shared Function EStatusTemporaneo(codiceStato As Integer) As Boolean
        Select Case codiceStato
            Case 408, 429, 500, 502, 503, 504
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Shared Function EStatusPayloadRifiutato(codiceStato As Integer) As Boolean
        Return codiceStato = 413 OrElse codiceStato = 400
    End Function

    Private Class EsitoAi
        Public Testo As String
        Public Errore As String
        Public CodiceHttp As Integer
        Public PayloadRifiutato As Boolean

        Public Function ComeTupla() As Tuple(Of String, String)
            Return New Tuple(Of String, String)(Testo, Errore)
        End Function
    End Class

    Private Class EsitoCaricamentoFile
        Public UriFile As String
        Public Mime As String
        Public CodiceHttp As Integer
        Public PayloadRifiutato As Boolean
        Public Errore As String
    End Class

    Private Shared Function EStatusPermanenteAutenticazione(codiceStato As Integer) As Boolean
        Return codiceStato = 401 OrElse codiceStato = 403
    End Function

    Private Shared Function DescriviErrorePermanente(nomeProvider As String, codiceStato As Integer) As String
        If EStatusPermanenteAutenticazione(codiceStato) Then
            Return "Chiave API di " & nomeProvider & " non valida o senza i permessi necessari (HTTP " & codiceStato.ToString() & ")"
        End If
        If codiceStato = 400 Then
            Return nomeProvider & " ha rifiutato la richiesta: il testo potrebbe contenere qualcosa di non supportato (HTTP 400)"
        End If
        Return "Errore da " & nomeProvider & ": HTTP " & codiceStato.ToString()
    End Function

    Private Shared Function AttesaBackoffSecondi(tentativo As Integer) As Integer
        Return Math.Min(CInt(Math.Pow(2, tentativo)), AttesaMassimaTraTentativiSecondi)
    End Function

    Private Shared Sub AttendiLimiteRichiesteTts(coda As Queue(Of DateTime), lockOggetto As Object)
        Try
            Do
                Dim daAttendere As TimeSpan = TimeSpan.Zero
                SyncLock lockOggetto
                    Dim adesso As DateTime = DateTime.UtcNow
                    Do While coda.Count > 0 AndAlso (adesso - coda.Peek()).TotalSeconds >= 60
                        coda.Dequeue()
                    Loop
                    If coda.Count < MassimoRichiesteTtsAlMinuto Then
                        coda.Enqueue(adesso)
                        Return
                    End If
                    daAttendere = TimeSpan.FromSeconds(60) - (adesso - coda.Peek())
                End SyncLock
                If daAttendere > TimeSpan.Zero Then Thread.Sleep(daAttendere)
            Loop
        Catch ex As Exception
            log.ScriviLog($"WGAi.AttendiLimiteRichiesteTts: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Const ChiaveConfigDataRichiesteGemini As String = "gemini_richieste_data"
    Private Const ChiaveConfigConteggioRichiesteGemini As String = "gemini_richieste_conteggio"
    Private Const LimiteStimatoRichiesteGeminiAlMinuto As Integer = 10
    Private Const LimiteStimatoRichiesteGeminiAlGiorno As Integer = 500

    Private Shared _richiesteGeminiUltimoMinuto As New Queue(Of DateTime)
    Private Shared _lockConteggioGemini As New Object()

    Private Shared _lockStatoGroq As New Object()
    Private Shared _groqLimiteRichieste As Integer = 0
    Private Shared _groqRichiesteRimanenti As Integer = 0
    Private Shared _groqLimiteToken As Integer = 0
    Private Shared _groqTokenRimanenti As Integer = 0
    Private Shared _groqStatoAggiornato As Boolean = False

    Public Shared Sub RegistraRichiestaGemini()
        Try
            SyncLock _lockConteggioGemini
                Dim adesso As DateTime = DateTime.UtcNow
                _richiesteGeminiUltimoMinuto.Enqueue(adesso)
                Do While _richiesteGeminiUltimoMinuto.Count > 0 AndAlso (adesso - _richiesteGeminiUltimoMinuto.Peek()).TotalSeconds >= 60
                    _richiesteGeminiUltimoMinuto.Dequeue()
                Loop
            End SyncLock

            Dim cfg As New WGConfig()
            Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
            Dim dataSalvata As String = cfg.LeggiValore(ChiaveConfigDataRichiesteGemini)
            Dim conteggioOggi As Integer = 0
            If dataSalvata = oggi Then
                Integer.TryParse(cfg.LeggiValore(ChiaveConfigConteggioRichiesteGemini), conteggioOggi)
            End If
            conteggioOggi += 1

            If Not cfg.EsisteId(ChiaveConfigDataRichiesteGemini) Then
                cfg.AggiungiValore(ChiaveConfigDataRichiesteGemini, oggi)
            Else
                cfg.ModificaValore(ChiaveConfigDataRichiesteGemini, oggi)
            End If
            If Not cfg.EsisteId(ChiaveConfigConteggioRichiesteGemini) Then
                cfg.AggiungiValore(ChiaveConfigConteggioRichiesteGemini, conteggioOggi.ToString())
            Else
                cfg.ModificaValore(ChiaveConfigConteggioRichiesteGemini, conteggioOggi.ToString())
            End If
        Catch ex As Exception
            log.ScriviLog($"WGAi.RegistraRichiestaGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function OttieniStatoRichiesteGemini() As Tuple(Of Integer, Integer, Integer, Integer)
        Try
            Dim usateUltimoMinuto As Integer
            SyncLock _lockConteggioGemini
                Dim adesso As DateTime = DateTime.UtcNow
                Do While _richiesteGeminiUltimoMinuto.Count > 0 AndAlso (adesso - _richiesteGeminiUltimoMinuto.Peek()).TotalSeconds >= 60
                    _richiesteGeminiUltimoMinuto.Dequeue()
                Loop
                usateUltimoMinuto = _richiesteGeminiUltimoMinuto.Count
            End SyncLock

            Dim cfg As New WGConfig()
            Dim oggi As String = DateTime.Now.ToString("yyyy-MM-dd")
            Dim dataSalvata As String = cfg.LeggiValore(ChiaveConfigDataRichiesteGemini)
            Dim usateOggi As Integer = 0
            If dataSalvata = oggi Then
                Integer.TryParse(cfg.LeggiValore(ChiaveConfigConteggioRichiesteGemini), usateOggi)
            End If

            Return New Tuple(Of Integer, Integer, Integer, Integer)(usateUltimoMinuto, LimiteStimatoRichiesteGeminiAlMinuto, usateOggi, LimiteStimatoRichiesteGeminiAlGiorno)
        Catch ex As Exception
            log.ScriviLog($"WGAi.OttieniStatoRichiesteGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of Integer, Integer, Integer, Integer)(0, LimiteStimatoRichiesteGeminiAlMinuto, 0, LimiteStimatoRichiesteGeminiAlGiorno)
        End Try
    End Function

    Private Shared Function OttieniValoreHeader(risposta As HttpResponseMessage, nomeHeader As String, ByRef valore As String) As Boolean
        Try
            Dim valori As IEnumerable(Of String) = Nothing
            If risposta.Headers.TryGetValues(nomeHeader, valori) Then
                valore = valori.FirstOrDefault()
                Return Not String.IsNullOrEmpty(valore)
            End If
            valore = Nothing
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGAi.OttieniValoreHeader: {ex.Message}")
            valore = Nothing
            Return False
        End Try
    End Function

    Public Shared Sub AggiornaStatoGroqDaRisposta(risposta As HttpResponseMessage)
        Try
            Dim valore As String = Nothing
            SyncLock _lockStatoGroq
                If OttieniValoreHeader(risposta, "x-ratelimit-limit-requests", valore) Then Integer.TryParse(valore, _groqLimiteRichieste)
                If OttieniValoreHeader(risposta, "x-ratelimit-remaining-requests", valore) Then Integer.TryParse(valore, _groqRichiesteRimanenti)
                If OttieniValoreHeader(risposta, "x-ratelimit-limit-tokens", valore) Then Integer.TryParse(valore, _groqLimiteToken)
                If OttieniValoreHeader(risposta, "x-ratelimit-remaining-tokens", valore) Then Integer.TryParse(valore, _groqTokenRimanenti)
                _groqStatoAggiornato = True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGAi.AggiornaStatoGroqDaRisposta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function OttieniStatoRichiesteGroq() As Tuple(Of Integer, Integer, Integer, Integer, Boolean)
        Try
            SyncLock _lockStatoGroq
                Return New Tuple(Of Integer, Integer, Integer, Integer, Boolean)(_groqRichiesteRimanenti, _groqLimiteRichieste, _groqTokenRimanenti, _groqLimiteToken, _groqStatoAggiornato)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGAi.OttieniStatoRichiesteGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of Integer, Integer, Integer, Integer, Boolean)(0, 0, 0, 0, False)
        End Try
    End Function

    Public Shared Function DescrizioneStatoRichieste() As String
        Try
            Dim costruttore As New StringBuilder()

            Dim statoGemini As Tuple(Of Integer, Integer, Integer, Integer) = OttieniStatoRichiesteGemini()
            costruttore.AppendLine("Gemini (conteggiate da WinGabriel, stima basata sui limiti tipici del livello gratuito):")
            costruttore.AppendLine("Nell'ultimo minuto: " & statoGemini.Item1.ToString() & " di circa " & statoGemini.Item2.ToString() & " richieste")
            costruttore.AppendLine("Oggi: " & statoGemini.Item3.ToString() & " di circa " & statoGemini.Item4.ToString() & " richieste")
            costruttore.AppendLine()

            Dim statoGroq As Tuple(Of Integer, Integer, Integer, Integer, Boolean) = OttieniStatoRichiesteGroq()
            costruttore.AppendLine("Groq (dati reali forniti da Groq nell'ultima risposta ricevuta):")
            If statoGroq.Item5 Then
                costruttore.AppendLine("Richieste rimanenti: " & statoGroq.Item1.ToString() & " di " & statoGroq.Item2.ToString())
                costruttore.AppendLine("Token rimanenti: " & statoGroq.Item3.ToString() & " di " & statoGroq.Item4.ToString())
            Else
                costruttore.AppendLine("Nessuna richiesta ancora effettuata in questa sessione, dato non disponibile.")
            End If

            Return costruttore.ToString().TrimEnd()
        Catch ex As Exception
            log.ScriviLog($"WGAi.DescrizioneStatoRichieste: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Impossibile recuperare lo stato delle richieste."
        End Try
    End Function

    Private Const ChiaveConfigApiGemini As String = "gemini_api_key_enc"
    Private Const ChiaveConfigApiGroq As String = "groq_api_key_enc"
    Private Const ChiaveConfigProviderPreferito As String = "ai_provider_preferito"
    Private Const PassphraseCifraturaChiavi As String = "WinGabriel-Dettatura-Chiavi-Locali-Rev1"

    Private Const ModelloGeminiPredefinito As String = "gemini-3.5-flash-lite"
    Private Const ModelloGroqPredefinito As String = "openai/gpt-oss-120b"
    ' OVHcloud AI Endpoints: API compatibile OpenAI, gratuita e senza chiave (con un limite di richieste al minuto).
    ' Ospita in Europa i modelli europei, per esempio quelli di Mistral.
    Private Const UrlBaseOvh As String = "https://oai.endpoints.kepler.ai.cloud.ovh.net/v1"
    Private Const ModelloOvhPredefinito As String = "Mistral-Small-3.2-24B-Instruct-2506"
    Private Const CaratteriBloccoRiscritturaOvh As Integer = 7000
    Private Const LunghezzaMassimaTestoAllegatoOvh As Integer = 16000
    Private Const LunghezzaMassimaDomandaOvh As Integer = 16000
    ' AI Horde: rete di volontari che mettono a disposizione i propri computer. API compatibile OpenAI, gratuita,
    ' usata con la chiave anonima. I modelli cambiano di continuo: l'elenco va letto ogni volta.
    Private Const UrlBaseHorde As String = "https://oai.aihorde.net/v1"
    Private Const ChiaveAnonimaHorde As String = "0000000000"
    Public Const ModelloHordeAutomatico As String = "Automatico"
    Private Const CaratteriBloccoRiscritturaHorde As Integer = 3000
    Private Const LunghezzaMassimaTestoAllegatoHorde As Integer = 12000
    Private Const NumeroMassimoTentativiHorde As Integer = 4
    Private Const SecondiValiditaElencoHorde As Integer = 60
    Private Shared ReadOnly FamiglieModelliEuropei As String() = {"mistral", "mixtral", "magistral", "ministral", "codestral", "pixtral", "devstral", "eurollm", "apertus", "teuken", "pharia", "velvet", "minerva", "salamandra", "occiglot", "lucie", "croissant"}
    Private Const ModelloGeminiTts As String = "gemini-3.1-flash-tts-preview"
    Private Const VoceGeminiTts As String = "Kore"
    Private Const LunghezzaMassimaTestoEmail As Integer = 60000

    Private Shared Function DecifraChiaveApi(testoCifratoBase64 As String) As String
        Try
            If String.IsNullOrWhiteSpace(testoCifratoBase64) Then Return ""
            If WGDb.WGCifraturaNativa.EProtetto(testoCifratoBase64) Then Return WGDb.WGCifraturaNativa.RivelaTesto("chiavi-api", testoCifratoBase64)

            Dim datiCompleti() As Byte = Convert.FromBase64String(testoCifratoBase64)
            If datiCompleti.Length < 32 Then Return ""

            Dim sale(15) As Byte
            Dim vettoreInizializzazione(15) As Byte
            Array.Copy(datiCompleti, 0, sale, 0, 16)
            Array.Copy(datiCompleti, 16, vettoreInizializzazione, 0, 16)

            Dim lunghezzaDatiCifrati As Integer = datiCompleti.Length - 32
            Dim datiCifrati(lunghezzaDatiCifrati - 1) As Byte
            Array.Copy(datiCompleti, 32, datiCifrati, 0, lunghezzaDatiCifrati)

            Using derivatore As New Rfc2898DeriveBytes(PassphraseCifraturaChiavi, sale, 100000, HashAlgorithmName.SHA256)
                Dim chiaveDerivata() As Byte = derivatore.GetBytes(32)

                Using aes As Aes = Aes.Create()
                    aes.Key = chiaveDerivata
                    aes.IV = vettoreInizializzazione
                    aes.Mode = CipherMode.CBC
                    aes.Padding = PaddingMode.PKCS7

                    Using decrittografo As ICryptoTransform = aes.CreateDecryptor()
                        Using memoria As New MemoryStream(datiCifrati)
                            Using flussoChiaro As New CryptoStream(memoria, decrittografo, CryptoStreamMode.Read)
                                Using lettore As New StreamReader(flussoChiaro, Encoding.UTF8)
                                    Return lettore.ReadToEnd()
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.DecifraChiaveApi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function LeggiChiaveApiGemini() As String
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = cfg.LeggiValore(ChiaveConfigApiGemini)
            If String.IsNullOrWhiteSpace(valoreCifrato) Then Return ""
            Return DecifraChiaveApi(valoreCifrato)
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiChiaveApiGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function LeggiChiaveApiGroq() As String
        Try
            Dim cfg As New WGConfig()
            Dim valoreCifrato As String = cfg.LeggiValore(ChiaveConfigApiGroq)
            If String.IsNullOrWhiteSpace(valoreCifrato) Then Return ""
            Return DecifraChiaveApi(valoreCifrato)
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiChiaveApiGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Function ChiaveApiConfigurata(provider As ProviderAi) As Boolean
        Try
            If provider = ProviderAi.Ovh Then Return WGDettatura.ChiaveApiOvhConfigurata()
            If provider = ProviderAi.Horde Then Return WGDettatura.ChiaveApiHordeConfigurata()
            If provider = ProviderAi.Groq Then Return Not String.IsNullOrWhiteSpace(LeggiChiaveApiGroq())
            Return Not String.IsNullOrWhiteSpace(LeggiChiaveApiGemini())
        Catch ex As Exception
            log.ScriviLog($"WGAi.ChiaveApiConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function AlmenoUnProviderConfigurato() As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(LeggiChiaveApiGemini()) OrElse Not String.IsNullOrWhiteSpace(LeggiChiaveApiGroq()) OrElse WGDettatura.ChiaveApiOvhConfigurata() OrElse WGDettatura.ChiaveApiHordeConfigurata()
        Catch ex As Exception
            log.ScriviLog($"WGAi.AlmenoUnProviderConfigurato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function LeggiProviderPreferito() As ProviderAi
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigProviderPreferito)
            If String.Equals(valore, "groq", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Groq
            If String.Equals(valore, "ovh", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Ovh
            If String.Equals(valore, "horde", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Horde
            Return ProviderAi.Gemini
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiProviderPreferito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ProviderAi.Gemini
        End Try
    End Function

    Public Shared Function NomeProviderInConfigurazione(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return "groq"
            Case ProviderAi.Ovh : Return "ovh"
            Case ProviderAi.Horde : Return "horde"
            Case Else : Return "gemini"
        End Select
    End Function

    Public Shared Function ProviderDaNomeInConfigurazione(nome As String) As ProviderAi
        If String.Equals(nome, "groq", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Groq
        If String.Equals(nome, "ovh", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Ovh
        If String.Equals(nome, "horde", StringComparison.OrdinalIgnoreCase) Then Return ProviderAi.Horde
        Return ProviderAi.Gemini
    End Function

    Public Shared Function NomeProviderInChiaro(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return "Groq"
            Case ProviderAi.Ovh : Return "OVHcloud"
            Case ProviderAi.Horde : Return "AI Horde"
            Case Else : Return "Gemini"
        End Select
    End Function

    ''' <summary>Gli altri motori configurati, dopo quello indicato, nell'ordine Gemini, Groq, OVHcloud, AI Horde.</summary>
    Public Shared Function AltriProviderConfigurati(provider As ProviderAi) As List(Of ProviderAi)
        Dim elenco As New List(Of ProviderAi)()
        For Each candidato As ProviderAi In New ProviderAi() {ProviderAi.Gemini, ProviderAi.Groq, ProviderAi.Ovh, ProviderAi.Horde}
            If candidato <> provider AndAlso ChiaveApiConfigurata(candidato) Then elenco.Add(candidato)
        Next
        Return elenco
    End Function

    Private Shared Function ProviderEffettivo(providerSovrascritto As ProviderAi?) As ProviderAi
        Dim provider As ProviderAi = If(providerSovrascritto.HasValue, providerSovrascritto.Value, LeggiProviderPreferito())
        If Not ChiaveApiConfigurata(provider) Then
            Dim altri As List(Of ProviderAi) = AltriProviderConfigurati(provider)
            If altri.Count > 0 Then Return altri(0)
            provider = If(provider = ProviderAi.Groq, ProviderAi.Gemini, ProviderAi.Groq)
        End If
        Return provider
    End Function

    Private Shared Function ModelloChatPredefinito(provider As ProviderAi) As String
        If provider = ProviderAi.Ovh Then Return ModelloOvhPredefinito
        If provider = ProviderAi.Horde Then Return ModelloHordeAutomatico
        Return If(provider = ProviderAi.Groq, ModelloGroqPredefinito, ModelloGeminiPredefinito)
    End Function

    Private Shared Function MimeTypeDaEstensione(percorso As String) As String
        Try
            Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
            Select Case estensione
                Case ".pdf" : Return "application/pdf"
                Case ".txt" : Return "text/plain"
                Case ".md" : Return "text/markdown"
                Case ".csv" : Return "text/csv"
                Case ".json" : Return "application/json"
                Case ".xml" : Return "application/xml"
                Case ".html", ".htm" : Return "text/html"
                Case ".doc" : Return "application/msword"
                Case ".docx" : Return "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                Case ".png" : Return "image/png"
                Case ".jpg", ".jpeg" : Return "image/jpeg"
                Case ".gif" : Return "image/gif"
                Case ".webp" : Return "image/webp"
                Case ".mp3" : Return "audio/mpeg"
                Case ".wav" : Return "audio/wav"
                Case ".mp4" : Return "video/mp4"
                Case Else : Return "application/octet-stream"
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGAi.MimeTypeDaEstensione: {ex.Message}")
            Return "application/octet-stream"
        End Try
    End Function

    Private Shared Function EEstensioneTesto(percorso As String) As Boolean
        Try
            Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
            Dim estensioniTesto As String() = {".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".htm", ".vb", ".cs", ".py", ".js", ".css", ".sql", ".ini", ".yaml", ".yml"}
            Return estensioniTesto.Contains(estensione)
        Catch ex As Exception
            log.ScriviLog($"WGAi.EEstensioneTesto: {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Function DividiInFrasi(testo As String) As List(Of String)
        Dim frasi As New List(Of String)
        Try
            Dim parti As String() = Regex.Split(testo, "(?<=[.!?])\s+")
            For Each parte As String In parti
                If Not String.IsNullOrWhiteSpace(parte) Then frasi.Add(parte & " ")
            Next
            Return frasi
        Catch ex As Exception
            log.ScriviLog($"WGAi.DividiInFrasi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            frasi.Clear()
            frasi.Add(testo)
            Return frasi
        End Try
    End Function

    Public Shared Function DividiInBlocchi(testo As String, Optional maxParoleBlocco As Integer = MassimoParoleBlocco) As List(Of String)
        Dim blocchi As New List(Of String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return blocchi

            Dim paroleTotali As Integer = testo.Split({" "c, ControlChars.Lf, ControlChars.Cr, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries).Length
            If paroleTotali <= maxParoleBlocco Then
                blocchi.Add(testo)
                Return blocchi
            End If

            Dim frasi As List(Of String) = DividiInFrasi(testo)
            Dim blocco As New StringBuilder()
            Dim paroleBlocco As Integer = 0

            For Each frase As String In frasi
                Dim paroleFrase As Integer = frase.Split({" "c}, StringSplitOptions.RemoveEmptyEntries).Length

                If paroleBlocco > 0 AndAlso paroleBlocco + paroleFrase > maxParoleBlocco Then
                    blocchi.Add(blocco.ToString().Trim())
                    blocco.Clear()
                    paroleBlocco = 0
                End If

                blocco.Append(frase)
                paroleBlocco += paroleFrase

                If paroleFrase > maxParoleBlocco Then
                    blocchi.Add(blocco.ToString().Trim())
                    blocco.Clear()
                    paroleBlocco = 0
                End If
            Next

            If blocco.Length > 0 Then blocchi.Add(blocco.ToString().Trim())
            Return blocchi
        Catch ex As Exception
            log.ScriviLog($"WGAi.DividiInBlocchi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            blocchi.Clear()
            blocchi.Add(testo)
            Return blocchi
        End Try
    End Function

    Public Shared Async Function OttieniRispostaAsync(domanda As String, Optional documenti As List(Of String) = Nothing, Optional providerSovrascritto As ProviderAi? = Nothing, Optional modelloSovrascritto As String = Nothing, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(domanda) Then Return New Tuple(Of String, String)(Nothing, "La domanda è vuota")
            If Not AlmenoUnProviderConfigurato() Then Return New Tuple(Of String, String)(Nothing, "Nessuna chiave API configurata")

            Dim provider As ProviderAi = ProviderEffettivo(providerSovrascritto)
            Dim modello As String = If(Not String.IsNullOrWhiteSpace(modelloSovrascritto), modelloSovrascritto, ModelloChatPredefinito(provider))

            Dim fileValidi As New List(Of String)
            Dim soloFileDiTesto As Boolean = True
            If documenti IsNot Nothing Then
                For Each percorso As String In documenti
                    If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Continue For
                    fileValidi.Add(percorso)
                    If Not EEstensioneTesto(percorso) Then soloFileDiTesto = False
                Next
            End If

            Dim risultato As Tuple(Of String, String) = Await InviaAlProviderAsync(provider, modello, domanda, fileValidi, testoFormattato)
            If RisultatoValido(risultato) Then Return risultato
            If providerSovrascritto.HasValue OrElse Not String.IsNullOrWhiteSpace(modelloSovrascritto) Then Return risultato

            For Each altroProvider As ProviderAi In AltriProviderConfigurati(provider)
                If altroProvider <> ProviderAi.Gemini AndAlso Not soloFileDiTesto Then Continue For
                Dim risultatoAltro As Tuple(Of String, String) = Await InviaAlProviderAsync(altroProvider, ModelloChatPredefinito(altroProvider), domanda, fileValidi, testoFormattato)
                If RisultatoValido(risultatoAltro) Then Return risultatoAltro
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGAi.OttieniRispostaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la richiesta: " & ex.Message)
        End Try
    End Function

    Private Shared Function RisultatoValido(risultato As Tuple(Of String, String)) As Boolean
        Try
            Return risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item1)
        Catch ex As Exception
            log.ScriviLog($"WGAi.RisultatoValido: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Const ChiaveConfigModelloRiscrittura As String = "ai_modello_riscrittura"
    Private Const CaratteriBloccoRiscritturaGemini As Integer = 14000
    Private Const CaratteriBloccoRiscritturaGroq As Integer = 7000
    Private Const MiliardiParametriMinimiRiscrittura As Double = 12

    Public Shared Function EFileRiscrivibile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If EEstensioneTesto(percorso) Then Return True
            Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
            Return estensione = ".php" OrElse estensione = ".asp" OrElse estensione = ".aspx"
        Catch ex As Exception
            log.ScriviLog($"WGAi.EFileRiscrivibile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function LeggiModelloRiscrittura(ByRef provider As ProviderAi, ByRef modello As String) As Boolean
        modello = ""
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigModelloRiscrittura)
            If String.IsNullOrWhiteSpace(valore) Then Return False
            Dim posizione As Integer = valore.IndexOf("|"c)
            If posizione <= 0 Then Return False
            Dim nomeProvider As String = valore.Substring(0, posizione)
            Dim nomeModello As String = valore.Substring(posizione + 1).Trim()
            If nomeModello = "" Then Return False
            Dim providerLetto As ProviderAi = ProviderDaNomeInConfigurazione(nomeProvider)
            If Not ChiaveApiConfigurata(providerLetto) Then Return False
            provider = providerLetto
            modello = nomeModello
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiModelloRiscrittura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            modello = ""
            Return False
        End Try
    End Function

    Public Shared Sub SalvaModelloRiscrittura(provider As ProviderAi, modello As String)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = "auto"
            If Not String.IsNullOrWhiteSpace(modello) Then valore = NomeProviderInConfigurazione(provider) & "|" & modello.Trim()
            If Not cfg.EsisteId(ChiaveConfigModelloRiscrittura) Then
                cfg.AggiungiValore(ChiaveConfigModelloRiscrittura, valore)
            Else
                cfg.ModificaValore(ChiaveConfigModelloRiscrittura, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGAi.SalvaModelloRiscrittura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Async Function ElencaModelliAsync(provider As ProviderAi, Optional soloUtiliPerRiscrittura As Boolean = True) As Task(Of List(Of String))
        Dim candidati As New List(Of String)()
        Try
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(30)
                If provider = ProviderAi.Horde Then
                    For Each modelloHorde As ModelloHorde In Await ModelliHordeAsync()
                        candidati.Add(modelloHorde.Id)
                    Next
                ElseIf provider = ProviderAi.Ovh Then
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, UrlBaseOvh & "/models")
                        Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                            Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                            If Not risposta.IsSuccessStatusCode Then
                                log.ScriviLog($"WGAi.ElencaModelliAsync: OVHcloud HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                                Return candidati
                            End If
                            Dim dati As JArray = TryCast(JObject.Parse(corpo)("data"), JArray)
                            If dati Is Nothing Then Return candidati
                            For Each voce As JToken In dati
                                Dim gettoneId As JToken = voce("id")
                                If gettoneId Is Nothing Then Continue For
                                Dim id As String = gettoneId.ToString()
                                If ModelloNonAdattoAlTesto(id) Then Continue For
                                candidati.Add(id)
                            Next
                        End Using
                    End Using
                    ' Si propongono i modelli europei (Mistral e simili); se l'elenco non ne contiene, tutti quelli di testo.
                    Dim europei As List(Of String) = candidati.Where(Function(m) ModelloEuropeo(m)).ToList()
                    If europei.Count > 0 Then
                        candidati.Clear()
                        candidati.AddRange(europei)
                    End If
                ElseIf provider = ProviderAi.Groq Then
                    Dim chiaveGroq As String = LeggiChiaveApiGroq()
                    If String.IsNullOrWhiteSpace(chiaveGroq) Then Return candidati
                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models")
                        richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", chiaveGroq)
                        Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                            Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                            If Not risposta.IsSuccessStatusCode Then
                                log.ScriviLog($"WGAi.ElencaModelliAsync: Groq HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                                Return candidati
                            End If
                            Dim dati As JArray = TryCast(JObject.Parse(corpo)("data"), JArray)
                            If dati Is Nothing Then Return candidati
                            For Each voce As JToken In dati
                                Dim gettoneId As JToken = voce("id")
                                If gettoneId Is Nothing Then Continue For
                                Dim id As String = gettoneId.ToString()
                                If ModelloNonAdattoAlTesto(id) Then Continue For
                                candidati.Add(id)
                            Next
                        End Using
                    End Using
                Else
                    Dim chiaveGemini As String = LeggiChiaveApiGemini()
                    If String.IsNullOrWhiteSpace(chiaveGemini) Then Return candidati
                    Dim gettonePagina As String = ""
                    For pagina As Integer = 1 To 6
                        Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200"
                        If gettonePagina <> "" Then url &= "&pageToken=" & Uri.EscapeDataString(gettonePagina)
                        Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                            richiesta.Headers.Add("x-goog-api-key", chiaveGemini)
                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                                Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                                If Not risposta.IsSuccessStatusCode Then
                                    log.ScriviLog($"WGAi.ElencaModelliAsync: Gemini HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                                    Exit For
                                End If
                                Dim radice As JObject = JObject.Parse(corpo)
                                Dim modelli As JArray = TryCast(radice("models"), JArray)
                                If modelli IsNot Nothing Then
                                    For Each voce As JToken In modelli
                                        Dim gettoneNome As JToken = voce("name")
                                        If gettoneNome Is Nothing Then Continue For
                                        Dim nome As String = gettoneNome.ToString()
                                        If nome.StartsWith("models/", StringComparison.OrdinalIgnoreCase) Then nome = nome.Substring(7)
                                        Dim metodi As JArray = TryCast(voce("supportedGenerationMethods"), JArray)
                                        If metodi IsNot Nothing AndAlso Not metodi.Any(Function(m) String.Equals(m.ToString(), "generateContent", StringComparison.OrdinalIgnoreCase)) Then Continue For
                                        If ModelloNonAdattoAlTesto(nome) Then Continue For
                                        candidati.Add(nome)
                                    Next
                                End If
                                Dim gettoneSuccessivo As JToken = radice("nextPageToken")
                                gettonePagina = If(gettoneSuccessivo Is Nothing, "", gettoneSuccessivo.ToString())
                            End Using
                        End Using
                        If gettonePagina = "" Then Exit For
                    Next
                End If
            End Using

            Dim tutti As List(Of String) = candidati.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Function(m) m, StringComparer.OrdinalIgnoreCase).ToList()
            If Not soloUtiliPerRiscrittura Then Return tutti

            Dim utili As List(Of String) = tutti.Where(Function(m) ModelloUtilePerRiscrittura(provider, m)).ToList()
            If utili.Count = 0 Then Return tutti
            Return utili
        Catch ex As Exception
            log.ScriviLog($"WGAi.ElencaModelliAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)()
        End Try
    End Function

    Public Shared Function ModelloEuropeo(nome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return False
            Dim minuscolo As String = nome.ToLowerInvariant()
            Return FamiglieModelliEuropei.Any(Function(f) minuscolo.Contains(f))
        Catch ex As Exception
            log.ScriviLog($"WGAi.ModelloEuropeo: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function ModelloNonAdattoAlTesto(nome As String) As Boolean
        Try
            Dim minuscolo As String = nome.ToLowerInvariant()
            Dim esclusi As String() = {"tts", "image", "embed", "aqa", "audio", "live", "imagen", "veo", "robotics", "computer-use", "whisper", "guard", "orpheus", "playai", "transcribe"}
            Return esclusi.Any(Function(e) minuscolo.Contains(e))
        Catch ex As Exception
            log.ScriviLog($"WGAi.ModelloNonAdattoAlTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function ModelloUtilePerRiscrittura(provider As ProviderAi, nome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return False
            Dim minuscolo As String = nome.ToLowerInvariant()

            Dim dimensione As Match = Regex.Match(minuscolo, "-(\d+(?:\.\d+)?)b(?=-|$)")
            If dimensione.Success Then
                Dim miliardi As Double = 0
                If Double.TryParse(dimensione.Groups(1).Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, miliardi) AndAlso miliardi < MiliardiParametriMinimiRiscrittura Then Return False
            End If

            If provider = ProviderAi.Gemini Then
                If Not minuscolo.StartsWith("gemini-") Then Return False
                If Not (minuscolo.Contains("flash") OrElse minuscolo.Contains("pro")) Then Return False
                Dim esclusiGemini As String() = {"exp", "learnlm", "deep-research", "customtools", "nano", "thinking", "vision"}
                If esclusiGemini.Any(Function(e) minuscolo.Contains(e)) Then Return False
                If Regex.IsMatch(minuscolo, "-\d{3,4}$") Then Return False
                If Regex.IsMatch(minuscolo, "-\d{2}-\d{2,4}$") Then Return False
                If Regex.IsMatch(minuscolo, "-preview-\d") Then Return False
                Return True
            End If

            If provider = ProviderAi.Ovh Then Return ModelloEuropeo(nome)
            ' AI Horde: solo i modelli di cui si conosce la grandezza (i più piccoli sono già stati scartati qui sopra).
            If provider = ProviderAi.Horde Then Return dimensione.Success

            Dim esclusiGroq As String() = {"safeguard", "compound", "vision", "guard"}
            If esclusiGroq.Any(Function(e) minuscolo.Contains(e)) Then Return False
            Dim famiglieGroq As String() = {"llama", "gpt-oss", "qwen", "kimi", "deepseek", "mixtral", "mistral", "gemma"}
            Return famiglieGroq.Any(Function(f) minuscolo.Contains(f))
        Catch ex As Exception
            log.ScriviLog($"WGAi.ModelloUtilePerRiscrittura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return True
        End Try
    End Function

    Public Shared Function CaratteriPerBloccoRiscrittura(providerSovrascritto As ProviderAi?) As Integer
        Try
            Select Case ProviderEffettivo(providerSovrascritto)
                Case ProviderAi.Groq : Return CaratteriBloccoRiscritturaGroq
                Case ProviderAi.Ovh : Return CaratteriBloccoRiscritturaOvh
                Case ProviderAi.Horde : Return CaratteriBloccoRiscritturaHorde
                Case Else : Return CaratteriBloccoRiscritturaGemini
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGAi.CaratteriPerBloccoRiscrittura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return CaratteriBloccoRiscritturaGroq
        End Try
    End Function

    Public Shared Function DividiPerRiscrittura(testo As String, massimoCaratteri As Integer) As List(Of String)
        Dim blocchi As New List(Of String)()
        Try
            If String.IsNullOrEmpty(testo) Then Return blocchi
            If massimoCaratteri < 500 Then massimoCaratteri = 500
            If testo.Length <= massimoCaratteri Then
                blocchi.Add(testo)
                Return blocchi
            End If
            Dim corrente As New StringBuilder()
            Dim posizione As Integer = 0
            While posizione < testo.Length
                Dim fineRiga As Integer = testo.IndexOf(ControlChars.Lf, posizione)
                Dim riga As String
                If fineRiga < 0 Then
                    riga = testo.Substring(posizione)
                    posizione = testo.Length
                Else
                    riga = testo.Substring(posizione, fineRiga - posizione + 1)
                    posizione = fineRiga + 1
                End If
                While riga.Length > massimoCaratteri
                    Dim taglio As Integer = riga.LastIndexOf(" "c, massimoCaratteri - 1)
                    If taglio < massimoCaratteri \ 2 Then taglio = massimoCaratteri - 1
                    If corrente.Length > 0 Then
                        blocchi.Add(corrente.ToString())
                        corrente.Clear()
                    End If
                    blocchi.Add(riga.Substring(0, taglio + 1))
                    riga = riga.Substring(taglio + 1)
                End While
                If corrente.Length > 0 AndAlso corrente.Length + riga.Length > massimoCaratteri Then
                    blocchi.Add(corrente.ToString())
                    corrente.Clear()
                End If
                corrente.Append(riga)
            End While
            If corrente.Length > 0 Then blocchi.Add(corrente.ToString())
            Return blocchi
        Catch ex As Exception
            log.ScriviLog($"WGAi.DividiPerRiscrittura: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            blocchi.Clear()
            blocchi.Add(testo)
            Return blocchi
        End Try
    End Function

    Public Shared Function LeggiTestoFile(percorso As String, ByRef codifica As Encoding) As String
        Try
            Dim dati As Byte() = File.ReadAllBytes(percorso)
            If dati.Length >= 3 AndAlso dati(0) = &HEF AndAlso dati(1) = &HBB AndAlso dati(2) = &HBF Then
                codifica = New UTF8Encoding(True)
                Return Encoding.UTF8.GetString(dati, 3, dati.Length - 3)
            End If
            If dati.Length >= 2 AndAlso dati(0) = &HFF AndAlso dati(1) = &HFE Then
                codifica = New UnicodeEncoding(False, True)
                Return Encoding.Unicode.GetString(dati, 2, dati.Length - 2)
            End If
            If dati.Length >= 2 AndAlso dati(0) = &HFE AndAlso dati(1) = &HFF Then
                codifica = New UnicodeEncoding(True, True)
                Return Encoding.BigEndianUnicode.GetString(dati, 2, dati.Length - 2)
            End If
            Try
                Dim testo As String = New UTF8Encoding(False, True).GetString(dati)
                codifica = New UTF8Encoding(False)
                Return testo
            Catch exCodifica As DecoderFallbackException
                codifica = Encoding.GetEncoding(1252)
                Return codifica.GetString(dati)
            End Try
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiTestoFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw
        End Try
    End Function

    Public Shared Sub ScriviTestoFile(percorso As String, testo As String, codifica As Encoding)
        Dim percorsoTemporaneo As String = ""
        Try
            Dim scelta As Encoding = codifica
            If scelta Is Nothing Then scelta = New UTF8Encoding(True)
            If Not (TypeOf scelta Is UTF8Encoding OrElse TypeOf scelta Is UnicodeEncoding) Then
                Dim prova As Byte() = scelta.GetBytes(testo)
                If Not String.Equals(scelta.GetString(prova), testo, StringComparison.Ordinal) Then scelta = New UTF8Encoding(True)
            End If
            Dim preambolo As Byte() = scelta.GetPreamble()
            Dim contenuto As Byte() = scelta.GetBytes(testo)
            Dim tutto(preambolo.Length + contenuto.Length - 1) As Byte
            Buffer.BlockCopy(preambolo, 0, tutto, 0, preambolo.Length)
            Buffer.BlockCopy(contenuto, 0, tutto, preambolo.Length, contenuto.Length)
            percorsoTemporaneo = percorso & "." & Guid.NewGuid().ToString("N").Substring(0, 8) & ".wgtmp"
            File.WriteAllBytes(percorsoTemporaneo, tutto)
            Try
                File.Replace(percorsoTemporaneo, percorso, Nothing)
            Catch exSostituzione As Exception
                log.ScriviLog($"WGAi.ScriviTestoFile (sostituzione): {exSostituzione.Message}{Environment.NewLine}{exSostituzione.StackTrace}")
                File.Copy(percorsoTemporaneo, percorso, True)
            End Try
        Catch ex As Exception
            log.ScriviLog($"WGAi.ScriviTestoFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw
        Finally
            Try
                If percorsoTemporaneo <> "" AndAlso File.Exists(percorsoTemporaneo) Then File.Delete(percorsoTemporaneo)
            Catch exPulizia As Exception
                log.ScriviLog($"WGAi.ScriviTestoFile (pulizia): {exPulizia.Message}")
            End Try
        End Try
    End Sub

    Private Shared Function PromptRiscrittura(testo As String, istruzioni As String) As String
        Dim prompt As New StringBuilder()
        prompt.Append("Riscrivi il testo compreso tra i marcatori INIZIO TESTO e FINE TESTO seguendo queste istruzioni dell'utente: ")
        prompt.Append(istruzioni.Trim())
        prompt.Append(vbLf & vbLf)
        prompt.Append("Regole: rispondi soltanto con il testo riscritto, senza introduzioni, spiegazioni, marcatori né recinzioni di codice. ")
        prompt.Append("Conserva la lingua del testo se le istruzioni non chiedono di cambiarla. ")
        prompt.Append("Conserva la struttura, gli a capo, i rientri, i nomi, i numeri, i tag, il codice e tutto ciò che le istruzioni non chiedono di modificare. ")
        prompt.Append("Il testo è una parte di un file più lungo: non aggiungere titoli, conclusioni o commenti e non togliere né spostare nulla che non vada modificato.")
        prompt.Append(vbLf & vbLf)
        prompt.Append("INIZIO TESTO" & vbLf)
        prompt.Append(testo)
        prompt.Append(vbLf & "FINE TESTO")
        Return prompt.ToString()
    End Function

    Public Shared Async Function RiscriviTestoAsync(testo As String, istruzioni As String, Optional providerSovrascritto As ProviderAi? = Nothing, Optional modelloSovrascritto As String = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return New Tuple(Of String, String)(testo, Nothing)
            If String.IsNullOrWhiteSpace(istruzioni) Then Return New Tuple(Of String, String)(Nothing, "Mancano le istruzioni per la riscrittura")
            Dim risultato As Tuple(Of String, String) = Await OttieniRispostaAsync(PromptRiscrittura(testo, istruzioni), Nothing, providerSovrascritto, modelloSovrascritto, True)
            If Not RisultatoValido(risultato) Then Return New Tuple(Of String, String)(Nothing, If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), risultato.Item2, "Nessuna risposta dall'intelligenza artificiale"))
            Dim riscritto As String = risultato.Item1
            riscritto = Regex.Replace(riscritto, "^\s*INIZIO TESTO[ \t]*\r?\n", "", RegexOptions.IgnoreCase)
            riscritto = Regex.Replace(riscritto, "\r?\n[ \t]*FINE TESTO\s*$", "", RegexOptions.IgnoreCase)
            Return New Tuple(Of String, String)(riscritto, Nothing)
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiscriviTestoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la richiesta: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function InviaAlProviderAsync(provider As ProviderAi, modello As String, domanda As String, fileValidi As List(Of String), testoFormattato As Boolean) As Task(Of Tuple(Of String, String))
        Try
            Using cts As New CancellationTokenSource()
                If provider = ProviderAi.Groq Then
                    Return Await InviaGroqUnaVoltaAsync(modello, domanda, fileValidi, cts.Token, testoFormattato)
                End If
                If provider = ProviderAi.Ovh Then
                    Return Await InviaOvhUnaVoltaAsync(modello, domanda, fileValidi, cts.Token, testoFormattato)
                End If
                If provider = ProviderAi.Horde Then
                    Return Await InviaHordeUnaVoltaAsync(modello, domanda, fileValidi, cts.Token, testoFormattato)
                End If
                Return Await InviaGeminiUnaVoltaAsync(modello, domanda, fileValidi, cts.Token, testoFormattato)
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaAlProviderAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la richiesta: " & ex.Message)
        End Try
    End Function

    Private Shared Function PromptRiassuntoCorto(Optional istruzioniAggiuntive As String = "") As String
        Dim prompt As String = "Riassumi il documento nella lingua del documento; se non è riconoscibile usa l'italiano. Copri le sezioni e il contenuto visivo rilevante, in modo chiaro e conciso."
        If Not String.IsNullOrWhiteSpace(istruzioniAggiuntive) Then prompt &= " " & istruzioniAggiuntive.Trim()
        Return prompt
    End Function

    Public Shared Async Function RiassumiTestoAsync(testo As String, Optional istruzioniAggiuntive As String = "", Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of Tuple(Of String, String))
        Dim percorsoTemporaneo As String = Nothing
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return New Tuple(Of String, String)(Nothing, "Il testo è vuoto")

            percorsoTemporaneo = Path.Combine(Path.GetTempPath(), "wgai-riassunto-" & Guid.NewGuid().ToString("N") & ".txt")
            File.WriteAllText(percorsoTemporaneo, testo, Encoding.UTF8)
            Return Await RiassumiFileAsync(percorsoTemporaneo, istruzioniAggiuntive, providerSovrascritto)
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiTestoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto: " & ex.Message)
        Finally
            Try
                If Not String.IsNullOrWhiteSpace(percorsoTemporaneo) AndAlso File.Exists(percorsoTemporaneo) Then File.Delete(percorsoTemporaneo)
            Catch
            End Try
        End Try
    End Function

    Public Shared Async Function RiassumiFileAsync(percorsoFile As String, Optional istruzioniAggiuntive As String = "", Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return New Tuple(Of String, String)(Nothing, "File non trovato")
            End If
            If Not AlmenoUnProviderConfigurato() Then Return New Tuple(Of String, String)(Nothing, "Nessuna chiave API configurata")

            Dim promptCorto As String = PromptRiassuntoCorto(istruzioniAggiuntive)
            Dim esitoGemini As EsitoAi = Nothing
            Dim testoEstratto As String = Nothing

            If ChiaveApiConfigurata(ProviderAi.Gemini) AndAlso (Not providerSovrascritto.HasValue OrElse providerSovrascritto.Value = ProviderAi.Gemini) Then
                esitoGemini = Await InviaGeminiRiassuntoFileAsync(percorsoFile, promptCorto)
                If esitoGemini IsNot Nothing AndAlso Not String.IsNullOrEmpty(esitoGemini.Testo) Then Return esitoGemini.ComeTupla()

                If esitoGemini IsNot Nothing AndAlso esitoGemini.PayloadRifiutato Then
                    testoEstratto = EstraiSoloTestoDaFile(percorsoFile)
                    If Not String.IsNullOrWhiteSpace(testoEstratto) Then
                        esitoGemini = Await InviaGeminiRiassuntoTestoConFallbackAsync(testoEstratto, promptCorto)
                        If esitoGemini IsNot Nothing AndAlso Not String.IsNullOrEmpty(esitoGemini.Testo) Then Return esitoGemini.ComeTupla()
                    End If
                End If
            End If

            Dim groqConsentito As Boolean = ChiaveApiConfigurata(ProviderAi.Groq) AndAlso (Not providerSovrascritto.HasValue OrElse providerSovrascritto.Value = ProviderAi.Groq)
            If groqConsentito AndAlso (esitoGemini Is Nothing OrElse String.IsNullOrEmpty(esitoGemini.Testo)) Then
                If String.IsNullOrWhiteSpace(testoEstratto) Then testoEstratto = EstraiSoloTestoDaFile(percorsoFile)
                Return Await InviaGroqRiassuntoTestoAsync(testoEstratto, promptCorto)
            End If

            Dim ovhConsentito As Boolean = ChiaveApiConfigurata(ProviderAi.Ovh) AndAlso (Not providerSovrascritto.HasValue OrElse providerSovrascritto.Value = ProviderAi.Ovh)
            If ovhConsentito AndAlso (esitoGemini Is Nothing OrElse String.IsNullOrEmpty(esitoGemini.Testo)) Then
                If String.IsNullOrWhiteSpace(testoEstratto) Then testoEstratto = EstraiSoloTestoDaFile(percorsoFile)
                Return Await InviaGroqRiassuntoTestoAsync(testoEstratto, promptCorto, ProviderAi.Ovh)
            End If

            Dim hordeConsentito As Boolean = ChiaveApiConfigurata(ProviderAi.Horde) AndAlso (Not providerSovrascritto.HasValue OrElse providerSovrascritto.Value = ProviderAi.Horde)
            If hordeConsentito AndAlso (esitoGemini Is Nothing OrElse String.IsNullOrEmpty(esitoGemini.Testo)) Then
                If String.IsNullOrWhiteSpace(testoEstratto) Then testoEstratto = EstraiSoloTestoDaFile(percorsoFile)
                Return Await InviaGroqRiassuntoTestoAsync(testoEstratto, promptCorto, ProviderAi.Horde)
            End If

            If esitoGemini IsNot Nothing Then Return esitoGemini.ComeTupla()
            Return New Tuple(Of String, String)(Nothing, "Impossibile ottenere il riassunto")
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiFileAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto del file: " & ex.Message)
        End Try
    End Function

    ''' <summary>Il testo di un file: documenti Word e OpenDocument, RTF, PDF e file di testo. Vuoto se non si riesce a leggerlo.</summary>
    Public Shared Function EstraiSoloTestoDaFile(percorsoFile As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then Return ""
            Dim estensione As String = Path.GetExtension(percorsoFile).ToLowerInvariant()
            Select Case estensione
                Case ".docx"
                    Dim testoDocx As String = EstraiTestoDaDocx(percorsoFile)
                    If Not String.IsNullOrWhiteSpace(testoDocx) Then Return testoDocx
                    Return WGWord.EstraiTestoCompleto(percorsoFile)
                Case ".pdf"
                    Return EstraiTestoDaPdf(percorsoFile)
                Case ".rtf"
                    Using rtb As New System.Windows.Forms.RichTextBox()
                        rtb.LoadFile(percorsoFile, System.Windows.Forms.RichTextBoxStreamType.RichText)
                        Return rtb.Text
                    End Using
                Case ".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".htm", ".vb", ".cs", ".py", ".js", ".css", ".sql", ".ini", ".yaml", ".yml"
                    Return File.ReadAllText(percorsoFile, Encoding.UTF8)
                Case Else
                    If EEstensioneTesto(percorsoFile) Then Return File.ReadAllText(percorsoFile, Encoding.UTF8)
                    Dim testoWord As String = WGWord.EstraiTestoCompleto(percorsoFile)
                    If Not String.IsNullOrWhiteSpace(testoWord) Then Return testoWord
                    Return ""
            End Select
        Catch ex As Exception
            log.ScriviLog($"WGAi.EstraiSoloTestoDaFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function EstraiTestoDaDocx(percorsoFile As String) As String
        Try
            Using archivio As ZipArchive = ZipFile.OpenRead(percorsoFile)
                Dim voce As ZipArchiveEntry = archivio.GetEntry("word/document.xml")
                If voce Is Nothing Then Return ""
                Using flusso As Stream = voce.Open()
                    Using lettore As New StreamReader(flusso, Encoding.UTF8)
                        Dim xmlDocumento As String = lettore.ReadToEnd()
                        If String.IsNullOrWhiteSpace(xmlDocumento) Then Return ""
                        Dim testo As String = Regex.Replace(xmlDocumento, "<w:tab[^/]*/>", " ")
                        testo = Regex.Replace(testo, "</w:p>", Environment.NewLine)
                        testo = Regex.Replace(testo, "<[^>]+>", "")
                        testo = System.Net.WebUtility.HtmlDecode(testo)
                        Return testo.Trim()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.EstraiTestoDaDocx: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function EstraiTestoDaPdf(percorsoFile As String) As String
        Try
            Dim costruttore As New StringBuilder()
            Using documento = PdfDocument.Open(percorsoFile)
                For Each pagina In documento.GetPages()
                    costruttore.AppendLine(pagina.Text)
                Next
            End Using
            Return costruttore.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGAi.EstraiTestoDaPdf: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Async Function InviaGeminiRiassuntoFileAsync(percorsoFile As String, promptCorto As String) As Task(Of EsitoAi)
        Try
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then
                Return New EsitoAi With {.Errore = "Chiave API di Gemini non configurata"}
            End If

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)
                Using cts As New CancellationTokenSource()
                    Dim caricamento As EsitoCaricamentoFile = Await CaricaAllegatoGeminiAsync(client, percorsoFile, chiave, cts.Token)
                    If caricamento Is Nothing OrElse String.IsNullOrWhiteSpace(caricamento.UriFile) Then
                        Dim esitoFallito As New EsitoAi()
                        If caricamento IsNot Nothing Then
                            esitoFallito.CodiceHttp = caricamento.CodiceHttp
                            esitoFallito.PayloadRifiutato = caricamento.PayloadRifiutato
                            esitoFallito.Errore = If(caricamento.Errore, "Impossibile caricare il file su Gemini")
                        Else
                            esitoFallito.Errore = "Impossibile caricare il file su Gemini"
                        End If
                        Return esitoFallito
                    End If

                    Dim parti As New JArray From {
                        New JObject From {{"text", promptCorto}},
                        New JObject From {
                            {"file_data", New JObject From {
                                {"mime_type", caricamento.Mime},
                                {"file_uri", caricamento.UriFile}
                            }}
                        }
                    }
                    Dim contenuti As New JArray From {New JObject From {{"role", "user"}, {"parts", parti}}}
                    Return Await EseguiRichiestaGeminiAsync(client, chiave, ModelloGeminiPredefinito, contenuti, cts.Token)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGeminiRiassuntoFileAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Async Function InviaGeminiRiassuntoTestoConFallbackAsync(testo As String, promptCorto As String) As Task(Of EsitoAi)
        Dim percorsoTemporaneo As String = Nothing
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return New EsitoAi With {.Errore = "Il testo estratto è vuoto"}

            percorsoTemporaneo = Path.Combine(Path.GetTempPath(), "wgai-riassunto-testo-" & Guid.NewGuid().ToString("N") & ".txt")
            File.WriteAllText(percorsoTemporaneo, testo, Encoding.UTF8)

            Dim esitoFile As EsitoAi = Await InviaGeminiRiassuntoFileAsync(percorsoTemporaneo, promptCorto)
            If esitoFile IsNot Nothing AndAlso Not String.IsNullOrEmpty(esitoFile.Testo) Then Return esitoFile

            If esitoFile IsNot Nothing AndAlso esitoFile.PayloadRifiutato Then
                Return Await RiassumiTestoABlocchiGeminiAsync(testo, promptCorto)
            End If

            Return If(esitoFile, New EsitoAi With {.Errore = "Gemini non ha restituito il riassunto del testo"})
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGeminiRiassuntoTestoConFallbackAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoAi With {.Errore = "Errore Gemini: " & ex.Message}
        Finally
            Try
                If Not String.IsNullOrWhiteSpace(percorsoTemporaneo) AndAlso File.Exists(percorsoTemporaneo) Then File.Delete(percorsoTemporaneo)
            Catch
            End Try
        End Try
    End Function

    Private Shared Async Function RiassumiTestoABlocchiGeminiAsync(testo As String, promptCorto As String) As Task(Of EsitoAi)
        Try
            Dim blocchi As List(Of String) = DividiInBlocchi(testo, MassimoParoleBlocco)
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then Return New EsitoAi With {.Errore = "Impossibile suddividere il testo"}

            If blocchi.Count = 1 Then
                Return Await InviaGeminiSoloPromptAsync(promptCorto & Environment.NewLine & Environment.NewLine & blocchi(0))
            End If

            Dim parziali As New List(Of String)
            For indiceBlocco As Integer = 0 To blocchi.Count - 1
                Dim promptBlocco As String = promptCorto & " Parte " & (indiceBlocco + 1).ToString() & " di " & blocchi.Count.ToString() & "."
                Dim esitoBlocco As EsitoAi = Await InviaGeminiSoloPromptAsync(promptBlocco & Environment.NewLine & Environment.NewLine & blocchi(indiceBlocco))
                If esitoBlocco Is Nothing OrElse String.IsNullOrEmpty(esitoBlocco.Testo) Then
                    Dim errore As String = "Riassunto interrotto alla parte " & (indiceBlocco + 1).ToString() & " di " & blocchi.Count.ToString()
                    If esitoBlocco IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(esitoBlocco.Errore) Then errore &= ": " & esitoBlocco.Errore
                    If parziali.Count > 0 Then
                        Return New EsitoAi With {.Testo = String.Join(Environment.NewLine & Environment.NewLine, parziali), .Errore = errore}
                    End If
                    Return New EsitoAi With {.Errore = errore, .PayloadRifiutato = If(esitoBlocco IsNot Nothing, esitoBlocco.PayloadRifiutato, False), .CodiceHttp = If(esitoBlocco IsNot Nothing, esitoBlocco.CodiceHttp, 0)}
                End If
                parziali.Add(esitoBlocco.Testo)
            Next

            If parziali.Count = 1 Then Return New EsitoAi With {.Testo = parziali(0)}
            Dim promptRiduci As String = promptCorto & " Unisci i riassunti parziali in un unico riassunto."
            Return Await InviaGeminiSoloPromptAsync(promptRiduci & Environment.NewLine & Environment.NewLine & String.Join(Environment.NewLine & Environment.NewLine, parziali))
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiTestoABlocchiGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Async Function InviaGeminiSoloPromptAsync(prompt As String) As Task(Of EsitoAi)
        Try
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New EsitoAi With {.Errore = "Chiave API di Gemini non configurata"}
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)
                Using cts As New CancellationTokenSource()
                    Dim parti As New JArray From {New JObject From {{"text", prompt}}}
                    Dim contenuti As New JArray From {New JObject From {{"role", "user"}, {"parts", parti}}}
                    Return Await EseguiRichiestaGeminiAsync(client, chiave, ModelloGeminiPredefinito, contenuti, cts.Token)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGeminiSoloPromptAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Async Function InviaGroqRiassuntoTestoAsync(testo As String, promptCorto As String, Optional provider As ProviderAi = ProviderAi.Groq) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return New Tuple(Of String, String)(Nothing, "Il testo da riassumere è vuoto")
            If Not ChiaveApiConfigurata(provider) Then Return New Tuple(Of String, String)(Nothing, "Il motore " & NomeProviderInChiaro(provider) & " non è configurato")

            Dim blocchi As List(Of String) = DividiInBlocchi(testo, MassimoParoleBloccoGroq)
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then Return New Tuple(Of String, String)(Nothing, "Impossibile suddividere il testo")

            If blocchi.Count = 1 Then
                Dim contenuto As String = promptCorto
                If blocchi(0).Length > LunghezzaMassimaTestoAllegatoGroq Then
                    contenuto &= Environment.NewLine & Environment.NewLine & blocchi(0).Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                Else
                    contenuto &= Environment.NewLine & Environment.NewLine & blocchi(0)
                End If
                Return Await OttieniRispostaAsync(contenuto, Nothing, provider)
            End If

            Dim parziali As New List(Of String)
            For indiceBlocco As Integer = 0 To blocchi.Count - 1
                Dim testoBlocco As String = blocchi(indiceBlocco)
                If testoBlocco.Length > LunghezzaMassimaTestoAllegatoGroq Then testoBlocco = testoBlocco.Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                Dim promptBlocco As String = promptCorto & " Parte " & (indiceBlocco + 1).ToString() & " di " & blocchi.Count.ToString() & "."
                Dim risultatoBlocco As Tuple(Of String, String) = Await OttieniRispostaAsync(promptBlocco & Environment.NewLine & Environment.NewLine & testoBlocco, Nothing, provider)
                If risultatoBlocco Is Nothing OrElse String.IsNullOrEmpty(risultatoBlocco.Item1) Then
                    If parziali.Count > 0 Then
                        Return New Tuple(Of String, String)(String.Join(Environment.NewLine & Environment.NewLine, parziali), If(risultatoBlocco IsNot Nothing, risultatoBlocco.Item2, "riassunto parziale"))
                    End If
                    Return New Tuple(Of String, String)(Nothing, "Riassunto Groq interrotto alla parte " & (indiceBlocco + 1).ToString() & " di " & blocchi.Count.ToString() & ": " & If(risultatoBlocco IsNot Nothing, risultatoBlocco.Item2, "errore sconosciuto"))
                End If
                parziali.Add(risultatoBlocco.Item1)
            Next

            If parziali.Count = 1 Then Return New Tuple(Of String, String)(parziali(0), Nothing)
            Dim promptRiduci As String = promptCorto & " Unisci i riassunti parziali in un unico riassunto."
            Return Await OttieniRispostaAsync(promptRiduci & Environment.NewLine & Environment.NewLine & String.Join(Environment.NewLine & Environment.NewLine, parziali), Nothing, provider)
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGroqRiassuntoTestoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore " & NomeProviderInChiaro(provider) & ": " & ex.Message)
        End Try
    End Function

    Public Shared Async Function DescriviImmagineAsync(percorsoImmagine As String, Optional istruzioniAggiuntive As String = "", Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(percorsoImmagine) OrElse Not File.Exists(percorsoImmagine) Then
                Return New Tuple(Of String, String)(Nothing, "Immagine non trovata")
            End If
            Dim promptTesto As String = "Descrivi il contenuto di questa immagine in italiano, in modo chiaro e dettagliato."
            If Not String.IsNullOrWhiteSpace(istruzioniAggiuntive) Then promptTesto &= " " & istruzioniAggiuntive
            Return Await OttieniRispostaAsync(promptTesto, New List(Of String) From {percorsoImmagine}, providerSovrascritto)
        Catch ex As Exception
            log.ScriviLog($"WGAi.DescriviImmagineAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la descrizione dell'immagine: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function CaricaAllegatoGeminiAsync(client As HttpClient, percorsoFile As String, chiave As String, token As CancellationToken) As Task(Of EsitoCaricamentoFile)
        Try
            Dim bytesFile() As Byte = File.ReadAllBytes(percorsoFile)
            Dim mimeType As String = MimeTypeDaEstensione(percorsoFile)
            Dim nomeVisualizzato As String = Path.GetFileName(percorsoFile)

            Dim urlAvvioCaricamento As String = "https://generativelanguage.googleapis.com/upload/v1beta/files"
            Dim ultimoEsito As New EsitoCaricamentoFile With {.Mime = mimeType, .Errore = "Impossibile caricare il file su Gemini"}

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False
                Try
                    Using richiestaAvvio As New HttpRequestMessage(HttpMethod.Post, urlAvvioCaricamento)
                        richiestaAvvio.Headers.Add("x-goog-api-key", chiave)
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Protocol", "resumable")
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Command", "start")
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Header-Content-Length", bytesFile.Length.ToString())
                        richiestaAvvio.Headers.Add("X-Goog-Upload-Header-Content-Type", mimeType)
                        Dim corpoMetadati As New JObject From {
                            {"file", New JObject From {{"display_name", nomeVisualizzato}}}
                        }
                        richiestaAvvio.Content = New StringContent(corpoMetadati.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")

                        Using rispostaAvvio As HttpResponseMessage = Await client.SendAsync(richiestaAvvio, token)
                            RegistraRichiestaGemini()
                            If Not rispostaAvvio.IsSuccessStatusCode Then
                                Dim corpoErrore As String = Await rispostaAvvio.Content.ReadAsStringAsync()
                                Dim codiceAvvio As Integer = CInt(rispostaAvvio.StatusCode)
                                ultimoEsito.CodiceHttp = codiceAvvio
                                ultimoEsito.PayloadRifiutato = EStatusPayloadRifiutato(codiceAvvio)
                                ultimoEsito.Errore = "Avvio caricamento Gemini HTTP " & codiceAvvio.ToString()
                                eTemporaneo = EStatusTemporaneo(codiceAvvio) AndAlso Not ultimoEsito.PayloadRifiutato
                                log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, avvio caricamento fallito, HTTP {codiceAvvio}.{Environment.NewLine}Corpo risposta: {corpoErrore}")
                                If ultimoEsito.PayloadRifiutato Then Return ultimoEsito
                            ElseIf Not rispostaAvvio.Headers.Contains("X-Goog-Upload-URL") Then
                                log.ScriviLog("WGAi.CaricaAllegatoGeminiAsync: intestazione X-Goog-Upload-URL assente nella risposta di avvio")
                            Else
                                Dim urlCaricamentoBytes As String = rispostaAvvio.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault()

                                Using richiestaCaricamento As New HttpRequestMessage(HttpMethod.Post, urlCaricamentoBytes)
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Offset", "0")
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Command", "upload, finalize")
                                    richiestaCaricamento.Content = New ByteArrayContent(bytesFile)
                                    richiestaCaricamento.Content.Headers.ContentLength = bytesFile.Length

                                    Using rispostaCaricamento As HttpResponseMessage = Await client.SendAsync(richiestaCaricamento, token)
                                        RegistraRichiestaGemini()
                                        Dim corpoRispostaCaricamento As String = Await rispostaCaricamento.Content.ReadAsStringAsync()

                                        If Not rispostaCaricamento.IsSuccessStatusCode Then
                                            Dim codiceCaricamento As Integer = CInt(rispostaCaricamento.StatusCode)
                                            ultimoEsito.CodiceHttp = codiceCaricamento
                                            ultimoEsito.PayloadRifiutato = EStatusPayloadRifiutato(codiceCaricamento)
                                            ultimoEsito.Errore = "Caricamento byte Gemini HTTP " & codiceCaricamento.ToString()
                                            eTemporaneo = EStatusTemporaneo(codiceCaricamento) AndAlso Not ultimoEsito.PayloadRifiutato
                                            log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, caricamento byte fallito, HTTP {codiceCaricamento}.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                            If ultimoEsito.PayloadRifiutato Then Return ultimoEsito
                                        Else
                                            Dim risultatoJson As JObject = JObject.Parse(corpoRispostaCaricamento)
                                            Dim gettoneUri As JToken = risultatoJson.SelectToken("file.uri")
                                            If gettoneUri Is Nothing Then
                                                log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: uri del file assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                                ultimoEsito.Errore = "Uri del file assente nella risposta di Gemini"
                                                Return ultimoEsito
                                            End If
                                            Return New EsitoCaricamentoFile With {.UriFile = gettoneUri.ToString(), .Mime = mimeType}
                                        End If
                                    End Using
                                End Using
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                Return ultimoEsito
            Next

            Return ultimoEsito
        Catch ex As Exception
            log.ScriviLog($"WGAi.CaricaAllegatoGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoCaricamentoFile With {.Errore = "Errore caricamento Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Async Function InviaGeminiUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Gemini non configurata")

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                Dim parti As New JArray()
                parti.Add(New JObject From {{"text", domanda}})

                For Each percorsoFile As String In file
                    Dim risultatoCaricamento As EsitoCaricamentoFile = Await CaricaAllegatoGeminiAsync(client, percorsoFile, chiave, token)
                    If risultatoCaricamento IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultatoCaricamento.UriFile) Then
                        parti.Add(New JObject From {
                            {"file_data", New JObject From {
                                {"mime_type", risultatoCaricamento.Mime},
                                {"file_uri", risultatoCaricamento.UriFile}
                            }}
                        })
                    Else
                        log.ScriviLog($"WGAi.InviaGeminiUnaVoltaAsync: impossibile caricare l'allegato {Path.GetFileName(percorsoFile)}, HTTP {If(risultatoCaricamento IsNot Nothing, risultatoCaricamento.CodiceHttp.ToString(), "?")}, proseguo senza di esso")
                    End If
                Next

                Dim contenuti As New JArray From {New JObject From {{"role", "user"}, {"parts", parti}}}
                Dim esito As EsitoAi = Await EseguiRichiestaGeminiAsync(client, chiave, modello, contenuti, token, testoFormattato)
                If esito Is Nothing Then Return New Tuple(Of String, String)(Nothing, "Gemini non ha risposto")
                Return esito.ComeTupla()
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGeminiUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function EseguiRichiestaGeminiAsync(client As HttpClient, chiave As String, modello As String, contenuti As JArray, token As CancellationToken, Optional testoFormattato As Boolean = False, Optional configurazioneGenerazione As JObject = Nothing) As Task(Of EsitoAi)
        Try
            Dim corpoRichiesta As New JObject From {{"contents", contenuti}}
            If configurazioneGenerazione IsNot Nothing Then corpoRichiesta("generationConfig") = configurazioneGenerazione
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models/" & modello & ":generateContent"

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False
                Dim messaggioErroreTentativo As String = Nothing
                Dim codiceHttpTentativo As Integer = 0
                Dim payloadRifiutatoTentativo As Boolean = False

                Try
                    Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                        richiesta.Headers.Add("x-goog-api-key", chiave)
                        richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                        Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                            RegistraRichiestaGemini()
                            Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                            If Not risposta.IsSuccessStatusCode Then
                                Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                codiceHttpTentativo = codiceStato
                                payloadRifiutatoTentativo = EStatusPayloadRifiutato(codiceStato)
                                eTemporaneo = EStatusTemporaneo(codiceStato) AndAlso Not payloadRifiutatoTentativo
                                messaggioErroreTentativo = If(eTemporaneo, "Errore da Gemini: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Gemini", codiceStato))
                                If codiceStato = 429 AndAlso QuotaGiornalieraEsaurita(corpoRisposta) Then
                                    eTemporaneo = False
                                    messaggioErroreTentativo = "Quota giornaliera di Gemini esaurita"
                                End If
                                log.ScriviLog($"WGAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {codiceStato} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                If payloadRifiutatoTentativo Then
                                    Return New EsitoAi With {.Errore = messaggioErroreTentativo, .CodiceHttp = codiceStato, .PayloadRifiutato = True}
                                End If
                            Else
                                Dim testo As String = EstraiTestoDaRispostaGemini(corpoRisposta, testoFormattato)
                                If testo Is Nothing Then
                                    log.ScriviLog($"WGAi.EseguiRichiestaGeminiAsync: impossibile estrarre il testo dalla risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                    Return New EsitoAi With {.Errore = "Gemini non ha restituito testo"}
                                End If
                                Return New EsitoAi With {.Testo = testo.Trim()}
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Errore di connessione verso Gemini: " & exRete.Message
                    log.ScriviLog($"WGAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    messaggioErroreTentativo = "Timeout della richiesta verso Gemini"
                    log.ScriviLog($"WGAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                If eTemporaneo Then Return New EsitoAi With {.Errore = "Gemini è momentaneamente sovraccarico"}
                Return New EsitoAi With {.Errore = If(messaggioErroreTentativo, "Gemini non ha risposto dopo diversi tentativi"), .CodiceHttp = codiceHttpTentativo, .PayloadRifiutato = payloadRifiutatoTentativo}
            Next

            Return New EsitoAi With {.Errore = "Gemini non ha risposto dopo diversi tentativi"}
        Catch ex As Exception
            log.ScriviLog($"WGAi.EseguiRichiestaGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New EsitoAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Function PuliscoTestoAi(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return testo
            Dim risultato As String = testo

            risultato = Regex.Replace(risultato, "```[a-zA-Z0-9]*\r?\n?", "")
            risultato = risultato.Replace("```", "")
            risultato = Regex.Replace(risultato, "`([^`]+)`", "$1")

            risultato = Regex.Replace(risultato, "\*\*(.+?)\*\*", "$1")
            risultato = Regex.Replace(risultato, "__(.+?)__", "$1")
            risultato = Regex.Replace(risultato, "\*(.+?)\*", "$1")
            risultato = Regex.Replace(risultato, "(?<!\w)_(.+?)_(?!\w)", "$1")
            risultato = Regex.Replace(risultato, "~~(.+?)~~", "$1")
            risultato = Regex.Replace(risultato, "\[(.+?)\]\((.+?)\)", "$1 ($2)")

            Dim righe As String() = risultato.Split({Environment.NewLine, vbLf, vbCr}, StringSplitOptions.None)
            For i As Integer = 0 To righe.Length - 1
                Dim spazioIniziale As String = righe(i).Substring(0, righe(i).Length - righe(i).TrimStart().Length)
                Dim contenuto As String = righe(i).TrimStart()

                If Regex.IsMatch(contenuto, "^-{3,}$") OrElse Regex.IsMatch(contenuto, "^\*{3,}$") OrElse Regex.IsMatch(contenuto, "^_{3,}$") Then
                    righe(i) = ""
                    Continue For
                End If

                contenuto = Regex.Replace(contenuto, "^#{1,6}\s+", "")
                contenuto = Regex.Replace(contenuto, "^>\s?", "")
                contenuto = Regex.Replace(contenuto, "^[\-\*\+]\s+", "")
                righe(i) = spazioIniziale & contenuto
            Next
            risultato = String.Join(Environment.NewLine, righe)

            risultato = risultato.Replace("*", "").Replace("#", "")
            risultato = AggiungiACapoDopoPunto(risultato)

            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGAi.PuliscoTestoAi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared ReadOnly EspressionePuntoFineFrase As New Regex("(?<!\.)(?<!^[ \t]*\d{1,3})(?<!\b(?:sig|sigg|dott|prof|ing|avv|ecc|pag|pagg|es|nr|tel|art|artt|vol|fig|geom|rag|arch|sen|mons|cfr|etc|ca|ss))\.(?!\.)[ \t]*(?=[^\s\d""'»)\]])", RegexOptions.IgnoreCase Or RegexOptions.Multiline Or RegexOptions.Compiled)

    Public Shared Function AggiungiACapoDopoPunto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return testo
            Dim risultato As String = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            risultato = EspressionePuntoFineFrase.Replace(risultato, "." & vbLf)
            risultato = Regex.Replace(risultato, "\n[ \t]+", vbLf)
            Return risultato.Replace(vbLf, vbCrLf)
        Catch ex As Exception
            log.ScriviLog($"WGAi.AggiungiACapoDopoPunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function AttesaSuggeritaGroq(risposta As HttpResponseMessage) As Integer
        Try
            If risposta Is Nothing OrElse risposta.Headers.RetryAfter Is Nothing Then Return 0
            If risposta.Headers.RetryAfter.Delta.HasValue Then Return CInt(Math.Ceiling(risposta.Headers.RetryAfter.Delta.Value.TotalSeconds)) + 1
            If risposta.Headers.RetryAfter.Date.HasValue Then
                Dim differenza As TimeSpan = risposta.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow
                If differenza.TotalSeconds > 0 Then Return CInt(Math.Ceiling(differenza.TotalSeconds)) + 1
            End If
            Return 0
        Catch ex As Exception
            log.ScriviLog($"WGAi.AttesaSuggeritaGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Shared Function LimiteGiornalieroGroqRaggiunto(corpoRisposta As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(corpoRisposta) Then Return False
            If corpoRisposta.IndexOf("per day", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If corpoRisposta.IndexOf("(TPD)", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            If corpoRisposta.IndexOf("(RPD)", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGAi.LimiteGiornalieroGroqRaggiunto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function QuotaGiornalieraEsaurita(corpoRisposta As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(corpoRisposta) Then Return False
            Return corpoRisposta.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0
        Catch ex As Exception
            log.ScriviLog($"WGAi.QuotaGiornalieraEsaurita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function EstraiTestoDaRispostaGemini(corpoRisposta As String, Optional testoFormattato As Boolean = False) As String
        Try
            Dim risultato As JObject = JObject.Parse(corpoRisposta)
            Dim candidati As JArray = TryCast(risultato("candidates"), JArray)
            If candidati Is Nothing OrElse candidati.Count = 0 Then Return Nothing

            Dim primoCandidato As JObject = TryCast(candidati(0), JObject)
            If primoCandidato Is Nothing Then Return Nothing

            Dim contenuto As JObject = TryCast(primoCandidato("content"), JObject)
            If contenuto Is Nothing Then Return Nothing

            Dim parti As JArray = TryCast(contenuto("parts"), JArray)
            If parti Is Nothing Then Return Nothing

            Dim costruttore As New StringBuilder()
            For Each parte As JObject In parti
                Dim gettonePensiero As JToken = parte("thought")
                If gettonePensiero IsNot Nothing AndAlso gettonePensiero.Type = JTokenType.Boolean AndAlso CBool(gettonePensiero) Then Continue For
                Dim gettoneTesto As JToken = parte("text")
                If gettoneTesto IsNot Nothing Then costruttore.Append(gettoneTesto.ToString())
            Next

            If costruttore.Length = 0 Then Return Nothing
            If testoFormattato Then Return RimuoviRecinzioniCodice(costruttore.ToString()).Trim()
            Return PuliscoTestoAi(costruttore.ToString())
        Catch ex As Exception
            log.ScriviLog($"WGAi.EstraiTestoDaRispostaGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Async Function InviaGroqUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = LeggiChiaveApiGroq()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Groq non configurata")

            Dim testoCompleto As String = domanda
            Dim fileNonSupportati As New List(Of String)
            For Each percorsoFile As String In file
                If EEstensioneTesto(percorsoFile) Then
                    Try
                        Dim contenutoFile As String = System.IO.File.ReadAllText(percorsoFile, Encoding.UTF8)
                        If contenutoFile.Length > LunghezzaMassimaTestoAllegatoGroq Then
                            contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                        End If
                        testoCompleto &= Environment.NewLine & Environment.NewLine & "Contenuto del file allegato (" & Path.GetFileName(percorsoFile) & "):" & Environment.NewLine & contenutoFile
                    Catch exLettura As Exception
                        log.ScriviLog($"WGAi.InviaGroqUnaVoltaAsync (lettura allegato): {exLettura.Message}")
                        fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                    End Try
                Else
                    fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                End If
            Next
            If fileNonSupportati.Count > 0 Then
                testoCompleto &= Environment.NewLine & Environment.NewLine & "(Nota: Groq non supporta questi allegati, sono stati ignorati: " & String.Join(", ", fileNonSupportati) & ")"
            End If
            If testoCompleto.Length > LunghezzaMassimaDomandaGroq Then testoCompleto = testoCompleto.Substring(0, LunghezzaMassimaDomandaGroq)

            Dim messaggi As New JArray From {New JObject From {{"role", "user"}, {"content", testoCompleto}}}
            Return Await EseguiRichiestaGroqAsync(chiave, modello, messaggi, token, testoFormattato)
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaGroqUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function EseguiRichiestaGroqAsync(chiave As String, modello As String, messaggi As JArray, token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim corpoRichiesta As New JObject From {{"model", modello}, {"messages", messaggi}}
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://api.groq.com/openai/v1/chat/completions"

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                For tentativo As Integer = 1 To NumeroMassimoTentativi
                    Dim eTemporaneo As Boolean = False
                    Dim messaggioErroreTentativo As String = Nothing
                    Dim attesaSuggeritaSecondi As Integer = 0

                    Try
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", chiave)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                AggiornaStatoGroqDaRisposta(risposta)
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    messaggioErroreTentativo = If(eTemporaneo, "Errore da Groq: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Groq", codiceStato))
                                    If codiceStato = 429 Then
                                        attesaSuggeritaSecondi = AttesaSuggeritaGroq(risposta)
                                        If LimiteGiornalieroGroqRaggiunto(corpoRisposta) Then
                                            eTemporaneo = False
                                            messaggioErroreTentativo = "Quota giornaliera di Groq esaurita"
                                        End If
                                    ElseIf codiceStato = 413 Then
                                        eTemporaneo = False
                                        messaggioErroreTentativo = "Il testo è troppo lungo per il piano gratuito di Groq"
                                    End If
                                    log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Dim risultato As JObject = JObject.Parse(corpoRisposta)
                                    Dim scelte As JArray = TryCast(risultato("choices"), JArray)
                                    If scelte Is Nothing OrElse scelte.Count = 0 Then
                                        log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: campo 'choices' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "Groq non ha restituito testo")
                                    End If
                                    Dim gettoneTesto As JToken = scelte(0).SelectToken("message.content")
                                    If gettoneTesto Is Nothing Then
                                        log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: campo 'message.content' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "Groq non ha restituito testo")
                                    End If
                                    Dim testoGroq As String = gettoneTesto.ToString().Trim()
                                    If String.IsNullOrWhiteSpace(testoGroq) Then Return New Tuple(Of String, String)(Nothing, "Groq non ha restituito testo")
                                    If testoFormattato Then Return New Tuple(Of String, String)(RimuoviRecinzioniCodice(testoGroq).Trim(), Nothing)
                                    Return New Tuple(Of String, String)(PuliscoTestoAi(testoGroq), Nothing)
                                End If
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Errore di connessione verso Groq: " & exRete.Message
                        log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        messaggioErroreTentativo = "Timeout della richiesta verso Groq"
                        log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                    End Try

                    If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                        Dim attesaSecondi As Integer = Math.Max(AttesaBackoffSecondi(tentativo), Math.Min(attesaSuggeritaSecondi, 65))
                        Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                        Continue For
                    End If

                    If eTemporaneo Then Return New Tuple(Of String, String)(Nothing, "Groq è momentaneamente sovraccarico")
                    Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "Groq non ha risposto dopo diversi tentativi"))
                Next

                Return New Tuple(Of String, String)(Nothing, "Groq non ha risposto dopo diversi tentativi")
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.EseguiRichiestaGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function InviaOvhUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim testoCompleto As String = domanda
            Dim fileNonSupportati As New List(Of String)
            For Each percorsoFile As String In file
                If EEstensioneTesto(percorsoFile) Then
                    Try
                        Dim contenutoFile As String = System.IO.File.ReadAllText(percorsoFile, Encoding.UTF8)
                        If contenutoFile.Length > LunghezzaMassimaTestoAllegatoOvh Then
                            contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoOvh)
                        End If
                        testoCompleto &= Environment.NewLine & Environment.NewLine & "Contenuto del file allegato (" & Path.GetFileName(percorsoFile) & "):" & Environment.NewLine & contenutoFile
                    Catch exLettura As Exception
                        log.ScriviLog($"WGAi.InviaOvhUnaVoltaAsync (lettura allegato): {exLettura.Message}")
                        fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                    End Try
                Else
                    fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                End If
            Next
            If fileNonSupportati.Count > 0 Then
                testoCompleto &= Environment.NewLine & Environment.NewLine & "(Nota: OVHcloud non supporta questi allegati, sono stati ignorati: " & String.Join(", ", fileNonSupportati) & ")"
            End If
            If testoCompleto.Length > LunghezzaMassimaDomandaOvh Then testoCompleto = testoCompleto.Substring(0, LunghezzaMassimaDomandaOvh)

            Dim messaggi As New JArray From {New JObject From {{"role", "user"}, {"content", testoCompleto}}}
            Return Await EseguiRichiestaOvhAsync(modello, messaggi, token, testoFormattato)
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaOvhUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore OVHcloud: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' Una richiesta di chat a OVHcloud AI Endpoints (API compatibile OpenAI). Non richiede nessuna chiave: vale il limite
    ''' gratuito più basso, e un errore 429 si risolve aspettando il tempo indicato dal servizio.
    ''' </summary>
    Public Shared Async Function EseguiRichiestaOvhAsync(modello As String, messaggi As JArray, token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim corpoRichiesta As New JObject From {{"model", modello}, {"messages", messaggi}}
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = UrlBaseOvh & "/chat/completions"

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                For tentativo As Integer = 1 To NumeroMassimoTentativi
                    Dim eTemporaneo As Boolean = False
                    Dim messaggioErroreTentativo As String = Nothing
                    Dim attesaSuggeritaSecondi As Integer = 0

                    Try
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    messaggioErroreTentativo = If(eTemporaneo, "Errore da OVHcloud: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("OVHcloud", codiceStato))
                                    If codiceStato = 429 Then
                                        attesaSuggeritaSecondi = AttesaSuggeritaGroq(risposta)
                                        If attesaSuggeritaSecondi = 0 Then attesaSuggeritaSecondi = 30
                                    ElseIf codiceStato = 413 Then
                                        eTemporaneo = False
                                        messaggioErroreTentativo = "Il testo è troppo lungo per il piano gratuito di OVHcloud"
                                    End If
                                    log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Dim risultato As JObject = JObject.Parse(corpoRisposta)
                                    Dim scelte As JArray = TryCast(risultato("choices"), JArray)
                                    If scelte Is Nothing OrElse scelte.Count = 0 Then
                                        log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: campo 'choices' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "OVHcloud non ha restituito testo")
                                    End If
                                    Dim gettoneTesto As JToken = scelte(0).SelectToken("message.content")
                                    If gettoneTesto Is Nothing Then
                                        log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: campo 'message.content' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "OVHcloud non ha restituito testo")
                                    End If
                                    Dim testoOvh As String = gettoneTesto.ToString().Trim()
                                    If String.IsNullOrWhiteSpace(testoOvh) Then Return New Tuple(Of String, String)(Nothing, "OVHcloud non ha restituito testo")
                                    If testoFormattato Then Return New Tuple(Of String, String)(RimuoviRecinzioniCodice(testoOvh).Trim(), Nothing)
                                    Return New Tuple(Of String, String)(PuliscoTestoAi(testoOvh), Nothing)
                                End If
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Errore di connessione verso OVHcloud: " & exRete.Message
                        log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        messaggioErroreTentativo = "Timeout della richiesta verso OVHcloud"
                        log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                    End Try

                    If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                        Dim attesaSecondi As Integer = Math.Max(AttesaBackoffSecondi(tentativo), Math.Min(attesaSuggeritaSecondi, 65))
                        Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                        Continue For
                    End If

                    If eTemporaneo Then Return New Tuple(Of String, String)(Nothing, "OVHcloud è momentaneamente sovraccarico o ha raggiunto il limite di richieste al minuto")
                    Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "OVHcloud non ha risposto dopo diversi tentativi"))
                Next

                Return New Tuple(Of String, String)(Nothing, "OVHcloud non ha risposto dopo diversi tentativi")
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.EseguiRichiestaOvhAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore OVHcloud: " & ex.Message)
        End Try
    End Function

    ' ---------- AI Horde ----------

    ''' <summary>Un modello offerto in questo momento da AI Horde, con i limiti dichiarati da chi lo ospita.</summary>
    Private Class ModelloHorde
        Public Id As String = ""
        Public Contesto As Integer
        Public MassimoRisposta As Integer
        Public Lavoratori As Integer
        Public Coda As Double
        Public Miliardi As Double
    End Class

    Private Shared ReadOnly _bloccoElencoHorde As New Object()
    Private Shared _elencoHorde As List(Of ModelloHorde) = Nothing
    Private Shared _momentoElencoHorde As DateTime = DateTime.MinValue

    ''' <summary>I modelli di testo che AI Horde sta servendo adesso (con almeno un computer attivo). L'elenco vale per un minuto.</summary>
    Private Shared Async Function ModelliHordeAsync(Optional forzaAggiornamento As Boolean = False) As Task(Of List(Of ModelloHorde))
        Try
            SyncLock _bloccoElencoHorde
                If Not forzaAggiornamento AndAlso _elencoHorde IsNot Nothing AndAlso (DateTime.UtcNow - _momentoElencoHorde).TotalSeconds < SecondiValiditaElencoHorde Then
                    Return New List(Of ModelloHorde)(_elencoHorde)
                End If
            End SyncLock

            Dim elenco As New List(Of ModelloHorde)()
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(30)
                Using richiesta As New HttpRequestMessage(HttpMethod.Get, UrlBaseHorde & "/models")
                    richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", ChiaveAnonimaHorde)
                    Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                        Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                        If Not risposta.IsSuccessStatusCode Then
                            log.ScriviLog($"WGAi.ModelliHordeAsync: AI Horde HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                            Return elenco
                        End If
                        Dim dati As JArray = TryCast(JObject.Parse(corpo)("data"), JArray)
                        If dati Is Nothing Then Return elenco
                        For Each voce As JToken In dati
                            Dim gettoneId As JToken = voce("id")
                            If gettoneId Is Nothing Then Continue For
                            Dim id As String = gettoneId.ToString()
                            If String.IsNullOrWhiteSpace(id) OrElse ModelloNonAdattoAlTesto(id) Then Continue For
                            Dim modello As New ModelloHorde With {.Id = id}
                            modello.Contesto = NumeroInteroHorde(voce.SelectToken("context_length"), 4096)
                            modello.MassimoRisposta = NumeroInteroHorde(voce.SelectToken("top_provider.max_completion_tokens"), 512)
                            modello.Lavoratori = NumeroInteroHorde(voce.SelectToken("x_horde.workers"), 1)
                            Dim gettoneCoda As JToken = voce.SelectToken("x_horde.queued")
                            If gettoneCoda IsNot Nothing Then Double.TryParse(gettoneCoda.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, modello.Coda)
                            Dim dimensione As Match = Regex.Match(id.ToLowerInvariant(), "(?<![a-z0-9.])(\d+(?:\.\d+)?)b(?![a-z0-9])")
                            If dimensione.Success Then Double.TryParse(dimensione.Groups(1).Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, modello.Miliardi)
                            If modello.Lavoratori > 0 Then elenco.Add(modello)
                        Next
                    End Using
                End Using
            End Using

            SyncLock _bloccoElencoHorde
                _elencoHorde = New List(Of ModelloHorde)(elenco)
                _momentoElencoHorde = DateTime.UtcNow
            End SyncLock
            Return elenco
        Catch ex As Exception
            log.ScriviLog($"WGAi.ModelliHordeAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of ModelloHorde)()
        End Try
    End Function

    Private Shared Function NumeroInteroHorde(gettone As JToken, predefinito As Integer) As Integer
        Try
            If gettone Is Nothing Then Return predefinito
            Dim valore As Double = 0
            If Not Double.TryParse(gettone.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, valore) Then Return predefinito
            If valore <= 0 OrElse valore > Integer.MaxValue Then Return predefinito
            Return CInt(Math.Floor(valore))
        Catch ex As Exception
            log.ScriviLog($"WGAi.NumeroInteroHorde: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return predefinito
        End Try
    End Function

    ''' <summary>
    ''' Il modello da usare: quello chiesto, se in questo momento è servito; altrimenti il più grande tra quelli senza coda
    ''' (a parità, quello con più computer attivi).
    ''' </summary>
    Private Shared Function ScegliModelloHorde(elenco As List(Of ModelloHorde), modelloChiesto As String, Optional daEscludere As String = Nothing) As ModelloHorde
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return Nothing
            If Not String.IsNullOrWhiteSpace(modelloChiesto) AndAlso Not String.Equals(modelloChiesto.Trim(), ModelloHordeAutomatico, StringComparison.OrdinalIgnoreCase) Then
                Dim chiesto As ModelloHorde = elenco.FirstOrDefault(Function(m) String.Equals(m.Id, modelloChiesto.Trim(), StringComparison.OrdinalIgnoreCase))
                If chiesto IsNot Nothing AndAlso Not String.Equals(chiesto.Id, daEscludere, StringComparison.OrdinalIgnoreCase) Then Return chiesto
            End If
            Dim candidati As List(Of ModelloHorde) = elenco.Where(Function(m) Not String.Equals(m.Id, daEscludere, StringComparison.OrdinalIgnoreCase)).ToList()
            If candidati.Count = 0 Then candidati = elenco
            Return candidati.OrderBy(Function(m) If(m.Coda > 0, 1, 0)).ThenByDescending(Function(m) m.Miliardi).ThenByDescending(Function(m) m.Lavoratori).ThenBy(Function(m) m.Id, StringComparer.OrdinalIgnoreCase).First()
        Catch ex As Exception
            log.ScriviLog($"WGAi.ScegliModelloHorde: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Shared Function LunghezzaMessaggiHorde(messaggi As JArray) As Integer
        Dim totale As Integer = 0
        Try
            For Each messaggio As JToken In messaggi
                totale += If(messaggio.Value(Of String)("content"), "").Length
            Next
        Catch ex As Exception
            log.ScriviLog($"WGAi.LunghezzaMessaggiHorde: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return totale
    End Function

    ''' <summary>
    ''' Accorcia i messaggi perché stiano nel contesto del modello: tiene il messaggio di sistema e i più recenti,
    ''' e se non basta taglia la fine dell'ultimo.
    ''' </summary>
    Private Shared Function RiduciMessaggiPerHorde(messaggi As JArray, massimoCaratteri As Integer) As JArray
        Try
            If messaggi Is Nothing OrElse messaggi.Count = 0 Then Return messaggi
            If massimoCaratteri < 500 Then massimoCaratteri = 500
            Dim copia As New JArray()
            For Each messaggio As JToken In messaggi
                copia.Add(messaggio.DeepClone())
            Next
            While LunghezzaMessaggiHorde(copia) > massimoCaratteri
                Dim primoRimovibile As Integer = If(String.Equals(If(copia(0).Value(Of String)("role"), ""), "system", StringComparison.OrdinalIgnoreCase), 1, 0)
                If copia.Count - primoRimovibile <= 1 Then Exit While
                copia.RemoveAt(primoRimovibile)
            End While
            Dim eccesso As Integer = LunghezzaMessaggiHorde(copia) - massimoCaratteri
            If eccesso > 0 Then
                Dim ultimo As JObject = TryCast(copia(copia.Count - 1), JObject)
                If ultimo IsNot Nothing Then
                    Dim contenuto As String = If(ultimo.Value(Of String)("content"), "")
                    ultimo("content") = contenuto.Substring(0, Math.Max(0, contenuto.Length - eccesso))
                End If
            End If
            Return copia
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiduciMessaggiPerHorde: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return messaggi
        End Try
    End Function

    Private Shared Async Function InviaHordeUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim testoCompleto As String = domanda
            Dim fileNonSupportati As New List(Of String)
            For Each percorsoFile As String In file
                If EEstensioneTesto(percorsoFile) Then
                    Try
                        Dim contenutoFile As String = System.IO.File.ReadAllText(percorsoFile, Encoding.UTF8)
                        If contenutoFile.Length > LunghezzaMassimaTestoAllegatoHorde Then
                            contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoHorde)
                        End If
                        testoCompleto &= Environment.NewLine & Environment.NewLine & "Contenuto del file allegato (" & Path.GetFileName(percorsoFile) & "):" & Environment.NewLine & contenutoFile
                    Catch exLettura As Exception
                        log.ScriviLog($"WGAi.InviaHordeUnaVoltaAsync (lettura allegato): {exLettura.Message}")
                        fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                    End Try
                Else
                    fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                End If
            Next
            If fileNonSupportati.Count > 0 Then
                testoCompleto &= Environment.NewLine & Environment.NewLine & "(Nota: AI Horde non supporta questi allegati, sono stati ignorati: " & String.Join(", ", fileNonSupportati) & ")"
            End If

            Dim messaggi As New JArray From {New JObject From {{"role", "user"}, {"content", testoCompleto}}}
            Return Await EseguiRichiestaHordeAsync(modello, messaggi, token, testoFormattato)
        Catch ex As Exception
            log.ScriviLog($"WGAi.InviaHordeUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore AI Horde: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' Una richiesta di chat ad AI Horde (API compatibile OpenAI) con la chiave anonima. Il modello chiesto si usa solo se
    ''' in questo momento è servito, altrimenti se ne sceglie uno tra quelli disponibili; la lunghezza della domanda e della
    ''' risposta segue i limiti dichiarati da chi ospita il modello.
    ''' </summary>
    Public Shared Async Function EseguiRichiestaHordeAsync(modello As String, messaggi As JArray, token As CancellationToken, Optional testoFormattato As Boolean = False) As Task(Of Tuple(Of String, String))
        Try
            Dim url As String = UrlBaseHorde & "/chat/completions"
            Dim daEscludere As String = Nothing
            Dim ultimoErrore As String = Nothing

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(5)

                For tentativo As Integer = 1 To NumeroMassimoTentativiHorde
                    Dim elenco As List(Of ModelloHorde) = Await ModelliHordeAsync(tentativo > 1)
                    Dim scelto As ModelloHorde = ScegliModelloHorde(elenco, modello, daEscludere)
                    If scelto Is Nothing Then
                        ultimoErrore = "In questo momento AI Horde non ha modelli disponibili"
                        If tentativo < NumeroMassimoTentativiHorde Then
                            Await Task.Delay(TimeSpan.FromSeconds(15), token)
                            Continue For
                        End If
                        Exit For
                    End If

                    Dim massimoRisposta As Integer = Math.Max(128, Math.Min(scelto.MassimoRisposta, Math.Max(256, scelto.Contesto \ 2)))
                    Dim massimoCaratteri As Integer = Math.Max(500, (scelto.Contesto - massimoRisposta) * 3)
                    Dim corpoRichiesta As New JObject From {{"model", scelto.Id}, {"messages", RiduciMessaggiPerHorde(messaggi, massimoCaratteri)}, {"max_tokens", massimoRisposta}}
                    Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)

                    Dim eTemporaneo As Boolean = False
                    Dim attesaSecondi As Integer = 10
                    Try
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", ChiaveAnonimaHorde)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    ultimoErrore = If(eTemporaneo, "AI Horde è momentaneamente sovraccarico", DescriviErrorePermanente("AI Horde", codiceStato))
                                    ' Il modello non è più servito, o chi lo ospita non accetta la richiesta: al prossimo tentativo se ne sceglie un altro.
                                    If codiceStato = 503 OrElse codiceStato = 404 OrElse codiceStato = 400 Then
                                        eTemporaneo = True
                                        daEscludere = scelto.Id
                                        attesaSecondi = 2
                                    ElseIf codiceStato = 429 Then
                                        attesaSecondi = Math.Min(Math.Max(AttesaSuggeritaGroq(risposta), 15), 60)
                                    End If
                                    log.ScriviLog($"WGAi.EseguiRichiestaHordeAsync: tentativo {tentativo} di {NumeroMassimoTentativiHorde}, modello {scelto.Id}, risposta HTTP {codiceStato} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Dim risultato As JObject = JObject.Parse(corpoRisposta)
                                    Dim scelte As JArray = TryCast(risultato("choices"), JArray)
                                    Dim gettoneTesto As JToken = If(scelte IsNot Nothing AndAlso scelte.Count > 0, scelte(0).SelectToken("message.content"), Nothing)
                                    Dim testoHorde As String = If(gettoneTesto Is Nothing, "", gettoneTesto.ToString().Trim())
                                    If String.IsNullOrWhiteSpace(testoHorde) Then
                                        log.ScriviLog($"WGAi.EseguiRichiestaHordeAsync: tentativo {tentativo} di {NumeroMassimoTentativiHorde}, modello {scelto.Id}, risposta senza testo.")
                                        ultimoErrore = "AI Horde non ha restituito testo"
                                        eTemporaneo = True
                                        daEscludere = scelto.Id
                                        attesaSecondi = 2
                                    Else
                                        If testoFormattato Then Return New Tuple(Of String, String)(RimuoviRecinzioniCodice(testoHorde).Trim(), Nothing)
                                        Return New Tuple(Of String, String)(PuliscoTestoAi(testoHorde), Nothing)
                                    End If
                                End If
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        ultimoErrore = "Errore di connessione verso AI Horde: " & exRete.Message
                        log.ScriviLog($"WGAi.EseguiRichiestaHordeAsync: tentativo {tentativo} di {NumeroMassimoTentativiHorde}, eccezione di rete: {exRete.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        ultimoErrore = "AI Horde non ha risposto in tempo"
                        daEscludere = scelto.Id
                        log.ScriviLog($"WGAi.EseguiRichiestaHordeAsync: tentativo {tentativo} di {NumeroMassimoTentativiHorde}, modello {scelto.Id}, tempo scaduto")
                    End Try

                    If Not eTemporaneo Then Exit For
                    If tentativo < NumeroMassimoTentativiHorde Then Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                Next
            End Using

            Return New Tuple(Of String, String)(Nothing, If(ultimoErrore, "AI Horde non ha risposto dopo diversi tentativi"))
        Catch ex As Exception
            log.ScriviLog($"WGAi.EseguiRichiestaHordeAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore AI Horde: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function GeneraAudioBloccoAsync(testo As String, percorsoFileDestinazione As String, Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return "Il testo è vuoto"
            If String.IsNullOrWhiteSpace(percorsoFileDestinazione) Then Return "Percorso di destinazione non valido"
            If Not ChiaveApiConfigurata(ProviderAi.Gemini) Then Return "La sintesi vocale con l'intelligenza artificiale richiede la chiave API di Gemini"

            Using cts As New CancellationTokenSource()
                Return Await GeneraAudioGeminiAsync(testo, percorsoFileDestinazione, cts.Token)
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.GeneraAudioBloccoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante la generazione dell'audio: " & ex.Message
        End Try
    End Function

    Public Shared Async Function GeneraAudioDaTestoAsync(testo As String, percorsoFileDestinazione As String, Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return "Il testo è vuoto"
            If String.IsNullOrWhiteSpace(percorsoFileDestinazione) Then Return "Percorso di destinazione non valido"
            If Not ChiaveApiConfigurata(ProviderAi.Gemini) Then Return "La sintesi vocale con l'intelligenza artificiale richiede la chiave API di Gemini"

            Dim blocchi As List(Of String) = DividiInBlocchi(testo, MassimoParoleBloccoTts)

            If blocchi.Count <= 1 Then
                Return Await GeneraAudioBloccoAsync(testo, percorsoFileDestinazione, providerSovrascritto)
            End If

            Dim percorsiTemporanei As New List(Of String)
            Try
                For indiceBlocco As Integer = 0 To blocchi.Count - 1
                    Dim percorsoTemporaneo As String = Path.Combine(Path.GetTempPath(), "wgai_audio_" & Guid.NewGuid().ToString("N") & ".wav")
                    Dim erroreBlocco As String = Await GeneraAudioBloccoAsync(blocchi(indiceBlocco), percorsoTemporaneo, providerSovrascritto)

                    If Not String.IsNullOrEmpty(erroreBlocco) Then
                        Return "Audio interrotto alla parte " & (indiceBlocco + 1).ToString() & " di " & blocchi.Count.ToString() & ": " & erroreBlocco
                    End If
                    percorsiTemporanei.Add(percorsoTemporaneo)
                Next

                Return UnisciFileWav(percorsiTemporanei, percorsoFileDestinazione)
            Finally
                For Each percorsoTemp As String In percorsiTemporanei
                    Try
                        If File.Exists(percorsoTemp) Then File.Delete(percorsoTemp)
                    Catch exPulizia As Exception
                        log.ScriviLog($"WGAi.GeneraAudioDaTestoAsync (pulizia temporanei): {exPulizia.Message}")
                    End Try
                Next
            End Try
        Catch ex As Exception
            log.ScriviLog($"WGAi.GeneraAudioDaTestoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante la generazione dell'audio: " & ex.Message
        End Try
    End Function

    Private Shared Async Function GeneraAudioGeminiAsync(testo As String, percorsoFileDestinazione As String, token As CancellationToken) As Task(Of String)
        Try
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return "Chiave API di Gemini non configurata"

            Dim numeroParole As Integer = testo.Split({" "c, ControlChars.Lf, ControlChars.Cr, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries).Length
            Dim tokenMassimi As Integer = Math.Min(30000, Math.Max(1500, CInt(Math.Ceiling(numeroParole / 2.0 * 1.5 * 25.0))))
            Dim corpoRichiesta As New JObject From {
                {"contents", New JArray From {New JObject From {{"parts", New JArray From {New JObject From {{"text", testo}}}}}}},
                {"generationConfig", New JObject From {
                    {"maxOutputTokens", tokenMassimi},
                    {"responseModalities", New JArray From {"AUDIO"}},
                    {"speechConfig", New JObject From {
                        {"voiceConfig", New JObject From {
                            {"prebuiltVoiceConfig", New JObject From {{"voiceName", VoceGeminiTts}}}
                        }}
                    }}
                }}
            }
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models/" & ModelloGeminiTts & ":generateContent"

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                For tentativo As Integer = 1 To NumeroMassimoTentativi
                    Dim eTemporaneo As Boolean = False
                    Dim messaggioErroreTentativo As String = Nothing

                    AttendiLimiteRichiesteTts(_tempiRichiesteTtsGemini, _lockLimiteTtsGemini)

                    Try
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Headers.Add("x-goog-api-key", chiave)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                RegistraRichiestaGemini()
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    messaggioErroreTentativo = If(eTemporaneo, "Errore da Gemini: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Gemini", codiceStato))
                                    log.ScriviLog($"WGAi.GeneraAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Return SalvaAudioGeminiComeWav(corpoRisposta, percorsoFileDestinazione)
                                End If
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Errore di connessione verso Gemini: " & exRete.Message
                        log.ScriviLog($"WGAi.GeneraAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        messaggioErroreTentativo = "Timeout della richiesta verso Gemini"
                        log.ScriviLog($"WGAi.GeneraAudioGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                    End Try

                    If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                        Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                        Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                        Continue For
                    End If

                    If eTemporaneo Then Return "Gemini è momentaneamente sovraccarico"
                    Return If(messaggioErroreTentativo, "Gemini non ha risposto dopo diversi tentativi")
                Next

                Return "Gemini non ha risposto dopo diversi tentativi"
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.GeneraAudioGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore Gemini: " & ex.Message
        End Try
    End Function

    Private Shared Function SalvaAudioGeminiComeWav(corpoRisposta As String, percorsoFileDestinazione As String) As String
        Try
            Dim risultato As JObject = JObject.Parse(corpoRisposta)
            Dim candidati As JArray = TryCast(risultato("candidates"), JArray)
            If candidati Is Nothing OrElse candidati.Count = 0 Then Return "Gemini non ha restituito audio"

            Dim primoCandidato As JObject = TryCast(candidati(0), JObject)
            Dim contenuto As JObject = If(primoCandidato IsNot Nothing, TryCast(primoCandidato("content"), JObject), Nothing)
            Dim parti As JArray = If(contenuto IsNot Nothing, TryCast(contenuto("parts"), JArray), Nothing)
            If parti Is Nothing OrElse parti.Count = 0 Then Return "Gemini non ha restituito audio"

            Dim datiAudioBase64 As String = Nothing
            Dim mimeType As String = "audio/L16;codec=pcm;rate=24000"
            For Each parte As JObject In parti
                Dim datiInline As JObject = TryCast(parte("inlineData"), JObject)
                If datiInline IsNot Nothing Then
                    Dim gettoneDati As JToken = datiInline("data")
                    If gettoneDati IsNot Nothing Then
                        datiAudioBase64 = gettoneDati.ToString()
                        Dim gettoneMime As JToken = datiInline("mimeType")
                        If gettoneMime IsNot Nothing Then mimeType = gettoneMime.ToString()
                        Exit For
                    End If
                End If
            Next

            If String.IsNullOrWhiteSpace(datiAudioBase64) Then Return "Gemini non ha restituito dati audio"

            Dim datiPcm() As Byte = Convert.FromBase64String(datiAudioBase64)
            Dim frequenzaCampionamento As Integer = 24000
            Dim corrispondenza As Match = Regex.Match(mimeType, "rate=(\d+)")
            If corrispondenza.Success Then Integer.TryParse(corrispondenza.Groups(1).Value, frequenzaCampionamento)

            ScriviFileWav(percorsoFileDestinazione, datiPcm, frequenzaCampionamento, 1, 16)
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGAi.SalvaAudioGeminiComeWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante il salvataggio dell'audio: " & ex.Message
        End Try
    End Function

    Private Shared Sub ScriviFileWav(percorsoFile As String, datiPcm() As Byte, frequenzaCampionamento As Integer, numeroCanali As Short, bitPerCampione As Short)
        Try
            Dim byteRate As Integer = frequenzaCampionamento * numeroCanali * (bitPerCampione \ 8)
            Dim blockAlign As Short = CShort(numeroCanali * (bitPerCampione \ 8))
            Dim dimensioneDati As Integer = datiPcm.Length

            Using flusso As New FileStream(percorsoFile, FileMode.Create, FileAccess.Write)
                Using scrittore As New BinaryWriter(flusso)
                    scrittore.Write(Encoding.ASCII.GetBytes("RIFF"))
                    scrittore.Write(36 + dimensioneDati)
                    scrittore.Write(Encoding.ASCII.GetBytes("WAVE"))
                    scrittore.Write(Encoding.ASCII.GetBytes("fmt "))
                    scrittore.Write(CInt(16))
                    scrittore.Write(CShort(1))
                    scrittore.Write(numeroCanali)
                    scrittore.Write(frequenzaCampionamento)
                    scrittore.Write(byteRate)
                    scrittore.Write(blockAlign)
                    scrittore.Write(bitPerCampione)
                    scrittore.Write(Encoding.ASCII.GetBytes("data"))
                    scrittore.Write(dimensioneDati)
                    scrittore.Write(datiPcm)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.ScriviFileWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function UnisciFileWav(percorsiFile As List(Of String), percorsoDestinazione As String) As String
        Try
            If percorsiFile Is Nothing OrElse percorsiFile.Count = 0 Then Return "Nessun file audio da unire"

            Dim frequenzaCampionamento As Integer = 0
            Dim numeroCanali As Short = 0
            Dim bitPerCampione As Short = 0
            Dim datiUniti As New List(Of Byte)

            For Each percorsoFile As String In percorsiFile
                Dim datiPcm() As Byte = Nothing
                Dim frequenzaLocale As Integer = 0
                Dim canaliLocali As Short = 0
                Dim bitLocali As Short = 0

                Dim erroreLettura As String = LeggiDatiPcmDaWav(percorsoFile, datiPcm, frequenzaLocale, canaliLocali, bitLocali)
                If Not String.IsNullOrEmpty(erroreLettura) Then Return erroreLettura

                If frequenzaCampionamento = 0 Then
                    frequenzaCampionamento = frequenzaLocale
                    numeroCanali = canaliLocali
                    bitPerCampione = bitLocali
                End If

                datiUniti.AddRange(datiPcm)
            Next

            ScriviFileWav(percorsoDestinazione, datiUniti.ToArray(), frequenzaCampionamento, numeroCanali, bitPerCampione)
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGAi.UnisciFileWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante l'unione dei file audio: " & ex.Message
        End Try
    End Function

    Private Shared Function LeggiDatiPcmDaWav(percorsoFile As String, ByRef datiPcm() As Byte, ByRef frequenzaCampionamento As Integer, ByRef numeroCanali As Short, ByRef bitPerCampione As Short) As String
        Try
            Using flusso As New FileStream(percorsoFile, FileMode.Open, FileAccess.Read)
                Using lettore As New BinaryReader(flusso)
                    Dim idRiff As String = New String(lettore.ReadChars(4))
                    If idRiff <> "RIFF" Then Return "File audio non valido (" & Path.GetFileName(percorsoFile) & ")"
                    lettore.ReadInt32()
                    Dim idWave As String = New String(lettore.ReadChars(4))
                    If idWave <> "WAVE" Then Return "File audio non valido (" & Path.GetFileName(percorsoFile) & ")"

                    Dim trovatoFmt As Boolean = False
                    Dim trovatoData As Boolean = False

                    Do While flusso.Position < flusso.Length AndAlso Not trovatoData
                        Dim idBlocco As String = New String(lettore.ReadChars(4))
                        Dim dimensioneBlocco As Integer = lettore.ReadInt32()

                        If idBlocco = "fmt " Then
                            Dim posizioneFineBlocco As Long = flusso.Position + dimensioneBlocco
                            lettore.ReadInt16()
                            numeroCanali = lettore.ReadInt16()
                            frequenzaCampionamento = lettore.ReadInt32()
                            lettore.ReadInt32()
                            lettore.ReadInt16()
                            bitPerCampione = lettore.ReadInt16()
                            flusso.Position = posizioneFineBlocco
                            trovatoFmt = True
                        ElseIf idBlocco = "data" Then
                            datiPcm = lettore.ReadBytes(dimensioneBlocco)
                            trovatoData = True
                        Else
                            flusso.Position += dimensioneBlocco
                        End If

                        If dimensioneBlocco Mod 2 <> 0 AndAlso flusso.Position < flusso.Length Then flusso.Position += 1
                    Loop

                    If Not trovatoFmt OrElse Not trovatoData Then Return "Impossibile leggere l'audio generato (" & Path.GetFileName(percorsoFile) & ")"
                End Using
            End Using
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGAi.LeggiDatiPcmDaWav: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante la lettura dell'audio: " & ex.Message
        End Try
    End Function

    Public Shared Async Function RiassumiEmailAsync(testoEmail As String) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(testoEmail) Then Return New Tuple(Of String, String)(Nothing, "Il messaggio è vuoto")
            Dim prompt As New StringBuilder()
            prompt.Append("Riassumi in italiano l'email riportata qui sotto, in modo breve e chiaro. ")
            prompt.Append("Nella prima frase indica chi scrive e che cosa vuole; poi riporta i punti importanti, le richieste rivolte a chi legge, le scadenze, le date e gli importi citati. ")
            prompt.Append("Se l'email contiene una conversazione con messaggi citati, riassumi soprattutto l'ultimo messaggio e usa i precedenti solo come contesto. ")
            prompt.Append("Scrivi solo il riassunto, senza introduzioni e senza commenti, in testo semplice senza formattazione Markdown.")
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append("Email:").Append(Environment.NewLine)
            prompt.Append(TroncaTesto(testoEmail, LunghezzaMassimaTestoEmail))
            Return Await OttieniRispostaAsync(prompt.ToString())
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiEmailAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto del messaggio: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function ScriviRispostaEmailAsync(testoEmailOriginale As String, istruzioni As String, formatoHtml As Boolean) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(istruzioni) Then Return New Tuple(Of String, String)(Nothing, "Indica che cosa vuoi rispondere")
            If String.IsNullOrWhiteSpace(testoEmailOriginale) Then Return New Tuple(Of String, String)(Nothing, "Il messaggio a cui rispondere è vuoto")
            Dim prompt As New StringBuilder()
            prompt.Append("Scrivi la risposta all'email riportata più sotto, seguendo le indicazioni di chi risponde.")
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append("Indicazioni di chi risponde: ").Append(istruzioni.Trim())
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append(RegoleScritturaEmail(formatoHtml, True))
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append("Email a cui rispondere:").Append(Environment.NewLine)
            prompt.Append(TroncaTesto(testoEmailOriginale, LunghezzaMassimaTestoEmail))
            Return Await ConcludiScritturaEmailAsync(prompt.ToString(), formatoHtml)
        Catch ex As Exception
            log.ScriviLog($"WGAi.ScriviRispostaEmailAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la scrittura della risposta: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function ScriviEmailAsync(istruzioni As String, destinatario As String, oggetto As String, testoAttuale As String, formatoHtml As Boolean) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(istruzioni) Then Return New Tuple(Of String, String)(Nothing, "Indica che cosa vuoi scrivere")
            Dim prompt As New StringBuilder()
            prompt.Append("Scrivi un'email seguendo le indicazioni di chi la manda.")
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append("Indicazioni: ").Append(istruzioni.Trim())
            If Not String.IsNullOrWhiteSpace(destinatario) Then
                prompt.Append(Environment.NewLine).Append("Destinatario: ").Append(destinatario.Trim())
            End If
            If Not String.IsNullOrWhiteSpace(oggetto) Then
                prompt.Append(Environment.NewLine).Append("Oggetto: ").Append(oggetto.Trim())
            End If
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            prompt.Append(RegoleScritturaEmail(formatoHtml, False))
            If Not String.IsNullOrWhiteSpace(testoAttuale) Then
                prompt.Append(Environment.NewLine).Append(Environment.NewLine)
                prompt.Append("Testo già scritto, da riscrivere o completare secondo le indicazioni:").Append(Environment.NewLine)
                prompt.Append(TroncaTesto(testoAttuale, LunghezzaMassimaTestoEmail))
            End If
            Return Await ConcludiScritturaEmailAsync(prompt.ToString(), formatoHtml)
        Catch ex As Exception
            log.ScriviLog($"WGAi.ScriviEmailAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la scrittura del messaggio: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function ConcludiScritturaEmailAsync(prompt As String, formatoHtml As Boolean) As Task(Of Tuple(Of String, String))
        Try
            Dim risultato As Tuple(Of String, String) = Await OttieniRispostaAsync(prompt, Nothing, Nothing, Nothing, formatoHtml)
            If Not RisultatoValido(risultato) Then Return risultato
            If Not formatoHtml Then Return New Tuple(Of String, String)(risultato.Item1.Trim(), Nothing)
            Dim html As String = ConvertiMarkdownInHtml(risultato.Item1)
            If String.IsNullOrWhiteSpace(html) Then Return New Tuple(Of String, String)(Nothing, "Il testo ricevuto è vuoto")
            Return New Tuple(Of String, String)(html, Nothing)
        Catch ex As Exception
            log.ScriviLog($"WGAi.ConcludiScritturaEmailAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la scrittura del messaggio: " & ex.Message)
        End Try
    End Function

    Private Shared Function RegoleScritturaEmail(formatoHtml As Boolean, eRisposta As Boolean) As String
        Try
            Dim regole As New StringBuilder()
            If eRisposta Then
                regole.Append("Scrivi nella lingua dell'email a cui rispondi. ")
            Else
                regole.Append("Scrivi in italiano, salvo indicazioni diverse. ")
            End If
            regole.Append("Usa un registro adatto: formale verso uffici, aziende e persone a cui si dà del lei, cordiale se il tono è confidenziale. ")
            regole.Append("Sviluppa le indicazioni in un testo completo e scorrevole, senza aggiungere fatti, date, cifre, promesse o impegni che non compaiono nelle indicazioni")
            If eRisposta Then regole.Append(" o nell'email")
            regole.Append(". ")
            regole.Append("Metti un saluto iniziale e una formula di chiusura, ma nessun nome e nessuna firma, perché la firma la aggiunge il programma. ")
            regole.Append("Restituisci solo il corpo del messaggio: niente oggetto, niente spiegazioni, niente frasi di presentazione del testo. ")
            If formatoHtml Then
                regole.Append("Formatta il testo in Markdown: paragrafi separati da una riga vuota; elenchi puntati o numerati solo quando le informazioni formano davvero un elenco; una tabella Markdown quando ci sono dati da disporre in righe e colonne, come orari, prezzi, quantità o confronti fra più voci; il grassetto solo per pochi elementi essenziali, come una data o un importo. Non usare titoli.")
            Else
                regole.Append("Scrivi in testo semplice, senza Markdown: niente asterischi, cancelletti o trattini a inizio riga. Per un elenco usa righe numerate; per dati da mettere in colonne usa una riga per voce, nella forma voce, due punti, valore.")
            End If
            Return regole.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGAi.RegoleScritturaEmail: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Async Function RiassumiArticoloAsync(titolo As String, testoArticolo As String) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(testoArticolo) Then Return New Tuple(Of String, String)(Nothing, "L'articolo è vuoto")
            Dim prompt As New StringBuilder()
            prompt.Append("Riassumi in italiano l'articolo di giornale riportato qui sotto, in modo breve e chiaro. ")
            prompt.Append("Nella prima frase indica il fatto principale: chi, che cosa, dove e quando. Poi riporta gli sviluppi, le cause e le conseguenze, e le posizioni delle persone citate, attribuendo a ciascuno le proprie affermazioni. ")
            prompt.Append("Non aggiungere informazioni che non compaiono nell'articolo e non esprimere opinioni. ")
            prompt.Append("Ignora pubblicità, inviti all'abbonamento, didascalie e collegamenti ad altri articoli. ")
            prompt.Append("Scrivi solo il riassunto, senza introduzioni e senza commenti, in testo semplice senza formattazione Markdown.")
            prompt.Append(Environment.NewLine).Append(Environment.NewLine)
            If Not String.IsNullOrWhiteSpace(titolo) Then prompt.Append("Titolo: ").Append(titolo.Trim()).Append(Environment.NewLine)
            prompt.Append("Articolo:").Append(Environment.NewLine)
            prompt.Append(TroncaTesto(TestoDaHtml(testoArticolo), LunghezzaMassimaTestoEmail))
            Return Await OttieniRispostaAsync(prompt.ToString())
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiArticoloAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto dell'articolo: " & ex.Message)
        End Try
    End Function

    Public Shared Function ResocontoSoloConGroq() As Boolean
        Try
            Return Not ChiaveApiConfigurata(ProviderAi.Gemini) AndAlso ChiaveApiConfigurata(ProviderAi.Groq)
        Catch ex As Exception
            log.ScriviLog($"WGAi.ResocontoSoloConGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Async Function CreaResocontoNotizieAsync(articoli As List(Of Tuple(Of String, String, String))) As Task(Of Tuple(Of String, String))
        Try
            If articoli Is Nothing OrElse articoli.Count = 0 Then Return New Tuple(Of String, String)(Nothing, "Nessun articolo da riassumere")
            Dim risultatoGemini As Tuple(Of String, String) = Nothing

            If ChiaveApiConfigurata(ProviderAi.Gemini) Then
                Dim materiale As New StringBuilder()
                Dim numero As Integer = 0
                For Each articolo As Tuple(Of String, String, String) In articoli
                    numero += 1
                    AggiungiArticoloAlMateriale(materiale, numero, articolo, 3500)
                Next
                Dim prompt As String = IstruzioniResoconto(False) & Environment.NewLine & Environment.NewLine & materiale.ToString()
                risultatoGemini = Await OttieniRispostaAsync(prompt, Nothing, ProviderAi.Gemini)
                If RisultatoValido(risultatoGemini) Then Return risultatoGemini
            End If

            If Not ChiaveApiConfigurata(ProviderAi.Groq) Then
                Return If(risultatoGemini, New Tuple(Of String, String)(Nothing, "Nessuna chiave API configurata"))
            End If
            Return Await CreaResocontoConGroqAsync(articoli)
        Catch ex As Exception
            log.ScriviLog($"WGAi.CreaResocontoNotizieAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la preparazione del resoconto: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function CreaResocontoConGroqAsync(articoli As List(Of Tuple(Of String, String, String))) As Task(Of Tuple(Of String, String))
        Try
            Dim sintesi As New StringBuilder()
            Dim numero As Integer = 0
            Dim ultimoErrore As String = ""
            For Each articolo As Tuple(Of String, String, String) In articoli
                If numero >= ArticoliMassimiResocontoGroq Then Exit For
                numero += 1
                Dim testoArticolo As String = TroncaTesto(TestoDaHtml(articolo.Item3), LunghezzaMassimaArticoloGroq)
                Dim prompt As New StringBuilder()
                prompt.Append("Riassumi in italiano, in tre o quattro frasi, l'articolo di giornale riportato qui sotto: chi, che cosa, dove, quando e perché. ")
                prompt.Append("Non aggiungere informazioni e non esprimere opinioni. Scrivi solo il riassunto, in testo semplice.")
                prompt.Append(Environment.NewLine).Append(Environment.NewLine)
                prompt.Append("Titolo: ").Append(articolo.Item2).Append(Environment.NewLine)
                prompt.Append(testoArticolo)
                Dim risultato As Tuple(Of String, String) = Await OttieniRispostaAsync(prompt.ToString(), Nothing, ProviderAi.Groq)
                If Not RisultatoValido(risultato) Then
                    ultimoErrore = If(risultato IsNot Nothing, risultato.Item2, "")
                    If Not String.IsNullOrWhiteSpace(ultimoErrore) AndAlso ultimoErrore.IndexOf("giornaliera", StringComparison.OrdinalIgnoreCase) >= 0 Then Exit For
                    Continue For
                End If
                sintesi.AppendLine($"Articolo {numero}")
                sintesi.AppendLine("Testata: " & articolo.Item1)
                sintesi.AppendLine("Titolo: " & articolo.Item2)
                sintesi.AppendLine(risultato.Item1.Replace(vbCrLf, " ").Trim())
                sintesi.AppendLine()
            Next

            If sintesi.Length = 0 Then
                Return New Tuple(Of String, String)(Nothing, If(String.IsNullOrWhiteSpace(ultimoErrore), "Non è stato possibile riassumere gli articoli", ultimoErrore))
            End If

            Dim promptFinale As String = IstruzioniResoconto(True) & Environment.NewLine & Environment.NewLine & TroncaTesto(sintesi.ToString(), LunghezzaMassimaDomandaGroq - 2500)
            Return Await OttieniRispostaAsync(promptFinale, Nothing, ProviderAi.Groq)
        Catch ex As Exception
            log.ScriviLog($"WGAi.CreaResocontoConGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la preparazione del resoconto: " & ex.Message)
        End Try
    End Function

    Private Shared Sub AggiungiArticoloAlMateriale(materiale As StringBuilder, numero As Integer, articolo As Tuple(Of String, String, String), lunghezzaMassima As Integer)
        Try
            materiale.AppendLine($"Articolo {numero}")
            materiale.AppendLine("Testata: " & articolo.Item1)
            materiale.AppendLine("Titolo: " & articolo.Item2)
            materiale.AppendLine(TroncaTesto(TestoDaHtml(articolo.Item3), lunghezzaMassima))
            materiale.AppendLine()
        Catch ex As Exception
            log.ScriviLog($"WGAi.AggiungiArticoloAlMateriale: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function IstruzioniResoconto(daRiassunti As Boolean) As String
        Try
            Dim prompt As New StringBuilder()
            prompt.Append("Scrivi in italiano un resoconto dei fatti più importanti del giorno, basato ")
            prompt.Append(If(daRiassunti, "sui riassunti di articoli", "sugli articoli"))
            prompt.Append(" riportati qui sotto e presi da testate diverse. ")
            prompt.Append("Raggruppa gli articoli che parlano dello stesso fatto e trattalo una sola volta, indicando le testate che lo riportano. ")
            prompt.Append("Ordina i fatti per importanza: prima quelli riportati da più testate e quelli di maggiore rilievo pubblico, per ultimi quelli di interesse locale o di costume. ")
            prompt.Append("Per ogni fatto scrivi un titolo breve su una riga, poi un paragrafo che dica chi, che cosa, dove e quando, con gli sviluppi principali e le posizioni delle persone citate. ")
            prompt.Append("Separa ogni fatto dal successivo con una riga vuota. ")
            prompt.Append("Non aggiungere informazioni che non compaiono nel materiale e non esprimere opinioni. Ignora pubblicità, inviti all'abbonamento e collegamenti ad altri articoli. ")
            prompt.Append("Scrivi solo il resoconto, senza introduzioni e senza commenti finali, in testo semplice senza formattazione Markdown.")
            Return prompt.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGAi.IstruzioniResoconto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Scrivi in italiano un resoconto dei fatti più importanti del giorno basato sul materiale seguente."
        End Try
    End Function

    Public Shared Function TestoDaHtml(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            If Not Regex.IsMatch(testo, "<[a-zA-Z/!][^>]*>") Then Return testo
            Dim risultato As String = Regex.Replace(testo, "<(script|style|head)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            risultato = Regex.Replace(risultato, "<!--.*?-->", " ", RegexOptions.Singleline)
            risultato = Regex.Replace(risultato, "<(br|/p|/div|/h[1-6]|/li|/tr)[^>]*>", vbLf, RegexOptions.IgnoreCase)
            risultato = Regex.Replace(risultato, "<[^>]+>", " ")
            risultato = System.Net.WebUtility.HtmlDecode(risultato)
            risultato = Regex.Replace(risultato, "[ \t\u00A0]+", " ")
            risultato = Regex.Replace(risultato, " *\n *", vbLf)
            risultato = Regex.Replace(risultato, "\n{3,}", vbLf & vbLf)
            Return risultato.Trim()
        Catch ex As Exception
            log.ScriviLog($"WGAi.TestoDaHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(testo, "")
        End Try
    End Function

    Public Shared Async Function RiassumiVideoAsync(urlVideo As String, Optional titolo As String = "", Optional istruzioniAggiuntive As String = "") As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(urlVideo) Then Return New Tuple(Of String, String)(Nothing, "Indirizzo del video non valido")
            Dim chiave As String = LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Il riassunto dei video richiede la chiave API di Gemini")

            Dim indirizzo As String = NormalizzaIndirizzoYoutube(urlVideo)
            Dim prompt As New StringBuilder()
            prompt.Append("Riassumi in italiano il contenuto di questo video di YouTube")
            If Not String.IsNullOrWhiteSpace(titolo) Then prompt.Append(", intitolato """).Append(titolo.Trim()).Append("""")
            prompt.Append(". ")
            prompt.Append("Inizia con una frase che dica di che cosa parla, poi riporta i punti principali nell'ordine in cui compaiono. ")
            prompt.Append("Se è un tutorial elenca i passaggi; se è una notizia, un'intervista o un dibattito riporta chi parla e che cosa sostiene. ")
            prompt.Append("Scrivi solo il riassunto, senza introduzioni e senza commenti, in testo semplice senza formattazione Markdown.")
            If Not String.IsNullOrWhiteSpace(istruzioniAggiuntive) Then prompt.Append(" ").Append(istruzioniAggiuntive.Trim())

            Dim parti As New JArray From {
                New JObject From {{"file_data", New JObject From {{"file_uri", indirizzo}}}},
                New JObject From {{"text", prompt.ToString()}}
            }
            Dim contenuti As New JArray From {New JObject From {{"role", "user"}, {"parts", parti}}}

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(10)
                Using cts As New CancellationTokenSource()
                    Dim configurazione As New JObject From {{"mediaResolution", "MEDIA_RESOLUTION_LOW"}}
                    Dim esito As EsitoAi = Await EseguiRichiestaGeminiAsync(client, chiave, ModelloGeminiPredefinito, contenuti, cts.Token, False, configurazione)
                    If esito IsNot Nothing AndAlso String.IsNullOrEmpty(esito.Testo) AndAlso esito.PayloadRifiutato Then
                        esito = Await EseguiRichiestaGeminiAsync(client, chiave, ModelloGeminiPredefinito, contenuti, cts.Token)
                    End If
                    If esito Is Nothing Then Return New Tuple(Of String, String)(Nothing, "Gemini non ha risposto")
                    Return esito.ComeTupla()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGAi.RiassumiVideoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto del video: " & ex.Message)
        End Try
    End Function

    Private Shared Function NormalizzaIndirizzoYoutube(urlVideo As String) As String
        Try
            Dim indirizzo As String = urlVideo.Trim()
            Dim corrispondenza As Match = Regex.Match(indirizzo, "(?:[?&]v=|youtu\.be/|/shorts/|/embed/|/live/)([A-Za-z0-9_-]{11})")
            If corrispondenza.Success Then Return "https://www.youtube.com/watch?v=" & corrispondenza.Groups(1).Value
            If Regex.IsMatch(indirizzo, "^[A-Za-z0-9_-]{11}$") Then Return "https://www.youtube.com/watch?v=" & indirizzo
            Return indirizzo
        Catch ex As Exception
            log.ScriviLog($"WGAi.NormalizzaIndirizzoYoutube: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return urlVideo
        End Try
    End Function

    Private Shared Function TroncaTesto(testo As String, lunghezzaMassima As Integer) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            If testo.Length <= lunghezzaMassima Then Return testo
            Return testo.Substring(0, lunghezzaMassima)
        Catch ex As Exception
            log.ScriviLog($"WGAi.TroncaTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function RimuoviRecinzioniCodice(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim risultato As String = Regex.Replace(testo, "```[a-zA-Z0-9]*[ \t]*\r?\n?", "")
            Return risultato.Replace("```", "")
        Catch ex As Exception
            log.ScriviLog($"WGAi.RimuoviRecinzioniCodice: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return If(testo, "")
        End Try
    End Function

    Public Shared Function ConvertiMarkdownInHtml(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim sorgente As String = RimuoviRecinzioniCodice(testo).Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim righe() As String = sorgente.Split(New Char() {ControlChars.Lf})
            Dim html As New StringBuilder()
            Dim paragrafo As New List(Of String)
            Dim tipoElenco As String = ""
            Dim indice As Integer = 0

            Do While indice < righe.Length
                Dim contenuto As String = righe(indice).Trim()

                If contenuto.Length = 0 OrElse Regex.IsMatch(contenuto, "^(-{3,}|\*{3,}|_{3,})$") Then
                    ChiudiParagrafoHtml(html, paragrafo)
                    ChiudiElencoHtml(html, tipoElenco)
                    indice += 1
                    Continue Do
                End If

                If contenuto.StartsWith("|") AndAlso indice + 1 < righe.Length AndAlso RigaSeparatoreTabella(righe(indice + 1)) Then
                    ChiudiParagrafoHtml(html, paragrafo)
                    ChiudiElencoHtml(html, tipoElenco)
                    Dim righeTabella As New List(Of String)
                    righeTabella.Add(contenuto)
                    indice += 2
                    Do While indice < righe.Length AndAlso righe(indice).Trim().StartsWith("|")
                        righeTabella.Add(righe(indice).Trim())
                        indice += 1
                    Loop
                    html.Append(CostruisciTabellaHtml(righeTabella))
                    Continue Do
                End If

                Dim titolo As Match = Regex.Match(contenuto, "^(#{1,6})\s+(.+)$")
                If titolo.Success Then
                    ChiudiParagrafoHtml(html, paragrafo)
                    ChiudiElencoHtml(html, tipoElenco)
                    Dim livello As Integer = Math.Min(Math.Max(titolo.Groups(1).Value.Length, 2), 4)
                    html.Append("<h").Append(livello).Append(">").Append(FormattaInlineHtml(titolo.Groups(2).Value.Trim().TrimEnd("#"c).Trim())).Append("</h").Append(livello).Append(">")
                    indice += 1
                    Continue Do
                End If

                Dim puntato As Match = Regex.Match(contenuto, "^[-*+•]\s+(.+)$")
                Dim numerato As Match = Regex.Match(contenuto, "^\d+[.)]\s+(.+)$")
                If puntato.Success OrElse numerato.Success Then
                    ChiudiParagrafoHtml(html, paragrafo)
                    Dim tipo As String = If(puntato.Success, "ul", "ol")
                    If tipoElenco <> tipo Then
                        ChiudiElencoHtml(html, tipoElenco)
                        html.Append("<").Append(tipo).Append(">")
                        tipoElenco = tipo
                    End If
                    Dim voce As String = If(puntato.Success, puntato.Groups(1).Value, numerato.Groups(1).Value)
                    html.Append("<li>").Append(FormattaInlineHtml(voce)).Append("</li>")
                    indice += 1
                    Continue Do
                End If

                ChiudiElencoHtml(html, tipoElenco)
                paragrafo.Add(FormattaInlineHtml(contenuto))
                indice += 1
            Loop

            ChiudiParagrafoHtml(html, paragrafo)
            ChiudiElencoHtml(html, tipoElenco)
            Return html.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGAi.ConvertiMarkdownInHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "<p>" & System.Net.WebUtility.HtmlEncode(If(testo, "")).Replace(vbLf, "<br>") & "</p>"
        End Try
    End Function

    Private Shared Sub ChiudiParagrafoHtml(html As StringBuilder, paragrafo As List(Of String))
        Try
            If paragrafo.Count = 0 Then Return
            html.Append("<p>").Append(String.Join("<br>", paragrafo)).Append("</p>")
            paragrafo.Clear()
        Catch ex As Exception
            log.ScriviLog($"WGAi.ChiudiParagrafoHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Sub ChiudiElencoHtml(html As StringBuilder, ByRef tipoElenco As String)
        Try
            If String.IsNullOrEmpty(tipoElenco) Then Return
            html.Append("</").Append(tipoElenco).Append(">")
            tipoElenco = ""
        Catch ex As Exception
            log.ScriviLog($"WGAi.ChiudiElencoHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RigaSeparatoreTabella(riga As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(riga) Then Return False
            Return Regex.IsMatch(riga.Trim(), "^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$")
        Catch ex As Exception
            log.ScriviLog($"WGAi.RigaSeparatoreTabella: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function DividiCelleTabella(riga As String) As List(Of String)
        Dim celle As New List(Of String)
        Try
            Dim contenuto As String = riga.Trim()
            If contenuto.StartsWith("|") Then contenuto = contenuto.Substring(1)
            If contenuto.EndsWith("|") Then contenuto = contenuto.Substring(0, contenuto.Length - 1)
            For Each cella As String In contenuto.Split(New Char() {"|"c})
                celle.Add(cella.Trim())
            Next
        Catch ex As Exception
            log.ScriviLog($"WGAi.DividiCelleTabella: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return celle
    End Function

    Private Shared Function CostruisciTabellaHtml(righeTabella As List(Of String)) As String
        Try
            If righeTabella Is Nothing OrElse righeTabella.Count = 0 Then Return ""
            Dim intestazione As List(Of String) = DividiCelleTabella(righeTabella(0))
            Dim colonne As Integer = intestazione.Count
            Dim html As New StringBuilder()
            html.Append("<table border=""1"" cellpadding=""4"" cellspacing=""0"" style=""border-collapse:collapse"">")
            html.Append("<tr>")
            For Each cella As String In intestazione
                html.Append("<th>").Append(FormattaInlineHtml(cella)).Append("</th>")
            Next
            html.Append("</tr>")
            For indiceRiga As Integer = 1 To righeTabella.Count - 1
                Dim celle As List(Of String) = DividiCelleTabella(righeTabella(indiceRiga))
                html.Append("<tr>")
                For indiceColonna As Integer = 0 To colonne - 1
                    Dim valore As String = If(indiceColonna < celle.Count, celle(indiceColonna), "")
                    html.Append("<td>").Append(FormattaInlineHtml(valore)).Append("</td>")
                Next
                html.Append("</tr>")
            Next
            html.Append("</table>")
            Return html.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGAi.CostruisciTabellaHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function FormattaInlineHtml(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim risultato As String = System.Net.WebUtility.HtmlEncode(testo)
            risultato = Regex.Replace(risultato, "\[([^\]]+)\]\(([^)\s]+)\)", "$1 ($2)")
            risultato = Regex.Replace(risultato, "\*\*(.+?)\*\*", "<b>$1</b>")
            risultato = Regex.Replace(risultato, "__(.+?)__", "<b>$1</b>")
            risultato = Regex.Replace(risultato, "(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", "<i>$1</i>")
            risultato = Regex.Replace(risultato, "`([^`]+)`", "$1")
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGAi.FormattaInlineHtml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return System.Net.WebUtility.HtmlEncode(If(testo, ""))
        End Try
    End Function

End Class