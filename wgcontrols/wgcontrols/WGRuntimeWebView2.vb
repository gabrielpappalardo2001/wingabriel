Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' Gestione del runtime WebView2 "Fixed Version": posizione, versione, download del .cab
''' dal catalogo ufficiale Microsoft ed estrazione nella cartella radice \WebView2.
''' Comprende anche il flusso guidato per l'utente (domanda, avanzamento ogni 5%, esito),
''' cosi' che qualunque parte del programma possa usarlo senza dipendere da altre classi del browser.
''' </summary>
Public NotInheritable Class WGRuntimeWebView2
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _semaforo As New SemaphoreSlim(1, 1)
    Private Shared ReadOnly _bloccoFlusso As New Object()
    Private Shared _installazioneGuidata As Task(Of Boolean)
    Private Shared _ultimoFallimento As DateTime = DateTime.MinValue
    Private Const PausaDopoFallimentoMinuti As Integer = 5
    Private Const AttesaAltraInstallazioneMinuti As Integer = 15

    Public Const NomeCartella As String = "WebView2"
    Public Const NomeEseguibile As String = "msedgewebview2.exe"

    Private Const IndirizzoCatalogo As String = "https://developer.microsoft.com/microsoft-edge/api/webview2"
    Private Const SuffissoStaging As String = ".tmp"
    Private Const SuffissoPrecedente As String = ".old"
    Private Const DimensioneMinimaCab As Long = 50L * 1024L * 1024L
    Private Const AttesaRichiestaSecondi As Integer = 30
    Private Const StalloScaricamentoMs As Integer = 60000
    Private Const AttesaEstrazioneMs As Integer = 10 * 60 * 1000
    Private Const ProfonditaRicercaMotore As Integer = 3
    Private Const PassoPercentuale As Integer = 5

    Private Sub New()
    End Sub

