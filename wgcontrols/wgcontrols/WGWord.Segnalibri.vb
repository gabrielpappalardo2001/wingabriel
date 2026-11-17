Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

' Gestione dei segnalibri, inserimento di data e ora, e divisione del testo in frasi per la navigazione.
Partial Public Class WGWord
    Public Sub AggiungiSegnalibro(nome As String)
        Try
            If SolaLettura OrElse String.IsNullOrWhiteSpace(nome) Then Return

            Dim idNuovo As String = "BKMK_" & nome
            Dim giaEsistente As Boolean = False
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each elChk In _elements
                        If TypeOf elChk Is DocParagraph AndAlso DirectCast(elChk, DocParagraph).BookmarkId = idNuovo Then
                            giaEsistente = True
                            Exit For
                        End If
                    Next
                End If
            End SyncLock

            If giaEsistente Then
                MostraMessaggioStato("Esiste già un segnalibro con questo nome")
                Return
            End If

            Dim para As DocParagraph = GetCurrentParagraph()
            If para IsNot Nothing Then
                para.BookmarkId = idNuovo
                _isModified = True
                TriggerLayout()
                MostraMessaggioStato("Segnalibro inserito")
            Else
                MostraMessaggioStato("Impossibile inserire un segnalibro in questa posizione")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuSegnalibri()
        Try

            If Me.IsDisposed OrElse Me.Disposing Then Return
            If _menuSegnalibri IsNot Nothing Then
                _menuSegnalibri.abbandona()
                _menuSegnalibri = Nothing
            End If

            _menuSegnalibri = New WGMenuLista()

            _menuSegnalibri.impostaTitolo("Segnalibri")

            Dim trovati As Integer = 0

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements

                        If TypeOf el Is DocParagraph Then
                            Dim p = DirectCast(el, DocParagraph)
                            If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso p.BookmarkId.StartsWith("BKMK_") Then

                                Dim nomeSegnalibro As String = p.BookmarkId.Substring(5)
                                _menuSegnalibri.aggiungiElemento(nomeSegnalibro, p.BookmarkId)
                                trovati += 1

                            End If
                        End If
                    Next
                End If
            End SyncLock

            If trovati = 0 Then
                MostraMessaggioStato("Nessun segnalibro presente.")
                Return
            End If

            _menuSegnalibri.aggiungiOpzione("Vai al segnalibro", AddressOf AzioneVaiASegnalibro)

            If Not SolaLettura Then _menuSegnalibri.aggiungiOpzione("Elimina segnalibro", AddressOf AzioneEliminaSegnalibro)

            AddHandler _menuSegnalibri.ElementoSelezionato, AddressOf AzioneVaiASegnalibro

            _menuSegnalibri.avvia()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AzioneVaiASegnalibro()
        Try

            If _menuSegnalibri IsNot Nothing Then
                Dim ids = _menuSegnalibri.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    VaiABookmark(ids(0))
                End If

                _menuSegnalibri.abbandona()
                _menuSegnalibri = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("AzioneVaiASegnalibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub AzioneEliminaSegnalibro()
        Try
            If _menuSegnalibri IsNot Nothing Then

                Dim ids = _menuSegnalibri.ottieniElementi()
                If ids IsNot Nothing AndAlso ids.Count > 0 Then
                    EliminaSegnalibroInterno(ids(0))
                End If
                _menuSegnalibri.abbandona()

                _menuSegnalibri = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("AzioneEliminaSegnalibro: " & ex.ToString())
        End Try
    End Sub

    Private Sub EliminaSegnalibroInterno(idSegnalibro As String)
        Try
            If SolaLettura Then Return
            Dim trovato As Boolean = False

            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then

                            Dim p = DirectCast(el, DocParagraph)
                            If p.BookmarkId = idSegnalibro Then
                                p.BookmarkId = ""

                                trovato = True
                            End If
                        End If
                    Next

                End If
            End SyncLock

            If trovato Then
                _isModified = True
                TriggerLayout()
                MostraMessaggioStato("Segnalibro eliminato.")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub InserisciDataOraOdierna()
        Try
            If SolaLettura Then Return

            Dim dataOra As String = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            SaveUndoState()
            If _selectionActive Then DeleteSelection()
            For Each c In dataOra
                InsertChar(c)
            Next
            _isModified = True

            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            UpdateCaretAndNotify(True)
            MostraMessaggioStato("Data e ora inserite")
        Catch ex As Exception

            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub EliminaTuttaFormattazioneDocumento()
        Try
            If SolaLettura Then Return
            Dim fontDefault As String = FontPredefinito
            Dim dimDefault As Single = DimensionePredefinita
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p = DirectCast(el, DocParagraph)
                            For Each r In p.Runs

                                r.Bold = False
                                r.Italic = False
                                r.Underline = False

                                r.Strikethrough = False
                                r.ColorHex = "Nero"
                                r.HighlightColorHex = "Nessuno"
                                r.FontFamily = fontDefault
                                r.FontSize = dimDefault
                            Next

                        ElseIf TypeOf el Is DocTable Then
                            Dim t = DirectCast(el, DocTable)
                            For Each row In t.Rows

                                For Each cell In row.Cells
                                    For Each p In cell.Paragraphs

                                        For Each r In p.Runs
                                            r.Bold = False
                                            r.Italic = False
                                            r.Underline = False
                                            r.Strikethrough = False

                                            r.ColorHex = "Nero"
                                            r.HighlightColorHex = "Nessuno"

                                            r.FontFamily = fontDefault
                                            r.FontSize = dimDefault

                                        Next
                                    Next
                                Next

                            Next
                        End If
                    Next
                End If
            End SyncLock

            _isModified = True
            TriggerLayout()
            InvalidateRect(Me.Handle, IntPtr.Zero, False)
            MostraMessaggioStato("Formattazione globale eliminata")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)

        End Try
    End Sub

    Public Sub SelezionaParagrafoCorrente()
        Try
            If _elements Is Nothing OrElse _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then Return
            Dim el = _elements(_caretIndex)
            If TypeOf el Is DocParagraph Then
                _selAnchorIndex = _caretIndex

                _selAnchorSubIndex = 0
                _selAnchorCharIndex = 0
                _caretCharIndex = DirectCast(el, DocParagraph).GetText().Length
                _selectionActive = True
                InvalidateRect(Me.Handle, IntPtr.Zero, False)

                UpdateCaretAndNotify(False)
                lettura.LeggiTesto("Paragrafo selezionato")
                MostraMessaggioStato("Paragrafo selezionato")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub DuplicaRigaCorrente()
        Try
            If SolaLettura Then Return

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    Dim el = _elements(_caretIndex)
                    If TypeOf el Is DocParagraph Then

                        Dim clonedPara = DirectCast(el, DocParagraph).Clone()
                        _elements.Insert(_caretIndex + 1, clonedPara)
                        _isModified = True
                        TriggerLayout()

                        InvalidateRect(Me.Handle, IntPtr.Zero, False)
                        MostraMessaggioStato("Riga duplicata")
                    End If
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub


    Private Function DividiInFrasi(testo As String) As List(Of String)
        Dim frasi As New List(Of String)
        Try
            If String.IsNullOrEmpty(testo) Then Return frasi
            Dim sb As New StringBuilder()
            For i As Integer = 0 To testo.Length - 1
                Dim c As Char = testo(i)
                sb.Append(c)
                If c = "."c OrElse c = "!"c OrElse c = "?"c Then
                    If i = testo.Length - 1 OrElse Char.IsWhiteSpace(testo(i + 1)) Then
                        Dim frase As String = sb.ToString().Trim()
                        If Not String.IsNullOrEmpty(frase) Then frasi.Add(frase)
                        sb.Clear()
                    End If
                End If
            Next
            If sb.Length > 0 Then
                Dim resto As String = sb.ToString().Trim()
                If Not String.IsNullOrEmpty(resto) Then frasi.Add(resto)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
        Return frasi
    End Function

    Private Function TrovaIndiceFraseCorrente(testo As String, posCursore As Integer) As Integer
        Try
            If String.IsNullOrEmpty(testo) Then Return -1
            Dim posizione As Integer = 0
            Dim indiceFrase As Integer = 0
            Dim sb As New StringBuilder()
            For i As Integer = 0 To testo.Length - 1
                Dim c As Char = testo(i)
                sb.Append(c)
                If c = "."c OrElse c = "!"c OrElse c = "?"c Then
                    If i = testo.Length - 1 OrElse Char.IsWhiteSpace(testo(i + 1)) Then
                        Dim lunghezzaConSpazio As Integer = sb.Length
                        Dim j As Integer = i + 1
                        While j < testo.Length AndAlso Char.IsWhiteSpace(testo(j))
                            lunghezzaConSpazio += 1
                            j += 1
                        End While
                        If posCursore >= posizione AndAlso posCursore < posizione + lunghezzaConSpazio Then
                            Return indiceFrase
                        End If
                        posizione += lunghezzaConSpazio
                        indiceFrase += 1
                        sb.Clear()
                    End If
                End If
            Next
            If sb.Length > 0 Then
                If posCursore >= posizione Then Return indiceFrase
            End If
            Return Math.Max(0, indiceFrase - 1)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return -1
        End Try
    End Function

    Private Function TrovaInizioFrase(testo As String, indiceFrase As Integer) As Integer
        Try
            If String.IsNullOrEmpty(testo) OrElse indiceFrase < 0 Then Return 0
            Dim posizione As Integer = 0
            Dim contatore As Integer = 0
            Dim sb As New StringBuilder()
            For i As Integer = 0 To testo.Length - 1
                Dim c As Char = testo(i)
                sb.Append(c)
                If c = "."c OrElse c = "!"c OrElse c = "?"c Then
                    If i = testo.Length - 1 OrElse Char.IsWhiteSpace(testo(i + 1)) Then
                        Dim lunghezzaConSpazio As Integer = sb.Length
                        Dim j As Integer = i + 1
                        While j < testo.Length AndAlso Char.IsWhiteSpace(testo(j))
                            lunghezzaConSpazio += 1
                            j += 1
                        End While
                        contatore += 1
                        If contatore = indiceFrase Then Return posizione + lunghezzaConSpazio
                        posizione += lunghezzaConSpazio
                        sb.Clear()
                    End If
                End If
            Next
            Return posizione
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

End Class