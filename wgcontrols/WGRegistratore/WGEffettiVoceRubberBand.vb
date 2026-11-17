' ============================================================================================
' WGEffettiVoceRubberBand - la correzione della melodia con Rubber Band (libreria rubberband-sharp, da NuGet)
' ============================================================================================
'
' Un editor di note come Melodyne non ricampiona la traccia: ogni nota si sposta da sola, di quanto serve, e il timbro delle vocali (le
' "formanti") resta quello della voce; così una voce spostata di un semitono resta la stessa voce, mentre ricampionarla sposta anche le
' formanti e la voce diventa metallica, "da robot". Rubber Band fa proprio questo: cambia l'altezza conservando le formanti.
'
' La curva di correzione (semitoni per quadro di 8 millisecondi, calcolata dal correttore) si divide in tratti a correzione quasi costante: di
' solito uno per nota. Ogni tratto, con un po' di margine prima e dopo, passa da Rubber Band in modalità offline con il suo rapporto, che
' in questo modo è esatto; il tratto spostato si rimette al suo posto con dissolvenze di 20 millisecondi, ai bordi, verso la voce originale
' o verso il tratto vicino. Le parti non intonate (consonanti, respiri, silenzi) non si toccano. Il vibrato della voce resta quello vero.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports RubberBand