#Region "Posizione e versione"

    ''' <summary>Cartella \WebView2 nella radice dell'applicazione (esista o no).</summary>
    Public Shared Function CartellaRuntime() As String
        Try
            Return Path.Combine(CartellaBaseApplicazione(), NomeCartella)
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.CartellaRuntime: " & ex.Message)
            Return Path.Combine(Application.StartupPath, NomeCartella)
        End Try
    End Function

    ''' <summary>
    ''' Cartella radice dell'applicazione, calcolata autonomamente (senza dipendere da altre classi
    ''' del browser) cosi' che questa classe possa vivere in un progetto separato, es. wgcontrols.
    ''' </summary>
    Public Shared Function CartellaBaseApplicazione() As String
        Try
            Return Path.GetDirectoryName(GetType(WGRuntimeWebView2).Assembly.Location)
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.CartellaBaseApplicazione: " & ex.Message)
            Return Application.StartupPath
        End Try
    End Function

    ''' <summary>La cartella \WebView2 se contiene un runtime utilizzabile, altrimenti Nothing.</summary>
    Public Shared Function CartellaRuntimeValida() As String
        Try
            Dim cartella As String = CartellaRuntime()
            If File.Exists(Path.Combine(cartella, NomeEseguibile)) Then Return cartella
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.CartellaRuntimeValida: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Shared Function RuntimePresente() As Boolean
        Return Not String.IsNullOrEmpty(CartellaRuntimeValida())
    End Function

    ''' <summary>Versione del runtime presente in \WebView2, stringa vuota se assente.</summary>
    Public Shared Function VersioneInstallata() As String
        Try
            Dim cartella As String = CartellaRuntimeValida()
            If String.IsNullOrEmpty(cartella) Then Return ""
            Return If(CoreWebView2Environment.GetAvailableBrowserVersionString(cartella), "").Trim()
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.VersioneInstallata: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Architettura"

    ''' <summary>Architettura del sistema operativo: "x86", "x64" o "arm64".</summary>
    Public Shared Function ArchitetturaSistema() As String
        Try
            Select Case RuntimeInformation.OSArchitecture
                Case Architecture.Arm64
                    Return "arm64"
                Case Architecture.X64
                    Return "x64"
                Case Else
                    Return "x86"
            End Select
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.ArchitetturaSistema: " & ex.Message)
            Return If(Environment.Is64BitOperatingSystem, "x64", "x86")
        End Try
    End Function

    ''' <summary>Architettura del processo in esecuzione: "x86", "x64" o "arm64".</summary>
    Public Shared Function ArchitetturaProcesso() As String
        Try
            Select Case RuntimeInformation.ProcessArchitecture
                Case Architecture.Arm64
                    Return "arm64"
                Case Architecture.X64
                    Return "x64"
                Case Else
                    Return "x86"
            End Select
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.ArchitetturaProcesso: " & ex.Message)
            Return If(Environment.Is64BitProcess, "x64", "x86")
        End Try
    End Function

    ''' <summary>
    ''' Architettura del runtime da scaricare: quella del sistema operativo, ma mai piu' ampia
    ''' di quella del processo (un'app a 32 bit su Windows a 64 bit richiede il runtime x86).
    ''' </summary>
    Public Shared Function ArchitetturaRuntime() As String
        Dim sistema As String = ArchitetturaSistema()
        Dim processo As String = ArchitetturaProcesso()
        If String.Equals(sistema, processo, StringComparison.Ordinal) Then Return sistema
        Return processo
    End Function

#End Region

#Region "Catalogo"

    ''' <summary>Legge il catalogo ufficiale Microsoft dei runtime a versione fissa (lista vuota se non raggiungibile).</summary>
    Public Shared Async Function LeggiCatalogoAsync(token As CancellationToken) As Task(Of List(Of WGCabRuntime))
        Dim elenco As New List(Of WGCabRuntime)()
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Using client As HttpClient = CreaClient()
                Using risposta As HttpResponseMessage = Await client.GetAsync(IndirizzoCatalogo, token).ConfigureAwait(False)
                    risposta.EnsureSuccessStatusCode()
                    Dim json As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
                    Dim versioni As JArray = JArray.Parse(json)
                    For Each voce As JToken In versioni
                        Dim versione As String = voce.Value(Of String)("version")
                        Dim build As JArray = TryCast(voce("builds"), JArray)
                        If String.IsNullOrWhiteSpace(versione) OrElse build Is Nothing Then Continue For
                        For Each b As JToken In build
                            Dim architettura As String = b.Value(Of String)("architecture")
                            Dim indirizzo As String = b.Value(Of String)("url")
                            If String.IsNullOrWhiteSpace(architettura) OrElse String.IsNullOrWhiteSpace(indirizzo) Then Continue For
                            elenco.Add(New WGCabRuntime With {.Versione = versione, .Architettura = architettura.ToLowerInvariant(), .Indirizzo = indirizzo})
                        Next
                    Next
                End Using
            End Using
        Catch ex As OperationCanceledException
            log.ScriviLog("WGRuntimeWebView2.LeggiCatalogoAsync: annullato o scaduto")
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.LeggiCatalogoAsync: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>La versione piu' recente del catalogo per l'architettura richiesta, Nothing se non c'e'.</summary>
    Public Shared Function TrovaUltimoCab(catalogo As List(Of WGCabRuntime), architettura As String) As WGCabRuntime
        Try
            If catalogo Is Nothing OrElse String.IsNullOrWhiteSpace(architettura) Then Return Nothing
            Dim candidati As List(Of WGCabRuntime) = catalogo.Where(Function(c) String.Equals(c.Architettura, architettura, StringComparison.OrdinalIgnoreCase)).ToList()
            If candidati.Count = 0 Then Return Nothing
            Return candidati.OrderByDescending(Function(c) VersioneNumerica(c.Versione)).First()
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.TrovaUltimoCab: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Restituisce la versione piu' recente disponibile se e' piu' nuova di quella installata, altrimenti stringa vuota.</summary>
    Public Shared Async Function VersioneAggiornataDisponibileAsync(token As CancellationToken) As Task(Of String)
        Try
            Dim installata As String = VersioneInstallata()
            Dim catalogo As List(Of WGCabRuntime) = Await LeggiCatalogoAsync(token).ConfigureAwait(False)
            Dim ultimo As WGCabRuntime = TrovaUltimoCab(catalogo, ArchitetturaRuntime())
            If ultimo Is Nothing Then Return ""
            If installata = "" Then Return ultimo.Versione
            If VersioneNumerica(ultimo.Versione).CompareTo(VersioneNumerica(installata)) > 0 Then Return ultimo.Versione
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.VersioneAggiornataDisponibileAsync: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function VersioneNumerica(testo As String) As Version
        Dim v As Version = Nothing
        If Version.TryParse(If(testo, "").Trim(), v) Then Return v
        Return New Version(0, 0)
    End Function

    Private Shared Function CreaClient() As HttpClient
        Dim client As New HttpClient()
        client.Timeout = TimeSpan.FromSeconds(AttesaRichiestaSecondi)
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0")
        Return client
    End Function

#End Region

#Region "Download ed estrazione"

    ''' <summary>
    ''' Scarica il .cab in percorsoDestinazione (file temporaneo .part, rinominato a fine scaricamento).
    ''' progresso riceve la percentuale 0-100 quando la dimensione e' nota.
    ''' </summary>
    Public Shared Async Function ScaricaCabAsync(cab As WGCabRuntime, percorsoDestinazione As String, progresso As IProgress(Of Integer), token As CancellationToken) As Task(Of Boolean)
        Dim temporaneo As String = percorsoDestinazione & ".part"
        Try
            If cab Is Nothing OrElse String.IsNullOrWhiteSpace(cab.Indirizzo) OrElse String.IsNullOrWhiteSpace(percorsoDestinazione) Then Return False
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12

            Dim totale As Long = -1L
            Dim letti As Long = 0L
            Using client As HttpClient = CreaClient()
                Using risposta As HttpResponseMessage = Await client.GetAsync(cab.Indirizzo, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(False)
                    risposta.EnsureSuccessStatusCode()
                    totale = If(risposta.Content.Headers.ContentLength, -1L)
                    Using origine As Stream = Await risposta.Content.ReadAsStreamAsync().ConfigureAwait(False)
                        Using destinazione As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                            Using controllo As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)
                                Dim buffer(81919) As Byte
                                Dim ultimaPercentuale As Integer = -1
                                Do
                                    controllo.CancelAfter(StalloScaricamentoMs)
                                    Dim n As Integer = Await origine.ReadAsync(buffer, 0, buffer.Length, controllo.Token).ConfigureAwait(False)
                                    If n <= 0 Then Exit Do
                                    Await destinazione.WriteAsync(buffer, 0, n, token).ConfigureAwait(False)
                                    letti += n
                                    If totale > 0L AndAlso progresso IsNot Nothing Then
                                        Dim percentuale As Integer = CInt(Math.Min(100L, letti * 100L \ totale))
                                        If percentuale <> ultimaPercentuale Then
                                            ultimaPercentuale = percentuale
                                            progresso.Report(percentuale)
                                        End If
                                    End If
                                Loop
                            End Using
                        End Using
                    End Using
                End Using
            End Using

            If letti < DimensioneMinimaCab OrElse (totale > 0L AndAlso letti <> totale) Then
                log.ScriviLog("WGRuntimeWebView2.ScaricaCabAsync: file incompleto (" & letti & " byte su " & totale & ")")
                Return False
            End If

            If File.Exists(percorsoDestinazione) Then File.Delete(percorsoDestinazione)
            File.Move(temporaneo, percorsoDestinazione)
            Return True
        Catch ex As OperationCanceledException
            log.ScriviLog("WGRuntimeWebView2.ScaricaCabAsync: " & If(token.IsCancellationRequested, "annullato", "scaricamento bloccato, nessun dato ricevuto per " & (StalloScaricamentoMs \ 1000) & " secondi"))
            Return False
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.ScaricaCabAsync: " & ex.Message)
            Return False
        Finally
            EliminaFile(temporaneo)
        End Try
    End Function

    ''' <summary>
    ''' Estrae il .cab in cartellaDestinazione con expand.exe di Windows (che ricrea anche le sottocartelle).
    ''' Funzione bloccante: da chiamare fuori dal thread dell'interfaccia.
    ''' </summary>
    Public Shared Function EstraiCab(percorsoCab As String, cartellaDestinazione As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoCab) OrElse Not File.Exists(percorsoCab) Then Return False
            If String.IsNullOrWhiteSpace(cartellaDestinazione) Then Return False

            Dim programma As String = Path.Combine(Environment.SystemDirectory, "expand.exe")
            If Not File.Exists(programma) Then
                log.ScriviLog("WGRuntimeWebView2.EstraiCab: expand.exe non trovato in " & Environment.SystemDirectory)
                Return False
            End If

            EliminaCartella(cartellaDestinazione)
            Directory.CreateDirectory(cartellaDestinazione)

            Dim argomenti As String = """" & percorsoCab & """ -F:* """ & cartellaDestinazione.TrimEnd("\"c) & """"
            Dim informazioni As New ProcessStartInfo(programma, argomenti)
            informazioni.UseShellExecute = False
            informazioni.CreateNoWindow = True
            informazioni.WindowStyle = ProcessWindowStyle.Hidden
            informazioni.RedirectStandardOutput = True
            informazioni.RedirectStandardError = True

            Dim errori As New StringBuilder()
            Using processo As Process = Process.Start(informazioni)
                If processo Is Nothing Then Return False
                AddHandler processo.OutputDataReceived, Sub(mittente As Object, e As DataReceivedEventArgs)
                                                        End Sub
                AddHandler processo.ErrorDataReceived, Sub(mittente As Object, e As DataReceivedEventArgs)
                                                           If e IsNot Nothing AndAlso Not String.IsNullOrEmpty(e.Data) Then errori.AppendLine(e.Data)
                                                       End Sub
                processo.BeginOutputReadLine()
                processo.BeginErrorReadLine()
                If Not processo.WaitForExit(AttesaEstrazioneMs) Then
                    Try
                        processo.Kill()
                    Catch exArresto As Exception
                        log.ScriviLog("WGRuntimeWebView2.EstraiCab arresto: " & exArresto.Message)
                    End Try
                    log.ScriviLog("WGRuntimeWebView2.EstraiCab: estrazione non terminata entro il tempo previsto")
                    Return False
                End If
                processo.WaitForExit()
                If processo.ExitCode <> 0 Then
                    log.ScriviLog("WGRuntimeWebView2.EstraiCab: expand.exe terminato con codice " & processo.ExitCode & " " & errori.ToString().Trim())
                    Return False
                End If
            End Using

            If TrovaCartellaMotore(cartellaDestinazione) Is Nothing Then
                log.ScriviLog("WGRuntimeWebView2.EstraiCab: " & NomeEseguibile & " non trovato nel contenuto estratto")
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.EstraiCab: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Porta il contenuto estratto nella cartella \WebView2 della radice, in modo che msedgewebview2.exe
    ''' stia direttamente in \WebView2 (il .cab contiene una sottocartella con il nome della versione).
    ''' Il runtime precedente viene tenuto da parte e ripristinato se lo spostamento fallisce.
    ''' Fallisce se il runtime attuale e' in uso da processi msedgewebview2 attivi.
    ''' </summary>
    Public Shared Function InstallaDaStaging(cartellaStaging As String) As Boolean
        Try
            Dim sorgente As String = TrovaCartellaMotore(cartellaStaging)
            If sorgente Is Nothing Then Return False

            Dim destinazione As String = CartellaRuntime()
            Dim precedente As String = destinazione & SuffissoPrecedente
            EliminaCartella(precedente)
            If Directory.Exists(precedente) Then
                log.ScriviLog("WGRuntimeWebView2.InstallaDaStaging: impossibile rimuovere " & precedente)
                Return False
            End If

            Dim messoDaParte As Boolean = False
            If Directory.Exists(destinazione) Then
                Directory.Move(destinazione, precedente)
                messoDaParte = True
            End If

            Try
                Directory.Move(sorgente, destinazione)
            Catch exSposta As Exception
                log.ScriviLog("WGRuntimeWebView2.InstallaDaStaging spostamento: " & exSposta.Message)
                If messoDaParte Then
                    Try
                        Directory.Move(precedente, destinazione)
                    Catch exRipristino As Exception
                        log.ScriviLog("WGRuntimeWebView2.InstallaDaStaging ripristino: " & exRipristino.Message)
                    End Try
                End If
                Return False
            End Try

            EliminaCartella(precedente)
            EliminaCartella(cartellaStaging)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.InstallaDaStaging: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function TrovaCartellaMotore(radice As String, Optional profondita As Integer = 0) As String
        Try
            If String.IsNullOrEmpty(radice) OrElse Not Directory.Exists(radice) Then Return Nothing
            If File.Exists(Path.Combine(radice, NomeEseguibile)) Then Return radice
            If profondita >= ProfonditaRicercaMotore Then Return Nothing
            For Each sotto As String In Directory.GetDirectories(radice)
                Dim trovata As String = TrovaCartellaMotore(sotto, profondita + 1)
                If trovata IsNot Nothing Then Return trovata
            Next
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.TrovaCartellaMotore: " & ex.Message)
        End Try
        Return Nothing
    End Function

#End Region

#Region "Operazione completa"

    ''' <summary>True se l'applicazione puo' scrivere nella propria cartella radice (serve per creare \WebView2).</summary>
    Public Shared Function CartellaScrivibile() As Boolean
        Try
            Dim prova As String = Path.Combine(CartellaBaseApplicazione(), ".wgscrittura_" & Guid.NewGuid().ToString("N"))
            File.WriteAllText(prova, "")
            File.Delete(prova)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.CartellaScrivibile: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Se \WebView2 non contiene un runtime (o se forza = True) lo scarica dal catalogo Microsoft
    ''' per l'architettura del sistema, lo estrae e lo installa in \WebView2.
    ''' Una sola operazione alla volta: una seconda chiamata contemporanea restituisce subito InCorso = True.
    ''' Se il runtime attuale e' in uso (browser aperto) la sostituzione non e' possibile e l'esito lo indica.
    ''' </summary>
    Public Shared Async Function AssicuraRuntimeAsync(Optional forza As Boolean = False, Optional progresso As IProgress(Of Integer) = Nothing, Optional token As CancellationToken = Nothing) As Task(Of WGEsitoRuntime)
        Dim esito As New WGEsitoRuntime()
        If Not Await _semaforo.WaitAsync(0).ConfigureAwait(False) Then
            esito.InCorso = True
            esito.Descrizione = "Scaricamento del runtime WebView2 gia' in corso."
            Return esito
        End If

        Dim percorsoCab As String = Nothing
        Dim staging As String = CartellaRuntime() & SuffissoStaging
        Try
            If Not forza AndAlso RuntimePresente() Then
                esito.Riuscito = True
                esito.Versione = VersioneInstallata()
                esito.Descrizione = "Runtime WebView2 gia' presente."
                Return esito
            End If

            If Not CartellaScrivibile() Then
                esito.ServonoPermessi = True
                esito.Descrizione = "La cartella dell'applicazione non e' scrivibile: servono i permessi per creare la cartella " & CartellaRuntime() & "."
                Return esito
            End If

            Dim architettura As String = ArchitetturaRuntime()
            Dim catalogo As List(Of WGCabRuntime) = Await LeggiCatalogoAsync(token).ConfigureAwait(False)
            Dim cab As WGCabRuntime = TrovaUltimoCab(catalogo, architettura)
            If cab Is Nothing Then
                esito.Descrizione = If(catalogo.Count = 0, "Elenco dei runtime Microsoft non raggiungibile.", "Nessun runtime disponibile per l'architettura " & architettura & ".")
                Return esito
            End If

            percorsoCab = Path.Combine(Path.GetTempPath(), "wgwebview2_" & Guid.NewGuid().ToString("N") & ".cab")
            If Not Await ScaricaCabAsync(cab, percorsoCab, progresso, token).ConfigureAwait(False) Then
                esito.Descrizione = If(token.IsCancellationRequested, "Scaricamento annullato.", "Scaricamento del runtime " & cab.Versione & " (" & architettura & ") non riuscito.")
                Return esito
            End If

            Dim estratto As Boolean = Await Task.Run(Function() EstraiCab(percorsoCab, staging)).ConfigureAwait(False)
            If Not estratto Then
                esito.Descrizione = "Estrazione del runtime scaricato non riuscita."
                Return esito
            End If

            Dim installato As Boolean = Await Task.Run(Function() InstallaDaStaging(staging)).ConfigureAwait(False)
            If Not installato Then
                esito.Descrizione = "Impossibile sostituire la cartella " & CartellaRuntime() & ": probabilmente e' in uso. Chiudere il browser e riprovare."
                Return esito
            End If

            esito.Versione = VersioneInstallata()
            esito.Riuscito = RuntimePresente()
            esito.Descrizione = If(esito.Riuscito, "Runtime WebView2 " & If(esito.Versione <> "", esito.Versione, cab.Versione) & " (" & architettura & ") installato in " & CartellaRuntime() & ".", "Runtime estratto ma non utilizzabile.")
            Return esito
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.AssicuraRuntimeAsync: " & ex.ToString())
            esito.Descrizione = ex.Message
            Return esito
        Finally
            EliminaFile(percorsoCab)
            EliminaCartella(staging)
            _semaforo.Release()
        End Try
    End Function

#End Region

#Region "Flusso guidato per l'utente"

    ''' <summary>
    ''' Esegue dopo() solo se il runtime e' installato. Se manca, un WGDomanda chiede se installarlo:
    ''' con Si' avvia l'installazione con avanzamento e, a installazione riuscita, esegue dopo().
    ''' Da chiamare dal thread dell'interfaccia.
    ''' </summary>
    Public Shared Sub AssicuraRuntime(dopo As Action)
        Try
            If RuntimePresente() Then
                If dopo IsNot Nothing Then dopo()
                Return
            End If
            Dim risposto As Boolean = False
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, Sub(risposta As DialogResult)
                                                    Try
                                                        If risposto Then Return
                                                        risposto = True
                                                        If risposta = DialogResult.Yes Then InstallaConAvanzamento(dopo)
                                                    Catch exRisposta As Exception
                                                        log.ScriviLog("WGRuntimeWebView2.AssicuraRuntime risposta: " & exRisposta.ToString())
                                                    End Try
                                                End Sub
            domanda.avvia("Per aprire il browser serve Microsoft WebView2 Runtime, che non è installato. Vuoi installarlo ora?")
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.AssicuraRuntime: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Installa il runtime mostrando un WGProcessoInCorso che annuncia la percentuale ogni 5%.
    ''' A installazione riuscita esegue dopo().
    ''' </summary>
    Public Shared Async Sub InstallaConAvanzamento(Optional dopo As Action = Nothing, Optional evitaRipetizioni As Boolean = False)
        Try
            If Await InstallaConAvanzamentoAsync(evitaRipetizioni) Then
                If dopo IsNot Nothing Then dopo()
            End If
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.InstallaConAvanzamento: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Versione attendibile: restituisce True quando il runtime e' presente (gia' c'era o e' appena stato installato),
    ''' False se l'installazione non e' riuscita. Se un'installazione guidata e' gia' in corso, chi chiama
    ''' si limita ad attenderne l'esito (una sola finestra di avanzamento per volta).
    ''' Usabile da qualsiasi componente (player, motori nascosti...) prima di creare un WebView2.
    ''' </summary>
    ''' <remarks>evitaRipetizioni = True per i chiamanti automatici (motori in sottofondo): dopo un fallimento non si riprova per alcuni minuti, cosi' non si ripresentano finestre ed errori a ogni chiamata.</remarks>
    Public Shared Function InstallaConAvanzamentoAsync(Optional evitaRipetizioni As Boolean = False) As Task(Of Boolean)
        Try
            If RuntimePresente() Then Return Task.FromResult(True)
            If evitaRipetizioni AndAlso (DateTime.UtcNow - _ultimoFallimento).TotalMinutes < PausaDopoFallimentoMinuti Then Return Task.FromResult(False)
            Dim tcs As TaskCompletionSource(Of Boolean)
            SyncLock _bloccoFlusso
                If _installazioneGuidata IsNot Nothing AndAlso Not _installazioneGuidata.IsCompleted Then Return _installazioneGuidata
                tcs = New TaskCompletionSource(Of Boolean)()
                _installazioneGuidata = tcs.Task
            End SyncLock
            EseguiSuInterfaccia(Sub() EseguiInstallazioneGuidata(tcs))
            Return tcs.Task
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.InstallaConAvanzamentoAsync: " & ex.ToString())
            Return Task.FromResult(False)
        End Try
    End Function

    ''' <summary>Se chiamato da un thread senza contesto grafico, porta l'azione sul thread della prima finestra aperta.</summary>
    Private Shared Sub EseguiSuInterfaccia(azione As Action)
        Try
            If SynchronizationContext.Current Is Nothing AndAlso Application.OpenForms.Count > 0 Then
                Dim finestra As Form = Application.OpenForms(0)
                If finestra IsNot Nothing AndAlso Not finestra.IsDisposed AndAlso finestra.IsHandleCreated AndAlso finestra.InvokeRequired Then
                    finestra.BeginInvoke(azione)
                    Return
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.EseguiSuInterfaccia: " & ex.Message)
        End Try
        azione()
    End Sub

    Private Shared Async Sub EseguiInstallazioneGuidata(tcs As TaskCompletionSource(Of Boolean))
        Dim processo As WGProcessoInCorso = Nothing
        Dim riuscito As Boolean = False
        Try
            processo = New WGProcessoInCorso()
            processo.Avvia("Installazione di Microsoft WebView2 Runtime")

            Dim ultimoAnnunciato As Integer = -1
            ' Progress(Of T) richiama il gestore sul thread dell'interfaccia.
            Dim progresso As New Progress(Of Integer)(Sub(percentuale As Integer)
                                                          Try
                                                              Dim passo As Integer = (Math.Min(100, Math.Max(0, percentuale)) \ PassoPercentuale) * PassoPercentuale
                                                              If passo > ultimoAnnunciato Then
                                                                  ultimoAnnunciato = passo
                                                                  processo.AggiornaPercentuale(passo)
                                                                  If passo = 100 Then Annuncia("Scaricamento completato, installazione in corso")
                                                              End If
                                                          Catch exAgg As Exception
                                                              log.ScriviLog("WGRuntimeWebView2.EseguiInstallazioneGuidata aggiornamento: " & exAgg.Message)
                                                          End Try
                                                      End Sub)

            Dim esito As WGEsitoRuntime = Await AssicuraRuntimeAsync(False, progresso)

            Try
                processo.Fine()
            Catch exChiudi As Exception
                log.ScriviLog("WGRuntimeWebView2.EseguiInstallazioneGuidata chiusura: " & exChiudi.Message)
            End Try
            processo = Nothing

            riuscito = esito.Riuscito
            If esito.InCorso Then
                ' Un'altra parte del programma sta gia' installando il runtime: si attende la fine.
                Dim limite As DateTime = DateTime.UtcNow.AddMinutes(AttesaAltraInstallazioneMinuti)
                Do While Not RuntimePresente() AndAlso DateTime.UtcNow < limite
                    Await Task.Delay(1000)
                Loop
                riuscito = RuntimePresente()
            End If
            If riuscito Then
                Annuncia("Microsoft WebView2 Runtime installato")
            Else
                MostraMessaggio(If(String.IsNullOrWhiteSpace(esito.Descrizione), "Installazione di Microsoft WebView2 Runtime non riuscita.", esito.Descrizione))
            End If
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.EseguiInstallazioneGuidata: " & ex.ToString())
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch
            End Try
            MostraMessaggio("Installazione di Microsoft WebView2 Runtime non riuscita.")
        Finally
            If Not riuscito Then _ultimoFallimento = DateTime.UtcNow
            tcs.TrySetResult(riuscito)
        End Try
    End Sub

    Private Shared Sub Annuncia(testo As String)
        Try
            sintesivocale.GetInstance().LeggiTesto(testo)
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.Annuncia: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.Show()
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Utilita'"

    Private Shared Sub EliminaFile(percorso As String)
        Try
            If Not String.IsNullOrEmpty(percorso) AndAlso File.Exists(percorso) Then File.Delete(percorso)
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.EliminaFile: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub EliminaCartella(percorso As String)
        Try
            If Not String.IsNullOrEmpty(percorso) AndAlso Directory.Exists(percorso) Then Directory.Delete(percorso, True)
        Catch ex As Exception
            log.ScriviLog("WGRuntimeWebView2.EliminaCartella: " & ex.Message)
        End Try
    End Sub

#End Region
End Class

Public NotInheritable Class WGCabRuntime
    Public Property Versione As String = ""
    Public Property Architettura As String = ""
    Public Property Indirizzo As String = ""
End Class

Public NotInheritable Class WGEsitoRuntime
    Public Property Riuscito As Boolean
    Public Property InCorso As Boolean
    Public Property ServonoPermessi As Boolean
    Public Property Versione As String = ""
    Public Property Descrizione As String = ""
End Class