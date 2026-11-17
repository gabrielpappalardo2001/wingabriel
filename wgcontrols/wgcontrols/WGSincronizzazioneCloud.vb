Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports GabrielSync.Filen
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

''' <summary>
''' Sincronizzazione dei dati tra computer tramite un account Filen.
''' Il file wingabriel-sincronizzazione.json (stesso formato dell'app Android) sta nella radice dell'account
''' ed è cifrato da Filen con la chiave dell'utente. Le credenziali di accesso sono conservate nella
''' configurazione, protette con DPAPI (valgono solo per questo utente di Windows su questo computer).
''' </summary>
Public NotInheritable Class WGSincronizzazioneCloud

    Public Class EsitoSincronizzazione
        Public Property Riuscita As Boolean = False
        Public Property FileAggiornato As Boolean = False
        Public Property Messaggio As String = ""
        Public Property Avvisi As New List(Of String)
    End Class

    Public Delegate Function UnioneSezioneCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))

    Public Delegate Sub ProceduraSincronizzazioneCloud(client As FilenClient, elenco As List(Of FilenClient.FilenFile), ct As CancellationToken, esito As EsitoSincronizzazione)

    Private Class SezioneRegistrata
        Public Property Nome As String = ""
        Public Property Unione As UnioneSezioneCloud
    End Class

    Private Class ProceduraRegistrata
        Public Property Nome As String = ""
        Public Property Procedura As ProceduraSincronizzazioneCloud
    End Class

    Private Class ConfrontoOggettiPerId
        Implements IComparer(Of JObject)

        Public Function Confronta(x As JObject, y As JObject) As Integer Implements IComparer(Of JObject).Compare
            Try
                Return String.CompareOrdinal(IdOggetto(x), IdOggetto(y))
            Catch
                Return 0
            End Try
        End Function
    End Class


    Public Const NomeFileSincronizzazione As String = "wingabriel-sincronizzazione.json"
    Private Const PrefissoFileSincronizzazione As String = "wingabriel-sincronizzazione"
    Private Const FormatoFile As String = "wingabriel-sincronizzazione"
    Private Const VersioneFormato As Integer = 1

    Private Const ChiaveSessione As String = "SincronizzazioneFilenSessione"
    Private Const ChiaveAbilitata As String = "SincronizzazioneFilenAbilitata"
    Private Const ChiaveRubrica As String = "SincronizzazioneCloudRubrica"
    Private Const ChiaveAgenda As String = "SincronizzazioneCloudAgenda"
    Private Const ChiaveRadio As String = "SincronizzazioneCloudRadio"
    Private Const ChiaveNotizie As String = "SincronizzazioneCloudNotizie"

    Public Const SezioneContatti As String = "contatti"
    Public Const SezioneAppuntamenti As String = "appuntamenti"
    Public Const SezioneRadioPreferiti As String = "radioPreferiti"
    Public Const SezioneRadioRecenti As String = "radioRecenti"
    Public Const SezioneCategorieTestate As String = "categorieTestate"
    Public Const SezioneTestate As String = "testate"

    Private Const IntervalloPeriodicoMs As Integer = 300000
    Private Const RitardoDopoModificaMs As Integer = 15000
    Private Const SecondiAttesaChiusura As Integer = 20

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _lockSincronizzazione As New Object()
    Private Shared ReadOnly _lockAvvio As New Object()
    Private Shared _timerPeriodico As System.Threading.Timer
    Private Shared _timerRitardo As System.Threading.Timer
    Private Shared _ultimoErrore As String = ""
    Private Shared ReadOnly _lockSezioni As New Object()
    Private Shared ReadOnly _sezioni As New List(Of SezioneRegistrata)
    Private Shared ReadOnly _procedure As New List(Of ProceduraRegistrata)
    Private Shared ReadOnly EntropiaSessione As Byte() = Encoding.UTF8.GetBytes("WinGabriel.Filen.Sessione.v1")
    Private Shared ReadOnly EntropiaChiavePortabile As Byte() = Encoding.UTF8.GetBytes("WinGabriel.Filen.ChiavePortabile.v1")
    Private Shared ReadOnly _lockChiavePortabile As New Object()
    Private Const ChiaveBrowserPortabile As String = "BrowserChiavePortabile"
    Private Const ScopoSessione As String = "filen-sessione"

    Public Shared Event SincronizzazioneTerminata(esito As EsitoSincronizzazione)

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property UltimoErrore As String
        Get
            Return If(_ultimoErrore, "")
        End Get
    End Property

    Public Shared Sub RegistraSezione(nome As String, unione As UnioneSezioneCloud)
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse unione Is Nothing Then Return
            SyncLock _lockSezioni
                For Each sezione As SezioneRegistrata In _sezioni
                    If String.Equals(sezione.Nome, nome, StringComparison.Ordinal) Then
                        sezione.Unione = unione
                        Return
                    End If
                Next
                Dim nuova As New SezioneRegistrata()
                nuova.Nome = nome
                nuova.Unione = unione
                _sezioni.Add(nuova)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.RegistraSezione (" & If(nome, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Sub RegistraProcedura(nome As String, procedura As ProceduraSincronizzazioneCloud)
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse procedura Is Nothing Then Return
            SyncLock _lockSezioni
                For Each registrata As ProceduraRegistrata In _procedure
                    If String.Equals(registrata.Nome, nome, StringComparison.Ordinal) Then
                        registrata.Procedura = procedura
                        Return
                    End If
                Next
                Dim nuova As New ProceduraRegistrata()
                nuova.Nome = nome
                nuova.Procedura = procedura
                _procedure.Add(nuova)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.RegistraProcedura (" & If(nome, "") & "): " & ex.ToString())
        End Try
    End Sub

    Public Shared Function NuovoClient() As FilenClient
        Try
            Dim sessione As FilenSessione = CaricaSessione()
            If sessione Is Nothing Then Return Nothing
            Return New FilenClient(sessione)
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.NuovoClient: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Shared Function DescriviErrore(ex As Exception, azione As String) As String
        Try
            Return TestoErrore(ex, azione)
        Catch exInterna As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.DescriviErrore: " & exInterna.ToString())
            Return "Non è stato possibile " & azione & "."
        End Try
    End Function


    ' ---------- Account Filen ----------

    Public Shared ReadOnly Property Collegato As Boolean
        Get
            Try
                Return CaricaSessione() IsNot Nothing
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Collegato: " & ex.ToString())
                Return False
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property Email As String
        Get
            Try
                Dim sessione As FilenSessione = CaricaSessione()
                If sessione Is Nothing Then Return ""
                Return If(sessione.Email, "")
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Email: " & ex.ToString())
                Return ""
            End Try
        End Get
    End Property

    Public Shared Property SincronizzazioneAbilitata As Boolean
        Get
            Try
                Return LeggiImpostazione(ChiaveAbilitata) = "1"
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.SincronizzazioneAbilitata get: " & ex.ToString())
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                SalvaImpostazione(ChiaveAbilitata, If(value, "1", "0"))
                If value Then
                    Avvia()
                Else
                    Ferma()
                End If
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.SincronizzazioneAbilitata set: " & ex.ToString())
            End Try
        End Set
    End Property

    ''' <summary>Vera solo se l'accesso a Filen è stato eseguito e la sincronizzazione è stata attivata.</summary>
    Public Shared ReadOnly Property Attiva As Boolean
        Get
            Try
                Return Collegato AndAlso SincronizzazioneAbilitata
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Attiva: " & ex.ToString())
                Return False
            End Try
        End Get
    End Property

    ''' <summary>Crea l'account. Filen invia un'email di conferma: finché non è confermata l'accesso non riesce.</summary>
    Public Shared Function Registra(email As String, password As String) As Boolean
        Try
            _ultimoErrore = ""
            email = If(email, "").Trim()
            If Not EmailPlausibile(email) Then
                _ultimoErrore = "Scrivi un indirizzo email valido."
                Return False
            End If
            If String.IsNullOrEmpty(password) Then
                _ultimoErrore = "Scrivi una password."
                Return False
            End If
            FilenClient.RegistraAsync(email, password, CancellationToken.None).GetAwaiter().GetResult()
            Return True
        Catch ex As Exception
            _ultimoErrore = TestoErrore(ex, "creare l'account")
            Return False
        End Try
    End Function

    Public Shared Function ReinviaConferma(email As String) As Boolean
        Try
            _ultimoErrore = ""
            email = If(email, "").Trim()
            If Not EmailPlausibile(email) Then
                _ultimoErrore = "Scrivi un indirizzo email valido."
                Return False
            End If
            FilenClient.ReinviaConfermaAsync(email, CancellationToken.None).GetAwaiter().GetResult()
            Return True
        Catch ex As Exception
            _ultimoErrore = TestoErrore(ex, "reinviare l'email di conferma")
            Return False
        End Try
    End Function

    ''' <summary>Esegue l'accesso e conserva la sessione. La sincronizzazione resta com'era: si attiva a parte.</summary>
    Public Shared Function Accedi(email As String, password As String, codice2fa As String) As Boolean
        Try
            _ultimoErrore = ""
            email = If(email, "").Trim()
            If Not EmailPlausibile(email) Then
                _ultimoErrore = "Scrivi un indirizzo email valido."
                Return False
            End If
            If String.IsNullOrEmpty(password) Then
                _ultimoErrore = "Scrivi la password."
                Return False
            End If
            Dim codice As String = If(String.IsNullOrWhiteSpace(codice2fa), Nothing, codice2fa.Trim())
            Dim sessione As FilenSessione = FilenClient.AccediAsync(email, password, codice, CancellationToken.None).GetAwaiter().GetResult()
            If Not SalvaSessione(sessione) Then
                _ultimoErrore = "Accesso riuscito, ma non è stato possibile conservare le credenziali su questo computer."
                Return False
            End If
            Return True
        Catch ex As Exception
            _ultimoErrore = TestoErrore(ex, "eseguire l'accesso")
            Return False
        End Try
    End Function

    ''' <summary>Dimentica l'account su questo computer e ferma la sincronizzazione. I dati su Filen e quelli locali restano.</summary>
    Public Shared Sub Esci()
        Try
            Ferma()
            SalvaImpostazione(ChiaveAbilitata, "0")
            SalvaImpostazione(ChiaveSessione, "")
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.Esci: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function EmailPlausibile(email As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(email) AndAlso email.IndexOf("@"c) > 0 AndAlso email.IndexOf("@"c) < email.Length - 1
    End Function

    Private Shared Function CaricaSessione() As FilenSessione
        Try
            Dim cifrata As String = LeggiImpostazione(ChiaveSessione)
            If cifrata.Length = 0 Then Return Nothing
            Dim testoSessione As String
            If WGDb.WGCifraturaNativa.EProtetto(cifrata) Then
                testoSessione = WGDb.WGCifraturaNativa.RivelaTesto(ScopoSessione, cifrata)
                If testoSessione.Length = 0 Then
                    _log.ScriviLog("WGSincronizzazioneCloud.CaricaSessione: credenziali non leggibili.")
                    Return Nothing
                End If
            Else
                ' formato precedente: DPAPI, valido solo per l'utente di Windows e il computer che l'hanno scritto
                Dim chiari As Byte() = ProtectedData.Unprotect(Convert.FromBase64String(cifrata), EntropiaSessione, DataProtectionScope.CurrentUser)
                testoSessione = Encoding.UTF8.GetString(chiari)
            End If
            Dim sessione As FilenSessione = JsonConvert.DeserializeObject(Of FilenSessione)(testoSessione)
            If sessione Is Nothing OrElse String.IsNullOrEmpty(sessione.ApiKey) OrElse sessione.ChiaviMaestre Is Nothing OrElse sessione.ChiaviMaestre.Count = 0 Then Return Nothing
            Return sessione
        Catch exCifratura As CryptographicException
            _log.ScriviLog("WGSincronizzazioneCloud.CaricaSessione: credenziali non leggibili (profilo o computer diverso): " & exCifratura.Message)
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.CaricaSessione: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function SalvaSessione(sessione As FilenSessione) As Boolean
        Try
            Dim json As String = JsonConvert.SerializeObject(sessione)
            ' libreria nativa, senza password: la sessione resta valida anche su un altro computer o con la configurazione importata
            Dim protetta As String = WGDb.WGCifraturaNativa.ProteggiTesto(ScopoSessione, json)
            If protetta.Length = 0 Then Return False
            SalvaImpostazione(ChiaveSessione, protetta)
            Return CaricaSessione() IsNot Nothing
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SalvaSessione: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>Traduce l'errore in una frase per l'utente. I codici di Filen sono riconosciuti per parole chiave: si affinano alla prima prova.</summary>
    Private Shared Function TestoErrore(ex As Exception, azione As String) As String
        Try
            Dim errorePiatto As Exception = ex
            Dim aggregato As AggregateException = TryCast(ex, AggregateException)
            If aggregato IsNot Nothing AndAlso aggregato.InnerException IsNot Nothing Then errorePiatto = aggregato.InnerException

            Dim erroreFilen As FilenException = TryCast(errorePiatto, FilenException)
            If erroreFilen IsNot Nothing Then
                _log.ScriviLog("WGSincronizzazioneCloud: errore di Filen durante '" & azione & "', codice " & erroreFilen.Codice & ": " & erroreFilen.Message)
                Dim chiave As String = (If(erroreFilen.Codice, "") & " " & If(erroreFilen.Message, "")).ToLowerInvariant()
                If chiave.Contains("confirm") Then
                    Return "L'account non è ancora confermato. Apri l'email che Filen ti ha inviato e conferma l'indirizzo, poi riprova."
                End If
                If chiave.Contains("2fa") OrElse chiave.Contains("two factor") OrElse chiave.Contains("two-factor") OrElse chiave.Contains("twofactor") Then
                    Return "Serve il codice a due fattori, oppure quello scritto non è corretto."
                End If
                If chiave.Contains("already") OrElse chiave.Contains("exist") OrElse chiave.Contains("taken") OrElse chiave.Contains("in use") Then
                    Return "Esiste già un account con questo indirizzo email."
                End If
                If chiave.Contains("password") OrElse chiave.Contains("credential") OrElse chiave.Contains("invalid") OrElse chiave.Contains("wrong") Then
                    Return "L'indirizzo email o la password non sono corretti."
                End If
                Return "Filen ha risposto con un errore: " & If(erroreFilen.Message, "") & " (codice " & If(erroreFilen.Codice, "") & ")."
            End If

            If TypeOf errorePiatto Is HttpRequestException OrElse TypeOf errorePiatto Is OperationCanceledException OrElse TypeOf errorePiatto Is IOException Then
                _log.ScriviLog("WGSincronizzazioneCloud: rete non raggiungibile durante '" & azione & "': " & errorePiatto.Message)
                Return "Non riesco a collegarmi a Filen. Controlla la connessione a Internet e riprova."
            End If

            _log.ScriviLog("WGSincronizzazioneCloud: errore durante '" & azione & "': " & errorePiatto.ToString())
            Return "Non è stato possibile " & azione & "."
        Catch exInterna As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.TestoErrore: " & exInterna.ToString())
            Return "Non è stato possibile " & azione & "."
        End Try
    End Function

    Public Shared Property RubricaSincronizzata As String
        Get
            Try
                Return LeggiImpostazione(ChiaveRubrica)
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.RubricaSincronizzata get: " & ex.ToString())
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                SalvaImpostazione(ChiaveRubrica, If(value, "").Trim())
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.RubricaSincronizzata set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property AgendaSincronizzata As String
        Get
            Try
                Return LeggiImpostazione(ChiaveAgenda)
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.AgendaSincronizzata get: " & ex.ToString())
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                SalvaImpostazione(ChiaveAgenda, If(value, "").Trim())
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.AgendaSincronizzata set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property RadioSincronizzata As Boolean
        Get
            Try
                Return LeggiImpostazione(ChiaveRadio) = "1"
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.RadioSincronizzata get: " & ex.ToString())
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                SalvaImpostazione(ChiaveRadio, If(value, "1", "0"))
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.RadioSincronizzata set: " & ex.ToString())
            End Try
        End Set
    End Property

    Public Shared Property NotizieSincronizzate As Boolean
        Get
            Try
                Return LeggiImpostazione(ChiaveNotizie) = "1"
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.NotizieSincronizzate get: " & ex.ToString())
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                SalvaImpostazione(ChiaveNotizie, If(value, "1", "0"))
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.NotizieSincronizzate set: " & ex.ToString())
            End Try
        End Set
    End Property


    ' ---------- Avvio, arresto e sincronizzazione ----------

    Public Shared Sub Avvia()
        SyncLock _lockAvvio
            Try
                FermaInterno()
                If Not Attiva Then Return
                _timerRitardo = New System.Threading.Timer(AddressOf TimerScaduto, Nothing, Timeout.Infinite, Timeout.Infinite)
                _timerPeriodico = New System.Threading.Timer(AddressOf TimerScaduto, Nothing, 5000, IntervalloPeriodicoMs)
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Avvia: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Shared Sub Ferma()
        SyncLock _lockAvvio
            Try
                FermaInterno()
            Catch ex As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Ferma: " & ex.ToString())
            End Try
        End SyncLock
    End Sub

    Public Shared Sub SincronizzaPrimaDellaChiusura()
        Try
            Ferma()
            If Not Attiva Then Return
            Using limite As New CancellationTokenSource(TimeSpan.FromSeconds(SecondiAttesaChiusura))
                SincronizzaConAttesa(limite.Token)
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SincronizzaPrimaDellaChiusura: " & ex.ToString())
        End Try
    End Sub

    Public Shared Sub RichiediSincronizzazione()
        Try
            Dim timer As System.Threading.Timer = _timerRitardo
            If timer IsNot Nothing Then timer.Change(RitardoDopoModificaMs, Timeout.Infinite)
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.RichiediSincronizzazione: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub FermaInterno()
        Try
            If _timerPeriodico IsNot Nothing Then
                _timerPeriodico.Dispose()
                _timerPeriodico = Nothing
            End If
            If _timerRitardo IsNot Nothing Then
                _timerRitardo.Dispose()
                _timerRitardo = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.FermaInterno timer: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub TimerScaduto(stato As Object)
        Try
            Sincronizza()
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.TimerScaduto: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function Sincronizza() As EsitoSincronizzazione
        Return SincronizzaConAttesa(CancellationToken.None)
    End Function

    Private Shared Function SincronizzaConAttesa(ct As CancellationToken) As EsitoSincronizzazione
        Dim esito As New EsitoSincronizzazione()
        Dim acquisito As Boolean = False
        Try
            Monitor.TryEnter(_lockSincronizzazione, acquisito)
            If Not acquisito Then
                esito.Messaggio = "È già in corso una sincronizzazione."
                Return esito
            End If
            esito = EseguiSincronizzazione(ct)
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.Sincronizza: " & ex.ToString())
            esito.Riuscita = False
            esito.Messaggio = "Errore durante la sincronizzazione."
        Finally
            If acquisito Then
                Try
                    Monitor.Exit(_lockSincronizzazione)
                Catch exUscita As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.Sincronizza uscita: " & exUscita.ToString())
                End Try
            End If
        End Try

        If acquisito Then
            Try
                RaiseEvent SincronizzazioneTerminata(esito)
            Catch exEvento As Exception
                _log.ScriviLog("WGSincronizzazioneCloud.Sincronizza evento: " & exEvento.ToString())
            End Try
        End If
        Return esito
    End Function

    Private Shared Function EseguiSincronizzazione(ct As CancellationToken) As EsitoSincronizzazione
        Dim esito As New EsitoSincronizzazione()
        Try
            _ultimoErrore = ""
            Dim sessione As FilenSessione = CaricaSessione()
            If sessione Is Nothing Then
                esito.Messaggio = "Non è stato eseguito l'accesso a Filen."
                Return esito
            End If
            If Not SincronizzazioneAbilitata Then
                esito.Messaggio = "La sincronizzazione non è attiva."
                Return esito
            End If

            Dim radiceCartellaPrima As String = sessione.CartellaRadice
            Dim client As New FilenClient(sessione)

            Dim elenco As List(Of FilenClient.FilenFile)
            Try
                elenco = client.ElencaAsync(ct).GetAwaiter().GetResult()
            Catch exElenco As Exception
                esito.Messaggio = TestoErrore(exElenco, "leggere i dati su Filen") & " Non è stata scritta nessuna modifica."
                Return esito
            End Try

            Dim principale As FilenClient.FilenFile = FilenClient.PiuRecente(elenco, NomeFileSincronizzazione)
            Dim radice As JObject
            If principale IsNot Nothing Then
                radice = LeggiFileRemoto(client, principale, ct)
                If radice Is Nothing Then
                    esito.Messaggio = "Il file di sincronizzazione su Filen non è leggibile: non è stata scritta nessuna modifica."
                    Return esito
                End If
            Else
                radice = New JObject()
            End If

            ' Copie di conflitto (nome diverso) e vecchie versioni con lo stesso nome: vengono unite al file principale.
            ' Le copie di conflitto si cestinano a scrittura riuscita; le vecchie versioni le pulisce già ScriviAsync.
            Dim nomiConflitto As New List(Of String)
            Dim unite As Integer = 0
            For Each altro As FilenClient.FilenFile In elenco
                If Object.ReferenceEquals(altro, principale) Then Continue For
                Dim stessoNome As Boolean = String.Equals(altro.Nome, NomeFileSincronizzazione, StringComparison.Ordinal)
                Dim copiaConflitto As Boolean = Not stessoNome AndAlso altro.Nome.StartsWith(PrefissoFileSincronizzazione, StringComparison.Ordinal) AndAlso altro.Nome.EndsWith(".json", StringComparison.Ordinal)
                If Not stessoNome AndAlso Not copiaConflitto Then Continue For

                Dim altra As JObject = LeggiFileRemoto(client, altro, ct)
                If altra Is Nothing Then
                    If stessoNome Then
                        esito.Messaggio = "Su Filen c'è una versione del file di sincronizzazione che non è leggibile: non è stata scritta nessuna modifica."
                        Return esito
                    End If
                    Continue For
                End If
                UnisciRadici(radice, altra)
                unite += 1
                If copiaConflitto AndAlso Not nomiConflitto.Contains(altro.Nome) Then nomiConflitto.Add(altro.Nome)
            Next

            Dim prima As JObject = CType(radice.DeepClone(), JObject)

            SincronizzaSezioni(radice, esito)

            radice("formato") = FormatoFile
            radice("versione") = VersioneFormato

            Dim cambiato As Boolean = principale Is Nothing OrElse unite > 0 OrElse Not JToken.DeepEquals(SenzaIntestazione(prima), SenzaIntestazione(radice))
            If cambiato Then
                radice("aggiornato") = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                radice("dispositivo") = Environment.MachineName
                Try
                    client.ScriviAsync(NomeFileSincronizzazione, radice.ToString(Newtonsoft.Json.Formatting.Indented), ct).GetAwaiter().GetResult()
                Catch exScrittura As Exception
                    esito.Messaggio = TestoErrore(exScrittura, "scrivere il file di sincronizzazione su Filen")
                    Return esito
                End Try
                esito.FileAggiornato = True
                For Each nome As String In nomiConflitto
                    Try
                        client.CestinaAsync(nome, ct).GetAwaiter().GetResult()
                    Catch exCestino As Exception
                        _log.ScriviLog("WGSincronizzazioneCloud.EseguiSincronizzazione copia di conflitto (" & nome & "): " & exCestino.ToString())
                    End Try
                Next
            End If

            EseguiProcedure(client, elenco, ct, esito)

            If Not String.Equals(radiceCartellaPrima, sessione.CartellaRadice, StringComparison.Ordinal) Then SalvaSessione(sessione)

            esito.Riuscita = True
            If esito.Avvisi.Count = 0 Then
                esito.Messaggio = "Sincronizzazione completata."
            Else
                esito.Messaggio = "Sincronizzazione completata con alcuni avvisi."
            End If
            Return esito
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.EseguiSincronizzazione: " & ex.ToString())
            esito.Riuscita = False
            esito.Messaggio = "Errore durante la sincronizzazione."
            Return esito
        End Try
    End Function

    Private Shared Sub EseguiProcedure(client As FilenClient, elenco As List(Of FilenClient.FilenFile), ct As CancellationToken, esito As EsitoSincronizzazione)
        Try
            Dim registrate As New List(Of ProceduraRegistrata)
            SyncLock _lockSezioni
                registrate.AddRange(_procedure)
            End SyncLock
            For Each registrata As ProceduraRegistrata In registrate
                Try
                    If registrata Is Nothing OrElse registrata.Procedura Is Nothing Then Continue For
                    If ct.IsCancellationRequested Then Exit For
                    registrata.Procedura.Invoke(client, elenco, ct, esito)
                Catch exProcedura As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.EseguiProcedure (" & registrata.Nome & "): " & exProcedura.ToString())
                    esito.Avvisi.Add("Non è stato possibile completare una parte della sincronizzazione.")
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.EseguiProcedure: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Scarica un file di Filen e lo legge come oggetto JSON. Nothing se non si riesce: in quel caso non si scrive nulla.</summary>
    Private Shared Function LeggiFileRemoto(client As FilenClient, file As FilenClient.FilenFile, ct As CancellationToken) As JObject
        Try
            Dim testo As String = client.LeggiTestoAsync(file, ct).GetAwaiter().GetResult()
            If String.IsNullOrWhiteSpace(testo) Then Return New JObject()

            Using lettoreTesto As New StringReader(testo)
                Using lettoreJson As New JsonTextReader(lettoreTesto)
                    lettoreJson.DateParseHandling = DateParseHandling.None
                    Dim token As JToken = JToken.ReadFrom(lettoreJson)
                    Dim oggetto As JObject = TryCast(token, JObject)
                    If oggetto Is Nothing Then _log.ScriviLog("WGSincronizzazioneCloud.LeggiFileRemoto (" & If(file.Nome, "") & "): il contenuto non è un oggetto JSON.")
                    Return oggetto
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.LeggiFileRemoto (" & If(file.Nome, "") & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Sub SincronizzaSezioni(radice As JObject, esito As EsitoSincronizzazione)
        Try
            Dim elenco As New List(Of SezioneRegistrata)
            SyncLock _lockSezioni
                elenco.AddRange(_sezioni)
            End SyncLock
            For Each sezione As SezioneRegistrata In elenco
                Try
                    If sezione Is Nothing OrElse sezione.Unione Is Nothing Then Continue For
                    Dim avvisi As New List(Of String)
                    Dim unite As List(Of Dictionary(Of String, Object)) = sezione.Unione(SezioneInVoci(radice, sezione.Nome), avvisi)
                    If unite IsNot Nothing Then radice(sezione.Nome) = VociInSezione(unite)
                    For Each avviso As String In avvisi
                        If Not String.IsNullOrWhiteSpace(avviso) AndAlso Not esito.Avvisi.Contains(avviso) Then esito.Avvisi.Add(avviso)
                    Next
                Catch exSezione As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.SincronizzaSezioni (" & sezione.Nome & "): " & exSezione.ToString())
                    esito.Avvisi.Add("Non è stato possibile sincronizzare una parte dei dati.")
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SincronizzaSezioni: " & ex.ToString())
            esito.Avvisi.Add("Non è stato possibile sincronizzare i dati.")
        End Try
    End Sub

    Private Shared Sub UnisciRadici(principale As JObject, altra As JObject)
        Try
            For Each proprieta As JProperty In altra.Properties()
                Try
                    Dim sezioneAltra As JArray = TryCast(proprieta.Value, JArray)
                    If sezioneAltra Is Nothing Then Continue For
                    Dim sezionePrincipale As JArray = TryCast(principale(proprieta.Name), JArray)
                    If sezionePrincipale Is Nothing Then
                        principale(proprieta.Name) = sezioneAltra.DeepClone()
                    Else
                        principale(proprieta.Name) = UnisciSezioni(sezionePrincipale, sezioneAltra)
                    End If
                Catch exProprieta As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.UnisciRadici sezione (" & proprieta.Name & "): " & exProprieta.ToString())
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.UnisciRadici: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function UnisciSezioni(prima As JArray, seconda As JArray) As JArray
        Try
            Dim indice As New Dictionary(Of String, JObject)(StringComparer.Ordinal)
            Dim senzaId As New List(Of JObject)
            AggiungiASezione(indice, senzaId, prima)
            AggiungiASezione(indice, senzaId, seconda)
            Dim elenco As New List(Of JObject)(indice.Values)
            elenco.Sort(New ConfrontoOggettiPerId())
            Dim risultato As New JArray()
            For Each oggetto As JObject In elenco
                risultato.Add(oggetto.DeepClone())
            Next
            For Each oggetto As JObject In senzaId
                risultato.Add(oggetto.DeepClone())
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.UnisciSezioni: " & ex.ToString())
            Return prima
        End Try
    End Function

    Private Shared Sub AggiungiASezione(indice As Dictionary(Of String, JObject), senzaId As List(Of JObject), sezione As JArray)
        Try
            For Each elemento As JToken In sezione
                Dim oggetto As JObject = TryCast(elemento, JObject)
                If oggetto Is Nothing Then Continue For
                Dim identificativo As String = IdOggetto(oggetto)
                If identificativo.Length = 0 Then
                    senzaId.Add(oggetto)
                    Continue For
                End If
                Dim esistente As JObject = Nothing
                If indice.TryGetValue(identificativo, esistente) Then
                    If DataOggetto(oggetto) <= DataOggetto(esistente) Then Continue For
                End If
                indice(identificativo) = oggetto
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.AggiungiASezione: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function IdOggetto(oggetto As JObject) As String
        Try
            If oggetto Is Nothing Then Return ""
            Dim token As JToken = oggetto("id")
            If token Is Nothing OrElse token.Type = JTokenType.Null Then Return ""
            Return Convert.ToString(CType(token, JValue).Value, CultureInfo.InvariantCulture).Trim()
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.IdOggetto: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function DataOggetto(oggetto As JObject) As DateTime
        Try
            If oggetto Is Nothing Then Return DateTime.MinValue
            Dim token As JToken = oggetto("modificato")
            If token Is Nothing OrElse token.Type = JTokenType.Null Then Return DateTime.MinValue
            Dim testo As String = Convert.ToString(CType(token, JValue).Value, CultureInfo.InvariantCulture)
            Dim risultato As DateTime
            If DateTime.TryParse(testo, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.DataOggetto: " & ex.ToString())
            Return DateTime.MinValue
        End Try
    End Function

    Private Shared Function SezioneInVoci(radice As JObject, nomeSezione As String) As List(Of Dictionary(Of String, Object))
        Dim risultato As New List(Of Dictionary(Of String, Object))
        Try
            Dim sezione As JArray = TryCast(radice(nomeSezione), JArray)
            If sezione Is Nothing Then Return risultato
            For Each elemento As JToken In sezione
                Dim oggetto As JObject = TryCast(elemento, JObject)
                If oggetto Is Nothing Then Continue For
                risultato.Add(OggettoInDizionario(oggetto))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SezioneInVoci (" & If(nomeSezione, "") & "): " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function OggettoInDizionario(oggetto As JObject) As Dictionary(Of String, Object)
        Dim risultato As New Dictionary(Of String, Object)(StringComparer.Ordinal)
        Try
            For Each proprieta As JProperty In oggetto.Properties()
                risultato(proprieta.Name) = ValoreDaToken(proprieta.Value)
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.OggettoInDizionario: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function ValoreDaToken(token As JToken) As Object
        Try
            If token Is Nothing OrElse token.Type = JTokenType.Null OrElse token.Type = JTokenType.Undefined Then Return Nothing
            Dim oggetto As JObject = TryCast(token, JObject)
            If oggetto IsNot Nothing Then Return OggettoInDizionario(oggetto)
            Dim elenco As JArray = TryCast(token, JArray)
            If elenco IsNot Nothing Then
                Dim valori As New List(Of Object)
                For Each elemento As JToken In elenco
                    valori.Add(ValoreDaToken(elemento))
                Next
                Return valori
            End If
            Dim valore As JValue = TryCast(token, JValue)
            If valore IsNot Nothing Then Return valore.Value
            Return token.ToString(Newtonsoft.Json.Formatting.None)
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.ValoreDaToken: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function VociInSezione(voci As List(Of Dictionary(Of String, Object))) As JArray
        Dim risultato As New JArray()
        Try
            Dim oggetti As New List(Of JObject)
            For Each voce As Dictionary(Of String, Object) In voci
                Try
                    If voce Is Nothing Then Continue For
                    Dim oggetto As JObject = JObject.FromObject(voce)
                    If IdOggetto(oggetto).Length = 0 Then Continue For
                    oggetti.Add(oggetto)
                Catch exVoce As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.VociInSezione voce: " & exVoce.ToString())
                End Try
            Next
            oggetti.Sort(New ConfrontoOggettiPerId())
            For Each oggetto As JObject In oggetti
                risultato.Add(oggetto)
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.VociInSezione: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Shared Function SenzaIntestazione(radice As JObject) As JObject
        Try
            Dim copia As JObject = CType(radice.DeepClone(), JObject)
            copia.Remove("aggiornato")
            copia.Remove("dispositivo")
            Return copia
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SenzaIntestazione: " & ex.ToString())
            Return radice
        End Try
    End Function

    ''' <summary>
    ''' Chiave per cifrare dati che devono poter essere letti su ogni computer collegato allo stesso account Filen
    ''' (password e accessi del browser). Deriva dalla chiave maestra più vecchia dell'account, quindi è uguale ovunque
    ''' e non cambia se si cambia la password di Filen. Una volta derivata resta conservata su questo computer,
    ''' protetta con DPAPI, così non si perde nemmeno uscendo dall'account. Vuota se non è mai stato fatto l'accesso a Filen.
    ''' </summary>
    Public Shared Function ChiavePortabile() As String
        Try
            SyncLock _lockChiavePortabile
                Dim memorizzata As String = ""
                Try
                    Dim cifrata As String = LeggiImpostazione(ChiaveBrowserPortabile)
                    If cifrata.Length > 0 Then
                        memorizzata = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cifrata), EntropiaChiavePortabile, DataProtectionScope.CurrentUser))
                    End If
                Catch exLettura As Exception
                    _log.ScriviLog("WGSincronizzazioneCloud.ChiavePortabile lettura: " & exLettura.Message)
                    memorizzata = ""
                End Try

                Dim sessione As FilenSessione = CaricaSessione()
                If sessione IsNot Nothing Then
                    Dim derivata As String
                    Using hmac As New HMACSHA256(Encoding.UTF8.GetBytes(sessione.ChiaviMaestre(0)))
                        derivata = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes("WinGabriel.DatiPortabili.v1"))).Replace("-", "").ToLowerInvariant()
                    End Using
                    If Not String.Equals(derivata, memorizzata, StringComparison.Ordinal) Then
                        Dim cifrati As Byte() = ProtectedData.Protect(Encoding.UTF8.GetBytes(derivata), EntropiaChiavePortabile, DataProtectionScope.CurrentUser)
                        SalvaImpostazione(ChiaveBrowserPortabile, Convert.ToBase64String(cifrati))
                    End If
                    Return derivata
                End If
                Return memorizzata
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.ChiavePortabile: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Function LeggiImpostazione(chiave As String) As String
        Try
            Dim configurazione As New WGConfig()
            If Not configurazione.EsisteId(chiave) Then Return ""
            Return If(configurazione.LeggiValore(chiave), "").Trim()
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.LeggiImpostazione (" & If(chiave, "") & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Shared Sub SalvaImpostazione(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            If configurazione.EsisteId(chiave) Then
                configurazione.ModificaValore(chiave, If(valore, ""))
            Else
                configurazione.AggiungiValore(chiave, If(valore, ""))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSincronizzazioneCloud.SalvaImpostazione (" & If(chiave, "") & "): " & ex.ToString())
        End Try
    End Sub

End Class


' =====================================================================================================
' Client minimale per Filen (https://docs.filen.io/docs/api): registrazione, login,
' lettura e scrittura di UN file JSON nella cartella radice dell'account.
' Conversione in VB.NET di FilenClient.cs (stesse firme pubbliche: il resto del codice non cambia).
'
' Versione per .NET Framework 4.7.2+ (quindi anche 4.8.1): niente AesGcm né altre API di .NET Core.
' Librerie esterne: Newtonsoft.Json (JSON) e BouncyCastle.Cryptography (solo per AES-GCM).
' PBKDF2-SHA512 usa Rfc2898DeriveBytes con HashAlgorithmName (disponibile da 4.7.2).
'
' STATO: scritto dalla documentazione ufficiale, NON provato contro il server.
' I punti da verificare alla prima prova sono marcati con "VERIFICARE".
' =====================================================================================================

Namespace Global.GabrielSync.Filen

    Public NotInheritable Class FilenException
        Inherits Exception

        Private ReadOnly _codice As String

        Public ReadOnly Property Codice As String
            Get
                Return _codice
            End Get
        End Property

        Public Sub New(codice As String, messaggio As String)
            MyBase.New(messaggio)
            _codice = codice
        End Sub
    End Class

    ''' <summary>Dati da conservare dopo il login (da proteggere: contengono la chiave di cifratura).</summary>
    Public NotInheritable Class FilenSessione
        Public Property Email As String = ""
        Public Property ApiKey As String = ""
        Public Property ChiaviMaestre As New List(Of String)
        Public Property CartellaRadice As String
    End Class

    ''' <summary>Piccoli aiuti per leggere il JSON di Filen senza eccezioni quando un campo manca o è null.</summary>
    Friend NotInheritable Class FilenJson

        Private Sub New()
        End Sub

        Public Shared Function Analizza(testo As String) As JToken
            Using lettoreTesto As New StringReader(testo)
                Using lettoreJson As New JsonTextReader(lettoreTesto)
                    lettoreJson.DateParseHandling = DateParseHandling.None
                    Return JToken.ReadFrom(lettoreJson)
                End Using
            End Using
        End Function

        ''' <summary>Segue il percorso di proprietà. Nothing se un passaggio manca o è null.</summary>
        Public Shared Function Percorso(radice As JToken, ParamArray chiavi As String()) As JToken
            Dim corrente As JToken = radice
            For Each chiave As String In chiavi
                Dim oggetto As JObject = TryCast(corrente, JObject)
                If oggetto Is Nothing Then Return Nothing
                corrente = oggetto(chiave)
            Next
            If corrente IsNot Nothing AndAlso (corrente.Type = JTokenType.Null OrElse corrente.Type = JTokenType.Undefined) Then Return Nothing
            Return corrente
        End Function

        Public Shared Function InTesto(token As JToken) As String
            If token Is Nothing OrElse token.Type = JTokenType.Null OrElse token.Type = JTokenType.Undefined Then Return Nothing
            Return token.ToString()
        End Function

        Public Shared Function InIntero(token As JToken, predefinito As Integer) As Integer
            If token IsNot Nothing AndAlso token.Type = JTokenType.Integer Then
                Return Convert.ToInt32(CType(token, JValue).Value, CultureInfo.InvariantCulture)
            End If
            Return predefinito
        End Function

        Public Shared Function InLungo(token As JToken, predefinito As Long) As Long
            If token IsNot Nothing AndAlso token.Type = JTokenType.Integer Then
                Return Convert.ToInt64(CType(token, JValue).Value, CultureInfo.InvariantCulture)
            End If
            Return predefinito
        End Function
    End Class

    Public NotInheritable Class FilenCrypto

        Private Const Alfabeto As String = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        Private Shared ReadOnly Rng As RandomNumberGenerator = RandomNumberGenerator.Create()
        Private Shared ReadOnly LockRng As New Object()

        Private Sub New()
        End Sub

        ''' <summary>Intero casuale uniforme in [0, esclusoMax), con esclusoMax tra 1 e 256 (scarta i valori che sbilancerebbero).</summary>
        Private Shared Function IntCasuale(esclusoMax As Integer) As Integer
            Dim limite As Integer = 256 - (256 Mod esclusoMax)
            Dim b As Byte() = New Byte(0) {}
            Do
                SyncLock LockRng
                    Rng.GetBytes(b)
                End SyncLock
            Loop While b(0) >= limite
            Return b(0) Mod esclusoMax
        End Function

        Private Shared Function InEsadecimale(dati As Byte()) As String
            Return BitConverter.ToString(dati).Replace("-", "").ToLowerInvariant()
        End Function

        Public Shared Function StringaCasuale(lunghezza As Integer) As String
            Dim caratteri As Char() = New Char(lunghezza - 1) {}
            For i As Integer = 0 To lunghezza - 1
                caratteri(i) = Alfabeto(IntCasuale(Alfabeto.Length))
            Next
            Return New String(caratteri)
        End Function

        Public Shared Function Sha1Hex(dati As Byte()) As String
            Using sha As SHA1 = SHA1.Create()
                Return InEsadecimale(sha.ComputeHash(dati))
            End Using
        End Function

        Public Shared Function Sha512Hex(dati As Byte()) As String
            Using sha As SHA512 = SHA512.Create()
                Return InEsadecimale(sha.ComputeHash(dati))
            End Using
        End Function

        ''' <summary>
        ''' Auth version 2: PBKDF2-SHA512, 200.000 iterazioni, 64 byte, in esadecimale.
        ''' Prima metà = chiave maestra; seconda metà = password di accesso, poi hashata con SHA-512.
        ''' (Rfc2898DeriveBytes di .NET Framework pretende un salt di almeno 8 byte: quello di Filen è molto più lungo.)
        ''' </summary>
        Public Shared Function DerivaCredenziali(password As String, salt As String) As (ChiaveMaestra As String, PasswordAccesso As String)
            Dim derivata As Byte()
            Using kdf As New Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 200000, HashAlgorithmName.SHA512)
                derivata = kdf.GetBytes(64)
            End Using
            Dim esadecimale As String = InEsadecimale(derivata)
            Dim meta As Integer = esadecimale.Length \ 2
            Dim chiaveMaestra As String = esadecimale.Substring(0, meta)
            Dim passwordGrezza As String = esadecimale.Substring(meta)
            Dim passwordAccesso As String = Sha512Hex(Encoding.UTF8.GetBytes(passwordGrezza))
            Return (chiaveMaestra, passwordAccesso)
        End Function

        Private Shared Function ChiaveMetadati(chiave As String) As Byte()
            Dim byteChiave As Byte() = Encoding.UTF8.GetBytes(chiave)
            Using kdf As New Rfc2898DeriveBytes(byteChiave, byteChiave, 1, HashAlgorithmName.SHA512)
                Return kdf.GetBytes(32)
            End Using
        End Function

        ' ---------- AES-256-GCM (BouncyCastle), tag di 16 byte ----------

        ''' <summary>Restituisce cifrato + tag (16 byte) in un unico blocco.</summary>
        Private Shared Function GcmCifra(chiave As Byte(), nonce As Byte(), chiaro As Byte()) As Byte()
            Dim cifrario As New Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(New Org.BouncyCastle.Crypto.Engines.AesEngine())
            cifrario.Init(True, New Org.BouncyCastle.Crypto.Parameters.AeadParameters(New Org.BouncyCastle.Crypto.Parameters.KeyParameter(chiave), 128, nonce))
            Dim uscita As Byte() = New Byte(cifrario.GetOutputSize(chiaro.Length) - 1) {}
            Dim scritti As Integer = cifrario.ProcessBytes(chiaro, 0, chiaro.Length, uscita, 0)
            scritti += cifrario.DoFinal(uscita, scritti)
            If scritti <> uscita.Length Then Array.Resize(uscita, scritti)
            Return uscita
        End Function

        ''' <summary>Decifra un blocco cifrato + tag. Se il tag non torna (chiave sbagliata o dati alterati) lancia CryptographicException.</summary>
        Private Shared Function GcmDecifra(chiave As Byte(), nonce As Byte(), cifratoETag As Byte()) As Byte()
            Try
                Dim cifrario As New Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(New Org.BouncyCastle.Crypto.Engines.AesEngine())
                cifrario.Init(False, New Org.BouncyCastle.Crypto.Parameters.AeadParameters(New Org.BouncyCastle.Crypto.Parameters.KeyParameter(chiave), 128, nonce))
                Dim uscita As Byte() = New Byte(cifrario.GetOutputSize(cifratoETag.Length) - 1) {}
                Dim scritti As Integer = cifrario.ProcessBytes(cifratoETag, 0, cifratoETag.Length, uscita, 0)
                scritti += cifrario.DoFinal(uscita, scritti)
                If scritti <> uscita.Length Then Array.Resize(uscita, scritti)
                Return uscita
            Catch exGcm As Org.BouncyCastle.Crypto.InvalidCipherTextException
                Throw New CryptographicException("Autenticazione AES-GCM non riuscita.", exGcm)
            End Try
        End Function

        ''' <summary>Formato "002" + nonce(12 caratteri) + base64(cifrato + tag).</summary>
        Public Shared Function CifraMetadati(testo As String, chiave As String) As String
            Dim nonce As String = StringaCasuale(12)
            Dim unito As Byte() = GcmCifra(ChiaveMetadati(chiave), Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(testo))
            Return "002" & nonce & Convert.ToBase64String(unito)
        End Function

        Public Shared Function DecifraMetadati(metadati As String, chiave As String) As String
            If metadati.Length < 3 + 12 OrElse Not metadati.StartsWith("002", StringComparison.Ordinal) Then
                Throw New FilenException("formato_metadati", "Metadati in formato non supportato.")
            End If
            Dim nonce As Byte() = Encoding.UTF8.GetBytes(metadati.Substring(3, 12))
            Dim dati As Byte() = Convert.FromBase64String(metadati.Substring(15))
            If dati.Length < 16 Then
                Throw New FilenException("formato_metadati", "Metadati troppo corti.")
            End If
            Return Encoding.UTF8.GetString(GcmDecifra(ChiaveMetadati(chiave), nonce, dati))
        End Function

        ''' <summary>Contenuto del file: nonce(12 byte) + cifrato + tag. La chiave è di 32 caratteri usati come byte UTF-8.</summary>
        Public Shared Function CifraDati(dati As Byte(), chiaveFile As String) As Byte()
            Dim chiave As Byte() = Encoding.UTF8.GetBytes(chiaveFile)
            If chiave.Length <> 32 Then
                Throw New FilenException("chiave_file", "La chiave del file deve essere di 32 byte.")
            End If
            Dim nonce As Byte() = Encoding.UTF8.GetBytes(StringaCasuale(12))
            Dim cifrato As Byte() = GcmCifra(chiave, nonce, dati)
            Dim risultato As Byte() = New Byte(12 + cifrato.Length - 1) {}
            Buffer.BlockCopy(nonce, 0, risultato, 0, 12)
            Buffer.BlockCopy(cifrato, 0, risultato, 12, cifrato.Length)
            Return risultato
        End Function

        Public Shared Function DecifraDati(blocco As Byte(), chiaveFile As String) As Byte()
            If blocco.Length < 12 + 16 Then
                Throw New FilenException("blocco", "Blocco cifrato troppo corto.")
            End If
            Dim chiave As Byte() = Encoding.UTF8.GetBytes(chiaveFile)
            Dim nonce As Byte() = New Byte(11) {}
            Dim cifratoETag As Byte() = New Byte(blocco.Length - 12 - 1) {}
            Buffer.BlockCopy(blocco, 0, nonce, 0, 12)
            Buffer.BlockCopy(blocco, 12, cifratoETag, 0, cifratoETag.Length)
            Return GcmDecifra(chiave, nonce, cifratoETag)
        End Function
    End Class

    Friend NotInheritable Class FilenHttp

        Private Shared ReadOnly ClienteHttp As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(90)}
        Private Const Gateway As String = "https://gateway.filen.io"
        Private Const Ingest As String = "https://ingest.filen.io"
        Private Const Egest As String = "https://egest.filen.io"

        Private Sub New()
        End Sub

        Public Shared Async Function PostAsync(percorso As String, corpo As JObject, apiKey As String, ct As CancellationToken) As Task(Of JToken)
            Using richiesta As New HttpRequestMessage(HttpMethod.Post, Gateway & percorso)
                If apiKey IsNot Nothing Then
                    richiesta.Headers.Authorization = New AuthenticationHeaderValue("Bearer", apiKey)
                End If
                richiesta.Content = New StringContent(corpo.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
                Using risposta As HttpResponseMessage = Await ClienteHttp.SendAsync(richiesta, ct).ConfigureAwait(False)
                    Return Await LeggiRispostaAsync(risposta, ct).ConfigureAwait(False)
                End Using
            End Using
        End Function

        Public Shared Async Function GetAsync(percorso As String, apiKey As String, ct As CancellationToken) As Task(Of JToken)
            Using richiesta As New HttpRequestMessage(HttpMethod.Get, Gateway & percorso)
                richiesta.Headers.Authorization = New AuthenticationHeaderValue("Bearer", apiKey)
                Using risposta As HttpResponseMessage = Await ClienteHttp.SendAsync(richiesta, ct).ConfigureAwait(False)
                    Return Await LeggiRispostaAsync(risposta, ct).ConfigureAwait(False)
                End Using
            End Using
        End Function

        Public Shared Async Function CaricaBloccoAsync(uuid As String, indice As Integer, genitore As String, uploadKey As String,
                                                       cifrato As Byte(), apiKey As String, ct As CancellationToken) As Task(Of (Bucket As String, Regione As String))
            Dim hash As String = FilenCrypto.Sha512Hex(cifrato)
            Dim url As String = $"{Ingest}/v3/upload?uuid={uuid}&index={indice}&parent={genitore}&uploadKey={uploadKey}&hash={hash}"
            Using richiesta As New HttpRequestMessage(HttpMethod.Post, url)
                richiesta.Headers.Authorization = New AuthenticationHeaderValue("Bearer", apiKey)
                richiesta.Content = New ByteArrayContent(cifrato)
                richiesta.Content.Headers.ContentType = New MediaTypeHeaderValue("application/octet-stream")
                Using risposta As HttpResponseMessage = Await ClienteHttp.SendAsync(richiesta, ct).ConfigureAwait(False)
                    Dim nodo As JToken = Await LeggiRispostaAsync(risposta, ct).ConfigureAwait(False)
                    ' La guida mostra {bucket, region} nel corpo; l'SDK le avvolge in "data". Gestisco entrambi.
                    Dim dati As JToken = FilenJson.Percorso(nodo, "data")
                    If dati Is Nothing Then dati = nodo
                    Dim bucket As String = FilenJson.InTesto(FilenJson.Percorso(dati, "bucket"))
                    Dim regione As String = FilenJson.InTesto(FilenJson.Percorso(dati, "region"))
                    If String.IsNullOrEmpty(bucket) OrElse String.IsNullOrEmpty(regione) Then
                        Throw New FilenException("upload", "Risposta di upload senza bucket/region.")
                    End If
                    Return (bucket, regione)
                End Using
            End Using
        End Function

        Public Shared Async Function ScaricaBloccoAsync(regione As String, bucket As String, uuid As String, indice As Integer,
                                                        apiKey As String, ct As CancellationToken) As Task(Of Byte())
            Dim url As String = $"{Egest}/{regione}/{bucket}/{uuid}/{indice}"
            Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                richiesta.Headers.Authorization = New AuthenticationHeaderValue("Bearer", apiKey)
                Using risposta As HttpResponseMessage = Await ClienteHttp.SendAsync(richiesta, ct).ConfigureAwait(False)
                    If Not risposta.IsSuccessStatusCode Then
                        Throw New FilenException("download_" & CInt(risposta.StatusCode).ToString(CultureInfo.InvariantCulture), "Download del blocco non riuscito.")
                    End If
                    Return Await risposta.Content.ReadAsByteArrayAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

        Private Shared Async Function LeggiRispostaAsync(risposta As HttpResponseMessage, ct As CancellationToken) As Task(Of JToken)
            Dim corpoTesto As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
            Dim nodo As JToken = Nothing
            Try
                nodo = FilenJson.Analizza(corpoTesto)
            Catch exJson As JsonException
                ' Corpo non JSON: si decide sotto in base al codice HTTP.
            End Try
            If nodo IsNot Nothing AndAlso nodo.Type = JTokenType.Null Then nodo = Nothing

            Dim oggetto As JObject = TryCast(nodo, JObject)
            If oggetto IsNot Nothing Then
                Dim stato As JToken = oggetto("status")
                If stato IsNot Nothing AndAlso stato.Type = JTokenType.Boolean AndAlso Not Convert.ToBoolean(CType(stato, JValue).Value, CultureInfo.InvariantCulture) Then
                    Throw New FilenException(
                        If(FilenJson.InTesto(oggetto("code")), "errore"),
                        If(FilenJson.InTesto(oggetto("message")), "Errore di Filen."))
                End If
            End If
            If Not risposta.IsSuccessStatusCode AndAlso nodo Is Nothing Then
                Dim codiceHttp As String = CInt(risposta.StatusCode).ToString(CultureInfo.InvariantCulture)
                Throw New FilenException("http_" & codiceHttp, "Errore di rete (HTTP " & codiceHttp & ").")
            End If
            Return nodo
        End Function
    End Class

    Public NotInheritable Class FilenClient

        Private Const DimensioneBlocco As Integer = 1024 * 1024

        ''' <summary>
        ''' Hash del nome per account con auth version 1/2 ("V2Hash" dell'SDK ufficiale):
        ''' SHA-1 (esadecimale) della stringa esadecimale del SHA-512 del nome IN MINUSCOLO -> 40 caratteri.
        ''' Il solo SHA-512 (128 caratteri) descritto nella documentazione viene rifiutato dal server
        ''' con "invalid_params: Invalid nameHashed".
        ''' </summary>
        Public Shared Property HashNome As Func(Of String, String) =
            Function(nome As String) FilenCrypto.Sha1Hex(Encoding.UTF8.GetBytes(FilenCrypto.Sha512Hex(Encoding.UTF8.GetBytes(nome.ToLowerInvariant()))))

        Private ReadOnly _sessione As FilenSessione

        Public Sub New(sessione As FilenSessione)
            _sessione = sessione
            If _sessione.ChiaviMaestre.Count = 0 Then
                Throw New FilenException("sessione", "Sessione senza chiavi di cifratura.")
            End If
        End Sub

        Public ReadOnly Property Sessione As FilenSessione
            Get
                Return _sessione
            End Get
        End Property

        ' ---------- Account ----------

        ''' <summary>
        ''' Crea l'account. Filen invia una email di conferma: finché non viene confermata il login non riesce.
        ''' VERIFICARE: se il server richiede altri campi (per esempio affId/refId) vanno aggiunti qui.
        ''' </summary>
        Public Shared Async Function RegistraAsync(email As String, password As String, Optional ct As CancellationToken = Nothing) As Task
            Dim salt As String = FilenCrypto.StringaCasuale(256)
            Dim credenziali As (ChiaveMaestra As String, PasswordAccesso As String) = FilenCrypto.DerivaCredenziali(password, salt)
            Dim corpo As New JObject()
            corpo("email") = email
            corpo("password") = credenziali.PasswordAccesso
            corpo("authVersion") = 2
            corpo("salt") = salt
            Await FilenHttp.PostAsync("/v3/register", corpo, Nothing, ct).ConfigureAwait(False)
        End Function

        Public Shared Async Function ReinviaConfermaAsync(email As String, Optional ct As CancellationToken = Nothing) As Task
            Dim corpo As New JObject()
            corpo("email") = email
            Await FilenHttp.PostAsync("/v3/confirmation/send", corpo, Nothing, ct).ConfigureAwait(False)
        End Function

        Public Shared Async Function AccediAsync(email As String, password As String, Optional codice2fa As String = Nothing, Optional ct As CancellationToken = Nothing) As Task(Of FilenSessione)
            Dim corpoInfo As New JObject()
            corpoInfo("email") = email
            Dim info As JToken = Await FilenHttp.PostAsync("/v3/auth/info", corpoInfo, Nothing, ct).ConfigureAwait(False)
            Dim datiInfo As JToken = FilenJson.Percorso(info, "data")
            Dim salt As String = FilenJson.InTesto(FilenJson.Percorso(datiInfo, "salt"))
            If String.IsNullOrEmpty(salt) Then
                Throw New FilenException("auth_info", "Il server non ha restituito il salt dell'account.")
            End If
            Dim versione As Integer = FilenJson.InIntero(FilenJson.Percorso(datiInfo, "authVersion"), 2)
            If versione <> 2 Then
                Throw New FilenException("auth_version", "Versione di autenticazione non supportata: " & versione.ToString(CultureInfo.InvariantCulture))
            End If

            Dim credenziali As (ChiaveMaestra As String, PasswordAccesso As String) = FilenCrypto.DerivaCredenziali(password, salt)
            Dim chiaveMaestra As String = credenziali.ChiaveMaestra
            Dim corpoLogin As New JObject()
            corpoLogin("email") = email
            corpoLogin("password") = credenziali.PasswordAccesso
            corpoLogin("twoFactorCode") = If(String.IsNullOrWhiteSpace(codice2fa), "XXXXXX", codice2fa)
            corpoLogin("authVersion") = 2
            Dim login As JToken = Await FilenHttp.PostAsync("/v3/login", corpoLogin, Nothing, ct).ConfigureAwait(False)
            Dim apiKey As String = FilenJson.InTesto(FilenJson.Percorso(login, "data", "apiKey"))
            If String.IsNullOrEmpty(apiKey) Then
                Throw New FilenException("login", "Login senza chiave API.")
            End If

            ' Elenco di tutte le chiavi maestre (attuale e passate). L'ultima è quella in uso.
            ' VERIFICARE: nome del campo di risposta ("keys").
            Dim corpoChiavi As New JObject()
            corpoChiavi("masterKeys") = FilenCrypto.CifraMetadati(chiaveMaestra, chiaveMaestra)
            Dim chiaviCifrate As JToken = Await FilenHttp.PostAsync("/v3/user/masterKeys", corpoChiavi, apiKey, ct).ConfigureAwait(False)
            Dim elencoCifrato As String = FilenJson.InTesto(FilenJson.Percorso(chiaviCifrate, "data", "keys"))
            Dim chiavi As New List(Of String)
            If Not String.IsNullOrEmpty(elencoCifrato) Then
                Dim elenco As String = FilenCrypto.DecifraMetadati(elencoCifrato, chiaveMaestra)
                chiavi.AddRange(elenco.Split(New Char() {"|"c}, StringSplitOptions.RemoveEmptyEntries))
            End If

            If Not chiavi.Contains(chiaveMaestra) Then chiavi.Add(chiaveMaestra)

            Dim nuovaSessione As New FilenSessione()
            nuovaSessione.Email = email
            nuovaSessione.ApiKey = apiKey
            nuovaSessione.ChiaviMaestre = chiavi
            Return nuovaSessione
        End Function

        ' ---------- File di sincronizzazione ----------

        Private ReadOnly Property ChiavePrincipale As String
            Get
                Return _sessione.ChiaviMaestre(_sessione.ChiaviMaestre.Count - 1)
            End Get
        End Property

        Private Async Function RadiceAsync(ct As CancellationToken) As Task(Of String)
            If Not String.IsNullOrEmpty(_sessione.CartellaRadice) Then Return _sessione.CartellaRadice
            Dim risposta As JToken = Await FilenHttp.GetAsync("/v3/user/baseFolder", _sessione.ApiKey, ct).ConfigureAwait(False)
            Dim uuid As String = FilenJson.InTesto(FilenJson.Percorso(risposta, "data", "uuid"))
            If String.IsNullOrEmpty(uuid) Then
                Throw New FilenException("radice", "Cartella radice non trovata.")
            End If
            _sessione.CartellaRadice = uuid
            Return uuid
        End Function

        Private Function DecifraConTutteLeChiavi(metadati As String) As String
            For i As Integer = _sessione.ChiaviMaestre.Count - 1 To 0 Step -1
                Try
                    Return FilenCrypto.DecifraMetadati(metadati, _sessione.ChiaviMaestre(i))
                Catch exCifratura As CryptographicException
                    ' Chiave sbagliata per questi metadati: si prova la precedente.
                End Try
            Next
            Return Nothing
        End Function

        ''' <summary>Un file nella cartella radice dell'account (nome già decifrato).</summary>
        Public NotInheritable Class FilenFile
            Public Property Uuid As String = ""
            Public Property Nome As String = ""
            Public Property Timestamp As Long
            Public Property Dimensione As Long
            Public Property Modificato As Long
            Friend Property Chiave As String = ""
            Friend Property Blocchi As Integer
            Friend Property Bucket As String = ""
            Friend Property Regione As String = ""
        End Class

        ''' <summary>Elenca i file della radice, cercandoli per nome: nessuna scelta da parte dell'utente.</summary>
        Public Async Function ElencaAsync(Optional ct As CancellationToken = Nothing) As Task(Of List(Of FilenFile))
            Dim radice As String = Await RadiceAsync(ct).ConfigureAwait(False)
            Dim corpo As New JObject()
            corpo("uuid") = radice
            Dim risposta As JToken = Await FilenHttp.PostAsync("/v3/dir/content", corpo, _sessione.ApiKey, ct).ConfigureAwait(False)
            Dim risultato As New List(Of FilenFile)
            Dim caricati As JArray = TryCast(FilenJson.Percorso(risposta, "data", "uploads"), JArray)
            If caricati Is Nothing Then Return risultato
            For Each elemento As JToken In caricati
                Dim metadati As String = FilenJson.InTesto(FilenJson.Percorso(elemento, "metadata"))
                If String.IsNullOrEmpty(metadati) Then Continue For
                Dim chiaro As String = DecifraConTutteLeChiavi(metadati)
                If chiaro Is Nothing Then Continue For
                Dim m As JToken = FilenJson.Analizza(chiaro)
                Dim nome As String = FilenJson.InTesto(FilenJson.Percorso(m, "name"))
                Dim chiave As String = FilenJson.InTesto(FilenJson.Percorso(m, "key"))
                If String.IsNullOrEmpty(nome) OrElse String.IsNullOrEmpty(chiave) Then Continue For

                Dim file As New FilenFile()
                file.Uuid = If(FilenJson.InTesto(FilenJson.Percorso(elemento, "uuid")), "")
                file.Nome = nome
                file.Chiave = chiave
                file.Blocchi = FilenJson.InIntero(FilenJson.Percorso(elemento, "chunks"), 1)
                file.Bucket = If(FilenJson.InTesto(FilenJson.Percorso(elemento, "bucket")), "")
                file.Regione = If(FilenJson.InTesto(FilenJson.Percorso(elemento, "region")), "")
                file.Timestamp = FilenJson.InLungo(FilenJson.Percorso(elemento, "timestamp"), 0)
                file.Dimensione = FilenJson.InLungo(FilenJson.Percorso(m, "size"), 0)
                file.Modificato = FilenJson.InLungo(FilenJson.Percorso(m, "lastModified"), file.Timestamp)
                risultato.Add(file)
            Next
            Return risultato
        End Function

        ''' <summary>Scarica e decifra un file come testo UTF-8 (senza BOM).</summary>
        Public Async Function LeggiTestoAsync(file As FilenFile, Optional ct As CancellationToken = Nothing) As Task(Of String)
            Using memoria As New MemoryStream()
                For i As Integer = 0 To file.Blocchi - 1
                    Dim cifrato As Byte() = Await FilenHttp.ScaricaBloccoAsync(file.Regione, file.Bucket, file.Uuid, i, _sessione.ApiKey, ct).ConfigureAwait(False)
                    Dim chiaro As Byte() = FilenCrypto.DecifraDati(cifrato, file.Chiave)
                    memoria.Write(chiaro, 0, chiaro.Length)
                Next

                Dim byteLetti As Byte() = memoria.ToArray()
                Dim inizio As Integer = 0
                If byteLetti.Length >= 3 AndAlso byteLetti(0) = &HEF AndAlso byteLetti(1) = &HBB AndAlso byteLetti(2) = &HBF Then inizio = 3
                Return Encoding.UTF8.GetString(byteLetti, inizio, byteLetti.Length - inizio)
            End Using
        End Function

        ''' <summary>Il file con quel nome più recente, oppure Nothing se non esiste.</summary>
        Public Shared Function PiuRecente(file As IEnumerable(Of FilenFile), nome As String) As FilenFile
            Return file.Where(Function(f) String.Equals(f.Nome, nome, StringComparison.Ordinal)) _
                       .OrderByDescending(Function(f) f.Timestamp) _
                       .FirstOrDefault()
        End Function

        ''' <summary>
        ''' Carica una nuova versione del file. Come da guida Filen si usa sempre un UUID nuovo, anche per sovrascrivere.
        ''' Poi, se restano copie più vecchie con lo stesso nome, le sposta nel cestino (best effort).
        ''' VERIFICARE: se il server versiona da solo, la pulizia non trova nulla da fare, ed è giusto così.
        ''' </summary>
        Public Async Function ScriviAsync(nome As String, json As String, Optional ct As CancellationToken = Nothing) As Task
            Dim dati As Byte() = New UTF8Encoding(False).GetBytes(json)
            If dati.Length = 0 Then
                Throw New FilenException("vuoto", "Filen non accetta file vuoti.")
            End If

            Dim radice As String = Await RadiceAsync(ct).ConfigureAwait(False)
            Dim uuid As String = Guid.NewGuid().ToString()
            ' VERIFICARE: la guida scrive "lunghezza 12" per la chiave, ma AES-256 richiede 32 byte
            ' (l'SDK ufficiale usa 32 caratteri). Uso 32.
            Dim chiaveFile As String = FilenCrypto.StringaCasuale(32)
            Dim uploadKey As String = FilenCrypto.StringaCasuale(32)
            Dim blocchi As Integer = (dati.Length + DimensioneBlocco - 1) \ DimensioneBlocco

            For i As Integer = 0 To blocchi - 1
                Dim inizio As Integer = i * DimensioneBlocco
                Dim lunghezza As Integer = Math.Min(DimensioneBlocco, dati.Length - inizio)
                Dim porzione As Byte() = New Byte(lunghezza - 1) {}
                Buffer.BlockCopy(dati, inizio, porzione, 0, lunghezza)
                Dim cifrato As Byte() = FilenCrypto.CifraDati(porzione, chiaveFile)
                Await FilenHttp.CaricaBloccoAsync(uuid, i, radice, uploadKey, cifrato, _sessione.ApiKey, ct).ConfigureAwait(False)
            Next

            Dim adesso As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            Dim oggettoMetadati As New JObject()
            oggettoMetadati("name") = nome
            oggettoMetadati("size") = dati.Length
            oggettoMetadati("mime") = "application/json"
            oggettoMetadati("key") = chiaveFile
            oggettoMetadati("lastModified") = adesso
            oggettoMetadati("creation") = adesso
            Dim metadatiFile As String = oggettoMetadati.ToString(Newtonsoft.Json.Formatting.None)

            ' VERIFICARE: la specifica non dice se "mime" va cifrato (l'SDK storico lo cifra con la chiave del file).
            Dim corpo As New JObject()
            corpo("uuid") = uuid
            corpo("name") = FilenCrypto.CifraMetadati(nome, chiaveFile)
            corpo("nameHashed") = HashNome(nome)
            corpo("size") = FilenCrypto.CifraMetadati(dati.Length.ToString(CultureInfo.InvariantCulture), chiaveFile)
            corpo("chunks") = blocchi
            corpo("mime") = "application/json"
            corpo("rm") = FilenCrypto.StringaCasuale(32)
            corpo("metadata") = FilenCrypto.CifraMetadati(metadatiFile, ChiavePrincipale)
            corpo("version") = 2
            corpo("uploadKey") = uploadKey
            Await FilenHttp.PostAsync("/v3/upload/done", corpo, _sessione.ApiKey, ct).ConfigureAwait(False)

            Try
                Dim dopo As List(Of FilenFile) = Await ElencaAsync(ct).ConfigureAwait(False)
                Dim nuovo As FilenFile = dopo.FirstOrDefault(Function(f) String.Equals(f.Uuid, uuid, StringComparison.Ordinal))
                If nuovo Is Nothing Then Return
                Dim vecchi As List(Of FilenFile) = dopo.Where(Function(f) String.Equals(f.Nome, nome, StringComparison.Ordinal) _
                                                                  AndAlso Not String.Equals(f.Uuid, uuid, StringComparison.Ordinal) _
                                                                  AndAlso f.Timestamp <= nuovo.Timestamp).ToList()
                For Each vecchio As FilenFile In vecchi
                    Await CestinaUuidAsync(vecchio.Uuid, ct).ConfigureAwait(False)
                Next
            Catch exPulizia As Exception
                ' La pulizia è facoltativa: il file nuovo è già al suo posto.
            End Try
        End Function

        Public Async Function CaricaFileAsync(nome As String, percorsoFile As String, modificatoUnix As Long, Optional ct As CancellationToken = Nothing) As Task(Of String)
            Dim radice As String = Await RadiceAsync(ct).ConfigureAwait(False)
            Dim uuid As String = Guid.NewGuid().ToString()
            Dim chiaveFile As String = FilenCrypto.StringaCasuale(32)
            Dim uploadKey As String = FilenCrypto.StringaCasuale(32)
            Dim lunghezza As Long = 0
            Dim blocchi As Integer = 0

            Using flusso As New FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, True)
                lunghezza = flusso.Length
                If lunghezza = 0 Then
                    Throw New FilenException("vuoto", "Filen non accetta file vuoti.")
                End If
                blocchi = CInt((lunghezza + DimensioneBlocco - 1) \ DimensioneBlocco)
                Dim lotto As Byte() = New Byte(DimensioneBlocco - 1) {}
                For indice As Integer = 0 To blocchi - 1
                    ct.ThrowIfCancellationRequested()
                    Dim previsti As Integer = CInt(Math.Min(CLng(DimensioneBlocco), lunghezza - CLng(indice) * DimensioneBlocco))
                    Dim letti As Integer = 0
                    While letti < previsti
                        Dim ottenuti As Integer = Await flusso.ReadAsync(lotto, letti, previsti - letti, ct).ConfigureAwait(False)
                        If ottenuti <= 0 Then Exit While
                        letti += ottenuti
                    End While
                    If letti <> previsti Then
                        Throw New FilenException("lettura", "Il file è cambiato durante il caricamento.")
                    End If
                    Dim porzione As Byte() = New Byte(previsti - 1) {}
                    Buffer.BlockCopy(lotto, 0, porzione, 0, previsti)
                    Dim cifrato As Byte() = FilenCrypto.CifraDati(porzione, chiaveFile)
                    Await CaricaBloccoConRiprovaAsync(uuid, indice, radice, uploadKey, cifrato, ct).ConfigureAwait(False)
                Next
            End Using

            Dim adesso As Long = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            If modificatoUnix <= 0 Then modificatoUnix = adesso
            Dim oggettoMetadati As New JObject()
            oggettoMetadati("name") = nome
            oggettoMetadati("size") = lunghezza
            oggettoMetadati("mime") = "application/octet-stream"
            oggettoMetadati("key") = chiaveFile
            oggettoMetadati("lastModified") = modificatoUnix
            oggettoMetadati("creation") = adesso
            Dim metadatiFile As String = oggettoMetadati.ToString(Newtonsoft.Json.Formatting.None)

            Dim corpo As New JObject()
            corpo("uuid") = uuid
            corpo("name") = FilenCrypto.CifraMetadati(nome, chiaveFile)
            corpo("nameHashed") = HashNome(nome)
            corpo("size") = FilenCrypto.CifraMetadati(lunghezza.ToString(CultureInfo.InvariantCulture), chiaveFile)
            corpo("chunks") = blocchi
            corpo("mime") = "application/octet-stream"
            corpo("rm") = FilenCrypto.StringaCasuale(32)
            corpo("metadata") = FilenCrypto.CifraMetadati(metadatiFile, ChiavePrincipale)
            corpo("version") = 2
            corpo("uploadKey") = uploadKey
            Await FilenHttp.PostAsync("/v3/upload/done", corpo, _sessione.ApiKey, ct).ConfigureAwait(False)
            Return uuid
        End Function

        Private Async Function CaricaBloccoConRiprovaAsync(uuid As String, indice As Integer, radice As String, uploadKey As String, cifrato As Byte(), ct As CancellationToken) As Task
            Dim ultimoErrore As Exception = Nothing
            For tentativo As Integer = 1 To 3
                Dim riuscito As Boolean = False
                Try
                    Await FilenHttp.CaricaBloccoAsync(uuid, indice, radice, uploadKey, cifrato, _sessione.ApiKey, ct).ConfigureAwait(False)
                    riuscito = True
                Catch exAnnullato As OperationCanceledException
                    Throw
                Catch ex As Exception
                    ultimoErrore = ex
                End Try
                If riuscito Then Return
                If tentativo < 3 Then Await Task.Delay(1500 * tentativo, ct).ConfigureAwait(False)
            Next
            If ultimoErrore IsNot Nothing Then Throw ultimoErrore
        End Function

        Public Async Function ScaricaFileAsync(file As FilenFile, percorsoDestinazione As String, Optional ct As CancellationToken = Nothing) As Task
            Using destinazione As New FileStream(percorsoDestinazione, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                For indice As Integer = 0 To file.Blocchi - 1
                    ct.ThrowIfCancellationRequested()
                    Dim cifrato As Byte() = Await FilenHttp.ScaricaBloccoAsync(file.Regione, file.Bucket, file.Uuid, indice, _sessione.ApiKey, ct).ConfigureAwait(False)
                    Dim chiaro As Byte() = FilenCrypto.DecifraDati(cifrato, file.Chiave)
                    Await destinazione.WriteAsync(chiaro, 0, chiaro.Length, ct).ConfigureAwait(False)
                Next
            End Using
        End Function

        Public Async Function CestinaFileAsync(uuid As String, Optional ct As CancellationToken = Nothing) As Task
            Await CestinaUuidAsync(uuid, ct).ConfigureAwait(False)
        End Function

        ''' <summary>Sposta nel cestino tutti i file della radice con quel nome (usato per le copie di conflitto già unite).</summary>
        Public Async Function CestinaAsync(nome As String, Optional ct As CancellationToken = Nothing) As Task
            Dim tutti As List(Of FilenFile) = Await ElencaAsync(ct).ConfigureAwait(False)
            For Each file As FilenFile In tutti.Where(Function(f) String.Equals(f.Nome, nome, StringComparison.Ordinal)).ToList()
                Await CestinaUuidAsync(file.Uuid, ct).ConfigureAwait(False)
            Next
        End Function

        Private Async Function CestinaUuidAsync(uuid As String, ct As CancellationToken) As Task
            Dim corpo As New JObject()
            corpo("uuid") = uuid
            Await FilenHttp.PostAsync("/v3/file/trash", corpo, _sessione.ApiKey, ct).ConfigureAwait(False)
        End Function
    End Class

    ''' <summary>Controlli locali (nessuna rete): servono a vedere subito se la crittografia gira sul dispositivo.</summary>
    Public NotInheritable Class FilenAutotest

        Private Sub New()
        End Sub

        Public Shared Function Esegui() As String
            Dim esiti As New List(Of String)
            Try
                Dim credenziali As (ChiaveMaestra As String, PasswordAccesso As String) = FilenCrypto.DerivaCredenziali("password di prova", "sale-di-prova")
                esiti.Add(If(credenziali.ChiaveMaestra.Length = 64 AndAlso credenziali.PasswordAccesso.Length = 128, "derivazione OK", "derivazione ANOMALA"))

                Dim metadati As String = FilenCrypto.CifraMetadati("{""a"":1}", credenziali.ChiaveMaestra)
                esiti.Add(If(FilenCrypto.DecifraMetadati(metadati, credenziali.ChiaveMaestra) = "{""a"":1}", "metadati OK", "metadati ANOMALI"))

                Dim chiaveFile As String = FilenCrypto.StringaCasuale(32)
                Dim originale As Byte() = Encoding.UTF8.GetBytes("{""prova"":""àèì""}")
                Dim cifrato As Byte() = FilenCrypto.CifraDati(originale, chiaveFile)
                Dim decifrato As Byte() = FilenCrypto.DecifraDati(cifrato, chiaveFile)
                esiti.Add(If(decifrato.SequenceEqual(originale), "dati OK", "dati ANOMALI"))
            Catch ex As Exception
                esiti.Add("ERRORE: " & ex.GetType().Name & " " & ex.Message)
            End Try

            Return String.Join("; ", esiti)
        End Function
    End Class

End Namespace