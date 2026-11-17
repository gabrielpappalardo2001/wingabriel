' ============================================================================================
' WGEffettiVoce - gli effetti che il registratore vocale e la fotocamera applicano alla voce appena registrata
' ============================================================================================
'
' Gli effetti si applicano alla traccia di voce subito dopo la registrazione, prima di metterla al suo posto rispetto al sottofondo
' e al video. Nessuno sposta la voce nel tempo: la traccia resta lunga uguale (solo l'eco aggiunge la coda che si spegne) e il suo
' inizio resta dov'era, così la sincronia con sottofondo e video non cambia. L'ordine è questo:
'
'   1. riduzione del rumore: taglia i rumori sotto i 70 Hz e toglie il fruscio con il riduttore a FFT di ffmpeg (afftdn), che
'      impara il rumore da solo; Leggero, Medio o Forte è la quantità di fruscio tolta (10, 18 o 28 decibel);
'   2. intonazione e tono: correzione dell'altezza con sovrapposizione di pezzetti di voce (vedi WGEffettiVoceAltezza.vb); il tono scelto
'      lo dà il filtro rubberband di ffmpeg, che conserva il timbro delle vocali, se c'è, altrimenti lo stesso correttore;
'   3. voce chiara: cinque piccoli ritocchi di equalizzazione (via i rimbombi bassi, più presenza e aria);
'   4. compressore: acompressor di ffmpeg (soglia -20 dB, rapporto 3,5, attacco 8 ms, rilascio 120 ms, guadagno di recupero 6 dB), con
'      un limitatore (alimiter) che non lascia superare 0,95: la voce resta più uniforme e non scoppia;
'   5. volume uniforme: la voce si porta a un livello fisso (-20 dB) senza superare il massimo;
'   6. riverbero (stanza, sala o grande), con la sua coda che si spegne;
'   7. eco: aecho di ffmpeg con quattro ripetizioni (250, 500, 750 e 1000 ms) che si spengono, su una coda di un secondo e mezzo.
'
' Servono gli stessi ffmpeg che usano le altre parti di WinGabriel (ffmpeg.exe accanto al programma, scaricato dal centro di controllo),
' che non hanno bisogno di internet per lavorare. Se ffmpeg manca o non ha questi filtri, rumore, compressore ed eco si fanno con le
' versioni semplici scritte qui; l'intonazione non ne ha bisogno.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Text
Imports NAudio.Wave

