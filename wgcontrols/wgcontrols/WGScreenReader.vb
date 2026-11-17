' ============================================================================================
' WGScreenReader - NVDA, JAWS, KSR e Assistente vocale mentre si usa WinGabriel
' ============================================================================================
'
' WinGabriel parla da solo: uno screen reader acceso insieme sovrappone le voci. Per le finestre
' esterne (impostazioni di Windows, altre applicazioni) lo screen reader serve, ma quando WinGabriel
' si apre o torna a schermo intero va sempre chiuso. WGContenitore chiama DopoAvvio quando la
' finestra compare e DopoRitornoDaAltraApplicazione quando torna attiva venendo da un'altra
' applicazione (WM_ACTIVATEAPP): si chiude solo se dopo un attimo WinGabriel è ancora davanti,
' così un passaggio di un istante (mentre si apre una pagina delle impostazioni) non conta.
'
'   WGScreenReader.Attivo()                          ' NVDA, JAWS, KSR o Assistente vocale in esecuzione
'   WGScreenReader.ChiudiAttivi()                    ' in sottofondo, una chiusura alla volta
'   WGScreenReader.ChiusuraAutomaticaSospesa = True  ' durante l'uscita da WinGabriel
' ============================================================================================
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class WGScreenReader

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly Processi As String() = {"nvda", "jfw", "KSR", "Narrator"}
    Private Shared _chiusuraInCorso As Integer
    Private Shared _verificaInCorso As Integer
    Private Shared _secondoPianoInCorso As Integer

    Public Shared Event PassatoInSecondoPiano As EventHandler

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processId As Integer) As Integer
    End Function

    Private Sub New()
    End Sub

    ''' <summary>Vero per non chiudere niente (per esempio mentre si esce attivando uno screen reader).</summary>
    Public Shared Property ChiusuraAutomaticaSospesa As Boolean

    Private Shared _avvioCompletato As Boolean

    ''' <summary>
    ''' Vero finché WinGabriel si sta avviando: dalla partenza del processo fino alla comparsa della finestra (al massimo due minuti).
    ''' In questo tempo nessuno screen reader viene riavviato da solo: l'ha appena chiuso l'avvio, e se rispuntasse si sentirebbero due voci.
    ''' </summary>
    Public Shared Function AvvioInCorso() As Boolean
        If _avvioCompletato Then Return False
        Try
            Using corrente As Process = Process.GetCurrentProcess()
                Return (DateTime.Now - corrente.StartTime).TotalSeconds < 120
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Vero se NVDA, JAWS, KSR o l'Assistente vocale sono in esecuzione.</summary>
    Public Shared Function Attivo() As Boolean
        Try
            For Each nome As String In Processi
                If InEsecuzione(nome) Then Return True
            Next
        Catch ex As Exception
            log.ScriviLog("WGScreenReader.Attivo: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function InEsecuzione(nome As String) As Boolean
        Dim trovati As Process() = Process.GetProcessesByName(nome)
        Try
            Return trovati.Length > 0
        Finally
            For Each p As Process In trovati
                p.Dispose()
            Next
        End Try
    End Function

    ''' <summary>WinGabriel si è appena aperto: parla la sua voce, gli screen reader si chiudono.</summary>
    Friend Shared Sub DopoAvvio()
        _avvioCompletato = True
        If ChiusuraAutomaticaSospesa Then Return
        ChiudiAttivi()
    End Sub

    ''' <summary>
    ''' WinGabriel torna davanti venendo da un'altra applicazione: dopo un secondo e mezzo, se è
    ''' ancora davanti, chiude gli screen reader rimasti accesi.
    ''' </summary>
    Friend Shared Sub DopoRitornoDaAltraApplicazione()
        If ChiusuraAutomaticaSospesa Then Return
        If Interlocked.Exchange(_verificaInCorso, 1) = 1 Then Return
        Task.Run(Async Function()
                     Try
                         Await Task.Delay(1500)
                         If Not ChiusuraAutomaticaSospesa AndAlso WinGabrielDavanti() Then ChiudiAttivi()
                     Catch ex As Exception
                         log.ScriviLog("WGScreenReader.DopoRitornoDaAltraApplicazione: " & ex.Message)
                     Finally
                         Interlocked.Exchange(_verificaInCorso, 0)
                     End Try
                 End Function)
    End Sub

    Friend Shared Sub DopoPassaggioInSecondoPiano()
        If ChiusuraAutomaticaSospesa Then Return
        If Interlocked.Exchange(_secondoPianoInCorso, 1) = 1 Then Return
        VerificaSecondoPiano()
    End Sub

    Private Shared Async Sub VerificaSecondoPiano()
        Try
            Await Task.Delay(800)
            If ChiusuraAutomaticaSospesa Then Return
            If WinGabrielDavanti() Then Return
            RaiseEvent PassatoInSecondoPiano(Nothing, EventArgs.Empty)
        Catch ex As Exception
            log.ScriviLog("WGScreenReader.VerificaSecondoPiano: " & ex.Message)
        Finally
            Interlocked.Exchange(_secondoPianoInCorso, 0)
        End Try
    End Sub

    Private Shared Function WinGabrielDavanti() As Boolean
        Try
            Dim finestra As IntPtr = GetForegroundWindow()
            If finestra = IntPtr.Zero Then Return False
            Dim processo As Integer = 0
            GetWindowThreadProcessId(finestra, processo)
            Using corrente As Process = Process.GetCurrentProcess()
                Return processo = corrente.Id
            End Using
        Catch ex As Exception
            log.ScriviLog("WGScreenReader.WinGabrielDavanti: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Chiude in sottofondo NVDA (con nvda -q, poi forzando), JAWS, KSR e l'Assistente vocale.</summary>
    Public Shared Sub ChiudiAttivi()
        If Interlocked.Exchange(_chiusuraInCorso, 1) = 1 Then Return
        Task.Run(Sub()
                     Try
                         If Not Attivo() Then Return
                         If InEsecuzione("nvda") Then ChiediUscitaNvda()
                         For Each nome As String In Processi
                             Termina(nome)
                         Next
                     Catch ex As Exception
                         log.ScriviLog("WGScreenReader.ChiudiAttivi: " & ex.Message)
                     Finally
                         Interlocked.Exchange(_chiusuraInCorso, 0)
                     End Try
                 End Sub)
    End Sub

    ' NVDA si chiude bene con "nvda.exe -q": si prova prima così, per tre secondi.
    Private Shared Sub ChiediUscitaNvda()
        Try
            Dim percorso As String = ""
            Dim trovati As Process() = Process.GetProcessesByName("nvda")
            For Each p As Process In trovati
                Try
                    If percorso = "" Then percorso = p.MainModule.FileName
                Catch
                End Try
                p.Dispose()
            Next
            If percorso = "" Then
                For Each cartella As String In {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}
                    Dim candidato As String = Path.Combine(cartella, "NVDA", "nvda.exe")
                    If File.Exists(candidato) Then
                        percorso = candidato
                        Exit For
                    End If
                Next
            End If
            If percorso = "" Then Return
            ' nvda.exe ha nel manifesto uiAccess="true": con CreateProcess (UseShellExecute = False) Windows risponde sempre "è necessaria
            ' l'esecuzione con privilegi elevati" (errore 740), anche se WinGabriel è amministratore, e NVDA veniva chiuso solo a forza.
            ' Con la shell (UseShellExecute = True) il programma parte e si chiude pulito con -q.
            Using uscita As Process = Process.Start(New ProcessStartInfo(percorso, "-q") With {.UseShellExecute = True, .WindowStyle = ProcessWindowStyle.Hidden})
                If uscita IsNot Nothing Then uscita.WaitForExit(3000)
            End Using
            Dim limite As DateTime = DateTime.Now.AddSeconds(3)
            While InEsecuzione("nvda") AndAlso DateTime.Now < limite
                Thread.Sleep(100)
            End While
        Catch ex As Exception
            log.ScriviLog("WGScreenReader.ChiediUscitaNvda: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Termina(nome As String)
        For Each p As Process In Process.GetProcessesByName(nome)
            Try
                p.Kill()
                p.WaitForExit(3000)
            Catch ex As Exception
                log.ScriviLog("WGScreenReader.Termina " & nome & ": " & ex.Message)
            Finally
                p.Dispose()
            End Try
        Next
    End Sub

End Class