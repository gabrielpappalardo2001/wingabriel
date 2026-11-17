' ============================================================================================
' WGFotocameraAcquisizione - la voce dal microfono, con i punti per allinearla al sottofondo
' ============================================================================================
'
' Stesso metodo del registratore vocale: a ogni blocco di voce che arriva si annota anche a che punto era il sottofondo (la posizione
' realmente suonata dalla scheda audio). Alla fine AllineatoreVoce, con una retta che passa per questi punti, mette ogni campione della
' voce dove deve stare, anche se i due orologi corrono in modo un po' diverso. Serve alla fotocamera, e a calibrare la sincronia.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports NAudio.Wave

Friend NotInheritable Class AcquisizioneVoce

    Private Const FrequenzaPreferita As Integer = 48000
    Private ReadOnly _log As New Logger()
    Private _ingresso As WaveInEvent
    Private ReadOnly _memoria As New MemoryStream()
    Private _campioni As Long
    Private _uscita As WaveOutEvent
    Private _ultimaPosizione As Long
    Private ReadOnly _blocco As New Object()
    Private _attiva As Boolean

    Public Frequenza As Integer = FrequenzaPreferita
    Public ReadOnly PuntiVoce As New List(Of Long)()
    Public ReadOnly PuntiSottofondo As New List(Of Long)()

    ''' <summary>Apre il microfono (con 48000 Hz, o 44100 se il microfono non li regge). uscita, se c'è, è quello che si sente mentre si registra.</summary>
    Public Sub Avvia(dispositivo As Integer, uscita As WaveOutEvent)
        _uscita = uscita
        Dim ultimoErrore As Exception = Nothing
        For Each frequenza As Integer In New Integer() {48000, 44100}
            Dim ingresso As New WaveInEvent()
            ingresso.DeviceNumber = dispositivo
            ingresso.BufferMilliseconds = 10
            ingresso.NumberOfBuffers = 4
            ingresso.WaveFormat = New WaveFormat(frequenza, 16, 1)
            Me.Frequenza = frequenza
            AddHandler ingresso.DataAvailable, AddressOf OnDati
            Try
                _attiva = True
                ingresso.StartRecording()
                _ingresso = ingresso
                Return
            Catch ex As Exception
                _attiva = False
                ultimoErrore = ex
                RemoveHandler ingresso.DataAvailable, AddressOf OnDati
                Try
                    ingresso.Dispose()
                Catch
                End Try
            End Try
        Next
        Throw ultimoErrore
    End Sub

    Private Sub OnDati(sender As Object, e As WaveInEventArgs)
        Try
            If Not _attiva OrElse e.BytesRecorded <= 0 Then Return
            SyncLock _blocco
                _memoria.Write(e.Buffer, 0, e.BytesRecorded)
                _campioni += e.BytesRecorded \ 2
                Dim uscita As WaveOutEvent = _uscita
                If uscita IsNot Nothing AndAlso uscita.PlaybackState = PlaybackState.Playing Then
                    Dim posizione As Long = uscita.GetPosition() \ 8
                    If posizione > _ultimaPosizione AndAlso posizione >= 4800 Then
                        _ultimaPosizione = posizione
                        PuntiVoce.Add(_campioni)
                        PuntiSottofondo.Add(posizione)
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("AcquisizioneVoce.OnDati: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude il microfono e restituisce la voce registrata.</summary>
    Public Function Ferma() As Short()
        _attiva = False
        Try
            If _ingresso IsNot Nothing Then
                RemoveHandler _ingresso.DataAvailable, AddressOf OnDati
                Try
                    _ingresso.StopRecording()
                Catch
                End Try
                _ingresso.Dispose()
                _ingresso = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("AcquisizioneVoce.Ferma: " & ex.Message)
        End Try
        Dim byteVoce() As Byte
        SyncLock _blocco
            byteVoce = _memoria.ToArray()
        End SyncLock
        Dim campioni(byteVoce.Length \ 2 - 1) As Short
        System.Buffer.BlockCopy(byteVoce, 0, campioni, 0, campioni.Length * 2)
        Return campioni
    End Function

    ''' <summary>Retta di allineamento con i punti raccolti (se c'era un sottofondo che suonava).</summary>
    Public Sub Allineamento(ByRef a As Double, ByRef d As Double)
        a = WGDsp.FC / Frequenza
        d = 0.0
        Dim voce As List(Of Long)
        Dim sotto As List(Of Long)
        SyncLock _blocco
            voce = New List(Of Long)(PuntiVoce)
            sotto = New List(Of Long)(PuntiSottofondo)
        End SyncLock
        If voce.Count > 0 Then AllineatoreVoce.RettaDiAllineamento(voce, sotto, Frequenza, a, d)
    End Sub

End Class
