' ============================================================================================
' WGSpartiti - gli spartiti letti da file: MusicXML (anche compresso, .mxl) e MIDI
' ============================================================================================
'
' Uno spartito diventa un elenco di parti (una per strumento o per rigo), ciascuna con le sue note già espresse in secondi:
' inizio, durata, altezza e forza. Le ripetizioni e i finali sono già "srotolati", i legami di valore già uniti e i cambi
' di tempo già applicati: chi suona (WGSpartitiRiproduzione) non deve sapere nulla di notazione musicale.
'
' I PDF e le immagini non si leggono qui: prima li riconosce il programma Audiveris (WGSpartitiRiconoscimento), che li
' trasforma in MusicXML, e poi si arriva a questo file.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Xml
Imports System.Xml.Linq
Imports NAudio.Midi

Public NotInheritable Class NotaSpartito
    Public Inizio As Double
    Public Durata As Double
    Public Nota As Integer
    Public Velocita As Double
End Class

Public NotInheritable Class ParteSpartito
    Public Id As String = ""
    Public Nome As String = ""
    Public Programma As Integer
    Public Batteria As Boolean
    ''' <summary>Falso solo per le tracce MIDI che non dicono quale strumento suonano (nessun cambio di programma): lo strumento lo sceglie WinGabriel.</summary>
    Public ProgrammaDichiarato As Boolean = True
    ''' <summary>La traccia del file MIDI da cui viene la parte.</summary>
    Public Traccia As Integer
    Public Note As New List(Of NotaSpartito)()
End Class

