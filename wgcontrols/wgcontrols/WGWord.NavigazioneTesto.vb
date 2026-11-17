Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

' Spostamento e lettura all'interno del testo per paragrafi, frasi, pagine e posizioni del cursore con supporto sonoro.
Partial Public Class WGWord

    Private _suoniNavigazione As WGSuoni
    Private _annuncioPaginaPendente As String = ""

#Region "Risoluzione robusta del paragrafo corrente"

    ''' <summary>
    ''' Risolve il paragrafo effettivamente puntato dal cursore, sia che si trovi
    ''' in un paragrafo "normale" di primo livello, sia che si trovi dentro una
    ''' cella di una tabella (_tblRow/_tblCol/_tblPara).
    ''' A differenza di GetCurrentParagraph() (che gestisce solo i paragrafi di
    ''' primo livello e restituisce Nothing per le tabelle, come dimostra la
    ''' gestione separata di Home/End su DocTable in WGWord.vb), questo metodo
    ''' copre anche il caso "cursore in tabella", eliminando i casi in cui i
    ''' comandi di navigazione restavano silenziosamente senza effetto.
    ''' </summary>
    ''' <param name="para">Il paragrafo corrente, se trovato.</param>
    ''' <param name="inTabella">True se il paragrafo si trova dentro una cella di tabella.</param>
    ''' <returns>True se un paragrafo valido è stato individuato.</returns>
    Private Function TryOttieniParagrafoCorrente(ByRef para As DocParagraph, ByRef inTabella As Boolean) As Boolean
        para = Nothing
        inTabella = False
        Try
            SyncLock _elementsLock
                If _elements Is Nothing OrElse _caretIndex < 0 OrElse _caretIndex >= _elements.Count Then
                    Return False
                End If

                Dim elemento As DocElement = _elements(_caretIndex)

                If TypeOf elemento Is DocParagraph Then
                    para = DirectCast(elemento, DocParagraph)
                    inTabella = False
                    Return True
                End If

                If TypeOf elemento Is DocTable Then
                    Dim tabella As DocTable = DirectCast(elemento, DocTable)
                    If tabella.Rows Is Nothing OrElse tabella.Rows.Count = 0 Then Return False

                    If _tblRow < 0 OrElse _tblRow >= tabella.Rows.Count Then _tblRow = 0
                    Dim riga = tabella.Rows(_tblRow)
                    If riga.Cells Is Nothing OrElse riga.Cells.Count = 0 Then Return False

                    If _tblCol < 0 OrElse _tblCol >= riga.Cells.Count Then _tblCol = 0
                    Dim cella As DocTableCell = riga.Cells(_tblCol)
                    If cella.Paragraphs Is Nothing OrElse cella.Paragraphs.Count = 0 Then Return False

                    If _tblPara < 0 OrElse _tblPara >= cella.Paragraphs.Count Then _tblPara = 0
                    para = cella.Paragraphs(_tblPara)
                    inTabella = True
                    Return True
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
        Return False
    End Function

    ''' <summary>
    ''' Applica in modo uniforme scroll, ridisegno e notifica del cursore, così
    ''' da avere un unico punto di sincronizzazione dopo ogni spostamento.
    ''' </summary>
    Public Sub VaiAParagrafoSuccessivo()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim count As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then count = _elements.Count
            End SyncLock

            If count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Documento vuoto")
                Return
            End If

            If _caretIndex >= count - 1 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("fine documento")
                Return
            End If

            _caretIndex += 1
            _caretCharIndex = 0
            _selectionActive = False

            Dim annuncio As String = PreparaElementoCorrente()
            AggiornaCursoreDopoSpostamento()
            If lettura IsNot Nothing Then lettura.LeggiTesto(annuncio)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAParagrafoPrecedente()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim count As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then count = _elements.Count
            End SyncLock

            If count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Documento vuoto")
                Return
            End If

            If _caretIndex <= 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("inizio documento")
                Return
            End If

            _caretIndex -= 1
            If _caretIndex >= count Then _caretIndex = count - 1
            _caretCharIndex = 0
            _selectionActive = False

            Dim annuncio As String = PreparaElementoCorrente()
            AggiornaCursoreDopoSpostamento()
            If lettura IsNot Nothing Then lettura.LeggiTesto(annuncio)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiABookmarkConFruscio(bookmarkId As String)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If String.IsNullOrEmpty(bookmarkId) Then Return

            Dim idPulito As String = If(bookmarkId.StartsWith("#"), bookmarkId.Substring(1), bookmarkId)

            Dim indiceTarget As Integer = -1
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = 0 To _elements.Count - 1
                        Dim pp As DocParagraph = TryCast(_elements(i), DocParagraph)
                        If pp IsNot Nothing AndAlso pp.BookmarkId = idPulito Then
                            indiceTarget = i
                            Exit For
                        End If
                    Next
                End If
            End SyncLock

            If indiceTarget < 0 Then
                VaiABookmark(idPulito)
                Return
            End If

            Dim interruzioni As List(Of Integer) = TrovaIndiciInterruzioniPagina()
            Dim paginaPartenza As Integer = 1
            Dim paginaArrivo As Integer = 1
            For Each b As Integer In interruzioni
                If b < _caretIndex Then paginaPartenza += 1
                If b < indiceTarget Then paginaArrivo += 1
            Next

            _caretIndex = indiceTarget
            _caretCharIndex = 0
            _selectionActive = False
            TriggerLayout()

            Dim titolo As String = TitoloSezioneDaIndice(idPulito)
            Dim annuncio As String = PreparaElementoCorrente()
            If Not String.IsNullOrEmpty(titolo) Then annuncio = "Sezione " & titolo & ". " & annuncio

            AggiornaCursoreDopoSpostamento()

            If paginaArrivo <> paginaPartenza AndAlso interruzioni.Count > 0 Then
                AnnunciaConFruscio(ComponiMessaggioPagina(paginaArrivo, annuncio))
            Else
                If lettura IsNot Nothing Then lettura.LeggiTesto(annuncio)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function TitoloSezioneDaIndice(bookmarkId As String) As String
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return ""
                For Each el In _elements
                    Dim p As DocParagraph = TryCast(el, DocParagraph)
                    If p Is Nothing OrElse Not p.IsIndexContainer OrElse p.Runs Is Nothing Then Continue For
                    For Each r As DocRun In p.Runs
                        If r IsNot Nothing AndAlso r.LinkUrl = bookmarkId AndAlso Not String.IsNullOrEmpty(r.Text) Then
                            Return r.Text.Trim()
                        End If
                    Next
                Next
            End SyncLock
            Return ""
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AssicuraSuoniNavigazione()
        Try
            If _suoniNavigazione Is Nothing Then
                _suoniNavigazione = New WGSuoni()
                AddHandler _suoniNavigazione.FruscioPaginaTerminato, AddressOf GestisciFruscioTerminato
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Friend _ultimaPaginaNavigazione As Integer = -1

    Friend Sub ControllaAnnuncioCambioPagina()
        Try
            If Not LeggiIntestazioneCambioPagina Then
                _ultimaPaginaNavigazione = -1
                Return
            End If

            Dim pagina As Integer = 1
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                Dim limite As Integer = Math.Min(_caretIndex - 1, _elements.Count - 1)
                For i As Integer = 0 To limite
                    If TypeOf _elements(i) Is DocParagraph AndAlso DirectCast(_elements(i), DocParagraph).IsPageBreak Then pagina += 1
                Next
            End SyncLock

            If _ultimaPaginaNavigazione = -1 Then
                _ultimaPaginaNavigazione = pagina
                Return
            End If

            If pagina <> _ultimaPaginaNavigazione Then
                _ultimaPaginaNavigazione = pagina
                If LeggiIntestazioneCambioPagina AndAlso lettura IsNot Nothing Then
                    Dim msg As String = "Pagina " & pagina & ". "
                    If Not String.IsNullOrEmpty(HeaderText) Then msg &= "Intestazione: " & HeaderText & ". "
                    If Not String.IsNullOrEmpty(FooterText) Then msg &= "Piè di pagina: " & FooterText & "."
                    lettura.LeggiTesto(msg)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("ControllaAnnuncioCambioPagina: " & ex.Message)
        End Try
    End Sub

    Private Function ComponiMessaggioPagina(numeroPagina As Integer, annuncio As String) As String
        Try
            Dim messaggio As String = "Pagina " & numeroPagina & ", "
            If LeggiIntestazioneCambioPagina Then
                If Not String.IsNullOrEmpty(HeaderText) Then
                    messaggio &= "Intestazione: " & HeaderText & ". "
                End If
                If Not String.IsNullOrEmpty(FooterText) Then
                    messaggio &= "Piè di pagina: " & FooterText & ". "
                End If
            End If
            Return messaggio & annuncio
        Catch ex As Exception
            logger.ScriviLog("ComponiMessaggioPagina: " & ex.Message)
            Return "Pagina " & numeroPagina & ", " & annuncio
        End Try
    End Function

    Private Sub AnnunciaConFruscio(messaggio As String)
        Try
            If Not FruscioPaginaAttivo Then
                If lettura IsNot Nothing Then lettura.LeggiTesto(messaggio)
                Return
            End If

            AssicuraSuoniNavigazione()
            _annuncioPaginaPendente = messaggio
            Dim avviato As Boolean = False
            If _suoniNavigazione IsNot Nothing Then avviato = _suoniNavigazione.FruscioPagina()
            If Not avviato Then
                _annuncioPaginaPendente = ""
                If lettura IsNot Nothing Then lettura.LeggiTesto(messaggio)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Try
                _annuncioPaginaPendente = ""
                If lettura IsNot Nothing Then lettura.LeggiTesto(messaggio)
            Catch ex2 As Exception
                logger.ScriviLog(ex2.Message)
            End Try
        End Try
    End Sub

    Private Sub GestisciFruscioTerminato()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Me.IsHandleCreated Then
                Me.BeginInvoke(New Action(AddressOf AnnunciaPaginaPendente))
            Else
                AnnunciaPaginaPendente()
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaPaginaPendente()
        Try
            Dim messaggio As String = _annuncioPaginaPendente
            _annuncioPaginaPendente = ""
            If Not String.IsNullOrEmpty(messaggio) AndAlso lettura IsNot Nothing Then lettura.LeggiTesto(messaggio)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAPaginaSuccessiva()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim count As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then count = _elements.Count
            End SyncLock

            Dim interruzioni As List(Of Integer) = TrovaIndiciInterruzioniPagina()
            If interruzioni.Count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna interruzione di pagina nel documento")
                Return
            End If

            Dim paginaCorrente As Integer = 1
            For Each b As Integer In interruzioni
                If b < _caretIndex Then paginaCorrente += 1
            Next

            If interruzioni.Count < paginaCorrente Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna pagina successiva")
                Return
            End If

            Dim target As Integer = interruzioni(paginaCorrente - 1) + 1
            If target >= count Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna pagina successiva")
                Return
            End If

            _caretIndex = target
            _caretCharIndex = 0
            _selectionActive = False

            Dim annuncio As String = PreparaElementoCorrente()
            AggiornaCursoreDopoSpostamento()
            AnnunciaConFruscio(ComponiMessaggioPagina(paginaCorrente + 1, annuncio))
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAPaginaPrecedente()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim count As Integer = 0
            SyncLock _elementsLock
                If _elements IsNot Nothing Then count = _elements.Count
            End SyncLock

            Dim interruzioni As List(Of Integer) = TrovaIndiciInterruzioniPagina()
            If interruzioni.Count = 0 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna interruzione di pagina nel documento")
                Return
            End If

            Dim paginaCorrente As Integer = 1
            For Each b As Integer In interruzioni
                If b < _caretIndex Then paginaCorrente += 1
            Next

            If paginaCorrente <= 1 Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessuna pagina precedente")
                Return
            End If

            Dim target As Integer
            If paginaCorrente - 1 = 1 Then
                target = 0
            Else
                target = interruzioni(paginaCorrente - 3) + 1
            End If
            If target < 0 Then target = 0
            If target >= count Then target = count - 1

            _caretIndex = target
            _caretCharIndex = 0
            _selectionActive = False

            Dim annuncio As String = PreparaElementoCorrente()
            AggiornaCursoreDopoSpostamento()
            AnnunciaConFruscio(ComponiMessaggioPagina(paginaCorrente - 1, annuncio))
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function PreparaElementoCorrente() As String
        Try
            Dim el As DocElement = Nothing
            SyncLock _elementsLock
                If _elements IsNot Nothing AndAlso _caretIndex >= 0 AndAlso _caretIndex < _elements.Count Then
                    el = _elements(_caretIndex)
                End If
            End SyncLock

            If el Is Nothing Then Return "Elemento non disponibile"

            If TypeOf el Is DocTable Then
                Dim tbl As DocTable = DirectCast(el, DocTable)
                _tblRow = 0
                _tblCol = 0
                _tblPara = 0
                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                    If tbl.SavedCol >= 0 AndAlso tbl.SavedCol < tbl.Rows(0).Cells.Count Then _tblCol = tbl.SavedCol
                End If
                Return "Tabella"
            End If

            If TypeOf el Is DocParagraph Then
                Dim para As DocParagraph = DirectCast(el, DocParagraph)
                _tblRow = -1
                _tblCol = -1
                _tblPara = -1
                If para.IsPageBreak Then Return "Interruzione di pagina"
                Dim testo As String = TestoLeggibileParagrafo(para)
                If String.IsNullOrWhiteSpace(testo) Then Return "Riga vuota"
                Return testo
            End If

            Return "Elemento sconosciuto"
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return "Errore"
        End Try
    End Function

    Friend _ultimoGruppoElencoAnnunciato As Integer = -1

    Friend Function AnnuncioElenco(para As DocParagraph, Optional indiceHint As Integer = -1) As String
        Try
            If para Is Nothing OrElse Not para.IsListItem Then
                _ultimoGruppoElencoAnnunciato = -1
                Return ""
            End If

            Dim indicePara As Integer = -1
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    If indiceHint >= 0 AndAlso indiceHint < _elements.Count AndAlso _elements(indiceHint) Is para Then
                        indicePara = indiceHint
                    Else
                        For i As Integer = 0 To _elements.Count - 1
                            If _elements(i) Is para Then
                                indicePara = i
                                Exit For
                            End If
                        Next
                    End If
                End If
            End SyncLock

            Dim vocePropria As String = If(Not String.IsNullOrEmpty(para.ListNumValue), para.ListNumValue & ", ", "puntato, ")

            If indicePara < 0 Then Return vocePropria

            Return vocePropria
        Catch ex As Exception
            logger.ScriviLog("AnnuncioElenco: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function TestoLeggibileParagrafo(para As DocParagraph) As String
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return ""
            Dim leggiNumeroNota As Boolean = LeggiNumeroNotaInScorrimento
            Dim sb As New System.Text.StringBuilder()
            sb.Append(AnnuncioElenco(para, _caretIndex))
            For Each r As DocRun In para.Runs
                If r Is Nothing Then Continue For
                If r.IsImage Then
                    sb.Append("immagine ")
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text).Append(" ")
                ElseIf r.IsSuperscript AndAlso Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                    If leggiNumeroNota Then
                        Dim numero As Integer = OttieniNumeroProgressivoNota(r.LinkUrl)
                        If numero > 0 Then sb.Append(" (").Append(numero).Append(")")
                    End If
                ElseIf r.IsHyperlink OrElse Not String.IsNullOrEmpty(r.LinkUrl) Then
                    sb.Append("link ")
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text).Append(" ")
                Else
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text)
                End If
            Next
            Return sb.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Function TrovaIndiciInterruzioniPagina() As List(Of Integer)
        Dim indici As New List(Of Integer)
        Try
            SyncLock _elementsLock
                If _elements IsNot Nothing Then
                    For i As Integer = 0 To _elements.Count - 1
                        Dim p As DocParagraph = TryCast(_elements(i), DocParagraph)
                        If p IsNot Nothing AndAlso p.IsPageBreak Then indici.Add(i)
                    Next
                End If
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
        Return indici
    End Function

    Private Function ContaCaratteriParagrafo(para As DocParagraph) As Integer
        Try
            If para Is Nothing Then Return 0
            Dim testo As String = para.GetText()
            If String.IsNullOrEmpty(testo) Then Return 0
            Return testo.Length
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Friend Sub AggiornaCursoreDopoSpostamento()
        ScrollToCaret()
        InvalidateRect(Me.Handle, IntPtr.Zero, False)
        UpdateCaretAndNotify(False)
    End Sub

#End Region

#Region "Inizio / fine del paragrafo corrente"

    Public Sub VaiAInizioParagrafo()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessun paragrafo disponibile")
                Return
            End If

            _caretCharIndex = 0
            _selectionActive = False
            AggiornaCursoreDopoSpostamento()
            If lettura IsNot Nothing Then
                Dim testo As String = para.GetText()
                If String.IsNullOrEmpty(testo) Then
                    lettura.LeggiTesto("Paragrafo vuoto")
                Else
                    lettura.LeggiTesto("Inizio paragrafo, " & WGSegni.Converti(testo(0).ToString()))
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAFineParagrafo()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then
                If lettura IsNot Nothing Then lettura.LeggiTesto("Nessun paragrafo disponibile")
                Return
            End If

            Dim testo As String = para.GetText()
            _caretCharIndex = If(String.IsNullOrEmpty(testo), 0, testo.Length)
            _selectionActive = False
            AggiornaCursoreDopoSpostamento()
            If lettura IsNot Nothing Then
                If String.IsNullOrEmpty(testo) Then
                    lettura.LeggiTesto("Paragrafo vuoto")
                Else
                    lettura.LeggiTesto("Fine paragrafo, dopo " & WGSegni.Converti(testo(testo.Length - 1).ToString()))
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

#End Region

    Public Sub LeggiParagrafoCorrente()
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then Return

            Dim testo As String = para.GetText()
            If String.IsNullOrWhiteSpace(testo) Then
                lettura.LeggiTesto("Paragrafo vuoto")
                Return
            End If
            lettura.LeggiTesto(testo)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiParagrafoPrecedente()
        Try
            Dim testo As String = ""
            Dim trovato As Boolean = False
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex - 1 To 0 Step -1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim t As String = DirectCast(_elements(i), DocParagraph).GetText()
                        testo = If(String.IsNullOrWhiteSpace(t), "Paragrafo vuoto", t)
                        trovato = True
                        Exit For
                    ElseIf TypeOf _elements(i) Is DocTable Then
                        testo = TestoTabellaPerSintesi(DirectCast(_elements(i), DocTable))
                        trovato = True
                        Exit For
                    End If
                Next
            End SyncLock
            If trovato Then
                lettura.LeggiTesto(testo)
            Else
                lettura.LeggiTesto("inizio documento")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiParagrafoSuccessivo()
        Try
            Dim testo As String = ""
            Dim trovato As Boolean = False
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = _caretIndex + 1 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        Dim t As String = DirectCast(_elements(i), DocParagraph).GetText()
                        testo = If(String.IsNullOrWhiteSpace(t), "Paragrafo vuoto", t)
                        trovato = True
                        Exit For
                    ElseIf TypeOf _elements(i) Is DocTable Then
                        testo = TestoTabellaPerSintesi(DirectCast(_elements(i), DocTable))
                        trovato = True
                        Exit For
                    End If
                Next
            End SyncLock
            If trovato Then
                lettura.LeggiTesto(testo)
            Else
                lettura.LeggiTesto("fine documento")
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiFraseCorrente()
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then Return

            Dim testo As String = para.GetText()
            Dim frasi As List(Of String) = DividiInFrasi(testo)
            If frasi.Count = 0 Then
                lettura.LeggiTesto("Paragrafo vuoto")
                Return
            End If
            Dim idx As Integer = TrovaIndiceFraseCorrente(testo, _caretCharIndex)
            If idx < 0 OrElse idx >= frasi.Count Then idx = 0
            _ultimaFraseLetta = frasi(idx)
            lettura.LeggiTesto(_ultimaFraseLetta)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAFraseSuccessiva()
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then Return

            Dim testo As String = para.GetText()
            Dim frasi As List(Of String) = DividiInFrasi(testo)
            Dim idx As Integer = TrovaIndiceFraseCorrente(testo, _caretCharIndex)
            If idx < 0 Then idx = 0
            If idx + 1 < frasi.Count Then
                _caretCharIndex = TrovaInizioFrase(testo, idx + 1)
                _selectionActive = False
                _ultimaFraseLetta = frasi(idx + 1)
                AggiornaCursoreDopoSpostamento()
                lettura.LeggiTesto(_ultimaFraseLetta)
            Else
                Dim nuovoIndex As Integer = -1
                Dim testoNuovo As String = ""
                Dim frasiNuove As List(Of String) = Nothing
                SyncLock _elementsLock
                    If _elements Is Nothing Then Return
                    For i As Integer = _caretIndex + 1 To _elements.Count - 1
                        If TypeOf _elements(i) Is DocParagraph Then
                            nuovoIndex = i
                            Dim pNuovo As DocParagraph = DirectCast(_elements(i), DocParagraph)
                            testoNuovo = pNuovo.GetText()
                            frasiNuove = DividiInFrasi(testoNuovo)
                            Exit For
                        End If
                    Next
                End SyncLock
                If nuovoIndex >= 0 Then
                    _caretIndex = nuovoIndex
                    _caretCharIndex = 0
                    _selectionActive = False
                    AggiornaCursoreDopoSpostamento()
                    If frasiNuove IsNot Nothing AndAlso frasiNuove.Count > 0 Then
                        _ultimaFraseLetta = frasiNuove(0)
                        lettura.LeggiTesto(_ultimaFraseLetta)
                    Else
                        lettura.LeggiTesto("Paragrafo vuoto")
                    End If
                Else
                    lettura.LeggiTesto("fine documento")
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub VaiAFrasePrecedente()
        Try
            Dim para As DocParagraph = Nothing
            Dim inTabella As Boolean = False
            If Not TryOttieniParagrafoCorrente(para, inTabella) Then Return

            Dim testo As String = para.GetText()
            Dim frasi As List(Of String) = DividiInFrasi(testo)
            Dim idx As Integer = TrovaIndiceFraseCorrente(testo, _caretCharIndex)
            If idx > 0 Then
                _caretCharIndex = TrovaInizioFrase(testo, idx - 1)
                _selectionActive = False
                _ultimaFraseLetta = frasi(idx - 1)
                AggiornaCursoreDopoSpostamento()
                lettura.LeggiTesto(_ultimaFraseLetta)
            Else
                Dim nuovoIndex As Integer = -1
                Dim nuovoChar As Integer = 0
                Dim testoLetto As String = ""
                SyncLock _elementsLock
                    If _elements Is Nothing Then Return
                    For i As Integer = _caretIndex - 1 To 0 Step -1
                        If TypeOf _elements(i) Is DocParagraph Then
                            nuovoIndex = i
                            Dim pNuovo As DocParagraph = DirectCast(_elements(i), DocParagraph)
                            Dim frasiNuove As List(Of String) = DividiInFrasi(pNuovo.GetText())
                            If frasiNuove.Count > 0 Then
                                nuovoChar = TrovaInizioFrase(pNuovo.GetText(), frasiNuove.Count - 1)
                                testoLetto = frasiNuove(frasiNuove.Count - 1)
                            Else
                                nuovoChar = 0
                                testoLetto = "Paragrafo vuoto"
                            End If
                            Exit For
                        End If
                    Next
                End SyncLock
                If nuovoIndex >= 0 Then
                    _caretIndex = nuovoIndex
                    _caretCharIndex = nuovoChar
                    _selectionActive = False
                    AggiornaCursoreDopoSpostamento()
                    _ultimaFraseLetta = testoLetto
                    lettura.LeggiTesto(_ultimaFraseLetta)
                Else
                    lettura.LeggiTesto("inizio documento")
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiPosizioneCursore()
        Try
            Dim totaleParagrafi As Integer = 0
            Dim numeroParagrafoCorrente As Integer = 0
            Dim inTabella As Boolean = False
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        totaleParagrafi += 1
                        If i = _caretIndex Then numeroParagrafoCorrente = totaleParagrafi
                    ElseIf TypeOf _elements(i) Is DocTable AndAlso i = _caretIndex Then
                        inTabella = True
                    End If
                Next
            End SyncLock
            Dim msg As String
            If inTabella Then
                msg = "In tabella"
            ElseIf numeroParagrafoCorrente = 0 Then
                msg = "Posizione sconosciuta"
            Else
                msg = "Paragrafo " & numeroParagrafoCorrente & " di " & totaleParagrafi
            End If
            Dim stato As StatoCursore = OttieniStatoCursore()
            If stato.InTabella Then
                msg &= ", in tabella, riga " & (stato.RigaTabella + 1) & " colonna " & (stato.ColonnaTabella + 1)
            End If
            msg &= ", carattere " & (_caretCharIndex + 1)
            lettura.LeggiTesto(msg)
            MostraMessaggioStato(msg)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AnnunciaContestoCorrente()
        Try
            Dim parti As New List(Of String)
            Dim totaleParagrafi As Integer = 0
            Dim numeroCorrente As Integer = 0
            Dim styleId As String = ""
            Dim bookmarkId As String = ""
            Dim runsSnapshot As List(Of DocRun) = Nothing
            SyncLock _elementsLock
                If _elements Is Nothing Then Return
                For i As Integer = 0 To _elements.Count - 1
                    If TypeOf _elements(i) Is DocParagraph Then
                        totaleParagrafi += 1
                        If i = _caretIndex Then
                            numeroCorrente = totaleParagrafi
                            Dim p As DocParagraph = DirectCast(_elements(i), DocParagraph)
                            styleId = If(p.StyleId, "")
                            bookmarkId = If(p.BookmarkId, "")
                            If p.Runs IsNot Nothing Then
                                runsSnapshot = New List(Of DocRun)(p.Runs)
                            End If
                        End If
                    End If
                Next
            End SyncLock
            parti.Add("Paragrafo " & numeroCorrente & " di " & totaleParagrafi)
            If Not String.IsNullOrEmpty(styleId) AndAlso _stiliTitolo.ContainsKey(styleId) Then
                parti.Add("Titolo livello " & _stiliTitolo(styleId))
            End If
            If Not String.IsNullOrEmpty(bookmarkId) AndAlso bookmarkId.StartsWith("BKMK_") Then
                parti.Add("Segnalibro " & bookmarkId.Substring(5))
            End If
            If Not String.IsNullOrEmpty(bookmarkId) AndAlso _commentiPerParagrafo.ContainsKey(bookmarkId) Then
                parti.Add(_commentiPerParagrafo(bookmarkId).Count & " commenti")
            End If
            Dim sezione As String = OttieniSezioneCorrenteIndice()
            If Not String.IsNullOrEmpty(sezione) AndAlso Not sezione.StartsWith("Il cursore non si trova") Then
                parti.Add("Sezione " & sezione)
            End If
            Dim stato As StatoCursore = OttieniStatoCursore()
            If stato.InTabella Then parti.Add("In tabella, riga " & (stato.RigaTabella + 1) & " colonna " & (stato.ColonnaTabella + 1))
            If runsSnapshot IsNot Nothing Then
                Dim charCount As Integer = 0
                For Each r As DocRun In runsSnapshot
                    Dim runLen As Integer = If(String.IsNullOrEmpty(r.Text), 0, r.Text.Length)
                    If r.IsImage Then runLen = 1
                    If _caretCharIndex >= charCount AndAlso _caretCharIndex < charCount + runLen Then
                        If Not String.IsNullOrEmpty(r.LinkUrl) Then parti.Add("Link " & r.LinkUrl)
                        If r.IsImage Then parti.Add("Immagine")
                        Exit For
                    End If
                    charCount += runLen
                Next
            End If
            Dim testoFinale As String = String.Join(", ", parti)
            lettura.LeggiTesto(testoFinale)
            MostraMessaggioStato(testoFinale)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

End Class