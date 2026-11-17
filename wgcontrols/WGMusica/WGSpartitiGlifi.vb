' ============================================================================================
' WGSpartitiGlifi - leggere i caratteri musicali di un PDF vero per controllare e correggere il riconoscimento
' ============================================================================================
'
' Un PDF esportato da un programma di notazione (Sibelius, Dorico, MuseScore, Finale) non è un'immagine: ogni testa di nota, pausa,
' chiave, alterazione e codina è un carattere di un font musicale, con la sua posizione esatta, e i righi e i gambi sono linee.
' Audiveris trasforma comunque tutto in un'immagine e la rilegge, e a volte sbaglia: perde una battuta intera, legge una croma come
' semiminima perché la codina è nascosta da un diesis, sbaglia una nota di un grado. Qui la stessa pagina si legge per quello che è.
'
' Il carattere non ha nome: si impara. Dove Audiveris ha riconosciuto un simbolo, in quel punto del PDF c'è un carattere; se lo stesso
' carattere cade sullo stesso simbolo quasi sempre (almeno nove volte su dieci), quel carattere vale quel simbolo. L'alfabeto del font
' si ricava così dal PDF stesso, senza tabelle scritte a mano, e vale per qualunque programma di notazione.
'
' Poi, per ogni battuta di ogni parte:
'   - l'altezza di ogni testa di nota si ricava esattamente dalla sua posizione rispetto alle cinque linee del rigo e dalla chiave;
'   - se la battuta nello spartito letto è vuota e nel PDF c'è un accordo di minime, l'accordo si ricostruisce: dura tutta la battuta;
'   - se il PDF ha una codina che nessuna nota letta ha, la nota vicina è una croma e non una semiminima;
'   - se una sola nota ha un'altezza diversa dal PDF, si corregge;
'   - tutto il resto che non coincide (note in più, note in meno) diventa un dubbio nell'elenco del controllo.
' Si corregge solo quando la soluzione è unica. Prima di fidarsi, si misura quanto le teste del PDF coincidono con lo spartito letto: se
' coincidono meno di 85 battute su 100, i due non sono allineati e non si fa niente.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml.Linq