Public NotInheritable Class Spartito
    Public Titolo As String = ""
    Public Parti As New List(Of ParteSpartito)()
    ''' <summary>L'istante di inizio di ogni battuta, in secondi.</summary>
    Public Battute As New List(Of Double)()
    ''' <summary>Per ogni battuta suonata, l'indice della battuta scritta (con i ritornelli una battuta scritta si suona più volte).</summary>
    Public OrdineMisure As New List(Of Integer)()
    Public Durata As Double
    ''' <summary>"MIDI" per un file MIDI, vuoto per gli altri.</summary>
    Public Origine As String = ""
    ''' <summary>Quante tracce con note ha il file MIDI.</summary>
    Public TracceMidi As Integer
    ''' <summary>Gli strumenti che WinGabriel ha scelto da solo perché il file MIDI non li dice, una frase per parte.</summary>
    Public ReadOnly Assegnazioni As New List(Of String)()

    Private Shared ReadOnly log As New Logger()

    ''' <summary>Legge un file MusicXML (.xml, .musicxml, .mxl) o MIDI (.mid, .midi).</summary>
    Public Shared Function Carica(percorso As String) As Spartito
        Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
        Dim spartito As Spartito
        Select Case estensione
            Case ".mid", ".midi", ".kar"
                spartito = LettoreMidi.Leggi(percorso)
            Case ".mxl", ".xml", ".musicxml"
                spartito = LettoreMusicXml.Leggi(percorso)
            Case Else
                Throw New InvalidDataException("Il formato " & estensione & " non è uno spartito che so leggere.")
        End Select
        If String.IsNullOrWhiteSpace(spartito.Titolo) Then spartito.Titolo = Path.GetFileNameWithoutExtension(percorso)
        spartito.Parti.RemoveAll(Function(p As ParteSpartito) p.Note.Count = 0)
        If spartito.Parti.Count = 0 Then Throw New InvalidDataException("Nello spartito non ci sono note da suonare.")
        For Each p As ParteSpartito In spartito.Parti
            p.Note.Sort(Function(a As NotaSpartito, b As NotaSpartito) a.Inizio.CompareTo(b.Inizio))
            For Each n As NotaSpartito In p.Note
                spartito.Durata = Math.Max(spartito.Durata, n.Inizio + n.Durata)
            Next
        Next
        If spartito.Battute.Count = 0 Then spartito.Battute.Add(0.0)
        Return spartito
    End Function

    ''' <summary>Il numero della battuta (da 1) in cui cade l'istante indicato.</summary>
    Public Function NumeroBattuta(secondi As Double) As Integer
        Dim n As Integer = 1
        For i As Integer = 0 To Battute.Count - 1
            If Battute(i) <= secondi + 0.0001 Then n = i + 1 Else Exit For
        Next
        Return n
    End Function

    Public Function InizioBattuta(numero As Integer) As Double
        If Battute.Count = 0 Then Return 0.0
        Return Battute(Math.Max(0, Math.Min(Battute.Count - 1, numero - 1)))
    End Function

    ''' <summary>Dal nome della parte (violino, flauto, chitarra...) al numero dello strumento General MIDI.</summary>
    Friend Shared Function ProgrammaDalNome(nome As String) As Integer
        Dim p As Integer = ProgrammaDalNomeNoto(nome)
        Return If(p >= 0, p, 0)
    End Function

    ''' <summary>
    ''' Lo strumento General MIDI (da 0 a 127) che il nome di una parte fa pensare, in italiano, inglese, tedesco o francese
    ''' (anche al plurale: "Flauti", "Violins", "Hörner"); -1 se il nome non è di uno strumento che conosciamo.
    ''' </summary>
    Friend Shared Function ProgrammaDalNomeNoto(nome As String) As Integer
        Dim n As String = If(nome, "").ToLowerInvariant()
        Dim tabella As String()() = {
            New String() {"pianoforte", "0"}, New String() {"klavier", "0"}, New String() {"piano", "0"},
            New String() {"clavicembalo", "6"}, New String() {"harpsichord", "6"}, New String() {"cembalo", "6"}, New String() {"celesta", "8"},
            New String() {"glockenspiel", "9"}, New String() {"vibrafono", "11"}, New String() {"vibraphone", "11"},
            New String() {"marimba", "12"}, New String() {"xilofono", "13"}, New String() {"xylophone", "13"},
            New String() {"organo", "19"}, New String() {"orgel", "19"}, New String() {"orgue", "19"}, New String() {"organ", "19"},
            New String() {"fisarmonica", "21"}, New String() {"accordion", "21"}, New String() {"armonica", "22"},
            New String() {"chitarra elettrica", "27"}, New String() {"electric guitar", "27"}, New String() {"chitarra", "24"}, New String() {"guitar", "24"}, New String() {"gitarre", "24"},
            New String() {"basso elettrico", "33"}, New String() {"electric bass", "33"},
            New String() {"contrabbass", "43"}, New String() {"kontrabass", "43"}, New String() {"contrebass", "43"}, New String() {"double bass", "43"}, New String() {"contrabass", "43"}, New String() {"basses", "43"},
            New String() {"violoncell", "42"}, New String() {"celli", "42"}, New String() {"cello", "42"},
            New String() {"bratsche", "41"}, New String() {"viola", "41"}, New String() {"viole", "41"},
            New String() {"violin", "40"}, New String() {"violon", "40"}, New String() {"geige", "40"},
            New String() {"arpa", "46"}, New String() {"harfe", "46"}, New String() {"harpe", "46"}, New String() {"harp", "46"},
            New String() {"timpan", "47"}, New String() {"pauke", "47"}, New String() {"timbale", "47"},
            New String() {"archi", "48"}, New String() {"strings", "48"}, New String() {"streicher", "48"},
            New String() {"coro", "52"}, New String() {"choir", "52"}, New String() {"chor", "52"}, New String() {"chœur", "52"},
            New String() {"soprano", "52"}, New String() {"tenore", "52"}, New String() {"tenor ", "52"},
            New String() {"voce", "53"}, New String() {"voice", "53"}, New String() {"vocal", "53"},
            New String() {"trombon", "57"}, New String() {"posaune", "57"},
            New String() {"tromb", "56"}, New String() {"trumpet", "56"}, New String() {"trompet", "56"},
            New String() {"tuba", "58"},
            New String() {"corno inglese", "69"}, New String() {"english horn", "69"}, New String() {"cor anglais", "69"}, New String() {"englischhorn", "69"},
            New String() {"hörn", "60"}, New String() {"hoern", "60"}, New String() {"horn", "60"}, New String() {"corn", "60"}, New String() {"cors", "60"},
            New String() {"ottoni", "61"}, New String() {"brass", "61"}, New String() {"blech", "61"},
            New String() {"sassofono soprano", "64"}, New String() {"sassofono contralto", "65"}, New String() {"sassofono tenore", "66"}, New String() {"sassofono baritono", "67"},
            New String() {"sassofon", "65"}, New String() {"saxofon", "65"}, New String() {"saxophon", "65"}, New String() {"sax", "65"},
            New String() {"oboe", "68"}, New String() {"oboi", "68"}, New String() {"hautbois", "68"},
            New String() {"controfagott", "70"}, New String() {"fagott", "70"}, New String() {"bassoon", "70"}, New String() {"basson", "70"},
            New String() {"clarinetto basso", "71"}, New String() {"clarinett", "71"}, New String() {"clarinet", "71"}, New String() {"klarinett", "71"},
            New String() {"ottavin", "72"}, New String() {"piccolo", "72"},
            New String() {"flauto dolce", "74"}, New String() {"recorder", "74"}, New String() {"blockflöte", "74"},
            New String() {"flaut", "73"}, New String() {"flute", "73"}, New String() {"flöt", "73"}, New String() {"floet", "73"}, New String() {"flûte", "73"}, New String() {"flute", "73"},
            New String() {"basso", "32"}, New String() {"bass", "32"}}
        For Each voce As String() In tabella
            If n.Contains(voce(0)) Then Return Integer.Parse(voce(1))
        Next
        Return -1
    End Function

    ''' <summary>
    ''' Di quanti semitoni suona diversamente da come è scritto uno strumento "in si bemolle", "in mi bemolle", "in fa", "in la"...
    ''' ricavato dal nome ("Clarinetti in Si♭", "Corni in Mi♭", "Horns in F"); 0 se il nome non lo dice o lo strumento non traspone.
    ''' </summary>
    Friend Shared Function TrasposizioneDalNome(nome As String) As Integer
        Dim n As String = " " & If(nome, "").ToLowerInvariant().Replace("♭", "b").Replace("♯", "#").Replace(" flat", "b").Replace(" bemolle", "b").Replace(" bémol", "b").Replace(" sharp", "#").Replace(" diesis", "#") & " "
        Dim corno As Boolean = n.Contains("corn") OrElse n.Contains("horn") OrElse n.Contains("cors") OrElse n.Contains("hörn")
        Dim soffiato As Boolean = n.Contains("clarinet") OrElse n.Contains("klarinett") OrElse n.Contains("tromb") OrElse n.Contains("trumpet") OrElse n.Contains("trompet") OrElse n.Contains("cornett")
        If n.Contains("sassofon") OrElse n.Contains("saxofon") OrElse n.Contains("saxophon") OrElse n.Contains(" sax") Then
            If n.Contains("sopran") Then Return -2
            If n.Contains("contralt") OrElse n.Contains("alto") Then Return -9
            If n.Contains("tenor") Then Return -14
            If n.Contains("barit") Then Return -21
        End If
        If n.Contains("ottavin") OrElse n.Contains("piccolo") Then Return 12
        If n.Contains("contrabbass") OrElse n.Contains("kontrabass") OrElse n.Contains("contrebass") OrElse n.Contains("double bass") OrElse n.Contains("contrabass ") OrElse n.Contains(" basses") Then Return -12
        If n.Contains("flauto contralto") OrElse n.Contains("alto flute") Then Return -5
        If Not corno AndAlso Not soffiato Then Return 0
        Dim i As Integer = n.IndexOf(" in ")
        If i < 0 Then i = n.IndexOf(" en ")
        If i < 0 Then i = n.IndexOf(" für ")
        If i < 0 Then Return 0
        Dim resto As String = n.Substring(i).TrimStart().Substring(If(n.Substring(i).TrimStart().StartsWith("für"), 3, 2)).Trim()
        Dim note As String() = {"sol", "do", "re", "mi", "fa", "la", "si", "c", "d", "e", "f", "g", "a", "b", "h"}
        Dim gradi As Integer() = {7, 0, 2, 4, 5, 9, 11, 0, 2, 4, 5, 7, 9, 10, 11}
        Dim grado As Integer = -1
        Dim lunghezza As Integer = 0
        Dim nomeNota As String = ""
        For k As Integer = 0 To note.Length - 1
            If resto.StartsWith(note(k)) Then
                grado = gradi(k)
                lunghezza = note(k).Length
                nomeNota = note(k)
                Exit For
            End If
        Next
        If grado < 0 Then Return 0
        Dim dopo As String = resto.Substring(lunghezza)
        Dim bemolle As Boolean = dopo.StartsWith("b") AndAlso (dopo.Length = 1 OrElse Not Char.IsLetter(dopo(1)))
        If nomeNota = "b" Then
            ' "B" da solo è il si bemolle nell'uso d'orchestra (in tedesco), il si naturale si scrive "H" o "si"
            If dopo.StartsWith("#") Then
                grado = 0
            Else
                grado = 10
            End If
        ElseIf corno AndAlso (nomeNota = "e" OrElse nomeNota = "mi") AndAlso Not bemolle AndAlso Not dopo.StartsWith("#") Then
            ' i corni in mi sono quasi sempre in mi bemolle, e il segno del bemolle spesso si perde nel testo
            grado = 3
        ElseIf bemolle Then
            grado -= 1
        ElseIf dopo.StartsWith("#") Then
            grado += 1
        End If
        grado = ((grado Mod 12) + 12) Mod 12
        If corno Then
            If grado = 0 Then Return 0
            Return grado - 12
        End If
        Return If(grado > 6, grado - 12, grado)
    End Function

    Friend Shared Function ENomeBatteria(nome As String) As Boolean
        Dim n As String = If(nome, "").ToLowerInvariant()
        Return n.Contains("batteria") OrElse n.Contains("percussion") OrElse n.Contains("drum") OrElse n.Contains("rullante") OrElse n.Contains("snare")
    End Function

