Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

''' <summary>
''' Il collegamento al relè della chat (chat.php). Il relè è uno solo e non si cambia (nelle prove si può passare un indirizzo di
''' prova al costruttore). Non porta né account né sessioni: ogni richiesta è autorizzata dai segreti della cassetta.
''' </summary>
Public NotInheritable Class ReleHttp
    Implements IReleChat

    Public Shared ReadOnly Property Indirizzo As String
        Get
            Return WGGiochi.AmbienteBeta.ServerChat
        End Get
    End Property
    Private ReadOnly _url As String

    Public Sub New()
        Try
            _url = Indirizzo
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Solo per le prove locali.</summary>
    Public Sub New(indirizzoProva As String)
        Try
            _url = indirizzoProva
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.New", ex)
            Throw
        End Try
    End Sub

    Private Async Function ChiamaAsync(azione As String, corpo As JObject, durataMassimaMs As Integer) As Task(Of Tuple(Of Integer, JObject))
        Try
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(_url & If(_url.Contains("?"), "&", "?") & "a=" & azione), HttpWebRequest)
            richiesta.Method = "POST"
            richiesta.UserAgent = "WinGabriel/1.0"
            richiesta.Accept = "application/json"
            ' l'ascolto è continuo: la connessione resta aperta e non si rifà la stretta di mano TLS a ogni attesa
            ' (se si è chiusa nel frattempo la richiesta fallisce e il motore della chat la ripete da solo)
            richiesta.KeepAlive = True
            richiesta.Timeout = durataMassimaMs
            richiesta.ReadWriteTimeout = durataMassimaMs
            Dim dati As Byte() = Encoding.UTF8.GetBytes(corpo.ToString(Newtonsoft.Json.Formatting.None))
            richiesta.ContentType = "application/json; charset=utf-8"
            richiesta.ContentLength = dati.Length
            Using limite As New CancellationTokenSource(durataMassimaMs)
                Using registrazione As CancellationTokenRegistration = limite.Token.Register(Sub() richiesta.Abort())
                    Dim risposta As HttpWebResponse = Nothing
                    Try
                        Using flusso As Stream = Await richiesta.GetRequestStreamAsync().ConfigureAwait(False)
                            Await flusso.WriteAsync(dati, 0, dati.Length).ConfigureAwait(False)
                        End Using
                        risposta = CType(Await richiesta.GetResponseAsync().ConfigureAwait(False), HttpWebResponse)
                    Catch ex As WebException
                        risposta = TryCast(ex.Response, HttpWebResponse)
                        If risposta Is Nothing Then Throw New IOException("Non riesco a collegarmi al server della chat.")
                    End Try
                    Using risposta
                        Using lettore As New StreamReader(risposta.GetResponseStream(), Encoding.UTF8)
                            Dim testo As String = Await lettore.ReadToEndAsync().ConfigureAwait(False)
                            Dim o As JObject = Nothing
                            Try
                                Dim t As JToken = JToken.Parse(testo)
                                o = TryCast(t, JObject)
                            Catch exLog As Exception
                                RegistroErrori.Registra("ReleHttp.ChiamaAsync", exLog)
                            End Try
                            Return Tuple.Create(CInt(risposta.StatusCode), If(o, New JObject()))
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.ChiamaAsync", ex)
            Throw
        End Try
    End Function

    ' ---------------------------------------------------------------- flusso di eventi
    Private Shared _flussiSospesiFino As DateTime = DateTime.MinValue
    Private _flusso As FlussoChat

    Private Function OttieniFlusso(cassette As JArray) As FlussoChat
        Try
            Dim chiave As String = String.Join(",", cassette.OfType(Of JObject)().Select(Function(c) Convert.ToString(c("mb"))).OrderBy(Function(m) m, StringComparer.Ordinal))
            SyncLock Me
                If _flusso IsNot Nothing AndAlso _flusso.Chiave = chiave AndAlso _flusso.Attivo Then Return _flusso
                If _flusso IsNot Nothing Then _flusso.Ferma()
                _flusso = New FlussoChat(_url & If(_url.Contains("?"), "&", "?") & "a=flusso", chiave, cassette)
                Return _flusso
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.OttieniFlusso", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Il flusso di eventi della chat: una connessione sempre aperta su cui il server scrive i blocchi appena arrivano. Tiene i blocchi
    ''' non ancora consegnati; si riapre da solo se la rete cade e si ferma da solo se nessuno lo usa per un minuto.
    ''' </summary>
    Private NotInheritable Class FlussoChat
        Private ReadOnly _indirizzo As String
        Private ReadOnly _corpo As JObject
        Private ReadOnly _blocco As New Object()
        Private ReadOnly _blocchi As New Dictionary(Of String, JObject)()
        Private ReadOnly _cts As New CancellationTokenSource()
        Private _cambio As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Private _errore As WGGiochi.GiochiErrore
        Private _fermato As Boolean
        Private _ultimoUso As DateTime = DateTime.UtcNow
        Public ReadOnly Chiave As String
        Private Shared ReadOnly _casuale As New Random()

        Public Sub New(indirizzo As String, chiave As String, cassette As JArray)
            Try
                _indirizzo = indirizzo
                Me.Chiave = chiave
                _corpo = New JObject From {{"cassette", CType(cassette.DeepClone(), JArray)}}
                Dim avvio As Task = Task.Run(Function() CicloAsync())
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.New", ex)
                Throw
            End Try
        End Sub

        Public ReadOnly Property Attivo As Boolean
            Get
                Return Not _fermato AndAlso _errore Is Nothing
            End Get
        End Property

        Public ReadOnly Property Errore As WGGiochi.GiochiErrore
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
                    RegistroErrori.Registra("ReleHttp.FlussoChat.Ferma", exLog)
                End Try
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.Ferma", ex)
                Throw
            End Try
        End Sub

        ''' <summary>I blocchi già elaborati e confermati non si riconsegnano.</summary>
        Public Sub Dimentica(ack As IList(Of String))
            Try
                SyncLock _blocco
                    For Each id As String In ack
                        _blocchi.Remove(id)
                    Next
                End SyncLock
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.Dimentica", ex)
                Throw
            End Try
        End Sub

        Private Sub ImpostaErrore(e As WGGiochi.GiochiErrore)
            Try
                SyncLock _blocco
                    _errore = e
                    _cambio.TrySetResult(True)
                End SyncLock
                ' il server non ha i flussi o sono pieni: per un po' non si riprova
                If e.Codice = 400 OrElse e.Codice = 404 OrElse e.Codice >= 500 Then _flussiSospesiFino = DateTime.UtcNow.AddMinutes(5)
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.ImpostaErrore", ex)
                Throw
            End Try
        End Sub

        Private Sub Registra(dati As JObject)
            Try
                Dim elenco As JArray = TryCast(dati("blocchi"), JArray)
                If elenco Is Nothing OrElse elenco.Count = 0 Then Return
                SyncLock _blocco
                    For Each b As JObject In elenco.OfType(Of JObject)()
                        _blocchi(Convert.ToString(b("id"))) = b
                    Next
                    Dim vecchio As TaskCompletionSource(Of Boolean) = _cambio
                    _cambio = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                    vecchio.TrySetResult(True)
                End SyncLock
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.Registra", ex)
                Throw
            End Try
        End Sub

        Private Async Function CicloAsync() As Task
            Try
                Dim tentativo As Integer = 0
                Dim inizioCaduta As DateTime = DateTime.UtcNow
                Try
                    Do While Not _fermato
                        If (DateTime.UtcNow - _ultimoUso).TotalSeconds > 60 Then Exit Do   ' nessuno lo usa più
                        Dim guasto As WGGiochi.GiochiErrore = Nothing
                        Try
                            Using f As WGGiochi.FlussoEventi = Await WGGiochi.FlussoEventi.ApriAsync(_indirizzo, "POST", _corpo, "", "WinGabriel/" & WGGiochi.GiochiClient.VersioneProgramma.ToString(), _cts.Token).ConfigureAwait(False)
                                tentativo = 0
                                Do
                                    Dim ev As WGGiochi.EventoFlusso = Await f.LeggiAsync(45000, _cts.Token).ConfigureAwait(False)
                                    If ev Is Nothing Then Exit Do   ' il server lo chiude ogni tanto: si riapre
                                    If (DateTime.UtcNow - _ultimoUso).TotalSeconds > 60 Then Exit Do
                                    If ev.Nome = "blocchi" Then
                                        Registra(JObject.Parse(ev.Dati))
                                    ElseIf ev.Nome = "errore" Then
                                        Dim x As JObject = Nothing
                                        Try
                                            x = JObject.Parse(ev.Dati)
                                        Catch exLog As Exception
                                            RegistroErrori.Registra("ReleHttp.FlussoChat.CicloAsync", exLog)
                                        End Try
                                        ImpostaErrore(New WGGiochi.GiochiErrore("Il server ha chiuso il collegamento.", If(x IsNot Nothing AndAlso x("stato") IsNot Nothing, x.Value(Of Integer)("stato"), 500)))
                                        Return
                                    End If
                                Loop
                            End Using
                        Catch ex As OperationCanceledException
                            Return
                        Catch ex As WGGiochi.GiochiErrore
                            If ex.Codice <> 0 Then
                                ImpostaErrore(ex)
                                Return
                            End If
                            guasto = ex
                        Catch ex As Exception
                            RegistroErrori.Registra("ReleHttp.FlussoChat.CicloAsync", ex)
                            guasto = New WGGiochi.GiochiErrore("Collegamento interrotto.", 0)
                        End Try
                        If guasto IsNot Nothing Then
                            If tentativo = 0 Then inizioCaduta = DateTime.UtcNow
                            tentativo += 1
                            If (DateTime.UtcNow - inizioCaduta).TotalSeconds >= 120 Then
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
                RegistroErrori.Registra("ReleHttp.FlussoChat.CicloAsync", ex)
                Throw
            End Try
        End Function

        ''' <summary>
        ''' Aspetta fino a "secondi" che arrivi qualche blocco. Restituisce i blocchi (e li toglie da qui), un elenco vuoto se nel tempo dato
        ''' non è arrivato niente, oppure Nothing se il flusso non c'è più (Errore dice perché).
        ''' </summary>
        Public Async Function AttendiAsync(secondi As Integer) As Task(Of JArray)
            Try
                _ultimoUso = DateTime.UtcNow
                Dim fine As DateTime = DateTime.UtcNow.AddSeconds(secondi)
                Do
                    Dim cambio As Task
                    SyncLock _blocco
                        If _blocchi.Count > 0 Then
                            Dim r As New JArray(_blocchi.Values.Select(Function(b) CType(b.DeepClone(), JToken)))
                            _blocchi.Clear()
                            Return r
                        End If
                        If _errore IsNot Nothing Then Return Nothing
                        cambio = _cambio.Task
                    End SyncLock
                    If _fermato Then Return Nothing
                    Dim resto As Double = (fine - DateTime.UtcNow).TotalMilliseconds
                    If resto <= 0 Then Return New JArray()
                    Await Task.WhenAny(cambio, Task.Delay(CInt(resto))).ConfigureAwait(False)
                    _ultimoUso = DateTime.UtcNow
                Loop
            Catch ex As Exception
                RegistroErrori.Registra("ReleHttp.FlussoChat.AttendiAsync", ex)
                Throw
            End Try
        End Function
    End Class

    Public Async Function InviaAsync(mb As String, tk As String, b64 As String) As Task(Of String) Implements IReleChat.InviaAsync
        Try
            Dim r As Tuple(Of Integer, JObject) = Await ChiamaAsync("invia", New JObject From {{"mb", mb}, {"tk", tk}, {"b", b64}}, 20000)
            Return If(r.Item1 = 200, "ok", "no")
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.InviaAsync", ex)
            Throw
        End Try
    End Function

    Public Async Function AscoltaAsync(cassette As JArray, ack As IList(Of String), attesa As Integer) As Task(Of JArray) Implements IReleChat.AscoltaAsync
        Try
            ' Con il flusso di eventi il server scrive appena arriva un blocco: l'attesa sta lì. Le conferme passano subito con una richiesta
            ' normale (senza attesa), poi si torna ad ascoltare il flusso. Se il server non ha i flussi, tutto va come prima.
            If attesa > 0 AndAlso cassette.Count > 0 AndAlso DateTime.UtcNow >= _flussiSospesiFino Then
                If ack.Count > 0 Then
                    _flusso?.Dimentica(ack)
                    attesa = 0
                Else
                    Dim flusso As FlussoChat = OttieniFlusso(cassette)
                    Dim blocchi As JArray = Await flusso.AttendiAsync(attesa).ConfigureAwait(False)
                    If blocchi IsNot Nothing Then Return blocchi
                    Dim guasto As WGGiochi.GiochiErrore = flusso.Errore
                    If guasto Is Nothing Then Return New JArray()   ' nessun blocco nel tempo dell'attesa
                    If guasto.Codice = 0 Then Throw New IOException("Non riesco a collegarmi al server della chat.")
                    ' altrimenti il server non ha i flussi: si ripiega sull'attesa normale qui sotto
                End If
            End If
            Dim corpo As New JObject From {{"cassette", cassette}, {"ack", New JArray(ack)}, {"attesa", attesa}}
            Dim r As Tuple(Of Integer, JObject) = Await ChiamaAsync("ascolta", corpo, (attesa + 20) * 1000)
            If r.Item1 <> 200 Then Throw New IOException("Il server della chat ha risposto con l'errore " & r.Item1.ToString() & ".")
            Dim b As JArray = TryCast(r.Item2("blocchi"), JArray)
            Return If(b, New JArray())
        Catch ex As Exception
            RegistroErrori.Registra("ReleHttp.AscoltaAsync", ex)
            Throw
        End Try
    End Function
End Class
