' ============================================================================================
' WGTestoFile - il testo contenuto in un file, per cercarci dentro
' ============================================================================================
'
' Legge il testo dei documenti più comuni senza aprirli a video, anche da un thread in sottofondo:
'   - testo semplice e codice (txt, md, csv, log, xml, json, html, sorgenti...);
'   - Word e OpenDocument (docx, odt) con WGWord;
'   - PDF con PdfPig;
'   - RTF togliendo i comandi di formattazione.
' I file più grandi di 30 MB si saltano, per non bloccare la ricerca.
'
'   If WGTestoFile.Supportato(percorso) Then testo = WGTestoFile.Estrai(percorso)
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions
Imports UglyToad.PdfPig

Public NotInheritable Class WGTestoFile

    Private Const DimensioneMassima As Long = 30L * 1024 * 1024
    Private Shared ReadOnly log As New Logger()

    Private Shared ReadOnly EstensioniTesto As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".txt", ".md", ".csv", ".log", ".ini", ".xml", ".json", ".yaml", ".yml", ".srt", ".vtt", ".nfo",
        ".vb", ".cs", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".css", ".sql", ".bat", ".ps1", ".tex"}
    Private Shared ReadOnly EstensioniDocumento As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".docx", ".odt", ".pdf", ".rtf", ".html", ".htm"}

    Private Sub New()
    End Sub

    ''' <summary>Vero se il tipo di file si sa leggere e il file non è troppo grande.</summary>
    Public Shared Function Supportato(percorso As String) As Boolean
        Try
            Dim estensione As String = Path.GetExtension(percorso)
            If Not EstensioniTesto.Contains(estensione) AndAlso Not EstensioniDocumento.Contains(estensione) Then Return False
            Return New FileInfo(percorso).Length <= DimensioneMassima
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Il testo del file; stringa vuota se non si riesce a leggerlo.</summary>
    Public Shared Function Estrai(percorso As String) As String
        Try
            Select Case Path.GetExtension(percorso).ToLowerInvariant()
                Case ".docx", ".odt"
                    Return If(WGWord.EstraiTestoCompleto(percorso), "")
                Case ".pdf"
                    Dim sb As New StringBuilder()
                    Using documento = PdfDocument.Open(percorso)
                        For Each pagina In documento.GetPages()
                            sb.AppendLine(pagina.Text)
                        Next
                    End Using
                    Return sb.ToString()
                Case ".rtf"
                    Return TestoDaRtf(File.ReadAllText(percorso, Encoding.Default))
                Case ".html", ".htm"
                    Return WGAi.TestoDaHtml(File.ReadAllText(percorso, Encoding.UTF8))
                Case Else
                    ' UTF-8 con riconoscimento del BOM; i vecchi file in codifica di Windows si leggono lo stesso nelle parti ASCII.
                    Return File.ReadAllText(percorso, Encoding.UTF8)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGTestoFile.Estrai " & percorso & ": " & ex.Message)
            Return ""
        End Try
    End Function

    ' RTF senza RichTextBox (che vuole il thread dell'interfaccia): si tolgono gruppi di controllo e comandi.
    Private Shared Function TestoDaRtf(rtf As String) As String
        Dim testo As String = Regex.Replace(rtf, "\{\\\*[^{}]*\}", " ")
        testo = Regex.Replace(testo, "\\'([0-9a-fA-F]{2})", Function(m) ChrW(Convert.ToInt32(m.Groups(1).Value, 16)).ToString())
        testo = Regex.Replace(testo, "\\par[d]?", vbLf)
        testo = Regex.Replace(testo, "\\[a-zA-Z]+-?\d* ?", " ")
        testo = testo.Replace("{", " ").Replace("}", " ")
        Return testo
    End Function

End Class
