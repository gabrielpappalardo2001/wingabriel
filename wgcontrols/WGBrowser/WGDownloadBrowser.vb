Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Web.WebView2.Core
Imports wgcontrols

Public Class ElementoDownload
    Public Property Id As Integer = 0
    Public Property Indirizzo As String = ""
    Public Property IndirizzoOriginale As String = ""
    Public Property Totali As Long = -1
    Public Property Ricevuti As Long = 0
    Public Property Percentuale As Integer = -1
    Public Property Velocita As Long = 0
    Public Property TipoMime As String = ""
    Public Property NomeProposto As String = ""
    Public Property PercorsoCompleto As String = ""
    Public Property InPausa As Boolean = False
    Public Property Completato As Boolean = False
    Public Property Annullato As Boolean = False
    Public Property Interrotto As Boolean = False

    Public ReadOnly Property Concluso As Boolean
        Get
            Return Completato OrElse Annullato OrElse Interrotto
        End Get
    End Property

    Public Function Copia() As ElementoDownload
        Try
            Return DirectCast(MemberwiseClone(), ElementoDownload)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("ElementoDownload.Copia: " & ex.Message)
            Return New ElementoDownload() With {.Id = Id, .Indirizzo = Indirizzo, .PercorsoCompleto = PercorsoCompleto}
        End Try
    End Function

    Friend Sub CalcolaPercentuale()
        Try
            If Totali > 0 Then
                Percentuale = CInt(Math.Max(0, Math.Min(100, (Ricevuti * 100L) \ Totali)))
            Else
                Percentuale = -1
            End If
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("ElementoDownload.CalcolaPercentuale: " & ex.Message)
            Percentuale = -1
        End Try
    End Sub
End Class

Public Interface IOperazioneDownload
    ReadOnly Property Elemento As ElementoDownload
    Sub Pausa()
    Sub Riprendi()
    Sub Annulla()
End Interface

