' ============================================================================================
' WGSintesi.Voci - le "voci" della sintesi degli strumenti musicali
' ============================================================================================
'
' Ogni strumento è fatto di voci: una voce per nota premuta. Le voci sono generate al calcolatore,
' campione per campione, a 48000 Hz con numeri a virgola mobile; non servono file di suoni.
'
'   VoceModale        pianoforti, campane, marimba, vibrafono: tante sinusoidi che si spengono ognuna con il suo tempo
'   VoceContinua      organi, flauti, clarinetto, fisarmonica, voci: armoniche tenute finché il tasto è premuto
'   VocePizzicata     chitarre, arpa, basso, clavicembalo: una corda simulata (Karplus-Strong esteso)
'   VoceSottrattiva   archi, ottoni, oboe, sassofono, sintetizzatori: onde ricche di armoniche e filtro
'   VoceFM            pianoforti elettrici, campanelli e bassi a modulazione di frequenza
'   VocePercussione   batteria e percussioni: toni che si spengono, rumore filtrato e colpo iniziale
' ============================================================================================
Imports System
Imports System.Collections.Generic

''' <summary>Costanti e funzioni di calcolo comuni a tutte le voci.</summary>
Public NotInheritable Class WGDsp

    Public Const FC As Double = 48000.0
    Public Const DuePi As Double = 6.283185307179586

    Private Sub New()
    End Sub

    Public Shared Function NotaInHz(nota As Double) As Double
        Return 440.0 * Math.Pow(2.0, (nota - 69.0) / 12.0)
    End Function

    Public Shared Function Limita(x As Double, minimo As Double, massimo As Double) As Double
        If x < minimo Then Return minimo
        If x > massimo Then Return massimo
        Return x
    End Function

    ''' <summary>Fattore da moltiplicare a ogni campione perché il livello scenda di 60 dB nel tempo indicato.</summary>
    Public Shared Function Decadimento(t60 As Double) As Double
        If t60 <= 0.0005 Then Return 0.0
        Return Math.Pow(0.001, 1.0 / (t60 * FC))
    End Function

    ''' <summary>Correzione che toglie il salto dagli spigoli delle onde a dente di sega e quadra.</summary>
    Public Shared Function PolyBlep(t As Double, dt As Double) As Double
        If t < dt Then
            Dim x As Double = t / dt
            Return x + x - x * x - 1.0
        ElseIf t > 1.0 - dt Then
            Dim x As Double = (t - 1.0) / dt
            Return x * x + x + x + 1.0
        End If
        Return 0.0
    End Function

    Public Shared Function CentoAMoltiplicatore(cent As Double) As Double
        Return Math.Pow(2.0, cent / 1200.0)
    End Function

End Class

''' <summary>Filtro a due poli (passa basso, passa banda, picco) con i coefficienti del manuale di Robert Bristow-Johnson.</summary>
Public NotInheritable Class FiltroBiquad

    Private _b0, _b1, _b2, _a1, _a2 As Double
    Private _x1, _x2, _y1, _y2 As Double

    Public Shared Function PassaBasso(freq As Double, q As Double) As FiltroBiquad
        Dim f As New FiltroBiquad()
        Dim w As Double = WGDsp.DuePi * WGDsp.Limita(freq, 10.0, 0.45 * WGDsp.FC) / WGDsp.FC
        Dim alfa As Double = Math.Sin(w) / (2.0 * q)
        Dim c As Double = Math.Cos(w)
        Dim a0 As Double = 1.0 + alfa
        f._b0 = (1.0 - c) / 2.0 / a0
        f._b1 = (1.0 - c) / a0
        f._b2 = f._b0
        f._a1 = -2.0 * c / a0
        f._a2 = (1.0 - alfa) / a0
        Return f
    End Function

    Public Shared Function PassaAlto(freq As Double, q As Double) As FiltroBiquad
        Dim f As New FiltroBiquad()
        Dim w As Double = WGDsp.DuePi * WGDsp.Limita(freq, 10.0, 0.45 * WGDsp.FC) / WGDsp.FC
        Dim alfa As Double = Math.Sin(w) / (2.0 * q)
        Dim c As Double = Math.Cos(w)
        Dim a0 As Double = 1.0 + alfa
        f._b0 = (1.0 + c) / 2.0 / a0
        f._b1 = -(1.0 + c) / a0
        f._b2 = f._b0
        f._a1 = -2.0 * c / a0
        f._a2 = (1.0 - alfa) / a0
        Return f
    End Function

    Public Shared Function PassaBanda(freq As Double, q As Double) As FiltroBiquad
        Dim f As New FiltroBiquad()
        Dim w As Double = WGDsp.DuePi * WGDsp.Limita(freq, 10.0, 0.45 * WGDsp.FC) / WGDsp.FC
        Dim alfa As Double = Math.Sin(w) / (2.0 * q)
        Dim c As Double = Math.Cos(w)
        Dim a0 As Double = 1.0 + alfa
        f._b0 = alfa / a0
        f._b1 = 0.0
        f._b2 = -alfa / a0
        f._a1 = -2.0 * c / a0
        f._a2 = (1.0 - alfa) / a0
        Return f
    End Function

    Public Shared Function Picco(freq As Double, q As Double, guadagnoDb As Double) As FiltroBiquad
        Dim f As New FiltroBiquad()
        Dim a As Double = Math.Pow(10.0, guadagnoDb / 40.0)
        Dim w As Double = WGDsp.DuePi * WGDsp.Limita(freq, 10.0, 0.45 * WGDsp.FC) / WGDsp.FC
        Dim alfa As Double = Math.Sin(w) / (2.0 * q)
        Dim c As Double = Math.Cos(w)
        Dim a0 As Double = 1.0 + alfa / a
        f._b0 = (1.0 + alfa * a) / a0
        f._b1 = -2.0 * c / a0
        f._b2 = (1.0 - alfa * a) / a0
        f._a1 = -2.0 * c / a0
        f._a2 = (1.0 - alfa / a) / a0
        Return f
    End Function

    Public Function Elabora(x As Double) As Double
        Dim y As Double = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2
        _x2 = _x1
        _x1 = x
        _y2 = _y1
        _y1 = y
        Return y
    End Function

