Imports System.IO
Imports System.IO.Compression
Imports System.Xml.Linq
Imports System.Text
Imports System.Collections.Generic
Imports System.Linq
Imports System.Text.RegularExpressions
Imports DocumentFormat.OpenXml
Imports DocumentFormat.OpenXml.Packaging
Imports OX = DocumentFormat.OpenXml.Wordprocessing

' Strumenti di utilità per il caricamento, il salvataggio e la conversione dei documenti tra formati diversi come DOCX e ODT.
Public Class WGDocumentState
    Public Property Elements As List(Of DocElement) = New List(Of DocElement)()
    Public Property PageSettings As Dictionary(Of String, Integer) = New Dictionary(Of String, Integer)()
    Public Property StiliTitolo As Dictionary(Of String, Integer) = New Dictionary(Of String, Integer)()
    Public Property CommentiPerParagrafo As Dictionary(Of String, List(Of String)) = New Dictionary(Of String, List(Of String))()
    Public Property ImmaginiMedia As Dictionary(Of String, Byte()) = New Dictionary(Of String, Byte())()
    Public Property MappaRelazioni As Dictionary(Of String, String) = New Dictionary(Of String, String)()
    Public Property SourcePath As String = String.Empty
    Public Property HeaderText As String = String.Empty
    Public Property FooterText As String = String.Empty
End Class