Friend NotInheritable Class SpostatoreRubberBand

    Private Shared _stato As Integer = -1

    Private Sub New()
    End Sub

    ''' <summary>Vero se la libreria (compresa la sua parte nativa) si carica e funziona su questo computer.</summary>
    Public Shared Function Disponibile() As Boolean
        If _stato >= 0 Then Return _stato = 1
        Dim ok As Boolean = False
        Try
            Using st As New RubberBandStretcher(48000, 1, Opzioni(), 1.0, 1.0)
                ok = st.GetChannelCount() = 1
            End Using
        Catch
            ok = False
        End Try
        _stato = If(ok, 1, 0)
        Return ok
    End Function

    Private Shared Function Opzioni() As RubberBandStretcher.Options
        Return RubberBandStretcher.Options.ProcessOffline Or RubberBandStretcher.Options.FormantPreserved Or RubberBandStretcher.Options.PitchHighQuality Or
               RubberBandStretcher.Options.TransientsMixed Or RubberBandStretcher.Options.WindowStandard Or RubberBandStretcher.Options.PhaseLaminar Or
               RubberBandStretcher.Options.ThreadingNever
    End Function

    Private NotInheritable Class Tratto
        Public Da As Integer
        Public A As Integer
        Public Semitoni As Double
    End Class

    ''' <summary>
    ''' Divide le frasi intonate in tratti a correzione quasi costante: un nuovo tratto quando la correzione si sposta di più di 0,12 semitoni
    ''' dalla media del tratto per almeno tre quadri, o quando il tratto dura più di mezzo secondo (la correzione di una nota lunga può
    ''' cambiare piano piano). Restano fuori i tratti con correzione trascurabile.
    ''' </summary>
    Private Shared Function Tratti(correzione() As Double, eIntonata() As Boolean, quadri As Integer, passo As Integer, lunghezza As Integer) As List(Of Tratto)
        Dim elenco As New List(Of Tratto)()
        Dim q0 As Integer = 0
        While q0 < quadri
            If Not eIntonata(q0) Then
                q0 += 1
                Continue While
            End If
            Dim q1 As Integer = q0
            While q1 < quadri AndAlso eIntonata(q1)
                q1 += 1
            End While
            Dim inizio As Integer = q0
            Dim somma As Double = 0.0
            Dim conta As Integer = 0
            Dim fuori As Integer = 0
            For q As Integer = q0 To q1 - 1
                Dim media As Double = If(conta > 0, somma / conta, correzione(q))
                If conta > 0 AndAlso Math.Abs(correzione(q) - media) > 0.12 Then fuori += 1 Else fuori = 0
                If (fuori >= 3 AndAlso q - fuori + 1 > inizio) OrElse q - inizio > 60 Then
                    Dim confine As Integer = If(fuori >= 3, q - fuori + 1, q)
                    AggiungiTratto(elenco, correzione, inizio, confine, passo, lunghezza)
                    inizio = confine
                    somma = 0.0
                    conta = 0
                    For j As Integer = inizio To q
                        somma += correzione(j)
                        conta += 1
                    Next
                    fuori = 0
                Else
                    somma += correzione(q)
                    conta += 1
                End If
            Next
            AggiungiTratto(elenco, correzione, inizio, q1, passo, lunghezza)
            q0 = q1
        End While
        Return elenco
    End Function

    Private Shared Sub AggiungiTratto(elenco As List(Of Tratto), correzione() As Double, q0 As Integer, q1 As Integer, passo As Integer, lunghezza As Integer)
        If q1 - q0 < 4 Then Return
        Dim somma As Double = 0.0
        For q As Integer = q0 To q1 - 1
            somma += correzione(q)
        Next
        Dim media As Double = somma / (q1 - q0)
        If Math.Abs(media) < 0.03 Then Return
        Dim t As New Tratto()
        t.Da = Math.Min(lunghezza, q0 * passo)
        t.A = Math.Min(lunghezza, q1 * passo)
        t.Semitoni = media
        If t.A > t.Da Then elenco.Add(t)
    End Sub

    ''' <summary>Sposta un pezzo di traccia di tanti semitoni, con Rubber Band offline: stessa lunghezza, formanti conservate.</summary>
    Private Shared Function SpostaPezzo(x() As Single, da As Integer, a As Integer, fs As Integer, semitoni As Double) As Single()
        Dim n As Integer = a - da
        Dim ingresso(0)() As Single
        ingresso(0) = New Single(n - 1) {}
        Array.Copy(x, da, ingresso(0), 0, n)
        Dim uscita(n - 1) As Single
        Dim scritti As Integer = 0
        Using st As New RubberBandStretcher(fs, 1, Opzioni(), 1.0, Math.Pow(2.0, semitoni / 12.0))
            st.SetExpectedInputDuration(n)
            st.Study(ingresso, True)
            st.Process(ingresso, True)
            Dim cuscinetto(0)() As Single
            cuscinetto(0) = New Single(8191) {}
            Dim disponibili As Integer = st.Available()
            While disponibili > 0 AndAlso scritti < n
                Dim richiesti As Integer = Math.Min(disponibili, cuscinetto(0).Length)
                Dim letti As Integer = CInt(st.Retrieve(cuscinetto, richiesti))
                If letti <= 0 Then Exit While
                For k As Integer = 0 To letti - 1
                    If scritti + k < n Then uscita(scritti + k) = cuscinetto(0)(k)
                Next
                scritti += letti
                disponibili = st.Available()
            End While
        End Using
        Return uscita
    End Function

    ''' <summary>Sposta le note della traccia di quanto dice la curva (semitoni per quadro), con la stessa lunghezza.</summary>
    Public Shared Function SpostaPerNote(x() As Single, fs As Integer, passo As Integer, quadri As Integer, eIntonata() As Boolean, correzione() As Double) As Single()
        Dim n As Integer = x.Length
        Dim somma(n - 1) As Double
        Dim peso(n - 1) As Double
        Dim margine As Integer = CInt(0.05 * fs)
        Dim meta As Integer = CInt(0.01 * fs)
        For Each t As Tratto In Tratti(correzione, eIntonata, quadri, passo, n)
            Dim da As Integer = Math.Max(0, t.Da - margine)
            Dim a As Integer = Math.Min(n, t.A + margine)
            Dim spostato() As Single = SpostaPezzo(x, da, a, fs, t.Semitoni)
            ' i pesi: 1 dentro il tratto, con dissolvenza lineare di 20 millisecondi attorno ai due bordi (da -10 a +10 millisecondi)
            For i As Integer = Math.Max(0, t.Da - meta) To Math.Min(n - 1, t.A + meta - 1)
                Dim su As Double = Math.Max(0.0, Math.Min(1.0, (i - (t.Da - meta)) / (2.0 * meta)))
                Dim giu As Double = Math.Max(0.0, Math.Min(1.0, ((t.A + meta) - i) / (2.0 * meta)))
                Dim w As Double = Math.Min(su, giu)
                somma(i) += w * spostato(i - da)
                peso(i) += w
            Next
        Next
        Dim y(n - 1) As Single
        For i As Integer = 0 To n - 1
            Dim w As Double = Math.Min(1.0, peso(i))
            y(i) = CSng(somma(i) + (1.0 - w) * x(i))
        Next
        Return y
    End Function

End Class
