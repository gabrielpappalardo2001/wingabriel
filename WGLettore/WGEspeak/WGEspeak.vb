' ============================================================================================
' WGEspeak - eSpeak NG come voce del lettore di schermo: scaricamento, installazione e motore
' ============================================================================================
'
' Libreria a parte, ricopiata da quella di WinGabriel ma indipendente da wgcontrols: eSpeak NG e' una sintesi vocale
' libera e leggerissima, che parte subito e si puo' velocizzare moltissimo. Il pacchetto ufficiale si scarica, se ne
' estraggono i file in una cartella (senza installare niente nel sistema) e si chiama la sua libreria; l'audio lo
' suona questa stessa libreria (cartella Audio, copia di WGUscitaDiretta).
'
'   WGEspeak.Archivio = New MioArchivio()      ' dove si scrivono le poche impostazioni (versione, ultimo tentativo)
'   WGEspeak.Installato()                      ' i file ci sono?
'   Await WGEspeak.InstallaAsync(progresso, token)
'   WGMotoreEspeak.Istanza()                   ' il motore: Voci, ImpostaVoce, ImpostaVelocita, Accoda, Ferma
' ============================================================================================

Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class WGEspeak

    Private Shared ReadOnly log As New Logger()
    Private Shared _archivio As IArchivioEspeak = New ArchivioInMemoria()
    Private Shared _cartellaBase As String = ""

    ' Dove si scrivono le poche impostazioni della libreria. Il programma ospite ci mette il suo archivio.
    Public Shared Property Archivio As IArchivioEspeak
        Get
            Return _archivio
        End Get
        Set(value As IArchivioEspeak)
            _archivio = If(value, New ArchivioInMemoria())
        End Set
    End Property

    ' La cartella in cui stanno la libreria e i dati di eSpeak. Se non e' scelta: "espeak" accanto al programma, o nei dati dell'utente se li' non si puo' scrivere.
    Public Shared Property CartellaBase As String
        Get
            Return _cartellaBase
        End Get
        Set(value As String)
            _cartellaBase = If(value, "")
        End Set
    End Property

    Private Const UrlUltimaVersioneApi As String = "https://api.github.com/repos/espeak-ng/espeak-ng/releases/latest"
    Private Const UrlUltimaVersionePagina As String = "https://github.com/espeak-ng/espeak-ng/releases/latest"
    Private Const UrlBaseScaricamento As String = "https://github.com/espeak-ng/espeak-ng/releases/download/"
    Private Const NomePacchettoPredefinito As String = "espeak-ng.msi"
    Private Const VersioneNota As String = "1.52.0"
    Private Const ImprontaVersioneNota As String = "7F673C709EA5DD579D3B5EBB98688CC575328A6AB7438D2BC405B88CEDAEAFB9"
    Private Const DimensionePrevista As Long = 12765862
    Private Const DimensioneMinima As Long = 1000000
    Private Const ChiaveVersione As String = "EspeakVersione"
    Private Const ChiaveMotore As String = "MotoreVocale"
    Private Const ChiaveUltimoTentativo As String = "EspeakUltimoTentativo"
    Private Const OreTraDueTentativi As Integer = 12
    Private Const PassoPercentuale As Integer = 10

    Private Sub New()
    End Sub

