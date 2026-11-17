Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports UIAutomationClient

' Iniezione e comunicazione con librerie di supporto nei processi esterni per estrarre informazioni sulla struttura delle
' finestre.
Friend NotInheritable Class AiutoInProcesso

    Private Const VersioneProtocollo As String = "1"
    Private Const PrefissoTubo As String = "WGAiutoLettore_v1_"
    Private Const NomeProcedura As String = "ProceduraMessaggi"
    Private Const AgganciaMessaggi As Integer = 3
    Private Const OggettoCliente As Integer = -4
    Private Const AccessoSincronizza As UInteger = &H100000UI
    Private Const AccessoInformazioniLimitate As UInteger = &H1000UI
    Private Const AttesaConnessioneMs As Integer = 1000
    Private Const RifiutiDefinitivi As Integer = 2
    Private Const MassimaRisposta As Integer = 64 * 1024 * 1024
    Private Const AttesaRiprovaMs As Long = 60000L

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryW(percorso As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Ansi, SetLastError:=True, ExactSpelling:=True)>
    Private Shared Function GetProcAddress(modulo As IntPtr, nome As String) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowsHookExW(idAggancio As Integer, procedura As IntPtr, modulo As IntPtr, thread As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(aggancio As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(finestra As IntPtr, ByRef pid As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function PostThreadMessageW(thread As UInteger, messaggio As UInteger, primo As IntPtr, secondo As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindow(finestra As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function OpenProcess(accesso As UInteger, eredita As Boolean, pid As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function IsWow64Process(processo As IntPtr, <MarshalAs(UnmanagedType.Bool)> ByRef wow As Boolean) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForSingleObject(oggetto As IntPtr, attesa As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(oggetto As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("oleacc.dll")>
    Private Shared Function AccessibleObjectFromEvent(finestra As IntPtr, oggetto As Integer, figlio As Integer, <MarshalAs(UnmanagedType.IUnknown)> ByRef accessibile As Object, ByRef figlioRisultato As Object) As Integer
    End Function

    Private NotInheritable Class Aggancio
        Public Thread As UInteger
        Public Pid As UInteger
        Public Finestra As IntPtr
        Public Maniglia As IntPtr
        Public Caricatore As Process
    End Class

    Private NotInheritable Class RichiestaAggancio
        Public Finestra As IntPtr
        Public Esito As Boolean
        Public ReadOnly Fatto As New ManualResetEventSlim(False)
    End Class

    Private Shared ReadOnly _istanza As New AiutoInProcesso()
    Private Shared _abilitato As Integer = 1
    Private Shared ReadOnly _pidOspite As UInteger = CUInt(Process.GetCurrentProcess().Id)

    Private ReadOnly log As New Logger()
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _bloccoTubo As New Object()
    Private ReadOnly _agganci As New Dictionary(Of UInteger, Aggancio)()
    Private ReadOnly _rifiutati As New Dictionary(Of UInteger, Integer())()
    Private ReadOnly _occupati As New Dictionary(Of UInteger, Integer)()
    Private Const AttesaOccupatoMs As Long = 4000L
    Private ReadOnly _verificati As New Dictionary(Of UInteger, Boolean)()
    Private _richieste As BlockingCollection(Of RichiestaAggancio)
    Private _threadAgganci As Thread
    Private _cartella As String = ""
    Private _cartellaPronta As Boolean
    Private _modulo As IntPtr
    Private _procedura As IntPtr
    Private _moduloProvato As Boolean
    Private _fermato As Integer

    Private Sub New()
    End Sub

    Public Shared ReadOnly Property Istanza As AiutoInProcesso
        Get
            Return _istanza
        End Get
    End Property

    Public Shared Property Abilitato As Boolean
        Get
            Return Volatile.Read(_abilitato) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_abilitato, If(value, 1, 0))
        End Set
    End Property

    Private Shared Function TrascorsoDa(tick As Integer) As Long
        Dim trascorso As Long = CLng(Environment.TickCount) - CLng(tick)
        If trascorso < 0L Then trascorso += 4294967296L
        Return trascorso
    End Function

    ' Le librerie si usano dove stanno: nessuna copia in una cartella di appoggio.
    Private Function CartellaModuli() As String
        Try
            SyncLock _blocco
                If _cartellaPronta Then Return _cartella
                _cartellaPronta = True
                Dim origine As String = Path.GetDirectoryName(GetType(AiutoInProcesso).Assembly.Location)
                Dim nomi As String() = {"WGAiutoLettore32.dll", "WGAiutoLettore64.dll", "WGAiutoLettoreCarica32.exe", "WGAiutoLettoreCarica64.exe"}
                Dim presenti As Integer = 0
                For Each nome As String In nomi
                    If File.Exists(Path.Combine(origine, nome)) Then presenti += 1
                Next
                _cartella = If(presenti = 0, "", origine)
                Return _cartella
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.CartellaModuli: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ProcessoA32Bit(pid As UInteger, ByRef noto As Boolean) As Boolean
        noto = False
        Dim processo As IntPtr = IntPtr.Zero
        Try
            If Not Environment.Is64BitOperatingSystem Then
                noto = True
                Return True
            End If
            processo = OpenProcess(AccessoInformazioniLimitate, False, pid)
            If processo = IntPtr.Zero Then Return False
            Dim wow As Boolean = False
            If Not IsWow64Process(processo, wow) Then Return False
            noto = True
            Return wow
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.ProcessoA32Bit: " & ex.Message)
            Return False
        Finally
            If processo <> IntPtr.Zero Then CloseHandle(processo)
        End Try
    End Function

    Private Function ProcessoVivo(pid As UInteger) As Boolean
        Dim processo As IntPtr = IntPtr.Zero
        Try
            processo = OpenProcess(AccessoSincronizza, False, pid)
            If processo = IntPtr.Zero Then Return False
            Return WaitForSingleObject(processo, 0UI) <> 0UI
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.ProcessoVivo: " & ex.Message)
            Return False
        Finally
            If processo <> IntPtr.Zero Then CloseHandle(processo)
        End Try
    End Function

    Private Sub AvviaThreadAgganci()
        Try
            SyncLock _blocco
                If _threadAgganci IsNot Nothing OrElse Volatile.Read(_fermato) = 1 Then Return
                _richieste = New BlockingCollection(Of RichiestaAggancio)()
                _threadAgganci = New Thread(AddressOf CicloAgganci)
                _threadAgganci.IsBackground = True
                _threadAgganci.Name = "WGLettoreSchermoAiuto"
                _threadAgganci.SetApartmentState(ApartmentState.MTA)
                _threadAgganci.Start()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.AvviaThreadAgganci: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloAgganci()
        Try
            Dim coda As BlockingCollection(Of RichiestaAggancio) = _richieste
            For Each richiesta As RichiestaAggancio In coda.GetConsumingEnumerable()
                Try
                    richiesta.Esito = EseguiAggancio(richiesta.Finestra)
                Catch exRichiesta As Exception
                    log.ScriviLog("AiutoInProcesso.CicloAgganci (richiesta): " & exRichiesta.Message)
                    richiesta.Esito = False
                Finally
                    richiesta.Fatto.Set()
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.CicloAgganci: " & ex.Message)
        End Try
        TogliTutti()
    End Sub

    Private Function CaricaModuloLocale() As Boolean
        Try
            If _moduloProvato Then Return _procedura <> IntPtr.Zero
            _moduloProvato = True
            Dim cartella As String = CartellaModuli()
            If cartella = "" Then Return False
            Dim nome As String = If(Environment.Is64BitProcess, "WGAiutoLettore64.dll", "WGAiutoLettore32.dll")
            Dim percorso As String = Path.Combine(cartella, nome)
            If Not File.Exists(percorso) Then Return False
            _modulo = LoadLibraryW(percorso)
            If _modulo = IntPtr.Zero Then
                log.ScriviLog("AiutoInProcesso.CaricaModuloLocale: LoadLibrary non riuscito, codice " & Marshal.GetLastWin32Error().ToString())
                Return False
            End If
            _procedura = GetProcAddress(_modulo, NomeProcedura)
            If _procedura = IntPtr.Zero Then
                log.ScriviLog("AiutoInProcesso.CaricaModuloLocale: procedura non trovata, codice " & Marshal.GetLastWin32Error().ToString())
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.CaricaModuloLocale: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EseguiAggancio(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero OrElse Not IsWindow(finestra) Then Return False
            Dim pid As UInteger = 0UI
            Dim thread As UInteger = GetWindowThreadProcessId(finestra, pid)
            If thread = 0UI OrElse pid = 0UI Then Return False
            PulisciMorti()
            Dim esistente As Aggancio = Nothing
            If _agganci.TryGetValue(thread, esistente) Then Return True
            Dim noto As Boolean = False
            Dim bersaglio32 As Boolean = ProcessoA32Bit(pid, noto)
            If Not noto Then Return False
            Dim ospite32 As Boolean = Not Environment.Is64BitProcess
            Dim nuovo As New Aggancio()
            nuovo.Thread = thread
            nuovo.Pid = pid
            nuovo.Finestra = finestra
            If bersaglio32 = ospite32 Then
                If Not CaricaModuloLocale() Then Return False
                nuovo.Maniglia = SetWindowsHookExW(AgganciaMessaggi, _procedura, _modulo, thread)
                If nuovo.Maniglia = IntPtr.Zero Then
                    log.ScriviLog("AiutoInProcesso.EseguiAggancio: SetWindowsHookEx non riuscito, codice " & Marshal.GetLastWin32Error().ToString())
                    Return False
                End If
            Else
                Dim cartella As String = CartellaModuli()
                If cartella = "" Then Return False
                Dim caricatore As String = Path.Combine(cartella, If(bersaglio32, "WGAiutoLettoreCarica32.exe", "WGAiutoLettoreCarica64.exe"))
                Dim modulo As String = Path.Combine(cartella, If(bersaglio32, "WGAiutoLettore32.dll", "WGAiutoLettore64.dll"))
                If Not File.Exists(caricatore) OrElse Not File.Exists(modulo) Then Return False
                Dim avvio As New ProcessStartInfo(caricatore)
                avvio.Arguments = thread.ToString() & " " & _pidOspite.ToString() & " " & pid.ToString() & " """ & modulo & """"
                avvio.UseShellExecute = False
                avvio.CreateNoWindow = True
                avvio.WindowStyle = ProcessWindowStyle.Hidden
                nuovo.Caricatore = Process.Start(avvio)
                If nuovo.Caricatore Is Nothing Then Return False
                If nuovo.Caricatore.WaitForExit(300) Then
                    log.ScriviLog("AiutoInProcesso.EseguiAggancio: il caricatore è uscito con codice " & nuovo.Caricatore.ExitCode.ToString())
                    nuovo.Caricatore.Dispose()
                    Return False
                End If
            End If
            _agganci(thread) = nuovo
            PostThreadMessageW(thread, 0UI, IntPtr.Zero, IntPtr.Zero)
            Return True
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.EseguiAggancio: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub PulisciMorti()
        Try
            Dim morti As New List(Of UInteger)()
            For Each coppia As KeyValuePair(Of UInteger, Aggancio) In _agganci
                Dim voce As Aggancio = coppia.Value
                Dim vivo As Boolean = ProcessoVivo(voce.Pid)
                If vivo AndAlso voce.Caricatore IsNot Nothing Then
                    Try
                        vivo = Not voce.Caricatore.HasExited
                    Catch exCaricatore As Exception
                        log.ScriviLog("AiutoInProcesso.PulisciMorti (caricatore): " & exCaricatore.Message)
                        vivo = False
                    End Try
                End If
                If Not vivo Then morti.Add(coppia.Key)
            Next
            For Each thread As UInteger In morti
                TogliAggancio(_agganci(thread))
                _agganci.Remove(thread)
            Next
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.PulisciMorti: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliAggancio(voce As Aggancio)
        Try
            If voce Is Nothing Then Return
            If voce.Maniglia <> IntPtr.Zero Then UnhookWindowsHookEx(voce.Maniglia)
            voce.Maniglia = IntPtr.Zero
            If voce.Caricatore IsNot Nothing Then
                Try
                    If Not voce.Caricatore.HasExited Then voce.Caricatore.Kill()
                Catch exChiusura As Exception
                    log.ScriviLog("AiutoInProcesso.TogliAggancio (caricatore): " & exChiusura.Message)
                End Try
                voce.Caricatore.Dispose()
                voce.Caricatore = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.TogliAggancio: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliTutti()
        Try
            For Each voce As Aggancio In _agganci.Values
                TogliAggancio(voce)
            Next
            _agganci.Clear()
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.TogliTutti: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            Interlocked.Exchange(_fermato, 1)
            Dim coda As BlockingCollection(Of RichiestaAggancio) = _richieste
            If coda IsNot Nothing AndAlso Not coda.IsAddingCompleted Then coda.CompleteAdding()
            Dim lavoro As Thread = _threadAgganci
            If lavoro IsNot Nothing Then lavoro.Join(2000)
            SyncLock _blocco
                _threadAgganci = Nothing
                _richieste = Nothing
                _verificati.Clear()
            End SyncLock
            Interlocked.Exchange(_fermato, 0)
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Function Aggancia(finestra As IntPtr) As Boolean
        Try
            AvviaThreadAgganci()
            Dim coda As BlockingCollection(Of RichiestaAggancio) = _richieste
            If coda Is Nothing OrElse coda.IsAddingCompleted Then Return False
            Dim richiesta As New RichiestaAggancio()
            richiesta.Finestra = finestra
            If Not coda.TryAdd(richiesta) Then Return False
            If Not richiesta.Fatto.Wait(3000) Then Return False
            Return richiesta.Esito
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.Aggancia: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function RifiutatoDiRecente(pid As UInteger) As Boolean
        Try
            SyncLock _blocco
                Dim stato As Integer() = Nothing
                Dim tickOccupato As Integer = 0
                If _occupati.TryGetValue(pid, tickOccupato) AndAlso TrascorsoDa(tickOccupato) < AttesaOccupatoMs Then Return True
                If Not _rifiutati.TryGetValue(pid, stato) Then Return False
                If stato(1) >= RifiutiDefinitivi Then Return True
                Return TrascorsoDa(stato(0)) < AttesaRiprovaMs
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.RifiutatoDiRecente: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SegnaRifiutato(pid As UInteger)
        Try
            SyncLock _blocco
                If _rifiutati.Count > 100 Then _rifiutati.Clear()
                Dim stato As Integer() = Nothing
                If _rifiutati.TryGetValue(pid, stato) Then
                    stato(0) = Environment.TickCount
                    stato(1) += 1
                Else
                    _rifiutati(pid) = New Integer() {Environment.TickCount, 1}
                End If
                _verificati.Remove(pid)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.SegnaRifiutato: " & ex.Message)
        End Try
    End Sub

    Private Sub SegnaOccupato(pid As UInteger)
        Try
            SyncLock _blocco
                If _occupati.Count > 100 Then _occupati.Clear()
                _occupati(pid) = Environment.TickCount
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.SegnaOccupato: " & ex.Message)
        End Try
    End Sub

    Private Function Verificato(pid As UInteger) As Boolean
        Try
            SyncLock _blocco
                Dim valore As Boolean = False
                Return _verificati.TryGetValue(pid, valore) AndAlso valore
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.Verificato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub SegnaVerificato(pid As UInteger)
        Try
            SyncLock _blocco
                If _verificati.Count > 100 Then _verificati.Clear()
                _verificati(pid) = True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.SegnaVerificato: " & ex.Message)
        End Try
    End Sub

    Public Function Pronto(finestra As IntPtr) As Boolean
        Try
            If Not Abilitato OrElse finestra = IntPtr.Zero Then Return False
            Dim pid As UInteger = 0UI
            GetWindowThreadProcessId(finestra, pid)
            If pid = 0UI OrElse RifiutatoDiRecente(pid) Then Return False
            If pid = _pidOspite Then Return False
            If Not Aggancia(finestra) Then
                SegnaRifiutato(pid)
                Return False
            End If
            If Verificato(pid) Then Return True
            Dim risposta As String = Nothing
            For tentativo As Integer = 1 To 2
                risposta = Invia(pid, "PING", 1500)
                If risposta IsNot Nothing Then Exit For
                Thread.Sleep(150)
            Next
            If risposta Is Nothing OrElse Not risposta.StartsWith("OK" & ControlChars.Tab & VersioneProtocollo & ControlChars.Tab, StringComparison.Ordinal) Then
                SegnaRifiutato(pid)
                Return False
            End If
            SegnaVerificato(pid)
            Return True
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.Pronto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function RichiediAlbero(finestra As IntPtr, idDocumento As Integer, massimoNodi As Integer, tempoMs As Integer) As String
        Try
            If Not Pronto(finestra) Then Return Nothing
            Dim pid As UInteger = 0UI
            GetWindowThreadProcessId(finestra, pid)
            Dim richiesta As String = "ALBERO" & ControlChars.Tab & finestra.ToInt64().ToString() & ControlChars.Tab & idDocumento.ToString() & ControlChars.Tab & massimoNodi.ToString() & ControlChars.Tab & tempoMs.ToString()
            Dim risposta As String = Invia(pid, richiesta, tempoMs + 6000)
            If risposta Is Nothing Then
                ' Il browser è occupato (di solito sta caricando una pagina): non è un rifiuto, si riprova fra poco.
                SegnaOccupato(pid)
                Return Nothing
            End If
            If Not risposta.StartsWith("OK" & ControlChars.Tab, StringComparison.Ordinal) Then Return Nothing
            Return risposta
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.RichiediAlbero: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function Invia(pid As UInteger, testo As String, attesaMs As Integer) As String
        Try
            SyncLock _bloccoTubo
                Using tubo As New NamedPipeClientStream(".", PrefissoTubo & pid.ToString(), PipeDirection.InOut, PipeOptions.Asynchronous)
                    tubo.Connect(AttesaConnessioneMs)
                    Dim dati As Byte() = Encoding.Unicode.GetBytes(testo)
                    Dim intestazione As Byte() = BitConverter.GetBytes(dati.Length)
                    tubo.Write(intestazione, 0, intestazione.Length)
                    tubo.Write(dati, 0, dati.Length)
                    tubo.Flush()
                    Dim scadenza As Integer = Environment.TickCount
                    Dim lunghezza(3) As Byte
                    If Not LeggiEsatti(tubo, lunghezza, 4, scadenza, attesaMs) Then Return Nothing
                    Dim dimensione As Integer = BitConverter.ToInt32(lunghezza, 0)
                    If dimensione < 0 OrElse dimensione > MassimaRisposta OrElse (dimensione Mod 2) <> 0 Then Return Nothing
                    If dimensione = 0 Then Return ""
                    Dim corpo(dimensione - 1) As Byte
                    If Not LeggiEsatti(tubo, corpo, dimensione, scadenza, attesaMs) Then Return Nothing
                    Return Encoding.Unicode.GetString(corpo)
                End Using
            End SyncLock
        Catch exTempo As TimeoutException
            Return Nothing
        Catch exIo As IOException
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.Invia: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function LeggiEsatti(tubo As NamedPipeClientStream, destinazione As Byte(), quanti As Integer, inizio As Integer, attesaMs As Integer) As Boolean
        Try
            Dim letti As Integer = 0
            While letti < quanti
                Dim restante As Long = CLng(attesaMs) - TrascorsoDa(inizio)
                If restante <= 0L Then Return False
                Dim compito As Task(Of Integer) = tubo.ReadAsync(destinazione, letti, quanti - letti)
                If Not compito.Wait(CInt(restante)) Then Return False
                Dim questa As Integer = compito.Result
                If questa <= 0 Then Return False
                letti += questa
            End While
            Return True
        Catch exAggregato As AggregateException
            Return False
        Catch ex As Exception
            log.ScriviLog("AiutoInProcesso.LeggiEsatti: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ElementoDaId(automazione As IUIAutomation, cache As IUIAutomationCacheRequest, finestra As IntPtr, id As Integer) As IUIAutomationElement
        Try
            If automazione Is Nothing OrElse finestra = IntPtr.Zero OrElse id = 0 Then Return Nothing
            Dim accessibile As Object = Nothing
            Dim figlio As Object = Nothing
            If AccessibleObjectFromEvent(finestra, OggettoCliente, id, accessibile, figlio) < 0 OrElse accessibile Is Nothing Then Return Nothing
            Dim idFiglio As Integer = 0
            If TypeOf figlio Is Integer Then idFiglio = CInt(figlio)
            If cache IsNot Nothing Then Return automazione.ElementFromIAccessibleBuildCache(accessibile, idFiglio, cache)
            Return automazione.ElementFromIAccessible(accessibile, idFiglio)
        Catch ex As Exception
            RegistroErrori.Scrivi("AiutoInProcesso.ElementoDaId", ex)
            Return Nothing
        End Try
    End Function

End Class
