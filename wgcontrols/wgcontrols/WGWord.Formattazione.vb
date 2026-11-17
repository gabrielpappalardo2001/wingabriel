Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

' Gestione della formattazione del testo e dei paragrafi, inclusi font, colori, elenchi puntati e numerati, e rientri.
Partial Public Class WGWord
    Public Sub LeggiInfoFormattazione()
        Try
            Dim info As New List(Of String)

            Dim fontName As String = "Arial"
            Dim fontSize As Single = 11.0F
            Dim isBold As Boolean = False
            Dim isItalic As Boolean = False
            Dim isUnderline As Boolean = False
            Dim isStrike As Boolean = False

            Dim align As String = "Allineato a sinistra"

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)


                    If TypeOf el Is DocTable Then
                        Dim tbl As DocTable = DirectCast(el, DocTable)
                        If tbl.Rows IsNot Nothing AndAlso _tblRow >= 0 AndAlso _tblRow < tbl.Rows.Count AndAlso tbl.Rows(_tblRow) IsNot Nothing AndAlso tbl.Rows(_tblRow).Cells IsNot Nothing AndAlso _tblCol >= 0 AndAlso _tblCol < tbl.Rows(_tblRow).Cells.Count Then

                            Dim cell As DocTableCell = tbl.Rows(_tblRow).Cells(_tblCol)
                            If cell IsNot Nothing AndAlso cell.Paragraphs IsNot Nothing AndAlso _tblPara >= 0 AndAlso _tblPara < cell.Paragraphs.Count AndAlso cell.Paragraphs(_tblPara) IsNot Nothing Then
                                el = cell.Paragraphs(_tblPara)

                            End If
                        End If
                    End If

                    If TypeOf el Is DocParagraph Then

                        Dim p As DocParagraph = DirectCast(el, DocParagraph)

                        Select Case If(p.Alignment, "").ToLower()

                            Case "center"
                                align = "Allineato al centro"
                            Case "right"
                                align = "Allineato a destra"
                            Case "justify"
                                align = "Giustificato"
                            Case Else

                                align = "Allineato a sinistra"
                        End Select

                        Dim charCount As Integer = 0

                        Dim targetCharIndex As Integer = _caretCharIndex

                        If _selectionActive Then
                            targetCharIndex = Math.Min(_selAnchorCharIndex, _caretCharIndex)

                        End If

                        If p.Runs IsNot Nothing Then
                        For Each r In p.Runs
                            If r Is Nothing OrElse r.IsImage Then Continue For
                            Dim runLen = Math.Max(1, If(r.Text, "").Length)


                            If targetCharIndex >= charCount AndAlso targetCharIndex < charCount + runLen Then
                                fontName = r.FontFamily
                                fontSize = r.FontSize

                                isBold = r.Bold
                                isItalic = r.Italic
                                isUnderline = r.Underline

                                isStrike = r.Strikethrough
                                Exit For
                            End If

                            charCount += runLen
                        Next
                        End If
                    End If
                End If
            End SyncLock


            info.Add("Tipo di carattere " & fontName)
            info.Add("Dimensione " & fontSize.ToString() & " punti")
            If isBold Then info.Add("Grassetto")
            If isItalic Then info.Add("Corsivo")
            If isUnderline Then info.Add("Sottolineato")
            If isStrike Then info.Add("Barrato")
            info.Add(align)


            Dim testoInfo As String = String.Join(", ", info)
            lettura.LeggiTesto(testoInfo)
            MostraMessaggioStato(testoInfo)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub GestisciChar(c As Char)
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then
                Return
            End If
            If Not Char.IsControl(c) Then
                SaveUndoState()
                If _selectionActive Then
                    DeleteSelection()
                End If

                Dim charInserito As Char = c

                If _shiftPremutoDaHook AndAlso Char.IsLetter(c) Then
                    charInserito = Char.ToUpper(c)
                End If

                If GestisciMaiuscolaAutomatica(charInserito) Then
                    _isModified = True
                    charInserito = Char.ToUpper(charInserito)
                Else
                    InsertChar(charInserito)
                End If

                Dim echoSetting As String = EchoDigitazione
                If charInserito = "@"c Then
                    lettura.LeggiTesto("chiocciola")
                ElseIf charInserito = "#"c Then
                    lettura.LeggiTesto("cancelletto")
                ElseIf Char.IsLetter(charInserito) AndAlso Char.IsUpper(charInserito) Then
                    lettura.LeggiTesto(charInserito.ToString() & " maiuscola")
                Else
                    If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.GestisciEcoDigitazione(charInserito, echoSetting)
                End If

                ScrollToCaret()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(True)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Dim msgErrore As New WGMessaggi()
            msgErrore.Messaggio(ex.Message)
            msgErrore.ShowDialog()
        End Try
    End Sub

    Private Function GestisciMaiuscolaAutomatica(ByVal c As Char) As Boolean

        Try
            If Not MaiuscolaAutomaticaAttiva Then
                Return False
            End If

            Dim p As DocParagraph = GetCurrentParagraph()
            If p Is Nothing Then
                Return False
            End If

            Dim testoPrecedente As String = p.GetText()

            If _caretCharIndex >= 0 AndAlso _caretCharIndex <= testoPrecedente.Length Then
                testoPrecedente = testoPrecedente.Substring(0, _caretCharIndex)
            End If
            Dim deveEssereMaiuscola As Boolean = False

            If String.IsNullOrWhiteSpace(testoPrecedente) Then
                deveEssereMaiuscola = True

            Else
                Dim pulito = testoPrecedente.TrimEnd()
                If pulito.EndsWith(".") OrElse pulito.EndsWith("!") OrElse pulito.EndsWith("?") Then
                    deveEssereMaiuscola = True
                End If
            End If

            If deveEssereMaiuscola AndAlso Char.IsLower(c) Then
                InsertChar(Char.ToUpper(c))
                Return True
            End If
            Return False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

            Return False
        End Try
    End Function

    Public Sub ConfiguraSoloCaratteri()
        Try
            If config IsNot Nothing Then config.AggiungiValore("echo_digitazione", "1")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraSoloParole()
        Try
            If config IsNot Nothing Then config.AggiungiValore("echo_digitazione", "2")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraCaratteriEParole()
        Try
            If config IsNot Nothing Then config.AggiungiValore("echo_digitazione", "3")

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ConfiguraNessuno()
        Try
            If config IsNot Nothing Then config.AggiungiValore("echo_digitazione", "0")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try

    End Sub

    Public Sub MostraMenuFont()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return
            Dim families As FontFamily() = FontFamily.Families
            If families Is Nothing OrElse families.Length = 0 Then
                MostraMessaggioStato("Nessun font installato.")
                Return
            End If

            If _menuFont IsNot Nothing Then
                _menuFont.abbandona()

                _menuFont = Nothing
            End If
            _menuFont = New WGMenuLista()
            _menuFont.impostaTitolo("Font")

            For Each f As FontFamily In families
                _menuFont.aggiungiElemento(f.Name, f.Name)
            Next

            AddHandler _menuFont.ElementoSelezionato, AddressOf MenuFont_Selezionato
            _menuFont.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MenuFont_Selezionato()
        Try
            If _menuFont IsNot Nothing Then

                Dim ids As List(Of String) = _menuFont.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    Dim id As String = ids(0)
                    ImpostaFont(id)
                    MostraMessaggioStato("Font " & id)
                End If
                _menuFont.abbandona()
                _menuFont = Nothing

            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuDimensioni()
        Try
            If Me.IsDisposed OrElse Me.Disposing OrElse SolaLettura Then Return
            If _menuDimensioni IsNot Nothing Then

                _menuDimensioni.abbandona()
                _menuDimensioni = Nothing
            End If
            _menuDimensioni = New WGMenuLista()
            _menuDimensioni.impostaTitolo("Dimensione")
            Dim dimensioni() As Single = {8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72}


            For Each d As Single In dimensioni
                _menuDimensioni.aggiungiElemento(d.ToString() & " punti", d.ToString())
            Next
            AddHandler _menuDimensioni.ElementoSelezionato, AddressOf MenuDimensioni_Selezionato
            _menuDimensioni.avvia()
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MenuDimensioni_Selezionato()
        Try
            If _menuDimensioni IsNot Nothing Then
                Dim ids As List(Of String) = _menuDimensioni.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    Dim id As String = ids(0)
                    Dim size As Single = CSng(id)
                    ImpostaDimensioneFont(size)
                    MostraMessaggioStato("Dimensione " & id)
                End If
                _menuDimensioni.abbandona()
                _menuDimensioni = Nothing

            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InserisciInterruzionePagina()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim newPara As New DocParagraph()
                newPara.IsPageBreak = True
                Dim newEmptyPara As New DocParagraph()
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements.Insert(_caretIndex + 1, newPara)
                        _elements.Insert(_caretIndex + 2, newEmptyPara)
                        _caretIndex += 2

                        _caretCharIndex = 0
                    End If
                End SyncLock
                _isModified = True
                TriggerLayout()

                ScrollToCaret()
                UpdateCaretAndNotify(True)

                MostraMessaggioStato("Interruzione pagina")
            End If
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Friend Function CalcolaProssimoNumeroElenco() As Integer
        Try
            Dim numero As Integer = 1
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex > 0 Then
                    Dim i As Integer = _caretIndex - 1
                    While i >= 0 AndAlso TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsListItem AndAlso Not String.IsNullOrEmpty(DirectCast(_elements(i), DocParagraph).ListNumValue)
                        numero += 1
                        i -= 1
                    End While
                End If
            End SyncLock
            Return numero
        Catch ex As Exception
            logger.ScriviLog("CalcolaProssimoNumeroElenco: " & ex.Message)
            Return 1
        End Try
    End Function

    Public Sub InserisciElencoPuntato()
        Try

            If SolaLettura Then Return
            Dim para = GetCurrentParagraph()
            If para IsNot Nothing Then

                If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 AndAlso para.Runs(0) IsNot Nothing Then
                    Dim primoTesto As String = If(para.Runs(0).Text, "")
                    Dim pulito As String = Regex.Replace(primoTesto, "^(\d+\.\s+|•\s+)", "")
                    para.Runs(0).Text = pulito
                    If _caretCharIndex > primoTesto.Length - pulito.Length Then
                        _caretCharIndex -= (primoTesto.Length - pulito.Length)
                    Else
                        _caretCharIndex = 0
                    End If
                End If

                para.IsListItem = True
                para.NumberingId = 1
                para.ListNumValue = ""
                _isModified = True
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                        _elements(_caretIndex).CachedHeight = -1

                    End If
                End SyncLock
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Elenco puntato")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InserisciElencoNumerato()
        Try

            If SolaLettura Then Return
            Dim para = GetCurrentParagraph()
            If para IsNot Nothing Then

                If para.Runs IsNot Nothing AndAlso para.Runs.Count > 0 AndAlso para.Runs(0) IsNot Nothing Then
                    Dim primoTesto As String = If(para.Runs(0).Text, "")
                    Dim pulito As String = Regex.Replace(primoTesto, "^(\d+\.\s+|•\s+)", "")
                    para.Runs(0).Text = pulito
                    If _caretCharIndex > primoTesto.Length - pulito.Length Then
                        _caretCharIndex -= (primoTesto.Length - pulito.Length)
                    Else
                        _caretCharIndex = 0
                    End If
                End If

                para.IsListItem = True
                para.NumberingId = 2
                para.ListNumValue = CalcolaProssimoNumeroElenco().ToString()
                _isModified = True
                SyncLock _elementsLock
                    If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then

                        _elements(_caretIndex).CachedHeight = -1
                    End If
                End SyncLock
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)

                UpdateCaretAndNotify(False)
                MostraMessaggioStato("Elenco numerato")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try

    End Sub

    Public Sub AumentaRientro()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                para.IndentLevel += 1

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Rientro aumentato")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RiduciRientro()
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing AndAlso para.IndentLevel > 0 Then
                para.IndentLevel -= 1
                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Rientro ridotto")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImpostaFont(fontName As String)
        Try

            If SolaLettura Then Return
            FormattaSelezione(Function(r)
                                  r.FontFamily = fontName
                                  Return r

                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImpostaDimensioneFont(size As Single)
        Try
            If SolaLettura Then Return

            FormattaSelezione(Function(r)
                                  r.FontSize = size
                                  Return r

                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImpostaColoreFont(colore As String)
        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)

                                  r.ColorHex = colore
                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ImpostaEvidenziatore(colore As String)
        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)

                                  r.HighlightColorHex = colore
                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Public Sub ImpostaAllineamento(allineamento As String)
        Try
            If SolaLettura Then Return
            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                para.Alignment = allineamento

                _isModified = True
                TriggerLayout()
                InvalidateRect(Me.Handle, IntPtr.Zero, False)
                MostraMessaggioStato("Allineamento " & allineamento)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ApplicaGrassetto()
        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)

                                  r.Bold = Not r.Bold
                                  Return r
                              End Function)

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ApplicaSottolineato()
        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)

                                  r.Underline = Not r.Underline
                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Public Sub ApplicaBarrato()
        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)
                                  r.Strikethrough = Not r.Strikethrough

                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ApplicaCorsivo()

        Try
            If SolaLettura Then Return
            FormattaSelezione(Function(r)
                                  r.Italic = Not r.Italic

                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub RimuoviFormattazione()
        Try

            If SolaLettura Then Return
            Dim fontDefault As String = FontPredefinito
            Dim dimDefault As Single = DimensionePredefinita
            FormattaSelezione(Function(r)
                                  r.Bold = False
                                  r.Italic = False

                                  r.Underline = False
                                  r.Strikethrough = False
                                  r.ColorHex = "Nero"

                                  r.HighlightColorHex = "Nessuno"
                                  r.FontFamily = fontDefault

                                  r.FontSize = dimDefault
                                  Return r
                              End Function)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

End Class