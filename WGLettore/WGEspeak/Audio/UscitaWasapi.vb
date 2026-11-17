Imports System.Runtime.InteropServices
Imports System.Threading

' Gestione dell'uscita audio tramite le API WASAPI per la riproduzione vocale, con controllo della latenza e dei buffer hardware.
Friend NotInheritable Class UscitaWasapi
    Inherits MotoreUscita

    Private ReadOnly _bassaLatenza As Boolean
    Private _enumeratore As IMMDeviceEnumerator
    Private _dispositivo As IMMDevice
    Private _client As IAudioClient
    Private _render As IAudioRenderClient
    Private _notifica As NotificaDispositivi
    Private _formato As FormatoAudio
    Private _evento As IntPtr = IntPtr.Zero
    Private _thread As Thread
    Private _fermare As Integer
    Private _valido As Integer
    Private _chiuso As Boolean
    Private _dimensioneBuffer As Integer
    Private _periodoFrame As Integer
    Private _periodoMs As Double
    Private _paddingUltimo As Integer
    Private _tagliaSubito As Integer
    Private _dopoSilenzio As Boolean = True

    Private _mono As Single()
    Private _uscitaVirgola As Single()
    Private _uscitaIntera16 As Short()
    Private _uscitaIntera32 As Integer()
    Private _uscitaIntera24 As Byte()

    Public Sub New(bassaLatenza As Boolean)
        _bassaLatenza = bassaLatenza
    End Sub

    Friend Overrides ReadOnly Property Nome As String
        Get
            Return If(_bassaLatenza, "WASAPI a bassa latenza", "WASAPI")
        End Get
    End Property

    Friend Overrides ReadOnly Property LatenzaMs As Double
        Get
            Return _periodoMs
        End Get
    End Property

    Friend Overrides ReadOnly Property Valido As Boolean
        Get
            Return Volatile.Read(_valido) = 1 AndAlso Volatile.Read(_fermare) = 0
        End Get
    End Property

    Friend Overrides ReadOnly Property MillisecondiInHardware As Integer
        Get
            Dim inUso As Integer = Volatile.Read(_paddingUltimo)
            If inUso <= 0 Then Return 0
            Return CInt(CLng(inUso) * 1000L \ Math.Max(1, FrequenzaUscita))
        End Get
    End Property

    Private Function Fallito(passo As String, hr As Integer) As Boolean
        Diagnosi = Nome & ": " & passo & " (0x" & hr.ToString("X8") & ")."
        Return False
    End Function

    Friend Overrides Function Apri(frequenzaIngresso As Integer) As Boolean
        Dim puntatoreFormato As IntPtr = IntPtr.Zero
        Try
            _enumeratore = CreaEnumeratoreDispositivi()
            Dim hr As Integer = _enumeratore.GetDefaultAudioEndpoint(DispositivoDiUscita, RuoloConsole, _dispositivo)
            If hr < 0 OrElse _dispositivo Is Nothing Then Return Fallito("nessuna uscita audio", hr)

            Dim idDispositivo As String = ""
            _dispositivo.GetId(idDispositivo)

            Dim interfaccia As Object = Nothing
            Dim iid As Guid = If(_bassaLatenza, IID_IAudioClient3, IID_IAudioClient)
            hr = _dispositivo.Activate(iid, CLSCTX_ALL, IntPtr.Zero, interfaccia)
            If hr < 0 OrElse interfaccia Is Nothing Then Return Fallito("il client audio non si apre", hr)
            _client = CType(interfaccia, IAudioClient)

            hr = _client.GetMixFormat(puntatoreFormato)
            If hr < 0 OrElse puntatoreFormato = IntPtr.Zero Then Return Fallito("il formato della scheda non si legge", hr)
            _formato = FormatoAudio.Leggi(puntatoreFormato)
            If _formato.Tipo = TipoCampione.NonSupportato OrElse _formato.Canali < 1 OrElse _formato.Frequenza < 8000 Then
                Diagnosi = Nome & ": il formato della scheda non è uno di quelli gestiti."
                Return False
            End If

            Dim sessione As Guid = Guid.Empty
            If _bassaLatenza Then
                Dim client3 As IAudioClient3 = TryCast(interfaccia, IAudioClient3)
                If client3 Is Nothing Then Return Fallito("IAudioClient3 non c'è", 0)
                Dim predefinito As UInteger, fondamentale As UInteger, minimo As UInteger, massimo As UInteger
                hr = client3.GetSharedModeEnginePeriod(puntatoreFormato, predefinito, fondamentale, minimo, massimo)
                If hr < 0 Then Return Fallito("il periodo del motore audio non si legge", hr)
                hr = client3.InitializeSharedAudioStream(AUDCLNT_STREAMFLAGS_EVENTCALLBACK, minimo, puntatoreFormato, sessione)
                If hr < 0 Then Return Fallito("la bassa latenza non parte", hr)
                _periodoMs = minimo * 1000.0 / _formato.Frequenza
                _periodoFrame = CInt(minimo)
                Diagnosi = Nome & ": periodo del motore audio " & minimo.ToString() & " frame (" & _periodoMs.ToString("0.0") & " ms), il più corto consentito dalla scheda; normale " & predefinito.ToString() & ", massimo " & massimo.ToString() & "."
            Else
                Dim periodoPredefinito As Long, periodoMinimo As Long
                hr = _client.GetDevicePeriod(periodoPredefinito, periodoMinimo)
                If hr < 0 Then Return Fallito("il periodo della scheda non si legge", hr)
                Dim periodoUsato As Long = periodoMinimo
                hr = _client.Initialize(0, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, periodoMinimo, 0, puntatoreFormato, sessione)
                If hr < 0 Then
                    periodoUsato = periodoPredefinito
                    hr = _client.Initialize(0, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, periodoPredefinito, 0, puntatoreFormato, sessione)
                End If
                If hr < 0 Then Return Fallito("l'uscita non parte", hr)
                _periodoMs = periodoUsato / 10000.0
                _periodoFrame = Math.Max(1, CInt(periodoUsato * _formato.Frequenza / 10000000.0))
            End If

            _evento = CreateEventW(IntPtr.Zero, False, False, Nothing)
            If _evento = IntPtr.Zero Then Return Fallito("l'evento non si crea", Marshal.GetLastWin32Error())
            hr = _client.SetEventHandle(_evento)
            If hr < 0 Then Return Fallito("l'evento non si collega", hr)

            Dim dimensione As UInteger
            hr = _client.GetBufferSize(dimensione)
            If hr < 0 OrElse dimensione = 0UI Then Return Fallito("la dimensione del buffer non si legge", hr)
            _dimensioneBuffer = CInt(dimensione)

            Dim servizio As Object = Nothing
            hr = _client.GetService(IID_IAudioRenderClient, servizio)
            If hr < 0 OrElse servizio Is Nothing Then Return Fallito("il client di scrittura non si apre", hr)
            _render = CType(servizio, IAudioRenderClient)

            FrequenzaUscita = _formato.Frequenza
            Coda = New CodaCampioni(_formato.Frequenza * 20)
            Dim capacita As Integer = _dimensioneBuffer * _formato.Canali
            _mono = New Single(_dimensioneBuffer - 1) {}
            Select Case _formato.Tipo
                Case TipoCampione.Virgola32 : _uscitaVirgola = New Single(capacita - 1) {}
                Case TipoCampione.Intero16 : _uscitaIntera16 = New Short(capacita - 1) {}
                Case TipoCampione.Intero24 : _uscitaIntera24 = New Byte(capacita * 3 - 1) {}
                Case TipoCampione.Intero32 : _uscitaIntera32 = New Integer(capacita - 1) {}
            End Select

            Volatile.Write(_paddingUltimo, 0)
            hr = _client.Start()
            If hr < 0 Then Return Fallito("l'uscita non si avvia", hr)
            _valido = 1
            _thread = New Thread(AddressOf Ciclo) With {.IsBackground = True, .Name = "WGUscitaAudio WASAPI", .Priority = ThreadPriority.Highest}
            _thread.Start()

            Try
                _notifica = New NotificaDispositivi(idDispositivo, AddressOf Invalida)
                _enumeratore.RegisterEndpointNotificationCallback(_notifica)
            Catch exNotifica As Exception
                _notifica = Nothing
                Diagnosi = Nome & ": gli avvisi della scheda non si registrano, " & exNotifica.Message & ". "
            End Try
            Return True
        Catch ex As Exception
            Diagnosi = Nome & ": " & ex.Message & ". "
            Return False
        Finally
            Try
                If puntatoreFormato <> IntPtr.Zero Then Marshal.FreeCoTaskMem(puntatoreFormato)
            Catch exFormato As Exception
                Diagnosi = Nome & ": il formato non si libera, " & exFormato.Message & ". "
            End Try
        End Try
    End Function

    Private Sub Invalida()
        Volatile.Write(_valido, 0)
    End Sub

    Friend Overrides Sub SvuotaHardware()
        Try
            If Volatile.Read(_paddingUltimo) > 0 Then Interlocked.Exchange(_tagliaSubito, 1)
            If _evento <> IntPtr.Zero Then SetEvent(_evento)
        Catch ex As Exception
            Diagnosi = Nome & ": lo svuotamento non parte, " & ex.Message & ". "
        End Try
    End Sub

    Friend Overrides Sub RichiediAvvio()
        Try
            If _evento <> IntPtr.Zero Then SetEvent(_evento)
        Catch ex As Exception
            Diagnosi = Nome & ": il risveglio non parte, " & ex.Message & ". "
        End Try
    End Sub

    Private Sub Ciclo()
        Dim maniglia As IntPtr = IntPtr.Zero
        Try
            Dim indice As Integer = 0
            maniglia = AvSetMmThreadCharacteristicsW("Pro Audio", indice)
        Catch exPriorita As Exception
            maniglia = IntPtr.Zero
            Diagnosi = Nome & ": la priorità audio non si imposta, " & exPriorita.Message & ". "
        End Try
        Try
            While Volatile.Read(_fermare) = 0
                WaitForSingleObject(_evento, 20)
                If Volatile.Read(_fermare) <> 0 Then Exit While
                If Interlocked.Exchange(_tagliaSubito, 0) = 1 Then
                    If Not Ricomincia() Then
                        Invalida()
                        Exit While
                    End If
                End If
                Dim inUso As UInteger
                Dim hr As Integer = _client.GetCurrentPadding(inUso)
                If hr < 0 OrElse CInt(inUso) > _dimensioneBuffer Then
                    Invalida()
                    Exit While
                End If
                Volatile.Write(_paddingUltimo, CInt(inUso))
                Dim inCoda As Integer = If(Coda Is Nothing, 0, Coda.Conteggio)
                If inCoda = 0 Then
                    If CLng(inUso) * 50L < CLng(FrequenzaUscita) Then _dopoSilenzio = True
                    Continue While
                End If
                Dim liberi As Integer = _dimensioneBuffer - CInt(inUso)
                If liberi <= 0 Then Continue While
                Dim scritti As Integer = 0
                If Not Riempi(Math.Min(liberi, inCoda), scritti) Then
                    Invalida()
                    Exit While
                End If
                Volatile.Write(_paddingUltimo, CInt(inUso) + scritti)
            End While
        Catch ex As Exception
            Diagnosi = Nome & ": il thread dell'uscita si è fermato, " & ex.Message & ". "
            Invalida()
        Finally
            Try
                If maniglia <> IntPtr.Zero Then AvRevertMmThreadCharacteristics(maniglia)
            Catch exRipristino As Exception
                Diagnosi = Nome & ": la priorità audio non si ripristina, " & exRipristino.Message & ". "
            End Try
        End Try
    End Sub

    Private Function Ricomincia() As Boolean
        Try
            If _client Is Nothing Then Return False
            _client.Stop()
            _client.Reset()
            Volatile.Write(_paddingUltimo, 0)
            _dopoSilenzio = True
            Return _client.Start() >= 0
        Catch ex As Exception
            Diagnosi = Nome & ": il flusso non riparte, " & ex.Message & ". "
            Return False
        End Try
    End Function

    Private Function Riempi(frame As Integer, ByRef scritti As Integer) As Boolean
        scritti = 0
        If frame <= 0 OrElse Coda Is Nothing Then Return True
        Dim letti As Integer = Coda.Leggi(_mono, Math.Min(frame, _mono.Length))
        If letti <= 0 Then Return True
        Ammorbidisci(_mono, letti)
        Dim dati As IntPtr = IntPtr.Zero
        Dim hr As Integer = _render.GetBuffer(CUInt(letti), dati)
        If hr < 0 OrElse dati = IntPtr.Zero Then Return False
        Dim canali As Integer = _formato.Canali
        Select Case _formato.Tipo
            Case TipoCampione.Virgola32
                Dim u As Single() = _uscitaVirgola
                For f As Integer = 0 To letti - 1
                    Dim v As Single = _mono(f)
                    Dim b As Integer = f * canali
                    u(b) = v
                    If canali > 1 Then u(b + 1) = v
                    For c As Integer = 2 To canali - 1
                        u(b + c) = 0.0F
                    Next
                Next
                Marshal.Copy(u, 0, dati, letti * canali)
            Case TipoCampione.Intero16
                Dim u As Short() = _uscitaIntera16
                For f As Integer = 0 To letti - 1
                    Dim v As Short = ComeIntero16(_mono(f))
                    Dim b As Integer = f * canali
                    u(b) = v
                    If canali > 1 Then u(b + 1) = v
                    For c As Integer = 2 To canali - 1
                        u(b + c) = 0
                    Next
                Next
                Marshal.Copy(u, 0, dati, letti * canali)
            Case TipoCampione.Intero32
                Dim u As Integer() = _uscitaIntera32
                For f As Integer = 0 To letti - 1
                    Dim v As Integer = ComeIntero32(_mono(f))
                    Dim b As Integer = f * canali
                    u(b) = v
                    If canali > 1 Then u(b + 1) = v
                    For c As Integer = 2 To canali - 1
                        u(b + c) = 0
                    Next
                Next
                Marshal.Copy(u, 0, dati, letti * canali)
            Case TipoCampione.Intero24
                Dim u As Byte() = _uscitaIntera24
                For f As Integer = 0 To letti - 1
                    Dim v As Integer = ComeIntero32(_mono(f))
                    Dim b As Integer = f * canali * 3
                    For c As Integer = 0 To canali - 1
                        Dim valore As Integer = If(c < 2, v, 0)
                        u(b + c * 3) = CByte((valore >> 8) And &HFF)
                        u(b + c * 3 + 1) = CByte((valore >> 16) And &HFF)
                        u(b + c * 3 + 2) = CByte((valore >> 24) And &HFF)
                    Next
                Next
                Marshal.Copy(u, 0, dati, letti * canali * 3)
        End Select
        If _render.ReleaseBuffer(CUInt(letti), 0) < 0 Then Return False
        scritti = letti
        Return True
    End Function

    Private Sub Ammorbidisci(campioni As Single(), quanti As Integer)
        If campioni Is Nothing OrElse quanti <= 0 Then Return
        For i As Integer = 0 To quanti - 1
            Dim v As Single = campioni(i)
            If v > 1.0F Then v = 1.0F
            If v < -1.0F Then v = -1.0F
            campioni(i) = v
        Next
        If Not _dopoSilenzio Then Return
        _dopoSilenzio = False
        Dim rampa As Integer = 16
        If _formato IsNot Nothing AndAlso _formato.Frequenza > 0 Then rampa = Math.Max(8, _formato.Frequenza \ 3000)
        If rampa > quanti Then rampa = quanti
        For i As Integer = 0 To rampa - 1
            campioni(i) *= CSng(i + 1) / CSng(rampa)
        Next
    End Sub

    Private Shared Function ComeIntero16(v As Single) As Short
        If v > 1.0F Then v = 1.0F
        If v < -1.0F Then v = -1.0F
        Return CShort(Math.Round(v * 32767.0F))
    End Function

    Private Shared Function ComeIntero32(v As Single) As Integer
        If v > 1.0F Then v = 1.0F
        If v < -1.0F Then v = -1.0F
        Return CInt(Math.Round(CDbl(v) * 2147483647.0))
    End Function

    Public Overrides Sub Dispose()
        If _chiuso Then Return
        _chiuso = True
        Try
            Volatile.Write(_fermare, 1)
            Volatile.Write(_valido, 0)
            If _evento <> IntPtr.Zero Then SetEvent(_evento)
            If _thread IsNot Nothing Then _thread.Join(2000)
        Catch ex As Exception
            Diagnosi = Nome & ": il thread non si chiude, " & ex.Message & ". "
        End Try
        Try
            If _client IsNot Nothing Then _client.Stop()
        Catch ex As Exception
            Diagnosi = Nome & ": il flusso non si ferma, " & ex.Message & ". "
        End Try
        Try
            If _notifica IsNot Nothing AndAlso _enumeratore IsNot Nothing Then _enumeratore.UnregisterEndpointNotificationCallback(_notifica)
        Catch ex As Exception
            Diagnosi = Nome & ": gli avvisi della scheda non si tolgono, " & ex.Message & ". "
        End Try
        Rilascia(_render)
        Rilascia(_client)
        Rilascia(_dispositivo)
        Rilascia(_enumeratore)
        _render = Nothing
        _client = Nothing
        _dispositivo = Nothing
        _enumeratore = Nothing
        Try
            If _evento <> IntPtr.Zero Then CloseHandle(_evento)
        Catch ex As Exception
            Diagnosi = Nome & ": l'evento non si chiude, " & ex.Message & ". "
        End Try
        _evento = IntPtr.Zero
    End Sub

    Private Sub Rilascia(oggetto As Object)
        Try
            If oggetto IsNot Nothing AndAlso Marshal.IsComObject(oggetto) Then Marshal.ReleaseComObject(oggetto)
        Catch ex As Exception
            Diagnosi = Nome & ": un oggetto audio non si rilascia, " & ex.Message & ". "
        End Try
    End Sub

End Class
