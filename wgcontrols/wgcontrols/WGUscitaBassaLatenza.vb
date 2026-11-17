' ============================================================================================
' WGUscitaBassaLatenza - l'uscita audio con la latenza più bassa che Windows consente
' ============================================================================================
'
' Con le impostazioni normali Windows tiene un buffer di circa dieci millisecondi per ogni passaggio, e il suono arriva
' dopo trenta o quaranta millisecondi dalla pressione del tasto: si sente. Da Windows 10 la scheda audio può funzionare
' con un buffer molto più piccolo, anche meno di tre millisecondi, restando in modalità condivisa: gli altri suoni, compresa
' la voce di WinGabriel, continuano a sentirsi. Si chiede a Windows il periodo minimo (interfaccia IAudioClient3) e un
' thread con la priorità dei programmi audio professionali riempie il buffer a ogni periodo.
'
' Due cose in più tolgono ancora tempo fra il tasto e il suono:
'   - il buffer non si tiene pieno. Prima, a ogni giro, lo si riempiva fino all'orlo: la nota nuova si accodava dietro dieci o venti
'     millisecondi di audio già pronto. Ora se ne tiene in coda poco più di un periodo (più un margine che cresce da solo se il computer
'     è occupato e il buffer si svuota): la nota passa quasi subito, e il suono non scatta;
'   - si chiede a Windows il modo "grezzo", senza gli effetti della scheda audio (equalizzatori, potenziamenti), che aggiungono tempo.
'     Se la scheda non lo consente si riparte senza.
'
' Se la scheda non lo consente (Windows più vecchio, formato diverso da 48000 Hz stereo, driver che rifiuta), Avvia restituisce
' Nothing e il chiamante usa l'uscita normale.
' ============================================================================================
Imports System
Imports System.Runtime.InteropServices
Imports System.Threading
Imports NAudio.Wave

<ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IEnumeratoreDispositivi
    Function EnumAudioEndpoints(flusso As Integer, stato As Integer) As IntPtr
    Function GetDefaultAudioEndpoint(flusso As Integer, ruolo As Integer) As IDispositivoAudio
End Interface

<ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IDispositivoAudio
    Function Activate(<MarshalAs(UnmanagedType.LPStruct)> iid As Guid, contesto As Integer, parametri As IntPtr) As <MarshalAs(UnmanagedType.IUnknown)> Object
End Interface

<ComImport, Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IClienteAudio3
    Sub Initialize(modo As Integer, flags As Integer, durata As Long, periodicita As Long, formato As IntPtr, sessione As IntPtr)
    Function GetBufferSize() As UInteger
    Function GetStreamLatency() As Long
    Function GetCurrentPadding() As UInteger
    <PreserveSig>
    Function IsFormatSupported(modo As Integer, formato As IntPtr, ByRef vicino As IntPtr) As Integer
    Function GetMixFormat() As IntPtr
    Sub GetDevicePeriod(ByRef predefinito As Long, ByRef minimo As Long)
    Sub Start()
    Sub [Stop]()
    Sub Reset()
    Sub SetEventHandle(evento As IntPtr)
    Function GetService(<MarshalAs(UnmanagedType.LPStruct)> iid As Guid) As <MarshalAs(UnmanagedType.IUnknown)> Object
    Function IsOffloadCapable(categoria As Integer) As Integer
    Sub SetClientProperties(proprieta As IntPtr)
    Sub GetBufferSizeLimits(formato As IntPtr, eventi As Integer, ByRef minimo As Long, ByRef massimo As Long)
    Sub GetSharedModeEnginePeriod(formato As IntPtr, ByRef predefinito As UInteger, ByRef fondamentale As UInteger, ByRef minimo As UInteger, ByRef massimo As UInteger)
    Sub GetCurrentSharedModeEnginePeriod(ByRef formato As IntPtr, ByRef periodo As UInteger)
    Sub InitializeSharedAudioStream(flags As Integer, periodo As UInteger, formato As IntPtr, sessione As IntPtr)
End Interface

<ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
Friend Interface IRenderAudio
    Function GetBuffer(fotogrammi As UInteger) As IntPtr
    Sub ReleaseBuffer(fotogrammi As UInteger, flags As Integer)
End Interface

