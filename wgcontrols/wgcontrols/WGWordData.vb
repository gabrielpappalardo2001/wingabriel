Imports System.Collections.Generic
Imports System.Text

' Strutture dati per i documenti di testo, inclusi paragrafi, tabelle, righe, celle e proprietà di formattazione.
Public Class UndoState
    Public Elements As List(Of DocElement)
    Public CaretIndex As Integer
    Public TblRow As Integer
    Public TblCol As Integer
    Public TblPara As Integer
    Public CaretCharIndex As Integer
End Class

<System.Xml.Serialization.XmlInclude(GetType(DocParagraph))>
<System.Xml.Serialization.XmlInclude(GetType(DocTable))>
Public MustInherit Class DocElement
    <System.Xml.Serialization.XmlIgnore>
    Public Property CachedHeight As Integer = -1
    Public MustOverride Function Clone() As DocElement
End Class

Public Class DocParagraph
    Inherits DocElement
    Public Property Runs As List(Of DocRun)
    Public Property StyleId As String = ""
    Public Property HeadingLevel As Integer = 0
    Public Property OutlineLevel As Integer = -1
    Public Property IsListItem As Boolean = False
    Public Property RawProperties As String = ""
    Public Property Alignment As String = "Left"
    Public Property IsIndexContainer As Boolean = False
    Public Property BookmarkId As String = ""
    Public Property IndentLevel As Integer = 0
    Public Property IsPageBreak As Boolean = False
    Public Property PageBreakBefore As Boolean = False
    Public Property KeepWithNext As Boolean = False
    Public Property KeepLines As Boolean = False
    Public Property LineSpacing As Single = 1.0F
    Public Property SpacingBefore As Double = 0.0
    Public Property SpacingAfter As Double = 0.0
    Public Property ListBulletChar As String = ""
    Public Property ListNumValue As String = ""
    Public Property TabStops As List(Of Integer)
    Public Property IsHeader As Boolean = False
    Public Property NumberingId As Integer = 0
    Public Property NumberingLevel As Integer = 0

    Public Sub New()
        Try
            Runs = New List(Of DocRun)()
            TabStops = New List(Of Integer)()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocParagraph New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function DaOpenXml(para As DocumentFormat.OpenXml.Wordprocessing.Paragraph) As DocParagraph
        Dim result As New DocParagraph()
        Try
            If para Is Nothing Then Return result

            Dim pPr = para.ParagraphProperties
            If pPr IsNot Nothing Then
                Dim spacing = pPr.SpacingBetweenLines
                If spacing IsNot Nothing Then
                    Dim v As Integer = 0
                    If Not String.IsNullOrEmpty(spacing.Before) AndAlso Integer.TryParse(spacing.Before, v) Then
                        result.SpacingBefore = Math.Max(0, v / 20.0)
                    End If
                    v = 0
                    If Not String.IsNullOrEmpty(spacing.After) AndAlso Integer.TryParse(spacing.After, v) Then
                        result.SpacingAfter = Math.Max(0, v / 20.0)
                    End If
                    v = 0
                    If Not String.IsNullOrEmpty(spacing.Line) AndAlso Integer.TryParse(spacing.Line, v) AndAlso v > 0 Then
                        result.LineSpacing = CSng(Math.Round(v / 240.0, 2))
                    End If
                End If

                Dim jc = pPr.Justification
                If jc IsNot Nothing AndAlso jc.Val IsNot Nothing Then
                    Select Case jc.Val.Value
                        Case DocumentFormat.OpenXml.Wordprocessing.JustificationValues.Center
                            result.Alignment = "center"
                        Case DocumentFormat.OpenXml.Wordprocessing.JustificationValues.Right
                            result.Alignment = "right"
                        Case DocumentFormat.OpenXml.Wordprocessing.JustificationValues.Both
                            result.Alignment = "justify"
                        Case Else
                            result.Alignment = "left"
                    End Select
                End If

                Dim ind = pPr.Indentation
                If ind IsNot Nothing Then
                    Dim twipsLeft As Integer = 0
                    If Not String.IsNullOrEmpty(ind.Left) AndAlso Integer.TryParse(ind.Left, twipsLeft) Then
                        result.IndentLevel = CInt(Math.Floor(twipsLeft / 720.0))
                    End If
                End If

                Dim numPr = pPr.NumberingProperties
                If numPr IsNot Nothing Then
                    result.IsListItem = True
                End If
            End If

        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocParagraph.DaOpenXml: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
        Return result
    End Function

    Public Function GetText() As String
        Try
            Dim sb As New StringBuilder()
            If Runs Is Nothing Then Return ""
            For Each r As DocRun In Runs
                If r Is Nothing OrElse r.IsImage OrElse r.Text Is Nothing Then Continue For
                sb.Append(r.Text)
            Next
            Return sb.ToString()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocParagraph.GetText: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Public Function GetTextLength() As Integer
        Try
            Return GetText().Length
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocParagraph.GetTextLength: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return 0
        End Try
    End Function

    Public Overrides Function Clone() As DocElement
        Try
            Dim newP As New DocParagraph()
            newP.StyleId = Me.StyleId
            newP.HeadingLevel = Me.HeadingLevel
            newP.OutlineLevel = Me.OutlineLevel
            newP.IsListItem = Me.IsListItem
            newP.CachedHeight = Me.CachedHeight
            newP.RawProperties = Me.RawProperties
            newP.Alignment = Me.Alignment
            newP.IsIndexContainer = Me.IsIndexContainer
            newP.BookmarkId = Me.BookmarkId
            newP.IndentLevel = Me.IndentLevel
            newP.IsPageBreak = Me.IsPageBreak
            newP.PageBreakBefore = Me.PageBreakBefore
            newP.KeepWithNext = Me.KeepWithNext
            newP.KeepLines = Me.KeepLines
            newP.LineSpacing = Me.LineSpacing
            newP.SpacingBefore = Me.SpacingBefore
            newP.SpacingAfter = Me.SpacingAfter
            newP.ListBulletChar = Me.ListBulletChar
            newP.ListNumValue = Me.ListNumValue
            newP.IsHeader = Me.IsHeader
            newP.NumberingId = Me.NumberingId
            newP.NumberingLevel = Me.NumberingLevel
            If Me.TabStops IsNot Nothing Then
                For Each ts As Integer In Me.TabStops
                    newP.TabStops.Add(ts)
                Next
            End If
            If Me.Runs IsNot Nothing Then
                For Each r As DocRun In Me.Runs
                    If r Is Nothing Then Continue For
                    Dim copia As DocRun = r.Clone()
                    If copia IsNot Nothing Then newP.Runs.Add(copia)
                Next
            End If
            Return newP
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocParagraph.Clone: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class DocTable
    Inherits DocElement
    Public Property Rows As List(Of DocTableRow)
    Public Property SavedRow As Integer = 0
    Public Property SavedCol As Integer = 0
    Public Property SavedPara As Integer = 0
    Public Property SavedCharIndex As Integer = 0

    Public Sub New()
        Try
            Rows = New List(Of DocTableRow)()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTable New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Overrides Function Clone() As DocElement
        Try
            Dim newT As New DocTable()
            newT.SavedRow = Me.SavedRow
            newT.SavedCol = Me.SavedCol
            newT.SavedPara = Me.SavedPara
            newT.SavedCharIndex = Me.SavedCharIndex
            newT.CachedHeight = Me.CachedHeight
            If Me.Rows IsNot Nothing Then
                For Each r As DocTableRow In Me.Rows
                    If r Is Nothing Then Continue For
                    Dim copia As DocTableRow = r.Clone()
                    If copia IsNot Nothing Then newT.Rows.Add(copia)
                Next
            End If
            Return newT
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTable.Clone: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class DocTableRow
    Public Property Cells As List(Of DocTableCell)
    Public Property IsHeader As Boolean = False

    Public Sub New()
        Try
            Cells = New List(Of DocTableCell)()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTableRow New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Function Clone() As DocTableRow
        Try
            Dim newR As New DocTableRow()
            newR.IsHeader = Me.IsHeader
            If Me.Cells IsNot Nothing Then
                For Each c As DocTableCell In Me.Cells
                    If c Is Nothing Then Continue For
                    Dim copia As DocTableCell = c.Clone()
                    If copia IsNot Nothing Then newR.Cells.Add(copia)
                Next
            End If
            Return newR
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTableRow.Clone: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class DocTableCell
    Public Property Paragraphs As List(Of DocParagraph)
    Public Property ColSpan As Integer = 1
    Public Property RowSpan As Integer = 1
    Public Property ShadingColorHex As String = "Nessuno"
    Public Property WidthTwips As Integer = 0
    Public Property IsHeader As Boolean = False

    Public Sub New()
        Try
            Paragraphs = New List(Of DocParagraph)()
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTableCell New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Function LarghezzaPunti() As Double
        Try
            If WidthTwips > 0 Then Return WidthTwips / 20.0
            Return 0
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTableCell.LarghezzaPunti: {ex.Message}")
            Return 0
        End Try
    End Function

    Public Function Clone() As DocTableCell
        Try
            Dim newC As New DocTableCell()
            newC.ColSpan = Me.ColSpan
            newC.RowSpan = Me.RowSpan
            newC.ShadingColorHex = Me.ShadingColorHex
            newC.WidthTwips = Me.WidthTwips
            newC.IsHeader = Me.IsHeader
            If Me.Paragraphs IsNot Nothing Then
                For Each p As DocParagraph In Me.Paragraphs
                    If p Is Nothing Then Continue For
                    Dim copia As DocParagraph = TryCast(p.Clone(), DocParagraph)
                    If copia IsNot Nothing Then newC.Paragraphs.Add(copia)
                Next
            End If
            Return newC
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocTableCell.Clone: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class

