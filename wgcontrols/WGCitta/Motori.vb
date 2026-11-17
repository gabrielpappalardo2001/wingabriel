Imports System

' ============================================================================================
' I motori di Porto Franco, fatti al momento (niente registrazioni): un piccolo modello di motore a quattro tempi.
'
'  - Il motore gira a un certo numero di giri al minuto; ogni cilindro accende una volta ogni due giri, quindi la frequenza delle
'    accensioni è giri / 120 per il numero dei cilindri. Ogni accensione è un impulso di scarico con tante armoniche: aprendo il gas
'    ne arrivano di più e il suono si fa più aspro.
'  - I cilindri non sono tutti uguali (ognuno spinge un po' diversamente, ogni tanto uno spinge male): da qui nasce il "brontolio"
'    irregolare di un motore vero, soprattutto al minimo. Al minimo i giri ballano piano piano.
'  - Il suono passa per tre risonanze fisse (scarico e carrozzeria): le armoniche, salendo di giri, le attraversano, ed è questo che
'    fa sembrare vero il motore invece di un tono che sale. In più il rumore dello scoppio ad ogni accensione e il fiato
'    dell'aspirazione, che con il gas aperto e i giri alti diventa un ruggito.
'  - Un cambio automatico decide la marcia dalla velocità: i giri salgono con la velocità, ai cambi di marcia calano di colpo e
'    risalgono; da fermi, col gas premuto, il motore sale di giri prima che la macchina parta. Lo scooter ha il variatore (i giri
'    salgono subito e restano lì), la barca ha la presa diretta.
' ============================================================================================

