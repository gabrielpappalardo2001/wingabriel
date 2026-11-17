Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb

' Integrazione con i servizi di intelligenza artificiale per chat, elaborazione di testi, gestione della cronologia e
' interrogazione dei modelli supportati.
Public Class CLSAi
    Implements IDisposable

    Private Shared log As New Logger()

    Public Enum ProviderAi
        Gemini
        Groq
        Ovh
        Horde
    End Enum

    Public Structure MessaggioChat
        Public Chiave As String
        Public Ruolo As String
        Public Testo As String
        Public NomeFileAllegato As String
        Public PercorsoFileAllegato As String
    End Structure

    ''' <summary>
    ''' Esito di una richiesta a Gemini con function calling: valorizza NomeFunzione/Argomenti
    ''' se il modello ha scelto di chiamare una funzione, oppure Testo se ha risposto in linguaggio
    ''' naturale (caso raro con tool_config in modalità ANY, ma gestito per sicurezza), oppure Errore.
    ''' </summary>
    Public Class RispostaFunzioneAi
        Public Property NomeFunzione As String = ""
        Public Property Argomenti As JObject = Nothing
        Public Property Testo As String = ""
        Public Property Errore As String = ""
    End Class

    Private Const NumeroMassimoTentativi As Integer = 8
    Private Const SecondiMassimiFunzioniGemini As Integer = 60
    Private Const SecondiTentativoFunzioniGemini As Integer = 25
    Private Const AttesaMassimaTraTentativiSecondi As Integer = 30

    Private Const ChiaveConfigProviderPreferitoAi As String = "ai_provider_preferito"
    Private Const ChiaveConfigModelloGemini As String = "ai_modello_gemini"
    Private Const ChiaveConfigModelloGroq As String = "ai_modello_groq"
    Private Const ModelloGeminiPredefinito As String = "gemini-3.5-flash-lite"
    Private Const ModelloGroqPredefinito As String = "openai/gpt-oss-120b"
    Private Const ChiaveConfigModelloOvh As String = "ai_modello_ovh"
    Private Const ModelloOvhPredefinito As String = "Mistral-Small-3.2-24B-Instruct-2506"
    Private Const ChiaveConfigModelloHorde As String = "ai_modello_horde"
    Private Shared ReadOnly ModelliDismessi As String() = {
        "llama-3.3-70b-versatile",
        "llama-3.1-8b-instant",
        "qwen/qwen3-32b",
        "meta-llama/llama-4-scout-17b-16e-instruct",
        "meta-llama/llama-4-maverick-17b-128e-instruct",
        "gemma2-9b-it",
        "deepseek-r1-distill-llama-70b"
    }

    Private Const NomeSottocartellaDati As String = "ai"
    Private Const NomeFileDatabase As String = "conversazioni.db"
    Private Const NomeTabellaConversazioni As String = "Conversazioni"
    Private Const NomeTabellaMessaggi As String = "Messaggi"

    Private Const LunghezzaMassimaTestoAllegatoGroq As Integer = 12000
    Private Const LunghezzaMassimaConversazioneGroq As Integer = 16000

    Public Event RispostaCompletata(testo As String)
    Public Event RispostaFallita(messaggioErrore As String)

    Private _ctsRichiesta As CancellationTokenSource
    Private _disposedValue As Boolean = False
    Private _cronologia As New List(Of MessaggioChat)

    Public Property IdConversazioneCorrente As String = ""

    Public ReadOnly Property Cronologia As List(Of MessaggioChat)
        Get
            Return _cronologia
        End Get
    End Property

    Public Shared Function LeggiProviderPreferito() As ProviderAi
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigProviderPreferitoAi)
            If String.Equals(valore, "groq", StringComparison.OrdinalIgnoreCase) Then
                Return ProviderAi.Groq
            End If
            If String.Equals(valore, "ovh", StringComparison.OrdinalIgnoreCase) Then
                Return ProviderAi.Ovh
            End If
            If String.Equals(valore, "horde", StringComparison.OrdinalIgnoreCase) Then
                Return ProviderAi.Horde
            End If
            Return ProviderAi.Gemini
        Catch ex As Exception
            log.ScriviLog($"CLSAi.LeggiProviderPreferito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ProviderAi.Gemini
        End Try
    End Function

    Public Shared Sub SalvaProviderPreferito(provider As ProviderAi)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = NomeProviderInConfigurazione(provider)
            If Not cfg.EsisteId(ChiaveConfigProviderPreferitoAi) Then
                cfg.AggiungiValore(ChiaveConfigProviderPreferitoAi, valore)
            Else
                cfg.ModificaValore(ChiaveConfigProviderPreferitoAi, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"CLSAi.SalvaProviderPreferito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function NomeProviderInConfigurazione(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return "groq"
            Case ProviderAi.Ovh : Return "ovh"
            Case ProviderAi.Horde : Return "horde"
            Case Else : Return "gemini"
        End Select
    End Function

    Public Shared Function NomeProviderInChiaro(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return "Groq"
            Case ProviderAi.Ovh : Return "OVHcloud"
            Case ProviderAi.Horde : Return "AI Horde"
            Case Else : Return "Gemini"
        End Select
    End Function

    Private Shared Function ModelloPredefinitoDelProvider(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return ModelloGroqPredefinito
            Case ProviderAi.Ovh : Return ModelloOvhPredefinito
            Case ProviderAi.Horde : Return WGAi.ModelloHordeAutomatico
            Case Else : Return ModelloGeminiPredefinito
        End Select
    End Function

    ''' <summary>Corrispondente di WGAi.ProviderAi, che ha gli stessi valori.</summary>
    Private Shared Function ProviderPerWGAi(provider As ProviderAi) As WGAi.ProviderAi
        Select Case provider
            Case ProviderAi.Groq : Return WGAi.ProviderAi.Groq
            Case ProviderAi.Ovh : Return WGAi.ProviderAi.Ovh
            Case ProviderAi.Horde : Return WGAi.ProviderAi.Horde
            Case Else : Return WGAi.ProviderAi.Gemini
        End Select
    End Function

    Private Shared Function ChiaveConfigModello(provider As ProviderAi) As String
        Select Case provider
            Case ProviderAi.Groq : Return ChiaveConfigModelloGroq
            Case ProviderAi.Ovh : Return ChiaveConfigModelloOvh
            Case ProviderAi.Horde : Return ChiaveConfigModelloHorde
            Case Else : Return ChiaveConfigModelloGemini
        End Select
    End Function

    Public Shared Function LeggiModelloPredefinito(provider As ProviderAi) As String
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigModello(provider))
            Dim modelloPredefinito As String = ModelloPredefinitoDelProvider(provider)
            If String.IsNullOrWhiteSpace(valore) Then Return modelloPredefinito
            If ModelloDismesso(valore.Trim()) Then
                SalvaModelloPredefinito(provider, modelloPredefinito)
                Return modelloPredefinito
            End If
            Return valore.Trim()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.LeggiModelloPredefinito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ModelloPredefinitoDelProvider(provider)
        End Try
    End Function

    Private Shared Function ModelloDismesso(modello As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(modello) Then Return False
            For Each dismesso As String In ModelliDismessi
                If String.Equals(modello, dismesso, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            If modello.StartsWith("gemini-1.", StringComparison.OrdinalIgnoreCase) Then Return True
            If modello.StartsWith("gemini-2.", StringComparison.OrdinalIgnoreCase) Then Return True
            If modello.StartsWith("gemini-3-", StringComparison.OrdinalIgnoreCase) Then Return True
            If modello.IndexOf("-preview", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso modello.StartsWith("gemini-3.1-flash-lite", StringComparison.OrdinalIgnoreCase) Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ModelloDismesso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Sub SalvaModelloPredefinito(provider As ProviderAi, modello As String)
        Try
            If String.IsNullOrWhiteSpace(modello) Then Return
            Dim cfg As New WGConfig()
            Dim chiaveConfig As String = ChiaveConfigModello(provider)
            If Not cfg.EsisteId(chiaveConfig) Then
                cfg.AggiungiValore(chiaveConfig, modello)
            Else
                cfg.ModificaValore(chiaveConfig, modello)
            End If
        Catch ex As Exception
            log.ScriviLog($"CLSAi.SalvaModelloPredefinito: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function ChiaveApiConfigurata(provider As ProviderAi) As Boolean
        Try
            If provider = ProviderAi.Ovh Then Return WGDettatura.ChiaveApiOvhConfigurata()
            If provider = ProviderAi.Horde Then Return WGDettatura.ChiaveApiHordeConfigurata()
            If provider = ProviderAi.Groq Then Return WGDettatura.ChiaveApiGroqConfigurata()
            Return WGDettatura.ChiaveApiGeminiConfigurata()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ChiaveApiConfigurata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function AlmenoUnProviderConfigurato() As Boolean
        Try
            Return WGDettatura.ChiaveApiGeminiConfigurata() OrElse WGDettatura.ChiaveApiGroqConfigurata() OrElse WGDettatura.ChiaveApiOvhConfigurata() OrElse WGDettatura.ChiaveApiHordeConfigurata()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.AlmenoUnProviderConfigurato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function LeggiChiaveApi(provider As ProviderAi) As String
        Try
            If provider = ProviderAi.Groq Then Return WGDettatura.LeggiChiaveApiGroq()
            Return WGDettatura.LeggiChiaveApiGemini()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.LeggiChiaveApi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Async Function ElencaModelliAsync(provider As ProviderAi) As Task(Of List(Of String))
        Try
            If provider = ProviderAi.Ovh Then Return Await WGAi.ElencaModelliAsync(WGAi.ProviderAi.Ovh, False)
            If provider = ProviderAi.Horde Then
                ' I modelli di AI Horde cambiano di continuo: la prima voce lascia scegliere al programma tra quelli disponibili.
                Dim modelliHorde As List(Of String) = Await WGAi.ElencaModelliAsync(WGAi.ProviderAi.Horde, False)
                modelliHorde.Insert(0, WGAi.ModelloHordeAutomatico)
                Return modelliHorde
            End If
            If provider = ProviderAi.Groq Then Return Await ElencaModelliGroqAsync()
            Return Await ElencaModelliGeminiAsync()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ElencaModelliAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)
        End Try
    End Function

    Private Shared Async Function ElencaModelliGeminiAsync() As Task(Of List(Of String))
        Dim elenco As New List(Of String)
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return elenco

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(30)
                Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models?key=" & Uri.EscapeDataString(chiave)
                Using risposta As HttpResponseMessage = Await client.GetAsync(url)
                    WGAi.RegistraRichiestaGemini()
                    Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                    If Not risposta.IsSuccessStatusCode Then
                        log.ScriviLog($"CLSAi.ElencaModelliGeminiAsync: risposta HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                        Return elenco
                    End If

                    Dim risultato As JObject = JObject.Parse(corpo)
                    Dim modelli As JArray = TryCast(risultato("models"), JArray)
                    If modelli Is Nothing Then Return elenco

                    For Each modello As JObject In modelli
                        Dim metodi As JArray = TryCast(modello("supportedGenerationMethods"), JArray)
                        Dim supportaChat As Boolean = False
                        If metodi IsNot Nothing Then
                            For Each metodo As JToken In metodi
                                If String.Equals(metodo.ToString(), "generateContent", StringComparison.OrdinalIgnoreCase) Then
                                    supportaChat = True
                                    Exit For
                                End If
                            Next
                        End If
                        If Not supportaChat Then Continue For

                        Dim nomeCompleto As String = CStr(modello("name"))
                        If String.IsNullOrWhiteSpace(nomeCompleto) Then Continue For
                        Dim nomeBreve As String = nomeCompleto
                        If nomeBreve.StartsWith("models/") Then nomeBreve = nomeBreve.Substring("models/".Length)
                        If Not elenco.Contains(nomeBreve) Then elenco.Add(nomeBreve)
                    Next
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ElencaModelliGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return elenco
    End Function

    Private Shared Async Function ElencaModelliGroqAsync() As Task(Of List(Of String))
        Dim elenco As New List(Of String)
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGroq()
            If String.IsNullOrWhiteSpace(chiave) Then Return elenco

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(30)
                Using richiesta As New HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models")
                    richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", chiave)
                    Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta)
                        WGAi.AggiornaStatoGroqDaRisposta(risposta)
                        Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                        If Not risposta.IsSuccessStatusCode Then
                            log.ScriviLog($"CLSAi.ElencaModelliGroqAsync: risposta HTTP {CInt(risposta.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpo}")
                            Return elenco
                        End If

                        Dim risultato As JObject = JObject.Parse(corpo)
                        Dim modelli As JArray = TryCast(risultato("data"), JArray)
                        If modelli Is Nothing Then Return elenco

                        For Each modello As JObject In modelli
                            Dim id As String = CStr(modello("id"))
                            If String.IsNullOrWhiteSpace(id) Then Continue For
                            If id.IndexOf("whisper", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
                            If Not elenco.Contains(id) Then elenco.Add(id)
                        Next
                    End Using
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ElencaModelliGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return elenco
    End Function

    Private Shared Function ApriDatabase() As WGDatabase
        Dim db As New WGDatabase(NomeSottocartellaDati, NomeFileDatabase)
        db.CreaTabellaSeNonEsiste(NomeTabellaConversazioni, "Titolo TEXT, Provider TEXT, Modello TEXT, Preferita TEXT")
        db.CreaTabellaSeNonEsiste(NomeTabellaMessaggi, "IdConversazione TEXT, Ruolo TEXT, Testo TEXT, NomeFileAllegato TEXT, PercorsoFileAllegato TEXT")
        db.AssicuraColonna(NomeTabellaConversazioni, "Titolo")
        db.AssicuraColonna(NomeTabellaConversazioni, "Provider")
        db.AssicuraColonna(NomeTabellaConversazioni, "Modello")
        db.AssicuraColonna(NomeTabellaConversazioni, "Preferita")
        db.AssicuraColonna(NomeTabellaMessaggi, "IdConversazione")
        db.AssicuraColonna(NomeTabellaMessaggi, "Ruolo")
        db.AssicuraColonna(NomeTabellaMessaggi, "Testo")
        db.AssicuraColonna(NomeTabellaMessaggi, "NomeFileAllegato")
        db.AssicuraColonna(NomeTabellaMessaggi, "PercorsoFileAllegato")
        Return db
    End Function

    Private Shared Function EStatusTemporaneo(codiceStato As Integer) As Boolean
        Select Case codiceStato
            Case 408, 429, 500, 502, 503, 504
                Return True
            Case Else
                Return False
        End Select
    End Function

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

    Private Shared Function TitoloDaDomanda(domanda As String) As String
        Try
            Dim testo As String = If(domanda, "").Trim()
            testo = testo.Replace(Environment.NewLine, " ").Replace(vbLf, " ").Replace(vbCr, " ")
            If testo.Length > 60 Then testo = testo.Substring(0, 60).TrimEnd() & "..."
            If String.IsNullOrWhiteSpace(testo) Then Return "Conversazione senza titolo"
            Return testo
        Catch ex As Exception
            log.ScriviLog($"CLSAi.TitoloDaDomanda: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Conversazione senza titolo"
        End Try
    End Function

    Private Shared Function CreaConversazione(titolo As String, provider As ProviderAi, modello As String) As String
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim valori As New Dictionary(Of String, Object) From {
                    {"Titolo", titolo},
                    {"Provider", NomeProviderInConfigurazione(provider)},
                    {"Modello", modello},
                    {"Preferita", "0"}
                }
                Return db.InserisciRecord(NomeTabellaConversazioni, valori)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.CreaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Sub TocaConversazione(idConversazione As String)
        Try
            If String.IsNullOrWhiteSpace(idConversazione) Then Return
            Using db As WGDatabase = ApriDatabase()
                Dim valori As New Dictionary(Of String, Object) From {
                    {WGDatabase.NomeColonnaDataModifica, WGDatabase.DataOraCorrente()}
                }
                db.AggiornaRecord(NomeTabellaConversazioni, idConversazione, valori)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.TocaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function ArchiviaAllegato(idConversazione As String, percorsoOriginale As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoOriginale) OrElse Not File.Exists(percorsoOriginale) Then Return ""
            Dim cartellaAllegati As String = WGDatabase.CartellaDati(Path.Combine(NomeSottocartellaDati, "allegati", idConversazione))
            If String.IsNullOrWhiteSpace(cartellaAllegati) Then Return ""

            Dim nomeFile As String = Path.GetFileName(percorsoOriginale)
            Dim percorsoDestinazione As String = Path.Combine(cartellaAllegati, nomeFile)

            Dim contatore As Integer = 1
            While File.Exists(percorsoDestinazione)
                Dim nomeSenzaEstensione As String = Path.GetFileNameWithoutExtension(nomeFile)
                Dim estensione As String = Path.GetExtension(nomeFile)
                percorsoDestinazione = Path.Combine(cartellaAllegati, nomeSenzaEstensione & "_" & contatore.ToString() & estensione)
                contatore += 1
            End While

            File.Copy(percorsoOriginale, percorsoDestinazione, False)
            Return percorsoDestinazione
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ArchiviaAllegato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function AggiungiMessaggio(idConversazione As String, ruolo As String, testo As String, nomeFileAllegato As String, percorsoFileAllegato As String) As String
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim valori As New Dictionary(Of String, Object) From {
                    {"IdConversazione", idConversazione},
                    {"Ruolo", ruolo},
                    {"Testo", testo},
                    {"NomeFileAllegato", If(nomeFileAllegato, "")},
                    {"PercorsoFileAllegato", If(percorsoFileAllegato, "")}
                }
                Dim chiave As String = db.InserisciRecord(NomeTabellaMessaggi, valori)
                TocaConversazione(idConversazione)
                Return chiave
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.AggiungiMessaggio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Shared Function ElencaConversazioni(Optional soloPreferite As Boolean = False, Optional limite As Integer = 0) As DataTable
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim tabella As DataTable
                If soloPreferite Then
                    Dim parametri As New Dictionary(Of String, Object) From {{"@preferita", "1"}}
                    tabella = db.EseguiQuery("SELECT * FROM " & NomeTabellaConversazioni & " WHERE Preferita = @preferita ORDER BY " & WGDatabase.NomeColonnaDataModifica & " DESC;", parametri)
                Else
                    tabella = db.OttieniTuttiIRecord(NomeTabellaConversazioni, WGDatabase.NomeColonnaDataModifica & " DESC")
                End If

                If limite > 0 AndAlso tabella.Rows.Count > limite Then
                    Dim tabellaLimitata As DataTable = tabella.Clone()
                    For indice As Integer = 0 To limite - 1
                        tabellaLimitata.ImportRow(tabella.Rows(indice))
                    Next
                    Return tabellaLimitata
                End If

                Return tabella
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ElencaConversazioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New DataTable()
        End Try
    End Function

    Public Shared Function ImpostaPreferita(idConversazione As String, preferita As Boolean) As Boolean
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim valori As New Dictionary(Of String, Object) From {
                    {"Preferita", If(preferita, "1", "0")}
                }
                Return db.AggiornaRecord(NomeTabellaConversazioni, idConversazione, valori)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.ImpostaPreferita: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function EliminaConversazione(idConversazione As String) As Boolean
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim parametri As New Dictionary(Of String, Object) From {{"@id", idConversazione}}
                db.EseguiNonQuery("DELETE FROM " & NomeTabellaMessaggi & " WHERE IdConversazione = @id;", parametri)
                Return db.EliminaRecord(NomeTabellaConversazioni, idConversazione)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EliminaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function OttieniMessaggi(idConversazione As String) As DataTable
        Try
            Using db As WGDatabase = ApriDatabase()
                Dim parametri As New Dictionary(Of String, Object) From {{"@id", idConversazione}}
                Return db.EseguiQuery("SELECT * FROM " & NomeTabellaMessaggi & " WHERE IdConversazione = @id ORDER BY " & WGDatabase.NomeColonnaDataCreazione & " ASC;", parametri)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.OttieniMessaggi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New DataTable()
        End Try
    End Function

    Public Shared Function OttieniConversazione(idConversazione As String) As DataRow
        Try
            Using db As WGDatabase = ApriDatabase()
                Return db.OttieniRecord(NomeTabellaConversazioni, idConversazione)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.OttieniConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Sub CaricaConversazione(idConversazione As String)
        Try
            IdConversazioneCorrente = idConversazione
            _cronologia.Clear()
            Dim tabella As DataTable = OttieniMessaggi(idConversazione)
            For Each riga As DataRow In tabella.Rows
                _cronologia.Add(New MessaggioChat With {
                    .Chiave = CStr(riga(WGDatabase.NomeColonnaChiavePredefinita)),
                    .Ruolo = CStr(riga("Ruolo")),
                    .Testo = CStr(riga("Testo")),
                    .NomeFileAllegato = If(tabella.Columns.Contains("NomeFileAllegato"), CStr(riga("NomeFileAllegato")), ""),
                    .PercorsoFileAllegato = If(tabella.Columns.Contains("PercorsoFileAllegato"), CStr(riga("PercorsoFileAllegato")), "")
                })
            Next
        Catch ex As Exception
            log.ScriviLog($"CLSAi.CaricaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub NuovaConversazione()
        Try
            IdConversazioneCorrente = ""
            _cronologia.Clear()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.NuovaConversazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub Annulla()
        Try
            If _ctsRichiesta IsNot Nothing Then _ctsRichiesta.Cancel()
        Catch ex As Exception
            log.ScriviLog($"CLSAi.Annulla: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Async Function InviaAlProviderAsync(provider As ProviderAi, modello As String, percorsoAllegato As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        If provider = ProviderAi.Groq Then Return Await InviaGroqAsync(modello, percorsoAllegato, token)
        If provider = ProviderAi.Ovh Then Return Await InviaOvhAsync(modello, percorsoAllegato, token)
        If provider = ProviderAi.Horde Then Return Await InviaHordeAsync(modello, percorsoAllegato, token)
        Return Await InviaGeminiAsync(modello, percorsoAllegato, token)
    End Function

    ''' <summary>La conversazione in corso con un modello di OVHcloud: stessi messaggi di Groq, ma solo allegati di testo.</summary>
    Private Async Function InviaOvhAsync(modello As String, percorsoAllegato As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim testoAllegatoInline As String = Nothing
            If Not String.IsNullOrWhiteSpace(percorsoAllegato) Then
                If Not EEstensioneTesto(percorsoAllegato) Then
                    Return New Tuple(Of String, String)(Nothing, "OVHcloud non supporta questo tipo di allegato: usa un file di testo oppure passa a Gemini per questo tipo di file")
                End If
                Try
                    Dim contenutoFile As String = File.ReadAllText(percorsoAllegato, Encoding.UTF8)
                    If contenutoFile.Length > LunghezzaMassimaTestoAllegatoGroq Then
                        contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                    End If
                    testoAllegatoInline = "Contenuto del file allegato (" & Path.GetFileName(percorsoAllegato) & "):" & Environment.NewLine & contenutoFile
                Catch exLettura As Exception
                    log.ScriviLog($"CLSAi.InviaOvhAsync (lettura allegato): {exLettura.Message}")
                    Return New Tuple(Of String, String)(Nothing, "Impossibile leggere il file allegato")
                End Try
            End If

            Dim messaggi As JArray = RiduciMessaggiPerGroq(CostruisciMessaggiGroq(testoAllegatoInline))
            Return Await WGAi.EseguiRichiestaOvhAsync(modello, messaggi, token)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaOvhAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore OVHcloud: " & ex.Message)
        End Try
    End Function

    ''' <summary>La conversazione in corso con un modello di AI Horde: stessi messaggi di Groq, ma solo allegati di testo.</summary>
    Private Async Function InviaHordeAsync(modello As String, percorsoAllegato As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim testoAllegatoInline As String = Nothing
            If Not String.IsNullOrWhiteSpace(percorsoAllegato) Then
                If Not EEstensioneTesto(percorsoAllegato) Then
                    Return New Tuple(Of String, String)(Nothing, "Con AI Horde si possono allegare solo file di testo. Per gli altri tipi di file serve Gemini.")
                End If
                Try
                    Dim contenutoFile As String = File.ReadAllText(percorsoAllegato, Encoding.UTF8)
                    If contenutoFile.Length > LunghezzaMassimaTestoAllegatoGroq Then
                        contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                    End If
                    testoAllegatoInline = "Contenuto del file allegato (" & Path.GetFileName(percorsoAllegato) & "):" & Environment.NewLine & contenutoFile
                Catch exLettura As Exception
                    log.ScriviLog($"CLSAi.InviaHordeAsync (lettura allegato): {exLettura.Message}")
                    Return New Tuple(Of String, String)(Nothing, "Impossibile leggere il file allegato")
                End Try
            End If

            Dim messaggi As JArray = RiduciMessaggiPerGroq(CostruisciMessaggiGroq(testoAllegatoInline))
            Return Await WGAi.EseguiRichiestaHordeAsync(modello, messaggi, token)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaHordeAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore AI Horde: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' Word, OpenDocument e RTF i modelli non li leggono come sono: se ne prende il testo e si manda quello, in un file di testo
    ''' temporaneo. Gli altri file (PDF, testo, immagini, audio) partono com'erano. Il file temporaneo si toglie a fine invio.
    ''' </summary>
    Private Shared Function PreparaAllegatoPerInvio(percorso As String, ByRef temporaneo As String, ByRef nonLeggibile As Boolean) As String
        temporaneo = ""
        nonLeggibile = False
        If String.IsNullOrWhiteSpace(percorso) Then Return percorso
        Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
        If estensione <> ".docx" AndAlso estensione <> ".docm" AndAlso estensione <> ".dotx" AndAlso estensione <> ".odt" AndAlso estensione <> ".rtf" Then Return percorso
        Dim testo As String = ""
        Try
            testo = WGAi.EstraiSoloTestoDaFile(percorso)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.PreparaAllegatoPerInvio: {ex.Message}")
        End Try
        If String.IsNullOrWhiteSpace(testo) Then
            nonLeggibile = True
            Return percorso
        End If
        temporaneo = Path.Combine(Path.GetTempPath(), "wg-allegato-" & Guid.NewGuid().ToString("N") & ".txt")
        File.WriteAllText(temporaneo, "Contenuto del documento " & Path.GetFileName(percorso) & ":" & Environment.NewLine & testo, Encoding.UTF8)
        Return temporaneo
    End Function

    Public Async Sub InviaDomandaAsync(domanda As String, Optional percorsoAllegato As String = "", Optional providerSovrascritto As ProviderAi? = Nothing, Optional modelloSovrascritto As String = Nothing)
        Dim allegatoTemporaneo As String = ""
        Try
            If String.IsNullOrWhiteSpace(domanda) Then
                RaiseEvent RispostaFallita("La domanda è vuota")
                Exit Sub
            End If

            Dim provider As ProviderAi = If(providerSovrascritto.HasValue, providerSovrascritto.Value, LeggiProviderPreferito())
            If Not ChiaveApiConfigurata(provider) Then
                RaiseEvent RispostaFallita("Nessuna chiave API configurata per il motore selezionato")
                Exit Sub
            End If
            Dim modello As String = If(Not String.IsNullOrWhiteSpace(modelloSovrascritto), modelloSovrascritto, LeggiModelloPredefinito(provider))

            If String.IsNullOrWhiteSpace(IdConversazioneCorrente) Then
                IdConversazioneCorrente = CreaConversazione(TitoloDaDomanda(domanda), provider, modello)
            End If

            Dim documentoNonLeggibile As Boolean = False
            Dim percorsoInvio As String = PreparaAllegatoPerInvio(percorsoAllegato, allegatoTemporaneo, documentoNonLeggibile)
            If documentoNonLeggibile Then
                RaiseEvent RispostaFallita("Non sono riuscito a leggere il testo del documento allegato")
                Exit Sub
            End If

            Dim nomeFileAllegato As String = ""
            Dim percorsoFileArchiviato As String = ""
            If Not String.IsNullOrWhiteSpace(percorsoAllegato) Then
                Try
                    nomeFileAllegato = Path.GetFileName(percorsoAllegato)
                Catch exNome As Exception
                    log.ScriviLog($"CLSAi.InviaDomandaAsync (nome allegato): {exNome.Message}")
                End Try
                percorsoFileArchiviato = ArchiviaAllegato(IdConversazioneCorrente, percorsoAllegato)
            End If

            Dim chiaveMessaggioUtente As String = AggiungiMessaggio(IdConversazioneCorrente, "utente", domanda, nomeFileAllegato, percorsoFileArchiviato)
            _cronologia.Add(New MessaggioChat With {.Chiave = chiaveMessaggioUtente, .Ruolo = "utente", .Testo = domanda, .NomeFileAllegato = nomeFileAllegato, .PercorsoFileAllegato = percorsoFileArchiviato})

            _ctsRichiesta = New CancellationTokenSource()
            Dim token As CancellationToken = _ctsRichiesta.Token

            Dim risultato As Tuple(Of String, String)
            risultato = Await InviaAlProviderAsync(provider, modello, percorsoInvio, token)

            If token.IsCancellationRequested Then Exit Sub

            Dim primoTentativoFallito As Boolean = risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1)
            If primoTentativoFallito AndAlso Not providerSovrascritto.HasValue AndAlso String.IsNullOrWhiteSpace(modelloSovrascritto) Then
                For Each altroProvider As ProviderAi In New ProviderAi() {ProviderAi.Gemini, ProviderAi.Groq, ProviderAi.Ovh, ProviderAi.Horde}
                    If altroProvider = provider OrElse Not ChiaveApiConfigurata(altroProvider) Then Continue For
                    Dim allegatoCompatibile As Boolean = altroProvider = ProviderAi.Gemini OrElse String.IsNullOrWhiteSpace(percorsoInvio) OrElse EEstensioneTesto(percorsoInvio)
                    If Not allegatoCompatibile Then Continue For
                    Dim risultatoAltro As Tuple(Of String, String) = Await InviaAlProviderAsync(altroProvider, LeggiModelloPredefinito(altroProvider), percorsoInvio, token)
                    If token.IsCancellationRequested Then Exit Sub
                    If risultatoAltro IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultatoAltro.Item1) Then
                        risultato = risultatoAltro
                        Exit For
                    End If
                Next
            End If

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim messaggioErrore As String = If(risultato IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultato.Item2), risultato.Item2, "Nessuna risposta ricevuta")
                RaiseEvent RispostaFallita(messaggioErrore)
                Exit Sub
            End If

            Dim chiaveMessaggioAssistente As String = AggiungiMessaggio(IdConversazioneCorrente, "assistente", risultato.Item1, "", "")
            _cronologia.Add(New MessaggioChat With {.Chiave = chiaveMessaggioAssistente, .Ruolo = "assistente", .Testo = risultato.Item1})

            RaiseEvent RispostaCompletata(risultato.Item1)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaDomandaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            RaiseEvent RispostaFallita("Errore durante l'invio della domanda: " & ex.Message)
        Finally
            Try
                If allegatoTemporaneo <> "" AndAlso File.Exists(allegatoTemporaneo) Then File.Delete(allegatoTemporaneo)
            Catch
            End Try
        End Try
    End Sub

    Public Async Function InterrogaConIstruzioniAsync(istruzioni As String, domanda As String, Optional numeroMassimoTurni As Integer = 12, Optional providerPrimo As ProviderAi? = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(domanda) Then Return New Tuple(Of String, String)(Nothing, "La domanda è vuota")

            Dim preferito As ProviderAi = LeggiProviderPreferito()
            Try
                If providerPrimo.HasValue AndAlso [Enum].IsDefined(GetType(ProviderAi), providerPrimo.Value) Then preferito = providerPrimo.Value
            Catch exProvider As Exception
                log.ScriviLog($"CLSAi.InterrogaConIstruzioniAsync (provider indicato): {exProvider.Message}{Environment.NewLine}{exProvider.StackTrace}")
            End Try
            Dim ordine As New List(Of ProviderAi)
            If ChiaveApiConfigurata(preferito) Then ordine.Add(preferito)
            For Each altro As ProviderAi In New ProviderAi() {ProviderAi.Gemini, ProviderAi.Groq, ProviderAi.Ovh, ProviderAi.Horde}
                If altro <> preferito AndAlso ChiaveApiConfigurata(altro) Then ordine.Add(altro)
            Next
            If ordine.Count = 0 Then Return New Tuple(Of String, String)(Nothing, "Nessuna chiave API configurata")

            Dim turni As New List(Of MessaggioChat)
            Dim inizio As Integer = Math.Max(0, _cronologia.Count - Math.Max(0, numeroMassimoTurni))
            For indice As Integer = inizio To _cronologia.Count - 1
                If Not String.IsNullOrWhiteSpace(_cronologia(indice).Testo) Then turni.Add(_cronologia(indice))
            Next

            Dim ultimoErrore As String = Nothing
            Using cts As New CancellationTokenSource(TimeSpan.FromMinutes(2))
                For Each provider As ProviderAi In ordine
                    Dim modello As String = LeggiModelloPredefinito(provider)
                    Dim risultato As Tuple(Of String, String)
                    If provider = ProviderAi.Groq OrElse provider = ProviderAi.Ovh OrElse provider = ProviderAi.Horde Then
                        Dim messaggi As New JArray()
                        messaggi.Add(New JObject From {{"role", "system"}, {"content", If(istruzioni, "")}})
                        For Each turno As MessaggioChat In turni
                            messaggi.Add(New JObject From {{"role", If(turno.Ruolo = "assistente", "assistant", "user")}, {"content", turno.Testo}})
                        Next
                        messaggi.Add(New JObject From {{"role", "user"}, {"content", domanda}})
                        If provider = ProviderAi.Horde Then
                            risultato = Await WGAi.EseguiRichiestaHordeAsync(modello, RiduciMessaggiPerGroq(messaggi), cts.Token)
                        ElseIf provider = ProviderAi.Ovh Then
                            risultato = Await WGAi.EseguiRichiestaOvhAsync(modello, RiduciMessaggiPerGroq(messaggi), cts.Token)
                        Else
                            risultato = Await EseguiRichiestaGroqAsync(WGDettatura.LeggiChiaveApiGroq(), modello, RiduciMessaggiPerGroq(messaggi), cts.Token)
                        End If
                    Else
                        Dim chiaveGemini As String = WGDettatura.LeggiChiaveApiGemini()
                        Dim contenuti As New JArray()
                        contenuti.Add(New JObject From {{"role", "user"}, {"parts", New JArray(New JObject From {{"text", If(istruzioni, "")}})}})
                        contenuti.Add(New JObject From {{"role", "model"}, {"parts", New JArray(New JObject From {{"text", "D'accordo."}})}})
                        For Each turno As MessaggioChat In turni
                            contenuti.Add(New JObject From {{"role", If(turno.Ruolo = "assistente", "model", "user")}, {"parts", New JArray(New JObject From {{"text", turno.Testo}})}})
                        Next
                        contenuti.Add(New JObject From {{"role", "user"}, {"parts", New JArray(New JObject From {{"text", domanda}})}})
                        Using client As New HttpClient()
                            client.Timeout = TimeSpan.FromMinutes(1)
                            risultato = Await EseguiRichiestaGeminiAsync(client, chiaveGemini, modello, contenuti, cts.Token)
                        End Using
                    End If

                    If risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item1) Then Return risultato
                    If risultato IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultato.Item2) Then ultimoErrore = risultato.Item2
                    If cts.IsCancellationRequested Then Exit For
                Next
            End Using

            Return New Tuple(Of String, String)(Nothing, If(String.IsNullOrEmpty(ultimoErrore), "Nessuna risposta ricevuta", ultimoErrore))
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InterrogaConIstruzioniAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la richiesta: " & ex.Message)
        End Try
    End Function

    Public Async Function InterrogaGeminiConAudioAsync(istruzioni As String, audioWav As Byte(), testoIndicativo As String, Optional numeroMassimoTurni As Integer = 12) As Task(Of Tuple(Of String, String))
        Try
            If audioWav Is Nothing OrElse audioWav.Length = 0 Then Return New Tuple(Of String, String)(Nothing, "Audio mancante")
            If Not ChiaveApiConfigurata(ProviderAi.Gemini) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Gemini non configurata")

            Dim chiave As String = WGDettatura.LeggiChiaveApiGemini()
            Dim modello As String = LeggiModelloPredefinito(ProviderAi.Gemini)

            Dim contenuti As New JArray()
            contenuti.Add(New JObject From {{"role", "user"}, {"parts", New JArray(New JObject From {{"text", If(istruzioni, "")}})}})
            contenuti.Add(New JObject From {{"role", "model"}, {"parts", New JArray(New JObject From {{"text", "D'accordo."}})}})

            Dim inizio As Integer = Math.Max(0, _cronologia.Count - Math.Max(0, numeroMassimoTurni))
            For indice As Integer = inizio To _cronologia.Count - 1
                Dim turno As MessaggioChat = _cronologia(indice)
                If String.IsNullOrWhiteSpace(turno.Testo) Then Continue For
                contenuti.Add(New JObject From {{"role", If(turno.Ruolo = "assistente", "model", "user")}, {"parts", New JArray(New JObject From {{"text", turno.Testo}})}})
            Next

            Dim parti As New JArray()
            parti.Add(New JObject From {{"inline_data", New JObject From {{"mime_type", "audio/wav"}, {"data", Convert.ToBase64String(audioWav)}}}})
            parti.Add(New JObject From {{"text", "Trascrizione automatica indicativa: " & If(testoIndicativo, "")}})
            contenuti.Add(New JObject From {{"role", "user"}, {"parts", parti}})

            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(60))
                Using client As New HttpClient()
                    client.Timeout = TimeSpan.FromSeconds(60)
                    Return Await EseguiRichiestaGeminiAsync(client, chiave, modello, contenuti, cts.Token)
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InterrogaGeminiConAudioAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' Interroga Gemini con function calling: il modello deve scegliere una fra le funzioni
    ''' dichiarate in dichiarazioniFunzioni invece di rispondere liberamente. Funziona solo con Gemini:
    ''' Groq (compatibile OpenAI) non è coinvolto, quindi va usata solo se il provider è Gemini
    ''' (il chiamante lo verifica già con CLSAi.ChiaveApiConfigurata(CLSAi.ProviderAi.Gemini)).
    ''' </summary>
    Public Async Function InterrogaGeminiConFunzioniAsync(istruzioni As String, audioWav As Byte(), testoIndicativo As String, dichiarazioniFunzioni As JArray, Optional numeroMassimoTurni As Integer = 12) As Task(Of RispostaFunzioneAi)
        Try
            Dim audioPresente As Boolean = audioWav IsNot Nothing AndAlso audioWav.Length > 0
            If Not audioPresente AndAlso String.IsNullOrWhiteSpace(testoIndicativo) Then
                Return New RispostaFunzioneAi With {.Errore = "Richiesta vuota"}
            End If
            If dichiarazioniFunzioni Is Nothing OrElse dichiarazioniFunzioni.Count = 0 Then
                Return New RispostaFunzioneAi With {.Errore = "Nessuna funzione dichiarata"}
            End If
            If Not ChiaveApiConfigurata(ProviderAi.Gemini) Then Return New RispostaFunzioneAi With {.Errore = "Chiave API di Gemini non configurata"}

            Dim chiave As String = WGDettatura.LeggiChiaveApiGemini()
            Dim modello As String = LeggiModelloPredefinito(ProviderAi.Gemini)

            Dim contenuti As New JArray()
            contenuti.Add(New JObject From {{"role", "user"}, {"parts", New JArray(New JObject From {{"text", If(istruzioni, "")}})}})
            contenuti.Add(New JObject From {{"role", "model"}, {"parts", New JArray(New JObject From {{"text", "D'accordo."}})}})

            Dim inizio As Integer = Math.Max(0, _cronologia.Count - Math.Max(0, numeroMassimoTurni))
            For indice As Integer = inizio To _cronologia.Count - 1
                Dim turno As MessaggioChat = _cronologia(indice)
                If String.IsNullOrWhiteSpace(turno.Testo) Then Continue For
                contenuti.Add(New JObject From {{"role", If(turno.Ruolo = "assistente", "model", "user")}, {"parts", New JArray(New JObject From {{"text", turno.Testo}})}})
            Next

            Dim parti As New JArray()
            If audioPresente Then
                parti.Add(New JObject From {{"inline_data", New JObject From {{"mime_type", "audio/wav"}, {"data", Convert.ToBase64String(audioWav)}}}})
                If Not String.IsNullOrWhiteSpace(testoIndicativo) Then
                    parti.Add(New JObject From {{"text", "Trascrizione automatica indicativa: " & testoIndicativo}})
                End If
            Else
                parti.Add(New JObject From {{"text", testoIndicativo}})
            End If
            contenuti.Add(New JObject From {{"role", "user"}, {"parts", parti}})

            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(SecondiMassimiFunzioniGemini))
                Using client As New HttpClient()
                    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan
                    Return Await EseguiRichiestaGeminiConFunzioniAsync(client, chiave, modello, contenuti, dichiarazioniFunzioni, cts.Token)
                End Using
            End Using
        Catch exAnnullata As OperationCanceledException
            log.ScriviLog($"CLSAi.InterrogaGeminiConFunzioniAsync: Gemini non ha risposto entro {SecondiMassimiFunzioniGemini} secondi")
            Return New RispostaFunzioneAi With {.Errore = "Timeout della richiesta verso Gemini"}
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InterrogaGeminiConFunzioniAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New RispostaFunzioneAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Async Function EseguiRichiestaGeminiConFunzioniAsync(client As HttpClient, chiave As String, modello As String, contenuti As JArray, dichiarazioniFunzioni As JArray, token As CancellationToken) As Task(Of RispostaFunzioneAi)
        Try
            Dim corpoRichiesta As New JObject From {
                {"contents", contenuti},
                {"tools", New JArray From {New JObject From {{"function_declarations", dichiarazioniFunzioni}}}},
                {"tool_config", New JObject From {{"function_calling_config", New JObject From {{"mode", "ANY"}}}}}
            }
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models/" & modello & ":generateContent"

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False
                Dim messaggioErroreTentativo As String = Nothing

                Try
                    Using ctsTentativo As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)
                        ctsTentativo.CancelAfter(TimeSpan.FromSeconds(SecondiTentativoFunzioniGemini))
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Headers.Add("x-goog-api-key", chiave)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, ctsTentativo.Token)
                                WGAi.RegistraRichiestaGemini()
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    messaggioErroreTentativo = If(eTemporaneo, "Errore da Gemini: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Gemini", codiceStato))
                                    If codiceStato = 429 AndAlso Not String.IsNullOrEmpty(corpoRisposta) AndAlso corpoRisposta.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                        eTemporaneo = False
                                        messaggioErroreTentativo = "Quota giornaliera di Gemini esaurita"
                                    End If
                                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Dim esito As RispostaFunzioneAi = AnalizzaRispostaGeminiFunzioni(corpoRisposta)
                                    If esito Is Nothing Then
                                        log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: impossibile interpretare la risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New RispostaFunzioneAi With {.Errore = "Gemini non ha restituito una risposta valida"}
                                    End If
                                    Return esito
                                End If
                            End Using
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Errore di connessione verso Gemini: " & exRete.Message
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exIO As IOException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Connessione interrotta verso Gemini: " & exIO.Message
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, connessione interrotta: {exIO.Message}")
                Catch exTimeout As OperationCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    messaggioErroreTentativo = "Timeout della richiesta verso Gemini"
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, Gemini non ha risposto entro {SecondiTentativoFunzioniGemini} secondi")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                If eTemporaneo Then
                    Return New RispostaFunzioneAi With {.Errore = If(messaggioErroreTentativo, "Gemini è momentaneamente sovraccarico")}
                End If

                Return New RispostaFunzioneAi With {.Errore = If(messaggioErroreTentativo, "Gemini non ha risposto dopo diversi tentativi")}
            Next

            Return New RispostaFunzioneAi With {.Errore = "Gemini non ha risposto dopo diversi tentativi"}
        Catch exAnnullata As OperationCanceledException
            log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: Gemini non ha risposto entro il tempo massimo di {SecondiMassimiFunzioniGemini} secondi")
            Return New RispostaFunzioneAi With {.Errore = "Timeout della richiesta verso Gemini"}
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EseguiRichiestaGeminiConFunzioniAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New RispostaFunzioneAi With {.Errore = "Errore Gemini: " & ex.Message}
        End Try
    End Function

    Private Shared Function AnalizzaRispostaGeminiFunzioni(corpoRisposta As String) As RispostaFunzioneAi
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

            Dim testoRaccolto As New StringBuilder()
            For Each parteToken As JToken In parti
                Dim parte As JObject = TryCast(parteToken, JObject)
                If parte Is Nothing Then Continue For

                Dim chiamataFunzione As JObject = TryCast(parte("functionCall"), JObject)
                If chiamataFunzione IsNot Nothing Then
                    Dim nomeFunzione As String = CStr(chiamataFunzione("name"))
                    If Not String.IsNullOrWhiteSpace(nomeFunzione) Then
                        Dim argomenti As JObject = TryCast(chiamataFunzione("args"), JObject)
                        Return New RispostaFunzioneAi With {.NomeFunzione = nomeFunzione, .Argomenti = If(argomenti, New JObject())}
                    End If
                End If

                Dim gettoneTesto As JToken = parte("text")
                If gettoneTesto IsNot Nothing Then testoRaccolto.Append(gettoneTesto.ToString())
            Next

            If testoRaccolto.Length > 0 Then Return New RispostaFunzioneAi With {.Testo = PuliscoTestoAi(testoRaccolto.ToString()).Trim()}

            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"CLSAi.AnalizzaRispostaGeminiFunzioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Public Sub RegistraScambio(domanda As String, risposta As String, Optional prefissoTitolo As String = "")
        Try
            If String.IsNullOrWhiteSpace(domanda) OrElse String.IsNullOrWhiteSpace(risposta) Then Exit Sub

            If String.IsNullOrWhiteSpace(IdConversazioneCorrente) Then
                Dim provider As ProviderAi = LeggiProviderPreferito()
                IdConversazioneCorrente = CreaConversazione(If(prefissoTitolo, "") & TitoloDaDomanda(domanda), provider, LeggiModelloPredefinito(provider))
            End If
            If String.IsNullOrWhiteSpace(IdConversazioneCorrente) Then Exit Sub

            Dim chiaveUtente As String = AggiungiMessaggio(IdConversazioneCorrente, "utente", domanda, "", "")
            _cronologia.Add(New MessaggioChat With {.Chiave = chiaveUtente, .Ruolo = "utente", .Testo = domanda})

            Dim chiaveAssistente As String = AggiungiMessaggio(IdConversazioneCorrente, "assistente", risposta, "", "")
            _cronologia.Add(New MessaggioChat With {.Chiave = chiaveAssistente, .Ruolo = "assistente", .Testo = risposta})
        Catch ex As Exception
            log.ScriviLog($"CLSAi.RegistraScambio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

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
            log.ScriviLog($"CLSAi.MimeTypeDaEstensione: {ex.Message}")
            Return "application/octet-stream"
        End Try
    End Function

    Private Shared Function EEstensioneTesto(percorso As String) As Boolean
        Try
            Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
            Dim estensioniTesto As String() = {".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".htm", ".vb", ".cs", ".py", ".js", ".css", ".sql", ".ini", ".yaml", ".yml"}
            Return estensioniTesto.Contains(estensione)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EEstensioneTesto: {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Async Function CaricaAllegatoGeminiAsync(client As HttpClient, percorsoFile As String, chiave As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim bytesFile() As Byte = System.IO.File.ReadAllBytes(percorsoFile)
            Dim mimeType As String = MimeTypeDaEstensione(percorsoFile)
            Dim nomeVisualizzato As String = Path.GetFileName(percorsoFile)

            Dim urlAvvioCaricamento As String = "https://generativelanguage.googleapis.com/upload/v1beta/files"

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
                            WGAi.RegistraRichiestaGemini()
                            If Not rispostaAvvio.IsSuccessStatusCode Then
                                Dim corpoErrore As String = Await rispostaAvvio.Content.ReadAsStringAsync()
                                eTemporaneo = EStatusTemporaneo(CInt(rispostaAvvio.StatusCode))
                                log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, avvio caricamento fallito, HTTP {CInt(rispostaAvvio.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpoErrore}")
                            ElseIf Not rispostaAvvio.Headers.Contains("X-Goog-Upload-URL") Then
                                log.ScriviLog("CLSAi.CaricaAllegatoGeminiAsync: intestazione X-Goog-Upload-URL assente nella risposta di avvio")
                            Else
                                Dim urlCaricamentoBytes As String = rispostaAvvio.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault()

                                Using richiestaCaricamento As New HttpRequestMessage(HttpMethod.Post, urlCaricamentoBytes)
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Offset", "0")
                                    richiestaCaricamento.Headers.Add("X-Goog-Upload-Command", "upload, finalize")
                                    richiestaCaricamento.Content = New ByteArrayContent(bytesFile)
                                    richiestaCaricamento.Content.Headers.ContentLength = bytesFile.Length

                                    Using rispostaCaricamento As HttpResponseMessage = Await client.SendAsync(richiestaCaricamento, token)
                                        WGAi.RegistraRichiestaGemini()
                                        Dim corpoRispostaCaricamento As String = Await rispostaCaricamento.Content.ReadAsStringAsync()

                                        If Not rispostaCaricamento.IsSuccessStatusCode Then
                                            eTemporaneo = EStatusTemporaneo(CInt(rispostaCaricamento.StatusCode))
                                            log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, caricamento byte fallito, HTTP {CInt(rispostaCaricamento.StatusCode)}.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                        Else
                                            Dim risultatoJson As JObject = JObject.Parse(corpoRispostaCaricamento)
                                            Dim gettoneUri As JToken = risultatoJson.SelectToken("file.uri")
                                            If gettoneUri Is Nothing Then
                                                log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: uri del file assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRispostaCaricamento}")
                                                Return Nothing
                                            End If
                                            Return New Tuple(Of String, String)(gettoneUri.ToString(), mimeType)
                                        End If
                                    End Using
                                End Using
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exIO As IOException
                    eTemporaneo = True
                    log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, connessione interrotta: {exIO.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                Return Nothing
            Next

            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"CLSAi.CaricaAllegatoGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function CostruisciContenutiGemini(uriAllegato As String, mimeTypeAllegato As String) As JArray
        Dim contenuti As New JArray()
        Try
            For indice As Integer = 0 To _cronologia.Count - 1
                Dim turno As MessaggioChat = _cronologia(indice)
                Dim ruolo As String = If(turno.Ruolo = "assistente", "model", "user")
                Dim parti As New JArray()
                parti.Add(New JObject From {{"text", turno.Testo}})

                Dim eUltimoTurno As Boolean = (indice = _cronologia.Count - 1)
                If eUltimoTurno AndAlso ruolo = "user" AndAlso Not String.IsNullOrWhiteSpace(uriAllegato) Then
                    parti.Add(New JObject From {
                        {"file_data", New JObject From {
                            {"mime_type", mimeTypeAllegato},
                            {"file_uri", uriAllegato}
                        }}
                    })
                End If

                contenuti.Add(New JObject From {{"role", ruolo}, {"parts", parti}})
            Next
        Catch ex As Exception
            log.ScriviLog($"CLSAi.CostruisciContenutiGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return contenuti
    End Function

    Private Async Function InviaGeminiAsync(modello As String, percorsoAllegato As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Gemini non configurata")

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                Dim uriAllegato As String = Nothing
                Dim mimeTypeAllegato As String = Nothing
                If Not String.IsNullOrWhiteSpace(percorsoAllegato) Then
                    Dim risultatoCaricamento As Tuple(Of String, String) = Await CaricaAllegatoGeminiAsync(client, percorsoAllegato, chiave, token)
                    If risultatoCaricamento Is Nothing Then
                        Return New Tuple(Of String, String)(Nothing, "Impossibile caricare l'allegato su Gemini")
                    End If
                    uriAllegato = risultatoCaricamento.Item1
                    mimeTypeAllegato = risultatoCaricamento.Item2
                End If

                Dim contenuti As JArray = CostruisciContenutiGemini(uriAllegato, mimeTypeAllegato)
                Return Await EseguiRichiestaGeminiAsync(client, chiave, modello, contenuti, token)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function EseguiRichiestaGeminiAsync(client As HttpClient, chiave As String, modello As String, contenuti As JArray, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim corpoRichiesta As New JObject From {{"contents", contenuti}}
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://generativelanguage.googleapis.com/v1beta/models/" & modello & ":generateContent"

            For tentativo As Integer = 1 To NumeroMassimoTentativi
                Dim eTemporaneo As Boolean = False
                Dim messaggioErroreTentativo As String = Nothing

                Try
                    Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                        richiesta.Headers.Add("x-goog-api-key", chiave)
                        richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                        Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                            WGAi.RegistraRichiestaGemini()
                            Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                            If Not risposta.IsSuccessStatusCode Then
                                Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                eTemporaneo = EStatusTemporaneo(codiceStato)
                                messaggioErroreTentativo = If(eTemporaneo, "Errore da Gemini: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Gemini", codiceStato))
                                If codiceStato = 429 AndAlso Not String.IsNullOrEmpty(corpoRisposta) AndAlso corpoRisposta.IndexOf("PerDay", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    eTemporaneo = False
                                    messaggioErroreTentativo = "Quota giornaliera di Gemini esaurita"
                                End If
                                log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                            Else
                                Dim testo As String = EstraiTestoDaRispostaGemini(corpoRisposta)
                                If testo Is Nothing Then
                                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: impossibile estrarre il testo dalla risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                    Return New Tuple(Of String, String)(Nothing, "Gemini non ha restituito testo")
                                End If
                                Return New Tuple(Of String, String)(testo.Trim(), Nothing)
                            End If
                        End Using
                    End Using
                Catch exRete As HttpRequestException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Errore di connessione verso Gemini: " & exRete.Message
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                Catch exIO As IOException
                    eTemporaneo = True
                    messaggioErroreTentativo = "Connessione interrotta verso Gemini: " & exIO.Message
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, connessione interrotta: {exIO.Message}")
                Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                    eTemporaneo = True
                    messaggioErroreTentativo = "Timeout della richiesta verso Gemini"
                    log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                End Try

                If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                    Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                    Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                    Continue For
                End If

                If eTemporaneo Then
                    Return New Tuple(Of String, String)(Nothing, "Gemini è momentaneamente sovraccarico")
                End If

                Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "Gemini non ha risposto dopo diversi tentativi"))
            Next

            Return New Tuple(Of String, String)(Nothing, "Gemini non ha risposto dopo diversi tentativi")
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EseguiRichiestaGeminiAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
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

            Return risultato
        Catch ex As Exception
            log.ScriviLog($"CLSAi.PuliscoTestoAi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function EstraiTestoDaRispostaGemini(corpoRisposta As String) As String
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
                Dim gettoneTesto As JToken = parte("text")
                If gettoneTesto IsNot Nothing Then costruttore.Append(gettoneTesto.ToString())
            Next

            If costruttore.Length = 0 Then Return Nothing
            Return PuliscoTestoAi(costruttore.ToString())
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EstraiTestoDaRispostaGemini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function

    Private Function CostruisciMessaggiGroq(testoAllegatoInline As String) As JArray
        Dim messaggi As New JArray()
        Try
            For indice As Integer = 0 To _cronologia.Count - 1
                Dim turno As MessaggioChat = _cronologia(indice)
                Dim ruolo As String = If(turno.Ruolo = "assistente", "assistant", "user")
                Dim contenuto As String = turno.Testo

                Dim eUltimoTurno As Boolean = (indice = _cronologia.Count - 1)
                If eUltimoTurno AndAlso ruolo = "user" AndAlso Not String.IsNullOrWhiteSpace(testoAllegatoInline) Then
                    contenuto = testoAllegatoInline & Environment.NewLine & Environment.NewLine & contenuto
                End If

                messaggi.Add(New JObject From {{"role", ruolo}, {"content", contenuto}})
            Next
        Catch ex As Exception
            log.ScriviLog($"CLSAi.CostruisciMessaggiGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return messaggi
    End Function

    Private Async Function InviaGroqAsync(modello As String, percorsoAllegato As String, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGroq()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Groq non configurata")

            Dim testoAllegatoInline As String = Nothing
            If Not String.IsNullOrWhiteSpace(percorsoAllegato) Then
                If Not EEstensioneTesto(percorsoAllegato) Then
                    Return New Tuple(Of String, String)(Nothing, "Groq non supporta questo tipo di allegato: usa un file di testo oppure passa a Gemini per questo tipo di file")
                End If
                Try
                    Dim contenutoFile As String = File.ReadAllText(percorsoAllegato, Encoding.UTF8)
                    If contenutoFile.Length > LunghezzaMassimaTestoAllegatoGroq Then
                        contenutoFile = contenutoFile.Substring(0, LunghezzaMassimaTestoAllegatoGroq)
                    End If
                    testoAllegatoInline = "Contenuto del file allegato (" & Path.GetFileName(percorsoAllegato) & "):" & Environment.NewLine & contenutoFile
                Catch exLettura As Exception
                    log.ScriviLog($"CLSAi.InviaGroqAsync (lettura allegato): {exLettura.Message}")
                    Return New Tuple(Of String, String)(Nothing, "Impossibile leggere il file allegato")
                End Try
            End If

            Dim messaggi As JArray = RiduciMessaggiPerGroq(CostruisciMessaggiGroq(testoAllegatoInline))
            Return Await EseguiRichiestaGroqAsync(chiave, modello, messaggi, token)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Private Shared Function RiduciMessaggiPerGroq(messaggi As JArray) As JArray
        Try
            If messaggi Is Nothing OrElse messaggi.Count = 0 Then Return messaggi
            Dim totale As Integer = 0
            For Each messaggio As JToken In messaggi
                totale += LunghezzaContenutoMessaggio(messaggio)
            Next
            If totale <= LunghezzaMassimaConversazioneGroq Then Return messaggi

            Dim ridotti As New List(Of JToken)
            Dim primoDiSistema As JToken = Nothing
            Dim indiceInizio As Integer = 0
            If String.Equals(If(messaggi(0).Value(Of String)("role"), ""), "system", StringComparison.OrdinalIgnoreCase) Then
                primoDiSistema = messaggi(0)
                indiceInizio = 1
            End If

            Dim disponibile As Integer = LunghezzaMassimaConversazioneGroq
            If primoDiSistema IsNot Nothing Then disponibile -= LunghezzaContenutoMessaggio(primoDiSistema)

            For indice As Integer = messaggi.Count - 1 To indiceInizio Step -1
                Dim messaggio As JToken = messaggi(indice)
                Dim lunghezza As Integer = LunghezzaContenutoMessaggio(messaggio)
                If ridotti.Count = 0 Then
                    If lunghezza > disponibile Then
                        Dim copia As JObject = CType(messaggio.DeepClone(), JObject)
                        Dim testoCompleto As String = copia.Value(Of String)("content")
                        Dim daTenere As Integer = Math.Max(0, Math.Min(disponibile, testoCompleto.Length))
                        copia("content") = testoCompleto.Substring(testoCompleto.Length - daTenere)
                        ridotti.Insert(0, copia)
                        disponibile = 0
                    Else
                        ridotti.Insert(0, messaggio)
                        disponibile -= lunghezza
                    End If
                    Continue For
                End If
                If lunghezza > disponibile Then Exit For
                ridotti.Insert(0, messaggio)
                disponibile -= lunghezza
            Next

            Do While ridotti.Count > 1 AndAlso Not String.Equals(If(ridotti(0).Value(Of String)("role"), ""), "user", StringComparison.OrdinalIgnoreCase)
                ridotti.RemoveAt(0)
            Loop

            Dim risultato As New JArray()
            If primoDiSistema IsNot Nothing Then risultato.Add(primoDiSistema)
            For Each messaggio As JToken In ridotti
                risultato.Add(messaggio)
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"CLSAi.RiduciMessaggiPerGroq: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return messaggi
        End Try
    End Function

    Private Shared Function LunghezzaContenutoMessaggio(messaggio As JToken) As Integer
        Try
            If messaggio Is Nothing Then Return 0
            Dim contenuto As JToken = messaggio("content")
            If contenuto Is Nothing Then Return 0
            Return contenuto.ToString().Length
        Catch ex As Exception
            log.ScriviLog($"CLSAi.LunghezzaContenutoMessaggio: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Private Shared Async Function EseguiRichiestaGroqAsync(chiave As String, modello As String, messaggi As JArray, token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim corpoRichiesta As New JObject From {{"model", modello}, {"messages", messaggi}}
            Dim jsonRichiesta As String = corpoRichiesta.ToString(Newtonsoft.Json.Formatting.None)
            Dim url As String = "https://api.groq.com/openai/v1/chat/completions"

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                For tentativo As Integer = 1 To NumeroMassimoTentativi
                    Dim eTemporaneo As Boolean = False
                    Dim messaggioErroreTentativo As String = Nothing

                    Try
                        Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                            richiesta.Headers.Authorization = New Net.Http.Headers.AuthenticationHeaderValue("Bearer", chiave)
                            richiesta.Content = New StringContent(jsonRichiesta, Encoding.UTF8, "application/json")

                            Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                                WGAi.AggiornaStatoGroqDaRisposta(risposta)
                                Dim corpoRisposta As String = Await risposta.Content.ReadAsStringAsync()

                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    eTemporaneo = EStatusTemporaneo(codiceStato)
                                    messaggioErroreTentativo = If(eTemporaneo, "Errore da Groq: " & risposta.StatusCode.ToString(), DescriviErrorePermanente("Groq", codiceStato))
                                    If codiceStato = 429 AndAlso Not String.IsNullOrEmpty(corpoRisposta) AndAlso (corpoRisposta.IndexOf("per day", StringComparison.OrdinalIgnoreCase) >= 0 OrElse corpoRisposta.IndexOf("(TPD)", StringComparison.OrdinalIgnoreCase) >= 0 OrElse corpoRisposta.IndexOf("(RPD)", StringComparison.OrdinalIgnoreCase) >= 0) Then
                                        eTemporaneo = False
                                        messaggioErroreTentativo = "Quota giornaliera di Groq esaurita"
                                    ElseIf codiceStato = 413 Then
                                        eTemporaneo = False
                                        messaggioErroreTentativo = "La conversazione è troppo lunga per il piano gratuito di Groq"
                                    End If
                                    log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, risposta HTTP {CInt(risposta.StatusCode)} ({risposta.StatusCode}).{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                Else
                                    Dim risultato As JObject = JObject.Parse(corpoRisposta)
                                    Dim scelte As JArray = TryCast(risultato("choices"), JArray)
                                    If scelte Is Nothing OrElse scelte.Count = 0 Then
                                        log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: campo 'choices' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "Groq non ha restituito testo")
                                    End If
                                    Dim gettoneTesto As JToken = scelte(0).SelectToken("message.content")
                                    If gettoneTesto Is Nothing Then
                                        log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: campo 'message.content' assente nella risposta.{Environment.NewLine}Corpo risposta: {corpoRisposta}")
                                        Return New Tuple(Of String, String)(Nothing, "Groq non ha restituito testo")
                                    End If
                                    Return New Tuple(Of String, String)(PuliscoTestoAi(gettoneTesto.ToString().Trim()), Nothing)
                                End If
                            End Using
                        End Using
                    Catch exRete As HttpRequestException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Errore di connessione verso Groq: " & exRete.Message
                        log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, eccezione di rete: {exRete.Message}")
                    Catch exIO As IOException
                        eTemporaneo = True
                        messaggioErroreTentativo = "Connessione interrotta verso Groq: " & exIO.Message
                        log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, connessione interrotta: {exIO.Message}")
                    Catch exTimeout As TaskCanceledException When Not token.IsCancellationRequested
                        eTemporaneo = True
                        messaggioErroreTentativo = "Timeout della richiesta verso Groq"
                        log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: tentativo {tentativo} di {NumeroMassimoTentativi}, timeout della richiesta")
                    End Try

                    If eTemporaneo AndAlso tentativo < NumeroMassimoTentativi Then
                        Dim attesaSecondi As Integer = AttesaBackoffSecondi(tentativo)
                        Await Task.Delay(TimeSpan.FromSeconds(attesaSecondi), token)
                        Continue For
                    End If

                    If eTemporaneo Then
                        Return New Tuple(Of String, String)(Nothing, "Groq è momentaneamente sovraccarico")
                    End If

                    Return New Tuple(Of String, String)(Nothing, If(messaggioErroreTentativo, "Groq non ha risposto dopo diversi tentativi"))
                Next

                Return New Tuple(Of String, String)(Nothing, "Groq non ha risposto dopo diversi tentativi")
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.EseguiRichiestaGroqAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function OttieniRispostaAsync(domanda As String, Optional link As List(Of String) = Nothing, Optional documenti As List(Of String) = Nothing, Optional immagini As List(Of String) = Nothing, Optional providerSovrascritto As ProviderAi? = Nothing, Optional modelloSovrascritto As String = Nothing) As Task(Of Tuple(Of String, String))
        Try
            If String.IsNullOrWhiteSpace(domanda) Then Return New Tuple(Of String, String)(Nothing, "La domanda è vuota")

            Dim provider As ProviderAi = If(providerSovrascritto.HasValue, providerSovrascritto.Value, LeggiProviderPreferito())
            If Not ChiaveApiConfigurata(provider) Then
                Return New Tuple(Of String, String)(Nothing, "Nessuna chiave API configurata per il motore selezionato")
            End If
            Dim modello As String = If(Not String.IsNullOrWhiteSpace(modelloSovrascritto), modelloSovrascritto, LeggiModelloPredefinito(provider))

            Dim tuttiIFile As New List(Of String)
            AggiungiFileEsistenti(tuttiIFile, documenti)
            AggiungiFileEsistenti(tuttiIFile, immagini)

            Dim domandaCompleta As String = domanda
            If link IsNot Nothing Then
                Dim linkValidi As New List(Of String)
                For Each indirizzo As String In link
                    If Not String.IsNullOrWhiteSpace(indirizzo) Then linkValidi.Add(indirizzo)
                Next
                If linkValidi.Count > 0 Then
                    domandaCompleta &= Environment.NewLine & Environment.NewLine & "Link di riferimento:" & Environment.NewLine & String.Join(Environment.NewLine, linkValidi)
                End If
            End If

            Using cts As New CancellationTokenSource()
                If provider = ProviderAi.Ovh Then
                    Dim risposta As Tuple(Of String, String) = Await WGAi.OttieniRispostaAsync(domandaCompleta, tuttiIFile, WGAi.ProviderAi.Ovh, modello)
                    Return risposta
                ElseIf provider = ProviderAi.Horde Then
                    Dim rispostaHorde As Tuple(Of String, String) = Await WGAi.OttieniRispostaAsync(domandaCompleta, tuttiIFile, WGAi.ProviderAi.Horde, modello)
                    Return rispostaHorde
                ElseIf provider = ProviderAi.Groq Then
                    Return Await InviaGroqUnaVoltaAsync(modello, domandaCompleta, tuttiIFile, cts.Token)
                Else
                    Return Await InviaGeminiUnaVoltaAsync(modello, domandaCompleta, tuttiIFile, cts.Token)
                End If
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.OttieniRispostaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante la richiesta: " & ex.Message)
        End Try
    End Function

    Private Shared Sub AggiungiFileEsistenti(destinazione As List(Of String), percorsi As List(Of String))
        Try
            If percorsi Is Nothing Then Return
            For Each percorso As String In percorsi
                If Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) Then destinazione.Add(percorso)
            Next
        Catch ex As Exception
            log.ScriviLog($"CLSAi.AggiungiFileEsistenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Async Function InviaGeminiUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGemini()
            If String.IsNullOrWhiteSpace(chiave) Then Return New Tuple(Of String, String)(Nothing, "Chiave API di Gemini non configurata")

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(3)

                Dim parti As New JArray()
                parti.Add(New JObject From {{"text", domanda}})

                For Each percorsoFile As String In file
                    Dim risultatoCaricamento As Tuple(Of String, String) = Await CaricaAllegatoGeminiAsync(client, percorsoFile, chiave, token)
                    If risultatoCaricamento IsNot Nothing Then
                        parti.Add(New JObject From {
                            {"file_data", New JObject From {
                                {"mime_type", risultatoCaricamento.Item2},
                                {"file_uri", risultatoCaricamento.Item1}
                            }}
                        })
                    Else
                        log.ScriviLog($"CLSAi.InviaGeminiUnaVoltaAsync: impossibile caricare l'allegato {percorsoFile}, proseguo senza di esso")
                    End If
                Next

                Dim contenuti As New JArray From {New JObject From {{"role", "user"}, {"parts", parti}}}
                Return Await EseguiRichiestaGeminiAsync(client, chiave, modello, contenuti, token)
            End Using
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaGeminiUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Gemini: " & ex.Message)
        End Try
    End Function

    Private Shared Async Function InviaGroqUnaVoltaAsync(modello As String, domanda As String, file As List(Of String), token As CancellationToken) As Task(Of Tuple(Of String, String))
        Try
            Dim chiave As String = WGDettatura.LeggiChiaveApiGroq()
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
                        log.ScriviLog($"CLSAi.InviaGroqUnaVoltaAsync (lettura allegato): {exLettura.Message}")
                        fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                    End Try
                Else
                    fileNonSupportati.Add(Path.GetFileName(percorsoFile))
                End If
            Next
            If fileNonSupportati.Count > 0 Then
                testoCompleto &= Environment.NewLine & Environment.NewLine & "(Nota: Groq non supporta questi allegati, sono stati ignorati: " & String.Join(", ", fileNonSupportati) & ")"
            End If

            Dim messaggi As New JArray From {New JObject From {{"role", "user"}, {"content", testoCompleto}}}
            Return Await EseguiRichiestaGroqAsync(chiave, modello, messaggi, token)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.InviaGroqUnaVoltaAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore Groq: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function GeneraAudioDaTestoAsync(testo As String, percorsoFileDestinazione As String, Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of String)
        Try
            Dim providerWGAi As WGAi.ProviderAi? = Nothing
            If providerSovrascritto.HasValue Then
                providerWGAi = ProviderPerWGAi(providerSovrascritto.Value)
            End If
            Return Await WGAi.GeneraAudioDaTestoAsync(testo, percorsoFileDestinazione, providerWGAi)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.GeneraAudioDaTestoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Errore durante la generazione dell'audio: " & ex.Message
        End Try
    End Function

    Public Shared Async Function RiassumiFileAsync(percorsoFile As String, Optional istruzioniAggiuntive As String = "", Optional providerSovrascritto As ProviderAi? = Nothing) As Task(Of Tuple(Of String, String))
        Try
            Dim providerWGAi As WGAi.ProviderAi? = Nothing
            If providerSovrascritto.HasValue Then
                providerWGAi = ProviderPerWGAi(providerSovrascritto.Value)
            End If
            Return Await WGAi.RiassumiFileAsync(percorsoFile, istruzioniAggiuntive, providerWGAi)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.RiassumiFileAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto del file: " & ex.Message)
        End Try
    End Function

    Public Shared Async Function RiassumiVideoAsync(urlVideo As String, Optional istruzioniAggiuntive As String = "") As Task(Of Tuple(Of String, String))
        Try
            Return Await WGAi.RiassumiVideoAsync(urlVideo, "", istruzioniAggiuntive)
        Catch ex As Exception
            log.ScriviLog($"CLSAi.RiassumiVideoAsync: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Tuple(Of String, String)(Nothing, "Errore durante il riassunto del video: " & ex.Message)
        End Try
    End Function

    Protected Overridable Sub Dispose(disposing As Boolean)
        Try
            If Not _disposedValue Then
                If disposing Then
                    Annulla()
                End If
                _disposedValue = True
            End If
        Catch ex As Exception
            log.ScriviLog($"CLSAi.Dispose: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

End Class