End Class

''' <summary>Filtro a variabili di stato (passa basso, passa banda, passa alto), stabile anche quando la frequenza cambia di continuo.</summary>
Public NotInheritable Class FiltroSvf

    Private _ic1, _ic2 As Double
    Private _a1, _a2, _a3, _k As Double

    Public Sub New()
        Imposta(1000.0, 0.707)
    End Sub

    Public Sub Imposta(freq As Double, q As Double)
        Dim g As Double = Math.Tan(Math.PI * WGDsp.Limita(freq, 10.0, 0.45 * WGDsp.FC) / WGDsp.FC)
        _k = 1.0 / Math.Max(0.3, q)
        _a1 = 1.0 / (1.0 + g * (g + _k))
        _a2 = g * _a1
        _a3 = g * _a2
    End Sub

    ''' <summary>modo: 0 passa basso, 1 passa banda, 2 passa alto.</summary>
    Public Function Elabora(x As Double, modo As Integer) As Double
        Dim v3 As Double = x - _ic2
        Dim v1 As Double = _a1 * _ic1 + _a2 * v3
        Dim v2 As Double = _ic2 + _a2 * _ic1 + _a3 * v3
        _ic1 = 2.0 * v1 - _ic1
        _ic2 = 2.0 * v2 - _ic2
        Select Case modo
            Case 0 : Return v2
            Case 1 : Return v1
            Case Else : Return x - _k * v1 - v2
        End Select
    End Function

End Class