''' <summary>Le caratteristiche di un tipo di motore.</summary>
Friend NotInheritable Class ProfiloMotore
    Public Cilindri As Integer = 4
    ''' <summary>Giri al minuto al minimo e al massimo.</summary>
    Public Minimo As Double = 800
    Public Massimo As Double = 6500
    ''' <summary>Marce del cambio; 0 variatore (scooter); -1 presa diretta (barca).</summary>
    Public Marce As Integer = 5
    ''' <summary>Quanto i cilindri sono diversi tra loro (0 niente, 0.3 molto).</summary>
    Public Irregolarita As Double = 0.05
    ''' <summary>Probabilità che un'accensione manchi (motore rotto).</summary>
    Public Guasto As Double = 0
    ''' <summary>Sposta le risonanze: sotto 1 più cupo e grosso, sopra 1 più sottile.</summary>
    Public Scala As Double = 1
    ''' <summary>Quanto è secco lo scoppio di ogni accensione (il diesel più forte).</summary>
    Public Scoppio As Double = 0.5
    ''' <summary>Quanto è forte il respiro dell'aspirazione.</summary>
    Public Aspirazione As Double = 0.5
    ''' <summary>Quanto il suono si fa brillante con il gas aperto.</summary>
    Public Brillantezza As Double = 1

    Public Shared Function Per(nome As String) As ProfiloMotore
        Try
            Dim p As New ProfiloMotore()
            Select Case nome
                Case "motore_utilitaria"
                    p.Cilindri = 3 : p.Minimo = 900 : p.Massimo = 6200 : p.Marce = 5 : p.Irregolarita = 0.11 : p.Scala = 1.1 : p.Scoppio = 0.5 : p.Aspirazione = 0.45
                Case "motore_sportiva"
                    p.Cilindri = 8 : p.Minimo = 950 : p.Massimo = 8200 : p.Marce = 6 : p.Irregolarita = 0.09 : p.Scala = 0.9 : p.Scoppio = 0.7 : p.Aspirazione = 0.8 : p.Brillantezza = 1.35
                Case "motore_furgone"
                    p.Cilindri = 4 : p.Minimo = 780 : p.Massimo = 4300 : p.Marce = 6 : p.Irregolarita = 0.06 : p.Scala = 0.85 : p.Scoppio = 1.0 : p.Aspirazione = 0.35 : p.Brillantezza = 0.75
                Case "motore_fuoristrada"
                    p.Cilindri = 6 : p.Minimo = 750 : p.Massimo = 5200 : p.Marce = 5 : p.Irregolarita = 0.07 : p.Scala = 0.8 : p.Scoppio = 0.6 : p.Aspirazione = 0.6 : p.Brillantezza = 0.9
                Case "motore_camion"
                    p.Cilindri = 6 : p.Minimo = 600 : p.Massimo = 2400 : p.Marce = 8 : p.Irregolarita = 0.05 : p.Scala = 0.62 : p.Scoppio = 1.1 : p.Aspirazione = 0.4 : p.Brillantezza = 0.6
                Case "motore_moto"
                    p.Cilindri = 2 : p.Minimo = 1300 : p.Massimo = 11000 : p.Marce = 6 : p.Irregolarita = 0.2 : p.Scala = 1.5 : p.Scoppio = 0.6 : p.Aspirazione = 0.7 : p.Brillantezza = 1.5
                Case "motore_muscle"
                    p.Cilindri = 8 : p.Minimo = 700 : p.Massimo = 6000 : p.Marce = 5 : p.Irregolarita = 0.13 : p.Scala = 0.72 : p.Scoppio = 0.85 : p.Aspirazione = 0.9 : p.Brillantezza = 1.1
                Case "motore_motocarro"
                    p.Cilindri = 1 : p.Minimo = 1100 : p.Massimo = 4200 : p.Marce = 4 : p.Irregolarita = 0 : p.Scala = 1.2 : p.Scoppio = 0.9 : p.Aspirazione = 0.3 : p.Brillantezza = 0.8
                Case "motore_scooter"
                    p.Cilindri = 1 : p.Minimo = 1700 : p.Massimo = 8500 : p.Marce = 0 : p.Irregolarita = 0 : p.Scala = 1.7 : p.Scoppio = 0.5 : p.Aspirazione = 0.5 : p.Brillantezza = 1.2
                Case "motore_barca"
                    p.Cilindri = 4 : p.Minimo = 700 : p.Massimo = 5600 : p.Marce = -1 : p.Irregolarita = 0.06 : p.Scala = 0.8 : p.Scoppio = 0.8 : p.Aspirazione = 0.6 : p.Brillantezza = 0.8
                Case "motore_guasto"
                    p.Cilindri = 4 : p.Minimo = 800 : p.Massimo = 5000 : p.Marce = 5 : p.Irregolarita = 0.35 : p.Guasto = 0.22 : p.Scala = 1.0 : p.Scoppio = 0.9 : p.Aspirazione = 0.3 : p.Brillantezza = 0.7
                Case Else
                    ' la berlina
            End Select
            Return p
        Catch ex As Exception
            RegistroErrori.Registra("ProfiloMotore.Per", ex)
            Throw
        End Try
    End Function

    ''' <summary>La velocità, come frazione della massima, a cui il motore arriva ai giri massimi in quella marcia (da 1 a Marce).</summary>
    Public Function VelocitaAlMassimo(marcia As Integer) As Double
        Try
            If Marce <= 1 Then Return 1
            Return 0.25 + 1.0 * Math.Pow((marcia - 1) / CDbl(Marce - 1), 0.75)
        Catch ex As Exception
            RegistroErrori.Registra("ProfiloMotore.VelocitaAlMassimo", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Un filtro a due poli (variabile di stato) che lascia passare una banda di frequenze.</summary>
Friend NotInheritable Class FiltroBanda
    Private _a1, _a2, _a3, _k As Double
    Private _ic1, _ic2 As Double

    Public Sub New(freq As Double, q As Double)
        Try
            Imposta(freq, q)
        Catch ex As Exception
            RegistroErrori.Registra("FiltroBanda.New", ex)
            Throw
        End Try
    End Sub

    Public Sub Imposta(freq As Double, q As Double)
        Try
            Dim g As Double = Math.Tan(Math.PI * Math.Min(freq, 20000) / 44100.0)
            _k = 1.0 / q
            _a1 = 1.0 / (1.0 + g * (g + _k))
            _a2 = g * _a1
            _a3 = g * _a2
        Catch ex As Exception
            RegistroErrori.Registra("FiltroBanda.Imposta", ex)
            Throw
        End Try
    End Sub

    Public Function Passa(x As Double) As Double
        Dim v3 As Double = x - _ic2
        Dim v1 As Double = _a1 * _ic1 + _a2 * v3
        Dim v2 As Double = _ic2 + _a2 * _ic1 + _a3 * v3
        _ic1 = 2 * v1 - _ic1
        _ic2 = 2 * v2 - _ic2
        Return _k * v1
    End Function
End Class

''' <summary>
''' Il motore di un veicolo: si dice solo a che velocità va (Giri, da 0 a 1 rispetto alla massima), quanto gas si dà (Gas; se non si sa si
''' ricava da come cambia la velocità), quanto è in salita (Sforzo) e l'effetto Doppler; il resto, marce e giri compresi, lo fa lui.
''' Registrazione sceglie il tipo di motore (motore_berlina, motore_moto...): non è più una registrazione, solo il nome del tipo.
''' </summary>
Public NotInheritable Class VoceMotore
    Inherits VoceAudio

    Private Const MAXARM As Integer = 40
    Private Const PASSO As Integer = 128

    ''' <summary>La velocità come frazione della massima (0 fermo, 1 massima).</summary>
    Public Giri As Single = 0
    ''' <summary>Sposta un poco il tono (per distinguere due motori uguali).</summary>
    Public Timbro As Single = 1
    ''' <summary>0 piano, 1 forte salita: il motore fa più fatica.</summary>
    Public Sforzo As Single = 0
    Public Doppler As Single = 1
    ''' <summary>Il gas da 0 a 1; negativo se non si sa (si ricava dalla velocità).</summary>
    Public Gas As Single = -1

    Private _nome As String = ""
    Private _p As ProfiloMotore = ProfiloMotore.Per("")
    Private _marcia As Integer = 1
    Private _cambio As Double
    Private _daCambio As Double = 5
    Private _rpm As Double = 800
    Private _vel As Double
    Private _velRif As Double
    Private _tAcc As Double
    Private _accLiscia As Double
    Private _gasIn As Double
    Private _gas As Double
    Private _balla As Double
    Private _dop As Single = 1
    Private _fase As Double
    Private _meta As Boolean
    Private _cil As Integer
    Private _guadagno As Double = 1
    Private _guadagnoMeta As Double = 1
    Private _alfaGuadagno As Double = 0.1
    Private _contatore As Integer = PASSO
    Private _rng As UInteger
    Private _f As Double = 50
    Private _nArm As Integer = 8
    Private ReadOnly _ampArm(MAXARM) As Double
    Private ReadOnly _cosFi(MAXARM) As Double
    Private ReadOnly _sinFi(MAXARM) As Double
    Private ReadOnly _bias(15) As Double
    Private _ampTot As Double = 0.3
    Private _ampScoppio As Double
    Private _ampAspirazione As Double
    Private _ampFischio As Double
    Private _lpRumore As Double
    Private _faseF As Double
    Private ReadOnly _risonanze(2) As FiltroBanda
    Private ReadOnly _gRis() As Double = {0.9, 0.75, 0.5}
    Private ReadOnly _aspirazione(1) As FiltroBanda
    Private _dcIn As Double
    Private _dcOut As Double
    Private _uscita As Double

    Public Sub New()
        Try
            Dim semi As Integer = New Random().Next(1, Integer.MaxValue)
            _rng = CUInt(semi) Or 1UI
            For i As Integer = 1 To MAXARM
                _cosFi(i) = Math.Cos(0.9 * Math.Sin(1.7 * i + 0.4))
                _sinFi(i) = Math.Sin(0.9 * Math.Sin(1.7 * i + 0.4))
            Next
            Registrazione = ""
        Catch ex As Exception
            RegistroErrori.Registra("VoceMotore.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il tipo di motore (motore_berlina, motore_moto...); vuoto: la berlina.</summary>
    Public Property Registrazione As String
        Get
            Return _nome
        End Get
        Set(value As String)
            Try
                If value Is Nothing Then value = ""
                If value = _nome AndAlso _risonanze(0) IsNot Nothing Then Return
                _nome = value
                _p = ProfiloMotore.Per(value)
                Dim s As Double = _p.Scala
                Dim fr As Double() = {95 * s, 210 * s, 520 * s}
                Dim qr As Double() = {3.5, 3.0, 2.5}
                For i As Integer = 0 To 2
                    If _risonanze(i) Is Nothing Then
                        _risonanze(i) = New FiltroBanda(fr(i), qr(i))
                    Else
                        _risonanze(i).Imposta(fr(i), qr(i))
                    End If
                Next
                If _aspirazione(0) Is Nothing Then
                    _aspirazione(0) = New FiltroBanda(380 * s, 1.2)
                    _aspirazione(1) = New FiltroBanda(1250 * s, 1.0)
                Else
                    _aspirazione(0).Imposta(380 * s, 1.2)
                    _aspirazione(1).Imposta(1250 * s, 1.0)
                End If
                ' ogni cilindro spinge un po' diversamente; sempre lo stesso per lo stesso motore, così il brontolio ha un suo carattere
                Dim generatore As New Random(_p.Cilindri * 7919 + _p.Massimo.GetHashCode())
                For i As Integer = 0 To 15
                    _bias(i) = 1 + _p.Irregolarita * (generatore.NextDouble() * 2 - 1)
                Next
                _marcia = 1
                _cambio = 0
                _daCambio = 5
                _rpm = _p.Minimo
            Catch ex As Exception
                RegistroErrori.Registra("VoceMotore.Registrazione", ex)
                Throw
            End Try
        End Set
    End Property

    ''' <summary>Un numero a caso tra -1 e 1 (veloce, senza allocazioni).</summary>
    Private Function Caso() As Double
        _rng = _rng Xor (_rng << 13)
        _rng = _rng Xor (_rng >> 17)
        _rng = _rng Xor (_rng << 5)
        Return CDbl(_rng) / 2147483648.0 - 1.0
    End Function

    Private Shared Function Limita(x As Double, a As Double, b As Double) As Double
        If x < a Then Return a
        If x > b Then Return b
        Return x
    End Function

    ''' <summary>Ogni PASSO campioni: il cambio, i giri, e i valori con cui si costruisce il suono.</summary>
    Private Sub Controllo()
        Try
            Dim p As ProfiloMotore = _p
            Dim dtc As Double = PASSO / 44100.0
            Dim vel As Double = Limita(Giri, 0, 1.05)
            Dim sforzo As Double = Limita(Sforzo, 0, 1)
            _dop += (Doppler - _dop) * 0.05F

            ' il gas: dato da chi guida, oppure ricavato da come cambia la velocità
            Dim gasVolonta As Double
            If Gas >= 0 Then
                gasVolonta = Limita(Gas, 0, 1)
            Else
                _tAcc += dtc
                If _tAcc >= 0.1 Then
                    _accLiscia += ((vel - _velRif) / _tAcc - _accLiscia) * 0.5
                    _velRif = vel
                    _tAcc = 0
                End If
                gasVolonta = Limita(0.22 + 14 * _accLiscia, 0, 1)
            End If
            gasVolonta = Math.Max(gasVolonta, 0.6 * sforzo)
            _gasIn += (gasVolonta - _gasIn) * 0.12

            _vel += (vel - _vel) * 0.12
            Dim gasMarcia As Double = _gasIn
            _daCambio += dtc

            Dim rpmMarcia As Double = 0
            Dim rpmFermo As Double = 0
            Dim bersaglio As Double
            If p.Marce > 0 Then
                If _cambio > 0 Then
                    _cambio -= dtc
                ElseIf _daCambio > 0.7 Then
                    Dim rpmQui As Double = p.Massimo * _vel / p.VelocitaAlMassimo(_marcia)
                    If _marcia < p.Marce AndAlso rpmQui > p.Massimo * (0.4 + 0.52 * gasMarcia) Then
                        _marcia += 1
                        _cambio = 0.3
                        _daCambio = 0
                    ElseIf _marcia > 1 AndAlso rpmQui < p.Massimo * (0.18 + 0.1 * gasMarcia) Then
                        _marcia -= 1
                        _cambio = 0.18
                        _daCambio = 0
                    End If
                End If
                rpmMarcia = p.Massimo * _vel / p.VelocitaAlMassimo(_marcia)
                ' fermi o quasi, col gas premuto il motore sale di giri (la frizione slitta) prima che la macchina parta
                Dim primaMarcia As Double = p.VelocitaAlMassimo(1)
                rpmFermo = p.Minimo + (p.Massimo * 0.55 - p.Minimo) * gasMarcia * Math.Max(0, 1 - _vel / (primaMarcia * 0.4))
                bersaglio = Math.Max(p.Minimo, Math.Max(rpmMarcia, rpmFermo))
            ElseIf p.Marce = 0 Then
                ' variatore: i giri salgono subito e restano lì mentre la velocità cresce
                Dim rapporto As Double = If(gasMarcia > 0.12, 0.55 + 0.45 * _vel, 0.55 * _vel)
                bersaglio = p.Minimo + (p.Massimo - p.Minimo) * Math.Min(1.0, rapporto)
            Else
                ' presa diretta
                bersaglio = p.Minimo + (p.Massimo - p.Minimo) * Math.Min(1.0, 0.1 + 0.8 * _vel + 0.1 * gasMarcia)
            End If
            bersaglio = Math.Min(bersaglio, p.Massimo * 1.02)

            ' durante la cambiata si molla il gas
            Dim gasEff As Double = If(_cambio > 0, _gasIn * 0.08, _gasIn)
            _gas += (gasEff - _gas) * 0.12

            Dim tau As Double = If(_cambio > 0, 0.16, If(bersaglio > _rpm, 0.07, 0.14))
            _rpm += (bersaglio - _rpm) * (1 - Math.Exp(-dtc / tau))

            ' al minimo i giri ballano piano
            _balla += (Caso() - _balla) * 0.04
            Dim vicinoAlMinimo As Double = Limita(1 - (_rpm - p.Minimo) / (0.25 * (p.Massimo - p.Minimo)), 0, 1)
            Dim rpmSuono As Double = _rpm * (1 + 0.012 * _balla * (1 + 2 * vicinoAlMinimo))

            Dim R As Double = Limita((rpmSuono - p.Minimo) / (p.Massimo - p.Minimo), 0, 1)
            Dim L As Double = Limita(_gas, 0, 1)

            _f = rpmSuono / 120.0 * p.Cilindri * _dop * (0.85 + 0.15 * Timbro)
            _nArm = CInt(Math.Max(1, Math.Min(MAXARM, Math.Floor(7500 / Math.Max(10, _f)))))

            ' lo spettro dell'impulso di scarico: gas aperto e giri alti lo fanno più ricco e brillante
            Dim espo As Double = 1.3 - 0.6 * L - If(p.Scoppio > 0.9, 0.2, 0)
            Dim taglio As Double = (450 + 3600 * L * (0.35 + 0.65 * R) + 350 * R) * p.Brillantezza
            Dim somma As Double = 0
            For k As Integer = 1 To _nArm
                Dim x As Double = k * _f / taglio
                _ampArm(k) = Math.Pow(k, -espo) / (1 + x * x)
                somma += _ampArm(k)
            Next
            Dim norma As Double = 0.55 / Math.Max(0.5, somma)
            For k As Integer = 1 To _nArm
                _ampArm(k) *= norma
            Next

            _ampTot = (0.3 + 0.7 * L) * (0.55 + 0.45 * R)
            _ampScoppio = p.Scoppio * (0.25 + 0.75 * L) * 0.55
            _ampAspirazione = p.Aspirazione * L * L * (0.2 + 0.8 * R) * 0.5
            ' un sibilo di ingranaggi che sale con la velocità
            _ampFischio = If(p.Marce > 0, 0.012, 0.005) * _vel
            _alfaGuadagno = 1 - Math.Exp(-10 * _f / 44100.0)
        Catch ex As Exception
            RegistroErrori.Registra("VoceMotore.Controllo", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Function Campione() As Single
        Try
            If _contatore >= PASSO Then
                _contatore = 0
                Controllo()
            End If
            _contatore += 1
            Dim p As ProfiloMotore = _p

            ' la fase delle accensioni; a metà periodo si decide com'è la prossima (cilindro e mancata accensione)
            _fase += _f / 44100.0
            If _fase >= 1 Then
                _fase -= 1
                _meta = False
            End If
            If Not _meta AndAlso _fase >= 0.5 Then
                _meta = True
                _cil = (_cil + 1) Mod Math.Max(1, p.Cilindri)
                _guadagnoMeta = _bias(_cil And 15) * (1 + 0.05 * Caso())
                If p.Guasto > 0 AndAlso (Caso() * 0.5 + 0.5) < p.Guasto Then _guadagnoMeta = 0.1
            End If
            _guadagno += (_guadagnoMeta - _guadagno) * _alfaGuadagno

            ' l'impulso di scarico: somma di armoniche (ognuna sfasata un po', così l'impulso non è simmetrico)
            Dim ang As Double = 2 * Math.PI * _fase
            Dim c1 As Double = Math.Cos(ang)
            Dim s1 As Double = Math.Sin(ang)
            Dim cr As Double = 1
            Dim sr As Double = 0
            Dim impulso As Double = 0
            For k As Integer = 1 To _nArm
                Dim cn As Double = cr * c1 - sr * s1
                Dim sn As Double = sr * c1 + cr * s1
                cr = cn
                sr = sn
                impulso += _ampArm(k) * (cr * _cosFi(k) - sr * _sinFi(k))
            Next
            impulso *= _guadagno

            ' lo scoppio: un soffio di rumore a ogni accensione
            Dim n As Double = Caso()
            _lpRumore += (n - _lpRumore) * 0.12
            Dim scoppio As Double = _lpRumore * Math.Exp(-9 * _fase) * _ampScoppio * _guadagno * 4

            Dim grezzo As Double = impulso + scoppio
            Dim ris As Double = _risonanze(0).Passa(grezzo) * _gRis(0) + _risonanze(1).Passa(grezzo) * _gRis(1) + _risonanze(2).Passa(grezzo) * _gRis(2)
            Dim motore As Double = grezzo * 0.4 + ris * 1.1

            ' il fiato dell'aspirazione
            Dim aspira As Double = (_aspirazione(0).Passa(Caso()) * 0.8 + _aspirazione(1).Passa(Caso()) * 0.6) * _ampAspirazione

            ' il sibilo meccanico
            _faseF += (150 + 1500 * _vel) / 44100.0
            If _faseF >= 1 Then _faseF -= 1
            Dim fischio As Double = Math.Sin(2 * Math.PI * _faseF) * _ampFischio

            Dim x0 As Double = (motore + aspira) * _ampTot * 2.2 + fischio
            ' tolta la componente continua, poi un limitatore morbido
            _dcOut = x0 - _dcIn + 0.995 * _dcOut
            _dcIn = x0
            Dim y As Double = _dcOut
            _uscita = y / (1 + Math.Abs(y))
            Return CSng(_uscita * 1.5)
        Catch ex As Exception
            RegistroErrori.Registra("VoceMotore.Campione", ex)
            Throw
        End Try
    End Function
End Class
