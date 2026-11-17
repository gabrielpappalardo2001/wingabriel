' ============================================================================================
' WGEffettiVoceAltezza - correzione dell'intonazione e cambio di tono della voce
' ============================================================================================
'
' Si fa in tre passi, tutti sul file registrato e senza spostare niente nel tempo (durata e inizio restano identici):
'
' 1. L'altezza. Ogni 8 millisecondi si misura l'altezza della voce con il metodo YIN (differenza normalizzata, su una copia a 16 kHz).
'    Tra i possibili candidati si sceglie quello più vicino all'altezza di poco prima, così non si salta di ottava, e si ignorano i
'    pezzi troppo corti per essere una vocale. Dove la voce non è intonata (silenzi, consonanti, fruscii) non si tocca niente.
' 2. Il bersaglio. L'altezza si porta alla nota più vicina della scala scelta (tutte le note, maggiore o minore, con la nota di
'    partenza). "Autotune" ci arriva subito (circa 25 millisecondi) e toglie anche il vibrato; "Melodia" corregge solo quanto la nota
'    è stonata in media (sullo scorrere di un quarto di secondo), e lascia il vibrato e le sfumature. La nota non cambia per un
'    soffio: serve uscire di più di un terzo di semitono dalla nota scelta.
' 3. Il cambio. Con sovrapposizione di pezzetti di voce (WSOLA con ricampionamento): ogni 16 millisecondi si prende un pezzetto di 32
'    millisecondi dalla voce vera, letto più in fretta o più piano di quanto serve a spostare l'altezza, e lo si rimette nello stesso
'    punto; il punto da cui si legge si cerca (entro un periodo di voce) dove il pezzetto continua meglio quello di prima, così le onde
'    si sommano senza battimenti. Non servono i picchi della voce, che nel parlato vero sono incerti: serve solo sapere di quanto
'    spostare. Il tono scelto si somma alla correzione.
' ============================================================================================
Imports System
Imports System.Collections.Generic

Friend NotInheritable Class CorrettoreAltezza


    Private Sub New()
    End Sub

    Private Const FrequenzaAnalisi As Integer = 16000
    Private Const FinestraMs As Double = 40.0
    Private Const PassoMs As Double = 8.0
    Private Const FMin As Double = 70.0
    Private Const FMax As Double = 1100.0

    ''' <summary>La traccia con l'altezza corretta (stessa lunghezza), o la stessa traccia se non c'è niente da correggere.</summary>
    Public Shared Function Elabora(x() As Single, fs As Integer, imp As ImpostazioniEffettiVoce) As Single()
        Dim eMelodia As Boolean = imp.Intonazione = "melodia"
        Dim eAutotune As Boolean = imp.Intonazione = "autotune"
        If Not eMelodia AndAlso Not eAutotune AndAlso imp.Semitoni = 0 Then Return x
        Dim n As Integer = x.Length
        Dim passo As Integer = Math.Max(1, CInt(PassoMs * fs / 1000.0))
        Dim quadri As Integer = n \ passo + 1
        Dim f0(quadri - 1) As Double
        Dim eIntonata(quadri - 1) As Boolean
        Rileva(x, fs, passo, quadri, f0, eIntonata)
        If eMelodia Then
            ' la melodia si corregge nota per nota (vedi CorrezioneNote); il tono scelto si somma dove la voce è intonata
            Dim perNota() As Double = CorrezioneNote(f0, eIntonata, quadri, imp)
            For q As Integer = 0 To quadri - 1
                If eIntonata(q) Then perNota(q) += imp.Semitoni
            Next
            Return SpostaMelodia(x, fs, passo, quadri, f0, eIntonata, perNota)
        End If
        ' la correzione in semitoni per ogni quadro (solo dove la voce è intonata)
        Dim correzione(quadri - 1) As Double
        Dim nota As Double = Double.NaN
        Dim mediaCorta As Double = 0.0
        Dim mediaLunga As Double = 0.0
        Dim conteggio As Integer = 0
        Dim attuale As Double = 0.0
        Dim alfa As Double = 1.0 - Math.Exp(-PassoMs / If(eAutotune, 25.0, 70.0))
        Dim alfaCorta As Double = 1.0 - Math.Exp(-PassoMs / 60.0)
        Dim alfaLunga As Double = 1.0 - Math.Exp(-PassoMs / 250.0)
        For q As Integer = 0 To quadri - 1
            If Not eIntonata(q) Then
                nota = Double.NaN
                conteggio = 0
                attuale = attuale * (1.0 - alfa)
                correzione(q) = attuale
                Continue For
            End If
            Dim midi As Double = 69.0 + 12.0 * Math.Log(f0(q) / 440.0, 2.0)
            If eAutotune OrElse eMelodia Then
                ' la nota si sceglie sull'altezza smussata (60 ms: il vibrato gira intorno alla nota e non la cambia, uno scivolamento sì);
                ' per non cambiare nota per un soffio, serve uscire di più di tre quinti di semitono da quella di prima.
                ' Nei primi quadri di una vocale la media è quella di tutti i quadri visti finora.
                If conteggio = 0 Then
                    mediaCorta = midi
                    mediaLunga = midi
                ElseIf conteggio < 8 Then
                    mediaCorta += (midi - mediaCorta) / (conteggio + 1)
                    mediaLunga += (midi - mediaLunga) / (conteggio + 1)
                Else
                    mediaCorta += alfaCorta * (midi - mediaCorta)
                    mediaLunga += alfaLunga * (midi - mediaLunga)
                End If
                conteggio += 1
                Dim candidata As Double = NotaPiuVicina(mediaCorta, imp)
                If Double.IsNaN(nota) OrElse (candidata <> nota AndAlso Math.Abs(mediaCorta - nota) > 0.6) Then nota = candidata
                Dim desiderata As Double
                If eAutotune Then
                    desiderata = nota - midi
                Else
                    ' melodia: la nota si porta tutta al suo posto (centro della nota sulla nota giusta) e il vibrato si riduce di due quinti:
                    ' le sfumature restano, ma la correzione si sente
                    desiderata = Math.Max(-2.0, Math.Min(2.0, nota - mediaLunga - 0.4 * (midi - mediaLunga)))
                End If
                attuale += alfa * (desiderata - attuale)
            Else
                attuale = 0.0
            End If
            correzione(q) = attuale + imp.Semitoni
        Next
        Return SpostaAltezza(x, fs, passo, quadri, f0, eIntonata, correzione)
    End Function

    ''' <summary>
    ''' La correzione "melodia", nota per nota come in un editor di note: la voce intonata si divide nelle note cantate (una nuova nota quando
    ''' l'altezza si ferma a più di due terzi di semitono dalla precedente per almeno 32 millisecondi); di ogni nota si prende l'altezza
    ''' centrale (la mediana), la si porta verso la nota giusta della scala con la forza scelta, e le oscillazioni attorno al centro
    ''' (vibrato e scivolamenti) si tengono nella misura scelta. Gli scivolamenti tra una nota e l'altra restano, perché stanno ai bordi.
    ''' </summary>
    Private Shared Function CorrezioneNote(f0() As Double, eIntonata() As Boolean, quadri As Integer, imp As ImpostazioniEffettiVoce) As Double()
        Dim c(quadri - 1) As Double
        Dim forza As Double = Math.Max(0, Math.Min(100, imp.ForzaMelodia)) / 100.0
        Dim tenuto As Double = Math.Max(0, Math.Min(100, imp.VibratoMantenuto)) / 100.0
        Dim midi(quadri - 1) As Double
        For q As Integer = 0 To quadri - 1
            If eIntonata(q) AndAlso f0(q) > 0.0 Then midi(q) = 69.0 + 12.0 * Math.Log(f0(q) / 440.0, 2.0)
        Next
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
            ' l'altezza smussata (media su sette quadri) serve solo a trovare i confini tra le note
            Dim liscio(q1 - q0 - 1) As Double
            For q As Integer = q0 To q1 - 1
                Dim s As Double = 0.0
                Dim k As Integer = 0
                For j As Integer = Math.Max(q0, q - 3) To Math.Min(q1 - 1, q + 3)
                    s += midi(j)
                    k += 1
                Next
                liscio(q - q0) = s / k
            Next
            Dim livello As Double = Math.Round(liscio(0))
            Dim inizioNota As Integer = q0
            Dim fuori As Integer = 0
            For q As Integer = q0 To q1 - 1
                If Math.Abs(liscio(q - q0) - livello) > 0.67 Then
                    fuori += 1
                Else
                    fuori = 0
                End If
                If fuori >= 4 Then
                    Dim confine As Integer = q - fuori + 1
                    If confine > inizioNota Then ChiudiNota(midi, c, inizioNota, confine, forza, tenuto, imp)
                    inizioNota = Math.Max(confine, inizioNota)
                    livello = Math.Round(liscio(q - q0))
                    fuori = 0
                End If
            Next
            ChiudiNota(midi, c, inizioNota, q1, forza, tenuto, imp)
            ' la correzione si ammorbidisce (media su cinque quadri) e sfuma ai bordi della frase
            Dim m(q1 - q0 - 1) As Double
            For q As Integer = q0 To q1 - 1
                Dim s As Double = 0.0
                Dim k As Integer = 0
                For j As Integer = Math.Max(q0, q - 2) To Math.Min(q1 - 1, q + 2)
                    s += c(j)
                    k += 1
                Next
                Dim peso As Double = Math.Min(1.0, Math.Min((q - q0 + 1) / 3.0, (q1 - q) / 3.0))
                m(q - q0) = s / k * peso
            Next
            For q As Integer = q0 To q1 - 1
                c(q) = m(q - q0)
            Next
            q0 = q1
        End While
        Return c
    End Function

    Private Shared Sub ChiudiNota(midi() As Double, c() As Double, da As Integer, a As Integer, forza As Double, tenuto As Double, imp As ImpostazioniEffettiVoce)
        If a <= da Then Return
        Dim valori As New List(Of Double)()
        For q As Integer = da To a - 1
            valori.Add(midi(q))
        Next
        valori.Sort()
        Dim centro As Double = valori(valori.Count \ 2)
        Dim bersaglio As Double = NotaPiuVicina(centro, imp)
        For q As Integer = da To a - 1
            Dim scarto As Double = Math.Max(-1.2, Math.Min(1.2, midi(q) - centro))
            c(q) = forza * (bersaglio - centro) + (tenuto - 1.0) * scarto
        Next
    End Sub

    Private Shared Function NotaPiuVicina(midi As Double, imp As ImpostazioniEffettiVoce) As Double
        Dim scala() As Integer
        Select Case imp.Scala
            Case "maggiore"
                scala = New Integer() {0, 2, 4, 5, 7, 9, 11}
            Case "minore"
                scala = New Integer() {0, 2, 3, 5, 7, 8, 10}
            Case Else
                scala = New Integer() {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}
        End Select
        ' le note del brano si prendono all'altezza vera del brano (la sua accordatura)
        Dim accordatura As Double = imp.Accordatura / 100.0
        Dim m As Double = midi - accordatura
        If imp.NoteAmmesse IsNot Nothing Then
            ' con le note di un sottofondo si va sempre al semitono più vicino, sulla griglia accordata come il brano: un accompagnamento non
            ' mostra tutte le note che una melodia canta (note di passaggio, settime), e spingere la voce solo sulle sue note la rovinerebbe.
            ' Le note del brano decidono solo quando la voce cade a metà strada tra due semitoni (a più di 35 centesimi dal più vicino): se solo
            ' l'altro semitono è una nota del brano, si va a quello.
            Dim n0 As Integer = CInt(Math.Round(m))
            Dim scarto As Double = m - n0
            Dim scelta As Integer = n0
            If Math.Abs(scarto) > 0.35 Then
                Dim n1 As Integer = n0 + If(scarto > 0, 1, -1)
                If imp.NoteAmmesse(((n1 Mod 12) + 12) Mod 12) AndAlso Not imp.NoteAmmesse(((n0 Mod 12) + 12) Mod 12) Then scelta = n1
            End If
            Return scelta + accordatura
        End If
        Dim migliore As Double = Math.Round(m)
        Dim distanza As Double = Double.MaxValue
        Dim centro As Integer = CInt(Math.Round(m))
        For c As Integer = centro - 4 To centro + 4
            If imp.NoteAmmesse IsNot Nothing Then
                If Not imp.NoteAmmesse((((c Mod 12) + 12) Mod 12)) Then Continue For
            Else
                Dim classe As Integer = (((c - imp.Tonica) Mod 12) + 12) Mod 12
                If Array.IndexOf(scala, classe) < 0 Then Continue For
            End If
            Dim d As Double = Math.Abs(c - m)
            If d < distanza Then
                distanza = d
                migliore = c
            End If
        Next
        Return migliore + accordatura
    End Function

    ' ---- 1. l'altezza ---------------------------------------------------------------------------------------

    Private Shared Sub Rileva(x() As Single, fs As Integer, passo As Integer, quadri As Integer, f0() As Double, eIntonata() As Boolean)
        Dim riduzione As Integer = Math.Max(1, CInt(Math.Round(fs / CDbl(FrequenzaAnalisi))))
        Dim fr As Double = fs / CDbl(riduzione)
        Dim nd As Integer = x.Length \ riduzione
        Dim xd(nd - 1) As Single
        For m As Integer = 0 To nd - 1
            Dim somma As Single = 0.0F
            For k As Integer = 0 To riduzione - 1
                somma += x(m * riduzione + k)
            Next
            xd(m) = somma / riduzione
        Next
        Dim finestra As Integer = CInt(FinestraMs * fr / 1000.0)
        Dim tauMin As Integer = Math.Max(2, CInt(fr / FMax))
        Dim tauMax As Integer = CInt(fr / FMin)
        Dim d(tauMax + 1) As Double
        Dim cm(tauMax + 2) As Double
        ' il livello della voce, per distinguere il parlato dal fruscio
        Dim soglia As Double = SogliaEnergia(xd)
        Dim precedente As Double = 0.0
        For q As Integer = 0 To quadri - 1
            Dim centro As Integer = (q * passo) \ riduzione
            Dim inizio As Integer = centro - finestra \ 2
            If inizio < 0 OrElse inizio + finestra + tauMax + 1 >= nd Then Continue For
            Dim energia As Double = 0.0
            For j As Integer = 0 To finestra - 1
                energia += xd(inizio + j) * xd(inizio + j)
            Next
            energia = Math.Sqrt(energia / finestra)
            If energia < soglia Then
                precedente = 0.0
                Continue For
            End If
            For tau As Integer = 1 To tauMax
                Dim somma As Double = 0.0
                For j As Integer = 0 To finestra - 1
                    Dim diff As Double = xd(inizio + j) - xd(inizio + j + tau)
                    somma += diff * diff
                Next
                d(tau) = somma
            Next
            Dim cumulata As Double = 0.0
            cm(0) = 1.0
            For tau As Integer = 1 To tauMax
                cumulata += d(tau)
                cm(tau) = If(cumulata > 0.0, d(tau) * tau / cumulata, 1.0)
            Next
            ' i candidati: i minimi locali della differenza normalizzata. Un sottomultiplo (metà o un terzo dell'altezza vera: periodo doppio o triplo)
            ' ha quasi lo stesso valore del periodo giusto, e scegliere quello sposterebbe la nota di un'ottava o di una dodicesima. Quindi, tra i
            ' candidati buoni quasi quanto il migliore, si prende quello più vicino all'altezza di poco prima; senza altezza di poco prima, il più acuto.
            Dim taus As New List(Of Double)()
            Dim valori As New List(Of Double)()
            Dim migliorValoreAssoluto As Double = 1.0
            For tau As Integer = tauMin To tauMax - 1
                If cm(tau) < cm(tau - 1) AndAlso cm(tau) <= cm(tau + 1) AndAlso cm(tau) < 0.35 Then
                    Dim tauFine As Double = tau
                    Dim den As Double = cm(tau - 1) - 2.0 * cm(tau) + cm(tau + 1)
                    If Math.Abs(den) > 0.000000001 Then tauFine = tau + 0.5 * (cm(tau - 1) - cm(tau + 1)) / den
                    taus.Add(tauFine)
                    valori.Add(cm(tau))
                    If cm(tau) < migliorValoreAssoluto Then migliorValoreAssoluto = cm(tau)
                End If
            Next
            Dim migliorTau As Double = 0.0
            Dim migliorValore As Double = 1.0
            Dim distanzaMinima As Double = Double.MaxValue
            For i As Integer = 0 To taus.Count - 1
                If valori(i) > migliorValoreAssoluto + 0.1 Then Continue For
                If precedente > 0.0 Then
                    Dim distanza As Double = Math.Abs(Math.Log((fr / taus(i)) / precedente, 2.0))
                    If distanza < distanzaMinima AndAlso distanza < 0.25 Then
                        distanzaMinima = distanza
                        migliorTau = taus(i)
                        migliorValore = valori(i)
                    End If
                End If
            Next
            If migliorTau = 0.0 Then
                ' nessun candidato vicino a quello di prima (o è l'inizio di una nota): il più acuto tra i buoni
                For i As Integer = 0 To taus.Count - 1
                    If valori(i) > migliorValoreAssoluto + 0.1 Then Continue For
                    If migliorTau = 0.0 OrElse taus(i) < migliorTau Then
                        migliorTau = taus(i)
                        migliorValore = valori(i)
                    End If
                Next
            End If
            If migliorTau > 0.0 AndAlso migliorValore < 0.3 Then
                f0(q) = fr / migliorTau
                eIntonata(q) = True
                precedente = f0(q)
            Else
                precedente = 0.0
            End If
        Next
        ' un pezzo troppo corto non è una vocale: si ignora; poi la mediana su cinque quadri toglie i salti isolati
        Dim n As Integer = quadri
        Dim q0 As Integer = 0
        While q0 < n
            If Not eIntonata(q0) Then
                q0 += 1
                Continue While
            End If
            Dim q1 As Integer = q0
            While q1 < n AndAlso eIntonata(q1)
                q1 += 1
            End While
            If q1 - q0 < 4 Then
                For q As Integer = q0 To q1 - 1
                    eIntonata(q) = False
                Next
            End If
            q0 = q1
        End While
        Dim filtrata(n - 1) As Double
        For q As Integer = 0 To n - 1
            If Not eIntonata(q) Then Continue For
            Dim vicini As New List(Of Double)()
            For k As Integer = Math.Max(0, q - 2) To Math.Min(n - 1, q + 2)
                If eIntonata(k) Then vicini.Add(f0(k))
            Next
            vicini.Sort()
            filtrata(q) = vicini(vicini.Count \ 2)
        Next
        For q As Integer = 0 To n - 1
            If eIntonata(q) Then f0(q) = filtrata(q)
        Next
    End Sub

    Private Shared Function SogliaEnergia(xd() As Single) As Double
        Dim blocco As Integer = 320
        Dim livelli As New List(Of Double)()
        For i As Integer = 0 To xd.Length - blocco Step blocco
            Dim s As Double = 0.0
            For j As Integer = 0 To blocco - 1
                s += xd(i + j) * xd(i + j)
            Next
            livelli.Add(Math.Sqrt(s / blocco))
        Next
        If livelli.Count = 0 Then Return 0.0
        livelli.Sort()
        ' un decimo del livello dei pezzi forti della voce, e comunque sopra il fondo (il quinto percentile)
        Dim alto As Double = livelli(CInt(livelli.Count * 0.9) Mod livelli.Count)
        Dim basso As Double = livelli(CInt(livelli.Count * 0.05))
        Return Math.Max(alto * 0.1, basso * 2.0)
    End Function

    ' ---- 3. il cambio d'altezza -----------------------------------------------------------------------------

    Private Shared Function Interpola(x() As Single, posizione As Double) As Single
        Dim i As Integer = CInt(Math.Floor(posizione))
        If i < 1 OrElse i >= x.Length - 2 Then
            If i < 0 OrElse i >= x.Length - 1 Then Return 0.0F
            Dim ft As Single = CSng(posizione - i)
            Return x(i) + (x(i + 1) - x(i)) * ft
        End If
        Dim t As Double = posizione - i
        Dim ym1 As Double = x(i - 1)
        Dim y0 As Double = x(i)
        Dim y1 As Double = x(i + 1)
        Dim y2 As Double = x(i + 2)
        Dim c1 As Double = 0.5 * (y1 - ym1)
        Dim c2 As Double = ym1 - 2.5 * y0 + 2.0 * y1 - 0.5 * y2
        Dim c3 As Double = 0.5 * (y2 - ym1) + 1.5 * (y0 - y1)
        Return CSng(((c3 * t + c2) * t + c1) * t + y0)
    End Function

    ''' <summary>Se vero (lo si può spegnere solo nelle prove) l'altezza si sposta con Rubber Band, quando la libreria si carica.</summary>
    Friend Shared UsaRubberBand As Boolean = True

    ''' <summary>
    ''' Sposta le note della melodia con Rubber Band, che conserva il timbro delle vocali e il vibrato vero (niente suono "da robot" di un
    ''' ricampionamento); se la libreria non c'è o non parte, con il motore a sovrapposizione di pezzetti (SpostaAltezza).
    ''' </summary>
    Private Shared Function SpostaMelodia(x() As Single, fs As Integer, passo As Integer, quadri As Integer, f0() As Double, eIntonata() As Boolean, correzione() As Double) As Single()
        If UsaRubberBand AndAlso SpostatoreRubberBand.Disponibile() Then
            Try
                Return SpostatoreRubberBand.SpostaPerNote(x, fs, passo, quadri, eIntonata, correzione)
            Catch
            End Try
        End If
        Return SpostaAltezza(x, fs, passo, quadri, f0, eIntonata, correzione)
    End Function

    ''' <summary>Il passo d'altezza (in semitoni) a un punto della traccia, dalla curva per quadri.</summary>
    Private Shared Function CorrezioneIn(posizione As Double, passo As Integer, correzione() As Double, quadri As Integer) As Double
        Dim q As Double = posizione / passo
        Dim i As Integer = Math.Max(0, Math.Min(quadri - 1, CInt(Math.Floor(q))))
        Dim j As Integer = Math.Min(quadri - 1, i + 1)
        Dim t As Double = Math.Max(0.0, Math.Min(1.0, q - i))
        Return correzione(i) + (correzione(j) - correzione(i)) * t
    End Function

    ''' <summary>Il periodo della voce vicino a un punto (dal quadro intonato più vicino); se non ce n'è, quello di una voce media.</summary>
    Private Shared Function PeriodoIn(posizione As Double, passo As Integer, f0() As Double, eIntonata() As Boolean, quadri As Integer, fs As Integer) As Double
        Dim i As Integer = Math.Max(0, Math.Min(quadri - 1, CInt(Math.Round(posizione / passo))))
        For d As Integer = 0 To 6
            If i - d >= 0 AndAlso eIntonata(i - d) AndAlso f0(i - d) > 0.0 Then Return fs / f0(i - d)
            If i + d < quadri AndAlso eIntonata(i + d) AndAlso f0(i + d) > 0.0 Then Return fs / f0(i + d)
        Next
        Return fs / 150.0
    End Function

    ''' <summary>
    ''' Sposta l'altezza della traccia di quanto dice la curva (semitoni per quadro), senza cambiare la durata. Vedi il punto 3 in cima al file.
    ''' </summary>
    Private Shared Function SpostaAltezza(x() As Single, fs As Integer, passo As Integer, quadri As Integer, f0() As Double, eIntonata() As Boolean, correzione() As Double) As Single()
        Dim n As Integer = x.Length
        Dim lg As Integer = 2 * Math.Max(16, CInt(0.032 * fs) \ 2)
        Dim hs As Integer = lg \ 2
        Dim d As Integer = Math.Max(1, fs \ 8000)
        ' la copia per la ricerca: media di d campioni (un passa-basso rozzo), a un d-esimo della frequenza
        Dim nd As Integer = n \ d
        Dim xd(Math.Max(nd, 1) - 1) As Single
        For m As Integer = 0 To nd - 1
            Dim somma As Single = 0.0F
            For k As Integer = 0 To d - 1
                somma += x(m * d + k)
            Next
            xd(m) = somma / d
        Next
        Dim mezzoD As Double = (d - 1) / 2.0
        Dim finestra(lg - 1) As Single
        For j As Integer = 0 To lg - 1
            finestra(j) = CSng(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * j / lg))
        Next
        Dim y(n + 2 * lg - 1) As Single
        Dim nTem As Integer = hs \ d
        Dim modello(nTem - 1) As Single
        Dim nTemFine As Integer = hs \ 2
        Dim modelloFine(nTemFine - 1) As Single
        Dim haModello As Boolean = False
        Dim ultimoGrano As Integer = (n + hs) \ hs
        For g As Integer = -1 To ultimoGrano
            Dim inizio As Integer = g * hs
            Dim centro As Double = inizio + hs
            Dim semitoni As Double = CorrezioneIn(Math.Max(0.0, Math.Min(n - 1.0, centro)), passo, correzione, quadri)
            Dim r As Double = Math.Pow(2.0, semitoni / 12.0)
            Dim delta As Double = 0.0
            If Math.Abs(semitoni) > 0.01 AndAlso haModello Then
                Dim portata As Double = Math.Min(CDbl(hs), 1.1 * PeriodoIn(centro, passo, f0, eIntonata, quadri, fs))
                ' ricerca grossolana sulla copia ridotta, un passo di d campioni alla volta
                Dim migliore As Double = Double.MinValue
                Dim migliorDelta As Double = 0.0
                Dim dd As Double = -portata
                While dd <= portata
                    Dim somma As Double = 0.0
                    Dim energia As Double = 0.000000000001
                    For jd As Integer = 0 To nTem - 1
                        Dim pos As Double = centro + dd + (jd * d - hs) * r
                        Dim v As Double = Interpola(xd, (pos - mezzoD) / d)
                        somma += modello(jd) * v
                        energia += v * v
                    Next
                    Dim punteggio As Double = somma / Math.Sqrt(energia)
                    If punteggio > migliore Then
                        migliore = punteggio
                        migliorDelta = dd
                    End If
                    dd += d
                End While
                ' rifinitura a campione intero attorno al punto trovato, sulla traccia vera
                Dim rifinito As Double = migliorDelta
                Dim migliore2 As Double = Double.MinValue
                For passoFine As Integer = -d To d
                    Dim dd2 As Double = migliorDelta + passoFine
                    Dim somma As Double = 0.0
                    Dim energia As Double = 0.000000000001
                    For jf As Integer = 0 To nTemFine - 1
                        Dim pos As Double = centro + dd2 + (jf * 2 - hs) * r
                        Dim v As Double = Interpola(x, pos)
                        somma += modelloFine(jf) * v
                        energia += v * v
                    Next
                    Dim punteggio As Double = somma / Math.Sqrt(energia)
                    If punteggio > migliore2 Then
                        migliore2 = punteggio
                        rifinito = dd2
                    End If
                Next
                delta = rifinito
            End If
            ' il pezzetto: letto con passo r (più di uno = voce più acuta) e finestrato
            For j As Integer = 0 To lg - 1
                Dim i As Integer = inizio + j + hs
                If i < 0 OrElse i >= y.Length Then Continue For
                y(i) += Interpola(x, centro + delta + (j - hs) * r) * finestra(j)
            Next
            ' il modello per il pezzetto dopo: come questo pezzetto continuerebbe nella seconda metà
            For jd As Integer = 0 To nTem - 1
                Dim pos As Double = centro + delta + jd * d * r
                modello(jd) = Interpola(xd, (pos - mezzoD) / d)
            Next
            For jf As Integer = 0 To nTemFine - 1
                modelloFine(jf) = Interpola(x, centro + delta + jf * 2 * r)
            Next
            haModello = True
        Next
        Dim uscita(n - 1) As Single
        Array.Copy(y, hs, uscita, 0, n)
        Return uscita
    End Function

End Class
