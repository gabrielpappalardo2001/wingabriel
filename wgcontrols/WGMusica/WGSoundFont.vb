' ============================================================================================
' WGSoundFont - gli strumenti campionati, da un file SoundFont (.sf2)
' ============================================================================================
'
' Un file SoundFont contiene le registrazioni di strumenti veri (i "campioni") e le istruzioni per suonarle: quale
' campione per quale nota e quale forza, come cresce e si spegne il suono, quanto è filtrato. Questo file legge il
' formato SoundFont 2 e produce le voci per il motore audio.
'
' Si usano: zone di tasti e di forza, tonalità e accordatura fine, inizio e fine del campione e il ciclo di ripetizione,
' l'inviluppo del volume (ritardo, attacco, tenuta, calo, sostegno, rilascio), il filtro passa basso, il pan, l'attenuazione,
' il vibrato e le classi esclusive (per esempio il charleston). Non si usano i modulatori né l'inviluppo del filtro.
'
' Il file consigliato, GeneralUser GS (circa 32 megabyte, di libero uso), si scarica dall'ambiente degli strumenti.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class PresetSf2
    Public Nome As String
    Public Banca As Integer
    Public Programma As Integer
    Public Indice As Integer
End Class

Friend NotInheritable Class ZonaSf2
    Public TastoMin As Integer = 0
    Public TastoMax As Integer = 127
    Public VelMin As Integer = 0
    Public VelMax As Integer = 127
    Public Generatori() As Integer
    Public Campione As Integer
End Class

Friend NotInheritable Class IntestazioneCampione
    Public Nome As String
    Public Inizio As Integer
    Public Fine As Integer
    Public CicloInizio As Integer
    Public CicloFine As Integer
    Public Frequenza As Integer
    Public NotaOriginale As Integer
    Public CorrezioneCent As Integer
    Public Tipo As Integer
End Class

