Imports System.Windows.Forms
Imports System.Drawing
Imports System.ComponentModel
Imports System.Threading
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports wgcontrols

' Gestione dei movimenti del cursore, della tastiera e della lettura vocale continua per la navigazione nel documento.
Public Class WGWordGestoreMovimento

    Private _word As WGWord
    Private logger As New Logger()
    Private WithEvents lettura As sintesivocale
    Private _ultimoGrassetto As Boolean = False
    Private _ultimoCorsivo As Boolean = False
    Private _ultimoLink As String = ""
    Private _inLetturaContinuaValore As Boolean = False
    ' La lettura continua (F8) e' una proprieta' perche' il braille deve saperne l'inizio e la fine.
    Public Property inLetturaContinua As Boolean
        Get
            Return _inLetturaContinuaValore
        End Get
        Set(value As Boolean)
            If _inLetturaContinuaValore = value Then Return
            _inLetturaContinuaValore = value
            WGBraille.ImpostaLetturaContinua(Me, value)
        End Set
    End Property

    Private Shared _istanzaInLetturaContinua As WGWordGestoreMovimento
    Private _confermatoLimiteVerticaleDoc As Boolean = False
    Private _direzioneLimiteVerticaleDoc As Integer = 0
    Private _colonnaDesiderataVerticale As Integer = -1

    ' Conta gli Invio consecutivi premuti stando sull'ultima cella dell'ultima riga di una
    ' tabella: solo al secondo Invio consecutivo si esce dalla tabella creando una nuova riga
    ' (paragrafo) subito sotto di essa. Viene azzerato da qualunque altro tasto (vedi
    ' ResetInvioConsecutiviTabella, chiamato da WGWord.GestisciKeyDown).
    Private _invioConsecutiviUltimaRigaTabella As Integer = 0

    Public Sub ResetInvioConsecutiviTabella()
        _invioConsecutiviUltimaRigaTabella = 0
    End Sub

    Public Sub ImpostaConfineVerticaleConfermato(direzione As Integer)
        _confermatoLimiteVerticaleDoc = True
        _direzioneLimiteVerticaleDoc = direzione
    End Sub
    Private _mappaLettura As New List(Of Tuple(Of Integer, Integer, Integer))
    Private _letturaContinuaIndex As Integer = 0
    Private WithEvents _timerLettura As New System.Windows.Forms.Timer()

    ' Evita che il watchdog (_timerLettura) e il vero segnale di fine lettura (OnFineLettura)
    ' possano entrambi far avanzare il paragrafo quasi contemporaneamente: solo il primo dei
    ' due che scatta per il paragrafo corrente ha effetto, l'altro viene ignorato.
    Private ReadOnly _transizioneLetturaLock As New Object()
    Private _transizioneGestita As Boolean = False

    Private WithEvents _tastiera As WGTastiera

    Private _isHookActive As Boolean = False
    Private _altPremutoDaHook As Boolean = False
    Private _altDestraPremuto As Boolean = False
    Private _altDestroUsato As Boolean = False
    Private isShiftPremuto As Boolean = False
    Private isCtrlSinistroPremuto As Boolean = False
    Private isCtrlDestroPremuto As Boolean = False

    Public Event menudestroapertura(sender As Object, e As EventArgs)

    <DllImport("user32.dll")>
    Private Shared Function GetKeyboardState(lpKeyState As Byte()) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetAsyncKeyState(ByVal vKey As Integer) As Short
    End Function

    Private Const VK_RMENU As Integer = &HA5

    Public Sub New(word As WGWord)
        Try
            If word IsNot Nothing Then
                _word = word
            End If
            lettura = sintesivocale.GetInstance()
            _tastiera = WGTastiera.GetInstance()

            _timerLettura.Interval = 6000
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftPremuto() Handles _tastiera.ShiftPremuto
        isShiftPremuto = True
    End Sub

    Private Sub _tastiera_ShiftRilasciato() Handles _tastiera.ShiftRilasciato
        isShiftPremuto = False
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs) Handles _tastiera.AltPremuto
        _altPremutoDaHook = True
    End Sub

    Private Sub _tastiera_AltRilasciato(sender As Object, e As EventArgs) Handles _tastiera.AltRilasciato
        _altPremutoDaHook = False
    End Sub

    ' Premere Ctrl ferma la voce: la lettura continua deve fermarsi con lei, altrimenti la fine della frase interrotta
    ' la fa ripartire dal paragrafo dopo.
    Private Sub InterrompiLetturaPerControl()
        Try
            If Not inLetturaContinua Then Return
            If _altDestraPremuto OrElse (_tastiera IsNot Nothing AndAlso _tastiera.AltDestroAttualmentePremuto) Then Return
            If _word Is Nothing OrElse _word.IsDisposed OrElse Not _word.ContainsFocus Then Return
            FermaLetturaContinua()
        Catch ex As Exception
            logger.ScriviLog("InterrompiLetturaPerControl: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ControlSinistroPremuto() Handles _tastiera.ControlSinistroPremuto
        isCtrlSinistroPremuto = True
        InterrompiLetturaPerControl()
    End Sub

    Private Sub _tastiera_ControlSinistroRilasciato() Handles _tastiera.ControlSinistroRilasciato
        isCtrlSinistroPremuto = False
    End Sub

    Private Sub _tastiera_ControlDestroPremuto() Handles _tastiera.ControlDestroPremuto
        isCtrlDestroPremuto = True
        InterrompiLetturaPerControl()
    End Sub

    Private Sub _tastiera_ControlDestroRilasciato() Handles _tastiera.ControlDestroRilasciato
        isCtrlDestroPremuto = False
    End Sub

    Private Sub _tastiera_AltDestroPremuto() Handles _tastiera.AltDestroPremuto
        _altDestraPremuto = True
        _altDestroUsato = False
    End Sub

    Private Sub _tastiera_AltDestroRilasciato() Handles _tastiera.AltDestroRilasciato
        Try
            If _altDestraPremuto AndAlso Not isShiftPremuto AndAlso Not _altDestroUsato Then
                RaiseEvent menudestroapertura(Me, EventArgs.Empty)
            End If
            _altDestraPremuto = False
            _altDestroUsato = False
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AzzeraModificatori()
        Try
            _altPremutoDaHook = False
            _altDestraPremuto = False
            _altDestroUsato = False
            isShiftPremuto = False
            isCtrlSinistroPremuto = False
            isCtrlDestroPremuto = False
        Catch ex As Exception
            logger.ScriviLog("WGWordGestoreMovimento.AzzeraModificatori: " & ex.Message)
        End Try
    End Sub

    Public Sub attivahook()
        Try
            _isHookActive = True
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub disattivahook()
        Try
            _isHookActive = False
            inLetturaContinua = False
            _timerLettura.Stop()
            If lettura IsNot Nothing Then
                lettura.FermaVoce()
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function OttieniCarattereDaTasto(tasto As Keys) As Char
        Dim virtualKey As UInteger = CUInt(tasto And Keys.KeyCode)

        ' Simboli italiani sopra i numeri solo con la tastiera italiana scelta in WinGabriel (vedi WGCampoMovimento
        ' per lo stesso controllo): con un'altra lingua di tastiera scelta nelle impostazioni si usa
        ' CarattereDaLayoutScelto piu' sotto, che da' il simbolo giusto per quella tastiera.
        If isShiftPremuto AndAlso virtualKey >= 48 AndAlso virtualKey <= 57 AndAlso WGTastiera.TastieraItalianaAttiva() Then
            Select Case virtualKey
                Case 49 : Return "!"c
                Case 50 : Return """"c
                Case 51 : Return "£"c
                Case 52 : Return "$"c
                Case 53 : Return "%"c
                Case 54 : Return "&"c
                Case 55 : Return "/"c
                Case 56 : Return "("c
                Case 57 : Return ")"c
                Case 48 : Return "="c
            End Select
        End If

        Dim keyboardState(255) As Byte
        GetKeyboardState(keyboardState)

        If isShiftPremuto Then
            keyboardState(Keys.ShiftKey) = &H80
        Else
            keyboardState(Keys.ShiftKey) = 0
        End If

        If My.Computer.Keyboard.CapsLock Then
            keyboardState(Keys.Capital) = 1
        Else
            keyboardState(Keys.Capital) = 0
        End If

        If (isCtrlSinistroPremuto OrElse isCtrlDestroPremuto) AndAlso _altPremutoDaHook Then
            keyboardState(Keys.ControlKey) = &H80
            keyboardState(Keys.Menu) = &H80
        End If

        ' La tastiera usata per tradurre il tasto e' quella scelta in WinGabriel, non quella che Windows ha attiva.
        Dim risultatoLayout As String = WGTastiera.CarattereDaLayoutScelto(virtualKey, keyboardState)
        If risultatoLayout <> "" Then Return risultatoLayout(0)
        Return Convert.ToChar(0)
    End Function

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If _altDestraPremuto OrElse (_tastiera IsNot Nothing AndAlso _tastiera.AltDestroAttualmentePremuto) Then
                _altDestroUsato = True
            End If

            If tasto = Keys.None OrElse Not _isHookActive Then Return

            If _word IsNot Nothing AndAlso Not _word.IsDisposed AndAlso _word.IsHandleCreated AndAlso _word.ContainsFocus Then

                Dim isCtrl As Boolean = isCtrlSinistroPremuto OrElse isCtrlDestroPremuto

                If isCtrl Then
                    inLetturaContinua = False
                    _timerLettura.Stop()
                    If lettura IsNot Nothing Then lettura.FermaVoce()
                End If

                ' Lo spazio interrompe la lettura continua, come in tutti gli altri campi.
                If tasto = Keys.Space AndAlso inLetturaContinua Then
                    FermaLetturaContinua()
                    tasto = Keys.None
                    Return
                End If

                If tasto = Keys.F8 Then
                    If inLetturaContinua Then
                        inLetturaContinua = False
                        _timerLettura.Stop()
                        If lettura IsNot Nothing Then lettura.FermaVoce()
                    Else
                        LetturaContinua()
                    End If
                    Return
                End If

                Dim tastoBase As Keys = tasto And Keys.KeyCode
                Dim isAltDestra As Boolean = _altDestraPremuto AndAlso WGTastiera.TastieraItalianaAttiva()
                Dim caratterePremuto As Char = OttieniCarattereDaTasto(tasto)
                Dim caratterePremutoStr As String = caratterePremuto.ToString()

                If isAltDestra Then
                    Dim insertChar As String = ""

                    If tastoBase = Keys.Oem3 OrElse caratterePremutoStr = "ò" OrElse caratterePremutoStr = "Ò" Then
                        insertChar = "@"
                    ElseIf tastoBase = Keys.Oem7 OrElse caratterePremutoStr = "à" OrElse caratterePremutoStr = "À" Then
                        insertChar = "#"
                    ElseIf tastoBase = Keys.Oem5 OrElse caratterePremutoStr = "è" OrElse caratterePremutoStr = "È" Then
                        insertChar = If(isShiftPremuto, "{", "[")
                    ElseIf tastoBase = Keys.Oemplus OrElse caratterePremutoStr = "+" Then
                        insertChar = If(isShiftPremuto, "}", "]")
                    ElseIf tastoBase = Keys.E OrElse caratterePremutoStr = "e" OrElse caratterePremutoStr = "E" Then
                        insertChar = "€"
                    End If

                    If insertChar <> "" Then
                        _altDestroUsato = True
                        If Not _word.SolaLettura Then
                            _word.SaveUndoState()
                            If _word._selectionActive Then _word.DeleteSelection()
                            _word.InsertChar(insertChar(0))
                            GestisciEcoDigitazione(insertChar(0), _word.config.LeggiValore("echo_digitazione"))
                            _word.ScrollToCaret()
                            _word.Invalidate()
                            _word.UpdateCaretAndNotify(True)
                        End If
                        tasto = Keys.None
                        Return
                    End If
                End If

                If tastoBase = Keys.Delete Then
                    If isCtrl AndAlso isShiftPremuto Then
                        GestisciCancellaAvanti(2)
                    ElseIf isCtrl Then
                        GestisciCancellaAvanti(1)
                    Else
                        GestisciCancellaAvanti(0)
                    End If
                    Return
                End If

                Dim gestito As Boolean = _word.GestisciKeyDown(tasto)
                If gestito Then
                    If tastoBase = Keys.Space Then
                        GestisciEcoDigitazione(" "c, _word.config.LeggiValore("echo_digitazione"))
                    End If
                Else
                    Dim c As Char = caratterePremuto
                    If Char.IsLetter(c) Then
                        Dim maiuscola As Boolean = isShiftPremuto
                        If Control.IsKeyLocked(Keys.CapsLock) Then maiuscola = Not maiuscola
                        c = If(maiuscola, Char.ToUpper(c), Char.ToLower(c))
                    ElseIf isShiftPremuto Then
                        c = Char.ToUpper(c)
                    End If
                    If c <> Convert.ToChar(0) AndAlso Not Char.IsControl(c) Then
                        _word.GestisciChar(c)
                        GestisciEcoDigitazione(c, _word.config.LeggiValore("echo_digitazione"))
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub
    Private vocebloccata As Boolean
    Private Sub bloccalettura() Handles lettura.vocestoppata
        vocebloccata = True
    End Sub

    Private Sub _timerLettura_Tick(sender As Object, e As EventArgs) Handles _timerLettura.Tick
        Try
            _timerLettura.Stop()
            If Not inLetturaContinua Then Return

            ' Prima di considerare la lettura "bloccata", verifichiamo lo stato reale del
            ' motore vocale (aggiornato dal poll NVDA). Se sta ancora effettivamente parlando
            ' (es. sta pronunciando le ultime parole di una frase lunga) non interrompiamo:
            ' diamo altro tempo, cosi' non vengono piu' tagliate le parole finali.
            If lettura IsNot Nothing AndAlso lettura.StaParlando Then
                _timerLettura.Start()
                Return
            End If

            SyncLock _transizioneLetturaLock
                If _transizioneGestita Then Return
                _transizioneGestita = True
            End SyncLock

            ' Nessun avanzamento rilevato dal motore vocale entro il tempo previsto.
            ' Invece di interrompere la lettura continua (F8), si prosegue con il paragrafo
            ' successivo cosi' la lettura non si blocca in modo silenzioso.
            If lettura IsNot Nothing Then lettura.FermaVoce()

            If _word Is Nothing OrElse _word.IsDisposed OrElse Not _word.IsHandleCreated OrElse vocebloccata = True Then
                inLetturaContinua = False
                Return
            End If

            _letturaContinuaIndex += 1
            _word.BeginInvoke(New Action(AddressOf LeggiParagrafoCorrente))
        Catch ex As Exception
            logger.ScriviLog("_timerLettura_Tick: " & ex.Message)
            inLetturaContinua = False
        End Try
    End Sub

    Private Sub GestisciParolaLetta(posizione As Integer, lunghezza As Integer) Handles lettura.OnParolaLetta
        If inLetturaContinua AndAlso _word IsNot Nothing AndAlso Not _word.IsDisposed AndAlso _word.IsHandleCreated Then
            _word.BeginInvoke(Sub()
                                  Try
                                      If _word Is Nothing OrElse _word.IsDisposed Then Return

                                      _timerLettura.Stop()
                                      _timerLettura.Start()

                                      Dim elemIndex As Integer = -1
                                      Dim offsetPara As Integer = 0
                                      Dim offsetIniziale As Integer = 0

                                      For i As Integer = _mappaLettura.Count - 1 To 0 Step -1
                                          If posizione >= _mappaLettura(i).Item1 Then
                                              elemIndex = _mappaLettura(i).Item2
                                              offsetPara = posizione - _mappaLettura(i).Item1
                                              offsetIniziale = _mappaLettura(i).Item3
                                              Exit For
                                          End If
                                      Next

                                      If elemIndex <> -1 AndAlso _word._elements IsNot Nothing AndAlso elemIndex < _word._elements.Count Then
                                          _word._caretIndex = elemIndex
                                          Dim el = _word._elements(elemIndex)

                                          If TypeOf el Is DocParagraph Then
                                              Dim para = DirectCast(el, DocParagraph)
                                              Dim testoLen As String = para.GetText()
                                              Dim textLen As Integer = If(String.IsNullOrEmpty(testoLen), 0, testoLen.Length)
                                              Dim posReale As Integer = offsetPara + offsetIniziale
                                              If posReale > textLen Then posReale = textLen
                                              If posReale < 0 Then posReale = 0
                                              _word._caretCharIndex = posReale
                                          Else
                                              _word._caretCharIndex = 0
                                          End If

                                          _word.ScrollToCaret()
                                          _word.Invalidate()
                                          _word.UpdateCaretAndNotify(False)
                                      End If
                                  Catch ex As Exception
                                      logger.ScriviLog(ex.Message)
                                  End Try
                              End Sub)
        End If
    End Sub

    Private Sub GestisciFineLettura() Handles lettura.OnFineLettura
        Try
            If Not inLetturaContinua Then
                _timerLettura.Stop()
                Return
            End If

            SyncLock _transizioneLetturaLock
                If _transizioneGestita Then Return
                _transizioneGestita = True
            End SyncLock

            _timerLettura.Stop()
            _letturaContinuaIndex += 1
            If _word Is Nothing OrElse _word.IsDisposed OrElse Not _word.IsHandleCreated Then
                inLetturaContinua = False
                Return
            End If
            _word.BeginInvoke(New Action(AddressOf LeggiParagrafoCorrente))
        Catch ex As Exception
            logger.ScriviLog("GestisciFineLettura: " & ex.Message)
            inLetturaContinua = False
            _timerLettura.Stop()
        End Try
    End Sub

    Private Sub LeggiParagrafoCorrente()
        Try
            If Not inLetturaContinua Then Return
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing Then
                inLetturaContinua = False
                _timerLettura.Stop()
                Return
            End If

            Dim testoParaLetto As String = Nothing
            Dim elemIndexTrovato As Integer = -1

            SyncLock _word._elementsLock
                Dim idx As Integer = _letturaContinuaIndex
                While idx < _word._elements.Count
                    Dim el = _word._elements(idx)
                    If TypeOf el Is DocParagraph Then
                        Dim para As DocParagraph = DirectCast(el, DocParagraph)
                        Dim testo As String = OttieniTestoLeggibile(para)
                        If String.IsNullOrWhiteSpace(testo) Then
                            idx += 1
                            Continue While
                        End If
                        _letturaContinuaIndex = idx
                        elemIndexTrovato = idx
                        testoParaLetto = testo
                        Exit While
                    ElseIf TypeOf el Is DocTable Then
                        Dim testoTabella As String = OttieniTestoLeggibileTabella(DirectCast(el, DocTable))
                        If String.IsNullOrWhiteSpace(testoTabella) Then
                            idx += 1
                            Continue While
                        End If
                        _letturaContinuaIndex = idx
                        elemIndexTrovato = idx
                        testoParaLetto = testoTabella
                        Exit While
                    End If
                    idx += 1
                End While
            End SyncLock

            If elemIndexTrovato = -1 Then
                inLetturaContinua = False
                _timerLettura.Stop()
                Return
            End If

            _word._caretIndex = elemIndexTrovato
            _word._caretCharIndex = 0
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing AndAlso elemIndexTrovato >= 0 AndAlso elemIndexTrovato < _word._elements.Count AndAlso TypeOf _word._elements(elemIndexTrovato) Is DocTable Then
                    _word._tblRow = 0
                    _word._tblCol = 0
                    _word._tblPara = 0
                End If
            End SyncLock
            _word.ScrollToCaret()
            _word.Invalidate()
            _word.UpdateCaretAndNotify(False)

            ' Nuovo paragrafo in lettura: da qui in poi timer e OnFineLettura possono
            ' nuovamente far avanzare la lettura (uno solo dei due, il primo che scatta).
            SyncLock _transizioneLetturaLock
                _transizioneGestita = False
            End SyncLock

            _timerLettura.Stop()
            ' Con la sola barra braille il paragrafo resta sulla barra per il tempo scelto: il controllo di blocco non deve scattare prima.
            _timerLettura.Interval = WGBraille.IntervalloWatchdog(6000)
            _timerLettura.Start()

            ' L'invio del testo al motore vocale viene eseguito su un thread separato:
            ' questo metodo gira sul thread UI (chiamato via _word.BeginInvoke) e non deve
            ' mai restare in attesa di operazioni di I/O verso il motore vocale (pipe/rete),
            ' altrimenti l'intera interfaccia si blocca.
            Dim testoDaLeggere As String = testoParaLetto
            Task.Run(Sub()
                         Try
                             lettura.LeggiTesto(testoDaLeggere, False, "Normale", False)
                         Catch ex As Exception
                             logger.ScriviLog("LeggiParagrafoCorrente (invio asincrono): " & ex.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            logger.ScriviLog("LeggiParagrafoCorrente: " & ex.Message)
            inLetturaContinua = False
            _timerLettura.Stop()
        End Try
    End Sub

    Private Sub GestisciCancellaAvanti(modalita As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.SolaLettura Then Return

            If _word._selectionActive Then
                _word.SaveUndoState()
                _word.DeleteSelection()
                If lettura IsNot Nothing Then lettura.LeggiTesto("selezione cancellata")
                _word.ScrollToCaret()
                _word.Invalidate()
                _word.UpdateCaretAndNotify(True)
                Return
            End If

            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para Is Nothing Then Return

            Dim txt As String = para.GetText()
            If _word._caretCharIndex < txt.Length Then
                Dim startIdx As Integer = _word._caretCharIndex
                Dim endIdx As Integer = startIdx

                If modalita = 0 Then
                    endIdx = startIdx + 1
                ElseIf modalita = 1 Then
                    While endIdx < txt.Length AndAlso Char.IsWhiteSpace(txt(endIdx))
                        endIdx += 1
                    End While
                    While endIdx < txt.Length AndAlso Not Char.IsWhiteSpace(txt(endIdx)) AndAlso Not Char.IsPunctuation(txt(endIdx))
                        endIdx += 1
                    End While
                    If endIdx = startIdx AndAlso endIdx < txt.Length Then endIdx += 1
                ElseIf modalita = 2 Then
                    While endIdx < txt.Length AndAlso txt(endIdx) <> "."c AndAlso txt(endIdx) <> "!"c AndAlso txt(endIdx) <> "?"c
                        endIdx += 1
                    End While
                    If endIdx < txt.Length Then endIdx += 1
                    While endIdx < txt.Length AndAlso Char.IsWhiteSpace(txt(endIdx))
                        endIdx += 1
                    End While
                    If endIdx = startIdx AndAlso endIdx < txt.Length Then endIdx += 1
                End If

                If endIdx > startIdx Then
                    Dim deletedText As String = txt.Substring(startIdx, endIdx - startIdx)

                    _word.SaveUndoState()
                    _word._selAnchorIndex = _word._caretIndex
                    _word._selAnchorCharIndex = endIdx
                    _word._selAnchorSubIndex = _word._tblPara
                    _word._selectionActive = True

                    _word._caretCharIndex = startIdx
                    _word.DeleteSelection()

                    If lettura IsNot Nothing Then
                        If modalita = 0 Then
                            lettura.LeggiTesto(WGSegni.Converti(deletedText, True) & " cancellato")
                        ElseIf modalita = 1 Then
                            lettura.LeggiTesto(deletedText & " cancellato")
                        Else
                            lettura.LeggiTesto("frase cancellata")
                        End If
                    End If
                    _word.ScrollToCaret()
                    _word.Invalidate()
                    _word.UpdateCaretAndNotify(True)
                End If
            Else
                Dim count As Integer = 0
                SyncLock _word._elementsLock
                    If _word._elements IsNot Nothing Then count = _word._elements.Count
                End SyncLock
                If _word._caretIndex < count - 1 Then
                    _word.SaveUndoState()
                    _word._selAnchorIndex = _word._caretIndex + 1
                    _word._selAnchorCharIndex = 0
                    _word._selAnchorSubIndex = 0
                    _word._selectionActive = True
                    _word.DeleteSelection()

                    If lettura IsNot Nothing Then lettura.LeggiTesto("riga unita")
                    _word.ScrollToCaret()
                    _word.Invalidate()
                    _word.UpdateCaretAndNotify(True)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function OttieniTestoLeggibile(para As DocParagraph) As String
        If para Is Nothing OrElse para.Runs Is Nothing Then Return ""
        Dim sb As New StringBuilder()
        If _word IsNot Nothing Then sb.Append(_word.AnnuncioElenco(para, _word._caretIndex))
        For Each r As DocRun In para.Runs
            If r IsNot Nothing Then
                If r.IsImage Then
                    sb.Append("immagine ")
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text).Append(" ")
                ElseIf Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                    If _word IsNot Nothing AndAlso _word.LeggiNumeroNotaInScorrimento Then
                        Dim numero As Integer = _word.OttieniNumeroProgressivoNota(r.LinkUrl)
                        If numero > 0 Then sb.Append(" (").Append(numero).Append(") ")
                    End If
                ElseIf r.IsHyperlink OrElse Not String.IsNullOrEmpty(r.LinkUrl) Then
                    sb.Append("link ")
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text).Append(" ")
                Else
                    If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text)
                End If
            End If
        Next
        Return sb.ToString().Trim()
    End Function

    Private Function OttieniTestoLeggibileTabella(tbl As DocTable) As String
        Try
            Dim sb As New StringBuilder()
            sb.Append("Tabella. ")
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return sb.ToString().Trim()
            Dim numeroRiga As Integer = 1
            For Each riga As DocTableRow In tbl.Rows
                Dim parti As New List(Of String)()
                If riga IsNot Nothing AndAlso riga.Cells IsNot Nothing Then
                    Dim numeroCella As Integer = 1
                    For Each cella As DocTableCell In riga.Cells
                        Dim cellText As New StringBuilder()
                        If cella IsNot Nothing AndAlso cella.Paragraphs IsNot Nothing Then
                            For Each p As DocParagraph In cella.Paragraphs
                                If p Is Nothing Then Continue For
                                Dim testoCella As String = OttieniTestoLeggibile(p)
                                If Not String.IsNullOrWhiteSpace(testoCella) Then cellText.Append(testoCella).Append(" ")
                            Next
                        End If
                        Dim testoPulito As String = cellText.ToString().Trim()
                        If String.IsNullOrWhiteSpace(testoPulito) Then testoPulito = "vuota"
                        parti.Add("colonna " & numeroCella.ToString() & " " & testoPulito)
                        numeroCella += 1
                    Next
                End If
                sb.Append("Riga ").Append(numeroRiga.ToString()).Append(": ").Append(String.Join(", ", parti.ToArray())).Append(". ")
                numeroRiga += 1
            Next
            Return sb.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog("OttieniTestoLeggibileTabella: " & ex.Message)
            Return "Tabella."
        End Try
    End Function

    Private Function OttieniTestoNota(bookmarkId As String) As String
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing Then Return ""
            If String.IsNullOrEmpty(bookmarkId) Then Return ""
            SyncLock _word._elementsLock
                For Each el In _word._elements
                    If TypeOf el Is DocParagraph Then
                        Dim p As DocParagraph = DirectCast(el, DocParagraph)
                        If p.BookmarkId = bookmarkId AndAlso p.Runs IsNot Nothing Then
                            Dim sb As New StringBuilder()
                            For Each r As DocRun In p.Runs
                                If r IsNot Nothing AndAlso Not r.IsImage Then
                                    If Not (Not String.IsNullOrEmpty(r.LinkUrl) AndAlso r.LinkUrl = bookmarkId AndAlso r.IsSuperscript) Then
                                        If Not String.IsNullOrEmpty(r.Text) Then sb.Append(r.Text)
                                    End If
                                End If
                            Next
                            Return sb.ToString().Trim()
                        End If
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("OttieniTestoNota Error: " & ex.Message)
        End Try
        Return ""
    End Function

    Public Sub LeggiTesto(testo As String)
        Try
            If lettura IsNot Nothing Then lettura.LeggiTesto(testo)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Stacca questa istanza dall'ascolto della tastiera fisica: da usare quando si crea
    ''' un'istanza aggiuntiva solo per accedere alle utilita' di lettura (es. dal tocco), senza
    ''' che duplichi anche la gestione della tastiera gia' fatta dall'istanza principale.
    ''' Azzerare un campo WithEvents stacca automaticamente tutti i gestori "Handles" collegati.
    ''' </summary>
    Public Sub DisattivaAscoltoTastieraFisica()
        Try
            _tastiera = Nothing
        Catch ex As Exception
            logger.ScriviLog("DisattivaAscoltoTastieraFisica: " & ex.Message)
        End Try
    End Sub

    Public Sub LetturaContinua()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing Then Return
            If lettura Is Nothing Then Return
            If Not _word.IsHandleCreated Then Return
            _letturaContinuaIndex = Math.Max(0, _word._caretIndex)
            inLetturaContinua = True
            _istanzaInLetturaContinua = Me
            SyncLock _transizioneLetturaLock
                _transizioneGestita = False
            End SyncLock
            _word.BeginInvoke(New Action(AddressOf LeggiParagrafoCorrente))
        Catch ex As Exception
            logger.ScriviLog("LetturaContinua: " & ex.Message)
            inLetturaContinua = False
        End Try
    End Sub

    Public Sub FermaLetturaContinua()
        Try
            inLetturaContinua = False
            If _istanzaInLetturaContinua Is Me Then _istanzaInLetturaContinua = Nothing
            _timerLettura.Stop()
            If lettura IsNot Nothing Then lettura.FermaVoce()
        Catch ex As Exception
            logger.ScriviLog("FermaLetturaContinua: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Attiva o disattiva la lettura continua a interruttore, come fa il tasto F8: se e' gia'
    ''' in corso la ferma, altrimenti la avvia dalla posizione attuale del cursore.
    ''' La lettura avviata da tastiera e quella avviata dal tocco passano da istanze diverse di
    ''' questa classe, ciascuna con il proprio inLetturaContinua: senza il riferimento condiviso
    ''' qui sotto l'interruttore del tocco non vedrebbe la lettura avviata con F8 e ne avvierebbe
    ''' una seconda invece di fermarla.
    ''' </summary>
    Public Sub AttivaDisattivaLetturaContinua()
        Try
            Dim istanzaAttiva As WGWordGestoreMovimento = _istanzaInLetturaContinua

            If istanzaAttiva IsNot Nothing AndAlso istanzaAttiva.inLetturaContinua Then
                istanzaAttiva.FermaLetturaContinua()
                _istanzaInLetturaContinua = Nothing
                Return
            End If

            If inLetturaContinua Then
                FermaLetturaContinua()
                _istanzaInLetturaContinua = Nothing
                Return
            End If

            _istanzaInLetturaContinua = Me
            LetturaContinua()
        Catch ex As Exception
            logger.ScriviLog("AttivaDisattivaLetturaContinua: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniLarghezzaContenutoCorrente() As Integer
        Try
            If _word Is Nothing OrElse _word.IsDisposed Then Return 600
            If _word._renderer IsNot Nothing Then
                Return _word._renderer.LarghezzaContenuto(_word.ClientSize.Width)
            End If
            Return Math.Max(40, _word.ClientSize.Width - 16)
        Catch ex As Exception
            logger.ScriviLog("OttieniLarghezzaContenutoCorrente: " & ex.Message)
            Return 600
        End Try
    End Function

    Private Function CalcolaRigheVisualiParagrafo(para As DocParagraph) As List(Of Tuple(Of Integer, Integer))
        Dim righe As New List(Of Tuple(Of Integer, Integer))
        Try
            If para Is Nothing Then
                righe.Add(Tuple.Create(0, 0))
                Return righe
            End If

            Dim zoom As Single = If(_word IsNot Nothing AndAlso _word._renderer IsNot Nothing, _word._renderer.Zoom, 1.0F)
            Dim isNotaPie As Boolean = Not String.IsNullOrEmpty(para.BookmarkId) AndAlso (para.BookmarkId.StartsWith("NOTA_PIE_") OrElse para.BookmarkId.StartsWith("NOTA_FINE_"))
            Dim fontFactor As Single = If(isNotaPie, 0.9F, 1.0F)
            Dim larghezzaContenuto As Integer = OttieniLarghezzaContenutoCorrente()
            Dim larghezzaParagrafo As Integer = Math.Max(40, larghezzaContenuto - (para.IndentLevel * CInt(40 * zoom)))

            Dim righeMotore As List(Of WGWordLayoutMotore.WGRigaVisuale) = WGWordLayoutMotore.CalcolaRighe(para, larghezzaParagrafo, zoom, fontFactor)
            For Each r As WGWordLayoutMotore.WGRigaVisuale In righeMotore
                righe.Add(Tuple.Create(r.OffsetInizio, r.Lunghezza))
            Next

            If righe.Count = 0 Then righe.Add(Tuple.Create(0, para.GetText().Length))
        Catch ex As Exception
            logger.ScriviLog("CalcolaRigheVisualiParagrafo: " & ex.Message)
            righe.Clear()
            righe.Add(Tuple.Create(0, If(para IsNot Nothing, para.GetText().Length, 0)))
        End Try
        Return righe
    End Function

    Private Function OttieniRigaVisualeCorrente(para As DocParagraph) As String
        Try
            Dim testo As String = para.GetText()
            Dim righe = CalcolaRigheVisualiParagrafo(para)
            Dim posizioneCaret As Integer = System.Math.Max(0, System.Math.Min(_word._caretCharIndex, testo.Length))
            For Each riga In righe
                Dim inizio As Integer = riga.Item1
                Dim fine As Integer = inizio + riga.Item2
                If posizioneCaret >= inizio AndAlso (posizioneCaret < fine OrElse fine = testo.Length) Then
                    Return testo.Substring(inizio, riga.Item2)
                End If
            Next
            If righe.Count > 0 Then
                Dim ultima = righe(righe.Count - 1)
                Return testo.Substring(ultima.Item1, ultima.Item2)
            End If
            Return testo
        Catch ex As Exception
            logger.ScriviLog("OttieniRigaVisualeCorrente: " & ex.Message)
            Return para.GetText()
        End Try
    End Function

    Public Sub LeggiRigaCorrente()
        Try
            If _word Is Nothing OrElse _word.IsDisposed Then Return
            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing AndAlso lettura IsNot Nothing Then
                Dim righe = CalcolaRigheVisualiParagrafo(para)
                Dim txt As String
                If righe.Count <= 1 Then
                    txt = OttieniTestoLeggibile(para)
                Else
                    txt = OttieniRigaVisualeCorrente(para)
                    If para.IsListItem Then txt = _word.AnnuncioElenco(para, _word._caretIndex) & txt
                End If
                If String.IsNullOrWhiteSpace(txt) Then txt = "riga vuota"
                lettura.LeggiTesto(txt)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiParolaCorrente()
        Try
            If _word Is Nothing OrElse _word.IsDisposed Then Return
            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing AndAlso para.Runs IsNot Nothing AndAlso lettura IsNot Nothing Then
                Dim currentPos As Integer = 0
                Dim foundImage As Boolean = False
                For Each r In para.Runs
                    If r IsNot Nothing Then
                        If r.IsImage AndAlso _word._caretCharIndex = currentPos Then
                            Dim dida As String = If(String.IsNullOrEmpty(r.Text), "Immagine", "Immagine " & r.Text)
                            lettura.LeggiTesto(dida)
                            foundImage = True
                            Exit For
                        ElseIf Not r.IsImage Then
                            If Not String.IsNullOrEmpty(r.Text) Then currentPos += r.Text.Length
                        End If
                    End If
                Next
                If foundImage Then Return

                Dim txt As String = para.GetText()
                If Not String.IsNullOrEmpty(txt) AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < txt.Length Then
                    Dim i As Integer = _word._caretCharIndex
                    While i > 0 AndAlso i <= txt.Length AndAlso Not Char.IsWhiteSpace(txt(i - 1))
                        i -= 1
                    End While
                    Dim j As Integer = _word._caretCharIndex
                    While j >= 0 AndAlso j < txt.Length AndAlso Not Char.IsWhiteSpace(txt(j))
                        j += 1
                    End While
                    If j > i AndAlso i >= 0 AndAlso j <= txt.Length Then
                        Dim wordToRead As String = txt.Substring(i, j - i)
                        Dim isLink As Boolean = False
                        Dim isNotaRef As Boolean = False
                        Dim linkTitle As String = ""
                        Dim cPos As Integer = 0
                        For Each r In para.Runs
                            If r IsNot Nothing AndAlso Not r.IsImage Then
                                Dim len As Integer = If(String.IsNullOrEmpty(r.Text), 1, Math.Max(1, r.Text.Length))
                                If i >= cPos AndAlso i < cPos + len Then
                                    If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                                        If _word IsNot Nothing AndAlso _word.LeggiNumeroNotaInScorrimento Then
                                            Dim numero As Integer = _word.OttieniNumeroProgressivoNota(r.LinkUrl)
                                            If numero > 0 Then
                                                isNotaRef = True
                                                linkTitle = numero.ToString()
                                            End If
                                        End If
                                    ElseIf Not String.IsNullOrEmpty(r.LinkUrl) OrElse r.IsHyperlink Then
                                        isLink = True
                                        linkTitle = r.Text
                                    End If
                                    Exit For
                                End If
                                If Not String.IsNullOrEmpty(r.Text) Then cPos += r.Text.Length
                            End If
                        Next
                        If isNotaRef Then
                            lettura.LeggiTesto(wordToRead & " (" & linkTitle & ")")
                        ElseIf isLink Then
                            lettura.LeggiTesto("link " & linkTitle)
                        Else
                            lettura.LeggiTesto(wordToRead)
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub LeggiInfoFormattazione()
        Try
            If _word Is Nothing OrElse _word.IsDisposed Then Return
            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing AndAlso para.Runs IsNot Nothing AndAlso lettura IsNot Nothing Then
                Dim charIndex As Integer = _word._caretCharIndex
                Dim testoIdx As String = para.GetText()
                Dim lunghezzaIdx As Integer = If(String.IsNullOrEmpty(testoIdx), 0, testoIdx.Length)
                If charIndex >= lunghezzaIdx AndAlso lunghezzaIdx > 0 Then
                    charIndex = lunghezzaIdx - 1
                End If
                If charIndex < 0 Then charIndex = 0

                Dim currentPos As Integer = 0
                Dim runTrovato As DocRun = Nothing

                If para.Runs.Count = 0 Then
                    lettura.LeggiTesto("Nessuna formattazione")
                    Return
                End If

                For Each r In para.Runs
                    If r IsNot Nothing Then
                        If r.IsImage Then
                            If charIndex = currentPos Then
                                lettura.LeggiTesto("Oggetto immagine " & r.Text)
                                Return
                            End If
                        Else
                            Dim len As Integer = If(String.IsNullOrEmpty(r.Text), 1, Math.Max(1, r.Text.Length))
                            If charIndex >= currentPos AndAlso charIndex < currentPos + len Then
                                runTrovato = r
                                Exit For
                            End If
                            If Not String.IsNullOrEmpty(r.Text) Then currentPos += r.Text.Length
                        End If
                    End If
                Next

                If runTrovato IsNot Nothing Then
                    If Not String.IsNullOrEmpty(runTrovato.LinkUrl) AndAlso (runTrovato.LinkUrl.StartsWith("NOTA_PIE_") OrElse runTrovato.LinkUrl.StartsWith("NOTA_FINE_")) Then
                        Dim tipoNota As String = If(runTrovato.LinkUrl.StartsWith("NOTA_PIE_"), "nota a piè di pagina", "nota di chiusura")
                        Dim testoNota As String = OttieniTestoNota(runTrovato.LinkUrl)
                        Dim info As String = tipoNota & " " & runTrovato.Text
                        If Not String.IsNullOrEmpty(testoNota) Then info &= ": " & testoNota
                        lettura.LeggiTesto(info)
                    Else
                        Dim info As String = "Tipo carattere " & runTrovato.FontFamily & " dimensione " & runTrovato.FontSize.ToString()
                        If runTrovato.Bold Then info &= " grassetto"
                        If runTrovato.Italic Then info &= " corsivo"
                        If runTrovato.Underline Then info &= " sottolineato"
                        If runTrovato.Strikethrough Then info &= " barrato"
                        If Not String.IsNullOrEmpty(runTrovato.LinkUrl) OrElse runTrovato.IsHyperlink Then info &= " link " & runTrovato.Text
                        info &= " colore " & runTrovato.ColorHex
                        lettura.LeggiTesto(info)
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ControllaFormattazione(para As DocParagraph, charIndex As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse lettura Is Nothing Then Return
            If para Is Nothing OrElse para.Runs Is Nothing OrElse charIndex < 0 Then Return

            Dim annunciaFormattazione As Boolean = _word.AnnunciaFormattazioneAttivo

            Dim currentPos As Integer = 0
            For Each r In para.Runs
                If r IsNot Nothing Then
                    If r.IsImage Then
                        If charIndex = currentPos Then
                            Dim dida As String = If(String.IsNullOrEmpty(r.Text), "Immagine", "Immagine " & r.Text)
                            lettura.LeggiTesto(dida)
                        End If
                    Else
                        Dim len As Integer = If(String.IsNullOrEmpty(r.Text), 1, Math.Max(1, r.Text.Length))
                        If charIndex >= currentPos AndAlso charIndex < currentPos + len Then
                            If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                                If _ultimoLink <> r.LinkUrl Then
                                    _ultimoLink = r.LinkUrl
                                    If _word IsNot Nothing AndAlso _word.LeggiNumeroNotaInScorrimento Then
                                        Dim numero As Integer = _word.OttieniNumeroProgressivoNota(r.LinkUrl)
                                        If numero > 0 Then lettura.LeggiTesto("(" & numero & ")")
                                    End If
                                End If
                                Exit For
                            End If
                            If annunciaFormattazione Then
                                If r.Bold <> _ultimoGrassetto Then
                                    _ultimoGrassetto = r.Bold
                                    If r.Bold Then lettura.LeggiTesto("Grassetto") Else lettura.LeggiTesto("Fine Grassetto")
                                End If
                                If r.Italic <> _ultimoCorsivo Then
                                    _ultimoCorsivo = r.Italic
                                    If r.Italic Then lettura.LeggiTesto("Corsivo") Else lettura.LeggiTesto("Fine Corsivo")
                                End If
                            End If
                            Dim linkSafe As String = If(r.LinkUrl, "")
                            If linkSafe <> _ultimoLink Then
                                _ultimoLink = linkSafe
                                If Not String.IsNullOrEmpty(r.LinkUrl) OrElse r.IsHyperlink Then
                                    lettura.LeggiTesto("link " & r.Text)
                                ElseIf annunciaFormattazione Then
                                    lettura.LeggiTesto("Fine link")
                                End If
                            End If
                            Exit For
                        End If
                        If Not String.IsNullOrEmpty(r.Text) Then currentPos += r.Text.Length
                    End If
                End If
            Next
        Catch ex As Exception
            logger.ScriviLog("ControllaFormattazione Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub NavigatePage(direction As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing Then Return
            Dim viewportH As Integer = _word.ClientRectangle.Height
            If viewportH <= 0 Then viewportH = 500

            Dim currentY As Integer = 0
            SyncLock _word._elementsLock
                Dim maxLoop As Integer = Math.Min(_word._caretIndex - 1, _word._elements.Count - 1)
                For i As Integer = 0 To maxLoop
                    If i >= 0 AndAlso i < _word._elements.Count Then
                        Dim h As Integer = _word._elements(i).CachedHeight
                        If h = -1 Then h = _word._lineHeight
                        currentY += h
                    End If
                Next
            End SyncLock

            Dim targetY As Integer = currentY + (direction * viewportH)
            If targetY < 0 Then targetY = 0

            Dim newIndex As Integer = 0
            Dim accumH As Integer = 0

            SyncLock _word._elementsLock
                For i As Integer = 0 To _word._elements.Count - 1
                    If i >= 0 AndAlso i < _word._elements.Count Then
                        Dim h As Integer = _word._elements(i).CachedHeight
                        If h = -1 Then h = _word._lineHeight
                        If accumH + h > targetY Then
                            newIndex = i
                            Exit For
                        End If
                        accumH += h
                        newIndex = i
                    End If
                Next
            End SyncLock

            If newIndex >= 0 AndAlso newIndex < _word._elements.Count Then
                _word._caretIndex = newIndex
                _word._caretCharIndex = 0

                Dim el As DocElement = _word._elements(_word._caretIndex)
                If TypeOf el Is DocTable Then
                    Dim tbl As DocTable = DirectCast(el, DocTable)
                    If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                        If tbl.SavedCol >= 0 AndAlso tbl.SavedCol < tbl.Rows(0).Cells.Count Then
                            _word._tblCol = tbl.SavedCol
                        Else
                            _word._tblCol = 0
                        End If
                    End If
                    Dim diff As Integer = targetY - accumH
                    Dim approxRow As Integer = diff \ _word._lineHeight
                    If approxRow < 0 Then approxRow = 0
                    If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                        If approxRow >= tbl.Rows.Count Then approxRow = tbl.Rows.Count - 1
                    End If
                    _word._tblRow = approxRow
                    _word._tblPara = 0
                End If
            End If

            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing AndAlso lettura IsNot Nothing Then
                Dim txtLeggibile As String = OttieniTestoLeggibile(para)
                If String.IsNullOrWhiteSpace(txtLeggibile) Then txtLeggibile = "vuoto"
                lettura.LeggiTesto(txtLeggibile, escludiPunteggiatura:=True)
            End If

        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub ResetColonnaDesiderata()
        _colonnaDesiderataVerticale = -1
    End Sub

    Private Function TrovaIndiceRigaPerCarattere(righe As List(Of Tuple(Of Integer, Integer)), caretChar As Integer) As Integer
        Try
            If righe Is Nothing OrElse righe.Count = 0 Then Return -1
            For i As Integer = 0 To righe.Count - 1
                Dim inizio As Integer = righe(i).Item1
                Dim fine As Integer = inizio + righe(i).Item2
                If caretChar >= inizio AndAlso (caretChar < fine OrElse i = righe.Count - 1) Then Return i
            Next
            Return righe.Count - 1
        Catch ex As Exception
            logger.ScriviLog("TrovaIndiceRigaPerCarattere: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function OttieniTestoLeggibileIntervallo(para As DocParagraph, inizio As Integer, fine As Integer) As String
        Try
            If para Is Nothing OrElse para.Runs Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            If inizio <= 0 AndAlso _word IsNot Nothing Then sb.Append(_word.AnnuncioElenco(para, _word._caretIndex))

            Dim offset As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage Then Continue For
                Dim testoRun As String = If(r.Text, "")
                Dim lunghezzaRun As Integer = testoRun.Length
                Dim daR As Integer = Math.Max(inizio, offset)
                Dim aR As Integer = Math.Min(fine, offset + lunghezzaRun)

                If aR > daR Then
                    Dim porzione As String = testoRun.Substring(daR - offset, aR - daR)
                    If Not String.IsNullOrEmpty(r.LinkUrl) AndAlso (r.LinkUrl.StartsWith("NOTA_PIE_") OrElse r.LinkUrl.StartsWith("NOTA_FINE_")) Then
                        If _word IsNot Nothing AndAlso _word.LeggiNumeroNotaInScorrimento Then
                            Dim numero As Integer = _word.OttieniNumeroProgressivoNota(r.LinkUrl)
                            If numero > 0 Then sb.Append(" (").Append(numero).Append(") ")
                        End If
                    ElseIf r.IsHyperlink OrElse Not String.IsNullOrEmpty(r.LinkUrl) Then
                        sb.Append("link ").Append(porzione).Append(" ")
                    Else
                        sb.Append(porzione)
                    End If
                End If

                offset += lunghezzaRun
            Next

            Return sb.ToString().Trim()
        Catch ex As Exception
            logger.ScriviLog("OttieniTestoLeggibileIntervallo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Friend Function OttieniTestoRigaPerCarattere(para As DocParagraph, caretChar As Integer) As String
        Try
            If para Is Nothing Then Return ""
            Dim righe As List(Of Tuple(Of Integer, Integer)) = CalcolaRigheVisualiParagrafo(para)
            If righe.Count <= 1 Then Return OttieniTestoLeggibile(para)

            Dim idx As Integer = TrovaIndiceRigaPerCarattere(righe, caretChar)
            If idx < 0 OrElse idx >= righe.Count Then Return OttieniTestoLeggibile(para)

            Return OttieniTestoLeggibileIntervallo(para, righe(idx).Item1, righe(idx).Item1 + righe(idx).Item2)
        Catch ex As Exception
            logger.ScriviLog("OttieniTestoRigaPerCarattere: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return OttieniTestoLeggibile(para)
        End Try
    End Function

    Public Sub NavigateUp()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim countUp As Integer = 0
            Dim currentElUp As DocElement = Nothing
            SyncLock _word._elementsLock
                countUp = _word._elements.Count
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < countUp Then
                    currentElUp = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If _confermatoLimiteVerticaleDoc AndAlso _direzioneLimiteVerticaleDoc = 1 Then
                _confermatoLimiteVerticaleDoc = False
                If TypeOf currentElUp Is DocParagraph Then
                    Dim paraConfermatoFine As DocParagraph = DirectCast(currentElUp, DocParagraph)
                    Dim txtConfermatoFine As String = OttieniTestoRigaPerCarattere(paraConfermatoFine, _word._caretCharIndex)
                    If lettura IsNot Nothing Then
                        If String.IsNullOrEmpty(txtConfermatoFine) Then
                            lettura.LeggiTesto("riga vuota")
                        Else
                            lettura.LeggiTesto(txtConfermatoFine, escludiPunteggiatura:=True)
                        End If
                    End If
                ElseIf TypeOf currentElUp Is DocTable Then
                    AnnunciaPosizioneCorrenteTabella(DirectCast(currentElUp, DocTable))
                End If
                Return
            End If

            If TypeOf currentElUp Is DocTable AndAlso _word._tblRow > 0 Then
                Dim tblCorrente As DocTable = DirectCast(currentElUp, DocTable)
                _confermatoLimiteVerticaleDoc = False
                _word._tblRow -= 1
                Dim rigaCorrenteUp As DocTableRow = tblCorrente.Rows(_word._tblRow)
                If _word._tblCol < 0 OrElse _word._tblCol >= rigaCorrenteUp.Cells.Count Then _word._tblCol = 0
                _word._tblPara = 0
                _word._caretCharIndex = 0

                Dim msgRigaUp As New StringBuilder()
                If rigaCorrenteUp.Cells IsNot Nothing AndAlso _word._tblCol < rigaCorrenteUp.Cells.Count AndAlso rigaCorrenteUp.Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso rigaCorrenteUp.Cells(_word._tblCol).Paragraphs.Count > 0 Then
                    Dim testoCellaUp As String = OttieniTestoLeggibile(rigaCorrenteUp.Cells(_word._tblCol).Paragraphs(0))
                    If String.IsNullOrWhiteSpace(testoCellaUp) Then testoCellaUp = "vuota"
                    msgRigaUp.Append("Riga ").Append(_word._tblRow + 1).Append(" Colonna ").Append(_word._tblCol + 1).Append(", ").Append(testoCellaUp)
                End If
                lettura.LeggiTesto(msgRigaUp.ToString(), escludiPunteggiatura:=True)
                Return
            End If

            If TypeOf currentElUp Is DocParagraph Then
                Dim paraUpVis As DocParagraph = DirectCast(currentElUp, DocParagraph)
                Dim righeUpVis As List(Of Tuple(Of Integer, Integer)) = CalcolaRigheVisualiParagrafo(paraUpVis)
                If righeUpVis.Count > 1 Then
                    Dim rigaIdxUp As Integer = TrovaIndiceRigaPerCarattere(righeUpVis, _word._caretCharIndex)
                    If rigaIdxUp > 0 Then
                        _confermatoLimiteVerticaleDoc = False
                        If _colonnaDesiderataVerticale < 0 Then _colonnaDesiderataVerticale = _word._caretCharIndex - righeUpVis(rigaIdxUp).Item1
                        Dim rigaDestUp As Tuple(Of Integer, Integer) = righeUpVis(rigaIdxUp - 1)
                        Dim maxColUp As Integer = rigaDestUp.Item2
                        If rigaIdxUp - 1 < righeUpVis.Count - 1 Then maxColUp = Math.Max(0, rigaDestUp.Item2 - 1)
                        _word._caretCharIndex = rigaDestUp.Item1 + Math.Min(Math.Max(0, _colonnaDesiderataVerticale), maxColUp)
                        _word._selectionActive = False
                        _word.AggiornaCursoreDopoSpostamento()
                        Dim testoRigaUp As String = OttieniTestoRigaPerCarattere(paraUpVis, _word._caretCharIndex)
                        lettura.LeggiTesto(If(String.IsNullOrEmpty(testoRigaUp), "riga vuota", testoRigaUp), escludiPunteggiatura:=True)
                        If _word IsNot Nothing Then _word.ControllaAnnuncioCambioPagina()
                        Return
                    End If
                End If
            End If

            If _word._caretIndex > 0 AndAlso _word._caretIndex < countUp Then
                _confermatoLimiteVerticaleDoc = False
                If TypeOf currentElUp Is DocTable Then
                    Dim t As DocTable = DirectCast(currentElUp, DocTable)
                    t.SavedCol = _word._tblCol
                End If

                _word._caretIndex -= 1

                If _word._caretIndex < 0 Then
                    _word._caretIndex = 0
                    _confermatoLimiteVerticaleDoc = True
                    _direzioneLimiteVerticaleDoc = -1
                    System.Media.SystemSounds.Beep.Play()
                    If lettura IsNot Nothing Then lettura.LeggiTesto("inizio documento")
                    Return
                End If

                Dim prevEl As DocElement = Nothing
                SyncLock _word._elementsLock
                    If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                        prevEl = _word._elements(_word._caretIndex)
                    End If
                End SyncLock

                Dim stato = _word.OttieniStatoCursore()

                If TypeOf prevEl Is DocParagraph Then
                    Dim para As DocParagraph = DirectCast(prevEl, DocParagraph)
                    Dim testoNav As String = para.GetText()
                    Dim lunghezzaNav As Integer = If(String.IsNullOrEmpty(testoNav), 0, testoNav.Length)
                    Dim righeEntrataUp As List(Of Tuple(Of Integer, Integer)) = CalcolaRigheVisualiParagrafo(para)
                    If righeEntrataUp.Count > 1 Then
                        Dim ultimaRigaUp As Tuple(Of Integer, Integer) = righeEntrataUp(righeEntrataUp.Count - 1)
                        Dim colDesUp As Integer = If(_colonnaDesiderataVerticale < 0, 0, _colonnaDesiderataVerticale)
                        _word._caretCharIndex = ultimaRigaUp.Item1 + Math.Min(colDesUp, ultimaRigaUp.Item2)
                    Else
                        If _word._caretCharIndex > lunghezzaNav Then _word._caretCharIndex = lunghezzaNav
                        If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
                    End If
                    Dim txtLeggibile As String = OttieniTestoRigaPerCarattere(para, _word._caretCharIndex)
                    If txtLeggibile.Length = 0 Then
                        lettura.LeggiTesto("riga vuota")
                    ElseIf txtLeggibile = " " Then
                        lettura.LeggiTesto("spazio")
                    Else
                        lettura.LeggiTesto(txtLeggibile, escludiPunteggiatura:=True)
                    End If
                ElseIf TypeOf prevEl Is DocTable Then
                    Dim tbl As DocTable = DirectCast(prevEl, DocTable)
                    If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                        _word._tblRow = tbl.Rows.Count - 1
                        If _word._tblRow < 0 Then _word._tblRow = 0

                        If tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso tbl.Rows(_word._tblRow).Cells.Count > 0 Then
                            If tbl.SavedCol >= 0 AndAlso tbl.SavedCol < tbl.Rows(_word._tblRow).Cells.Count Then
                                _word._tblCol = tbl.SavedCol
                            Else
                                _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1
                            End If
                            If _word._tblCol < 0 Then _word._tblCol = 0

                            Dim cell As DocTableCell = tbl.Rows(_word._tblRow).Cells(_word._tblCol)
                            If cell.Paragraphs IsNot Nothing AndAlso cell.Paragraphs.Count > 0 Then
                                _word._tblPara = cell.Paragraphs.Count - 1
                                If _word._tblPara < 0 Then _word._tblPara = 0
                                Dim testoCellaNav As String = cell.Paragraphs(_word._tblPara).GetText()
                                _word._caretCharIndex = If(String.IsNullOrEmpty(testoCellaNav), 0, testoCellaNav.Length)
                                If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0

                                Dim msgOutput As New StringBuilder()
                                msgOutput.Append("Tabella, ")
                                Dim headerText As String = ""
                                If tbl.Rows(0).Cells IsNot Nothing AndAlso tbl.Rows(0).Cells.Count > _word._tblCol AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs.Count > 0 Then
                                    headerText = OttieniTestoLeggibile(tbl.Rows(0).Cells(_word._tblCol).Paragraphs(0))
                                End If
                                Dim cellText As String = OttieniTestoLeggibile(cell.Paragraphs(_word._tblPara))
                                If String.IsNullOrWhiteSpace(cellText) Then cellText = "vuota"
                                msgOutput.Append("Riga " & (_word._tblRow + 1) & " Colonna " & (_word._tblCol + 1))
                                If Not String.IsNullOrEmpty(headerText) Then msgOutput.Append(", " & headerText)
                                msgOutput.Append(". " & cellText)
                                lettura.LeggiTesto(msgOutput.ToString(), escludiPunteggiatura:=True)
                            End If
                        End If
                    End If
                End If
            Else
                _confermatoLimiteVerticaleDoc = True
                _direzioneLimiteVerticaleDoc = -1
                System.Media.SystemSounds.Beep.Play()
                If lettura IsNot Nothing Then lettura.LeggiTesto("inizio documento")
            End If
            If _word IsNot Nothing Then _word.ControllaAnnuncioCambioPagina()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateDown()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim count As Integer = 0
            SyncLock _word._elementsLock
                count = _word._elements.Count
            End SyncLock
            Dim currentEl As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < count Then
                    currentEl = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If _confermatoLimiteVerticaleDoc AndAlso _direzioneLimiteVerticaleDoc = -1 Then
                _confermatoLimiteVerticaleDoc = False
                If TypeOf currentEl Is DocParagraph Then
                    Dim paraConfermatoInizio As DocParagraph = DirectCast(currentEl, DocParagraph)
                    Dim txtConfermatoInizio As String = OttieniTestoRigaPerCarattere(paraConfermatoInizio, _word._caretCharIndex)
                    If lettura IsNot Nothing Then
                        If String.IsNullOrEmpty(txtConfermatoInizio) Then
                            lettura.LeggiTesto("riga vuota")
                        Else
                            lettura.LeggiTesto(txtConfermatoInizio, escludiPunteggiatura:=True)
                        End If
                    End If
                ElseIf TypeOf currentEl Is DocTable Then
                    AnnunciaPosizioneCorrenteTabella(DirectCast(currentEl, DocTable))
                ElseIf lettura IsNot Nothing Then
                    If count <= 0 Then
                        _confermatoLimiteVerticaleDoc = True
                        _direzioneLimiteVerticaleDoc = 1
                        lettura.LeggiTesto("fine documento")
                    Else
                        lettura.LeggiTesto("riga vuota")
                    End If
                End If
                Return
            End If

            If TypeOf currentEl Is DocTable Then
                Dim tblCorrenteDown As DocTable = DirectCast(currentEl, DocTable)
                If tblCorrenteDown.Rows IsNot Nothing AndAlso _word._tblRow < tblCorrenteDown.Rows.Count - 1 Then
                    _confermatoLimiteVerticaleDoc = False
                    _word._tblRow += 1
                    Dim rigaCorrenteDown As DocTableRow = tblCorrenteDown.Rows(_word._tblRow)
                    If _word._tblCol < 0 OrElse _word._tblCol >= rigaCorrenteDown.Cells.Count Then _word._tblCol = 0
                    _word._tblPara = 0
                    _word._caretCharIndex = 0

                    Dim msgRigaDown As New StringBuilder()
                    If rigaCorrenteDown.Cells IsNot Nothing AndAlso _word._tblCol < rigaCorrenteDown.Cells.Count AndAlso rigaCorrenteDown.Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso rigaCorrenteDown.Cells(_word._tblCol).Paragraphs.Count > 0 Then
                        Dim testoCellaDown As String = OttieniTestoLeggibile(rigaCorrenteDown.Cells(_word._tblCol).Paragraphs(0))
                        If String.IsNullOrWhiteSpace(testoCellaDown) Then testoCellaDown = "vuota"
                        msgRigaDown.Append("Riga ").Append(_word._tblRow + 1).Append(" Colonna ").Append(_word._tblCol + 1).Append(", ").Append(testoCellaDown)
                    End If
                    lettura.LeggiTesto(msgRigaDown.ToString(), escludiPunteggiatura:=True)
                    Return
                End If
            End If

            If TypeOf currentEl Is DocParagraph Then
                Dim paraDownVis As DocParagraph = DirectCast(currentEl, DocParagraph)
                Dim righeDownVis As List(Of Tuple(Of Integer, Integer)) = CalcolaRigheVisualiParagrafo(paraDownVis)
                If righeDownVis.Count > 1 Then
                    Dim rigaIdxDown As Integer = TrovaIndiceRigaPerCarattere(righeDownVis, _word._caretCharIndex)
                    If rigaIdxDown >= 0 AndAlso rigaIdxDown < righeDownVis.Count - 1 Then
                        _confermatoLimiteVerticaleDoc = False
                        If _colonnaDesiderataVerticale < 0 Then _colonnaDesiderataVerticale = _word._caretCharIndex - righeDownVis(rigaIdxDown).Item1
                        Dim rigaDestDown As Tuple(Of Integer, Integer) = righeDownVis(rigaIdxDown + 1)
                        Dim maxColDown As Integer = rigaDestDown.Item2
                        If rigaIdxDown + 1 < righeDownVis.Count - 1 Then maxColDown = Math.Max(0, rigaDestDown.Item2 - 1)
                        _word._caretCharIndex = rigaDestDown.Item1 + Math.Min(Math.Max(0, _colonnaDesiderataVerticale), maxColDown)
                        _word._selectionActive = False
                        _word.AggiornaCursoreDopoSpostamento()
                        Dim testoRigaDown As String = OttieniTestoRigaPerCarattere(paraDownVis, _word._caretCharIndex)
                        lettura.LeggiTesto(If(String.IsNullOrEmpty(testoRigaDown), "riga vuota", testoRigaDown), escludiPunteggiatura:=True)
                        If _word IsNot Nothing Then _word.ControllaAnnuncioCambioPagina()
                        Return
                    End If
                End If
            End If

            If _word._caretIndex >= 0 AndAlso _word._caretIndex < count - 1 Then
                _confermatoLimiteVerticaleDoc = False
                If TypeOf currentEl Is DocTable Then
                    Dim t As DocTable = DirectCast(currentEl, DocTable)
                    t.SavedCol = _word._tblCol
                End If

                Dim countTotaleElementi As Integer = 0
                _word._caretIndex += 1
                SyncLock _word._elementsLock
                    countTotaleElementi = _word._elements.Count
                End SyncLock

                If _word._caretIndex >= countTotaleElementi Then
                    _word._caretIndex = countTotaleElementi - 1
                    _confermatoLimiteVerticaleDoc = True
                    _direzioneLimiteVerticaleDoc = 1
                    System.Media.SystemSounds.Beep.Play()
                    If lettura IsNot Nothing Then lettura.LeggiTesto("fine documento")
                    Return
                End If

                Dim nextEl As DocElement = Nothing
                SyncLock _word._elementsLock
                    If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                        nextEl = _word._elements(_word._caretIndex)
                    End If
                End SyncLock
                Dim stato = _word.OttieniStatoCursore()

                If TypeOf nextEl Is DocParagraph Then
                    Dim para As DocParagraph = DirectCast(nextEl, DocParagraph)
                    Dim testoNav As String = para.GetText()
                    Dim lunghezzaNav As Integer = If(String.IsNullOrEmpty(testoNav), 0, testoNav.Length)
                    Dim righeEntrataDown As List(Of Tuple(Of Integer, Integer)) = CalcolaRigheVisualiParagrafo(para)
                    If righeEntrataDown.Count > 1 Then
                        Dim primaRigaDown As Tuple(Of Integer, Integer) = righeEntrataDown(0)
                        Dim colDesDown As Integer = If(_colonnaDesiderataVerticale < 0, 0, _colonnaDesiderataVerticale)
                        _word._caretCharIndex = primaRigaDown.Item1 + Math.Min(colDesDown, Math.Max(0, primaRigaDown.Item2 - 1))
                    Else
                        If _word._caretCharIndex > lunghezzaNav Then _word._caretCharIndex = lunghezzaNav
                        If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
                    End If

                    Dim txtLeggibile As String = OttieniTestoRigaPerCarattere(para, _word._caretCharIndex)
                    If txtLeggibile.Length = 0 Then
                        lettura.LeggiTesto("riga vuota")
                    ElseIf txtLeggibile = " " Then
                        lettura.LeggiTesto("spazio")
                    Else
                        lettura.LeggiTesto(txtLeggibile, escludiPunteggiatura:=True)
                    End If
                ElseIf TypeOf nextEl Is DocTable Then
                    Dim tbl As DocTable = DirectCast(nextEl, DocTable)
                    _word._tblRow = 0
                    If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                        If tbl.SavedCol >= 0 AndAlso tbl.SavedCol < tbl.Rows(0).Cells.Count Then
                            _word._tblCol = tbl.SavedCol
                        Else
                            _word._tblCol = tbl.Rows(0).Cells.Count - 1
                        End If
                        If _word._tblCol < 0 Then _word._tblCol = 0
                    Else
                        _word._tblCol = 0
                    End If
                    _word._tblPara = 0
                    _word._caretCharIndex = 0

                    Dim msgOutput As New StringBuilder()
                    msgOutput.Append("Tabella, ")

                    If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing AndAlso tbl.Rows(0).Cells.Count > _word._tblCol AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs.Count > 0 Then
                        Dim headerText As String = OttieniTestoLeggibile(tbl.Rows(0).Cells(_word._tblCol).Paragraphs(0))
                        Dim cellText As String = OttieniTestoLeggibile(tbl.Rows(0).Cells(_word._tblCol).Paragraphs(0))
                        If String.IsNullOrWhiteSpace(cellText) Then cellText = "vuota"
                        msgOutput.Append("Riga " & (_word._tblRow + 1) & " Colonna " & (_word._tblCol + 1))
                        msgOutput.Append(", " & headerText & ". " & cellText)
                    End If
                    lettura.LeggiTesto(msgOutput.ToString(), escludiPunteggiatura:=True)
                End If
            Else
                _confermatoLimiteVerticaleDoc = True
                _direzioneLimiteVerticaleDoc = 1
                System.Media.SystemSounds.Beep.Play()
                If lettura IsNot Nothing Then lettura.LeggiTesto("fine documento")
            End If
            If _word IsNot Nothing Then _word.ControllaAnnuncioCambioPagina()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateLeft(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse lettura Is Nothing Then Return
            _confermatoLimiteVerticaleDoc = False
            _colonnaDesiderataVerticale = -1

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim pronunciaSegni As Boolean = _word.LetturaPunteggiaturaLettura

            Dim paraClamp As DocParagraph = _word.GetCurrentParagraph()
            If paraClamp IsNot Nothing Then
                Dim testoClamp As String = paraClamp.GetText()
                Dim lunghezzaClamp As Integer = If(String.IsNullOrEmpty(testoClamp), 0, testoClamp.Length)
                If _word._caretCharIndex > lunghezzaClamp Then _word._caretCharIndex = lunghezzaClamp
                If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim isAtStart As Boolean = False
            If _word._caretIndex = 0 AndAlso _word._caretCharIndex = 0 Then
                Dim startEl As DocElement = Nothing
                SyncLock _word._elementsLock
                    If _word._elements IsNot Nothing AndAlso _word._elements.Count > 0 Then startEl = _word._elements(0)
                End SyncLock
                If TypeOf startEl Is DocTable Then
                    If _word._tblRow = 0 AndAlso _word._tblCol = 0 AndAlso _word._tblPara = 0 Then isAtStart = True
                Else
                    isAtStart = True
                End If
            End If

            If isAtStart Then
                lettura.LeggiTesto("inizio documento")
                Return
            End If

            Dim charRead As String = ""
            Dim charSelected As String = ""
            If _word._caretCharIndex > 0 Then
                Dim para As DocParagraph = _word.GetCurrentParagraph()
                _word._caretCharIndex -= 1

                If para IsNot Nothing Then
                    Dim text As String = para.GetText()
                    If _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                        charSelected = text(_word._caretCharIndex).ToString()
                        charRead = charSelected
                        ControllaFormattazione(para, _word._caretCharIndex)
                    End If
                End If
            ElseIf _word._caretIndex > 0 Then
                _word._caretIndex -= 1
                Dim el As DocElement = Nothing
                SyncLock _word._elementsLock
                    If _word._elements IsNot Nothing AndAlso _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                        el = _word._elements(_word._caretIndex)
                    End If
                End SyncLock
                If el IsNot Nothing Then
                    If TypeOf el Is DocParagraph Then
                        Dim para As DocParagraph = DirectCast(el, DocParagraph)
                        Dim testoPrec As String = para.GetText()
                        _word._caretCharIndex = If(String.IsNullOrEmpty(testoPrec), 0, testoPrec.Length)
                        If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
                        If String.IsNullOrEmpty(testoPrec) Then
                            charSelected = "riga vuota"
                            charRead = "riga vuota"
                        Else
                            charSelected = "fine riga"
                            charRead = "fine riga"
                            ControllaFormattazione(para, _word._caretCharIndex - 1)
                        End If
                    ElseIf TypeOf el Is DocTable Then
                        Dim tbl As DocTable = DirectCast(el, DocTable)
                        If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                            _word._tblRow = tbl.Rows.Count - 1
                            Dim ultimaRigaTbl As DocTableRow = tbl.Rows(_word._tblRow)
                            If ultimaRigaTbl.Cells IsNot Nothing AndAlso ultimaRigaTbl.Cells.Count > 0 Then
                                _word._tblCol = ultimaRigaTbl.Cells.Count - 1
                                Dim ultimaCellaTbl As DocTableCell = ultimaRigaTbl.Cells(_word._tblCol)
                                If ultimaCellaTbl.Paragraphs IsNot Nothing AndAlso ultimaCellaTbl.Paragraphs.Count > 0 Then
                                    _word._tblPara = ultimaCellaTbl.Paragraphs.Count - 1
                                    Dim testoUltimaCella As String = ultimaCellaTbl.Paragraphs(_word._tblPara).GetText()
                                    _word._caretCharIndex = If(String.IsNullOrEmpty(testoUltimaCella), 0, testoUltimaCella.Length)
                                Else
                                    _word._tblPara = 0
                                    _word._caretCharIndex = 0
                                End If
                            Else
                                _word._tblCol = 0
                                _word._tblPara = 0
                                _word._caretCharIndex = 0
                            End If
                        Else
                            _word._tblRow = 0
                            _word._tblCol = 0
                            _word._tblPara = 0
                            _word._caretCharIndex = 0
                        End If
                        charSelected = "tabella"
                        charRead = "tabella"
                    End If
                End If
            End If

            If shift Then
                Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                If distNew > distOld Then
                    lettura.LeggiTesto("Selezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                Else
                    lettura.LeggiTesto("Deselezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                End If
            Else
                lettura.LeggiTesto(WGSegni.Converti(charRead, pronunciaSegni))
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateRight(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse lettura Is Nothing Then Return
            _confermatoLimiteVerticaleDoc = False
            _colonnaDesiderataVerticale = -1

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim pronunciaSegni As Boolean = _word.LetturaPunteggiaturaLettura

            Dim paraClampDx As DocParagraph = _word.GetCurrentParagraph()
            If paraClampDx IsNot Nothing Then
                Dim testoClampDx As String = paraClampDx.GetText()
                Dim lunghezzaClampDx As Integer = If(String.IsNullOrEmpty(testoClampDx), 0, testoClampDx.Length)
                If _word._caretCharIndex > lunghezzaClampDx Then _word._caretCharIndex = lunghezzaClampDx
                If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim count As Integer = 0
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing Then count = _word._elements.Count
            End SyncLock

            Dim isAtEnd As Boolean = False
            If _word._caretIndex = count - 1 Then
                Dim el As DocElement = Nothing
                SyncLock _word._elementsLock
                    If _word._elements IsNot Nothing AndAlso count > 0 Then el = _word._elements(_word._caretIndex)
                End SyncLock
                If el IsNot Nothing Then
                    If TypeOf el Is DocParagraph Then
                        Dim testoFine As String = DirectCast(el, DocParagraph).GetText()
                        Dim lunghezzaFine As Integer = If(String.IsNullOrEmpty(testoFine), 0, testoFine.Length)
                        If _word._caretCharIndex >= lunghezzaFine Then
                            isAtEnd = True
                        End If
                    ElseIf TypeOf el Is DocTable Then
                        Dim tbl As DocTable = DirectCast(el, DocTable)
                        If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 Then
                            If _word._tblRow = tbl.Rows.Count - 1 Then
                                If tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso tbl.Rows(_word._tblRow).Cells.Count > 0 Then
                                    If _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1 Then
                                        Dim cell As DocTableCell = tbl.Rows(_word._tblRow).Cells(_word._tblCol)
                                        If cell.Paragraphs IsNot Nothing AndAlso cell.Paragraphs.Count > 0 Then
                                            If _word._tblPara = cell.Paragraphs.Count - 1 Then
                                                Dim testoCellaFine As String = cell.Paragraphs(_word._tblPara).GetText()
                                                Dim lunghezzaCellaFine As Integer = If(String.IsNullOrEmpty(testoCellaFine), 0, testoCellaFine.Length)
                                                If _word._caretCharIndex >= lunghezzaCellaFine Then
                                                    isAtEnd = True
                                                End If
                                            End If
                                        End If
                                    End If
                                End If
                            End If
                        End If
                    End If
                End If
            End If

            If isAtEnd Then
                lettura.LeggiTesto("fine documento")
                Return
            End If

            Dim charRead As String = ""
            Dim charSelected As String = ""
            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim text As String = para.GetText()
                If _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                    ControllaFormattazione(para, _word._caretCharIndex)
                    charSelected = text(_word._caretCharIndex).ToString()
                    _word._caretCharIndex += 1
                    If _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                        charRead = text(_word._caretCharIndex).ToString()
                    Else
                        charRead = "fine riga"
                    End If
                Else
                    Dim syncCount As Integer = 0
                    SyncLock _word._elementsLock
                        If _word._elements IsNot Nothing Then syncCount = _word._elements.Count
                    End SyncLock
                    If _word._caretIndex >= 0 AndAlso _word._caretIndex < syncCount - 1 Then
                        charSelected = "fine riga"
                        _word._caretIndex += 1
                        _word._caretCharIndex = 0
                        Dim el As DocElement = Nothing
                        SyncLock _word._elementsLock
                            If _word._caretIndex >= 0 AndAlso _word._caretIndex < syncCount Then
                                el = _word._elements(_word._caretIndex)
                            End If
                        End SyncLock
                        If el IsNot Nothing Then
                            If TypeOf el Is DocTable Then
                                Dim tbl As DocTable = DirectCast(el, DocTable)
                                _word._tblRow = 0
                                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                                    If _word._tblCol >= tbl.Rows(0).Cells.Count Then _word._tblCol = tbl.Rows(0).Cells.Count - 1
                                Else
                                    _word._tblCol = 0
                                End If
                                If _word._tblCol < 0 Then _word._tblCol = 0
                                _word._tblPara = 0
                                _word._caretCharIndex = 0
                                charRead = "Inizio riga, tabella"
                            ElseIf TypeOf el Is DocParagraph Then
                                Dim nextPara = DirectCast(el, DocParagraph)
                                Dim nextText As String = nextPara.GetText()
                                If Not String.IsNullOrEmpty(nextText) Then
                                    charRead = nextText(0).ToString()
                                Else
                                    charRead = "riga vuota"
                                End If
                            End If
                        End If
                    End If
                End If
            End If

            If shift Then
                Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                If distNew > distOld Then
                    lettura.LeggiTesto("Selezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                Else
                    lettura.LeggiTesto("Deselezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                End If
            Else
                If Not String.IsNullOrEmpty(charRead) Then lettura.LeggiTesto(WGSegni.Converti(charRead, pronunciaSegni))
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateTableWordLeft(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            If _word._caretCharIndex <= 0 Then Return
            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim text As String = para.GetText()
                If String.IsNullOrEmpty(text) Then Return
                Dim i As Integer = _word._caretCharIndex
                If i > text.Length Then i = text.Length
                While i > 0 AndAlso i <= text.Length AndAlso Char.IsWhiteSpace(text(i - 1))
                    i -= 1
                End While
                While i > 0 AndAlso i <= text.Length AndAlso Not Char.IsWhiteSpace(text(i - 1))
                    i -= 1
                End While
                If i >= 0 AndAlso _word._caretCharIndex <= text.Length AndAlso _word._caretCharIndex >= i Then
                    Dim word As String = text.Substring(i, _word._caretCharIndex - i)
                    _word._caretCharIndex = i
                    ControllaFormattazione(para, _word._caretCharIndex)

                    If shift Then
                        Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                        If distNew > distOld Then
                            lettura.LeggiTesto("Selezionato " & word, escludiPunteggiatura:=True)
                        Else
                            lettura.LeggiTesto("Deselezionato " & word, escludiPunteggiatura:=True)
                        End If
                    Else
                        lettura.LeggiTesto(word, escludiPunteggiatura:=True)
                    End If
                    _word.UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateTableWordRight(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim txt As String = para.GetText()
                If String.IsNullOrEmpty(txt) Then Return

                If _word._caretCharIndex >= txt.Length OrElse _word._caretCharIndex < 0 Then
                    GestisciTabInTabella(False)
                    Return
                End If
                ControllaFormattazione(para, _word._caretCharIndex)
                Dim i As Integer = _word._caretCharIndex
                While i >= 0 AndAlso i < txt.Length AndAlso Not Char.IsWhiteSpace(txt(i))
                    i += 1
                End While
                While i >= 0 AndAlso i < txt.Length AndAlso Char.IsWhiteSpace(txt(i))
                    i += 1
                End While
                If i >= 0 AndAlso i <= txt.Length AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex <= i Then
                    Dim word As String = txt.Substring(_word._caretCharIndex, i - _word._caretCharIndex)
                    _word._caretCharIndex = i

                    If shift Then
                        Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                        If distNew > distOld Then
                            lettura.LeggiTesto("Selezionato " & word, escludiPunteggiatura:=True)
                        Else
                            lettura.LeggiTesto("Deselezionato " & word, escludiPunteggiatura:=True)
                        End If
                    Else
                        lettura.LeggiTesto(word, escludiPunteggiatura:=True)
                    End If
                    _word.UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SaltaAParagrafoSuccessivoParola()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            Dim count As Integer = 0
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing Then count = _word._elements.Count
            End SyncLock

            If _word._caretIndex >= count - 1 Then
                lettura.LeggiTesto("fine documento")
                Return
            End If

            _word._caretIndex += 1
            _word._caretCharIndex = 0

            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing AndAlso _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock

            If TypeOf el Is DocTable Then
                Dim tbl As DocTable = DirectCast(el, DocTable)
                _word._tblRow = 0
                _word._tblCol = 0
                _word._tblPara = 0
                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                    If tbl.SavedCol >= 0 AndAlso tbl.SavedCol < tbl.Rows(0).Cells.Count Then _word._tblCol = tbl.SavedCol
                End If
                lettura.LeggiTesto("Riga successiva, tabella")
            ElseIf TypeOf el Is DocParagraph Then
                Dim nextPara As DocParagraph = DirectCast(el, DocParagraph)
                If nextPara.IsPageBreak Then
                    lettura.LeggiTesto("Interruzione di pagina")
                Else
                    Dim testoNext As String = nextPara.GetText()
                    If String.IsNullOrWhiteSpace(testoNext) Then
                        lettura.LeggiTesto("Riga successiva, vuota")
                    Else
                        lettura.LeggiTesto("Riga successiva, " & PrimaParola(testoNext))
                    End If
                End If
            End If

            _word.ScrollToCaret()
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SaltaAParagrafoPrecedenteParola()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If _word._caretIndex <= 0 Then
                lettura.LeggiTesto("inizio documento")
                Return
            End If

            _word._caretIndex -= 1
            _word._caretCharIndex = 0

            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing AndAlso _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock

            If TypeOf el Is DocTable Then
                Dim tbl As DocTable = DirectCast(el, DocTable)
                _word.LoadTableState(tbl)
                lettura.LeggiTesto("Riga precedente, tabella")
            ElseIf TypeOf el Is DocParagraph Then
                Dim prevPara As DocParagraph = DirectCast(el, DocParagraph)
                If prevPara.IsPageBreak Then
                    lettura.LeggiTesto("Interruzione di pagina")
                Else
                    Dim testoPrev As String = prevPara.GetText()
                    If String.IsNullOrWhiteSpace(testoPrev) Then
                        lettura.LeggiTesto("Riga precedente, vuota")
                    Else
                        _word._caretCharIndex = testoPrev.Length
                        lettura.LeggiTesto("Riga precedente, " & UltimaParola(testoPrev))
                    End If
                End If
            End If

            _word.ScrollToCaret()
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Sposta il cursore al paragrafo successivo e ne legge il testo per intero. A differenza di
    ''' SaltaAParagrafoSuccessivoParola, che serve alla navigazione per parola e annuncia solo la
    ''' prima parola del paragrafo raggiunto, qui il paragrafo e' l'unita' di spostamento e va
    ''' quindi letto tutto.
    ''' </summary>
    Public Sub NavigateParagrafoAvanti()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            Dim numeroElementi As Integer = 0
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing Then numeroElementi = _word._elements.Count
            End SyncLock

            If _word._caretIndex >= numeroElementi - 1 Then
                lettura.LeggiTesto("fine documento")
                Return
            End If

            _word._caretIndex += 1
            _word._caretCharIndex = 0

            Dim elemento As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing AndAlso _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    elemento = _word._elements(_word._caretIndex)
                End If
            End SyncLock

            AnnunciaParagrafoRaggiunto(elemento)

            _word.ScrollToCaret()
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog("NavigateParagrafoAvanti: " & ex.Message)
        End Try
    End Sub

    Public Sub NavigateParagrafoIndietro()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If _word._caretIndex <= 0 Then
                lettura.LeggiTesto("inizio documento")
                Return
            End If

            _word._caretIndex -= 1
            _word._caretCharIndex = 0

            Dim elemento As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._elements IsNot Nothing AndAlso _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    elemento = _word._elements(_word._caretIndex)
                End If
            End SyncLock

            AnnunciaParagrafoRaggiunto(elemento)

            _word.ScrollToCaret()
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog("NavigateParagrafoIndietro: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnunciaParagrafoRaggiunto(elemento As DocElement)
        Try
            If lettura Is Nothing Then Return

            If TypeOf elemento Is DocTable Then
                Dim tabella As DocTable = DirectCast(elemento, DocTable)
                _word.LoadTableState(tabella)
                lettura.LeggiTesto("Tabella")
                Return
            End If

            If TypeOf elemento Is DocParagraph Then
                Dim paragrafo As DocParagraph = DirectCast(elemento, DocParagraph)
                If paragrafo.IsPageBreak Then
                    lettura.LeggiTesto("Interruzione di pagina")
                    Return
                End If

                Dim testo As String = paragrafo.GetText()
                If String.IsNullOrWhiteSpace(testo) Then
                    lettura.LeggiTesto("Paragrafo vuoto")
                Else
                    lettura.LeggiTesto(testo)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("AnnunciaParagrafoRaggiunto: " & ex.Message)
        End Try
    End Sub

    Private Function PrimaParola(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim parti As String() = testo.Split(New Char() {" "c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
            If parti.Length > 0 Then Return parti(0)
            Return ""
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Function UltimaParola(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim parti As String() = testo.Split(New Char() {" "c, ChrW(9), ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries)
            If parti.Length > 0 Then Return parti(parti.Length - 1)
            Return ""
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Public Sub NavigateWordLeft(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim text As String = para.GetText()
                If String.IsNullOrEmpty(text) Then
                    If shift Then
                        NavigateLeft(shift)
                    Else
                        SaltaAParagrafoPrecedenteParola()
                    End If
                    Return
                End If
                If _word._caretCharIndex <= 0 Then
                    If shift Then
                        NavigateLeft(shift)
                    Else
                        SaltaAParagrafoPrecedenteParola()
                    End If
                    Return
                End If
                Dim i As Integer = _word._caretCharIndex
                If i > text.Length Then i = text.Length
                While i > 0 AndAlso i <= text.Length AndAlso Char.IsWhiteSpace(text(i - 1))
                    i -= 1
                End While
                While i > 0 AndAlso i <= text.Length AndAlso Not Char.IsWhiteSpace(text(i - 1))
                    i -= 1
                End While
                If i >= 0 AndAlso _word._caretCharIndex <= text.Length AndAlso _word._caretCharIndex >= i Then
                    Dim word As String = text.Substring(i, _word._caretCharIndex - i)
                    _word._caretCharIndex = i
                    ControllaFormattazione(para, _word._caretCharIndex)

                    If shift Then
                        Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                        If distNew > distOld Then
                            lettura.LeggiTesto("Selezionato " & word, escludiPunteggiatura:=True)
                        Else
                            lettura.LeggiTesto("Deselezionato " & word, escludiPunteggiatura:=True)
                        End If
                    Else
                        lettura.LeggiTesto(word, escludiPunteggiatura:=True)
                    End If
                    _word.UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateWordRight(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim para As DocParagraph = _word.GetCurrentParagraph()
            If para IsNot Nothing Then
                Dim txt As String = para.GetText()
                If String.IsNullOrEmpty(txt) Then
                    If shift Then
                        NavigateRight(shift)
                    Else
                        SaltaAParagrafoSuccessivoParola()
                    End If
                    Return
                End If
                If _word._caretCharIndex >= txt.Length OrElse _word._caretCharIndex < 0 Then
                    If shift Then
                        NavigateRight(shift)
                    Else
                        SaltaAParagrafoSuccessivoParola()
                    End If
                    Return
                End If
                ControllaFormattazione(para, _word._caretCharIndex)
                Dim i As Integer = _word._caretCharIndex
                While i >= 0 AndAlso i < txt.Length AndAlso Not Char.IsWhiteSpace(txt(i))
                    i += 1
                End While
                While i >= 0 AndAlso i < txt.Length AndAlso Char.IsWhiteSpace(txt(i))
                    i += 1
                End While
                If i >= 0 AndAlso i <= txt.Length AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex <= i Then
                    Dim word As String = txt.Substring(_word._caretCharIndex, i - _word._caretCharIndex)
                    _word._caretCharIndex = i
                    If shift Then
                        Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                        If distNew > distOld Then
                            lettura.LeggiTesto("Selezionato " & word, escludiPunteggiatura:=True)
                        Else
                            lettura.LeggiTesto("Deselezionato " & word, escludiPunteggiatura:=True)
                        End If
                    Else
                        lettura.LeggiTesto(word, escludiPunteggiatura:=True)
                    End If
                    _word.UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub NavigateTableCharLeft(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim pronunciaSegni As Boolean = _word.LetturaPunteggiaturaLettura

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim charRead As String = ""
            Dim charSelected As String = ""

            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return

            Dim tbl As DocTable = DirectCast(el, DocTable)
            If tbl.Rows Is Nothing OrElse _word._tblRow < 0 OrElse _word._tblRow >= tbl.Rows.Count Then Return
            Dim row As DocTableRow = tbl.Rows(_word._tblRow)
            If row.Cells Is Nothing OrElse _word._tblCol < 0 OrElse _word._tblCol >= row.Cells.Count Then Return
            Dim cell As DocTableCell = row.Cells(_word._tblCol)
            If cell.Paragraphs Is Nothing OrElse _word._tblPara < 0 OrElse _word._tblPara >= cell.Paragraphs.Count Then Return
            Dim para As DocParagraph = cell.Paragraphs(_word._tblPara)

            If _word._caretCharIndex > 0 Then
                _word._caretCharIndex -= 1
                Dim text As String = para.GetText()
                If Not String.IsNullOrEmpty(text) AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                    charSelected = text(_word._caretCharIndex).ToString()
                    charRead = charSelected
                    ControllaFormattazione(para, _word._caretCharIndex)
                End If
                If shift Then
                    Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                    If distNew > distOld Then
                        lettura.LeggiTesto("Selezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                    Else
                        lettura.LeggiTesto("Deselezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                    End If
                Else
                    If Not String.IsNullOrEmpty(charRead) Then lettura.LeggiTesto(WGSegni.Converti(charRead, pronunciaSegni))
                End If
            Else
                If _word._tblPara > 0 Then
                    _word._tblPara -= 1
                    para = cell.Paragraphs(_word._tblPara)
                    Dim testoParaNav As String = para.GetText()
                    _word._caretCharIndex = If(String.IsNullOrEmpty(testoParaNav), 0, testoParaNav.Length)
                    If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
                    charSelected = "fine riga"
                    charRead = "fine riga"
                    If _word._caretCharIndex > 0 Then
                        _word._caretCharIndex -= 1
                        Dim text As String = para.GetText()
                        If Not String.IsNullOrEmpty(text) AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                            charSelected = text(_word._caretCharIndex).ToString()
                            charRead = charSelected
                            ControllaFormattazione(para, _word._caretCharIndex)
                        End If
                    End If
                    If shift Then
                        Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                        If distNew > distOld Then
                            lettura.LeggiTesto("Selezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                        Else
                            lettura.LeggiTesto("Deselezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                        End If
                    Else
                        If Not String.IsNullOrEmpty(charRead) Then lettura.LeggiTesto(WGSegni.Converti(charRead, pronunciaSegni))
                    End If
                Else
                    _word._tblCol -= 1
                    If _word._tblCol < 0 Then
                        _word._tblRow -= 1
                        If _word._tblRow < 0 Then
                            _word._tblRow = 0
                            _word._tblCol = 0

                            If _word._caretIndex <= 0 Then
                                lettura.LeggiTesto("inizio documento")
                                Return
                            End If

                            _word._caretIndex -= 1

                            Dim elPrimaTabella As DocElement = Nothing
                            SyncLock _word._elementsLock
                                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                                    elPrimaTabella = _word._elements(_word._caretIndex)
                                End If
                            End SyncLock

                            If TypeOf elPrimaTabella Is DocParagraph Then
                                Dim paraPrimaTabella As DocParagraph = DirectCast(elPrimaTabella, DocParagraph)
                                Dim testoPrimaTabella As String = paraPrimaTabella.GetText()
                                Dim lunghezzaPrimaTabella As Integer = If(String.IsNullOrEmpty(testoPrimaTabella), 0, testoPrimaTabella.Length)
                                _word._caretCharIndex = lunghezzaPrimaTabella
                                If lunghezzaPrimaTabella > 0 Then
                                    lettura.LeggiTesto(WGSegni.Converti(testoPrimaTabella(lunghezzaPrimaTabella - 1).ToString(), pronunciaSegni))
                                Else
                                    lettura.LeggiTesto("riga vuota")
                                End If
                            ElseIf TypeOf elPrimaTabella Is DocTable Then
                                Dim tblPrecedente As DocTable = DirectCast(elPrimaTabella, DocTable)
                                _word._caretCharIndex = 0
                                If tblPrecedente.Rows IsNot Nothing AndAlso tblPrecedente.Rows.Count > 0 Then
                                    _word._tblRow = tblPrecedente.Rows.Count - 1
                                    Dim ultimaRigaPrec As DocTableRow = tblPrecedente.Rows(_word._tblRow)
                                    If ultimaRigaPrec.Cells IsNot Nothing AndAlso ultimaRigaPrec.Cells.Count > 0 Then
                                        _word._tblCol = ultimaRigaPrec.Cells.Count - 1
                                    Else
                                        _word._tblCol = 0
                                    End If
                                    _word._tblPara = 0
                                End If
                                lettura.LeggiTesto("tabella")
                            End If

                            _word.UpdateCaretAndNotify(False)
                            Return
                        Else
                            If tbl.Rows(_word._tblRow).Cells IsNot Nothing Then
                                _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1
                                If _word._tblCol < 0 Then _word._tblCol = 0
                            End If
                        End If
                    End If
                    If tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso _word._tblCol >= 0 AndAlso _word._tblCol < tbl.Rows(_word._tblRow).Cells.Count Then
                        Dim targetCell = tbl.Rows(_word._tblRow).Cells(_word._tblCol)
                        If targetCell.Paragraphs IsNot Nothing Then
                            _word._tblPara = targetCell.Paragraphs.Count - 1
                            If _word._tblPara < 0 Then _word._tblPara = 0
                            If _word._tblPara < targetCell.Paragraphs.Count Then
                                para = targetCell.Paragraphs(_word._tblPara)
                                Dim testoCellaPrec As String = para.GetText()
                                _word._caretCharIndex = If(String.IsNullOrEmpty(testoCellaPrec), 0, testoCellaPrec.Length)
                                If _word._caretCharIndex < 0 Then _word._caretCharIndex = 0
                            End If
                        End If
                    End If
                    lettura.LeggiTesto("Cella precedente")
                End If
            End If
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub AnnunciaPosizioneCorrenteTabella(tbl As DocTable)
        Try
            If _word Is Nothing OrElse tbl Is Nothing OrElse lettura Is Nothing Then Return
            If tbl.Rows Is Nothing OrElse tbl.Rows.Count = 0 Then Return
            If _word._tblRow < 0 OrElse _word._tblRow >= tbl.Rows.Count Then Return

            Dim riga As DocTableRow = tbl.Rows(_word._tblRow)
            If riga.Cells Is Nothing OrElse _word._tblCol < 0 OrElse _word._tblCol >= riga.Cells.Count Then Return

            Dim cellText As String = ""
            Dim cella As DocTableCell = riga.Cells(_word._tblCol)
            If cella.Paragraphs IsNot Nothing AndAlso cella.Paragraphs.Count > 0 Then
                cellText = OttieniTestoLeggibile(cella.Paragraphs(0))
            End If
            If String.IsNullOrWhiteSpace(cellText) Then cellText = "vuota"

            lettura.LeggiTesto("Tabella, Riga " & (_word._tblRow + 1) & " Colonna " & (_word._tblCol + 1) & ", " & cellText, escludiPunteggiatura:=True)
            _word.UpdateCaretAndNotify(False)
        Catch ex As Exception
            logger.ScriviLog("AnnunciaPosizioneCorrenteTabella Error: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub NavigateTableCharRight(shift As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return

            If Not shift AndAlso _word._selectionActive Then
                _word._selectionActive = False
                _word.Invalidate()
            End If

            Dim pronunciaSegni As Boolean = _word.LetturaPunteggiaturaLettura

            Dim distOld As Long = 0
            If shift Then distOld = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))

            Dim charRead As String = ""
            Dim charSelected As String = ""
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return

            Dim tbl As DocTable = DirectCast(el, DocTable)
            If tbl.Rows Is Nothing OrElse _word._tblRow < 0 OrElse _word._tblRow >= tbl.Rows.Count Then Return
            Dim row As DocTableRow = tbl.Rows(_word._tblRow)
            If row.Cells Is Nothing OrElse _word._tblCol < 0 OrElse _word._tblCol >= row.Cells.Count Then Return
            Dim cell As DocTableCell = row.Cells(_word._tblCol)
            If cell.Paragraphs Is Nothing OrElse _word._tblPara < 0 OrElse _word._tblPara >= cell.Paragraphs.Count Then Return
            Dim para As DocParagraph = cell.Paragraphs(_word._tblPara)

            Dim text As String = para.GetText()
            If Not String.IsNullOrEmpty(text) AndAlso _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                ControllaFormattazione(para, _word._caretCharIndex)
                charSelected = text(_word._caretCharIndex).ToString()
                _word._caretCharIndex += 1
                If _word._caretCharIndex >= 0 AndAlso _word._caretCharIndex < text.Length Then
                    charRead = text(_word._caretCharIndex).ToString()
                Else
                    charRead = "fine riga"
                End If
                If shift Then
                    Dim distNew As Long = Math.Abs(_word.PosizioneAssoluta(_word._caretIndex, _word.GetPackedTableIndex(), _word._caretCharIndex) - _word.PosizioneAssoluta(_word._selAnchorIndex, _word._selAnchorSubIndex, _word._selAnchorCharIndex))
                    If distNew > distOld Then
                        lettura.LeggiTesto("Selezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                    Else
                        lettura.LeggiTesto("Deselezionato " & WGSegni.Converti(charSelected, pronunciaSegni))
                    End If
                Else
                    If Not String.IsNullOrEmpty(charRead) Then lettura.LeggiTesto(WGSegni.Converti(charRead, pronunciaSegni))
                End If
                _word.UpdateCaretAndNotify(False)
            Else
                If _word._tblPara < cell.Paragraphs.Count - 1 Then
                    _word._tblPara += 1
                    _word._caretCharIndex = 0
                    _word.UpdateCaretAndNotify(False)
                Else
                    ' Calcola la prossima cella SENZA ancora modificare lo stato reale: se si
                    ' scopre che siamo all'ultima cella dell'ultima riga e la tabella e' anche
                    ' l'ultimo elemento del documento, questa e' la vera fine del documento e lo
                    ' stato (riga, colonna, paragrafo, carattere) resta esattamente com'era. Così,
                    ' premendo ancora freccia destra, si ottiene sempre e solo il segnale acustico
                    ' e "fine documento", senza ripetere "Cella successiva" né spostare il cursore.
                    Dim colProssima As Integer = _word._tblCol + 1
                    Dim rigaProssima As Integer = _word._tblRow
                    Dim cambiaRiga As Boolean = False
                    If colProssima >= row.Cells.Count Then
                        colProssima = 0
                        rigaProssima += 1
                        cambiaRiga = True
                    End If

                    If cambiaRiga AndAlso rigaProssima >= tbl.Rows.Count Then
                        Dim countUscita As Integer = 0
                        SyncLock _word._elementsLock
                            countUscita = _word._elements.Count
                        End SyncLock

                        If _word._caretIndex + 1 >= countUscita Then
                            System.Media.SystemSounds.Beep.Play()
                            lettura.LeggiTesto("fine documento")
                            Return
                        End If

                        ' C'e' dell'altro dopo la tabella: si esce davvero dalla tabella e si
                        ' passa all'elemento successivo del documento.
                        _word._tblCol = 0
                        _word._tblRow = tbl.Rows.Count - 1
                        If _word._tblRow < 0 Then _word._tblRow = 0
                        _word._caretIndex += 1
                        _word._caretCharIndex = 0

                        Dim elDopoTabella As DocElement = Nothing
                        SyncLock _word._elementsLock
                            If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                                elDopoTabella = _word._elements(_word._caretIndex)
                            End If
                        End SyncLock

                        If TypeOf elDopoTabella Is DocParagraph Then
                            Dim paraDopoTabella As DocParagraph = DirectCast(elDopoTabella, DocParagraph)
                            Dim testoDopoTabella As String = paraDopoTabella.GetText()
                            lettura.LeggiTesto(If(String.IsNullOrEmpty(testoDopoTabella), "riga vuota", WGSegni.Converti(testoDopoTabella(0).ToString(), pronunciaSegni)))
                        ElseIf TypeOf elDopoTabella Is DocTable Then
                            _word._tblRow = 0
                            _word._tblCol = 0
                            _word._tblPara = 0
                            lettura.LeggiTesto("tabella")
                        End If

                        _word.UpdateCaretAndNotify(False)
                        Return
                    End If

                    _word._tblCol = colProssima
                    _word._tblRow = rigaProssima
                    _word._tblPara = 0
                    _word._caretCharIndex = 0
                    lettura.LeggiTesto("Cella successiva")
                    _word.UpdateCaretAndNotify(False)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub EstendiSelezioneRigaTabella(direzione As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return
            Dim tbl As DocTable = DirectCast(el, DocTable)
            If tbl.Rows Is Nothing OrElse tbl.Rows.Count = 0 Then Return

            If Not _word._tblSelAttiva OrElse _word._tblSelModalita <> "RIGHE" Then
                _word._tblSelAttiva = True
                _word._tblSelModalita = "RIGHE"
                _word._tblSelAncora = _word._tblRow
            End If

            Dim nuovaRiga As Integer = _word._tblRow + direzione
            If nuovaRiga < 0 Then nuovaRiga = 0
            If nuovaRiga > tbl.Rows.Count - 1 Then nuovaRiga = tbl.Rows.Count - 1
            _word._tblRow = nuovaRiga
            _word._tblPara = 0
            _word._caretCharIndex = 0
            _word.UpdateCaretAndNotify(False)

            Dim inizio As Integer = System.Math.Min(_word._tblSelAncora, _word._tblRow)
            Dim fine As Integer = System.Math.Max(_word._tblSelAncora, _word._tblRow)
            Dim numRighe As Integer = fine - inizio + 1
            If numRighe = 1 Then
                lettura.LeggiTesto("Riga " & (inizio + 1) & " selezionata")
            Else
                lettura.LeggiTesto(numRighe & " righe selezionate, dalla " & (inizio + 1) & " alla " & (fine + 1))
            End If
        Catch ex As Exception
            logger.ScriviLog("EstendiSelezioneRigaTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub EstendiSelezioneColonnaTabella(direzione As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return
            Dim tbl As DocTable = DirectCast(el, DocTable)
            If tbl.Rows Is Nothing OrElse tbl.Rows.Count = 0 OrElse tbl.Rows(0).Cells Is Nothing OrElse tbl.Rows(0).Cells.Count = 0 Then Return

            If Not _word._tblSelAttiva OrElse _word._tblSelModalita <> "COLONNE" Then
                _word._tblSelAttiva = True
                _word._tblSelModalita = "COLONNE"
                _word._tblSelAncora = _word._tblCol
            End If

            Dim numeroColonne As Integer = tbl.Rows(0).Cells.Count
            Dim nuovaColonna As Integer = _word._tblCol + direzione
            If nuovaColonna < 0 Then nuovaColonna = 0
            If nuovaColonna > numeroColonne - 1 Then nuovaColonna = numeroColonne - 1
            _word._tblCol = nuovaColonna
            _word._tblPara = 0
            _word._caretCharIndex = 0
            _word.UpdateCaretAndNotify(False)

            Dim inizio As Integer = System.Math.Min(_word._tblSelAncora, _word._tblCol)
            Dim fine As Integer = System.Math.Max(_word._tblSelAncora, _word._tblCol)
            Dim numColonne As Integer = fine - inizio + 1
            If numColonne = 1 Then
                lettura.LeggiTesto("Colonna " & (inizio + 1) & " selezionata")
            Else
                lettura.LeggiTesto(numColonne & " colonne selezionate, dalla " & (inizio + 1) & " alla " & (fine + 1))
            End If
        Catch ex As Exception
            logger.ScriviLog("EstendiSelezioneColonnaTabella: " & ex.Message)
        End Try
    End Sub

    Public Sub NavigateTableVertical(direction As Integer)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return

            If _confermatoLimiteVerticaleDoc AndAlso _direzioneLimiteVerticaleDoc = -1 AndAlso direction > 0 Then
                NavigateDown()
                Return
            End If
            If _confermatoLimiteVerticaleDoc AndAlso _direzioneLimiteVerticaleDoc = 1 AndAlso direction < 0 Then
                NavigateUp()
                Return
            End If

            Dim tbl As DocTable = DirectCast(el, DocTable)
            tbl.SavedCol = _word._tblCol
            Dim nextRow As Integer = _word._tblRow + direction
            If tbl.Rows IsNot Nothing AndAlso nextRow >= 0 AndAlso nextRow < tbl.Rows.Count Then
                _word._tblRow = nextRow
                If tbl.Rows(_word._tblRow).Cells IsNot Nothing Then
                    If _word._tblCol >= tbl.Rows(_word._tblRow).Cells.Count Then
                        _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1
                    End If
                    If _word._tblCol < 0 Then _word._tblCol = 0
                Else
                    _word._tblCol = 0
                End If
                _word._tblPara = 0
                _word._caretCharIndex = 0

                Dim headerText As String = ""
                If tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing AndAlso tbl.Rows(0).Cells.Count > _word._tblCol Then
                    If tbl.Rows(0).Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs.Count > 0 Then
                        headerText = OttieniTestoLeggibile(tbl.Rows(0).Cells(_word._tblCol).Paragraphs(0))
                    End If
                End If

                If tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso _word._tblCol >= 0 AndAlso _word._tblCol < tbl.Rows(_word._tblRow).Cells.Count Then
                    Dim cell As DocTableCell = tbl.Rows(_word._tblRow).Cells(_word._tblCol)
                    If cell.Paragraphs IsNot Nothing AndAlso cell.Paragraphs.Count > 0 Then
                        Dim cellText As String = OttieniTestoLeggibile(cell.Paragraphs(0))
                        If String.IsNullOrWhiteSpace(cellText) Then cellText = "vuota"
                        lettura.LeggiTesto("Riga " & (_word._tblRow + 1) & " Colonna " & (_word._tblCol + 1) & ", " & headerText & ". " & cellText, escludiPunteggiatura:=True)
                    End If
                End If
                _word.UpdateCaretAndNotify(False)
            Else
                If direction > 0 Then NavigateDown() Else NavigateUp()
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    ' Chiamato dall'Invio quando ci si trova dentro una tabella (WGWord.GestisciKeyDown). Se non
    ' si e' sull'ultima cella dell'ultima riga, l'Invio si comporta come il Tab (nessuna novità).
    ' Se invece ci si e' gia' sull'ultima cella dell'ultima riga, il primo Invio avvisa soltanto,
    ' e solo il secondo Invio consecutivo crea una nuova riga (un nuovo paragrafo) subito sotto
    ' la tabella, uscendo dalla tabella stessa.
    Public Sub GestisciInvioTabella()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return

            Dim tbl As DocTable = DirectCast(el, DocTable)
            Dim inUltimaRiga As Boolean = tbl.Rows IsNot Nothing AndAlso _word._tblRow = tbl.Rows.Count - 1
            Dim inUltimaCella As Boolean = inUltimaRiga AndAlso tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1

            If Not inUltimaCella Then
                _invioConsecutiviUltimaRigaTabella = 0
                _word.MostraMessaggioStato("Nuova riga")
                GestisciTabInTabella(False)
                Return
            End If

            _invioConsecutiviUltimaRigaTabella += 1
            If _invioConsecutiviUltimaRigaTabella < 2 Then
                _word.MostraMessaggioStato("Premi ancora invio per creare una nuova riga sotto la tabella")
                lettura.LeggiTesto("Premi ancora invio per uscire dalla tabella")
                Return
            End If

            _invioConsecutiviUltimaRigaTabella = 0
            CreaRigaSottoLaTabella()
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    ' Inserisce un nuovo paragrafo vuoto subito dopo la tabella (non una nuova riga DENTRO la
    ' tabella) e vi sposta il cursore, uscendo cosi' dalla tabella.
    Private Sub CreaRigaSottoLaTabella()
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing Then Return
            _word.SaveUndoState()

            Dim indiceTabella As Integer = _word._caretIndex
            Dim nuovoParagrafo As New DocParagraph()

            SyncLock _word._elementsLock
                If indiceTabella < 0 OrElse indiceTabella >= _word._elements.Count Then Return
                _word._elements.Insert(indiceTabella + 1, nuovoParagrafo)
            End SyncLock

            _word._caretIndex = indiceTabella + 1
            _word._caretCharIndex = 0
            _word._tblRow = 0
            _word._tblCol = 0
            _word._tblPara = 0

            _word.UpdateCaretAndNotify(True)
            _word.MostraMessaggioStato("Nuova riga sotto la tabella")
            If lettura IsNot Nothing Then lettura.LeggiTesto("riga vuota")
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub GestisciTabInTabella(shiftPremuto As Boolean)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word._elements Is Nothing OrElse lettura Is Nothing Then Return
            Dim el As DocElement = Nothing
            SyncLock _word._elementsLock
                If _word._caretIndex >= 0 AndAlso _word._caretIndex < _word._elements.Count Then
                    el = _word._elements(_word._caretIndex)
                End If
            End SyncLock
            If el Is Nothing OrElse Not TypeOf el Is DocTable Then Return

            Dim tbl As DocTable = DirectCast(el, DocTable)
            Dim changed As Boolean = False
            Dim rigaAggiunta As Boolean = False

            If shiftPremuto Then
                _word._tblCol -= 1
                If _word._tblCol < 0 Then
                    _word._tblRow -= 1
                    If _word._tblRow < 0 Then
                        _word._tblRow = 0
                        _word._tblCol = 0
                        NavigateUp()
                        Return
                    Else
                        If tbl.Rows IsNot Nothing AndAlso _word._tblRow >= 0 AndAlso _word._tblRow < tbl.Rows.Count AndAlso tbl.Rows(_word._tblRow).Cells IsNot Nothing Then
                            _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1
                        End If
                        If _word._tblCol < 0 Then _word._tblCol = 0
                    End If
                End If
                changed = True
            Else
                _word._tblCol += 1
                If tbl.Rows IsNot Nothing AndAlso _word._tblRow >= 0 AndAlso _word._tblRow < tbl.Rows.Count AndAlso tbl.Rows(_word._tblRow).Cells IsNot Nothing Then
                    If _word._tblCol >= tbl.Rows(_word._tblRow).Cells.Count Then
                        _word._tblCol = 0
                        _word._tblRow += 1
                    End If
                End If
                If tbl.Rows IsNot Nothing AndAlso _word._tblRow >= tbl.Rows.Count Then
                    If _word.SolaLettura Then
                        _word._tblRow = tbl.Rows.Count - 1
                        If _word._tblRow < 0 Then _word._tblRow = 0
                        If tbl.Rows.Count > 0 AndAlso tbl.Rows(_word._tblRow).Cells IsNot Nothing Then
                            _word._tblCol = tbl.Rows(_word._tblRow).Cells.Count - 1
                            If _word._tblCol < 0 Then _word._tblCol = 0
                        End If
                        NavigateDown()
                        Return
                    Else
                        SyncLock _word._elementsLock
                            Dim newRow As New DocTableRow()
                            Dim colCount As Integer = 1
                            If tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing Then
                                colCount = tbl.Rows(0).Cells.Count
                            End If
                            For i As Integer = 0 To colCount - 1
                                Dim newCell As New DocTableCell()
                                newCell.Paragraphs.Add(New DocParagraph())
                                newRow.Cells.Add(newCell)
                            Next
                            tbl.Rows.Add(newRow)
                            _word._tblRow = tbl.Rows.Count - 1
                            If _word._tblRow < 0 Then _word._tblRow = 0
                            _word._tblCol = 0
                            tbl.CachedHeight = -1
                            rigaAggiunta = True
                        End SyncLock
                    End If
                End If
                changed = True
            End If

            If changed Then
                _word._tblPara = 0
                _word._caretCharIndex = 0
                Dim headerText As String = ""
                If tbl.Rows IsNot Nothing AndAlso tbl.Rows.Count > 0 AndAlso tbl.Rows(0).Cells IsNot Nothing AndAlso tbl.Rows(0).Cells.Count > _word._tblCol AndAlso _word._tblCol >= 0 Then
                    If tbl.Rows(0).Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso tbl.Rows(0).Cells(_word._tblCol).Paragraphs.Count > 0 Then
                        headerText = OttieniTestoLeggibile(tbl.Rows(0).Cells(_word._tblCol).Paragraphs(0))
                    End If
                End If
                Dim cellText As String = ""
                If tbl.Rows IsNot Nothing AndAlso _word._tblRow >= 0 AndAlso _word._tblRow < tbl.Rows.Count AndAlso tbl.Rows(_word._tblRow).Cells IsNot Nothing AndAlso _word._tblCol >= 0 AndAlso _word._tblCol < tbl.Rows(_word._tblRow).Cells.Count Then
                    If tbl.Rows(_word._tblRow).Cells(_word._tblCol).Paragraphs IsNot Nothing AndAlso tbl.Rows(_word._tblRow).Cells(_word._tblCol).Paragraphs.Count > 0 Then
                        cellText = OttieniTestoLeggibile(tbl.Rows(_word._tblRow).Cells(_word._tblCol).Paragraphs(0))
                    End If
                End If

                If String.IsNullOrWhiteSpace(cellText) Then cellText = "vuota"

                lettura.LeggiTesto("Riga " & (_word._tblRow + 1) & " Colonna " & (_word._tblCol + 1) & ", " & headerText & ". " & cellText, escludiPunteggiatura:=True)
                _word.UpdateCaretAndNotify(rigaAggiunta)
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public Sub GestisciEcoDigitazione(c As Char, echoSetting As String)
        Try
            If _word Is Nothing OrElse _word.IsDisposed OrElse _word.config Is Nothing OrElse _word.SolaLettura OrElse lettura Is Nothing Then Return
            If echoSetting = "0" Then Return
            If echoSetting <> "1" AndAlso echoSetting <> "2" AndAlso echoSetting <> "3" Then echoSetting = "3"

            Dim pronunciaSegni As Boolean = True

            Dim isFineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))
            Dim wordDaLeggere As String = ""
            Dim spazioDopoSpazio As Boolean = False
            Dim simboloPrecedente As String = ""

            If c = " "c Then
                Dim paraSpc As DocParagraph = _word.GetCurrentParagraph()
                If paraSpc IsNot Nothing Then
                    Dim txtSpc As String = paraSpc.GetText()
                    Dim prevIdx As Integer = _word._caretCharIndex - 2
                    If prevIdx < 0 Then
                        spazioDopoSpazio = True
                    ElseIf prevIdx < txtSpc.Length AndAlso Char.IsWhiteSpace(txtSpc(prevIdx)) Then
                        spazioDopoSpazio = True
                    ElseIf prevIdx >= 0 AndAlso prevIdx < txtSpc.Length AndAlso Char.IsPunctuation(txtSpc(prevIdx)) Then
                        simboloPrecedente = txtSpc(prevIdx).ToString()
                    End If
                Else
                    spazioDopoSpazio = True
                End If
            End If

            If (echoSetting = "2" Or echoSetting = "3") AndAlso isFineParola AndAlso Not spazioDopoSpazio Then
                Dim para As DocParagraph = _word.GetCurrentParagraph()
                If para IsNot Nothing Then
                    Dim txt As String = para.GetText()
                    If Not String.IsNullOrEmpty(txt) AndAlso _word._caretCharIndex > 1 AndAlso _word._caretCharIndex <= txt.Length + 1 Then
                        Dim i As Integer = _word._caretCharIndex - 2
                        If i >= txt.Length Then i = txt.Length - 1
                        While i >= 0 AndAlso i < txt.Length AndAlso Char.IsWhiteSpace(txt(i))
                            i -= 1
                        End While
                        ' Se il carattere appena prima del cursore è un simbolo di punteggiatura
                        ' (es. virgola prima dello spazio), lo saltiamo per poter comunque
                        ' recuperare la parola che lo precede.
                        If i >= 0 AndAlso i < txt.Length AndAlso Char.IsPunctuation(txt(i)) Then
                            i -= 1
                        End If
                        Dim endWord As Integer = i + 1
                        While i >= 0 AndAlso i < txt.Length AndAlso Not Char.IsWhiteSpace(txt(i)) AndAlso Not Char.IsPunctuation(txt(i))
                            i -= 1
                        End While
                        Dim startWord As Integer = i + 1
                        If endWord > startWord AndAlso startWord >= 0 AndAlso endWord <= txt.Length Then
                            wordDaLeggere = txt.Substring(startWord, endWord - startWord)
                        End If
                    End If
                End If
            End If

            If c = " "c Then
                Dim nomeSimboloPrecedente As String = If(simboloPrecedente.Length > 0, WGSegni.Converti(simboloPrecedente, True), "")
                Dim testoSpazio As String = If(nomeSimboloPrecedente.Length > 0, nomeSimboloPrecedente & " spazio", ". spazio")
                Dim escludiSpazio As Boolean = (nomeSimboloPrecedente.Length = 0)

                If echoSetting = "1" Then
                    lettura.LeggiTesto(testoSpazio, escludiPunteggiatura:=escludiSpazio)
                ElseIf echoSetting = "2" Then
                    If wordDaLeggere.Length > 0 Then
                        If nomeSimboloPrecedente.Length > 0 Then
                            lettura.LeggiTesto(wordDaLeggere & " " & nomeSimboloPrecedente)
                        Else
                            lettura.LeggiTesto(wordDaLeggere)
                        End If
                    Else
                        lettura.LeggiTesto(testoSpazio, escludiPunteggiatura:=escludiSpazio)
                    End If
                Else
                    If wordDaLeggere.Length > 0 Then
                        lettura.LeggiTesto(wordDaLeggere & " " & testoSpazio, escludiPunteggiatura:=escludiSpazio)
                    Else
                        lettura.LeggiTesto(testoSpazio, escludiPunteggiatura:=escludiSpazio)
                    End If
                End If
                Return
            End If

            If echoSetting = "1" Then
                Dim tradotto As String = WGSegni.Converti(c.ToString(), pronunciaSegni)
                If Not String.IsNullOrWhiteSpace(tradotto) Then lettura.LeggiTesto(tradotto)
            ElseIf echoSetting = "2" Then
                If isFineParola AndAlso wordDaLeggere.Length > 0 Then
                    If Char.IsPunctuation(c) Then
                        If pronunciaSegni Then
                            lettura.LeggiTesto(wordDaLeggere & " " & WGSegni.Converti(c.ToString(), True))
                        Else
                            lettura.LeggiTesto(wordDaLeggere & c.ToString())
                        End If
                    Else
                        lettura.LeggiTesto(wordDaLeggere)
                    End If
                End If
            ElseIf echoSetting = "3" Then
                Dim nomeSimbolo As String = WGSegni.Converti(c.ToString(), pronunciaSegni)
                If c = "@"c Then nomeSimbolo = "chiocciola"
                If c = "#"c Then nomeSimbolo = "cancelletto"
                If Char.IsLetter(c) AndAlso Char.IsUpper(c) Then nomeSimbolo = c.ToString() & " maiuscola"

                Dim isSimbolo As Boolean = WGTastiera.SimboloDaLeggereSubito(c)
                Dim isFineParolaPerSimboli As Boolean = (isFineParola OrElse isSimbolo)

                If isFineParolaPerSimboli AndAlso wordDaLeggere.Length > 1 Then
                    If isSimbolo Then
                        If pronunciaSegni Then
                            lettura.LeggiTesto(wordDaLeggere & " " & nomeSimbolo)
                        Else
                            lettura.LeggiTesto(wordDaLeggere)
                        End If
                    Else
                        If pronunciaSegni Then
                            lettura.LeggiTesto(wordDaLeggere & " " & WGSegni.Converti(c.ToString(), True))
                        Else
                            lettura.LeggiTesto(wordDaLeggere & c.ToString())
                        End If
                    End If
                Else
                    If isSimbolo AndAlso _word.LivelloPunteggiaturaScrittura < WGImpostazioniComuni.LivelloSimboloScrittura(c) Then Return
                    If Not String.IsNullOrWhiteSpace(nomeSimbolo) Then lettura.LeggiTesto(nomeSimbolo)
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub
End Class

Friend Class WGSegni
    Private Shared _gestoreEmogi As New WGEmogi()

    Public Shared Function Converti(testo As String, Optional pronunciaSegni As Boolean = True) As String
        If String.IsNullOrEmpty(testo) Then Return "vuoto"
        If testo = " " Then Return "spazio"

        Dim descEmogi As String = _gestoreEmogi.OttieniDescrizione(testo)
        If descEmogi <> "Emogi sconosciuta" Then
            Return descEmogi
        End If

        If testo.Length = 1 Then
            If Char.IsLetter(testo(0)) AndAlso Char.IsUpper(testo(0)) Then Return testo & " maiuscola"
            ' Il nome del simbolo copre tutte le tastiere scelte in Impostazioni, non solo l'italiana.
            Dim nome As String = WGTastiera.NomeSimbolo(testo(0))
            If Not String.Equals(nome, testo, StringComparison.Ordinal) Then Return nome.ToLowerInvariant()
        End If
        Return testo
    End Function
End Class