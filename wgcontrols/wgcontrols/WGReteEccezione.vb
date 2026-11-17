Imports System
Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

' Gestione centralizzata e tracciamento su file delle eccezioni non gestite nei thread dell'interfaccia e dei task.
Public NotInheritable Class WGReteEccezioni

    Private Shared ReadOnly _lockScrittura As New Object()
    Private Shared ReadOnly _lockAttivazione As New Object()
    Private Shared _attivata As Integer = 0
    Private Shared _percorsoTraccia As String = ""
    Private Shared _logger As Logger = Nothing
    Private Shared _tracciaAttiva As Integer = 1

    Private Sub New()
    End Sub

    Public Shared Property TracciaAttiva As Boolean
        Get
            Return Interlocked.CompareExchange(_tracciaAttiva, 0, 0) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_tracciaAttiva, If(value, 1, 0))
        End Set
    End Property

    Public Shared ReadOnly Property PercorsoFileTraccia As String
        Get
            Try
                SyncLock _lockAttivazione
                    Return _percorsoTraccia
                End SyncLock
            Catch ex As exception
                Dim log As New logger()
                log.ScriviLog(ex.message & ex.stacktrace)
                Return ""
            End Try
        End Get
    End Property

    Public Shared Sub Attiva()

        If Interlocked.Exchange(_attivata, 1) = 1 Then Return

            SyncLock _lockAttivazione
                Try
                    _logger = New Logger()
                Catch ex As exception
                    Dim log As New logger()
                    log.ScriviLog(ex.message & ex.stacktrace)
                    _logger = Nothing
                End Try

                _percorsoTraccia = CalcolaPercorsoTraccia()
            End SyncLock

            Try
                AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf GestisciEccezioneNonGestita
            Catch ex As Exception
                Registra("Attiva - AppDomain.UnhandledException", ex)
            End Try

            Try
                AddHandler Application.ThreadException, AddressOf GestisciEccezioneThreadUI
            Catch ex As Exception
                Registra("Attiva - Application.ThreadException", ex)
            End Try

            Try
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
            Catch ex As Exception
                Registra("Attiva - SetUnhandledExceptionMode", ex)
            End Try

            Try
                AddHandler TaskScheduler.UnobservedTaskException, AddressOf GestisciEccezioneTask
            Catch ex As Exception
                Registra("Attiva - TaskScheduler.UnobservedTaskException", ex)
            End Try

            Try
                AddHandler AppDomain.CurrentDomain.FirstChanceException, AddressOf GestisciPrimaOccorrenza
            Catch ex As Exception
                Registra("Attiva - FirstChanceException", ex)
            End Try
                     End Sub

    Public Shared Sub Traccia(messaggio As String)
        Try
            If Interlocked.CompareExchange(_tracciaAttiva, 0, 0) <> 1 Then Return
            If String.IsNullOrEmpty(messaggio) Then Return

            Dim percorso As String = ""
            SyncLock _lockAttivazione
                percorso = _percorsoTraccia
            End SyncLock
            If String.IsNullOrEmpty(percorso) Then Return

            Dim riga As String = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture) &
                                 " [t" & Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture) & "] " &
                                 messaggio & vbCrLf

            SyncLock _lockScrittura
                Try
                    Using flusso As New FileStream(percorso, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 1024, FileOptions.WriteThrough)
                        Dim byteRiga As Byte() = Encoding.UTF8.GetBytes(riga)
                        flusso.Write(byteRiga, 0, byteRiga.Length)
                        flusso.Flush(True)
                    End Using
                Catch ex As exception
                    Dim log As New logger()
                    log.ScriviLog(ex.message & ex.stacktrace)
                End Try
            End SyncLock
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
        End Try
    End Sub

    Public Shared Sub TracciaEccezione(contesto As String, ex As Exception)
        Try
            If ex Is Nothing Then
                Traccia("ECCEZIONE " & contesto & ": nessun dettaglio")
                Return
            End If
            Traccia("ECCEZIONE " & contesto & ": " & DescriviEccezione(ex))
        Catch ex2 As exception
            Dim log As New logger()
            log.ScriviLog(ex2.Message & ex2.StackTrace)
        End Try
    End Sub

    Private Shared Sub GestisciEccezioneNonGestita(mittente As Object, argomenti As UnhandledExceptionEventArgs)
        Try
            Dim ex As Exception = TryCast(argomenti.ExceptionObject, Exception)
            Dim testo As String = "NON GESTITA (terminazione=" & argomenti.IsTerminating.ToString() & ") " &
                                  If(ex Is Nothing, "oggetto non convertibile in Exception", DescriviEccezione(ex))
            Traccia(testo)
            RegistraTesto(testo)
        Catch ex2 As exception
            Dim log As New logger()
            log.ScriviLog(ex2.Message & ex2.StackTrace)
        End Try
    End Sub

    Private Shared Sub GestisciEccezioneThreadUI(mittente As Object, argomenti As ThreadExceptionEventArgs)
        Try
            Dim testo As String = "THREAD UI " & DescriviEccezione(argomenti.Exception)
            Traccia(testo)
            RegistraTesto(testo)
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
        End Try
    End Sub

    Private Shared Sub GestisciEccezioneTask(mittente As Object, argomenti As UnobservedTaskExceptionEventArgs)
        Try
            Dim testo As String = "TASK NON OSSERVATO " & DescriviEccezione(argomenti.Exception)
            Traccia(testo)
            RegistraTesto(testo)
            Try
                argomenti.SetObserved()
            Catch ex As exception
                Dim log As New logger()
                log.ScriviLog(ex.message & ex.stacktrace)
            End Try
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
        End Try
    End Sub

    Private Shared Sub GestisciPrimaOccorrenza(mittente As Object, argomenti As Runtime.ExceptionServices.FirstChanceExceptionEventArgs)
        Try
            If Interlocked.CompareExchange(_tracciaAttiva, 0, 0) <> 1 Then Return
            Dim ex As Exception = argomenti.Exception
            If ex Is Nothing Then Return
            If TypeOf ex Is OperationCanceledException Then Return
            If TypeOf ex Is ThreadAbortException Then Return

            Dim traccia As String = ex.StackTrace
            If String.IsNullOrEmpty(traccia) Then traccia = "(stack trace non disponibile a questo punto)"

            traccia &= "PRIMA OCCORRENZA " & ex.GetType().FullName & " - " & ex.Message & vbCrLf & traccia
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
        End Try
    End Sub

    Private Shared Function DescriviEccezione(ex As Exception) As String
        Try
            If ex Is Nothing Then Return "nessun dettaglio"

            Dim costruttore As New StringBuilder()
            Dim corrente As Exception = ex
            Dim livello As Integer = 0

            Do While corrente IsNot Nothing AndAlso livello < 6
                If livello > 0 Then costruttore.Append(vbCrLf & "  ---> interna: ")
                costruttore.Append(corrente.GetType().FullName)
                costruttore.Append(" - ")
                costruttore.Append(corrente.Message)
                If Not String.IsNullOrEmpty(corrente.StackTrace) Then
                    costruttore.Append(vbCrLf)
                    costruttore.Append(corrente.StackTrace)
                End If
                corrente = corrente.InnerException
                livello += 1
            Loop

            Return costruttore.ToString()
        Catch ex2 As exception
            Dim log As New logger()
            log.ScriviLog(ex2.Message & ex2.StackTrace)
            Return "descrizione non disponibile"
        End Try
    End Function

    Private Shared Sub Registra(contesto As String, ex As Exception)
        Try
            RegistraTesto("[WGReteEccezioni] " & contesto & ": " & DescriviEccezione(ex))
        Catch ex2 As exception
            Dim log As New logger()
            log.ScriviLog(ex2.Message & ex2.StackTrace)
        End Try
    End Sub

    Private Shared Sub RegistraTesto(testo As String)
        Try
            Dim logLocale As Logger = Nothing
            SyncLock _lockAttivazione
                logLocale = _logger
            End SyncLock
            If logLocale Is Nothing Then Return
            logLocale.ScriviLog("[WGReteEccezioni] " & testo)
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
        End Try
    End Sub

    Private Shared Function VersioneApplicazione() As String
        Try
            Dim assemblea As Assembly = Assembly.GetEntryAssembly()
            If assemblea Is Nothing Then assemblea = Assembly.GetExecutingAssembly()
            If assemblea Is Nothing Then Return "non disponibile"
            Return assemblea.GetName().Version.ToString()
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
            Return "non disponibile"
        End Try
    End Function

    Private Shared Function CalcolaPercorsoTraccia() As String
        Try
            Dim cartellaBase As String = ""
            Try
                cartellaBase = Application.StartupPath
            Catch ex As exception
                Dim log As New logger()
                log.ScriviLog(ex.message & ex.stacktrace)
                cartellaBase = ""
            End Try

            If String.IsNullOrEmpty(cartellaBase) Then
                Try
                    cartellaBase = AppDomain.CurrentDomain.BaseDirectory
                Catch ex As exception
                    Dim log As New logger()
                    log.ScriviLog(ex.message & ex.stacktrace)
                    cartellaBase = ""
                End Try
            End If

            If String.IsNullOrEmpty(cartellaBase) Then Return ""

            ' la traccia degli arresti sta con i log
            Dim cartellaLog As String = cartellaBase
            Try
                cartellaLog = WGDb.WGPercorsiApplicazione.CartellaLog()
            Catch ex As exception
                Dim log As New logger()
                log.ScriviLog(ex.message & ex.stacktrace)
            End Try

            Return Path.Combine(cartellaLog, "traccia-crash.log")
        Catch ex As exception
            Dim log As New logger()
            log.ScriviLog(ex.message & ex.stacktrace)
            Return ""
        End Try
    End Function

End Class