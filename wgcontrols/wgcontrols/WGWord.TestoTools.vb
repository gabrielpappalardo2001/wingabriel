Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

Partial Public Class WGWord
    Public Sub MostraSuggerimentiOrtografici()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return
            Dim stato = OttieniStatoCursore()
            If String.IsNullOrEmpty(stato.ParolaCorrente) Then
                Return

            End If

            Dim suggerimenti = _symSpell.Lookup(stato.ParolaCorrente, SymSpell.Verbosity.Closest)

            If suggerimenti Is Nothing OrElse suggerimenti.Count = 0 Then
                MostraMessaggioStato("Nessun suggerimento")
                Return
            End If

            If _menuSuggerimenti IsNot Nothing Then
                _menuSuggerimenti.abbandona()
                _menuSuggerimenti = Nothing
            End If


            _menuSuggerimenti = New WGMenuLista()
            _menuSuggerimenti.impostaTitolo("Suggerimenti")

            For Each sugg In suggerimenti
                _menuSuggerimenti.aggiungiElemento(sugg.term, sugg.term)
            Next

            AddHandler _menuSuggerimenti.ElementoSelezionato, AddressOf ApplicaSuggerimento
            _menuSuggerimenti.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApplicaSuggerimento()
        Try
            If _menuSuggerimenti IsNot Nothing Then
                Dim testi As List(Of String) = _menuSuggerimenti.ottieniElementi()
                If testi IsNot Nothing AndAlso testi.Count > 0 Then
                    Dim testo As String = testi(0)
                    Dim stato = OttieniStatoCursore()
                    SaveUndoState()

                    Dim testoComp As String = stato.TestoContenitore

                    Dim startI As Integer = Math.Min(Math.Max(0, _caretCharIndex), testoComp.Length - 1)
                    If startI < 0 Then
                        startI = 0
                    End If

                    Dim i As Integer = startI

                    While i > 0 AndAlso Not Char.IsWhiteSpace(testoComp(i - 1))
                        i -= 1
                    End While

                    Dim j As Integer = startI

                    While j < testoComp.Length AndAlso Not Char.IsWhiteSpace(testoComp(j))
                        j += 1
                    End While


                    _caretCharIndex = i
                    _selectionActive = True
                    _selAnchorIndex = _caretIndex
                    _selAnchorCharIndex = j

                    _selAnchorSubIndex = GetPackedTableIndex()

                    DeleteSelection()
                    For Each c In testo
                        InsertChar(c)
                    Next


                    _isModified = True
                    TriggerLayout()
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)
                End If
                _menuSuggerimenti.abbandona()

                _menuSuggerimenti = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuInserisciEmogi()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return

            If _menuInserisciEmogi IsNot Nothing Then
                _menuInserisciEmogi.abbandona()
                _menuInserisciEmogi = Nothing
            End If

            _menuInserisciEmogi = New WGMenuLista()
            _menuInserisciEmogi.impostaTitolo("Inserisci Emogi")

            If _gestoreEmogi IsNot Nothing Then
                Dim tutteLeEmogi = _gestoreEmogi.OttieniTutte()
                For Each kvp In tutteLeEmogi
                    _menuInserisciEmogi.aggiungiElemento(kvp.Value, kvp.Key)
                Next
            End If


            AddHandler _menuInserisciEmogi.ElementoSelezionato, AddressOf AzioneInserisciEmogi
            AddHandler _menuInserisciEmogi.escPremuto, AddressOf ChiudiMenuEmogi
            _menuInserisciEmogi.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AzioneInserisciEmogi()
        Try
            If _menuInserisciEmogi IsNot Nothing Then

                Dim ids As List(Of String) = _menuInserisciEmogi.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    Dim emogiScelta As String = ids(0)
                    SaveUndoState()

                    If _selectionActive Then
                        DeleteSelection()
                    End If
                    For Each c As Char In emogiScelta

                        InsertChar(c)
                    Next
                    _isModified = True
                    TriggerLayout()
                    InvalidateRect(Me.Handle, IntPtr.Zero, False)

                    UpdateCaretAndNotify(True)
                    If _gestoreEmogi IsNot Nothing Then
                        MostraMessaggioStato(_gestoreEmogi.OttieniDescrizione(emogiScelta) & " inserita")
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try

    End Sub

    Private Sub ChiudiMenuEmogi()
        Try
            If _menuInserisciEmogi IsNot Nothing Then
                _menuInserisciEmogi.abbandona()
                _menuInserisciEmogi = Nothing
            End If
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AumentaInterlinea()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                para.LineSpacing += PassoInterlinea

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Interlinea aumentata a " & para.LineSpacing.ToString("0.0"))
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RiduciInterlinea()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing AndAlso para.LineSpacing > 1.0F Then
                para.LineSpacing -= PassoInterlinea
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)

                MostraMessaggioStato("Interlinea ridotta a " & para.LineSpacing.ToString("0.0"))
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub


    Public Sub AumentaZoom()
        Try
            If _renderer IsNot Nothing Then
                _renderer.Zoom += PassoZoom
                InvalidaTutteLeAltezze()
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Zoom aumentato a " & Math.Round(_renderer.Zoom * 100).ToString() & " percento")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RiduciZoom()

        Try
            If _renderer IsNot Nothing AndAlso _renderer.Zoom > 0.2F Then
                _renderer.Zoom -= PassoZoom
                InvalidaTutteLeAltezze()
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Zoom ridotto a " & Math.Round(_renderer.Zoom * 100).ToString() & " percento")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAlLinkSuccessivo()

        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                Dim startI = Math.Max(0, _caretIndex)
                Dim startC = Math.Max(0, _caretCharIndex)
                Dim found As Boolean = False

                For i As Integer = startI To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)

                        Dim charCount As Integer = 0
                        For Each r As DocRun In p.Runs
                            If r.IsImage Then Continue For
                            Dim runLen = Math.Max(1, r.Text.Length)
                            If i = startI AndAlso charCount <= startC Then
                                charCount += runLen
                                Continue For

                            End If

                            If Not String.IsNullOrEmpty(r.LinkUrl) OrElse r.IsHyperlink Then
                                _caretIndex = i

                                _caretCharIndex = charCount
                                _selectionActive = False
                                found = True

                                TriggerLayout()
                                ScrollToCaret()
                                UpdateCaretAndNotify(False)

                                Dim linkTesto As String = r.Text
                                MostraMessaggioStato("Link successivo: " & linkTesto & ". Destinazione: " & r.LinkUrl)
                                Return
                            End If

                            charCount += runLen
                        Next
                    End If
                Next

                If Not found Then
                    MostraMessaggioStato("Nessun link successivo trovato.")
                End If

            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAlLinkPrecedente()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return

                Dim startI = Math.Min(_elements.Count - 1, Math.Max(0, _caretIndex))
                Dim startC = Math.Max(0, _caretCharIndex)
                Dim found As Boolean = False
                For i As Integer = startI To 0 Step -1

                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        Dim charCount As Integer = p.GetText().Length
                        For j As Integer = p.Runs.Count - 1 To 0 Step -1
                            Dim r As DocRun = p.Runs(j)
                            If r.IsImage Then Continue For
                            Dim runLen = Math.Max(1, r.Text.Length)

                            charCount -= runLen

                            If i = startI AndAlso charCount >= startC Then
                                Continue For

                            End If

                            If Not String.IsNullOrEmpty(r.LinkUrl) OrElse r.IsHyperlink Then
                                _caretIndex = i

                                _caretCharIndex = charCount
                                _selectionActive = False
                                found = True

                                TriggerLayout()
                                ScrollToCaret()
                                UpdateCaretAndNotify(False)

                                Dim linkTesto As String = r.Text
                                MostraMessaggioStato("Link precedente: " & linkTesto & ". Destinazione: " & r.LinkUrl)
                                Return
                            End If

                        Next
                    End If
                Next
                If Not found Then
                    MostraMessaggioStato("Nessun link precedente trovato.")
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Private Function ColoreEsadecimaleDaNome(nome As String) As String
        Try
            If String.IsNullOrEmpty(nome) Then Return "000000"
            Dim n As String = nome.Trim()
            If n.StartsWith("#") Then n = n.Substring(1)
            If n.Length = 6 AndAlso System.Text.RegularExpressions.Regex.IsMatch(n, "^[0-9A-Fa-f]{6}$") Then Return n.ToUpper()
            Select Case n.ToLower()
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
                Case Else : Return "000000"
            End Select
        Catch ex As Exception
            logger.ScriviLog("ColoreEsadecimaleDaNome: " & ex.Message)
            Return "000000"
        End Try
    End Function

    Private Function TestoRtfSicuro(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim sb As New StringBuilder()
            For Each c As Char In testo
                Select Case c
                    Case "\"c : sb.Append("\\")
                    Case "{"c : sb.Append("\{")
                    Case "}"c : sb.Append("\}")
                    Case ControlChars.Lf : sb.Append("\line ")
                    Case ControlChars.Cr
                    Case Else
                        If AscW(c) > 127 Then
                            sb.Append("\u").Append(AscW(c).ToString()).Append("?")
                        Else
                            sb.Append(c)
                        End If
                End Select
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("TestoRtfSicuro: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AccumulaRisorseRtf(para As DocParagraph, fonts As List(Of String), colori As List(Of String))
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage Then Continue For
                Dim famiglia As String = If(String.IsNullOrEmpty(r.FontFamily), "Arial", r.FontFamily)
                If Not fonts.Contains(famiglia) Then fonts.Add(famiglia)
                Dim col As String = ColoreEsadecimaleDaNome(r.ColorHex)
                If Not colori.Contains(col) Then colori.Add(col)
            Next
        Catch ex As Exception
            logger.ScriviLog("AccumulaRisorseRtf: " & ex.Message)
        End Try
    End Sub

    Private Function ParagrafoInRtf(para As DocParagraph, fonts As List(Of String), colori As List(Of String)) As String
        Try
            If para Is Nothing Then Return "\pard\par" & Environment.NewLine
            Dim sb As New StringBuilder()
            sb.Append("\pard")

            Dim allineamento As String = If(para.Alignment, "Left")
            If allineamento.Equals("Center", StringComparison.OrdinalIgnoreCase) Then
                sb.Append("\qc")
            ElseIf allineamento.Equals("Right", StringComparison.OrdinalIgnoreCase) Then
                sb.Append("\qr")
            ElseIf allineamento.Equals("Justify", StringComparison.OrdinalIgnoreCase) Then
                sb.Append("\qj")
            Else
                sb.Append("\ql")
            End If

            If para.IndentLevel > 0 Then sb.Append("\li").Append((para.IndentLevel * 567).ToString())

            If para.Runs IsNot Nothing Then
                For Each r As DocRun In para.Runs
                    If r Is Nothing OrElse r.IsImage Then Continue For
                    Dim testoRun As String = If(r.Text, "")
                    If testoRun.Length = 0 Then Continue For

                    Dim famiglia As String = If(String.IsNullOrEmpty(r.FontFamily), "Arial", r.FontFamily)
                    Dim indiceFont As Integer = Math.Max(0, fonts.IndexOf(famiglia))
                    Dim indiceColore As Integer = colori.IndexOf(ColoreEsadecimaleDaNome(r.ColorHex)) + 1

                    sb.Append("{")
                    sb.Append("\f").Append(indiceFont.ToString())
                    sb.Append("\fs").Append(CInt(Math.Max(1, r.FontSize) * 2).ToString())
                    If indiceColore > 0 Then sb.Append("\cf").Append(indiceColore.ToString())
                    If r.Bold Then sb.Append("\b")
                    If r.Italic Then sb.Append("\i")
                    If r.Underline OrElse r.IsHyperlink OrElse Not String.IsNullOrEmpty(r.LinkUrl) Then sb.Append("\ul")
                    If r.Strikethrough Then sb.Append("\strike")
                    If r.IsSuperscript Then sb.Append("\super")
                    If r.IsSubscript Then sb.Append("\sub")
                    sb.Append(" ")
                    sb.Append(TestoRtfSicuro(testoRun))
                    sb.Append("}")
                Next
            End If

            sb.Append("\par").Append(Environment.NewLine)
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("ParagrafoInRtf: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "\pard\par" & Environment.NewLine
        End Try
    End Function

    Private Function TabellaInRtf(tbl As DocTable, fonts As List(Of String), colori As List(Of String)) As String
        Try
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return ""
            Dim sb As New StringBuilder()

            Dim numeroColonne As Integer = 0
            For Each riga As DocTableRow In tbl.Rows
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                If riga.Cells.Count > numeroColonne Then numeroColonne = riga.Cells.Count
            Next
            If numeroColonne < 1 Then Return ""
            Dim larghezzaCella As Integer = CInt(9350 / numeroColonne)

            For Each riga As DocTableRow In tbl.Rows
                If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                sb.Append("\trowd\trgaph108")
                For c As Integer = 1 To numeroColonne
                    sb.Append("\clbrdrt\brdrs\clbrdrl\brdrs\clbrdrb\brdrs\clbrdrr\brdrs")
                    sb.Append("\cellx").Append((larghezzaCella * c).ToString())
                Next
                sb.Append(Environment.NewLine)

                For c As Integer = 0 To numeroColonne - 1
                    sb.Append("\pard\intbl\ql ")
                    If c < riga.Cells.Count Then
                        Dim cella As DocTableCell = riga.Cells(c)
                        If cella IsNot Nothing AndAlso cella.Paragraphs IsNot Nothing Then
                            For Each p As DocParagraph In cella.Paragraphs
                                If p Is Nothing OrElse p.Runs Is Nothing Then Continue For
                                For Each r As DocRun In p.Runs
                                    If r Is Nothing OrElse r.IsImage Then Continue For
                                    Dim testoRun As String = If(r.Text, "")
                                    If testoRun.Length = 0 Then Continue For
                                    Dim famiglia As String = If(String.IsNullOrEmpty(r.FontFamily), "Arial", r.FontFamily)
                                    Dim indiceFont As Integer = Math.Max(0, fonts.IndexOf(famiglia))
                                    Dim indiceColore As Integer = colori.IndexOf(ColoreEsadecimaleDaNome(r.ColorHex)) + 1
                                    sb.Append("{")
                                    sb.Append("\f").Append(indiceFont.ToString())
                                    sb.Append("\fs").Append(CInt(Math.Max(1, r.FontSize) * 2).ToString())
                                    If indiceColore > 0 Then sb.Append("\cf").Append(indiceColore.ToString())
                                    If r.Bold Then sb.Append("\b")
                                    If r.Italic Then sb.Append("\i")
                                    If r.Underline Then sb.Append("\ul")
                                    If r.Strikethrough Then sb.Append("\strike")
                                    If r.IsSuperscript Then sb.Append("\super")
                                    If r.IsSubscript Then sb.Append("\sub")
                                    sb.Append(" ")
                                    sb.Append(TestoRtfSicuro(testoRun))
                                    sb.Append("}")
                                Next
                            Next
                        End If
                    End If
                    sb.Append("\cell").Append(Environment.NewLine)
                Next
                sb.Append("\row").Append(Environment.NewLine)
            Next
            sb.Append("\pard").Append(Environment.NewLine)
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("TabellaInRtf: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Friend Function GeneraRtfDaElementi(elementi As List(Of DocElement)) As String
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""

            Dim fonts As New List(Of String)()
            Dim colori As New List(Of String)()

            For Each el As DocElement In elementi
                If TypeOf el Is DocParagraph Then
                    AccumulaRisorseRtf(DirectCast(el, DocParagraph), fonts, colori)
                ElseIf TypeOf el Is DocTable Then
                    Dim tbl As DocTable = DirectCast(el, DocTable)
                    If tbl.Rows IsNot Nothing Then
                        For Each riga As DocTableRow In tbl.Rows
                            If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                            For Each cella As DocTableCell In riga.Cells
                                If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                                For Each p As DocParagraph In cella.Paragraphs
                                    AccumulaRisorseRtf(p, fonts, colori)
                                Next
                            Next
                        Next
                    End If
                End If
            Next

            If fonts.Count = 0 Then fonts.Add("Arial")
            If colori.Count = 0 Then colori.Add("000000")

            Dim sb As New StringBuilder()
            sb.Append("{\rtf1\ansi\ansicpg1252\deff0")

            sb.Append("{\fonttbl")
            For i As Integer = 0 To fonts.Count - 1
                sb.Append("{\f").Append(i.ToString()).Append("\fnil ").Append(TestoRtfSicuro(fonts(i))).Append(";}")
            Next
            sb.Append("}")

            sb.Append("{\colortbl;")
            For Each col As String In colori
                Dim r As Integer = Convert.ToInt32(col.Substring(0, 2), 16)
                Dim g As Integer = Convert.ToInt32(col.Substring(2, 2), 16)
                Dim b As Integer = Convert.ToInt32(col.Substring(4, 2), 16)
                sb.Append("\red").Append(r.ToString()).Append("\green").Append(g.ToString()).Append("\blue").Append(b.ToString()).Append(";")
            Next
            sb.Append("}")
            sb.Append(Environment.NewLine)

            For Each el As DocElement In elementi
                If TypeOf el Is DocParagraph Then
                    sb.Append(ParagrafoInRtf(DirectCast(el, DocParagraph), fonts, colori))
                ElseIf TypeOf el Is DocTable Then
                    sb.Append(TabellaInRtf(DirectCast(el, DocTable), fonts, colori))
                End If
            Next

            sb.Append("}")
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("GeneraRtfDaElementi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function ParagrafoDaSubIndice(tbl As DocTable, subIndice As Integer) As DocParagraph
        Try
            If tbl Is Nothing OrElse tbl.Rows Is Nothing OrElse subIndice < 0 Then Return Nothing
            Dim r As Integer = subIndice \ 10000
            Dim c As Integer = (subIndice Mod 10000) \ 100
            Dim p As Integer = subIndice Mod 100
            If r < 0 OrElse r >= tbl.Rows.Count Then Return Nothing
            Dim riga As DocTableRow = tbl.Rows(r)
            If riga Is Nothing OrElse riga.Cells Is Nothing OrElse c < 0 OrElse c >= riga.Cells.Count Then Return Nothing
            Dim cella As DocTableCell = riga.Cells(c)
            If cella Is Nothing OrElse cella.Paragraphs Is Nothing OrElse p < 0 OrElse p >= cella.Paragraphs.Count Then Return Nothing
            Return cella.Paragraphs(p)
        Catch ex As Exception
            logger.ScriviLog("ParagrafoDaSubIndice: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Public Sub CopiaFormattato()
        Try
            If Not SelezioneAttivaNonVuota() Then
                MostraMessaggioStato("Nessun testo da copiare.")
                Return
            End If

            Dim selStartIdx, selEndIdx, selStartSub, selEndSub, selStartChar, selEndChar As Integer
            CalculateSelectionRange(selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)

            If _appuntiFormattati Is Nothing Then _appuntiFormattati = New List(Of DocRun)()
            _appuntiFormattati.Clear()

            Dim elementiDaCopiare As New List(Of DocElement)()

            SyncLock _elementsLock
                Dim paraSorgente As DocParagraph = Nothing

                If _elements IsNot Nothing AndAlso selStartIdx >= 0 AndAlso selStartIdx < _elements.Count AndAlso selStartIdx = selEndIdx Then
                    Dim elSel As DocElement = _elements(selStartIdx)
                    If TypeOf elSel Is DocParagraph Then
                        paraSorgente = DirectCast(elSel, DocParagraph)
                    ElseIf TypeOf elSel Is DocTable AndAlso selStartSub = selEndSub Then
                        paraSorgente = ParagrafoDaSubIndice(DirectCast(elSel, DocTable), selStartSub)
                    End If
                End If

                If paraSorgente IsNot Nothing AndAlso paraSorgente.Runs IsNot Nothing Then
                    Dim charAccumulator As Integer = 0

                    For Each r As DocRun In paraSorgente.Runs
                        If r Is Nothing OrElse r.IsImage Then Continue For
                        Dim runStart As Integer = charAccumulator
                        Dim runLen As Integer = If(r.Text, "").Length
                        Dim runEnd As Integer = charAccumulator + runLen

                        If runEnd > selStartChar AndAlso runStart < selEndChar Then
                            Dim startInRun As Integer = Math.Max(0, selStartChar - runStart)
                            Dim endInRun As Integer = Math.Min(runLen, selEndChar - runStart)
                            If endInRun > startInRun Then
                                Dim cloned As DocRun = r.Clone()
                                cloned.Text = r.Text.Substring(startInRun, endInRun - startInRun)
                                _appuntiFormattati.Add(cloned)
                            End If
                        End If
                        charAccumulator += runLen
                    Next

                    If _appuntiFormattati.Count > 0 Then
                        Dim paraCopia As New DocParagraph()
                        paraCopia.Alignment = paraSorgente.Alignment
                        paraCopia.IndentLevel = paraSorgente.IndentLevel
                        If paraCopia.Runs Is Nothing Then paraCopia.Runs = New List(Of DocRun)()
                        paraCopia.Runs.Clear()
                        For Each rc As DocRun In _appuntiFormattati
                            paraCopia.Runs.Add(rc.Clone())
                        Next
                        elementiDaCopiare.Add(paraCopia)
                    End If
                Else
                    Dim selezionati As List(Of DocElement) = GetSelectedElements()
                    If selezionati IsNot Nothing AndAlso selezionati.Count > 0 Then elementiDaCopiare.AddRange(selezionati)
                End If
            End SyncLock

            If elementiDaCopiare.Count = 0 Then
                MostraMessaggioStato("Nessun testo da copiare.")
                Return
            End If

            MettiElementiNegliAppunti(elementiDaCopiare)
            MostraMessaggioStato("Testo formattato copiato.")
        Catch ex As Exception
            logger.ScriviLog("CopiaFormattato: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Delegate Function GeneratoreFormatoAppunti(elementi As List(Of DocElement)) As String

    Private Delegate Sub AzioneAppunti(data As DataObject)

    Private Function FornitoriFormatoAppunti() As List(Of KeyValuePair(Of String, GeneratoreFormatoAppunti))
        Dim lista As New List(Of KeyValuePair(Of String, GeneratoreFormatoAppunti))()
        Try
            lista.Add(New KeyValuePair(Of String, GeneratoreFormatoAppunti)(DataFormats.Rtf, AddressOf GeneraRtfDaElementi))
            lista.Add(New KeyValuePair(Of String, GeneratoreFormatoAppunti)(DataFormats.Html, AddressOf GeneraHtmlDaElementi))
            lista.Add(New KeyValuePair(Of String, GeneratoreFormatoAppunti)("WGWordFormat", AddressOf GeneraFormatoInternoDaElementi))
        Catch ex As Exception
            logger.ScriviLog("FornitoriFormatoAppunti: " & ex.Message)
        End Try
        Return lista
    End Function

    Friend Function GeneraTestoDaElementi(elementi As List(Of DocElement)) As String
        Try
            If elementi Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            For i As Integer = 0 To elementi.Count - 1
                Dim el As DocElement = elementi(i)
                If TypeOf el Is DocParagraph Then
                    sb.Append(DirectCast(el, DocParagraph).GetText())
                ElseIf TypeOf el Is DocTable Then
                    Dim tbl As DocTable = DirectCast(el, DocTable)
                    If tbl.Rows IsNot Nothing Then
                        For Each riga As DocTableRow In tbl.Rows
                            If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                            Dim primaCella As Boolean = True
                            For Each cella As DocTableCell In riga.Cells
                                If Not primaCella Then sb.Append(ControlChars.Tab)
                                If cella IsNot Nothing AndAlso cella.Paragraphs IsNot Nothing Then
                                    For Each p As DocParagraph In cella.Paragraphs
                                        If p IsNot Nothing Then sb.Append(p.GetText())
                                    Next
                                End If
                                primaCella = False
                            Next
                            sb.AppendLine()
                        Next
                    End If
                End If
                If i < elementi.Count - 1 Then sb.AppendLine()
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("GeneraTestoDaElementi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Friend Function GeneraFormatoInternoDaElementi(elementi As List(Of DocElement)) As String
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Dim serializer As New System.Xml.Serialization.XmlSerializer(GetType(List(Of DocElement)))
            Dim sw As New IO.StringWriter()
            serializer.Serialize(sw, elementi)
            Return sw.ToString()
        Catch ex As Exception
            logger.ScriviLog("GeneraFormatoInternoDaElementi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Function TestoHtmlSicuro(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim sb As New StringBuilder()
            For Each c As Char In testo
                Select Case c
                    Case "&"c : sb.Append("&amp;")
                    Case "<"c : sb.Append("&lt;")
                    Case ">"c : sb.Append("&gt;")
                    Case """"c : sb.Append("&quot;")
                    Case ControlChars.Lf : sb.Append("<br>")
                    Case ControlChars.Cr
                    Case Else
                        If AscW(c) > 127 Then
                            sb.Append("&#").Append(AscW(c).ToString()).Append(";")
                        Else
                            sb.Append(c)
                        End If
                End Select
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("TestoHtmlSicuro: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RunInHtml(r As DocRun) As String
        Try
            If r Is Nothing OrElse r.IsImage Then Return ""
            Dim testoRun As String = If(r.Text, "")
            If testoRun.Length = 0 Then Return ""

            Dim stile As New StringBuilder()
            stile.Append("font-family:").Append(If(String.IsNullOrEmpty(r.FontFamily), "Arial", r.FontFamily)).Append(";")
            stile.Append("font-size:").Append(CInt(Math.Max(1, r.FontSize)).ToString()).Append("pt;")
            stile.Append("color:#").Append(ColoreEsadecimaleDaNome(r.ColorHex)).Append(";")
            If r.Bold Then stile.Append("font-weight:bold;")
            If r.Italic Then stile.Append("font-style:italic;")
            Dim decorazioni As New List(Of String)()
            If r.Underline OrElse r.IsHyperlink OrElse Not String.IsNullOrEmpty(r.LinkUrl) Then decorazioni.Add("underline")
            If r.Strikethrough Then decorazioni.Add("line-through")
            If decorazioni.Count > 0 Then stile.Append("text-decoration:").Append(String.Join(" ", decorazioni.ToArray())).Append(";")
            If Not String.IsNullOrEmpty(r.HighlightColorHex) AndAlso Not r.HighlightColorHex.Equals("Nessuno", StringComparison.OrdinalIgnoreCase) Then
                stile.Append("background-color:#").Append(ColoreEsadecimaleDaNome(r.HighlightColorHex)).Append(";")
            End If

            Dim contenuto As String = TestoHtmlSicuro(testoRun)
            If r.IsSuperscript Then contenuto = "<sup>" & contenuto & "</sup>"
            If r.IsSubscript Then contenuto = "<sub>" & contenuto & "</sub>"

            Return "<span style=""" & stile.ToString() & """>" & contenuto & "</span>"
        Catch ex As Exception
            logger.ScriviLog("RunInHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ParagrafoInHtml(para As DocParagraph) As String
        Try
            If para Is Nothing Then Return "<p></p>"
            Dim allineamento As String = If(para.Alignment, "Left")
            Dim stile As New StringBuilder()
            If allineamento.Equals("Center", StringComparison.OrdinalIgnoreCase) Then
                stile.Append("text-align:center;")
            ElseIf allineamento.Equals("Right", StringComparison.OrdinalIgnoreCase) Then
                stile.Append("text-align:right;")
            ElseIf allineamento.Equals("Justify", StringComparison.OrdinalIgnoreCase) Then
                stile.Append("text-align:justify;")
            End If
            If para.IndentLevel > 0 Then stile.Append("margin-left:").Append((para.IndentLevel * 40).ToString()).Append("px;")

            Dim sb As New StringBuilder()
            sb.Append("<p style=""").Append(stile.ToString()).Append(""">")
            If para.Runs IsNot Nothing Then
                For Each r As DocRun In para.Runs
                    sb.Append(RunInHtml(r))
                Next
            End If
            sb.Append("</p>")
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("ParagrafoInHtml: " & ex.Message)
            Return "<p></p>"
        End Try
    End Function

    Friend Function GeneraHtmlDaElementi(elementi As List(Of DocElement)) As String
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return ""
            Dim corpo As New StringBuilder()

            For Each el As DocElement In elementi
                If TypeOf el Is DocParagraph Then
                    corpo.Append(ParagrafoInHtml(DirectCast(el, DocParagraph)))
                ElseIf TypeOf el Is DocTable Then
                    Dim tbl As DocTable = DirectCast(el, DocTable)
                    corpo.Append("<table border=""1"" style=""border-collapse:collapse;"">")
                    If tbl.Rows IsNot Nothing Then
                        For Each riga As DocTableRow In tbl.Rows
                            If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                            corpo.Append("<tr>")
                            For Each cella As DocTableCell In riga.Cells
                                Dim etichetta As String = If(riga.IsHeader, "th", "td")
                                corpo.Append("<").Append(etichetta).Append(" style=""border:1px solid #000;padding:4px;"">")
                                If cella IsNot Nothing AndAlso cella.Paragraphs IsNot Nothing Then
                                    For Each p As DocParagraph In cella.Paragraphs
                                        If p Is Nothing OrElse p.Runs Is Nothing Then Continue For
                                        For Each r As DocRun In p.Runs
                                            corpo.Append(RunInHtml(r))
                                        Next
                                    Next
                                End If
                                corpo.Append("</").Append(etichetta).Append(">")
                            Next
                            corpo.Append("</tr>")
                        Next
                    End If
                    corpo.Append("</table>")
                End If
            Next

            Dim frammento As String = corpo.ToString()
            Dim prefisso As String = "<html><body><!--StartFragment-->"
            Dim suffisso As String = "<!--EndFragment--></body></html>"
            Dim intestazione As String = "Version:0.9" & vbCrLf & "StartHTML:{0}" & vbCrLf & "EndHTML:{1}" & vbCrLf & "StartFragment:{2}" & vbCrLf & "EndFragment:{3}" & vbCrLf

            Dim lunghezzaIntestazione As Integer = String.Format(intestazione, "0000000000", "0000000000", "0000000000", "0000000000").Length
            Dim inizioHtml As Integer = lunghezzaIntestazione
            Dim inizioFrammento As Integer = inizioHtml + prefisso.Length
            Dim fineFrammento As Integer = inizioFrammento + frammento.Length
            Dim fineHtml As Integer = fineFrammento + suffisso.Length

            Return String.Format(intestazione, inizioHtml.ToString("D10"), fineHtml.ToString("D10"), inizioFrammento.ToString("D10"), fineFrammento.ToString("D10")) & prefisso & frammento & suffisso
        Catch ex As Exception
            logger.ScriviLog("GeneraHtmlDaElementi: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Sub ScriviAppuntiSuThreadInterfaccia(data As DataObject)
        Try
            Clipboard.SetDataObject(data, True)
        Catch exPersistenza As Exception
            logger.ScriviLog("ScriviAppuntiSuThreadInterfaccia: " & exPersistenza.Message)
            Try
                Clipboard.SetDataObject(data, False)
            Catch exRipiego As Exception
                logger.ScriviLog("ScriviAppuntiSuThreadInterfaccia ripiego: " & exRipiego.Message)
            End Try
        End Try
    End Sub

    Friend Sub MettiElementiNegliAppunti(elementi As List(Of DocElement))
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim data As New DataObject()
            Dim qualcosaDaCopiare As Boolean = False

            Dim testoSemplice As String = GeneraTestoDaElementi(elementi)
            If Not String.IsNullOrEmpty(testoSemplice) Then
                data.SetText(testoSemplice)
                qualcosaDaCopiare = True
            End If

            For Each fornitore As KeyValuePair(Of String, GeneratoreFormatoAppunti) In FornitoriFormatoAppunti()
                Try
                    Dim contenuto As String = fornitore.Value.Invoke(elementi)
                    If Not String.IsNullOrEmpty(contenuto) Then
                        data.SetData(fornitore.Key, contenuto)
                        qualcosaDaCopiare = True
                    End If
                Catch exFormato As Exception
                    logger.ScriviLog("MettiElementiNegliAppunti formato " & fornitore.Key & ": " & exFormato.Message)
                End Try
            Next

            If Not qualcosaDaCopiare Then Return

            If Me.IsHandleCreated AndAlso Me.InvokeRequired Then
                Me.Invoke(New AzioneAppunti(AddressOf ScriviAppuntiSuThreadInterfaccia), New Object() {data})
            Else
                ScriviAppuntiSuThreadInterfaccia(data)
            End If
        Catch ex As Exception
            logger.ScriviLog("MettiElementiNegliAppunti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CopiaTestoSelezionatoNegliAppuntiDiSistema()
        Try
            If _appuntiFormattati Is Nothing OrElse _appuntiFormattati.Count = 0 Then Return

            Dim paraTemporaneo As New DocParagraph()
            If paraTemporaneo.Runs Is Nothing Then paraTemporaneo.Runs = New List(Of DocRun)()
            paraTemporaneo.Runs.Clear()
            For Each r In _appuntiFormattati
                If r IsNot Nothing Then paraTemporaneo.Runs.Add(r.Clone())
            Next

            Dim elencoTemporaneo As New List(Of DocElement)()
            elencoTemporaneo.Add(paraTemporaneo)
            MettiElementiNegliAppunti(elencoTemporaneo)
        Catch ex As Exception
            logger.ScriviLog("CopiaTestoSelezionatoNegliAppuntiDiSistema: " & ex.Message)
        End Try
    End Sub

    Public Sub TagliaFormattato()
        Try
            If SolaLettura Then Return
            If Not _selectionActive Then Return
            CopiaFormattato()
            DeleteSelection()

            MostraMessaggioStato("Testo formattato tagliato.")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub IncollaFormattato()
        Try

            If SolaLettura Then Return

            If _appuntiFormattati IsNot Nothing AndAlso _appuntiFormattati.Count > 0 Then
                If _selectionActive Then DeleteSelection()
                Dim cloni As New List(Of DocRun)()
                For Each r In _appuntiFormattati

                    cloni.Add(r.Clone())
                Next
                InsertRuns(cloni)
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)

                UpdateCaretAndNotify(True)

                MostraMessaggioStato("Testo formattato incollato.")
            Else

                PasteText()
                MostraMessaggioStato("Testo incollato.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraElencoLink()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _menuElencoLink IsNot Nothing Then
                _menuElencoLink.abbandona()
                _menuElencoLink = Nothing
            End If


            _menuElencoLink = New WGMenuLista()
            _menuElencoLink.impostaTitolo("Elenco Link")

            Dim linkTrovati As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p = DirectCast(el, DocParagraph)
                            For Each r In p.Runs

                                If Not String.IsNullOrEmpty(r.LinkUrl) Then
                                    _menuElencoLink.aggiungiElemento(r.Text.Trim(), r.LinkUrl)

                                    linkTrovati += 1
                                End If
                            Next
                        End If

                    Next
                End If
            End SyncLock

            If linkTrovati = 0 Then
                MostraMessaggioStato("Nessun link presente nel documento.")
                Return
            End If

            AddHandler _menuElencoLink.ElementoSelezionato, AddressOf AzioneApriLinkElenco
            AddHandler _menuElencoLink.escPremuto, AddressOf ChiudiMenuElencoLink
            _menuElencoLink.avvia()
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AzioneApriLinkElenco()
        Try
            If _menuElencoLink IsNot Nothing Then
                Dim ids As List(Of String) = _menuElencoLink.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then

                    Dim url As String = ids(0)
                    If url.StartsWith("#") Then
                        VaiABookmark(url.Substring(1))
                    Else

                        WGImpostazioniComuni.ApriLinkConBrowserPreferito(url)
                    End If
                End If
            End If
            ChiudiMenuElencoLink()
        Catch ex As Exception

            logger.ScriviLog("Errore nell'apertura del link: " & ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio("Errore nell'apertura del link: " & ex.Message)
            msgErrore.ShowDialog()
        End Try
    End Sub

    Private Sub ChiudiMenuElencoLink()
        Try
            If _menuElencoLink IsNot Nothing Then

                _menuElencoLink.abbandona()
                _menuElencoLink = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ConvertiSelezioneMaiuscolo()
        If SolaLettura Then Return

        ApplicaTrasformazioneTesto(Function(txt) txt.ToUpper())
    End Sub

    Public Sub ConvertiSelezioneMinuscolo()
        If SolaLettura Then Return
        ApplicaTrasformazioneTesto(Function(txt) txt.ToLower())
    End Sub

    Public Sub ConvertiSelezioneInizialiMaiuscole()
        If SolaLettura Then Return
        ApplicaTrasformazioneTesto(Function(txt) System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(txt.ToLower()))
    End Sub

    Private Sub ApplicaTrasformazioneTesto(trasformazione As Func(Of String, String))
        Try

            If SolaLettura Then Return
            If Not _selectionActive Then Return

            Dim selStartIdx, selEndIdx, selStartSub, selEndSub, selStartChar, selEndChar As Integer
            CalculateSelectionRange(selStartIdx, selStartSub, selStartChar, selEndIdx, selEndSub, selEndChar)

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso selStartIdx >= 0 AndAlso selStartIdx < _elements.Count AndAlso selStartIdx = selEndIdx AndAlso TypeOf _elements(selStartIdx) Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(_elements(selStartIdx), DocParagraph)
                    Dim charAccumulator As Integer = 0
                    For Each r In p.Runs
                        If r.IsImage Then Continue For

                        Dim runStart = charAccumulator
                        Dim runLen = r.Text.Length
                        Dim runEnd = charAccumulator + runLen

                        If runEnd > selStartChar AndAlso runStart < selEndChar Then
                            Dim startInRun = Math.Max(0, selStartChar - runStart)
                            Dim endInRun = Math.Min(runLen, selEndChar - runStart)

                            Dim p1 As String = r.Text.Substring(0, startInRun)
                            Dim p2 As String = r.Text.Substring(startInRun, endInRun - startInRun)
                            Dim p3 As String = r.Text.Substring(endInRun)

                            r.Text = p1 & trasformazione(p2) & p3
                        End If
                        charAccumulator += runLen
                    Next

                    p.CachedHeight = -1
                End If
            End SyncLock

            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            MostraMessaggioStato("Testo convertito")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RimuoviSpaziVuotiExtra()
        Try
            If SolaLettura Then Return
            Dim trovato As Boolean = False

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = 0 To _elements.Count - 1
                        If TypeOf _elements(i) Is DocParagraph Then

                            Dim p = DirectCast(_elements(i), DocParagraph)
                            For Each r In p.Runs
                                If Not r.IsImage AndAlso Not String.IsNullOrEmpty(r.Text) Then

                                    Dim nuovoTesto = Regex.Replace(r.Text, "\s+", " ")
                                    If nuovoTesto <> r.Text Then

                                        r.Text = nuovoTesto
                                        p.CachedHeight = -1
                                        trovato = True

                                    End If
                                End If
                            Next

                        End If
                    Next
                End If
            End SyncLock
            If trovato Then

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Spazi extra rimossi")
            Else
                MostraMessaggioStato("Nessuno spazio extra trovato nel documento.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Public Sub EsportaTestoPuro(percorso As String)
        Try
            Dim sb As New StringBuilder()
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                        ElseIf TypeOf el Is DocTable Then

                            Dim tbl As DocTable = DirectCast(el, DocTable)
                            For Each r In tbl.Rows
                                Dim rowText As New List(Of String)

                                For Each c In r.Cells
                                    Dim cellText As New StringBuilder()

                                    For Each p In c.Paragraphs
                                        cellText.Append(p.GetText() & " ")
                                    Next

                                    rowText.Add(cellText.ToString().Trim())
                                Next
                                sb.AppendLine(String.Join(vbTab, rowText))

                            Next
                        End If
                    Next
                End If
            End SyncLock

            File.WriteAllText(percorso, sb.ToString())
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

End Class