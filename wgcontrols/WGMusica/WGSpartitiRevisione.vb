' ============================================================================================
' WGSpartitiRevisione - controllare e correggere uno spartito MusicXML
' ============================================================================================
'
' Nessun programma riconosce una partitura d'orchestra senza sbagliare mai: qualche nota o durata resta sbagliata. Chi non
' vede lo spartito non può accorgersene a occhio, e chi non ha l'orecchio assoluto non può accorgersene ascoltando. Per questo
' la revisione non si affida all'orecchio: il programma controlla da solo quello che si può controllare con certezza.
'   - la durata: in ogni battuta e in ogni voce le note devono riempire esattamente il tempo della battuta;
'   - l'estensione: ogni strumento ha una nota più grave e una più acuta; fuori da quel campo, è un errore di lettura;
'   - le alterazioni: una nota fuori dalla tonalità, senza diesis o bemolle scritto, è quasi sempre un'alterazione perduta;
'   - i salti: più di due ottave fra una nota e la successiva nella stessa voce sono quasi sempre un errore di ottava.
' Ogni dubbio diventa una voce di un elenco: si apre la battuta, si leggono le note una per una, si corregge. Lo spartito
' corretto si salva e si può esportare in MusicXML.
'
' Quello che il controllo non può trovare: una nota sbagliata che però ha la durata giusta, sta nella tonalità e nell'estensione.
' Per quelle l'unica verifica indipendente è il confronto con la pagina stampata, o con il risultato di un secondo programma.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports System.Xml.Linq

