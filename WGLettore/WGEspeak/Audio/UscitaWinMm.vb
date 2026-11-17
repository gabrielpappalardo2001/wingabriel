' ============================================================================================
' UscitaWinMm - l'uscita con le vecchie API di Windows (waveOut), dove WASAPI non c'è o non parte
' ============================================================================================
'
' Due blocchi da 10 millisecondi, mono a 16 bit, alla frequenza della voce: ci pensa Windows a portarli alla
' frequenza della scheda. Un thread aspetta che Windows finisca un blocco e ne prepara subito un altro.
' Uno stop azzera i blocchi già in coda, sullo stesso thread. Più lento di WASAPI, ma gira su qualsiasi Windows.
' ============================================================================================
Imports System.Runtime.InteropServices
Imports System.Threading

Friend NotInheritable Class UscitaWinMm
    Inherits MotoreUscita

    Private Const CALLBACK_EVENT As Integer = &H50000
    Private Const WHDR_INQUEUE As Integer = &H10
    Private Const WAVE_FORMAT_PCM As Short = 1
    Private Const NumeroBlocchi As Integer = 2
    Private Const MillisecondiPerBlocco As Integer = 10

    <StructLayout(LayoutKind.Sequential, Pack:=1)>
    Private Structure FormatoWaveEx
        Public Tag As Short
        Public Canali As Short
        Public Frequenza As Integer
        Public ByteAlSecondo As Integer
        Public AllineamentoBlocco As Short
        Public BitPerCampione As Short
        Public Extra As Short
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure IntestazioneWave
        Public Dati As IntPtr
        Public LunghezzaBuffer As Integer
        Public ByteRegistrati As Integer
        Public DatiUtente As IntPtr
        Public Flag As Integer
        Public Cicli As Integer
        Public Successivo As IntPtr
        Public Riservato As IntPtr
    End Structure

    <DllImport("winmm.dll")>
    Private Shared Function waveOutOpen(ByRef uscita As IntPtr, dispositivo As Integer, ByRef formato As FormatoWaveEx, richiamo As IntPtr, istanza As IntPtr, flags As Integer) As Integer
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function waveOutPrepareHeader(uscita As IntPtr, intestazione As IntPtr, dimensione As Integer) As Integer
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function waveOutUnprepareHeader(uscita As IntPtr, intestazione As IntPtr, dimensione As Integer) As Integer
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function waveOutWrite(uscita As IntPtr, intestazione As IntPtr, dimensione As Integer) As Integer
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function waveOutReset(uscita As IntPtr) As Integer
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function waveOutClose(uscita As IntPtr) As Integer
    End Function

    Private _uscita As IntPtr = IntPtr.Zero
    Private _evento As IntPtr = IntPtr.Zero
    Private _thread As Thread
    Private _fermare As Integer
    Private _valido As Integer
    Private _chiuso As Boolean
    Private _campioniPerBlocco As Integer
    Private _dimensioneIntestazione As Integer
    Private _offsetFlag As Integer
    Private ReadOnly _intestazioni(NumeroBlocchi - 1) As IntPtr
    Private ReadOnly _dati(NumeroBlocchi - 1) As IntPtr
    Private _mono As Single()
    Private _corti As Short()
    ' Quanti campioni di voce (non di silenzio) ha ogni blocco: i blocchi sono sempre tutti in coda.
    Private ReadOnly _campioniDiVoce(NumeroBlocchi - 1) As Integer
    Private _resetRichiesto As Integer

    Friend Overrides ReadOnly Property Nome As String
        Get
            Return "WinMM"
        End Get
    End Property

    Friend Overrides ReadOnly Property LatenzaMs As Double
        Get
            Return NumeroBlocchi * MillisecondiPerBlocco
        End Get
    End Property

    Friend Overrides ReadOnly Property Valido As Boolean
        Get
            Return Volatile.Read(_valido) = 1 AndAlso Volatile.Read(_fermare) = 0
        End Get
    End Property

    Friend Overrides Sub SvuotaHardware()
        Interlocked.Exchange(_resetRichiesto, 1)
        If _evento <> IntPtr.Zero Then SetEvent(_evento)
    End Sub

    Friend Overrides Sub RichiediAvvio()
        Dim soloSilenzio As Boolean = True
        For i As Integer = 0 To NumeroBlocchi - 1
            If Volatile.Read(_campioniDiVoce(i)) > 0 Then soloSilenzio = False
        Next
        If Not soloSilenzio Then Return
        Interlocked.Exchange(_resetRichiesto, 1)
        If _evento <> IntPtr.Zero Then SetEvent(_evento)
    End Sub

    Friend Overrides ReadOnly Property MillisecondiInHardware As Integer
        Get
            Dim campioni As Long = 0
            For i As Integer = 0 To NumeroBlocchi - 1
                If _intestazioni(i) <> IntPtr.Zero AndAlso (Marshal.ReadInt32(_intestazioni(i), _offsetFlag) And WHDR_INQUEUE) <> 0 Then campioni += Volatile.Read(_campioniDiVoce(i))
            Next
            Return CInt(campioni * 1000L \ Math.Max(1, FrequenzaUscita))
        End Get
    End Property

    Friend Overrides Function Apri(frequenzaIngresso As Integer) As Boolean
        Try
            FrequenzaUscita = frequenzaIngresso
            Coda = New CodaCampioni(frequenzaIngresso * 20)
            _campioniPerBlocco = Math.Max(64, frequenzaIngresso * MillisecondiPerBlocco \ 1000)
            _mono = New Single(_campioniPerBlocco - 1) {}
            _corti = New Short(_campioniPerBlocco - 1) {}
            _dimensioneIntestazione = Marshal.SizeOf(GetType(IntestazioneWave))
            _offsetFlag = CInt(Marshal.OffsetOf(GetType(IntestazioneWave), "Flag"))

            _evento = CreateEventW(IntPtr.Zero, False, False, Nothing)
            If _evento = IntPtr.Zero Then
                Diagnosi = "WinMM: l'evento non si crea."
                Return False
            End If

            Dim formato As New FormatoWaveEx() With {
                .Tag = WAVE_FORMAT_PCM, .Canali = 1, .Frequenza = frequenzaIngresso,
                .ByteAlSecondo = frequenzaIngresso * 2, .AllineamentoBlocco = 2, .BitPerCampione = 16, .Extra = 0}
            Dim esito As Integer = waveOutOpen(_uscita, -1, formato, _evento, IntPtr.Zero, CALLBACK_EVENT)
            If esito <> 0 Then
                Diagnosi = "WinMM: l'uscita non si apre (errore " & esito.ToString() & ")."
                Return False
            End If

            For i As Integer = 0 To NumeroBlocchi - 1
                _dati(i) = Marshal.AllocHGlobal(_campioniPerBlocco * 2)
                _intestazioni(i) = Marshal.AllocHGlobal(_dimensioneIntestazione)
                Dim intestazione As New IntestazioneWave() With {.Dati = _dati(i), .LunghezzaBuffer = _campioniPerBlocco * 2}
                Marshal.StructureToPtr(intestazione, _intestazioni(i), False)
                esito = waveOutPrepareHeader(_uscita, _intestazioni(i), _dimensioneIntestazione)
                If esito <> 0 Then
                    Diagnosi = "WinMM: un blocco non si prepara (errore " & esito.ToString() & ")."
                    Return False
                End If
            Next

            _valido = 1
            ' Tutti i blocchi partono subito: poi il thread li rinnova uno a uno.
            For i As Integer = 0 To NumeroBlocchi - 1
                If Not Riempi(i) Then
                    _valido = 0
                    Diagnosi = "WinMM: la scrittura non parte."
                    Return False
                End If
            Next
            _thread = New Thread(AddressOf Ciclo) With {.IsBackground = True, .Name = "WGUscitaAudio WinMM", .Priority = ThreadPriority.Highest}
            _thread.Start()
            Return True
        Catch ex As Exception
            Diagnosi = "WinMM: " & ex.Message & ". "
            Return False
        End Try
    End Function

    ' Un blocco: la voce che c'è, silenzio per il resto.
    Private Function Riempi(indice As Integer) As Boolean
        Dim letti As Integer = Coda.Leggi(_mono, _campioniPerBlocco)
        For i As Integer = 0 To _campioniPerBlocco - 1
            Dim v As Single = If(i < letti, _mono(i), 0.0F)
            If v > 1.0F Then v = 1.0F
            If v < -1.0F Then v = -1.0F
            _corti(i) = CShort(Math.Round(v * 32767.0F))
        Next
        Marshal.Copy(_corti, 0, _dati(indice), _campioniPerBlocco)
        Volatile.Write(_campioniDiVoce(indice), letti)
        Return waveOutWrite(_uscita, _intestazioni(indice), _dimensioneIntestazione) = 0
    End Function

    Private Sub Ciclo()
        Try
            While Volatile.Read(_fermare) = 0
                WaitForSingleObject(_evento, 100)
                If Volatile.Read(_fermare) <> 0 Then Exit While
                If Interlocked.Exchange(_resetRichiesto, 0) = 1 Then
                    If _uscita <> IntPtr.Zero Then waveOutReset(_uscita)
                    For i As Integer = 0 To NumeroBlocchi - 1
                        Volatile.Write(_campioniDiVoce(i), 0)
                    Next
                    For i As Integer = 0 To NumeroBlocchi - 1
                        If Not Riempi(i) Then
                            Volatile.Write(_valido, 0)
                            Return
                        End If
                    Next
                    Continue While
                End If
                For i As Integer = 0 To NumeroBlocchi - 1
                    If (Marshal.ReadInt32(_intestazioni(i), _offsetFlag) And WHDR_INQUEUE) = 0 Then
                        If Not Riempi(i) Then
                            Volatile.Write(_valido, 0)
                            Return
                        End If
                    End If
                Next
            End While
        Catch
            Volatile.Write(_valido, 0)
        End Try
    End Sub

    Public Overrides Sub Dispose()
        If _chiuso Then Return
        _chiuso = True
        Try
            Volatile.Write(_fermare, 1)
            Volatile.Write(_valido, 0)
            If _evento <> IntPtr.Zero Then SetEvent(_evento)
            If _thread IsNot Nothing Then _thread.Join(2000)
        Catch
        End Try
        Try
            If _uscita <> IntPtr.Zero Then waveOutReset(_uscita)
            For i As Integer = 0 To NumeroBlocchi - 1
                If _intestazioni(i) <> IntPtr.Zero AndAlso _uscita <> IntPtr.Zero Then waveOutUnprepareHeader(_uscita, _intestazioni(i), _dimensioneIntestazione)
            Next
            If _uscita <> IntPtr.Zero Then waveOutClose(_uscita)
        Catch
        End Try
        _uscita = IntPtr.Zero
        For i As Integer = 0 To NumeroBlocchi - 1
            Try
                If _intestazioni(i) <> IntPtr.Zero Then Marshal.FreeHGlobal(_intestazioni(i))
                If _dati(i) <> IntPtr.Zero Then Marshal.FreeHGlobal(_dati(i))
            Catch
            End Try
            _intestazioni(i) = IntPtr.Zero
            _dati(i) = IntPtr.Zero
        Next
        Try
            If _evento <> IntPtr.Zero Then CloseHandle(_evento)
        Catch
        End Try
        _evento = IntPtr.Zero
    End Sub

End Class
