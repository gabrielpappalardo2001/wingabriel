' ============================================================================================
' WGSpartitiImmagini - prepara la scansione di uno spartito prima del riconoscimento
' ============================================================================================
'
' Il riconoscimento delle note è tanto più preciso quanto più l'immagine è pulita. Per i PDF che contengono pagine scansionate
' WinGabriel non li fa ridisegnare: estrae le immagini così come sono state scansionate (nessuna perdita) e per ciascuna:
'   1. porta il righino a una dimensione ideale: Audiveris lavora al meglio con circa venti punti fra una linea del rigo e la
'      successiva; la distanza si misura sulla pagina stessa;
'   2. toglie le ombre e le sfumature con una soglia locale, così lo sfondo diventa bianco e le note nere;
'   3. raddrizza la pagina, misurando di quanto è inclinata (un grado di inclinazione basta a rovinare il riconoscimento);
'   4. toglie i bordi neri e le ombre della scansione, i puntini di sporco, e ricuce le linee del rigo spezzate o sbiadite;
'   5. lascia fuori le pagine senza righi (copertine, testo, pagine bianche): non si perde tempo a leggerle.
' Se le immagini del PDF non si possono estrarre (formati come JPEG 2000 o JBIG2) le pagine si disegnano con il lettore di PDF di Windows.
' Le pagine preparate vengono raccolte in un unico file TIFF a più pagine, e il brano si riconosce come una cosa sola.
'
' I PDF "veri", esportati da un programma di notazione, non si toccano: sono già perfetti e si passano così come sono.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Linq
Imports UglyToad.PdfPig