Friend NotInheritable Class RisultatoGlifi
    Public ReadOnly Correzioni As New List(Of String)()
    Public ReadOnly Dubbi As New List(Of DubbioMisura)()
    ''' <summary>Le battute (chiave parte e battuta) in cui le altezze di tutte le note coincidono con le teste del PDF: lì un salto grande o una nota fuori estensione sono veri.</summary>
    Public ReadOnly Verificate As New HashSet(Of Long)()
    Public Confrontate As Integer
    Public Uguali As Integer
    Public Riassunto As String = ""

    Public Sub Scrivi(percorso As String)
        Dim righe As New List(Of String)()
        righe.AddRange(Correzioni)
        For Each d As DubbioMisura In Dubbi
            righe.Add("D|" & d.Parte.ToString() & "|" & d.Misura.ToString() & "|" & d.Motivo.Replace("|", " ").Replace(vbCr, " ").Replace(vbLf, " "))
        Next
        For Each k As Long In Verificate
            righe.Add("V|" & (k \ 1000000L).ToString() & "|" & (k Mod 1000000L).ToString())
        Next
        File.WriteAllLines(percorso, righe, New UTF8Encoding(True))
    End Sub

    Public Shared Function Leggi(percorso As String) As RisultatoGlifi
        Dim r As New RisultatoGlifi()
        If Not File.Exists(percorso) Then Return r
        For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
            If riga.StartsWith("C|") Then
                r.Correzioni.Add(riga)
            ElseIf riga.StartsWith("V|") Then
                Dim v As String() = riga.Split("|"c)
                Dim vp As Integer
                Dim vk As Integer
                If v.Length >= 3 AndAlso Integer.TryParse(v(1), vp) AndAlso Integer.TryParse(v(2), vk) Then r.Verificate.Add(RisultatoVerifica.Chiave(vp, vk))
            ElseIf riga.StartsWith("D|") Then
                Dim p As String() = riga.Split("|"c)
                Dim parte As Integer
                Dim misura As Integer
                If p.Length >= 4 AndAlso Integer.TryParse(p(1), parte) AndAlso Integer.TryParse(p(2), misura) Then
                    Dim d As New DubbioMisura()
                    d.Parte = parte
                    d.Misura = misura
                    d.Motivo = p(3)
                    r.Dubbi.Add(d)
                End If
            End If
        Next
        Return r
    End Function
End Class

Friend NotInheritable Class LetturaGlifi

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture
    Private Const NomiPassi As String = "CDEFGAB"

    Private Sub New()
    End Sub

    ' ---- le strutture ------------------------------------------------------------------------------------------

    Private Class InterInfo
        Public Forma As String = ""
        Public X As Double
        Public Y As Double
        Public L As Double
        Public A As Double
    End Class

    Private Class RigoInfo
        Public Parte As Integer
        Public Alto As Double
        Public Basso As Double
        Public Sinistra As Double
        Public Destra As Double
    End Class

    Private Class SistemaInfo
        Public ReadOnly Pile As New List(Of Double())()
        Public ReadOnly Righi As New List(Of RigoInfo)()
        Public ReadOnly Inters As New List(Of InterInfo)()
        Public PrimaBattuta As Integer
    End Class

    Private Class FoglioInfo
        Public Larghezza As Double
        Public Altezza As Double
        Public Interlinea As Double
        Public ReadOnly Sistemi As New List(Of SistemaInfo)()
    End Class

    Private Class CarattereInfo
        Public Font As String = ""
        Public Codice As String = ""
        Public Cx As Double
        Public Cy As Double
    End Class

    Private Class TestaInfo
        Public X As Double
        Public Y As Double
        Public Interlinea As Double
        Public D As Integer
        Public Vuota As Boolean
    End Class

    Private Class BandieraInfo
        Public X As Double
        Public Giu As Boolean
        Public Livello As Integer
    End Class

    Private Class AlterazioneInfo
        Public X As Double
        Public Y As Double
        Public Valore As Integer
    End Class

    Private Class CellaInfo
        Public ReadOnly Teste As New List(Of TestaInfo)()
        Public ReadOnly Bandiere As New List(Of BandieraInfo)()
        Public ReadOnly Alterazioni As New List(Of AlterazioneInfo)()
        Public Pause As Integer
        Public Interlinea As Double
    End Class

    Private Class NotaXml
        Public El As XElement
        Public Ordinale As Integer
        Public D As Integer
        Public X As Double
        Public Tipo As String = ""
        Public Travata As Boolean
        Public Accordo As Boolean
        Public Grazia As Boolean
        Public Passo As String = ""
        Public Accidentale As Boolean
    End Class

    Private Shared Function Num(e As XElement, nome As String, predefinito As Double) As Double
        If e Is Nothing OrElse e.Attribute(nome) Is Nothing Then Return predefinito
        Dim v As Double
        If Double.TryParse(e.Attribute(nome).Value, NumberStyles.Float, Inv, v) Then Return v
        Return predefinito
    End Function

    ' ---- lettura del lavoro di Audiveris -----------------------------------------------------------------------

    Private Shared Function LeggiFoglio(zip As ZipArchive, numero As Integer, ByRef ok As Boolean) As FoglioInfo
        Dim f As New FoglioInfo()
        ok = False
        Dim voce As ZipArchiveEntry = zip.GetEntry("sheet#" & numero.ToString() & "/sheet#" & numero.ToString() & ".xml")
        If voce Is Nothing Then Return f
        Dim doc As XDocument
        Using s As Stream = voce.Open()
            doc = XDocument.Load(s)
        End Using
        Dim quadro As XElement = doc.Root.Element("picture")
        Dim scala As XElement = doc.Root.Element("scale")
        If quadro Is Nothing OrElse scala Is Nothing OrElse scala.Element("interline") Is Nothing Then Return f
        f.Larghezza = Num(quadro, "width", 0)
        f.Altezza = Num(quadro, "height", 0)
        f.Interlinea = Num(scala.Element("interline"), "main", 0)
        If f.Larghezza <= 0 OrElse f.Altezza <= 0 OrElse f.Interlinea <= 0 Then Return f
        For Each sis As XElement In doc.Root.Descendants("system")
            Dim si As New SistemaInfo()
            For Each st As XElement In sis.Elements("stack")
                si.Pile.Add(New Double() {Num(st, "left", 0), Num(st, "right", 0)})
            Next
            If si.Pile.Count = 0 Then Continue For
            Dim indiceParte As Integer = 0
            For Each parte As XElement In sis.Elements("part")
                For Each staff As XElement In parte.Elements("staff")
                    Dim ys As New List(Of Double)()
                    For Each linea As XElement In staff.Descendants("line")
                        Dim punti As List(Of XElement) = linea.Elements("point").ToList()
                        If punti.Count >= 2 Then ys.Add((Num(punti(0), "y", 0) + Num(punti(punti.Count - 1), "y", 0)) / 2.0)
                    Next
                    If ys.Count >= 5 Then
                        Dim r As New RigoInfo()
                        r.Parte = indiceParte
                        r.Alto = ys.Min()
                        r.Basso = ys.Max()
                        r.Sinistra = Num(staff, "left", 0)
                        r.Destra = Num(staff, "right", 0)
                        si.Righi.Add(r)
                    End If
                Next
                indiceParte += 1
            Next
            For Each el As XElement In sis.Descendants()
                If el.Attribute("shape") Is Nothing Then Continue For
                Dim b As XElement = el.Element("bounds")
                If b Is Nothing Then Continue For
                Dim i As New InterInfo()
                i.Forma = el.Attribute("shape").Value
                i.X = Num(b, "x", 0)
                i.Y = Num(b, "y", 0)
                i.L = Num(b, "w", 0)
                i.A = Num(b, "h", 0)
                si.Inters.Add(i)
            Next
            f.Sistemi.Add(si)
        Next
        ok = True
        Return f
    End Function

    ' ---- l'alfabeto del carattere ------------------------------------------------------------------------------

    Private Shared Function VotoValido(forma As String) As Boolean
        For Each k As String In New String() {"LEDGER", "BARLINE", "CONNECTOR", "ENDING", "REPEAT_DOT", "STEM", "BEAM", "SLUR", "BRACKET", "BRACE"}
            If forma.Contains(k) Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' Analizza un brano: il PDF, il lavoro di Audiveris (.omr) e il MusicXML che ne è uscito. Restituisce le correzioni certe e i dubbi.
    ''' Se i tre non sono allineati (meno di 85 battute su 100 coincidono) non restituisce niente.
    ''' </summary>
    Public Shared Function Analizza(percorsoPdf As String, percorsoOmr As String, percorsoMxl As String, progresso As Action(Of Integer), annulla As Func(Of Boolean)) As RisultatoGlifi
        Dim ris As New RisultatoGlifi()
        Dim rev As RevisioneMusicXml = RevisioneMusicXml.Apri(percorsoMxl)
        Dim nParti As Integer = rev.Parti.Count
        If nParti = 0 Then Return ris

        ' 1. i fogli di Audiveris
        Dim fogli As New List(Of FoglioInfo)()
        Dim numeriFogli As New List(Of Integer)()
        Using zip As ZipArchive = ZipFile.OpenRead(percorsoOmr)
            Dim numeri As New List(Of Integer)()
            For Each e As ZipArchiveEntry In zip.Entries
                Dim m As Match = Regex.Match(e.FullName, "^sheet#(\d+)/sheet#\1\.xml$")
                If m.Success Then numeri.Add(Integer.Parse(m.Groups(1).Value))
            Next
            numeri.Sort()
            For Each n As Integer In numeri
                Dim ok As Boolean
                Dim f As FoglioInfo = LeggiFoglio(zip, n, ok)
                If Not ok Then Return ris
                fogli.Add(f)
                numeriFogli.Add(n)
            Next
        End Using
        If fogli.Count = 0 Then Return ris

        ' le parti con un solo rigo (con due righi, come il pianoforte, non si confrontano)
        Dim righiPerParte(nParti - 1) As Integer
        Dim conteggioOk As Boolean = True
        For Each f As FoglioInfo In fogli
            For Each si As SistemaInfo In f.Sistemi
                Dim perParte As New Dictionary(Of Integer, Integer)()
                For Each r As RigoInfo In si.Righi
                    If r.Parte >= nParti Then
                        conteggioOk = False
                    Else
                        Dim c As Integer = 0
                        perParte.TryGetValue(r.Parte, c)
                        perParte(r.Parte) = c + 1
                    End If
                Next
                For Each kv As KeyValuePair(Of Integer, Integer) In perParte
                    righiPerParte(kv.Key) = Math.Max(righiPerParte(kv.Key), kv.Value)
                Next
            Next
        Next
        If Not conteggioOk Then Return ris
        Dim eleggibile(nParti - 1) As Boolean
        For p As Integer = 0 To nParti - 1
            eleggibile(p) = (righiPerParte(p) = 1)
        Next

        ' 2. i caratteri del PDF, pagina per pagina
        Dim caratteri As New List(Of List(Of CarattereInfo))()
        Using pdf As UglyToad.PdfPig.PdfDocument = UglyToad.PdfPig.PdfDocument.Open(percorsoPdf)
            If numeriFogli.Max() > pdf.NumberOfPages Then Return ris
            For indiceFoglio As Integer = 0 To fogli.Count - 1
                If annulla() Then Return ris
                progresso(CInt(indiceFoglio * 40L \ fogli.Count))
                Dim pg = pdf.GetPage(numeriFogli(indiceFoglio))
                Dim f As FoglioInfo = fogli(indiceFoglio)
                Dim sx As Double = f.Larghezza / pg.Width
                Dim sy As Double = f.Altezza / pg.Height
                Dim elenco As New List(Of CarattereInfo)()
                For Each l In pg.Letters
                    If String.IsNullOrEmpty(l.Value) Then Continue For
                    Dim nome As String = If(l.FontName, "")
                    If nome.Contains("Times") OrElse nome.Contains("Arial") OrElse nome.Contains("Helvetica") OrElse nome.Contains("Calibri") Then Continue For
                    Dim b = l.BoundingBox
                    Dim c As New CarattereInfo()
                    c.Font = If(nome.Contains("+"), nome.Substring(nome.IndexOf("+"c) + 1), nome)
                    c.Codice = AscW(l.Value(0)).ToString("X")
                    c.Cx = (b.Left + b.Width / 2.0) * sx
                    c.Cy = (pg.Height - (b.Bottom + b.Height / 2.0)) * sy
                    elenco.Add(c)
                Next
                caratteri.Add(elenco)
            Next
        End Using

        ' 3. l'alfabeto: quale simbolo di Audiveris cade sotto ogni carattere
        Dim voti As New Dictionary(Of String, Dictionary(Of String, Integer))()
        Dim scarti As New Dictionary(Of String, List(Of Double()))()
        For n As Integer = 0 To fogli.Count - 1
            If annulla() Then Return ris
            Dim f As FoglioInfo = fogli(n)
            Dim tutti As New List(Of InterInfo)()
            For Each si As SistemaInfo In f.Sistemi
                tutti.AddRange(si.Inters)
            Next
            For Each c As CarattereInfo In caratteri(n)
                Dim migliore As InterInfo = Nothing
                Dim distMigliore As Double = Double.MaxValue
                For Each i As InterInfo In tutti
                    If c.Cx >= i.X - 4 AndAlso c.Cx <= i.X + i.L + 4 AndAlso c.Cy >= i.Y - 4 AndAlso c.Cy <= i.Y + i.A + 4 Then
                        Dim d As Double = Math.Abs(c.Cx - (i.X + i.L / 2)) + Math.Abs(c.Cy - (i.Y + i.A / 2))
                        If d < distMigliore AndAlso VotoValido(i.Forma) Then
                            distMigliore = d
                            migliore = i
                        End If
                    End If
                Next
                If migliore Is Nothing Then Continue For
                Dim chiave As String = c.Font & "|" & c.Codice
                If Not voti.ContainsKey(chiave) Then voti(chiave) = New Dictionary(Of String, Integer)()
                Dim v As Integer = 0
                voti(chiave).TryGetValue(migliore.Forma, v)
                voti(chiave)(migliore.Forma) = v + 1
                If migliore.Forma.StartsWith("NOTEHEAD") OrElse migliore.Forma.Contains("REST") Then
                    If Not scarti.ContainsKey(chiave) Then scarti(chiave) = New List(Of Double())()
                    scarti(chiave).Add(New Double() {c.Cx - (migliore.X + migliore.L / 2), c.Cy - (migliore.Y + migliore.A / 2)})
                End If
            Next
        Next
        Dim alfabeto As New Dictionary(Of String, String)()
        For Each kv As KeyValuePair(Of String, Dictionary(Of String, Integer)) In voti
            Dim totale As Integer = kv.Value.Values.Sum()
            Dim primo As KeyValuePair(Of String, Integer) = kv.Value.OrderByDescending(Function(x) x.Value).First()
            If totale >= 3 AndAlso primo.Value >= 0.9 * totale Then alfabeto(kv.Key) = primo.Key
        Next
        If Not alfabeto.Values.Any(Function(x) x = "NOTEHEAD_BLACK") Then Return ris
        Dim spostamento As New Dictionary(Of String, Double())()
        For Each kv As KeyValuePair(Of String, List(Of Double())) In scarti
            spostamento(kv.Key) = New Double() {kv.Value.Average(Function(x) x(0)), kv.Value.Average(Function(x) x(1))}
        Next

        ' 4. le chiavi e le note del MusicXML
        Dim misure(nParti - 1) As List(Of MisuraInfo)
        Dim chiavi(nParti - 1) As List(Of Integer)
        Dim noteXml As New Dictionary(Of Long, List(Of NotaXml))()
        For p As Integer = 0 To nParti - 1
            If Not eleggibile(p) Then Continue For
            misure(p) = rev.Analizza(p)
            chiavi(p) = New List(Of Integer)()
            Dim corrente As Integer = Integer.MinValue
            Dim indiceMisura As Integer = 0
            For Each m As XElement In rev.Parti(p).Elemento.Elements("measure")
                For Each a As XElement In m.Elements("attributes")
                    Dim c As XElement = a.Element("clef")
                    If c IsNot Nothing Then
                        Dim sign As String = If(c.Element("sign") IsNot Nothing, c.Element("sign").Value.Trim(), "")
                        Dim linea As Integer = 2
                        If c.Element("line") IsNot Nothing Then Integer.TryParse(c.Element("line").Value.Trim(), linea)
                        Dim ott As Integer = 0
                        If c.Element("clef-octave-change") IsNot Nothing Then Integer.TryParse(c.Element("clef-octave-change").Value.Trim(), ott)
                        corrente = BasePentagramma(sign, linea, ott)
                    End If
                Next
                chiavi(p).Add(corrente)
                Dim lista As New List(Of NotaXml)()
                Dim ordinale As Integer = 0
                For Each n As XElement In m.Elements("note")
                    Dim altezza As XElement = n.Element("pitch")
                    If altezza IsNot Nothing AndAlso altezza.Element("step") IsNot Nothing AndAlso altezza.Element("octave") IsNot Nothing Then
                        Dim nx As New NotaXml()
                        nx.El = n
                        nx.Ordinale = ordinale
                        nx.Passo = altezza.Element("step").Value.Trim()
                        Dim ottava As Integer
                        Integer.TryParse(altezza.Element("octave").Value.Trim(), ottava)
                        nx.D = ottava * 7 + Math.Max(0, NomiPassi.IndexOf(nx.Passo))
                        nx.X = Num(n, "default-x", Double.NaN)
                        nx.Tipo = If(n.Element("type") IsNot Nothing, n.Element("type").Value.Trim(), "")
                        nx.Travata = n.Element("beam") IsNot Nothing
                        nx.Accordo = n.Element("chord") IsNot Nothing
                        nx.Grazia = n.Element("grace") IsNot Nothing
                        nx.Accidentale = n.Element("accidental") IsNot Nothing
                        lista.Add(nx)
                    End If
                    ordinale += 1
                Next
                noteXml(RisultatoVerifica.Chiave(p, indiceMisura)) = lista
                indiceMisura += 1
            Next
        Next

        ' 5. le teste, le codine, le alterazioni e le pause del PDF, per parte e battuta
        Dim celle As New Dictionary(Of Long, CellaInfo)()
        Dim primaBattutaFoglio As Integer = 0
        For n As Integer = 0 To fogli.Count - 1
            If annulla() Then Return ris
            progresso(40 + CInt(n * 60L \ fogli.Count))
            Dim f As FoglioInfo = fogli(n)
            Dim totaleFoglio As Integer = 0
            For Each si As SistemaInfo In f.Sistemi
                si.PrimaBattuta = primaBattutaFoglio + totaleFoglio
                totaleFoglio += si.Pile.Count
            Next
            For Each c As CarattereInfo In caratteri(n)
                Dim chiaveAlf As String = c.Font & "|" & c.Codice
                Dim forma As String = Nothing
                If Not alfabeto.TryGetValue(chiaveAlf, forma) Then Continue For
                Dim eTesta As Boolean = (forma = "NOTEHEAD_BLACK" OrElse forma = "NOTEHEAD_VOID")
                Dim eBandiera As Boolean = forma.StartsWith("FLAG_")
                Dim eAlter As Boolean = (forma = "FLAT" OrElse forma = "SHARP" OrElse forma = "NATURAL")
                Dim ePausa As Boolean = forma.Contains("REST")
                If Not (eTesta OrElse eBandiera OrElse eAlter OrElse ePausa) Then Continue For
                Dim cx As Double = c.Cx
                Dim cy As Double = c.Cy
                Dim sp As Double() = Nothing
                If eTesta AndAlso spostamento.TryGetValue(chiaveAlf, sp) Then
                    cx -= sp(0)
                    cy -= sp(1)
                End If
                ' a quale rigo e a quale battuta appartiene
                Dim sisTrovato As SistemaInfo = Nothing
                Dim rigoTrovato As RigoInfo = Nothing
                Dim distMin As Double = Double.MaxValue
                For Each si As SistemaInfo In f.Sistemi
                    For Each r As RigoInfo In si.Righi
                        Dim ils As Double = (r.Basso - r.Alto) / 4.0
                        If cx >= r.Sinistra - 5 AndAlso cx <= r.Destra + 5 AndAlso cy >= r.Alto - 8 * ils AndAlso cy <= r.Basso + 8 * ils Then
                            Dim d As Double = Math.Abs(cy - (r.Alto + r.Basso) / 2.0)
                            If d < distMin Then
                                distMin = d
                                sisTrovato = si
                                rigoTrovato = r
                            End If
                        End If
                    Next
                Next
                If sisTrovato Is Nothing OrElse Not eleggibile(rigoTrovato.Parte) Then Continue For
                Dim k As Integer = sisTrovato.Pile.Count - 1
                For j As Integer = 0 To sisTrovato.Pile.Count - 1
                    If cx < sisTrovato.Pile(j)(1) Then
                        k = j
                        Exit For
                    End If
                Next
                Dim battuta As Integer = sisTrovato.PrimaBattuta + k
                Dim chiaveCella As Long = RisultatoVerifica.Chiave(rigoTrovato.Parte, battuta)
                Dim cella As CellaInfo = Nothing
                If Not celle.TryGetValue(chiaveCella, cella) Then
                    cella = New CellaInfo()
                    cella.Interlinea = (rigoTrovato.Basso - rigoTrovato.Alto) / 4.0
                    celle(chiaveCella) = cella
                End If
                Dim xRel As Double = cx - sisTrovato.Pile(k)(0)
                Dim ilRigo As Double = (rigoTrovato.Basso - rigoTrovato.Alto) / 4.0
                If eTesta Then
                    Dim chiaviParte As List(Of Integer) = chiavi(rigoTrovato.Parte)
                    If battuta >= chiaviParte.Count Then Continue For
                    Dim baseP As Integer = chiaviParte(battuta)
                    If baseP = Integer.MinValue Then Continue For
                    Dim t As New TestaInfo()
                    t.X = xRel
                    t.Y = cy
                    t.Interlinea = ilRigo
                    t.D = baseP + CInt(Math.Round((rigoTrovato.Basso - cy) / (ilRigo / 2.0)))
                    t.Vuota = (forma = "NOTEHEAD_VOID")
                    cella.Teste.Add(t)
                ElseIf eBandiera Then
                    Dim b As New BandieraInfo()
                    b.X = xRel
                    b.Giu = forma.EndsWith("_DOWN")
                    Dim mm As Match = Regex.Match(forma, "FLAG_(\d+)")
                    b.Livello = If(mm.Success, Integer.Parse(mm.Groups(1).Value), 1)
                    cella.Bandiere.Add(b)
                ElseIf eAlter Then
                    Dim a As New AlterazioneInfo()
                    a.X = xRel
                    a.Y = cy
                    a.Valore = If(forma = "FLAT", -1, If(forma = "SHARP", 1, 0))
                    cella.Alterazioni.Add(a)
                Else
                    cella.Pause += 1
                End If
            Next
            primaBattutaFoglio += totaleFoglio
        Next

        ' 6. la taratura: dove cade nel PDF la testa che l'XML dice a un certo "default-x"; e dove stanno le codine rispetto alla testa
        Dim diffX As New List(Of Double)()
        Dim diffSu As New List(Of Double)()
        Dim diffGiu As New List(Of Double)()
        Dim confrontate As Integer = 0
        Dim uguali As Integer = 0
        For Each kv As KeyValuePair(Of Long, CellaInfo) In celle
            Dim p As Integer = CInt(kv.Key \ 1000000L)
            Dim k As Integer = CInt(kv.Key Mod 1000000L)
            Dim xs As List(Of NotaXml) = Nothing
            If Not noteXml.TryGetValue(kv.Key, xs) Then Continue For
            Dim teste As List(Of TestaInfo) = kv.Value.Teste.OrderBy(Function(t) t.X).ThenBy(Function(t) t.D).ToList()
            Dim ordX As List(Of NotaXml) = xs.OrderBy(Function(x) x.X).ThenBy(Function(x) x.D).ToList()
            If teste.Count = 0 AndAlso ordX.Count = 0 Then Continue For
            confrontate += 1
            If teste.Select(Function(t) t.D).OrderBy(Function(d) d).SequenceEqual(ordX.Select(Function(x) x.D).OrderBy(Function(d) d)) Then
                uguali += 1
                If teste.Count > 0 Then ris.Verificate.Add(kv.Key)
                If teste.Count = ordX.Count Then
                    For i As Integer = 0 To teste.Count - 1
                        If Not Double.IsNaN(ordX(i).X) AndAlso teste(i).D = ordX(i).D Then diffX.Add(teste(i).X - ordX(i).X * teste(i).Interlinea / 10.0)
                    Next
                End If
            End If
        Next
        ris.Confrontate = confrontate
        ris.Uguali = uguali
        If confrontate < 20 OrElse uguali < 0.85 * confrontate OrElse diffX.Count < 20 Then
            ris.Riassunto = "le teste di nota del PDF coincidono con lo spartito letto solo in " & uguali.ToString() & " battute su " & confrontate.ToString() & ": non le uso"
            Return ris
        End If
        diffX.Sort()
        Dim mu As Double = diffX(diffX.Count \ 2)
        For Each kv As KeyValuePair(Of Long, CellaInfo) In celle
            Dim xs As List(Of NotaXml) = Nothing
            If Not noteXml.TryGetValue(kv.Key, xs) Then Continue For
            Dim conCodina As List(Of NotaXml) = xs.Where(Function(x) Not x.Accordo AndAlso Not x.Grazia AndAlso Not x.Travata AndAlso (x.Tipo = "eighth" OrElse x.Tipo = "16th" OrElse x.Tipo = "32nd")).OrderBy(Function(x) x.X).ToList()
            Dim bandiere As List(Of BandieraInfo) = kv.Value.Bandiere.OrderBy(Function(b) b.X).ToList()
            If bandiere.Count <> conCodina.Count OrElse bandiere.Count = 0 Then Continue For
            For i As Integer = 0 To bandiere.Count - 1
                If Double.IsNaN(conCodina(i).X) Then Continue For
                Dim d As Double = (bandiere(i).X - (conCodina(i).X * kv.Value.Interlinea / 10.0 + mu)) / kv.Value.Interlinea
                If bandiere(i).Giu Then diffGiu.Add(d) Else diffSu.Add(d)
            Next
        Next
        diffSu.Sort()
        diffGiu.Sort()
        Dim offsetSu As Double = If(diffSu.Count >= 5, diffSu(diffSu.Count \ 2), Double.NaN)
        Dim offsetGiu As Double = If(diffGiu.Count >= 5, diffGiu(diffGiu.Count \ 2), Double.NaN)

        ' 7. le decisioni, battuta per battuta
        Dim corretteAltezze As Integer = 0
        Dim ricostruite As Integer = 0
        Dim codine As Integer = 0
        For Each kv As KeyValuePair(Of Long, CellaInfo) In celle.OrderBy(Function(c) c.Key)
            If annulla() Then Exit For
            Dim p As Integer = CInt(kv.Key \ 1000000L)
            Dim k As Integer = CInt(kv.Key Mod 1000000L)
            If misure(p) Is Nothing OrElse k >= misure(p).Count Then Continue For
            Dim cella As CellaInfo = kv.Value
            Dim il As Double = cella.Interlinea
            Dim mi As MisuraInfo = misure(p)(k)
            Dim xs As List(Of NotaXml) = Nothing
            If Not noteXml.TryGetValue(kv.Key, xs) Then xs = New List(Of NotaXml)()
            Dim teste As List(Of TestaInfo) = cella.Teste.OrderBy(Function(t) t.X).ThenBy(Function(t) t.D).ToList()

            ' (a) la battuta è vuota nello spartito letto e nel PDF c'è un accordo di minime
            If mi.Elemento.Elements("note").Count() = 0 AndAlso teste.Count > 0 Then
                Dim vuote As Boolean = teste.All(Function(t) t.Vuota)
                Dim larghezza As Double = teste.Max(Function(t) t.X) - teste.Min(Function(t) t.X)
                Dim quarti As Double = 4.0 * mi.Stato.Battiti / Math.Max(1, mi.Stato.Valore)
                Dim valido As Boolean = (Math.Abs(quarti - 2.0) < 0.001 OrElse Math.Abs(quarti - 3.0) < 0.001 OrElse Math.Abs(quarti - 4.0) < 0.001 OrElse Math.Abs(quarti - 6.0) < 0.001)
                If vuote AndAlso larghezza <= 1.6 * il AndAlso cella.Pause = 0 AndAlso valido AndAlso mi.Attesa > 0 Then
                    Dim elenco As New List(Of String)()
                    For Each t As TestaInfo In teste.OrderBy(Function(x) x.D)
                        Dim alter As Integer = AlterazioneDellaTesta(t, cella, mi.Stato.Quinte)
                        elenco.Add(NomiPassi(((t.D Mod 7) + 7) Mod 7) & "," & (t.D \ 7).ToString() & "," & alter.ToString())
                    Next
                    ris.Correzioni.Add("C|N|" & p.ToString() & "|" & k.ToString() & "|" & quarti.ToString("0.###", Inv) & "|" & String.Join(";", elenco))
                    ricostruite += 1
                Else
                    ris.Dubbi.Add(NuovoDubbio(p, k, "la battuta è vuota nello spartito letto, ma nel PDF ci sono " & teste.Count.ToString() & If(teste.Count = 1, " nota", " note") & ": manca qualcosa"))
                End If
                Continue For
            End If

            ' (b) le codine
            Dim conCodinaXml As List(Of NotaXml) = xs.Where(Function(x) Not x.Accordo AndAlso Not x.Grazia AndAlso Not x.Travata AndAlso (x.Tipo = "eighth" OrElse x.Tipo = "16th" OrElse x.Tipo = "32nd")).ToList()
            If cella.Bandiere.Count > conCodinaXml.Count AndAlso Not Double.IsNaN(offsetSu) Then
                Dim candidate As List(Of NotaXml) = xs.Where(Function(x) Not x.Accordo AndAlso Not x.Grazia AndAlso Not x.Travata AndAlso x.Tipo = "quarter" AndAlso Not Double.IsNaN(x.X)).ToList()
                Dim usate As New HashSet(Of Integer)()
                For Each b As BandieraInfo In cella.Bandiere.OrderBy(Function(x) x.X)
                    Dim spost As Double = If(b.Giu, offsetGiu, offsetSu)
                    If Double.IsNaN(spost) Then Continue For
                    Dim attesaX As Double = b.X - spost * il
                    ' una codina già spiegata da una nota con la codina, non serve
                    If conCodinaXml.Any(Function(x) Not Double.IsNaN(x.X) AndAlso Math.Abs(x.X * il / 10.0 + mu - attesaX) <= 0.4 * il) Then Continue For
                    Dim migliore As NotaXml = Nothing
                    Dim distMigliore As Double = Double.MaxValue
                    For Each x As NotaXml In candidate
                        Dim d As Double = Math.Abs(x.X * il / 10.0 + mu - attesaX)
                        If d < distMigliore AndAlso Not usate.Contains(x.Ordinale) Then
                            distMigliore = d
                            migliore = x
                        End If
                    Next
                    If migliore IsNot Nothing AndAlso distMigliore <= 0.4 * il Then
                        usate.Add(migliore.Ordinale)
                        Dim tipo As String = If(b.Livello = 2, "16th", If(b.Livello >= 3, "32nd", "eighth"))
                        ris.Correzioni.Add("C|F|" & p.ToString() & "|" & k.ToString() & "|" & migliore.Ordinale.ToString() & "|" & tipo)
                        codine += 1
                    Else
                        ris.Dubbi.Add(NuovoDubbio(p, k, "nel PDF c'è una codina che nessuna nota letta ha: una durata potrebbe essere sbagliata"))
                    End If
                Next
            End If

            ' (c) le altezze
            Dim dPdf As List(Of Integer) = teste.Select(Function(t) t.D).OrderBy(Function(d) d).ToList()
            Dim dXml As List(Of Integer) = xs.Select(Function(x) x.D).OrderBy(Function(d) d).ToList()
            If dPdf.SequenceEqual(dXml) Then Continue For
            Dim restiPdf As List(Of TestaInfo) = teste.ToList()
            Dim restiXml As List(Of NotaXml) = xs.ToList()
            For Each x As NotaXml In xs
                Dim trovata As TestaInfo = Nothing
                For Each t As TestaInfo In restiPdf
                    If t.D = x.D AndAlso (Double.IsNaN(x.X) OrElse Math.Abs(t.X - (x.X * il / 10.0 + mu)) <= 0.7 * il) Then
                        trovata = t
                        Exit For
                    End If
                Next
                If trovata IsNot Nothing Then
                    restiPdf.Remove(trovata)
                    restiXml.Remove(x)
                End If
            Next
            If restiPdf.Count = 0 AndAlso restiXml.Count = 0 Then Continue For
            If restiPdf.Count = 1 AndAlso restiXml.Count = 1 AndAlso Not restiXml(0).Accidentale AndAlso Not cella.Alterazioni.Any(Function(a) Math.Abs(a.X - restiPdf(0).X) <= 3.2 * il AndAlso a.X < restiPdf(0).X) AndAlso Not xs.Any(Function(x) x.Accidentale AndAlso x.Passo = NomiPassi(((restiPdf(0).D Mod 7) + 7) Mod 7)) Then
                Dim t As TestaInfo = restiPdf(0)
                ris.Correzioni.Add("C|A|" & p.ToString() & "|" & k.ToString() & "|" & restiXml(0).Ordinale.ToString() & "|" & NomiPassi(((t.D Mod 7) + 7) Mod 7) & "|" & (t.D \ 7).ToString())
                corretteAltezze += 1
                Continue For
            End If
            ' (d) il resto sono dubbi: solo se mancano altezze diverse, non semplici note doppie all'unisono
            Dim mancanti As List(Of Integer) = restiPdf.Select(Function(t) t.D).Distinct().Where(Function(d) Not dXml.Contains(d)).ToList()
            Dim inPiu As List(Of Integer) = restiXml.Select(Function(x) x.D).Distinct().Where(Function(d) Not dPdf.Contains(d)).ToList()
            If mancanti.Count > 0 Then ris.Dubbi.Add(NuovoDubbio(p, k, "nel PDF " & If(mancanti.Count = 1, "c'è una nota che", "ci sono " & mancanti.Count.ToString() & " note che") & " nello spartito letto non " & If(mancanti.Count = 1, "c'è", "ci sono") & ", per esempio " & NomeD(mancanti(0))))
            If inPiu.Count > 0 Then ris.Dubbi.Add(NuovoDubbio(p, k, "nello spartito letto c'è una nota, " & NomeD(inPiu(0)) & ", che nel PDF non c'è"))
        Next
        ris.Riassunto = "teste del PDF uguali allo spartito letto in " & uguali.ToString() & " battute su " & confrontate.ToString() & "; ricostruite " & ricostruite.ToString() & " battute vuote, " & codine.ToString() & " codine, " & corretteAltezze.ToString() & " altezze; " & ris.Dubbi.Count.ToString() & " dubbi"
        Return ris
    End Function

    Private Shared Function NuovoDubbio(parte As Integer, misura As Integer, motivo As String) As DubbioMisura
        Dim d As New DubbioMisura()
        d.Parte = parte
        d.Misura = misura
        d.Motivo = motivo
        Return d
    End Function

    Private Shared Function BasePentagramma(segno As String, linea As Integer, ottava As Integer) As Integer
        If ottava <> 0 Then Return Integer.MinValue
        Select Case segno
            Case "G" : Return 32 - 2 * (linea - 1)
            Case "F" : Return 24 - 2 * (linea - 1)
            Case "C" : Return 28 - 2 * (linea - 1)
        End Select
        Return Integer.MinValue
    End Function

    Private Shared Function NomeD(d As Integer) As String
        Dim passo As String = NomiPassi(((d Mod 7) + 7) Mod 7)
        Return RevisioneMusicXml.NomeNota(passo, 0, d \ 7)
    End Function

    ''' <summary>L'alterazione di una testa: quella scritta subito prima di lei nella battuta, altrimenti quella della tonalità.</summary>
    Private Shared Function AlterazioneDellaTesta(t As TestaInfo, cella As CellaInfo, quinte As Integer) As Integer
        For Each a As AlterazioneInfo In cella.Alterazioni
            If a.X < t.X AndAlso t.X - a.X <= 3.2 * t.Interlinea AndAlso Math.Abs(a.Y - t.Y) <= 1.2 * t.Interlinea Then Return a.Valore
        Next
        Return RevisioneMusicXml.AlterazioneInChiave(NomiPassi(((t.D Mod 7) + 7) Mod 7), quinte)
    End Function
End Class