End Class

' ---------------------------------------------------------------------------------------------
' tempo: dalle posizioni in quarti ai secondi
' ---------------------------------------------------------------------------------------------
Friend NotInheritable Class MappaTempo

    Private ReadOnly _quarti As New List(Of Double)()
    Private ReadOnly _secondi As New List(Of Double)()
    Private ReadOnly _bpm As New List(Of Double)()

    Public Sub New(cambi As List(Of KeyValuePair(Of Double, Double)))
        Dim ordinati As List(Of KeyValuePair(Of Double, Double)) = cambi.OrderBy(Function(c) c.Key).ToList()
        Dim attuale As Double = 120.0
        If ordinati.Count > 0 AndAlso ordinati(0).Key <= 0.0001 Then attuale = ordinati(0).Value
        _quarti.Add(0.0)
        _secondi.Add(0.0)
        _bpm.Add(attuale)
        For Each c As KeyValuePair(Of Double, Double) In ordinati
            If c.Key <= 0.0001 Then Continue For
            Dim ultimo As Integer = _quarti.Count - 1
            Dim sec As Double = _secondi(ultimo) + (c.Key - _quarti(ultimo)) * 60.0 / _bpm(ultimo)
            _quarti.Add(c.Key)
            _secondi.Add(sec)
            _bpm.Add(c.Value)
        Next
    End Sub

    Public Function InSecondi(quarti As Double) As Double
        Dim i As Integer = _quarti.Count - 1
        While i > 0 AndAlso _quarti(i) > quarti
            i -= 1
        End While
        Return _secondi(i) + (quarti - _quarti(i)) * 60.0 / _bpm(i)
    End Function

End Class