Public NotInheritable Class PreparazioneScansione

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Restituisce il file da dare al riconoscimento: un TIFF preparato, oppure l'originale se è un PDF vettoriale o se la
    ''' preparazione non riesce. esito contiene una frase che descrive cosa è stato fatto.
    ''' </summary>
    Public Shared Function Prepara(percorso As String, cartellaLavoro As String, ByRef esito As String, Optional interlineaIdeale As Double = 20.0) As String
        esito = ""
        Try
            Dim nonLeggibile As Boolean = False
            Dim pagine As List(Of Bitmap) = CaricaPagine(percorso, nonLeggibile)
            If (pagine Is Nothing OrElse pagine.Count = 0) AndAlso nonLeggibile Then
                ' il PDF è una scansione ma le sue immagini sono in un formato che non si estrae: si disegnano le pagine con Windows
                Dim esitoDisegno As String = ""
                Dim disegnato As String = RenderizzaPdf(percorso, Path.Combine(cartellaLavoro, "pagine"), 1.0, esitoDisegno, Sub(i As Integer, n As Integer) Return, Function() False)
                If disegnato <> "" Then pagine = CaricaPagine(disegnato, nonLeggibile)
            End If
            If pagine Is Nothing OrElse pagine.Count = 0 Then
                esito = "Il PDF contiene note disegnate, non una scansione: lo passo com'è."
                Return percorso
            End If
            Directory.CreateDirectory(cartellaLavoro)
            Dim destinazione As String = Path.Combine(cartellaLavoro, "preparato.tif")
            If File.Exists(destinazione) Then File.Delete(destinazione)
            Dim preparate As New List(Of Bitmap)()
            Dim senzaRighi As Integer = 0
            Try
                For Each originale As Bitmap In pagine
                    Dim angolo As Double = 0.0
                    Dim conRighi As Boolean = False
                    Dim pronta As Bitmap = PreparaPagina(originale, angolo, interlineaIdeale, conRighi)
                    ' l'originale non serve più: con molte pagine tenerle tutte in memoria raddoppia lo spazio occupato
                    originale.Dispose()
                    If conRighi Then
                        preparate.Add(pronta)
                    Else
                        senzaRighi += 1
                        pronta.Dispose()
                    End If
                Next
                If preparate.Count = 0 Then
                    esito = "In questo PDF non ci sono righi musicali: non è uno spartito, forse è un libro."
                    Return ""
                End If
                ScriviTiff(preparate, destinazione)
            Finally
                For Each b As Bitmap In pagine
                    b.Dispose()
                Next
                For Each b As Bitmap In preparate
                    b.Dispose()
                Next
            End Try
            esito = preparate.Count.ToString() & If(preparate.Count = 1, " pagina preparata", " pagine preparate") & ": ripulita, ingrandita o ridotta alla misura ideale, raddrizzata, senza bordi e senza sporco."
            If senzaRighi > 0 Then esito &= " " & senzaRighi.ToString() & If(senzaRighi = 1, " pagina senza righi lasciata fuori.", " pagine senza righi lasciate fuori.")
            Return destinazione
        Catch ex As Exception
            log.ScriviLog("PreparazioneScansione.Prepara: " & ex.Message & Environment.NewLine & ex.StackTrace)
            esito = "Non sono riuscito a preparare la scansione: uso il file originale."
            Return percorso
        End Try
    End Function

    Private Shared Function TipoWinRt(nome As String) As Type
        Return Type.GetType(nome & ", Windows, ContentType=WindowsRuntime")
    End Function

    ''' <summary>
    ''' Aspetta un'operazione asincrona di Windows (WinRT) e ne restituisce il risultato. Si fa per riflessione, senza riferimento al
    ''' Windows.winmd del Windows SDK: così il progetto compila e si impacchetta come prima (con il riferimento, la generazione dei
    ''' serializzatori XML falliva).
    ''' </summary>
    Private Shared Function Attendi(operazione As Object, Optional tipoRisultato As Type = Nothing) As Object
        Dim estensioni As Type = GetType(System.WindowsRuntimeSystemExtensions)
        Dim asTask As System.Reflection.MethodInfo
        If tipoRisultato Is Nothing Then
            asTask = estensioni.GetMethods().First(Function(m) m.Name = "AsTask" AndAlso Not m.IsGenericMethod AndAlso m.GetParameters().Length = 1 AndAlso m.GetParameters()(0).ParameterType.Name = "IAsyncAction")
        Else
            asTask = estensioni.GetMethods().First(Function(m) m.Name = "AsTask" AndAlso m.IsGenericMethod AndAlso m.GetParameters().Length = 1 AndAlso m.GetParameters()(0).ParameterType.Name = "IAsyncOperation`1").MakeGenericMethod(tipoRisultato)
        End If
        Dim compito As System.Threading.Tasks.Task = CType(asTask.Invoke(Nothing, New Object() {operazione}), System.Threading.Tasks.Task)
        compito.GetAwaiter().GetResult()
        If tipoRisultato Is Nothing Then Return Nothing
        Return compito.GetType().GetProperty("Result").GetValue(compito)
    End Function

    ''' <summary>
    ''' Disegna le pagine di un PDF con il lettore di PDF di Windows alla scala voluta e le raccoglie in un TIFF a più pagine. fattore è
    ''' rispetto ai 300 punti per pollice con cui Audiveris legge un PDF da solo: con 1,4 le teste di nota sono il 40% più grandi.
    ''' Serve alla seconda lettura dei PDF veri: Audiveris, che li legge sempre a 300 punti, ha il suo punto debole nelle partiture
    ''' d'orchestra, dove un rigo è piccolo e le note sono poco più di una dozzina di punti. Restituisce "" se non riesce.
    ''' </summary>
    Public Shared Function RenderizzaPdf(percorso As String, cartellaLavoro As String, fattore As Double, ByRef esito As String, avanzamento As Action(Of Integer, Integer), annulla As Func(Of Boolean)) As String
        esito = ""
        Dim primo As Bitmap = Nothing
        Try
            Directory.CreateDirectory(cartellaLavoro)
            Dim destinazione As String = Path.Combine(cartellaLavoro, "disegnato.tif")
            If File.Exists(destinazione) Then File.Delete(destinazione)
            Dim tFile As Type = TipoWinRt("Windows.Storage.StorageFile")
            Dim tDoc As Type = TipoWinRt("Windows.Data.Pdf.PdfDocument")
            Dim tOpzioni As Type = TipoWinRt("Windows.Data.Pdf.PdfPageRenderOptions")
            Dim tFlusso As Type = TipoWinRt("Windows.Storage.Streams.InMemoryRandomAccessStream")
            Dim tIngresso As Type = TipoWinRt("Windows.Storage.Streams.IInputStream")
            Dim tColori As Type = TipoWinRt("Windows.UI.Colors")
            If tFile Is Nothing OrElse tDoc Is Nothing OrElse tOpzioni Is Nothing OrElse tFlusso Is Nothing OrElse tIngresso Is Nothing OrElse tColori Is Nothing Then
                esito = "Questo Windows non ha il lettore di PDF."
                Return ""
            End If
            Dim sorgente As Object = Attendi(tFile.GetMethod("GetFileFromPathAsync").Invoke(Nothing, New Object() {percorso}), tFile)
            Dim doc As Object = Attendi(tDoc.GetMethod("LoadFromFileAsync", New Type() {tFile}).Invoke(Nothing, New Object() {sorgente}), tDoc)
            Dim codec As ImageCodecInfo = ImageCodecInfo.GetImageEncoders().First(Function(c) c.MimeType = "image/tiff")
            Dim comeFlusso As System.Reflection.MethodInfo = GetType(System.IO.WindowsRuntimeStreamExtensions).GetMethods().First(Function(m) m.Name = "AsStreamForRead" AndAlso m.GetParameters().Length = 1 AndAlso m.GetParameters()(0).ParameterType Is tIngresso)
            Dim bianco As Object = tColori.GetProperty("White").GetValue(Nothing)
            Dim totale As Integer = CInt(doc.PageCount)
            For i As Integer = 0 To totale - 1
                If annulla() Then Throw New OperationCanceledException()
                avanzamento(i + 1, totale)
                Dim pagina As Object = doc.GetPage(CUInt(i))
                Try
                    Dim misura As Object = pagina.Size
                    Dim f As Double = fattore
                    Dim larghezza As Double = CDbl(misura.Width) * 3.125 * f
                    Dim altezza As Double = CDbl(misura.Height) * 3.125 * f
                    If larghezza * altezza > 90000000.0 Then
                        f = f * Math.Sqrt(90000000.0 / (larghezza * altezza))
                        larghezza = CDbl(misura.Width) * 3.125 * f
                        altezza = CDbl(misura.Height) * 3.125 * f
                    End If
                    Dim opzioni As Object = Activator.CreateInstance(tOpzioni)
                    opzioni.DestinationWidth = CUInt(Math.Round(larghezza))
                    opzioni.DestinationHeight = CUInt(Math.Round(altezza))
                    opzioni.BackgroundColor = bianco
                    Dim flusso As Object = Activator.CreateInstance(tFlusso)
                    Try
                        Attendi(pagina.RenderToStreamAsync(flusso, opzioni))
                        Using memoria As New MemoryStream()
                            Using lettura As Stream = CType(comeFlusso.Invoke(Nothing, New Object() {flusso}), Stream)
                                lettura.CopyTo(memoria)
                            End Using
                            memoria.Position = 0
                            Using png As New Bitmap(memoria)
                                ' il lettore di Windows può tener conto dell'ingrandimento dello schermo: si porta l'immagine alla misura chiesta
                                Dim lw As Integer = CInt(Math.Round(larghezza))
                                Dim lh As Integer = CInt(Math.Round(altezza))
                                Dim b As New Bitmap(lw, lh, PixelFormat.Format24bppRgb)
                                Using g As Graphics = Graphics.FromImage(b)
                                    g.Clear(Color.White)
                                    g.InterpolationMode = InterpolationMode.HighQualityBicubic
                                    g.PixelOffsetMode = PixelOffsetMode.HighQuality
                                    g.DrawImage(png, 0, 0, lw, lh)
                                End Using
                                If i = 0 Then
                                    Dim comp As New EncoderParameters(2)
                                    comp.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.MultiFrame))
                                    comp.Param(1) = New EncoderParameter(Encoder.Compression, CLng(EncoderValue.CompressionLZW))
                                    b.Save(destinazione, codec, comp)
                                    primo = b
                                Else
                                    Dim p As New EncoderParameters(2)
                                    p.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.FrameDimensionPage))
                                    p.Param(1) = New EncoderParameter(Encoder.Compression, CLng(EncoderValue.CompressionLZW))
                                    primo.SaveAdd(b, p)
                                    b.Dispose()
                                End If
                            End Using
                        End Using
                    Finally
                        CType(flusso, IDisposable).Dispose()
                    End Try
                Finally
                    CType(pagina, IDisposable).Dispose()
                End Try
            Next
            If primo IsNot Nothing Then
                Dim fine As New EncoderParameters(1)
                fine.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.Flush))
                primo.SaveAdd(fine)
            End If
            esito = totale.ToString() & " pagine disegnate con il lettore di PDF di Windows alla scala " & fattore.ToString("0.00", Globalization.CultureInfo.InvariantCulture)
            Return destinazione
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            log.ScriviLog("PreparazioneScansione.RenderizzaPdf: " & ex.Message & Environment.NewLine & ex.StackTrace)
            esito = "Non sono riuscito a disegnare le pagine: " & ex.Message
            Return ""
        Finally
            If primo IsNot Nothing Then primo.Dispose()
        End Try
    End Function

    ' ---- ingresso -------------------------------------------------------------------------------------------

    Private Shared Function CaricaPagine(percorso As String, ByRef nonLeggibile As Boolean) As List(Of Bitmap)
        nonLeggibile = False
        Dim risultato As New List(Of Bitmap)()
        If Path.GetExtension(percorso).ToLowerInvariant() <> ".pdf" Then
            Using sorgente As New Bitmap(percorso)
                Dim dimensione As New FrameDimension(sorgente.FrameDimensionsList(0))
                Dim n As Integer = sorgente.GetFrameCount(dimensione)
                For i As Integer = 0 To n - 1
                    sorgente.SelectActiveFrame(dimensione, i)
                    risultato.Add(New Bitmap(sorgente))
                Next
            End Using
            Return risultato
        End If

        Using doc As PdfDocument = PdfDocument.Open(percorso)
            For Each pagina In doc.GetPages()
                Dim migliore As Bitmap = Nothing
                Dim areaPagina As Double = pagina.Width * pagina.Height
                Dim cePaginaIntera As Boolean = False
                For Each immagine In pagina.GetImages()
                    Dim a As Double = Math.Abs(immagine.BoundingBox.Width * immagine.BoundingBox.Height)
                    If a < areaPagina * 0.6 Then Continue For
                    cePaginaIntera = True
                    Dim png As Byte() = Nothing
                    If immagine.TryGetPng(png) AndAlso png IsNot Nothing Then
                        Using ms As New MemoryStream(png)
                            Using tmp As New Bitmap(ms)
                                migliore = New Bitmap(tmp)
                            End Using
                        End Using
                        Exit For
                    ElseIf immagine.RawBytes.Length > 0 Then
                        Try
                            Using ms As New MemoryStream(immagine.RawBytes.ToArray())
                                Using tmp As New Bitmap(ms)
                                    migliore = New Bitmap(tmp)
                                End Using
                            End Using
                            Exit For
                        Catch
                        End Try
                    End If
                Next
                If migliore Is Nothing Then
                    nonLeggibile = cePaginaIntera
                    For Each b As Bitmap In risultato
                        b.Dispose()
                    Next
                    Return Nothing
                End If
                risultato.Add(migliore)
            Next
        End Using
        Return risultato
    End Function

    ' ---- elaborazione di una pagina --------------------------------------------------------------------------

    Private Shared Function ToGrigi(b As Bitmap, ByRef w As Integer, ByRef h As Integer) As Byte()
        w = b.Width
        h = b.Height
        Dim dati As BitmapData = b.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb)
        Try
            Dim righe(dati.Stride * h - 1) As Byte
            System.Runtime.InteropServices.Marshal.Copy(dati.Scan0, righe, 0, righe.Length)
            Dim g(w * h - 1) As Byte
            For y As Integer = 0 To h - 1
                Dim o As Integer = y * dati.Stride
                Dim k As Integer = y * w
                For x As Integer = 0 To w - 1
                    Dim bl As Integer = righe(o)
                    Dim ve As Integer = righe(o + 1)
                    Dim ro As Integer = righe(o + 2)
                    g(k + x) = CByte((ro * 299 + ve * 587 + bl * 114) \ 1000)
                    o += 3
                Next
            Next
            Return g
        Finally
            b.UnlockBits(dati)
        End Try
    End Function

    Private Shared Function DaGrigi(g As Byte(), w As Integer, h As Integer) As Bitmap
        Dim b As New Bitmap(w, h, PixelFormat.Format24bppRgb)
        Dim dati As BitmapData = b.LockBits(New Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb)
        Try
            Dim righe(dati.Stride * h - 1) As Byte
            For y As Integer = 0 To h - 1
                Dim o As Integer = y * dati.Stride
                Dim k As Integer = y * w
                For x As Integer = 0 To w - 1
                    Dim v As Byte = g(k + x)
                    righe(o) = v
                    righe(o + 1) = v
                    righe(o + 2) = v
                    o += 3
                Next
            Next
            System.Runtime.InteropServices.Marshal.Copy(righe, 0, dati.Scan0, righe.Length)
        Finally
            b.UnlockBits(dati)
        End Try
        Return b
    End Function

    ''' <summary>Binarizza con una soglia che si adatta alla luce di ogni zona: 0 = nero, 255 = bianco.</summary>
    Private Shared Function SogliaLocale(g As Byte(), w As Integer, h As Integer) As Byte()
        ' media locale su finestre grandi, calcolata sull'immagine ridotta di quattro volte
        Dim f As Integer = 4
        Dim sw As Integer = Math.Max(1, w \ f)
        Dim sh As Integer = Math.Max(1, h \ f)
        Dim integrale((sw + 1) * (sh + 1) - 1) As Long
        For y As Integer = 0 To sh - 1
            Dim somma As Long = 0
            For x As Integer = 0 To sw - 1
                somma += g(Math.Min(h - 1, y * f) * w + Math.Min(w - 1, x * f))
                integrale((y + 1) * (sw + 1) + x + 1) = integrale(y * (sw + 1) + x + 1) + somma
            Next
        Next
        Dim raggio As Integer = Math.Max(8, Math.Min(sw, sh) \ 12)
        Dim medie(sw * sh - 1) As Single
        For y As Integer = 0 To sh - 1
            Dim y0 As Integer = Math.Max(0, y - raggio)
            Dim y1 As Integer = Math.Min(sh, y + raggio + 1)
            For x As Integer = 0 To sw - 1
                Dim x0 As Integer = Math.Max(0, x - raggio)
                Dim x1 As Integer = Math.Min(sw, x + raggio + 1)
                Dim s As Long = integrale(y1 * (sw + 1) + x1) - integrale(y0 * (sw + 1) + x1) - integrale(y1 * (sw + 1) + x0) + integrale(y0 * (sw + 1) + x0)
                medie(y * sw + x) = CSng(s / CDbl((x1 - x0) * (y1 - y0)))
            Next
        Next
        Dim r(w * h - 1) As Byte
        For y As Integer = 0 To h - 1
            Dim sy As Integer = Math.Min(sh - 1, y \ f)
            For x As Integer = 0 To w - 1
                Dim m As Single = medie(sy * sw + Math.Min(sw - 1, x \ f))
                Dim soglia As Single = m * 0.82F
                r(y * w + x) = If(g(y * w + x) < soglia, CByte(0), CByte(255))
            Next
        Next
        Return r
    End Function

    ''' <summary>Misura la distanza fra una linea del rigo e la successiva (da una linea alla seguente) in punti.</summary>
    Private Shared Function MisuraPassoRigo(b As Byte(), w As Integer, h As Integer) As Double
        Dim forza As Integer = 0
        Return MisuraPassoRigo(b, w, h, forza)
    End Function

    ''' <summary>Come sopra; forza dice quanto è netto il risultato (0 se non si trova nessun rigo).</summary>
    Private Shared Function MisuraPassoRigo(b As Byte(), w As Integer, h As Integer, ByRef forza As Integer) As Double
        forza = 0
        Dim istogramma(200) As Integer
        Dim passoColonne As Integer = Math.Max(1, w \ 400)
        For x As Integer = 0 To w - 1 Step passoColonne
            Dim y As Integer = 0
            Dim ultimaFineNera As Integer = -1
            Dim ultimaLunghezzaNera As Integer = 0
            While y < h
                If b(y * w + x) = 0 Then
                    Dim inizio As Integer = y
                    While y < h AndAlso b(y * w + x) = 0
                        y += 1
                    End While
                    Dim lunghezza As Integer = y - inizio
                    If ultimaFineNera >= 0 AndAlso lunghezza <= 12 AndAlso ultimaLunghezzaNera <= 12 Then
                        Dim passo As Integer = inizio - (ultimaFineNera - ultimaLunghezzaNera)
                        If passo >= 4 AndAlso passo <= 200 Then istogramma(passo) += 1
                    End If
                    ultimaFineNera = y
                    ultimaLunghezzaNera = lunghezza
                Else
                    y += 1
                End If
            End While
        Next
        Dim migliore As Integer = 0
        Dim massimo As Integer = 0
        For i As Integer = 4 To 200
            Dim somma As Integer = istogramma(i) * 2 + istogramma(i - 1) + If(i < 200, istogramma(i + 1), 0)
            If somma > massimo Then
                massimo = somma
                migliore = i
            End If
        Next
        If migliore = 0 OrElse massimo < 50 Then Return 0.0
        forza = massimo
        Return migliore
    End Function

    ''' <summary>La pagina scambiata fra righe e colonne: le linee verticali diventano orizzontali.</summary>
    Private Shared Function Trasposta(b As Byte(), w As Integer, h As Integer) As Byte()
        Dim t(w * h - 1) As Byte
        For y As Integer = 0 To h - 1
            Dim riga As Integer = y * w
            For x As Integer = 0 To w - 1
                t(x * h + y) = b(riga + x)
            Next
        Next
        Return t
    End Function

    ''' <summary>
    ''' Vero se i righi, già orizzontali e dritti, hanno la chiave e l'armatura a destra invece che a sinistra: la pagina è capovolta.
    ''' All'inizio di ogni rigo ci sono chiave, armatura e tempo, alla fine solo una stanghetta: dove c'è più inchiostro è l'inizio.
    ''' </summary>
    Private Shared Function CapovoltaSottoSopra(b As Byte(), w As Integer, h As Integer) As Boolean
        ' si lavora sulla pagina ridotta: ogni punto è nero se lo è un punto qualunque del suo quadratino, così le linee dei righi
        ' curve (vicino alla rilegatura) o spezzate restano linee continue
        Dim f As Integer = Math.Max(2, w \ 700)
        Dim sw As Integer = w \ f
        Dim sh As Integer = h \ f
        If sw < 50 OrElse sh < 50 Then Return False
        Dim r(sw * sh - 1) As Boolean
        For y As Integer = 0 To sh * f - 1
            Dim riga As Integer = y * w
            Dim ry As Integer = (y \ f) * sw
            For x As Integer = 0 To sw * f - 1
                If b(riga + x) = 0 Then r(ry + x \ f) = True
            Next
        Next
        ' ingrossate in verticale di due punti per parte, solo per trovare le linee
        Dim grossa(sw * sh - 1) As Boolean
        For x As Integer = 0 To sw - 1
            Dim vicini As Integer = 0
            For y As Integer = 0 To sh - 1 + 2
                If y < sh AndAlso r(y * sw + x) Then vicini += 1
                If y - 5 >= 0 AndAlso r((y - 5) * sw + x) Then vicini -= 1
                Dim centro As Integer = y - 2
                If centro >= 0 AndAlso centro < sh AndAlso vicini > 0 Then grossa(centro * sw + x) = True
            Next
        Next
        Dim inizi As New List(Of Integer)()
        Dim fini As New List(Of Integer)()
        Dim primaRiga As Integer = -1
        Dim ultimaRiga As Integer = -1
        Dim minimo As Integer = CInt(sw * 0.2)
        For y As Integer = 0 To sh - 1
            Dim riga As Integer = y * sw
            Dim migliorInizio As Integer = -1
            Dim migliorLunghezza As Integer = 0
            Dim x As Integer = 0
            While x < sw
                If grossa(riga + x) Then
                    Dim inizio As Integer = x
                    While x < sw AndAlso grossa(riga + x)
                        x += 1
                    End While
                    If x - inizio > migliorLunghezza Then
                        migliorLunghezza = x - inizio
                        migliorInizio = inizio
                    End If
                Else
                    x += 1
                End If
            End While
            If migliorLunghezza >= minimo Then
                inizi.Add(migliorInizio)
                fini.Add(migliorInizio + migliorLunghezza)
                If primaRiga < 0 Then primaRiga = y
                ultimaRiga = y
            End If
        Next
        If inizi.Count < 5 Then Return False
        inizi.Sort()
        fini.Sort()
        Dim sinistra As Integer = inizi(inizi.Count \ 2)
        Dim destra As Integer = fini(fini.Count \ 2)
        Dim fascia As Integer = CInt((destra - sinistra) * 0.1)
        If fascia < 3 Then Return False
        ' l'inchiostro delle fasce all'inizio e alla fine dei righi, tolte le linee stesse (uguali da una parte e dall'altra)
        Dim inchiostroSinistra As Long = 0
        Dim inchiostroDestra As Long = 0
        For y As Integer = primaRiga To ultimaRiga
            Dim riga As Integer = y * sw
            For x As Integer = sinistra To Math.Min(sw - 1, sinistra + fascia - 1)
                If r(riga + x) Then inchiostroSinistra += 1
            Next
            For x As Integer = Math.Max(0, destra - fascia) To Math.Min(sw - 1, destra - 1)
                If r(riga + x) Then inchiostroDestra += 1
            Next
        Next
        Return inchiostroDestra > inchiostroSinistra
    End Function

    Private Shared Function Gira180(b As Byte(), w As Integer, h As Integer) As Byte()
        Dim r(w * h - 1) As Byte
        Dim n As Integer = w * h
        For i As Integer = 0 To n - 1
            r(n - 1 - i) = b(i)
        Next
        Return r
    End Function

    ''' <summary>Di quanti gradi (da -4 a +4) è inclinata la pagina: l'angolo dove le linee dei righi sono più nette.</summary>
    Private Shared Function MisuraInclinazione(b As Byte(), w As Integer, h As Integer) As Double
        Dim passo As Integer = Math.Max(1, Math.Min(w, h) \ 700)
        Dim xs As New List(Of Integer)()
        Dim ys As New List(Of Integer)()
        For y As Integer = 0 To h - 1 Step passo
            For x As Integer = 0 To w - 1 Step passo
                If b(y * w + x) = 0 Then
                    xs.Add(x \ passo)
                    ys.Add(y \ passo)
                End If
            Next
        Next
        If xs.Count < 1000 Then Return 0.0
        Dim altezzaRidotta As Integer = h \ passo + 1
        Dim larghezzaRidotta As Integer = w \ passo + 1
        Dim miglioreAngolo As Double = 0.0
        Dim miglioreValore As Double = -1.0
        Dim valuta As Func(Of Double, Double) =
            Function(gradi As Double) As Double
                Dim rad As Double = gradi * Math.PI / 180.0
                Dim s As Double = Math.Sin(rad)
                Dim c As Double = Math.Cos(rad)
                Dim righe(altezzaRidotta + larghezzaRidotta + 2) As Integer
                Dim offset As Integer = larghezzaRidotta
                For i As Integer = 0 To xs.Count - 1
                    Dim yy As Integer = CInt(ys(i) * c - xs(i) * s) + offset
                    If yy >= 0 AndAlso yy < righe.Length Then righe(yy) += 1
                Next
                Dim somma As Double = 0.0
                For i As Integer = 1 To righe.Length - 1
                    Dim d As Double = righe(i) - righe(i - 1)
                    somma += d * d
                Next
                Return somma
            End Function
        Dim a As Double = -4.0
        While a <= 4.0001
            Dim v As Double = valuta(a)
            If v > miglioreValore Then
                miglioreValore = v
                miglioreAngolo = a
            End If
            a += 0.1
        End While
        Dim centro As Double = miglioreAngolo
        a = centro - 0.1
        While a <= centro + 0.1001
            Dim v As Double = valuta(a)
            If v > miglioreValore Then
                miglioreValore = v
                miglioreAngolo = a
            End If
            a += 0.02
        End While
        Return miglioreAngolo
    End Function

    Private Shared Function Ruota(b As Byte(), w As Integer, h As Integer, gradi As Double) As Byte()
        If Math.Abs(gradi) < 0.03 Then Return b
        Dim rad As Double = gradi * Math.PI / 180.0
        Dim s As Double = Math.Sin(rad)
        Dim c As Double = Math.Cos(rad)
        Dim cx As Double = w / 2.0
        Dim cy As Double = h / 2.0
        Dim r(w * h - 1) As Byte
        For y As Integer = 0 To h - 1
            For x As Integer = 0 To w - 1
                ' la pagina ruotata di -gradi: dalla posizione di arrivo si risale a quella di partenza
                Dim dx As Double = x - cx
                Dim dy As Double = y - cy
                Dim sx As Integer = CInt(Math.Round(cx + dx * c - dy * s))
                Dim sy As Integer = CInt(Math.Round(cy + dx * s + dy * c))
                If sx >= 0 AndAlso sx < w AndAlso sy >= 0 AndAlso sy < h Then
                    r(y * w + x) = b(sy * w + sx)
                Else
                    r(y * w + x) = 255
                End If
            Next
        Next
        Return r
    End Function

    Private Shared Function PreparaPagina(originale As Bitmap, ByRef angolo As Double, interlineaIdeale As Double, ByRef conRighi As Boolean) As Bitmap
        Dim giratoDiLato As Boolean = False
        Dim w As Integer = 0
        Dim h As Integer = 0
        Dim grigi As Byte() = ToGrigi(originale, w, h)
        Dim binaria As Byte() = SogliaLocale(grigi, w, h)

        ' una pagina scansionata di lato ha i righi verticali: se le linee si misurano meglio fra una colonna e l'altra, la si gira
        Dim forzaOrizzontale As Integer = 0
        MisuraPassoRigo(binaria, w, h, forzaOrizzontale)
        Dim forzaVerticale As Integer = 0
        MisuraPassoRigo(Trasposta(binaria, w, h), h, w, forzaVerticale)
        If forzaVerticale >= 50 AndAlso forzaVerticale > forzaOrizzontale * 2 Then
            originale.RotateFlip(RotateFlipType.Rotate90FlipNone)
            grigi = ToGrigi(originale, w, h)
            binaria = SogliaLocale(grigi, w, h)
            giratoDiLato = True
        End If

        ' la scala: dal passo del rigo che si misura sulla pagina
        Dim passo As Double = MisuraPassoRigo(binaria, w, h)
        conRighi = passo > 0.0
        If passo > 0.0 Then
            Dim fattore As Double = interlineaIdeale / passo
            If fattore > 1.12 OrElse fattore < 0.88 Then
                fattore = Math.Max(0.4, Math.Min(4.0, fattore))
                Dim nw As Integer = CInt(w * fattore)
                Dim nh As Integer = CInt(h * fattore)
                If CLng(nw) * nh < 80000000L Then
                    Using grande As New Bitmap(nw, nh, PixelFormat.Format24bppRgb)
                        Using gr As Graphics = Graphics.FromImage(grande)
                            gr.InterpolationMode = InterpolationMode.HighQualityBicubic
                            gr.PixelOffsetMode = PixelOffsetMode.HighQuality
                            gr.SmoothingMode = SmoothingMode.HighQuality
                            gr.DrawImage(originale, 0, 0, nw, nh)
                        End Using
                        grigi = ToGrigi(grande, w, h)
                    End Using
                    binaria = SogliaLocale(grigi, w, h)
                End If
            End If
        End If

        angolo = MisuraInclinazione(binaria, w, h)
        binaria = Ruota(binaria, w, h, angolo)
        ' una pagina capovolta (o girata di lato dalla parte sbagliata) si riconosce dal punto dove stanno la chiave e l'armatura
        If conRighi AndAlso CapovoltaSottoSopra(binaria, w, h) Then
            binaria = Gira180(binaria, w, h)
            log.ScriviLog("PreparazioneScansione: pagina girata di 180 gradi" & If(giratoDiLato, " (dopo averla girata di lato)", ""))
        ElseIf giratoDiLato Then
            log.ScriviLog("PreparazioneScansione: pagina girata di lato")
        End If
        If conRighi Then Ritocca(binaria, w, h)
        Return DaGrigi(binaria, w, h)
    End Function

    ' ---- ritocchi ---------------------------------------------------------------------------------------------

    ' Ricuce le linee del rigo spezzate: un'interruzione di tre punti al massimo fra due tratti lunghi sulla stessa riga si riempie.
    ' Le linee sbiadite o rovinate dalla scansione sono la prima causa di righi non riconosciuti.
    Private Shared Sub RattoppaRighe(b As Byte(), w As Integer, h As Integer)
        For y As Integer = 0 To h - 1
            Dim riga As Integer = y * w
            Dim x As Integer = 0
            Dim inizioUnito As Integer = -1
            Dim fineUnita As Integer = -1
            While x < w
                If b(riga + x) = 0 Then
                    Dim inizio As Integer = x
                    While x < w AndAlso b(riga + x) = 0
                        x += 1
                    End While
                    If fineUnita >= 0 AndAlso inizio - fineUnita <= 3 AndAlso (x - inizio) >= 12 AndAlso (fineUnita - inizioUnito) >= 12 Then
                        For k As Integer = fineUnita To inizio - 1
                            b(riga + k) = 0
                        Next
                    Else
                        inizioUnito = inizio
                    End If
                    fineUnita = x
                Else
                    x += 1
                End If
            End While
        Next
    End Sub

    ' Toglie lo sporco: i puntini piccolissimi, e i bordi neri e le ombre della scansione, cioè le macchie nere che toccano il margine
    ' della pagina e la attraversano per più di metà.
    Private Shared Sub Ritocca(b As Byte(), w As Integer, h As Integer)
        RattoppaRighe(b, w, h)
        Dim visto(w * h - 1) As Boolean
        Dim pila As New List(Of Integer)()
        Dim componente As New List(Of Integer)()
        For i As Integer = 0 To w * h - 1
            If b(i) <> 0 OrElse visto(i) Then Continue For
            componente.Clear()
            pila.Clear()
            pila.Add(i)
            visto(i) = True
            Dim minX As Integer = w
            Dim maxX As Integer = 0
            Dim minY As Integer = h
            Dim maxY As Integer = 0
            While pila.Count > 0
                Dim p As Integer = pila(pila.Count - 1)
                pila.RemoveAt(pila.Count - 1)
                componente.Add(p)
                Dim py As Integer = p \ w
                Dim px As Integer = p - py * w
                If px < minX Then minX = px
                If px > maxX Then maxX = px
                If py < minY Then minY = py
                If py > maxY Then maxY = py
                If px > 0 AndAlso b(p - 1) = 0 AndAlso Not visto(p - 1) Then
                    visto(p - 1) = True
                    pila.Add(p - 1)
                End If
                If px < w - 1 AndAlso b(p + 1) = 0 AndAlso Not visto(p + 1) Then
                    visto(p + 1) = True
                    pila.Add(p + 1)
                End If
                If py > 0 AndAlso b(p - w) = 0 AndAlso Not visto(p - w) Then
                    visto(p - w) = True
                    pila.Add(p - w)
                End If
                If py < h - 1 AndAlso b(p + w) = 0 AndAlso Not visto(p + w) Then
                    visto(p + w) = True
                    pila.Add(p + w)
                End If
            End While
            Dim toccaIlMargine As Boolean = minX = 0 OrElse maxX = w - 1 OrElse minY = 0 OrElse maxY = h - 1
            Dim alto As Integer = maxY - minY + 1
            Dim largo As Integer = maxX - minX + 1
            Dim daTogliere As Boolean = componente.Count <= 6
            ' un bordo o un'ombra sono macchie piene; un rigo che arriva al margine, con le sue stanghette, è una rete rada: non si tocca
            Dim piena As Boolean = componente.Count >= 0.25 * CDbl(alto) * CDbl(largo)
            If Not daTogliere AndAlso toccaIlMargine AndAlso piena Then
                If alto >= h \ 2 Then
                    daTogliere = True
                ElseIf largo >= w \ 2 AndAlso alto < h \ 20 AndAlso (minY < h \ 25 OrElse maxY > h - h \ 25) Then
                    daTogliere = True
                End If
            End If
            If daTogliere Then
                For Each p As Integer In componente
                    b(p) = 255
                Next
            End If
        Next
    End Sub

    ' ---- uscita ---------------------------------------------------------------------------------------------

    Private Shared Sub ScriviTiff(pagine As List(Of Bitmap), destinazione As String)
        Dim codec As ImageCodecInfo = ImageCodecInfo.GetImageEncoders().First(Function(c) c.MimeType = "image/tiff")
        Dim primo As Bitmap = pagine(0)
        Dim comp As New EncoderParameters(2)
        comp.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.MultiFrame))
        comp.Param(1) = New EncoderParameter(Encoder.Compression, CLng(EncoderValue.CompressionLZW))
        primo.Save(destinazione, codec, comp)
        For i As Integer = 1 To pagine.Count - 1
            Dim p As New EncoderParameters(2)
            p.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.FrameDimensionPage))
            p.Param(1) = New EncoderParameter(Encoder.Compression, CLng(EncoderValue.CompressionLZW))
            primo.SaveAdd(pagine(i), p)
        Next
        Dim fine As New EncoderParameters(1)
        fine.Param(0) = New EncoderParameter(Encoder.SaveFlag, CLng(EncoderValue.Flush))
        primo.SaveAdd(fine)
    End Sub

End Class