''' <summary>Un file SoundFont caricato in memoria.</summary>
Public NotInheritable Class BancoSoundFont

    Private Const GenInizio As Integer = 0
    Private Const GenFine As Integer = 1
    Private Const GenCicloInizio As Integer = 2
    Private Const GenCicloFine As Integer = 3
    Private Const GenInizioGrosso As Integer = 4
    Private Const GenVibLfoAltezza As Integer = 6
    Private Const GenModLfoAltezza As Integer = 5
    Private Const GenFiltroFc As Integer = 8
    Private Const GenFiltroQ As Integer = 9
    Private Const GenFineGrosso As Integer = 12
    Private Const GenPan As Integer = 17
    Private Const GenRitardoModLfo As Integer = 21
    Private Const GenFreqModLfo As Integer = 22
    Private Const GenRitardoVibLfo As Integer = 23
    Private Const GenFreqVibLfo As Integer = 24
    Private Const GenRitardoVol As Integer = 33
    Private Const GenAttaccoVol As Integer = 34
    Private Const GenTenutaVol As Integer = 35
    Private Const GenCaloVol As Integer = 36
    Private Const GenSostegnoVol As Integer = 37
    Private Const GenRilascioVol As Integer = 38
    Private Const GenTenutaTastoVol As Integer = 39
    Private Const GenCaloTastoVol As Integer = 40
    Private Const GenStrumento As Integer = 41
    Private Const GenZonaTasti As Integer = 43
    Private Const GenZonaForza As Integer = 44
    Private Const GenCicloInizioGrosso As Integer = 45
    Private Const GenAttenuazione As Integer = 48
    Private Const GenCicloFineGrosso As Integer = 50
    Private Const GenTonoGrosso As Integer = 51
    Private Const GenTonoFine As Integer = 52
    Private Const GenCampione As Integer = 53
    Private Const GenModoCampione As Integer = 54
    Private Const GenScalaTono As Integer = 56
    Private Const GenClasseEsclusiva As Integer = 57
    Private Const GenNotaRadice As Integer = 58
    Private Const NumeroGeneratori As Integer = 64

    Private Structure Bag
        Public IndiceGen As Integer
        Public IndiceMod As Integer
    End Structure

    Private Structure Gen
        Public Operazione As Integer
        Public Valore As Integer
        Public Basso As Integer
        Public Alto As Integer
    End Structure

    Private Structure IntestazionePreset
        Public Nome As String
        Public Programma As Integer
        Public Banca As Integer
        Public IndiceBag As Integer
    End Structure

    Private _campioni() As Short
    Private _intestazioniPreset As List(Of IntestazionePreset)
    Private _presetBag As List(Of Bag)
    Private _presetGen As List(Of Gen)
    Private _strumentoNome As List(Of String)
    Private _strumentoBag As List(Of Integer)
    Private _istBag As List(Of Bag)
    Private _istGen As List(Of Gen)
    Private _intestazioni As List(Of IntestazioneCampione)
    Private ReadOnly _zone As New Dictionary(Of Integer, List(Of ZonaSf2))()
    Private ReadOnly _bloccoZone As New Object()

    Public ReadOnly Property Percorso As String
    Public ReadOnly Property Preset As New List(Of PresetSf2)()

    Private Sub New(percorso As String)
        Me.Percorso = percorso
    End Sub

    ' ---- lettura del file ---------------------------------------------------------------------------

    Public Shared Function Carica(percorso As String) As BancoSoundFont
        Dim dati() As Byte = File.ReadAllBytes(percorso)
        Dim banco As New BancoSoundFont(percorso)
        banco.Analizza(dati)
        Return banco
    End Function

    Private Shared Function Intero16(d() As Byte, p As Integer) As Integer
        Return d(p) Or (CInt(d(p + 1)) << 8)
    End Function

    Private Shared Function Intero32(d() As Byte, p As Integer) As Integer
        Return d(p) Or (CInt(d(p + 1)) << 8) Or (CInt(d(p + 2)) << 16) Or (CInt(d(p + 3)) << 24)
    End Function

    Private Shared Function Testo(d() As Byte, p As Integer, n As Integer) As String
        Dim lunghezza As Integer = 0
        While lunghezza < n AndAlso d(p + lunghezza) <> 0
            lunghezza += 1
        End While
        Return Encoding.ASCII.GetString(d, p, lunghezza).Trim()
    End Function

    Private Sub Analizza(d() As Byte)
        If d.Length < 12 OrElse Encoding.ASCII.GetString(d, 0, 4) <> "RIFF" OrElse Encoding.ASCII.GetString(d, 8, 4) <> "sfbk" Then
            Throw New InvalidDataException("Non è un file SoundFont 2.")
        End If
        Dim posizione As Integer = 12
        While posizione + 8 <= d.Length
            Dim id As String = Encoding.ASCII.GetString(d, posizione, 4)
            Dim dimensione As Integer = Intero32(d, posizione + 4)
            Dim inizio As Integer = posizione + 8
            If id = "LIST" AndAlso inizio + 4 <= d.Length Then
                Dim tipo As String = Encoding.ASCII.GetString(d, inizio, 4)
                If tipo = "sdta" Then
                    LeggiCampioni(d, inizio + 4, dimensione - 4)
                ElseIf tipo = "pdta" Then
                    LeggiTabelle(d, inizio + 4, dimensione - 4)
                End If
            End If
            posizione = inizio + dimensione + (dimensione And 1)
        End While
        If _campioni Is Nothing OrElse _intestazioniPreset Is Nothing Then Throw New InvalidDataException("Il file SoundFont è incompleto.")
        For i As Integer = 0 To _intestazioniPreset.Count - 1
            Dim p As IntestazionePreset = _intestazioniPreset(i)
            Dim voce As New PresetSf2()
            voce.Nome = p.Nome
            voce.Banca = p.Banca
            voce.Programma = p.Programma
            voce.Indice = i
            Preset.Add(voce)
        Next
    End Sub

    Private Sub LeggiCampioni(d() As Byte, inizio As Integer, lunghezza As Integer)
        Dim posizione As Integer = inizio
        Dim fine As Integer = inizio + lunghezza
        While posizione + 8 <= fine
            Dim id As String = Encoding.ASCII.GetString(d, posizione, 4)
            Dim dimensione As Integer = Intero32(d, posizione + 4)
            If id = "smpl" Then
                ReDim _campioni(dimensione \ 2 - 1)
                System.Buffer.BlockCopy(d, posizione + 8, _campioni, 0, (dimensione \ 2) * 2)
            End If
            posizione += 8 + dimensione + (dimensione And 1)
        End While
    End Sub

    Private Sub LeggiTabelle(d() As Byte, inizio As Integer, lunghezza As Integer)
        _intestazioniPreset = New List(Of IntestazionePreset)()
        _presetBag = New List(Of Bag)()
        _presetGen = New List(Of Gen)()
        _strumentoNome = New List(Of String)()
        _strumentoBag = New List(Of Integer)()
        _istBag = New List(Of Bag)()
        _istGen = New List(Of Gen)()
        _intestazioni = New List(Of IntestazioneCampione)()
        Dim posizione As Integer = inizio
        Dim fine As Integer = inizio + lunghezza
        While posizione + 8 <= fine
            Dim id As String = Encoding.ASCII.GetString(d, posizione, 4)
            Dim dimensione As Integer = Intero32(d, posizione + 4)
            Dim p As Integer = posizione + 8
            Select Case id
                Case "phdr"
                    For i As Integer = 0 To dimensione \ 38 - 1
                        Dim q As Integer = p + i * 38
                        Dim h As New IntestazionePreset()
                        h.Nome = Testo(d, q, 20)
                        h.Programma = Intero16(d, q + 20)
                        h.Banca = Intero16(d, q + 22)
                        h.IndiceBag = Intero16(d, q + 24)
                        _intestazioniPreset.Add(h)
                    Next
                Case "pbag"
                    For i As Integer = 0 To dimensione \ 4 - 1
                        _presetBag.Add(New Bag With {.IndiceGen = Intero16(d, p + i * 4), .IndiceMod = Intero16(d, p + i * 4 + 2)})
                    Next
                Case "pgen"
                    For i As Integer = 0 To dimensione \ 4 - 1
                        _presetGen.Add(LeggiGen(d, p + i * 4))
                    Next
                Case "inst"
                    For i As Integer = 0 To dimensione \ 22 - 1
                        _strumentoNome.Add(Testo(d, p + i * 22, 20))
                        _strumentoBag.Add(Intero16(d, p + i * 22 + 20))
                    Next
                Case "ibag"
                    For i As Integer = 0 To dimensione \ 4 - 1
                        _istBag.Add(New Bag With {.IndiceGen = Intero16(d, p + i * 4), .IndiceMod = Intero16(d, p + i * 4 + 2)})
                    Next
                Case "igen"
                    For i As Integer = 0 To dimensione \ 4 - 1
                        _istGen.Add(LeggiGen(d, p + i * 4))
                    Next
                Case "shdr"
                    For i As Integer = 0 To dimensione \ 46 - 1
                        Dim q As Integer = p + i * 46
                        Dim h As New IntestazioneCampione()
                        h.Nome = Testo(d, q, 20)
                        h.Inizio = Intero32(d, q + 20)
                        h.Fine = Intero32(d, q + 24)
                        h.CicloInizio = Intero32(d, q + 28)
                        h.CicloFine = Intero32(d, q + 32)
                        h.Frequenza = Intero32(d, q + 36)
                        h.NotaOriginale = d(q + 40)
                        h.CorrezioneCent = CInt(CSByte(If(d(q + 41) > 127, d(q + 41) - 256, d(q + 41))))
                        h.Tipo = Intero16(d, q + 44)
                        _intestazioni.Add(h)
                    Next
            End Select
            posizione += 8 + dimensione + (dimensione And 1)
        End While
        ' l'ultimo elemento di preset, strumenti e campioni è il terminatore
        If _intestazioniPreset.Count > 0 Then _intestazioniPreset.RemoveAt(_intestazioniPreset.Count - 1)
    End Sub

    Private Shared Function LeggiGen(d() As Byte, p As Integer) As Gen
        Dim g As New Gen()
        g.Operazione = Intero16(d, p)
        g.Basso = d(p + 2)
        g.Alto = d(p + 3)
        Dim v As Integer = Intero16(d, p + 2)
        If v > 32767 Then v -= 65536
        g.Valore = v
        Return g
    End Function

    ' ---- le zone ------------------------------------------------------------------------------------

    Private Shared Function Predefiniti() As Integer()
        Dim g(NumeroGeneratori - 1) As Integer
        g(GenFiltroFc) = 13500
        g(GenRitardoModLfo) = -12000
        g(GenRitardoVibLfo) = -12000
        g(GenRitardoVol) = -12000
        g(GenAttaccoVol) = -12000
        g(GenTenutaVol) = -12000
        g(GenCaloVol) = -12000
        g(GenRilascioVol) = -12000
        g(23) = -12000
        g(25) = -12000
        g(26) = -12000
        g(27) = -12000
        g(28) = -12000
        g(30) = -12000
        g(GenScalaTono) = 100
        g(GenNotaRadice) = -1
        Return g
    End Function

    ''' <summary>Tutte le zone (preset, strumento, campione) di un preset, già sommate.</summary>
    Private Function ZoneDelPreset(indice As Integer) As List(Of ZonaSf2)
        SyncLock _bloccoZone
            Dim presenti As List(Of ZonaSf2) = Nothing
            If _zone.TryGetValue(indice, presenti) Then Return presenti
            Dim risultato As New List(Of ZonaSf2)()
            Try
                CostruisciZone(indice, risultato)
            Catch ex As Exception
                risultato.Clear()
            End Try
            _zone(indice) = risultato
            Return risultato
        End SyncLock
    End Function

    Private Shared Sub ApplicaGen(destinazione() As Integer, g As Gen, sommare As Boolean, ByRef tastoMin As Integer, ByRef tastoMax As Integer, ByRef velMin As Integer, ByRef velMax As Integer)
        Select Case g.Operazione
            Case GenZonaTasti
                tastoMin = Math.Max(tastoMin, g.Basso)
                tastoMax = Math.Min(tastoMax, g.Alto)
            Case GenZonaForza
                velMin = Math.Max(velMin, g.Basso)
                velMax = Math.Min(velMax, g.Alto)
            Case Else
                If g.Operazione >= 0 AndAlso g.Operazione < NumeroGeneratori Then
                    If sommare Then
                        destinazione(g.Operazione) += g.Valore
                    Else
                        destinazione(g.Operazione) = g.Valore
                    End If
                End If
        End Select
    End Sub

    Private Sub CostruisciZone(indicePreset As Integer, risultato As List(Of ZonaSf2))
        Dim p As IntestazionePreset = _intestazioniPreset(indicePreset)
        Dim primoBag As Integer = p.IndiceBag
        Dim ultimoBag As Integer = _intestazioniPreset(Math.Min(_intestazioniPreset.Count - 1, indicePreset + 1)).IndiceBag
        If indicePreset = _intestazioniPreset.Count - 1 Then ultimoBag = _presetBag.Count - 1

        Dim globalePreset(NumeroGeneratori - 1) As Integer
        Dim globaleTastoMin As Integer = 0
        Dim globaleTastoMax As Integer = 127
        Dim globaleVelMin As Integer = 0
        Dim globaleVelMax As Integer = 127
        For b As Integer = primoBag To ultimoBag - 1
            Dim da As Integer = _presetBag(b).IndiceGen
            Dim a As Integer = _presetBag(b + 1).IndiceGen
            Dim strumento As Integer = -1
            For g As Integer = da To a - 1
                If _presetGen(g).Operazione = GenStrumento Then strumento = _presetGen(g).Valore
            Next
            If strumento < 0 Then
                If b = primoBag Then
                    For g As Integer = da To a - 1
                        ApplicaGen(globalePreset, _presetGen(g), False, globaleTastoMin, globaleTastoMax, globaleVelMin, globaleVelMax)
                    Next
                End If
                Continue For
            End If
            Dim zonaPreset(NumeroGeneratori - 1) As Integer
            Array.Copy(globalePreset, zonaPreset, NumeroGeneratori)
            Dim pTastoMin As Integer = globaleTastoMin
            Dim pTastoMax As Integer = globaleTastoMax
            Dim pVelMin As Integer = globaleVelMin
            Dim pVelMax As Integer = globaleVelMax
            For g As Integer = da To a - 1
                If _presetGen(g).Operazione <> GenStrumento Then ApplicaGen(zonaPreset, _presetGen(g), False, pTastoMin, pTastoMax, pVelMin, pVelMax)
            Next
            CostruisciZoneStrumento(strumento, zonaPreset, pTastoMin, pTastoMax, pVelMin, pVelMax, risultato)
        Next
    End Sub

    Private Sub CostruisciZoneStrumento(strumento As Integer, zonaPreset() As Integer, pTastoMin As Integer, pTastoMax As Integer, pVelMin As Integer, pVelMax As Integer, risultato As List(Of ZonaSf2))
        If strumento < 0 OrElse strumento >= _strumentoBag.Count - 1 Then Return
        Dim primoBag As Integer = _strumentoBag(strumento)
        Dim ultimoBag As Integer = _strumentoBag(strumento + 1)
        Dim globale() As Integer = Predefiniti()
        Dim gTastoMin As Integer = 0
        Dim gTastoMax As Integer = 127
        Dim gVelMin As Integer = 0
        Dim gVelMax As Integer = 127
        For b As Integer = primoBag To ultimoBag - 1
            Dim da As Integer = _istBag(b).IndiceGen
            Dim a As Integer = _istBag(b + 1).IndiceGen
            Dim campione As Integer = -1
            For g As Integer = da To a - 1
                If _istGen(g).Operazione = GenCampione Then campione = _istGen(g).Valore
            Next
            If campione < 0 Then
                If b = primoBag Then
                    For g As Integer = da To a - 1
                        ApplicaGen(globale, _istGen(g), False, gTastoMin, gTastoMax, gVelMin, gVelMax)
                    Next
                End If
                Continue For
            End If
            If campione >= _intestazioni.Count - 1 Then Continue For
            Dim gen(NumeroGeneratori - 1) As Integer
            Array.Copy(globale, gen, NumeroGeneratori)
            Dim tastoMin As Integer = gTastoMin
            Dim tastoMax As Integer = gTastoMax
            Dim velMin As Integer = gVelMin
            Dim velMax As Integer = gVelMax
            For g As Integer = da To a - 1
                If _istGen(g).Operazione <> GenCampione Then ApplicaGen(gen, _istGen(g), False, tastoMin, tastoMax, velMin, velMax)
            Next
            ' a livello di preset si sommano le grandezze relative (accordatura, tempi, attenuazione, pan, filtro)
            For g As Integer = 0 To NumeroGeneratori - 1
                Select Case g
                    Case GenInizio, GenFine, GenCicloInizio, GenCicloFine, GenInizioGrosso, GenFineGrosso, GenCicloInizioGrosso, GenCicloFineGrosso,
                         GenCampione, GenStrumento, GenModoCampione, GenClasseEsclusiva, GenNotaRadice, GenZonaTasti, GenZonaForza
                        ' non si sommano
                    Case Else
                        gen(g) += zonaPreset(g)
                End Select
            Next
            Dim z As New ZonaSf2()
            z.TastoMin = Math.Max(tastoMin, pTastoMin)
            z.TastoMax = Math.Min(tastoMax, pTastoMax)
            z.VelMin = Math.Max(velMin, pVelMin)
            z.VelMax = Math.Min(velMax, pVelMax)
            z.Generatori = gen
            z.Campione = campione
            If z.TastoMin <= z.TastoMax AndAlso z.VelMin <= z.VelMax Then risultato.Add(z)
        Next
    End Sub

    ' ---- le voci ------------------------------------------------------------------------------------

    ''' <summary>La voce (o le voci, se la nota usa più campioni) per una nota; Nothing se il preset non ha nulla per quella nota.</summary>
    Public Function CreaVoce(preset As PresetSf2, nota As Integer, velocita As Double) As VoceSintesi
        Dim forza As Integer = Math.Max(1, Math.Min(127, CInt(Math.Round(velocita * 127.0))))
        Dim insieme As VoceComposta = Nothing
        Dim unica As VoceSintesi = Nothing
        For Each z As ZonaSf2 In ZoneDelPreset(preset.Indice)
            If nota < z.TastoMin OrElse nota > z.TastoMax OrElse forza < z.VelMin OrElse forza > z.VelMax Then Continue For
            Dim h As IntestazioneCampione = _intestazioni(z.Campione)
            If (h.Tipo And &H8000) <> 0 Then Continue For
            Dim v As VoceSintesi = New VoceSoundFont(_campioni, h, z.Generatori, nota, forza)
            If unica Is Nothing Then
                unica = v
            Else
                If insieme Is Nothing Then
                    insieme = New VoceComposta()
                    insieme.Aggiungi(unica)
                End If
                insieme.Aggiungi(v)
            End If
        Next
        If insieme IsNot Nothing Then Return insieme
        Return unica
    End Function

    Friend Shared Function TempoDaTimecent(tc As Integer) As Double
        Return Math.Pow(2.0, tc / 1200.0)
    End Function

End Class

''' <summary>Una nota suonata con un campione del SoundFont.</summary>
Friend NotInheritable Class VoceSoundFont
    Inherits VoceSintesi

    Private ReadOnly _s() As Short
    Private _pos As Double
    Private ReadOnly _passo As Double
    Private ReadOnly _inizio As Integer
    Private ReadOnly _fine As Integer
    Private ReadOnly _cicloInizio As Integer
    Private ReadOnly _cicloFine As Integer
    Private ReadOnly _modo As Integer
    Private ReadOnly _guadagno As Double

    ' inviluppo
    Private _stadio As Integer
    Private _contatore As Integer
    Private _env As Double
    Private ReadOnly _campioniRitardo As Integer
    Private ReadOnly _campioniAttacco As Integer
    Private ReadOnly _campioniTenuta As Integer
    Private ReadOnly _fattoreCalo As Double
    Private ReadOnly _livelloSostegno As Double
    Private _fattoreRilascio As Double
    Private ReadOnly _fattoreRilascioNota As Double

    ' vibrato
    Private ReadOnly _vibratoCent As Double
    Private ReadOnly _vibratoPasso As Double
    Private ReadOnly _vibratoRitardo As Integer
    Private _vibratoFase As Double
    Private _eta As Integer

    ' filtro
    Private ReadOnly _filtro As FiltroBiquad

    Public Sub New(campioni() As Short, h As IntestazioneCampione, g() As Integer, nota As Integer, forza As Integer)
        _s = campioni
        Dim inizioOffset As Integer = g(0) + g(4) * 32768
        Dim fineOffset As Integer = g(1) + g(12) * 32768
        Dim cicloInizioOffset As Integer = g(2) + g(45) * 32768
        Dim cicloFineOffset As Integer = g(3) + g(50) * 32768
        _inizio = Math.Max(0, Math.Min(campioni.Length - 2, h.Inizio + inizioOffset))
        _fine = Math.Max(_inizio + 2, Math.Min(campioni.Length - 1, h.Fine + fineOffset))
        _cicloInizio = Math.Max(_inizio, Math.Min(_fine - 1, h.CicloInizio + cicloInizioOffset))
        _cicloFine = Math.Max(_cicloInizio + 2, Math.Min(_fine, h.CicloFine + cicloFineOffset))
        _modo = g(54) And 3
        If _cicloFine - _cicloInizio < 8 Then _modo = 0
        _pos = _inizio

        Dim radice As Integer = If(g(58) >= 0, g(58), h.NotaOriginale)
        If radice < 0 OrElse radice > 127 Then radice = 60
        Dim tonoCent As Double = (nota - radice) * (g(56) / 100.0) * 100.0 + g(51) * 100.0 + g(52) + h.CorrezioneCent
        _passo = Math.Pow(2.0, tonoCent / 1200.0) * (h.Frequenza / WGDsp.FC)

        Dim attenuazioneCb As Double = Math.Max(0.0, g(48))
        Dim curvaForza As Double = (forza / 127.0) * (forza / 127.0)
        _guadagno = Math.Pow(10.0, -attenuazioneCb / 200.0) * curvaForza * 1.5

        ' inviluppo del volume
        _campioniRitardo = CInt(BancoSoundFont.TempoDaTimecent(g(33)) * WGDsp.FC)
        _campioniAttacco = Math.Max(1, CInt(BancoSoundFont.TempoDaTimecent(g(34)) * WGDsp.FC))
        _campioniTenuta = CInt(BancoSoundFont.TempoDaTimecent(g(35) + g(39) * (60 - nota)) * WGDsp.FC)
        ' il calo del suono a tasto premuto è rallentato: nei pianoforti campionati tenendo la nota si deve sentire svanire piano piano
        Dim tempoCalo As Double = Math.Max(0.001, BancoSoundFont.TempoDaTimecent(g(36) + g(40) * (60 - nota))) * 2.5
        _livelloSostegno = Math.Pow(10.0, -WGDsp.Limita(g(37), 0.0, 1440.0) / 200.0)
        _fattoreCalo = Math.Pow(0.00001, 1.0 / (tempoCalo * WGDsp.FC))
        Dim tempoRilascio As Double = Math.Max(0.005, BancoSoundFont.TempoDaTimecent(g(38)))
        _fattoreRilascioNota = Math.Pow(0.00001, 1.0 / (tempoRilascio * WGDsp.FC))
        _fattoreRilascio = _fattoreRilascioNota
        _stadio = If(_campioniRitardo > 0, 0, 1)

        ' vibrato (LFO di vibrato e, se manca, quello di modulazione)
        Dim cent As Double = g(6)
        Dim freqTc As Integer = g(24)
        Dim ritardoTc As Integer = g(23)
        If cent = 0.0 AndAlso g(5) <> 0 Then
            cent = g(5)
            freqTc = g(22)
            ritardoTc = g(21)
        End If
        _vibratoCent = WGDsp.Limita(cent, -1200.0, 1200.0)
        _vibratoPasso = WGDsp.DuePi * 8.176 * Math.Pow(2.0, freqTc / 1200.0) / WGDsp.FC
        _vibratoRitardo = CInt(BancoSoundFont.TempoDaTimecent(ritardoTc) * WGDsp.FC)

        ' filtro passa basso
        Dim fc As Double = 8.176 * Math.Pow(2.0, WGDsp.Limita(g(8), 1500.0, 13500.0) / 1200.0)
        If fc < 17000.0 Then _filtro = FiltroBiquad.PassaBasso(fc, 0.707 * Math.Pow(10.0, g(9) / 200.0))

        Pan = CSng(WGDsp.Limita(g(17) / 500.0, -1.0, 1.0))
        If g(57) <> 0 Then GruppoDiSilenzio = 1000 + g(57)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        Dim s() As Short = _s
        For i As Integer = 0 To quanti - 1
            If Finita Then Exit For
            ' inviluppo
            Select Case _stadio
                Case 0
                    _contatore += 1
                    If _contatore >= _campioniRitardo Then
                        _stadio = 1
                        _contatore = 0
                    End If
                Case 1
                    _env += 1.0 / _campioniAttacco
                    If _env >= 1.0 Then
                        _env = 1.0
                        _stadio = 2
                        _contatore = 0
                    End If
                Case 2
                    _contatore += 1
                    If _contatore >= _campioniTenuta Then _stadio = 3
                Case 3
                    _env *= _fattoreCalo
                    If _env <= _livelloSostegno Then
                        _env = _livelloSostegno
                        _stadio = 4
                    End If
                Case 5
                    _env *= _fattoreRilascio
                    If _env < 0.00005 Then
                        Finita = True
                        Exit For
                    End If
            End Select

            ' campione con interpolazione cubica
            Dim indice As Integer = CInt(_pos)
            Dim f As Double = _pos - indice
            Dim y0 As Double = s(If(indice > _inizio, indice - 1, indice))
            Dim y1 As Double = s(indice)
            Dim y2 As Double = s(If(indice + 1 <= _fine, indice + 1, indice))
            Dim y3 As Double = s(If(indice + 2 <= _fine, indice + 2, If(indice + 1 <= _fine, indice + 1, indice)))
            Dim c1 As Double = 0.5 * (y2 - y0)
            Dim c2 As Double = y0 - 2.5 * y1 + 2.0 * y2 - 0.5 * y3
            Dim c3 As Double = 0.5 * (y3 - y0) + 1.5 * (y1 - y2)
            Dim valore As Double = (((c3 * f + c2) * f + c1) * f + y1) / 32768.0

            If _filtro IsNot Nothing Then valore = _filtro.Elabora(valore)
            uscita(i) += CSng(valore * _env * _guadagno * Livello)

            ' avanzamento
            Dim passo As Double = _passo
            If _vibratoCent <> 0.0 AndAlso _eta > _vibratoRitardo Then
                passo *= 1.0 + _vibratoCent * Math.Sin(_vibratoFase) * 0.000577623
                _vibratoFase += _vibratoPasso
            End If
            _eta += 1
            _pos += passo
            Dim inCiclo As Boolean = _modo = 1 OrElse (_modo = 3 AndAlso _stadio < 5)
            If inCiclo AndAlso _pos >= _cicloFine Then
                _pos -= (_cicloFine - _cicloInizio)
                If _pos < _cicloInizio Then _pos = _cicloInizio
            ElseIf _pos >= _fine - 1 Then
                Finita = True
                Exit For
            End If
        Next
    End Sub

    Public Overrides Sub Rilascia()
        If InRilascio Then Return
        InRilascio = True
        If _stadio < 5 Then
            If _stadio < 1 Then _env = 0.0
            _stadio = 5
        End If
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        _stadio = 5
        _fattoreRilascio = Math.Min(_fattoreRilascio, Math.Pow(0.00001, 1.0 / (0.02 * WGDsp.FC)))
    End Sub

End Class