''' <summary>Una nota che suona. Somma il suo suono, campione per campione, nel buffer che riceve.</summary>
Public MustInherit Class VoceSintesi

    Private Shared _semeCasuale As UInteger = 2463534242UI

    Public Nota As Integer
    ''' <summary>Posizione fra gli altoparlanti, da -1 (sinistra) a 1 (destra).</summary>
    Public Pan As Single = 0.0F
    Public Livello As Single = 1.0F
    Public InRilascio As Boolean
    Public Finita As Boolean
    ''' <summary>Vero finché il pedale di sostegno tiene la nota dopo che il tasto è stato lasciato.</summary>
    Public TenutaDalPedale As Boolean
    ''' <summary>Vero dopo che il tasto è stato sollevato: il motore abbassa il suono in pochi millisecondi e poi spegne la voce.</summary>
    Public TaglioRapido As Boolean
    Public GuadagnoTaglio As Double = 1.0
    ''' <summary>Le voci dello stesso gruppo si fermano a vicenda (per esempio i due charleston); 0 nessun gruppo.</summary>
    Public GruppoDiSilenzio As Integer

    Private _rng As UInteger

    Protected Sub New()
        _semeCasuale = CUInt((CLng(_semeCasuale) * 1664525L + 1013904223L) And &HFFFFFFFFL)
        _rng = _semeCasuale Or 1UI
    End Sub

    ''' <summary>Rumore bianco fra -1 e 1.</summary>
    Protected Function Rumore() As Double
        _rng = _rng Xor (_rng << 13)
        _rng = _rng Xor (_rng >> 17)
        _rng = _rng Xor (_rng << 5)
        Return CDbl(_rng) / 2147483648.0 - 1.0
    End Function

    Public MustOverride Sub Genera(uscita() As Single, quanti As Integer)

    Public MustOverride Sub Rilascia()

    ''' <summary>Smorza in fretta la voce (per esempio quando si cambia strumento o si ferma tutto).</summary>
    Public MustOverride Sub Silenzia()

End Class

''' <summary>Somma di sinusoidi che si spengono ognuna con il proprio tempo: il suono di pianoforti, campane e strumenti a percussione intonati.</summary>
Public NotInheritable Class VoceModale
    Inherits VoceSintesi

    Private ReadOnly _provvisorie As New List(Of Double())()
    Private _x() As Double
    Private _y() As Double
    Private _c() As Double
    Private _s() As Double
    Private _n As Integer
    Private _gRilascio As Double = 1.0
    Private ReadOnly _fattoreRilascio As Double
    Private _fattoreSilenzio As Double = 0.0
    Private _eta As Long
    Private ReadOnly _etaMassima As Long
    Private _rumoreLivello As Double
    Private _rumoreDecadimento As Double
    Private _rumoreCoef As Double
    Private _rumoreStato As Double
    ''' <summary>Tremolo del vibrafono: la modulazione dell'ampiezza dovuta alle pale rotanti.</summary>
    Public TremoloHz As Double
    Public TremoloProfondita As Double
    Private _fasTrem As Double

    Public Sub New(t60Rilascio As Double, durataMassimaSec As Double)
        _fattoreRilascio = WGDsp.Decadimento(t60Rilascio)
        _etaMassima = CLng(durataMassimaSec * WGDsp.FC)
    End Sub

    Public Sub Aggiungi(freq As Double, ampiezza As Double, t60 As Double, Optional fase As Double = 0.0)
        If freq >= 0.46 * WGDsp.FC OrElse freq < 10.0 OrElse ampiezza < 0.0004 Then Return
        _provvisorie.Add(New Double() {freq, ampiezza, t60, fase})
    End Sub

    ''' <summary>Il colpo del martelletto: un soffio di rumore morbido che si spegne subito.</summary>
    Public Sub Colpo(livello As Double, durataSec As Double, tagliaHz As Double)
        _rumoreLivello = livello
        _rumoreDecadimento = WGDsp.Decadimento(durataSec)
        _rumoreCoef = 1.0 - Math.Exp(-WGDsp.DuePi * WGDsp.Limita(tagliaHz, 50.0, 12000.0) / WGDsp.FC)
    End Sub

    Public Sub Prepara()
        _n = _provvisorie.Count
        ReDim _x(_n)
        ReDim _y(_n)
        ReDim _c(_n)
        ReDim _s(_n)
        For j As Integer = 0 To _n - 1
            Dim p As Double() = _provvisorie(j)
            Dim w As Double = WGDsp.DuePi * p(0) / WGDsp.FC
            Dim r As Double = WGDsp.Decadimento(p(2))
            _c(j) = r * Math.Cos(w)
            _s(j) = r * Math.Sin(w)
            _x(j) = p(1) * Math.Cos(p(3))
            _y(j) = p(1) * Math.Sin(p(3))
        Next
        _provvisorie.Clear()
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        If _x Is Nothing Then Prepara()
        Dim n As Integer = _n
        For i As Integer = 0 To quanti - 1
            Dim somma As Double = 0.0
            For j As Integer = 0 To n - 1
                Dim xn As Double = _x(j) * _c(j) - _y(j) * _s(j)
                Dim yn As Double = _x(j) * _s(j) + _y(j) * _c(j)
                _x(j) = xn
                _y(j) = yn
                somma += yn
            Next
            If _rumoreLivello > 0.00001 Then
                _rumoreStato += (Rumore() * _rumoreLivello - _rumoreStato) * _rumoreCoef
                somma += _rumoreStato
                _rumoreLivello *= _rumoreDecadimento
            End If
            If InRilascio Then _gRilascio *= _fattoreRilascio
            If TremoloProfondita > 0.0 Then
                somma *= 1.0 - TremoloProfondita * (0.5 + 0.5 * Math.Sin(_fasTrem))
                _fasTrem += WGDsp.DuePi * TremoloHz / WGDsp.FC
            End If
            uscita(i) += CSng(somma * _gRilascio * Livello)
            _eta += 1
        Next
        If (InRilascio AndAlso _gRilascio < 0.0003) OrElse _eta > _etaMassima Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        InRilascio = True
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        If _gRilascio > 0.02 Then _gRilascio = 0.02
    End Sub

End Class

