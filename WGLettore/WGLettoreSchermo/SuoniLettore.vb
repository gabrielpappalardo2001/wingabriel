Imports System.Threading
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders

' Interruttore generale: spento, nessun suono del lettore, da qualunque parte arrivi la richiesta.
Friend NotInheritable Class SuoniLettore

    Private Const Frequenza As Integer = 44100
    Private Const Volume As Single = 0.22F
    Private Const InattivitaMs As Integer = 5000

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _uscita As WaveOutEvent
    Private Shared _miscelatore As MixingSampleProvider
    Private Shared _controllo As System.Threading.Timer
    Private Shared _ultimoSuonoTick As Integer
    Private Shared _abilitati As Integer = 1

    Private Sub New()
    End Sub

    ' Interruttore generale: spento, nessun suono del lettore, da qualunque parte arrivi la richiesta.
    Public Shared Property Abilitati As Boolean
        Get
            Return Volatile.Read(_abilitati) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_abilitati, If(value, 1, 0))
            If Not value Then Ferma()
        End Set
    End Property

    Public Shared Sub ModalitaFocus()
        Try
            Suona(New Double() {523.25, 783.99}, 45)
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.ModalitaFocus: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ModalitaNavigazione()
        Try
            Suona(New Double() {783.99, 523.25}, 45)
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.ModalitaNavigazione: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Avviso()
        Suona(New Double() {880.0, 659.25, 880.0}, 70)
    End Sub

    Public Shared Sub LimiteRaggiunto()
        Suona(New Double() {196.0}, 90)
    End Sub

    Public Shared Sub TastoEsplorato()
        Suona(New Double() {1046.5}, 35)
    End Sub

    Public Shared Sub Progresso(percentuale As Integer)
        Try
            Dim valore As Integer = Math.Max(0, Math.Min(100, percentuale))
            Dim frequenza As Double = 110.0 * Math.Pow(2.0, valore / 25.0)
            Suona(New Double() {frequenza}, 40)
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.Progresso: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Ferma()
        Try
            SyncLock _blocco
                ChiudiUscita()
                If _controllo IsNot Nothing Then
                    _controllo.Dispose()
                    _controllo = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Suona(frequenze As Double(), durataMs As Integer)
        Try
            If Not Abilitati Then Return
            SyncLock _blocco
                PreparaUscita()
                If _miscelatore Is Nothing Then Return
                _miscelatore.AddMixerInput(New SequenzaToni(_miscelatore.WaveFormat, frequenze, durataMs, Volume))
                _ultimoSuonoTick = Environment.TickCount
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.Suona: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub PreparaUscita()
        Try
            If _uscita IsNot Nothing AndAlso _miscelatore IsNot Nothing Then Return
            ChiudiUscita()
            _miscelatore = New MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Frequenza, 1))
            _miscelatore.ReadFully = True
            _uscita = New WaveOutEvent()
            _uscita.DesiredLatency = 60
            _uscita.Init(_miscelatore)
            _uscita.Play()
            If _controllo Is Nothing Then _controllo = New System.Threading.Timer(AddressOf ControllaInattivita, Nothing, 1000, 1000)
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.PreparaUscita: " & ex.Message)
            ChiudiUscita()
        End Try
    End Sub

    Private Shared Sub ControllaInattivita(stato As Object)
        Try
            SyncLock _blocco
                If _uscita Is Nothing Then Return
                If Environment.TickCount - _ultimoSuonoTick < InattivitaMs Then Return
                ChiudiUscita()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.ControllaInattivita: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiudiUscita()
        Try
            If _uscita IsNot Nothing Then
                Try
                    _uscita.Stop()
                Catch ex As Exception
                    log.ScriviLog("SuoniLettore.ChiudiUscita (arresto): " & ex.Message)
                End Try
                _uscita.Dispose()
            End If
        Catch ex As Exception
            log.ScriviLog("SuoniLettore.ChiudiUscita: " & ex.Message)
        Finally
            _uscita = Nothing
            _miscelatore = Nothing
        End Try
    End Sub

    Private NotInheritable Class SequenzaToni
        Implements ISampleProvider

        Private ReadOnly _formato As WaveFormat
        Private ReadOnly _frequenze As Double()
        Private ReadOnly _campioniPerTono As Integer
        Private ReadOnly _rampa As Integer
        Private ReadOnly _volume As Single
        Private _posizione As Integer
        Private _fase As Double

        Public Sub New(formato As WaveFormat, frequenze As Double(), durataMs As Integer, volume As Single)
            Try
                _formato = formato
                _frequenze = If(frequenze, New Double() {})
                _campioniPerTono = Math.Max(1, CInt(CLng(formato.SampleRate) * durataMs \ 1000))
                _rampa = Math.Max(1, Math.Min(_campioniPerTono \ 6, CInt(formato.SampleRate \ 200)))
                _volume = volume
            Catch ex As Exception
                log.ScriviLog("SuoniLettore.SequenzaToni.New: " & ex.Message)
            End Try
        End Sub

        Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
            Get
                Return _formato
            End Get
        End Property

        Public Function Read(buffer As Single(), offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
            Dim scritti As Integer = 0
            Try
                If _frequenze Is Nothing OrElse _frequenze.Length = 0 Then Return 0
                Dim totale As Integer = _frequenze.Length * _campioniPerTono
                Dim passoCompleto As Double = 2.0 * Math.PI
                While scritti < count AndAlso _posizione < totale
                    Dim indiceTono As Integer = _posizione \ _campioniPerTono
                    Dim dentro As Integer = _posizione Mod _campioniPerTono
                    Dim inviluppo As Double = 1.0
                    If dentro < _rampa Then inviluppo = dentro / CDbl(_rampa)
                    Dim restanti As Integer = _campioniPerTono - dentro
                    If restanti < _rampa Then inviluppo = Math.Min(inviluppo, restanti / CDbl(_rampa))
                    _fase += passoCompleto * _frequenze(indiceTono) / _formato.SampleRate
                    If _fase >= passoCompleto Then _fase -= passoCompleto
                    buffer(offset + scritti) = CSng(Math.Sin(_fase) * inviluppo * _volume)
                    scritti += 1
                    _posizione += 1
                End While
            Catch ex As Exception
                log.ScriviLog("SuoniLettore.SequenzaToni.Read: " & ex.Message)
            End Try
            Return scritti
        End Function
    End Class

End Class