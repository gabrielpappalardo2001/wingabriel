Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb

''' <summary>Un errore del server dei giochi o della connessione, con il testo da dire all'utente. Codice 0: nessuna risposta dal server.</summary>
Public NotInheritable Class GiochiErrore
    Inherits Exception

    Public ReadOnly Property Codice As Integer
    ''' <summary>Vero se il server ha detto che la stessa persona è entrata da un altro apparecchio (e quindi questo non deve rientrare da solo).</summary>
    Public ReadOnly Property Sostituita As Boolean

    Public Sub New(messaggio As String, codice As Integer)
        MyBase.New(messaggio)
        Me.Codice = codice
    End Sub

    Public Sub New(messaggio As String, codice As Integer, sostituita As Boolean)
        MyBase.New(messaggio)
        Me.Codice = codice
        Me.Sostituita = sostituita
    End Sub

    ''' <summary>La prova di calcolo che il server chiede per creare un nome utente (codice 428): {sfida, bit}. Nothing se non c'è.</summary>
    Public ReadOnly Property Prova As JObject

    Public Sub New(messaggio As String, codice As Integer, sostituita As Boolean, prova As JObject)
        MyBase.New(messaggio)
        Me.Codice = codice
        Me.Sostituita = sostituita
        Me.Prova = prova
    End Sub
End Class

''' <summary>Un evento di un flusso (server-sent events): il nome e i dati (JSON su una riga). Il nome "ping" è il segno di vita del server.</summary>
Public NotInheritable Class EventoFlusso
    Public ReadOnly Property Nome As String
    Public ReadOnly Property Dati As String

    Public Sub New(nome As String, dati As String)
        Try
            Me.Nome = nome
            Me.Dati = dati
        Catch ex As Exception
            RegistroErrori.Registra("EventoFlusso.New", ex)
            Throw
        End Try
    End Sub
End Class