' ---------------------------------------------------------------------------------------------
' MusicXML
' ---------------------------------------------------------------------------------------------
Friend NotInheritable Class LettoreMusicXml

    Private NotInheritable Class NotaQuarti
        Public Inizio As Double
        Public Durata As Double
        Public Nota As Integer
        Public Velocita As Double
        Public LegaInizio As Boolean
        Public LegaFine As Boolean
    End Class

    Private NotInheritable Class MisuraParte
        Public Lunghezza As Double
        Public Note As New List(Of NotaQuarti)()
        Public Tempi As New List(Of KeyValuePair(Of Double, Double))()
    End Class

    Private NotInheritable Class MisuraStruttura
        Public RipetizioneInizio As Boolean
        Public RipetizioneFine As Boolean
        Public Volte As Integer = 2
        Public Finale As List(Of Integer)
        Public FineFinale As Boolean
        Public Lunghezza As Double
    End Class

    Private NotInheritable Class ParteInterna
        Public Id As String = ""
        Public Nome As String = ""
        Public Programma As Integer = -1
        Public Canale As Integer
        Public Batteria As Boolean
        Public Misure As New List(Of MisuraParte)()
        Public NotePercussione As New Dictionary(Of String, Integer)()
    End Class

    Private Shared Function Figli(e As XElement, nome As String) As IEnumerable(Of XElement)
        Return e.Elements().Where(Function(x) x.Name.LocalName = nome)
    End Function

    Private Shared Function Figlio(e As XElement, nome As String) As XElement
        If e Is Nothing Then Return Nothing
        Return e.Elements().FirstOrDefault(Function(x) x.Name.LocalName = nome)
    End Function

    Private Shared Function Testo(e As XElement, nome As String) As String
        Dim f As XElement = Figlio(e, nome)
        If f Is Nothing Then Return ""
        Return f.Value.Trim()
    End Function

    Private Shared Function Numero(testo As String, predefinito As Double) As Double
        Dim v As Double
        If Double.TryParse(testo, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v) Then Return v
        Return predefinito
    End Function

    Friend Shared Function CaricaDocumentoPubblico(percorso As String) As XDocument
        Return CaricaDocumento(percorso)
    End Function

    Private Shared Function CaricaDocumento(percorso As String) As XDocument
        Dim impostazioni As New XmlReaderSettings()
        impostazioni.DtdProcessing = DtdProcessing.Ignore
        impostazioni.XmlResolver = Nothing
        If Path.GetExtension(percorso).ToLowerInvariant() = ".mxl" Then
            Using zip As ZipArchive = ZipFile.OpenRead(percorso)
                Dim voce As ZipArchiveEntry = Nothing
                Dim contenitore As ZipArchiveEntry = zip.GetEntry("META-INF/container.xml")
                If contenitore IsNot Nothing Then
                    Using s As Stream = contenitore.Open()
                        Using r As XmlReader = XmlReader.Create(s, impostazioni)
                            Dim cont As XDocument = XDocument.Load(r)
                            Dim radice As XElement = cont.Descendants().FirstOrDefault(Function(x) x.Name.LocalName = "rootfile")
                            If radice IsNot Nothing AndAlso radice.Attribute("full-path") IsNot Nothing Then
                                voce = zip.GetEntry(radice.Attribute("full-path").Value)
                            End If
                        End Using
                    End Using
                End If
                If voce Is Nothing Then
                    voce = zip.Entries.FirstOrDefault(Function(x) x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) AndAlso Not x.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase))
                End If
                If voce Is Nothing Then Throw New InvalidDataException("Il file MusicXML compresso è vuoto.")
                Using s As Stream = voce.Open()
                    Using r As XmlReader = XmlReader.Create(s, impostazioni)
                        Return XDocument.Load(r)
                    End Using
                End Using
            End Using
        End If
        Using r As XmlReader = XmlReader.Create(percorso, impostazioni)
            Return XDocument.Load(r)
        End Using
    End Function

    Private Shared Function Velocita(nomeDinamica As String) As Double
        Select Case nomeDinamica
            Case "pppp" : Return 0.2
            Case "ppp" : Return 0.28
            Case "pp" : Return 0.36
            Case "p" : Return 0.5
            Case "mp" : Return 0.62
            Case "mf" : Return 0.74
            Case "f" : Return 0.86
            Case "ff" : Return 0.95
            Case "fff", "ffff" : Return 1.0
            Case "sf", "sfz", "fz", "fp" : Return 0.95
            Case Else : Return -1.0
        End Select
    End Function

    Private Shared Function SemitonoNota(passo As String) As Integer
        Select Case passo
            Case "C" : Return 0
            Case "D" : Return 2
            Case "E" : Return 4
            Case "F" : Return 5
            Case "G" : Return 7
            Case "A" : Return 9
            Case "B" : Return 11
            Case Else : Return 0
        End Select
    End Function

    Public Shared Function Leggi(percorso As String) As Spartito
        Dim doc As XDocument = CaricaDocumento(percorso)
        Dim radice As XElement = doc.Root
        If radice Is Nothing OrElse radice.Name.LocalName <> "score-partwise" Then
            Throw New InvalidDataException("Questo MusicXML non è in forma 'partwise', che è l'unica che so leggere.")
        End If
        Dim spartito As New Spartito()
        Dim lavoro As XElement = Figlio(radice, "work")
        spartito.Titolo = Testo(lavoro, "work-title")
        If spartito.Titolo = "" Then spartito.Titolo = Testo(radice, "movement-title")

        Dim parti As New List(Of ParteInterna)()
        Dim elencoParti As XElement = Figlio(radice, "part-list")
        If elencoParti IsNot Nothing Then
            For Each sp As XElement In Figli(elencoParti, "score-part")
                Dim p As New ParteInterna()
                p.Id = If(sp.Attribute("id") IsNot Nothing, sp.Attribute("id").Value, "")
                p.Nome = Testo(sp, "part-name")
                If p.Nome = "" Then p.Nome = Testo(Figlio(sp, "score-instrument"), "instrument-name")
                For Each mi As XElement In Figli(sp, "midi-instrument")
                    Dim idStrumento As String = If(mi.Attribute("id") IsNot Nothing, mi.Attribute("id").Value, "")
                    Dim canale As Integer = CInt(Numero(Testo(mi, "midi-channel"), 0))
                    Dim programma As Integer = CInt(Numero(Testo(mi, "midi-program"), 0))
                    Dim senzaNota As Integer = CInt(Numero(Testo(mi, "midi-unpitched"), 0))
                    If senzaNota > 0 Then p.NotePercussione(idStrumento) = senzaNota
                    If p.Programma < 0 Then
                        If canale > 0 Then p.Canale = canale
                        If programma > 0 Then p.Programma = programma - 1
                    End If
                Next
                parti.Add(p)
            Next
        End If

        Dim strutture As New List(Of List(Of MisuraStruttura))()
        For Each parteXml As XElement In Figli(radice, "part")
            Dim id As String = If(parteXml.Attribute("id") IsNot Nothing, parteXml.Attribute("id").Value, "")
            Dim interna As ParteInterna = parti.FirstOrDefault(Function(x) x.Id = id)
            If interna Is Nothing Then
                interna = New ParteInterna()
                interna.Id = id
                interna.Nome = "Strumento " & (parti.Count + 1).ToString()
                parti.Add(interna)
            End If
            Dim stParte As New List(Of MisuraStruttura)()
            LeggiParte(parteXml, interna, stParte, True)
            strutture.Add(stParte)
        Next
        parti.RemoveAll(Function(p) p.Misure.Count = 0)
        If parti.Count = 0 Then Throw New InvalidDataException("Nello spartito non ci sono strumenti.")

        Dim struttura As List(Of MisuraStruttura) = UnisciStrutture(strutture)
        Dim nMisure As Integer = struttura.Count
        For i As Integer = 0 To nMisure - 1
            ' la lunghezza della battuta è quella che dicono quasi tutte le parti: se una sola ha letto male il ritmo,
            ' non deve spostare tutte le altre
            Dim conteggio As New Dictionary(Of Double, Integer)()
            Dim massimo As Double = 0.0
            For Each p As ParteInterna In parti
                If i < p.Misure.Count AndAlso p.Misure(i).Lunghezza > 0.0 Then
                    Dim l As Double = Math.Round(p.Misure(i).Lunghezza, 4)
                    Dim c As Integer = 0
                    conteggio.TryGetValue(l, c)
                    conteggio(l) = c + 1
                    massimo = Math.Max(massimo, l)
                End If
            Next
            Dim scelta As Double = massimo
            Dim voti As Integer = 0
            For Each kv As KeyValuePair(Of Double, Integer) In conteggio
                If kv.Value > voti OrElse (kv.Value = voti AndAlso kv.Key > scelta) Then
                    voti = kv.Value
                    scelta = kv.Key
                End If
            Next
            struttura(i).Lunghezza = If(scelta <= 0.0, 4.0, scelta)
        Next

        Dim ordine As List(Of Integer) = OrdineDiSuonata(struttura)
        spartito.OrdineMisure.AddRange(ordine)
        Dim inizioQuarti As New List(Of Double)()
        Dim totale As Double = 0.0
        For Each indice As Integer In ordine
            inizioQuarti.Add(totale)
            totale += struttura(indice).Lunghezza
        Next

        Dim cambiTempo As New List(Of KeyValuePair(Of Double, Double))()
        Dim viste As New HashSet(Of String)()
        For k As Integer = 0 To ordine.Count - 1
            For Each p As ParteInterna In parti
                Dim indice As Integer = ordine(k)
                If indice >= p.Misure.Count Then Continue For
                For Each t As KeyValuePair(Of Double, Double) In p.Misure(indice).Tempi
                    Dim pos As Double = inizioQuarti(k) + t.Key
                    Dim chiave As String = Math.Round(pos * 1000.0).ToString()
                    If viste.Add(chiave) Then cambiTempo.Add(New KeyValuePair(Of Double, Double)(pos, t.Value))
                Next
            Next
        Next
        Dim mappa As New MappaTempo(cambiTempo)

        For Each q As Double In inizioQuarti
            spartito.Battute.Add(mappa.InSecondi(q))
        Next

        For Each interna As ParteInterna In parti
            Dim parte As New ParteSpartito()
            parte.Id = interna.Id
            parte.Nome = If(interna.Nome = "", "Strumento " & (spartito.Parti.Count + 1).ToString(), interna.Nome)
            parte.Batteria = interna.Batteria OrElse (interna.Canale = 10 AndAlso interna.Programma < 0) OrElse Spartito.ENomeBatteria(interna.Nome)
            Dim daNome As Integer = Spartito.ProgrammaDalNomeNoto(interna.Nome)
            ' il programma 54 (coro "Voice Oohs") è quello che il riconoscimento mette quando non sa lo strumento: se il nome lo sa, vale il nome
            If daNome >= 0 AndAlso (interna.Programma < 0 OrElse interna.Programma = 53) Then
                parte.Programma = daNome
            Else
                parte.Programma = If(interna.Programma >= 0, interna.Programma, Math.Max(0, daNome))
            End If
            Dim assolute As New List(Of NotaQuarti)()
            For k As Integer = 0 To ordine.Count - 1
                Dim indice As Integer = ordine(k)
                If indice >= interna.Misure.Count Then Continue For
                For Each n As NotaQuarti In interna.Misure(indice).Note
                    Dim copia As New NotaQuarti()
                    copia.Inizio = inizioQuarti(k) + n.Inizio
                    copia.Durata = n.Durata
                    copia.Nota = n.Nota
                    copia.Velocita = n.Velocita
                    copia.LegaInizio = n.LegaInizio
                    copia.LegaFine = n.LegaFine
                    assolute.Add(copia)
                Next
            Next
            assolute.Sort(Function(a As NotaQuarti, b As NotaQuarti) a.Inizio.CompareTo(b.Inizio))
            Dim aperte As New Dictionary(Of Integer, NotaQuarti)()
            Dim finali As New List(Of NotaQuarti)()
            For Each n As NotaQuarti In assolute
                Dim aperta As NotaQuarti = Nothing
                If n.LegaFine AndAlso aperte.TryGetValue(n.Nota, aperta) AndAlso Math.Abs(aperta.Inizio + aperta.Durata - n.Inizio) < 0.001 Then
                    aperta.Durata += n.Durata
                    If Not n.LegaInizio Then aperte.Remove(n.Nota)
                Else
                    finali.Add(n)
                    If n.LegaInizio Then aperte(n.Nota) = n Else aperte.Remove(n.Nota)
                End If
            Next
            For Each n As NotaQuarti In finali
                Dim s As New NotaSpartito()
                s.Inizio = mappa.InSecondi(n.Inizio)
                s.Durata = Math.Max(0.03, mappa.InSecondi(n.Inizio + n.Durata) - s.Inizio)
                s.Nota = n.Nota
                s.Velocita = n.Velocita
                parte.Note.Add(s)
            Next
            spartito.Parti.Add(parte)
        Next
        Return spartito
    End Function

    Private Shared Sub LeggiParte(parteXml As XElement, parte As ParteInterna, struttura As List(Of MisuraStruttura), primaParte As Boolean)
        Dim divisioni As Double = 1.0
        Dim velocitaCorrente As Double = 0.74
        Dim trasposizione As Integer = 0
        Dim indiceMisura As Integer = 0
        For Each mx As XElement In Figli(parteXml, "measure")
            Dim m As New MisuraParte()
            Dim s As MisuraStruttura = Nothing
            If primaParte Then
                s = New MisuraStruttura()
                struttura.Add(s)
            ElseIf indiceMisura < struttura.Count Then
                s = struttura(indiceMisura)
            End If
            Dim pos As Double = 0.0
            Dim ultimoInizio As Double = 0.0
            Dim massimo As Double = 0.0
            For Each el As XElement In mx.Elements()
                Select Case el.Name.LocalName
                    Case "attributes"
                        Dim d As XElement = Figlio(el, "divisions")
                        If d IsNot Nothing Then divisioni = Math.Max(1.0, Numero(d.Value, 1.0))
                        Dim tr As XElement = Figlio(el, "transpose")
                        If tr IsNot Nothing Then trasposizione = CInt(Numero(Testo(tr, "chromatic"), 0.0)) + 12 * CInt(Numero(Testo(tr, "octave-change"), 0.0))
                    Case "backup"
                        pos -= Numero(Testo(el, "duration"), 0.0) / divisioni
                        If pos < 0.0 Then pos = 0.0
                    Case "forward"
                        pos += Numero(Testo(el, "duration"), 0.0) / divisioni
                    Case "direction"
                        LeggiDirezione(el, m, pos, velocitaCorrente)
                    Case "sound"
                        LeggiSuono(el, m, pos, velocitaCorrente)
                    Case "barline"
                        If s IsNot Nothing Then LeggiBarra(el, s)
                    Case "note"
                        If Figlio(el, "grace") IsNot Nothing Then Continue For
                        Dim inAccordo As Boolean = Figlio(el, "chord") IsNot Nothing
                        Dim durata As Double = Numero(Testo(el, "duration"), 0.0) / divisioni
                        Dim inizio As Double = If(inAccordo, ultimoInizio, pos)
                        If Figlio(el, "rest") Is Nothing Then
                            Dim nota As Integer = -1
                            Dim altezza As XElement = Figlio(el, "pitch")
                            If altezza IsNot Nothing Then
                                Dim ottava As Integer = CInt(Numero(Testo(altezza, "octave"), 4))
                                Dim alterazione As Double = Numero(Testo(altezza, "alter"), 0.0)
                                nota = 12 * (ottava + 1) + SemitonoNota(Testo(altezza, "step")) + CInt(Math.Round(alterazione)) + trasposizione
                            ElseIf Figlio(el, "unpitched") IsNot Nothing Then
                                parte.Batteria = True
                                Dim idStrumento As String = ""
                                Dim istr As XElement = Figlio(el, "instrument")
                                If istr IsNot Nothing AndAlso istr.Attribute("id") IsNot Nothing Then idStrumento = istr.Attribute("id").Value
                                If Not parte.NotePercussione.TryGetValue(idStrumento, nota) Then nota = 38
                            End If
                            If nota >= 0 AndAlso nota <= 127 Then
                                Dim n As New NotaQuarti()
                                n.Inizio = inizio
                                n.Durata = Math.Max(durata, 0.0625)
                                n.Nota = nota
                                n.Velocita = velocitaCorrente
                                Dim attributoDinamica As XAttribute = el.Attribute("dynamics")
                                If attributoDinamica IsNot Nothing Then n.Velocita = Math.Max(0.15, Math.Min(1.0, Numero(attributoDinamica.Value, 90.0) / 100.0 * 0.82))
                                For Each legame As XElement In Figli(el, "tie")
                                    Dim tipo As String = If(legame.Attribute("type") IsNot Nothing, legame.Attribute("type").Value, "")
                                    If tipo = "start" Then n.LegaInizio = True
                                    If tipo = "stop" Then n.LegaFine = True
                                Next
                                Dim notazioni As XElement = Figlio(el, "notations")
                                If notazioni IsNot Nothing Then
                                    Dim articolazioni As XElement = Figlio(notazioni, "articulations")
                                    If articolazioni IsNot Nothing Then
                                        If Figlio(articolazioni, "staccato") IsNot Nothing OrElse Figlio(articolazioni, "staccatissimo") IsNot Nothing Then n.Durata *= 0.5
                                        If Figlio(articolazioni, "accent") IsNot Nothing OrElse Figlio(articolazioni, "strong-accent") IsNot Nothing Then n.Velocita = Math.Min(1.0, n.Velocita + 0.15)
                                    End If
                                End If
                                m.Note.Add(n)
                            End If
                        End If
                        If Not inAccordo Then
                            ultimoInizio = pos
                            pos += durata
                        End If
                End Select
                If pos > massimo Then massimo = pos
            Next
            m.Lunghezza = massimo
            parte.Misure.Add(m)
            indiceMisura += 1
        Next
    End Sub

    Private Shared Sub LeggiDirezione(direzione As XElement, m As MisuraParte, pos As Double, ByRef velocitaCorrente As Double)
        For Each tipo As XElement In Figli(direzione, "direction-type")
            Dim dinamica As XElement = Figlio(tipo, "dynamics")
            If dinamica IsNot Nothing Then
                For Each d As XElement In dinamica.Elements()
                    Dim v As Double = Velocita(d.Name.LocalName)
                    If v > 0.0 Then velocitaCorrente = v
                Next
            End If
            Dim metronomo As XElement = Figlio(tipo, "metronome")
            If metronomo IsNot Nothing Then
                Dim unita As String = Testo(metronomo, "beat-unit")
                Dim valore As Double = Numero(Testo(metronomo, "per-minute"), 0.0)
                If valore > 0.0 Then
                    Dim quarti As Double = 1.0
                    Select Case unita
                        Case "whole" : quarti = 4.0
                        Case "half" : quarti = 2.0
                        Case "eighth" : quarti = 0.5
                        Case "16th" : quarti = 0.25
                    End Select
                    If Figlio(metronomo, "beat-unit-dot") IsNot Nothing Then quarti *= 1.5
                    m.Tempi.Add(New KeyValuePair(Of Double, Double)(pos, valore * quarti))
                End If
            End If
        Next
        Dim suono As XElement = Figlio(direzione, "sound")
        If suono IsNot Nothing Then LeggiSuono(suono, m, pos, velocitaCorrente)
    End Sub

    Private Shared Sub LeggiSuono(suono As XElement, m As MisuraParte, pos As Double, ByRef velocitaCorrente As Double)
        Dim t As XAttribute = suono.Attribute("tempo")
        If t IsNot Nothing Then
            Dim bpm As Double = Numero(t.Value, 0.0)
            If bpm > 10.0 AndAlso Not m.Tempi.Any(Function(x) Math.Abs(x.Key - pos) < 0.001) Then m.Tempi.Add(New KeyValuePair(Of Double, Double)(pos, bpm))
        End If
        Dim d As XAttribute = suono.Attribute("dynamics")
        If d IsNot Nothing Then velocitaCorrente = Math.Max(0.15, Math.Min(1.0, Numero(d.Value, 90.0) / 100.0 * 0.82))
    End Sub

    Private Shared Sub LeggiBarra(barra As XElement, s As MisuraStruttura)
        Dim rip As XElement = Figlio(barra, "repeat")
        If rip IsNot Nothing Then
            Dim direzione As String = If(rip.Attribute("direction") IsNot Nothing, rip.Attribute("direction").Value, "")
            If direzione = "forward" Then s.RipetizioneInizio = True
            If direzione = "backward" Then
                s.RipetizioneFine = True
                If rip.Attribute("times") IsNot Nothing Then s.Volte = Math.Max(2, CInt(Numero(rip.Attribute("times").Value, 2.0)))
            End If
        End If
        Dim finale As XElement = Figlio(barra, "ending")
        If finale IsNot Nothing AndAlso finale.Attribute("number") IsNot Nothing Then
            Dim tipo As String = If(finale.Attribute("type") IsNot Nothing, finale.Attribute("type").Value, "")
            If tipo = "stop" OrElse tipo = "discontinue" Then s.FineFinale = True
            If tipo = "start" Then
                s.Finale = New List(Of Integer)()
                For Each pezzo As String In finale.Attribute("number").Value.Split(New Char() {","c, " "c}, StringSplitOptions.RemoveEmptyEntries)
                    Dim n As Integer
                    If Integer.TryParse(pezzo.Trim(), n) Then s.Finale.Add(n)
                Next
            End If
        End If
    End Sub

    ''' <summary>
    ''' Ritornelli e finali si leggono in tutte le parti e vale quello che dice la maggioranza: nelle scansioni una parte sola
    ''' può scambiare una legatura o un segno di espressione per una parentesi di finale, e salterebbe battute vere.
    ''' Un finale conta soltanto se chiude con una barra di ritornello (il primo) o segue il primo (gli altri).
    ''' </summary>
    Private Shared Function UnisciStrutture(strutture As List(Of List(Of MisuraStruttura))) As List(Of MisuraStruttura)
        Dim n As Integer = 0
        For Each st As List(Of MisuraStruttura) In strutture
            n = Math.Max(n, st.Count)
        Next
        Dim soglia As Double = strutture.Count / 2.0
        Dim unita As New List(Of MisuraStruttura)()
        For i As Integer = 0 To n - 1
            Dim m As New MisuraStruttura()
            Dim vIn As Integer = 0
            Dim vFine As Integer = 0
            Dim vFineFinale As Integer = 0
            Dim finali As New Dictionary(Of String, Integer)()
            For Each st As List(Of MisuraStruttura) In strutture
                If i >= st.Count Then Continue For
                Dim s As MisuraStruttura = st(i)
                If s.RipetizioneInizio Then vIn += 1
                If s.RipetizioneFine Then
                    vFine += 1
                    m.Volte = Math.Max(m.Volte, s.Volte)
                End If
                If s.FineFinale Then vFineFinale += 1
                If s.Finale IsNot Nothing Then
                    Dim chiave As String = String.Join(",", s.Finale.Select(Function(x) x.ToString()))
                    Dim c As Integer = 0
                    finali.TryGetValue(chiave, c)
                    finali(chiave) = c + 1
                End If
            Next
            m.RipetizioneInizio = vIn > soglia
            m.RipetizioneFine = vFine > soglia
            m.FineFinale = vFineFinale > soglia
            For Each kv As KeyValuePair(Of String, Integer) In finali
                If kv.Value > soglia Then
                    m.Finale = New List(Of Integer)()
                    For Each pezzo As String In kv.Key.Split(","c)
                        Dim x As Integer
                        If Integer.TryParse(pezzo, x) Then m.Finale.Add(x)
                    Next
                End If
            Next
            unita.Add(m)
        Next

        ' i finali che non hanno senso si ignorano
        Dim ultimoFinePrimo As Integer = -10
        Dim i2 As Integer = 0
        While i2 < unita.Count
            If unita(i2).Finale Is Nothing Then
                i2 += 1
                Continue While
            End If
            Dim fine As Integer = -1
            Dim j As Integer = i2
            While j < unita.Count AndAlso j - i2 < 40
                If (j > i2 AndAlso unita(j).Finale IsNot Nothing) Then Exit While
                If unita(j).FineFinale OrElse unita(j).RipetizioneFine Then
                    fine = j
                    Exit While
                End If
                j += 1
            End While
            If fine < 0 Then fine = Math.Min(unita.Count - 1, Math.Max(i2, j - 1))
            Dim valido As Boolean
            If unita(i2).Finale.Contains(1) Then
                valido = unita(fine).RipetizioneFine
                If valido Then ultimoFinePrimo = fine
            Else
                valido = (ultimoFinePrimo = i2 - 1)
            End If
            If Not valido Then
                For k As Integer = i2 To fine
                    unita(k).Finale = Nothing
                    unita(k).FineFinale = False
                Next
            End If
            i2 = fine + 1
        End While
        Return unita
    End Function

    ''' <summary>Dall'ordine delle battute scritte all'ordine in cui si suonano, con ritornelli e finali.</summary>
    Private Shared Function OrdineDiSuonata(struttura As List(Of MisuraStruttura)) As List(Of Integer)
        ' a ogni battuta, i finali (le "volte") a cui appartiene
        Dim volte As New List(Of List(Of Integer))()
        Dim corrente As List(Of Integer) = Nothing
        For i As Integer = 0 To struttura.Count - 1
            Dim s As MisuraStruttura = struttura(i)
            If s.Finale IsNot Nothing Then corrente = s.Finale
            volte.Add(corrente)
            If s.FineFinale OrElse s.RipetizioneFine Then corrente = Nothing
        Next
        Dim ordine As New List(Of Integer)()
        Dim indice As Integer = 0
        Dim partenza As Integer = 0
        Dim passata As Integer = 1
        Dim precedente As List(Of Integer) = Nothing
        Dim sicurezza As Integer = 0
        While indice < struttura.Count AndAlso sicurezza < 100000
            sicurezza += 1
            Dim s As MisuraStruttura = struttura(indice)
            Dim finale As List(Of Integer) = volte(indice)
            If finale IsNot Nothing AndAlso Not finale.Contains(passata) Then
                precedente = finale
                indice += 1
                Continue While
            End If
            If finale Is Nothing AndAlso precedente IsNot Nothing Then
                passata = 1
                partenza = indice
            End If
            precedente = finale
            If s.RipetizioneInizio Then partenza = indice
            ordine.Add(indice)
            If s.RipetizioneFine Then
                If passata < s.Volte Then
                    passata += 1
                    indice = partenza
                    precedente = Nothing
                    Continue While
                End If
                passata = 1
                partenza = indice + 1
            End If
            indice += 1
        End While
        If ordine.Count = 0 Then
            For k As Integer = 0 To struttura.Count - 1
                ordine.Add(k)
            Next
        End If
        Return ordine
    End Function

