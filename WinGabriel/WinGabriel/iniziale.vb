Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Punto di ingresso principale del programma, gestione dell'avvio, controllo dei lettori di schermo attivi e migrazione dei dati.
Module iniziale

    Private Declare Unicode Function DeleteFileW Lib "kernel32.dll" (ByVal lpFileName As String) As Boolean

    Friend appMutex As Mutex

    Friend risultatoScaricamentoFile As WGUtils.WGRisultatoAggiornamentoFile

    Friend Declare Auto Function FindWindow Lib "user32.dll" (ByVal lpClassName As String, ByVal lpWindowName As String) As IntPtr

    Friend Declare Auto Function SetForegroundWindow Lib "user32.dll" (ByVal hWnd As IntPtr) As Boolean

    Friend Declare Auto Function ShowWindow Lib "user32.dll" (ByVal hWnd As IntPtr, ByVal nCmdShow As Integer) As Boolean

    Friend Declare Function GetWindowThreadProcessId Lib "user32.dll" (ByVal hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger

    Friend Declare Function GetCurrentThreadId Lib "kernel32.dll" () As UInteger

    Friend Declare Function AttachThreadInput Lib "user32.dll" (ByVal idAttach As UInteger, ByVal idAttachTo As UInteger, ByVal fAttach As Boolean) As Boolean

    Friend Declare Function GetForegroundWindow Lib "user32.dll" () As IntPtr

    Friend Const SW_RESTORE As Integer = 9

    Friend Const NOME_MUTEX_ISTANZA_UNICA As String = "WinGabrielSingleInstanceMutex"

    Friend Const TITOLO_FINESTRA_MENU_INIZIALE As String = "Menù iniziale"

    Friend Const NOME_PROCESSO_AGGIORNATORE As String = "WGUpdater"

    Friend Const ATTESA_MASSIMA_CHIUSURA_AGGIORNATORE_MS As Integer = 15000

    Friend Const INTERVALLO_ATTESA_AGGIORNATORE_MS As Integer = 250

    Friend WithEvents domandaAggiornamento As WGDomanda

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Sub RimuoviZonaDaBinari()
        Try
            Dim cartellaBase As String = AppDomain.CurrentDomain.BaseDirectory

            If String.IsNullOrEmpty(cartellaBase) OrElse Not Directory.Exists(cartellaBase) Then
                Return
            End If

            Dim estensioni As String() = New String() {"*.exe", "*.dll"}

            For Each filtro As String In estensioni
                Try
                    For Each percorsoFile As String In Directory.GetFiles(cartellaBase, filtro, SearchOption.AllDirectories)
                        Try
                            DeleteFileW(percorsoFile & ":Zone.Identifier")
                        Catch exFile As Exception
                            ScriviLogBootstrap("RimuoviZonaDaBinari (file " & percorsoFile & "): " & exFile.Message)
                        End Try
                    Next
                Catch exFiltro As Exception
                    ScriviLogBootstrap("RimuoviZonaDaBinari (filtro " & filtro & "): " & exFiltro.Message & Environment.NewLine & exFiltro.StackTrace)
                End Try
            Next
        Catch ex As Exception
            ScriviLogBootstrap("RimuoviZonaDaBinari eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Function AgganciaWgControls() As Boolean
        Try
            logAvvio = New wgcontrols.Logger()
            wgControlsAgganciato = True
            Return True
        Catch ex As Exception
            ScriviLogBootstrap("AgganciaWgControls: impossibile agganciare wgcontrols.dll: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Sub ChiudiScreenReaderAttivi()
        Try
            ChiudiProcessiPerNome("nvda")
        Catch exNvda As Exception
            ScriviLogBootstrap("ChiudiScreenReaderAttivi (NVDA): " & exNvda.Message & Environment.NewLine & exNvda.StackTrace)
        End Try

        Try
            ChiudiProcessiPerNome("jfw")
        Catch exJaws As Exception
            ScriviLogBootstrap("ChiudiScreenReaderAttivi (JAWS): " & exJaws.Message & Environment.NewLine & exJaws.StackTrace)
        End Try

        ' Chi non si chiude con la forza (per esempio NVDA avviato come amministratore, se la conferma non è stata data) si chiude
        ' con il suo comando di uscita, subito e in sottofondo. Succede qui, dopo la richiesta di amministratore (andata a buon fine
        ' o no) e mai prima.
        Try
            ChiudiLettoriRimastiConWgControls()
        Catch exRimasti As Exception
            ScriviLogBootstrap("ChiudiScreenReaderAttivi (lettori rimasti): " & exRimasti.Message)
        End Try

        Try
            ChiudiProcessiPerNome("KSR")
        Catch exKsr As Exception
            ScriviLogBootstrap("ChiudiScreenReaderAttivi (KSR): " & exKsr.Message & Environment.NewLine & exKsr.StackTrace)
        End Try

        Try
            ChiudiProcessiPerNome("Narrator")
        Catch exNarrator As Exception
            ScriviLogBootstrap("ChiudiScreenReaderAttivi (Assistente vocale): " & exNarrator.Message & Environment.NewLine & exNarrator.StackTrace)
        End Try
    End Sub

    Friend Sub AvviaSorveglianzaLettori()
        Dim filo As New Thread(AddressOf SorvegliaLettori)
        filo.IsBackground = True
        filo.Name = "SorveglianzaLettori"
        filo.Start()
    End Sub

    ' Per mezzo minuto dall'avvio, ogni mezzo secondo: se NVDA o JAWS sono tornati in esecuzione (li ha riavviati Windows dopo la richiesta
    ' di amministratore) si chiudono di nuovo. Si ferma se si sta uscendo dal programma attivando proprio uno screen reader.
    Friend Sub SorvegliaLettori()
        Try
            Dim fine As DateTime = DateTime.UtcNow.AddSeconds(30)
            While DateTime.UtcNow < fine
                Thread.Sleep(500)
                If ChiusuraAutomaticaSospesaDaUscita() Then Exit While
                Dim presenti As Boolean = (Process.GetProcessesByName("nvda").Length > 0) OrElse (Process.GetProcessesByName("jfw").Length > 0)
                If Not presenti Then Continue While
                Try
                    ChiudiProcessiPerNome("nvda")
                    ChiudiProcessiPerNome("jfw")
                    ChiudiLettoriRimastiConWgControls()
                Catch exChiusura As Exception
                    ScriviLogBootstrap("SorvegliaLettori: " & exChiusura.Message)
                End Try
            End While
        Catch ex As Exception
            ScriviLogBootstrap("SorvegliaLettori: " & ex.Message)
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Function ChiusuraAutomaticaSospesaDaUscita() As Boolean
        Try
            Return WGScreenReader.ChiusuraAutomaticaSospesa
        Catch
            Return False
        End Try
    End Function

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Function ControlloRapidoAggiornamento() As Boolean
        Dim canale As String = RilascioFirmato.NormalizzaCanale(LeggiValoreConfigurazione("canale_aggiornamenti"))
        Return RilascioFirmato.AggiornatoESano("wingabriel", canale, WGDb.WGPercorsiApplicazione.CartellaCache(), AppDomain.CurrentDomain.BaseDirectory, 6000)
    End Function

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Sub ChiudiLettoriRimastiConWgControls()
        WGScreenReader.ChiudiAttivi()
    End Sub

    Friend Sub ChiudiProcessiPerNome(nomeProcesso As String)
        Try
            For Each processo As Process In Process.GetProcessesByName(nomeProcesso)
                Try
                    Try
                        processo.Kill()
                        processo.WaitForExit(3000)
                    Catch exChiusura As Exception
                        ScriviLogBootstrap("ChiudiProcessiPerNome (" & nomeProcesso & "): " & exChiusura.Message & Environment.NewLine & exChiusura.StackTrace)
                    End Try
                Finally
                    Try
                        processo.Dispose()
                    Catch exDispose As Exception
                        ScriviLogBootstrap("ChiudiProcessiPerNome (" & nomeProcesso & ", dispose): " & exDispose.Message)
                    End Try
                End Try
            Next
        Catch ex As Exception
            ScriviLogBootstrap("ChiudiProcessiPerNome eccezione (" & nomeProcesso & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub AlzaVolumeAvvio()
        Try
            Dim volumePreferito As Integer = VolumePreferitoAvvio()

            If volumePreferito < 0 Then Return

            Try
                Dim esito As Boolean = WGVolumeWindows.ImpostaVolumeSuoniProcessoCorrente(volumePreferito)

                If Not esito Then
                End If
            Catch exVolumeSuoni As Exception
                ScriviLogBootstrap("AlzaVolumeAvvio (volume sessione audio): " & exVolumeSuoni.Message & Environment.NewLine & exVolumeSuoni.StackTrace)
            End Try
        Catch ex As Exception
            ScriviLogBootstrap("AlzaVolumeAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub SistemaDefaultPunteggiatura()
        Try
            Dim configPunteggiatura As New WGConfig()

            Try
                If Not configPunteggiatura.EsisteId("livello_punteggiatura_lettura") Then
                    configPunteggiatura.AggiungiValore("livello_punteggiatura_lettura", "2")
                End If
            Catch exLettura As Exception
                ScriviLogAvvio("SistemaDefaultPunteggiatura (lettura): " & exLettura.Message & Environment.NewLine & exLettura.StackTrace)
            End Try

            Try
                If Not configPunteggiatura.EsisteId("livello_punteggiatura_scrittura") Then
                    configPunteggiatura.AggiungiValore("livello_punteggiatura_scrittura", "2")
                End If
            Catch exScrittura As Exception
                ScriviLogAvvio("SistemaDefaultPunteggiatura (scrittura): " & exScrittura.Message & Environment.NewLine & exScrittura.StackTrace)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("SistemaDefaultPunteggiatura eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Function RisolviAssembly(sender As Object, args As ResolveEventArgs) As Reflection.Assembly
        Try
            Dim nomeRichiesto As String = args.Name

            If nomeRichiesto.Contains(",") Then
                nomeRichiesto = nomeRichiesto.Substring(0, nomeRichiesto.IndexOf(","c))
            End If

            nomeRichiesto = nomeRichiesto.Trim()

            If String.IsNullOrWhiteSpace(nomeRichiesto) Then Return Nothing

            If nomeRichiesto.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            If nomeRichiesto.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If

            Dim percorso As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, nomeRichiesto & ".dll")

            If File.Exists(percorso) Then
                Return Reflection.Assembly.LoadFrom(percorso)
            End If

            If nomeRichiesto.Equals("System.Net.Http", StringComparison.OrdinalIgnoreCase) Then
                Return CaricaSystemNetHttpDisponibile()
            End If

            Return Nothing
        Catch ex As Exception
            ScriviLogBootstrap("RisolviAssembly eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    ' Il reindirizzamento puo' chiedere la 4.2.0.0 (assembly di riferimento di .NET 4.8),
    ' mentre nel GAC/runtime c'e' la versione installata (spesso 4.0.0.0). Si carica
    ' quella presente, senza richiedere una versione precisa.
    Friend Function CaricaSystemNetHttpDisponibile() As Reflection.Assembly
        Try
            For Each giaCaricato As Reflection.Assembly In AppDomain.CurrentDomain.GetAssemblies()
                Try
                    If String.Equals(giaCaricato.GetName().Name, "System.Net.Http", StringComparison.OrdinalIgnoreCase) Then
                        Return giaCaricato
                    End If
                Catch
                End Try
            Next

            Dim percorsoRuntime As String = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Net.Http.dll")
            If File.Exists(percorsoRuntime) Then
                Return Reflection.Assembly.LoadFrom(percorsoRuntime)
            End If

            Return Nothing
        Catch ex As Exception
            ScriviLogBootstrap("CaricaSystemNetHttpDisponibile eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Friend Function AggiornatoreInEsecuzione() As Boolean
        Try
            Dim processi As Process() = Process.GetProcessesByName(NOME_PROCESSO_AGGIORNATORE)
            If processi Is Nothing OrElse processi.Length = 0 Then Return False

            Dim aggiornatoreAttivo As Boolean = False

            For indice As Integer = 0 To processi.Length - 1
                Dim processo As Process = processi(indice)

                Try
                    If processo IsNot Nothing AndAlso Not processo.HasExited Then
                        aggiornatoreAttivo = True
                    End If
                Catch exProcesso As Exception
                    ScriviLogBootstrap("AggiornatoreInEsecuzione (verifica processo): " & exProcesso.Message)
                    aggiornatoreAttivo = True
                End Try

                Try
                    If processo IsNot Nothing Then processo.Dispose()
                Catch exSmaltimento As Exception
                    ScriviLogBootstrap("AggiornatoreInEsecuzione (smaltimento processo): " & exSmaltimento.Message)
                End Try
            Next

            Return aggiornatoreAttivo
        Catch ex As Exception
            ScriviLogBootstrap("AggiornatoreInEsecuzione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Function AvvioBloccatoDaAggiornamento() As Boolean
        Try
            If Not AggiornatoreInEsecuzione() Then Return False

            Dim tempoAtteso As Integer = 0

            While tempoAtteso < ATTESA_MASSIMA_CHIUSURA_AGGIORNATORE_MS
                Try
                    Thread.Sleep(INTERVALLO_ATTESA_AGGIORNATORE_MS)
                Catch exAttesa As Exception
                    ScriviLogBootstrap("AvvioBloccatoDaAggiornamento (attesa): " & exAttesa.Message)
                    Exit While
                End Try

                tempoAtteso += INTERVALLO_ATTESA_AGGIORNATORE_MS

                If Not AggiornatoreInEsecuzione() Then
                    Return False
                End If
            End While

            Return True
        Catch ex As Exception
            ScriviLogBootstrap("AvvioBloccatoDaAggiornamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Function AvvioConFileDaAprire() As Boolean
        Try
            Dim argomenti As String() = Environment.GetCommandLineArgs()
            If argomenti Is Nothing OrElse argomenti.Length <= 1 Then Return False

            Dim percorso As String = argomenti(1)
            If String.IsNullOrWhiteSpace(percorso) Then Return False

            Return File.Exists(percorso)
        Catch ex As Exception
            ScriviLogBootstrap("AvvioConFileDaAprire: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Sub PortaInPrimoPianoIstanzaEsistente()
        Try
            Dim finestraEsistente As IntPtr = FindWindow(Nothing, TITOLO_FINESTRA_MENU_INIZIALE)
            If finestraEsistente = IntPtr.Zero Then Return

            Try
                ShowWindow(finestraEsistente, SW_RESTORE)
            Catch exMostra As Exception
                ScriviLogBootstrap("PortaInPrimoPianoIstanzaEsistente (ShowWindow): " & exMostra.Message)
            End Try

            Try
                If Not SetForegroundWindow(finestraEsistente) Then
                    Dim hwndCorrente As IntPtr = GetForegroundWindow()
                    If hwndCorrente <> IntPtr.Zero AndAlso hwndCorrente <> finestraEsistente Then
                        Dim pidCorrente As UInteger = 0
                        Dim threadCorrente As UInteger = GetWindowThreadProcessId(hwndCorrente, pidCorrente)
                        Dim threadMio As UInteger = GetCurrentThreadId()
                        Dim agganciato As Boolean = False
                        Try
                            If threadCorrente <> 0UI AndAlso threadCorrente <> threadMio Then
                                agganciato = AttachThreadInput(threadMio, threadCorrente, True)
                            End If
                            SetForegroundWindow(finestraEsistente)
                        Finally
                            If agganciato Then AttachThreadInput(threadMio, threadCorrente, False)
                        End Try
                    End If
                End If
            Catch exPrimoPiano As Exception
                ScriviLogBootstrap("PortaInPrimoPianoIstanzaEsistente (SetForegroundWindow): " & exPrimoPiano.Message)
            End Try
        Catch ex As Exception
            ScriviLogBootstrap("PortaInPrimoPianoIstanzaEsistente: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Function VerificaIstanzaUnica() As Boolean
        Try
            If appMutex IsNot Nothing Then Return True
            If AvvioConFileDaAprire() Then Return True

            Dim creataNuova As Boolean = False

            Try
                appMutex = New Mutex(True, NOME_MUTEX_ISTANZA_UNICA, creataNuova)
            Catch exMutex As Exception
                ScriviLogBootstrap("VerificaIstanzaUnica (creazione mutex): " & exMutex.Message & Environment.NewLine & exMutex.StackTrace)
                appMutex = Nothing
                Return True
            End Try

            If creataNuova Then Return True

            PortaInPrimoPianoIstanzaEsistente()

            Try
                appMutex.Dispose()
            Catch exSmaltimento As Exception
                ScriviLogBootstrap("VerificaIstanzaUnica (smaltimento mutex): " & exSmaltimento.Message)
            End Try

            appMutex = Nothing
            Return False
        Catch ex As Exception
            ScriviLogBootstrap("VerificaIstanzaUnica: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Sub main()
        ' Rilanciato con /integrazione:installa o /integrazione:rimuovi (dalla configurazione guidata o dalle impostazioni, per avere i
        ' privilegi di amministratore): fa solo quel lavoro ed esce.
        Dim codiceIntegrazione As Integer = IntegrazioneWindows.EseguiDaRigaDiComando()
        If codiceIntegrazione >= 0 Then
            Environment.Exit(codiceIntegrazione)
            Return
        End If
        ' Prima di tutto il resto, perché il resto può creare la configurazione: serve a sapere se è una installazione nuova.
        Try
            AvvioAmministratore.RegistraProfiloNuovo()
        Catch exProfilo As Exception
            ScriviLogBootstrap("main: lettura del profilo non riuscita: " & exProfilo.Message)
        End Try
        WGReteEccezioni.Attiva()
        Try

            If AvvioBloccatoDaAggiornamento() Then
                Return
            End If
        Catch exAggiornatore As Exception
            ScriviLogBootstrap("main: verifica aggiornatore in esecuzione non riuscita: " & exAggiornatore.Message & Environment.NewLine & exAggiornatore.StackTrace)
        End Try

        ' Prima dell'istanza unica: si chiede di partire come amministratore e, se Windows parte con il nuovo WinGabriel, questo esce.
        ' Se la conferma non viene data si va avanti senza privilegi.
        Try
            If AvvioAmministratore.RilanciaSeServe() Then Return
        Catch exAmministratore As Exception
            ScriviLogBootstrap("main: richiesta di avvio come amministratore non riuscita: " & exAmministratore.Message)
        End Try

        Try
            If Not VerificaIstanzaUnica() Then
                Return
            End If
        Catch exIstanza As Exception
            ScriviLogBootstrap("main: verifica istanza unica non riuscita: " & exIstanza.Message & Environment.NewLine & exIstanza.StackTrace)
        End Try

        Try
            ChiudiScreenReaderAttivi()
        Catch exScreenReader As Exception
            ScriviLogBootstrap("main: impossibile chiudere gli screen reader attivi: " & exScreenReader.Message & Environment.NewLine & exScreenReader.StackTrace)
        End Try

        ' La richiesta di amministratore fa avviare NVDA e JAWS anche a Windows (strumenti per la schermata sicura): chiusi da noi,
        ' qualche secondo dopo la fine della richiesta Windows li riattiva. Per i primi secondi li si richiude se rispuntano.
        Try
            AvviaSorveglianzaLettori()
        Catch exSorveglianza As Exception
            ScriviLogBootstrap("main: impossibile avviare la sorveglianza degli screen reader: " & exSorveglianza.Message)
        End Try

        Try
            AnnunciaConSintesiBootstrap("WinGabriel in caricamento, attendere.")
        Catch exAnnuncio As Exception
            ScriviLogBootstrap("main: impossibile annunciare l'avvio di WinGabriel: " & exAnnuncio.Message & Environment.NewLine & exAnnuncio.StackTrace)
        End Try

        Try
            WGSuonoVerifica.Avvia()
        Catch exSuono As Exception
            ScriviLogBootstrap("main: impossibile avviare il suono di accompagnamento: " & exSuono.Message)
        End Try

        Try
            If Not SuoniVolumeAvvioDisattivati() Then
                AlzaVolumeAvvio()
            End If
        Catch exVolume As Exception
            ScriviLogBootstrap("main: impossibile alzare il volume all'avvio: " & exVolume.Message & Environment.NewLine & exVolume.StackTrace)
        End Try

        Try
            AddHandler AppDomain.CurrentDomain.AssemblyResolve, AddressOf RisolviAssembly
        Catch exHandler As Exception
            ScriviLogBootstrap("main: impossibile registrare il gestore AssemblyResolve: " & exHandler.Message)
        End Try

        Try
            AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
            AddHandler AppDomain.CurrentDomain.ProcessExit, AddressOf OnProcessExit
        Catch exGestori As Exception
            ScriviLogBootstrap("main: impossibile registrare i gestori globali delle eccezioni: " & exGestori.Message)
        End Try

        Try
            Dim wgControlsPronto As Boolean = False

            Try
                wgControlsPronto = VerificaEScaricaWgControls()
            Catch exVerifica As Exception
                ScriviLogBootstrap("Errore durante la verifica/download di wgcontrols.dll: " & exVerifica.Message & Environment.NewLine & exVerifica.StackTrace)
                wgControlsPronto = False
            End Try

            If Not wgControlsPronto Then
                Return
            End If

            RimuoviZonaDaBinari()

            If Not AgganciaWgControls() Then
                AnnunciaConSintesiBootstrap("Non riesco a caricare un componente necessario. WinGabriel non può avviarsi.")
                Return
            End If

            Try
                SistemaDefaultPunteggiatura()
            Catch exPunteggiatura As Exception
                ScriviLogAvvio("main: impossibile sistemare la configurazione della punteggiatura: " & exPunteggiatura.Message & Environment.NewLine & exPunteggiatura.StackTrace)
            End Try

            AvviaProgramma()
        Catch ex As Exception
            ScriviLogAvvio("ERRORE FATALE IN SUB MAIN: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            WGSuonoVerifica.Ferma()
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Sub AvviaProgramma()
        Try
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            Try
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
                AddHandler Application.ThreadException, AddressOf OnThreadException
            Catch exGestoreThread As Exception
                ScriviLogAvvio("AvviaProgramma: impossibile registrare il gestore ThreadException: " & exGestoreThread.Message)
            End Try

            Dim aggiornamentoTrovato As Boolean = False

            Try
                Dim verifica As New connessione()
                If verifica.VerificaConnessione() Then
                    AggiornaComponentiSilenti()

                    If VerificaAggiornamentiAvvioAttiva() Then
                        aggiornamentoTrovato = VerificaAggiornamentiDisponibili()
                    End If
                End If
            Catch exConnessione As Exception
                ScriviLogAvvio("Errore verifica connessione in AvviaProgramma: " & exConnessione.Message & Environment.NewLine & exConnessione.StackTrace)
            End Try

            WGSuonoVerifica.Ferma()

            If aggiornamentoTrovato Then
                GestisciDomandaAggiornamento()
            Else
                EseguiAvvioNormale()
            End If

        Catch ex As Exception
            ScriviLogAvvio("ERRORE FATALE IN AVVIAPROGRAMMA: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub GestisciDomandaAggiornamento()
        Try
            If Not VerificaIstanzaUnica() Then Return

            If AvviaFileDaRigaDiComando() Then Return

            domandaAggiornamento = New WGDomanda()
            domandaAggiornamento.avvia("È disponibile un nuovo aggiornamento di WinGabriel, vuoi scaricarlo e installarlo adesso?")
            Application.Run()
        Catch ex As Exception
            ScriviLogAvvio("Errore nell'avvio della domanda di aggiornamento: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                EseguiAvvioNormale()
            Catch exRipiego As Exception
                ScriviLogAvvio("GestisciDomandaAggiornamento (ripiego avvio normale): " & exRipiego.Message & Environment.NewLine & exRipiego.StackTrace)
            End Try
        End Try
    End Sub

    Friend Sub ProseguiVersoMenuIniziale()
        Try
            PreparaAmbienteEAvviaMenuIniziale()
        Catch ex As Exception
            ScriviLogAvvio("ProseguiVersoMenuIniziale: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub RispostaAggiornamento(rispostadom As DialogResult) Handles domandaAggiornamento.rispostadomanda
        Try
            If rispostadom = DialogResult.Yes Then

                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Sta per iniziare l'aggiornamento del programma")
                messaggio.ShowDialog()
                Dim updaterPath As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WGUpdater.exe")

                If Not File.Exists(updaterPath) Then
                    Dim avviso As New WGMessaggi()
                    avviso.Messaggio("Non e' stato possibile avviare l'aggiornamento. WinGabriel prosegue normalmente.")
                    avviso.ShowDialog()
                    ProseguiVersoMenuIniziale()
                    Return
                End If

                WGUtils.RimuoviMarkOfTheWeb(updaterPath)
                Dim infoAvvioUpdater As New ProcessStartInfo()
                infoAvvioUpdater.FileName = updaterPath
                infoAvvioUpdater.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                ImpostaAvvioAggiornatore(infoAvvioUpdater)

                Dim processoUpdater As Process = Process.Start(infoAvvioUpdater)

                If processoUpdater Is Nothing OrElse processoUpdater.HasExited Then
                End If

                Try
                    WGUtils.uscita()
                Catch exUscita As Exception
                    log.ScriviLog("RispostaAggiornamento (uscita): " & exUscita.Message)
                End Try

                Application.Exit()
                Environment.Exit(0)
            Else
                ProseguiVersoMenuIniziale()
            End If
        Catch ex As Exception

            ScriviLogAvvio("Errore durante avvio WGUpdater: " & ex.Message & Environment.NewLine & ex.StackTrace)

            If rispostadom = DialogResult.Yes Then
                Try
                    ProseguiVersoMenuIniziale()
                Catch exProseguiErrore As Exception
                    ScriviLogAvvio("RispostaAggiornamento (prosecuzione dopo errore di aggiornamento): " & exProseguiErrore.Message)
                End Try
            Else
                Try
                    ProseguiVersoMenuIniziale()
                Catch exProsegui As Exception
                    ScriviLogAvvio("RispostaAggiornamento (prosecuzione dopo errore): " & exProsegui.Message)
                End Try
            End If
        End Try
    End Sub

    Friend Function AvviaFileDaRigaDiComando() As Boolean
        Try
            Dim argomenti As String() = Environment.GetCommandLineArgs()
            If argomenti Is Nothing OrElse argomenti.Length <= 1 Then Return False

            Dim filePath As String = argomenti(1)

            If String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                Return False
            End If

            Dim fileExtension As String = Path.GetExtension(filePath).ToLower()
            Dim textExtensions() As String = {".txt", ".rtf", ".log", ".ini"}
            Dim audioVideoExtensions() As String = {".mp3", ".wav", ".wma", ".aac", ".flac", ".mp4", ".avi", ".mkv", ".mov", ".wmv"}

            If Array.IndexOf(textExtensions, fileExtension) < 0 AndAlso Array.IndexOf(audioVideoExtensions, fileExtension) < 0 Then
                Return False
            End If

            Try
                Dim fileManager As New WGFileManager.MenuFile()
                fileManager.Apri(filePath)
            Catch exApertura As Exception
                ScriviLogAvvio("AvviaFileDaRigaDiComando (apertura file da 'Invia a'): " & exApertura.Message & Environment.NewLine & exApertura.StackTrace)
            End Try

            WGUtils.uscita()
            Return True
        Catch ex As Exception
            ScriviLogAvvio("AvviaFileDaRigaDiComando: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Sub PreparaAmbienteEAvviaMenuIniziale()
        Try
            If menuInizialeGiaAvviato Then Return
            menuInizialeGiaAvviato = True

            Try
                AssistenteVocale.PrecaricaLibrerieNative()
            Catch exLibrerie As Exception
                ScriviLogAvvio("PreparaAmbienteEAvviaMenuIniziale (librerie native): " & exLibrerie.Message & Environment.NewLine & exLibrerie.StackTrace)
            End Try

            Try
                AvviaSintesiVocaleInAnticipo()
            Catch exSintesi As Exception
                ScriviLogAvvio("PreparaAmbienteEAvviaMenuIniziale (sintesi vocale): " & exSintesi.Message & Environment.NewLine & exSintesi.StackTrace)
            End Try

            Try
                GestisciCollegamentoDesktopIniziale()
            Catch exCollegamento As Exception
                ScriviLogAvvio("PreparaAmbienteEAvviaMenuIniziale (collegamento desktop): " & exCollegamento.Message & Environment.NewLine & exCollegamento.StackTrace)
            End Try

            Try
                If StandbyDisattivatoDuranteUso() Then
                    WGNoStandby.Avvia()
                End If
            Catch exStandby As Exception
                ScriviLogAvvio("PreparaAmbienteEAvviaMenuIniziale (standby): " & exStandby.Message & Environment.NewLine & exStandby.StackTrace)
            End Try

            AvviaMenuInizialeEServizi()
        Catch ex As Exception
            menuInizialeGiaAvviato = False
            ScriviLogAvvio("PreparaAmbienteEAvviaMenuIniziale: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Sub EseguiAvvioNormale()
        Try
            RegistraBrowserWinGabriel()

            If AvviaFileDaRigaDiComando() Then Return

            If Not VerificaIstanzaUnica() Then Return

            PreparaAmbienteEAvviaMenuIniziale()

            If Not menuInizialeGiaAvviato Then
                AnnunciaConSintesiBootstrap("Non riesco ad aprire il menu applicazioni. WinGabriel non puo' avviarsi.")
                Return
            End If

            Application.Run()

        Catch ex As Exception

            ScriviLogAvvio("ERRORE FATALE IN ESEGUI AVVIO NORMALE: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub AvviaSintesiVocaleInAnticipo()
        Try
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then Return
        Catch ex As Exception
            log.ScriviLog("AvviaSintesiVocaleInAnticipo eccezione: " & ex.Message)
        End Try
    End Sub

    Friend Sub OnProcessExit(sender As Object, e As EventArgs)
        Try
            If WGNoStandby.Attivo Then WGNoStandby.Ferma()
        Catch exStandby As Exception
            ScriviLogAvvio("OnProcessExit (standby): " & exStandby.Message)
        End Try

        ChiudiProcessiAusiliari()

        If appMutex IsNot Nothing Then
            appMutex.Dispose()
        End If
    End Sub

    Friend Sub ChiudiProcessiAusiliari()
        Try
            ChiudiProcessiPerNome("WGService")
        Catch exServizio As Exception
            ScriviLogBootstrap("ChiudiProcessiAusiliari (WGService): " & exServizio.Message & Environment.NewLine & exServizio.StackTrace)
        End Try

        Try
            ChiudiProcessiPerNome("WGSapi")
        Catch exSapi As Exception
            ScriviLogBootstrap("ChiudiProcessiAusiliari (WGSapi): " & exSapi.Message & Environment.NewLine & exSapi.StackTrace)
        End Try
    End Sub

    Friend Sub OnThreadException(sender As Object, e As ThreadExceptionEventArgs)
        Try
            Dim ex As Exception = e.Exception
            ScriviLogAvvio($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Eccezione non gestita sul thread dell'interfaccia: {If(ex IsNot Nothing, ex.ToString(), "(nulla)")}")
        Catch exLog As Exception
            Try
                ScriviLogBootstrap("OnThreadException (log): " & exLog.Message)
            Catch
            End Try
        End Try
    End Sub

    Friend Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Try

            Throw CType(e.ExceptionObject, Exception)
        Catch ex As Exception
            If appMutex IsNot Nothing Then
                Try
                    appMutex.ReleaseMutex()
                    appMutex.Dispose()

                Catch exMutex As Exception
                End Try
            End If
            ScriviLogAvvio($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Eccezione non gestita nell'applicazione: {ex.ToString()}")

            ChiudiProcessiAusiliari()

            Try
                WGUtils.uscita()
            Catch exUscita As Exception
                ScriviLogBootstrap("OnUnhandledException (uscita): " & exUscita.Message)
            End Try
        End Try
    End Sub

    Friend Function VerificaEScaricaWgControls() As Boolean
        Dim percorsoLocaleWGControls As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wgcontrols.dll")

        Try
            If System.IO.File.Exists(percorsoLocaleWGControls) Then
                Return True
            End If
        Catch exControllo As Exception
            ScriviLogBootstrap("VerificaEScaricaWgControls (controllo presenza file): " & exControllo.Message & Environment.NewLine & exControllo.StackTrace)
        End Try

        Const tentativiMassimi As Integer = 3

        For tentativo As Integer = 1 To tentativiMassimi
            Try
                ' Il file è quello del rilascio firmato scelto (stabile o beta): senza rilascio valido non si scarica niente.
                Dim erroreRilascio As String = ""
                Dim puntatoreRilascio As RilascioFirmato.Puntatore = RilascioFirmato.LeggiPuntatore("wingabriel", RilascioFirmato.NormalizzaCanale(LeggiValoreConfigurazione("canale_aggiornamenti")), erroreRilascio)
                If puntatoreRilascio Is Nothing Then Throw New InvalidOperationException("nessun rilascio valido: " & erroreRilascio)
                Dim urlRemotoWGControls As String = RilascioFirmato.UrlFile(RilascioFirmato.UrlBaseFile(puntatoreRilascio), "wgcontrols.dll")

                Using client As New WebClient()
                    client.Headers.Add(HttpRequestHeader.UserAgent, "WinGabriel/1.0")
                    client.DownloadFile(urlRemotoWGControls, percorsoLocaleWGControls)
                End Using

                If System.IO.File.Exists(percorsoLocaleWGControls) Then
                    Return True
                End If
            Catch exDownload As Exception
                ScriviLogBootstrap("VerificaEScaricaWgControls, tentativo " & tentativo.ToString() & " di " & tentativiMassimi.ToString() & " fallito: " & exDownload.Message & Environment.NewLine & exDownload.StackTrace)

                Try
                    If File.Exists(percorsoLocaleWGControls) Then
                        File.Delete(percorsoLocaleWGControls)
                    End If
                Catch exPulizia As Exception
                    ScriviLogBootstrap("VerificaEScaricaWgControls, pulizia file parziale fallita: " & exPulizia.Message & Environment.NewLine & exPulizia.StackTrace)
                End Try
            End Try

            If tentativo < tentativiMassimi Then
                Try
                    Thread.Sleep(2000)
                Catch exAttesa As Exception
                    ScriviLogBootstrap("VerificaEScaricaWgControls, attesa tra i tentativi fallita: " & exAttesa.Message)
                End Try
            End If
        Next

        AnnunciaConSintesiBootstrap("Non riesco a scaricare un componente necessario. WinGabriel non può avviarsi. Controlla la connessione a internet.")

        Return False
    End Function

    Friend Const CHIAVE_REGISTRO_VOCI_ONECORE As String = "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices"

    Friend Const CHIAVE_REGISTRO_VOCI_CLASSICHE_32BIT As String = "HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices"

    Friend Const CHIAVE_REGISTRO_VOCI_CLASSICHE_NATIVA As String = "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices"

    Friend Const LCID_ITALIANO As String = "410"

    Friend Function OttieniDescrizioneTokenVoceCom(token As Object) As String
        Try
            If token Is Nothing Then Return ""
            Return CStr(token.GetDescription())
        Catch ex As Exception
            ScriviLogAvvio("OttieniDescrizioneTokenVoceCom eccezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Function TokenVoceComEItaliano(token As Object) As Boolean
        Try
            If token Is Nothing Then Return False

            Try
                Dim linguaggio As String = CStr(token.GetAttribute("Language"))
                If Not String.IsNullOrEmpty(linguaggio) Then
                    Dim partiLinguaggio As String() = linguaggio.Split(";"c)
                    For Each parte As String In partiLinguaggio
                        If parte.Trim().Equals(LCID_ITALIANO, StringComparison.OrdinalIgnoreCase) Then
                            Return True
                        End If
                    Next
                End If
            Catch exAttributo As Exception
                ScriviLogAvvio("TokenVoceComEItaliano (attributo Language): " & exAttributo.Message)
            End Try

            Dim descrizione As String = OttieniDescrizioneTokenVoceCom(token)
            If Not String.IsNullOrEmpty(descrizione) Then
                If descrizione.IndexOf("italian", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            End If

            Return False
        Catch ex As Exception
            ScriviLogAvvio("TokenVoceComEItaliano eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Sub AggiungiTokenVociDaCategoriaCom(percorsoRegistro As String, elencoDestinazione As List(Of Object), Optional percorsoRegistroFallback As String = "")
        Dim categoria As Object = Nothing
        Dim enumeratore As Object = Nothing
        Dim conteggioAggiunti As Integer = 0
        Try
            categoria = CreateObject("SAPI.SpObjectTokenCategory")
            If categoria Is Nothing Then Return

            categoria.SetId(percorsoRegistro, False)
            enumeratore = categoria.EnumerateTokens("", "")
            If enumeratore Is Nothing Then Return

            Dim conteggio As Integer = CInt(enumeratore.Count)
            For indice As Integer = 0 To conteggio - 1
                Try
                    Dim token As Object = enumeratore.Item(indice)
                    If token IsNot Nothing Then
                        elencoDestinazione.Add(token)
                        conteggioAggiunti += 1
                    End If
                Catch exItem As Exception
                    ScriviLogAvvio("AggiungiTokenVociDaCategoriaCom (elemento " & indice.ToString() & ", " & percorsoRegistro & "): " & exItem.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLogAvvio("AggiungiTokenVociDaCategoriaCom eccezione (" & percorsoRegistro & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If enumeratore IsNot Nothing Then Marshal.ReleaseComObject(enumeratore)
            Catch exRilascioEnum As Exception
                ScriviLogAvvio("AggiungiTokenVociDaCategoriaCom (rilascio enumeratore): " & exRilascioEnum.Message)
            End Try
            Try
                If categoria IsNot Nothing Then Marshal.ReleaseComObject(categoria)
            Catch exRilascioCategoria As Exception
                ScriviLogAvvio("AggiungiTokenVociDaCategoriaCom (rilascio categoria): " & exRilascioCategoria.Message)
            End Try
        End Try

        If conteggioAggiunti = 0 AndAlso Not String.IsNullOrEmpty(percorsoRegistroFallback) Then
            Try
                AggiungiTokenVociDaCategoriaCom(percorsoRegistroFallback, elencoDestinazione)
            Catch exFallback As Exception
                ScriviLogAvvio("AggiungiTokenVociDaCategoriaCom (fallback " & percorsoRegistroFallback & "): " & exFallback.Message & Environment.NewLine & exFallback.StackTrace)
            End Try
        End If
    End Sub

    Friend Sub AnnunciaConSintesiBootstrap(testo As String)
        Try
            If Threading.Thread.CurrentThread.GetApartmentState() = Threading.ApartmentState.STA Then
                AnnunciaConSintesiBootstrapSta(testo)
                Return
            End If
            Dim filo As New Threading.Thread(Sub() AnnunciaConSintesiBootstrapSta(testo))
            filo.SetApartmentState(Threading.ApartmentState.STA)
            filo.IsBackground = True
            filo.Start()
            filo.Join(15000)
        Catch ex As Exception
            ScriviLogAvvio("AnnunciaConSintesiBootstrap (thread STA): " & ex.Message)
        End Try
    End Sub

    Friend Sub AnnunciaConSintesiBootstrapSta(testo As String)
        Dim voceCom As Object = Nothing
        Dim elencoTokenVoci As New List(Of Object)

        Try
            Dim indiceVoce As Integer = -1
            Dim percentualeVelocita As Integer = -1
            Dim percentualeVolume As Integer = -1

            Try
                Dim percorsoConfig As String = AvvioAmministratore.PercorsoConfigurazione()

                If File.Exists(percorsoConfig) Then
                    Dim documentoConfig As New XmlDocument()
                    documentoConfig.Load(percorsoConfig)

                    Dim nodoVoce As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='SapiVoice']")
                    If nodoVoce IsNot Nothing Then
                        Integer.TryParse(nodoVoce.InnerText, indiceVoce)
                    End If

                    Dim nodoVelocita As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='SapiRate']")
                    If nodoVelocita IsNot Nothing Then
                        Integer.TryParse(nodoVelocita.InnerText, percentualeVelocita)
                    End If

                    Dim nodoVolume As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='SapiVolume']")
                    If nodoVolume IsNot Nothing Then
                        Integer.TryParse(nodoVolume.InnerText, percentualeVolume)
                    End If
                End If
            Catch exConfig As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (lettura config): " & exConfig.Message & Environment.NewLine & exConfig.StackTrace)
            End Try

            Try
                voceCom = CreateObject("SAPI.SpVoice")
            Catch exCreazione As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (creazione SAPI.SpVoice): " & exCreazione.Message & Environment.NewLine & exCreazione.StackTrace)
            End Try

            If voceCom Is Nothing Then
                Return
            End If

            Try
                AggiungiTokenVociDaCategoriaCom(CHIAVE_REGISTRO_VOCI_CLASSICHE_32BIT, elencoTokenVoci, CHIAVE_REGISTRO_VOCI_CLASSICHE_NATIVA)
            Catch exClassici As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (enumerazione voci classiche): " & exClassici.Message & Environment.NewLine & exClassici.StackTrace)
            End Try

            Try
                AggiungiTokenVociDaCategoriaCom(CHIAVE_REGISTRO_VOCI_ONECORE, elencoTokenVoci)
            Catch exOneCore As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (enumerazione voci onecore): " & exOneCore.Message & Environment.NewLine & exOneCore.StackTrace)
            End Try

            If elencoTokenVoci.Count = 0 Then
                Return
            End If

            Dim indiceScelto As Integer = 0
            Dim voceConfigurataValida As Boolean = (indiceVoce >= 0 AndAlso indiceVoce < elencoTokenVoci.Count)
            Dim voceConfigurataItaliana As Boolean = False

            If voceConfigurataValida Then
                voceConfigurataItaliana = TokenVoceComEItaliano(elencoTokenVoci(indiceVoce))
            End If

            If voceConfigurataItaliana Then
                indiceScelto = indiceVoce
            Else
                Dim indiceItalianoTrovato As Integer = -1
                For indice As Integer = 0 To elencoTokenVoci.Count - 1
                    If TokenVoceComEItaliano(elencoTokenVoci(indice)) Then
                        indiceItalianoTrovato = indice
                        Exit For
                    End If
                Next

                If indiceItalianoTrovato >= 0 Then
                    indiceScelto = indiceItalianoTrovato
                ElseIf voceConfigurataValida Then
                    indiceScelto = indiceVoce
                Else
                    indiceScelto = 0
                End If
            End If

            Try
                voceCom.Voice = elencoTokenVoci(indiceScelto)
            Catch exImpostaVoce As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (impostazione voce): " & exImpostaVoce.Message & Environment.NewLine & exImpostaVoce.StackTrace)
            End Try

            Try
                If percentualeVelocita >= 0 AndAlso percentualeVelocita <= 100 Then
                    Dim velocitaSapi As Integer = (percentualeVelocita \ 5) - 10
                    If velocitaSapi < -10 Then velocitaSapi = -10
                    If velocitaSapi > 10 Then velocitaSapi = 10
                    voceCom.Rate = velocitaSapi
                End If
            Catch exVelocita As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (impostazione velocità): " & exVelocita.Message & Environment.NewLine & exVelocita.StackTrace)
            End Try

            Try
                If percentualeVolume >= 0 AndAlso percentualeVolume <= 100 Then
                    voceCom.Volume = percentualeVolume
                End If
            Catch exVolume As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (impostazione volume): " & exVolume.Message & Environment.NewLine & exVolume.StackTrace)
            End Try

            Try
                voceCom.Speak(testo, 0)
            Catch exParla As Exception
                ScriviLogAvvio("AnnunciaConSintesiBootstrap (Speak): " & exParla.Message & Environment.NewLine & exParla.StackTrace)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("AnnunciaConSintesiBootstrap eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            For Each token As Object In elencoTokenVoci
                Try
                    Marshal.ReleaseComObject(token)
                Catch
                End Try
            Next

            Try
                If voceCom IsNot Nothing Then Marshal.ReleaseComObject(voceCom)
            Catch
            End Try
        End Try
    End Sub

    Friend Sub ScaricaComponenteSincrono(url As String, percorso As String)
        Try
            Dim request As HttpWebRequest = CType(WebRequest.Create(url), HttpWebRequest)
            request.Method = "HEAD"
            request.Timeout = 5000
            Using response As HttpWebResponse = CType(request.GetResponse(), HttpWebResponse)
                Dim dataRemota As DateTime = response.LastModified

                Dim dataLocale As DateTime = DateTime.MinValue
                If File.Exists(percorso) Then dataLocale = File.GetLastWriteTime(percorso)
                If dataRemota > dataLocale Then
                    Using client As New WebClient()
                        client.DownloadFile(url, percorso)
                        File.SetLastWriteTime(percorso, dataRemota)
                    End Using
                End If
            End Using
        Catch ex As Exception
        End Try
    End Sub

    Friend Sub AggiornaComponentiSilenti()
        Try
            Dim percorsoAddon As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wingabriel.nvda-addon")
            Task.Run(Sub()
                         Try
                             ' Il componente è quello del rilascio firmato scelto: senza rilascio valido non si scarica niente.
                             Dim erroreRilascio As String = ""
                             Dim p As RilascioFirmato.Puntatore = RilascioFirmato.LeggiPuntatore("wingabriel", RilascioFirmato.NormalizzaCanale(LeggiValoreConfigurazione("canale_aggiornamenti")), erroreRilascio)
                             If p Is Nothing Then Return
                             ScaricaComponenteSincrono(RilascioFirmato.UrlFile(RilascioFirmato.UrlBaseFile(p), "wingabriel.nvda-addon"), percorsoAddon)
                         Catch exAddon As Exception
                             ScriviLogAvvio("AggiornaComponentiSilenti: " & exAddon.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
        End Try
    End Sub

    Friend Sub AvviaMenuInizialeEServizi()
        Try
            Try
                EstrattoreArticoli.Registra()
            Catch exEstrattore As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (estrattore degli articoli): " & exEstrattore.Message)
            End Try

            Try
                AssistenteVocale.AvviaSeAttivoEAttendi(New Action(AddressOf ApriMenuIniziale))
            Catch exAssistente As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (assistente vocale): " & exAssistente.Message & Environment.NewLine & exAssistente.StackTrace)
                ApriMenuIniziale()
            End Try

            ProgrammaAvvioWGService()

            Try
                ApplicaLettoreSchermoWinGabriel()
            Catch exLettore As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (lettore di schermo): " & exLettore.Message & Environment.NewLine & exLettore.StackTrace)
            End Try

            Try
                ServerVoceLettore.Avvia()
            Catch exServerVoce As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (pipe della voce): " & exServerVoce.Message & Environment.NewLine & exServerVoce.StackTrace)
            End Try

            Try
                AvvioLettorePreferito.Registra()
            Catch exAvvioLettore As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (avvio del lettore preferito): " & exAvvioLettore.Message & Environment.NewLine & exAvvioLettore.StackTrace)
            End Try

            Try
                RegistraOriginiBackup()
            Catch exOrigini As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (origini backup): " & exOrigini.Message)
            End Try

            Try
                AvviaMigrazioneCartellaDatiSeNecessaria()
            Catch exMigrazione As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (migrazione cartella dati): " & exMigrazione.Message)
            End Try

            Try
                WGCloudDati.Registra()
                RegistraSezioniSincronizzazioneCloud()
                WGSincronizzazioneCloud.Avvia()
            Catch exSincronizzazione As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (sincronizzazione tra computer): " & exSincronizzazione.Message)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("AvviaMenuInizialeEServizi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                ApriMenuIniziale()
            Catch exMenu As Exception
                ScriviLogAvvio("AvviaMenuInizialeEServizi (apertura menu di ripiego): " & exMenu.Message & Environment.NewLine & exMenu.StackTrace)
            End Try
        End Try
    End Sub

    Friend Sub ApriMenuIniziale()
        Try
            If menuInizialeAperto Then Return
            menuInizialeAperto = True
            Dim menuInizialeObj As New menuiniziale()
        Catch ex As Exception
            ScriviLogAvvio("ApriMenuIniziale: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend menuInizialeAperto As Boolean = False

    Friend menuInizialeGiaAvviato As Boolean = False

    Friend Sub RiallineaModuliDatiDopoMigrazione()
        Try
            If PreavvisoManager.IstanzaAttiva IsNot Nothing Then
                PreavvisoManager.IstanzaAttiva.RiallineaCartellaDatiAgenda()
            Else
                CLSAgenda.ReimpostaIstanza()
            End If
        Catch ex As Exception
            ScriviLogAvvio("RiallineaModuliDatiDopoMigrazione: " & ex.Message)
        End Try
    End Sub

    Friend Sub CompletaMigrazioneCartellaDati()
        Try
            If _processoMigrazioneDati IsNot Nothing Then
                Try
                    _processoMigrazioneDati.Fine()
                Catch exFine As Exception
                    ScriviLogAvvio("CompletaMigrazioneCartellaDati chiusura processo: " & exFine.Message)
                End Try
                _processoMigrazioneDati = Nothing
            End If

            Try
                RiallineaModuliDatiDopoMigrazione()
            Catch exRiallineo As Exception
                ScriviLogAvvio("CompletaMigrazioneCartellaDati riallineamento: " & exRiallineo.Message)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("CompletaMigrazioneCartellaDati: " & ex.Message)
        End Try
    End Sub

    Friend Sub LavoroMigrazioneCartellaDati()
        Try
            WGDb.WGDatabase.EseguiMigrazioneCartellaDati(AddressOf AvanzamentoMigrazioneCartellaDati)
        Catch ex As Exception
            ScriviLogAvvio("LavoroMigrazioneCartellaDati: " & ex.Message)
        Finally
            Try
                EseguiSuInterfacciaPrincipale(New MethodInvoker(AddressOf CompletaMigrazioneCartellaDati))
            Catch exChiusura As Exception
                ScriviLogAvvio("LavoroMigrazioneCartellaDati chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Sub

    Friend Sub AvanzamentoMigrazioneCartellaDati(fatti As Integer, totale As Integer)
        Try
            If _processoMigrazioneDati Is Nothing Then Return
            If totale <= 0 Then Return
            Dim testo As String = fatti.ToString() & " di " & totale.ToString()
            EseguiSuInterfacciaPrincipale(New MethodInvoker(Sub() AggiornaStatoMigrazione(testo)))
        Catch ex As Exception
            ScriviLogAvvio("AvanzamentoMigrazioneCartellaDati: " & ex.Message)
        End Try
    End Sub

    Friend Sub AggiornaStatoMigrazione(testo As String)
        Try
            If _processoMigrazioneDati Is Nothing Then Return
            _processoMigrazioneDati.AggiornaStato(testo)
        Catch ex As Exception
            ScriviLogAvvio("AggiornaStatoMigrazione: " & ex.Message)
        End Try
    End Sub

    Friend Sub AvviaMigrazioneCartellaDatiSeNecessaria()
        Try
            If Not WGDb.WGDatabase.MigrazioneCartellaDatiNecessaria() Then Return

            Try
                _processoMigrazioneDati = New WGProcessoInCorso()
                _processoMigrazioneDati.Avvia("Sto raccogliendo i dati di WinGabriel in una sottocartella dedicata, attendi", False)
            Catch exProcesso As Exception
                ScriviLogAvvio("AvviaMigrazioneCartellaDatiSeNecessaria avvio processo: " & exProcesso.Message)
            End Try

            Task.Run(New Action(AddressOf LavoroMigrazioneCartellaDati))
        Catch ex As Exception
            ScriviLogAvvio("AvviaMigrazioneCartellaDatiSeNecessaria: " & ex.Message)
        End Try
    End Sub

    Friend _processoMigrazioneDati As WGProcessoInCorso = Nothing

    Friend Sub RegistraSezioniSincronizzazioneCloud()
        Try
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneContatti, AddressOf UnisciContattiCloud)
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneAppuntamenti, AddressOf UnisciAppuntamentiCloud)
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneRadioPreferiti, AddressOf UnisciRadioPreferiteCloud)
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneRadioRecenti, AddressOf UnisciRadioRecentiCloud)
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneCategorieTestate, AddressOf UnisciCategorieTestateCloud)
            WGSincronizzazioneCloud.RegistraSezione(WGSincronizzazioneCloud.SezioneTestate, AddressOf UnisciTestateCloud)
            WGSincronizzazioneCloud.RegistraSezione(TvPromemoria.SezionePromemoriaRadio, AddressOf UnisciPromemoriaRadioCloud)
            WGSincronizzazioneCloud.RegistraSezione(TvPromemoria.SezioneSeguitiRadio, AddressOf UnisciSeguitiRadioCloud)
            WGSincronizzazioneCloud.RegistraSezione(TvPromemoria.SezionePromemoriaTv, AddressOf UnisciPromemoriaTvCloud)
            WGSincronizzazioneCloud.RegistraSezione(TvPromemoria.SezioneSeguitiTv, AddressOf UnisciSeguitiTvCloud)
        Catch ex As Exception
            ScriviLogAvvio("RegistraSezioniSincronizzazioneCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' Promemoria e programmi seguiti delle radio (con le radio sincronizzate) e della televisione.
    Private Function UnisciPromemoriaProgrammiCloud(tipo As String, seguiti As Boolean, vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            If String.Equals(tipo, TvPromemoria.TIPO_RADIO, StringComparison.OrdinalIgnoreCase) AndAlso Not WGSincronizzazioneCloud.RadioSincronizzata Then Return Nothing
            Dim unite As List(Of Dictionary(Of String, Object)) = TvPromemoria.UnisciSezione(tipo, seguiti, vociRemote)
            If unite Is Nothing Then avvisi.Add(If(seguiti, "Non è stato possibile sincronizzare i programmi seguiti.", "Non è stato possibile sincronizzare i promemoria dei programmi."))
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciPromemoriaProgrammiCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add(If(seguiti, "Non è stato possibile sincronizzare i programmi seguiti.", "Non è stato possibile sincronizzare i promemoria dei programmi."))
            Return Nothing
        End Try
    End Function

    Friend Function UnisciPromemoriaRadioCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Return UnisciPromemoriaProgrammiCloud(TvPromemoria.TIPO_RADIO, False, vociRemote, avvisi)
    End Function

    Friend Function UnisciSeguitiRadioCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Return UnisciPromemoriaProgrammiCloud(TvPromemoria.TIPO_RADIO, True, vociRemote, avvisi)
    End Function

    Friend Function UnisciPromemoriaTvCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Return UnisciPromemoriaProgrammiCloud(TvPromemoria.TIPO_TV, False, vociRemote, avvisi)
    End Function

    Friend Function UnisciSeguitiTvCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Return UnisciPromemoriaProgrammiCloud(TvPromemoria.TIPO_TV, True, vociRemote, avvisi)
    End Function

    Friend Function UnisciContattiCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Dim contatti As CLSContatti = Nothing
        Dim istanzaTemporanea As Boolean = False
        Try
            Dim chiaveRubrica As String = WGSincronizzazioneCloud.RubricaSincronizzata
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then Return Nothing
            contatti = CLSContatti.IstanzaConRubricaAperta(chiaveRubrica)
            If contatti Is Nothing Then
                contatti = New CLSContatti()
                istanzaTemporanea = True
            End If
            Dim unite As List(Of Dictionary(Of String, Object)) = contatti.UnisciSincronizzazioneFile(chiaveRubrica, vociRemote)
            If Not String.IsNullOrWhiteSpace(contatti.UltimoErrore) Then avvisi.Add(contatti.UltimoErrore)
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciContattiCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare la rubrica.")
            Return Nothing
        Finally
            If istanzaTemporanea AndAlso contatti IsNot Nothing Then
                Try
                    contatti.Dispose()
                Catch exChiusura As Exception
                    ScriviLogAvvio("UnisciContattiCloud chiusura: " & exChiusura.Message)
                End Try
            End If
        End Try
    End Function

    Friend Function UnisciAppuntamentiCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            Dim chiaveAgenda As String = WGSincronizzazioneCloud.AgendaSincronizzata
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then Return Nothing
            Dim agenda As CLSAgenda = CLSAgenda.GetInstance()
            If agenda Is Nothing Then
                avvisi.Add("L'agenda non è disponibile.")
                Return Nothing
            End If
            Dim unite As List(Of Dictionary(Of String, Object)) = agenda.UnisciSincronizzazioneFile(chiaveAgenda, vociRemote)
            If Not String.IsNullOrWhiteSpace(agenda.UltimoErroreSincronizzazioneFile) Then avvisi.Add(agenda.UltimoErroreSincronizzazioneFile)
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciAppuntamentiCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare l'agenda.")
            Return Nothing
        End Try
    End Function

    Friend Function UnisciRadioPreferiteCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            If Not WGSincronizzazioneCloud.RadioSincronizzata Then Return Nothing
            Dim unite As List(Of Dictionary(Of String, Object)) = RadioTvDatabase.GetInstance().UnisciPreferitiRadioFile(vociRemote)
            If unite Is Nothing Then avvisi.Add("Non è stato possibile sincronizzare le radio preferite.")
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciRadioPreferiteCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare le radio preferite.")
            Return Nothing
        End Try
    End Function

    Friend Function UnisciRadioRecentiCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            If Not WGSincronizzazioneCloud.RadioSincronizzata Then Return Nothing
            Dim unite As List(Of Dictionary(Of String, Object)) = RadioTvDatabase.GetInstance().UnisciRecentiRadioFile(vociRemote)
            If unite Is Nothing Then avvisi.Add("Non è stato possibile sincronizzare le radio ascoltate di recente.")
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciRadioRecentiCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare le radio ascoltate di recente.")
            Return Nothing
        End Try
    End Function

    Friend Function UnisciCategorieTestateCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            If Not WGSincronizzazioneCloud.NotizieSincronizzate Then Return Nothing
            Dim elenco As New ElencoQuotidiani()
            Dim unite As List(Of Dictionary(Of String, Object)) = elenco.UnisciCategorieTestateFile(vociRemote)
            If unite Is Nothing Then avvisi.Add("Non è stato possibile sincronizzare le categorie delle testate.")
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciCategorieTestateCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare le categorie delle testate.")
            Return Nothing
        End Try
    End Function

    Friend Function UnisciTestateCloud(vociRemote As List(Of Dictionary(Of String, Object)), avvisi As List(Of String)) As List(Of Dictionary(Of String, Object))
        Try
            If Not WGSincronizzazioneCloud.NotizieSincronizzate Then Return Nothing
            Dim elenco As New ElencoQuotidiani()
            Dim unite As List(Of Dictionary(Of String, Object)) = elenco.UnisciTestateFile(vociRemote)
            If unite Is Nothing Then avvisi.Add("Non è stato possibile sincronizzare le testate.")
            Return unite
        Catch ex As Exception
            ScriviLogAvvio("UnisciTestateCloud: " & ex.Message & Environment.NewLine & ex.StackTrace)
            avvisi.Add("Non è stato possibile sincronizzare le testate.")
            Return Nothing
        End Try
    End Function

End Module