''' <summary>
''' Un flusso di eventi (server-sent events) aperto verso il motore d'attesa del server: invece di rifare una richiesta ogni 15-20 secondi,
''' il server scrive quando c'è qualcosa di nuovo e dà segno di vita ogni 15 secondi. Si legge un evento per volta.
''' Se il server non ha i flussi (risponde con un errore invece che con un flusso) ApriAsync solleva GiochiErrore con il codice del server.
''' </summary>
Public NotInheritable Class FlussoEventi
    Implements IDisposable

    Private ReadOnly _richiesta As HttpWebRequest
    Private ReadOnly _risposta As HttpWebResponse
    Private ReadOnly _lettore As StreamReader
    Private ReadOnly _dati As New StringBuilder()
    Private _nome As String = ""

    Private Sub New(richiesta As HttpWebRequest, risposta As HttpWebResponse)
        Try
            _richiesta = richiesta
            _risposta = risposta
            _lettore = New StreamReader(risposta.GetResponseStream(), Encoding.UTF8)
        Catch ex As Exception
            RegistroErrori.Registra("FlussoEventi.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Apre il flusso. corpo: JSON da inviare (metodo POST) oppure Nothing.</summary>
    Public Shared Async Function ApriAsync(url As String, metodo As String, corpo As JObject, token As String, agente As String, ct As CancellationToken) As Task(Of FlussoEventi)
        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            Dim r As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            r.Method = metodo
            r.UserAgent = agente
            r.Accept = "text/event-stream"
            r.KeepAlive = False
            r.Timeout = 20000
            If Not String.IsNullOrEmpty(token) Then r.Headers.Add("X-Token", token)
            Using registrazione As CancellationTokenRegistration = ct.Register(Sub() r.Abort())
                Dim rispostaErrore As HttpWebResponse = Nothing
                Try
                    If corpo IsNot Nothing Then
                        Dim byteCorpo As Byte() = Encoding.UTF8.GetBytes(corpo.ToString(Newtonsoft.Json.Formatting.None))
                        r.ContentType = "application/json; charset=utf-8"
                        r.ContentLength = byteCorpo.Length
                        Using flusso As Stream = Await r.GetRequestStreamAsync().ConfigureAwait(False)
                            Await flusso.WriteAsync(byteCorpo, 0, byteCorpo.Length).ConfigureAwait(False)
                        End Using
                    End If
                    Dim risposta As HttpWebResponse = CType(Await r.GetResponseAsync().ConfigureAwait(False), HttpWebResponse)
                    If risposta.StatusCode = HttpStatusCode.OK AndAlso If(risposta.ContentType, "").IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return New FlussoEventi(r, risposta)
                    End If
                    rispostaErrore = risposta   ' una risposta normale, non un flusso: si legge cosa dice
                Catch ex As WebException
                    If ct.IsCancellationRequested Then Throw New OperationCanceledException(ct)
                    rispostaErrore = TryCast(ex.Response, HttpWebResponse)
                    If rispostaErrore Is Nothing Then Throw New GiochiErrore("Non riesco a collegarmi al server.", 0)
                End Try
                Using rispostaErrore
                    Dim codice As Integer = CInt(rispostaErrore.StatusCode)
                    Dim testo As String = ""
                    Try
                        Using l As New StreamReader(rispostaErrore.GetResponseStream(), Encoding.UTF8)
                            Dim j As JObject = TryCast(JToken.Parse(Await l.ReadToEndAsync().ConfigureAwait(False)), JObject)
                            If j IsNot Nothing Then testo = Convert.ToString(j("errore"))
                        End Using
                    Catch exLog As Exception
                        RegistroErrori.Registra("FlussoEventi.ApriAsync", exLog)
                    End Try
                    If String.IsNullOrWhiteSpace(testo) Then testo = "Il server ha dato l'errore " & codice.ToString() & "."
                    Throw New GiochiErrore(testo, If(codice = 200, 404, codice))
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("FlussoEventi.ApriAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Il prossimo evento. Nothing se il server ha chiuso il flusso (normale: lo chiude ogni tanto, si riapre). Il segno di vita arriva come evento "ping".
    ''' Se per inattivitaMs non arriva niente, nemmeno il segno di vita, il collegamento si considera morto (GiochiErrore, codice 0).
    ''' </summary>
    Public Async Function LeggiAsync(inattivitaMs As Integer, ct As CancellationToken) As Task(Of EventoFlusso)
        Try
            Using registrazione As CancellationTokenRegistration = ct.Register(Sub() _richiesta.Abort())
                Do
                    Dim lettura As Task(Of String) = _lettore.ReadLineAsync()
                    Dim riga As String
                    Using orologio As New CancellationTokenSource()
                        Dim scaduto As Task = Task.Delay(inattivitaMs, orologio.Token)
                        Dim primo As Task = Await Task.WhenAny(lettura, scaduto).ConfigureAwait(False)
                        If primo Is scaduto Then
                            _richiesta.Abort()
                            Throw New GiochiErrore("Il collegamento non risponde.", 0)
                        End If
                        orologio.Cancel()
                    End Using
                    Try
                        riga = Await lettura.ConfigureAwait(False)
                    Catch ex As Exception
                        RegistroErrori.Registra("FlussoEventi.LeggiAsync", ex)
                        If ct.IsCancellationRequested Then Throw New OperationCanceledException(ct)
                        Throw New GiochiErrore("Collegamento interrotto.", 0)
                    End Try
                    If riga Is Nothing Then Return Nothing
                    If riga.Length = 0 Then
                        If _nome.Length > 0 OrElse _dati.Length > 0 Then
                            Dim ev As New EventoFlusso(If(_nome.Length > 0, _nome, "message"), _dati.ToString())
                            _nome = ""
                            _dati.Clear()
                            Return ev
                        End If
                    ElseIf riga(0) = ":"c Then
                        Return New EventoFlusso("ping", "")
                    ElseIf riga.StartsWith("event:", StringComparison.Ordinal) Then
                        _nome = riga.Substring(6).Trim()
                    ElseIf riga.StartsWith("data:", StringComparison.Ordinal) Then
                        If _dati.Length > 0 Then _dati.Append(vbLf)
                        _dati.Append(riga.Substring(5).TrimStart())
                    End If
                Loop
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("FlussoEventi.LeggiAsync", ex)
            Throw
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Try
                _richiesta.Abort()
            Catch exLog As Exception
                RegistroErrori.Registra("FlussoEventi.Dispose", exLog)
            End Try
            Try
                _lettore.Dispose()
            Catch exLog As Exception
                RegistroErrori.Registra("FlussoEventi.Dispose", exLog)
            End Try
            Try
                _risposta.Close()
            Catch exLog As Exception
                RegistroErrori.Registra("FlussoEventi.Dispose", exLog)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("FlussoEventi.Dispose", ex)
            Throw
        End Try
    End Sub
End Class

''' <summary>
''' Il collegamento al server dei giochi (giochi.php): ingresso con nome utente e password, stanze, partite, mosse e chat.
''' Il server è uno solo e non si può cambiare. Il nome utente e il codice di sessione stanno nella configurazione di questo computer.
''' La password, se chi entra sceglie di salvarla, resta solo cifrata (libreria nativa, scopo "giochi"): all'accesso successivo si entra con quell'utente.
''' </summary>
Public NotInheritable Class GiochiClient

    Private Const CHIAVE_SERVER As String = "GiochiServer"
    ' nell'ambiente beta (vedi AmbienteBeta) nome, sessione e password si salvano a parte
    Private ReadOnly CHIAVE_NOME As String
    Private ReadOnly CHIAVE_TOKEN As String
    Private ReadOnly CHIAVE_PASSWORD As String
    Private Const SCOPO_PASSWORD As String = "giochi"

    Private Shared ReadOnly _logger As New Logger()
    ''' <summary>Il collegamento condiviso da giochi e chat: un solo nome utente e una sola sessione.</summary>
    Public Shared ReadOnly Condiviso As New GiochiClient()
    ''' <summary>Il collegamento al motore locale, per giocare senza internet (solo contro il computer): ha un suo utente e una sua sessione, separati da quelli online.</summary>
    Public Shared ReadOnly Locale As New GiochiClient(True)
    ''' <summary>Vero per il collegamento al motore locale (giochi offline).</summary>
    Public ReadOnly ELocale As Boolean

    Public Sub New()
        Me.New(False)
    End Sub

    Private Sub New(locale As Boolean)
        Try
            ELocale = locale
            Dim suffisso As String = If(locale, "Locale", AmbienteBeta.Suffisso)
            CHIAVE_NOME = "GiochiNome" & suffisso
            CHIAVE_TOKEN = "GiochiSessione" & suffisso
            CHIAVE_PASSWORD = "GiochiPassword" & suffisso
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.New", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly Indirizzo As String = AmbienteBeta.ServerGiochi
    Public Property Nome As String = ""
    Public Property Token As String = ""
    ''' <summary>La metà "locale" del derivato della password: serve a proteggere la chat sul computer, non lascia mai la memoria e non si salva.</summary>
    Public Property Master As Byte()

    ' ------------------------------------------------------------------ configurazione
    Private Shared Function Leggi(chiave As String) As String
        Try
            Dim c As New WGConfig()
            If Not c.EsisteId(chiave) Then Return ""
            Return If(c.LeggiValore(chiave), "").Trim()
        Catch ex As Exception
            _logger.ScriviLog("GiochiClient.Leggi (" & chiave & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Dim c As New WGConfig()
            c.SalvaValore(chiave, If(valore, ""))
        Catch ex As Exception
            _logger.ScriviLog("GiochiClient.Scrivi (" & chiave & "): " & ex.Message)
        End Try
    End Sub

    Public Sub Carica()
        Try
            ' Una versione precedente permetteva di scegliere il server: ora è uno solo e quello che era stato salvato si toglie.
            If Leggi(CHIAVE_SERVER).Length > 0 Then Scrivi(CHIAVE_SERVER, "")
            Nome = Leggi(CHIAVE_NOME)
            Token = Leggi(CHIAVE_TOKEN)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.Carica", ex)
            Throw
        End Try
    End Sub

    Public Sub ImpostaNome(valore As String)
        Try
            Nome = If(valore, "").Trim()
            Scrivi(CHIAVE_NOME, Nome)
            Token = ""
            Scrivi(CHIAVE_TOKEN, "")
            DimenticaPassword()
            Master = Nothing
            _authMemoria = ""
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ImpostaNome", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Salva la password solo cifrata. Se la cifratura non riesce non si scrive niente, nemmeno in chiaro.</summary>
    Public Sub SalvaPassword(password As String)
        Try
            If String.IsNullOrEmpty(password) Then
                DimenticaPassword()
                Return
            End If
            Dim protetta As String = WGCifraturaNativa.ProteggiTesto(SCOPO_PASSWORD, password)
            If protetta = "" Then
                _logger.ScriviLog("GiochiClient.SalvaPassword: cifratura non riuscita, la password non viene salvata.")
                DimenticaPassword()
                Return
            End If
            Scrivi(CHIAVE_PASSWORD, protetta)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.SalvaPassword", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La password salvata, solo se è cifrata. Stringa vuota se manca o non si decifra.</summary>
    Public Function PasswordSalvata() As String
        Try
            Dim valore As String = Leggi(CHIAVE_PASSWORD)
            If valore = "" Then Return ""
            If Not WGCifraturaNativa.EProtetto(valore) Then
                DimenticaPassword()
                Return ""
            End If
            Return If(WGCifraturaNativa.RivelaTesto(SCOPO_PASSWORD, valore), "")
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.PasswordSalvata", ex)
            Throw
        End Try
    End Function

    Public Sub DimenticaPassword()
        Try
            Scrivi(CHIAVE_PASSWORD, "")
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.DimenticaPassword", ex)
            Throw
        End Try
    End Sub

    ' ---- codice della chat, ricerca per nome utente, blocchi
    ''' <summary>Rende il proprio codice della chat trovabile per nome utente.</summary>
    Public Function PubblicaChatAsync(codice As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "chat_pubblica", Corpo("codice", codice), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.PubblicaChatAsync", ex)
            Throw
        End Try
    End Function

    Public Function RitiraChatAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "chat_ritira", New JObject(), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.RitiraChatAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il codice della chat di una persona (GiochiErrore 404 se non si trova o non è contattabile).</summary>
    Public Function CercaAsync(nome As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "cerca", Corpo("nome", nome), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.CercaAsync", ex)
            Throw
        End Try
    End Function

    Public Function BloccaAsync(nome As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "blocca", Corpo("nome", nome), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.BloccaAsync", ex)
            Throw
        End Try
    End Function

    Public Function SbloccaAsync(nome As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "sblocca", Corpo("nome", nome), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.SbloccaAsync", ex)
            Throw
        End Try
    End Function

    Public Function BloccatiAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "bloccati", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.BloccatiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Nomi utente collegati adesso (visti negli ultimi due minuti), senza se stessi e senza i bloccati.</summary>
    Public Function InLineaAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "in_linea", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.InLineaAsync", ex)
            Throw
        End Try
    End Function

    Public Sub DimenticaSessione()
        Try
            Token = ""
            Scrivi(CHIAVE_TOKEN, "")
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.DimenticaSessione", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ richieste
    ''' <summary>
    ''' Chiamato quando il collegamento cade (False) e quando torna (True) durante una richiesta che si sta ripetendo, una volta sola per ogni caduta.
    ''' Chi vuole avvisare a voce lo imposta (e lo toglie quando ha finito). Arriva sul thread che ha fatto la richiesta.
    ''' </summary>
    Public Property AvvisoConnessione As Action(Of Boolean)

    Private Const FINESTRA_PROVE_BREVE_S As Integer = 25      ' elenchi e domande: non si resta ad aspettare a lungo
    Private Const FINESTRA_PROVE_PARTITA_S As Integer = 180   ' lo stato di una partita: una caduta di rete non fa uscire dalla partita
    Private Const FINESTRA_PROVE_MOSSA_S As Integer = 60
    Private Shared ReadOnly _casuale As New Random()
    Private _senzaConnessione As Integer   ' 1 mentre si sa che la rete è caduta (si avvisa una volta sola anche con più richieste insieme)

    ''' <summary>La versione di WinGabriel che sta girando (se non si conosce, quella di questa libreria).</summary>
    Public Shared ReadOnly Property VersioneProgramma As Version
        Get
            Try
                Dim a As System.Reflection.Assembly = System.Reflection.Assembly.GetEntryAssembly()
                If a IsNot Nothing Then Return a.GetName().Version
            Catch exLog As Exception
                RegistroErrori.Registra("GiochiClient.DimenticaSessione", exLog)
            End Try
            Return GetType(GiochiClient).Assembly.GetName().Version
        End Get
    End Property

    Private Shared ReadOnly AgenteUtente As String = "WinGabriel/" & VersioneProgramma.ToString()

    Private Shared Function Riprovabile(ex As GiochiErrore) As Boolean
        Try
            Return ex.Codice = 0 OrElse ex.Codice = 502 OrElse ex.Codice = 503 OrElse ex.Codice = 504
        Catch exErr As Exception
            RegistroErrori.Registra("GiochiClient.Riprovabile", exErr)
            Throw
        End Try
    End Function

    Private Sub Avvisa(contesto As SynchronizationContext, connesso As Boolean)
        Try
            Dim a As Action(Of Boolean) = AvvisoConnessione
            If a Is Nothing Then Return
            Try
                If contesto IsNot Nothing Then
                    contesto.Post(Sub(s As Object) a(connesso), Nothing)
                Else
                    a(connesso)
                End If
            Catch ex As Exception
                _logger.ScriviLog("GiochiClient.Avvisa: " & ex.Message)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.Avvisa", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Una richiesta al server. Le richieste che si possono ripetere senza danni (di lettura, e le mosse che portano un numero proprio)
    ''' se la rete cade o il server non risponde si ripetono da sole, con pause sempre più lunghe, finché passa la finestra di tempo:
    ''' solo allora l'errore arriva a chi ha chiesto. Le altre (accesso, creazione...) partono una volta sola.
    ''' </summary>
    Private Async Function ChiamaConProveAsync(metodo As String, azione As String, corpo As JObject, query As String, durataMassimaMs As Integer, ct As CancellationToken, ripetibile As Boolean?, finestraSecondi As Integer) As Task(Of JObject)
        Try
            Dim ripeti As Boolean = If(ripetibile.HasValue, ripetibile.Value, metodo = "GET")
            If ELocale OrElse Not ripeti Then Return Await ChiamaUnaVoltaAsync(metodo, azione, corpo, query, durataMassimaMs, ct, False).ConfigureAwait(False)
            Dim contesto As SynchronizationContext = SynchronizationContext.Current
            Dim inizio As DateTime = DateTime.UtcNow
            Dim tentativo As Integer = 0
            Dim avvisato As Boolean = False
            Do
                Dim fallito As GiochiErrore = Nothing
                Try
                    ' le richieste che si ripetono tengono aperta la connessione: niente nuova stretta di mano TLS a ogni attesa
                    ' (se la connessione si è chiusa nel frattempo la richiesta si ripete da sola)
                    Dim r As JObject = Await ChiamaUnaVoltaAsync(metodo, azione, corpo, query, durataMassimaMs, ct, True).ConfigureAwait(False)
                    If avvisato AndAlso Interlocked.Exchange(_senzaConnessione, 0) = 1 Then Avvisa(contesto, True)
                    Return r
                Catch ex As GiochiErrore When Riprovabile(ex)
                    fallito = ex
                End Try
                If ct.IsCancellationRequested Then Throw New OperationCanceledException(ct)
                If (DateTime.UtcNow - inizio).TotalSeconds >= finestraSecondi Then
                    If avvisato Then Interlocked.Exchange(_senzaConnessione, 0)
                    Throw fallito
                End If
                tentativo += 1
                If Not avvisato AndAlso tentativo >= 2 Then
                    avvisato = True
                    If Interlocked.Exchange(_senzaConnessione, 1) = 0 Then Avvisa(contesto, False)
                End If
                Dim pausa As Integer = Math.Min(15000, 1000 * (1 << Math.Min(tentativo - 1, 4))) + _casuale.Next(0, 500)
                Await Task.Delay(pausa, ct).ConfigureAwait(False)
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ChiamaConProveAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Una richiesta al server (con le ripetizioni descritte sopra). Se la sessione è scaduta (401) e questo programma conosce la password
    ''' (in memoria da quando si è entrati, o salvata cifrata), rientra da solo e rifà la richiesta: il server non l'aveva eseguita.
    ''' Non lo fa se il server dice che la stessa persona è entrata da un altro apparecchio: i due non devono contendersi la sessione.
    ''' </summary>
    Private Async Function ChiamaAsync(metodo As String, azione As String, corpo As JObject, query As String, durataMassimaMs As Integer, ct As CancellationToken, Optional ripetibile As Boolean? = Nothing, Optional finestraSecondi As Integer = FINESTRA_PROVE_BREVE_S) As Task(Of JObject)
        Try
            Dim tokenUsato As String = Token
            Dim fallito As GiochiErrore = Nothing
            Try
                Return Await ChiamaConProveAsync(metodo, azione, corpo, query, durataMassimaMs, ct, ripetibile, finestraSecondi).ConfigureAwait(False)
            Catch ex As GiochiErrore When ex.Codice = 401 AndAlso Not ex.Sostituita AndAlso Not ELocale AndAlso tokenUsato.Length > 0 AndAlso azione <> "entra" AndAlso azione <> "registra" AndAlso azione <> "elimina_account"
                fallito = ex
            End Try
            If Not Await RientraAsync(tokenUsato).ConfigureAwait(False) Then Throw fallito
            Return Await ChiamaConProveAsync(metodo, azione, corpo, query, durataMassimaMs, ct, ripetibile, finestraSecondi).ConfigureAwait(False)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ChiamaAsync", ex)
            Throw
        End Try
    End Function

    Private ReadOnly _rientro As New SemaphoreSlim(1, 1)
    ''' <summary>Il derivato della password, solo in memoria e solo da quando si è entrati in questo programma: serve a rientrare da soli.</summary>
    Private _authMemoria As String = ""

    ''' <summary>Rientra con il nome utente e la password (in memoria o salvata). Vero se ha una sessione valida (anche se l'ha già rifatta un'altra richiesta).</summary>
    Private Async Function RientraAsync(tokenFallito As String) As Task(Of Boolean)
        Try
            Await _rientro.WaitAsync().ConfigureAwait(False)
            Try
                If Token.Length > 0 AndAlso Token <> tokenFallito Then Return True
                If Nome.Length = 0 Then Return False
                Dim auth As String = _authMemoria
                Dim master As Byte() = Nothing
                If String.IsNullOrEmpty(auth) Then
                    Dim password As String = PasswordSalvata()
                    If password.Length = 0 Then Return False
                    auth = Deriva(Nome, password, master)
                End If
                Dim r As JObject
                Try
                    r = Await ChiamaUnaVoltaAsync("POST", "entra", Corpo("nome", Nome, "auth", auth), "", DURATA_NORMALE_MS, CancellationToken.None, False).ConfigureAwait(False)
                Catch ex As GiochiErrore
                    Return False
                End Try
                If Me.Master Is Nothing AndAlso master IsNot Nothing Then Me.Master = master
                _authMemoria = auth
                Token = Convert.ToString(r("token"))
                Scrivi(CHIAVE_TOKEN, Token)
                Return True
            Finally
                _rientro.Release()
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.RientraAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function ChiamaUnaVoltaAsync(metodo As String, azione As String, corpo As JObject, query As String, durataMassimaMs As Integer, ct As CancellationToken, Optional tieniAperta As Boolean = False) As Task(Of JObject)
        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            ' senza internet la richiesta non esce: risponde il motore dei giochi che sta dentro WinGabriel
            If ELocale Then Return Await Task.Run(Function() MotoreLocale.Chiama(metodo, azione, corpo, query, Token)).ConfigureAwait(False)
            Dim url As String = Indirizzo & If(Indirizzo.Contains("?"), "&", "?") & "a=" & azione & If(String.IsNullOrEmpty(query), "", "&" & query)
            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            richiesta.Method = metodo
            richiesta.UserAgent = AgenteUtente
            richiesta.Accept = "application/json"
            richiesta.KeepAlive = tieniAperta
            richiesta.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
            If Token.Length > 0 Then richiesta.Headers.Add("X-Token", Token)

            Using limite As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct)
                limite.CancelAfter(durataMassimaMs)
                Using registrazione As CancellationTokenRegistration = limite.Token.Register(Sub() richiesta.Abort())
                    Dim rispostaErrore As HttpWebResponse = Nothing
                    Try
                        If corpo IsNot Nothing Then
                            Dim byteCorpo As Byte() = Encoding.UTF8.GetBytes(corpo.ToString(Newtonsoft.Json.Formatting.None))
                            richiesta.ContentType = "application/json; charset=utf-8"
                            richiesta.ContentLength = byteCorpo.Length
                            Using flusso As Stream = Await richiesta.GetRequestStreamAsync().ConfigureAwait(False)
                                Await flusso.WriteAsync(byteCorpo, 0, byteCorpo.Length).ConfigureAwait(False)
                            End Using
                        End If
                        Using risposta As HttpWebResponse = CType(Await richiesta.GetResponseAsync().ConfigureAwait(False), HttpWebResponse)
                            Return Await LeggiAsync(risposta).ConfigureAwait(False)
                        End Using
                    Catch ex As WebException
                        If ct.IsCancellationRequested Then Throw New OperationCanceledException(ct)
                        rispostaErrore = TryCast(ex.Response, HttpWebResponse)
                        If rispostaErrore Is Nothing Then
                            Throw New GiochiErrore("Non riesco a collegarmi al server dei giochi. Controlla la connessione a internet.", 0)
                        End If
                    End Try

                    ' il server ha risposto con un errore: si legge il motivo
                    Using rispostaErrore
                        Dim dati As JObject = Nothing
                        Try
                            dati = Await LeggiAsync(rispostaErrore).ConfigureAwait(False)
                        Catch exLog As Exception
                            RegistroErrori.Registra("GiochiClient.ChiamaUnaVoltaAsync", exLog)
                        End Try
                        Dim testo As String = If(dati IsNot Nothing, Convert.ToString(dati("errore")), "")
                        If String.IsNullOrWhiteSpace(testo) Then testo = "Il server dei giochi ha dato l'errore " & CInt(rispostaErrore.StatusCode).ToString() & "."
                        Dim sostituita As Boolean = dati IsNot Nothing AndAlso dati("sostituita") IsNot Nothing AndAlso dati("sostituita").Type = JTokenType.Boolean AndAlso CBool(dati("sostituita"))
                        Dim prova As JObject = If(dati IsNot Nothing, TryCast(dati("prova"), JObject), Nothing)
                        Throw New GiochiErrore(testo, CInt(rispostaErrore.StatusCode), sostituita, prova)
                    End Using
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ChiamaUnaVoltaAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Async Function LeggiAsync(risposta As HttpWebResponse) As Task(Of JObject)
        Try
            Using flusso As Stream = risposta.GetResponseStream()
                Using lettore As New StreamReader(flusso, Encoding.UTF8)
                    Dim testo As String = Await lettore.ReadToEndAsync().ConfigureAwait(False)
                    Dim token As JToken = JToken.Parse(testo)
                    Dim elenco As JArray = TryCast(token, JArray)
                    If elenco IsNot Nothing AndAlso elenco.Count = 0 Then Return New JObject()
                    Dim oggetto As JObject = TryCast(token, JObject)
                    If oggetto Is Nothing Then Throw New GiochiErrore("Risposta del server dei giochi non valida.", 0)
                    Return oggetto
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.LeggiAsync", ex)
            Throw
        End Try
    End Function

    Private Function Corpo(ParamArray coppie As Object()) As JObject
        Try
            Dim o As New JObject()
            For i As Integer = 0 To coppie.Length - 2 Step 2
                o(Convert.ToString(coppie(i))) = JToken.FromObject(coppie(i + 1))
            Next
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.Corpo", ex)
            Throw
        End Try
    End Function

    Private Const DURATA_NORMALE_MS As Integer = 20000

    ''' <summary>Entra con un nome utente e la password. Con nome o password sbagliati solleva GiochiErrore (codice 401; 429 se è bloccato per troppi errori). Se il nome non ha ancora una password, scriverla gliela assegna. Senza password non si entra.</summary>
    Public Function EntraAsync(nome As String, password As String) As Task
        Try
            Return AccessoAsync("entra", nome, password)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.EntraAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Crea un nome utente nuovo e ci entra. Se esiste già o i dati non vanno bene solleva GiochiErrore (codice 409 o 400).</summary>
    Public Function RegistraAsync(nome As String, password As String) As Task
        Try
            Return AccessoAsync("registra", nome, password)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.RegistraAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' La password non parte mai: al server va un derivato (PBKDF2-SHA256, 600000 giri, sale "WGAccount-v1|nome in minuscolo", 64 byte).
    ''' I primi 32 byte, in esadecimale, sono l'auth per il server; gli altri 32 restano qui e proteggono la chat. Stessa regola del browser.
    ''' </summary>
    Public Shared Function Deriva(nome As String, password As String, ByRef master As Byte()) As String
        Try
            Dim sale As Byte() = Encoding.UTF8.GetBytes("WGAccount-v1|" & nome.ToLowerInvariant())
            Dim r As Byte() = WGCifraturaNativa.DerivaPbkdf2(password, sale, 600000, 64)
            Dim auth As New StringBuilder(64)
            For i As Integer = 0 To 31
                auth.Append(r(i).ToString("x2"))
            Next
            master = New Byte(31) {}
            Buffer.BlockCopy(r, 32, master, 0, 32)
            Return auth.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.Deriva", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' La prova di calcolo per creare un nome utente: cerca un numero (in esadecimale minuscolo) tale che SHA-256 di "sfida|nome in minuscolo|numero"
    ''' abbia almeno "bit" bit a zero all'inizio. Pochi secondi di calcolo per chi crea un nome, troppi per chi ne crea migliaia.
    ''' La usa anche la chat (WGChat) per aprire cassette nuove quando il relè lo chiede: lì al posto del nome c'è l'impronta delle cassette.
    ''' </summary>
    Public Shared Function RisolviProva(sfida As String, nome As String, bit As Integer) As String
        Try
            Dim prefisso As Byte() = Encoding.UTF8.GetBytes(sfida & "|" & nome.ToLowerInvariant() & "|")
            Dim blocco(prefisso.Length + 31) As Byte
            Buffer.BlockCopy(prefisso, 0, blocco, 0, prefisso.Length)
            Using sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Dim contatore As ULong = 0UL
                Do
                    Dim esa As String = contatore.ToString("x")
                    For i As Integer = 0 To esa.Length - 1
                        blocco(prefisso.Length + i) = CByte(AscW(esa(i)))
                    Next
                    Dim digest As Byte() = sha.ComputeHash(blocco, 0, prefisso.Length + esa.Length)
                    If ZeriIniziali(digest) >= bit Then Return esa
                    contatore += 1UL
                Loop
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.RisolviProva", ex)
            Throw
        End Try
    End Function

    Private Shared Function ZeriIniziali(digest As Byte()) As Integer
        Try
            Dim n As Integer = 0
            For Each b As Byte In digest
                If b = 0 Then
                    n += 8
                Else
                    Dim x As Integer = b
                    While (x And &H80) = 0
                        n += 1
                        x <<= 1
                    End While
                    Exit For
                End If
            Next
            Return n
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ZeriIniziali", ex)
            Throw
        End Try
    End Function

    Private Async Function AccessoAsync(azione As String, nome As String, password As String) As Task
        Try
            If String.IsNullOrEmpty(password) Then Throw New GiochiErrore("Scrivi la password.", 401)
            Dim master As Byte() = Nothing
            Dim auth As String = Deriva(nome, password, master)
            Dim corpoRichiesta As JObject = Corpo("nome", nome, "auth", auth)
            Dim r As JObject = Nothing
            Dim vecchioNome As Boolean = False
            Dim provaChiesta As JObject = Nothing
            Try
                r = Await ChiamaAsync("POST", azione, corpoRichiesta, "", DURATA_NORMALE_MS, CancellationToken.None).ConfigureAwait(False)
            Catch ex As GiochiErrore When azione = "entra" AndAlso ex.Codice = 401
                vecchioNome = True
            Catch ex As GiochiErrore When azione = "registra" AndAlso ex.Codice = 428 AndAlso ex.Prova IsNot Nothing
                provaChiesta = ex.Prova
            End Try
            ' il server chiede una piccola prova di calcolo (succede solo quando in un'ora si creano moltissimi nomi utente): si fa e si rifà la richiesta
            If provaChiesta IsNot Nothing Then
                Dim sfida As String = Convert.ToString(provaChiesta("sfida"))
                Dim bit As Integer = CInt(provaChiesta("bit"))
                If String.IsNullOrEmpty(sfida) OrElse bit < 1 OrElse bit > 26 Then Throw New GiochiErrore("Il server ha chiesto una verifica che questo programma non sa fare: riprova più tardi.", 428)
                Dim nonce As String = Await Task.Run(Function() RisolviProva(sfida, nome, bit)).ConfigureAwait(False)
                corpoRichiesta("prova") = New JObject From {{"sfida", sfida}, {"nonce", nonce}}
                r = Await ChiamaAsync("POST", azione, corpoRichiesta, "", DURATA_NORMALE_MS, CancellationToken.None).ConfigureAwait(False)
            End If
            ' un nome utente creato prima del derivato: la vecchia password vale una volta sola, poi il server lo converte
            If vecchioNome Then r = Await ChiamaAsync("POST", "entra", Corpo("nome", nome, "password", password), "", DURATA_NORMALE_MS, CancellationToken.None).ConfigureAwait(False)
            Me.Master = master
            _authMemoria = auth
            Token = Convert.ToString(r("token"))
            Me.Nome = Convert.ToString(r("nome"))
            Scrivi(CHIAVE_NOME, Me.Nome)
            Scrivi(CHIAVE_TOKEN, Token)
            ' La password non si salva qui: chi ha chiesto l'accesso decide se salvarla, e solo dopo che l'ingresso è riuscito.
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.AccessoAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cancella il nome utente dal server (e le sue partite). Serve la password.</summary>
    Public Async Function EliminaAccountAsync(password As String) As Task
        Try
            Dim master As Byte() = Nothing
            Dim auth As String = Deriva(Nome, password, master)
            Await ChiamaAsync("POST", "elimina_account", Corpo("auth", auth), "", DURATA_NORMALE_MS, CancellationToken.None).ConfigureAwait(False)
            Me.Master = Nothing
            _authMemoria = ""
            Nome = ""
            Scrivi(CHIAVE_NOME, "")
            DimenticaPassword()
            DimenticaSessione()
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.EliminaAccountAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Per i giochi senza internet: entra nel motore locale con un utente che si crea da solo (il nome dell'utente di Windows),
    ''' senza chiedere niente. Il motore risponde solo a questo computer.
    ''' </summary>
    Public Async Function AccessoLocaleAsync() As Task
        Try
            Dim nome As String = NomeLocale()
            Const PASSWORD_LOCALE As String = "WinGabriel-giochi-locali-2026"
            Dim nuovo As Boolean = False
            Try
                Await EntraAsync(nome, PASSWORD_LOCALE).ConfigureAwait(False)
            Catch ex As GiochiErrore When ex.Codice = 401
                nuovo = True
            End Try
            If nuovo Then Await RegistraAsync(nome, PASSWORD_LOCALE).ConfigureAwait(False)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.AccessoLocaleAsync", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeLocale() As String
        Try
            Dim sb As New StringBuilder()
            For Each ch As Char In Environment.UserName
                If Char.IsLetterOrDigit(ch) OrElse ch = " "c OrElse ch = "."c OrElse ch = "_"c OrElse ch = "-"c Then sb.Append(ch)
            Next
            Dim n As String = sb.ToString().Trim()
            If n.Length > 20 Then n = n.Substring(0, 20).Trim()
            If n.Length < 2 Then n = "Giocatore"
            Return n
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.NomeLocale", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vero se il codice di sessione conservato vale ancora.</summary>
    Public Async Function SessioneValidaAsync() As Task(Of Boolean)
        Try
            If Token.Length = 0 Then Return False
            Try
                Await ChiamaAsync("GET", "mia", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None).ConfigureAwait(False)
                Return True
            Catch ex As GiochiErrore
                If ex.Codice = 401 Then
                    DimenticaSessione()
                    Return False
                End If
                Throw
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.SessioneValidaAsync", ex)
            Throw
        End Try
    End Function

    Public Function GiochiAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "giochi", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.GiochiAsync", ex)
            Throw
        End Try
    End Function

    Public Function StanzeAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "stanze", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.StanzeAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>I tavoli in attesa. Con un gioco, solo quelli di quel gioco. Ogni voce può avere invitato=true.</summary>
    Public Function TavoliAsync(Optional gioco As String = "") As Task(Of JObject)
        Try
            Dim query As String = If(String.IsNullOrEmpty(gioco), "", "gioco=" & Uri.EscapeDataString(gioco))
            Return ChiamaAsync("GET", "tavoli", Nothing, query, DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.TavoliAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>I tavoli a cui sei stato invitato e che hanno ancora un posto libero.</summary>
    Public Function InvitiAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "inviti", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.InvitiAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Invita un giocatore, per nome utente, al tavolo in cui sei seduto.</summary>
    Public Function InvitaAsync(id As String, nome As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "invita", Corpo("id", id, "nome", nome), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.InvitaAsync", ex)
            Throw
        End Try
    End Function

    Public Function RifiutaInvitoAsync(id As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "rifiuta_invito", Corpo("id", id), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.RifiutaInvitoAsync", ex)
            Throw
        End Try
    End Function

    Public Function MiaAsync() As Task(Of JObject)
        Try
            Return ChiamaAsync("GET", "mia", Nothing, "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.MiaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il link web della partita: chi lo apre entra nella stessa stanza dal browser, vedente o no.</summary>
    Public Shared Function LinkPartita(id As String) As String
        Try
            Return AmbienteBeta.SitoGiochi & "?p=" & Uri.EscapeDataString(If(id, ""))
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.LinkPartita", ex)
            Throw
        End Try
    End Function

    ''' <summary>Crea un tavolo. variante è l'id di una variante del gioco (per esempio "scopone", "marianna", "completo"); "classica" è quella di base.</summary>
    Public Function CreaAsync(gioco As String, posti As Integer, computer As Boolean, Optional punti As Integer = 0, Optional variante As String = "classica", Optional livello As String = "normale") As Task(Of JObject)
        Try
            Dim v As String = If(String.IsNullOrWhiteSpace(variante), "classica", variante.Trim())
            Dim l As String = If(String.IsNullOrWhiteSpace(livello), "normale", livello.Trim())
            Return ChiamaAsync("POST", "crea", Corpo("gioco", gioco, "posti", posti, "computer", computer, "punti", punti, "variante", v, "livello", l), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.CreaAsync", ex)
            Throw
        End Try
    End Function

    Public Function EntraStanzaAsync(id As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "entra_stanza", Corpo("id", id), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.EntraStanzaAsync", ex)
            Throw
        End Try
    End Function

    Public Function AvviaAsync(id As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "avvia", Corpo("id", id), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.AvviaAsync", ex)
            Throw
        End Try
    End Function

    Public Function LasciaAsync(id As String) As Task(Of JObject)
        Try
            ChiudiFlusso(id)
            Return ChiamaAsync("POST", "lascia", Corpo("id", id), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.LasciaAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Lo stato della partita. Con attesa il server risponde appena c'è qualcosa di nuovo o dopo al massimo quei secondi.</summary>
    Public Async Function StatoAsync(id As String, da As Integer, attesaSecondi As Integer, conSituazione As Boolean, ct As CancellationToken) As Task(Of JObject)
        Try
            If ELocale AndAlso attesaSecondi > 0 Then
                ' il motore locale serve una richiesta alla volta: niente attese lunghe sul server, si aspetta qui e si richiede spesso
                Await Task.Delay(300, ct).ConfigureAwait(False)
                attesaSecondi = 0
            End If
            If attesaSecondi > 0 AndAlso Not ELocale AndAlso Not conSituazione AndAlso DateTime.UtcNow >= _flussiSospesiFino Then
                ' invece di una richiesta lunga si ascolta il flusso di eventi: il server scrive appena c'è qualcosa
                Dim flusso As FlussoPartita = OttieniFlusso(id, da)
                Dim novita As JObject = Await flusso.AttendiAsync(da, attesaSecondi, ct).ConfigureAwait(False)
                If novita IsNot Nothing Then Return novita
                Dim guasto As GiochiErrore = flusso.Errore
                If guasto Is Nothing Then
                    attesaSecondi = 0   ' niente di nuovo nel tempo dato: si rilegge lo stato com'è
                ElseIf guasto.Codice = 0 Then
                    Throw guasto        ' la rete non c'è: dopo le prove fatte dal flusso l'errore arriva a chi gioca
                End If
                ' altrimenti il server non ha i flussi: si ripiega sull'attesa normale qui sotto
            End If
            Dim query As String = "id=" & Uri.EscapeDataString(id) & "&da=" & da.ToString() & "&attesa=" & attesaSecondi.ToString() & If(conSituazione, "&situazione=1", "")
            Return Await ChiamaAsync("GET", "stato", Nothing, query, (attesaSecondi + 15) * 1000, ct, True, FINESTRA_PROVE_PARTITA_S).ConfigureAwait(False)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.StatoAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Gioca una mossa. Porta un numero proprio ("mid"): se la risposta si perde e la richiesta si ripete, il server non rifà la mossa
    ''' ma risponde con lo stato com'è adesso.
    ''' </summary>
    Public Function MossaAsync(id As String, mossa As String, da As Integer) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "mossa", Corpo("id", id, "mossa", mossa, "da", da, "mid", Guid.NewGuid().ToString("N")), "", DURATA_NORMALE_MS, CancellationToken.None, True, FINESTRA_PROVE_MOSSA_S)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.MossaAsync", ex)
            Throw
        End Try
    End Function

    Public Function ChatAsync(id As String, testo As String) As Task(Of JObject)
        Try
            Return ChiamaAsync("POST", "chat", Corpo("id", id, "testo", testo), "", DURATA_NORMALE_MS, CancellationToken.None)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ChatAsync", ex)
            Throw
        End Try
    End Function


    ' ------------------------------------------------------------------ flusso di eventi della partita
    Private Shared _flussiSospesiFino As DateTime = DateTime.MinValue
    Private ReadOnly _flussi As New Dictionary(Of String, FlussoPartita)()

    ''' <summary>Il server non ha i flussi di eventi (o non vanno): per un po' si usano solo le attese normali.</summary>
    Private Shared Sub SospendiFlussi(minuti As Integer)
        Try
            _flussiSospesiFino = DateTime.UtcNow.AddMinutes(minuti)
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.SospendiFlussi", ex)
            Throw
        End Try
    End Sub

    Private Function OttieniFlusso(id As String, da As Integer) As FlussoPartita
        Try
            SyncLock _flussi
                Dim f As FlussoPartita = Nothing
                If _flussi.TryGetValue(id, f) AndAlso f.Attivo Then Return f
                For Each chiave As String In _flussi.Where(Function(x) Not x.Value.Attivo).Select(Function(x) x.Key).ToList()
                    _flussi.Remove(chiave)
                Next
                f = New FlussoPartita(Me, id, da)
                _flussi(id) = f
                Return f
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.OttieniFlusso", ex)
            Throw
        End Try
    End Function

    Private Sub ChiudiFlusso(id As String)
        Try
            SyncLock _flussi
                Dim f As FlussoPartita = Nothing
                If _flussi.TryGetValue(id, f) Then
                    f.Ferma()
                    _flussi.Remove(id)
                End If
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("GiochiClient.ChiudiFlusso", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Il flusso di eventi di una partita: tiene aperta una connessione con il server, che scrive quando c'è qualcosa di nuovo, e conserva
    ''' l'ultimo stato e gli eventi arrivati. Chi aspetta (AttendiAsync) riceve lo stato appena ci sono eventi più nuovi di quelli che conosce.
    ''' Si riapre da solo se la rete cade (e avvisa una volta sola); si ferma da solo se nessuno lo usa per un minuto e mezzo.
    ''' Se il server non ha i flussi, Errore dice perché e chi chiama ripiega sulle attese normali.
    ''' </summary>
    Private NotInheritable Class FlussoPartita
        Private ReadOnly _client As GiochiClient
        Private ReadOnly _id As String
        Private ReadOnly _contesto As SynchronizationContext
        Private ReadOnly _blocco As New Object()
        Private ReadOnly _eventi As New SortedDictionary(Of Integer, JObject)()
        Private ReadOnly _cts As New CancellationTokenSource()
        Private _ultimoStato As JObject
        Private _daServer As Integer          ' l'ultimo evento visto dal server (serve per riaprire il flusso)
        Private _maxVisibile As Integer       ' l'ultimo evento che questo giocatore può vedere
        Private _cambio As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Private _errore As GiochiErrore
        Private _fermato As Boolean
        Private _ultimoUso As DateTime = DateTime.UtcNow

        Public Sub New(client As GiochiClient, id As String, da As Integer)
            Try
                _client = client
                _id = id
                _daServer = da
                _maxVisibile = da
                _contesto = SynchronizationContext.Current
                Dim avvio As Task = Task.Run(Function() CicloAsync())
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.New", ex)
                Throw
            End Try
        End Sub

        Public ReadOnly Property Attivo As Boolean
            Get
                Return Not _fermato AndAlso _errore Is Nothing
            End Get
        End Property

        Public ReadOnly Property Errore As GiochiErrore
            Get
                Return _errore
            End Get
        End Property

        Public Sub Ferma()
            Try
                _fermato = True
                Try
                    _cts.Cancel()
                Catch exLog As Exception
                    RegistroErrori.Registra("GiochiClient.FlussoPartita.Ferma", exLog)
                End Try
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.Ferma", ex)
                Throw
            End Try
        End Sub

        Private Sub ImpostaErrore(e As GiochiErrore)
            Try
                SyncLock _blocco
                    _errore = e
                    _cambio.TrySetResult(True)
                End SyncLock
                ' il server non ha i flussi o sono pieni: per un po' non si riprova (una richiesta 401 o 403 invece è un fatto di questa partita)
                If e.Codice = 400 OrElse e.Codice = 404 OrElse e.Codice >= 500 Then SospendiFlussi(5)
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.ImpostaErrore", ex)
                Throw
            End Try
        End Sub

        Private Sub Registra(stato As JObject)
            Try
                SyncLock _blocco
                    _ultimoStato = stato
                    Dim elenco As JArray = TryCast(stato("eventi"), JArray)
                    If elenco IsNot Nothing Then
                        For Each e As JObject In elenco.OfType(Of JObject)()
                            Dim n As Integer = e.Value(Of Integer)("n")
                            _eventi(n) = e
                            If n > _maxVisibile Then _maxVisibile = n
                        Next
                    End If
                    Dim u As Integer? = stato.Value(Of Integer?)("ultimo_evento")
                    If u.HasValue AndAlso u.Value > _daServer Then _daServer = u.Value
                    While _eventi.Count > 400
                        _eventi.Remove(_eventi.Keys.First())
                    End While
                    Dim vecchio As TaskCompletionSource(Of Boolean) = _cambio
                    _cambio = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                    vecchio.TrySetResult(True)
                End SyncLock
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.Registra", ex)
                Throw
            End Try
        End Sub

        Private Function DaCorrente() As Integer
            Try
                SyncLock _blocco
                    Return _daServer
                End SyncLock
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.DaCorrente", ex)
                Throw
            End Try
        End Function

        Private Async Function CicloAsync() As Task
            Try
                Dim tentativo As Integer = 0
                Dim inizioCaduta As DateTime = DateTime.UtcNow
                Dim avvisato As Boolean = False
                Try
                    Do While Not _fermato
                        If (DateTime.UtcNow - _ultimoUso).TotalSeconds > 90 Then Exit Do   ' nessuno lo usa più
                        Dim guasto As GiochiErrore = Nothing
                        Try
                            Dim url As String = _client.Indirizzo & If(_client.Indirizzo.Contains("?"), "&", "?") & "a=flusso&id=" & Uri.EscapeDataString(_id) & "&da=" & DaCorrente().ToString()
                            Using f As FlussoEventi = Await FlussoEventi.ApriAsync(url, "GET", Nothing, _client.Token, AgenteUtente, _cts.Token).ConfigureAwait(False)
                                If avvisato AndAlso Interlocked.Exchange(_client._senzaConnessione, 0) = 1 Then _client.Avvisa(_contesto, True)
                                avvisato = False
                                tentativo = 0
                                Do
                                    Dim ev As EventoFlusso = Await f.LeggiAsync(45000, _cts.Token).ConfigureAwait(False)
                                    If ev Is Nothing Then Exit Do   ' il server lo chiude ogni tanto: si riapre
                                    If (DateTime.UtcNow - _ultimoUso).TotalSeconds > 90 Then Exit Do
                                    If ev.Nome = "stato" Then
                                        Registra(JObject.Parse(ev.Dati))
                                    ElseIf ev.Nome = "errore" Then
                                        Dim x As JObject = Nothing
                                        Try
                                            x = JObject.Parse(ev.Dati)
                                        Catch exLog As Exception
                                            RegistroErrori.Registra("GiochiClient.FlussoPartita.CicloAsync", exLog)
                                        End Try
                                        Dim codice As Integer = If(x IsNot Nothing AndAlso x("stato") IsNot Nothing, x.Value(Of Integer)("stato"), 500)
                                        ImpostaErrore(New GiochiErrore("Il server ha chiuso il collegamento.", codice))
                                        Return
                                    End If
                                Loop
                            End Using
                        Catch ex As OperationCanceledException
                            Return
                        Catch ex As GiochiErrore
                            If ex.Codice <> 0 Then
                                ImpostaErrore(ex)   ' 401, 403, 404, 503...: il flusso non serve: chi aspetta ripiega sulle richieste normali
                                Return
                            End If
                            guasto = ex
                        Catch ex As Exception
                            _logger.ScriviLog("GiochiClient.Flusso: " & ex.Message)
                            guasto = New GiochiErrore("Collegamento interrotto.", 0)
                        End Try
                        If guasto IsNot Nothing Then
                            If tentativo = 0 Then inizioCaduta = DateTime.UtcNow
                            tentativo += 1
                            If Not avvisato AndAlso tentativo >= 2 Then
                                avvisato = True
                                If Interlocked.Exchange(_client._senzaConnessione, 1) = 0 Then _client.Avvisa(_contesto, False)
                            End If
                            If (DateTime.UtcNow - inizioCaduta).TotalSeconds >= FINESTRA_PROVE_PARTITA_S Then
                                If avvisato Then Interlocked.Exchange(_client._senzaConnessione, 0)
                                ImpostaErrore(guasto)
                                Return
                            End If
                            Await Task.Delay(Math.Min(15000, 1000 * (1 << Math.Min(tentativo - 1, 4))) + _casuale.Next(0, 500), _cts.Token).ConfigureAwait(False)
                        End If
                    Loop
                Catch ex As OperationCanceledException
                Finally
                    _fermato = True
                    SyncLock _blocco
                        _cambio.TrySetResult(True)
                    End SyncLock
                End Try
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.CicloAsync", ex)
                Throw
            End Try
        End Function

        ''' <summary>
        ''' Aspetta fino a "secondi" che ci siano eventi più nuovi di "da". Restituisce lo stato (con solo gli eventi dopo "da"), oppure Nothing
        ''' se nel tempo dato non è arrivato niente o se il flusso non c'è più (in quel caso Errore, o Attivo falso, lo dicono).
        ''' </summary>
        Public Async Function AttendiAsync(da As Integer, secondi As Integer, ct As CancellationToken) As Task(Of JObject)
            Try
                _ultimoUso = DateTime.UtcNow
                Dim fine As DateTime = DateTime.UtcNow.AddSeconds(secondi)
                Do
                    Dim cambio As Task
                    SyncLock _blocco
                        If _errore IsNot Nothing Then Return Nothing
                        If _ultimoStato IsNot Nothing AndAlso _maxVisibile > da Then Return Costruisci(da)
                        cambio = _cambio.Task
                    End SyncLock
                    If _fermato Then Return Nothing
                    Dim resto As Double = (fine - DateTime.UtcNow).TotalMilliseconds
                    If resto <= 0 Then Return Nothing
                    Await Task.WhenAny(cambio, Task.Delay(CInt(resto), ct)).ConfigureAwait(False)
                    ct.ThrowIfCancellationRequested()
                    _ultimoUso = DateTime.UtcNow
                Loop
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.AttendiAsync", ex)
                Throw
            End Try
        End Function

        Private Function Costruisci(da As Integer) As JObject
            Try
                Dim r As JObject = CType(_ultimoStato.DeepClone(), JObject)
                Dim elenco As New JArray()
                For Each kv As KeyValuePair(Of Integer, JObject) In _eventi
                    If kv.Key > da Then elenco.Add(kv.Value.DeepClone())
                Next
                r("eventi") = elenco
                Return r
            Catch ex As Exception
                RegistroErrori.Registra("GiochiClient.FlussoPartita.Costruisci", ex)
                Throw
            End Try
        End Function
    End Class

End Class
