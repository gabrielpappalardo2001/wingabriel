Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports WGBrowserManager
Imports wgcontrols

Public NotInheritable Class WGMotoreBrowser
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared _ambiente As CoreWebView2Environment = Nothing
    Private Shared _creazione As Task(Of CoreWebView2Environment) = Nothing
    Private Shared _versione As String = ""
    Private Shared _idThreadInterfaccia As Integer = 0
    Private Shared _installazioneInCorso As Integer = 0
    Private Shared _controllerVivi As Integer = 0
    Private Shared _istanteUltimoRecupero As DateTime = DateTime.MinValue
    Private Shared _argomenti As String = Nothing
    Private Shared _dpiAvvioMotore As String = ""
    Private Shared _ultimaUscitaMotore As String = ""
    Private Shared _runtimeFissoEscluso As Boolean = False
    Private Shared _passoRecupero As Integer = 0
    Private Shared _senzaArgomenti As Boolean = False
    Private Shared _cartellaAlternativa As Boolean = False
    Private Shared ReadOnly _semaforoRecupero As New SemaphoreSlim(1, 1)
    Private Const SecondiTraRecuperi As Integer = 20
    Private Const ErroreStatoNonValido As Integer = &H8007139F

    Public Const NomeProfiloPrivato As String = "WinGabrielPrivata"
    Public Const LarghezzaVista As Integer = 1920
    Public Const AltezzaVista As Integer = 1080
    Public Const LinguaInterfaccia As String = "it-IT"
    Public Const SorgenteSchermoIntero As String = "Schermo"
    Public Const MarcatoreFinestraCondivisa As String = "[condivisa da WinGabriel]"

    Private Const VersioneRiserva As String = "152.0.0.0"
    Private Const VersioneSceltaSchermo As Integer = 140

    ''' <summary>
    ''' Se falso, il motore parte con l'accelerazione grafica disattivata (--disable-gpu).
    ''' Non legge WGImpostazioniBrowser direttamente (per restare indipendente da quel progetto):
    ''' e' il chiamante a tenerla aggiornata, es. WGBrowser all'avvio e quando l'impostazione cambia.
    ''' </summary>
    Public Shared Property AccelerazioneGraficaAttiva As Boolean = True

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property CartellaProfilo As String
        Get
            Try
                ' i dati del browser sono di chi usa il programma: nei dati locali dell'utente se è installato, accanto al programma se è portatile
                Dim cartella As String = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaDatiBrowser(), If(_cartellaAlternativa, "webview2.alt", "webview2"))
                If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                Return cartella
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.CartellaProfilo: " & ex.Message)
                Try
                    Dim riserva As String = Path.Combine(Path.GetTempPath(), "WinGabriel", "webview2")
                    If Not Directory.Exists(riserva) Then Directory.CreateDirectory(riserva)
                    Return riserva
                Catch exRiserva As Exception
                    log.ScriviLog("WGMotoreBrowser.CartellaProfilo riserva: " & exRiserva.Message)
                    Return Path.GetTempPath()
                End Try
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property Ambiente As CoreWebView2Environment
        Get
            Try
                SyncLock _lock
                    Return _ambiente
                End SyncLock
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.Ambiente: " & ex.Message)
                Return Nothing
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Cartella del runtime WebView2 "Fixed Version" da usare, sempre \WebView2 nella radice
    ''' dell'applicazione (vedi WGRuntimeWebView2). Nothing se assente o escluso dopo un errore.
    ''' </summary>
    Public Shared Function CartellaRuntimeFissa() As String
        Try
            If _runtimeFissoEscluso Then Return Nothing
            Return WGRuntimeWebView2.CartellaRuntimeValida()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.CartellaRuntimeFissa: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Shared Function VersioneRuntime() As String
        Try
            Dim versione As String = CoreWebView2Environment.GetAvailableBrowserVersionString(CartellaRuntimeFissa())
            Return If(versione, "").Trim()
        Catch ex As WebView2RuntimeNotFoundException
            log.ScriviLog("WGMotoreBrowser.VersioneRuntime: motore Microsoft Edge WebView2 non installato: " & ex.Message)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.VersioneRuntime: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Shared Function VersioneMotore() As String
        Try
            Dim versione As String = _versione
            If String.IsNullOrWhiteSpace(versione) Then versione = VersioneRuntime()
            versione = If(versione, "").Trim()
            Dim spazio As Integer = versione.IndexOf(" "c)
            If spazio > 0 Then versione = versione.Substring(0, spazio)
            If versione <> "" AndAlso Char.IsDigit(versione(0)) Then Return versione
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.VersioneMotore: " & ex.Message)
        End Try
        Return VersioneRiserva
    End Function

    Public Shared Function VersionePrincipaleEffettiva() As Integer
        Try
            Dim versione As String = _versione
            If String.IsNullOrWhiteSpace(versione) Then versione = VersioneRuntime()
            versione = If(versione, "").Trim()
            Dim punto As Integer = versione.IndexOf("."c)
            Dim numero As Integer
            If Integer.TryParse(If(punto > 0, versione.Substring(0, punto), versione), numero) AndAlso numero > 0 Then Return numero
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.VersionePrincipaleEffettiva: " & ex.Message)
        End Try
        Return 0
    End Function

    Public Shared ReadOnly Property UsaRuntimeFisso As Boolean
        Get
            Try
                Return Not String.IsNullOrEmpty(CartellaRuntimeFissa())
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.UsaRuntimeFisso: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared ReadOnly Property FinestreCondivisibili As Boolean
        Get
            Try
                Return VersionePrincipaleEffettiva() >= VersioneSceltaSchermo
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.FinestreCondivisibili: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared Sub RegistraThreadInterfaccia()
        Try
            If _idThreadInterfaccia = 0 Then _idThreadInterfaccia = Thread.CurrentThread.ManagedThreadId
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.RegistraThreadInterfaccia: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property SuThreadInterfaccia As Boolean
        Get
            Try
                Return _idThreadInterfaccia <> 0 AndAlso Thread.CurrentThread.ManagedThreadId = _idThreadInterfaccia
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.SuThreadInterfaccia: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public Shared Function AssicuraAmbienteAsync() As Task(Of CoreWebView2Environment)
        Try
            SyncLock _lock
                If _ambiente IsNot Nothing Then Return Task.FromResult(_ambiente)
                If _creazione Is Nothing OrElse _creazione.IsCompleted Then _creazione = CreaAmbienteAsync()
                Return _creazione
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.AssicuraAmbienteAsync: " & ex.ToString())
            Return Task.FromResult(Of CoreWebView2Environment)(Nothing)
        End Try
    End Function

    ' Un ambiente per un thread diverso da quello dell'interfaccia: gli oggetti di WebView2 si possono usare solo dal thread che li ha creati
    ' ("members can only be accessed from the UI thread"), quindi la pagina nascosta che gira su un thread suo non può usare quello
    ' condiviso. Ha le stesse opzioni, cartella del motore e profilo: con opzioni diverse WebView2 rifiuterebbe di aprire lo stesso profilo.
    Public Shared Async Function CreaAmbientePerQuestoThreadAsync() As Task(Of CoreWebView2Environment)
        Try
            Dim opzioni As New CoreWebView2EnvironmentOptions(ArgomentiFissati(), LinguaInterfaccia, Nothing, False, Nothing)
            Try
                opzioni.EnableTrackingPrevention = False
            Catch exTracciamento As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbientePerQuestoThreadAsync prevenzione tracciamento: " & exTracciamento.Message)
            End Try
            Try
                opzioni.AreBrowserExtensionsEnabled = False
            Catch exEstensioni As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbientePerQuestoThreadAsync estensioni: " & exEstensioni.Message)
            End Try
            Return Await CoreWebView2Environment.CreateAsync(CartellaRuntimeFissa(), CartellaProfilo, opzioni)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.CreaAmbientePerQuestoThreadAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Async Function CreaAmbienteAsync() As Task(Of CoreWebView2Environment)
        Try
            RegistraThreadInterfaccia()
            Dim opzioni As New CoreWebView2EnvironmentOptions(ArgomentiFissati(), LinguaInterfaccia, Nothing, False, Nothing)
            Try
                opzioni.EnableTrackingPrevention = False
            Catch exTracciamento As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbienteAsync prevenzione tracciamento: " & exTracciamento.Message)
            End Try
            Try
                opzioni.AreBrowserExtensionsEnabled = False
            Catch exEstensioni As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbienteAsync estensioni: " & exEstensioni.Message)
            End Try
            _dpiAvvioMotore = NomeDpi(ContestoDpiThread())
            Dim nuovo As CoreWebView2Environment = Await CoreWebView2Environment.CreateAsync(CartellaRuntimeFissa(), CartellaProfilo, opzioni)
            If nuovo Is Nothing Then Return Nothing
            Try
                _versione = If(nuovo.BrowserVersionString, "")
            Catch exVersione As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbienteAsync versione: " & exVersione.Message)
            End Try
            Try
                AddHandler nuovo.BrowserProcessExited, AddressOf ProcessoMotoreTerminato
            Catch exEvento As Exception
                log.ScriviLog("WGMotoreBrowser.CreaAmbienteAsync evento: " & exEvento.Message)
            End Try
            SyncLock _lock
                _ambiente = nuovo
            End SyncLock
            Return nuovo
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.CreaAmbienteAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Shared Function ArgomentiFissati() As String
        Try
            SyncLock _lock
                If _argomenti IsNot Nothing Then Return _argomenti
            End SyncLock
            Dim calcolati As String = ArgomentiMotore()
            If VersionePrincipaleEffettiva() <= 0 Then Return calcolati
            SyncLock _lock
                If _argomenti Is Nothing Then _argomenti = calcolati
                Return _argomenti
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ArgomentiFissati: " & ex.Message)
            Return ArgomentiMotore()
        End Try
    End Function

    Private Shared Function ArgomentiMotore() As String
        Dim argomenti As New List(Of String)()
        Try
            If _senzaArgomenti Then Return ""
            argomenti.Add("--disable-features=BackForwardCache")
            argomenti.Add("--autoplay-policy=no-user-gesture-required")
            argomenti.Add("--disable-renderer-accessibility")
            If VersionePrincipaleEffettiva() >= VersioneSceltaSchermo Then
                argomenti.Add("--auto-select-screen-capture-source")
            Else
                argomenti.Add("--auto-select-desktop-capture-source=""" & SorgenteSchermoIntero & """")
            End If
            argomenti.Add("--auto-select-window-capture-source-by-title=""" & MarcatoreFinestraCondivisa & """")
            If Not AccelerazioneGraficaAttiva Then argomenti.Add("--disable-gpu")
            Dim ublockPath As String = Path.Combine(WGRuntimeWebView2.CartellaBaseApplicazione(), "Extensions", "uBlockOrigin")
            If File.Exists(Path.Combine(ublockPath, "manifest.json")) Then
                argomenti.Add("--load-extension=""" & ublockPath & """")
            End If
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ArgomentiMotore: " & ex.Message)
        End Try
        Return String.Join(" ", argomenti)
    End Function

    Public Shared Function OpzioniController(ambienteMotore As CoreWebView2Environment, privata As Boolean) As CoreWebView2ControllerOptions
        Try
            If ambienteMotore Is Nothing Then Return Nothing
            Dim opzioni As CoreWebView2ControllerOptions = ambienteMotore.CreateCoreWebView2ControllerOptions()
            If opzioni Is Nothing Then Return Nothing
            If privata Then
                opzioni.ProfileName = NomeProfiloPrivato
                opzioni.IsInPrivateModeEnabled = True
            End If
            Try
                opzioni.DefaultBackgroundColor = Color.White
            Catch exSfondo As Exception
                log.ScriviLog("WGMotoreBrowser.OpzioniController sfondo: " & exSfondo.Message)
            End Try
            Return opzioni
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.OpzioniController: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Sub ControllerCreato()
        Try
            Interlocked.Increment(_controllerVivi)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ControllerCreato: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ControllerChiuso()
        Try
            Dim attuale As Integer = Interlocked.CompareExchange(_controllerVivi, 0, 0)
            While attuale > 0
                Dim precedente As Integer = Interlocked.CompareExchange(_controllerVivi, attuale - 1, attuale)
                If precedente = attuale Then Exit While
                attuale = precedente
            End While
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ControllerChiuso: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property ControllerVivi As Integer
        Get
            Try
                Return Interlocked.CompareExchange(_controllerVivi, 0, 0)
            Catch ex As Exception
                log.ScriviLog("WGMotoreBrowser.ControllerVivi: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    <DllImport("user32.dll")>
    Private Shared Function GetThreadDpiAwarenessContext() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowDpiAwarenessContext(hwnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAwarenessFromDpiAwarenessContext(contesto As IntPtr) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function AreDpiAwarenessContextsEqual(primo As IntPtr, secondo As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    Private Shared Function ContestoDpiThread() As IntPtr
        Try
            Return GetThreadDpiAwarenessContext()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ContestoDpiThread: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Function ContestoDpiFinestra(finestra As IntPtr) As IntPtr
        Try
            If finestra = IntPtr.Zero Then Return IntPtr.Zero
            Return GetWindowDpiAwarenessContext(finestra)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ContestoDpiFinestra: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Function NomeDpi(contesto As IntPtr) As String
        Try
            If contesto = IntPtr.Zero Then Return "non disponibile"
            If AreDpiAwarenessContextsEqual(contesto, New IntPtr(-4)) Then Return "per monitor v2"
            If AreDpiAwarenessContextsEqual(contesto, New IntPtr(-5)) Then Return "non consapevole con scala GDI"
            Select Case GetAwarenessFromDpiAwarenessContext(contesto)
                Case 0
                    Return "non consapevole"
                Case 1
                    Return "sistema"
                Case 2
                    Return "per monitor"
                Case Else
                    Return "non valido"
            End Select
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.NomeDpi: " & ex.Message)
            Return "non disponibile"
        End Try
    End Function

    Private Shared Function DescriviProcessiAmbiente(ambienteMotore As CoreWebView2Environment) As String
        Try
            If ambienteMotore Is Nothing Then Return "ambiente assente"
            Dim infos As IReadOnlyList(Of CoreWebView2ProcessInfo) = ambienteMotore.GetProcessInfos()
            If infos Is Nothing OrElse infos.Count = 0 Then Return "nessuno"
            Dim parti As New List(Of String)()
            For Each info As CoreWebView2ProcessInfo In infos
                parti.Add(info.Kind.ToString() & " " & info.ProcessId)
            Next
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.DescriviProcessiAmbiente: " & ex.Message)
            Return "lettura non riuscita: " & ex.Message
        End Try
    End Function

    Private Shared Function DiagnosiRuntime() As String
        Try
            Dim versione As String = VersioneRuntime()
            If versione = "" Then Return "Diagnosi del runtime: versione non rilevata."
            Dim fissa As String = CartellaRuntimeFissa()
            Dim radice As String = fissa
            If String.IsNullOrEmpty(radice) Then radice = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application")
            Dim cartellaVersione As String = Path.Combine(radice, versione)
            Dim parti As New List(Of String)()
            For Each relativo As String In New String() {"msedgewebview2.exe", Path.Combine(versione, "msedge.dll"), Path.Combine(versione, "icudtl.dat"), Path.Combine(versione, "resources.pak"), Path.Combine(versione, "EBWebView", "x64", "EmbeddedBrowserWebView.dll")}
                Dim percorso As String = Path.Combine(radice, relativo)
                If File.Exists(percorso) Then
                    Dim dimensione As Long = New FileInfo(percorso).Length
                    parti.Add(relativo & " " & If(dimensione = 0, "VUOTO", dimensione.ToString() & " byte"))
                Else
                    parti.Add(relativo & " MANCANTE")
                End If
            Next
            Return "Diagnosi del runtime in " & radice & ": " & String.Join("; ", parti) & "."
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.DiagnosiRuntime: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function DescriviStato(ambienteMotore As CoreWebView2Environment, finestra As IntPtr) As String
        Try
            Dim cartellaFissa As String = CartellaRuntimeFissa()
            Dim versione As String = ""
            Dim cartellaDati As String = ""
            Try
                If ambienteMotore IsNot Nothing Then
                    versione = ambienteMotore.BrowserVersionString
                    cartellaDati = ambienteMotore.UserDataFolder
                End If
            Catch exAmbiente As Exception
                log.ScriviLog("WGMotoreBrowser.DescriviStato ambiente: " & exAmbiente.Message)
            End Try
            Return "Stato del motore: versione " & If(versione, "") &
                ", runtime " & If(String.IsNullOrEmpty(cartellaFissa), "installato in Windows", "fisso in " & cartellaFissa) &
                ", cartella dati " & If(cartellaDati, "") &
                ", processi dell'ambiente: " & DescriviProcessiAmbiente(ambienteMotore) &
                ", ultima uscita del motore: " & If(_ultimaUscitaMotore = "", "nessuna", _ultimaUscitaMotore) &
                ", schede aperte: " & ControllerVivi &
                ", DPI all'avvio del motore: " & If(_dpiAvvioMotore = "", "sconosciuto", _dpiAvvioMotore) &
                ", DPI del thread: " & NomeDpi(ContestoDpiThread()) &
                ", DPI della finestra: " & NomeDpi(ContestoDpiFinestra(finestra)) &
                ", thread dell'interfaccia: " & If(SuThreadInterfaccia, "sì", "no") & ". " & DiagnosiRuntime()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.DescriviStato: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function EStatoNonValido(ex As Exception) As Boolean
        Try
            Return ex IsNot Nothing AndAlso ex.HResult = ErroreStatoNonValido
        Catch exControllo As Exception
            log.ScriviLog("WGMotoreBrowser.EStatoNonValido: " & exControllo.Message)
            Return False
        End Try
    End Function

    Private Shared Function TerminaAlbero(idProcesso As Integer) As Boolean
        Try
            If idProcesso <= 0 OrElse idProcesso = System.Diagnostics.Process.GetCurrentProcess().Id Then Return False
            Dim info As New System.Diagnostics.ProcessStartInfo("taskkill", "/PID " & idProcesso.ToString() & " /T /F")
            info.CreateNoWindow = True
            info.UseShellExecute = False
            Using p As System.Diagnostics.Process = System.Diagnostics.Process.Start(info)
                If p Is Nothing Then Return False
                p.WaitForExit(4000)
            End Using
            Return True
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.TerminaAlbero: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ProcessiAttivi() As Dictionary(Of Integer, String)
        Dim risultato As New Dictionary(Of Integer, String)()
        Try
            For Each p As System.Diagnostics.Process In System.Diagnostics.Process.GetProcesses()
                Try
                    risultato(p.Id) = p.ProcessName
                Catch exProcesso As Exception
                    log.ScriviLog("WGMotoreBrowser.ProcessiAttivi processo: " & exProcesso.Message)
                Finally
                    p.Dispose()
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ProcessiAttivi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function ProcessiMotoreDellaCartellaDati() As List(Of WGProcessoMotore)
        Dim risultato As New List(Of WGProcessoMotore)()
        Try
            Dim cartella As String = CartellaProfilo
            If String.IsNullOrEmpty(cartella) Then Return risultato
            cartella = cartella.TrimEnd("\"c)
            Dim copione As String = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;" &
                "Get-CimInstance Win32_Process -Filter ""Name='msedgewebview2.exe'"" | ForEach-Object { '' + $_.ProcessId + '|' + $_.ParentProcessId + '|' + $_.CommandLine }"
            Dim info As New System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " & Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(copione)))
            info.CreateNoWindow = True
            info.UseShellExecute = False
            info.RedirectStandardOutput = True
            info.StandardOutputEncoding = System.Text.Encoding.UTF8
            Using proc As System.Diagnostics.Process = System.Diagnostics.Process.Start(info)
                If proc Is Nothing Then Return risultato
                Dim lettura As Task(Of String) = proc.StandardOutput.ReadToEndAsync()
                If Not lettura.Wait(15000) Then
                    Try
                        proc.Kill()
                    Catch exChiusura As Exception
                        log.ScriviLog("WGMotoreBrowser.ProcessiMotoreDellaCartellaDati chiusura: " & exChiusura.Message)
                    End Try
                    Return risultato
                End If
                For Each riga As String In lettura.Result.Split(New Char() {ControlChars.Lf, ControlChars.Cr}, StringSplitOptions.RemoveEmptyEntries)
                    Dim primaBarra As Integer = riga.IndexOf("|"c)
                    If primaBarra <= 0 Then Continue For
                    Dim secondaBarra As Integer = riga.IndexOf("|"c, primaBarra + 1)
                    If secondaBarra <= primaBarra Then Continue For
                    Dim id As Integer
                    Dim idPadre As Integer
                    If Not Integer.TryParse(riga.Substring(0, primaBarra).Trim(), id) Then Continue For
                    Integer.TryParse(riga.Substring(primaBarra + 1, secondaBarra - primaBarra - 1).Trim(), idPadre)
                    Dim rigaComando As String = riga.Substring(secondaBarra + 1)
                    If rigaComando.IndexOf(cartella, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                    Dim elemento As New WGProcessoMotore()
                    elemento.Id = id
                    elemento.IdPadre = idPadre
                    elemento.RigaComando = rigaComando.Trim()
                    elemento.Principale = rigaComando.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) < 0
                    risultato.Add(elemento)
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ProcessiMotoreDellaCartellaDati: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function DescriviProcessi(processi As List(Of WGProcessoMotore)) As String
        Try
            Dim principali As List(Of WGProcessoMotore) = processi.FindAll(AddressOf EProcessoPrincipale)
            If principali.Count = 0 Then Return "Nessun processo principale del motore trovato sulla cartella dati " & CartellaProfilo & "."
            Dim idCorrente As Integer = System.Diagnostics.Process.GetCurrentProcess().Id
            Dim attivi As Dictionary(Of Integer, String) = ProcessiAttivi()
            Dim parti As New List(Of String)()
            For Each p As WGProcessoMotore In principali
                Dim padre As String = ""
                If Not attivi.TryGetValue(p.IdPadre, padre) Then padre = ""
                Dim origine As String = If(p.IdPadre = idCorrente, "avviato da questo stesso WinGabriel", "avviato da " & If(padre = "", "un processo sconosciuto", padre) & " (PID " & p.IdPadre & ")")
                Dim comando As String = p.RigaComando
                If comando.Length > 900 Then comando = comando.Substring(0, 900) & "..."
                parti.Add("processo " & p.Id & " " & origine & ", riga di comando: " & comando)
            Next
            Return "Sulla cartella dati del motore girano: " & String.Join(" | ", parti) & ". Argomenti attesi da WinGabriel: " & ArgomentiFissati() & "."
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.DescriviProcessi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function EProcessoPrincipale(p As WGProcessoMotore) As Boolean
        Try
            Return p IsNot Nothing AndAlso p.Principale
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.EProcessoPrincipale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function RaccogliProcessi() As List(Of WGProcessoMotore)
        Try
            Return ProcessiMotoreDellaCartellaDati()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.RaccogliProcessi: " & ex.Message)
            Return New List(Of WGProcessoMotore)()
        End Try
    End Function

    Private Shared Function TerminaProcessiRuntimeFisso(cartella As String) As List(Of Integer)
        Dim terminati As New List(Of Integer)()
        Try
            If String.IsNullOrEmpty(cartella) Then Return terminati
            For Each p As System.Diagnostics.Process In System.Diagnostics.Process.GetProcessesByName("msedgewebview2")
                Try
                    Dim percorso As String = p.MainModule.FileName
                    If percorso IsNot Nothing AndAlso percorso.StartsWith(cartella, StringComparison.OrdinalIgnoreCase) Then
                        If TerminaAlbero(p.Id) Then terminati.Add(p.Id)
                    End If
                Catch exProcesso As Exception
                    log.ScriviLog("WGMotoreBrowser.TerminaProcessiRuntimeFisso processo: " & exProcesso.Message)
                Finally
                    p.Dispose()
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.TerminaProcessiRuntimeFisso: " & ex.Message)
        End Try
        Return terminati
    End Function

    Private Shared Async Function AttendiUscitaAsync(identificativi As List(Of Integer)) As Task
        Try
            Dim limite As DateTime = DateTime.Now.AddSeconds(6)
            While DateTime.Now < limite
                Dim ancoraAttivi As Boolean = False
                Dim attivi As Dictionary(Of Integer, String) = ProcessiAttivi()
                For Each id As Integer In identificativi
                    If attivi.ContainsKey(id) Then
                        ancoraAttivi = True
                        Exit For
                    End If
                Next
                If Not ancoraAttivi Then Exit While
                Await Task.Delay(200)
            End While
            Await Task.Delay(500)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.AttendiUscitaAsync: " & ex.Message)
        End Try
    End Function

    Public Shared Function AttendiRecuperoAsync() As Task
        Try
            Return _semaforoRecupero.WaitAsync()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.AttendiRecuperoAsync: " & ex.Message)
            Return Task.FromResult(0)
        End Try
    End Function

    Public Shared Sub EsciRecupero()
        Try
            _semaforoRecupero.Release()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.EsciRecupero: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CartellaGuasta() As String
        Try
            Return CartellaProfilo.TrimEnd("\"c) & ".guasta"
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.CartellaGuasta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function SpostaCartellaDati() As String
        Try
            Dim cartella As String = CartellaProfilo.TrimEnd("\"c)
            Dim guasta As String = CartellaGuasta()
            If guasta = "" Then Return "percorso della cartella di riserva non disponibile"
            If Not Directory.Exists(cartella) Then Return ""
            If Directory.Exists(guasta) Then Directory.Delete(guasta, True)
            Directory.Move(cartella, guasta)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.SpostaCartellaDati: " & ex.Message)
            Return ex.Message
        End Try
    End Function

    Private Shared Async Function SpostaCartellaDatiAsync() As Task(Of String)
        Dim ultimoErrore As String = ""
        Try
            For tentativo As Integer = 1 To 5
                ultimoErrore = Await Task.Run(New Func(Of String)(AddressOf SpostaCartellaDati))
                If ultimoErrore = "" Then Return ""
                Await Task.Delay(500)
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.SpostaCartellaDatiAsync: " & ex.Message)
            ultimoErrore = ex.Message
        End Try
        Return ultimoErrore
    End Function

    Private Shared Function RuntimeInstallatoDisponibile() As Boolean
        Try
            Dim versione As String = CoreWebView2Environment.GetAvailableBrowserVersionString(Nothing)
            Return Not String.IsNullOrWhiteSpace(versione)
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.RuntimeInstallatoDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Async Function RinnovaMotoreAsync(esito As WGEsitoRecuperoMotore) As Task(Of WGEsitoRecuperoMotore)
        Try
            AmbienteTerminato(Nothing)
            Do
                Dim passo As Integer = Interlocked.Increment(_passoRecupero)
                Select Case passo
                    Case 1
                        Dim errore As String = Await SpostaCartellaDatiAsync()
                        If errore = "" Then
                            esito.Liberato = True
                            esito.Descrizione &= " La cartella dati del motore è stata sostituita con una nuova, la precedente è in " & CartellaGuasta() & "."
                            Return esito
                        End If
                        esito.Descrizione &= " Sostituzione della cartella dati non riuscita: " & errore & "."
                    Case 2
                        Dim cartellaFissa As String = CartellaRuntimeFissa()
                        If Not String.IsNullOrEmpty(cartellaFissa) AndAlso RuntimeInstallatoDisponibile() Then
                            _runtimeFissoEscluso = True
                            SyncLock _lock
                                _argomenti = Nothing
                            End SyncLock
                            esito.Liberato = True
                            esito.Descrizione &= " Escluso il runtime fisso in " & cartellaFissa & ": in uso quello installato in Windows."
                            Return esito
                        End If
                    Case 3
                        If Not _senzaArgomenti Then
                            _senzaArgomenti = True
                            SyncLock _lock
                                _argomenti = Nothing
                            End SyncLock
                            esito.Liberato = True
                            esito.Descrizione &= " Il motore parte senza argomenti personalizzati (condivisione dello schermo e altre opzioni avanzate potrebbero non funzionare)."
                            Return esito
                        End If
                    Case 4
                        If Not _cartellaAlternativa Then
                            _cartellaAlternativa = True
                            esito.Liberato = True
                            esito.Descrizione &= " Il motore usa una cartella dati alternativa (" & CartellaProfilo & "): accessi e cookie salvati non sono disponibili in questa cartella."
                            Return esito
                        End If
                    Case Else
                        esito.Descrizione &= " Nessun altro rimedio disponibile."
                        Return esito
                End Select
            Loop
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.RinnovaMotoreAsync: " & ex.Message)
            Return esito
        End Try
    End Function

    Public Shared Async Function LiberaMotoreInConflittoAsync() As Task(Of WGEsitoRecuperoMotore)
        Dim esito As New WGEsitoRecuperoMotore()
        Try
            Dim processi As List(Of WGProcessoMotore) = Await Task.Run(New Func(Of List(Of WGProcessoMotore))(AddressOf RaccogliProcessi))
            esito.Descrizione = DescriviProcessi(processi)
            Dim vivi As Integer = ControllerVivi
            If vivi > 0 Then
                esito.Descrizione &= " Il motore ha " & vivi & If(vivi = 1, " scheda aperta", " schede aperte") & ", quindi i processi non sono stati chiusi."
                Return esito
            End If
            If processi.Count = 0 Then Return Await RinnovaMotoreAsync(esito)
            SyncLock _lock
                If (DateTime.Now - _istanteUltimoRecupero).TotalSeconds < SecondiTraRecuperi Then
                    esito.Descrizione &= " Chiusura dei processi già tentata da meno di " & SecondiTraRecuperi & " secondi."
                    Return esito
                End If
                _istanteUltimoRecupero = DateTime.Now
            End SyncLock
            Dim terminati As New List(Of Integer)()
            Dim daChiudere As List(Of WGProcessoMotore) = processi.FindAll(AddressOf EProcessoPrincipale)
            If daChiudere.Count = 0 Then daChiudere = processi
            For Each p As WGProcessoMotore In daChiudere
                If TerminaAlbero(p.Id) Then terminati.Add(p.Id)
            Next
            If terminati.Count = 0 Then terminati.AddRange(TerminaProcessiRuntimeFisso(CartellaRuntimeFissa()))
            If terminati.Count = 0 Then
                esito.Descrizione &= " Nessun processo del motore da chiudere."
                Return esito
            End If
            Dim attesa As New List(Of Integer)(terminati)
            For Each p As WGProcessoMotore In processi
                If Not attesa.Contains(p.Id) Then attesa.Add(p.Id)
            Next
            Await AttendiUscitaAsync(attesa)
            esito.Descrizione &= " Chiusi " & terminati.Count & If(terminati.Count = 1, " processo.", " processi.")
            esito.Liberato = True
            If Interlocked.CompareExchange(_passoRecupero, 0, 0) = 0 Then Return Await RinnovaMotoreAsync(esito)
            Return esito
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.LiberaMotoreInConflittoAsync: " & ex.Message)
            Return esito
        End Try
    End Function

    Public Shared Sub AmbienteTerminato(ambienteFinito As CoreWebView2Environment)
        Try
            SyncLock _lock
                If ambienteFinito Is Nothing OrElse ambienteFinito Is _ambiente Then
                    _ambiente = Nothing
                    _creazione = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.AmbienteTerminato: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ProcessoMotoreTerminato(sender As Object, e As CoreWebView2BrowserProcessExitedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento BrowserProcessExited")
            If e IsNot Nothing Then _ultimaUscitaMotore = DateTime.Now.ToString("HH:mm:ss.fff") & " processo " & e.BrowserProcessId & " (" & e.BrowserProcessExitKind.ToString() & ")"
            Dim finito As CoreWebView2Environment = TryCast(sender, CoreWebView2Environment)
            Dim eraCorrente As Boolean = finito IsNot Nothing AndAlso finito Is Ambiente
            AmbienteTerminato(finito)
            If eraCorrente Then Interlocked.Exchange(_controllerVivi, 0)
            If e IsNot Nothing AndAlso e.BrowserProcessExitKind = CoreWebView2BrowserProcessExitKind.Failed Then
                log.ScriviLog("WGMotoreBrowser: il processo del motore " & e.BrowserProcessId & " si è interrotto in modo anomalo")
            End If
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ProcessoMotoreTerminato: " & ex.Message)
        End Try
    End Sub

    Public Shared Function AttendiCompito(compito As Task, millisecondi As Integer) As Boolean
        Try
            If compito Is Nothing Then Return False
            If Not SuThreadInterfaccia Then Return compito.Wait(Math.Max(0, millisecondi))
            Dim limite As DateTime = DateTime.Now.AddMilliseconds(Math.Max(0, millisecondi))
            While Not compito.IsCompleted AndAlso DateTime.Now < limite
                Application.DoEvents()
                If Not compito.IsCompleted Then Thread.Sleep(15)
            End While
            Return compito.IsCompleted
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.AttendiCompito: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub ProponiInstallazione(richiamo As Action)
        Try
            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            If Interlocked.CompareExchange(_installazioneInCorso, 0, 0) = 1 Then
                lettura.LeggiTesto("L'installazione del motore del browser è già in corso, attendere")
                Return
            End If
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, Sub(risposta As DialogResult)
                                                    Try
                                                        If risposta <> DialogResult.Yes Then Return
                                                        InstallaEPoi(richiamo)
                                                    Catch exRisposta As Exception
                                                        log.ScriviLog("WGMotoreBrowser.ProponiInstallazione risposta: " & exRisposta.Message)
                                                    End Try
                                                End Sub
            domanda.avvia("Il browser di WinGabriel usa il motore Microsoft Edge WebView2, che su questo computer non è installato. Scaricarlo e installarlo adesso?")
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.ProponiInstallazione: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Scarica dal catalogo Microsoft il .cab del runtime "Fixed Version" adatto al sistema
    ''' (32 o 64 bit) e lo estrae in \WebView2, usando WGRuntimeWebView2.
    ''' </summary>
    Private Shared Async Sub InstallaEPoi(richiamo As Action)
        If Interlocked.Exchange(_installazioneInCorso, 1) = 1 Then Return
        Try
            Dim lettura As sintesivocale = sintesivocale.GetInstance()
            lettura.LeggiTesto("Scaricamento e installazione del motore del browser, attendere qualche minuto")
            Dim esito As WGEsitoRuntime = Await WGRuntimeWebView2.AssicuraRuntimeAsync(forza:=True)
            If esito.Riuscito Then
                lettura.LeggiTesto("Motore del browser installato")
                If richiamo IsNot Nothing Then richiamo()
                Return
            End If
            If esito.ServonoPermessi Then
                MostraMessaggio("Su questo computer non è possibile creare la cartella del motore del browser Microsoft Edge WebView2 (" & WGRuntimeWebView2.CartellaRuntime() & "): " & esito.Descrizione & " Chiedere a chi amministra il computer di installarlo, oppure eseguire WinGabriel con i permessi necessari sulla propria cartella.")
            Else
                MostraMessaggio("Non è stato possibile installare il motore del browser Microsoft Edge WebView2: " & esito.Descrizione)
            End If
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.InstallaEPoi: " & ex.ToString())
        Finally
            Interlocked.Exchange(_installazioneInCorso, 0)
        End Try
    End Sub

    Private Shared Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.Show()
        Catch ex As Exception
            log.ScriviLog("WGMotoreBrowser.MostraMessaggio: " & ex.Message)
        End Try
    End Sub
End Class

Public NotInheritable Class WGProcessoMotore
    Public Property Id As Integer
    Public Property IdPadre As Integer
    Public Property RigaComando As String = ""
    Public Property Principale As Boolean
End Class

Public NotInheritable Class WGEsitoRecuperoMotore
    Public Property Liberato As Boolean
    Public Property Descrizione As String = ""
End Class

Public NotInheritable Class WGTracciaMotore
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly log As New Logger()
    Private Shared _percorso As String = Nothing
    Private Shared _erroreRegistrato As Boolean = False
    Private Const DimensioneMassima As Long = 1048576

    Private Sub New()
    End Sub

    Private Shared Function Percorso() As String
        If _percorso IsNot Nothing Then Return _percorso
        Dim cartella As String = WGDb.WGPercorsiApplicazione.CartellaLog()
        If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
        _percorso = Path.Combine(cartella, "motore.txt")
        Return _percorso
    End Function

    Public Shared Sub Scrivi(testo As String)
        Try
            SyncLock _lock
                Dim file As String = Percorso()
                Dim info As New FileInfo(file)
                If info.Exists AndAlso info.Length > DimensioneMassima Then
                    Dim precedente As String = Path.Combine(Path.GetDirectoryName(file), "motore.1.txt")
                    If System.IO.File.Exists(precedente) Then System.IO.File.Delete(precedente)
                    System.IO.File.Move(file, precedente)
                End If
                Dim thread As String = If(WGMotoreBrowser.SuThreadInterfaccia, "UI", "T" & System.Threading.Thread.CurrentThread.ManagedThreadId.ToString())
                System.IO.File.AppendAllText(file, DateTime.Now.ToString("dd/MM HH:mm:ss.fff") & " " & thread & " " & If(testo, "") & Environment.NewLine)
            End SyncLock
        Catch ex As Exception
            If _erroreRegistrato Then Return
            _erroreRegistrato = True
            log.ScriviLog("WGTracciaMotore.Scrivi: " & ex.Message)
        End Try
    End Sub
End Class