Public NotInheritable Class WGDownloadWebView2
    Implements IOperazioneDownload

    Private ReadOnly log As New Logger()
    Private ReadOnly _operazione As CoreWebView2DownloadOperation
    Private ReadOnly _elemento As ElementoDownload
    Private ReadOnly _notifica As Action(Of ElementoDownload)
    Private ReadOnly _contesto As System.Threading.SynchronizationContext = System.Threading.SynchronizationContext.Current
    Private _collegato As Boolean = False
    Private _istanteMisura As DateTime = DateTime.Now
    Private _byteMisura As Long = 0
    Private _velocita As Double = 0
    Private _istanteNotifica As DateTime = DateTime.MinValue

    Private Const IntervalloNotificaMs As Integer = 400

    Public Sub New(operazione As CoreWebView2DownloadOperation, elemento As ElementoDownload, notifica As Action(Of ElementoDownload))
        _operazione = operazione
        _elemento = If(elemento, New ElementoDownload())
        _notifica = notifica
        Try
            If _operazione Is Nothing Then Return
            AddHandler _operazione.StateChanged, AddressOf StatoCambiato
            AddHandler _operazione.BytesReceivedChanged, AddressOf ByteCambiati
            _collegato = True
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.New: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property Elemento As ElementoDownload Implements IOperazioneDownload.Elemento
        Get
            Return _elemento
        End Get
    End Property

    Private Sub Scollega()
        Try
            If Not _collegato OrElse _operazione Is Nothing Then Return
            _collegato = False
            RemoveHandler _operazione.StateChanged, AddressOf StatoCambiato
            RemoveHandler _operazione.BytesReceivedChanged, AddressOf ByteCambiati
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaDopoEvento()
        Try
            If Not _collegato OrElse _contesto Is Nothing Then Return
            _contesto.Post(Sub(stato As Object) Scollega(), Nothing)
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.ScollegaDopoEvento: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaVelocita()
        Try
            Dim ora As DateTime = DateTime.Now
            Dim secondi As Double = (ora - _istanteMisura).TotalSeconds
            If secondi < 0.5 Then Return
            Dim differenza As Long = Math.Max(0L, _elemento.Ricevuti - _byteMisura)
            Dim istantanea As Double = differenza / secondi
            _velocita = If(_velocita <= 0, istantanea, _velocita * 0.7 + istantanea * 0.3)
            _istanteMisura = ora
            _byteMisura = _elemento.Ricevuti
            _elemento.Velocita = CLng(Math.Max(0, _velocita))
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.AggiornaVelocita: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiStato()
        Try
            If _operazione Is Nothing Then Return
            Try
                _elemento.Ricevuti = Math.Max(0L, _operazione.BytesReceived)
            Catch exByte As Exception
                log.ScriviLog("WGDownloadWebView2.LeggiStato byte: " & exByte.Message)
            End Try
            Try
                Dim totale As ULong? = _operazione.TotalBytesToReceive
                If totale.HasValue AndAlso totale.Value > 0UL Then
                    _elemento.Totali = CLng(Math.Min(CDec(totale.Value), CDec(Long.MaxValue)))
                End If
            Catch exTotale As Exception
                log.ScriviLog("WGDownloadWebView2.LeggiStato totale: " & exTotale.Message)
            End Try
            Try
                Dim percorso As String = _operazione.ResultFilePath
                If Not String.IsNullOrWhiteSpace(percorso) Then _elemento.PercorsoCompleto = percorso
            Catch exPercorso As Exception
                log.ScriviLog("WGDownloadWebView2.LeggiStato percorso: " & exPercorso.Message)
            End Try
            Dim stato As CoreWebView2DownloadState = _operazione.State
            Select Case stato
                Case CoreWebView2DownloadState.Completed
                    _elemento.InPausa = False
                    _elemento.Completato = True
                    If _elemento.Totali <= 0 Then _elemento.Totali = _elemento.Ricevuti
                Case CoreWebView2DownloadState.Interrupted
                    Dim motivo As CoreWebView2DownloadInterruptReason = CoreWebView2DownloadInterruptReason.None
                    Try
                        motivo = _operazione.InterruptReason
                    Catch exMotivo As Exception
                        log.ScriviLog("WGDownloadWebView2.LeggiStato motivo: " & exMotivo.Message)
                    End Try
                    Select Case motivo
                        Case CoreWebView2DownloadInterruptReason.UserPaused
                            _elemento.InPausa = True
                        Case CoreWebView2DownloadInterruptReason.UserCanceled, CoreWebView2DownloadInterruptReason.UserShutdown
                            _elemento.InPausa = False
                            _elemento.Annullato = True
                        Case Else
                            _elemento.InPausa = False
                            _elemento.Interrotto = True
                            log.ScriviLog("WGDownloadWebView2: scaricamento interrotto, motivo " & motivo.ToString())
                    End Select
                Case Else
                    _elemento.InPausa = False
            End Select
            _elemento.CalcolaPercentuale()
            AggiornaVelocita()
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.LeggiStato: " & ex.Message)
        End Try
    End Sub

    Private Sub Notifica(forza As Boolean)
        Try
            Dim ora As DateTime = DateTime.Now
            If Not forza AndAlso (ora - _istanteNotifica).TotalMilliseconds < IntervalloNotificaMs Then Return
            _istanteNotifica = ora
            If _notifica IsNot Nothing Then _notifica(_elemento.Copia())
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Notifica: " & ex.Message)
        End Try
    End Sub

    Private Sub StatoCambiato(sender As Object, e As Object)
        Try
            LeggiStato()
            If _elemento.Concluso Then ScollegaDopoEvento()
            Notifica(True)
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.StatoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub ByteCambiati(sender As Object, e As Object)
        Try
            LeggiStato()
            Notifica(False)
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.ByteCambiati: " & ex.Message)
        End Try
    End Sub

    Public Sub Aggiorna()
        Try
            LeggiStato()
            Notifica(True)
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Aggiorna: " & ex.Message)
        End Try
    End Sub

    Public Sub Pausa() Implements IOperazioneDownload.Pausa
        Try
            If _operazione Is Nothing OrElse _elemento.Concluso Then Return
            If _operazione.State = CoreWebView2DownloadState.InProgress Then _operazione.Pause()
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Pausa: " & ex.Message)
        End Try
    End Sub

    Public Sub Riprendi() Implements IOperazioneDownload.Riprendi
        Try
            If _operazione Is Nothing OrElse _elemento.Concluso Then Return
            If _operazione.State = CoreWebView2DownloadState.Interrupted AndAlso _operazione.CanResume Then _operazione.Resume()
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Riprendi: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla() Implements IOperazioneDownload.Annulla
        Try
            If _operazione Is Nothing OrElse _elemento.Concluso Then Return
            _operazione.Cancel()
        Catch ex As Exception
            log.ScriviLog("WGDownloadWebView2.Annulla: " & ex.Message)
        End Try
    End Sub