Public Class WGWordUtils

    Private Shared logger As New Logger()
    Private Shared ReadOnly _configAutore As New WGConfig()

    Public Shared Function OttieniNomeAutore() As String
        Try
            Dim nome As String = _configAutore.LeggiValore("NomeAutore")
            If Not String.IsNullOrWhiteSpace(nome) Then Return nome.Trim()
        Catch ex As Exception
            logger.ScriviLog($"OttieniNomeAutore lettura configurazione: {ex.Message}")
        End Try

        Try
            Dim nomeUtente As String = Environment.UserName
            If Not String.IsNullOrWhiteSpace(nomeUtente) Then Return nomeUtente.Trim()
        Catch ex As Exception
            logger.ScriviLog($"OttieniNomeAutore lettura utente Windows: {ex.Message}")
        End Try

        Return "WinGabriel"
    End Function

    Private Shared ReadOnly NsW As XNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
    Private Shared ReadOnly NsR As XNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
    Private Shared ReadOnly NsMc As XNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006"
    Private Shared ReadOnly NsWp As XNamespace = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
    Private Shared ReadOnly NsA As XNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main"
    Private Shared ReadOnly NsPic As XNamespace = "http://schemas.openxmlformats.org/drawingml/2006/picture"
    Private Shared ReadOnly NsV As XNamespace = "urn:schemas-microsoft-com:vml"
    Private Shared ReadOnly NsO As XNamespace = "urn:schemas-microsoft-com:office:office"
    Private Shared ReadOnly NsWps As XNamespace = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
    Private Shared ReadOnly NsWpg As XNamespace = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup"
    Private Shared _bookmarkSaveCounter As Integer = 1

    Private Shared ReadOnly EntriesSostituite As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "word/document.xml",
        "word/styles.xml",
        "word/settings.xml",
        "word/numbering.xml",
        "word/comments.xml",
        "word/footnotes.xml",
        "word/endnotes.xml",
        "[Content_Types].xml",
        "_rels/.rels",
        "word/_rels/document.xml.rels"
    }

    Public Shared Function OttieniDimensioniTipoPagina(tipoPagina As String) As Tuple(Of Integer, Integer)
        Try
            Select Case If(tipoPagina, "").Trim().ToUpperInvariant()
                Case "A4" : Return Tuple.Create(11906, 16838)
                Case "LETTER" : Return Tuple.Create(12240, 15840)
                Case "LEGAL" : Return Tuple.Create(12240, 20160)
                Case "A3" : Return Tuple.Create(16838, 23811)
                Case "A5" : Return Tuple.Create(8391, 11906)
                Case "EXECUTIVE" : Return Tuple.Create(10440, 15120)
                Case Else : Return Tuple.Create(11906, 16838)
            End Select
        Catch ex As Exception
            logger.ScriviLog($"OttieniDimensioniTipoPagina: {ex.Message}")
            Return Tuple.Create(11906, 16838)
        End Try
    End Function

    Public Shared Function ElencoTipiPaginaDisponibili() As List(Of String)
        Return New List(Of String) From {"A4", "Letter", "Legal", "A3", "A5", "Executive"}
    End Function

    Public Shared Sub ApplicaTipoPagina(pageSettings As Dictionary(Of String, Integer), tipoPagina As String)
        Try
            If pageSettings Is Nothing Then Return
            Dim dimensioni As Tuple(Of Integer, Integer) = OttieniDimensioniTipoPagina(tipoPagina)

            Dim orientLandscape As Boolean = pageSettings.ContainsKey("Orientation") AndAlso pageSettings("Orientation") = 2
            Dim larghezza As Integer = dimensioni.Item1
            Dim altezza As Integer = dimensioni.Item2
            If orientLandscape AndAlso larghezza < altezza Then
                Dim scambio As Integer = larghezza
                larghezza = altezza
                altezza = scambio
            End If

            pageSettings("PageWidth") = larghezza
            pageSettings("PageHeight") = altezza
        Catch ex As Exception
            logger.ScriviLog($"ApplicaTipoPagina: {ex.Message}")
        End Try
    End Sub

    Private Shared Function TrovaVoceArchivio(archive As ZipArchive, nomeEsatto As String, prefisso As String) As ZipArchiveEntry
        Try
            Dim entryEsatta As ZipArchiveEntry = archive.GetEntry(nomeEsatto)
            If entryEsatta IsNot Nothing Then Return entryEsatta

            For Each entry As ZipArchiveEntry In archive.Entries
                If entry.FullName.StartsWith(prefisso, StringComparison.OrdinalIgnoreCase) AndAlso entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) Then
                    Return entry
                End If
            Next
            Return Nothing
        Catch ex As Exception
            logger.ScriviLog("TrovaVoceArchivio: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function TrovaTuttiIParagrafi(elements As List(Of DocElement)) As List(Of DocParagraph)
        Dim risultato As New List(Of DocParagraph)
        Try
            For Each el In elements
                If TypeOf el Is DocParagraph Then
                    risultato.Add(DirectCast(el, DocParagraph))
                ElseIf TypeOf el Is DocTable Then
                    Dim tbl As DocTable = DirectCast(el, DocTable)
                    If tbl.Rows Is Nothing Then Continue For
                    For Each riga In tbl.Rows
                        If riga.Cells Is Nothing Then Continue For
                        For Each cella In riga.Cells
                            If cella.Paragraphs Is Nothing Then Continue For
                            For Each paraCella In cella.Paragraphs
                                risultato.Add(paraCella)
                            Next
                        Next
                    Next
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("TrovaTuttiIParagrafi: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function TestoSembraIndirizzoWeb(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim t As String = testo.Trim()
            If t.Contains(" ") Then Return False
            If t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then Return True
            If t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then Return True
            If t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then Return True
            If t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) Then Return True
            If t.Contains("@") AndAlso t.Contains(".") AndAlso Not t.EndsWith(".") Then Return True
            Dim estensioni() As String = {".it", ".com", ".org", ".net", ".eu", ".info", ".gov", ".edu"}
            For Each est As String In estensioni
                If t.EndsWith(est, StringComparison.OrdinalIgnoreCase) OrElse t.Contains(est & "/") Then Return True
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog("TestoSembraIndirizzoWeb: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function NormalizzaIndirizzoWeb(testo As String) As String
        Try
            Dim t As String = If(testo, "").Trim()
            If String.IsNullOrEmpty(t) Then Return ""
            If t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then Return t
            If t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then Return t
            If t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) Then Return t
            If t.StartsWith("#") Then Return t
            If t.Contains("@") AndAlso Not t.Contains("/") Then Return "mailto:" & t
            Return "https://" & t
        Catch ex As Exception
            logger.ScriviLog("NormalizzaIndirizzoWeb: " & ex.Message)
            Return testo
        End Try
    End Function

    Public Shared Sub RiparaCollegamentiIpertestuali(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            Dim impostazioni As New WGImpostazioniComuni()
            Dim eliminaSeIrrecuperabile As Boolean = impostazioni.EliminaNotaConParola

            For Each p As DocParagraph In TrovaTuttiIParagrafi(elements)
                If p.Runs Is Nothing Then Continue For
                For Each r As DocRun In p.Runs
                    If r Is Nothing OrElse r.IsImage Then Continue For
                    If r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then Continue For
                    If Not String.IsNullOrEmpty(p.BookmarkId) Then Continue For

                    If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso Not r.LinkUrl.StartsWith("NOTA_") Then
                        Dim urlNormalizzato As String = NormalizzaIndirizzoWeb(r.LinkUrl)
                        If urlNormalizzato <> r.LinkUrl Then
                            r.LinkUrl = urlNormalizzato
                        End If
                        If Not r.IsHyperlink Then
                            r.IsHyperlink = True
                        End If
                        If String.IsNullOrEmpty(r.ColorHex) OrElse r.ColorHex = "Nero" Then r.ColorHex = "Blu"
                        If Not r.Underline Then r.Underline = True
                        Continue For
                    End If

                    If r.IsHyperlink AndAlso String.IsNullOrEmpty(r.LinkUrl) Then
                        If TestoSembraIndirizzoWeb(r.Text) Then
                            r.LinkUrl = NormalizzaIndirizzoWeb(r.Text)
                        ElseIf eliminaSeIrrecuperabile Then
                            r.Text = ""
                            r.IsHyperlink = False
                            r.Underline = False
                            r.ColorHex = "Nero"
                        Else
                            r.IsHyperlink = False
                        End If
                        Continue For
                    End If

                    If Not r.IsHyperlink AndAlso String.IsNullOrEmpty(r.LinkUrl) AndAlso r.Underline AndAlso r.ColorHex = "Blu" AndAlso TestoSembraIndirizzoWeb(r.Text) Then
                        r.LinkUrl = NormalizzaIndirizzoWeb(r.Text)
                        r.IsHyperlink = True
                    End If
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog("RiparaCollegamentiIpertestuali: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub RiparaStruttureTabelle(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            For Each el In elements
                If Not TypeOf el Is DocTable Then Continue For
                Dim tbl As DocTable = DirectCast(el, DocTable)
                If tbl.Rows Is Nothing OrElse tbl.Rows.Count = 0 Then Continue For

                Dim numeroColonneMassimo As Integer = 0
                For Each riga In tbl.Rows
                    If riga.Cells IsNot Nothing AndAlso riga.Cells.Count > numeroColonneMassimo Then numeroColonneMassimo = riga.Cells.Count
                Next
                If numeroColonneMassimo = 0 Then Continue For

                For Each riga In tbl.Rows
                    If riga.Cells Is Nothing Then Continue For
                    While riga.Cells.Count < numeroColonneMassimo
                        Dim cellaNuova As New DocTableCell()
                        cellaNuova.Paragraphs = New List(Of DocParagraph)()
                        cellaNuova.Paragraphs.Add(New DocParagraph())
                        riga.Cells.Add(cellaNuova)
                    End While
                    For Each cella In riga.Cells
                        If cella.Paragraphs Is Nothing Then
                            cella.Paragraphs = New List(Of DocParagraph)()
                        End If
                        If cella.Paragraphs.Count = 0 Then
                            cella.Paragraphs.Add(New DocParagraph())
                        End If
                    Next
                Next
                tbl.CachedHeight = -1
            Next
        Catch ex As Exception
            logger.ScriviLog("RiparaStruttureTabelle: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub RiparaParagrafiVuoti(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            For Each p As DocParagraph In TrovaTuttiIParagrafi(elements)
                If p.Runs Is Nothing Then Continue For
                If p.Runs.Count = 0 Then
                    p.Runs.Add(New DocRun())
                End If
                For Each r As DocRun In p.Runs
                    If r IsNot Nothing AndAlso r.Text Is Nothing Then r.Text = ""
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog("RiparaParagrafiVuoti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared ReadOnly _estensioniWebDaNonSpezzare As String() = {"it", "com", "org", "net", "eu", "info", "biz", "co", "gov", "edu"}

    Public Shared Function RicostruisciACapoNelTesto(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return testo
            If testo.Contains(ControlChars.Lf) Then Return testo
            If testo.Length <= 150 Then Return testo

            Dim sb As New StringBuilder(testo.Length + 16)
            For i As Integer = 0 To testo.Length - 1
                Dim c As Char = testo(i)
                sb.Append(c)

                If (c = "."c OrElse c = "!"c OrElse c = "?"c) AndAlso i < testo.Length - 1 Then
                    Dim successivo As Char = testo(i + 1)
                    If Char.IsLetter(successivo) Then
                        Dim fineParola As Integer = i + 1
                        While fineParola < testo.Length AndAlso Char.IsLetter(testo(fineParola))
                            fineParola += 1
                        End While
                        Dim parolaSuccessiva As String = testo.Substring(i + 1, fineParola - (i + 1)).ToLowerInvariant()

                        Dim eEstensioneWeb As Boolean = False
                        If c = "."c Then
                            For Each est As String In _estensioniWebDaNonSpezzare
                                If parolaSuccessiva = est Then
                                    eEstensioneWeb = True
                                    Exit For
                                End If
                            Next
                        End If

                        If Not eEstensioneWeb Then sb.Append(ControlChars.Lf)
                    End If
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("RicostruisciACapoNelTesto: " & ex.Message)
            Return testo
        End Try
    End Function

    Public Shared Sub RiparaACapoNelleNote(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            For Each el In elements
                If Not TypeOf el Is DocParagraph Then Continue For
                Dim p As DocParagraph = DirectCast(el, DocParagraph)
                If String.IsNullOrEmpty(p.BookmarkId) Then Continue For
                If Not (p.BookmarkId.StartsWith("NOTA_PIE_") OrElse p.BookmarkId.StartsWith("NOTA_FINE_")) Then Continue For
                If p.Runs Is Nothing Then Continue For

                Dim testoCompleto As String = p.GetText()
                If testoCompleto.Contains(ControlChars.Lf) Then Continue For
                If testoCompleto.Length <= 150 Then Continue For

                For Each r As DocRun In p.Runs
                    If r Is Nothing OrElse r.IsImage OrElse r.IsSuperscript Then Continue For
                    If String.IsNullOrEmpty(r.Text) OrElse r.Text.Length <= 150 Then Continue For

                    Dim testoRiparato As String = RicostruisciACapoNelTesto(r.Text)
                    If testoRiparato <> r.Text Then
                        r.Text = testoRiparato
                        p.CachedHeight = -1
                    End If
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog("RiparaACapoNelleNote: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub RiparaDocumento(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            RiparaParagrafiVuoti(elements)
            RiparaStruttureTabelle(elements)
            RiparaCollegamentiIpertestuali(elements)
            RipariaCollegamentiNoteOrfani(elements)
            RiparaACapoNelleNote(elements)
            RinumeraElenchiNumerati(elements)
            AssicuraParagrafiAttornoTabelle(elements)
        Catch ex As Exception
            logger.ScriviLog("RiparaDocumento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub RipariaCollegamentiNoteOrfani(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return

            Dim impostazioni As New WGImpostazioniComuni()
            Dim eliminaSeIrrecuperabile As Boolean = impostazioni.EliminaNotaConParola

            Dim paragrafiNota As New Dictionary(Of String, DocParagraph)(StringComparer.OrdinalIgnoreCase)
            For Each el In elements
                If TypeOf el Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(el, DocParagraph)
                    If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso (p.BookmarkId.StartsWith("NOTA_PIE_") OrElse p.BookmarkId.StartsWith("NOTA_FINE_")) Then
                        paragrafiNota(p.BookmarkId) = p
                    End If
                End If
            Next

            Dim tuttiParagrafi As List(Of DocParagraph) = TrovaTuttiIParagrafi(elements)
            Dim bookmarkReferenziati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each p As DocParagraph In tuttiParagrafi
                If Not String.IsNullOrEmpty(p.BookmarkId) Then Continue For
                If p.Runs Is Nothing Then Continue For
                For Each r As DocRun In p.Runs
                    If r Is Nothing OrElse Not r.IsSuperscript OrElse String.IsNullOrEmpty(r.LinkUrl) Then Continue For
                    If Not (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then Continue For

                    If paragrafiNota.ContainsKey(r.LinkUrl) Then
                        bookmarkReferenziati.Add(r.LinkUrl)
                        Continue For
                    End If

                    Dim prefissoAlt As String = If(r.LinkUrl.StartsWith("NOTA_PIE_"), "NOTA_FINE_", "NOTA_PIE_")
                    Dim numeroTesto As String = r.LinkUrl.Substring(r.LinkUrl.LastIndexOf("_"c) + 1)
                    Dim chiaveAlternativa As String = prefissoAlt & numeroTesto

                    If paragrafiNota.ContainsKey(chiaveAlternativa) Then
                        r.LinkUrl = chiaveAlternativa
                        r.IsHyperlink = True
                        bookmarkReferenziati.Add(chiaveAlternativa)
                    ElseIf eliminaSeIrrecuperabile Then
                        r.Text = ""
                        r.LinkUrl = ""
                        r.IsHyperlink = False
                    Else
                        r.LinkUrl = ""
                        r.IsHyperlink = False
                    End If
                Next
            Next

            For Each kvp In paragrafiNota
                If bookmarkReferenziati.Contains(kvp.Key) Then Continue For

                Dim numeroNotaTesto As String = kvp.Key.Substring(kvp.Key.LastIndexOf("_"c) + 1)
                Dim ricostruito As Boolean = False

                For Each p As DocParagraph In tuttiParagrafi
                    If ricostruito Then Exit For
                    If Not String.IsNullOrEmpty(p.BookmarkId) Then Continue For
                    If p.Runs Is Nothing Then Continue For

                    For i As Integer = 0 To p.Runs.Count - 1
                        Dim r As DocRun = p.Runs(i)
                        If r Is Nothing OrElse r.IsImage OrElse String.IsNullOrEmpty(r.Text) Then Continue For
                        If r.IsSuperscript Then Continue For
                        If Not r.Text.EndsWith(numeroNotaTesto) Then Continue For
                        If r.Text.Length <= numeroNotaTesto.Length Then Continue For

                        Dim carattereProbabile As Char = r.Text(r.Text.Length - numeroNotaTesto.Length - 1)
                        If Not Char.IsLetter(carattereProbabile) Then Continue For

                        r.Text = r.Text.Substring(0, r.Text.Length - numeroNotaTesto.Length)

                        Dim runRiferimento As New DocRun()
                        runRiferimento.Text = numeroNotaTesto
                        runRiferimento.IsSuperscript = True
                        runRiferimento.Bold = True
                        runRiferimento.FontSize = 8
                        runRiferimento.LinkUrl = kvp.Key
                        runRiferimento.IsHyperlink = True
                        p.Runs.Insert(i + 1, runRiferimento)
                        p.CachedHeight = -1

                        ricostruito = True
                        Exit For
                    Next
                Next

                If Not ricostruito Then
                    If eliminaSeIrrecuperabile Then
                        elements.Remove(kvp.Value)
                    Else
                    End If
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("RipariaCollegamentiNoteOrfani: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub AssicuraParagrafiAttornoTabelle(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            Dim i As Integer = 0
            While i < elements.Count
                If TypeOf elements(i) Is DocTable Then
                    Dim serveDopo As Boolean = True
                    If i + 1 < elements.Count Then
                        Dim elDopo As DocElement = elements(i + 1)
                        If TypeOf elDopo Is DocParagraph AndAlso String.IsNullOrEmpty(DirectCast(elDopo, DocParagraph).GetText()) Then serveDopo = False
                    End If
                    If serveDopo Then
                        elements.Insert(i + 1, New DocParagraph())
                    End If

                    Dim servePrima As Boolean = True
                    If i > 0 Then
                        Dim elPrima As DocElement = elements(i - 1)
                        If TypeOf elPrima Is DocParagraph AndAlso String.IsNullOrEmpty(DirectCast(elPrima, DocParagraph).GetText()) Then servePrima = False
                    Else
                        servePrima = False
                    End If
                    If servePrima Then
                        elements.Insert(i, New DocParagraph())
                        i += 1
                    End If
                End If
                i += 1
            End While
        Catch ex As Exception
            logger.ScriviLog("AssicuraParagrafiAttornoTabelle: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Sub RinumeraElenchiNumerati(elements As List(Of DocElement))
        Try
            If elements Is Nothing Then Return
            Dim contatore As Integer = 0
            For i As Integer = 0 To elements.Count - 1
                Dim p As DocParagraph = TryCast(elements(i), DocParagraph)
                If p IsNot Nothing AndAlso p.IsListItem AndAlso Not String.IsNullOrEmpty(p.ListNumValue) Then
                    contatore += 1
                    p.ListNumValue = contatore.ToString()
                Else
                    contatore = 0
                    Dim t As DocTable = TryCast(elements(i), DocTable)
                    If t IsNot Nothing Then
                        For Each row As DocTableRow In t.Rows
                            For Each cell As DocTableCell In row.Cells
                                Dim contatoreCella As Integer = 0
                                For Each pc As DocParagraph In cell.Paragraphs
                                    If pc.IsListItem AndAlso Not String.IsNullOrEmpty(pc.ListNumValue) Then
                                        contatoreCella += 1
                                        pc.ListNumValue = contatoreCella.ToString()
                                    Else
                                        contatoreCella = 0
                                    End If
                                Next
                            Next
                        Next
                    End If
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("RinumeraElenchiNumerati: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function CaricaDocumento(filePath As String, stopCheck As Func(Of Boolean), Optional pageSettings As Dictionary(Of String, Integer) = Nothing) As List(Of DocElement)
        Dim state As New WGDocumentState()
        If pageSettings IsNot Nothing Then state.PageSettings = pageSettings
        CaricaDocumentoInStato(filePath, stopCheck, state)
        If pageSettings IsNot Nothing Then
            For Each kvp In state.PageSettings
                pageSettings(kvp.Key) = kvp.Value
            Next
        End If
        Return state.Elements
    End Function

    Public Shared Function CaricaDocumentoCompleto(filePath As String, stopCheck As Func(Of Boolean), stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), Optional pageSettings As Dictionary(Of String, Integer) = Nothing) As List(Of DocElement)
        Dim state As New WGDocumentState()
        If pageSettings IsNot Nothing Then state.PageSettings = pageSettings
        If stiliTitolo IsNot Nothing Then state.StiliTitolo = stiliTitolo
        If commentiPerParagrafo IsNot Nothing Then state.CommentiPerParagrafo = commentiPerParagrafo
        CaricaDocumentoInStato(filePath, stopCheck, state)
        If pageSettings IsNot Nothing Then
            For Each kvp In state.PageSettings
                pageSettings(kvp.Key) = kvp.Value
            Next
        End If
        If stiliTitolo IsNot Nothing Then
            For Each kvp In state.StiliTitolo
                stiliTitolo(kvp.Key) = kvp.Value
            Next
        End If
        If commentiPerParagrafo IsNot Nothing Then
            For Each kvp In state.CommentiPerParagrafo
                commentiPerParagrafo(kvp.Key) = kvp.Value
            Next
        End If
        Return state.Elements
    End Function

    Public Shared Function CaricaStato(filePath As String, stopCheck As Func(Of Boolean), Optional pageSettings As Dictionary(Of String, Integer) = Nothing) As WGDocumentState
        Dim state As New WGDocumentState()
        If pageSettings IsNot Nothing Then state.PageSettings = pageSettings
        CaricaDocumentoInStato(filePath, stopCheck, state)
        Return state
    End Function

    Private Shared Sub CaricaDocumentoInStato(filePath As String, stopCheck As Func(Of Boolean), state As WGDocumentState)
        Try
            If String.IsNullOrWhiteSpace(filePath) Then Throw New ArgumentException("Il percorso del file non può essere vuoto.")
            If Not File.Exists(filePath) Then Throw New FileNotFoundException("Il file non esiste.", filePath)
            Dim ext As String = Path.GetExtension(filePath).ToLowerInvariant()
            If ext <> ".docx" AndAlso ext <> ".dotx" AndAlso ext <> ".docm" AndAlso ext <> ".odt" Then
                Throw New NotSupportedException("Formato file non supportato. Sono accettati solo file .docx, .dotx, .docm e .odt.")
            End If
            Dim fi As New FileInfo(filePath)
            If fi.Length > 500 * 1024 * 1024 Then
                Throw New IOException("Il file supera la dimensione massima consentita di 500 MB.")
            End If

            WGWordLayoutMotore.WGTemaDocumento.Ripristina()
            ImpostaPaletteTemaDefault()

            If ext = ".odt" Then
                CaricaDocumentoOdtInStato(filePath, stopCheck, state)
                Return
            End If

            state.SourcePath = filePath
            Dim mappaCommenti As New Dictionary(Of Integer, String)
            Dim mappaFootnotes As New Dictionary(Of Integer, String)()
            Dim mappaEndnotes As New Dictionary(Of Integer, String)()
            Dim mappaHeaderRels As New Dictionary(Of String, String)()
            Dim mappaFooterRels As New Dictionary(Of String, String)()

            Dim xmlSettings As New System.Xml.XmlReaderSettings()
            xmlSettings.DtdProcessing = System.Xml.DtdProcessing.Prohibit
            xmlSettings.MaxCharactersInDocument = 50_000_000L
            xmlSettings.MaxCharactersFromEntities = 1_000_000L

            Using fs As FileStream = New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using archive As ZipArchive = New ZipArchive(fs, ZipArchiveMode.Read)

                    Try
                        Dim entryTema As ZipArchiveEntry = TrovaVoceArchivio(archive, "word/theme/theme1.xml", "word/theme")
                        If entryTema IsNot Nothing Then
                            Using stTema As Stream = entryTema.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stTema, xmlSettings)
                                    Dim xTema As XDocument = XDocument.Load(xr)
                                    WGWordLayoutMotore.WGTemaDocumento.CaricaDaXmlTema(xTema.ToString())
                                    _paletteTemaCorrente = PaletteTemaDaXmlOoxml(xTema)
                                End Using
                            End Using
                        End If
                    Catch exTema As Exception
                        logger.ScriviLog($"CaricaDocumentoInStato tema: {exTema.Message}{Environment.NewLine}{exTema.StackTrace}")
                    End Try

                    Dim entryCommenti As ZipArchiveEntry = TrovaVoceArchivio(archive, "word/comments.xml", "word/comments")
                    If entryCommenti IsNot Nothing Then
                        Try
                            Using stCmt As Stream = entryCommenti.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stCmt, xmlSettings)
                                    Dim xCmt As XDocument = XDocument.Load(xr)
                                    For Each cmt As XElement In xCmt.Descendants(NsW + "comment")
                                        Dim idAttr = cmt.Attribute(NsW + "id")
                                        If idAttr IsNot Nothing Then
                                            Dim idVal As Integer
                                            If Integer.TryParse(idAttr.Value, idVal) Then
                                                Dim sbTesto As New StringBuilder()
                                                For Each tEl As XElement In cmt.Descendants(NsW + "t")
                                                    sbTesto.Append(tEl.Value)
                                                Next
                                                mappaCommenti(idVal) = sbTesto.ToString()
                                            End If
                                        End If
                                    Next
                                End Using
                            End Using
                        Catch exCmt As Exception
                            logger.ScriviLog($"CaricaDocumentoInStato commenti: {exCmt.Message}{Environment.NewLine}{exCmt.StackTrace}")
                        End Try
                    End If

                    Try
                        Dim relsEntry As ZipArchiveEntry = archive.GetEntry("word/_rels/document.xml.rels")
                        If relsEntry IsNot Nothing Then
                            Using relsStream As Stream = relsEntry.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(relsStream, xmlSettings)
                                    Dim xRels As XDocument = XDocument.Load(xr)
                                    Dim nsRels As XNamespace = "http://schemas.openxmlformats.org/package/2006/relationships"
                                    For Each rel As XElement In xRels.Descendants(nsRels + "Relationship")
                                        Try
                                            Dim idAttr = rel.Attribute("Id")
                                            Dim targetAttr = rel.Attribute("Target")
                                            Dim typeAttr = rel.Attribute("Type")
                                            If idAttr Is Nothing OrElse targetAttr Is Nothing OrElse typeAttr Is Nothing Then Continue For

                                            Dim tipoLower As String = typeAttr.Value.ToLower()
                                            Dim eImmagine As Boolean = tipoLower.Contains("image") OrElse tipoLower.Contains("hdphoto")
                                            Dim eOle As Boolean = tipoLower.Contains("oleobject")
                                            Dim eHeader As Boolean = tipoLower.Contains("relationships/header")
                                            Dim eFooter As Boolean = tipoLower.Contains("relationships/footer")

                                            If eHeader Then
                                                Dim targetHeader As String = targetAttr.Value.Replace("\", "/").TrimStart("/"c)
                                                If Not targetHeader.StartsWith("word/", StringComparison.OrdinalIgnoreCase) Then targetHeader = "word/" & targetHeader
                                                mappaHeaderRels(idAttr.Value) = targetHeader
                                                Continue For
                                            End If
                                            If eFooter Then
                                                Dim targetFooter As String = targetAttr.Value.Replace("\", "/").TrimStart("/"c)
                                                If Not targetFooter.StartsWith("word/", StringComparison.OrdinalIgnoreCase) Then targetFooter = "word/" & targetFooter
                                                mappaFooterRels(idAttr.Value) = targetFooter
                                                Continue For
                                            End If

                                            If Not eImmagine AndAlso Not eOle Then Continue For

                                            Dim target As String = targetAttr.Value.Replace("\", "/")

                                            If target.StartsWith("../") Then
                                                target = "word/" & target.Substring(3)
                                            ElseIf target.StartsWith("/") Then
                                                target = target.TrimStart("/"c)
                                            ElseIf Not target.Contains("/") Then
                                                target = "word/media/" & target
                                            ElseIf Not target.StartsWith("word/") Then
                                                target = "word/" & target
                                            End If

                                            If target.Contains("..") OrElse
                                           target.Contains(":") OrElse
                                           target.Contains("\\") OrElse
                                           Not target.StartsWith("word/", StringComparison.OrdinalIgnoreCase) Then
                                                Continue For
                                            End If

                                            state.MappaRelazioni(idAttr.Value) = target
                                        Catch exRel As Exception
                                            logger.ScriviLog($"CaricaDocumentoInStato rel singola: {exRel.Message}")
                                        End Try
                                    Next
                                End Using
                            End Using
                        End If
                    Catch exRels As Exception
                        logger.ScriviLog($"CaricaDocumentoInStato rels: {exRels.Message}{Environment.NewLine}{exRels.StackTrace}")
                    End Try

                    Dim maxMediaBytes As Long = 50 * 1024 * 1024
                    For Each kvp In state.MappaRelazioni
                        Try
                            Dim mediaEntry As ZipArchiveEntry = archive.GetEntry(kvp.Value)
                            If mediaEntry IsNot Nothing Then
                                If mediaEntry.Length > maxMediaBytes Then
                                    Continue For
                                End If
                                Using ms As New MemoryStream()
                                    Using mediaStream As Stream = mediaEntry.Open()
                                        mediaStream.CopyTo(ms)
                                    End Using
                                    Dim bytes As Byte() = ms.ToArray()
                                    If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                                        state.ImmaginiMedia(kvp.Key) = bytes
                                    End If
                                End Using
                            Else
                            End If
                        Catch exMedia As Exception
                            logger.ScriviLog($"CaricaDocumentoInStato: errore caricamento media rId='{kvp.Key}'. Errore: {exMedia.Message}{Environment.NewLine}{exMedia.StackTrace}")
                        End Try
                    Next

                    Try
                        Dim fnEntry As ZipArchiveEntry = TrovaVoceArchivio(archive, "word/footnotes.xml", "word/footnotes")
                        If fnEntry IsNot Nothing Then
                            Using fnStream As Stream = fnEntry.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(fnStream, xmlSettings)
                                    Dim xFn As XDocument = XDocument.Load(xr)
                                    For Each fn As XElement In xFn.Descendants(NsW + "footnote")
                                        Dim idAttr = fn.Attribute(NsW + "id")
                                        Dim typeAttr = fn.Attribute(NsW + "type")
                                        If idAttr Is Nothing Then Continue For
                                        If typeAttr IsNot Nothing AndAlso (typeAttr.Value = "separator" OrElse typeAttr.Value = "continuationSeparator" OrElse typeAttr.Value = "continuationNotice") Then Continue For
                                        Dim idVal As Integer
                                        If Integer.TryParse(idAttr.Value, idVal) AndAlso idVal > 0 Then
                                            Dim sbFn As New StringBuilder()
                                            For Each nodoFn As XElement In fn.Descendants()
                                                If nodoFn.Name = NsW + "t" Then
                                                    sbFn.Append(nodoFn.Value)
                                                ElseIf nodoFn.Name = NsW + "br" Then
                                                    sbFn.Append(ControlChars.Lf)
                                                End If
                                            Next
                                            Dim testoFn As String = sbFn.ToString().Trim()
                                            Dim numPulito As String = Regex.Replace(testoFn, "^\d+\s*", "").Trim()
                                            mappaFootnotes(idVal) = If(String.IsNullOrEmpty(numPulito), testoFn, numPulito)
                                        End If
                                    Next
                                End Using
                            End Using
                        End If
                    Catch exFn As Exception
                        logger.ScriviLog($"CaricaDocumentoInStato footnotes: {exFn.Message}")
                    End Try

                    Try
                        Dim enEntry As ZipArchiveEntry = TrovaVoceArchivio(archive, "word/endnotes.xml", "word/endnotes")
                        If enEntry IsNot Nothing Then
                            Using enStream As Stream = enEntry.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(enStream, xmlSettings)
                                    Dim xEn As XDocument = XDocument.Load(xr)
                                    For Each en As XElement In xEn.Descendants(NsW + "endnote")
                                        Dim idAttr = en.Attribute(NsW + "id")
                                        Dim typeAttr = en.Attribute(NsW + "type")
                                        If idAttr Is Nothing Then Continue For
                                        If typeAttr IsNot Nothing AndAlso (typeAttr.Value = "separator" OrElse typeAttr.Value = "continuationSeparator" OrElse typeAttr.Value = "continuationNotice") Then Continue For
                                        Dim idVal As Integer
                                        If Integer.TryParse(idAttr.Value, idVal) AndAlso idVal > 0 Then
                                            Dim sbEn As New StringBuilder()
                                            For Each tEl As XElement In en.Descendants(NsW + "t")
                                                sbEn.Append(tEl.Value)
                                            Next
                                            Dim testoEn As String = sbEn.ToString().Trim()
                                            Dim numPulito As String = Regex.Replace(testoEn, "^\d+\s*", "").Trim()
                                            mappaEndnotes(idVal) = If(String.IsNullOrEmpty(numPulito), testoEn, numPulito)
                                        End If
                                    Next
                                End Using
                            End Using
                        End If
                    Catch exEn As Exception
                        logger.ScriviLog($"CaricaDocumentoInStato endnotes: {exEn.Message}")
                    End Try

                    Dim documentEntry As ZipArchiveEntry = archive.GetEntry("word/document.xml")
                    If documentEntry IsNot Nothing Then
                        Using stream As Stream = documentEntry.Open()
                            Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stream, xmlSettings)
                                Dim xdoc As XDocument = XDocument.Load(xr)
                                Dim body As XElement = Nothing
                                For Each bodyEl As XElement In xdoc.Descendants(NsW + "body")
                                    body = bodyEl
                                    Exit For
                                Next
                                If body IsNot Nothing Then
                                    Dim sectPr As XElement = Nothing
                                    For Each sectEl As XElement In body.Elements(NsW + "sectPr")
                                        sectPr = sectEl
                                        Exit For
                                    Next
                                    If sectPr IsNot Nothing Then
                                        Try
                                            Dim pgMar As XElement = sectPr.Element(NsW + "pgMar")
                                            If pgMar IsNot Nothing Then
                                                Dim v As Integer = 0
                                                If pgMar.Attribute(NsW + "top") IsNot Nothing AndAlso Integer.TryParse(pgMar.Attribute(NsW + "top").Value, v) Then state.PageSettings("MarginTop") = v
                                                If pgMar.Attribute(NsW + "bottom") IsNot Nothing AndAlso Integer.TryParse(pgMar.Attribute(NsW + "bottom").Value, v) Then state.PageSettings("MarginBottom") = v
                                                If pgMar.Attribute(NsW + "left") IsNot Nothing AndAlso Integer.TryParse(pgMar.Attribute(NsW + "left").Value, v) Then state.PageSettings("MarginLeft") = v
                                                If pgMar.Attribute(NsW + "right") IsNot Nothing AndAlso Integer.TryParse(pgMar.Attribute(NsW + "right").Value, v) Then state.PageSettings("MarginRight") = v
                                            End If
                                            Dim pgSz As XElement = sectPr.Element(NsW + "pgSz")
                                            If pgSz IsNot Nothing Then
                                                Dim v As Integer = 0
                                                If pgSz.Attribute(NsW + "w") IsNot Nothing AndAlso Integer.TryParse(pgSz.Attribute(NsW + "w").Value, v) Then state.PageSettings("PageWidth") = v
                                                If pgSz.Attribute(NsW + "h") IsNot Nothing AndAlso Integer.TryParse(pgSz.Attribute(NsW + "h").Value, v) Then state.PageSettings("PageHeight") = v
                                                Dim orientAttr = pgSz.Attribute(NsW + "orient")
                                                state.PageSettings("Orientation") = If(orientAttr IsNot Nothing AndAlso orientAttr.Value.ToLower() = "landscape", 2, 1)
                                            End If
                                        Catch exSect As Exception
                                            logger.ScriviLog($"CaricaDocumentoInStato sectPr: {exSect.Message}")
                                        End Try

                                        Try
                                            For Each hdrRefEl As XElement In sectPr.Elements(NsW + "headerReference")
                                                Dim tipoAttr = hdrRefEl.Attribute(NsW + "type")
                                                Dim idAttrHdr = hdrRefEl.Attribute(NsR + "id")
                                                If idAttrHdr IsNot Nothing AndAlso (tipoAttr Is Nothing OrElse tipoAttr.Value = "default") Then
                                                    If mappaHeaderRels.ContainsKey(idAttrHdr.Value) Then
                                                        Dim headerEntry As ZipArchiveEntry = archive.GetEntry(mappaHeaderRels(idAttrHdr.Value))
                                                        If headerEntry IsNot Nothing Then
                                                            Using stHdr As Stream = headerEntry.Open()
                                                                Using xrHdr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stHdr, xmlSettings)
                                                                    Dim xHdr As XDocument = XDocument.Load(xrHdr)
                                                                    Dim sbHdr As New StringBuilder()
                                                                    For Each tEl As XElement In xHdr.Descendants(NsW + "t")
                                                                        sbHdr.Append(tEl.Value)
                                                                    Next
                                                                    state.HeaderText = sbHdr.ToString()
                                                                End Using
                                                            End Using
                                                        End If
                                                    End If
                                                    Exit For
                                                End If
                                            Next
                                            For Each ftrRefEl As XElement In sectPr.Elements(NsW + "footerReference")
                                                Dim tipoAttr = ftrRefEl.Attribute(NsW + "type")
                                                Dim idAttrFtr = ftrRefEl.Attribute(NsR + "id")
                                                If idAttrFtr IsNot Nothing AndAlso (tipoAttr Is Nothing OrElse tipoAttr.Value = "default") Then
                                                    If mappaFooterRels.ContainsKey(idAttrFtr.Value) Then
                                                        Dim footerEntry As ZipArchiveEntry = archive.GetEntry(mappaFooterRels(idAttrFtr.Value))
                                                        If footerEntry IsNot Nothing Then
                                                            Using stFtr As Stream = footerEntry.Open()
                                                                Using xrFtr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stFtr, xmlSettings)
                                                                    Dim xFtr As XDocument = XDocument.Load(xrFtr)
                                                                    Dim sbFtr As New StringBuilder()
                                                                    For Each tEl As XElement In xFtr.Descendants(NsW + "t")
                                                                        sbFtr.Append(tEl.Value)
                                                                    Next
                                                                    state.FooterText = sbFtr.ToString()
                                                                End Using
                                                            End Using
                                                        End If
                                                    End If
                                                    Exit For
                                                End If
                                            Next
                                        Catch exHdrFtr As Exception
                                            logger.ScriviLog($"CaricaDocumentoInStato intestazione/piè di pagina: {exHdrFtr.Message}{Environment.NewLine}{exHdrFtr.StackTrace}")
                                        End Try
                                    End If

                                    For Each child As XElement In body.Elements()
                                        Try
                                            If stopCheck IsNot Nothing AndAlso stopCheck() Then Return
                                            If child.Name = NsW + "p" Then
                                                state.Elements.Add(ParseParagraphConMetadati(child, state.StiliTitolo, state.CommentiPerParagrafo, mappaCommenti, state.ImmaginiMedia, state.MappaRelazioni, mappaFootnotes, mappaEndnotes))
                                            ElseIf child.Name = NsW + "tbl" Then
                                                state.Elements.Add(ParseTable(child, state.ImmaginiMedia, state.MappaRelazioni))
                                            End If
                                        Catch exChild As Exception
                                            logger.ScriviLog($"CaricaDocumentoInStato child: {exChild.Message}")
                                        End Try
                                    Next
                                End If

                                If mappaFootnotes.Count > 0 Then
                                    Try
                                        Dim chiaviFn As New List(Of Integer)(mappaFootnotes.Keys)
                                        chiaviFn.Sort()
                                        For Each kvpFnKey As Integer In chiaviFn
                                            Dim notaPara As New DocParagraph()
                                            notaPara.BookmarkId = "NOTA_PIE_" & kvpFnKey
                                            notaPara.LineSpacing = 1.0F
                                            Dim runNum As New DocRun() With {.Text = kvpFnKey.ToString(), .IsSuperscript = True, .Bold = True, .FontSize = 8, .LinkUrl = "NOTA_PIE_" & kvpFnKey}
                                            notaPara.Runs.Add(runNum)
                                            notaPara.Runs.Add(New DocRun() With {.Text = " "})
                                            notaPara.Runs.Add(New DocRun() With {.Text = RicostruisciACapoNelTesto(mappaFootnotes(kvpFnKey)), .FontSize = 9})
                                            state.Elements.Add(notaPara)
                                        Next
                                    Catch exFnAdd As Exception
                                        logger.ScriviLog($"CaricaDocumentoInStato footnote paragraphs: {exFnAdd.Message}")
                                    End Try
                                End If

                                If mappaEndnotes.Count > 0 Then
                                    Try
                                        Dim chiaveEn As New List(Of Integer)(mappaEndnotes.Keys)
                                        chiaveEn.Sort()
                                        For Each kvpEnKey As Integer In chiaveEn
                                            Dim notaPara As New DocParagraph()
                                            notaPara.BookmarkId = "NOTA_FINE_" & kvpEnKey
                                            notaPara.LineSpacing = 1.0F
                                            Dim runNum As New DocRun() With {.Text = kvpEnKey.ToString(), .IsSuperscript = True, .Bold = True, .FontSize = 8, .LinkUrl = "NOTA_FINE_" & kvpEnKey}
                                            notaPara.Runs.Add(runNum)
                                            notaPara.Runs.Add(New DocRun() With {.Text = " "})
                                            notaPara.Runs.Add(New DocRun() With {.Text = RicostruisciACapoNelTesto(mappaEndnotes(kvpEnKey)), .FontSize = 9})
                                            state.Elements.Add(notaPara)
                                        Next
                                    Catch exEnAdd As Exception
                                        logger.ScriviLog($"CaricaDocumentoInStato endnote paragraphs: {exEnAdd.Message}")
                                    End Try
                                End If
                            End Using
                        End Using
                    End If
                End Using
            End Using
        Catch ex As UnauthorizedAccessException
            logger.ScriviLog($"CaricaDocumentoInStato UnauthorizedAccess: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il file non è accessibile per la lettura a causa delle autorizzazioni.")
        Catch ex As DirectoryNotFoundException
            logger.ScriviLog($"CaricaDocumentoInStato DirectoryNotFound: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il percorso del file non è valido o la cartella non esiste.")
        Catch ex As FileNotFoundException
            logger.ScriviLog($"CaricaDocumentoInStato FileNotFound: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il file da caricare non è stato trovato.")
        Catch ex As IOException
            logger.ScriviLog($"CaricaDocumentoInStato IOException: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Errore di input o output durante la lettura. Il file potrebbe essere in uso.")
        Catch ex As Exception
            logger.ScriviLog($"CaricaDocumentoInStato Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try

        RiparaDocumento(state.Elements)
    End Sub

    Private Shared ReadOnly NsOdfOffice As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:office:1.0"
    Private Shared ReadOnly NsOdfText As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:text:1.0"
    Private Shared ReadOnly NsOdfTable As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:table:1.0"
    Private Shared ReadOnly NsOdfStyle As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:style:1.0"
    Private Shared ReadOnly NsOdfFo As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"
    Private Shared ReadOnly NsOdfDraw As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
    Private Shared ReadOnly NsOdfXlink As XNamespace = "http://www.w3.org/1999/xlink"
    ' Namespace dell'estensione LibreOffice usata per i temi colore in ODF (non ancora standard OASIS).
    Private Shared ReadOnly NsLoext As XNamespace = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0"

    Private Class StileOdt
        Public Bold As Boolean
        Public Italic As Boolean
        Public Underline As Boolean
        Public Strikethrough As Boolean
        Public FontFamily As String = ""
        Public FontSize As Single = 0
        Public ColorHex As String = ""
        Public HighlightColorHex As String = ""
        Public Alignment As String = ""
        Public IndentLevel As Integer = 0
    End Class

    Private Shared Sub CaricaDocumentoOdtInStato(filePath As String, stopCheck As Func(Of Boolean), state As WGDocumentState)
        Try
            state.SourcePath = filePath

            Dim xmlSettings As New System.Xml.XmlReaderSettings()
            xmlSettings.DtdProcessing = System.Xml.DtdProcessing.Prohibit
            xmlSettings.MaxCharactersInDocument = 50_000_000L
            xmlSettings.MaxCharactersFromEntities = 1_000_000L

            Dim docContent As XDocument = Nothing
            Dim docStyles As XDocument = Nothing
            Dim immaginiZip As New Dictionary(Of String, Byte())

            Using fs As FileStream = New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using archive As ZipArchive = New ZipArchive(fs, ZipArchiveMode.Read)
                    Dim entryContent As ZipArchiveEntry = archive.GetEntry("content.xml")
                    If entryContent Is Nothing Then Throw New InvalidDataException("Il file ODT non contiene content.xml, non è un documento valido.")
                    Using stC As Stream = entryContent.Open()
                        Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stC, xmlSettings)
                            docContent = XDocument.Load(xr)
                        End Using
                    End Using

                    Dim entryStyles As ZipArchiveEntry = archive.GetEntry("styles.xml")
                    If entryStyles IsNot Nothing Then
                        Try
                            Using stS As Stream = entryStyles.Open()
                                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(stS, xmlSettings)
                                    docStyles = XDocument.Load(xr)
                                End Using
                            End Using
                        Catch exStyles As Exception
                            logger.ScriviLog("CaricaDocumentoOdtInStato (styles.xml): " & exStyles.Message)
                        End Try
                    End If

                    For Each entry As ZipArchiveEntry In archive.Entries
                        If entry.FullName.StartsWith("Pictures/", StringComparison.OrdinalIgnoreCase) Then
                            Try
                                Using stImg As Stream = entry.Open()
                                    Using ms As New MemoryStream()
                                        stImg.CopyTo(ms)
                                        immaginiZip(entry.FullName) = ms.ToArray()
                                    End Using
                                End Using
                            Catch exImg As Exception
                                logger.ScriviLog("CaricaDocumentoOdtInStato (immagine " & entry.FullName & "): " & exImg.Message)
                            End Try
                        End If
                    Next
                End Using
            End Using

            Dim mappaStili As New Dictionary(Of String, StileOdt)
            Dim mappaListeNumerate As New Dictionary(Of String, Boolean)
            CostruisciMappaStiliOdt(docContent.Root, mappaStili, mappaListeNumerate)
            If docStyles IsNot Nothing Then CostruisciMappaStiliOdt(docStyles.Root, mappaStili, mappaListeNumerate)

            If docStyles IsNot Nothing Then
                Try
                    Dim masterPage As XElement = docStyles.Descendants(NsOdfStyle + "master-page").FirstOrDefault()
                    If masterPage IsNot Nothing Then
                        _paletteTemaCorrente = PaletteTemaDaMasterPageOdt(masterPage)
                        Dim headerEl As XElement = masterPage.Element(NsOdfStyle + "header")
                        If headerEl IsNot Nothing Then
                            Dim sbHdr As New StringBuilder()
                            For Each pEl As XElement In headerEl.Descendants(NsOdfText + "p")
                                If sbHdr.Length > 0 Then sbHdr.Append(" ")
                                sbHdr.Append(pEl.Value)
                            Next
                            state.HeaderText = sbHdr.ToString()
                        End If
                        Dim footerEl As XElement = masterPage.Element(NsOdfStyle + "footer")
                        If footerEl IsNot Nothing Then
                            Dim sbFtr As New StringBuilder()
                            For Each pEl As XElement In footerEl.Descendants(NsOdfText + "p")
                                If sbFtr.Length > 0 Then sbFtr.Append(" ")
                                sbFtr.Append(pEl.Value)
                            Next
                            state.FooterText = sbFtr.ToString()
                        End If
                    End If
                Catch exMasterPage As Exception
                    logger.ScriviLog("CaricaDocumentoOdtInStato (master-page intestazione/piè di pagina): " & exMasterPage.Message)
                End Try
            End If

            Dim body As XElement = docContent.Root.Element(NsOdfOffice + "body")
            If body Is Nothing Then Throw New InvalidDataException("Il file ODT non contiene un corpo del documento valido.")
            Dim textRoot As XElement = body.Element(NsOdfOffice + "text")
            If textRoot Is Nothing Then Throw New InvalidDataException("Il file ODT non contiene testo.")

            Dim elements As New List(Of DocElement)
            Dim contatoreNote As Integer() = {0}

            For Each nodo As XElement In textRoot.Elements()
                If stopCheck IsNot Nothing AndAlso stopCheck.Invoke() Then Exit For
                Try
                    ProcessaNodoCorpoOdt(nodo, elements, mappaStili, mappaListeNumerate, immaginiZip, contatoreNote)
                Catch exNodo As Exception
                    logger.ScriviLog("CaricaDocumentoOdtInStato (nodo " & nodo.Name.LocalName & "): " & exNodo.Message)
                End Try
            Next

            If elements.Count = 0 Then elements.Add(New DocParagraph())

            state.Elements = elements
        Catch ex As UnauthorizedAccessException
            logger.ScriviLog($"CaricaDocumentoOdtInStato UnauthorizedAccess: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il file non è accessibile per la lettura a causa delle autorizzazioni.")
        Catch ex As IOException
            logger.ScriviLog($"CaricaDocumentoOdtInStato IOException: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Errore di input o output durante la lettura. Il file potrebbe essere in uso.")
        Catch ex As Exception
            logger.ScriviLog($"CaricaDocumentoOdtInStato Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Impossibile leggere il file ODT: " & ex.Message)
        End Try

        RiparaDocumento(state.Elements)
    End Sub

    Private Shared Sub CostruisciMappaStiliOdt(radice As XElement, mappaStili As Dictionary(Of String, StileOdt), mappaListeNumerate As Dictionary(Of String, Boolean))
        Try
            If radice Is Nothing Then Return
            For Each contenitoreStili In {radice.Element(NsOdfOffice + "automatic-styles"), radice.Element(NsOdfOffice + "styles")}
                If contenitoreStili Is Nothing Then Continue For
                For Each styleEl As XElement In contenitoreStili.Elements(NsOdfStyle + "style")
                    Try
                        Dim nomeAttr = styleEl.Attribute(NsOdfStyle + "name")
                        If nomeAttr Is Nothing Then Continue For
                        Dim s As New StileOdt()

                        Dim propTesto As XElement = styleEl.Element(NsOdfStyle + "text-properties")
                        If propTesto IsNot Nothing Then
                            Dim pesoAttr = propTesto.Attribute(NsOdfFo + "font-weight")
                            If pesoAttr IsNot Nothing AndAlso pesoAttr.Value = "bold" Then s.Bold = True
                            Dim stileAttr = propTesto.Attribute(NsOdfFo + "font-style")
                            If stileAttr IsNot Nothing AndAlso stileAttr.Value = "italic" Then s.Italic = True
                            Dim sotAttr = propTesto.Attribute(NsOdfStyle + "text-underline-style")
                            If sotAttr IsNot Nothing AndAlso sotAttr.Value <> "none" Then s.Underline = True
                            Dim barAttr = propTesto.Attribute(NsOdfStyle + "text-line-through-style")
                            If barAttr IsNot Nothing AndAlso barAttr.Value <> "none" Then s.Strikethrough = True
                            Dim fontAttr = propTesto.Attribute(NsOdfStyle + "font-name")
                            If fontAttr IsNot Nothing Then s.FontFamily = fontAttr.Value
                            Dim dimAttr = propTesto.Attribute(NsOdfFo + "font-size")
                            If dimAttr IsNot Nothing Then
                                Dim ptStr As String = dimAttr.Value.Replace("pt", "").Trim()
                                Dim ptVal As Single
                                If Single.TryParse(ptStr, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, ptVal) Then s.FontSize = ptVal
                            End If
                            Dim colAttr = propTesto.Attribute(NsOdfFo + "color")
                            If colAttr IsNot Nothing Then
                                s.ColorHex = ConvertHexToNameColor(colAttr.Value.TrimStart("#"c))
                            Else
                                Dim ruoloAttr = propTesto.Attribute(NsLoext + "theme-color")
                                If ruoloAttr IsNot Nothing Then
                                    Dim ruoloInterno As String = Nothing
                                    If MappaRuoliOdt.TryGetValue(ruoloAttr.Value, ruoloInterno) Then
                                        Dim hexDaTema As String = HexPerRuoloTema(ruoloInterno, PaletteTemaAttiva())
                                        If Not String.IsNullOrEmpty(hexDaTema) Then s.ColorHex = ConvertHexToNameColor(hexDaTema)
                                    End If
                                End If
                            End If
                            Dim bgAttr = propTesto.Attribute(NsOdfFo + "background-color")
                            If bgAttr IsNot Nothing AndAlso bgAttr.Value <> "transparent" Then s.HighlightColorHex = ConvertHexToNameColor(bgAttr.Value.TrimStart("#"c))
                        End If

                        Dim propPara As XElement = styleEl.Element(NsOdfStyle + "paragraph-properties")
                        If propPara IsNot Nothing Then
                            Dim allineaAttr = propPara.Attribute(NsOdfFo + "text-align")
                            If allineaAttr IsNot Nothing Then
                                Select Case allineaAttr.Value.ToLowerInvariant()
                                    Case "center" : s.Alignment = "Center"
                                    Case "end" : s.Alignment = "Right"
                                    Case "justify" : s.Alignment = "Justify"
                                    Case Else : s.Alignment = "Left"
                                End Select
                            End If
                            Dim marginAttr = propPara.Attribute(NsOdfFo + "margin-left")
                            If marginAttr IsNot Nothing Then
                                Dim cmStr As String = marginAttr.Value.Replace("cm", "").Trim()
                                Dim cmVal As Single
                                If Single.TryParse(cmStr, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, cmVal) Then
                                    s.IndentLevel = CInt(System.Math.Round(cmVal / 1.27))
                                End If
                            End If
                        End If

                        mappaStili(nomeAttr.Value) = s
                    Catch exStile As Exception
                        logger.ScriviLog("CostruisciMappaStiliOdt (stile): " & exStile.Message)
                    End Try
                Next

                For Each listStyleEl As XElement In contenitoreStili.Elements(NsOdfText + "list-style")
                    Try
                        Dim nomeListaAttr = listStyleEl.Attribute(NsOdfStyle + "name")
                        If nomeListaAttr Is Nothing Then Continue For
                        Dim eNumerata As Boolean = listStyleEl.Elements(NsOdfText + "list-level-style-number").Any()
                        mappaListeNumerate(nomeListaAttr.Value) = eNumerata
                    Catch exLista As Exception
                        logger.ScriviLog("CostruisciMappaStiliOdt (lista): " & exLista.Message)
                    End Try
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog("CostruisciMappaStiliOdt: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ProcessaNodoCorpoOdt(nodo As XElement, elements As List(Of DocElement), mappaStili As Dictionary(Of String, StileOdt), mappaListeNumerate As Dictionary(Of String, Boolean), immaginiZip As Dictionary(Of String, Byte()), contatoreNote As Integer())
        Try
            If nodo.Name = NsOdfText + "p" OrElse nodo.Name = NsOdfText + "h" Then
                Dim note As New List(Of DocParagraph)
                Dim para As DocParagraph = CostruisciParagrafoOdt(nodo, mappaStili, immaginiZip, contatoreNote, note)
                If nodo.Name = NsOdfText + "h" Then
                    Dim livelloAttr = nodo.Attribute(NsOdfText + "outline-level")
                    Dim livello As Integer = 1
                    If livelloAttr IsNot Nothing Then Integer.TryParse(livelloAttr.Value, livello)
                    para.BookmarkId = "STL_" & Guid.NewGuid().ToString("N").Substring(0, 8)
                End If
                elements.Add(para)
                For Each n In note
                    elements.Add(n)
                Next
            ElseIf nodo.Name = NsOdfText + "list" Then
                Dim styleNameAttr = nodo.Attribute(NsOdfText + "style-name")
                Dim eNumerata As Boolean = False
                If styleNameAttr IsNot Nothing AndAlso mappaListeNumerate.ContainsKey(styleNameAttr.Value) Then
                    eNumerata = mappaListeNumerate(styleNameAttr.Value)
                End If
                For Each itemEl As XElement In nodo.Elements(NsOdfText + "list-item")
                    For Each pInterno As XElement In itemEl.Elements(NsOdfText + "p")
                        Dim note As New List(Of DocParagraph)
                        Dim paraLista As DocParagraph = CostruisciParagrafoOdt(pInterno, mappaStili, immaginiZip, contatoreNote, note)
                        paraLista.IsListItem = True
                        If eNumerata Then paraLista.ListNumValue = "decimal"
                        elements.Add(paraLista)
                        For Each n In note
                            elements.Add(n)
                        Next
                    Next
                    For Each listaAnnidata As XElement In itemEl.Elements(NsOdfText + "list")
                        ProcessaNodoCorpoOdt(listaAnnidata, elements, mappaStili, mappaListeNumerate, immaginiZip, contatoreNote)
                    Next
                Next
            ElseIf nodo.Name = NsOdfTable + "table" Then
                elements.Add(CostruisciTabellaOdt(nodo, mappaStili, immaginiZip, contatoreNote))
            End If
        Catch ex As Exception
            logger.ScriviLog("ProcessaNodoCorpoOdt: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CostruisciParagrafoOdt(pEl As XElement, mappaStili As Dictionary(Of String, StileOdt), immaginiZip As Dictionary(Of String, Byte()), contatoreNote As Integer(), noteEstratte As List(Of DocParagraph)) As DocParagraph
        Dim para As New DocParagraph()
        Try
            Dim styleNameAttr = pEl.Attribute(NsOdfText + "style-name")
            If styleNameAttr IsNot Nothing AndAlso mappaStili.ContainsKey(styleNameAttr.Value) Then
                Dim sPara As StileOdt = mappaStili(styleNameAttr.Value)
                If Not String.IsNullOrEmpty(sPara.Alignment) Then para.Alignment = sPara.Alignment
                para.IndentLevel = sPara.IndentLevel
            End If

            CostruisciRunsOdt(pEl, para.Runs, mappaStili, immaginiZip, contatoreNote, noteEstratte)
        Catch ex As Exception
            logger.ScriviLog("CostruisciParagrafoOdt: " & ex.Message)
        End Try
        Return para
    End Function

    Private Shared Sub CostruisciRunsOdt(contenitore As XElement, runs As List(Of DocRun), mappaStili As Dictionary(Of String, StileOdt), immaginiZip As Dictionary(Of String, Byte()), contatoreNote As Integer(), noteEstratte As List(Of DocParagraph))
        Try
            For Each nodo As XNode In contenitore.Nodes()
                If TypeOf nodo Is XText Then
                    Dim testo As String = DirectCast(nodo, XText).Value
                    If Not String.IsNullOrEmpty(testo) Then
                        runs.Add(New DocRun() With {.Text = testo})
                    End If
                    Continue For
                End If

                If Not (TypeOf nodo Is XElement) Then Continue For
                Dim el As XElement = DirectCast(nodo, XElement)

                If el.Name = NsOdfText + "span" Then
                    Dim styleNameAttr = el.Attribute(NsOdfText + "style-name")
                    Dim runsInterni As New List(Of DocRun)
                    CostruisciRunsOdt(el, runsInterni, mappaStili, immaginiZip, contatoreNote, noteEstratte)
                    If styleNameAttr IsNot Nothing AndAlso mappaStili.ContainsKey(styleNameAttr.Value) Then
                        Dim sSpan As StileOdt = mappaStili(styleNameAttr.Value)
                        For Each ri In runsInterni
                            ri.Bold = sSpan.Bold
                            ri.Italic = sSpan.Italic
                            ri.Underline = sSpan.Underline
                            ri.Strikethrough = sSpan.Strikethrough
                            If Not String.IsNullOrEmpty(sSpan.FontFamily) Then ri.FontFamily = sSpan.FontFamily
                            If sSpan.FontSize > 0 Then ri.FontSize = sSpan.FontSize
                            If Not String.IsNullOrEmpty(sSpan.ColorHex) Then ri.ColorHex = sSpan.ColorHex
                            If Not String.IsNullOrEmpty(sSpan.HighlightColorHex) Then ri.HighlightColorHex = sSpan.HighlightColorHex
                        Next
                    End If
                    runs.AddRange(runsInterni)
                ElseIf el.Name = NsOdfText + "a" Then
                    Dim hrefAttr = el.Attribute(NsOdfXlink + "href")
                    Dim runsLink As New List(Of DocRun)
                    CostruisciRunsOdt(el, runsLink, mappaStili, immaginiZip, contatoreNote, noteEstratte)
                    For Each rl In runsLink
                        rl.IsHyperlink = True
                        If hrefAttr IsNot Nothing Then rl.LinkUrl = hrefAttr.Value
                    Next
                    runs.AddRange(runsLink)
                ElseIf el.Name = NsOdfText + "tab" Then
                    runs.Add(New DocRun() With {.Text = vbTab})
                ElseIf el.Name = NsOdfText + "line-break" Then
                    runs.Add(New DocRun() With {.Text = vbLf})
                ElseIf el.Name = NsOdfText + "s" Then
                    Dim numAttr = el.Attribute(NsOdfText + "c")
                    Dim numSpazi As Integer = 1
                    If numAttr IsNot Nothing Then Integer.TryParse(numAttr.Value, numSpazi)
                    runs.Add(New DocRun() With {.Text = New String(" "c, System.Math.Max(1, numSpazi))})
                ElseIf el.Name = NsOdfDraw + "frame" Then
                    Dim imgEl As XElement = el.Element(NsOdfDraw + "image")
                    If imgEl IsNot Nothing Then
                        Dim hrefImgAttr = imgEl.Attribute(NsOdfXlink + "href")
                        If hrefImgAttr IsNot Nothing AndAlso immaginiZip.ContainsKey(hrefImgAttr.Value) Then
                            Dim imgRun As New DocRun()
                            imgRun.ImageBytes = immaginiZip(hrefImgAttr.Value)
                            Dim wAttr = el.Attribute(NsOdfSvg + "width")
                            Dim hAttr = el.Attribute(NsOdfSvg + "height")
                            If wAttr IsNot Nothing Then imgRun.ImageWidthPt = ConvertiOdtCmAPunti(wAttr.Value)
                            If hAttr IsNot Nothing Then imgRun.ImageHeightPt = ConvertiOdtCmAPunti(hAttr.Value)
                            runs.Add(imgRun)
                        End If
                    End If
                ElseIf el.Name = NsOdfText + "note" Then
                    Try
                        Dim classeAttr = el.Attribute(NsOdfText + "note-class")
                        Dim eFootnote As Boolean = (classeAttr Is Nothing OrElse classeAttr.Value <> "endnote")
                        Dim citazioneEl As XElement = el.Element(NsOdfText + "note-citation")
                        Dim corpoEl As XElement = el.Element(NsOdfText + "note-body")
                        Dim citazioneTesto As String = If(citazioneEl IsNot Nothing, citazioneEl.Value, "")
                        Dim corpoTesto As String = ""
                        If corpoEl IsNot Nothing Then
                            corpoTesto = String.Join(" ", corpoEl.Descendants(NsOdfText + "p").Select(Function(pp) pp.Value)).Trim()
                        End If

                        contatoreNote(0) += 1
                        Dim prefissoBookmark As String = If(eFootnote, "NOTA_PIE_", "NOTA_FINE_")
                        Dim bookmarkId As String = prefissoBookmark & contatoreNote(0)

                        Dim runMarcatore As New DocRun() With {
                            .Text = If(String.IsNullOrWhiteSpace(citazioneTesto), contatoreNote(0).ToString(), citazioneTesto),
                            .IsSuperscript = True,
                            .Bold = True,
                            .FontSize = 8,
                            .LinkUrl = bookmarkId
                        }
                        runs.Add(runMarcatore)

                        Dim notaPara As New DocParagraph()
                        notaPara.BookmarkId = bookmarkId
                        notaPara.Runs.Add(New DocRun() With {
                            .Text = If(String.IsNullOrWhiteSpace(citazioneTesto), contatoreNote(0).ToString(), citazioneTesto),
                            .IsSuperscript = True,
                            .Bold = True,
                            .FontSize = 8,
                            .LinkUrl = bookmarkId
                        })
                        notaPara.Runs.Add(New DocRun() With {.Text = " "})
                        notaPara.Runs.Add(New DocRun() With {.Text = corpoTesto, .FontSize = 9})
                        noteEstratte.Add(notaPara)
                    Catch exNota As Exception
                        logger.ScriviLog("CostruisciRunsOdt (nota): " & exNota.Message)
                    End Try
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("CostruisciRunsOdt: " & ex.Message)
        End Try
    End Sub

    Private Shared ReadOnly NsOdfSvg As XNamespace = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"

    Private Shared Function ConvertiOdtCmAPunti(valore As String) As Single
        Try
            Dim v As String = valore.Trim()
            Dim moltiplicatore As Single = 28.35F
            If v.EndsWith("in") Then
                moltiplicatore = 72.0F
                v = v.Substring(0, v.Length - 2)
            ElseIf v.EndsWith("cm") Then
                v = v.Substring(0, v.Length - 2)
            ElseIf v.EndsWith("mm") Then
                moltiplicatore = 2.835F
                v = v.Substring(0, v.Length - 2)
            End If
            Dim numerico As Single
            If Single.TryParse(v, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, numerico) Then
                Return numerico * moltiplicatore
            End If
            Return 0
        Catch ex As Exception
            logger.ScriviLog("ConvertiOdtCmAPunti: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function CostruisciTabellaOdt(tblEl As XElement, mappaStili As Dictionary(Of String, StileOdt), immaginiZip As Dictionary(Of String, Byte()), contatoreNote As Integer()) As DocTable
        Dim tabella As New DocTable()
        Try
            For Each rowEl As XElement In tblEl.Elements(NsOdfTable + "table-row")
                Dim riga As New DocTableRow()
                For Each cellEl As XElement In rowEl.Elements(NsOdfTable + "table-cell")
                    Dim cella As New DocTableCell()
                    For Each pCellEl As XElement In cellEl.Elements(NsOdfText + "p")
                        Dim noteCella As New List(Of DocParagraph)
                        Dim paraCella As DocParagraph = CostruisciParagrafoOdt(pCellEl, mappaStili, immaginiZip, contatoreNote, noteCella)
                        cella.Paragraphs.Add(paraCella)
                    Next
                    If cella.Paragraphs.Count = 0 Then cella.Paragraphs.Add(New DocParagraph())
                    riga.Cells.Add(cella)
                Next
                If riga.Cells.Count > 0 Then tabella.Rows.Add(riga)
            Next
        Catch ex As Exception
            logger.ScriviLog("CostruisciTabellaOdt: " & ex.Message)
        End Try
        Return tabella
    End Function

    Public Shared Sub SalvaDocumento(elements As List(Of DocElement), outputPath As String, Optional pageSettings As Dictionary(Of String, Integer) = Nothing)
        SalvaDocumentoCompleto(elements, outputPath, Nothing, Nothing, Nothing, pageSettings, String.Empty, String.Empty, String.Empty)
    End Sub

    Public Shared Sub SalvaDocumento(state As WGDocumentState, outputPath As String)
        SalvaDocumentoCompleto(state.Elements, outputPath, state.ImmaginiMedia, state.StiliTitolo, state.CommentiPerParagrafo, state.PageSettings, state.SourcePath, state.HeaderText, state.FooterText)
    End Sub

    ' ===================== Conversione fra formati (odt <-> docx) =====================
    ' Non è una pipeline "a parte": è esattamente il percorso "Apri" + "Salva con nome",
    ' riusando CaricaStato (che sa già leggere sia .odt che .docx/.dotx/.docm, incluso il
    ' tema colori) e SalvaDocumento (che sceglie il writer giusto in base all'estensione di
    ' output). Immagini, tema colori, intestazioni/piè di pagina, commenti e impaginazione
    ' seguono quindi automaticamente le stesse regole già usate per aprire/salvare i singoli
    ' formati, senza bisogno di codice di conversione dedicato.

    ''' <summary>
    ''' Converte un documento .odt in un nuovo file .docx.
    ''' </summary>
    ''' <param name="percorsoOdt">Percorso del file .odt di origine.</param>
    ''' <param name="percorsoDocx">Percorso del file .docx da creare (o sovrascrivere).</param>
    ''' <param name="stopCheck">
    ''' Funzione opzionale che, se restituisce True, interrompe l'operazione (es. per annullamento
    ''' da UI durante il caricamento di un file molto grande). Se Nothing, l'operazione non viene
    ''' mai interrotta.
    ''' </param>
    Public Shared Sub ConvertiOdtInDocx(percorsoOdt As String, percorsoDocx As String, Optional stopCheck As Func(Of Boolean) = Nothing)
        If String.IsNullOrWhiteSpace(percorsoOdt) Then Throw New ArgumentException("Il percorso del file .odt di origine non può essere vuoto.")
        If String.IsNullOrWhiteSpace(percorsoDocx) Then Throw New ArgumentException("Il percorso del file .docx di destinazione non può essere vuoto.")
        If Not Path.GetExtension(percorsoOdt).Equals(".odt", StringComparison.OrdinalIgnoreCase) Then
            Throw New NotSupportedException("Il file di origine deve avere estensione .odt.")
        End If
        If Not Path.GetExtension(percorsoDocx).Equals(".docx", StringComparison.OrdinalIgnoreCase) Then
            Throw New NotSupportedException("Il file di destinazione deve avere estensione .docx.")
        End If
        If Not File.Exists(percorsoOdt) Then Throw New FileNotFoundException("Il file .odt di origine non esiste.", percorsoOdt)

        Dim verificaStop As Func(Of Boolean) = If(stopCheck, Function() False)

        Try
            Dim state As WGDocumentState = CaricaStato(percorsoOdt, verificaStop)
            If verificaStop() Then Return
            SalvaDocumento(state, percorsoDocx)
        Catch ex As Exception
            logger.ScriviLog($"ConvertiOdtInDocx ('{percorsoOdt}' -> '{percorsoDocx}'): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Converte un documento .docx/.dotx/.docm in un nuovo file .odt. Aggiunta per simmetria
    ''' a ConvertiOdtInDocx: essendo la stessa pipeline in entrambe le direzioni, il costo di
    ''' offrire anche il verso opposto è minimo.
    ''' </summary>
    Public Shared Sub ConvertiDocxInOdt(percorsoDocx As String, percorsoOdt As String, Optional stopCheck As Func(Of Boolean) = Nothing)
        If String.IsNullOrWhiteSpace(percorsoDocx) Then Throw New ArgumentException("Il percorso del file di origine non può essere vuoto.")
        If String.IsNullOrWhiteSpace(percorsoOdt) Then Throw New ArgumentException("Il percorso del file .odt di destinazione non può essere vuoto.")
        Dim estOrigine As String = Path.GetExtension(percorsoDocx).ToLowerInvariant()
        If estOrigine <> ".docx" AndAlso estOrigine <> ".dotx" AndAlso estOrigine <> ".docm" Then
            Throw New NotSupportedException("Il file di origine deve avere estensione .docx, .dotx o .docm.")
        End If
        If Not Path.GetExtension(percorsoOdt).Equals(".odt", StringComparison.OrdinalIgnoreCase) Then
            Throw New NotSupportedException("Il file di destinazione deve avere estensione .odt.")
        End If
        If Not File.Exists(percorsoDocx) Then Throw New FileNotFoundException("Il file di origine non esiste.", percorsoDocx)

        Dim verificaStop As Func(Of Boolean) = If(stopCheck, Function() False)

        Try
            Dim state As WGDocumentState = CaricaStato(percorsoDocx, verificaStop)
            If verificaStop() Then Return
            SalvaDocumento(state, percorsoOdt)
        Catch ex As Exception
            logger.ScriviLog($"ConvertiDocxInOdt ('{percorsoDocx}' -> '{percorsoOdt}'): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw
        End Try
    End Sub
    ' =================== Fine conversione fra formati ===================

    Private Shared Sub SpostaOCopiaFile(origine As String, destinazione As String)
        Try
            File.Move(origine, destinazione)
        Catch exMoveIniziale As Exception
            logger.ScriviLog($"SpostaOCopiaFile: File.Move diretto non riuscito, uso copia+cancellazione. Errore: {exMoveIniziale.Message}")
            File.Copy(origine, destinazione, True)
            Try
                File.Delete(origine)
            Catch exDelete As Exception
                logger.ScriviLog($"SpostaOCopiaFile: impossibile eliminare il file temporaneo '{origine}' dopo la copia. Errore: {exDelete.Message}")
            End Try
        End Try
    End Sub

    Private Shared Function CompletaStiliMancanti(stylesXmlOriginale As String, haCommenti As Boolean, haFootnotes As Boolean, haEndnotes As Boolean) As String
        Try
            Dim sbAggiunte As New StringBuilder()

            If haCommenti AndAlso Not stylesXmlOriginale.Contains("w:styleId=""CommentText""") Then
                sbAggiunte.Append("<w:style w:type=""paragraph"" w:styleId=""CommentText""><w:name w:val=""annotation text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""20""/><w:szCs w:val=""20""/></w:rPr></w:style>")
            End If
            If haCommenti AndAlso Not stylesXmlOriginale.Contains("w:styleId=""CommentReference""") Then
                sbAggiunte.Append("<w:style w:type=""character"" w:styleId=""CommentReference""><w:name w:val=""annotation reference""/><w:rPr><w:sz w:val=""16""/><w:szCs w:val=""16""/></w:rPr></w:style>")
            End If
            If haFootnotes AndAlso Not stylesXmlOriginale.Contains("w:styleId=""FootnoteText""") Then
                sbAggiunte.Append("<w:style w:type=""paragraph"" w:styleId=""FootnoteText""><w:name w:val=""footnote text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""18""/><w:szCs w:val=""18""/></w:rPr></w:style>")
            End If
            If haFootnotes AndAlso Not stylesXmlOriginale.Contains("w:styleId=""FootnoteReference""") Then
                sbAggiunte.Append("<w:style w:type=""character"" w:styleId=""FootnoteReference""><w:name w:val=""footnote reference""/><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr></w:style>")
            End If
            If haEndnotes AndAlso Not stylesXmlOriginale.Contains("w:styleId=""EndnoteText""") Then
                sbAggiunte.Append("<w:style w:type=""paragraph"" w:styleId=""EndnoteText""><w:name w:val=""endnote text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""18""/><w:szCs w:val=""18""/></w:rPr></w:style>")
            End If
            If haEndnotes AndAlso Not stylesXmlOriginale.Contains("w:styleId=""EndnoteReference""") Then
                sbAggiunte.Append("<w:style w:type=""character"" w:styleId=""EndnoteReference""><w:name w:val=""endnote reference""/><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr></w:style>")
            End If

            If sbAggiunte.Length = 0 Then Return stylesXmlOriginale

            Dim posizioneChiusura As Integer = stylesXmlOriginale.LastIndexOf("</w:styles>")
            If posizioneChiusura < 0 Then Return stylesXmlOriginale

            Return stylesXmlOriginale.Substring(0, posizioneChiusura) & sbAggiunte.ToString() & stylesXmlOriginale.Substring(posizioneChiusura)
        Catch ex As Exception
            logger.ScriviLog("CompletaStiliMancanti: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return stylesXmlOriginale
        End Try
    End Function

    Public Shared Sub SalvaDocumentoCompleto(elements As List(Of DocElement), outputPath As String, stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), Optional pageSettings As Dictionary(Of String, Integer) = Nothing)
        SalvaDocumentoCompleto(elements, outputPath, Nothing, stiliTitolo, commentiPerParagrafo, pageSettings, String.Empty, String.Empty, String.Empty)
    End Sub

    Public Shared Sub SalvaDocumentoCompleto(elements As List(Of DocElement), outputPath As String, immaginiMedia As Dictionary(Of String, Byte()), stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), Optional pageSettings As Dictionary(Of String, Integer) = Nothing)
        SalvaDocumentoCompleto(elements, outputPath, immaginiMedia, stiliTitolo, commentiPerParagrafo, pageSettings, String.Empty, String.Empty, String.Empty)
    End Sub

    Public Shared Sub SalvaDocumentoCompleto(elements As List(Of DocElement), outputPath As String, immaginiMedia As Dictionary(Of String, Byte()), stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), pageSettings As Dictionary(Of String, Integer), sourcePath As String, Optional headerText As String = "", Optional footerText As String = "")
        Try
            If Not String.IsNullOrEmpty(outputPath) AndAlso Path.GetExtension(outputPath).Equals(".odt", StringComparison.OrdinalIgnoreCase) Then
                SalvaDocumentoOdt(elements, outputPath, immaginiMedia, commentiPerParagrafo, pageSettings, sourcePath, headerText, footerText)
                Return
            End If

            _bookmarkSaveCounter = 1
            SalvaDocumentoDocxOpenXml(elements, outputPath, immaginiMedia, stiliTitolo, commentiPerParagrafo, pageSettings, sourcePath, headerText, footerText)
        Catch ex As Exception
            logger.ScriviLog("SalvaDocumentoCompleto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Sub

    Private Shared Function state_GetMediaPath(nomeFileMedia As String) As String
        Return "word/media/" & nomeFileMedia
    End Function

    Private Shared Function OttieniChiaveRisoluzioneImmagine(run As DocRun) As String
        Try
            If run Is Nothing Then Return ""
            If Not String.IsNullOrEmpty(run.OriginalRId) Then Return run.OriginalRId
            If Not String.IsNullOrEmpty(run.RawContent) Then
                Dim candidati As List(Of String) = EstraiRelationshipIdDaRawContent(run.RawContent)
                If candidati.Count > 0 Then Return candidati(0)
            End If
            Return ""
        Catch ex As Exception
            logger.ScriviLog($"OttieniChiaveRisoluzioneImmagine: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Sub RaccogliImmaginiDaParagrafo(p As DocParagraph, immaginiMedia As Dictionary(Of String, Byte()), immaginiUsate As Dictionary(Of String, Byte()), mappaRidInOutput As Dictionary(Of String, String), ByRef mediaCounter As Integer, sourcePath As String, cacheRelsXml As Dictionary(Of String, XDocument))
        Try
            For Each run As DocRun In p.Runs
                If Not run.IsImage Then Continue For

                Dim rIdsNelContenuto As List(Of String) = If(String.IsNullOrEmpty(run.RawContent), New List(Of String), EstraiRelationshipIdDaRawContent(run.RawContent))
                If String.IsNullOrEmpty(run.OriginalRId) AndAlso rIdsNelContenuto.Count = 0 Then Continue For

                Dim chiave As String = OttieniChiaveRisoluzioneImmagine(run)
                If String.IsNullOrEmpty(chiave) Then Continue For
                If mappaRidInOutput.ContainsKey(chiave) Then Continue For

                Dim datiImmagine As Byte() = Nothing
                Dim motivoFallimento As String = ""

                If run.ImageBytes IsNot Nothing AndAlso run.ImageBytes.Length > 0 Then
                    datiImmagine = run.ImageBytes
                ElseIf Not String.IsNullOrEmpty(run.OriginalRId) AndAlso immaginiMedia IsNot Nothing AndAlso immaginiMedia.ContainsKey(run.OriginalRId) Then
                    datiImmagine = immaginiMedia(run.OriginalRId)
                ElseIf rIdsNelContenuto.Count > 0 AndAlso Not String.IsNullOrEmpty(sourcePath) Then
                    datiImmagine = LeggiImmagineDaPacchettoOriginale(sourcePath, rIdsNelContenuto, motivoFallimento, cacheRelsXml)
                End If

                If datiImmagine Is Nothing OrElse datiImmagine.Length = 0 Then
                    Continue For
                End If

                Dim estensione As String = RilevaEstensione(datiImmagine)
                Dim nomeFile As String = "img" & mediaCounter.ToString() & "." & estensione
                mediaCounter += 1

                immaginiUsate(nomeFile) = datiImmagine
                mappaRidInOutput(chiave) = nomeFile
            Next
        Catch ex As Exception
            logger.ScriviLog($"RaccogliImmaginiDaParagrafo Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Shared Function RilevaEstensione(dati As Byte()) As String
        Try
            If dati Is Nothing OrElse dati.Length < 4 Then Return "png"
            If dati(0) = &H89 AndAlso dati(1) = &H50 AndAlso dati(2) = &H4E AndAlso dati(3) = &H47 Then Return "png"
            If dati(0) = &HFF AndAlso dati(1) = &HD8 Then Return "jpg"
            If dati(0) = &H47 AndAlso dati(1) = &H49 AndAlso dati(2) = &H46 Then Return "gif"
            If dati(0) = &H42 AndAlso dati(1) = &H4D Then Return "bmp"
            Return "png"
        Catch ex As Exception
            logger.ScriviLog($"RilevaEstensione Error: {ex.Message}")
            Return "png"
        End Try
    End Function

    Private Shared Function AggiornRidInRawContent(rawContent As String, oldRid As String, newRid As String) As String
        Try
            If String.IsNullOrEmpty(rawContent) OrElse String.IsNullOrEmpty(oldRid) Then Return rawContent
            Return rawContent.Replace("""" & oldRid & """", """" & newRid & """")
        Catch ex As Exception
            logger.ScriviLog($"AggiornRidInRawContent Error: {ex.Message}")
            Return rawContent
        End Try
    End Function

    Private Shared Function ParseParagraph(p As XElement, immaginiMedia As Dictionary(Of String, Byte()), mappaRelazioni As Dictionary(Of String, String)) As DocParagraph
        Return ParseParagraphConMetadati(p, Nothing, Nothing, Nothing, immaginiMedia, mappaRelazioni)
    End Function

    Private Shared Function ParseParagraphConMetadati(p As XElement, stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), mappaCommenti As Dictionary(Of Integer, String), immaginiMedia As Dictionary(Of String, Byte()), mappaRelazioni As Dictionary(Of String, String), Optional mappaFootnotes As Dictionary(Of Integer, String) = Nothing, Optional mappaEndnotes As Dictionary(Of Integer, String) = Nothing) As DocParagraph
        Dim newPara As New DocParagraph()
        Try
            Dim livelloTitoloRilevato As Integer = 0
            Dim pPr As XElement = p.Element(NsW + "pPr")
            If pPr IsNot Nothing Then
                Dim pStyleEl As XElement = pPr.Element(NsW + "pStyle")
                If pStyleEl IsNot Nothing AndAlso pStyleEl.Attribute(NsW + "val") IsNot Nothing Then
                    Dim styleVal As String = pStyleEl.Attribute(NsW + "val").Value
                    newPara.StyleId = styleVal
                    Dim m = Regex.Match(styleVal, "^(?:Heading|heading|Titolo|titolo)\s*(\d+)$")
                    If m.Success Then
                        Dim livelloParse As Integer
                        If Integer.TryParse(m.Groups(1).Value, livelloParse) AndAlso livelloParse >= 1 AndAlso livelloParse <= 6 Then
                            livelloTitoloRilevato = livelloParse
                            newPara.HeadingLevel = livelloParse
                        End If
                    End If
                End If
                Dim outlineLvlEl As XElement = pPr.Element(NsW + "outlineLvl")
                If outlineLvlEl IsNot Nothing AndAlso outlineLvlEl.Attribute(NsW + "val") IsNot Nothing Then
                    Dim outVal As Integer
                    If Integer.TryParse(outlineLvlEl.Attribute(NsW + "val").Value, outVal) Then
                        newPara.OutlineLevel = outVal
                        If livelloTitoloRilevato = 0 AndAlso outVal >= 0 AndAlso outVal <= 5 Then
                            livelloTitoloRilevato = outVal + 1
                            newPara.HeadingLevel = livelloTitoloRilevato
                        End If
                    End If
                End If
                Dim jc As XElement = pPr.Element(NsW + "jc")
                If jc IsNot Nothing AndAlso jc.Attribute(NsW + "val") IsNot Nothing Then
                    Select Case jc.Attribute(NsW + "val").Value.ToLower()
                        Case "center" : newPara.Alignment = "Center"
                        Case "right" : newPara.Alignment = "Right"
                        Case "both" : newPara.Alignment = "Justify"
                        Case Else : newPara.Alignment = "Left"
                    End Select
                End If
                Dim ind As XElement = pPr.Element(NsW + "ind")
                If ind IsNot Nothing AndAlso ind.Attribute(NsW + "left") IsNot Nothing Then
                    Dim leftVal As Integer
                    If Integer.TryParse(ind.Attribute(NsW + "left").Value, leftVal) Then
                        newPara.IndentLevel = CInt(System.Math.Floor(leftVal / 720.0))
                    End If
                End If
                Dim spacing As XElement = pPr.Element(NsW + "spacing")
                If spacing IsNot Nothing Then
                    Dim v As Integer = 0
                    If spacing.Attribute(NsW + "before") IsNot Nothing AndAlso Integer.TryParse(spacing.Attribute(NsW + "before").Value, v) Then newPara.SpacingBefore = System.Math.Max(0, v / 20.0)
                    v = 0
                    If spacing.Attribute(NsW + "after") IsNot Nothing AndAlso Integer.TryParse(spacing.Attribute(NsW + "after").Value, v) Then newPara.SpacingAfter = System.Math.Max(0, v / 20.0)
                    v = 0
                    If spacing.Attribute(NsW + "line") IsNot Nothing AndAlso Integer.TryParse(spacing.Attribute(NsW + "line").Value, v) AndAlso v > 0 Then newPara.LineSpacing = CSng(System.Math.Round(v / 240.0, 2))
                End If
                Dim numPr As XElement = pPr.Element(NsW + "numPr")
                If numPr IsNot Nothing Then
                    newPara.IsListItem = True
                    Dim numIdEl As XElement = numPr.Element(NsW + "numId")
                    If numIdEl IsNot Nothing AndAlso numIdEl.Attribute(NsW + "val") IsNot Nothing Then
                        Dim numIdVal As Integer
                        If Integer.TryParse(numIdEl.Attribute(NsW + "val").Value, numIdVal) Then
                            newPara.NumberingId = numIdVal
                            If numIdVal = 2 Then newPara.ListNumValue = "decimal"
                        End If
                    End If
                    Dim ilvlEl As XElement = numPr.Element(NsW + "ilvl")
                    If ilvlEl IsNot Nothing AndAlso ilvlEl.Attribute(NsW + "val") IsNot Nothing Then
                        Dim ilvlVal As Integer
                        If Integer.TryParse(ilvlEl.Attribute(NsW + "val").Value, ilvlVal) Then newPara.NumberingLevel = ilvlVal
                    End If
                End If
                If pPr.Element(NsW + "pageBreakBefore") IsNot Nothing Then
                    newPara.PageBreakBefore = True
                    newPara.IsPageBreak = True
                End If
                If pPr.Element(NsW + "keepNext") IsNot Nothing Then newPara.KeepWithNext = True
                If pPr.Element(NsW + "keepLines") IsNot Nothing Then newPara.KeepLines = True
                For Each tabEl As XElement In pPr.Elements(NsW + "tabs")
                    For Each tabStop As XElement In tabEl.Elements(NsW + "tab")
                        If tabStop.Attribute(NsW + "pos") IsNot Nothing Then
                            Dim posVal As Integer
                            If Integer.TryParse(tabStop.Attribute(NsW + "pos").Value, posVal) AndAlso posVal > 0 Then
                                newPara.TabStops.Add(posVal)
                            End If
                        End If
                    Next
                Next
            End If

            If Not newPara.IsPageBreak Then
                For Each brEl As XElement In p.Descendants(NsW + "br")
                    Dim typeAttr = brEl.Attribute(NsW + "type")
                    If typeAttr IsNot Nothing AndAlso typeAttr.Value = "page" Then
                        newPara.IsPageBreak = True
                        Exit For
                    End If
                Next
            End If

            Dim idsCommentiAssociati As New List(Of Integer)
            If mappaCommenti IsNot Nothing AndAlso mappaCommenti.Count > 0 Then
                For Each crsEl As XElement In p.Elements(NsW + "commentRangeStart")
                    Dim idAttr = crsEl.Attribute(NsW + "id")
                    If idAttr IsNot Nothing Then
                        Dim idVal As Integer
                        If Integer.TryParse(idAttr.Value, idVal) Then idsCommentiAssociati.Add(idVal)
                    End If
                Next
            End If

            Try
                For Each bkEl As XElement In p.Elements(NsW + "bookmarkStart")
                    Dim nomeAttr = bkEl.Attribute(NsW + "name")
                    If nomeAttr IsNot Nothing AndAlso Not String.IsNullOrEmpty(nomeAttr.Value) Then
                        Dim nomeBk As String = nomeAttr.Value
                        If Not nomeBk.Equals("_GoBack", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrEmpty(newPara.BookmarkId) Then
                            newPara.BookmarkId = nomeBk
                        End If
                    End If
                Next
            Catch exBk As Exception
                logger.ScriviLog($"ParseParagraphConMetadati bookmarkStart: {exBk.Message}")
            End Try

            If Not newPara.IsPageBreak Then
                Dim runsDaElaborare As New List(Of (Elemento As XElement, IsHyperlink As Boolean, LinkTarget As String))()

                For Each el As XElement In p.Elements()
                    Try
                        If el.Name = NsW + "r" Then
                            EstraiRunDaTxbx(el, immaginiMedia, mappaRelazioni, newPara)
                            If Not ContieneSoloTxbx(el) Then runsDaElaborare.Add((el, False, ""))
                        ElseIf el.Name = NsW + "hyperlink" Then
                            Dim targetLink As String = ""
                            Dim anchorAttr = el.Attribute(NsW + "anchor")
                            If anchorAttr IsNot Nothing AndAlso Not String.IsNullOrEmpty(anchorAttr.Value) Then
                                targetLink = anchorAttr.Value
                            Else
                                Dim ridAttr = el.Attribute(NsR + "id")
                                If ridAttr IsNot Nothing AndAlso mappaRelazioni IsNot Nothing AndAlso mappaRelazioni.ContainsKey(ridAttr.Value) Then
                                    targetLink = mappaRelazioni(ridAttr.Value)
                                End If
                            End If
                            For Each r As XElement In el.Elements(NsW + "r")
                                EstraiRunDaTxbx(r, immaginiMedia, mappaRelazioni, newPara)
                                If Not ContieneSoloTxbx(r) Then runsDaElaborare.Add((r, True, targetLink))
                            Next
                        ElseIf el.Name = NsMc + "AlternateContent" Then
                            Dim sorgente As XElement = el.Element(NsMc + "Choice")
                            If sorgente Is Nothing Then sorgente = el.Element(NsMc + "Fallback")
                            If sorgente IsNot Nothing Then
                                For Each r As XElement In sorgente.Descendants(NsW + "r")
                                    EstraiRunDaTxbx(r, immaginiMedia, mappaRelazioni, newPara)
                                    If Not ContieneSoloTxbx(r) Then runsDaElaborare.Add((r, False, ""))
                                Next
                            End If
                        End If
                    Catch exEl As Exception
                        logger.ScriviLog($"ParseParagraphConMetadati element: {exEl.Message}")
                    End Try
                Next

                For Each coppia In runsDaElaborare
                    Try
                        Dim run = ParseRun(coppia.Elemento, immaginiMedia, mappaRelazioni)
                        If run Is Nothing Then Continue For
                        If coppia.IsHyperlink Then
                            run.IsHyperlink = True
                            run.ColorHex = "Blu"
                            run.Underline = True
                            If Not String.IsNullOrEmpty(coppia.LinkTarget) AndAlso String.IsNullOrEmpty(run.LinkUrl) Then
                                run.LinkUrl = coppia.LinkTarget
                            End If
                        End If
                        Dim eRPr As XElement = coppia.Elemento.Element(NsW + "rPr")
                        Dim isCommentRef As Boolean = False
                        If eRPr IsNot Nothing Then
                            Dim rStyleEl As XElement = eRPr.Element(NsW + "rStyle")
                            If rStyleEl IsNot Nothing AndAlso rStyleEl.Attribute(NsW + "val") IsNot Nothing Then
                                If rStyleEl.Attribute(NsW + "val").Value.Equals("CommentReference", StringComparison.OrdinalIgnoreCase) Then isCommentRef = True
                            End If
                        End If
                        If coppia.Elemento.Element(NsW + "commentReference") IsNot Nothing Then isCommentRef = True
                        If Not isCommentRef Then newPara.Runs.Add(run)
                    Catch exRun As Exception
                        logger.ScriviLog($"ParseParagraphConMetadati run: {exRun.Message}")
                    End Try
                Next
            End If

            Try
                For Each fnRefEl As XElement In p.Descendants(NsW + "footnoteReference")
                    Dim idAttr = fnRefEl.Attribute(NsW + "id")
                    If idAttr Is Nothing Then Continue For
                    Dim fnId As Integer
                    If Integer.TryParse(idAttr.Value, fnId) AndAlso fnId > 0 Then
                        Dim runMarcatore As New DocRun() With {
                            .Text = fnId.ToString(),
                            .IsSuperscript = True,
                            .Bold = True,
                            .FontSize = 8,
                            .LinkUrl = "NOTA_PIE_" & fnId
                        }
                        newPara.Runs.Add(runMarcatore)
                    End If
                Next
            Catch exFnInject As Exception
                logger.ScriviLog($"ParseParagraphConMetadati footnoteRef: {exFnInject.Message}")
            End Try

            Try
                For Each enRefEl As XElement In p.Descendants(NsW + "endnoteReference")
                    Dim idAttr = enRefEl.Attribute(NsW + "id")
                    If idAttr Is Nothing Then Continue For
                    Dim enId As Integer
                    If Integer.TryParse(idAttr.Value, enId) AndAlso enId > 0 Then
                        Dim runMarcatore As New DocRun() With {
                            .Text = enId.ToString(),
                            .IsSuperscript = True,
                            .Bold = True,
                            .FontSize = 8,
                            .LinkUrl = "NOTA_FINE_" & enId
                        }
                        newPara.Runs.Add(runMarcatore)
                    End If
                Next
            Catch exEnInject As Exception
                logger.ScriviLog($"ParseParagraphConMetadati endnoteRef: {exEnInject.Message}")
            End Try

            Try
                If Not newPara.IsIndexContainer AndAlso newPara.Runs IsNot Nothing Then
                    For Each rChk As DocRun In newPara.Runs
                        If rChk IsNot Nothing AndAlso Not String.IsNullOrEmpty(rChk.LinkUrl) AndAlso (rChk.LinkUrl.StartsWith("IND_") OrElse rChk.LinkUrl.StartsWith("AUTO_IND_")) Then
                            newPara.IsIndexContainer = True
                            Exit For
                        End If
                    Next
                End If
            Catch exIdx As Exception
                logger.ScriviLog($"ParseParagraphConMetadati indice: {exIdx.Message}")
            End Try

            If livelloTitoloRilevato >= 1 AndAlso stiliTitolo IsNot Nothing Then
                If String.IsNullOrEmpty(newPara.StyleId) Then newPara.StyleId = "STL_AUTO_" & Guid.NewGuid().ToString("N").Substring(0, 8)
                stiliTitolo(newPara.StyleId) = livelloTitoloRilevato
            End If

            If idsCommentiAssociati.Count > 0 AndAlso commentiPerParagrafo IsNot Nothing AndAlso mappaCommenti IsNot Nothing Then
                If String.IsNullOrEmpty(newPara.BookmarkId) Then newPara.BookmarkId = "CMT_AUTO_" & Guid.NewGuid().ToString("N").Substring(0, 8)
                If Not commentiPerParagrafo.ContainsKey(newPara.BookmarkId) Then commentiPerParagrafo(newPara.BookmarkId) = New List(Of String)
                For Each idAss In idsCommentiAssociati
                    If mappaCommenti.ContainsKey(idAss) Then commentiPerParagrafo(newPara.BookmarkId).Add(mappaCommenti(idAss))
                Next
            End If
        Catch ex As Exception
            logger.ScriviLog($"ParseParagraphConMetadati Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return newPara
    End Function

    Private Shared Function ContieneSoloTxbx(r As XElement) As Boolean
        Try
            For Each el As XElement In r.Elements()
                If el.Name = NsW + "rPr" Then Continue For
                If el.Name = NsW + "lastRenderedPageBreak" Then Continue For
                If el.Name = NsMc + "AlternateContent" Then
                    Dim choice As XElement = el.Element(NsMc + "Choice")
                    If choice Is Nothing Then choice = el.Element(NsMc + "Fallback")
                    If choice IsNot Nothing Then
                        For Each drawing As XElement In choice.Descendants(NsW + "drawing")
                            For Each txbx As XElement In drawing.Descendants(NsWps + "txbx")
                                Return True
                            Next
                        Next
                    End If
                ElseIf el.Name = NsW + "drawing" Then
                    For Each txbx As XElement In el.Descendants(NsWps + "txbx")
                        Return True
                    Next
                Else
                    Return False
                End If
            Next
            Return False
        Catch ex As Exception
            logger.ScriviLog($"ContieneSoloTxbx: {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Sub EstraiRunDaTxbx(r As XElement, immaginiMedia As Dictionary(Of String, Byte()), mappaRelazioni As Dictionary(Of String, String), para As DocParagraph)
        Try
            For Each drawing As XElement In r.Descendants(NsW + "drawing")
                For Each txbx As XElement In drawing.Descendants(NsWps + "txbx")
                    For Each txContent As XElement In txbx.Elements(NsW + "txbxContent")
                        For Each innerP As XElement In txContent.Elements(NsW + "p")
                            For Each innerR As XElement In innerP.Elements(NsW + "r")
                                Try
                                    Dim innerDrawing As XElement = innerR.Element(NsW + "drawing")
                                    If innerDrawing Is Nothing Then Continue For
                                    Dim tuttiRid As List(Of String) = EstraiTuttiRelationshipId(innerDrawing)
                                    For Each rId As String In tuttiRid
                                        If String.IsNullOrEmpty(rId) Then Continue For
                                        If mappaRelazioni Is Nothing OrElse Not mappaRelazioni.ContainsKey(rId) Then Continue For
                                        If immaginiMedia Is Nothing OrElse Not immaginiMedia.ContainsKey(rId) Then Continue For
                                        Dim bytes As Byte() = immaginiMedia(rId)
                                        If bytes Is Nothing OrElse bytes.Length = 0 Then Continue For
                                        Dim imgRun As New DocRun()
                                        imgRun.IsImage = True
                                        imgRun.OriginalRId = rId
                                        imgRun.ImageBytes = bytes
                                        imgRun.RawContent = innerDrawing.ToString()
                                        Dim xmlImg As String = imgRun.RawContent
                                        Dim mCx = Regex.Match(xmlImg, "cx=""(\d+)""", RegexOptions.None, TimeSpan.FromSeconds(2))
                                        Dim mCy = Regex.Match(xmlImg, "cy=""(\d+)""", RegexOptions.None, TimeSpan.FromSeconds(2))
                                        If mCx.Success AndAlso mCy.Success Then
                                            Dim cx As Long, cy As Long
                                            If Long.TryParse(mCx.Groups(1).Value, cx) AndAlso Long.TryParse(mCy.Groups(1).Value, cy) Then
                                                imgRun.ImageWidthPt = cx / 12700.0
                                                imgRun.ImageHeightPt = cy / 12700.0
                                            End If
                                        End If
                                        para.Runs.Add(imgRun)
                                        Exit For
                                    Next
                                Catch exInner As Exception
                                    logger.ScriviLog($"EstraiRunDaTxbx innerR: {exInner.Message}")
                                End Try
                            Next
                        Next
                    Next
                Next
            Next
        Catch ex As Exception
            logger.ScriviLog($"EstraiRunDaTxbx: {ex.Message}")
        End Try
    End Sub

    Private Shared Function ParseRun(r As XElement, immaginiMedia As Dictionary(Of String, Byte()), mappaRelazioni As Dictionary(Of String, String)) As DocRun
        Dim newRun As New DocRun()
        Try
            Dim rPr As XElement = r.Element(NsW + "rPr")
            If rPr IsNot Nothing Then
                For Each bEl As XElement In rPr.Elements(NsW + "b")
                    newRun.Bold = True
                    Exit For
                Next
                For Each iEl As XElement In rPr.Elements(NsW + "i")
                    newRun.Italic = True
                    Exit For
                Next
                For Each uEl As XElement In rPr.Elements(NsW + "u")
                    newRun.Underline = True
                    Exit For
                Next
                For Each stEl As XElement In rPr.Elements(NsW + "strike")
                    newRun.Strikethrough = True
                    Exit For
                Next
                Dim vertAlign As XElement = rPr.Element(NsW + "vertAlign")
                If vertAlign IsNot Nothing AndAlso vertAlign.Attribute(NsW + "val") IsNot Nothing Then
                    Dim vertVal As String = vertAlign.Attribute(NsW + "val").Value.ToLower()
                    newRun.IsSuperscript = (vertVal = "superscript")
                    newRun.IsSubscript = (vertVal = "subscript")
                End If
                Dim rFonts As XElement = rPr.Element(NsW + "rFonts")
                If rFonts IsNot Nothing Then
                    Dim fontAttr = If(rFonts.Attribute(NsW + "ascii"), rFonts.Attribute(NsW + "hAnsi"))
                    If fontAttr IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(fontAttr.Value) Then newRun.FontFamily = fontAttr.Value.Trim()
                End If
                Dim colorEl As XElement = rPr.Element(NsW + "color")
                If colorEl IsNot Nothing AndAlso colorEl.Attribute(NsW + "val") IsNot Nothing Then newRun.ColorHex = ConvertHexToNameColor(colorEl.Attribute(NsW + "val").Value)
                Dim hlEl As XElement = rPr.Element(NsW + "highlight")
                If hlEl IsNot Nothing AndAlso hlEl.Attribute(NsW + "val") IsNot Nothing Then newRun.HighlightColorHex = ConvertHighlightToNameColor(hlEl.Attribute(NsW + "val").Value)
                Dim szEl As XElement = rPr.Element(NsW + "sz")
                If szEl IsNot Nothing AndAlso szEl.Attribute(NsW + "val") IsNot Nothing Then
                    Dim val As Integer
                    If Integer.TryParse(szEl.Attribute(NsW + "val").Value, val) AndAlso val > 0 Then newRun.FontSize = CSng(val / 2.0F)
                End If
                Dim rStyleEl As XElement = rPr.Element(NsW + "rStyle")
                If rStyleEl IsNot Nothing AndAlso rStyleEl.Attribute(NsW + "val") IsNot Nothing Then newRun.StyleId = rStyleEl.Attribute(NsW + "val").Value
                If rPr.Element(NsW + "smallCaps") IsNot Nothing Then newRun.SmallCaps = True
                If rPr.Element(NsW + "caps") IsNot Nothing Then newRun.AllCaps = True
                If rPr.Element(NsW + "vanish") IsNot Nothing Then newRun.Hidden = True
                Dim langEl As XElement = rPr.Element(NsW + "lang")
                If langEl IsNot Nothing Then
                    Dim langAttr = If(langEl.Attribute(NsW + "val"), langEl.Attribute(NsW + "bidi"))
                    If langAttr IsNot Nothing AndAlso Not String.IsNullOrEmpty(langAttr.Value) Then newRun.Language = langAttr.Value
                End If
                Dim spacingEl As XElement = rPr.Element(NsW + "spacing")
                If spacingEl IsNot Nothing AndAlso spacingEl.Attribute(NsW + "val") IsNot Nothing Then
                    Dim spcVal As Integer
                    If Integer.TryParse(spacingEl.Attribute(NsW + "val").Value, spcVal) Then newRun.SpacingPt = spcVal / 20.0
                End If
                Dim wEl As XElement = rPr.Element(NsW + "w")
                If wEl IsNot Nothing AndAlso wEl.Attribute(NsW + "val") IsNot Nothing Then
                    Dim scaleVal As Integer
                    If Integer.TryParse(wEl.Attribute(NsW + "val").Value, scaleVal) AndAlso scaleVal > 0 Then newRun.ScalePercent = scaleVal
                End If
            End If

            Dim t As XElement = r.Element(NsW + "t")
            If t IsNot Nothing Then
                newRun.Text = t.Value
                If rPr IsNot Nothing Then newRun.RawProperties = rPr.ToString()
                Return newRun
            End If

            Dim drawing As XElement = r.Element(NsW + "drawing")
            If drawing Is Nothing Then
                For Each mcEl As XElement In r.Elements(NsMc + "AlternateContent")
                    Dim choice As XElement = mcEl.Element(NsMc + "Choice")
                    If choice Is Nothing Then choice = mcEl.Element(NsMc + "Fallback")
                    If choice IsNot Nothing Then
                        drawing = choice.Element(NsW + "drawing")
                        If drawing IsNot Nothing Then Exit For
                        For Each dDesc As XElement In choice.Descendants(NsW + "drawing")
                            drawing = dDesc
                            Exit For
                        Next
                        If drawing IsNot Nothing Then Exit For
                    End If
                Next
            End If
            Dim pict As XElement = r.Element(NsW + "pict")
            If pict Is Nothing Then
                For Each mcEl As XElement In r.Elements(NsMc + "AlternateContent")
                    Dim fallback As XElement = mcEl.Element(NsMc + "Fallback")
                    If fallback IsNot Nothing Then
                        pict = fallback.Element(NsW + "pict")
                        If pict IsNot Nothing Then Exit For
                    End If
                Next
            End If
            Dim obj As XElement = r.Element(NsW + "object")

            If drawing IsNot Nothing OrElse pict IsNot Nothing OrElse obj IsNot Nothing Then
                newRun.IsImage = True
                Dim content As New StringBuilder()
                If drawing IsNot Nothing Then content.Append(drawing.ToString())
                If pict IsNot Nothing Then content.Append(pict.ToString())
                If obj IsNot Nothing Then content.Append(obj.ToString())
                newRun.RawContent = content.ToString()

                Dim tuttiRid As List(Of String) = EstraiTuttiRelationshipId(drawing, pict, obj)

                For Each rId As String In tuttiRid
                    If String.IsNullOrEmpty(rId) Then Continue For
                    If mappaRelazioni Is Nothing OrElse Not mappaRelazioni.ContainsKey(rId) Then Continue For
                    Try
                        If immaginiMedia IsNot Nothing AndAlso immaginiMedia.ContainsKey(rId) Then
                            Dim bytesGrezzi As Byte() = immaginiMedia(rId)
                            If bytesGrezzi IsNot Nothing AndAlso bytesGrezzi.Length > 0 Then
                                newRun.OriginalRId = rId
                                newRun.ImageBytes = bytesGrezzi

                                Dim xmlImg As String = content.ToString()
                                Dim mCx = Regex.Match(xmlImg, "cx=""(\d+)""")
                                Dim mCy = Regex.Match(xmlImg, "cy=""(\d+)""")
                                If mCx.Success AndAlso mCy.Success Then
                                    Dim cx As Long, cy As Long
                                    If Long.TryParse(mCx.Groups(1).Value, cx) AndAlso Long.TryParse(mCy.Groups(1).Value, cy) Then
                                        newRun.ImageWidthPt = cx / 12700.0
                                        newRun.ImageHeightPt = cy / 12700.0
                                    End If
                                End If
                                Exit For
                            End If
                        Else
                        End If
                    Catch exImg As Exception
                        logger.ScriviLog($"ParseRun: errore lettura immagine rId='{rId}'. Errore: {exImg.Message}{Environment.NewLine}{exImg.StackTrace}")
                    End Try
                Next

                Return newRun
            End If

        Catch ex As Exception
            logger.ScriviLog($"ParseRun Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return Nothing
    End Function

    Private Shared Function EstraiTuttiRelationshipId(ParamArray elementi As XElement()) As List(Of String)
        Dim risultati As New List(Of String)()
        Try
            Dim candidatiAttributo As String() = {"embed", "id", "link", "href"}

            For Each elemento As XElement In elementi
                If elemento Is Nothing Then Continue For
                Try
                    For Each discendente As XElement In elemento.DescendantsAndSelf()
                        For Each nomeAttr As String In candidatiAttributo
                            Dim valoreR As String = discendente.Attribute(NsR + nomeAttr)?.Value
                            If Not String.IsNullOrEmpty(valoreR) AndAlso valoreR.StartsWith("rId") AndAlso Not risultati.Contains(valoreR) Then
                                risultati.Add(valoreR)
                            End If
                        Next

                        For Each attr As XAttribute In discendente.Attributes()
                            Dim nomeLocale As String = attr.Name.LocalName.ToLower()
                            If (nomeLocale = "embed" OrElse nomeLocale = "id" OrElse nomeLocale = "link") AndAlso
                               attr.Value.StartsWith("rId") AndAlso Not risultati.Contains(attr.Value) Then
                                risultati.Add(attr.Value)
                            End If
                        Next
                    Next
                Catch exEl As Exception
                    logger.ScriviLog($"EstraiTuttiRelationshipId element: {exEl.Message}")
                End Try
            Next

            If risultati.Count = 0 Then
                Dim xml As New StringBuilder()
                For Each el As XElement In elementi
                    If el IsNot Nothing Then xml.Append(el.ToString())
                Next
                Dim testoXml As String = xml.ToString()
                For Each m As Match In Regex.Matches(testoXml, "rId\d+", RegexOptions.None, TimeSpan.FromSeconds(2))
                    Dim v As String = m.Value
                    If Not risultati.Contains(v) Then risultati.Add(v)
                Next
            End If

        Catch ex As Exception
            logger.ScriviLog($"EstraiTuttiRelationshipId: errore generale. Errore: {ex.Message}")
        End Try
        Return risultati
    End Function

    Private Shared Function ParseTable(tbl As XElement, immaginiMedia As Dictionary(Of String, Byte()), mappaRelazioni As Dictionary(Of String, String)) As DocTable
        Dim newTable As New DocTable()
        Try
            For Each tr As XElement In tbl.Elements(NsW + "tr")
                Dim newRow As New DocTableRow()
                Dim trPr As XElement = tr.Element(NsW + "trPr")
                If trPr IsNot Nothing Then
                    For Each thEl As XElement In trPr.Elements(NsW + "tblHeader")
                        newRow.IsHeader = True
                        Exit For
                    Next
                End If
                For Each tc As XElement In tr.Elements(NsW + "tc")
                    Dim newCell As New DocTableCell()
                    Dim tcPr As XElement = tc.Element(NsW + "tcPr")
                    If tcPr IsNot Nothing Then
                        Dim tcW As XElement = tcPr.Element(NsW + "tcW")
                        If tcW IsNot Nothing AndAlso tcW.Attribute(NsW + "w") IsNot Nothing Then
                            Dim wVal As Integer = 0
                            If Integer.TryParse(tcW.Attribute(NsW + "w").Value, wVal) Then newCell.WidthTwips = System.Math.Max(0, wVal)
                        End If
                        Dim gridSpan As XElement = tcPr.Element(NsW + "gridSpan")
                        If gridSpan IsNot Nothing AndAlso gridSpan.Attribute(NsW + "val") IsNot Nothing Then
                            Dim gs As Integer = 1
                            If Integer.TryParse(gridSpan.Attribute(NsW + "val").Value, gs) Then newCell.ColSpan = System.Math.Max(1, gs)
                        End If
                        Dim vMerge As XElement = tcPr.Element(NsW + "vMerge")
                        If vMerge IsNot Nothing Then
                            Dim mergeAttr = vMerge.Attribute(NsW + "val")
                            If mergeAttr IsNot Nothing AndAlso mergeAttr.Value = "restart" Then newCell.RowSpan = 2
                        End If
                        Dim shd As XElement = tcPr.Element(NsW + "shd")
                        If shd IsNot Nothing AndAlso shd.Attribute(NsW + "fill") IsNot Nothing Then newCell.ShadingColorHex = ConvertHexToNameColor(shd.Attribute(NsW + "fill").Value)
                        newCell.IsHeader = newRow.IsHeader
                    End If
                    For Each p As XElement In tc.Elements(NsW + "p")
                        newCell.Paragraphs.Add(ParseParagraph(p, immaginiMedia, mappaRelazioni))
                    Next
                    If newCell.Paragraphs.Count = 0 Then newCell.Paragraphs.Add(New DocParagraph())
                    newRow.Cells.Add(newCell)
                Next
                newTable.Rows.Add(newRow)
            Next
        Catch ex As Exception
            logger.ScriviLog($"ParseTable Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return newTable
    End Function

    Private Shared Function ConvertaImmaginePerPdf(dati As Byte()) As Byte()
        Try
            If dati Is Nothing OrElse dati.Length = 0 Then Return dati

            Using ms As New MemoryStream(dati)
                Using img As System.Drawing.Image = System.Drawing.Image.FromStream(ms)
                    Dim dpiTarget As Single = 150
                    Dim w As Integer = CInt(img.Width * dpiTarget / System.Math.Max(1, img.HorizontalResolution))
                    Dim h As Integer = CInt(img.Height * dpiTarget / System.Math.Max(1, img.VerticalResolution))
                    If w <= 0 Then w = 800
                    If h <= 0 Then h = 600
                    If w > 4000 Then w = 4000
                    If h > 4000 Then h = 4000

                    Using bmp As New System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb)
                        bmp.SetResolution(dpiTarget, dpiTarget)
                        Using g As System.Drawing.Graphics = System.Drawing.Graphics.FromImage(bmp)
                            g.Clear(System.Drawing.Color.White)
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality
                            g.DrawImage(img, 0, 0, w, h)
                        End Using

                        Dim codecJpeg As System.Drawing.Imaging.ImageCodecInfo = Nothing
                        For Each codec As System.Drawing.Imaging.ImageCodecInfo In System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                            If codec.MimeType = "image/jpeg" Then
                                codecJpeg = codec
                                Exit For
                            End If
                        Next
                        If codecJpeg Is Nothing Then Return dati

                        Dim parametriJpeg As New System.Drawing.Imaging.EncoderParameters(1)
                        parametriJpeg.Param(0) = New System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L)

                        Using msOut As New MemoryStream()
                            bmp.Save(msOut, codecJpeg, parametriJpeg)
                            Return msOut.ToArray()
                        End Using
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"ConvertaImmaginePerPdf: errore. Errore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return dati
        End Try
    End Function

    Private Shared Function IsXmlSafe(xml As String) As Boolean
        If String.IsNullOrEmpty(xml) Then Return True
        Try
            Dim settings As New System.Xml.XmlReaderSettings()
            settings.DtdProcessing = System.Xml.DtdProcessing.Prohibit
            settings.ConformanceLevel = System.Xml.ConformanceLevel.Fragment
            settings.MaxCharactersInDocument = 5_000_000L
            Using sr As New System.IO.StringReader("<root>" & xml & "</root>")
                Using xr As System.Xml.XmlReader = System.Xml.XmlReader.Create(sr, settings)
                    While xr.Read()
                    End While
                End Using
            End Using
            Return True
        Catch exXmlSafe As Exception
            logger.ScriviLog($"IsXmlSafe: frammento scartato come non sicuro. Errore: {exXmlSafe.Message}")
            Return False
        End Try
    End Function

    Private Shared Function ConvertNameColorToHex(name As String) As String
        If String.IsNullOrEmpty(name) Then Return "000000"
        Select Case name.Trim().ToLower()
            Case "nero" : Return "000000"
            Case "bianco" : Return "FFFFFF"
            Case "rosso" : Return "FF0000"
            Case "verde" : Return "00FF00"
            Case "blu" : Return "0000FF"
            Case "giallo" : Return "FFFF00"
            Case "arancione" : Return "FFA500"
            Case "viola" : Return "800080"
            Case "grigio" : Return "808080"
            Case "marrone" : Return "A52A2A"
            Case "celeste" : Return "ADD8E6"
            Case "rosa" : Return "FFC0CB"
            Case "nessuno" : Return "auto"
            Case Else : Return "000000"
        End Select
    End Function

    Private Shared Function ConvertHexToNameColor(hex As String) As String
        If String.IsNullOrEmpty(hex) OrElse hex.ToLower() = "auto" Then Return "Nero"
        Select Case hex.ToUpper()
            Case "000000" : Return "Nero"
            Case "FFFFFF" : Return "Bianco"
            Case "FF0000" : Return "Rosso"
            Case "00FF00", "008000" : Return "Verde"
            Case "0000FF", "0563C1" : Return "Blu"
            Case "FFFF00" : Return "Giallo"
            Case "FFA500" : Return "Arancione"
            Case "800080" : Return "Viola"
            Case "808080" : Return "Grigio"
            Case "A52A2A" : Return "Marrone"
            Case "ADD8E6" : Return "Celeste"
            Case "FFC0CB" : Return "Rosa"
            Case Else : Return "Nero"
        End Select
    End Function

    Private Shared Function ConvertNameColorToHighlight(name As String) As String
        If String.IsNullOrEmpty(name) Then Return "none"
        Select Case name.Trim().ToLower()
            Case "nero" : Return "black"
            Case "bianco" : Return "white"
            Case "rosso" : Return "red"
            Case "verde" : Return "green"
            Case "blu" : Return "blue"
            Case "giallo" : Return "yellow"
            Case "viola" : Return "magenta"
            Case "grigio" : Return "lightGray"
            Case "celeste" : Return "cyan"
            Case "nessuno" : Return "none"
            Case Else : Return "none"
        End Select
    End Function

    Private Shared Function ConvertHighlightToNameColor(hl As String) As String
        If String.IsNullOrEmpty(hl) Then Return "Nessuno"
        Select Case hl.Trim().ToLower()
            Case "black" : Return "Nero"
            Case "white" : Return "Bianco"
            Case "red", "darkred" : Return "Rosso"
            Case "green", "darkgreen" : Return "Verde"
            Case "blue", "darkblue" : Return "Blu"
            Case "yellow", "darkyellow" : Return "Giallo"
            Case "magenta", "darkmagenta" : Return "Viola"
            Case "lightgray", "darkgray" : Return "Grigio"
            Case "cyan", "darkcyan" : Return "Celeste"
            Case "none" : Return "Nessuno"
            Case Else : Return "Nessuno"
        End Select
    End Function

    ' ===================== Gestione colori di tema (docx e odt) =====================
    ' Palette attiva per il documento correntemente in caricamento/salvataggio: mappa i 12
    ' ruoli di colore di tema (stessi nomi usati in OOXML a:clrScheme: dk1, lt1, dk2, lt2,
    ' accent1-6, hlink, folHlink) al valore RGB esadecimale corrente.
    ' Viene riportata alla palette di default "WinGabriel" a inizio caricamento
    ' (ImpostaPaletteTemaDefault, chiamata da CaricaDocumentoInStato); se il file sorgente
    ' contiene un proprio tema viene sovrascritta con i valori letti dal file (sia per il
    ' .docx da word/theme/theme1.xml, sia per l'.odt da loext:theme in styles.xml).
    ' Se il file è nuovo, o non aveva un tema proprio, resta quella di default e viene
    ' scritta come tale nel documento salvato: il salvataggio del tema è quindi sempre
    ' presente, per entrambi i formati.
    Private Shared ReadOnly RuoliTema As String() = {"dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"}

    Private Shared _paletteTemaCorrente As Dictionary(Of String, String) = Nothing

    Private Shared Function PaletteTemaDiDefault() As Dictionary(Of String, String)
        Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"dk1", "000000"}, {"lt1", "FFFFFF"}, {"dk2", "44546A"}, {"lt2", "E7E6E6"},
            {"accent1", "4472C4"}, {"accent2", "ED7D31"}, {"accent3", "A5A5A5"},
            {"accent4", "FFC000"}, {"accent5", "5B9BD5"}, {"accent6", "70AD47"},
            {"hlink", "0563C1"}, {"folHlink", "954F72"}
        }
    End Function

    ''' <summary>
    ''' Da chiamare a inizio caricamento (sia .docx che .odt) per azzerare la palette di
    ''' tema attiva sui valori di default, prima di provare a leggere un eventuale tema
    ''' presente nel file sorgente. Se il file non ne ha uno, questa resta la palette usata
    ''' anche in fase di salvataggio.
    ''' </summary>
    Private Shared Sub ImpostaPaletteTemaDefault()
        _paletteTemaCorrente = PaletteTemaDiDefault()
    End Sub

    Private Shared Function PaletteTemaAttiva() As Dictionary(Of String, String)
        If _paletteTemaCorrente Is Nothing Then _paletteTemaCorrente = PaletteTemaDiDefault()
        Return _paletteTemaCorrente
    End Function

    ''' <summary>Legge la clrScheme da un word/theme/theme1.xml OOXML e la trasforma nella palette interna.</summary>
    Private Shared Function PaletteTemaDaXmlOoxml(xTema As XDocument) As Dictionary(Of String, String)
        Dim palette As Dictionary(Of String, String) = PaletteTemaDiDefault()
        Try
            Dim nsA As XNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main"
            Dim clrScheme As XElement = xTema.Descendants(nsA + "clrScheme").FirstOrDefault()
            If clrScheme IsNot Nothing Then
                For Each ruolo In RuoliTema
                    Dim elRuolo As XElement = clrScheme.Element(nsA + ruolo)
                    If elRuolo IsNot Nothing Then
                        Dim srgb As XElement = elRuolo.Element(nsA + "srgbClr")
                        Dim sys As XElement = elRuolo.Element(nsA + "sysClr")
                        If srgb IsNot Nothing AndAlso srgb.Attribute("val") IsNot Nothing Then
                            palette(ruolo) = srgb.Attribute("val").Value.ToUpperInvariant()
                        ElseIf sys IsNot Nothing AndAlso sys.Attribute("lastClr") IsNot Nothing Then
                            palette(ruolo) = sys.Attribute("lastClr").Value.ToUpperInvariant()
                        End If
                    End If
                Next
            End If
        Catch exPal As Exception
            logger.ScriviLog("PaletteTemaDaXmlOoxml: " & exPal.Message)
        End Try
        Return palette
    End Function

    ' Corrispondenza fra i nomi di ruolo usati da LibreOffice nell'estensione loext:theme
    ' (styles.xml, dentro <style:master-page>) e i nomi interni (allineati a OOXML a:clrScheme).
    ' NOTA: verificata sulla documentazione pubblica del progetto LibreOffice; la sintassi
    ' loext è un'estensione non standard OASIS e può differire fra versioni di LibreOffice.
    ' Se in un file reale risultasse diversa, va aggiornata solo questa mappa e/o il parsing
    ' in PaletteTemaDaMasterPageOdt: il resto del codice non dipende dalla sintassi esatta.
    Private Shared ReadOnly MappaRuoliOdt As Dictionary(Of String, String) = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
        {"dark1", "dk1"}, {"light1", "lt1"}, {"dark2", "dk2"}, {"light2", "lt2"},
        {"accent1", "accent1"}, {"accent2", "accent2"}, {"accent3", "accent3"},
        {"accent4", "accent4"}, {"accent5", "accent5"}, {"accent6", "accent6"},
        {"hyperlink", "hlink"}, {"followedhyperlink", "folHlink"}
    }

    ''' <summary>
    ''' Legge l'estensione loext:theme (LibreOffice) da un master-page di styles.xml.
    ''' Se il tag non è presente (file creato da altro software, o versione LibreOffice
    ''' priva del tema) la palette restituita è quella di default.
    ''' </summary>
    Private Shared Function PaletteTemaDaMasterPageOdt(masterPage As XElement) As Dictionary(Of String, String)
        Dim palette As Dictionary(Of String, String) = PaletteTemaDiDefault()
        Try
            If masterPage Is Nothing Then Return palette
            Dim temaEl As XElement = masterPage.Element(NsLoext + "theme")
            If temaEl Is Nothing Then Return palette
            For Each colEl As XElement In temaEl.Descendants(NsLoext + "color")
                Dim nomeAttr = colEl.Attribute(NsLoext + "name")
                Dim colAttr = colEl.Attribute(NsLoext + "color")
                If nomeAttr IsNot Nothing AndAlso colAttr IsNot Nothing Then
                    Dim ruoloInterno As String = Nothing
                    If MappaRuoliOdt.TryGetValue(nomeAttr.Value, ruoloInterno) Then
                        palette(ruoloInterno) = colAttr.Value.TrimStart("#"c).ToUpperInvariant()
                    End If
                End If
            Next
        Catch exPal As Exception
            logger.ScriviLog("PaletteTemaDaMasterPageOdt: " & exPal.Message)
        End Try
        Return palette
    End Function

    ''' <summary>Cerca il ruolo di tema il cui colore coincide con l'hex indicato (case-insensitive). Nothing se nessuna corrispondenza.</summary>
    Private Shared Function RuoloTemaPerHex(hex As String, palette As Dictionary(Of String, String)) As String
        If String.IsNullOrEmpty(hex) OrElse palette Is Nothing Then Return Nothing
        Dim hexUp As String = hex.TrimStart("#"c).ToUpperInvariant()
        For Each kvp In palette
            If kvp.Value.Equals(hexUp, StringComparison.OrdinalIgnoreCase) Then Return kvp.Key
        Next
        Return Nothing
    End Function

    Private Shared Function HexPerRuoloTema(ruolo As String, palette As Dictionary(Of String, String)) As String
        If String.IsNullOrEmpty(ruolo) OrElse palette Is Nothing Then Return Nothing
        Dim valore As String = Nothing
        If palette.TryGetValue(ruolo, valore) Then Return valore
        Return Nothing
    End Function

    Private Shared Function ThemeColorValuePerRuolo(ruolo As String) As OX.ThemeColorValues?
        Select Case ruolo
            Case "dk1" : Return OX.ThemeColorValues.Dark1
            Case "lt1" : Return OX.ThemeColorValues.Light1
            Case "dk2" : Return OX.ThemeColorValues.Dark2
            Case "lt2" : Return OX.ThemeColorValues.Light2
            Case "accent1" : Return OX.ThemeColorValues.Accent1
            Case "accent2" : Return OX.ThemeColorValues.Accent2
            Case "accent3" : Return OX.ThemeColorValues.Accent3
            Case "accent4" : Return OX.ThemeColorValues.Accent4
            Case "accent5" : Return OX.ThemeColorValues.Accent5
            Case "accent6" : Return OX.ThemeColorValues.Accent6
            Case "hlink" : Return OX.ThemeColorValues.Hyperlink
            Case "folHlink" : Return OX.ThemeColorValues.FollowedHyperlink
            Case Else : Return Nothing
        End Select
    End Function

    ''' <summary>Nome loext (LibreOffice) del ruolo, per scrivere il riferimento "punta al tema" nei run del testo ODT.</summary>
    Private Shared Function NomeLoextPerRuolo(ruolo As String) As String
        Select Case ruolo
            Case "dk1" : Return "dark1"
            Case "lt1" : Return "light1"
            Case "dk2" : Return "dark2"
            Case "lt2" : Return "light2"
            Case "hlink" : Return "hyperlink"
            Case "folHlink" : Return "followedHyperlink"
            Case Else : Return ruolo ' accent1..accent6 hanno lo stesso nome nei due formati
        End Select
    End Function

    ''' <summary>
    ''' Genera il blocco XML del tema colori da inserire nel master-page di styles.xml
    ''' (estensione loext:theme). Usa sempre la palette passata: se il documento proviene
    ''' da un .odt che aveva già un proprio tema, sarà quella letta al caricamento; se il
    ''' documento è nuovo (o il sorgente non aveva un tema), sarà la palette di default
    ''' "WinGabriel" — il tema è quindi sempre presente nel file salvato.
    ''' </summary>
    Private Shared Function XmlTemaOdt(palette As Dictionary(Of String, String)) As String
        Dim sb As New StringBuilder()
        sb.Append("<loext:theme loext:name=""WinGabriel""><loext:theme-colors>")
        For Each ruolo In RuoliTema
            sb.Append("<loext:color loext:name=""").Append(NomeLoextPerRuolo(ruolo)).Append(""" loext:color=""").Append(HexPerRuoloTema(ruolo, palette)).Append("""/>")
        Next
        sb.Append("</loext:theme-colors></loext:theme>")
        Return sb.ToString()
    End Function
    ' =================== Fine gestione colori di tema ===================

    Private Shared Function OdtEsc(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return System.Security.SecurityElement.Escape(testo)
        Catch ex As Exception
            logger.ScriviLog($"OdtEsc: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function OdtTestoRun(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim sb As New StringBuilder()
            Dim spazi As Integer = 0
            For Each ch As Char In testo
                If ch = " "c Then
                    spazi += 1
                    Continue For
                End If
                If spazi = 1 Then
                    sb.Append(" ")
                ElseIf spazi > 1 Then
                    sb.Append("<text:s text:c=""").Append(spazi).Append("""/>")
                End If
                spazi = 0
                If ch = ChrW(9) Then
                    sb.Append("<text:tab/>")
                ElseIf ch = ChrW(10) Then
                    sb.Append("<text:line-break/>")
                ElseIf ch = ChrW(13) Then
                    Continue For
                Else
                    sb.Append(OdtEsc(ch.ToString()))
                End If
            Next
            If spazi = 1 Then
                sb.Append(" ")
            ElseIf spazi > 1 Then
                sb.Append("<text:s text:c=""").Append(spazi).Append("""/>")
            End If
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog($"OdtTestoRun: {ex.Message}")
            Return OdtEsc(testo)
        End Try
    End Function

    Private Shared Function OdtNomeSegnalibro(id As String) As String
        Try
            If String.IsNullOrEmpty(id) Then Return ""
            Return Regex.Replace(id, "[^A-Za-z0-9_]", "_")
        Catch ex As Exception
            logger.ScriviLog($"OdtNomeSegnalibro: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function OdtOttieniStileTesto(run As DocRun, styleDefs As StringBuilder, cache As Dictionary(Of String, String), ByRef contatore As Integer) As String
        Try
            If run Is Nothing Then Return ""
            Dim fontFamily As String = If(String.IsNullOrWhiteSpace(run.FontFamily), "Arial", run.FontFamily.Trim())
            Dim colorHex As String = ConvertNameColorToHex(run.ColorHex)
            Try
                ' Un run di ipertesto punta sempre al ruolo di tema "hyperlink", come già
                ' avviene per lo stile Hyperlink del .docx (w:themeColor="hyperlink").
                If run.IsHyperlink Then
                    Dim hexHlink As String = HexPerRuoloTema("hlink", PaletteTemaAttiva())
                    If Not String.IsNullOrEmpty(hexHlink) Then colorHex = hexHlink
                End If
            Catch exHlink As Exception
                logger.ScriviLog($"OdtOttieniStileTesto: verifica IsHyperlink fallita. Errore: {exHlink.Message}")
            End Try
            Dim highlightHex As String = ""
            Dim spacingPt As Single = 0
            Dim scalePercent As Integer = 0
            Dim lingua As String = ""
            Dim smallCaps As Boolean = False
            Dim allCaps As Boolean = False
            Dim nascosto As Boolean = False
            Try
                Dim nomeEvid As String = run.HighlightColorHex
                If Not String.IsNullOrEmpty(nomeEvid) AndAlso Not nomeEvid.Equals("Nessuno", StringComparison.OrdinalIgnoreCase) Then
                    highlightHex = ConvertNameColorToHex(nomeEvid)
                End If
                spacingPt = run.SpacingPt
                scalePercent = run.ScalePercent
                lingua = run.Language
                smallCaps = run.SmallCaps
                allCaps = run.AllCaps
                nascosto = run.Hidden
            Catch exProprieta As Exception
                logger.ScriviLog($"OdtOttieniStileTesto: proprietà secondarie non lette completamente. Errore: {exProprieta.Message}")
            End Try

            Dim firma As String = String.Join("|", run.Bold, run.Italic, run.Underline, run.Strikethrough, run.IsSuperscript, run.IsSubscript, fontFamily, run.FontSize, colorHex, highlightHex, spacingPt, scalePercent, lingua, smallCaps, allCaps, nascosto)

            If cache.ContainsKey(firma) Then Return cache(firma)

            contatore += 1
            Dim nomeStile As String = "WGT" & contatore
            cache(firma) = nomeStile

            styleDefs.Append("<style:style style:name=""").Append(nomeStile).Append(""" style:family=""text""><style:text-properties")
            styleDefs.Append(" style:font-name=""").Append(OdtEsc(fontFamily)).Append("""")
            styleDefs.Append(" fo:font-size=""").Append(run.FontSize.ToString(Globalization.CultureInfo.InvariantCulture)).Append("pt""")
            If run.Bold Then styleDefs.Append(" fo:font-weight=""bold""")
            If run.Italic Then styleDefs.Append(" fo:font-style=""italic""")
            If run.Underline Then styleDefs.Append(" style:text-underline-style=""solid"" style:text-underline-width=""auto"" style:text-underline-color=""font-color""")
            If run.Strikethrough Then styleDefs.Append(" style:text-line-through-style=""solid""")
            If run.IsSuperscript Then styleDefs.Append(" style:text-position=""super 58%""")
            If run.IsSubscript Then styleDefs.Append(" style:text-position=""sub 58%""")
            If colorHex <> "000000" AndAlso colorHex <> "auto" Then
                styleDefs.Append(" fo:color=""#").Append(colorHex).Append("""")
                Dim ruoloTemaColore As String = RuoloTemaPerHex(colorHex, PaletteTemaAttiva())
                If Not String.IsNullOrEmpty(ruoloTemaColore) Then styleDefs.Append(" loext:theme-color=""").Append(NomeLoextPerRuolo(ruoloTemaColore)).Append("""")
            End If
            If Not String.IsNullOrEmpty(highlightHex) AndAlso highlightHex <> "auto" Then styleDefs.Append(" fo:background-color=""#").Append(highlightHex).Append("""")
            If spacingPt <> 0 Then styleDefs.Append(" fo:letter-spacing=""").Append(spacingPt.ToString(Globalization.CultureInfo.InvariantCulture)).Append("pt""")
            If scalePercent > 0 AndAlso scalePercent <> 100 Then styleDefs.Append(" style:text-scale=""").Append(scalePercent).Append("%""")
            If Not String.IsNullOrEmpty(lingua) Then styleDefs.Append(" fo:language=""").Append(OdtEsc(lingua)).Append("""")
            If smallCaps Then styleDefs.Append(" fo:font-variant=""small-caps""")
            If nascosto Then styleDefs.Append(" text:display=""none""")
            styleDefs.Append("/></style:style>")

            Return nomeStile
        Catch ex As Exception
            logger.ScriviLog($"OdtOttieniStileTesto: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function OdtOttieniStileParagrafo(p As DocParagraph, styleDefs As StringBuilder, cache As Dictionary(Of String, String), ByRef contatore As Integer) As String
        Try
            If p Is Nothing Then Return ""
            Dim allineamento As String = "start"
            Select Case p.Alignment.ToLower()
                Case "center" : allineamento = "center"
                Case "right" : allineamento = "end"
                Case "justify" : allineamento = "justify"
                Case Else : allineamento = "start"
            End Select

            Dim livelloTitolo As Integer = 0
            Dim keepNext As Boolean = False
            Dim keepLines As Boolean = False
            Dim pageBreakBefore As Boolean = False
            Try
                livelloTitolo = p.HeadingLevel
                keepNext = p.KeepWithNext
                keepLines = p.KeepLines
                pageBreakBefore = p.PageBreakBefore
            Catch exProprieta As Exception
                logger.ScriviLog($"OdtOttieniStileParagrafo: proprietà secondarie non lette completamente. Errore: {exProprieta.Message}")
            End Try

            Dim firma As String = String.Join("|", allineamento, p.IndentLevel, p.LineSpacing, p.SpacingBefore, p.SpacingAfter, keepNext, keepLines, pageBreakBefore, livelloTitolo)

            If cache.ContainsKey(firma) Then Return cache(firma)

            contatore += 1
            Dim nomeStile As String = "WGP" & contatore
            cache(firma) = nomeStile

            Dim parentStile As String = "Standard"
            If livelloTitolo >= 1 AndAlso livelloTitolo <= 6 Then parentStile = "Heading_20_" & livelloTitolo

            styleDefs.Append("<style:style style:name=""").Append(nomeStile).Append(""" style:family=""paragraph"" style:parent-style-name=""").Append(parentStile).Append(""">")
            styleDefs.Append("<style:paragraph-properties fo:text-align=""").Append(allineamento).Append("""")
            If p.IndentLevel > 0 Then
                Dim indentIn As Double = p.IndentLevel * 0.5
                styleDefs.Append(" fo:margin-left=""").Append(indentIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in""")
            End If
            If p.LineSpacing > 0 Then
                Dim percentuale As Integer = CInt(System.Math.Max(100, p.LineSpacing * 100))
                styleDefs.Append(" fo:line-height=""").Append(percentuale).Append("%""")
            End If
            If p.SpacingBefore > 0 Then styleDefs.Append(" fo:margin-top=""").Append(p.SpacingBefore.ToString(Globalization.CultureInfo.InvariantCulture)).Append("pt""")
            If p.SpacingAfter > 0 Then styleDefs.Append(" fo:margin-bottom=""").Append(p.SpacingAfter.ToString(Globalization.CultureInfo.InvariantCulture)).Append("pt""")
            If keepNext Then styleDefs.Append(" fo:keep-with-next=""always""")
            If keepLines Then styleDefs.Append(" fo:keep-together=""always""")
            If pageBreakBefore Then styleDefs.Append(" fo:break-before=""page""")
            styleDefs.Append("/></style:style>")

            Return nomeStile
        Catch ex As Exception
            logger.ScriviLog($"OdtOttieniStileParagrafo: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function OdtOttieniStileCella(cell As DocTableCell, styleDefs As StringBuilder, cache As Dictionary(Of String, String), ByRef contatore As Integer) As String
        Try
            If cell Is Nothing Then Return ""
            Dim sfondoNome As String = ""
            Try
                sfondoNome = cell.ShadingColorHex
            Catch exShading As Exception
                logger.ScriviLog($"OdtOttieniStileCella: ShadingColorHex non leggibile. Errore: {exShading.Message}")
            End Try
            If String.IsNullOrEmpty(sfondoNome) OrElse sfondoNome.Equals("Nessuno", StringComparison.OrdinalIgnoreCase) Then Return "WGCellaBase"

            Dim firma As String = "cella|" & sfondoNome
            If cache.ContainsKey(firma) Then Return cache(firma)

            contatore += 1
            Dim nomeStile As String = "WGTC" & contatore
            cache(firma) = nomeStile

            Dim sfondoHex As String = ConvertNameColorToHex(sfondoNome)
            styleDefs.Append("<style:style style:name=""").Append(nomeStile).Append(""" style:family=""table-cell""><style:table-cell-properties fo:background-color=""#").Append(sfondoHex).Append(""" fo:border=""0.5pt solid #000000"" fo:padding=""0.05in""/></style:style>")

            Return nomeStile
        Catch ex As Exception
            logger.ScriviLog($"OdtOttieniStileCella: {ex.Message}")
            Return ""
        End Try
    End Function

    Private Shared Function EstraiRelationshipIdDaRawContent(rawContent As String) As List(Of String)
        Dim risultati As New List(Of String)
        Try
            If String.IsNullOrEmpty(rawContent) Then Return risultati
            Dim matches = Regex.Matches(rawContent, "rId\d+", RegexOptions.None, TimeSpan.FromSeconds(2))
            For Each m As Match In matches
                If Not risultati.Contains(m.Value) Then risultati.Add(m.Value)
            Next
        Catch ex As Exception
            logger.ScriviLog($"EstraiRelationshipIdDaRawContent: {ex.Message}")
        End Try
        Return risultati
    End Function

    Private Shared Function RisolviPercorsoRelativoZip(baseFolder As String, target As String) As String
        Try
            Dim combinato As String
            If target.StartsWith("/") Then
                combinato = target.TrimStart("/"c)
            Else
                combinato = baseFolder & target
            End If
            combinato = combinato.Replace("\", "/")

            Dim parti As String() = combinato.Split("/"c)
            Dim risultato As New List(Of String)
            For Each parte As String In parti
                If parte = "." OrElse parte = "" Then
                    Continue For
                ElseIf parte = ".." Then
                    If risultato.Count > 0 Then risultato.RemoveAt(risultato.Count - 1)
                Else
                    risultato.Add(parte)
                End If
            Next
            Return String.Join("/", risultato)
        Catch ex As Exception
            logger.ScriviLog($"RisolviPercorsoRelativoZip: {ex.Message}")
            Return target
        End Try
    End Function

    Private Shared Function LeggiImmagineDaPacchettoOriginale(sourcePath As String, rIds As List(Of String), ByRef motivoFallimento As String, Optional cacheRelsXml As Dictionary(Of String, XDocument) = Nothing) As Byte()
        motivoFallimento = ""
        Try
            If String.IsNullOrEmpty(sourcePath) Then
                motivoFallimento = "sourcePath vuoto"
                Return Nothing
            End If
            If Not File.Exists(sourcePath) Then
                motivoFallimento = $"file non trovato su disco: '{sourcePath}'"
                Return Nothing
            End If
            If rIds Is Nothing OrElse rIds.Count = 0 Then
                motivoFallimento = "nessun rId candidato"
                Return Nothing
            End If

            Using fs As New FileStream(sourcePath, FileMode.Open, FileAccess.Read)
                Using arc As New ZipArchive(fs, ZipArchiveMode.Read)
                    Dim relsEntries = arc.Entries.Where(Function(e) e.FullName.Replace("\", "/").Contains("/_rels/") AndAlso e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToList()
                    If relsEntries.Count = 0 Then
                        motivoFallimento = "nessun file .rels trovato nello zip sorgente"
                        Return Nothing
                    End If

                    Dim nsRel As XNamespace = "http://schemas.openxmlformats.org/package/2006/relationships"
                    Dim dettagli As New List(Of String)

                    For Each rIdCandidato As String In rIds
                        Dim relazioneTrovata As Boolean = False
                        For Each relsEntry In relsEntries
                            Try
                                Dim relsXml As XDocument = Nothing
                                Dim chiaveCache As String = sourcePath & "|" & relsEntry.FullName

                                If cacheRelsXml IsNot Nothing AndAlso cacheRelsXml.ContainsKey(chiaveCache) Then
                                    relsXml = cacheRelsXml(chiaveCache)
                                Else
                                    Using sr As New StreamReader(relsEntry.Open(), Encoding.UTF8)
                                        relsXml = XDocument.Load(sr)
                                    End Using
                                    If cacheRelsXml IsNot Nothing Then cacheRelsXml(chiaveCache) = relsXml
                                End If

                                Dim relEl = relsXml.Descendants(nsRel + "Relationship").FirstOrDefault(Function(r) r.Attribute("Id")?.Value = rIdCandidato)
                                If relEl Is Nothing Then Continue For

                                relazioneTrovata = True

                                Dim target As String = relEl.Attribute("Target")?.Value
                                If String.IsNullOrEmpty(target) Then
                                    dettagli.Add($"{rIdCandidato}: relationship trovata in '{relsEntry.FullName}' ma senza attributo Target")
                                    Continue For
                                End If

                                Dim fullNameNorm As String = relsEntry.FullName.Replace("\", "/")
                                Dim idxRels As Integer = fullNameNorm.IndexOf("/_rels/")
                                Dim baseFolder As String = If(idxRels >= 0, fullNameNorm.Substring(0, idxRels + 1), "")
                                Dim targetNormalizzato As String = RisolviPercorsoRelativoZip(baseFolder, target)

                                Dim mediaEntry = arc.GetEntry(targetNormalizzato)
                                If mediaEntry Is Nothing Then
                                    mediaEntry = arc.Entries.FirstOrDefault(Function(e) e.FullName.Replace("\", "/").Equals(targetNormalizzato, StringComparison.OrdinalIgnoreCase))
                                End If
                                If mediaEntry Is Nothing Then
                                    dettagli.Add($"{rIdCandidato}: Target='{target}' (in '{relsEntry.FullName}', base '{baseFolder}') normalizzato in '{targetNormalizzato}', nessuna entry corrispondente nello zip")
                                    Continue For
                                End If

                                Using msOut As New MemoryStream()
                                    Using streamMedia = mediaEntry.Open()
                                        streamMedia.CopyTo(msOut)
                                    End Using
                                    If msOut.Length > 0 Then Return msOut.ToArray()
                                    dettagli.Add($"{rIdCandidato}: entry '{targetNormalizzato}' trovata ma con 0 byte")
                                End Using
                            Catch exRid As Exception
                                dettagli.Add($"{rIdCandidato}: eccezione su '{relsEntry.FullName}': {exRid.Message}")
                            End Try
                        Next

                        If Not relazioneTrovata Then
                            dettagli.Add($"{rIdCandidato}: non trovato in nessuno dei {relsEntries.Count} file .rels ({String.Join(",", relsEntries.Select(Function(e) e.FullName))})")
                        End If
                    Next

                    motivoFallimento = String.Join(" | ", dettagli)
                End Using
            End Using
        Catch ex As Exception
            motivoFallimento = $"eccezione generale: {ex.Message}"
            logger.ScriviLog($"LeggiImmagineDaPacchettoOriginale: {ex.Message}")
        End Try
        Return Nothing
    End Function

    Private Shared Sub OdtRaccogliImmagine(run As DocRun, immaginiMedia As Dictionary(Of String, Byte()), immaginiOdt As Dictionary(Of String, Byte()), mappaImmagini As Dictionary(Of DocRun, String), ByRef contatoreImmagini As Integer, sourcePath As String, cacheRelsXml As Dictionary(Of String, XDocument))
        Try
            If run Is Nothing OrElse Not run.IsImage Then Return
            If mappaImmagini.ContainsKey(run) Then Return

            Dim datiImmagine As Byte() = Nothing
            Dim rIdsCandidati As New List(Of String)
            Dim motivoFallback As String = "non tentato"
            Try
                If run.ImageBytes IsNot Nothing AndAlso run.ImageBytes.Length > 0 Then
                    datiImmagine = run.ImageBytes
                End If

                If datiImmagine Is Nothing AndAlso Not String.IsNullOrEmpty(run.OriginalRId) AndAlso immaginiMedia IsNot Nothing AndAlso immaginiMedia.ContainsKey(run.OriginalRId) Then
                    datiImmagine = immaginiMedia(run.OriginalRId)
                End If

                If datiImmagine Is Nothing AndAlso Not String.IsNullOrEmpty(run.RawContent) AndAlso run.RawContent.Length < 500 AndAlso File.Exists(run.RawContent) Then
                    datiImmagine = File.ReadAllBytes(run.RawContent)
                End If

                If datiImmagine Is Nothing AndAlso Not String.IsNullOrEmpty(run.RawContent) Then
                    rIdsCandidati = EstraiRelationshipIdDaRawContent(run.RawContent)
                    If rIdsCandidati.Count > 0 Then
                        datiImmagine = LeggiImmagineDaPacchettoOriginale(sourcePath, rIdsCandidati, motivoFallback, cacheRelsXml)
                    Else
                        motivoFallback = "nessun rId estratto dal contenuto grezzo"
                    End If
                End If
            Catch exLettura As Exception
                logger.ScriviLog($"OdtRaccogliImmagine lettura: {exLettura.Message}")
            End Try

            If datiImmagine Is Nothing OrElse datiImmagine.Length = 0 Then
                Return
            End If

            contatoreImmagini += 1
            Dim estensione As String = RilevaEstensione(datiImmagine)
            Dim nomeFile As String = "Pictures/immagine" & contatoreImmagini & "." & estensione
            immaginiOdt(nomeFile) = datiImmagine
            mappaImmagini(run) = nomeFile
        Catch ex As Exception
            logger.ScriviLog($"OdtRaccogliImmagine: {ex.Message}")
        End Try
    End Sub

    Private Shared Function OdtOttieniDimensioniImmagine(run As DocRun, datiImmagine As Byte()) As Tuple(Of Double, Double)
        Try
            Dim wPt As Double = 0, hPt As Double = 0
            Try
                wPt = run.ImageWidthPt
                hPt = run.ImageHeightPt
            Catch exImgSize As Exception
                logger.ScriviLog($"OdtDimensioniImmagine: dimensioni immagine non lette. Errore: {exImgSize.Message}")
            End Try
            If wPt > 0 AndAlso hPt > 0 Then
                Return Tuple.Create(wPt / 72.0, hPt / 72.0)
            End If
            If datiImmagine IsNot Nothing AndAlso datiImmagine.Length > 0 Then
                Try
                    Using ms As New MemoryStream(datiImmagine)
                        Using img As System.Drawing.Image = System.Drawing.Image.FromStream(ms)
                            Dim dpiX As Single = If(img.HorizontalResolution > 0, img.HorizontalResolution, 96.0F)
                            Dim dpiY As Single = If(img.VerticalResolution > 0, img.VerticalResolution, 96.0F)
                            Return Tuple.Create(CDbl(img.Width / dpiX), CDbl(img.Height / dpiY))
                        End Using
                    End Using
                Catch exImg As Exception
                    logger.ScriviLog($"OdtOttieniDimensioniImmagine: {exImg.Message}")
                End Try
            End If
            Return Tuple.Create(2.0, 1.5)
        Catch ex As Exception
            logger.ScriviLog($"OdtOttieniDimensioniImmagine outer: {ex.Message}")
            Return Tuple.Create(2.0, 1.5)
        End Try
    End Function

    Private Shared Sub OdtScriviParagrafo(sb As StringBuilder, p As DocParagraph, contesto As OdtContestoScrittura)
        Try
            If p Is Nothing Then Return

            If p.IsPageBreak Then
                sb.Append("<text:p text:style-name=""WGPageBreak""/>")
                Return
            End If

            Dim nomeStileP As String = OdtOttieniStileParagrafo(p, contesto.StyleDefs, contesto.CacheStileParagrafo, contesto.ContatoreStileParagrafo)
            Dim livelloTitolo As Integer = 0
            Try
                livelloTitolo = p.HeadingLevel
            Catch exHeading As Exception
                logger.ScriviLog($"OdtScriviParagrafo: HeadingLevel non letto. Errore: {exHeading.Message}")
            End Try

            Dim tagApertura As String
            Dim tagChiusura As String
            If livelloTitolo >= 1 AndAlso livelloTitolo <= 6 Then
                tagApertura = "<text:h text:style-name=""" & nomeStileP & """ text:outline-level=""" & livelloTitolo & """>"
                tagChiusura = "</text:h>"
            Else
                tagApertura = "<text:p text:style-name=""" & nomeStileP & """>"
                tagChiusura = "</text:p>"
            End If
            sb.Append(tagApertura)

            Dim idSegnalibro As String = ""
            Try
                idSegnalibro = p.BookmarkId
            Catch exBkmk As Exception
                logger.ScriviLog($"OdtScriviParagrafo: BookmarkId non letto. Errore: {exBkmk.Message}")
            End Try
            Dim eNota As Boolean = Not String.IsNullOrEmpty(idSegnalibro) AndAlso (idSegnalibro.StartsWith("NOTA_PIE_") OrElse idSegnalibro.StartsWith("NOTA_FINE_"))
            If Not String.IsNullOrEmpty(idSegnalibro) AndAlso Not eNota Then
                Dim nomeOdf As String = OdtNomeSegnalibro(idSegnalibro)
                If Not String.IsNullOrEmpty(nomeOdf) Then sb.Append("<text:bookmark text:name=""").Append(OdtEsc(nomeOdf)).Append("""/>")
            End If

            If p.Runs IsNot Nothing Then
                For Each r As DocRun In p.Runs
                    If r Is Nothing Then Continue For

                    If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.IsSuperscript AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                        Dim classeNota As String = If(r.LinkUrl.StartsWith("NOTA_PIE_"), "footnote", "endnote")
                        Dim runsNota As List(Of DocRun) = Nothing
                        If contesto.TestoNote.ContainsKey(r.LinkUrl) Then runsNota = contesto.TestoNote(r.LinkUrl)
                        sb.Append("<text:note text:id=""wgnota_" & contesto.ContatoreNote & """ text:note-class=""").Append(classeNota).Append(""">")
                        contesto.ContatoreNote += 1
                        sb.Append("<text:note-citation>").Append(OdtEsc(r.Text)).Append("</text:note-citation><text:note-body><text:p>")
                        If runsNota IsNot Nothing Then
                            For Each rn In runsNota
                                If rn Is Nothing OrElse rn.IsImage Then Continue For
                                Dim stileNota As String = OdtOttieniStileTesto(rn, contesto.StyleDefs, contesto.CacheStileTesto, contesto.ContatoreStileTesto)
                                sb.Append("<text:span text:style-name=""").Append(stileNota).Append(""">").Append(OdtTestoRun(rn.Text)).Append("</text:span>")
                            Next
                        End If
                        sb.Append("</text:p></text:note-body></text:note>")
                        Continue For
                    End If

                    If r.IsImage Then
                        If contesto.MappaImmagini.ContainsKey(r) Then
                            Dim percorsoImg As String = contesto.MappaImmagini(r)
                            Dim dimensioni As Tuple(Of Double, Double) = OdtOttieniDimensioniImmagine(r, If(contesto.ImmaginiOdt.ContainsKey(percorsoImg), contesto.ImmaginiOdt(percorsoImg), Nothing))
                            sb.Append("<draw:frame draw:name=""img").Append(contesto.ContatoreFrame).Append(""" text:anchor-type=""as-char"" svg:width=""").Append(dimensioni.Item1.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" svg:height=""").Append(dimensioni.Item2.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in""><draw:image xlink:href=""").Append(OdtEsc(percorsoImg)).Append(""" xlink:type=""simple"" xlink:show=""embed"" xlink:actuate=""onLoad""/></draw:frame>")
                            contesto.ContatoreFrame += 1
                        End If
                        Continue For
                    End If

                    Dim nomeStileT As String = OdtOttieniStileTesto(r, contesto.StyleDefs, contesto.CacheStileTesto, contesto.ContatoreStileTesto)
                    Dim testoOdf As String = OdtTestoRun(r.Text)

                    Dim eLink As Boolean = Not String.IsNullOrEmpty(r.LinkUrl)
                    If eLink Then
                        Dim urlPulito As String = r.LinkUrl
                        If urlPulito.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                            sb.Append("<text:a xlink:href=""").Append(OdtEsc(urlPulito)).Append(""" xlink:type=""simple"">")
                            sb.Append("<text:span text:style-name=""").Append(nomeStileT).Append(""">").Append(testoOdf).Append("</text:span>")
                            sb.Append("</text:a>")
                        Else
                            Dim ancoraPulita As String = If(urlPulito.StartsWith("#"), urlPulito.Substring(1), urlPulito)
                            Dim nomeAncora As String = OdtNomeSegnalibro(ancoraPulita)
                            sb.Append("<text:a xlink:href=""#").Append(OdtEsc(nomeAncora)).Append(""" xlink:type=""simple"">")
                            sb.Append("<text:span text:style-name=""").Append(nomeStileT).Append(""">").Append(testoOdf).Append("</text:span>")
                            sb.Append("</text:a>")
                        End If
                    Else
                        sb.Append("<text:span text:style-name=""").Append(nomeStileT).Append(""">").Append(testoOdf).Append("</text:span>")
                    End If
                Next
            End If

            If contesto.CommentiPerParagrafo IsNot Nothing AndAlso Not String.IsNullOrEmpty(idSegnalibro) AndAlso contesto.CommentiPerParagrafo.ContainsKey(idSegnalibro) Then
                Dim nomeAutoreAnnotazione As String = OdtEsc(OttieniNomeAutore())
                For Each testoCommento In contesto.CommentiPerParagrafo(idSegnalibro)
                    contesto.ContatoreCommenti += 1
                    sb.Append("<office:annotation office:name=""wgcmt").Append(contesto.ContatoreCommenti).Append("""><dc:creator>").Append(nomeAutoreAnnotazione).Append("</dc:creator><dc:date>").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")).Append("</dc:date><text:p>").Append(OdtTestoRun(testoCommento)).Append("</text:p></office:annotation>")
                Next
            End If

            sb.Append(tagChiusura)
        Catch ex As Exception
            logger.ScriviLog($"OdtScriviParagrafo: {ex.Message}")
        End Try
    End Sub

    Private Class OdtContestoScrittura
        Public StyleDefs As New StringBuilder()
        Public CacheStileTesto As New Dictionary(Of String, String)()
        Public ContatoreStileTesto As Integer = 0
        Public CacheStileParagrafo As New Dictionary(Of String, String)()
        Public ContatoreStileParagrafo As Integer = 0
        Public CacheStileCella As New Dictionary(Of String, String)()
        Public ContatoreStileCella As Integer = 0
        Public MappaImmagini As New Dictionary(Of DocRun, String)()
        Public ImmaginiOdt As New Dictionary(Of String, Byte())()
        Public ContatoreFrame As Integer = 0
        Public ContatoreNote As Integer = 0
        Public ContatoreCommenti As Integer = 0
        Public TestoNote As New Dictionary(Of String, List(Of DocRun))()
        Public CommentiPerParagrafo As Dictionary(Of String, List(Of String))
        Public CacheRelsXml As New Dictionary(Of String, XDocument)()
    End Class

    Public Shared Sub SalvaDocumentoOdt(elements As List(Of DocElement), outputPath As String, immaginiMedia As Dictionary(Of String, Byte()), commentiPerParagrafo As Dictionary(Of String, List(Of String)), pageSettings As Dictionary(Of String, Integer), Optional sourcePath As String = "", Optional headerText As String = "", Optional footerText As String = "")
        Try
            Dim tempFile As String = Path.Combine(Path.GetTempPath(), "wgword_" & Path.GetRandomFileName() & ".odt")

            Dim contesto As New OdtContestoScrittura()
            contesto.CommentiPerParagrafo = commentiPerParagrafo

            Dim elementiCorpo As New List(Of DocElement)()
            If elements IsNot Nothing Then
                For Each el In elements
                    If TypeOf el Is DocParagraph Then
                        Dim pChk As DocParagraph = DirectCast(el, DocParagraph)
                        Dim bkChk As String = ""
                        Try
                            bkChk = pChk.BookmarkId
                        Catch exBkChk As Exception
                            logger.ScriviLog($"OdtScriviCorpo: BookmarkId nota non letto. Errore: {exBkChk.Message}")
                        End Try
                        If Not String.IsNullOrEmpty(bkChk) AndAlso (bkChk.StartsWith("NOTA_PIE_") OrElse bkChk.StartsWith("NOTA_FINE_")) Then
                            Dim runsNota As New List(Of DocRun)()
                            If pChk.Runs IsNot Nothing Then
                                For Each rNota In pChk.Runs
                                    If rNota Is Nothing Then Continue For
                                    If rNota.IsSuperscript AndAlso rNota.LinkUrl = bkChk Then Continue For
                                    runsNota.Add(rNota)
                                Next
                            End If
                            contesto.TestoNote(bkChk) = runsNota
                            Continue For
                        End If
                    End If
                    elementiCorpo.Add(el)
                Next
            End If

            For Each el In elementiCorpo
                If TypeOf el Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(el, DocParagraph)
                    If p.Runs IsNot Nothing Then
                        For Each r As DocRun In p.Runs
                            OdtRaccogliImmagine(r, immaginiMedia, contesto.ImmaginiOdt, contesto.MappaImmagini, contesto.ContatoreFrame, sourcePath, contesto.CacheRelsXml)
                        Next
                    End If
                ElseIf TypeOf el Is DocTable Then
                    Dim t As DocTable = DirectCast(el, DocTable)
                    For Each riga In t.Rows
                        For Each cella In riga.Cells
                            For Each pc In cella.Paragraphs
                                If pc.Runs IsNot Nothing Then
                                    For Each r As DocRun In pc.Runs
                                        OdtRaccogliImmagine(r, immaginiMedia, contesto.ImmaginiOdt, contesto.MappaImmagini, contesto.ContatoreFrame, sourcePath, contesto.CacheRelsXml)
                                    Next
                                End If
                            Next
                        Next
                    Next
                End If
            Next
            contesto.ContatoreFrame = 0

            Dim corpo As New StringBuilder()
            Dim listaAttivaId As Integer = -1
            Dim listaAperta As Boolean = False

            For Each el In elementiCorpo
                If TypeOf el Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(el, DocParagraph)
                    Dim eListaOra As Boolean = False
                    Dim numIdOra As Integer = 0
                    Try
                        eListaOra = p.IsListItem
                        numIdOra = p.NumberingId
                    Catch exListaOra As Exception
                        logger.ScriviLog($"OdtScriviCorpo: proprietà lista non lette. Errore: {exListaOra.Message}")
                    End Try

                    If eListaOra Then
                        If Not listaAperta OrElse numIdOra <> listaAttivaId Then
                            If listaAperta Then corpo.Append("</text:list>")
                            Dim nomeStileLista As String = If(numIdOra = 2, "WGNumberList", "WGBulletList")
                            corpo.Append("<text:list text:style-name=""").Append(nomeStileLista).Append(""">")
                            listaAperta = True
                            listaAttivaId = numIdOra
                        End If
                        corpo.Append("<text:list-item>")
                        OdtScriviParagrafo(corpo, p, contesto)
                        corpo.Append("</text:list-item>")
                    Else
                        If listaAperta Then
                            corpo.Append("</text:list>")
                            listaAperta = False
                        End If
                        OdtScriviParagrafo(corpo, p, contesto)
                    End If
                ElseIf TypeOf el Is DocTable Then
                    If listaAperta Then
                        corpo.Append("</text:list>")
                        listaAperta = False
                    End If
                    Dim t As DocTable = DirectCast(el, DocTable)
                    contesto.ContatoreStileParagrafo += 1
                    Dim nomeTabella As String = "WGTable" & contesto.ContatoreStileParagrafo
                    corpo.Append("<table:table table:name=""").Append(nomeTabella).Append(""">")
                    Dim numColonne As Integer = 0
                    If t.Rows IsNot Nothing AndAlso t.Rows.Count > 0 AndAlso t.Rows(0).Cells IsNot Nothing Then numColonne = t.Rows(0).Cells.Count
                    For colIdx As Integer = 0 To numColonne - 1
                        corpo.Append("<table:table-column/>")
                    Next
                    If t.Rows IsNot Nothing Then
                        For Each riga In t.Rows
                            corpo.Append("<table:table-row>")
                            If riga.Cells IsNot Nothing Then
                                For Each cella In riga.Cells
                                    Dim nomeStileCella As String = OdtOttieniStileCella(cella, contesto.StyleDefs, contesto.CacheStileCella, contesto.ContatoreStileCella)
                                    corpo.Append("<table:table-cell table:style-name=""").Append(nomeStileCella).Append(""" office:value-type=""string"">")
                                    If cella.Paragraphs IsNot Nothing AndAlso cella.Paragraphs.Count > 0 Then
                                        For Each pc In cella.Paragraphs
                                            OdtScriviParagrafo(corpo, pc, contesto)
                                        Next
                                    Else
                                        corpo.Append("<text:p/>")
                                    End If
                                    corpo.Append("</table:table-cell>")
                                Next
                            End If
                            corpo.Append("</table:table-row>")
                        Next
                    End If
                    corpo.Append("</table:table>")
                End If
            Next
            If listaAperta Then corpo.Append("</text:list>")

            Dim pw As Integer = 11906, ph As Integer = 16838
            Dim mt As Integer = 1417, mb As Integer = 1417, ml As Integer = 1417, mr As Integer = 1417
            Dim orientLandscape As Boolean = False
            If pageSettings IsNot Nothing Then
                If pageSettings.ContainsKey("PageWidth") Then pw = pageSettings("PageWidth")
                If pageSettings.ContainsKey("PageHeight") Then ph = pageSettings("PageHeight")
                If pageSettings.ContainsKey("MarginTop") Then mt = pageSettings("MarginTop")
                If pageSettings.ContainsKey("MarginBottom") Then mb = pageSettings("MarginBottom")
                If pageSettings.ContainsKey("MarginLeft") Then ml = pageSettings("MarginLeft")
                If pageSettings.ContainsKey("MarginRight") Then mr = pageSettings("MarginRight")
                If pageSettings.ContainsKey("Orientation") Then orientLandscape = (pageSettings("Orientation") = 2)
            End If
            Dim pwIn As Double = pw / 1440.0
            Dim phIn As Double = ph / 1440.0
            Dim mtIn As Double = mt / 1440.0
            Dim mbIn As Double = mb / 1440.0
            Dim mlIn As Double = ml / 1440.0
            Dim mrIn As Double = mr / 1440.0
            If orientLandscape AndAlso pwIn < phIn Then
                Dim scambio As Double = pwIn
                pwIn = phIn
                phIn = scambio
            End If

            Dim contentXml As New StringBuilder()
            contentXml.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
            contentXml.Append("<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:table=""urn:oasis:names:tc:opendocument:xmlns:table:1.0"" xmlns:draw=""urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" xmlns:xlink=""http://www.w3.org/1999/xlink"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:svg=""urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"" xmlns:loext=""urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0"" office:version=""1.2"">")
            contentXml.Append("<office:automatic-styles>")
            contentXml.Append("<style:style style:name=""WGCellaBase"" style:family=""table-cell""><style:table-cell-properties fo:border=""0.5pt solid #000000"" fo:padding=""0.05in""/></style:style>")
            contentXml.Append("<style:style style:name=""WGPageBreak"" style:family=""paragraph"" style:parent-style-name=""Standard""><style:paragraph-properties fo:break-before=""page""/></style:style>")
            contentXml.Append("<text:list-style style:name=""WGBulletList"">")
            For lvl As Integer = 1 To 5
                Dim indentBullet As Double = lvl * 0.3
                contentXml.Append("<text:list-level-style-bullet text:level=""").Append(lvl).Append(""" text:bullet-char=""•""><style:list-level-properties text:space-before=""").Append(indentBullet.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" text:min-label-width=""0.25in""/></text:list-level-style-bullet>")
            Next
            contentXml.Append("</text:list-style>")
            contentXml.Append("<text:list-style style:name=""WGNumberList"">")
            For lvl As Integer = 1 To 5
                Dim indentNum As Double = lvl * 0.3
                contentXml.Append("<text:list-level-style-number text:level=""").Append(lvl).Append(""" style:num-suffix=""."" style:num-format=""1""><style:list-level-properties text:space-before=""").Append(indentNum.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" text:min-label-width=""0.35in""/></text:list-level-style-number>")
            Next
            contentXml.Append("</text:list-style>")
            contentXml.Append(contesto.StyleDefs.ToString())
            contentXml.Append("</office:automatic-styles>")
            contentXml.Append("<office:body><office:text>")
            contentXml.Append(corpo.ToString())
            contentXml.Append("</office:text></office:body>")
            contentXml.Append("</office:document-content>")

            Dim stylesXml As New StringBuilder()
            stylesXml.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
            stylesXml.Append("<office:document-styles xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:style=""urn:oasis:names:tc:opendocument:xmlns:style:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:fo=""urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0"" xmlns:svg=""urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"" xmlns:loext=""urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0"" office:version=""1.2"">")
            stylesXml.Append("<office:styles>")
            stylesXml.Append("<style:default-style style:family=""paragraph""><style:paragraph-properties fo:orphans=""2"" fo:widows=""2""/><style:text-properties style:font-name=""Arial"" fo:font-size=""11pt""/></style:default-style>")
            stylesXml.Append("<style:style style:name=""Standard"" style:family=""paragraph"" style:class=""text""/>")
            Dim dimensioniTitolo() As Integer = {18, 16, 14, 13, 12, 11}
            For livello As Integer = 1 To 6
                stylesXml.Append("<style:style style:name=""Heading_20_").Append(livello).Append(""" style:display-name=""Heading ").Append(livello).Append(""" style:family=""paragraph"" style:parent-style-name=""Standard"" style:next-style-name=""Standard"" style:class=""text""><style:paragraph-properties fo:margin-top=""0.15in"" fo:margin-bottom=""0.08in"" fo:keep-with-next=""always""/><style:text-properties fo:font-weight=""bold"" fo:font-size=""").Append(dimensioniTitolo(livello - 1)).Append("pt""/></style:style>")
            Next
            stylesXml.Append("<text:outline-style style:name=""WGOutline"">")
            For livello As Integer = 1 To 6
                stylesXml.Append("<text:outline-level-style text:level=""").Append(livello).Append(""" style:num-format=""""/>")
            Next
            stylesXml.Append("</text:outline-style>")
            stylesXml.Append("</office:styles>")
            stylesXml.Append("<office:automatic-styles>")
            stylesXml.Append("<style:page-layout style:name=""WGPageLayout""><style:page-layout-properties fo:page-width=""").Append(pwIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" fo:page-height=""").Append(phIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" style:print-orientation=""").Append(If(orientLandscape, "landscape", "portrait")).Append(""" fo:margin-top=""").Append(mtIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" fo:margin-bottom=""").Append(mbIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" fo:margin-left=""").Append(mlIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in"" fo:margin-right=""").Append(mrIn.ToString("0.00", Globalization.CultureInfo.InvariantCulture)).Append("in""/><style:header-style><style:header-footer-properties fo:min-height=""0.3in"" fo:margin-bottom=""0.1in""/></style:header-style><style:footer-style><style:header-footer-properties fo:min-height=""0.3in"" fo:margin-top=""0.1in""/></style:footer-style></style:page-layout>")
            stylesXml.Append("</office:automatic-styles>")
            Dim masterPageHeaderFooter As New StringBuilder()
            If Not String.IsNullOrEmpty(headerText) Then
                masterPageHeaderFooter.Append("<style:header><text:p>").Append(OdtEsc(headerText)).Append("</text:p></style:header>")
            End If
            If Not String.IsNullOrEmpty(footerText) Then
                masterPageHeaderFooter.Append("<style:footer><text:p>").Append(OdtEsc(footerText)).Append("</text:p></style:footer>")
            End If
            stylesXml.Append("<office:master-styles><style:master-page style:name=""Standard"" style:page-layout-name=""WGPageLayout"">").Append(masterPageHeaderFooter.ToString()).Append(XmlTemaOdt(PaletteTemaAttiva())).Append("</style:master-page></office:master-styles>")
            stylesXml.Append("</office:document-styles>")

            Dim metaXml As String = "<?xml version=""1.0"" encoding=""UTF-8""?><office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0"" office:version=""1.2""><office:meta><meta:generator>WinGabriel</meta:generator><dc:creator>" & OdtEsc(OttieniNomeAutore()) & "</dc:creator><dc:date>" & DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") & "</dc:date></office:meta></office:document-meta>"

            Dim manifestXml As New StringBuilder()
            manifestXml.Append("<?xml version=""1.0"" encoding=""UTF-8""?>")
            manifestXml.Append("<manifest:manifest xmlns:manifest=""urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"" manifest:version=""1.2"">")
            manifestXml.Append("<manifest:file-entry manifest:full-path=""/"" manifest:version=""1.2"" manifest:media-type=""application/vnd.oasis.opendocument.text""/>")
            manifestXml.Append("<manifest:file-entry manifest:full-path=""content.xml"" manifest:media-type=""text/xml""/>")
            manifestXml.Append("<manifest:file-entry manifest:full-path=""styles.xml"" manifest:media-type=""text/xml""/>")
            manifestXml.Append("<manifest:file-entry manifest:full-path=""meta.xml"" manifest:media-type=""text/xml""/>")
            For Each kvp In contesto.ImmaginiOdt
                Dim estensione As String = Path.GetExtension(kvp.Key).TrimStart("."c)
                Dim mediaType As String = "image/png"
                Select Case estensione.ToLowerInvariant()
                    Case "jpg", "jpeg" : mediaType = "image/jpeg"
                    Case "gif" : mediaType = "image/gif"
                    Case "bmp" : mediaType = "image/bmp"
                    Case Else : mediaType = "image/png"
                End Select
                manifestXml.Append("<manifest:file-entry manifest:full-path=""").Append(OdtEsc(kvp.Key)).Append(""" manifest:media-type=""").Append(mediaType).Append("""/>")
            Next
            manifestXml.Append("</manifest:manifest>")

            If File.Exists(tempFile) Then File.Delete(tempFile)
            Using archivio As ZipArchive = ZipFile.Open(tempFile, ZipArchiveMode.Create)
                Dim entryMime = archivio.CreateEntry("mimetype", CompressionLevel.NoCompression)
                Using w As New StreamWriter(entryMime.Open(), New UTF8Encoding(False))
                    w.Write("application/vnd.oasis.opendocument.text")
                End Using

                Dim entryManifest = archivio.CreateEntry("META-INF/manifest.xml")
                Using w As New StreamWriter(entryManifest.Open(), New UTF8Encoding(False))
                    w.Write(manifestXml.ToString())
                End Using

                Dim entryContent = archivio.CreateEntry("content.xml")
                Using w As New StreamWriter(entryContent.Open(), New UTF8Encoding(False))
                    w.Write(contentXml.ToString())
                End Using

                Dim entryStyles = archivio.CreateEntry("styles.xml")
                Using w As New StreamWriter(entryStyles.Open(), New UTF8Encoding(False))
                    w.Write(stylesXml.ToString())
                End Using

                Dim entryMeta = archivio.CreateEntry("meta.xml")
                Using w As New StreamWriter(entryMeta.Open(), New UTF8Encoding(False))
                    w.Write(metaXml)
                End Using

                For Each kvp In contesto.ImmaginiOdt
                    Try
                        Dim entryImg = archivio.CreateEntry(kvp.Key)
                        Using streamImg = entryImg.Open()
                            streamImg.Write(kvp.Value, 0, kvp.Value.Length)
                        End Using
                    Catch exImgWrite As Exception
                        logger.ScriviLog($"SalvaDocumentoOdt scrittura immagine '{kvp.Key}': {exImgWrite.Message}")
                    End Try
                Next
            End Using

            Dim moveRiuscito As Boolean = False
            Dim backupPath As String = outputPath & ".bak"
            Try
                If File.Exists(outputPath) Then File.Move(outputPath, backupPath)
                Try
                    SpostaOCopiaFile(tempFile, outputPath)
                    moveRiuscito = True
                Catch exMove As Exception
                    If File.Exists(backupPath) Then File.Move(backupPath, outputPath)
                    Throw
                End Try

                If moveRiuscito AndAlso File.Exists(backupPath) Then
                    Try
                        File.Delete(backupPath)
                    Catch exDelBackup As Exception
                        logger.ScriviLog($"SalvaDocumentoOdt: impossibile eliminare il backup temporaneo '{backupPath}'. Errore: {exDelBackup.Message}")
                    End Try
                End If
            Finally
                If Not moveRiuscito AndAlso File.Exists(tempFile) Then
                    Try
                        File.Delete(tempFile)
                    Catch exDelTemp As Exception
                        logger.ScriviLog($"SalvaDocumentoOdt: impossibile eliminare il file temporaneo '{tempFile}'. Errore: {exDelTemp.Message}")
                    End Try
                End If
            End Try
        Catch ex As UnauthorizedAccessException
            logger.ScriviLog($"SalvaDocumentoOdt UnauthorizedAccess: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il percorso specificato non è scrivibile o non si dispone delle autorizzazioni necessarie.")
        Catch ex As DirectoryNotFoundException
            logger.ScriviLog($"SalvaDocumentoOdt DirectoryNotFound: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Il percorso specificato non è valido o la cartella non esiste.")
        Catch ex As IOException
            logger.ScriviLog($"SalvaDocumentoOdt IOException: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Throw New Exception("Errore di input o output sul file. Potrebbe essere aperto in un altro programma o il percorso non è valido.")
        Catch ex As Exception
            logger.ScriviLog($"SalvaDocumentoOdt Error: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub



    Public Shared Sub SalvaDocumentoDocxOpenXml(elements As List(Of DocElement), outputPath As String, immaginiMedia As Dictionary(Of String, Byte()), stiliTitolo As Dictionary(Of String, Integer), commentiPerParagrafo As Dictionary(Of String, List(Of String)), pageSettings As Dictionary(Of String, Integer), sourcePath As String, headerText As String, footerText As String)
        Dim tempFile As String = Path.Combine(Path.GetTempPath(), "wgword_ox_" & Path.GetRandomFileName() & ".docx")
        Try
            Dim assegnazioneCommenti As New Dictionary(Of String, List(Of Integer))
            Dim mappaIdTesto As New Dictionary(Of Integer, String)
            Dim contatoreCommento As Integer = 0
            If commentiPerParagrafo IsNot Nothing AndAlso commentiPerParagrafo.Count > 0 Then
                For Each kvp In commentiPerParagrafo
                    Dim listaIds As New List(Of Integer)
                    For Each testoCmt In kvp.Value
                        listaIds.Add(contatoreCommento)
                        mappaIdTesto(contatoreCommento) = testoCmt
                        contatoreCommento += 1
                    Next
                    assegnazioneCommenti(kvp.Key) = listaIds
                Next
            End If
            Dim haCommenti As Boolean = (mappaIdTesto.Count > 0)

            Dim noteAPiePagina As New Dictionary(Of Integer, String)()
            Dim noteDiChiusura As New Dictionary(Of Integer, String)()
            Dim elementiCorpo As New List(Of DocElement)()
            EstraiNoteEFiltraCorpoOX(elements, noteAPiePagina, noteDiChiusura, elementiCorpo)
            Dim haFootnotes As Boolean = noteAPiePagina.Count > 0
            Dim haEndnotes As Boolean = noteDiChiusura.Count > 0
            Dim haHeader As Boolean = Not String.IsNullOrEmpty(headerText)
            Dim haFooter As Boolean = Not String.IsNullOrEmpty(footerText)

            Dim sorgentePath As String = If(Not String.IsNullOrEmpty(sourcePath) AndAlso File.Exists(sourcePath), sourcePath, String.Empty)
            Dim hasSorgente As Boolean = Not String.IsNullOrEmpty(sorgentePath)

            Dim immaginiUsate As New Dictionary(Of String, Byte())
            Dim mappaRidInOutput As New Dictionary(Of String, String)
            Dim mediaCounter As Integer = 1
            Dim cacheRelsXmlDocx As New Dictionary(Of String, XDocument)
            For Each el In elementiCorpo
                If TypeOf el Is DocParagraph Then
                    RaccogliImmaginiDaParagrafo(DirectCast(el, DocParagraph), immaginiMedia, immaginiUsate, mappaRidInOutput, mediaCounter, sorgentePath, cacheRelsXmlDocx)
                ElseIf TypeOf el Is DocTable Then
                    For Each row As DocTableRow In DirectCast(el, DocTable).Rows
                        For Each cell As DocTableCell In row.Cells
                            For Each p As DocParagraph In cell.Paragraphs
                                RaccogliImmaginiDaParagrafo(p, immaginiMedia, immaginiUsate, mappaRidInOutput, mediaCounter, sorgentePath, cacheRelsXmlDocx)
                            Next
                        Next
                    Next
                End If
            Next

            If hasSorgente Then
                File.Copy(sorgentePath, tempFile, True)
            End If

            Using wordDoc As WordprocessingDocument = If(hasSorgente, WordprocessingDocument.Open(tempFile, True), WordprocessingDocument.Create(tempFile, WordprocessingDocumentType.Document))
                Dim mainPart As MainDocumentPart = wordDoc.MainDocumentPart
                If mainPart Is Nothing Then
                    mainPart = wordDoc.AddMainDocumentPart()
                End If
                If mainPart.Document Is Nothing Then
                    mainPart.Document = New OX.Document(New OX.Body())
                End If
                If mainPart.Document.Body Is Nothing Then
                    mainPart.Document.AppendChild(New OX.Body())
                End If

                Dim body As OX.Body = mainPart.Document.Body
                body.RemoveAllChildren()

                Dim mappaHyperlinkRid As New Dictionary(Of String, String)
                Dim mappaImmagineRidOX As New Dictionary(Of String, String)

                For Each el As DocElement In elementiCorpo
                    If TypeOf el Is DocParagraph Then
                        body.AppendChild(CostruisciParagrafoOX(mainPart, DirectCast(el, DocParagraph), stiliTitolo, assegnazioneCommenti, noteAPiePagina, noteDiChiusura, mappaHyperlinkRid, mappaRidInOutput, immaginiUsate, mappaImmagineRidOX))
                    ElseIf TypeOf el Is DocTable Then
                        body.AppendChild(CostruisciTabellaOX(mainPart, DirectCast(el, DocTable), stiliTitolo, assegnazioneCommenti, noteAPiePagina, noteDiChiusura, mappaHyperlinkRid, mappaRidInOutput, immaginiUsate, mappaImmagineRidOX))
                    End If
                Next

                Dim sectPr As New OX.SectionProperties()
                If haHeader Then AggiungiHeaderOX(wordDoc, mainPart, sectPr, headerText)
                If haFooter Then AggiungiFooterOX(wordDoc, mainPart, sectPr, footerText)
                AggiungiImpostazioniPaginaOX(sectPr, pageSettings)
                body.AppendChild(sectPr)

                Dim haElenchi As Boolean = False
                For Each elChk As DocElement In elementiCorpo
                    If TypeOf elChk Is DocParagraph AndAlso DirectCast(elChk, DocParagraph).IsListItem Then
                        haElenchi = True
                        Exit For
                    ElseIf TypeOf elChk Is DocTable Then
                        For Each rowChk As DocTableRow In DirectCast(elChk, DocTable).Rows
                            For Each cellChk As DocTableCell In rowChk.Cells
                                For Each pChk As DocParagraph In cellChk.Paragraphs
                                    If pChk.IsListItem Then
                                        haElenchi = True
                                        Exit For
                                    End If
                                Next
                            Next
                        Next
                        If haElenchi Then Exit For
                    End If
                Next
                If haElenchi Then CostruisciNumberingPartOX(mainPart)

                If haFootnotes Then CostruisciFootnotesPartOX(wordDoc, mainPart, noteAPiePagina)
                If haEndnotes Then CostruisciEndnotesPartOX(wordDoc, mainPart, noteDiChiusura)
                If haCommenti Then CostruisciCommentsPartOX(wordDoc, mainPart, mappaIdTesto)

                CompletaOAggiungiStylesPartOX(wordDoc, mainPart, haCommenti, haFootnotes, haEndnotes)

                AssicuraCompatibilitaModernaOX(mainPart)
                AssicuraProprietaDocumentoOX(wordDoc)
                AssicuraTemaOX(mainPart)
                AssicuraTabellaFontOX(mainPart)

                Dim partiImmagineDaTenere As New HashSet(Of String)(mappaImmagineRidOX.Values)
                Dim partiImmagineDaRimuovere As New List(Of ImagePart)
                For Each parteImmagine As ImagePart In mainPart.ImageParts
                    Dim ridParteImmagine As String = mainPart.GetIdOfPart(parteImmagine)
                    If Not partiImmagineDaTenere.Contains(ridParteImmagine) Then partiImmagineDaRimuovere.Add(parteImmagine)
                Next
                For Each parteDaRimuovere As ImagePart In partiImmagineDaRimuovere
                    mainPart.DeletePart(parteDaRimuovere)
                Next

                Dim relazioniIpertestoDaTenere As New HashSet(Of String)(mappaHyperlinkRid.Values)
                Dim relazioniIpertestoDaRimuovere As New List(Of HyperlinkRelationship)
                For Each relazioneIpertesto As HyperlinkRelationship In mainPart.HyperlinkRelationships
                    If Not relazioniIpertestoDaTenere.Contains(relazioneIpertesto.Id) Then relazioniIpertestoDaRimuovere.Add(relazioneIpertesto)
                Next
                For Each relazioneDaRimuovere As HyperlinkRelationship In relazioniIpertestoDaRimuovere
                    mainPart.DeleteReferenceRelationship(relazioneDaRimuovere)
                Next

                mainPart.Document.Save()
            End Using

            SpostaOCopiaFile(tempFile, outputPath)
        Catch ex As Exception
            logger.ScriviLog("SalvaDocumentoDocxOpenXml: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch exDelTemp As Exception
                logger.ScriviLog($"SalvaDocumentoDocxOpenXml: impossibile eliminare il file temporaneo '{tempFile}'. Errore: {exDelTemp.Message}")
            End Try
            Throw
        End Try
    End Sub

    Private Shared Sub EstraiNoteEFiltraCorpoOX(elements As List(Of DocElement), noteAPiePagina As Dictionary(Of Integer, String), noteDiChiusura As Dictionary(Of Integer, String), elementiCorpo As List(Of DocElement))
        Try
            For Each el In elements
                If TypeOf el Is DocParagraph Then
                    Dim pCheck As DocParagraph = DirectCast(el, DocParagraph)
                    If Not String.IsNullOrEmpty(pCheck.BookmarkId) AndAlso pCheck.BookmarkId.StartsWith("NOTA_PIE_") Then
                        Dim numVal As Integer
                        If Integer.TryParse(pCheck.BookmarkId.Substring("NOTA_PIE_".Length), numVal) Then
                            noteAPiePagina(numVal) = TestoNotaSenzaMarcatore(pCheck)
                        End If
                        Continue For
                    ElseIf Not String.IsNullOrEmpty(pCheck.BookmarkId) AndAlso pCheck.BookmarkId.StartsWith("NOTA_FINE_") Then
                        Dim numVal As Integer
                        If Integer.TryParse(pCheck.BookmarkId.Substring("NOTA_FINE_".Length), numVal) Then
                            noteDiChiusura(numVal) = TestoNotaSenzaMarcatore(pCheck)
                        End If
                        Continue For
                    End If
                End If
                elementiCorpo.Add(el)
            Next

            For Each elCorpo In elementiCorpo
                If TypeOf elCorpo Is DocParagraph Then
                    Dim pCorpo As DocParagraph = DirectCast(elCorpo, DocParagraph)
                    If pCorpo.Runs Is Nothing Then Continue For
                    For Each r As DocRun In pCorpo.Runs
                        If r IsNot Nothing AndAlso r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) Then
                            Dim orfano As Boolean = False
                            If r.LinkUrl.StartsWith("NOTA_PIE_") Then
                                Dim numRif As Integer
                                If Integer.TryParse(r.LinkUrl.Substring("NOTA_PIE_".Length), numRif) AndAlso Not noteAPiePagina.ContainsKey(numRif) Then orfano = True
                            ElseIf r.LinkUrl.StartsWith("NOTA_FINE_") Then
                                Dim numRif As Integer
                                If Integer.TryParse(r.LinkUrl.Substring("NOTA_FINE_".Length), numRif) AndAlso Not noteDiChiusura.ContainsKey(numRif) Then orfano = True
                            End If
                            If orfano Then
                                Dim impostazioniSalvataggio As New WGImpostazioniComuni()
                                If impostazioniSalvataggio.EliminaNotaConParola Then
                                    r.Text = ""
                                Else
                                End If
                                r.LinkUrl = ""
                                r.IsHyperlink = False
                            End If
                        End If
                    Next
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("EstraiNoteEFiltraCorpoOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function TestoNotaSenzaMarcatore(pCheck As DocParagraph) As String
        Dim sbNota As New StringBuilder()
        For Each r As DocRun In pCheck.Runs
            If r IsNot Nothing AndAlso Not r.IsImage Then
                If Not (r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl = pCheck.BookmarkId) Then
                    If Not String.IsNullOrEmpty(r.Text) Then sbNota.Append(r.Text)
                End If
            End If
        Next
        Return sbNota.ToString().Trim()
    End Function

    Private Shared Function CostruisciParagrafoOX(mainPart As MainDocumentPart, p As DocParagraph, stiliTitolo As Dictionary(Of String, Integer), assegnazioneCommenti As Dictionary(Of String, List(Of Integer)), noteAPiePagina As Dictionary(Of Integer, String), noteDiChiusura As Dictionary(Of Integer, String), mappaHyperlinkRid As Dictionary(Of String, String), mappaRidInOutput As Dictionary(Of String, String), immaginiUsate As Dictionary(Of String, Byte()), mappaImmagineRidOX As Dictionary(Of String, String)) As OX.Paragraph
        Try
            Dim paragrafoOX As New OX.Paragraph()

            If p.IsPageBreak Then
                Dim runBreak As New OX.Run(New OX.Break() With {.Type = OX.BreakValues.Page})
                paragrafoOX.AppendChild(runBreak)
                Return paragrafoOX
            End If

            Dim pPr As New OX.ParagraphProperties()

            Dim livelloTitolo As Integer = 0
            If p.HeadingLevel >= 1 AndAlso p.HeadingLevel <= 6 Then
                livelloTitolo = p.HeadingLevel
            ElseIf stiliTitolo IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.StyleId) AndAlso stiliTitolo.ContainsKey(p.StyleId) Then
                livelloTitolo = stiliTitolo(p.StyleId)
            End If
            If Not String.IsNullOrEmpty(p.StyleId) AndAlso livelloTitolo = 0 Then
                pPr.ParagraphStyleId = New OX.ParagraphStyleId() With {.Val = p.StyleId}
            ElseIf livelloTitolo >= 1 AndAlso livelloTitolo <= 6 Then
                pPr.ParagraphStyleId = New OX.ParagraphStyleId() With {.Val = "Heading" & livelloTitolo}
            End If

            If p.KeepWithNext Then pPr.KeepNext = New OX.KeepNext()
            If p.KeepLines Then pPr.KeepLines = New OX.KeepLines()
            If p.PageBreakBefore Then pPr.PageBreakBefore = New OX.PageBreakBefore()

            If p.IsListItem Then
                Dim numId As Integer = If(p.NumberingId > 0, p.NumberingId, If(Not String.IsNullOrEmpty(p.ListNumValue), 2, 1))
                pPr.NumberingProperties = New OX.NumberingProperties(New OX.NumberingLevelReference() With {.Val = p.NumberingLevel}, New OX.NumberingId() With {.Val = numId})
            End If

            If p.SpacingBefore > 0 OrElse p.SpacingAfter > 0 OrElse p.LineSpacing > 0 Then
                Dim lineVal As Integer = CInt(p.LineSpacing * 240)
                If lineVal < 240 Then lineVal = 240
                pPr.SpacingBetweenLines = New OX.SpacingBetweenLines() With {
                    .Before = CInt(p.SpacingBefore * 20).ToString(),
                    .After = CInt(p.SpacingAfter * 20).ToString(),
                    .Line = lineVal.ToString(),
                    .LineRule = OX.LineSpacingRuleValues.Auto
                }
            End If

            If p.IndentLevel > 0 Then
                pPr.Indentation = New OX.Indentation() With {.Left = (p.IndentLevel * 720).ToString()}
            End If

            Dim allineamentoVal As OX.JustificationValues = OX.JustificationValues.Left
            If p.Alignment.Equals("Center", StringComparison.OrdinalIgnoreCase) Then allineamentoVal = OX.JustificationValues.Center
            If p.Alignment.Equals("Right", StringComparison.OrdinalIgnoreCase) Then allineamentoVal = OX.JustificationValues.Right
            If p.Alignment.Equals("Justify", StringComparison.OrdinalIgnoreCase) Then allineamentoVal = OX.JustificationValues.Both
            pPr.Justification = New OX.Justification() With {.Val = allineamentoVal}

            paragrafoOX.ParagraphProperties = pPr

            Try
                If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso Not p.BookmarkId.StartsWith("NOTA_PIE_") AndAlso Not p.BookmarkId.StartsWith("NOTA_FINE_") Then
                    Dim nomeBookmark As String = Regex.Replace(p.BookmarkId, "[^A-Za-z0-9_]", "_")
                    If Not String.IsNullOrEmpty(nomeBookmark) Then
                        Dim idBk As Integer = _bookmarkSaveCounter
                        paragrafoOX.AppendChild(New OX.BookmarkStart() With {.Id = idBk.ToString(), .Name = nomeBookmark})
                        paragrafoOX.AppendChild(New OX.BookmarkEnd() With {.Id = idBk.ToString()})
                        _bookmarkSaveCounter += 1
                    End If
                End If
            Catch exBk As Exception
                logger.ScriviLog("CostruisciParagrafoOX bookmark: " & exBk.Message)
            End Try

            Dim idsCommentiPara As List(Of Integer) = Nothing
            If assegnazioneCommenti IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.BookmarkId) AndAlso assegnazioneCommenti.ContainsKey(p.BookmarkId) Then
                idsCommentiPara = assegnazioneCommenti(p.BookmarkId)
            End If
            If idsCommentiPara IsNot Nothing Then
                For Each idCmt In idsCommentiPara
                    paragrafoOX.AppendChild(New OX.CommentRangeStart() With {.Id = idCmt.ToString()})
                Next
            End If

            For Each run As DocRun In p.Runs
                AggiungiRunOX(paragrafoOX, mainPart, run, noteAPiePagina, noteDiChiusura, mappaHyperlinkRid, mappaRidInOutput, immaginiUsate, mappaImmagineRidOX)
            Next

            If idsCommentiPara IsNot Nothing Then
                For Each idCmt In idsCommentiPara
                    paragrafoOX.AppendChild(New OX.CommentRangeEnd() With {.Id = idCmt.ToString()})
                    Dim runRif As New OX.Run()
                    runRif.RunProperties = New OX.RunProperties(New OX.RunStyle() With {.Val = "CommentReference"})
                    runRif.AppendChild(New OX.CommentReference() With {.Id = idCmt.ToString()})
                    paragrafoOX.AppendChild(runRif)
                Next
            End If

            Return paragrafoOX
        Catch ex As Exception
            logger.ScriviLog("CostruisciParagrafoOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New OX.Paragraph()
        End Try
    End Function

    Private Shared Sub AggiungiRunOX(paragrafoOX As OX.Paragraph, mainPart As MainDocumentPart, run As DocRun, noteAPiePagina As Dictionary(Of Integer, String), noteDiChiusura As Dictionary(Of Integer, String), mappaHyperlinkRid As Dictionary(Of String, String), mappaRidInOutput As Dictionary(Of String, String), immaginiUsate As Dictionary(Of String, Byte()), mappaImmagineRidOX As Dictionary(Of String, String))
        Try
            If run.IsImage Then
                Dim chiave As String = OttieniChiaveRisoluzioneImmagine(run)
                If String.IsNullOrEmpty(chiave) OrElse Not mappaRidInOutput.ContainsKey(chiave) Then
                    Return
                End If
                Dim nomeFileOutput As String = mappaRidInOutput(chiave)
                If Not mappaImmagineRidOX.ContainsKey(nomeFileOutput) Then
                    Dim tipoImmagine As PartTypeInfo = ImagePartType.Png
                    Dim est As String = Path.GetExtension(nomeFileOutput).TrimStart("."c).ToLower()
                    Select Case est
                        Case "jpg", "jpeg" : tipoImmagine = ImagePartType.Jpeg
                        Case "gif" : tipoImmagine = ImagePartType.Gif
                        Case "bmp" : tipoImmagine = ImagePartType.Bmp
                        Case Else : tipoImmagine = ImagePartType.Png
                    End Select
                    Dim imagePart As ImagePart = mainPart.AddImagePart(tipoImmagine)
                    Using stream As New MemoryStream(immaginiUsate(nomeFileOutput))
                        imagePart.FeedData(stream)
                    End Using
                    mappaImmagineRidOX(nomeFileOutput) = mainPart.GetIdOfPart(imagePart)
                End If
                Dim ridOutput As String = mappaImmagineRidOX(nomeFileOutput)
                If Not String.IsNullOrEmpty(run.RawContent) AndAlso IsXmlSafe(run.RawContent) Then
                    Dim rawAggiornato As String = AggiornRidInRawContent(run.RawContent, chiave, ridOutput)
                    Dim runImg As New OX.Run()
                    Dim xmlDaIniettare As String = ""
                    If Not String.IsNullOrEmpty(run.RawProperties) AndAlso IsXmlSafe(run.RawProperties) Then xmlDaIniettare &= run.RawProperties
                    xmlDaIniettare &= rawAggiornato
                    runImg.InnerXml = xmlDaIniettare
                    paragrafoOX.AppendChild(runImg)
                End If
                Return
            End If

            If Not String.IsNullOrEmpty(run.LinkUrl) AndAlso run.LinkUrl.StartsWith("NOTA_PIE_") Then
                Dim numVal As Integer
                If Integer.TryParse(run.LinkUrl.Substring("NOTA_PIE_".Length), numVal) AndAlso noteAPiePagina IsNot Nothing AndAlso noteAPiePagina.ContainsKey(numVal) Then
                    Dim runNota As New OX.Run()
                    runNota.RunProperties = New OX.RunProperties(New OX.RunStyle() With {.Val = "FootnoteReference"})
                    runNota.AppendChild(New OX.FootnoteReference() With {.Id = numVal})
                    paragrafoOX.AppendChild(runNota)
                Else
                    paragrafoOX.AppendChild(CostruisciRunOX(run, run.Text))
                End If
                Return
            End If

            If Not String.IsNullOrEmpty(run.LinkUrl) AndAlso run.LinkUrl.StartsWith("NOTA_FINE_") Then
                Dim numVal As Integer
                If Integer.TryParse(run.LinkUrl.Substring("NOTA_FINE_".Length), numVal) AndAlso noteDiChiusura IsNot Nothing AndAlso noteDiChiusura.ContainsKey(numVal) Then
                    Dim runNota As New OX.Run()
                    runNota.RunProperties = New OX.RunProperties(New OX.RunStyle() With {.Val = "EndnoteReference"})
                    runNota.AppendChild(New OX.EndnoteReference() With {.Id = numVal})
                    paragrafoOX.AppendChild(runNota)
                Else
                    paragrafoOX.AppendChild(CostruisciRunOX(run, run.Text))
                End If
                Return
            End If

            If Not String.IsNullOrEmpty(run.LinkUrl) AndAlso Not run.LinkUrl.StartsWith("NOTA_PIE_") AndAlso Not run.LinkUrl.StartsWith("NOTA_FINE_") AndAlso Not run.LinkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                Dim idAncora As String = If(run.LinkUrl.StartsWith("#"), run.LinkUrl.Substring(1), run.LinkUrl)
                Dim nomeAncora As String = Regex.Replace(idAncora, "[^A-Za-z0-9_]", "_")
                If Not String.IsNullOrEmpty(nomeAncora) Then
                    Dim link As New OX.Hyperlink() With {.Anchor = nomeAncora, .History = True}
                    link.AppendChild(CostruisciRunOX(run, run.Text))
                    paragrafoOX.AppendChild(link)
                Else
                    paragrafoOX.AppendChild(CostruisciRunOX(run, run.Text))
                End If
                Return
            End If

            If Not String.IsNullOrEmpty(run.LinkUrl) AndAlso run.LinkUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                Dim ridLink As String = OttieniRidHyperlink(mainPart, mappaHyperlinkRid, run.LinkUrl)
                Dim link As New OX.Hyperlink() With {.Id = ridLink, .History = True}
                link.AppendChild(CostruisciRunOX(run, run.Text))
                paragrafoOX.AppendChild(link)
                Return
            End If

            Dim testo As String = run.Text
            Dim ultimoIndice As Integer = 0
            Dim corrispondenze = Regex.Matches(testo, "(https?://[^\s]+)", RegexOptions.None, TimeSpan.FromSeconds(2))
            If corrispondenze.Count > 0 Then
                For Each m As Match In corrispondenze
                    If m.Index > ultimoIndice Then paragrafoOX.AppendChild(CostruisciRunOX(run, testo.Substring(ultimoIndice, m.Index - ultimoIndice)))
                    Dim ridLink As String = OttieniRidHyperlink(mainPart, mappaHyperlinkRid, m.Value)
                    Dim link As New OX.Hyperlink() With {.Id = ridLink, .History = True}
                    Dim runLink As DocRun = run.Clone()
                    runLink.ColorHex = "Blu"
                    runLink.Underline = True
                    link.AppendChild(CostruisciRunOX(runLink, m.Value))
                    paragrafoOX.AppendChild(link)
                    ultimoIndice = m.Index + m.Length
                Next
                If ultimoIndice < testo.Length Then paragrafoOX.AppendChild(CostruisciRunOX(run, testo.Substring(ultimoIndice)))
            Else
                paragrafoOX.AppendChild(CostruisciRunOX(run, testo))
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiungiRunOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function OttieniRidHyperlink(mainPart As MainDocumentPart, mappaHyperlinkRid As Dictionary(Of String, String), url As String) As String
        Try
            If mappaHyperlinkRid.ContainsKey(url) Then Return mappaHyperlinkRid(url)
            Dim relazione = mainPart.AddHyperlinkRelationship(New Uri(url, UriKind.Absolute), True)
            mappaHyperlinkRid(url) = relazione.Id
            Return relazione.Id
        Catch ex As Exception
            logger.ScriviLog("OttieniRidHyperlink: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub AggiungiTestoConACapo(runOX As OX.Run, testo As String)
        Try
            Dim segmenti() As String = If(testo, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
            For s As Integer = 0 To segmenti.Length - 1
                If s > 0 Then runOX.AppendChild(New OX.Break())
                If segmenti(s).Length > 0 Then
                    Dim testoOX As New OX.Text(segmenti(s))
                    testoOX.Space = SpaceProcessingModeValues.Preserve
                    runOX.AppendChild(testoOX)
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("AggiungiTestoConACapo: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CostruisciRunOX(run As DocRun, testo As String) As OX.Run
        Try
            Dim runOX As New OX.Run()
            Dim rPr As New OX.RunProperties()

            If Not String.IsNullOrEmpty(run.StyleId) Then rPr.RunStyle = New OX.RunStyle() With {.Val = run.StyleId}
            If run.Bold Then rPr.Bold = New OX.Bold()
            If run.Italic Then rPr.Italic = New OX.Italic()
            If run.Underline Then rPr.Underline = New OX.Underline() With {.Val = OX.UnderlineValues.Single}
            If run.Strikethrough Then rPr.Strike = New OX.Strike()
            If run.SmallCaps Then rPr.SmallCaps = New OX.SmallCaps()
            If run.AllCaps Then rPr.Caps = New OX.Caps()
            If run.Hidden Then rPr.Vanish = New OX.Vanish()
            If run.IsSuperscript Then rPr.VerticalTextAlignment = New OX.VerticalTextAlignment() With {.Val = OX.VerticalPositionValues.Superscript}
            If run.IsSubscript Then rPr.VerticalTextAlignment = New OX.VerticalTextAlignment() With {.Val = OX.VerticalPositionValues.Subscript}

            Dim fontFamily As String = If(String.IsNullOrWhiteSpace(run.FontFamily), "Arial", run.FontFamily.Trim())
            rPr.RunFonts = New OX.RunFonts() With {.Ascii = fontFamily, .HighAnsi = fontFamily, .ComplexScript = fontFamily}

            Dim hexColor As String = ConvertNameColorToHex(run.ColorHex)
            Try
                ' Un run di ipertesto punta sempre al ruolo di tema "hyperlink", coerentemente
                ' con lo stile Hyperlink (w:themeColor="hyperlink") già usato in AssicuraTemaOX.
                If run.IsHyperlink Then
                    Dim hexHlink As String = HexPerRuoloTema("hlink", PaletteTemaAttiva())
                    If Not String.IsNullOrEmpty(hexHlink) Then hexColor = hexHlink
                End If
            Catch exHlink As Exception
                logger.ScriviLog($"CostruisciRunOX: verifica IsHyperlink fallita. Errore: {exHlink.Message}")
            End Try
            If hexColor <> "000000" AndAlso hexColor <> "auto" Then
                Dim colorOx As New OX.Color() With {.Val = hexColor}
                Dim ruoloTemaColore As String = RuoloTemaPerHex(hexColor, PaletteTemaAttiva())
                If Not String.IsNullOrEmpty(ruoloTemaColore) Then
                    Dim tcv As OX.ThemeColorValues? = ThemeColorValuePerRuolo(ruoloTemaColore)
                    If tcv.HasValue Then colorOx.ThemeColor = New EnumValue(Of OX.ThemeColorValues)(tcv.Value)
                End If
                rPr.Color = colorOx
            End If

            Dim hlColor As String = ConvertNameColorToHighlight(run.HighlightColorHex)
            If hlColor <> "none" Then rPr.Highlight = New OX.Highlight() With {.Val = New OX.HighlightColorValues(hlColor)}

            If run.SpacingPt <> 0 Then rPr.Spacing = New OX.Spacing() With {.Val = CInt(run.SpacingPt * 20)}
            If run.ScalePercent > 0 AndAlso run.ScalePercent <> 100 Then rPr.CharacterScale = New OX.CharacterScale() With {.Val = run.ScalePercent}
            If Not String.IsNullOrEmpty(run.Language) Then rPr.Languages = New OX.Languages() With {.Val = run.Language}

            Dim dimensioneMezziPunti As String = CInt(System.Math.Max(8, run.FontSize) * 2).ToString()
            rPr.FontSize = New OX.FontSize() With {.Val = dimensioneMezziPunti}
            rPr.FontSizeComplexScript = New OX.FontSizeComplexScript() With {.Val = dimensioneMezziPunti}

            runOX.RunProperties = rPr

            AggiungiTestoConACapo(runOX, testo)

            Return runOX
        Catch ex As Exception
            logger.ScriviLog("CostruisciRunOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New OX.Run(New OX.Text(testo))
        End Try
    End Function

    Private Shared Function CostruisciTabellaOX(mainPart As MainDocumentPart, t As DocTable, stiliTitolo As Dictionary(Of String, Integer), assegnazioneCommenti As Dictionary(Of String, List(Of Integer)), noteAPiePagina As Dictionary(Of Integer, String), noteDiChiusura As Dictionary(Of Integer, String), mappaHyperlinkRid As Dictionary(Of String, String), mappaRidInOutput As Dictionary(Of String, String), immaginiUsate As Dictionary(Of String, Byte()), mappaImmagineRidOX As Dictionary(Of String, String)) As OX.Table
        Try
            Dim tabellaOX As New OX.Table()

            Dim bordi As New OX.TableBorders(
                New OX.TopBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0},
                New OX.LeftBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0},
                New OX.BottomBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0},
                New OX.RightBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0},
                New OX.InsideHorizontalBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0},
                New OX.InsideVerticalBorder() With {.Val = OX.BorderValues.Single, .Size = 4, .Space = 0})

            Dim tblPr As New OX.TableProperties(New OX.TableWidth() With {.Width = "0", .Type = OX.TableWidthUnitValues.Auto}, bordi)
            tabellaOX.AppendChild(tblPr)

            Dim numeroColonne As Integer = 0
            If t.Rows IsNot Nothing Then
                For Each rigaConta As DocTableRow In t.Rows
                    If rigaConta Is Nothing OrElse rigaConta.Cells Is Nothing Then Continue For
                    If rigaConta.Cells.Count > numeroColonne Then numeroColonne = rigaConta.Cells.Count
                Next
            End If
            If numeroColonne < 1 Then numeroColonne = 1

            Dim larghezzeColonne(numeroColonne - 1) As Integer
            If t.Rows IsNot Nothing Then
                For Each rigaLarg As DocTableRow In t.Rows
                    If rigaLarg Is Nothing OrElse rigaLarg.Cells Is Nothing Then Continue For
                    For c As Integer = 0 To System.Math.Min(rigaLarg.Cells.Count, numeroColonne) - 1
                        Dim cellaLarg As DocTableCell = rigaLarg.Cells(c)
                        If cellaLarg IsNot Nothing AndAlso cellaLarg.WidthTwips > larghezzeColonne(c) Then
                            larghezzeColonne(c) = cellaLarg.WidthTwips
                        End If
                    Next
                Next
            End If

            Dim larghezzaPredefinita As Integer = CInt(9350 / numeroColonne)
            Dim grigliaOX As New OX.TableGrid()
            For c As Integer = 0 To numeroColonne - 1
                Dim larghezzaColonna As Integer = larghezzeColonne(c)
                If larghezzaColonna <= 0 Then larghezzaColonna = larghezzaPredefinita
                grigliaOX.AppendChild(New OX.GridColumn() With {.Width = larghezzaColonna.ToString()})
            Next
            tabellaOX.AppendChild(grigliaOX)

            For Each row As DocTableRow In t.Rows
                Dim rigaOX As New OX.TableRow()
                If row.IsHeader Then
                    rigaOX.AppendChild(New OX.TableRowProperties(New OX.TableHeader()))
                End If
                For Each cell As DocTableCell In row.Cells
                    Dim cellaOX As New OX.TableCell()
                    Dim tcPr As New OX.TableCellProperties()
                    If cell.WidthTwips > 0 Then
                        tcPr.TableCellWidth = New OX.TableCellWidth() With {.Width = cell.WidthTwips.ToString(), .Type = OX.TableWidthUnitValues.Dxa}
                    Else
                        tcPr.TableCellWidth = New OX.TableCellWidth() With {.Width = "0", .Type = OX.TableWidthUnitValues.Auto}
                    End If
                    If cell.ColSpan > 1 Then tcPr.GridSpan = New OX.GridSpan() With {.Val = cell.ColSpan}
                    If cell.RowSpan > 1 Then tcPr.VerticalMerge = New OX.VerticalMerge() With {.Val = OX.MergedCellValues.Restart}
                    Dim shadingHex As String = ConvertNameColorToHex(cell.ShadingColorHex)
                    If shadingHex <> "auto" AndAlso shadingHex <> "000000" Then
                        tcPr.Shading = New OX.Shading() With {.Val = OX.ShadingPatternValues.Clear, .Color = "auto", .Fill = shadingHex}
                    End If
                    cellaOX.TableCellProperties = tcPr

                    For Each p As DocParagraph In cell.Paragraphs
                        cellaOX.AppendChild(CostruisciParagrafoOX(mainPart, p, stiliTitolo, assegnazioneCommenti, noteAPiePagina, noteDiChiusura, mappaHyperlinkRid, mappaRidInOutput, immaginiUsate, mappaImmagineRidOX))
                    Next
                    If cell.Paragraphs.Count = 0 Then
                        cellaOX.AppendChild(New OX.Paragraph(New OX.ParagraphProperties(New OX.SpacingBetweenLines() With {.After = "0"})))
                    End If

                    rigaOX.AppendChild(cellaOX)
                Next
                tabellaOX.AppendChild(rigaOX)
            Next

            Return tabellaOX
        Catch ex As Exception
            logger.ScriviLog("CostruisciTabellaOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New OX.Table()
        End Try
    End Function

    Private Shared Sub AggiungiImpostazioniPaginaOX(sectPr As OX.SectionProperties, pageSettings As Dictionary(Of String, Integer))
        Try
            Dim pw As Integer = 11906, ph As Integer = 16838, mt As Integer = 1417, mr As Integer = 1417, mb As Integer = 1417, ml As Integer = 1417
            Dim orientLandscape As Boolean = False
            If pageSettings IsNot Nothing Then
                If pageSettings.ContainsKey("PageWidth") Then pw = pageSettings("PageWidth")
                If pageSettings.ContainsKey("PageHeight") Then ph = pageSettings("PageHeight")
                If pageSettings.ContainsKey("MarginTop") Then mt = pageSettings("MarginTop")
                If pageSettings.ContainsKey("MarginRight") Then mr = pageSettings("MarginRight")
                If pageSettings.ContainsKey("MarginBottom") Then mb = pageSettings("MarginBottom")
                If pageSettings.ContainsKey("MarginLeft") Then ml = pageSettings("MarginLeft")
                If pageSettings.ContainsKey("Orientation") Then orientLandscape = (pageSettings("Orientation") = 1)
            End If

            Dim pgSz As New OX.PageSize() With {.Width = CUInt(pw), .Height = CUInt(ph)}
            If orientLandscape Then pgSz.Orient = New EnumValue(Of OX.PageOrientationValues)(OX.PageOrientationValues.Landscape)
            sectPr.AppendChild(pgSz)

            sectPr.AppendChild(New OX.PageMargin() With {
                .Top = mt, .Right = CUInt(mr), .Bottom = mb, .Left = CUInt(ml),
                .Header = 708, .Footer = 708, .Gutter = 0
            })
        Catch ex As Exception
            logger.ScriviLog("AggiungiImpostazioniPaginaOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AggiungiHeaderOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, sectPr As OX.SectionProperties, headerText As String)
        Try
            Dim partiHeaderEsistenti As New List(Of HeaderPart)(mainPart.HeaderParts)
            For Each vecchiaParte As HeaderPart In partiHeaderEsistenti
                mainPart.DeletePart(vecchiaParte)
            Next
            Dim headerPart As HeaderPart = mainPart.AddNewPart(Of HeaderPart)()
            Dim header As New OX.Header()
            Dim para As New OX.Paragraph()
            Dim runTesto As New OX.Run()
            AggiungiTestoConACapo(runTesto, headerText)
            para.AppendChild(runTesto)
            header.AppendChild(para)
            headerPart.Header = header
            headerPart.Header.Save()

            sectPr.AppendChild(New OX.HeaderReference() With {.Type = OX.HeaderFooterValues.Default, .Id = mainPart.GetIdOfPart(headerPart)})
        Catch ex As Exception
            logger.ScriviLog("AggiungiHeaderOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub AggiungiFooterOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, sectPr As OX.SectionProperties, footerText As String)
        Try
            Dim partiFooterEsistenti As New List(Of FooterPart)(mainPart.FooterParts)
            For Each vecchiaParte As FooterPart In partiFooterEsistenti
                mainPart.DeletePart(vecchiaParte)
            Next
            Dim footerPart As FooterPart = mainPart.AddNewPart(Of FooterPart)()
            Dim footer As New OX.Footer()
            Dim para As New OX.Paragraph()
            Dim runTesto As New OX.Run()
            AggiungiTestoConACapo(runTesto, footerText)
            para.AppendChild(runTesto)
            footer.AppendChild(para)
            footerPart.Footer = footer
            footerPart.Footer.Save()

            sectPr.AppendChild(New OX.FooterReference() With {.Type = OX.HeaderFooterValues.Default, .Id = mainPart.GetIdOfPart(footerPart)})
        Catch ex As Exception
            logger.ScriviLog("AggiungiFooterOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CostruisciNumberingPartOX(mainPart As MainDocumentPart)
        Try
            If mainPart.NumberingDefinitionsPart IsNot Nothing Then Return
            Dim numberingPart As NumberingDefinitionsPart = mainPart.AddNewPart(Of NumberingDefinitionsPart)()
            Dim sbNum As New StringBuilder()
            sbNum.Append("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>")
            sbNum.Append("<w:numbering xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">")
            sbNum.Append("<w:abstractNum w:abstractNumId=""0"">")
            For lvl As Integer = 0 To 8
                sbNum.AppendFormat("<w:lvl w:ilvl=""{0}""><w:start w:val=""1""/><w:numFmt w:val=""bullet""/><w:lvlText w:val=""&#8226;""/><w:lvlJc w:val=""left""/><w:pPr><w:ind w:left=""{1}"" w:hanging=""360""/></w:pPr><w:rPr><w:rFonts w:ascii=""Symbol"" w:hAnsi=""Symbol"" w:hint=""default""/></w:rPr></w:lvl>", lvl, 720 * (lvl + 1))
            Next
            sbNum.Append("</w:abstractNum>")
            sbNum.Append("<w:abstractNum w:abstractNumId=""1"">")
            For lvl As Integer = 0 To 8
                sbNum.AppendFormat("<w:lvl w:ilvl=""{0}""><w:start w:val=""1""/><w:numFmt w:val=""decimal""/><w:lvlText w:val=""%{1}.""/><w:lvlJc w:val=""left""/><w:pPr><w:ind w:left=""{2}"" w:hanging=""360""/></w:pPr></w:lvl>", lvl, lvl + 1, 720 * (lvl + 1))
            Next
            sbNum.Append("</w:abstractNum>")
            sbNum.Append("<w:num w:numId=""1""><w:abstractNumId w:val=""0""/></w:num>")
            sbNum.Append("<w:num w:numId=""2""><w:abstractNumId w:val=""1""/></w:num>")
            sbNum.Append("</w:numbering>")
            Using writer As New StreamWriter(numberingPart.GetStream(FileMode.Create, FileAccess.Write))
                writer.Write(sbNum.ToString())
            End Using
        Catch ex As Exception
            logger.ScriviLog("CostruisciNumberingPartOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CostruisciFootnotesPartOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, noteAPiePagina As Dictionary(Of Integer, String))
        Try
            If mainPart.FootnotesPart IsNot Nothing Then mainPart.DeletePart(mainPart.FootnotesPart)
            Dim footnotesPart As FootnotesPart = mainPart.AddNewPart(Of FootnotesPart)()
            Dim footnotes As New OX.Footnotes()

            Dim separatore As New OX.Footnote() With {.Type = OX.FootnoteEndnoteValues.Separator, .Id = -1}
            separatore.AppendChild(New OX.Paragraph(New OX.Run(New OX.SeparatorMark())))
            footnotes.AppendChild(separatore)

            Dim contSeparatore As New OX.Footnote() With {.Type = OX.FootnoteEndnoteValues.ContinuationSeparator, .Id = 0}
            contSeparatore.AppendChild(New OX.Paragraph(New OX.Run(New OX.ContinuationSeparatorMark())))
            footnotes.AppendChild(contSeparatore)

            Dim chiavi As New List(Of Integer)(noteAPiePagina.Keys)
            chiavi.Sort()
            For Each k As Integer In chiavi
                Dim nota As New OX.Footnote() With {.Id = k}
                Dim para As New OX.Paragraph()
                para.AppendChild(New OX.ParagraphProperties(New OX.ParagraphStyleId() With {.Val = "FootnoteText"}))
                Dim runRif As New OX.Run()
                runRif.RunProperties = New OX.RunProperties(New OX.RunStyle() With {.Val = "FootnoteReference"})
                runRif.AppendChild(New OX.FootnoteReferenceMark())
                para.AppendChild(runRif)
                Dim runTestoNota As New OX.Run()
                AggiungiTestoConACapo(runTestoNota, " " & noteAPiePagina(k))
                para.AppendChild(runTestoNota)
                nota.AppendChild(para)
                footnotes.AppendChild(nota)
            Next

            footnotesPart.Footnotes = footnotes
            footnotesPart.Footnotes.Save()
        Catch ex As Exception
            logger.ScriviLog("CostruisciFootnotesPartOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CostruisciEndnotesPartOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, noteDiChiusura As Dictionary(Of Integer, String))
        Try
            If mainPart.EndnotesPart IsNot Nothing Then mainPart.DeletePart(mainPart.EndnotesPart)
            Dim endnotesPart As EndnotesPart = mainPart.AddNewPart(Of EndnotesPart)()
            Dim endnotes As New OX.Endnotes()

            Dim separatore As New OX.Endnote() With {.Type = OX.FootnoteEndnoteValues.Separator, .Id = -1}
            separatore.AppendChild(New OX.Paragraph(New OX.Run(New OX.SeparatorMark())))
            endnotes.AppendChild(separatore)

            Dim contSeparatore As New OX.Endnote() With {.Type = OX.FootnoteEndnoteValues.ContinuationSeparator, .Id = 0}
            contSeparatore.AppendChild(New OX.Paragraph(New OX.Run(New OX.ContinuationSeparatorMark())))
            endnotes.AppendChild(contSeparatore)

            Dim chiavi As New List(Of Integer)(noteDiChiusura.Keys)
            chiavi.Sort()
            For Each k As Integer In chiavi
                Dim nota As New OX.Endnote() With {.Id = k}
                Dim para As New OX.Paragraph()
                para.AppendChild(New OX.ParagraphProperties(New OX.ParagraphStyleId() With {.Val = "EndnoteText"}))
                Dim runRif As New OX.Run()
                runRif.RunProperties = New OX.RunProperties(New OX.RunStyle() With {.Val = "EndnoteReference"})
                runRif.AppendChild(New OX.FootnoteReferenceMark())
                para.AppendChild(runRif)
                Dim runTestoNota As New OX.Run()
                AggiungiTestoConACapo(runTestoNota, " " & noteDiChiusura(k))
                para.AppendChild(runTestoNota)
                nota.AppendChild(para)
                endnotes.AppendChild(nota)
            Next

            endnotesPart.Endnotes = endnotes
            endnotesPart.Endnotes.Save()
        Catch ex As Exception
            logger.ScriviLog("CostruisciEndnotesPartOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CostruisciCommentsPartOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, mappaIdTesto As Dictionary(Of Integer, String))
        Try
            If mainPart.WordprocessingCommentsPart IsNot Nothing Then mainPart.DeletePart(mainPart.WordprocessingCommentsPart)
            Dim commentsPart As WordprocessingCommentsPart = mainPart.AddNewPart(Of WordprocessingCommentsPart)()
            Dim comments As New OX.Comments()

            Dim chiavi As New List(Of Integer)(mappaIdTesto.Keys)
            chiavi.Sort()
            For Each k As Integer In chiavi
                Dim commento As New OX.Comment() With {.Id = k.ToString(), .Author = "WinGabriel", .Initials = "WG", .Date = DateTime.Now}
                Dim para As New OX.Paragraph()
                para.AppendChild(New OX.Run(New OX.Text(mappaIdTesto(k)) With {.Space = SpaceProcessingModeValues.Preserve}))
                commento.AppendChild(para)
                comments.AppendChild(commento)
            Next

            commentsPart.Comments = comments
            commentsPart.Comments.Save()
        Catch ex As Exception
            logger.ScriviLog("CostruisciCommentsPartOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Garantisce che word/settings.xml dichiari un compatibilityMode "moderno" (15),
    ''' evitando che Word 2013/2016/2019/2021/365 apra il documento in Modalità compatibilità.
    ''' Funziona sia quando il DocumentSettingsPart viene creato da zero, sia quando proviene
    ''' da un file sorgente esistente (in tal caso il valore potrebbe essere assente o vecchio,
    ''' ad es. 11/12/14 se il file originale era stato salvato da Word 2007/2010/2013 in modo datato).
    ''' </summary>
    Private Shared Sub AssicuraCompatibilitaModernaOX(mainPart As MainDocumentPart)
        Try
            Dim settingsPart As DocumentSettingsPart = mainPart.DocumentSettingsPart
            If settingsPart Is Nothing Then
                settingsPart = mainPart.AddNewPart(Of DocumentSettingsPart)()
                settingsPart.Settings = New OX.Settings()
            End If

            Dim settings As OX.Settings = settingsPart.Settings
            If settings Is Nothing Then
                settings = New OX.Settings()
                settingsPart.Settings = settings
            End If

            Dim compat As OX.Compatibility = settings.Elements(Of OX.Compatibility)().FirstOrDefault()
            If compat Is Nothing Then
                compat = New OX.Compatibility()
                settings.AppendChild(compat)
            End If

            Dim compatSettingModalita As OX.CompatibilitySetting =
                compat.Elements(Of OX.CompatibilitySetting)().FirstOrDefault(
                    Function(cs) cs.Name IsNot Nothing AndAlso cs.Name.Value = OX.CompatSettingNameValues.CompatibilityMode)

            If compatSettingModalita Is Nothing Then
                compat.AppendChild(New OX.CompatibilitySetting() With {
                    .Name = OX.CompatSettingNameValues.CompatibilityMode,
                    .Uri = "http://schemas.microsoft.com/office/word",
                    .Val = "15"
                })
            Else
                compatSettingModalita.Val = "15"
            End If

            settings.Save()
        Catch ex As Exception
            logger.ScriviLog("AssicuraCompatibilitaModernaOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Aggiunge/aggiorna docProps/core.xml (proprietà standard, gestite automaticamente
    ''' dall'SDK tramite PackageProperties) e docProps/app.xml (ExtendedFileProperties),
    ''' dichiarando esplicitamente "Microsoft Office Word" come applicazione generatrice.
    ''' Senza queste parti il file resta un docx valido, ma alcuni strumenti (e talvolta
    ''' Word stesso in scenari di verifica IRM/criteri aziendali) trattano il file come
    ''' proveniente da una fonte non riconosciuta.
    ''' </summary>
    Private Shared Sub AssicuraProprietaDocumentoOX(wordDoc As WordprocessingDocument)
        Try
            Dim adesso As DateTime = DateTime.UtcNow
            Dim autore As String = OttieniNomeAutore()

            With wordDoc.PackageProperties
                If String.IsNullOrWhiteSpace(.Creator) Then .Creator = autore
                .LastModifiedBy = autore
                If Not .Created.HasValue Then .Created = adesso
                .Modified = adesso
            End With

            Dim extPart As ExtendedFilePropertiesPart = wordDoc.ExtendedFilePropertiesPart
            If extPart Is Nothing Then
                extPart = wordDoc.AddNewPart(Of ExtendedFilePropertiesPart)()
            End If

            Dim props As New DocumentFormat.OpenXml.ExtendedProperties.Properties()
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.Application("Microsoft Office Word"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.ApplicationVersion("16.0000"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.DocumentSecurity("0"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.ScaleCrop("false"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.LinksUpToDate("false"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.SharedDocument("false"))
            props.AppendChild(New DocumentFormat.OpenXml.ExtendedProperties.HyperlinksChanged("false"))
            extPart.Properties = props
            extPart.Properties.Save()
        Catch ex As Exception
            logger.ScriviLog("AssicuraProprietaDocumentoOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Aggiunge word/theme/theme1.xml se assente, usando la palette e i font standard
    ''' del tema "Office" di default. Serve a dare un riferimento coerente ai colori/font
    ''' di tema già citati in styles.xml (es. w:themeColor="hyperlink"), evitando che Word
    ''' debba ricostruirlo silenziosamente al salvataggio.
    ''' </summary>
    Private Shared Sub AssicuraTemaOX(mainPart As MainDocumentPart)
        Try
            If mainPart.ThemePart IsNot Nothing Then Return
            Dim themePart As ThemePart = mainPart.AddNewPart(Of ThemePart)()

            Dim temaXml As String =
                "<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>" &
                "<a:theme xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" name=""WinGabriel"">" &
                "<a:themeElements>" &
                "<a:clrScheme name=""WinGabriel"">" &
                "<a:dk1><a:sysClr val=""windowText"" lastClr=""000000""/></a:dk1>" &
                "<a:lt1><a:sysClr val=""window"" lastClr=""FFFFFF""/></a:lt1>" &
                "<a:dk2><a:srgbClr val=""44546A""/></a:dk2>" &
                "<a:lt2><a:srgbClr val=""E7E6E6""/></a:lt2>" &
                "<a:accent1><a:srgbClr val=""4472C4""/></a:accent1>" &
                "<a:accent2><a:srgbClr val=""ED7D31""/></a:accent2>" &
                "<a:accent3><a:srgbClr val=""A5A5A5""/></a:accent3>" &
                "<a:accent4><a:srgbClr val=""FFC000""/></a:accent4>" &
                "<a:accent5><a:srgbClr val=""5B9BD5""/></a:accent5>" &
                "<a:accent6><a:srgbClr val=""70AD47""/></a:accent6>" &
                "<a:hlink><a:srgbClr val=""0563C1""/></a:hlink>" &
                "<a:folHlink><a:srgbClr val=""954F72""/></a:folHlink>" &
                "</a:clrScheme>" &
                "<a:fontScheme name=""WinGabriel"">" &
                "<a:majorFont><a:latin typeface=""Calibri Light""/><a:ea typeface=""""/><a:cs typeface=""""/></a:majorFont>" &
                "<a:minorFont><a:latin typeface=""Calibri""/><a:ea typeface=""""/><a:cs typeface=""""/></a:minorFont>" &
                "</a:fontScheme>" &
                "<a:fmtScheme name=""WinGabriel"">" &
                "<a:fillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:fillStyleLst>" &
                "<a:lnStyleLst><a:ln w=""6350""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:ln><a:ln w=""12700""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:ln><a:ln w=""19050""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:ln></a:lnStyleLst>" &
                "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" &
                "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:bgFillStyleLst>" &
                "</a:fmtScheme>" &
                "</a:themeElements>" &
                "</a:theme>"

            Using writer As New StreamWriter(themePart.GetStream(FileMode.Create, FileAccess.Write))
                writer.Write(temaXml)
            End Using
        Catch ex As Exception
            logger.ScriviLog("AssicuraTemaOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>
    ''' Ricostruisce word/fontTable.xml elencando tutti i font effettivamente usati nel
    ''' documento (corpo, stili, numerazione, note, commenti, header/footer), invece di
    ''' lasciare la parte assente. Non è indispensabile per evitare la Modalità compatibilità,
    ''' ma è ciò che un documento salvato da Word contiene sempre.
    ''' </summary>
    Private Shared Sub AssicuraTabellaFontOX(mainPart As MainDocumentPart)
        Try
            Dim nomiFont As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            Dim raccogli As Action(Of OpenXmlElement) =
                Sub(radice)
                    If radice Is Nothing Then Return
                    For Each rf As OX.RunFonts In radice.Descendants(Of OX.RunFonts)()
                        If rf.Ascii IsNot Nothing Then nomiFont.Add(rf.Ascii.Value)
                        If rf.HighAnsi IsNot Nothing Then nomiFont.Add(rf.HighAnsi.Value)
                        If rf.ComplexScript IsNot Nothing Then nomiFont.Add(rf.ComplexScript.Value)
                        If rf.EastAsia IsNot Nothing Then nomiFont.Add(rf.EastAsia.Value)
                    Next
                End Sub

            raccogli(mainPart.Document)
            If mainPart.StyleDefinitionsPart IsNot Nothing Then raccogli(mainPart.StyleDefinitionsPart.Styles)
            If mainPart.NumberingDefinitionsPart IsNot Nothing Then raccogli(mainPart.NumberingDefinitionsPart.Numbering)
            If mainPart.FootnotesPart IsNot Nothing Then raccogli(mainPart.FootnotesPart.Footnotes)
            If mainPart.EndnotesPart IsNot Nothing Then raccogli(mainPart.EndnotesPart.Endnotes)
            If mainPart.WordprocessingCommentsPart IsNot Nothing Then raccogli(mainPart.WordprocessingCommentsPart.Comments)
            For Each hp As HeaderPart In mainPart.HeaderParts
                raccogli(hp.Header)
            Next
            For Each fp As FooterPart In mainPart.FooterParts
                raccogli(fp.Footer)
            Next

            nomiFont.Add("Arial")
            If nomiFont.Count = 0 Then Return

            If mainPart.FontTablePart IsNot Nothing Then mainPart.DeletePart(mainPart.FontTablePart)
            Dim fontTablePart As FontTablePart = mainPart.AddNewPart(Of FontTablePart)()

            Dim sbFont As New StringBuilder()
            sbFont.Append("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><w:fonts xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">")
            For Each nome As String In nomiFont
                sbFont.AppendFormat("<w:font w:name=""{0}""><w:family w:val=""auto""/><w:pitch w:val=""default""/></w:font>", System.Security.SecurityElement.Escape(nome))
            Next
            sbFont.Append("</w:fonts>")

            Using writer As New StreamWriter(fontTablePart.GetStream(FileMode.Create, FileAccess.Write))
                writer.Write(sbFont.ToString())
            End Using
        Catch ex As Exception
            logger.ScriviLog("AssicuraTabellaFontOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub CompletaOAggiungiStylesPartOX(wordDoc As WordprocessingDocument, mainPart As MainDocumentPart, haCommenti As Boolean, haFootnotes As Boolean, haEndnotes As Boolean)
        Try
            Dim stylesPart As StyleDefinitionsPart = mainPart.StyleDefinitionsPart
            If stylesPart IsNot Nothing Then
                Dim xmlOriginale As String
                Using reader As New StreamReader(stylesPart.GetStream(FileMode.Open, FileAccess.Read))
                    xmlOriginale = reader.ReadToEnd()
                End Using
                Dim xmlCompletato As String = CompletaStiliMancanti(xmlOriginale, haCommenti, haFootnotes, haEndnotes)
                If xmlCompletato <> xmlOriginale Then
                    Using writer As New StreamWriter(stylesPart.GetStream(FileMode.Create, FileAccess.Write))
                        writer.Write(xmlCompletato)
                    End Using
                End If
                Return
            End If

            stylesPart = mainPart.AddNewPart(Of StyleDefinitionsPart)()
            Dim sbStyles As New StringBuilder()
            sbStyles.Append("<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><w:styles xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">")
            sbStyles.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=""Arial"" w:hAnsi=""Arial"" w:cs=""Arial""/><w:sz w:val=""22""/><w:szCs w:val=""22""/><w:lang w:val=""it-IT""/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=""160"" w:line=""259"" w:lineRule=""auto""/></w:pPr></w:pPrDefault></w:docDefaults>")
            sbStyles.Append("<w:style w:type=""paragraph"" w:default=""1"" w:styleId=""Normale""><w:name w:val=""Normal""/><w:qFormat/></w:style>")
            sbStyles.Append("<w:style w:type=""character"" w:styleId=""Hyperlink""><w:name w:val=""Hyperlink""/><w:rPr><w:color w:val=""0563C1"" w:themeColor=""hyperlink""/><w:u w:val=""single""/></w:rPr></w:style>")
            Dim sizeHeading() As Integer = {36, 32, 28, 26, 24, 22}
            For livello As Integer = 1 To 6
                sbStyles.AppendFormat("<w:style w:type=""paragraph"" w:styleId=""Heading{0}""><w:name w:val=""heading {0}""/><w:basedOn w:val=""Normale""/><w:next w:val=""Normale""/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=""240"" w:after=""60""/><w:outlineLvl w:val=""{1}""/></w:pPr><w:rPr><w:b/><w:sz w:val=""{2}""/><w:szCs w:val=""{2}""/></w:rPr></w:style>", livello, livello - 1, sizeHeading(livello - 1))
            Next
            If haCommenti Then
                sbStyles.Append("<w:style w:type=""paragraph"" w:styleId=""CommentText""><w:name w:val=""annotation text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""20""/><w:szCs w:val=""20""/></w:rPr></w:style>")
                sbStyles.Append("<w:style w:type=""character"" w:styleId=""CommentReference""><w:name w:val=""annotation reference""/><w:rPr><w:sz w:val=""16""/><w:szCs w:val=""16""/></w:rPr></w:style>")
            End If
            If haFootnotes Then
                sbStyles.Append("<w:style w:type=""paragraph"" w:styleId=""FootnoteText""><w:name w:val=""footnote text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""18""/><w:szCs w:val=""18""/></w:rPr></w:style>")
                sbStyles.Append("<w:style w:type=""character"" w:styleId=""FootnoteReference""><w:name w:val=""footnote reference""/><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr></w:style>")
            End If
            If haEndnotes Then
                sbStyles.Append("<w:style w:type=""paragraph"" w:styleId=""EndnoteText""><w:name w:val=""endnote text""/><w:basedOn w:val=""Normale""/><w:rPr><w:sz w:val=""18""/><w:szCs w:val=""18""/></w:rPr></w:style>")
                sbStyles.Append("<w:style w:type=""character"" w:styleId=""EndnoteReference""><w:name w:val=""endnote reference""/><w:rPr><w:vertAlign w:val=""superscript""/></w:rPr></w:style>")
            End If
            sbStyles.Append("</w:styles>")

            Using writer As New StreamWriter(stylesPart.GetStream(FileMode.Create, FileAccess.Write))
                writer.Write(sbStyles.ToString())
            End Using
        Catch ex As Exception
            logger.ScriviLog("CompletaOAggiungiStylesPartOX: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

End Class