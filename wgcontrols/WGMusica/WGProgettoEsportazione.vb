' ============================================================================================
' WGProgettoEsportazione - dal progetto musicale ai file: MusicXML, MIDI, mp3 e wav
' ============================================================================================
'
' MIDI e MusicXML partono dalle note del progetto. Le note sono state suonate a mano, senza un metronomo: per la notazione si
' assume un tempo fisso di 120 al minuto in 4/4 e le note si arrotondano alla semicroma, quindi lo spartito è una trascrizione
' vicina a quello che si è suonato, non una lettura del ritmo "inteso". mp3 e wav si ottengono facendo suonare il progetto al
' motore audio, più veloce del tempo reale, e comprendono il sottofondo se c'era.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Xml
Imports System.Xml.Linq
Imports NAudio.Midi
Imports NAudio.Wave

Public NotInheritable Class ProgettoEsportazione

    Private Sub New()
    End Sub

    Private Shared ReadOnly Cultura As CultureInfo = CultureInfo.InvariantCulture

    ' ---- MIDI -------------------------------------------------------------------------------------------

    Private Const TicksPerQuarto As Integer = 480

    Public Shared Sub ScriviMidi(progetto As ProgettoMusicale, percorso As String)
        Dim bpm As Double = Math.Max(20, Math.Min(400, progetto.Tempo))
        Dim ticksAlSecondo As Double = TicksPerQuarto * bpm / 60.0
        Dim eventi As New MidiEventCollection(1, TicksPerQuarto)
        eventi.AddEvent(New TempoEvent(CInt(60000000.0 / bpm), 0), 0)
        eventi.AddEvent(New TimeSignatureEvent(0, Math.Max(1, progetto.Battiti), 2, 24, 8), 0)
        eventi.AddEvent(New TextEvent(progetto.Nome, MetaEventType.SequenceTrackName, 0), 0)
        Dim canale As Integer = 1
        For i As Integer = 0 To progetto.Tracce.Count - 1
            Dim t As TracciaProgetto = progetto.Tracce(i)
            Dim traccia As Integer = i + 1
            Dim c As Integer
            If t.Percussivo Then
                c = 10
            Else
                c = canale
                canale += 1
                If canale = 10 Then canale += 1
                If canale > 16 Then canale = 1
            End If
            eventi.AddEvent(New TextEvent(t.NomeStrumento, MetaEventType.SequenceTrackName, 0), traccia)
            If Not t.Percussivo Then eventi.AddEvent(New PatchChangeEvent(0, c, Math.Max(0, t.Programma)), traccia)
            eventi.AddEvent(New ControlChangeEvent(0, c, MidiController.MainVolume, Math.Max(0, Math.Min(127, CInt(Math.Round(100.0 * t.Volume / 100.0))))), traccia)
            For Each n As NotaSpartito In t.Note
                Dim nota As Integer = NotaMidi(t, n.Nota)
                Dim inizio As Long = CLng(Math.Round(n.Inizio * ticksAlSecondo))
                Dim fine As Long = Math.Max(inizio + 1, CLng(Math.Round((n.Inizio + n.Durata) * ticksAlSecondo)))
                Dim velocita As Integer = Math.Max(1, Math.Min(127, CInt(Math.Round(n.Velocita * 127.0))))
                eventi.AddEvent(New NoteEvent(inizio, c, MidiCommandCode.NoteOn, nota, velocita), traccia)
                eventi.AddEvent(New NoteEvent(fine, c, MidiCommandCode.NoteOff, nota, 0), traccia)
            Next
        Next
        eventi.PrepareForExport()
        MidiFile.Export(percorso, eventi)
    End Sub

    ''' <summary>Per la batteria il numero del pezzo diventa la nota General MIDI di quel pezzo.</summary>
    Private Shared Function NotaMidi(t As TracciaProgetto, nota As Integer) As Integer
        Dim n As Integer = nota
        If t.Percussivo Then n = If(t.Origine = "sf", CatalogoSoundFont.NotaGmBatteria(nota), 36 + ((nota Mod 40) + 40) Mod 40)
        Return Math.Max(0, Math.Min(127, n))
    End Function

    ' ---- MusicXML ---------------------------------------------------------------------------------------

    ''' <summary>La griglia della notazione: una suddivisione è una semicroma (a 120 al minuto dura 0,125 secondi); una battuta ne ha quattro per battito.</summary>
    Private NotInheritable Class Griglia
        Public SuddivisioneSec As Double
        Public PerBattuta As Integer
        Public Battiti As Integer
        Public Tempo As Integer
    End Class

    Private NotInheritable Class Gruppo
        Public Inizio As Integer
        Public Durata As Integer
        Public Note As New List(Of Integer)()
    End Class

    Private Shared ReadOnly Passi() As String = {"C", "C", "D", "D", "E", "F", "F", "G", "G", "A", "A", "B"}
    Private Shared ReadOnly Alterazioni() As Integer = {0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0}
    Private Shared ReadOnly PassiBatteria() As String = {"C", "D", "E", "F", "G", "A", "B"}

    Public Shared Sub ScriviMusicXml(progetto As ProgettoMusicale, percorso As String)
        Dim griglia As New Griglia()
        griglia.Tempo = Math.Max(20, Math.Min(400, progetto.Tempo))
        griglia.Battiti = Math.Max(1, Math.Min(16, progetto.Battiti))
        griglia.SuddivisioneSec = 60.0 / griglia.Tempo / 4.0
        griglia.PerBattuta = griglia.Battiti * 4
        Dim elenco As New XElement("part-list")
        Dim parti As New List(Of XElement)()
        Dim lunghezzaBattute As Integer = 1
        Dim tracceGruppi As New List(Of List(Of List(Of Gruppo)))()
        For Each t As TracciaProgetto In progetto.Tracce
            Dim voci As List(Of List(Of Gruppo)) = CostruisciVoci(t, griglia)
            tracceGruppi.Add(voci)
            For Each v As List(Of Gruppo) In voci
                If v.Count > 0 Then
                    Dim ultimo As Gruppo = v(v.Count - 1)
                    lunghezzaBattute = Math.Max(lunghezzaBattute, (ultimo.Inizio + ultimo.Durata + griglia.PerBattuta - 1) \ griglia.PerBattuta)
                End If
            Next
        Next
        For i As Integer = 0 To progetto.Tracce.Count - 1
            Dim t As TracciaProgetto = progetto.Tracce(i)
            Dim id As String = "P" & (i + 1).ToString(Cultura)
            Dim nome As String = t.NomeStrumento
            Dim programma As Integer = Math.Max(0, t.Programma)
            Dim scorePart As New XElement("score-part", New XAttribute("id", id), New XElement("part-name", nome))
            scorePart.Add(New XElement("score-instrument", New XAttribute("id", id & "-I1"), New XElement("instrument-name", nome)))
            scorePart.Add(New XElement("midi-instrument", New XAttribute("id", id & "-I1"),
                                       New XElement("midi-channel", If(t.Percussivo, 10, 1)),
                                       New XElement("midi-program", programma + 1),
                                       New XElement("volume", Math.Min(100, t.Volume))))
            elenco.Add(scorePart)
            parti.Add(CostruisciParte(t, id, tracceGruppi(i), lunghezzaBattute, i = 0, griglia))
        Next
        Dim radice As New XElement("score-partwise", New XAttribute("version", "3.1"),
                                   New XElement("work", New XElement("work-title", If(progetto.Nome = "", "Registrazione", progetto.Nome))),
                                   New XElement("identification", New XElement("encoding", New XElement("software", "WinGabriel"))),
                                   elenco)
        For Each p As XElement In parti
            radice.Add(p)
        Next
        Dim doc As New XDocument(New XDeclaration("1.0", "UTF-8", Nothing), New XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 3.1 Partwise//EN", "http://www.musicxml.org/dtds/partwise.dtd", Nothing), radice)
        Dim impostazioni As New XmlWriterSettings() With {.Indent = True, .Encoding = New System.Text.UTF8Encoding(False)}
        Using w As XmlWriter = XmlWriter.Create(percorso, impostazioni)
            doc.Save(w)
        End Using
    End Sub

    ''' <summary>
    ''' Le note quantizzate, raggruppate per accordi (stesso inizio e stessa durata) e distribuite in voci: una voce non ha due
    ''' gruppi che si sovrappongono.
    ''' </summary>
    Private Shared Function CostruisciVoci(t As TracciaProgetto, griglia As Griglia) As List(Of List(Of Gruppo))
        Dim gruppi As New Dictionary(Of String, Gruppo)()
        For Each n As NotaSpartito In t.Note
            Dim inizio As Integer = CInt(Math.Round(n.Inizio / griglia.SuddivisioneSec))
            Dim durata As Integer = Math.Max(1, CInt(Math.Round(n.Durata / griglia.SuddivisioneSec)))
            Dim chiave As String = inizio.ToString(Cultura) & "/" & durata.ToString(Cultura)
            Dim g As Gruppo = Nothing
            If Not gruppi.TryGetValue(chiave, g) Then
                g = New Gruppo() With {.Inizio = inizio, .Durata = durata}
                gruppi(chiave) = g
            End If
            If Not g.Note.Contains(n.Nota) Then g.Note.Add(n.Nota)
        Next
        Dim ordinati As List(Of Gruppo) = gruppi.Values.OrderBy(Function(g) g.Inizio).ThenBy(Function(g) g.Durata).ToList()
        Dim voci As New List(Of List(Of Gruppo))()
        For Each g As Gruppo In ordinati
            Dim messo As Boolean = False
            For Each v As List(Of Gruppo) In voci
                Dim ultimo As Gruppo = v(v.Count - 1)
                If ultimo.Inizio + ultimo.Durata <= g.Inizio Then
                    v.Add(g)
                    messo = True
                    Exit For
                End If
            Next
            If Not messo AndAlso voci.Count < 8 Then voci.Add(New List(Of Gruppo)() From {g})
        Next
        If voci.Count = 0 Then voci.Add(New List(Of Gruppo)())
        Return voci
    End Function

    Private Shared Function CostruisciParte(t As TracciaProgetto, id As String, voci As List(Of List(Of Gruppo)), battute As Integer, primaParte As Boolean, griglia As Griglia) As XElement
        Dim parte As New XElement("part", New XAttribute("id", id))
        Dim alta As Boolean = True
        If Not t.Percussivo Then
            Dim somma As Long = 0
            Dim quante As Integer = 0
            For Each n As NotaSpartito In t.Note
                somma += n.Nota
                quante += 1
            Next
            alta = quante = 0 OrElse somma / quante >= 58
        End If
        For m As Integer = 0 To battute - 1
            Dim misura As New XElement("measure", New XAttribute("number", (m + 1).ToString(Cultura)))
            If m = 0 Then
                Dim attributi As New XElement("attributes", New XElement("divisions", 4),
                                              New XElement("key", New XElement("fifths", 0)),
                                              New XElement("time", New XElement("beats", griglia.Battiti), New XElement("beat-type", 4)))
                If t.Percussivo Then
                    attributi.Add(New XElement("clef", New XElement("sign", "percussion"), New XElement("line", 2)))
                ElseIf alta Then
                    attributi.Add(New XElement("clef", New XElement("sign", "G"), New XElement("line", 2)))
                Else
                    attributi.Add(New XElement("clef", New XElement("sign", "F"), New XElement("line", 4)))
                End If
                misura.Add(attributi)
                If primaParte Then
                    misura.Add(New XElement("direction", New XAttribute("placement", "above"),
                                            New XElement("direction-type", New XElement("metronome", New XElement("beat-unit", "quarter"), New XElement("per-minute", griglia.Tempo))),
                                            New XElement("sound", New XAttribute("tempo", griglia.Tempo))))
                End If
            End If
            For v As Integer = 0 To voci.Count - 1
                If v > 0 Then misura.Add(New XElement("backup", New XElement("duration", griglia.PerBattuta)))
                ScriviVoceNellaMisura(misura, t, voci(v), v + 1, m * griglia.PerBattuta, (m + 1) * griglia.PerBattuta)
            Next
            parte.Add(misura)
        Next
        Return parte
    End Function

    ''' <summary>Scrive una voce per una battuta: le note che cadono lì (tagliate ai bordi e legate), con le pause nei vuoti.</summary>
    Private Shared Sub ScriviVoceNellaMisura(misura As XElement, t As TracciaProgetto, voce As List(Of Gruppo), numeroVoce As Integer, da As Integer, a As Integer)
        Dim posizione As Integer = da
        For Each g As Gruppo In voce
            Dim fineGruppo As Integer = g.Inizio + g.Durata
            If fineGruppo <= da OrElse g.Inizio >= a Then Continue For
            Dim inizio As Integer = Math.Max(g.Inizio, da)
            Dim fine As Integer = Math.Min(fineGruppo, a)
            If inizio > posizione Then ScriviPause(misura, inizio - posizione, numeroVoce)
            Dim leghiPrima As Boolean = g.Inizio < da
            Dim leghiDopo As Boolean = fineGruppo > a
            ScriviNote(misura, t, g.Note, fine - inizio, numeroVoce, leghiPrima, leghiDopo)
            posizione = fine
        Next
        If posizione < a Then ScriviPause(misura, a - posizione, numeroVoce)
    End Sub

    Private Shared ReadOnly Valori() As Integer = {16, 12, 8, 6, 4, 3, 2, 1}

    Private Shared Function Scomponi(durata As Integer) As List(Of Integer)
        Dim pezzi As New List(Of Integer)()
        Dim resto As Integer = durata
        While resto > 0
            For Each v As Integer In Valori
                If v <= resto Then
                    pezzi.Add(v)
                    resto -= v
                    Exit For
                End If
            Next
        End While
        Return pezzi
    End Function

    Private Shared Sub AggiungiTipo(nota As XElement, valore As Integer)
        Select Case valore
            Case 16 : nota.Add(New XElement("type", "whole"))
            Case 12 : nota.Add(New XElement("type", "half"), New XElement("dot"))
            Case 8 : nota.Add(New XElement("type", "half"))
            Case 6 : nota.Add(New XElement("type", "quarter"), New XElement("dot"))
            Case 4 : nota.Add(New XElement("type", "quarter"))
            Case 3 : nota.Add(New XElement("type", "eighth"), New XElement("dot"))
            Case 2 : nota.Add(New XElement("type", "eighth"))
            Case Else : nota.Add(New XElement("type", "16th"))
        End Select
    End Sub

    Private Shared Sub ScriviPause(misura As XElement, durata As Integer, numeroVoce As Integer)
        For Each valore As Integer In Scomponi(durata)
            Dim pausa As New XElement("note", New XElement("rest"), New XElement("duration", valore), New XElement("voice", numeroVoce))
            AggiungiTipo(pausa, valore)
            misura.Add(pausa)
        Next
    End Sub

    Private Shared Sub ScriviNote(misura As XElement, t As TracciaProgetto, note As List(Of Integer), durata As Integer, numeroVoce As Integer, leghiPrima As Boolean, leghiDopo As Boolean)
        Dim pezzi As List(Of Integer) = Scomponi(durata)
        For p As Integer = 0 To pezzi.Count - 1
            Dim legaPrima As Boolean = leghiPrima OrElse p > 0
            Dim legaDopo As Boolean = leghiDopo OrElse p < pezzi.Count - 1
            For k As Integer = 0 To note.Count - 1
                Dim nota As New XElement("note")
                If k > 0 Then nota.Add(New XElement("chord"))
                nota.Add(Altezza(t, note(k)))
                nota.Add(New XElement("duration", pezzi(p)))
                If legaPrima Then nota.Add(New XElement("tie", New XAttribute("type", "stop")))
                If legaDopo Then nota.Add(New XElement("tie", New XAttribute("type", "start")))
                nota.Add(New XElement("voice", numeroVoce))
                AggiungiTipo(nota, pezzi(p))
                If legaPrima OrElse legaDopo Then
                    Dim legature As New XElement("notations")
                    If legaPrima Then legature.Add(New XElement("tied", New XAttribute("type", "stop")))
                    If legaDopo Then legature.Add(New XElement("tied", New XAttribute("type", "start")))
                    nota.Add(legature)
                End If
                misura.Add(nota)
            Next
        Next
    End Sub

    Private Shared Function Altezza(t As TracciaProgetto, nota As Integer) As XElement
        If t.Percussivo Then
            Dim i As Integer = Math.Max(0, nota)
            Return New XElement("unpitched", New XElement("display-step", PassiBatteria(i Mod 7)), New XElement("display-octave", 4 + (i \ 7) Mod 3))
        End If
        Dim n As Integer = Math.Max(0, Math.Min(127, nota))
        Dim pitch As New XElement("pitch", New XElement("step", Passi(n Mod 12)))
        If Alterazioni(n Mod 12) <> 0 Then pitch.Add(New XElement("alter", Alterazioni(n Mod 12)))
        pitch.Add(New XElement("octave", n \ 12 - 1))
        Return pitch
    End Function

    ' ---- audio ------------------------------------------------------------------------------------------

    Private NotInheritable Class EventoAudio
        Public Fotogramma As Long
        Public Acceso As Boolean
        Public Chiave As Integer
        Public Nota As Integer
        Public Velocita As Double
        Public Strumento As DefinizioneStrumento
        Public Guadagno As Double = 1.0
    End Class

    ''' <summary>
    ''' Fa suonare il progetto al motore audio, senza scheda audio e più in fretta del tempo reale, e lo scrive in un file wav a
    ''' virgola mobile. Restituisce "" se è andata bene, altrimenti il motivo.
    ''' </summary>
    Public Shared Function RendiInWav(progetto As ProgettoMusicale, percorsoWav As String, volumeStrumento As Single, volumeSottofondo As Single, riverbero As Single) As String
        Dim motore As New MotoreSintesi()
        Try
            motore.VolumeStrumento = volumeStrumento
            motore.VolumeSottofondo = volumeSottofondo
            motore.ScalaRiverbero = riverbero
            motore.SottofondoRipetuto = progetto.SottofondoRipetuto
            Dim eventi As New List(Of EventoAudio)()
            Dim chiave As Integer = 1
            For Each t As TracciaProgetto In progetto.Tracce
                If t.Strumento Is Nothing Then Continue For
                For Each n As NotaSpartito In t.Note
                    eventi.Add(New EventoAudio() With {.Fotogramma = CLng(n.Inizio * 48000.0), .Acceso = True, .Chiave = chiave, .Nota = n.Nota, .Velocita = n.Velocita, .Strumento = t.Strumento, .Guadagno = t.Volume / 100.0})
                    Dim durata As Double = If(t.Percussivo, Math.Min(n.Durata, 0.4), n.Durata * 0.98)
                    eventi.Add(New EventoAudio() With {.Fotogramma = CLng((n.Inizio + durata) * 48000.0), .Acceso = False, .Chiave = chiave})
                    chiave += 1
                Next
            Next
            eventi.Sort(Function(a As EventoAudio, b As EventoAudio)
                            Dim c As Integer = a.Fotogramma.CompareTo(b.Fotogramma)
                            If c <> 0 Then Return c
                            Return a.Acceso.CompareTo(b.Acceso)
                        End Function)
            If progetto.Sottofondo <> "" AndAlso File.Exists(progetto.Sottofondo) Then
                motore.CaricaSottofondo(New SottofondoAudio(progetto.Sottofondo))
                motore.AvviaSottofondo(True)
            End If
            Dim totale As Long = CLng((progetto.Durata + 3.0) * 48000.0)
            Dim buffer(128 * 8 - 1) As Byte
            Dim prossimo As Integer = 0
            Using scrittore As New WaveFileWriter(percorsoWav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))
                Dim fotogramma As Long = 0
                While fotogramma < totale
                    Dim n As Integer = CInt(Math.Min(128L, totale - fotogramma))
                    While prossimo < eventi.Count AndAlso eventi(prossimo).Fotogramma < fotogramma + n
                        Dim e As EventoAudio = eventi(prossimo)
                        If e.Acceso Then motore.NotaOnStrumento(e.Chiave, e.Nota, e.Velocita, e.Strumento, e.Guadagno) Else motore.NotaOff(e.Chiave)
                        prossimo += 1
                    End While
                    motore.Read(buffer, 0, n * 8)
                    scrittore.Write(buffer, 0, n * 8)
                    fotogramma += n
                End While
            End Using
            Return ""
        Catch ex As Exception
            Return "Non è stato possibile suonare il progetto in un file: " & ex.Message
        Finally
            Try
                motore.Dispose()
            Catch
            End Try
        End Try
    End Function

End Class
