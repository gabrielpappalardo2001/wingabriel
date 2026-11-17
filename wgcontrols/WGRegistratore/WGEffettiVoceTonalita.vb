' ============================================================================================
' WGEffettiVoceTonalita - le note e l'accordatura del sottofondo, per intonare la voce su quelle
' ============================================================================================
'
' Se si canta sopra un sottofondo, le note giuste sono quelle del sottofondo, non quelle di una scala scelta a caso, e vanno prese
' all'altezza vera del brano: molte registrazioni sono accordate un poco sopra o sotto il La a 440 Hz. Qui si ascolta il sottofondo
' (fino a un minuto e mezzo) e si fanno due cose, con il metodo di Goertzel su pezzetti di 0,75 secondi:
'
' 1. l'accordatura: si misura l'energia delle note con cinque scarti possibili (da -40 a +40 centesimi di semitono) e lo scarto con più
'    energia è quello del brano (con un'interpolazione a parabola per i valori in mezzo);
' 2. le note: con quello scarto si misura l'energia di ciascuna delle dodici note (da Mi 2 a Sol 6, ogni pezzetto con lo stesso peso).
'    Le note che ci sono davvero nel brano sono quelle con energia vicina alle più forti; le altre sono solo armoniche e rumore. Si usano
'    le note e non il nome della tonalità perché nelle canzoni vere (dominanti, sottodominanti, note di passaggio) due tonalità vicine
'    si confondono, mentre le note presenti si leggono senza ambiguità.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports NAudio.Wave

