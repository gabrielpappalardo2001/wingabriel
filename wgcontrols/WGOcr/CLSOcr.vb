Imports System
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports Tesseract
Imports UglyToad.PdfPig

Public Class CLSOcr

    Private Const NomeLinguaOcr As String = "ita"
    Private Const NomeFileLinguaOcr As String = "ita.traineddata"
    Private Const UrlDownloadLinguaOcr As String = "https://github.com/tesseract-ocr/tessdata_best/raw/main/ita.traineddata"

    Private Shared ReadOnly OggettoLock As New Object()

    Private ReadOnly PercorsoCartellaTessdata As String
    Private ReadOnly PercorsoFileLog As String


    ' La cartella dei log: con il programma installato (Programmi\WinGabriel) %LocalAppData%\WinGabriel\logs, con quello portatile logs
    ' nella cartella del programma. Lo stesso criterio di WGPercorsiApplicazione, che qui non si può usare.
    Private Shared Function CartellaLogApplicazione() As String
        Try
            Dim programma As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c)
            For Each cartella As Environment.SpecialFolder In New Environment.SpecialFolder() {Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86}
                If String.Equals(programma, Path.Combine(Environment.GetFolderPath(cartella), "WinGabriel").TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then
                    Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "logs")
                End If
            Next
            Return Path.Combine(programma, "logs")
        Catch
            Return Path.Combine(Path.GetTempPath(), "WinGabriel")
        End Try
    End Function

    Public Sub New()
        Try
            ' la lingua del riconoscimento è un modello di intelligenza artificiale: sta in models	essdata
            PercorsoCartellaTessdata = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaModelli(), "tessdata")
            PercorsoFileLog = Path.Combine(CartellaLogApplicazione(), "log.txt")
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            AssicuraCartellaTessdata()
            AssicuraLinguaItalianaDisponibile()
        Catch ex As Exception
            ScriviLog("CLSOcr.New: " & ex.ToString())
        End Try
    End Sub

    Public ReadOnly Property PercorsoTessdata As String
        Get
            Return PercorsoCartellaTessdata
        End Get
    End Property

    Public Function EstraiTesto(percorsoFile As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) Then
                Return String.Empty
            End If

            If Not File.Exists(percorsoFile) Then
                Return String.Empty
            End If

            Dim estensione As String = Path.GetExtension(percorsoFile).ToLowerInvariant()

            If estensione = ".pdf" Then
                Return EstraiTestoDaPdf(percorsoFile)
            End If

            Return EstraiTestoDaImmagine(percorsoFile)
        Catch ex As Exception
            ScriviLog("CLSOcr.EstraiTesto: " & ex.ToString())
            Return String.Empty
        End Try
    End Function

    Public Function EstraiTestoDaImmagine(percorsoImmagine As String) As String
        Dim testoRilevato As String = String.Empty
        Try
            If String.IsNullOrWhiteSpace(percorsoImmagine) OrElse Not File.Exists(percorsoImmagine) Then
                Return String.Empty
            End If

            If Not AssicuraLinguaItalianaDisponibile() Then Return String.Empty

            Using motoreOcr As New TesseractEngine(PercorsoCartellaTessdata, NomeLinguaOcr, EngineMode.Default)
                Using immaginePix As Pix = Pix.LoadFromFile(percorsoImmagine)
                    Using paginaOcr As Tesseract.Page = motoreOcr.Process(immaginePix)
                        testoRilevato = paginaOcr.GetText()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("CLSOcr.EstraiTestoDaImmagine: " & ex.ToString())
            testoRilevato = String.Empty
        End Try
        Return testoRilevato
    End Function

    Public Function EstraiTestoDaPdf(percorsoPdf As String) As String
        Dim generatoreTesto As New StringBuilder()
        Try
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then
                Return String.Empty
            End If

            Using documentoPdf As PdfDocument = PdfDocument.Open(percorsoPdf)
                For Each paginaPdf In documentoPdf.GetPages()
                    Try
                        generatoreTesto.AppendLine(paginaPdf.Text)
                    Catch exPagina As Exception
                        ScriviLog("CLSOcr.EstraiTestoDaPdf pagina: " & exPagina.ToString())
                    End Try
                Next
            End Using

            If String.IsNullOrWhiteSpace(generatoreTesto.ToString()) Then
                Return EstraiTestoDaPdfConOcr(percorsoPdf)
            End If
        Catch ex As Exception
            ScriviLog("CLSOcr.EstraiTestoDaPdf: " & ex.ToString())
            Try
                Return EstraiTestoDaPdfConOcr(percorsoPdf)
            Catch exOcr As Exception
                ScriviLog("CLSOcr.EstraiTestoDaPdf fallback ocr: " & exOcr.ToString())
                Return String.Empty
            End Try
        End Try
        Return generatoreTesto.ToString()
    End Function

    Private Function EstraiTestoDaPdfConOcr(percorsoPdf As String) As String
        Dim generatoreTesto As New StringBuilder()
        Dim cartellaTemporanea As String = String.Empty
        Try
            If Not AssicuraLinguaItalianaDisponibile() Then Return String.Empty

            cartellaTemporanea = Path.Combine(Path.GetTempPath(), "CLSOcr_" & Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory(cartellaTemporanea)

            Using documentoPdf As PdfDocument = PdfDocument.Open(percorsoPdf)
                Dim numeroPagina As Integer = 0
                For Each paginaPdf In documentoPdf.GetPages()
                    numeroPagina += 1
                    Try
                        Dim immagini = paginaPdf.GetImages()
                        For Each immaginePdf In immagini
                            Try
                                Dim bytesImmagine As Byte() = Nothing
                                Dim datiPng As Byte() = Nothing

                                If immaginePdf.TryGetPng(datiPng) Then
                                    bytesImmagine = datiPng
                                Else
                                    Dim memoriaImmagine As Memory(Of Byte)
                                    If immaginePdf.TryGetBytesAsMemory(memoriaImmagine) Then
                                        bytesImmagine = memoriaImmagine.ToArray()
                                    End If
                                End If

                                If bytesImmagine Is Nothing OrElse bytesImmagine.Length = 0 Then Continue For

                                Dim percorsoImmagineTemp As String = Path.Combine(cartellaTemporanea, "pagina" & numeroPagina.ToString() & "_" & Guid.NewGuid().ToString("N") & ".png")
                                File.WriteAllBytes(percorsoImmagineTemp, bytesImmagine)

                                Dim testoPagina As String = EstraiTestoDaImmagine(percorsoImmagineTemp)
                                If Not String.IsNullOrWhiteSpace(testoPagina) Then
                                    generatoreTesto.AppendLine(testoPagina)
                                End If
                            Catch exImmagine As Exception
                                ScriviLog("CLSOcr.EstraiTestoDaPdfConOcr immagine pagina " & numeroPagina.ToString() & ": " & exImmagine.ToString())
                            End Try
                        Next
                    Catch exPagina As Exception
                        ScriviLog("CLSOcr.EstraiTestoDaPdfConOcr pagina " & numeroPagina.ToString() & ": " & exPagina.ToString())
                    End Try
                Next
            End Using
        Catch ex As Exception
            ScriviLog("CLSOcr.EstraiTestoDaPdfConOcr: " & ex.ToString())
        Finally
            Try
                If Not String.IsNullOrWhiteSpace(cartellaTemporanea) AndAlso Directory.Exists(cartellaTemporanea) Then
                    Directory.Delete(cartellaTemporanea, True)
                End If
            Catch exPulizia As Exception
                ScriviLog("CLSOcr.EstraiTestoDaPdfConOcr pulizia: " & exPulizia.ToString())
            End Try
        End Try
        Return generatoreTesto.ToString()
    End Function

    Private Sub AssicuraCartellaTessdata()
        Try
            If Not Directory.Exists(PercorsoCartellaTessdata) Then
                Directory.CreateDirectory(PercorsoCartellaTessdata)
            End If
        Catch ex As Exception
            ScriviLog("CLSOcr.AssicuraCartellaTessdata: " & ex.ToString())
        End Try
    End Sub

    ' La lingua italiana del riconoscimento è un modello: se manca la scarica il centro di controllo, senza attese. Qui si risponde subito: vero
    ' se il modello c'è, falso se è partito lo scaricamento (chi chiama non può ancora leggere il testo e l'utente è avvisato).
    Private Function AssicuraLinguaItalianaDisponibile() As Boolean
        Try
            Dim percorsoFileLingua As String = Path.Combine(PercorsoCartellaTessdata, NomeFileLinguaOcr)
            If File.Exists(percorsoFileLingua) AndAlso New FileInfo(percorsoFileLingua).Length > 0 Then Return True

            AssicuraCartellaTessdata()
            Dim elementi As New System.Collections.Generic.List(Of wgcontrols.ElementoModello)()
            elementi.Add(New wgcontrols.ElementoModello() With {.Indirizzo = UrlDownloadLinguaOcr, .NomeFile = NomeFileLinguaOcr, .DimensioneMinima = 1000000L})
            Dim destinazione As String = percorsoFileLingua
            Dim installa As Func(Of String, String) =
                Function(temporanea As String) As String
                    If File.Exists(destinazione) Then File.Delete(destinazione)
                    File.Move(Path.Combine(temporanea, NomeFileLinguaOcr), destinazione)
                    Return ""
                End Function
            Dim centro As wgcontrols.WGCentroDiControllo = wgcontrols.WGCentroDiControllo.GetInstance()
            If Not centro.ScaricamentoModelliInCorso("ocr-italiano") Then
                If centro.AggiungiScaricamentoModelli("ocr-italiano", "modello per leggere il testo nelle immagini", elementi, installa,
                    Sub(esito As String)
                        If esito = "" Then centro.AvvisaUtente("Il modello per leggere il testo nelle immagini è pronto: ripeti l'operazione.")
                    End Sub) Then
                    centro.AvvisaUtente("Manca il modello per leggere il testo nelle immagini. Lo sto scaricando dal centro di controllo: quando è pronto ti avviso, e potrai ripetere l'operazione.")
                End If
            End If
            Return False
        Catch ex As Exception
            ScriviLog("CLSOcr.AssicuraLinguaItalianaDisponibile: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub ScriviLog(messaggio As String)
        Try
            SyncLock OggettoLock
                Try
                    Dim cartellaLog As String = Path.GetDirectoryName(PercorsoFileLog)
                    If Not String.IsNullOrWhiteSpace(cartellaLog) AndAlso Not Directory.Exists(cartellaLog) Then
                        Directory.CreateDirectory(cartellaLog)
                    End If

                    Dim riga As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine
                    File.AppendAllText(PercorsoFileLog, riga, Encoding.UTF8)
                Catch
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

End Class