#Region "Dove sta e che motore è scelto"

    ''' <summary>La cartella con la libreria e i dati di eSpeak, nella radice dell'applicazione (non nei dati).</summary>
    Public Shared Function Cartella() As String
        Return RisolviCartella()
    End Function

    Friend Shared Function PercorsoLibreria() As String
        Return Path.Combine(Cartella(), "libespeak-ng.dll")
    End Function

    ''' <summary>Vero se i file di eSpeak ci sono tutti (la libreria e i dati dell'italiano).</summary>
    Public Shared Function Installato() As Boolean
        Try
            Return File.Exists(PercorsoLibreria()) AndAlso File.Exists(Path.Combine(Cartella(), "espeak-ng-data", "it_dict"))
        Catch ex As Exception
            log.ScriviLog("WGEspeak.Installato: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function VersioneInstallata() As String
        Try
            If Not Installato() Then Return ""
            Return If(Archivio.Leggi(ChiaveVersione), "").Replace(vbCr, "").Replace(vbLf, "").Trim()
        Catch ex As Exception
            log.ScriviLog("WGEspeak.VersioneInstallata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Archivio.Scrivi(chiave, valore)
        Catch ex As Exception
            log.ScriviLog("WGEspeak.Scrivi " & chiave & ": " & ex.Message)
        End Try
    End Sub

    Private Shared ReadOnly _cartelleCercate As New List(Of String)()

    ''' <summary>
    ''' Un'altra cartella dove cercare eSpeak già installato (per esempio quella di un programma che lo usa già): se i file ci sono tutti,
    ''' si usano quelli e non serve scaricare niente.
    ''' </summary>
    Public Shared Sub CercaAnche(cartella As String)
        Try
            If String.IsNullOrWhiteSpace(cartella) Then Return
            SyncLock _cartelleCercate
                For Each presente As String In _cartelleCercate
                    If String.Equals(presente, cartella, StringComparison.OrdinalIgnoreCase) Then Return
                Next
                _cartelleCercate.Add(cartella)
            End SyncLock
            _cartellaBase = ""
        Catch ex As Exception
            log.ScriviLog("WGEspeak.CercaAnche: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CartellaCompleta(cartella As String) As Boolean
        Try
            Return File.Exists(Path.Combine(cartella, "libespeak-ng.dll")) AndAlso File.Exists(Path.Combine(cartella, "espeak-ng-data", "it_dict"))
        Catch exAtteso As Exception
            Return False
        End Try
    End Function

    Private Shared Function RisolviCartella() As String
        Try
            If _cartellaBase <> "" Then Return _cartellaBase
            Dim accanto As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "espeak")
            Dim locale As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WGLettore", "espeak")
            Dim candidate As New List(Of String)()
            candidate.Add(accanto)
            candidate.Add(locale)
            SyncLock _cartelleCercate
                candidate.AddRange(_cartelleCercate)
            End SyncLock
            For Each cartella As String In candidate
                If CartellaCompleta(cartella) Then
                    _cartellaBase = cartella
                    Return _cartellaBase
                End If
            Next
            ' Non c'è da nessuna parte: è la cartella dove si scarica.
            If PuoScrivere(accanto) Then
                _cartellaBase = accanto
            Else
                _cartellaBase = locale
            End If
            Return _cartellaBase
        Catch ex As Exception
            log.ScriviLog("WGEspeak.RisolviCartella: " & ex.Message)
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "espeak")
        End Try
    End Function

    Private Shared Function PuoScrivere(cartella As String) As Boolean
        Try
            If File.Exists(Path.Combine(cartella, "libespeak-ng.dll")) Then Return True
            Directory.CreateDirectory(cartella)
            Dim prova As String = Path.Combine(cartella, ".prova" & Guid.NewGuid().ToString("N"))
            File.WriteAllText(prova, "")
            File.Delete(prova)
            Return True
        Catch exAtteso As Exception
            Return False
        End Try
    End Function


#End Region


#Region "Scaricamento e installazione"

    Public Shared Async Function InstallaAsync(progresso As IProgress(Of Integer), token As CancellationToken) As Task(Of String)
        Dim lavoro As String = Path.Combine(Path.GetTempPath(), "WGLettore-espeak-" & Guid.NewGuid().ToString("N"))
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Directory.CreateDirectory(lavoro)
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(15)
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WGLettore")
                Dim pacchetto As PacchettoEspeak = Await RisolviPacchettoAsync(client, token)
                Dim fileMsi As String = Path.Combine(lavoro, "espeak-ng.msi")
                Dim errore As String = Await ScaricaAsync(client, pacchetto, fileMsi, progresso, token)
                If errore <> "" Then Return errore
                errore = ControllaPacchetto(pacchetto, fileMsi)
                If errore <> "" Then Return errore
                Dim estratto As String = Path.Combine(lavoro, "estratto")
                errore = Await EstraiAsync(fileMsi, estratto, token)
                If errore <> "" Then Return errore
                Dim sorgente As String = Path.Combine(estratto, "eSpeak NG")
                If Not File.Exists(Path.Combine(sorgente, "libespeak-ng.dll")) OrElse Not File.Exists(Path.Combine(sorgente, "espeak-ng-data", "it_dict")) Then
                    Return "nel pacchetto mancano dei file"
                End If
                errore = Await Task.Factory.StartNew(Of String)(New Func(Of Object, String)(AddressOf MettiAlPostoDaStato), sorgente)
                If errore = "" Then Scrivi(ChiaveVersione, pacchetto.Versione)
                Return errore
            End Using
        Catch ex As Exception
            log.ScriviLog("WGEspeak.InstallaAsync: " & ex.ToString())
            Return "errore imprevisto, " & ex.Message
        Finally
            Try
                If Directory.Exists(lavoro) Then Directory.Delete(lavoro, True)
            Catch exPulizia As Exception
                log.ScriviLog("WGEspeak.InstallaAsync pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Function

    Private Shared Function MettiAlPostoDaStato(stato As Object) As String
        Try
            Return MettiAlPosto(CStr(stato))
        Catch ex As Exception
            log.ScriviLog("WGEspeak.MettiAlPostoDaStato: " & ex.Message)
            Return "non riesco a copiare i file nella cartella di eSpeak"
        End Try
    End Function

    Private Shared Async Function RisolviPacchettoAsync(client As HttpClient, token As CancellationToken) As Task(Of PacchettoEspeak)
        Try
            Dim daApi As PacchettoEspeak = Await PacchettoDaApiAsync(client, token)
            If daApi IsNot Nothing Then Return daApi
            Dim daPagina As PacchettoEspeak = Await PacchettoDaPaginaAsync(client, token)
            If daPagina IsNot Nothing Then Return daPagina
        Catch ex As Exception
            log.ScriviLog("WGEspeak.RisolviPacchettoAsync: " & ex.Message)
        End Try
        Return PacchettoVersioneNota()
    End Function

    Private Shared Function PacchettoVersioneNota() As PacchettoEspeak
        Try
            Return New PacchettoEspeak() With {
                .Versione = VersioneNota,
                .Url = UrlBaseScaricamento & VersioneNota & "/" & NomePacchettoPredefinito,
                .Impronta = ImprontaVersioneNota,
                .Dimensione = DimensionePrevista}
        Catch ex As Exception
            log.ScriviLog("WGEspeak.PacchettoVersioneNota: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Async Function PacchettoDaApiAsync(client As HttpClient, token As CancellationToken) As Task(Of PacchettoEspeak)
        Try
            Using richiesta As New HttpRequestMessage(HttpMethod.Get, UrlUltimaVersioneApi)
                richiesta.Headers.Accept.ParseAdd("application/vnd.github+json")
                Using risposta As HttpResponseMessage = Await client.SendAsync(richiesta, token)
                    If Not risposta.IsSuccessStatusCode Then Return Nothing
                    Dim json As String = Await risposta.Content.ReadAsStringAsync()
                    Dim versione As String = ValoreJson(json, "tag_name")
                    If Not VersioneValida(versione) Then Return Nothing
                    Dim migliore As PacchettoEspeak = Nothing
                    Dim punteggioMigliore As Integer = -1
                    Dim indirizzi As MatchCollection = Regex.Matches(json, """browser_download_url""\s*:\s*""([^""]+)""")
                    Dim inizioSegmento As Integer = 0
                    For Each indirizzo As Match In indirizzi
                        Dim segmento As String = json.Substring(inizioSegmento, indirizzo.Index - inizioSegmento)
                        inizioSegmento = indirizzo.Index + indirizzo.Length
                        Dim url As String = indirizzo.Groups(1).Value
                        If Not url.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) Then Continue For
                        If Not url.StartsWith("https://github.com/espeak-ng/espeak-ng/releases/download/", StringComparison.OrdinalIgnoreCase) Then Continue For
                        Dim punteggio As Integer = PunteggioPacchetto(url)
                        If punteggio <= punteggioMigliore Then Continue For
                        Dim dimensione As Long = 0
                        Dim trovataDimensione As Match = Regex.Match(segmento, """size""\s*:\s*(\d+)")
                        If trovataDimensione.Success Then Long.TryParse(trovataDimensione.Groups(1).Value, dimensione)
                        Dim impronta As String = ""
                        Dim trovataImpronta As Match = Regex.Match(segmento, """digest""\s*:\s*""sha256:([0-9a-fA-F]{64})""")
                        If trovataImpronta.Success Then impronta = trovataImpronta.Groups(1).Value.ToUpperInvariant()
                        If impronta = "" AndAlso versione = VersioneNota Then impronta = ImprontaVersioneNota
                        migliore = New PacchettoEspeak() With {.Versione = versione, .Url = url, .Impronta = impronta, .Dimensione = dimensione}
                        punteggioMigliore = punteggio
                    Next
                    Return migliore
                End Using
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            log.ScriviLog("WGEspeak.PacchettoDaApiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Async Function PacchettoDaPaginaAsync(client As HttpClient, token As CancellationToken) As Task(Of PacchettoEspeak)
        Try
            Using risposta As HttpResponseMessage = Await client.GetAsync(UrlUltimaVersionePagina, HttpCompletionOption.ResponseHeadersRead, token)
                If Not risposta.IsSuccessStatusCode OrElse risposta.RequestMessage Is Nothing OrElse risposta.RequestMessage.RequestUri Is Nothing Then Return Nothing
                Dim percorso As String = risposta.RequestMessage.RequestUri.AbsolutePath
                Dim posizione As Integer = percorso.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase)
                If posizione < 0 Then Return Nothing
                Dim versione As String = Uri.UnescapeDataString(percorso.Substring(posizione + 5)).Trim("/"c)
                If Not VersioneValida(versione) Then Return Nothing
                Return New PacchettoEspeak() With {
                    .Versione = versione,
                    .Url = UrlBaseScaricamento & Uri.EscapeDataString(versione) & "/" & NomePacchettoPredefinito,
                    .Impronta = If(versione = VersioneNota, ImprontaVersioneNota, ""),
                    .Dimensione = 0}
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            log.ScriviLog("WGEspeak.PacchettoDaPaginaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function PunteggioPacchetto(url As String) As Integer
        Try
            Dim nome As String = Path.GetFileName(New Uri(url).AbsolutePath).ToLowerInvariant()
            Dim a64 As Boolean = nome.Contains("x64") OrElse nome.Contains("amd64") OrElse nome.Contains("win64")
            Dim a32 As Boolean = nome.Contains("x86") OrElse nome.Contains("win32") OrElse nome.Contains("i686")
            Dim arm As Boolean = nome.Contains("arm")
            If arm Then Return 0
            If Environment.Is64BitProcess Then
                If a64 Then Return 3
                If nome = NomePacchettoPredefinito Then Return 2
                If Not a32 Then Return 1
                Return 0
            End If
            If a32 Then Return 3
            If nome = NomePacchettoPredefinito Then Return 2
            If Not a64 Then Return 1
            Return 0
        Catch ex As Exception
            log.ScriviLog("WGEspeak.PunteggioPacchetto: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function ValoreJson(json As String, chiave As String) As String
        Try
            Dim trovato As Match = Regex.Match(json, """" & Regex.Escape(chiave) & """\s*:\s*""([^""]*)""")
            If trovato.Success Then Return trovato.Groups(1).Value.Trim()
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGEspeak.ValoreJson: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function VersioneValida(versione As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(versione) OrElse versione.Length > 40 Then Return False
            Return Regex.IsMatch(versione, "^[0-9][0-9A-Za-z._-]*$")
        Catch ex As Exception
            log.ScriviLog("WGEspeak.VersioneValida: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ControllaPacchetto(pacchetto As PacchettoEspeak, percorso As String) As String
        Try
            Dim informazioni As New FileInfo(percorso)
            If Not informazioni.Exists OrElse informazioni.Length < DimensioneMinima Then Return "il file scaricato non è quello previsto"
            If pacchetto.Dimensione > 0 AndAlso informazioni.Length <> pacchetto.Dimensione Then Return "il file scaricato è incompleto"
            If pacchetto.Impronta <> "" Then
                If Not String.Equals(ImprontaDi(percorso), pacchetto.Impronta, StringComparison.OrdinalIgnoreCase) Then Return "il file scaricato non è quello previsto"
                Return ""
            End If
            Dim firma As Byte() = {&HD0, &HCF, &H11, &HE0, &HA1, &HB1, &H1A, &HE1}
            Dim inizio(7) As Byte
            Using file As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read)
                If file.Read(inizio, 0, 8) <> 8 Then Return "il file scaricato non è quello previsto"
            End Using
            For indice As Integer = 0 To 7
                If inizio(indice) <> firma(indice) Then Return "il file scaricato non è quello previsto"
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGEspeak.ControllaPacchetto: " & ex.Message)
            Return "non riesco a controllare il file scaricato"
        End Try
    End Function

    Private Shared Async Function ScaricaAsync(client As HttpClient, pacchetto As PacchettoEspeak, destinazione As String, progresso As IProgress(Of Integer), token As CancellationToken) As Task(Of String)
        Try
            If pacchetto Is Nothing Then Return "non trovo il pacchetto di eSpeak sul sito ufficiale"
            Using risposta As HttpResponseMessage = Await client.GetAsync(pacchetto.Url, HttpCompletionOption.ResponseHeadersRead, token)
                If Not risposta.IsSuccessStatusCode Then Return "il sito ha risposto con l'errore " & CInt(risposta.StatusCode).ToString()
                Dim totale As Long = DimensionePrevista
                If risposta.Content.Headers.ContentLength.HasValue Then
                    totale = risposta.Content.Headers.ContentLength.Value
                ElseIf pacchetto.Dimensione > 0 Then
                    totale = pacchetto.Dimensione
                End If
                Using origine As Stream = Await risposta.Content.ReadAsStreamAsync()
                    Using file As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                        Dim buffer(81919) As Byte
                        Dim letti As Long = 0
                        Dim n As Integer = Await origine.ReadAsync(buffer, 0, buffer.Length, token)
                        While n > 0
                            Await file.WriteAsync(buffer, 0, n, token)
                            letti += n
                            If progresso IsNot Nothing AndAlso totale > 0 Then progresso.Report(CInt(Math.Min(99L, letti * 100L \ totale)))
                            n = Await origine.ReadAsync(buffer, 0, buffer.Length, token)
                        End While
                    End Using
                End Using
            End Using
            If progresso IsNot Nothing Then progresso.Report(100)
            Return ""
        Catch ex As OperationCanceledException
            Return "operazione annullata"
        Catch ex As Exception
            log.ScriviLog("WGEspeak.ScaricaAsync: " & ex.Message)
            Return "non riesco a scaricare il file, controlla la connessione a internet"
        End Try
    End Function

    Private Shared Function ImprontaDi(percorso As String) As String
        Using file As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read)
            Using sha As SHA256 = SHA256.Create()
                Return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "")
            End Using
        End Using
    End Function

    ' Estrae i file del pacchetto senza installarlo: l'installazione "amministrativa" di Windows Installer
    ' (msiexec /a) li copia in una cartella qualsiasi, senza toccare il sistema e senza chiedere permessi.
    Private Shared Async Function EstraiAsync(pacchetto As String, destinazione As String, token As CancellationToken) As Task(Of String)
        Try
            Directory.CreateDirectory(destinazione)
            Dim avvio As New ProcessStartInfo() With {
                .FileName = "msiexec.exe",
                .Arguments = "/a """ & pacchetto & """ /qn TARGETDIR=""" & destinazione & """",
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
            Using processo As Process = Process.Start(avvio)
                If processo Is Nothing Then Return "non riesco ad aprire il programma che estrae i file"
                Dim finito As Boolean = Await Task.Run(Function() processo.WaitForExit(240000))
                If Not finito Then
                    Try
                        processo.Kill()
                    Catch exKill As Exception
                        log.ScriviLog("WGEspeak.EstraiAsync kill: " & exKill.Message)
                    End Try
                    Return "l'estrazione dei file ci mette troppo"
                End If
                If processo.ExitCode <> 0 AndAlso processo.ExitCode <> 3010 Then Return "l'estrazione dei file non è riuscita, codice " & processo.ExitCode.ToString()
            End Using
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGEspeak.EstraiAsync: " & ex.Message)
            Return "l'estrazione dei file non è riuscita"
        End Try
    End Function

    ' Copia in una cartella d'appoggio e poi scambia: se qualcosa va storto non resta una installazione a metà.
    Private Shared Function MettiAlPosto(sorgente As String) As String
        Try
            Dim destinazione As String = Cartella()
            Dim nuova As String = destinazione & ".nuova"
            Dim vecchia As String = destinazione & ".vecchia"
            Directory.CreateDirectory(Path.GetDirectoryName(destinazione))
            If Directory.Exists(nuova) Then Directory.Delete(nuova, True)
            If Directory.Exists(vecchia) Then Directory.Delete(vecchia, True)
            CopiaCartella(sorgente, nuova)
            If Directory.Exists(destinazione) Then Directory.Move(destinazione, vecchia)
            Directory.Move(nuova, destinazione)
            If Directory.Exists(vecchia) Then
                Try
                    Directory.Delete(vecchia, True)
                Catch exVecchia As Exception
                    log.ScriviLog("WGEspeak.MettiAlPosto vecchia: " & exVecchia.Message)
                End Try
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGEspeak.MettiAlPosto: " & ex.Message)
            Return "non riesco a copiare i file nella cartella di eSpeak"
        End Try
    End Function

    Private Shared Sub CopiaCartella(origine As String, destinazione As String)
        Directory.CreateDirectory(destinazione)
        For Each file As String In Directory.GetFiles(origine)
            System.IO.File.Copy(file, Path.Combine(destinazione, Path.GetFileName(file)), True)
        Next
        For Each cartella As String In Directory.GetDirectories(origine)
            CopiaCartella(cartella, Path.Combine(destinazione, Path.GetFileName(cartella)))
        Next
    End Sub

#End Region

End Class

Friend Class PacchettoEspeak
    Public Versione As String = ""
    Public Url As String = ""
    Public Impronta As String = ""
    Public Dimensione As Long = 0
End Class

''' <summary>Una voce di eSpeak: il nome che si legge e quello che si passa alla libreria.</summary>
Public Class VoceEspeak
    Public Nome As String
    Public Identificatore As String
End Class

''' <summary>
''' Il motore di eSpeak dentro WinGabriel: chiama la libreria libespeak-ng.dll, prende i campioni audio e li
''' suona. Parla in un thread suo: il testo si accoda (Accoda), un messaggio nuovo interrompe quello prima
''' come fanno le altre voci, Ferma taglia tutto. Quando un messaggio è finito di suonare solleva FineMessaggio.
''' </summary>
Public NotInheritable Class WGMotoreEspeak

    Private Shared ReadOnly log As New Logger()
    Private Shared _istanza As WGMotoreEspeak
    Private Shared ReadOnly _bloccoIstanza As New Object()

    Private Delegate Function RichiamoSintesi(campioni As IntPtr, numero As Integer, eventi As IntPtr) As Integer

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryW(percorso As String) As IntPtr
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_Initialize(uscita As Integer, lunghezzaBuffer As Integer, <MarshalAs(UnmanagedType.LPStr)> percorso As String, opzioni As Integer) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub espeak_SetSynthCallback(richiamo As RichiamoSintesi)
    End Sub

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_SetVoiceByName(<MarshalAs(UnmanagedType.LPStr)> nome As String) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_SetParameter(parametro As Integer, valore As Integer, relativo As Integer) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_Synth(testo As Byte(), dimensione As UIntPtr, posizione As UInteger, tipoPosizione As Integer, fine As UInteger, opzioni As UInteger, identificativo As IntPtr, datiUtente As IntPtr) As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_Cancel() As Integer
    End Function

    <DllImport("libespeak-ng.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function espeak_ListVoices(filtro As IntPtr) As IntPtr
    End Function

    ' Un evento di sintesi di espeak-ng (espeak_EVENT di speak_lib.h): l'unione finale, che in C e'
    ' union { int number; int id[2]; }, occupa otto byte e qui diventa una coppia di interi.
    <StructLayout(LayoutKind.Sequential)>
    Private Structure EventoSintesi
        Public Tipo As Integer
        Public Identificativo As UInteger
        Public PosizioneTesto As Integer
        Public Lunghezza As Integer
        Public PosizioneAudio As Integer
        Public Campione As Integer
        Public DatiUtente As IntPtr
        Public Numero As Integer
        Public IdSecondo As Integer
    End Structure

    ' I tipi di evento di espeak-ng (espeak_EVENT_TYPE di speak_lib.h).
    Private Const EventoElencoTerminato As Integer = 0
    Private Const EventoParola As Integer = 1

    <StructLayout(LayoutKind.Sequential)>
    Private Structure VoceNativa
        Public Nome As IntPtr
        Public Lingue As IntPtr
        Public Identificatore As IntPtr
        Public Genere As Byte
        Public Eta As Byte
        Public Variante As Byte
        Public Riservato As Byte
        Public Punteggio As Integer
        Public Riserva As IntPtr
    End Structure

    Private Const UscitaSincrona As Integer = 2
    Private Const NonUscireMai As Integer = &H8000
    Private Const ParametroVelocita As Integer = 1
    Private Const ParametroVolume As Integer = 2
    Private Const TestoUtf8 As UInteger = 1UI
    Private Const PosizioneInCaratteri As Integer = 1
    ' Blocchi da 60 millisecondi, la misura di eSpeak. Con blocchi da pochi millisecondi il motore taglia
    ' le parole e ripete la sillaba a cavallo dei due pezzi: la prima sillaba arriva comunque subito.
    Private Const BloccoSintesiMs As Integer = 60
    Private Const SogliaSilenzio As Integer = 32
    ' Non si prepara troppo audio in anticipo: uno stop o una frase nuova buttano poco, e la coda non allunga l'attesa.
    Private Const MillisecondiInCodaMassimi As Integer = 200

    Private Class Richiesta
        Public MessaggioId As Integer
        Public Ultimo As Boolean
        Public Testo As String
        Public Generazione As Integer
    End Class

    Private ReadOnly _coda As New System.Collections.Concurrent.BlockingCollection(Of Richiesta)()
    Private ReadOnly _bloccoAccodamento As New Object()
    Private ReadOnly _bloccoUscita As New Object()
    Private _avviato As Boolean
    Private _frequenza As Integer = 22050
    Private _richiamo As RichiamoSintesi
    Private _thread As Thread
    Private _generazione As Integer
    Private _generazioneInCorso As Integer
    ' 1 durante il riscaldamento: la sintesi gira (così la voce si carica) ma i campioni non si suonano.
    Private _scarta As Integer
    Private _ultimoMessaggio As Integer = -1
    Private _sintetizzando As Boolean
    Private _tickAccodamento As Integer
    Private _latenzaMs As Integer = -1
    Private _primoCampioneInviato As Boolean
    ' Vero se l'ultima sintesi è stata interrotta: prima della frase nuova il motore va azzerato da questo thread.
    Private _sintesiInterrotta As Boolean
    ' Il messaggio in sintesi e la posizione in caratteri dell'ultima parola sintetizzata.
    Private _messaggioInCorsoId As Integer = -1
    Private _posizioneParola As Integer

    ' L'uscita audio è la libreria WGUscitaDiretta: WASAPI a bassa latenza, WASAPI o le vecchie API secondo il sistema.
    Private _uscita As UscitaAudio
    Private _generazioneDaTagliare As Integer = -1

    Private _voci As New List(Of VoceEspeak)()
    Private _voceDaUsare As String = "it"
    Private _velocitaDaUsare As Integer = 175
    Private _volumeDaUsare As Integer = 100
    Private _impostazioniCambiate As Boolean = True

    ' Una sola sintesi alla volta dentro la libreria: il parlato e i file mp3 si alternano, non si sovrappongono.
    Private ReadOnly _bloccoLibreria As New Object()
    ' Se c'è, i campioni della libreria non si suonano ma vanno qui (sintesi per i file mp3).
    Private _raccolta As Stream
    Private _annullaRaccolta As Func(Of Boolean)

    ''' <summary>Un messaggio è stato suonato fino in fondo (non se è stato interrotto).</summary>
    Public Event FineMessaggio(messaggioId As Integer)

    ''' <summary>Il motore sta lavorando: serve a chi aspetta la fine per non credere che sia bloccato.</summary>
    Public Event Attivita()

    ''' <summary>
    ''' Una parola e' stata sintetizzata: posizione in caratteri dall'inizio del testo del messaggio e lunghezza.
    ''' Così chi ascolta sa dove sta la voce, come con gli eventi WORD del canale SAPI.
    ''' </summary>
    Public Event ParolaLetta(messaggioId As Integer, posizione As Integer, lunghezza As Integer)

    ''' <summary>Il motore pronto all'uso, oppure Nothing se la libreria non parte.</summary>
    Public Shared Function Istanza() As WGMotoreEspeak
        SyncLock _bloccoIstanza
            If _istanza Is Nothing Then
                Dim nuovo As New WGMotoreEspeak()
                If nuovo.Avvia() Then _istanza = nuovo
            End If
            Return _istanza
        End SyncLock
    End Function

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Quanto è passato, dall'ultimo messaggio nuovo accodato al primo campione audio messo nell'uscita,
    ''' in millisecondi (-1 se non si sa). Dopo c'è solo il ritardo dell'uscita (vedi NomeUscita e LatenzaUscitaMs).
    ''' </summary>
    Public ReadOnly Property LatenzaUltimoMessaggioMs As Integer
        Get
            Return _latenzaMs
        End Get
    End Property

    Private Function Avvia() As Boolean
        Try
            If _avviato Then Return True
            If Not WGEspeak.Installato() Then Return False
            Dim cartella As String = WGEspeak.Cartella()
            If LoadLibraryW(WGEspeak.PercorsoLibreria()) = IntPtr.Zero Then
                log.ScriviLog("WGMotoreEspeak.Avvia: la libreria non si carica, errore " & Marshal.GetLastWin32Error().ToString())
                Return False
            End If
            Dim frequenza As Integer = espeak_Initialize(UscitaSincrona, BloccoSintesiMs, cartella, NonUscireMai)
            If frequenza <= 0 Then
                log.ScriviLog("WGMotoreEspeak.Avvia: espeak_Initialize ha risposto " & frequenza.ToString())
                Return False
            End If
            _frequenza = frequenza
            _richiamo = AddressOf AlCampione
            espeak_SetSynthCallback(_richiamo)
            CaricaVoci()
            _thread = New Thread(AddressOf CicloLavoro) With {.IsBackground = True, .Name = "WGMotoreEspeak", .Priority = ThreadPriority.Highest}
            _thread.Start()
            _avviato = True
            Return True
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.Avvia: " & ex.ToString())
            Return False
        End Try
    End Function

#Region "Voci"

    ' Prima le varianti dell'italiano, poi le altre lingue in ordine alfabetico. Le voci "mbrola" non ci sono:
    ' hanno bisogno di file a parte.
    Private Sub CaricaVoci()
        Try
            _voci.Clear()
            For Each coppia As KeyValuePair(Of String, String) In New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("Italiano", "it"),
                New KeyValuePair(Of String, String)("Italiano, maschile 2", "it+m2"),
                New KeyValuePair(Of String, String)("Italiano, maschile 3", "it+m3"),
                New KeyValuePair(Of String, String)("Italiano, maschile 4", "it+m4"),
                New KeyValuePair(Of String, String)("Italiano, maschile 5", "it+m5"),
                New KeyValuePair(Of String, String)("Italiano, femminile 1", "it+f1"),
                New KeyValuePair(Of String, String)("Italiano, femminile 2", "it+f2"),
                New KeyValuePair(Of String, String)("Italiano, femminile 3", "it+f3"),
                New KeyValuePair(Of String, String)("Italiano, femminile 4", "it+f4"),
                New KeyValuePair(Of String, String)("Italiano, femminile 5", "it+f5"),
                New KeyValuePair(Of String, String)("Italiano, rauca", "it+croak"),
                New KeyValuePair(Of String, String)("Italiano, sussurrata", "it+whisper")}
                _voci.Add(New VoceEspeak() With {.Nome = coppia.Key, .Identificatore = coppia.Value})
            Next

            Dim altre As New List(Of VoceEspeak)()
            Dim elenco As IntPtr = espeak_ListVoices(IntPtr.Zero)
            If elenco <> IntPtr.Zero Then
                For i As Integer = 0 To 1000
                    Dim puntatore As IntPtr = Marshal.ReadIntPtr(elenco, i * IntPtr.Size)
                    If puntatore = IntPtr.Zero Then Exit For
                    Dim voce As VoceNativa = CType(Marshal.PtrToStructure(puntatore, GetType(VoceNativa)), VoceNativa)
                    Dim identificatore As String = If(voce.Identificatore = IntPtr.Zero, "", Marshal.PtrToStringAnsi(voce.Identificatore))
                    Dim nome As String = If(voce.Nome = IntPtr.Zero, "", Marshal.PtrToStringAnsi(voce.Nome))
                    If identificatore = "" OrElse nome = "" Then Continue For
                    If identificatore.StartsWith("mb\", StringComparison.OrdinalIgnoreCase) OrElse identificatore.StartsWith("mb/", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If identificatore.Replace("\"c, "/"c) = "roa/it" Then Continue For
                    altre.Add(New VoceEspeak() With {.Nome = nome.Replace("_"c, " "c), .Identificatore = identificatore.Replace("\"c, "/"c)})
                Next
            End If
            _voci.AddRange(altre.OrderBy(Function(v) v.Nome, StringComparer.CurrentCultureIgnoreCase))
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.CaricaVoci: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Le voci, nell'ordine: il numero di posizione è l'identificativo che usa il resto del programma.</summary>
    Public Function Voci() As List(Of VoceEspeak)
        Return New List(Of VoceEspeak)(_voci)
    End Function

    ''' <summary>Sceglie la voce dal suo numero di posizione nell'elenco.</summary>
    Public Sub ImpostaVoce(indice As String)
        Try
            Dim n As Integer
            If Not Integer.TryParse(If(indice, "").Trim(), n) OrElse n < 0 OrElse n >= _voci.Count Then Return
            SyncLock _bloccoAccodamento
                _voceDaUsare = _voci(n).Identificatore
                _impostazioniCambiate = True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.ImpostaVoce: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dalla velocità da 0 a 100 alle parole al minuto di eSpeak (50 sono 175).</summary>
    Public Shared Function ParoleDaVelocita(valore As Integer) As Integer
        Dim v As Integer = Math.Max(0, Math.Min(100, valore))
        Return If(v <= 50, 80 + CInt(v / 50.0 * 95), 175 + CInt((v - 50) / 50.0 * 275))
    End Function

    ''' <summary>Dal volume da 0 a 100 all'ampiezza di eSpeak (80 è quella normale).</summary>
    Public Shared Function AmpiezzaDaVolume(valore As Integer) As Integer
        Dim v As Integer = Math.Max(0, Math.Min(100, valore))
        Return CInt(v * 1.25)
    End Function

    ''' <summary>Velocità da 0 a 100 come per le altre voci: 50 sono le 175 parole al minuto di eSpeak.</summary>
    Public Sub ImpostaVelocita(valore As Integer)
        Dim parole As Integer = ParoleDaVelocita(valore)
        SyncLock _bloccoAccodamento
            _velocitaDaUsare = parole
            _impostazioniCambiate = True
        End SyncLock
    End Sub

    ''' <summary>Volume da 0 a 100 come per le altre voci (80 è l'ampiezza normale di eSpeak).</summary>
    Public Sub ImpostaVolume(valore As Integer)
        Dim ampiezza As Integer = AmpiezzaDaVolume(valore)
        SyncLock _bloccoAccodamento
            _volumeDaUsare = ampiezza
            _impostazioniCambiate = True
        End SyncLock
    End Sub

#End Region

#Region "Parlare"

    ''' <summary>
    ''' Mette il testo in coda. Un messaggio nuovo (numero diverso dal precedente) interrompe subito quello
    ''' che stava parlando, le parti dello stesso messaggio si accodano. Con ultimo a vero, finito di suonare
    ''' il messaggio si solleva FineMessaggio.
    ''' </summary>
    Public Sub Accoda(messaggioId As Integer, ultimo As Boolean, testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) AndAlso Not ultimo Then Return
            SyncLock _bloccoAccodamento
                If messaggioId <> _ultimoMessaggio Then
                    _ultimoMessaggio = messaggioId
                    _tickAccodamento = Environment.TickCount
                    _primoCampioneInviato = False
                    Interrompi()
                    Volatile.Write(_generazioneDaTagliare, Volatile.Read(_generazione))
                End If
                _coda.Add(New Richiesta() With {.MessaggioId = messaggioId, .Ultimo = ultimo, .Testo = If(testo, ""), .Generazione = Volatile.Read(_generazione)})
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.Accoda: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Taglia subito quello che sta parlando e svuota la coda.</summary>
    Public Sub Ferma()
        Try
            SyncLock _bloccoAccodamento
                _ultimoMessaggio = -1
                Interrompi()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.Ferma: " & ex.Message)
        End Try
    End Sub

    ' Non si chiama espeak_Cancel da qui. Il richiamo gira sull'altro thread: cancellare nel mezzo
    ' lascia il motore a metà sillaba, e la frase dopo ripete quel pezzo e si mangia le prime parole.
    ' Il richiamo vede la generazione nuova e restituisce 1, così la sintesi si chiude da sola.
    Private Sub Interrompi()
        Interlocked.Increment(_generazione)
        Dim scarto As Richiesta = Nothing
        While _coda.TryTake(scarto)
        End While
        SyncLock _bloccoUscita
            If _uscita IsNot Nothing Then _uscita.Svuota()
        End SyncLock
    End Sub

    Private Sub CicloLavoro()
        Try
            Scalda()
            For Each richiesta As Richiesta In _coda.GetConsumingEnumerable()
                Try
                    If richiesta.Generazione <> Volatile.Read(_generazione) Then Continue For
                    Pronuncia(richiesta)
                Catch ex As Exception
                    _sintetizzando = False
                    log.ScriviLog("WGMotoreEspeak.CicloLavoro: " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.CicloLavoro: " & ex.Message)
        End Try
    End Sub

    Private Sub Pronuncia(richiesta As Richiesta)
        _sintetizzando = True
        Dim libreriaPresa As Boolean = False
        Try
            If richiesta.Generazione <> Volatile.Read(_generazione) Then Return
            AssicuraUscita()
            Monitor.Enter(_bloccoLibreria)
            libreriaPresa = True
            ApplicaImpostazioni()
            If richiesta.Generazione <> Volatile.Read(_generazione) Then Return
            If _sintesiInterrotta Then
                _sintesiInterrotta = False
                Try
                    espeak_Cancel()
                Catch ex As Exception
                    log.ScriviLog("WGMotoreEspeak.Pronuncia: " & ex.Message)
                End Try
            End If
            If richiesta.Generazione <> Volatile.Read(_generazione) Then
                _sintesiInterrotta = True
                Return
            End If
            Volatile.Write(_generazioneInCorso, richiesta.Generazione)
            _messaggioInCorsoId = richiesta.MessaggioId
            _posizioneParola = 0
            Dim testo As String = If(richiesta.Testo, "").Trim()
            If testo.Length > 0 Then
                Dim byteTesto As Byte() = Encoding.UTF8.GetBytes(testo)
                ' La lunghezza non comprende lo zero finale: contarlo fa saltare l'ultima parola.
                Dim conZero(byteTesto.Length) As Byte
                Buffer.BlockCopy(byteTesto, 0, conZero, 0, byteTesto.Length)
                espeak_Synth(conZero, New UIntPtr(CUInt(byteTesto.Length)), 0UI, PosizioneInCaratteri, 0UI, TestoUtf8, IntPtr.Zero, IntPtr.Zero)
            End If
            If libreriaPresa Then
                Monitor.Exit(_bloccoLibreria)
                libreriaPresa = False
            End If
            If richiesta.Generazione <> Volatile.Read(_generazione) Then
                _sintesiInterrotta = True
                Return
            End If
            RaiseEvent Attivita()
            If Not richiesta.Ultimo Then Return

            ' Ultima parte del messaggio: si aspetta che l'audio finisca di suonare.
            Dim inizio As Integer = Environment.TickCount
            Dim ultimoSegnale As Integer = inizio
            While richiesta.Generazione = Volatile.Read(_generazione) AndAlso MillisecondiInCoda() > 0 AndAlso Environment.TickCount - inizio < 180000
                Thread.Sleep(10)
                If Environment.TickCount - ultimoSegnale > 3000 Then
                    ultimoSegnale = Environment.TickCount
                    RaiseEvent Attivita()
                End If
            End While
            If richiesta.Generazione = Volatile.Read(_generazione) Then
                ' Il suono nell'uscita ha ancora un attimo di ritardo prima di arrivare alle cuffie.
                Thread.Sleep(Math.Max(10, CInt(LatenzaUscitaMs)))
                If richiesta.Generazione = Volatile.Read(_generazione) Then RaiseEvent FineMessaggio(richiesta.MessaggioId)
            Else
                _sintesiInterrotta = True
            End If
        Finally
            If libreriaPresa Then Monitor.Exit(_bloccoLibreria)
            _sintetizzando = False
            If richiesta.Generazione <> Volatile.Read(_generazione) Then _sintesiInterrotta = True
        End Try
    End Sub

    ' Prima del primo messaggio vero: carica i dati della voce (la prima sintesi è la più lenta) e apre
    ' l'uscita audio, così la prima frase non paga né l'uno né l'altra.
    ' La sintesi deve arrivare in fondo: se il richiamo la ferma subito, la voce non si carica.
    Private Sub Scalda()
        Try
            AssicuraUscita()
            ApplicaImpostazioni()
            Interlocked.Exchange(_scarta, 1)
            _generazioneInCorso = Volatile.Read(_generazione)
            Dim byteTesto As Byte() = Encoding.UTF8.GetBytes("a")
            Dim conZero(byteTesto.Length) As Byte
            Buffer.BlockCopy(byteTesto, 0, conZero, 0, byteTesto.Length)
            espeak_Synth(conZero, New UIntPtr(CUInt(byteTesto.Length)), 0UI, PosizioneInCaratteri, 0UI, TestoUtf8, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.Scalda: " & ex.Message)
        Finally
            Interlocked.Exchange(_scarta, 0)
        End Try
    End Sub

    ''' <summary>Quanto audio deve ancora suonare (quello in coda e quello già dato a Windows), in millisecondi.</summary>
    Private Function MillisecondiInCoda() As Integer
        Dim uscita As UscitaAudio = _uscita
        Return If(uscita Is Nothing, 0, uscita.MillisecondiInCoda())
    End Function

    ''' <summary>Il sistema audio in uso ("WASAPI a bassa latenza", "WASAPI", "WinMM"); vuoto se l'uscita è chiusa.</summary>
    Public ReadOnly Property NomeUscita As String
        Get
            Dim uscita As UscitaAudio = _uscita
            Return If(uscita Is Nothing, "", uscita.Nome)
        End Get
    End Property

    ''' <summary>Il ritardo di base dell'uscita in uso, in millisecondi (0 se chiusa).</summary>
    Public ReadOnly Property LatenzaUscitaMs As Double
        Get
            Dim uscita As UscitaAudio = _uscita
            Return If(uscita Is Nothing, 0, uscita.LatenzaMs)
        End Get
    End Property

    ' Dentro la libreria: riceve i campioni audio (16 bit, mono) e li passa all'uscita. Restituire 1 ferma la sintesi.
    Private Function AlCampione(campioni As IntPtr, numero As Integer, eventi As IntPtr) As Integer
        Try
            Dim raccolta As Stream = _raccolta
            If raccolta IsNot Nothing Then
                If campioni <> IntPtr.Zero AndAlso numero > 0 Then
                    Dim raccolti(numero * 2 - 1) As Byte
                    Marshal.Copy(campioni, raccolti, 0, raccolti.Length)
                    raccolta.Write(raccolti, 0, raccolti.Length)
                End If
                Dim annulla As Func(Of Boolean) = _annullaRaccolta
                If annulla IsNot Nothing AndAlso annulla() Then Return 1
                Return 0
            End If
            If _generazioneInCorso <> Volatile.Read(_generazione) Then Return 1
            If Volatile.Read(_scarta) <> 0 Then Return 0
            If campioni = IntPtr.Zero OrElse numero <= 0 Then Return 0
            Dim primo As Integer = 0
            Dim generazione As Integer = Volatile.Read(_generazioneInCorso)
            If Volatile.Read(_generazioneDaTagliare) = generazione Then
                primo = PrimoCampioneUdibile(campioni, numero)
                If primo >= numero Then
                    InterpretaEventi(eventi)
                    Return 0
                End If
                Interlocked.CompareExchange(_generazioneDaTagliare, -1, generazione)
            End If
            Dim dati((numero - primo) * 2 - 1) As Byte
            Marshal.Copy(New IntPtr(campioni.ToInt64() + primo * 2L), dati, 0, dati.Length)
            ' Non si sintetizza troppo in anticipo: così uno stop o un messaggio nuovo hanno poco da buttare.
            Dim attese As Integer = 0
            While MillisecondiInCoda() > MillisecondiInCodaMassimi AndAlso attese < 400
                If _generazioneInCorso <> Volatile.Read(_generazione) Then Return 1
                Thread.Sleep(1)
                attese += 1
            End While
            ' Il controllo sta nel blocco insieme alla scrittura: uno svuota partito prima non si fa ricoprire
            ' dai campioni della frase appena interrotta, che se no si sentono come una sillaba ripetuta.
            SyncLock _bloccoUscita
                If _generazioneInCorso <> Volatile.Read(_generazione) Then Return 1
                If _uscita IsNot Nothing Then _uscita.Scrivi(dati, 0, dati.Length)
            End SyncLock
            If Not _primoCampioneInviato Then
                _primoCampioneInviato = True
                _latenzaMs = Environment.TickCount - _tickAccodamento
            End If
            InterpretaEventi(eventi)
            Return 0
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.AlCampione: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Function PrimoCampioneUdibile(campioni As IntPtr, numero As Integer) As Integer
        Try
            For i As Integer = 0 To numero - 1
                Dim valore As Integer = Marshal.ReadInt16(campioni, i * 2)
                If valore > SogliaSilenzio OrElse valore < -SogliaSilenzio Then Return i
            Next
            Return numero
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.PrimoCampioneUdibile: " & ex.Message)
            Return 0
        End Try
    End Function

    ''' <summary>La posizione in caratteri dell'ultima parola sintetizzata, dall'inizio del testo del messaggio.</summary>
    Public ReadOnly Property PosizioneParlata As Integer
        Get
            Return _posizioneParola
        End Get
    End Property

    ' Gli eventi di espeak-ng arrivano insieme ai campioni, in un elenco che finisce con il tipo zero.
    ' La posizione della parola e' in caratteri dall'inizio del testo dato a espeak_Synth: chi vuole
    ' puo' seguire il parlato (evidenziare la parola, muoversi nel testo) come con gli eventi WORD di SAPI.
    Private Sub InterpretaEventi(eventi As IntPtr)
        Try
            If eventi = IntPtr.Zero Then Return
            Dim dimensione As Integer = Marshal.SizeOf(GetType(EventoSintesi))
            For indice As Integer = 0 To 63
                Dim puntatore As IntPtr = New IntPtr(eventi.ToInt64() + indice * CLng(dimensione))
                Dim evento As EventoSintesi = CType(Marshal.PtrToStructure(puntatore, GetType(EventoSintesi)), EventoSintesi)
                If evento.Tipo = EventoElencoTerminato Then Exit For
                If evento.Tipo = EventoParola Then
                    _posizioneParola = evento.PosizioneTesto
                    Dim messaggio As Integer = _messaggioInCorsoId
                    If messaggio >= 0 Then RaiseEvent ParolaLetta(messaggio, evento.PosizioneTesto, evento.Lunghezza)
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.InterpretaEventi: " & ex.Message)
        End Try
    End Sub

    ' Voce, velocità e volume si applicano tra un testo e l'altro, dal thread che sintetizza.
    Private Sub ApplicaImpostazioni()
        Dim voce As String
        Dim velocita As Integer
        Dim volume As Integer
        SyncLock _bloccoAccodamento
            If Not _impostazioniCambiate Then Return
            _impostazioniCambiate = False
            voce = _voceDaUsare
            velocita = _velocitaDaUsare
            volume = _volumeDaUsare
        End SyncLock
        If espeak_SetVoiceByName(voce) <> 0 Then espeak_SetVoiceByName("it")
        espeak_SetParameter(ParametroVelocita, velocita, 0)
        espeak_SetParameter(ParametroVolume, volume, 0)
    End Sub

#End Region

#Region "Sintesi per i file"

    ''' <summary>La frequenza dei campioni che dà la libreria (Hz): 16 bit, mono.</summary>
    Public ReadOnly Property Frequenza As Integer
        Get
            Return _frequenza
        End Get
    End Property

    ''' <summary>
    ''' Sintetizza un testo senza suonarlo e ne scrive i campioni (16 bit, mono, alla frequenza di Frequenza) nel flusso, con la voce,
    ''' la velocità (parole al minuto) e l'ampiezza dati. Il parlato di WinGabriel aspetta che finisca, quindi i testi vanno dati a pezzi
    ''' non troppo lunghi. Falso se la sintesi non è riuscita o se annulla ha detto di fermarsi.
    ''' </summary>
    Public Function SintetizzaSuFlusso(testo As String, identificatoreVoce As String, parole As Integer, ampiezza As Integer, destinazione As Stream, annulla As Func(Of Boolean)) As Boolean
        Try
            If destinazione Is Nothing OrElse String.IsNullOrWhiteSpace(testo) Then Return False
            SyncLock _bloccoLibreria
                Try
                    If _sintesiInterrotta Then
                        _sintesiInterrotta = False
                        Try
                            espeak_Cancel()
                        Catch exCancel As Exception
                            log.ScriviLog("WGMotoreEspeak.SintetizzaSuFlusso (azzeramento): " & exCancel.Message)
                        End Try
                    End If
                    If espeak_SetVoiceByName(If(String.IsNullOrWhiteSpace(identificatoreVoce), "it", identificatoreVoce)) <> 0 Then espeak_SetVoiceByName("it")
                    espeak_SetParameter(ParametroVelocita, parole, 0)
                    espeak_SetParameter(ParametroVolume, ampiezza, 0)
                    _annullaRaccolta = annulla
                    _raccolta = destinazione

                    Dim byteTesto As Byte() = Encoding.UTF8.GetBytes(testo.Trim())
                    Dim conZero(byteTesto.Length) As Byte
                    Buffer.BlockCopy(byteTesto, 0, conZero, 0, byteTesto.Length)
                    espeak_Synth(conZero, New UIntPtr(CUInt(byteTesto.Length)), 0UI, PosizioneInCaratteri, 0UI, TestoUtf8, IntPtr.Zero, IntPtr.Zero)

                    If annulla IsNot Nothing AndAlso annulla() Then
                        ' fermata a metà: il motore va azzerato prima della sintesi dopo
                        _sintesiInterrotta = True
                        Return False
                    End If
                    Return True
                Finally
                    _raccolta = Nothing
                    _annullaRaccolta = Nothing
                    ' voce, velocità e volume del parlato si rimettono come li ha scelti l'utente
                    SyncLock _bloccoAccodamento
                        _impostazioniCambiate = True
                    End SyncLock
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreEspeak.SintetizzaSuFlusso: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

#Region "L'uscita audio"

    Private Sub AssicuraUscita()
        SyncLock _bloccoUscita
            If _uscita IsNot Nothing AndAlso Not _uscita.Valida Then
                Try
                    _uscita.Dispose()
                Catch ex As Exception
                    log.ScriviLog("WGMotoreEspeak.AssicuraUscita chiusura: " & ex.Message)
                End Try
                _uscita = Nothing
            End If
            If _uscita IsNot Nothing Then Return
            Try
                _uscita = UscitaAudio.Apri(_frequenza)
                If _uscita Is Nothing Then log.ScriviLog("WGMotoreEspeak.AssicuraUscita: nessuna uscita audio, " & UscitaAudio.Diagnosi)
            Catch ex As Exception
                _uscita = Nothing
                log.ScriviLog("WGMotoreEspeak.AssicuraUscita apertura: " & ex.Message)
            End Try
        End SyncLock
    End Sub

#End Region

End Class

' Dove la libreria scrive le sue poche impostazioni.
Public Interface IArchivioEspeak
    Function Leggi(chiave As String) As String
    Sub Scrivi(chiave As String, valore As String)
End Interface

Friend NotInheritable Class ArchivioInMemoria
    Implements IArchivioEspeak

    Private ReadOnly _valori As New Dictionary(Of String, String)()

    Public Function Leggi(chiave As String) As String Implements IArchivioEspeak.Leggi
        Dim valore As String = Nothing
        SyncLock _valori
            If _valori.TryGetValue(chiave, valore) Then Return valore
        End SyncLock
        Return ""
    End Function

    Public Sub Scrivi(chiave As String, valore As String) Implements IArchivioEspeak.Scrivi
        SyncLock _valori
            _valori(chiave) = valore
        End SyncLock
    End Sub
End Class

Friend NotInheritable Class Logger
    Public Sub ScriviLog(testo As String)
        Try
            System.Diagnostics.Trace.WriteLine("WGEspeak: " & testo)
        Catch ex As Exception
            ' Il registro non deve mai fermare la voce.
        End Try
    End Sub
End Class