Public Class DocRun
    Public Property Text As String = ""
    Public Property StyleId As String = ""
    Public Property Bold As Boolean = False
    Public Property Italic As Boolean = False
    Public Property FontSize As Single = 11.0F
    Public Property IsImage As Boolean = False
    Public Property RawContent As String = ""
    Public Property RawProperties As String = ""
    Public Property ColorHex As String = "Nero"
    Public Property HighlightColorHex As String = "Nessuno"
    Public Property Underline As Boolean = False
    Public Property Strikethrough As Boolean = False
    Public Property LinkUrl As String = ""
    Public Property FontFamily As String = "Arial"
    Public Property IsSuperscript As Boolean = False
    Public Property IsSubscript As Boolean = False
    Public Property IsHyperlink As Boolean = False
    Public Property SmallCaps As Boolean = False
    Public Property AllCaps As Boolean = False
    Public Property Hidden As Boolean = False
    Public Property Language As String = ""
    Public Property SpacingPt As Double = 0.0
    Public Property ScalePercent As Integer = 100

    <System.Xml.Serialization.XmlIgnore>
    Public Property ImageBytes As Byte() = Nothing
    Public Property ImageWidthPt As Double = 0
    Public Property ImageHeightPt As Double = 0
    Public Property OriginalRId As String = ""

    Public Function Clone() As DocRun
        Try
            Return CType(Me.MemberwiseClone(), DocRun)
        Catch ex As Exception
            Dim logger As New Logger()
            logger.ScriviLog($"DocRun.Clone: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Nothing
        End Try
    End Function
End Class