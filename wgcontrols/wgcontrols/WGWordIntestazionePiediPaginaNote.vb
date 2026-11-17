Imports System.Collections.Generic
Imports System.Text

' Gestione di intestazioni, piè di pagina, note a piè di pagina e collegamenti nel testo del documento.
Partial Public Class WGWord
    Private _campiIntestazionePiePagina As Campi
    Private _tipoCampiIntestazionePiePaginaCorrente As String = ""

    Private _campiNote As Campi
    Private _listaBookmarkNoteCorrente As New List(Of String)

    Public Sub MostraMenuIntestazionePiePaginaNote()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi(If(SolaLettura, "Leggi intestazione", "Leggi o modifica intestazione"), AddressOf leggiModificaIntestazioneMenu)
            menu.aggiungi(If(SolaLettura, "Leggi piè di pagina", "Leggi o modifica piè di pagina"), AddressOf leggiModificaPiePaginaMenu)
            If EsistonoNote() Then
                menu.aggiungi(If(SolaLettura, "Leggi note", "Leggi o modifica note"), AddressOf leggiModificaNoteMenu)
            End If
            menu.avvia("Intestazione, piè di pagina e note", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuIntestazionePiePaginaNote: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub leggiModificaIntestazioneMenu(sender As Object, e As EventArgs)
        Try
            LeggiModificaIntestazione()
        Catch ex As Exception
            logger.ScriviLog("leggiModificaIntestazioneMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub leggiModificaPiePaginaMenu(sender As Object, e As EventArgs)
        Try
            LeggiModificaPiePagina()
        Catch ex As Exception
            logger.ScriviLog("leggiModificaPiePaginaMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub leggiModificaNoteMenu(sender As Object, e As EventArgs)
        Try
            LeggiModificaNote()
        Catch ex As Exception
            logger.ScriviLog("leggiModificaNoteMenu: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub LeggiModificaIntestazione()
        Try
            ApriCampiIntestazionePiePagina("INTESTAZIONE")
        Catch ex As Exception
            logger.ScriviLog("LeggiModificaIntestazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub LeggiModificaPiePagina()
        Try
            ApriCampiIntestazionePiePagina("PIEPAGINA")
        Catch ex As Exception
            logger.ScriviLog("LeggiModificaPiePagina: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriCampiIntestazionePiePagina(tipo As String)
        Try
            _tipoCampiIntestazionePiePaginaCorrente = tipo

            Dim titoloCampo As String
            Dim testoIniziale As String
            If tipo = "INTESTAZIONE" Then
                titoloCampo = "Intestazione documento"
                testoIniziale = HeaderText
            Else
                titoloCampo = "Piè di pagina documento"
                testoIniziale = FooterText
            End If

            _campiIntestazionePiePagina = New Campi()
            _campiIntestazionePiePagina.AggiungiMultiriga(titoloCampo, "c.testo", False, SolaLettura, testoIniziale)
            _campiIntestazionePiePagina.SalvaSuEscape = Not SolaLettura
            _campiIntestazionePiePagina.ImpostaMaiuscolaAutomatica("c.testo", MaiuscolaAutomaticaAttiva AndAlso Not SolaLettura)
            _campiIntestazionePiePagina.ImpostaImpostazioniTemporanee(config.LeggiValore("echo_digitazione"), LivelloPunteggiaturaScrittura)
            AddHandler _campiIntestazionePiePagina.Fine, AddressOf FineCampiIntestazionePiePagina
            _campiIntestazionePiePagina.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ApriCampiIntestazionePiePagina: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _testoPendenteIntestazione As String = ""

    Private Sub FineCampiIntestazionePiePagina(sender As Object, e As EventArgs)
        Try
            If _campiIntestazionePiePagina Is Nothing Then Return
            RemoveHandler _campiIntestazionePiePagina.Fine, AddressOf FineCampiIntestazionePiePagina
            If SolaLettura Then Return

            _testoPendenteIntestazione = _campiIntestazionePiePagina.RecuperaValore("c.testo")

            Dim menu As New WGMenu()
            menu.aggiungi("Chiudi e salva", AddressOf ChiudiESalvaIntestazione)
            menu.aggiungi("Aggiungi un'altra riga", AddressOf AggiungiRigaIntestazione)
            menu.avvia(If(_tipoCampiIntestazionePiePaginaCorrente = "INTESTAZIONE", "Intestazione", "Piè di pagina"), True)
        Catch ex As Exception
            logger.ScriviLog("FineCampiIntestazionePiePagina: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiESalvaIntestazione(sender As Object, e As EventArgs)
        Try
            If _tipoCampiIntestazionePiePaginaCorrente = "INTESTAZIONE" Then
                ImpostaIntestazionePiePagina(_testoPendenteIntestazione, FooterText)
            Else
                ImpostaIntestazionePiePagina(HeaderText, _testoPendenteIntestazione)
            End If

            Dim msg As New WGMessaggi()
            msg.Messaggio(If(_tipoCampiIntestazionePiePaginaCorrente = "INTESTAZIONE", "Intestazione salvata.", "Piè di pagina salvato."))
            msg.ShowDialog()

            Try
                If Me.IsHandleCreated Then Me.Focus()
                If lettura IsNot Nothing Then lettura.LeggiTesto("Documento " & _docTitle)
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiRigaCorrente()
            Catch exRitorno As Exception
                logger.ScriviLog("Annuncio ritorno documento: " & exRitorno.Message)
            End Try
        Catch ex As Exception
            logger.ScriviLog("ChiudiESalvaIntestazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiungiRigaIntestazione(sender As Object, e As EventArgs)
        Try
            Dim titoloCampo As String = If(_tipoCampiIntestazionePiePaginaCorrente = "INTESTAZIONE", "Intestazione documento", "Piè di pagina documento")
            Dim testoConNuovaRiga As String = _testoPendenteIntestazione
            If Not String.IsNullOrEmpty(testoConNuovaRiga) Then testoConNuovaRiga &= vbLf

            _campiIntestazionePiePagina = New Campi()
            _campiIntestazionePiePagina.AggiungiMultiriga(titoloCampo, "c.testo", False, False, testoConNuovaRiga)
            _campiIntestazionePiePagina.SalvaSuEscape = True
            _campiIntestazionePiePagina.ImpostaMaiuscolaAutomatica("c.testo", MaiuscolaAutomaticaAttiva AndAlso Not SolaLettura)
            _campiIntestazionePiePagina.ImpostaImpostazioniTemporanee(config.LeggiValore("echo_digitazione"), LivelloPunteggiaturaScrittura)
            AddHandler _campiIntestazionePiePagina.Fine, AddressOf FineCampiIntestazionePiePagina
            _campiIntestazionePiePagina.Avvia()
        Catch ex As Exception
            logger.ScriviLog("AggiungiRigaIntestazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function TrovaBookmarkNotaAllaPosizioneCorrente() As String
        Try
            Dim bookmarkTrovato As String = ""

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count AndAlso TypeOf _elements(_caretIndex) Is DocParagraph Then
                    Dim p As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                    Dim testoParagrafo As String = p.GetText()

                    Dim fineParola As Integer = System.Math.Max(0, System.Math.Min(_caretCharIndex, testoParagrafo.Length))
                    While fineParola < testoParagrafo.Length AndAlso Not Char.IsWhiteSpace(testoParagrafo(fineParola))
                        fineParola += 1
                    End While

                    Dim inizioParola As Integer = System.Math.Max(0, System.Math.Min(_caretCharIndex, testoParagrafo.Length))
                    While inizioParola > 0 AndAlso Not Char.IsWhiteSpace(testoParagrafo(inizioParola - 1))
                        inizioParola -= 1
                    End While

                    Dim charCount As Integer = 0
                    For Each r As DocRun In p.Runs
                        If r Is Nothing OrElse r.IsImage Then Continue For
                        Dim runLen As Integer = If(r.Text, "").Length
                        Dim inizioRun As Integer = charCount
                        Dim fineRun As Integer = charCount + runLen

                        Dim dentroParola As Boolean = (inizioRun < fineParola AndAlso fineRun > inizioParola)

                        If dentroParola AndAlso r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                            Dim soloCifre As Boolean = runLen > 0
                            For Each ch As Char In r.Text
                                If Not Char.IsDigit(ch) Then
                                    soloCifre = False
                                    Exit For
                                End If
                            Next
                            If soloCifre Then
                                bookmarkTrovato = r.LinkUrl
                                Exit For
                            End If
                        End If

                        charCount = fineRun
                    Next
                End If
            End SyncLock

            Return bookmarkTrovato
        Catch ex As Exception
            logger.ScriviLog("TrovaBookmarkNotaAllaPosizioneCorrente: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Function EBookmarkNotaAllaPosizioneCorrente() As String
        Return TrovaBookmarkNotaAllaPosizioneCorrente()
    End Function

    Public Sub LeggiNumeroNotaAllaPosizioneCorrente()
        Try
            Dim bookmarkTrovato As String = TrovaBookmarkNotaAllaPosizioneCorrente()

            If String.IsNullOrEmpty(bookmarkTrovato) Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Non sei su un riferimento a una nota")
                Return
            End If

            Dim descrizione As String = OttieniDescrizioneNumeroNota(bookmarkTrovato)
            If Not String.IsNullOrEmpty(descrizione) AndAlso lettura IsNot Nothing Then
                lettura.LeggiTesto(descrizione)
            End If
        Catch ex As Exception
            logger.ScriviLog("LeggiNumeroNotaAllaPosizioneCorrente: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub EliminaCollegamentoAllaPosizioneCorrente()
        Try
            If SolaLettura Then Return

            Dim runDaSvuotare As DocRun = Nothing
            Dim paraCorrente As DocParagraph = Nothing

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count AndAlso TypeOf _elements(_caretIndex) Is DocParagraph Then
                    paraCorrente = DirectCast(_elements(_caretIndex), DocParagraph)
                    Dim charCount As Integer = 0
                    For Each r As DocRun In paraCorrente.Runs
                        If r IsNot Nothing AndAlso Not r.IsImage Then
                            Dim runLen As Integer = If(r.Text, "").Length
                            If _caretCharIndex >= charCount AndAlso _caretCharIndex < charCount + runLen Then
                                If Not r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso Not r.LinkUrl.StartsWith("NOTA_PIE_") AndAlso Not r.LinkUrl.StartsWith("NOTA_FINE_") Then
                                    runDaSvuotare = r
                                End If
                                Exit For
                            End If
                            charCount += runLen
                        End If
                    Next
                End If
            End SyncLock

            If runDaSvuotare Is Nothing OrElse paraCorrente Is Nothing Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Non sei su un collegamento")
                Return
            End If

            SaveUndoState()

            SyncLock _elementsLock
                Dim lunghezzaRimossa As Integer = If(runDaSvuotare.Text, "").Length
                Dim posizioneRun As Integer = 0
                Dim contatore As Integer = 0
                For Each r As DocRun In paraCorrente.Runs
                    If r IsNot Nothing AndAlso Not r.IsImage Then
                        If contatore < _caretCharIndex Then posizioneRun += If(r.Text, "").Length
                    End If
                    contatore += If(r IsNot Nothing AndAlso Not r.IsImage, If(r.Text, "").Length, 0)
                Next
                runDaSvuotare.Text = ""
                RimuoviRunVuoti(paraCorrente)
                paraCorrente.CachedHeight = -1
                _caretCharIndex = System.Math.Max(0, System.Math.Min(_caretCharIndex, paraCorrente.GetText().Length))
            End SyncLock

            _isModified = True
            TriggerLayout()
            If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
            If lettura IsNot Nothing Then lettura.LeggiTesto("Collegamento eliminato")
        Catch ex As Exception
            logger.ScriviLog("EliminaCollegamentoAllaPosizioneCorrente: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function OffsetRunNota(para As DocParagraph, bookmarkId As String) As Integer
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return -1
            Dim offset As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then Continue For
                If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl = bookmarkId Then Return offset
                offset += If(r.Text, "").Length
            Next
            Return -1
        Catch ex As Exception
            logger.ScriviLog("OffsetRunNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Friend Function TornaAlTestoDellaNota(bookmarkId As String) As Boolean
        Try
            If String.IsNullOrEmpty(bookmarkId) Then Return False
            Dim trovato As Boolean = False

            SyncLock _elementsLock
                If _elements Is Nothing Then Return False

                For i As Integer = 0 To _elements.Count - 1
                    Dim el As DocElement = _elements(i)

                    Dim paraCorpo As DocParagraph = TryCast(el, DocParagraph)
                    If paraCorpo IsNot Nothing Then
                        If paraCorpo.BookmarkId = bookmarkId Then Continue For
                        Dim offParagrafo As Integer = OffsetRunNota(paraCorpo, bookmarkId)
                        If offParagrafo >= 0 Then
                            _caretIndex = i
                            _caretCharIndex = offParagrafo
                            _tblRow = -1
                            _tblCol = -1
                            _tblPara = -1
                            _selectionActive = False
                            trovato = True
                            Exit For
                        End If
                        Continue For
                    End If

                    Dim tabellaCorpo As DocTable = TryCast(el, DocTable)
                    If tabellaCorpo IsNot Nothing AndAlso tabellaCorpo.Rows IsNot Nothing Then
                        For r As Integer = 0 To tabellaCorpo.Rows.Count - 1
                            Dim riga As DocTableRow = tabellaCorpo.Rows(r)
                            If riga Is Nothing OrElse riga.Cells Is Nothing Then Continue For
                            For c As Integer = 0 To riga.Cells.Count - 1
                                Dim cella As DocTableCell = riga.Cells(c)
                                If cella Is Nothing OrElse cella.Paragraphs Is Nothing Then Continue For
                                For pIdx As Integer = 0 To cella.Paragraphs.Count - 1
                                    Dim offCella As Integer = OffsetRunNota(cella.Paragraphs(pIdx), bookmarkId)
                                    If offCella >= 0 Then
                                        _caretIndex = i
                                        _tblRow = r
                                        _tblCol = c
                                        _tblPara = pIdx
                                        _caretCharIndex = offCella
                                        _selectionActive = False
                                        trovato = True
                                        Exit For
                                    End If
                                Next
                                If trovato Then Exit For
                            Next
                            If trovato Then Exit For
                        Next
                        If trovato Then Exit For
                    End If
                Next
            End SyncLock

            If trovato Then
                TriggerLayout()
                ScrollToCaret()
                If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
                UpdateCaretAndNotify(False)
                Dim paraArrivo As DocParagraph = GetCurrentParagraph()
                Dim testoArrivo As String = If(paraArrivo IsNot Nothing, paraArrivo.GetText(), "")
                If _gestoreMovimento IsNot Nothing Then
                    _gestoreMovimento.LeggiTesto(If(String.IsNullOrWhiteSpace(testoArrivo), "Riferimento della nota", "Riferimento della nota. " & testoArrivo))
                End If
            End If

            Return trovato
        Catch ex As Exception
            logger.ScriviLog("TornaAlTestoDellaNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Sub VaiAllaNotaNelDocumento(bookmarkId As String)
        Try
            If String.IsNullOrEmpty(bookmarkId) Then Return
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim pNota As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If pNota.BookmarkId = bookmarkId Then
                            _caretIndex = i
                            Dim testoNota As String = pNota.GetText()
                            _caretCharIndex = If(String.IsNullOrEmpty(testoNota), 0, testoNota.Length)
                            _selectionActive = False
                            _tblRow = -1
                            _tblCol = -1
                            _tblPara = -1
                            Exit For
                        End If
                    End If
                Next
            End SyncLock
            ScrollToCaret()
            UpdateCaretAndNotify(False)
            If lettura IsNot Nothing Then lettura.LeggiTesto("Modifica nota nel documento")
        Catch ex As Exception
            logger.ScriviLog("VaiAllaNotaNelDocumento: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub EliminaNotaAllaPosizioneCorrente()
        Try
            If SolaLettura Then Return

            Dim bookmarkTrovato As String = TrovaBookmarkNotaAllaPosizioneCorrente()
            If String.IsNullOrEmpty(bookmarkTrovato) Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Non sei su un riferimento a una nota")
                Return
            End If

            SaveUndoState()

            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count AndAlso TypeOf _elements(_caretIndex) Is DocParagraph Then
                    Dim pCorrente As DocParagraph = DirectCast(_elements(_caretIndex), DocParagraph)
                    Dim runDaRimuovere As DocRun = Nothing
                    For Each r As DocRun In pCorrente.Runs
                        If r IsNot Nothing AndAlso r.IsSuperscript AndAlso r.LinkUrl = bookmarkTrovato Then
                            runDaRimuovere = r
                            Exit For
                        End If
                    Next
                    If runDaRimuovere IsNot Nothing Then
                        pCorrente.Runs.Remove(runDaRimuovere)
                        pCorrente.CachedHeight = -1
                    End If
                End If

                Dim indiceParagrafoNota As Integer = -1
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim pn As DocParagraph = DirectCast(_elements(i), DocParagraph)
                        If pn.BookmarkId = bookmarkTrovato Then
                            indiceParagrafoNota = i
                            Exit For
                        End If
                    End If
                Next
                If indiceParagrafoNota >= 0 Then
                    _elements.RemoveAt(indiceParagrafoNota)
                    If _caretIndex > indiceParagrafoNota Then _caretIndex -= 1
                End If
            End SyncLock

            _isModified = True
            TriggerLayout()
            If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
            If lettura IsNot Nothing Then lettura.LeggiTesto("Nota eliminata")
        Catch ex As Exception
            logger.ScriviLog("EliminaNotaAllaPosizioneCorrente: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub


    Public Function OttieniNumeroProgressivoNota(bookmarkId As String) As Integer
        Try
            Dim listaBookmark As List(Of String) = OttieniElencoBookmarkNote()
            Dim indice As Integer = listaBookmark.IndexOf(bookmarkId)
            If indice < 0 Then Return 0

            Dim eNotaFinale As Boolean = bookmarkId.StartsWith("NOTA_FINE_")
            Dim numeroStessoTipo As Integer = 0
            For i As Integer = 0 To indice
                If listaBookmark(i).StartsWith("NOTA_FINE_") = eNotaFinale Then numeroStessoTipo += 1
            Next
            Return numeroStessoTipo
        Catch ex As Exception
            logger.ScriviLog("OttieniNumeroProgressivoNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 0
        End Try
    End Function

    Public Function OttieniDescrizioneNumeroNota(bookmarkId As String) As String
        Try
            Dim numero As Integer = OttieniNumeroProgressivoNota(bookmarkId)
            If numero <= 0 Then Return ""

            Dim eNotaFinale As Boolean = bookmarkId.StartsWith("NOTA_FINE_")
            Return If(eNotaFinale, "Nota di chiusura " & numero, "Nota " & numero)
        Catch ex As Exception
            logger.ScriviLog("OttieniDescrizioneNumeroNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Function EsistonoNote() As Boolean
        Try
            Return OttieniElencoBookmarkNote().Count > 0
        Catch ex As Exception
            logger.ScriviLog("EsistonoNote: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private _bookmarkNotaInModifica As String = ""

    Private ReadOnly _estensioniComuniDaNonSpezzare As New List(Of String) From {"it", "com", "org", "net", "eu", "info", "biz", "co"}

    Public Sub RiparaNoteSenzaACapo()
        Try
            Dim listaBookmark As List(Of String) = OttieniElencoBookmarkNote()
            Dim daRiparare As New List(Of String)
            For Each bookmarkId In listaBookmark
                Dim testo As String = OttieniTestoNota(bookmarkId)
                If Not String.IsNullOrEmpty(testo) AndAlso Not testo.Contains(ControlChars.Lf) AndAlso testo.Length > 150 AndAlso RiparaTestoSenzaACapo(testo) <> testo Then
                    daRiparare.Add(bookmarkId)
                End If
            Next

            If daRiparare.Count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna nota da riparare trovata")
                Return
            End If

            If daRiparare.Count = 1 Then
                ApriCampoRiparazioneNota(daRiparare(0))
                Return
            End If

            Dim menu As New WGMenu()
            For i As Integer = 0 To daRiparare.Count - 1
                Dim bookmarkCatturato As String = daRiparare(i)
                menu.aggiungi("Nota " & (i + 1) & " di " & daRiparare.Count, Sub(s As Object, ev As EventArgs) ApriCampoRiparazioneNota(bookmarkCatturato))
            Next
            menu.avvia(daRiparare.Count & " note da riparare", True)
        Catch ex As Exception
            logger.ScriviLog("RiparaNoteSenzaACapo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriCampoRiparazioneNota(bookmarkId As String)
        Try
            Dim testoOriginale As String = OttieniTestoNota(bookmarkId)
            Dim testoRiparato As String = RiparaTestoSenzaACapo(testoOriginale)

            _bookmarkNotaInModifica = bookmarkId
            _campiNote = New Campi()
            _campiNote.AggiungiMultiriga("Nota da riparare, verifica gli a-capo prima di confermare", "c.nota", False, False, testoRiparato)
            _campiNote.SalvaSuEscape = True
            _campiNote.ImpostaMaiuscolaAutomatica("c.nota", MaiuscolaAutomaticaAttiva)
            _campiNote.ImpostaImpostazioniTemporanee(config.LeggiValore("echo_digitazione"), LivelloPunteggiaturaScrittura)
            AddHandler _campiNote.Fine, AddressOf FineCampoNotaSingola
            _campiNote.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ApriCampoRiparazioneNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function RiparaTestoSenzaACapo(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.Contains(ControlChars.Lf) Then Return testo

            Dim sb As New StringBuilder(testo.Length + 16)
            For i As Integer = 0 To testo.Length - 1
                Dim c As Char = testo(i)
                sb.Append(c)

                If (c = "."c OrElse c = "!"c OrElse c = "?"c) AndAlso i < testo.Length - 1 Then
                    Dim successivo As Char = testo(i + 1)
                    If Char.IsLetter(successivo) Then
                        Dim fineParolaSuccessiva As Integer = i + 1
                        While fineParolaSuccessiva < testo.Length AndAlso Char.IsLetter(testo(fineParolaSuccessiva))
                            fineParolaSuccessiva += 1
                        End While
                        Dim parolaSuccessiva As String = testo.Substring(i + 1, fineParolaSuccessiva - (i + 1)).ToLowerInvariant()

                        If c = "."c AndAlso _estensioniComuniDaNonSpezzare.Contains(parolaSuccessiva) Then
                            Continue For
                        End If

                        sb.Append(ControlChars.Lf)
                    End If
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("RiparaTestoSenzaACapo: " & ex.Message)
            Return testo
        End Try
    End Function

    Public Sub LeggiModificaNote(Optional bookmarkIniziale As String = "")
        Try
            Dim listaBookmark As List(Of String) = OttieniElencoBookmarkNote()
            If listaBookmark.Count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Il documento non contiene note")
                Return
            End If

            If Not String.IsNullOrEmpty(bookmarkIniziale) AndAlso listaBookmark.Contains(bookmarkIniziale) Then
                ApriCampoNotaSingola(bookmarkIniziale)
                Return
            End If

            _listaBookmarkNoteCorrente = listaBookmark
            Dim menu As New WGMenu()
            For i As Integer = 0 To listaBookmark.Count - 1
                Dim bookmarkId As String = listaBookmark(i)
                Dim eNotaFinale As Boolean = bookmarkId.StartsWith("NOTA_FINE_")
                Dim testoNota As String = OttieniTestoNota(bookmarkId)
                Dim anteprima As String = testoNota
                If anteprima.Length > 60 Then anteprima = anteprima.Substring(0, 60) & "..."
                Dim voce As String = "Nota " & (i + 1) & If(eNotaFinale, " di chiusura", "") & ": " & anteprima
                Dim bookmarkCatturato As String = bookmarkId
                menu.aggiungi(voce, Sub(s As Object, ev As EventArgs) ApriCampoNotaSingola(bookmarkCatturato))
            Next
            menu.avvia("Note del documento, " & listaBookmark.Count & " note", True)
        Catch ex As Exception
            logger.ScriviLog("LeggiModificaNote: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriCampoNotaSingola(bookmarkId As String)
        Try
            _bookmarkNotaInModifica = bookmarkId
            Dim listaBookmark As List(Of String) = OttieniElencoBookmarkNote()
            Dim indice As Integer = listaBookmark.IndexOf(bookmarkId)
            Dim eNotaFinale As Boolean = bookmarkId.StartsWith("NOTA_FINE_")
            Dim titoloCampo As String = "Nota " & (indice + 1) & " di " & listaBookmark.Count & If(eNotaFinale, " (nota di chiusura)", " (nota a piè di pagina)")

            _campiNote = New Campi()
            _campiNote.AggiungiMultiriga(titoloCampo, "c.nota", False, SolaLettura, OttieniTestoNota(bookmarkId))
            _campiNote.SalvaSuEscape = Not SolaLettura
            _campiNote.ImpostaMaiuscolaAutomatica("c.nota", MaiuscolaAutomaticaAttiva AndAlso Not SolaLettura)
            _campiNote.ImpostaImpostazioniTemporanee(config.LeggiValore("echo_digitazione"), LivelloPunteggiaturaScrittura)
            AddHandler _campiNote.Fine, AddressOf FineCampoNotaSingola
            _campiNote.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ApriCampoNotaSingola: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineCampoNotaSingola(sender As Object, e As EventArgs)
        Try
            If _campiNote Is Nothing Then Return
            RemoveHandler _campiNote.Fine, AddressOf FineCampoNotaSingola
            If SolaLettura OrElse String.IsNullOrEmpty(_bookmarkNotaInModifica) Then Return

            Dim testoAggiornato As String = _campiNote.RecuperaValore("c.nota")
            AggiornaTestoNota(_bookmarkNotaInModifica, testoAggiornato)
            _bookmarkNotaInModifica = ""

            Dim msg As New WGMessaggi()
            msg.Messaggio("Nota salvata.")
            msg.ShowDialog()

            Try
                If Me.IsHandleCreated Then Me.Focus()
                If lettura IsNot Nothing Then lettura.LeggiTesto("Documento " & _docTitle)
                If _gestoreMovimento IsNot Nothing Then _gestoreMovimento.LeggiRigaCorrente()
            Catch exRitorno As Exception
                logger.ScriviLog("Annuncio ritorno documento: " & exRitorno.Message)
            End Try
        Catch ex As Exception
            logger.ScriviLog("FineCampoNotaSingola: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub



    Private Function OttieniElencoBookmarkNote() As List(Of String)
        Dim risultato As New List(Of String)
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            If Not String.IsNullOrEmpty(p.BookmarkId) AndAlso (p.BookmarkId.StartsWith("NOTA_PIE_") OrElse p.BookmarkId.StartsWith("NOTA_FINE_")) Then
                                risultato.Add(p.BookmarkId)
                            End If
                        End If
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("OttieniElencoBookmarkNote: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Function OttieniTestoNota(bookmarkId As String) As String
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            If p.BookmarkId = bookmarkId Then
                                Dim sbTesto As New StringBuilder()
                                If p.Runs IsNot Nothing Then
                                    For Each r As DocRun In p.Runs
                                        If r IsNot Nothing AndAlso Not r.IsImage Then
                                            If Not (r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl = bookmarkId) Then
                                                If Not String.IsNullOrEmpty(r.Text) Then sbTesto.Append(r.Text)
                                            End If
                                        End If
                                    Next
                                End If
                                Return sbTesto.ToString().Trim()
                            End If
                        End If
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("OttieniTestoNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return ""
    End Function

    Private Sub AggiornaTestoNota(bookmarkId As String, nuovoTesto As String)
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For Each el In _elements
                        If TypeOf el Is DocParagraph Then
                            Dim p As DocParagraph = DirectCast(el, DocParagraph)
                            If p.BookmarkId = bookmarkId Then
                                Dim runMarcatore As DocRun = Nothing
                                If p.Runs IsNot Nothing Then
                                    For Each r As DocRun In p.Runs
                                        If r IsNot Nothing AndAlso r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl = bookmarkId Then
                                            runMarcatore = r
                                            Exit For
                                        End If
                                    Next
                                End If

                                SaveUndoState()

                                p.Runs.Clear()
                                If runMarcatore IsNot Nothing Then p.Runs.Add(runMarcatore)
                                p.Runs.Add(New DocRun() With {.Text = " "})
                                Dim runsNuovoTesto As List(Of DocRun) = CreaRunsDaTestoPiano(nuovoTesto, 9.0F)
                                If runsNuovoTesto IsNot Nothing Then
                                    For Each rNuovo As DocRun In runsNuovoTesto
                                        If rNuovo IsNot Nothing Then p.Runs.Add(rNuovo)
                                    Next
                                End If
                                p.CachedHeight = -1

                                _isModified = True
                                TriggerLayout()
                                If Me.IsHandleCreated Then InvalidateRect(Me.Handle, IntPtr.Zero, False)
                                Exit For
                            End If
                        End If
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("AggiornaTestoNota: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub
End Class