''' <summary>
''' Armoniche tenute finché il tasto è premuto: organi, flauti, clarinetto, fisarmonica, cori. Può avere vibrato, tremolo,
''' soffio di fiato, colpo iniziale e una armonica percussiva che si spegne (la "percussione" dell'organo elettrico).
''' </summary>
Public NotInheritable Class VoceContinua
    Inherits VoceSintesi

    Private ReadOnly _f0 As Double
    Public Rapporti() As Double
    Public Ampiezze() As Double
    ''' <summary>Se diverso da zero, un secondo strato leggermente stonato (cori, fisarmonica).</summary>
    Public StratoCent As Double
    Public VibratoHz As Double
    Public VibratoCent As Double
    Public VibratoRitardo As Double
    Public TremoloHz As Double
    Public TremoloProfondita As Double
    Public SoffioLivello As Double
    Public SoffioCentroHz As Double
    Public SoffioQ As Double = 1.5
    Public ColpoLivello As Double
    Public ColpoDurata As Double = 0.06
    Public Attacco As Double = 0.02
    Public RilascioSec As Double = 0.08
    Public PercussioneRapporto As Double
    Public PercussioneLivello As Double
    Public PercussioneT60 As Double = 0.25

    Private _fase As Double
    Private _fase2 As Double
    Private _fasLfo As Double
    Private _fasTrem As Double
    Private _env As Double
    Private _eta As Long
    Private ReadOnly _filtroSoffio As New FiltroSvf()
    Private _soffioPronto As Boolean
    Private _colpoEnv As Double = 1.0
    Private _percEnv As Double = 1.0
    Private _percDec As Double
    Private _colpoDec As Double
    Private _fattoreRilascio As Double
    Private _gSilenzio As Double = 1.0

    Public Sub New(f0 As Double)
        _f0 = f0
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        If Not _soffioPronto Then
            _soffioPronto = True
            If SoffioLivello > 0.0 Then _filtroSoffio.Imposta(If(SoffioCentroHz > 0.0, SoffioCentroHz, _f0 * 2.0), SoffioQ)
            _percDec = WGDsp.Decadimento(PercussioneT60)
            _colpoDec = WGDsp.Decadimento(ColpoDurata)
            _fattoreRilascio = WGDsp.Decadimento(RilascioSec)
        End If
        Dim n As Integer = Rapporti.Length
        Dim passoAttacco As Double = 1.0 / (Math.Max(0.002, Attacco) * WGDsp.FC)
        For i As Integer = 0 To quanti - 1
            Dim moltiplicatore As Double = 1.0
            If VibratoCent > 0.0 Then
                Dim ingresso As Double = WGDsp.Limita((CDbl(_eta) / WGDsp.FC - VibratoRitardo) / 0.35, 0.0, 1.0)
                moltiplicatore = 1.0 + VibratoCent * ingresso * Math.Sin(_fasLfo) * 0.000577623
                _fasLfo += WGDsp.DuePi * VibratoHz / WGDsp.FC
            End If
            Dim passo As Double = WGDsp.DuePi * _f0 * moltiplicatore / WGDsp.FC
            _fase += passo
            Dim somma As Double = 0.0
            For k As Integer = 0 To n - 1
                somma += Ampiezze(k) * Math.Sin(_fase * Rapporti(k))
            Next
            If StratoCent <> 0.0 Then
                _fase2 += passo * WGDsp.CentoAMoltiplicatore(StratoCent)
                Dim somma2 As Double = 0.0
                For k As Integer = 0 To n - 1
                    somma2 += Ampiezze(k) * Math.Sin(_fase2 * Rapporti(k))
                Next
                somma = (somma + somma2) * 0.5
            End If
            If PercussioneLivello > 0.0 AndAlso _percEnv > 0.0005 Then
                somma += PercussioneLivello * _percEnv * Math.Sin(_fase * PercussioneRapporto)
                _percEnv *= _percDec
            End If
            If TremoloProfondita > 0.0 Then
                somma *= 1.0 - TremoloProfondita * (0.5 + 0.5 * Math.Sin(_fasTrem))
                _fasTrem += WGDsp.DuePi * TremoloHz / WGDsp.FC
            End If
            If SoffioLivello > 0.0 Then
                Dim sof As Double = _filtroSoffio.Elabora(Rumore(), 1) * SoffioLivello
                If ColpoLivello > 0.0 AndAlso _colpoEnv > 0.001 Then
                    sof += _filtroSoffio.Elabora(Rumore(), 1) * ColpoLivello * _colpoEnv
                    _colpoEnv *= _colpoDec
                End If
                somma += sof
            End If
            If InRilascio Then
                _env *= _fattoreRilascio
            ElseIf _env < 1.0 Then
                _env = Math.Min(1.0, _env + passoAttacco)
            End If
            uscita(i) += CSng(somma * _env * _gSilenzio * Livello)
            _eta += 1
        Next
        If InRilascio AndAlso _env < 0.0003 Then Finita = True
        If _gSilenzio < 0.0003 Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        InRilascio = True
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _gSilenzio = 0.0002
    End Sub

End Class

''' <summary>Una corda pizzicata simulata con una linea di ritardo e un filtro (Karplus-Strong esteso), più la cassa dello strumento.</summary>
Public NotInheritable Class VocePizzicata
    Inherits VoceSintesi

    Private _buf() As Double
    Private ReadOnly _l As Integer
    Private _pos As Integer
    Private ReadOnly _coefAllpass As Double
    Private _apX1, _apY1 As Double
    Private _peso As Double
    Private _rho As Double
    Private ReadOnly _rhoRilascio As Double
    Private ReadOnly _filtri As New List(Of FiltroBiquad)()
    Private _eta As Long
    Private ReadOnly _etaMassima As Long
    Private _quieto As Integer
    Private _gSilenzio As Double = 1.0

    ''' <param name="brillantezza">0 suono scuro, 1 suono molto brillante.</param>
    ''' <param name="posizionePlettro">dove si pizzica la corda, da 0 a 0.5 (0.12 vicino al ponte, 0.5 al centro).</param>
    Public Sub New(f0 As Double, t60 As Double, brillantezza As Double, posizionePlettro As Double, livelloEccitazione As Double, t60Rilascio As Double, morbidezzaPlettro As Double)
        _peso = WGDsp.Limita(1.0 - brillantezza, 0.0, 1.0) * 0.9
        Dim n As Double = WGDsp.FC / f0
        Dim d As Double = n + 0.5 * _peso
        _l = Math.Max(3, CInt(Math.Floor(d - 0.5)))
        Dim f As Double = d - _l
        _coefAllpass = (1.0 - f) / (1.0 + f)
        ReDim _buf(_l)
        _rho = Math.Pow(0.001, 1.0 / (t60 * f0))
        _rhoRilascio = Math.Pow(0.001, 1.0 / (Math.Max(0.02, t60Rilascio) * f0))
        _etaMassima = CLng(t60 * 1.6 * WGDsp.FC)

        Dim eccitazione(_l - 1) As Double
        Dim lp As Double = 0.0
        Dim coef As Double = 1.0 - Math.Exp(-WGDsp.DuePi * WGDsp.Limita(morbidezzaPlettro, 300.0, 20000.0) / WGDsp.FC)
        For i As Integer = 0 To _l - 1
            lp += (Rumore() - lp) * coef
            eccitazione(i) = lp
        Next
        Dim pp As Integer = Math.Max(1, CInt(posizionePlettro * _l))
        Dim somma2 As Double = 0.0
        For i As Integer = 0 To _l - 1
            Dim v As Double = eccitazione(i)
            If i >= pp Then v -= eccitazione(i - pp)
            _buf(i) = v
            somma2 += v * v
        Next
        Dim rms As Double = Math.Sqrt(somma2 / _l)
        If rms > 0.000001 Then
            Dim scala As Double = livelloEccitazione / rms
            For i As Integer = 0 To _l - 1
                _buf(i) *= scala
            Next
        End If
    End Sub

    Public Sub AggiungiFiltro(filtro As FiltroBiquad)
        _filtri.Add(filtro)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        Dim l As Integer = _l
        For i As Integer = 0 To quanti - 1
            Dim a As Double = _buf(_pos)
            Dim posSuccessiva As Integer = _pos + 1
            If posSuccessiva >= l Then posSuccessiva = 0
            Dim b As Double = _buf(posSuccessiva)
            Dim y As Double = (a * (1.0 - _peso * 0.5) + b * (_peso * 0.5)) * _rho
            Dim z As Double = _coefAllpass * y + _apX1 - _coefAllpass * _apY1
            _apX1 = y
            _apY1 = z
            _buf(_pos) = z
            _pos = posSuccessiva
            Dim o As Double = z
            For Each f As FiltroBiquad In _filtri
                o = f.Elabora(o)
            Next
            uscita(i) += CSng(o * _gSilenzio * Livello)
            If Math.Abs(z) < 0.00002 Then
                _quieto += 1
            Else
                _quieto = 0
            End If
        Next
        _eta += quanti
        If _eta > _etaMassima OrElse _quieto > 4096 OrElse _gSilenzio < 0.0003 Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        If InRilascio Then Return
        InRilascio = True
        _rho = _rhoRilascio
        _peso = Math.Max(_peso, 0.6)
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _gSilenzio = 0.0002
    End Sub