''' <summary>Quali effetti sono attivi e con che valori; si leggono dalle impostazioni del registratore.</summary>
Public NotInheritable Class ImpostazioniEffettiVoce

    Public Rumore As Boolean
    ''' <summary>Quanto fruscio si toglie, in decibel (10 leggero, 18 medio, 28 forte).</summary>
    Public RumoreDb As Integer = 18
    Public Compressore As Boolean
    Public Eco As Boolean
    ''' <summary>"nessuno", "stanza", "sala" o "grande".</summary>
    Public Riverbero As String = "nessuno"
    ''' <summary>Voce più chiara e presente: toglie i rimbombi bassi, dà corpo alle consonanti e un poco d'aria.</summary>
    Public Chiarezza As Boolean
    ''' <summary>Porta la voce a un volume sempre uguale, senza spingerla oltre il massimo.</summary>
    Public Livella As Boolean
    ''' <summary>"nessuna", "melodia" o "autotune".</summary>
    Public Intonazione As String = "nessuna"
    ''' <summary>Quanto alzare o abbassare la voce, in semitoni (da -12 a 12).</summary>
    Public Semitoni As Integer
    ''' <summary>Quanto la melodia si porta sulla nota giusta, da 0 a 100 per cento.</summary>
    Public ForzaMelodia As Integer = 100
    ''' <summary>Quanto del vibrato e degli scivolamenti si tiene nella melodia, da 0 a 100 per cento.</summary>
    Public VibratoMantenuto As Integer = 100
    ''' <summary>Il file del sottofondo su cui si canta (lo mettono i chiamanti); serve alla scala "sottofondo".</summary>
    Public Sottofondo As String = ""
    ''' <summary>Le note presenti nel sottofondo (dodici valori da Do a Si): se ci sono, sono le uniche note giuste.</summary>
    Public NoteAmmesse() As Boolean
    ''' <summary>L'accordatura del sottofondo, in centesimi di semitono rispetto al La a 440 Hz.</summary>
    Public Accordatura As Double
    ''' <summary>"cromatica", "maggiore", "minore" o "sottofondo" (la tonalità si ricava dal sottofondo).</summary>
    Public Scala As String = "cromatica"
    ''' <summary>La nota di partenza della scala, da 0 (Do) a 11 (Si).</summary>
    Public Tonica As Integer

    Public Shared ReadOnly NomiNote() As String = {"Do", "Do diesis", "Re", "Re diesis", "Mi", "Fa", "Fa diesis", "Sol", "Sol diesis", "La", "La diesis", "Si"}

    Public Shared Function Leggi() As ImpostazioniEffettiVoce
        Dim i As New ImpostazioniEffettiVoce()
        i.Rumore = ImpostazioniMusica.LeggiBooleano("voce_fx_rumore", False)
        i.RumoreDb = Math.Max(6, Math.Min(40, ImpostazioniMusica.LeggiIntero("voce_fx_rumore_db", 18)))
        i.Compressore = ImpostazioniMusica.LeggiBooleano("voce_fx_compressore", False)
        i.Eco = ImpostazioniMusica.LeggiBooleano("voce_fx_eco", False)
        i.Riverbero = ImpostazioniMusica.Leggi("voce_fx_riverbero", "nessuno")
        If i.Riverbero <> "stanza" AndAlso i.Riverbero <> "sala" AndAlso i.Riverbero <> "grande" Then i.Riverbero = "nessuno"
        i.Chiarezza = ImpostazioniMusica.LeggiBooleano("voce_fx_chiarezza", False)
        i.Livella = ImpostazioniMusica.LeggiBooleano("voce_fx_livella", False)
        i.Intonazione = ImpostazioniMusica.Leggi("voce_fx_intonazione", "nessuna")
        If i.Intonazione <> "melodia" AndAlso i.Intonazione <> "autotune" Then i.Intonazione = "nessuna"
        i.ForzaMelodia = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("voce_fx_melodia_forza", 100)))
        i.VibratoMantenuto = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("voce_fx_melodia_vibrato", 100)))
        i.Semitoni = Math.Max(-12, Math.Min(12, ImpostazioniMusica.LeggiIntero("voce_fx_semitoni", 0)))
        i.Scala = ImpostazioniMusica.Leggi("voce_fx_scala", "sottofondo")
        If i.Scala <> "maggiore" AndAlso i.Scala <> "minore" AndAlso i.Scala <> "sottofondo" Then i.Scala = "cromatica"
        i.Tonica = Math.Max(0, Math.Min(11, ImpostazioniMusica.LeggiIntero("voce_fx_tonica", 0)))
        Return i
    End Function

    Public ReadOnly Property Attivo As Boolean
        Get
            Return Rumore OrElse Compressore OrElse Eco OrElse Chiarezza OrElse Livella OrElse Riverbero <> "nessuno" OrElse Intonazione <> "nessuna" OrElse Semitoni <> 0
        End Get
    End Property

    ''' <summary>Una copia con un altro tono (serve a dare prima la correzione e poi il tono con un altro sistema).</summary>
    Public Function ConSemitoni(semitoni As Integer) As ImpostazioniEffettiVoce
        Dim c As ImpostazioniEffettiVoce = CType(Me.MemberwiseClone(), ImpostazioniEffettiVoce)
        c.Semitoni = semitoni
        Return c
    End Function

    Public Function DescrizioneScala() As String
        If Scala = "cromatica" Then Return "tutte le note"
        If Scala = "sottofondo" Then Return "le note del sottofondo"
        Return "scala di " & NomiNote(Tonica) & If(Scala = "maggiore", " maggiore", " minore")
    End Function

    ''' <summary>Gli effetti attivi, detti a voce.</summary>
    Public Function Descrizione() As String
        If Not Attivo Then Return "nessun effetto"
        Dim v As New List(Of String)()
        If Rumore Then v.Add("riduzione del rumore " & If(RumoreDb <= 12, "leggera", If(RumoreDb >= 24, "forte", "media")))
        If Intonazione = "autotune" Then v.Add("autotune su " & DescrizioneScala())
        If Intonazione = "melodia" Then v.Add("correzione della melodia su " & DescrizioneScala())
        If Semitoni <> 0 Then v.Add("tono " & If(Semitoni > 0, "più acuto", "più grave") & " di " & Math.Abs(Semitoni).ToString() & If(Math.Abs(Semitoni) = 1, " semitono", " semitoni"))
        If Chiarezza Then v.Add("voce chiara")
        If Compressore Then v.Add("compressore")
        If Livella Then v.Add("volume uniforme")
        If Riverbero <> "nessuno" Then v.Add("riverbero " & If(Riverbero = "grande", "da ambiente grande", If(Riverbero = "sala", "da sala", "da stanza")))
        If Eco Then v.Add("eco")
        Return String.Join(", ", v)
    End Function

    Private Shared Function PuntoVoce(valore As Boolean) As String
        Return If(valore, "attivo", "non attivo")
    End Function