Public NotInheritable Class TonalitaSottofondo


    Private Sub New()
    End Sub

    ''' <summary>L'energia delle note del brano (dodici valori, da Do a Si, la più forte vale 1) e la sua accordatura in centesimi di semitono rispetto al La a 440 Hz. Falso se non si riesce a misurare.</summary>
    Public Shared Function Analizza(percorso As String, ByRef croma() As Double, ByRef centesimi As Double) As Boolean
        Try
            Dim frequenza As Integer
            Dim x() As Single = LeggiCampioni(percorso, frequenza)
            Return AnalizzaCampioni(x, frequenza, croma, centesimi)
        Catch
            Return False
        End Try
    End Function

    ''' <summary>La stessa analisi su campioni già letti (un canale).</summary>
    Public Shared Function AnalizzaCampioni(x() As Single, frequenza As Integer, ByRef cromaFinale() As Double, ByRef centesimi As Double) As Boolean
        Dim scarti() As Double = {-40.0, -20.0, 0.0, 20.0, 40.0}
        Dim energie(scarti.Length - 1) As Double
        Dim migliore As Integer = 0
        For i As Integer = 0 To scarti.Length - 1
            Dim croma() As Double = CromaDaCampioni(x, frequenza, scarti(i), energie(i))
            If croma Is Nothing Then Return False
            If energie(i) > energie(migliore) Then migliore = i
        Next
        Dim sommaE As Double = 0.0
        Dim minE As Double = Double.MaxValue
        For i As Integer = 0 To energie.Length - 1
            sommaE += energie(i)
            minE = Math.Min(minE, energie(i))
        Next
        Dim fiducia As Double = If(sommaE > 0.0, (energie(migliore) - minE) / (sommaE / energie.Length), 0.0)
        Dim cent As Double = scarti(migliore)
        If migliore > 0 AndAlso migliore < scarti.Length - 1 Then
            ' parabola per i tre punti attorno al massimo
            Dim a As Double = energie(migliore - 1)
            Dim b As Double = energie(migliore)
            Dim c As Double = energie(migliore + 1)
            Dim den As Double = a - 2.0 * b + c
            If Math.Abs(den) > 0.000000001 Then cent += 20.0 * 0.5 * (a - c) / den
        End If
        ' secondo giro, più fine, attorno al punto trovato: cinque scarti a passi di 5 centesimi, e di nuovo la parabola
        Dim fini() As Double = {cent - 10.0, cent - 5.0, cent, cent + 5.0, cent + 10.0}
        Dim energieFini(fini.Length - 1) As Double
        Dim migliorFine As Integer = 2
        For i As Integer = 0 To fini.Length - 1
            Dim c As Double() = CromaDaCampioni(x, frequenza, fini(i), energieFini(i))
            If c Is Nothing Then Return False
            If energieFini(i) > energieFini(migliorFine) Then migliorFine = i
        Next
        Dim centraFine As Double = fini(migliorFine)
        If migliorFine > 0 AndAlso migliorFine < fini.Length - 1 Then
            Dim a2 As Double = energieFini(migliorFine - 1)
            Dim b2 As Double = energieFini(migliorFine)
            Dim c2 As Double = energieFini(migliorFine + 1)
            Dim den2 As Double = a2 - 2.0 * b2 + c2
            If Math.Abs(den2) > 0.000000001 Then centraFine += 5.0 * 0.5 * (a2 - c2) / den2
        End If
        cent = centraFine
        cent = Math.Max(-50.0, Math.Min(50.0, cent))
        Dim energia As Double
        Dim finale() As Double = CromaDaCampioni(x, frequenza, cent, energia)
        If finale Is Nothing Then Return False
        Dim sommaC As Double = 0.0
        For k As Integer = 0 To 11
            sommaC += finale(k)
        Next
        Dim piattezza As Double = sommaC / 12.0
        ' senza una tonalità riconoscibile (rumore, percussioni, note troppo deboli) l'accordatura e le note sarebbero casuali: non si usano
        If fiducia < 0.12 OrElse piattezza > 0.75 Then Return False
        cromaFinale = finale
        centesimi = cent
        Return True
    End Function

    Public Shared Function LeggiCampioni(percorso As String, ByRef frequenza As Integer) As Single()
        Dim campioni As New List(Of Single)()
        Using lettore As New AudioFileReader(percorso)
            Dim canali As Integer = lettore.WaveFormat.Channels
            Dim rapporto As Integer = Math.Max(1, lettore.WaveFormat.SampleRate \ 11025)
            frequenza = lettore.WaveFormat.SampleRate \ rapporto
            Dim massimo As Long = CLng(frequenza) * 90L
            Dim blocco(4095 * canali * rapporto - 1) As Single
            Dim n As Integer
            Do
                n = lettore.Read(blocco, 0, blocco.Length)
                Dim passo As Integer = canali * rapporto
                For i As Integer = 0 To n - passo Step passo
                    Dim s As Single = 0.0F
                    For k As Integer = 0 To passo - 1
                        s += blocco(i + k)
                    Next
                    campioni.Add(s / passo)
                Next
            Loop While n > 0 AndAlso campioni.Count < massimo
        End Using
        Return campioni.ToArray()
    End Function

    ''' <summary>Una nota c'è se la sua energia arriva a più di metà di quella della nota più forte; si tengono almeno cinque note.</summary>
    Public Shared Function NoteDaCroma(croma() As Double) As Boolean()
        If croma Is Nothing Then Return Nothing
        Dim note(11) As Boolean
        Dim n As Integer = 0
        For k As Integer = 0 To 11
            note(k) = croma(k) >= 0.5
            If note(k) Then n += 1
        Next
        If n < 5 Then
            ' poche note sopra la soglia: si prendono le cinque più forti
            Dim ordine As New List(Of Integer)()
            For k As Integer = 0 To 11
                ordine.Add(k)
            Next
            ordine.Sort(Function(a, b) croma(b).CompareTo(croma(a)))
            For k As Integer = 0 To 11
                note(k) = False
            Next
            For j As Integer = 0 To 4
                note(ordine(j)) = True
            Next
        End If
        Return note
    End Function

    ''' <summary>
    ''' L'energia delle dodici note (la più forte vale 1) con le frequenze spostate di tanti centesimi di semitono, e l'energia totale
    ''' misurata (serve a trovare l'accordatura). Nothing se il pezzo è troppo corto.
    ''' </summary>
    Public Shared Function CromaDaCampioni(x() As Single, frequenza As Integer, centesimi As Double, ByRef energiaTotale As Double) As Double()
        energiaTotale = 0.0
        Dim finestra As Integer = 8192
        Dim passo As Integer = 8192
        If x.Length < finestra * 2 Then Return Nothing
        Dim hann(finestra - 1) As Double
        For i As Integer = 0 To finestra - 1
            hann(i) = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / finestra)
        Next
        Dim primaNota As Integer = 40
        Dim ultimaNota As Integer = 91
        Dim coeff(ultimaNota - primaNota) As Double
        For m As Integer = primaNota To ultimaNota
            Dim f As Double = 440.0 * Math.Pow(2.0, (m - 69 + centesimi / 100.0) / 12.0)
            coeff(m - primaNota) = 2.0 * Math.Cos(2.0 * Math.PI * f / frequenza)
        Next
        Dim croma(11) As Double
        Dim blocco(finestra - 1) As Double
        Dim pezzo(11) As Double
        For inizio As Integer = 0 To x.Length - finestra Step passo
            For i As Integer = 0 To finestra - 1
                blocco(i) = x(inizio + i) * hann(i)
            Next
            Array.Clear(pezzo, 0, 12)
            Dim totale As Double = 0.0
            For m As Integer = primaNota To ultimaNota
                Dim c As Double = coeff(m - primaNota)
                Dim s1 As Double = 0.0
                Dim s2 As Double = 0.0
                For i As Integer = 0 To finestra - 1
                    Dim s As Double = blocco(i) + c * s1 - s2
                    s2 = s1
                    s1 = s
                Next
                Dim potenza As Double = Math.Max(0.0, s1 * s1 + s2 * s2 - c * s1 * s2)
                Dim ampiezza As Double = Math.Sqrt(potenza)
                pezzo(m Mod 12) += ampiezza
                totale += ampiezza
            Next
            ' ogni pezzetto pesa uguale (i tratti forti non coprono i deboli), e i silenzi non pesano
            If totale > 0.0001 Then
                For k As Integer = 0 To 11
                    croma(k) += pezzo(k) / totale
                Next
                ' per l'accordatura conta quanta energia cade proprio sulle note (con l'accordatura giusta le armoniche cadono al centro di ogni nota)
                energiaTotale += Math.Sqrt(PiuAlta(pezzo))
            End If
        Next
        Dim massimo As Double = 0.0
        For k As Integer = 0 To 11
            massimo = Math.Max(massimo, croma(k))
        Next
        If massimo <= 0.0 Then Return Nothing
        For k As Integer = 0 To 11
            croma(k) /= massimo
        Next
        Return croma
    End Function

    Private Shared Function PiuAlta(pezzo() As Double) As Double
        Dim somma As Double = 0.0
        For k As Integer = 0 To 11
            somma += pezzo(k) * pezzo(k)
        Next
        Return somma
    End Function

End Class
