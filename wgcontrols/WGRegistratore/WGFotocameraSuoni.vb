' ============================================================================================
' WGFotocameraSuoni - i suoni della fotocamera: scatto, inizio e fine del video
' ============================================================================================
'
' Sono suoni generati qui, brevi e senza parole. Lo scatto è un doppio clic (come un otturatore); l'inizio del video sono due note che
' salgono, la fine due note che scendono. L'inizio e la fine si aspettano fino all'ultimo suono, così il microfono non li registra.
' ============================================================================================
Imports System
Imports System.Threading
Imports NAudio.Wave

Friend NotInheritable Class SuoniFotocamera

    Private Shared ReadOnly _log As New Logger()
    Private Const Frequenza As Integer = 44100

    Private Sub New()
    End Sub

    Private Shared Function Nota(frequenzaNota As Double, durata As Double, volume As Double) As Single()
        Dim n As Integer = CInt(durata * Frequenza)
        Dim x(n - 1) As Single
        For i As Integer = 0 To n - 1
            Dim t As Double = i / CDbl(Frequenza)
            Dim inviluppo As Double = Math.Min(1.0, t / 0.005) * Math.Min(1.0, (durata - t) / 0.02)
            x(i) = CSng(Math.Sin(2.0 * Math.PI * frequenzaNota * t) * inviluppo * volume)
        Next
        Return x
    End Function

    Private Shared Function Unisci(ParamArray parti()() As Single) As Single()
        Dim totale As Integer = 0
        For Each p As Single() In parti
            totale += p.Length
        Next
        Dim x(totale - 1) As Single
        Dim pos As Integer = 0
        For Each p As Single() In parti
            Array.Copy(p, 0, x, pos, p.Length)
            pos += p.Length
        Next
        Return x
    End Function

    Private Shared Function Scatto() As Single()
        Dim rnd As New Random(7)
        Dim n As Integer = CInt(0.16 * Frequenza)
        Dim x(n - 1) As Single
        For colpo As Integer = 0 To 1
            Dim inizio As Integer = CInt(If(colpo = 0, 0.0, 0.085) * Frequenza)
            Dim forza As Double = If(colpo = 0, 0.85, 0.6)
            For i As Integer = 0 To CInt(0.05 * Frequenza)
                If inizio + i >= n Then Exit For
                Dim t As Double = i / CDbl(Frequenza)
                x(inizio + i) += CSng(((rnd.NextDouble() * 2.0 - 1.0) * Math.Exp(-t * 140.0) * 0.7 + Math.Sin(2.0 * Math.PI * 210.0 * t) * Math.Exp(-t * 70.0) * 0.5) * forza)
            Next
        Next
        Return x
    End Function

    Private Shared Sub Suona(campioni() As Single, aspetta As Boolean)
        Try
            Dim byteCampioni(campioni.Length * 2 - 1) As Byte
            For i As Integer = 0 To campioni.Length - 1
                Dim v As Short = CShort(Math.Max(-32767.0, Math.Min(32767.0, campioni(i) * 32767.0)))
                byteCampioni(i * 2) = CByte(v And &HFF)
                byteCampioni(i * 2 + 1) = CByte((v >> 8) And &HFF)
            Next
            Dim flusso As New RawSourceWaveStream(New System.IO.MemoryStream(byteCampioni), New WaveFormat(Frequenza, 16, 1))
            Dim uscita As New WaveOutEvent()
            uscita.Init(flusso)
            Dim fine As New ManualResetEvent(False)
            AddHandler uscita.PlaybackStopped, Sub(s As Object, e As StoppedEventArgs)
                                                   Try
                                                       uscita.Dispose()
                                                       flusso.Dispose()
                                                   Catch
                                                   End Try
                                                   fine.Set()
                                               End Sub
            uscita.Play()
            If aspetta Then fine.WaitOne(CInt(campioni.Length * 1000L \ Frequenza) + 600)
        Catch ex As Exception
            _log.ScriviLog("SuoniFotocamera.Suona: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SuonaScatto()
        Suona(Scatto(), False)
    End Sub

    ''' <summary>Il suono di inizio del video: si ferma finché non è finito.</summary>
    Public Shared Sub SuonaInizioVideo()
        Suona(Unisci(Nota(880.0, 0.09, 0.5), Nota(1320.0, 0.14, 0.5)), True)
    End Sub

    Public Shared Sub SuonaFineVideo()
        Suona(Unisci(Nota(1320.0, 0.09, 0.5), Nota(880.0, 0.14, 0.5)), True)
    End Sub

End Class