End Class

Public NotInheritable Class WGDownloadDiretto
    Implements IOperazioneDownload

    Private Shared ReadOnly _lockClient As New Object()
    Private Shared _client As HttpClient = Nothing

    Private ReadOnly log As New Logger()
    Private ReadOnly _lock As New Object()
    Private ReadOnly _elemento As ElementoDownload
    Private ReadOnly _intestazioni As Dictionary(Of String, String)
    Private ReadOnly _notifica As Action(Of ElementoDownload)
    Private ReadOnly _percorsoParziale As String
    Private ReadOnly _accettaNomeServer As Boolean
    Private _nomeServer As String = ""
    Private _annullamento As CancellationTokenSource = Nothing
    Private _trasferimento As Task = Nothing
    Private _annullato As Boolean = False
    Private _concluso As Boolean = False
    Private _istanteMisura As DateTime = DateTime.Now
    Private _byteMisura As Long = 0
    Private _velocita As Double = 0
    Private _istanteNotifica As DateTime = DateTime.MinValue

    Private Const DimensioneBlocco As Integer = 81920
    Private Const IntervalloNotificaMs As Integer = 500

    Public Sub New(elemento As ElementoDownload, intestazioni As Dictionary(Of String, String), notifica As Action(Of ElementoDownload), Optional accettaNomeServer As Boolean = False)
        _elemento = If(elemento, New ElementoDownload())
        _intestazioni = If(intestazioni, New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase))
        _notifica = notifica
        _accettaNomeServer = accettaNomeServer
        Try
            _percorsoParziale = _elemento.PercorsoCompleto & ".scaricamento"
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.New: " & ex.Message)
            _percorsoParziale = Path.Combine(Path.GetTempPath(), "WinGabriel_" & Guid.NewGuid().ToString("N") & ".scaricamento")
        End Try
    End Sub

    Public ReadOnly Property Elemento As ElementoDownload Implements IOperazioneDownload.Elemento
        Get
            Return _elemento
        End Get
    End Property

    Private Shared Function Client() As HttpClient
        SyncLock _lockClient
            If _client Is Nothing Then
                Try
                    ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
                Catch exProtocollo As Exception
                    Dim registroProtocollo As New Logger()
                    registroProtocollo.ScriviLog("WGDownloadDiretto.Client protocollo: " & exProtocollo.Message)
                End Try
                Dim gestore As New HttpClientHandler()
                gestore.UseCookies = False
                gestore.AllowAutoRedirect = True
                gestore.AutomaticDecompression = DecompressionMethods.None
                Dim nuovo As New HttpClient(gestore)
                nuovo.Timeout = Timeout.InfiniteTimeSpan
                _client = nuovo
            End If
            Return _client
        End SyncLock
    End Function

    Public Sub Avvia()
        AvviaTrasferimento(False)
    End Sub

    Private Sub AvviaTrasferimento(riprendi As Boolean)
        Try
            SyncLock _lock
                If _concluso OrElse _annullato Then Return
                If _trasferimento IsNot Nothing AndAlso Not _trasferimento.IsCompleted Then Return
                Dim sorgente As New CancellationTokenSource()
                _annullamento = sorgente
                _elemento.InPausa = False
                Dim token As CancellationToken = sorgente.Token
                _trasferimento = Task.Run(Function() TrasferisciAsync(riprendi, token))
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.AvviaTrasferimento: " & ex.Message)
            Concludi("interrotto")
        End Try
    End Sub

    Private Sub AggiornaVelocita()
        Try
            Dim ora As DateTime = DateTime.Now
            Dim secondi As Double = (ora - _istanteMisura).TotalSeconds
            If secondi < 0.5 Then Return
            Dim differenza As Long = Math.Max(0L, _elemento.Ricevuti - _byteMisura)
            Dim istantanea As Double = differenza / secondi
            _velocita = If(_velocita <= 0, istantanea, _velocita * 0.7 + istantanea * 0.3)
            _istanteMisura = ora
            _byteMisura = _elemento.Ricevuti
            _elemento.Velocita = CLng(Math.Max(0, _velocita))
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.AggiornaVelocita: " & ex.Message)
        End Try
    End Sub

    Private Sub Notifica(forza As Boolean)
        Try
            Dim ora As DateTime = DateTime.Now
            If Not forza AndAlso (ora - _istanteNotifica).TotalMilliseconds < IntervalloNotificaMs Then Return
            _istanteNotifica = ora
            If _notifica IsNot Nothing Then _notifica(_elemento.Copia())
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.Notifica: " & ex.Message)
        End Try
    End Sub

    Private Function PreparaRichiesta(inizio As Long) As HttpRequestMessage
        Dim richiesta As New HttpRequestMessage(HttpMethod.Get, _elemento.Indirizzo)
        Try
            For Each coppia As KeyValuePair(Of String, String) In _intestazioni
                If String.IsNullOrWhiteSpace(coppia.Key) OrElse coppia.Value Is Nothing Then Continue For
                richiesta.Headers.TryAddWithoutValidation(coppia.Key, coppia.Value)
            Next
            If Not richiesta.Headers.Contains("Accept") Then richiesta.Headers.TryAddWithoutValidation("Accept", "*/*")
            If inizio > 0 Then richiesta.Headers.Range = New RangeHeaderValue(inizio, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.PreparaRichiesta: " & ex.Message)
        End Try
        Return richiesta
    End Function

    Private Shared Function NomeDaDisposizione(disposizione As ContentDispositionHeaderValue) As String
        Try
            If disposizione Is Nothing Then Return ""
            Dim nome As String = If(disposizione.FileNameStar, disposizione.FileName)
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Return nome.Trim().Trim(""""c)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGDownloadDiretto.NomeDaDisposizione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function TrasferisciAsync(riprendi As Boolean, token As CancellationToken) As Task
        Dim esito As String = ""
        Try
            Dim inizio As Long = 0
            If riprendi AndAlso File.Exists(_percorsoParziale) Then inizio = New FileInfo(_percorsoParziale).Length
            Dim cartella As String = Path.GetDirectoryName(_percorsoParziale)
            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Using richiesta As HttpRequestMessage = PreparaRichiesta(inizio)
                Using risposta As HttpResponseMessage = Await Client().SendAsync(richiesta, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(False)
                    If Not risposta.IsSuccessStatusCode Then
                        log.ScriviLog("WGDownloadDiretto.TrasferisciAsync: il server ha risposto " & CInt(risposta.StatusCode) & " per " & _elemento.Indirizzo)
                        esito = "interrotto"
                    Else
                        Dim parziale As Boolean = inizio > 0 AndAlso risposta.StatusCode = HttpStatusCode.PartialContent
                        If Not parziale Then inizio = 0
                        Dim lunghezza As Long? = risposta.Content.Headers.ContentLength
                        If lunghezza.HasValue AndAlso lunghezza.Value >= 0 Then
                            _elemento.Totali = inizio + lunghezza.Value
                        ElseIf Not parziale Then
                            _elemento.Totali = -1
                        End If
                        If risposta.Content.Headers.ContentType IsNot Nothing AndAlso Not String.IsNullOrEmpty(risposta.Content.Headers.ContentType.MediaType) Then _elemento.TipoMime = risposta.Content.Headers.ContentType.MediaType
                        Dim nomeServer As String = NomeDaDisposizione(risposta.Content.Headers.ContentDisposition)
                        If nomeServer <> "" AndAlso _accettaNomeServer Then
                            _nomeServer = nomeServer
                            _elemento.NomeProposto = nomeServer
                        End If
                        _elemento.Ricevuti = inizio
                        _byteMisura = inizio
                        _istanteMisura = DateTime.Now
                        _elemento.CalcolaPercentuale()
                        Notifica(True)
                        Using sorgente As Stream = Await risposta.Content.ReadAsStreamAsync().ConfigureAwait(False)
                            Using destinazione As New FileStream(_percorsoParziale, If(parziale, FileMode.Append, FileMode.Create), FileAccess.Write, FileShare.Read, DimensioneBlocco, True)
                                Dim blocco(DimensioneBlocco - 1) As Byte
                                Do
                                    Dim letti As Integer = Await sorgente.ReadAsync(blocco, 0, blocco.Length, token).ConfigureAwait(False)
                                    If letti <= 0 Then Exit Do
                                    Await destinazione.WriteAsync(blocco, 0, letti, token).ConfigureAwait(False)
                                    _elemento.Ricevuti += letti
                                    _elemento.CalcolaPercentuale()
                                    AggiornaVelocita()
                                    Notifica(False)
                                Loop
                                Await destinazione.FlushAsync(token).ConfigureAwait(False)
                            End Using
                        End Using
                        If _elemento.Totali > 0 AndAlso _elemento.Ricevuti < _elemento.Totali Then
                            log.ScriviLog("WGDownloadDiretto.TrasferisciAsync: ricevuti " & _elemento.Ricevuti & " byte su " & _elemento.Totali & " per " & _elemento.Indirizzo)
                            esito = "interrotto"
                        Else
                            esito = "completato"
                        End If
                    End If
                End Using
            End Using
        Catch ex As OperationCanceledException
            esito = If(_annullato, "annullato", "pausa")
        Catch ex As Exception
            If token.IsCancellationRequested Then
                esito = If(_annullato, "annullato", "pausa")
            Else
                log.ScriviLog("WGDownloadDiretto.TrasferisciAsync: " & ex.Message)
                esito = "interrotto"
            End If
        End Try
        Try
            Select Case esito
                Case "completato"
                    Concludi("completato")
                Case "annullato"
                    Concludi("annullato")
                Case "pausa"
                    SyncLock _lock
                        _elemento.InPausa = True
                        _elemento.Velocita = 0
                        _velocita = 0
                    End SyncLock
                    Notifica(True)
                Case Else
                    Concludi("interrotto")
            End Select
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.TrasferisciAsync esito: " & ex.Message)
        End Try
    End Function

    Private Sub CancellaParziale()
        Try
            If File.Exists(_percorsoParziale) Then File.Delete(_percorsoParziale)
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.CancellaParziale: " & ex.Message)
        End Try
    End Sub

    Private Function SpostaNelPercorsoFinale() As Boolean
        Try
            Dim destinazione As String = _elemento.PercorsoCompleto
            Dim cartella As String = Path.GetDirectoryName(destinazione)
            If _accettaNomeServer AndAlso _nomeServer <> "" AndAlso Not String.IsNullOrEmpty(cartella) Then
                destinazione = Path.Combine(cartella, WGBrowserGestoreDownload.NomeFileSicuro(_nomeServer))
            End If
            If File.Exists(destinazione) AndAlso Not String.IsNullOrEmpty(cartella) Then
                destinazione = WGBrowserGestoreDownload.PercorsoLibero(cartella, Path.GetFileName(destinazione))
            End If
            File.Move(_percorsoParziale, destinazione)
            _elemento.PercorsoCompleto = destinazione
            Return True
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.SpostaNelPercorsoFinale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub Concludi(esito As String)
        Try
            SyncLock _lock
                If _concluso Then Return
                _concluso = True
                _elemento.InPausa = False
                _elemento.Velocita = 0
                Select Case esito
                    Case "completato"
                        If SpostaNelPercorsoFinale() Then
                            _elemento.Completato = True
                            If _elemento.Totali <= 0 Then _elemento.Totali = _elemento.Ricevuti
                            _elemento.CalcolaPercentuale()
                        Else
                            _elemento.Interrotto = True
                            CancellaParziale()
                        End If
                    Case "annullato"
                        _elemento.Annullato = True
                        CancellaParziale()
                    Case Else
                        _elemento.Interrotto = True
                        CancellaParziale()
                End Select
            End SyncLock
            Notifica(True)
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.Concludi: " & ex.Message)
        End Try
    End Sub

    Public Sub Pausa() Implements IOperazioneDownload.Pausa
        Try
            SyncLock _lock
                If _concluso OrElse _annullato Then Return
                If _trasferimento Is Nothing OrElse _trasferimento.IsCompleted Then Return
                _elemento.InPausa = True
                If _annullamento IsNot Nothing Then _annullamento.Cancel()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.Pausa: " & ex.Message)
        End Try
    End Sub

    Public Sub Riprendi() Implements IOperazioneDownload.Riprendi
        Try
            SyncLock _lock
                If _concluso OrElse _annullato Then Return
                If _trasferimento IsNot Nothing AndAlso Not _trasferimento.IsCompleted Then Return
            End SyncLock
            AvviaTrasferimento(True)
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.Riprendi: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla() Implements IOperazioneDownload.Annulla
        Try
            Dim inCorso As Boolean
            SyncLock _lock
                If _concluso Then Return
                _annullato = True
                inCorso = _trasferimento IsNot Nothing AndAlso Not _trasferimento.IsCompleted
                If inCorso AndAlso _annullamento IsNot Nothing Then _annullamento.Cancel()
            End SyncLock
            If Not inCorso Then Concludi("annullato")
        Catch ex As Exception
            log.ScriviLog("WGDownloadDiretto.Annulla: " & ex.Message)
        End Try
    End Sub
End Class