Public NotInheritable Class UscitaBassaLatenza
    Implements IDisposable

    Private Const FlagEventi As Integer = &H40000
    Private Const FlagSilenzio As Integer = 2

    <DllImport("avrt.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function AvSetMmThreadCharacteristicsW(nomeAttivita As String, ByRef indice As Integer) As IntPtr
    End Function

    <DllImport("avrt.dll", SetLastError:=True)>
    Private Shared Function AvRevertMmThreadCharacteristics(maniglia As IntPtr) As Boolean
    End Function

    Private Shared ReadOnly log As New Logger()

    ''' <summary>Perché l'ultima apertura non è riuscita (vuoto se è riuscita): serve a capire come mai si è usata l'uscita normale.</summary>
    Public Shared UltimoMotivo As String = ""

    Private _cliente As IClienteAudio3
    Private _render As IRenderAudio
    Private _formato As IntPtr = IntPtr.Zero
    Private _sorgente As IWaveProvider
    Private ReadOnly _evento As New AutoResetEvent(False)
    Private _thread As Thread
    Private _attivo As Boolean
    Private _dimensioneBuffer As UInteger
    Private _periodoFotogrammi As UInteger
    ''' <summary>I periodi che la scheda audio consente, in fotogrammi: serve per capire quanto si può scendere.</summary>
    Public Shared UltimiPeriodi As String = ""

    ''' <summary>Il flusso si è interrotto da solo (per esempio perché è cambiata la scheda audio predefinita).</summary>
    Public Event Interrotta As EventHandler
    Private _raw As Boolean
    Private _margine As UInteger = 96UI
    Private _cicli As Long
    Private _sommaInCoda As Long
    Private _massimoInCoda As UInteger
    Private _bufferVuoti As Long

    ''' <summary>Vero se Windows ha accettato il modo grezzo, senza gli effetti della scheda audio.</summary>
    Public ReadOnly Property Grezzo As Boolean
        Get
            Return _raw
        End Get
    End Property

    ''' <summary>Il margine di sicurezza del buffer, in fotogrammi: cresce da solo se il buffer si svuota.</summary>
    Public ReadOnly Property Margine As UInteger
        Get
            Return _margine
        End Get
    End Property

    ''' <summary>La latenza che Windows dichiara per il flusso, in millisecondi (il tempo che il suono passa dentro Windows).</summary>
    Public ReadOnly Property LatenzaFlussoMs As Double
        Get
            Try
                Return _cliente.GetStreamLatency() / 10000.0
            Catch
                Return 0.0
            End Try
        End Get
    End Property

    ''' <summary>Quanti fotogrammi entrano nel buffer di Windows.</summary>
    Public ReadOnly Property DimensioneBuffer As UInteger
        Get
            Return _dimensioneBuffer
        End Get
    End Property

    ''' <summary>Quanti fotogrammi in media erano già in coda, per Windows, ogni volta che si è riempito il buffer: quelli sono la latenza che si aggiunge al periodo.</summary>
    Public ReadOnly Property MediaInCoda As Double
        Get
            Dim c As Long = Interlocked.Read(_cicli)
            Return If(c = 0, 0.0, Interlocked.Read(_sommaInCoda) / CDbl(c))
        End Get
    End Property

    Public ReadOnly Property MassimoInCoda As UInteger
        Get
            Return _massimoInCoda
        End Get
    End Property

    ''' <summary>Quante volte il buffer era già vuoto quando si è tornati a riempirlo: ogni volta è uno scatto nel suono.</summary>
    Public ReadOnly Property BufferVuoti As Long
        Get
            Return Interlocked.Read(_bufferVuoti)
        End Get
    End Property

    Public ReadOnly Property Cicli As Long
        Get
            Return Interlocked.Read(_cicli)
        End Get
    End Property

    ''' <summary>Il periodo del buffer in fotogrammi (a 48000 Hz, 128 sono 2,7 millisecondi).</summary>
    Public ReadOnly Property PeriodoFotogrammi As UInteger
        Get
            Return _periodoFotogrammi
        End Get
    End Property

    ''' <summary>Apre l'uscita con il periodo minimo e comincia a riempirla dalla sorgente (float a 32 bit, stereo, 48000 Hz). Nothing se non è possibile.</summary>
    Public Shared Function Avvia(sorgente As IWaveProvider) As UscitaBassaLatenza
        For Each grezzo As Boolean In New Boolean() {True, False}
            Dim uscita As New UscitaBassaLatenza()
            UltimoMotivo = ""
            Try
                If uscita.Apri(sorgente, grezzo) Then Return uscita
            Catch ex As Exception
                UltimoMotivo = ex.GetType().Name & ": " & ex.Message
                log.ScriviLog("UscitaBassaLatenza.Avvia (" & If(grezzo, "grezzo", "normale") & "): " & ex.Message)
            End Try
            uscita.Dispose()
        Next
        Return Nothing
    End Function

    ''' <summary>Chiede a Windows di non applicare gli effetti della scheda audio a questo flusso (AUDCLNT_STREAMOPTIONS_RAW).</summary>
    Private Shared Sub ImpostaGrezzo(cliente As IClienteAudio3)
        Dim proprieta As IntPtr = Marshal.AllocCoTaskMem(16)
        Try
            Marshal.WriteInt32(proprieta, 0, 16)
            Marshal.WriteInt32(proprieta, 4, 0)
            Marshal.WriteInt32(proprieta, 8, 0)
            Marshal.WriteInt32(proprieta, 12, 1)
            cliente.SetClientProperties(proprieta)
        Finally
            Marshal.FreeCoTaskMem(proprieta)
        End Try
    End Sub

    Private Function Apri(sorgente As IWaveProvider, grezzo As Boolean) As Boolean
        If sorgente.WaveFormat.Encoding <> WaveFormatEncoding.IeeeFloat OrElse sorgente.WaveFormat.Channels <> 2 OrElse sorgente.WaveFormat.SampleRate <> 48000 Then
            UltimoMotivo = "formato della sorgente non previsto"
            Return False
        End If
        _sorgente = sorgente

        Dim enumeratore As IEnumeratoreDispositivi = DirectCast(Activator.CreateInstance(Type.GetTypeFromCLSID(New Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))), IEnumeratoreDispositivi)
        Dim dispositivo As IDispositivoAudio = enumeratore.GetDefaultAudioEndpoint(0, 0)
        Dim oggetto As Object = dispositivo.Activate(New Guid("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42"), 23, IntPtr.Zero)
        _cliente = DirectCast(oggetto, IClienteAudio3)

        _formato = _cliente.GetMixFormat()
        Dim etichetta As Integer = Marshal.ReadInt16(_formato, 0) And &HFFFF
        Dim canali As Integer = Marshal.ReadInt16(_formato, 2)
        Dim frequenza As Integer = Marshal.ReadInt32(_formato, 4)
        Dim bit As Integer = Marshal.ReadInt16(_formato, 14)
        Dim eFloat As Boolean = etichetta = 3
        If etichetta = &HFFFE AndAlso Marshal.ReadInt16(_formato, 16) >= 22 Then eFloat = Marshal.ReadInt32(_formato, 24) = 3
        If Not eFloat OrElse canali <> 2 OrElse frequenza <> 48000 OrElse bit <> 32 Then
            UltimoMotivo = "il formato di Windows non è float a 32 bit, stereo, 48000 Hz: " & etichetta.ToString() & "/" & canali.ToString() & "/" & frequenza.ToString() & "/" & bit.ToString()
            Return False
        End If

        Dim predefinito As UInteger = 0UI
        Dim fondamentale As UInteger = 0UI
        Dim minimo As UInteger = 0UI
        Dim massimo As UInteger = 0UI
        _cliente.GetSharedModeEnginePeriod(_formato, predefinito, fondamentale, minimo, massimo)
        UltimiPeriodi = "predefinito " & predefinito.ToString() & ", fondamentale " & fondamentale.ToString() & ", minimo " & minimo.ToString() & ", massimo " & massimo.ToString()
        If minimo = 0UI Then
            UltimoMotivo = "periodo minimo non disponibile"
            Return False
        End If
        ' il periodo più piccolo possibile fa scattare il suono appena il computer è un po' occupato: si usano circa cinque millisecondi,
        ' che per l'orecchio sono ancora istantanei, e il multiplo che la scheda audio consente
        Dim periodo As UInteger = Math.Max(minimo, 240UI)
        If fondamentale > 0UI Then periodo = ((periodo + fondamentale - 1UI) \ fondamentale) * fondamentale
        If massimo > 0UI Then periodo = Math.Min(periodo, massimo)
        If periodo < minimo Then periodo = minimo
        _periodoFotogrammi = periodo

        If grezzo Then ImpostaGrezzo(_cliente)
        _raw = grezzo
        _margine = Math.Min(96UI, periodo)
        _cliente.InitializeSharedAudioStream(FlagEventi, periodo, _formato, IntPtr.Zero)
        _cliente.SetEventHandle(_evento.SafeWaitHandle.DangerousGetHandle())
        _dimensioneBuffer = _cliente.GetBufferSize()
        _render = DirectCast(_cliente.GetService(New Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")), IRenderAudio)

        ' il buffer parte in silenzio, ma solo per un periodo e il margine: non per tutta la sua lunghezza
        Dim iniziale As UInteger = Math.Min(_dimensioneBuffer, periodo + _margine)
        _render.GetBuffer(iniziale)
        _render.ReleaseBuffer(iniziale, FlagSilenzio)

        _attivo = True
        _thread = New Thread(AddressOf Ciclo)
        _thread.IsBackground = True
        _thread.Name = "WGUscitaBassaLatenza"
        _thread.Priority = ThreadPriority.Highest
        _thread.Start()
        _cliente.Start()
        Return True
    End Function

    Private Sub Ciclo()
        Dim caratteristiche As IntPtr = IntPtr.Zero
        Try
            Dim indice As Integer = 0
            caratteristiche = AvSetMmThreadCharacteristicsW("Pro Audio", indice)
        Catch
        End Try
        Dim byteBuffer(0) As Byte
        Try
            While _attivo
                If Not _evento.WaitOne(250) Then Continue While
                If Not _attivo Then Exit While
                Dim usati As UInteger = _cliente.GetCurrentPadding()
                Interlocked.Increment(_cicli)
                Interlocked.Add(_sommaInCoda, CLng(usati))
                If usati > _massimoInCoda Then _massimoInCoda = usati
                If usati = 0UI Then
                    ' il buffer si è svuotato prima che arrivasse il giro: il margine era troppo stretto per questo computer
                    Interlocked.Increment(_bufferVuoti)
                    _margine = Math.Min(_periodoFotogrammi, _margine + 48UI)
                End If
                ' si tiene in coda un periodo più il margine, non di più: quello che è già in coda suona prima della nota nuova
                Dim obiettivo As UInteger = Math.Min(_dimensioneBuffer, _periodoFotogrammi + _margine)
                If usati >= obiettivo Then Continue While
                Dim liberi As UInteger = obiettivo - usati
                If liberi = 0UI Then Continue While
                Dim byteRichiesti As Integer = CInt(liberi) * 8
                If byteBuffer.Length < byteRichiesti Then ReDim byteBuffer(byteRichiesti - 1)
                Dim letti As Integer = 0
                Try
                    letti = _sorgente.Read(byteBuffer, 0, byteRichiesti)
                Catch ex As Exception
                    letti = 0
                End Try
                If letti < byteRichiesti Then Array.Clear(byteBuffer, letti, byteRichiesti - letti)
                Dim destinazione As IntPtr = _render.GetBuffer(liberi)
                Marshal.Copy(byteBuffer, 0, destinazione, byteRichiesti)
                _render.ReleaseBuffer(liberi, 0)
            End While
        Catch ex As Exception
            log.ScriviLog("UscitaBassaLatenza.Ciclo: " & ex.Message)
            ' la scheda audio è cambiata (cuffie collegate o scollegate, dispositivo predefinito diverso) e il flusso non vale più:
            ' chi ha aperto l'uscita lo avvisa e la riapre sul dispositivo nuovo; senza, il suono resterebbe muto fino al riavvio
            If _attivo Then
                Try
                    RaiseEvent Interrotta(Me, EventArgs.Empty)
                Catch
                End Try
            End If
        Finally
            Try
                If caratteristiche <> IntPtr.Zero Then AvRevertMmThreadCharacteristics(caratteristiche)
            Catch
            End Try
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        _attivo = False
        Try
            _evento.Set()
            If _thread IsNot Nothing AndAlso _thread IsNot Thread.CurrentThread Then _thread.Join(500)
        Catch
        End Try
        Try
            If _cliente IsNot Nothing Then
                Try
                    _cliente.Stop()
                Catch
                End Try
                Try
                    _cliente.Reset()
                Catch
                End Try
            End If
        Catch
        End Try
        Try
            If _render IsNot Nothing Then Marshal.ReleaseComObject(_render)
            If _cliente IsNot Nothing Then Marshal.ReleaseComObject(_cliente)
            If _formato <> IntPtr.Zero Then Marshal.FreeCoTaskMem(_formato)
        Catch
        End Try
        _render = Nothing
        _cliente = Nothing
        _formato = IntPtr.Zero
    End Sub

End Class