End Class

''' <summary>Strumenti a suono continuo ricco di armoniche, con filtro che si apre e si chiude: archi, ottoni, ance, sintetizzatori.</summary>
Public NotInheritable Class VoceSottrattiva
    Inherits VoceSintesi

    ''' <summary>0 dente di sega, 1 quadra o rettangolare, 2 triangolare.</summary>
    Public Onda As Integer
    Public LarghezzaImpulso As Double = 0.5
    Public NumeroOscillatori As Integer = 1
    Public SpargimentoCent As Double = 0.0
    Public SottoOttavaLivello As Double
    Public TagliaBaseHz As Double = 2000.0
    Public TagliaInviluppoOttave As Double = 0.0
    Public TagliaTempoApertura As Double = 0.08
    Public TagliaSostegno As Double = 1.0
    Public TagliaVelocitaOttave As Double = 1.0
    Public Risonanza As Double = 0.8
    Public Attacco As Double = 0.03
    Public Decadimento As Double = 0.2
    Public Sostegno As Double = 1.0
    Public RilascioSec As Double = 0.12
    Public VibratoHz As Double = 5.5
    Public VibratoCent As Double = 0.0
    Public VibratoRitardo As Double = 0.3
    Public SoffioLivello As Double
    Public Formanti As New List(Of FiltroBiquad)()

    Private ReadOnly _f0 As Double
    Private ReadOnly _velocita As Double
    Private _fasi() As Double
    Private _moltiplicatori() As Double
    Private _fasiSub As Double
    Private _fasLfo As Double
    Private _stadio As Integer
    Private _env As Double
    Private _envF As Double = 1.0
    Private ReadOnly _filtro1 As New FiltroSvf()
    Private ReadOnly _filtro2 As New FiltroSvf()
    Private ReadOnly _filtroSoffio As New FiltroSvf()
    Private _eta As Long
    Private _pronto As Boolean
    Private _fattoreDecadimento As Double
    Private _fattoreRilascio As Double
    Private _fattoreApertura As Double
    Private _gSilenzio As Double = 1.0
    Private _stato As Double
    Private _passoAttacco As Double

    Public Sub New(f0 As Double, velocita As Double)
        _f0 = f0
        _velocita = velocita
    End Sub

    Private Sub Prepara()
        _pronto = True
        Dim n As Integer = Math.Max(1, NumeroOscillatori)
        ReDim _fasi(n - 1)
        ReDim _moltiplicatori(n - 1)
        For u As Integer = 0 To n - 1
            Dim spostamento As Double = 0.0
            If n > 1 Then spostamento = SpargimentoCent * (CDbl(u) / (n - 1) - 0.5) * 2.0
            _moltiplicatori(u) = WGDsp.CentoAMoltiplicatore(spostamento)
            _fasi(u) = If(u = 0, 0.0, 0.5 * (Rumore() + 1.0))
        Next
        _fattoreDecadimento = Math.Exp(-1.0 / (Math.Max(0.005, Decadimento) * 0.25 * WGDsp.FC))
        _fattoreRilascio = WGDsp.Decadimento(RilascioSec)
        _fattoreApertura = Math.Exp(-1.0 / (Math.Max(0.005, TagliaTempoApertura) * WGDsp.FC))
        _passoAttacco = 1.0 / (Math.Max(0.002, Attacco) * WGDsp.FC)
        If SoffioLivello > 0.0 Then _filtroSoffio.Imposta(Math.Min(9000.0, _f0 * 3.0), 1.2)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        If Not _pronto Then Prepara()
        Dim n As Integer = _fasi.Length
        Dim normalizzazione As Double = 1.0 / Math.Sqrt(n)
        For i As Integer = 0 To quanti - 1
            Dim moltiplicatore As Double = 1.0
            If VibratoCent > 0.0 Then
                Dim ingresso As Double = WGDsp.Limita((CDbl(_eta) / WGDsp.FC - VibratoRitardo) / 0.4, 0.0, 1.0)
                moltiplicatore = 1.0 + VibratoCent * ingresso * Math.Sin(_fasLfo) * 0.000577623
                _fasLfo += WGDsp.DuePi * VibratoHz / WGDsp.FC
            End If

            ' inviluppo dell'ampiezza
            Select Case _stadio
                Case 0
                    _env += _passoAttacco
                    If _env >= 1.0 Then
                        _env = 1.0
                        _stadio = 1
                    End If
                Case 1
                    _env = Sostegno + (_env - Sostegno) * _fattoreDecadimento
                    If Math.Abs(_env - Sostegno) < 0.001 Then _stadio = 2
                Case 3
                    _env *= _fattoreRilascio
            End Select
            _envF = TagliaSostegno + (_envF - TagliaSostegno) * _fattoreApertura

            ' il filtro si aggiorna ogni 8 campioni
            If (i And 7) = 0 Then
                Dim ottave As Double = TagliaInviluppoOttave * _envF + TagliaVelocitaOttave * (_velocita - 0.6)
                Dim taglia As Double = TagliaBaseHz * Math.Pow(2.0, ottave)
                taglia = WGDsp.Limita(taglia, 60.0, 0.42 * WGDsp.FC)
                _filtro1.Imposta(taglia, Risonanza)
                _filtro2.Imposta(taglia, 0.707)
            End If

            Dim somma As Double = 0.0
            For u As Integer = 0 To n - 1
                Dim dt As Double = _f0 * moltiplicatore * _moltiplicatori(u) / WGDsp.FC
                _fasi(u) += dt
                If _fasi(u) >= 1.0 Then _fasi(u) -= 1.0
                Dim t As Double = _fasi(u)
                Dim v As Double
                Select Case Onda
                    Case 0
                        v = 2.0 * t - 1.0 - WGDsp.PolyBlep(t, dt)
                    Case 1
                        Dim t2 As Double = t - LarghezzaImpulso
                        If t2 < 0.0 Then t2 += 1.0
                        v = If(t < LarghezzaImpulso, 1.0, -1.0) + WGDsp.PolyBlep(t, dt) - WGDsp.PolyBlep(t2, dt)
                    Case Else
                        v = If(t < 0.5, 4.0 * t - 1.0, 3.0 - 4.0 * t)
                End Select
                somma += v
            Next
            somma *= normalizzazione
            If SottoOttavaLivello > 0.0 Then
                _fasiSub += _f0 * 0.5 * moltiplicatore / WGDsp.FC
                If _fasiSub >= 1.0 Then _fasiSub -= 1.0
                somma += SottoOttavaLivello * If(_fasiSub < 0.5, 1.0, -1.0)
            End If
            If SoffioLivello > 0.0 Then somma += _filtroSoffio.Elabora(Rumore(), 1) * SoffioLivello

            Dim f1 As Double = _filtro1.Elabora(somma, 0)
            Dim f2 As Double = _filtro2.Elabora(f1, 0)
            For Each formante As FiltroBiquad In Formanti
                f2 = formante.Elabora(f2)
            Next
            uscita(i) += CSng(f2 * _env * _gSilenzio * Livello)
            _eta += 1
        Next
        If (_stadio = 3 AndAlso _env < 0.0003) OrElse _gSilenzio < 0.0003 Then Finita = True
        If _stadio = 2 AndAlso Sostegno < 0.001 Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        If InRilascio Then Return
        InRilascio = True
        _stadio = 3
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _stadio = 3
        _gSilenzio = 0.0002
    End Sub