End Class

' ---------------------------------------------------------------------------------------------
' MIDI
' ---------------------------------------------------------------------------------------------
Friend NotInheritable Class LettoreMidi

    Public Shared Function Leggi(percorso As String) As Spartito
        Dim file As New MidiFile(percorso, False)
        Dim ticksQuarto As Integer = file.DeltaTicksPerQuarterNote
        Dim spartito As New Spartito()

        Dim tempiTick As New SortedDictionary(Of Long, Double)()
        Dim metriche As New SortedDictionary(Of Long, Integer())()
        Dim ultimoTick As Long = 0
        For t As Integer = 0 To file.Tracks - 1
            For Each ev As MidiEvent In file.Events(t)
                ultimoTick = Math.Max(ultimoTick, ev.AbsoluteTime)
                Dim tempo As TempoEvent = TryCast(ev, TempoEvent)
                If tempo IsNot Nothing Then
                    tempiTick(tempo.AbsoluteTime) = 60000000.0 / Math.Max(1.0, CDbl(tempo.MicrosecondsPerQuarterNote))
                    Continue For
                End If
                Dim metrica As TimeSignatureEvent = TryCast(ev, TimeSignatureEvent)
                If metrica IsNot Nothing Then
                    Dim pezzi As String() = metrica.TimeSignature.Split("/"c)
                    Dim num As Integer = 4
                    Dim den As Integer = 4
                    If pezzi.Length = 2 Then
                        Integer.TryParse(pezzi(0), num)
                        Integer.TryParse(pezzi(1), den)
                    End If
                    metriche(metrica.AbsoluteTime) = New Integer() {Math.Max(1, num), Math.Max(1, den)}
                End If
            Next
        Next
        Dim cambiQuarti As New List(Of KeyValuePair(Of Double, Double))()
        For Each kv As KeyValuePair(Of Long, Double) In tempiTick
            cambiQuarti.Add(New KeyValuePair(Of Double, Double)(kv.Key / CDbl(ticksQuarto), kv.Value))
        Next
        Dim mappa As New MappaTempo(cambiQuarti)

        Dim gruppi As New Dictionary(Of String, ParteSpartito)()
        Dim ordineGruppi As New List(Of String)()
        For t As Integer = 0 To file.Tracks - 1
            Dim nomeTraccia As String = ""
            Dim programmi As New Dictionary(Of Integer, Integer)()
            For Each ev As MidiEvent In file.Events(t)
                Dim testo As TextEvent = TryCast(ev, TextEvent)
                If testo IsNot Nothing AndAlso testo.MetaEventType = MetaEventType.SequenceTrackName AndAlso nomeTraccia = "" Then nomeTraccia = testo.Text.Trim()
                Dim patch As PatchChangeEvent = TryCast(ev, PatchChangeEvent)
                If patch IsNot Nothing AndAlso Not programmi.ContainsKey(patch.Channel) Then programmi(patch.Channel) = patch.Patch
                Dim nota As NoteOnEvent = TryCast(ev, NoteOnEvent)
                If nota IsNot Nothing AndAlso nota.Velocity > 0 AndAlso nota.OffEvent IsNot Nothing Then
                    Dim chiave As String = t.ToString() & "/" & nota.Channel.ToString()
                    Dim parte As ParteSpartito = Nothing
                    If Not gruppi.TryGetValue(chiave, parte) Then
                        parte = New ParteSpartito()
                        parte.Batteria = nota.Channel = 10
                        parte.Programma = If(programmi.ContainsKey(nota.Channel), programmi(nota.Channel), 0)
                        parte.ProgrammaDichiarato = programmi.ContainsKey(nota.Channel) OrElse parte.Batteria
                        parte.Traccia = t
                        parte.Nome = nomeTraccia
                        gruppi(chiave) = parte
                        ordineGruppi.Add(chiave)
                    End If
                    Dim inizio As Double = mappa.InSecondi(nota.AbsoluteTime / CDbl(ticksQuarto))
                    Dim fine As Double = mappa.InSecondi(nota.OffEvent.AbsoluteTime / CDbl(ticksQuarto))
                    Dim n As New NotaSpartito()
                    n.Inizio = inizio
                    n.Durata = Math.Max(0.03, fine - inizio)
                    n.Nota = nota.NoteNumber
                    n.Velocita = Math.Max(0.1, Math.Min(1.0, nota.Velocity / 127.0))
                    parte.Note.Add(n)
                End If
            Next
        Next
        Dim numero As Integer = 0
        For Each chiave As String In ordineGruppi
            Dim parte As ParteSpartito = gruppi(chiave)
            numero += 1
            If parte.Nome = "" Then parte.Nome = If(parte.Batteria, "Batteria", "Strumento " & numero.ToString())
            spartito.Parti.Add(parte)
        Next
        spartito.Origine = "MIDI"
        spartito.TracceMidi = spartito.Parti.Select(Function(p As ParteSpartito) p.Traccia).Distinct().Count()
        AssegnaStrumenti(spartito)

        ' le battute, dalle indicazioni di tempo in chiave
        Dim tickBattuta As Long = 0
        Dim num2 As Integer = 4
        Dim den2 As Integer = 4
        Dim prossimaMetrica As IEnumerator(Of KeyValuePair(Of Long, Integer())) = metriche.GetEnumerator()
        Dim haMetrica As Boolean = prossimaMetrica.MoveNext()
        Dim sicurezza As Integer = 0
        While tickBattuta <= ultimoTick AndAlso sicurezza < 100000
            sicurezza += 1
            While haMetrica AndAlso prossimaMetrica.Current.Key <= tickBattuta
                num2 = prossimaMetrica.Current.Value(0)
                den2 = prossimaMetrica.Current.Value(1)
                haMetrica = prossimaMetrica.MoveNext()
            End While
            spartito.Battute.Add(mappa.InSecondi(tickBattuta / CDbl(ticksQuarto)))
            tickBattuta += CLng(ticksQuarto * 4.0 * num2 / den2)
        End While
        Return spartito
    End Function

    Private Shared Function MedianaNote(parte As ParteSpartito) As Integer
        Dim note As List(Of Integer) = parte.Note.Select(Function(n As NotaSpartito) n.Nota).OrderBy(Function(x As Integer) x).ToList()
        Return If(note.Count = 0, 60, note(note.Count \ 2))
    End Function

    ''' <summary>Lo strumento che sta bene a una parte che suona intorno a quella nota MIDI, per chi ha poche parti.</summary>
    Private Shared Function ProgrammaPerRegistro(mediana As Integer) As Integer
        If mediana >= 76 Then Return 73
        If mediana >= 67 Then Return 40
        If mediana >= 57 Then Return 41
        If mediana >= 45 Then Return 42
        Return 43
    End Function

    ''' <summary>
    ''' Un file MIDI non dice sempre quale strumento suona ogni traccia: se non c'è il cambio di programma, lo strumento lo sceglie WinGabriel.
    ''' Prima dal nome della traccia ("Flute", "Violins 2", "Corni"); poi dal registro: con poche parti la più acuta ai violini, la più
    ''' grave ai violoncelli; con molte parti, dall'acuto al grave, flauti, oboi, clarinetti, violini, viole, corni, violoncelli, contrabbassi.
    ''' Un file con una sola parte suona con il pianoforte. Le percussioni e le tracce che dichiarano lo strumento non si toccano.
    ''' </summary>
    Private Shared Sub AssegnaStrumenti(spartito As Spartito)
        Dim melodiche As List(Of ParteSpartito) = spartito.Parti.Where(Function(p As ParteSpartito) Not p.Batteria).ToList()
        Dim senza As List(Of ParteSpartito) = melodiche.Where(Function(p As ParteSpartito) Not p.ProgrammaDichiarato).ToList()
        If senza.Count = 0 Then Return
        Dim daRegistro As New List(Of ParteSpartito)()
        For Each p As ParteSpartito In senza
            Dim dalNome As Integer = Spartito.ProgrammaDalNomeNoto(p.Nome)
            If dalNome >= 0 Then
                p.Programma = dalNome
                spartito.Assegnazioni.Add(p.Nome & ": " & CatalogoSoundFont.NomeProgramma(dalNome).ToLowerInvariant() & ", dal nome")
            Else
                daRegistro.Add(p)
            End If
        Next
        If daRegistro.Count = 0 Then Return
        If melodiche.Count = 1 Then
            daRegistro(0).Programma = 0
            spartito.Assegnazioni.Add(daRegistro(0).Nome & ": pianoforte, perché è l'unico strumento")
            Return
        End If
        Dim ordinate As List(Of ParteSpartito) = daRegistro.OrderByDescending(Function(p As ParteSpartito) MedianaNote(p)).ToList()
        Dim scala As Integer() = {73, 68, 71, 40, 41, 60, 42, 43}
        For i As Integer = 0 To ordinate.Count - 1
            Dim programma As Integer
            If ordinate.Count <= 4 Then
                programma = ProgrammaPerRegistro(MedianaNote(ordinate(i)))
            Else
                programma = scala(Math.Min(scala.Length - 1, CInt(Math.Floor(i * scala.Length / CDbl(ordinate.Count)))))
            End If
            ordinate(i).Programma = programma
            spartito.Assegnazioni.Add(ordinate(i).Nome & ": " & CatalogoSoundFont.NomeProgramma(programma).ToLowerInvariant() & ", dal registro")
        Next
    End Sub

End Class