End Class

''' <summary>Le elaborazioni vere e proprie.</summary>
Public NotInheritable Class EffettiVoce

    Private Shared ReadOnly _log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Applica gli effetti a una traccia di voce (16 bit, un canale). Restituisce la traccia nuova, lunga uguale tranne che con l'eco;
    ''' se qualcosa va storto restituisce la traccia com'era.
    ''' </summary>
    Public Shared Function Applica(campioni() As Short, frequenza As Integer, imp As ImpostazioniEffettiVoce) As Short()
        Try
            If imp Is Nothing OrElse Not imp.Attivo OrElse campioni.Length < frequenza \ 10 Then Return campioni
            Dim x(campioni.Length - 1) As Single
            For i As Integer = 0 To campioni.Length - 1
                x(i) = campioni(i) / 32768.0F
            Next
            x = Elabora(x, frequenza, imp)
            Dim uscita(x.Length - 1) As Short
            For i As Integer = 0 To x.Length - 1
                uscita(i) = CShort(Math.Max(-32767.0, Math.Min(32767.0, x(i) * 32767.0)))
            Next
            Return uscita
        Catch ex As Exception
            _log.ScriviLog("EffettiVoce.Applica: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return campioni
        End Try
    End Function

    ''' <summary>Gli effetti sui campioni in virgola mobile (da -1 a 1), nell'ordine descritto in cima; serve anche alle prove.</summary>
    Public Shared Function Elabora(x() As Single, frequenza As Integer, imp As ImpostazioniEffettiVoce) As Single()
        Dim conFfmpeg As Boolean = FfmpegPronto()
        If imp.Scala = "sottofondo" Then
            ' le note giuste sono quelle del sottofondo: se non c'è o non si capisce la tonalità, valgono tutte le note
            imp = imp.ConSemitoni(imp.Semitoni)
            Dim croma() As Double = Nothing
            Dim centesimi As Double
            If imp.Sottofondo <> "" AndAlso File.Exists(imp.Sottofondo) AndAlso TonalitaSottofondo.Analizza(imp.Sottofondo, croma, centesimi) Then
                imp.NoteAmmesse = TonalitaSottofondo.NoteDaCroma(croma)
                imp.Accordatura = centesimi
            End If
            imp.Scala = "cromatica"
        End If
        If imp.Rumore Then
            Dim r As Single() = Nothing
            If conFfmpeg Then
                ' il fruscio si misura dalla traccia stessa (i pezzi più silenziosi), così il riduttore sa quanto è forte e quanto deve toglierne
                Dim fondo As Double = StimaFondoDb(x, frequenza)
                ' il riduttore considera rumore tutto quello che sta sotto la soglia nf: un poco sopra il fondo misurato, così lo toglie bene senza toccare la voce
                Dim nf As Double = Math.Max(-80.0, Math.Min(-25.0, fondo + 5.0))
                r = PassaFfmpeg(x, frequenza, "highpass=f=70,afftdn=nr=" & imp.RumoreDb.ToString(CultureInfo.InvariantCulture) & ":nf=" & nf.ToString("0.0", CultureInfo.InvariantCulture) & ":rf=-80:tn=0", False, 25.0)
            End If
            If r IsNot Nothing Then
                x = r
            Else
                RiduciRumore(x, frequenza)
            End If
        End If
        If imp.Intonazione <> "nessuna" OrElse imp.Semitoni <> 0 Then
            Try
                Dim fatto As Boolean = False
                ' il tono scelto si dà con rubberband (le vocali non cambiano timbro) dopo l'eventuale correzione dell'intonazione; senza, lo dà il correttore
                If imp.Semitoni <> 0 AndAlso conFfmpeg AndAlso RubberbandPronto() Then
                    Dim corretto As Single() = x
                    If imp.Intonazione <> "nessuna" Then corretto = CorrettoreAltezza.Elabora(x, frequenza, imp.ConSemitoni(0))
                    Dim rapporto As Double = Math.Pow(2.0, imp.Semitoni / 12.0)
                    Dim r As Single() = PassaFfmpeg(corretto, frequenza, "rubberband=pitch=" & rapporto.ToString("0.#####", CultureInfo.InvariantCulture) & ":formant=preserved:pitchq=quality", False, 0.0)
                    If r IsNot Nothing Then
                        x = r
                        fatto = True
                    End If
                End If
                If Not fatto Then x = CorrettoreAltezza.Elabora(x, frequenza, imp)
            Catch ex As Exception
                _log.ScriviLog("EffettiVoce.Elabora (altezza): " & ex.Message & Environment.NewLine & ex.StackTrace)
            End Try
        End If
        If imp.Chiarezza Then x = RendiChiara(x, frequenza)
        If imp.Compressore Then
            Dim r As Single() = Nothing
            If conFfmpeg Then r = PassaFfmpeg(x, frequenza, "acompressor=threshold=0.1:ratio=3.5:attack=8:release=120:makeup=2:knee=3,alimiter=limit=0.95:level=0", False, 5.0)
            If r IsNot Nothing Then
                x = r
            Else
                Comprimi(x, frequenza)
            End If
        End If
        If imp.Livella Then Livella(x, frequenza)
        If imp.Riverbero <> "nessuno" Then x = AggiungiRiverbero(x, frequenza, imp.Riverbero)
        If imp.Eco Then
            Dim r As Single() = Nothing
            If conFfmpeg Then r = PassaFfmpeg(x, frequenza, "apad=pad_dur=0.5,aecho=0.8:1.25:250|500|750|1000:0.4|0.28|0.18|0.1", True, 0.0)
            If r IsNot Nothing Then
                x = r
            Else
                x = AggiungiEco(x, frequenza)
            End If
        End If
        ' un limite dolce, perché nessun effetto mandi la voce oltre il massimo
        For i As Integer = 0 To x.Length - 1
            Dim v As Single = x(i)
            If Math.Abs(v) > 0.95F Then x(i) = CSng(Math.Tanh(v))
        Next
        Return x
    End Function

    ''' <summary>Il livello del fruscio di fondo (in decibel sotto il massimo): la media dei pezzi di venti millisecondi più silenziosi, il decimo più basso.</summary>
    Private Shared Function StimaFondoDb(x() As Single, frequenza As Integer) As Double
        Dim blocco As Integer = Math.Max(1, frequenza \ 50)
        Dim livelli As New List(Of Double)()
        For i As Integer = 0 To x.Length - blocco Step blocco
            Dim s As Double = 0.0
            For j As Integer = 0 To blocco - 1
                s += x(i + j) * x(i + j)
            Next
            livelli.Add(Math.Sqrt(s / blocco))
        Next
        If livelli.Count = 0 Then Return -50.0
        livelli.Sort()
        Dim n As Integer = Math.Max(1, livelli.Count \ 10)
        Dim somma As Double = 0.0
        For i As Integer = 0 To n - 1
            somma += livelli(i)
        Next
        Return 20.0 * Math.Log10(Math.Max(somma / n, 0.000001))
    End Function

    ' ---- ffmpeg -------------------------------------------------------------------------------------------

    Private Shared _filtriControllati As Integer = -1
    Private Shared _conRubberband As Boolean

    ''' <summary>Vero se ffmpeg ha anche il filtro rubberband (cambio d'altezza di qualità, che conserva il timbro delle vocali).</summary>
    Public Shared Function RubberbandPronto() As Boolean
        FfmpegPronto()
        Return _conRubberband
    End Function

    ''' <summary>Vero se c'è ffmpeg con i filtri che servono (afftdn, acompressor, alimiter, aecho, apad, highpass).</summary>
    Public Shared Function FfmpegPronto() As Boolean
        If _filtriControllati >= 0 Then Return _filtriControllati = 1
        Dim ok As Boolean = False
        Try
            If FfmpegUtil.Percorso() <> "" Then
                Dim codice As Integer
                Dim elenco As String = ""
                FfmpegUtil.EseguiConUscita("-hide_banner -filters", codice, elenco, 30000)
                ok = codice = 0
                _conRubberband = ok AndAlso elenco.Contains(" rubberband ")
                For Each f As String In New String() {" afftdn ", " acompressor ", " alimiter ", " aecho ", " apad ", " highpass "}
                    If Not elenco.Contains(f) Then ok = False
                Next
            End If
        Catch
            ok = False
        End Try
        _filtriControllati = If(ok, 1, 0)
        Return ok
    End Function

    ''' <summary>
    ''' Passa la traccia da una catena di filtri di ffmpeg (su file temporanei, in virgola mobile a 32 bit, un canale). Alcuni filtri (il riduttore
    ''' di rumore a FFT, il limitatore) ritardano il suono di qualche millisecondo: per non spostare la voce davanti alla traccia si mette un colpo
    ''' di prova, di cui si misura il ritardo vero all'uscita, e si toglie quel ritardo. Se il colpo non si ritrova si usa il ritardo previsto
    ''' (ritardoPrevistoMs). Restituisce la traccia nuova, della stessa lunghezza (o più lunga con allunga, per la coda dell'eco); Nothing se
    ''' qualcosa non va.
    ''' </summary>
    Private Shared Function PassaFfmpeg(x() As Single, frequenza As Integer, catena As String, allunga As Boolean, ritardoPrevistoMs As Double) As Single()
        Dim ingresso As String = Path.Combine(Path.GetTempPath(), "wg-fx-" & Guid.NewGuid().ToString("N") & "-in.wav")
        Dim uscita As String = Path.Combine(Path.GetTempPath(), "wg-fx-" & Guid.NewGuid().ToString("N") & "-out.wav")
        Try
            Dim conProva As Boolean = ritardoPrevistoMs > 0.0
            Dim prova As Integer = If(conProva, CInt(0.12 * frequenza), 0)
            Dim coda As Integer = If(conProva, CInt(0.2 * frequenza), 0)
            Dim buf(prova + x.Length + coda - 1) As Single
            Array.Copy(x, 0, buf, prova, x.Length)
            Dim posizioneColpo As Integer = 0
            If conProva Then
                Dim inizioColpo As Integer = CInt(0.04 * frequenza)
                Dim durata As Integer = CInt(0.003 * frequenza)
                Dim massimo As Single = 0.0F
                For i As Integer = 0 To durata - 1
                    buf(inizioColpo + i) = CSng(0.5 * Math.Sin(2.0 * Math.PI * 1000.0 * i / frequenza) * Math.Sin(Math.PI * i / durata))
                    If Math.Abs(buf(inizioColpo + i)) > massimo Then
                        massimo = Math.Abs(buf(inizioColpo + i))
                        posizioneColpo = inizioColpo + i
                    End If
                Next
            End If
            Using w As New WaveFileWriter(ingresso, WaveFormat.CreateIeeeFloatWaveFormat(frequenza, 1))
                w.WriteSamples(buf, 0, buf.Length)
            End Using
            Dim codice As Integer
            Dim messaggio As String = FfmpegUtil.Esegui("-y -hide_banner -loglevel error -i " & FfmpegUtil.Virgolette(ingresso) & " -af " & FfmpegUtil.Virgolette(catena) & " -c:a pcm_f32le " & FfmpegUtil.Virgolette(uscita), codice, 600000)
            If codice <> 0 OrElse Not File.Exists(uscita) Then
                _log.ScriviLog("EffettiVoce.PassaFfmpeg: " & catena & ": " & messaggio)
                Return Nothing
            End If
            Dim letti As New List(Of Single)(buf.Length)
            Using r As New AudioFileReader(uscita)
                Dim b(4095) As Single
                Dim n As Integer
                Do
                    n = r.Read(b, 0, b.Length)
                    For i As Integer = 0 To n - 1
                        letti.Add(b(i))
                    Next
                Loop While n > 0
            End Using
            If letti.Count < x.Length \ 2 Then Return Nothing
            Dim ritardo As Integer = 0
            If conProva Then
                ritardo = CInt(Math.Round(ritardoPrevistoMs * frequenza / 1000.0))
                Dim massimo As Single = 0.0F
                Dim posizione As Integer = -1
                For i As Integer = Math.Max(0, posizioneColpo - CInt(0.005 * frequenza)) To Math.Min(letti.Count - 1, posizioneColpo + CInt(0.08 * frequenza))
                    If Math.Abs(letti(i)) > massimo Then
                        massimo = Math.Abs(letti(i))
                        posizione = i
                    End If
                Next
                If posizione >= 0 AndAlso massimo > 0.05F Then ritardo = posizione - posizioneColpo
            End If
            Dim lunghezza As Integer = If(allunga, letti.Count - prova - ritardo, x.Length)
            If lunghezza < 1 Then Return Nothing
            Dim r2(lunghezza - 1) As Single
            For i As Integer = 0 To lunghezza - 1
                Dim s As Integer = prova + ritardo + i
                If s >= 0 AndAlso s < letti.Count Then r2(i) = letti(s)
            Next
            Return r2
        Catch ex As Exception
            _log.ScriviLog("EffettiVoce.PassaFfmpeg: " & ex.Message)
            Return Nothing
        Finally
            Try
                If File.Exists(ingresso) Then File.Delete(ingresso)
                If File.Exists(uscita) Then File.Delete(uscita)
            Catch
            End Try
        End Try
    End Function

    ' ---- riduzione del rumore ----------------------------------------------------------------------------

    ''' <summary>Abbassa i tratti in cui c'è solo il rumore di fondo: lo si stima dalla parte più silenziosa della registrazione.</summary>
    Public Shared Sub RiduciRumore(x() As Single, frequenza As Integer)
        Dim passo As Integer = Math.Max(1, frequenza \ 100)
        Dim quadri As Integer = x.Length \ passo + 1
        Dim rms(quadri - 1) As Single
        For f As Integer = 0 To quadri - 1
            Dim somma As Double = 0.0
            Dim n As Integer = 0
            For i As Integer = f * passo To Math.Min(x.Length, (f + 1) * passo) - 1
                somma += x(i) * x(i)
                n += 1
            Next
            rms(f) = If(n > 0, CSng(Math.Sqrt(somma / n)), 0.0F)
        Next
        Dim ordinati(quadri - 1) As Single
        Array.Copy(rms, ordinati, quadri)
        Array.Sort(ordinati)
        Dim fondo As Single = Math.Max(0.0001F, ordinati(quadri \ 10))
        Dim soglia As Single = fondo * 3.0F
        Dim attacco As Double = Math.Exp(-1.0 / (0.003 * frequenza))
        Dim rilascio As Double = Math.Exp(-1.0 / (0.15 * frequenza))
        Dim guadagno As Double = 1.0
        For i As Integer = 0 To x.Length - 1
            Dim f As Integer = i \ passo
            Dim massimo As Single = rms(f)
            If f + 1 < quadri Then massimo = Math.Max(massimo, rms(f + 1))
            If f > 0 Then massimo = Math.Max(massimo, rms(f - 1))
            Dim obiettivo As Double = If(massimo > soglia, 1.0, 0.12)
            If obiettivo > guadagno Then
                guadagno = attacco * guadagno + (1.0 - attacco) * obiettivo
            Else
                guadagno = rilascio * guadagno + (1.0 - rilascio) * obiettivo
            End If
            x(i) = CSng(x(i) * guadagno)
        Next
    End Sub

    ' ---- compressore -------------------------------------------------------------------------------------

    Public Shared Sub Comprimi(x() As Single, frequenza As Integer)
        Dim attacco As Double = Math.Exp(-1.0 / (0.001 * frequenza))
        Dim rilascio As Double = Math.Exp(-1.0 / (0.12 * frequenza))
        Const soglia As Double = 0.12
        Const rapporto As Double = 3.5
        Const recupero As Double = 1.8
        Dim inviluppo As Double = 0.0
        For i As Integer = 0 To x.Length - 1
            Dim a As Double = Math.Abs(x(i))
            If a > inviluppo Then
                inviluppo = attacco * inviluppo + (1.0 - attacco) * a
            Else
                inviluppo = rilascio * inviluppo + (1.0 - rilascio) * a
            End If
            Dim g As Double = 1.0
            If inviluppo > soglia Then g = (soglia + (inviluppo - soglia) / rapporto) / inviluppo
            x(i) = CSng(x(i) * g * recupero)
        Next
    End Sub

    ' ---- voce chiara ---------------------------------------------------------------------------------------

    ''' <summary>Un filtro del secondo ordine (le formule del "cookbook" di Robert Bristow-Johnson), che si applica in sequenza ai campioni.</summary>
    Private NotInheritable Class Biquad
        Private ReadOnly _b0, _b1, _b2, _a1, _a2 As Double
        Private _z1, _z2 As Double

        Private Sub New(b0 As Double, b1 As Double, b2 As Double, a0 As Double, a1 As Double, a2 As Double)
            _b0 = b0 / a0
            _b1 = b1 / a0
            _b2 = b2 / a0
            _a1 = a1 / a0
            _a2 = a2 / a0
        End Sub

        Public Shared Function PassaAlto(fs As Integer, f As Double, q As Double) As Biquad
            Dim w As Double = 2.0 * Math.PI * f / fs
            Dim c As Double = Math.Cos(w)
            Dim al As Double = Math.Sin(w) / (2.0 * q)
            Return New Biquad((1.0 + c) / 2.0, -(1.0 + c), (1.0 + c) / 2.0, 1.0 + al, -2.0 * c, 1.0 - al)
        End Function

        Public Shared Function Picco(fs As Integer, f As Double, q As Double, db As Double) As Biquad
            Dim a As Double = Math.Pow(10.0, db / 40.0)
            Dim w As Double = 2.0 * Math.PI * f / fs
            Dim c As Double = Math.Cos(w)
            Dim al As Double = Math.Sin(w) / (2.0 * q)
            Return New Biquad(1.0 + al * a, -2.0 * c, 1.0 - al * a, 1.0 + al / a, -2.0 * c, 1.0 - al / a)
        End Function

        Public Shared Function ScaffaleAlto(fs As Integer, f As Double, db As Double) As Biquad
            Dim a As Double = Math.Pow(10.0, db / 40.0)
            Dim w As Double = 2.0 * Math.PI * f / fs
            Dim c As Double = Math.Cos(w)
            Dim al As Double = Math.Sin(w) / 2.0 * Math.Sqrt(2.0)
            Dim r As Double = 2.0 * Math.Sqrt(a) * al
            Return New Biquad(a * ((a + 1.0) + (a - 1.0) * c + r), -2.0 * a * ((a - 1.0) + (a + 1.0) * c), a * ((a + 1.0) + (a - 1.0) * c - r),
                              (a + 1.0) - (a - 1.0) * c + r, 2.0 * ((a - 1.0) - (a + 1.0) * c), (a + 1.0) - (a - 1.0) * c - r)
        End Function

        Public Sub Applica(x() As Single)
            For i As Integer = 0 To x.Length - 1
                Dim v As Double = x(i)
                Dim u As Double = _b0 * v + _z1
                _z1 = _b1 * v - _a1 * u + _z2
                _z2 = _b2 * v - _a2 * u
                x(i) = CSng(u)
            Next
        End Sub
    End Class

    ''' <summary>
    ''' Voce più chiara: toglie i rumori sotto gli 80 Hz e un poco del rimbombo attorno ai 250 Hz, dà presenza alle consonanti attorno ai 3 kHz
    ''' e un po' d'aria sopra i 9 kHz. Sono ritocchi piccoli (da 2 a 3 decibel): la voce resta la stessa, ma si capisce meglio.
    ''' </summary>
    Public Shared Function RendiChiara(x() As Single, frequenza As Integer) As Single()
        Dim y(x.Length - 1) As Single
        Array.Copy(x, y, x.Length)
        Biquad.PassaAlto(frequenza, 80.0, 0.7071).Applica(y)
        Biquad.Picco(frequenza, 250.0, 1.0, -2.0).Applica(y)
        Biquad.Picco(frequenza, 3200.0, 0.9, 3.0).Applica(y)
        If frequenza >= 22050 Then Biquad.ScaffaleAlto(frequenza, 9000.0, 2.0).Applica(y)
        Return y
    End Function

    ' ---- volume uniforme -----------------------------------------------------------------------------------

    ''' <summary>
    ''' Porta la voce a un livello di -20 decibel (valore efficace dei tratti in cui si parla, non dei silenzi), senza superare il massimo. Il guadagno
    ''' è uno solo per tutta la traccia: serve a far sentire sempre la stessa voce con lo stesso volume, qualunque fosse la distanza dal microfono.
    ''' </summary>
    Public Shared Sub Livella(x() As Single, frequenza As Integer)
        Dim blocco As Integer = Math.Max(1, frequenza \ 50)
        Dim livelli As New List(Of Double)()
        For i As Integer = 0 To x.Length - blocco Step blocco
            Dim s As Double = 0.0
            For j As Integer = 0 To blocco - 1
                s += x(i + j) * x(i + j)
            Next
            livelli.Add(s / blocco)
        Next
        If livelli.Count = 0 Then Return
        Dim ordinati As New List(Of Double)(livelli)
        ordinati.Sort()
        Dim soglia As Double = ordinati(CInt(ordinati.Count * 0.9) Mod ordinati.Count) * 0.05
        Dim somma As Double = 0.0
        Dim n As Integer = 0
        For Each l As Double In livelli
            If l >= soglia AndAlso l > 0.0 Then
                somma += l
                n += 1
            End If
        Next
        If n = 0 Then Return
        Dim rms As Double = Math.Sqrt(somma / n)
        Dim picco As Double = 0.0
        For i As Integer = 0 To x.Length - 1
            picco = Math.Max(picco, Math.Abs(x(i)))
        Next
        If rms < 0.00001 OrElse picco < 0.00001 Then Return
        Dim guadagno As Double = Math.Min(0.1 / rms, 0.95 / picco)
        guadagno = Math.Max(0.25, Math.Min(12.0, guadagno))
        For i As Integer = 0 To x.Length - 1
            x(i) = CSng(x(i) * guadagno)
        Next
    End Sub

    ' ---- riverbero ---------------------------------------------------------------------------------------

    ''' <summary>
    ''' Il riverbero di Schroeder e Moorer nella versione "Freeverb": otto filtri a pettine in parallelo (ognuno con le alte frequenze smorzate) e
    ''' quattro filtri passatutto in serie. "stanza", "sala" e "grande" cambiano la durata della coda e quanto riverbero si sente.
    ''' </summary>
    Public Shared Function AggiungiRiverbero(x() As Single, frequenza As Integer, tipo As String) As Single()
        Dim stanza As Double = If(tipo = "grande", 0.93, If(tipo = "sala", 0.86, 0.74))
        Dim smorzo As Double = If(tipo = "grande", 0.3, If(tipo = "sala", 0.4, 0.5))
        Dim livello As Double = If(tipo = "grande", 0.42, If(tipo = "sala", 0.32, 0.22))
        Dim coda As Integer = CInt(frequenza * If(tipo = "grande", 2.6, If(tipo = "sala", 1.8, 1.0)))
        Dim ritardiPettine() As Integer = {1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617}
        Dim ritardiPassatutto() As Integer = {556, 441, 341, 225}
        Dim scala As Double = frequenza / 44100.0
        Dim n As Integer = x.Length + coda
        Dim ingresso(n - 1) As Single
        Array.Copy(x, ingresso, x.Length)
        Dim umido(n - 1) As Single
        For c As Integer = 0 To ritardiPettine.Length - 1
            Dim lunghezza As Integer = Math.Max(8, CInt(ritardiPettine(c) * scala))
            Dim buf(lunghezza - 1) As Double
            Dim indice As Integer = 0
            Dim filtro As Double = 0.0
            For i As Integer = 0 To n - 1
                Dim uscita As Double = buf(indice)
                filtro = uscita * (1.0 - smorzo) + filtro * smorzo
                buf(indice) = ingresso(i) * 0.015 + filtro * stanza
                indice += 1
                If indice >= lunghezza Then indice = 0
                umido(i) += CSng(uscita)
            Next
        Next
        For p As Integer = 0 To ritardiPassatutto.Length - 1
            Dim lunghezza As Integer = Math.Max(8, CInt(ritardiPassatutto(p) * scala))
            Dim buf(lunghezza - 1) As Double
            Dim indice As Integer = 0
            For i As Integer = 0 To n - 1
                Dim v As Double = umido(i)
                Dim dalBuffer As Double = buf(indice)
                umido(i) = CSng(-v + dalBuffer)
                buf(indice) = v + dalBuffer * 0.5
                indice += 1
                If indice >= lunghezza Then indice = 0
            Next
        Next
        ' l'umido esce molto più tenue dell'ingresso (il filtro riduce di molto l'ingresso): lo si porta a una frazione fissa del livello della voce
        Dim energiaSecca As Double = 0.0
        Dim energiaUmida As Double = 0.0
        For i As Integer = 0 To x.Length - 1
            energiaSecca += x(i) * x(i)
            energiaUmida += umido(i) * umido(i)
        Next
        Dim fattore As Double = If(energiaUmida > 0.0, livello * Math.Sqrt(energiaSecca / energiaUmida), 0.0)
        For i As Integer = 0 To n - 1
            ingresso(i) = CSng(ingresso(i) + umido(i) * fattore)
        Next
        Return ingresso
    End Function

    ' ---- eco ---------------------------------------------------------------------------------------------

    Public Shared Function AggiungiEco(x() As Single, frequenza As Integer) As Single()
        Dim ritardo As Integer = CInt(0.28 * frequenza)
        Dim coda As Integer = CInt(1.6 * frequenza)
        Dim y(x.Length + coda - 1) As Single
        Array.Copy(x, y, x.Length)
        For i As Integer = ritardo To y.Length - 1
            y(i) += 0.4F * y(i - ritardo)
        Next
        Return y
    End Function

End Class