End Class

''' <summary>Modulazione di frequenza a due modulatori: pianoforti elettrici, campanelli, bassi e suoni di sintesi.</summary>
Public NotInheritable Class VoceFM
    Inherits VoceSintesi

    Public RapportoPortante As Double = 1.0
    Public RapportoMod1 As Double = 1.0
    Public Indice1Inizio As Double = 2.0
    Public Indice1Fine As Double = 0.5
    Public Indice1T60 As Double = 1.0
    Public RapportoMod2 As Double = 14.0
    Public Indice2Inizio As Double = 0.0
    Public Indice2T60 As Double = 0.12
    Public Retroazione As Double = 0.0
    ''' <summary>Tempo di caduta dell'ampiezza (0 = si mantiene finché il tasto è premuto).</summary>
    Public AmpiezzaT60 As Double = 2.0
    Public Attacco As Double = 0.003
    Public RilascioSec As Double = 0.2
    Public TremoloHz As Double = 0.0
    Public TremoloProfondita As Double = 0.0

    Private ReadOnly _f0 As Double
    Private ReadOnly _velocita As Double
    Private _fp, _fm1, _fm2 As Double
    Private _ind1, _ind2 As Double
    Private _fattoreInd1, _fattoreInd2, _fattoreAmp, _fattoreRilascio, _passoAttacco As Double
    Private _env As Double
    Private _ampCaduta As Double = 1.0
    Private _pronto As Boolean
    Private _ultimo As Double
    Private _fasTrem As Double
    Private _gSilenzio As Double = 1.0
    Private _ind1Cadente As Double

    Public Sub New(f0 As Double, velocita As Double)
        _f0 = f0
        _velocita = velocita
    End Sub

    Private Sub Prepara()
        _pronto = True
        _ind1 = Indice1Inizio * (0.55 + 0.6 * _velocita)
        _ind1Cadente = Indice1Fine * (0.55 + 0.6 * _velocita)
        _ind2 = Indice2Inizio * (0.5 + 0.7 * _velocita)
        _fattoreInd1 = WGDsp.Decadimento(Math.Max(0.01, Indice1T60))
        _fattoreInd2 = WGDsp.Decadimento(Math.Max(0.01, Indice2T60))
        _fattoreAmp = If(AmpiezzaT60 > 0.0, WGDsp.Decadimento(AmpiezzaT60), 1.0)
        _fattoreRilascio = WGDsp.Decadimento(RilascioSec)
        _passoAttacco = 1.0 / (Math.Max(0.001, Attacco) * WGDsp.FC)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        If Not _pronto Then Prepara()
        Dim fcPortante As Double = WGDsp.DuePi * _f0 * RapportoPortante / WGDsp.FC
        Dim fcMod1 As Double = WGDsp.DuePi * _f0 * RapportoMod1 / WGDsp.FC
        Dim fcMod2 As Double = WGDsp.DuePi * _f0 * RapportoMod2 / WGDsp.FC
        For i As Integer = 0 To quanti - 1
            _fp += fcPortante
            _fm1 += fcMod1
            _fm2 += fcMod2
            ' la parte variabile dell'indice cala verso il valore finale
            Dim indice1 As Double = _ind1
            Dim mod1 As Double = Math.Sin(_fm1 + Retroazione * _ultimo) * indice1
            Dim mod2 As Double = Math.Sin(_fm2) * _ind2
            Dim y As Double = Math.Sin(_fp + mod1 + mod2)
            _ultimo = y
            _ind1 = _ind1Cadente + (_ind1 - _ind1Cadente) * _fattoreInd1
            _ind2 *= _fattoreInd2
            If AmpiezzaT60 > 0.0 Then _ampCaduta *= _fattoreAmp
            If InRilascio Then
                _env *= _fattoreRilascio
            ElseIf _env < 1.0 Then
                _env = Math.Min(1.0, _env + _passoAttacco)
            End If
            Dim trem As Double = 1.0
            If TremoloProfondita > 0.0 Then
                trem = 1.0 - TremoloProfondita * (0.5 + 0.5 * Math.Sin(_fasTrem))
                _fasTrem += WGDsp.DuePi * TremoloHz / WGDsp.FC
            End If
            uscita(i) += CSng(y * _env * _ampCaduta * trem * _gSilenzio * Livello)
        Next
        If (InRilascio AndAlso _env < 0.0003) OrElse _ampCaduta < 0.0003 OrElse _gSilenzio < 0.0003 Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        InRilascio = True
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _gSilenzio = 0.0002
    End Sub

