' ============================================================================================
' WGSintesi.Catalogo - le categorie e gli strumenti musicali
' ============================================================================================
'
' Ogni strumento è una funzione che costruisce la voce di una nota. Le categorie raccolgono gli strumenti simili.
' I valori (tempi di spegnimento, armoniche, filtri) sono scelti ad orecchio per assomigliare agli strumenti veri;
' si possono regolare qui senza toccare il resto.
' ============================================================================================
Imports System
Imports System.Collections.Generic

''' <summary>Più voci che suonano insieme come una sola nota (clavicembalo con il registro all'ottava, mandolino con le corde doppie).</summary>
Public NotInheritable Class VoceComposta
    Inherits VoceSintesi

    Private ReadOnly _voci As New List(Of VoceSintesi)()

    Public Sub Aggiungi(voce As VoceSintesi)
        _voci.Add(voce)
    End Sub

    Public Overrides Sub Genera(uscita() As Single, quanti As Integer)
        Dim tutteFinite As Boolean = True
        For Each v As VoceSintesi In _voci
            v.Genera(uscita, quanti)
            If Not v.Finita Then tutteFinite = False
        Next
        If tutteFinite Then Finita = True
    End Sub

    Public Overrides Sub Rilascia()
        InRilascio = True
        For Each v As VoceSintesi In _voci
            v.Rilascia()
        Next
    End Sub

    Public Overrides Sub Silenzia()
        InRilascio = True
        For Each v As VoceSintesi In _voci
            v.Silenzia()
        Next
    End Sub

End Class

Public NotInheritable Class CatalogoStrumenti

    Private Shared _categorie As List(Of CategoriaStrumenti)
    Private Shared ReadOnly _blocco As New Object()

    Private Sub New()
    End Sub

    Public Shared Function Categorie() As List(Of CategoriaStrumenti)
        SyncLock _blocco
            If _categorie Is Nothing Then _categorie = Costruisci()
            Return _categorie
        End SyncLock
    End Function

    Private Shared Function Nuova(elenco As List(Of CategoriaStrumenti), nome As String, descrizione As String) As CategoriaStrumenti
        Dim c As New CategoriaStrumenti()
        c.Nome = nome
        c.Descrizione = descrizione
        elenco.Add(c)
        Return c
    End Function

    Private Shared Sub Strumento(c As CategoriaStrumenti, nome As String, descrizione As String, crea As Func(Of Integer, Double, VoceSintesi),
                                 Optional ottava As Integer = 4, Optional riverbero As Double = 0.2, Optional livello As Double = 1.0)
        Dim s As New DefinizioneStrumento()
        s.Nome = nome
        s.Descrizione = descrizione
        s.Crea = crea
        s.OttavaIniziale = ottava
        s.Riverbero = riverbero
        s.Livello = livello
        s.Origine = "sintetico"
        c.Strumenti.Add(s)
    End Sub

    Private Shared Sub Batteria(c As CategoriaStrumenti, nome As String, descrizione As String, pezzi As List(Of ProfiloPercussione), riverbero As Double)
        Dim s As New DefinizioneStrumento()
        s.Nome = nome
        s.Descrizione = descrizione
        s.Percussivo = True
        s.Origine = "sintetico"
        s.Riverbero = riverbero
        s.MassimoVoci = 32
        Dim nomi(pezzi.Count - 1) As String
        For i As Integer = 0 To pezzi.Count - 1
            nomi(i) = pezzi(i).Nome
        Next
        s.NomiPezzi = nomi
        s.Crea = Function(nota As Integer, vel As Double) As VoceSintesi
                     Dim indice As Integer = ((nota Mod pezzi.Count) + pezzi.Count) Mod pezzi.Count
                     Return New VocePercussione(pezzi(indice), vel)
                 End Function
        c.Strumenti.Add(s)
    End Sub

    Private Shared Function Costruisci() As List(Of CategoriaStrumenti)
        Dim elenco As New List(Of CategoriaStrumenti)()

        Dim tastiere As CategoriaStrumenti = Nuova(elenco, "Pianoforti e tastiere", "Pianoforti, pianoforti elettrici, clavicembalo e celesta")
        Strumento(tastiere, "Pianoforte a coda", "Pianoforte da concerto, suono pieno e lungo", Function(n, v) Pianoforte(n, v, False), 4, 0.3)
        Strumento(tastiere, "Pianoforte verticale", "Pianoforte da casa, più asciutto e brillante", Function(n, v) Pianoforte(n, v, True), 4, 0.18)
        Strumento(tastiere, "Piano elettrico Rhodes", "Pianoforte elettrico morbido, con il tremolo", Function(n, v) PianoElettrico(n, v, False), 4, 0.2)
        Strumento(tastiere, "Piano elettrico Wurlitzer", "Pianoforte elettrico più graffiante", Function(n, v) PianoElettrico(n, v, True), 4, 0.18)
        Strumento(tastiere, "Clavicembalo", "Clavicembalo con il registro all'ottava", AddressOf Clavicembalo, 4, 0.28)
        Strumento(tastiere, "Celesta", "Celesta, un suono di campanelli dolcissimo", Function(n, v) Mallet(n, v, New Double() {1.0, 4.0, 9.2, 15.0}, New Double() {1.0, 0.35, 0.14, 0.05}, New Double() {1.0, 0.4, 0.15, 0.08}, 2.6, 0.01, 0.0, 0.0), 5, 0.4)

        Dim organi As CategoriaStrumenti = Nuova(elenco, "Organi e fisarmonica", "Organi elettrici e a canne, fisarmonica e armonica")
        Strumento(organi, "Organo elettrico", "Organo a ruote foniche con la percussione", AddressOf OrganoElettrico, 3, 0.15)
        Strumento(organi, "Organo da chiesa", "Organo a canne con il principale e le ottave", AddressOf OrganoDaChiesa, 3, 0.7)
        Strumento(organi, "Organo positivo", "Piccolo organo di flauti, dolce e raccolto", AddressOf OrganoPositivo, 4, 0.4)
        Strumento(organi, "Fisarmonica", "Fisarmonica con le ance accordate un po' alte", AddressOf Fisarmonica, 4, 0.2)
        Strumento(organi, "Armonica a bocca", "Armonica con il vibrato e il soffio", AddressOf Armonica, 4, 0.15)

        Dim corde As CategoriaStrumenti = Nuova(elenco, "Chitarre e corde pizzicate", "Chitarre, bassi, arpa, banjo, mandolino")
        Strumento(corde, "Chitarra classica", "Chitarra a corde di nylon", AddressOf ChitarraClassica, 3, 0.25)
        Strumento(corde, "Chitarra acustica", "Chitarra a corde d'acciaio", AddressOf ChitarraAcustica, 3, 0.22)
        Strumento(corde, "Chitarra elettrica pulita", "Chitarra elettrica senza distorsione", AddressOf ChitarraElettrica, 3, 0.25)
        Strumento(corde, "Basso elettrico", "Basso elettrico suonato con le dita", AddressOf BassoElettrico, 2, 0.1)
        Strumento(corde, "Contrabbasso pizzicato", "Contrabbasso suonato pizzicando", AddressOf ContrabbassoPizzicato, 2, 0.15)
        Strumento(corde, "Arpa", "Arpa da concerto, con le note che risuonano", AddressOf Arpa, 4, 0.5)
        Strumento(corde, "Banjo", "Banjo a cinque corde, brillante e corto", AddressOf Banjo, 4, 0.12)
        Strumento(corde, "Mandolino", "Mandolino con le corde doppie", AddressOf Mandolino, 4, 0.25)

        Dim archi As CategoriaStrumenti = Nuova(elenco, "Archi", "Violino, violoncello, contrabbasso e orchestra d'archi")
        Strumento(archi, "Violino", "Violino con il vibrato", Function(n, v) Arco(n, v, 0), 4, 0.4)
        Strumento(archi, "Violoncello", "Violoncello, caldo e profondo", Function(n, v) Arco(n, v, 1), 3, 0.4)
        Strumento(archi, "Contrabbasso con l'arco", "Contrabbasso suonato con l'arco", Function(n, v) Arco(n, v, 2), 2, 0.35)
        Strumento(archi, "Orchestra d'archi", "Tutta la sezione degli archi insieme", Function(n, v) Arco(n, v, 3), 3, 0.6)

        Dim legni As CategoriaStrumenti = Nuova(elenco, "Fiati e legni", "Flauti, clarinetto, oboe, sassofono, ocarina")
        Strumento(legni, "Flauto traverso", "Flauto traverso con il soffio e il vibrato", AddressOf FlautoTraverso, 5, 0.35)
        Strumento(legni, "Flauto dolce", "Flauto dolce, dritto e puro", AddressOf FlautoDolce, 5, 0.3)
        Strumento(legni, "Flauto di Pan", "Flauto di Pan, ricco di aria", AddressOf FlautoDiPan, 4, 0.4)
        Strumento(legni, "Ocarina", "Ocarina, un suono tondo e dolce", AddressOf Ocarina, 5, 0.3)
        Strumento(legni, "Clarinetto", "Clarinetto, voce scura e vellutata", AddressOf Clarinetto, 4, 0.3)
        Strumento(legni, "Oboe", "Oboe, voce nasale e penetrante", AddressOf Oboe, 4, 0.3)
        Strumento(legni, "Sassofono contralto", "Sassofono con il suo timbro caldo", AddressOf Sassofono, 4, 0.3)

        Dim ottoni As CategoriaStrumenti = Nuova(elenco, "Ottoni", "Tromba, trombone, corno, tuba e sezione di ottoni")
        Strumento(ottoni, "Tromba", "Tromba, squillante", Function(n, v) Ottone(n, v, 0), 4, 0.3)
        Strumento(ottoni, "Trombone", "Trombone, caldo e pieno", Function(n, v) Ottone(n, v, 1), 3, 0.3)
        Strumento(ottoni, "Corno francese", "Corno, rotondo e morbido", Function(n, v) Ottone(n, v, 2), 3, 0.4)
        Strumento(ottoni, "Tuba", "Tuba, profonda", Function(n, v) Ottone(n, v, 3), 2, 0.3)
        Strumento(ottoni, "Sezione di ottoni", "Più ottoni insieme", Function(n, v) Ottone(n, v, 4), 3, 0.35)

        Dim intonate As CategoriaStrumenti = Nuova(elenco, "Percussioni intonate", "Marimba, xilofono, vibrafono, campane e simili")
        Strumento(intonate, "Marimba", "Marimba, calda e legnosa", Function(n, v) Mallet(n, v, New Double() {1.0, 3.93, 9.8}, New Double() {1.0, 0.5, 0.12}, New Double() {1.0, 0.18, 0.06}, 2.4, 0.06, 0.0, 0.0), 3, 0.25)
        Strumento(intonate, "Xilofono", "Xilofono, secco e brillante", Function(n, v) Mallet(n, v, New Double() {1.0, 3.0, 6.0}, New Double() {1.0, 0.45, 0.22}, New Double() {1.0, 0.3, 0.12}, 0.9, 0.08, 0.0, 0.0), 4, 0.2)
        Strumento(intonate, "Vibrafono", "Vibrafono con le pale che girano", Function(n, v) Mallet(n, v, New Double() {1.0, 4.0, 10.0}, New Double() {1.0, 0.3, 0.1}, New Double() {1.0, 0.25, 0.1}, 5.5, 0.04, 5.0, 0.35), 4, 0.35)
        Strumento(intonate, "Glockenspiel", "Glockenspiel, metallico e cristallino", Function(n, v) Mallet(n, v, New Double() {1.0, 2.76, 5.4, 8.93}, New Double() {1.0, 0.45, 0.25, 0.12}, New Double() {1.0, 0.5, 0.3, 0.15}, 3.0, 0.03, 0.0, 0.0), 5, 0.4)
        Strumento(intonate, "Campane tubolari", "Campane tubolari d'orchestra", Function(n, v) Mallet(n, v, New Double() {1.0, 2.0, 3.0, 4.2, 5.4, 6.8}, New Double() {1.0, 0.6, 0.4, 0.3, 0.2, 0.12}, New Double() {1.0, 0.8, 0.6, 0.5, 0.4, 0.3}, 7.0, 0.03, 0.0, 0.0), 4, 0.5)
        Strumento(intonate, "Carillon", "Carillon, come una scatola musicale", Function(n, v) Mallet(n, v, New Double() {1.0, 2.9, 5.6}, New Double() {1.0, 0.3, 0.12}, New Double() {1.0, 0.4, 0.2}, 1.3, 0.02, 0.0, 0.0), 5, 0.3)
        Strumento(intonate, "Kalimba", "Kalimba, il piano a pollice africano", Function(n, v) Mallet(n, v, New Double() {1.0, 5.4, 8.9}, New Double() {1.0, 0.25, 0.1}, New Double() {1.0, 0.18, 0.1}, 1.6, 0.09, 0.0, 0.0), 4, 0.3)
        Strumento(intonate, "Steel drum", "Tamburo d'acciaio dei Caraibi", Function(n, v) Mallet(n, v, New Double() {1.0, 2.0, 3.0, 4.01}, New Double() {1.0, 0.55, 0.3, 0.12}, New Double() {1.0, 0.6, 0.4, 0.25}, 1.8, 0.05, 6.5, 0.25), 4, 0.3)

        Dim voci As CategoriaStrumenti = Nuova(elenco, "Voci", "Cori che cantano una vocale")
        Strumento(voci, "Coro, vocale a", "Coro che canta la vocale a", Function(n, v) Coro(n, v, True), 4, 0.6)
        Strumento(voci, "Coro, vocale u", "Coro che canta la vocale u", Function(n, v) Coro(n, v, False), 4, 0.6)

        Dim sintesi As CategoriaStrumenti = Nuova(elenco, "Sintetizzatori", "Pad, lead e bassi elettronici")
        Strumento(sintesi, "Pad caldo", "Tappeto sonoro morbido, che sale piano", AddressOf PadCaldo, 3, 0.55)
        Strumento(sintesi, "Lead a dente di sega", "Suono solista brillante", AddressOf LeadSega, 4, 0.25)
        Strumento(sintesi, "Lead a onda quadra", "Suono solista cavo, come un clarinetto elettronico", AddressOf LeadQuadro, 4, 0.25)
        Strumento(sintesi, "Basso sintetico", "Basso elettronico con la sotto ottava", AddressOf BassoSintetico, 2, 0.08)
        Strumento(sintesi, "Pizzicato sintetico", "Nota breve e brillante", AddressOf PizzicatoSintetico, 4, 0.3)
        Strumento(sintesi, "Basso a modulazione di frequenza", "Basso elettronico stile anni ottanta", AddressOf BassoFm, 2, 0.1)

        Dim batterie As CategoriaStrumenti = Nuova(elenco, "Batteria e percussioni", "Batterie e percussioni: ogni tasto è un pezzo diverso")
        Batteria(batterie, "Batteria acustica", "Cassa, rullante, charleston, tom e piatti", MappaBatteria(PezziBatteriaAcustica(), MappaAcustica()), 0.18)
        Batteria(batterie, "Batteria elettronica", "Batteria elettronica stile drum machine", MappaBatteria(PezziBatteria808(), MappaElettronica()), 0.1)
        Batteria(batterie, "Percussioni latine", "Conga, bongo, timbal, claves, maracas e altre", MappaBatteria(PezziLatini(), MappaLatina()), 0.2)
        Batteria(batterie, "Percussioni orchestrali", "Timpani, gran cassa, piatti, triangolo e altre", MappaBatteria(PezziOrchestrali(), MappaOrchestrale()), 0.5)

        Return elenco
    End Function

    ' ================================================================================================
    ' pianoforti e mallet
    ' ================================================================================================

    Private Shared Function Pianoforte(nota As Integer, vel As Double, verticale As Boolean) As VoceSintesi
        Dim f0 As Double = WGDsp.NotaInHz(nota)
        ' tenendo il tasto il suono svanisce lentamente, come in un pianoforte a coda; sollevato, il motore lo stacca subito
        Dim t60f As Double = WGDsp.Limita(45.0 * Math.Exp(-0.03 * (nota - 21)) * If(verticale, 0.7, 1.0), 2.0, 40.0)
        Dim b As Double = 0.00005 * Math.Exp(0.052 * (nota - 21)) * If(verticale, 1.3, 1.0)
        Dim t60Rilascio As Double = 0.14 + 0.4 * WGDsp.Limita((72 - nota) / 50.0, 0.0, 1.0)
        Dim v As New VoceModale(t60Rilascio, t60f * 1.7 + 1.0)
        Dim numeroCorde As Integer = If(nota < 33, 1, If(nota < 55, 2, 3))
        Dim scostamenti As Double() = If(numeroCorde = 1, New Double() {0.0}, If(numeroCorde = 2, New Double() {-0.4, 0.4}, New Double() {-0.55, 0.0, 0.55}))
        If verticale Then
            For i As Integer = 0 To scostamenti.Length - 1
                scostamenti(i) *= 1.5
            Next
        End If
        Dim p As Double = 0.85 + (1.0 - vel) * 0.9
        Dim taglio As Double = (1800.0 + 6500.0 * vel) * If(verticale, 1.25, 1.0)
        Dim freq As New List(Of Double)()
        Dim amp As New List(Of Double)()
        Dim tempo As New List(Of Double)()
        Dim somma2 As Double = 0.0
        For k As Integer = 1 To 36
            Dim fk As Double = f0 * k * Math.Sqrt(1.0 + b * k * k)
            If fk > 0.45 * WGDsp.FC Then Exit For
            Dim a As Double = Math.Pow(k, -p)
            a *= Math.Abs(Math.Sin(Math.PI * k * 0.125)) + 0.15
            a /= 1.0 + Math.Pow(fk / taglio, 2.0)
            freq.Add(fk)
            amp.Add(a)
            tempo.Add(Math.Max(0.06, t60f / (1.0 + 0.45 * Math.Pow(k - 1, 1.15))))
            somma2 += a * a
        Next
        Dim scala As Double = 0.13 * Math.Pow(vel, 1.2) / Math.Sqrt(somma2 / 2.0)
        For i As Integer = 0 To freq.Count - 1
            Dim a As Double = amp(i) * scala
            If i < 10 AndAlso numeroCorde > 1 Then
                For s As Integer = 0 To numeroCorde - 1
                    v.Aggiungi(freq(i) * WGDsp.CentoAMoltiplicatore(scostamenti(s)), a / numeroCorde, tempo(i) * If(s = 0, 1.0, 1.6))
                Next
            Else
                v.Aggiungi(freq(i), a, tempo(i))
            End If
        Next
        v.Colpo(0.03 * vel, 0.025, 1200.0 + 3000.0 * vel)
        v.Pan = CSng(WGDsp.Limita((nota - 60) / 70.0, -0.6, 0.6))
        v.Prepara()
        Return v
    End Function

    Private Shared Function Mallet(nota As Integer, vel As Double, rapporti() As Double, ampiezze() As Double, tempiRelativi() As Double,
                                   t60Base As Double, colpo As Double, tremoloHz As Double, tremoloProfondita As Double) As VoceSintesi
        Dim f0 As Double = WGDsp.NotaInHz(nota)
        Dim t60 As Double = WGDsp.Limita(t60Base * Math.Exp(-0.025 * (nota - 60)), 0.25, t60Base * 2.5)
        Dim v As New VoceModale(0.12, t60 * 1.6 + 0.5)
        Dim somma As Double = 0.0
        For i As Integer = 0 To rapporti.Length - 1
            somma += ampiezze(i)
        Next
        Dim scala As Double = 0.34 * vel / somma
        For i As Integer = 0 To rapporti.Length - 1
            v.Aggiungi(f0 * rapporti(i), ampiezze(i) * scala, t60 * tempiRelativi(i))
        Next
        v.Colpo(colpo * vel, 0.012, 4500.0)
        v.TremoloHz = tremoloHz
        v.TremoloProfondita = tremoloProfondita
        v.Pan = CSng(WGDsp.Limita((nota - 60) / 80.0, -0.5, 0.5))
        v.Prepara()
        Return v
    End Function

    Private Shared Function Clavicembalo(nota As Integer, vel As Double) As VoceSintesi
        Dim c As New VoceComposta()
        c.Aggiungi(Corda(nota, 0.5 + 0.5 * vel, 2.2, 0.85, 0.2, 12000.0, 0.17, 0.06, Nothing))
        c.Aggiungi(Corda(nota + 12, 0.35 + 0.4 * vel, 1.2, 0.9, 0.2, 12000.0, 0.1, 0.06, Nothing))
        c.Livello = 0.9F
        Return c
    End Function

    ' ================================================================================================
    ' pianoforti elettrici (modulazione di frequenza)
    ' ================================================================================================

    Private Shared Function PianoElettrico(nota As Integer, vel As Double, wurlitzer As Boolean) As VoceSintesi
        Dim f0 As Double = WGDsp.NotaInHz(nota)
        Dim v As New VoceFM(f0, vel)
        v.RapportoPortante = 1.0
        If Not wurlitzer Then
            v.RapportoMod1 = 1.0
            v.Indice1Inizio = 1.7
            v.Indice1Fine = 0.3
            v.Indice1T60 = 1.1
            v.RapportoMod2 = 14.0
            v.Indice2Inizio = 1.1
            v.Indice2T60 = 0.09
            v.AmpiezzaT60 = WGDsp.Limita(4.0 * Math.Exp(-0.03 * (nota - 40)), 0.8, 5.0)
            v.TremoloHz = 4.6
            v.TremoloProfondita = 0.12
        Else
            v.RapportoMod1 = 1.0
            v.Indice1Inizio = 3.0
            v.Indice1Fine = 0.7
            v.Indice1T60 = 0.5
            v.RapportoMod2 = 3.0
            v.Indice2Inizio = 0.9
            v.Indice2T60 = 0.2
            v.Retroazione = 0.25
            v.AmpiezzaT60 = WGDsp.Limita(2.6 * Math.Exp(-0.03 * (nota - 40)), 0.6, 3.5)
            v.TremoloHz = 5.2
            v.TremoloProfondita = 0.08
        End If
        v.Attacco = 0.0025
        v.RilascioSec = 0.28
        v.Livello = 0.32F
        v.Pan = CSng(WGDsp.Limita((nota - 60) / 90.0, -0.4, 0.4))
        Return v
    End Function

    Private Shared Function BassoFm(nota As Integer, vel As Double) As VoceSintesi
        Dim v As New VoceFM(WGDsp.NotaInHz(nota), vel)
        v.RapportoMod1 = 1.0
        v.Indice1Inizio = 3.2
        v.Indice1Fine = 0.8
        v.Indice1T60 = 0.35
        v.RapportoMod2 = 2.0
        v.Indice2Inizio = 0.6
        v.Indice2T60 = 0.15
        v.AmpiezzaT60 = 1.6
        v.Attacco = 0.003
        v.RilascioSec = 0.12
        v.Livello = 0.4F
        Return v
    End Function

    ' ================================================================================================
    ' organi e fisarmonica (armoniche continue)
    ' ================================================================================================

    Private Shared Function Continua(nota As Integer, vel As Double, rapporti() As Double, ampiezze() As Double, regola As Action(Of VoceContinua)) As VoceSintesi
        Dim f0 As Double = WGDsp.NotaInHz(nota)
        Dim r As New List(Of Double)()
        Dim a As New List(Of Double)()
        Dim somma As Double = 0.0
        For i As Integer = 0 To rapporti.Length - 1
            If f0 * rapporti(i) * 1.05 >= 0.46 * WGDsp.FC Then Continue For
            r.Add(rapporti(i))
            a.Add(ampiezze(i))
            somma += ampiezze(i)
        Next
        If r.Count = 0 Then
            r.Add(1.0)
            a.Add(0.0)
            somma = 1.0
        End If
        Dim v As New VoceContinua(f0)
        v.Rapporti = r.ToArray()
        v.Ampiezze = a.ToArray()
        If regola IsNot Nothing Then regola(v)
        v.Livello = CSng(0.3 / Math.Max(0.2, somma) * (0.55 + 0.45 * vel))
        v.Pan = CSng(WGDsp.Limita((nota - 60) / 90.0, -0.4, 0.4))
        Return v
    End Function

    Private Shared Function OrganoElettrico(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {0.5, 1.5, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0},
                        New Double() {0.7, 0.5, 1.0, 0.8, 0.45, 0.35, 0.18, 0.12, 0.1},
                        Sub(v)
                            v.Attacco = 0.006
                            v.RilascioSec = 0.045
                            v.PercussioneRapporto = 3.0
                            v.PercussioneLivello = 0.35
                            v.PercussioneT60 = 0.22
                            v.TremoloHz = 6.6
                            v.TremoloProfondita = 0.14
                            v.VibratoHz = 6.6
                            v.VibratoCent = 4.0
                            v.VibratoRitardo = 0.0
                        End Sub)
    End Function

    Private Shared Function OrganoDaChiesa(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 4.0, 6.0, 8.0},
                        New Double() {1.0, 0.75, 0.35, 0.5, 0.2, 0.15},
                        Sub(v)
                            v.Attacco = 0.05
                            v.RilascioSec = 0.16
                            v.SoffioLivello = 0.012
                            v.SoffioCentroHz = Math.Min(6000.0, WGDsp.NotaInHz(nota) * 4.0)
                            v.ColpoLivello = 0.04
                            v.ColpoDurata = 0.08
                        End Sub)
    End Function

    Private Shared Function OrganoPositivo(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 4.0, 6.0},
                        New Double() {1.0, 0.42, 0.1, 0.08, 0.03},
                        Sub(v)
                            v.Attacco = 0.035
                            v.RilascioSec = 0.12
                            v.SoffioLivello = 0.008
                            v.ColpoLivello = 0.03
                        End Sub)
    End Function

    Private Shared Function Fisarmonica(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0},
                        New Double() {1.0, 0.6, 0.5, 0.35, 0.25, 0.15, 0.1, 0.07},
                        Sub(v)
                            v.StratoCent = 14.0
                            v.Attacco = 0.03
                            v.RilascioSec = 0.07
                            v.SoffioLivello = 0.01
                            v.TremoloHz = 0.35
                            v.TremoloProfondita = 0.05
                        End Sub)
    End Function

    Private Shared Function Armonica(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 4.0, 5.0, 6.0},
                        New Double() {1.0, 0.7, 0.5, 0.4, 0.3, 0.2},
                        Sub(v)
                            v.StratoCent = 9.0
                            v.Attacco = 0.04
                            v.RilascioSec = 0.09
                            v.SoffioLivello = 0.02
                            v.VibratoHz = 5.4
                            v.VibratoCent = 12.0
                            v.VibratoRitardo = 0.25
                        End Sub)
    End Function

    ' ================================================================================================
    ' flauti e legni
    ' ================================================================================================

    Private Shared Function FlautoTraverso(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 4.0}, New Double() {1.0, 0.25, 0.08, 0.03},
                        Sub(v)
                            v.Attacco = 0.045
                            v.RilascioSec = 0.07
                            v.SoffioLivello = 0.045
                            v.SoffioCentroHz = Math.Max(1800.0, WGDsp.NotaInHz(nota) * 3.0)
                            v.ColpoLivello = 0.09
                            v.ColpoDurata = 0.05
                            v.VibratoHz = 5.0
                            v.VibratoCent = 18.0
                            v.VibratoRitardo = 0.35
                        End Sub)
    End Function

    Private Shared Function FlautoDolce(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0}, New Double() {1.0, 0.08, 0.02},
                        Sub(v)
                            v.Attacco = 0.025
                            v.RilascioSec = 0.06
                            v.SoffioLivello = 0.015
                            v.SoffioCentroHz = Math.Max(2000.0, WGDsp.NotaInHz(nota) * 2.0)
                            v.ColpoLivello = 0.04
                        End Sub)
    End Function

    Private Shared Function FlautoDiPan(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0}, New Double() {1.0, 0.1, 0.04},
                        Sub(v)
                            v.Attacco = 0.06
                            v.RilascioSec = 0.1
                            v.SoffioLivello = 0.08
                            v.SoffioCentroHz = Math.Max(1500.0, WGDsp.NotaInHz(nota) * 2.0)
                            v.SoffioQ = 2.0
                            v.ColpoLivello = 0.08
                            v.ColpoDurata = 0.08
                        End Sub)
    End Function

    Private Shared Function Ocarina(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0}, New Double() {1.0, 0.03},
                        Sub(v)
                            v.Attacco = 0.04
                            v.RilascioSec = 0.08
                            v.SoffioLivello = 0.01
                            v.VibratoHz = 4.5
                            v.VibratoCent = 10.0
                            v.VibratoRitardo = 0.4
                        End Sub)
    End Function

    Private Shared Function Clarinetto(nota As Integer, vel As Double) As VoceSintesi
        Return Continua(nota, vel, New Double() {1.0, 2.0, 3.0, 5.0, 7.0, 9.0, 11.0},
                        New Double() {1.0, 0.04, 0.6, 0.42, 0.25, 0.15, 0.1},
                        Sub(v)
                            v.Attacco = 0.03
                            v.RilascioSec = 0.06
                            v.SoffioLivello = 0.012
                            v.VibratoHz = 4.8
                            v.VibratoCent = 6.0
                            v.VibratoRitardo = 0.5
                        End Sub)
    End Function

    ' ================================================================================================
    ' corde pizzicate
    ' ================================================================================================

    Private Shared Function Corda(nota As Integer, vel As Double, t60Base As Double, brillantezza As Double, posizione As Double,
                                  morbidezza As Double, eccitazione As Double, t60Rilascio As Double, regola As Action(Of VocePizzicata)) As VocePizzicata
        Dim f0 As Double = WGDsp.NotaInHz(nota)
        Dim t60 As Double = WGDsp.Limita(t60Base * Math.Exp(-0.012 * (nota - 52)), 0.3, t60Base * 3.0)
        Dim v As New VocePizzicata(f0, t60, WGDsp.Limita(brillantezza * (0.7 + 0.5 * vel), 0.0, 1.0), posizione,
                                   eccitazione * (0.5 + 0.7 * vel), t60Rilascio, morbidezza * (0.7 + 0.5 * vel))
        If regola IsNot Nothing Then regola(v)
        v.Pan = CSng(WGDsp.Limita((nota - 55) / 60.0, -0.45, 0.45))
        Return v
    End Function

    Private Shared Function ChitarraClassica(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 2.2, 0.35, 0.17, 3500.0, 0.2, 0.25, Sub(v)
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(105.0, 3.0, 7.0))
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(215.0, 2.5, 4.0))
                                                                  End Sub)
    End Function

    Private Shared Function ChitarraAcustica(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 3.0, 0.7, 0.12, 8000.0, 0.2, 0.3, Sub(v)
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(100.0, 3.0, 6.0))
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(200.0, 2.5, 4.0))
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(3800.0, 1.0, 3.0))
                                                                  End Sub)
    End Function

    Private Shared Function ChitarraElettrica(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 4.0, 0.55, 0.22, 6000.0, 0.2, 0.4, Sub(v)
                                                                       v.AggiungiFiltro(FiltroBiquad.PassaAlto(80.0, 0.7))
                                                                       v.AggiungiFiltro(FiltroBiquad.Picco(2500.0, 1.0, 4.0))
                                                                   End Sub)
    End Function

    Private Shared Function BassoElettrico(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 2.5, 0.3, 0.25, 1500.0, 0.2, 0.15, Sub(v)
                                                                       v.AggiungiFiltro(FiltroBiquad.Picco(80.0, 1.5, 5.0))
                                                                       v.AggiungiFiltro(FiltroBiquad.PassaBasso(3200.0, 0.7))
                                                                       v.Livello = 1.5F
                                                                   End Sub)
    End Function

    Private Shared Function ContrabbassoPizzicato(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 1.6, 0.25, 0.3, 1000.0, 0.2, 0.12, Sub(v)
                                                                       v.AggiungiFiltro(FiltroBiquad.Picco(70.0, 1.5, 6.0))
                                                                       v.AggiungiFiltro(FiltroBiquad.PassaBasso(2200.0, 0.7))
                                                                       v.Livello = 1.6F
                                                                   End Sub)
    End Function

    Private Shared Function Arpa(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 3.5, 0.5, 0.3, 3000.0, 0.2, 1.2, Sub(v)
                                                                      v.AggiungiFiltro(FiltroBiquad.Picco(160.0, 2.0, 3.0))
                                                                  End Sub)
    End Function

    Private Shared Function Banjo(nota As Integer, vel As Double) As VoceSintesi
        Return Corda(nota, vel, 0.8, 0.9, 0.1, 12000.0, 0.2, 0.08, Sub(v)
                                                                       v.AggiungiFiltro(FiltroBiquad.Picco(600.0, 2.0, 8.0))
                                                                       v.AggiungiFiltro(FiltroBiquad.Picco(2400.0, 1.5, 4.0))
                                                                   End Sub)
    End Function

    Private Shared Function Mandolino(nota As Integer, vel As Double) As VoceSintesi
        Dim c As New VoceComposta()
        c.Aggiungi(Corda(nota, vel, 1.0, 0.8, 0.15, 10000.0, 0.17, 0.1, Nothing))
        Dim seconda As VocePizzicata = Corda(nota, vel, 1.0, 0.8, 0.15, 10000.0, 0.17, 0.1, Nothing)
        c.Aggiungi(seconda)
        c.Livello = 0.9F
        Return c
    End Function

    ' ================================================================================================
    ' sottrattiva: archi, ottoni, ance, voci, sintetizzatori
    ' ================================================================================================

    Private Shared Function Sott(nota As Integer, vel As Double, livello As Double, regola As Action(Of VoceSottrattiva)) As VoceSintesi
        Dim v As New VoceSottrattiva(WGDsp.NotaInHz(nota), vel)
        regola(v)
        v.Livello = CSng(livello * (0.6 + 0.4 * vel))
        v.Pan = CSng(WGDsp.Limita((nota - 60) / 90.0, -0.35, 0.35))
        Return v
    End Function

    Private Shared Function Arco(nota As Integer, vel As Double, tipo As Integer) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 0
                                        v.SoffioLivello = 0.012
                                        v.Sostegno = 1.0
                                        v.TagliaSostegno = 0.55
                                        v.TagliaInviluppoOttave = 1.0
                                        v.TagliaTempoApertura = 0.16
                                        v.Risonanza = 1.1
                                        Select Case tipo
                                            Case 0
                                                v.NumeroOscillatori = 3
                                                v.SpargimentoCent = 6.0
                                                v.TagliaBaseHz = 3600.0
                                                v.Attacco = 0.07
                                                v.RilascioSec = 0.15
                                                v.VibratoHz = 5.5
                                                v.VibratoCent = 22.0
                                                v.VibratoRitardo = 0.25
                                                v.Formanti.Add(FiltroBiquad.Picco(290.0, 2.0, 4.0))
                                                v.Formanti.Add(FiltroBiquad.Picco(3000.0, 1.5, 5.0))
                                            Case 1
                                                v.NumeroOscillatori = 3
                                                v.SpargimentoCent = 7.0
                                                v.TagliaBaseHz = 1900.0
                                                v.Attacco = 0.09
                                                v.RilascioSec = 0.18
                                                v.VibratoHz = 5.0
                                                v.VibratoCent = 18.0
                                                v.VibratoRitardo = 0.3
                                                v.Formanti.Add(FiltroBiquad.Picco(110.0, 1.5, 4.0))
                                                v.Formanti.Add(FiltroBiquad.Picco(1500.0, 1.5, 4.0))
                                            Case 2
                                                v.NumeroOscillatori = 3
                                                v.SpargimentoCent = 8.0
                                                v.TagliaBaseHz = 950.0
                                                v.Attacco = 0.12
                                                v.RilascioSec = 0.22
                                                v.VibratoHz = 4.6
                                                v.VibratoCent = 14.0
                                                v.VibratoRitardo = 0.35
                                                v.Formanti.Add(FiltroBiquad.Picco(80.0, 1.5, 4.0))
                                            Case Else
                                                v.NumeroOscillatori = 5
                                                v.SpargimentoCent = 20.0
                                                v.TagliaBaseHz = 3000.0
                                                v.Attacco = 0.35
                                                v.RilascioSec = 0.4
                                                v.VibratoHz = 5.3
                                                v.VibratoCent = 10.0
                                                v.VibratoRitardo = 0.4
                                        End Select
                                    End Sub)
    End Function

    Private Shared Function Ottone(nota As Integer, vel As Double, tipo As Integer) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 0
                                        v.NumeroOscillatori = 1
                                        v.Risonanza = 1.8
                                        v.Sostegno = 0.85
                                        v.Decadimento = 0.15
                                        v.TagliaSostegno = 0.55
                                        v.TagliaTempoApertura = 0.09
                                        v.TagliaVelocitaOttave = 1.6
                                        v.VibratoHz = 5.4
                                        v.VibratoCent = 8.0
                                        v.VibratoRitardo = 0.5
                                        v.SoffioLivello = 0.006
                                        Select Case tipo
                                            Case 0
                                                v.TagliaBaseHz = 900.0
                                                v.TagliaInviluppoOttave = 2.6
                                                v.Attacco = 0.04
                                                v.RilascioSec = 0.1
                                                v.Formanti.Add(FiltroBiquad.Picco(1200.0, 1.5, 6.0))
                                            Case 1
                                                v.TagliaBaseHz = 520.0
                                                v.TagliaInviluppoOttave = 2.2
                                                v.Attacco = 0.06
                                                v.RilascioSec = 0.12
                                                v.Formanti.Add(FiltroBiquad.Picco(500.0, 1.5, 5.0))
                                            Case 2
                                                v.TagliaBaseHz = 480.0
                                                v.TagliaInviluppoOttave = 1.6
                                                v.Attacco = 0.09
                                                v.RilascioSec = 0.15
                                                v.Risonanza = 1.2
                                            Case 3
                                                v.TagliaBaseHz = 300.0
                                                v.TagliaInviluppoOttave = 1.5
                                                v.Attacco = 0.08
                                                v.RilascioSec = 0.14
                                                v.Formanti.Add(FiltroBiquad.Picco(200.0, 1.2, 5.0))
                                            Case Else
                                                v.NumeroOscillatori = 3
                                                v.SpargimentoCent = 12.0
                                                v.TagliaBaseHz = 800.0
                                                v.TagliaInviluppoOttave = 2.4
                                                v.Attacco = 0.06
                                                v.RilascioSec = 0.14
                                                v.Formanti.Add(FiltroBiquad.Picco(1000.0, 1.5, 5.0))
                                        End Select
                                    End Sub)
    End Function

    Private Shared Function Oboe(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.32, Sub(v)
                                         v.Onda = 0
                                         v.NumeroOscillatori = 1
                                         v.TagliaBaseHz = 3200.0
                                         v.TagliaInviluppoOttave = 0.5
                                         v.TagliaSostegno = 0.6
                                         v.Risonanza = 1.5
                                         v.Attacco = 0.04
                                         v.RilascioSec = 0.08
                                         v.VibratoHz = 5.4
                                         v.VibratoCent = 20.0
                                         v.VibratoRitardo = 0.35
                                         v.SoffioLivello = 0.015
                                         v.Formanti.Add(FiltroBiquad.Picco(1300.0, 3.0, 8.0))
                                         v.Formanti.Add(FiltroBiquad.Picco(2800.0, 2.0, 5.0))
                                     End Sub)
    End Function

    Private Shared Function Sassofono(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 1
                                        v.LarghezzaImpulso = 0.42
                                        v.NumeroOscillatori = 2
                                        v.SpargimentoCent = 4.0
                                        v.TagliaBaseHz = 1500.0
                                        v.TagliaInviluppoOttave = 1.2
                                        v.TagliaSostegno = 0.6
                                        v.TagliaTempoApertura = 0.12
                                        v.Risonanza = 1.4
                                        v.Attacco = 0.035
                                        v.RilascioSec = 0.09
                                        v.VibratoHz = 5.2
                                        v.VibratoCent = 16.0
                                        v.VibratoRitardo = 0.4
                                        v.SoffioLivello = 0.03
                                        v.Formanti.Add(FiltroBiquad.Picco(900.0, 2.0, 6.0))
                                    End Sub)
    End Function

    Private Shared Function Coro(nota As Integer, vel As Double, vocaleA As Boolean) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 0
                                        v.NumeroOscillatori = 5
                                        v.SpargimentoCent = 18.0
                                        v.TagliaBaseHz = 3500.0
                                        v.Risonanza = 0.8
                                        v.Attacco = 0.25
                                        v.RilascioSec = 0.4
                                        v.VibratoHz = 5.2
                                        v.VibratoCent = 10.0
                                        v.VibratoRitardo = 0.3
                                        v.SoffioLivello = 0.008
                                        If vocaleA Then
                                            v.Formanti.Add(FiltroBiquad.Picco(800.0, 6.0, 12.0))
                                            v.Formanti.Add(FiltroBiquad.Picco(1150.0, 8.0, 10.0))
                                            v.Formanti.Add(FiltroBiquad.Picco(2900.0, 8.0, 6.0))
                                        Else
                                            v.Formanti.Add(FiltroBiquad.Picco(350.0, 6.0, 12.0))
                                            v.Formanti.Add(FiltroBiquad.Picco(600.0, 8.0, 9.0))
                                            v.Formanti.Add(FiltroBiquad.Picco(2700.0, 8.0, 4.0))
                                        End If
                                    End Sub)
    End Function

    Private Shared Function PadCaldo(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.28, Sub(v)
                                         v.Onda = 0
                                         v.NumeroOscillatori = 5
                                         v.SpargimentoCent = 25.0
                                         v.TagliaBaseHz = 1200.0
                                         v.TagliaInviluppoOttave = 0.8
                                         v.TagliaTempoApertura = 0.6
                                         v.Risonanza = 0.9
                                         v.Attacco = 0.6
                                         v.RilascioSec = 0.9
                                         v.VibratoHz = 4.0
                                         v.VibratoCent = 6.0
                                         v.VibratoRitardo = 0.8
                                     End Sub)
    End Function

    Private Shared Function LeadSega(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 0
                                        v.NumeroOscillatori = 2
                                        v.SpargimentoCent = 8.0
                                        v.TagliaBaseHz = 2800.0
                                        v.TagliaInviluppoOttave = 1.5
                                        v.TagliaTempoApertura = 0.12
                                        v.Risonanza = 1.6
                                        v.Attacco = 0.01
                                        v.RilascioSec = 0.12
                                        v.VibratoHz = 5.5
                                        v.VibratoCent = 10.0
                                        v.VibratoRitardo = 0.4
                                    End Sub)
    End Function

    Private Shared Function LeadQuadro(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.28, Sub(v)
                                        v.Onda = 1
                                        v.LarghezzaImpulso = 0.5
                                        v.NumeroOscillatori = 2
                                        v.SpargimentoCent = 6.0
                                        v.TagliaBaseHz = 2200.0
                                        v.TagliaInviluppoOttave = 1.3
                                        v.TagliaTempoApertura = 0.1
                                        v.Risonanza = 1.4
                                        v.Attacco = 0.01
                                        v.RilascioSec = 0.1
                                        v.VibratoHz = 5.5
                                        v.VibratoCent = 9.0
                                        v.VibratoRitardo = 0.4
                                    End Sub)
    End Function

    Private Shared Function BassoSintetico(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.32, Sub(v)
                                        v.Onda = 0
                                        v.NumeroOscillatori = 2
                                        v.SpargimentoCent = 5.0
                                        v.SottoOttavaLivello = 0.45
                                        v.TagliaBaseHz = 380.0
                                        v.TagliaInviluppoOttave = 2.6
                                        v.TagliaTempoApertura = 0.1
                                        v.TagliaSostegno = 0.15
                                        v.Risonanza = 3.0
                                        v.Attacco = 0.004
                                        v.Sostegno = 0.85
                                        v.Decadimento = 0.2
                                        v.RilascioSec = 0.08
                                    End Sub)
    End Function

    Private Shared Function PizzicatoSintetico(nota As Integer, vel As Double) As VoceSintesi
        Return Sott(nota, vel, 0.3, Sub(v)
                                        v.Onda = 0
                                        v.NumeroOscillatori = 2
                                        v.SpargimentoCent = 7.0
                                        v.TagliaBaseHz = 700.0
                                        v.TagliaInviluppoOttave = 2.8
                                        v.TagliaTempoApertura = 0.12
                                        v.TagliaSostegno = 0.0
                                        v.Risonanza = 2.2
                                        v.Attacco = 0.003
                                        v.Sostegno = 0.0
                                        v.Decadimento = 0.9
                                        v.RilascioSec = 0.08
                                    End Sub)
    End Function

    ' ================================================================================================
    ' batterie e percussioni
    ' ================================================================================================

    Private Shared Function Pr(nome As String) As ProfiloPercussione
        Dim p As New ProfiloPercussione()
        p.Nome = nome
        Return p
    End Function

    Private Shared Function Pezzo(nome As String, spazzata As Double(), rumore As Double(), colpo As Double, Optional toni As Double()() = Nothing, Optional gruppo As Integer = 0, Optional livello As Double = 1.0, Optional pan As Single = 0.0F) As ProfiloPercussione
        Dim p As ProfiloPercussione = Pr(nome)
        p.Spazzata = spazzata
        p.Rumore = rumore
        p.ColpoLivello = colpo
        p.Gruppo = gruppo
        p.Livello = livello
        p.Pan = pan
        If toni IsNot Nothing Then
            For Each t As Double() In toni
                p.Tono(t(0), t(1), t(2))
            Next
        End If
        Return p
    End Function

    Private Shared Function T(freq As Double, ampiezza As Double, t60 As Double) As Double()
        Return New Double() {freq, ampiezza, t60}
    End Function

    ''' <summary>Costruisce l'elenco dei pezzi nell'ordine dei tasti a partire da un dizionario di nomi.</summary>
    Private Shared Function MappaBatteria(pezzi As Dictionary(Of String, ProfiloPercussione), ordine As String()) As List(Of ProfiloPercussione)
        Dim l As New List(Of ProfiloPercussione)()
        For Each nome As String In ordine
            l.Add(pezzi(nome))
        Next
        Return l
    End Function

    Private Shared Function PezziBatteriaAcustica() As Dictionary(Of String, ProfiloPercussione)
        Dim d As New Dictionary(Of String, ProfiloPercussione)()
        d("Cassa") = Pezzo("Cassa", New Double() {170.0, 48.0, 0.03, 1.0, 0.3}, Nothing, 0.45, New Double()() {T(55.0, 0.25, 0.28)}, 0, 1.25)
        d("Cassa secca") = Pezzo("Cassa secca", New Double() {200.0, 58.0, 0.025, 0.9, 0.18}, Nothing, 0.55, Nothing, 0, 1.2)
        d("Rullante") = Pezzo("Rullante", Nothing, New Double() {1.0, 4500.0, 0.7, 0.6, 0.22, 0.0}, 0.3, New Double()() {T(185.0, 0.4, 0.12), T(330.0, 0.2, 0.07)}, 0, 1.0, 0.1F)
        d("Colpo sul bordo") = Pezzo("Colpo sul bordo", Nothing, New Double() {1.0, 3000.0, 1.0, 0.18, 0.05, 0.0}, 0.2, New Double()() {T(400.0, 0.4, 0.07), T(1200.0, 0.25, 0.05)}, 0, 1.0, 0.1F)
        d("Charleston chiuso") = Pezzo("Charleston chiuso", Nothing, New Double() {2.0, 7500.0, 0.8, 0.3, 0.07, 0.0}, 0.1, New Double()() {T(4000.0, 0.1, 0.05), T(6300.0, 0.08, 0.05), T(8900.0, 0.06, 0.04)}, 1, 0.9, 0.25F)
        d("Charleston aperto") = Pezzo("Charleston aperto", Nothing, New Double() {2.0, 7000.0, 0.8, 0.3, 0.9, 0.0}, 0.05, New Double()() {T(4100.0, 0.08, 0.7), T(6500.0, 0.07, 0.6), T(9200.0, 0.05, 0.5)}, 1, 0.9, 0.25F)
        d("Charleston a pedale") = Pezzo("Charleston a pedale", Nothing, New Double() {2.0, 6500.0, 0.8, 0.18, 0.05, 0.0}, 0.08, New Double()() {T(3800.0, 0.06, 0.04)}, 1, 0.9, 0.25F)
        d("Tom alto") = Pezzo("Tom alto", New Double() {240.0, 160.0, 0.05, 0.9, 0.35}, Nothing, 0.3, Nothing, 0, 1.1, -0.15F)
        d("Tom medio") = Pezzo("Tom medio", New Double() {170.0, 110.0, 0.05, 0.9, 0.45}, Nothing, 0.3, Nothing, 0, 1.1, 0.0F)
        d("Tom basso") = Pezzo("Tom basso", New Double() {110.0, 70.0, 0.05, 0.9, 0.6}, Nothing, 0.3, Nothing, 0, 1.15, 0.2F)
        d("Battimani") = Pezzo("Battimani", Nothing, New Double() {1.0, 1200.0, 1.2, 0.7, 0.16, 0.001}, 0.35, Nothing)
        d("Campanaccio") = Pezzo("Campanaccio", Nothing, Nothing, 0.1, New Double()() {T(562.0, 0.35, 0.35), T(845.0, 0.3, 0.28)})
        d("Crash") = Pezzo("Crash", Nothing, New Double() {2.0, 5500.0, 0.8, 0.35, 2.2, 0.0}, 0.15, New Double()() {T(3100.0, 0.07, 1.8), T(4900.0, 0.06, 1.6), T(7200.0, 0.05, 1.4), T(9800.0, 0.04, 1.2)}, 0, 0.9, -0.3F)
        d("Splash") = Pezzo("Splash", Nothing, New Double() {2.0, 7000.0, 0.8, 0.3, 0.8, 0.0}, 0.1, New Double()() {T(4200.0, 0.06, 0.6), T(6800.0, 0.05, 0.5)}, 0, 0.9, -0.2F)
        d("China") = Pezzo("China", Nothing, New Double() {2.0, 4500.0, 0.6, 0.35, 1.4, 0.0}, 0.2, New Double()() {T(2400.0, 0.08, 1.1), T(3900.0, 0.07, 1.0), T(6100.0, 0.05, 0.9)}, 0, 0.9, 0.3F)
        d("Ride") = Pezzo("Ride", Nothing, New Double() {1.0, 8000.0, 0.7, 0.15, 1.6, 0.0}, 0.1, New Double()() {T(2100.0, 0.1, 2.5), T(3300.0, 0.08, 2.2), T(5000.0, 0.06, 1.8)}, 0, 0.8, 0.35F)
        d("Campana del ride") = Pezzo("Campana del ride", Nothing, Nothing, 0.1, New Double()() {T(1900.0, 0.25, 1.5), T(2870.0, 0.18, 1.2), T(4300.0, 0.1, 0.9)}, 0, 0.9, 0.35F)
        d("Tamburello") = Pezzo("Tamburello", Nothing, New Double() {2.0, 6500.0, 0.8, 0.25, 0.25, 0.0}, 0.1, New Double()() {T(7800.0, 0.06, 0.2)})
        d("Blocco di legno") = Pezzo("Blocco di legno", Nothing, Nothing, 0.1, New Double()() {T(1100.0, 0.4, 0.06), T(1650.0, 0.25, 0.05)})
        Return d
    End Function

    Private Shared Function MappaAcustica() As String()
        Return New String() {"Cassa", "Cassa secca", "Rullante", "Colpo sul bordo", "Charleston chiuso", "Charleston aperto", "Charleston a pedale", "Tom alto",
                             "Battimani", "Tom medio", "Campanaccio", "Tom basso", "Crash", "Splash", "Ride", "China", "Campana del ride",
                             "Cassa", "Rullante", "Charleston chiuso", "Charleston aperto", "Tom alto", "Tamburello", "Tom medio", "Crash", "Blocco di legno",
                             "Ride", "Battimani", "Tom basso", "Campana del ride", "Campanaccio", "China"}
    End Function

    Private Shared Function PezziBatteria808() As Dictionary(Of String, ProfiloPercussione)
        Dim d As New Dictionary(Of String, ProfiloPercussione)()
        d("Cassa") = Pezzo("Cassa", New Double() {130.0, 46.0, 0.045, 1.0, 1.2}, Nothing, 0.2, New Double()() {T(46.0, 0.2, 1.1)}, 0, 1.3)
        d("Cassa corta") = Pezzo("Cassa corta", New Double() {150.0, 52.0, 0.03, 1.0, 0.35}, Nothing, 0.3, Nothing, 0, 1.25)
        d("Rullante") = Pezzo("Rullante", Nothing, New Double() {2.0, 2000.0, 0.7, 0.45, 0.2, 0.0}, 0.15, New Double()() {T(180.0, 0.4, 0.12), T(330.0, 0.2, 0.08)})
        d("Battimani") = Pezzo("Battimani", Nothing, New Double() {1.0, 1500.0, 1.0, 0.7, 0.2, 0.001}, 0.4, Nothing)
        d("Colpo sul bordo") = Pezzo("Colpo sul bordo", Nothing, Nothing, 0.1, New Double()() {T(1700.0, 0.3, 0.03), T(500.0, 0.3, 0.04)})
        d("Charleston chiuso") = Pezzo("Charleston chiuso", Nothing, New Double() {2.0, 8000.0, 0.8, 0.3, 0.05, 0.0}, 0.05, New Double()() {T(3200.0, 0.1, 0.04), T(5600.0, 0.09, 0.04), T(7400.0, 0.08, 0.03), T(8600.0, 0.07, 0.03)}, 1, 0.9)
        d("Charleston aperto") = Pezzo("Charleston aperto", Nothing, New Double() {2.0, 7500.0, 0.8, 0.28, 0.5, 0.0}, 0.05, New Double()() {T(3200.0, 0.09, 0.4), T(5600.0, 0.08, 0.4), T(7400.0, 0.07, 0.35), T(8600.0, 0.06, 0.3)}, 1, 0.9)
        d("Tom alto") = Pezzo("Tom alto", New Double() {330.0, 250.0, 0.04, 0.9, 0.3}, Nothing, 0.1)
        d("Tom medio") = Pezzo("Tom medio", New Double() {240.0, 170.0, 0.04, 0.9, 0.38}, Nothing, 0.1)
        d("Tom basso") = Pezzo("Tom basso", New Double() {170.0, 110.0, 0.05, 0.9, 0.5}, Nothing, 0.1)
        d("Campanaccio") = Pezzo("Campanaccio", Nothing, Nothing, 0.05, New Double()() {T(540.0, 0.3, 0.35), T(800.0, 0.3, 0.3)})
        d("Claves") = Pezzo("Claves", Nothing, Nothing, 0.05, New Double()() {T(2500.0, 0.5, 0.08)})
        d("Maracas") = Pezzo("Maracas", Nothing, New Double() {2.0, 7000.0, 0.8, 0.25, 0.09, 0.012}, 0.0, Nothing)
        d("Crash") = Pezzo("Crash", Nothing, New Double() {2.0, 6000.0, 0.8, 0.3, 1.8, 0.0}, 0.1, New Double()() {T(3300.0, 0.07, 1.5), T(5200.0, 0.06, 1.3), T(8000.0, 0.05, 1.0)})
        Return d
    End Function

    Private Shared Function MappaElettronica() As String()
        Return New String() {"Cassa", "Cassa corta", "Rullante", "Colpo sul bordo", "Charleston chiuso", "Charleston aperto", "Battimani", "Tom alto",
                             "Maracas", "Tom medio", "Claves", "Tom basso", "Crash", "Campanaccio", "Charleston chiuso", "Rullante", "Cassa",
                             "Cassa", "Rullante", "Charleston chiuso", "Charleston aperto", "Tom alto", "Battimani", "Tom medio", "Crash", "Claves",
                             "Maracas", "Campanaccio", "Tom basso", "Colpo sul bordo", "Cassa corta", "Rullante"}
    End Function

    Private Shared Function PezziLatini() As Dictionary(Of String, ProfiloPercussione)
        Dim d As New Dictionary(Of String, ProfiloPercussione)()
        d("Conga grave") = Pezzo("Conga grave", New Double() {260.0, 195.0, 0.04, 0.8, 0.3}, New Double() {1.0, 1800.0, 1.0, 0.08, 0.03, 0.0}, 0.2, Nothing, 0, 1.1, -0.2F)
        d("Conga acuta") = Pezzo("Conga acuta", New Double() {370.0, 300.0, 0.035, 0.8, 0.25}, New Double() {1.0, 2200.0, 1.0, 0.08, 0.03, 0.0}, 0.2, Nothing, 0, 1.1, -0.1F)
        d("Conga a schiaffo") = Pezzo("Conga a schiaffo", Nothing, New Double() {1.0, 2600.0, 1.2, 0.5, 0.06, 0.0}, 0.3, New Double()() {T(380.0, 0.3, 0.06)}, 0, 1.0, -0.15F)
        d("Bongo acuto") = Pezzo("Bongo acuto", New Double() {520.0, 440.0, 0.03, 0.8, 0.2}, New Double() {1.0, 3000.0, 1.0, 0.07, 0.025, 0.0}, 0.2, Nothing, 0, 1.0, 0.25F)
        d("Bongo grave") = Pezzo("Bongo grave", New Double() {390.0, 330.0, 0.03, 0.8, 0.22}, New Double() {1.0, 2600.0, 1.0, 0.07, 0.025, 0.0}, 0.2, Nothing, 0, 1.0, 0.2F)
        d("Timbale acuto") = Pezzo("Timbale acuto", Nothing, New Double() {1.0, 3500.0, 1.0, 0.2, 0.08, 0.0}, 0.25, New Double()() {T(700.0, 0.4, 0.35), T(1400.0, 0.2, 0.2), T(2100.0, 0.1, 0.12)})
        d("Timbale grave") = Pezzo("Timbale grave", Nothing, New Double() {1.0, 2800.0, 1.0, 0.2, 0.08, 0.0}, 0.25, New Double()() {T(520.0, 0.4, 0.4), T(1040.0, 0.2, 0.22), T(1560.0, 0.1, 0.12)})
        d("Campanaccio") = Pezzo("Campanaccio", Nothing, Nothing, 0.1, New Double()() {T(562.0, 0.35, 0.35), T(845.0, 0.3, 0.28)})
        d("Claves") = Pezzo("Claves", Nothing, Nothing, 0.05, New Double()() {T(2400.0, 0.5, 0.08), T(3600.0, 0.15, 0.05)})
        d("Maracas") = Pezzo("Maracas", Nothing, New Double() {2.0, 7000.0, 0.8, 0.25, 0.09, 0.015}, 0.0, Nothing, 0, 1.0, -0.3F)
        d("Cabasa") = Pezzo("Cabasa", Nothing, New Double() {2.0, 8500.0, 0.8, 0.22, 0.12, 0.012}, 0.0, Nothing, 0, 1.0, 0.3F)
        d("Shaker") = Pezzo("Shaker", Nothing, New Double() {1.0, 5500.0, 0.8, 0.25, 0.14, 0.02}, 0.0, Nothing, 0, 1.0, 0.0F)
        d("Agogo acuto") = Pezzo("Agogo acuto", Nothing, Nothing, 0.1, New Double()() {T(900.0, 0.35, 0.5), T(1350.0, 0.25, 0.4)})
        d("Agogo grave") = Pezzo("Agogo grave", Nothing, Nothing, 0.1, New Double()() {T(640.0, 0.35, 0.55), T(960.0, 0.25, 0.45)})
        d("Tamburello") = Pezzo("Tamburello", Nothing, New Double() {2.0, 6500.0, 0.8, 0.25, 0.25, 0.0}, 0.1, New Double()() {T(7800.0, 0.06, 0.2)})
        d("Triangolo") = Pezzo("Triangolo", Nothing, Nothing, 0.1, New Double()() {T(4800.0, 0.3, 1.8), T(7200.0, 0.2, 1.5), T(9700.0, 0.1, 1.2)})
        d("Blocco di legno") = Pezzo("Blocco di legno", Nothing, Nothing, 0.1, New Double()() {T(1100.0, 0.4, 0.06), T(1650.0, 0.25, 0.05)})
        d("Battimani") = Pezzo("Battimani", Nothing, New Double() {1.0, 1200.0, 1.2, 0.7, 0.16, 0.001}, 0.35, Nothing)
        Return d
    End Function

    Private Shared Function MappaLatina() As String()
        Return New String() {"Conga grave", "Conga acuta", "Conga a schiaffo", "Bongo acuto", "Bongo grave", "Timbale acuto", "Timbale grave", "Campanaccio",
                             "Claves", "Maracas", "Cabasa", "Shaker", "Agogo acuto", "Agogo grave", "Tamburello", "Triangolo", "Blocco di legno",
                             "Conga grave", "Conga acuta", "Conga a schiaffo", "Bongo acuto", "Bongo grave", "Timbale acuto", "Timbale grave", "Battimani",
                             "Claves", "Maracas", "Cabasa", "Shaker", "Agogo acuto", "Tamburello", "Triangolo"}
    End Function

    Private Shared Function PezziOrchestrali() As Dictionary(Of String, ProfiloPercussione)
        Dim d As New Dictionary(Of String, ProfiloPercussione)()
        d("Timpano grave") = Pezzo("Timpano grave", New Double() {104.0, 88.0, 0.12, 0.9, 1.8}, Nothing, 0.2, New Double()() {T(88.0, 0.3, 2.2), T(131.0, 0.1, 1.2)}, 0, 1.2, -0.25F)
        d("Timpano medio") = Pezzo("Timpano medio", New Double() {130.0, 110.0, 0.12, 0.9, 1.6}, Nothing, 0.2, New Double()() {T(110.0, 0.3, 2.0), T(165.0, 0.1, 1.1)}, 0, 1.2, 0.0F)
        d("Timpano acuto") = Pezzo("Timpano acuto", New Double() {165.0, 140.0, 0.1, 0.9, 1.4}, Nothing, 0.2, New Double()() {T(140.0, 0.3, 1.8), T(210.0, 0.1, 1.0)}, 0, 1.2, 0.25F)
        d("Grancassa") = Pezzo("Grancassa", Nothing, New Double() {0.0, 180.0, 0.7, 0.35, 0.6, 0.0}, 0.3, New Double()() {T(62.0, 0.6, 1.5), T(95.0, 0.2, 0.8)}, 0, 1.3)
        d("Piatti a due") = Pezzo("Piatti a due", Nothing, New Double() {2.0, 4500.0, 0.7, 0.35, 1.8, 0.0}, 0.2, New Double()() {T(2700.0, 0.08, 1.6), T(4200.0, 0.07, 1.4), T(6600.0, 0.06, 1.2), T(9000.0, 0.04, 1.0)}, 0, 0.9)
        d("Tam tam") = Pezzo("Tam tam", Nothing, New Double() {1.0, 900.0, 0.5, 0.25, 4.5, 0.05}, 0.1, New Double()() {T(220.0, 0.2, 4.0), T(380.0, 0.15, 3.5), T(610.0, 0.12, 3.0), T(1100.0, 0.1, 2.5), T(1900.0, 0.07, 2.0)}, 0, 0.9)
        d("Triangolo") = Pezzo("Triangolo", Nothing, Nothing, 0.1, New Double()() {T(4800.0, 0.3, 1.8), T(7200.0, 0.2, 1.5), T(9700.0, 0.1, 1.2)}, 0, 0.9, 0.3F)
        d("Tamburello") = Pezzo("Tamburello", Nothing, New Double() {2.0, 6500.0, 0.8, 0.25, 0.3, 0.0}, 0.1, New Double()() {T(7800.0, 0.06, 0.25)})
        d("Rullante") = Pezzo("Rullante", Nothing, New Double() {1.0, 4000.0, 0.7, 0.5, 0.25, 0.0}, 0.3, New Double()() {T(190.0, 0.35, 0.14)})
        d("Castagnette") = Pezzo("Castagnette", Nothing, New Double() {1.0, 2600.0, 1.5, 0.5, 0.03, 0.0}, 0.2, New Double()() {T(1900.0, 0.3, 0.04)})
        d("Sonagli") = Pezzo("Sonagli", Nothing, New Double() {2.0, 7500.0, 0.8, 0.2, 0.7, 0.0}, 0.0, New Double()() {T(5200.0, 0.06, 0.8), T(6400.0, 0.06, 0.7), T(7600.0, 0.05, 0.7), T(9100.0, 0.04, 0.6)})
        d("Blocco di legno") = Pezzo("Blocco di legno", Nothing, Nothing, 0.1, New Double()() {T(1100.0, 0.4, 0.06), T(1650.0, 0.25, 0.05)})
        d("Campanaccio") = Pezzo("Campanaccio", Nothing, Nothing, 0.1, New Double()() {T(562.0, 0.35, 0.35), T(845.0, 0.3, 0.28)})
        d("Campana") = Pezzo("Campana", Nothing, Nothing, 0.1, New Double()() {T(440.0, 0.3, 3.5), T(880.0, 0.2, 3.0), T(1320.0, 0.15, 2.5), T(1860.0, 0.1, 2.0)})
        Return d
    End Function

    Private Shared Function MappaOrchestrale() As String()
        Return New String() {"Timpano grave", "Grancassa", "Timpano medio", "Rullante", "Timpano acuto", "Piatti a due", "Tam tam", "Triangolo",
                             "Tamburello", "Castagnette", "Sonagli", "Blocco di legno", "Campanaccio", "Campana", "Piatti a due", "Triangolo", "Tamburello",
                             "Timpano grave", "Grancassa", "Timpano medio", "Rullante", "Timpano acuto", "Piatti a due", "Tam tam", "Triangolo",
                             "Tamburello", "Castagnette", "Sonagli", "Blocco di legno", "Campanaccio", "Campana", "Grancassa"}
    End Function

End Class
