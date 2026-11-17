' ============================================================================================
' WGSpartitiVerifica - controllare il riconoscimento di uno spartito senza fidarsi di una sola lettura
' ============================================================================================
'
' Una nota sbagliata che però ha la durata giusta, sta nella tonalità e nell'estensione, non la scopre nessun controllo
' fatto sulle note: l'unica verifica indipendente è guardare di nuovo la pagina, o leggerla una seconda volta in un altro modo.
' Qui ci sono i due modi:
'
'   1. VerificaConImmagine: dopo il riconoscimento Audiveris lascia, oltre al MusicXML, il suo lavoro completo (file .omr): la pagina
'      in bianco e nero, la posizione di ogni segno riconosciuto e quanta sicurezza ha avuto nel riconoscerlo. Si cancellano dalla pagina
'      i righi e tutti i segni riconosciuti: se resta dell'inchiostro dentro un rigo, quel segno è stato visto e non capito (una nota
'      persa, una legatura). Le note lette con poca sicurezza si segnano. Ogni dubbio finisce nella battuta e nella parte giuste.
'
'   2. ConfrontoLetture: lo stesso brano letto due volte (due scale diverse della stessa scansione, oppure due edizioni o la
'      partitura e le parti staccate) dà due MusicXML. Gli errori dei due riconoscimenti difficilmente cadono nello stesso punto.
'      Dove le due letture coincidono la battuta è sicura. Dove differiscono, si sceglie solo con un criterio certo: una
'      sola delle due fa tornare il conto delle durate (e rispetta estensione e tonalità); se tornano entrambe o nessuna,
'      decide l'immagine (quella che ha meno inchiostro senza spiegazione e più sicurezza); se nemmeno questo basta, resta un
'      dubbio nell'elenco del controllo, e non si indovina.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml.Linq

''' <summary>Un dubbio su una battuta di una parte (indici da zero, nell'ordine del MusicXML).</summary>
Public NotInheritable Class DubbioMisura
    Public Parte As Integer
    Public Misura As Integer
    Public Motivo As String = ""
End Class

Public NotInheritable Class RisultatoVerifica
    Public ReadOnly Dubbi As New List(Of DubbioMisura)()
    ''' <summary>Quanto una battuta è sospetta (chiave: parte * 1000000 + battuta): più è alto, meno l'immagine conferma la lettura.</summary>
    Public ReadOnly Penalita As New Dictionary(Of Long, Double)()
    Public Misure As Integer
    ''' <summary>Le battute (chiave come in Penalita) che il confronto fra due letture ha sostituito con quelle dell'altra lettura.</summary>
    Public ReadOnly Sostituite As New HashSet(Of Long)()

    Public Shared Function Chiave(parte As Integer, misura As Integer) As Long
        Return CLng(parte) * 1000000L + misura
    End Function

    Public Sub Segna(parte As Integer, misura As Integer, peso As Double, motivo As String)
        Dim k As Long = Chiave(parte, misura)
        Dim v As Double = 0
        Penalita.TryGetValue(k, v)
        Penalita(k) = v + peso
        If motivo <> "" Then
            For Each d As DubbioMisura In Dubbi
                If d.Parte = parte AndAlso d.Misura = misura AndAlso d.Motivo = motivo Then Return
            Next
            Dim nuovo As New DubbioMisura()
            nuovo.Parte = parte
            nuovo.Misura = misura
            nuovo.Motivo = motivo
            Dubbi.Add(nuovo)
        End If
    End Sub

    ' ---- il file che accompagna il MusicXML ------------------------------------------------------------------

    Public Sub Scrivi(percorso As String)
        Dim righe As New List(Of String)()
        For Each d As DubbioMisura In Dubbi
            righe.Add("D|" & d.Parte.ToString() & "|" & d.Misura.ToString() & "|" & d.Motivo.Replace("|", " ").Replace(vbCr, " ").Replace(vbLf, " "))
        Next
        For Each k As Long In Sostituite
            righe.Add("S|" & (k \ 1000000L).ToString() & "|" & (k Mod 1000000L).ToString() & "|-")
        Next
        For Each kv As KeyValuePair(Of Long, Double) In Penalita
            righe.Add("P|" & (kv.Key \ 1000000L).ToString() & "|" & (kv.Key Mod 1000000L).ToString() & "|" & kv.Value.ToString("0.###", CultureInfo.InvariantCulture))
        Next
        File.WriteAllLines(percorso, righe, New UTF8Encoding(True))
    End Sub

    Public Shared Function Leggi(percorso As String) As RisultatoVerifica
        Dim r As New RisultatoVerifica()
        If Not File.Exists(percorso) Then Return r
        For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
            Dim pezzi As String() = riga.Split("|"c)
            If pezzi.Length < 4 Then Continue For
            Dim parte As Integer
            Dim misura As Integer
            If Not Integer.TryParse(pezzi(1), parte) OrElse Not Integer.TryParse(pezzi(2), misura) Then Continue For
            If pezzi(0) = "D" Then
                Dim d As New DubbioMisura()
                d.Parte = parte
                d.Misura = misura
                d.Motivo = pezzi(3)
                r.Dubbi.Add(d)
            ElseIf pezzi(0) = "S" Then
                r.Sostituite.Add(Chiave(parte, misura))
            ElseIf pezzi(0) = "P" Then
                Dim v As Double
                If Double.TryParse(pezzi(3), NumberStyles.Float, CultureInfo.InvariantCulture, v) Then r.Penalita(Chiave(parte, misura)) = v
            End If
        Next
        Return r
    End Function
End Class

' ---------------------------------------------------------------------------------------------
' 1. il confronto con l'immagine della pagina
' ---------------------------------------------------------------------------------------------
Friend NotInheritable Class VerificaConImmagine

    Private Shared ReadOnly log As New Logger()
    ' una nota letta con meno sicurezza di così (su 1) è segnalata: su una partitura d'orchestra di cinquecento battute sono poche decine
    Private Const SogliaSicurezza As Double = 0.6

    Private Sub New()
    End Sub

    Private Shared Function Num(e As XElement, nome As String, predefinito As Double) As Double
        If e Is Nothing OrElse e.Attribute(nome) Is Nothing Then Return predefinito
        Dim v As Double
        If Double.TryParse(e.Attribute(nome).Value, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return predefinito
    End Function

    ''' <summary>
    ''' Analizza il lavoro di Audiveris (.omr) di un brano. progresso riceve da 0 a 100; annulla dice se fermarsi.
    ''' Funziona per un file con un solo brano: le battute si contano di seguito, pagina dopo pagina.
    ''' </summary>
    Public Shared Function Analizza(percorsoOmr As String, progresso As Action(Of Integer), annulla As Func(Of Boolean)) As RisultatoVerifica
        Dim risultato As New RisultatoVerifica()
        Using zip As ZipArchive = ZipFile.OpenRead(percorsoOmr)
            Dim fogli As New List(Of Integer)()
            For Each e As ZipArchiveEntry In zip.Entries
                Dim m As Match = Regex.Match(e.FullName, "^sheet#(\d+)/sheet#\1\.xml$")
                If m.Success Then fogli.Add(Integer.Parse(m.Groups(1).Value))
            Next
            fogli.Sort()
            Dim primaBattuta As Integer = 0
            For i As Integer = 0 To fogli.Count - 1
                If annulla() Then Exit For
                progresso(CInt(i * 100L \ Math.Max(1, fogli.Count)))
                Try
                    primaBattuta += AnalizzaFoglio(zip, fogli(i), primaBattuta, risultato)
                Catch ex As Exception
                    log.ScriviLog("VerificaConImmagine.Analizza (pagina " & fogli(i).ToString() & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
                    ' una pagina che non si riesce a leggere sposta il conteggio delle battute: meglio fermarsi che segnare battute sbagliate
                    risultato.Dubbi.Clear()
                    risultato.Penalita.Clear()
                    Exit For
                End Try
            Next
            risultato.Misure = primaBattuta
        End Using
        progresso(100)
        Return risultato
    End Function

    Private Class SegnoResiduo
        Public Rettangolo As Rectangle
        Public Area As Integer
    End Class

    Private Class Rigo
        Public Sinistra As Integer
        Public Destra As Integer
        Public Alto As Integer
        Public Basso As Integer
        Public Parte As Integer
    End Class

    ''' <summary>Analizza una pagina; restituisce quante battute ci sono in ogni parte.</summary>
    Private Shared Function AnalizzaFoglio(zip As ZipArchive, numero As Integer, primaBattuta As Integer, risultato As RisultatoVerifica) As Integer
        Dim voce As ZipArchiveEntry = zip.GetEntry("sheet#" & numero.ToString() & "/sheet#" & numero.ToString() & ".xml")
        Dim doc As XDocument
        Using s As Stream = voce.Open()
            doc = XDocument.Load(s)
        End Using
        Dim scala As XElement = doc.Root.Element("scale")
        Dim interlinea As Integer = CInt(Math.Round(Num(If(scala IsNot Nothing, scala.Element("interline"), Nothing), "main", 0)))

        Dim larghezza As Integer = 0
        Dim altezza As Integer = 0
        Dim inchiostro As Boolean() = If(interlinea > 0, LeggiInchiostro(zip, numero, larghezza, altezza), Nothing)
        Dim mascherato As Boolean() = Nothing
        If inchiostro IsNot Nothing Then mascherato = New Boolean(inchiostro.Length - 1) {}

        Dim battuteFoglio As Integer = 0
        For Each sistema As XElement In doc.Root.Descendants("system")
            Dim pile As List(Of XElement) = sistema.Elements("stack").ToList()
            If pile.Count = 0 Then Continue For
            Dim staffParte As New Dictionary(Of String, Integer)()
            Dim righi As New List(Of Rigo)()
            Dim indiceParte As Integer = 0
            For Each parte As XElement In sistema.Elements("part")
                For Each st As XElement In parte.Elements("staff")
                    Dim id As String = If(st.Attribute("id") IsNot Nothing, st.Attribute("id").Value, "")
                    staffParte(id) = indiceParte
                    Dim ys As New List(Of Double)()
                    For Each linea As XElement In st.Descendants("line")
                        Dim punti As List(Of XElement) = linea.Elements("point").ToList()
                        If punti.Count >= 2 Then
                            ys.Add((Num(punti(0), "y", 0) + Num(punti(punti.Count - 1), "y", 0)) / 2.0)
                            If mascherato IsNot Nothing Then DisegnaLinea(mascherato, larghezza, altezza, Num(punti(0), "x", 0), Num(punti(0), "y", 0), Num(punti(punti.Count - 1), "x", 0), Num(punti(punti.Count - 1), "y", 0), CInt(Math.Round(Num(linea, "thickness", 2))))
                        End If
                    Next
                    If ys.Count > 0 Then
                        Dim r As New Rigo()
                        r.Sinistra = CInt(Num(st, "left", 0))
                        r.Destra = CInt(Num(st, "right", 0))
                        r.Alto = CInt(ys.Min())
                        r.Basso = CInt(ys.Max())
                        r.Parte = indiceParte
                        righi.Add(r)
                    End If
                Next
                indiceParte += 1
            Next

            Dim battutaDelle As Func(Of Double, Integer) = Function(x As Double) As Integer
                                                               For k As Integer = 0 To pile.Count - 1
                                                                   If x < Num(pile(k), "right", 0) Then Return k
                                                               Next
                                                               Return pile.Count - 1
                                                           End Function

            ' le note lette con poca sicurezza
            For Each testa As XElement In sistema.Descendants("head")
                Dim sicurezza As Double = Num(testa, "ctx-grade", Num(testa, "grade", 1.0))
                If sicurezza >= SogliaSicurezza Then Continue For
                Dim staffId As String = If(testa.Attribute("staff") IsNot Nothing, testa.Attribute("staff").Value, "")
                Dim parte As Integer
                If Not staffParte.TryGetValue(staffId, parte) Then Continue For
                Dim limiti As XElement = testa.Element("bounds")
                If limiti Is Nothing Then Continue For
                Dim k As Integer = battutaDelle(Num(limiti, "x", 0) + Num(limiti, "w", 0) / 2.0)
                risultato.Segna(parte, primaBattuta + battuteFoglio + k, (0.85 - sicurezza) * 10.0, "una nota è stata letta con poca sicurezza")
            Next

            ' l'inchiostro che nessun segno riconosciuto spiega
            If inchiostro IsNot Nothing Then
                For Each el As XElement In sistema.Descendants()
                    Dim limiti As XElement = el.Element("bounds")
                    If limiti Is Nothing Then Continue For
                    Dim nome As String = el.Name.LocalName
                    If nome = "staff" OrElse nome = "part" OrElse nome = "system" Then Continue For
                    Dim x As Integer = CInt(Num(limiti, "x", 0))
                    Dim y As Integer = CInt(Num(limiti, "y", 0))
                    Dim w As Integer = CInt(Num(limiti, "w", 0))
                    Dim h As Integer = CInt(Num(limiti, "h", 0))
                    For yy As Integer = Math.Max(0, y - 2) To Math.Min(altezza - 1, y + h + 2)
                        For xx As Integer = Math.Max(0, x - 2) To Math.Min(larghezza - 1, x + w + 2)
                            mascherato(yy * larghezza + xx) = True
                        Next
                    Next
                Next
                ' Si segnala solo quello che cambierebbe il suono. Il resto dell'inchiostro avanzato (scritte come "cresc." e "a 2", archi delle
                ' corone, pezzi di legature di frase già riconosciute) è quasi sempre un falso allarme e si lascia stare:
                '   - un segno pieno, grande come una testa di nota: forse una nota persa;
                '   - un arco fra due note uguali sullo stesso rigo: una legatura di valore persa, e le due note vanno suonate come una.
                Dim teste As List(Of XElement) = sistema.Descendants("head").ToList()
                For Each r As Rigo In righi
                    For Each segno As SegnoResiduo In SegniNonRiconosciuti(inchiostro, mascherato, larghezza, altezza, r, interlinea)
                        Dim rett As Rectangle = segno.Rettangolo
                        Dim k As Integer = battutaDelle(rett.X + rett.Width / 2.0)
                        If rett.Width >= 2.5 * interlinea AndAlso rett.Height <= 1.6 * interlinea Then
                            If TraDueTesteUguali(teste, rett, interlinea) Then
                                risultato.Segna(r.Parte, primaBattuta + battuteFoglio + k, 2.0, "nell'immagine c'è una legatura di valore che non è stata riconosciuta: due note uguali collegate vanno suonate come una sola")
                            End If
                        ElseIf SembraUnaNota(segno, interlinea) Then
                            risultato.Segna(r.Parte, primaBattuta + battuteFoglio + k, 3.0, "nell'immagine c'è un segno pieno, grande come una nota, che non è stato riconosciuto: forse una nota persa")
                        End If
                    Next
                Next
            End If
            battuteFoglio += pile.Count
        Next
        Return battuteFoglio
    End Function

    Private Shared Function SembraUnaNota(s As SegnoResiduo, interlinea As Integer) As Boolean
        Dim w As Integer = s.Rettangolo.Width
        Dim h As Integer = s.Rettangolo.Height
        Dim riempimento As Double = s.Area / CDbl(Math.Max(1, w * h))
        Return w >= 0.8 * interlinea AndAlso w <= 1.8 * interlinea AndAlso h >= 0.7 * interlinea AndAlso h <= 1.5 * interlinea AndAlso riempimento >= 0.6 AndAlso s.Area >= 0.6 * interlinea * interlinea
    End Function

    ''' <summary>Vero se subito prima e subito dopo l'arco ci sono due teste di nota riconosciute alla stessa altezza dello stesso rigo.</summary>
    Private Shared Function TraDueTesteUguali(teste As List(Of XElement), arco As Rectangle, interlinea As Integer) As Boolean
        Dim centro As Double = arco.Y + arco.Height / 2.0
        Dim prima As New List(Of XElement)()
        Dim dopo As New List(Of XElement)()
        For Each t As XElement In teste
            Dim b As XElement = t.Element("bounds")
            If b Is Nothing Then Continue For
            Dim x As Double = Num(b, "x", 0)
            Dim y As Double = Num(b, "y", 0)
            Dim w As Double = Num(b, "w", 0)
            Dim h As Double = Num(b, "h", 0)
            If Math.Abs((y + h / 2.0) - centro) > 2.2 * interlinea Then Continue For
            If Math.Abs((x + w) - arco.X) <= 1.6 * interlinea Then prima.Add(t)
            If Math.Abs(x - (arco.X + arco.Width)) <= 1.6 * interlinea Then dopo.Add(t)
        Next
        For Each a As XElement In prima
            For Each b As XElement In dopo
                If a.Attribute("pitch") Is Nothing OrElse b.Attribute("pitch") Is Nothing Then Continue For
                If a.Attribute("pitch").Value = b.Attribute("pitch").Value AndAlso If(a.Attribute("staff") IsNot Nothing, a.Attribute("staff").Value, "") = If(b.Attribute("staff") IsNot Nothing, b.Attribute("staff").Value, "") Then Return True
            Next
        Next
        Return False
    End Function

    Private Shared Sub DisegnaLinea(maschera As Boolean(), larghezza As Integer, altezza As Integer, x1 As Double, y1 As Double, x2 As Double, y2 As Double, spessore As Integer)
        Dim n As Integer = CInt(x2 - x1) + 1
        Dim met As Integer = spessore \ 2 + 1
        For i As Integer = 0 To n - 1
            Dim x As Integer = CInt(x1) + i
            If x < 0 OrElse x >= larghezza Then Continue For
            Dim y As Integer = CInt(Math.Round(y1 + (y2 - y1) * i / Math.Max(1, n - 1)))
            For yy As Integer = Math.Max(0, y - met) To Math.Min(altezza - 1, y + met)
                maschera(yy * larghezza + x) = True
            Next
        Next
    End Sub

    ''' <summary>La pagina in bianco e nero di Audiveris: vero dove c'è inchiostro. Nothing se manca.</summary>
    Private Shared Function LeggiInchiostro(zip As ZipArchive, numero As Integer, ByRef larghezza As Integer, ByRef altezza As Integer) As Boolean()
        Dim voce As ZipArchiveEntry = zip.GetEntry("sheet#" & numero.ToString() & "/BINARY.png")
        If voce Is Nothing Then Return Nothing
        Using flusso As Stream = voce.Open()
            Using memoria As New MemoryStream()
                flusso.CopyTo(memoria)
                memoria.Position = 0
                Using b As New Bitmap(memoria)
                    larghezza = b.Width
                    altezza = b.Height
                    Dim dati As BitmapData = b.LockBits(New Rectangle(0, 0, larghezza, altezza), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)
                    Dim byteTotali As Integer = Math.Abs(dati.Stride) * altezza
                    Dim buffer(byteTotali - 1) As Byte
                    System.Runtime.InteropServices.Marshal.Copy(dati.Scan0, buffer, 0, byteTotali)
                    Dim passo As Integer = dati.Stride
                    b.UnlockBits(dati)
                    Dim scuri(larghezza * altezza - 1) As Boolean
                    Dim quanti As Long = 0
                    For y As Integer = 0 To altezza - 1
                        For x As Integer = 0 To larghezza - 1
                            If buffer(y * passo + x * 4) < 128 Then
                                scuri(y * larghezza + x) = True
                                quanti += 1
                            End If
                        Next
                    Next
                    ' lo sfondo è la parte più grande: se i pixel scuri sono la maggioranza, i colori sono invertiti
                    If quanti * 2 > CLng(larghezza) * altezza Then
                        For i As Integer = 0 To scuri.Length - 1
                            scuri(i) = Not scuri(i)
                        Next
                    End If
                    Return scuri
                End Using
            End Using
        End Using
    End Function

    ''' <summary>I gruppi di inchiostro dentro la fascia di un rigo che nessun segno riconosciuto copre e che sono grandi come un segno vero.</summary>
    Private Shared Function SegniNonRiconosciuti(inchiostro As Boolean(), mascherato As Boolean(), larghezza As Integer, altezza As Integer, rigo As Rigo, interlinea As Integer) As List(Of SegnoResiduo)
        Dim trovati As New List(Of SegnoResiduo)()
        Dim y0 As Integer = Math.Max(0, rigo.Alto - 2 * interlinea)
        Dim y1 As Integer = Math.Min(altezza - 1, rigo.Basso + 2 * interlinea)
        Dim x0 As Integer = Math.Max(0, rigo.Sinistra)
        Dim x1 As Integer = Math.Min(larghezza - 1, rigo.Destra)
        Dim visto As New HashSet(Of Integer)()
        Dim areaMinima As Double = 0.35 * interlinea * interlinea
        Dim areaMassima As Double = 30.0 * interlinea * interlinea
        For y As Integer = y0 To y1
            For x As Integer = x0 To x1
                Dim i As Integer = y * larghezza + x
                If Not inchiostro(i) OrElse mascherato(i) OrElse visto.Contains(i) Then Continue For
                Dim pila As New Stack(Of Integer)()
                pila.Push(i)
                visto.Add(i)
                Dim minX As Integer = x
                Dim maxX As Integer = x
                Dim minY As Integer = y
                Dim maxY As Integer = y
                Dim area As Integer = 0
                While pila.Count > 0
                    Dim p As Integer = pila.Pop()
                    Dim py As Integer = p \ larghezza
                    Dim px As Integer = p - py * larghezza
                    area += 1
                    If px < minX Then minX = px
                    If px > maxX Then maxX = px
                    If py < minY Then minY = py
                    If py > maxY Then maxY = py
                    For dy As Integer = -1 To 1
                        For dx As Integer = -1 To 1
                            If dx = 0 AndAlso dy = 0 Then Continue For
                            Dim nx As Integer = px + dx
                            Dim ny As Integer = py + dy
                            If nx < x0 OrElse nx > x1 OrElse ny < y0 OrElse ny > y1 Then Continue For
                            Dim q As Integer = ny * larghezza + nx
                            If inchiostro(q) AndAlso Not mascherato(q) AndAlso visto.Add(q) Then pila.Push(q)
                        Next
                    Next
                End While
                Dim w As Integer = maxX - minX + 1
                Dim h As Integer = maxY - minY + 1
                If area >= areaMinima AndAlso area <= areaMassima AndAlso Math.Max(w, h) >= 0.6 * interlinea Then
                    Dim segnoTrovato As New SegnoResiduo()
                    segnoTrovato.Rettangolo = New Rectangle(minX, minY, w, h)
                    segnoTrovato.Area = area
                    trovati.Add(segnoTrovato)
                End If
            Next
        Next
        Return trovati
    End Function
End Class

' ---------------------------------------------------------------------------------------------
' 2. il confronto fra due letture dello stesso brano
' ---------------------------------------------------------------------------------------------
Public NotInheritable Class EsitoConfronto
    Public Confrontabile As Boolean
    Public Motivo As String = ""
    Public Misure As Integer
    Public Uguali As Integer
    Public Prese As Integer
    Public Tenute As Integer
    Public Dubbie As Integer
    Public ReadOnly Dubbi As New List(Of DubbioMisura)()
    Public ReadOnly Sostituite As New List(Of Long)()

    Public Function Riassunto() As String
        If Not Confrontabile Then Return "Non ho potuto confrontare le due letture: " & Motivo & "."
        Dim t As String = "Ho confrontato due letture dello spartito: " & Uguali.ToString() & " battute su " & Misure.ToString() & " coincidono"
        If Tenute > 0 Then t &= ", " & Tenute.ToString() & If(Tenute = 1, " battuta differiva ma resta com'era perché l'altra lettura non tornava", " battute differivano ma restano com'erano perché l'altra lettura non tornava")
        If Prese > 0 Then t &= ", " & Prese.ToString() & If(Prese = 1, " battuta è stata presa dall'altra lettura perché era l'unica che tornava", " battute sono state prese dall'altra lettura perché era l'unica che tornava")
        If Dubbie > 0 Then
            t &= ", " & Dubbie.ToString() & If(Dubbie = 1, " battuta resta dubbia", " battute restano dubbie") & " e " & If(Dubbie = 1, "è nell'elenco", "sono nell'elenco") & " del controllo"
        Else
            t &= ", nessun dubbio rimasto"
        End If
        Return t
    End Function
End Class

Public NotInheritable Class ConfrontoLetture

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>Le note che si sentono in una battuta, senza nome di voce né pause: due letture della stessa battuta devono dare la stessa firma.</summary>
    Private Shared Function Firma(mi As MisuraInfo) As String
        Dim d As Double = Math.Max(1, mi.Stato.Divisioni)
        Dim pezzi As New List(Of String)()
        For Each n As NotaInfo In mi.Note
            If n.Pausa OrElse n.SenzaAltezza OrElse n.Passo = "" Then Continue For
            pezzi.Add(CInt(Math.Round(n.Inizio * 960.0 / d)).ToString("00000") & ":" & CInt(Math.Round(n.Durata * 960.0 / d)).ToString("00000") & ":" & n.Passo & n.Alter.ToString() & n.Ottava.ToString())
        Next
        pezzi.Sort(StringComparer.Ordinal)
        Return String.Join("|", pezzi)
    End Function

    Private Shared Function ConteggioProblemi(r As RevisioneMusicXml) As Dictionary(Of Long, Integer)
        Dim c As New Dictionary(Of Long, Integer)()
        For Each pr As ProblemaSpartito In r.ProblemiInterni()
            Dim k As Long = RisultatoVerifica.Chiave(pr.Parte, pr.Misura)
            Dim v As Integer = 0
            c.TryGetValue(k, v)
            c(k) = v + 1
        Next
        Return c
    End Function

    ''' <summary>
    ''' Confronta due letture dello stesso brano battuta per battuta e, dove l'altra lettura è certamente migliore, la copia in principale.
    ''' penalitaPrincipale e penalitaAltra (dal confronto con l'immagine) servono solo a decidere quando i controlli sulle note pareggiano.
    ''' </summary>
    Public Shared Function Applica(principale As RevisioneMusicXml, altra As RevisioneMusicXml, penalitaPrincipale As Dictionary(Of Long, Double), penalitaAltra As Dictionary(Of Long, Double)) As EsitoConfronto
        Dim esito As New EsitoConfronto()
        If principale.Parti.Count <> altra.Parti.Count Then
            esito.Motivo = "le due letture hanno un numero diverso di strumenti (" & principale.Parti.Count.ToString() & " e " & altra.Parti.Count.ToString() & ")"
            Return esito
        End If
        esito.Confrontabile = True
        Dim problemiPrincipale As Dictionary(Of Long, Integer) = ConteggioProblemi(principale)
        Dim problemiAltra As Dictionary(Of Long, Integer) = ConteggioProblemi(altra)
        Dim partiSaltate As Integer = 0
        For p As Integer = 0 To principale.Parti.Count - 1
            Dim a As List(Of MisuraInfo) = principale.Analizza(p)
            Dim b As List(Of MisuraInfo) = altra.Analizza(p)
            If a.Count <> b.Count Then
                partiSaltate += 1
                Continue For
            End If
            For k As Integer = 0 To a.Count - 1
                esito.Misure += 1
                If Firma(a(k)) = Firma(b(k)) Then
                    esito.Uguali += 1
                    Continue For
                End If
                Dim chiave As Long = RisultatoVerifica.Chiave(p, k)
                Dim cA As Integer = 0
                Dim cB As Integer = 0
                problemiPrincipale.TryGetValue(chiave, cA)
                problemiAltra.TryGetValue(chiave, cB)
                Dim prendiAltra As Boolean = False
                Dim deciso As Boolean = True
                If cB < cA Then
                    prendiAltra = True
                ElseIf cA < cB Then
                    prendiAltra = False
                Else
                    Dim pA As Double = 0
                    Dim pB As Double = 0
                    Dim haA As Boolean = penalitaPrincipale IsNot Nothing AndAlso penalitaPrincipale.Count > 0
                    Dim haB As Boolean = penalitaAltra IsNot Nothing AndAlso penalitaAltra.Count > 0
                    If haA Then penalitaPrincipale.TryGetValue(chiave, pA)
                    If haB Then penalitaAltra.TryGetValue(chiave, pB)
                    If haA AndAlso haB AndAlso Math.Abs(pA - pB) >= 1.0 Then
                        prendiAltra = pB < pA
                    Else
                        deciso = False
                    End If
                End If
                If deciso AndAlso Not prendiAltra Then esito.Tenute += 1
                If deciso AndAlso prendiAltra Then
                    If principale.SostituisciMisura(p, a(k), b(k)) Then
                        esito.Prese += 1
                        esito.Sostituite.Add(chiave)
                    Else
                        deciso = False
                    End If
                End If
                If Not deciso Then
                    esito.Dubbie += 1
                    Dim d As New DubbioMisura()
                    d.Parte = p
                    d.Misura = k
                    d.Motivo = "le due letture dello spartito non coincidono e non c'è un modo certo di scegliere quale sia giusta"
                    esito.Dubbi.Add(d)
                End If
            Next
        Next
        If partiSaltate > 0 Then esito.Motivo = partiSaltate.ToString() & If(partiSaltate = 1, " strumento ha", " strumenti hanno") & " un numero diverso di battute nelle due letture e non " & If(partiSaltate = 1, "è stata confrontata", "sono state confrontate")
        Return esito
    End Function
End Class