End Class

''' <summary>Descrizione di un pezzo di batteria o percussione.</summary>
Public NotInheritable Class ProfiloPercussione

    Public Nome As String
    ''' <summary>Toni che si spengono: ogni riga è frequenza, ampiezza, t60.</summary>
    Public Toni As New List(Of Double())()
    ''' <summary>Tono con la frequenza che scende: inizio, fine, costante di tempo, ampiezza, t60.</summary>
    Public Spazzata As Double()
    ''' <summary>Rumore filtrato: modo (0 passa basso, 1 passa banda, 2 passa alto), frequenza, Q, ampiezza, t60, attacco.</summary>
    Public Rumore As Double()
    ''' <summary>Colpo iniziale: ampiezza e durata in secondi.</summary>
    Public ColpoLivello As Double
    Public ColpoDurata As Double = 0.004
    Public Gruppo As Integer
    Public Livello As Double = 1.0
    Public Pan As Single

    Public Function Tono(freq As Double, ampiezza As Double, t60 As Double) As ProfiloPercussione
        Toni.Add(New Double() {freq, ampiezza, t60})
        Return Me
    End Function

End Class

''' <summary>Un colpo di batteria o di percussione, costruito da toni, rumore e colpo iniziale.</summary>
Public NotInheritable Class VocePercussione
    Inherits VoceSintesi

    Private ReadOnly _p As ProfiloPercussione
    Private ReadOnly _velocita As Double
    Private _x() As Double
    Private _y() As Double
    Private _c() As Double
    Private _s() As Double
    Private _n As Integer
    Private _faseSpazzata As Double
    Private _tempoSpazzata As Double
    Private ReadOnly _filtro As New FiltroSvf()
    Private _envRumore As Double
    Private _fattoreRumore As Double
    Private _passoAttaccoRumore As Double
    Private _attaccoRumore As Double = 1.0
    Private _envSpazzata As Double = 1.0
    Private _fattoreSpazzata As Double
    Private _colpo As Double
    Private _fattoreColpo As Double
    Private _eta As Long
    Private _etaMassima As Long
    Private _gSilenzio As Double = 1.0
    Private _lpColpo As Double

    Public Sub New(profilo As ProfiloPercussione, velocita As Double)
        _p = profilo
        _velocita = velocita
        Pan = profilo.Pan
        GruppoDiSilenzio = profilo.Gruppo
        Dim t60Massimo As Double = 0.05
        _n = profilo.Toni.Count
        ReDim _x(_n)
        ReDim _y(_n)
        ReDim _c(_n)
        ReDim _s(_n)
        For j As Integer = 0 To _n - 1
            Dim t As Double() = profilo.Toni(j)
            Dim w As Double = WGDsp.DuePi * t(0) / WGDsp.FC
            Dim r As Double = WGDsp.Decadimento(t(2))
            _c(j) = r * Math.Cos(w)
            _s(j) = r * Math.Sin(w)
            _x(j) = t(1) * velocita
            _y(j) = 0.0
            t60Massimo = Math.Max(t60Massimo, t(2))
        Next
        If profilo.Spazzata IsNot Nothing Then
            _fattoreSpazzata = WGDsp.Decadimento(profilo.Spazzata(4))
            t60Massimo = Math.Max(t60Massimo, profilo.Spazzata(4))
        End If
        If profilo.Rumore IsNot Nothing Then
            _filtro.Imposta(profilo.Rumore(1), profilo.Rumore(2))
            _envRumore = profilo.Rumore(3) * velocita
            _fattoreRumore = WGDsp.Decadimento(profilo.Rumore(4))
            If profilo.Rumore(5) > 0.0005 Then
                _attaccoRumore = 0.0
                _passoAttaccoRumore = 1.0 / (profilo.Rumore(5) * WGDsp.FC)
            End If
            t60Massimo = Math.Max(t60Massimo, profilo.Rumore(4))
        End If
        _colpo = profilo.ColpoLivello * velocita
        _fattoreColpo = WGDsp.Decadimento(profilo.ColpoDurata)
        _etaMassima = CLng((t60Massimo * 1.3 + 0.1) * WGDsp.FC)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        For i As Integer = 0 To quanti - 1
            Dim somma As Double = 0.0
            For j As Integer = 0 To _n - 1
                Dim xn As Double = _x(j) * _c(j) - _y(j) * _s(j)
                Dim yn As Double = _x(j) * _s(j) + _y(j) * _c(j)
                _x(j) = xn
                _y(j) = yn
                somma += yn
            Next
            If _p.Spazzata IsNot Nothing AndAlso _envSpazzata > 0.0003 Then
                Dim freq As Double = _p.Spazzata(1) + (_p.Spazzata(0) - _p.Spazzata(1)) * Math.Exp(-_tempoSpazzata / _p.Spazzata(2))
                _faseSpazzata += WGDsp.DuePi * freq / WGDsp.FC
                _tempoSpazzata += 1.0 / WGDsp.FC
                somma += Math.Sin(_faseSpazzata) * _p.Spazzata(3) * _velocita * _envSpazzata
                _envSpazzata *= _fattoreSpazzata
            End If
            If _p.Rumore IsNot Nothing AndAlso _envRumore > 0.00005 Then
                If _attaccoRumore < 1.0 Then _attaccoRumore = Math.Min(1.0, _attaccoRumore + _passoAttaccoRumore)
                somma += _filtro.Elabora(Rumore(), CInt(_p.Rumore(0))) * _envRumore * _attaccoRumore
                _envRumore *= _fattoreRumore
            End If
            If _colpo > 0.0003 Then
                _lpColpo += (Rumore() - _lpColpo) * 0.5
                somma += _lpColpo * _colpo
                _colpo *= _fattoreColpo
            End If
            If InRilascio Then _gSilenzio *= 0.9993
            uscita(i) += CSng(somma * _gSilenzio * Livello * _p.Livello)
        Next
        _eta += quanti
        If _eta > _etaMassima OrElse _gSilenzio < 0.0003 Then Finita = True
    End Sub

    ''' <summary>Le percussioni suonano fino in fondo anche se il tasto viene lasciato; un gruppo (charleston) le fa tacere.</summary>
    Public Overrides Sub Rilascia()
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _gSilenzio = Math.Min(_gSilenzio, 0.02)
    End Sub

End Class