Friend NotInheritable Class NotaInfo
    Public El As XElement
    Public Voce As Integer = 1
    Public Rigo As Integer = 1
    Public Inizio As Integer
    Public Durata As Integer
    Public Accordo As Boolean
    Public Pausa As Boolean
    Public Grazia As Boolean
    Public SenzaAltezza As Boolean
    Public Passo As String = ""
    Public Alter As Integer
    Public Ottava As Integer
    ''' <summary>La nota MIDI che si sente (con la trasposizione dello strumento); -1 per le pause.</summary>
    Public Midi As Integer = -1
End Class

Friend NotInheritable Class StatoMisura
    Public Divisioni As Integer = 1
    Public Battiti As Integer = 4
    Public Valore As Integer = 4
    Public Quinte As Integer = 0
    Public Trasposizione As Integer = 0

    Public Function Copia() As StatoMisura
        Return DirectCast(MemberwiseClone(), StatoMisura)
    End Function
End Class

Friend NotInheritable Class MisuraInfo
    Public Indice As Integer
    Public Numero As String = ""
    Public Elemento As XElement
    Public Stato As StatoMisura
    Public Note As New List(Of NotaInfo)()
    Public Somme As New Dictionary(Of String, Integer)()
    Public Implicita As Boolean
    Public Attesa As Integer
End Class

Public NotInheritable Class ProblemaSpartito
    Public Parte As Integer
    Public Misura As Integer
    Public Numero As String = ""
    Public Testo As String = ""
End Class

Public NotInheritable Class RevisioneMusicXml

    Private Shared ReadOnly NomiPassi() As String = {"C", "D", "E", "F", "G", "A", "B"}
    Private Shared ReadOnly NomiItaliani() As String = {"Do", "Re", "Mi", "Fa", "Sol", "La", "Si"}
    Private Shared ReadOnly OrdineDiesis() As String = {"F", "C", "G", "D", "A", "E", "B"}
    Private Shared ReadOnly OrdineBemolle() As String = {"B", "E", "A", "D", "G", "C", "F"}

    Public NotInheritable Class ParteXml
        Public Id As String = ""
        Public Nome As String = ""
        Public Elemento As XElement
    End Class

    Private _doc As XDocument
    Public ReadOnly Parti As New List(Of ParteXml)()

    Private Sub New(doc As XDocument)
        _doc = doc
        Ricostruisci()
    End Sub

    ''' <summary>Lo spartito com'è adesso, come testo: serve a tornare indietro dopo la correzione automatica.</summary>
    Public Function Istantanea() As String
        Return _doc.ToString(SaveOptions.DisableFormatting)
    End Function

    Public Sub Ripristina(istantanea As String)
        _doc = XDocument.Parse(istantanea)
        Ricostruisci()
    End Sub

    Private Sub Ricostruisci()
        Parti.Clear()
        Dim radice As XElement = _doc.Root
        Dim nomi As New Dictionary(Of String, String)()
        Dim elenco As XElement = radice.Element("part-list")
        If elenco IsNot Nothing Then
            For Each sp As XElement In elenco.Elements("score-part")
                Dim id As String = If(sp.Attribute("id") IsNot Nothing, sp.Attribute("id").Value, "")
                Dim nome As String = If(sp.Element("part-name") IsNot Nothing, sp.Element("part-name").Value.Trim(), "")
                nomi(id) = nome
            Next
        End If
        For Each p As XElement In radice.Elements("part")
            Dim parte As New ParteXml()
            parte.Elemento = p
            parte.Id = If(p.Attribute("id") IsNot Nothing, p.Attribute("id").Value, "")
            Dim nome As String = ""
            nomi.TryGetValue(parte.Id, nome)
            parte.Nome = If(String.IsNullOrWhiteSpace(nome), "Strumento " & (Parti.Count + 1).ToString(), nome)
            Parti.Add(parte)
        Next
    End Sub

    Public Shared Function Apri(percorso As String) As RevisioneMusicXml
        Dim doc As XDocument = LettoreMusicXml.CaricaDocumentoPubblico(percorso)
        If doc.Root Is Nothing OrElse doc.Root.Name.LocalName <> "score-partwise" Then
            Throw New InvalidDataException("Questo MusicXML non è in forma 'partwise'.")
        End If
        Return New RevisioneMusicXml(doc)
    End Function

    ' ---- lettura -----------------------------------------------------------------------------------------

    Private Shared Function Intero(e As XElement, nome As String, predefinito As Integer) As Integer
        If e Is Nothing Then Return predefinito
        Dim f As XElement = e.Element(nome)
        If f Is Nothing Then Return predefinito
        Dim v As Integer
        If Integer.TryParse(f.Value.Trim(), v) Then Return v
        Return predefinito
    End Function

    Private Shared Function SemitonoPasso(passo As String) As Integer
        Select Case passo
            Case "C" : Return 0
            Case "D" : Return 2
            Case "E" : Return 4
            Case "F" : Return 5
            Case "G" : Return 7
            Case "A" : Return 9
            Case "B" : Return 11
        End Select
        Return 0
    End Function

    Public Shared Function AlterazioneInChiave(passo As String, quinte As Integer) As Integer
        If quinte > 0 Then
            For i As Integer = 0 To Math.Min(7, quinte) - 1
                If OrdineDiesis(i) = passo Then Return 1
            Next
        ElseIf quinte < 0 Then
            For i As Integer = 0 To Math.Min(7, -quinte) - 1
                If OrdineBemolle(i) = passo Then Return -1
            Next
        End If
        Return 0
    End Function

    Private Shared Sub AggiornaStato(attributi As XElement, stato As StatoMisura)
        Dim d As XElement = attributi.Element("divisions")
        If d IsNot Nothing Then stato.Divisioni = Math.Max(1, Intero(attributi, "divisions", 1))
        Dim chiave As XElement = attributi.Element("key")
        If chiave IsNot Nothing Then stato.Quinte = Intero(chiave, "fifths", stato.Quinte)
        Dim tempo As XElement = attributi.Element("time")
        If tempo IsNot Nothing Then
            Dim b As Integer = 0
            Dim v As Integer = 0
            Dim sb As String = If(tempo.Element("beats") IsNot Nothing, tempo.Element("beats").Value.Trim(), "")
            Integer.TryParse(sb.Split("+"c)(0), b)
            If sb.Contains("+") Then
                b = 0
                For Each pezzo As String In sb.Split("+"c)
                    Dim n As Integer
                    If Integer.TryParse(pezzo, n) Then b += n
                Next
            End If
            Integer.TryParse(If(tempo.Element("beat-type") IsNot Nothing, tempo.Element("beat-type").Value.Trim(), "4"), v)
            If b > 0 AndAlso v > 0 Then
                stato.Battiti = b
                stato.Valore = v
            End If
        End If
        Dim tr As XElement = attributi.Element("transpose")
        If tr IsNot Nothing Then stato.Trasposizione = Intero(tr, "chromatic", 0) + 12 * Intero(tr, "octave-change", 0)
    End Sub

    Friend Function Analizza(indiceParte As Integer) As List(Of MisuraInfo)
        Dim risultato As New List(Of MisuraInfo)()
        Dim stato As New StatoMisura()
        Dim k As Integer = 0
        For Each m As XElement In Parti(indiceParte).Elemento.Elements("measure")
            Dim mi As New MisuraInfo()
            mi.Indice = k
            mi.Elemento = m
            mi.Numero = If(m.Attribute("number") IsNot Nothing, m.Attribute("number").Value, (k + 1).ToString())
            mi.Implicita = m.Attribute("implicit") IsNot Nothing AndAlso m.Attribute("implicit").Value = "yes"
            Dim pos As Integer = 0
            Dim ultimoInizio As Integer = 0
            For Each el As XElement In m.Elements()
                Select Case el.Name.LocalName
                    Case "attributes"
                        AggiornaStato(el, stato)
                    Case "backup"
                        pos = Math.Max(0, pos - Intero(el, "duration", 0))
                    Case "forward"
                        Dim dur As Integer = Intero(el, "duration", 0)
                        pos += dur
                        Dim chiave As String = Intero(el, "staff", 1).ToString() & "/" & Intero(el, "voice", 1).ToString()
                        Dim s As Integer = 0
                        mi.Somme.TryGetValue(chiave, s)
                        mi.Somme(chiave) = s + dur
                    Case "note"
                        Dim n As New NotaInfo()
                        n.El = el
                        n.Grazia = el.Element("grace") IsNot Nothing
                        n.Accordo = el.Element("chord") IsNot Nothing
                        n.Pausa = el.Element("rest") IsNot Nothing
                        n.Durata = If(n.Grazia, 0, Intero(el, "duration", 0))
                        n.Voce = Intero(el, "voice", 1)
                        n.Rigo = Intero(el, "staff", 1)
                        n.Inizio = If(n.Accordo, ultimoInizio, pos)
                        Dim altezza As XElement = el.Element("pitch")
                        If altezza IsNot Nothing Then
                            n.Passo = If(altezza.Element("step") IsNot Nothing, altezza.Element("step").Value.Trim(), "C")
                            Dim a As Double = 0.0
                            If altezza.Element("alter") IsNot Nothing Then Double.TryParse(altezza.Element("alter").Value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, a)
                            n.Alter = CInt(Math.Round(a))
                            n.Ottava = Intero(altezza, "octave", 4)
                            n.Midi = 12 * (n.Ottava + 1) + SemitonoPasso(n.Passo) + n.Alter + stato.Trasposizione
                        ElseIf Not n.Pausa Then
                            n.SenzaAltezza = True
                        End If
                        mi.Note.Add(n)
                        If Not n.Accordo AndAlso Not n.Grazia Then
                            ultimoInizio = pos
                            pos += n.Durata
                            Dim chiave As String = n.Rigo.ToString() & "/" & n.Voce.ToString()
                            Dim s As Integer = 0
                            mi.Somme.TryGetValue(chiave, s)
                            mi.Somme(chiave) = s + n.Durata
                        End If
                End Select
            Next
            mi.Stato = stato.Copia()
            mi.Attesa = stato.Divisioni * 4 * stato.Battiti \ Math.Max(1, stato.Valore)
            risultato.Add(mi)
            k += 1
        Next
        Return risultato
    End Function

    ' ---- il controllo -----------------------------------------------------------------------------------

    Private Shared Function QuartiTesto(durata As Integer, divisioni As Integer) As String
        Dim q As Double = durata / CDbl(Math.Max(1, divisioni))
        Dim t As String = q.ToString("0.##", Globalization.CultureInfo.GetCultureInfo("it-IT"))
        Return t & If(Math.Abs(q - 1.0) < 0.001, " quarto", " quarti")
    End Function

    Private Shared Function IntervalloStrumento(nome As String, ByRef basso As Integer, ByRef alto As Integer) As Boolean
        Dim n As String = If(nome, "").ToLowerInvariant()
        Dim tabella As Object()() = {
            New Object() {"ottavino", 74, 108}, New Object() {"piccolo", 74, 108},
            New Object() {"flaut", 59, 97}, New Object() {"flute", 59, 97},
            New Object() {"corno inglese", 52, 82}, New Object() {"english horn", 52, 82},
            New Object() {"oboe", 58, 92}, New Object() {"oboi", 58, 92}, New Object() {"fagott", 34, 76}, New Object() {"bassoon", 34, 76},
            New Object() {"clarinett", 50, 95}, New Object() {"clarinet", 50, 95},
            New Object() {"sassofono", 44, 82}, New Object() {"sax", 44, 82},
            New Object() {"tromb", 52, 88}, New Object() {"trumpet", 52, 88},
            New Object() {"trombon", 38, 73}, New Object() {"tuba", 26, 60},
            New Object() {"corn", 33, 79}, New Object() {"horn", 33, 79},
            New Object() {"timpani", 38, 58},
            New Object() {"violino", 54, 104}, New Object() {"violin", 54, 104},
            New Object() {"viola", 47, 90}, New Object() {"violoncell", 35, 83}, New Object() {"celli", 35, 83}, New Object() {"cello", 35, 83},
            New Object() {"contrabbass", 27, 69}, New Object() {"double bass", 27, 69}, New Object() {"contrebasse", 27, 69},
            New Object() {"arpa", 23, 104}, New Object() {"harp", 23, 104},
            New Object() {"pianoforte", 20, 109}, New Object() {"piano", 20, 109},
            New Object() {"soprano", 58, 86}, New Object() {"contralto", 52, 79}, New Object() {"tenore", 46, 74}, New Object() {"tenor", 46, 74},
            New Object() {"basso", 38, 66}}
        For Each voce As Object() In tabella
            If n.Contains(CStr(voce(0))) Then
                basso = CInt(voce(1))
                alto = CInt(voce(2))
                Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>I dubbi che vengono da fuori: dal confronto con l'immagine della pagina o con un'altra lettura dello stesso spartito.</summary>
    Public ReadOnly DubbiEsterni As New List(Of DubbioMisura)()
    ''' <summary>Le battute (chiave come in RisultatoVerifica.Chiave) in cui le altezze coincidono con quelle dei caratteri del PDF: un salto grande o una nota fuori estensione sono veri e non si segnalano.</summary>
    Public ReadOnly MisureVerificate As New HashSet(Of Long)()

    Public Function NumeroMisura(indiceParte As Integer, indiceMisura As Integer) As String
        Dim m As XElement = Parti(indiceParte).Elemento.Elements("measure").ElementAtOrDefault(indiceMisura)
        If m IsNot Nothing AndAlso m.Attribute("number") IsNot Nothing Then Return m.Attribute("number").Value
        Return (indiceMisura + 1).ToString()
    End Function

    ''' <summary>L'elenco del controllo: i problemi che si trovano dalle note e i dubbi venuti da fuori.</summary>
    Public Function Problemi() As List(Of ProblemaSpartito)
        Dim tutti As List(Of ProblemaSpartito) = ProblemiInterni()
        If MisureVerificate.Count > 0 Then
            tutti.RemoveAll(Function(x) MisureVerificate.Contains(RisultatoVerifica.Chiave(x.Parte, x.Misura)) AndAlso (x.Testo.Contains("salto di più di due ottave") OrElse x.Testo.Contains("fuori dall'estensione dello strumento")))
        End If
        For Each d As DubbioMisura In DubbiEsterni
            If d.Parte < 0 OrElse d.Parte >= Parti.Count OrElse d.Misura < 0 OrElse d.Misura >= Misure(d.Parte) Then Continue For
            Dim pr As New ProblemaSpartito()
            pr.Parte = d.Parte
            pr.Misura = d.Misura
            pr.Numero = NumeroMisura(d.Parte, d.Misura)
            pr.Testo = "Battuta " & pr.Numero & ", " & Parti(d.Parte).Nome & ": " & d.Motivo
            tutti.Add(pr)
        Next
        Dim viste As New HashSet(Of String)()
        Dim unici As New List(Of ProblemaSpartito)()
        For Each pr As ProblemaSpartito In tutti
            If viste.Add(pr.Testo) Then unici.Add(pr)
        Next
        Return unici.OrderBy(Function(x) x.Misura).ThenBy(Function(x) x.Parte).Take(500).ToList()
    End Function

    Friend Function ProblemiInterni() As List(Of ProblemaSpartito)
        Dim elenco As New List(Of ProblemaSpartito)()
        For p As Integer = 0 To Parti.Count - 1
            Dim misure As List(Of MisuraInfo) = Analizza(p)
            Dim basso As Integer = 0
            Dim alto As Integer = 0
            Dim haIntervallo As Boolean = IntervalloStrumento(Parti(p).Nome, basso, alto)
            Dim ultimaNotaPerVoce As New Dictionary(Of String, Integer)()
            Dim parte As Integer = p   ' copia per la funzione qui sotto: la variabile del ciclo non va catturata
            For Each mi As MisuraInfo In misure
                Dim misura As MisuraInfo = mi
                Dim aggiungi As Action(Of String) = Sub(testo As String)
                                                        Dim pr As New ProblemaSpartito()
                                                        pr.Parte = parte
                                                        pr.Misura = misura.Indice
                                                        pr.Numero = misura.Numero
                                                        pr.Testo = "Battuta " & misura.Numero & ", " & Parti(parte).Nome & ": " & testo
                                                        elenco.Add(pr)
                                                    End Sub
                ' la durata delle voci
                Dim primaOUltima As Boolean = (mi.Indice = 0 OrElse mi.Indice = misure.Count - 1)
                If mi.Attesa > 0 AndAlso Not mi.Implicita Then
                    For Each kv As KeyValuePair(Of String, Integer) In mi.Somme
                        If kv.Value = mi.Attesa Then Continue For
                        If primaOUltima AndAlso kv.Value < mi.Attesa Then Continue For
                        Dim numeroVoce As String = kv.Key.Split("/"c)(1)
                        If kv.Value > mi.Attesa Then
                            aggiungi("la voce " & numeroVoce & " dura " & QuartiTesto(kv.Value, mi.Stato.Divisioni) & " invece di " & QuartiTesto(mi.Attesa, mi.Stato.Divisioni) & ", è troppo lunga")
                        Else
                            aggiungi("la voce " & numeroVoce & " dura " & QuartiTesto(kv.Value, mi.Stato.Divisioni) & " invece di " & QuartiTesto(mi.Attesa, mi.Stato.Divisioni) & ", è troppo corta")
                        End If
                    Next
                End If
                ' l'estensione, le alterazioni e i salti
                Dim alterazioniViste As New Dictionary(Of String, Integer)()
                For Each n As NotaInfo In mi.Note
                    If n.Pausa OrElse n.SenzaAltezza OrElse n.Passo = "" Then Continue For
                    Dim chiaveNota As String = n.Passo & n.Ottava.ToString()
                    If haIntervallo AndAlso (n.Midi < basso - 2 OrElse n.Midi > alto + 2) Then
                        aggiungi("la nota " & NomeNota(n.Passo, n.Alter, n.Ottava) & " è fuori dall'estensione dello strumento")
                    End If
                    If n.El.Element("accidental") Is Nothing AndAlso n.El.Element("tie") Is Nothing Then
                        Dim previsto As Integer = AlterazioneInChiave(n.Passo, mi.Stato.Quinte)
                        Dim vista As Integer = 0
                        Dim notaPrecedente As Boolean = alterazioniViste.TryGetValue(chiaveNota, vista)
                        If notaPrecedente Then previsto = vista
                        If n.Alter <> previsto Then
                            aggiungi("la nota " & NomeNota(n.Passo, n.Alter, n.Ottava) & " è fuori dalla tonalità e non ha il segno di alterazione: forse è un'alterazione non riconosciuta")
                        End If
                    End If
                    alterazioniViste(chiaveNota) = n.Alter
                    If Not n.Accordo AndAlso Not n.Grazia Then
                        Dim chiaveVoce As String = n.Rigo.ToString() & "/" & n.Voce.ToString()
                        Dim precedente As Integer = 0
                        If ultimaNotaPerVoce.TryGetValue(chiaveVoce, precedente) AndAlso Math.Abs(n.Midi - precedente) > 24 Then
                            aggiungi("c'è un salto di più di due ottave verso " & NomeNota(n.Passo, n.Alter, n.Ottava) & ": forse l'ottava è sbagliata")
                        End If
                        ultimaNotaPerVoce(chiaveVoce) = n.Midi
                    End If
                Next
            Next
        Next
        ' al massimo un problema per tipo e per battuta uguale
        Dim viste As New HashSet(Of String)()
        Dim unici As New List(Of ProblemaSpartito)()
        For Each pr As ProblemaSpartito In elenco
            If viste.Add(pr.Testo) Then unici.Add(pr)
        Next
        Return unici.OrderBy(Function(x) x.Misura).ThenBy(Function(x) x.Parte).ToList()
    End Function

    ' ---- nomi e altezze ----------------------------------------------------------------------------------

    Public Shared Function NomeNota(passo As String, alter As Integer, ottava As Integer) As String
        Dim i As Integer = Array.IndexOf(NomiPassi, passo)
        If i < 0 Then Return passo
        Dim s As String = NomiItaliani(i)
        Select Case alter
            Case 1 : s &= " diesis"
            Case 2 : s &= " doppio diesis"
            Case -1 : s &= " bemolle"
            Case -2 : s &= " doppio bemolle"
        End Select
        Return s & " " & ottava.ToString()
    End Function

    ''' <summary>Capisce "do4", "re#5", "mib3", "sol diesis 4", "la bemolle 2", "c4", "f#3".</summary>
    Public Shared Function LeggiAltezza(testo As String, ByRef passo As String, ByRef alter As Integer, ByRef ottava As Integer) As Boolean
        Dim t As String = If(testo, "").Trim().ToLowerInvariant().Replace(" ", "")
        t = t.Replace("doppiodiesis", "x").Replace("doppiobemolle", "bb").Replace("diesis", "#").Replace("bemolle", "b")
        Dim passi As String() = {"sol", "do", "re", "mi", "fa", "la", "si"}
        Dim lettere As String() = {"G", "C", "D", "E", "F", "A", "B"}
        Dim resto As String = ""
        passo = ""
        For i As Integer = 0 To passi.Length - 1
            If t.StartsWith(passi(i)) Then
                passo = lettere(i)
                resto = t.Substring(passi(i).Length)
                Exit For
            End If
        Next
        If passo = "" AndAlso t.Length > 0 AndAlso "cdefgab".Contains(t(0)) Then
            passo = t.Substring(0, 1).ToUpperInvariant()
            resto = t.Substring(1)
        End If
        If passo = "" Then Return False
        alter = 0
        If resto.StartsWith("##") OrElse resto.StartsWith("x") Then
            alter = 2
            resto = resto.Substring(If(resto.StartsWith("##"), 2, 1))
        ElseIf resto.StartsWith("#") Then
            alter = 1
            resto = resto.Substring(1)
        ElseIf resto.StartsWith("bb") Then
            alter = -2
            resto = resto.Substring(2)
        ElseIf resto.StartsWith("b") Then
            alter = -1
            resto = resto.Substring(1)
        End If
        Dim o As Integer
        If Not Integer.TryParse(resto, o) OrElse o < 0 OrElse o > 9 Then Return False
        ottava = o
        Return True
    End Function

    Private Shared ReadOnly ValoriNote() As Double = {8.0, 4.0, 2.0, 1.0, 0.5, 0.25, 0.125, 0.0625}
    Private Shared ReadOnly TipiNote() As String = {"breve", "whole", "half", "quarter", "eighth", "16th", "32nd", "64th"}
    Private Shared ReadOnly NomiValori() As String = {"breve", "semibreve", "minima", "semiminima", "croma", "semicroma", "biscroma", "semibiscroma"}

    Public Shared Function NomiDurate() As String()
        Dim r As New List(Of String)()
        For i As Integer = 1 To NomiValori.Length - 1
            r.Add(NomiValori(i) & " (" & ValoriNote(i).ToString("0.####", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " quarti)")
            r.Add(NomiValori(i) & " puntata (" & (ValoriNote(i) * 1.5).ToString("0.####", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " quarti)")
        Next
        Return r.ToArray()
    End Function

    Public Shared Function QuartiDellaDurata(indiceElenco As Integer) As Double
        Dim valore As Double = ValoriNote(indiceElenco \ 2 + 1)
        Return If(indiceElenco Mod 2 = 1, valore * 1.5, valore)
    End Function

    Friend Shared Function Descrizione(n As NotaInfo, divisioni As Integer) As String
        Dim q As Double = n.Durata / CDbl(Math.Max(1, divisioni))
        Dim tm As XElement = n.El.Element("time-modification")
        If tm IsNot Nothing Then
            Dim attuali As Integer = Intero(tm, "actual-notes", 1)
            Dim normali As Integer = Intero(tm, "normal-notes", 1)
            If attuali > 0 Then q = q * attuali / normali
        End If
        Dim nomeDurata As String = ""
        For i As Integer = 1 To ValoriNote.Length - 1
            If Math.Abs(q - ValoriNote(i)) < 0.0005 Then nomeDurata = NomiValori(i)
            If Math.Abs(q - ValoriNote(i) * 1.5) < 0.0005 Then nomeDurata = NomiValori(i) & " puntata"
        Next
        If nomeDurata = "" Then nomeDurata = "durata " & (n.Durata / CDbl(Math.Max(1, divisioni))).ToString("0.##", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " quarti"
        Dim t As New StringBuilder()
        If n.Grazia Then
            t.Append("Nota d'abbellimento, ")
        ElseIf n.Accordo Then
            t.Append("Nello stesso accordo, ")
        End If
        If n.Pausa Then
            t.Append("Pausa di ").Append(nomeDurata)
        ElseIf n.SenzaAltezza Then
            t.Append("Percussione, ").Append(nomeDurata)
        Else
            t.Append(NomeNota(n.Passo, n.Alter, n.Ottava)).Append(", ").Append(nomeDurata)
        End If
        If n.Voce <> 1 Then t.Append(", voce ").Append(n.Voce.ToString())
        If n.El.Element("tie") IsNot Nothing Then t.Append(", legata")
        Return t.ToString()
    End Function

    ' ---- modifiche ---------------------------------------------------------------------------------------

    Private Shared Sub InserisciDopoUltimo(padre As XElement, nuovo As XElement, ParamArray nomi As String())
        Dim ultimo As XElement = Nothing
        For Each f As XElement In padre.Elements()
            If nomi.Contains(f.Name.LocalName) Then ultimo = f
        Next
        If ultimo IsNot Nothing Then ultimo.AddAfterSelf(nuovo) Else padre.AddFirst(nuovo)
    End Sub

    Private Shared Sub ScriviAccidentale(el As XElement, passo As String, alter As Integer, quinte As Integer)
        Dim vecchio As XElement = el.Element("accidental")
        If vecchio IsNot Nothing Then vecchio.Remove()
        If alter = AlterazioneInChiave(passo, quinte) Then Return
        Dim nome As String = "natural"
        Select Case alter
            Case 1 : nome = "sharp"
            Case 2 : nome = "double-sharp"
            Case -1 : nome = "flat"
            Case -2 : nome = "flat-flat"
        End Select
        InserisciDopoUltimo(el, New XElement("accidental", nome), "chord", "pitch", "rest", "unpitched", "duration", "tie", "cue", "instrument", "footnote", "level", "voice", "type", "dot")
    End Sub

    Friend Sub ImpostaAltezza(n As NotaInfo, passo As String, alter As Integer, ottava As Integer, quinte As Integer)
        Dim el As XElement = n.El
        Dim pausa As XElement = el.Element("rest")
        If pausa IsNot Nothing Then pausa.Remove()
        Dim altezza As XElement = el.Element("pitch")
        If altezza Is Nothing Then
            altezza = New XElement("pitch")
            Dim durata As XElement = el.Element("duration")
            If durata IsNot Nothing Then durata.AddBeforeSelf(altezza) Else InserisciDopoUltimo(el, altezza, "grace", "chord")
        End If
        altezza.RemoveNodes()
        altezza.Add(New XElement("step", passo))
        If alter <> 0 Then altezza.Add(New XElement("alter", alter.ToString()))
        altezza.Add(New XElement("octave", ottava.ToString()))
        ScriviAccidentale(el, passo, alter, quinte)
    End Sub

    Friend Sub ImpostaPausa(n As NotaInfo)
        Dim el As XElement = n.El
        For Each nome As String In New String() {"pitch", "unpitched", "accidental", "tie", "notations", "beam", "stem"}
            For Each f As XElement In el.Elements(nome).ToList()
                f.Remove()
            Next
        Next
        If el.Element("rest") Is Nothing Then
            Dim durata As XElement = el.Element("duration")
            If durata IsNot Nothing Then durata.AddBeforeSelf(New XElement("rest")) Else InserisciDopoUltimo(el, New XElement("rest"), "grace", "chord")
        End If
    End Sub

    Friend Sub Elimina(n As NotaInfo)
        If Not n.Accordo Then
            Dim seguente As XElement = TryCast(n.El.NextNode, XElement)
            While seguente IsNot Nothing AndAlso seguente.Name.LocalName <> "note"
                seguente = TryCast(seguente.NextNode, XElement)
                If seguente IsNot Nothing AndAlso seguente.Name.LocalName = "note" Then Exit While
            End While
            If seguente IsNot Nothing AndAlso seguente.Element("chord") IsNot Nothing Then seguente.Element("chord").Remove()
        End If
        n.El.Remove()
    End Sub

    ''' <summary>Cambia la durata. Restituisce falso se la suddivisione dello spartito non permette quella durata.</summary>
    Friend Function ImpostaDurata(n As NotaInfo, divisioni As Integer, quarti As Double) As Boolean
        Dim el As XElement = n.El
        Dim fattore As Double = 1.0
        Dim tm As XElement = el.Element("time-modification")
        If tm IsNot Nothing Then
            Dim attuali As Integer = Intero(tm, "actual-notes", 1)
            Dim normali As Integer = Intero(tm, "normal-notes", 1)
            If attuali > 0 Then fattore = normali / CDbl(attuali)
        End If
        Dim d As Double = quarti * divisioni * fattore
        If d < 0.999 OrElse Math.Abs(d - Math.Round(d)) > 0.001 Then Return False
        Dim tipo As String = ""
        Dim punti As Integer = 0
        For i As Integer = 0 To ValoriNote.Length - 1
            If Math.Abs(quarti - ValoriNote(i)) < 0.0005 Then
                tipo = TipiNote(i)
            ElseIf Math.Abs(quarti - ValoriNote(i) * 1.5) < 0.0005 Then
                tipo = TipiNote(i)
                punti = 1
            End If
        Next
        If tipo = "" Then Return False
        Dim durata As XElement = el.Element("duration")
        If durata Is Nothing Then
            durata = New XElement("duration")
            InserisciDopoUltimo(el, durata, "grace", "chord", "pitch", "rest", "unpitched")
        End If
        durata.Value = CInt(Math.Round(d)).ToString()
        For Each f As XElement In el.Elements("type").ToList()
            f.Remove()
        Next
        For Each f As XElement In el.Elements("dot").ToList()
            f.Remove()
        Next
        Dim elementoTipo As New XElement("type", tipo)
        InserisciDopoUltimo(el, elementoTipo, "grace", "chord", "pitch", "rest", "unpitched", "duration", "tie", "cue", "instrument", "footnote", "level", "voice")
        If punti > 0 Then elementoTipo.AddAfterSelf(New XElement("dot"))
        Return True
    End Function

    Private Shared Function UltimoDelGruppo(n As NotaInfo) As XElement
        Dim ultimo As XElement = n.El
        Dim s As XNode = n.El.NextNode
        While s IsNot Nothing
            Dim e As XElement = TryCast(s, XElement)
            If e IsNot Nothing Then
                If e.Name.LocalName = "note" AndAlso e.Element("chord") IsNot Nothing Then
                    ultimo = e
                Else
                    Exit While
                End If
            End If
            s = s.NextNode
        End While
        Return ultimo
    End Function

    ''' <summary>Aggiunge una pausa (passo vuoto) o una nota dopo quella indicata, nella stessa voce.</summary>
    Friend Function AggiungiDopo(n As NotaInfo, divisioni As Integer, quarti As Double, passo As String, alter As Integer, ottava As Integer, quinte As Integer) As Boolean
        Return CreaDopo(n, divisioni, quarti, passo, alter, ottava, quinte) IsNot Nothing
    End Function

    Private Function CreaDopo(n As NotaInfo, divisioni As Integer, quarti As Double, passo As String, alter As Integer, ottava As Integer, quinte As Integer) As XElement
        Dim nuova As New XElement("note")
        If passo = "" Then nuova.Add(New XElement("rest")) Else nuova.Add(New XElement("pitch", New XElement("step", passo), New XElement("octave", ottava.ToString())))
        nuova.Add(New XElement("duration", "1"))
        nuova.Add(New XElement("voice", n.Voce.ToString()))
        Dim rigo As XElement = n.El.Element("staff")
        If rigo IsNot Nothing Then nuova.Add(New XElement("staff", rigo.Value))
        Dim nodo As New NotaInfo()
        nodo.El = nuova
        nodo.Voce = n.Voce
        nodo.Rigo = n.Rigo
        If Not ImpostaDurata(nodo, divisioni, quarti) Then Return Nothing
        If passo <> "" Then ImpostaAltezza(nodo, passo, alter, ottava, quinte)
        UltimoDelGruppo(n).AddAfterSelf(nuova)
        Return nuova
    End Function

    ''' <summary>Imposta di quanti semitoni suona diversamente da come è scritto uno strumento (clarinetto in si bemolle: -2).</summary>
    Public Sub ImpostaTrasposizione(indiceParte As Integer, semitoni As Integer)
        Dim parte As XElement = Parti(indiceParte).Elemento
        For Each t As XElement In parte.Descendants("transpose").ToList()
            t.Remove()
        Next
        Dim prima As XElement = parte.Elements("measure").FirstOrDefault()
        If prima Is Nothing Then Return
        Dim attributi As XElement = prima.Element("attributes")
        If attributi Is Nothing Then
            attributi = New XElement("attributes")
            prima.AddFirst(attributi)
        End If
        If semitoni = 0 Then Return
        Dim ottave As Integer = CInt(Math.Truncate(semitoni / 12.0))
        Dim cromatico As Integer = semitoni - 12 * ottave
        Dim tr As New XElement("transpose", New XElement("chromatic", cromatico.ToString()))
        If ottave <> 0 Then tr.Add(New XElement("octave-change", ottave.ToString()))
        attributi.Add(tr)
    End Sub

    ''' <summary>Dà a una parte il nome e lo strumento General MIDI (programma da 0 a 127) con cui deve suonare.</summary>
    Public Sub ImpostaStrumentoParte(indiceParte As Integer, nome As String, programma As Integer)
        Dim id As String = Parti(indiceParte).Id
        Dim elenco As XElement = _doc.Root.Element("part-list")
        If elenco Is Nothing Then Return
        Dim sp As XElement = elenco.Elements("score-part").FirstOrDefault(Function(x) x.Attribute("id") IsNot Nothing AndAlso x.Attribute("id").Value = id)
        If sp Is Nothing Then Return
        Dim nomeParte As XElement = sp.Element("part-name")
        If nomeParte Is Nothing Then
            nomeParte = New XElement("part-name", nome)
            Dim identificazione As XElement = sp.Element("identification")
            If identificazione IsNot Nothing Then identificazione.AddAfterSelf(nomeParte) Else sp.AddFirst(nomeParte)
        Else
            nomeParte.Value = nome
        End If
        For Each v As XElement In sp.Elements("part-name-display").ToList()
            v.Remove()
        Next
        Dim idStrumento As String = id & "-I1"
        Dim strumento As XElement = sp.Element("score-instrument")
        If strumento Is Nothing Then
            strumento = New XElement("score-instrument", New XAttribute("id", idStrumento), New XElement("instrument-name", nome))
            Dim ultimoNome As XElement = sp.Elements().LastOrDefault(Function(x) x.Name.LocalName.StartsWith("part-"))
            If ultimoNome IsNot Nothing Then ultimoNome.AddAfterSelf(strumento) Else sp.AddFirst(strumento)
        Else
            Dim nomeStrumento As XElement = strumento.Element("instrument-name")
            If nomeStrumento Is Nothing Then strumento.Add(New XElement("instrument-name", nome)) Else nomeStrumento.Value = nome
            If strumento.Attribute("id") IsNot Nothing Then idStrumento = strumento.Attribute("id").Value
        End If
        Dim midi As XElement = sp.Element("midi-instrument")
        If midi Is Nothing Then
            midi = New XElement("midi-instrument", New XAttribute("id", idStrumento))
            sp.Add(midi)
        End If
        For Each nomeElemento As String In New String() {"midi-channel", "midi-unpitched", "midi-bank", "midi-program"}
            For Each v As XElement In midi.Elements(nomeElemento).ToList()
                v.Remove()
            Next
        Next
        midi.AddFirst(New XElement("midi-program", (programma + 1).ToString()))
        Ricostruisci()
    End Sub

    ''' <summary>Imposta il tempo di tutto il brano (semiminime al minuto) dalla prima battuta.</summary>
    Public Sub ImpostaTempo(battiti As Integer)
        Dim prima As Boolean = True
        For Each p As ParteXml In Parti
            Dim misura As XElement = p.Elemento.Elements("measure").FirstOrDefault()
            If misura Is Nothing Then Continue For
            For Each d As XElement In misura.Elements("direction").ToList()
                Dim suono As XElement = d.Element("sound")
                If (suono IsNot Nothing AndAlso suono.Attribute("tempo") IsNot Nothing) OrElse d.Descendants("metronome").Any() Then d.Remove()
            Next
            If prima Then
                prima = False
                Dim direzione As New XElement("direction", New XAttribute("placement", "above"),
                                              New XElement("direction-type", New XElement("metronome", New XElement("beat-unit", "quarter"), New XElement("per-minute", battiti.ToString()))),
                                              New XElement("sound", New XAttribute("tempo", battiti.ToString())))
                Dim attributi As XElement = misura.Element("attributes")
                If attributi IsNot Nothing Then attributi.AddAfterSelf(direzione) Else misura.AddFirst(direzione)
            End If
        Next
    End Sub

    ''' <summary>Gli organici d'orchestra riconosciuti: la descrizione e poi, in ordine di partitura, nome|strumento General MIDI|trasposizione.</summary>
    Public Shared ReadOnly Organici()() As String = {
        New String() {"Orchestra classica a dodici strumenti: flauti, oboi, clarinetti, fagotti, corni, trombe, timpani, violini primi e secondi, viole, violoncelli, contrabbassi",
                       "Flauti|73|0", "Oboi|68|0", "Clarinetti|71|-2", "Fagotti|70|0", "Corni|60|-9", "Trombe|56|0", "Timpani|47|0",
                       "Violini primi|40|0", "Violini secondi|40|0", "Viole|41|0", "Violoncelli|42|0", "Contrabbassi|43|-12"},
        New String() {"Quartetto d'archi: due violini, viola e violoncello", "Violino primo|40|0", "Violino secondo|40|0", "Viola|41|0", "Violoncello|42|0"},
        New String() {"Pianoforte: mano destra e mano sinistra", "Pianoforte mano destra|0|0", "Pianoforte mano sinistra|0|0"}}

    Private Function ParteGenerica(indice As Integer) As Boolean
        Dim n As String = Parti(indice).Nome.Trim().ToLowerInvariant()
        If n = "" OrElse n = "voice" OrElse n = "voci" OrElse n = "voce" Then Return True
        Return n.StartsWith("parte ") OrElse n.StartsWith("part ") OrElse n.StartsWith("strumento ")
    End Function

    ''' <summary>Vero se nessuna parte ha un nome da strumento: il riconoscimento di un PDF le chiama tutte "Voice" e le farebbe suonare da un coro.</summary>
    Public Function TutteGeneriche() As Boolean
        If Parti.Count = 0 Then Return False
        For i As Integer = 0 To Parti.Count - 1
            If Not ParteGenerica(i) Then Return False
        Next
        Return True
    End Function

    Private Shared Function Mediana(valori As List(Of Integer)) As Integer
        If valori.Count = 0 Then Return 0
        Dim v As List(Of Integer) = valori.OrderBy(Function(x) x).ToList()
        Return v(v.Count \ 2)
    End Function

    Private Shared Function FuoriEstensione(valori As List(Of Integer), nome As String, trasposizione As Integer) As Double
        Dim basso As Integer = 0
        Dim alto As Integer = 0
        If valori.Count = 0 OrElse Not IntervalloStrumento(nome, basso, alto) Then Return 0.0
        Dim fuori As Integer = 0
        For Each m As Integer In valori
            Dim suono As Integer = m + trasposizione
            If suono < basso - 2 OrElse suono > alto + 2 Then fuori += 1
        Next
        Return fuori / CDbl(valori.Count)
    End Function

    ''' <summary>
    ''' Dà un nome e uno strumento a parti che non ne hanno: prima cerca un organico d'orchestra con lo stesso numero di parti
    ''' e con le note dentro l'estensione di ogni strumento, poi sceglie per ogni parte lo strumento più vicino all'altezza delle note.
    ''' Restituisce falso se non ha potuto assegnare nulla.
    ''' </summary>
    Public Function AssegnaAutomaticamente(ByRef descrizione As String) As Boolean
        descrizione = ""
        Dim note As New List(Of List(Of Integer))()
        For p As Integer = 0 To Parti.Count - 1
            Dim valori As New List(Of Integer)()
            For Each mi As MisuraInfo In Analizza(p)
                For Each n As NotaInfo In mi.Note
                    If Not n.Pausa AndAlso Not n.SenzaAltezza AndAlso Not n.Grazia AndAlso n.Passo <> "" Then valori.Add(n.Midi)
                Next
            Next
            note.Add(valori)
        Next

        For Each organico As String() In Organici
            If organico.Length - 1 <> Parti.Count Then Continue For
            Dim adatto As Boolean = True
            For i As Integer = 0 To Parti.Count - 1
                Dim campi As String() = organico(i + 1).Split("|"c)
                If FuoriEstensione(note(i), campi(0), Integer.Parse(campi(2))) > 0.05 Then
                    adatto = False
                    Exit For
                End If
            Next
            If Not adatto Then Continue For
            For i As Integer = 0 To Parti.Count - 1
                Dim campi As String() = organico(i + 1).Split("|"c)
                ImpostaStrumentoParte(i, campi(0), Integer.Parse(campi(1)))
                ImpostaTrasposizione(i, Integer.Parse(campi(2)))
            Next
            descrizione = "Gli strumenti sono stati riconosciuti dall'ordine e dall'estensione degli strumenti: " & organico(0).Split(":"c)(0)
            Return True
        Next

        Dim candidati As Object()() = {
            New Object() {"Flauti", 73, 0, 79}, New Object() {"Oboi", 68, 0, 74}, New Object() {"Clarinetti", 71, -2, 70}, New Object() {"Fagotti", 70, 0, 55},
            New Object() {"Corni", 60, -9, 62}, New Object() {"Trombe", 56, 0, 68}, New Object() {"Tromboni", 57, 0, 52}, New Object() {"Tuba", 58, 0, 40},
            New Object() {"Violini", 40, 0, 74}, New Object() {"Viole", 41, 0, 64}, New Object() {"Violoncelli", 42, 0, 52}, New Object() {"Contrabbassi", 43, -12, 40}}
        Dim scelte As New Dictionary(Of Integer, Object())()
        For i As Integer = 0 To Parti.Count - 1
            If note(i).Count < 8 Then Continue For
            Dim centro As Integer = Mediana(note(i))
            Dim migliore As Object() = Nothing
            Dim distanzaMigliore As Integer = Integer.MaxValue
            For Each c As Object() In candidati
                Dim traspo As Integer = CInt(c(2))
                If FuoriEstensione(note(i), CStr(c(0)), traspo) > 0.1 Then Continue For
                Dim d As Integer = Math.Abs(centro + traspo - CInt(c(3)))
                If d < distanzaMigliore Then
                    distanzaMigliore = d
                    migliore = c
                End If
            Next
            If migliore IsNot Nothing Then scelte(i) = migliore
        Next
        If scelte.Count = 0 Then Return False
        For Each kv As KeyValuePair(Of Integer, Object()) In scelte
            ImpostaStrumentoParte(kv.Key, CStr(kv.Value(0)), CInt(kv.Value(1)))
            ImpostaTrasposizione(kv.Key, CInt(kv.Value(2)))
        Next
        descrizione = "Gli strumenti sono stati scelti dall'altezza delle note di ogni strumento, ma possono essere sbagliati: controllali con l'opzione Assegna uno strumento"
        Return True
    End Function

    ''' <summary>Se il nome di una parte dice la tonalità ("Clarinetti in Si bemolle", "Corni in Mi bemolle") e la parte non ha già la trasposizione, la imposta. Vero se ha cambiato qualcosa.</summary>
    Public Function ApplicaTrasposizioniDaiNomi() As Boolean
        Dim cambiato As Boolean = False
        For i As Integer = 0 To Parti.Count - 1
            If Parti(i).Elemento.Descendants("transpose").Any() Then Continue For
            Dim t As Integer = Spartito.TrasposizioneDalNome(Parti(i).Nome)
            If t <> 0 Then
                ImpostaTrasposizione(i, t)
                cambiato = True
            End If
        Next
        Return cambiato
    End Function

    Public Function TrasposizioneAttuale(indiceParte As Integer) As Integer
        Dim t As XElement = Parti(indiceParte).Elemento.Descendants("transpose").FirstOrDefault()
        If t Is Nothing Then Return 0
        Return Intero(t, "chromatic", 0) + 12 * Intero(t, "octave-change", 0)
    End Function

    Public Function Misure(indiceParte As Integer) As Integer
        Return Parti(indiceParte).Elemento.Elements("measure").Count()
    End Function

    Public Function IndiceMisuraDaNumero(indiceParte As Integer, numero As String) As Integer
        Dim k As Integer = 0
        For Each m As XElement In Parti(indiceParte).Elemento.Elements("measure")
            Dim n As String = If(m.Attribute("number") IsNot Nothing, m.Attribute("number").Value, (k + 1).ToString())
            If n = numero Then Return k
            k += 1
        Next
        Return -1
    End Function

    ' ---- riparazioni sicure, fatte da sole all'apertura ---------------------------------------------------------------------
    ' Guardando una partitura vera pagina per pagina accanto a quello che Audiveris aveva letto, si vedono errori che si ripetono, e
    ' che si riconoscono con certezza perché lo spartito stesso, scritto in un altro modo, dice com'è giusto:
    '   - una pausa di battuta intera dura tutta la battuta, qualunque sia il tempo: Audiveris le dava a volte una durata strana
    '     (tre quarti in un due quarti), e la battuta sembrava "troppo lunga";
    '   - a volte Audiveris scrive la suddivisione del tempo uguale a zero, e da lì in avanti tutte le durate vengono sbagliate (una
    '     minima dura un quarto, e per venti battute di seguito): ma accanto a ogni durata scrive anche il valore della nota ("minima",
    '     "croma"), e quello è giusto;
    '   - tre crome dopo una pausa di croma diventavano "terzine" perché una scritta ("cresc.") sembrava un 3: ma con il valore scritto
    '     le note riempiono la battuta esattamente, senza terzina, e con la terzina no.
    '   - una nota da sola in una battuta, scritta con un punto che le farebbe sforare il tempo (una minima col punto in un due quarti):
    '     l'unico modo in cui può stare nella battuta è senza il punto, che nell'immagine non c'è (l'ha inventato il riconoscimento);
    '   - una pausa da sola in una battuta riempie la battuta;
    '   - le voci secondarie (la seconda, la terza) a cui manca la pausa finale, per esempio due flauti sullo stesso rigo con un solo
    '     suono nel secondo quarto: il riconoscimento non scrive la pausa nascosta, e la voce "sembra" troppo corta. Aggiungere una
    '     pausa non inventa nessuna nota.
    ' Il valore scritto della nota (il "tipo") si usa solo quando fa tornare il conto della battuta; in tutti gli altri casi non si tocca niente.
    ' Le durate si riscrivono solo nelle battute con una voce sola: con più voci, spostare una durata sposterebbe anche gli altri.

    Private Shared Function QuartiDelTipo(tipo As String) As Double
        Select Case tipo
            Case "long" : Return 16.0
            Case "breve" : Return 8.0
            Case "whole" : Return 4.0
            Case "half" : Return 2.0
            Case "quarter" : Return 1.0
            Case "eighth" : Return 0.5
            Case "16th" : Return 0.25
            Case "32nd" : Return 0.125
            Case "64th" : Return 0.0625
            Case "128th" : Return 0.03125
        End Select
        Return 0.0
    End Function

    ''' <summary>Quante unità di divisione dovrebbe durare una nota secondo il suo valore scritto; -1 se non si può dire.</summary>
    Private Shared Function UnitaDalTipo(n As XElement, divisioni As Integer, conTerzina As Boolean, Optional conPunti As Boolean = True) As Integer
        Dim t As XElement = n.Element("type")
        If t Is Nothing Then Return -1
        Dim q As Double = QuartiDelTipo(t.Value.Trim())
        If q <= 0 Then Return -1
        If conPunti Then q = q * (2.0 - Math.Pow(0.5, n.Elements("dot").Count()))
        If conTerzina Then
            Dim tm As XElement = n.Element("time-modification")
            If tm IsNot Nothing Then
                Dim attuali As Integer = Intero(tm, "actual-notes", 1)
                Dim normali As Integer = Intero(tm, "normal-notes", 1)
                If attuali > 0 Then q = q * normali / CDbl(attuali)
            End If
        End If
        Dim u As Double = q * divisioni
        If u < 0.999 OrElse Math.Abs(u - Math.Round(u)) > 0.001 Then Return -1
        Return CInt(Math.Round(u))
    End Function

    ''' <summary>
    ''' Ripara da sola le battute che si possono riparare con certezza. Restituisce una voce per ogni riparazione, con la battuta e la parte.
    ''' </summary>
    Public Function RiparaDaiTipi() As List(Of ProblemaSpartito)
        Dim fatte As New List(Of ProblemaSpartito)()
        For p As Integer = 0 To Parti.Count - 1
            Dim indiceParte As Integer = p
            Dim nomeParte As String = Parti(p).Nome
            Dim stato As New StatoMisura()
            Dim ultimaValida As Integer = 0
            Dim senzaSuddivisione As Boolean = False
            Dim k As Integer = 0
            For Each m As XElement In Parti(p).Elemento.Elements("measure")
                Dim misuraCorrente As XElement = m
                Dim indiceMisura As Integer = k
                Dim numero As String = If(m.Attribute("number") IsNot Nothing, m.Attribute("number").Value, (k + 1).ToString())
                k += 1
                Dim registra As Action(Of String) = Sub(testo As String)
                                                        Dim c As New ProblemaSpartito()
                                                        c.Parte = indiceParte
                                                        c.Misura = indiceMisura
                                                        c.Numero = numero
                                                        c.Testo = "Battuta " & numero & ", " & nomeParte & ": " & testo
                                                        fatte.Add(c)
                                                    End Sub
                For Each attributi As XElement In m.Elements("attributes")
                    Dim d As XElement = attributi.Element("divisions")
                    If d IsNot Nothing Then
                        Dim v As Integer
                        If Integer.TryParse(d.Value.Trim(), v) AndAlso v > 0 Then
                            ultimaValida = v
                            senzaSuddivisione = False
                        ElseIf ultimaValida > 0 Then
                            d.Value = ultimaValida.ToString()
                            registra("la suddivisione del tempo era scritta zero: l'ho riportata a " & ultimaValida.ToString() & ", come prima")
                        Else
                            senzaSuddivisione = True
                        End If
                    End If
                    AggiornaStato(attributi, stato)
                Next
                If senzaSuddivisione Then Continue For
                If m.Attribute("implicit") IsNot Nothing AndAlso m.Attribute("implicit").Value = "yes" Then Continue For
                Dim div As Integer = Math.Max(1, stato.Divisioni)
                Dim attesa As Integer = div * 4 * stato.Battiti \ Math.Max(1, stato.Valore)
                If attesa <= 0 Then Continue For

                ' la pausa di battuta intera dura la battuta
                For Each n As XElement In m.Elements("note")
                    Dim pausa As XElement = n.Element("rest")
                    If pausa Is Nothing OrElse pausa.Attribute("measure") Is Nothing OrElse pausa.Attribute("measure").Value <> "yes" Then Continue For
                    Dim durata As XElement = n.Element("duration")
                    Dim vecchia As Integer = Intero(n, "duration", 0)
                    If durata IsNot Nothing AndAlso vecchia <> attesa Then
                        durata.Value = attesa.ToString()
                        registra("la pausa di battuta intera durava " & vecchia.ToString() & " unità invece di " & attesa.ToString() & ": ora dura tutta la battuta")
                    End If
                Next

                ' le durate che il valore scritto delle note corregge
                If m.Elements("backup").Any() OrElse m.Elements("forward").Any() Then Continue For
                Dim voci As New Dictionary(Of String, List(Of XElement))()
                For Each n As XElement In m.Elements("note")
                    If n.Element("grace") IsNot Nothing Then Continue For
                    Dim chiave As String = Intero(n, "staff", 1).ToString() & "/" & Intero(n, "voice", 1).ToString()
                    If Not voci.ContainsKey(chiave) Then voci(chiave) = New List(Of XElement)()
                    voci(chiave).Add(n)
                Next
                If voci.Count <> 1 Then Continue For
                For Each kv As KeyValuePair(Of String, List(Of XElement)) In voci
                    Dim note As List(Of XElement) = kv.Value
                    Dim base As List(Of XElement) = note.Where(Function(x) x.Element("chord") Is Nothing).ToList()
                    Dim somma As Integer = 0
                    For Each x As XElement In base
                        somma += Intero(x, "duration", 0)
                    Next
                    If somma = attesa Then Continue For
                    Dim somma1 As Integer = 0
                    Dim somma0 As Integer = 0
                    Dim ok1 As Boolean = True
                    Dim ok0 As Boolean = True
                    Dim conTerzine As Boolean = False
                    For Each x As XElement In base
                        Dim u1 As Integer = UnitaDalTipo(x, div, True)
                        Dim u0 As Integer = UnitaDalTipo(x, div, False)
                        If u1 < 0 Then ok1 = False Else somma1 += u1
                        If u0 < 0 Then ok0 = False Else somma0 += u0
                        If x.Element("time-modification") IsNot Nothing Then conTerzine = True
                    Next
                    Dim senzaTerzine As Boolean = False
                    ' una pausa da sola riempie la battuta
                    If base.Count = 1 AndAlso base(0).Element("rest") IsNot Nothing Then
                        Dim durataPausa As XElement = base(0).Element("duration")
                        If durataPausa IsNot Nothing Then
                            durataPausa.Value = attesa.ToString()
                            registra("una pausa da sola in una battuta riempie tutta la battuta: ora dura " & attesa.ToString() & " unità")
                        End If
                        Continue For
                    End If
                    ' una nota da sola con un punto che la fa sforare: senza il punto riempie la battuta
                    If base.Count = 1 AndAlso base(0).Element("dot") IsNot Nothing AndAlso base(0).Element("time-modification") Is Nothing Then
                        Dim senzaPunto As Integer = UnitaDalTipo(base(0), div, False, False)
                        If senzaPunto = attesa Then
                            For Each x As XElement In note
                                For Each punto As XElement In x.Elements("dot").ToList()
                                    punto.Remove()
                                Next
                                Dim dx As XElement = x.Element("duration")
                                If dx IsNot Nothing Then dx.Value = attesa.ToString()
                            Next
                            registra("la nota da sola nella battuta era scritta col punto, che le faceva sforare il tempo e nell'immagine non c'è: ho tolto il punto")
                            Continue For
                        End If
                    End If
                    If ok1 AndAlso somma1 = attesa Then
                        senzaTerzine = False
                    ElseIf ok0 AndAlso conTerzine AndAlso somma0 = attesa Then
                        senzaTerzine = True
                    Else
                        Continue For
                    End If
                    Dim ultimaUnita As Integer = 0
                    For Each x As XElement In note
                        If senzaTerzine Then
                            Dim tm As XElement = x.Element("time-modification")
                            If tm IsNot Nothing Then tm.Remove()
                            Dim notazioni As XElement = x.Element("notations")
                            If notazioni IsNot Nothing Then
                                For Each tup As XElement In notazioni.Elements("tuplet").ToList()
                                    tup.Remove()
                                Next
                                If Not notazioni.HasElements Then notazioni.Remove()
                            End If
                        End If
                        Dim unita As Integer = UnitaDalTipo(x, div, Not senzaTerzine)
                        If x.Element("chord") IsNot Nothing Then unita = ultimaUnita
                        If unita > 0 Then
                            Dim dur As XElement = x.Element("duration")
                            If dur IsNot Nothing Then dur.Value = unita.ToString()
                            ultimaUnita = unita
                        End If
                    Next
                    registra(If(senzaTerzine, "le note risultavano in terzina, ma senza terzina i valori scritti riempiono esattamente la battuta e con la terzina no: ho tolto la terzina e rimesso le durate", "le durate non tornavano, ma i valori scritti delle note riempiono esattamente la battuta: ho rimesso le durate secondo i valori scritti"))
                Next
            Next

            ' le voci secondarie a cui manca la pausa finale
            Dim misure As List(Of MisuraInfo) = Analizza(p)
            For Each mi As MisuraInfo In misure
                If mi.Attesa <= 0 OrElse mi.Implicita Then Continue For
                If mi.Elemento.Elements("forward").Any() Then Continue For
                Dim div As Integer = mi.Stato.Divisioni
                For Each kv As KeyValuePair(Of String, Integer) In mi.Somme.ToList()
                    If kv.Value <= 0 OrElse kv.Value >= mi.Attesa Then Continue For
                    Dim pezzi As String() = kv.Key.Split("/"c)
                    Dim rigo As Integer = Integer.Parse(pezzi(0))
                    Dim voce As Integer = Integer.Parse(pezzi(1))
                    If voce < 2 Then Continue For
                    Dim dellaVoce As List(Of NotaInfo) = mi.Note.Where(Function(x) x.Rigo = rigo AndAlso x.Voce = voce AndAlso Not x.Accordo AndAlso Not x.Grazia).ToList()
                    If dellaVoce.Count = 0 Then Continue For
                    Dim mancano As Integer = mi.Attesa - kv.Value
                    Dim pause As List(Of Double) = DividiInPause(mancano, div)
                    If pause.Count = 0 Then Continue For
                    Dim corrente As NotaInfo = dellaVoce.Last()
                    Dim aggiunte As Integer = 0
                    For Each v As Double In pause
                        Dim nuovoEl As XElement = CreaDopo(corrente, div, v, "", 0, 0, mi.Stato.Quinte)
                        If nuovoEl Is Nothing Then Exit For
                        Dim ni As New NotaInfo()
                        ni.El = nuovoEl
                        ni.Voce = voce
                        ni.Rigo = rigo
                        corrente = ni
                        aggiunte += 1
                    Next
                    If aggiunte > 0 Then
                        Dim c As New ProblemaSpartito()
                        c.Parte = indiceParte
                        c.Misura = mi.Indice
                        c.Numero = mi.Numero
                        c.Testo = "Battuta " & mi.Numero & ", " & nomeParte & ": alla voce " & voce.ToString() & " mancava la pausa finale: l'ho aggiunta, non inventa nessuna nota"
                        fatte.Add(c)
                    End If
                Next
            Next
        Next
        Return fatte
    End Function

    ' ---- correzione automatica ----------------------------------------------------------------------------
    ' Si correggono da soli solo i casi in cui la soluzione è praticamente unica:
    '   - una voce troppo corta o troppo lunga che torna giusta cambiando una sola nota in uno solo dei modi più comuni da
    '     leggere male (un punto in più o in meno, la durata dimezzata o raddoppiata). Se i modi possibili sono due o più,
    '     non si sceglie: resta un dubbio;
    '   - una voce troppo corta che non si risolve così: si aggiungono pause in fondo, che non inventano nessuna nota;
    '   - una nota fuori dall'estensione dello strumento che, spostata di una o più ottave, ci rientra.
    ' Le alterazioni mancanti e i salti non si correggono: potrebbero essere veri.

    Private Shared ReadOnly DurateAmmesse() As Double = {6.0, 4.0, 3.0, 2.0, 1.5, 1.0, 0.75, 0.5, 0.375, 0.25, 0.1875, 0.125, 0.09375, 0.0625}

    Private Shared Function DurataRappresentabile(quarti As Double, divisioni As Integer) As Boolean
        Dim unita As Double = quarti * divisioni
        If unita < 0.999 OrElse Math.Abs(unita - Math.Round(unita)) > 0.001 Then Return False
        For Each v As Double In DurateAmmesse
            If Math.Abs(v - quarti) < 0.0005 Then Return True
        Next
        Return False
    End Function

    Private Shared Function NomeDurataQuarti(quarti As Double) As String
        For i As Integer = 1 To ValoriNote.Length - 1
            If Math.Abs(quarti - ValoriNote(i)) < 0.0005 Then Return NomiValori(i)
            If Math.Abs(quarti - ValoriNote(i) * 1.5) < 0.0005 Then Return NomiValori(i) & " puntata"
        Next
        Return quarti.ToString("0.##", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " quarti"
    End Function

    ''' <summary>Divide una durata in pause scrivibili; vuoto se non si può.</summary>
    Private Shared Function DividiInPause(unita As Integer, divisioni As Integer) As List(Of Double)
        Dim r As New List(Of Double)()
        Dim resto As Integer = unita
        While resto > 0
            Dim trovato As Boolean = False
            For Each v As Double In DurateAmmesse
                Dim u As Double = v * divisioni
                If Math.Abs(u - Math.Round(u)) < 0.001 AndAlso CInt(Math.Round(u)) <= resto AndAlso CInt(Math.Round(u)) > 0 Then
                    r.Add(v)
                    resto -= CInt(Math.Round(u))
                    trovato = True
                    Exit For
                End If
            Next
            If Not trovato Then Return New List(Of Double)()
        End While
        Return r
    End Function

    Private Function ImpostaDurataGruppo(mi As MisuraInfo, n As NotaInfo, quarti As Double) As Boolean
        If Not ImpostaDurata(n, mi.Stato.Divisioni, quarti) Then Return False
        Dim i As Integer = mi.Note.IndexOf(n) + 1
        While i > 0 AndAlso i < mi.Note.Count AndAlso mi.Note(i).Accordo
            ImpostaDurata(mi.Note(i), mi.Stato.Divisioni, quarti)
            i += 1
        End While
        Return True
    End Function

    Public Function CorreggiAutomaticamente() As List(Of ProblemaSpartito)
        Dim fatte As New List(Of ProblemaSpartito)()
        For p As Integer = 0 To Parti.Count - 1
            Dim indiceParte As Integer = p
            Dim nomeParte As String = Parti(p).Nome
            Dim misure As List(Of MisuraInfo) = Analizza(p)
            For Each mi As MisuraInfo In misure
                Dim misuraCorrente As MisuraInfo = mi
                Dim registra As Action(Of String) = Sub(testo As String)
                                                        Dim c As New ProblemaSpartito()
                                                        c.Parte = indiceParte
                                                        c.Misura = misuraCorrente.Indice
                                                        c.Numero = misuraCorrente.Numero
                                                        c.Testo = "Battuta " & misuraCorrente.Numero & ", " & nomeParte & ": " & testo
                                                        fatte.Add(c)
                                                    End Sub
                If mi.Attesa <= 0 OrElse mi.Implicita Then Continue For
                If mi.Elemento.Elements("forward").Any() Then Continue For
                Dim primaOUltima As Boolean = (mi.Indice = 0 OrElse mi.Indice = misure.Count - 1)
                Dim div As Integer = mi.Stato.Divisioni
                For Each kv As KeyValuePair(Of String, Integer) In mi.Somme.ToList()
                    If kv.Value = mi.Attesa Then Continue For
                    If primaOUltima AndAlso kv.Value < mi.Attesa Then Continue For
                    Dim pezzi As String() = kv.Key.Split("/"c)
                    Dim rigo As Integer = Integer.Parse(pezzi(0))
                    Dim voce As Integer = Integer.Parse(pezzi(1))
                    Dim dellaVoce As List(Of NotaInfo) = mi.Note.Where(Function(x) x.Rigo = rigo AndAlso x.Voce = voce AndAlso Not x.Accordo AndAlso Not x.Grazia).ToList()
                    If dellaVoce.Count = 0 Then Continue For
                    ' un solo cambio possibile?
                    Dim candidati As New List(Of KeyValuePair(Of NotaInfo, Double))()
                    For Each n As NotaInfo In dellaVoce
                        If n.El.Element("time-modification") IsNot Nothing OrElse n.Durata <= 0 Then Continue For
                        Dim q As Double = n.Durata / CDbl(div)
                        For Each fattore As Double In New Double() {1.5, 1.0 / 1.5, 2.0, 0.5}
                            Dim nuova As Double = q * fattore
                            Dim nuovaUnita As Double = nuova * div
                            If Math.Abs(nuovaUnita - Math.Round(nuovaUnita)) > 0.001 Then Continue For
                            If Not DurataRappresentabile(nuova, div) Then Continue For
                            If kv.Value - n.Durata + CInt(Math.Round(nuovaUnita)) = mi.Attesa Then candidati.Add(New KeyValuePair(Of NotaInfo, Double)(n, nuova))
                        Next
                    Next
                    If candidati.Count = 1 Then
                        Dim n As NotaInfo = candidati(0).Key
                        Dim vecchia As Double = n.Durata / CDbl(div)
                        Dim nome As String = If(n.Pausa, "la pausa", If(n.Passo = "", "la nota", "la nota " & NomeNota(n.Passo, n.Alter, n.Ottava)))
                        If ImpostaDurataGruppo(mi, n, candidati(0).Value) Then
                            registra("la voce " & voce.ToString() & " non tornava: " & nome & " passa da " & NomeDurataQuarti(vecchia) & " a " & NomeDurataQuarti(candidati(0).Value) & ", perché è l'unico cambio che fa tornare il conto")
                        End If
                    ElseIf candidati.Count = 0 AndAlso kv.Value < mi.Attesa Then
                        Dim mancano As Integer = mi.Attesa - kv.Value
                        Dim pause As List(Of Double) = DividiInPause(mancano, div)
                        If pause.Count > 0 Then
                            Dim corrente As NotaInfo = dellaVoce.Last()
                            For Each v As Double In pause
                                Dim nuovoEl As XElement = CreaDopo(corrente, div, v, "", 0, 0, mi.Stato.Quinte)
                                If nuovoEl Is Nothing Then Exit For
                                Dim ni As New NotaInfo()
                                ni.El = nuovoEl
                                ni.Voce = voce
                                ni.Rigo = rigo
                                corrente = ni
                            Next
                            registra("la voce " & voce.ToString() & " era troppo corta: aggiunte pause in fondo per " & (mancano / CDbl(div)).ToString("0.##", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " quarti")
                        End If
                    End If
                Next
            Next

            ' le note fuori dall'estensione
            Dim basso As Integer = 0
            Dim alto As Integer = 0
            If IntervalloStrumento(nomeParte, basso, alto) Then
                For Each mi As MisuraInfo In Analizza(p)
                    For Each n As NotaInfo In mi.Note
                        If n.Pausa OrElse n.SenzaAltezza OrElse n.Passo = "" Then Continue For
                        If n.Midi >= basso - 2 AndAlso n.Midi <= alto + 2 Then Continue For
                        For Each k As Integer In New Integer() {1, -1, 2, -2, 3, -3}
                            Dim m As Integer = n.Midi + 12 * k
                            If m >= basso AndAlso m <= alto Then
                                Dim prima As String = NomeNota(n.Passo, n.Alter, n.Ottava)
                                ImpostaAltezza(n, n.Passo, n.Alter, n.Ottava + k, mi.Stato.Quinte)
                                Dim c As New ProblemaSpartito()
                                c.Parte = p
                                c.Misura = mi.Indice
                                c.Numero = mi.Numero
                                c.Testo = "Battuta " & mi.Numero & ", " & nomeParte & ": la nota " & prima & " era fuori dall'estensione dello strumento, spostata di " & Math.Abs(k).ToString() & If(Math.Abs(k) = 1, " ottava", " ottave") & If(k > 0, " più in alto", " più in basso") & " in " & NomeNota(n.Passo, n.Alter, n.Ottava + k)
                                fatte.Add(c)
                                Exit For
                            End If
                        Next
                    Next
                Next
            End If
        Next
        Return fatte
    End Function

    ''' <summary>
    ''' Sostituisce le note di una battuta con quelle della stessa battuta letta altrove, portando le durate alla stessa unità di
    ''' divisione. Falso, senza cambiare niente, se non si può farlo senza perdere precisione.
    ''' </summary>
    Friend Function SostituisciMisura(indiceParte As Integer, proprio As MisuraInfo, donatore As MisuraInfo) As Boolean
        Dim dA As Integer = Math.Max(1, proprio.Stato.Divisioni)
        Dim dB As Integer = Math.Max(1, donatore.Stato.Divisioni)
        Dim moltiplica As Integer = 1
        Dim dividi As Integer = 1
        If dA <> dB Then
            If dA Mod dB = 0 Then
                moltiplica = dA \ dB
            ElseIf dB Mod dA = 0 Then
                dividi = dB \ dA
            Else
                Return False
            End If
        End If
        Dim nuovi As New List(Of XElement)()
        For Each el As XElement In donatore.Elemento.Elements()
            Dim nome As String = el.Name.LocalName
            If nome <> "note" AndAlso nome <> "backup" AndAlso nome <> "forward" Then Continue For
            Dim copia As New XElement(el)
            For Each d As XElement In copia.Elements("duration")
                Dim v As Integer
                If Not Integer.TryParse(d.Value.Trim(), v) Then Return False
                If (v * moltiplica) Mod dividi <> 0 Then Return False
                d.Value = ((v * moltiplica) \ dividi).ToString()
            Next
            nuovi.Add(copia)
        Next
        If nuovi.Count = 0 Then Return False
        Dim vecchi As List(Of XElement) = proprio.Elemento.Elements().Where(Function(x) x.Name.LocalName = "note" OrElse x.Name.LocalName = "backup" OrElse x.Name.LocalName = "forward").ToList()
        Dim ultimoFisso As XElement = Nothing
        For Each x As XElement In proprio.Elemento.Elements()
            Dim nm As String = x.Name.LocalName
            If nm = "note" Then Exit For
            If nm = "attributes" OrElse nm = "print" Then ultimoFisso = x
        Next
        For Each x As XElement In vecchi
            x.Remove()
        Next
        If ultimoFisso IsNot Nothing Then
            ultimoFisso.AddAfterSelf(nuovi)
        Else
            proprio.Elemento.AddFirst(nuovi)
        End If
        Return True
    End Function

    ''' <summary>
    ''' Applica le correzioni ricavate leggendo i caratteri del PDF (vedi WGSpartitiGlifi): una codina che mancava, una nota di altezza
    ''' sbagliata, una battuta vuota da ricostruire. Restituisce una voce per ogni correzione fatta.
    ''' </summary>
    Friend Function ApplicaCorrezioniGlifi(correzioni As IEnumerable(Of String)) As List(Of ProblemaSpartito)
        Dim fatte As New List(Of ProblemaSpartito)()
        Dim analisi As New Dictionary(Of Integer, List(Of MisuraInfo))()
        For Each riga As String In correzioni
            Try
                Dim c As String() = riga.Split("|"c)
                If c.Length < 6 OrElse c(0) <> "C" Then Continue For
                Dim p As Integer
                Dim k As Integer
                If Not Integer.TryParse(c(2), p) OrElse Not Integer.TryParse(c(3), k) Then Continue For
                If p < 0 OrElse p >= Parti.Count Then Continue For
                Dim misura As XElement = Parti(p).Elemento.Elements("measure").ElementAtOrDefault(k)
                If misura Is Nothing Then Continue For
                If Not analisi.ContainsKey(p) Then analisi(p) = Analizza(p)
                If k >= analisi(p).Count Then Continue For
                Dim mi As MisuraInfo = analisi(p)(k)
                Dim numero As String = mi.Numero
                Dim nomeParte As String = Parti(p).Nome
                Dim divisioni As Integer = Math.Max(1, mi.Stato.Divisioni)
                Dim registra As Action(Of String) = Sub(testo As String)
                                                        Dim r As New ProblemaSpartito()
                                                        r.Parte = p
                                                        r.Misura = k
                                                        r.Numero = numero
                                                        r.Testo = "Battuta " & numero & ", " & nomeParte & ": " & testo
                                                        fatte.Add(r)
                                                    End Sub
                If c(1) = "F" Then
                    ' una nota letta come semiminima che nel PDF ha la codina: è una croma (o meno)
                    Dim ordinale As Integer
                    If Not Integer.TryParse(c(4), ordinale) Then Continue For
                    Dim elenco As List(Of XElement) = misura.Elements("note").ToList()
                    If ordinale < 0 OrElse ordinale >= elenco.Count Then Continue For
                    Dim el As XElement = elenco(ordinale)
                    If el.Element("type") Is Nothing OrElse el.Element("type").Value.Trim() <> "quarter" Then Continue For
                    Dim quarti As Double = If(c(5) = "16th", 0.25, If(c(5) = "32nd", 0.125, 0.5))
                    Dim nota As New NotaInfo()
                    nota.El = el
                    nota.Voce = Intero(el, "voice", 1)
                    nota.Rigo = Intero(el, "staff", 1)
                    Dim nome As String = If(el.Element("pitch") IsNot Nothing AndAlso el.Element("pitch").Element("step") IsNot Nothing, NomeNota(el.Element("pitch").Element("step").Value.Trim(), 0, Intero(el.Element("pitch"), "octave", 4)), "una nota")
                    If Not ImpostaDurata(nota, divisioni, quarti) Then Continue For
                    Dim seguente As XElement = TryCast(el.NextNode, XElement)
                    While seguente IsNot Nothing AndAlso seguente.Name.LocalName = "note" AndAlso seguente.Element("chord") IsNot Nothing
                        Dim membro As New NotaInfo()
                        membro.El = seguente
                        membro.Voce = nota.Voce
                        membro.Rigo = nota.Rigo
                        ImpostaDurata(membro, divisioni, quarti)
                        seguente = TryCast(seguente.NextNode, XElement)
                    End While
                    registra("la nota " & nome & " era letta come semiminima, ma nel PDF ha la codina: ora è " & If(c(5) = "eighth", "una croma", If(c(5) = "16th", "una semicroma", "una biscroma")))
                ElseIf c(1) = "A" Then
                    ' una nota con l'altezza sbagliata: nel PDF la testa sta in un'altra posizione del rigo
                    If c.Length < 7 Then Continue For
                    Dim ordinale As Integer
                    Dim ottava As Integer
                    If Not Integer.TryParse(c(4), ordinale) OrElse Not Integer.TryParse(c(6), ottava) Then Continue For
                    Dim elenco As List(Of XElement) = misura.Elements("note").ToList()
                    If ordinale < 0 OrElse ordinale >= elenco.Count Then Continue For
                    Dim el As XElement = elenco(ordinale)
                    If el.Element("pitch") Is Nothing OrElse el.Element("pitch").Element("step") Is Nothing Then Continue For
                    Dim prima As String = NomeNota(el.Element("pitch").Element("step").Value.Trim(), 0, Intero(el.Element("pitch"), "octave", 4))
                    Dim nota As New NotaInfo()
                    nota.El = el
                    nota.Voce = Intero(el, "voice", 1)
                    nota.Rigo = Intero(el, "staff", 1)
                    Dim passo As String = c(5)
                    Dim alter As Integer = AlterazioneInChiave(passo, mi.Stato.Quinte)
                    ImpostaAltezza(nota, passo, alter, ottava, mi.Stato.Quinte)
                    registra("la nota era letta " & prima & ", ma nel PDF la testa è in " & NomeNota(passo, alter, ottava) & ": corretta")
                ElseIf c(1) = "N" Then
                    ' una battuta vuota nello spartito letto, con un accordo nel PDF: si ricostruisce, dura tutta la battuta
                    If misura.Elements("note").Any() Then Continue For
                    Dim quarti As Double
                    If Not Double.TryParse(c(4), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, quarti) Then Continue For
                    Dim create As New List(Of XElement)()
                    Dim primo As Boolean = True
                    Dim riuscito As Boolean = True
                    Dim descrizione As New List(Of String)()
                    For Each voce As String In c(5).Split(";"c)
                        Dim q As String() = voce.Split(","c)
                        If q.Length < 3 Then
                            riuscito = False
                            Exit For
                        End If
                        Dim ottava As Integer
                        Dim alter As Integer
                        If Not Integer.TryParse(q(1), ottava) OrElse Not Integer.TryParse(q(2), alter) Then
                            riuscito = False
                            Exit For
                        End If
                        Dim nuova As New XElement("note")
                        If Not primo Then nuova.Add(New XElement("chord"))
                        nuova.Add(New XElement("pitch", New XElement("step", q(0)), New XElement("octave", ottava.ToString())))
                        nuova.Add(New XElement("duration", "1"))
                        nuova.Add(New XElement("voice", "1"))
                        Dim nodo As New NotaInfo()
                        nodo.El = nuova
                        nodo.Voce = 1
                        nodo.Rigo = 1
                        If Not ImpostaDurata(nodo, divisioni, quarti) Then
                            riuscito = False
                            Exit For
                        End If
                        ImpostaAltezza(nodo, q(0), alter, ottava, mi.Stato.Quinte)
                        create.Add(nuova)
                        descrizione.Add(NomeNota(q(0), alter, ottava))
                        primo = False
                    Next
                    If riuscito AndAlso create.Count > 0 Then
                        For Each nuova As XElement In create
                            misura.Add(nuova)
                        Next
                        registra("la battuta era vuota nello spartito letto: nel PDF c'è " & If(create.Count = 1, "la nota ", "l'accordo ") & String.Join(", ", descrizione) & ", lungo tutta la battuta. L'ho ricostruito dai caratteri del PDF")
                    End If
                End If
            Catch ex As Exception
                ' una correzione che non si può applicare si lascia stare
            End Try
        Next
        Return fatte
    End Function

    ''' <summary>Quante indicazioni di diteggiatura (i numeri da 1 a 5 vicino alle note) ci sono nello spartito.</summary>
    Public Function NumeroDiteggiature() As Integer
        Return _doc.Descendants("fingering").Count()
    End Function

    ''' <summary>Vero se lo spartito dice già a che velocità va suonato (numero del metronomo o tempo nel suono).</summary>
    Public Function TempoDichiarato() As Boolean
        Return _doc.Descendants("metronome").Any() OrElse _doc.Descendants("sound").Any(Function(s) s.Attribute("tempo") IsNot Nothing)
    End Function

    ''' <summary>Le parole scritte sopra le prime battute della prima parte (Allegro, = 120...), dove di solito sta l'indicazione di tempo.</summary>
    Public Function TestoIndicazioniTempo() As String
        If Parti.Count = 0 Then Return ""
        Dim parole As New List(Of String)()
        For Each misura As XElement In Parti(0).Elemento.Elements("measure").Take(4)
            For Each w As XElement In misura.Descendants("words")
                If w.Value.Trim() <> "" Then parole.Add(w.Value.Trim())
            Next
        Next
        Return String.Join(" ", parole)
    End Function

    ''' <summary>Scrive il titolo nello spartito se il riconoscimento non l'ha trovato. Vero se l'ha scritto.</summary>
    Public Function ImpostaTitoloSeMancante(titolo As String) As Boolean
        If String.IsNullOrWhiteSpace(titolo) Then Return False
        Dim radice As XElement = _doc.Root
        Dim opera As XElement = radice.Element("work")
        Dim titoloOpera As XElement = If(opera IsNot Nothing, opera.Element("work-title"), Nothing)
        If titoloOpera IsNot Nothing AndAlso titoloOpera.Value.Trim() <> "" Then Return False
        Dim titoloMovimento As XElement = radice.Element("movement-title")
        If titoloMovimento IsNot Nothing AndAlso titoloMovimento.Value.Trim() <> "" Then Return False
        If opera Is Nothing Then
            opera = New XElement("work")
            radice.AddFirst(opera)
        End If
        If titoloOpera Is Nothing Then
            titoloOpera = New XElement("work-title")
            opera.AddFirst(titoloOpera)
        End If
        titoloOpera.Value = titolo.Trim()
        Return True
    End Function

    ' ---- salvataggio -------------------------------------------------------------------------------------

    Public Sub Salva(percorso As String, compresso As Boolean)
        Directory.CreateDirectory(Path.GetDirectoryName(percorso))
        If Not compresso Then
            Using w As New StreamWriter(percorso, False, New UTF8Encoding(False))
                _doc.Save(w)
            End Using
            Return
        End If
        If File.Exists(percorso) Then File.Delete(percorso)
        Using zip As ZipArchive = ZipFile.Open(percorso, ZipArchiveMode.Create)
            Dim nomeInterno As String = Path.GetFileNameWithoutExtension(percorso) & ".musicxml"
            Dim cont As ZipArchiveEntry = zip.CreateEntry("META-INF/container.xml")
            Using s As New StreamWriter(cont.Open(), New UTF8Encoding(False))
                s.Write("<?xml version=""1.0"" encoding=""UTF-8""?><container><rootfiles><rootfile full-path=""" & nomeInterno & """ media-type=""application/vnd.recordare.musicxml+xml""/></rootfiles></container>")
            End Using
            Dim voce As ZipArchiveEntry = zip.CreateEntry(nomeInterno)
            Using s As New StreamWriter(voce.Open(), New UTF8Encoding(False))
                _doc.Save(s)
            End Using
        End Using
    End Sub

End Class

' ---------------------------------------------------------------------------------------------
' l'interfaccia della revisione
' ---------------------------------------------------------------------------------------------

''' <summary>L'elenco dei problemi trovati nello spartito.</summary>
Friend NotInheritable Class ElencoProblemi

    Private ReadOnly _revisione As RevisioneMusicXml
    Private ReadOnly _apriBattuta As Action(Of Integer, Integer)

    Public Sub New(revisione As RevisioneMusicXml, apriBattuta As Action(Of Integer, Integer))
        _revisione = revisione
        _apriBattuta = apriBattuta
    End Sub

    Public Sub Avvia()
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto controllando lo spartito, attendere", False)
        Task.Run(Sub()
                     Dim problemi As List(Of ProblemaSpartito) = Nothing
                     Dim errore As String = ""
                     Try
                         problemi = _revisione.Problemi()
                     Catch ex As Exception
                         errore = "Non è stato possibile controllare lo spartito: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If errore <> "" Then
                                                       AiutoMusica.Messaggio(errore)
                                                       Return
                                                   End If
                                                   Mostra(problemi)
                                               End Sub)
                 End Sub)
    End Sub

    Private Sub Mostra(problemi As List(Of ProblemaSpartito))
        If problemi.Count = 0 Then
            AiutoMusica.Messaggio("Il controllo non ha trovato problemi: le durate delle battute tornano, le note sono nell'estensione degli strumenti e le alterazioni sono coerenti. Il controllo non può però accorgersi di una nota sbagliata che sia comunque plausibile.")
            Return
        End If
        Dim menu As New WGMenu()
        For Each p As ProblemaSpartito In problemi
            Dim scelto As ProblemaSpartito = p
            menu.aggiungi(scelto.Testo, Sub(s As Object, e As EventArgs) _apriBattuta(scelto.Parte, scelto.Misura))
        Next
        menu.avvia(problemi.Count.ToString() & If(problemi.Count = 1, " dubbio trovato", " dubbi trovati"))
    End Sub

End Class

''' <summary>Le note di una battuta, lette una per una, e le correzioni.</summary>
Friend NotInheritable Class EditorBattuta

    Private ReadOnly _revisione As RevisioneMusicXml
    Private ReadOnly _parte As Integer
    Private ReadOnly _misura As Integer
    Private ReadOnly _alCambio As Action
    Private ReadOnly _ascolta As Action(Of Integer, Integer)
    Private ReadOnly _log As New Logger()
    Private _menuNote As WGMenu
    Private _menuAzioni As WGMenu
    Private _info As MisuraInfo

    Public Sub New(revisione As RevisioneMusicXml, parte As Integer, misura As Integer, alCambio As Action, ascolta As Action(Of Integer, Integer))
        _revisione = revisione
        _parte = parte
        _misura = misura
        _alCambio = alCambio
        _ascolta = ascolta
    End Sub

    Public Sub Avvia()
        Try
            Dim misure As List(Of MisuraInfo) = _revisione.Analizza(_parte)
            If _misura < 0 OrElse _misura >= misure.Count Then
                AiutoMusica.Messaggio("Questa battuta non esiste.")
                Return
            End If
            _info = misure(_misura)
            Dim menu As New WGMenu()
            _menuNote = menu
            Dim i As Integer = 0
            For Each n As NotaInfo In _info.Note
                i += 1
                Dim scelta As NotaInfo = n
                menu.aggiungi(i.ToString() & ". " & RevisioneMusicXml.Descrizione(n, _info.Stato.Divisioni), Sub(s As Object, e As EventArgs) AzioniNota(scelta))
            Next
            menu.aggiungi("Ascolta questa battuta", Sub(s As Object, e As EventArgs) _ascolta(_parte, _misura))
            If _info.Note.Count > 0 Then
                menu.aggiungi("Aggiungi una nota in fondo", Sub(s As Object, e As EventArgs) AggiungiInFondo(False))
                menu.aggiungi("Aggiungi una pausa in fondo", Sub(s As Object, e As EventArgs) AggiungiInFondo(True))
            End If
            menu.avvia("Battuta " & _info.Numero & ", " & _revisione.Parti(_parte).Nome)
        Catch ex As Exception
            _log.ScriviLog("EditorBattuta.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire la battuta.")
        End Try
    End Sub

    Private Sub Rifresca()
        Try
            If _menuAzioni IsNot Nothing Then _menuAzioni.chiudi()
        Catch
        End Try
        Try
            If _menuNote IsNot Nothing Then _menuNote.chiudi()
        Catch
        End Try
        _menuAzioni = Nothing
        _menuNote = Nothing
        _alCambio()
        Avvia()
    End Sub

    Private Sub AzioniNota(n As NotaInfo)
        Try
            Dim menu As New WGMenu()
            _menuAzioni = menu
            If Not n.Grazia Then
                menu.aggiungi("Cambia l'altezza", Sub(s As Object, e As EventArgs) CambiaAltezza(n))
                menu.aggiungi("Cambia la durata", Sub(s As Object, e As EventArgs) CambiaDurata(n))
                If Not n.Accordo Then menu.aggiungi("Trasforma in pausa", Sub(s As Object, e As EventArgs)
                                                                              _revisione.ImpostaPausa(n)
                                                                              Rifresca()
                                                                          End Sub)
                menu.aggiungi("Aggiungi una nota dopo", Sub(s As Object, e As EventArgs) AggiungiDopo(n, False))
                menu.aggiungi("Aggiungi una pausa dopo", Sub(s As Object, e As EventArgs) AggiungiDopo(n, True))
            End If
            menu.aggiungi("Elimina", Sub(s As Object, e As EventArgs)
                                         _revisione.Elimina(n)
                                         Rifresca()
                                     End Sub)
            menu.avvia(RevisioneMusicXml.Descrizione(n, _info.Stato.Divisioni))
        Catch ex As Exception
            _log.ScriviLog("EditorBattuta.AzioniNota: " & ex.Message)
        End Try
    End Sub

    Private Sub CambiaAltezza(n As NotaInfo)
        AiutoRevisione.ChiediTesto("Nuova altezza", If(n.Pausa OrElse n.Passo = "", "", RevisioneMusicXml.NomeNota(n.Passo, n.Alter, n.Ottava)),
            Sub(testo As String)
                Dim passo As String = ""
                Dim alter As Integer = 0
                Dim ottava As Integer = 0
                If Not RevisioneMusicXml.LeggiAltezza(testo, passo, alter, ottava) Then
                    AiutoMusica.Messaggio("Non ho capito la nota. Scrivi il nome, l'eventuale diesis o bemolle e l'ottava, per esempio re diesis 5.")
                    Return
                End If
                _revisione.ImpostaAltezza(n, passo, alter, ottava, _info.Stato.Quinte)
                Rifresca()
            End Sub)
    End Sub

    Private Sub MenuDurate(titolo As String, alScelta As Action(Of Double))
        Dim menu As New WGMenu()
        Dim nomi As String() = RevisioneMusicXml.NomiDurate()
        For i As Integer = 0 To nomi.Length - 1
            Dim indice As Integer = i
            menu.aggiungi(nomi(i), Sub(s As Object, e As EventArgs)
                                       Try
                                           menu.chiudi()
                                       Catch
                                       End Try
                                       alScelta(RevisioneMusicXml.QuartiDellaDurata(indice))
                                   End Sub)
        Next
        menu.avvia(titolo)
    End Sub

    Private Sub CambiaDurata(n As NotaInfo)
        MenuDurate("Nuova durata", Sub(quarti As Double)
                                       If Not _revisione.ImpostaDurata(n, _info.Stato.Divisioni, quarti) Then
                                           AiutoMusica.Messaggio("Questa durata non è possibile con la suddivisione di questo spartito.")
                                           Return
                                       End If
                                       Rifresca()
                                   End Sub)
    End Sub

    Private Sub AggiungiDopo(n As NotaInfo, pausa As Boolean)
        MenuDurate("Durata", Sub(quarti As Double)
                                 If pausa Then
                                     If Not _revisione.AggiungiDopo(n, _info.Stato.Divisioni, quarti, "", 0, 0, _info.Stato.Quinte) Then
                                         AiutoMusica.Messaggio("Questa durata non è possibile con la suddivisione di questo spartito.")
                                         Return
                                     End If
                                     Rifresca()
                                 Else
                                     AiutoRevisione.ChiediTesto("Altezza", "",
                                         Sub(testo As String)
                                             Dim passo As String = ""
                                             Dim alter As Integer = 0
                                             Dim ottava As Integer = 0
                                             If Not RevisioneMusicXml.LeggiAltezza(testo, passo, alter, ottava) Then
                                                 AiutoMusica.Messaggio("Non ho capito la nota.")
                                                 Return
                                             End If
                                             If Not _revisione.AggiungiDopo(n, _info.Stato.Divisioni, quarti, passo, alter, ottava, _info.Stato.Quinte) Then
                                                 AiutoMusica.Messaggio("Questa durata non è possibile con la suddivisione di questo spartito.")
                                                 Return
                                             End If
                                             Rifresca()
                                         End Sub)
                                 End If
                             End Sub)
    End Sub

    Private Sub AggiungiInFondo(pausa As Boolean)
        Dim ultima As NotaInfo = _info.Note.LastOrDefault(Function(x) Not x.Accordo)
        If ultima Is Nothing Then Return
        AggiungiDopo(ultima, pausa)
    End Sub

End Class
